using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.Cleanup.Interfaces;
using VoltrisOptimizer.Services.Cleanup.Modules;

namespace VoltrisOptimizer.Services.Cleanup
{
    public class NativeDiskCleanerEngine
    {
        private readonly List<ICleanupModule> _modules;

        public NativeDiskCleanerEngine()
        {
            _modules = new List<ICleanupModule>
            {
                new TempFilesCleanupModule(),
                new RecycleBinCleanupModule(),
                new WindowsUpdateCleanupModule(),
                new ThumbnailCacheCleanupModule(),
                new SystemLogsCleanupModule(),
                new WindowsErrorReportsCleanupModule(),
                new DeliveryOptimizationCleanupModule(),
                new ShaderCacheCleanupModule(),
                new PrefetchCleanupModule(),
                new BrowserCacheCleanupModule()
            };
        }

        public async Task<long> AnalyzeAllAsync(CancellationToken ct)
        {
            long totalSpace = 0;
            
            var tasks = _modules.Select(async module => 
            {
                try
                {
                    return await module.AnalyzeAsync(ct).ConfigureAwait(false);
                }
                catch
                {
                    return 0L;
                }
            });

            long[] results = await Task.WhenAll(tasks).ConfigureAwait(false);
            totalSpace = results.Sum();

            return totalSpace;
        }

        public async Task<long> RunAllAsync(IProgress<string> progress, CancellationToken ct)
        {
            long totalFreed = 0;

            var tasks = _modules.Select(async module => 
            {
                try
                {
                    return await module.CleanAsync(progress, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    progress?.Report($"[Erro] Falha no módulo {module.Name}: {ex.Message}");
                    return 0L;
                }
            });

            long[] results = await Task.WhenAll(tasks).ConfigureAwait(false);
            totalFreed = results.Sum();

            return totalFreed;
        }
    }
}
