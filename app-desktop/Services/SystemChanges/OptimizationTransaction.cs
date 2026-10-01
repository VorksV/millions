using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.SystemChanges
{
    /// <summary>
    /// Gerenciador de transações de otimização com rollback completo.
    /// </summary>
    public class OptimizationTransactionManager
    {
        private readonly ConcurrentDictionary<string, OptimizationTransaction> _transactions;
        private readonly ISafeOperation _safeOperation;
        private readonly object _lockObject = new object();

        /// <summary>
        /// Inicializa uma nova instância de <see cref="OptimizationTransactionManager"/>.
        /// </summary>
        /// <param name="safeOperation">Serviço de operações seguras.</param>
        public OptimizationTransactionManager(ISafeOperation safeOperation)
        {
            _safeOperation = safeOperation ?? throw new ArgumentNullException(nameof(safeOperation));
            _transactions = new ConcurrentDictionary<string, OptimizationTransaction>();
        }

        /// <summary>
        /// Inicia uma nova transação de otimização.
        /// </summary>
        /// <param name="transactionId">ID da transação.</param>
        /// <param name="description">Descrição da transação.</param>
        /// <returns>Transação de otimização.</returns>
        public OptimizationTransaction BeginTransaction(string transactionId, string description)
        {
            if (string.IsNullOrEmpty(transactionId))
                throw new ArgumentException("O ID da transação não pode ser vazio.", nameof(transactionId));

            var transaction = new OptimizationTransaction
            {
                Id = transactionId,
                Description = description,
                StartTime = DateTime.UtcNow,
                Status = TransactionStatus.InProgress
            };

            _transactions[transactionId] = transaction;
            return transaction;
        }

        /// <summary>
        /// Finaliza uma transação com sucesso.
        /// </summary>
        /// <param name="transactionId">ID da transação.</param>
        /// <returns>Tarefa assíncrona.</returns>
        public async Task CommitAsync(string transactionId)
        {
            if (string.IsNullOrEmpty(transactionId))
                throw new ArgumentException("O ID da transação não pode ser vazio.", nameof(transactionId));

            if (_transactions.TryGetValue(transactionId, out var transaction))
            {
                transaction.Status = TransactionStatus.Committed;
                transaction.EndTime = DateTime.UtcNow;
                
                // Limpa os dados de rollback após commit bem-sucedido
                transaction.RollbackActions.Clear();
                
                await Task.CompletedTask;
            }
            else
            {
                throw new InvalidOperationException($"Transação '{transactionId}' não encontrada.");
            }
        }

        /// <summary>
        /// Reverte uma transação.
        /// </summary>
        /// <param name="transactionId">ID da transação.</param>
        /// <returns>Resultado do rollback.</returns>
        public async Task<RollbackResult> RollbackAsync(string transactionId)
        {
            if (string.IsNullOrEmpty(transactionId))
                throw new ArgumentException("O ID da transação não pode ser vazio.", nameof(transactionId));

            if (!_transactions.TryGetValue(transactionId, out var transaction))
            {
                throw new InvalidOperationException($"Transação '{transactionId}' não encontrada.");
            }

            var result = new RollbackResult
            {
                TransactionId = transactionId,
                Success = true,
                RollbackActionsExecuted = 0
            };

            // Executa as ações de rollback em ordem inversa
            for (int i = transaction.RollbackActions.Count - 1; i >= 0; i--)
            {
                try
                {
                    await transaction.RollbackActions[i]();
                    result.RollbackActionsExecuted++;
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.Errors.Add($"Falha ao executar rollback da ação {i + 1}: {ex.Message}");
                }
            }

            transaction.Status = TransactionStatus.RolledBack;
            transaction.EndTime = DateTime.UtcNow;

            return result;
        }

        /// <summary>
        /// Adiciona uma ação de rollback a uma transação.
        /// </summary>
        /// <param name="transactionId">ID da transação.</param>
        /// <param name="rollbackAction">Ação de rollback.</param>
        public void AddRollbackAction(string transactionId, Func<Task> rollbackAction)
        {
            if (string.IsNullOrEmpty(transactionId))
                throw new ArgumentException("O ID da transação não pode ser vazio.", nameof(transactionId));

            if (rollbackAction == null)
                throw new ArgumentNullException(nameof(rollbackAction));

            if (_transactions.TryGetValue(transactionId, out var transaction))
            {
                transaction.RollbackActions.Add(rollbackAction);
            }
            else
            {
                throw new InvalidOperationException($"Transação '{transactionId}' não encontrada.");
            }
        }

        /// <summary>
        /// Obtém o status de uma transação.
        /// </summary>
        /// <param name="transactionId">ID da transação.</param>
        /// <returns>Status da transação.</returns>
        public TransactionStatus GetTransactionStatus(string transactionId)
        {
            if (string.IsNullOrEmpty(transactionId))
                throw new ArgumentException("O ID da transação não pode ser vazio.", nameof(transactionId));

            if (_transactions.TryGetValue(transactionId, out var transaction))
            {
                return transaction.Status;
            }

            throw new InvalidOperationException($"Transação '{transactionId}' não encontrada.");
        }

        /// <summary>
        /// Obtém todas as transações ativas.
        /// </summary>
        /// <returns>Lista de transações ativas.</returns>
        public List<OptimizationTransaction> GetActiveTransactions()
        {
            var activeTransactions = new List<OptimizationTransaction>();
            
            foreach (var transaction in _transactions.Values)
            {
                if (transaction.Status == TransactionStatus.InProgress)
                {
                    activeTransactions.Add(transaction);
                }
            }
            
            return activeTransactions;
        }

        /// <summary>
        /// Remove uma transação do gerenciador.
        /// </summary>
        /// <param name="transactionId">ID da transação.</param>
        public void RemoveTransaction(string transactionId)
        {
            if (string.IsNullOrEmpty(transactionId))
                throw new ArgumentException("O ID da transação não pode ser vazio.", nameof(transactionId));

            _transactions.TryRemove(transactionId, out _);
        }
    }

    /// <summary>
    /// Transação de otimização.
    /// </summary>
    public class OptimizationTransaction
    {
        /// <summary>
        /// ID da transação.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Descrição da transação.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Data/hora de início da transação.
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// Data/hora de término da transação.
        /// </summary>
        public DateTime? EndTime { get; set; }

        /// <summary>
        /// Status da transação.
        /// </summary>
        public TransactionStatus Status { get; set; }

        /// <summary>
        /// Ações de rollback associadas à transação.
        /// </summary>
        public List<Func<Task>> RollbackActions { get; set; } = new List<Func<Task>>();

        /// <summary>
        /// Metadados da transação.
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// Status de uma transação.
    /// </summary>
    public enum TransactionStatus
    {
        /// <summary>
        /// Transação em andamento.
        /// </summary>
        InProgress,

        /// <summary>
        /// Transação confirmada.
        /// </summary>
        Committed,

        /// <summary>
        /// Transação revertida.
        /// </summary>
        RolledBack,

        /// <summary>
        /// Transação falhou.
        /// </summary>
        Failed
    }

    /// <summary>
    /// Resultado de uma operação de rollback.
    /// </summary>
    public class RollbackResult
    {
        /// <summary>
        /// ID da transação.
        /// </summary>
        public string TransactionId { get; set; }

        /// <summary>
        /// Indica se o rollback foi bem-sucedido.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Número de ações de rollback executadas.
        /// </summary>
        public int RollbackActionsExecuted { get; set; }

        /// <summary>
        /// Lista de erros ocorridos durante o rollback.
        /// </summary>
        public List<string> Errors { get; set; } = new List<string>();

        /// <summary>
        /// Tempo total de execução do rollback.
        /// </summary>
        public TimeSpan Duration { get; set; }
    }

    /// <summary>
    /// Serviço de auditoria de transações.
    /// </summary>
    public class TransactionAuditService
    {
        private readonly OptimizationTransactionManager _transactionManager;
        private readonly List<TransactionAuditRecord> _auditLog;

        /// <summary>
        /// Inicializa uma nova instância de <see cref="TransactionAuditService"/>.
        /// </summary>
        /// <param name="transactionManager">Gerenciador de transações.</param>
        public TransactionAuditService(OptimizationTransactionManager transactionManager)
        {
            _transactionManager = transactionManager ?? throw new ArgumentNullException(nameof(transactionManager));
            _auditLog = new List<TransactionAuditRecord>();
        }

        /// <summary>
        /// Registra uma operação em uma transação.
        /// </summary>
        /// <param name="transactionId">ID da transação.</param>
        /// <param name="operation">Descrição da operação.</param>
        /// <param name="details">Detalhes da operação.</param>
        public void LogOperation(string transactionId, string operation, string details = null)
        {
            var record = new TransactionAuditRecord
            {
                TransactionId = transactionId,
                Operation = operation,
                Details = details,
                Timestamp = DateTime.UtcNow
            };

            _auditLog.Add(record);
        }

        /// <summary>
        /// Obtém o log de auditoria para uma transação.
        /// </summary>
        /// <param name="transactionId">ID da transação.</param>
        /// <returns>Lista de registros de auditoria.</returns>
        public List<TransactionAuditRecord> GetAuditLog(string transactionId)
        {
            return _auditLog.FindAll(r => r.TransactionId == transactionId);
        }

        /// <summary>
        /// Gera um relatório de auditoria.
        /// </summary>
        /// <returns>Relatório de auditoria.</returns>
        public TransactionAuditReport GenerateAuditReport()
        {
            var report = new TransactionAuditReport
            {
                GeneratedAt = DateTime.UtcNow,
                TotalTransactions = _auditLog.Count > 0 ? _auditLog.ConvertAll(r => r.TransactionId).Distinct().Count() : 0,
                SuccessfulCommits = _auditLog.Count(r => r.Operation == "Commit"),
                RollbacksPerformed = _auditLog.Count(r => r.Operation == "Rollback")
            };

            return report;
        }
    }

    /// <summary>
    /// Registro de auditoria de transação.
    /// </summary>
    public class TransactionAuditRecord
    {
        /// <summary>
        /// ID da transação.
        /// </summary>
        public string TransactionId { get; set; }

        /// <summary>
        /// Operação realizada.
        /// </summary>
        public string Operation { get; set; }

        /// <summary>
        /// Detalhes da operação.
        /// </summary>
        public string Details { get; set; }

        /// <summary>
        /// Timestamp do registro.
        /// </summary>
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Relatório de auditoria de transações.
    /// </summary>
    public class TransactionAuditReport
    {
        /// <summary>
        /// Data/hora de geração do relatório.
        /// </summary>
        public DateTime GeneratedAt { get; set; }

        /// <summary>
        /// Número total de transações.
        /// </summary>
        public int TotalTransactions { get; set; }

        /// <summary>
        /// Número de commits bem-sucedidos.
        /// </summary>
        public int SuccessfulCommits { get; set; }

        /// <summary>
        /// Número de rollbacks realizados.
        /// </summary>
        public int RollbacksPerformed { get; set; }

        /// <summary>
        /// Taxa de sucesso de transações.
        /// </summary>
        public double SuccessRate => TotalTransactions > 0 ? (double)SuccessfulCommits / TotalTransactions : 0;
    }
}
