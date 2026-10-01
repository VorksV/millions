using VoltrisOptimizer.Utils;
using VoltrisOptimizer.Helpers;
using System;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Gamer.GamerModeManager;
using VoltrisOptimizer.Services.Gamer.Overlay.Models;
using VoltrisOptimizer.Services.Thermal;

namespace VoltrisOptimizer.Services.Gamer.Overlay.Implementation
{
    /// <summary>
    /// Serviço profissional de telemetria para overlay
    /// Usa múltiplos métodos para garantir leituras precisas:
    /// - nvidia-smi para GPUs NVIDIA
    /// - PerformanceCounters para GPU (quando disponível)
    /// - WMI para CPU, RAM e temperaturas
    /// - ThermalMonitorService existente como fallback
    /// </summary>
    public class TelemetryService : IDisposable
    {
        private readonly ILoggingService? _logger;
        private readonly VoltrisOptimizer.Services.Thermal.IThermalMonitorService? _thermalMonitor;
        private readonly Process? _gameProcess;
        private bool _disposed = false;
        
        // NativeSystemMetrics (substitui PerformanceCounters pesados para CPU/RAM)
        private readonly VoltrisOptimizer.Helpers.NativeSystemMetrics _nativeMetrics = new();
        private SafePerformanceCounter? _gpuCounter;
        
        // Cache para reduzir overhead
        private DateTime _lastGpuUpdate = DateTime.MinValue;
        private DateTime _lastCpuUpdate = DateTime.MinValue;
        private DateTime _lastRamUpdate = DateTime.MinValue;
        private DateTime _lastTempUpdate = DateTime.MinValue;
        
        private const int GpuUpdateIntervalMs = 200; // GPU a cada 200ms
        private const int CpuUpdateIntervalMs = 100;  // CPU a cada 100ms
        private const int RamUpdateIntervalMs = 500;  // RAM a cada 500ms
        private const int TempUpdateIntervalMs = 1000; // Temp a cada 1s
        
        // Valores em cache
        private double _cachedGpuUsage = 0;
        private double _cachedGpuTemp = 0;
        private double _cachedGpuCoreClock = 0;
        private double _cachedGpuMemoryClock = 0;
        private double _cachedVramUsed = 0;
        private double _cachedVramTotal = 0;
        private double _cachedCpuUsage = 0;
        private double _cachedCpuTemp = 0;
        private double _cachedCpuClock = 0;
        private double _cachedRamUsage = 0;
        
        // nvidia-smi path
        private static readonly string NvidiaSmiPath = @"C:\Windows\System32\nvidia-smi.exe";
        private readonly bool _hasNvidiaSmi;

        // CORREÇÃO FORENSE #2: Flag estático que suprime re-tentativas da query WMI
        // "MSAcpi_ThermalZoneTemperature" quando a classe não existe no sistema.
        // Sem isso, ManagementException "Classe inválida" era disparada 1x/segundo
        // em TODA sessão, gerando centenas de first-chance exceptions registradas nos logs.
        private static volatile bool _wmiThermalClassAvailable = true;
        
        public TelemetryService(ILoggingService? logger, Process? gameProcess = null, VoltrisOptimizer.Services.Thermal.IThermalMonitorService? thermalMonitor = null)
        {
            _logger?.LogEntry(nameof(TelemetryService));
            _logger = logger;
            _gameProcess = gameProcess;
            _thermalMonitor = thermalMonitor;
            _hasNvidiaSmi = System.IO.File.Exists(NvidiaSmiPath);
            
            InitializeCounters();
            _logger?.LogExit(nameof(TelemetryService));
        }
        
        private void InitializeCounters()
        {
            _logger?.LogEntry(nameof(InitializeCounters));
            try
            {
                // CPU/RAM: NativeSystemMetrics (warm-up)
                _nativeMetrics.GetCpuUsage();
                _nativeMetrics.GetMemoryUsage();
                
                // GPU Counter (pode não estar disponível em todos os sistemas)
                try
                {
                    var gpuCategory = new SafePerformanceCounterCategory("GPU Engine");
                    var instances = gpuCategory.GetInstanceNames();
                    
                    foreach (var instance in instances)
                    {
                        if (instance.Contains("engtype_3D") || instance.Contains("engtype_Compute"))
                        {
                            try
                            {
                                _gpuCounter = new SafePerformanceCounter("GPU Engine", "Utilization Percentage", instance);
                                _gpuCounter.NextValue(); // Primeira leitura
                                _logger?.LogInfo($"[Telemetry] GPU SafePerformanceCounter inicializado: {instance}");
                                break;
                            }
                            catch { }
                        }
                    }
                }
                catch
                {
                    _logger?.LogInfo("[Telemetry] GPU SafePerformanceCounter não disponível, usando nvidia-smi/WMI");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[Telemetry] Erro ao inicializar contadores: {ex.Message}");
            }
            _logger?.LogExit(nameof(InitializeCounters));
        }
        
        /// <summary>
        /// Coleta todas as métricas do sistema
        /// </summary>
        public async Task<MetricsData> CollectMetricsAsync(CancellationToken cancellationToken = default)
        {
            _logger?.LogEntry(nameof(CollectMetricsAsync));
            var metrics = new MetricsData
            {
                Timestamp = DateTime.Now
            };
            
            try
            {
                // Coletar métricas em paralelo quando possível
                var cpuTask = Task.Run(() => CollectCpuMetrics(metrics), cancellationToken);
                var ramTask = Task.Run(() => CollectRamMetrics(metrics), cancellationToken);
                var gpuTask = CollectGpuMetricsAsync(metrics, cancellationToken);
                var tempTask = CollectTemperatureMetricsAsync(metrics, cancellationToken);
                
                await Task.WhenAll(cpuTask, ramTask, gpuTask, tempTask);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[Telemetry] Erro ao coletar métricas: {ex.Message}");
            }
            
            _logger?.LogExit(nameof(CollectMetricsAsync));
            return metrics;
        }
        
        private void CollectCpuMetrics(MetricsData metrics)
        {
            _logger?.LogEntry(nameof(CollectCpuMetrics));
            try
            {
                var now = DateTime.Now;
                
                if ((now - _lastCpuUpdate).TotalMilliseconds >= CpuUpdateIntervalMs)
                {
                    // CPU Usage via NativeSystemMetrics (GetSystemTimes - mesmo que Task Manager)
                    try
                    {
                        var cpuUsage = _nativeMetrics.GetCpuUsage();
                        _cachedCpuUsage = cpuUsage;
                        metrics.CpuUsagePercent = cpuUsage;
                    }
                    catch
                    {
                        metrics.CpuUsagePercent = _cachedCpuUsage;
                    }
                    
                    // CPU Clock via WMI
                    try
                    {
                        using var searcher = new ManagementObjectSearcher("SELECT CurrentClockSpeed FROM Win32_Processor");
                        foreach (ManagementObject obj in searcher.Get())
                        {
                using var __dispose_obj = obj;
                            var clock = obj["CurrentClockSpeed"];
                            if (clock != null)
                            {
                                var clockMhz = Convert.ToDouble(clock);
                                if (clockMhz > 100 && clockMhz < 10000)
                                {
                                    _cachedCpuClock = clockMhz;
                                    metrics.CpuClockMhz = clockMhz;
                                }
                                else
                                {
                                    metrics.CpuClockMhz = _cachedCpuClock;
                                }
                            }
                            break;
                        }
                    }
                    catch
                    {
                        metrics.CpuClockMhz = _cachedCpuClock;
                    }
                    
                    _lastCpuUpdate = now;
                }
                else
                {
                    // Usar valores em cache
                    metrics.CpuUsagePercent = _cachedCpuUsage;
                    metrics.CpuClockMhz = _cachedCpuClock;
                }
            }
            catch
            {
                metrics.CpuUsagePercent = _cachedCpuUsage;
                metrics.CpuClockMhz = _cachedCpuClock;
            }
            _logger?.LogExit(nameof(CollectCpuMetrics));
        }
        
        private void CollectRamMetrics(MetricsData metrics)
        {
            _logger?.LogEntry(nameof(CollectRamMetrics));
            try
            {
                var now = DateTime.Now;
                
                if ((now - _lastRamUpdate).TotalMilliseconds >= RamUpdateIntervalMs)
                {
                    try
                    {
                        var memInfo = _nativeMetrics.GetMemoryUsage();
                        _cachedRamUsage = memInfo.UsagePercent;
                        metrics.RamUsagePercent = memInfo.UsagePercent;
                    }
                    catch
                    {
                        metrics.RamUsagePercent = _cachedRamUsage;
                    }
                    
                    _lastRamUpdate = now;
                }
                else
                {
                    metrics.RamUsagePercent = _cachedRamUsage;
                }
            }
            catch
            {
                metrics.RamUsagePercent = _cachedRamUsage;
            }
            _logger?.LogExit(nameof(CollectRamMetrics));
        }
        
        private async Task CollectGpuMetricsAsync(MetricsData metrics, CancellationToken cancellationToken)
        {
            _logger?.LogEntry(nameof(CollectGpuMetricsAsync));
            try
            {
                var now = DateTime.Now;
                
                if ((now - _lastGpuUpdate).TotalMilliseconds >= GpuUpdateIntervalMs)
                {
                    // PRIORIDADE 1: nvidia-smi (mais confiável para NVIDIA)
                    if (_hasNvidiaSmi)
                    {
                        try
                        {
                            var gpuMetrics = await GetNvidiaGpuMetricsAsync(cancellationToken);
                            _cachedGpuUsage = gpuMetrics.Usage;
                            _cachedGpuTemp = gpuMetrics.Temperature;
                            _cachedGpuCoreClock = gpuMetrics.CoreClock;
                            _cachedGpuMemoryClock = gpuMetrics.MemoryClock;
                            _cachedVramUsed = gpuMetrics.VramUsed;
                            _cachedVramTotal = gpuMetrics.VramTotal;
                            
                            metrics.GpuUsagePercent = gpuMetrics.Usage;
                            metrics.GpuTemperature = gpuMetrics.Temperature > 0 ? gpuMetrics.Temperature : null;
                            metrics.GpuCoreClockMhz = gpuMetrics.CoreClock > 0 ? gpuMetrics.CoreClock : null;
                            metrics.GpuMemoryClockMhz = gpuMetrics.MemoryClock > 0 ? gpuMetrics.MemoryClock : null;
                            
                            // Calcular VRAM usage percent
                            if (gpuMetrics.VramTotal > 0)
                            {
                                metrics.VramUsagePercent = (gpuMetrics.VramUsed / gpuMetrics.VramTotal) * 100;
                            }
                            
                            _lastGpuUpdate = now;
                            return; // Sucesso com nvidia-smi
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning($"[Telemetry] Erro ao usar nvidia-smi: {ex.Message}");
                        }
                    }
                    
                    // PRIORIDADE 2: SafePerformanceCounter (funciona em algumas GPUs)
                    if (_gpuCounter != null)
                    {
                        try
                        {
                            var gpuUsage = _gpuCounter.NextValue();
                            if (gpuUsage >= 0 && gpuUsage <= 100)
                            {
                                _cachedGpuUsage = gpuUsage;
                                metrics.GpuUsagePercent = gpuUsage;
                            }
                            else
                            {
                                metrics.GpuUsagePercent = _cachedGpuUsage;
                            }
                        }
                        catch
                        {
                            metrics.GpuUsagePercent = _cachedGpuUsage;
                        }
                    }
                    
                    // PRIORIDADE 3: ThermalMonitorService (fallback)
                    if (_thermalMonitor != null && metrics.GpuUsagePercent == 0)
                    {
                        try
                        {
                            var thermalMetrics = await _thermalMonitor.GetCurrentMetricsAsync();
                            if (thermalMetrics.GpuUsage > 0)
                            {
                                _cachedGpuUsage = thermalMetrics.GpuUsage;
                                metrics.GpuUsagePercent = thermalMetrics.GpuUsage;
                            }
                            
                            if (thermalMetrics.GpuTemperature > 0)
                            {
                                _cachedGpuTemp = thermalMetrics.GpuTemperature;
                                metrics.GpuTemperature = thermalMetrics.GpuTemperature;
                            }
                            
                            // OTIMIZAÇÃO: Propriedades GpuCoreClock/GpuMemoryClock não existem mais - usar valores padrão
                            // Manter valores cached anteriores se existirem
                            if (_cachedGpuCoreClock > 0)
                            {
                                metrics.GpuCoreClockMhz = _cachedGpuCoreClock;
                            }
                            
                            if (_cachedGpuMemoryClock > 0)
                            {
                                metrics.GpuMemoryClockMhz = _cachedGpuMemoryClock;
                            }
                            
                            if (thermalMetrics.GpuVramTotal > 0)
                            {
                                _cachedVramTotal = thermalMetrics.GpuVramTotal;
                                _cachedVramUsed = thermalMetrics.GpuVramUsed;
                                metrics.VramUsagePercent = (thermalMetrics.GpuVramUsed / thermalMetrics.GpuVramTotal) * 100;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning($"[Telemetry] Erro ao usar ThermalMonitorService: {ex.Message}");
                        }
                    }
                    
                    // Se ainda não temos valores, usar cache
                    if (metrics.GpuUsagePercent == 0)
                    {
                        metrics.GpuUsagePercent = _cachedGpuUsage;
                    }
                    
                    if (!metrics.GpuTemperature.HasValue && _cachedGpuTemp > 0)
                    {
                        metrics.GpuTemperature = _cachedGpuTemp;
                    }
                    
                    if (!metrics.GpuCoreClockMhz.HasValue && _cachedGpuCoreClock > 0)
                    {
                        metrics.GpuCoreClockMhz = _cachedGpuCoreClock;
                    }
                    
                    if (!metrics.GpuMemoryClockMhz.HasValue && _cachedGpuMemoryClock > 0)
                    {
                        metrics.GpuMemoryClockMhz = _cachedGpuMemoryClock;
                    }
                    
                    if (metrics.VramUsagePercent == 0 && _cachedVramTotal > 0)
                    {
                        metrics.VramUsagePercent = (_cachedVramUsed / _cachedVramTotal) * 100;
                    }
                    
                    _lastGpuUpdate = now;
                }
                else
                {
                    // Usar valores em cache
                    metrics.GpuUsagePercent = _cachedGpuUsage;
                    metrics.GpuTemperature = _cachedGpuTemp > 0 ? _cachedGpuTemp : null;
                    metrics.GpuCoreClockMhz = _cachedGpuCoreClock > 0 ? _cachedGpuCoreClock : null;
                    metrics.GpuMemoryClockMhz = _cachedGpuMemoryClock > 0 ? _cachedGpuMemoryClock : null;
                    if (_cachedVramTotal > 0)
                    {
                        metrics.VramUsagePercent = (_cachedVramUsed / _cachedVramTotal) * 100;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[Telemetry] Erro ao coletar métricas de GPU: {ex.Message}");
                // Usar valores em cache em caso de erro
                metrics.GpuUsagePercent = _cachedGpuUsage;
                metrics.GpuTemperature = _cachedGpuTemp > 0 ? _cachedGpuTemp : null;
            }
            _logger?.LogExit(nameof(CollectGpuMetricsAsync));
        }
        
        private async Task<(double Temperature, double Usage, double CoreClock, double MemoryClock, double VramUsed, double VramTotal)> GetNvidiaGpuMetricsAsync(CancellationToken cancellationToken)
        {
            _logger?.LogEntry(nameof(GetNvidiaGpuMetricsAsync));
            try
            {
                using var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = NvidiaSmiPath,
                        Arguments = "--query-gpu=temperature.gpu,utilization.gpu,clocks.gr,clocks.mem,memory.used,memory.total --format=csv,noheader,nounits",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true
                    }
                };
                
                proc.Start();
                var output = await proc.StandardOutput.ReadToEndAsync();
                await proc.WaitForExitAsync(cancellationToken);
                
                var parts = output.Trim().Split(',');
                if (parts.Length >= 6)
                {
                    double.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var temp);
                    double.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var usage);
                    double.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var coreClock);
                    double.TryParse(parts[3].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var memClock);
                    double.TryParse(parts[4].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var vramUsed);
                    double.TryParse(parts[5].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var vramTotal);
                    
                    // Validar valores
                    if (temp < 0 || temp > 150) temp = 0;
                    if (usage < 0 || usage > 100) usage = 0;
                    if (coreClock < 0 || coreClock > 5000) coreClock = 0;
                    if (memClock < 0 || memClock > 10000) memClock = 0;
                    if (vramUsed < 0) vramUsed = 0;
                    if (vramTotal < 0) vramTotal = 0;
                    
                    // Converter VRAM de MB para GB
                    vramUsed = vramUsed / 1024.0;
                    vramTotal = vramTotal / 1024.0;
                    
                    return (temp, usage, coreClock, memClock, vramUsed, vramTotal);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[Telemetry] Erro ao executar nvidia-smi: {ex.Message}");
            }
            
            _logger?.LogExit(nameof(GetNvidiaGpuMetricsAsync));
            return (0, 0, 0, 0, 0, 0);
        }
        
        private async Task CollectTemperatureMetricsAsync(MetricsData metrics, CancellationToken cancellationToken)
        {
            _logger?.LogEntry(nameof(CollectTemperatureMetricsAsync));
            try
            {
                var now = DateTime.Now;
                
                if ((now - _lastTempUpdate).TotalMilliseconds >= TempUpdateIntervalMs)
                {
                    // CPU Temperature via WMI
                    // CORREÇÃO FORENSE #2: Tentativa suprimida se a classe WMI não existe neste sistema
                    if (_wmiThermalClassAvailable)
                    {
                        try
                        {
                            using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM MSAcpi_ThermalZoneTemperature");
                            foreach (ManagementObject obj in searcher.Get())
                            {
                    using var __dispose_obj = obj;
                                var temp = obj["CurrentTemperature"];
                                if (temp != null)
                                {
                                    var tempKelvin = Convert.ToDouble(temp) / 10.0;
                                    var tempCelsius = tempKelvin - 273.15;
                                    if (tempCelsius > 0 && tempCelsius < 150)
                                    {
                                        _cachedCpuTemp = tempCelsius;
                                        metrics.CpuTemperature = tempCelsius;
                                        break;
                                    }
                                }
                            }
                        }
                        catch (ManagementException ex) when (
                            ex.ErrorCode == ManagementStatus.InvalidClass ||
                            ex.ErrorCode == ManagementStatus.NotFound)
                        {
                            // Classe WMI não registrada neste sistema — suprimir tentativas futuras
                            _wmiThermalClassAvailable = false;
                            _logger?.LogEntry($"[TelemetryService] WMI MSAcpi_ThermalZoneTemperature não disponível (código: {ex.ErrorCode}). Usando ThermalMonitorService como fonte exclusiva.");
                        }
                        catch { /* Erro transiente — manter _wmiThermalClassAvailable = true para re-tentar */ }
                    }
                    
                    // Se não encontrou CPU temp, tentar ThermalMonitorService
                    if (!metrics.CpuTemperature.HasValue && _thermalMonitor != null)
                    {
                        try
                        {
                            var thermalMetrics = await _thermalMonitor.GetCurrentMetricsAsync();
                            if (thermalMetrics.CpuTemperature > 0)
                            {
                                _cachedCpuTemp = thermalMetrics.CpuTemperature;
                                metrics.CpuTemperature = thermalMetrics.CpuTemperature;
                            }
                        }
                        catch { }
                    }
                    
                    // GPU Temperature já coletada em CollectGpuMetricsAsync
                    
                    _lastTempUpdate = now;
                }
                else
                {
                    // Usar valores em cache
                    if (_cachedCpuTemp > 0)
                    {
                        metrics.CpuTemperature = _cachedCpuTemp;
                    }
                    if (_cachedGpuTemp > 0)
                    {
                        metrics.GpuTemperature = _cachedGpuTemp;
                    }
                }
            }
            catch
            {
                // Usar valores em cache
                if (_cachedCpuTemp > 0)
                {
                    metrics.CpuTemperature = _cachedCpuTemp;
                }
                if (_cachedGpuTemp > 0)
                {
                    metrics.GpuTemperature = _cachedGpuTemp;
                }
            }
            _logger?.LogExit(nameof(CollectTemperatureMetricsAsync));
        }
        
        private double GetTotalRamGb()
        {
            _logger?.LogEntry(nameof(GetTotalRamGb));
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Capacity FROM Win32_PhysicalMemory");
                long totalBytes = 0;
                
                foreach (ManagementObject obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    if (obj["Capacity"] != null)
                    {
                        totalBytes += Convert.ToInt64(obj["Capacity"]);
                    }
                }
                
                _logger?.LogExit(nameof(GetTotalRamGb));
                return totalBytes / (1024.0 * 1024.0 * 1024.0);
            }
            catch
            {
                _logger?.LogExit(nameof(GetTotalRamGb));
                return 0;
            }
        }
        
        public void Dispose()
        {
            _logger?.LogEntry(nameof(Dispose));
            if (_disposed)
            {
                _logger?.LogExit(nameof(Dispose));
                return;
            }
            
            _gpuCounter?.Dispose();
            
            _disposed = true;
            _logger?.LogExit(nameof(Dispose));
        }
    }
}


