using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Cleanup.Modules
{
    public class TempFilesCleanupModule : BaseCleanupModule
    {
        public override string Name => "Arquivos Temporários";

        private readonly string[] _targetDirectories = new[]
        {
            Path.GetTempPath(),
            @"C:\Windows\Temp"
        };

        public override Task<long> AnalyzeAsync(CancellationToken ct)
        {
            return Task.Run(() =>
            {
                long totalSize = 0;
                foreach (var dir in _targetDirectories)
                {
                    ct.ThrowIfCancellationRequested();
                    totalSize += GetDirectorySizeSafe(dir);
                }
                return totalSize;
            });
        }

        public override Task<long> CleanAsync(IProgress<string> progress, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                long totalCleaned = 0;
                foreach (var dir in _targetDirectories)
                {
                    ct.ThrowIfCancellationRequested();
                    progress?.Report($"Limpando {dir}...");
                    totalCleaned += CleanDirectorySafe(dir, ct, progress);
                }
                return totalCleaned;
            });
        }
    }
}
