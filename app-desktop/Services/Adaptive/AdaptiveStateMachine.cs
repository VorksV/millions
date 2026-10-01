using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Monitoring;

namespace VoltrisOptimizer.Services.Adaptive
{
    /// <summary>
    /// State Machine adaptativa para modo gamer
    /// Ajusta comportamento com base em métricas em tempo real
    /// </summary>
    public class AdaptiveStateMachine
    {
        /// <summary>
        /// Estados possíveis do modo gamer
        /// </summary>
        public enum GamerState
        {
            Stable,         // Tudo OK - monitoramento leve
            HighLoad,       // CPU/GPU alta - reduzir monitoramento
            ThermalRisk,    // CPU > 80°C - desabilitar overclock
            MemoryPressure, // RAM > 90% - liberar memória
            LatencyRisk,    // Input lag > 50ms - priorizar jogo
            Recovery        // Pós-problema - aguardar estabilização
        }
        
        private GamerState _currentState = GamerState.Stable;
        private DateTime _lastStateChange = DateTime.MinValue;
        
        private readonly GamerOptimizerService _gamerService;
        private readonly MetricsCollector _metrics;
        private readonly ILoggingService _logger;
        
        // Configuração adaptativa por estado
        private readonly Dictionary<GamerState, StateConfiguration> _stateConfigs;
        
        public AdaptiveStateMachine(
            GamerOptimizerService gamerService,
            MetricsCollector metrics,
            ILoggingService logger)
        {
            _gamerService = gamerService ?? throw new ArgumentNullException(nameof(gamerService));
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            _stateConfigs = InitializeStateConfigurations();
        }
        
        /// <summary>
        /// Inicializa configurações para cada estado
        /// </summary>
        private Dictionary<GamerState, StateConfiguration> InitializeStateConfigurations()
        {
            return new Dictionary<GamerState, StateConfiguration>
            {
                [GamerState.Stable] = new StateConfiguration
                {
                    MonitoringFrequencyMs = 5000,  // 5s
                    OptimizationIntensity = OptimizationLevel.Moderate,
                    EnabledServices = ServiceFlags.All,
                    Description = "Sistema estável - monitoramento normal"
                },
                [GamerState.HighLoad] = new StateConfiguration
                {
                    MonitoringFrequencyMs = 30000, // 30s - reduzir overhead
                    OptimizationIntensity = OptimizationLevel.Conservative,
                    EnabledServices = ServiceFlags.Essential,
                    Description = "Alta carga - reduzir monitoramento para evitar overhead"
                },
                [GamerState.ThermalRisk] = new StateConfiguration
                {
                    MonitoringFrequencyMs = 10000,
                    OptimizationIntensity = OptimizationLevel.ThermalSafe,
                    EnabledServices = ServiceFlags.Thermal,
                    Description = "Risco térmico - desabilitar tweaks agressivos"
                },
                [GamerState.MemoryPressure] = new StateConfiguration
                {
                    MonitoringFrequencyMs = 15000,
                    OptimizationIntensity = OptimizationLevel.MemoryOptimized,
                    EnabledServices = ServiceFlags.Memory,
                    Description = "Pressão de memória - liberar RAM"
                },
                [GamerState.LatencyRisk] = new StateConfiguration
                {
                    MonitoringFrequencyMs = 3000,  // Mais frequente
                    OptimizationIntensity = OptimizationLevel.Aggressive,
                    EnabledServices = ServiceFlags.Latency,
                    Description = "Latência alta - priorizar responsividade"
                },
                [GamerState.Recovery] = new StateConfiguration
                {
                    MonitoringFrequencyMs = 60000, // 1min - deixar sistema estabilizar
                    OptimizationIntensity = OptimizationLevel.None,
                    EnabledServices = ServiceFlags.None,
                    Description = "Recuperação - aguardar estabilização"
                }
            };
        }
        
        /// <summary>
        /// Estado atual do sistema
        /// </summary>
        public GamerState CurrentState => _currentState;
        
        /// <summary>
        /// Configuração do estado atual
        /// </summary>
        public StateConfiguration CurrentConfiguration => _stateConfigs[_currentState];
        
        /// <summary>
        /// Atualiza estado baseado em métricas atuais
        /// </summary>
        public async Task UpdateStateAsync(SystemMetrics metrics)
        {
            var previousState = _currentState;
            var newState = DetermineOptimalState(metrics);
            
            if (newState != previousState)
            {
                await TransitionToStateAsync(previousState, newState);
            }
        }
        
        /// <summary>
        /// Determina estado ótimo baseado em métricas
        /// PRIORIDADE: Recovery > ThermalRisk > MemoryPressure > LatencyRisk > HighLoad > Stable
        /// </summary>
        private GamerState DetermineOptimalState(SystemMetrics metrics)
        {
            // RECOVERY: Aguardar 60s antes de sair do recovery
            if (_currentState == GamerState.Recovery)
            {
                if ((DateTime.Now - _lastStateChange).TotalSeconds < 60)
                    return GamerState.Recovery;
            }
            
            // THERMAL RISK: Sempre tem prioridade máxima (evita danos ao hardware)
            if (metrics.CpuTemperature > 80 || metrics.GpuTemperature > 85)
            {
                _logger.LogWarning($"[StateMachine] ⚠️ Thermal risk: CPU={metrics.CpuTemperature}°C GPU={metrics.GpuTemperature}°C");
                return GamerState.ThermalRisk;
            }
                
            // MEMORY PRESSURE: Segunda prioridade
            if (metrics.RamPressure > 90)
            {
                _logger.LogWarning($"[StateMachine] ⚠️ Memory pressure: {metrics.RamPressure:F1}%");
                return GamerState.MemoryPressure;
            }
                
            // LATENCY RISK: Input lag crítico
            if (metrics.InputLatency > 50)
            {
                _logger.LogWarning($"[StateMachine] ⚠️ Latency risk: {metrics.InputLatency:F1}ms");
                return GamerState.LatencyRisk;
            }
                
            // HIGH LOAD: CPU/GPU alta (mas estável)
            if (metrics.CpuUsage > 85 || metrics.GpuLoad > 95)
            {
                _logger.LogInfo($"[StateMachine] ℹ️ High load: CPU={metrics.CpuUsage:F1}% GPU={metrics.GpuLoad:F1}%");
                return GamerState.HighLoad;
            }
                
            // STABLE: Tudo OK
            return GamerState.Stable;
        }
        
        /// <summary>
        /// Executa transição entre estados
        /// </summary>
        private async Task TransitionToStateAsync(GamerState from, GamerState to)
        {
            _logger.LogInfo($"[StateMachine] 🔄 Transição: {from} → {to}");
            
            _lastStateChange = DateTime.Now;
            _currentState = to;
            
            var config = _stateConfigs[to];
            
            try
            {
                // ADAPTAR COMPORTAMENTO DO GAMER SERVICE EXISTENTE
                await ApplyStateConfigurationAsync(config);
                
                // Evento para UI
                StateChanged?.Invoke(this, new StateChangedEventArgs(from, to, config));
                
                _logger.LogSuccess($"[StateMachine] ✅ Estado aplicado: {config.Description}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[StateMachine] Erro ao aplicar estado {to}", ex);
            }
        }
        
        /// <summary>
        /// Aplica configuração específica do estado
        /// </summary>
        private async Task ApplyStateConfigurationAsync(StateConfiguration config)
        {
            switch (config.OptimizationIntensity)
            {
                case OptimizationLevel.Aggressive:
                    // Modo máximo: ativar gamer mode SE não estiver ativo
                    if (!_gamerService.IsGamerModeActive)
                    {
                        await _gamerService.ActivateGamerModeAsync();
                    }
                    _gamerService.SetAdaptiveGovernorEnabled(true);
                    _logger.LogInfo("[StateMachine] Modo agressivo ativado");
                    break;
                    
                case OptimizationLevel.Moderate:
                    // Modo padrão: gamer mode ativo, adaptive governor DESABILITADO
                    if (!_gamerService.IsGamerModeActive)
                    {
                        await _gamerService.ActivateGamerModeAsync();
                    }
                    _gamerService.SetAdaptiveGovernorEnabled(false);
                    _logger.LogInfo("[StateMachine] Modo moderado ativado");
                    break;
                    
                case OptimizationLevel.Conservative:
                    // Modo conservador: desabilitar tweaks agressivos
                    _gamerService.SetAdaptiveGovernorEnabled(false);
                    _logger.LogInfo("[StateMachine] Modo conservador ativado");
                    break;
                    
                case OptimizationLevel.ThermalSafe:
                    // Modo térmico: DESABILITAR tudo que aumenta temperatura
                    _gamerService.SetAdaptiveGovernorEnabled(false);
                    _logger.LogWarning("[StateMachine] ⚠️ Modo térmico - reduzindo intensidade");
                    break;
                    
                case OptimizationLevel.MemoryOptimized:
                    // Modo memória: priorizar liberação de RAM
                    _logger.LogInfo("[StateMachine] Modo otimizado para memória");
                    // TODO: Integrar com SystemCleaner para liberar RAM
                    break;
                    
                case OptimizationLevel.None:
                    // Recovery: NÃO fazer nada
                    _logger.LogInfo("[StateMachine] ℹ️ Recovery mode - aguardando estabilização");
                    break;
            }
        }
        
        /// <summary>
        /// Força transição para estado específico (override manual)
        /// </summary>
        public async Task ForceStateAsync(GamerState targetState)
        {
            _logger.LogWarning($"[StateMachine] ⚠️ Forçando estado: {targetState}");
            await TransitionToStateAsync(_currentState, targetState);
        }
        
        /// <summary>
        /// Evento disparado quando estado muda
        /// </summary>
        public event EventHandler<StateChangedEventArgs>? StateChanged;
    }
    
    #region Events
    
    /// <summary>
    /// Evento de mudança de estado
    /// </summary>
    public class StateChangedEventArgs : EventArgs
    {
        public AdaptiveStateMachine.GamerState FromState { get; }
        public AdaptiveStateMachine.GamerState ToState { get; }
        public StateConfiguration Configuration { get; }
        
        public StateChangedEventArgs(
            AdaptiveStateMachine.GamerState from,
            AdaptiveStateMachine.GamerState to,
            StateConfiguration config)
        {
            FromState = from;
            ToState = to;
            Configuration = config;
        }
    }
    
    #endregion
}
