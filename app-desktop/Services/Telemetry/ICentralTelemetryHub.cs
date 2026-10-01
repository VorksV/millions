using System;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Telemetry
{
    public interface ICentralTelemetryHub
    {
        // Static Hardware/OS Info (Cached indefinitely)
        string CpuName { get; }
        int CpuCores { get; }
        int CpuLogicalProcessors { get; }
        uint CpuMaxClockSpeed { get; }

        string GpuName { get; }
        ulong GpuAdapterRam { get; }

        ulong TotalPhysicalMemory { get; }
        uint RamSpeed { get; }

        string OsCaption { get; }
        int ChassisType { get; }
        
        bool IsLaptop { get; }

        // Dynamic Telemetry (Updated periodically)
        double CpuUsagePercent { get; }
        double DiskUsagePercent { get; }
        double GpuUsagePercent { get; }
        ulong AvailablePhysicalMemory { get; }

        // Control
        void StartDynamicTelemetry();
        void StopDynamicTelemetry();
    }
}
