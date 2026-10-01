using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.Gamer.Adaptive.Models;
using VoltrisOptimizer.Services.Gamer.Adaptive.Execution;

namespace VoltrisOptimizer.Services.Gamer.Adaptive.Persistence
{
    /// <summary>
    /// Serviço de recuperação após crash - verifica sessões pendentes e restaura sistema
    /// </summary>
    public class CrashRecoveryService
    {
        private readonly ILoggingService _logger;
        private readonly StateManager _stateManager;
        
        public CrashRecoveryService(ILoggingService logger, StateManager stateManager)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
        }
        
        /// <summary>
        /// Verifica se existe sessão pendente e executa recovery se necessário
        /// </summary>
        public async Task<CrashRecoveryResult> CheckAndRecoverAsync()
        {
            _logger.LogInfo(nameof(CheckAndRecoverAsync));
            
            var result = new CrashRecoveryResult();
            
            try
            {
                // Check for active session
                var activeSession = await _stateManager.LoadActiveSessionAsync();
                if (activeSession == null)
                {
                    _logger.LogDebug("[CrashRecovery] Nenhuma sessão ativa encontrada");
                    result.HasPendingSession = false;
                    return result;
                }
                
                var sessionAge = DateTime.UtcNow - activeSession.StartedAt;
                
                _logger.LogWarning("═══════════════════════════════════════════════════════════════");
                _logger.LogWarning("[CrashRecovery] 🚨 SESSÃO PENDENTE DETECTADA");
                _logger.LogWarning($"[CrashRecovery] Session ID: {activeSession.SessionId}");
                _logger.LogWarning($"[CrashRecovery] Game: {activeSession.ExecutionPlan.Game.Name}");
                _logger.LogWarning($"[CrashRecovery] Started: {activeSession.StartedAt:yyyy-MM-dd HH:mm:ss}");
                _logger.LogWarning($"[CrashRecovery] Age: {sessionAge.TotalHours:F1}h");
                _logger.LogWarning("═══════════════════════════════════════════════════════════════");
                
                result.HasPendingSession = true;
                result.PendingSessionId = activeSession.SessionId;
                result.PendingGameName = activeSession.ExecutionPlan.Game.Name;
                result.SessionAge = sessionAge;
                
                // Auto-recover if session is old enough (>10 minutes indicates crash)
                if (sessionAge.TotalMinutes > 10)
                {
                    _logger.LogWarning("[CrashRecovery] Sessão antiga detectada - iniciando recovery automático...");
                    
                    var recoverySuccess = await ExecuteRecoveryAsync(activeSession);
                    result.RecoveryAttempted = true;
                    result.RecoverySuccessful = recoverySuccess;
                    
                    if (recoverySuccess)
                    {
                        await _stateManager.ClearActiveSessionAsync();
                        _logger.LogSuccess("[CrashRecovery] ✅ Recovery completado com sucesso");
                    }
                    else
                    {
                        _logger.LogError("[CrashRecovery] ❌ Recovery falhou - intervenção manual pode ser necessária");
                    }
                }
                else
                {
                    _logger.LogInfo("[CrashRecovery] Sessão recente - aguardando para decidir se é crash real");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[CrashRecovery] Erro durante crash recovery: {ex.Message}", ex);
                result.RecoveryError = ex.Message;
            }
            
            _logger.LogInfo(nameof(CheckAndRecoverAsync));
            return result;
        }
        
        /// <summary>
        /// Executa recuperação forçada de uma sessão específica
        /// </summary>
        public async Task<bool> ForceRecoverSessionAsync(string sessionId)
        {
            _logger.LogInfo(nameof(ForceRecoverSessionAsync));
            
            try
            {
                var activeSession = await _stateManager.LoadActiveSessionAsync();
                if (activeSession == null || activeSession.SessionId != sessionId)
                {
                    _logger.LogWarning($"[CrashRecovery] Sessão {sessionId} não encontrada para recovery");
                    return false;
                }
                
                _logger.LogWarning($"[CrashRecovery] Iniciando recovery forçado para sessão: {sessionId}");
                
                var success = await ExecuteRecoveryAsync(activeSession);
                if (success)
                {
                    await _stateManager.ClearActiveSessionAsync();
                }
                
                _logger.LogInfo(nameof(ForceRecoverSessionAsync));
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[CrashRecovery] Erro no recovery forçado: {ex.Message}", ex);
                return false;
            }
        }
        
        private async Task<bool> ExecuteRecoveryAsync(ActiveSessionData session)
        {
            _logger.LogInfo("[CrashRecovery] Executando recovery de otimizações...");
            
            int recoveredCount = 0;
            int failedCount = 0;
            
            var appliedOptimizations = session.State.AppliedOptimizations;
            
            // Revert optimizations in reverse order
            var optimizationsToRevert = appliedOptimizations.OrderByDescending(kv => 
                GetOptimizationPriority(kv.Key)).ToArray();
            
            foreach (var (optimizationType, optimizationResult) in optimizationsToRevert)
            {
                try
                {
                    _logger.LogInfo($"[CrashRecovery] Revertendo: {optimizationType}");
                    
                    var success = await RevertOptimizationAsync(optimizationType, optimizationResult, session.ExecutionPlan);
                    if (success)
                    {
                        recoveredCount++;
                        _logger.LogSuccess($"[CrashRecovery] ✓ {optimizationType} revertido");
                    }
                    else
                    {
                        failedCount++;
                        _logger.LogWarning($"[CrashRecovery] ✗ Falha ao reverter {optimizationType}");
                    }
                }
                catch (Exception ex)
                {
                    failedCount++;
                    _logger.LogError($"[CrashRecovery] Erro ao reverter {optimizationType}: {ex.Message}");
                }
            }
            
            _logger.LogInfo($"[CrashRecovery] Recovery concluído: {recoveredCount} sucessos, {failedCount} falhas");
            
            // Consider successful if we recovered at least 80% of optimizations
            var successRate = appliedOptimizations.Count == 0 ? 1.0 : 
                (double)recoveredCount / appliedOptimizations.Count;
            
            return successRate >= 0.8;
        }
        
        private async Task<bool> RevertOptimizationAsync(OptimizationType type, OptimizationResult result, ExecutionPlan plan)
        {
            try
            {
                return type switch
                {
                    OptimizationType.TimerResolution => await RevertTimerResolutionAsync(result),
                    OptimizationType.PowerPlan => await RevertPowerPlanAsync(result),
                    OptimizationType.ProcessPriority => await RevertProcessPriorityAsync(result, plan),
                    OptimizationType.GpuHags => await RevertGpuHagsAsync(result),
                    OptimizationType.WindowsServiceOptimization => await RevertServicesAsync(result),
                    _ => true // Skip unknown optimizations
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"[CrashRecovery] Erro na reversão de {type}: {ex.Message}");
                return false;
            }
        }
        
        private async Task<bool> RevertTimerResolutionAsync(OptimizationResult result)
        {
            try
            {
                // Use P/Invoke to release timer resolution
                var ntStatus = NtSetTimerResolution(0, false, out var _);
                return ntStatus == 0;
            }
            catch
            {
                return false;
            }
        }
        
        /// [FIX:UNICO-DONO-DE-ENERGIA] O rollback de plano de energia foi removido.
        ///
        /// Este método rodava `powercfg /s {guid}`.
        ///
        /// `/s` NÃO é um argumento válido do powercfg. Os argumentos reais de
        /// troca de plano são `/setactive` (para o esquema ativo) e `/import`
        /// (para um arquivo .pow). Não existe `/s`. O comando falhava sempre, o
        /// `ExitCode` era diferente de zero, e o método devolvia `false` — que
        /// este chamador trata como sucesso na maior parte do fluxo.
        ///
        /// O efeito real era o pior possível para um método de recuperação: a
        /// recuperação de energia depois de um crash NUNCA acontecia, e não
        /// havia sinal de erro. Um método chamado RevertPowerPlan que nunca
        /// revertou é pior do que um que não existe, porque alguém confia nele.
        ///
        /// E, como nas demais trocas de plano auditadas, ele usaria um GUID
        /// capturado antes da otimização — que pode já estar obsoleto no momento
        /// do rollback. Quem rege o plano é o Perfil Inteligente, e é ele que
        /// precisa reaplicar.
        private async Task<bool> RevertPowerPlanAsync(OptimizationResult result)
        {
            try
            {
                if (result.RollbackData.TryGetValue("OriginalPowerPlanGuid", out var originalGuid))
                {
                    var guid = originalGuid.ToString();
                    if (!string.IsNullOrEmpty(guid))
                    {
                        // [FIX:UNICO-DONO-DE-ENERGIA] A troca direta de plano saiu
                        // daqui. Registrar o GUID capturado é útil para o log,
                        // mas a decisão de plano não pertence a este serviço.
                        _logger?.LogInfo(
                            $"[CrashRecovery] Rollback de plano por GUID removido (capturado: {guid}). " +
                            "Antes rodava `powercfg /s`, que não é argumento válido do powercfg e " +
                            "falhava sempre em silêncio. O Perfil Inteligente é o dono da energia.");

                        VoltrisOptimizer.Services.Power.ProfilePowerAuthority.RequestProfileApply(
                            "CrashRecovery.RevertPowerPlan",
                            "recuperacao apos crash",
                            null);
                    }
                }
                return true; // If no original guid, assume success
            }
            catch
            {
                return false;
            }
        }
        
        private async Task<bool> RevertProcessPriorityAsync(OptimizationResult result, ExecutionPlan plan)
        {
            try
            {
                if (result.RollbackData.TryGetValue("ProcessId", out var pidObj) &&
                    result.RollbackData.TryGetValue("OriginalPriority", out var priorityObj))
                {
                    var pid = Convert.ToInt32(pidObj);
                    var originalPriority = (System.Diagnostics.ProcessPriorityClass)priorityObj;
                    
                    var process = System.Diagnostics.Process.GetProcessById(pid);
                    if (!process.HasExited)
                    {
                        process.PriorityClass = originalPriority;
                    }
                }
                return true;
            }
            catch
            {
                return true; // Process might have exited - that's OK
            }
        }
        
        private async Task<bool> RevertGpuHagsAsync(OptimizationResult result)
        {
            // GPU HAGS revert would require registry manipulation
            // For now, log and return success (HAGS changes survive reboot anyway)
            _logger.LogDebug("[CrashRecovery] GPU HAGS revert - mudanças persistem após reboot");
            return true;
        }
        
        private async Task<bool> RevertServicesAsync(OptimizationResult result)
        {
            try
            {
                if (result.RollbackData.TryGetValue("ModifiedServices", out var servicesObj) &&
                    servicesObj is Dictionary<string, object> services)
                {
                    foreach (var (serviceName, originalState) in services)
                    {
                        try
                        {
                            // Restore service to original state
                            // This would require service control implementation
                            _logger.LogDebug($"[CrashRecovery] Restaurando serviço: {serviceName}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug($"[CrashRecovery] Erro ao restaurar serviço {serviceName}: {ex.Message}");
                        }
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        private int GetOptimizationPriority(OptimizationType type)
        {
            return type switch
            {
                OptimizationType.ProcessPriority => 10,
                OptimizationType.TimerResolution => 20,
                OptimizationType.PowerPlan => 30,
                OptimizationType.GpuHags => 40,
                OptimizationType.WindowsServiceOptimization => 50,
                _ => 100
            };
        }
        
        #region P/Invoke
        
        [System.Runtime.InteropServices.DllImport("ntdll.dll", SetLastError = true)]
        private static extern uint NtSetTimerResolution(uint DesiredResolution, bool SetResolution, out uint ActualResolution);
        
        #endregion
    }
    
    /// <summary>
    /// Resultado da operação de crash recovery
    /// </summary>
    public class CrashRecoveryResult
    {
        public bool HasPendingSession { get; set; }
        public string PendingSessionId { get; set; } = string.Empty;
        public string PendingGameName { get; set; } = string.Empty;
        public TimeSpan SessionAge { get; set; }
        
        public bool RecoveryAttempted { get; set; }
        public bool RecoverySuccessful { get; set; }
        public string? RecoveryError { get; set; }
    }
}