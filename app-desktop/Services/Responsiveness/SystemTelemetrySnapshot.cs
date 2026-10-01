using System.Collections.Generic;
using System.Diagnostics;

namespace VoltrisOptimizer.Services.Responsiveness
{
    /// <summary>
    /// Snapshot de telemetria coletado pelo IRE v2.
    /// Separado do SystemState50 para não criar dependência circular com o DSL.
    /// </summary>
    public sealed class SystemTelemetrySnapshot
    {
        public double CpuTotalPercent { get; set; }
        public float DiskQueueLength { get; set; }
        public float DiskActivePercent { get; set; }
        public int ForegroundPid { get; set; }
        public long LastInputMs { get; set; }

        /// <summary>
        /// Processos com CPU% real calculado no intervalo.
        /// Key = PID, Value = CPU% normalizado por core.
        /// </summary>
        public IReadOnlyList<ProcessCpuSample> ProcessSamples { get; set; } = [];

        public bool IsCpuHigh(double threshold) => CpuTotalPercent >= threshold;
        public bool IsDiskContended(float queueThreshold) => DiskQueueLength >= queueThreshold;
        public bool HasRecentInput(long maxMs = 500) => LastInputMs < maxMs;
    }

    public sealed class ProcessCpuSample
    {
        public int Pid { get; init; }
        public string Name { get; init; } = "";
        public double CpuPercent { get; init; }
        public long WorkingSetBytes { get; init; }
        public bool IsForeground { get; init; }
    }
}
