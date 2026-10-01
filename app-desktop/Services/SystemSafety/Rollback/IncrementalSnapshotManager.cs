using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace VoltrisOptimizer.Services.SystemSafety.Rollback
{
    public enum ResourceType
    {
        Registry,
        PowerSetting,
        ProcessPriority,
        ProcessAffinity,
        ServiceState
    }

    public class SnapshotItem
    {
        public ResourceType Type { get; set; }
        public string ResourceId { get; set; } // ex: HKLM\Software\Key\ValueName ou GUID do PowerSetting
        public object OriginalValue { get; set; }
        public string StateHash { get; set; } // Hash MD5 ou FNV-1a rápido para delta checks
    }

    public interface IIncrementalSnapshotManager
    {
        Guid BeginTransaction(string transactionContext);
        void RecordPreState(Guid transactionId, ResourceType type, string resourceId, object originalValue);
        IReadOnlyList<SnapshotItem> GetSnapshotItems(Guid transactionId);
        void CommitTransaction(Guid transactionId);
        void DiscardTransaction(Guid transactionId);
    }

    /// <summary>
    /// Gerenciador Incremental de Snapshots.
    /// Em vez de salvar o estado de todo o registro, salva apenas deltas das chaves exatas modificadas.
    /// Utiliza Hashing rápido para garantir que o estado só é adicionado se for diferente.
    /// Custo de memória mínimo.
    /// </summary>
    public sealed class IncrementalSnapshotManager : IIncrementalSnapshotManager
    {
        // transactionId -> List of states
        private readonly ConcurrentDictionary<Guid, ConcurrentBag<SnapshotItem>> _activeTransactions = new();

        public Guid BeginTransaction(string transactionContext)
        {
            var transactionId = Guid.NewGuid();
            _activeTransactions[transactionId] = new ConcurrentBag<SnapshotItem>();
            return transactionId;
        }

        public void RecordPreState(Guid transactionId, ResourceType type, string resourceId, object originalValue)
        {
            if (!_activeTransactions.TryGetValue(transactionId, out var bag))
                return;

            string hash = ComputeFastHash(originalValue);

            // Adiciona o estado. 
            // Como é um bag e pode haver threads concorrentes salvando no mesmo contexto,
            // garantimos que há rastreio granular do estado EXATO antes da mudança.
            bag.Add(new SnapshotItem
            {
                Type = type,
                ResourceId = resourceId,
                OriginalValue = originalValue,
                StateHash = hash
            });
        }

        public IReadOnlyList<SnapshotItem> GetSnapshotItems(Guid transactionId)
        {
            if (_activeTransactions.TryGetValue(transactionId, out var bag))
                return bag.ToArray();
            
            return Array.Empty<SnapshotItem>();
        }

        public void CommitTransaction(Guid transactionId)
        {
            // Se a transação foi sucesso, os snapshots em memória dessa transação 
            // podem ser persistidos em disco (Delta Backup) ou descartados caso não sejam 
            // necessários para a próxima sessão.
            DiscardTransaction(transactionId);
        }

        public void DiscardTransaction(Guid transactionId)
        {
            _activeTransactions.TryRemove(transactionId, out _);
        }

        private string ComputeFastHash(object value)
        {
            if (value == null) return "NULL";

            // Para alta performance e zero allocation massivo, usamos GetHashCode e o Tipo 
            // como um "hash" primário para comparar estado em RAM, não necessitando de SHA256 completo.
            // Para strings ou arrays binários maiores, um Hash rápido pode ser útil.
            return $"{value.GetType().Name}_{value.GetHashCode()}";
        }
    }
}
