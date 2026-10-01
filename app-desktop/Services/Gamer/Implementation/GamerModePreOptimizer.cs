using System;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.Display;
using VoltrisOptimizer.Services.Gamer.Interfaces;
using VoltrisOptimizer.Services.Gamer.Models;
using VoltrisOptimizer.Services.Optimization.Unification;
using VoltrisOptimizer.Services.Performance;
using VoltrisOptimizer.Services.Performance.Models;

namespace VoltrisOptimizer.Services.Gamer.Implementation
{
    public class GamerModePreOptimizer
    {
        private readonly ILoggingService _logger;
        private readonly ICpuGamingOptimizer _cpuOptimizer;
        private readonly IGpuGamingOptimizer _gpuOptimizer;
        private readonly INetworkGamingOptimizer _networkOptimizer;
        private readonly IMemoryGamingOptimizer _memoryOptimizer;
        private readonly ITimerResolutionService? _timerService;
        private readonly DisplayService _displayService;
        private readonly ThermalSafetyGuard? _thermalGuard;
        private TimerResolutionManager? _timerManager;

        private bool _isSystemPrepared = false;
        private bool _timerRequested = false;

        private bool _cpuWasApplied = false;
        private bool _gpuWasApplied = false;
        private bool _netWasApplied = false;
        private bool _vsyncWasApplied = false;
        private bool _gammaWasApplied = false;
        private double _originalGamma = 1.0;
        private bool _originalVSync = true;

        public GamerModePreOptimizer(
            ILoggingService logger,
            ICpuGamingOptimizer cpuOptimizer,
            IGpuGamingOptimizer gpuOptimizer,
            INetworkGamingOptimizer networkOptimizer,
            IMemoryGamingOptimizer memoryOptimizer,
            DisplayService displayService,
            ITimerResolutionService? timerService = null,
            ThermalSafetyGuard? thermalGuard = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogEntry(nameof(GamerModePreOptimizer));
            _cpuOptimizer = cpuOptimizer ?? throw new ArgumentNullException(nameof(cpuOptimizer));
            _gpuOptimizer = gpuOptimizer ?? throw new ArgumentNullException(nameof(gpuOptimizer));
            _networkOptimizer = networkOptimizer ?? throw new ArgumentNullException(nameof(networkOptimizer));
            _memoryOptimizer = memoryOptimizer ?? throw new ArgumentNullException(nameof(memoryOptimizer));
            _displayService = displayService ?? throw new ArgumentNullException(nameof(displayService));
            _timerService = timerService;
            _thermalGuard = thermalGuard;
            if (timerService != null)
                _timerManager = new TimerResolutionManager(logger, timerService);
            _logger.LogExit(nameof(GamerModePreOptimizer));
        }

        public void Reset()
        {
            _logger.LogEntry(nameof(Reset));
            _isSystemPrepared = false;
            _cpuWasApplied = false;
            _gpuWasApplied = false;
            _netWasApplied = false;
            _vsyncWasApplied = false;
            _gammaWasApplied = false;
            _timerRequested = false;
            _logger.LogInfo("[PreOptimizer] Estado resetado para nova sessão.");
            _logger.LogExit(nameof(Reset));
        }

        public async Task<bool> PrepareSystemForGamingAsync(
            GamerOptimizationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _logger.LogEntry(nameof(PrepareSystemForGamingAsync));
            Reset();

            if (_isSystemPrepared)
            {
                _logger.LogWarning("[PreOptimizer] Sistema já preparado — ignorando chamada duplicada.");
                _logger.LogExit(nameof(PrepareSystemForGamingAsync));
                return true;
            }

            bool doCpu = options?.OptimizeCpu ?? true;
            bool doGpu = options?.OptimizeGpu ?? true;
            bool doNet = options?.OptimizeNetwork ?? true;
            bool doMem = options?.OptimizeMemory ?? true;
            bool doTimer = options?.ReduceLatency ?? true;
            bool doVSync = options?.DisableDwmVSync ?? true;
            bool doGamma = options?.OptimizeGamma ?? true;

            _logger.LogInfo("═══════════════════════════════════════════");
            _logger.LogInfo("🚀 [PRE-OPTIMIZER] PREPARANDO SISTEMA PARA GAMING");
            _logger.LogInfo($"[PreOptimizer] Opções: CPU={doCpu} GPU={doGpu} Net={doNet} Mem={doMem} Timer={doTimer}");
            _logger.LogInfo("═══════════════════════════════════════════");

            var startTime = DateTime.Now;

            if (_thermalGuard != null && doCpu)
            {
                bool isLaptop = DetectIsLaptop();
                var metrics = _thermalGuard.GetCurrentMetrics();
                double currentTemp = metrics?.CpuTemperature ?? 0;
                if (isLaptop && currentTemp >= 70.0)
                {
                    _logger.LogWarning($"[PreOptimizer] ⚠️ Laptop detectado com {currentTemp:F1}°C — reduzindo agressividade CPU para evitar thermal throttling");
                    doCpu = false;
                    doGpu = false;
                }
                else if (currentTemp >= 80.0)
                {
                    _logger.LogWarning($"[PreOptimizer] ⚠️ Temperatura elevada ({currentTemp:F1}°C) — aplicando perfil balanceado");
                    doTimer = false;
                }
            }
            int step = 0;
            int totalSteps = (doCpu ? 1 : 0) + (doGpu ? 1 : 0) + (doNet ? 1 : 0) + (doMem ? 1 : 0) + (doTimer ? 1 : 0);
            int applied = 0;

            bool cpuApplied = false;
            bool gpuApplied = false;
            bool netApplied = false;

            if (doCpu)
            {
                step++;
                _logger.LogInfo($"[PreOptimizer] [{step}/{totalSteps}] Otimizando CPU (Power Plan, Core Parking, Scheduler)...");
                try
                {
                    await _cpuOptimizer.OptimizeAsync(cancellationToken);
                    cpuApplied = true;
                    applied++;
                    _logger.LogSuccess($"[PreOptimizer] [{step}/{totalSteps}] ✅ CPU otimizada.");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[PreOptimizer] [{step}/{totalSteps}] ❌ Falha ao otimizar CPU: {ex.Message}", ex);
                }
            }

            if (doGpu)
            {
                step++;
                _logger.LogInfo($"[PreOptimizer] [{step}/{totalSteps}] Otimizando GPU (Performance Mode)...");
                try
                {
                    await _gpuOptimizer.OptimizeAsync(cancellationToken);
                    gpuApplied = true;
                    applied++;
                    _logger.LogSuccess($"[PreOptimizer] [{step}/{totalSteps}] ✅ GPU otimizada.");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[PreOptimizer] [{step}/{totalSteps}] ❌ Falha ao otimizar GPU: {ex.Message}", ex);
                }
            }

            if (doNet)
            {
                step++;
                _logger.LogInfo($"[PreOptimizer] [{step}/{totalSteps}] Otimizando Rede (TCP Stack, Latency)...");
                try
                {
                    await _networkOptimizer.OptimizeAsync(cancellationToken);
                    netApplied = true;
                    applied++;
                    _logger.LogSuccess($"[PreOptimizer] [{step}/{totalSteps}] ✅ Rede otimizada.");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[PreOptimizer] [{step}/{totalSteps}] ❌ Falha ao otimizar Rede: {ex.Message}", ex);
                }
            }

            if (doMem)
            {
                step++;
                _logger.LogInfo($"[PreOptimizer] [{step}/{totalSteps}] Otimizando Memória (Standby List)...");
                try
                {
                    _memoryOptimizer.CleanStandbyList();
                    applied++;
                    _logger.LogSuccess($"[PreOptimizer] [{step}/{totalSteps}] ✅ Memória otimizada.");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[PreOptimizer] [{step}/{totalSteps}] ❌ Falha ao otimizar Memória: {ex.Message}", ex);
                }
            }

            if (doTimer && _timerManager != null)
            {
                step++;
                _logger.LogInfo($"[PreOptimizer] [{step}/{totalSteps}] Otimizando Timer Resolution...");
                try
                {
                    _timerManager.RequestHighPrecision("GamerModePreOptimizer");
                    _timerRequested = true;
                    applied++;
                    _logger.LogSuccess($"[PreOptimizer] [{step}/{totalSteps}] ✅ Timer Resolution configurado.");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[PreOptimizer] [{step}/{totalSteps}] ❌ Falha ao configurar Timer: {ex.Message}", ex);
                }
            }

            if (doVSync)
            {
                _logger.LogInfo("[PreOptimizer] Desativando DWM VSync (Latência reduzida)...");
                _originalVSync = SettingsService.Instance.Settings.DisplayVSyncEnabled;
                await _displayService.SetDwmVSyncAsync(false);
                _vsyncWasApplied = true;
                applied++;
            }

            if (doGamma)
            {
                _logger.LogInfo("[PreOptimizer] Skipping Gamma optimization to preserve default brightness.");
            }

            _isSystemPrepared = applied > 0;
            _cpuWasApplied = cpuApplied;
            _gpuWasApplied = gpuApplied;
            _netWasApplied = netApplied;

            var duration = DateTime.Now - startTime;
            _logger.LogSuccess($"[PreOptimizer] ✅ Preparação concluída em {duration.TotalMilliseconds:F0}ms — {applied}/{totalSteps} otimizações aplicadas.");
            _logger.LogInfo($"[PreOptimizer] Flags: CPU={cpuApplied} GPU={gpuApplied} Net={netApplied} Mem=N/A Timer={_timerRequested}");

            if (applied == 0)
            {
                _logger.LogWarning("[PreOptimizer] ⚠️ Nenhuma otimização foi aplicada com sucesso.");
                _logger.LogExit(nameof(PrepareSystemForGamingAsync));
                return false;
            }

            _logger.LogExit(nameof(PrepareSystemForGamingAsync));
            return true;
        }

        public async Task<bool> RestoreSystemAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogEntry(nameof(RestoreSystemAsync));
            if (!_isSystemPrepared)
            {
                _logger.LogInfo("[PreOptimizer] RestoreSystemAsync chamado mas _isSystemPrepared=false — nada a restaurar.");
                _logger.LogExit(nameof(RestoreSystemAsync));
                return true;
            }

            _logger.LogInfo("═══════════════════════════════════════════");
            _logger.LogInfo("🔄 [PRE-OPTIMIZER] RESTAURANDO SISTEMA");
            _logger.LogInfo($"[PreOptimizer] Flags a restaurar: CPU={_cpuWasApplied} GPU={_gpuWasApplied} Net={_netWasApplied} Timer={_timerRequested}");
            _logger.LogInfo("═══════════════════════════════════════════");

            var startTime = DateTime.Now;
            int restored = 0;
            bool allOk = true;

            try
            {
                if (_timerRequested && _timerManager != null)
                {
                    _logger.LogInfo("[PreOptimizer] Restaurando Timer Resolution...");
                    try
                    {
                        _timerManager.ReleaseHighPrecision("GamerModePreOptimizer.Restore");
                        _timerRequested = false;
                        restored++;
                        _logger.LogSuccess("[PreOptimizer] ✅ Timer Resolution restaurado.");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"[PreOptimizer] ❌ Falha ao restaurar Timer: {ex.Message}", ex);
                        allOk = false;
                    }
                }

                if (_gammaWasApplied)
                {
                    _logger.LogInfo($"[PreOptimizer] Restaurando Gamma para: {_originalGamma:F1}");
                    await _displayService.SetGammaAsync(_originalGamma);
                    _gammaWasApplied = false;
                }

                if (_vsyncWasApplied)
                {
                    _logger.LogInfo($"[PreOptimizer] Restaurando DWM VSync para: {_originalVSync}");
                    await _displayService.SetDwmVSyncAsync(_originalVSync);
                    _vsyncWasApplied = false;
                }

                if (_netWasApplied)
                {
                    _logger.LogInfo("[PreOptimizer] Restaurando Rede...");
                    try
                    {
                        await _networkOptimizer.RestoreAsync(CancellationToken.None);
                        _netWasApplied = false;
                        restored++;
                        _logger.LogSuccess("[PreOptimizer] ✅ Rede restaurada.");
                    }
                    catch (Exception ex)
                    {
                        if (ex is OperationCanceledException)
                            _logger.LogWarning("[PreOptimizer] ⚠️ Restauração de Rede foi interrompida (cancelamento de sistema).");
                        else
                            _logger.LogError($"[PreOptimizer] ❌ Falha ao restaurar Rede: {ex.Message}", ex);

                        allOk = false;
                    }
                }

                if (_gpuWasApplied)
                {
                    _logger.LogInfo("[PreOptimizer] Restaurando GPU...");
                    try
                    {
                        await _gpuOptimizer.RestoreAsync(CancellationToken.None);
                        _gpuWasApplied = false;
                        restored++;
                        _logger.LogSuccess("[PreOptimizer] ✅ GPU restaurada.");
                    }
                    catch (Exception ex)
                    {
                        if (ex is OperationCanceledException)
                            _logger.LogWarning("[PreOptimizer] ⚠️ Restauração de GPU foi interrompida.");
                        else
                            _logger.LogError($"[PreOptimizer] ❌ Falha ao restaurar GPU: {ex.Message}", ex);

                        allOk = false;
                    }
                }

                if (_cpuWasApplied)
                {
                    _logger.LogInfo("[PreOptimizer] Restaurando CPU...");
                    try
                    {
                        await _cpuOptimizer.RestoreAsync(CancellationToken.None);
                        _cpuWasApplied = false;
                        restored++;
                        _logger.LogSuccess("[PreOptimizer] ✅ CPU restaurada.");
                    }
                    catch (Exception ex)
                    {
                        if (ex is OperationCanceledException)
                            _logger.LogWarning("[PreOptimizer] ⚠️ Restauração de CPU foi interrompida.");
                        else
                            _logger.LogError($"[PreOptimizer] ❌ Falha ao restaurar CPU: {ex.Message}", ex);

                        allOk = false;
                    }
                }

                _isSystemPrepared = false;
                var duration = DateTime.Now - startTime;
                _logger.LogSuccess($"[PreOptimizer] ✅ Restauração concluída em {duration.TotalMilliseconds:F0}ms — {restored} componentes restaurados. AllOk={allOk}");
                _logger.LogInfo("═══════════════════════════════════════════");

                _logger.LogExit(nameof(RestoreSystemAsync));
                return allOk;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[PreOptimizer] ❌ Erro crítico ao restaurar sistema: {ex.Message}", ex);
                _isSystemPrepared = false;
                _logger.LogExit(nameof(RestoreSystemAsync));
                return false;
            }
        }

        public bool IsSystemPrepared => _isSystemPrepared;

        private static bool DetectIsLaptop()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Battery");
                foreach (ManagementObject obj in searcher.Get())
                {
                    using var __dispose_obj = obj;
                    if (obj["Availability"] != null)
                        return true;
                }
            }
            catch { }
            return false;
        }
    }
}
