using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Scheduler
{
    /// <summary>
    /// Ponte para o Agendador de Tarefas do Windows (Task Scheduler).
    ///
    /// POR QUE ISTO EXISTE:
    /// O agendamento do VOLTRIS precisa CONTINUAR FUNCIONANDO quando:
    ///   (a) o aplicativo está fechado / minimizado para bandeja;
    ///   (b) o Windows foi reiniciado e o VOLTRIS não foi aberto;
    ///   (c) o computador estava desligado/hibernado no horário agendado.
    /// Um System.Timers.Timer dentro do processo NÃO satisfaz nenhum desses cenários:
    /// o timer morre junto com o processo. Por isso cada tarefa é materializada como
    /// uma tarefa REAL do Windows que invoca o mesmo executável com o argumento
    /// <c>--run-scheduled &lt;taskId&gt;</c>. A execução em si é feita pelo MESMO
    /// código de serviços já existente (<see cref="SchedulerService.ExecuteTaskActions"/>),
    /// portanto não há implementação paralela.
    ///
    /// IMPLEMENTAÇÃO:
    /// Registro via `schtasks.exe /Create /XML`, e não via COM. Motivos:
    ///   1. `/XML` expõe tudo que precisamos e que a forma `/SC` não permite
    ///      (dias da semana, dia do mês, StartWhenAvailable, MultipleInstances,
    ///       DisallowStartIfOnBatteries=false, RestartOnFailure);
    ///   2. o código de saída de schtasks é verificável, o que permite exigir
    ///      comprovação real de registro — em vez de assumir sucesso;
    ///   3. dispensa P/Invoke e não depende da apartment-threaded COM do Task
    ///      Scheduler 2.0 (que exige STA e falha em cenários de thread de fundo).
    ///
    /// SEGURANÇA: a conta de execução é LOCAL SYSTEM (SID S-1-5-18) — único modo que
    /// garante execução com o VOLTRIS fechado, sem senha armazenada e com elevação
    /// completa. Nenhuma senha é usada ou registrada, e o XML gerado não contém credenciais.
    ///
    /// CONSEQUÊNCIA DE USAR SYSTEM (e como é resolvida):
    ///   Sob LOCAL SYSTEM, Environment.SpecialFolder.LocalApplicationData resolve para
    ///   C:\Windows\System32\config\systemprofile\AppData\Local, e NÃO para o perfil do
    ///   usuário. O runner, portanto, não encontraria schedules.json e TODA tarefa
    ///   agendada terminaria em "não encontrada" (exit 2) sem erro visível na interface.
    ///   A correção é dupla e está em <see cref="BuildTaskArguments"/>:
    ///     (1) --data-root leva explicitamente a raiz do usuário que criou a tarefa;
    ///     (2) --task-payload leva a definição inteira em Base64, tornando a execução
    ///         independente de qualquer leitura de arquivo.
    ///   Ambas são verificadas no read-back por <see cref="DefinitionMatches"/>.
    /// </summary>
    internal static class WindowsTaskSchedulerBridge
    {
        private const string TaskFolderName = "Voltris";
        private const string SystemAccountSid = "S-1-5-18"; // LOCAL SYSTEM
        private static readonly ILoggingService? Log = App.LoggingService;
        private static readonly object ExistenceCacheLock = new object();
        private static string? _exePathCache;

        /// <summary>
        /// Verifica se o mecanismo está utilizável nesta máquina.
        /// </summary>
        public static bool IsSupported(out string reason)
        {
            if (!OperatingSystem.IsWindows())
            {
                reason = "Fora do Windows.";
                return false;
            }

            var probe = RunSchtasks("/Query /FO LIST", 15_000, out _, out var probeError, out _);
            if (probe)
            {
                reason = string.Empty;
                return true;
            }

            reason = string.IsNullOrEmpty(probeError)
                ? "Agendador de Tareças do Windows indisponível (schtasks.exe)."
                : $"Agendador de Tareças do Windows indisponível: {probeError}";
            return false;
        }

        /// <summary>
        /// Cria ou atualiza a tarefa do Windows correspondente a uma tarefa agendada do VOLTRIS.
        /// Retorna <c>true</c> somente após reler o registro e confirmar que a definição
        /// gravada corresponde ao esperado.
        /// </summary>
        public static bool RegisterTask(ScheduledTask task, out string error)
        {
            error = string.Empty;

            if (!IsSupported(out error)) return false;

            var exePath = ResolveExecutablePath();
            if (string.IsNullOrEmpty(exePath))
            {
                error = "Não foi possível localizar o executável do VOLTRIS para registrar a tarefa.";
                Log?.Log(LogLevel.Error, LogCategory.Scheduler,
                    $"[TaskSchedulerBridge] {error}");
                return false;
            }

            string xml;
            try
            {
                xml = BuildTaskXml(task, exePath);
            }
            catch (Exception ex)
            {
                error = $"Falha ao gerar a definição XML da tarefa: {ex.Message}";
                Log?.Log(LogLevel.Error, LogCategory.Scheduler,
                    $"[TaskSchedulerBridge] {error}", ex);
                return false;
            }

            var windowsName = GetWindowsTaskName(task.Id);
            var fullName = $@"\{TaskFolderName}\{windowsName}";

            string xmlPath;
            try
            {
                xmlPath = Path.Combine(Path.GetTempPath(), $"voltris_task_{windowsName}.xml");
                // schtasks /XML exige o arquivo em Unicode (UTF-16), coerente com a
                // declaração <?xml ... encoding="UTF-16"?> gerada abaixo.
                File.WriteAllText(xmlPath, xml, new UnicodeEncoding(false, true));
            }
            catch (Exception ex)
            {
                error = $"Não foi possível gravar o XML temporário da tarefa: {ex.Message}";
                return false;
            }

            try
            {
                var args = $"/Create /TN \"{fullName}\" /XML \"{xmlPath}\" /F";
                var ok = RunSchtasks(args, 30_000, out var stdout, out var stderr, out var exitCode);

                if (!ok)
                {
                    error = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                    Log?.Log(LogLevel.Error, LogCategory.Scheduler,
                        $"[TaskSchedulerBridge] schtasks /Create falhou (exit={exitCode}) para '{task.Name}': {error}");
                    return false;
                }

                // VERIFICAÇÃO REAL: relê a definição registrada e confere gatilho e ação.
                if (!TryReadRegisteredDefinition(fullName, out var registeredXml, out var readError))
                {
                    error = $"A tarefa foi criada mas não pôde ser relida: {readError}";
                    Log?.Log(LogLevel.Error, LogCategory.Scheduler,
                        $"[TaskSchedulerBridge] {error}");
                    return false;
                }

                if (!DefinitionMatches(registeredXml, task, exePath, out var mismatch))
                {
                    error = $"A definição gravada no Windows diverge do esperado: {mismatch}";
                    Log?.Log(LogLevel.Error, LogCategory.Scheduler,
                        $"[TaskSchedulerBridge] {error}");
                    return false;
                }

                // Aplica o estado enabled/disabled explicitamente após a criação.
                SetTaskEnabled(fullName, task.IsEnabled, out _);

                Log?.Log(LogLevel.Success, LogCategory.Scheduler,
                    $"[TaskSchedulerBridge] Tarefa Windows '{windowsName}' registrada E VERIFICADA " +
                    $"(trigger={task.ScheduleType}, habilitada={task.IsEnabled}, exe={exePath}).");
                return true;
            }
            finally
            {
                try { if (File.Exists(xmlPath)) File.Delete(xmlPath); } catch { /* best-effort */ }
            }
        }

        /// <summary>
        /// Remove a tarefa do Windows. Idempotente: "não existe" não é falha.
        /// </summary>
        public static bool UnregisterTask(string taskId, out string error)
        {
            error = string.Empty;
            if (!IsSupported(out error)) return false;

            var fullName = $@"\{TaskFolderName}\{GetWindowsTaskName(taskId)}";
            var ok = RunSchtasks($"/Delete /TN \"{fullName}\" /F", 20_000, out var stdout, out var stderr, out var exitCode);

            if (ok)
            {
                Log?.Log(LogLevel.Success, LogCategory.Scheduler,
                    $"[TaskSchedulerBridge] Tarefa Windows '{GetWindowsTaskName(taskId)}' removida.");
                return true;
            }

            // Código 1 com mensagem indicando inexistência é sucesso idempotente.
            if (stderr.Contains("não existe", StringComparison.OrdinalIgnoreCase) ||
                stderr.Contains("cannot find", StringComparison.OrdinalIgnoreCase) ||
                stderr.Contains("NÃO EXISTE", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            error = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            Log?.Log(LogLevel.Warning, LogCategory.Scheduler,
                $"[TaskSchedulerBridge] Falha ao remover '{fullName}' (exit={exitCode}): {error}");
            return false;
        }

        /// <summary>
        /// Confirma se a tarefa existe no Agendador do Windows. Usado para comprovar
        /// persistência real após fechar/reabrir o VOLTRIS.
        /// </summary>
        public static bool TaskExists(string taskId, out string state)
        {
            state = string.Empty;
            if (!IsSupported(out var support)) { state = support; return false; }

            var fullName = $@"\{TaskFolderName}\{GetWindowsTaskName(taskId)}";
            var ok = RunSchtasks($"/Query /TN \"{fullName}\" /FO LIST /V", 20_000, out var stdout, out _, out _);
            state = ok ? "Registered" : "NotRegistered";
            return ok;
        }

        /// <summary>
        /// Habilita ou desabilita a tarefa do Windows sem recriá-la.
        /// </summary>
        public static bool SetTaskEnabled(string taskId, bool enabled, out string error)
        {
            error = string.Empty;
            if (!IsSupported(out error)) return false;

            var fullName = $@"\{TaskFolderName}\{GetWindowsTaskName(taskId)}";
            var ok = RunSchtasks($"/Change /TN \"{fullName}\" /{(enabled ? "ENABLE" : "DISABLE")}",
                                20_000, out var stdout, out var stderr, out var exitCode);
            if (ok) return true;

            error = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            Log?.Log(LogLevel.Warning, LogCategory.Scheduler,
                $"[TaskSchedulerBridge] /Change {(enabled ? "ENABLE" : "DISABLE")} falhou (exit={exitCode}): {error}");
            return false;
        }

        /// <summary>
        /// Lê a próxima execução programmada pelo Windows. Permite à UI mostrar o
        /// horário real do agendador em vez de um cálculo paralelo e potencialmente divergente.
        /// </summary>
        public static bool TryGetNextRunTime(string taskId, out DateTime nextRun)
        {
            nextRun = default;
            if (!IsSupported(out _)) return false;

            var fullName = $@"\{TaskFolderName}\{GetWindowsTaskName(taskId)}";
            if (!RunSchtasks($"/Query /TN \"{fullName}\" /FO LIST /V", 20_000, out var stdout, out _, out _))
                return false;

            // Formato: "Next Run Time: 27/09/2026 08:00:00"
            foreach (var rawLine in stdout.Split('\n'))
            {
                var line = rawLine.Trim();
                int idx = line.IndexOf("Next Run Time", StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;
                if (line.Length <= idx + 1) continue;
                var sep = line.IndexOf(':', idx);
                if (sep < 0) continue;
                var value = line.Substring(sep + 1).Trim();
                if (value.Length == 0) return false;
                if (DateTime.TryParse(value, System.Globalization.CultureInfo.CurrentCulture,
                        System.Globalization.DateTimeStyles.None, out var parsed))
                {
                    nextRun = parsed;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Dispara a tarefa imediatamente (usado por "Executar agora").
        /// </summary>
        public static bool RunNow(string taskId, out string error)
        {
            error = string.Empty;
            if (!IsSupported(out error)) return false;

            var fullName = $@"\{TaskFolderName}\{GetWindowsTaskName(taskId)}";
            var ok = RunSchtasks($"/Run /TN \"{fullName}\"", 20_000, out var stdout, out var stderr, out var exitCode);
            if (ok) return true;

            error = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            Log?.Log(LogLevel.Warning, LogCategory.Scheduler,
                $"[TaskSchedulerBridge] /Run falhou (exit={exitCode}) para '{fullName}': {error}");
            return false;
        }

        /// <summary>
        /// Nome da tarefa no Windows. Derivado do Id do VOLTRIS de forma determinística,
        /// permitindo reconciliar o registro a partir do JSON a qualquer momento.
        /// </summary>
        public static string GetWindowsTaskName(string taskId)
        {
            var safe = new string((taskId ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());
            if (safe.Length > 32) safe = safe.Substring(0, 32);
            if (safe.Length == 0) safe = "task";
            return "VoltrisTask_" + safe;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Geração do XML
        // ─────────────────────────────────────────────────────────────────────────

        private static string BuildTaskXml(ScheduledTask task, string exePath)
        {
            var sb = new StringBuilder(2048);
            var args = BuildTaskArguments(task);
            var workingDir = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;

            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-16\"?>");
            sb.AppendLine("<Task version=\"1.4\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">");
            sb.AppendLine("  <RegistrationInfo>");
            sb.AppendLine("    <Author>VOLTRIS</Author>");
            sb.AppendLine($"    <Description>{EscapeXml($"VOLTRIS Optimizer - tarefa agendada '{task.Name}' ({task.ScheduleType}). Execução feita pelo próprio executável do VOLTRIS em modo headless.")}</Description>");
            sb.AppendLine("  </RegistrationInfo>");
            sb.AppendLine("  <Triggers>");

            switch (task.ScheduleType)
            {
                case ScheduleType.Daily:
                    {
                        var time = task.ScheduledTime ?? new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 8, 0, 0);
                        // StartBoundary é uma data-âncora; o que importa é o horário.
                        sb.AppendLine("      <CalendarTrigger>");
                        sb.AppendLine($"        <StartBoundary>{FormatBoundary(time)}</StartBoundary>");
                        sb.AppendLine("        <Enabled>true</Enabled>");
                        sb.AppendLine("        <ScheduleByDay>");
                        sb.AppendLine("          <DaysInterval>1</DaysInterval>");
                        sb.AppendLine("        </ScheduleByDay>");
                        sb.AppendLine("      </CalendarTrigger>");
                        break;
                    }
                case ScheduleType.Weekly:
                    {
                        var days = task.DaysOfWeek;
                        if (days == WeekdaySelection.None)
                            throw new InvalidOperationException("Nenhum dia da semana foi selecionado para a tarefa semanal.");
                        var time = task.ScheduledTime ?? new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 8, 0, 0);
                        sb.AppendLine("      <CalendarTrigger>");
                        sb.AppendLine($"        <StartBoundary>{FormatBoundary(time)}</StartBoundary>");
                        sb.AppendLine("        <Enabled>true</Enabled>");
                        sb.AppendLine("        <ScheduleByWeek>");
                        sb.AppendLine("          <WeeksInterval>1</WeeksInterval>");
                        sb.AppendLine("          <DaysOfWeek>");
                        foreach (var d in Enum.GetValues<WeekdaySelection>())
                        {
                            if (d == WeekdaySelection.None) continue;
                            if (days.HasFlag(d)) sb.AppendLine($"            <Day>{DayName(d)}</Day>");
                        }
                        sb.AppendLine("          </DaysOfWeek>");
                        sb.AppendLine("        </ScheduleByWeek>");
                        sb.AppendLine("      </CalendarTrigger>");
                        break;
                    }
                case ScheduleType.Monthly:
                    {
                        var dayList = task.DaysOfMonth;
                        if (dayList == null || dayList.Count == 0)
                            throw new InvalidOperationException("Nenhum dia do mês foi selecionado para a tarefa mensal.");
                        var time = task.ScheduledTime ?? new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 8, 0, 0);
                        sb.AppendLine("      <CalendarTrigger>");
                        sb.AppendLine($"        <StartBoundary>{FormatBoundary(time)}</StartBoundary>");
                        sb.AppendLine("        <Enabled>true</Enabled>");
                        sb.AppendLine("        <ScheduleByMonth>");
                        sb.AppendLine("          <DaysOfMonth>");
                        foreach (var d in dayList.Where(x => x >= 1 && x <= 31).Distinct().OrderBy(x => x))
                            sb.AppendLine($"            <Day>{d}</Day>");
                        sb.AppendLine("          </DaysOfMonth>");
                        sb.AppendLine("          <Months>");
                        for (int m = 1; m <= 12; m++) sb.AppendLine($"            <Month>{m}</Month>");
                        sb.AppendLine("          </Months>");
                        sb.AppendLine("        </ScheduleByMonth>");
                        sb.AppendLine("      </CalendarTrigger>");
                        break;
                    }
                case ScheduleType.Once:
                    {
                        if (!task.ScheduledTime.HasValue)
                            throw new InvalidOperationException("Execução única exige data e hora.");

                        var when = task.ScheduledTime.Value;
                        // <RunOnlyIf> não existe; execuções únicas usam um gatilho diário
                        // ancorado na data desejada mais <EndBoundary> no dia seguinte,
                        // o que garante UMA execução real.
                        sb.AppendLine("      <CalendarTrigger>");
                        sb.AppendLine($"        <StartBoundary>{FormatBoundary(when)}</StartBoundary>");
                        sb.AppendLine($"        <EndBoundary>{FormatBoundary(when.Date.AddDays(1))}</EndBoundary>");
                        sb.AppendLine("        <Enabled>true</Enabled>");
                        sb.AppendLine("        <ScheduleByDay>");
                        sb.AppendLine("          <DaysInterval>1</DaysInterval>");
                        sb.AppendLine("        </ScheduleByDay>");
                        sb.AppendLine("      </CalendarTrigger>");
                        break;
                    }
                case ScheduleType.OnStartup:
                    sb.AppendLine("      <BootTrigger>");
                    sb.AppendLine("        <Enabled>true</Enabled>");
                    sb.AppendLine("        <Delay>PT1M</Delay>");
                    sb.AppendLine("      </BootTrigger>");
                    break;
                case ScheduleType.OnLogon:
                    sb.AppendLine("      <LogonTrigger>");
                    sb.AppendLine("        <Enabled>true</Enabled>");
                    sb.AppendLine("        <Delay>PT1M</Delay>");
                    sb.AppendLine("      </LogonTrigger>");
                    break;
                default:
                    throw new InvalidOperationException($"Tipo de agendamento não suportado: {task.ScheduleType}");
            }

            sb.AppendLine("  </Triggers>");
            sb.AppendLine("  <Principals>");
            sb.AppendLine("    <Principal id=\"Author\">");
            sb.AppendLine($"      <UserId>{SystemAccountSid}</UserId>");
            sb.AppendLine("      <LogonType>ServiceAccount</LogonType>");
            sb.AppendLine("      <RunLevel>HighestAvailable</RunLevel>");
            sb.AppendLine("    </Principal>");
            sb.AppendLine("  </Principals>");
            sb.AppendLine("  <Settings>");
            sb.AppendLine($"    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>");
            sb.AppendLine("    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>");
            sb.AppendLine("    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>");
            sb.AppendLine("    <AllowHardTerminate>true</AllowHardTerminate>");
            sb.AppendLine("    <StartWhenAvailable>true</StartWhenAvailable>");
            // "Computador desligado no horário" -> o Windows roda assim que possível.
            sb.AppendLine("    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>");
            // Até 3 tentativas, 10 min de intervalo: cobre falha transitória de serviço.
            sb.AppendLine("    <RestartOnFailure>");
            sb.AppendLine("      <Interval>PT10M</Interval>");
            sb.AppendLine("      <Count>3</Count>");
            sb.AppendLine("    </RestartOnFailure>");
            sb.AppendLine("    <ExecutionTimeLimit>PT2H</ExecutionTimeLimit>");
            sb.AppendLine("    <Priority>7</Priority>");
            sb.AppendLine("    <Enabled>true</Enabled>");
            sb.AppendLine("  </Settings>");
            sb.AppendLine("  <Actions Context=\"Author\">");
            sb.AppendLine("    <Exec>");
            sb.AppendLine($"      <Command>{EscapeXml(exePath)}</Command>");
            sb.AppendLine($"      <Arguments>{EscapeXml(args)}</Arguments>");
            sb.AppendLine($"      <WorkingDirectory>{EscapeXml(workingDir)}</WorkingDirectory>");
            sb.AppendLine("    </Exec>");
            sb.AppendLine("  </Actions>");
            sb.AppendLine("</Task>");
            return sb.ToString();
        }

        /// <summary>
        /// Monta a linha de comando da tarefa do Windows.
        ///
        /// São TRÊS informações, e todas são necessárias:
        ///   --run-scheduled &lt;id&gt;   identifica a tarefa (contrato com Program.Main);
        ///   --data-root &lt;raiz&gt;        overcome o problema de CONTEXTO DE CONTA: a
        ///                                tarefa roda como LOCAL SYSTEM, e sob SYSTEM
        ///                                SpecialFolder.LocalApplicationData aponta para
        ///                                systemprofile — onde não existe schedules.json.
        ///                                Sem esta raiz, toda tarefa agendada falharia
        ///                                com "tarefa não encontrada", silenciosamente;
        ///   --task-payload &lt;b64&gt;     definition completa em Base64(UTF8 JSON), para
        ///                                que a execução não dependa de LEITURA de nenhum
        ///                                arquivo. O Agendador do Windows passa a ser
        ///                                também o armazenamento durável da tarefa.
        /// </summary>
        private static string BuildTaskArguments(ScheduledTask task)
        {
            var sb = new StringBuilder(256);
            sb.Append($"--run-scheduled \"{task.Id}\"");
            sb.Append($" --data-root \"{AppDataPaths.UnifiedRoot}\"");
            sb.Append($" --task-payload \"{EncodeTaskPayload(task)}\"");
            return sb.ToString();
        }

        private static string EncodeTaskPayload(ScheduledTask task)
        {
            // Somente o que o runner EXECUTA precisa viajar: Id, nome, tipo, ações,
            // estado e horário. Levar LastResult/WindowsRegistrationError seria ruído
            // que o headless jamais deve sobrescrever (é o resultado que a UI grava).
            var snapshot = new
            {
                task.Id,
                task.Name,
                task.ScheduleType,
                task.ScheduledTime,
                task.DaysOfWeek,
                task.DaysOfMonth,
                task.Actions,
                task.IsEnabled
            };
            var json = JsonSerializer.Serialize(snapshot);
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        }

        private static string FormatBoundary(DateTime dt) =>
            dt.ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

        private static string DayName(WeekdaySelection day) => day switch
        {
            WeekdaySelection.Sunday => "Sunday",
            WeekdaySelection.Monday => "Monday",
            WeekdaySelection.Tuesday => "Tuesday",
            WeekdaySelection.Wednesday => "Wednesday",
            WeekdaySelection.Thursday => "Thursday",
            WeekdaySelection.Friday => "Friday",
            WeekdaySelection.Saturday => "Saturday",
            _ => "Sunday"
        };

        private static string EscapeXml(string s) =>
            s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
             .Replace("\"", "&quot;").Replace("'", "&apos;");

        // ─────────────────────────────────────────────────────────────────────────
        // Verificação
        // ─────────────────────────────────────────────────────────────────────────

        private static bool TryReadRegisteredDefinition(string fullName, out string xml, out string error)
        {
            xml = string.Empty;
            error = string.Empty;
            var ok = RunSchtasks($"/Query /TN \"{fullName}\" /XML", 20_000, out var stdout, out var stderr, out _);
            if (!ok)
            {
                error = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                return false;
            }
            xml = stdout;
            return true;
        }

        private static bool DefinitionMatches(string registeredXml, ScheduledTask task, string exePath, out string mismatch)
        {
            mismatch = string.Empty;

            if (!registeredXml.Contains("--run-scheduled", StringComparison.OrdinalIgnoreCase))
            {
                mismatch = "o argumento '--run-scheduled' não está presente na definição registrada.";
                return false;
            }
            if (!registeredXml.Contains("--data-root", StringComparison.OrdinalIgnoreCase))
            {
                // Sem a raiz do usuário a tarefa roda como SYSTEM e não acha o
                // schedules.json — falha silenciosa com exit code 2. Precisa ser
                // tratado como falha de REGISTRO, não de execução.
                mismatch = "o argumento '--data-root' não está presente: a tarefa não conseguiria " +
                           "enxergar os dados do usuário ao rodar como SYSTEM.";
                return false;
            }
            if (!registeredXml.Contains("--task-payload", StringComparison.OrdinalIgnoreCase))
            {
                mismatch = "o argumento '--task-payload' não está presente: a execução dependeria de " +
                           "ler um arquivo do perfil do usuário, inacessível sob a conta SYSTEM.";
                return false;
            }
            if (!registeredXml.Contains(task.Id, StringComparison.OrdinalIgnoreCase))
            {
                mismatch = $"o Id da tarefa '{task.Id}' não está presente na definição registrada.";
                return false;
            }
            if (!registeredXml.Contains(exePath, StringComparison.OrdinalIgnoreCase))
            {
                mismatch = $"o executável registrado não corresponde a '{exePath}'.";
                return false;
            }

            var expectedTrigger = task.ScheduleType switch
            {
                ScheduleType.OnStartup => "BootTrigger",
                ScheduleType.OnLogon => "LogonTrigger",
                _ => "CalendarTrigger"
            };
            if (!registeredXml.Contains(expectedTrigger, StringComparison.OrdinalIgnoreCase))
            {
                mismatch = $"gatilho esperado '{expectedTrigger}' não encontrado na definição registrada.";
                return false;
            }

            return true;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Processo
        // ─────────────────────────────────────────────────────────────────────────

        private static bool RunSchtasks(string arguments, int timeoutMs,
            out string stdout, out string stderr, out int exitCode)
        {
            stdout = string.Empty;
            stderr = string.Empty;
            exitCode = -1;

            try
            {
                var psi = new ProcessStartInfo("schtasks.exe", arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.Default,
                    StandardErrorEncoding = Encoding.Default
                };

                using var proc = new Process { StartInfo = psi };
                proc.Start();

                // Leitura assíncrona dos dois pipes em paralelo evita o deadlock
                // clássico quando um dos buffers enche antes de WaitForExit.
                var tOut = proc.StandardOutput.ReadToEndAsync();
                var tErr = proc.StandardError.ReadToEndAsync();

                if (!proc.WaitForExit(timeoutMs))
                {
                    try { proc.Kill(entireProcessTree: true); } catch { /* best-effort */ }
                    stderr = "Tempo esgotado aguardando schtasks.exe.";
                    return false;
                }

                stdout = SafeGet(tOut);
                stderr = SafeGet(tErr);
                exitCode = proc.ExitCode;
                return exitCode == 0;
            }
            catch (Exception ex)
            {
                stderr = $"{ex.GetType().Name}: {ex.Message}";
                Log?.Log(LogLevel.Error, LogCategory.Scheduler,
                    $"[TaskSchedulerBridge] Falha ao executar schtasks.exe {arguments}: {stderr}", ex);
                return false;
            }
        }

        private static string SafeGet(Task<string> t)
        {
            try { return t.GetAwaiter().GetResult() ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static string ResolveExecutablePath()
        {
            lock (ExistenceCacheLock)
            {
                if (!string.IsNullOrEmpty(_exePathCache)) return _exePathCache;

                string? found = null;
                try
                {
                    var exe = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(exe) &&
                        exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                        File.Exists(exe))
                    {
                        var name = Path.GetFileNameWithoutExtension(exe);
                        // Em desenvolvimento o processo pode ser dotnet/testhost; nesses
                        // casos o VOLTRIS real não é um .exe registering-se no Task Scheduler.
                        bool isHost = name.Equals("dotnet", StringComparison.OrdinalIgnoreCase) ||
                                      name.Equals("testhost", StringComparison.OrdinalIgnoreCase) ||
                                      name.Equals("VBCSCompiler", StringComparison.OrdinalIgnoreCase) ||
                                      name.Equals("MSBuild", StringComparison.OrdinalIgnoreCase);
                        if (!isHost) found = exe;
                    }
                }
                catch { /* segue para o fallback */ }

                if (string.IsNullOrEmpty(found))
                {
                    try
                    {
                        var candidate = Path.Combine(AppContext.BaseDirectory, "VoltrisOptimizer.exe");
                        if (File.Exists(candidate)) found = candidate;
                    }
                    catch { /* ignora */ }
                }

                _exePathCache = found ?? string.Empty;
                return _exePathCache;
            }
        }
    }

    /// <summary>
    /// Dia(s) da semana selecionados para tarefas semanais. Os valores seguem as
    /// flags ACEITAS pelo Agendador do Windows, para que o mapeamento seja direto.
    /// </summary>
    [Flags]
    public enum WeekdaySelection
    {
        None = 0,
        Sunday = 1,
        Monday = 2,
        Tuesday = 4,
        Wednesday = 8,
        Thursday = 16,
        Friday = 32,
        Saturday = 64
    }
}
