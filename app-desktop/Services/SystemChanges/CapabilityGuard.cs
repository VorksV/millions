using System;
using System.Runtime.InteropServices;
using System.Management;
using VoltrisOptimizer.Core.Hardware;

namespace VoltrisOptimizer.Services.SystemChanges
{
    public class CapabilityGuard : ICapabilityGuard
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public uint BatteryLifeTime;
            public uint BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS systemPowerStatus);

        // Lazily evaluated and cached capabilities for high performance
        private readonly Lazy<bool> _isWindows10OrHigher;
        private readonly Lazy<bool> _isWindows11;
        private readonly Lazy<bool> _isLaptop;
        private readonly Lazy<bool> _isHandheld;
        private readonly Lazy<bool> _isVirtualMachine;
        private readonly Lazy<bool> _hasEfficiencyCores;
        private readonly Lazy<bool> _supportsSmt;
        private readonly Lazy<bool> _hasMultipleGpus;

        public CapabilityGuard()
        {
            _isWindows10OrHigher = new Lazy<bool>(() => Environment.OSVersion.Version.Major >= 10);
            _isWindows11 = new Lazy<bool>(() => Environment.OSVersion.Version.Build >= 22000);
            
            _isLaptop = new Lazy<bool>(() => 
            {
                try
                {
                    if (GetSystemPowerStatus(out SYSTEM_POWER_STATUS status))
                    {
                        // 128 indicates "No system battery"
                        return status.BatteryFlag != 128 && status.BatteryFlag != 255;
                    }
                }
                catch { }
                return false;
            });

            _isHandheld = new Lazy<bool>(() =>
            {
                // Basic detection for Asus ROG Ally, Lenovo Legion Go, etc.
                try 
                {
                    using var searcher = new ManagementObjectSearcher("SELECT SystemFamily, Model FROM Win32_ComputerSystem");
                    foreach (var obj in searcher.Get())
                    {
                using var __dispose_obj = obj;
                        var model = obj["Model"]?.ToString()?.ToLowerInvariant() ?? "";
                        if (model.Contains("rog ally") || model.Contains("legion go") || model.Contains("msi claw"))
                        {
                            return true;
                        }
                    }
                } 
                catch { }
                return false;
            });

            _isVirtualMachine = new Lazy<bool>(() => HardwareAwarenessManager.Instance.IsVirtualMachine);
            _hasEfficiencyCores = new Lazy<bool>(() => HardwareAwarenessManager.Instance.HasEfficiencyCores);
            _supportsSmt = new Lazy<bool>(() => HardwareAwarenessManager.Instance.HasSmt);
            
            _hasMultipleGpus = new Lazy<bool>(() => 
            {
                try
                {
                    int gpuCount = 0;
                    using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
                    foreach (var obj in searcher.Get())
                    {
                using var __dispose_obj = obj;
                        gpuCount++;
                    }
                    return gpuCount > 1;
                }
                catch { }
                return false;
            });
        }

        public bool IsWindows10OrHigher() => _isWindows10OrHigher.Value;
        public bool IsWindows11() => _isWindows11.Value;
        public bool IsLaptop() => _isLaptop.Value;
        public bool IsHandheld() => _isHandheld.Value;
        public bool IsVirtualMachine() => _isVirtualMachine.Value;
        public bool HasEfficiencyCores() => _hasEfficiencyCores.Value;
        
        // Thread Director is available on Windows 11 with Efficiency Cores
        public bool SupportsThreadDirector() => IsWindows11() && HasEfficiencyCores();
        public bool SupportsHeterogeneousScheduling() => IsWindows11() && HasEfficiencyCores();
        
        public bool AllowServiceTweaks() => IsWindows10OrHigher();
        public bool AllowRegistryTweaks() => IsWindows10OrHigher();
        public bool HasMultipleGpus() => _hasMultipleGpus.Value;
        public bool SupportsHyperV() => IsWindows10OrHigher(); // Simplified, HyperV is built-in Pro/Ent
        public bool SupportsSmt() => _supportsSmt.Value;
    }
}
