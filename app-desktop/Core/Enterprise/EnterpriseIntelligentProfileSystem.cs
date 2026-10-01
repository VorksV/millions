using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core.SystemIntelligenceProfiler;
using VoltrisOptimizer.Core.SystemIntelligenceProfiler.Models;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Adaptive;
using VoltrisOptimizer.Services.Gaming;
using VoltrisOptimizer.Services.Hardware;
using VoltrisOptimizer.Services.Performance;
using VoltrisOptimizer.Services.SystemChanges;
using VoltrisOptimizer.Services.Validation;
using VoltrisOptimizer.UI.ViewModels;

namespace VoltrisOptimizer.Core.Enterprise;

public class EnterpriseIntelligentProfileSystem
{
	private readonly StateDetectionEngine _stateDetector;

	private readonly EnhancedRollbackManager _rollbackManager;

	private readonly StructuredOptimizationLogger _structuredLogger;

	private readonly AdaptiveOptimizationProfile _adaptiveProfile;

	private readonly ILoggingService _logger;

	private readonly ISystemInfoService _systemInfoService;

	private readonly PerformanceValidationService _performanceValidator;

	private readonly PerformanceAwareOptimizationExecutor _performanceAwareExecutor;

	private readonly EnhancedHardwareDetector _hardwareDetector;

	private readonly GameCompatibilityProtector _gameProtector;

	private readonly OptimizationEffectivenessValidator _effectivenessValidator;

	public EnterpriseIntelligentProfileSystem(StateDetectionEngine stateDetector, EnhancedRollbackManager rollbackManager, StructuredOptimizationLogger structuredLogger, AdaptiveOptimizationProfile adaptiveProfile, ILoggingService logger, ISystemInfoService systemInfoService)
	{
		_stateDetector = stateDetector ?? throw new ArgumentNullException("stateDetector");
		_rollbackManager = rollbackManager ?? throw new ArgumentNullException("rollbackManager");
		_structuredLogger = structuredLogger ?? throw new ArgumentNullException("structuredLogger");
		_adaptiveProfile = adaptiveProfile ?? throw new ArgumentNullException("adaptiveProfile");
		_logger = logger ?? throw new ArgumentNullException("logger");
		_systemInfoService = systemInfoService ?? throw new ArgumentNullException("systemInfoService");
		_performanceValidator = new PerformanceValidationService(logger, systemInfoService);
		_performanceAwareExecutor = new PerformanceAwareOptimizationExecutor(stateDetector, logger, rollbackManager, _performanceValidator, structuredLogger);
		_hardwareDetector = new EnhancedHardwareDetector(systemInfoService, logger);
		_gameProtector = new GameCompatibilityProtector(logger, systemInfoService);
		_logger.Log(LogLevel.Info, LogCategory.System, "Enterprise Intelligent Profile System initialized with full validation and protection", null, "EnterpriseIntelligentProfile");
	}

	public async Task<ApplyAllViewModel> CreateProductionReadyViewModelAsync(CancellationToken ct = default(CancellationToken))
	{
		try
		{
			_logger.Log(LogLevel.Info, LogCategory.General, "Creating production-ready ApplyAllViewModel with comprehensive validation", null, "EnterpriseIntelligentProfile");
			DetailedHardwareProfile hardwareProfile = await _hardwareDetector.AnalyzeHardwareAsync();
			await GetCurrentSystemLoadAsync(ct);
			ILoggingService logger = _logger;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(65, 3);
			defaultInterpolatedStringHandler.AppendLiteral("System analysis - Performance Score: ");
			defaultInterpolatedStringHandler.AppendFormatted(hardwareProfile.PerformanceScore, "F1");
			defaultInterpolatedStringHandler.AppendLiteral(", ");
			defaultInterpolatedStringHandler.AppendLiteral("Gaming Score: ");
			defaultInterpolatedStringHandler.AppendFormatted(hardwareProfile.GamingScore, "F1");
			defaultInterpolatedStringHandler.AppendLiteral(", Workload: ");
			defaultInterpolatedStringHandler.AppendFormatted(hardwareProfile.WorkloadClassification);
			logger.Log(LogLevel.Info, LogCategory.General, defaultInterpolatedStringHandler.ToStringAndClear(), null, "EnterpriseIntelligentProfile");
			List<string> runningGames = await _gameProtector.GetRunningProtectedGamesAsync(ct);
			if (runningGames.Count > 0)
			{
				_logger.Log(LogLevel.Warning, LogCategory.System, "Games currently running: " + string.Join(", ", runningGames) + ". Applying gaming-safe optimizations only.", null, "EnterpriseIntelligentProfile");
			}
			ApplyAllViewModel viewModel = new ApplyAllViewModel(systemProfiler: new ProductionSystemProfiler(_stateDetector, _hardwareDetector, _logger), decisionEngine: new ProductionDecisionEngine(_adaptiveProfile, _effectivenessValidator, _logger), stateDetector: _stateDetector, executor: _performanceAwareExecutor, logger: _logger);
			await viewModel.InitializeAsync(ct);
			_logger.Log(LogLevel.Success, LogCategory.General, "Production-ready ApplyAllViewModel created and initialized", null, "EnterpriseIntelligentProfile");
			return viewModel;
		}
		catch (Exception ex2)
		{
			Exception ex = ex2;
			_logger.LogError("Failed to create production-ready ViewModel: " + ex.Message, ex);
			throw;
		}
	}

	public async Task<EnterpriseOptimizationResult> ExecuteEnterpriseOptimizationAsync(OptimizationMode mode = OptimizationMode.Smart, bool validatePerformance = true, bool checkGameCompatibility = true, bool validateEffectiveness = true, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			ILoggingService logger = _logger;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(68, 1);
			defaultInterpolatedStringHandler.AppendLiteral("Starting enterprise-grade optimization with full validation (Mode: ");
			defaultInterpolatedStringHandler.AppendFormatted(mode);
			defaultInterpolatedStringHandler.AppendLiteral(")");
			logger.Log(LogLevel.Info, LogCategory.General, defaultInterpolatedStringHandler.ToStringAndClear(), null, "EnterpriseIntelligentProfile");
			EnterpriseOptimizationResult result = new EnterpriseOptimizationResult
			{
				StartTime = DateTime.UtcNow,
				Mode = mode,
				PerformanceValidationEnabled = validatePerformance,
				GameCompatibilityCheckEnabled = checkGameCompatibility,
				EffectivenessValidationEnabled = validateEffectiveness
			};
			EnterpriseOptimizationResult enterpriseOptimizationResult = result;
			enterpriseOptimizationResult.HardwareAnalysis = await _hardwareDetector.AnalyzeHardwareAsync();
			EnterpriseOptimizationResult enterpriseOptimizationResult2 = result;
			enterpriseOptimizationResult2.SystemLoad = await GetCurrentSystemLoadAsync(ct);
			ILoggingService logger2 = _logger;
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(52, 2);
			defaultInterpolatedStringHandler.AppendLiteral("Hardware analysis complete - Performance: ");
			defaultInterpolatedStringHandler.AppendFormatted(result.HardwareAnalysis.PerformanceScore, "F1");
			defaultInterpolatedStringHandler.AppendLiteral(", ");
			defaultInterpolatedStringHandler.AppendLiteral("Gaming: ");
			defaultInterpolatedStringHandler.AppendFormatted(result.HardwareAnalysis.GamingScore, "F1");
			logger2.Log(LogLevel.Info, LogCategory.General, defaultInterpolatedStringHandler.ToStringAndClear(), null, "EnterpriseIntelligentProfile");
			if (checkGameCompatibility)
			{
				EnterpriseOptimizationResult enterpriseOptimizationResult3 = result;
				enterpriseOptimizationResult3.RunningGames = await _gameProtector.GetRunningProtectedGamesAsync(ct);
				if (result.RunningGames.Count > 0)
				{
					_logger.Log(LogLevel.Warning, LogCategory.System, "Games detected during optimization: " + string.Join(", ", result.RunningGames), null, "EnterpriseIntelligentProfile");
				}
			}
			IntelligentProfileType profileType = SettingsService.Instance.Settings.IntelligentProfile;
			ILoggingService logger3 = _logger;
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(30, 1);
			defaultInterpolatedStringHandler.AppendLiteral("Selected Intelligent Profile: ");
			defaultInterpolatedStringHandler.AppendFormatted(profileType);
			logger3.Log(LogLevel.Info, LogCategory.General, defaultInterpolatedStringHandler.ToStringAndClear(), null, "EnterpriseIntelligentProfile");
			VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization[] recommendations = await GenerateValidatedOptimizationsAsync(result.HardwareAnalysis, result.RunningGames, validateEffectiveness, profileType, ct);
			PerformanceAwareOptimizationResult executionResult = (result.ExecutionResult = await _performanceAwareExecutor.ApplyOptimizationsWithValidationAsync(recommendations, mode, validatePerformance, TimeSpan.FromSeconds(30.0), ct));
			result.PerformanceDegradationDetected = executionResult.PerformanceDegradationDetected;
			result.EndTime = DateTime.UtcNow;
			result.Duration = result.EndTime - result.StartTime;
			ILoggingService logger4 = _logger;
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(77, 3);
			defaultInterpolatedStringHandler.AppendLiteral("Enterprise optimization completed - Applied: ");
			defaultInterpolatedStringHandler.AppendFormatted(executionResult.AppliedCount);
			defaultInterpolatedStringHandler.AppendLiteral(", ");
			defaultInterpolatedStringHandler.AppendLiteral("Failed: ");
			defaultInterpolatedStringHandler.AppendFormatted(executionResult.FailedCount);
			defaultInterpolatedStringHandler.AppendLiteral(", Performance Issues: ");
			defaultInterpolatedStringHandler.AppendFormatted(result.PerformanceDegradationDetected);
			logger4.Log(LogLevel.Success, LogCategory.General, defaultInterpolatedStringHandler.ToStringAndClear(), null, "EnterpriseIntelligentProfile");
			return result;
		}
		catch (Exception ex2)
		{
			Exception ex = ex2;
			_logger.LogError("Enterprise optimization failed: " + ex.Message, ex);
			throw;
		}
	}

	public async Task<SystemReadinessReport> ValidateSystemReadinessAsync(CancellationToken ct = default(CancellationToken))
	{
		SystemReadinessReport report = new SystemReadinessReport();
		try
		{
			_logger.Log(LogLevel.Info, LogCategory.General, "Performing comprehensive system readiness validation", null, "EnterpriseIntelligentProfile");
			SystemReadinessReport systemReadinessReport = report;
			systemReadinessReport.HardwareAnalysis = await _hardwareDetector.AnalyzeHardwareAsync();
			report.HardwareReady = report.HardwareAnalysis.PerformanceScore >= 3.0;
			SystemReadinessReport systemReadinessReport2 = report;
			systemReadinessReport2.RunningGames = await _gameProtector.GetRunningProtectedGamesAsync(ct);
			report.GamingSessionActive = report.RunningGames.Count > 0;
			SystemReadinessReport systemReadinessReport3 = report;
			systemReadinessReport3.SystemLoad = await GetCurrentSystemLoadAsync(ct);
			report.SystemUnderHeavyLoad = report.SystemLoad.CpuUsagePercent > 80.0 || report.SystemLoad.MemoryUsagePercent > 85.0;
			report.ReadyForOptimization = report.HardwareReady && !report.GamingSessionActive && !report.SystemUnderHeavyLoad;
			ILoggingService logger = _logger;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(60, 4);
			defaultInterpolatedStringHandler.AppendLiteral("System readiness assessment: ");
			defaultInterpolatedStringHandler.AppendFormatted(report.ReadyForOptimization ? "READY" : "NOT READY");
			defaultInterpolatedStringHandler.AppendLiteral(" ");
			defaultInterpolatedStringHandler.AppendLiteral("(Hardware: ");
			defaultInterpolatedStringHandler.AppendFormatted(report.HardwareReady);
			defaultInterpolatedStringHandler.AppendLiteral(", Gaming: ");
			defaultInterpolatedStringHandler.AppendFormatted(!report.GamingSessionActive);
			defaultInterpolatedStringHandler.AppendLiteral(", Load: ");
			defaultInterpolatedStringHandler.AppendFormatted(!report.SystemUnderHeavyLoad);
			defaultInterpolatedStringHandler.AppendLiteral(")");
			logger.Log(LogLevel.Info, LogCategory.General, defaultInterpolatedStringHandler.ToStringAndClear(), null, "EnterpriseIntelligentProfile");
			return report;
		}
		catch (Exception ex2)
		{
			Exception ex = ex2;
			_logger.LogError("System readiness validation failed: " + ex.Message, ex);
			report.ReadyForOptimization = false;
			report.ErrorMessage = ex.Message;
			return report;
		}
	}

	private async Task<EnterpriseSystemLoad> GetCurrentSystemLoadAsync(CancellationToken ct)
	{
		double cpuUsage = await _systemInfoService.GetCpuUsageAsync();
		double memoryUsage = await _systemInfoService.GetMemoryUsageAsync();
		double gpuUsage = await _systemInfoService.GetGpuUsageAsync();
		return new EnterpriseSystemLoad
		{
			CpuUsagePercent = cpuUsage,
			MemoryUsagePercent = memoryUsage,
			GpuUsagePercent = gpuUsage,
			IsUnderHeavyLoad = (cpuUsage > 80.0 || memoryUsage > 85.0)
		};
	}

	private async Task<VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization[]> GenerateValidatedOptimizationsAsync(DetailedHardwareProfile hardware, List<string> runningGames, bool validateEffectiveness, IntelligentProfileType profileType, CancellationToken ct)
	{
		List<VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization> optimizations = new List<VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization>();
		AddProfileSpecificOptimizations(optimizations, profileType, hardware);
		_logger.Log(LogLevel.Info, LogCategory.System, "\ud83d\udd0e Verificando otimizações já existentes no Windows...", null, "EnterpriseIntelligentProfile");
		OptimizationCategory generalCategory = new OptimizationCategory
		{
			Name = "Intelligent Profile Check"
		};
		foreach (VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization opt4 in optimizations)
		{
			if (opt4.TargetRegistryValues != null)
			{
				foreach (string key2 in opt4.TargetRegistryValues.Keys)
				{
					if (!generalCategory.RegistryKeys.Contains(key2))
					{
						generalCategory.RegistryKeys.Add(key2);
					}
				}
			}
			if (opt4.TargetServiceStates == null)
			{
				continue;
			}
			foreach (string key in opt4.TargetServiceStates.Keys)
			{
				if (!generalCategory.Services.Contains(key))
				{
					generalCategory.Services.Add(key);
				}
			}
		}
		SystemStateSnapshot currentState = await _stateDetector.CaptureCurrentStateAsync(generalCategory, ct);
		List<VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization> pendingOptimizations = new List<VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization>();
		foreach (VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization opt3 in optimizations)
		{
			OptimizationStatus status = _stateDetector.AnalyzeOptimizationStatus(opt3, currentState);
			if (status == OptimizationStatus.AlreadyApplied)
			{
				_logger.Log(LogLevel.Info, LogCategory.Optimization, "[SKIPPED] Otimização '" + opt3.Name + "' já está aplicada no sistema.", null, "EnterpriseIntelligentProfile");
			}
			else
			{
				pendingOptimizations.Add(opt3);
			}
		}
		optimizations = pendingOptimizations;
		if (validateEffectiveness)
		{
			List<VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization> validatedOptimizations = new List<VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization>();
			foreach (VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization opt2 in optimizations)
			{
				ct.ThrowIfCancellationRequested();
				EffectivenessValidationResult validationResult = await _effectivenessValidator.ValidateOptimizationEffectivenessAsync(opt2, ConvertToHardwareProfile(hardware), ct);
				if (validationResult.IsEffective)
				{
					validatedOptimizations.Add(opt2);
					ILoggingService logger = _logger;
					DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(50, 2);
					defaultInterpolatedStringHandler.AppendLiteral("Optimization ");
					defaultInterpolatedStringHandler.AppendFormatted(opt2.Name);
					defaultInterpolatedStringHandler.AppendLiteral(" validated as effective (");
					defaultInterpolatedStringHandler.AppendFormatted(validationResult.PredictedImprovement.ImprovementPercentage, "F1");
					defaultInterpolatedStringHandler.AppendLiteral("% predicted)");
					logger.Log(LogLevel.Info, LogCategory.General, defaultInterpolatedStringHandler.ToStringAndClear(), null, "EnterpriseIntelligentProfile");
				}
				else
				{
					_logger.Log(LogLevel.Warning, LogCategory.General, "Optimization " + opt2.Name + " skipped due to low effectiveness prediction", null, "EnterpriseIntelligentProfile");
				}
			}
			optimizations = validatedOptimizations;
		}
		List<VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization> gameSafeOptimizations = new List<VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization>();
		foreach (VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization opt in optimizations)
		{
			ct.ThrowIfCancellationRequested();
			GameCompatibilityResult compatibilityResult = await _gameProtector.CheckOptimizationCompatibilityAsync(new ActionRecommendation
			{
				Name = opt.Name
			}, ct);
			if (compatibilityResult.IsCompatible || !compatibilityResult.IsGamingSession)
			{
				gameSafeOptimizations.Add(opt);
			}
			else
			{
				_logger.Log(LogLevel.Warning, LogCategory.General, "Optimization " + opt.Name + " skipped due to game compatibility issues", null, "EnterpriseIntelligentProfile");
			}
		}
		return gameSafeOptimizations.ToArray();
	}

	public static HardwareProfile ConvertToHardwareProfile(DetailedHardwareProfile detailed)
	{
		HardwareProfile hardwareProfile = new HardwareProfile();
		hardwareProfile.Cpu = new VoltrisOptimizer.Core.SystemIntelligenceProfiler.Models.CpuInfo
		{
			Name = detailed.CpuAnalysis.Name,
			LogicalCores = detailed.CpuAnalysis.ThreadCount,
			PhysicalCores = detailed.CpuAnalysis.CoreCount,
			MaxClockSpeed = (int)detailed.CpuAnalysis.MaxClockSpeedMhz,
			Architecture = detailed.CpuAnalysis.Architecture
		};
		hardwareProfile.Ram = new VoltrisOptimizer.Core.SystemIntelligenceProfiler.Models.RamInfo
		{
			TotalMb = (int)(detailed.MemoryAnalysis.TotalBytes / 1048576),
			AvailableMb = (int)(detailed.MemoryAnalysis.AvailableBytes / 1048576)
		};
		VoltrisOptimizer.Core.SystemIntelligenceProfiler.Models.StorageInfo obj = new VoltrisOptimizer.Core.SystemIntelligenceProfiler.Models.StorageInfo
		{
			Type = detailed.StorageAnalysis.PrimaryStorageType.ToString()
		};
		DriveAnalysis? driveAnalysis = detailed.StorageAnalysis.Drives.FirstOrDefault();
		obj.TotalGb = (int)((driveAnalysis != null) ? (driveAnalysis!.TotalBytes / 1073741824) : 0);
		DriveAnalysis? driveAnalysis2 = detailed.StorageAnalysis.Drives.FirstOrDefault();
		obj.FreeGb = (int)((driveAnalysis2 != null) ? (driveAnalysis2!.FreeBytes / 1073741824) : 0);
		hardwareProfile.Storage = obj;
		hardwareProfile.HardwareScore = detailed.PerformanceScore * 10.0;
		HardwareProfile hardwareProfile2 = hardwareProfile;
		WorkloadClassification workloadClassification = detailed.WorkloadClassification;
		if (1 == 0)
		{
		}
		HardwareClass classification = workloadClassification switch
		{
			WorkloadClassification.Budget => HardwareClass.LowEnd, 
			WorkloadClassification.Mainstream => HardwareClass.MidRange, 
			WorkloadClassification.Workstation => HardwareClass.HighEnd, 
			WorkloadClassification.GamingFocused => HardwareClass.Workstation, 
			WorkloadClassification.GamingHighPerformance => HardwareClass.Server, 
			_ => HardwareClass.Unknown};
		if (1 == 0)
		{
		}
		hardwareProfile2.Classification = classification;
		return hardwareProfile;
	}

	private VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization CreateMemoryOptimization()
	{
		return new VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization
		{
			Name = "Memory Management Optimization",
			Description = "Optimize memory allocation and paging behavior",
			Category = new OptimizationCategory
			{
				Name = "Memory"
			},
			TargetRegistryValues = new Dictionary<string, object> { { "HKLM\\SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Memory Management\\LargeSystemCache", 0 } },
			MinRamGb = 8
		};
	}

	private VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization CreateCpuOptimization()
	{
		return new VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization
		{
			Name = "CPU Scheduling Optimization",
			Description = "Optimize processor scheduling for better responsiveness",
			Category = new OptimizationCategory
			{
				Name = "CPU"
			},
			TargetRegistryValues = new Dictionary<string, object> { { "HKLM\\SYSTEM\\CurrentControlSet\\Control\\PriorityControl\\Win32PrioritySeparation", 38 } },
			MinCpuCores = 2
		};
	}

	private VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization CreateStorageOptimization()
	{
		return new VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization
		{
			Name = "SSD Optimization",
			Description = "Optimize storage settings for solid state drives",
			Category = new OptimizationCategory
			{
				Name = "Storage"
			},
			TargetRegistryValues = new Dictionary<string, object> { { "HKLM\\SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters\\DisableTaskOffload", 0 } },
			RequiresSSD = true
		};
	}

	private void AddProfileSpecificOptimizations(List<VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization> optimizations, IntelligentProfileType profileType, DetailedHardwareProfile hardware)
	{
		if (hardware.MemoryAnalysis.Suitability >= MemorySuitability.Good)
		{
			optimizations.Add(CreateMemoryOptimization());
		}
		if (hardware.CpuAnalysis.Tier >= CpuTier.MidRange)
		{
			optimizations.Add(CreateCpuOptimization());
		}
		if (hardware.StorageAnalysis.HasFastStorage)
		{
			optimizations.Add(CreateStorageOptimization());
		}
		switch (profileType)
		{
		case IntelligentProfileType.GamerCompetitive:
		case IntelligentProfileType.GamerSinglePlayer:
			_logger.Log(LogLevel.Info, LogCategory.System, "Configurando otimizações para GAMING (Baixa Latência)", null, "EnterpriseIntelligentProfile");
			optimizations.Add(new VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization
			{
				Name = "Gaming Response Time",
				Description = "Otimiza a resposta do Windows para inputs de jogos",
				TargetRegistryValues = new Dictionary<string, object>
				{
					{ "HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile\\Tasks\\Games\\GPU Priority", 8 },
					{ "HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile\\Tasks\\Games\\Priority", 6 }
				}
			});
			break;
		case IntelligentProfileType.WorkOffice:
			_logger.Log(LogLevel.Info, LogCategory.System, "Configurando otimizações para TRABALHO (Estabilidade e Rede)", null, "EnterpriseIntelligentProfile");
			optimizations.Add(new VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization
			{
				Name = "Work Stability Profile",
				Description = "Foco em estabilidade de rede e serviços essenciais",
				TargetRegistryValues = new Dictionary<string, object> { { "HKLM\\SYSTEM\\CurrentControlSet\\Services\\LanmanWorkstation\\Parameters\\MaxCmds", 100 } }
			});
			break;
		case IntelligentProfileType.CreativeVideoEditing:
			_logger.Log(LogLevel.Info, LogCategory.System, "Configurando otimizações para EDIÇÃO (Multithreading)", null, "EnterpriseIntelligentProfile");
			optimizations.Add(new VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization
			{
				Name = "Creative Content Pipeline",
				Description = "Otimiza o processamento em threads para edição de vídeo/foto",
				TargetRegistryValues = new Dictionary<string, object> { { "HKLM\\SYSTEM\\CurrentControlSet\\Control\\PriorityControl\\Win32PrioritySeparation", 24 } }
			});
			break;
		case IntelligentProfileType.EnterpriseSecure:
			_logger.Log(LogLevel.Info, LogCategory.System, "Configurando otimizações para ENTERPRISE (Segurança e Auditoria)", null, "EnterpriseIntelligentProfile");
			optimizations.Add(new VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization
			{
				Name = "Enterprise Network Stability",
				Description = "Estabilidade de rede corporativa com auditoria",
				TargetRegistryValues = new Dictionary<string, object> { { "HKLM\\SYSTEM\\CurrentControlSet\\Services\\LanmanWorkstation\\Parameters\\MaxCmds", 100 } }
			});
			optimizations.Add(new VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization
			{
				Name = "Enterprise Security Hardening",
				Description = "Fortalecimento de segurança para ambientes corporativos",
				TargetRegistryValues = new Dictionary<string, object>
				{
					{ "HKLM\\SYSTEM\\CurrentControlSet\\Control\\Session Manager\\kernel\\DisableExceptionChainValidation", 0 },
					{ "HKLM\\SYSTEM\\CurrentControlSet\\Services\\LanmanServer\\Parameters\\Size", 3 }
				}
			});
			optimizations.Add(new VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization
			{
				Name = "Enterprise Audit Optimization",
				Description = "Otimização de logs e auditoria corporativa",
				TargetRegistryValues = new Dictionary<string, object> { { "HKLM\\SYSTEM\\CurrentControlSet\\Services\\EventLog\\System\\MaxSize", 20971520 } }
			});
			break;
		case IntelligentProfileType.GeneralBalanced:
			_logger.Log(LogLevel.Info, LogCategory.System, "Configurando otimizações para BALANCED (Uso Geral)", null, "EnterpriseIntelligentProfile");
			optimizations.Add(new VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization
			{
				Name = "Balanced Performance",
				Description = "Configurações equilibradas para uso geral do sistema",
				TargetRegistryValues = new Dictionary<string, object> { { "HKLM\\SYSTEM\\CurrentControlSet\\Control\\PriorityControl\\Win32PrioritySeparation", 2 } }
			});
			break;
		case IntelligentProfileType.GamerSimulation:
		case IntelligentProfileType.GamerMMO:
		case IntelligentProfileType.GamerStrategy:
		case IntelligentProfileType.DeveloperProgramming:
			break;
		}
	}
}
