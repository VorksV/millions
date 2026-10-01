using System;

namespace VoltrisOptimizer.Services.Session
{
    /// <summary>
    /// ActivityMonitor — stub. Sistema de telemetria removido.
    /// </summary>
    public class ActivityMonitor
    {
        public event EventHandler<bool>? IdleStateChanged;
        public bool IsIdle => false;

        public ActivityMonitor(ILoggingService logger) { }
        public void StartMonitoring() { }
        public void StopMonitoring() { }
    }
}
