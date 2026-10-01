using System;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Responsiveness
{
    /// <summary>
    /// Immediate Responsiveness Engine v2 (IRE v2)
    /// Orquestra impacto imediato no startup e loop adaptativo de responsividade.
    /// Complementa o DSL 5.0 — age ANTES do delay de 2s do DynamicLoadStabilizer.
    /// </summary>
    public interface IImmediateResponsivenessEngine : IDisposable
    {
        bool IsRunning { get; }

        /// <summary>
        /// Executa o Startup Mode: impacto imediato em até 200ms.
        /// Detecta processos pesados e aplica I/O + CPU reduction.
        /// </summary>
        Task RunStartupModeAsync(CancellationToken ct = default);

        /// <summary>
        /// Inicia o Adaptive Loop (2–5s interval, age apenas quando necessário).
        /// </summary>
        Task StartAdaptiveLoopAsync(CancellationToken ct = default);

        /// <summary>
        /// Para o loop adaptativo.
        /// </summary>
        Task StopAsync();

        // === DSL COMMANDS ===

        /// <summary>
        /// DSL: Aplica I/O VeryLow em processo pesado de background.
        /// </summary>
        bool ApplyIoPriority(int pid, IoPriorityTarget target);

        /// <summary>
        /// DSL: Boost no processo foreground atual (CPU AboveNormal + I/O High).
        /// Duração máxima: 2s, sem Realtime.
        /// </summary>
        bool BoostForeground();

        /// <summary>
        /// DSL: Normaliza processos de background (restaura prioridades).
        /// </summary>
        void NormalizeBackground();
    }

    public enum IoPriorityTarget
    {
        VeryLow = 0,
        Low = 1,
        Normal = 2,
        High = 3
    }
}
