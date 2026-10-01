using System;
using System.IO;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.UltraClean.Modules.System
{
    public class UserTempModule : DirectoryCleanupModuleBase
    {
        public override string Id => "System.UserTemp";
        public override string Name => "Arquivos Temporários do Usuário";
        public override string Description => "Arquivos residuais em %TEMP% de instalações passadas e cache de apps.";
        public override string Category => "Limpeza de Usuário";
        public override ModuleSafetyLevel Safety => ModuleSafetyLevel.Safe;
        public override ModuleRiskLevel Risk => ModuleRiskLevel.Low;
        public override bool RequiresAdmin => false;

        public UserTempModule(ILoggingService logger) : base(logger) 
        {
            _logger.LogTrace("[MODULE] UserTempModule inicializado");
        }

        protected override string[] TargetDirectories => new[]
        {
            Path.GetTempPath(),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp")
        };
    }
}
