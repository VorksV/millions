using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Uninstaller
{
    /// <summary>
    /// Motor de desinstalação profissional com Install Trace Engine integrado
    /// </summary>
    public class UninstallEngine
    {
        private readonly ILoggingService _logger;
        private readonly RegistryScanner _registryScanner;
        private readonly InstallTraceEngine _traceEngine;

        public UninstallEngine(ILoggingService logger)
        {
            _logger = logger;
            _registryScanner = new RegistryScanner(logger);
            _traceEngine = new InstallTraceEngine(logger);
        }

        /// <summary>
        /// Obter lista de programas instalados
        /// </summary>
        public List<AppInfo> GetInstalledPrograms()
        {
            return _registryScanner.ScanInstalledApps();
        }

        /// <summary>
        /// Desinstalação avançada com Install Trace Engine
        /// Remove completamente o aplicativo incluindo todos os resíduos
        /// </summary>
        public async Task<bool> PerformAdvancedUninstall(AppInfo app, bool forceRemoval = false)
        {
            try
            {
                var result = await _traceEngine.PerformAdvancedUninstall(app, forceRemoval);
                return result.Success;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erro na desinstalação avançada de {app.Name}", ex);
                return false;
            }
        }

        /// <summary>
        /// Desinstalação profissional (método legado mantido para compatibilidade)
        /// Redireciona para o método avançado
        /// </summary>
        public async Task<bool> PerformProfessionalUninstall(AppInfo app)
        {
            return await PerformAdvancedUninstall(app, forceRemoval: false);
        }
    }
}
