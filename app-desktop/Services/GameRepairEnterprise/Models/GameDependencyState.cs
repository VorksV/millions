using System;

namespace VoltrisOptimizer.Services.GameRepairEnterprise.Models
{
    public enum GameDependencyState
    {
        Unchecked,
        Checking,
        Downloading,
        Installing,
        Validating,
        Repaired,
        Failed,
        RebootRequired
    }
}
