using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Performance.Models;
namespace VoltrisOptimizer.Services.Performance
{
    /// <summary>
    /// Interface para serviços de otimização de performance
    /// Abstrai o UltraPerformanceService para permitir injeção de dependência
    /// </summary>
    public interface IPerformanceOptimizationService
    {
        /// <summary>
        /// Detecta o perfil do sistema
        /// </summary>
        PerformanceSystemProfile DetectSystemProfile();

        /// <summary>
        /// Obtém categorias de otimização disponíveis
        /// </summary>
        IReadOnlyList<PerformanceCategory> GetOptimizationCategories();

        /// <summary>
        /// Aplica otimizações recomendadas (baseado em IsRecommended + compatibilidade de hardware)
        /// </summary>
        Task<PerformanceOptimizationResult> ApplyRecommendedOptimizationsAsync(CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Aplica apenas as otimizações selecionadas pelo usuário (por nome), respeitando compatibilidade de hardware
        /// </summary>
        Task<PerformanceOptimizationResult> ApplyOptimizationsAsync(IEnumerable<string> selectedOptimizationNames, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Reverte todas as otimizações aplicadas
        /// </summary>
        Task<PerformanceOptimizationResult> RevertAllOptimizationsAsync(CancellationToken cancellationToken = default);
    }
}

