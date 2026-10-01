using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core.VMRG.Interfaces;
using VoltrisOptimizer.Core.VMRG.Models;
using VoltrisOptimizer.Services.VMRG.Detection.Providers;

namespace VoltrisOptimizer.Services.VMRG.Detection
{
    public class VmDetectionService : IVmDetectionService
    {
        private readonly List<IVmDetectionProvider> _providers = new();
        private readonly object _lock = new();
        private readonly ProcessCacheService _processCache;
        private readonly ILoggingService _logger;
        private UnifiedVmProvider? _unifiedProvider;

        public VmDetectionService(ProcessCacheService processCache, ILoggingService logger)
        {
            _processCache = processCache ?? throw new ArgumentNullException(nameof(processCache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void RegisterProvider(IVmDetectionProvider provider)
        {
            lock (_lock)
            {
                _providers.Add(provider);
            }
        }

        public void RegisterProviders(IEnumerable<IVmDetectionProvider> providers)
        {
            lock (_lock)
            {
                _providers.AddRange(providers);
            }
        }

        public async Task<VmDetectionResult> DetectAllAsync(CancellationToken ct)
        {
            var result = new VmDetectionResult
            {
                DetectedAt = DateTime.UtcNow
            };

            try
            {
                if (_unifiedProvider == null)
                {
                    _unifiedProvider = new UnifiedVmProvider(_processCache, _logger);
                }

                var unifiedVms = await _unifiedProvider.DetectAsync(ct);

                foreach (var vm in unifiedVms)
                {
                    if (vm.IsHostProcess)
                        result.HostProcesses.Add(vm);
                    else
                        result.DetectedVms.Add(vm);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[VmDetectionService] UnifiedVmProvider falhou, fallback para providers individuais: {ex.Message}", ex);

                List<IVmDetectionProvider> providers;
                lock (_lock)
                {
                    providers = _providers.ToList();
                }

                foreach (var provider in providers)
                {
                    if (ct.IsCancellationRequested) break;

                    try
                    {
                        var vms = await provider.DetectAsync(ct);

                        foreach (var vm in vms)
                        {
                            if (vm.IsHostProcess)
                                result.HostProcesses.Add(vm);
                            else
                                result.DetectedVms.Add(vm);
                        }
                    }
                    catch
                    {
                    }
                }
            }

            return result;
        }
    }
}
