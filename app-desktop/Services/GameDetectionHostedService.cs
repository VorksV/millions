using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace VoltrisOptimizer.Services
{
    public class GameDetectionHostedService : BackgroundService
    {
        private readonly GameDetectionService _detector;

        public GameDetectionHostedService(GameDetectionService detector)
        {
            _detector = detector;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _detector.StartMonitoring();
            stoppingToken.Register(() => _detector.StopMonitoring());
            return Task.CompletedTask;
        }
    }
}

