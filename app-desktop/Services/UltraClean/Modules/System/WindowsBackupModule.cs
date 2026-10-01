using System;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.UltraClean.Modules.System
{
    public class WindowsBackupModule : DirectoryCleanupModuleBase
    {
        public override string Id => "System.WindowsBackup";
        public override string Name => "Resíduos de Backup do Windows";
        public override string Description => "Arquivos temporários e logs do sistema de recuperação e backup";
        public override string Category => "Sistema";
        public override ModuleSafetyLevel Safety => ModuleSafetyLevel.Moderate;
        public override ModuleRiskLevel Risk => ModuleRiskLevel.Medium;
        public override bool RequiresAdmin => true;

        public WindowsBackupModule(ILoggingService logger) : base(logger) { }

        protected override string[] TargetDirectories => new[]
        {
            @"C:\Windows\Logs\SystemRestore",
            @"C:\System Volume Information\Windows Backup"
        };
    }
}
