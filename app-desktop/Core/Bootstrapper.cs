using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using VoltrisOptimizer.Core.Body;
using VoltrisOptimizer.Core.Body.Arms;
using VoltrisOptimizer.Core.Brain.V2;
using VoltrisOptimizer.Core.Intelligence;
using VoltrisOptimizer.Core.SystemIntelligenceProfiler;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services.Benchmark;
using VoltrisOptimizer.Services.Display;
using VoltrisOptimizer.Services.Enterprise;
using VoltrisOptimizer.Services.Gamer.Intelligence;
using VoltrisOptimizer.Services.Gamer.GamerModeManager;
using VoltrisOptimizer.Services.Gamer.Overlay.Implementation;
using VoltrisOptimizer.Services.Gamer.Overlay.Interfaces;
using VoltrisOptimizer.Services.Hardware;
using VoltrisOptimizer.Services.License;
using VoltrisOptimizer.Services.License.Interfaces;
using VoltrisOptimizer.Services.Licensing;
using VoltrisOptimizer.Services.Notifications;
using VoltrisOptimizer.Services.Performance;
using VoltrisOptimizer.Services.Performance.CpuTuning;
using VoltrisOptimizer.Services.Persistence;
using VoltrisOptimizer.Services.Personalize;
using VoltrisOptimizer.Services.Power;
using VoltrisOptimizer.Services.RateLimiting;
using VoltrisOptimizer.Services.Scheduling;
using VoltrisOptimizer.Services.Session;
using VoltrisOptimizer.Services.Shell;
using VoltrisOptimizer.Services.StreamHub.Implementation;
using VoltrisOptimizer.Services.StreamHub.Interfaces;
using VoltrisOptimizer.Services.SystemSnapshot;
using VoltrisOptimizer.Services.Telemetry;
using VoltrisOptimizer.UI.Services;
using VoltrisOptimizer.UI.ViewModels;
using VoltrisOptimizer.UI.Widgets;

namespace VoltrisOptimizer.Core;

public static class Bootstrapper
{
	public static IServiceProvider ConfigureServices(ILoggingService loggingService)
	{
		loggingService.LogEntry(nameof(ConfigureServices));
		int managedThreadId = Thread.CurrentThread.ManagedThreadId;
		Stopwatch stopwatch = Stopwatch.StartNew();
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(42, 1);
		defaultInterpolatedStringHandler.AppendLiteral("[BOOTSTRAP][TID:");
		defaultInterpolatedStringHandler.AppendFormatted(managedThreadId);
		defaultInterpolatedStringHandler.AppendLiteral("] ConfigureServices INÍCIO");
		loggingService.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
		ServiceCollection serviceCollection = new ServiceCollection();
		serviceCollection.AddSingleton(loggingService);
		serviceCollection.AddSingleton<PersistenceMigrationService>();
		serviceCollection.AddSingleton<DesktopContextMenuService>();
		serviceCollection.AddSingleton<CommandPipeService>();
		serviceCollection.AddSingleton<PerformanceMetricsCollector>();
		serviceCollection.AddSingleton<SystemSnapshotService>();
		serviceCollection.AddSingleton<ServerSideLicenseValidator>();
		serviceCollection.AddSingleton(GlobalRateLimiter.Instance);
		serviceCollection.AddSingleton<EnterpriseService>();
		serviceCollection.AddSingleton<ActivityMonitor>();
		serviceCollection.AddSingleton<MachineIdentityService>();
		serviceCollection.AddSingleton<SessionManager>();
		serviceCollection.AddSingleton<VoltrisOptimizer.Services.Telemetry.TelemetryService>();
		serviceCollection.AddSingleton<IOverlayService, OverlayService>();
		serviceCollection.AddSingleton<ILicenseService, LicenseService>();
		serviceCollection.AddSingleton<ILicenseGuard, LicenseGuard>();
		serviceCollection.AddSingleton<ILicenseDialogService, LicenseDialogService>();
		serviceCollection.AddSingleton((IServiceProvider sp) => LicenseOrchestrationService.Instance);
		serviceCollection.AddVoltrisServices();
		serviceCollection.AddGamerIntelligenceServices();
		serviceCollection.AddGamerModeManager();
		
		// ⚠️ GAMER MODE ORCHESTRATOR V2 - REGISTRADO MANUALMENTE NO APP.XAML.CS
		// Motivo: Evitar deadlock na inicialização devido a dependências complexas
		// serviceCollection.AddSingleton<VoltrisOptimizer.Services.Gamer.Interfaces.IGamerModeOrchestrator, VoltrisOptimizer.Services.Gamer.Implementation.GamerModeOrchestrator>(sp =>
		// 	new VoltrisOptimizer.Services.Gamer.Implementation.GamerModeOrchestrator(
		// 		sp.GetRequiredService<VoltrisOptimizer.Services.ILoggingService>(),
		// 		sp.GetRequiredService<VoltrisOptimizer.Services.Gamer.Interfaces.ICpuGamingOptimizer>(),
		// 		sp.GetRequiredService<VoltrisOptimizer.Services.Gamer.Interfaces.IGpuGamingOptimizer>(),
		// 		sp.GetRequiredService<VoltrisOptimizer.Services.Gamer.Interfaces.INetworkGamingOptimizer>(),
		// 		sp.GetRequiredService<VoltrisOptimizer.Services.Gamer.Interfaces.IMemoryGamingOptimizer>(),
		// 		sp.GetRequiredService<VoltrisOptimizer.Services.Gamer.Interfaces.IProcessPrioritizer>(),
		// 		sp.GetRequiredService<VoltrisOptimizer.Services.Gamer.Interfaces.IHardwareDetector>(),
		// 		sp.GetRequiredService<VoltrisOptimizer.Services.ITimerResolutionService>(),
		// 		sp.GetRequiredService<VoltrisOptimizer.Services.Gamer.Interfaces.IGameDetector>(),
		// 		sp.GetRequiredService<VoltrisOptimizer.Services.HistoryService>(),
		// 		sp.GetRequiredService<VoltrisOptimizer.Core.Brain.V2.VoltrisBrainV2>(),
		// 		sp.GetRequiredService<VoltrisOptimizer.Services.SchedulerService>()
		// ));
		
		serviceCollection.AddSingleton<CentralizedBackgroundScheduler>();
		serviceCollection.AddSingleton((IServiceProvider sp) => CentralizedBackgroundScheduler.Global = sp.GetRequiredService<CentralizedBackgroundScheduler>());
		serviceCollection.AddSingleton<IHardwareCapabilityDetector, VoltrisOptimizer.Services.Performance.CpuTuning.HardwareCapabilityDetector>();
	serviceCollection.AddSingleton<SmartEnergyService>();
	// [FIX:UNICA-FONTE] Removidos do container junto com a página ENERGIA:
	//   o servico legado    - editor manual de planos (a página usava)
	//   o servico legado  - diagnosticar e "consertar" energia do Windows
	//   o servico legado      - telemetria da pagina
	//   o servico legado         - max processor state, escrita paralela
	//
	// Os tres primeiros eram exclusivos da pagina de Energia, que foi removida.
	// O quarto gravava estado de processador por conta propria e nao tinha
	// dependencia de UI: ele simplesmente nao deveria existir, porque quem decide
	// o maximo de processor agora e a tabela do Perfil Inteligente.
		serviceCollection.AddSingleton<VoltrisOptimizer.Services.HardwareTelemetry.SensorCache>();
		serviceCollection.AddSingleton<VoltrisOptimizer.Services.HardwareTelemetry.HardwareTelemetryService>();
		serviceCollection.AddSingleton<IHardwareTelemetryHub, HardwareTelemetryHub>();
		serviceCollection.AddSingleton<ICentralTelemetryHub, CentralTelemetryHub>();
		// ⚠️ REMOVIDO: LowLevelHardwareService substituído por IHardwareBackend (WinRing0Backend)
		// serviceCollection.AddSingleton<LowLevelHardwareService>();
		serviceCollection.AddSingleton<DisplayService>();
		serviceCollection.AddSingleton<SystemTweaksService>();
		serviceCollection.AddSingleton<Windows11IconsService>();
		serviceCollection.AddSingleton<GpuControlService>();
		serviceCollection.AddSingleton<VoltrisBlurService>();
		serviceCollection.AddSingleton<TaskbarControlService>();
		serviceCollection.AddSingleton<CursorThemeService>();
		serviceCollection.AddSingleton<StateDetectionEngine>();
		serviceCollection.AddSingleton<IObsService, ObsWebSocketService>();
		serviceCollection.AddSingleton<ITwitchService, TwitchService>();
		serviceCollection.AddSingleton<IYouTubeService, YouTubeService>();
		serviceCollection.AddSingleton<IStreamHealthMonitor, StreamHealthMonitor>();
		serviceCollection.AddSingleton<IHighlightDetectorService, HighlightDetectorService>();
		serviceCollection.AddSingleton<EngagementAssistantService>();
		serviceCollection.AddSingleton<IStreamHubService, StreamHubOrchestrator>();
		serviceCollection.AddSingleton<WidgetManagerService>();
		serviceCollection.AddSingleton<VoltrisNotificationService>();
		((IServiceCollection)serviceCollection).AddSingleton((Func<IServiceProvider, INotificationService>)((IServiceProvider sp) => sp.GetRequiredService<VoltrisNotificationService>()));
		serviceCollection.AddSingleton<BrainSensorHub>();
		((IServiceCollection)serviceCollection).AddSingleton((Func<IServiceProvider, IBrainSensor>)((IServiceProvider sp) => sp.GetRequiredService<BrainSensorHub>()));
		serviceCollection.AddSingleton<BrainDecisionEngineV2>();
		serviceCollection.AddSingleton<BrainActionExecutorV2>();
		((IServiceCollection)serviceCollection).AddSingleton((Func<IServiceProvider, IBrainExecutor>)((IServiceProvider sp) => sp.GetRequiredService<BrainActionExecutorV2>()));
		serviceCollection.AddSingleton<BrainContextMemory>();
		serviceCollection.AddSingleton<BrainSessionStats>();
		serviceCollection.AddSingleton<VoltrisBrainV2>();
		serviceCollection.AddSingleton<StutterPreventionModule>();
		// [FIX:UNICO-DONO-DE-ENERGIA] O PowerPlanOrchestrator saiu do container.
		//
		// Ele existia para arbitrar QUEM escolhe o plano, e arbitração pressupõe
		// que vários podem escolher. Aqui havia quinze chamadores, e quatro deles
		// com prioridade Emergency (0) — acima do Perfil Inteligente (3). O
		// resultado era o app disputando o plano consigo mesmo.
		//
		// Arbitrar não era a solução porque o problema não era a arbitragem: era
		// haver quinze maneiras de escolher o plano. Enquanto isso existir, o
		// Perfil não é dono de nada. Agora há um dono só, e a porta legítima
		// é Services.Power.ProfilePowerAuthority.
		serviceCollection.AddSingleton<PowerArm>();
		((IServiceCollection)serviceCollection).AddSingleton((Func<IServiceProvider, IPowerArm>)((IServiceProvider sp) => sp.GetRequiredService<PowerArm>()));
		serviceCollection.AddSingleton<ProcessArm>();
		((IServiceCollection)serviceCollection).AddSingleton((Func<IServiceProvider, IProcessArm>)((IServiceProvider sp) => sp.GetRequiredService<ProcessArm>()));
		serviceCollection.AddSingleton<ActiveBackgroundDirector>();
		((IServiceCollection)serviceCollection).AddSingleton((Func<IServiceProvider, IActiveBackgroundDirector>)((IServiceProvider sp) => sp.GetRequiredService<ActiveBackgroundDirector>()));
		((IServiceCollection)serviceCollection).AddSingleton((Func<IServiceProvider, IAutoStartService>)((IServiceProvider sp) => sp.GetRequiredService<ActiveBackgroundDirector>()));
		serviceCollection.AddSingleton<AutoStartOptimizer>();
		serviceCollection.AddSingleton<SystemArm>();
		((IServiceCollection)serviceCollection).AddSingleton((Func<IServiceProvider, ISystemArm>)((IServiceProvider sp) => sp.GetRequiredService<SystemArm>()));
		serviceCollection.AddSingleton<GpuArm>();
		((IServiceCollection)serviceCollection).AddSingleton((Func<IServiceProvider, IGpuArm>)((IServiceProvider sp) => sp.GetRequiredService<GpuArm>()));
		serviceCollection.AddSingleton<NetworkArm>();
		((IServiceCollection)serviceCollection).AddSingleton((Func<IServiceProvider, INetworkArm>)((IServiceProvider sp) => sp.GetRequiredService<NetworkArm>()));
		serviceCollection.AddSingleton<VoltrisLegs>();
		((IServiceCollection)serviceCollection).AddSingleton((Func<IServiceProvider, IVoltrisLegs>)((IServiceProvider sp) => sp.GetRequiredService<VoltrisLegs>()));
		serviceCollection.AddSingleton<VoltrisSpine>();
		((IServiceCollection)serviceCollection).AddSingleton((Func<IServiceProvider, IVoltrisSpine>)((IServiceProvider sp) => sp.GetRequiredService<VoltrisSpine>()));
		serviceCollection.AddSingleton((IServiceProvider sp) => new VoltrisBody(sp.GetRequiredService<ILoggingService>(), sp.GetRequiredService<VoltrisBrainV2>(), sp.GetRequiredService<IPowerArm>(), sp.GetRequiredService<IProcessArm>(), sp.GetRequiredService<ISystemArm>(), sp.GetRequiredService<IGpuArm>(), sp.GetRequiredService<INetworkArm>(), sp.GetRequiredService<IVoltrisLegs>(), sp.GetRequiredService<IVoltrisSpine>(), sp.GetRequiredService<BrainSensorHub>(), sp.GetRequiredService<IPredictivePreWarmEngine>(), sp.GetRequiredService<IActiveBackgroundDirector>()));
		((IServiceCollection)serviceCollection).AddSingleton((Func<IServiceProvider, IVoltrisBody>)((IServiceProvider sp) => sp.GetRequiredService<VoltrisBody>()));
		((IServiceCollection)serviceCollection).AddSingleton((Func<IServiceProvider, IAutoStartService>)((IServiceProvider sp) => sp.GetRequiredService<VoltrisBody>()));
		serviceCollection.AddSingleton<NetworkIntelligence.NetworkIntelligenceOrchestrator>();
		((IServiceCollection)serviceCollection).AddSingleton((Func<IServiceProvider, NetworkIntelligence.INetworkIntelligenceOrchestrator>)((IServiceProvider sp) => sp.GetRequiredService<NetworkIntelligence.NetworkIntelligenceOrchestrator>()));
		serviceCollection.AddSingleton<PerformanceBenchmarkService>();
		((IServiceCollection)serviceCollection).AddSingleton((Func<IServiceProvider, IPerformanceBenchmarkService>)((IServiceProvider sp) => sp.GetRequiredService<PerformanceBenchmarkService>()));
		((IServiceCollection)serviceCollection).AddSingleton((Func<IServiceProvider, IAutoStartService>)((IServiceProvider sp) => sp.GetRequiredService<PerformanceBenchmarkService>()));
		serviceCollection.AddSingleton<TemporalPatternEngine>();
		((IServiceCollection)serviceCollection).AddSingleton((Func<IServiceProvider, ITemporalPatternEngine>)((IServiceProvider sp) => sp.GetRequiredService<TemporalPatternEngine>()));
		((IServiceCollection)serviceCollection).AddSingleton((Func<IServiceProvider, IAutoStartService>)((IServiceProvider sp) => sp.GetRequiredService<TemporalPatternEngine>()));
		serviceCollection.AddSingleton<PredictivePreWarmEngine>();
		((IServiceCollection)serviceCollection).AddSingleton((Func<IServiceProvider, IPredictivePreWarmEngine>)((IServiceProvider sp) => sp.GetRequiredService<PredictivePreWarmEngine>()));
		serviceCollection.AddSingleton<BehaviorScoreEngine>();
		((IServiceCollection)serviceCollection).AddSingleton((Func<IServiceProvider, IBehaviorScoreEngine>)((IServiceProvider sp) => sp.GetRequiredService<BehaviorScoreEngine>()));
		((IServiceCollection)serviceCollection).AddSingleton((Func<IServiceProvider, IAutoStartService>)((IServiceProvider sp) => sp.GetRequiredService<BehaviorScoreEngine>()));
		((IServiceCollection)serviceCollection).AddSingleton((Func<IServiceProvider, IAutoStartService>)((IServiceProvider sp) => (IAutoStartService)sp.GetRequiredService<IPatternRecognitionService>()));
		serviceCollection.AddSingleton<VoltrisOptimizer.Services.Optimization.DlsPolicyCoordinator>();
		((IServiceCollection)serviceCollection).AddSingleton((Func<IServiceProvider, IAutoStartService>)((IServiceProvider sp) => sp.GetRequiredService<VoltrisOptimizer.Services.Optimization.DlsPolicyCoordinator>()));

		// [FIX:BRAIN-LIGADO] O CICLO ANTI-STUTTER PASSA A EXISTIR
		//
		// Este registro e a unica coisa que faltava para a inteligencia do
		// projeto funcionar. O `AntiStutterOrchestrator` ja criava o proprio
		// profiler, o proprio analisador e o proprio motor de decisao no
		// construtor — mas NINGUEM criava o orquestrador. Ele nao estava
		// registrado em container nenhum e nao implementava `IAutoStartService`,
		// entao o App (que so inicia `IAutoStartService`) nunca o via.
		//
		// O resultado era um Brain completo, correto e completamente parado:
		//
		//     sem profiler -> sem snapshot -> regime sempre "Unknown"
		//     -> escalada nunca autorizada -> prioridade nunca corrigida
		//
		// A forma do registro e identica a do `DlsPolicyCoordinator` acima: uma
		// instancia unica, exposta tambem como `IAutoStartService`, de modo que
		// o `StartAsync` do pipeline de autostart a encontre.
		//
		// `AddSingleton` (e nao `AddHostedService`) porque o app resolve os
		// `IAutoStartService` por `GetServices<>` e inicia cada um com um
		// timeout de 15 segundos — o mesmo tratamento dos outros.
		serviceCollection.AddSingleton<VoltrisOptimizer.Core.Brain.V2.AntiStutter.AntiStutterOrchestrator>();
		((IServiceCollection)serviceCollection).AddSingleton((Func<IServiceProvider, IAutoStartService>)((IServiceProvider sp) => sp.GetRequiredService<VoltrisOptimizer.Core.Brain.V2.AntiStutter.AntiStutterOrchestrator>()));
		// [FIX:A-1] Registro duplicado REMOVIDO.
		//
		// DashboardViewModel era registrado como Transient em DOIS lugares
		// ( aqui e em ServiceCollectionExtensions.AddVoltrisServices), com o
		// mesmo lifetime. No MS DI o ultimo descriptor registrado e o que vence,
		// entao o lifetime efetivo dependia da ordem de chamada — nao
		// determinismo. Como ambos eram AddTransient sem factory, remover um e
		// estritamente neutro em comportamento e elimina a ambiguidade.
		serviceCollection.AddSingleton<PerformanceViewModel>();
		serviceCollection.AddSingleton<PrivacyViewModel>();
		serviceCollection.AddSingleton<SecurityViewModel>();
		serviceCollection.AddSingleton<RepairViewModel>();
		serviceCollection.AddSingleton<RecoveryViewModel>();
		serviceCollection.AddSingleton<StreamHubViewModel>();
		serviceCollection.AddSingleton<DisplayViewModel>();
		serviceCollection.AddSingleton<PersonalizeViewModel>();
		serviceCollection.AddSingleton<ApplyAllViewModel>();
		serviceCollection.AddSingleton<NotificationDrawerViewModel>();
		
		// Hardware Backend seguro (SEM driver de kernel). O WinRing0 foi removido:
		// distribuía WinRing0x64.sys não assinado → quarentena (HackTool/Riskware) + BYOVD.
		serviceCollection.AddSingleton<VoltrisOptimizer.Services.Performance.CpuTuning.Core.Interfaces.IHardwareBackend, VoltrisOptimizer.Services.Performance.CpuTuning.Core.Backends.SafeFallbackBackend>();
		serviceCollection.AddSingleton<VoltrisOptimizer.Services.Performance.CpuTuning.Core.Managers.PowerLimitManager>();
		
		// ── MOTORES AUTÔNOMOS PROFISSIONAIS (100% C# / Win32 Nativo, Zero Placebo) ──
		serviceCollection.AddSingleton<VoltrisOptimizer.Core.Optimization.ContinuousVoltriScoreEngine>();
		serviceCollection.AddSingleton<VoltrisOptimizer.Services.Optimization.SmartActionNotificationEngine>();
		serviceCollection.AddSingleton<VoltrisOptimizer.Services.Intelligence.AutonomousReportService>();
		serviceCollection.AddSingleton<VoltrisOptimizer.Services.Maintenance.NightIdleMaintenanceService>();
		serviceCollection.AddSingleton<VoltrisOptimizer.Services.Responsiveness.SmartAppFocusEngine>();

		ServiceProvider result = serviceCollection.BuildServiceProvider();
		stopwatch.Stop();
		loggingService.LogValue("ServiceRegistrationCount", serviceCollection.Count);
		defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(52, 2);
		defaultInterpolatedStringHandler.AppendLiteral("[BOOTSTRAP][TID:");
		defaultInterpolatedStringHandler.AppendFormatted(managedThreadId);
		defaultInterpolatedStringHandler.AppendLiteral("] ConfigureServices FIM. Duração: ");
		defaultInterpolatedStringHandler.AppendFormatted(stopwatch.ElapsedMilliseconds);
		defaultInterpolatedStringHandler.AppendLiteral("ms");
		loggingService.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
		loggingService.LogExit(nameof(ConfigureServices), result != null ? "BuiltServiceProvider" : null, stopwatch.ElapsedMilliseconds);
		return result;
	}
}
