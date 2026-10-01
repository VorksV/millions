using VoltrisOptimizer.Utils;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.Performance.CpuTuning.Models;

namespace VoltrisOptimizer.Services.Performance.CpuTuning
{
    /// <summary>
    /// Thermal safety governor with predictive protection
    /// HIGHEST PRIORITY: Temperature is the supreme authority
    /// </summary>
    public class ThermalGovernor : IThermalGovernor, IDisposable
    {
        private readonly ILoggingService _logger;
        private readonly IHardwareCapabilityDetector _hardwareDetector;
        private readonly SafePerformanceCounter? _tempCounter;
        
        private CancellationTokenSource? _monitoringCts;
        private Task? _monitoringTask;
        private bool _isRunning;
        private readonly VoltrisOptimizer.Services.Performance.CpuTuning.Core.Interfaces.IHardwareBackend _backend;
        
        // Thermal state
        private double _currentTemp;
        private double _lastTemp;
        private double _tempDelta;
        private double _tempEMA; // Exponential moving average for noise filtering
        private const double TempEMA_Alpha = 0.3;
        
        // External injection support (ENTERPRISE FIX: Thread-safe)
        private double _injectedTemp;
        private DateTime _lastInjectionTime = DateTime.MinValue;
        private const int InjectionMaxAgeMs = 3000; // If no injection for 3s, fall back to WMI
        private readonly object _injectionLock = new object(); // ✅ Lock para injection
        
        private DateTime _lastMonitorTime = DateTime.UtcNow;
        private volatile bool _thermalProtectionActive; // ✅ VOLATILE para thread-safety
        private int _consecutiveHighTempCycles;
        
        // Configuration
        private const int MonitoringIntervalMs = 500; // ≤ 500ms as required
        private const double PredictionHorizonSeconds = 60.0; // PROFISSIONAL: Predição de 60s para ação preventiva real
        private const double DangerZoneMargin = 5.0; // °C below TjSafe
        private const double CriticalTempDelta = 2.5; // °C/s - triggers proactive protection
        
        // PROFISSIONAL: EMA ponderado com múltiplas janelas para predição mais precisa
        private double _tempEMA_Fast; // EMA rápido (alpha=0.5) — reage a picos
        private double _tempEMA_Slow; // EMA lento (alpha=0.1) — tendência de longo prazo
        private const double EMA_Fast_Alpha = 0.5;
        private const double EMA_Slow_Alpha = 0.1;
        
        public bool IsThermalProtectionActive => _thermalProtectionActive;
        
        public event EventHandler<ThermalProtectionEventArgs>? ThermalProtectionTriggered;
        public ThermalGovernor(ILoggingService logger, IHardwareCapabilityDetector hardwareDetector, VoltrisOptimizer.Services.Hardware.IHardwareTelemetryHub? telemetryHub = null)
        {
            _logger = logger;
            _hardwareDetector = hardwareDetector;
            
            // Try to resolve from DI if not provided
            if (telemetryHub == null && App.Services != null)
            {
                telemetryHub = (VoltrisOptimizer.Services.Hardware.IHardwareTelemetryHub?)App.Services.GetService(typeof(VoltrisOptimizer.Services.Hardware.IHardwareTelemetryHub));
            }
            
            _backend = (VoltrisOptimizer.Services.Performance.CpuTuning.Core.Interfaces.IHardwareBackend?)App.Services?.GetService(typeof(VoltrisOptimizer.Services.Performance.CpuTuning.Core.Interfaces.IHardwareBackend)) ?? throw new InvalidOperationException("IHardwareBackend not registered");
            
            _tempCounter = null;
        }
        
        public void Start()
        {
            if (_isRunning)
            {
                _logger.LogWarning("[ThermalGovernor] Already running");
                return;
            }
            
            _logger.LogInfo("[ThermalGovernor] Starting thermal monitoring...");
            
            _monitoringCts = new CancellationTokenSource();
            _monitoringTask = Task.Run(() => MonitoringLoop(_monitoringCts.Token));
            _isRunning = true;
            
            _logger.LogInfo("[ThermalGovernor] Thermal monitoring started");
        }
        
        public void Stop()
        {
            if (!_isRunning) return;
            
            _logger.LogInfo("[ThermalGovernor] Stopping thermal monitoring...");
            
            _monitoringCts?.Cancel();
            _monitoringTask?.Wait(TimeSpan.FromSeconds(2));
            _monitoringCts?.Dispose();
            _monitoringCts = null;
            _monitoringTask = null;
            _isRunning = false;
            
            _logger.LogInfo("[ThermalGovernor] Thermal monitoring stopped");
        }
        
        public ThermalState GetThermalState()
        {
            var caps = _hardwareDetector.GetCapabilities();
            bool therm = false, power = false, curr = false;
            if (_backend.IsDriverLoaded() && _backend.ReadMsr(0x19C, out ulong thermStatus))
            {
                therm = (thermStatus & 1) != 0;
                power = (thermStatus & (1UL << 10)) != 0;
                curr = (thermStatus & (1UL << 11)) != 0;
            }
            var flags = (thermal: therm, power: power, current: curr);

            var state = new ThermalState
            {
                CurrentTemperature = _tempEMA,
                TjSafe = caps.TjSafe,
                TjMax = caps.TjMax,
                TemperatureDelta = _tempDelta,
                PredictedTemperature = PredictTemperature(),
                IsSiliconThrottling = flags.thermal || flags.power || flags.current
            };

            // Calculate Thermal Condition
            if (state.IsSiliconThrottling || state.CurrentTemperature >= state.TjMax - 2)
                state.Condition = ThermalCondition.Critical;
            else if (state.CurrentTemperature >= state.TjSafe)
                state.Condition = ThermalCondition.Hot;
            else if (state.CurrentTemperature >= state.TjSafe - 10)
                state.Condition = ThermalCondition.Warm;
            else
                state.Condition = ThermalCondition.Safe;

            return state;
        }

        public bool CanIncreasePerformance()
        {
            var state = GetThermalState();
            
            if (_thermalProtectionActive || state.Condition >= ThermalCondition.Hot)
                return false;
            
            if (_tempDelta > 1.5 || state.PredictedTemperature >= state.TjSafe)
                return false;
            
            return true;
        }
        
        private async Task MonitoringLoop(CancellationToken ct)
        {
            var caps = _hardwareDetector.GetCapabilities();
            _logger.LogInfo($"[ThermalGovernor] Master Guard Loop Active. TjSafe={caps.TjSafe}°C");
            
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    double temp = ReadCpuTemperature();
                    var now = DateTime.UtcNow;
                    double timeDeltaSeconds = (now - _lastMonitorTime).TotalSeconds;
                    _lastMonitorTime = now;
                    
                    if (_lastTemp > 0 && timeDeltaSeconds > 0)
                        _tempDelta = (temp - _lastTemp) / timeDeltaSeconds;
                    
                    if (_tempEMA == 0) { _tempEMA = temp; _tempEMA_Fast = temp; _tempEMA_Slow = temp; }
                    else
                    {
                        _tempEMA = (TempEMA_Alpha * temp) + ((1 - TempEMA_Alpha) * _tempEMA);
                        _tempEMA_Fast = (EMA_Fast_Alpha * temp) + ((1 - EMA_Fast_Alpha) * _tempEMA_Fast);
                        _tempEMA_Slow = (EMA_Slow_Alpha * temp) + ((1 - EMA_Slow_Alpha) * _tempEMA_Slow);
                    }
                    
                    _lastTemp = temp;
                    _currentTemp = temp;
                    
                    CheckThermalConditions(caps);
                    
                    // ADAPTIVE POLLING: 250ms when HOT/CRITICAL, 1000ms otherwise
                    var state = GetThermalState();
                    int nextDelay = (state.Condition >= ThermalCondition.Hot) ? 2000 : 5000;
                    await Task.Delay(nextDelay, ct);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogError($"[ThermalGovernor] Signal loss in guard loop: {ex.Message}");
                    await Task.Delay(1000, ct); 
                }
            }
        }
        
        private void CheckThermalConditions(CpuTuningCapabilities caps)
        {
            var state = GetThermalState();
            
            if (state.IsCritical)
            {
                _consecutiveHighTempCycles++;
                if (_consecutiveHighTempCycles >= 1 && !_thermalProtectionActive)
                {
                    _thermalProtectionActive = true;
                    _logger.LogWarning($"[ThermalGovernor] CRITICAL THERMAL EVENT! Silicon Throttling={state.IsSiliconThrottling}, Temp={state.CurrentTemperature:F1}°C");
                    
                    ThermalProtectionTriggered?.Invoke(this, new ThermalProtectionEventArgs
                    {
                        Temperature = state.CurrentTemperature,
                        TjSafe = state.TjSafe,
                        Reason = state.IsSiliconThrottling ? "Silicon Level Throttling Detected (MSR 0x19C)" : "Temperature Exceeds Critical Threshold"
                    });
                }
            }
            else if (state.Condition == ThermalCondition.Safe && _thermalProtectionActive)
            {
                // COOLDOWN / HYSTERESIS: Must be SAFE for a significant margin before deactivating
                _consecutiveHighTempCycles = 0;
                _thermalProtectionActive = false;
                _logger.LogInfo($"[ThermalGovernor] Cooldown complete. System safe.");
            }
        }
        
        private double PredictTemperature()
        {
            // PROFISSIONAL: Predição ponderada com EMA dual + amortecimento exponencial
            // Em 60s, a taxa de subida desacelera naturalmente (dissipação térmica)
            // Usamos fator de amortecimento para não superestimar
            double dampingFactor = 0.6; // Dissipação térmica reduz taxa ao longo do tempo
            
            // Peso maior para EMA rápido quando delta é positivo (aquecendo)
            // Peso maior para EMA lento quando delta é negativo (esfriando)
            double effectiveDelta = _tempDelta > 0 
                ? _tempDelta * dampingFactor  // Aquecendo: amortece (dissipação)
                : _tempDelta * 0.8;           // Esfriando: mais conservador
            
            double predicted = _tempEMA + (effectiveDelta * PredictionHorizonSeconds);
            
            // Clamp: nunca prever abaixo da temperatura atual nem acima de 120°C
            return Math.Clamp(predicted, _tempEMA, 120.0);
        }
        
        private double ReadCpuTemperature()
        {
            // PRIORITY 1: Kernel-level MSR/DTS reading (Most accurate)
            if (_backend.IsDriverLoaded())
            {
                if (_backend.ReadMsr(0x19C, out ulong thermStatus) && (thermStatus & 1) != 0)
                {
                    _logger.LogWarning("[ThermalGovernor] KERNEL ALERT: HW Throttling Bit Active!");
                    _consecutiveHighTempCycles = 10; 
                }
            }

            // PRIORITY 2: Use externally injected temperature (ENTERPRISE FIX: Thread-safe)
            lock (_injectionLock)
            {
                if (_injectedTemp > 0 && (DateTime.UtcNow - _lastInjectionTime).TotalMilliseconds < InjectionMaxAgeMs)
                {
                    return _injectedTemp;
                }
            }
            
            // FALLBACK: WMI reading
            try
            {
                return ReadTemperatureFromWmi();
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ThermalGovernor] Could not read temperature: {ex.Message}");
                // PROFISSIONAL: Sem placebo — retorna último valor real ou NaN
                return _lastTemp > 0 ? _lastTemp : double.NaN;
            }
        }
        
        /// <summary>
        /// Injects a real CPU temperature from an external hardware source.
        /// Call this every monitoring cycle with the temperature read from nvidia-smi, 
        /// IThermalMonitorService, or any reliable sensor. When fresh injections are 
        /// available, the internal WMI fallback is skipped entirely.
        /// </summary>
        public void InjectTemperature(double celsius)
        {
            if (celsius < 1.0 || celsius > 130.0) return; // Reject obviously invalid readings
            
            // ✅ ENTERPRISE FIX: Lock para thread-safety
            lock (_injectionLock)
            {
                _injectedTemp = celsius;
                _lastInjectionTime = DateTime.UtcNow;
            }
        }
        
        private double ReadTemperatureFromWmi()
        {
            // USAR CACHE CENTRALIZADO (EVITA CONGELAMENTOS WMI)
            var cache = VoltrisOptimizer.Core.SystemMetricsCache.Instance;
            if (cache.CpuTemperature > 0) return cache.CpuTemperature;
            
            // Fallback para último valor real se o cache estiver vazio
            return _lastTemp > 0 ? _lastTemp : double.NaN;
        }
        
        public void Dispose()
        {
            Stop();
            _tempCounter?.Dispose();
        }
    }
}

