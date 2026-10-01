using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Optimization.ZeroTrustCleaning
{
    public enum NodeType
    {
        File,
        Directory,
        RegistryKey
    }

    public class MutableNode
    {
        public string Path { get; set; } = string.Empty;
        public NodeType Type { get; set; }
        public DateTime LastAccessTime { get; set; }
        public long SizeBytes { get; set; }
        public string SourceModuleId { get; set; } = string.Empty;
        public string FriendlyName { get; set; } = string.Empty;
    }

    public class NodeRiskProfile
    {
        public int DangerScore { get; set; }
        public int RebuildCostSeconds { get; set; }
        public bool IsHardBlocked { get; set; }
        public string BlockReason { get; set; } = string.Empty;
    }

    public interface IRiskAdaptiveEngine
    {
        Task<NodeRiskProfile> EvaluateRiskAsync(MutableNode node, IntelligentProfileType currentProfile);
    }

    public interface IPersonalDataGuard
    {
        bool ContainsUserGeneratedData(MutableNode node);
        bool ContainsSensitiveMagicNumbers(MutableNode node);
    }

    public interface IAppSafetyLayer
    {
        bool IsLockedByActiveProcess(MutableNode node);
        bool IsProtectedGameAsset(MutableNode node, IEnumerable<string> knownGameDirs);
        bool WasAccessedInLastHours(MutableNode node, int hours = 24);
    }

    public class DryRunResult
    {
        public long TotalSpaceFreed { get; set; }
        public int TotalRebuildCostSeconds { get; set; }
        public List<MutableNode> SafeNodes { get; set; } = new();
        public List<MutableNode> BlockedNodes { get; set; } = new();
        public Dictionary<string, long> ImpactPerApp { get; set; } = new();
        public List<string> DetectedRisks { get; set; } = new();
    }

    public class ExecutionResult
    {
        public bool Success { get; set; }
        public long SpaceCleaned { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public string TransactionId { get; set; } = string.Empty;
    }

    public interface ITransactionalCleaner
    {
        Task<DryRunResult> SimulateAsync(IEnumerable<MutableNode> nodes, IntelligentProfileType profile);
        Task<ExecutionResult> ExecuteAtomicAsync(IEnumerable<MutableNode> validatedNodes);
        Task RollbackLastTransactionAsync(string transactionId);
    }

    public interface IQuarantineSystem
    {
        Task<bool> MoveToQuarantineAsync(MutableNode node);
        Task<bool> RestoreFromQuarantineAsync(string originalPath);
        Task PurgeExpiredItemsAsync();
    }
}
