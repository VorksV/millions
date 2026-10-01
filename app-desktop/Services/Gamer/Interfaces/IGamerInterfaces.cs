using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.Gamer.Models;
using VoltrisOptimizer.Services.Gamer.Implementation;

namespace VoltrisOptimizer.Services.Gamer.Interfaces
{
    /// <summary>
    /// Orquestra todas as operações do modo gamer
    /// </summary>
    public interface IGamerModeOrchestrator
    {
        /// <summary>
        /// Status atual do modo gamer
        /// </summary>
        Models.GamerModeStatus Status { get; }
        
        /// <summary>
        /// Indica se o modo gamer está ativo
        /// </summary>
        bool IsActive { get; }
        
        /// <summary>
        /// Evento disparado quando o status muda
        /// </summary>
        event EventHandler<Models.GamerModeStatus>? StatusChanged;
        
        /// <summary>
        /// Ativa o modo gamer com as opções especificadas
        /// </summary>
        Task<bool> ActivateAsync(Models.GamerOptimizationOptions options, string? gameExecutable = null, IProgress<int>? progress = null, CancellationToken cancellationToken = default, bool isManual = true);
        
        /// <summary>
        /// Desativa o modo gamer e restaura configurações
        /// </summary>
        Task<bool> DeactivateAsync(IProgress<int>? progress = null, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Obtém as opções atuais de otimização
        /// </summary>
        Models.GamerOptimizationOptions GetCurrentOptions();
        
        /// <summary>
        /// Define as opções de otimização
        /// </summary>
        /// <summary>
        /// Define as opções de otimização
        /// </summary>
        void SetOptions(Models.GamerOptimizationOptions options);

        /// <summary>
        /// Inicia o monitoramento automático de jogos (Auto-Pilot)
        /// </summary>
        void StartAutoPilot();

        /// <summary>
        /// Para o monitoramento automático de jogos
        /// </summary>
        void StopAutoPilot();

        /// <summary>
        /// Aplica otimizações que requerem reinício ou são persistentes
        /// </summary>
        Task ApplyPersistentOptimizationsAsync(Models.GamerOptimizationOptions options, CancellationToken cancellationToken = default);


        /// <summary>
        /// Reverte otimizações persistentes
        /// </summary>
        Task RevertPersistentOptimizationsAsync();

        /// <summary>
        /// Verifica se houve um crash e restaura o sistema se necessário
        /// </summary>
        Task<bool> RestoreIfCrashedAsync();
    }

    /// <summary>
    /// Detecta jogos instalados e em execução
    /// </summary>
    public interface IGameDetector
    {
        /// <summary>
        /// Evento disparado quando um jogo é detectado em execução
        /// </summary>
        event EventHandler<Models.DetectedGame>? GameStarted;
        
        /// <summary>
        /// Evento disparado quando um jogo para de executar
        /// </summary>
        event EventHandler<Models.DetectedGame>? GameStopped;
        
        /// <summary>
        /// Detecta jogos instalados no sistema
        /// </summary>
        Task<IReadOnlyList<Models.DetectedGame>> DetectInstalledGamesAsync(CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Verifica se um processo é um jogo conhecido
        /// </summary>
        bool IsKnownGame(string processName, string? executablePath = null);
        
        /// <summary>
        /// Indica se há pelo menos um jogo detectado rodando no momento
        /// </summary>
        bool HasActiveRunningGameSession { get; }

        /// <summary>
        /// Retorna o jogo principal atualmente em execução, se houver
        /// </summary>
        Models.DetectedGame? CurrentRunningGame { get; }

        /// <summary>
        /// Retorna lista de jogos atualmente em execução
        /// </summary>
        List<Models.DetectedGame> GetRunningGames();
        
        /// <summary>
        /// Inicia monitoramento automático de jogos
        /// </summary>
        void StartMonitoring();
        
        /// <summary>
        /// Para monitoramento automático
        /// </summary>
        void StopMonitoring();
        
        /// <summary>
        /// Indica se o monitoramento está ativo
        /// </summary>
        bool IsMonitoring { get; }
        
        /// <summary>
        /// Força uma verificação imediata de jogos em execução (bypass throttle de 30s)
        /// Útil quando a licença é ativada e precisa detectar jogos já rodando
        /// </summary>
        Task ForceScanRunningGamesAsync(CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Diagnóstico avançado para CS2
        /// </summary>
        void DiagnoseCs2DetectionPublic();
        
        event EventHandler<GameDetectionProgress>? ProgressChanged; // Evento de progresso
    }

    /// <summary>
    /// Progresso da detecção de jogos
    /// </summary>
    public class GameDetectionProgress
    {
        /// <summary>
        /// Texto da etapa atual (ex: "Escaneando Steam...")
        /// </summary>
        public string? Status { get; set; }

        /// <summary>
        /// Porcentagem completa (0-100)
        /// </summary>
        public int PercentComplete { get; set; }

        /// <summary>
        /// Número de jogos detectados até o momento
        /// </summary>
        public int GamesFound { get; set; }
    }

    /// <summary>
    /// Gerencia a biblioteca de jogos detectados
    /// </summary>
    public interface IGameLibraryService
    {
        /// <summary>
        /// Obtém todos os jogos na biblioteca
        /// </summary>
        IReadOnlyList<Models.DetectedGame> GetAllGames();
        
        /// <summary>
        /// Adiciona um jogo à biblioteca
        /// </summary>
        bool AddGame(Models.DetectedGame game);
        
        /// <summary>
        /// Remove um jogo da biblioteca por caminho do executável
        /// </summary>
        bool RemoveGame(string executablePath);
        
        /// <summary>
        /// Remove um jogo da biblioteca pelo nome
        /// </summary>
        bool RemoveGameByName(string gameName);
        
        /// <summary>
        /// Atualiza o perfil de um jogo
        /// </summary>
        void UpdateGameProfile(string gameName, Models.GameProfile profile);
        
        /// <summary>
        /// Verifica se um jogo está na biblioteca
        /// </summary>
        bool ContainsGame(string executablePath);
        
        /// <summary>
        /// Atualiza a biblioteca com novos jogos detectados
        /// </summary>
        Task<int> RefreshAsync(CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Salva a biblioteca em disco
        /// </summary>
        void Save();
        
        /// <summary>
        /// Carrega a biblioteca do disco
        /// </summary>
        void Load();
        
        /// <summary>
        /// Remove jogos com baixa confiança e entradas órfãs do cache
        /// </summary>
        int CleanCache(int minimumConfidence = 45);
    }

    /// <summary>
    /// Gerencia perfis de otimização por jogo
    /// </summary>
    public interface IGameProfileService
    {
        /// <summary>
        /// Obtém todos os perfis
        /// </summary>
        IReadOnlyList<Models.GameProfile> GetAllProfiles();
        
        /// <summary>
        /// Obtém perfil por nome do jogo
        /// </summary>
        Models.GameProfile? GetProfile(string gameName);
        
        /// <summary>
        /// Obtém perfil por caminho do executável
        /// </summary>
        Models.GameProfile? GetProfileByPath(string executablePath);
        
        /// <summary>
        /// Cria ou atualiza um perfil
        /// </summary>
        void SaveProfile(Models.GameProfile profile);
        
        /// <summary>
        /// Remove um perfil
        /// </summary>
        bool DeleteProfile(string gameName);
        
        /// <summary>
        /// Aplica um perfil de otimização
        /// </summary>
        Task<bool> ApplyProfileAsync(string gameName, IProgress<int>? progress = null, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Otimiza CPU para jogos
    /// </summary>
    public interface ICpuGamingOptimizer
    {
        /// <summary>
        /// Aplica otimizações de CPU para jogos
        /// </summary>
        Task<bool> OptimizeAsync(CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Restaura configurações originais de CPU
        /// </summary>
        Task<bool> RestoreAsync(CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Define prioridade alta para um processo de jogo
        /// </summary>
        bool SetGameProcessPriority(int processId, Models.ProcessPriorityLevel priority);
        
        /// <summary>
        /// Desabilita core parking
        /// </summary>
        Task<bool> DisableCoreParkingAsync();
        
        /// <summary>
        /// Aplica CPU sets para processo (CPUs híbridas)
        /// </summary>
        bool ApplyCpuSets(int processId);
    }

    /// <summary>
    /// Otimiza GPU para jogos
    /// </summary>
    public interface IGpuGamingOptimizer
    {
        /// <summary>
        /// Obtém informações da GPU
        /// </summary>
        Task<Models.GpuInfo> GetGpuInfoAsync(CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Obtém temperatura atual da GPU
        /// </summary>
        Task<Models.GpuTemperature> GetTemperatureAsync(CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Aplica otimizações de GPU para jogos
        /// </summary>
        Task<bool> OptimizeAsync(CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Restaura configurações originais de GPU
        /// </summary>
        Task<bool> RestoreAsync(CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Aplica otimizações de GPU com opções granulares
        /// </summary>
        Task<Models.GpuOptimizationResult> ApplyOptimizationsAsync(Models.GpuOptimizationOptions options, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Habilita/Desabilita HAGS
        /// </summary>

        Task<bool> SetHagsEnabledAsync(bool enabled);
        
        /// <summary>
        /// Aplica tweaks de TDR para jogos
        /// </summary>
        bool ApplyTdrTweaks(bool enable);
    }

    /// <summary>
    /// Otimiza rede para jogos
    /// </summary>
    public interface INetworkGamingOptimizer
    {
        /// <summary>
        /// Aplica otimizações de rede para jogos
        /// </summary>
        Task<bool> OptimizeAsync(CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Restaura configurações originais de rede
        /// </summary>
        Task<bool> RestoreAsync(CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Aplica QoS DSCP para um aplicativo
        /// </summary>
        bool ApplyQosDscp(string executablePath, int dscpValue = 46);
        
        /// <summary>
        /// Remove QoS DSCP de um aplicativo
        /// </summary>
        bool RemoveQosDscp(string executablePath);
        
        /// <summary>
        /// Configura interrupt moderation da NIC
        /// </summary>
        bool SetNicInterruptModeration(bool enabled);
        
        /// <summary>
        /// Mede latência para um host
        /// </summary>
        Task<double> MeasureLatencyAsync(string host, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Gerencia memória RAM para jogos
    /// </summary>
    public interface IMemoryGamingOptimizer
    {
        /// <summary>
        /// Limpa standby list
        /// </summary>
        bool CleanStandbyList();
        
        /// <summary>
        /// Inicia monitoramento contínuo de RAM
        /// </summary>
        void StartMonitoring(int thresholdMb = 1024, int standbyThresholdMb = 1024);
        
        /// <summary>
        /// Para monitoramento de RAM
        /// </summary>
        void StopMonitoring();
        
        /// <summary>
        /// Obtém memória livre em MB
        /// </summary>
        double GetFreeMemoryMb();
        
        /// <summary>
        /// Obtém tamanho da standby cache em MB
        /// </summary>
        double GetStandbyCacheMb();
    }

    /// <summary>
    /// Gerencia timer resolution do sistema
    /// </summary>
    public interface ITimerResolutionService
    {
        /// <summary>
        /// Define resolução máxima do timer
        /// </summary>
        bool SetMaximumResolution();
        
        /// <summary>
        /// Libera resolução do timer
        /// </summary>
        bool ReleaseResolution();
        
        /// <summary>
        /// Define resolução segura (1ms) — para uso com anti-cheat
        /// </summary>
        bool SetSafeResolution();

        /// <summary>
        /// Indica se a resolução máxima está ativa
        /// </summary>
        bool IsMaxResolutionActive { get; }
        
        /// <summary>
        /// Obtém resolução atual em 100ns units
        /// </summary>
        uint GetCurrentResolution();

        /// <summary>
        /// Obtém informações detalhadas da resolução
        /// </summary>
        (double current, double max) GetResolutionInfo();
    }

    /// <summary>
    /// Governança adaptativa de performance
    /// </summary>
    public interface IAdaptiveGovernor
    {
        /// <summary>
        /// Evento disparado quando um stutter é detectado
        /// </summary>
        event EventHandler<Models.StutterIncident>? StutterDetected;
        
        /// <summary>
        /// Inicia governança para um processo de jogo
        /// </summary>
        void Start(int gameProcessId, Models.GamerOptimizationOptions options);
        
        /// <summary>
        /// Para a governança
        /// </summary>
        void Stop();
        
        /// <summary>
        /// Indica se a governança está ativa
        /// </summary>
        bool IsRunning { get; }
        
        /// <summary>
        /// Obtém incidentes recentes de stutter
        /// </summary>
        IReadOnlyList<Models.StutterIncident> GetRecentIncidents();
        
        /// <summary>
        /// Limpa histórico de incidentes
        /// </summary>
        void ClearIncidents();
    }

    /// <summary>
    /// Detecta capacidades do hardware
    /// </summary>
    public interface IHardwareDetector
    {
        /// <summary>
        /// Obtém capacidades completas do hardware
        /// </summary>
        Task<Models.HardwareCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Obtém informações da GPU
        /// </summary>
        Task<Models.GpuInfo> GetGpuInfoAsync(CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Verifica se a CPU é híbrida (P-cores + E-cores)
        /// </summary>
        bool IsHybridCpu();
        
        /// <summary>
        /// Obtém quantidade de RAM em GB
        /// </summary>
        /// <summary>
        /// Obtém contagem de núcleos e threads (Síncrono)
        /// </summary>
        (int Cores, int Threads) GetCpuCounts();

        /// <summary>
        /// Obtém quantidade de RAM em GB
        /// </summary>
        int GetTotalRamGb();

        /// <summary>
        /// Verifica se dispositivo é Laptop/Notebook
        /// </summary>
        bool IsLaptop();

        /// <summary>
        /// Verifica se dispositivo está rodando na bateria
        /// </summary>
        bool IsOnBattery();
    }

    /// <summary>
    /// Gerencia processos para otimização
    /// </summary>
    public interface IProcessPrioritizer
    {
        /// <summary>
        /// Fecha processos desnecessários para jogos
        /// </summary>
        int CloseUnnecessaryProcesses();
        
        /// <summary>
        /// Define prioridade de um processo
        /// </summary>
        bool SetPriority(int processId, Models.ProcessPriorityLevel priority);
        
        /// <summary>
        /// Reduz prioridade de processos de background
        /// </summary>
        int LowerBackgroundProcessesPriority();
        
        /// <summary>
        /// Restaura prioridades normais
        /// </summary>
        void RestoreAllPriorities();
        
        /// <summary>
        /// Verifica se um processo é protegido (não pode ser fechado)
        /// </summary>
        bool IsProtectedProcess(string processName);
    }

    /// <summary>
    /// Otimiza o ambiente para imersão total (Focus Assist, Updates, Sleep)
    /// </summary>
    public interface IImmersiveGamingOptimizer
    {
        /// <summary>
        /// Aplica configurações de imersão (Silenciar notificações, pausar updates, etc)
        /// </summary>
        Task<bool> OptimizeAsync(CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Restaura configurações de ambiente originais
        /// </summary>
        Task<bool> RestoreAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Serviço de boost agressivo para jogos
    /// </summary>
    public interface IRealGameBoosterService : IDisposable
    {
        bool IsActive { get; }
        Task<RealGameBoostResult> ActivateFullBoostAsync(System.Diagnostics.Process? gameProcess = null, CancellationToken ct = default, RealGameBoostOptions? options = null);
        Task<bool> DeactivateAsync(CancellationToken ct = default);
        bool TryActivateStreamerMode();
    }
    
    /// <summary>
    /// 🚀 Serviço de cache em RAM para aceleração de jogos
    /// </summary>
    public interface IVoltrisRAMCacheService : IDisposable
    {
        bool IsActive { get; }
        int CacheSizeMB { get; }
        double HitRate { get; }
        bool Start(int cacheSizeMB = 2048);
        void Stop();
        bool TryGet(string filePath, long offset, int length, out byte[] data);
        void Put(string filePath, long offset, int length, byte[] data);
        CacheStatistics GetStatistics();
    }
}


