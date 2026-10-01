using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Monitoring
{
    /// <summary>
    /// Coletor de métricas leve para monitoramento de performance em tempo real
    /// ZERO impacto em FPS - usa cache agressivo e métodos otimizados
    /// </summary>
    public class MetricsCollector : IDisposable
    {
        private readonly WmiCacheService _wmiCache;
        private readonly ProcessCacheService _processCache;
        private readonly ILoggingService _logger;
        
        // Cache local para evitar chamadas excessivas
        private SystemMetrics _cachedMetrics = new();
        private DateTime _lastCollection = DateTime.MinValue;
        private readonly TimeSpan _cacheInterval = TimeSpan.FromSeconds(2);
        private bool _disposed = false;
        
        // NativeSystemMetrics (substitui PerformanceCounters pesados)
        private readonly VoltrisOptimizer.Helpers.NativeSystemMetrics _nativeMetrics = new();
        
        public MetricsCollector(
            WmiCacheService wmiCache,
            ProcessCacheService processCache,
            ILoggingService logger)
        {
            _wmiCache = wmiCache ?? throw new ArgumentNullException(nameof(wmiCache));
            _processCache = processCache ?? throw new ArgumentNullException(nameof(processCache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            InitializePerformanceCounters();
        }
        
        private void InitializePerformanceCounters()
        {
            try
            {
                // Warm-up NativeSystemMetrics (GetSystemTimes/GlobalMemoryStatusEx)
                _nativeMetrics.GetCpuUsage();
                _nativeMetrics.GetMemoryUsage();
                _logger.LogInfo("[MetricsCollector] NativeSystemMetrics inicializados (ultra leve)");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[MetricsCollector] NativeSystemMetrics não disponíveis: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Obtém métricas atuais do sistema (cached - 2s TTL)
        /// </summary>
        public async Task<SystemMetrics> GetCurrentMetricsAsync()
        {
            // CACHE: Retornar cache se coletado há menos de 2s
            if ((DateTime.Now - _lastCollection) < _cacheInterval)
                return _cachedMetrics;
                
            var metrics = new SystemMetrics();
            
            try
            {
                // CPU (NativeSystemMetrics - microsegundos, mesmo que Task Manager)
                metrics.CpuUsage = _nativeMetrics.GetCpuUsage();
                
                // RAM (NativeSystemMetrics - GlobalMemoryStatusEx)
                var memInfo = _nativeMetrics.GetMemoryUsage();
                metrics.RamPressure = memInfo.UsagePercent;
                
                // FPS (via processo de jogo detectado)
                metrics.Fps = await GetFpsAsync();
                
                // FrameTime (calculado a partir de FPS)
                metrics.FrameTime = metrics.Fps > 0 ? 1000.0 / metrics.Fps : 0;
                
                // FrameTime Variance (requer histórico)
                metrics.FrameTimeVariance = CalculateFrameTimeVariance();
                
                // Input Latency (via Dispatcher round-trip)
                metrics.InputLatency = GetInputLatency();
                
                // GPU Load (placeholder - requer integração com DXGI/NVML)
                metrics.GpuLoad = 0; // TODO: Implementar via DXGI quando jogo D3D ativo
                
                // Temperaturas (placeholder - requer LibreHardwareMonitor ou similar)
                metrics.CpuTemperature = 0;
                metrics.GpuTemperature = 0;
                
                _cachedMetrics = metrics;
                _lastCollection = DateTime.Now;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[MetricsCollector] Erro ao coletar métricas: {ex.Message}");
            }
            
            return metrics;
        }
        
        /// <summary>
        /// Obtém FPS do jogo ativo (via processo detectado)
        /// </summary>
        private async Task<double> GetFpsAsync()
        {
            try
            {
                // Se GameDetectionService está ativo, obter processo de jogo
                if (App.GameDetectionService?.IsMonitoring == true)
                {
                    // Placeholder: FPS real requer hooking de Present() ou leitura de shared memory
                    // Por enquanto, retornar estimativa baseada em frametime
                    return 60.0; // TODO: Implementar FPS real via hooking ou overlay
                }
                
                return 0;
            }
            catch
            {
                return 0;
            }
        }
        
        /// <summary>
        /// Calcula variância de frametime (detecta micro-stutter)
        /// </summary>
        private double CalculateFrameTimeVariance()
        {
            // TODO: Implementar histórico de frametime e calcular desvio padrão
            // Por enquanto, retornar 0
            return 0;
        }
        
        /// <summary>
        /// Mede input latency via Dispatcher round-trip
        /// MÉTODO LEVE: Força round-trip no Dispatcher e mede tempo
        /// CORREÇÃO: Usa BeginInvoke + TaskCompletionSource para evitar deadlock
        /// </summary>
        private double GetInputLatency()
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted)
                    return 0;
                
                // Se já estamos na UI thread, retornar 0 (sem sentido medir)
                if (dispatcher.CheckAccess())
                    return 0;
                
                var sw = Stopwatch.StartNew();
                var tcs = new TaskCompletionSource<bool>();
                
                dispatcher.BeginInvoke(new Action(() => tcs.TrySetResult(true)), 
                    System.Windows.Threading.DispatcherPriority.Normal);
                
                // Aguardar com timeout de 100ms para nunca bloquear
                if (tcs.Task.Wait(100))
                {
                    sw.Stop();
                    return sw.Elapsed.TotalMilliseconds;
                }
                
                return 100; // Timeout = latência alta
            }
            catch
            {
                return 0;
            }
        }
        
        /// <summary>
        /// Obtém CPU usage via WMI (fallback quando Performance Counter não disponível)
        /// </summary>
        private double GetCpuUsageViaWmi()
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT LoadPercentage FROM Win32_Processor");
                    
                foreach (System.Management.ManagementObject obj in searcher.Get())
                {
                    var load = obj["LoadPercentage"];
                    if (load != null)
                    {
                        return Convert.ToDouble(load);
                    }
                }
                
                return 0;
            }
            catch
            {
                return 0;
            }
        }
        
        /// <summary>
        /// Obtém RAM pressure via GetPhysicallyInstalledSystemMemory
        /// </summary>
        private double GetRamPressure()
        {
            try
            {
                var gcMemory = GC.GetTotalMemory(false);
                var process = Process.GetCurrentProcess();
                var workingSet = process.WorkingSet64;
                
                // Estimativa baseada em working set
                var totalRam = GetTotalPhysicalMemory();
                if (totalRam > 0)
                {
                    return (workingSet / (double)totalRam) * 100.0;
                }
                
                return 0;
            }
            catch
            {
                return 0;
            }
        }
        
        /// <summary>
        /// Obtém total de RAM física
        /// </summary>
        private long GetTotalPhysicalMemory()
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
                    
                foreach (System.Management.ManagementObject obj in searcher.Get())
                {
                    var total = obj["TotalPhysicalMemory"];
                    if (total != null)
                    {
                        return Convert.ToInt64(total);
                    }
                }
                
                return 0;
            }
            catch
            {
                return 0;
            }
        }
        
        public void Dispose()
        {
            if (_disposed)
                return;
            
            _disposed = true;
            _logger.LogInfo("[MetricsCollector] Disposed");
        }
    }
    
    /// <summary>
    /// Snapshot de métricas do sistema em um ponto no tempo
    /// </summary>
    public class SystemMetrics
    {
        public double CpuUsage { get; set; }          // Percentual 0-100
        public double GpuLoad { get; set; }           // Percentual 0-100
        public double RamPressure { get; set; }       // Percentual 0-100
        public double Fps { get; set; }               // Frames por segundo
        public double FrameTime { get; set; }         // Milissegundos
        public double FrameTimeVariance { get; set; } // Desvio padrão em ms
        public double InputLatency { get; set; }      // Milissegundos
        public double CpuTemperature { get; set; }    // Celsius
        public double GpuTemperature { get; set; }    // Celsius
        public DateTime Timestamp { get; set; } = DateTime.Now;
        
        public override string ToString()
        {
            return $"CPU:{CpuUsage:F1}% GPU:{GpuLoad:F1}% RAM:{RamPressure:F1}% " +
                   $"FPS:{Fps:F0} FrameTime:{FrameTime:F2}ms InputLag:{InputLatency:F1}ms";
        }
    }
}
