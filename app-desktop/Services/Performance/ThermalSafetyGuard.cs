using System;
using System.Management;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.Performance.Models;
using VoltrisOptimizer.Services.Thermal;
using VoltrisOptimizer.Services.Thermal.Models;

namespace VoltrisOptimizer.Services.Performance
{
    /// <summary>
    /// Guarda de segurança térmica
    /// Monitora temperatura e desativa otimizações se necessário
    /// Integrado com GlobalThermalMonitorService
    /// </summary>
    public class ThermalSafetyGuard
    {
        private readonly ILoggingService _logger;
        private readonly IGlobalThermalMonitorService? _thermalMonitor;
        private PerformanceProfile? _currentProfile;
        private ThermalMetrics? _lastMetrics;
        private bool _currentlyDeactivated;
        private bool _currentlyThrottling;
        private DateTime _lastDeactivationTime = DateTime.MinValue;
        private DateTime _lastLogWarningTime = DateTime.MinValue;
        private static readonly TimeSpan _recoveryCooldown = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan _logDebounceTime = TimeSpan.FromSeconds(5);
        private const double HysteresisDegrees = 10.0;

        public event EventHandler<ThermalSafetyAlert>? ThermalAlertRaised;
        public event EventHandler? SafetyRecovered;

        public ThermalSafetyGuard(ILoggingService logger, IGlobalThermalMonitorService? thermalMonitor)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _thermalMonitor = thermalMonitor;

            if (_thermalMonitor != null)
            {
                _thermalMonitor.MetricsUpdated += OnThermalMetricsUpdated;
                _thermalMonitor.AlertGenerated += OnThermalAlertGenerated;
                _logger.LogInfo("[ThermalSafety] Integrado com GlobalThermalMonitorService");
            }
            else
            {
                _logger.LogWarning("[ThermalSafety] GlobalThermalMonitorService não disponível - proteção térmica limitada");
            }
        }

        /// <summary>
        /// Define o perfil de performance atual para verificação de thresholds
        /// </summary>
        public void SetCurrentProfile(PerformanceProfile profile)
        {
            _currentProfile = profile;
            _logger.LogDebug($"[ThermalSafety] Perfil definido: {profile.Name} (Threshold: {profile.ThermalThrottleThreshold}°C)");
        }

        /// <summary>
        /// Verifica se é seguro aplicar otimizações de performance
        /// </summary>
        public bool IsSafeToOptimize(out string reason)
        {
            reason = string.Empty;

            if (_thermalMonitor == null)
            {
                reason = "Monitor térmico não disponível";
                _logger.LogWarning("[ThermalSafety] Monitor térmico não disponível - permitindo otimização com aviso");
                return true; // Permitir se não houver monitor (fallback)
            }

            if (_lastMetrics == null)
            {
                reason = "Aguardando primeira leitura de temperatura";
                _logger.LogDebug("[ThermalSafety] Aguardando primeira leitura de temperatura");
                return true; // Permitir na primeira execução
            }

            if (_currentProfile == null)
            {
                reason = "Perfil de performance não definido";
                // Race condition na inicialização — perfil será definido em breve, não logar warning
                return true;
            }

            // Verificar temperatura da CPU com histerese
            double cpuTemp = _lastMetrics.CpuTemperature;
            double gpuTemp = _lastMetrics.GpuTemperature;
            double threshold = _currentProfile.ThermalThrottleThreshold;
            double releaseThreshold = threshold - HysteresisDegrees;

            bool cpuHot = !double.IsNaN(cpuTemp) && cpuTemp >= (_currentlyThrottling ? releaseThreshold : threshold);
            bool gpuHot = !double.IsNaN(gpuTemp) && gpuTemp >= (_currentlyThrottling ? releaseThreshold : threshold);

            if (cpuHot || gpuHot)
            {
                string src = cpuHot ? "CPU" : "GPU";
                double temp = cpuHot ? cpuTemp : gpuTemp;
                reason = $"{src} {(cpuHot ? "acima do threshold" : "ainda acima do release")}: {temp:F1}°C >= {(_currentlyThrottling ? releaseThreshold : threshold):F1}°C" +
                    $" (histerese: {HysteresisDegrees}°C)";
                if (!_currentlyThrottling)
                {
                    _logger.LogWarning($"[ThermalSafety] INÍCIO THROTTLE: {reason}");
                }
                if (DateTime.UtcNow - _lastLogWarningTime > _logDebounceTime)
                {
                    _logger.LogDebug($"[ThermalSafety] {reason}");
                    _lastLogWarningTime = DateTime.UtcNow;
                }
                _currentlyThrottling = true;
                return false;
            }

            if (_currentlyThrottling)
            {
                _currentlyThrottling = false;
                _logger.LogSuccess($"[ThermalSafety] FIM THROTTLE: temperatura normalizou (CPU: {cpuTemp:F1}°C, GPU: {gpuTemp:F1}°C)");
            }

            // Verificar temperatura crítica absoluta (90°C+)
            double maxTemp = Math.Max(
                double.IsNaN(_lastMetrics.CpuTemperature) ? 0 : _lastMetrics.CpuTemperature,
                double.IsNaN(_lastMetrics.GpuTemperature) ? 0 : _lastMetrics.GpuTemperature
            );

            if (maxTemp >= 98.0)
            {
                reason = $"Temperatura crítica detectada: {maxTemp:F1}°C";
                _logger.LogError($"[ThermalSafety] {reason}");
                return false;
            }

            _logger.LogDebug($"[ThermalSafety] Seguro para otimizar - CPU: {_lastMetrics.CpuTemperature:F1}°C, GPU: {_lastMetrics.GpuTemperature:F1}°C");
            return true;
        }

        /// <summary>
        /// Verifica se deve desativar otimizações devido a temperatura
        /// </summary>
        public bool ShouldDeactivate(out string reason)
        {
            reason = string.Empty;

            if (_currentProfile == null || !_currentProfile.DisableOnThermalAlert)
            {
                return false;
            }

            if (_lastMetrics == null)
            {
                return false;
            }

            double maxTemp = Math.Max(
                double.IsNaN(_lastMetrics.CpuTemperature) ? 0 : _lastMetrics.CpuTemperature,
                double.IsNaN(_lastMetrics.GpuTemperature) ? 0 : _lastMetrics.GpuTemperature
            );

            if (maxTemp >= 98.0)
            {
                reason = $"Temperatura crítica: {maxTemp:F1}°C - desativando otimizações para proteção";
                _logger.LogError($"[ThermalSafety] {reason}");
                return true;
            }

            if (maxTemp >= _currentProfile.ThermalThrottleThreshold)
            {
                reason = $"Temperatura acima do threshold do perfil: {maxTemp:F1}°C >= {_currentProfile.ThermalThrottleThreshold}°C";
                if (DateTime.UtcNow - _lastLogWarningTime > _logDebounceTime)
                {
                    _logger.LogWarning($"[ThermalSafety] {reason}");
                    _lastLogWarningTime = DateTime.UtcNow;
                }
                return true;
            }

            return false;
        }

        /// <summary>
        /// Verifica se a temperatura já voltou a um nível seguro para reativar otimizações
        /// </summary>
        public bool ShouldReactivate(out string reason)
        {
            reason = string.Empty;

            if (!_currentlyDeactivated)
                return false;

            if (DateTime.UtcNow - _lastDeactivationTime < _recoveryCooldown)
            {
                reason = $"Aguardando cooldown de {(int)(_recoveryCooldown - (DateTime.UtcNow - _lastDeactivationTime)).TotalSeconds}s";
                return false;
            }

            if (_lastMetrics == null)
                return false;

            double maxTemp = Math.Max(
                double.IsNaN(_lastMetrics.CpuTemperature) ? 0 : _lastMetrics.CpuTemperature,
                double.IsNaN(_lastMetrics.GpuTemperature) ? 0 : _lastMetrics.GpuTemperature
            );

            double safeThreshold = _currentProfile?.ThermalThrottleThreshold > 0
                ? _currentProfile.ThermalThrottleThreshold - HysteresisDegrees
                : 75.0;

            if (maxTemp <= safeThreshold)
            {
                reason = $"Temperatura normalizada: {maxTemp:F1}°C <= {safeThreshold}°C - reativando otimizações";
                _logger.LogSuccess($"[ThermalSafety] {reason}");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Obtém as métricas térmicas atuais
        /// </summary>
        public ThermalMetrics? GetCurrentMetrics()
        {
            return _lastMetrics;
        }

        /// <summary>
        /// Recomenda um perfil de performance seguro baseado na temperatura atual e tipo de hardware
        /// </summary>
        public PerformanceProfile GetSafeProfile(PerformanceProfile requestedProfile)
        {
            if (_lastMetrics == null)
                return requestedProfile;

            double maxTemp = Math.Max(
                double.IsNaN(_lastMetrics.CpuTemperature) ? 0 : _lastMetrics.CpuTemperature,
                double.IsNaN(_lastMetrics.GpuTemperature) ? 0 : _lastMetrics.GpuTemperature
            );

            bool isLaptop = DetectIsLaptop();

            if (isLaptop && requestedProfile.AggressivenessLevel >= 75)
            {
                if (maxTemp >= 75.0)
                    return PerformanceProfile.Presets.GamerBalanced;

                if (maxTemp >= 65.0)
                {
                    var safeProfile = PerformanceProfile.Presets.GamerCompetitive;
                    safeProfile.MinProcessorState = 90;
                    safeProfile.TurboBoostPolicy = 50;
                    return safeProfile;
                }
            }

            if (maxTemp >= 80.0 && requestedProfile.AggressivenessLevel >= 75)
                return PerformanceProfile.Presets.GamerBalanced;

            return requestedProfile;
        }

        private static bool DetectIsLaptop()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Battery");
                foreach (ManagementObject obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    if (obj["Availability"] != null)
                        return true;
                }
            }
            catch { }
            return false;
        }

        private void OnThermalMetricsUpdated(object? sender, ThermalMetrics metrics)
        {
            _lastMetrics = metrics;

            if (_currentProfile == null) return;

            // Verificar se deve DESATIVAR otimizações
            if (ShouldDeactivate(out string reason))
            {
                if (!_currentlyDeactivated)
                {
                    _currentlyDeactivated = true;
                    _lastDeactivationTime = DateTime.UtcNow;

                    var alert = new ThermalSafetyAlert
                    {
                        Level = metrics.CpuTemperature >= 90 || metrics.GpuTemperature >= 90 
                            ? ThermalSafetyLevel.Critical 
                            : ThermalSafetyLevel.Warning,
                        CpuTemperature = metrics.CpuTemperature,
                        GpuTemperature = metrics.GpuTemperature,
                        Message = reason,
                        Recommendation = "Otimizações de performance foram desativadas automaticamente",
                        Timestamp = DateTime.Now
                    };

                    ThermalAlertRaised?.Invoke(this, alert);
                }
                return;
            }

            // Verificar se deve REATIVAR otimizações (auto-recovery)
            if (_currentlyDeactivated && ShouldReactivate(out string recoveryReason))
            {
                _currentlyDeactivated = false;
                SafetyRecovered?.Invoke(this, EventArgs.Empty);
            }
        }

        private void OnThermalAlertGenerated(object? sender, ThermalAlert alert)
        {
            // Converter alerta do GlobalThermalMonitorService para ThermalSafetyAlert
            var safetyAlert = new ThermalSafetyAlert
            {
                Level = alert.Level == ThermalAlertLevel.Critical 
                    ? ThermalSafetyLevel.Critical 
                    : ThermalSafetyLevel.Warning,
                CpuTemperature = _lastMetrics?.CpuTemperature ?? 0,
                GpuTemperature = _lastMetrics?.GpuTemperature ?? 0,
                Message = alert.Message,
                Recommendation = alert.Recommendation,
                Timestamp = DateTime.Now
            };

            _logger.LogWarning($"[ThermalSafety] Alerta térmico recebido: {alert.Message}");
            ThermalAlertRaised?.Invoke(this, safetyAlert);
        }

        public void Dispose()
        {
            if (_thermalMonitor != null)
            {
                _thermalMonitor.MetricsUpdated -= OnThermalMetricsUpdated;
                _thermalMonitor.AlertGenerated -= OnThermalAlertGenerated;
            }
        }
    }
}
