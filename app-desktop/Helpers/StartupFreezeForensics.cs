using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using VoltrisOptimizer.Services;

#nullable enable

namespace VoltrisOptimizer.Helpers
{
    /// <summary>
    /// FORENSE DE CONGELAMENTO NA INICIALIZAÇÃO
    /// 
    /// Detecta EXATAMENTE o que está bloqueando a UI thread quando o app congela.
    /// Diferente do UIThreadMonitor (que só detecta SE houve freeze),
    /// esta classe identifica O QUÊ causou o freeze por:
    /// 
    /// 1. Rastrear TODAS as operações que entram na UI thread (Dispatcher queue)
    /// 2. Correlacionar freezes com o StartupStepTracker (qual passo estava ativo)
    /// 3. Detectar padrões de deadlock: background thread esperando Dispatcher.Invoke
    ///    enquanto UI thread está bloqueada por outro Invoke/Wait
    /// 4. Gerar relatório forense com timeline precisa
    /// </summary>
    public sealed class StartupFreezeForensics : IDisposable
    {
        private readonly ILoggingService? _logger;
        private readonly CancellationTokenSource _cts = new();
        private readonly Stopwatch _globalSw = Stopwatch.StartNew();
        private readonly Stopwatch _startupStopwatch = new();
        private readonly ConcurrentQueue<DispatcherOperation> _trackedOps = new();
        private readonly ConcurrentDictionary<string, long> _operationTimestamps = new();
        private readonly string _logPath;
        private volatile bool _disposed;
        private volatile bool _isMonitoring;
        private readonly long _startupGracePeriodMs = 60_000;
        private long _monitoringStartTick;
        private volatile bool _startupStopwatchStarted;
        private Thread? _uiThread;
        private int _uiThreadNativeId;
        private int _freezeCount;
        
        // Rastrear operações que estão executando na UI thread
        private volatile string? _currentUIOperation;
        private long _currentUIOperationStartMs;

        public static StartupFreezeForensics? Instance { get; private set; }

        public StartupFreezeForensics(ILoggingService? logger)
        {
            _logger = logger;
            var logDir = LogDirectoryResolver.Resolve();
            Directory.CreateDirectory(logDir);
            _logPath = Path.Combine(logDir, $"FreezeForensics_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");
            Instance = this;

            WriteLog("╔══════════════════════════════════════════════════════════╗");
            WriteLog("║       STARTUP FREEZE FORENSICS — INITIALIZED            ║");
            WriteLog($"║  PID: {Environment.ProcessId}  Arch: {(IntPtr.Size == 8 ? "x64" : "x86")}");
            WriteLog($"║  Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            WriteLog("╚══════════════════════════════════════════════════════════╝");
        }

        /// <summary>
        /// Inicia o monitoramento forense. Deve ser chamado da UI thread.
        /// </summary>
        public void Start()
        {
            if (_isMonitoring) return;
            _isMonitoring = true;
            _monitoringStartTick = Environment.TickCount64;

            // Capturar referência da UI thread
            _uiThread = Thread.CurrentThread;
            _uiThreadNativeId = GetCurrentNativeThreadId();
            WriteLog($"[FORENSICS] UI Thread capturada: ManagedId={_uiThread.ManagedThreadId}, NativeId={_uiThreadNativeId}");
            WriteLog($"[FORENSICS] Grace period de {_startupGracePeriodMs / 1000}s ativo — reports suprimidos durante startup");

            // Iniciar watchdog em task assíncrona (substitui thread dedicada)
            var watchdogTask = Task.Run(() => WatchdogLoopAsync(_cts.Token));
        }

        public void MarkMainWindowLoaded()
        {
            if (_startupStopwatchStarted) return;
            _startupStopwatchStarted = true;
            _startupStopwatch.Restart();
            WriteLog($"[TIMELINE][{_globalSw.ElapsedMilliseconds:D6}ms] MainWindow_Loaded — iniciando timer de startup até Dashboard.Loaded");
        }

        public void MarkDashboardLoaded()
        {
            if (!_startupStopwatchStarted) return;
            _startupStopwatch.Stop();
            var elapsed = _startupStopwatch.ElapsedMilliseconds;
            WriteLog($"[TIMELINE][{_globalSw.ElapsedMilliseconds:D6}ms] Dashboard.Loaded — startup UI flow completed in {elapsed}ms");

            if (elapsed > 500)
            {
                WriteLog($"╔══════════════════════════════════════════════════════════╗");
                WriteLog($"║  ⛔ FREEZE DETECTED: Dashboard startup took {elapsed}ms (>500ms)");
                WriteLog($"║  Timestamp: {DateTime.Now:HH:mm:ss.fff}");
                WriteLog($"╚══════════════════════════════════════════════════════════╝");
                Interlocked.Increment(ref _freezeCount);
            }
            else
            {
                WriteLog($"[TIMELINE] Dashboard startup OK: {elapsed}ms");
            }
        }

        /// <summary>
        /// Rastreia uma operação que vai executar na UI thread.
        /// Chamar ANTES de executar algo pesado no Dispatcher.
        /// </summary>
        public void TrackOperation(string operationName)
        {
            if (_disposed) return;
            _operationTimestamps[operationName] = _globalSw.ElapsedMilliseconds;
            WriteLog($"[TRACK][{_globalSw.ElapsedMilliseconds:D6}ms][Thread:{Thread.CurrentThread.ManagedThreadId:D2}] ENQUEUE: {operationName}");
        }

        /// <summary>
        /// Marca o início da execução de uma operação na UI thread.
        /// Chamar DENTRO do callback do Dispatcher.
        /// </summary>
        public void BeginUIOperation(string operationName)
        {
            if (_disposed) return;
            _currentUIOperation = operationName;
            _currentUIOperationStartMs = _globalSw.ElapsedMilliseconds;
            WriteLog($"[UI-OP][{_globalSw.ElapsedMilliseconds:D6}ms][Thread:{Thread.CurrentThread.ManagedThreadId:D2}] BEGIN: {operationName}");
        }

        /// <summary>
        /// Marca o fim da execução de uma operação na UI thread.
        /// </summary>
        public void EndUIOperation(string operationName)
        {
            if (_disposed) return;
            var duration = _globalSw.ElapsedMilliseconds - _currentUIOperationStartMs;
            _currentUIOperation = null;
            
            string severity = duration > 500 ? " ⚠️ SLOW" : duration > 100 ? " ⏱️" : "";
            WriteLog($"[UI-OP][{_globalSw.ElapsedMilliseconds:D6}ms][Thread:{Thread.CurrentThread.ManagedThreadId:D2}] END: {operationName} | {duration}ms{severity}");
        }

        /// <summary>
        /// Loop de watchdog que verifica se a UI thread está responsiva.
        /// Executa em thread dedicada com prioridade alta.
        /// </summary>
        private void WatchdogLoop()
        {
            WriteLog("[WATCHDOG] Watchdog thread iniciada");
            var sw = new Stopwatch();

            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    Thread.Sleep(2000); // Check a cada 2s (reduz overhead)
                    if (_cts.Token.IsCancellationRequested) break;

                    sw.Restart();
                    bool responded = false;

                    var dispatcher = Application.Current?.Dispatcher;
                    if (dispatcher == null) break;

                    var operation = dispatcher.BeginInvoke(DispatcherPriority.Send, (Action)(() =>
                    {
                        responded = true;
                    }));

                    // Usar timeout de 800ms (abaixo do threshold de "freeze perceptível")
                    var status = operation.Wait(TimeSpan.FromMilliseconds(2000));
                    sw.Stop();

                    if (status != DispatcherOperationStatus.Completed && sw.ElapsedMilliseconds >= 2000)
                    {
                        if (Environment.TickCount64 - _monitoringStartTick < _startupGracePeriodMs)
                            continue;
                        // ⚡ FREEZE DETECTADO — Capturar tudo
                        Interlocked.Increment(ref _freezeCount);
                        CaptureForensicSnapshot(sw.ElapsedMilliseconds);
                    }
                }
                catch (TaskCanceledException) { break; }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    WriteLog($"[WATCHDOG] Erro no loop: {ex.Message}");
                }
            }

            WriteLog("[WATCHDOG] Watchdog thread finalizada");
        }

        /// <summary>
        /// Captura um snapshot forense completo no momento do freeze.
        /// </summary>

        // ==========================
        // Async watchdog (replaces thread)
        // ==========================
        private async Task WatchdogLoopAsync(CancellationToken token)
        {
            WriteLog("[WATCHDOG] Watchdog thread iniciada (async)");
            var sw = new Stopwatch();

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(2000, token); // Check a cada 2s (reduz overhead)
                    if (token.IsCancellationRequested) break;

                    sw.Restart();
                    bool responded = false;

                    var dispatcher = Application.Current?.Dispatcher;
                    if (dispatcher == null) break;

                    var operation = dispatcher.BeginInvoke(DispatcherPriority.Send, (Action)(() =>
                    {
                        responded = true;
                    }));

                    // Usar timeout de 800ms (abaixo do threshold de "freeze perceptível")
                    var status = operation.Wait(TimeSpan.FromMilliseconds(2000));
                    sw.Stop();

                    if (status != DispatcherOperationStatus.Completed && sw.ElapsedMilliseconds >= 2000)
                    {
                        if (Environment.TickCount64 - _monitoringStartTick < _startupGracePeriodMs)
                            continue;
                        // ❗ FREEZE DETECTADO – Capturar tudo
                        Interlocked.Increment(ref _freezeCount);
                        CaptureForensicSnapshot(sw.ElapsedMilliseconds);
                    }
                }
                catch (TaskCanceledException) { break; }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    WriteLog($"[WATCHDOG] Erro no loop: {ex.Message}");
                }
            }

            WriteLog("[WATCHDOG] Watchdog thread finalizada (async)");
        }

        private void CaptureForensicSnapshot(long freezeDurationMs)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine();
                sb.AppendLine("╔════════════════════════════════════════════════════════════════╗");
                sb.AppendLine($"║  ⛔ FREEZE #{_freezeCount} DETECTED — Duration: {freezeDurationMs}ms");
                sb.AppendLine($"║  Timestamp: {DateTime.Now:HH:mm:ss.fff}");
                sb.AppendLine($"║  Boot Time: {_globalSw.ElapsedMilliseconds}ms since app start");
                sb.AppendLine("╠════════════════════════════════════════════════════════════════╣");

                // 1. O que a UI thread estava fazendo?
                var currentOp = _currentUIOperation;
                var opDuration = currentOp != null ? _globalSw.ElapsedMilliseconds - _currentUIOperationStartMs : 0;
                sb.AppendLine($"║  🎯 UI Thread Operation: {currentOp ?? "UNKNOWN/NONE"}");
                if (currentOp != null)
                    sb.AppendLine($"║     Running for: {opDuration}ms");

                // 2. Quais passos de startup estão ativos?
                sb.AppendLine("║");
                sb.AppendLine("║  📋 Active Startup Steps (from StartupStepTracker):");
                var tracker = StartupStepTracker.Instance;
                if (tracker != null)
                {
                    // Acessar via reflection (campo privado _steps)
                    try
                    {
                        var stepsField = typeof(StartupStepTracker).GetField("_steps",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (stepsField?.GetValue(tracker) is ConcurrentDictionary<string, object> steps)
                        {
                            foreach (var kvp in steps)
                            {
                                var entry = kvp.Value;
                                var statusProp = entry.GetType().GetProperty("Status");
                                var nameProp = entry.GetType().GetProperty("Name");
                                var startMsProp = entry.GetType().GetProperty("StartMs");
                                var threadIdProp = entry.GetType().GetProperty("ThreadId");
                                
                                var status = statusProp?.GetValue(entry)?.ToString();
                                if (status == "Running")
                                {
                                    var name = nameProp?.GetValue(entry)?.ToString() ?? "?";
                                    var startMs = startMsProp?.GetValue(entry) ?? 0;
                                    var tid = threadIdProp?.GetValue(entry) ?? 0;
                                    sb.AppendLine($"║     🔴 {name} (running for {_globalSw.ElapsedMilliseconds - (long)startMs}ms on thread {tid})");
                                }
                            }
                        }
                    }
                    catch
                    {
                        sb.AppendLine("║     (Reflection access failed — check manually)");
                    }
                }
                else
                {
                    sb.AppendLine("║     (StartupStepTracker not available)");
                }

                // 3. Thread Dump completo
                sb.AppendLine("║");
                sb.AppendLine("║  🧵 Thread Analysis:");
                var proc = Process.GetCurrentProcess();
                sb.AppendLine($"║     Process: {proc.ProcessName} (PID: {proc.Id})");
                sb.AppendLine($"║     Responding: {proc.Responding}");
                sb.AppendLine($"║     Working Set: {proc.WorkingSet64 / 1024 / 1024}MB");
                sb.AppendLine($"║     Total Threads: {proc.Threads.Count}");

                // UI Thread específica
                if (_uiThread != null)
                {
                    sb.AppendLine($"║     UI Thread State: {_uiThread.ThreadState}");
                }

                // Top 15 threads por CPU
                sb.AppendLine("║");
                sb.AppendLine("║     Top Threads by CPU:");
                var threads = new System.Collections.Generic.List<ProcessThread>();
                foreach (ProcessThread pt in proc.Threads) threads.Add(pt);

                var topThreads = threads
                    .OrderByDescending(t => { try { return t.TotalProcessorTime.TotalMilliseconds; } catch { return 0; } })
                    .Take(15);

                foreach (var pt in topThreads)
                {
                    try
                    {
                        string marker = pt.Id == _uiThreadNativeId ? " ◀◀ UI THREAD" : "";
                        string waitInfo = "";
                        if (pt.ThreadState == System.Diagnostics.ThreadState.Wait)
                        {
                            try { waitInfo = $" Wait:{pt.WaitReason}"; } catch { }
                        }

                        sb.AppendLine($"║       TID:{pt.Id,-6} State:{pt.ThreadState,-10}{waitInfo} CPU:{pt.TotalProcessorTime.TotalMilliseconds:F0}ms{marker}");
                    }
                    catch { }
                }

                // 4. Operações pendentes na fila
                sb.AppendLine("║");
                sb.AppendLine("║  📨 Pending Dispatcher Operations:");
                var pendingOps = _operationTimestamps
                    .Where(kvp => !kvp.Key.StartsWith("DONE:"))
                    .OrderBy(kvp => kvp.Value)
                    .ToList();
                
                if (pendingOps.Any())
                {
                    foreach (var op in pendingOps)
                    {
                        var age = _globalSw.ElapsedMilliseconds - op.Value;
                        sb.AppendLine($"║     📌 {op.Key} (queued {age}ms ago)");
                    }
                }
                else
                {
                    sb.AppendLine("║     (No tracked pending operations)");
                }

                // 5. Recomendação automática baseada nas evidências
                sb.AppendLine("║");
                sb.AppendLine("║  💡 Auto-Diagnosis:");
                
                if (_uiThread?.ThreadState == System.Threading.ThreadState.WaitSleepJoin)
                {
                    sb.AppendLine("║     🔴 UI Thread está em WaitSleepJoin → DEADLOCK ou BLOCKING CALL");
                    sb.AppendLine("║     Provável causa: Dispatcher.Invoke() de background thread");
                    sb.AppendLine("║     ou Wait()/Result em Task no UI thread");
                }
                else if (_uiThread?.ThreadState == System.Threading.ThreadState.Running)
                {
                    sb.AppendLine("║     🟡 UI Thread está Running → COMPUTAÇÃO PESADA no UI thread");
                    sb.AppendLine("║     Provável causa: WMI query, file I/O, ou XAML layout pesado");
                }
                
                if (currentOp != null && opDuration > 500)
                {
                    sb.AppendLine($"║     🔴 Operação '{currentOp}' está rodando há {opDuration}ms — PRINCIPAL SUSPEITA");
                }

                sb.AppendLine("╚════════════════════════════════════════════════════════════════╝");

                var report = sb.ToString();
                WriteLog(report);
                _logger?.LogError($"[FREEZE_FORENSICS] {report}");
            }
            catch (Exception ex)
            {
                WriteLog($"[FORENSICS] Erro ao capturar snapshot: {ex.Message}");
            }
        }

        public void StopMonitoring()
        {
            if (!_isMonitoring) return;
            _isMonitoring = false;
            
            WriteLog($"[FORENSICS] Monitoramento encerrado. Total freezes detectados: {_freezeCount}");
            WriteLog($"[FORENSICS] Boot total: {_globalSw.ElapsedMilliseconds}ms");
            
            if (_freezeCount == 0)
            {
                WriteLog("[FORENSICS] ✅ STARTUP LIMPO — Nenhum freeze detectado!");
            }
            else
            {
                WriteLog($"[FORENSICS] ⚠️ {_freezeCount} freeze(s) detectado(s) durante o startup.");
            }

            try
            {
                _cts.Cancel();
            }
            catch { }

            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void WriteLog(string message)
        {
            try
            {
                var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}";
                File.AppendAllText(_logPath, line, System.Text.Encoding.UTF8);
            }
            catch { }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        private static int GetCurrentNativeThreadId()
        {
            try { return (int)GetCurrentThreadId(); }
            catch { return -1; }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cts.Cancel();
            _cts.Dispose();
            Instance = null;
        }
    }
}
