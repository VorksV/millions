using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core.VMRG.Interfaces;
using VoltrisOptimizer.Core.VMRG.Models;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.VMRG.Detection.Providers
{
    public class UnifiedVmProvider : IVmDetectionProvider
    {
        public string ProviderName => "UnifiedVM";
        public VmHypervisor Hypervisor => VmHypervisor.Unknown;

        private static readonly Dictionary<string, (VmHypervisor hypervisor, bool isHost)> ProcessLookup = new(StringComparer.OrdinalIgnoreCase)
        {
            ["VirtualBoxVM"] = (VmHypervisor.VirtualBox, false),
            ["VBoxHeadless"] = (VmHypervisor.VirtualBox, false),
            ["VirtualBox"] = (VmHypervisor.VirtualBox, true),
            ["VBoxSVC"] = (VmHypervisor.VirtualBox, true),
            ["VBoxManage"] = (VmHypervisor.VirtualBox, true),
            ["VBoxNetDHCP"] = (VmHypervisor.VirtualBox, true),
            ["VBoxNetNAT"] = (VmHypervisor.VirtualBox, true),
            ["vmware-vmx"] = (VmHypervisor.VMwareWorkstation, false),
            ["vmware"] = (VmHypervisor.VMwareWorkstation, true),
            ["vmware-tray"] = (VmHypervisor.VMwareWorkstation, true),
            ["vmware-usbarbitrator64"] = (VmHypervisor.VMwareWorkstation, true),
            ["vmware-authd"] = (VmHypervisor.VMwareWorkstation, true),
            ["vmware-hostd"] = (VmHypervisor.VMwareWorkstation, true),
            ["vmwp"] = (VmHypervisor.HyperV, false),
            ["vmms"] = (VmHypervisor.HyperV, true),
            ["vmcompute"] = (VmHypervisor.HyperV, true),
            ["qemu"] = (VmHypervisor.Qemu, false),
            ["qemu-system-x86_64"] = (VmHypervisor.Qemu, false),
            ["qemu-system-i386"] = (VmHypervisor.Qemu, false),
            ["qemu-ga"] = (VmHypervisor.Qemu, true),
            ["wsl"] = (VmHypervisor.Wsl2, false),
            ["wslservice"] = (VmHypervisor.Wsl2, false),
            ["wslhost"] = (VmHypervisor.Wsl2, false),
            ["emulator"] = (VmHypervisor.AndroidEmulator, false),
            ["emulator-x86"] = (VmHypervisor.AndroidEmulator, false),
            ["HD-Player"] = (VmHypervisor.BlueStacks, false),
            ["BlueStacks"] = (VmHypervisor.BlueStacks, false),
            ["BstkSVC"] = (VmHypervisor.BlueStacks, false),
            ["ldplayer"] = (VmHypervisor.LDPlayer, false),
            ["dnplayer"] = (VmHypervisor.LDPlayer, false),
            ["ldconsole"] = (VmHypervisor.LDPlayer, false),
            ["Nox"] = (VmHypervisor.NoxPlayer, false),
            ["NoxVMHandle"] = (VmHypervisor.NoxPlayer, false),
            ["MEmu"] = (VmHypervisor.MEmu, false),
            ["MEmuHeadless"] = (VmHypervisor.MEmu, false),
            ["MuMuPlayer"] = (VmHypervisor.MuMuPlayer, false),
            ["MuMuVMM"] = (VmHypervisor.MuMuPlayer, false),
            ["genymotion"] = (VmHypervisor.Genymotion, false),
            ["player"] = (VmHypervisor.Genymotion, false),
            ["adb"] = (VmHypervisor.AndroidEmulator, true),
            ["NoxSVC"] = (VmHypervisor.NoxPlayer, true),
            ["MEmuSVC"] = (VmHypervisor.MEmu, true),
            ["MuMuSVC"] = (VmHypervisor.MuMuPlayer, true)};

        private readonly ProcessCacheService _processCache;
        private readonly ILoggingService _logger;

        public UnifiedVmProvider(ProcessCacheService processCache, ILoggingService logger)
        {
            _processCache = processCache ?? throw new ArgumentNullException(nameof(processCache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task<List<VmInfo>> DetectAsync(CancellationToken ct)
        {
            var result = new List<VmInfo>();

            try
            {
                var cachedProcesses = _processCache.GetCachedProcessInfos();

                foreach (var cached in cachedProcesses)
                {
                    if (ct.IsCancellationRequested) break;

                    if (ProcessLookup.TryGetValue(cached.ProcessName, out var entry))
                    {
                        var info = new VmInfo
                        {
                            ProcessId = cached.Id,
                            ProcessName = cached.ProcessName,
                            Hypervisor = entry.hypervisor,
                            IsHostProcess = entry.isHost,
                            FirstDetectedAt = DateTime.UtcNow,
                            LastActiveAt = DateTime.UtcNow,
                            WorkingSetMb = cached.WorkingSet64 / 1024 / 1024,
                            OriginalPriority = VmInfo.FromProcessPriorityClass(
                                cached.BasePriority >= 13 ? ProcessPriorityClass.High :
                                cached.BasePriority >= 10 ? ProcessPriorityClass.AboveNormal :
                                cached.BasePriority >= 8 ? ProcessPriorityClass.Normal :
                                cached.BasePriority >= 6 ? ProcessPriorityClass.BelowNormal :
                                ProcessPriorityClass.Idle),
                            CurrentPriority = VmInfo.FromProcessPriorityClass(
                                cached.BasePriority >= 13 ? ProcessPriorityClass.High :
                                cached.BasePriority >= 10 ? ProcessPriorityClass.AboveNormal :
                                cached.BasePriority >= 8 ? ProcessPriorityClass.Normal :
                                cached.BasePriority >= 6 ? ProcessPriorityClass.BelowNormal :
                                ProcessPriorityClass.Idle)};

                        if (!entry.isHost)
                        {
                            var proc = cached.Process;
                            if (proc != null)
                            {
                                info.WindowTitle = proc.MainWindowTitle;
                                try { info.AffinityMask = (long)proc.ProcessorAffinity; info.OriginalAffinityMask = info.AffinityMask; } catch (Exception exAff) { _logger?.LogWarning($"[UnifiedVmProvider] Erro ao ler afinidade: {exAff.Message}"); }
                            }
                        }

                        result.Add(info);
                    }
                }

                _logger.LogDebug($"[UnifiedVmProvider] Detectados {result.Count} processos VM (cache)");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[UnifiedVmProvider] Erro na detecção: {ex.Message}", ex);
            }

            return Task.FromResult(result);
        }
    }
}
