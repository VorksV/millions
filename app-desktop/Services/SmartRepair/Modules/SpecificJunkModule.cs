using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.SmartRepair.Architecture;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.SmartRepair.Modules
{
    public class SpecificJunkModule : ISmartRepairModule
    {
        private readonly ILoggingService _logger;
        private readonly ISmartRepairEventBus _eventBus;
        private readonly string _correlationId;

        public string ModuleId => "SpecificJunk";
        public string Category => "Junk";
        public string Name => VoltrisOptimizer.Services.LocalizationService.Instance.GetString("SmartRepair_Module_SpecificJunk_Name") ?? "Lixo Específico (Cache GPU e Visual)";
        public string Description => VoltrisOptimizer.Services.LocalizationService.Instance.GetString("SmartRepair_Module_SpecificJunk_Desc") ?? "Limpa Shaders de Placas de Vídeo (NVIDIA/AMD), Cache de Fontes e Miniaturas do Windows (Thumbnails).";

        public ModuleCapabilities Capabilities { get; } = new ModuleCapabilities
        {
            SupportsScan = true,
            SupportsSimulation = true,
            SupportsExecution = true,
            SupportsRollback = false,
            SupportsParallelism = true,
            HasDestructiveOperations = false,
            MaxRiskLevel = RiskLevel.Safe,
            RequiresAdmin = true
        };

        public IReadOnlyList<string> Dependencies => Array.Empty<string>();

        public SpecificJunkModule(ILoggingService logger, ISmartRepairEventBus eventBus, string correlationId)
        {
            _logger = logger;
            _eventBus = eventBus;
            _correlationId = correlationId;
        }

        private class JunkTarget
        {
            public string Category { get; set; } = string.Empty;
            public string Path { get; set; } = string.Empty;
            public string Pattern { get; set; } = "*";
        }

        public async Task<ModuleScanResult> ScanAsync(IProgress<RepairProgress> progress, CancellationToken ct)
        {
            _logger.LogInfo($"[SmartRepair][{ModuleId}][{_correlationId}] ═══════════════════════════════════════");
            _logger.LogInfo($"[SmartRepair][{ModuleId}][{_correlationId}] INÍCIO DO SCAN - Módulo: {Name}");
            
            var result = new ModuleScanResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_SpecificJunk_ScanStarting") ?? "Mapeando caches visuais e Shaders de GPU...", SmartRepairEventType.OperationStarted);
            _logger.LogDebug($"[SmartRepair][{ModuleId}] Evento de scan iniciado publicado no EventBus");

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string winDir = Environment.GetEnvironmentVariable("WINDIR") ?? "C:\\Windows";
            _logger.LogDebug($"[SmartRepair][{ModuleId}] LocalAppData: {localAppData}");
            _logger.LogDebug($"[SmartRepair][{ModuleId}] WindDir: {winDir}");

            var targets = new List<JunkTarget>
            {
                new JunkTarget { Category = "NVIDIA DXCache", Path = Path.Combine(localAppData, "NVIDIA", "DXCache") },
                new JunkTarget { Category = "NVIDIA GLCache", Path = Path.Combine(localAppData, "NVIDIA", "GLCache") },
                new JunkTarget { Category = "AMD DxCache", Path = Path.Combine(localAppData, "AMD", "DxCache") },
                new JunkTarget { Category = "Thumbnails", Path = Path.Combine(localAppData, "Microsoft", "Windows", "Explorer"), Pattern = "thumbcache_*.db" },
                new JunkTarget { Category = "Icon Cache", Path = Path.Combine(localAppData, "Microsoft", "Windows", "Explorer"), Pattern = "iconcache_*.db" },
                new JunkTarget { Category = "Font Cache", Path = Path.Combine(winDir, "ServiceProfiles", "LocalService", "AppData", "Local", "FontCache") }
            };
            _logger.LogDebug($"[SmartRepair][{ModuleId}] {targets.Count} categorias de cache para verificar");

            await Task.Run(() =>
            {
                int foundCount = 0;
                int skippedCount = 0;
                
                foreach (var t in targets)
                {
                    ct.ThrowIfCancellationRequested();
                    
                    if (!Directory.Exists(t.Path))
                    {
                        skippedCount++;
                        _logger.LogDebug($"[SmartRepair][{ModuleId}] Categoria ignorada (pasta não existe): {t.Category}");
                        continue;
                    }
                    
                    try
                    {
                        var files = Directory.GetFiles(t.Path, t.Pattern, SearchOption.AllDirectories);
                        if (files.Length > 0)
                        {
                            result.FoundItems.Add(t);
                            foundCount++;
                            _logger.LogDebug($"[SmartRepair][{ModuleId}] ✅ {t.Category}: {files.Length} arquivos encontrados em {t.Path}");
                            _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_SpecificJunk_Found") ?? "Encontrado lixo em {0} ({1} arquivos).", t.Category, files.Length), SmartRepairEventType.ProgressChanged);
                        }
                        else
                        {
                            _logger.LogDebug($"[SmartRepair][{ModuleId}] ℹ️ {t.Category}: Pasta vazia ou sem arquivos matching");
                        }
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        _logger.LogDebug($"[SmartRepair][{ModuleId}] ⚠️ {t.Category}: Sem permissão - {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug($"[SmartRepair][{ModuleId}] ⚠️ {t.Category}: Erro ao escanear - {ex.Message}");
                    }
                }
                
                _logger.LogInfo($"[SmartRepair][{ModuleId}] Scan concluído: {foundCount} categorias com lixo, {skippedCount} ignoradas");
            }, ct);

            result.Statistics.ItemsFound = result.FoundItems.Count;
            result.Statistics.ItemsScanned = targets.Count;
            
            _logger.LogInfo($"[SmartRepair][{ModuleId}] Estatísticas: ItemsFound={result.Statistics.ItemsFound}, ItemsScanned={result.Statistics.ItemsScanned}");
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_SpecificJunk_ScanComplete") ?? "Mapeamento de Caches Específicos finalizado.", SmartRepairEventType.OperationFinished);
            _logger.LogInfo($"[SmartRepair][{ModuleId}][{_correlationId}] ═══════════════════════════════════════");
            
            return result;
        }

        public Task<ModuleSimulationResult> SimulateAsync(ModuleScanResult scanResult, CancellationToken ct)
        {
            _logger.LogDebug($"[SmartRepair][{ModuleId}][{_correlationId}] INÍCIO da Simulação - Items: {scanResult.FoundItems.Count}");
            
            var sim = new ModuleSimulationResult();
            sim.RiskLevel = RiskLevel.Safe;
            _logger.LogDebug($"[SmartRepair][{ModuleId}] RiskLevel definido como: {sim.RiskLevel} (Safe - não destrutivo)");
            
            long estimative = 0;
            foreach (JunkTarget target in scanResult.FoundItems)
            {
                sim.ItemsToProcess.Add(target);
                estimative += 50 * 1024 * 1024;
                _logger.LogDebug($"[SmartRepair][{ModuleId}] {target.Category}: +50MB (estimativa)");
            }

            sim.EstimatedStatistics.ItemsFound = scanResult.FoundItems.Count;
            
            _logger.LogInfo($"[SmartRepair][{ModuleId}] Simulação concluída: {sim.ItemsToProcess.Count} categorias, estimativa={estimative / (1024 * 1024)}MB");
            
            return Task.FromResult(sim);
        }

        public async Task<ModuleExecutionResult> ExecuteAsync(ModuleSimulationResult simResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            _logger.LogInfo($"[SmartRepair][{ModuleId}][{_correlationId}] ═══════════════════════════════════════");
            _logger.LogInfo($"[SmartRepair][{ModuleId}][{_correlationId}] INÍCIO DA EXECUÇÃO - Módulo: {Name}");
            _logger.LogInfo($"[SmartRepair][{ModuleId}] Categorias para limpar: {simResult.ItemsToProcess.Count}");
            
            var result = new ModuleExecutionResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_SpecificJunk_ExecStarting") ?? "Iniciando limpeza de Lixo Específico...", SmartRepairEventType.OperationStarted);
            _logger.LogDebug($"[SmartRepair][{ModuleId}] Evento de execução iniciado publicado");

            int total = simResult.ItemsToProcess.Count;
            int current = 0;
            long totalFreed = 0;
            int successCount = 0;
            int failedCount = 0;

            await Task.Run(() =>
            {
                foreach (JunkTarget target in simResult.ItemsToProcess)
                {
                    ct.ThrowIfCancellationRequested();
                    current++;
                    
                    string statusText = string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_SpecificJunk_ExecProgress") ?? "Limpando {0}...", target.Category);
                    
                    _logger.LogDebug($"[SmartRepair][{ModuleId}] [{current}/{total}] Limpando: {target.Category} ({target.Path})");
                    
                    _eventBus.PublishMessage(_correlationId, ModuleId, statusText, SmartRepairEventType.ProgressChanged);
                    progress?.Report(new RepairProgress { StepPercent = (int)((current / (double)total) * 100), StatusMessage = statusText });

                    try
                    {
                        var cleanResult = FileSystemHelper.CleanDirectory(target.Path, target.Pattern);
                        totalFreed += cleanResult.bytesFreed;

                        if (cleanResult.filesDeleted > 0)
                        {
                            successCount++;
                            result.FinalStatistics.ItemsRepaired++;
                            _logger.LogDebug($"[SmartRepair][{ModuleId}] [{current}/{total}] ✅ {target.Category}: {cleanResult.filesDeleted} arquivos, {FileSystemHelper.FormatBytes(cleanResult.bytesFreed)} liberados");
                        }
                        else
                        {
                            result.FinalStatistics.ItemsIgnored++;
                            _logger.LogDebug($"[SmartRepair][{ModuleId}] [{current}/{total}] ℹ️ {target.Category}: Nenhum arquivo removido");
                        }
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        result.FinalStatistics.ItemsIgnored++;
                        _logger.LogWarning($"[SmartRepair][{ModuleId}] [{current}/{total}] ❌ {target.Category}: Erro - {ex.Message}");
                    }
                }
                
                _logger.LogInfo($"[SmartRepair][{ModuleId}] Execução concluída: {current}/{total} categorias processadas");
            }, ct);

            _logger.LogInfo($"[SmartRepair][{ModuleId}] ═══════════════════════════════════════");
            _logger.LogInfo($"[SmartRepair][{ModuleId}] EXECUÇÃO FINALIZADA");
            _logger.LogInfo($"[SmartRepair][{ModuleId}] Resultados:");
            _logger.LogInfo($"[SmartRepair][{ModuleId}]   - Total categorias: {total}");
            _logger.LogInfo($"[SmartRepair][{ModuleId}]   - Limpas com sucesso: {successCount}");
            _logger.LogInfo($"[SmartRepair][{ModuleId}]   - Falharam/ignorado: {failedCount}");
            _logger.LogSuccess($"[SmartRepair][{ModuleId}]   - Espaço total recuperado: {FileSystemHelper.FormatBytes(totalFreed)}");
            
            _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_SpecificJunk_Done") ?? "Limpeza Específica concluída! Total recuperado: {0}", FileSystemHelper.FormatBytes(totalFreed)), SmartRepairEventType.OperationFinished);
            _logger.LogInfo($"[SmartRepair][{ModuleId}][{_correlationId}] ═══════════════════════════════════════");
            
            return result;
        }

        public Task<ModuleRollbackResult> RollbackAsync(ModuleExecutionResult execResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            throw new NotSupportedException(Services.LocalizationService.Instance.GetString("SmartRepair_Module_SpecificJunk_NoRollback") ?? "Limpeza não suporta rollback.");
        }
    }
}
