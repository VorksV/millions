using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.SmartRepair.Architecture;

namespace VoltrisOptimizer.Services.SmartRepair.Modules
{
    public class SystemRestoreModule : ISmartRepairModule
    {
        private readonly ILoggingService _logger;
        private readonly ISmartRepairEventBus _eventBus;
        private readonly string _correlationId;
        private readonly SystemToolsService _systemToolsService;
        private static DateTime _lastRestorePointCreation = DateTime.MinValue;
        private static readonly TimeSpan RestorePointCooldown = TimeSpan.FromHours(1);

        public string ModuleId => "SystemRestore";
        public string Category => "System";
        public string Name => VoltrisOptimizer.Services.LocalizationService.Instance.GetString("SmartRepair_Module_Restore_Name") ?? "Ponto de Restauração";
        public string Description => VoltrisOptimizer.Services.LocalizationService.Instance.GetString("SmartRepair_Module_Restore_Desc") ?? "Cria um ponto de segurança antes de prosseguir com reparos.";

        public ModuleCapabilities Capabilities { get; } = new ModuleCapabilities
        {
            SupportsScan = true,
            SupportsSimulation = true,
            SupportsExecution = true,
            SupportsRollback = false,
            SupportsParallelism = false,
            HasDestructiveOperations = false,
            MaxRiskLevel = RiskLevel.Safe,
            RequiresAdmin = true
        };

        public IReadOnlyList<string> Dependencies => Array.Empty<string>();

        public SystemRestoreModule(ILoggingService logger, ISmartRepairEventBus eventBus, string correlationId)
        {
            _logger = logger;
            _eventBus = eventBus;
            _correlationId = correlationId;
            _systemToolsService = new SystemToolsService(logger);
        }

        public Task<ModuleScanResult> ScanAsync(IProgress<RepairProgress> progress, CancellationToken ct)
        {
            _logger.LogInfo($"[SmartRepair][{ModuleId}][{_correlationId}] ═══════════════════════════════════════");
            _logger.LogInfo($"[SmartRepair][{ModuleId}][{_correlationId}] INÍCIO DO SCAN - Módulo: {Name}");
            
            var result = new ModuleScanResult();
            
            _logger.LogDebug($"[SmartRepair][{ModuleId}] Verificando necessidade de criar ponto de restauração...");
            
            // Simula que encontramos a "necessidade" de criar o ponto de restauração
            result.FoundItems.Add(new { Name = "Criar Ponto de Restauração do Sistema" });
            result.Statistics.ItemsFound = 1;
            result.Statistics.ItemsScanned = 1;
            
            _logger.LogInfo($"[SmartRepair][{ModuleId}] Scan concluído: 1 item encontrado (criação de restore point)");
            _logger.LogInfo($"[SmartRepair][{ModuleId}][{_correlationId}] ═══════════════════════════════════════");
            
            return Task.FromResult(result);
        }

        public async Task<ModuleExecutionResult> ExecuteAsync(ModuleSimulationResult simResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            _logger.LogInfo($"[SmartRepair][{ModuleId}][{_correlationId}] ═══════════════════════════════════════");
            _logger.LogInfo($"[SmartRepair][{ModuleId}][{_correlationId}] INÍCIO DA EXECUÇÃO - Módulo: {Name}");
            
            var result = new ModuleExecutionResult();
            
            _eventBus.PublishMessage(_correlationId, ModuleId, VoltrisOptimizer.Services.LocalizationService.Instance.GetString("SmartRepair_Restore_Creating") ?? "Criando ponto de restauração de segurança...", SmartRepairEventType.OperationStarted);
            _logger.LogDebug($"[SmartRepair][{ModuleId}] Evento de execução iniciado publicado");

            try
            {
                var elapsedSinceLast = DateTime.UtcNow - _lastRestorePointCreation;
                if (elapsedSinceLast < RestorePointCooldown)
                {
                    _logger.LogInfo($"[SmartRepair][{ModuleId}] Rate limit: último ponto criado há {elapsedSinceLast.TotalMinutes:F0}min (mínimo 60min). Pulando criação.");
                    _eventBus.PublishMessage(_correlationId, ModuleId, $"Ponto de restauração já criado há {elapsedSinceLast.TotalMinutes:F0}min. Pulando (limite de 1h).", SmartRepairEventType.OperationFinished);
                    result.Success = true;
                    _logger.LogInfo($"[SmartRepair][{ModuleId}][{_correlationId}] ═══════════════════════════════════════");
                    return result;
                }
                
                var rpName = $"Voltris_SmartRepair_{DateTime.Now:yyyyMMdd_HHmmss}";
                _logger.LogInfo($"[SmartRepair][{ModuleId}] Criando ponto de restauração: {rpName}");
                
                var isCreated = await _systemToolsService.CreateSystemRestorePointAsync(rpName, silent: true);
                
                if (isCreated)
                {
                    _lastRestorePointCreation = DateTime.UtcNow;
                    _logger.LogSuccess($"[SmartRepair][{ModuleId}] ✅ Ponto de restauração criado com sucesso!");
                    _eventBus.PublishMessage(_correlationId, ModuleId, VoltrisOptimizer.Services.LocalizationService.Instance.GetString("SmartRepair_Restore_Success") ?? "Ponto de restauração criado com sucesso.", SmartRepairEventType.OperationFinished);
                    result.Success = true;
                    result.FinalStatistics.ItemsRepaired = 1;
                }
                else
                {
                    _logger.LogWarning($"[SmartRepair][{ModuleId}] ⚠️ Falha ao criar ou cancelado pelo usuário");
                    _eventBus.PublishMessage(_correlationId, ModuleId, (VoltrisOptimizer.Services.LocalizationService.Instance.GetString("SmartRepair_Restore_Warning") ?? "Aviso: ") + "Falha ou cancelado pelo usuário.", SmartRepairEventType.WarningRaised);
                    result.Success = true; 
                    result.FinalStatistics.WarningCount = 1;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[SmartRepair][{ModuleId}] Erro crítico ao criar ponto de restauração", ex);
                _eventBus.PublishMessage(_correlationId, ModuleId, VoltrisOptimizer.Services.LocalizationService.Instance.GetString("SmartRepair_Restore_Error") ?? "Erro crítico ao criar ponto de restauração.", SmartRepairEventType.ErrorRaised);
                result.Success = true;
                result.FinalStatistics.ErrorCount = 1;
            }

            _logger.LogInfo($"[SmartRepair][{ModuleId}] ═══════════════════════════════════════");
            _logger.LogInfo($"[SmartRepair][{ModuleId}] EXECUÇÃO FINALIZADA");
            _logger.LogInfo($"[SmartRepair][{ModuleId}] Resultado: Success={result.Success}, ItemsRepaired={result.FinalStatistics.ItemsRepaired}, Warnings={result.FinalStatistics.WarningCount}, Errors={result.FinalStatistics.ErrorCount}");
            _logger.LogInfo($"[SmartRepair][{ModuleId}][{_correlationId}] ═══════════════════════════════════════");

            return result;
        }

        public Task<ModuleSimulationResult> SimulateAsync(ModuleScanResult scanResult, CancellationToken ct)
        {
            _logger.LogDebug($"[SmartRepair][{ModuleId}][{_correlationId}] INÍCIO da Simulação");
            
            var result = new ModuleSimulationResult { Success = true };
            result.RiskLevel = RiskLevel.Safe;
            
            _logger.LogDebug($"[SmartRepair][{ModuleId}] RiskLevel definido como: {result.RiskLevel} (Safe)");
            _logger.LogInfo($"[SmartRepair][{ModuleId}] Simulação concluída: ponto de restauração será criado");
            
            return Task.FromResult(result);
        }

        public Task<ModuleRollbackResult> RollbackAsync(ModuleExecutionResult execResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            throw new NotSupportedException(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Restore_NoRollback") ?? "Ponto de restauração não suporta rollback interno.");
        }
    }
}
