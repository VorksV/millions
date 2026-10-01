using VoltrisOptimizer.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Performance
{
    /// <summary>
    /// Coletor de métricas de performance para validação de ganhos reais
    /// CORREÇÃO ENTERPRISE: Prova científica de valor
    /// </summary>
    public class PerformanceMetricsCollector : IDisposable
    {
        private readonly ILoggingService _logger;
        private CancellationTokenSource? _monitoringCts;
        private Task? _monitoringTask;
        
        private readonly List<PerformanceSnapshot> _snapshots = new();
        private readonly object _snapshotsLock = new();
        
        private SafePerformanceCounter? _fpsCounter;
        private SafePerformanceCounter? _frameTimeCounter;
        private Process? _targetProcess;
        
        public event EventHandler<PerformanceSnapshot>? SnapshotCaptured;
        
        public bool IsMonitoring => _monitoringTask != null && !_monitoringTask.IsCompleted;
        
        public PerformanceMetricsCollector(ILoggingService logger)
        {
            _logger = logger;
        }
        
        /// <summary>
        /// Inicia monitoramento de FPS para processo específico
        /// </summary>
        public async Task StartMonitoringAsync(string processName)
        {
            await Task.CompletedTask;
            if (IsMonitoring) return;
            
            try
            {
                _logger.LogInfo($"[PerfMetrics] Iniciando monitoramento de {processName}");
                
                // Encontrar processo
                var processes = Process.GetProcessesByName(processName);
                if (processes.Length == 0)
                {
                    _logger.LogWarning($"[PerfMetrics] Processo {processName} não encontrado");
                    return;
                }
                
                _targetProcess = processes[0];
                
                // Inicializar contadores
                InitializeCounters();
                
                // Iniciar loop de coleta
                _monitoringCts = new CancellationTokenSource();
                _monitoringTask = Task.Run(() => MonitoringLoopAsync(_monitoringCts.Token));
                
                _logger.LogSuccess($"[PerfMetrics] Monitoramento iniciado para {processName}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[PerfMetrics] Erro ao iniciar monitoramento: {ex.Message}", ex);
            }
        }
        
        /// <summary>
        /// Para monitoramento
        /// </summary>
        public async Task StopMonitoringAsync()
        {
            if (!IsMonitoring) return;
            
            _monitoringCts?.Cancel();
            if (_monitoringTask != null)
            {
                try { await _monitoringTask; } catch { }
            }
            
            _monitoringCts?.Dispose();
            _monitoringCts = null;
            _monitoringTask = null;
            
            _logger.LogInfo("[PerfMetrics] Monitoramento parado");
        }
        
        private void InitializeCounters()
        {
            try
            {
                // Nota: FPS não tem contador direto no Windows
                // Vamos usar heurística baseada em frame time
                // Em produção, integrar com RTSS/PresentMon para FPS real
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[PerfMetrics] Falha ao inicializar contadores: {ex.Message}");
            }
        }
        
        private async Task MonitoringLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var snapshot = await CaptureSnapshotAsync();
                    
                    if (snapshot != null)
                    {
                        lock (_snapshotsLock)
                        {
                            _snapshots.Add(snapshot);
                            
                            // Manter apenas últimos 1000 snapshots (aprox 16min a 1s/snapshot)
                            if (_snapshots.Count > 1000)
                            {
                                _snapshots.RemoveAt(0);
                            }
                        }
                        
                        SnapshotCaptured?.Invoke(this, snapshot);
                    }
                    
                    await Task.Delay(1000, cancellationToken); // 1 snapshot/segundo
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogError($"[PerfMetrics] Erro no loop: {ex.Message}");
                    await Task.Delay(5000, cancellationToken);
                }
            }
        }
        
        private async Task<PerformanceSnapshot?> CaptureSnapshotAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (_targetProcess == null || _targetProcess.HasExited)
                        return null;
                    
                    _targetProcess.Refresh();
                    
                    // Capturar métricas básicas
                    var snapshot = new PerformanceSnapshot
                    {
                        Timestamp = DateTime.UtcNow,
                        ProcessName = _targetProcess.ProcessName,
                        CpuUsagePercent = GetProcessCpuUsage(_targetProcess),
                        MemoryUsageMB = _targetProcess.WorkingSet64 / 1024.0 / 1024.0,
                        ThreadCount = _targetProcess.Threads.Count,
                        HandleCount = _targetProcess.HandleCount
                    };
                    
                    // FPS estimado (placeholder - integrar com RTSS/PresentMon)
                    snapshot.EstimatedFPS = EstimateFPS();
                    
                    return snapshot;
                }
                catch
                {
                    return null;
                }
            });
        }
        
        private double GetProcessCpuUsage(Process process)
        {
            try
            {
                // Heurística simples - em produção usar SafePerformanceCounter específico
                var startTime = DateTime.UtcNow;
                var startCpuUsage = process.TotalProcessorTime;
                
                Thread.Sleep(100);
                
                var endTime = DateTime.UtcNow;
                var endCpuUsage = process.TotalProcessorTime;
                
                var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
                var totalMsPassed = (endTime - startTime).TotalMilliseconds;
                var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);
                
                return cpuUsageTotal * 100;
            }
            catch
            {
                return 0;
            }
        }
        
        private double EstimateFPS()
        {
            // Placeholder: Integrar com RTSS SDK ou PresentMon
            // Por enquanto retorna 0 para indicar "não disponível"
            return 0;
        }
        
        /// <summary>
        /// Gera relatório de comparação antes/depois
        /// </summary>
        public PerformanceComparisonReport GenerateComparisonReport(DateTime beforeTimestamp, DateTime afterTimestamp)
        {
            lock (_snapshotsLock)
            {
                var beforeSnapshots = _snapshots.Where(s => s.Timestamp < beforeTimestamp).ToList();
                var afterSnapshots = _snapshots.Where(s => s.Timestamp >= afterTimestamp).ToList();
                
                if (beforeSnapshots.Count == 0 || afterSnapshots.Count == 0)
                {
                    return new PerformanceComparisonReport
                    {
                        IsValid = false,
                        Message = "Dados insuficientes para comparação"
                    };
                }
                
                var report = new PerformanceComparisonReport
                {
                    IsValid = true,
                    BeforePeriod = new PerformanceStats
                    {
                        AverageFPS = beforeSnapshots.Average(s => s.EstimatedFPS),
                        AverageCpuUsage = beforeSnapshots.Average(s => s.CpuUsagePercent),
                        AverageMemoryMB = beforeSnapshots.Average(s => s.MemoryUsageMB),
                        SampleCount = beforeSnapshots.Count
                    },
                    AfterPeriod = new PerformanceStats
                    {
                        AverageFPS = afterSnapshots.Average(s => s.EstimatedFPS),
                        AverageCpuUsage = afterSnapshots.Average(s => s.CpuUsagePercent),
                        AverageMemoryMB = afterSnapshots.Average(s => s.MemoryUsageMB),
                        SampleCount = afterSnapshots.Count
                    }
                };
                
                // Calcular ganhos
                report.FPSGainPercent = CalculateGainPercent(report.BeforePeriod.AverageFPS, report.AfterPeriod.AverageFPS);
                report.CpuReductionPercent = CalculateGainPercent(report.BeforePeriod.AverageCpuUsage, report.AfterPeriod.AverageCpuUsage);
                
                return report;
            }
        }
        
        private double CalculateGainPercent(double before, double after)
        {
            if (before == 0) return 0;
            return ((after - before) / before) * 100;
        }
        
        public void Dispose()
        {
            _ = StopMonitoringAsync();
            _fpsCounter?.Dispose();
            _frameTimeCounter?.Dispose();
        }
    }
    
    /// <summary>
    /// Snapshot de performance em um momento específico
    /// </summary>
    public class PerformanceSnapshot
    {
        public DateTime Timestamp { get; set; }
        public string ProcessName { get; set; } = "";
        public double EstimatedFPS { get; set; }
        public double CpuUsagePercent { get; set; }
        public double MemoryUsageMB { get; set; }
        public int ThreadCount { get; set; }
        public int HandleCount { get; set; }
    }
    
    /// <summary>
    /// Estatísticas de performance agregadas
    /// </summary>
    public class PerformanceStats
    {
        public double AverageFPS { get; set; }
        public double AverageCpuUsage { get; set; }
        public double AverageMemoryMB { get; set; }
        public int SampleCount { get; set; }
    }
    
    /// <summary>
    /// Relatório de comparação de performance
    /// </summary>
    public class PerformanceComparisonReport
    {
        public bool IsValid { get; set; }
        public string Message { get; set; } = "";
        public PerformanceStats BeforePeriod { get; set; } = new();
        public PerformanceStats AfterPeriod { get; set; } = new();
        public double FPSGainPercent { get; set; }
        public double CpuReductionPercent { get; set; }
        
        public string GetSummary()
        {
            if (!IsValid) return Message;
            
            return $"FPS: {FPSGainPercent:+0.0;-0.0}% | CPU: {CpuReductionPercent:+0.0;-0.0}%";
        }
    }
}

