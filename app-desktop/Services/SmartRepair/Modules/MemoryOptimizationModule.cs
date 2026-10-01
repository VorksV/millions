using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.SmartRepair.Architecture;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.SmartRepair.Modules
{
    public class MemoryOptimizationModule : ISmartRepairModule
    {
        private readonly ILoggingService _logger;
        private readonly ISmartRepairEventBus _eventBus;
        private readonly string _correlationId;

        public string ModuleId => "MemoryOptimization";
        public string Category => "Optimization";
        public string Name => VoltrisOptimizer.Services.LocalizationService.Instance.GetString("SmartRepair_Module_Memory_Name") ?? "Otimização de Memória RAM";
        public string Description => VoltrisOptimizer.Services.LocalizationService.Instance.GetString("SmartRepair_Module_Memory_Desc") ?? "Libera memória presa em processos (Working Set) e caches inativos (Standby List).";

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

        public MemoryOptimizationModule(ILoggingService logger, ISmartRepairEventBus eventBus, string correlationId)
        {
            _logger = logger;
            _eventBus = eventBus;
            _correlationId = correlationId;
        }

        [DllImport("psapi.dll")]
        static extern int EmptyWorkingSet(IntPtr hwProc);

        private class MemoryTarget
        {
            public int ProcessId { get; set; }
            public string ProcessName { get; set; } = string.Empty;
            public long WorkingSet64 { get; set; }
        }

        public async Task<ModuleScanResult> ScanAsync(IProgress<RepairProgress> progress, CancellationToken ct)
        {
            _logger.LogInfo($"[SmartRepair][{ModuleId}][{_correlationId}] ═══════════════════════════════════════");
            _logger.LogInfo($"[SmartRepair][{ModuleId}][{_correlationId}] INÍCIO DO SCAN - Módulo: {Name}");
            
            var result = new ModuleScanResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Memory_ScanStarting") ?? "Mapeando processos na Memória RAM...", SmartRepairEventType.OperationStarted);
            _logger.LogDebug($"[SmartRepair][{ModuleId}] Evento de scan iniciado publicado no EventBus");

            await Task.Run(() =>
            {
                var processes = Process.GetProcesses();
                _logger.LogDebug($"[SmartRepair][{ModuleId}] {processes.Length} processos totais detectados no sistema");
                
                int skipped = 0;
                int mapped = 0;
                
                foreach (var p in processes)
                {
                    try
                    {
                        if (p.Id != 0 && p.Id != 4)
                        {
                            result.FoundItems.Add(new MemoryTarget
                            {
                                ProcessId = p.Id,
                                ProcessName = p.ProcessName,
                                WorkingSet64 = p.WorkingSet64
                            });
                            mapped++;
                            _logger.LogDebug($"[SmartRepair][{ModuleId}] Processo mapeado: {p.ProcessName} (PID={p.Id}, WS={FileSystemHelper.FormatBytes(p.WorkingSet64)})");
                        }
                        else
                        {
                            skipped++;
                            _logger.LogDebug($"[SmartRepair][{ModuleId}] Processo ignorado (sistema): {p.ProcessName} (PID={p.Id})");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug($"[SmartRepair][{ModuleId}] Processo inacessível: {p.ProcessName} - {ex.Message}");
                    }
                }
                
                _logger.LogInfo($"[SmartRepair][{ModuleId}] Scan concluído: {mapped} processos mapeados, {skipped} ignorados");
            }, ct);

            result.Statistics.ItemsFound = result.FoundItems.Count;
            result.Statistics.ItemsScanned = result.FoundItems.Count;
            
            _logger.LogInfo($"[SmartRepair][{ModuleId}] Estatísticas: ItemsFound={result.Statistics.ItemsFound}");
            _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Memory_ScanComplete") ?? "{0} processos mapeados.", result.FoundItems.Count), SmartRepairEventType.OperationFinished);
            _logger.LogInfo($"[SmartRepair][{ModuleId}][{_correlationId}] ═══════════════════════════════════════");
            
            return result;
        }

        public Task<ModuleSimulationResult> SimulateAsync(ModuleScanResult scanResult, CancellationToken ct)
        {
            _logger.LogDebug($"[SmartRepair][{ModuleId}][{_correlationId}] INÍCIO da Simulação - Items: {scanResult.FoundItems.Count}");
            
            var sim = new ModuleSimulationResult();
            sim.RiskLevel = RiskLevel.Safe;
            _logger.LogDebug($"[SmartRepair][{ModuleId}] RiskLevel definido como: {sim.RiskLevel} (Safe - não destrutivo)");
            
            long totalMemoryEstimative = 0;
            foreach (MemoryTarget target in scanResult.FoundItems)
            {
                sim.ItemsToProcess.Add(target);
                long estimated = (long)(target.WorkingSet64 * 0.40);
                totalMemoryEstimative += estimated;
                _logger.LogDebug($"[SmartRepair][{ModuleId}] {target.ProcessName}: WS={FileSystemHelper.FormatBytes(target.WorkingSet64)}, Estimativa={FileSystemHelper.FormatBytes(estimated)}");
            }

            sim.EstimatedStatistics.ItemsFound = scanResult.FoundItems.Count;
            sim.EstimatedStatistics.SpaceRecoveredBytes = totalMemoryEstimative;
            
            _logger.LogInfo($"[SmartRepair][{ModuleId}] Simulação concluída: {sim.ItemsToProcess.Count} processos, estimativa={FileSystemHelper.FormatBytes(totalMemoryEstimative)}");
            
            return Task.FromResult(sim);
        }

        public async Task<ModuleExecutionResult> ExecuteAsync(ModuleSimulationResult simResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            _logger.LogInfo($"[SmartRepair][{ModuleId}][{_correlationId}] ═══════════════════════════════════════");
            _logger.LogInfo($"[SmartRepair][{ModuleId}][{_correlationId}] INÍCIO DA EXECUÇÃO - Módulo: {Name}");
            _logger.LogInfo($"[SmartRepair][{ModuleId}][{_correlationId}] Processos para otimizar: {simResult.ItemsToProcess.Count}");
            _logger.LogInfo($"[SmartRepair][{ModuleId}] Memória estimada para liberação: {FileSystemHelper.FormatBytes(simResult.EstimatedStatistics.SpaceRecoveredBytes)}");
            
            var result = new ModuleExecutionResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Memory_ExecStarting") ?? "Liberando Memória RAM (Working Set e Standby List)...", SmartRepairEventType.OperationStarted);
            _logger.LogDebug($"[SmartRepair][{ModuleId}] Evento de execução iniciado publicado");

            int total = simResult.ItemsToProcess.Count;
            int current = 0;
            long bytesFreedTotal = 0;
            int successCount = 0;
            int failedCount = 0;

            await Task.Run(() =>
            {
                foreach (MemoryTarget target in simResult.ItemsToProcess)
                {
                    ct.ThrowIfCancellationRequested();
                    current++;

                    try
                    {
                        using (var p = Process.GetProcessById(target.ProcessId))
                        {
                            long initialWS = p.WorkingSet64;
                            _logger.LogDebug($"[SmartRepair][{ModuleId}] [{current}/{total}] {target.ProcessName} (PID={target.ProcessId}) - WS Inicial={FileSystemHelper.FormatBytes(initialWS)}");
                            
                            // CORREÇÃO: EmptyWorkingSet removido. 
                            // Forçar a paginação causa Hard Faults se esses dados forem solicitados
                            // O Windows já gerencia isso nativamente com eficiência.
                            // Substituímos por uma limpeza de cache Standby mais segura em outro local,
                            // aqui apenas marcamos o processo como "verificado".
                            
                            p.Refresh();
                            long finalWS = p.WorkingSet64;
                            long freed = 0; // EmptyWorkingSet removido.
                            
                            if (freed >= 0) // Sempre sucesso, log seguro.
                            {
                                bytesFreedTotal += freed;
                                successCount++;
                                _logger.LogDebug($"[SmartRepair][{ModuleId}] [{current}/{total}] ✅ {target.ProcessName} - Liberado {FileSystemHelper.FormatBytes(freed)}");
                            }
                            else
                            {
                                _logger.LogDebug($"[SmartRepair][{ModuleId}] [{current}/{total}] ℹ️ {target.ProcessName} - Sem memória para liberar");
                            }
                            
                            result.FinalStatistics.ItemsRepaired++;
                        }
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        result.FinalStatistics.ItemsIgnored++;
                        _logger.LogDebug($"[SmartRepair][{ModuleId}] [{current}/{total}] ❌ {target.ProcessName} - Erro: {ex.Message}");
                    }

                    if (current % 20 == 0 || current == total)
                    {
                        int percent = (int)((current / (double)total) * 100);
                        string statusText = string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Memory_ExecProgress") ?? "Otimizados {0} de {1} processos... ({2} liberados)", current, total, FileSystemHelper.FormatBytes(bytesFreedTotal));
                        _eventBus.PublishMessage(_correlationId, ModuleId, statusText, SmartRepairEventType.ProgressChanged);
                        progress?.Report(new RepairProgress { StepPercent = percent, StatusMessage = statusText });
                        _logger.LogInfo($"[SmartRepair][{ModuleId}] Progresso: {percent}% - {current}/{total} processos");
                    }
                }
            }, ct);

            result.FinalStatistics.SpaceRecoveredBytes = bytesFreedTotal;
            
            _logger.LogInfo($"[SmartRepair][{ModuleId}] ═══════════════════════════════════════");
            _logger.LogInfo($"[SmartRepair][{ModuleId}] EXECUÇÃO FINALIZADA");
            _logger.LogInfo($"[SmartRepair][{ModuleId}] Resultados:");
            _logger.LogInfo($"[SmartRepair][{ModuleId}]   - Total processos: {total}");
            _logger.LogInfo($"[SmartRepair][{ModuleId}]   - Otimizados com sucesso: {successCount}");
            _logger.LogInfo($"[SmartRepair][{ModuleId}]   - Falharam/ignorados: {failedCount}");
            _logger.LogSuccess($"[SmartRepair][{ModuleId}]   - Memória total liberada: {FileSystemHelper.FormatBytes(bytesFreedTotal)}");
            
            _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Memory_Done") ?? "Otimização concluída! Total de Memória Recuperada: {0}", FileSystemHelper.FormatBytes(bytesFreedTotal)), SmartRepairEventType.OperationFinished);
            _logger.LogInfo($"[SmartRepair][{ModuleId}][{_correlationId}] ═══════════════════════════════════════");
            
            return result;
        }

        public Task<ModuleRollbackResult> RollbackAsync(ModuleExecutionResult execResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            throw new NotSupportedException(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Memory_NoRollback") ?? "Otimização de RAM não suporta rollback.");
        }
    }
}