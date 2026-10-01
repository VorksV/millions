using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Optimization.Unification;

namespace VoltrisOptimizer.Services.Optimization.Features
{
    public class AdaptiveExplorerOptimizer : IDisposable
    {
        private readonly ILoggingService _logger;
        private readonly OptimizationOrchestrator _orchestrator;
        private CancellationTokenSource? _cts;
        private Task? _monitorTask;
        
        // Removed duplicated P/Invokes

        public AdaptiveExplorerOptimizer(ILoggingService logger, OptimizationOrchestrator orchestrator)
        {
            _logger = logger;
            _orchestrator = orchestrator;
        }

        public void Start()
        {
            if (_cts != null) return;
            
            _cts = new CancellationTokenSource();
            _monitorTask = Task.Run(() => MonitorExplorerAsync(_cts.Token), _cts.Token);
            _logger.LogInfo("[AdaptiveExplorer] Otimizador de inicialização Explorer ATIVADO.");
        }

        public void Stop()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        private async Task MonitorExplorerAsync(CancellationToken ct)
        {
            int lastForegroundPid = 0;
            
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(1000, ct);

                    int pid = VoltrisOptimizer.Core.ForegroundWindowTracker.Instance.CurrentPid;
                    if (pid <= 0 || pid == lastForegroundPid) continue;

                    lastForegroundPid = pid;

                    try
                    {
                        var process = Process.GetProcessById((int)pid);
                        if (process.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase))
                        {
                            ApplyIoPriority(process);
                        }
                    }
                    catch { }
                }
                catch (OperationCanceledException) { break; }
                catch { }
            }
        }

        private void ApplyIoPriority(Process process)
        {
            // OTIMIZACAO EXPLORER: Como o explorer lida com disco e UI pesada,
            // Ao ganhar foco ele ganha prioridade temporária (ou IoPriority)
            try
            {
                if (process.PriorityClass != ProcessPriorityClass.High)
                {
                    var origPri = process.PriorityClass;
                    process.PriorityClass = ProcessPriorityClass.High;

                    // Release safe após InputIdle ou Timeout de interface
                    Task.Run(async () =>
                    {
                        try 
                        {
                            await Task.Delay(1000); 
                            if (!process.HasExited && process.PriorityClass == ProcessPriorityClass.High)
                            {
                                process.PriorityClass = origPri;
                            }
                        } catch { } // Processo ja morreu ou perdeu handle
                    });
                }
            }
            catch { }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
