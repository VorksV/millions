using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services.Gamer.Interfaces;

namespace VoltrisOptimizer.Services.Gamer.Implementation.AdaptiveGovernor
{
    /// <summary>
    /// Gerenciador de rollback - reverte todas as ações aplicadas
    /// Garante que nada fique permanente se o sistema falhar
    /// </summary>
    internal class RollbackManager
    {
        private readonly ILoggingService _logger;
        private readonly IProcessPrioritizer _processPrioritizer;
        private readonly List<RollbackData> _rollbackStack = new List<RollbackData>();
        private readonly object _lock = new object();
        
        // Windows API para afinidade
        [DllImport("kernel32.dll")]
        private static extern bool SetProcessAffinityMask(IntPtr hProcess, IntPtr dwProcessAffinityMask);
        
        public RollbackManager(ILoggingService logger, IProcessPrioritizer processPrioritizer)
        {
            _logger.LogEntry(nameof(RollbackManager), ("logger", logger != null), ("processPrioritizer", processPrioritizer != null));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _processPrioritizer = processPrioritizer ?? throw new ArgumentNullException(nameof(processPrioritizer));
            _logger.LogExit(nameof(RollbackManager));
        }
        
        /// <summary>
        /// Registra uma ação para rollback futuro
        /// </summary>
        public void RegisterRollback(RollbackData rollbackData)
        {
            _logger.LogEntry(nameof(RegisterRollback), ("rollbackData.Type", rollbackData?.Type), ("rollbackData.ProcessId", rollbackData?.ProcessId));
            
            if (rollbackData == null)
            {
                _logger.LogWarning("[RollbackManager] RollbackData nulo ignorado");
                _logger.LogExit(nameof(RegisterRollback));
                return;
            }
            
            lock (_lock)
            {
                // Verificar se já existe rollback para este processo e tipo
                var existing = _rollbackStack.FirstOrDefault(r => 
                    r.ProcessId == rollbackData.ProcessId && 
                    r.Type == rollbackData.Type);
                
                if (existing != null)
                {
                    // Atualizar existente
                    _rollbackStack.Remove(existing);
                    _logger.LogDebug($"[RollbackManager] Rollback existente atualizado para {rollbackData.Type}");
                }
                
                _rollbackStack.Add(rollbackData);
                _logger.LogInfo($"[RollbackManager] Rollback registrado: {rollbackData.Type} para processo {rollbackData.ProcessId}");
            }
            
            _logger.LogExit(nameof(RegisterRollback));
        }
        
        /// <summary>
        /// Reverte todas as ações registradas
        /// </summary>
        public async Task<int> RollbackAllAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogEntry(nameof(RollbackAllAsync));
            var stopwatch = Stopwatch.StartNew();
            
            List<RollbackData> toRollback;
            
            lock (_lock)
            {
                toRollback = _rollbackStack.ToList();
                _rollbackStack.Clear();
            }
            
            _logger.LogDebug($"[RollbackManager] {toRollback.Count} rollbacks pendentes para processar");
            
            int rolledBack = 0;
            
            foreach (var rollback in toRollback)
            {
                try
                {
                    var success = await RollbackSingleAsync(rollback, cancellationToken);
                    if (success)
                    {
                        rolledBack++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[RollbackManager] Erro ao reverter {rollback.Type}: {ex.Message}");
                }
            }
            
            if (rolledBack > 0)
            {
                _logger.LogInfo($"[RollbackManager] {rolledBack} ação(ões) revertida(s) em {stopwatch.ElapsedMilliseconds}ms");
            }
            else
            {
                _logger.LogDebug("[RollbackManager] Nenhuma ação revertida");
            }
            
            _logger.LogExit(nameof(RollbackAllAsync), rolledBack, stopwatch.ElapsedMilliseconds);
            return rolledBack;
        }
        
        /// <summary>
        /// Reverte uma ação específica
        /// </summary>
        private async Task<bool> RollbackSingleAsync(RollbackData rollback, CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(RollbackSingleAsync), ("rollback.Type", rollback.Type), ("rollback.ProcessId", rollback.ProcessId));
            
            return await Task.Run(() =>
            {
                try
                {
                    bool success;
                    switch (rollback.Type)
                    {
                        case RollbackType.ProcessPriority:
                            success = RollbackProcessPriority(rollback);
                            break;
                        
                        case RollbackType.ProcessAffinity:
                            success = RollbackProcessAffinity(rollback);
                            break;
                        
                        default:
                            _logger.LogWarning($"[RollbackManager] Tipo de rollback desconhecido: {rollback.Type}");
                            success = false;
                            break;
                    }
                    
                    _logger.LogExit(nameof(RollbackSingleAsync), success);
                    return success;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[RollbackManager] Erro ao reverter {rollback.Type}: {ex.Message}", ex);
                    _logger.LogExit(nameof(RollbackSingleAsync), false);
                    return false;
                }
            }, cancellationToken);
        }
        
        private bool RollbackProcessPriority(RollbackData rollback)
        {
            _logger.LogEntry(nameof(RollbackProcessPriority), ("processId", rollback.ProcessId), ("priority", rollback.OriginalPriority));
            
            try
            {
                using var process = Process.GetProcessById(rollback.ProcessId);
                
                if (rollback.OriginalPriority.HasValue)
                {
                    process.PriorityClass = rollback.OriginalPriority.Value;
                    _logger.LogInfo($"[RollbackManager] Prioridade do processo {rollback.ProcessId} restaurada para {rollback.OriginalPriority.Value}");
                    _logger.LogExit(nameof(RollbackProcessPriority), true);
                    return true;
                }
            }
            catch (ArgumentException)
            {
                // Processo não existe mais - não é erro
                _logger.LogInfo($"[RollbackManager] Processo {rollback.ProcessId} não existe mais (já encerrou)");
                _logger.LogExit(nameof(RollbackProcessPriority), true);
                return true; // Considerar sucesso
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[RollbackManager] Erro ao restaurar prioridade do processo {rollback.ProcessId}: {ex.Message}");
            }
            
            _logger.LogExit(nameof(RollbackProcessPriority), false);
            return false;
        }
        
        private bool RollbackProcessAffinity(RollbackData rollback)
        {
            _logger.LogEntry(nameof(RollbackProcessAffinity), ("processId", rollback.ProcessId), ("hasAffinity", rollback.OriginalAffinity.HasValue));
            
            try
            {
                using var process = Process.GetProcessById(rollback.ProcessId);
                
                if (rollback.OriginalAffinity.HasValue)
                {
                    if (SetProcessAffinityMask(process.Handle, rollback.OriginalAffinity.Value))
                    {
                        _logger.LogInfo($"[RollbackManager] Afinidade do processo {rollback.ProcessId} restaurada");
                        _logger.LogExit(nameof(RollbackProcessAffinity), true);
                        return true;
                    }
                }
            }
            catch (ArgumentException)
            {
                // Processo não existe mais - não é erro
                _logger.LogInfo($"[RollbackManager] Processo {rollback.ProcessId} não existe mais (já encerrou)");
                _logger.LogExit(nameof(RollbackProcessAffinity), true);
                return true; // Considerar sucesso
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[RollbackManager] Erro ao restaurar afinidade do processo {rollback.ProcessId}: {ex.Message}");
            }
            
            _logger.LogExit(nameof(RollbackProcessAffinity), false);
            return false;
        }
        
        /// <summary>
        /// Limpa o stack de rollback (usado quando tudo foi revertido)
        /// </summary>
        public void Clear()
        {
            _logger.LogEntry(nameof(Clear));
            
            lock (_lock)
            {
                _rollbackStack.Clear();
            }
            
            _logger.LogInfo("[RollbackManager] Stack de rollback limpo");
            _logger.LogExit(nameof(Clear));
        }
        
        /// <summary>
        /// Obtém número de ações pendentes de rollback
        /// </summary>
        public int PendingRollbacks
        {
            get
            {
                lock (_lock)
                {
                    return _rollbackStack.Count;
                }
            }
        }
    }
}

