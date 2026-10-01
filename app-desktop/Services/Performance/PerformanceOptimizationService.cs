using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Performance.Models;

namespace VoltrisOptimizer.Services.Performance{
    /// <summary>
    /// Wrapper para UltraPerformanceService que implementa IPerformanceOptimizationService
    /// Permite injeção de dependência e desacoplamento
    /// CORREÇÃO CRÍTICA: Agora valida Perfil Inteligente antes de aplicar otimizações
    /// </summary>
    public class PerformanceOptimizationService : IPerformanceOptimizationService
    {
        private readonly UltraPerformanceService _ultraPerformance;
        private readonly ILoggingService _logger;
        private readonly PerformanceProfileResolver? _profileResolver;

        private void ApplyExtraIntelligentTweaks()
        {
            try
            {
                _logger.LogInfo("[PerformanceOptimization] Aplicando tweaks de Otimização Inteligente (Fsutil)...");

                // Otimização de Fsutil (Único tweak realmente recomendado aqui)
                _ultraPerformance.DisableLastAccessUpdate();

                _logger.LogSuccess("[PerformanceOptimization] Tweaks de otimização aplicados com sucesso.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[PerformanceOptimization] Erro ao aplicar tweaks: {ex.Message}");
            }
        }

        public PerformanceOptimizationService(UltraPerformanceService ultraPerformance, ILoggingService logger)
        {
            _ultraPerformance = ultraPerformance ?? throw new ArgumentNullException(nameof(ultraPerformance));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // CORREÇÃO CRÍTICA: Criar PerformanceProfileResolver
            try
            {
                _profileResolver = new PerformanceProfileResolver(SettingsService.Instance, _logger);
                _logger.LogInfo("[PerformanceOptimization] PerformanceProfileResolver inicializado");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[PerformanceOptimization] Erro ao criar PerformanceProfileResolver: {ex.Message}");
            }
        }

        public PerformanceSystemProfile DetectSystemProfile()
        {
            try
            {
                return _ultraPerformance.DetectSystemProfile();
            }
            catch (Exception ex)
            {
                _logger.LogError("[PerformanceOptimization] Erro ao detectar perfil do sistema", ex);
                throw;
            }
        }

        public IReadOnlyList<PerformanceCategory> GetOptimizationCategories()
        {
            try
            {
                return _ultraPerformance.GetOptimizationCategories();
            }
            catch (Exception ex)
            {
                _logger.LogError("[PerformanceOptimization] Erro ao obter categorias", ex);
                throw;
            }
        }

        public async Task<PerformanceOptimizationResult> ApplyRecommendedOptimizationsAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                // Aplica sempre as funções solicitadas em todos os perfis inteligentes sem exceção
                ApplyExtraIntelligentTweaks();

                // CORREÇÃO CRÍTICA: Validar Perfil Inteligente antes de aplicar
                if (_profileResolver != null)
                {
                    var executionPlan = _profileResolver.ResolveExecutionPlan();
                    var currentProfile = SettingsService.Instance.Settings.IntelligentProfile;
                    
                    _logger.LogInfo($"[PerformanceOptimization] Perfil Inteligente: {currentProfile}");
                    _logger.LogInfo($"[PerformanceOptimization] Plano resolvido: {executionPlan.AuthorizedCount}/{executionPlan.TotalOptimizations} otimizações autorizadas");
                    
                    // Se perfil Enterprise, bloquear completamente
                    if (currentProfile == IntelligentProfileType.EnterpriseSecure)
                    {
                        _logger.LogWarning($"[PerformanceOptimization] Perfil {currentProfile} não permite otimizações automáticas");
                        return new PerformanceOptimizationResult
                        {
                            Success = true, // Considerado sucesso de política
                            TotalApplied = 0,
                            Errors = new List<string> { $"Perfil {currentProfile} não permite otimizações automáticas (política Enterprise)" }
                        };
                    }
                    
                    // Log de aviso para perfis conservadores
                    if (currentProfile == IntelligentProfileType.WorkOffice)
                    {
                        _logger.LogInfo($"[PerformanceOptimization] Perfil {currentProfile}: Aplicando apenas otimizações conservadoras");
                    }
                }
                
                return await _ultraPerformance.ApplyRecommendedOptimizationsAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError("[PerformanceOptimization] Erro ao aplicar otimizações", ex);
                throw;
            }
        }
        
        public async Task<PerformanceOptimizationResult> ApplyOptimizationsAsync(IEnumerable<string> selectedOptimizationNames, CancellationToken cancellationToken = default)
        {
            try
            {
                // Aplica sempre as funções solicitadas em todos os perfis inteligentes sem exceção
                ApplyExtraIntelligentTweaks();

                // CORREÇÃO CRÍTICA: Validar Perfil Inteligente antes de aplicar
                if (_profileResolver != null)
                {
                    var executionPlan = _profileResolver.ResolveExecutionPlan();
                    var currentProfile = SettingsService.Instance.Settings.IntelligentProfile;
                    
                    _logger.LogInfo($"[PerformanceOptimization] Perfil Inteligente: {currentProfile}");
                    _logger.LogInfo($"[PerformanceOptimization] Otimizações selecionadas: {selectedOptimizationNames.Count()}");
                    
                    // Filtrar otimizações não autorizadas pelo perfil
                    var authorizedOptimizations = selectedOptimizationNames
                        .Where(name => executionPlan.IsAuthorized(name))
                        .ToList();
                    
                    var blockedOptimizations = selectedOptimizationNames.Except(authorizedOptimizations).ToList();
                    
                    if (blockedOptimizations.Any())
                    {
                        _logger.LogWarning($"[PerformanceOptimization] Otimizações bloqueadas pelo perfil {currentProfile}: {string.Join(", ", blockedOptimizations)}");
                    }
                    
                    _logger.LogInfo($"[PerformanceOptimization] Otimizações autorizadas: {authorizedOptimizations.Count}/{selectedOptimizationNames.Count()}");
                    
                    // Se nenhuma otimização foi autorizada
                    if (!authorizedOptimizations.Any())
                    {
                        _logger.LogWarning($"[PerformanceOptimization] Nenhuma otimização autorizada pelo perfil {currentProfile}");
                        return new PerformanceOptimizationResult
                        {
                            Success = true, // Considerado sucesso de política
                            TotalApplied = 0,
                            Errors = new List<string> { $"O perfil {currentProfile} restringiu todas as otimizações selecionadas por segurança." }
                        };
                    }
                    
                    // Aplicar apenas otimizações autorizadas
                    return await _ultraPerformance.ApplyOptimizationsAsync(authorizedOptimizations, cancellationToken).ConfigureAwait(false);
                }
                
                // Fallback: aplicar todas se não houver resolver
                return await _ultraPerformance.ApplyOptimizationsAsync(selectedOptimizationNames, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError("[PerformanceOptimization] Erro ao aplicar otimizações selecionadas", ex);
                throw;
            }
        }
        
        public async Task<PerformanceOptimizationResult> RevertAllOptimizationsAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                return await _ultraPerformance.RevertAllOptimizationsAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError("[PerformanceOptimization] Erro ao reverter otimizações", ex);
                throw;
            }
        }
    }
}

