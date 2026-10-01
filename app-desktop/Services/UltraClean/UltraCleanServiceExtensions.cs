using Microsoft.Extensions.DependencyInjection;
using VoltrisOptimizer.Services.UltraClean.Modules.System;

namespace VoltrisOptimizer.Services.UltraClean
{
    public static class UltraCleanServiceExtensions
    {
        public static IServiceCollection AddUltraCleanModules(this IServiceCollection services)
        {
            services.AddSingleton<IUltraCleanModule, PrefetchModule>();
            services.AddSingleton<IUltraCleanModule, UserTempModule>();
            services.AddSingleton<IUltraCleanModule, WindowsUpdateModule>();
            services.AddSingleton<IUltraCleanModule, ErrorReportingModule>();
            services.AddSingleton<IUltraCleanModule, WindowsBackupModule>();
            services.AddSingleton<IUltraCleanModule, EventLogsModule>();
            // Add more modules here as they are refactored
            
            return services;
        }
    }
}
