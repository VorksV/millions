using Microsoft.Extensions.DependencyInjection;
using VoltrisOptimizer.Services.Gamer.Implementation;
using VoltrisOptimizer.Services.Gamer.Interfaces;
using VoltrisOptimizer.Services.Gamer.GamerModeManager;
using VoltrisOptimizer.Services.Optimization;
using VoltrisOptimizer.Services.Optimization.Unification;
using VoltrisOptimizer.Services.Gamer.OptimizationModules;
using VoltrisOptimizer.Services.Performance;

namespace VoltrisOptimizer.Services.Gamer
{
    /// <summary>
    /// 🎮 MODO GAMER PROFISSIONAL - SERVIÇOS ESSENCIAIS
    /// Serviços com polling loops removidos (VFE FluidityEngine, AdaptivePowerController, etc.)
    /// </summary>
    public static class GamerServiceExtensions
    {
        public static void AddGamerServices(this IServiceCollection services)
        {
            // ═══════════════════════════════════════════════════════════════
            // SERVIÇOS PRINCIPAIS
            // ═══════════════════════════════════════════════════════════════
            services.AddSingleton<ICpuGamingOptimizer, CpuGamingOptimizerService>();
            services.AddSingleton<IGpuGamingOptimizer, GpuGamingOptimizerService>();
            services.AddSingleton<INetworkGamingOptimizer, NetworkGamingOptimizerService>();
            services.AddSingleton<IMemoryGamingOptimizer, MemoryGamingOptimizerService>();
            services.AddSingleton<IProcessPrioritizer, ProcessPrioritizerService>();
            services.AddSingleton<IHardwareDetector, HardwareDetectorService>();
            services.AddSingleton<ITimerResolutionService, TimerResolutionService>();
            services.AddSingleton<TimerResolutionManager>();
            services.AddSingleton<IRealGameBoosterService, RealGameBoosterService>();
            
            // ═══════════════════════════════════════════════════════════════
            // DETECÇÃO DE JOGOS
            // ═══════════════════════════════════════════════════════════════
            services.AddSingleton<IGameDetector, GameDetectorService>();
            services.AddSingleton<IGameLibraryService, GameLibraryService>();
            services.AddSingleton<IGameProfileService, GameProfileService>();
            
            // ═══════════════════════════════════════════════════════════════
            // ORQUESTRADOR PRINCIPAL (V1 RESTAURADO - com stubs para loops)
            // ═══════════════════════════════════════════════════════════════
            services.AddSingleton<GamerStateMemory>();
            services.AddSingleton<VoltrisOptimizer.Services.Performance.HpetController>();
            services.AddSingleton<WallpaperSlideshowModule>();
            services.AddSingleton<UwpBackgroundAppsModule>();
            services.AddSingleton<VisualEffectsOptimizer>();
            services.AddSingleton<IImmersiveGamingOptimizer, ImmersiveEnvironmentService>();
            
            // ═══════════════════════════════════════════════════════════════
            // NOTA: GamerModeOrchestratorV2 removido - V1 restaurado como principal
            // FluidityEngine (VFE) removido - stubs seguros para serviços com loops
            // ═══════════════════════════════════════════════════════════════
            
            // ═══════════════════════════════════════════════════════════════
            // OVERLAY OSD
            // ═══════════════════════════════════════════════════════════════
            services.AddSingleton<VoltrisOptimizer.Services.Gamer.Overlay.Interfaces.IOverlayService, 
                VoltrisOptimizer.Services.Gamer.Overlay.Implementation.OverlayService>();
            services.AddSingleton<VoltrisOptimizer.Services.Gamer.Overlay.Interfaces.IMetricsCollector, 
                VoltrisOptimizer.Services.Gamer.Overlay.Implementation.MetricsCollector>();
        }
    }
}