using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.SmartRepair.Architecture
{
    public class ModularSmartRepairEngine
    {
        private readonly List<ISmartRepairModule> _modules = new();
        private readonly ILoggingService _logger;
        private readonly ISmartRepairEventBus _eventBus;
        private readonly IExclusionService _exclusionService;
        private readonly string _correlationId;

        public IReadOnlyList<ISmartRepairModule> Modules => _modules.AsReadOnly();

        public ModularSmartRepairEngine(ILoggingService logger, ISmartRepairEventBus eventBus, IExclusionService exclusionService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _exclusionService = exclusionService ?? throw new ArgumentNullException(nameof(exclusionService));
            _correlationId = Guid.NewGuid().ToString("N");
        }

        public void RegisterModule(ISmartRepairModule module)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            if (_modules.Any(m => m.ModuleId.Equals(module.ModuleId, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Module with ID {module.ModuleId} is already registered.");
            }
            _modules.Add(module);
            _logger.LogInfo($"[ModularEngine] Module {module.ModuleId} registered.");
                _logger.LogDebug($"[ModularEngine] Module details: Name={module.Name}, Category={module.Category}");
        }

        public IReadOnlyList<ISmartRepairModule> GetExecutionPlan()
        {
            var sorted = TopologicalSort(_modules);
            var restoreModule = sorted.FirstOrDefault(m => m.ModuleId.Equals("SystemRestore", StringComparison.OrdinalIgnoreCase));
            if (restoreModule != null)
            {
                sorted.Remove(restoreModule);
                sorted.Insert(0, restoreModule);
            }
            return sorted.AsReadOnly();
        }

        private List<ISmartRepairModule> TopologicalSort(List<ISmartRepairModule> modules)
        {
            var sorted = new List<ISmartRepairModule>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Visit(ISmartRepairModule module)
            {
                if (visited.Contains(module.ModuleId)) return;
                if (visiting.Contains(module.ModuleId)) throw new InvalidOperationException($"Circular dependency detected involving {module.ModuleId}");

                visiting.Add(module.ModuleId);

                foreach (var depId in module.Dependencies)
                {
                    var depModule = modules.FirstOrDefault(m => m.ModuleId.Equals(depId, StringComparison.OrdinalIgnoreCase));
                    if (depModule != null)
                    {
                        Visit(depModule);
                    }
                    else
                    {
                        _logger.LogWarning($"[ModularEngine] Dependency {depId} for {module.ModuleId} was not found.");
                    }
                }

                visiting.Remove(module.ModuleId);
                visited.Add(module.ModuleId);
                sorted.Add(module);
            }

            foreach (var mod in modules)
            {
                Visit(mod);
            }

            return sorted;
        }

        public async Task<Dictionary<string, ModuleScanResult>> ScanAllAsync(IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var results = new Dictionary<string, ModuleScanResult>();
            var plan = GetExecutionPlan();
            
            _eventBus.PublishMessage(_correlationId, "Engine", LocalizationService.Instance.GetString("Engine_StartingGlobalScan"), SmartRepairEventType.ProgressChanged);

            foreach (var module in plan)
            {
                ct.ThrowIfCancellationRequested();
                if (!module.Capabilities.SupportsScan) continue;

                _eventBus.PublishMessage(_correlationId, module.ModuleId, string.Format(LocalizationService.Instance.GetString("Module_StartingScan"), module.Name), SmartRepairEventType.ModuleStarted);
                try
                {
                    var result = await module.ScanAsync(progress, ct);
                    results[module.ModuleId] = result;
                    _eventBus.PublishMessage(_correlationId, module.ModuleId, string.Format(LocalizationService.Instance.GetString("Module_FinishedScan"), module.Name), SmartRepairEventType.ModuleFinished);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _eventBus.PublishError(_correlationId, module.ModuleId, ex, string.Format(LocalizationService.Instance.GetString("ErrorScanningModule"), module.Name));
                    results[module.ModuleId] = new ModuleScanResult { Success = false, Message = ex.Message };
                }
            }

            return results;
        }

        public async Task<Dictionary<string, ModuleSimulationResult>> SimulateAllAsync(Dictionary<string, ModuleScanResult> scanResults, CancellationToken ct)
        {
            var results = new Dictionary<string, ModuleSimulationResult>();
            var plan = GetExecutionPlan();

            foreach (var module in plan)
            {
                ct.ThrowIfCancellationRequested();
                if (!module.Capabilities.SupportsSimulation || !scanResults.TryGetValue(module.ModuleId, out var scanResult))
                    continue;

                try
                {
                    var result = await module.SimulateAsync(scanResult, ct);
                    results[module.ModuleId] = result;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[ModularEngine] Simulation failed for {module.ModuleId}", ex);
                    results[module.ModuleId] = new ModuleSimulationResult { Success = false, Message = ex.Message };
                }
            }
            return results;
        }

        public async Task<Dictionary<string, ModuleExecutionResult>> ExecuteAllAsync(Dictionary<string, ModuleSimulationResult> simResults, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var results = new Dictionary<string, ModuleExecutionResult>();
            var plan = GetExecutionPlan();

            for (int i = 0; i < plan.Count; i++)
            {
                var module = plan[i];
                ct.ThrowIfCancellationRequested();
                if (!module.Capabilities.SupportsExecution || !simResults.TryGetValue(module.ModuleId, out var simResult))
                    continue;

                _eventBus.Publish(new SmartRepairEventArgs
                {
                    CorrelationId = _correlationId,
                    ModuleId = module.ModuleId,
                    Message = string.Format(LocalizationService.Instance.GetString("ExecutingModule"), module.Name),
                    EventType = SmartRepairEventType.OperationStarted,
                    ProgressPercentage = 0,
                    StepIndex = i
                });

                try
                {
                    var moduleProgress = new Progress<RepairProgress>(p =>
                    {
                        progress?.Report(p);
                        if (!string.IsNullOrWhiteSpace(p.StatusMessage) &&
                            !p.StatusMessage.Contains("[==") && !p.StatusMessage.Contains("---") && !p.StatusMessage.Contains("==="))
                        {
                            _eventBus.Publish(new SmartRepairEventArgs
                            {
                                CorrelationId = _correlationId,
                                ModuleId = module.ModuleId,
                                Message = p.StatusMessage,
                                EventType = SmartRepairEventType.ProgressChanged,
                                ProgressPercentage = p.StepPercent,
                                StepIndex = i
                            });
                        }
                    });

                    var result = await module.ExecuteAsync(simResult, moduleProgress, ct);
                    results[module.ModuleId] = result;
                    
                    _eventBus.Publish(new SmartRepairEventArgs
                    {
                        CorrelationId = _correlationId,
                        ModuleId = module.ModuleId,
                        Message = string.Format(LocalizationService.Instance.GetString("ExecutionCompletedFor"), module.Name),
                        EventType = SmartRepairEventType.OperationFinished,
                        ProgressPercentage = 100,
                        StepIndex = i
                    });
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _eventBus.PublishError(_correlationId, module.ModuleId, ex, string.Format(LocalizationService.Instance.GetString("ExecutionFailedFor"), module.Name));
                    results[module.ModuleId] = new ModuleExecutionResult { Success = false, Message = ex.Message };
                }
            }

            return results;
        }
    }
}
