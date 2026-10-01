using System;
using VoltrisOptimizer.Models;

namespace VoltrisOptimizer.Services.Optimization
{
    /// <summary>
    /// DSL 5.0 - Safety Matrix & Decision Engine
    /// Implementa a matriz de veto cruzado entre motores.
    /// </summary>
    public class DecisionEngine50
    {
        public bool ShouldOptimizeCpu(SystemState50 state) => 
            state.CpuPressure >= PressureLevel.High || state.CpuUsagePercent > 85;

        public bool ShouldOptimizeMemory(SystemState50 state)
        {
            // SAFETY MATRIX VETO: NUNCA purgar se o disco estiver sob estresse (DiskActiveTime > 80% ou Queue > 1.8)
            // Impede o efeito 'Paging Storm'.
            if (state.DiskActiveTime > 80f || state.DiskQueueLength > 1.8f) return false;
            
            return state.MemoryPressure >= PressureLevel.High || state.CommitChargePercent > 80;
        }

        public bool ShouldOptimizeIo(SystemState50 state) => 
            state.IoPressure >= PressureLevel.High || state.DiskQueueLength > 2.0f;

        public bool ShouldApplyLowLatency(SystemState50 state)
        {
            // Boost se houver atividade de input recente (< 500ms)
            bool activeInput = state.LastInputActivityMs < 500;
            return activeInput || state.InputLatencyRisk || state.DpcTime > 0.8f;
        }

        public bool ShouldApplyThermalContention(SystemState50 state) => 
            state.ThermalThrottlingDetected;
    }
}

