using System;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace VoltrisOptimizer.Services.Enterprise
{
    public class TelemetryService
    {
        private static TelemetryService? _instance;
        public static TelemetryService Instance => _instance ??= new TelemetryService();

        public void Initialize(string sessionId, string machineId) { }
        public Task LogNavigationAsync(string pageName) => Task.CompletedTask;
        public Task LogActionAsync(string featureName, string actionName, object? metadata = null) => Task.CompletedTask;
    }
}
