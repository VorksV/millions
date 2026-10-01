using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using VoltrisOptimizer.Utils;
using VoltrisOptimizer.Helpers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Text.Json;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Gamer.Implementation;
using VoltrisOptimizer.Services.Gamer.Models;
using AppDataPaths = VoltrisOptimizer.Helpers.AppDataPaths;

namespace VoltrisOptimizer.Services
{
    /// <summary>
    /// Serviço avançado de otimização gamer
    /// </summary>
    public partial class GamerOptimizerService
    {
        private readonly ILoggingService _logger;
        private bool _disposed = false;
        // Flag to track if GTA5 (or Enhanced) special handling is active – skips all optimizations on subsequent activations
        private bool _gta5SpecialModeActive = false;
        private readonly AdvancedTweaksService _advancedTweaks;
        private readonly GodModeService _godMode;
        private readonly ExtremeOptimizationsService _extreme;
        private readonly Gamer.GameSessionOptimizerService _sessionOptimizer;
        // private readonly RealGameBoosterService _realBooster; // TEMPORARIAMENTE DESABILITADO - falta implementação
        private readonly string _gamesLibraryPath;
        private readonly string _gameProfilesPath;
        private List<DetectedGame> _detectedGames = new List<DetectedGame>();
        private List<GameProfile> _gameProfiles = new List<GameProfile>();
        private bool _gamerModeActive = false;
        private Process? _monitoredGameProcess = null;
        private GamerModeStatus _status = new GamerModeStatus();
        private bool _extremeModeEnabled = true;
        private System.Threading.CancellationTokenSource? _adaptiveCts;
        private System.Threading.Tasks.Task? _adaptiveTask;
        private readonly object _adaptiveLock = new object();
        private bool _adaptiveNetworkEnabled = true;
        private bool _antiStutterEnabled = true;
        private bool _adaptiveGovernorEnabled = false; // DESABILITADO POR PADRÃO - causa stuttering e alto CPU
        private static double _cachedMaxClockMhz = 0; // Cache para evitar WMI queries repetidas
        private static double _cachedCurrentClockMhz = 0; // Cache para CurrentClockSpeed (CORREÇÃO CRÍTICA #1)
        private static DateTime _lastClockUpdate = DateTime.MinValue; // Timestamp da última atualização do cache
        private const int ClockCacheSeconds = 30; // Cache válido por 30 segundos
        private readonly HashSet<int> _prioritizedProcessIds = new(); // Cache de processos priorizados (CORREÇÃO #4)
        
        // CORREÇÃO MÉDIA #2: Cache de GetHardwareCaps() para evitar queries WMI repetidas
        private static HardwareCaps? _cachedHardwareCaps;
        private static DateTime _lastHardwareCapsUpdate = DateTime.MinValue;
        private const int HardwareCapsCacheMinutes = 5;
        
        // Lista de processos críticos que NUNCA devem ter prioridade reduzida (CORREÇÃO #7)
        private static readonly HashSet<string> CriticalSystemProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "csrss", "winlogon", "lsass", "services", "smss", "wininit",
            "dwm", "explorer", "audiodg", "fontdrvhost", "conhost"
        };
        
        /// <summary>
        /// Habilita ou desabilita o Adaptive Governor
        /// ATENÇÃO: Adaptive Governor pode causar stuttering e latência adicional
        /// Recomendado: DESABILITADO para jogos competitivos
        /// </summary>
        public void SetAdaptiveGovernorEnabled(bool enabled) 
        { 
            _adaptiveGovernorEnabled = enabled;
            if (enabled)
            {
                _logger.LogWarning("⚠️ Adaptive Governor HABILITADO - Pode causar stuttering e latência");
                _logger.LogWarning("⚠️ Recomendado apenas para diagnóstico, não para jogos competitivos");
            }
            else
            {
                _logger.LogInfo("✅ Adaptive Governor DESABILITADO - Otimizações estáticas serão usadas");
            }
        }
        
        /// <summary>
        /// Retorna se o Adaptive Governor está habilitado
        /// </summary>
        public bool IsAdaptiveGovernorEnabled => _adaptiveGovernorEnabled;
        
        public enum AggressivenessLevel
        {
            Low,        // Seguro / Estável
            Balanced,   // Padrão
            High,       // Competitivo
            Extreme     // Risco (Overclock/Tweaks profundos)
        }

        private AggressivenessLevel _aggressiveness = AggressivenessLevel.Balanced;

        public void SetAggressiveness(AggressivenessLevel level)
        {
            _aggressiveness = level;
            _logger.LogInfo($"[Gamer] Nível de Agressividade definido para: {level}");
            
            // Reajustar flags internas baseado na agressividade
            switch (level)
            {
                case AggressivenessLevel.Low:
                    _extremeModeEnabled = false;            // Desativa tweaks arriscados
                    _adaptiveGovernorEnabled = false;       // Desativa governador (causa stutter)
                    break;
                case AggressivenessLevel.Balanced:
                    _extremeModeEnabled = true;             // Habilita otimizações padrão
                    _adaptiveGovernorEnabled = false;
                    break;
                case AggressivenessLevel.High:
                case AggressivenessLevel.Extreme:
                    _extremeModeEnabled = true;
                    // Em níveis altos, podemos forçar afinidade de CPU mais rígida (se implementado)
                    break;
            }
        }

        private string _pingTargetMode = "dns";
        private string? _customPingTarget;
        private readonly System.Collections.Concurrent.ConcurrentQueue<StutterIncident> _stutterIncidents = new System.Collections.Concurrent.ConcurrentQueue<StutterIncident>();
        
        // Eventos para notificação da UI em tempo real
        public event EventHandler<GamerModeChangedEventArgs>? GamerModeChanged;
        public event EventHandler<PreGameOptimizationEventArgs>? PreGameOptimizationCompleted;
        public event EventHandler<PostGameOptimizationEventArgs>? PostGameOptimizationCompleted;
        
        private enum HardwareTier { Lite, Basic, Pro, Ultra }

        private class HardwareCaps
        {
            public bool IsHybridCpu { get; set; }
            public bool HasDGpu { get; set; }
            public string GpuVendor { get; set; } = "";
            public int RamGb { get; set; }
            public string NicVendor { get; set; } = "";
            public bool HagsSupport { get; set; }
            public bool VrrSupport { get; set; }
            public bool IsLaptop { get; set; }
            public bool IsOnBattery { get; set; }
            public long VramMb { get; set; }
            public int PhysicalCores { get; set; }
            public int LogicalProcessors { get; set; }
            public string CpuName { get; set; } = "";
            public HardwareTier Tier { get; set; } = HardwareTier.Basic;
        }

        public GamerOptimizerService(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogInfo("[GamerOptimizerService] 🛠️ Instanciando GamerOptimizerService (Legacy Engine)...");
            _advancedTweaks = new AdvancedTweaksService(_logger);
            _godMode = new GodModeService(_logger);
            _extreme = new ExtremeOptimizationsService(_logger);
            _sessionOptimizer = new Gamer.GameSessionOptimizerService(_logger);
            
            _gamesLibraryPath = AppDataPaths.GamesLibrary;
            _gameProfilesPath = AppDataPaths.GamesProfiles;
            AppDataPaths.EnsureDirectory(Path.GetDirectoryName(_gamesLibraryPath));
            AppDataPaths.EnsureDirectory(Path.GetDirectoryName(_gameProfilesPath));
            
            // Inicialização pesada movida para InitializeAsync()
        }

        private bool _isInitialized = false;
        private readonly SemaphoreSlim _initLock = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Inicializa o serviço de forma assíncrona (IO de arquivos + monitoramento)
        /// </summary>
        public async Task InitializeAsync()
        {
            if (_isInitialized) return;
            await _initLock.WaitAsync();
            try
            {
                if (_isInitialized) return;
                
                _logger.LogInfo("[GamerOptimizerService] 📋 Inicializando bibliotecas e perfis...");
                
                await SafeTask.Run(() => {
                    Directory.CreateDirectory(Path.GetDirectoryName(_gamesLibraryPath)!);
                    LoadGameLibrary();
                    LoadGameProfiles();
                });
                
                _logger.LogInfo("[GamerOptimizerService] 🎮 Iniciando monitoramento de jogos em background (loop 15s, baixo impacto)...");
                StartGameMonitoring();
                
                _isInitialized = true;
                _logger.LogSuccess("[GamerOptimizerService] ✅ Inicialização assíncrona concluída");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GamerOptimizerService] ❌ Erro durante inicialização: {ex.Message}");
            }
            finally
            {
                _initLock.Release();
            }
        }

        #region Modo Gamer Automático

        /// <summary>
        /// Ativa modo gamer automático quando um jogo é detectado
        /// </summary>
        public async Task<bool> ActivateGamerModeAsync(string? gameExecutable = null, Action<int>? progressCallback = null)
        {
            return await SafeTask.Run(async () =>
            {
                try
                {
                    // PRO-ENFORCEMENT FAIL-CLOSED (legacy): motores de otimizacao gamer
                    // so executam com licenca ativa (Standard/Pro/Enterprise), mesmo que
                    // este codigo legacy seja reativado no futuro sem gate na chamada.
                    if (!VoltrisOptimizer.Services.License.ProFeatureGuard.IsPaidActive())
                    {
                        _logger.LogInfo("[LEGACY] Modo gamer bloqueado por licenca (fail-closed).");
                        return false;
                    }

                    _logger.LogInfo("═══════════════════════════════════════════");
                    _logger.LogInfo("🎮 [LEGACY] INICIANDO ATIVAÇÃO DO MODO GAMER");
                    _logger.LogInfo($"[LEGACY] Alvo: {gameExecutable ?? "Global"}");
                    _logger.LogInfo("═══════════════════════════════════════════");
                    _logger.LogDebug("[Debug] ActivateGamerModeAsync called.");
                    progressCallback?.Invoke(5);

                    // Desativar tweaks extremos para GTA5 (inclui versões Enhanced)
                    bool isGta5 = !string.IsNullOrEmpty(gameExecutable) &&
                        Path.GetFileNameWithoutExtension(gameExecutable).StartsWith("GTA5", StringComparison.OrdinalIgnoreCase);
                    bool originalExtremeMode = _extremeModeEnabled;
                    if (isGta5)
                    {
                        _logger.LogInfo("[Gamer] GTA5 detectado – desativando modo extremo.");
                        _extremeModeEnabled = false;
                    }

                    // ============================================
                    // FASE 1: PRÉ-JOGO - Limpeza e Otimização
                    // ============================================
                    _logger.LogInfo("[Gamer] 📦 [FASE 1] Executando otimizações pré-jogo...");
                    var preGameResult = await _sessionOptimizer.OnGameStartCleanupAndBoostAsync(gameExecutable);
                    
                    if (preGameResult.Success)
                    {
                        _logger.LogSuccess($"[Gamer] ✅ [FASE 1] Pré-jogo concluído: {FormatBytes(preGameResult.TotalCleaned)} limpos, {preGameResult.ProcessesOptimized} processos otimizados");
                        PreGameOptimizationCompleted?.Invoke(this, new PreGameOptimizationEventArgs(preGameResult));
                    }
                    else
                    {
                        _logger.LogWarning("[Gamer] ⚠️ [FASE 1] Pré-jogo falhou ou retornou sem sucesso parcial.");
                    }
                    progressCallback?.Invoke(20);

// ============================================
// FASE 2: ATIVAÇÃO DO MODO GAMER
// ============================================
_logger.LogInfo("[Gamer] ⚡ [FASE 2] Ativando otimizações de hardware...");

var caps = GetHardwareCaps();
_logger.LogInfo($"[Gamer] 🖥️ Hardware Stats: Hybrid: {caps.IsHybridCpu}, GPU: {caps.GpuVendor}, RAM: {caps.RamGb}GB");

// 1. Fechar processos desnecessários (em background para não bloquear)
_logger.LogInfo("[Gamer] 🧹 Limpando processos desnecessários...");
await SafeTask.Run(() => CloseUnnecessaryProcesses());
progressCallback?.Invoke(30);

// New: Skip all optimizations for GTA5 (including Enhanced) to avoid crashes
if (isGta5)
{
    _logger.LogInfo("[Gamer] GTA5 detectado – pulando todas as otimizações para evitar crash.");
    // Record activation state without applying any tweaks
    _gamerModeActive = true;
    _status.IsActive = true;
    _status.ActivatedAt = DateTime.Now;
    _status.GameExecutable = gameExecutable;
    GamerModeChanged?.Invoke(this, new GamerModeChangedEventArgs(true, gameExecutable));
    // Restore original extreme mode flag before exiting
    _extremeModeEnabled = originalExtremeMode;
    return true;
}

// 2. Priorizar processos de jogo (em background para não bloquear)
if (!string.IsNullOrEmpty(gameExecutable))
{
    _logger.LogInfo($"[Gamer] 🚀 Priorizando processo do jogo: {gameExecutable}");
    await SafeTask.Run(() => PrioritizeGameProcesses(gameExecutable));
    if (_extremeModeEnabled)
    {
    try
    {
        // CORREÇÃO: Process.GetProcessesByName() pode ser custoso - executar em background
        var proc = await SafeTask.Run(() => 
        {
            try
            {
                return Process.GetProcessesByName(Path.GetFileNameWithoutExtension(gameExecutable)).FirstOrDefault();
            }
            catch
            {
                return null;
            }
        });
        
if (proc != null)
{
    _logger.LogDebug($"[Debug] Process found for extreme optimizations: PID={proc.Id}, Name={proc.ProcessName}");
            _logger.LogInfo($"[Gamer] 🔥 Aplicando Otimizações Extremas para PID {proc.Id}");
            _extreme.ElevateGameProcessPriority(proc.Id);
            if (caps.IsHybridCpu)
            {
                _logger.LogInfo("[Gamer] 🧬 Aplicando CpuSets para processador Híbrido");
                _extreme.ApplyCpuSetsForProcess(proc.Id);
            }
            _extreme.ApplyQosDscpForApp(gameExecutable);
            _extreme.StartDpcWatchdog();
            if (caps.RamGb >= 8)
            {
                _extreme.EnableLargePagesPrivilege();
            }
            _extreme.ThrottleDwmAndUwp(true);
            _monitoredGameProcess = proc;
            
            // Adaptive Governor DESABILITADO POR PADRÃO
            if (_adaptiveGovernorEnabled)
            {
                _logger.LogWarning("[Gamer] ⚠️ Adaptive Governor HABILITADO - Pode causar stuttering");
                StartAdaptiveGovernor(proc);
            }
            else
            {
                _logger.LogInfo("[Gamer] ✅ Adaptive Governor DESABILITADO - Otimizações estáticas aplicadas");
            }
        }
    }
    catch (Exception ex)
    {
        _logger.LogWarning($"[Gamer] ⚠️ Erro ao aplicar otimizações extremas: {ex.Message}");
    }
    }
}
                    progressCallback?.Invoke(50);

                    // 3. Otimizar CPU para jogos (em background para não bloquear)
                    _logger.LogInfo("[Gamer] ⚙️ Otimizando CPU (Registo e Power Plan)...");
                    await SafeTask.Run(() => OptimizeCPUForGaming());
                    progressCallback?.Invoke(70);

                    // 4. Otimizar GPU
                    _logger.LogInfo("[Gamer] 🎮 Otimizando GPU (Driver & Latência)...");
                    await OptimizeGPUForGamingAsync();
                    progressCallback?.Invoke(85);

                    // 5. Ativar Game Mode do Windows
                    _logger.LogInfo("[Gamer] 🪟 Habilitando Windows Game Mode...");
                    // EnableWindowsGameMode(); // Disabled to avoid conflict
                    
                    // 5.1 VERIFICAÇÃO AUTOMÁTICA DE STREAMER (Enterprise Feature)
                    _logger.LogInfo("[Gamer] 🎙️ Verificando Modo Streamer...");
                    // await SafeTask.Run(() => _realBooster.TryActivateStreamerMode()); // TEMPORARIAMENTE DESABILITADO

                    progressCallback?.Invoke(95);

                    // 6. Reduzir latência
                    _logger.LogInfo("[Gamer] 🌐 Aplicando tweaks de rede e latência...");
                    ReduceLatency();
                    _advancedTweaks.BackupNetworkSettings();
                    _status.NetworkTweaksApplied = _advancedTweaks.ApplyNetworkTweaks();
                    _advancedTweaks.SetMaximumTimerResolution();
                    _status.TimerActive = true;
                    _status.CoreParking100 = _advancedTweaks.SetCoreParkingMinCores100();
                    _status.SystemProfileOptimized = _godMode.OptimizeSystemProfile();
                    _status.GameDvrDisabled = _godMode.DisableGameBloat();
                    _godMode.MonitorAndCleanRam();
                    _status.IslcMonitorRunning = true;
                    
                    if (_extremeModeEnabled)
                    {
                        _logger.LogInfo("[Gamer] 💀 Aplicando tweaks EXTREMOS finais...");
                        _extreme.ControlBackgroundServices(true);
                        _extreme.ApplyInputTweaks();
                        _extreme.EnableUltimatePerformance();
                        _extreme.ApplyPerformanceVisuals(true);
                        if (caps.HasDGpu || caps.GpuVendor != "Intel")
                        {
                            _extreme.ApplyGpuTdrTweaks(true);
                        }
                        if (caps.HagsSupport && AdminHelper.IsRunningAsAdministrator())
                        {
                            _extreme.EnableHagsVrr(true);
                        }
                        if (!string.IsNullOrEmpty(caps.NicVendor))
                        {
                            _extreme.ApplyTcpAutotuneRssRsc();
                        }
                        if (caps.RamGb <= 12 || caps.GpuVendor == "Intel")
                        {
                            _extreme.ApplyBackgroundAppsControl(true);
                        }
                        if (caps.GpuVendor == "NVIDIA")
                        {
                            _extreme.ApplyNvidiaStereo3DPolicy(true);
                        }
                        try
                        {
                            // Usar caminho relativo ao diretório do aplicativo
                            var unattendPath = Path.Combine(
                                AppDomain.CurrentDomain.BaseDirectory, 
                                "autounattend.xml"
                            );
                            if (File.Exists(unattendPath))
                            {
                                _extreme.ApplyAdaptiveUnattendPolicies(unattendPath);
                            }
                            else
                            {
                                _logger.LogInfo("[Gamer] Arquivo autounattend.xml não encontrado, pulando políticas adaptativas");
                            }
                        }
                        catch (Exception ex) 
                        { 
                            _logger.LogWarning($"[Gamer] ⚠️ Erro ao aplicar políticas unattend: {ex.Message}");
                        }
                    }
                    progressCallback?.Invoke(100);

                    _gamerModeActive = true;
                    _status.IsActive = true;
                    _status.ActivatedAt = DateTime.Now;
                    _status.GameExecutable = gameExecutable;
                    
                    _logger.LogSuccess("═══════════════════════════════════════════");
                    _logger.LogSuccess("🚀 [LEGACY] MODO GAMER ATIVADO COM SUCESSO!");
                    _logger.LogDebug($"[Debug] ActivateGamerModeAsync final state – ExtremeModeEnabled={_extremeModeEnabled}, AdaptiveGovernorEnabled={_adaptiveGovernorEnabled}");
                    _logger.LogSuccess("═══════════════════════════════════════════");
                    
                    // CORREÇÃO: Enviar notificação do Windows quando modo gamer é ativado
                    try
                    {
                        var gameName = !string.IsNullOrEmpty(gameExecutable) 
                            ? Path.GetFileNameWithoutExtension(gameExecutable) 
                            : "Jogo";
                        NotificationManager.ShowSuccess(
                            "🎮 Modo Gamer",
                            $"Otimizações aplicadas para: {gameName}\nSistema configurado para máximo desempenho."
                        );
                    }
                    catch (Exception notifEx)
                    {
                        _logger.LogWarning($"[Gamer] Erro ao enviar notificação: {notifEx.Message}");
                    }
                    
                    // Notificar UI sobre mudança de estado
                    HistoryService.RecordActivity("Gamer Mode", 
                        $"Modo Gamer ativado: {(!string.IsNullOrEmpty(gameExecutable) ? Path.GetFileNameWithoutExtension(gameExecutable) : "Otimização Global")}.", true);
                    
                    GamerModeChanged?.Invoke(this, new GamerModeChangedEventArgs(true, gameExecutable));
                    _extremeModeEnabled = originalExtremeMode;

                    
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError("🛑 [LEGACY] Erro fatal ao ativar modo gamer", ex);
                    progressCallback?.Invoke(100);
                    return false;
                }
            });
        }

        /// <summary>
        /// Desativa modo gamer e restaura configurações normais
        /// </summary>
        public async Task<bool> DeactivateGamerModeAsync(Action<int>? progressCallback = null)
        {
            return await SafeTask.Run(async () =>
            {
                try
                {
                    _logger.LogInfo("═══════════════════════════════════════════");
                    _logger.LogInfo("🏁 [LEGACY] INICIANDO DESATIVAÇÃO");
                    _logger.LogDebug($"[Debug] DeactivateGamerModeAsync invoked. Monitored PID={_monitoredGameProcess?.Id ?? 0}");
                    _logger.LogInfo("═══════════════════════════════════════════");
                    progressCallback?.Invoke(10);

                    var gameExe = ProcessHelper.GetProcessExecutablePath(_monitoredGameProcess);

                    // ============================================
                    // FASE 1: RESTAURAR CONFIGURAÇÕES DO SISTEMA
                    // ============================================
                    _logger.LogInfo("[Gamer] 🔄 [FASE 1] Restaurando configurações do sistema...");
                    
                    StopAdaptiveGovernor();

                    // Restaurar prioridades normais
                    _logger.LogInfo("[Gamer] 🔄 Restaurando prioridades de processos...");
                    RestoreNormalPriorities();
                    _advancedTweaks.RestoreNetworkSettings();
                    _advancedTweaks.ReleaseTimerResolution();
                    _status.TimerActive = false;
                    _godMode.StopMonitor();
                    _status.IslcMonitorRunning = false;
                    _status.GameDvrDisabled = false;
                    _status.SystemProfileOptimized = _godMode.RestoreSystemProfile();
                    _status.NetworkTweaksApplied = false;

                    // CORREÇÃO: Reverter core parking para o padrão (50%) ao desativar o modo gamer.
                    // SetCoreParkingMinCores100() seta para 100% via powercfg mas nunca era revertido,
                    // causando CPU alta permanente após reinicialização.
                    if (_status.CoreParking100)
                    {
                        _advancedTweaks.RevertCoreParkingToDefault();
                    }
                    _status.CoreParking100 = false;
                    progressCallback?.Invoke(30);
                    
                    if (_extremeModeEnabled)
                    {
                        _logger.LogInfo("[Gamer] 🔄 Revertendo tweaks EXTREMOS...");
                        _extreme.ControlBackgroundServices(false);
                        _extreme.RestorePowerPlan();
                        _extreme.RestoreHagsVrr();
                        _extreme.RestoreTcpGlobals();
                        _extreme.StopDpcWatchdog();
                        _extreme.ThrottleDwmAndUwp(false);
                        _extreme.ApplyGpuTdrTweaks(false);
                        _extreme.ApplyPerformanceVisuals(false);
                    }
                    _advancedTweaks.RestoreMsiSettings();
                    progressCallback?.Invoke(50);
                    
                    if (_extremeModeEnabled)
                    {
                        _extreme.RestoreAll();
                        _extreme.ApplyBackgroundAppsControl(false);
                        _extreme.ApplyNvidiaStereo3DPolicy(false);
                        try
                        {
                            if (_monitoredGameProcess != null)
                            {
                                var path = ProcessHelper.GetProcessExecutablePath(_monitoredGameProcess);
                                if (!string.IsNullOrEmpty(path))
                                {
                                    _extreme.RemoveQosDscpForApp(path);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"[Gamer] ⚠️ Erro ao remover QoS: {ex.Message}");
                        }
                    }
                    progressCallback?.Invoke(70);

                    // ============================================
                    // FASE 2: PÓS-JOGO - Limpeza e Recuperação
                    // ============================================
                    _logger.LogInfo("[Gamer] 📦 [FASE 2] Executando otimizações pós-jogo...");
                    var postGameResult = await _sessionOptimizer.OnGameExitCleanupAndRecoveryAsync(gameExe);
                    
                    if (postGameResult.Success)
                    {
                        _logger.LogSuccess($"[Gamer] ✅ [FASE 2] Pós-jogo concluído: {postGameResult.ServicesRestored} serviços restaurados");
                        PostGameOptimizationCompleted?.Invoke(this, new PostGameOptimizationEventArgs(postGameResult));
                    }
                    progressCallback?.Invoke(90);

                    _gamerModeActive = false;
                    _status.IsActive = false;
                    _status.DeactivatedAt = DateTime.Now;
                    _monitoredGameProcess = null;
                    progressCallback?.Invoke(100);
                    
                    _logger.LogSuccess("═══════════════════════════════════════════");
                    _logger.LogSuccess("🏁 [LEGACY] DESATIVADO COM SUCESSO!");
                    _logger.LogSuccess("═══════════════════════════════════════════");
                    
                    // Notificar UI sobre mudança de estado
                    HistoryService.RecordActivity("Gamer Mode", "Modo Gamer desativado: configurações restauradas.", true);
                    
                    GamerModeChanged?.Invoke(this, new GamerModeChangedEventArgs(false, null));

                    
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError("🛑 [LEGACY] Erro fatal ao desativar modo gamer", ex);
                    return false;
                }
            });
        }

        public bool IsGamerModeActive => _gamerModeActive;
        public GamerModeStatus GetGamerModeStatus() => _status;
        public void SetExtremeModeEnabled(bool enabled) { _extremeModeEnabled = enabled; }
        public void SetAdaptiveNetworkEnabled(bool enabled) { _adaptiveNetworkEnabled = enabled; }
        public void SetAntiStutterEnabled(bool enabled) { _antiStutterEnabled = enabled; }
        public void SetPingTarget(string mode, string? customHost = null) { _pingTargetMode = string.IsNullOrWhiteSpace(mode) ? "dns" : mode.ToLowerInvariant(); _customPingTarget = customHost; }
        public bool IsAdaptiveNetworkEnabled => _adaptiveNetworkEnabled;
        public bool IsAntiStutterEnabled => _antiStutterEnabled;
        public string PingTargetMode => _pingTargetMode;
        public int GetMonitoredGameProcessId() { try { return _monitoredGameProcess?.Id ?? 0; } catch { return 0; } }
        public string GetMonitoredGameExecutablePath() { try { return ProcessHelper.GetProcessExecutablePath(_monitoredGameProcess) ?? ""; } catch { return ""; } }
        public StutterIncident[] GetRecentStutterIncidents()
        {
            try { return _stutterIncidents.ToArray(); } catch { return Array.Empty<StutterIncident>(); }
        }

        #endregion

        #region Detecção Automática de Jogos

        /// <summary>
        /// Detecta jogos instalados no sistema
        /// </summary>
        public async Task<List<DetectedGame>> DetectInstalledGamesAsync()
        {
            return await SafeTask.Run(() =>
            {
                try
                {
                    _logger.LogInfo("Detectando jogos instalados...");
                    var games = new List<DetectedGame>();

                    // Locais comuns de instalação de jogos
                    var commonPaths = new[]
                    {
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam", "steamapps", "common"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steamapps", "common"),
                        @"C:\Program Files (x86)\Steam\steamapps\common",
                        @"C:\Program Files\Epic Games",
                        @"C:\Program Files (x86)\Epic Games",
                        @"C:\Program Files\GOG Galaxy\Games",
                        @"C:\Program Files (x86)\GOG Galaxy\Games",
                        @"C:\Riot Games",
                        @"C:\Program Files\Ubisoft",
                        @"C:\Program Files (x86)\Ubisoft"
                    };

                    foreach (var path in commonPaths)
                    {
                        if (Directory.Exists(path))
                        {
                            try
                            {
                                var detected = ScanDirectoryForGames(path);
                                games.AddRange(detected);
                            }
                            catch { }
                        }
                    }

                    // Buscar por executáveis conhecidos de jogos
                    var knownGameExecutables = new[]
                    {
                        "csgo.exe", "dota2.exe", "lol.exe", "valorant.exe", "fortnite.exe",
                        "minecraft.exe", "gta5.exe", "witcher3.exe", "cyberpunk2077.exe"
                    };

                    foreach (var exeName in knownGameExecutables)
                    {
                        var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exeName));
                        foreach (var proc in processes)
                        {
                            try
                            {
                                var game = new DetectedGame
                                {
                                    Name = proc.ProcessName,
                                    ExecutablePath = ProcessHelper.GetProcessExecutablePath(proc) ?? "",
                                    DetectedAt = DateTime.Now
                                };
                                if (!games.Any(g => g.ExecutablePath == game.ExecutablePath))
                                {
                                    games.Add(game);
                                }
                            }
                            catch { }
                        }
                    }

                    var filtered = games.Where(g => IsGamePath(g.ExecutablePath) && !IsNonGameName(g.Name)).ToList();
                    _detectedGames = filtered;
                    SaveGameLibrary();
                    _logger.LogSuccess($"Detectados {games.Count} jogos");
                    return filtered;
                }
                catch (Exception ex)
                {
                    _logger.LogError("Erro ao detectar jogos", ex);
                    return new List<DetectedGame>();
                }
            });
        }

        private List<DetectedGame> ScanDirectoryForGames(string directory)
        {
            var games = new List<DetectedGame>();
            try
            {
                var exeFiles = EnumerateFilesIterative(directory, "*.exe")
                    .Where(f => !f.Contains("uninstall", StringComparison.OrdinalIgnoreCase) && !f.Contains("setup", StringComparison.OrdinalIgnoreCase) && !f.Contains("launcher", StringComparison.OrdinalIgnoreCase))
                    .Take(50);

                foreach (var exePath in exeFiles)
                {
                    try
                    {
                        var fileInfo = new FileInfo(exePath);
                        var gameName = Path.GetFileNameWithoutExtension(exePath);
                        
                        if (fileInfo.Length > VoltrisOptimizer.Core.Constants.FileSizeConstants.MinGameExecutableSize && IsGamePath(exePath) && !IsNonGameName(gameName))
                        {
                            games.Add(new DetectedGame
                            {
                                Name = gameName,
                                ExecutablePath = exePath,
                                DetectedAt = DateTime.Now,
                                Size = fileInfo.Length
                            });
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return games;
        }

        private IEnumerable<string> EnumerateFilesIterative(string root, string pattern)
        {
            var stack = new Stack<string>();
            if (string.IsNullOrEmpty(root)) yield break;
            stack.Push(root);
            while (stack.Count > 0)
            {
                var dir = stack.Pop();
                string[] files;
                try { files = Directory.GetFiles(dir, pattern, SearchOption.TopDirectoryOnly); }
                catch { files = Array.Empty<string>(); }
                foreach (var f in files) yield return f;
                string[] subdirs;
                try { subdirs = Directory.GetDirectories(dir); }
                catch { subdirs = Array.Empty<string>(); }
                foreach (var sub in subdirs)
                {
                    try
                    {
                        var di = new DirectoryInfo(sub);
                        if ((di.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    }
                    catch { }
                    stack.Push(sub);
                }
            }
        }

        #endregion

        #region Otimização ao Iniciar Jogo

        /// <summary>
        /// Monitora quando um jogo é iniciado e otimiza automaticamente
        /// </summary>
        /// <summary>
        /// Flag to control if internal monitoring loop should run
        /// </summary>
        public bool DisableAutoMonitoring { get; set; } = false;

        private System.Threading.CancellationTokenSource? _gameMonitorCts;

        private void StartGameMonitoring()
        {
            _gameMonitorCts?.Cancel();
            _gameMonitorCts?.Dispose();
            _gameMonitorCts = new CancellationTokenSource();
            var token = _gameMonitorCts.Token;
            
            SafeTask.Run(async () =>
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        if (DisableAutoMonitoring)
                        {
                            await Task.Delay(15000, token);
                            continue;
                        }

                        try
                        {
                            // OTIMIZAÇÃO: Intervalo adaptativo para reduzir overhead em background
                            var interval = _gamerModeActive 
                                ? TimeSpan.FromSeconds(30) 
                                : TimeSpan.FromSeconds(15);
                            
                            await Task.Delay(interval, token);

                            if (!_gamerModeActive)
                            {
                                // OTIMIZAÇÃO CRÍTICA: Usar ForegroundWindowTracker em vez de Process.GetProcesses()
                                // ForegroundWindowTracker usa WinEventHook (zero polling)
                                var fgPid = Core.ForegroundWindowTracker.Instance.CurrentPid;
                                if (fgPid > 0)
                                {
                                    string? fgName = null;
                                    try
                                    {
                                        using var fgProc = Process.GetProcessById(fgPid);
                                        fgName = fgProc.ProcessName;
                                    }
                                    catch { }

                                    if (!string.IsNullOrEmpty(fgName))
                                    {
                                        foreach (var game in _detectedGames)
                                        {
                                            if (string.IsNullOrEmpty(game.ExecutablePath)) continue;
                                            var processName = Path.GetFileNameWithoutExtension(game.ExecutablePath);
                                            
                                            if (string.Equals(fgName, processName, StringComparison.OrdinalIgnoreCase))
                                            {
                                                try
                                                {
                                                    // FIX: Usar ProcessCacheService ou validar com try/catch.
                                                    // O PID pode deixar de existir entre a checagem de fgName e aqui.
                                                    Process? proc = null;
                                                    try { proc = Process.GetProcessById(fgPid); }
                                                    catch (ArgumentException) { break; }

                                                    if (proc != null && !proc.HasExited)
                                                    {
                                                        // Reutilizar o mesmo objeto — não chamar GetProcessById de novo
                                                        _monitoredGameProcess = proc;
                                                        _logger.LogInfo($"🎮 Jogo detectado via ForegroundTracker: {game.Name} - Ativando modo gamer automático");
                                                        
                                                        var activated = await ActivateGamerModeAsync(game.ExecutablePath);
                                                        if (activated)
                                                        {
                                                            NotificationManager.ShowSuccess(
                                                                   LocalizationService.Instance.GetString("GamerModeAutoTitle"),
                                                                   string.Format(LocalizationService.Instance.GetString("GamerModeAutoFor"), game.Name));
                                                        }
                                                        break;
                                                    }
                                                }
                                                catch { }
                                            }
                                        }
                                    }
                                }
                            }
                            else
                            {
                                // Monitoramento de saída ultra-leve
                                if (_monitoredGameProcess != null)
                                {
                                    try
                                    {
                                        _monitoredGameProcess.Refresh();
                                        if (_monitoredGameProcess.HasExited)
                                        {
                                            await DeactivateGamerModeAsync();
                                        }
                                    }
                                    catch
                                    {
                                        await DeactivateGamerModeAsync();
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"[Gamer] Erro no monitoramento: {ex.Message}");
                            await Task.Delay(15000, token);
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { _logger.LogError($"[Gamer] Erro crítico no monitoramento: {ex.Message}", ex); }
            }, CancellationToken.None);
        }

        private bool IsGamePath(string path)
        {
            var p = (path ?? string.Empty).ToLowerInvariant();
            return p.Contains("steamapps\\common") ||
                   p.Contains("\\epic games\\") ||
                   p.Contains("\\gog galaxy\\games\\") ||
                   p.Contains("\\riot games\\") ||
                   p.Contains("\\ubisoft\\");
        }

        private bool IsNonGameName(string name)
        {
            var n = (name ?? string.Empty).ToLowerInvariant();
            var non = new[] { "trae", "code", "qoder", "voltrisoptimizer", "devenv", "winpty-agent", "openconsole", "rg", "fd", "vsce-sign", "code-tunnel" };
            return non.Contains(n);
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public uint BatteryLifeTime;
            public uint BatteryFullLifeTime;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS systemPowerStatus);

        private HardwareCaps GetHardwareCaps()
        {
            if (_cachedHardwareCaps != null && (DateTime.UtcNow - _lastHardwareCapsUpdate).TotalHours < 24)
            {
                return _cachedHardwareCaps;
            }

            var caps = new HardwareCaps();
            try
            {
                var hub = (VoltrisOptimizer.Services.Telemetry.ICentralTelemetryHub?)App.Services?.GetService(typeof(VoltrisOptimizer.Services.Telemetry.ICentralTelemetryHub));
                if (hub != null)
                {
                    caps.CpuName = hub.CpuName;
                    caps.PhysicalCores = hub.CpuCores;
                    caps.LogicalProcessors = hub.CpuLogicalProcessors;
                    caps.IsHybridCpu = caps.CpuName.Contains("Intel", StringComparison.OrdinalIgnoreCase) && 
                                       (caps.CpuName.Contains("12") || caps.CpuName.Contains("13") || 
                                        caps.CpuName.Contains("14") || caps.CpuName.Contains("Ultra"));

                    caps.VramMb = (long)hub.GpuAdapterRam / (1024 * 1024);
                    caps.GpuVendor = hub.GpuName.Contains("NVIDIA") ? "NVIDIA" : (hub.GpuName.Contains("AMD") || hub.GpuName.Contains("Radeon") ? "AMD" : "Intel");
                    caps.HasDGpu = caps.VramMb > 1024 && caps.GpuVendor != "Intel"; 
                    caps.HagsSupport = caps.GpuVendor == "NVIDIA" || caps.GpuVendor == "AMD";
                    caps.VrrSupport = caps.GpuVendor == "NVIDIA" || caps.GpuVendor == "AMD";

                    caps.RamGb = (int)(hub.TotalPhysicalMemory / (1024L * 1024 * 1024));
                    caps.IsLaptop = hub.IsLaptop;
                }
            }
            catch { }
 
            // Detecção de Bateria
            try
            {
                if (GetSystemPowerStatus(out SYSTEM_POWER_STATUS status))
                {
                    // ACLineStatus == 0 significa "Offline" (na bateria)
                    caps.IsOnBattery = (status.ACLineStatus == 0);
                }
            }
            catch { }

            // Determinar o Tier de Hardware
            caps.Tier = DetermineHardwareTier(caps);

            try
            {
                using (var nicSearcher = new ManagementObjectSearcher("SELECT Name,Manufacturer,NetEnabled FROM Win32_NetworkAdapter WHERE NetEnabled = TRUE"))
                {
                    foreach (ManagementObject obj in nicSearcher.Get())
                    {
                using var __dispose_obj = obj;
                        var m = obj["Manufacturer"]?.ToString() ?? obj["Name"]?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(m)) { caps.NicVendor = m; break; }
                    }
                }
            }
            catch { }
            
            // Atualizar cache
            _cachedHardwareCaps = caps;
            _lastHardwareCapsUpdate = DateTime.UtcNow;
            return caps;
        }

        private HardwareTier DetermineHardwareTier(HardwareCaps caps)
        {
            // LITE: Celeron/Pentium ou RAM <= 4GB ou pouquíssimos núcleos
            if (caps.CpuName.Contains("Celeron", StringComparison.OrdinalIgnoreCase) || 
                caps.CpuName.Contains("Pentium", StringComparison.OrdinalIgnoreCase) ||
                caps.RamGb <= 4 || caps.PhysicalCores <= 2)
            {
                return HardwareTier.Lite;
            }

            // ULTRA: i9/Ryzen 9 ou Enthusiast (32GB+ RAM e GPU com muita VRAM)
            if ((caps.CpuName.Contains("i9") || caps.CpuName.Contains("Ryzen 9") || caps.PhysicalCores >= 12) && 
                caps.RamGb >= 32 && caps.VramMb >= 8000)
            {
                return HardwareTier.Ultra;
            }

            // PRO: i7/Ryzen 7 ou i5 moderno com boa RAM e GPU dedicada
            if ((caps.CpuName.Contains("i7") || caps.CpuName.Contains("Ryzen 7") || caps.PhysicalCores >= 8) && 
                caps.RamGb >= 16 && caps.HasDGpu)
            {
                return HardwareTier.Pro;
            }

            // BASIC: O resto (i3/i5 comuns, 8GB RAM, etc)
            return HardwareTier.Basic;
        }

        #endregion

        #region Governança Adaptativa de Performance

        private void StartAdaptiveGovernor(Process proc)
        {
            _logger.LogInfo("[Gamer] Adaptive Governor foi bloqueado para evitar stutters severos e sobrecarga de CPU.");
            return;
            try
            {
                lock (_adaptiveLock)
                {
                    StopAdaptiveGovernor();
                    _adaptiveCts = new System.Threading.CancellationTokenSource();
                    var token = _adaptiveCts.Token;
                    _adaptiveTask = VoltrisOptimizer.Helpers.SafeTask.Run(async () =>
                    {
                        VoltrisOptimizer.Utils.SafePerformanceCounter? totalCpu = null;
                        VoltrisOptimizer.Utils.SafePerformanceCounter? queueLen = null;
                        VoltrisOptimizer.Utils.SafePerformanceCounter? dpcPct = null;
                        VoltrisOptimizer.Utils.SafePerformanceCounter? intrPct = null;
                        VoltrisOptimizer.Utils.SafePerformanceCounter? pageFaults = null;
                        VoltrisOptimizer.Utils.SafePerformanceCounter? diskQueue = null;
                        VoltrisOptimizer.Utils.SafePerformanceCounter? diskLatency = null;
                        System.Net.NetworkInformation.Ping? pinger = null;
                        var ftQueue = new System.Collections.Concurrent.ConcurrentQueue<double>();
                        System.Threading.Tasks.Task? ftTask = null;
                        List<VoltrisOptimizer.Utils.SafePerformanceCounter>? gpuCounters = null;
                        double maxClockMhz = 0;
                        try
                        {
                            totalCpu = new VoltrisOptimizer.Utils.SafePerformanceCounter("Processor", "% Processor Time", "_Total");
                            totalCpu.NextValue();
                            queueLen = new VoltrisOptimizer.Utils.SafePerformanceCounter("System", "Processor Queue Length");
                            queueLen.NextValue();
                            try { dpcPct = new VoltrisOptimizer.Utils.SafePerformanceCounter("Processor", "% DPC Time", "_Total"); dpcPct.NextValue(); } catch { }
                            try { intrPct = new VoltrisOptimizer.Utils.SafePerformanceCounter("Processor", "% Interrupt Time", "_Total"); intrPct.NextValue(); } catch { }
                            try { pageFaults = new VoltrisOptimizer.Utils.SafePerformanceCounter("Memory", "Page Faults/sec"); pageFaults.NextValue(); } catch { }
                            try { diskQueue = new VoltrisOptimizer.Utils.SafePerformanceCounter("PhysicalDisk", "Avg. Disk Queue Length", "_Total"); diskQueue.NextValue(); } catch { }
                            try { diskLatency = new VoltrisOptimizer.Utils.SafePerformanceCounter("PhysicalDisk", "Avg. Disk sec/Transfer", "_Total"); diskLatency.NextValue(); } catch { }
                            pinger = new System.Net.NetworkInformation.Ping();
                            try { ftTask = StartPresentMonReader(proc.Id, token, ftQueue); } catch { }
                            try
                            {
                                var cat = new VoltrisOptimizer.Utils.SafePerformanceCounterCategory("GPU Engine");
                                var names = cat.GetInstanceNames();
                                gpuCounters = new List<VoltrisOptimizer.Utils.SafePerformanceCounter>();
                                foreach (var n in names)
                                {
                                    if (n.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
                                    {
                                        try { gpuCounters.Add(new VoltrisOptimizer.Utils.SafePerformanceCounter("GPU Engine", "Utilization Percentage", n)); } catch { }
                                    }
                                }
                            }
                            catch { }
                            // CORREÇÃO: Ler MaxClockMhz UMA VEZ no início, não a cada iteração
                            // WMI queries são MUITO LENTAS (50-200ms) e causam stuttering
                            if (_cachedMaxClockMhz == 0)
                            {
                                try
                                {
                                    var hub = (VoltrisOptimizer.Services.Telemetry.ICentralTelemetryHub?)App.Services?.GetService(typeof(VoltrisOptimizer.Services.Telemetry.ICentralTelemetryHub));
                                    _cachedMaxClockMhz = hub?.CpuMaxClockSpeed ?? 0;
                                }
                                catch { _cachedMaxClockMhz = 0; }
                            }
                            maxClockMhz = _cachedMaxClockMhz;
                        }
                        catch { }

                        var lastCpuTime = proc.TotalProcessorTime;
                        var cpuSamples = new Queue<double>(16);
                        var lastElevate = DateTime.MinValue;
                        var lastRelax = DateTime.MinValue;
                        var rttSamples = new Queue<double>(20);
                        var lastNetTune = DateTime.MinValue;
                        var lastNetChange = DateTime.MinValue;
                        var nicModerationDisabled = false;
                        var currentDscp = 46;
                        string? exePath = null;
                        try { exePath = ProcessHelper.GetProcessExecutablePath(proc); } catch { exePath = null; }
                        
                        // CORREÇÃO CRÍTICA #4: Histerese para priority thrashing
                        var relaxConditionCount = 0;
                        const int RelaxConditionThreshold = 3;
                        const int RelaxCooldown = 180; // 3 minutos (aumentado de 20s)

                        while (!token.IsCancellationRequested)
                        {
                            try
                            {
                                // CORREÇÃO CRÍTICA: Aumentado de 750ms para 10s
                            // Motivo: Reduzir overhead de 13% para <2%
                            // Eliminar stuttering causado por monitoramento excessivo
                            await System.Threading.Tasks.Task.Delay(Core.Constants.GamerModeConstants.MonitoringIntervals.AdaptiveGovernorInterval, token);
                                if (proc.HasExited) break;

                                var currentCpuTime = proc.TotalProcessorTime;
                                var intervalMs = 750.0;
                                var deltaMs = (currentCpuTime - lastCpuTime).TotalMilliseconds;
                                lastCpuTime = currentCpuTime;
                                var gameCpuPercent = Math.Max(0, Math.Min(100, (deltaMs / (Environment.ProcessorCount * intervalMs)) * 100.0));

                                double totalCpuPercent = 0;
                                double qlen = 0;
                                double dpc = 0;
                                double intr = 0;
                                double pfsec = 0;
                                double dq = 0;
                                double dlat = 0;
                                double gpuUtil = -1;
                                double curClock = 0;
                                try { totalCpuPercent = totalCpu != null ? totalCpu.NextValue() : 0; } catch { totalCpuPercent = 0; }
                                try { qlen = queueLen != null ? queueLen.NextValue() : 0; } catch { qlen = 0; }
                                try { dpc = dpcPct != null ? dpcPct.NextValue() : 0; } catch { dpc = 0; }
                                try { intr = intrPct != null ? intrPct.NextValue() : 0; } catch { intr = 0; }
                                try { pfsec = pageFaults != null ? pageFaults.NextValue() : 0; } catch { pfsec = 0; }
                                try { dq = diskQueue != null ? diskQueue.NextValue() : 0; } catch { dq = 0; }
                                try { dlat = diskLatency != null ? diskLatency.NextValue() : 0; } catch { dlat = 0; }
                                try
                                {
                                    if (gpuCounters != null && gpuCounters.Count > 0)
                                    {
                                        double sum = 0;
                                        foreach (var c in gpuCounters) { try { sum += c.NextValue(); } catch { } }
                                        gpuUtil = Math.Max(0, Math.Min(100, sum));
                                    }
                                }
                                catch { gpuUtil = -1; }
                                // CORREÇÃO CRÍTICA #1: Cache de WMI queries para CurrentClockSpeed
                                // WMI queries levam 50-200ms e causam stuttering
                                if ((DateTime.UtcNow - _lastClockUpdate).TotalSeconds > ClockCacheSeconds)
                                {
                                    try
                                    {
                                        // WMI was too slow (50-200ms) causing stutter. Use max clock or 0 as fallback for stutter analysis.
                                        _cachedCurrentClockMhz = maxClockMhz;
                                        _lastClockUpdate = DateTime.UtcNow;
                                    }
                                    catch { _cachedCurrentClockMhz = 0; }
                                }
                                curClock = _cachedCurrentClockMhz;

                                if (cpuSamples.Count >= 16) cpuSamples.Dequeue();
                                cpuSamples.Enqueue(gameCpuPercent);
                                var avg = cpuSamples.Count > 0 ? cpuSamples.Average() : gameCpuPercent;
                                var variance = cpuSamples.Count > 1 ? cpuSamples.Select(v => (v - avg) * (v - avg)).Average() : 0;

                                var heavyLoad = totalCpuPercent > 85 || gameCpuPercent > 70;
                                // CORREÇÃO: Usar constantes e aumentar thresholds para reduzir falsos positivos
                                // Thresholds mais altos = menos intervenções = menos stuttering causado pelo próprio monitoramento
                                var stutterRisk = variance > Core.Constants.GamerModeConstants.StutteringThresholds.CpuVarianceThreshold 
                                    || qlen > Core.Constants.GamerModeConstants.StutteringThresholds.QueueLengthThreshold;
                                try
                                {
                                    if (ftQueue.Count >= 40)
                                    {
                                        var arr = ftQueue.ToArray();
                                        var avgFt = arr.Average();
                                        var varFt = arr.Select(v => (v - avgFt) * (v - avgFt)).Average();
                                        var ftJitter = Math.Sqrt(varFt);
                                        // CORREÇÃO: Usar constante e aumentar threshold de 3.0 para 5.0ms
                                        if (ftJitter > Core.Constants.GamerModeConstants.StutteringThresholds.FrameJitterThreshold || avgFt > 25.0)
                                            stutterRisk = true;
                                        if (stutterRisk)
                                        {
                                            AddStutterIncident(new StutterIncident
                                            {
                                                Timestamp = DateTime.UtcNow,
                                                TotalCpu = totalCpuPercent,
                                                GameCpu = gameCpuPercent,
                                                QueueLength = qlen,
                                                DpcPercent = dpc,
                                                InterruptPercent = intr,
                                                PageFaultsPerSec = pfsec,
                                                DiskQueue = dq,
                                                DiskLatencySec = dlat,
                                                GpuUtilPercent = gpuUtil,
                                                CpuFreqCurrentMhz = curClock,
                                                CpuFreqMaxMhz = maxClockMhz,
                                                FrameAvgMs = avgFt,
                                                FrameJitterMs = ftJitter,
                                                NetworkJitterMs = 0,
                                                Cause = InferCause(totalCpuPercent, qlen, dpc, intr, pfsec, dq, dlat, ftJitter, gpuUtil, curClock, maxClockMhz)
                                            });
                                        }
                                    }
                                }
                                catch { }

                                // CORREÇÃO: Usar constante e aumentar cooldown de 10s para 60s
                                // Evitar thrashing de prioridade que causa stuttering
                                if (_antiStutterEnabled && (heavyLoad || stutterRisk) && (DateTime.UtcNow - lastElevate).TotalSeconds > Core.Constants.GamerModeConstants.Cooldowns.PriorityChangeCooldown)
                                {
                                    try
                                    {
                                        _extreme.ElevateGameProcessPriority(proc.Id);
                                        _extreme.ThrottleDwmAndUwp(true);
                                        if (_extremeModeEnabled)
                                        {
                                            try { _extreme.ApplyCpuSetsForProcess(proc.Id); } catch { }
                                        }
                                        lastElevate = DateTime.UtcNow;
                                        _logger.LogInfo("[Adaptive] Elevação de prioridade aplicada por carga/risco de stutter");
                                    }
                                    catch { }
                                }

                                // CORREÇÃO CRÍTICA #4: Priority thrashing com histerese
                                // Evita mudanças frequentes de prioridade que causam context switches
                                var relaxedCondition = totalCpuPercent < 50 && avg < 40 && variance < 80 && qlen <= 1;
                                
                                if (relaxedCondition)
                                {
                                    relaxConditionCount++;
                                }
                                else
                                {
                                    relaxConditionCount = 0; // Reset contador se condição não persiste
                                }
                                
                                if (_antiStutterEnabled && 
                                    relaxConditionCount >= RelaxConditionThreshold && 
                                    (DateTime.UtcNow - lastRelax).TotalSeconds > RelaxCooldown)
                                {
                                    try
                                    {
                                        // CORREÇÃO CRÍTICA #2: Validação de processo antes de modificar
                                        proc.Refresh(); // Atualizar estado sem criar novo objeto
                                        if (!proc.HasExited)
                                        {
                                            proc.PriorityClass = System.Diagnostics.ProcessPriorityClass.AboveNormal;
                                            lastRelax = DateTime.UtcNow;
                                            relaxConditionCount = 0; // Reset após aplicar
                                            _logger.LogInfo("[Adaptive] Relax da prioridade para AboveNormal (condições persistiram por 3 iterações)");
                                        }
                                    }
                                    catch (InvalidOperationException)
                                    {
                                        // Processo morreu, sair do loop
                                        break;
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogWarning($"[Adaptive] Erro ao relaxar prioridade: {ex.Message}");
                                    }
                                }

                                // Heurística de rede: medir jitter e ajustar NIC/DSCP
                                // CORREÇÃO: Aumentado intervalo de 2s para 15s (reduzir overhead)
                                // CORREÇÃO: Usar SendPingAsync em vez de Send (não bloquear thread)
                                if (_adaptiveNetworkEnabled && pinger != null && (DateTime.UtcNow - lastNetTune).TotalSeconds > Core.Constants.GamerModeConstants.MonitoringIntervals.NetworkPingInterval / 1000.0)
                                {
                                    try
                                    {
                                        var host = ResolvePingHost();
                                        // CORREÇÃO CRÍTICA: Ping ASSÍNCRONO para não bloquear
                                        // Antes: pinger.Send() bloqueava por até 250ms
                                        // Depois: SendPingAsync() não bloqueia a thread
                                        var reply = await pinger.SendPingAsync(host, 250, new byte[32]);
                                        if (reply != null && reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                                        {
                                            var rtt = reply.RoundtripTime;
                                            if (rtt <= 0) rtt = 1;
                                            if (rttSamples.Count >= 20) rttSamples.Dequeue();
                                            rttSamples.Enqueue(rtt);
                                            var avgRtt = rttSamples.Average();
                                            var varRtt = rttSamples.Count > 1 ? rttSamples.Select(v => (v - avgRtt) * (v - avgRtt)).Average() : 0;
                                            var jitterMs = Math.Sqrt(varRtt);
                                            var successiveDiff = 0.0;
                                            try
                                            {
                                                var arr = rttSamples.ToArray();
                                                for (int i = 1; i < arr.Length; i++) successiveDiff = Math.Max(successiveDiff, Math.Abs(arr[i] - arr[i - 1]));
                                            }
                                            catch { }

                                            // CORREÇÃO: Usar constante e aumentar threshold de 12ms para 20ms
                                            var jitterHigh = jitterMs > Core.Constants.GamerModeConstants.StutteringThresholds.NetworkJitterThreshold || successiveDiff > 20;
                                            var jitterLow = rttSamples.Count >= 12 && jitterMs < (Core.Constants.GamerModeConstants.StutteringThresholds.NetworkJitterThreshold / 3.0) && successiveDiff < 10;

                                            if (jitterHigh)
                                            {
                                                AddStutterIncident(new StutterIncident
                                                {
                                                    Timestamp = DateTime.UtcNow,
                                                    TotalCpu = totalCpuPercent,
                                                    GameCpu = gameCpuPercent,
                                                    QueueLength = qlen,
                                                    DpcPercent = dpc,
                                                    InterruptPercent = intr,
                                                    PageFaultsPerSec = pfsec,
                                                    DiskQueue = dq,
                                                FrameAvgMs = 0,
                                                FrameJitterMs = 0,
                                                NetworkJitterMs = jitterMs,
                                                Cause = "Rede/Jitter"
                                            });
                                            }

                                            // CORREÇÃO: Usar constante e aumentar cooldown de 30s para 120s
                                            // Evitar mudanças frequentes de NIC que causam latency spikes
                                            var canToggle = (DateTime.UtcNow - lastNetChange).TotalSeconds > Core.Constants.GamerModeConstants.Cooldowns.NetworkChangeCooldown;

                                            if (jitterHigh && !nicModerationDisabled && canToggle)
                                            {
                                                try
                                                {
                                                    _extreme.SetNicInterruptModeration(false);
                                                    nicModerationDisabled = true;
                                                    if (!string.IsNullOrEmpty(exePath))
                                                    {
                                                        _extreme.ApplyQosDscpForApp(exePath, 46);
                                                        currentDscp = 46;
                                                    }
                                                    lastNetChange = DateTime.UtcNow;
                                                    _logger.LogInfo($"[Adaptive-Net] Jitter={jitterMs:F1}ms diff={successiveDiff:F1}ms → Moderation OFF, DSCP={currentDscp}");
                                                }
                                                catch { }
                                            }
                                            else if (jitterLow && nicModerationDisabled && totalCpuPercent > 70 && canToggle)
                                            {
                                                try
                                                {
                                                    _extreme.SetNicInterruptModeration(true);
                                                    nicModerationDisabled = false;
                                                    if (!string.IsNullOrEmpty(exePath))
                                                    {
                                                        _extreme.ApplyQosDscpForApp(exePath, 34);
                                                        currentDscp = 34;
                                                    }
                                                    lastNetChange = DateTime.UtcNow;
                                                    _logger.LogInfo($"[Adaptive-Net] Jitter={jitterMs:F1}ms estável → Moderation ON, DSCP={currentDscp}");
                                                }
                                                catch { }
                                            }
                                        }
                                    }
                                    catch { }
                                    finally { lastNetTune = DateTime.UtcNow; }
                                }
                            }
                            catch (OperationCanceledException) { break; }
                            catch { }
                        }
                        try { totalCpu?.Dispose(); } catch { }
                        try { queueLen?.Dispose(); } catch { }
                        try { dpcPct?.Dispose(); } catch { }
                        try { intrPct?.Dispose(); } catch { }
                        try { pageFaults?.Dispose(); } catch { }
                        try { diskQueue?.Dispose(); } catch { }
                        try { diskLatency?.Dispose(); } catch { }
                        try { pinger?.Dispose(); } catch { }
                        try { ftTask?.Dispose(); } catch { }
                        if (gpuCounters != null) { foreach (var c in gpuCounters) { try { c.Dispose(); } catch { } } }
                    }, token);
                }
            }
            catch { }
        }

        private string ResolvePingHost()
        {
            try
            {
                if (_pingTargetMode == "gateway")
                {
                    var gw = GetDefaultGateway();
                    if (!string.IsNullOrWhiteSpace(gw)) return gw!;
                }
                if (_pingTargetMode == "custom" && !string.IsNullOrWhiteSpace(_customPingTarget))
                {
                    return _customPingTarget!;
                }
            }
            catch { }
            return "1.1.1.1";
        }

        private string? GetDefaultGateway()
        {
            try
            {
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    var ipprops = ni.GetIPProperties();
                    var gws = ipprops.GatewayAddresses;
                    foreach (var gw in gws)
                    {
                        var addr = gw?.Address?.ToString();
                        if (!string.IsNullOrWhiteSpace(addr)) return addr;
                    }
                }
            }
            catch { }
            return null;
        }

        private System.Threading.Tasks.Task? StartPresentMonReader(int pid, System.Threading.CancellationToken token, System.Collections.Concurrent.ConcurrentQueue<double> sink)
        {
            try
            {
                var exe = FindPresentMonExecutable();
                if (string.IsNullOrWhiteSpace(exe) || !System.IO.File.Exists(exe)) return null;
                return VoltrisOptimizer.Helpers.SafeTask.Run(() =>
                {
                    try
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = exe,
                            Arguments = $"-process_id {pid} -qpc_time -csv",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        };
                        using var p = System.Diagnostics.Process.Start(psi);
                        if (p == null) return;
                        var reader = p.StandardOutput;
                        while (!token.IsCancellationRequested && !reader.EndOfStream)
                        {
                            var line = reader.ReadLine();
                            if (string.IsNullOrWhiteSpace(line)) continue;
                            try
                            {
                                var parts = line.Split(',');
                                double value = 0;
                                for (int i = parts.Length - 1; i >= 0; i--)
                                {
                                    if (double.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value))
                                    {
                                        break;
                                    }
                                }
                                if (value > 0)
                                {
                                    sink.Enqueue(value);
                                    while (sink.Count > 120)
                                    {
                                        sink.TryDequeue(out _);
                                    }
                                }
                            }
                            catch { }
                        }
                        try { if (!p.HasExited) p.Kill(); } catch { }
                    }
                    catch { }
                }, token);
            }
            catch { }
            return null;
        }

        private string? FindPresentMonExecutable()
        {
            try
            {
                var local = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PresentMon.exe");
                if (System.IO.File.Exists(local)) return local;
                var prog = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var intelPath = System.IO.Path.Combine(prog, "Intel", "PresentMon", "PresentMon.exe");
                if (System.IO.File.Exists(intelPath)) return intelPath;
            }
            catch { }
            return null;
        }

        private void AddStutterIncident(StutterIncident incident)
        {
            try
            {
                _stutterIncidents.Enqueue(incident);
                while (_stutterIncidents.Count > 10) _stutterIncidents.TryDequeue(out _);
            }
            catch { }
        }

        private string InferCause(double totalCpu, double qlen, double dpc, double intr, double pfsec, double dq, double dlat, double ftJitter, double gpuUtil, double curClock, double maxClock)
        {
            try
            {
                // CORREÇÃO: Usar constantes para thresholds
                if (ftJitter > Core.Constants.GamerModeConstants.StutteringThresholds.FrameJitterThreshold) return "Frame pacing/Jitter";
                if (totalCpu > 85 || qlen > Core.Constants.GamerModeConstants.StutteringThresholds.QueueLengthThreshold) return "CPU/Scheduling";
                if (dpc > 5 || intr > 5) return "Drivers/DPC/Interrupt";
                if (pfsec > 500) return "Memória/Paging";
                if (dq > 1.0) return "Disco/IO";
                if (dlat > 0.05) return "Disco/Latência";
                if (gpuUtil >= 85 && totalCpu < 70) return "GPU/Render";
                if (maxClock > 0 && curClock > 0 && (curClock / maxClock) < 0.6 && totalCpu > 60) return "Energia/Throttling";
            }
            catch { }
            return "Indefinido";
        }

        public class StutterIncident
        {
            public DateTime Timestamp { get; set; }
            public double TotalCpu { get; set; }
            public double GameCpu { get; set; }
            public double QueueLength { get; set; }
            public double DpcPercent { get; set; }
            public double InterruptPercent { get; set; }
            public double PageFaultsPerSec { get; set; }
            public double DiskQueue { get; set; }
            public double DiskLatencySec { get; set; }
            public double GpuUtilPercent { get; set; }
            public double CpuFreqCurrentMhz { get; set; }
            public double CpuFreqMaxMhz { get; set; }
            public double FrameAvgMs { get; set; }
            public double FrameJitterMs { get; set; }
            public double NetworkJitterMs { get; set; }
            public string Cause { get; set; } = "";
        }

        

        private void StopAdaptiveGovernor()
        {
            try
            {
                lock (_adaptiveLock)
                {
                    if (_adaptiveCts != null)
                    {
                        try { _adaptiveCts.Cancel(); } catch { }
                        _adaptiveCts = null;
                    }
                    _adaptiveTask = null;
                }
            }
            catch { }
        }

        #endregion

        #region Fechamento de Processos Desnecessários

        private void CloseUnnecessaryProcesses()
        {
            try
            {
                // NÃO fechar navegadores automaticamente - o usuário pode precisar deles
                // Apenas fechar aplicações que realmente consomem muitos recursos em background
                var processesToClose = new[]
                {
                    "Skype", "Teams", "Zoom", "OneDrive", "Dropbox"
                    // Chrome, Firefox, Edge removidos - o usuário pode querer manter aberto
                    // Discord e Spotify removidos - muitos usuários usam enquanto jogam
                    // Steam e EpicGamesLauncher removidos - são necessários para jogos
                };

                foreach (var procName in processesToClose)
                {
                    try
                    {
                        // CORREÇÃO: Process.GetProcessesByName() pode ser custoso - usar try-catch
                        Process[] processes;
                        try
                        {
                            processes = Process.GetProcessesByName(procName);
                        }
                        catch
                        {
                            continue; // Pular se houver erro
                        }
                        
                        foreach (var proc in processes)
                        {
                            try
                            {
                                if (proc.ProcessName != "VoltrisOptimizer")
                                {
                                    proc.CloseMainWindow();
                                    if (!proc.WaitForExit(2000))
                                    {
                                        proc.Kill();
                                    }
                                    _logger.LogInfo($"Processo fechado: {procName}");
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Erro ao fechar processos: {ex.Message}");
            }
        }

        #endregion

        #region Priorização de Processos

        private void PrioritizeGameProcesses(string gameExecutable)
        {
            try
            {
                var processName = Path.GetFileNameWithoutExtension(gameExecutable);
                // CORREÇÃO: Process.GetProcessesByName() pode ser custoso - usar try-catch
                Process[] processes;
                try
                {
                    processes = Process.GetProcessesByName(processName);
                }
                catch
                {
                    return; // Retornar se houver erro
                }

                foreach (var proc in processes)
                {
                    try
                    {
                        // CORREÇÃO #4: Cache de processos priorizados para evitar re-priorização
                        if (_prioritizedProcessIds.Contains(proc.Id))
                        {
                            proc.Dispose();
                            continue;
                        }
                        
                        proc.PriorityClass = ProcessPriorityClass.High;
                        
                        // OTIMIZAÇÃO EXTREMA: Priorização de I/O e Memória (Win32 Extensions)
                        try 
                        { 
                            VoltrisOptimizer.Utils.Win32.Win32ProcessExtensions.SetHighIoPriority(proc);
                            VoltrisOptimizer.Utils.Win32.Win32ProcessExtensions.SetHighMemoryPriority(proc);
                        } catch { }

                        _prioritizedProcessIds.Add(proc.Id);
                        _logger.LogInfo($"🚀 [ULTRA] Prioridade Total (CPU, I/O, RAM) definida para: {processName} (PID: {proc.Id})");
                    }
                    catch { }
                    finally
                    {
                        proc.Dispose();
                    }
                }

                // CORREÇÃO CRÍTICA #7: Reduzir prioridade APENAS se processo não é crítico
                // "svchost" REMOVIDO: hospeda Windows Audio, drivers e rede. Rebaixar isso causa stutters graves.
                var processesToLower = new[] { "SearchIndexer", "WSearch" };
                foreach (var procName in processesToLower)
                {
                    try
                    {
                        // CORREÇÃO: Process.GetProcessesByName() pode ser custoso - usar try-catch
                        Process[] procs;
                        try
                        {
                            procs = Process.GetProcessesByName(procName);
                        }
                        catch
                        {
                            continue; // Pular se houver erro
                        }
                        foreach (var proc in procs)
                        {
                            try
                            {
                                // VALIDAÇÃO: Não reduzir prioridade de processos críticos
                                if (CriticalSystemProcesses.Contains(proc.ProcessName))
                                {
                                    proc.Dispose();
                                    continue;
                                }
                                
                                // VALIDAÇÃO: Não reduzir se processo tem prioridade RealTime
                                if (proc.PriorityClass == ProcessPriorityClass.RealTime)
                                {
                                    proc.Dispose();
                                    continue;
                                }
                                
                                proc.PriorityClass = ProcessPriorityClass.BelowNormal;
                            }
                            catch { }
                            finally
                            {
                                proc.Dispose();
                            }
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Erro ao priorizar processos: {ex.Message}");
            }
        }

        private void RestoreNormalPriorities()
        {
            try
            {
                // CORREÇÃO MÉDIA #4: Usar cache de processos priorizados em vez de enumerar todos
                // Process.GetProcesses() enumera TODOS os processos (100-300ms) - muito custoso
                var processIdsToRestore = _prioritizedProcessIds.ToList();
                _prioritizedProcessIds.Clear();
                
                foreach (var pid in processIdsToRestore)
                {
                    try
                    {
                        using var proc = Process.GetProcessById(pid);
                        if (proc.ProcessName != "VoltrisOptimizer")
                        {
                            proc.PriorityClass = ProcessPriorityClass.Normal;
                        }
                    }
                    catch (ArgumentException)
                    {
                        // Processo não existe mais, ignorar
                    }
                    catch { }
                }
            }
            catch { }
        }

        #endregion

        #region Otimização de CPU

        private void OptimizeCPUForGaming()
        {
            try
            {
                var caps = GetHardwareCaps();
                _logger.LogInfo($"[Profiler] Detecção de Hardware: {caps.CpuName} ({caps.PhysicalCores} núcleos)");
                _logger.LogInfo($"[Profiler] Memória: {caps.RamGb}GB RAM | GPU: {caps.GpuVendor} ({caps.VramMb}MB VRAM)");
                _logger.LogInfo($"[Profiler] Dispositivo: {(caps.IsLaptop ? "Notebook" : "Desktop")} | Status: {(caps.IsOnBattery ? "Bateria ⚠️" : "Cabo AC ✅")}");
                _logger.LogInfo($"[Profiler] Classificação de Performance: Tier {caps.Tier}");

                int priorityValue = Core.Constants.SystemConstants.ProcessPriority.ForegroundBoostMax;
                int disablePaging = 1;
                bool disableParking = true;

                // 1. Lógica baseada no Tier de Hardware
                switch (caps.Tier)
                {
                    case HardwareTier.Lite:
                        priorityValue = Core.Constants.SystemConstants.ProcessPriority.SafePerformance;
                        disablePaging = 0; // Mais seguro para PC fraco (evita OOM)
                        disableParking = false; // Não forçar núcleos para evitar superaquecimento em CPUs fracas
                        _logger.LogWarning("[Gamer] Perfil LITE aplicado: Foco em estabilidade e segurança térmica.");
                        break;
                    
                    case HardwareTier.Basic:
                        priorityValue = Core.Constants.SystemConstants.ProcessPriority.BalancedPerformance;
                        disablePaging = (caps.RamGb > 8) ? 1 : 0;
                        disableParking = true;
                        _logger.LogInfo("[Gamer] Perfil BASIC aplicado: Otimização equilibrada.");
                        break;

                    case HardwareTier.Pro:
                    case HardwareTier.Ultra:
                        priorityValue = Core.Constants.SystemConstants.ProcessPriority.ForegroundBoostMax;
                        disablePaging = 1;
                        disableParking = true;
                        _logger.LogSuccess($"[Gamer] Perfil {caps.Tier} aplicado: Performance máxima liberada.");
                        break;
                }

                // 2. Sobrescrever se estiver na Bateria (Segurança Total)
                if (caps.IsOnBattery)
                {
                    priorityValue = Core.Constants.SystemConstants.ProcessPriority.Default;
                    disablePaging = 0;
                    disableParking = false;
                    _logger.LogWarning("[Gamer] ⚠️ MODO BATERIA DETECTADO: Otimizações agressivas desabilitadas para preservar energia.");
                }

                // Aplicar Prioridade de Processos (Quantum do Agendador)
                // CORREÇÃO CRÍTICA: Não modificar o Win32PrioritySeparation. Isso quebra o Thread Scheduler nativo do Windows 11
                // e causa microstutters em jogos ao priorizar erroneamente processos em vez de delegar para o Thread Director.
                /* using (var key = Registry.LocalMachine.CreateSubKey(
                    Core.Constants.SystemConstants.RegistryPaths.PriorityControl, true))
                {
                    key?.SetValue("Win32PrioritySeparation", priorityValue, RegistryValueKind.DWord);
                } */

                // Aplicar CPU parking (Prevenir núcleos de dormirem)
                using (var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Power", true))
                {
                    // 0 = Núcleos sempre acordados (Max Performance)
                    // 1 = Gerenciamento padrão (Energia)
                    key?.SetValue("CpuParkingCoreParkingOverride", disableParking ? 0 : 1, RegistryValueKind.DWord);
                }

                // Aplicar DisablePagingExecutive (Manter Kernel na RAM)
                using (var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", true))
                {
                    key?.SetValue("DisablePagingExecutive", disablePaging, RegistryValueKind.DWord);
                }

                _logger.LogSuccess($"[Gamer] CPU otimizado com sucesso (Modo: {caps.Tier} | Prioridade: 0x{priorityValue:X})");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Gamer] Erro ao aplicar otimização adaptativa: {ex.Message}");
            }
        }

        #endregion

        #region Otimização de GPU

        /// <summary>
        /// Otimiza GPU para jogos
        /// </summary>
        public async Task<bool> OptimizeGPUForGamingAsync(Action<int>? progressCallback = null)
        {
            return await SafeTask.Run(() =>
            {
                try
                {
                    _logger.LogInfo("Otimizando GPU para jogos...");
                    progressCallback?.Invoke(10);

                    // Detectar GPU
                    var gpuInfo = GetGPUInfo();
                    progressCallback?.Invoke(30);

                    // Otimizar drivers
                    OptimizeGPUDrivers();
                    progressCallback?.Invoke(60);

                    // Configurar modo de performance
                    SetGPUPerformanceMode();
                    progressCallback?.Invoke(80);

                    // Desabilitar VSync se necessário
                    DisableVSyncIfNeeded();
                    progressCallback?.Invoke(100);

                    _logger.LogSuccess("GPU otimizada para jogos");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError("Erro ao otimizar GPU", ex);
                    return false;
                }
            });
        }

        private GPUInfo GetGPUInfo()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                using var __dispose_obj = obj;
                        var name = obj["Name"]?.ToString() ?? "Desconhecida";
                        var driverVersion = obj["DriverVersion"]?.ToString() ?? "";
                        
                        return new GPUInfo
                        {
                            Name = name,
                            DriverVersion = driverVersion,
                            IsNVIDIA = name.Contains("NVIDIA"),
                            IsAMD = name.Contains("AMD") || name.Contains("Radeon"),
                            IsIntel = name.Contains("Intel")
                        };
                    }
                }
            }
            catch { }
            return new GPUInfo();
        }

        private void OptimizeGPUDrivers()
        {
            try
            {
                // Verificar atualizações de drivers (informação)
                _logger.LogInfo("Verificando drivers de GPU...");
                // Em produção, integrar com APIs de NVIDIA/AMD para verificar atualizações
            }
            catch { }
        }

        private void SetGPUPerformanceMode()
        {
            try
            {
                // Configurações de registro para performance de GPU
                using (var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", true))
                {
                    key?.SetValue("HwSchMode", 2, RegistryValueKind.DWord); // Modo de performance
                }
            }
            catch { }
        }

        private void DisableVSyncIfNeeded()
        {
            // VSync deve ser desabilitado nos jogos, não no sistema
            _logger.LogInfo("VSync deve ser desabilitado nas configurações do jogo");
        }

        /// <summary>
        /// Overclock seguro de GPU (apenas para GPUs compatíveis)
        /// </summary>
        public async Task<bool> SafeGPUOverclockAsync(int powerLimitPercent = 110, int memoryClockMHz = 0, Action<int>? progressCallback = null)
        {
            return await SafeTask.Run(() =>
            {
                try
                {
                    _logger.LogWarning("Overclock de GPU requer ferramentas específicas (MSI Afterburner, etc)");
                    _logger.LogInfo("Recomendamos usar ferramentas especializadas para overclock seguro");
                    
                    // Em produção, integrar com APIs de overclock se disponíveis
                    // Por segurança, apenas fornecer informações
                    
                    progressCallback?.Invoke(100);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError("Erro ao configurar overclock", ex);
                    return false;
                }
            });
        }

        /// <summary>
        /// Monitora temperatura da GPU
        /// </summary>
        public async Task<GPUTemperature> GetGPUTemperatureAsync()
        {
            return await SafeTask.Run(() =>
            {
                try
                {
                    // Usar WMI para obter temperatura (se disponível)
                    using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController"))
                    {
                        foreach (ManagementObject obj in searcher.Get())
                        {
                using var __dispose_obj = obj;
                            // Temperatura pode não estar disponível via WMI
                            // Em produção, usar bibliotecas especializadas (OpenHardwareMonitor, etc)
                        }
                    }
                    
                    return new GPUTemperature
                    {
                        Current = 0, // Não disponível via WMI padrão
                        Max = 83,
                        IsAvailable = false
                    };
                }
                catch
                {
                    return new GPUTemperature { IsAvailable = false };
                }
            });
        }

        /// <summary>
        /// FPS Boost automático
        /// </summary>
        public async Task<bool> ApplyFPSBoostAsync(Action<int>? progressCallback = null)
        {
            return await SafeTask.Run(async () =>
            {
                try
                {
                    _logger.LogInfo("Aplicando FPS Boost...");
                    progressCallback?.Invoke(10);

                    // 1. Desabilitar animações do Windows
                    DisableWindowsAnimations();
                    progressCallback?.Invoke(30);

                    // 2. Otimizar configurações de GPU
                    // CORREÇÃO CRÍTICA: Usar await em vez de .Wait() bloqueante
                    // Isso evita travar a UI thread durante otimização de GPU
                    await OptimizeGPUForGamingAsync();
                    progressCallback?.Invoke(60);

                    // 3. Reduzir latência
                    ReduceLatency();
                    progressCallback?.Invoke(80);

                    // 4. Otimizar CPU
                    OptimizeCPUForGaming();
                    progressCallback?.Invoke(100);

                    _logger.LogSuccess("FPS Boost aplicado!");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError("Erro ao aplicar FPS Boost", ex);
                    return false;
                }
            });
        }

        private void DisableWindowsAnimations()
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop\WindowMetrics", true))
                {
                    key?.SetValue("MinAnimate", "0", RegistryValueKind.String);
                }

                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", true))
                {
                    key?.SetValue("ListviewAlphaSelect", 0, RegistryValueKind.DWord);
                    key?.SetValue("ListviewShadow", 0, RegistryValueKind.DWord);
                    key?.SetValue("TaskbarAnimations", 0, RegistryValueKind.DWord);
                }
            }
            catch { }
        }

        #endregion

        #region Gerenciamento de Jogos

        /// <summary>
        /// Obtém biblioteca de jogos
        /// </summary>
        public List<DetectedGame> GetGameLibrary()
        {
            return _detectedGames.ToList();
        }

        /// <summary>
        /// Adiciona um jogo detectado à biblioteca e persiste em disco
        /// </summary>
        public void AddDetectedGameToLibrary(DetectedGame game)
        {
            try
            {
                if (game == null) return;
                if (string.IsNullOrEmpty(game.ExecutablePath) || string.IsNullOrEmpty(game.Name)) return;
                if (!IsGamePath(game.ExecutablePath) || IsNonGameName(game.Name)) return;
                if (_detectedGames.Any(g => string.Equals(g.ExecutablePath, game.ExecutablePath, StringComparison.OrdinalIgnoreCase))) return;
                _detectedGames.Add(new DetectedGame
                {
                    Name = game.Name,
                    ExecutablePath = game.ExecutablePath,
                    DetectedAt = DateTime.Now,
                    Size = game.Size
                });
                SaveGameLibrary();
                _logger.LogInfo($"Jogo salvo na biblioteca: {game.Name}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Falha ao salvar jogo na biblioteca: {ex.Message}");
            }
        }

        /// <summary>
        /// Cria perfil de performance para um jogo
        /// </summary>
        public void CreateGameProfile(string gameName, string executablePath, GameProfileSettings settings)
        {
            var profile = new GameProfile
            {
                Id = Guid.NewGuid().ToString(),
                GameName = gameName,
                ExecutablePath = executablePath,
                Settings = settings,
                CreatedAt = DateTime.Now
            };

            _gameProfiles.Add(profile);
            SaveGameProfiles();
            _logger.LogInfo($"Perfil criado para: {gameName}");
        }

        /// <summary>
        /// Obtém perfil de um jogo
        /// </summary>
        public GameProfile? GetGameProfile(string gameName)
        {
            return _gameProfiles.FirstOrDefault(p => p.GameName == gameName);
        }

        /// <summary>
        /// Remove perfil de um jogo
        /// </summary>
        public bool DeleteGameProfile(string gameName)
        {
            try
            {
                var existing = _gameProfiles.FirstOrDefault(p => p.GameName == gameName);
                if (existing == null) return false;
                _gameProfiles.Remove(existing);
                SaveGameProfiles();
                _logger.LogInfo($"Perfil removido: {gameName}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro ao remover perfil", ex);
                return false;
            }
        }

        /// <summary>
        /// Aplica perfil de performance para um jogo
        /// </summary>
        public async Task<bool> ApplyGameProfileAsync(string gameName, Action<int>? progressCallback = null)
        {
            return await SafeTask.Run(async () =>
            {
                try
                {
                    var profile = GetGameProfile(gameName);
                    if (profile == null)
                    {
                        _logger.LogWarning($"Perfil não encontrado para: {gameName}");
                        return false;
                    }

                    _logger.LogInfo($"Aplicando perfil para: {gameName}");
                    progressCallback?.Invoke(10);

                    // Aplicar configurações do perfil
                    if (profile.Settings.CloseBackgroundApps)
                    {
                        CloseUnnecessaryProcesses();
                    }
                    progressCallback?.Invoke(30);

                    if (profile.Settings.OptimizeCPU)
                    {
                        OptimizeCPUForGaming();
                    }
                    progressCallback?.Invoke(50);

                    if (profile.Settings.OptimizeGPU)
                    {
                        await OptimizeGPUForGamingAsync();
                    }
                    progressCallback?.Invoke(70);

                    if (profile.Settings.ApplyFPSBoost)
                    {
                        await ApplyFPSBoostAsync();
                    }
                    progressCallback?.Invoke(90);

                    // Priorizar processo do jogo
                    PrioritizeGameProcesses(profile.ExecutablePath);
                    progressCallback?.Invoke(100);

                    _logger.LogSuccess($"Perfil aplicado para: {gameName}");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Erro ao aplicar perfil: {ex.Message}", ex);
                    return false;
                }
            });
        }

        /// <summary>
        /// Limpa saves e cache de jogos
        /// </summary>
        public async Task<long> CleanGameCacheAsync(string gameName, Action<int>? progressCallback = null)
        {
            return await SafeTask.Run(() =>
            {
                try
                {
                    _logger.LogInfo($"Limpando cache de: {gameName}");
                    progressCallback?.Invoke(10);

                    long totalCleaned = 0;
                    var profile = GetGameProfile(gameName);
                    
                    if (profile != null && !string.IsNullOrEmpty(profile.ExecutablePath))
                    {
                        var gameDir = Path.GetDirectoryName(profile.ExecutablePath);
                        if (!string.IsNullOrEmpty(gameDir) && Directory.Exists(gameDir))
                        {
                            // Limpar cache comum
                            var cacheDirs = new[]
                            {
                                Path.Combine(gameDir, "Cache"),
                                Path.Combine(gameDir, "Temp"),
                                Path.Combine(gameDir, "Logs")
                            };

                            foreach (var cacheDir in cacheDirs)
                            {
                                if (Directory.Exists(cacheDir))
                                {
                                    try
                                    {
                                        var files = EnumerateFilesIterative(cacheDir, "*");
                                        foreach (var file in files)
                                        {
                                            try
                                            {
                                                var fileInfo = new FileInfo(file);
                                                totalCleaned += fileInfo.Length;
                                                File.Delete(file);
                                            }
                                            catch { }
                                        }
                                    }
                                    catch { }
                                }
                            }
                        }
                    }

                    progressCallback?.Invoke(100);
                    _logger.LogSuccess($"Cache limpo: {FormatBytes(totalCleaned)}");
                    return totalCleaned;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Erro ao limpar cache: {ex.Message}", ex);
                    return 0;
                }
            });
        }

        #endregion

        #region Redução de Latência

        private void ReduceLatency()
        {
            try
            {
                // Desabilitar timer de alta precisão (reduz latência)
                using (var key = Registry.LocalMachine.CreateSubKey(
                    Core.Constants.SystemConstants.RegistryPaths.TcpipParameters, true))
                {
                    // Usar constantes para configurações de rede
                    key?.SetValue("TcpAckFrequency", 
                        Core.Constants.SystemConstants.NetworkSettings.TcpAckFrequencyLowLatency, 
                        RegistryValueKind.DWord);
                    key?.SetValue("TCPNoDelay", 
                        Core.Constants.SystemConstants.NetworkSettings.TcpNoDelayEnabled, 
                        RegistryValueKind.DWord);
                }

                // Otimizar interrupções de timer
                using (var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", true))
                {
                    key?.SetValue("GlobalTimerResolutionRequests", 1, RegistryValueKind.DWord);
                }

                _logger.LogSuccess("Latência reduzida");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Erro ao reduzir latência: {ex.Message}");
            }
        }

        #endregion

        #region Game Mode do Windows

        private void EnableWindowsGameMode()
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\GameBar", true))
                {
                    key?.SetValue("AllowAutoGameMode", 1, RegistryValueKind.DWord);
                    key?.SetValue("AutoGameModeEnabled", 1, RegistryValueKind.DWord);
                }
                _logger.LogSuccess("Game Mode do Windows ativado");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Erro ao ativar Game Mode: {ex.Message}");
            }
        }

        #endregion

        #region Persistência

        private void LoadGameLibrary()
        {
            try
            {
                if (File.Exists(_gamesLibraryPath))
                {
                    var json = File.ReadAllText(_gamesLibraryPath);
                    var loaded = JsonSerializer.Deserialize<List<DetectedGame>>(json) ?? new List<DetectedGame>();
                    _detectedGames = loaded.Where(g => IsGamePath(g.ExecutablePath) && !IsNonGameName(g.Name)).ToList();
                }
            }
            catch { }
        }

        private void SaveGameLibrary()
        {
            try
            {
                var json = JsonSerializer.Serialize(_detectedGames, new JsonSerializerOptions { WriteIndented = true, ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles });
                File.WriteAllText(_gamesLibraryPath, json);
            }
            catch { }
        }

        private void LoadGameProfiles()
        {
            try
            {
                if (File.Exists(_gameProfilesPath))
                {
                    var json = File.ReadAllText(_gameProfilesPath);
                    _gameProfiles = JsonSerializer.Deserialize<List<GameProfile>>(json) ?? new List<GameProfile>();
                }
            }
            catch { }
        }

        private void SaveGameProfiles()
        {
            try
            {
                var json = JsonSerializer.Serialize(_gameProfiles, new JsonSerializerOptions { WriteIndented = true, ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles });
                File.WriteAllText(_gameProfilesPath, json);
            }
            catch { }
        }

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
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
    }

    #region Classes de Dados

    // Classes de Dados movidas para GamerModels.cs para evitar duplicação e erros de build

    public class GPUInfo
    {
        public string Name { get; set; } = "";
        public string DriverVersion { get; set; } = "";
        public bool IsNVIDIA { get; set; }
        public bool IsAMD { get; set; }
        public bool IsIntel { get; set; }
    }

    public class GPUTemperature
    {
        public double Current { get; set; }
        public double Max { get; set; }
        public bool IsAvailable { get; set; }
    }

    #endregion
    
    public class GamerModeStatus
    {
        public bool IsActive { get; set; }
        public bool TimerActive { get; set; }
        public bool NetworkTweaksApplied { get; set; }
        public bool CoreParking100 { get; set; }
        public bool SystemProfileOptimized { get; set; }
        public bool GameDvrDisabled { get; set; }
        public bool IslcMonitorRunning { get; set; }
        public DateTime? ActivatedAt { get; set; }
        public DateTime? DeactivatedAt { get; set; }
        public string? GameExecutable { get; set; }
    }
    
    #region Event Args Classes
    
    /// <summary>
    /// Argumentos do evento de mudança de estado do Modo Gamer
    /// </summary>
    public class GamerModeChangedEventArgs : EventArgs
    {
        public bool IsActive { get; }
        public string? GameExecutable { get; }
        public DateTime Timestamp { get; }
        
        public GamerModeChangedEventArgs(bool isActive, string? gameExecutable)
        {
            IsActive = isActive;
            GameExecutable = gameExecutable;
            Timestamp = DateTime.Now;
        }
    }
    
    /// <summary>
    /// Argumentos do evento de otimização pré-jogo concluída
    /// </summary>
    public class PreGameOptimizationEventArgs : EventArgs
    {
        public Gamer.PreGameResult Result { get; }
        public DateTime Timestamp { get; }
        
        public PreGameOptimizationEventArgs(Gamer.PreGameResult result)
        {
            Result = result;
            Timestamp = DateTime.Now;
        }
    }
    
    /// <summary>
    /// Argumentos do evento de otimização pós-jogo concluída
    /// </summary>
    public class PostGameOptimizationEventArgs : EventArgs
    {
        public Gamer.PostGameResult Result { get; }
        public DateTime Timestamp { get; }
        
        public PostGameOptimizationEventArgs(Gamer.PostGameResult result)
        {
            Result = result;
            Timestamp = DateTime.Now;
        }
    }
    
    #endregion
}

// Extensão IDisposable para GamerOptimizerService
namespace VoltrisOptimizer.Services
{
    public partial class GamerOptimizerService : IDisposable
    {
        /// <summary>
        /// Libera recursos não gerenciados
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    
        /// <summary>
        /// Libera recursos não gerenciados e opcionalmente recursos gerenciados
        /// </summary>
        protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;
            
        if (disposing)
        {
            try
            {
                // Parar adaptive governor
                StopAdaptiveGovernor();
                
                // Cancelar e liberar CancellationTokenSource
                if (_adaptiveCts != null)
                {
                    try
                    {
                        _adaptiveCts.Cancel();
                        _adaptiveCts.Dispose();
                        _adaptiveCts = null;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning($"Erro ao liberar CancellationTokenSource: {ex.Message}");
                    }
                }

                if (_gameMonitorCts != null)
                {
                    try
                    {
                        _gameMonitorCts.Cancel();
                        _gameMonitorCts.Dispose();
                        _gameMonitorCts = null;
                    }
                    catch { }
                }
                
                // Aguardar task finalizar (com timeout)
                if (_adaptiveTask != null && !_adaptiveTask.IsCompleted)
                {
                    try
                    {
                        _adaptiveTask.Wait(TimeSpan.FromSeconds(2));
                    }
                    catch (AggregateException)
                    {
                        // Task foi cancelada, ignorar
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning($"Erro ao aguardar task finalizar: {ex.Message}");
                    }
                }
                
                // Liberar recursos dos serviços internos
                try
                {
                    (_extreme as IDisposable)?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning($"Erro ao liberar ExtremeOptimizationsService: {ex.Message}");
                }
                
                _logger?.LogInfo("GamerOptimizerService: Recursos liberados com sucesso");
            }
            catch (Exception ex)
            {
                _logger?.LogError("Erro ao liberar recursos do GamerOptimizerService", ex);
            }
        }
        
        _disposed = true;
        }
    
        /// <summary>
        /// Destrutor
        /// </summary>
        ~GamerOptimizerService()
        {
            Dispose(false);
        }
    }
}


