using System;
using System.IO;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Cleanup.Modules
{
    public class DeliveryOptimizationCleanupModule : BaseCleanupModule
    {
        public override string Name => "Cache de Otimização de Entrega";

        private readonly string _doCacheDir = @"C:\Windows\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache";

        public override Task<long> AnalyzeAsync(CancellationToken ct)
        {
            return Task.Run(() => GetDirectorySizeSafe(_doCacheDir));
        }

        public override Task<long> CleanAsync(IProgress<string> progress, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                if (!Directory.Exists(_doCacheDir)) return 0L;
                
                long freedSpace = 0;
                bool wasRunning = false;

                try
                {
                    using var sc = new ServiceController("DoSvc");
                    if (sc.Status == ServiceControllerStatus.Running)
                    {
                        wasRunning = true;
                        progress?.Report("Parando serviço de Otimização de Entrega (DoSvc)...");
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
                    }
                }
                catch { /* Serviço pode não existir ou não termos permissão */ }

                progress?.Report("Limpando cache de Otimização de Entrega...");
                freedSpace = CleanDirectorySafe(_doCacheDir, ct, progress);

                if (wasRunning)
                {
                    try
                    {
                        using var sc = new ServiceController("DoSvc");
                        progress?.Report("Reiniciando serviço de Otimização de Entrega...");
                        sc.Start();
                        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
                    }
                    catch { }
                }

                return freedSpace;
            });
        }
    }
}
