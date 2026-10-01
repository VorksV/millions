using System;
using System.Diagnostics;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.Gamer.Adaptive.Models;

namespace VoltrisOptimizer.Services.Gamer.Adaptive.Monitoring
{
    /// <summary>
    /// Monitoramento robusto de ciclo de vida do jogo com 3 camadas de detecção
    /// Layer 1: Process.Exited event (mais rápido)
    /// Layer 2: WMI Process Deletion event (backup)
    /// Layer 3: Polling (último recurso)
    /// </summary>
    public class RobustGameMonitor : IDisposable
    {
        private readonly ILoggingService _logger;
        private readonly object _lock = new();
        private readonly SemaphoreSlim _validationLock = new(1, 1);
        
        private Process? _gameProcess;
        private GameProfile? _gameProfile;
        private ManagementEventWatcher? _wmiWatcher;
        private CancellationTokenSource? _pollingCts;
        private Task? _pollingTask;
        
        private bool _isMonitoring = false;
        private bool _gameTerminatedFired = false;
        private DateTime _monitoringStartedAt;
        private DateTime _lastHeartbeat;
        
        // Configuration
        private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(10);
        private readonly TimeSpan _sessionTimeout = TimeSpan.FromHours(6); // Force terminate after 6h
        private readonly TimeSpan _focusLossTimeout = TimeSpan.FromMinutes(5); // If game loses focus for 5min
        
        // Events
        public event EventHandler<GameTerminatedEventArgs>? GameTerminated;
        public event EventHandler<GameFocusLostEventArgs>? GameFocusLost;
        
        public RobustGameMonitor(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        /// <summary>
        /// Inicia monitoramento robusto do jogo
        /// </summary>
        public async Task StartMonitoringAsync(Process gameProcess, GameProfile gameProfile)
        {
            _logger.LogInfo(nameof(StartMonitoringAsync));
            
            if (gameProcess == null) throw new ArgumentNullException(nameof(gameProcess));
            if (gameProfile == null) throw new ArgumentNullException(nameof(gameProfile));
            
            lock (_lock)
            {
                if (_isMonitoring)
                {
                    _logger.LogWarning("[RobustGameMonitor] Já está monitorando um jogo");
                    return;
                }
                
                _gameProcess = gameProcess;
                _gameProfile = gameProfile;
                _isMonitoring = true;
                _gameTerminatedFired = false; // Reset flag for new monitoring session
                _monitoringStartedAt = DateTime.UtcNow;
                _lastHeartbeat = DateTime.UtcNow;
            }
            
            _logger.LogInfo("═══════════════════════════════════════════════════════════════");
            _logger.LogInfo($"[RobustGameMonitor] 👁️ INICIANDO MONITORAMENTO ROBUSTO");
            _logger.LogInfo($"[RobustGameMonitor] Game: {gameProfile.Name}");
            _logger.LogInfo($"[RobustGameMonitor] PID: {gameProcess.Id}");
            _logger.LogInfo($"[RobustGameMonitor] Executable: {gameProfile.ExecutablePath}");
            _logger.LogInfo("═══════════════════════════════════════════════════════════════");
            
            // Layer 1: Process.Exited event
            AttachProcessExitedEvent(gameProcess);
            
            // Layer 2: WMI Watcher
            StartWmiWatcher(gameProcess.Id);
            
            // Layer 3: Polling fallback
            StartPollingLoop();
            
            _logger.LogSuccess("[RobustGameMonitor] ✅ Monitoramento iniciado com 3 camadas");
            _logger.LogInfo($"[RobustGameMonitor]   Layer 1: Process.Exited event attached");
            _logger.LogInfo($"[RobustGameMonitor]   Layer 2: WMI Watcher started (PID: {gameProcess.Id})");
            _logger.LogInfo($"[RobustGameMonitor]   Layer 3: Polling fallback active (interval: {_pollingInterval.TotalSeconds}s)");
            
            _logger.LogInfo(nameof(StartMonitoringAsync));
            await Task.CompletedTask;
        }
        
        /// <summary>
        /// Para monitoramento
        /// </summary>
        public async Task StopMonitoringAsync()
        {
            _logger.LogInfo(nameof(StopMonitoringAsync));
            
            lock (_lock)
            {
                if (!_isMonitoring)
                {
                    _logger.LogWarning("[RobustGameMonitor] Não está monitorando nenhum jogo");
                    return;
                }
                
                _isMonitoring = false;
                _gameTerminatedFired = false; // Reset flag when stopping monitoring
            }
            
            _logger.LogInfo("[RobustGameMonitor] Parando monitoramento...");
            
            // Cleanup Layer 1
            if (_gameProcess != null)
            {
                try
                {
                    _gameProcess.Exited -= OnProcessExited;
                    _gameProcess.EnableRaisingEvents = false;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug($"[RobustGameMonitor] Erro ao remover event handler: {ex.Message}");
                }
            }
            
            // Cleanup Layer 2
            if (_wmiWatcher != null)
            {
                try
                {
                    _wmiWatcher.Stop();
                    _wmiWatcher.Dispose();
                    _wmiWatcher = null;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug($"[RobustGameMonitor] Erro ao parar WMI watcher: {ex.Message}");
                }
            }
            
            // Cleanup Layer 3
            if (_pollingCts != null)
            {
                _pollingCts.Cancel();
                if (_pollingTask != null)
                {
                    try { await _pollingTask; }
                    catch (OperationCanceledException) { }
                }
                _pollingCts.Dispose();
                _pollingCts = null;
                _pollingTask = null;
            }
            
            _gameProcess = null;
            _gameProfile = null;
            
            _logger.LogSuccess("[RobustGameMonitor] ✅ Monitoramento parado");
            _logger.LogInfo(nameof(StopMonitoringAsync));
        }
        
        /// <summary>
        /// Verifica se jogo ainda está rodando
        /// </summary>
        public bool IsGameStillRunning()
        {
            lock (_lock)
            {
                if (_gameProcess == null) return false;
                
                try
                {
                    // Multiple checks for robustness
                    if (_gameProcess.HasExited) return false;
                    
                    // Heartbeat check: try to access handle
                    _ = _gameProcess.Handle;
                    
                    _lastHeartbeat = DateTime.UtcNow;
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }
        
        #region Layer 1: Process.Exited Event
        
        private void AttachProcessExitedEvent(Process process)
        {
            try
            {
                process.EnableRaisingEvents = true;
                process.Exited += OnProcessExited;
                _logger.LogInfo("[RobustGameMonitor] [Layer 1] Process.Exited event attached");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[RobustGameMonitor] [Layer 1] Falha ao anexar event: {ex.Message}");
            }
        }
        
        private void OnProcessExited(object? sender, EventArgs e)
        {
            _logger.LogInfo("═══════════════════════════════════════════════════════════════");
            _logger.LogInfo("[RobustGameMonitor] [Layer 1] 🔴 Process.Exited event disparado");
            _logger.LogInfo($"[RobustGameMonitor] [Layer 1] Sender: {sender?.GetType().Name ?? "null"}");
            _logger.LogInfo("═══════════════════════════════════════════════════════════════");
            OnGameTerminatedDetected("Process.Exited event (Layer 1)");
        }
        
        #endregion
        
        #region Layer 2: WMI Watcher
        
        private void StartWmiWatcher(int processId)
        {
            try
            {
                var query = $"SELECT * FROM Win32_ProcessStopTrace WHERE ProcessID = {processId}";
                _wmiWatcher = new ManagementEventWatcher(query);
                _wmiWatcher.EventArrived += OnWmiProcessStopped;
                _wmiWatcher.Start();
                _logger.LogInfo($"[RobustGameMonitor] [Layer 2] WMI Watcher started for PID {processId}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[RobustGameMonitor] [Layer 2] Falha ao iniciar WMI watcher: {ex.Message}");
                _logger.LogWarning($"[RobustGameMonitor] [Layer 2] Continuando sem WMI (layers 1 e 3 ainda ativos)");
            }
        }
        
        private void OnWmiProcessStopped(object sender, EventArrivedEventArgs e)
        {
            _logger.LogInfo("═══════════════════════════════════════════════════════════════");
            _logger.LogInfo("[RobustGameMonitor] [Layer 2] 🔴 WMI Process Deletion event disparado");
            _logger.LogInfo($"[RobustGameMonitor] [Layer 2] Event type: {e.NewEvent?.ClassPath.ClassName}");
            _logger.LogInfo("═══════════════════════════════════════════════════════════════");
            OnGameTerminatedDetected("WMI Process Deletion (Layer 2)");
        }
        
        #endregion
        
        #region Layer 3: Polling Fallback
        
        private void StartPollingLoop()
        {
            _pollingCts = new CancellationTokenSource();
            var ct = _pollingCts.Token;
            
            _pollingTask = Task.Run(async () =>
            {
                _logger.LogInfo($"[RobustGameMonitor] [Layer 3] ⏱️ Polling loop iniciado (interval: {_pollingInterval.TotalSeconds}s)");
                
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(_pollingInterval, ct);
                        
                        // Check if process is still alive
                        var isStillRunning = IsGameStillRunning();
                        _logger.LogDebug($"[RobustGameMonitor] [Layer 3] 📊 Polling check: isStillRunning={isStillRunning}");
                        
                        if (!isStillRunning)
                        {
                            _logger.LogWarning("[RobustGameMonitor] [Layer 3] 🔴 Polling detectou processo encerrado");
                            OnGameTerminatedDetected("Polling check (Layer 3)");
                            break;
                        }
                        
                        // Check session timeout
                        var sessionDuration = DateTime.UtcNow - _monitoringStartedAt;
                        if (sessionDuration > _sessionTimeout)
                        {
                            _logger.LogWarning($"[RobustGameMonitor] [Layer 3] ⏱️ Session timeout ({sessionDuration.TotalHours:F1}h)");
                            OnGameTerminatedDetected($"Session timeout ({_sessionTimeout.TotalHours}h)");
                            break;
                        }
                        
                        // Log heartbeat
                        _logger.LogDebug($"[RobustGameMonitor] [Layer 3] 💓 Heartbeat: processo ainda ativo (uptime: {sessionDuration.TotalMinutes:F0}min)");
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogDebug("[RobustGameMonitor] [Layer 3] Polling loop cancelado");
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"[RobustGameMonitor] [Layer 3] ❌ Erro no polling: {ex.Message}");
                    }
                }
                
                _logger.LogInfo("[RobustGameMonitor] [Layer 3] Polling loop encerrado");
            }, ct);
        }
        
        #endregion
        
        #region Termination Handling
        
        private void OnGameTerminatedDetected(string detectionMethod)
        {
            _logger.LogInfo("═══════════════════════════════════════════════════════════════");
            _logger.LogInfo($"[RobustGameMonitor] OnGameTerminatedDetected chamado");
            _logger.LogInfo($"[RobustGameMonitor] Detection Method: {detectionMethod}");
            _logger.LogInfo("═══════════════════════════════════════════════════════════════");
            
            // Ensure we only fire once
            bool shouldFire = false;
            lock (_lock)
            {
                _logger.LogInfo($"[RobustGameMonitor] Lock adquirido");
                _logger.LogInfo($"[RobustGameMonitor] _gameTerminatedFired={_gameTerminatedFired}, _isMonitoring={_isMonitoring}");
                
                if (!_gameTerminatedFired && _isMonitoring)
                {
                    _gameTerminatedFired = true;
                    shouldFire = true;
                    _logger.LogSuccess("[RobustGameMonitor] ✅ shouldFire=true, event será disparado");
                }
                else
                {
                    _logger.LogWarning($"[RobustGameMonitor] ⚠️ shouldFire=false");
                    _logger.LogWarning($"[RobustGameMonitor]    _gameTerminatedFired={_gameTerminatedFired} (deve ser false)");
                    _logger.LogWarning($"[RobustGameMonitor]    _isMonitoring={_isMonitoring} (deve ser true)");
                }
            }
            
            if (!shouldFire)
            {
                _logger.LogWarning($"[RobustGameMonitor] ⚠️ Evento já foi disparado, ignorando detecção duplicada via {detectionMethod}");
                return;
            }
            
            var duration = DateTime.UtcNow - _monitoringStartedAt;
            
            _logger.LogInfo("═══════════════════════════════════════════════════════════════");
            _logger.LogSuccess($"[RobustGameMonitor] ⏹️ JOGO ENCERRADO DETECTADO");
            _logger.LogInfo($"[RobustGameMonitor] Detection Method: {detectionMethod}");
            _logger.LogInfo($"[RobustGameMonitor] Game: {_gameProfile?.Name ?? "Unknown"}");
            _logger.LogInfo($"[RobustGameMonitor] Duration: {duration.TotalHours:F0}h {duration.Minutes}min {duration.Seconds}s");
            _logger.LogInfo($"[RobustGameMonitor] GameTerminated event subscribers: {GameTerminated?.GetInvocationList().Length ?? 0}");
            _logger.LogInfo("═══════════════════════════════════════════════════════════════");
            
            // Fire event
            try
            {
                var args = new GameTerminatedEventArgs
                {
                    GameProfile = _gameProfile,
                    DetectionMethod = detectionMethod,
                    SessionDuration = duration,
                    DetectedAt = DateTime.UtcNow
                };
                
                _logger.LogInfo("[RobustGameMonitor] Disparando evento GameTerminated...");
                GameTerminated?.Invoke(this, args);
                _logger.LogSuccess("[RobustGameMonitor] ✅ Evento GameTerminated disparado com sucesso!");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[RobustGameMonitor] ❌ Erro ao disparar evento GameTerminated: {ex.Message}", ex);
            }
        }
        
        #endregion
        
        public void Dispose()
        {
            _ = StopMonitoringAsync();
        }
    }
    
    #region Event Args
    
    public class GameTerminatedEventArgs : EventArgs
    {
        public GameProfile? GameProfile { get; set; }
        public string DetectionMethod { get; set; } = string.Empty;
        public TimeSpan SessionDuration { get; set; }
        public DateTime DetectedAt { get; set; }
    }
    
    public class GameFocusLostEventArgs : EventArgs
    {
        public GameProfile? GameProfile { get; set; }
        public TimeSpan FocusLostDuration { get; set; }
    }
    
    #endregion
}
