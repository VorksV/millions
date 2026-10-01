using System;

namespace VoltrisOptimizer.Services.Optimization.Providers
{
    public interface ICpuCoreLoadProvider
    {
        double[] GetCoreLoads();
    }
}

