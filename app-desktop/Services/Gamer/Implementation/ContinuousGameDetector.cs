using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.Gamer.Models;
using VoltrisOptimizer.Utils;

namespace VoltrisOptimizer.Services.Gamer.Implementation
{
    public class ContinuousGameDetector : IDisposable
    {
        private readonly ILoggingService _logger;

        private bool _isRunning = false;
        private DetectedGame? _currentGame = null;
        private int? _currentProcessId = null;
        private CancellationTokenSource? _detectionCts;
        private Task? _detectionTask;

        public event EventHandler<DetectedGame>? GameStarted;
        public event EventHandler<DetectedGame>? GameStopped;
        public event EventHandler<DetectedGame>? GameFocusChanged;

        private readonly Dictionary<int, ProcessInfo> _trackedProcesses = new();
        private readonly HashSet<string> _knownGameExecutables = new();

        private readonly TimeSpan _processCheckInterval = TimeSpan.FromSeconds(30);

        private readonly GameConfidenceEvaluator _evaluator;

        public ContinuousGameDetector(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogEntry(nameof(ContinuousGameDetector));
            _evaluator = new GameConfidenceEvaluator(logger);
            LoadKnownGameExecutables();
            _logger.LogExit(nameof(ContinuousGameDetector));
        }

        public async Task<bool> StartDetectionAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogEntry(nameof(StartDetectionAsync));
            if (_isRunning)
            {
                _logger.LogWarning("[GameDetector] Detecção já está ativa");
                _logger.LogExit(nameof(StartDetectionAsync));
                return false;
            }

            try
            {
                _isRunning = true;
                _detectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                _detectionTask = Task.Run(() => DetectionLoop(_detectionCts.Token), _detectionCts.Token);

                _logger.LogSuccess("[GameDetector] Detecção contínua iniciada");
                _logger.LogExit(nameof(StartDetectionAsync));
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("[GameDetector] Erro ao iniciar detecção", ex);
                await StopDetectionAsync();
                _logger.LogExit(nameof(StartDetectionAsync));
                return false;
            }
        }

        public async Task<bool> StopDetectionAsync()
        {
            _logger.LogEntry(nameof(StopDetectionAsync));
            if (!_isRunning)
            {
                _logger.LogExit(nameof(StopDetectionAsync));
                return true;
            }

            try
            {
                _isRunning = false;
                _detectionCts?.Cancel();

                if (_detectionTask != null)
                {
                    await _detectionTask;
                }

                if (_currentGame != null)
                {
                    GameStopped?.Invoke(this, _currentGame);
                    _currentGame = null;
                    _currentProcessId = null;
                }

                _logger.LogInfo("[GameDetector] Detecção contínua parada");
                _logger.LogExit(nameof(StopDetectionAsync));
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("[GameDetector] Erro ao parar detecção", ex);
                _logger.LogExit(nameof(StopDetectionAsync));
                return false;
            }
            finally
            {
                _detectionCts?.Dispose();
                _detectionCts = null;
                _detectionTask = null;
            }
        }

        public DetectedGame? GetCurrentGame()
        {
            _logger.LogEntry(nameof(GetCurrentGame));
            _logger.LogExit(nameof(GetCurrentGame));
            return _currentGame;
        }

        public bool IsGameProcess(int processId)
        {
            _logger.LogEntry(nameof(IsGameProcess));
            if (_trackedProcesses.TryGetValue(processId, out var processInfo))
            {
                _logger.LogExit(nameof(IsGameProcess));
                return processInfo.IsGame;
            }
            _logger.LogExit(nameof(IsGameProcess));
            return false;
        }

        private async Task DetectionLoop(CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(DetectionLoop));
            _logger.LogInfo("[GameDetector] Loop de detecção iniciado (event-driven + sync periódico 30s)");

            VoltrisOptimizer.Core.ForegroundWindowTracker.Instance.ForegroundChanged += OnForegroundChangedFromTracker;

            try
            {
                var lastProcessCheck = DateTime.MinValue;

                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        var now = DateTime.Now;

                        if (now - lastProcessCheck >= _processCheckInterval)
                        {
                            await CheckProcessChangesAsync(cancellationToken);
                            lastProcessCheck = now;
                        }

                        await Task.Delay(_processCheckInterval, cancellationToken);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"[GameDetector] Erro no loop: {ex.Message}");
                        await Task.Delay(5000, cancellationToken);
                    }
                }
            }
            finally
            {
                VoltrisOptimizer.Core.ForegroundWindowTracker.Instance.ForegroundChanged -= OnForegroundChangedFromTracker;
            }
            _logger.LogExit(nameof(DetectionLoop));
        }

        private void OnForegroundChangedFromTracker(object? sender, int newPid)
        {
            _logger.LogEntry(nameof(OnForegroundChangedFromTracker));
            try
            {
                if (newPid == _currentProcessId)
                {
                    if (_currentGame != null)
                    {
                        _logger.LogInfo($"[GameDetector] Foco recuperado: {_currentGame.Name}");
                        GameFocusChanged?.Invoke(this, _currentGame);
                    }
                    _logger.LogExit(nameof(OnForegroundChangedFromTracker));
                    return;
                }

                if (_currentProcessId.HasValue)
                {
                    if (_trackedProcesses.TryGetValue(newPid, out var fgProcess))
                        _logger.LogInfo($"[GameDetector] Foco perdido {fgProcess.ProcessName}");
                    else
                        _logger.LogInfo($"[GameDetector] Foco perdido PID {newPid}");

                    if (_currentGame != null)
                        GameFocusChanged?.Invoke(this, _currentGame);
                }
                else
                {
                    Task.Run(async () => await CheckSpecificProcessAsync(newPid));
                }
            }
            catch
            {
            }
            _logger.LogExit(nameof(OnForegroundChangedFromTracker));
        }

        private async Task CheckSpecificProcessAsync(int pid)
        {
            _logger.LogEntry(nameof(CheckSpecificProcessAsync));
            // [FIX:C-3] SafeProcess.TryGet em vez de GetProcessById direto.
            //
            // Este detector é notificado de PIDs de processos recém-iniciados e
            // os inspeciona logo depois. É a janela mais provável do sistema
            // inteiro para o processo já ter terminado: um launcher que abre e
            // fecha um processo curto-lived antes do detector inspecionar é
            // comum, e o GetProcessById lançava ArgumentException.
            using var process = SafeProcess.TryGet(pid);
            if (process == null)
            {
                // Processo já encerrou. Não é erro: é a corrida natural entre
                // "ser notificado que algo iniciou" e "algo ainda existir".
                _logger.LogDebug($"[GameDetector] PID {pid} encerrou antes da analise (corrida esperada)");
                _logger.LogExit(nameof(CheckSpecificProcessAsync));
                return;
            }

            try
            {
                if (_trackedProcesses.ContainsKey(pid))
                {
                    _logger.LogExit(nameof(CheckSpecificProcessAsync));
                    return;
                }

                var processInfo = await AnalyzeProcessAsync(process, CancellationToken.None);
                _trackedProcesses[pid] = processInfo;

                if (processInfo.IsGame && _currentProcessId == null)
                {
                    await HandleGameStartedAsync(processInfo, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                // O catch era vazio e engolia TUDO, inclusive falha real de
                // analise. Registrar em Debug deixa o motivo recuperável sem
                // poluir o log principal.
                _logger.LogDebug($"[GameDetector] Falha ao analisar PID {pid}: {ex.GetType().Name}: {ex.Message}");
            }
            // process.Dispose() agora sai pelo using: antes, uma excecao em
            // AnalyzeProcessAsync impedia o Dispose e vazava o handle do
            // processo a cada ciclo.
            _logger.LogExit(nameof(CheckSpecificProcessAsync));
        }

        private async Task CheckProcessChangesAsync(CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(CheckProcessChangesAsync));
            try
            {
                var currentProcesses = Process.GetProcesses();
                var currentPids = new HashSet<int>(currentProcesses.Select(p => p.Id));

                var removedPids = _trackedProcesses.Keys.Where(pid => !currentPids.Contains(pid)).ToList();

                foreach (var pid in removedPids)
                {
                    var removedProcess = _trackedProcesses[pid];
                    _trackedProcesses.Remove(pid);

                    if (removedProcess.IsGame && pid == _currentProcessId)
                    {
                        await HandleGameStoppedAsync(removedProcess, cancellationToken);
                    }
                }

                foreach (var process in currentProcesses)
                {
                    if (!_trackedProcesses.ContainsKey(process.Id))
                    {
                        var processInfo = await AnalyzeProcessAsync(process, cancellationToken);
                        _trackedProcesses[process.Id] = processInfo;

                        if (processInfo.IsGame && _currentProcessId == null)
                        {
                            await HandleGameStartedAsync(processInfo, cancellationToken);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[GameDetector] Erro ao verificar processos: {ex.Message}");
            }
            _logger.LogExit(nameof(CheckProcessChangesAsync));
        }

        private async Task<ProcessInfo> AnalyzeProcessAsync(Process process, CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(AnalyzeProcessAsync));
            var processInfo = new ProcessInfo
            {
                ProcessId = process.Id,
                ProcessName = process.ProcessName,
                ExecutablePath = GetProcessPath(process),
                StartTime = process.StartTime,
                IsGame = false
            };

            if (!string.IsNullOrEmpty(processInfo.ExecutablePath))
            {
                var fileName = Path.GetFileNameWithoutExtension(processInfo.ExecutablePath).ToLowerInvariant();
                var fileNameWithExt = Path.GetFileName(processInfo.ExecutablePath).ToLowerInvariant();

                bool isKnownHardcoded = _knownGameExecutables.Contains(fileName) || _knownGameExecutables.Contains(fileNameWithExt) || IsGameByProcessName(processInfo.ProcessName);
                bool isGameByWindow = IsGameByWindowClass(process.Id);

                if (isKnownHardcoded || isGameByWindow)
                {
                    processInfo.IsGame = true;
                }
                else
                {
                    var evalResult = _evaluator.Evaluate(processInfo.ExecutablePath);
                    processInfo.IsGame = evalResult.IsGame && !evalResult.IsExcluded;
                }
            }

            _logger.LogExit(nameof(AnalyzeProcessAsync));
            return processInfo;
        }

        private async Task HandleGameStartedAsync(ProcessInfo processInfo, CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(HandleGameStartedAsync));
            _currentProcessId = processInfo.ProcessId;
            _currentGame = new DetectedGame
            {
                Name = GetGameDisplayName(processInfo.ProcessName, processInfo.ExecutablePath),
                ExecutablePath = processInfo.ExecutablePath ?? string.Empty,
                ProcessId = processInfo.ProcessId
            };

            _logger.LogSuccess($"[GameDetector] JOGO DETECTADO: {_currentGame.Name} (PID: {_currentGame.ProcessId})");
            GameStarted?.Invoke(this, _currentGame);
            _logger.LogExit(nameof(HandleGameStartedAsync));
        }

        private async Task HandleGameStoppedAsync(ProcessInfo processInfo, CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(HandleGameStoppedAsync));
            if (_currentGame != null)
            {
                _logger.LogInfo($"[GameDetector] JOGO ENCERRADO: {_currentGame.Name}");
                GameStopped?.Invoke(this, _currentGame);
            }

            _currentGame = null;
            _currentProcessId = null;
            _logger.LogExit(nameof(HandleGameStoppedAsync));
        }

        private void LoadKnownGameExecutables()
        {
            _logger.LogEntry(nameof(LoadKnownGameExecutables));
            foreach (var game in VoltrisOptimizer.Services.Gamer.Data.GameDatabase.KnownGames)
            {
                _knownGameExecutables.Add(game.ToLowerInvariant());
            }

            var genericLaunchers = new[] { "launcher.exe", "game.exe", "client.exe", "engine.exe", "main.exe", "mu.exe", "steam.exe", "epicgameslauncher.exe" };
            foreach (var l in genericLaunchers)
            {
                _knownGameExecutables.Add(l.ToLowerInvariant());
            }

            _logger.LogInfo($"[GameDetector] Carregados {_knownGameExecutables.Count} executáveis conhecidos do banco de dados oficial");
            _logger.LogExit(nameof(LoadKnownGameExecutables));
        }

        private bool IsGameByProcessName(string processName)
        {
            _logger.LogEntry(nameof(IsGameByProcessName));
            var name = processName.ToLowerInvariant();

            var gamePatterns = new[]
            {
                "game", "play", "launcher", "client", "steam", "epic", "origin", "uplay", "battle", "valorant", "league", "dota", "minecraft", "roblox", "fortnite", "gta", "rdr", "cyberpunk", "eldenring"
            };

            _logger.LogExit(nameof(IsGameByProcessName));
            return gamePatterns.Any(pattern => name.Contains(pattern));
        }

        private bool IsGameByWindowClass(int processId)
        {
            _logger.LogEntry(nameof(IsGameByWindowClass));
            try
            {
                var gameWindowClasses = new[]
                {
                    "UnityWndClass", "UnrealWindow", "SDL_app", "GLFW30", "Direct3DWindowClass", "OpenGLWindowClass"
                };

                foreach (var hWnd in GetProcessWindows(processId))
                {
                    var className = GetWindowClassName(hWnd);

                    if (gameWindowClasses.Any(cls => className.Contains(cls, StringComparison.OrdinalIgnoreCase)))
                    {
                        _logger.LogExit(nameof(IsGameByWindowClass));
                        return true;
                    }
                }
            }
            catch
            {
            }

            _logger.LogExit(nameof(IsGameByWindowClass));
            return false;
        }

        private string GetGameDisplayName(string processName, string? executablePath)
        {
            _logger.LogEntry(nameof(GetGameDisplayName));
            if (!string.IsNullOrEmpty(executablePath) && File.Exists(executablePath))
            {
                try
                {
                    var versionInfo = FileVersionInfo.GetVersionInfo(executablePath);

                    if (!string.IsNullOrEmpty(versionInfo.FileDescription) && versionInfo.FileDescription != processName)
                    {
                        _logger.LogExit(nameof(GetGameDisplayName));
                        return versionInfo.FileDescription;
                    }
                }
                catch
                {
                }
            }

            _logger.LogExit(nameof(GetGameDisplayName));
            return processName;
        }

        private string? GetProcessPath(Process process)
        {
            _logger.LogEntry(nameof(GetProcessPath));
            try
            {
                _logger.LogExit(nameof(GetProcessPath));
                return process.MainModule?.FileName;
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is NotSupportedException)
            {
                _logger.LogExit(nameof(GetProcessPath));
                return null;
            }
        }

        private IntPtr[] GetProcessWindows(int processId)
        {
            _logger.LogEntry(nameof(GetProcessWindows));
            var windows = new List<IntPtr>();

            EnumWindows((hWnd, lParam) =>
            {
                GetWindowThreadProcessId(hWnd, out uint windowProcessId);

                if ((int)windowProcessId == processId)
                {
                    windows.Add(hWnd);
                }

                return true;
            }, IntPtr.Zero);

            _logger.LogExit(nameof(GetProcessWindows));
            return windows.ToArray();
        }

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private string GetWindowClassName(IntPtr hWnd)
        {
            _logger.LogEntry(nameof(GetWindowClassName));
            var className = new System.Text.StringBuilder(256);
            GetClassName(hWnd, className, className.Capacity);
            _logger.LogExit(nameof(GetWindowClassName));
            return className.ToString();
        }

        public void Dispose()
        {
            _logger.LogEntry(nameof(Dispose));
            try
            {
                _ = StopDetectionAsync();
            }
            catch
            {
            }
            _logger.LogExit(nameof(Dispose));
        }
    }

    internal class ProcessInfo
    {
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public string? ExecutablePath { get; set; }
        public DateTime StartTime { get; set; }
        public bool IsGame { get; set; }
    }
}
