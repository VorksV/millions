using Microsoft.Extensions.DependencyInjection;
using VoltrisOptimizer.Services.Gamer.OptimizationModules;
using VoltrisOptimizer.Services.Gamer.Interfaces;
using VoltrisOptimizer.Services.Thermal;

namespace VoltrisOptimizer.Services.Gamer.GamerModeManager
{
    /// <summary>
    /// Extensões de DI para o GamerModeManager
    /// </summary>
    public static class GamerModeServiceExtensions
    {
        /// <summary>
        /// Registra todos os serviços do GamerModeManager
        /// </summary>
        public static IServiceCollection AddGamerModeManager(this IServiceCollection services)
        {
            // Serviços internos
            services.AddSingleton<IPowerPlanService, PowerPlanService>();
            
            // Registrar módulos de otimização gamer temporários
            services.AddGamerOptimizationModules();
            services.AddSingleton<IGpuOptimizationService, GpuOptimizationService>();
            
            // OTIMIZAÇÃO: Usar o serviço térmico global centralizado em vez de instanciar um local redundante
            services.AddSingleton<IThermalMonitorService>(sp => 
                sp.GetRequiredService<VoltrisOptimizer.Services.Thermal.IGlobalThermalMonitorService>());
                
            services.AddSingleton<IGameDetectionService, GameDetectionService>();
            services.AddSingleton<IProcessOptimizationService, ProcessOptimizationService>();
            
            // Manager principal - injetar o GameDetectionService compartilhado
            //
            // [FIX:UNICO-DONO-DE-ENERGIA] O Modo Gamer não recebe mais o
            // orquestrador de planos. A energia passou a ter um dono só — o
            // Perfil Inteligente — e o Modo Gamer é uma das duas portas de
            // entrada dele, não um competidor. A porta legítima é
            // ProfilePowerAuthority.RequestProfile, que muda o PERFIL; o plano
            // vem como consequência, nunca como decisão de quem pede.
            services.AddSingleton<IGamerModeManager>(provider =>
                new GamerModeManager(
                    provider.GetRequiredService<ILoggingService>(),
                    provider.GetRequiredService<IGameDetectionService>(),
                    provider.GetRequiredService<IThermalMonitorService>()
                ));
            
            return services;
        }
    }
}

