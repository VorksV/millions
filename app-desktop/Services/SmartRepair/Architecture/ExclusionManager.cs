using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VoltrisOptimizer.Services.SmartRepair.Architecture
{
    public class ExclusionManager : IExclusionService
    {
        private readonly HashSet<string> _excludedPaths = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _excludedRegistryKeys = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _excludedServices = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _excludedTasks = new(StringComparer.OrdinalIgnoreCase);

        public ExclusionManager()
        {
            InitializeDefaultExclusions();
        }

        private void InitializeDefaultExclusions()
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            
            // Core user directories
            _excludedPaths.Add(Path.Combine(userProfile, "Desktop"));
            _excludedPaths.Add(Path.Combine(userProfile, "Documents"));
            _excludedPaths.Add(Path.Combine(userProfile, "Downloads"));
            _excludedPaths.Add(Path.Combine(userProfile, "Music"));
            _excludedPaths.Add(Path.Combine(userProfile, "Pictures"));
            _excludedPaths.Add(Path.Combine(userProfile, "Videos"));
            _excludedPaths.Add(Path.Combine(userProfile, "OneDrive"));
            
            // Common development directories
            _excludedPaths.Add(Path.Combine(userProfile, "source", "repos"));
            _excludedPaths.Add(Path.Combine(userProfile, ".ssh"));
            _excludedPaths.Add(Path.Combine(userProfile, ".aws"));
            _excludedPaths.Add(Path.Combine(userProfile, ".gitconfig"));
            
            // Important System Services
            _excludedServices.Add("wuauserv"); // Windows Update
            _excludedServices.Add("LanmanWorkstation");
            _excludedServices.Add("LanmanServer");
            
            // Important Tasks
            _excludedTasks.Add(@"\Microsoft\Windows\Defrag\ScheduledDefrag");
        }

        public bool IsExcludedPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            
            // Check if path starts with any excluded directory path
            return _excludedPaths.Any(excluded => 
                path.Equals(excluded, StringComparison.OrdinalIgnoreCase) || 
                path.StartsWith(excluded + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(excluded + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        }

        public bool IsExcludedRegistryKey(string keyPath)
        {
            if (string.IsNullOrWhiteSpace(keyPath)) return false;
            return _excludedRegistryKeys.Contains(keyPath);
        }

        public bool IsExcludedService(string serviceName)
        {
            if (string.IsNullOrWhiteSpace(serviceName)) return false;
            return _excludedServices.Contains(serviceName);
        }

        public bool IsExcludedTask(string taskName)
        {
            if (string.IsNullOrWhiteSpace(taskName)) return false;
            return _excludedTasks.Contains(taskName);
        }

        public IReadOnlyList<string> GetExcludedPaths()
        {
            return _excludedPaths.ToList().AsReadOnly();
        }
    }
}
