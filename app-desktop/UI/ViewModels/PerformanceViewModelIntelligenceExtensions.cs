using System;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Performance;
using VoltrisOptimizer.Services.Performance.Decision.Models;

namespace VoltrisOptimizer.UI.ViewModels
{
    /// <summary>
    /// Extension methods for PerformanceViewModel to add intelligent analysis capabilities.
    /// </summary>
    public static class PerformanceViewModelIntelligenceExtensions
    {
        /// <summary>
        /// Background loop for intelligent performance analysis.
        /// Runs every 2 minutes, provides recommendations without interfering with manual operations.
        /// </summary>
        public static async Task StartIntelligentAnalysisLoopAsync(
            this PerformanceViewModel viewModel,
            IntelligentPerformanceCoordinator coordinator,
            ILoggingService logger,
            CancellationToken cancellationToken = default)
        {
            if (coordinator == null)
                return;
                
            logger?.LogInfo("[PerformanceVM] 🧠 Starting intelligent analysis loop");
            
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Skip analysis if user is actively optimizing
                    if (viewModel.IsOptimizing)
                    {
                        await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
                        continue;
                    }
                    
                    // Analyze system state
                    var recommendation = await coordinator.AnalyzeAsync();
                    
                    // Update UI on dispatcher thread (não-bloqueante)
                    await System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        UpdateIntelligentRecommendationUI(viewModel, recommendation, logger);
                    });
                    
                    // Wait 2 minutes before next analysis
                    await Task.Delay(TimeSpan.FromMinutes(2), cancellationToken);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    logger?.LogError("[PerformanceVM] Error in intelligent analysis loop", ex);
                    try { await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }
        
        /// <summary>
        /// Updates UI with intelligent recommendation.
        /// </summary>
        private static void UpdateIntelligentRecommendationUI(
            PerformanceViewModel viewModel,
            PerformanceRecommendation recommendation,
            ILoggingService logger)
        {
            if (recommendation.Decision.Type == DecisionType.NoAction)
            {
                viewModel.IntelligentRecommendation = LocalizationService.Instance.GetString("PerfSystemOptimizedNoAction");
                viewModel.HasIntelligentRecommendation = false;
                viewModel.RecommendationIcon = "✅";
            }
            else if (recommendation.ShouldAct)
            {
                var decisionType = recommendation.Decision.Type.ToString();
                var confidence = recommendation.Decision.Confidence.ToString();
                var intensity = recommendation.Decision.Intensity.ToString();
                
                viewModel.IntelligentRecommendation = string.Format(
                    LocalizationService.Instance.GetString("PerfRecText"),
                    GetFriendlyDecisionName(recommendation.Decision.Type),
                    intensity,
                    confidence,
                    recommendation.Reasoning);
                viewModel.HasIntelligentRecommendation = true;
                viewModel.RecommendationIcon = GetRecommendationIcon(recommendation.Decision.Type);
                
                logger?.LogInfo($"[PerformanceVM] 💡 New recommendation: {decisionType} (Confidence: {confidence})");
            }
            else
            {
                viewModel.IntelligentRecommendation = $"ℹ️ {recommendation.Reasoning}";
                viewModel.HasIntelligentRecommendation = false;
                viewModel.RecommendationIcon = "ℹ️";
            }
        }
        
        private static string GetFriendlyDecisionName(DecisionType type)
        {
            return type switch
            {
                DecisionType.EnableGamerMode => LocalizationService.Instance.GetString("PerfDecisionEnableGamerMode"),
                DecisionType.EnableDynamicBalancing => LocalizationService.Instance.GetString("PerfDecisionEnableDynamicBalancing"),
                DecisionType.ApplySystemTweaks => LocalizationService.Instance.GetString("PerfDecisionApplySystemTweaks"),
                DecisionType.ReduceIntensity => LocalizationService.Instance.GetString("PerfDecisionReduceIntensity"),
                DecisionType.Revert => LocalizationService.Instance.GetString("PerfDecisionRevert"),
                _ => type.ToString()
            };
        }
        
        private static string GetRecommendationIcon(DecisionType type)
        {
            return type switch
            {
                DecisionType.EnableGamerMode => "🎮",
                DecisionType.EnableDynamicBalancing => "⚖️",
                DecisionType.ApplySystemTweaks => "🔧",
                DecisionType.ReduceIntensity => "🔽",
                DecisionType.Revert => "🔄",
                _ => "💡"
            };
        }
    }
}
