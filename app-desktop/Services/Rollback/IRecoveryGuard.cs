using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Rollback
{
    public interface IRecoveryGuard
    {
        Task CheckAndRecoverPendingTransactionsAsync();
        Task<string> BeginTransactionAsync(string componentName, string actionDescription, Dictionary<string, object> originalState);
        Task CommitTransactionAsync(string txId);
        Task RollbackTransactionAsync(string txId);
    }
}
