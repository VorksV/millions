using System;
using VoltrisOptimizer.Services.Performance.CpuTuning.Models;

namespace VoltrisOptimizer.Services.Performance.CpuTuning
{
    /// <summary>
    /// Intelligent performance policy engine with adaptive decision-making
    /// </summary>
    public class PerformancePolicyEngine : IPerformancePolicyEngine
    {
        private readonly ILoggingService _logger;
        private readonly IThermalGovernor _thermalGovernor;
        
        private DateTime _lastAdjustmentTime = DateTime.MinValue;
        private const int AdjustmentCooldownSeconds = 30; // Prevent oscillation
        
        // Workload classification state
        private DateTime _idleStartTime = DateTime.MaxValue;
        private const int IdleThresholdSeconds = 10;
        
        public PerformancePolicyEngine(ILoggingService logger, IThermalGovernor thermalGovernor)
        {
            _logger = logger;
            _thermalGovernor = thermalGovernor;
        }
        
        public WorkloadType ClassifyWorkload(CpuMetrics metrics)
        {
            // Rule 1: Memory-bound (highest priority after thermal)
            if (metrics.RamUsagePercent > 90.0)
            {
                return WorkloadType.MemoryBound;
            }
            
            // Rule 2: CPU-bound
            if (metrics.CpuUsagePercent > 88.0 && metrics.GpuUsagePercent < 75.0)
            {
                ResetIdleTimer();
                return WorkloadType.CpuBound;
            }
            
            // Rule 3: GPU-bound
            if (metrics.GpuUsagePercent > 92.0 && metrics.CpuUsagePercent < 65.0)
            {
                ResetIdleTimer();
                return WorkloadType.GpuBound;
            }
            
            // Rule 4: Balanced
            if (metrics.CpuUsagePercent > 80.0 && metrics.GpuUsagePercent > 80.0)
            {
                ResetIdleTimer();
                return WorkloadType.Balanced;
            }
            
            // Rule 5: Idle (requires sustained low usage)
            if (metrics.CpuUsagePercent < 30.0)
            {
                if (_idleStartTime == DateTime.MaxValue)
                {
                    _idleStartTime = DateTime.UtcNow;
                }
                else if ((DateTime.UtcNow - _idleStartTime).TotalSeconds > IdleThresholdSeconds)
                {
                    return WorkloadType.Idle;
                }
            }
            else
            {
                ResetIdleTimer();
            }
            
            // Default: Balanced
            return WorkloadType.Balanced;
        }
        
        public int DetermineOptimalEPP(WorkloadType workload, ThermalState thermal, MachineClass machineClass, PlatformType platform)
        {
            // THERMAL OVERRIDE: If thermal protection is active, force efficiency
            if (_thermalGovernor.IsThermalProtectionActive)
            {
                _logger.LogInfo("[PolicyEngine] Thermal protection active - setting EPP to efficiency mode");
                return 128; // Balanced-to-efficiency
            }
            
            // THERMAL CAUTION: If in danger zone, be conservative
            if (thermal.IsInDangerZone)
            {
                return 64; // Balanced
            }
            
            // Workload-based EPP selection
            int baseEpp = workload switch
            {
                WorkloadType.CpuBound => 0,      // Maximum performance
                WorkloadType.Balanced => 32,      // Performance-biased
                WorkloadType.GpuBound => 50,      // Balanced (CPU not bottleneck)
                WorkloadType.MemoryBound => 64,   // Balanced
                WorkloadType.Idle => 128,         // Efficiency
                _ => 32
            };
            
            // Adjust based on machine class
            int adjustment = machineClass switch
            {
                MachineClass.LowEnd => 32,        // More conservative
                MachineClass.Entry => 16,
                MachineClass.MidRange => 0,
                MachineClass.HighEnd => -8,       // More aggressive
                MachineClass.Enthusiast => -16,
                _ => 0
            };
            
            int finalEpp = baseEpp + adjustment;

            // LAPTOP SAFETY: Most laptop firmware (Samsung/Dell) becomes unstable with EPP 0 in High Performance mode
            // This causes premature thermal throttling or VRM protection.
            if (platform == PlatformType.Laptop)
            {
                // Ensure EPP is at least 32 for performance stability on laptops
                if (finalEpp < 32 && workload != WorkloadType.CpuBound) finalEpp = 32;
                else if (finalEpp < 24) finalEpp = 24; // Absolute minimum for laptop high-performance stability
            }
            
            return Math.Clamp(finalEpp, 0, 255);
        }
        
        public (int pl1, int pl2, double tau) DetermineOptimalPowerLimits(WorkloadType workload, ThermalState thermal, CpuTuningCapabilities capabilities)
        {
            // Valores padrão de segurança (ponto de partida neutro)
            int baseTdp = capabilities.BaseTdp;
            var machineClass = capabilities.Classification;
            var platform = capabilities.Platform;

            int finalPl1 = baseTdp;
            int finalPl2 = (int)(baseTdp * 1.25);
            double finalTau = 28.0;

            // 1. THERMAL EMERGENCY: Se proteção ativa, reduz drasticamente
            if (_thermalGovernor.IsThermalProtectionActive)
            {
                finalPl1 = Math.Max(baseTdp - 15, 15);
                finalPl2 = finalPl1; // Desativa boost temporariamente
                _logger.LogWarning($"[PolicyEngine] Thermal Protection! Reduzindo PL1 para {finalPl1}W");
                return (finalPl1, finalPl2, 8.0);
            }

            // 2. Cálculo adaptativo por tipo de máquina
            // Política VOLTRIS: nunca "capar" artificialmente notebooks finos durante o Modo Gamer.
            // Usamos o TDP base como piso e deixamos a segurança totalmente a cargo do ThermalGovernor.
            if (platform == PlatformType.Laptop)
            {
                // Todos os laptops (inclusive LowEnd/Entry) partem de, no mínimo, baseTdp.
                finalPl1 = baseTdp;
                finalPl2 = (int)(baseTdp * 1.25);

                // Se existir um preset gamer específico para este CPU (ex: i5-1135G7), usar como alvo principal
                if (capabilities.GamerPl1Override.HasValue && capabilities.GamerPl2Override.HasValue)
                {
                    finalPl1 = capabilities.GamerPl1Override.Value;
                    finalPl2 = capabilities.GamerPl2Override.Value;
                    if (capabilities.GamerTauOverride.HasValue)
                        finalTau = capabilities.GamerTauOverride.Value;
                }

                // Notebooks gamer / HighEnd podem esticar PL1 quando há folga térmica real.
                if (machineClass >= MachineClass.HighEnd && !thermal.IsInDangerZone && thermal.ThermalHeadroom > 15)
                {
                    if (workload == WorkloadType.CpuBound || workload == WorkloadType.Balanced)
                    {
                        finalPl1 = baseTdp + 10;
                        finalPl2 = finalPl1 + 20;
                        finalTau = 56.0;
                    }
                }
            }
            else // Desktop
            {
                if (machineClass >= MachineClass.HighEnd)
                {
                    // Desktops entusiastas podem ignorar limites conservadores de firmware se houver folga térmica
                    if (!thermal.IsInDangerZone && thermal.ThermalHeadroom > 20)
                    {
                        finalPl1 = Math.Max(baseTdp + 25, 125);
                        finalPl2 = finalPl1 + 50;
                        finalTau = 96.0;
                    }
                }
            }

            // 3. Ajuste por Carga Atual (Power Stepping)
            if (workload == WorkloadType.Idle)
            {
                finalPl1 = Math.Max(baseTdp - 10, 10);
                finalPl2 = finalPl1 + 5;
            }
            else if (workload == WorkloadType.CpuBound && !thermal.IsInDangerZone)
            {
                // Se estamos em carga pesada e frio, maximizamos o Tau para manter o boost por mais tempo
                finalTau = Math.Max(finalTau, 56.0);
            }

            // 4. Salvaguarda de Danger Zone
            if (thermal.IsInDangerZone)
            {
                finalPl1 = Math.Max(baseTdp - 5, 15);
                finalPl2 = finalPl1 + 10;
                finalTau = 8.0;
            }

            return (finalPl1, finalPl2, finalTau);
        }
        
        public bool ShouldDisableCStates(WorkloadType workload, double frameTimeJitter)
        {
            // Disable C-States if:
            // 1. CPU-bound workload with high frame time jitter (latency-sensitive)
            // 2. Balanced workload with very high jitter
            
            if (workload == WorkloadType.CpuBound && frameTimeJitter > 6.0)
            {
                return true; // Disable for latency reduction
            }
            
            if (workload == WorkloadType.Balanced && frameTimeJitter > 8.0)
            {
                return true;
            }
            
            // For other workloads, keep C-States enabled for power efficiency
            return false;
        }
        
        public bool CanAdjustTuning()
        {
            // Enforce cooldown period to prevent oscillation
            var timeSinceLastAdjustment = DateTime.UtcNow - _lastAdjustmentTime;
            
            if (timeSinceLastAdjustment.TotalSeconds < AdjustmentCooldownSeconds)
            {
                return false;
            }
            
            return true;
        }
        
        public void RecordAdjustment()
        {
            _lastAdjustmentTime = DateTime.UtcNow;
        }
        
        private void ResetIdleTimer()
        {
            _idleStartTime = DateTime.MaxValue;
        }
    }
}
