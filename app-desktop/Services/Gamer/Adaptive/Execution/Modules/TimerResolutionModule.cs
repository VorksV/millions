using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.Gamer.Adaptive.Models;
using VoltrisOptimizer.Services.Gamer.Adaptive.Execution;

namespace VoltrisOptimizer.Services.Gamer.Adaptive.Execution.Modules
{
    /// <summary>
    /// Módulo de Timer Resolution - aplica 0.5ms de forma segura e reversível
    /// </summary>
    public class TimerResolutionModule : IOptimizationModule
    {
        public string Name => "Timer Resolution";
        public OptimizationType Type => OptimizationType.TimerResolution;
        public RiskLevel Risk => RiskLevel.LOW;
        
        private readonly ILoggingService _logger;
        
        // Rollback data keys
        private const string ROLLBACK_ORIGINAL_RESOLUTION = "OriginalResolution";
        private const string ROLLBACK_WAS_SET = "WasSet";
        
        public TimerResolutionModule(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public bool CanApply(OptimizationDecision decision, ExecutionPlan plan)
        {
            return decision.Action == OptimizationAction.APPLY && 
                   decision.Parameters.ContainsKey("TargetResolution");
        }
        
        public async Task<OptimizationResult> ApplyAsync(OptimizationDecision decision, ExecutionPlan plan, CancellationToken ct = default)
        {
            _logger.LogInfo(nameof(ApplyAsync));
            
            var result = new OptimizationResult();
            
            try
            {
                // Get current resolution for rollback
                var (currentRes, _) = GetCurrentResolution();
                
                // Get target resolution from decision
                var targetMs = Convert.ToDouble(decision.Parameters["TargetResolution"]);
                var targetUnits = (uint)(targetMs * 10000); // Convert ms to 100ns units
                
                _logger.LogInfo($"[TimerResolution] Aplicando timer resolution: {targetMs}ms");
                _logger.LogInfo($"[TimerResolution] Resolução atual: {currentRes:F2}ms");
                
                // Apply new resolution
                var ntStatus = NtSetTimerResolution(targetUnits, true, out var actualRes);
                
                if (ntStatus == 0) // NT_SUCCESS
                {
                    var actualMs = actualRes / 10000.0;
                    
                    result.Success = true;
                    result.ChangesApplied = 1;
                    
                    // Store rollback data
                    result.RollbackData[ROLLBACK_ORIGINAL_RESOLUTION] = currentRes;
                    result.RollbackData[ROLLBACK_WAS_SET] = true;
                    
                    // Store metrics
                    result.Metrics["OriginalResolutionMs"] = currentRes;
                    result.Metrics["TargetResolutionMs"] = targetMs;
                    result.Metrics["ActualResolutionMs"] = actualMs;
                    
                    _logger.LogSuccess($"[TimerResolution] ✓ Timer resolution aplicado: {actualMs:F2}ms (target: {targetMs}ms)");
                }
                else
                {
                    result.Success = false;
                    result.ErrorMessage = $"NtSetTimerResolution failed with status: 0x{ntStatus:X8}";
                    _logger.LogError($"[TimerResolution] ✗ Falha ao aplicar timer resolution: {result.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                _logger.LogError($"[TimerResolution] ✗ Erro ao aplicar timer resolution: {ex.Message}", ex);
            }
            
            _logger.LogInfo(nameof(ApplyAsync));
            return result;
        }
        
        public async Task<OptimizationResult> RevertAsync(ExecutionPlan plan, CancellationToken ct = default)
        {
            _logger.LogInfo(nameof(RevertAsync));
            
            var result = new OptimizationResult();
            
            try
            {
                // Find applied decision for this module
                var decision = plan.Decisions.FirstOrDefault(d => d.Type == OptimizationType.TimerResolution);
                if (decision?.Action != OptimizationAction.APPLY)
                {
                    result.Success = true; // Nothing to revert
                    _logger.LogInfo("[TimerResolution] Nenhuma alteração para reverter");
                    return result;
                }
                
                _logger.LogInfo("[TimerResolution] Revertendo timer resolution...");
                
                // Release timer resolution (Windows will restore default)
                var ntStatus = NtSetTimerResolution(0, false, out var _);
                
                if (ntStatus == 0) // NT_SUCCESS
                {
                    result.Success = true;
                    result.ChangesReverted = 1;
                    
                    var (currentRes, _) = GetCurrentResolution();
                    _logger.LogSuccess($"[TimerResolution] ✓ Timer resolution revertido para: {currentRes:F2}ms");
                }
                else
                {
                    result.Success = false;
                    result.ErrorMessage = $"NtSetTimerResolution release failed: 0x{ntStatus:X8}";
                    _logger.LogWarning($"[TimerResolution] ⚠️ Falha ao reverter timer resolution: {result.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                _logger.LogError($"[TimerResolution] ✗ Erro ao reverter timer resolution: {ex.Message}", ex);
            }
            
            _logger.LogInfo(nameof(RevertAsync));
            return result;
        }
        
        public async Task<bool> ValidateAsync(ExecutionPlan plan, CancellationToken ct = default)
        {
            try
            {
                var (currentRes, _) = GetCurrentResolution();
                
                // Find the target resolution from plan
                var decision = plan.Decisions.FirstOrDefault(d => d.Type == OptimizationType.TimerResolution);
                if (decision?.Action != OptimizationAction.APPLY || !decision.Parameters.ContainsKey("TargetResolution"))
                    return true; // Nothing to validate
                
                var targetMs = Convert.ToDouble(decision.Parameters["TargetResolution"]);
                
                // Allow some tolerance (±0.1ms)
                var tolerance = 0.1;
                var isValid = Math.Abs(currentRes - targetMs) <= tolerance;
                
                _logger.LogDebug($"[TimerResolution] Validação: current={currentRes:F2}ms, target={targetMs}ms, valid={isValid}");
                
                return isValid;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[TimerResolution] Erro na validação: {ex.Message}");
                return false;
            }
        }
        
        #region Native Methods
        
        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern uint NtQueryTimerResolution(out uint MinimumResolution, out uint MaximumResolution, out uint CurrentResolution);
        
        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern uint NtSetTimerResolution(uint DesiredResolution, bool SetResolution, out uint ActualResolution);
        
        private (double currentMs, double maxMs) GetCurrentResolution()
        {
            try
            {
                var status = NtQueryTimerResolution(out var min, out var max, out var current);
                if (status == 0) // NT_SUCCESS
                {
                    return (current / 10000.0, max / 10000.0);
                }
                return (15.6, 0.5); // Default Windows values
            }
            catch
            {
                return (15.6, 0.5); // Fallback
            }
        }
        
        #endregion
    }
}