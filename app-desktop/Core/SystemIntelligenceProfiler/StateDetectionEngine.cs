using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Core.SystemIntelligenceProfiler.Models;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Core.SystemIntelligenceProfiler
{
    /// <summary>
    /// Enhanced State Detection Engine for intelligent optimization profiling
    /// Provides comprehensive system state analysis and optimization status evaluation
    /// </summary>
    public class StateDetectionEngine
    {
        private readonly ILoggingService _logger;
        private readonly ISystemInfoService _systemInfoService;

        public StateDetectionEngine(ILoggingService logger, ISystemInfoService systemInfoService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _systemInfoService = systemInfoService ?? throw new ArgumentNullException(nameof(systemInfoService));
        }

        /// <summary>
        /// Captures a complete system state snapshot for the specified optimization category
        /// </summary>
        /// <param name="category">Optimization category to analyze</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Complete system state snapshot</returns>
        public async Task<SystemStateSnapshot> CaptureCurrentStateAsync(OptimizationCategory category, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            _logger.Log(LogLevel.Info, LogCategory.System, 
                $"Capturing system state for category: {category.Name}", 
                source: "StateDetectionEngine");

            var snapshot = new SystemStateSnapshot
            {
                Timestamp = DateTime.UtcNow,
                Category = category,
                HardwareProfile = await DetectHardwareProfileAsync(ct)
            };

            // Capture registry states
            if (category.RegistryKeys?.Any() == true)
            {
                snapshot.RegistryStates = await DetectRegistryStatesAsync(category.RegistryKeys, ct);
            }

            // Capture service states
            if (category.Services?.Any() == true)
            {
                snapshot.ServiceStates = await DetectServiceStatesAsync(category.Services, ct);
            }

            // Capture policy states
            if (category.Policies?.Any() == true)
            {
                snapshot.PolicyStates = await DetectPolicyStatesAsync(category.Policies, ct);
            }

            // Capture file states
            if (category.ConfigFiles?.Any() == true)
            {
                snapshot.FileStates = await DetectFileStatesAsync(category.ConfigFiles, ct);
            }

            _logger.Log(LogLevel.Info, LogCategory.System, 
                $"State capture completed for {category.Name}. " +
                $"Registry: {snapshot.RegistryStates.Count}, " +
                $"Services: {snapshot.ServiceStates.Count}, " +
                $"Policies: {snapshot.PolicyStates.Count}, " +
                $"Files: {snapshot.FileStates.Count}",
                source: "StateDetectionEngine");

            return snapshot;
        }

        /// <summary>
        /// Analyzes whether an optimization needs to be applied based on current system state
        /// </summary>
        /// <param name="optimization">Optimization to evaluate</param>
        /// <param name="currentState">Current system state snapshot</param>
        /// <returns>Optimization status determination</returns>
        public OptimizationStatus AnalyzeOptimizationStatus(Optimization optimization, SystemStateSnapshot currentState)
        {
            try
            {
                _logger.Log(LogLevel.Info, LogCategory.Optimization, 
                    $"Analyzing optimization status: {optimization.Name}", 
                    source: "StateDetectionEngine");

                // Check hardware compatibility first
                if (!IsHardwareCompatible(optimization, currentState.HardwareProfile))
                {
                    _logger.Log(LogLevel.Warning, LogCategory.Optimization, 
                        $"Optimization {optimization.Name} is incompatible with hardware", 
                        source: "StateDetectionEngine");
                    return OptimizationStatus.Incompatible;
                }

                // Check if already applied by comparing current state with target state
                if (IsAlreadyApplied(optimization, currentState))
                {
                    _logger.Log(LogLevel.Info, LogCategory.Optimization, 
                        $"Optimization {optimization.Name} is already applied", 
                        source: "StateDetectionEngine");
                    return OptimizationStatus.AlreadyApplied;
                }

                // Check for conflicts with existing configurations
                if (HasConflicts(optimization, currentState))
                {
                    _logger.Log(LogLevel.Warning, LogCategory.Optimization, 
                        $"Optimization {optimization.Name} has conflicts with existing configuration", 
                        source: "StateDetectionEngine");
                    return OptimizationStatus.ConflictsDetected;
                }

                // Check if elevation is required
                if (RequiresElevation(optimization))
                {
                    _logger.Log(LogLevel.Info, LogCategory.Optimization, 
                        $"Optimization {optimization.Name} requires administrative privileges", 
                        source: "StateDetectionEngine");
                    return OptimizationStatus.RequiresElevation;
                }

                _logger.Log(LogLevel.Info, LogCategory.Optimization, 
                    $"Optimization {optimization.Name} needs application", 
                    source: "StateDetectionEngine");
                return OptimizationStatus.NeedsApplication;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error analyzing optimization status for {optimization.Name}: {ex.Message}", ex);
                return OptimizationStatus.Unknown;
            }
        }

        #region Private Detection Methods

        private async Task<HardwareProfile> DetectHardwareProfileAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var cpuInfo = await _systemInfoService.GetCpuInfoAsync();
                var ramInfo = await _systemInfoService.GetRamInfoAsync();
                
                var cpuModel = new VoltrisOptimizer.Core.SystemIntelligenceProfiler.Models.CpuInfo
                {
                    Name = cpuInfo?.Name ?? "Unknown",
                    LogicalCores = cpuInfo?.ThreadCount ?? 0,
                    PhysicalCores = cpuInfo?.CoreCount ?? 0,
                    MaxClockSpeed = (int)(cpuInfo?.MaxClockSpeedMHz ?? 0),
                    Architecture = cpuInfo?.Architecture ?? (Environment.Is64BitOperatingSystem ? "x64" : "x86")
                };

                var ramModel = new VoltrisOptimizer.Core.SystemIntelligenceProfiler.Models.RamInfo
                {
                    TotalMb = ramInfo != null ? (int)(ramInfo.TotalBytes / (1024 * 1024)) : 0,
                    AvailableMb = ramInfo != null ? (int)(ramInfo.AvailableBytes / (1024 * 1024)) : 0,
                    SpeedMhz = 0,
                    Type = "Unknown"
                };

                var storageModel = await DetectStorageInfoAsync();
                
                var profile = new HardwareProfile
                {
                    Cpu = cpuModel,
                    Ram = ramModel,
                    Storage = storageModel,
                    HardwareScore = CalculateHardwareScore(cpuModel, ramModel),
                    Classification = ClassifyHardware(cpuModel, ramModel)
                };

                return profile;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error detecting hardware profile: {ex.Message}", ex);
                return new HardwareProfile { Classification = HardwareClass.Unknown };
            }
        }

        private async Task<VoltrisOptimizer.Core.SystemIntelligenceProfiler.Models.StorageInfo> DetectStorageInfoAsync()
        {
            try
            {
                var drive = System.IO.DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady && d.DriveType == System.IO.DriveType.Fixed);
                if (drive == null) return new VoltrisOptimizer.Core.SystemIntelligenceProfiler.Models.StorageInfo();

                return new VoltrisOptimizer.Core.SystemIntelligenceProfiler.Models.StorageInfo
                {
                    TotalGb = (int)(drive.TotalSize / (1024 * 1024 * 1024)),
                    FreeGb = (int)(drive.AvailableFreeSpace / (1024 * 1024 * 1024)),
                    Type = drive.DriveFormat.ToUpper() == "NTFS" ? "SSD" : "HDD", // Simplified detection
                    DriveLetter = drive.Name
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error detecting storage info: {ex.Message}", ex);
                return new VoltrisOptimizer.Core.SystemIntelligenceProfiler.Models.StorageInfo();
            }
        }

        private double CalculateHardwareScore(
            VoltrisOptimizer.Core.SystemIntelligenceProfiler.Models.CpuInfo cpuInfo, 
            VoltrisOptimizer.Core.SystemIntelligenceProfiler.Models.RamInfo ramInfo)
        {
            // Simple scoring algorithm - can be enhanced
            double score = 1.0;
            
            // CPU contribution (0-4 points)
            if (cpuInfo != null)
            {
                score += Math.Min(cpuInfo.LogicalCores / 2.0, 4.0);
            }
            
            // RAM contribution (0-3 points)
            if (ramInfo != null)
            {
                score += Math.Min(ramInfo.TotalMb / 4096.0, 3.0);
            }
            
            // Cap at 10.0
            return Math.Min(score, 10.0);
        }

        private HardwareClass ClassifyHardware(
            VoltrisOptimizer.Core.SystemIntelligenceProfiler.Models.CpuInfo cpuInfo, 
            VoltrisOptimizer.Core.SystemIntelligenceProfiler.Models.RamInfo ramInfo)
        {
            var score = CalculateHardwareScore(cpuInfo, ramInfo);
            
            return score switch
            {
                < 3.0 => HardwareClass.LowEnd,
                < 5.0 => HardwareClass.MidRange,
                < 7.0 => HardwareClass.HighEnd,
                < 9.0 => HardwareClass.Workstation,
                _ => HardwareClass.Server
            };
        }

        private async Task<Dictionary<string, RegistryValueState>> DetectRegistryStatesAsync(List<string> registryKeys, CancellationToken ct)
        {
            var states = new Dictionary<string, RegistryValueState>();

            foreach (var keyPath in registryKeys)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var parts = keyPath.Split('\\');
                    if (parts.Length < 3) continue;

                    var hive = parts[0];
                    var subKeyPath = string.Join("\\", parts.Skip(1).Take(parts.Length - 2));
                    var valueName = parts.Last();

                    using var baseKey = hive.ToUpper() switch
                    {
                        "HKLM" or "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
                        "HKCU" or "HKEY_CURRENT_USER" => Registry.CurrentUser,
                        "HKCR" or "HKEY_CLASSES_ROOT" => Registry.ClassesRoot,
                        "HKU" or "HKEY_USERS" => Registry.Users,
                        _ => null
                    };

                    if (baseKey == null) continue;

                    using var subKey = baseKey.OpenSubKey(subKeyPath);
                    if (subKey == null)
                    {
                        states[keyPath] = new RegistryValueState
                        {
                            KeyPath = keyPath,
                            ValueName = valueName,
                            Exists = false
                        };
                        continue;
                    }

                    var value = subKey.GetValue(valueName);
                    var valueType = subKey.GetValueKind(valueName);

                    states[keyPath] = new RegistryValueState
                    {
                        KeyPath = keyPath,
                        ValueName = valueName,
                        ValueData = value,
                        ValueType = valueType,
                        Exists = value != null,
                        ValueHash = value?.ToString().GetHashCode().ToString()
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Error detecting registry state for {keyPath}: {ex.Message}");
                    states[keyPath] = new RegistryValueState { KeyPath = keyPath, Exists = false };
                }
            }

            return states;
        }

        private async Task<Dictionary<string, ServiceState>> DetectServiceStatesAsync(List<string> serviceNames, CancellationToken ct)
        {
            var states = new Dictionary<string, ServiceState>();

            foreach (var serviceName in serviceNames)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    using var service = new ServiceController(serviceName);
                    states[serviceName] = new ServiceState
                    {
                        ServiceName = serviceName,
                        DisplayName = service.DisplayName,
                        Status = service.Status,
                        StartType = GetServiceStartType(serviceName),
                        Exists = true,
                        ExecutablePath = GetServiceExecutablePath(serviceName)
                    };
                }
                catch (InvalidOperationException)
                {
                    // Service doesn't exist
                    states[serviceName] = new ServiceState
                    {
                        ServiceName = serviceName,
                        Exists = false
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Error detecting service state for {serviceName}: {ex.Message}");
                    states[serviceName] = new ServiceState { ServiceName = serviceName, Exists = false };
                }
            }

            return states;
        }

        private async Task<Dictionary<string, PolicyState>> DetectPolicyStatesAsync(List<string> policies, CancellationToken ct)
        {
            var states = new Dictionary<string, PolicyState>();

            foreach (var policy in policies)
            {
                ct.ThrowIfCancellationRequested();

                // Simplified policy detection - would need Group Policy APIs for full implementation
                states[policy] = new PolicyState
                {
                    PolicyName = policy,
                    Category = "System",
                    Value = null,
                    IsEnforced = false,
                    Source = "Local"
                };
            }

            return states;
        }

        private async Task<Dictionary<string, FileState>> DetectFileStatesAsync(List<string> filePaths, CancellationToken ct)
        {
            var states = new Dictionary<string, FileState>();

            foreach (var filePath in filePaths)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    if (File.Exists(filePath))
                    {
                        var info = new System.IO.FileInfo(filePath);
                        states[filePath] = new FileState
                        {
                            FilePath = filePath,
                            Exists = true,
                            Size = info.Length,
                            LastModified = info.LastWriteTimeUtc,
                            Hash = ComputeFileHash(filePath),
                            Attributes = info.Attributes
                        };
                    }
                    else
                    {
                        states[filePath] = new FileState
                        {
                            FilePath = filePath,
                            Exists = false
                        };
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Error detecting file state for {filePath}: {ex.Message}");
                    states[filePath] = new FileState { FilePath = filePath, Exists = false };
                }
            }

            return states;
        }

        #endregion

        #region Helper Methods

        private bool IsHardwareCompatible(Optimization optimization, HardwareProfile hardware)
        {
            // Check hardware requirements
            if (optimization.RequiresSSD && hardware.Storage?.Type?.Contains("SSD") != true)
                return false;

            if (optimization.MinRamGb > 0 && (hardware.Ram?.TotalMb ?? 0) < optimization.MinRamGb * 1024)
                return false;

            if (optimization.MinCpuCores > 0 && (hardware.Cpu?.LogicalCores ?? 0) < optimization.MinCpuCores)
                return false;

            return true;
        }

        private bool IsAlreadyApplied(Optimization optimization, SystemStateSnapshot currentState)
        {
            // Compare registry states
            if (optimization.TargetRegistryValues?.Any() == true)
            {
                foreach (var targetReg in optimization.TargetRegistryValues)
                {
                    if (currentState.RegistryStates.TryGetValue(targetReg.Key, out var currentReg))
                    {
                        if (!currentReg.Exists || !Equals(currentReg.ValueData, targetReg.Value))
                            return false;
                    }
                    else
                    {
                        return false;
                    }
                }
            }

            // Compare service states
            if (optimization.TargetServiceStates?.Any() == true)
            {
                foreach (var targetSvc in optimization.TargetServiceStates)
                {
                    if (currentState.ServiceStates.TryGetValue(targetSvc.Key, out var currentSvc))
                    {
                        if (!currentSvc.Exists || currentSvc.Status != targetSvc.Value)
                            return false;
                    }
                    else
                    {
                        return false;
                    }
                }
            }

            // Compare file states
            if (optimization.TargetFileStates?.Any() == true)
            {
                foreach (var targetFile in optimization.TargetFileStates)
                {
                    if (currentState.FileStates.TryGetValue(targetFile.Key, out var currentFile))
                    {
                        if (!currentFile.Exists || currentFile.Hash != targetFile.Value)
                            return false;
                    }
                    else
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private bool HasConflicts(Optimization optimization, SystemStateSnapshot currentState)
        {
            // Check for conflicting registry values
            if (optimization.ConflictingRegistryKeys?.Any() == true)
            {
                foreach (var conflictKey in optimization.ConflictingRegistryKeys)
                {
                    if (currentState.RegistryStates.ContainsKey(conflictKey))
                        return true;
                }
            }

            // Check for conflicting services
            if (optimization.ConflictingServices?.Any() == true)
            {
                foreach (var conflictService in optimization.ConflictingServices)
                {
                    if (currentState.ServiceStates.ContainsKey(conflictService))
                        return true;
                }
            }

            // Check for conflicting files
            if (optimization.ConflictingFiles?.Any() == true)
            {
                foreach (var conflictFile in optimization.ConflictingFiles)
                {
                    if (currentState.FileStates.ContainsKey(conflictFile))
                        return true;
                }
            }

            return false;
        }

        private bool RequiresElevation(Optimization optimization)
        {
            // Check if optimization requires admin privileges
            return optimization.RequiresAdmin;
        }

        private ServiceStartMode GetServiceStartType(string serviceName)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
                var startValue = key?.GetValue("Start");
                return startValue switch
                {
                    2 => ServiceStartMode.Automatic,
                    3 => ServiceStartMode.Manual,
                    4 => ServiceStartMode.Disabled,
                    _ => ServiceStartMode.Manual
                };
            }
            catch
            {
                return ServiceStartMode.Manual;
            }
        }

        private string GetServiceExecutablePath(string serviceName)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
                return key?.GetValue("ImagePath")?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private string ComputeFileHash(string filePath)
        {
            try
            {
                using var sha256 = SHA256.Create();
                using var stream = File.OpenRead(filePath);
                var hash = sha256.ComputeHash(stream);
                return Convert.ToBase64String(hash);
            }
            catch
            {
                return string.Empty;
            }
        }

        #endregion
    }

    /// <summary>
    /// Represents an optimization with its requirements and state information
    /// </summary>
    public class Optimization
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public OptimizationCategory Category { get; set; } = new();
        public bool RequiresSSD { get; set; }
        public int MinRamGb { get; set; }
        public int MinCpuCores { get; set; }
        public bool RequiresAdmin { get; set; }
        public Func<CancellationToken, Task> ApplyAction { get; set; } = _ => Task.CompletedTask;
        public Func<CancellationToken, Task> RevertAction { get; set; } = _ => Task.CompletedTask;
        
        // Target states for comparison
        public Dictionary<string, object> TargetRegistryValues { get; set; } = new();
        public Dictionary<string, ServiceControllerStatus> TargetServiceStates { get; set; } = new();
        public Dictionary<string, string> TargetFileStates { get; set; } = new();
        
        // Conflicting configurations
        public List<string> ConflictingRegistryKeys { get; set; } = new();
        public List<string> ConflictingServices { get; set; } = new();
        public List<string> ConflictingFiles { get; set; } = new();
    }
}
