using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer
{
    public static class Program
    {
        private static readonly string _crashLogPath = Path.Combine(LogDirectoryResolver.Resolve(), "CRASH_TRACE.log");

        // Mutex global de instância única — prefixo "Global\\" garante funcionamento
        // mesmo quando iniciado via TaskScheduler em sessões de usuário distintas.
        // Mantido vivo durante toda a execução para evitar GC prematuro.
        private static Mutex? _singleInstanceMutex;

        /// <summary>
        /// Argumento usado pelo Agendador de Tareças do Windows para executar uma
        /// tarefa agendada com o VOLTRIS fechado. Definido aqui para que
        /// Program e Scheduler usem exatamente o mesmo contrato.
        /// </summary>
        public const string RunScheduledArg = "--run-scheduled";

        /// <summary>
        /// Caminho da raiz de dados do USUÁRIO que criou a tarefa.
        ///
        /// A tarefa roda como LOCAL SYSTEM, e SpecialFolder.LocalApplicationData
        /// sob SYSTEM aponta para systemprofile — não para o perfil do usuário. Sem
        /// este argumento o runner procuraria schedules.json num diretório vazio e
        /// todas as tarefas agendadas falhariam com "não encontrada".
        /// </summary>
        public const string DataRootArg = "--data-root";

        /// <summary>
        /// Definição completa da tarefa em Base64(UTF8 JSON), embutida nos argumentos
        /// da tarefa do Windows. Torna a execução autossuficiente: mesmo que o JSON do
        /// usuário esteja inacessível, o runner sabe exatamente o que executar.
        /// </summary>
        public const string TaskPayloadArg = "--task-payload";

        [STAThread]
        public static void Main(string[] args)
        {
            // CORREÇÃO CRÍTICA: Garantir que o diretório de trabalho seja o da aplicação
            // Isso previne "DllNotFoundException: vcruntime140_cor3.dll" quando iniciado pelo TaskScheduler ou atalho
            Environment.CurrentDirectory = AppContext.BaseDirectory;

            // ─────────────────────────────────────────────────────────────────────
            // MODO HEADLESS DE TAREFA AGENDADA
            // O Agendador do Windows invoca:
            //   VoltrisOptimizer.exe --run-scheduled <id> --data-root <raiz> --task-payload <b64>
            //
            // Deve ser tratado ANTES do mutex de instância única, porque:
            //  (1) o VOLTRIS pode estar aberto — e mesmo assim a tarefa precisa rodar;
            //  (2) iniciar o WPF para executar uma limpeza desperdiça memória e
            //      mostra uma janela fantasma.
            // Nenhuma UI é criada neste caminho: apenas logging + serviços + histórico.
            // ─────────────────────────────────────────────────────────────────────
            string? scheduledTaskId = TryExtractArgumentValue(args, RunScheduledArg);
            if (scheduledTaskId != null)
            {
                // A ordem importa: o override da raiz tem de entrar em vigor ANTES
                // de qualquer serviço tocar AppDataPaths (o resolvedor é preguiçoso,
                // por isso ainda não houve acesso neste ponto).
                string? dataRoot = TryExtractArgumentValue(args, DataRootArg);
                if (string.IsNullOrWhiteSpace(dataRoot))
                {
                    WriteCrashLog("HEADLESS_MISSING_DATA_ROOT — tarefa agendada sem --data-root; " +
                                  $"a conta em execução ({SafeAccountHint()}) pode não enxergar o perfil do usuário.");
                }
                else
                {
                    try
                    {
                        AppDataPaths.ApplyDataRootOverride(dataRoot);
                    }
                    catch (Exception ex)
                    {
                        WriteCrashLog($"HEADLESS_DATA_ROOT_FAILED — {ex.GetType().Name}: {ex.Message}");
                    }
                }

                string? payload = TryExtractArgumentValue(args, TaskPayloadArg);
                int exitCode = ScheduledTaskRunner.Run(scheduledTaskId, payload);
                Environment.Exit(exitCode);
                return;
            }

            RunInteractive();
        }

        private static string SafeAccountHint()
        {
            try
            {
                using var who = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("whoami")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                });
                return who?.StandardOutput.ReadToEnd().Trim() ?? "desconhecida";
            }
            catch { return "desconhecida"; }
        }

        /// <summary>
        /// Extrai o valor de <c>--nome valor</c> ou <c>--nome=valor</c> dos argumentos.
        /// Tolera aspas porque o Agendador do Windows as repassa literalmente.
        /// </summary>
        private static string? TryExtractArgumentValue(string[]? args, string name)
        {
            if (args == null || args.Length == 0) return null;

            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (string.IsNullOrWhiteSpace(arg)) continue;

                if (arg.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length && !string.IsNullOrWhiteSpace(args[i + 1]))
                        return args[i + 1].Trim().Trim('"');
                    return null;
                }

                // Forma --nome=valor
                var prefix = name + "=";
                if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var value = arg.Substring(prefix.Length).Trim().Trim('"');
                    return string.IsNullOrWhiteSpace(value) ? null : value;
                }
            }

            return null;
        }

        private static void RunInteractive()
        {
            // CORREÇÃO FORENSE #7: Verificação de instância única no ponto mais
            // cedo possível — antes de qualquer inicialização do WPF ou serviços.
            // O Semaphore existente no OnStartup detectava o problema tarde demais:
            // ambas as instâncias já estavam inicializando, causando o boot de 22s
            // e os 7 UI freezes consecutivos registrados em 12/08.
            _singleInstanceMutex = new Mutex(initiallyOwned: true, name: "Global\\VoltrisOptimizer_SingleInstance_v2", createdNew: out bool isNewInstance);

            if (!isNewInstance)
            {
                // Segunda instância detectada — sair IMEDIATAMENTE sem qualquer
                // inicialização para não desperdiçar recursos.
                WriteCrashLog("PROGRAM_MAIN_START_DUPLICATE — instância já em execução. Encerrando.");
                try { _singleInstanceMutex.Dispose(); } catch { }
                return;
            }

            // Registrar liberação do Mutex ao encerrar o processo (incluindo kill externo)
            AppDomain.CurrentDomain.ProcessExit += (_, __) =>
            {
                try { _singleInstanceMutex?.ReleaseMutex(); _singleInstanceMutex?.Dispose(); } catch { }
            };

            WriteCrashLog("PROGRAM_MAIN_START");
            try
            {
                WriteCrashLog("BEFORE_APP_CTOR");
                var app = new App();
                WriteCrashLog("AFTER_APP_CTOR");

                WriteCrashLog("BEFORE_INIT_COMPONENT");
                app.InitializeComponent();
                WriteCrashLog("AFTER_INIT_COMPONENT");

                WriteCrashLog("BEFORE_APP_RUN");
                app.Run();
                WriteCrashLog("AFTER_APP_RUN");
            }
            catch (Exception ex)
            {
                WriteCrashLog($"CRASH: {ex.GetType().Name}: {ex.Message}");
                WriteCrashLog($"STACK: {ex.StackTrace}");

                try
                {
                    File.WriteAllText("STARTUP_ERROR.log", ex.ToString());
                }
                catch (Exception ex2) { System.Diagnostics.Debug.WriteLine($"[Program] WriteStartupError: {ex2.Message}"); }

                try
                {
                    MessageBox.Show(ex.ToString(), VoltrisOptimizer.Services.LocalizationService.Instance.GetString("VoltrisFatalError"));
                }
                catch (Exception ex3) { System.Diagnostics.Debug.WriteLine($"[Program] ShowFatalError: {ex3.Message}"); }
            }
        }

        public static void WriteCrashLog(string message)
        {
            try
            {
                var dir = Path.GetDirectoryName(_crashLogPath);
                if (dir != null && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                FileHelper.AppendTextShared(_crashLogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}][T{Environment.CurrentManagedThreadId}] {message}");

                if (message.StartsWith("CRASH:"))
                {
                    VoltrisOptimizer.Services.TelegramLogger.SendMessageFireAndForget($"❌ <b>Voltris Optimizer CRASH</b>\n<b>Máquina:</b> {Environment.MachineName}\n<pre>{message}</pre>", true);
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Program] WriteCrashLog: {ex.Message}"); }
        }
    }
}
