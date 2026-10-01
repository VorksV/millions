using System;
using System.Collections.Generic;

namespace VoltrisOptimizer.Services.Gamer.Models
{
    /// <summary>
    /// Jogo detectado no sistema
    /// </summary>
    public class DetectedGame
    {
        public string Name { get; set; } = string.Empty;
        public string ExecutablePath { get; set; } = string.Empty;
        public string ProcessName { get; set; } = string.Empty;
        public int ProcessId { get; set; }
        public long Size { get; set; }
        public GameLauncher Launcher { get; set; } = GameLauncher.Unknown;
        public bool HasProfile { get; set; }
        public string? ProfileId { get; set; }
        public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
        
        public int ConfidenceScore { get; set; }
        public bool IsHighConfidence => ConfidenceScore >= 80;
        public string? Publisher { get; set; }
        public string? Engine { get; set; }
        public string? Platform { get; set; }
        public string? Category { get; set; }
        public DateTime? LastPlayed { get; set; }
        
        public DetectedGame()
        {
            App.LoggingService?.LogTrace($"[GAMER_MODEL] Novo objeto de Jogo Detectado criado");
        }

        public override string ToString() => $"{Name} [{ConfidenceScore}%]";
    }

    /// <summary>
    /// Launcher de origem do jogo
    /// </summary>
    public enum GameLauncher
    {
        Unknown,
        Steam,
        EpicGames,
        GOG,
        Ubisoft,
        EA,
        Riot,
        Blizzard,
        Xbox,
        Manual
    }

    /// <summary>
    /// Perfil de otimização para um jogo específico
    /// </summary>
    public class GameProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string GameName { get; set; } = string.Empty;
        public string ExecutablePath { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastUsed { get; set; } = DateTime.UtcNow;
        public GameProfileSettings Settings { get; set; } = new();
        public GameProfile()
        {
            App.LoggingService?.LogTrace($"[GAMER_MODEL] Novo Perfil de Jogo instanciado");
        }
    }

    /// <summary>
    /// Configurações de um perfil de jogo
    /// </summary>
    public class GameProfileSettings
    {
        public bool OptimizePowerPlan { get; set; } = true;
        public bool CloseBackgroundApps { get; set; } = true;
        public bool OptimizeCPU { get; set; } = true;
        public bool OptimizeGPU { get; set; } = true;
        public bool OptimizeNetwork { get; set; } = true;
        public bool OptimizeMemory { get; set; } = true;
        public bool ApplyFPSBoost { get; set; } = true;
        public bool ReduceLatency { get; set; } = true;
        public bool EnableGameMode { get; set; } = true;
        public bool EnableExtremeMode { get; set; } = false;
        public bool EnableAntiStutter { get; set; } = true;
        public bool EnableAdaptiveNetwork { get; set; } = true;
        
        // Novas otimizações granulares
        public bool DisableHpet { get; set; } = true;
        public bool DisableWallpaperSlideshow { get; set; } = false;
        public bool DisableUwpBackgroundApps { get; set; } = true;
        public bool DisableHeavyServices { get; set; } = true;
        public bool DisableDwmVSync { get; set; } = true;
        public bool OptimizeGamma { get; set; } = true;
        public VisualOptimizationLevel VisualLevel { get; set; } = VisualOptimizationLevel.Balanced;
        
        public ProcessPriorityLevel ProcessPriority { get; set; } = ProcessPriorityLevel.High;
        public int CPUAffinity { get; set; } = -1; // -1 = todos os cores
    }

    /// <summary>
    /// Níveis de prioridade de processo
    /// </summary>
    public enum ProcessPriorityLevel
    {
        Idle,
        BelowNormal,
        Normal,
        AboveNormal,
        High,
        RealTime
    }

    /// <summary>
    /// Status atual do modo gamer
    /// </summary>
    public class GamerModeStatus
    {
        public bool IsActive { get; set; }
        public string? ActiveGameName { get; set; }
        public int? ActiveGameProcessId { get; set; }
        public DateTime? ActivatedAt { get; set; }
        public DateTime? EndTime { get; set; }
        public TimeSpan? Duration { get; set; }
        
        // Status dos subsistemas
        public bool TimerResolutionActive { get; set; }
        public bool NetworkTweaksApplied { get; set; }
        public bool CoreParkingDisabled { get; set; }
        public bool SystemProfileOptimized { get; set; }
        public bool GameDvrDisabled { get; set; }
        public bool RamMonitorRunning { get; set; }
        public bool AdaptiveGovernorRunning { get; set; }
        public bool ExtremeModeActive { get; set; }
        
        // Métricas
        public double CurrentCpuUsage { get; set; }
        public double CurrentGpuUsage { get; set; }
        public double CurrentRamUsage { get; set; }
        public double CurrentNetworkLatency { get; set; }
        public double CurrentCpuTemp { get; set; }
        public double CurrentGpuTemp { get; set; }
        public bool CpuThrottlingActive { get; set; }
        public bool GpuThrottlingActive { get; set; }

        public GamerModeStatus()
        {
            App.LoggingService?.LogTrace("[GAMER_MODEL] Objeto GamerModeStatus instanciado");
        }
    }

    /// <summary>
    /// Incidente de stutter detectado
    /// </summary>
    public class StutterIncident
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public StutterCause Cause { get; set; } = StutterCause.Unknown;
        public string Summary { get; set; } = string.Empty;
        
        // Métricas no momento do incidente
        public double TotalCpuPercent { get; set; }
        public double GameCpuPercent { get; set; }
        public double ProcessorQueueLength { get; set; }
        public double DpcPercent { get; set; }
        public double InterruptPercent { get; set; }
        public double PageFaultsPerSec { get; set; }
        public double DiskQueueLength { get; set; }
        public double DiskLatencySec { get; set; }
        public double GpuUtilPercent { get; set; }
        public double CpuFreqCurrentMhz { get; set; }
        public double CpuFreqMaxMhz { get; set; }
        public double FrameAvgMs { get; set; }
        public double FrameJitterMs { get; set; }
        public double NetworkJitterMs { get; set; }

        public StutterIncident()
        {
            App.LoggingService?.LogTrace("[GAMER_MODEL] Registro de Incidente de Stutter criado");
        }
    }

    /// <summary>
    /// Causa identificada do stutter
    /// </summary>
    public enum StutterCause
    {
        Unknown,
        CpuScheduling,
        GpuRender,
        FramePacing,
        DriversInterrupt,
        MemoryPaging,
        DiskIO,
        DiskLatency,
        NetworkJitter,
        ThermalThrottling
    }

    /// <summary>
    /// Informações da GPU
    /// </summary>
    public class GpuInfo
    {
        public string Name { get; set; } = string.Empty;
        public string DriverVersion { get; set; } = string.Empty;
        public GpuVendor Vendor { get; set; } = GpuVendor.Unknown;
        public long VideoMemoryBytes { get; set; }
        public bool IsDiscrete { get; set; }
        public bool SupportsHags { get; set; }
        public bool SupportsVrr { get; set; }
    }

    /// <summary>
    /// Vendor da GPU
    /// </summary>
    public enum GpuVendor
    {
        Unknown,
        Nvidia,
        Amd,
        Intel
    }

    /// <summary>
    /// Temperatura da GPU
    /// </summary>
    public class GpuTemperature
    {
        public double Current { get; set; }
        public double Max { get; set; } = 83;
        public bool IsAvailable { get; set; }
        public bool IsThrottling => Current >= Max * 0.95;
    }

    /// <summary>
    /// Capacidades do hardware do sistema
    /// </summary>
    public class HardwareCapabilities
    {
        // CPU
        public string CpuName { get; set; } = string.Empty;
        public int CoreCount { get; set; }
        public int ThreadCount { get; set; }
        public bool IsHybridCpu { get; set; }
        public double MaxClockSpeedMhz { get; set; }
        
        // GPU
        public GpuInfo PrimaryGpu { get; set; } = new();
        public bool HasDiscreteGpu { get; set; }
        
        // RAM
        public int TotalRamGb { get; set; }
        public bool HasSufficientRam => TotalRamGb >= 8;
        
        // Network
        public string NetworkAdapterName { get; set; } = string.Empty;
        public string NetworkAdapterVendor { get; set; } = string.Empty;
        
        // Flags derivados
        public bool SupportsHags => PrimaryGpu.SupportsHags;
        public bool SupportsVrr => PrimaryGpu.SupportsVrr;

        // Contexto de Energia/Chassis
        public bool IsLaptop { get; set; }
        public bool IsOnBattery { get; set; }
    }

    /// <summary>
    /// Configuração de ping target para monitoramento de rede
    /// </summary>
    public enum PingTargetMode
    {
        CloudflareGns,  // 1.1.1.1
        GoogleDns,      // 8.8.8.8
        Gateway,        // Default gateway
        Custom          // Host personalizado
    }

    /// <summary>
    /// Opções de otimização selecionadas pelo usuário
    /// </summary>
    public class GamerOptimizationOptions
    {
        public bool OptimizePowerPlan { get; set; } = true;
        public bool OptimizeCpu { get; set; } = true;
        public bool OptimizeGpu { get; set; } = true;
        public bool OptimizeNetwork { get; set; } = true;
        public bool OptimizeMemory { get; set; } = true;
        public bool EnableGameMode { get; set; } = true;
        public bool ReduceLatency { get; set; } = true;
        public bool CloseBackgroundApps { get; set; } = true;
        public bool ApplyFpsBoost { get; set; } = true;
        public bool EnableExtremeMode { get; set; } = false;
        public bool EnableAntiStutter { get; set; } = true;
        public bool EnableAdaptiveNetwork { get; set; } = true;
        public PingTargetMode PingTarget { get; set; } = PingTargetMode.CloudflareGns;
        public string? CustomPingHost { get; set; }
        
        // Controle granular de efeitos visuais
        public VisualOptimizationLevel VisualLevel { get; set; } = VisualOptimizationLevel.Balanced;
        
        // Novas otimizações
        public bool DisableHpet { get; set; } = true;
        public bool DisableWallpaperSlideshow { get; set; } = false;
        public bool DisableUwpBackgroundApps { get; set; } = true;
        public bool DisableHeavyServices { get; set; } = true;
        public bool DisableDwmVSync { get; set; } = true;
        public bool OptimizeGamma { get; set; } = true;
        
        // Novas otimizações - Toggles de Scheduler e Serviços
        public bool EnableSchedulerSuspension { get; set; } = true;
        public bool EnableWindowsServiceOptimization { get; set; } = true;

        public bool AnyOptimizationsEnabled()
        {
            return OptimizePowerPlan || OptimizeCpu || OptimizeGpu || OptimizeNetwork || OptimizeMemory ||
                   EnableGameMode || ReduceLatency || CloseBackgroundApps || ApplyFpsBoost ||
                   EnableExtremeMode || EnableAntiStutter || EnableAdaptiveNetwork ||
                   DisableHpet || DisableWallpaperSlideshow || DisableUwpBackgroundApps ||
                   DisableHeavyServices || VisualLevel != VisualOptimizationLevel.None ||
                   DisableDwmVSync || OptimizeGamma || EnableSchedulerSuspension || EnableWindowsServiceOptimization;
        }
    }


    /// <summary>
    /// Estado de restauração para recuperação após crash
    /// </summary>
    public class GamerRestorationState
    {
        public bool WasGamerModeActive { get; set; }
        public DateTime ActivatedAt { get; set; }
        public string? GameExecutable { get; set; }
        public GamerOptimizationOptions Options { get; set; } = new();
    }

    /// <summary>
    /// Opções para o Real Game Booster
    /// </summary>
    public class RealGameBoostOptions
    {
        public bool AggressiveMode { get; set; }
        public bool ConservativeMode { get; set; }
        public bool EnableTimerResolution { get; set; } = true;
        public bool EnablePowerPlan { get; set; } = true;
        public bool DisableGameBar { get; set; } = true;
        public bool DisableFullscreenOptimizations { get; set; } = true;
    }

    /// <summary>
    /// Resultado das otimizações do Real Game Booster
    /// </summary>
    public class RealGameBoostResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public bool TimerResolution { get; set; }
        public bool PowerPlan { get; set; }
        public bool GamePriority { get; set; }
        public bool GameBarDisabled { get; set; }
        public bool FsoDisabled { get; set; }
        public bool GpuOptimized { get; set; }
        public bool MemoryCleaned { get; set; }
        public bool HpetOptimized { get; set; }
        public int OptimizationsApplied { get; set; }

        public RealGameBoostResult()
        {
             App.LoggingService?.LogTrace("[GAMER_MODEL] Resultado de RealGameBoost capturado");
        }
    }

    /// <summary>
    /// Intensidade da otimização de GPU
    /// </summary>
    public enum GpuOptimizationIntensity
    {
        Low,
        Medium,
        High,
        Extreme
    }

    /// <summary>
    /// Opções para otimização de GPU
    /// </summary>
    public class GpuOptimizationOptions
    {
        public GpuOptimizationIntensity Intensity { get; set; } = GpuOptimizationIntensity.Medium;
        public bool EnableFpsBoost { get; set; } = true;
        public bool EnableHags { get; set; } = true;
    }

    /// <summary>
    /// Resultado da otimização de GPU
    /// </summary>
    public class GpuOptimizationResult
    {
        public bool Success { get; set; }
        public string? Details { get; set; }
    }

    public enum VisualOptimizationLevel
    {
        None,               // Não alterar
        Balanced,           // Desativar animações pesadas, manter fontes/sombras
        MaximumPerformance  // Desativar tudo (Visual Windows Classico/Basico)
    }
}
