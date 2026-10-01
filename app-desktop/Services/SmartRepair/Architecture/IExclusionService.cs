using System.Collections.Generic;

namespace VoltrisOptimizer.Services.SmartRepair.Architecture
{
    public interface IExclusionService
    {
        bool IsExcludedPath(string path);
        bool IsExcludedRegistryKey(string keyPath);
        bool IsExcludedService(string serviceName);
        bool IsExcludedTask(string taskName);
        
        IReadOnlyList<string> GetExcludedPaths();
    }
}
