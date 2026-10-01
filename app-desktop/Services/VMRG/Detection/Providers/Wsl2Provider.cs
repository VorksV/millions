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
    public class Wsl2Provider : IVmDetectionProvider
    {
        public string ProviderName => "WSL2";
        public VmHypervisor Hypervisor => VmHypervisor.Wsl2;

        private static readonly string[] VmProcesses = { "wsl", "wslservice", "wslhost" };

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
                        Hypervisor = VmHypervisor.Wsl2,
                        IsHostProcess = false,
                        FirstDetectedAt = DateTime.UtcNow,
                        LastActiveAt = DateTime.UtcNow
                    };
                    VirtualBoxProvider.ReadProcessMetrics(vmProc, info);
                    result.Add(info);
                }
                catch { System.Diagnostics.Debug.WriteLine("[Wsl2Provider] Erro ao processar processo WSL"); }
            }

            return Task.FromResult(result);
        }
    }
}
