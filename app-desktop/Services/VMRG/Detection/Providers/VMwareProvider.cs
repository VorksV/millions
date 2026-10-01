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
    public class VMwareProvider : IVmDetectionProvider
    {
        public string ProviderName => "VMware";
        public VmHypervisor Hypervisor => VmHypervisor.VMwareWorkstation;

        private static readonly string[] VmProcesses = { "vmware-vmx" };
        private static readonly string[] HostProcesses = { "vmware", "vmware-tray", "vmware-usbarbitrator64", "vmware-authd", "vmware-hostd" };

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
                        Hypervisor = VmHypervisor.VMwareWorkstation,
                        IsHostProcess = false,
                        FirstDetectedAt = DateTime.UtcNow,
                        LastActiveAt = DateTime.UtcNow
                    };
                    VirtualBoxProvider.ReadProcessMetrics(vmProc, info);
                    result.Add(info);
                }
                catch { System.Diagnostics.Debug.WriteLine("[VMwareProvider] Erro ao processar processo VM"); }
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
                        Hypervisor = VmHypervisor.VMwareWorkstation,
                        IsHostProcess = true,
                        FirstDetectedAt = DateTime.UtcNow,
                        LastActiveAt = DateTime.UtcNow
                    });
                }
                catch { System.Diagnostics.Debug.WriteLine("[VMwareProvider] Erro ao processar host process"); }
            }

            return Task.FromResult(result);
        }
    }
}
