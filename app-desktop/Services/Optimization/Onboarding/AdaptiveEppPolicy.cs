using System;
using System.Diagnostics;
using System.Threading.Tasks;
using VoltrisOptimizer.Core.Body;
using VoltrisOptimizer.Models;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Optimization.Onboarding
{
    public sealed class AdaptiveEppPolicy
    {
        private readonly ILoggingService _logger;
        private readonly IPowerArm _powerArm;

        public int CurrentEpp { get; private set; } = 50;
        public int TargetEpp { get; private set; } = 50;
        public bool Applied { get; private set; }
        public string Reason { get; private set; } = string.Empty;
        public bool IsAlreadyOptimized { get; private set; }

        public AdaptiveEppPolicy(ILoggingService logger, IPowerArm powerArm)
        {
            _logger = logger;
            _powerArm = powerArm;
        }

        public async Task<AdaptiveEppPolicy> EvaluateAsync(HardwareProfile hardware, ThermalState thermal, PowerSource power)
        {
            _logger?.LogInfo("[EppPolicy] Avaliando EPP (Energy Performance Preference)...");
            var sw = Stopwatch.StartNew();

            CurrentEpp = _powerArm.CurrentEpp;
            if (CurrentEpp <= 0) CurrentEpp = 50;

            int target = 50;
            string reason = "EPP equilibrado (50) — padrão do Windows";

            if (thermal == ThermalState.Throttling)
            {
                target = 100;
                reason = "Throttling detectado — EPP máximo para resfriar (100)";
            }
            else if (thermal == ThermalState.Hot)
            {
                target = 75;
                reason = "Temperatura elevada — EPP conservador (75)";
            }
            else if (power == PowerSource.Battery)
            {
                if (hardware.IsLowEnd)
                {
                    target = 60;
                    reason = "Low-end em bateria — EPP equilibrado (60)";
                }
                else
                {
                    target = 50;
                    reason = "Bateria — EPP equilibrado (50)";
                }
            }
            else if (hardware.MachineClass == MachineClass.Laptop)
            {
                target = 35;
                reason = "Notebook AC — EPP moderado (35)";
            }
            else if (hardware.CpuVendor == CpuVendor.Amd)
            {
                target = 25;
                reason = "Desktop AMD — EPP otimizado (25) — Ryzen responde bem";
            }
            else if (hardware.IsEfficientCoreCpu)
            {
                target = 25;
                reason = "Desktop Intel híbrido — EPP moderado (25) respeita eficiência";
            }
            else // Desktop Intel
            {
                // P1: High-end desktop Intel AC cool → EPP=0 (performance máxima)
                if (hardware.IsHighEnd && thermal == ThermalState.Cool && power == PowerSource.AC)
                {
                    target = 0;
                    reason = "Desktop Intel high-end AC cool → EPP=0 (performance máxima, latência zero)";
                }
                else
                {
                    target = 15;
                    reason = "Desktop Intel — EPP agressivo (15) para baixa latência";
                }
            }

            if (hardware.IsHighEnd && thermal == ThermalState.Cool && power == PowerSource.AC)
            {
                if (target > 15) target = 15;
                reason += " + hardware high-end";
            }

            int delta = Math.Abs(target - CurrentEpp);
            TargetEpp = target;
            Reason = reason;

            if (delta >= 25)
            {
                try
                {
                    _logger?.LogInfo($"[EppPolicy] Aplicando EPP {CurrentEpp}→{target}: {reason}");
                    var result = await _powerArm.SetEppAsync(target);
                    if (result.Executed)
                    {
                        Applied = true;
                        _logger?.LogSuccess($"[EppPolicy] EPP alterado em {sw.ElapsedMilliseconds}ms");
                    }
                    else if (result.RequiresAdmin)
                    {
                        _logger?.LogWarning("[EppPolicy] EPP requer admin — não aplicado");
                    }
                    else if (result.CooldownBlocked)
                    {
                        _logger?.LogInfo("[EppPolicy] EPP em cooldown — pulando");
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning($"[EppPolicy] Erro ao aplicar EPP: {ex.Message}");
                }
            }
            else
            {
                IsAlreadyOptimized = true;
                _logger?.LogInfo($"[EppPolicy] EPP já próximo do alvo ({CurrentEpp}→{target}, delta={delta}): {reason}");
            }

            return this;
        }
    }
}
