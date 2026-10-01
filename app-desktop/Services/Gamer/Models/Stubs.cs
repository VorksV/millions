using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core.Brain.V2;
using VoltrisOptimizer.Core.Body;

namespace VoltrisOptimizer.Services.Gamer.Models
{
    // STUBS PARA COMPILAÇÃO - Tipos removidos durante refatoração do VoltrisBrain
    
    public interface IOptimizationState : IDisposable
    {
        Task<bool> RestoreAsync(CancellationToken cancellationToken = default);
        Task<bool> RollbackAsync(CancellationToken cancellationToken = default);
    }

    public class OptimizationState : IOptimizationState
    {
        public Task<bool> RestoreAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public Task<bool> RollbackAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public void Dispose()
        {
        }
    }

    public enum OptimizationType
    {
        Cpu,
        Gpu,
        Network,
        Storage,
        Memory,
        Cleanup,
        Performance,
        Gaming,
        Full
    }

    public class UnifiedOptimizationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public OptimizationType Type { get; set; }
        public long SpaceFreed { get; set; }
        public double PerformanceGain { get; set; }
        public List<string> OptimizationsApplied { get; set; } = new();
        public UnifiedOptimizationResult OptimizationResult { get; set; } = new();
        public TimeSpan Duration { get; set; }
    }

    public class IntelligentOptimizationResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public UnifiedOptimizationResult OptimizationResult { get; set; } = new();
        public UnifiedOptimizationResult Result { get; set; } = new();
    }

    public class OptimizationContext
    {
        public bool IsGamingMode { get; set; }
        public bool IsOnBattery { get; set; }
        public bool IsBatteryPowered { get; set; }
        public bool AllowAdvancedTweaks { get; set; }
        public Dictionary<string, object> CustomParameters { get; set; } = new();
    }

    // Stubs adicionais para Core.Optimization

    // Stubs para RealIntelligenceEngine
    public class RealIntelligenceEngine
    {
    }
}
