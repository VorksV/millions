using System;

namespace VoltrisOptimizer.Services.Benchmark
{
    /// <summary>
    /// Snapshot imutável do estado do sistema em um momento.
    /// Coletado sem overhead significativo (APIs nativas + cache existente).
    /// </summary>
    public sealed class PerformanceSnapshot
    {
        public DateTime TimestampUtc { get; init; }
        public double TimerResolutionMs { get; init; } = 15.6;
        public int Epp { get; init; } = 50;
        public double AvailableRamGb { get; init; }
        public double TotalRamGb { get; init; }
        public int ProcessCount { get; init; }
        public double DiskQueueLength { get; init; }
        public bool HagsEnabled { get; init; }
        public string PowerPlanName { get; init; } = "Balanced";
        public int CpuUsagePercent { get; init; }
        public int GpuUsagePercent { get; init; }
        public bool IsLaptop { get; init; }
        public bool HasSsd { get; init; }
        public long BootUptimeMs { get; init; }

        public double AvailableRamPercent => TotalRamGb > 0 ? AvailableRamGb / TotalRamGb * 100.0 : 0;
    }

    /// <summary>
    /// Baseline persistido entre sessões (score ESTRUTURAL apenas).
    /// Salvo em disco para evitar reset brusco na percepção do usuário.
    /// </summary>
    public sealed class StructuralBaseline
    {
        public int StructuralScore { get; set; } = 50;
        public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
        public int BestStructuralScore { get; set; } = 50;
        public int TotalOptimizationsPerformed { get; set; }
        public DateTime FirstRunUtc { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Resultado de uma otimização — usado pelo OptimizationImpactAnalyzer.
    /// </summary>
    public sealed class OptimizationImpact
    {
        public int TimerBeforeMs { get; init; }
        public int TimerAfterMs { get; init; }
        public int EppBefore { get; init; }
        public int EppAfter { get; init; }
        public double RamBeforeGb { get; init; }
        public double RamAfterGb { get; init; }
        public int ProcessesBefore { get; init; }
        public int ProcessesAfter { get; init; }
        public bool HagsChanged { get; init; }
        public bool GamingModeChanged { get; init; }
        public long DurationMs { get; init; }

        public int StructuralGain =>
            (TimerBeforeMs > TimerAfterMs ? (TimerBeforeMs - TimerAfterMs) / 2 : 0)
            + (EppBefore > EppAfter ? (EppBefore - EppAfter) / 5 : 0)
            + (HagsChanged ? 10 : 0);

        public int DynamicGain =>
            (int)Math.Max(0, (RamAfterGb - RamBeforeGb) * 2)
            + Math.Max(0, (ProcessesBefore - ProcessesAfter) / 3);
    }
}
