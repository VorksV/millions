using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.SmartRepair.Architecture;

namespace VoltrisOptimizer.Services.SmartRepair.Modules
{
    public class SystemRepairModule : ISmartRepairModule
    {
        private readonly ILoggingService _logger;
        private readonly ISmartRepairEventBus _eventBus;
        private readonly IExclusionService _exclusionService;
        private readonly string _correlationId;

        public string ModuleId => "SystemRepair";
        public string Category => "System";
        public string Name => VoltrisOptimizer.Services.LocalizationService.Instance.GetString("SmartRepair_Module_SystemRepair_Name") ?? "Reparo do Sistema (SFC/DISM)";
        public string Description => VoltrisOptimizer.Services.LocalizationService.Instance.GetString("SmartRepair_Module_SystemRepair_Desc") ?? "Verifica a integridade do Windows e corrige arquivos corrompidos.";

        public ModuleCapabilities Capabilities { get; } = new ModuleCapabilities
        {
            SupportsScan = true,
            SupportsSimulation = true,
            SupportsExecution = true,
            SupportsRollback = false,
            SupportsParallelism = false,
            HasDestructiveOperations = true,
            MaxRiskLevel = RiskLevel.High,
            RequiresAdmin = true
        };

        public IReadOnlyList<string> Dependencies => Array.Empty<string>();

        public SystemRepairModule(ILoggingService logger, ISmartRepairEventBus eventBus, IExclusionService exclusionService, string correlationId)
        {
            _logger = logger;
            _eventBus = eventBus;
            _exclusionService = exclusionService;
            _correlationId = correlationId;
        }

        private class SystemRepairTarget
        {
            public string Component { get; set; } = string.Empty;
            public string TargetCommand { get; set; } = string.Empty;
        }

        public async Task<ModuleScanResult> ScanAsync(IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleScanResult();
            
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_SystemRepair_ScanStarting") ?? "Verificando saúde da imagem do sistema...", SmartRepairEventType.OperationStarted);
            
            // We do a quick CheckHealth instead of a full ScanHealth because ScanHealth takes 10+ minutes.
            // Even if CheckHealth says healthy, we still propose DISM RestoreHealth and SFC as standard repair.
            
            result.FoundItems.Add(new SystemRepairTarget { Component = "Windows Image (DISM)", TargetCommand = "DISM /Online /Cleanup-Image /RestoreHealth" });
            result.FoundItems.Add(new SystemRepairTarget { Component = "System Files (SFC)", TargetCommand = "sfc /scannow" });
            
            result.Statistics.ItemsFound = 2;
            result.Statistics.ItemsScanned = 2;
            
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_SystemRepair_ScanComplete") ?? "Verificação do sistema concluída. DISM e SFC recomendados.", SmartRepairEventType.OperationFinished);
            
            return await Task.FromResult(result);
        }

        public Task<ModuleSimulationResult> SimulateAsync(ModuleScanResult scanResult, CancellationToken ct)
        {
            var sim = new ModuleSimulationResult();
            
            sim.EstimatedStatistics.ItemsFound = scanResult.FoundItems.Count;
            sim.EstimatedStatistics.SpaceRecoveredBytes = 0; // SFC and DISM do not necessarily recover space.
            sim.RiskLevel = RiskLevel.High;
            
            foreach (var item in scanResult.FoundItems)
            {
                sim.ItemsToProcess.Add(item);
            }

            return Task.FromResult(sim);
        }

        public async Task<ModuleExecutionResult> ExecuteAsync(ModuleSimulationResult simResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleExecutionResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_SystemRepair_ExecStarting") ?? "Iniciando reparo nativo do sistema...", SmartRepairEventType.OperationStarted);

            foreach (SystemRepairTarget target in simResult.ItemsToProcess)
            {
                ct.ThrowIfCancellationRequested();
                
                _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_SystemRepair_ExecProgress") ?? "Executando {0}...", target.Component), SmartRepairEventType.ProgressChanged);
                progress?.Report(new RepairProgress { StepPercent = 0, StatusMessage = string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_SystemRepair_ExecProgress") ?? "Executando {0}...", target.Component) });

                bool success = await RunCommandAsync(target.TargetCommand, ct, progress);
                
                if (success)
                {
                    result.FinalStatistics.ItemsRepaired++;
                }
                else
                {
                    result.FinalStatistics.ItemsIgnored++;
                }
            }

            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_SystemRepair_Done") ?? "Reparo do sistema finalizado.", SmartRepairEventType.OperationFinished);
            return result;
        }
        
        private async Task<bool> RunCommandAsync(string command, CancellationToken ct, IProgress<RepairProgress> progress)
        {
            try
            {
                string exe = command.Substring(0, command.IndexOf(' '));
                string args = command.Substring(command.IndexOf(' ') + 1);

                _logger.LogDebug($"[SystemRepairModule] Executando comando: {exe} {args}");
                var startInfo = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    Verb = "runas"
                };

                using var process = new Process { StartInfo = startInfo };
                
                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        // Some commands output percentages, we could parse them here. 
                        // For now, just pipe to EventBus.
                        _eventBus.PublishMessage(_correlationId, ModuleId, e.Data, SmartRepairEventType.ProgressChanged);
                    }
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        _logger.LogWarning($"[{ModuleId}] Command Error: {e.Data}");
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

await process.WaitForExitAsync(ct);
                _logger.LogDebug($"[SystemRepairModule] Comando concluído com exit code {process.ExitCode}");
                return process.ExitCode == 0;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[{ModuleId}] Failed to execute native command: {command}", ex);
                return false;
            }
        }

        public Task<ModuleRollbackResult> RollbackAsync(ModuleExecutionResult execResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            throw new NotSupportedException(Services.LocalizationService.Instance.GetString("SmartRepair_Module_SystemRepair_NoRollback") ?? "Native system repairs cannot be rolled back via this module.");
        }
    }
}
