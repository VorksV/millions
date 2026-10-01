using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.Gamer.Models;
using VoltrisOptimizer.Services.Performance.CpuTuning.Features;

namespace VoltrisOptimizer.Services.Gamer.GamerModeManager
{
    /// <summary>
    /// Interface principal do GamerModeManager
    /// Gerencia o ciclo completo do Modo Gamer com monitoramento e rollback automático
    /// </summary>
    public interface IGamerModeManager : IDisposable
    {
        /// <summary>
        /// Status atual do modo gamer
        /// </summary>
        GamerModeState CurrentState { get; }
        
        /// <summary>
        /// Indica se o modo gamer está ativo
        /// </summary>
        bool IsActive { get; }
        
        /// <summary>
        /// Métricas de hardware em tempo real
        /// </summary>
        HardwareMetrics CurrentMetrics { get; }
        
        /// <summary>
        /// Configuração atual
        /// </summary>
        GamerModeConfig Config { get; }
        
        /// <summary>
        /// Evento disparado quando o status muda
        /// </summary>
        event EventHandler<GamerModeState>? StateChanged;
        
        /// <summary>
        /// Evento disparado quando métricas são atualizadas
        /// </summary>
        event EventHandler<HardwareMetrics>? MetricsUpdated;
        
        /// <summary>
        /// Evento disparado quando ocorre rollback de segurança
        /// </summary>
        event EventHandler<SafetyRollbackEventArgs>? SafetyRollbackTriggered;
        
        /// <summary>
        /// Evento disparado quando um jogo é detectado
        /// </summary>
        event EventHandler<GameDetectedEventArgs>? GameDetected;
        
        /// <summary>
        /// Evento disparado para log de eventos
        /// </summary>
        event EventHandler<GamerModeLogEntry>? LogEntry;
        
        /// <summary>
        /// Ativa o modo gamer manualmente
        /// </summary>
        Task<bool> ActivateAsync(string? gameExecutable = null, CancellationToken ct = default);
        
        /// <summary>
        /// Desativa o modo gamer (com rollback completo)
        /// </summary>
        Task<bool> DeactivateAsync(CancellationToken ct = default);
        
        /// <summary>
        /// Força rollback de emergência
        /// </summary>
        Task ForceEmergencyRollbackAsync();
        
        /// <summary>
        /// Inicia monitoramento de jogos
        /// </summary>
        void StartGameDetection();
        
        /// <summary>
        /// Para monitoramento de jogos
        /// </summary>
        void StopGameDetection();
        
        /// <summary>
        /// Carrega configuração de arquivo
        /// </summary>
        Task LoadConfigAsync();
        
        /// <summary>
        /// Salva configuração atual
        /// </summary>
        Task SaveConfigAsync();
        
        /// <summary>
        /// Adiciona jogo à lista de monitoramento
        /// </summary>
        void AddGameToWatch(string executablePath, GameProfile? profile = null);
        
        /// <summary>
        /// Remove jogo da lista de monitoramento
        /// </summary>
        void RemoveGameFromWatch(string executablePath);
        
        /// <summary>
        /// Obtém lista de jogos monitorados
        /// </summary>
        IReadOnlyList<WatchedGame> GetWatchedGames();
        
        /// <summary>
        /// Obtém logs recentes
        /// </summary>
        IReadOnlyList<GamerModeLogEntry> GetRecentLogs(int count = 100);
        
        /// <summary>
        /// Restaura estado anterior apos crash.
        /// Retorna true somente quando um rollback real foi executado.
        /// </summary>
        Task<bool> RestorePreviousStateAsync();

        // 🎛️ CPU TUNING FEATURES
        FivrService? FivrService { get; }
        ProchotService? ProchotService { get; }
        SpeedShiftService? SpeedShiftService { get; }
        AdvancedMsrService? AdvancedMsrService { get; }
    }
    
    /// <summary>
    /// Estado atual do modo gamer - snapshot completo para rollback atômico
    /// </summary>
    public class GamerModeState
    {
        public bool IsActive { get; set; }
        public DateTime? ActivatedAt { get; set; }
        public string? ActiveGameName { get; set; }
        public int? ActiveGameProcessId { get; set; }
        public string? ActiveGamePath { get; set; }
        
        // Power Plan
        public string? OriginalPowerPlanGuid { get; set; }
        public string? CurrentPowerPlanGuid { get; set; }
        public string? CurrentPowerPlanName { get; set; }
        
        // GPU
        public GpuVendor GpuVendor { get; set; }
        public string? GpuName { get; set; }
        public bool GpuOptimized { get; set; }
        public string? GpuPowerMode { get; set; }
        
        // GPU Registry Backup (para rollback atômico)
        public int? OriginalHwSchMode { get; set; }              // HAGS: HwSchMode original
        public int? OriginalGameDVREnabled { get; set; }         // GameDVR original
        public int? OriginalFsoBehaviorMode { get; set; }        // FSO HonorUserFSEBehaviorMode
        public int? OriginalDxgiHonorFse { get; set; }           // DXGIHonorFSEWindowsCompatible
        // NVIDIA
        public int? OriginalNvPowerMizerEnable { get; set; }
        public int? OriginalNvPowerMizerLevel { get; set; }
        public int? OriginalNvPerfLevelSrc { get; set; }
        public int? OriginalNvPowerMizerLevelAC { get; set; }
        public int? OriginalNvPowerMizerLevelDC { get; set; }
        // AMD
        public int? OriginalAmdPowerXpress { get; set; }
        public int? OriginalAmdGlobalPerformance { get; set; }
        public string? OriginalAmdPpTable { get; set; }
        
        // Process
        public bool ProcessPrioritySet { get; set; }
        public bool ProcessAffinitySet { get; set; }
        public int? OriginalProcessPriority { get; set; }      // Prioridade original do processo do jogo (0 = nao alterada)
        public int? OriginalProcessAffinity { get; set; }      // Afinidade original do processo do jogo (0 = nao alterada)
        
        // Windows Services Backup
        public List<ServiceStateInfo> ServiceStates { get; set; } = new();
        
        // Kernel Registry Backup
        public int? OriginalWin32PrioritySeparation { get; set; }
        public int? OriginalQuantumBoost { get; set; }
        public int? OriginalCoreParkingValue { get; set; }
        public int? OriginalDisablePagingExecutive { get; set; }
        public int? OriginalEnableCompression { get; set; }
        public int? OriginalIoPriority { get; set; }
        public int? OriginalIrq8Priority { get; set; }
        public int? OriginalIrq16Priority { get; set; }
        public int? OriginalIrqPriority { get; set; }
        public int? OriginalCpuLatency { get; set; }
        public int? OriginalNumaAllocationPolicy { get; set; }
        public int? OriginalLargePageMinimum { get; set; }
        public int? OriginalSystemPages { get; set; }
        
        // Network Registry Backup
        public int? OriginalNetworkThrottlingIndex { get; set; }
        public int? OriginalSystemResponsiveness { get; set; }
        public int? OriginalTcpAckFrequency { get; set; }
        
        // Pagefile Backup
        public string? OriginalPagingFiles { get; set; }
        
        // Audio/Display
        public int? OriginalAllowSpatialAudio { get; set; }
        public byte[]? OriginalGammaRamp { get; set; }           // Gamma ramp original do display
        
        // Timer Resolution
        public bool TimerResolutionActive { get; set; }
        
        // BH PROCHOT
        public bool OriginalBdProchotEnabled { get; set; }
        
        // Monitoring
        public bool ThermalMonitoringActive { get; set; }
        public bool GameDetectionActive { get; set; }
        
        // Safety
        public bool SafetyRollbackOccurred { get; set; }
        public string? LastSafetyReason { get; set; }

        // Verification Watchdog
        public bool VerificationActive { get; set; }
        public int VerificationSuccessCount { get; set; }
        public int VerificationFailCount { get; set; }
        public int VerificationReapplyCount { get; set; }
        public DateTime? LastVerificationTime { get; set; }
        public bool CpuAffinityFixed { get; set; }
        
        // Performance stats
        public TimeSpan SessionDuration => ActivatedAt.HasValue 
            ? DateTime.Now - ActivatedAt.Value 
            : TimeSpan.Zero;

        public GamerModeState Clone()
        {
            return new GamerModeState
            {
                IsActive = IsActive,
                ActivatedAt = ActivatedAt,
                ActiveGameName = ActiveGameName,
                ActiveGameProcessId = ActiveGameProcessId,
                ActiveGamePath = ActiveGamePath,
                OriginalPowerPlanGuid = OriginalPowerPlanGuid,
                CurrentPowerPlanGuid = CurrentPowerPlanGuid,
                CurrentPowerPlanName = CurrentPowerPlanName,
                GpuVendor = GpuVendor,
                GpuName = GpuName,
                GpuOptimized = GpuOptimized,
                GpuPowerMode = GpuPowerMode,
                ProcessPrioritySet = ProcessPrioritySet,
                ProcessAffinitySet = ProcessAffinitySet,
                ThermalMonitoringActive = ThermalMonitoringActive,
                GameDetectionActive = GameDetectionActive,
                SafetyRollbackOccurred = SafetyRollbackOccurred,
                LastSafetyReason = LastSafetyReason,
                VerificationActive = VerificationActive,
                VerificationSuccessCount = VerificationSuccessCount,
                VerificationFailCount = VerificationFailCount,
                VerificationReapplyCount = VerificationReapplyCount,
                LastVerificationTime = LastVerificationTime,
                CpuAffinityFixed = CpuAffinityFixed,
                OriginalHwSchMode = OriginalHwSchMode,
                OriginalGameDVREnabled = OriginalGameDVREnabled,
                OriginalFsoBehaviorMode = OriginalFsoBehaviorMode,
                OriginalDxgiHonorFse = OriginalDxgiHonorFse,
                OriginalNvPowerMizerEnable = OriginalNvPowerMizerEnable,
                OriginalNvPowerMizerLevel = OriginalNvPowerMizerLevel,
                OriginalNvPerfLevelSrc = OriginalNvPerfLevelSrc,
                OriginalNvPowerMizerLevelAC = OriginalNvPowerMizerLevelAC,
                OriginalNvPowerMizerLevelDC = OriginalNvPowerMizerLevelDC,
                OriginalAmdPowerXpress = OriginalAmdPowerXpress,
                OriginalAmdGlobalPerformance = OriginalAmdGlobalPerformance,
                OriginalAmdPpTable = OriginalAmdPpTable,
                OriginalProcessPriority = OriginalProcessPriority,
                OriginalProcessAffinity = OriginalProcessAffinity,
                ServiceStates = ServiceStates?.Select(s => s.Clone()).ToList() ?? new(),
                OriginalWin32PrioritySeparation = OriginalWin32PrioritySeparation,
                OriginalQuantumBoost = OriginalQuantumBoost,
                OriginalCoreParkingValue = OriginalCoreParkingValue,
                OriginalDisablePagingExecutive = OriginalDisablePagingExecutive,
                OriginalEnableCompression = OriginalEnableCompression,
                OriginalIoPriority = OriginalIoPriority,
                OriginalIrq8Priority = OriginalIrq8Priority,
                OriginalIrq16Priority = OriginalIrq16Priority,
                OriginalIrqPriority = OriginalIrqPriority,
                OriginalCpuLatency = OriginalCpuLatency,
                OriginalNumaAllocationPolicy = OriginalNumaAllocationPolicy,
                OriginalLargePageMinimum = OriginalLargePageMinimum,
                OriginalSystemPages = OriginalSystemPages,
                OriginalNetworkThrottlingIndex = OriginalNetworkThrottlingIndex,
                OriginalSystemResponsiveness = OriginalSystemResponsiveness,
                OriginalTcpAckFrequency = OriginalTcpAckFrequency,
                OriginalPagingFiles = OriginalPagingFiles,
                OriginalAllowSpatialAudio = OriginalAllowSpatialAudio,
                OriginalGammaRamp = OriginalGammaRamp?.ToArray(),
                TimerResolutionActive = TimerResolutionActive,
                OriginalBdProchotEnabled = OriginalBdProchotEnabled
            };
        }
    }
    
    /// <summary>
    /// Estado de um serviço Windows para rollback
    /// </summary>
    public class ServiceStateInfo
    {
        public string ServiceName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public int OriginalStartType { get; set; } = 3; // 2=Auto, 3=Manual, 4=Disabled
        public string OriginalStatus { get; set; } = "Stopped"; // Running, Stopped, Paused
        
        public ServiceStateInfo Clone()
        {
            return new ServiceStateInfo
            {
                ServiceName = ServiceName,
                DisplayName = DisplayName,
                OriginalStartType = OriginalStartType,
                OriginalStatus = OriginalStatus
            };
        }
    }
    
    /// <summary>
    /// Métricas de hardware em tempo real
    /// </summary>
    public class HardwareMetrics
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        
        // CPU
        public double CpuTemperature { get; set; }
        public double CpuUsage { get; set; }
        public double CpuPower { get; set; }
        public double CpuFrequency { get; set; }
        public bool CpuThrottling { get; set; }
        
        // GPU
        public double GpuTemperature { get; set; }
        public double GpuUsage { get; set; }
        public double GpuPower { get; set; }
        public double GpuCoreClock { get; set; }
        public double GpuMemoryClock { get; set; }
        public double GpuVramUsed { get; set; }
        public double GpuVramTotal { get; set; }
        public bool GpuThrottling { get; set; }
        
        // RAM
        public double RamUsagePercent { get; set; }
        public double RamUsedGb { get; set; }
        public double RamTotalGb { get; set; }
        
        // Network
        public double NetworkLatencyMs { get; set; }
        public double NetworkDownloadMbps { get; set; }
        public double NetworkUploadMbps { get; set; }
        
        // Game-specific
        public double? GameFps { get; set; }
        public double? GameFrameTime { get; set; }
    }
    
    /// <summary>
    /// Configuração do modo gamer
    /// </summary>
    public class GamerModeConfig
    {
        // Geral
        public bool AutoActivateOnGameStart { get; set; } = true;
        public bool AutoDeactivateOnGameEnd { get; set; } = true;
        public int GameDetectionIntervalMs { get; set; } = 2000;
        
        // Power Plan
        public bool OptimizePowerPlan { get; set; } = true;
        public bool PreferUltimatePerformance { get; set; } = true;
        
        // Process
        public bool SetHighPriority { get; set; } = true;
        public bool OptimizeAffinity { get; set; } = false;
        public bool CloseBackgroundApps { get; set; } = false;
        public List<string> AppsToClose { get; set; } = new();
        
        // GPU
        public bool OptimizeGpu { get; set; } = true;
        public bool ForceMaxPerformance { get; set; } = true;
        
        // 🚀 OTIMIZAÇÕES SURREAIS
        public bool OptimizeNetwork { get; set; } = true;
        public bool OptimizeMemory { get; set; } = false;  // false por default: pagefile não é reescrito sem consentimento
        public bool OptimizeKernel { get; set; } = false;   // false por default: kernel registry não alterado sem consentimento
        public bool OptimizeDisplay { get; set; } = false;  // false por default: display registry não alterado sem consentimento
        public bool OptimizeAudio { get; set; } = false;    // false por default: audio registry não alterado sem consentimento
        public bool OptimizeCpuScheduler { get; set; } = false; // false por default: cpu scheduler registry não alterado sem consentimento
        
        // 💾 SERVICE OPTIMIZER - Desativação de Serviços do Windows
        public bool DisableHeavyServices { get; set; } = false; // false por default: serviços não desabilitados sem consentimento
        
        // Verification Watchdog
        public bool EnableVerificationWatchdog { get; set; } = false; // false por default: watchdog não re-aplica mutações sem consentimento
        public int VerificationIntervalMs { get; set; } = 5000;
        public bool EnableAutoReapply { get; set; } = false; // false por default: não re-aplica mutações destrutivas
        public bool EnableCpuAffinityLock { get; set; } = true;
        public bool ForcePcoreOnly { get; set; } = false;

        // Safety
        public int CpuMaxTemp { get; set; } = 90;
        public int GpuMaxTemp { get; set; } = 85;
        public int ThermalCheckIntervalMs { get; set; } = 500;
        public bool AutoRollbackOnOverheat { get; set; } = true;
        
        // Monitored games
        public List<WatchedGame> WatchedGames { get; set; } = new();
    }
    
    /// <summary>
    /// Jogo monitorado
    /// </summary>
    public class WatchedGame
    {
        public string Name { get; set; } = "";
        public string ExecutablePath { get; set; } = "";
        public string ProcessName { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public GameProfile? CustomProfile { get; set; }
        public DateTime? LastPlayed { get; set; }
        public TimeSpan TotalPlayTime { get; set; }
    }
    
    /// <summary>
    /// Perfil customizado para um jogo
    /// </summary>
    public class GameProfile
    {
        public string Name { get; set; } = "";
        public ProcessPriorityLevel Priority { get; set; } = ProcessPriorityLevel.High;
        public bool UseAllCores { get; set; } = true;
        public int[]? SpecificCores { get; set; }
        public bool OptimizeGpu { get; set; } = true;
        public bool OptimizeNetwork { get; set; } = true;
        public int? GpuPowerLimit { get; set; }
        public int? CpuTempLimit { get; set; }
        public int? GpuTempLimit { get; set; }
    }
    
    /// <summary>
    /// Nível de prioridade do processo
    /// </summary>
    public enum ProcessPriorityLevel
    {
        Normal,
        AboveNormal,
        High,
        RealTime
    }
    
    /// <summary>
    /// Argumentos do evento de rollback de segurança
    /// </summary>
    public class SafetyRollbackEventArgs : EventArgs
    {
        public string Reason { get; set; } = "";
        public SafetyTrigger Trigger { get; set; }
        public double? Temperature { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
    
    /// <summary>
    /// Gatilho de segurança
    /// </summary>
    public enum SafetyTrigger
    {
        CpuOverheat,
        GpuOverheat,
        CpuThrottling,
        GpuThrottling,
        SystemInstability,
        DriverError,
        UserRequest,
        VerificationFailure
    }
    
    /// <summary>
    /// Argumentos do evento de jogo detectado
    /// </summary>
    public class GameDetectedEventArgs : EventArgs
    {
        public string GameName { get; set; } = "";
        public string ExecutablePath { get; set; } = "";
        public int ProcessId { get; set; }
        public bool IsStarting { get; set; }
    }
    
    /// <summary>
    /// Entrada de log do modo gamer
    /// </summary>
    public class GamerModeLogEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public LogLevel Level { get; set; }
        public string Message { get; set; } = "";
        public string? Details { get; set; }
        public string Category { get; set; } = "General";
    }
    
    /// <summary>
    /// Nível de log
    /// </summary>
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error,
        Critical
    }
}

