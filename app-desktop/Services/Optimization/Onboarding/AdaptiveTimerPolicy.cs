using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using VoltrisOptimizer.Core.Body;
using VoltrisOptimizer.Models;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Benchmark;

namespace VoltrisOptimizer.Services.Optimization.Onboarding
{
    public sealed class AdaptiveTimerPolicy
    {
        private readonly ILoggingService _logger;
        private readonly ISystemArm _systemArm;

        public double CurrentTimerMs { get; private set; } = 15.6;
        public double TargetTimerMs { get; private set; } = 15.6;
        public bool Applied { get; private set; }
        public string Reason { get; private set; } = string.Empty;
        public bool IsAlreadyOptimized { get; private set; }

        public AdaptiveTimerPolicy(ILoggingService logger, ISystemArm systemArm)
        {
            _logger = logger;
            _systemArm = systemArm;
        }

        public async Task<AdaptiveTimerPolicy> EvaluateAsync(HardwareProfile hardware, ThermalState thermal, PowerSource power)
        {
            _logger?.LogInfo("[TimerPolicy] Avaliando timer resolution...");
            var sw = Stopwatch.StartNew();

            try { CurrentTimerMs = QueryCurrentTimerMs(); }
            catch { CurrentTimerMs = 15.6; }

            int targetTicks = 156;
            string reason = "Timer padrão do Windows (15.6ms)";

            // P1: Context-aware — 0.5ms SÓ durante jogo detectado
            bool isGaming = App.GameDetectionService?.HasActiveRunningGameSession == true;

            if (thermal == ThermalState.Throttling)
            {
                targetTicks = 156;
                reason = "Thermal throttling — mantendo timer padrão para reduzir carga";
            }
            else if (power == PowerSource.Battery && hardware.MachineClass == MachineClass.Laptop)
            {
                if (hardware.IsLowEnd)
                {
                    targetTicks = 156;
                    reason = "Notebook low-end em bateria — timer padrão preserva bateria";
                }
                else
                {
                    targetTicks = 100;
                    reason = "Notebook em bateria — timer moderado (10ms)";
                }
            }
            else if (hardware.MachineClass == MachineClass.Laptop)
            {
                targetTicks = thermal == ThermalState.Hot ? 100 : 50;
                reason = thermal == ThermalState.Hot
                    ? "Notebook AC quente — timer moderado (5ms)"
                    : "Notebook AC — timer equilibrado (5ms)";
            }
            else // Desktop AC
            {
                if (isGaming)
                {
                    targetTicks = 5; // 0.5ms SÓ durante jogo
                    reason = "Desktop AC — JOGO ATIVO → timer 0.5ms para latência mínima";
                }
                else
                {
                    targetTicks = 156; // Fora de jogo: padrão Windows (evita consumo CPU/energia)
                    reason = "Desktop AC — IDLE/Work → timer padrão 15.6ms (economia energia)";
                }
            }

            if (hardware.IsHighEnd && thermal != ThermalState.Hot && thermal != ThermalState.Throttling)
            {
                if (hardware.MachineClass == MachineClass.Desktop && targetTicks > 10 && isGaming)
                {
                    targetTicks = 5;
                    reason += " + high-end gaming";
                }
            }

            if (hardware.IsLowEnd && power == PowerSource.Battery)
            {
                targetTicks = 156;
                reason = "Low-end em bateria — sem alteração para preservar autonomia";
            }

            TargetTimerMs = targetTicks * 0.1;
            Reason = reason;

            if (targetTicks < 156 && Math.Abs(CurrentTimerMs - TargetTimerMs) > 0.5)
            {
                try
                {
                    _logger?.LogInfo($"[TimerPolicy] Aplicando {targetTicks} ticks = {TargetTimerMs:F1}ms: {reason}");
                    var result = await _systemArm.SetTimerResolutionAsync((uint)targetTicks);
                    if (result)
                    {
                        Applied = true;
                        _logger?.LogSuccess($"[TimerPolicy] Timer alterado em {sw.ElapsedMilliseconds}ms");
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning($"[TimerPolicy] Não foi possível alterar timer (requer admin): {ex.Message}");
                }
            }
            else
            {
                if (targetTicks < 156 && Math.Abs(CurrentTimerMs - TargetTimerMs) <= 0.5)
                {
                    IsAlreadyOptimized = true;
                }
                _logger?.LogInfo($"[TimerPolicy] Timer mantido: {reason}");
            }

            return this;
        }

        private static double QueryCurrentTimerMs()
        {
            uint min = 0, max = 0, cur = 0;
            int result = NtQueryTimerResolution(ref min, ref max, ref cur);
            return result == 0 ? cur * 0.1 : 15.6;
        }

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtQueryTimerResolution(ref uint MinimumResolution, ref uint MaximumResolution, ref uint CurrentResolution);
    }
}
