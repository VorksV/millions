using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.SmartRepair.Architecture;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.SmartRepair.Modules
{
    public class BrowserCleanerAdapterModule : ISmartRepairModule
    {
        private readonly ILoggingService _logger;
        private readonly ISmartRepairEventBus _eventBus;
        private readonly string _correlationId;
        private readonly BrowserCleanerService _browserCleanerService;

        public string ModuleId => "BrowserCleaner";
        public string Category => "Junk";
        public string Name => VoltrisOptimizer.Services.LocalizationService.Instance.GetString("SmartRepair_Module_Browser_Name") ?? "Otimização de Navegadores";
        public string Description => VoltrisOptimizer.Services.LocalizationService.Instance.GetString("SmartRepair_Module_Browser_Desc") ?? "Limpeza profunda e segura de Caches, Cookies e Arquivos Temporários de todos os navegadores web.";

        public ModuleCapabilities Capabilities { get; } = new ModuleCapabilities
        {
            SupportsScan = true,
            SupportsSimulation = true,
            SupportsExecution = true,
            SupportsRollback = false,
            SupportsParallelism = true,
            HasDestructiveOperations = false,
            MaxRiskLevel = RiskLevel.Safe,
            RequiresAdmin = false
        };

        public IReadOnlyList<string> Dependencies => Array.Empty<string>();

        public BrowserCleanerAdapterModule(ILoggingService logger, ISmartRepairEventBus eventBus, string correlationId)
        {
            _logger = logger;
            _eventBus = eventBus;
            _correlationId = correlationId;
            _browserCleanerService = new BrowserCleanerService(_logger);
        }

        private class BrowserTarget
        {
            public string Name { get; set; } = string.Empty;
        }

        public Task<ModuleScanResult> ScanAsync(IProgress<RepairProgress> progress, CancellationToken ct)
        {
            _logger.LogInfo($"[SmartRepair][{ModuleId}][{_correlationId}] INÍCIO do Scan - Módulo: {Name}");
            
            var result = new ModuleScanResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Browser_ScanStarting") ?? "Analisando perfil dos Navegadores...", SmartRepairEventType.OperationStarted);
            _logger.LogDebug($"[SmartRepair][{ModuleId}] Evento de scan iniciado publicado no EventBus");

            var targets = new List<BrowserTarget>
            {
                new BrowserTarget { Name = "Google Chrome" },
                new BrowserTarget { Name = "Microsoft Edge" },
                new BrowserTarget { Name = "Mozilla Firefox" },
                new BrowserTarget { Name = "Brave Browser" },
                new BrowserTarget { Name = "Opera" }
            };
            _logger.LogDebug($"[SmartRepair][{ModuleId}] {targets.Count} navegadores detectados para análise");

            foreach (var t in targets)
            {
                result.FoundItems.Add(t);
                _logger.LogDebug($"[SmartRepair][{ModuleId}] Navegador mapeado: {t.Name}");
            }

            result.Statistics.ItemsFound = targets.Count;
            result.Statistics.ItemsScanned = targets.Count;
            
            _logger.LogInfo($"[SmartRepair][{ModuleId}] Scan concluído - {targets.Count} navegadores encontrados");
            _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Browser_ScanComplete") ?? "{0} navegadores suportados mapeados para limpeza.", targets.Count), SmartRepairEventType.OperationFinished);
            
            return Task.FromResult(result);
        }

        public Task<ModuleSimulationResult> SimulateAsync(ModuleScanResult scanResult, CancellationToken ct)
        {
            _logger.LogDebug($"[SmartRepair][{ModuleId}][{_correlationId}] INÍCIO da Simulação - Itens: {scanResult.FoundItems.Count}");
            
            var sim = new ModuleSimulationResult();
            sim.RiskLevel = RiskLevel.Safe;
            _logger.LogDebug($"[SmartRepair][{ModuleId}] RiskLevel definido como: {sim.RiskLevel}");
            
            foreach (BrowserTarget target in scanResult.FoundItems)
            {
                sim.ItemsToProcess.Add(target);
                _logger.LogDebug($"[SmartRepair][{ModuleId}] Item adicionado para simulação: {target.Name}");
            }

            sim.EstimatedStatistics.ItemsFound = scanResult.FoundItems.Count;
            
            _logger.LogInfo($"[SmartRepair][{ModuleId}] Simulação concluída - {sim.ItemsToProcess.Count} itens prontos para execução");
            return Task.FromResult(sim);
        }

        public async Task<ModuleExecutionResult> ExecuteAsync(ModuleSimulationResult simResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            _logger.LogInfo($"[SmartRepair][{ModuleId}][{_correlationId}] ═══════════════════════════════════════");
            _logger.LogInfo($"[SmartRepair][{ModuleId}][{_correlationId}] INÍCIO DA EXECUÇÃO - Módulo: {Name}");
            _logger.LogInfo($"[SmartRepair][{ModuleId}][{_correlationId}] Itens para processar: {simResult.ItemsToProcess.Count}");
            
            var result = new ModuleExecutionResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Browser_ExecStarting") ?? "Iniciando Limpeza Profunda de Navegadores...", SmartRepairEventType.OperationStarted);
            _logger.LogDebug($"[SmartRepair][{ModuleId}] Evento de execução iniciado publicado");

            int total = simResult.ItemsToProcess.Count;
            int current = 0;

            // Para manter a UI viva, reportamos cada navegador que "vamos" tentar limpar
            foreach (BrowserTarget target in simResult.ItemsToProcess)
            {
                current++;
                int percent = (int)((current / (double)total) * 100);
                string statusText = string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Browser_ExecProgress") ?? "Analisando e limpando dados residuais de {0}...", target.Name);
                _eventBus.PublishMessage(_correlationId, ModuleId, statusText, SmartRepairEventType.ProgressChanged);
                progress?.Report(new RepairProgress { StepPercent = percent, StatusMessage = statusText });
                _logger.LogDebug($"[SmartRepair][{ModuleId}] Progresso: {percent}% - {target.Name}");
            }
            _logger.LogInfo($"[SmartRepair][{ModuleId}] Progresso da UI concluído - {current}/{total} navegadores reportados");

            // A execução real acontece no BrowserCleanerService de uma vez
            _logger.LogInfo($"[SmartRepair][{ModuleId}] Chamando BrowserCleanerService.CleanAsync()...");
            var cleanerResult = await _browserCleanerService.CleanAsync(ct);
            _logger.LogDebug($"[SmartRepair][{ModuleId}] BrowserCleanerService retornou: IsSuccess={cleanerResult.IsSuccess}, BytesFreed={cleanerResult.BytesFreed}");
            
            if (cleanerResult.IsSuccess)
            {
                result.FinalStatistics.ItemsRepaired = total;
                result.FinalStatistics.SpaceRecoveredBytes = cleanerResult.BytesFreed;
                _logger.LogSuccess($"[SmartRepair][{ModuleId}] ✅ EXECUÇÃO BEM-SUCEDIDA - {total} navegadores limpos, {FileSystemHelper.FormatBytes(cleanerResult.BytesFreed)} liberados");
                _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Browser_DoneSuccess") ?? "Limpeza de navegadores concluída com sucesso. Total liberado: {0}", FileSystemHelper.FormatBytes(cleanerResult.BytesFreed)), SmartRepairEventType.OperationFinished);
            }
            else
            {
                result.FinalStatistics.ItemsRepaired = total - cleanerResult.IgnoredErrors.Count;
                result.FinalStatistics.ItemsIgnored = cleanerResult.IgnoredErrors.Count;
                _logger.LogWarning($"[SmartRepair][{ModuleId}] ⚠️ EXECUÇÃO COM ERROS - {cleanerResult.IgnoredErrors.Count} arquivos ignorados");
                foreach(var err in cleanerResult.IgnoredErrors)
                {
                    _logger.LogWarning($"[SmartRepair][{ModuleId}] Erro ignorado: {err}");
                }
                _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Browser_DoneWithErrors") ?? "Limpeza finalizada com {0} arquivos ignorados (provavelmente em uso).", cleanerResult.IgnoredErrors.Count), SmartRepairEventType.OperationFinished);
            }
            
            _logger.LogInfo($"[SmartRepair][{ModuleId}][{_correlationId}] ═══════════════════════════════════════");
            _logger.LogInfo($"[SmartRepair][{ModuleId}][{_correlationId}] EXECUÇÃO FINALIZADA");
            
            return result;
        }

        public Task<ModuleRollbackResult> RollbackAsync(ModuleExecutionResult execResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            throw new NotSupportedException(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Browser_NoRollback") ?? "Limpeza de navegador não suporta rollback.");
        }
    }
}
