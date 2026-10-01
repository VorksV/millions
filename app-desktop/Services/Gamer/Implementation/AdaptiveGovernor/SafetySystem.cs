using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services.Gamer.Models;

namespace VoltrisOptimizer.Services.Gamer.Implementation.AdaptiveGovernor
{
    /// <summary>
    /// Sistema de segurança (watchdog) — monitora estabilidade e reverte ações se necessário.
    /// Usa baseline deslizante (média móvel) para evitar falsos positivos por flutuações naturais.
    /// Só dispara rollback quando FPS baseline >= 20 — evita loading screens causarem throttle.
    /// </summary>
    internal class SafetySystem
    {
        private readonly ILoggingService _logger;
        private readonly object _lock = new object();

        private readonly Queue<AdaptiveSystemMetrics> _metricsHistory = new Queue<AdaptiveSystemMetrics>();
        private const int MaxHistorySize = 10;

        private readonly Queue<double> _fpsBaseline = new Queue<double>();
        private readonly Queue<double> _cpuBaseline = new Queue<double>();
        private readonly Queue<double> _gpuBaseline = new Queue<double>();
        private readonly Queue<double> _ramBaseline = new Queue<double>();
        private const int BaselineWindowSize = 8;

        private const double FpsDegradationThreshold  = 0.50;
        private const double TrendDegradationThreshold = 0.45;
        private const double CpuSpikeThreshold = 60.0;
        private const double GpuSpikeThreshold = 60.0;
        private const double RamSpikeThreshold = 30.0;

        private int _consecutiveFailures = 0;
        private const int MaxConsecutiveFailures = 30;
        private int _stableCount = 0;
        private const int MinStableSamples = 8;
        private double _initialBaselineFps = 0;
        private bool _baselineLocked = false;

        public SafetySystem(ILoggingService logger)
        {
            _logger.LogEntry(nameof(SafetySystem), ("logger", logger != null));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogExit(nameof(SafetySystem));
        }

        /// <summary>
        /// Verifica estabilidade. Só dispara rollback se baseline FPS >= 20
        /// (evita loading screens e menus causarem falsos positivos).
        /// </summary>
        public SafetyCheckResult CheckStability(AdaptiveSystemMetrics currentMetrics, AdaptiveSystemMetrics? baselineMetrics = null, bool actionWasApplied = false)
        {
            _logger.LogEntry(nameof(CheckStability), ("currentMetrics.Fps", currentMetrics?.Fps), ("actionWasApplied", actionWasApplied));
            
            var result = new SafetyCheckResult { IsStable = true, ShouldRollback = false, Reason = "Sistema estável" };

            if (currentMetrics == null)
            {
                _logger.LogDebug("[SafetySystem] CheckStability: currentMetrics = null → estável (sem dados)");
                _logger.LogExit(nameof(CheckStability), result.IsStable);
                return result;
            }

            lock (_lock)
            {
                _metricsHistory.Enqueue(currentMetrics);
                if (_metricsHistory.Count > MaxHistorySize) _metricsHistory.Dequeue();
                if (_metricsHistory.Count < 2)
                {
                    _logger.LogTrace($"[SafetySystem] Histórico insuficiente ({_metricsHistory.Count} amostras) → estável");
                    _logger.LogExit(nameof(CheckStability), result.IsStable);
                    return result;
                }

                if (currentMetrics.Fps > 5)          { _fpsBaseline.Enqueue(currentMetrics.Fps);              if (_fpsBaseline.Count > BaselineWindowSize) _fpsBaseline.Dequeue(); }
                if (currentMetrics.CpuUsagePercent > 0) { _cpuBaseline.Enqueue(currentMetrics.CpuUsagePercent); if (_cpuBaseline.Count > BaselineWindowSize) _cpuBaseline.Dequeue(); }
                if (currentMetrics.GpuUsagePercent > 0) { _gpuBaseline.Enqueue(currentMetrics.GpuUsagePercent); if (_gpuBaseline.Count > BaselineWindowSize) _gpuBaseline.Dequeue(); }
                if (currentMetrics.RamUsagePercent > 0) { _ramBaseline.Enqueue(currentMetrics.RamUsagePercent); if (_ramBaseline.Count > BaselineWindowSize) _ramBaseline.Dequeue(); }
            }

            double refFps = 0, refCpu = 0, refGpu = 0, refRam = 0;
            lock (_lock)
            {
                if (_fpsBaseline.Count >= 3) refFps = _fpsBaseline.Take(_fpsBaseline.Count - 1).Average();
                if (_cpuBaseline.Count >= 3) refCpu = _cpuBaseline.Take(_cpuBaseline.Count - 1).Average();
                if (_gpuBaseline.Count >= 3) refGpu = _gpuBaseline.Take(_gpuBaseline.Count - 1).Average();
                if (_ramBaseline.Count >= 3) refRam = _ramBaseline.Take(_ramBaseline.Count - 1).Average();
            }

            if (refFps <= 0) refFps = baselineMetrics?.Fps ?? 0;
            if (refCpu <= 0) refCpu = baselineMetrics?.CpuUsagePercent ?? 0;
            if (refGpu <= 0) refGpu = baselineMetrics?.GpuUsagePercent ?? 0;
            if (refRam <= 0) refRam = baselineMetrics?.RamUsagePercent ?? 0;

            _logger.LogTrace($"[SafetySystem] Baseline: FPS={refFps:F1}, CPU={refCpu:F1}%, GPU={refGpu:F1}%, RAM={refRam:F1}% | Atual: FPS={currentMetrics.Fps:F1}, CPU={currentMetrics.CpuUsagePercent:F1}%, GPU={currentMetrics.GpuUsagePercent:F1}%, RAM={currentMetrics.RamUsagePercent:F1}%");

            // Travar baseline inicial após primeiras amostras estáveis
            if (!_baselineLocked && _stableCount >= 3 && refFps > 0)
            {
                _initialBaselineFps = refFps;
                _baselineLocked = true;
                _logger.LogInfo($"[SafetySystem] 📌 Baseline inicial travado: {refFps:F1} FPS");
            }

            if (refFps < 15.0)
            {
                _logger.LogDebug($"[SafetySystem] Baseline FPS ({refFps:F1}) < 15 — pulando verificação (loading screen/menu)");
                _logger.LogExit(nameof(CheckStability), result.IsStable);
                return result;
            }

            if (_stableCount < MinStableSamples)
            {
                _stableCount++;
                _logger.LogDebug($"[SafetySystem] Coletando amostras ({_stableCount}/{MinStableSamples}) — skipping check");
                _logger.LogExit(nameof(CheckStability), result.IsStable);
                return result;
            }

            // Usar baseline inicial travado se disponível (evita degradação em cascata)
            if (_baselineLocked && _initialBaselineFps > 0)
            {
                refFps = _initialBaselineFps;
                _logger.LogTrace($"[SafetySystem] Usando baseline travado: {refFps:F1} FPS");
            }

            if (refFps > 0 && currentMetrics.Fps > 0)
            {
                var fpsDeg = (refFps - currentMetrics.Fps) / refFps;
                if (fpsDeg > FpsDegradationThreshold)
                {
                    _consecutiveFailures++;
                    result.IsStable = false; result.ShouldRollback = true;
                    result.Reason = $"FPS degradou {fpsDeg * 100:F1}% (ref={refFps:F1} → atual={currentMetrics.Fps:F1})";
                    _logger.LogWarning($"[SafetySystem] ⚠️ {result.Reason} | consecutivas={_consecutiveFailures}");

                    if (_consecutiveFailures >= MaxConsecutiveFailures)
                    {
                        _logger.LogCritical($"[SafetySystem] 🛑 {_consecutiveFailures} falhas consecutivas — desativando AdaptiveGovernor");
                    }

                    _logger.LogExit(nameof(CheckStability), result.IsStable);
                    return result;
                }
            }

            if (actionWasApplied)
            {
                if (refCpu > 0 && currentMetrics.CpuUsagePercent > refCpu + CpuSpikeThreshold)
                {
                    _consecutiveFailures++;
                    result.IsStable = false; result.ShouldRollback = true;
                    result.Reason = $"CPU aumentou {currentMetrics.CpuUsagePercent - refCpu:F1}% após ação (ref={refCpu:F1}%, atual={currentMetrics.CpuUsagePercent:F1}%)";
                    _logger.LogWarning($"[SafetySystem] ⚠️ {result.Reason} | consecutivas={_consecutiveFailures}");
                    _logger.LogExit(nameof(CheckStability), result.IsStable);
                    return result;
                }
                if (refGpu > 0 && currentMetrics.GpuUsagePercent > refGpu + GpuSpikeThreshold)
                {
                    _consecutiveFailures++;
                    result.IsStable = false; result.ShouldRollback = true;
                    result.Reason = $"GPU aumentou {currentMetrics.GpuUsagePercent - refGpu:F1}% após ação (ref={refGpu:F1}%, atual={currentMetrics.GpuUsagePercent:F1}%)";
                    _logger.LogWarning($"[SafetySystem] ⚠️ {result.Reason} | consecutivas={_consecutiveFailures}");
                    _logger.LogExit(nameof(CheckStability), result.IsStable);
                    return result;
                }
                if (refRam > 0 && currentMetrics.RamUsagePercent > refRam + RamSpikeThreshold)
                {
                    _consecutiveFailures++;
                    result.IsStable = false; result.ShouldRollback = true;
                    result.Reason = $"RAM aumentou {currentMetrics.RamUsagePercent - refRam:F1}% após ação (ref={refRam:F1}%, atual={currentMetrics.RamUsagePercent:F1}%)";
                    _logger.LogWarning($"[SafetySystem] ⚠️ {result.Reason} | consecutivas={_consecutiveFailures}");
                    _logger.LogExit(nameof(CheckStability), result.IsStable);
                    return result;
                }
            }
            else
            {
                _logger.LogTrace("[SafetySystem] Ação não aplicada — pulando verificação de spikes");
            }

            // Tendência de queda consistente (4 amostras todas >= 20 FPS e em queda)
            lock (_lock)
            {
                if (_metricsHistory.Count >= 4)
                {
                    try
                    {
                        var recent = _metricsHistory.TakeLast(4).ToList();
                        if (recent.All(m => m != null && m.Fps >= 20.0))
                        {
                            bool allDecreasing = recent[0].Fps > recent[1].Fps &&
                                                 recent[1].Fps > recent[2].Fps &&
                                                 recent[2].Fps > recent[3].Fps;
                            if (allDecreasing)
                            {
                                var deg = (recent[0].Fps - recent[3].Fps) / recent[0].Fps;
                                if (deg > TrendDegradationThreshold)
                                {
                                    result.IsStable = false; result.ShouldRollback = true;
                                    result.Reason = $"FPS caindo consistentemente ({recent[0].Fps:F1} → {recent[3].Fps:F1}, -{deg * 100:F1}%)";
                                    _logger.LogWarning($"[SafetySystem] ⚠️ {result.Reason}");
                                    _logger.LogExit(nameof(CheckStability), result.IsStable);
                                    return result;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"[SafetySystem] Erro ao verificar histórico: {ex.Message}");
                    }
                }
            }

            if (result.IsStable && !result.ShouldRollback)
            {
                if (_consecutiveFailures > 0)
                {
                    _consecutiveFailures = 0;
                    _logger.LogDebug("[SafetySystem] Sistema estabilizado — resetando contador de falhas");
                }
                _logger.LogTrace($"[SafetySystem] ✅ Estável | FPS Δ={(refFps - currentMetrics.Fps) / refFps * 100:F1}% | CPU Δ={currentMetrics.CpuUsagePercent - refCpu:F1}% | GPU Δ={currentMetrics.GpuUsagePercent - refGpu:F1}%");
            }

            _logger.LogExit(nameof(CheckStability), result.IsStable);
            return result;
        }

        public void RegisterFailure()
        {
            _logger.LogEntry(nameof(RegisterFailure), ("_consecutiveFailures", _consecutiveFailures));
            
            _consecutiveFailures++;
            if (_consecutiveFailures >= MaxConsecutiveFailures)
                _logger.LogError($"[SafetySystem] ⚠️ {_consecutiveFailures} falhas consecutivas - Governor deve ser desativado");
            
            _logger.LogExit(nameof(RegisterFailure));
        }

        public void ResetFailureCount()
        {
            _logger.LogEntry(nameof(ResetFailureCount));
            _consecutiveFailures = 0;
            _logger.LogExit(nameof(ResetFailureCount));
        }

        public bool ShouldDisableGovernor()
        {
            var shouldDisable = _consecutiveFailures >= MaxConsecutiveFailures;
            _logger.LogEntry(nameof(ShouldDisableGovernor), ("_consecutiveFailures", _consecutiveFailures), ("shouldDisable", shouldDisable));
            _logger.LogExit(nameof(ShouldDisableGovernor), shouldDisable);
            return shouldDisable;
        }

        public void ClearHistory()
        {
            _logger.LogEntry(nameof(ClearHistory));
            
            lock (_lock)
            {
                _metricsHistory.Clear();
                _fpsBaseline.Clear();
                _cpuBaseline.Clear();
                _gpuBaseline.Clear();
                _ramBaseline.Clear();
                _consecutiveFailures = 0;
                _stableCount = 0;
                _baselineLocked = false;
                _initialBaselineFps = 0;
            }
            _logger.LogInfo("[SafetySystem] Histórico limpo — baseline será recalculado");
            _logger.LogExit(nameof(ClearHistory));
        }

        public void ResetBaseline()
        {
            _logger.LogEntry(nameof(ResetBaseline));
            
            lock (_lock)
            {
                _baselineLocked = false;
                _initialBaselineFps = 0;
                _stableCount = 0;
            }
            _logger.LogInfo("[SafetySystem] Baseline resetado — novo baseline será calculado");
            _logger.LogExit(nameof(ResetBaseline));
        }
    }

    internal class SafetyCheckResult
    {
        public bool IsStable { get; set; }
        public bool ShouldRollback { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}
