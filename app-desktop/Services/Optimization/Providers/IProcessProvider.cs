using System.Diagnostics;

namespace VoltrisOptimizer.Services.Optimization.Providers
{
    public interface IProcessProvider
    {
        Process[] GetProcesses();
        Process? GetForegroundProcess();
    }
}

