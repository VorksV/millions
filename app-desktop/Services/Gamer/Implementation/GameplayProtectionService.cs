using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.Gamer.Interfaces;

namespace VoltrisOptimizer.Services.Gamer.Implementation
{
    public class GameplayProtectionService : IDisposable
    {
        private readonly ILoggingService _logger;
        private readonly GamerOptimizationCoordinator _coordinator;

        private bool _isGameplayActive = false;
        private int? _activeGameProcessId = null;
        private readonly SemaphoreSlim _protectionLock = new(1, 1);

        private CancellationTokenSource? _processWatcherCts;
        private Task? _processWatcherTask;

        private readonly HashSet<string> _blockedOperations = new();
        private readonly Dictionary<string, DateTime> _operationAttempts = new();

        private static readonly string[] STUTTER_CAUSING_OPERATIONS = new[]
        {
            "MemoryCleanStandbyList",
            "RegistryRealtimeChange",
            "PowerPlanReapply",
            "NetworkTcpTweak",
            "ServiceStopStart",
            "DriverChange",
            "SystemFileModify",
            "HeavyDiskOperation",
            "WindowsUpdateCheck"
        };

        public GameplayProtectionService(ILoggingService logger, GamerOptimizationCoordinator coordinator)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogEntry(nameof(GameplayProtectionService));
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            _logger.LogExit(nameof(GameplayProtectionService));
        }

        public async Task<bool> ActivateGameplayProtectionAsync(int gameProcessId, CancellationToken cancellationToken = default)
        {
            _logger.LogEntry(nameof(ActivateGameplayProtectionAsync));
            await _protectionLock.WaitAsync(cancellationToken);

            try
            {
                if (_isGameplayActive && _activeGameProcessId == gameProcessId)
                {
                    _logger.LogInfo("[GameplayProtection] Proteção já ativa para este processo");
                    _logger.LogExit(nameof(ActivateGameplayProtectionAsync));
                    return true;
                }

                if (!IsValidGameProcess(gameProcessId))
                {
                    _logger.LogWarning($"[GameplayProtection] Processo {gameProcessId} não encontrado");
                    _logger.LogExit(nameof(ActivateGameplayProtectionAsync));
                    return false;
                }

                _isGameplayActive = true;
                _activeGameProcessId = gameProcessId;

                await StartProcessWatcherAsync(gameProcessId, cancellationToken);

                BlockStutterCausingOperations();

                _logger.LogSuccess($"[GameplayProtection] Proteção ativada para PID {gameProcessId}");
                _logger.LogExit(nameof(ActivateGameplayProtectionAsync));
                return true;
            }
            finally
            {
                _protectionLock.Release();
            }
        }

        public async Task<bool> DeactivateGameplayProtectionAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogEntry(nameof(DeactivateGameplayProtectionAsync));
            await _protectionLock.WaitAsync(cancellationToken);

            try
            {
                if (!_isGameplayActive)
                {
                    _logger.LogInfo("[GameplayProtection] Proteção não está ativa");
                    _logger.LogExit(nameof(DeactivateGameplayProtectionAsync));
                    return true;
                }

                _isGameplayActive = false;
                var oldProcessId = _activeGameProcessId;
                _activeGameProcessId = null;

                await StopProcessWatcherAsync();

                UnblockAllOperations();

                _logger.LogSuccess($"[GameplayProtection] Proteção desativada (era PID {oldProcessId})");
                _logger.LogExit(nameof(DeactivateGameplayProtectionAsync));
                return true;
            }
            finally
            {
                _protectionLock.Release();
            }
        }

        public bool IsOperationAllowed(string operationName)
        {
            _logger.LogEntry(nameof(IsOperationAllowed));
            if (!_isGameplayActive)
            {
                _logger.LogExit(nameof(IsOperationAllowed));
                return true;
            }

            var isBlocked = _blockedOperations.Contains(operationName);

            if (isBlocked)
            {
                _operationAttempts[operationName] = DateTime.Now;
                _logger.LogWarning($"[GameplayProtection] OPERAÇÃO BLOQUEADA: {operationName} (stutter risk)");
            }

            _logger.LogExit(nameof(IsOperationAllowed));
            return !isBlocked;
        }

        public bool IsCriticalGameplayMode()
        {
            _logger.LogEntry(nameof(IsCriticalGameplayMode));
            _logger.LogExit(nameof(IsCriticalGameplayMode));
            return _isGameplayActive && _activeGameProcessId.HasValue;
        }

        public Dictionary<string, int> GetBlockedOperationStats()
        {
            _logger.LogEntry(nameof(GetBlockedOperationStats));
            var stats = new Dictionary<string, int>();
            var now = DateTime.Now;

            foreach (var kvp in _operationAttempts.Where(x => (now - x.Value).TotalMinutes < 5))
            {
                stats[kvp.Key] = stats.GetValueOrDefault(kvp.Key, 0) + 1;
            }

            _logger.LogExit(nameof(GetBlockedOperationStats));
            return stats;
        }

        private async Task StartProcessWatcherAsync(int processId, CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(StartProcessWatcherAsync));
            await StopProcessWatcherAsync();

            _processWatcherCts = new CancellationTokenSource();
            var token = _processWatcherCts.Token;

            _processWatcherTask = Task.Run(async () =>
            {
                _logger.LogInfo($"[GameplayProtection] Iniciando watcher do processo {processId}");

                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        if (!IsValidGameProcess(processId))
                        {
                            _logger.LogInfo($"[GameplayProtection] Processo {processId} encerrado - desativando proteção");
                            await DeactivateGameplayProtectionAsync(token);
                            break;
                        }

                        if (!IsProcessInForeground(processId))
                        {
                            _logger.LogDebug($"[GameplayProtection] Jogo minimizado - modo relaxado");
                        }

                        await Task.Delay(1000, token);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"[GameplayProtection] Erro no watcher: {ex.Message}");
                        await Task.Delay(5000, token);
                    }
                }
            }, token);
            _logger.LogExit(nameof(StartProcessWatcherAsync));
        }

        private async Task StopProcessWatcherAsync()
        {
            _logger.LogEntry(nameof(StopProcessWatcherAsync));
            if (_processWatcherCts != null)
            {
                _processWatcherCts.Cancel();
                _processWatcherCts.Dispose();
                _processWatcherCts = null;
            }

            if (_processWatcherTask != null)
            {
                try
                {
                    await _processWatcherTask;
                }
                catch (TaskCanceledException)
                {
                }
                _processWatcherTask = null;
            }
            _logger.LogExit(nameof(StopProcessWatcherAsync));
        }

        private void BlockStutterCausingOperations()
        {
            _logger.LogEntry(nameof(BlockStutterCausingOperations));
            _blockedOperations.Clear();

            foreach (var operation in STUTTER_CAUSING_OPERATIONS)
            {
                _blockedOperations.Add(operation);
            }

            _logger.LogInfo($"[GameplayProtection] Bloqueadas {_blockedOperations.Count} operações que causam stutter");
            _logger.LogExit(nameof(BlockStutterCausingOperations));
        }

        private void UnblockAllOperations()
        {
            _logger.LogEntry(nameof(UnblockAllOperations));
            var blockedCount = _blockedOperations.Count;
            _blockedOperations.Clear();
            _operationAttempts.Clear();

            _logger.LogInfo($"[GameplayProtection] Liberadas {blockedCount} operações");
            _logger.LogExit(nameof(UnblockAllOperations));
        }

        private bool IsValidGameProcess(int processId)
        {
            _logger.LogEntry(nameof(IsValidGameProcess));
            try
            {
                using var process = Process.GetProcessById(processId);
                var result = !process.HasExited && process.ProcessName.Length > 0;
                _logger.LogExit(nameof(IsValidGameProcess));
                return result;
            }
            catch
            {
                _logger.LogExit(nameof(IsValidGameProcess));
                return false;
            }
        }

        private bool IsProcessInForeground(int processId)
        {
            _logger.LogEntry(nameof(IsProcessInForeground));
            try
            {
                int foregroundProcessId = VoltrisOptimizer.Core.ForegroundWindowTracker.Instance.CurrentPid;
                var result = foregroundProcessId == processId;
                _logger.LogExit(nameof(IsProcessInForeground));
                return result;
            }
            catch
            {
                _logger.LogExit(nameof(IsProcessInForeground));
                return false;
            }
        }

        public void Dispose()
        {
            _logger.LogEntry(nameof(Dispose));
            try
            {
                _ = DeactivateGameplayProtectionAsync();
            }
            catch
            {
            }
            _protectionLock?.Dispose();
            _processWatcherCts?.Dispose();
            _logger.LogExit(nameof(Dispose));
        }
    }

    public static class GameplayProtectionExtensions
    {
        public static bool CanExecuteOperation(this GameplayProtectionService protection, string operationName)
        {
            return protection.IsOperationAllowed(operationName);
        }

        public static async Task<T> ExecuteWithProtectionAsync<T>(this GameplayProtectionService protection, string operationName, Func<Task<T>> operation, CancellationToken cancellationToken = default)
        {
            if (!protection.CanExecuteOperation(operationName))
            {
                throw new InvalidOperationException($"Operação '{operationName}' bloqueada durante gameplay");
            }

            return await operation();
        }
    }
}
