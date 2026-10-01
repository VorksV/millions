using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using VoltrisOptimizer.Core.EnterpriseCheck;
using VoltrisOptimizer.Core.GameRecognition;
using VoltrisOptimizer.Core.Intelligence;
using VoltrisOptimizer.Core.Intelligence.Heuristics.Abstractions;
using VoltrisOptimizer.Core.Intelligence.Heuristics.Pipeline;
using VoltrisOptimizer.Core.Intelligence.Heuristics.Rules;
using VoltrisOptimizer.Core.SystemIntelligenceProfiler;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Enterprise;
using VoltrisOptimizer.Services.Gamer;
using VoltrisOptimizer.Services.Gamer.Adaptive;
using VoltrisOptimizer.Services.Gamer.Implementation;
using VoltrisOptimizer.Services.Gamer.Intelligence;
using VoltrisOptimizer.Services.Gamer.Intelligence.Interfaces;
using VoltrisOptimizer.Services.Gamer.Interfaces;
using VoltrisOptimizer.Services.Gamer.Overlay.Implementation;
using VoltrisOptimizer.Services.Gamer.Overlay.Interfaces;
using VoltrisOptimizer.Services.Hardware;
using VoltrisOptimizer.Services.Intelligence.VPIS.Core;
using VoltrisOptimizer.Services.Intelligence.VPIS.Delivery;
using VoltrisOptimizer.Services.Intelligence.VPIS.Engines;
using VoltrisOptimizer.Services.Intelligence.VPIS.Forensics;
using VoltrisOptimizer.Services.Intelligence.VPIS.Telemetry;
using VoltrisOptimizer.Services.Localization;
using VoltrisOptimizer.Services.Logging;
using VoltrisOptimizer.Services.Monitoring.Implementation;
using VoltrisOptimizer.Services.Monitoring.Interfaces;
using VoltrisOptimizer.Services.Optimization;
using VoltrisOptimizer.Services.Optimization.Providers;
using VoltrisOptimizer.Services.Performance;
using VoltrisOptimizer.Services.Performance.Benchmark;
using VoltrisOptimizer.Services.Performance.Decision;
using VoltrisOptimizer.Services.Performance.Orchestration;
using VoltrisOptimizer.Services.Power;
using VoltrisOptimizer.Services.Responsiveness;
using VoltrisOptimizer.Services.Rollback;
using VoltrisOptimizer.Services.Shield;
using VoltrisOptimizer.Services.Shield.Advanced;
using VoltrisOptimizer.Services.Shield.Network;
using VoltrisOptimizer.Services.SystemChanges;
using VoltrisOptimizer.Services.SystemIntelligenceProfiler;
using VoltrisOptimizer.Services.Telemetry;
using VoltrisOptimizer.Services.Thermal;
using VoltrisOptimizer.Services.Tuning;
using VoltrisOptimizer.UI.ViewModels;
using VoltrisOptimizer.Services.GameRepairEnterprise;

namespace VoltrisOptimizer.Core;

public static class ServiceCollectionExtensions
{
	public static IServiceCollection AddVoltrisServices(this IServiceCollection services)
	{
		// Strangler Fig Pattern: Core Services (Fase 1)
		services.AddSingleton<VoltrisOptimizer.Core.Configuration.IVoltrisFeatureFlagManager, VoltrisOptimizer.Core.Configuration.FeatureFlagManager>();
		services.AddSingleton<VoltrisOptimizer.Services.SystemChanges.ICapabilityGuard, VoltrisOptimizer.Services.SystemChanges.CapabilityGuard>();
		services.AddSingleton(VoltrisOptimizer.Core.Telemetry.UnifiedTelemetryBus.Instance);
        
        // Safety Wrappers (Fase 2)
        services.AddSingleton<VoltrisOptimizer.Services.SystemSafety.ISafeRegistryManager, VoltrisOptimizer.Services.SystemSafety.SafeRegistryManager>();
        services.AddSingleton<VoltrisOptimizer.Services.SystemSafety.ISafePowerManager, VoltrisOptimizer.Services.SystemSafety.SafePowerManager>();
        services.AddSingleton<VoltrisOptimizer.Services.SystemSafety.ISafeProcessManager, VoltrisOptimizer.Services.SystemSafety.SafeProcessManager>();
        services.AddSingleton<VoltrisOptimizer.Services.SystemSafety.ISafeAffinityManager, VoltrisOptimizer.Services.SystemSafety.SafeAffinityManager>();
        
        // Rollback e Snapshots (Fase 3)
        services.AddSingleton<VoltrisOptimizer.Services.SystemSafety.Rollback.IIncrementalSnapshotManager, VoltrisOptimizer.Services.SystemSafety.Rollback.IncrementalSnapshotManager>();
        services.AddSingleton<VoltrisOptimizer.Services.SystemSafety.Rollback.IGranularRollbackEngine, VoltrisOptimizer.Services.SystemSafety.Rollback.GranularRollbackEngine>();
        
        // Observability (Fase 5)
        services.AddSingleton<VoltrisOptimizer.Services.Diagnostics.IEnterpriseObservabilityService, VoltrisOptimizer.Services.Diagnostics.EnterpriseObservabilityService>();

		// Strangler Fig Pattern: Execution & Scheduler (Fase 4)
		services.AddSingleton(VoltrisOptimizer.Core.Execution.OptimizationScheduler.Instance);

		// Strangler Fig Pattern: Validação e Decisão (Fase 3)
		services.AddSingleton(VoltrisOptimizer.Core.Validation.AtomicRollbackManager.Instance);
		services.AddSingleton(VoltrisOptimizer.Core.Validation.EvidenceValidator.Instance);
		services.AddSingleton(VoltrisOptimizer.Core.Orchestration.PolicyEngine.Instance);
		services.AddSingleton(VoltrisOptimizer.Core.Orchestration.UnifiedDecisionEngine.Instance);

		// Strangler Fig Pattern: Consciência de Hardware e IA (Fase 4)
		services.AddSingleton(VoltrisOptimizer.Core.Hardware.HardwareAwarenessManager.Instance);
		services.AddSingleton(VoltrisOptimizer.Core.Optimizations.OptimizationCatalog.Instance);
		services.AddSingleton(VoltrisOptimizer.Core.Brain.V2.MultiDimensionalRewardEngine.Instance);

		if (!services.Any((ServiceDescriptor d) => d.ServiceType == typeof(ILoggingService)))
		{
			services.AddSingleton((Func<IServiceProvider, ILoggingService>)delegate
			{
				// Mesmo resolvedor usado por App.xaml.cs, para que este ramo nunca
				// grave em um diretorio diferente do logger principal.
				string text = VoltrisOptimizer.Services.Logging.LogDirectoryResolver.Resolve();
				Directory.CreateDirectory(text);
				return new ProfessionalLoggingService(text);
			});
		}
		services.AddSingleton<IProcessRunner, ProcessRunnerService>();
		services.AddSingleton<IRegistryService, RegistryService>();
		services.AddSingleton<ISystemInfoService, SystemInfoServiceImpl>();
		services.AddSingleton<MachineIdentityService>();
		services.AddSingleton<MonitorService>();
		services.AddSingleton<SystemSafetyService>();
		services.AddSingleton((IServiceProvider provider) => SettingsService.Instance);
		services.AddSingleton<VoltrisOptimizer.Services.HardwareTelemetry.SensorCache>();
		services.AddSingleton<VoltrisOptimizer.Services.HardwareTelemetry.HardwareTelemetryService>();
		services.AddSingleton<IHardwareTelemetryHub, HardwareTelemetryHub>();
		services.AddSingleton<ICentralTelemetryHub, CentralTelemetryHub>();
		services.AddSingleton<ProcessCacheService>();
		services.AddSingleton<WmiCacheService>();
		services.AddSingleton<IMachineProfileDetector, MachineProfileDetector>();
		services.AddSingleton((Func<IServiceProvider, IAdaptiveOptimizationEngine>)delegate(IServiceProvider sp)
		{
			IServiceProvider sp2 = sp;
			return new AdaptiveOptimizationEngine(() => sp2.GetRequiredService<IGamerModeOrchestrator>(), sp2.GetRequiredService<IRealGameBoosterService>(), sp2.GetRequiredService<IGpuGamingOptimizer>(), sp2.GetRequiredService<IMachineProfileDetector>(), sp2.GetRequiredService<ILoggingService>());
		});
		services.AddSingleton<SystemCleaner>();
		services.AddSingleton<VoltrisPerformanceOptimizer>();
		services.AddSingleton<VoltrisGlobalInsightService>();
		services.AddSingleton<UnifiedOptimizationService>();
		services.AddSingleton<NetworkOptimizer>();
		services.AddSingleton<AdvancedOptimizer>();
		services.AddSingleton<AdvancedTweaksService>();
		services.AddSingleton<ExtremeOptimizationsService>();
		services.AddSingleton<GameSessionOptimizerService>();
		services.AddSingleton<GamerOptimizerService>();
		services.AddSingleton<GodModeService>();
		services.AddSingleton<GameDiagnosticsService>();
		services.AddSingleton<GameRepairService>();
		services.AddSingleton<UltraPerformanceService>();
		services.AddSingleton<UltraCleanerService>();
		services.AddSingleton<IProcessProvider, WindowsProcessProvider>();
		services.AddSingleton<ICpuCoreLoadProvider, WindowsCpuCoreLoadProvider>();
		services.AddSingleton<IGpuLoadProvider, WindowsGpuLoadProvider>();
		services.AddSingleton<CoreLoadHeuristics>();
		services.AddSingleton<CriticalProcessDetector>();
		services.AddSingleton<StabilityEngineService>();
		services.AddSingleton<IDynamicLoadStabilizer, DynamicLoadStabilizer>();
		services.AddSingleton<HistoryService>();
		services.AddSingleton<SchedulerService>();
		services.AddSingleton<IntelligentCleanupEngine>();
		services.AddSingleton<IDialogService, DialogService>();
		services.AddSingleton<INavigationService, NavigationService>();
		services.AddSingleton<ProfileStore>();
		services.AddSingleton<VoltrisOptimizer.Core.SystemIntelligenceProfiler.IRollbackManager, VoltrisOptimizer.Core.SystemIntelligenceProfiler.RollbackManager>();
		services.AddSingleton<IAuditCollector, AuditCollector>();
		services.AddSingleton<IRecoveryGuard, RecoveryGuard>();
		services.AddSingleton<IEtwFrameTimeMonitor, EtwFrameTimeMonitor>();
		services.AddSingleton<VoltrisOptimizer.Services.SystemChanges.IRollbackManager, EnhancedRollbackManager>();
		services.AddSingleton<IDecisionEngine, DecisionEngine>();
		services.AddSingleton<ICompatibilityPolicy, DefaultCompatibilityPolicy>();
		services.AddSingleton<VoltrisOptimizer.Interfaces.ISystemProfiler, VoltrisOptimizer.Core.SystemIntelligenceProfiler.SystemIntelligenceProfiler>();
        services.AddGameRepairEnterprise(); // Fase 1 Enterprise Game Repair
		services.AddSingleton<EtwSession>();
		services.AddSingleton<EtwEventParser>();
		services.AddSingleton((Func<IServiceProvider, IEtwAnalyzer>)((IServiceProvider provider) => new EtwAnalyzer(provider.GetRequiredService<EtwSession>(), provider.GetRequiredService<EtwEventParser>(), provider.GetRequiredService<ILoggingService>())));
		services.AddSingleton<GameDetectionService>();
		services.AddSingleton<IGameProfileRepository, GameProfileRepositoryAdapter>();
		services.AddSingleton<IGameRecognitionEngine, GameRecognitionEngine>();
		services.AddSingleton<IGamerModeOrchestrator, GamerModeOrchestrator>();
		services.AddSingleton<IGameDetector, GameDetectionService>();
		services.AddSingleton<IGameLibraryService, GameLibraryService>();
		services.AddSingleton<IGameProfileService, GameProfileService>();
		services.AddSingleton<IGpuGamingOptimizer, GpuGamingOptimizerService>();
		// TimerResolutionService e TimerResolutionManager já registrados em GamerServiceExtensions
		services.AddSingleton<IRealGameBoosterService, RealGameBoosterService>();
		services.AddSingleton<IGlobalThermalMonitorService, GlobalThermalMonitorService>();
		services.AddSingleton<VoltrisOptimizer.Services.Telemetry.TelemetryService>();
		services.AddSingleton<VoltrisOptimizer.Services.Enterprise.TelemetryService>();

		services.AddSingleton((Func<IServiceProvider, IOverlayService>)((IServiceProvider provider) => new OverlayService(provider.GetService<ILoggingService>())));
		// [FIX:UNICA-FONTE] O `o servico legado` saiu do container.
		//
		// Ele era o segundo criador de planos do app: criava e renomeava a família
		// "Voltris - {Perfil}" e tinha a própria lógica de ativação. Subscrito ao
		// mesmo `ProfileChanged` que o sistema novo, ele produzia planos duplicados
		// com o mesmo nome e configurações opostas — o que o usuário via como "dois
		// Voltris - Equilibrado" em Opções de Energia.
		//
		// A energia agora tem UM dono: `ProfilePowerCoordinator`, que é estático
		// e não precisa de registro no container.
		
		// STARTUP ORCHESTRATOR
		services.AddSingleton<VoltrisOptimizer.Core.VoltrisStartupOrchestrator>();

		// VMRG — Voltris Virtual Machine Resource Governor
		services.AddSingleton<VoltrisOptimizer.Core.VMRG.Interfaces.IVmDetectionService, VoltrisOptimizer.Services.VMRG.Detection.VmDetectionService>();
		services.AddSingleton<VoltrisOptimizer.Core.VMRG.Interfaces.IVmDetectionProvider, VoltrisOptimizer.Services.VMRG.Detection.Providers.VirtualBoxProvider>();
		services.AddSingleton<VoltrisOptimizer.Core.VMRG.Interfaces.IVmDetectionProvider, VoltrisOptimizer.Services.VMRG.Detection.Providers.VMwareProvider>();
		services.AddSingleton<VoltrisOptimizer.Core.VMRG.Interfaces.IVmDetectionProvider, VoltrisOptimizer.Services.VMRG.Detection.Providers.HyperVProvider>();
		services.AddSingleton<VoltrisOptimizer.Core.VMRG.Interfaces.IVmDetectionProvider, VoltrisOptimizer.Services.VMRG.Detection.Providers.Wsl2Provider>();
		services.AddSingleton<VoltrisOptimizer.Core.VMRG.Interfaces.IVmDetectionProvider, VoltrisOptimizer.Services.VMRG.Detection.Providers.QemuProvider>();
		services.AddSingleton<VoltrisOptimizer.Core.VMRG.Interfaces.IVmDetectionProvider, VoltrisOptimizer.Services.VMRG.Detection.Providers.AndroidEmulatorProvider>();
		services.AddSingleton<VoltrisOptimizer.Core.VMRG.Interfaces.IVmMonitorService, VoltrisOptimizer.Services.VMRG.Services.VmMonitorService>();
		services.AddSingleton<VoltrisOptimizer.Core.VMRG.Interfaces.IVmDecisionEngine, VoltrisOptimizer.Services.VMRG.Services.VmDecisionEngine>();
		services.AddSingleton<VoltrisOptimizer.Core.VMRG.Interfaces.IVmActionExecutor, VoltrisOptimizer.Services.VMRG.Services.VmActionExecutor>();
		services.AddSingleton<VoltrisOptimizer.Core.VMRG.Interfaces.IVmValidationService, VoltrisOptimizer.Services.VMRG.Services.VmValidationService>();
		services.AddSingleton<VoltrisOptimizer.Core.VMRG.Interfaces.IVmLearningService, VoltrisOptimizer.Services.VMRG.Services.VmLearningService>();
		services.AddSingleton<VoltrisOptimizer.Core.VMRG.Interfaces.IVmrgOrchestrator, VoltrisOptimizer.Services.VMRG.VmrgOrchestrator>();
		
		services.AddSingleton<LatestPerformanceMetricsProvider>();
		// [FIX:UNICA-FONTE] O `o servico legado` saiu do container.
		// Ele gravava EPP e estados de processador no esquema ATIVO, em paralelo
		// com o Perfil Inteligente, sem lock e sem ordem. Removido do projeto.
		// [FIX:UNICO-DONO-DE-ENERGIA] O OptimizationManager não recebe mais o
// orquestrador de planos: a energia tem um dono só, o Perfil Inteligente.
// Qualquer pedido de plano que chegar aqui é reaplicação do perfil, nunca
// uma escolha de plano.
services.AddSingleton((IServiceProvider provider) => new OptimizationManager(provider.GetRequiredService<ILoggingService>(), SettingsService.Instance, provider.GetRequiredService<IGlobalThermalMonitorService>(), provider.GetService<IGamerModeOrchestrator>()));
		services.AddSingleton((IServiceProvider provider) => new HardwarePerformanceOptimizationService(provider.GetRequiredService<ILoggingService>(), provider.GetRequiredService<IGlobalThermalMonitorService>(), provider.GetService<IGamerModeOrchestrator>()));
		services.AddGamerServices();
		services.AddSingleton<SecurityLogService>();
		services.AddSingleton<SignatureVerificationService>();
		services.AddSingleton<QuarantineService>();
		services.AddSingleton<FileMonitorService>();
		services.AddSingleton<StartupMonitorService>();
		services.AddSingleton<AdwareScannerService>();
		services.AddSingleton<DefenderIntegrationService>();
		services.AddSingleton<RansomwareMonitorService>();
		services.AddSingleton<PortMonitorService>();
		services.AddSingleton<ThreatProtectionService>();
		services.AddSingleton<VoltrisOptimizer.Services.Shield.ShieldLicenseGate>();
		services.AddSingleton<VoltrisShieldService>();
		services.AddSingleton<ShieldNotificationService>();
		services.AddSingleton<AdvancedStaticAnalyzer>();
		services.AddSingleton<BehavioralMonitor>();
		services.AddSingleton<HeuristicAnalyzer>();
		services.AddSingleton<DefenderScanService>();
		services.AddSingleton<CodeInjectionDetector>();
		services.AddSingleton<NetworkMonitorService>();
		services.AddSingleton<NetworkScannerService>();
		services.AddSingleton<DeviceTrackerService>();
		services.AddSingleton<DeviceIdentificationEngine>();
		services.AddSingleton<DeviceClassifier>();
		services.AddSingleton<DeviceVendorService>();
		services.AddSingleton<HostnameAnalyzer>();
		services.AddSingleton<OUIDatabase>();
		services.AddSingleton<ShieldViewModel>();
		services.AddSingleton((IServiceProvider provider) => new GamerViewModel(provider.GetRequiredService<IGamerModeOrchestrator>(), provider.GetRequiredService<IGameDetector>(), provider.GetRequiredService<IGameLibraryService>(), provider.GetRequiredService<IGpuGamingOptimizer>(), provider.GetRequiredService<ILoggingService>(), provider.GetRequiredService<IMachineProfileDetector>(), provider.GetRequiredService<IAdaptiveOptimizationEngine>(), provider.GetRequiredService<IHardwareDetector>(), provider.GetService<IOverlayService>(), provider.GetService<IGameProfileService>(), provider.GetService<IRealGameBoosterService>(), provider.GetService<IPowerProfileDiagnosticsService>()));
		services.AddSingleton<HotkeyService>();
		services.AddSingleton((IServiceProvider provider) => new ShortcutsViewModel(provider.GetService<HotkeyService>()));
		services.AddSingleton<EnvironmentDetector>();
		services.AddSingleton<IPerformanceDecisionEngine, RuleBasedDecisionEngine>();
		services.AddSingleton<IPerformanceOrchestrator, PerformanceOrchestrator>();
		services.AddSingleton<HardwareProfiler>();
		services.AddSingleton<PerformanceContextBuilder>();
		services.AddSingleton<PerformanceValidationService>();
		services.AddSingleton<IntelligentPerformanceCoordinator>();
		services.AddSingleton<IPerformanceOptimizationService, PerformanceOptimizationService>();
		services.AddSingleton<BenchmarkMetricCollector>();
		services.AddSingleton<BenchmarkStatisticalAnalyzer>();
		services.AddSingleton<BenchmarkEngine>();
		services.AddSingleton<BenchmarkPersistenceService>();
		services.AddSingleton<ISecurityTuningService, SecurityTuningService>();
		services.AddSingleton<IPrivacyTuningService, PrivacyTuningService>();
		services.AddSingleton<IDebloatTuningService, DebloatTuningService>();
		services.AddSingleton<StateDetectionEngine>();
		services.AddSingleton<SystemIntelligenceProfilerService>();
		services.AddSingleton<IntelligentOptimizationExecutor>();
		services.AddTransient<DashboardViewModel>();  // ⚠️ TRANSIENT: nova instância a cada navegação (evita flash de UI antiga)
		services.AddSingleton<ApplyAllViewModel>();
		services.AddSingleton<BenchmarkViewModel>();
		services.AddSingleton<DebloatViewModel>();
		services.AddSingleton<PrivacyViewModel>();
		services.AddSingleton<SecurityViewModel>();
		services.AddSingleton<PerformanceViewModel>();
		services.AddSingleton<RecoveryViewModel>();
		services.AddSingleton<RepairViewModel>();
		services.AddSingleton<SmartRepairViewModel>();
		services.AddSingleton<StreamHubViewModel>();
		services.AddSingleton<NotificationDrawerViewModel>();
		services.AddSingleton<VoltrisOptimizer.Services.Localization.LocalizationService>();
		services.AddSingleton<ResponsivenessConfig>();
		services.AddSingleton((Func<IServiceProvider, IImmediateResponsivenessEngine>)((IServiceProvider provider) => new ImmediateResponsivenessEngine(provider.GetRequiredService<ILoggingService>(), provider.GetRequiredService<CriticalProcessDetector>(), provider.GetRequiredService<ProcessCacheService>())));
		services.AddSingleton<IFrametimeProvider, PresentMonFrametimeProvider>();
		services.AddSingleton<VpisEventPipeline>();
		services.AddSingleton<RootCauseRankingEngine>();
		services.AddSingleton<CausalTimelineBuilder>();
		services.AddSingleton<SessionInvestigator>();
		services.AddSingleton((IServiceProvider provider) => new VpisNotificationHandler(customToastEnabled: true));
		services.AddTransient<IVpisDiagnosticEngine, ThermalThrottlingEngine>();
		services.AddTransient<IVpisDiagnosticEngine, VramSaturationEngine>();
		services.AddTransient<IVpisDiagnosticEngine, CpuBottleneckEngine>();
		services.AddTransient<IVpisDiagnosticEngine, GpuBottleneckEngine>();
		services.AddTransient<IVpisDiagnosticEngine, RamSaturationEngine>();
		services.AddTransient<IVpisDiagnosticEngine, FrametimeSpikeEngine>();
		services.AddTransient<IVpisDiagnosticEngine, NetworkLatencyEngine>();
		services.AddSingleton<VpisSessionManager>();
		services.AddSingleton<IHeuristicsPipeline, HeuristicsDecisionEngine>();
		services.AddTransient<IHeuristicRule, SysMainDiskTypeRule>();
		services.AddTransient<IHeuristicRule, SysMainRamCapacityRule>();
		services.AddTransient<IHeuristicRule, SysMainTelemetryRule>();
		services.AddTransient<IHeuristicRule, AllowAllOptimizationsRule>();
		services.AddSingleton<ILearningLogger, LearningLogger>();
		services.AddSingleton<IPatternRecognitionService, PatternRecognitionService>();
		return services;
	}
}
