using System;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.License.Models;

namespace VoltrisOptimizer.Services.License
{
    /// <summary>
    /// Orquestrador de licença — Single Source of Truth para toda a UI.
    /// Delega ao LicenseService (que usa LicenseTokenStore + HardwareTrialService).
    /// Cache de 30s para evitar chamadas excessivas da UI.
    /// </summary>
    public sealed class LicenseOrchestrationService : IDisposable
    {
        #region Singleton

        private static readonly Lazy<LicenseOrchestrationService> _instance =
            new(() => new LicenseOrchestrationService());
        public static LicenseOrchestrationService Instance => _instance.Value;

        private LicenseOrchestrationService()
        {
            App.LoggingService?.LogInfo("[LicenseOrchestrator] Instância criada");
            // CORREÇÃO: Adiar monitoramento inicial para não competir com o startup da MainWindow.
            // O Task.Delay(5000).ContinueWith() é fire-and-forget — a Task retornada por
            // StartStateMonitoringAsync() é ignorada pelo ContinueWith, o que pode suprimir
            // exceções. Substituído por Task.Run com delay explícito e tratamento de exceção.
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(5000).ConfigureAwait(false);
                    await StartStateMonitoringAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    App.LoggingService?.LogError($"[LicenseOrchestrator] Falha no monitoramento inicial: {ex.Message}");
                }
            });
        }

        #endregion

        #region Dependências

        private readonly SemaphoreSlim _stateLock = new(1, 1);
        private LicenseState? _cachedState;
        private DateTime _lastStateUpdate = DateTime.MinValue;
        private readonly TimeSpan _stateCacheDuration = TimeSpan.FromSeconds(30);
        private CancellationTokenSource? _monitoringCts;

        #endregion

        #region Eventos

        public event EventHandler<LicenseStateChangedEventArgs>? StateChanged;

        #endregion

        #region API Pública

        /// <summary>
        /// Obtém o estado atual da licença.
        /// Cache de 30s é para forçar atualização use RefreshStateAsync().
        /// </summary>
        public async Task<LicenseState> GetCurrentStateAsync(CancellationToken cancellationToken = default)
        {
            App.LoggingService?.LogTrace("[LicenseOrchestrator] GetCurrentStateAsync chamado");

            // Cache check (sem lock para performance)
            if (_cachedState != null && DateTime.UtcNow - _lastStateUpdate < _stateCacheDuration)
            {
                App.LoggingService?.LogTrace($"[LicenseOrchestrator] Cache válido (age={(DateTime.UtcNow - _lastStateUpdate).TotalSeconds:F1}s) é retornando cache: {_cachedState.LicenseType}");
                return _cachedState;
            }

            App.LoggingService?.LogDebug("[LicenseOrchestrator] Cache expirado é calculando novo estado...");

            await _stateLock.WaitAsync(cancellationToken);
            try
            {
                // Double-check
                if (_cachedState != null && DateTime.UtcNow - _lastStateUpdate < _stateCacheDuration)
                {
                    App.LoggingService?.LogTrace("[LicenseOrchestrator] Double-check: cache válido após lock");
                    return _cachedState;
                }

                var newState = await BuildCurrentStateAsync();
                await UpdateCachedStateAsync(newState);
                return newState;
            }
            finally
            {
                _stateLock.Release();
            }
        }

        /// <summary>
        /// Força refresh completo do estado (limpa cache e consulta backend).
        /// </summary>
        public async Task<LicenseState> RefreshStateAsync(CancellationToken cancellationToken = default)
        {
            App.LoggingService?.LogDebug("[LicenseOrchestrator] RefreshStateAsync chamado é forçando atualização");

            await _stateLock.WaitAsync(cancellationToken);
            try
            {
                _cachedState = null;
                _lastStateUpdate = DateTime.MinValue;
                App.LoggingService?.LogDebug("[LicenseOrchestrator] Cache limpo");

                await HardwareTrialService.Instance.ForceRefreshTrialStatusAsync();
                App.LoggingService?.LogDebug("[LicenseOrchestrator] HardwareTrialService refresh concluído");

                var freshState = await BuildCurrentStateAsync();
                await UpdateCachedStateAsync(freshState);
                App.LoggingService?.LogDebug($"[LicenseOrchestrator] RefreshStateAsync concluído: {freshState.LicenseType}, active={freshState.IsActive}");
                return freshState;
            }
            finally
            {
                _stateLock.Release();
            }
        }

        /// <summary>
        /// Invalida o cache de estado da licença (sem fazer refresh de trial backend).
        /// Usado quando LicenseTokenStore é atualizado para forçar reconstrução imediata.
        /// </summary>
        public void InvalidateCache()
        {
            try
            {
                var oldState = _cachedState;
                _cachedState = null;
                _lastStateUpdate = DateTime.MinValue;
                App.LoggingService?.LogDebug("[LicenseOrchestrator] Cache invalidado sincrono (por LicenseTokenStore update)");
                
                // Disparar evento para notificar Views que estado pode ter mudado
                StateChanged?.Invoke(this, new LicenseStateChangedEventArgs 
                { 
                    PreviousState = oldState, 
                    CurrentState = null,  // Será reconstruído na próxima leitura
                    ChangeTimestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[LicenseOrchestrator] Erro ao invalidar cache", ex);
            }
        }

        #endregion

        #region Feature Gates (UI binding)

        public static class Features
        {
            // OTIMIZAÇÃO: Usar estado cacheado do orquestrador para evitar re-avaliação WMI/IO síncrona na UI thread
            private static LicenseState? State => Instance._cachedState;

            // FREEMIUM: otimização é o CORE gratuito — disponível sempre.
            public static bool IsOptimizationEnabled => true;
            // PRO (modo gamer exige licença paga) — apenas com licença paga ativa.
            public static bool IsGamerModeEnabled => LicenseTokenStore.IsProActive || IsPaidLicenseActive();
            // FREEMIUM: ferramentas avançadas são gratuitas.
            public static bool IsAdvancedToolsEnabled => true;
            // FREEMIUM: monitoramento em tempo real é gratuito.
            public static bool IsRealTimeMonitoringEnabled => true;
            // FREEMIUM: limpeza ultra é gratuita — disponível sempre.
            public static bool IsCleanupEnabled => true;
            // FREEMIUM: reparação é gratuita — disponível sempre.
            public static bool IsRepairEnabled => true;
            // FREEMIUM: navegação é gratuita.
            public static bool IsNavigationEnabled => true;
            public static bool IsCloudSyncEnabled => LicenseTokenStore.IsProActive || (State?.IsActive == true && State?.LicenseType == "Pro");
            public static bool IsPremiumSupportEnabled => LicenseTokenStore.IsProActive || (State?.IsActive == true && State?.LicenseType == "Pro");

            /// <summary>
            /// Verifica se há uma licença paga (Standard/Pro/Enterprise) ativa no cache,
            /// sem chamadas assíncronas (seguro para UI thread).
            /// </summary>
            private static bool IsPaidLicenseActive()
            {
                var s = State;
                if (s?.IsActive != true) return false;
                var t = s.LicenseType;
                return t != null &&
                       (t.Equals("Standard", StringComparison.OrdinalIgnoreCase) ||
                        t.Equals("Pro", StringComparison.OrdinalIgnoreCase) ||
                        t.Equals("Enterprise", StringComparison.OrdinalIgnoreCase));
            }
        }

        #endregion

        #region Propriedades síncronas (UI binding)

        public LicenseState? CachedState => _cachedState;
        /// <summary>
        /// CORREÇÃO: Quando _cachedState é null (cache não populado durante boot),
        /// retornar false (fail-open) para não bloquear navegação antes da inicialização.
        /// </summary>
        public bool IsTrialExpired => _cachedState != null ? (!_cachedState.IsTrialActive && !LicenseTokenStore.IsProActive) : false;
        public int DaysRemaining => Math.Max(0, _cachedState?.DaysRemaining ?? 0);
        public bool IsActive => _cachedState?.IsActive == true;
        public LicenseType LicenseType => ParseLicenseType(_cachedState?.LicenseType ?? "None");

        #endregion

        #region Construção de estado

        private async Task<LicenseState> BuildCurrentStateAsync()
        {
            // PROTEÇÃO CONTRA RE-ATIVAÇÃO: Verificar estado crítico de Token Store
            App.LoggingService?.LogDebug($"[LicenseOrchestrator] BuildCurrentStateAsync iniciado");
            
            // 🔴 PASSO 1: Licença Pro/Standard/Enterprise ativa via LicenseTokenStore
            if (LicenseTokenStore.IsProActive)
            {
                App.LoggingService?.LogInfo($"[LicenseOrchestrator] ✓ Licença Pro ativa detectada no TokenStore");
                
                // PROTEÇÃO: Validar que tipo não é "None"
                if (LicenseTokenStore.LicenseType == "None")
                {
                    App.LoggingService?.LogError($"[LicenseOrchestrator] ❌ ERRO CRÍTICO: IsProActive=true mas LicenseType='None'");
                    App.LoggingService?.LogError($"[LicenseOrchestrator] Isso indica corrupção de estado - revogando licença de emergência");
                    LicenseTokenStore.Revoke("emergency_recovery");
                    return CreateDefaultState();
                }
                
                var proState = CreateProLicenseState();
                return proState;
            }
            else
            {
                // ✅ TokenStore está limpo
                App.LoggingService?.LogDebug($"[LicenseOrchestrator] TokenStore sem licença ativa (esperado após desativação)");
            }

            // 🟡 PASSO 2: Trial via HardwareTrialService (Supabase)
            try
            {
                var hwidStatus = await HardwareTrialService.Instance.CheckTrialStatusAsync();
                if (hwidStatus != null)
                {
                    App.LoggingService?.LogDebug($"[LicenseOrchestrator] Trial detectado: {hwidStatus.DaysRemaining} dias restantes, Active={hwidStatus.IsActive}");
                    
                    // PROTEÇÃO: Se não está ativo, não criar estado Trial ativo
                    if (!hwidStatus.IsActive)
                    {
                        App.LoggingService?.LogDebug($"[LicenseOrchestrator] Trial não está ativo - pulando para fallback");
                    }
                    else
                    {
                        var mappedState = MapFromHardwareTrial(hwidStatus);
                        return mappedState;
                    }
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[LicenseOrchestrator] HardwareTrialService falhou", ex);
            }

            // 🟡 PASSO 3: Fallback para TrialProtectionService (legado local)
            App.LoggingService?.LogDebug($"[LicenseOrchestrator] PASSO 3: Fallback para TrialProtectionService (legado)");
            try
            {
                var legacyState = MapFromLegacyTrial();
                App.LoggingService?.LogDebug($"[LicenseOrchestrator] Legacy trial: active={legacyState.IsTrialActive}, days={legacyState.DaysRemaining}, Type={legacyState.LicenseType}");
                
                // PROTEÇÃO: Se licença legado ainda marcada como ativa após desativação
                if (legacyState.IsActive && LicenseTokenStore.LicenseType == "None")
                {
                    App.LoggingService?.LogWarning($"[LicenseOrchestrator] ⚠️  Legacy trial ativo mas TokenStore limpo - corrigindo para Default");
                    return CreateDefaultState();
                }
                
                return legacyState;
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[LicenseOrchestrator] TrialProtectionService falhou", ex);
            }

            // ⚫ PASSO 4: Estado padrão (sem licença)
            App.LoggingService?.LogInfo($"[LicenseOrchestrator] PASSO 4: Todos os serviços sem licença - estado DEFAULT");
            var defaultState = CreateDefaultState();
            return defaultState;
        }

        private LicenseState CreateProLicenseState()
        {
            var licenseInfo = LicenseManager.Instance.GetLicenseInfo();
            App.LoggingService?.LogDebug($"[LicenseOrchestrator] CreateProLicenseState INICIADO");
            App.LoggingService?.LogDebug($"[LicenseOrchestrator] LicenseTokenStore - Type={LicenseTokenStore.LicenseType}, DisplayName={LicenseTokenStore.LicenseDisplayName}, Expires={LicenseTokenStore.ExpiresAt:yyyy-MM-dd}, BillingPeriod={LicenseTokenStore.BillingPeriod}");
            App.LoggingService?.LogDebug($"[LicenseOrchestrator] LicenseManager - MaxDevices={licenseInfo?.MaxDevices}, ActiveDevices={licenseInfo?.ActiveDevices}");
            
            // CORREÇÃO: Extrair apenas o nome da licença sem duplicar "Licença X ativa"
            var displayName = LicenseTokenStore.LicenseDisplayName ?? $"VOLTRIS {LicenseTokenStore.LicenseType?.ToUpper()}";
            var cleanName = GetCleanLicenseName(displayName);
            var formattedStatus = cleanName;
            
            App.LoggingService?.LogDebug($"[LicenseOrchestrator] Nome final: displayName={displayName}, formattedStatus={formattedStatus}");
            
            var licenseState = new LicenseState
            {
                IsActive = true,
                IsTrialActive = false,
                LicenseType = LicenseTokenStore.LicenseType, // ?? CORREÇÃO: Usar o tipo real
                ExpiresAt = LicenseTokenStore.ExpiresAt == DateTime.MinValue ? null : LicenseTokenStore.ExpiresAt,
                LicenseKey = LicenseTokenStore.LicenseKey,
                MaxDevices = licenseInfo?.MaxDevices ?? 1,
                DevicesInUse = licenseInfo?.ActiveDevices ?? 0,
                IsOnlineMode = true,
                IsOfflineMode = false,
                LastValidated = DateTime.UtcNow,
                ValidationSource = "LicenseTokenStore",
                BillingPeriod = LicenseTokenStore.BillingPeriod,
                Message = $"Licença {LicenseTokenStore.LicenseType} ativa",
                CanRenew = false,
                CanUpgrade = false,
                SupportLevel = "Premium",
                FormattedStatus = formattedStatus
            };
            
            App.LoggingService?.LogSuccess($"[LicenseOrchestrator] CreateProLicenseState CONCLUÍDO: {licenseState.FormattedStatus}");
            return licenseState;
        }

        private LicenseState MapFromHardwareTrial(TrialStatus hwidStatus)
        {
            App.LoggingService?.LogDebug($"[LicenseOrchestrator] MapFromHardwareTrial: active={hwidStatus.IsActive}, days={hwidStatus.DaysRemaining}");
            return new LicenseState
            {
                IsActive = hwidStatus.IsActive,
                IsTrialActive = hwidStatus.IsActive,
                LicenseType = hwidStatus.IsActive ? "Trial" : "None",
                ExpiresAt = hwidStatus.ExpiresAt,
                LicenseKey = "",
                MaxDevices = 1,
                DevicesInUse = 0,
                IsOnlineMode = hwidStatus.IsOnlineMode,
                IsOfflineMode = hwidStatus.IsOfflineMode,
                LastValidated = DateTime.UtcNow,
                ValidationSource = "HardwareTrialService",
                Message = hwidStatus.Message,
                CanRenew = !hwidStatus.IsActive,
                CanUpgrade = hwidStatus.IsActive,
                SupportLevel = hwidStatus.IsActive ? "Basic" : "None",
                FormattedStatus = hwidStatus.IsActive ? LocalizationService.Instance["DashboardTrialActive"] : LocalizationService.Instance["DashboardTrialExpired"]
            };
        }

        private LicenseState MapFromLegacyTrial()
        {
            var isExpired = TrialProtectionService.Instance.IsTrialExpired();
            var daysRemaining = TrialProtectionService.Instance.GetDaysRemaining();
            var isActive = !isExpired && daysRemaining > 0;
            App.LoggingService?.LogDebug($"[LicenseOrchestrator] MapFromLegacyTrial: expired={isExpired}, days={daysRemaining}");
            return new LicenseState
            {
                IsActive = isActive,
                IsTrialActive = isActive,
                LicenseType = isActive ? "Trial" : "None",
                ExpiresAt = isActive ? DateTime.UtcNow.AddDays(daysRemaining) : null,
                LicenseKey = "",
                MaxDevices = 1,
                DevicesInUse = 0,
                IsOnlineMode = false,
                IsOfflineMode = true,
                LastValidated = DateTime.UtcNow,
                ValidationSource = "TrialProtectionService",
                Message = isActive ? LocalizationService.Instance["DashboardTrialActive"] + " (offline)" : LocalizationService.Instance["TrialExpiredBadge"],
                CanRenew = isExpired,
                CanUpgrade = isActive,
                SupportLevel = isActive ? "Basic" : "None",
                FormattedStatus = isActive ? LocalizationService.Instance["DashboardTrialActive"] : LocalizationService.Instance["DashboardTrialExpired"]
            };
        }

        private LicenseState CreateDefaultState()
        {
            App.LoggingService?.LogDebug("[LicenseOrchestrator] CreateDefaultState: sem licença");
            return new LicenseState
            {
                IsActive = false,
                IsTrialActive = false,
                LicenseType = "None",
                ExpiresAt = null,
                LicenseKey = "",
                MaxDevices = 0,
                DevicesInUse = 0,
                IsOnlineMode = false,
                IsOfflineMode = false,
                LastValidated = DateTime.UtcNow,
                ValidationSource = "Default",
                Message = "Nenhuma licença encontrada",
                CanRenew = true,
                CanUpgrade = true,
                SupportLevel = "None",
                FormattedStatus = LocalizationService.Instance["LicUnlicensed"]
            };
        }

        #endregion

        #region Cache e monitoramento

        private async Task UpdateCachedStateAsync(LicenseState newState)
        {
            var oldState = _cachedState;
            _cachedState = newState;
            _lastStateUpdate = DateTime.UtcNow;

            App.LoggingService?.LogDebug($"[LicenseOrchestrator] Estado atualizado: {newState.LicenseType}, active={newState.IsActive}, trialActive={newState.IsTrialActive}, days={newState.DaysRemaining}");

            var hasChanged = oldState == null || !oldState.Equals(newState);
            if (hasChanged)
            {
                App.LoggingService?.LogInfo($"[LicenseOrchestrator] Estado mudou: {oldState?.LicenseType ?? "null"} ? {newState.LicenseType}");
                StateChanged?.Invoke(this, new LicenseStateChangedEventArgs
                {
                    PreviousState = oldState,
                    CurrentState = newState,
                    ChangeTimestamp = DateTime.UtcNow
                });
            }

            await Task.CompletedTask;
        }

        private async Task StartStateMonitoringAsync()
        {
            App.LoggingService?.LogInfo("[LicenseOrchestrator] Monitoramento de estado iniciado (intervalo: 30m)");
            _monitoringCts?.Cancel();
            _monitoringCts = new CancellationTokenSource();

            _ = Task.Run(async () =>
            {
                try
                {
                    while (!_monitoringCts.Token.IsCancellationRequested)
                    {
                        await Task.Delay(TimeSpan.FromMinutes(30), _monitoringCts.Token);
                        if (!_monitoringCts.Token.IsCancellationRequested)
                        {
                            App.LoggingService?.LogTrace("[LicenseOrchestrator] Monitoramento: verificando estado...");
                            await RefreshStateAsync(_monitoringCts.Token);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    App.LoggingService?.LogInfo("[LicenseOrchestrator] Monitoramento cancelado");
                }
                catch (Exception ex)
                {
                    App.LoggingService?.LogError("[LicenseOrchestrator] Erro no monitoramento de estado", ex);
                }
            });
        }

        private static LicenseType ParseLicenseType(string type)
        {
            var result = type?.ToLower() switch
            {
                "pro" => VoltrisOptimizer.Services.License.LicenseType.Pro,
                "standard" => VoltrisOptimizer.Services.License.LicenseType.Standard,
                "enterprise" => VoltrisOptimizer.Services.License.LicenseType.Enterprise,
                "trial" => VoltrisOptimizer.Services.License.LicenseType.Trial,
                "none" => VoltrisOptimizer.Services.License.LicenseType.None,
                _ => VoltrisOptimizer.Services.License.LicenseType.None
            };
            
            App.LoggingService?.LogDebug($"[LicenseOrchestrator] ParseLicenseType: '{type}' -> {result}");
            return result;
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Extrai apenas o nome da licença sem o prefixo "LICENÇA X ATIVA"
        /// </summary>
        /// <param name="licenseDisplayName">Nome completo da licença (ex: "LICENÇA STANDARD ATIVA")</param>
        /// <returns>Apenas o nome da licença (ex: "STANDARD")</returns>
        private static string GetCleanLicenseName(string? licenseDisplayName)
        {
            if (string.IsNullOrEmpty(licenseDisplayName))
                return "UNKNOWN";
            
            // Remover prefixos comuns: "LICENÇA", "LICENSE", etc.
            var cleanName = licenseDisplayName;
            
            // Padrões a remover
            var prefixesToRemove = new[] 
            {
                "LICENÇA ", "LICENSE ", "LICENCA ", 
                "LICENÇA", "LICENSE", "LICENCA"
            };
            
            var suffixesToRemove = new[] 
            {
                " ATIVA", " ACTIVE", " ATIVO", " ACTIVE"
            };
            
            // Remover prefixos
            foreach (var prefix in prefixesToRemove)
            {
                if (cleanName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    cleanName = cleanName.Substring(prefix.Length);
                    break;
                }
            }
            
            // Remover sufixos
            foreach (var suffix in suffixesToRemove)
            {
                if (cleanName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    cleanName = cleanName.Substring(0, cleanName.Length - suffix.Length);
                    break;
                }
            }
            
            // Limpar espaços extras
            cleanName = cleanName.Trim();
            
            // Se ainda estiver vazio, retornar o original limpo
            if (string.IsNullOrEmpty(cleanName))
            {
                // Tentar extrair a primeira palavra significativa
                var words = licenseDisplayName.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                return words.Length > 0 ? words[0] : "UNKNOWN";
            }
            
            return cleanName;
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            App.LoggingService?.LogDebug("[LicenseOrchestrator] Dispose chamado");
            _monitoringCts?.Cancel();
            _monitoringCts?.Dispose();
            _stateLock?.Dispose();
        }

        #endregion
    }
}
