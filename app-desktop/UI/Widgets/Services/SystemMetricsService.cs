using System;
using System.Diagnostics;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.UI.Widgets.Services
{
    public class SystemMetricsService : IDisposable
    {
        private readonly ILoggingService _logger;
        private bool _isDisposed;

        // [FIX:A-5] Estado de dispatch.
        // _uiDispatcher    : cacheado na primeira resolucao; null enquanto nao
        //                    houver Application (uso em contexto nao-WPF).
        // _raiseInFlight   : garante no maximo um BeginInvoke pendente, para que
        //                    o evento nao acumule na fila do Dispatcher.
        private System.Windows.Threading.Dispatcher? _uiDispatcher;
        private int _raiseInFlight;
        private bool _loggedThreadHop;

        public double CpuUsage { get; private set; }
        public double RamUsage { get; private set; }
        public double DiskUsage { get; private set; }
        public double? CpuTemperature { get; private set; }
        public ulong TotalRam { get; private set; }
        public ulong AvailableRam { get; private set; }
        public event EventHandler MetricsUpdated;

        public SystemMetricsService(ILoggingService logger)
        {
            _logger = logger;
            TotalRam = GetTotalPhysicalMemory();
        }

        public void Start()
        {
            if(_isDisposed) return;
            VoltrisOptimizer.Core.SystemMetricsCache.Instance.MetricsUpdated += OnCacheUpdated;
            // Forçar primeira atualização imediata
            UpdateMetricsFromCache();
            _logger?.LogInfo("[MetricsService] Serviço de métricas iniciado (via Cache Unificado)");
        }

        public void Stop()
        {
            VoltrisOptimizer.Core.SystemMetricsCache.Instance.MetricsUpdated -= OnCacheUpdated;
            _logger?.LogInfo("[MetricsService] Serviço de métricas parado");
        }

        private void OnCacheUpdated(object? sender, EventArgs e)
        {
            UpdateMetricsFromCache();
        }

        private void UpdateMetricsFromCache()
        {
            if(_isDisposed) return;
            try
            {
                var cache = VoltrisOptimizer.Core.SystemMetricsCache.Instance;
                
                CpuUsage = cache.CpuPercent;
                RamUsage = cache.MemoryUsedPercent;
                DiskUsage = cache.DiskUsagePercent;
                CpuTemperature = cache.CpuTemperature > 0 ? cache.CpuTemperature : (double?)null;
                
                // Conversão de Mb para Bytes para manter compatibilidade
                AvailableRam = (ulong)(cache.AvailableRamMb * 1024 * 1024);
                
                RaiseMetricsUpdated();
            }
            catch(Exception ex)
            {
                _logger?.LogDebug($"[MetricsService] Erro na atualização: {ex.Message}");
            }
        }

        // [FIX:A-5] BUG ORIGINAL: MetricsUpdated era invocado direto no thread
        // produtor (pool do timer do SystemMetricsCache), sem passar pelo
        // Dispatcher. Qualquer assinante WPF que tocasse um DependencyObject
        // recebia InvalidOperationException "chamada de thread diferente".
        //
        // EVIDENCIA (2026-09-27): 13x InvalidOperationException first-chance em
        // 6,4s, coincidindo com a construcao do DashboardViewModel.
        //
        // A leitura dos valores continua no thread produtor (properties
        // primitivas do cache, sem dependencia de UI); apenas a NOTIFICACAO e
        // despachada, com coalescing para nao saturar a fila do Dispatcher.
        private void RaiseMetricsUpdated()
        {
            var dispatcher = ResolveUiDispatcher();

            // Sem Application (contexto nao-WPF) ou ja estamos na thread da UI:
            // comportamento original, invoke direto.
            if (dispatcher is null || dispatcher.CheckAccess())
            {
                MetricsUpdated?.Invoke(this, EventArgs.Empty);
                return;
            }

            int producerThread = Environment.CurrentManagedThreadId;

            // Coalescing: ja existe um BeginInvoke em voo. Como a proxima
            // execucao le o cache de novo, ela carrega o valor mais recente,
            // entao descartar esta nao perde informacao e evita a fila crescer.
            if (Interlocked.Exchange(ref _raiseInFlight, 1) == 1)
                return;

            dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                try
                {
                    MetricsUpdated?.Invoke(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(
                        $"[FIX:A-5] Excecao em assinante de MetricsUpdated no thread da UI: {ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    Interlocked.Exchange(ref _raiseInFlight, 0);
                }
            }));

            if (!_loggedThreadHop)
            {
                _loggedThreadHop = true;
                _logger?.LogInfo(
                    $"[FIX:A-5] MetricsUpdated despachado para o Dispatcher | " +
                    $"produtor=Thread{producerThread} -> ui=Thread{dispatcher.Thread.ManagedThreadId} | " +
                    $"modo=coalescing(1-em-voo)");
            }
        }

        private System.Windows.Threading.Dispatcher? ResolveUiDispatcher()
        {
            if (_uiDispatcher is not null)
                return _uiDispatcher;

            var d = System.Windows.Application.Current?.Dispatcher;
            if (d is not null)
                _uiDispatcher = d;
            return _uiDispatcher;
        }

        private ulong GetTotalPhysicalMemory()
        {
            var cache = VoltrisOptimizer.Core.SystemMetricsCache.Instance;
            if (cache.Hardware.TotalRamGb > 0) return (ulong)(cache.Hardware.TotalRamGb * 1024 * 1024 * 1024);
            return 16UL * 1024 * 1024 * 1024;
        }

        public void Dispose()
        {
            if(_isDisposed) return;
            _isDisposed = true;
            Stop();
        }
    }
}
