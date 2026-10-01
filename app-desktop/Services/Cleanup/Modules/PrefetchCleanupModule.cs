using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Cleanup.Modules
{
    public class PrefetchCleanupModule : BaseCleanupModule
    {
        public override string Name => "Prefetch";

        private readonly string _prefetchDir = @"C:\Windows\Prefetch";

        public override Task<long> AnalyzeAsync(CancellationToken ct)
        {
            return Task.Run(() => GetDirectorySizeSafe(_prefetchDir));
        }

        public override Task<long> CleanAsync(IProgress<string> progress, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                progress?.Report("Limpando arquivos do Prefetch...");
                return CleanDirectorySafe(_prefetchDir, ct, progress);
            });
        }
    }
}
