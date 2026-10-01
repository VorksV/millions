using System;
using VoltrisOptimizer.Services.Performance.CpuTuning.Core.Models;
using VoltrisOptimizer.Services.Performance.CpuTuning.Models;

namespace VoltrisOptimizer.Services.Performance.CpuTuning.Core.Interfaces
{
    public interface IHardwareBackend
    {
        bool Initialize();
        void Shutdown();
        bool IsDriverLoaded();
        bool ReadMsr(uint index, out ulong value);
        bool WriteMsr(uint index, ulong value);
        CpuVendor GetCpuVendor();
        CpuGeneration DetectGeneration();
        bool SupportsPowerLimitControl();
        bool GetCurrentPowerLimits(out PowerLimitState limits);
        bool SetPowerLimits(PowerLimitRequest request);
        BackendStatus GetStatus();

        // MMIO Sync Support (Sync MMIO — mesma funcionalidade do ThrottleStop)
        bool ReadPciConfig(uint pciAddress, uint regAddress, out uint value);
        ulong DiscoverMchBar();
        bool ReadPhysicalMemory(ulong address, uint size, out ulong value);
        bool WritePhysicalMemory(ulong address, ulong data, uint size);
    }
}
