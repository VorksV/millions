using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.SmartRepair.Architecture;

namespace VoltrisOptimizer.Services.SmartRepair.Modules
{
    public class LegacyStepAdapterModule : ISmartRepairModule
    {
        private readonly RepairStepBase _legacyStep;
        private readonly ILoggingService _logger;
        private readonly ISmartRepairEventBus _eventBus;
        private readonly string _correlationId;

        public string ModuleId => $"LegacyStep_{_legacyStep.Id:D2}";
        public string Category => "System";
        
        // This will pull translated Name/Description using the step's built-in properties
        public string Name => _legacyStep.Name ?? $"Etapa {_legacyStep.Id:D2}";
        public string Description => _legacyStep.Description ?? "Otimização do sistema Windows";

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

        public LegacyStepAdapterModule(RepairStepBase legacyStep, ILoggingService logger, ISmartRepairEventBus eventBus, string correlationId)
        {
            _legacyStep = legacyStep;
            _logger = logger;
            _eventBus = eventBus;
            _correlationId = correlationId;
        }

        public Task<ModuleScanResult> ScanAsync(IProgress<RepairProgress> progress, CancellationToken ct)
        {
            _logger.LogDebug($"[LegacyStepAdapterModule] ScanAsync iniciado para step {_legacyStep.Id}");
            var result = new ModuleScanResult();
            // Dummy scan representation
            result.FoundItems.Add(new { Target = _legacyStep.Name });
            result.Statistics.ItemsFound = 1;
            result.Statistics.ItemsScanned = 1;
            return Task.FromResult(result);
        }

        public Task<ModuleSimulationResult> SimulateAsync(ModuleScanResult scanResult, CancellationToken ct)
        {
            _logger.LogDebug($"[LegacyStepAdapterModule] SimulateAsync iniciado para step {_legacyStep.Id}");
            var result = new ModuleSimulationResult
            {
                Success = true,
                EstimatedStatistics = new ModuleStatistics { EstimatedTime = TimeSpan.FromSeconds(5) }
            };
            return Task.FromResult(result);
        }

        public async Task<ModuleExecutionResult> ExecuteAsync(ModuleSimulationResult simulation, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            _logger.LogDebug($"[LegacyStepAdapterModule] ExecuteAsync iniciado para step {_legacyStep.Id}");
            _eventBus.PublishMessage(_correlationId, ModuleId, $"Iniciando {_legacyStep.Name}...", SmartRepairEventType.OperationStarted);

            // Create a sub-progress to map the old RepairProgress to the new UI structure
            var subProgress = new Progress<RepairProgress>(p =>
            {
                p.CurrentStepId = _legacyStep.Id;
                progress?.Report(p);
                if (!string.IsNullOrEmpty(p.StatusMessage))
                {
                    _eventBus.PublishMessage(_correlationId, ModuleId, p.StatusMessage, SmartRepairEventType.ProgressChanged);
                }
            });

            try
            {
                // Execute the actual old step
                var stepResult = await _legacyStep.ExecuteAsync(subProgress, ct);
                _logger.LogDebug($"[LegacyStepAdapter] Step {_legacyStep.Id} completed. Success={stepResult.Success}, Details={stepResult.DetailSummary}");
                return new ModuleExecutionResult
                {
                    Success = stepResult.Success,
                    Message = stepResult.ErrorMessage ?? stepResult.DetailSummary,
                    FinalStatistics = new ModuleStatistics
                    {
                        ItemsRepaired = stepResult.Success ? 1 : 0,
                        SpaceRecoveredBytes = stepResult.SpaceRecoveredBytes
                    }
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[LegacyStepAdapterModule] Erro ao executar etapa {_legacyStep.Id}: {ex.Message}", ex);
                return new ModuleExecutionResult { Success = false, Message = ex.Message };
            }
        }

        public Task<ModuleRollbackResult> RollbackAsync(ModuleExecutionResult execResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            return Task.FromResult(new ModuleRollbackResult { Success = false, Message = "Not supported" });
        }
    }
}
