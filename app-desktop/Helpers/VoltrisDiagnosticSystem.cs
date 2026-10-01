using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Media;
using VoltrisOptimizer.Services;
using Microsoft.Diagnostics.Runtime;

#nullable enable

namespace VoltrisOptimizer.Helpers
{
    /// <summary>
    /// SISTEMA CENTRALIZADO DE DIAGNÓSTICO VOLTRIS
    ///
    /// Integra todos os helpers de diagnóstico existentes e adiciona:
    ///  - Detecção de deadlock via análise de threads
    ///  - Captura de first-chance exceptions
    ///  - Timeline completa de inicialização
    ///  - Detecção de async void problemáticos
    ///  - Monitoramento de profundidade da fila do Dispatcher
    ///  - Detecção de .Result/.Wait() na UI thread
    ///  - Snapshot automático ao freeze
    /// </summary>
    public sealed class VoltrisDiagnosticSystem : IDisposable
    {
        // ── Singleton ──────────────────────────────────────────────────────────
        private static VoltrisDiagnosticSystem? _instance;
        public static VoltrisDiagnosticSystem Instance =>
            _instance ??= new VoltrisDiagnosticSystem();

        // ── Estado interno ─────────────────────────────────────────────────────
        private readonly Stopwatch _globalSw = Stopwatch.StartNew();
        private readonly CancellationTokenSource _cts = new();
        private readonly string _logPath;
        private readonly object _writeLock = new();
        private volatile bool _disposed;
        private volatile bool _running;

        // Timeline de eventos
        private readonly ConcurrentQueue<TimelineEvent> _timeline = new();

        // Rastreamento de operações na UI thread
        private volatile string? _currentUIOperation;
        private long _currentUIOpStartMs;

        // Referência à UI thread
        private Thread? _uiThread;
        private int _uiThreadNativeId;

        // Dispatcher hooks
        private long _dispatcherPending;

        // Render monitor
        private long _lastRenderTime;

        // UI heartbeat
        private long _lastUIHeartbeat = Environment.TickCount64;
        private DispatcherTimer? _heartbeatTimer;

        // Contadores
        private int _freezeCount;
        private int _firstChanceExceptionCount;
        private int _unobservedTaskCount;

        // Configuração (usando DiagnosticConfig)
        private int WatchdogIntervalMs => DiagnosticConfig.WatchdogIntervalMs;
        private int FreezeThresholdMs => DiagnosticConfig.FreezeThresholdMs;
        private int SlowOperationThresholdMs => DiagnosticConfig.SlowOperationThresholdMs;

        // ── Estruturas ─────────────────────────────────────────────────────────
        private record TimelineEvent(long Ms, string Category, string Message, int ThreadId);

        // ── Construtor ─────────────────────────────────────────────────────────
        private VoltrisDiagnosticSystem()
        {
            var logDir = LogDirectoryResolver.Resolve();
            Directory.CreateDirectory(logDir);
            _logPath = Path.Combine(logDir, $"VoltrisDiag_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");

            WriteHeader();
            CleanupOldLogs();
        }

        // ── API Pública ────────────────────────────────────────────────────────

        /// <summary>
        /// Inicia o sistema de diagnóstico. Deve ser chamado da UI thread.
        /// </summary>
        public void Start()
        {
            if (_running) return;
            _running = true;

            _uiThread = Thread.CurrentThread;
            _uiThreadNativeId = GetNativeThreadId();

            // 1. Capturar first-chance exceptions (exceções silenciosas)
            AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;

            // 2. Capturar UnobservedTaskExceptions
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            // 3. Iniciar watchdog de freeze em task assíncrona (substitui thread dedicada)
            var watchdogTask = Task.Run(() => WatchdogLoopAsync(_cts.Token));

            // 4. Iniciar monitor de deadlock em task assíncrona (substitui thread dedicada)
            var deadlockTask = Task.Run(() => DeadlockMonitorLoopAsync(_cts.Token));

            // 5. Instalar hooks do Dispatcher, monitor de renderização e heartbeat
            InstallDispatcherHooks();
            InstallRenderingMonitor();
            StartUIHeartbeat();

            Log("SYSTEM", $"VoltrisDiagnosticSystem INICIADO | UI Thread ManagedId={_uiThread.ManagedThreadId} NativeId={_uiThreadNativeId}");
            Log("SYSTEM", $"PID={Environment.ProcessId} | Arch={(IntPtr.Size == 8 ? "x64" : "x86")} | .NET={Environment.Version}");
            Log("SYSTEM", $"OS={Environment.OSVersion} | CPUs={Environment.ProcessorCount} | RAM={GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024}MB");
        }

        /// <summary>
        /// Registra um evento na timeline de inicialização.
        /// Thread-safe. Pode ser chamado de qualquer thread.
        /// </summary>
        public void Timeline(string category, string message)
        {
            var ev = new TimelineEvent(_globalSw.ElapsedMilliseconds, category, message, Environment.CurrentManagedThreadId);
            _timeline.Enqueue(ev);

            // A timeline e consumida em Janela/Take(10) e nunca esvaziada. Sem teto ela
            // cresce a cada Timeline() da sessao inteira. O log em disco continua
            // recebendo tudo; aqui mantemos apenas a janela recente para diagnostico.
            while (_timeline.Count > 3000 && _timeline.TryDequeue(out _))
            {
            }

            Log(category, message);
        }

        /// <summary>
        /// Marca o início de uma operação na UI thread.
        /// </summary>
        public void BeginUIOperation(string name)
        {
            _currentUIOperation = name;
            _currentUIOpStartMs = _globalSw.ElapsedMilliseconds;
            Log("UI-BEGIN", $"{name} | Thread={Thread.CurrentThread.ManagedThreadId}");
        }

        /// <summary>
        /// Marca o fim de uma operação na UI thread.
        /// </summary>
        public void EndUIOperation(string name)
        {
            var duration = _globalSw.ElapsedMilliseconds - _currentUIOpStartMs;
            _currentUIOperation = null;
            var severity = duration > SlowOperationThresholdMs ? " ⚠️ LENTO" : "";
            Log("UI-END", $"{name} | {duration}ms{severity}");

            if (duration > SlowOperationThresholdMs)
            {
                Log("PERF-WARN", $"Operação '{name}' levou {duration}ms na UI thread — acima do limite de {SlowOperationThresholdMs}ms");
            }
        }

        /// <summary>
        /// Nome da operação de UI atualmente em andamento (null se a UI thread está ociosa).
        /// Usado pelo UIThreadMonitor para correlacionar um freeze com a operação que o causou.
        /// </summary>
        public string? CurrentUIOperationName => _currentUIOperation;

        /// <summary>
        /// Verifica se o código está rodando na UI thread e loga aviso se não deveria.
        /// </summary>
        public void AssertNotUIThread(string callerName)
        {
            if (_uiThread != null && Thread.CurrentThread.ManagedThreadId == _uiThread.ManagedThreadId)
            {
                Log("UI-VIOLATION", $"⛔ '{callerName}' está rodando na UI thread — DEVERIA estar em background!");
                CaptureCallStack("UI-VIOLATION", callerName);
            }
        }

        /// <summary>
        /// Verifica se o código está rodando na UI thread (esperado).
        /// </summary>
        public void AssertUIThread(string callerName)
        {
            if (_uiThread != null && Thread.CurrentThread.ManagedThreadId != _uiThread.ManagedThreadId)
            {
                Log("THREAD-WARN", $"⚠️ '{callerName}' está rodando em thread de background (TID={Thread.CurrentThread.ManagedThreadId}) — esperava UI thread");
            }
        }

        /// <summary>
        /// Gera relatório final da timeline de inicialização.
        /// </summary>
        public void GenerateStartupReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("╔══════════════════════════════════════════════════════════════════╗");
            sb.AppendLine("║           VOLTRIS DIAGNOSTIC — STARTUP TIMELINE REPORT          ║");
            sb.AppendLine($"║  Total boot time: {_globalSw.ElapsedMilliseconds}ms");
            sb.AppendLine($"║  Freezes detectados: {_freezeCount}");
            sb.AppendLine($"║  First-chance exceptions: {_firstChanceExceptionCount}");
            sb.AppendLine($"║  Unobserved task exceptions: {_unobservedTaskCount}");
            sb.AppendLine("╠══════════════════════════════════════════════════════════════════╣");
            sb.AppendLine("║  TIMELINE:");

            long prev = 0;
            foreach (var ev in _timeline.OrderBy(e => e.Ms))
            {
                var delta = ev.Ms - prev;
                var deltaStr = delta > 500 ? $" (+{delta}ms ⚠️)" : delta > 100 ? $" (+{delta}ms)" : $" (+{delta}ms)";
                sb.AppendLine($"║  [{ev.Ms:D6}ms]{deltaStr,-18} [{ev.Category,-15}] TID:{ev.ThreadId,-3} {ev.Message}");
                prev = ev.Ms;
            }

            sb.AppendLine("╚══════════════════════════════════════════════════════════════════╝");
            WriteRaw(sb.ToString());
        }

        /// <summary>
        /// Para o sistema de diagnóstico e gera relatório final.
        /// </summary>
        public void Stop()
        {
            if (!_running) return;

            AppDomain.CurrentDomain.FirstChanceException -= OnFirstChanceException;
            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

            // Desinscrever monitor de renderização para parar callbacks na thread da UI
            try
            {
                CompositionTarget.Rendering -= OnRendering;
            }
            catch { }

            // Parar o timer de heartbeat da UI thread
            try
            {
                if (_heartbeatTimer != null)
                {
                    _heartbeatTimer.Stop();
                    _heartbeatTimer = null;
                }
            }
            catch { }

            GenerateStartupReport();
            Log("SYSTEM", $"VoltrisDiagnosticSystem ENCERRADO | Boot={_globalSw.ElapsedMilliseconds}ms | Freezes={_freezeCount}");

            try { _cts.Cancel(); } catch { }

            _running = false;
        }

        // ── Watchdog de Freeze ─────────────────────────────────────────────────

        // Período de silêncio durante o startup (ms):
        // O WPF XAML parser ocupa a UI thread por 300-900ms de forma legítima (CPU puro).
        // Detectar isso como "freeze" é um falso positivo estrutural — suprimir na janela de boot.
        // WaitSleepJoin ainda dispara imediatamente pois é sempre um bug real.
        private const int StartupSilenceMs = 5000;

        private void WatchdogLoop()
        {
            Log("WATCHDOG", "Thread de watchdog iniciada");

            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    Thread.Sleep(WatchdogIntervalMs);
                    if (_cts.Token.IsCancellationRequested) break;

                    // 1. Verificar Heartbeat da UI thread
                    var now = Environment.TickCount64;
                    var heartbeatAge = now - _lastUIHeartbeat;
                    if (heartbeatAge > 2000)
                    {
                        Log("WATCHDOG", $"🚨 THREAD DE UI CONGELADA DETECTADA VIA HEARTBEAT (Último heartbeat há {heartbeatAge}ms)!");
                        CaptureUIThreadStack();
                        CaptureFullFreezeSnapshot(heartbeatAge);
                        
                        // Evitar logar repetidamente em segundos consecutivos se continuar bloqueado
                        Thread.Sleep(5000);
                        continue;
                    }

                     var dispatcher = Application.Current?.Dispatcher;
                     // Run consolidated startup watchdog work if enabled
                     StartupWatchdog.PerformWorkIfDue();
                    if (dispatcher == null || dispatcher.HasShutdownStarted) break;

                    bool responded = false;
                    var sw = Stopwatch.StartNew();

                    var op = dispatcher.BeginInvoke(DispatcherPriority.Send, (Action)(() => { responded = true; }));
                    var status = op.Wait(TimeSpan.FromMilliseconds(FreezeThresholdMs));
                    sw.Stop();

                    if (status != DispatcherOperationStatus.Completed && sw.ElapsedMilliseconds >= FreezeThresholdMs)
                    {
                        // Verificar estado da UI thread: Running = XAML/CPU legítimo durante boot
                        bool isRunningFreeze = _uiThread != null &&
                            (_uiThread.ThreadState & System.Threading.ThreadState.WaitSleepJoin) == 0 &&
                            (_uiThread.ThreadState & System.Threading.ThreadState.Running) != 0;

                        bool inStartupWindow = _globalSw.ElapsedMilliseconds < StartupSilenceMs;

                        if (isRunningFreeze && inStartupWindow)
                        {
                            // Falso positivo de startup: XAML parser / CPU init — apenas logar, não contar como freeze
                            Log("WATCHDOG", $"⚡ UI Thread ocupada (Running) durante janela de startup ({sw.ElapsedMilliseconds}ms) — " +
                                $"XAML init esperado, não contabilizado como freeze. Boot={_globalSw.ElapsedMilliseconds}ms");
                        }
                        else
                        {
                            Interlocked.Increment(ref _freezeCount);
                            CaptureUIThreadStack();
                            CaptureFullFreezeSnapshot(sw.ElapsedMilliseconds);
                        }
                    }
                }
                catch (TaskCanceledException) { break; }
                catch (OperationCanceledException) { break; }
                catch { /* watchdog nunca deve morrer */ }
            }

            Log("WATCHDOG", "Thread de watchdog encerrada");
        }

        // ── Monitor de Deadlock ────────────────────────────────────────────────

        private void DeadlockMonitorLoop()
        {
            // Detecta padrão clássico de deadlock WPF:
            // Thread de background em WaitSleepJoin enquanto UI thread também está bloqueada
            Log("DEADLOCK-MON", "Monitor de deadlock iniciado");

            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    Thread.Sleep(1000);
                    if (_cts.Token.IsCancellationRequested) break;

                    if (_uiThread == null) continue;

                    var uiState = _uiThread.ThreadState;

                    // UI thread bloqueada em Wait/Sleep/Join é sinal de deadlock
                    if ((uiState & System.Threading.ThreadState.WaitSleepJoin) != 0)
                    {
                        Log("DEADLOCK-WARN",
                            $"⛔ UI Thread (TID={_uiThread.ManagedThreadId}) está em WaitSleepJoin! " +
                            $"Estado={uiState} | Op atual='{_currentUIOperation ?? "NONE"}' | " +
                            $"Duração op={_globalSw.ElapsedMilliseconds - _currentUIOpStartMs}ms");

                        CaptureUIThreadStack();
                        CaptureDeadlockSnapshot();
                    }
                }
                catch (TaskCanceledException) { break; }
                catch (OperationCanceledException) { break; }
                catch { }
            }

            Log("DEADLOCK-MON", "Monitor de deadlock encerrado");
        }

        // ── Snapshots Forenses ─────────────────────────────────────────────────

        // ==========================
        // Async watchdog & deadlock monitors (replace thread usage)
        // ==========================
        private async Task WatchdogLoopAsync(CancellationToken token)
        {
            Log("WATCHDOG", "Task de watchdog iniciada");

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(WatchdogIntervalMs, token);
                    if (token.IsCancellationRequested) break;

                    // 1. Verificar Heartbeat da UI thread
                    var now = Environment.TickCount64;
                    var heartbeatAge = now - _lastUIHeartbeat;
                    if (heartbeatAge > 2000)
                    {
                        Log("WATCHDOG", $"🚨 THREAD DE UI CONGELADA DETECTADA VIA HEARTBEAT (Último heartbeat há {heartbeatAge}ms)!");
                        CaptureUIThreadStack();
                        CaptureFullFreezeSnapshot(heartbeatAge);

                        // Evitar logar repetidamente em segundos consecutivos se continuar bloqueado
                        await Task.Delay(5000, token);
                        continue;
                    }

                    var dispatcher = Application.Current?.Dispatcher;
                    // Run consolidated startup watchdog work if enabled
                    StartupWatchdog.PerformWorkIfDue();
                    if (dispatcher == null || dispatcher.HasShutdownStarted) break;

                    bool responded = false;
                    var sw = Stopwatch.StartNew();

                    var op = dispatcher.BeginInvoke(DispatcherPriority.Send, (Action)(() => { responded = true; }));
                    var status = op.Wait(TimeSpan.FromMilliseconds(FreezeThresholdMs));
                    sw.Stop();

                    if (status != DispatcherOperationStatus.Completed && sw.ElapsedMilliseconds >= FreezeThresholdMs)
                    {
                        // Verificar estado da UI thread: Running = XAML/CPU legítimo durante boot
                        bool isRunningFreeze = _uiThread != null &&
                            (_uiThread.ThreadState & System.Threading.ThreadState.WaitSleepJoin) == 0 &&
                            (_uiThread.ThreadState & System.Threading.ThreadState.Running) != 0;

                        bool inStartupWindow = _globalSw.ElapsedMilliseconds < StartupSilenceMs;

                        if (isRunningFreeze && inStartupWindow)
                        {
                            // Falso positivo de startup: XAML parser / CPU init — apenas logar, não contar como freeze
                            Log("WATCHDOG", $"⚡ UI Thread ocupada (Running) durante janela de startup ({sw.ElapsedMilliseconds}ms) — " +
                                $"XAML init esperado, não contabilizado como freeze. Boot={_globalSw.ElapsedMilliseconds}ms");
                        }
                        else
                        {
                            Interlocked.Increment(ref _freezeCount);
                            CaptureUIThreadStack();
                            CaptureFullFreezeSnapshot(sw.ElapsedMilliseconds);
                        }
                    }
                }
                catch (TaskCanceledException) { break; }
                catch (OperationCanceledException) { break; }
                catch { /* watchdog nunca deve morrer */ }
            }

            Log("WATCHDOG", "Task de watchdog encerrada");
        }

        private async Task DeadlockMonitorLoopAsync(CancellationToken token)
        {
            Log("DEADLOCK-MON", "Monitor de deadlock iniciado");

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(1000, token);
                    if (token.IsCancellationRequested) break;

                    if (_uiThread == null) continue;

                    var uiState = _uiThread.ThreadState;

                    // UI thread bloqueada em Wait/Sleep/Join é sinal de deadlock
                    if ((uiState & System.Threading.ThreadState.WaitSleepJoin) != 0)
                    {
                        Log("DEADLOCK-WARN",
                            $"⛔ UI Thread (TID={_uiThread.ManagedThreadId}) está em WaitSleepJoin! " +
                            $"Estado={uiState} | Op atual='{_currentUIOperation ?? "NONE"}' | " +
                            $"Duração op={_globalSw.ElapsedMilliseconds - _currentUIOpStartMs}ms");

                        CaptureUIThreadStack();
                        CaptureDeadlockSnapshot();
                    }
                }
                catch (TaskCanceledException) { break; }
                catch (OperationCanceledException) { break; }
                catch { }
            }

            Log("DEADLOCK-MON", "Monitor de deadlock encerrado");
        }

        // ── Snapshots Forenses ─────────────────────────────────────────────────

        private void CaptureFullFreezeSnapshot(long durationMs)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine();
                sb.AppendLine($"╔══════════════════════════════════════════════════════════════════╗");
                sb.AppendLine($"║  ⛔ FREEZE #{_freezeCount} | Duração: {durationMs}ms | Boot: {_globalSw.ElapsedMilliseconds}ms");
                sb.AppendLine($"║  Timestamp: {DateTime.Now:HH:mm:ss.fff}");
                sb.AppendLine("╠══════════════════════════════════════════════════════════════════╣");

                // Operação atual na UI thread
                var op = _currentUIOperation;
                var opMs = op != null ? _globalSw.ElapsedMilliseconds - _currentUIOpStartMs : 0L;
                sb.AppendLine($"║  🎯 UI Op: {op ?? "NENHUMA"}{(op != null ? $" (rodando há {opMs}ms)" : "")}");

                // Estado da UI thread
                if (_uiThread != null)
                {
                    sb.AppendLine($"║  🧵 UI Thread State: {_uiThread.ThreadState}");

                    // DIAGNÓSTICO AUTOMÁTICO baseado no estado
                    if ((_uiThread.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0)
                    {
                        sb.AppendLine("║  🔴 DIAGNÓSTICO: UI Thread em WaitSleepJoin");
                        sb.AppendLine("║     → Causa provável: .Result/.Wait() ou Dispatcher.Invoke() bloqueante");
                        sb.AppendLine("║     → Verificar: WmiHelper.QuerySafe(), Task.WhenAll().WaitAsync()");
                        sb.AppendLine("║     → Verificar: Dispatcher.Invoke() chamado de background thread");
                    }
                    else if ((_uiThread.ThreadState & System.Threading.ThreadState.Running) != 0)
                    {
                        sb.AppendLine("║  🟡 DIAGNÓSTICO: UI Thread Running (computação pesada)");
                        sb.AppendLine("║     → Causa provável: WMI query, I/O síncrono, XAML layout pesado");
                        sb.AppendLine("║     → Verificar: InitializeStaticInfo(), ProfileStore.Load()");
                    }
                }

                // Passos de startup ativos
                sb.AppendLine("║");
                sb.AppendLine("║  📋 Startup Steps Ativos:");
                AppendActiveStartupSteps(sb);

                // Forense de componentes (Widget, Dashboard, Tooltip)
                AppendComponentStatusForensics(sb);

                // Análise de threads do processo
                sb.AppendLine("║");
                sb.AppendLine("║  🧵 Threads do Processo:");
                AppendThreadDump(sb);

                // Últimos 10 eventos da timeline
                sb.AppendLine("║");
                sb.AppendLine("║  📅 Últimos eventos da timeline:");
                var recent = _timeline.OrderByDescending(e => e.Ms).Take(10).OrderBy(e => e.Ms);
                foreach (var ev in recent)
                    sb.AppendLine($"║    [{ev.Ms:D6}ms] [{ev.Category}] {ev.Message}");

                sb.AppendLine("╚══════════════════════════════════════════════════════════════════╝");
                WriteRaw(sb.ToString());
            }
            catch { /* snapshot nunca deve travar o watchdog */ }
        }

        private void CaptureDeadlockSnapshot()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine();
                sb.AppendLine("╔══════════════════════════════════════════════════════════════════╗");
                sb.AppendLine($"║  🔒 POSSÍVEL DEADLOCK DETECTADO | {DateTime.Now:HH:mm:ss.fff}");
                sb.AppendLine("╠══════════════════════════════════════════════════════════════════╣");
                sb.AppendLine("║  Padrão: UI Thread em WaitSleepJoin");
                sb.AppendLine("║");
                sb.AppendLine("║  Causas mais comuns neste projeto:");
                sb.AppendLine("║  1. WmiHelper.QuerySafe() chamado na UI thread (.GetAwaiter().GetResult())");
                sb.AppendLine("║  2. Task.WhenAll(...).WaitAsync() aguardado na UI thread");
                sb.AppendLine("║  3. Dispatcher.Invoke() de background thread enquanto UI está bloqueada");
                sb.AppendLine("║  4. LicenseOrchestrationService.GetCurrentStateAsync() sem ConfigureAwait");
                sb.AppendLine("║");
                sb.AppendLine("║  Operação UI atual: " + (_currentUIOperation ?? "NENHUMA"));
                sb.AppendLine("║");
                sb.AppendLine("║  🧩 Forense de Status de Componentes (Widget, Dashboard, Tooltip):");
                AppendComponentStatusForensics(sb);
                sb.AppendLine("║");
                AppendThreadDump(sb);
                sb.AppendLine("╚══════════════════════════════════════════════════════════════════╝");
                WriteRaw(sb.ToString());
            }
            catch { }
        }

        private void CaptureCallStack(string category, string context)
        {
            try
            {
                var stack = new StackTrace(2, true);
                var sb = new StringBuilder();
                sb.AppendLine($"[{category}] Call stack para '{context}':");
                foreach (var frame in stack.GetFrames().Take(15))
                {
                    var method = frame.GetMethod();
                    if (method == null) continue;
                    var file = frame.GetFileName();
                    var line = frame.GetFileLineNumber();
                    var loc = file != null ? $" ({Path.GetFileName(file)}:{line})" : "";
                    sb.AppendLine($"  at {method.DeclaringType?.Name}.{method.Name}{loc}");
                }
                WriteRaw(sb.ToString());
            }
            catch { }
        }

        private void AppendActiveStartupSteps(StringBuilder sb)
        {
            try
            {
                var tracker = StartupStepTracker.Instance;
                if (tracker == null) { sb.AppendLine("║    (StartupStepTracker não disponível)"); return; }

                var stepsField = typeof(StartupStepTracker).GetField("_steps",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (stepsField?.GetValue(tracker) is ConcurrentDictionary<string, object> steps)
                {
                    bool any = false;
                    foreach (var kvp in steps)
                    {
                        var statusProp = kvp.Value.GetType().GetProperty("Status");
                        if (statusProp?.GetValue(kvp.Value)?.ToString() != "Running") continue;
                        any = true;
                        var startMs = kvp.Value.GetType().GetProperty("StartMs")?.GetValue(kvp.Value) ?? 0L;
                        var tid = kvp.Value.GetType().GetProperty("ThreadId")?.GetValue(kvp.Value) ?? 0;
                        var elapsed = _globalSw.ElapsedMilliseconds - (long)startMs;
                        sb.AppendLine($"║    🔴 {kvp.Key} (rodando há {elapsed}ms em TID={tid})");
                    }
                    if (!any) sb.AppendLine("║    (Nenhum passo ativo)");
                }
            }
            catch { sb.AppendLine("║    (Erro ao acessar StartupStepTracker)"); }
        }

        private void AppendComponentStatusForensics(StringBuilder sb)
        {
            try
            {
                sb.AppendLine("║");
                sb.AppendLine("║  🧩 STATUS FORENSICS DE COMPONENTES:");

                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null)
                {
                    sb.AppendLine("║     [FORENSICS] Dispatcher da aplicação não disponível");
                    return;
                }

                // Usamos InvokeAsync com um timeout de 150ms para evitar travar o Watchdog se a UI Thread estiver completamente bloqueada
                bool widgetVisibleState = false;
                string widgetWindowDetails = "null / não criado";
                bool widgetManagerExists = false;
                string widgetError = null;

                bool dashVmExists = false;
                string dashVmDetails = "Não instanciado";
                string dashTimerDetails = "N/A";
                string dashLockDetails = "N/A";
                string dashError = null;

                bool mainWinExists = false;
                int mainWinThreadId = 0;
                bool tooltipVisibleState = false;
                string tooltipWindowDetails = "null / não criado";
                string tooltipTimerDetails = "N/A";
                string tooltipError = null;

                var op = dispatcher.InvokeAsync(() =>
                {
                    // 1. Widget
                    try
                    {
                        var wm = VoltrisOptimizer.App.WidgetManager;
                        if (wm != null)
                        {
                            widgetManagerExists = true;
                            widgetVisibleState = wm.IsWidgetVisible;
                            var field = wm.GetType().GetField("_widgetWindow", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            if (field?.GetValue(wm) is Window window)
                            {
                                widgetWindowDetails = $"{window.GetType().Name} | Loaded={window.IsLoaded} | Visibility={window.Visibility} | Pos=({window.Left}, {window.Top})";
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        widgetError = ex.Message;
                    }

                    // 2. Dashboard
                    try
                    {
                        if (VoltrisOptimizer.App.Services != null)
                        {
                            var dashVm = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<VoltrisOptimizer.UI.ViewModels.DashboardViewModel>(VoltrisOptimizer.App.Services);
                            if (dashVm != null)
                            {
                                dashVmExists = true;
                                dashVmDetails = dashVm.GetType().Name;
                                var timerField = dashVm.GetType().GetField("_telemetryTimer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                if (timerField?.GetValue(dashVm) is System.Windows.Threading.DispatcherTimer timer)
                                {
                                    dashTimerDetails = $"Enabled={timer.IsEnabled}, Interval={timer.Interval}";
                                }
                                var optLockField = dashVm.GetType().GetField("_quickOptimizeLock", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                if (optLockField?.GetValue(dashVm) is SemaphoreSlim optLock)
                                {
                                    dashLockDetails = $"CurrentCount={optLock.CurrentCount}";
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        dashError = ex.Message;
                    }

                    // 3. Tooltip
                    try
                    {
                        var mainWin = Application.Current?.MainWindow;
                        if (mainWin != null)
                        {
                            mainWinExists = true;
                            mainWinThreadId = mainWin.Dispatcher.Thread.ManagedThreadId;
                            var ttField = mainWin.GetType().GetField("_modernTooltip", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            var ttVisField = mainWin.GetType().GetField("_tooltipVisible", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            var ttPollField = mainWin.GetType().GetField("_tooltipMousePollTimer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            
                            var tooltip = ttField?.GetValue(mainWin) as Window;
                            var isVisible = ttVisField?.GetValue(mainWin) as bool?;
                            var pollTimer = ttPollField?.GetValue(mainWin) as System.Windows.Threading.DispatcherTimer;
                            
                            tooltipVisibleState = isVisible ?? false;
                            if (tooltip != null)
                            {
                                tooltipWindowDetails = $"Loaded={tooltip.IsLoaded} | Visible={tooltip.IsVisible} | Opacity={tooltip.Opacity} | Pos=({tooltip.Left}, {tooltip.Top})";
                            }
                            if (pollTimer != null)
                            {
                                tooltipTimerDetails = $"Enabled={pollTimer.IsEnabled}, Interval={pollTimer.Interval}";
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        tooltipError = ex.Message;
                    }
                });

                // Esperar a operação do dispatcher com timeout de 150ms
                bool completed = false;
                try
                {
                    var status = op.Wait(TimeSpan.FromMilliseconds(150));
                    completed = (status == System.Windows.Threading.DispatcherOperationStatus.Completed);
                }
                catch { }

                if (!completed)
                {
                    sb.AppendLine("║     ⚠️ AVISO: UI Thread está COMPLETAMENTE TRAVADA (Timeout ao obter forensics)");
                    sb.AppendLine("║              Não foi possível ler as propriedades dinâmicas dos componentes.");
                    return;
                }

                // Se completou, imprimir os resultados coletados
                // 1. Widget
                if (widgetError != null)
                {
                    sb.AppendLine($"║     [WIDGET] Erro ao obter dados: {widgetError}");
                }
                else if (widgetManagerExists)
                {
                    sb.AppendLine($"║     [WIDGET] Manager: Ativo | Visível: {widgetVisibleState}");
                    sb.AppendLine($"║              Window: {widgetWindowDetails}");
                }
                else
                {
                    sb.AppendLine("║     [WIDGET] Manager: null / não instanciado");
                }

                // 2. Dashboard
                if (dashError != null)
                {
                    sb.AppendLine($"║     [DASHBOARD] Erro ao obter dados: {dashError}");
                }
                else if (dashVmExists)
                {
                    sb.AppendLine($"║     [DASHBOARD] VM: Ativa ({dashVmDetails})");
                    sb.AppendLine($"║                 TelemetryTimer: {dashTimerDetails}");
                    sb.AppendLine($"║                 QuickOptimizeLock: {dashLockDetails}");
                }
                else
                {
                    sb.AppendLine("║     [DASHBOARD] VM: null (não instanciado via DI)");
                }

                // 3. Tooltip
                if (tooltipError != null)
                {
                    sb.AppendLine($"║     [TOOLTIP] Erro ao obter dados: {tooltipError}");
                }
                else if (mainWinExists)
                {
                    sb.AppendLine($"║     [TOOLTIP] MainWindow: Ativa | Dispatcher Thread={mainWinThreadId}");
                    sb.AppendLine($"║               Visible (MainWindow State): {tooltipVisibleState}");
                    sb.AppendLine($"║               ModernTrayTooltip: {tooltipWindowDetails}");
                    sb.AppendLine($"║               MousePollTimer: {tooltipTimerDetails}");
                }
                else
                {
                    sb.AppendLine("║     [TOOLTIP] MainWindow: null");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"║     [FORENSICS] Erro Geral: {ex.Message}");
            }
        }

        private void AppendThreadDump(StringBuilder sb)
        {
            try
            {
                var proc = Process.GetCurrentProcess();
                sb.AppendLine($"║    Processo: {proc.ProcessName} PID={proc.Id} Threads={proc.Threads.Count} WS={proc.WorkingSet64 / 1024 / 1024}MB");

                var threads = new List<ProcessThread>();
                foreach (ProcessThread pt in proc.Threads) threads.Add(pt);

                // Ordenar: UI thread primeiro, depois por CPU
                var sorted = threads.OrderByDescending(t => t.Id == _uiThreadNativeId ? long.MaxValue :
                    (long)TryGetCpuMs(t)).Take(20);

                foreach (var pt in sorted)
                {
                    try
                    {
                        var marker = pt.Id == _uiThreadNativeId ? " ◀ UI THREAD" : "";
                        var wait = "";
                        if (pt.ThreadState == System.Diagnostics.ThreadState.Wait)
                            try { wait = $" Wait={pt.WaitReason}"; } catch { }
                        sb.AppendLine($"║    TID:{pt.Id,-6} {pt.ThreadState,-12}{wait} CPU={TryGetCpuMs(pt):F0}ms{marker}");
                    }
                    catch { }
                }
            }
            catch { sb.AppendLine("║    (Erro ao capturar thread dump)"); }
        }

        private static double TryGetCpuMs(ProcessThread t)
        {
            try { return t.TotalProcessorTime.TotalMilliseconds; } catch { return 0; }
        }

        // ── Handlers de Exceção ────────────────────────────────────────────────

        private void OnFirstChanceException(object? sender, FirstChanceExceptionEventArgs e)
        {
            // Filtrar exceções esperadas/barulhentas para não poluir o log
            var ex = e.Exception;
            if (ex is ThreadAbortException) return;
            if (ex is OperationCanceledException) return;
            if (ex is TaskCanceledException) return;

            // Filtrar exceções de WMI que já são tratadas
            if (ex.Message.Contains("InvalidNamespace", StringComparison.OrdinalIgnoreCase)) return;
            if (ex.Message.Contains("Namespace", StringComparison.OrdinalIgnoreCase) &&
                ex.GetType().Name.Contains("Management")) return;
            if (ex.Message.Contains("WaitReason", StringComparison.OrdinalIgnoreCase)) return;
            
            // Filtrar exceções de DLL de GPU esperadas do LibreHardwareMonitor em sistemas onde a marca não existe
            if (ex is DllNotFoundException && (ex.Message.Contains("atiadlxx.dll", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("nvml.dll", StringComparison.OrdinalIgnoreCase))) return;

            Interlocked.Increment(ref _firstChanceExceptionCount);

            // Logar apenas as primeiras 50 para não sobrecarregar
            if (_firstChanceExceptionCount > 50) return;

            var tid = Thread.CurrentThread.ManagedThreadId;
            var isUIThread = _uiThread != null && tid == _uiThread.ManagedThreadId;
            var uiMarker = isUIThread ? " ⛔ NA UI THREAD" : "";

            Log("FIRST-CHANCE",
                $"[#{_firstChanceExceptionCount}]{uiMarker} {ex.GetType().Name}: {ex.Message?.Substring(0, Math.Min(120, ex.Message?.Length ?? 0))} | TID={tid}");

            // Se for na UI thread, capturar stack trace completo — é suspeito
            if (isUIThread)
            {
                Log("FIRST-CHANCE-STACK", $"Stack: {ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim() ?? "N/A"}");
            }
        }

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            Interlocked.Increment(ref _unobservedTaskCount);
            var inner = e.Exception?.GetBaseException();
            Log("UNOBSERVED-TASK",
                $"[#{_unobservedTaskCount}] {inner?.GetType().Name}: {inner?.Message} | Stack: {inner?.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}");
            e.SetObserved();
        }

        // ── Novos Helpers de Diagnóstico e Instrumentação ──────────────────────────

        private void CaptureUIThreadStack()
        {
            try
            {
                using var dataTarget = DataTarget.AttachToProcess(
                    Environment.ProcessId,
                    suspend: false);

                var clrVersion = dataTarget.ClrVersions.FirstOrDefault();

                if (clrVersion == null)
                {
                    Log("STACK", "CLR version não encontrada");
                    return;
                }

                var runtime = clrVersion.CreateRuntime();

                foreach (var thread in runtime.Threads)
                {
                    if (!thread.IsAlive)
                        continue;

                    if (thread.OSThreadId != (uint)_uiThreadNativeId)
                        continue;

                    Log("STACK", "════════ UI THREAD STACK ════════");

                    foreach (var frame in thread.EnumerateStackTrace())
                    {
                        Log("STACK", frame.ToString() ?? "N/A");
                    }

                    Log("STACK", "═════════════════════════════════");
                }
            }
            catch (Exception ex)
            {
                Log("STACK-ERROR", ex.ToString());
            }
        }

        public void LogCriticalSyncBlock(string caller)
        {
            Log("SYNC-BLOCK", $"⛔ SYNC-OVER-ASYNC DETECTADO em '{caller}'");
            CaptureCallStack("SYNC-BLOCK", caller);
        }

        private void InstallDispatcherHooks()
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null) return;

                var hooks = dispatcher.Hooks;

                hooks.OperationPosted += (_, e) =>
                {
                    Interlocked.Increment(ref _dispatcherPending);
                    // OTIMIZAÇÃO: Não logar cada operação individual para evitar arquivos de log de gigabytes e overhead de CPU/Disco.
                };

                hooks.OperationCompleted += (_, e) =>
                {
                    Interlocked.Decrement(ref _dispatcherPending);
                };
            }
            catch (Exception ex)
            {
                Log("DISPATCHER-ERROR", $"Erro ao instalar hooks do dispatcher: {ex.Message}");
            }
        }

        private void InstallRenderingMonitor()
        {
            try
            {
                _lastRenderTime = Environment.TickCount64;
                CompositionTarget.Rendering += OnRendering;
            }
            catch (Exception ex)
            {
                Log("RENDER-ERROR", $"Erro ao instalar monitor de renderização: {ex.Message}");
            }
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            var now = Environment.TickCount64;
            var delta = now - _lastRenderTime;
            _lastRenderTime = now;

            if (delta > 100) // Mais que 100ms sem renderizar (starvation de frame)
            {
                Log("FRAME-STALL", $"⚠️ Frame stall detectado: {delta}ms sem renderizar!");
            }
        }

        private void StartUIHeartbeat()
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null) return;

                dispatcher.Invoke(() =>
                {
                    _heartbeatTimer = new DispatcherTimer(DispatcherPriority.Send);
                     _heartbeatTimer.Interval = TimeSpan.FromMilliseconds(500);
                    _heartbeatTimer.Tick += (_, _) =>
                    {
                        _lastUIHeartbeat = Environment.TickCount64;
                    };
                    _heartbeatTimer.Start();
                     Log("HEARTBEAT", "Heartbeat da UI thread iniciado (500ms interval)");
                });
            }
            catch (Exception ex)
            {
                Log("HEARTBEAT-ERROR", $"Erro ao iniciar heartbeat da UI thread: {ex.Message}");
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void TraceUI(string name)
        {
            Log("UI-TRACE", name);

            var st = new StackTrace(1, true);

            foreach (var f in st.GetFrames().Take(10))
            {
                Log("UI-TRACE", $"{f.GetMethod()?.DeclaringType?.Name}.{f.GetMethod()?.Name}");
            }
        }

        // ── Logging ────────────────────────────────────────────────────────────

        private void Log(string category, string message)
        {
            if (!_running) return;
            var line = $"[{DateTime.Now:HH:mm:ss.fff}][{_globalSw.ElapsedMilliseconds:D7}ms][{category,-18}][TID:{Thread.CurrentThread.ManagedThreadId:D3}] {message}";
            WriteRaw(line);
        }

        private void WriteRaw(string text)
        {
            try
            {
                lock (_writeLock)
                {
                    File.AppendAllText(_logPath, text + Environment.NewLine, System.Text.Encoding.UTF8);
                }
            }
            catch { }
        }

        private void WriteHeader()
        {
            var sb = new StringBuilder();
            sb.AppendLine("╔══════════════════════════════════════════════════════════════════╗");
            sb.AppendLine("║         VOLTRIS DIAGNOSTIC SYSTEM — INITIALIZED                 ║");
            sb.AppendLine($"║  PID: {Environment.ProcessId}  Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            sb.AppendLine($"║  Log: {_logPath}");
            sb.AppendLine("╚══════════════════════════════════════════════════════════════════╝");
            WriteRaw(sb.ToString());
        }

        // ── Win32 ──────────────────────────────────────────────────────────────

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        private static int GetNativeThreadId()
        {
            try { return (int)GetCurrentThreadId(); } catch { return -1; }
        }

        // ── IDisposable ────────────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            try { _cts.Cancel(); _cts.Dispose(); } catch { }
            if (_instance == this) _instance = null;
        }

        private void CleanupOldLogs()
        {
            try
            {
                var logDir = LogDirectoryResolver.Resolve();
                if (!Directory.Exists(logDir)) return;

                // Deletar logs do VoltrisDiagnosticSystem com mais de 5 dias
                var cutoffDate = DateTime.Now.AddDays(-5);
                var di = new DirectoryInfo(logDir);
                foreach (var file in di.GetFiles("VoltrisDiag_*.log"))
                {
                    if (file.LastWriteTime < cutoffDate)
                    {
                        file.Delete();
                    }
                }
            }
            catch { }
        }
    }
}

