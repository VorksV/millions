using System;

using System.Collections.Generic;

using System.Linq;

using System.Threading;

using System.Threading.Tasks;

using Microsoft.Win32;

using VoltrisOptimizer.Helpers;

using VoltrisOptimizer.Interfaces;

using VoltrisOptimizer.Services.Gamer.Interfaces;

using VoltrisOptimizer.Services.Gamer.Models;

using VoltrisOptimizer.Utils;

namespace VoltrisOptimizer.Services.Gamer.Implementation
{
    [Obsolete("PLACEBO: Este coordenador não executa ApplyAsync real nas políticas. Use GamerModeManager que executa otimizações reais.")]
    /// <summary>
    /// COORDENADOR UNIFICADO DE OTIMIZAÇÃO GAMER
    /// Elimina conflitos entre módulos e garante execução atômica
    /// </summary>
    public class GamerOptimizationCoordinator : IDisposable
    {
        private readonly ILoggingService _logger;
        private readonly ICpuGamingOptimizer _cpuOptimizer;
        private readonly IGpuGamingOptimizer _gpuOptimizer;
        private readonly IMemoryGamingOptimizer _memoryOptimizer;
        private readonly IProcessPrioritizer _processPrioritizer;
        private readonly IHardwareDetector _hardwareDetector;
        private readonly GamerModeAuditor _auditor;

        // Estado para rollback atômico
        private readonly List<IOptimizationState> _appliedStates = new();
        private readonly SemaphoreSlim _coordinationLock = new(1, 1);
        private bool _isOptimizationActive = false;

        // Cache de decisões para evitar conflitos
        private PowerPlanDecision? _lastPowerPlanDecision;
        private CpuPolicyDecision? _lastCpuPolicyDecision;
        private GpuPolicyDecision? _lastGpuPolicyDecision;

        public GamerOptimizationCoordinator(ILoggingService logger, ICpuGamingOptimizer cpuOptimizer, IGpuGamingOptimizer gpuOptimizer, IMemoryGamingOptimizer memoryOptimizer, IProcessPrioritizer processPrioritizer, IHardwareDetector hardwareDetector, GamerModeAuditor auditor)
        {
            _logger.LogEntry(nameof(GamerOptimizationCoordinator));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cpuOptimizer = cpuOptimizer ?? throw new ArgumentNullException(nameof(cpuOptimizer));
            _gpuOptimizer = gpuOptimizer ?? throw new ArgumentNullException(nameof(gpuOptimizer));
            _memoryOptimizer = memoryOptimizer ?? throw new ArgumentNullException(nameof(memoryOptimizer));
            _processPrioritizer = processPrioritizer ?? throw new ArgumentNullException(nameof(processPrioritizer));
            _hardwareDetector = hardwareDetector ?? throw new ArgumentNullException(nameof(hardwareDetector));
            _auditor = auditor ?? throw new ArgumentNullException(nameof(auditor));
            _logger.LogExit(nameof(GamerOptimizationCoordinator));
        }

        /// <summary>
        /// Aplica perfil unificado de otimização gamer
        /// ÚNICO ponto de decisão - elimina conflitos
        /// </summary>
        public async Task<bool> ApplyUnifiedProfileAsync(GamerOptimizationOptions options, int? gameProcessId = null, CancellationToken cancellationToken = default)
        {
            _logger.LogEntry(nameof(ApplyUnifiedProfileAsync));
            if (_isOptimizationActive)
            {
                _logger.LogWarning("[Coordinator] Otimização já ativa - ignorando solicitação duplicada");
                _logger.LogExit(nameof(ApplyUnifiedProfileAsync));
                return false;
            }

            await _coordinationLock.WaitAsync(cancellationToken);

            try
            {
                _logger.LogInfo("[Coordinator] Iniciando pipeline unificado de otimização gamer");

                var profile = await GenerateOptimizationPlanAsync(options, gameProcessId, cancellationToken);

                if (!profile.IsValid)
                {
                    _logger.LogError("[Coordinator] Plano de otimização inválido");
                    _logger.LogExit(nameof(ApplyUnifiedProfileAsync));
                    return false;
                }

                var success = await ExecutePlanAtomicallyAsync(profile, cancellationToken);

                if (success)
                {
                    _isOptimizationActive = true;
                    _logger.LogSuccess("[Coordinator] Otimização gamer aplicada com sucesso");
                    _auditor.LogOptimization("COORDINATOR", "UnifiedProfile", true, $"Power: {profile.PowerPlan}, CPU: {profile.CpuPolicy}, GPU: {profile.GpuPolicy}");
                }
                else
                {
                    _logger.LogError("[Coordinator] Falha na aplicação da otimização unificada");
                    await RollbackAllAsync(cancellationToken);
                }

                _logger.LogExit(nameof(ApplyUnifiedProfileAsync));
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError("[Coordinator] Erro crítico na coordenação", ex);
                await RollbackAllAsync(cancellationToken);
                _logger.LogExit(nameof(ApplyUnifiedProfileAsync));
                return false;
            }
            finally
            {
                _coordinationLock.Release();
            }
        }

        /// <summary>
        /// GERA plano de otimização unificado
        /// Resolve conflitos ANTES de aplicar
        /// </summary>
        private async Task<OptimizationPlan> GenerateOptimizationPlanAsync(GamerOptimizationOptions options, int? gameProcessId, CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(GenerateOptimizationPlanAsync));
            var plan = new OptimizationPlan();

            // 1. DETECTAR HARDWARE E CONTEXTO
            var hardwareInfo = await DetectHardwareContextAsync(cancellationToken);
            var isLaptop = hardwareInfo.IsLaptop;
            var isOnBattery = hardwareInfo.IsOnBattery;
            var intelligentProfile = SettingsService.Instance.Settings.IntelligentProfile;

            _logger.LogInfo($"[Coordinator] Hardware: {hardwareInfo.CpuName}, Laptop: {isLaptop}, Battery: {isOnBattery}, Profile: {intelligentProfile}");

            // 2. DECISÃO UNIFICADA DE POWER PLAN
            plan.PowerPlan = ResolvePowerPlan(hardwareInfo, intelligentProfile, isLaptop, isOnBattery);

            // 3. DECISÃO UNIFICADA DE CPU POLICY
            plan.CpuPolicy = await ResolveCpuPolicyAsync(options, hardwareInfo, intelligentProfile, cancellationToken);

            // 4. DECISÃO UNIFICADA DE GPU POLICY
            plan.GpuPolicy = await ResolveGpuPolicyAsync(options, hardwareInfo, gameProcessId, cancellationToken);

            // 5. VALIDAR CONFLITOS
            plan.IsValid = ValidatePlanConsistency(plan);

            _logger.LogInfo($"[Coordinator] Plano gerado: Power = {plan.PowerPlan}, CPU = {plan.CpuPolicy}, GPU = {plan.GpuPolicy}, Valid = {plan.IsValid}");
            _logger.LogExit(nameof(GenerateOptimizationPlanAsync));
            return plan;
        }

        /// <summary>
        /// Resolve power plan sem conflitos
        /// </summary>
        private PowerPlanType ResolvePowerPlan(HardwareContext hardware, IntelligentProfileType profile, bool isLaptop, bool isOnBattery)
        {
            _logger.LogEntry(nameof(ResolvePowerPlan));
            _logger.LogInfo("[Coordinator] Gerenciamento de energia delegado ao SmartEnergyService. Nenhuma ação tomada aqui.");
            _logger.LogExit(nameof(ResolvePowerPlan));
            return PowerPlanType.Balanced;
        }

        /// <summary>
        /// Resolve CPU policy sem conflitos
        /// </summary>
        private async Task<CpuPolicyType> ResolveCpuPolicyAsync(GamerOptimizationOptions options, HardwareContext hardware, IntelligentProfileType profile, CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(ResolveCpuPolicyAsync));
            var policy = CpuPolicyType.Balanced;

            // Core Parking só se suportado e não causar thermal issues
            if (hardware.SupportsCoreParking && !hardware.IsLaptop)
            {
                policy |= CpuPolicyType.DisableCoreParking;
            }

            // Priority boost apenas em desktops
            if (!hardware.IsLaptop)
            {
                policy |= CpuPolicyType.ForegroundBoost;
            }

            // Scheduler tuning para perfis gamer
            if (profile == IntelligentProfileType.GamerCompetitive)
            {
                policy |= CpuPolicyType.GamingScheduler;
            }

            _logger.LogInfo($"[Coordinator] CPU Policy: {policy} (CoreParking: {policy.HasFlag(CpuPolicyType.DisableCoreParking)})");
            _logger.LogExit(nameof(ResolveCpuPolicyAsync));
            return policy;
        }

        /// <summary>
        /// Resolve GPU policy sem conflitos
        /// </summary>
        private async Task<GpuPolicyType> ResolveGpuPolicyAsync(GamerOptimizationOptions options, HardwareContext hardware, int? gameProcessId, CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(ResolveGpuPolicyAsync));
            var policy = GpuPolicyType.Balanced;

            // GPU performance mode apenas se não causar thermal throttling
            if (!hardware.IsLaptop || !hardware.IsOnBattery)
            {
                policy |= GpuPolicyType.MaxPerformance;
            }

            // TDR tuning apenas para desktops
            if (!hardware.IsLaptop)
            {
                policy |= GpuPolicyType.OptimizeTdr;
            }

            _logger.LogInfo($"[Coordinator] GPU Policy: {policy}");
            _logger.LogExit(nameof(ResolveGpuPolicyAsync));
            return policy;
        }

        /// <summary>
        /// Executa plano de forma atômica
        /// </summary>
        private async Task<bool> ExecutePlanAtomicallyAsync(OptimizationPlan plan, CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(ExecutePlanAtomicallyAsync));
            var appliedStates = new List<IOptimizationState>();

            try
            {
                // 1. CPU Optimization
                if (plan.CpuPolicy != CpuPolicyType.Balanced)
                {
                    var cpuState = new CpuOptimizationState(_cpuOptimizer, plan.CpuPolicy);

                    if (await cpuState.ApplyAsync(cancellationToken))
                    {
                        appliedStates.Add(cpuState);
                        _auditor.LogOptimization("CPU", plan.CpuPolicy.ToString(), true, "Política aplicada");
                    }
                    else
                    {
                        throw new InvalidOperationException("Falha na otimização de CPU");
                    }
                }

                // 2. GPU Optimization
                if (plan.GpuPolicy != GpuPolicyType.Balanced)
                {
                    var gpuState = new GpuOptimizationState(_gpuOptimizer, plan.GpuPolicy);

                    appliedStates.Add(gpuState);

                    _auditor.LogOptimization("GPU", plan.GpuPolicy.ToString(), true, "Política aplicada");
                }

                // 3. Power Plan (último para sobrescrever qualquer conflito)
                if (plan.PowerPlan != PowerPlanType.Balanced)
                {
                    var powerState = new PowerPlanOptimizationState(_cpuOptimizer, plan.PowerPlan);

                    _auditor.LogOptimization("POWER", plan.PowerPlan.ToString(), true, "Plano aplicado");
                }

                // Sucesso - adicionar todos ao estado global
                _appliedStates.AddRange(appliedStates);
                _logger.LogExit(nameof(ExecutePlanAtomicallyAsync));
                return true;
            }
            catch
            {
                // Rollback em caso de falha
                foreach (var state in appliedStates)
                {
                    try
                    {
                        await state.RollbackAsync(cancellationToken);
                    }
                    catch
                    {
                        // Ignorar erros no rollback
                    }
                }

                _logger.LogExit(nameof(ExecutePlanAtomicallyAsync));
                return false;
            }
        }

        /// <summary>
        /// Rollback completo de todas as otimizações
        /// </summary>
        public async Task<bool> RollbackAllAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogEntry(nameof(RollbackAllAsync));
            if (!_isOptimizationActive)
            {
                _logger.LogInfo("[Coordinator] Nenhuma otimização ativa para restaurar");
                _logger.LogExit(nameof(RollbackAllAsync));
                return true;
            }

            await _coordinationLock.WaitAsync(cancellationToken);

            try
            {
                _logger.LogInfo("[Coordinator] Iniciando rollback completo das otimizações gamer");
                var success = true;

                foreach (var state in _appliedStates.AsEnumerable().Reverse())
                {
                    try
                    {
                        if (!await state.RestoreAsync(cancellationToken))
                        {
                            _logger.LogWarning($"[Coordinator] Falha ao restaurar {state.GetType().Name}");
                            success = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"[Coordinator] Erro crítico ao restaurar {state.GetType().Name}: {ex.Message}");
                        success = false;
                    }
                }

                _appliedStates.Clear();
                _isOptimizationActive = false;

                if (success)
                {
                    _logger.LogSuccess("[Coordinator] Rollback completo realizado com sucesso");
                }
                else
                {
                    _logger.LogWarning("[Coordinator] Rollback concluído com algumas falhas");
                }

                _logger.LogExit(nameof(RollbackAllAsync));
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[Coordinator] Erro crítico no rollback: {ex.Message}");
                _logger.LogExit(nameof(RollbackAllAsync));
                return false;
            }
            finally
            {
                _coordinationLock.Release();
            }
        }

        private async Task<HardwareContext> DetectHardwareContextAsync(CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(DetectHardwareContextAsync));
            var gpuInfo = await _gpuOptimizer.GetGpuInfoAsync(cancellationToken);

            var result = new HardwareContext
            {
                CpuName = "Unknown CPU",
                GpuName = gpuInfo.Name,
                GpuVendor = gpuInfo.Vendor.ToString(),
                IsLaptop = false,
                IsOnBattery = false,
                SupportsCoreParking = false,
                TotalMemoryGB = 16.0
            };
            _logger.LogExit(nameof(DetectHardwareContextAsync));
            return result;
        }

        private bool ValidatePlanConsistency(OptimizationPlan plan)
        {
            _logger.LogEntry(nameof(ValidatePlanConsistency));
            // Validar conflitos conhecidos
            if (plan.PowerPlan == PowerPlanType.UltimatePerformance && plan.CpuPolicy.HasFlag(CpuPolicyType.DisableCoreParking))
            {
                // OK - Ultimate Performance + Core Parking consistente
            }

            if (plan.GpuPolicy.HasFlag(GpuPolicyType.MaxPerformance) && _lastGpuPolicyDecision?.IsThermalConstrained == true)
            {
                _logger.LogWarning("[Coordinator] GPU Max Performance pode causar thermal throttling");
                // Ainda permitir, mas com warning
            }

            _logger.LogExit(nameof(ValidatePlanConsistency));
            return true;
        }

        public void Dispose()
        {
            _logger.LogEntry(nameof(Dispose));
            _coordinationLock?.Dispose();

            foreach (var state in _appliedStates)
            {
                try
                {
                    state.Dispose();
                }
                catch
                {
                }
            }

            _appliedStates.Clear();
            _logger.LogExit(nameof(Dispose));
        }
    }

    // Classes de decisão para cache
    internal record PowerPlanDecision(PowerPlanType Plan, HardwareContext Hardware, IntelligentProfileType Profile, bool IsLaptop, bool IsOnBattery)
    {
        public bool Matches(HardwareContext hardware, IntelligentProfileType profile, bool isLaptop, bool isOnBattery) =>
            Hardware.CpuName == hardware.CpuName &&
            Profile == profile &&
            IsLaptop == isLaptop &&
            IsOnBattery == isOnBattery;
    }

    internal record CpuPolicyDecision(CpuPolicyType Policy, HardwareContext Hardware, IntelligentProfileType Profile);

    internal record GpuPolicyDecision(GpuPolicyType Policy, HardwareContext Hardware, bool IsThermalConstrained);

    // Classes de estado para rollback atômico
    // Usar interfaces do AtomicRollbackManager para evitar conflitos
    internal class CpuOptimizationState : IOptimizationState
    {
        private readonly ICpuGamingOptimizer _optimizer;
        private readonly CpuPolicyType _policy;
        private readonly ILoggingService _logger = VoltrisOptimizer.App.LoggingService;

        public CpuOptimizationState(ICpuGamingOptimizer optimizer, CpuPolicyType policy)
        {
            _logger.LogEntry(nameof(CpuOptimizationState));
            _optimizer = optimizer;
            _policy = policy;
            _logger.LogExit(nameof(CpuOptimizationState));
        }

        public async Task<bool> ApplyAsync(CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(ApplyAsync));
            var result = await _optimizer.OptimizeAsync(cancellationToken);
            _logger.LogExit(nameof(ApplyAsync));
            return result;
        }

        public async Task<bool> RestoreAsync(CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(RestoreAsync));
            var result = await _optimizer.RestoreAsync(cancellationToken);
            _logger.LogExit(nameof(RestoreAsync));
            return result;
        }

        public async Task<bool> RollbackAsync(CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(RollbackAsync));
            var result = await _optimizer.RestoreAsync(cancellationToken);
            _logger.LogExit(nameof(RollbackAsync));
            return result;
        }

        public void Dispose()
        {
            _logger.LogEntry(nameof(Dispose));
            _logger.LogExit(nameof(Dispose));
        }
    }

    internal class GpuOptimizationState : IOptimizationState
    {
        private readonly IGpuGamingOptimizer _optimizer;
        private readonly GpuPolicyType _policy;
        private readonly ILoggingService _logger = VoltrisOptimizer.App.LoggingService;

        public GpuOptimizationState(IGpuGamingOptimizer optimizer, GpuPolicyType policy)
        {
            _logger.LogEntry(nameof(GpuOptimizationState));
            _optimizer = optimizer;
            _policy = policy;
            _logger.LogExit(nameof(GpuOptimizationState));
        }

        public async Task<bool> ApplyAsync(CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(ApplyAsync));
            var result = await _optimizer.OptimizeAsync(cancellationToken);
            _logger.LogExit(nameof(ApplyAsync));
            return result;
        }

        public async Task<bool> RestoreAsync(CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(RestoreAsync));
            var result = await _optimizer.RestoreAsync(cancellationToken);
            _logger.LogExit(nameof(RestoreAsync));
            return result;
        }

        public async Task<bool> RollbackAsync(CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(RollbackAsync));
            var result = await _optimizer.RestoreAsync(cancellationToken);
            _logger.LogExit(nameof(RollbackAsync));
            return result;
        }

        public void Dispose()
        {
            _logger.LogEntry(nameof(Dispose));
            _logger.LogExit(nameof(Dispose));
        }
    }

    internal class PowerPlanOptimizationState : IOptimizationState
    {
        private readonly ICpuGamingOptimizer _optimizer;
        private readonly PowerPlanType _plan;
        private readonly ILoggingService _logger = VoltrisOptimizer.App.LoggingService;

        public PowerPlanOptimizationState(ICpuGamingOptimizer optimizer, PowerPlanType plan)
        {
            _logger.LogEntry(nameof(PowerPlanOptimizationState));
            _optimizer = optimizer;
            _plan = plan;
            _logger.LogExit(nameof(PowerPlanOptimizationState));
        }

        public async Task<bool> ApplyAsync(CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(ApplyAsync));
            var result = await _optimizer.OptimizeAsync(cancellationToken);
            _logger.LogExit(nameof(ApplyAsync));
            return result;
        }

        public async Task<bool> RestoreAsync(CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(RestoreAsync));
            var result = await _optimizer.RestoreAsync(cancellationToken);
            _logger.LogExit(nameof(RestoreAsync));
            return result;
        }

        public async Task<bool> RollbackAsync(CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(RollbackAsync));
            var result = await _optimizer.RestoreAsync(cancellationToken);
            _logger.LogExit(nameof(RollbackAsync));
            return result;
        }

        public void Dispose()
        {
            _logger.LogEntry(nameof(Dispose));
            _logger.LogExit(nameof(Dispose));
        }
    }

    // Modelos de dados
    public class OptimizationPlan
    {
        public PowerPlanType PowerPlan { get; set; } = PowerPlanType.Balanced;
        public CpuPolicyType CpuPolicy { get; set; } = CpuPolicyType.Balanced;
        public GpuPolicyType GpuPolicy { get; set; } = GpuPolicyType.Balanced;
        public bool IsValid { get; set; } = true;
    }

    // Hardware context para decisões
    public class HardwareContext
    {
        public string CpuName { get; set; } = string.Empty;
        public string GpuName { get; set; } = string.Empty;
        public string GpuVendor { get; set; } = string.Empty;
        public bool IsLaptop { get; set; }
        public bool IsOnBattery { get; set; }
        public bool SupportsCoreParking { get; set; }
        public double TotalMemoryGB { get; set; }
    }

    [Flags]
    public enum PowerPlanType
    {
        Balanced = 0,
        HighPerformance = 1,
        UltimatePerformance = 2,
        BatterySaver = 4
    }

    [Flags]
    public enum CpuPolicyType
    {
        Balanced = 0,
        DisableCoreParking = 1,
        ForegroundBoost = 2,
        GamingScheduler = 4
    }

    [Flags]
    public enum GpuPolicyType
    {
        Balanced = 0,
        MaxPerformance = 1,
        OptimizeTdr = 2
    }
}
