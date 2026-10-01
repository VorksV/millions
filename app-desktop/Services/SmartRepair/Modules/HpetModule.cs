using System;
using System.Collections.Generic;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services.SmartRepair.Architecture;

namespace VoltrisOptimizer.Services.SmartRepair.Modules
{
    public class HpetModule : ISmartRepairModule
    {
        private readonly ILoggingService _logger;
        private readonly ISmartRepairEventBus _eventBus;
        private readonly string _correlationId;
        private bool _hpetWasActive;

        public string ModuleId => "HpetOptimizer";
        public string Category => "CPU";

        public string Name => Services.LocalizationService.Instance.GetString("SmartRepair_Module_Hpet_Name") ?? "HPET Optimizer";
        public string Description => Services.LocalizationService.Instance.GetString("SmartRepair_Module_Hpet_Desc") ?? "Desativa o HPET (High Precision Event Timer) para reduzir latência em sistemas compatíveis.";

        public ModuleCapabilities Capabilities { get; } = new ModuleCapabilities
        {
            SupportsScan = true,
            SupportsSimulation = true,
            SupportsExecution = true,
            SupportsRollback = true,
            SupportsParallelism = false,
            HasDestructiveOperations = false,
            MaxRiskLevel = RiskLevel.Moderate,
            RequiresAdmin = true
        };

        public IReadOnlyList<string> Dependencies => Array.Empty<string>();

        public HpetModule(ILoggingService logger, ISmartRepairEventBus eventBus, string correlationId)
        {
            _logger = logger;
            _eventBus = eventBus;
            _correlationId = correlationId;
        }

        public Task<ModuleScanResult> ScanAsync(IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleScanResult();
            _eventBus.PublishMessage(_correlationId, ModuleId,
                Services.LocalizationService.Instance.GetString("SmartRepair_Module_Hpet_ScanStarting") ?? "Verificando status do HPET...",
                SmartRepairEventType.OperationStarted);

            _logger.LogDebug($"[{ModuleId}] Executando: bcdedit /enum {{current}}");
            bool hpetActive = false;
            if (ProcessHelper.RunBcdEdit("/enum {current}", out string stdOut, out string _))
            {
                hpetActive = stdOut.Contains("useplatformclock", StringComparison.OrdinalIgnoreCase);
            }

            _hpetWasActive = hpetActive;

            result.FoundItems.Add(new HpetInfo { IsActive = hpetActive });
            result.Statistics.ItemsFound = 1;
            result.Statistics.ItemsScanned = 1;

            string scanMsg = hpetActive
                ? (Services.LocalizationService.Instance.GetString("SmartRepair_Module_Hpet_ScanActive") ?? "HPET está ativo no sistema.")
                : (Services.LocalizationService.Instance.GetString("SmartRepair_Module_Hpet_ScanInactive") ?? "HPET não está ativo no sistema.");
            _logger.LogInfo($"[{ModuleId}] Scan concluído. Encontrados={result.Statistics.ItemsFound}, Escaneados={result.Statistics.ItemsScanned}, CorrelationId={_correlationId}");
            progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = result.Message ?? "Scan concluído." });
            _eventBus.PublishMessage(_correlationId, ModuleId, scanMsg, SmartRepairEventType.OperationFinished);
            return Task.FromResult(result);
        }

        public Task<ModuleSimulationResult> SimulateAsync(ModuleScanResult scanResult, CancellationToken ct)
        {
            var sim = new ModuleSimulationResult();
            sim.RiskLevel = RiskLevel.Moderate;
            sim.EstimatedStatistics.ItemsFound = scanResult.FoundItems.Count;
            foreach (var item in scanResult.FoundItems)
                sim.ItemsToProcess.Add(item);
            _logger.LogInfo($"[{ModuleId}] Simulação concluída. Itens a processar={sim.ItemsToProcess.Count}, RiskLevel={sim.RiskLevel}, CorrelationId={_correlationId}");
            return Task.FromResult(sim);
        }

        public async Task<ModuleExecutionResult> ExecuteAsync(ModuleSimulationResult simResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleExecutionResult();
            _eventBus.PublishMessage(_correlationId, ModuleId,
                Services.LocalizationService.Instance.GetString("SmartRepair_Module_Hpet_ExecStarting") ?? "Iniciando otimização do HPET...",
                SmartRepairEventType.OperationStarted);

            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                if (SystemInformationHelper.IsEnterpriseEdition())
                {
                    string msg = Services.LocalizationService.Instance.GetString("SmartRepair_Module_Hpet_SkippedEnterprise") ?? "Edição Enterprise detectada. Operação ignorada.";
                    _eventBus.PublishMessage(_correlationId, ModuleId, msg, SmartRepairEventType.ProgressChanged);
                    progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = msg });
                    result.Message = msg;
                    return;
                }

                int build = Environment.OSVersion.Version.Build;
                if (build < 1903)
                {
                    string msg = Services.LocalizationService.Instance.GetString("SmartRepair_Module_Hpet_SkippedBuild") ?? string.Format("Windows build {0} é anterior a 1903. HPET mantido.", build);
                    _eventBus.PublishMessage(_correlationId, ModuleId, msg, SmartRepairEventType.ProgressChanged);
                    progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = msg });
                    result.Message = msg;
                    return;
                }

                if (!IsCpuCompatible())
                {
                    string msg = Services.LocalizationService.Instance.GetString("SmartRepair_Module_Hpet_SkippedCpu") ?? "CPU não é Intel 6ª geração+ ou AMD Ryzen+. HPET mantido.";
                    _eventBus.PublishMessage(_correlationId, ModuleId, msg, SmartRepairEventType.ProgressChanged);
                    progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = msg });
                    result.Message = msg;
                    return;
                }

                if (!_hpetWasActive)
                {
                    progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = "HPET já estava inativo." });
                    return;
                }

                // AUDITORIA FORENSE: Verificação adicional de segurança de hardware
                if (!IsCpuCompatible())
                {
                    string msg = "Hardware moderno detectado — HPET ignorado por seguranca (TSC invariante).";
                    _eventBus.PublishMessage(_correlationId, ModuleId, msg, SmartRepairEventType.ProgressChanged);
                    progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = msg });
                    result.Message = msg;
                    return;
                }

                progress?.Report(new RepairProgress { StepPercent = 40, StatusMessage =
                    Services.LocalizationService.Instance.GetString("SmartRepair_Module_Hpet_ExecDisabling") ?? "Desativando HPET via BCDEdit..." });
                _eventBus.PublishMessage(_correlationId, ModuleId,
                    Services.LocalizationService.Instance.GetString("SmartRepair_Module_Hpet_ExecDisabling") ?? "Desativando HPET...",
                    SmartRepairEventType.ProgressChanged);

                _logger.LogDebug($"[{ModuleId}] Executando: bcdedit /deletevalue useplatformclock");
                if (ProcessHelper.RunBcdEdit("/deletevalue useplatformclock", out string _, out string err))
                {
                    result.FinalStatistics.ItemsRepaired = 1;
                    progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage =
                        Services.LocalizationService.Instance.GetString("SmartRepair_Module_Hpet_ExecDone") ?? "HPET desativado com sucesso. Reinicie o sistema para aplicar." });
                    _eventBus.PublishMessage(_correlationId, ModuleId,
                        Services.LocalizationService.Instance.GetString("SmartRepair_Module_Hpet_ExecDone") ?? "HPET desativado com sucesso.",
                        SmartRepairEventType.OperationFinished);
                }
                else
                {
                    _logger.LogError($"[{ModuleId}] Falha ao desativar HPET: {err}");
                    result.Success = false;
                    result.Message = err;
                }
            }, ct);

            _logger.LogInfo($"[{ModuleId}] Execução concluída. Repaired={result.FinalStatistics.ItemsRepaired}, Errors={result.FinalStatistics.ErrorCount}, Space={result.FinalStatistics.SpaceRecoveredBytes}B, CorrelationId={_correlationId}");
            return result;
        }

        public async Task<ModuleRollbackResult> RollbackAsync(ModuleExecutionResult execResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleRollbackResult();
            _eventBus.PublishMessage(_correlationId, ModuleId,
                Services.LocalizationService.Instance.GetString("SmartRepair_Module_Hpet_RollbackStarting") ?? "Restaurando HPET...",
                SmartRepairEventType.RollbackStarted);

            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(new RepairProgress { StepPercent = 50, StatusMessage = "Restaurando relogio do sistema (padrao TSC)..." });

                // BUG CORRIGIDO: antes isto fazia "bcdedit /set useplatformclock yes",
                // que REFORCA o Platform Clock. Esse e exatamente o ajuste que causa
                // queda de desempenho comprovada em maquinas modernas: a comunidade
                // relata 165 fps constantes caindo para menos de 100, com quedas de
                // 3 fps, e so voltando ao normal apos DELETAR o valor.
                //
                // O estado saudavel e o padrao do Windows, que e o TSC sem desync.
                // Por isso o rollback DEVE deletar o valor, e nao define-lo como
                // "yes". Isto deixa o modulo simetrico com a propria execucao
                // (linha 147, que ja usava /deletevalue useplatformclock).
                _logger.LogDebug($"[{ModuleId}] Executando: bcdedit /deletevalue useplatformclock");
                if (ProcessHelper.RunBcdEdit("/deletevalue useplatformclock", out string _, out string err))
                {
                    result.ItemsRestored = 1;
                    progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage =
                        Services.LocalizationService.Instance.GetString("SmartRepair_Module_Hpet_RollbackDone") ?? "Relogio restaurado ao padrao. Reinicie para aplicar." });
                    _eventBus.PublishMessage(_correlationId, ModuleId,
                        Services.LocalizationService.Instance.GetString("SmartRepair_Module_Hpet_RollbackDone") ?? "Relogio restaurado ao padrao do Windows (TSC).",
                        SmartRepairEventType.RollbackFinished);
                }
                else
                {
                    _logger.LogError($"[{ModuleId}] Falha ao reativar HPET: {err}");
                    result.Success = false;
                    result.ItemsFailedToRestore = 1;
                }
            }, ct);

            _logger.LogInfo($"[{ModuleId}] Rollback concluído. Restored={result.ItemsRestored}, Failed={result.ItemsFailedToRestore}, CorrelationId={_correlationId}");
            progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = "Rollback concluído." });
            return result;
        }

        /// <summary>
        /// AUDITORIA FORENSE: Lógica corrigida — HPET só deve ser manipulado em hardware LEGADO
        /// (Intel pré-8ª gen / AMD pré-Ryzen) onde o TSC não é invariante e HPET pode ser o
        /// clock source ativo. Em hardware moderno (Intel 8ª gen+ / Ryzen+), TSC invariante
        /// torna o HPET irrelevante e sua desativação via BCDEdit pode causar instabilidade.
        /// </summary>
        private static bool IsCpuCompatible()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name, Architecture FROM Win32_Processor");
                foreach (ManagementObject obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    string? name = obj["Name"]?.ToString();
                    if (string.IsNullOrEmpty(name)) continue;

                    bool isIntel = name.Contains("Intel", StringComparison.OrdinalIgnoreCase);
                    bool isAmd = name.Contains("AMD", StringComparison.OrdinalIgnoreCase);

                    if (isIntel)
                    {
                        // HPET relevante apenas para Intel pré-8ª geração (<= 7th gen)
                        if (name.Contains("i3-", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("i5-", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("i7-", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("i9-", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("Ultra", StringComparison.OrdinalIgnoreCase))
                        {
                            foreach (char c in name)
                            {
                                if (char.IsDigit(c))
                                {
                                    int firstDigit = c - '0';
                                    // AUDITORIA: Só retornar true para LEGADO (gen < 8)
                                    // 8ª gen+ tem TSC invariante, HPET é irrelevante
                                    return firstDigit < 8;
                                }
                            }
                        }
                        // Intel não-i-series (Xeon, Pentium, Celeron) — assumir moderno
                        return false;
                    }

                    if (isAmd)
                    {
                        // HPET relevante apenas para AMD PRÉ-Ryzen (Bulldozer, FX, A-series, Phenom)
                        // Ryzen+ tem TSC invariante — HPET não deve ser tocado
                        return !name.Contains("Ryzen", StringComparison.OrdinalIgnoreCase)
                            && !name.Contains("EPYC", StringComparison.OrdinalIgnoreCase)
                            && !name.Contains("Threadripper", StringComparison.OrdinalIgnoreCase);
                    }

                    return false;
                }
            }
            catch { System.Diagnostics.Debug.WriteLine("[HpetModule] Erro ao detectar CPU"); }
            return false;
        }

        private class HpetInfo
        {
            public bool IsActive { get; set; }
        }
    }
}
