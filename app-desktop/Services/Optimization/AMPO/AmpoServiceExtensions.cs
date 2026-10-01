using Microsoft.Extensions.DependencyInjection;

namespace VoltrisOptimizer.Services.Optimization.AMPO
{
    /// <summary>
    /// Extensões para registro do AMPO no container DI
    /// </summary>
    public static class AmpoServiceExtensions
    {
        public static IServiceCollection AddAmpoServices(this IServiceCollection services)
        {
            services.AddSingleton<IAdaptiveMemoryProcessOptimizer, AdaptiveMemoryProcessOptimizer>();
            return services;
        }
    }
}

