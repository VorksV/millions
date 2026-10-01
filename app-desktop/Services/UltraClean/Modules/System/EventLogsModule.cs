using System;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.UltraClean.Modules.System
{
    public class EventLogsModule : DirectoryCleanupModuleBase
    {
        public override string Id => "System.EventLogs";
        public override string Name => "Logs de Eventos Antigos";
        public override string Description => "Arquivos .evtx que não são mais necessários (pode apagar histórico de erros)";
        public override string Category => "Sistema";
        public override ModuleSafetyLevel Safety => ModuleSafetyLevel.Moderate;
        public override ModuleRiskLevel Risk => ModuleRiskLevel.Medium;
        public override bool RequiresAdmin => true;

        public EventLogsModule(ILoggingService logger) : base(logger) { }

        protected override string[] TargetDirectories => new[]
        {
            @"C:\Windows\System32\winevt\Logs"
        };
    }
}
