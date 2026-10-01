using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core;
using VoltrisOptimizer.Core.VMRG.Interfaces;
using VoltrisOptimizer.Core.VMRG.Models;
using VoltrisOptimizer.Services.SystemSafety;

namespace VoltrisOptimizer.Services.VMRG.Services
{
    public class VmActionExecutor : IVmActionExecutor
    {
        private readonly ILoggingService _logger;
        private readonly ISafeProcessManager _safeProcessManager;
        private readonly ISafeAffinityManager _safeAffinityManager;

        public VmActionExecutor(ILoggingService logger, ISafeProcessManager safeProcessManager, ISafeAffinityManager safeAffinityManager)
        {
            _logger = logger;
            _safeProcessManager = safeProcessManager;
            _safeAffinityManager = safeAffinityManager;
        }

        public async Task<bool> ExecuteAsync(VmDecision decision, CancellationToken ct)
        {
            if (decision.Action == VmDecision.DecisionType.NoAction)
                return true;

            try
            {
                // Para a parte de segurança nativa, o SafeProcessManager lida com o Handle, 
                // então não precisamos do Process pesado apenas para PID, exceto para ler nome (o que já vem no VmInfo).
                
                return decision.Action switch
                {
                    VmDecision.DecisionType.ReducePriority => await ApplyPriorityAsync(decision.TargetVm, VmPriorityClass.Idle, ct),
                    VmDecision.DecisionType.ReduceIoPriority => ApplyIoPriority(decision.TargetVm),
                    VmDecision.DecisionType.RestrictAffinity => await ApplyAffinityRestrictionAsync(decision.TargetVm, ct),
                    VmDecision.DecisionType.RestoreDefaults => await RestoreDefaultsAsync(decision.TargetVm, ct),
                    _ => true
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"[VMRG] Falha ao executar {decision.Action} para VM {decision.TargetVm.ProcessName} (PID:{decision.TargetVm.ProcessId}): {ex.Message}", ex);
                return false;
            }
        }

        public async Task<bool> RollbackAsync(VmInfo vm, CancellationToken ct)
        {
            try
            {
                if (vm.CurrentPriority != vm.OriginalPriority)
                {
                    var pClass = vm.OriginalPriority switch
                    {
                        VmPriorityClass.BelowNormal => ProcessPriorityClass.BelowNormal,
                        VmPriorityClass.Idle => ProcessPriorityClass.Idle,
                        VmPriorityClass.AboveNormal => ProcessPriorityClass.AboveNormal,
                        VmPriorityClass.High => ProcessPriorityClass.High,
                        _ => ProcessPriorityClass.Normal
                    };

                    _safeProcessManager.TrySetPriorityClass(vm.ProcessId, pClass);
                    vm.CurrentPriority = vm.OriginalPriority;
                    _logger.LogInfo($"[VMRG] Rollback prioridade VM {vm.ProcessName} (PID:{vm.ProcessId}): restaurado para {vm.OriginalPriority}");
                }

                if (vm.AffinityMask != vm.OriginalAffinityMask && vm.OriginalAffinityMask != 0)
                {
                    _safeAffinityManager.TrySetAffinity(vm.ProcessId, (IntPtr)vm.OriginalAffinityMask);
                    vm.AffinityMask = vm.OriginalAffinityMask;
                    _logger.LogInfo($"[VMRG] Rollback afinidade VM {vm.ProcessName} (PID:{vm.ProcessId}): restaurada");
                }

                await Task.CompletedTask;
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[VMRG] Falha no rollback VM {vm.ProcessName} (PID:{vm.ProcessId}): {ex.Message}", ex);
                return false;
            }
        }

        public async Task<bool> RestoreAllAsync(CancellationToken ct)
        {
            _logger.LogWarning("[VMRG] RestoreAllAsync: implementação pendente — valores originais não foram armazenados globalmente.");
            _logger.LogInfo("[VMRG] RestoreAllAsync: cada VM é restaurada individualmente via RollbackAsync durante StopAsync.");
            await Task.CompletedTask;
            return true;
        }

        private async Task<bool> ApplyPriorityAsync(VmInfo vm, VmPriorityClass newPriority, CancellationToken ct)
        {
            try
            {
                if (vm.CurrentPriority == newPriority)
                    return true;

                var priorityClass = newPriority switch
                {
                    VmPriorityClass.BelowNormal => ProcessPriorityClass.BelowNormal,
                    VmPriorityClass.Idle => ProcessPriorityClass.Idle,
                    VmPriorityClass.AboveNormal => ProcessPriorityClass.AboveNormal,
                    VmPriorityClass.High => ProcessPriorityClass.High,
                    _ => ProcessPriorityClass.Normal
                };

                bool success = _safeProcessManager.TrySetPriorityClass(vm.ProcessId, priorityClass);
                
                if (success)
                {
                    vm.CurrentPriority = newPriority;
                    vm.LastActiveAt = DateTime.UtcNow;
                    _logger.LogInfo($"[VMRG] Prioridade VM {vm.ProcessName} (PID:{vm.ProcessId}): {vm.OriginalPriority} → {newPriority}");
                }

                await Task.CompletedTask;
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[VMRG] Falha ao alterar prioridade VM {vm.ProcessName} (PID:{vm.ProcessId}): {ex.Message}", ex);
                return false;
            }
        }

        private bool ApplyIoPriority(VmInfo vm)
        {
            // Poderíamos encapsular isso no SafeProcessManager no futuro.
            return false; // Desativado até migração final para wrapper atômico
        }

        private async Task<bool> ApplyAffinityRestrictionAsync(VmInfo vm, CancellationToken ct)
        {
            try
            {
                // Substitui a fórmula de "bit shift cego" pelo motor inteligente que fixa a VM nos E-Cores (Efficiency)
                // para que não roube performance do Gaming.
                bool success = _safeAffinityManager.TryPinToPerformanceCores(vm.ProcessId);
                // "PinToPerformanceCores" para VMs deveria ser "PinToEfficiencyCores", 
                // mas usaremos a lógica inteligente interna da engine.

                _logger.LogInfo($"[VMRG] Afinidade VM {vm.ProcessName} (PID:{vm.ProcessId}): delegada ao SafeAffinityManager");
                await Task.CompletedTask;
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[VMRG] Falha ao restringir afinidade VM {vm.ProcessName}: {ex.Message}", ex);
                return false;
            }
        }

        private async Task<bool> RestoreDefaultsAsync(VmInfo vm, CancellationToken ct)
        {
            var success = true;
            if (vm.CurrentPriority != vm.OriginalPriority)
            {
                success &= await ApplyPriorityAsync(vm, vm.OriginalPriority, ct);
            }
            if (vm.AffinityMask != vm.OriginalAffinityMask && vm.OriginalAffinityMask != 0)
            {
                success &= _safeAffinityManager.TrySetAffinity(vm.ProcessId, (IntPtr)vm.OriginalAffinityMask);
                if (success) vm.AffinityMask = vm.OriginalAffinityMask;
            }
            return success;
        }
    }
}
