using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core.VMRG.Models;

namespace VoltrisOptimizer.Core.VMRG.Interfaces
{
    public interface IVmMonitorService
    {
        IReadOnlyList<VmInfo> ActiveVms { get; }
        IReadOnlyList<VmInfo> HostProcesses { get; }
        bool AnyVmDetected { get; }
        event EventHandler<VmInfo>? VmDetected;
        event EventHandler<VmInfo>? VmStateChanged;
        event EventHandler<VmInfo>? VmRemoved;
        Task RefreshAsync(CancellationToken ct);
    }
}
