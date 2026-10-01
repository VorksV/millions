using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Cleanup.Modules
{
    public class SystemLogsCleanupModule : BaseCleanupModule
    {
        public override string Name => "Logs de Sistema e Eventos";

        private readonly string _evtxPath = @"C:\Windows\System32\winevt\Logs";
        private readonly string _iisLogsPath = @"C:\inetpub\logs\LogFiles";
        private readonly string _dismLogsPath = @"C:\Windows\Logs\DISM";

        public override Task<long> AnalyzeAsync(CancellationToken ct)
        {
            return Task.Run(() =>
            {
                long totalSize = 0;

                // Evtx
                if (Directory.Exists(_evtxPath))
                {
                    try
                    {
                        var files = Directory.GetFiles(_evtxPath, "*.evtx")
                            .Where(f => !f.Contains("System.evtx") && !f.Contains("Application.evtx") && !f.Contains("Security.evtx"));
                        
                        foreach (var file in files)
                        {
                            ct.ThrowIfCancellationRequested();
                            totalSize += new FileInfo(file).Length;
                        }
                    }
                    catch { }
                }

                totalSize += GetDirectorySizeSafe(_iisLogsPath);
                totalSize += GetDirectorySizeSafe(_dismLogsPath);

                return totalSize;
            });
        }

        public override Task<long> CleanAsync(IProgress<string> progress, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                long freedSpace = 0;

                // Limpar Evtx (comportamento seguro)
                progress?.Report("Limpando logs de eventos do Windows...");
                if (Directory.Exists(_evtxPath))
                {
                    try
                    {
                        var files = Directory.GetFiles(_evtxPath, "*.evtx")
                            .Where(f => !f.Contains("System.evtx") && !f.Contains("Application.evtx") && !f.Contains("Security.evtx"))
                            .Where(f => new FileInfo(f).LastWriteTime < DateTime.Now.AddDays(-7)); // Manter recentes
                        
                        foreach (var file in files)
                        {
                            ct.ThrowIfCancellationRequested();
                            try
                            {
                                var info = new FileInfo(file);
                                long size = info.Length;
                                info.Delete();
                                freedSpace += size;
                            }
                            catch { }
                        }
                    }
                    catch { }
                }

                // Limpar IIS Logs
                if (Directory.Exists(_iisLogsPath))
                {
                    progress?.Report("Limpando logs do IIS...");
                    freedSpace += CleanDirectorySafe(_iisLogsPath, ct, progress);
                }

                // Limpar DISM Logs
                if (Directory.Exists(_dismLogsPath))
                {
                    progress?.Report("Limpando logs do DISM...");
                    freedSpace += CleanDirectorySafe(_dismLogsPath, ct, progress);
                }

                return freedSpace;
            });
        }
    }
}
