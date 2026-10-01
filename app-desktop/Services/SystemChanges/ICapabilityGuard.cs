using System;

namespace VoltrisOptimizer.Services.SystemChanges
{
    public interface ICapabilityGuard
    {
        bool IsWindows10OrHigher();
        bool IsWindows11();
        bool IsLaptop();
        bool IsHandheld();
        bool IsVirtualMachine();
        bool HasEfficiencyCores();
        bool SupportsThreadDirector();
        bool SupportsHeterogeneousScheduling();
        bool AllowServiceTweaks();
        bool AllowRegistryTweaks();
        bool HasMultipleGpus();
        bool SupportsHyperV();
        bool SupportsSmt();
    }
}
