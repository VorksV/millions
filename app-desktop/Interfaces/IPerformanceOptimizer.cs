using System;
using System.Threading.Tasks;
using VoltrisOptimizer.Core;

namespace VoltrisOptimizer.Interfaces
{
    /// <summary>
    /// Interface para serviço de otimização de desempenho
    /// TODAS as operações retornam OperationResult com validação obrigatória
    /// </summary>
    public interface IPerformanceOptimizer
    {
        Task<OperationResult> SetHighPerformancePlanAsync(Action<int>? progressCallback = null);
        Task<OperationResult> SetBalancedPlanAsync(Action<int>? progressCallback = null);
        Task<OperationResult> OptimizeStartupAsync(Action<int>? progressCallback = null);
        Task<OperationResult> OptimizeServicesAsync(Action<int>? progressCallback = null);
        Task<OperationResult> OptimizeRAMAsync(Action<int>? progressCallback = null);
    }
}
