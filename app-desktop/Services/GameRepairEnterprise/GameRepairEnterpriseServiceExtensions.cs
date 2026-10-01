using Microsoft.Extensions.DependencyInjection;
using VoltrisOptimizer.Services.GameRepairEnterprise.Engine;
using VoltrisOptimizer.Services.GameRepairEnterprise.Interfaces;
using VoltrisOptimizer.Services.GameRepairEnterprise.Providers;

namespace VoltrisOptimizer.Services.GameRepairEnterprise
{
    public static class GameRepairEnterpriseServiceExtensions
    {
        public static IServiceCollection AddGameRepairEnterprise(this IServiceCollection services)
        {
            // Core Engine & Services
            services.AddSingleton<ISecureDownloadService, SecureDownloadService>();
            services.AddSingleton<IEnterpriseGameRepairEngine, EnterpriseGameRepairEngine>();

            // Providers
            services.AddSingleton<IGameDependencyProvider, VisualCppProvider>();
            services.AddSingleton<IGameDependencyProvider, DirectXProvider>();
            services.AddSingleton<IGameDependencyProvider, VulkanProvider>();
            services.AddSingleton<IGameDependencyProvider, OpenGLProvider>();
            services.AddSingleton<IGameDependencyProvider, OpenALProvider>();
            services.AddSingleton<IGameDependencyProvider, PhysXLegacyProvider>();
            services.AddSingleton<IGameDependencyProvider, DotNetFrameworkProvider>();

            return services;
        }
    }
}
