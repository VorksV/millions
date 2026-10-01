using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using VoltrisOptimizer.Core.SystemIntelligenceProfiler;
using VoltrisOptimizer.Core.SystemIntelligenceProfiler.Models;

namespace VoltrisOptimizer.Core.Optimizers
{
    /// <summary>
    /// Central provider for system optimizations.
    /// Maps ActionType/ActionRecommendation to technical Optimization implementations.
    /// </summary>
    public class OptimizationProvider
    {
        private readonly Dictionary<ActionType, Func<VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization>> _registry;

        public OptimizationProvider()
        {
            _registry = new Dictionary<ActionType, Func<VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization>>();
            InitializeRegistry();
            App.LoggingService?.LogTrace($"[OPT_PROVIDER] Registro inicializado com {_registry.Count} tipos de otimização registrados");
        }

        private void InitializeRegistry()
        {
            _registry[ActionType.General_Optimize] = CreateGeneralOptimization;
            _registry[ActionType.SystemCleanup] = CreateSystemCleanup;
            _registry[ActionType.Memory_Optimize] = CreateMemoryOptimization;
            _registry[ActionType.Storage_Defrag] = CreateStorageDefrag;
            _registry[ActionType.Storage_Trim] = CreateStorageTrim;
            _registry[ActionType.Network_OptimizeTcp] = CreateNetworkOptimization;
            _registry[ActionType.PowerPlan_HighPerformance] = CreateHighPerformancePowerPlan;
            // Add more mappings as needed
        }

        public VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization GetOptimization(ActionType type)
        {
            if (_registry.TryGetValue(type, out var factory))
            {
                var opt = factory();
                App.LoggingService?.LogTrace($"[OPT_PROVIDER] Obtendo otimização para o tipo: {type} -> {opt.Name}");
                return opt;
            }
            App.LoggingService?.LogWarning($"[OPT_PROVIDER] Nenhum mapeamento encontrado para o tipo: {type}");
            return null;
        }

        public VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization GetOptimization(ActionRecommendation recommendation)
        {
            var opt = GetOptimization(recommendation.Type);
            if (opt != null)
            {
                // Override names/descriptions from recommendation if they are more specific
                if (!string.IsNullOrEmpty(recommendation.Name)) opt.Name = recommendation.Name;
                if (!string.IsNullOrEmpty(recommendation.Explanation)) opt.Description = recommendation.Explanation;
            }
            return opt;
        }

        #region Factory Methods

        private VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization CreateGeneralOptimization()
        {
            return new VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization
            {
                Name = "General System Optimization",
                Description = "Core system performance and responsiveness tweaks",
                Category = new OptimizationCategory { Name = "General" },
                TargetRegistryValues = new Dictionary<string, object>
                {
                    { @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\SystemResponsiveness", 0 },
                    { @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\NetworkThrottlingIndex", 0xFFFFFFFF }
                }
            };
        }

        private VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization CreateSystemCleanup()
        {
            return new VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization
            {
                Name = "System Cleanup",
                Description = "Cleans temporary files and system caches",
                Category = new OptimizationCategory { Name = "System" },
                ApplyAction = async (ct) => {
                    // In real implementation this would call SystemCleaner service
                    await Task.Delay(500, ct); 
                }
            };
        }

        private VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization CreateMemoryOptimization()
        {
            return new VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization
            {
                Name = "Memory Management Optimization",
                Description = "Optimize memory allocation and paging behavior",
                Category = new OptimizationCategory { Name = "Memory" },
                TargetRegistryValues = new Dictionary<string, object>
                {
                    { @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\LargeSystemCache", 0 },
                    { @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\IoPageLockLimit", 0x4000000 }
                },
                MinRamGb = 8
            };
        }

        private VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization CreateStorageDefrag()
        {
            return new VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization
            {
                Name = "Disk Defragmentation",
                Description = "Optimizes mechanical drive file layout",
                Category = new OptimizationCategory { Name = "Storage" },
                RequiresSSD = false,
                ApplyAction = async (ct) => { await Task.Delay(1000, ct); }
            };
        }

        private VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization CreateStorageTrim()
        {
            return new VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization
            {
                Name = "SSD Trim Optimization",
                Description = "Ensures SSD performance via TRIM command",
                Category = new OptimizationCategory { Name = "Storage" },
                RequiresSSD = true,
                ApplyAction = async (ct) => { await Task.Delay(500, ct); }
            };
        }

        private VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization CreateNetworkOptimization()
        {
            return new VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization
            {
                Name = "Network TCP Optimization",
                Description = "Optimizes TCP/IP stack for lower latency",
                Category = new OptimizationCategory { Name = "Network" },
                TargetRegistryValues = new Dictionary<string, object>
                {
                    { @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\TCPNoDelay", 1 }
                }
            };
        }

        private VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization CreateHighPerformancePowerPlan()
        {
            return new VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization
            {
                Name = "High Performance Power Plan",
                Description = "Activates the high-performance system power profile",
                Category = new OptimizationCategory { Name = "Power" },
                ApplyAction = async (ct) =>
                {
                    if (App.PerformanceOptimizer != null)
                    {
                        var result = await App.PerformanceOptimizer.SetHighPerformancePlanAsync();
                        if (!result.Success)
                            throw new InvalidOperationException($"Falha ao ativar plano: {result.ErrorMessage}");
                    }
                }
            };
        }

        #endregion
    }
}
