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
    public class AndroidEmulatorProvider : IVmDetectionProvider
    {
        public string ProviderName => "Android Emulator";
        public VmHypervisor Hypervisor => VmHypervisor.AndroidEmulator;

        // Android Studio Emulator + all major Android emulators
        private static readonly Dictionary<string, VmHypervisor> EmulatorProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            ["qemu-system-x86_64"] = VmHypervisor.AndroidEmulator,
            ["emulator"] = VmHypervisor.AndroidEmulator,
            ["emulator-x86"] = VmHypervisor.AndroidEmulator,
            ["HD-Player"] = VmHypervisor.BlueStacks,
            ["BlueStacks"] = VmHypervisor.BlueStacks,
            ["BstkSVC"] = VmHypervisor.BlueStacks,
            ["ldplayer"] = VmHypervisor.LDPlayer,
            ["dnplayer"] = VmHypervisor.LDPlayer,
            ["ldconsole"] = VmHypervisor.LDPlayer,
            ["Nox"] = VmHypervisor.NoxPlayer,
            ["NoxVMHandle"] = VmHypervisor.NoxPlayer,
            ["MEmu"] = VmHypervisor.MEmu,
            ["MEmuHeadless"] = VmHypervisor.MEmu,
            ["MuMuPlayer"] = VmHypervisor.MuMuPlayer,
            ["MuMuVMM"] = VmHypervisor.MuMuPlayer,
            ["genymotion"] = VmHypervisor.Genymotion,
            ["player"] = VmHypervisor.Genymotion
        };

        private static readonly string[] HostServiceProcesses =
        {
            "adb", "BstkSVC", "NoxSVC", "MEmuSVC", "MuMuSVC"
        };

        public Task<List<VmInfo>> DetectAsync(CancellationToken ct)
        {
            var result = new List<VmInfo>();
            var allProcesses = Process.GetProcesses();

            foreach (var proc in allProcesses)
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    if (EmulatorProcesses.TryGetValue(proc.ProcessName, out var hypervisor))
                    {
                        var info = new VmInfo
                        {
                            ProcessId = proc.Id,
                            ProcessName = proc.ProcessName,
                            WindowTitle = proc.MainWindowTitle,
                            Hypervisor = hypervisor,
                            IsHostProcess = false,
                            FirstDetectedAt = DateTime.UtcNow,
                            LastActiveAt = DateTime.UtcNow
                        };
                        VirtualBoxProvider.ReadProcessMetrics(proc, info);
                        result.Add(info);
                    }
                    else if (HostServiceProcesses.Contains(proc.ProcessName, StringComparer.OrdinalIgnoreCase))
                    {
                        result.Add(new VmInfo
                        {
                            ProcessId = proc.Id,
                            ProcessName = proc.ProcessName,
                            Hypervisor = VmHypervisor.AndroidEmulator,
                            IsHostProcess = true,
                            FirstDetectedAt = DateTime.UtcNow,
                            LastActiveAt = DateTime.UtcNow
                        });
                    }
                }
                catch { System.Diagnostics.Debug.WriteLine("[AndroidEmulatorProvider] Erro ao processar processo"); }
            }

            return Task.FromResult(result);
        }
    }
}
