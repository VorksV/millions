using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services.SmartRepair.Architecture;

namespace VoltrisOptimizer.Services.SmartRepair.Modules
{
    public class WindowsSearchOptimizerModule : ISmartRepairModule
    {
        private readonly ILoggingService _logger;
        private readonly ISmartRepairEventBus _eventBus;
        private readonly string _correlationId;
        private readonly List<string> _appliedExclusions = new();
        private bool _restoreRequired;
        private int _originalSearchServiceStart;

        public string ModuleId => "WindowsSearchOptimizer";
        public string Category => "System";

        public string Name => Services.LocalizationService.Instance.GetString("SmartRepair_Module_WSearch_Name") ?? "Otimização Inteligente do Windows Search";
        public string Description => Services.LocalizationService.Instance.GetString("SmartRepair_Module_WSearch_Desc") ?? "Ajusta a indexação para incluir apenas pastas do usuário, excluindo cache, temp e system32. Mantém a pesquisa funcionando normalmente.";

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

        public WindowsSearchOptimizerModule(ILoggingService logger, ISmartRepairEventBus eventBus, string correlationId)
        {
            _logger = logger;
            _eventBus = eventBus;
            _correlationId = correlationId;
        }

        private static readonly string[] SearchExclusionFolders =
        {
            @"C:\Windows",
            @"C:\Windows\System32",
            @"C:\Windows\Temp",
            @"C:\ProgramData\Microsoft\Windows\WER",
            @"C:\ProgramData\USOShared",
            @"C:\ProgramData\Package Cache",
            @"C:\$Recycle.Bin",
            @"C:\ProgramData\Microsoft\Diagnosis",
            @"C:\ProgramData\Microsoft\Windows\Caches",
            @"C:\Windows\SoftwareDistribution\Download",
            @"C:\ProgramData\Microsoft\Windows Defender\Scans",
            @"C:\ProgramData\Microsoft\Windows\RetailDemo",
            @"C:\ProgramData\Microsoft\Windows\WindowsUpdate"
        };

        private static readonly string[] SearchExclusionExtensions =
        {
            ".log", ".tmp", ".dmp", ".etl", ".evtx", ".cab", ".msi", ".pdb"
        };

        public async Task<ModuleScanResult> ScanAsync(IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleScanResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_WSearch_ScanStarting") ?? "Verificando configuração da indexação do Windows Search...", SmartRepairEventType.OperationStarted);

            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    bool needsOptimization = false;

                    try
                    {
                        using var svcKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\WSearch");
                        if (svcKey != null)
                        {
                            var startValue = svcKey.GetValue("Start");
                            if (startValue is int start && start == 4)
                            {
                                result.FoundItems.Add("WSearch_Disabled");
                                result.Statistics.ItemsFound++;
                                needsOptimization = true;
                                _eventBus.PublishMessage(_correlationId, ModuleId, "Serviço Windows Search está desabilitado", SmartRepairEventType.ProgressChanged);
                            }
                        }
                    }
                    catch (UnauthorizedAccessException uaEx)
                    {
                        _logger.LogWarning($"[{ModuleId}] Acesso negado ao verificar WSearch service (não crítico): {uaEx.Message}");
                    }

                    try
                    {
                        using var defaultRules = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows Search\CrawlScopeManager\Windows\SystemIndex\DefaultRules");
                        if (defaultRules == null)
                        {
                            result.FoundItems.Add("NoExclusionRules");
                            result.Statistics.ItemsFound++;
                            needsOptimization = true;
                            _eventBus.PublishMessage(_correlationId, ModuleId, "Regras de exclusão do indexador não configuradas", SmartRepairEventType.ProgressChanged);
                        }
                        else
                        {
                            var existingRules = defaultRules.GetValueNames();
                            foreach (var folder in SearchExclusionFolders)
                            {
                                bool hasRule = existingRules.Any(rule => rule.IndexOf(folder.Replace(@"\", "/"), StringComparison.OrdinalIgnoreCase) >= 0);
                                if (!hasRule)
                                {
                                    result.FoundItems.Add($"MissingExclusion:{folder}");
                                    result.Statistics.ItemsFound++;
                                    needsOptimization = true;
                                }
                            }
                        }
                    }
                    catch (UnauthorizedAccessException uaEx)
                    {
                        _logger.LogWarning($"[{ModuleId}] Acesso negado ao ler regras de exclusão do Windows Search (não crítico, requer elevação): {uaEx.Message}");
                        result.FoundItems.Add("AccessDenied_DefaultRules");
                        result.Statistics.ErrorCount++;
                    }

                    if (!needsOptimization)
                    {
                        string msg = Services.LocalizationService.Instance.GetString("SmartRepair_Module_WSearch_ScanAlreadyOptimized") ?? "Indexação do Windows Search já está otimizada.";
                        _eventBus.PublishMessage(_correlationId, ModuleId, msg, SmartRepairEventType.ProgressChanged);
                        result.Message = msg;
                        return;
                    }

                    string needsMsg = Services.LocalizationService.Instance.GetString("SmartRepair_Module_WSearch_ScanNeedsOptimization") ?? "Windows Search precisa ser otimizado.";
                    result.Message = needsMsg;
                    _eventBus.PublishMessage(_correlationId, ModuleId, needsMsg, SmartRepairEventType.ProgressChanged);
                    _logger.LogDebug($"[{ModuleId}] Scan concluído. Encontrados={result.Statistics.ItemsFound}, Escaneados={result.Statistics.ItemsScanned}, CorrelationId={_correlationId}");
                    progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = result.Message ?? "Scan concluído." });
                    _eventBus.PublishMessage(_correlationId, ModuleId, result.Message ?? "Scan concluído.", SmartRepairEventType.OperationFinished);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[{ModuleId}] Erro no scan: {ex.Message}");
                    result.Statistics.ErrorCount++;
                }
            }, ct);

            return result;
        }

        public Task<ModuleSimulationResult> SimulateAsync(ModuleScanResult scanResult, CancellationToken ct)
        {
            var sim = new ModuleSimulationResult();
            sim.RiskLevel = RiskLevel.Safe;
            sim.EstimatedStatistics.ItemsFound = scanResult.FoundItems.Count;
            foreach (var item in scanResult.FoundItems)
                sim.ItemsToProcess.Add(item);
            _logger.LogDebug($"[{ModuleId}] Simulação concluída. Itens a processar={sim.ItemsToProcess.Count}, RiskLevel={sim.RiskLevel}, CorrelationId={_correlationId}");
            return Task.FromResult(sim);
        }

        public async Task<ModuleExecutionResult> ExecuteAsync(ModuleSimulationResult simResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleExecutionResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_WSearch_ExecStarting") ?? "Aplicando otimização inteligente do Windows Search...", SmartRepairEventType.OperationStarted);

            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    bool isSearchDisabled = false;
                    try
                    {
                        using var svcKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\WSearch");
                        if (svcKey?.GetValue("Start") is int start && start == 4)
                            isSearchDisabled = true;
                    }
                    catch (UnauthorizedAccessException uaEx)
                    {
                        _logger.LogWarning($"[{ModuleId}] Acesso negado ao ler WSearch service key: {uaEx.Message}");
                    }

                    if (isSearchDisabled)
                    {
                        try
                        {
                            using var svcKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\WSearch", true);
                            if (svcKey != null)
                            {
                                _originalSearchServiceStart = svcKey.GetValue("Start") is int s ? s : 4;
                                svcKey.SetValue("Start", 3, RegistryValueKind.DWord);
                                _logger.LogDebug("[{ModuleId}] WSearch reativado (Start=3, Manual)");
                                result.FinalStatistics.ItemsRepaired++;
                            }
                            _restoreRequired = true;
                        }
                        catch (UnauthorizedAccessException uaEx)
                        {
                            _logger.LogWarning($"[{ModuleId}] Acesso negado ao reativar WSearch service (requer elevação): {uaEx.Message}");
                            result.FinalStatistics.ItemsIgnored++;
                        }
                    }

                    progress?.Report(new RepairProgress { StepPercent = 30, StatusMessage = "Configurando regras de exclusão..." });
                    _eventBus.PublishMessage(_correlationId, ModuleId, "Configurando regras de exclusão...", SmartRepairEventType.ProgressChanged);

                    try
                    {
                        using var defaultRules = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows Search\CrawlScopeManager\Windows\SystemIndex\DefaultRules", true);
                        if (defaultRules != null)
                        {
                            foreach (var folder in SearchExclusionFolders)
                            {
                                ct.ThrowIfCancellationRequested();
                                try
                                {
                                    var url = "file:///" + folder.Replace(@"\", "/") + "/";
                                    if (defaultRules.GetValue(url) == null)
                                    {
                                        defaultRules.SetValue(url, 0, RegistryValueKind.DWord);
                                        _appliedExclusions.Add(url);
                                        result.FinalStatistics.ItemsRepaired++;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning($"[{ModuleId}] Erro ao excluir {folder}: {ex.Message}");
                                    result.FinalStatistics.ItemsIgnored++;
                                }
                            }
                        }
                    }
                    catch (UnauthorizedAccessException uaEx)
                    {
                        _logger.LogWarning($"[{ModuleId}] Acesso negado ao criar/escrever regras de exclusão do Windows Search (requer elevação de administrador): {uaEx.Message}");
                        result.FinalStatistics.ItemsIgnored++;
                    }

                    progress?.Report(new RepairProgress { StepPercent = 70, StatusMessage = "Regras de exclusão aplicadas" });

                    try
                    {
                        using var searchKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows Search\Gathering Manager", true);
                        if (searchKey != null)
                        {
                            var currentValue = searchKey.GetValue("DisableBackOff");
                            if (currentValue == null || (currentValue is int dv && dv != 1))
                            {
                                searchKey.SetValue("DisableBackOff", 1, RegistryValueKind.DWord);
                                result.FinalStatistics.ItemsRepaired++;
                            }
                        }
                    }
                    catch (UnauthorizedAccessException uaEx)
                    {
                        _logger.LogWarning($"[{ModuleId}] Acesso negado ao configurar Gathering Manager (não crítico): {uaEx.Message}");
                    }

                    progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = "Windows Search otimizado com sucesso" });
                    _eventBus.PublishMessage(_correlationId, ModuleId, "Windows Search otimizado com sucesso", SmartRepairEventType.OperationFinished);
                    _logger.LogDebug($"[{ModuleId}] Execução concluída. Repaired={result.FinalStatistics.ItemsRepaired}, Errors={result.FinalStatistics.ErrorCount}, Space={result.FinalStatistics.SpaceRecoveredBytes}B, CorrelationId={_correlationId}");
                }
                catch (OperationCanceledException)
                {
                    _logger.LogDebug($"[{ModuleId}] Execução cancelada");
                    result.Success = false;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[{ModuleId}] Erro: {ex.Message}");
                    result.Success = false;
                    result.FinalStatistics.ErrorCount++;
                }
            }, ct);

            return result;
        }

        public async Task<ModuleRollbackResult> RollbackAsync(ModuleExecutionResult execResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleRollbackResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_WSearch_RollbackStarting") ?? "Revertendo otimização do Windows Search...", SmartRepairEventType.RollbackStarted);

            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(new RepairProgress { StepPercent = 50, StatusMessage = "Removendo regras de exclusão..." });

                try
                {
                    using var defaultRules = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows Search\CrawlScopeManager\Windows\SystemIndex\DefaultRules", true);
                    if (defaultRules != null)
                    {
                        foreach (var url in _appliedExclusions)
                        {
                            try
                            {
                                defaultRules.DeleteValue(url, throwOnMissingValue: false);
                                result.ItemsRestored++;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning($"[{ModuleId}] Erro ao remover exclusão {url}: {ex.Message}");
                                result.ItemsFailedToRestore++;
                            }
                        }
                    }

                    if (_restoreRequired)
                    {
                        using var svcKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\WSearch", true);
                        if (svcKey != null)
                        {
                            svcKey.SetValue("Start", _originalSearchServiceStart, RegistryValueKind.DWord);
                            result.ItemsRestored++;
                        }
                    }

                    using var searchKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows Search\Gathering Manager", true);
                    if (searchKey?.GetValue("DisableBackOff") != null)
                    {
                        searchKey.DeleteValue("DisableBackOff", throwOnMissingValue: false);
                        result.ItemsRestored++;
                    }

                    _logger.LogDebug($"[{ModuleId}] Rollback concluído. Restored={result.ItemsRestored}, Failed={result.ItemsFailedToRestore}, CorrelationId={_correlationId}");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[{ModuleId}] Erro no rollback", ex);
                    result.ItemsFailedToRestore++;
                }

                progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_WSearch_RollbackDone") ?? "Rollback da otimização do Windows Search concluído." });
                _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_WSearch_RollbackDone") ?? "Rollback da otimização do Windows Search concluído.", SmartRepairEventType.RollbackFinished);
            }, ct);

            return result;
        }
    }
}
