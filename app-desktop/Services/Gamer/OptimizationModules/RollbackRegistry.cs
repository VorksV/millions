using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Win32;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer.OptimizationModules
{
    public class RollbackRegistry
    {
        private readonly ILoggingService _logger;

        public RollbackRegistry()
        {
            _logger = null!;
        }

        public RollbackRegistry(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogEntry(nameof(RollbackRegistry));
            _logger.LogExit(nameof(RollbackRegistry));
        }

        private readonly List<RegistrySnapshot> _registrySnapshots = new();
        private PowerPlanSnapshot? _powerPlanSnapshot;
        private readonly List<ProcessSnapshot> _processSnapshots = new();

        public void RegisterRegistryKey(RegistryHive hive, string subKey, string valueName, object? originalValue, RegistryValueKind valueKind)
        {
            _logger.LogEntry(nameof(RegisterRegistryKey));
            _registrySnapshots.Add(new RegistrySnapshot
            {
                Hive = hive,
                SubKey = subKey,
                ValueName = valueName,
                OriginalValue = originalValue?.ToString(),
                ValueKind = valueKind
            });
            _logger.LogExit(nameof(RegisterRegistryKey));
        }

        public IEnumerable<RegistrySnapshot> GetRegistrySnapshots()
        {
            _logger.LogEntry(nameof(GetRegistrySnapshots));
            _logger.LogExit(nameof(GetRegistrySnapshots));
            return _registrySnapshots.ToList();
        }

        public void RegisterPowerPlan(string guid, string name)
        {
            _logger.LogEntry(nameof(RegisterPowerPlan));
            _powerPlanSnapshot = new PowerPlanSnapshot
            {
                Guid = guid,
                Name = name
            };
            _logger.LogExit(nameof(RegisterPowerPlan));
        }

        public PowerPlanSnapshot? GetPowerPlanSnapshot()
        {
            _logger.LogEntry(nameof(GetPowerPlanSnapshot));
            var result = _powerPlanSnapshot;
            _logger.LogExit(nameof(GetPowerPlanSnapshot));
            return result;
        }

        public void RegisterProcessPriority(int processId, ProcessPriorityClass originalPriority)
        {
            _logger.LogEntry(nameof(RegisterProcessPriority));
            var existing = _processSnapshots.FirstOrDefault(s => s.ProcessId == processId);
            if (existing == null)
            {
                _processSnapshots.Add(new ProcessSnapshot
                {
                    ProcessId = processId,
                    OriginalPriority = originalPriority
                });
            }
            else
            {
                existing.OriginalPriority = originalPriority;
            }
            _logger.LogExit(nameof(RegisterProcessPriority));
        }

        public void RegisterProcessIoPriority(int processId, int originalIoPriority)
        {
            _logger.LogEntry(nameof(RegisterProcessIoPriority));
            var existing = _processSnapshots.FirstOrDefault(s => s.ProcessId == processId);
            if (existing == null)
            {
                _processSnapshots.Add(new ProcessSnapshot
                {
                    ProcessId = processId,
                    OriginalIoPriority = originalIoPriority
                });
            }
            else
            {
                existing.OriginalIoPriority = originalIoPriority;
            }
            _logger.LogExit(nameof(RegisterProcessIoPriority));
        }

        public void RegisterProcessAffinity(int processId, IntPtr originalAffinity)
        {
            _logger.LogEntry(nameof(RegisterProcessAffinity));
            var existing = _processSnapshots.FirstOrDefault(s => s.ProcessId == processId);
            if (existing == null)
            {
                _processSnapshots.Add(new ProcessSnapshot
                {
                    ProcessId = processId,
                    OriginalAffinity = originalAffinity
                });
            }
            else
            {
                existing.OriginalAffinity = originalAffinity;
            }
            _logger.LogExit(nameof(RegisterProcessAffinity));
        }

        public IEnumerable<ProcessSnapshot> GetProcessSnapshots()
        {
            _logger.LogEntry(nameof(GetProcessSnapshots));
            _logger.LogExit(nameof(GetProcessSnapshots));
            return _processSnapshots.ToList();
        }
    }

    public class RegistrySnapshot
    {
        public RegistryHive Hive { get; set; }
        public string SubKey { get; set; } = string.Empty;
        public string ValueName { get; set; } = string.Empty;
        public string? OriginalValue { get; set; }
        public RegistryValueKind ValueKind { get; set; }
    }

    public class ProcessSnapshot
    {
        public int ProcessId { get; set; }
        public ProcessPriorityClass OriginalPriority { get; set; }
        public IntPtr? OriginalAffinity { get; set; }
        public int OriginalIoPriority { get; set; }
    }

    public struct PowerPlanSnapshot
    {
        public string Guid { get; set; }
        public string Name { get; set; }
    }
}
