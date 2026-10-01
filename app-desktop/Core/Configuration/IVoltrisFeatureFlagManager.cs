using System;
using System.Collections.Generic;

namespace VoltrisOptimizer.Core.Configuration
{
    public interface IVoltrisFeatureFlagManager
    {
        bool IsEnabled(string featureKey);
        
        // Fast paths for Critical Strangler Pattern integrations
        bool UseSafeRegistryManager { get; }
        bool UseSafePowerManager { get; }
        bool UseSafeProcessManager { get; }
        bool UseSafeAffinityManager { get; }
        bool UseAtomicRollbackEngine { get; }
        bool UseEnterpriseScheduler { get; }
    }
}
