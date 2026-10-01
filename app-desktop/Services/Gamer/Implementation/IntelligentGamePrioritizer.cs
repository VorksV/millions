using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Gamer.Interfaces;
using VoltrisOptimizer.Services.Gamer.Models;

namespace VoltrisOptimizer.Services.Gamer.Implementation
{
    public class IntelligentGamePrioritizer : IDisposable
    {
        private readonly ILoggingService _logger;
        private readonly IProcessPrioritizer _processPrioritizer;
        private readonly object _lock = new();
        private readonly SemaphoreSlim _priorityChangeLock = new(1, 1);

        private List<Process> _gameProcesses = new();
        private Dictionary<int, ProcessPriorityLevel> _currentPriorities = new();
        private Dictionary<int, ProcessPriorityLevel> _originalPriorities = new();

        private int _foregroundGamePid;
        private DateTime _lastPriorityChangeTime = DateTime.MinValue;
        private const int PRIORITY_CHANGE_DEBOUNCE_MS = 500;

        private IntPtr _winEventHook;
        private Thread _hookThread;
        private bool _hookActive;
        private readonly AutoResetEvent _hookReadyEvent = new(false);
        private WinEventDelegate _winEventDelegate;

        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

        private const int DEBOUNCE_MS = 100;
        private DateTime _lastForegroundChange = DateTime.MinValue;
        private DateTime _gameLostFocusTime = DateTime.MinValue;
        private CancellationTokenSource? _deferredPriorityCts;

        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int ptX;
            public int ptY;
        }

        private const int PM_REMOVE = 1;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        public IntelligentGamePrioritizer(
            ILoggingService logger,
            IProcessPrioritizer processPrioritizer)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _processPrioritizer = processPrioritizer ?? throw new ArgumentNullException(nameof(processPrioritizer));

            _logger.LogSuccess("[IntelligentGamePrioritizer] INICIALIZADO - Escalonador Foreground-Aware");
            _logger.LogInfo("[IntelligentGamePrioritizer] Estrategia: Foreground=HIGH, Background=Idle");
            _logger.LogInfo("[IntelligentGamePrioritizer] Deteccao: SetWinEventHook (eventos reais do Windows)");
        }

        public void StartMonitoring(List<Process> gameProcesses)
        {
            lock (_lock)
            {
                StopMonitoring();

                _gameProcesses = gameProcesses.Where(p => !p.HasExited).ToList();
                _foregroundGamePid = 0;
                _currentPriorities.Clear();

                _logger.LogInfo($"[IntelligentGamePrioritizer] INICIANDO MONITORAMENTO - {_gameProcesses.Count} jogo(s)");
                foreach (var proc in _gameProcesses)
                {
                    _logger.LogInfo($"[IntelligentGamePrioritizer]   + {proc.ProcessName} (PID: {proc.Id})");
                    if (!_originalPriorities.ContainsKey(proc.Id))
                    {
                        try { _originalPriorities[proc.Id] = MapToProcessPriorityLevel(proc.PriorityClass); }
                        catch { _originalPriorities[proc.Id] = ProcessPriorityLevel.Normal; }
                    }
                }

                _winEventDelegate = OnWinEvent;
                _hookActive = true;

                _hookThread = new Thread(WinEventHookThread)
                {
                    Name = "VFE-ForegroundHook",
                    IsBackground = true
                };
                _hookThread.SetApartmentState(ApartmentState.STA);
                _hookThread.Start();

                if (_hookReadyEvent.WaitOne(3000))
                {
                    _logger.LogSuccess("[IntelligentGamePrioritizer] WinEventHook ativo - monitorando foreground em tempo real");
                }
                else
                {
                    _logger.LogWarning("[IntelligentGamePrioritizer] WinEventHook nao respondeu - usando fallback de timer");
                    Task.Run(() => FallbackPollingLoopAsync());
                }

                _ = SafetyNetLoopAsync();
            }
        }

        private void WinEventHookThread()
        {
            try
            {
                _winEventHook = SetWinEventHook(
                    EVENT_SYSTEM_FOREGROUND,
                    EVENT_SYSTEM_FOREGROUND,
                    IntPtr.Zero,
                    _winEventDelegate,
                    0, 0,
                    WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

                if (_winEventHook == IntPtr.Zero)
                {
                    _logger.LogError("[IntelligentGamePrioritizer] SetWinEventHook falhou - erro ao criar hook");
                    _hookReadyEvent.Set();
                    return;
                }

                _hookReadyEvent.Set();

                MSG msg;
                while (_hookActive && PeekMessage(out msg, IntPtr.Zero, 0, 0, PM_REMOVE))
                {
                }

                while (_hookActive)
                {
                    if (GetMessage(out msg, IntPtr.Zero, 0, 0))
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[IntelligentGamePrioritizer] Hook thread error: {ex.Message}");
                _hookReadyEvent.Set();
            }
            finally
            {
                if (_winEventHook != IntPtr.Zero)
                {
                    UnhookWinEvent(_winEventHook);
                    _winEventHook = IntPtr.Zero;
                }
            }
        }

        private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            try
            {
                if (eventType != EVENT_SYSTEM_FOREGROUND) return;
                if (hwnd == IntPtr.Zero) return;

                GetWindowThreadProcessId(hwnd, out uint pid);
                int foregroundPid = (int)pid;

                var now = DateTime.Now;
                if ((now - _lastForegroundChange).TotalMilliseconds < DEBOUNCE_MS) return;
                _lastForegroundChange = now;

                string windowTitle = GetWindowTitle(hwnd);

                lock (_lock)
                {
                    if (_gameProcesses.Count == 0) return;

                    var foregroundGame = _gameProcesses.FirstOrDefault(p => p.Id == foregroundPid);

                    if (foregroundGame != null)
                    {
                        if (_foregroundGamePid != foregroundPid)
                        {
                            _foregroundGamePid = foregroundPid;
                            LogForegroundChange(foregroundGame.ProcessName, foregroundPid, windowTitle);
                        }

                        // DEFERRED: A prioridade NÃO é aplicada imediatamente no WinEventHook para evitar
                        // scheduler cascade durante a transição de Alt+Tab. Aplica após 500ms.
                        ScheduleDeferredPriority(foregroundGame, ProcessPriorityLevel.High, "Foreground Window");
                    }
                    else
                    {
                        if (_foregroundGamePid != 0)
                        {
                            _foregroundGamePid = 0;
                            _gameLostFocusTime = DateTime.Now; // Marca o momento exato que perdeu o foco
                            LogAllIdle(foregroundPid, windowTitle);
                            // NUNCA SetAllGamesToNormal() aqui para não causar spikes de Alt+Tab! 
                            // O SafetyNetLoop cuidará se o tempo de histerese passar.
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[IntelligentGamePrioritizer] Erro no callback: {ex.Message}");
            }
        }

        private async Task SafetyNetLoopAsync()
        {
            // INTERVALO AUMENTADO: 5000ms para minimizar CPU wakeups durante gameplay
            // A prioridade do foreground já é gerenciada pelo ScheduleDeferredPriority
            const int SafetyNetIntervalMs = 5000;
            
            while (_hookActive)
            {
                await Task.Delay(SafetyNetIntervalMs);

                try
                {
                    lock (_lock)
                    {
                        if (_gameProcesses.Count == 0) continue;
                        
                        var now = DateTime.Now;

                        // Apenas gerencia hysteresis de background games após 3s sem foco
                        var timeSinceLastChange = (now - _lastForegroundChange).TotalMilliseconds;
                        if (timeSinceLastChange < 3000) continue;

                        foreach (var game in _gameProcesses)
                        {
                            if (game.HasExited) continue;
                            if (_foregroundGamePid != 0 && game.Id == _foregroundGamePid) continue;

                            double timeSinceLostFocus = _foregroundGamePid == 0 
                                ? (now - _gameLostFocusTime).TotalMilliseconds 
                                : timeSinceLastChange;

                            if (timeSinceLostFocus >= 3000)
                            {
                                try
                                {
                                    using var check = Process.GetProcessById(game.Id);
                                    if (!check.HasExited && check.PriorityClass != System.Diagnostics.ProcessPriorityClass.Normal)
                                    {
                                        _logger.LogDebug($"[IntelligentGamePrioritizer] Histerese Completa (>3s): {game.ProcessName} reaplicando Normal");
                                        _processPrioritizer.SetPriority(game.Id, ProcessPriorityLevel.Normal);
                                        _currentPriorities[game.Id] = ProcessPriorityLevel.Normal;
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                }
                catch { }
            }
        }

        private void ScheduleDeferredPriority(Process game, ProcessPriorityLevel level, string reason)
        {
            _deferredPriorityCts?.Cancel();
            _deferredPriorityCts = new CancellationTokenSource();
            var token = _deferredPriorityCts.Token;
            var pid = game.Id;
            var name = game.ProcessName;

            _ = Task.Run(async () =>
            {
                try
                {
                    // Aguarda 500ms para a transição de Alt+Tab completar
                    await Task.Delay(500, token).ConfigureAwait(false);
                    lock (_lock)
                    {
                        if (token.IsCancellationRequested) return;
                        var stillActive = _gameProcesses.FirstOrDefault(p => p.Id == pid && !p.HasExited);
                        if (stillActive != null && _foregroundGamePid == pid)
                        {
                            SetPrioritySafe(stillActive, level, reason);
                        }
                    }
                }
                catch (OperationCanceledException) { }
            });
        }

        private async Task FallbackPollingLoopAsync()
        {
            while (_hookActive)
            {
                await Task.Delay(1000);

                try
                {
                    lock (_lock)
                    {
                        if (_gameProcesses.Count == 0) continue;

                        IntPtr hwnd = GetForegroundWindowFallback();
                        if (hwnd == IntPtr.Zero) continue;

                        GetWindowThreadProcessId(hwnd, out uint pid);
                        int foregroundPid = (int)pid;

                        var foregroundGame = _gameProcesses.FirstOrDefault(p => p.Id == foregroundPid);

                        if (foregroundGame != null)
                        {
                            if (foregroundPid != _foregroundGamePid)
                            {
                                _foregroundGamePid = foregroundPid;
                                string title = GetWindowTitle(hwnd);
                                LogForegroundChange(foregroundGame.ProcessName, foregroundPid, title);

                                foreach (var game in _gameProcesses)
                                {
                                    if (game.HasExited) continue;
                                    if (game.Id == foregroundPid)
                                        SetPrioritySafe(game, ProcessPriorityLevel.High, "Foreground Window");
                                    else
                                        SetPrioritySafe(game, ProcessPriorityLevel.Normal, "Background (outro jogo em foco)");
                                }
                            }
                        }
                        else
                        {
                            if (_foregroundGamePid != 0)
                            {
                                _foregroundGamePid = 0;
                                string title = GetWindowTitle(hwnd);
                                LogAllIdle(foregroundPid, title);
                                SetAllGamesToNormal("Nenhum jogo em primeiro plano");
                            }
                        }
                    }
                }
                catch { }
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        private static IntPtr GetForegroundWindowFallback()
        {
            return GetForegroundWindow();
        }

        private static string GetWindowTitle(IntPtr hwnd)
        {
            int len = GetWindowTextLength(hwnd);
            if (len <= 0) return "(sem titulo)";
            var sb = new System.Text.StringBuilder(len + 1);
            GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        private void SetPrioritySafe(Process game, ProcessPriorityLevel newLevel, string reason)
        {
            try
            {
                if (game.HasExited) return;

                var currentPid = game.Id;

                // CRITICAL FIX: Debounce para evitar oscilacoes de prioridade
                var now = DateTime.Now;
                if ((now - _lastPriorityChangeTime).TotalMilliseconds < PRIORITY_CHANGE_DEBOUNCE_MS)
                {
                    _logger.LogDebug($"[IntelligentGamePrioritizer] Debounce: Ignorando mudança de prioridade para {game.ProcessName} (PID {currentPid}) - muito recente");
                    return;
                }

                ProcessPriorityLevel? oldLevel = null;
                if (_currentPriorities.TryGetValue(currentPid, out var existing))
                    oldLevel = existing;

                if (oldLevel == newLevel) return;

                // CRITICAL FIX: Usar lock para evitar race condition com SafetyNetLoop
                if (!_priorityChangeLock.Wait(0))
                {
                    _logger.LogDebug($"[IntelligentGamePrioritizer] Lock ocupado: Ignorando mudança de prioridade para {game.ProcessName}");
                    return;
                }

                try
                {
                    _lastPriorityChangeTime = now;
                    bool success = _processPrioritizer.SetPriority(currentPid, newLevel);

                    if (success)
                    {
                        _currentPriorities[currentPid] = newLevel;
                        LogTransition(game.ProcessName, currentPid, oldLevel, newLevel, reason, true);
                        _logger.LogDebug($"[IntelligentGamePrioritizer] Prioridade alterada: {game.ProcessName} {oldLevel} -> {newLevel}");
                    }
                    else
                    {
                        _logger.LogWarning($"[IntelligentGamePrioritizer] FALHA ao alterar prioridade de {game.ProcessName} (PID {currentPid}) para {newLevel}");
                    }
                }
                finally
                {
                    _priorityChangeLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[IntelligentGamePrioritizer] Erro SetPrioritySafe {game.ProcessName}: {ex.Message}");
            }
        }

        private void SetAllGamesToNormal(string reason)
        {
            foreach (var game in _gameProcesses)
            {
                if (game.HasExited) continue;
                SetPrioritySafe(game, ProcessPriorityLevel.Normal, reason);
            }
        }

        public void UpdateGameProcesses(List<Process> newGames)
        {
            lock (_lock)
            {
                int oldCount = _gameProcesses.Count;
                var validNewGames = newGames.Where(p => !p.HasExited).ToList();

                foreach (var proc in _gameProcesses)
                {
                    if (!validNewGames.Any(p => p.Id == proc.Id) && !_originalPriorities.ContainsKey(proc.Id))
                    {
                        try
                        {
                            var p = Process.GetProcessById(proc.Id);
                            if (!p.HasExited)
                            {
                                _originalPriorities[proc.Id] = MapToProcessPriorityLevel(p.PriorityClass);
                                p.Dispose();
                            }
                        }
                        catch { }
                    }
                }

                _gameProcesses = validNewGames;

                foreach (var proc in _gameProcesses)
                {
                    if (!_originalPriorities.ContainsKey(proc.Id))
                    {
                        try { _originalPriorities[proc.Id] = MapToProcessPriorityLevel(proc.PriorityClass); }
                        catch { _originalPriorities[proc.Id] = ProcessPriorityLevel.Normal; }
                    }
                }

                var deadPids = _currentPriorities.Keys
                    .Where(pid => !_gameProcesses.Any(p => p.Id == pid))
                    .ToList();
                foreach (var pid in deadPids) _currentPriorities.Remove(pid);

                _logger.LogInfo($"[IntelligentGamePrioritizer] LISTA ATUALIZADA: {oldCount} -> {_gameProcesses.Count} jogo(s)");
            }
        }

        public void StopMonitoring()
        {
            _deferredPriorityCts?.Cancel();
            _deferredPriorityCts?.Dispose();
            _deferredPriorityCts = null;
            lock (_lock)
            {
                _hookActive = false;

                if (_hookThread != null && _hookThread.IsAlive)
                {
                    try
                    {
                        _hookThread.Join(1000);
                    }
                    catch { }
                    _hookThread = null;
                }

                if (_winEventHook != IntPtr.Zero)
                {
                    UnhookWinEvent(_winEventHook);
                    _winEventHook = IntPtr.Zero;
                }

                RestoreAllPriorities();

                _gameProcesses.Clear();
                _currentPriorities.Clear();
                _foregroundGamePid = 0;

                _logger.LogInfo("[IntelligentGamePrioritizer] MONITORAMENTO PARADO");
            }
        }

        private void RestoreAllPriorities()
        {
            _logger.LogInfo("[IntelligentGamePrioritizer] Restaurando prioridades originais...");

            foreach (var kvp in _originalPriorities.ToList())
            {
                try
                {
                    using var proc = Process.GetProcessById(kvp.Key);
                    if (!proc.HasExited)
                    {
                        var targetClass = kvp.Value switch
                        {
                            ProcessPriorityLevel.Idle => System.Diagnostics.ProcessPriorityClass.Idle,
                            ProcessPriorityLevel.BelowNormal => System.Diagnostics.ProcessPriorityClass.BelowNormal,
                            ProcessPriorityLevel.Normal => System.Diagnostics.ProcessPriorityClass.Normal,
                            ProcessPriorityLevel.AboveNormal => System.Diagnostics.ProcessPriorityClass.AboveNormal,
                            ProcessPriorityLevel.High => System.Diagnostics.ProcessPriorityClass.High,
                            ProcessPriorityLevel.RealTime => System.Diagnostics.ProcessPriorityClass.High,
                            _ => System.Diagnostics.ProcessPriorityClass.Normal
                        };

                        try { proc.PriorityClass = targetClass; }
                        catch { }
                        _logger.LogInfo($"[IntelligentGamePrioritizer] Restaurado {proc.ProcessName} -> {kvp.Value}");
                    }
                }
                catch { }
            }

            _originalPriorities.Clear();
            _logger.LogSuccess("[IntelligentGamePrioritizer] Prioridades originais restauradas");
        }

        private void LogForegroundChange(string processName, int pid, string windowTitle)
        {
            _logger.LogInfo("═══════════════════════════════════════════");
            _logger.LogInfo($"  {DateTime.Now:HH:mm:ss}");
            _logger.LogInfo("");
            _logger.LogInfo($"  Foreground alterado:");
            _logger.LogInfo($"  {processName}");
            _logger.LogInfo($"  PID: {pid}");
            if (!string.IsNullOrEmpty(windowTitle) && windowTitle != "(sem titulo)")
                _logger.LogInfo($"  Janela: {windowTitle}");
            _logger.LogInfo("");
            _logger.LogInfo($"  Motivo:");
            _logger.LogInfo($"  Foreground Window");
            _logger.LogInfo("═══════════════════════════════════════════");
        }

        private void LogAllIdle(int currentForegroundPid, string windowTitle)
        {
            var foregroundProc = SafeGetProcess(currentForegroundPid);
            string foregroundName = foregroundProc?.ProcessName ?? $"PID {currentForegroundPid}";
            foregroundProc?.Dispose();

            _logger.LogInfo("═══════════════════════════════════════════");
            _logger.LogInfo($"  {DateTime.Now:HH:mm:ss}");
            _logger.LogInfo("");
            _logger.LogInfo($"  Foreground alterado:");
            _logger.LogInfo($"  {foregroundName}");
            if (!string.IsNullOrEmpty(windowTitle) && windowTitle != "(sem titulo)")
                _logger.LogInfo($"  Janela: {windowTitle}");
            _logger.LogInfo("");
            _logger.LogInfo($"  Jogos detectados:");
            _logger.LogInfo($"  {_gameProcesses.Count(p => !p.HasExited)}");
            _logger.LogInfo("");

            foreach (var game in _gameProcesses)
            {
                if (game.HasExited) continue;

                var oldPrio = _currentPriorities.TryGetValue(game.Id, out var old) ? old.ToString() : "?";
                _logger.LogInfo($"  {game.ProcessName} (PID {game.Id})");
                _logger.LogInfo($"  {oldPrio} -> Idle");
            }

            _logger.LogInfo("");
            _logger.LogInfo($"  Motivo:");
            _logger.LogInfo($"  Nenhum jogo esta em primeiro plano");
            _logger.LogInfo("═══════════════════════════════════════════");
        }

        private void LogTransition(string process, int pid, ProcessPriorityLevel? oldPrio, ProcessPriorityLevel newPrio, string reason, bool success)
        {
            if (success)
            {
                _logger.LogInfo($"[{DateTime.Now:HH:mm:ss}] {process} (PID {pid}): {(oldPrio?.ToString() ?? "?")} -> {newPrio} | {reason}");
            }
        }

        private static Process SafeGetProcess(int pid)
        {
            try { return Process.GetProcessById(pid); }
            catch { return null; }
        }

        private static ProcessPriorityLevel MapToProcessPriorityLevel(System.Diagnostics.ProcessPriorityClass priorityClass)
        {
            return priorityClass switch
            {
                System.Diagnostics.ProcessPriorityClass.Idle => ProcessPriorityLevel.Idle,
                System.Diagnostics.ProcessPriorityClass.BelowNormal => ProcessPriorityLevel.BelowNormal,
                System.Diagnostics.ProcessPriorityClass.Normal => ProcessPriorityLevel.Normal,
                System.Diagnostics.ProcessPriorityClass.AboveNormal => ProcessPriorityLevel.AboveNormal,
                System.Diagnostics.ProcessPriorityClass.High => ProcessPriorityLevel.High,
                System.Diagnostics.ProcessPriorityClass.RealTime => ProcessPriorityLevel.RealTime,
                _ => ProcessPriorityLevel.Normal
            };
        }

        public void Dispose()
        {
            StopMonitoring();
            _hookReadyEvent.Dispose();
        }
    }
}
