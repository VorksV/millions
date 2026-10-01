using Microsoft.Extensions.DependencyInjection;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.Gamer.GamerModeManager;

namespace VoltrisOptimizer.Services.Gamer.OptimizationModules
{
    /// <summary>
    /// Extensões para registro de serviços de otimização gamer no DI container
    /// </summary>
    public static class GamerOptimizationModuleExtensions
    {
        /// <summary>
        /// Registra todos os serviços de otimização gamer temporários
        /// </summary>
        public static IServiceCollection AddGamerOptimizationModules(this IServiceCollection services)
        {
            // Registrar módulos individuais como transientes (podem ser múltiplas instâncias)
            services.AddTransient<ProcessCountOffloadModule>();
            services.AddTransient<LatencyReductionModule>();
            services.AddTransient<FrameRateOptimizationModule>();
            
            // Registrar como IGamerOptimizationModule também
            services.AddTransient<IGamerOptimizationModule, ProcessCountOffloadModule>();
            services.AddTransient<IGamerOptimizationModule, LatencyReductionModule>();
            services.AddTransient<IGamerOptimizationModule, FrameRateOptimizationModule>();

            // Registrar array de módulos (para injeção no GamerSessionManager)
            services.AddSingleton<IGamerOptimizationModule[]>(sp =>
            {
                return new IGamerOptimizationModule[]
                {
                    sp.GetRequiredService<ProcessCountOffloadModule>(),
                    sp.GetRequiredService<LatencyReductionModule>(),
                    sp.GetRequiredService<FrameRateOptimizationModule>()
                };
            });

            // Registrar GamerSessionManager
            services.AddSingleton<GamerSessionManager>(sp =>
            {
                var logger = sp.GetRequiredService<ILoggingService>();
                var powerPlan = sp.GetRequiredService<IPowerPlanService>();
                var processService = sp.GetRequiredService<IProcessOptimizationService>();
                var registry = sp.GetRequiredService<IRegistryService>();
                var modules = sp.GetRequiredService<IGamerOptimizationModule[]>();

                return new GamerSessionManager(logger, powerPlan, processService, registry, modules);
            });

            return services;
        }
    }
}

