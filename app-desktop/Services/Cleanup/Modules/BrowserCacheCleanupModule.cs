using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Cleanup.Modules
{
    public class BrowserCacheCleanupModule : BaseCleanupModule
    {
        public override string Name => "Cache de Navegadores";

        private readonly string[] _browserCacheDirectories = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\Google\Chrome\User Data\Default\Cache",
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\Google\Chrome\User Data\Default\Code Cache",
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\Microsoft\Edge\User Data\Default\Cache",
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\Microsoft\Edge\User Data\Default\Code Cache",
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\BraveSoftware\Brave-Browser\User Data\Default\Cache",
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\Opera Software\Opera Stable\Cache"
        };

        public override Task<long> AnalyzeAsync(CancellationToken ct)
        {
            return Task.Run(() =>
            {
                long totalSize = 0;
                foreach (var dir in _browserCacheDirectories)
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
                foreach (var dir in _browserCacheDirectories)
                {
                    ct.ThrowIfCancellationRequested();
                    progress?.Report($"Limpando cache de navegador: {dir}...");
                    totalCleaned += CleanDirectorySafe(dir, ct, progress);
                }
                return totalCleaned;
            });
        }
    }
}
