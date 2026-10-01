using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Services.Gamer.Adaptive;
using VoltrisOptimizer.Services.Gamer.Intelligence;
using VoltrisOptimizer.Core.Configuration;
using VoltrisOptimizer.Core.Execution;
using VoltrisOptimizer.Core.Execution.Actions;

namespace VoltrisOptimizer.Services.Optimization
{
    public enum SmoothnessLevel
    {
        Idle = 0,
        Light = 1,
        Medium = 2,
        Aggressive = 3,
        Gaming = 4
    }

    public class RealTimeSmoothnessService : IDisposable
    {
        private readonly ILoggingService _logger;
        private readonly OptimizationConflictResolver _conflictResolver;
        private readonly ProfileOptimizationMap _profileMap;

        private const string MMCSS_PATH = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
        private const string AUDIO_TASK_PATH = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Pro Audio";
        private const string GAMES_TASK_PATH = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games";
        private const string CAPTURE_TASK_PATH = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Capture";

        [DllImport("ntdll.dll", EntryPoint = "NtSetTimerResolution")]
        private static extern int NtSetTimerResolution(uint DesiredResolution, bool SetResolution, out uint CurrentResolution);

        private SmoothnessLevel _currentLevel = SmoothnessLevel.Idle;
        private bool _isAlwaysOn;
        private CancellationTokenSource? _watchdogCts;
        private Task? _watchdogTask;
        private int _originalSystemResponsiveness = 20;
        private int _originalNetworkThrottling = 10;
        private bool _originalTimerCaptured;
        private readonly object _lock = new();

        public bool IsActive => _currentLevel > SmoothnessLevel.Idle;
        public SmoothnessLevel CurrentLevel => _currentLevel;
        public bool IsAlwaysOn => _isAlwaysOn;

        public RealTimeSmoothnessService(
            ILoggingService logger,
            OptimizationConflictResolver conflictResolver,
            ProfileOptimizationMap profileMap)
        {
            _logger = logger;
            _conflictResolver = conflictResolver;
            _profileMap = profileMap;
        }

        public void EnableAlwaysOn()
        {
            if (_isAlwaysOn) return;
            _isAlwaysOn = true;
            _logger.LogInfo("[RealTimeSmoothness] Always-On ativado. Monitorando sistema 24/7.");

            _watchdogCts = new CancellationTokenSource();
            _watchdogTask = WatchdogLoopAsync(_watchdogCts.Token);

            CaptureOriginalValues();
            ApplyBaselineSmoothness();
        }

        public void DisableAlwaysOn()
        {
            if (!_isAlwaysOn) return;
            _isAlwaysOn = false;
            _logger.LogInfo("[RealTimeSmoothness] Always-On desativado.");

            _watchdogCts?.Cancel();
            _watchdogTask = null;

            if (_currentLevel > SmoothnessLevel.Idle)
                RevertToLevel(SmoothnessLevel.Idle);

            _conflictResolver.ReleaseSource(OptimizationSource.RealTimeSmoothness);
        }

        public void SetSmoothnessLevel(SmoothnessLevel level)
        {
            if (level == _currentLevel) return;

            _logger.LogDebug($"[RealTimeSmoothness] Transição: {_currentLevel} -> {level}", "Smoothness");
            lock (_lock)
            {
                ApplyLevel(level);
                _currentLevel = level;
            }
        }

        public void ApplyGamingSmoothness(ProfileOptimizationMap.OptimizationPreset preset)
        {
            _logger.LogInfo("[RealTimeSmoothness] Aplicando perfil Gaming Smoothness...");

            if (VoltrisFeatureFlags.Instance.UseUnifiedScheduler)
            {
                _logger.LogInfo("[RealTimeSmoothness] Usando nova arquitetura baseada em Evidências (Strangler Fig).");
                var action = new RealTimeSmoothnessAction(
                    (int)preset.TimerResolution100ns,
                    preset.SystemResponsiveness);
                
                OptimizationScheduler.Instance.RequestAction(action);
                
                // Em modo Unified, não fazemos o controle de estado manual aqui para evitar concorrência.
                _currentLevel = SmoothnessLevel.Gaming;
                _logger.LogSuccess("[RealTimeSmoothness] Ação submetida ao Scheduler com sucesso.");
                return;
            }

            // --- CÓDIGO LEGADO (MANTIDO INTACTO PARA GARANTIA DE COMPATIBILIDADE) ---

            lock (_lock)
            {
                int sr = preset.SystemResponsiveness;
                var resolvedSr = _conflictResolver.Resolve(
                    OptimizationDomain.SystemResponsiveness, sr,
                    OptimizationSource.RealTimeSmoothness,
                    $"Gaming profile SR={sr}");

                if (resolvedSr is int finalSr)
                    ApplySystemResponsiveness(finalSr);

                var resolvedTimer = _conflictResolver.Resolve(
                    OptimizationDomain.TimerResolution, (int)preset.TimerResolution100ns,
                    OptimizationSource.RealTimeSmoothness,
                    $"Gaming timer={preset.TimerResolution100ns}");

                if (resolvedTimer is int finalTimer)
                    ApplyTimerResolution(finalTimer);

                var resolvedAudio = _conflictResolver.Resolve(
                    OptimizationDomain.AudioIsolation, preset.AudioOptimizationLevel,
                    OptimizationSource.RealTimeSmoothness,
                    $"Gaming audio={preset.AudioOptimizationLevel}");

                if (resolvedAudio is string finalAudio)
                    ApplyAudioIsolation(finalAudio);

                OptimizeMMCSS(
                    preset.EnableGamingMode,
                    preset.SystemResponsiveness,
                    preset.ForegroundQuantum);

                _currentLevel = SmoothnessLevel.Gaming;
            }

            _logger.LogSuccess("[RealTimeSmoothness] Gaming Smoothness aplicado via modo legado.");
        }

        public void RevertGamingSmoothness()
        {
            _logger.LogInfo("[RealTimeSmoothness] Revertendo Gaming Smoothness...");

            _conflictResolver.ReleaseSource(OptimizationSource.RealTimeSmoothness);

            lock (_lock)
            {
                if (_isAlwaysOn)
                {
                    ApplyBaselineSmoothness();
                    _currentLevel = SmoothnessLevel.Light;
                }
                else
                {
                    RevertToLevel(SmoothnessLevel.Idle);
                }
            }

            _logger.LogInfo("[RealTimeSmoothness] Smoothness revertido.");
        }

        private void CaptureOriginalValues()
        {
            if (_originalTimerCaptured) return;

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(MMCSS_PATH);
                if (key != null)
                {
                    _originalSystemResponsiveness = key.GetValue("SystemResponsiveness") as int? ?? 20;
                    var nti = key.GetValue("NetworkThrottlingIndex");
                    _originalNetworkThrottling = nti is int i ? i : 10;
                }
                _originalTimerCaptured = true;
                _logger.LogDebug($"[RealTimeSmoothness] Original: SR={_originalSystemResponsiveness}, NTI={_originalNetworkThrottling}", "Smoothness");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[RealTimeSmoothness] Falha ao capturar valores originais: {ex.Message}");
            }
        }

        private void ApplyBaselineSmoothness()
        {
            _logger.LogInfo("[RealTimeSmoothness] Aplicando baseline smoothness (24/7)...");

            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(MMCSS_PATH, true))
                {
                    if (key != null)
                    {
                        SetRegValue(key, "SystemResponsiveness", 15, RegistryValueKind.DWord);
                        SetRegValue(key, "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), RegistryValueKind.DWord);
                    }
                }

                ApplyTimerResolution(10000);

                _currentLevel = SmoothnessLevel.Light;
                _logger.LogSuccess("[RealTimeSmoothness] Baseline smoothness ativo (SR=15, Timer=1ms).");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[RealTimeSmoothness] Erro baseline: {ex.Message}");
            }
        }

        private void ApplyLevel(SmoothnessLevel level)
        {
            switch (level)
            {
                case SmoothnessLevel.Idle:
                    NtSetTimerResolution(0, false, out _);
                    using (var key = Registry.LocalMachine.OpenSubKey(MMCSS_PATH, true))
                    {
                        if (key != null)
                        {
                            SetRegValue(key, "SystemResponsiveness", _originalSystemResponsiveness, RegistryValueKind.DWord);
                            SetRegValue(key, "NetworkThrottlingIndex", _originalNetworkThrottling, RegistryValueKind.DWord);
                        }
                    }
                    break;

                case SmoothnessLevel.Light:
                    ApplyBaselineSmoothness();
                    break;

                case SmoothnessLevel.Medium:
                    using (var medKey = Registry.LocalMachine.OpenSubKey(MMCSS_PATH, true))
                    {
                        if (medKey != null)
                        {
                            SetRegValue(medKey, "SystemResponsiveness", 12, RegistryValueKind.DWord);
                            SetRegValue(medKey, "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), RegistryValueKind.DWord);
                        }
                    }
                    ApplyTimerResolution(8000);
                    break;

                case SmoothnessLevel.Aggressive:
                    using (var aggKey = Registry.LocalMachine.OpenSubKey(MMCSS_PATH, true))
                    {
                        if (aggKey != null)
                        {
                            SetRegValue(aggKey, "SystemResponsiveness", 8, RegistryValueKind.DWord);
                            SetRegValue(aggKey, "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), RegistryValueKind.DWord);
                        }
                    }
                    ApplyTimerResolution(5000);
                    OptimizeMMCSS(true, 8, 4);
                    ApplyAudioIsolation("High");
                    break;
            }
        }

        private void RevertToLevel(SmoothnessLevel level)
        {
            ApplyLevel(level);
            _currentLevel = level;
        }

        private void ApplySystemResponsiveness(int value)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(MMCSS_PATH, true);
                if (key != null)
                    SetRegValue(key, "SystemResponsiveness", value, RegistryValueKind.DWord);
                _logger.LogDebug($"[RealTimeSmoothness] SR aplicado: {value}", "Smoothness");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[RealTimeSmoothness] Falha SR: {ex.Message}");
            }
        }

        private void ApplyTimerResolution(int resolution100ns)
        {
            try
            {
                NtSetTimerResolution((uint)resolution100ns, true, out _);
                _logger.LogDebug($"[RealTimeSmoothness] Timer: {resolution100ns / 10000f:F2}ms", "Smoothness");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[RealTimeSmoothness] Falha timer: {ex.Message}");
            }
        }

        private void OptimizeMMCSS(bool gamingMode, int systemResponsiveness, int foregroundQuantum)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(MMCSS_PATH, true);
                if (key != null)
                {
                    SetRegValue(key, "SystemResponsiveness", systemResponsiveness, RegistryValueKind.DWord);
                    SetRegValue(key, "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), RegistryValueKind.DWord);
                    if (gamingMode)
                        SetRegValue(key, "ForegroundQuantum", (uint)foregroundQuantum, RegistryValueKind.DWord);
                }

                if (gamingMode)
                {
                    using var gamesKey = Registry.LocalMachine.OpenSubKey(GAMES_TASK_PATH, true);
                    if (gamesKey != null)
                    {
                        SetRegValue(gamesKey, "GPU Priority", 8, RegistryValueKind.DWord);
                        SetRegValue(gamesKey, "Priority", 6, RegistryValueKind.DWord);
                        SetRegValue(gamesKey, "Scheduling Category", "High", RegistryValueKind.String);
                        SetRegValue(gamesKey, "SFIO Priority", "High", RegistryValueKind.String);
                    }

                    using var audioKey = Registry.LocalMachine.OpenSubKey(AUDIO_TASK_PATH, true);
                    if (audioKey != null)
                    {
                        SetRegValue(audioKey, "GPU Priority", 8, RegistryValueKind.DWord);
                        SetRegValue(audioKey, "Priority", 6, RegistryValueKind.DWord);
                        SetRegValue(audioKey, "Scheduling Category", "High", RegistryValueKind.String);
                        SetRegValue(audioKey, "SFIO Priority", "High", RegistryValueKind.String);
                    }
                }

                _logger.LogDebug($"[RealTimeSmoothness] MMCSS: gaming={gamingMode}, SR={systemResponsiveness}, Q={foregroundQuantum}", "Smoothness");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[RealTimeSmoothness] Falha MMCSS: {ex.Message}");
            }
        }

        private void ApplyAudioIsolation(string level)
        {
            if (level == "Low") return;

            try
            {
                var audioProcesses = Process.GetProcessesByName("audiodg");
                foreach (var p in audioProcesses)
                {
                    try
                    {
                        p.PriorityClass = level == "High"
                            ? ProcessPriorityClass.High
                            : ProcessPriorityClass.AboveNormal;

                        if (level == "High" && Environment.ProcessorCount > 4)
                            p.ProcessorAffinity = (IntPtr)0x03;

                        _logger.LogDebug($"[RealTimeSmoothness] audiodg ({p.Id}): {level}", "Smoothness");
                    }
                    finally { p.Dispose(); }
                }
            }
            catch { }
        }

        private async Task WatchdogLoopAsync(CancellationToken ct)
        {
            _logger.LogInfo("[RealTimeSmoothness] Watchdog iniciado (ciclo 5s).");

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(5000, ct);

                    if (!_isAlwaysOn) break;

                    using var checkKey = Registry.LocalMachine.OpenSubKey(MMCSS_PATH);
                    if (checkKey != null)
                    {
                        var currentSr = checkKey.GetValue("SystemResponsiveness") as int?;
                        if (_currentLevel == SmoothnessLevel.Light && currentSr != 15)
                        {
                            _logger.LogWarning($"[RealTimeSmoothness] Watchdog: SR alterado para {currentSr}, restaurando...");
                            ApplyBaselineSmoothness();
                        }
                    }
                }
                catch (TaskCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[RealTimeSmoothness] Watchdog erro: {ex.Message}");
                }
            }

            _logger.LogInfo("[RealTimeSmoothness] Watchdog encerrado.");
        }

        private static void SetRegValue(RegistryKey key, string name, object value, RegistryValueKind kind)
        {
            try
            {
                var existing = key.GetValue(name);
                if (existing != null)
                {
                    try { var existingKind = key.GetValueKind(name); if (existingKind != kind) key.DeleteValue(name, false); }
                    catch { key.DeleteValue(name, false); }
                }
                key.SetValue(name, value, kind);
            }
            catch { }
        }

        public string GetDiagnosticsReport()
        {
            return $"[RealTimeSmoothness] Nível={_currentLevel}, AlwaysOn={_isAlwaysOn}, Watchdog={_watchdogTask?.Status}";
        }

        public void Dispose()
        {
            _watchdogCts?.Cancel();
            _watchdogCts?.Dispose();

            if (_currentLevel > SmoothnessLevel.Idle)
            {
                NtSetTimerResolution(0, false, out _);
            }
        }
    }
}
