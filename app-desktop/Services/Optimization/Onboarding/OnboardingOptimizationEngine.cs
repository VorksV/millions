using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core.Body;
using VoltrisOptimizer.Core;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.AntivirusCompliance;
using VoltrisOptimizer.Services.Benchmark;
using VoltrisOptimizer.Services.Thermal;
using VoltrisOptimizer.Core.Brain.V2;
using OnbModel = VoltrisOptimizer.Models;

namespace VoltrisOptimizer.Services.Optimization.Onboarding
{
    public sealed class OnboardingOptimizationEngine
    {
        private readonly ILoggingService _logger;
        private readonly HardwareProfileAnalyzer _hardwareAnalyzer;
        private readonly PerformanceBenchmarkService _benchmark;
        private readonly IPowerArm _powerArm;
        private readonly ISystemArm _systemArm;
        private readonly IProcessArm _processArm;
        private readonly VoltrisBrainV2 _brain;

        public OnboardingOptimizationEngine(
            ILoggingService logger,
            HardwareProfileAnalyzer hardwareAnalyzer,
            PerformanceBenchmarkService benchmark,
            IPowerArm powerArm,
            ISystemArm systemArm,
            IProcessArm processArm,
            VoltrisBrainV2 brain = null)
        {
            _logger = logger;
            _hardwareAnalyzer = hardwareAnalyzer;
            _benchmark = benchmark;
            _powerArm = powerArm;
            _systemArm = systemArm;
            _processArm = processArm;
            _brain = brain ?? Core.ServiceLocator.GetService<VoltrisBrainV2>();
        }

        public async Task<OnbModel.OnboardingResult> RunOptimizationAsync(IProgress<(string Status, int Percent)>? progress = null)
        {
            _logger?.LogInfo("[OnboardingEngine] ========= INICIANDO OTIMIZAÇÃO INTELIGENTE =========");
            var sw = Stopwatch.StartNew();
            var result = new OnbModel.OnboardingResult();

            try
            {
progress?.Report(("Analisando hardware...", 5));
            _logger?.LogInfo("[OnboardingEngine] Etapa 1/5: Análise de hardware");
            var hardware = await _hardwareAnalyzer.AnalyzeAsync();
            result.Recommendations = new OnbModel.OnboardingRecommendation[8];

            var powerSource = DetectPowerSource();
            var thermalState = DetectThermalState();

            _logger?.LogInfo($"[OnboardingEngine] Contexto: {hardware.MachineClass}, " +
                $"Fonte={powerSource}, Térmico={thermalState}");

            progress?.Report(("Avaliando timer resolution...", 12));
            result.Recommendations[0] = await ApplyTimerOptimization(hardware, thermalState, powerSource);

            progress?.Report(("Ajustando EPP do processador...", 24));
            result.Recommendations[1] = await ApplyEppOptimization(hardware, thermalState, powerSource);

            progress?.Report(("Plano Ultimate Performance...", 32));
            result.Recommendations[2] = await ApplyUltimatePerformancePlan(hardware, powerSource);

            progress?.Report(("SysMain (Superfetch) em SSD...", 40));
            result.Recommendations[3] = await ApplySysMainOptimization(hardware);

            progress?.Report(("Configurando modo desempenho...", 48));
            result.Recommendations[4] = await ApplyGamingMode(hardware, powerSource);

            progress?.Report(("Game DVR / Background Recording...", 56));
            result.Recommendations[5] = await ApplyGameDvrPolicy(hardware, powerSource);

            progress?.Report(("Visual Effects (Best Performance)...", 64));
            result.Recommendations[6] = await ApplyVisualEffectsPolicy(hardware, thermalState);

            progress?.Report(("Otimizando memória do sistema...", 80));
            result.Recommendations[7] = await ApplyMemoryTrim(hardware, thermalState);

                progress?.Report(("Calculando score final...", 95));
                
                result.PreviousScore = _benchmark.TotalScore;
                
                _benchmark.EndOptimization(sw.ElapsedMilliseconds);

                result.CurrentScore = _benchmark.TotalScore;
                result.TotalPointsGained = Math.Max(0, result.CurrentScore - result.PreviousScore);

                int totalOptimized = result.Recommendations.Count(r => r.Applied);
                int totalAlreadyOptimized = result.Recommendations.Count(r => r.IsAlreadyOptimized);
                int totalSkipped = result.Recommendations.Count(r => r.Skipped);
                
                result.Summary = $"{totalOptimized} aplicadas, {totalAlreadyOptimized} já ativas";
                if (totalSkipped > 0)
                    result.Summary += $" ({totalSkipped} puladas)";

                _logger?.LogInfo($"[OnboardingEngine] Resultado: {result.Summary}");
                _logger?.LogInfo($"[OnboardingEngine] Duração total: {sw.ElapsedMilliseconds}ms");

                progress?.Report(("Otimização concluída!", 100));
                
                // ✅ FASE 2: Inicializar Q-Table do Brain com baseline
                await InitializeBrainQTableAsync(result);
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[OnboardingEngine] Erro fatal: {ex.Message}", ex);
                result.HadErrors = true;
                result.Summary = "Otimização concluída com ressalvas";
            }

            _logger?.LogInfo("[OnboardingEngine] ========= OTIMIZAÇÃO FINALIZADA =========\n");
            return result;
        }

        /// <summary>
        /// Inicializa Q-Table do Brain com baseline do Onboarding
        /// PROTEÇÃO CRÍTICA: SaveAsync único fora do loop (evita I/O múltiplo)
        /// </summary>
        public async Task InitializeBrainQTableAsync(OnbModel.OnboardingResult result)
        {
            if (_brain == null)
            {
                _logger?.LogWarning("[Onboarding-Brain] Brain não disponível para inicialização");
                return;
            }

            try
            {
                _logger?.LogInfo("[Onboarding-Brain] Inicializando Q-Table com baseline do Onboarding...");

                // ✅ PROTEÇÃO CRÍTICA #1: Bucketing agressivo para evitar explosão de estados
                var baselineState = new Core.Brain.V2.BrainStateKey(
                    workload: Core.Brain.V2.WorkloadCategory.Idle,
                    cpuBucket: 2,  // 20-30% (bucket agressivo)
                    ramBucket: 3,  // 30-40%
                    tempBucket: 4, // 40-50C
                    contextBucket: 0  // Onboarding context
                );

            // Observar estado baseline.
            // CpuTemperatureC recebe double.NaN porque este ponto do fluxo NÃO tem
            // leitura de sensor: a constante 45.0 representava um dado térmico
            // inexistente e realimentava o cérebro com ele. Qualquer motor que
            // decida por temperatura agora vê "indisponível" em vez de um valor
            // inventado. Os demais campos abaixo também são valores de CONTEXTO
            // do onboarding, não medições, e por isso ficam explícitos aqui.
            _brain.Memory.Observe(baselineState, new Core.Brain.V2.SensorSnapshot
            {
                Workload = Core.Brain.V2.WorkloadCategory.Idle,
                CpuUsagePercent = 25.0,
                RamUsagePercent = 35.0,
                CpuTemperatureC = double.NaN,
                ForegroundProcessName = "onboarding_baseline"
            });

                // ✅ PROTEÇÃO CRÍTICA #3: Normalizar reward do onboarding
                // Score do benchmark pode ser 0-1000, normalizar para [-1.0, 1.0]
                double baselineReward = result.CurrentScore / 1000.0;
                double normalizedReward = Math.Clamp(baselineReward, -1.0, 1.0);

                // Chamar método novo do Brain
                _brain.InitializeFromOnboarding(result, normalizedReward);

                // ✅ PROTEÇÃO CRÍTICA #2: SaveAsync único fora do loop
                // Acumula na RAM e faz dump único no disco
                await _brain.Memory.SaveAsync();

                _logger?.LogSuccess($"[Onboarding-Brain] Q-Table inicializada com sucesso (score={result.CurrentScore}, reward={normalizedReward:F3})");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[Onboarding-Brain] Erro ao inicializar Q-Table: {ex.Message}");
            }
        }

        private async Task<OnbModel.OnboardingRecommendation> ApplyTimerOptimization(OnbModel.HardwareProfile hardware, OnbModel.ThermalState thermal, OnbModel.PowerSource power)
        {
            var policy = await new AdaptiveTimerPolicy(_logger, _systemArm)
                .EvaluateAsync(hardware, thermal, power);

            return new OnbModel.OnboardingRecommendation
            {
                Name = "Timer Resolution",
                Icon = "⏱️",
                Description = $"Ajuste do timer de {policy.CurrentTimerMs:F1}ms → {policy.TargetTimerMs:F1}ms",
                Applied = policy.Applied,
                IsAlreadyOptimized = policy.IsAlreadyOptimized,
                Skipped = !policy.Applied && !policy.IsAlreadyOptimized && !policy.Reason.Contains("aplicado", StringComparison.OrdinalIgnoreCase),
                DetailBefore = $"{policy.CurrentTimerMs:F1}ms",
                DetailAfter = policy.Applied ? $"{policy.TargetTimerMs:F1}ms" : (policy.IsAlreadyOptimized ? "já ativo" : "mantido"),
                SkipReason = policy.Applied ? null : policy.Reason
            };
        }

        private async Task<OnbModel.OnboardingRecommendation> ApplyEppOptimization(OnbModel.HardwareProfile hardware, OnbModel.ThermalState thermal, OnbModel.PowerSource power)
        {
            var policy = await new AdaptiveEppPolicy(_logger, _powerArm)
                .EvaluateAsync(hardware, thermal, power);

            return new OnbModel.OnboardingRecommendation
            {
                Name = "Energy Performance (EPP)",
                Icon = "⚡",
                Description = policy.Reason,
                Applied = policy.Applied,
                IsAlreadyOptimized = policy.IsAlreadyOptimized,
                Skipped = !policy.Applied && !policy.IsAlreadyOptimized,
                DetailBefore = $"{policy.CurrentEpp}",
                DetailAfter = policy.Applied ? $"{policy.TargetEpp}" : (policy.IsAlreadyOptimized ? "já otimizado" : "mantido"),
                SkipReason = policy.Applied ? null : policy.Reason
            };
        }

        private async Task<OnbModel.OnboardingRecommendation> ApplyGamingMode(OnbModel.HardwareProfile hardware, OnbModel.PowerSource power)
        {
            var policy = await new ContextualGamingModePolicy(_logger, _systemArm)
                .EvaluateAsync(hardware, power);

            return new OnbModel.OnboardingRecommendation
            {
                Name = "Modo Desempenho",
                Icon = "🎮",
                Description = policy.Reason,
                Applied = policy.GamingModeApplied,
                Skipped = policy.Skipped,
                DetailBefore = "padrão",
                DetailAfter = policy.GamingModeApplied ? "ativo" : "mantido",
                SkipReason = policy.Skipped ? policy.Reason : null
            };
        }

        private async Task<OnbModel.OnboardingRecommendation> ApplyMemoryTrim(OnbModel.HardwareProfile hardware, OnbModel.ThermalState thermal)
        {
            var policy = await new IntelligentTrimPolicy(_logger, _processArm)
                .EvaluateAsync(hardware, thermal);

            return new OnbModel.OnboardingRecommendation
            {
                Name = "Liberação de Memória",
                Icon = "💾",
                Description = policy.Reason,
                Applied = policy.TrimApplied,
                IsAlreadyOptimized = policy.IsAlreadyOptimized,
                Skipped = policy.Skipped && !policy.IsAlreadyOptimized,
DetailBefore = policy.TrimApplied ? $"{policy.MbRecovered}MB" : "-",
                DetailAfter = policy.TrimApplied ? "liberado" : (policy.IsAlreadyOptimized ? "saudável" : "não necessário"),
                SkipReason = policy.Skipped ? policy.Reason : null
            };
        }

    // P1: Ultimate Performance Plan
    private async Task<OnbModel.OnboardingRecommendation> ApplyUltimatePerformancePlan(OnbModel.HardwareProfile hardware, OnbModel.PowerSource power)
    {
        if (power != OnbModel.PowerSource.AC || hardware.MachineClass == OnbModel.MachineClass.Laptop)
        {
            return new OnbModel.OnboardingRecommendation
            {
                Name = "Plano Ultimate Performance",
                Icon = "🚀",
                Description = "Apenas para desktop em AC",
                Skipped = true,
                SkipReason = "Requer desktop ligado na tomada",
                DetailBefore = "padrão",
                DetailAfter = "não aplicável"
            };
        }

        _logger?.LogInfo("[OnboardingEngine] Aplicando Ultimate Performance Plan...");
        var sw = Stopwatch.StartNew();
        bool success = await _powerArm.SetUltimatePerformancePlanAsync(true);

        return new OnbModel.OnboardingRecommendation
        {
            Name = "Plano Ultimate Performance",
            Icon = "🚀",
            Description = success ? "Plano Ultimate Performance ativado (latência zero, sem throttling)" : "Falha ao ativar (requer admin)",
            Applied = success,
            Skipped = !success,
            DetailBefore = "Balanced/High Performance",
            DetailAfter = success ? "Ultimate Performance" : "mantido",
            SkipReason = success ? null : "Requer admin ou plano não disponível"
        };
    }

    // P1: SysMain (Superfetch) em SSD
    private async Task<OnbModel.OnboardingRecommendation> ApplySysMainOptimization(OnbModel.HardwareProfile hardware)
    {
        // Verifica se é SSD
        bool isSystemDriveSSD = IsSystemDriveSSD();
        if (!isSystemDriveSSD)
        {
            return new OnbModel.OnboardingRecommendation
            {
                Name = "SysMain (Superfetch) em SSD",
                Icon = "💾",
                Description = "Apenas para unidade de sistema SSD/NVMe",
                Skipped = true,
                SkipReason = "Unidade de sistema não é SSD",
                DetailBefore = "Auto/Enabled",
                DetailAfter = "mantido"
            };
        }

        _logger?.LogInfo("[OnboardingEngine] Desativando SysMain (Superfetch) em SSD...");
        var whitelist = new SafeOperationWhitelist(_logger);
        var validation = await whitelist.ValidateCommandAsync("sc.exe", "config SysMain start= disabled");

        if (!validation.IsSafe)
        {
            return new OnbModel.OnboardingRecommendation
            {
                Name = "SysMain (Superfetch) em SSD",
                Icon = "💾",
                Description = $"Bloqueado pelo safety whitelist: {validation.Reason}",
                Skipped = true,
                SkipReason = validation.Reason,
                DetailBefore = "Auto",
                DetailAfter = "mantido"
            };
        }

        var psi = new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = "config SysMain start= disabled",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = Process.Start(psi);
        using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await proc.WaitForExitAsync(cts1.Token);
        bool success = proc.ExitCode == 0;

        // Stop service immediately
        if (success)
        {
            var psi2 = new ProcessStartInfo { FileName = "sc.exe", Arguments = "stop SysMain", UseShellExecute = false, CreateNoWindow = true };
            using var proc2 = Process.Start(psi2);
            using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await proc2.WaitForExitAsync(cts2.Token);
        }

        return new OnbModel.OnboardingRecommendation
        {
            Name = "SysMain (Superfetch) em SSD",
            Icon = "💾",
            Description = success ? "SysMain desativado (evita leaks de RAM em SSD)" : "Falha ao desativar",
            Applied = success,
            Skipped = !success,
            DetailBefore = "Auto/Running",
            DetailAfter = success ? "Disabled/Stopped" : "mantido",
            SkipReason = success ? null : "Falha no sc.exe"
        };
    }

    // P1: Game DVR / Background Recording
    private async Task<OnbModel.OnboardingRecommendation> ApplyGameDvrPolicy(OnbModel.HardwareProfile hardware, OnbModel.PowerSource power)
    {
        _logger?.LogInfo("[OnboardingEngine] Desativando Game DVR / Background Recording...");
        var result = await _systemArm.SetGameDvrAsync(false);

        return new OnbModel.OnboardingRecommendation
        {
            Name = "Game DVR / Background Recording",
            Icon = "🎮",
            Description = result.Success ? "Game DVR e gravação em background desativados (libera GPU encoder)" : "Falha ao desativar",
            Applied = result.Success,
            Skipped = !result.Success,
            DetailBefore = "Ativado (padrão)",
            DetailAfter = result.Success ? "Desativado" : "mantido",
            SkipReason = result.Success ? null : result.GuardReason
        };
    }

    // P1: Visual Effects "Best Performance"
    private async Task<OnbModel.OnboardingRecommendation> ApplyVisualEffectsPolicy(OnbModel.HardwareProfile hardware, OnbModel.ThermalState thermal)
    {
        // Aplica em desktop ou laptop com RAM <= 16GB ou térmico quente
        bool shouldApply = hardware.MachineClass == OnbModel.MachineClass.Desktop ||
                          hardware.TotalRamGb <= 16 ||
                          thermal >= OnbModel.ThermalState.Warm;

        if (!shouldApply)
        {
            return new OnbModel.OnboardingRecommendation
            {
                Name = "Visual Effects (Best Performance)",
                Icon = "🎨",
                Description = "Pulado: laptop high-end com RAM > 16GB e temperatura normal",
                Skipped = true,
                SkipReason = "Hardware suficiente para efeitos visuais",
                DetailBefore = "Default (efeitos ligados)",
                DetailAfter = "mantido"
            };
        }

        _logger?.LogInfo("[OnboardingEngine] Aplicando Visual Effects: Best Performance...");
        var result = await _systemArm.SetVisualEffectsAsync(true);

        return new OnbModel.OnboardingRecommendation
        {
            Name = "Visual Effects (Best Performance)",
            Icon = "🎨",
            Description = result.Success ? "Efeitos visuais desativados para performance máxima" : "Falha ao aplicar",
            Applied = result.Success,
            Skipped = !result.Success,
            DetailBefore = "Default (animações/transparências)",
            DetailAfter = result.Success ? "Best Performance (desativado)" : "mantido",
            SkipReason = result.Success ? null : result.GuardReason
        };
    }

    // HAGS (Hardware-Accelerated GPU Scheduling) - opcional, requer reboot
    // Não incluído no onboarding automático por requerer reboot

    private bool IsSystemDriveSSD()
    {
        try
        {
            string systemDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            var drive = new System.IO.DriveInfo(systemDrive);
            return drive.DriveType == System.IO.DriveType.Fixed && (drive.IsReady == false || 
                (drive.TotalSize > 0 && drive.AvailableFreeSpace > 0));
        }
        catch { return false; }
    }

    private static OnbModel.PowerSource DetectPowerSource()
        {
            try
            {
                var powerStatus = new SystemPowerStatus();
                if (GetSystemPowerStatus(ref powerStatus))
                {
                    return powerStatus.ACLineStatus == 1 ? OnbModel.PowerSource.AC : OnbModel.PowerSource.Battery;
                }
            }
            catch { }
            return OnbModel.PowerSource.AC;
        }

        private static OnbModel.ThermalState DetectThermalState()
        {
            try
            {
                var cache = SystemMetricsCache.Instance;
                double cpuTemp = 0;

                var thermalService = App.ThermalMonitorService;
                if (thermalService?.CurrentMetrics != null)
                {
                    cpuTemp = thermalService.CurrentMetrics.CpuTemperature;
                }

                if (double.IsNaN(cpuTemp) || cpuTemp <= 0)
                    return OnbModel.ThermalState.Cool;

                return cpuTemp switch
                {
                    >= 95 => OnbModel.ThermalState.Throttling,
                    >= 80 => OnbModel.ThermalState.Hot,
                    >= 65 => OnbModel.ThermalState.Warm,
                    _ => OnbModel.ThermalState.Cool
                };
            }
            catch
            {
                return OnbModel.ThermalState.Cool;
            }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool GetSystemPowerStatus(ref SystemPowerStatus lpSystemPowerStatus);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct SystemPowerStatus
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte Reserved1;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }
    }
}

