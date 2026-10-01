using Microsoft.Extensions.DependencyInjection;
using VoltrisOptimizer.Services.Thermal;

namespace VoltrisOptimizer.Services
{
    /// <summary>
    /// Extensões para registro de serviços principais do VoltrisOptimizer
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registra todos os serviços principais do VoltrisOptimizer
        /// </summary>
        public static IServiceCollection AddVoltrisServices(this IServiceCollection services)
        {
            // 🔥 CRÍTICO: GlobalThermalMonitorService - ESSENCIAL PARA TEMPERATURA EM TEMPO REAL
            services.AddSingleton<IGlobalThermalMonitorService, GlobalThermalMonitorService>();
            
            return services;
        }
    }
}
