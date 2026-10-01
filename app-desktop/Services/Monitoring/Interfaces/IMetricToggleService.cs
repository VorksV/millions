using System;
using System.Collections.Generic;

namespace VoltrisOptimizer.Services.Monitoring.Interfaces
{
    public enum MetricType
    {
        CpuUsage,
        CpuTemperature,
        CpuClock,
        GpuUsage,
        GpuTemperature,
        GpuClock,
        GpuMemoryClock,
        RamUsage,
        VramUsage,
        DiskUsage,
        Fps,
        FrameTime,
        InputLatency
    }

    public interface IMetricToggleService
    {
        bool IsEnabled(MetricType metric);
        void SetEnabled(MetricType metric, bool enabled);
        event EventHandler<MetricType> MetricToggled;
        IReadOnlyDictionary<MetricType, bool> GetAllStates();
        bool IsAnyGpuMetricEnabled { get; }
        bool IsAnyCpuMetricEnabled { get; }
        bool IsAnyMemoryMetricEnabled { get; }
        bool IsFpsMetricEnabled { get; }
    }
}
