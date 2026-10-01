using System;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Prediction
{
    /// <summary>
    /// Interface para o motor preditivo anti-stutter.
    /// </summary>
    public interface IPAEEngine
    {
        /// <summary>
        /// Inicia o monitoramento e predição de stutter.
        /// </summary>
        /// <returns>Tarefa assíncrona.</returns>
        Task StartAsync();

        /// <summary>
        /// Para o monitoramento e predição de stutter.
        /// </summary>
        /// <returns>Tarefa assíncrona.</returns>
        Task StopAsync();

        /// <summary>
        /// Força uma atualização das predições.
        /// </summary>
        /// <returns>Tarefa assíncrona.</returns>
        Task ForcePredictionUpdateAsync();

        /// <summary>
        /// Evento disparado quando uma possível stutter é predita.
        /// </summary>
        event EventHandler<StutterPredictedEventArgs> StutterPredicted;

        /// <summary>
        /// Evento disparado quando uma otimização é aplicada.
        /// </summary>
        event EventHandler<OptimizationAppliedEventArgs> OptimizationApplied;
    }

    /// <summary>
    /// Argumentos para evento de stutter predita.
    /// </summary>
    public class StutterPredictedEventArgs : EventArgs
    {
        /// <summary>
        /// Probabilidade da stutter ocorrer (0.0 a 1.0).
        /// </summary>
        public double Probability { get; }

        /// <summary>
        /// Tempo estimado até a stutter ocorrer em milissegundos.
        /// </summary>
        public int EstimatedTimeToStutterMs { get; }

        /// <summary>
        /// Fatores que contribuíram para a predição.
        /// </summary>
        public StutterFactors Factors { get; }

        /// <summary>
        /// Inicializa uma nova instância de <see cref="StutterPredictedEventArgs"/>.
        /// </summary>
        /// <param name="probability">Probabilidade da stutter.</param>
        /// <param name="estimatedTimeToStutterMs">Tempo estimado até a stutter.</param>
        /// <param name="factors">Fatores da predição.</param>
        public StutterPredictedEventArgs(double probability, int estimatedTimeToStutterMs, StutterFactors factors)
        {
            Probability = probability;
            EstimatedTimeToStutterMs = estimatedTimeToStutterMs;
            Factors = factors;
        }
    }

    /// <summary>
    /// Fatores que contribuem para uma possível stutter.
    /// </summary>
    public class StutterFactors
    {
        /// <summary>
        /// Uso elevado de CPU.
        /// </summary>
        public bool HighCpuUsage { get; set; }

        /// <summary>
        /// Uso elevado de GPU.
        /// </summary>
        public bool HighGpuUsage { get; set; }

        /// <summary>
        /// Picos de uso de memória.
        /// </summary>
        public bool MemorySpikes { get; set; }

        /// <summary>
        /// Atividade intensa de I/O.
        /// </summary>
        public bool HighIoActivity { get; set; }

        /// <summary>
        /// Syscalls bloqueantes.
        /// </summary>
        public bool BlockingSyscalls { get; set; }

        /// <summary>
        /// Cache misses elevados.
        /// </summary>
        public bool HighCacheMisses { get; set; }

        /// <summary>
        /// Context switches excessivos.
        /// </summary>
        public bool ExcessiveContextSwitches { get; set; }
    }

    /// <summary>
    /// Argumentos para evento de otimização aplicada.
    /// </summary>
    public class OptimizationAppliedEventArgs : EventArgs
    {
        /// <summary>
        /// Tipo de otimização aplicada.
        /// </summary>
        public OptimizationType Type { get; }

        /// <summary>
        /// Descrição da otimização.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Ganho esperado em milissegundos.
        /// </summary>
        public int ExpectedGainMs { get; }

        /// <summary>
        /// Inicializa uma nova instância de <see cref="OptimizationAppliedEventArgs"/>.
        /// </summary>
        /// <param name="type">Tipo de otimização.</param>
        /// <param name="description">Descrição da otimização.</param>
        /// <param name="expectedGainMs">Ganho esperado.</param>
        public OptimizationAppliedEventArgs(OptimizationType type, string description, int expectedGainMs)
        {
            Type = type;
            Description = description;
            ExpectedGainMs = expectedGainMs;
        }
    }

    /// <summary>
    /// Tipos de otimização que podem ser aplicadas.
    /// </summary>
    public enum OptimizationType
    {
        /// <summary>
        /// Prefetch de recursos.
        /// </summary>
        ResourcePrefetch,

        /// <summary>
        /// Ajuste de prioridade de threads.
        /// </summary>
        ThreadPriorityAdjustment,

        /// <summary>
        /// Otimização de alocação de memória.
        /// </summary>
        MemoryAllocationOptimization,

        /// <summary>
        /// Preparação de contexto da GPU.
        /// </summary>
        GpuContextPreparation,

        /// <summary>
        /// Otimização de I/O.
        /// </summary>
        IoOptimization,

        /// <summary>
        /// Redução de latência de sistema.
        /// </summary>
        SystemLatencyReduction
    }
}
