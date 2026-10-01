using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core.Brain.V2;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.Gamer.Adaptive.Intelligence;
using VoltrisOptimizer.Services.Gamer.Adaptive.Models;
using VoltrisOptimizer.Services.Gamer.Adaptive.Monitoring;
using VoltrisOptimizer.Services.Gamer.Adaptive.Persistence;
using VoltrisOptimizer.Services.Gamer.Adaptive.Execution;
using VoltrisOptimizer.Services.Gamer.Interfaces;

namespace VoltrisOptimizer.Services.Gamer.Adaptive
{
    /// <summary>
    /// Orquestrador unificado do Gamer Mode V3 - Sistema adaptativo completo
    /// </summary>
    public class GamerModeOrchestratorV3 : IGamerModeOrchestrator
    {
        private readonly ILoggingService _logger;
        private readonly AdaptiveOptimizationEngine _optimizationEngine;
        private readonly RobustGameMonitor _gameMonitor;
        private readonly StateManager _stateManager;
        private readonly CrashRecoveryService _crashRecovery;
        private readonly VoltrisBrainV2 _brain;
        private readonly List<IOptimizationModule> _modules;
        
        private readonly object _lock = new();
        private readonly SemaphoreSlim _operationLock = new(1, 1);
        
        private ExecutionPlan? _activeExecutionPlan;
        private GamerSessionState _sessionState = new();
        private VoltrisOptimizer.Services.Gamer.Models.GamerOptimizationOptions _currentOptions = new();
        private bool _isActive = false;
        private bool _autoPilotActive = false;
        private DateTime _sessionStartTime;
        
        // Events
        public event EventHandler<VoltrisOptimizer.Services.Gamer.Models.GamerModeStatus>? StatusChanged;
        
        public GamerModeOrchestratorV3(
            ILoggingService logger,
            AdaptiveOptimizationEngine optimizationEngine,
            RobustGameMonitor gameMonitor,
            StateManager stateManager,
            CrashRecoveryService crashRecovery,
            VoltrisBrainV2 brain,
            IEnumerable<IOptimizationModule> modules)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _optimizationEngine = optimizationEngine ?? throw new ArgumentNullException(nameof(optimizationEngine));
            _gameMonitor = gameMonitor ?? throw new ArgumentNullException(nameof(gameMonitor));
            _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
            _crashRecovery = crashRecovery ?? throw new ArgumentNullException(nameof(crashRecovery));
            _brain = brain ?? throw new ArgumentNullException(nameof(brain));
            _modules = modules.ToList();
            
            // Subscribe to game monitor events
            _gameMonitor.GameTerminated += OnGameTerminated;
        }
        
        /// <summary>
        /// Indica se o Gamer Mode está ativo
        /// </summary>
        public bool IsActive
        {
            get
            {
                lock (_lock)
                {
                    return _isActive;
                }
            }
        }
        
        /// <summary>
        /// Status atual do Gamer Mode
        /// </summary>
        public VoltrisOptimizer.Services.Gamer.Models.GamerModeStatus Status
        {
            get
            {
                lock (_lock)
                {
                    return new VoltrisOptimizer.Services.Gamer.Models.GamerModeStatus
                    {
                        IsActive = _isActive,
                        ActiveGameName = _activeExecutionPlan?.Game.Name ?? string.Empty,
                        ActivatedAt = _sessionState.StartedAt,
                        EndTime = _isActive ? null : DateTime.Now
                    };
                }
            }
        }
        
        /// <summary>
        /// Inicializa o orquestrador - executa crash recovery se necessário
        /// </summary>
        public async Task InitializeAsync()
        {
            _logger.LogInfo(nameof(InitializeAsync));
            
            _logger.LogInfo("═══════════════════════════════════════════════════════════════");
            _logger.LogInfo("[GamerModeV3] 🚀 INICIALIZANDO SISTEMA ADAPTATIVO");
            _logger.LogInfo($"[GamerModeV3] Modules: {_modules.Count}");
            _logger.LogInfo("═══════════════════════════════════════════════════════════════");
            
            try
            {
                // Execute crash recovery check
                var recoveryResult = await _crashRecovery.CheckAndRecoverAsync();
                
                if (recoveryResult.HasPendingSession)
                {
                    _logger.LogInfo($"[GamerModeV3] Crash recovery: {(recoveryResult.RecoverySuccessful ? "SUCESSO" : "FALHA")}");
                    
                    // Report to brain
                    var rewardValue = recoveryResult.RecoverySuccessful ? 0.8 : -0.5;
                    await ReportToBrainAsync("crash_recovery", rewardValue, new 
                    { 
                        sessionAge = recoveryResult.SessionAge.TotalMinutes,
                        recovered = recoveryResult.RecoverySuccessful 
                    });
                }
                
                _logger.LogSuccess("[GamerModeV3] ✅ Inicialização concluída");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GamerModeV3] Erro na inicialização: {ex.Message}", ex);
            }
            
            _logger.LogInfo(nameof(InitializeAsync));
        }
        
        /// <summary>
        /// Ativa Gamer Mode para um jogo específico
        /// </summary>
        public async Task<bool> ActivateAsync(
            VoltrisOptimizer.Services.Gamer.Models.GamerOptimizationOptions options, 
            string? gameExecutable = null, 
            IProgress<int>? progress = null, 
            CancellationToken cancellationToken = default,
            bool isManual = true)
        {
            _logger.LogInfo(nameof(ActivateAsync));
            
            // Store options
            _currentOptions = options;
            
            // License gate
            if (!VoltrisOptimizer.Services.License.ProFeatureGuard.IsPaidActive())
            {
                if (isManual)
                {
                    VoltrisOptimizer.Services.License.ProFeatureGuard.RequirePaid("gamer_mode");
                }
                else
                {
                    _logger.LogInfo("[GamerModeV3] Ativação automática ignorada: licença paga necessária");
                }
                return false;
            }
            
            await _operationLock.WaitAsync(cancellationToken);
            
            try
            {
                lock (_lock)
                {
                    if (_isActive)
                    {
                        _logger.LogWarning("[GamerModeV3] ⚠️ Modo Gamer já está ativo");
                        return true;
                    }
                }
                
                _logger.LogInfo("═══════════════════════════════════════════════════════════════");
                _logger.LogInfo("[GamerModeV3] 🎮 INICIANDO ATIVAÇÃO ADAPTATIVA");
                _logger.LogInfo($"[GamerModeV3] Game Executable: {gameExecutable ?? "Auto-detect"}");
                _logger.LogInfo($"[GamerModeV3] Manual Activation: {isManual}");
                _logger.LogInfo("═══════════════════════════════════════════════════════════════");
                
                progress?.Report(10);
                
                // 1. Create game profile
                var gameProfile = await CreateGameProfileAsync(gameExecutable);
                progress?.Report(20);
                
                // 2. Create adaptive execution plan
                _logger.LogInfo("[GamerModeV3] 🧠 Criando plano de execução adaptativo...");
                _activeExecutionPlan = await _optimizationEngine.CreateExecutionPlanAsync(gameProfile);
                progress?.Report(40);
                
                // 3. Execute optimizations
                _logger.LogInfo("[GamerModeV3] ⚡ Executando otimizações...");
                var executionResult = await ExecuteOptimizationPlanAsync(_activeExecutionPlan, progress, cancellationToken);
                progress?.Report(70);
                
                if (!executionResult.Success)
                {
                    _logger.LogError($"[GamerModeV3] ❌ Falha na execução do plano: {executionResult.ErrorMessage}");
                    return false;
                }
                
                // 4. Start game monitoring
                _logger.LogInfo("[GamerModeV3] 👁️ Iniciando monitoramento do jogo...");
                await StartGameMonitoringAsync(_activeExecutionPlan, progress);
                progress?.Report(90);
                
                // 5. Update state and save
                lock (_lock)
                {
                    _isActive = true;
                    _sessionStartTime = DateTime.UtcNow;
                    _sessionState = new GamerSessionState
                    {
                        IsActive = true,
                        StartedAt = _sessionStartTime,
                        AppliedOptimizations = executionResult.AppliedOptimizations
                    };
                }
                
                await _stateManager.SaveActiveSessionAsync(_activeExecutionPlan, _sessionState);
                progress?.Report(100);
                
                // Fire status changed event
                StatusChanged?.Invoke(this, Status);
                
                // Report success to brain
                await ReportToBrainAsync("gamer_activation", 1.0, new 
                { 
                    gameName = gameProfile.Name,
                    hardwareTier = _activeExecutionPlan.Hardware.OverallTier.ToString(),
                    appliedOptimizations = executionResult.AppliedOptimizations.Count,
                    isManual = isManual
                });
                
                _logger.LogSuccess("═══════════════════════════════════════════════════════════════");
                _logger.LogSuccess($"[GamerModeV3] ✅ ATIVAÇÃO CONCLUÍDA");
                _logger.LogSuccess($"[GamerModeV3] Session ID: {_activeExecutionPlan.SessionId}");
                _logger.LogSuccess($"[GamerModeV3] Game: {gameProfile.Name}");
                _logger.LogSuccess($"[GamerModeV3] Applied: {executionResult.AppliedOptimizations.Count}/{_activeExecutionPlan.Decisions.Count} optimizations");
                _logger.LogSuccess("═══════════════════════════════════════════════════════════════");
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GamerModeV3] Erro na ativação: {ex.Message}", ex);
                
                // Report failure to brain
                await ReportToBrainAsync("gamer_activation", -0.8, new { error = ex.GetType().Name });
                
                return false;
            }
            finally
            {
                _operationLock.Release();
                _logger.LogInfo(nameof(ActivateAsync));
            }
        }
        
        /// <summary>
        /// Desativa Gamer Mode e restaura sistema
        /// </summary>
        public async Task<bool> DeactivateAsync(IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        {
            _logger.LogInfo(nameof(DeactivateAsync));
            
            await _operationLock.WaitAsync();
            
            try
            {
                ExecutionPlan? planToRevert;
                DateTime sessionStart;
                var appliedOptimizations = new Dictionary<OptimizationType, OptimizationResult>();
                
                lock (_lock)
                {
                    if (!_isActive || _activeExecutionPlan == null)
                    {
                        _logger.LogWarning("[GamerModeV3] Modo Gamer não está ativo");
                        return false;
                    }
                    
                    planToRevert = _activeExecutionPlan;
                    sessionStart = _sessionStartTime;
                    appliedOptimizations = new Dictionary<OptimizationType, OptimizationResult>(_sessionState.AppliedOptimizations);
                }
                
                var sessionDuration = DateTime.UtcNow - sessionStart;
                
                _logger.LogInfo("═══════════════════════════════════════════════════════════════");
                _logger.LogInfo("[GamerModeV3] ⏹️ DESATIVANDO MODO GAMER");
                _logger.LogInfo($"[GamerModeV3] Session ID: {planToRevert.SessionId}");
                _logger.LogInfo($"[GamerModeV3] Duration: {sessionDuration.TotalMinutes:F1} minutes");
                _logger.LogInfo($"[GamerModeV3] Optimizations to revert: {appliedOptimizations.Count}");
                _logger.LogInfo("═══════════════════════════════════════════════════════════════");
                
                // Stop game monitoring
                await _gameMonitor.StopMonitoringAsync();
                
                // Revert optimizations in reverse order
                int revertedCount = 0;
                int failedCount = 0;
                
                var optimizationsToRevert = appliedOptimizations
                    .OrderByDescending(kv => GetOptimizationPriority(kv.Key))
                    .ToArray();
                
                foreach (var (optimizationType, result) in optimizationsToRevert)
                {
                    var module = _modules.FirstOrDefault(m => m.Type == optimizationType);
                    if (module != null)
                    {
                        try
                        {
                            _logger.LogInfo($"[GamerModeV3] Revertendo: {optimizationType}");
                            var revertResult = await module.RevertAsync(planToRevert);
                            
                            if (revertResult.Success)
                            {
                                revertedCount++;
                                _logger.LogSuccess($"[GamerModeV3] ✓ {optimizationType} revertido");
                            }
                            else
                            {
                                failedCount++;
                                _logger.LogWarning($"[GamerModeV3] ✗ Falha ao reverter {optimizationType}: {revertResult.ErrorMessage}");
                            }
                        }
                        catch (Exception ex)
                        {
                            failedCount++;
                            _logger.LogError($"[GamerModeV3] Erro ao reverter {optimizationType}: {ex.Message}");
                        }
                    }
                }
                
                // Update state
                lock (_lock)
                {
                    _isActive = false;
                    _activeExecutionPlan = null;
                    _sessionState = new GamerSessionState { IsActive = false };
                }
                
                // Clean up state files
                await _stateManager.ClearActiveSessionAsync();
                await _stateManager.SaveSessionHistoryAsync(planToRevert, _sessionState, sessionDuration);
                
                // Fire status changed event
                StatusChanged?.Invoke(this, Status);
                
                // Report to brain with normalized reward
                var successRate = appliedOptimizations.Count == 0 ? 1.0 : (double)revertedCount / appliedOptimizations.Count;
                var sessionReward = Math.Clamp(sessionDuration.TotalMinutes / 60.0, 0.1, 1.0) * successRate;
                
                await ReportToBrainAsync("gamer_session", sessionReward, new 
                { 
                    sessionDurationMinutes = sessionDuration.TotalMinutes,
                    revertedOptimizations = revertedCount,
                    failedReverts = failedCount,
                    hardwareTier = planToRevert.Hardware.OverallTier.ToString()
                });
                
                _logger.LogSuccess("═══════════════════════════════════════════════════════════════");
                _logger.LogSuccess($"[GamerModeV3] ✅ DESATIVAÇÃO CONCLUÍDA");
                _logger.LogSuccess($"[GamerModeV3] Reverted: {revertedCount}/{appliedOptimizations.Count} optimizations");
                _logger.LogSuccess($"[GamerModeV3] Session Duration: {sessionDuration.TotalMinutes:F1} minutes");
                _logger.LogSuccess("═══════════════════════════════════════════════════════════════");
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GamerModeV3] Erro na desativação: {ex.Message}", ex);
                return false;
            }
            finally
            {
                _operationLock.Release();
                _logger.LogInfo(nameof(DeactivateAsync));
            }
        }
        
        /// <summary>
        /// Executa crash recovery manual
        /// </summary>
        public async Task<bool> RestoreIfCrashedAsync()
        {
            var result = await _crashRecovery.CheckAndRecoverAsync();
            if (result.RecoveryAttempted)
            {
                await ReportToBrainAsync("manual_recovery", result.RecoverySuccessful ? 0.5 : -0.3, new { manual = true });
            }
            return result.RecoverySuccessful;
        }
        
        /// <summary>
        /// Obtém opções atuais
        /// </summary>
        public VoltrisOptimizer.Services.Gamer.Models.GamerOptimizationOptions GetCurrentOptions()
        {
            lock (_lock)
            {
                return _currentOptions;
            }
        }
        
        /// <summary>
        /// Define opções do Gamer Mode
        /// </summary>
        public void SetOptions(VoltrisOptimizer.Services.Gamer.Models.GamerOptimizationOptions options)
        {
            lock (_lock)
            {
                _currentOptions = options;
            }
        }
        
        /// <summary>
        /// Inicia AutoPilot (detecção automática de jogos)
        /// </summary>
        public void StartAutoPilot()
        {
            lock (_lock)
            {
                _autoPilotActive = true;
                _logger.LogInfo("[GamerModeV3] AutoPilot ativado");
            }
        }
        
        /// <summary>
        /// Para AutoPilot
        /// </summary>
        public void StopAutoPilot()
        {
            lock (_lock)
            {
                _autoPilotActive = false;
                _logger.LogInfo("[GamerModeV3] AutoPilot desativado");
            }
        }
        
        /// <summary>
        /// Aplica otimizações persistentes
        /// </summary>
        public async Task ApplyPersistentOptimizationsAsync(VoltrisOptimizer.Services.Gamer.Models.GamerOptimizationOptions options, CancellationToken cancellationToken = default)
        {
            _logger.LogInfo("[GamerModeV3] Aplicando otimizações persistentes...");
            
            // For now, delegate to regular activation
            await ActivateAsync(options, null, null, cancellationToken, true);
        }
        
        /// <summary>
        /// Reverte otimizações persistentes
        /// </summary>
        public async Task RevertPersistentOptimizationsAsync()
        {
            _logger.LogInfo("[GamerModeV3] Revertendo otimizações persistentes...");
            
            // Delegate to regular deactivation
            await DeactivateAsync();
        }
        
        #region Private Methods
        
        private async Task<GameProfile> CreateGameProfileAsync(string? gameExecutable)
        {
            // This would integrate with existing game detection logic
            // For now, create a basic profile
            var profile = new GameProfile
            {
                Name = string.IsNullOrEmpty(gameExecutable) ? "Unknown Game" : Path.GetFileNameWithoutExtension(gameExecutable),
                ExecutablePath = gameExecutable ?? string.Empty,
                ProcessName = string.IsNullOrEmpty(gameExecutable) ? "unknown" : Path.GetFileNameWithoutExtension(gameExecutable),
                Type = GameType.Unknown,
                ConfidenceScore = string.IsNullOrEmpty(gameExecutable) ? 0.3 : 0.7
            };
            
            // Try to detect game process if executable provided
            if (!string.IsNullOrEmpty(gameExecutable))
            {
                try
                {
                    var processName = Path.GetFileNameWithoutExtension(gameExecutable);
                    var processes = System.Diagnostics.Process.GetProcessesByName(processName);
                    var gameProcess = processes.FirstOrDefault(p => !p.HasExited);
                    if (gameProcess != null)
                    {
                        profile.ProcessId = gameProcess.Id;
                        profile.ConfidenceScore = 0.9;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug($"[GamerModeV3] Erro ao detectar processo do jogo: {ex.Message}");
                }
            }
            
            return profile;
        }
        
        private async Task<PlanExecutionResult> ExecuteOptimizationPlanAsync(ExecutionPlan plan, IProgress<int>? progress, CancellationToken ct)
        {
            var result = new PlanExecutionResult();
            var appliedOptimizations = new Dictionary<OptimizationType, OptimizationResult>();
            
            // Filter decisions to apply
            var decisionsToApply = plan.Decisions
                .Where(d => d.Action == OptimizationAction.APPLY)
                .OrderByDescending(d => d.Priority)
                .ToArray();
            
            _logger.LogInfo($"[GamerModeV3] Executando {decisionsToApply.Length} otimizações...");
            
            int processedCount = 0;
            foreach (var decision in decisionsToApply)
            {
                if (ct.IsCancellationRequested) break;
                
                var module = _modules.FirstOrDefault(m => m.Type == decision.Type);
                if (module == null)
                {
                    _logger.LogWarning($"[GamerModeV3] Módulo não encontrado para: {decision.Type}");
                    continue;
                }
                
                if (!module.CanApply(decision, plan))
                {
                    _logger.LogInfo($"[GamerModeV3] Módulo {decision.Type} recusou aplicação");
                    continue;
                }
                
                try
                {
                    _logger.LogInfo($"[GamerModeV3] Aplicando: {decision.Type}");
                    var moduleResult = await module.ApplyAsync(decision, plan, ct);
                    
                    if (moduleResult.Success)
                    {
                        appliedOptimizations[decision.Type] = moduleResult;
                        result.SuccessCount++;
                        _logger.LogSuccess($"[GamerModeV3] ✓ {decision.Type}: {moduleResult.ChangesApplied} mudanças");
                    }
                    else
                    {
                        result.FailedCount++;
                        _logger.LogWarning($"[GamerModeV3] ✗ {decision.Type}: {moduleResult.ErrorMessage}");
                    }
                }
                catch (Exception ex)
                {
                    result.FailedCount++;
                    _logger.LogError($"[GamerModeV3] Erro ao aplicar {decision.Type}: {ex.Message}");
                }
                
                processedCount++;
                var progressValue = 40 + (int)((double)processedCount / decisionsToApply.Length * 30);
                progress?.Report(progressValue);
            }
            
            result.Success = result.SuccessCount > 0;
            result.AppliedOptimizations = appliedOptimizations;
            
            return result;
        }
        
        private async Task StartGameMonitoringAsync(ExecutionPlan plan, IProgress<int>? progress)
        {
            _logger.LogInfo("═══════════════════════════════════════════════════════════════");
            _logger.LogInfo("[GamerModeV3] Iniciando monitoramento do jogo...");
            _logger.LogInfo($"[GamerModeV3] Game: {plan.Game.Name}");
            _logger.LogInfo($"[GamerModeV3] ProcessId: {plan.Game.ProcessId}");
            _logger.LogInfo("═══════════════════════════════════════════════════════════════");
            
            if (plan.Game.ProcessId > 0)
            {
                try
                {
                    var gameProcess = System.Diagnostics.Process.GetProcessById(plan.Game.ProcessId);
                    _logger.LogInfo($"[GamerModeV3] Processo encontrado: HasExited={gameProcess.HasExited}");
                    
                    if (!gameProcess.HasExited)
                    {
                        _logger.LogInfo("[GamerModeV3] Chamando _gameMonitor.StartMonitoringAsync...");
                        await _gameMonitor.StartMonitoringAsync(gameProcess, plan.Game);
                        _logger.LogSuccess($"[GamerModeV3] ✅ Monitoramento iniciado para PID {plan.Game.ProcessId}");
                        return;
                    }
                    else
                    {
                        _logger.LogWarning("[GamerModeV3] ⚠️ Processo já encerrou!");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[GamerModeV3] ❌ Erro ao iniciar monitoramento: {ex.Message}");
                }
            }
            
            _logger.LogWarning("[GamerModeV3] ⚠️ Processo de jogo não encontrado - monitoramento passivo ativado");
        }
        
        private async void OnGameTerminated(object? sender, GameTerminatedEventArgs e)
        {
            _logger.LogInfo("═══════════════════════════════════════════════════════════════");
            _logger.LogInfo($"[GamerModeV3] 🔴 OnGameTerminated chamado!");
            _logger.LogInfo($"[GamerModeV3] Jogo encerrado: {e.GameProfile?.Name ?? "Unknown"}");
            _logger.LogInfo($"[GamerModeV3] Detection Method: {e.DetectionMethod}");
            _logger.LogInfo($"[GamerModeV3] Session Duration: {e.SessionDuration.TotalMinutes:F0} minutos");
            _logger.LogInfo($"[GamerModeV3] IsActive={_isActive}, AutoPilotActive={_autoPilotActive}");
            _logger.LogInfo("═══════════════════════════════════════════════════════════════");
            
            // Auto-deactivate
            try
            {
                _logger.LogInfo("[GamerModeV3] Iniciando desativação automática...");
                await DeactivateAsync();
                _logger.LogSuccess("[GamerModeV3] ✅ Desativação concluída com sucesso!");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GamerModeV3] ❌ Erro na desativação automática: {ex.Message}", ex);
            }
        }
        
        private async Task ReportToBrainAsync(string eventType, double reward, object metadata)
        {
            try
            {
                // Normalize reward to [-1.0, 1.0] range as specified
                var normalizedReward = Math.Clamp(reward, -1.0, 1.0);
                
                _brain.ReportExternalReward($"gamer_{eventType}", normalizedReward, metadata);
                
                _logger.LogDebug($"[GamerModeV3] Brain report: {eventType} = {normalizedReward:F2}");
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[GamerModeV3] Erro ao reportar para Brain: {ex.Message}");
            }
        }
        
        private int GetOptimizationPriority(OptimizationType type)
        {
            return type switch
            {
                OptimizationType.ProcessPriority => 10,
                OptimizationType.TimerResolution => 20,
                OptimizationType.PowerPlan => 30,
                OptimizationType.GpuHags => 40,
                OptimizationType.GpuLowLatency => 41,
                OptimizationType.GpuPowerMode => 42,
                OptimizationType.MemoryOptimization => 50,
                OptimizationType.NetworkOptimization => 60,
                OptimizationType.WindowsServiceOptimization => 70,
                OptimizationType.SchedulerSuspension => 80,
                OptimizationType.CpuAffinity => 90,
                _ => 100
            };
        }
        
        #endregion
        
        public void Dispose()
        {
            _gameMonitor?.Dispose();
            _operationLock?.Dispose();
        }
    }
    
    #region Supporting Classes
    
    internal class PlanExecutionResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public int SuccessCount { get; set; }
        public int FailedCount { get; set; }
        public Dictionary<OptimizationType, OptimizationResult> AppliedOptimizations { get; set; } = new();
    }
    
    #endregion
}