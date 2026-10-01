using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.SmartRepair.Architecture;

namespace VoltrisOptimizer.Services.SmartRepair.Modules
{
    public class DiskOptimizationModule : ISmartRepairModule
    {
        private readonly ILoggingService _logger;
        private readonly ISmartRepairEventBus _eventBus;
        private readonly string _correlationId;

        public string ModuleId => "DiskOptimization";
        public string Category => "Optimization";
        public string Name => VoltrisOptimizer.Services.LocalizationService.Instance.GetString("SmartRepair_Module_Disk_Name") ?? "Otimização de Disco";
        public string Description => VoltrisOptimizer.Services.LocalizationService.Instance.GetString("SmartRepair_Module_Disk_Desc") ?? "Desfragmenta discos rígidos mecânicos (HDD) de forma segura, ignorando SSDs.";

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

        public DiskOptimizationModule(ILoggingService logger, ISmartRepairEventBus eventBus, string correlationId)
        {
            _logger = logger;
            _eventBus = eventBus;
            _correlationId = correlationId;
        }

        private class DiskTarget
        {
            public string DriveLetter { get; set; } = string.Empty;
            public string MediaType { get; set; } = string.Empty;
        }

        public async Task<ModuleScanResult> ScanAsync(IProgress<RepairProgress> progress, CancellationToken ct)
        {
            _logger.LogInfo($"[SmartRepair][{ModuleId}][{_correlationId}] ═══════════════════════════════════════");
            _logger.LogInfo($"[SmartRepair][{ModuleId}][{_correlationId}] INÍCIO DO SCAN - Módulo: {Name}");
            
            var result = new ModuleScanResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Disk_ScanStarting") ?? "Mapeando discos físicos...", SmartRepairEventType.OperationStarted);
            _logger.LogDebug($"[SmartRepair][{ModuleId}] Evento de scan iniciado publicado no EventBus");

            await Task.Run(() =>
            {
                try
                {
                    _logger.LogDebug($"[SmartRepair][{ModuleId}] Consultando PowerShell para detectar tipo de mídia dos discos...");
                    
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = "powershell",
                        Arguments = "-NoProfile -Command \"Get-Partition | Select-Object DriveLetter, @{n='MediaType';e={(Get-PhysicalDisk -ObjectId $_.DiskId).MediaType}} | ConvertTo-Json\"",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using (Process process = Process.Start(psi))
                    {
                        string output = process.StandardOutput.ReadToEnd();
                        process.WaitForExit();
                        _logger.LogDebug($"[SmartRepair][{ModuleId}] PowerShell executado, output: {output.Length} caracteres");

                        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        string currentDrive = null;
                        int hddCount = 0;
                        int ssdCount = 0;
                        int unknownCount = 0;
                        
                        foreach (var line in lines)
                        {
                            if (line.Contains("\"DriveLetter\":") && !line.Contains("null"))
                            {
                                var parts = line.Split(':');
                                if (parts.Length == 2)
                                {
                                    string val = parts[1].Trim(' ', ',', '"');
                                    if (val.Length > 0 && char.IsLetter(val[0]))
                                        currentDrive = val.Substring(0, 1) + ":";
                                }
                            }
                            else if (line.Contains("\"MediaType\":") && currentDrive != null)
                            {
                                var parts = line.Split(':');
                                if (parts.Length == 2)
                                {
                                    string mType = parts[1].Trim(' ', ',', '"');
                                    int typeCode;
                                    if (int.TryParse(mType, out typeCode))
                                    {
                                        mType = typeCode == 3 ? "HDD" : (typeCode == 4 ? "SSD" : "Unspecified");
                                    }

                                    result.FoundItems.Add(new DiskTarget { DriveLetter = currentDrive, MediaType = mType });
                                    
                                    if (mType == "HDD") hddCount++;
                                    else if (mType == "SSD") ssdCount++;
                                    else unknownCount++;
                                    
                                    _logger.LogDebug($"[SmartRepair][{ModuleId}] Disco detectado: {currentDrive} - {mType}");
                                    currentDrive = null;
                                }
                            }
                        }
                        
                        _logger.LogInfo($"[SmartRepair][{ModuleId}] Scan concluído: {hddCount} HDD(s), {ssdCount} SSD(s), {unknownCount} desconhecido(s)");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[SmartRepair][{ModuleId}] Erro ao mapear discos", ex);
                    _logger.LogWarning($"[SmartRepair][{ModuleId}] Usando fallback para disco C:");
                    result.FoundItems.Add(new DiskTarget { DriveLetter = "C:", MediaType = "Unknown" });
                }
            }, ct);

            result.Statistics.ItemsFound = result.FoundItems.Count;
            result.Statistics.ItemsScanned = result.FoundItems.Count;
            
            _logger.LogInfo($"[SmartRepair][{ModuleId}] Estatísticas: ItemsFound={result.Statistics.ItemsFound}");
            _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Disk_ScanComplete") ?? "{0} discos mapeados.", result.FoundItems.Count), SmartRepairEventType.OperationFinished);
            _logger.LogInfo($"[SmartRepair][{ModuleId}][{_correlationId}] ═══════════════════════════════════════");
            
            return result;
        }

        public Task<ModuleSimulationResult> SimulateAsync(ModuleScanResult scanResult, CancellationToken ct)
        {
            _logger.LogDebug($"[SmartRepair][{ModuleId}][{_correlationId}] INÍCIO da Simulação - Discos: {scanResult.FoundItems.Count}");
            
            var sim = new ModuleSimulationResult();
            sim.RiskLevel = RiskLevel.Safe;
            _logger.LogDebug($"[SmartRepair][{ModuleId}] RiskLevel definido como: {sim.RiskLevel} (Safe)");
            
            int hddCount = 0;
            int ssdCount = 0;
            
            foreach (DiskTarget target in scanResult.FoundItems)
            {
                sim.ItemsToProcess.Add(target);
                if (target.MediaType == "HDD") hddCount++;
                else if (target.MediaType == "SSD") ssdCount++;
                
                _logger.LogDebug($"[SmartRepair][{ModuleId}] {target.DriveLetter} ({target.MediaType}) adicionado à simulação");
            }

            sim.EstimatedStatistics.ItemsFound = scanResult.FoundItems.Count;
            
            _logger.LogInfo($"[SmartRepair][{ModuleId}] Simulação concluída: {hddCount} HDD(s) para desfragmentar, {ssdCount} SSD(s) serão ignorados");
            
            return Task.FromResult(sim);
        }

        public async Task<ModuleExecutionResult> ExecuteAsync(ModuleSimulationResult simResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            _logger.LogInfo($"[SmartRepair][{ModuleId}][{_correlationId}] ═══════════════════════════════════════");
            _logger.LogInfo($"[SmartRepair][{ModuleId}][{_correlationId}] INÍCIO DA EXECUÇÃO - Módulo: {Name}");
            _logger.LogInfo($"[SmartRepair][{ModuleId}] Discos para otimizar: {simResult.ItemsToProcess.Count}");
            
            var result = new ModuleExecutionResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Disk_ExecStarting") ?? "Iniciando otimização de discos...", SmartRepairEventType.OperationStarted);
            _logger.LogDebug($"[SmartRepair][{ModuleId}] Evento de execução iniciado publicado");

            int total = simResult.ItemsToProcess.Count;
            int current = 0;
            int hddOptimized = 0;
            int ssdSkipped = 0;
            int failed = 0;

            foreach (DiskTarget target in simResult.ItemsToProcess)
            {
                ct.ThrowIfCancellationRequested();
                current++;

                int percent = (int)((current / (double)total) * 100);
                
                if (target.MediaType == "SSD")
                {
                    string msg = string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Disk_SkippingSsd") ?? "Ignorando {0}: SSD detectado (TRIM automático)", target.DriveLetter);
                    _logger.LogDebug($"[SmartRepair][{ModuleId}] [{current}/{total}] ℹ️ {msg}");
                    _eventBus.PublishMessage(_correlationId, ModuleId, msg, SmartRepairEventType.ProgressChanged);
                    progress?.Report(new RepairProgress { StepPercent = percent, StatusMessage = msg });
                    result.FinalStatistics.ItemsIgnored++;
                    ssdSkipped++;
                    continue;
                }

                string statusText = string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Disk_Defragging") ?? "Desfragmentando {0}... Isso pode levar muito tempo.", target.DriveLetter);
                _logger.LogInfo($"[SmartRepair][{ModuleId}] [{current}/{total}] 🔄 {statusText}");
                _eventBus.PublishMessage(_correlationId, ModuleId, statusText, SmartRepairEventType.ProgressChanged);
                progress?.Report(new RepairProgress { StepPercent = percent, StatusMessage = statusText });

                try
                {
                    _logger.LogDebug($"[SmartRepair][{ModuleId}] Executando: defrag.exe {target.DriveLetter} /O /U /V");
                    
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = "defrag.exe",
                        Arguments = $"{target.DriveLetter} /O /U /V",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using (Process process = Process.Start(psi))
                    {
                        process.OutputDataReceived += (s, e) =>
                        {
                            if (!string.IsNullOrWhiteSpace(e.Data) && !e.Data.Contains("Copyright"))
                            {
                                _logger.LogDebug($"[SmartRepair][{ModuleId}] [defrag output] {e.Data.Trim()}");
                                _eventBus.PublishMessage(_correlationId, ModuleId, e.Data.Trim(), SmartRepairEventType.ProgressChanged);
                            }
                        };
                        process.BeginOutputReadLine();
                        await process.WaitForExitAsync(ct);
                        
                        if (process.ExitCode == 0)
                        {
                            hddOptimized++;
                            result.FinalStatistics.ItemsRepaired++;
                            _logger.LogSuccess($"[SmartRepair][{ModuleId}] [{current}/{total}] ✅ {target.DriveLetter} desfragmentado com sucesso");
                            _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Disk_DefragSuccess") ?? "Disco {0} desfragmentado com sucesso.", target.DriveLetter), SmartRepairEventType.ProgressChanged);
                        }
                        else
                        {
                            failed++;
                            result.FinalStatistics.ItemsIgnored++;
                            _logger.LogWarning($"[SmartRepair][{ModuleId}] [{current}/{total}] ⚠️ {target.DriveLetter} - ExitCode {process.ExitCode}");
                            _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Disk_DefragWarning") ?? "Aviso ao otimizar {0} (código {1}).", target.DriveLetter, process.ExitCode), SmartRepairEventType.ProgressChanged);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning($"[SmartRepair][{ModuleId}] [{current}/{total}] ❌ Operação cancelada pelo usuário");
                    throw;
                }
                catch (Exception ex)
                {
                    failed++;
                    result.FinalStatistics.ItemsIgnored++;
                    _logger.LogError($"[SmartRepair][{ModuleId}] [{current}/{total}] Erro na desfragmentação de {target.DriveLetter}", ex);
                }
            }

            _logger.LogInfo($"[SmartRepair][{ModuleId}] ═══════════════════════════════════════");
            _logger.LogInfo($"[SmartRepair][{ModuleId}] EXECUÇÃO FINALIZADA");
            _logger.LogInfo($"[SmartRepair][{ModuleId}] Resultados:");
            _logger.LogInfo($"[SmartRepair][{ModuleId}]   - Total discos: {total}");
            _logger.LogInfo($"[SmartRepair][{ModuleId}]   - HDDs desfragmentados: {hddOptimized}");
            _logger.LogInfo($"[SmartRepair][{ModuleId}]   - SSDs ignorados (TRIM): {ssdSkipped}");
            _logger.LogInfo($"[SmartRepair][{ModuleId}]   - Falharam: {failed}");
            
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Disk_Done") ?? "Otimização de discos concluída.", SmartRepairEventType.OperationFinished);
            _logger.LogInfo($"[SmartRepair][{ModuleId}][{_correlationId}] ═══════════════════════════════════════");
            
            return result;
        }

        public Task<ModuleRollbackResult> RollbackAsync(ModuleExecutionResult execResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            throw new NotSupportedException(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Disk_NoRollback") ?? "Desfragmentação não suporta rollback.");
        }
    }
}
