using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core.VMRG.Interfaces;
using VoltrisOptimizer.Core.VMRG.Models;

namespace VoltrisOptimizer.Services.VMRG.Detection.Providers
{
    [Obsolete("Use UnifiedVmProvider que consolida todos os hypervisors com cache de processos.")]
    public class QemuProvider : IVmDetectionProvider
    {
        public string ProviderName => "QEMU";
        public VmHypervisor Hypervisor => VmHypervisor.Qemu;

        private static readonly string[] VmProcesses = { "qemu", "qemu-system-x86_64", "qemu-system-i386" };
        private static readonly string[] HostProcesses = { "qemu-ga" };

        public Task<List<VmInfo>> DetectAsync(CancellationToken ct)
        {
            var result = new List<VmInfo>();
            var allProcesses = Process.GetProcesses();

            foreach (var vmProc in allProcesses.Where(p => VmProcesses.Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase)))
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    var info = new VmInfo
                    {
                        ProcessId = vmProc.Id,
                        ProcessName = vmProc.ProcessName,
                        WindowTitle = vmProc.MainWindowTitle,
                        Hypervisor = VmHypervisor.Qemu,
                        IsHostProcess = false,
                        FirstDetectedAt = DateTime.UtcNow,
                        LastActiveAt = DateTime.UtcNow
                    };
                    VirtualBoxProvider.ReadProcessMetrics(vmProc, info);
                    result.Add(info);
                }
                catch { System.Diagnostics.Debug.WriteLine("[QemuProvider] Erro ao processar processo VM"); }
            }

            foreach (var hostProc in allProcesses.Where(p => HostProcesses.Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase)))
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    result.Add(new VmInfo
                    {
                        ProcessId = hostProc.Id,
                        ProcessName = hostProc.ProcessName,
                        Hypervisor = VmHypervisor.Qemu,
                        IsHostProcess = true,
                        FirstDetectedAt = DateTime.UtcNow,
                        LastActiveAt = DateTime.UtcNow
                    });
                }
                catch { System.Diagnostics.Debug.WriteLine("[QemuProvider] Erro ao processar host process"); }
            }

            return Task.FromResult(result);
        }
    }
}
