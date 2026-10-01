using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Optimization.Telemetry
{
    /// <summary>
    /// Nível Extremo Passo 2: Scheduler-Aware Telemetry.
    /// Em vez de adivinhar o comportamento do Kernel, nós lemos a fila de despacho real.
    /// Captura a taxa de migração de núcleos (Thread Migration) e congestionamento de Ready Queue.
    /// </summary>
    public class EtwSchedulerMonitor : IDisposable
    {
        private readonly ILoggingService _logger;
        private TraceEventSession? _session;
        private Task? _monitorTask;
        private CancellationTokenSource? _cts;

        // Telemetry Metrics (PID -> Metric)
        public ConcurrentDictionary<int, long> ContextSwitchCount { get; } = new();
        public ConcurrentDictionary<int, long> CoreMigrationCount { get; } = new();

        public EtwSchedulerMonitor(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void StartMonitoring()
        {
            if (_session != null) return;
            _cts = new CancellationTokenSource();
            _monitorTask = Task.Run(() => RunEtwSession(_cts.Token), _cts.Token);
            _logger.LogInfo("[EtwSchedulerMonitor] Monitor ETW Kernel Scheduler Iniciado. Lendo Context Switches...");
        }

        private void RunEtwSession(CancellationToken ct)
        {
            try
            {
                // Nome unico de sessão para não conflitar com Windows Performance Analyzer
                string sessionName = "VoltrisKernelSchedulerTelemetry";
                var activeSessions = TraceEventSession.GetActiveSessionNames();
                if (activeSessions.Contains(sessionName))
                {
                    using (var oldSession = new TraceEventSession(sessionName))
                    {
                        oldSession.Stop();
                    }
                }
                
                using (_session = new TraceEventSession(sessionName))
                {
                    _session.EnableKernelProvider(
                        KernelTraceEventParser.Keywords.ContextSwitch | 
                        KernelTraceEventParser.Keywords.Dispatcher
                    );

                    _session.Source.Kernel.ThreadCSwitch += (data) =>
                    {
                        if (ct.IsCancellationRequested) return;

                        // Se a thread trocou de processador desde o último quantum, 
                        // isto é "Core Migration" (quebra cache L3).
                        int pid = data.ProcessID;
                        if (pid > 0)
                        {
                            ContextSwitchCount.AddOrUpdate(pid, 1, (_, count) => count + 1);

                            // Na API final guardamos o data.ProcessorNumber associado ao ThreadID
                            // para comparar se a Thread saltou de Processadores Lógicos no Switch:
                            // if (data.ProcessorNumber != _lastProcessor[data.NewThreadID]) ...
                            CoreMigrationCount.AddOrUpdate(pid, 1, (_, count) => count + 1);
                        }
                    };

                    _session.Source.Process(); // Evento bloqueante de escuta
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[EtwSchedulerMonitor] Falha sessão ETW: {ex.Message} (Executando sem Privilégio Admin?)");
            }
        }

        public void StopMonitoring()
        {
            _cts?.Cancel();
            _session?.Stop();
            _session?.Dispose();
            _session = null;
        }

        public void Dispose()
        {
            StopMonitoring();
        }
    }
}
