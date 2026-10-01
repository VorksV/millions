using System;

using System.Collections.Generic;

using System.Threading;

using System.Threading.Tasks;

using VoltrisOptimizer.Services.Gamer.Intelligence.Models;



namespace VoltrisOptimizer.Services.Gamer.Intelligence.Interfaces

{

    /// <summary>

    /// Classificador inteligente de hardware

    /// </summary>

    public interface IHardwareProfiler

    {

        /// <summary>

        /// Perfil atual do hardware

        /// </summary>

        HardwareProfile CurrentProfile { get; }



        /// <summary>

        /// Evento quando perfil é atualizado

        /// </summary>

        event EventHandler<HardwareProfile>? ProfileUpdated;



        /// <summary>

        /// Analisa e classifica o hardware do sistema

        /// </summary>

        Task<HardwareProfile> AnalyzeAsync(CancellationToken cancellationToken = default);



        /// <summary>

        /// Obtém estratégia de otimização recomendada

        /// </summary>

        OptimizationStrategy GetRecommendedStrategy(string? gameId = null);



        /// <summary>

        /// Verifica se o PC suporta uma otimização específica

        /// </summary>

        bool CanApplyOptimization(string optimizationName);

    }



    /// <summary>

    /// Otimizador de frame time

    /// </summary>

    public interface IFrameTimeOptimizer

    {

        /// <summary>

        /// Métricas atuais

        /// </summary>

        FrameTimeMetrics CurrentMetrics { get; }



        /// <summary>

        /// Evento quando stutter é detectado

        /// </summary>

        event EventHandler<StutterEvent>? StutterDetected;



        /// <summary>

        /// Inicia monitoramento de frame time

        /// </summary>

        void StartMonitoring(int processId);



        /// <summary>

        /// Para monitoramento

        /// </summary>

        void StopMonitoring();



        /// <summary>

        /// Obtém histórico de stutters

        /// </summary>

        IReadOnlyList<StutterEvent> GetStutterHistory();



        /// <summary>

        /// Aplica correções preventivas baseado em padrões

        /// </summary>

        Task<bool> ApplyPreventiveFixesAsync(CancellationToken cancellationToken = default);

    }



    /// <summary>

    /// Eliminador de input lag

    /// </summary>

    public interface IInputLagOptimizer

    {

        /// <summary>

        /// Métricas atuais de input lag

        /// </summary>

        InputLagMetrics CurrentMetrics { get; }



        /// <summary>

        /// Analisa input lag do sistema

        /// </summary>

        Task<InputLagMetrics> AnalyzeAsync(CancellationToken cancellationToken = default);



        /// <summary>

        /// Aplica todas as otimizações de input lag

        /// </summary>

        Task<bool> OptimizeAsync(CancellationToken cancellationToken = default);



        /// <summary>

        /// Restaura configurações originais

        /// </summary>

        Task<bool> RestoreAsync(CancellationToken cancellationToken = default);



        /// <summary>

        /// Otimiza polling rate do mouse

        /// </summary>

        bool OptimizeMousePolling();



        /// <summary>

        /// Desabilita composição do Windows

        /// </summary>

        bool DisableComposition();

    }



    /// <summary>

    /// Monitor de temperatura

    /// </summary>

    public interface IThermalMonitor

    {

        /// <summary>

        /// Perfil térmico atual

        /// </summary>

        ThermalProfile CurrentThermal { get; }



        /// <summary>

        /// Evento quando throttling é detectado

        /// </summary>

        event EventHandler<ThermalProfile>? ThrottlingDetected;



        /// <summary>

        /// Inicia monitoramento térmico

        /// </summary>

        void StartMonitoring(int intervalMs = 2000);



        /// <summary>

        /// Para monitoramento

        /// </summary>

        void StopMonitoring();



        /// <summary>

        /// Verifica se sistema está em throttling

        /// </summary>

        bool IsThrottling();



        /// <summary>

        /// Obtém ação recomendada baseado em temperaturas

        /// </summary>

        ThermalAction GetRecommendedAction();

    }



    public enum ThermalAction

    {

        None,

        ReduceOptimizations,

        PauseOptimizations,

        EmergencyThrottle

    }



    /// <summary>

    /// Gerenciador de perfis inteligentes por jogo

    /// </summary>

    public interface IGameIntelligence

    {

        /// <summary>

        /// Obtém perfil inteligente de um jogo

        /// </summary>

        GameIntelligenceProfile? GetGameProfile(string gameNameOrExe);



        /// <summary>

        /// Detecta categoria de um jogo

        /// </summary>

        GameCategory DetectGameCategory(string executablePath);



        /// <summary>

        /// Obtém otimizações recomendadas para um jogo

        /// </summary>

        OptimizationStrategy GetGameStrategy(string gameNameOrExe, HardwareProfile hardware);



        /// <summary>

        /// Registra métricas de jogo para aprendizado

        /// </summary>

        void RecordGameMetrics(string gameId, FrameTimeMetrics metrics);



        /// <summary>

        /// Obtém configurações in-game recomendadas

        /// </summary>

        Dictionary<string, string> GetRecommendedSettings(string gameId, HardwareProfile hardware);



        /// <summary>

        /// 🔥 MÉTODO ADICIONADO: Aplicar otimizações de jogo

        /// </summary>

        Task<bool> ApplyGameOptimizationsAsync();

    }



    /// <summary>

    /// Inteligência de rede

    /// </summary>

    public interface INetworkIntelligence

    {

        /// <summary>

        /// Detecta servidor do jogo automaticamente

        /// </summary>

        Task<Models.NetworkIntelligence?> DetectGameServerAsync(int processId, CancellationToken cancellationToken = default);



        /// <summary>

        /// Aplica otimizações de rede específicas para jogo

        /// </summary>

        Task<bool> OptimizeForGameAsync(string gameId, CancellationToken cancellationToken = default);



        /// <summary>

        /// Mede latência para servidor específico

        /// </summary>

        Task<double> MeasureLatencyAsync(string serverIp, CancellationToken cancellationToken = default);



        /// <summary>

        /// Obtém melhor configuração de DNS

        /// </summary>

        Task<string> GetOptimalDnsAsync(CancellationToken cancellationToken = default);

    }



    /// <summary>

    /// Gerenciador de VRAM

    /// </summary>

    public interface IVramManager

    {

        /// <summary>

        /// Status atual de VRAM

        /// </summary>

        VramStatus CurrentStatus { get; }



        /// <summary>

        /// Evento quando VRAM está crítica

        /// </summary>

        event EventHandler<VramStatus>? VramCritical;



        /// <summary>

        /// Inicia monitoramento de VRAM

        /// </summary>

        void StartMonitoring(int intervalMs = 1000);



        /// <summary>

        /// Para monitoramento

        /// </summary>

        void StopMonitoring();



        /// <summary>

        /// Libera VRAM de processos não essenciais

        /// </summary>

        Task<long> FreeVramAsync(CancellationToken cancellationToken = default);



        /// <summary>

        /// Obtém consumidores de VRAM

        /// </summary>

        IReadOnlyList<VramConsumer> GetTopConsumers();

    }



    /// <summary>

    /// Balanceador de energia

    /// </summary>

    public interface IPowerBalancer

    {

        /// <summary>

        /// Perfil de energia atual

        /// </summary>

        VoltrisOptimizer.Services.Gamer.Intelligence.Models.PowerProfile CurrentPower { get; }



        /// <summary>

        /// Evento quando status de bateria muda

        /// </summary>

        event EventHandler<VoltrisOptimizer.Services.Gamer.Intelligence.Models.PowerProfile>? PowerStatusChanged;



        /// <summary>

        /// Detecta modo de energia ideal

        /// </summary>

        PowerMode GetOptimalPowerMode(bool isGaming);



        /// <summary>

        /// Aplica modo de energia

        /// </summary>

        Task<bool> ApplyPowerModeAsync(PowerMode mode, CancellationToken cancellationToken = default);



        /// <summary>

        /// Ajusta otimizações baseado em bateria

        /// </summary>

        OptimizationStrategy AdjustForBattery(OptimizationStrategy baseStrategy);

    }



    public enum PowerMode

    {

        BatterySaver,

        Balanced,

        Performance,

        UltimatePerformance,

        GamingOnBattery,

        GamingPluggedIn

    }



    /// <summary>

    /// Sistema de benchmark

    /// </summary>

    public interface IAutoBenchmark

    {

        /// <summary>

        /// Último resultado de benchmark

        /// </summary>

        BenchmarkResult? LastResult { get; }



        /// <summary>

        /// Executa benchmark completo

        /// </summary>

        Task<BenchmarkResult> RunFullBenchmarkAsync(IProgress<int>? progress = null, CancellationToken cancellationToken = default);



        /// <summary>

        /// Executa benchmark rápido

        /// </summary>

        Task<BenchmarkResult> RunQuickBenchmarkAsync(CancellationToken cancellationToken = default);



        /// <summary>

        /// Obtém comparação com outros PCs

        /// </summary>

        int GetPercentileRank(int score);



        /// <summary>

        /// Obtém recomendações baseado no resultado

        /// </summary>

        IReadOnlyList<BenchmarkRecommendation> GetRecommendations(BenchmarkResult result);

    }



    /// <summary>

    /// Orquestrador de inteligência - coordena todos os módulos

    /// </summary>

    public interface IIntelligenceOrchestrator

    {

        /// <summary>

        /// Inicializa todos os módulos de inteligência

        /// </summary>

        Task InitializeAsync(CancellationToken cancellationToken = default);



        /// <summary>

        /// Obtém perfil de hardware atual

        /// </summary>

        HardwareProfile GetHardwareProfile();



        /// <summary>

        /// Obtém estratégia otimizada para um jogo

        /// </summary>

        OptimizationStrategy GetOptimizedStrategy(string? gameId = null);



        /// <summary>

        /// Aplica estratégia de otimização

        /// </summary>

        Task<bool> ApplyStrategyAsync(OptimizationStrategy strategy, int? gameProcessId = null, CancellationToken cancellationToken = default);



        /// <summary>

        /// Reverte todas as otimizações

        /// </summary>

        Task<bool> RevertAllAsync(CancellationToken cancellationToken = default);



        /// <summary>

        /// Obtém status de todos os módulos

        /// </summary>

        IntelligenceStatus GetStatus();

    }



    public class IntelligenceStatus

    {

        public HardwareClass HardwareClass { get; set; }

        public bool IsMonitoringFrameTime { get; set; }

        public bool IsMonitoringThermal { get; set; }

        public bool IsMonitoringVram { get; set; }

        public bool IsOptimizationActive { get; set; }

        public string ActiveGame { get; set; } = "";

        public FrameTimeMetrics? CurrentFrameMetrics { get; set; }

        public ThermalProfile? CurrentThermal { get; set; }

        public VramStatus? CurrentVram { get; set; }

        public int ActiveOptimizations { get; set; }

    }



    /// <summary>

    /// Engine adaptativa de hardware

    /// </summary>

    public interface IAdaptiveHardwareEngine

    {

        bool IsRunning { get; }

        Task StartEngineAsync(int? gameProcessId = null, CancellationToken cancellationToken = default);

        Task StopEngineAsync(CancellationToken cancellationToken = default);

        string GetCurrentClassification();

    }

}



