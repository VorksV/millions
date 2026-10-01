using System;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.UltraClean.Modules.System
{
    public class PrefetchModule : DirectoryCleanupModuleBase
    {
        public override string Id => "System.Prefetch";
        public override string Name => "Prefetch";
        public override string Description => "Arquivos de pré-carregamento do Windows que podem se tornar obsoletos.";
        public override string Category => "Cache do Windows";
        public override bool RequiresAdmin => true;

        public PrefetchModule(ILoggingService logger) : base(logger) { }

        protected override string[] TargetDirectories => new[] { @"%SystemRoot%\Prefetch" };
    }
}
