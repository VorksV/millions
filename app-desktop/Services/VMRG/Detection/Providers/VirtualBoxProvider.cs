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
    public class VirtualBoxProvider : IVmDetectionProvider
    {
        public string ProviderName => "VirtualBox";
        public VmHypervisor Hypervisor => VmHypervisor.VirtualBox;

        private static readonly string[] VmProcesses = { "VirtualBoxVM", "VBoxHeadless" };
        private static readonly string[] HostProcesses = { "VirtualBox", "VBoxSVC", "VBoxSDS", "VBoxManage", "VBoxNetDHCP", "VBoxNetNAT" };

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
                        Hypervisor = VmHypervisor.VirtualBox,
                        IsHostProcess = false,
                        FirstDetectedAt = DateTime.UtcNow,
                        LastActiveAt = DateTime.UtcNow
                    };
                    TryReadProcessMetrics(vmProc, info);
                    result.Add(info);
                }
                catch { System.Diagnostics.Debug.WriteLine("[VirtualBoxProvider] Erro ao processar processo VM"); }
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
                        Hypervisor = VmHypervisor.VirtualBox,
                        IsHostProcess = true,
                        FirstDetectedAt = DateTime.UtcNow,
                        LastActiveAt = DateTime.UtcNow
                    });
                }
                catch { System.Diagnostics.Debug.WriteLine("[VirtualBoxProvider] Erro ao processar host process"); }
            }

            return Task.FromResult(result);
        }

        protected static void TryReadProcessMetrics(Process proc, VmInfo info)
        {
            try
            {
                using var sample = Process.GetProcessById(proc.Id);
                info.WorkingSetMb = sample.WorkingSet64 / 1024 / 1024;
                info.OriginalPriority = VmInfo.FromProcessPriorityClass(sample.PriorityClass);
                info.CurrentPriority = info.OriginalPriority;
                try
                {
                    info.AffinityMask = (long)sample.ProcessorAffinity;
                    info.OriginalAffinityMask = info.AffinityMask;
                }
                catch { System.Diagnostics.Debug.WriteLine("[VirtualBoxProvider] Erro ao ler afinidade"); }
            }
            catch { System.Diagnostics.Debug.WriteLine("[VirtualBoxProvider] Erro ao ler métricas do processo"); }
        }

        public static void ReadProcessMetrics(Process proc, VmInfo info)
        {
            TryReadProcessMetrics(proc, info);
        }
    }
}
