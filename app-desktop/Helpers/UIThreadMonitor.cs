using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.Linq;
using System.Runtime.InteropServices;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Helpers
{
    public sealed class UIThreadMonitor : IDisposable
    {
        private readonly ILoggingService? _logger;
        private readonly int _checkIntervalMs;
        private readonly int _warnThresholdMs;
        private readonly CancellationTokenSource _cts = new();
        private int _uiResponseCount;
        private int _uiFreezeCount;
        private bool _isMonitoring = false;

        // Momento em que o processo iniciou — usado para classificar freezes da fase de warm-up.
        private static readonly DateTime _processStartUtc = DateTime.UtcNow;
        
        // Para capturar stack trace da UI thread quando travada
        private Thread? _uiThread;
        private uint _uiThreadOsId;

        public UIThreadMonitor(ILoggingService? logger, int checkIntervalMs = 500, int warnThresholdMs = 1000)
        {
            _logger = logger;
            _checkIntervalMs = checkIntervalMs;
            _warnThresholdMs = warnThresholdMs;
        }

        private static bool IsUiRelevantNow()
        {
            try
            {
                var w = Application.Current?.MainWindow;
                if (w == null) return false;
                if (w.WindowState == WindowState.Minimized) return false;
                return w.IsVisible;
            }
            catch
            {
                return true;
            }
        }

        public void Start()
        {
            if (_isMonitoring) return;
            _isMonitoring = true;
            _uiThread = Application.Current?.Dispatcher?.Thread;
            if (Application.Current?.Dispatcher != null)
            {
                Application.Current.Dispatcher.InvokeAsync(() => 
                { 
                    _uiThreadOsId = GetCurrentThreadId(); 
                });
            }
            
            _ = Task.Run(async () =>
            {
                var sw = new Stopwatch();
                var rateLimitedSw = Stopwatch.StartNew();
                int consecutiveFreezes = 0;
                
                while (!_cts.Token.IsCancellationRequested)
                {
                    if (_cts.Token.IsCancellationRequested) break;

                    // Sondagem adaptativa: com a janela minimizada/oculta o probe não
                    // agrega informação (a UI thread não processa nada de forma útil) e ainda
                    // gera custo fixo de CPU. Mantemos o monitor ATIVO, apenas com cadência
                    // maior. Com a janela visível o intervalo original de 500 ms é preservado,
                    // portanto a detecção de freeze do usuário continua idêntica.
                    int waitMs = IsUiRelevantNow() ? _checkIntervalMs : Math.Max(_checkIntervalMs * 4, 2000);
                    try
                    {
                        await Task.Delay(waitMs, _cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    if (_cts.Token.IsCancellationRequested) break;

                    // Rate-limit: no máximo 1 diagnóstico completo a cada 30s para não sobrecarregar logs
                    bool doFullDiagnostics = rateLimitedSw.ElapsedMilliseconds >= 30000;
                    
                    sw.Restart();
                    bool responded = false;

                    try
                    {
                        // CORREÇÃO DE FALSO POSITIVO (FASE 1):
                        // O probe era enfileirado em DispatcherPriority.Background (prioridade
                        // 4, baixa). O Dispatcher só executa itens dessa prioridade quando
                        // não há nada mais importante — logo, uma UI thread OCUPADA mas
                        // responsiva (processando layout, input, ou trabalho de maior
                        // prioridade) fazia o probe esperar >1s e ser reportada como
                        // "congelada", mesmo estando respondedora.
                        //
                        // Evidência medida: o stack capturado durante os "freezes" era
                        //   Dispatcher.PushFrameImpl -> Dispatcher.GetMessage -> MSG
                        // ou seja, a UI thread estava PARADA em GetMessage esperando
                        // mensagem — o oposto de um travamento.
                        //
                        // O probe correto usa prioridade ALTA: uma UI thread travada não
                        // executa item de nenhuma prioridade; uma UI thread apenas ocupada
                        // executa um item de prioridade alta imediatamente. Isso mede
                        // responsividade real, em vez de escassez de prioridade.
                        var operation = Application.Current?.Dispatcher?.BeginInvoke(DispatcherPriority.Input, (Action)(() =>
                        {
                            responded = true;
                        }));

                        if (operation != null)
                        {
                            var status = operation.Wait(TimeSpan.FromMilliseconds(_warnThresholdMs));
                            if (status == DispatcherOperationStatus.Completed)
                            {
                                _uiResponseCount++;
                                consecutiveFreezes = 0;
                                if (sw.ElapsedMilliseconds > _warnThresholdMs)
                                {
                                    _logger?.LogWarning($"[UI_MONITOR] UI thread respondeu em {sw.ElapsedMilliseconds}ms (acima do threshold)");
                                }
                            }
                            else
                            {
                                long elapsed = sw.ElapsedMilliseconds;
                                
                                // Ignorar falsos positivos durante shutdown do app
                                // (Dispatcher aborta operações pendentes com status Aborted ou flag HasShutdownStarted)
                                bool isShutdown = status == DispatcherOperationStatus.Aborted ||
                                                  Application.Current == null ||
                                                  Application.Current.Dispatcher.HasShutdownStarted;

                                if (isShutdown)
                                {
                                    _logger?.LogInfo($"[UI_MONITOR] Operação abortada em {elapsed}ms (shutdown do Dispatcher detectado) — parando monitor");
                                    break;
                                }
                                
                                Interlocked.Increment(ref _uiFreezeCount);
                                consecutiveFreezes++;
                                
                                // Só logar diagnóstico completo se for o primeiro freeze de uma série
                                // ou se passou o rate-limit de 10s
                                if (consecutiveFreezes <= 1 || doFullDiagnostics)
                                {
                                    // ====== CAPTURAR STACK TRACE DA UI THREAD ======
                                    string stackInfo = "Stack trace indisponível";
                                    try
                                    {
                                        stackInfo = CaptureUIThreadStack();
                                    }
                                    catch (Exception stEx)
                                    {
                                        stackInfo = $"Erro ao capturar stack: {stEx.Message}";
                                    }
                                    
                                    // Se a UI travou por menos de 5 segundos, é apenas um LAG. Se for mais, é um travamento severo.
                                    if (elapsed > 5000)
                                    {
                                        // Freeze prolongado durante a inicialização é ESPERADO: a primeira
                                        // renderização/layout do MainWindow (Window.Show) roda na UI thread
                                        // antes de exibir qualquer conteúdo, período em que o usuário não
                                        // interage. Classificar como Warning diagnóstico, não como Error.
                                        bool isStartupWarmup = _uiFreezeCount <= 1 &&
                                                               (DateTime.UtcNow - _processStartUtc).TotalSeconds < 60;
                                        if (isStartupWarmup)
                                        {
                                            _logger?.LogWarning($"[UI_MONITOR] UI THREAD SOB CARGA DURANTE INICIALIZAÇÃO por {elapsed}ms " +
                                                $"(warm-up — primeira renderização). Total freezes: {_uiFreezeCount}. " +
                                                $"[DIAGNOSTICO]:\n{stackInfo}");
                                        }
                                        else
                                        {
                                            _logger?.LogError($"[UI_MONITOR] UI THREAD SEM RESPOSTA por {elapsed}ms! " +
                                                $"Total freezes: {_uiFreezeCount}, " +
                                                $"Consecutive: {consecutiveFreezes}, " +
                                                $"MonitorThread: {Thread.CurrentThread.ManagedThreadId}\n" +
                                                $"[UI_MONITOR] DIAGNOSTICO DA UI THREAD:\n{stackInfo}");
                                        }
                                    }
                                    else
                                    {
                                        _logger?.LogWarning($"[UI_MONITOR] LENTIDAO NA UI THREAD por {elapsed}ms (Lag temporario) " +
                                            $"Total freezes: {_uiFreezeCount}. " +
                                            $"[DIAGNOSTICO]:\n{stackInfo}");
                                    }
                                    
                                    rateLimitedSw.Restart();
                                }
                                else
                                {
                                    // Log compacto para freezes consecutivos
                                    _logger?.LogInfo($"[UI_MONITOR] Freeze consecutivo #{consecutiveFreezes}: {elapsed}ms (total: {_uiFreezeCount})");
                                }

                                // Também gravar em arquivo direto para garantir persistência
                                try
                                {
                                    var logDir = LogDirectoryResolver.Resolve();
                                    System.IO.Directory.CreateDirectory(logDir);
                                    var path = System.IO.Path.Combine(logDir, $"UIFreeze_{DateTime.Now:yyyy-MM-dd}.log");
                                    var entry = $"[{DateTime.Now:HH:mm:ss.fff}] FREEZE #{_uiFreezeCount} ({elapsed}ms)\n---\n";
                                    System.IO.File.AppendAllText(path, entry, System.Text.Encoding.UTF8);
                                }
                                catch { }

                                // Se 5+ freezes consecutivos, pausar o monitor por tempo fixo
                                // para não piorar o problema de performance durante startup pesado.
                                if (consecutiveFreezes >= 5 && elapsed < _checkIntervalMs + 500)
                                {
                                    _logger?.LogWarning($"[UI_MONITOR] {consecutiveFreezes} freezes consecutivos — pausando monitor por 15000ms");
                                    await Task.Delay(15000, _cts.Token);
                                    consecutiveFreezes = 0;
                                    continue;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError($"[UI_MONITOR] Erro no monitor: {ex.Message}");
                    }
                }
            }, _cts.Token);
        }
        
        /// <summary>
        /// Captura o diagnóstico da UI thread.
        /// </summary>
        private string CaptureUIThreadStack()
        {
            if (_uiThread == null)
                return "UI Thread reference não capturada";
            
            try
            {
                var proc = Process.GetCurrentProcess();
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"  Process: {proc.ProcessName} (PID: {proc.Id})");
                sb.AppendLine($"  Working Set: {proc.WorkingSet64 / 1024 / 1024}MB");
                sb.AppendLine($"  Thread Count: {proc.Threads.Count}");
                sb.AppendLine($"  Responding: {proc.Responding}");
                sb.AppendLine($"  UI Thread ManagedId: {_uiThread.ManagedThreadId}");
                sb.AppendLine($"  UI Thread State: {_uiThread.ThreadState}");
                
                // Em .NET 8.0, StackTrace(Thread) não é suportado para outras threads.
                // Logamos o estado de todas as threads do processo para ver quem está consumindo CPU.
                sb.AppendLine("  === SNAPSHOT CLRMD (STACK TRACE DA UI THREAD) ===");
                bool clrmdOk = false;
                try
                {
                    using (var dataTarget = Microsoft.Diagnostics.Runtime.DataTarget.CreateSnapshotAndAttach(Process.GetCurrentProcess().Id))
                    {
                        var runtime = dataTarget.ClrVersions.FirstOrDefault()?.CreateRuntime();
                        if (runtime != null)
                        {
                            var clrThread = runtime.Threads.FirstOrDefault(t => t.OSThreadId == _uiThreadOsId);
                            if (clrThread != null)
                            {
                                int frames = 0;
                                foreach (var frame in clrThread.EnumerateStackTrace())
                                {
                                    if (frames++ >= 25) { sb.AppendLine("    ... (truncado)"); break; }
                                    sb.AppendLine($"    {frame.Method?.ToString() ?? "<metodo desconhecido>"}");
                                }
                                clrmdOk = frames > 0;
                            }
                            else
                            {
                                sb.AppendLine("    [ClrMD] UI Thread nao encontrada no Runtime.");
                            }
                        }
                        else
                        {
                            sb.AppendLine("    [ClrMD] CLR Runtime nao encontrado.");
                        }
                    }
                }
                catch (Exception clrmdEx)
                {
                    // ClrMD nao e confiavel: auto-attach pode falhar por versao do CLR, falta de
                    // privilegio ou simbolos. Antes isso era engolido e o log "[DIAGNOSTICO]:"
                    // saia VAZIO, deixando todo freeze sem diagnostico. Fallback abaixo.
                    sb.AppendLine($"    [ClrMD indisponivel] {clrmdEx.GetType().Name}: {clrmdEx.Message}");
                }

                if (!clrmdOk)
                {
                    // FALLBACK FIÁVEL: o probe usa DispatcherPriority.Background, que só é processado
                    // DEPOIS que todas as operações de UI de prioridade maior terminam. Logo, se o
                    // probe não rodou, a UI thread estava ocupada executando código de aplicação
                    // (não parada esperando eventos) — e a origem dessa espera é registrada pela
                    // telemetria de operações de UI.
                    sb.AppendLine("    [Origem provavel] A UI thread estava ocupada executando codigo de aplicacao.");
                    sb.AppendLine("    [UI-OP] BEGIN/END markers no voltris.log.txt identificam a operacao exata.");

                    try
                    {
                        var currentOp = VoltrisDiagnosticSystem.Instance.CurrentUIOperationName;
                        if (!string.IsNullOrEmpty(currentOp))
                            sb.AppendLine($"    [UI-OP] Operacao de UI em andamento: {currentOp}");
                    }
                    catch
                    {
                        // best-effort
                    }
                }
                sb.AppendLine("  ===================================================");
                
                sb.AppendLine($"  Top Threads (CPU Usage):");
                var threadStats = new System.Collections.Generic.List<(int Id, string isUI, string State, string Wait, double Cpu)>();
                
                foreach (System.Diagnostics.ProcessThread pt in proc.Threads)
                {
                    try
                    {
                        var cpu = pt.TotalProcessorTime.TotalMilliseconds;
                        var state = pt.ThreadState.ToString();
                        var wait = pt.ThreadState == System.Diagnostics.ThreadState.Wait ? pt.WaitReason.ToString() : "";
                        var isUI = (pt.Id == _uiThreadOsId) ? " [UI THREAD]" : "";
                        
                        threadStats.Add((pt.Id, isUI, state, wait, cpu));
                    }
                    catch
                    {
                        // Thread exited or access denied
                    }
                }
                
                var topThreads = threadStats.OrderByDescending(t => t.Cpu).Take(10);

                foreach (var pt in topThreads)
                {
                    sb.AppendLine($"    TID:{pt.Id}{pt.isUI} State:{pt.State} Wait:{pt.Wait} CPU:{pt.Cpu:F0}ms");
                }
                
                return sb.ToString();
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("exited") || ex is InvalidOperationException)
                {
                    return "Diagnóstico indisponível: A thread principal foi encerrada antes da captura do estado.";
                }
                return $"Erro ao capturar diagnóstico: {ex.Message}";
            }
        }

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();



        public void LogReport()
        {
            _logger?.LogInfo($"[UI_MONITOR] Relatório: {_uiResponseCount} checks OK, {_uiFreezeCount} freezes detectados");
        }
        /// <summary>
        /// Stops the monitor prematurely without disposing.
        /// Useful for application shutdown.
        /// </summary>
        public void StopMonitoring()
        {
            _cts.Cancel();
        }

        public void Dispose()
        {
            _isMonitoring = false;
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
