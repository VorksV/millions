using System.Runtime.InteropServices;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Core.Constants;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.Gamer.Interfaces;
using VoltrisOptimizer.Services.Performance;
using VoltrisOptimizer.Utils;
using GamerModels = VoltrisOptimizer.Services.Gamer.Models;
using VoltrisOptimizer.Services.Performance.CpuTuning;
using VoltrisOptimizer.Services.Performance.CpuTuning.Models;

namespace VoltrisOptimizer.Services.Gamer.Implementation
{
    [Obsolete("PLACEBO: Este serviço não executa otimizações reais. Use GamerModeManager para otimizações reais de CPU.")]
    public class CpuGamingOptimizerService : ICpuGamingOptimizer
    {
        private readonly ILoggingService _logger;
        private readonly IRegistryService? _registry;
        private readonly IProcessRunner? _processRunner;
        private readonly VoltrisOptimizer.Services.Gamer.GamerModeManager.IPowerPlanService? _powerPlanService;
        private readonly IHardwareDetector? _hardwareDetector;
        private readonly VoltrisOptimizer.Services.Performance.CpuTuning.HardwareCapabilityDetector _hardwareCapabilityDetector;
        private readonly RegistryValidator _registryValidator;

        private int? _originalPrioritySeparation;
        private bool _originalCoreParkingDisabled;
        private int? _originalDisablePagingExecutive;
        private string? _originalPowerPlanGuid;

        public CpuGamingOptimizerService(
            ILoggingService logger,
            IRegistryService? registry = null,
            IProcessRunner? processRunner = null,
            VoltrisOptimizer.Services.Gamer.GamerModeManager.IPowerPlanService? powerPlanService = null,
            IHardwareDetector? hardwareDetector = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogEntry(nameof(CpuGamingOptimizerService));
            _registry = registry;
            _processRunner = processRunner;
            _powerPlanService = powerPlanService;
            _hardwareDetector = hardwareDetector;

            if (App.Services != null)
                _hardwareCapabilityDetector = (VoltrisOptimizer.Services.Performance.CpuTuning.HardwareCapabilityDetector)App.Services.GetService(typeof(IHardwareCapabilityDetector)) ?? new VoltrisOptimizer.Services.Performance.CpuTuning.HardwareCapabilityDetector(logger, (VoltrisOptimizer.Services.Performance.CpuTuning.Core.Interfaces.IHardwareBackend)App.Services.GetService(typeof(VoltrisOptimizer.Services.Performance.CpuTuning.Core.Interfaces.IHardwareBackend))!);
            else
                _hardwareCapabilityDetector = new VoltrisOptimizer.Services.Performance.CpuTuning.HardwareCapabilityDetector(logger, new VoltrisOptimizer.Services.Performance.CpuTuning.Core.Backends.SafeFallbackBackend(logger));

            _registryValidator = new RegistryValidator(logger);

            if (_hardwareDetector == null && App.Services != null)
            {
                _hardwareDetector = App.Services.GetService(typeof(IHardwareDetector)) as IHardwareDetector;
            }

            if (_powerPlanService == null && App.Services != null)
            {
                _powerPlanService = App.Services.GetService(typeof(VoltrisOptimizer.Services.Gamer.GamerModeManager.IPowerPlanService))
                    as VoltrisOptimizer.Services.Gamer.GamerModeManager.IPowerPlanService;
            }

            var caps = _hardwareCapabilityDetector.Capabilities;
            _logger.LogInfo($"[CpuOptimizer] Hardware: {caps.ProcessorName}");
            _logger.LogInfo($"[CpuOptimizer] Cores: {caps.PhysicalCores}, Threads: {caps.LogicalProcessors}");
            _logger.LogInfo($"[CpuOptimizer] Suporte - CoreParking: {caps.SupportsCoreParking}, TurboBoost: {caps.SupportsTurboBoost}, Hetero: {caps.SupportsHeteroPolicy}");
            _logger.LogExit(nameof(CpuGamingOptimizerService));
        }

        public async Task<bool> OptimizeAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogEntry(nameof(OptimizeAsync));
            return await Task.Run(async () =>
            {
                try
                {
                    _logger.LogInfo("[CpuOptimizer] Aplicando otimizações de CPU...");

                    SetForegroundPriority();

                    OptimizeScheduler();

                    _logger.LogSuccess("[CpuOptimizer] CPU otimizada para jogos");
                    _logger.LogExit(nameof(OptimizeAsync));
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError("[CpuOptimizer] Erro ao otimizar CPU", ex);
                    _logger.LogExit(nameof(OptimizeAsync));
                    return false;
                }
            }, cancellationToken);
        }

        public async Task<bool> RestoreAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogEntry(nameof(RestoreAsync));
            return await Task.Run(async () =>
            {
                try
                {
                    _logger.LogInfo("[CpuOptimizer] Restaurando configurações de CPU...");

                    _logger.LogSuccess("[CpuOptimizer] Configurações de CPU restauradas");
                    _logger.LogExit(nameof(RestoreAsync));
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError("[CpuOptimizer] Erro ao restaurar CPU", ex);
                    _logger.LogExit(nameof(RestoreAsync));
                    return false;
                }
            }, cancellationToken);
        }

        private void SetForegroundPriority()
        {
            _logger.LogEntry(nameof(SetForegroundPriority));
            _logger.LogInfo("[CpuOptimizer] SetForegroundPriority ignorado (delegado para o VoltrisBody / OBrain)");
            _logger.LogExit(nameof(SetForegroundPriority));
        }

        private void OptimizeScheduler()
        {
            _logger.LogEntry(nameof(OptimizeScheduler));
            _logger.LogInfo("[CpuOptimizer] OptimizeScheduler ignorado (delegado para o VoltrisBody / OBrain)");
            _logger.LogExit(nameof(OptimizeScheduler));
        }

        public bool SetGameProcessPriority(int processId, GamerModels.ProcessPriorityLevel priority)
        {
            _logger.LogEntry(nameof(SetGameProcessPriority));
            try
            {
                using var process = Process.GetProcessById(processId);

                process.PriorityClass = priority switch
                {
                    GamerModels.ProcessPriorityLevel.Normal => ProcessPriorityClass.Normal,
                    GamerModels.ProcessPriorityLevel.AboveNormal => ProcessPriorityClass.AboveNormal,
                    GamerModels.ProcessPriorityLevel.High => ProcessPriorityClass.High,
                    GamerModels.ProcessPriorityLevel.RealTime => ProcessPriorityClass.RealTime,
                    _ => ProcessPriorityClass.High
                };

                _logger.LogInfo($"[CpuOptimizer] Prioridade {priority} definida para processo {processId}");
                _logger.LogExit(nameof(SetGameProcessPriority));
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[CpuOptimizer] Erro ao definir prioridade: {ex.Message}");
                _logger.LogExit(nameof(SetGameProcessPriority));
                return false;
            }
        }

        public bool ApplyCpuSets(int processId)
        {
            _logger.LogEntry(nameof(ApplyCpuSets));
            try
            {
                if (_hardwareDetector != null && _hardwareDetector.IsHybridCpu())
                {
                    var counts = _hardwareDetector.GetCpuCounts();
                    var pCores = counts.Threads - counts.Cores;

                    if (pCores > 0)
                    {
                        int pCoreThreads = pCores * 2;

                        long affinityMask = (1L << pCoreThreads) - 1;

                        using var process = Process.GetProcessById(processId);
                        process.ProcessorAffinity = (IntPtr)affinityMask;

                        _logger.LogSuccess($"[CpuOptimizer] 🧠 Afinidade Híbrida: Jogo isolado nos {pCores} P-Cores ({pCoreThreads} threads).");
                        _logger.LogExit(nameof(ApplyCpuSets));
                        return true;
                    }
                }

                _logger.LogInfo($"[CpuOptimizer] CPU não híbrida ou lógica não aplicável. Afinidade padrão mantida.");
                _logger.LogExit(nameof(ApplyCpuSets));
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[CpuOptimizer] Erro em ApplyCpuSets: {ex.Message}");
                _logger.LogExit(nameof(ApplyCpuSets));
                return false;
            }
        }

        public Task<bool> DisableCoreParkingAsync()
        {
            _logger.LogEntry(nameof(DisableCoreParkingAsync));
            _logger.LogInfo("[CpuOptimizer] Core Parking agora é gerenciado pela OBrainOrchestrator.");
            _logger.LogExit(nameof(DisableCoreParkingAsync));
            return Task.FromResult(true);
        }
    }
}
