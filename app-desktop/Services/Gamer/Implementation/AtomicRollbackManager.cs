using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.Gamer.Interfaces;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer.Implementation
{
    public class AtomicRollbackManager : IDisposable
    {
        private readonly ILoggingService _logger;
        private readonly string _stateFilePath;
        private readonly string _backupDirectory;
        private readonly SemaphoreSlim _rollbackLock = new(1, 1);

        private readonly Dictionary<string, IOptimizationState> _appliedStates = new();
        private bool _isRollbackInProgress = false;

        private PersistentOptimizationState _persistentState = new();

        public AtomicRollbackManager(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogEntry(nameof(AtomicRollbackManager));

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _stateFilePath = Path.Combine(baseDir, "Data", "gamer_optimization_state.json");
            _backupDirectory = Path.Combine(baseDir, "Data", "gamer_backups");

            Directory.CreateDirectory(Path.GetDirectoryName(_stateFilePath)!);
            Directory.CreateDirectory(_backupDirectory);

            LoadPersistentState();

            RegisterCrashHandlers();
            _logger.LogExit(nameof(AtomicRollbackManager));
        }

        public async Task<bool> ApplyWithRollbackAsync(IOptimization optimization, CancellationToken cancellationToken = default)
        {
            _logger.LogEntry(nameof(ApplyWithRollbackAsync));
            if (optimization == null)
                throw new ArgumentNullException(nameof(optimization));

            await _rollbackLock.WaitAsync(cancellationToken);

            try
            {
                var state = await optimization.CaptureStateAsync(cancellationToken);

                if (state == null)
                {
                    _logger.LogWarning($"[RollbackManager] Não foi possível capturar estado de {optimization.GetType().Name}");
                    _logger.LogExit(nameof(ApplyWithRollbackAsync));
                    return false;
                }

                try
                {
                    var success = await optimization.ApplyAsync(cancellationToken);

                    if (!success)
                    {
                        _logger.LogError($"[RollbackManager] Falha ao aplicar {optimization.GetType().Name}");
                        _logger.LogExit(nameof(ApplyWithRollbackAsync));
                        return false;
                    }

                    _appliedStates[optimization.GetType().Name] = state;

                    await PersistStateAsync(optimization.GetType().Name, state, cancellationToken);

                    _logger.LogSuccess($"[RollbackManager] {optimization.GetType().Name} aplicado com rollback seguro");

                    _logger.LogExit(nameof(ApplyWithRollbackAsync));
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[RollbackManager] Erro ao aplicar {optimization.GetType().Name} - iniciando rollback", ex);

                    try
                    {
                        await state.RestoreAsync(cancellationToken);
                        _logger.LogInfo($"[RollbackManager] Rollback imediato executado para {optimization.GetType().Name}");
                    }
                    catch (Exception rollbackEx)
                    {
                        _logger.LogError("[RollbackManager] Falha crítica no rollback imediato", rollbackEx);
                    }

                    _logger.LogExit(nameof(ApplyWithRollbackAsync));
                    return false;
                }
            }
            finally
            {
                _rollbackLock.Release();
            }
        }

        public async Task<bool> RollbackAllAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogEntry(nameof(RollbackAllAsync));
            await _rollbackLock.WaitAsync(cancellationToken);

            // [FIX:M-5] Este motor tem o próprio _rollbackLock e o próprio
            // _isRollbackInProgress, mas existe um segundo motor de rollback
            // ativo no processo (Core.Validation.AtomicRollbackManager, chamado
            // pelo ExecutionQueue quando uma evidência é reprovada) que não
            // enxergava este lock. Adquirir o gate compartilhado garante que os
            // dois reverts de sistema nunca interleavam.
            var aguardouGateGlobal = !VoltrisOptimizer.Core.Validation.AtomicRollbackManager.GlobalRollbackGate.Wait(0);
            if (aguardouGateGlobal)
            {
                _logger.LogWarning(
                    "[FIX:M-5] Outro motor de rollback está revertsendo; aguardando o gate global " +
                    "antes de reverter as otimizações do Gamer (motor=Services.Gamer.Implementation)");
                await VoltrisOptimizer.Core.Validation.AtomicRollbackManager.GlobalRollbackGate.WaitAsync(cancellationToken);
            }

            try
            {
                _logger.LogInfo(
                    $"[FIX:M-5] Gate global de rollback adquirido | motor=Services.Gamer.Implementation | " +
                    $"waited={aguardouGateGlobal} | otimizacoesAtivas={_appliedStates.Count}");
                if (_isRollbackInProgress)
                {
                    _logger.LogWarning("[RollbackManager] Rollback já em progressão");
                    _logger.LogExit(nameof(RollbackAllAsync));
                    return false;
                }

                if (!_appliedStates.Any())
                {
                    _logger.LogInfo("[RollbackManager] Nenhuma otimização ativa para restaurar");
                    _logger.LogExit(nameof(RollbackAllAsync));
                    return true;
                }

                _isRollbackInProgress = true;

                _logger.LogInfo($"[RollbackManager] Iniciando rollback de {_appliedStates.Count} otimizações");

                var success = true;
                var rollbackOrder = _appliedStates.ToList().AsEnumerable().Reverse().ToList();

                foreach (var kvp in rollbackOrder)
                {
                    try
                    {
                        _logger.LogInfo($"[RollbackManager] Restáaurando {kvp.Key}...");

                        var restáãoreSuccess = await kvp.Value.RestoreAsync(cancellationToken);

                        if (!restáãoreSuccess)
                        {
                            _logger.LogWarning($"[RollbackManager] Falha ao restaurar {kvp.Key}");
                            success = false;
                        }
                        else
                        {
                            _logger.LogSuccess($"[RollbackManager] {kvp.Key} restaurado com sucessão");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"[RollbackManager] Erro crítico ao restaurar {kvp.Key}", ex);
                        success = false;
                    }
                }

                _appliedStates.Clear();
                await ClearPersistentStateAsync();

                _isRollbackInProgress = false;

                if (success)
                {
                    _logger.LogSuccess("[RollbackManager] Rollback completo realizado com sucessão");
                }
                else
                {
                    _logger.LogWarning("[RollbackManager] Rollback concluído com algumas falhas");
                }

                _logger.LogExit(nameof(RollbackAllAsync));
                return success;
            }
            finally
            {
                // [FIX:M-5] Libera os dois locks na ordem inversa da aquisição.
                VoltrisOptimizer.Core.Validation.AtomicRollbackManager.GlobalRollbackGate.Release();
                _rollbackLock.Release();
            }
        }

        public async Task<bool> RecoverFromCrashAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogEntry(nameof(RecoverFromCrashAsync));
            _logger.LogInfo("[RollbackManager] Iniciando recuperação de crash");

            try
            {
                LoadPersistentState();

                if (!_persistentState.HasActiveOptimizations)
                {
                    _logger.LogInfo("[RollbackManager] Nenhuma otimização ativa detectada para recuperação");
                    _logger.LogExit(nameof(RecoverFromCrashAsync));
                    return true;
                }

                _logger.LogWarning($"[RollbackManager] Detectadas {_persistentState.AppliedOptimizations.Count} otimizações ativas - iniciando rollback");

                var success = true;

                foreach (var optState in _persistentState.AppliedOptimizations.AsEnumerable().Reverse())
                {
                    try
                    {
                        _logger.LogInfo($"[RollbackManager] Recuperando {optState.OptimizationType}...");

                        var optimization = CreateOptimizationFromType(optState.OptimizationType);

                        if (optimization != null)
                        {
                            var restáãoreSuccess = await RestoreOptimizationStateAsync(optimization, optState, cancellationToken);

                            if (!restáãoreSuccess)
                            {
                                _logger.LogWarning($"[RollbackManager] Falha ao recuperar {optState.OptimizationType}");
                                success = false;
                            }
                        }
                        else
                        {
                            _logger.LogWarning($"[RollbackManager] Não foi possível criar otimização {optState.OptimizationType}");
                            success = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"[RollbackManager] Erro na recuperação de {optState.OptimizationType}", ex);
                        success = false;
                    }
                }

                await ClearPersistentStateAsync();

                if (success)
                {
                    _logger.LogSuccess("[RollbackManager] Recuperação de crash concluída com sucessão");
                }
                else
                {
                    _logger.LogWarning("[RollbackManager] Recuperação concluída com algumas falhas");
                }

                _logger.LogExit(nameof(RecoverFromCrashAsync));
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError("[RollbackManager] Erro crítico na recuperação de crash", ex);
                _logger.LogExit(nameof(RecoverFromCrashAsync));
                return false;
            }
        }

        public bool HasPendingRollbacks()
        {
            _logger.LogEntry(nameof(HasPendingRollbacks));
            _logger.LogExit(nameof(HasPendingRollbacks));
            return _appliedStates.Any() || _persistentState.HasActiveOptimizations;
        }

        public RollbackStatus GetStatus()
        {
            _logger.LogEntry(nameof(GetStatus));
            _logger.LogExit(nameof(GetStatus));
            return new RollbackStatus
            {
                IsActive = _appliedStates.Any(),
                AppliedOptimizations = _appliedStates.Keys.ToList(),
                HasCrashState = _persistentState.HasActiveOptimizations,
                CrashOptimizations = _persistentState.AppliedOptimizations.Select(o => o.OptimizationType).ToList(),
                IsRollbackInProgress = _isRollbackInProgress
            };
        }

        private async Task PersistStateAsync(string optimizationType, IOptimizationState state, CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(PersistStateAsync));
            try
            {
                var optState = new OptimizationStateData
                {
                    OptimizationType = optimizationType,
                    Timestamp = DateTime.UtcNow,
                    SerializedState = await SerializeStateAsync(state)
                };

                _persistentState.AppliedOptimizations.Add(optState);
                await SavePersistentStateAsync(cancellationToken);

                _logger.LogDebug($"[RollbackManager] Estado persistido para {optimizationType}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[RollbackManager] Erro ao persistir estado: {ex.Message}");
            }
            _logger.LogExit(nameof(PersistStateAsync));
        }

        private async Task SavePersistentStateAsync(CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(SavePersistentStateAsync));
            try
            {
                var json = JsonSerializer.Serialize(_persistentState, new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, WriteIndented = true });
                await File.WriteAllTextAsync(_stateFilePath, json, cancellationToken);
                _logger.LogDebug("[RollbackManager] Estado persistente salvo");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[RollbackManager] Erro ao salvar estado persistente: {ex.Message}");
            }
            _logger.LogExit(nameof(SavePersistentStateAsync));
        }

        private void SavePersistentStateSync()
        {
            _logger.LogEntry(nameof(SavePersistentStateSync));
            try
            {
                var json = JsonSerializer.Serialize(_persistentState, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_stateFilePath, json);
                _logger.LogDebug("[RollbackManager] Estado persistente salvo (Sync)");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[RollbackManager] Erro ao salvar estado persistente (Sync): {ex.Message}");
            }
            _logger.LogExit(nameof(SavePersistentStateSync));
        }

        private void LoadPersistentState()
        {
            _logger.LogEntry(nameof(LoadPersistentState));
            try
            {
                var legacyPath = Path.Combine(Path.GetDirectoryName(_stateFilePath)!, "gamer_optimization_state.jsãon");
                var loadPath = File.Exists(_stateFilePath) ? _stateFilePath
                    : File.Exists(legacyPath) ? legacyPath : _stateFilePath;

                if (File.Exists(loadPath))
                {
                    var json = File.ReadAllText(loadPath);
                    _persistentState = JsonSerializer.Deserialize<PersistentOptimizationState>(json) ?? new();
                    _logger.LogInfo($"[RollbackManager] Estado persistente carregado: {_persistentState.AppliedOptimizations.Count} otimizações");
                }
                else
                {
                    _persistentState = new();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[RollbackManager] Erro ao carregar estado persistente: {ex.Message}");
                _persistentState = new();
            }
            _logger.LogExit(nameof(LoadPersistentState));
        }

        private async Task ClearPersistentStateAsync()
        {
            _logger.LogEntry(nameof(ClearPersistentStateAsync));
            try
            {
                _persistentState = new();

                if (File.Exists(_stateFilePath))
                {
                    File.Delete(_stateFilePath);
                }

                _logger.LogInfo("[RollbackManager] Estado persistente limpo");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[RollbackManager] Erro ao limpar estado persistente: {ex.Message}");
            }
            _logger.LogExit(nameof(ClearPersistentStateAsync));
        }

        private async Task<string> SerializeStateAsync(IOptimizationState state)
        {
            _logger.LogEntry(nameof(SerializeStateAsync));
            try
            {
                _logger.LogExit(nameof(SerializeStateAsync));
                return $"State_{state.GetType().Name}_{DateTime.UtcNow:O}";
            }
            catch
            {
                _logger.LogExit(nameof(SerializeStateAsync));
                return string.Empty;
            }
        }

        private IOptimization? CreateOptimizationFromType(string optimizationType)
        {
            _logger.LogEntry(nameof(CreateOptimizationFromType));
            _logger.LogExit(nameof(CreateOptimizationFromType));
            return optimizationType switch
            {
                "CpuGamingOptimizer" => App.Services?.GetService(typeof(ICpuGamingOptimizer)) as IOptimization,
                "GpuGamingOptimizer" => App.Services?.GetService(typeof(IGpuGamingOptimizer)) as IOptimization,
                "MemoryGamingOptimizer" => App.Services?.GetService(typeof(IMemoryGamingOptimizer)) as IOptimization,
                _ => null
            };
        }

        private async Task<bool> RestoreOptimizationStateAsync(IOptimization optimization, OptimizationStateData stateData, CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(RestoreOptimizationStateAsync));
            try
            {
                _logger.LogExit(nameof(RestoreOptimizationStateAsync));
                return await optimization.RestoreAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[RollbackManager] Erro ao restaurar estado de {stateData.OptimizationType}: {ex.Message}");
                _logger.LogExit(nameof(RestoreOptimizationStateAsync));
                return false;
            }
        }

        private void RegisterCrashHandlers()
        {
            _logger.LogEntry(nameof(RegisterCrashHandlers));
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            _logger.LogExit(nameof(RegisterCrashHandlers));
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            _logger.LogEntry(nameof(OnUnhandledException));
            _logger.LogError("[RollbackManager] Unhandled exception detectado - salvando estado para recovery");
            try
            {
                SavePersistentStateSync();
            }
            catch
            {
            }
            _logger.LogExit(nameof(OnUnhandledException));
        }

        private void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            _logger.LogEntry(nameof(OnUnobservedTaskException));
            if (!e.Observed)
            {
                _logger.LogError("[RollbackManager] Unobserved task exception detectado - salvando estado para recovery");
                try
                {
                    SavePersistentStateSync();
                }
                catch
                {
                }
            }
            _logger.LogExit(nameof(OnUnobservedTaskException));
        }

        public void Dispose()
        {
            _logger.LogEntry(nameof(Dispose));
            try
            {
                if (_appliedStates.Any())
                {
                    _ = RollbackAllAsync();
                }
            }
            catch
            {
            }

            _rollbackLock?.Dispose();
            _logger.LogExit(nameof(Dispose));
        }

        public class PersistentOptimizationState
        {
            public List<OptimizationStateData> AppliedOptimizations { get; set; } = new();
            public DateTime LastSaved { get; set; } = DateTime.UtcNow;
            public bool HasActiveOptimizations => AppliedOptimizations.Any();
        }

        public class OptimizationStateData
        {
            public string OptimizationType { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; }
            public string SerializedState { get; set; } = string.Empty;
        }

        public class RollbackStatus
        {
            public bool IsActive { get; set; }
            public List<string> AppliedOptimizations { get; set; } = new();
            public bool HasCrashState { get; set; }
            public List<string> CrashOptimizations { get; set; } = new();
            public bool IsRollbackInProgress { get; set; }
        }

        public interface IOptimization
        {
            Task<IOptimizationState> CaptureStateAsync(CancellationToken cancellationToken = default);
            Task<bool> ApplyAsync(CancellationToken cancellationToken = default);
            Task<bool> RestoreAsync(CancellationToken cancellationToken = default);
        }

        public interface IOptimizationState : IDisposable
        {
            Task<bool> RestoreAsync(CancellationToken cancellationToken = default);
        }
    }
}
