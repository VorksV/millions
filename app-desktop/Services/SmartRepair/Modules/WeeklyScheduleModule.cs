using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.SmartRepair.Architecture;

namespace VoltrisOptimizer.Services.SmartRepair.Modules
{
    public class WeeklyScheduleModule : ISmartRepairModule
    {
        private readonly ILoggingService _logger;
        private readonly ISmartRepairEventBus _eventBus;
        private readonly string _correlationId;

        private const string TaskName = "VoltrisSmartRepair";
        private const string TaskTime = "03:00";
        private const string TaskDays = "SUN";

        public string ModuleId => "WeeklySchedule";
        public string Category => "Maintenance";

        public string Name => Services.LocalizationService.Instance.GetString("SmartRepair_Module_WeeklySchedule_Name") ?? "Agendamento Semanal de Reparo";
        public string Description => Services.LocalizationService.Instance.GetString("SmartRepair_Module_WeeklySchedule_Desc") ?? "Agenda execução automática do Smart Repair semanalmente aos domingos às 03:00.";

        public ModuleCapabilities Capabilities { get; } = new ModuleCapabilities
        {
            SupportsScan = true,
            SupportsSimulation = true,
            SupportsExecution = true,
            SupportsRollback = true,
            SupportsParallelism = false,
            HasDestructiveOperations = false,
            MaxRiskLevel = RiskLevel.Safe,
            RequiresAdmin = true
        };

        public IReadOnlyList<string> Dependencies => Array.Empty<string>();

        public WeeklyScheduleModule(ILoggingService logger, ISmartRepairEventBus eventBus, string correlationId)
        {
            _logger = logger;
            _eventBus = eventBus;
            _correlationId = correlationId;
        }

        public Task<ModuleScanResult> ScanAsync(IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleScanResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_WeeklySchedule_ScanStarting") ?? "Verificando agendamento semanal do Smart Repair...", SmartRepairEventType.OperationStarted);

            var taskExists = CheckTaskExists();

            if (taskExists)
            {
                result.FoundItems.Add(new { Task = TaskName, Status = "AlreadyExists" });
                result.Statistics.ItemsFound = 1;
                _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_WeeklySchedule_ScanAlreadyExists") ?? "Tarefa agendada já existe.", SmartRepairEventType.OperationFinished);
            }
            else
            {
                result.FoundItems.Add(new { Task = TaskName, Status = "NotExists" });
                result.Statistics.ItemsFound = 1;
                _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_WeeklySchedule_ScanNotExists") ?? "Tarefa agendada não encontrada — será criada.", SmartRepairEventType.OperationFinished);
            }

            result.Statistics.ItemsScanned = 1;
            progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = result.Message ?? "Scan concluído." });
            _logger.LogInfo($"[WeeklySchedule] Scan concluído. TaskExists={taskExists}, CorrelationId={_correlationId}");

            return Task.FromResult(result);
        }

        private static bool CheckTaskExists()
        {
            try
            {
                ProcessHelper.Run("schtasks", $"/Query /TN \"{TaskName}\" /FO LIST", out var output, out var error, 10000);
                return !string.IsNullOrEmpty(output) && output.Contains("TaskName:", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public Task<ModuleSimulationResult> SimulateAsync(ModuleScanResult scanResult, CancellationToken ct)
        {
            var sim = new ModuleSimulationResult();
            sim.RiskLevel = RiskLevel.Safe;

            foreach (var item in scanResult.FoundItems)
            {
                sim.ItemsToProcess.Add(item);
            }

            sim.EstimatedStatistics.ItemsFound = scanResult.FoundItems.Count;
            _logger.LogInfo($"[WeeklySchedule] Simulação concluída. Itens={sim.ItemsToProcess.Count}, Risk={sim.RiskLevel}");
            return Task.FromResult(sim);
        }

        public async Task<ModuleExecutionResult> ExecuteAsync(ModuleSimulationResult simResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleExecutionResult();

            // Check if task already exists
            _logger.LogDebug($"[WeeklySchedule] Verificando existência da tarefa agendada...");
            if (CheckTaskExists())
            {
                _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_WeeklySchedule_ExecDone") ?? "Tarefa já existe. Nenhuma ação necessária.", SmartRepairEventType.OperationFinished);
                result.FinalStatistics.ItemsIgnored = 1;
                return result;
            }

            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_WeeklySchedule_ExecCreating") ?? "Criando tarefa agendada semanal...", SmartRepairEventType.OperationStarted);
            progress?.Report(new RepairProgress { StepPercent = 10, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_WeeklySchedule_ExecCreating") ?? "Criando tarefa..." });

            try
            {
                var appPath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "VoltrisOptimizer.exe";
                var appDir = Path.GetDirectoryName(appPath) ?? Environment.CurrentDirectory;
                var executablePath = Path.Combine(appDir, "VoltrisOptimizer.exe");

                if (!File.Exists(executablePath))
                {
                    // Fall back to the current process path
                    executablePath = appPath;
                }

                _logger.LogDebug($"[WeeklySchedule] Executando schtasks /Create para {TaskName}...");
                var arguments = $"/Create /SC WEEKLY /D {TaskDays} /TN \"{TaskName}\" /TR \"\\\"{executablePath}\\\" --smart-repair-auto\" /ST {TaskTime} /RL HIGHEST /RU SYSTEM /F";
                var success = ProcessHelper.Run("schtasks", arguments, out var stdOut, out var stdErr, 30000);

                if (success)
                {
                    result.FinalStatistics.ItemsRepaired = 1;
                    _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_WeeklySchedule_ExecDone") ?? "Tarefa agendada criada com sucesso.", SmartRepairEventType.OperationFinished);
                    _logger.LogInfo($"[WeeklySchedule] Task created successfully. {stdOut}");
                }
                else
                {
                    result.Success = false;
                    result.Message = stdErr;
                    _eventBus.PublishMessage(_correlationId, ModuleId, string.Format("Falha ao criar tarefa: {0}", stdErr), SmartRepairEventType.ErrorRaised);
                    _logger.LogError($"[WeeklySchedule] Falha ao criar tarefa: {stdErr}");
                }

                progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_WeeklySchedule_ExecDone") ?? "Concluído." });
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError($"[WeeklySchedule] Erro ao criar tarefa", ex);
                result.Success = false;
                result.Message = ex.Message;
            }

            return result;
        }

        public async Task<ModuleRollbackResult> RollbackAsync(ModuleExecutionResult execResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleRollbackResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_WeeklySchedule_RollbackStarting") ?? "Removendo tarefa agendada...", SmartRepairEventType.RollbackStarted);
            progress?.Report(new RepairProgress { StepPercent = 10, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_WeeklySchedule_RollbackStarting") ?? "Removendo tarefa..." });

            try
            {
                var success = ProcessHelper.Run("schtasks", $"/Delete /TN \"{TaskName}\" /F", out var stdOut, out var stdErr, 15000);

                if (success)
                {
                    result.ItemsRestored = 1;
                    result.Success = true;
                    _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_WeeklySchedule_RollbackDone") ?? "Tarefa agendada removida com sucesso.", SmartRepairEventType.RollbackFinished);
                    _logger.LogInfo($"[WeeklySchedule] Rollback concluído. Task removed.");
                }
                else
                {
                    result.Success = false;
                    result.Message = stdErr;
                    _logger.LogError($"[WeeklySchedule] Falha ao remover tarefa: {stdErr}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[WeeklySchedule] Rollback error", ex);
                result.Success = false;
                result.Message = ex.Message;
            }

            return result;
        }
    }
}
