using System;
using System.Collections.Generic;
using System.Linq;
using VoltrisOptimizer.Services.Performance.Decision.Models;
using VoltrisOptimizer.Services.Performance.Models;

namespace VoltrisOptimizer.Services.Performance.Decision
{
    /// <summary>
    /// Simple rule-based decision engine.
    /// Deterministic, stateless, no side effects.
    /// Uses simple heuristics to decide optimization strategy.
    /// </summary>
    public class RuleBasedDecisionEngine : IPerformanceDecisionEngine
    {
        public PerformanceDecision Decide(PerformanceContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            // Rule 1: If system is idle with low resource usage, no action needed
            if (context.CpuUsagePercent < 5 && context.GpuUsagePercent < 5 && !context.IsGameRunning)
            {
                return PerformanceDecision.NoAction(
                    context.ContextId,
                    "System is idle with low resource usage");
            }

            // Rule 2: If on battery (laptop), use conservative approach
            if (context.IsOnBattery)
            {
                return CreateBatteryOptimizationDecision(context);
            }

            // Rule 3: If game is running, enable gaming optimizations
            if (context.IsGameRunning)
            {
                return CreateGamingDecision(context);
            }

            // Rule 4: If under heavy load, enable dynamic balancing
            if (context.CpuUsagePercent > 70 || context.MemoryUsagePercent > 80)
            {
                return CreateHeavyLoadDecision(context);
            }

            // Default: Light optimizations for general use
            return CreateGeneralOptimizationDecision(context);
        }

        private PerformanceDecision CreateBatteryOptimizationDecision(PerformanceContext context)
        {
            var targets = new OptimizationTargets
            {
                AllowAny = true,
                ServiceQoS = true
            };

            return new PerformanceDecision(
                context.ContextId,
                DecisionType.ApplySystemTweaks,
                DecisionIntensity.Light,
                DecisionConfidence.High,
                "System is running on battery power",
                new[] { "Laptop detected", "Battery mode active" },
                targets,
                DecisionPriority.High);
        }

        private PerformanceDecision CreateGamingDecision(PerformanceContext context)
        {
            var intensity = DetermineGamingIntensity(context);
            var targets = new OptimizationTargets
            {
                AllowAny = true,
                CpuPriority = true,
                CpuAffinity = context.HardwareProfile.IsHybrid,
                TimerResolution = true,
                ServiceQoS = true,
                LaunchBoost = true,
                EnableDynamicBalancing = true
            };

            var factors = new List<string> { "Game detected and running" };
            if (context.HardwareTier == HardwareTier.HighEnd)
                factors.Add("High-end hardware detected");
            if (context.GpuUsagePercent > 60)
                factors.Add($"High GPU usage ({context.GpuUsagePercent:F0}%)");

            return new PerformanceDecision(
                context.ContextId,
                DecisionType.EnableGamerMode,
                intensity,
                DecisionConfidence.High,
                "Gaming session detected",
                factors,
                targets,
                DecisionPriority.Critical);
        }

        private PerformanceDecision CreateHeavyLoadDecision(PerformanceContext context)
        {
            var targets = new OptimizationTargets
            {
                AllowAny = true,
                EnableDynamicBalancing = true,
                CpuPriority = true
            };

            var factors = new List<string>();
            if (context.CpuUsagePercent > 80)
                factors.Add($"High CPU usage ({context.CpuUsagePercent:F0}%)");
            if (context.MemoryUsagePercent > 85)
                factors.Add($"High memory usage ({context.MemoryUsagePercent:F0}%)");

            return new PerformanceDecision(
                context.ContextId,
                DecisionType.EnableDynamicBalancing,
                DecisionIntensity.Moderate,
                DecisionConfidence.High,
                "System is under heavy load",
                factors,
                targets,
                DecisionPriority.High);
        }

        private PerformanceDecision CreateGeneralOptimizationDecision(PerformanceContext context)
        {
            var targets = new OptimizationTargets
            {
                AllowAny = true,
                TimerResolution = false, // Not needed for general use
                ServiceQoS = true
            };

            return new PerformanceDecision(
                context.ContextId,
                DecisionType.ApplySystemTweaks,
                DecisionIntensity.Light,
                DecisionConfidence.Medium,
                "General usage scenario",
                new[] { "Standard workload", "No specific optimization needed" },
                targets,
                DecisionPriority.Low);
        }

        private DecisionIntensity DetermineGamingIntensity(PerformanceContext context)
        {
            // High-end hardware can handle aggressive optimizations
            if (context.HardwareTier == HardwareTier.HighEnd || context.HardwareTier == HardwareTier.Enthusiast)
                return DecisionIntensity.Aggressive;

            // Low-end hardware needs conservative approach
            if (context.HardwareTier == HardwareTier.LowEnd)
                return DecisionIntensity.Light;

            // Mid-range gets balanced approach
            return DecisionIntensity.Moderate;
        }
    }
}
