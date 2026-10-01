using System;
using System.IO;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Cleanup.Modules
{
    public class WindowsUpdateCleanupModule : BaseCleanupModule
    {
        public override string Name => "Cache do Windows Update";

        private readonly string _downloadDir = @"C:\Windows\SoftwareDistribution\Download";

        public override Task<long> AnalyzeAsync(CancellationToken ct)
        {
            return Task.Run(() => GetDirectorySizeSafe(_downloadDir));
        }

        public override Task<long> CleanAsync(IProgress<string> progress, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                if (!Directory.Exists(_downloadDir)) return 0L;
                
                long freedSpace = 0;
                bool wasRunning = false;

                try
                {
                    using var sc = new ServiceController("wuauserv");
                    if (sc.Status == ServiceControllerStatus.Running)
                    {
                        wasRunning = true;
                        progress?.Report("Parando serviço do Windows Update...");
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
                    }
                }
                catch { /* Ignora se o serviço não puder ser parado */ }

                progress?.Report("Limpando cache de downloads...");
                freedSpace = CleanDirectorySafe(_downloadDir, ct, progress);

                if (wasRunning)
                {
                    try
                    {
                        using var sc = new ServiceController("wuauserv");
                        progress?.Report("Reiniciando serviço do Windows Update...");
                        sc.Start();
                        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
                    }
                    catch { /* Ignora falha ao reiniciar */ }
                }

                return freedSpace;
            });
        }
    }
}
