using System;
using Microsoft.Extensions.DependencyInjection;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer
{
    /// <summary>
    /// Fábrica para criação dos serviços de otimização de troca de contexto
    /// </summary>
    public static class AppSwitchOptimizationFactory
    {
        /// <summary>
        /// Registra todos os serviços de otimização de app-switching no container de DI
        /// </summary>
        public static void AddAppSwitchOptimizationServices(this IServiceCollection services)
        {
            // Serviços principais
            services.AddSingleton<OptimizationModules.RollbackRegistry>();
            services.AddSingleton<ContextSwitchDetectorService>();
            services.AddSingleton<ResourcePreAllocatorService>();
            // ❌ REMOVIDO: IntelligentBackgroundSuspender desativado (V2 Architecture - zero loops)
            services.AddSingleton<PriorityCacheService>();
            // ❌ REMOVIDO: InputLatencyMonitorService desativado (V2 Architecture - zero loops)
            services.AddSingleton<SystemNotificationBlockerService>();
            services.AddSingleton<LoadingPhaseOptimizerService>();
            services.AddSingleton<AppSwitchOptimizationCoordinator>();
        }
        
        /// <summary>
        /// Cria instância do coordenador de otimização com todas as dependências
        /// </summary>
        public static AppSwitchOptimizationCoordinator CreateAppSwitchCoordinator(IServiceProvider serviceProvider)
        {
            ILoggingService? logger = null;
            try
            {
                logger = serviceProvider.GetService<ILoggingService>();
                if (logger == null)
                    throw new InvalidOperationException("ILoggingService não disponível");
                
                return new AppSwitchOptimizationCoordinator(
                    logger,
                    serviceProvider.GetRequiredService<ContextSwitchDetectorService>(),
                    serviceProvider.GetRequiredService<ResourcePreAllocatorService>(),
                    serviceProvider.GetRequiredService<PriorityCacheService>(),
                    serviceProvider.GetRequiredService<SystemNotificationBlockerService>(),
                    serviceProvider.GetRequiredService<LoadingPhaseOptimizerService>());
            }
            catch (Exception ex)
            {
                logger?.LogError($"Falha ao criar AppSwitchCoordinator: {ex.Message}");
                throw;
            }
        }
    }
}
