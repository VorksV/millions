using System;
using System.IO;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.UltraClean.Modules.System
{
    public class ErrorReportingModule : DirectoryCleanupModuleBase
    {
        public override string Id => "System.ErrorReporting";
        public override string Name => "Relatório de Erros do Windows";
        public override string Description => "Arquivos gerados pelo Windows Error Reporting (WER) e Dumps de Kernel";
        public override string Category => "Sistema";
        public override ModuleSafetyLevel Safety => ModuleSafetyLevel.Safe;
        public override ModuleRiskLevel Risk => ModuleRiskLevel.Low;
        public override bool RequiresAdmin => true;

        public ErrorReportingModule(ILoggingService logger) : base(logger) { }

        protected override string[] TargetDirectories => new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "WER"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "WER"),
            @"C:\Windows\LiveKernelReports"
        };
    }
}
