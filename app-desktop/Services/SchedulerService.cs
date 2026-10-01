using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Timer = System.Timers.Timer;
using VoltrisOptimizer.Core;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services.Scheduler;

namespace VoltrisOptimizer.Services
{
    /// <summary>
    /// Serviço de agendamento automático de otimizações.
    ///
    /// ARQUITETURA (duas camadas complementares, uma única implementação de execução):
    ///
    /// 1) AGENDADOR DO WINDOWS (<see cref="WindowsTaskSchedulerBridge"/>)
    ///    É a fonte de verdade. Cada tarefa é registrada como tarefa real do Windows
    ///    em "\Voltris\VoltrisTask_*", executando o próprio executável com
    ///    "--run-scheduled &lt;taskId&gt;". Consequências:
    ///      - a tarefa executa com o VOLTRIS FECHADO;
    ///      - sobrevive a reinício do Windows;
    ///      - "computador desligado no horário" é coberto por StartWhenAvailable;
    ///      - falhas transitórias são cobertas por RestartOnFailure;
    ///      - MultipleInstancesPolicy=IgnoreNew impede execuções duplicadas.
    ///
    /// 2) TIMER IN-PROCESSO (60 s)
    ///    Mantido como uma malha de segurança: detecta tarefas cujo registro no
    ///    Windows não pôde ser criado (ex.: instalação em diretório sem permissão)
    ///    e executa dentro do app. Também é quem reprocessa a fila após o app voltar.
    ///    Ambos os caminhos chamam EXECUTAM O MESMO <see cref="ExecuteTaskActions"/>,
    ///    portanto não existe implementação paralela de limpeza/otimização.
    ///
    /// O estado de execução de cada tarefa é controlado por um
    /// <c>ConcurrentDictionary</c> para impedir que o timer de 60 s e uma execução
    /// longa (>60 s) lancem a mesma tarefa duas vezes em paralelo.
    /// </summary>
    public partial class SchedulerService : IDisposable
    {
        private const int MaxTasks = 200;

        private readonly ILoggingService _logger;
        private readonly HistoryService _historyService;
        private readonly string _schedulesPath;
        private readonly string _schedulesBackupPath;
        private readonly object _tasksLock = new();
        private readonly ConcurrentDictionary<string, bool> _runningTasks = new(StringComparer.OrdinalIgnoreCase);
        private Timer? _checkTimer;
        private List<ScheduledTask> _tasks = new List<ScheduledTask>();
        private volatile bool _isPausedByGamerMode;
        private bool _disposed;
        private bool _reconciliationDone;
        private DateTime _lastDiskStamp = DateTime.MinValue;

        [StructLayout(LayoutKind.Sequential)]
        struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll")]
        static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public uint BatteryLifeTime;
            public uint BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS systemPowerStatus);

        public event EventHandler<ScheduledTask>? TaskExecuted;

        /// <summary>Raised quando uma execução termina, com o desfecho real (usado pela UI).</summary>
        public event EventHandler<ScheduledTaskRunResult>? TaskRunCompleted;

        public SchedulerService(ILoggingService logger, HistoryService historyService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _historyService = historyService ?? throw new ArgumentNullException(nameof(historyService));
            _schedulesPath = AppDataPaths.GetPath("Scheduler/schedules.json");
            _schedulesBackupPath = _schedulesPath + ".bak";
            AppDataPaths.EnsureDirectory(Path.GetDirectoryName(_schedulesPath));
            LoadSchedules();
            StartScheduler();

            // Reconciliação com o Windows fora do construtor: registrar tarefas no
            // Agendador do Windows é operação lenta e não pode travar o DI/startup.
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(2500).ConfigureAwait(false);
                    ReconcileWithWindowsScheduler();
                }
                catch (Exception ex)
                {
                    _logger.Log(LogLevel.Warning, LogCategory.Scheduler,
                        "[SchedulerService] Falha na reconciliação inicial com o Windows: " + ex.Message);
                }
            });
        }

        // ─────────────────────────────────────────────────────────────────────────
        // API pública
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Adiciona uma tarefa agendada. Retorna a tarefa efetivamente persistida
        /// ou <c>null</c> com o motivo em <paramref name="error"/>.
        /// </summary>
        public ScheduledTask? AddTask(ScheduledTask task, out string error)
        {
            error = string.Empty;
            if (task == null) { error = "Tarefa nula."; return null; }

            lock (_tasksLock)
            {
                // Prevenção de tarefas duplicadas: mesma recorrência + mesmo horário +
                // mesmas ações + mesmo nome => já existe.
                var duplicate = _tasks.FirstOrDefault(t =>
                    t.Id != task.Id &&
                    string.Equals(t.Name?.Trim(), task.Name?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    t.ScheduleType == task.ScheduleType &&
                    t.ScheduledTime == task.ScheduledTime &&
                    t.DaysOfWeek == task.DaysOfWeek &&
                    t.Actions.Count == task.Actions.Count &&
                    t.Actions.All(a => task.Actions.Any(b => string.Equals(a, b, StringComparison.OrdinalIgnoreCase))));

                if (duplicate != null)
                {
                    error = $"Já existe uma tarefa equivalente: '{duplicate.Name}'.";
                    _logger.Log(LogLevel.Warning, LogCategory.Scheduler,
                        $"[SchedulerService] Tarefa duplicada rejeitada: '{task.Name}' == '{duplicate.Name}'.");
                    return null;
                }

                if (_tasks.Count >= MaxTasks)
                {
                    error = $"Limite de {MaxTasks} tarefas agendadas atingido.";
                    return null;
                }

                if (string.IsNullOrWhiteSpace(task.Id)) task.Id = Guid.NewGuid().ToString("N");
                task.NextExecution = CalculateNextExecution(task, DateTime.Now, force: true);
                _tasks.Add(task);
            }

            if (!SaveSchedules(out var saveError))
            {
                error = saveError;
                return null;
            }

            // Materializa no Windows. Falha aqui NÃO invalida a tarefa: ela passa a
            // ser executada apenas pela malha de segurança in-process, e o motivo é
            // exposto para a UI informar honestamente.
            if (!RegisterInWindows(task, out var registerError))
            {
                _logger.Log(LogLevel.Warning, LogCategory.Scheduler,
                    $"[SchedulerService] Tarefa '{task.Name}' salva, mas NÃO registrada no Agendador do Windows: {registerError}. " +
                    "Ela será executada apenas enquanto o VOLTRIS estiver em execução.");
                task.WindowsRegistrationError = registerError;
            }
            else
            {
                task.WindowsRegistrationError = null;
            }

            _logger.Log(LogLevel.Success, LogCategory.Scheduler,
                $"[SchedulerService] Tarefa criada: '{task.Name}' tipo={task.ScheduleType} " +
                $"hora={task.ScheduledTime:HH:mm} dias={task.DaysOfWeek} acoes=[{string.Join(", ", task.Actions)}] " +
                $"proxima={task.NextExecution:dd/MM/yyyy HH:mm}");
            return task;
        }

        public void RemoveTask(string taskId)
        {
            if (string.IsNullOrEmpty(taskId)) return;

            string? taskName = null;
            lock (_tasksLock)
            {
                var task = _tasks.FirstOrDefault(t => t.Id == taskId);
                if (task != null)
                {
                    taskName = task.Name;
                    _tasks.Remove(task);
                }
            }

            if (!UnregisterFromWindows(taskId, out var unregError))
            {
                _logger.Log(LogLevel.Warning, LogCategory.Scheduler,
                    $"[SchedulerService] Falha ao remover '{taskName ?? taskId}' do Agendador do Windows: {unregError}. " +
                    "Restaurando o registro para não deixar tarefa órfã executando no Windows.");
                var orphan = _tasks.FirstOrDefault(t => t.Id == taskId);
                if (orphan != null) RegisterInWindows(orphan, out _);
            }

            SaveSchedules(out _);
            if (taskName != null)
                _logger.Log(LogLevel.Info, LogCategory.Scheduler, $"[SchedulerService] Tarefa removida: '{taskName}'");
        }

        /// <summary>
        /// Atualiza uma tarefa existente (usado pelo botão Editar e pelo toggle
        /// Ativar/Pausar, que antes usava Remove+Add e reordenava a lista).
        /// </summary>
        public bool UpdateTask(ScheduledTask updated, out string error)
        {
            error = string.Empty;
            if (updated == null) { error = "Tarefa nula."; return false; }

            lock (_tasksLock)
            {
                var idx = _tasks.FindIndex(t => t.Id == updated.Id);
                if (idx < 0)
                {
                    error = "Tarefa não encontrada para atualização.";
                    return false;
                }
                updated.NextExecution = CalculateNextExecution(updated, DateTime.Now, force: true);
                _tasks[idx] = updated;
            }

            if (!SaveSchedules(out var saveError)) { error = saveError; return false; }

            if (!RegisterInWindows(updated, out var registerError))
            {
                updated.WindowsRegistrationError = registerError;
                _logger.Log(LogLevel.Warning, LogCategory.Scheduler,
                    $"[SchedulerService] Atualização de '{updated.Name}' não refletida no Windows: {registerError}");
            }
            else
            {
                updated.WindowsRegistrationError = null;
            }

            _logger.Log(LogLevel.Info, LogCategory.Scheduler,
                $"[SchedulerService] Tarefa atualizada: '{updated.Name}' habilitada={updated.IsEnabled} " +
                $"proxima={updated.NextExecution:dd/MM/yyyy HH:mm}");
            return true;
        }

        public List<ScheduledTask> GetTasks()
        {
            ReloadIfChangedOnDisk();
            lock (_tasksLock) { return _tasks.ToList(); }
        }

        /// <summary>
        /// Relê o arquivo de tarefas se ele mudou em disco. Necessário porque as
        /// tarefas também são executadas por um PROCESSO SEPARADO (o runner headless
        /// disparado pelo Agendador do Windows), que grava LastExecuted/LastResult.
        /// Sem isso, a interface continuaria mostrando dados obsoletos depois de uma
        /// execução com o VOLTRIS fechado.
        /// </summary>
        private void ReloadIfChangedOnDisk()
        {
            try
            {
                var info = new FileInfo(_schedulesPath);
                var stamp = info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue;
                if (stamp == _lastDiskStamp) return;

                lock (_tasksLock)
                {
                    var loaded = TryLoadFrom(_schedulesPath);
                    if (loaded == null) return;
                    _tasks = loaded;
                    NormalizeLoadedTasks();
                    _lastDiskStamp = stamp;
                    _logger.Log(LogLevel.Info, LogCategory.Scheduler,
                        $"[SchedulerService] Recarregadas {_tasks.Count} tarefa(s): o arquivo foi alterado por outro processo (execução agendada).");
                }
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Warning, LogCategory.Scheduler,
                    "[SchedulerService] Falha ao revalidar o arquivo de tarefas: " + ex.Message);
            }
        }

        public ScheduledTask? GetTask(string taskId)
        {
            ReloadIfChangedOnDisk();
            lock (_tasksLock) { return _tasks.FirstOrDefault(t => t.Id == taskId); }
        }

        /// <summary>
        /// Executa imediatamente, independente do horário. Usado pelo botão
        /// "Executar agora" e pelo próprio Agendador do Windows.
        /// </summary>
        public Task<ScheduledTaskRunResult> RunNowAsync(ScheduledTask task, HistoryOrigin origin, CancellationToken ct = default)
        {
            return ExecuteGuardedAsync(task, origin, ct);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Malha de segurança in-process
        // ─────────────────────────────────────────────────────────────────────────

        private void StartScheduler()
        {
            _checkTimer = new Timer(60_000); // Verificar a cada minuto
            _checkTimer.Elapsed += CheckScheduledTasks;
            _checkTimer.AutoReset = true;
            _checkTimer.Start();
            _logger.Log(LogLevel.Info, LogCategory.Scheduler,
                "[SchedulerService] Malha de segurança in-process iniciada (60s). " +
                "A execução real é feita pelo Agendador do Windows quando registrado.");
        }

        private void CheckScheduledTasks(object? sender, ElapsedEventArgs e)
        {
            if (_disposed) return;

            try
            {
                var now = DateTime.Now;
                List<ScheduledTask> candidates;
                lock (_tasksLock)
                {
                    candidates = _tasks.Where(t => t.IsEnabled && ShouldExecuteInProcess(t, now)).ToList();
                }

                foreach (var task in candidates)
                {
                    // Dispara sem esperar: o timer não pode bloquear.
                    _ = Task.Run(() => ExecuteGuardedAsync(task, HistoryOrigin.Scheduled, CancellationToken.None));
                }
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, LogCategory.Scheduler,
                    "[SchedulerService] Erro no tick do agendador: " + ex.Message, ex);
            }
        }

        /// <summary>
        /// Decide se a malha in-process deve executar a tarefa. Só atua como rede de
        /// segurança quando o registro no Windows AUSENTE (ou a tarefa foi criada
        /// enquanto o Windows não tinha o registro). Tarefas já materializadas no
        /// Windows ficam a cargo do Windows, evitando execução dupla.
        /// </summary>
        private bool ShouldExecuteInProcess(ScheduledTask task, DateTime now)
        {
            if (task.NextExecution == null)
            {
                task.NextExecution = CalculateNextExecution(task, now, force: true);
                return false;
            }
            if (now < task.NextExecution.Value) return false;

            // Só a malha de segurança assume: ou o registro no Windows falhou, ou
            // a tarefa está no tipo que o Windows ainda não materializou.
            if (WindowsTaskSchedulerBridge.IsSupported(out _) && WindowsTaskSchedulerBridge.TaskExists(task.Id, out _))
            {
                // Não executa aqui, mas evita reprocessar a cada tick mantendo o
                // cálculo de NextExecution coerente para exibição.
                task.NextExecution = CalculateNextExecution(task, now, force: false);
                return false;
            }

            return true;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Execução
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Executa uma tarefa com controle de concorrência. Somente uma execução por
        /// tarefa pode estar em andamento: o timer de 60 s e o Agendador do Windows
        /// podem disparar o mesmo Id.
        /// </summary>
        private async Task<ScheduledTaskRunResult> ExecuteGuardedAsync(ScheduledTask task, HistoryOrigin origin, CancellationToken ct)
        {
            if (!_runningTasks.TryAdd(task.Id, true))
            {
                _logger.Log(LogLevel.Debug, LogCategory.Scheduler,
                    $"[SchedulerService] Execução de '{task.Name}' ignorada: já está em andamento.");
                return new ScheduledTaskRunResult
                {
                    TaskId = task.Id,
                    TaskName = task.Name,
                    Success = false,
                    Error = "Já existe uma execução em andamento para esta tarefa.",
                    Skipped = true
                };
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                _logger.Log(LogLevel.Info, LogCategory.Scheduler,
                    $"[SchedulerService] INICIANDO tarefa '{task.Name}' (Id={task.Id}) origem={origin} " +
                    $"acoes=[{string.Join(", ", task.Actions)}] admin={IsRunningAsAdmin()}");

                if (origin == HistoryOrigin.Scheduled)
                {
                    if (!await IsSafeToExecuteAsync(task, ct).ConfigureAwait(false))
                    {
                        sw.Stop();
                        _logger.Log(LogLevel.Warning, LogCategory.Scheduler,
                            $"[SchedulerService] Tarefa '{task.Name}' ADIADA pelo verificador de segurança " +
                            $"(perfil/CPU/idle/bateria). Nenhuma alteração foi feita.");

                        var skipped = new ScheduledTaskRunResult
                        {
                            TaskId = task.Id,
                            TaskName = task.Name,
                            Success = false,
                            Skipped = true,
                            Error = "Execução adiada pelas verificações de segurança (CPU, atividade do usuário, bateria ou perfil).",
                            Duration = sw.Elapsed
                        };
                        NotifyRun(task, skipped);
                        return skipped;
                    }
                }

                var result = await ExecuteTaskActionsAsync(task, origin, ct).ConfigureAwait(false);
                result.Duration = sw.Elapsed;

                // Atualiza LastExecuted/NextExecution e persiste.
                lock (_tasksLock)
                {
                    var stored = _tasks.FirstOrDefault(t => t.Id == task.Id);
                    if (stored != null)
                    {
                        stored.LastExecuted = DateTime.Now;
                        stored.LastResult = result;
                        // Tarefa de execução única: desativa após rodar.
                        if (stored.ScheduleType == ScheduleType.Once)
                        {
                            stored.IsEnabled = false;
                            UnregisterFromWindows(stored.Id, out _);
                        }
                        else
                        {
                            stored.NextExecution = CalculateNextExecution(stored, DateTime.Now, force: true);
                        }
                    }
                }
                SaveSchedules(out _);

                if (result.Success)
                {
                    try
                    {
                        new ToastService().Show(
                            LocalizationService.Instance.GetString("TaskCompleted"),
                            string.Format(LocalizationService.Instance.GetString("TaskExecutedSuccessfully"), task.Name));
                    }
                    catch (Exception toastEx)
                    {
                        _logger.Log(LogLevel.Debug, LogCategory.Scheduler,
                            "[SchedulerService] Falha ao exibir toast: " + toastEx.Message);
                    }
                }

                _logger.Log(result.Success ? LogLevel.Success : LogLevel.Error, LogCategory.Scheduler,
                    $"[SchedulerService] CONCLUÍDA tarefa '{task.Name}' em {sw.Elapsed.TotalSeconds:F1}s " +
                    $"sucesso={result.Success} parcial={result.PartialFailure} acoesOk={result.SucceededActions.Count} " +
                    $"acoesFalha={result.FailedActions.Count} erro='{result.Error}'");

                try { TaskExecuted?.Invoke(this, task); } catch { }
                NotifyRun(task, result);
                return result;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.Log(LogLevel.Error, LogCategory.Scheduler,
                    $"[SchedulerService] EXCEÇÃO ao executar tarefa '{task.Name}': {ex.Message}", ex);

                var failed = new ScheduledTaskRunResult
                {
                    TaskId = task.Id,
                    TaskName = task.Name,
                    Success = false,
                    Error = $"{ex.GetType().Name}: {ex.Message}",
                    Duration = sw.Elapsed
                };
                RecordTaskRun(task, failed, origin, sw.Elapsed);
                NotifyRun(task, failed);
                return failed;
            }
            finally
            {
                _runningTasks.TryRemove(task.Id, out _);
            }
        }

        private void NotifyRun(ScheduledTask task, ScheduledTaskRunResult result)
        {
            try { TaskRunCompleted?.Invoke(this, result); } catch { }
        }

        private async Task<bool> IsSafeToExecuteAsync(ScheduledTask task, CancellationToken ct)
        {
            var intelligentProfile = SettingsService.Instance.Settings.IntelligentProfile;
            _logger.Log(LogLevel.AI_DECISION, LogCategory.Scheduler,
                $"🧠 Verificando segurança para tarefa '{task.Name}' com Perfil Inteligente: {intelligentProfile}");

            if (intelligentProfile == IntelligentProfileType.EnterpriseSecure)
            {
                _logger.Log(LogLevel.AI_DECISION, LogCategory.Scheduler,
                    $"⛔ Abortando tarefa '{task.Name}': Perfil EnterpriseSecure não permite automação.");
                return false;
            }

            if (_isPausedByGamerMode)
            {
                _logger.Log(LogLevel.AI_DECISION, LogCategory.Scheduler,
                    $"Abortando execução da tarefa '{task.Name}': Modo Gamer ativo (tarefas pausadas).");
                return false;
            }

            double cpuUsage;
            try { cpuUsage = await SystemInfoService.GetCPUUsagePercentAsync().ConfigureAwait(false); }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Warning, LogCategory.Scheduler,
                    $"[SchedulerService] Não foi possível ler a CPU de '{task.Name}' ({ex.Message}). Executando mesmo assim " +
                    "para não perder a janela de execução agendada.");
                cpuUsage = -1;
            }

            double cpuThreshold = intelligentProfile switch
            {
                IntelligentProfileType.WorkOffice => 20.0,
                IntelligentProfileType.GamerCompetitive => 30.0,
                IntelligentProfileType.GamerSinglePlayer => 30.0,
                IntelligentProfileType.CreativeVideoEditing => 25.0,
                IntelligentProfileType.DeveloperProgramming => 25.0,
                IntelligentProfileType.GeneralBalanced => 35.0,
                _ => 40.0
            };

            if (cpuUsage >= 0 && cpuUsage > cpuThreshold)
            {
                _logger.Log(LogLevel.AI_DECISION, LogCategory.Scheduler,
                    $"Postergando tarefa '{task.Name}': CPU {cpuUsage:F0}% > {cpuThreshold}% (limite para perfil {intelligentProfile}).");
                return false;
            }

            var idleTime = GetIdleTime();
            double idleMinutesRequired = intelligentProfile switch
            {
                IntelligentProfileType.WorkOffice => 15.0,
                IntelligentProfileType.GamerCompetitive => 10.0,
                IntelligentProfileType.GamerSinglePlayer => 10.0,
                IntelligentProfileType.CreativeVideoEditing => 12.0,
                IntelligentProfileType.DeveloperProgramming => 12.0,
                IntelligentProfileType.GeneralBalanced => 8.0,
                _ => 5.0
            };

            if (idleTime.TotalMinutes < idleMinutesRequired)
            {
                _logger.Log(LogLevel.AI_DECISION, LogCategory.Scheduler,
                    $"Postergando tarefa '{task.Name}': Idle {idleTime.TotalMinutes:F1}min < {idleMinutesRequired}min (requisito para perfil {intelligentProfile}).");
                return false;
            }

            if (IsOnBattery())
            {
                _logger.Log(LogLevel.AI_DECISION, LogCategory.Scheduler,
                    $"Postergando tarefa '{task.Name}': Notebook operando em bateria.");
                return false;
            }

            _logger.Log(LogLevel.Success, LogCategory.Scheduler,
                $"✅ Tarefa '{task.Name}' aprovada para execução (Perfil: {intelligentProfile}, CPU: {cpuUsage:F1}%, Idle: {idleTime.TotalMinutes:F1}min)");
            return true;
        }

        private TimeSpan GetIdleTime()
        {
            try
            {
                var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
                if (GetLastInputInfo(ref lii))
                {
                    // TickCount e dwTime são ambos uint; a subtração com wrap é
                    // intencional e correta para este par de APIs.
                    uint delta = unchecked((uint)Environment.TickCount) - lii.dwTime;
                    return TimeSpan.FromMilliseconds(delta);
                }
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Debug, LogCategory.Scheduler,
                    "[SchedulerService] GetLastInputInfo falhou: " + ex.Message);
            }
            // Falha de leitura não deve adiar indefinidamente a tarefa.
            return TimeSpan.MaxValue;
        }

        private bool IsOnBattery()
        {
            try
            {
                if (GetSystemPowerStatus(out var status))
                    return status.ACLineStatus == 0;
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Debug, LogCategory.Scheduler,
                    "[SchedulerService] GetSystemPowerStatus falhou: " + ex.Message);
            }
            return false;
        }

        /// <summary>
        /// Executa as ações de uma tarefa reaproveitando os SERVIÇOS EXISTENTES.
        /// Cada ação reporta sucesso real: serviço ausente, retorno falso ou exceção
        /// viram falha explícita, nunca "sucesso" silencioso.
        /// </summary>
        private async Task<ScheduledTaskRunResult> ExecuteTaskActionsAsync(ScheduledTask task, HistoryOrigin origin, CancellationToken ct)
        {
            var result = new ScheduledTaskRunResult { TaskId = task.Id, TaskName = task.Name };
            long totalSpaceFreed = 0;
            var details = new Dictionary<string, object>();

            if (task.Actions == null || task.Actions.Count == 0)
            {
                result.Success = false;
                result.Error = "A tarefa não possui nenhuma ação configurada.";
                RecordTaskRun(task, result, origin, TimeSpan.Zero);
                return result;
            }

            foreach (var action in task.Actions)
            {
                if (ct.IsCancellationRequested)
                {
                    result.Cancelled = true;
                    result.Error = "Execução cancelada antes de concluir todas as ações.";
                    break;
                }

                var started = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    _logger.Log(LogLevel.Info, LogCategory.Scheduler,
                        $"[SchedulerService] ▶ Ação '{action}' da tarefa '{task.Name}'");

                    var actionResult = await ExecuteSingleActionAsync(action, task, ct).ConfigureAwait(false);
                    started.Stop();

                    if (actionResult.Success)
                    {
                        result.SucceededActions.Add(action);
                        totalSpaceFreed += actionResult.SpaceFreed;
                        _logger.Log(LogLevel.Success, LogCategory.Scheduler,
                            $"[SchedulerService] ✔ Ação '{action}' OK em {started.Elapsed.TotalSeconds:F1}s " +
                            $"liberado={actionResult.SpaceFreed} bytes{(string.IsNullOrEmpty(actionResult.Detail) ? "" : $" detalhe='{actionResult.Detail}'")}");
                    }
                    else
                    {
                        result.FailedActions.Add(action);
                        result.Errors.Add($"{action}: {actionResult.Error}");
                        _logger.Log(LogLevel.Error, LogCategory.Scheduler,
                            $"[SchedulerService] ✘ Ação '{action}' FALHOU em {started.Elapsed.TotalSeconds:F1}s: {actionResult.Error}");
                    }
                }
                catch (Exception ex)
                {
                    started.Stop();
                    result.FailedActions.Add(action);
                    result.Errors.Add($"{action}: {ex.GetType().Name}: {ex.Message}");
                    _logger.Log(LogLevel.Error, LogCategory.Scheduler,
                        $"[SchedulerService] ✘ Ação '{action}' lançou {ex.GetType().Name}: {ex.Message}", ex);
                }
            }

            result.SpaceFreed = totalSpaceFreed;

            // Sucesso só quando TODAS as ações realmente tiveram sucesso.
            result.Success = result.FailedActions.Count == 0 && result.SucceededActions.Count > 0 && !result.Cancelled;
            result.PartialFailure = result.FailedActions.Count > 0 && result.SucceededActions.Count > 0;
            if (!result.Success && result.Errors.Count > 0)
                result.Error = string.Join(" | ", result.Errors);

            result.ItemCount = result.SucceededActions.Count + result.FailedActions.Count;

            details["TaskID"] = task.Id;
            details["ScheduleType"] = task.ScheduleType.ToString();
            details["SucceededActions"] = string.Join(", ", result.SucceededActions);
            details["FailedActions"] = string.Join(", ", result.FailedActions);
            details["WindowsRegistered"] = WindowsTaskSchedulerBridge.TaskExists(task.Id, out _);

            RecordTaskRun(task, result, origin, result.Duration, totalSpaceFreed, details);
            return result;
        }

        private readonly struct ActionResult
        {
            public bool Success { get; init; }
            public long SpaceFreed { get; init; }
            public string Error { get; init; }
            public string Detail { get; init; }

            public static ActionResult Ok(long freed = 0, string detail = "") =>
                new() { Success = true, SpaceFreed = freed, Detail = detail };
            public static ActionResult Fail(string error) =>
                new() { Success = false, Error = error };
        }

        /// <summary>
        /// Mapeamento de ação -> serviço existente do VOLTRIS. Nenhuma ação executa
        /// lógica própria de otimização: todas delegam aos serviços já registrados no DI.
        /// </summary>
        private async Task<ActionResult> ExecuteSingleActionAsync(string action, ScheduledTask task, CancellationToken ct)
        {
            switch (action.Trim().ToLowerInvariant())
            {
                case "limpeza":
                    {
                        var cleaner = App.SystemCleaner;
                        if (cleaner == null) return ActionResult.Fail("Serviço de limpeza (SystemCleaner) indisponível.");

                        long freed = 0;
                        var steps = new List<string>();
                        try { freed += await cleaner.CleanTempFilesAsync().ConfigureAwait(false); steps.Add("temporários"); }
                        catch (Exception ex) { return ActionResult.Fail($"Falha em arquivos temporários: {ex.Message}"); }

                        try { await cleaner.EmptyRecycleBinAsync().ConfigureAwait(false); steps.Add("lixeira"); }
                        catch (Exception ex) { return ActionResult.Fail($"Falha ao esvaziar a lixeira: {ex.Message}"); }

                        try { await cleaner.CleanBrowserCacheAsync().ConfigureAwait(false); steps.Add("cache de navegadores"); }
                        catch (Exception ex) { return ActionResult.Fail($"Falha ao limpar cache de navegadores: {ex.Message}"); }

                        return ActionResult.Ok(freed, string.Join("+", steps));
                    }

                case "limpeza_inteligente":
                    {
                        var engine = ServiceLocator.GetService<IntelligentCleanupEngine>();
                        if (engine == null) return ActionResult.Fail("Serviço IntelligentCleanupEngine indisponível.");
                        var freed = await engine.RunIntelligentCleanupAsync().ConfigureAwait(false);
                        return ActionResult.Ok(freed, "motor de limpeza inteligente");
                    }

                case "limpeza_profunda":
                    {
                        // Reutiliza o mesmo caminho da limpeza profunda manual
                        // (UltraCleanerService), em vez de duplicar a lógica.
                        var ultra = App.UltraCleaner;
                        if (ultra == null) return ActionResult.Fail("Serviço de limpeza profunda (UltraCleanerService) indisponível.");

                        var cleanup = await ultra.PerformSafeCleanupAsync(null, ct).ConfigureAwait(false);
                        if (cleanup == null)
                            return ActionResult.Fail("O motor de limpeza profunda não retornou resultado.");

                        if (!cleanup.Success)
                        {
                            var errors = cleanup.Errors != null && cleanup.Errors.Count > 0
                                ? string.Join(" | ", cleanup.Errors.Take(5))
                                : "falhas não especificadas";
                            return ActionResult.Fail($"Limpeza profunda concluída com falhas: {errors}");
                        }

                        return ActionResult.Ok(cleanup.SpaceCleaned, $"{cleanup.ItemsCleaned} módulos");
                    }

                case "desempenho":
                    {
                        var perf = App.PerformanceOptimizer;
                        if (perf == null) return ActionResult.Fail("Serviço de otimização de desempenho indisponível.");
                        var steps = new List<string>();

                        try
                        {
                            var plan = await perf.SetHighPerformancePlanAsync().ConfigureAwait(false);
                            if (plan == null || !plan.Success)
                                return ActionResult.Fail($"Falha ao definir o plano de energia: {plan?.ErrorMessage ?? "sem resultado"}");
                            steps.Add("plano de energia");
                        }
                        catch (Exception ex) { return ActionResult.Fail($"Falha ao definir plano de energia: {ex.Message}"); }

                        try
                        {
                            var startup = await perf.OptimizeStartupAsync().ConfigureAwait(false);
                            if (startup == null || !startup.Success)
                                return ActionResult.Fail($"Falha ao otimizar a inicialização: {startup?.ErrorMessage ?? "sem resultado"}");
                            steps.Add("itens de inicialização");
                        }
                        catch (Exception ex) { return ActionResult.Fail($"Falha ao otimizar inicialização: {ex.Message}"); }

                        if (App.AdvancedOptimizer != null)
                        {
                            try
                            {
                                var mem = await App.AdvancedOptimizer.OptimizeMemoryAsync().ConfigureAwait(false);
                                if (!mem) return ActionResult.Fail("Otimização de memória reportou falha.");
                                steps.Add("memória");
                            }
                            catch (Exception ex) { return ActionResult.Fail($"Falha na otimização de memória: {ex.Message}"); }
                        }

                        return ActionResult.Ok(0, string.Join("+", steps));
                    }

                case "rede":
                    {
                        var net = App.NetworkOptimizer;
                        if (net == null) return ActionResult.Fail("Serviço de otimização de rede indisponível.");
                        var steps = new List<string>();

                        try { await net.FlushDnsAsync().ConfigureAwait(false); steps.Add("flush DNS"); }
                        catch (Exception ex) { return ActionResult.Fail($"Falha no flush de DNS: {ex.Message}"); }

                        try { await net.OptimizeTcpSettingsAsync().ConfigureAwait(false); steps.Add("parâmetros TCP"); }
                        catch (Exception ex) { return ActionResult.Fail($"Falha nos parâmetros TCP: {ex.Message}"); }

                        return ActionResult.Ok(0, string.Join("+", steps));
                    }

                case "avançado":
                    {
                        var adv = App.AdvancedOptimizer;
                        if (adv == null) return ActionResult.Fail("Serviço de otimização avançada indisponível.");
                        var steps = new List<string>();

                        try { await adv.OptimizeProcessesAsync().ConfigureAwait(false); steps.Add("prioridade de processos"); }
                        catch (Exception ex) { return ActionResult.Fail($"Falha na otimização de processos: {ex.Message}"); }

                        try { await adv.CleanRegistryAsync().ConfigureAwait(false); steps.Add("limpeza de registro"); }
                        catch (Exception ex) { return ActionResult.Fail($"Falha na limpeza de registro: {ex.Message}"); }

                        return ActionResult.Ok(0, string.Join("+", steps));
                    }

                case "reparacao_completa":
                    {
                        if (!IsRunningAsAdmin())
                            return ActionResult.Fail("Reparação completa exige privilégios administrativos.");

                        var repairSvc = new AdvancedRepairService(_logger);
                        var steps = new List<string>();

                        try { await repairSvc.Step08_CacheCleanAsync(ct).ConfigureAwait(false); steps.Add("cache"); }
                        catch (Exception ex) { return ActionResult.Fail($"Falha na limpeza de cache: {ex.Message}"); }

                        try { await repairSvc.Step07_RegistryRepairAsync(ct).ConfigureAwait(false); steps.Add("reparo de registro"); }
                        catch (Exception ex) { return ActionResult.Fail($"Falha no reparo de registro: {ex.Message}"); }

                        try { await repairSvc.Step16_IntegrityCheckAsync(ct).ConfigureAwait(false); steps.Add("integridade de arquivos"); }
                        catch (Exception ex) { return ActionResult.Fail($"Falha na verificação de integridade: {ex.Message}"); }

                        return ActionResult.Ok(0, string.Join("+", steps));
                    }

                case "gamer":
                    {
                        var orchestrator = ServiceLocator.GetService<VoltrisOptimizer.Services.Gamer.Interfaces.IGamerModeOrchestrator>();
                        if (orchestrator == null) return ActionResult.Fail("Orquestrador do Modo Gamer indisponível.");

                        if (orchestrator.IsActive) return ActionResult.Ok(0, "modo gamer já estava ativo");

                        var activated = await orchestrator
                            .ActivateAsync(new VoltrisOptimizer.Services.Gamer.Models.GamerOptimizationOptions(),
                                           isManual: false, cancellationToken: ct)
                            .ConfigureAwait(false);
                        return activated
                            ? ActionResult.Ok(0, "modo gamer ativado")
                            : ActionResult.Fail("O orquestrador recusou a ativação do Modo Gamer.");
                    }

                case "stream":
                    {
                        var streamSvc = ServiceLocator.GetService<VoltrisOptimizer.Services.StreamHub.Interfaces.IStreamHubService>();
                        if (streamSvc == null) return ActionResult.Fail("Serviço do Stream Hub indisponível.");

                        if (streamSvc.IsRunning) return ActionResult.Ok(0, "modo stream já estava ativo");

                        await streamSvc.StartAsync(streamSvc.Settings, ct).ConfigureAwait(false);
                        if (streamSvc.IsRunning)
                            return ActionResult.Ok(0, "modo stream ativado");
                        return ActionResult.Fail("O modo stream não entrou em execução.");
                    }

                default:
                    return ActionResult.Fail($"Ação desconhecida: '{action}'.");
            }
        }

        private void RecordTaskRun(ScheduledTask task, ScheduledTaskRunResult result, HistoryOrigin origin,
            TimeSpan duration, long spaceFreed = -1, Dictionary<string, object>? extraDetails = null)
        {
            if (spaceFreed < 0) spaceFreed = result.SpaceFreed;

            var details = new Dictionary<string, object>();
            if (extraDetails != null)
                foreach (var kv in extraDetails) details[kv.Key] = kv.Value;
            if (result.SucceededActions.Count > 0) details["Ações executadas"] = string.Join(", ", result.SucceededActions);
            if (result.FailedActions.Count > 0) details["Ações com falha"] = string.Join(", ", result.FailedActions);
            details["Janela de verificação"] = $"{duration.TotalSeconds:F1}s";

            _historyService.RecordActivityInstance(
                actionType: HistoryActionTypes.ScheduledAutomation,
                description: result.Skipped
                    ? $"Tarefa '{task.Name}' não executada: {result.Error}"
                    : $"Tarefa '{task.Name}' — {result.SucceededActions.Count} de {result.SucceededActions.Count + result.FailedActions.Count} ações concluídas.",
                success: result.Success,
                spaceFreed: spaceFreed,
                origin: origin,
                duration: duration,
                itemCount: result.ItemCount,
                errorMessage: result.Error,
                extraDetails: details);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Cálculo de próxima execução
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Calcula a próxima execução. <paramref name="force"/>=true recalcula mesmo
        /// quando a data atual ainda não passou (usado ao criar/editar, para a UI
        /// mostrar um horário futuro real).
        /// </summary>
        public DateTime? CalculateNextExecution(ScheduledTask task, DateTime now, bool force)
        {
            var time = task.ScheduledTime?.TimeOfDay ?? new TimeSpan(8, 0, 0);

            switch (task.ScheduleType)
            {
                case ScheduleType.Daily:
                    {
                        var candidate = now.Date + time;
                        if (candidate <= now) candidate = candidate.AddDays(1);
                        return candidate;
                    }

                case ScheduleType.Weekly:
                    {
                        if (task.DaysOfWeek == WeekdaySelection.None)
                        {
                            _logger.Log(LogLevel.Warning, LogCategory.Scheduler,
                                $"[SchedulerService] Tarefa '{task.Name}' é semanal mas não tem dia definido. " +
                                "Usando o dia atual como fallback.");
                        }
                        var allowed = task.DaysOfWeek == WeekdaySelection.None
                            ? ToFlag(now.DayOfWeek)
                            : task.DaysOfWeek;

                        for (int i = 0; i <= 7; i++)
                        {
                            var day = now.Date.AddDays(i);
                            if (!allowed.HasFlag(ToFlag(day.DayOfWeek))) continue;
                            var candidate = day + time;
                            if (candidate > now) return candidate;
                        }
                        // Nenhum dia futuro na janela de 7 dias: avança uma semana.
                        return now.Date.AddDays(7) + time;
                    }

                case ScheduleType.Monthly:
                    {
                        var days = task.DaysOfMonth;
                        if (days == null || days.Count == 0)
                        {
                            _logger.Log(LogLevel.Warning, LogCategory.Scheduler,
                                $"[SchedulerService] Tarefa '{task.Name}' é mensal mas não tem dia definido. Usando dia 1 como fallback.");
                            var first = new DateTime(now.Year, now.Month, 1) + time;
                            return first > now ? first : first.AddMonths(1);
                        }

                        for (int monthOffset = 0; monthOffset <= 2; monthOffset++)
                        {
                            var monthStart = new DateTime(now.Year, now.Month, 1).AddMonths(monthOffset);
                            foreach (var d in days.Where(x => x >= 1 && x <= 31).Distinct().OrderBy(x => x))
                            {
                                if (d > DateTime.DaysInMonth(monthStart.Year, monthStart.Month)) continue;
                                var candidate = new DateTime(monthStart.Year, monthStart.Month, d) + time;
                                if (candidate > now) return candidate;
                            }
                        }
                        return (new DateTime(now.Year, now.Month, 1).AddMonths(1)) + time;
                    }

                case ScheduleType.Once:
                    {
                        if (!task.ScheduledTime.HasValue)
                        {
                            _logger.Log(LogLevel.Error, LogCategory.Scheduler,
                                $"[SchedulerService] Tarefa '{task.Name}' de execução única sem data definida.");
                            return null;
                        }
                        // A data escolhida é absoluta: se já passou, não há próxima.
                        return task.ScheduledTime.Value > now ? task.ScheduledTime.Value : (DateTime?)null;
                    }

                case ScheduleType.OnStartup:
                    // Executado pelo <BootTrigger> do Windows. Não há "próxima" por relógio.
                    return null;

                case ScheduleType.OnLogon:
                    // Executado pelo <LogonTrigger> do Windows.
                    return null;

                default:
                    return null;
            }
        }

        private static WeekdaySelection ToFlag(DayOfWeek dow) => dow switch
        {
            DayOfWeek.Sunday => WeekdaySelection.Sunday,
            DayOfWeek.Monday => WeekdaySelection.Monday,
            DayOfWeek.Tuesday => WeekdaySelection.Tuesday,
            DayOfWeek.Wednesday => WeekdaySelection.Wednesday,
            DayOfWeek.Thursday => WeekdaySelection.Thursday,
            DayOfWeek.Friday => WeekdaySelection.Friday,
            DayOfWeek.Saturday => WeekdaySelection.Saturday,
            _ => WeekdaySelection.None
        };

        // ─────────────────────────────────────────────────────────────────────────
        // Persistência
        // ─────────────────────────────────────────────────────────────────────────

        private void LoadSchedules()
        {
            lock (_tasksLock)
            {
                var loaded = TryLoadFrom(_schedulesPath);
                if (loaded == null)
                {
                    loaded = TryLoadFrom(_schedulesBackupPath);
                    if (loaded != null)
                    {
                        _logger.Log(LogLevel.Warning, LogCategory.Scheduler,
                            $"[SchedulerService] schedules.json ilegível; restaurado do backup ({loaded.Count} tarefas).");
                    }
                }

                if (loaded != null)
                {
                    _tasks = loaded;
                    NormalizeLoadedTasks();
                    try
                    {
                        var fi = new FileInfo(_schedulesPath);
                        _lastDiskStamp = fi.Exists ? fi.LastWriteTimeUtc : DateTime.MinValue;
                    }
                    catch { /* ignore */ }

                    _logger.Log(LogLevel.Info, LogCategory.Scheduler,
                        $"[SchedulerService] {_tasks.Count} tarefa(s) agendada(s) carregada(s) do disco.");
                    return;
                }

                if (File.Exists(_schedulesPath))
                {
                    _logger.Log(LogLevel.Error, LogCategory.Scheduler,
                        "[SchedulerService] schedules.json existe e está ilegível, e não há backup utilizável. " +
                        "O arquivo em disco NÃO será apagado nesta sessão.");
                }

                _tasks = new List<ScheduledTask>();
            }
        }

        private void NormalizeLoadedTasks()
        {
            var now = DateTime.Now;
            foreach (var t in _tasks)
            {
                if (string.IsNullOrWhiteSpace(t.Id)) t.Id = Guid.NewGuid().ToString("N");
                t.Actions ??= new List<string>();
                t.LastResult ??= new ScheduledTaskRunResult();
                // Recalcula porque o valor persistido pode estar no passado (execução
                // que não ocorreu porque o app estava fechado).
                t.NextExecution = CalculateNextExecution(t, now, force: true);
            }
        }

        private List<ScheduledTask>? TryLoadFrom(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                {
                    _logger.Log(LogLevel.Warning, LogCategory.Scheduler,
                        $"[SchedulerService] Arquivo '{path}' está vazio.");
                    return null;
                }
                return JsonSerializer.Deserialize<List<ScheduledTask>>(json);
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, LogCategory.Scheduler,
                    $"[SchedulerService] Falha ao ler '{path}': {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        private bool SaveSchedules(out string error)
        {
            error = string.Empty;
            List<ScheduledTask> snapshot;
            lock (_tasksLock) { snapshot = _tasks.ToList(); }

            string? tempPath = null;
            try
            {
                var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
                {
                    ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
                    WriteIndented = true
                });

                tempPath = _schedulesPath + ".tmp";
                File.WriteAllText(tempPath, json);

                // Backup do último estado bom antes de substituir.
                try
                {
                    if (File.Exists(_schedulesPath))
                        File.Copy(_schedulesPath, _schedulesBackupPath, overwrite: true);
                }
                catch (Exception ex)
                {
                    _logger.Log(LogLevel.Debug, LogCategory.Scheduler,
                        "[SchedulerService] Backup de schedules.json falhou: " + ex.Message);
                }

                File.Copy(tempPath, _schedulesPath, overwrite: true);

                // Marca o que acabamos de gravar para que ReloadIfChangedOnDisk
                // não releia o próprio arquivo.
                try
                {
                    var fi = new FileInfo(_schedulesPath);
                    _lastDiskStamp = fi.LastWriteTimeUtc;
                }
                catch { /* ignore */ }

                return true;
            }
            catch (Exception ex)
            {
                error = $"{ex.GetType().Name}: {ex.Message}";
                _logger.Log(LogLevel.Error, LogCategory.Scheduler,
                    $"[SchedulerService] Erro ao salvar agendamentos: {error}", ex);
                return false;
            }
            finally
            {
                if (tempPath != null)
                {
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Reconciliação com o Agendador do Windows
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Garante que o estado do disco e o Agendador do Windows concordem.
        /// Executa no startup da aplicação e após cada alteração estrutural.
        /// </summary>
        public void ReconcileWithWindowsScheduler()
        {
            if (_disposed || _reconciliationDone) return;
            _reconciliationDone = true;

            if (!WindowsTaskSchedulerBridge.IsSupported(out var reason))
            {
                _logger.Log(LogLevel.Warning, LogCategory.Scheduler,
                    $"[SchedulerService] Agendador do Windows indisponível ({reason}). " +
                    "As tarefas funcionarão apenas enquanto o VOLTRIS estiver em execução.");
                return;
            }

            List<ScheduledTask> snapshot;
            lock (_tasksLock) { snapshot = _tasks.ToList(); }

            int registered = 0, failed = 0, absent = 0;
            foreach (var task in snapshot)
            {
                var exists = WindowsTaskSchedulerBridge.TaskExists(task.Id, out _);
                if (!exists)
                {
                    absent++;
                    if (RegisterInWindows(task, out var regErr))
                    {
                        registered++;
                        _logger.Log(LogLevel.Info, LogCategory.Scheduler,
                            $"[SchedulerService] Tarefa '{task.Name}' estava ausente no Windows e foi registrada agora.");
                    }
                    else
                    {
                        failed++;
                        _logger.Log(LogLevel.Warning, LogCategory.Scheduler,
                            $"[SchedulerService] Não foi possível registrar '{task.Name}' no Windows: {regErr}");
                    }
                }
                else
                {
                    registered++;
                }

                // Reflete o estado real do toggle no Windows.
                WindowsTaskSchedulerBridge.SetTaskEnabled(task.Id, task.IsEnabled, out _);
            }

            _logger.Log(LogLevel.Success, LogCategory.Scheduler,
                $"[SchedulerService] Reconciliação concluída: {snapshot.Count} tarefa(s), " +
                $"{registered} registrada(s) no Windows, {absent} ausente(s) corrigida(s), {failed} falha(s).");
        }

        private bool RegisterInWindows(ScheduledTask task, out string error)
        {
            try
            {
                return WindowsTaskSchedulerBridge.RegisterTask(task, out error);
            }
            catch (Exception ex)
            {
                error = $"{ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }

        private bool UnregisterFromWindows(string taskId, out string error)
        {
            try
            {
                return WindowsTaskSchedulerBridge.UnregisterTask(taskId, out error);
            }
            catch (Exception ex)
            {
                error = $"{ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }

        public static bool IsRunningAsAdmin()
        {
            try
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Pausa / retomada (Modo Gamer)
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Pausa as tarefas agendadas por ação do Modo Gamer.
        /// Guarda o estado anterior em <see cref="ScheduledTask.UserEnabled"/>
        /// para que a retomada NÃO sobrescreva decisões do usuário.
        /// </summary>
        public void PauseScheduledTasks()
        {
            if (_isPausedByGamerMode) return;

            lock (_tasksLock)
            {
                foreach (var task in _tasks.Where(t => t.IsEnabled))
                {
                    task.UserEnabled = true;   // memoriza o estado real antes de pausar
                    task.IsEnabled = false;
                    WindowsTaskSchedulerBridge.SetTaskEnabled(task.Id, false, out _);
                }
            }
            _isPausedByGamerMode = true;
            SaveSchedules(out _);
            _logger.Log(LogLevel.Info, LogCategory.Scheduler,
                "[SchedulerService] 🎮 Tarefas agendadas pausadas pelo Modo Gamer (estado do usuário preservado).");
        }

        /// <summary>
        /// Retoma as tarefas pausadas pelo Modo Gamer, restaurando exatamente o
        /// estado anterior em vez de reativar tudo indiscriminadamente.
        /// </summary>
        public void ResumeScheduledTasks()
        {
            if (!_isPausedByGamerMode) return;

            lock (_tasksLock)
            {
                foreach (var task in _tasks)
                {
                    // Só restaura quem estava efetivamente habilitado antes da pausa.
                    // Tarefas que o próprio usuário mantinha pausadas (UserEnabled == null
                    // porque nunca foram pausadas por nós) continuam pausadas.
                    var shouldBeEnabled = task.UserEnabled ?? false;
                    task.UserEnabled = null;
                    task.IsEnabled = shouldBeEnabled;
                    WindowsTaskSchedulerBridge.SetTaskEnabled(task.Id, shouldBeEnabled, out _);
                }
            }
            _isPausedByGamerMode = false;
            SaveSchedules(out _);
            _logger.Log(LogLevel.Info, LogCategory.Scheduler,
                "[SchedulerService] ✅ Tarefas agendadas retomadas após Modo Gamer (estado do usuário restaurado).");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                _checkTimer?.Stop();
                _checkTimer?.Dispose();
                _checkTimer = null;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("[SchedulerService] Erro no Dispose do timer: " + ex.Message);
            }
        }
    }

    public class ScheduledTask
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public ScheduleType ScheduleType { get; set; } = ScheduleType.Daily;

        /// <summary>Data e hora da execução (dia + horário).</summary>
        public DateTime? ScheduledTime { get; set; }

        /// <summary>Dias da semana para tarefas semanais.</summary>
        public WeekdaySelection DaysOfWeek { get; set; } = WeekdaySelection.None;

        /// <summary>Dias do mês para tarefas mensais (1..31).</summary>
        public List<int>? DaysOfMonth { get; set; }

        public List<string> Actions { get; set; } = new List<string>();

        /// <summary>Estado efetivo (inclui pausa pelo Modo Gamer).</summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// Estado escolhido pelo usuário. Preservado durante a pausa pelo Modo Gamer
        /// para que a retomada não sobrescreva tarefas que o usuário mantinha pausadas.
        /// </summary>
        public bool? UserEnabled { get; set; }

        public DateTime? LastExecuted { get; set; }
        public DateTime? NextExecution { get; set; }

        /// <summary>Desfecho real da última execução (exibido na UI).</summary>
        public ScheduledTaskRunResult? LastResult { get; set; }

        /// <summary>Último erro de registro no Agendador do Windows, se houver.</summary>
        public string? WindowsRegistrationError { get; set; }
    }

    public enum ScheduleType
    {
        /// <summary>Executa uma única vez na data/hora informada e se desativa.</summary>
        Once = 0,
        Daily,
        Weekly,
        Monthly,
        OnStartup,
        OnLogon
    }

    /// <summary>
    /// Desfecho real de uma execução agendada. Nenhum campo é assumido:
    /// <see cref="Success"/> só é verdadeiro quando todas as ações reportaram sucesso.
    /// </summary>
    public class ScheduledTaskRunResult
    {
        public string TaskId { get; set; } = "";
        public string TaskName { get; set; } = "";
        public bool Success { get; set; }
        public bool PartialFailure { get; set; }
        public bool Skipped { get; set; }
        public bool Cancelled { get; set; }
        public string Error { get; set; } = "";
        public TimeSpan Duration { get; set; }
        public long SpaceFreed { get; set; }
        public int ItemCount { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public List<string> SucceededActions { get; set; } = new List<string>();
        public List<string> FailedActions { get; set; } = new List<string>();
        public List<string> Errors { get; set; } = new List<string>();
    }
}
