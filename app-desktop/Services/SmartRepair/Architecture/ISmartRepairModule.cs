using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.SmartRepair.Architecture
{
    public interface ISmartRepairModule
    {
        string ModuleId { get; }
        string Category { get; }
        
        string Name { get; }
        string Description { get; }
        
        ModuleCapabilities Capabilities { get; }
        IReadOnlyList<string> Dependencies { get; } // IDs of modules that must run before this one

        Task<ModuleScanResult> ScanAsync(IProgress<RepairProgress> progress, CancellationToken ct);
        Task<ModuleSimulationResult> SimulateAsync(ModuleScanResult scanResult, CancellationToken ct);
        Task<ModuleExecutionResult> ExecuteAsync(ModuleSimulationResult simResult, IProgress<RepairProgress> progress, CancellationToken ct);
        Task<ModuleRollbackResult> RollbackAsync(ModuleExecutionResult execResult, IProgress<RepairProgress> progress, CancellationToken ct);
    }
}
