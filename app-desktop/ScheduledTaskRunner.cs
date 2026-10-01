using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Logging;

namespace VoltrisOptimizer
{
    /// <summary>
    /// Executa uma tarefa agendada do VOLTRIS em modo headless, invocado pelo
    /// Agendador de Tareças do Windows como:
    /// <c>VoltrisOptimizer.exe --run-scheduled &lt;taskId&gt;</c>.
    ///
    /// POR QUE EXISTE:
    /// Sem ele, uma tarefa agendada só funcionaria enquanto a interface estivesse
    /// aberta, porque o <see cref="SchedulerService"/> interno depende do processo.
    /// Este runner faz o VOLTRIS realmente executar com a janela fechada e após
    /// reiniciar o Windows.
    ///
    /// PRINCÍPIOS:
    ///  - Nenhuma UI, nenhum App, nenhum MessageBox: é um processo de console-ish
    ///    que apenas loga e executa. Uma tarefa agendada não pode abrir janelas.
    ///  - Reutiliza EXATAMENTE os mesmos serviços registrados no DI da aplicação,
    ///    via <see cref="ServiceCollectionExtensions.AddVoltrisServices"/>. Não existe
    ///    uma segunda implementação de limpeza/otimização.
    ///  - Falha visível: sempre grava o desfecho no log e em schedules.json, e
    ///    devolve um exit code coerente para o Task Scheduler registrar o resultado.
    /// </summary>
    internal static class ScheduledTaskRunner
    {
        private static readonly string HeadlessLogPath = Path.Combine(LogDirectoryResolver.Resolve(), "scheduled_runs.log");

        /// <summary>
        /// Executa a tarefa e devolve o exit code:
        /// 0 = sucesso, 1 = falha total/parcial, 2 = tarefa não encontrada ou erro de infraestrutura.
        /// </summary>
        /// <param name="taskId">Id da tarefa agendada.</param>
        /// <param name="taskPayloadBase64">
        /// Definição da tarefa em Base64(UTF8 JSON), embutida nos argumentos da tarefa
        /// do Windows. É a REDE DE SEGURANÇA contra o problema de contexto de conta:
        /// a tarefa roda como LOCAL SYSTEM e não tem acesso garantido ao
        /// schedules.json do usuário, mas a definição viaja na própria linha de
        /// comando. Quando presente, esta fonte tem precedência sobre o JSON.
        /// </param>
        public static int Run(string taskId, string? taskPayloadBase64 = null)
        {
            var sw = Stopwatch.StartNew();
            string header = $"=== HEADLESS RUN taskId={taskId} start={DateTime.Now:yyyy-MM-dd HH:mm:ss} ===";

            ILoggingService? logger = null;
            ServiceProvider? provider = null;
            SchedulerService? scheduler = null;

            try
            {
                WriteHeadlessLog(header);
                WriteHeadlessLog($"user={SafeUserName()} admin={SchedulerService.IsRunningAsAdmin()} session={SafeSessionId()}");
                WriteHeadlessLog($"dataRoot={AppDataPaths.EffectiveUnifiedRoot} payload={(string.IsNullOrWhiteSpace(taskPayloadBase64) ? "ausente" : "presente")}");

                // 1) Logging no MESMO diretório da aplicação (Logs na raiz do programa).
                //    Mantido idêntico ao caminho usado pelo logging interativo, de modo
                //    que a infraestrutura de logs continue funcionando como sempre.
                var logDir = LogDirectoryResolver.Resolve();
                Directory.CreateDirectory(logDir);
                logger = new ProfessionalLoggingService(logDir);
                logger.Log(LogLevel.Info, LogCategory.Scheduler,
                    $"[HeadlessScheduler] Execução agendada iniciada. taskId={taskId}");

                // 2) DI mínimo: somente o que o SchedulerService e as ações exigem.
                var services = new ServiceCollection();
                services.AddVoltrisServices();
                provider = services.BuildServiceProvider();

                // O ProfessionalLoggingService do contêiner é o mesmo que acabamos
                // de criar; o restante da pilha de logs permanece intacta.
                var history = provider.GetService<HistoryService>();
                if (history == null)
                    throw new InvalidOperationException("HistoryService não pôde ser resolvido.");

                scheduler = provider.GetService<SchedulerService>()
                           ?? throw new InvalidOperationException("SchedulerService não pôde ser resolvido.");

                // 3) Propaga as dependências das ações para App.*, que é a porta de
                //    entrada usada pelo restante do aplicativo. Sem WPF envolvido.
                PopulateStaticServices(provider, logger, history);

                var task = ResolveTask(taskId, taskPayloadBase64, scheduler)
                           ?? throw new InvalidOperationException(
                               $"Tarefa '{taskId}' não encontrada nem no payload embutido nem em schedules.json " +
                               $"(raia lida: {AppDataPaths.EffectiveUnifiedRoot}).");

                // A definicao veio do proprio Agendador do Windows; se o Id divergir,
                // e' sinal de payload corrompido ou reutilizado. Recusar em vez de
                // executar as acoes de outra tarefa.
                if (!string.Equals(task.Id, taskId, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Payload embutido pertence à tarefa '{task.Id}', mas o agendador pediu '{taskId}'. Execução abortada.");
                }

                if (!task.IsEnabled)
                {
                    logger.Log(LogLevel.Warning, LogCategory.Scheduler,
                        $"[HeadlessScheduler] Tarefa '{task.Name}' está desabilitada. Execução abortada pelo próprio Windows/agendador.");
                    WriteHeadlessLog($"-- Tarefa desabilitada: {task.Name}");
                    return 0;
                }

                logger.Log(LogLevel.Info, LogCategory.Scheduler,
                    $"[HeadlessScheduler] Tarefa encontrada: '{task.Name}' tipo={task.ScheduleType} " +
                    $"acoes=[{string.Join(", ", task.Actions)}]");

                // 4) Execução: o MESMO caminho usado pelo botão "Executar agora"
                //    e pela malha in-process. Sem bypass de verificações.
                var result = scheduler.RunNowAsync(task, HistoryOrigin.Scheduled, CancellationToken.None)
                                  .GetAwaiter().GetResult();

                sw.Stop();
                result.Duration = sw.Elapsed;

                var status = result.Success ? "OK" : (result.Skipped ? "SKIPPED" : "FAILED");
                logger.Log(result.Success ? LogLevel.Success : LogLevel.Warning, LogCategory.Scheduler,
                    $"[HeadlessScheduler] Resultado {status}: '{task.Name}' em {sw.Elapsed.TotalSeconds:F1}s " +
                    $"liberado={result.SpaceFreed}bytes erro='{result.Error}'");
                WriteHeadlessLog($"-- Resultado {status} em {sw.Elapsed.TotalSeconds:F1}s liberado={result.SpaceFreed} erro={result.Error}");

                return result.Success ? 0 : 1;
            }
            catch (Exception ex)
            {
                var msg = $"FALHA CRÍTICA na execução headless: {ex.GetType().Name}: {ex.Message}";
                WriteHeadlessLog("!! " + msg);
                WriteHeadlessLog(ex.ToString());
                try { logger?.LogCritical("[HeadlessScheduler] " + msg, ex); }
                catch { /* nada a fazer */ }
                try { VoltrisOptimizer.Services.TelegramLogger.SendMessageFireAndForget($"⚠️ <b>VOLTRIS — tarefa agendada falhou</b>\n<pre>{msg}</pre>", true); }
                catch { }
                return 2;
            }
            finally
            {
                sw.Stop();
                try
                {
                    logger?.Log(LogLevel.Info, LogCategory.Scheduler,
                        $"[HeadlessScheduler] Execução headless encerrada em {sw.Elapsed.TotalSeconds:F1}s.");
                    logger?.Flush();
                    logger?.Dispose();
                }
                catch { }
                try { provider?.Dispose(); } catch { }
                WriteHeadlessLog($"=== HEADLESS RUN end taskId={taskId} elapsed={sw.Elapsed.TotalSeconds:F1}s ===");
            }
        }

        /// <summary>
        /// Popula as propriedades estáticas de <see cref="App"/> usadas pelas ações
        /// agendadas. Espelha o que <c>App.PopulateStaticServicesAsync</c> faz na
        /// aplicação interativa, mas apenas com os serviços necessários — sem
        /// criar a instância de <see cref="App"/> (que dispararia o WPF).
        /// </summary>
        private static void PopulateStaticServices(IServiceProvider sp, ILoggingService logger, HistoryService history)
        {
            App.SetLoggingServiceForHeadless(logger);
            App.SetHistoryServiceForHeadless(history);

            App.SystemCleaner = SafeResolve<SystemCleaner>(sp, "SystemCleaner");
            App.PerformanceOptimizer = SafeResolve<VoltrisOptimizer.Services.VoltrisPerformanceOptimizer>(sp, "PerformanceOptimizer");
            App.NetworkOptimizer = SafeResolve<NetworkOptimizer>(sp, "NetworkOptimizer");
            App.AdvancedOptimizer = SafeResolve<AdvancedOptimizer>(sp, "AdvancedOptimizer");
            App.UltraCleaner = SafeResolve<UltraCleanerService>(sp, "UltraCleaner");
            App.ThermalMonitorService = SafeResolve<VoltrisOptimizer.Services.Thermal.IGlobalThermalMonitorService>(sp, "ThermalMonitorService");
            App.TelemetryService = SafeResolve<VoltrisOptimizer.Services.Telemetry.TelemetryService>(sp, "TelemetryService");
        }

        /// <summary>
        /// Resolve a definição da tarefa, priorizando o payload embutido nos argumentos
        /// da tarefa do Windows e caindo para o schedules.json do usuário.
        ///
        /// A ordem é deliberada: o payload é a fonte que funciona MESMO quando o
        /// processo roda em outra conta (SYSTEM), porque não depende de permissões
        /// de leitura sobre o perfil do usuário. O JSON é o fallback para execução
        /// manual ("--run-scheduled &lt;id&gt;" digitado à mão, depuração).
        /// </summary>
        private static ScheduledTask? ResolveTask(string taskId, string? payloadBase64, SchedulerService? fallback)
        {
            if (!string.IsNullOrWhiteSpace(payloadBase64))
            {
                try
                {
                    var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payloadBase64.Trim()));
                    var fromPayload = System.Text.Json.JsonSerializer.Deserialize<ScheduledTask>(json);
                    if (fromPayload != null)
                    {
                        WriteHeadlessLog($"Tarefa resolvida pelo payload embutido: '{fromPayload.Name}' tipo={fromPayload.ScheduleType}");
                        return fromPayload;
                    }
                    WriteHeadlessLog("!! Payload embutido desserializou para null; usando schedules.json.");
                }
                catch (Exception ex)
                {
                    // Um payload inválido NÃO pode impedir a execução: o JSON ainda
                    // pode estar acessível. Registra e segue para o fallback.
                    WriteHeadlessLog($"!! Payload embutido inválido ({ex.GetType().Name}: {ex.Message}); usando schedules.json.");
                }
            }

            if (fallback == null) return null;

            try
            {
                var fromJson = fallback.GetTask(taskId);
                if (fromJson != null)
                    WriteHeadlessLog($"Tarefa resolvida por schedules.json: '{fromJson.Name}' tipo={fromJson.ScheduleType}");
                else
                    WriteHeadlessLog($"!! schedules.json ({AppDataPaths.EffectiveUnifiedRoot}) não contém a tarefa '{taskId}'.");
                return fromJson;
            }
            catch (Exception ex)
            {
                WriteHeadlessLog($"!! Falha ao ler schedules.json: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }
        private static T? SafeResolve<T>(IServiceProvider sp, string label) where T : class
        {
            try
            {
                return sp.GetService<T>();
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning(
                    $"[HeadlessScheduler] Não foi possível resolver '{label}': {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        private static void WriteHeadlessLog(string message)
        {
            try
            {
                var dir = Path.GetDirectoryName(HeadlessLogPath);
                if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                FileHelper.AppendTextShared(HeadlessLogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}][T{Environment.CurrentManagedThreadId}] {message}");
            }
            catch
            {
                // Logging jamais pode derrubar a execução agendada.
            }
        }

        private static string SafeUserName()
        {
            try { return Environment.UserName; } catch { return "desconhecido"; }
        }

        private static string SafeSessionId()
        {
            try { return System.Diagnostics.Process.GetCurrentProcess().SessionId.ToString(); } catch { return "?"; }
        }
    }
}
