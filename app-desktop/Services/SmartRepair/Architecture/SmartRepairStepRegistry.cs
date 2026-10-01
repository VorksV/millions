using System;
using System.Collections.Generic;
using System.Linq;

namespace VoltrisOptimizer.Services.SmartRepair.Architecture
{
    public class ModuleRegistration
    {
        public string ModuleId { get; init; } = string.Empty;
        public string NameLocalizationKey { get; init; } = string.Empty;
        public string DescriptionLocalizationKey { get; init; } = string.Empty;
        public int ExecutionOrder { get; init; }
        public List<string> Dependencies { get; init; } = new();
        public Func<ILoggingService, ISmartRepairEventBus, IExclusionService, string, ISmartRepairModule>? Factory { get; init; }
        public bool IsLegacyInjectionPoint { get; init; }
    }

    public class SmartRepairStepRegistry
    {
        private static readonly Lazy<SmartRepairStepRegistry> _instance = new(() => new SmartRepairStepRegistry());
        private readonly List<ModuleRegistration> _dedicatedModules = new();

        public static SmartRepairStepRegistry Instance => _instance.Value;

        public IReadOnlyList<ModuleRegistration> DedicatedModules => _dedicatedModules.AsReadOnly();

        private SmartRepairStepRegistry()
        {
            RegisterAllDedicated();
        }

        private void RegisterAllDedicated()
        {
            int order = 0;

            Register("SystemRestore", "SmartRepair_Module_Restore_Name", "SmartRepair_Module_Restore_Desc", order++, (l, e, x, c) => new Modules.SystemRestoreModule(l, e, c));
            Register("Privacy", "SmartRepair_Module_Privacy_Name", "SmartRepair_Module_Privacy_Desc", order++, (l, e, x, c) => new Modules.PrivacyModule(l, e, x, c));
            Register("TemporaryFiles", "SmartRepair_Module_TempFiles_Name", "SmartRepair_Module_TempFiles_Desc", order++, (l, e, x, c) => new Modules.TemporaryFilesModule(l, e, x, c));
            Register("SpecificJunk", "SmartRepair_Module_SpecificJunk_Name", "SmartRepair_Module_SpecificJunk_Desc", order++, (l, e, x, c) => new Modules.SpecificJunkModule(l, e, c));
            Register("BrowserCleaner", "SmartRepair_Module_Browser_Name", "SmartRepair_Module_Browser_Desc", order++, (l, e, x, c) => new Modules.BrowserCleanerAdapterModule(l, e, c));
            Register("Registry", "SmartRepair_Module_Registry_Name", "SmartRepair_Module_Registry_Desc", order++, (l, e, x, c) => new Modules.RegistryModule(l, e, x, c));

            // Legacy steps are injected by the ViewModel at this position (order is reserved)

            _dedicatedModules.Add(new ModuleRegistration
            {
                ModuleId = "__LEGACY_STEPS__",
                NameLocalizationKey = string.Empty,
                DescriptionLocalizationKey = string.Empty,
                ExecutionOrder = order++,
                IsLegacyInjectionPoint = true
            });

            Register("MemoryOptimization", "SmartRepair_Module_Memory_Name", "SmartRepair_Module_Memory_Desc", order++, (l, e, x, c) => new Modules.MemoryOptimizationModule(l, e, c));
            Register("DiskOptimization", "SmartRepair_Module_Disk_Name", "SmartRepair_Module_Disk_Desc", order++, (l, e, x, c) => new Modules.DiskOptimizationModule(l, e, c));
            Register("ChkDsk", "SmartRepair_Module_ChkDsk_Name", "SmartRepair_Module_ChkDsk_Desc", order++, (l, e, x, c) => new Modules.ChkDskModule(l, e, c));
            Register("DnsOptimizer", "SmartRepair_Module_Dns_Name", "SmartRepair_Module_Dns_Desc", order++, (l, e, x, c) => new Modules.DnsOptimizerModule(l, e, c));
            Register("UltraClean", "CleanupUltraTitle", "CleanupUltraDesc", order++, (l, e, x, c) => new Modules.UltraCleanAdapterModule(l, e, c));

            Register("StandbyListClean", "SmartRepair_Module_StandbyList_Name", "SmartRepair_Module_StandbyList_Desc", order++, (l, e, x, c) => new Modules.StandbyListCleanModule(l, e, c));
            Register("PageCombining", "SmartRepair_Module_PageCombine_Name", "SmartRepair_Module_PageCombine_Desc", order++, (l, e, x, c) => new Modules.PageCombiningModule(l, e, c));
            Register("PagefileOptimizer", "SmartRepair_Module_Pagefile_Name", "SmartRepair_Module_Pagefile_Desc", order++, (l, e, x, c) => new Modules.PagefileOptimizerModule(l, e, c));
            Register("SysMainConfig", "SmartRepair_Module_SysMain_Name", "SmartRepair_Module_SysMain_Desc", order++, (l, e, x, c) => new Modules.SysMainConfigModule(l, e, c));

            Register("PriorityScheduler", "SmartRepair_Module_Priority_Name", "SmartRepair_Module_Priority_Desc", order++, (l, e, x, c) => new Modules.PrioritySchedulerModule(l, e, c));
            Register("HpetOptimizer", "SmartRepair_Module_Hpet_Name", "SmartRepair_Module_Hpet_Desc", order++, (l, e, x, c) => new Modules.HpetModule(l, e, c));
            Register("DpcLatency", "SmartRepair_Module_DpcLatency_Name", "SmartRepair_Module_DpcLatency_Desc", order++, (l, e, x, c) => new Modules.DpcLatencyModule(l, e, c));

            Register("WindowsOldRemoval", "SmartRepair_Module_WinOld_Name", "SmartRepair_Module_WinOld_Desc", order++, (l, e, x, c) => new Modules.WindowsOldRemovalModule(l, e, c));
            Register("NtfsOptimizer", "SmartRepair_Module_Ntfs_Name", "SmartRepair_Module_Ntfs_Desc", order++, (l, e, x, c) => new Modules.NtfsOptimizerModule(l, e, c));
            Register("NtfsMetaFileDefrag", "SmartRepair_Module_NtfsMeta_Name", "SmartRepair_Module_NtfsMeta_Desc", order++, (l, e, x, c) => new Modules.NtfsMetaFileDefragModule(l, e, c));

            Register("InterruptModeration", "SmartRepair_Module_Interrupt_Name", "SmartRepair_Module_Interrupt_Desc", order++, (l, e, x, c) => new Modules.InterruptModerationModule(l, e, c));
            Register("MtuOptimizer", "SmartRepair_Module_Mtu_Name", "SmartRepair_Module_Mtu_Desc", order++, (l, e, x, c) => new Modules.MtuOptimizerModule(l, e, c));
            Register("QoSBandwidth", "SmartRepair_Module_QoS_Name", "SmartRepair_Module_QoS_Desc", order++, (l, e, x, c) => new Modules.QoSBandwidthModule(l, e, c));
            Register("RemoteDiffCompression", "SmartRepair_Module_RemoteDiff_Name", "SmartRepair_Module_RemoteDiff_Desc", order++, (l, e, x, c) => new Modules.RemoteDiffCompressionModule(l, e, c));
            Register("NetPowerSaving", "SmartRepair_Module_NetPower_Name", "SmartRepair_Module_NetPower_Desc", order++, (l, e, x, c) => new Modules.NetPowerSavingModule(l, e, c));
            Register("ArpNetBiosReset", "SmartRepair_Module_ArpReset_Name", "SmartRepair_Module_ArpReset_Desc", order++, (l, e, x, c) => new Modules.ArpNetBiosResetModule(l, e, c));

            Register("CortanaBlocker", "SmartRepair_Module_Cortana_Name", "SmartRepair_Module_Cortana_Desc", order++, (l, e, x, c) => new Modules.CortanaBlockerModule(l, e, c));
            Register("TelemetryBlocker", "SmartRepair_Module_Telemetry_Name", "SmartRepair_Module_Telemetry_Desc", order++, (l, e, x, c) => new Modules.TelemetryBlockerModule(l, e, c));
            Register("WindowsSearchOptimizer", "SmartRepair_Module_WSearch_Name", "SmartRepair_Module_WSearch_Desc", order++, (l, e, x, c) => new Modules.WindowsSearchOptimizerModule(l, e, c));
            Register("BackgroundApps", "SmartRepair_Module_BackgroundApps_Name", "SmartRepair_Module_BackgroundApps_Desc", order++, (l, e, x, c) => new Modules.BackgroundAppsModule(l, e, c));
            Register("WindowsFeatures", "SmartRepair_Module_WinFeatures_Name", "SmartRepair_Module_WinFeatures_Desc", order++, (l, e, x, c) => new Modules.WindowsFeaturesModule(l, e, c));
            Register("VisualEffects", "SmartRepair_Module_VisualFx_Name", "SmartRepair_Module_VisualFx_Desc", order++, (l, e, x, c) => new Modules.VisualEffectsModule(l, e, c));

            Register("GhostDevices", "SmartRepair_Module_GhostDevices_Name", "SmartRepair_Module_GhostDevices_Desc", order++, (l, e, x, c) => new Modules.GhostDevicesModule(l, e, c));
            Register("GpuOptimizer", "SmartRepair_Module_Gpu_Name", "SmartRepair_Module_Gpu_Desc", order++, (l, e, x, c) => new Modules.GpuOptimizerModule(l, e, c));

            Register("WeeklySchedule", "SmartRepair_Module_WeeklySchedule_Name", "SmartRepair_Module_WeeklySchedule_Desc", order++, (l, e, x, c) => new Modules.WeeklyScheduleModule(l, e, c));

            Register("BloatwareRemoval", "SmartRepair_Module_Bloatware_Name", "SmartRepair_Module_Bloatware_Desc", order++, (l, e, x, c) => new Modules.BloatwareRemovalModule(l, e, c));
        }

        private void Register(string moduleId, string nameKey, string descKey, int order,
            Func<ILoggingService, ISmartRepairEventBus, IExclusionService, string, ISmartRepairModule> factory)
        {
            if (_dedicatedModules.Any(m => m.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Module {moduleId} já registrado no registry.");

            _dedicatedModules.Add(new ModuleRegistration
            {
                ModuleId = moduleId,
                NameLocalizationKey = nameKey,
                DescriptionLocalizationKey = descKey,
                ExecutionOrder = order,
                Factory = factory
            });
        }

        public IReadOnlyList<ModuleRegistration> GetOrderedDedicated()
        {
            return _dedicatedModules.OrderBy(m => m.ExecutionOrder).ToList().AsReadOnly();
        }
    }
}
