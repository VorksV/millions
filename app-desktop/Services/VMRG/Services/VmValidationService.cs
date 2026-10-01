using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core;
using VoltrisOptimizer.Core.VMRG.Interfaces;
using VoltrisOptimizer.Core.VMRG.Models;

namespace VoltrisOptimizer.Services.VMRG.Services
{
    public class VmValidationService : IVmValidationService
    {
        private readonly ILoggingService _logger;

        public VmValidationService(ILoggingService logger)
        {
            _logger = logger;
        }

        public async Task<VmValidationResult> ValidateAsync(VmInfo before, VmInfo after, VmDecision decision, CancellationToken ct)
        {
            var result = new VmValidationResult();

            try
            {
                var cpuBefore = before.CpuUsagePercent;
                var cpuAfter = after.CpuUsagePercent;
                result.CpuDelta = cpuAfter - cpuBefore;

                var ramBefore = before.WorkingSetMb;
                var ramAfter = after.WorkingSetMb;
                result.RamDelta = ramAfter - ramBefore;

                var priorityChanged = before.CurrentPriority != after.CurrentPriority;
                var affinityChanged = before.AffinityMask != after.AffinityMask;

                using var proc = GetProcessSafe(after.ProcessId);
                if (proc != null)
                {
                    var actualPriority = VmInfo.FromProcessPriorityClass(proc.PriorityClass);
                    var expectedPriority = decision.Action == VmDecision.DecisionType.ReducePriority
                        ? VmPriorityClass.BelowNormal
                        : after.OriginalPriority;

                    if (decision.Action != VmDecision.DecisionType.NoAction)
                    {
                        if (actualPriority != expectedPriority && priorityChanged)
                        {
                            result.Success = false;
                            result.Details = $"Prioridade não aplicada: esperado {expectedPriority}, obtido {actualPriority}";
                            result.ShouldRollback = true;
                            _logger.LogWarning($"[VMRG] Validação falhou: {result.Details}");
                            return result;
                        }
                    }
                }

                if (decision.Action == VmDecision.DecisionType.ReducePriority && priorityChanged)
                {
                    var systemCpuAfter = SystemMetricsCache.Instance.CpuPercent;
                    if (systemCpuAfter < before.CpuUsagePercent - 5)
                    {
                        result.Success = true;
                        result.Details = $"CPU do sistema reduziu {before.CpuUsagePercent - systemCpuAfter:F1}% após alteração";
                    }
                    else if (systemCpuAfter > before.CpuUsagePercent + 10)
                    {
                        result.Success = false;
                        result.Details = $"CPU do sistema aumentou {systemCpuAfter - before.CpuUsagePercent:F1}% — possível regressão";
                        result.ShouldRollback = true;
                    }
                    else
                    {
                        result.Success = true;
                        result.Details = $"CPU estável ({systemCpuAfter:F1}%), alteração aplicada sem regressão";
                    }
                }
                else
                {
                    result.Success = true;
                    result.Details = "Nenhuma alteração significativa necessária";
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Details = $"Erro na validação: {ex.Message}";
                result.ShouldRollback = true;
                _logger.LogError($"[VMRG] Erro na validação: {ex.Message}", ex);
            }

            await Task.CompletedTask;
            return result;
        }

        private static Process? GetProcessSafe(int pid)
        {
            try
            {
                return Process.GetProcessById(pid);
            }
            catch
            {
                return null;
            }
        }
    }
}
