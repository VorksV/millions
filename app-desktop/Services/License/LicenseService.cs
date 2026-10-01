using System;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.License.Interfaces;
using VoltrisOptimizer.Services.License.Models;
using VoltrisOptimizer.Services.License.Exceptions;

namespace VoltrisOptimizer.Services.License
{
    /// <summary>
    /// Implementação de ILicenseService — Domain Layer.
    /// Fonte única de verdade para o LicenseGuard.
    /// Cache com TTL diferenciado: trial ativo (5min) vs expirado (1h).
    /// </summary>
    public class LicenseService : ILicenseService
    {
        private readonly HardwareTrialService _hwidTrialService;
        private readonly SemaphoreSlim _semaphore = new(1, 1);

        // Cache thread-safe
        private LicenseState? _cachedState;
        private DateTime _cacheTimestamp = DateTime.MinValue;
        private readonly object _cacheLock = new();

        private static readonly TimeSpan CacheDurationTrialActive = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan CacheDurationTrialExpired = TimeSpan.FromHours(1);
        private static readonly TimeSpan CacheDurationPro = TimeSpan.FromHours(24);

        public LicenseService()
        {
            _hwidTrialService = HardwareTrialService.Instance;
            App.LoggingService?.LogInfo("[LicenseService] Instância criada com HardwareTrialService.Instance");
        }

        public async Task<LicenseState> GetCurrentStateAsync(bool forceRefresh = false)
        {
            lock (_cacheLock)
            {
                if (forceRefresh)
                {
                    App.LoggingService?.LogInfo("[LicenseService] forceRefresh=true — limpando cache");
                    _cachedState = null;
                    _cacheTimestamp = DateTime.MinValue;
                }
                else if (_cachedState != null)
                {
                    var age = DateTime.UtcNow - _cacheTimestamp;
                    var ttl = GetCacheTtl(_cachedState);
                    if (age < ttl && !_cachedState.IsInconsistent)
                    {
                        App.LoggingService?.LogTrace($"[LicenseService] Cache válido (age={age.TotalMinutes:F1}min, ttl={ttl.TotalMinutes:F0}min) — retornando cache");
                        return _cachedState;
                    }
                    App.LoggingService?.LogInfo($"[LicenseService] Cache expirado (age={age.TotalMinutes:F1}min) ou inconsistente — atualizando");
                }
            }

            await _semaphore.WaitAsync();
            try
            {
                // Double-check após adquirir semáforo
                lock (_cacheLock)
                {
                    if (_cachedState != null && !forceRefresh)
                    {
                        var age = DateTime.UtcNow - _cacheTimestamp;
                        var ttl = GetCacheTtl(_cachedState);
                        if (age < ttl && !_cachedState.IsInconsistent)
                        {
                            App.LoggingService?.LogTrace("[LicenseService] Double-check: cache válido após semáforo");
                            return _cachedState;
                        }
                    }
                }

                return await RefreshFromBackendAsync();
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private static readonly System.Collections.Generic.HashSet<string> ProOnlyFeatures =
            new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "smart_repair", "gamer_mode", "stream_mode", "intelligent_optimization", "shield", "intelligent_profile" };

        private static bool IsPaidTier(LicenseState state)
        {
            var t = state.LicenseType?.ToLowerInvariant();
            return t == "standard" || t == "pro" || t == "enterprise";
        }

        public async Task<LicenseCheckResult> CheckFeatureAccessAsync(string feature, bool forceRefresh = false)
        {
            App.LoggingService?.LogTrace($"[LicenseService] CheckFeatureAccessAsync({feature}, forceRefresh={forceRefresh}) chamado");
            var state = await GetCurrentStateAsync(forceRefresh);

            // Licença paga (Standard, Pro ou Enterprise) tem prioridade e libera tudo
            if (LicenseTokenStore.IsProActive || IsPaidTier(state))
            {
                ProEngagementTracker.RecordConversion();
                App.LoggingService?.LogTrace($"[LicenseService] CheckFeatureAccessAsync({feature}): licença paga ativa → PERMITIDO");
                return LicenseCheckResult.Allowed(state, feature);
            }

            // Recursos PRO exigem licença paga (Standard/Pro/Enterprise)
            if (ProOnlyFeatures.Contains(feature))
            {
                App.LoggingService?.LogWarning($"[LicenseService] CheckFeatureAccessAsync({feature}): recurso PRO sem licença paga → BLOQUEADO");
                return LicenseCheckResult.Blocked(state, feature, "Recurso PRO — requer licença Standard, Pro ou Enterprise.");
            }

            // Todo o restante do programa é gratuito
            App.LoggingService?.LogTrace($"[LicenseService] CheckFeatureAccessAsync({feature}): recurso gratuito → PERMITIDO");
            return LicenseCheckResult.Allowed(state, feature);
        }

        public async Task InvalidateCacheAsync()
        {
            App.LoggingService?.LogInfo("[LicenseService] InvalidateCacheAsync chamado");
            lock (_cacheLock)
            {
                _cachedState = null;
                _cacheTimestamp = DateTime.MinValue;
            }
            await Task.CompletedTask;
        }

        public async Task<bool> RefreshIfNeededAsync()
        {
            App.LoggingService?.LogTrace("[LicenseService] RefreshIfNeededAsync chamado");
            try
            {
                var state = await GetCurrentStateAsync();
                if (state.IsInconsistent || state.IsNearExpiry)
                {
                    App.LoggingService?.LogInfo("[LicenseService] Estado inconsistente ou próximo de expirar — forçando refresh");
                    await GetCurrentStateAsync(forceRefresh: true);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[LicenseService] Erro no RefreshIfNeededAsync", ex);
                return false;
            }
        }

        // ─── Backend refresh ──────────────────────────────────────────────────

        private async Task<LicenseState> RefreshFromBackendAsync()
        {
            App.LoggingService?.LogInfo("[LicenseService] RefreshFromBackendAsync — consultando backend...");
            

            
            try
            {
                // Prioridade 1: Licença Pro (LicenseTokenStore)
                if (LicenseTokenStore.IsProActive)
                {
                    App.LoggingService?.LogInfo("[LicenseService] RefreshFromBackend: Pro ativo via LicenseTokenStore");
                    
                                        
                    var proState = new LicenseState
                    {
                        LicenseType = LicenseTokenStore.LicenseType,
                        IsActive = true,
                        IsTrialActive = false,
                        ExpiresAt = LicenseTokenStore.ExpiresAt == DateTime.MinValue ? null : LicenseTokenStore.ExpiresAt,
                        FormattedStatus = $"Licença {LicenseTokenStore.LicenseType} ativa",
                        SupportLevel = "Premium",
                        Message = "Licença Pro ativa",
                        LastChecked = DateTime.UtcNow
                    };
                    UpdateCache(proState);
                    return proState;
                }

                // Prioridade 2: Trial via HardwareTrialService (Supabase)
                App.LoggingService?.LogInfo("[LicenseService] RefreshFromBackend: verificando trial via HardwareTrialService...");
                await _hwidTrialService.ForceRefreshTrialStatusAsync(notify: false);
                var trialStatus = await _hwidTrialService.CheckTrialStatusAsync();

                if (trialStatus == null)
                {
                    App.LoggingService?.LogWarning("[LicenseService] RefreshFromBackend: HardwareTrialService retornou null — estado expirado");
                    var expiredState = BuildExpiredState("HardwareTrialService retornou null");
                    UpdateCache(expiredState);
                    return expiredState;
                }

                App.LoggingService?.LogInfo($"[LicenseService] RefreshFromBackend: trial — active={trialStatus.IsActive}, days={trialStatus.DaysRemaining}, online={trialStatus.IsOnlineMode}");

                var state = new LicenseState
                {
                    LicenseType = trialStatus.IsActive ? "Trial" : "None",
                    IsActive = trialStatus.IsActive,
                    IsTrialActive = trialStatus.IsActive && trialStatus.DaysRemaining > 0,
                    ExpiresAt = trialStatus.ExpiresAt,
                    FormattedStatus = BuildFormattedStatus(trialStatus),
                    SupportLevel = trialStatus.IsActive ? "Trial" : "None",
                    Message = trialStatus.Message ?? string.Empty,
                    LastChecked = DateTime.UtcNow
                };

                UpdateCache(state);
                App.LoggingService?.LogInfo($"[LicenseService] RefreshFromBackend concluído: {state.FormattedStatus}, days={state.DaysRemaining}");
                return state;
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[LicenseService] Erro no RefreshFromBackendAsync — retornando estado expirado (fail-closed)", ex);
                // FAIL-CLOSED: erro = estado expirado (não libera acesso)
                var errorState = BuildExpiredState($"Erro ao verificar licença: {ex.Message}");
                UpdateCache(errorState);
                return errorState;
            }
        }

        private void UpdateCache(LicenseState state)
        {
            lock (_cacheLock)
            {
                _cachedState = state;
                _cacheTimestamp = DateTime.UtcNow;
            }
            App.LoggingService?.LogTrace($"[LicenseService] Cache atualizado: {state.FormattedStatus}, TTL={GetCacheTtl(state).TotalMinutes:F0}min");
        }

        private TimeSpan GetCacheTtl(LicenseState state)
        {
            if (state.LicenseType != "None" && state.LicenseType != "Trial")
                return CacheDurationPro;
            if (state.IsTrialActive)
                return CacheDurationTrialActive;
            return CacheDurationTrialExpired;
        }

        private LicenseState BuildExpiredState(string message) => new LicenseState
        {
            LicenseType = "None",
            IsActive = false,
            IsTrialActive = false,
            ExpiresAt = null,
            FormattedStatus = "Sem Licença",
            SupportLevel = "None",
            Message = message,
            LastChecked = DateTime.UtcNow
        };

        private string BuildFormattedStatus(TrialStatus status)
        {
            if (!status.IsActive) return "Trial Expirado";
            if (status.DaysRemaining <= 0) return "Trial Expirando Hoje";
            if (status.DaysRemaining == 1) return "Trial: último dia";
            return $"Trial: {status.DaysRemaining} dias";
        }
    }
}
