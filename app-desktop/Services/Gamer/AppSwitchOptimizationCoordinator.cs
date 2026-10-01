using System;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer
{
    /// <summary>
    /// Coordenador de otimização para trocas de contexto entre aplicativos
    /// Integra todos os serviços especializados para eliminar travamentos durante app-switching
    /// </summary>
    public class AppSwitchOptimizationCoordinator : IDisposable
    {
        private readonly ILoggingService _logger;
        private readonly ContextSwitchDetectorService _contextDetector;
        private readonly ResourcePreAllocatorService _resourcePreAllocator;
        private readonly PriorityCacheService _priorityCache;
        private readonly SystemNotificationBlockerService _notificationBlocker;
        private readonly LoadingPhaseOptimizerService _loadingOptimizer;
        
        private int _gameProcessId;
        private bool _isRunning = false;
        private readonly object _lock = new();
        
        public AppSwitchOptimizationCoordinator(
            ILoggingService logger,
            ContextSwitchDetectorService contextDetector,
            ResourcePreAllocatorService resourcePreAllocator,
            PriorityCacheService priorityCache,
            SystemNotificationBlockerService notificationBlocker,
            LoadingPhaseOptimizerService loadingOptimizer)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _contextDetector = contextDetector ?? throw new ArgumentNullException(nameof(contextDetector));
            _resourcePreAllocator = resourcePreAllocator ?? throw new ArgumentNullException(nameof(resourcePreAllocator));
            _priorityCache = priorityCache ?? throw new ArgumentNullException(nameof(priorityCache));
            _notificationBlocker = notificationBlocker ?? throw new ArgumentNullException(nameof(notificationBlocker));
            _loadingOptimizer = loadingOptimizer ?? throw new ArgumentNullException(nameof(loadingOptimizer));
            
            _logger.LogEntry(nameof(AppSwitchOptimizationCoordinator));
            
            // Registrar eventos do detector de contexto
            _contextDetector.AppSwitchDetected += OnAppSwitchDetected;
            _contextDetector.SwitchPatternIdentified += OnSwitchPatternIdentified;
            
            // Registrar eventos do otimizador de carregamento
            _loadingOptimizer.OnLoadingPhaseStarted += OnLoadingPhaseStarted;
            _loadingOptimizer.OnLoadingPhaseEnded += OnLoadingPhaseEnded;
            
            _logger.LogExit(nameof(AppSwitchOptimizationCoordinator));
        }
        
        /// <summary>
        /// Inicia todos os serviços de otimização
        /// </summary>
        public async Task StartAsync(int gameProcessId, CancellationToken ct = default)
        {
            lock (_lock)
            {
                if (_isRunning)
                    return;
                
                _gameProcessId = gameProcessId;
                _isRunning = true;
            }
            
            _logger.LogEntry(nameof(StartAsync));
            
            try
            {
                // Iniciar todos os serviços especializados
                _contextDetector.StartMonitoring(gameProcessId);
                _resourcePreAllocator.Start(gameProcessId);
                _priorityCache.Start(gameProcessId);
                _notificationBlocker.StartBlocking(gameProcessId);
                _loadingOptimizer.StartMonitoring(gameProcessId);
                
                _logger.LogInfo($"[AppSwitchCoord] Todos os serviços de otimização iniciados para processo {gameProcessId}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[AppSwitchCoord] Erro ao iniciar serviços: {ex.Message}");
                throw;
            }
            finally
            {
                _logger.LogExit(nameof(StartAsync));
            }
        }
        
        /// <summary>
        /// Para todos os serviços de otimização
        /// </summary>
        public async Task StopAsync(CancellationToken ct = default)
        {
            lock (_lock)
            {
                if (!_isRunning)
                    return;
                
                _isRunning = false;
            }
            
            _logger.LogEntry(nameof(StopAsync));
            
            try
            {
                // Parar todos os serviços especializados
                _contextDetector.StopMonitoring();
                _resourcePreAllocator.Stop();
                _priorityCache.Stop();
                _notificationBlocker.StopBlocking();
                _loadingOptimizer.StopMonitoring();
                
                _logger.LogInfo("[AppSwitchCoord] Todos os serviços de otimização parados");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[AppSwitchCoord] Erro ao parar serviços: {ex.Message}");
            }
            
            _logger.LogExit(nameof(StopAsync));
        }
        
        /// <summary>
        /// Handler para detecção de troca de contexto
        /// </summary>
        private void OnAppSwitchDetected(object? sender, AppSwitchEventArgs e)
        {
            _logger.LogEntry(nameof(OnAppSwitchDetected));
            try
            {
                // Coordenar todas as otimizações necessárias
                HandlePriorityOptimization(e);
                HandleResourceOptimization(e);
                HandleBackgroundOptimization(e);
                HandleLatencyOptimization(e);
                HandleNotificationBlocking(e); // Nova otimização
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[AppSwitchCoord] Erro no handler de app switch: {ex.Message}");
            }
            _logger.LogExit(nameof(OnAppSwitchDetected));
        }
        
        /// <summary>
        /// Handler para identificação de padrões de troca
        /// </summary>
        private void OnSwitchPatternIdentified(object? sender, SwitchPatternEventArgs e)
        {
            _logger.LogEntry(nameof(OnSwitchPatternIdentified));
            try
            {
                _logger.LogInfo($"[AppSwitchCoord] Padrão identificado: {e.PatternType} (frequência: {e.SwitchesPerMinute:F2}/s)");
                
                // Ajustar estratégias baseado em padrões
                AdjustStrategiesBasedOnPatterns(e);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[AppSwitchCoord] Erro no handler de padrões: {ex.Message}");
            }
            _logger.LogExit(nameof(OnSwitchPatternIdentified));
        }
        
        /// <summary>
        /// Handler para início de fase de carregamento
        /// </summary>
        private void OnLoadingPhaseStarted(object? sender, EventArgs e)
        {
            _logger.LogEntry(nameof(OnLoadingPhaseStarted));
            try
            {
                _logger.LogInfo("[AppSwitchCoord] Fase de carregamento iniciada - aplicando otimizações");
                
                // ❌ REMOVIDO: IntelligentBackgroundSuspender desativado
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[AppSwitchCoord] Erro no handler de carregamento: {ex.Message}");
            }
            _logger.LogExit(nameof(OnLoadingPhaseStarted));
        }
        
        /// <summary>
        /// Handler para fim de fase de carregamento
        /// </summary>
        private void OnLoadingPhaseEnded(object? sender, EventArgs e)
        {
            _logger.LogEntry(nameof(OnLoadingPhaseEnded));
            try
            {
                _logger.LogInfo("[AppSwitchCoord] Fase de carregamento encerrada - restaurando prioridades");
                
                // ❌ REMOVIDO: IntelligentBackgroundSuspender desativado
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[AppSwitchCoord] Erro no handler de fim de carregamento: {ex.Message}");
            }
            _logger.LogExit(nameof(OnLoadingPhaseEnded));
        }
        
        /// <summary>
        /// Otimização de prioridades durante troca
        /// </summary>
        private void HandlePriorityOptimization(AppSwitchEventArgs e)
        {
            _logger.LogEntry(nameof(HandlePriorityOptimization));
            try
            {
                // BOOST TRANSITÓRIO DE ALT+TAB:
                // Quando trocando de foco, garantir que o novo processo alvo ganhe responsividade imediata.
                if (e.ToProcessId > 0 && e.ToProcessId != _gameProcessId)
                {
                    try
                    {
                        using var toProc = System.Diagnostics.Process.GetProcessById(e.ToProcessId);
                        if (!toProc.HasExited && toProc.PriorityClass < System.Diagnostics.ProcessPriorityClass.Normal)
                        {
                            toProc.PriorityClass = System.Diagnostics.ProcessPriorityClass.Normal;
                            _logger.LogDebug($"[AppSwitchCoord] Boost transitório aplicado ao processo {e.ToProcessName} (PID: {e.ToProcessId}) para Normal");
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[AppSwitchCoord] Erro na otimização de prioridades: {ex.Message}");
            }
            _logger.LogExit(nameof(HandlePriorityOptimization));
        }
        
        /// <summary>
        /// Otimização de recursos durante troca
        /// </summary>
        private void HandleResourceOptimization(AppSwitchEventArgs e)
        {
            _logger.LogEntry(nameof(HandleResourceOptimization));
            try
            {
                // O pré-alocador já lida com isso automaticamente
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[AppSwitchCoord] Erro na otimização de recursos: {ex.Message}");
            }
            _logger.LogExit(nameof(HandleResourceOptimization));
        }
        
        /// <summary>
        /// Otimização de processos em background
        /// </summary>
        private void HandleBackgroundOptimization(AppSwitchEventArgs e)
        {
            _logger.LogEntry(nameof(HandleBackgroundOptimization));
            try
            {
                // ❌ REMOVIDO: IntelligentBackgroundSuspender desativado (V2 Architecture)
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[AppSwitchCoord] Erro na otimização de background: {ex.Message}");
            }
            _logger.LogExit(nameof(HandleBackgroundOptimization));
        }
        
        /// <summary>
        /// Otimização de latência de input
        /// </summary>
        private void HandleLatencyOptimization(AppSwitchEventArgs e)
        {
            _logger.LogEntry(nameof(HandleLatencyOptimization));
            try
            {
                // ❌ REMOVIDO: InputLatencyMonitorService desativado (V2 Architecture)
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[AppSwitchCoord] Erro na otimização de latência: {ex.Message}");
            }
            _logger.LogExit(nameof(HandleLatencyOptimization));
        }
        
        /// <summary>
        /// Bloqueio de notificações durante troca
        /// </summary>
        private void HandleNotificationBlocking(AppSwitchEventArgs e)
        {
            _logger.LogEntry(nameof(HandleNotificationBlocking));
            try
            {
                // O bloqueador de notificações já está ativo, mas podemos reforçar
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[AppSwitchCoord] Erro no bloqueio de notificações: {ex.Message}");
            }
            _logger.LogExit(nameof(HandleNotificationBlocking));
        }
        
        /// <summary>
        /// Ajusta estratégias baseado em padrões identificados
        /// </summary>
        private void AdjustStrategiesBasedOnPatterns(SwitchPatternEventArgs e)
        {
            _logger.LogEntry(nameof(AdjustStrategiesBasedOnPatterns));
            try
            {
                // Ajustar comportamento baseado na frequência de trocas
                if (e.SwitchesPerMinute > 120.0) // Mais de 2 trocas por segundo
                {
                    _logger.LogInfo("[AppSwitchCoord] Alta frequência detectada - otimizando para multitasking");
                    // Estratégia mais conservadora para preservar performance
                }
                else if (e.SwitchesPerMinute < 12.0) // Menos de 1 troca a cada 5 segundos
                {
                    _logger.LogInfo("[AppSwitchCoord] Baixa frequência detectada - otimizando para performance pura");
                    // Estratégia mais agressiva para maximizar performance do jogo
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[AppSwitchCoord] Erro ao ajustar estratégias: {ex.Message}");
            }
            _logger.LogExit(nameof(AdjustStrategiesBasedOnPatterns));
        }
        
        public void Dispose()
        {
            _logger.LogEntry(nameof(Dispose));
            // Cancelar eventos
            _contextDetector.AppSwitchDetected -= OnAppSwitchDetected;
            _contextDetector.SwitchPatternIdentified -= OnSwitchPatternIdentified;
            _loadingOptimizer.OnLoadingPhaseStarted -= OnLoadingPhaseStarted;
            _loadingOptimizer.OnLoadingPhaseEnded -= OnLoadingPhaseEnded;
            
            // Parar serviços
            try
            {
                StopAsync().Wait(1000); // Esperar no máximo 1 segundo
            }
            catch
            {
                // Ignorar erros ao parar serviços durante o dispose
            }
            _logger.LogExit(nameof(Dispose));
        }
    }
}
