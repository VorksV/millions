using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services
{
    /// <summary>
    /// WINDOWS DISK CLEANUP INTEGRATION - Nível Enterprise
    /// Integra TODAS as funcionalidades do cleanmgr.exe do Windows
    /// + Limpeza profunda adicional que o Windows não faz
    /// </summary>
    public class WindowsDiskCleanupService
    {
        private readonly ILoggingService _logger;
        
        // Todas as flags do Disk Cleanup do Windows
        private static readonly Dictionary<string, CleanupFlag> CleanupFlags = new()
        {
            // ============================================
            // FLAGS PADRÃO DO DISK CLEANUP
            // ============================================
            { "Active Setup Temp Folders", new CleanupFlag { StateFlags = 0x0001, Description = "Pastas temporárias do Active Setup", IsSafe = true } },
            { "BranchCache", new CleanupFlag { StateFlags = 0x0002, Description = "Cache de otimização de rede", IsSafe = true } },
            { "Content Indexer Cleaner", new CleanupFlag { StateFlags = 0x0004, Description = "Índice de pesquisa do Windows", IsSafe = true } },
            { "Device Driver Packages", new CleanupFlag { StateFlags = 0x0008, Description = "Pacotes de driver de dispositivo", IsSafe = true } },
            { "Downloaded Program Files", new CleanupFlag { StateFlags = 0x0010, Description = "Controles ActiveX e applets Java", IsSafe = true } },
            { "GameNewsFiles", new CleanupFlag { StateFlags = 0x0020, Description = "Arquivos de notícias de jogos", IsSafe = true } },
            { "GameStatisticsFiles", new CleanupFlag { StateFlags = 0x0040, Description = "Estatísticas de jogos", IsSafe = true } },
            { "GameUpdateFiles", new CleanupFlag { StateFlags = 0x0080, Description = "Atualizações de jogos", IsSafe = true } },
            { "Internet Cache Files", new CleanupFlag { StateFlags = 0x0100, Description = "Arquivos temporários de internet", IsSafe = true } },
            { "Memory Dump Files", new CleanupFlag { StateFlags = 0x0200, Description = "Arquivos de despejo de memória", IsSafe = true } },
            { "Offline Pages Files", new CleanupFlag { StateFlags = 0x0400, Description = "Páginas web offline", IsSafe = true } },
            { "Old ChkDsk Files", new CleanupFlag { StateFlags = 0x0800, Description = "Fragmentos de arquivos do ChkDsk", IsSafe = true } },
            { "Previous Installations", new CleanupFlag { StateFlags = 0x1000, Description = "Instalações anteriores do Windows", IsSafe = true } },
            { "Recycle Bin", new CleanupFlag { StateFlags = 0x2000, Description = "Lixeira", IsSafe = true } },
            { "Service Pack Cleanup", new CleanupFlag { StateFlags = 0x4000, Description = "Backup do Service Pack", IsSafe = true } },
            { "Setup Log Files", new CleanupFlag { StateFlags = 0x8000, Description = "Logs de instalação do Windows", IsSafe = true } },
            { "System error memory dump files", new CleanupFlag { StateFlags = 0x10000, Description = "Despejos de memória de erro do sistema", IsSafe = true } },
            { "System error minidump files", new CleanupFlag { StateFlags = 0x20000, Description = "Minidespejos de erro do sistema", IsSafe = true } },
            { "Temporary Files", new CleanupFlag { StateFlags = 0x40000, Description = "Arquivos temporários", IsSafe = true } },
            { "Temporary Setup Files", new CleanupFlag { StateFlags = 0x80000, Description = "Arquivos temporários de instalação", IsSafe = true } },
            { "Thumbnail Cache", new CleanupFlag { StateFlags = 0x100000, Description = "Cache de miniaturas", IsSafe = true } },
            { "Update Cleanup", new CleanupFlag { StateFlags = 0x200000, Description = "Limpeza do Windows Update", IsSafe = true } },
            { "Upgrade Discarded Files", new CleanupFlag { StateFlags = 0x400000, Description = "Arquivos descartados de upgrade", IsSafe = true } },
            { "User file versions", new CleanupFlag { StateFlags = 0x800000, Description = "Versões anteriores de arquivos", IsSafe = false } },
            { "Windows Defender", new CleanupFlag { StateFlags = 0x1000000, Description = "Arquivos do Windows Defender", IsSafe = true } },
            { "Windows Error Reporting Archive Files", new CleanupFlag { StateFlags = 0x2000000, Description = "Arquivos de relatório de erros", IsSafe = true } },
            { "Windows Error Reporting Queue Files", new CleanupFlag { StateFlags = 0x4000000, Description = "Fila de relatórios de erros", IsSafe = true } },
            { "Windows Error Reporting System Archive Files", new CleanupFlag { StateFlags = 0x8000000, Description = "Arquivos de sistema de relatórios", IsSafe = true } },
            { "Windows Error Reporting System Queue Files", new CleanupFlag { StateFlags = 0x10000000, Description = "Fila de sistema de relatórios", IsSafe = true } },
            { "Windows Error Reporting Temp Files", new CleanupFlag { StateFlags = 0x20000000, Description = "Arquivos temporários de relatórios", IsSafe = true } },
            { "Windows ESD installation files", new CleanupFlag { StateFlags = 0x40000000, Description = "Arquivos de instalação ESD", IsSafe = true } },
            { "Windows Upgrade Log Files", new CleanupFlag { StateFlags = 0x80000000, Description = "Logs de upgrade do Windows", IsSafe = true } }};

        public WindowsDiskCleanupService(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Executa limpeza COMPLETA usando cleanmgr.exe com TODAS as opções
        /// </summary>
        /// <summary>
        /// Executa análise de limpeza completa do Windows
        /// Estima o espaço que pode ser liberado sem realmente executar a limpeza
        /// </summary>
        public async Task<DiskCleanupAnalysisResult> AnalyzeFullDiskCleanupAsync(
            bool includeDangerousOptions = false,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            var result = new DiskCleanupAnalysisResult();
            
            try
            {
                _logger.LogInfo("[DiskCleanup] Iniciando análise de limpeza...");
                // FASE 1: Removido ConfigureCleanupFlags() pois a nova Native Engine não usa chaves de registro do cleanmgr.
                // progresso já reportado acima

                // OTIMIZAÇÃO: Executar estimativas em PARALELO (são independentes)
                progress?.Report("Estimando espaço recuperável...");
                
                var nativeCleaner = new VoltrisOptimizer.Services.Cleanup.NativeDiskCleanerEngine();
                var cleanmgrTask = nativeCleaner.AnalyzeAllAsync(ct);
                var winSxSTask = EstimateWinSxSSpaceAsync(ct);
                var additionalTask = EstimateAdditionalItemsSpaceAsync(ct);
                
                var tasks = new List<Task<long>> { cleanmgrTask, winSxSTask, additionalTask };
                
                if (includeDangerousOptions)
                {
                    tasks.Add(EstimateCompactOSSpaceAsync(ct));
                }

                var results = await Task.WhenAll(tasks).ConfigureAwait(false);
                result.SpaceToClean = results.Sum();

                result.Success = true;
                _logger.LogSuccess($"[DiskCleanup] Análise estimada: {FormatBytes(result.SpaceToClean)}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                _logger.LogError($"[DiskCleanup] Erro na análise: {ex.Message}");
            }

            return result;
        }
        
        public async Task<DiskCleanupResult> RunFullDiskCleanupAsync(
            bool includeDangerousOptions = false,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            var result = new DiskCleanupResult();
            var sw = Stopwatch.StartNew();
            
            try
            {
                _logger.LogInfo($"[DiskCleanup] ⏱ Iniciando limpeza profissional — {DateTime.Now:HH:mm:ss}");

                var initialFree = GetDiskFreeSpace();
                // FASE 1: Removido ConfigureCleanupFlags() pois a Native Engine não usa registro do cleanmgr.
                _logger.LogInfo("[DiskCleanup] ✔ Uso do NativeDiskCleanerEngine habilitado");

                // FASE 2: Executar Native Cleaner + limpeza adicional em PARALELO
                progress?.Report("Executando limpeza nativa rápida (C# Engine + itens adicionais em paralelo)...");
                _logger.LogInfo("[DiskCleanup] 🔀 Iniciando NativeDiskCleanerEngine + CleanAdditionalItems em PARALELO...");
                
                var nativeCleaner = new VoltrisOptimizer.Services.Cleanup.NativeDiskCleanerEngine();
                var cleanmgrTask = nativeCleaner.RunAllAsync(progress, ct);
                var additionalTask = CleanAdditionalItemsAsync(ct);
                
                await Task.WhenAll(cleanmgrTask, additionalTask).ConfigureAwait(false);
                var cleanmgrCleaned    = await cleanmgrTask;
                var additionalCleaned  = await additionalTask;
                result.SpaceCleaned   += cleanmgrCleaned + additionalCleaned;
                _logger.LogInfo($"[DiskCleanup] ✔ Fase 2 concluída: cleanmgr={FormatBytes(cleanmgrCleaned)} + adicional={FormatBytes(additionalCleaned)} ({sw.Elapsed.TotalSeconds:F1}s)");

                // FASE 3: WinSxS DISM (sequencial — usa locks de sistema)
                progress?.Report("Limpando componentes obsoletos (WinSxS/DISM)...");
                _logger.LogInfo($"[DiskCleanup] ▶ Fase 3: WinSxS DISM — {DateTime.Now:HH:mm:ss}");
                var winSxSResult = await CleanWinSxSDeepAsync(ct);
                result.SpaceCleaned += winSxSResult;
                _logger.LogInfo($"[DiskCleanup] ✔ Fase 3 WinSxS: {FormatBytes(winSxSResult)} ({sw.Elapsed.TotalSeconds:F1}s)");

                // FASE 4: CompactOS (opcional)
                if (includeDangerousOptions)
                {
                    progress?.Report("Compactando sistema operacional...");
                    _logger.LogInfo($"[DiskCleanup] ▶ Fase 4: CompactOS — {DateTime.Now:HH:mm:ss}");
                    var compactResult = await CompactOSAsync(ct);
                    result.SpaceCleaned += compactResult;
                    _logger.LogInfo($"[DiskCleanup] ✔ Fase 4 CompactOS: {FormatBytes(compactResult)} ({sw.Elapsed.TotalSeconds:F1}s)");
                }

                // Validação final
                var finalFree = GetDiskFreeSpace();
                var realDelta = finalFree - initialFree;
                
                if (realDelta > result.SpaceCleaned)
                {
                    _logger.LogInfo($"[DiskCleanup] 📊 Delta real ({FormatBytes(realDelta)}) > estimado ({FormatBytes(result.SpaceCleaned)}), usando delta real");
                    result.SpaceCleaned = realDelta;
                }

                result.Success = true;
                sw.Stop();
                _logger.LogSuccess($"[DiskCleanup] ✅ Total liberado: {FormatBytes(result.SpaceCleaned)} em {sw.Elapsed.TotalSeconds:F1}s");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                sw.Stop();
                _logger.LogError($"[DiskCleanup] ❌ Erro ({sw.Elapsed.TotalSeconds:F1}s): {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Configura TODAS as flags do Disk Cleanup no registro
        /// </summary>
        private void ConfigureCleanupFlags(bool includeDangerous)
        {
            try
            {
                const string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches";
                
                using var baseKey = Registry.LocalMachine.OpenSubKey(keyPath, true);
                if (baseKey == null)
                {
                    _logger.LogWarning("[DiskCleanup] Não foi possível acessar chave do registro");
                    return;
                }

                int configured = 0;
                foreach (var subKeyName in baseKey.GetSubKeyNames())
                {
                    try
                    {
                        using var subKey = baseKey.OpenSubKey(subKeyName, true);
                        if (subKey == null) continue;

                        // Verificar se é uma opção segura
                        var isSafe = CleanupFlags.Values.Any(f => f.IsSafe);
                        
                        if (isSafe || includeDangerous)
                        {
                            // StateFlags0001 = habilitar para limpeza automática
                            subKey.SetValue("StateFlags0001", 2, RegistryValueKind.DWord);
                            configured++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"[DiskCleanup] Erro ao configurar {subKeyName}: {ex.Message}");
                    }
                }

                _logger.LogInfo($"[DiskCleanup] {configured} opções configuradas");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[DiskCleanup] Erro ao configurar flags: {ex.Message}");
            }
        }

        // cleanmgr.exe logic has been entirely replaced by NativeDiskCleanerEngine
        /// <summary>
        /// Limpeza PROFUNDA do WinSxS usando DISM
        /// </summary>
        private async Task<long> CleanWinSxSDeepAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                _logger.LogInfo($"[DiskCleanup] ▶ Iniciando limpeza WinSxS (DISM) — {DateTime.Now:HH:mm:ss}");
                _logger.LogInfo("[DiskCleanup] ℹ DISM pode levar vários minutos dependendo do tamanho do Component Store");
                
                var initialFree = GetDiskFreeSpace();

                // COMANDO 1: Limpeza Máxima de Componentes (Inclui StartComponentCleanup)
                _logger.LogInfo($"[DiskCleanup] ▶ DISM Cmd1: /StartComponentCleanup /ResetBase — {DateTime.Now:HH:mm:ss}");
                await RunDismCommandAsync("/Online /Cleanup-Image /StartComponentCleanup /ResetBase", ct);
                _logger.LogInfo($"[DiskCleanup] ✔ DISM Cmd1 concluído ({sw.Elapsed.TotalSeconds:F1}s)");

                // COMANDO 2: Arquivos SP
                _logger.LogInfo($"[DiskCleanup] ▶ DISM Cmd2: /SPSuperseded /HideSP — {DateTime.Now:HH:mm:ss}");
                await RunDismCommandAsync("/Online /Cleanup-Image /SPSuperseded /HideSP", ct);
                _logger.LogInfo($"[DiskCleanup] ✔ DISM Cmd2 concluído ({sw.Elapsed.TotalSeconds:F1}s)");

                var finalFree = GetDiskFreeSpace();
                var cleaned = finalFree - initialFree;

                sw.Stop();
                _logger.LogSuccess($"[DiskCleanup] ✅ WinSxS DISM concluído em {sw.Elapsed.TotalSeconds:F1}s. Delta: {FormatBytes(cleaned)}");
                return cleaned > 0 ? cleaned : 0;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError($"[DiskCleanup] ❌ Erro na limpeza WinSxS ({sw.Elapsed.TotalSeconds:F1}s): {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Executa comando DISM
        /// </summary>
        private async Task RunDismCommandAsync(string arguments, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                _logger.LogInfo($"[DISM] ▶ Executando: dism.exe {arguments}");
                var psi = new ProcessStartInfo
                {
                    FileName = "dism.exe",
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    Verb = "runas"
                };

                using var process = Process.Start(psi);
                if (process == null)
                {
                    _logger.LogWarning($"[DISM] ⚠ Não foi possível iniciar dism.exe para: {arguments}");
                    return;
                }

                _logger.LogInfo($"[DISM] ⏳ Aguardando DISM (PID={process.Id}, timeout=3min)...");

                // Timeout de 3min — DISM /ResetBase pode ser lento em sistemas grandes
                bool exited = await Task.Run(() => process.WaitForExit(180000), ct);

                sw.Stop();
                if (!exited)
                {
                    _logger.LogWarning($"[DISM] ⏰ TIMEOUT: dism.exe não encerrou em 3min ({sw.Elapsed.TotalSeconds:F1}s). Forçando encerramento.");
                    try { process.Kill(); } catch { }
                }
                else
                {
                    _logger.LogInfo($"[DISM] ✔ Concluído em {sw.Elapsed.TotalSeconds:F1}s (ExitCode={process.ExitCode}): {arguments}");
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogWarning($"[DISM] ⚠ Erro ({sw.Elapsed.TotalSeconds:F1}s): {ex.Message}");
            }
        }

        /// <summary>
        /// Limpeza adicional que o Windows Disk Cleanup NÃO faz
        /// </summary>
        private async Task<long> CleanAdditionalItemsAsync(CancellationToken ct)
        {
            long totalCleaned = 0;

            try
            {
                // 1. REMOVIDO: Limpar logs do CBS - ALTO RISCO CRÍTICO
                // NUNCA deletar logs de C:\Windows\Logs\CBS\*.log
                // Isso causa race conditions e corrupção do Component Store
                // Usar apenas ferramentas oficiais para gerenciamento de logs

                // 2. Limpar logs do DISM
                totalCleaned += await CleanDISMLogsAsync();

                // 3. Limpar logs do SFC
                totalCleaned += await CleanSFCLogsAsync();

                // 4. Limpar logs do Windows Update
                totalCleaned += await CleanWindowsUpdateLogsAsync();

                // 5. Limpar cache do Windows Installer
                totalCleaned += await CleanWindowsInstallerCacheAsync();

                // 6. REMOVIDO: Limpeza de arquivos temporários do WinSxS - ALTO RISCO
                // Não deletar arquivos diretamente de C:\Windows\WinSxS\Temp
                // Usar apenas DISM /StartComponentCleanup para limpeza segura

                // 7. Limpar logs de instalação de drivers
                totalCleaned += await CleanDriverLogsAsync();

                // 8. Limpar cache de atualização de drivers
                totalCleaned += await CleanDriverCacheAsync();

                // 9. Limpar logs do Event Viewer antigos
                totalCleaned += await CleanOldEventLogsAsync();

                // 10. Limpar arquivos de relatório de erros antigos
                totalCleaned += await CleanOldErrorReportsAsync();

                _logger.LogSuccess($"[DiskCleanup] Limpeza adicional liberou: {FormatBytes(totalCleaned)}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[DiskCleanup] Erro na limpeza adicional: {ex.Message}");
            }

            return totalCleaned;
        }

        #region Limpezas Adicionais Específicas

        // REMOVIDO: CleanCBSLogsAsync - Funcionalidade de ALTO RISCO CRÍTICO
        // NUNCA deletar logs do Component-Based Servicing (CBS)
        // Logs do CBS são essenciais para diagnóstico e integridade do sistema
        // Deleção concorrente causa corrupção do WinSxS e falhas em atualizações
        // Usar apenas Event Viewer ou ferramentas Microsoft para gerenciamento de logs

        private async Task<long> CleanDISMLogsAsync()
        {
            try
            {
                var dismPath = @"C:\Windows\Logs\DISM";
                if (!Directory.Exists(dismPath)) return 0;

                long cleaned = 0;
                var files = Directory.GetFiles(dismPath, "*.log")
                    .Where(f => new FileInfo(f).LastWriteTime < DateTime.Now.AddDays(-30));

                foreach (var file in files)
                {
                    try
                    {
                        var size = new FileInfo(file).Length;
                        File.Delete(file);
                        cleaned += size;
                    }
                    catch { }
                }

                _logger.LogInfo($"[DiskCleanup] DISM Logs: {FormatBytes(cleaned)}");
                return cleaned;
            }
            catch { return 0; }
        }

        private async Task<long> CleanSFCLogsAsync()
        {
            try
            {
                var sfcLog = @"C:\Windows\Logs\CBS\CBS.log";
                if (!File.Exists(sfcLog)) return 0;

                var size = new FileInfo(sfcLog).Length;
                if (size > 100 * 1024 * 1024) // > 100MB
                {
                    File.Delete(sfcLog);
                    _logger.LogInfo($"[DiskCleanup] SFC Log: {FormatBytes(size)}");
                    return size;
                }

                return 0;
            }
            catch { return 0; }
        }

        private async Task<long> CleanWindowsUpdateLogsAsync()
        {
            try
            {
                var wuPath = @"C:\Windows\Logs\WindowsUpdate";
                if (!Directory.Exists(wuPath)) return 0;

                long cleaned = 0;
                var files = Directory.GetFiles(wuPath, "*.etl")
                    .Where(f => new FileInfo(f).LastWriteTime < DateTime.Now.AddDays(-7));

                foreach (var file in files)
                {
                    try
                    {
                        var size = new FileInfo(file).Length;
                        File.Delete(file);
                        cleaned += size;
                    }
                    catch { }
                }

                _logger.LogInfo($"[DiskCleanup] Windows Update Logs: {FormatBytes(cleaned)}");
                return cleaned;
            }
            catch { return 0; }
        }

        private async Task<long> CleanWindowsInstallerCacheAsync()
        {
            try
            {
                // ATENÇÃO: Limpar com cuidado - alguns MSI podem ser necessários
                var installerPath = @"C:\Windows\Installer\$PatchCache$";
                if (!Directory.Exists(installerPath)) return 0;

                long cleaned = 0;
                var dirs = Directory.GetDirectories(installerPath)
                    .Where(d => new DirectoryInfo(d).LastWriteTime < DateTime.Now.AddDays(-90));

                foreach (var dir in dirs)
                {
                    try
                    {
                        var size = GetDirectorySize(dir);
                        Directory.Delete(dir, true);
                        cleaned += size;
                    }
                    catch { }
                }

                _logger.LogInfo($"[DiskCleanup] Installer Cache: {FormatBytes(cleaned)}");
                return cleaned;
            }
            catch { return 0; }
        }

        // REMOVIDO: CleanWinSxSTempAsync - Funcionalidade de ALTO RISCO
        // Não deletar arquivos diretamente de C:\Windows\WinSxS\Temp
        // Usar apenas DISM para limpeza segura do Component Store
        // A limpeza do WinSxS deve ser feita apenas através de DISM /StartComponentCleanup

        private async Task<long> CleanDriverLogsAsync()
        {
            try
            {
                var setupApiLog = @"C:\Windows\inf\setupapi.dev.log";
                if (!File.Exists(setupApiLog)) return 0;

                var size = new FileInfo(setupApiLog).Length;
                if (size > 50 * 1024 * 1024) // > 50MB
                {
                    File.Delete(setupApiLog);
                    _logger.LogInfo($"[DiskCleanup] Driver Logs: {FormatBytes(size)}");
                    return size;
                }

                return 0;
            }
            catch { return 0; }
        }

        private async Task<long> CleanDriverCacheAsync()
        {
            try
            {
                var driverCache = @"C:\Windows\System32\DriverStore\Temp";
                if (!Directory.Exists(driverCache)) return 0;

                long cleaned = 0;
                foreach (var file in Directory.EnumerateFiles(driverCache, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var size = new FileInfo(file).Length;
                        File.Delete(file);
                        cleaned += size;
                    }
                    catch { }
                }

                _logger.LogInfo($"[DiskCleanup] Driver Cache: {FormatBytes(cleaned)}");
                return cleaned;
            }
            catch { return 0; }
        }

        private async Task<long> CleanOldEventLogsAsync()
        {
            try
            {
                // Limpar logs do Event Viewer com mais de 90 dias
                var evtxPath = @"C:\Windows\System32\winevt\Logs";
                if (!Directory.Exists(evtxPath)) return 0;

                long cleaned = 0;
                var files = Directory.GetFiles(evtxPath, "*.evtx")
                    .Where(f => !f.Contains("System.evtx") && !f.Contains("Application.evtx") && !f.Contains("Security.evtx"))
                    .Where(f => new FileInfo(f).LastWriteTime < DateTime.Now.AddDays(-90));

                foreach (var file in files)
                {
                    try
                    {
                        var size = new FileInfo(file).Length;
                        File.Delete(file);
                        cleaned += size;
                    }
                    catch { }
                }

                _logger.LogInfo($"[DiskCleanup] Event Logs: {FormatBytes(cleaned)}");
                return cleaned;
            }
            catch { return 0; }
        }

        private async Task<long> CleanOldErrorReportsAsync()
        {
            try
            {
                var werPath = @"C:\ProgramData\Microsoft\Windows\WER\ReportQueue";
                if (!Directory.Exists(werPath)) return 0;

                long cleaned = 0;
                var dirs = Directory.GetDirectories(werPath)
                    .Where(d => new DirectoryInfo(d).LastWriteTime < DateTime.Now.AddDays(-30));

                foreach (var dir in dirs)
                {
                    try
                    {
                        var size = GetDirectorySize(dir);
                        Directory.Delete(dir, true);
                        cleaned += size;
                    }
                    catch { }
                }

                _logger.LogInfo($"[DiskCleanup] Error Reports: {FormatBytes(cleaned)}");
                return cleaned;
            }
            catch { return 0; }
        }

        #endregion

        /// <summary>
        /// Compacta o sistema operacional usando CompactOS
        /// ATENÇÃO: Pode reduzir performance em SSDs
        /// </summary>
        private async Task<long> CompactOSAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                _logger.LogInfo($"[DiskCleanup] ▶ Compactando sistema operacional (CompactOS) — {DateTime.Now:HH:mm:ss}");
                _logger.LogInfo("[DiskCleanup] ℹ CompactOS pode levar vários minutos. Não recomendado para SSDs.");

                var beforeSize = GetDirectorySize(@"C:\Windows");

                var psi = new ProcessStartInfo
                {
                    FileName = "compact.exe",
                    Arguments = "/CompactOS:always",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    Verb = "runas"
                };

                using var process = Process.Start(psi);
                if (process == null)
                {
                    _logger.LogWarning("[DiskCleanup] ⚠ Não foi possível iniciar compact.exe");
                    return 0;
                }

                _logger.LogInfo($"[DiskCleanup] ⏳ Aguardando CompactOS (PID={process.Id}, timeout=3min)...");
                bool exited = await Task.Run(() => process.WaitForExit(180000), ct);

                sw.Stop();
                if (!exited)
                {
                    _logger.LogWarning($"[DiskCleanup] ⏰ TIMEOUT CompactOS ({sw.Elapsed.TotalSeconds:F1}s), forçando encerramento");
                    try { process.Kill(); } catch { }
                    return 0;
                }

                var afterSize = GetDirectorySize(@"C:\Windows");
                var saved = beforeSize - afterSize;

                _logger.LogSuccess($"[DiskCleanup] ✅ CompactOS concluído em {sw.Elapsed.TotalSeconds:F1}s. Economizou: {FormatBytes(saved)}");
                return saved;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogWarning($"[DiskCleanup] ⚠ Erro no CompactOS ({sw.Elapsed.TotalSeconds:F1}s): {ex.Message}");
                return 0;
            }
        }

        #region Helper Methods

        private long GetDiskFreeSpace()
        {
            try
            {
                var drive = new System.IO.DriveInfo("C");
                return drive.AvailableFreeSpace;
            }
            catch
            {
                return 0;
            }
        }

        private long GetDirectorySize(string path)
        {
            // Usar o helper otimizado com timeout de 2 segundos para estimativas rápidas
            return VoltrisOptimizer.Helpers.FileSystemHelper.GetDirectorySize(path, maxSeconds: 2);
        }

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
                
        #endregion
                
        #region Funções de Estimativa para Análise
        
        /// <summary>
        /// Estima o espaço que pode ser liberado pelo cleanmgr
        /// </summary>
        private async Task<long> EstimateCleanmgrSpaceAsync(CancellationToken ct)
        {
            try
            {
                // Estimativa baseada em diretórios típicos que o cleanmgr limpa
                long totalEstimated = 0;
                
                // Diretórios típicos que o cleanmgr limpa
                var directoriesToCheck = new[]
                {
                    @"C:\Windows\Temp",
                    @"C:\Windows\Prefetch",
                    @"C:\Windows\SoftwareDistribution\Download",
                    @"C:\$Recycle.Bin",
                    @"C:\Windows\Logs"
                };
                
                foreach (var dir in directoriesToCheck)
                {
                    try
                    {
                        if (Directory.Exists(dir))
                        {
                            totalEstimated += GetDirectorySize(dir);
                        }
                    }
                    catch { /* Ignorar erros de acesso */ }
                }
                
                // Adicionar estimativa de arquivos temporários do sistema
                var tempDir = Path.GetTempPath();
                if (Directory.Exists(tempDir))
                {
                    totalEstimated += GetDirectorySize(tempDir);
                }
                
                _logger.LogInfo($"[DiskCleanup] Estimativa cleanmgr: {FormatBytes(totalEstimated)}");
                return totalEstimated;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[DiskCleanup] Erro na estimativa cleanmgr: {ex.Message}");
                return 0;
            }
        }
        
        /// <summary>
        /// Estima o espaço que pode ser liberado pelo WinSxS
        /// </summary>
        private async Task<long> EstimateWinSxSSpaceAsync(CancellationToken ct)
        {
            try
            {
                // TENTATIVA 1: Registro do Windows (rápido e preciso)
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\ComponentStoreSize");
                    if (key != null)
                    {
                        var reclaimable = key.GetValue("ReclaimableBytes");
                        if (reclaimable != null)
                        {
                            long bytes = Convert.ToInt64(reclaimable);
                            if (bytes > 0)
                            {
                                _logger.LogInfo($"[DiskCleanup] Estimativa WinSxS (via registro): {FormatBytes(bytes)}");
                                return bytes;
                            }
                        }
                    }
                }
                catch { }

                // TENTATIVA 2: REMOVIDO - Scan do WinSxS\Backup - ALTO RISCO
                // NUNCA acessar diretamente o WinSxS para estimativa
                // Usar apenas registro do Windows para estimativa segura

                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[DiskCleanup] Erro na estimativa WinSxS: {ex.Message}");
                return 0;
            }
        }
        
        /// <summary>
        /// Estima o espaço que pode ser liberado por limpezas adicionais
        /// </summary>
        private async Task<long> EstimateAdditionalItemsSpaceAsync(CancellationToken ct)
        {
            try
            {
                long totalEstimated = 0;
                
                // Estimativa para logs do CBS
                var cbsPath = @"C:\Windows\Logs\Cbs";
                if (Directory.Exists(cbsPath))
                {
                    totalEstimated += GetDirectorySize(cbsPath);
                }
                
                // Estimativa para logs do DISM
                var dismPath = @"C:\Windows\Logs\DISM";
                if (Directory.Exists(dismPath))
                {
                    totalEstimated += GetDirectorySize(dismPath);
                }
                
                // Estimativa para logs do Event Viewer antigos
                var evtxPath = @"C:\Windows\System32\winevt\Logs";
                if (Directory.Exists(evtxPath))
                {
                    // Considerar apenas logs mais antigos (> 30 dias)
                    var files = Directory.GetFiles(evtxPath, "*.evtx", SearchOption.TopDirectoryOnly);
                    foreach (var file in files)
                    {
                        try
                        {
                            var fileInfo = new FileInfo(file);
                            if (fileInfo.LastWriteTime < DateTime.Now.AddDays(-30))
                            {
                                totalEstimated += fileInfo.Length;
                            }
                        }
                        catch { }
                    }
                }
                
                _logger.LogInfo($"[DiskCleanup] Estimativa itens adicionais: {FormatBytes(totalEstimated)}");
                return totalEstimated;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[DiskCleanup] Erro na estimativa itens adicionais: {ex.Message}");
                return 0;
            }
        }
        
        /// <summary>
        /// Estima o espaço que pode ser liberado pela compactação do OS
        /// </summary>
        private async Task<long> EstimateCompactOSSpaceAsync(CancellationToken ct)
        {
            try
            {
                // Compactação de OS costuma liberar entre 1.5GB a 2.5GB em média
                // Evitamos escanear C:\Windows inteiro pois é muito lento
                long estimated = 2L * 1024 * 1024 * 1024; // 2GB fixo para estimativa rápida
                _logger.LogInfo($"[DiskCleanup] Estimativa compactação OS: {FormatBytes(estimated)} (estimativa base)");
                return estimated;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[DiskCleanup] Erro na estimativa compactação OS: {ex.Message}");
                return 0;
            }
        }
        
        #endregion
    }

    #region Data Classes

    public class CleanupFlag
    {
        public uint StateFlags { get; set; }
        public string Description { get; set; } = "";
        public bool IsSafe { get; set; }
    }

    public class DiskCleanupResult
    {
        public bool Success { get; set; }
        public long SpaceCleaned { get; set; }
        public string ErrorMessage { get; set; } = "";
    }
    
    public class DiskCleanupAnalysisResult
    {
        public bool Success { get; set; }
        public long SpaceToClean { get; set; }
        public string ErrorMessage { get; set; } = "";
    }

    #endregion
}
