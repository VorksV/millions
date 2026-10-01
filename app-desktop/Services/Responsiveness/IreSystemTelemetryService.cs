using VoltrisOptimizer.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using VoltrisOptimizer.Core;

namespace VoltrisOptimizer.Services.Responsiveness
{
    /// <summary>
    /// Coleta telemetria do sistema para o IRE v2.
    /// 
    /// PERFORMANCE FIX: Removidos 3 SafePerformanceCounter (CPU total, DiskQueue, DiskActive).
    /// Agora lê diretamente do SystemMetricsCache centralizado (zero overhead extra).
    /// CPU por processo mantido via TotalProcessorTime (mais leve que PDH).
    /// </summary>
    internal sealed class IreSystemTelemetryService : IDisposable
    {
        private readonly ILoggingService _logger;

        // CPU por processo: PID → último TotalProcessorTime
        private readonly ConcurrentDictionary<int, TimeSpan> _prevCpuTimes = new();
        private DateTime _lastSampleTime = DateTime.MinValue;
        private readonly ProcessCacheService _processCache;

        private bool _disposed;

        public IreSystemTelemetryService(ILoggingService logger, ProcessCacheService processCache)
        {
            _logger = logger;
            _processCache = processCache;
        }

        /// <summary>
        /// Inicializa. Sem PerformanceCounters — usa SystemMetricsCache.
        /// </summary>
        public void Initialize()
        {
            // Nada a inicializar — SystemMetricsCache já está ativo
            _logger.LogDebug("[IRE] Telemetry inicializada via SystemMetricsCache (zero PDH).", "IRE");
        }

        public SystemTelemetrySnapshot Collect(System.Diagnostics.Process[] processes)
        {
            var cache = SystemMetricsCache.Instance;
            
            var snapshot = new SystemTelemetrySnapshot
            {
                // Lê do cache compartilhado — zero syscall extra
                CpuTotalPercent  = cache.CpuPercent,
                DiskQueueLength  = cache.DiskQueueLength,
                DiskActivePercent = 0, // Não crítico para IRE — removido para poupar CPU
                ForegroundPid    = GetForegroundPid(),
                LastInputMs      = cache.LastInputMs
            };

            // OTIMIZAÇÃO: Usar o cache nativo de processos em vez de iterar sobre o array pesado de Process[]
            snapshot.ProcessSamples = CollectProcessSamplesFromCache(snapshot.ForegroundPid);
            return snapshot;
        }

        private IReadOnlyList<ProcessCpuSample> CollectProcessSamplesFromCache(int foregroundPid)
        {
            var now = DateTime.UtcNow;
            double intervalMs = _lastSampleTime == DateTime.MinValue
                ? 3000
                : Math.Max(100, (now - _lastSampleTime).TotalMilliseconds);
            _lastSampleTime = now;

            var cachedInfos = _processCache.GetCachedProcessInfos();
            var samples = new List<ProcessCpuSample>();
            int cores = Math.Max(1, Environment.ProcessorCount);

            foreach (var info in cachedInfos)
            {
                try
                {
                    // PERFORMANCE: Usando métricas pré-calculadas enviadas pelo ProcessCache (Zero Syscalls)
                    double cpuPercent = info.CpuUsage;

                    // Apenas adicionar ao snapshot se tiver algum impacto ou for o foreground
                    if (cpuPercent > 0.5 || info.Id == foregroundPid)
                    {
                        samples.Add(new ProcessCpuSample
                        {
                            Pid = info.Id,
                            Name = info.ProcessName,
                            CpuPercent = cpuPercent,
                            WorkingSetBytes = info.WorkingSet64,
                            IsForeground = info.Id == foregroundPid
                        });
                    }
                }
                catch (Exception ex) { _logger?.LogWarning($"[IreSystemTelemetry] Erro ao coletar amostra de processo: {ex.Message}"); }
            }

            // Cleanup O(1) de processos mortos via dicionário é manejado naturalmente pela rotatividade do cache
            return samples;
        }

        private int GetForegroundPid()
        {
            try
            {
                int pid = VoltrisOptimizer.Core.ForegroundWindowTracker.Instance.CurrentPid;
                return pid > 0 ? pid : 0;
            }
            catch (Exception ex) { _logger?.LogWarning($"[IreSystemTelemetry] Erro ao obter foreground PID: {ex.Message}"); return 0; }
        }

        private IReadOnlyList<ProcessCpuSample> CollectProcessSamples(System.Diagnostics.Process[] processes, int foregroundPid)
        {
            var now = DateTime.UtcNow;
            double intervalMs = _lastSampleTime == DateTime.MinValue
                ? 3000
                : Math.Max(100, (now - _lastSampleTime).TotalMilliseconds);
            _lastSampleTime = now;

            var samples = new List<ProcessCpuSample>(processes.Length);
            int cores = Math.Max(1, Environment.ProcessorCount);

            foreach (var p in processes)
            {
                try
                {
                    if (p.HasExited) continue;

                    double cpuPercent = 0;
                    var cpuNow = p.TotalProcessorTime;

                    if (_prevCpuTimes.TryGetValue(p.Id, out var prev))
                    {
                        var delta = cpuNow - prev;
                        cpuPercent = Math.Max(0, Math.Min(100,
                            (delta.TotalMilliseconds / intervalMs) * 100.0 / cores));
                    }
                    _prevCpuTimes[p.Id] = cpuNow;

                    samples.Add(new ProcessCpuSample
                    {
                        Pid = p.Id,
                        Name = p.ProcessName,
                        CpuPercent = cpuPercent,
                        WorkingSetBytes = p.WorkingSet64,
                        IsForeground = p.Id == foregroundPid
                    });
                }
                catch (InvalidOperationException) { _logger?.LogWarning($"[IreSystemTelemetry] Processo {p.Id} encerrado durante coleta"); }
                catch (Exception ex) { _logger?.LogWarning($"[IreSystemTelemetry] Erro ao processar CPU do processo {p.Id}: {ex.Message}"); }
            }

            // Cleanup de PIDs que não existem mais — usa HashSet para O(n)
            var activePids = new HashSet<int>(processes.Length);
            foreach (var p in processes) activePids.Add(p.Id);
            foreach (var key in _prevCpuTimes.Keys)
            {
                if (!activePids.Contains(key))
                    _prevCpuTimes.TryRemove(key, out _);
            }

            return samples;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            // Sem PerformanceCounters para descartar
        }

        #region Win32

        #endregion
    }
}

