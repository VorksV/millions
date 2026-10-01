using System;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.License
{
    /// <summary>
    /// Feature Gate global — controla acesso a todas as funcionalidades.
    /// Comportamento: FAIL-CLOSED (exceção = bloqueado, nunca liberado).
    /// O estado é derivado exclusivamente do LicenseTokenStore (Pro) e
    /// HardwareTrialService (Trial) — sem lógica local bypassável.
    /// </summary>
    public class GlobalFeatureGateService
    {
        private static readonly Lazy<GlobalFeatureGateService> _instance =
            new(() => new GlobalFeatureGateService());
        public static GlobalFeatureGateService Instance => _instance.Value;

        private GlobalFeatureGateService()
        {
            App.LoggingService?.LogInfo("[FeatureGate] GlobalFeatureGateService inicializado");
        }

        // ─── Verificação síncrona (para UI binding) ───────────────────────────

        /// <summary>
        /// Verifica se uma funcionalidade está disponível.
        /// FAIL-CLOSED: qualquer exceção retorna false (bloqueado).
        /// </summary>
        public bool IsFeatureEnabled(string featureName)
        {
            App.LoggingService?.LogTrace($"[FeatureGate] IsFeatureEnabled({featureName}) chamado");
            try
            {
                var result = EvaluateAccess(featureName);
                App.LoggingService?.LogTrace($"[FeatureGate] IsFeatureEnabled({featureName}) → {result}");
                return result;
            }
            catch (Exception ex)
            {
                // FAIL-CLOSED: exceção = bloqueado
                App.LoggingService?.LogError($"[FeatureGate] EXCEÇÃO em IsFeatureEnabled({featureName}) — BLOQUEANDO por segurança", ex);
                return false;
            }
        }

        /// <summary>
        /// Verifica se o usuário pode usar qualquer funcionalidade.
        /// FAIL-CLOSED: exceção = bloqueado.
        /// </summary>
        public bool CanUseAnyFeatures()
        {
            App.LoggingService?.LogTrace("[FeatureGate] CanUseAnyFeatures chamado");
            try
            {
                var result = EvaluateAccess("any");
                App.LoggingService?.LogTrace($"[FeatureGate] CanUseAnyFeatures → {result}");
                return result;
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[FeatureGate] EXCEÇÃO em CanUseAnyFeatures — BLOQUEANDO por segurança", ex);
                return false;
            }
        }

        /// <summary>
        /// Lança LicenseExpiredException se a funcionalidade não estiver disponível.
        /// FAIL-CLOSED: exceção interna = bloqueia.
        /// </summary>
        public void EnsureFeatureEnabled(string featureName)
        {
            App.LoggingService?.LogTrace($"[FeatureGate] EnsureFeatureEnabled({featureName}) chamado");
            try
            {
                if (!EvaluateAccess(featureName))
                {
                    App.LoggingService?.LogWarning($"[FeatureGate] BLOQUEADO: {featureName} — licença inativa ou trial expirado");
                    throw new LicenseExpiredException($"A funcionalidade '{featureName}' requer uma licença válida.");
                }
                App.LoggingService?.LogTrace($"[FeatureGate] PERMITIDO: {featureName}");
            }
            catch (LicenseExpiredException)
            {
                throw; // Re-lançar exceção de licença sem modificar
            }
            catch (Exception ex)
            {
                // FAIL-CLOSED: qualquer outra exceção = bloqueia
                App.LoggingService?.LogError($"[FeatureGate] EXCEÇÃO em EnsureFeatureEnabled({featureName}) — BLOQUEANDO por segurança", ex);
                throw new LicenseExpiredException($"Erro ao verificar licença para '{featureName}'. Acesso negado por segurança.");
            }
        }

        // ─── Execução condicional ─────────────────────────────────────────────

        public async Task<T> ExecuteIfFeatureEnabledAsync<T>(string featureName, Func<Task<T>> action, T defaultValue = default!)
        {
            App.LoggingService?.LogTrace($"[FeatureGate] ExecuteIfFeatureEnabledAsync({featureName}) chamado");
            try
            {
                EnsureFeatureEnabled(featureName);
                return await action();
            }
            catch (LicenseExpiredException ex)
            {
                App.LoggingService?.LogWarning($"[FeatureGate] Ação bloqueada por licença: {featureName} — {ex.Message}");
                return defaultValue;
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[FeatureGate] Erro ao executar ação {featureName}", ex);
                return defaultValue;
            }
        }

        public async Task ExecuteIfFeatureEnabledAsync(string featureName, Func<Task> action)
        {
            App.LoggingService?.LogTrace($"[FeatureGate] ExecuteIfFeatureEnabledAsync(void)({featureName}) chamado");
            try
            {
                EnsureFeatureEnabled(featureName);
                await action();
            }
            catch (LicenseExpiredException ex)
            {
                App.LoggingService?.LogWarning($"[FeatureGate] Ação bloqueada por licença: {featureName} — {ex.Message}");
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[FeatureGate] Erro ao executar ação {featureName}", ex);
            }
        }

        public T ExecuteIfFeatureEnabled<T>(string featureName, Func<T> action, T defaultValue = default!)
        {
            App.LoggingService?.LogTrace($"[FeatureGate] ExecuteIfFeatureEnabled({featureName}) chamado");
            try
            {
                EnsureFeatureEnabled(featureName);
                return action();
            }
            catch (LicenseExpiredException ex)
            {
                App.LoggingService?.LogWarning($"[FeatureGate] Ação bloqueada por licença: {featureName} — {ex.Message}");
                return defaultValue;
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[FeatureGate] Erro ao executar ação {featureName}", ex);
                return defaultValue;
            }
        }

        public void ExecuteIfFeatureEnabled(string featureName, Action action)
        {
            App.LoggingService?.LogTrace($"[FeatureGate] ExecuteIfFeatureEnabled(void)({featureName}) chamado");
            try
            {
                EnsureFeatureEnabled(featureName);
                action();
            }
            catch (LicenseExpiredException ex)
            {
                App.LoggingService?.LogWarning($"[FeatureGate] Ação bloqueada por licença: {featureName} — {ex.Message}");
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[FeatureGate] Erro ao executar ação {featureName}", ex);
            }
        }

        // ─── Lógica central de avaliação ──────────────────────────────────────

        private bool EvaluateAccess(string featureName)
        {
            // PRIORIDADE 1: Licença Pro ativa (LicenseTokenStore — valor em memória ultra-rápido)
            if (LicenseTokenStore.IsProActive)
            {
                App.LoggingService?.LogTrace($"[FeatureGate] EvaluateAccess({featureName}): Pro ativo via TokenStore → PERMITIDO");
                return true;
            }

            // PRIORIDADE 2: Verificar cache do orquestrador (evita WMI síncrono)
            if (LicenseOrchestrationService.Instance.IsActive)
            {
                App.LoggingService?.LogTrace($"[FeatureGate] EvaluateAccess({featureName}): Licença/Trial ativa via Cache Orquestrador → PERMITIDO");
                return true;
            }

            // PRIORIDADE 3: Fallback apenas se necessário (evitar se estivermos na UI thread)
            // Se chegamos aqui, o cache diz que não está ativo. 
            // Não queremos disparar LicenseManager.Instance.IsTrialExpired() que pode travar a UI.
            
            App.LoggingService?.LogTrace($"[FeatureGate] EvaluateAccess({featureName}): sem licença ativa detectada no cache → BLOQUEADO");
            return false;
        }
    }
}
