using System;

namespace VoltrisOptimizer.Services.HardwareTelemetry
{
    /// <summary>
    /// Representa um snapshot imutável e thread-safe de todo o hardware do sistema.
    /// </summary>
    public sealed record HardwareSnapshot
    {
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;

        // CPU Metrics
        public double CpuTemperature { get; init; } = double.NaN;
        public double CpuUsage { get; init; } = double.NaN;
        public double CpuClock { get; init; } = double.NaN;
        public double CpuPackagePower { get; init; } = double.NaN;
        public double CpuVoltage { get; init; } = double.NaN;

        // GPU Metrics
        public double GpuTemperature { get; init; } = double.NaN;
        public double GpuUsage { get; init; } = double.NaN;
        public double GpuCoreClock { get; init; } = double.NaN;
        public double GpuMemoryClock { get; init; } = double.NaN;
        public double GpuPower { get; init; } = double.NaN;
        public double GpuMemoryUsed { get; init; } = double.NaN;
        public double GpuMemoryTotal { get; init; } = double.NaN;

        // Memory Metrics
        public double RamUsage { get; init; } = double.NaN;
        public double RamAvailable { get; init; } = double.NaN;

        // Storage Metrics
        public double SsdTemperature { get; init; } = double.NaN;

        // Motherboard
        public double MotherboardTemperature { get; init; } = double.NaN;
        public double FanRpm { get; init; } = double.NaN;

        public static HardwareSnapshot Empty => new HardwareSnapshot();
    }
}
