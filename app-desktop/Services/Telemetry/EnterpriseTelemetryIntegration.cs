using System.Threading.Tasks;

namespace VoltrisOptimizer.Services
{
    public class EnterpriseTelemetryIntegration
    {
        public EnterpriseTelemetryIntegration(object telemetryService, ILoggingService logger) { }
        public Task InitializeAsync() => Task.CompletedTask;
    }
}
