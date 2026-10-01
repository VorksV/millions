using Microsoft.Extensions.DependencyInjection;
using VoltrisOptimizer.Services.Gamer.Intelligence.Interfaces;
using VoltrisOptimizer.Services.Gamer.Intelligence.Implementation;
using VoltrisOptimizer.Services.Gamer.Intelligence.Telemetry;
using VoltrisOptimizer.Core.Intelligence;
using VoltrisOptimizer.Services.Gamer.Models;

namespace VoltrisOptimizer.Services.Gamer.Intelligence
{
    /// <summary>
    /// ExtensÃµes para registro de serÃ¡viÃ§os de inteligÃªncia no DI
    /// </summary>
    public static class IntelligenceServiceExtensions
    {
        /// <summary>
        /// Registra todos os serÃ¡viÃ§os de inteligÃªncia de gaming
        /// </summary>
        public static IServiceCollection AddGamerIntelligenceServices(this IServiceCollection services)
        {
            // Core Services
            services.AddSingleton<IHardwareProfiler, HardwareProfilerService>();
            services.AddSingleton<IGameIntelligence, GameIntelligenceService>();
            
            // Monitoring Services
            services.AddSingleton<IFrameTimeOptimizer, FrameTimeOptimizerService>();
            services.AddSingleton<IThermalMonitor, VoltrisOptimizer.Services.Gamer.Intelligence.Implementation.ThermalMonitorService>();
            services.AddSingleton<IVramManager, VramManagerService>();
            
            // Optimization Services
            services.AddSingleton<IInputLagOptimizer, InputLagOptimizerService>();
            services.AddSingleton<INetworkIntelligence, NetworkIntelligenceService>();
            // [FIX:UNICA-FONTE] ``IPowerBalancer`` saiu do container.
            //
            // A implementacao (``o servico legado``) gravava no esquema ATIVO:
            // core parking 0, processador minimo 100, USB e PCIe sem suspensao.
            // Era uma quarta via de escrita de energia, e rodava por ``powercfg``
            // direto, fora de qualquer portao.
            //
            // Core parking 0 e minimo 100 juntos impedem o processador de baixar o
            // clock: num ultrabook de 15W o orcamento termico e gasto antes da
            // carga chegar, e o clock sustentado cai. A medicao nesta maquina
            // deu 108% para 62% de pico com estacionamento desligado.
            //
            // A interface ``IPowerBalancer`` permanece declarada, sem implementacao:
            // ela e o contrato do que equilibrar energia deveria significar. O lugar
            // certo para cumpri-lo hoje e a tabela do Perfil Inteligente.
            
            // Benchmark
            services.AddSingleton<IAutoBenchmark, AutoBenchmarkService>();
            // IPowerProfileDiagnosticsService: stub com parÃ¢metros object â€” ViewModel cria manualmente em InitializeBackgroundServicesAsync
            // IAdaptiveOptimizationEngine registrado via factory em ServiceCollectionExtensions (injeta Func<IGamerModeOrchestrator>)
            
            // ðŸ”§ CRITICAL FIX: Intelligence Services missing from DI
            services.AddSingleton<PerGameLearningProfileService>();
            services.AddSingleton<PredictiveStutterPreventionService>();
            services.AddSingleton<AdaptiveHardwareEngineService>();
            
            // â€” NOVA IA REALMENTE INTELIGENTE
            services.AddSingleton<RealIntelligenceEngine>();
            
            // â€”ðŸ”§ CRITICAL FIX: Missing dependencies for AdaptiveHardwareEngineService
            services.AddSingleton<IEngineTelemetryFacade, EngineTelemetryFacade>();

            return services;
        }
    }
}

