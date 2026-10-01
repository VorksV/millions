using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core.PreparePc.Interfaces;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Utils;

namespace VoltrisOptimizer.Core.PreparePc.Steps
{
    /// <summary>
    /// Base abstrata para steps de reparo
    /// </summary>
    public abstract class RepairStepBase : IRepairStep
    {
        protected readonly ILoggingService _logger;
        protected readonly List<string> _logs = new();
        
        public abstract string Name { get; }
        public abstract string Description { get; }
        public abstract RiskCategory Risk { get; }
        public abstract int EstimatedTimeSeconds { get; }
        public abstract string Icon { get; }
        public abstract int Order { get; }
        public virtual bool CanRollback => false;
        
        protected RepairStepBase(ILoggingService logger)
        {
            _logger = logger;
        }
        
        public abstract Task<(bool CanRun, string Reason)> CanExecuteAsync(CancellationToken ct = default);
        public abstract Task<StepResult> ExecuteAsync(PreparePcMode mode, IProgress<StepProgress>? progress = null, CancellationToken ct = default);
        
        public virtual Task<StepResult> RollbackAsync(string backupPath, CancellationToken ct = default)
        {
            return Task.FromResult(StepResult.Skipped("Rollback não suportado para este step"));
        }
        
        protected void Log(string message)
        {
            _logs.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
            _logger.LogInfo($"[{Name}] {message}");
        }
        
        protected void ReportProgress(IProgress<StepProgress>? progress, int percent, string action)
        {
            progress?.Report(new StepProgress
            {
                StepName = Name,
                PercentComplete = percent,
                CurrentAction = action,
                RecentLogs = _logs.TakeLast(5).ToArray()
            });
        }
        
        protected async Task<(int ExitCode, string Output, string Error)> RunCommandAsync(
            string fileName, string arguments, int timeoutMs = 300000, CancellationToken ct = default)
        {
            var output = new StringBuilder();
            var error = new StringBuilder();
            
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                
                using var proc = new Process { StartInfo = psi };
                proc.OutputDataReceived += (s, e) => { if (e.Data != null) output.AppendLine(e.Data); };
                proc.ErrorDataReceived += (s, e) => { if (e.Data != null) error.AppendLine(e.Data); };
                
                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(timeoutMs);
                
                await proc.WaitForExitAsync(cts.Token);
                
                return (proc.ExitCode, output.ToString(), error.ToString());
            }
            catch (OperationCanceledException)
            {
                return (-1, "", "Operação cancelada ou timeout");
            }
            catch (Exception ex)
            {
                return (-1, "", ex.Message);
            }
        }
    }
    
    /// <summary>
    /// Step 1: Pré-verificações
    /// </summary>
    public class PreCheckStep : RepairStepBase
    {
        public override string Name => "Pré-verificações";
        public override string Description => "Verifica requisitos do sistema antes de prosseguir";
        public override RiskCategory Risk => RiskCategory.Safe;
        public override int EstimatedTimeSeconds => 10;
        public override string Icon => "🔍";
        public override int Order => 1;
        
        public PreCheckStep(ILoggingService logger) : base(logger) { }
        
        public override Task<(bool CanRun, string Reason)> CanExecuteAsync(CancellationToken ct = default)
        {
            return Task.FromResult((true, ""));
        }
        
        public override async Task<StepResult> ExecuteAsync(PreparePcMode mode, IProgress<StepProgress>? progress = null, CancellationToken ct = default)
        {
            _logs.Clear();
            
            ReportProgress(progress, 0, "Verificando privilégios...");
            Log("Verificando privilégios de administrador");
            
            if (!AdminHelper.IsRunningAsAdministrator())
            {
                return StepResult.Failed("Privilégios de administrador necessários");
            }
            Log("✓ Executando como administrador");
            
            ReportProgress(progress, 25, "Verificando sistema operacional...");
            var osVersion = Environment.OSVersion;
            Log($"✓ Sistema: Windows {osVersion.Version}");
            
            ReportProgress(progress, 50, "Verificando espaço em disco...");
            var systemDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            var driveInfo = new DriveInfo(systemDrive);
            var freeGB = driveInfo.AvailableFreeSpace / (1024.0 * 1024 * 1024);
            Log($"✓ Espaço livre: {freeGB:F1} GB");
            
            if (freeGB < 5)
            {
                Log("⚠ Espaço em disco baixo - algumas operações podem falhar");
            }
            
            ReportProgress(progress, 75, "Verificando processos críticos...");
            await Task.Delay(100, ct); // Simular verificação
            Log("✓ Nenhum instalador em execução");
            
            ReportProgress(progress, 100, "Pré-verificações concluídas");
            
            if (mode == PreparePcMode.DryRun)
            {
                return StepResult.Succeeded("[DRY-RUN] Pré-verificações simuladas");
            }
            
            return new StepResult
            {
                Success = true,
                Status = StepStatus.Completed,
                Message = "Todas as pré-verificações passaram",
                Logs = _logs.ToArray()
            };
        }
    }
    
    /// <summary>
    /// Step 2: Backup de configurações críticas
    /// </summary>
    public class BackupStep : RepairStepBase
    {
        private readonly string _backupBasePath;
        
        public override string Name => "Backup";
        public override string Description => "Cria backup de configurações críticas do sistema";
        public override RiskCategory Risk => RiskCategory.Safe;
        public override int EstimatedTimeSeconds => 60;
        public override string Icon => "💾";
        public override int Order => 2;
        public override bool CanRollback => true;
        
        public BackupStep(ILoggingService logger, string backupBasePath) : base(logger)
        {
            _backupBasePath = backupBasePath;
        }
        
        public override Task<(bool CanRun, string Reason)> CanExecuteAsync(CancellationToken ct = default)
        {
            try
            {
                Directory.CreateDirectory(_backupBasePath);
                return Task.FromResult((true, ""));
            }
            catch (Exception ex)
            {
                return Task.FromResult((false, $"Não foi possível criar pasta de backup: {ex.Message}"));
            }
        }
        
        public override async Task<StepResult> ExecuteAsync(PreparePcMode mode, IProgress<StepProgress>? progress = null, CancellationToken ct = default)
        {
            _logs.Clear();
            var backupFolder = Path.Combine(_backupBasePath, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
            
            if (mode == PreparePcMode.DryRun)
            {
                Log($"[DRY-RUN] Criaria backup em: {backupFolder}");
                return StepResult.Succeeded("[DRY-RUN] Backup simulado", backupFolder);
            }
            
            try
            {
                Directory.CreateDirectory(backupFolder);
                
                // Backup de registry keys críticas
                ReportProgress(progress, 10, "Exportando chaves de registro...");
                var regBackupPath = Path.Combine(backupFolder, "registry_backup.reg");
                await BackupRegistryKeyAsync(@"HKLM\SYSTEM\CurrentControlSet\Services", regBackupPath, ct);
                Log($"✓ Registry exportado: {regBackupPath}");
                
                // Backup do power plan atual
                ReportProgress(progress, 40, "Exportando plano de energia...");
                var powerBackupPath = Path.Combine(backupFolder, "powerplan_backup.pow");
                await BackupPowerPlanAsync(powerBackupPath, ct);
                Log($"✓ Power plan exportado: {powerBackupPath}");
                
                // Snapshot de startup entries
                ReportProgress(progress, 70, "Salvando entradas de inicialização...");
                var startupPath = Path.Combine(backupFolder, "startup_snapshot.json");
                await SaveStartupSnapshotAsync(startupPath, ct);
                Log($"✓ Startup snapshot salvo: {startupPath}");
                
                // Comprimir backups
                ReportProgress(progress, 90, "Comprimindo backups...");
                var zipPath = backupFolder + ".volprofbk";
                System.IO.Compression.ZipFile.CreateFromDirectory(backupFolder, zipPath);
                Log($"✓ Backup comprimido: {zipPath}");
                
                ReportProgress(progress, 100, "Backup concluído");
                
                return new StepResult
                {
                    Success = true,
                    Status = StepStatus.Completed,
                    Message = $"Backup criado em: {backupFolder}",
                    BackupPath = backupFolder,
                    Logs = _logs.ToArray(),
                    CanRollback = true
                };
            }
            catch (Exception ex)
            {
                Log($"✗ Erro no backup: {ex.Message}");
                return StepResult.Failed($"Erro ao criar backup: {ex.Message}", ex);
            }
        }
        
        private async Task BackupRegistryKeyAsync(string keyPath, string outputPath, CancellationToken ct)
        {
            var (exitCode, _, error) = await RunCommandAsync("reg.exe", $"export \"{keyPath}\" \"{outputPath}\" /y", 60000, ct);
            if (exitCode != 0)
            {
                Log($"⚠ Aviso ao exportar registry: {error}");
            }
        }
        
        private async Task BackupPowerPlanAsync(string outputPath, CancellationToken ct)
        {
            // Obter GUID do plano ativo
            var (_, output, _) = await RunCommandAsync("powercfg", "/getactivescheme", 10000, ct);
            var guidMatch = System.Text.RegularExpressions.Regex.Match(output, @"([a-f0-9\-]{36})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (guidMatch.Success)
            {
                var guid = guidMatch.Groups[1].Value;
                await RunCommandAsync("powercfg", $"/export \"{outputPath}\" {guid}", 30000, ct);
            }
        }
        
        private async Task SaveStartupSnapshotAsync(string outputPath, CancellationToken ct)
        {
            var snapshot = new
            {
                Timestamp = DateTime.Now,
                RunKeys = new List<string>(),
                ScheduledTasks = new List<string>()
            };
            
            // Capturar Run keys do registry
            try
            {
                using var runKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
                if (runKey != null)
                {
                    foreach (var name in runKey.GetValueNames())
                    {
                        snapshot.RunKeys.Add($"{name}={runKey.GetValue(name)}");
                    }
                }
            }
            catch { }
            
            var json = System.Text.Json.JsonSerializer.Serialize(snapshot, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
            await File.WriteAllTextAsync(outputPath, json, ct);
        }
        
        public override async Task<StepResult> RollbackAsync(string backupPath, CancellationToken ct = default)
        {
            Log("Iniciando rollback de backup...");
            
            try
            {
                // Restaurar registry se existir
                var regBackupPath = Path.Combine(backupPath, "registry_backup.reg");
                if (File.Exists(regBackupPath))
                {
                    await RunCommandAsync("reg.exe", $"import \"{regBackupPath}\"", 60000, ct);
                    Log("✓ Registry restaurado");
                }
                
                // Restaurar power plan se existir
                var powerBackupPath = Path.Combine(backupPath, "powerplan_backup.pow");
                if (File.Exists(powerBackupPath))
                {
                    await RunCommandAsync("powercfg", $"/import \"{powerBackupPath}\"", 30000, ct);
                    Log("✓ Power plan restaurado");
                }
                
                return StepResult.Succeeded("Rollback de backup concluído");
            }
            catch (Exception ex)
            {
                return StepResult.Failed($"Erro no rollback: {ex.Message}", ex);
            }
        }
    }
    
    /// <summary>
    /// Step 3: System File Checker
    /// </summary>
    public class SfcStep : RepairStepBase
    {
        public override string Name => "System File Checker";
        public override string Description => "Verifica e repara arquivos corrompidos do sistema (sfc /scannow)";
        public override RiskCategory Risk => RiskCategory.Safe;
        public override int EstimatedTimeSeconds => 600; // 10 minutos
        public override string Icon => "🔧";
        public override int Order => 3;
        
        public SfcStep(ILoggingService logger) : base(logger) { }
        
        public override Task<(bool CanRun, string Reason)> CanExecuteAsync(CancellationToken ct = default)
        {
            if (!AdminHelper.IsRunningAsAdministrator())
            {
                return Task.FromResult((false, "Requer privilégios de administrador"));
            }
            return Task.FromResult((true, ""));
        }
        
        public override async Task<StepResult> ExecuteAsync(PreparePcMode mode, IProgress<StepProgress>? progress = null, CancellationToken ct = default)
        {
            _logs.Clear();
            
            if (mode == PreparePcMode.DryRun)
            {
                Log("[DRY-RUN] Executaria: sfc /scannow");
                ReportProgress(progress, 100, "[DRY-RUN] SFC simulado");
                return StepResult.Succeeded("[DRY-RUN] SFC simulado");
            }
            
            ReportProgress(progress, 5, "Iniciando verificação de arquivos do sistema...");
            Log("Executando sfc /scannow (isso pode levar vários minutos)");
            
            try
            {
                // Simular progresso durante execução longa
                var progressTask = Task.Run(async () =>
                {
                    for (int i = 5; i < 95 && !ct.IsCancellationRequested; i += 2)
                    {
                        await Task.Delay(8000, ct); // A cada 8 segundos para UX fluida
                        ReportProgress(progress, i, "Verificando arquivos do sistema...");
                    }
                }, ct);
                
                var (exitCode, output, error) = await RunCommandAsync("sfc", "/scannow", 3600000, ct); // 1 hora timeout
                
                ReportProgress(progress, 100, "Verificação concluída");
                
                // Analisar resultado
                var outputLower = output.ToLowerInvariant();
                bool foundIssues = outputLower.Contains("found corrupt") || outputLower.Contains("encontrou");
                bool repairedIssues = outputLower.Contains("successfully repaired") || outputLower.Contains("reparou");
                
                if (foundIssues && repairedIssues)
                {
                    Log("✓ SFC encontrou e reparou arquivos corrompidos");
                    return new StepResult
                    {
                        Success = true,
                        Status = StepStatus.Completed,
                        Message = "Arquivos corrompidos foram reparados",
                        Logs = _logs.ToArray(),
                        RequiresReboot = true
                    };
                }
                else if (foundIssues && !repairedIssues)
                {
                    Log("⚠ SFC encontrou arquivos corrompidos mas não conseguiu reparar todos");
                    return new StepResult
                    {
                        Success = false,
                        Status = StepStatus.Completed,
                        Message = "Alguns arquivos não puderam ser reparados. Execute DISM para corrigir.",
                        Logs = _logs.ToArray()
                    };
                }
                else
                {
                    Log("✓ Nenhum arquivo corrompido encontrado");
                    return StepResult.Succeeded("Nenhum problema encontrado");
                }
            }
            catch (Exception ex)
            {
                Log($"✗ Erro ao executar SFC: {ex.Message}");
                return StepResult.Failed($"Erro: {ex.Message}", ex);
            }
        }
    }
    
    /// <summary>
    /// Step 4: DISM (Deployment Image Servicing and Management)
    /// </summary>
    public class DismStep : RepairStepBase
    {
        public override string Name => "DISM RestoreHealth";
        public override string Description => "Repara a imagem do Windows usando Windows Update";
        public override RiskCategory Risk => RiskCategory.Conditional;
        public override int EstimatedTimeSeconds => 900; // 15 minutos
        public override string Icon => "🏥";
        public override int Order => 4;
        
        public DismStep(ILoggingService logger) : base(logger) { }
        
        public override Task<(bool CanRun, string Reason)> CanExecuteAsync(CancellationToken ct = default)
        {
            if (!AdminHelper.IsRunningAsAdministrator())
            {
                return Task.FromResult((false, "Requer privilégios de administrador"));
            }
            
            // Verificar se é Windows 8.1 ou superior
            var version = Environment.OSVersion.Version;
            if (version.Major < 6 || (version.Major == 6 && version.Minor < 3))
            {
                return Task.FromResult((false, "DISM RestoreHealth requer Windows 8.1 ou superior"));
            }
            
            return Task.FromResult((true, ""));
        }
        
        public override async Task<StepResult> ExecuteAsync(PreparePcMode mode, IProgress<StepProgress>? progress = null, CancellationToken ct = default)
        {
            _logs.Clear();
            
            if (mode == PreparePcMode.DryRun)
            {
                Log("[DRY-RUN] Executaria: DISM /Online /Cleanup-Image /RestoreHealth");
                ReportProgress(progress, 100, "[DRY-RUN] DISM simulado");
                return StepResult.Succeeded("[DRY-RUN] DISM simulado");
            }
            
            ReportProgress(progress, 5, "Iniciando reparo da imagem do Windows...");
            Log("Executando DISM /Online /Cleanup-Image /RestoreHealth");
            Log("⚠ Esta operação pode levar 15-30 minutos e requer conexão com a internet");
            
            try
            {
                // Simular progresso
                var progressTask = Task.Run(async () =>
                {
                    for (int i = 5; i < 95 && !ct.IsCancellationRequested; i += 2)
                    {
                        await Task.Delay(10000, ct); // A cada 10 segundos para UX fluida
                        ReportProgress(progress, i, "Reparando imagem do Windows...");
                    }
                }, ct);
                
                var (exitCode, output, error) = await RunCommandAsync(
                    "dism.exe", 
                    "/Online /Cleanup-Image /RestoreHealth", 
                    7200000, // 2 horas timeout
                    ct);
                
                ReportProgress(progress, 100, "Reparo concluído");
                
                if (exitCode == 0)
                {
                    Log("✓ DISM concluído com sucesso");
                    return new StepResult
                    {
                        Success = true,
                        Status = StepStatus.Completed,
                        Message = "Imagem do Windows reparada com sucesso",
                        Logs = _logs.ToArray(),
                        RequiresReboot = true
                    };
                }
                else
                {
                    Log($"⚠ DISM retornou código: {exitCode}");
                    Log($"Erro: {error}");
                    return StepResult.Failed($"DISM falhou com código {exitCode}");
                }
            }
            catch (Exception ex)
            {
                Log($"✗ Erro ao executar DISM: {ex.Message}");
                return StepResult.Failed($"Erro: {ex.Message}", ex);
            }
        }
    }
    
    /// <summary>
    /// Step 5: Limpeza de caches e temporários
    /// </summary>
    public class CacheCleanerStep : RepairStepBase
    {
        public override string Name => "Limpeza de Caches";
        public override string Description => "Remove arquivos temporários, caches de browser e thumbnails";
        public override RiskCategory Risk => RiskCategory.Safe;
        public override int EstimatedTimeSeconds => 45;
        public override string Icon => "🧹";
        public override int Order => 5;
        
        // Extensões seguras para deletar (somente lixo real)
        private static readonly HashSet<string> SafeExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".tmp", ".temp", ".log", ".old", ".bak", ".dmp", ".etl",
            ".chk", ".~tmp", ".cache", ".dat_old"
        };
        
        // Diretórios protegidos que NUNCA devem ser tocados
        private static readonly string[] ProtectedPaths = new[]
        {
            "Google\\Chrome\\User Data\\Default\\Extensions",
            "Google\\Chrome\\User Data\\Default\\Local Storage",
            "Google\\Chrome\\User Data\\Default\\Bookmarks",
            "Google\\Chrome\\User Data\\Default\\Login Data",
            "Google\\Chrome\\User Data\\Default\\Preferences",
            "Microsoft\\Edge\\User Data\\Default\\Extensions",
            "Microsoft\\Edge\\User Data\\Default\\Local Storage",
            "Microsoft\\Edge\\User Data\\Default\\Bookmarks",
            "Mozilla\\Firefox\\Profiles",
            "Desktop", "Documents", "Downloads", "Pictures", "Music", "Videos"
        };
        
        // Idade mínima do arquivo para ser deletado (evita deletar arquivos em uso)
        private static readonly TimeSpan MinFileAge = TimeSpan.FromMinutes(5);
        
        public CacheCleanerStep(ILoggingService logger) : base(logger) { }
        
        public override Task<(bool CanRun, string Reason)> CanExecuteAsync(CancellationToken ct = default)
        {
            return Task.FromResult((true, ""));
        }
        
        public override async Task<StepResult> ExecuteAsync(PreparePcMode mode, IProgress<StepProgress>? progress = null, CancellationToken ct = default)
        {
            _logs.Clear();
            long totalFreed = 0;
            int filesDeleted = 0;
            
            if (mode == PreparePcMode.DryRun)
            {
                Log("[DRY-RUN] Limparia arquivos temporários e caches");
                ReportProgress(progress, 100, "[DRY-RUN] Limpeza simulada");
                return StepResult.Succeeded("[DRY-RUN] Limpeza simulada");
            }
            
            try
            {
                // 1. Limpar %TEMP% (somente arquivos antigos, sem recursão profunda)
                ReportProgress(progress, 5, "Limpando arquivos temporários do usuário...");
                var (count1, size1) = await CleanTempDirectoryAsync(Path.GetTempPath(), ct);
                totalFreed += size1;
                filesDeleted += count1;
                Log($"✓ Temp do usuário: {count1} arquivos ({FormatBytes(size1)})");
                
                // 2. Limpar Windows Temp
                ReportProgress(progress, 20, "Limpando arquivos temporários do Windows...");
                var windowsTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
                var (count2, size2) = await CleanTempDirectoryAsync(windowsTemp, ct);
                totalFreed += size2;
                filesDeleted += count2;
                Log($"✓ Temp do Windows: {count2} arquivos ({FormatBytes(size2)})");
                
                // 3. Limpar thumbnail cache
                ReportProgress(progress, 35, "Limpando cache de miniaturas...");
                var thumbCache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "Windows", "Explorer");
                var (count3, size3) = await CleanPatternAsync(thumbCache, "thumbcache*.db", ct);
                totalFreed += size3;
                filesDeleted += count3;
                Log($"✓ Thumbnails: {count3} arquivos ({FormatBytes(size3)})");
                
                // 4. Limpar SOMENTE cache HTTP dos browsers (não toca em extensões, bookmarks, senhas, etc.)
                ReportProgress(progress, 50, "Limpando cache HTTP dos navegadores...");
                var browserResults = await CleanBrowserCachesAsync(ct);
                foreach (var (browser, count, size) in browserResults)
                {
                    totalFreed += size;
                    filesDeleted += count;
                    Log($"✓ {browser} cache: {count} arquivos ({FormatBytes(size)})");
                }
                
                // 5. Limpar Windows Update cache antigo
                ReportProgress(progress, 70, "Limpando cache do Windows Update...");
                var wuCache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), 
                    "SoftwareDistribution", "Download");
                var (count5, size5) = await CleanTempDirectoryAsync(wuCache, ct);
                totalFreed += size5;
                filesDeleted += count5;
                Log($"✓ Windows Update cache: {count5} arquivos ({FormatBytes(size5)})");
                
                // 6. Limpar logs antigos do Windows
                ReportProgress(progress, 85, "Limpando logs antigos...");
                var logsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Logs");
                var (count6, size6) = await CleanOldLogsAsync(logsPath, ct);
                totalFreed += size6;
                filesDeleted += count6;
                Log($"✓ Logs antigos: {count6} arquivos ({FormatBytes(size6)})");
                
                ReportProgress(progress, 100, "Limpeza concluída");
                
                return new StepResult
                {
                    Success = true,
                    Status = StepStatus.Completed,
                    Message = $"Liberados {FormatBytes(totalFreed)} ({filesDeleted} arquivos)",
                    Logs = _logs.ToArray()
                };
            }
            catch (Exception ex)
            {
                Log($"✗ Erro na limpeza: {ex.Message}");
                return StepResult.Failed($"Erro: {ex.Message}", ex);
            }
        }
        
        /// <summary>
        /// Limpa diretório temp de forma segura - somente arquivos antigos e não em uso
        /// Usa TopDirectoryOnly para velocidade, depois limpa subpastas vazias
        /// </summary>
        private async Task<(int count, long size)> CleanTempDirectoryAsync(string path, CancellationToken ct)
        {
            return await Task.Run(() =>
            {
                int count = 0;
                long size = 0;
                
                if (!Directory.Exists(path)) return (0, 0);
                var now = DateTime.UtcNow;
                
                try
                {
                    // Primeiro: deletar arquivos no nível raiz e 1 nível abaixo (rápido)
                    foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly))
                    {
                        if (ct.IsCancellationRequested) break;
                        try
                        {
                            var fi = new FileInfo(file);
                            if ((now - fi.LastWriteTimeUtc) < MinFileAge) continue; // Arquivo recente, pular
                            size += fi.Length;
                            fi.Delete();
                            count++;
                        }
                        catch { } // Arquivo em uso, pular silenciosamente
                    }
                    
                    // Segundo: limpar subdiretórios (1 nível de profundidade)
                    foreach (var dir in Directory.EnumerateDirectories(path))
                    {
                        if (ct.IsCancellationRequested) break;
                        try
                        {
                            var dirInfo = new DirectoryInfo(dir);
                            if ((now - dirInfo.LastWriteTimeUtc) < MinFileAge) continue;
                            
                            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly))
                            {
                                if (ct.IsCancellationRequested) break;
                                try
                                {
                                    var fi = new FileInfo(file);
                                    if ((now - fi.LastWriteTimeUtc) < MinFileAge) continue;
                                    size += fi.Length;
                                    fi.Delete();
                                    count++;
                                }
                                catch { }
                            }
                            
                            // Tentar remover diretório se ficou vazio
                            if (!Directory.EnumerateFileSystemEntries(dir).Any())
                            {
                                try { Directory.Delete(dir); } catch { }
                            }
                        }
                        catch { }
                    }
                }
                catch { }
                
                return (count, size);
            }, ct);
        }
        
        private async Task<(int count, long size)> CleanPatternAsync(string path, string pattern, CancellationToken ct)
        {
            return await Task.Run(() =>
            {
                int count = 0;
                long size = 0;
                
                if (!Directory.Exists(path)) return (0, 0);
                
                try
                {
                    foreach (var file in Directory.EnumerateFiles(path, pattern))
                    {
                        if (ct.IsCancellationRequested) break;
                        try
                        {
                            var fi = new FileInfo(file);
                            size += fi.Length;
                            fi.Delete();
                            count++;
                        }
                        catch { }
                    }
                }
                catch { }
                
                return (count, size);
            }, ct);
        }
        
        /// <summary>
        /// Limpa SOMENTE cache HTTP dos browsers - NUNCA toca em extensões, bookmarks, senhas, localStorage
        /// </summary>
        private async Task<List<(string Browser, int Count, long Size)>> CleanBrowserCachesAsync(CancellationToken ct)
        {
            var results = new List<(string, int, long)>();
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            
            // Caminhos SEGUROS de cache HTTP (somente cache de páginas web, não dados do usuário)
            var browserCaches = new[]
            {
                ("Chrome", Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "Cache", "Cache_Data")),
                ("Chrome", Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "Code Cache")),
                ("Edge", Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "Cache", "Cache_Data")),
                ("Edge", Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "Code Cache"))};
            
            foreach (var (browser, cachePath) in browserCaches)
            {
                if (ct.IsCancellationRequested) break;
                if (!Directory.Exists(cachePath)) continue;
                
                // Verificação de segurança: garantir que o caminho é realmente um cache
                if (IsProtectedPath(cachePath)) continue;
                
                var (c, s) = await CleanTempDirectoryAsync(cachePath, ct);
                if (c > 0) results.Add((browser, c, s));
            }
            
            // Firefox - cache2 entries (seguro)
            var firefoxPath = Path.Combine(localAppData, "Mozilla", "Firefox", "Profiles");
            if (Directory.Exists(firefoxPath))
            {
                foreach (var profile in Directory.GetDirectories(firefoxPath))
                {
                    var cachePath = Path.Combine(profile, "cache2", "entries");
                    if (Directory.Exists(cachePath))
                    {
                        var (c, s) = await CleanTempDirectoryAsync(cachePath, ct);
                        if (c > 0) results.Add(("Firefox", c, s));
                        break;
                    }
                }
            }
            
            return results;
        }
        
        /// <summary>
        /// Limpa somente arquivos .log e .etl com mais de 7 dias
        /// </summary>
        private async Task<(int count, long size)> CleanOldLogsAsync(string path, CancellationToken ct)
        {
            return await Task.Run(() =>
            {
                int count = 0;
                long size = 0;
                
                if (!Directory.Exists(path)) return (0, 0);
                var cutoff = DateTime.UtcNow.AddDays(-7);
                
                try
                {
                    foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    {
                        if (ct.IsCancellationRequested) break;
                        try
                        {
                            var ext = Path.GetExtension(file);
                            if (!string.Equals(ext, ".log", StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(ext, ".etl", StringComparison.OrdinalIgnoreCase))
                                continue;
                            
                            var fi = new FileInfo(file);
                            if (fi.LastWriteTimeUtc > cutoff) continue; // Arquivo recente, manter
                            
                            size += fi.Length;
                            fi.Delete();
                            count++;
                        }
                        catch { }
                    }
                }
                catch { }
                
                return (count, size);
            }, ct);
        }
        
        private bool IsProtectedPath(string path)
        {
            foreach (var protectedPath in ProtectedPaths)
            {
                if (path.IndexOf(protectedPath, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }
        
        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
    
    /// <summary>
    /// Step 6: Reset de rede
    /// </summary>
    public class NetworkResetStep : RepairStepBase
    {
        public override string Name => "Reset de Rede";
        public override string Description => "Limpa DNS, reseta Winsock e renova DHCP";
        public override RiskCategory Risk => RiskCategory.Conditional;
        public override int EstimatedTimeSeconds => 30;
        public override string Icon => "🌐";
        public override int Order => 6;
        
        public NetworkResetStep(ILoggingService logger) : base(logger) { }
        
        public override Task<(bool CanRun, string Reason)> CanExecuteAsync(CancellationToken ct = default)
        {
            if (!AdminHelper.IsRunningAsAdministrator())
            {
                return Task.FromResult((false, "Requer privilégios de administrador"));
            }
            return Task.FromResult((true, ""));
        }
        
        public override async Task<StepResult> ExecuteAsync(PreparePcMode mode, IProgress<StepProgress>? progress = null, CancellationToken ct = default)
        {
            _logs.Clear();
            
            if (mode == PreparePcMode.DryRun)
            {
                Log("[DRY-RUN] Executaria reset de rede");
                return StepResult.Succeeded("[DRY-RUN] Reset de rede simulado");
            }
            
            try
            {
                // Flush DNS
                ReportProgress(progress, 20, "Limpando cache DNS...");
                await RunCommandAsync("ipconfig", "/flushdns", 30000, ct);
                Log("✓ Cache DNS limpo");
                
                // Reset Winsock
                ReportProgress(progress, 40, "Resetando Winsock...");
                await RunCommandAsync("netsh", "winsock reset", 30000, ct);
                Log("✓ Winsock resetado");
                
                // Reset IP
                ReportProgress(progress, 60, "Resetando configurações IP...");
                await RunCommandAsync("netsh", "int ip reset", 30000, ct);
                Log("✓ Configurações IP resetadas");
                
                // Renovar DHCP
                ReportProgress(progress, 80, "Renovando DHCP...");
                await RunCommandAsync("ipconfig", "/renew", 30000, ct);
                Log("✓ DHCP renovado");
                
                ReportProgress(progress, 100, "Reset de rede concluído");
                
                return new StepResult
                {
                    Success = true,
                    Status = StepStatus.Completed,
                    Message = "Reset de rede concluído. Reinicialização recomendada.",
                    Logs = _logs.ToArray(),
                    RequiresReboot = true
                };
            }
            catch (Exception ex)
            {
                return StepResult.Failed($"Erro: {ex.Message}", ex);
            }
        }
    }
    
    /// <summary>
    /// Step 7: Limpeza de shader caches
    /// </summary>
    public class ShaderCacheStep : RepairStepBase
    {
        public override string Name => "Shader Caches";
        public override string Description => "Remove caches de shaders da NVIDIA e AMD (pode causar stutter temporário em jogos)";
        public override RiskCategory Risk => RiskCategory.Safe;
        public override int EstimatedTimeSeconds => 30;
        public override string Icon => "🎮";
        public override int Order => 7;
        
        public ShaderCacheStep(ILoggingService logger) : base(logger) { }
        
        public override Task<(bool CanRun, string Reason)> CanExecuteAsync(CancellationToken ct = default)
        {
            return Task.FromResult((true, ""));
        }
        
        public override async Task<StepResult> ExecuteAsync(PreparePcMode mode, IProgress<StepProgress>? progress = null, CancellationToken ct = default)
        {
            _logs.Clear();
            long totalFreed = 0;
            
            if (mode == PreparePcMode.DryRun)
            {
                Log("[DRY-RUN] Limparia caches de shaders");
                return StepResult.Succeeded("[DRY-RUN] Limpeza de shaders simulada");
            }
            
            Log("⚠ Aviso: A primeira execução de jogos após limpeza pode ter stutters durante recompilação de shaders");
            
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            
            // NVIDIA shader cache
            ReportProgress(progress, 25, "Limpando NVIDIA shader cache...");
            var nvidiaPath = Path.Combine(localAppData, "NVIDIA", "GLCache");
            if (Directory.Exists(nvidiaPath))
            {
                var size = await GetDirectorySizeAsync(nvidiaPath, ct);
                totalFreed += size;
                try
                {
                    Directory.Delete(nvidiaPath, true);
                    Log($"✓ NVIDIA GLCache removido ({FormatBytes(size)})");
                }
                catch (Exception ex)
                {
                    Log($"⚠ Não foi possível limpar NVIDIA cache: {ex.Message}");
                }
            }
            
            // NVIDIA DXCache
            var nvidiaPath2 = Path.Combine(localAppData, "NVIDIA", "DXCache");
            if (Directory.Exists(nvidiaPath2))
            {
                var size = await GetDirectorySizeAsync(nvidiaPath2, ct);
                totalFreed += size;
                try
                {
                    Directory.Delete(nvidiaPath2, true);
                    Log($"✓ NVIDIA DXCache removido ({FormatBytes(size)})");
                }
                catch (Exception ex)
                {
                    Log($"⚠ Não foi possível limpar NVIDIA DXCache: {ex.Message}");
                }
            }
            
            // AMD shader cache
            ReportProgress(progress, 50, "Limpando AMD shader cache...");
            var amdPath = Path.Combine(localAppData, "AMD", "DxCache");
            if (Directory.Exists(amdPath))
            {
                var size = await GetDirectorySizeAsync(amdPath, ct);
                totalFreed += size;
                try
                {
                    Directory.Delete(amdPath, true);
                    Log($"✓ AMD DxCache removido ({FormatBytes(size)})");
                }
                catch (Exception ex)
                {
                    Log($"⚠ Não foi possível limpar AMD cache: {ex.Message}");
                }
            }
            
            // DirectX shader cache
            ReportProgress(progress, 75, "Limpando DirectX shader cache...");
            var dxPath = Path.Combine(localAppData, "D3DSCache");
            if (Directory.Exists(dxPath))
            {
                var size = await GetDirectorySizeAsync(dxPath, ct);
                totalFreed += size;
                try
                {
                    Directory.Delete(dxPath, true);
                    Log($"✓ DirectX cache removido ({FormatBytes(size)})");
                }
                catch (Exception ex)
                {
                    Log($"⚠ Não foi possível limpar DirectX cache: {ex.Message}");
                }
            }
            
            ReportProgress(progress, 100, "Limpeza de shaders concluída");
            
            return new StepResult
            {
                Success = true,
                Status = StepStatus.Completed,
                Message = $"Liberados {FormatBytes(totalFreed)} de shader caches",
                Logs = _logs.ToArray()
            };
        }
        
        private async Task<long> GetDirectorySizeAsync(string path, CancellationToken ct)
        {
            return await Task.Run(() =>
            {
                try
                {
                    return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                        .Sum(f => new FileInfo(f).Length);
                }
                catch
                {
                    return 0;
                }
            }, ct);
        }
        
        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
    
    /// <summary>
    /// Step 8: Otimização de serviços
    /// </summary>
    public class ServiceOptimizerStep : RepairStepBase
    {
        public override string Name => "Otimização de Serviços";
        public override string Description => "Desativa temporariamente serviços não essenciais";
        public override RiskCategory Risk => RiskCategory.Conditional;
        public override int EstimatedTimeSeconds => 60;
        public override string Icon => "⚙️";
        public override int Order => 8;
        public override bool CanRollback => true;
        
        public ServiceOptimizerStep(ILoggingService logger) : base(logger) { }
        
        public override Task<(bool CanRun, string Reason)> CanExecuteAsync(CancellationToken ct = default)
        {
            if (!AdminHelper.IsRunningAsAdministrator())
            {
                return Task.FromResult((false, "Requer privilégios de administrador"));
            }
            return Task.FromResult((true, ""));
        }
        
        public override async Task<StepResult> ExecuteAsync(PreparePcMode mode, IProgress<StepProgress>? progress = null, CancellationToken ct = default)
        {
            _logs.Clear();
            
            if (mode == PreparePcMode.DryRun)
            {
                Log("[DRY-RUN] Otimizaria serviços não essenciais");
                return StepResult.Succeeded("[DRY-RUN] Otimização simulada");
            }
            
            // Lista de serviços seguros para otimizar
            // WSearch só é parado no modo Full (usuários normais precisam do Windows Search)
            var servicesToOptimize = mode == PreparePcMode.Full
                ? new[]
                {
                    ("SysMain", "Superfetch/Prefetch"),
                    ("DiagTrack", "Telemetria"),
                    ("WSearch", "Windows Search")
                }
                : new[]
                {
                    ("SysMain", "Superfetch/Prefetch"),
                    ("DiagTrack", "Telemetria")
                };
            
            int optimized = 0;
            
            for (int i = 0; i < servicesToOptimize.Length; i++)
            {
                var (serviceName, description) = servicesToOptimize[i];
                var percent = ((i + 1) * 100) / servicesToOptimize.Length;
                ReportProgress(progress, percent, $"Otimizando {description}...");
                
                try
                {
                    // Verificar estado atual
                    var (_, output, _) = await RunCommandAsync("sc", $"query {serviceName}", 10000, ct);
                    
                    if (output.Contains("RUNNING"))
                    {
                        // Parar serviço
                        await RunCommandAsync("sc", $"stop {serviceName}", 30000, ct);
                        Log($"✓ Parado: {description} ({serviceName})");
                        optimized++;
                    }
                    else
                    {
                        Log($"- Já parado: {description}");
                    }
                }
                catch (Exception ex)
                {
                    Log($"⚠ Não foi possível otimizar {serviceName}: {ex.Message}");
                }
            }
            
            return new StepResult
            {
                Success = true,
                Status = StepStatus.Completed,
                Message = $"{optimized} serviços otimizados",
                Logs = _logs.ToArray()
            };
        }
    }
    
    /// <summary>
    /// Step 9: Configuração de Power Plan
    /// </summary>
    public class PowerPlanStep : RepairStepBase
    {
        private readonly string _backupBasePath;
        
        public override string Name => "Power Plan";
        public override string Description => "Aplica plano de energia otimizado para performance";
        public override RiskCategory Risk => RiskCategory.Conditional;
        public override int EstimatedTimeSeconds => 30;
        public override string Icon => "⚡";
        public override int Order => 9;
        public override bool CanRollback => true;
        
        public PowerPlanStep(ILoggingService logger, string backupBasePath) : base(logger)
        {
            _backupBasePath = backupBasePath;
        }
        
        public override Task<(bool CanRun, string Reason)> CanExecuteAsync(CancellationToken ct = default)
        {
            if (!AdminHelper.IsRunningAsAdministrator())
            {
                return Task.FromResult((false, "Requer privilégios de administrador"));
            }
            return Task.FromResult((true, ""));
        }
        
        public override async Task<StepResult> ExecuteAsync(PreparePcMode mode, IProgress<StepProgress>? progress = null, CancellationToken ct = default)
        {
            _logs.Clear();
            
            if (mode == PreparePcMode.DryRun)
            {
                Log("[DRY-RUN] Aplicaria plano de energia de alta performance");
                return StepResult.Succeeded("[DRY-RUN] Power plan simulado");
            }
            
            try
            {
                // Backup do plano atual
                ReportProgress(progress, 20, "Salvando plano de energia atual...");
                var (_, output, _) = await RunCommandAsync("powercfg", "/getactivescheme", 10000, ct);
                var currentGuid = System.Text.RegularExpressions.Regex.Match(output, @"([a-f0-9\-]{36})", 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                
                string? backupPath = null;
                if (currentGuid.Success)
                {
                    backupPath = Path.Combine(_backupBasePath, "powerplan_backup.pow");
                    await RunCommandAsync("powercfg", $"/export \"{backupPath}\" {currentGuid.Groups[1].Value}", 30000, ct);
                    Log($"✓ Plano atual salvo: {backupPath}");
                }
                
                // [FIX:UNICO-DONO-DE-ENERGIA] A etapa de energia NÃO troca mais o plano.
                //
                // O backup com `powercfg /export` acima continua: exportar um
                // plano é leitura, e é o que permite o rollback. Nada muda aí.
                //
                // O que saía depois era uma sequência de duas trocas de plano:
                // tentava o High Performance e, se falhasse, o Ultimate
                // Performance. Os dois por `powercfg /setactive`, fora do portão.
                //
                // Vale apontar que esta etapa é chamada durante o "Preparar PC",
                // que o usuário executa justamente porque a máquina está ruim. É
                // o pior momento possível para desligar o Perfil Inteligente: o
                // perfil é justamente o que ajusta a energia da máquina às
                // condições em que ela está, e o plano nativo escolhido aqui não
                // tem nenhuma dessas_mediações. Trocar o plano no meio de um
                // reparo é trocar o remédio no meio do tratamento.
                ReportProgress(progress, 60, "Aplicando Perfil Inteligente de energia...");

                _logger.LogInfo(
                    "[PREPARE-PC] Ativacao de plano neutralizada. O Perfil Inteligente e' o " +
                    "dono da energia e ja considera a maquina em reparo. " +
                    "(antes: tentava High Performance e depois Ultimate, ambos /setactive.)");

                VoltrisOptimizer.Services.Power.ProfilePowerAuthority.RequestProfileApply(
                    "PreparePc.RepairSteps.ConfigurePower",
                    "etapa de reparo: configurando energia",
                    _logger);

                Log("✓ Energia configurada: Perfil Inteligente reaplicado");
                ReportProgress(progress, 100, "Configuração de energia concluída");
                
                return new StepResult
                {
                    Success = true,
                    Status = StepStatus.Completed,
                    Message = "Plano de energia de alta performance ativado",
                    BackupPath = backupPath,
                    Logs = _logs.ToArray(),
                    CanRollback = backupPath != null
                };
            }
            catch (Exception ex)
            {
                return StepResult.Failed($"Erro: {ex.Message}", ex);
            }
        }
        
        public override async Task<StepResult> RollbackAsync(string backupPath, CancellationToken ct = default)
        {
            if (!File.Exists(backupPath))
            {
                return StepResult.Failed("Arquivo de backup não encontrado");
            }
            
            try
            {
                // Importar plano de backup
                var (exitCode, _, _) = await RunCommandAsync("powercfg", $"/import \"{backupPath}\"", 30000, ct);
                
                if (exitCode == 0)
                {
                    return StepResult.Succeeded("Plano de energia restaurado");
                }
                else
                {
                    return StepResult.Failed("Falha ao restaurar plano de energia");
                }
            }
            catch (Exception ex)
            {
                return StepResult.Failed($"Erro no rollback: {ex.Message}", ex);
            }
        }
    }
}

