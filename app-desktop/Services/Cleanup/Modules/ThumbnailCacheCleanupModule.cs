using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Cleanup.Modules
{
    public class ThumbnailCacheCleanupModule : BaseCleanupModule
    {
        public override string Name => "Cache de Miniaturas e Ícones";

        private readonly string _explorerDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\Explorer");

        public override Task<long> AnalyzeAsync(CancellationToken ct)
        {
            return Task.Run(() => GetDirectorySizeSafe(_explorerDir));
        }

        public override Task<long> CleanAsync(IProgress<string> progress, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                long freedSpace = 0;
                if (!Directory.Exists(_explorerDir)) return 0L;

                progress?.Report("Limpando cache de miniaturas...");
                
                foreach (var file in Directory.EnumerateFiles(_explorerDir))
                {
                    ct.ThrowIfCancellationRequested();
                    var fileName = Path.GetFileName(file).ToLower();
                    
                    // Somente os arquivos de cache de thumbnail e ícones
                    if (fileName.StartsWith("thumbcache_") || fileName.StartsWith("iconcache_"))
                    {
                        try
                        {
                            var info = new FileInfo(file);
                            long size = info.Length;
                            info.Delete();
                            freedSpace += size;
                        }
                        catch { /* Ignora arquivos travados pelo Explorer.exe */ }
                    }
                }
                
                return freedSpace;
            });
        }
    }
}
