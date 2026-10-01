using System;
using System.Threading.Tasks;
using System.Windows;

namespace VoltrisOptimizer.Helpers
{
    public static class SyncBlockDetector
    {
        public static T RunSync<T>(Func<Task<T>> task, string caller)
        {
            if (Application.Current?.Dispatcher?.CheckAccess() == true)
            {
                VoltrisDiagnosticSystem.Instance.LogCriticalSyncBlock(caller);
            }

            return task().GetAwaiter().GetResult();
        }

        public static void RunSync(Func<Task> task, string caller)
        {
            if (Application.Current?.Dispatcher?.CheckAccess() == true)
            {
                VoltrisDiagnosticSystem.Instance.LogCriticalSyncBlock(caller);
            }

            task().GetAwaiter().GetResult();
        }
    }
}
