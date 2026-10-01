using System;
using System.Collections.Generic;
using VoltrisOptimizer.Services.Gamer.Adaptive;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer.Intelligence
{
    public class ProfileOptimizationMap
    {
        private readonly ILoggingService _logger;
        private readonly Dictionary<string, OptimizationPreset> _presetCache = new();

        public ProfileOptimizationMap(ILoggingService logger)
        {
            _logger = logger;
            _logger.LogEntry(nameof(ProfileOptimizationMap));
            _logger.LogExit(nameof(ProfileOptimizationMap));
        }

        public class OptimizationPreset
        {
            public int EppValue { get; set; }
            public int SystemResponsiveness { get; set; }
            public int TimerResolution100ns { get; set; }
            public bool EnableGamingMode { get; set; }
            public string PowerPlanPreference { get; set; } = "Balanced";
            public string CpuPriorityClass { get; set; } = "Normal";
            public bool TrimMemory { get; set; }
            public bool CloseBackgroundApps { get; set; }
            public bool OptimizeNetwork { get; set; }
            public bool ApplyGpuProfile { get; set; }
            public bool EnableAntiStutter { get; set; }
            public int GpuPowerPreference { get; set; } = 0;
            public string AudioOptimizationLevel { get; set; } = "Medium";
            public bool EnableMsiMode { get; set; }
            public int ForegroundQuantum { get; set; } = 6;
            public bool DisableHardwareAcceleration { get; set; }
            public bool AggressiveMemoryTrim { get; set; }
            public bool HighPrecisionTimerOnly { get; set; }
            public bool OptimizeScheduledTasks { get; set; }
            public bool DisableVisualEffects { get; set; }
            public bool DisableWallpaper { get; set; }
            public bool OptimizeServices { get; set; }
            public string ThermalThrottleBehavior { get; set; } = "ReduceEPP";
            public bool EnableProcessBlacklist { get; set; }
            public bool UseEcoQoS { get; set; }
        }

        public OptimizationPreset GetPreset(IntelligentProfileType profile, MachineProfile machineProfile, bool isNotebook)
        {
            _logger.LogEntry(nameof(GetPreset), ("profile", profile), ("machineProfile", machineProfile), ("isNotebook", isNotebook));
            var cacheKey = $"{profile}_{machineProfile}_{isNotebook}";
            if (_presetCache.TryGetValue(cacheKey, out var cached))
            {
                _logger.LogExit(nameof(GetPreset), "cached");
                return cached;
            }

            var preset = BuildPreset(profile, machineProfile, isNotebook);
            _presetCache[cacheKey] = preset;

            _logger.LogDebug($"[ProfileMap] Preset gerado: {cacheKey} -> EPP={preset.EppValue}, GamingMode={preset.EnableGamingMode}, Timer={preset.TimerResolution100ns}", "ProfileMap");
            _logger.LogExit(nameof(GetPreset));
            return preset;
        }

        private OptimizationPreset BuildPreset(IntelligentProfileType profile, MachineProfile machine, bool isNotebook)
        {
            _logger.LogEntry(nameof(BuildPreset), ("profile", profile), ("machine", machine), ("isNotebook", isNotebook));
            var p = new OptimizationPreset();
            bool weakMachine = machine == MachineProfile.EntryLevel;
            bool midMachine = machine == MachineProfile.MidRange;
            bool strongMachine = machine == MachineProfile.HighEnd || machine == MachineProfile.GamingMachine;

            switch (profile)
            {
                case IntelligentProfileType.GamerCompetitive:
                    p.EppValue = strongMachine ? 0 : (midMachine ? 10 : 15);
                    p.SystemResponsiveness = strongMachine ? 8 : 10;
                    p.TimerResolution100ns = 5000;
                    p.EnableGamingMode = true;
                    p.PowerPlanPreference = strongMachine ? "UltimatePerformance" : "HighPerformance";
                    p.CpuPriorityClass = "High";
                    p.TrimMemory = true;
                    p.CloseBackgroundApps = true;
                    p.OptimizeNetwork = true;
                    p.ApplyGpuProfile = true;
                    p.EnableAntiStutter = true;
                    p.GpuPowerPreference = 1;
                    p.AudioOptimizationLevel = "High";
                    p.EnableMsiMode = strongMachine;
                    p.ForegroundQuantum = weakMachine ? 6 : 4;
                    p.AggressiveMemoryTrim = strongMachine;
                    p.OptimizeScheduledTasks = true;
                    p.DisableVisualEffects = true;
                    p.DisableWallpaper = true;
                    p.OptimizeServices = strongMachine;
                    p.ThermalThrottleBehavior = "ReduceEPP";
                    p.EnableProcessBlacklist = true;
                    break;

                case IntelligentProfileType.GamerSinglePlayer:
                    p.EppValue = strongMachine ? 5 : (midMachine ? 15 : 20);
                    p.SystemResponsiveness = 10;
                    p.TimerResolution100ns = 5000;
                    p.EnableGamingMode = true;
                    p.PowerPlanPreference = "HighPerformance";
                    p.CpuPriorityClass = "AboveNormal";
                    p.TrimMemory = true;
                    p.CloseBackgroundApps = true;
                    p.OptimizeNetwork = true;
                    p.ApplyGpuProfile = true;
                    p.EnableAntiStutter = true;
                    p.GpuPowerPreference = 1;
                    p.AudioOptimizationLevel = "High";
                    p.ForegroundQuantum = weakMachine ? 6 : 5;
                    p.AggressiveMemoryTrim = false;
                    p.OptimizeScheduledTasks = true;
                    p.DisableVisualEffects = true;
                    p.DisableWallpaper = true;
                    break;

                case IntelligentProfileType.GamerSimulation:
                    p.EppValue = strongMachine ? 10 : (midMachine ? 20 : 25);
                    p.SystemResponsiveness = 10;
                    p.TimerResolution100ns = 5000;
                    p.EnableGamingMode = true;
                    p.PowerPlanPreference = "HighPerformance";
                    p.CpuPriorityClass = "AboveNormal";
                    p.TrimMemory = true;
                    p.CloseBackgroundApps = true;
                    p.OptimizeNetwork = false;
                    p.ApplyGpuProfile = true;
                    p.EnableAntiStutter = true;
                    p.GpuPowerPreference = 1;
                    p.AudioOptimizationLevel = "Medium";
                    p.OptimizeScheduledTasks = true;
                    p.DisableVisualEffects = false;
                    break;

                case IntelligentProfileType.GamerMMO:
                    p.EppValue = strongMachine ? 5 : (midMachine ? 15 : 20);
                    p.SystemResponsiveness = 10;
                    p.TimerResolution100ns = 5000;
                    p.EnableGamingMode = true;
                    p.PowerPlanPreference = "HighPerformance";
                    p.CpuPriorityClass = "AboveNormal";
                    p.TrimMemory = true;
                    p.CloseBackgroundApps = true;
                    p.OptimizeNetwork = true;
                    p.ApplyGpuProfile = true;
                    p.EnableAntiStutter = true;
                    p.AudioOptimizationLevel = "Medium";
                    p.AggressiveMemoryTrim = weakMachine ? false : true;
                    p.OptimizeScheduledTasks = true;
                    p.DisableVisualEffects = weakMachine;
                    break;

                case IntelligentProfileType.GamerStrategy:
                    p.EppValue = strongMachine ? 15 : (midMachine ? 25 : 30);
                    p.SystemResponsiveness = 12;
                    p.TimerResolution100ns = 5000;
                    p.EnableGamingMode = true;
                    p.PowerPlanPreference = "HighPerformance";
                    p.CpuPriorityClass = "AboveNormal";
                    p.TrimMemory = true;
                    p.CloseBackgroundApps = true;
                    p.OptimizeNetwork = false;
                    p.ApplyGpuProfile = false;
                    p.EnableAntiStutter = true;
                    p.AudioOptimizationLevel = "Low";
                    p.OptimizeScheduledTasks = true;
                    break;

                case IntelligentProfileType.WorkOffice:
                    p.EppValue = isNotebook ? 60 : 50;
                    p.SystemResponsiveness = 20;
                    p.TimerResolution100ns = 10000;
                    p.EnableGamingMode = false;
                    p.PowerPlanPreference = "Balanced";
                    p.CpuPriorityClass = "Normal";
                    p.TrimMemory = false;
                    p.CloseBackgroundApps = false;
                    p.OptimizeNetwork = false;
                    p.ApplyGpuProfile = false;
                    p.EnableAntiStutter = false;
                    p.AudioOptimizationLevel = "Low";
                    p.OptimizeScheduledTasks = false;
                    p.DisableVisualEffects = false;
                    break;

                case IntelligentProfileType.CreativeVideoEditing:
                    p.EppValue = isNotebook ? 35 : 25;
                    p.SystemResponsiveness = 12;
                    p.TimerResolution100ns = 5000;
                    p.EnableGamingMode = false;
                    p.PowerPlanPreference = "HighPerformance";
                    p.CpuPriorityClass = "AboveNormal";
                    p.TrimMemory = true;
                    p.CloseBackgroundApps = true;
                    p.OptimizeNetwork = false;
                    p.ApplyGpuProfile = true;
                    p.EnableAntiStutter = false;
                    p.AudioOptimizationLevel = "High";
                    break;

                case IntelligentProfileType.DeveloperProgramming:
                    p.EppValue = isNotebook ? 50 : 40;
                    p.SystemResponsiveness = 20;
                    p.TimerResolution100ns = 10000;
                    p.EnableGamingMode = false;
                    p.PowerPlanPreference = "Balanced";
                    p.CpuPriorityClass = "Normal";
                    p.TrimMemory = true;
                    p.CloseBackgroundApps = false;
                    p.OptimizeNetwork = false;
                    p.ApplyGpuProfile = false;
                    p.EnableAntiStutter = false;
                    p.AudioOptimizationLevel = "Low";
                    break;

                case IntelligentProfileType.GeneralBalanced:
                    p.EppValue = isNotebook ? 55 : 45;
                    p.SystemResponsiveness = 20;
                    p.TimerResolution100ns = 10000;
                    p.EnableGamingMode = false;
                    p.PowerPlanPreference = "Balanced";
                    p.CpuPriorityClass = "Normal";
                    p.TrimMemory = false;
                    p.CloseBackgroundApps = false;
                    p.OptimizeNetwork = false;
                    p.ApplyGpuProfile = false;
                    p.EnableAntiStutter = false;
                    p.AudioOptimizationLevel = "Low";
                    break;

                case IntelligentProfileType.EnterpriseSecure:
                    p.EppValue = 70;
                    p.SystemResponsiveness = 30;
                    p.TimerResolution100ns = 156000;
                    p.EnableGamingMode = false;
                    p.PowerPlanPreference = "Balanced";
                    p.CpuPriorityClass = "Normal";
                    p.TrimMemory = false;
                    p.CloseBackgroundApps = false;
                    p.OptimizeNetwork = false;
                    p.ApplyGpuProfile = false;
                    p.EnableAntiStutter = false;
                    p.AudioOptimizationLevel = "Low";
                    p.OptimizeScheduledTasks = false;
                    p.DisableVisualEffects = false;
                    break;

                default:
                    p.EppValue = 50;
                    p.SystemResponsiveness = 20;
                    p.TimerResolution100ns = 10000;
                    p.EnableGamingMode = false;
                    p.PowerPlanPreference = "Balanced";
                    break;
            }

            if (isNotebook && strongMachine)
            {
                p.PowerPlanPreference = "Balanced";
                p.GpuPowerPreference = 0;
            }

            _logger.LogExit(nameof(BuildPreset));
            return p;
        }
    }
}
