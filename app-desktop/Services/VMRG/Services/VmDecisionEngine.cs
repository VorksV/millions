using System;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core;
using VoltrisOptimizer.Core.VMRG.Interfaces;
using VoltrisOptimizer.Core.VMRG.Models;

namespace VoltrisOptimizer.Services.VMRG.Services
{
    public class VmDecisionEngine : IVmDecisionEngine
    {
        private readonly IVmLearningService _learning;
        private readonly ILoggingService _logger;

        private const double HighCpuThreshold = 70.0;
        private const double CriticalRamThreshold = 90.0;
        private const double HighRamThreshold = 75.0;
        private const int UserInactiveMs = 30000;

        public VmDecisionEngine(IVmLearningService learning, ILoggingService logger)
        {
            _learning = learning;
            _logger = logger;
        }

        public async Task<VmDecision> DecideAsync(VmInfo vm, VmContext context, CancellationToken ct)
        {
            var decision = new VmDecision
            {
                TargetVm = vm,
                DecidedAt = DateTime.UtcNow
            };

            if (vm.WindowState == VmWindowState.Foreground)
            {
                decision.Action = VmDecision.DecisionType.NoAction;
                decision.Reason = "VM em primeiro plano — mantendo recursos";
                decision.Confidence = 1.0;
                return decision;
            }

            if (vm.WindowState == VmWindowState.Closed)
            {
                if (vm.ChangesApplied)
                {
                    decision.Action = VmDecision.DecisionType.RestoreDefaults;
                    decision.Reason = "VM encerrada — restaurando padrões";
                    decision.Confidence = 1.0;
                    return decision;
                }
                decision.Action = VmDecision.DecisionType.NoAction;
                decision.Reason = "VM encerrada — sem alterações a reverter";
                decision.Confidence = 1.0;
                return decision;
            }

            if (context.GamerModeActive && context.GameRunning)
            {
                decision.Action = VmDecision.DecisionType.ReducePriority;
                decision.Reason = "Modo Gamer ativo com jogo em execução — reduzindo prioridade da VM";
                decision.Confidence = 0.9;
                decision.RequiresValidation = true;
                return decision;
            }

            if (vm.ChangesApplied && !SystemUnderLoad(context) && vm.WindowState == VmWindowState.Foreground)
            {
                decision.Action = VmDecision.DecisionType.RestoreDefaults;
                decision.Reason = "VM em primeiro plano — restaurando recursos";
                decision.Confidence = 0.8;
                return decision;
            }

            // O usuário requisitou que qualquer VM em Background vá para idle imediatamente
            if (vm.WindowState == VmWindowState.Background)
            {
                decision.Action = VmDecision.DecisionType.ReducePriority;
                decision.Reason = "VM minimizada/background — aplicando Idle / Efficiency Mode";
                decision.Confidence = 0.95;
                decision.RequiresValidation = false; // Ação imediata solicitada pelo usuário
                return decision;
            }

            // Lógica legada de Heavy Load removida, pois a regra acima (Background = Idle) 
            // já engloba e tem prioridade maior.

            if (context.AvailableRamMb < 2048 && vm.WorkingSetMb > 1024 && vm.WindowState != VmWindowState.Foreground)
            {
                decision.Action = VmDecision.DecisionType.ReduceMemoryPriority;
                decision.Reason = $"RAM crítica ({context.AvailableRamMb}MB livre, VM usando {vm.WorkingSetMb}MB) — reduzindo prioridade de memória";
                decision.Confidence = 0.7;
                decision.RequiresValidation = true;
                return decision;
            }

            if (context.GamerModeActive && !context.GameRunning)
            {
                decision.Action = VmDecision.DecisionType.NoAction;
                decision.Reason = "Modo Gamer ativo sem jogo — VMRG em espera";
                decision.Confidence = 1.0;
                return decision;
            }

            if (UserIsInactive(context) && vm.WindowState == VmWindowState.Background)
            {
                decision.Action = VmDecision.DecisionType.ReducePriority;
                decision.Reason = "Usuário inativo — reduzindo prioridade da VM em segundo plano";
                decision.Confidence = 0.6;
                decision.RequiresValidation = true;
                return decision;
            }

            decision.Action = VmDecision.DecisionType.NoAction;
            decision.Reason = "Condições normais — nenhuma ação necessária";
            decision.Confidence = 0.9;
            return decision;
        }

        private static bool SystemUnderLoad(VmContext context)
        {
            return context.SystemCpuPercent > HighCpuThreshold ||
                   context.SystemRamPercent > HighRamThreshold;
        }

        private static bool SystemUnderHeavyLoad(VmContext context)
        {
            return context.SystemCpuPercent > HighCpuThreshold ||
                   context.SystemRamPercent > CriticalRamThreshold;
        }

        private static bool UserIsInactive(VmContext context)
        {
            return context.LastInputMs > UserInactiveMs;
        }
    }
}
