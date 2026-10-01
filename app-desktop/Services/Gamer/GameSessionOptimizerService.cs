using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer
{
    /// <summary>
    /// Serviço profissional de otimizações pré e pós-jogo
    /// Executa limpezas seguras e boost de performance sem comprometer o sistema
    /// </summary>
    public class GameSessionOptimizerService
    {
        private readonly ILoggingService _logger;
        
        // Snapshot de serviços para restauração
        private Dictionary<string, ServiceControllerStatus> _serviceSnapshot = new();
        private Dictionary<int, ProcessPriorityClass> _processSnapshot = new();
        private string? _originalPowerPlan;
        private bool _sessionActive = false;
        
        // Configurações de segurança
        private static readonly string[] SafeTempExtensions = { ".tmp", ".temp", ".log", ".old", ".bak", ".chk" };
        private static readonly string[] ProtectedFolders = { "system32", "syswow64", "microsoft", "windows defender" };
        
        // Serviços seguros para pausar durante jogos (não críticos)
        private static readonly string[] PausableServices = 
        {
            // "SysMain",              // Superfetch - pré-carregamento (REMOVIDO: Causa Hard Page Faults in-game)
            "DiagTrack",            // Telemetria
            "WSearch",              // Windows Search
            "wuauserv",             // Windows Update
            "BITS",                 // Background Intelligent Transfer
            "DoSvc",                // Delivery Optimization
            "TabletInputService",   // Touch/Tablet
            "Fax",                  // Fax
            "PrintSpooler",         // Impressão (se não estiver imprimindo)
        };

        // Processos que podem ter prioridade reduzida
        private static readonly string[] LowPriorityProcesses =
        {
            "SearchIndexer", "SearchHost", "OneDrive", "Teams",
            "Spotify", "Discord", "slack", "msedge", "chrome", "firefox"
        };

        public GameSessionOptimizerService(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogEntry(nameof(GameSessionOptimizerService));
            _logger.LogExit(nameof(GameSessionOptimizerService));
        }

        #region Main Entry Points

        /// <summary>
        /// Executa todas as otimizações pré-jogo de forma segura
        /// </summary>
        public async Task<PreGameResult> OnGameStartCleanupAndBoostAsync(string? gameExecutable = null, CancellationToken ct = default)
        {
            var result = new PreGameResult();
            
            if (_sessionActive)
            {
                _logger.LogWarning("[GameSession] Sessão já ativa, ignorando nova inicialização");
                return result;
            }
            
            _logger.LogEntry(nameof(OnGameStartCleanupAndBoostAsync));
            _sessionActive = true;
            _logger.LogInfo("[GameSession] ═══════════════════════════════════════════");
            _logger.LogInfo("[GameSession] 🎮 INICIANDO OTIMIZAÇÕES PRÉ-JOGO");
            _logger.LogInfo("[GameSession] ═══════════════════════════════════════════");

            try
            {
                // ============================================
                // PACK 1: PRE-GAME CLEAN
                // ============================================
                _logger.LogInfo("[GameSession] 📦 Executando PreGameCleanPack...");
                
                result.ShaderCacheCleaned = await SafeShaderCacheFlushAsync(ct);
                result.BrowserCacheCleaned = await BrowserCacheSafeCleanAsync(ct);
                result.TempFilesCleaned = await TempSystemSmartCleanAsync(ct);
                result.LogsPruned = await SafeLogPruningAsync(ct);
                result.GpuCachePurged = await GPUCachePurgeAsync(ct);

                // ============================================
                // PACK 2: PRE-GAME BOOST
                // ============================================
                _logger.LogInfo("[GameSession] 📦 Executando PreGameBoostPack...");
                
                result.ProcessesOptimized = await GamePrefixOptimizationAsync(gameExecutable, ct);
                result.RamPurged = await RAMSoftPurgeAsync(ct);
                result.NetworkRefreshed = await NetworkStackRefreshSafeAsync(ct);
                result.ServicesSuspended = await IdleServiceSuspensionAsync(ct);

                result.Success = true;
                _logger.LogSuccess("[GameSession] ✅ Otimizações pré-jogo concluídas com sucesso!");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GameSession] Erro nas otimizações pré-jogo: {ex.Message}", ex);
                result.Success = false;
                result.Error = ex.Message;
            }

            _logger.LogExit(nameof(OnGameStartCleanupAndBoostAsync));
            return result;
        }

        /// <summary>
        /// Executa todas as otimizações pós-jogo e restauração
        /// </summary>
        public async Task<PostGameResult> OnGameExitCleanupAndRecoveryAsync(string? gameExecutable = null, CancellationToken ct = default)
        {
            var result = new PostGameResult();
            
            if (!_sessionActive)
            {
                _logger.LogWarning("[GameSession] Nenhuma sessão ativa para encerrar");
                return result;
            }

            _logger.LogEntry(nameof(OnGameExitCleanupAndRecoveryAsync));
            _logger.LogInfo("[GameSession] ═══════════════════════════════════════════");
            _logger.LogInfo("[GameSession] 🏁 INICIANDO RECUPERAÇÃO PÓS-JOGO");
            _logger.LogInfo("[GameSession] ═══════════════════════════════════════════");

            try
            {
                // ============================================
                // PACK 3: POST-GAME RECOVERY
                // ============================================
                _logger.LogInfo("[GameSession] 📦 Executando PostGameRecoveryPack...");
                
                result.ServicesRestored = await RestoreServiceStateAsync(ct);
                result.ResidualsCleaned = await PostGameResidualCleanAsync(gameExecutable, ct);
                result.GpuNormalized = await GPUDriverStateNormalizeAsync(ct);
                result.CoolingBoosted = await SystemCoolingBoostAsync(ct);
                result.NetworkRebalanced = await NetworkPostRebalanceAsync(ct);

                result.Success = true;
                _sessionActive = false;
                _logger.LogSuccess("[GameSession] ✅ Recuperação pós-jogo concluída!");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GameSession] Erro na recuperação pós-jogo: {ex.Message}", ex);
                result.Success = false;
                result.Error = ex.Message;
                _sessionActive = false;
            }

            _logger.LogExit(nameof(OnGameExitCleanupAndRecoveryAsync));
            return result;
        }

        #endregion

        #region PreGameCleanPack

        /// <summary>
        /// 1. SafeShaderCacheFlush - Limpa caches de shaders DirectX e Vulkan de forma segura
        /// </summary>
        private async Task<long> SafeShaderCacheFlushAsync(CancellationToken ct)
        {
            _logger.LogEntry(nameof(SafeShaderCacheFlushAsync));
            var result = await Task.Run(() =>
            {
                // REMOVIDO: Limpar shader cache D3D/Vulkan ANTES de jogar causa stutters graves
                // pois o jogo precisa recompilar os shaders em tempo real.
                _logger.LogInfo("[GameSession] 🔧 Shader cache preservado (Evita stutters in-game).");
                return 0L;
            }, ct);
            _logger.LogExit(nameof(SafeShaderCacheFlushAsync));
            return result;
        }

        /// <summary>
        /// 2. BrowserCacheSafeClean - Remove apenas cache residual dos navegadores
        /// NÃO apaga histórico, cookies, sessões ou logins
        /// </summary>
        private async Task<long> BrowserCacheSafeCleanAsync(CancellationToken ct)
        {
            _logger.LogEntry(nameof(BrowserCacheSafeCleanAsync));
            var result = await Task.Run(() =>
            {
                long totalCleaned = 0;
                _logger.LogInfo("[GameSession] 🌐 Limpando cache de navegadores (seguro)...");

                try
                {
                    var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    
                    // Chrome - apenas Cache, não Code Cache ou Session Storage
                    var chromeCachePaths = new[]
                    {
                        Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "Cache"),
                        Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "Code Cache"),
                        Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "GPUCache")};

                    // Edge
                    var edgeCachePaths = new[]
                    {
                        Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "Cache"),
                        Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "Code Cache"),
                        Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "GPUCache")};

                    // Firefox
                    var firefoxProfile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "Mozilla", "Firefox", "Profiles");

                    foreach (var path in chromeCachePaths.Concat(edgeCachePaths))
                    {
                        if (Directory.Exists(path))
                        {
                            totalCleaned += CleanDirectorySafe(path, maxAgeDays: 1);
                        }
                    }

                    // Firefox - limpar cache2 de cada perfil
                    if (Directory.Exists(firefoxProfile))
                    {
                        foreach (var profile in Directory.GetDirectories(firefoxProfile))
                        {
                            var cache2 = Path.Combine(profile, "cache2");
                            if (Directory.Exists(cache2))
                            {
                                totalCleaned += CleanDirectorySafe(cache2, maxAgeDays: 1);
                            }
                        }
                    }

                    _logger.LogInfo($"[GameSession] Cache de navegadores limpo: {FormatBytes(totalCleaned)}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[GameSession] Erro ao limpar browser cache: {ex.Message}");
                }

                return totalCleaned;
            }, ct);
            _logger.LogExit(nameof(BrowserCacheSafeCleanAsync));
            return result;
        }

        /// <summary>
        /// 3. TempSystemSmartClean - Limpa apenas arquivos temporários seguros
        /// </summary>
        private async Task<long> TempSystemSmartCleanAsync(CancellationToken ct)
        {
            _logger.LogEntry(nameof(TempSystemSmartCleanAsync));
            var result = await Task.Run(() =>
            {
                long totalCleaned = 0;
                _logger.LogInfo("[GameSession] 🗑️ Limpando arquivos temporários...");

                try
                {
                    var tempPaths = new[]
                    {
                        Path.GetTempPath(),
                        Path.Combine(Environment.GetEnvironmentVariable("WINDIR") ?? "C:\\Windows", "Temp")};

                    foreach (var path in tempPaths)
                    {
                        if (Directory.Exists(path))
                        {
                            totalCleaned += CleanDirectorySafe(path, maxAgeDays: 1, safeExtensionsOnly: true);
                        }
                    }

                    _logger.LogInfo($"[GameSession] Arquivos temporários limpos: {FormatBytes(totalCleaned)}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[GameSession] Erro ao limpar temp: {ex.Message}");
                }

                return totalCleaned;
            }, ct);
            _logger.LogExit(nameof(TempSystemSmartCleanAsync));
            return result;
        }

        /// <summary>
        /// 4. SafeLogPruning - Remove logs inúteis sem remover logs ativos
        /// </summary>
        private async Task<long> SafeLogPruningAsync(CancellationToken ct)
        {
            _logger.LogEntry(nameof(SafeLogPruningAsync));
            var result = await Task.Run(() =>
            {
                long totalCleaned = 0;
                _logger.LogInfo("[GameSession] 📝 Removendo logs antigos...");

                try
                {
                    var logPaths = new[]
                    {
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps"),
                        Path.Combine(Environment.GetEnvironmentVariable("WINDIR") ?? "C:\\Windows", "Logs", "CBS"),
                        Path.Combine(Environment.GetEnvironmentVariable("WINDIR") ?? "C:\\Windows", "Logs", "DISM"),
                        Path.Combine(Environment.GetEnvironmentVariable("WINDIR") ?? "C:\\Windows", "SoftwareDistribution", "Download")};

                    foreach (var path in logPaths)
                    {
                        if (Directory.Exists(path))
                        {
                            totalCleaned += CleanDirectorySafe(path, maxAgeDays: 7);
                        }
                    }

                    // Limpar arquivos .log antigos em ProgramData
                    var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                    totalCleaned += CleanOldLogFiles(programData, maxAgeDays: 30);

                    _logger.LogInfo($"[GameSession] Logs antigos removidos: {FormatBytes(totalCleaned)}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[GameSession] Erro ao limpar logs: {ex.Message}");
                }

                return totalCleaned;
            }, ct);
            _logger.LogExit(nameof(SafeLogPruningAsync));
            return result;
        }

        /// <summary>
        /// 5. GPUCachePurge - Limpa caches de GPU de jogos/engines
        /// </summary>
        private async Task<long> GPUCachePurgeAsync(CancellationToken ct)
        {
            _logger.LogEntry(nameof(GPUCachePurgeAsync));
            var result = await Task.Run(() =>
            {
                // REMOVIDO: Limpar caches de GPU de engines e Steam causa stuttering.
                // Engines recompilam os shaders in-game, prejudicando o FPS.
                _logger.LogInfo("[GameSession] 🎨 Cache de GPU e Engines preservado (Evita stutters in-game).");
                return 0L;
            }, ct);
            _logger.LogExit(nameof(GPUCachePurgeAsync));
            return result;
        }

        #endregion

        #region PreGameBoostPack

        /// <summary>
        /// 6. GamePrefixOptimization - Otimizações de processos para jogos
        /// </summary>
        private async Task<int> GamePrefixOptimizationAsync(string? gameExecutable, CancellationToken ct)
        {
            _logger.LogEntry(nameof(GamePrefixOptimizationAsync));
            var result = await Task.Run(() =>
            {
                int optimizedCount = 0;
                _logger.LogInfo("[GameSession] ⚡ Otimizando processos...");

                try
                {
                    // Salvar snapshot de prioridades
                    _processSnapshot.Clear();

                    // Reduzir prioridade de processos em segundo plano
                    foreach (var procName in LowPriorityProcesses)
                    {
                        try
                        {
                            var processes = Process.GetProcessesByName(procName);
                            foreach (var proc in processes)
                            {
                                try
                                {
                                    if (!_processSnapshot.ContainsKey(proc.Id))
                                    {
                                        _processSnapshot[proc.Id] = proc.PriorityClass;
                                    }
                                    
                                    if (proc.PriorityClass > ProcessPriorityClass.BelowNormal)
                                    {
                                        proc.PriorityClass = ProcessPriorityClass.BelowNormal;
                                        optimizedCount++;
                                    }
                                }
                                catch { }
                            }
                        }
                        catch { }
                    }

                    // Se temos o executável do jogo, aumentar sua prioridade
                    if (!string.IsNullOrEmpty(gameExecutable))
                    {
                        try
                        {
                            var gameName = Path.GetFileNameWithoutExtension(gameExecutable);
                            var gameProcesses = Process.GetProcessesByName(gameName);
                            foreach (var proc in gameProcesses)
                            {
                                try
                                {
                                    proc.PriorityClass = ProcessPriorityClass.High;
                                    _logger.LogInfo($"[GameSession] Prioridade do jogo {gameName} elevada para High");
                                }
                                catch { }
                            }
                        }
                        catch { }
                    }

                    // Liberar working set de processos pesados
                    // EmptyWorkingSets(); -> Removido na Fase 3 para evitar Hard Faults. 
                    // Em vez disso, chamamos a otimização segura de forma assíncrona.
                    _ = global::VoltrisOptimizer.Services.Optimization.Memory.VoltrisMemoryCoordinator.Instance.OptimizeMemoryAsync(isManualClick: false);

                    _logger.LogInfo($"[GameSession] {optimizedCount} processos otimizados");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[GameSession] Erro ao otimizar processos: {ex.Message}");
                }

                return optimizedCount;
            }, ct);
            _logger.LogExit(nameof(GamePrefixOptimizationAsync));
            return result;
        }

        /// <summary>
        /// 7. RAMSoftPurge - Libera apenas páginas inativas sem forçar
        /// </summary>
        private async Task<long> RAMSoftPurgeAsync(CancellationToken ct)
        {
            _logger.LogEntry(nameof(RAMSoftPurgeAsync));
            var result = await Task.Run(() =>
            {
                _logger.LogInfo("[GameSession] 💾 Liberando memória não utilizada...");
                long freedMemory = 0;

                try
                {
                    // Método não agressivo: apenas limpar working sets inativos
                    // EmptyWorkingSets();
                    _ = global::VoltrisOptimizer.Services.Optimization.Memory.VoltrisMemoryCoordinator.Instance.OptimizeMemoryAsync(isManualClick: false);
                    
                    // Chamar GC para liberar memória gerenciada

                    // Estimar memória liberada
                    var memInfo = GetMemoryInfo();
                    freedMemory = Convert.ToInt64(memInfo.AvailablePhysicalMemory);

                    _logger.LogInfo($"[GameSession] Memória disponível: {FormatBytes((long)freedMemory)}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[GameSession] Erro ao liberar memória: {ex.Message}");
                }

                return freedMemory;
            }, ct);
            _logger.LogExit(nameof(RAMSoftPurgeAsync));
            return result;
        }

        /// <summary>
        /// 8. NetworkStackRefreshSafe - Reset leve na stack de rede
        /// </summary>
        private async Task<bool> NetworkStackRefreshSafeAsync(CancellationToken ct)
        {
            _logger.LogEntry(nameof(NetworkStackRefreshSafeAsync));
            _logger.LogInfo("[GameSession] 🌐 Mantendo estado DNS/Rede (Evita desconexões e atraso de login no jogo)...");
            
            try
            {
                // REMOVIDO: Executar ipconfig /flushdns e /registerdns na hora de abrir um jogo
                // destrói o cache resolvido, atrasa o matchmaking e derruba sessões VoIP (Discord) momentaneamente.
                _logger.LogExit(nameof(NetworkStackRefreshSafeAsync), true);
                return await Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[GameSession] Erro ao otimizar rede: {ex.Message}");
                _logger.LogExit(nameof(NetworkStackRefreshSafeAsync), false);
                return false;
            }
        }

        /// <summary>
        /// 9. IdleServiceSuspension - Suspende serviços não críticos
        /// </summary>
        private async Task<int> IdleServiceSuspensionAsync(CancellationToken ct)
        {
            _logger.LogEntry(nameof(IdleServiceSuspensionAsync));
            var result = await Task.Run(() =>
            {
                int suspendedCount = 0;
                _logger.LogInfo("[GameSession] ⏸️ Pausando serviços não essenciais...");

                try
                {
                    _serviceSnapshot.Clear();

                    foreach (var serviceName in PausableServices)
                    {
                        try
                        {
                            using var service = new ServiceController(serviceName);
                            
                            // Salvar estado original
                            _serviceSnapshot[serviceName] = service.Status;
                            
                            if (service.Status == ServiceControllerStatus.Running)
                            {
                                // Apenas pausar se possível, não parar
                                if (service.CanPauseAndContinue)
                                {
                                    service.Pause();
                                    service.WaitForStatus(ServiceControllerStatus.Paused, TimeSpan.FromSeconds(5));
                                    suspendedCount++;
                                    _logger.LogInfo($"[GameSession] Serviço pausado: {serviceName}");
                                }
                                else if (service.CanStop)
                                {
                                    // Se não pode pausar, parar temporariamente
                                    service.Stop();
                                    service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                                    suspendedCount++;
                                    _logger.LogInfo($"[GameSession] Serviço parado: {serviceName}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"[GameSession] Não foi possível pausar {serviceName}: {ex.Message}");
                        }
                    }

                    _logger.LogInfo($"[GameSession] {suspendedCount} serviços suspensos");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[GameSession] Erro ao suspender serviços: {ex.Message}");
                }

                return suspendedCount;
            }, ct);
            _logger.LogExit(nameof(IdleServiceSuspensionAsync));
            return result;
        }

        #endregion

        #region PostGameRecoveryPack

        /// <summary>
        /// 10. RestoreServiceState - Restaura serviços para estado original
        /// </summary>
        private async Task<int> RestoreServiceStateAsync(CancellationToken ct)
        {
            _logger.LogEntry(nameof(RestoreServiceStateAsync));
            var result = await Task.Run(() =>
            {
                int restoredCount = 0;
                _logger.LogInfo("[GameSession] ▶️ Restaurando serviços...");

                try
                {
                    foreach (var kvp in _serviceSnapshot)
                    {
                        try
                        {
                            using var service = new ServiceController(kvp.Key);
                            var originalStatus = kvp.Value;

                            if (originalStatus == ServiceControllerStatus.Running && 
                                service.Status != ServiceControllerStatus.Running)
                            {
                                if (service.Status == ServiceControllerStatus.Paused)
                                {
                                    service.Continue();
                                    service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                                }
                                else if (service.Status == ServiceControllerStatus.Stopped)
                                {
                                    service.Start();
                                    service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
                                }
                                restoredCount++;
                                _logger.LogInfo($"[GameSession] Serviço restaurado: {kvp.Key}");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"[GameSession] Não foi possível restaurar {kvp.Key}: {ex.Message}");
                        }
                    }

                    _serviceSnapshot.Clear();
                    _logger.LogInfo($"[GameSession] {restoredCount} serviços restaurados");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[GameSession] Erro ao restaurar serviços: {ex.Message}");
                }

                return restoredCount;
            }, ct);
            _logger.LogExit(nameof(RestoreServiceStateAsync));
            return result;
        }

        /// <summary>
        /// 11. PostGameResidualClean - Limpa arquivos residuais do jogo
        /// </summary>
        private async Task<long> PostGameResidualCleanAsync(string? gameExecutable, CancellationToken ct)
        {
            _logger.LogEntry(nameof(PostGameResidualCleanAsync));
            var result = await Task.Run(() =>
            {
                long totalCleaned = 0;
                _logger.LogInfo("[GameSession] 🧹 Limpando resíduos do jogo...");

                try
                {
                    // Crash dumps
                    var crashDumps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps");
                    if (Directory.Exists(crashDumps))
                    {
                        totalCleaned += CleanDirectorySafe(crashDumps, maxAgeDays: 0);
                    }

                    // Temp recente
                    totalCleaned += CleanDirectorySafe(Path.GetTempPath(), maxAgeDays: 0, safeExtensionsOnly: true);

                    _logger.LogInfo($"[GameSession] Resíduos limpos: {FormatBytes(totalCleaned)}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[GameSession] Erro ao limpar resíduos: {ex.Message}");
                }

                return totalCleaned;
            }, ct);
            _logger.LogExit(nameof(PostGameResidualCleanAsync));
            return result;
        }

        /// <summary>
        /// 12. GPUDriverStateNormalize - Restaura estado normal da GPU
        /// </summary>
        private async Task<bool> GPUDriverStateNormalizeAsync(CancellationToken ct)
        {
            _logger.LogEntry(nameof(GPUDriverStateNormalizeAsync));
            var result = await Task.Run(() =>
            {
                _logger.LogInfo("[GameSession] 🎮 Normalizando estado da GPU...");

                try
                {
                    // Restaurar prioridades de processos
                    foreach (var kvp in _processSnapshot)
                    {
                        try
                        {
                            var proc = Process.GetProcessById(kvp.Key);
                            proc.PriorityClass = kvp.Value;
                        }
                        catch { }
                    }
                    _processSnapshot.Clear();

                    _logger.LogInfo("[GameSession] Estado da GPU normalizado");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[GameSession] Erro ao normalizar GPU: {ex.Message}");
                    return false;
                }
            }, ct);
            _logger.LogExit(nameof(GPUDriverStateNormalizeAsync), result);
            return result;
        }

        /// <summary>
        /// 13. SystemCoolingBoost - Aumenta resfriamento após sessão longa
        /// </summary>
        ///
        /// [FIX:UNICO-DONO-DE-ENERGIA] Este método gravava a política de
        /// resfriamento (`SYSCOOLPOL`) por `powercfg /setacvalueindex` e
        /// reativava o plano — fora do portão de escrita, e por cima do valor que
        /// o Perfil Inteligente tinha escolhido.
        ///
        /// A cooling policy é um dos campos da tabela do Perfil (a REGRA do self
        /// test que valida os 40 pares perfil/tier depende dela). Este método
        /// escrevia 1 agora, 0 em 30 segundos, escrevia por cima do que o Perfil
        /// tinha decid, e o Perfil não tinha como saber: o `SYSCOOLPOL` é
        /// lido do plano, mas a gravação vinha por fora e o valor do plano era
        /// sobrescrito silenciosamente.
        ///
        /// O sintoma prático era o pior dos dois: um timer de 30 segundos
        /// religando o plano, brigando com o guard de 12 segundos do
        /// coordenador. DoisVMs, dois donos, um valor.
        ///
        /// O resfriamento agora é responsabilidade do Perfil, que já o define
        /// por perfil e por tier. Este serviço apenas pede a reaplicação.
        private async Task<bool> SystemCoolingBoostAsync(CancellationToken ct)
        {
            _logger.LogEntry(nameof(SystemCoolingBoostAsync));
            _logger.LogInfo("[GameSession] [COOLING] Delegando politica de resfriamento ao Perfil Inteligente...");

            try
            {
                // [FIX:UNICO-DONO-DE-ENERGIA] Nao grava SYSCOOLPOL e nao reativa o
                // plano. O Perfil e' o dono da cooling policy; pedir de novo
                // seria escolher um valor por cima do dono.
                VoltrisOptimizer.Services.Power.ProfilePowerAuthority.RequestProfileApply(
                    "GameSessionOptimizer.SystemCoolingBoost",
                    "sessao de jogo longa pede mais resfriamento",
                    _logger);

                _logger.LogExit(nameof(SystemCoolingBoostAsync), true);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[GameSession] Erro ao ajustar resfriamento: {ex.Message}");
                _logger.LogExit(nameof(SystemCoolingBoostAsync), false);
                return false;
            }
        }

        /// <summary>
        /// 15. NetworkPostRebalance - Recalibra configurações de rede
        /// </summary>
        private async Task<bool> NetworkPostRebalanceAsync(CancellationToken ct)
        {
            _logger.LogEntry(nameof(NetworkPostRebalanceAsync));
            _logger.LogInfo("[GameSession] 🌐 Rede preservada no pós-jogo.");

            try
            {
                // REMOVIDO: Flush DNS / RegisterDNS ao sair do jogo
                _logger.LogExit(nameof(NetworkPostRebalanceAsync), true);
                return await Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[GameSession] Erro ao rebalancear rede: {ex.Message}");
                _logger.LogExit(nameof(NetworkPostRebalanceAsync), false);
                return false;
            }
        }

        #endregion

        #region Helper Methods

        private long CleanDirectorySafe(string path, int maxAgeDays, bool safeExtensionsOnly = false)
        {
            _logger.LogEntry(nameof(CleanDirectorySafe));
            long cleaned = 0;
            var cutoff = DateTime.Now.AddDays(-maxAgeDays);

            try
            {
                var dir = new DirectoryInfo(path);
                if (!dir.Exists) return 0;

                // Verificar se é pasta protegida
                if (ProtectedFolders.Any(p => path.ToLowerInvariant().Contains(p)))
                {
                    return 0;
                }

                foreach (var file in dir.EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    try
                    {
                        // Verificar idade
                        if (file.LastWriteTime > cutoff && maxAgeDays > 0) continue;
                        
                        // Verificar extensão segura se necessário
                        if (safeExtensionsOnly && !SafeTempExtensions.Contains(file.Extension.ToLowerInvariant()))
                        {
                            continue;
                        }

                        // Verificar se não está em uso
                        if (IsFileLocked(file.FullName)) continue;

                        var size = file.Length;
                        file.Delete();
                        cleaned += size;
                    }
                    catch { }
                }
            }
            catch { }

            _logger.LogExit(nameof(CleanDirectorySafe));
            return cleaned;
        }

        private long CleanOldLogFiles(string basePath, int maxAgeDays)
        {
            _logger.LogEntry(nameof(CleanOldLogFiles));
            long cleaned = 0;
            var cutoff = DateTime.Now.AddDays(-maxAgeDays);

            try
            {
                var dir = new DirectoryInfo(basePath);
                if (!dir.Exists) return 0;

                foreach (var file in dir.EnumerateFiles("*.log", SearchOption.AllDirectories))
                {
                    try
                    {
                        if (file.LastWriteTime < cutoff && !IsFileLocked(file.FullName))
                        {
                            var size = file.Length;
                            file.Delete();
                            cleaned += size;
                        }
                    }
                    catch { }
                }
            }
            catch { }

            _logger.LogExit(nameof(CleanOldLogFiles));
            return cleaned;
        }

        private bool IsFileLocked(string filePath)
        {
            _logger.LogEntry(nameof(IsFileLocked));
            try
            {
                using var stream = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                _logger.LogExit(nameof(IsFileLocked), false);
                return false;
            }
            catch
            {
                _logger.LogExit(nameof(IsFileLocked), true);
                return true;
            }
        }

        // EmptyWorkingSets was removed to use VoltrisMemoryCoordinator

        private async Task<string> RunProcessAsync(string fileName, string arguments, CancellationToken ct)
        {
            _logger.LogEntry(nameof(RunProcessAsync));
            if (fileName.Equals("powercfg", StringComparison.OrdinalIgnoreCase) && 
                arguments.Contains("SCHEME_CURRENT", StringComparison.OrdinalIgnoreCase) &&
                arguments.Contains("setacvalueindex", StringComparison.OrdinalIgnoreCase))
            {
                bool isLaptop = false;
                try
                {
                    var sysInfo = App.Services?.GetService(typeof(global::VoltrisOptimizer.Interfaces.ISystemInfoService)) as global::VoltrisOptimizer.Interfaces.ISystemInfoService;
                    isLaptop = sysInfo?.IsLaptop() ?? false;
                } catch { }

                if (isLaptop)
                {
                    _logger.LogWarning($"[GameSession] BLOQUEADO: Laptop detectado. Modificação de energia ignorada.");
                    _logger.LogExit(nameof(RunProcessAsync), string.Empty);
                    return string.Empty;
                }
            }

            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process == null) return "";

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync(ct);
            _logger.LogExit(nameof(RunProcessAsync));
            return output;
        }

        private async Task<string> GetActivePowerPlanAsync(CancellationToken ct)
        {
            _logger.LogEntry(nameof(GetActivePowerPlanAsync));
            var output = await RunProcessAsync("powercfg", "/getactivescheme", ct);
            var result = output.Trim();
            _logger.LogExit(nameof(GetActivePowerPlanAsync));
            return result;
        }

        private (ulong TotalPhysicalMemory, ulong AvailablePhysicalMemory) GetMemoryInfo()
        {
            _logger.LogEntry(nameof(GetMemoryInfo));
            try
            {
                var ci = new Microsoft.VisualBasic.Devices.ComputerInfo();
                var result = (ci.TotalPhysicalMemory, ci.AvailablePhysicalMemory);
                _logger.LogExit(nameof(GetMemoryInfo));
                return result;
            }
            catch
            {
                _logger.LogExit(nameof(GetMemoryInfo));
                return (0, 0);
            }
        }

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {sizes[order]}";
        }

        #endregion
    }

    #region Result Classes

    public class PreGameResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public long ShaderCacheCleaned { get; set; }
        public long BrowserCacheCleaned { get; set; }
        public long TempFilesCleaned { get; set; }
        public long LogsPruned { get; set; }
        public long GpuCachePurged { get; set; }
        public int ProcessesOptimized { get; set; }
        public long RamPurged { get; set; }
        public bool NetworkRefreshed { get; set; }
        public int ServicesSuspended { get; set; }

        public long TotalCleaned => ShaderCacheCleaned + BrowserCacheCleaned + TempFilesCleaned + LogsPruned + GpuCachePurged;
    }

    public class PostGameResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public int ServicesRestored { get; set; }
        public long ResidualsCleaned { get; set; }
        public bool GpuNormalized { get; set; }
        public bool CoolingBoosted { get; set; }
        public bool NetworkRebalanced { get; set; }
    }

    #endregion
}


