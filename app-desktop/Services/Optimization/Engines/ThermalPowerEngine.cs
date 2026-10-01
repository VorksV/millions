using VoltrisOptimizer.Utils;
using System;
using System.Diagnostics;
using System.Management;
using System.Linq;
using System.Runtime.InteropServices;

namespace VoltrisOptimizer.Services.Optimization.Engines
{
    /// <summary>
    /// DSL 5.0 - Thermal & Power Intelligence Engine
    /// Monitoramento térmico preventivo com ações corretivas para evitar Power Throttling agressivo.
    /// </summary>
    public class ThermalPowerEngine : IDisposable
    {
        private readonly ILoggingService _logger;
        private SafePerformanceCounter? _thermalThrottlingCounter;
        private bool _isThrottlingTriggered = false;
        private DateTime _lastCorrectiveAction = DateTime.MinValue;

        public ThermalPowerEngine(ILoggingService logger)
        {
            _logger = logger;
            InitializeCounters();
        }

        private void InitializeCounters()
        {
            try
            {
                if (SafePerformanceCounterCategory.Exists("Thermal Zone Information"))
                {
                    var cat = new SafePerformanceCounterCategory("Thermal Zone Information");
                    if (cat.InstanceExists("_Total"))
                    {
                        var counters = cat.GetCounters("_Total");
                        if (System.Linq.Enumerable.Any(counters, c => c.CounterName == "Percent Passive Limit"))
                        {
                            _thermalThrottlingCounter = new SafePerformanceCounter("Thermal Zone Information", "Percent Passive Limit", "_Total");
                            _thermalThrottlingCounter.NextValue();
                        }
                        else
                        {
                            _logger?.LogInfo("[Thermal] Counter 'Percent Passive Limit' não encontrado em 'Thermal Zone Information'. Monitor térmico desativado.");
                        }
                    }
                    else
                    {
                        _logger?.LogInfo("[Thermal] Instância '_Total' não encontrada em 'Thermal Zone Information'. Monitor térmico desativado.");
                    }
                }
                else
                {
                    _logger?.LogInfo("[Thermal] Categoria 'Thermal Zone Information' não encontrada. Monitor térmico primário desativado.");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[Thermal] Falha ao inicializar contadores térmicos: {ex.Message}");
            }
        }

        public void Update()
        {
            try
            {
                // PERFORMANCE: Usar GlobalThermalMonitorService em vez de WMI/Counters agressivos
                var thermalMonitor = Core.ServiceLocator.GetService<VoltrisOptimizer.Services.Thermal.IGlobalThermalMonitorService>();
                float? temperature = null;
                
                if (thermalMonitor != null)
                {
                    temperature = (float?)(thermalMonitor.CurrentMetrics?.CpuTemperature);
                }

                // Fallback para contadores primários se o monitor global falhar
                if (!temperature.HasValue || temperature <= 0 || temperature > 110)
                {
                    temperature = GetTemperatureFromPrimarySource();
                }

                // Último recurso: WMI (muito lento, por isso é o último)
                if (!temperature.HasValue || temperature <= 0)
                {
                    temperature = GetTemperatureFromWmiFallback();
                }

                // HARDENING: Se não conseguimos medir a temperatura, modo de segurança
                if (!temperature.HasValue)
                {
                    if (!_isThrottlingTriggered)
                    {
                        _logger.LogInfo("[Thermal] Telemetria térmica indisponível — ativando modo de segurança preventivo.");
                        _isThrottlingTriggered = true;
                    }
                    return;
                }

                float throttleLimit = temperature.Value;

                if (throttleLimit < 100)
                {
                    if (!_isThrottlingTriggered)
                    {
                        _logger.LogInfo($"[Thermal] Proteção térmica ativa: limite detectado em {throttleLimit}%. Aplicando contenção de carga.");
                        _isThrottlingTriggered = true;
                    }
                    // IMPLEMENTADO: Ações corretivas quando throttling térmico é detectado
                    ApplyThermalCorrectiveActions();
                }
                else
                {
                    if (_isThrottlingTriggered)
                    {
                        _logger.LogInfo("[Thermal] Temperatura normalizada. Saindo do modo de contenção.");
                    }
                    _isThrottlingTriggered = false;
                }
            }
            catch { }
        }

        /// <summary>
        /// IMPLEMENTADO: Ações corretivas reais quando throttling térmico é detectado.
        /// Reduz carga de processos background para permitir que o sistema resfrie.
        /// </summary>
        private void ApplyThermalCorrectiveActions()
        {
            // Cooldown de 10 segundos entre ações corretivas
            if ((DateTime.Now - _lastCorrectiveAction).TotalSeconds < 10) return;

            try
            {
                // OTIMIZAÇÃO: Usar Cache Service em vez de redundante Process.GetProcesses()
                var processCache = Core.ServiceLocator.GetService<ProcessCacheService>();
                var processInfos = processCache?.GetCachedProcessInfos() ?? Enumerable.Empty<ProcessCacheService.CachedProcessInfo>();
                
                int throttledCount = 0;
                int fgPid = (int)Core.ForegroundWindowTracker.Instance.CurrentPid;

                foreach (var p in processInfos)
                {
                    try
                    {
                        if (p.Id == fgPid) continue;
                        if (p.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase)) continue;
                        if (p.ProcessName.Equals("dwm", StringComparison.OrdinalIgnoreCase)) continue;
                        if (VoltrisOptimizer.Services.Gamer.Data.GameDatabase.KnownGames.Contains(p.ProcessName)) continue;

                        // Só throttlar processos com carga (CpuUsage > 5) para evitar ruído inútil
                        if (p.CpuUsage > 5.0 || p.WorkingSet64 > 500 * 1024 * 1024)
                        {
                            IntPtr hProcess = OpenProcess(0x0200 | 0x0400, false, p.Id);
                            if (hProcess != IntPtr.Zero)
                            {
                                try
                                {
                                    var state = new PROCESS_POWER_THROTTLING_STATE
                                    {
                                        Version = 1,
                                        ControlMask = 0x01,
                                        StateMask = 0x01 // Enable EcoQoS
                                    };
                                    SetProcessInformation(hProcess, 4, ref state, Marshal.SizeOf(state));
                                    throttledCount++;
                                }
                                finally { CloseHandle(hProcess); }
                            }
                        }
                    }
                    catch { }
                }

                if (throttledCount > 0)
                    _logger.LogInfo($"[Thermal] Ação corretiva: {throttledCount} processos pesados em EcoQoS para resfriamento.");

                _lastCorrectiveAction = DateTime.Now;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Thermal] Erro na ação corretiva: {ex.Message}");
            }
        }


        private float? GetTemperatureFromPrimarySource()
        {
            try { return _thermalThrottlingCounter?.NextValue(); } catch { return null; }
        }

        private DateTime _lastWmiQuery = DateTime.MinValue;
        private bool _wmiSupported = true;

        private float? GetTemperatureFromWmiFallback()
        {
            if (!_wmiSupported) return null;
            // Ocultar WMI fallback - custa muito caro (CPU spikes)
            if ((DateTime.Now - _lastWmiQuery).TotalSeconds < 30) return null;
            _lastWmiQuery = DateTime.Now;

            try
            {
                using (var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM MSAcpi_ThermalZoneTemperature"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                using var __dispose_obj = obj;
                        double tempDK = Convert.ToDouble(obj["CurrentTemperature"]);
                        double tempC = (tempDK - 2732) / 10.0;
                        return tempC > 85 ? 50f : 100f;
                    }
                }
            }
            catch 
            {
                _wmiSupported = false; // Disable to avoid continuous first-chance exceptions
            }
            return null;
        }

        // Removidos P/Invokes duplicados de foreground
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")] private static extern bool SetProcessInformation(IntPtr hProcess, int infoClass, ref PROCESS_POWER_THROTTLING_STATE info, int size);

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_POWER_THROTTLING_STATE { public uint Version; public uint ControlMask; public uint StateMask; }

        public void Dispose()
        {
            _thermalThrottlingCounter?.Dispose();
        }
    }
}

