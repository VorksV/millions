using VoltrisOptimizer.Utils;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Benchmark
{
    /// <summary>
    /// Serviço central de benchmark e score de performance.
    /// Coleta dados sem overhead, sem SafePerformanceCounter, sem WMI contínuo.
    /// 100% assíncrono, cancelável, com fallback.
    /// </summary>
    public interface IPerformanceBenchmarkService
    {
        /// <summary>Snapshot atual do sistema (coletado sob demanda).</summary>
        PerformanceSnapshot? CurrentSnapshot { get; }

        /// <summary>Score estrutural (EPP, HAGS, Timer) — persistente entre sessões.</summary>
        int StructuralScore { get; }

        /// <summary>Score dinâmico (RAM, CPU, processos) — varia durante a sessão.</summary>
        int DynamicScore { get; }

        /// <summary>Score total (estrutural + dinâmico), suavizado.</summary>
        int TotalScore { get; }

        /// <summary>Baseline coletado no startup.</summary>
        PerformanceSnapshot? StartupBaseline { get; }

        /// <summary>Impacto da última otimização (antes/depois).</summary>
        OptimizationImpact? LastOptimizationImpact { get; }

        /// <summary>Service lifecycle.</summary>
        Task StartAsync(CancellationToken ct = default);
        Task StopAsync();

        /// <summary>
        /// Marca o início de uma otimização. Coleta snapshot BEFORE.
        /// </summary>
        void BeginOptimization();

        /// <summary>
        /// Marca o fim de uma otimização. Calcula impacto.
        /// </summary>
        void EndOptimization(long durationMs);
    }
}

