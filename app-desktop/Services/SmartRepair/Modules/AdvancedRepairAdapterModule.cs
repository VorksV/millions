using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.SmartRepair.Architecture;

namespace VoltrisOptimizer.Services.SmartRepair.Modules
{
    public class AdvancedRepairAdapterModule : ISmartRepairModule
    {
        private readonly ILoggingService _logger;
        private readonly ISmartRepairEventBus _eventBus;
        private readonly string _correlationId;
        private readonly AdvancedRepairService _advancedRepairService;

        public string ModuleId => "AdvancedRepair";
        public string Category => "System";
        public string Name => VoltrisOptimizer.Services.LocalizationService.Instance.GetString("SmartRepair_Module_Advanced_Name") ?? "Reparo Avançado do Sistema";
        public string Description => VoltrisOptimizer.Services.LocalizationService.Instance.GetString("SmartRepair_Module_Advanced_Desc") ?? "Executa todas as 18 etapas profundas de restauração estrutural, incluindo rede, serviços, cache, boot e muito mais.";

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

        public AdvancedRepairAdapterModule(ILoggingService logger, ISmartRepairEventBus eventBus, string correlationId)
        {
            _logger = logger;
            _eventBus = eventBus;
            _correlationId = correlationId;
            _advancedRepairService = new AdvancedRepairService(logger);

            // Redirecionando os logs estáticos/callbacks do serviço legado para o EventBus moderno
            _advancedRepairService.OnLog = (msg, hexColor) =>
            {
                // msg muitas vezes contém ícones como '✓', '⚠', '→' e espaços.
                _eventBus.PublishMessage(_correlationId, ModuleId, msg.Trim(), SmartRepairEventType.ProgressChanged);
            };

            _advancedRepairService.OnProgress = (pct, msg) =>
            {
                // Pode disparar progress update, mas nós vamos gerenciar via Report() no ExecuteAsync.
                _eventBus.PublishMessage(_correlationId, ModuleId, msg.Trim(), SmartRepairEventType.ProgressChanged);
            };
        }

        private class AdvancedRepairTarget
        {
            public int StepNumber { get; set; }
            public string StepName { get; set; } = string.Empty;
        }

        public Task<ModuleScanResult> ScanAsync(IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleScanResult();
            
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Advanced_ScanStarting") ?? "Mapeando as etapas do Reparo Completo...", SmartRepairEventType.OperationStarted);

            // Mapeando todas as 18 etapas originais (+ CPU Fix)
            var targets = new List<AdvancedRepairTarget>
            {
                new AdvancedRepairTarget { StepNumber = 0, StepName = "Ponto de Restauração (Step00)" },
                new AdvancedRepairTarget { StepNumber = 1, StepName = "Reset de Rede (Step01)" },
                new AdvancedRepairTarget { StepNumber = 2, StepName = "Reparo .NET Framework (Step02)" },
                new AdvancedRepairTarget { StepNumber = 3, StepName = "Reparo Visual C++ (Step03)" },
                new AdvancedRepairTarget { StepNumber = 4, StepName = "Reset Windows Store (Step04)" },
                new AdvancedRepairTarget { StepNumber = 5, StepName = "Reparo de Boot (Step05)" },
                new AdvancedRepairTarget { StepNumber = 6, StepName = "Reset de Serviços (Step06)" },
                new AdvancedRepairTarget { StepNumber = 7, StepName = "Reparo do Registro (Step07)" },
                new AdvancedRepairTarget { StepNumber = 8, StepName = "Limpeza de Cache Profunda (Step08)" },
                new AdvancedRepairTarget { StepNumber = 9, StepName = "Reparo do Windows Defender (Step09)" },
                new AdvancedRepairTarget { StepNumber = 10, StepName = "Reparo de Drivers (Step10)" },
                new AdvancedRepairTarget { StepNumber = 11, StepName = "Análise de BSOD (Step11)" },
                new AdvancedRepairTarget { StepNumber = 12, StepName = "Reparo do Event Viewer (Step12)" },
                new AdvancedRepairTarget { StepNumber = 13, StepName = "Reset do Print Spooler (Step13)" },
                new AdvancedRepairTarget { StepNumber = 14, StepName = "Reparo DirectX e Áudio (Step14)" },
                new AdvancedRepairTarget { StepNumber = 15, StepName = "Reparo de Perfil de Usuário (Step15)" },
                new AdvancedRepairTarget { StepNumber = 16, StepName = "Verificação de Integridade DISM/SFC (Step16)" },
                new AdvancedRepairTarget { StepNumber = 17, StepName = "Diagnóstico Inteligente (Step17)" },
                new AdvancedRepairTarget { StepNumber = 18, StepName = "Reparo Windows Installer MSI (Step18)" },
                new AdvancedRepairTarget { StepNumber = 19, StepName = "Fix CPU Alta / Core Parking" }
            };

            foreach (var t in targets)
            {
                result.FoundItems.Add(t);
            }

            result.Statistics.ItemsFound = targets.Count;
            result.Statistics.ItemsScanned = targets.Count;
            
            _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Advanced_ScanComplete") ?? "{0} etapas avançadas detectadas e mapeadas.", targets.Count), SmartRepairEventType.OperationFinished);
            // Log and report scan completion
            _logger.LogInfo($"[{ModuleId}] Scan concluído. Etapas encontradas={result.Statistics.ItemsFound}, CorrelationId={_correlationId}");
            progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_Advanced_ScanComplete") ?? $"Mapeado {result.Statistics.ItemsFound} etapas." });
            
            return Task.FromResult(result);
        }

        public Task<ModuleSimulationResult> SimulateAsync(ModuleScanResult scanResult, CancellationToken ct)
        {
            var sim = new ModuleSimulationResult();
            
            sim.EstimatedStatistics.ItemsFound = scanResult.FoundItems.Count;
            sim.RiskLevel = RiskLevel.High;
            
            foreach (var item in scanResult.FoundItems)
            {
                sim.ItemsToProcess.Add(item);
            }

            // Log simulation completion
            _logger.LogInfo($"[{ModuleId}] Simulação concluída. Itens a processar={sim.ItemsToProcess.Count}, RiskLevel={sim.RiskLevel}, CorrelationId={_correlationId}");
            return Task.FromResult(sim);
        }

        public async Task<ModuleExecutionResult> ExecuteAsync(ModuleSimulationResult simResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleExecutionResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Advanced_ExecStarting") ?? "Iniciando motor de Reparação Completa...", SmartRepairEventType.OperationStarted);

            int total = simResult.ItemsToProcess.Count;
            int current = 0;

            foreach (AdvancedRepairTarget target in simResult.ItemsToProcess)
            {
                ct.ThrowIfCancellationRequested();
                current++;

                string statusText = string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Advanced_ExecProgress") ?? "[{0}/{1}] {2}", current, total, target.StepName);
                _eventBus.PublishMessage(_correlationId, ModuleId, statusText, SmartRepairEventType.ProgressChanged);
                
                int percent = (int)((current / (double)total) * 100);
                progress?.Report(new RepairProgress { StepPercent = percent, StatusMessage = statusText });

                bool success = false;

                try
                {
                    // Chamadas delegadas ao serviço original
                    AdvancedRepairStepResult stepResult = null;

                    _logger.LogDebug($"[AdvancedRepairAdapter] Executando step {target.StepNumber}: {target.StepName}");
                    switch (target.StepNumber)
                    {
                        case 0: stepResult = await _advancedRepairService.Step00_CreateRestorePointAsync(ct); break;
                        case 1: stepResult = await _advancedRepairService.Step01_NetworkResetAsync(ct); break;
                        case 2: stepResult = await _advancedRepairService.Step02_DotNetRepairAsync(ct); break;
                        case 3: stepResult = await _advancedRepairService.Step03_VcRedistRepairAsync(ct); break;
                        case 4: stepResult = await _advancedRepairService.Step04_WindowsStoreResetAsync(ct); break;
                        case 5: stepResult = await _advancedRepairService.Step05_BootRepairAsync(ct); break;
                        case 6: stepResult = await _advancedRepairService.Step06_ServicesResetAsync(ct); break;
                        case 7: stepResult = await _advancedRepairService.Step07_RegistryRepairAsync(ct); break;
                        case 8: stepResult = await _advancedRepairService.Step08_CacheCleanAsync(ct); break;
                        case 9: stepResult = await _advancedRepairService.Step09_DefenderRepairAsync(ct); break;
                        case 10: stepResult = await _advancedRepairService.Step10_DriverRepairAsync(ct); break;
                        case 11: stepResult = await _advancedRepairService.Step11_BsodAnalysisAsync(ct); break;
                        case 12: stepResult = await _advancedRepairService.Step12_EventViewerRepairAsync(ct); break;
                        case 13: stepResult = await _advancedRepairService.Step13_PrintSpoolerResetAsync(ct); break;
                        case 14: stepResult = await _advancedRepairService.Step14_DirectXAudioRepairAsync(ct); break;
                        case 15: stepResult = await _advancedRepairService.Step15_UserProfileRepairAsync(ct); break;
                        case 16: stepResult = await _advancedRepairService.Step16_IntegrityCheckAsync(ct, null); break;
                        case 17: stepResult = await _advancedRepairService.Step17_SmartDiagnosticsAsync(ct); break;
                        case 18: stepResult = await _advancedRepairService.Step18_WindowsInstallerRepairAsync(ct); break;
                        case 19: stepResult = await _advancedRepairService.FixHighCpuCoreParkingAsync(ct); break;
                    }

                    if (stepResult != null && stepResult.Success)
                    {
                        success = true;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw; // Aborta
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[{ModuleId}] Erro ao executar etapa {target.StepNumber}: {target.StepName}", ex);
                    _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Advanced_StepFailed") ?? "Falha crítica na etapa {0}: {1}", target.StepNumber, ex.Message), SmartRepairEventType.ProgressChanged);
                }

                if (success)
                    result.FinalStatistics.ItemsRepaired++;
                else
                    result.FinalStatistics.ItemsIgnored++;
            }

            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Advanced_Done") ?? "Motor de Reparação Completa finalizado.", SmartRepairEventType.OperationFinished);
            // Log execution completion
            _logger.LogInfo($"[{ModuleId}] Execução concluída. Repaired={result.FinalStatistics.ItemsRepaired}, Ignored={result.FinalStatistics.ItemsIgnored}, CorrelationId={_correlationId}");
            return result;
        }

        public Task<ModuleRollbackResult> RollbackAsync(ModuleExecutionResult execResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            throw new NotSupportedException(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Advanced_NoRollback") ?? "A Reparação Completa cria o seu próprio Ponto de Restauração (Step00). Faça o rollback pelo sistema do Windows.");
        }
    }
}
