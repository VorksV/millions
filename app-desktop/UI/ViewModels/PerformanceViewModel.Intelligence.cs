using System;
using System.Threading.Tasks;

namespace VoltrisOptimizer.UI.ViewModels
{
    /// <summary>
    /// Partial class for PerformanceViewModel - Intelligent Performance Methods
    /// </summary>
    public partial class PerformanceViewModel
    {
        /// <summary>
        /// Applies the current intelligent recommendation.
        /// </summary>
        private async Task ApplyIntelligentRecommendationAsync()
        {
            if (_intelligentCoordinator == null || !HasIntelligentRecommendation || IsOptimizing)
                return;
                
            IsOptimizing = true;
            OptimizationStatus = "Aplicando recomendação inteligente...";
            
            try
            {
                _logger?.LogInfo("[PerformanceVM] 🚀 Applying intelligent recommendation");
                
                var result = await _intelligentCoordinator.ExecuteRecommendationAsync();
                
                if (result.Success)
                {
                    OptimizationStatus = $"✅ {result.Message}";
                    HasIntelligentRecommendation = false;
                    _logger?.LogSuccess($"[PerformanceVM] Intelligent recommendation applied: {result.Message}");
                }
                else
                {
                    OptimizationStatus = $"❌ {result.Message}";
                    _logger?.LogError($"[PerformanceVM] Failed to apply recommendation: {result.Message}");
                }
            }
            catch (Exception ex)
            {
                OptimizationStatus = $"❌ Erro: {ex.Message}";
                _logger?.LogError("[PerformanceVM] Error applying intelligent recommendation", ex);
            }
            finally
            {
                IsOptimizing = false;
            }
        }
    }
}
