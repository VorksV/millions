using System;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.License;

namespace VoltrisOptimizer.Services.Shield
{
    /// <summary>
    /// Ponto único de decisão de licenciamento do Voltris Shield.
    ///
    /// REGRA: SOMENTE licença Standard, Pro ou Enterprise libera o Shield.
    /// Trial e usuário sem licença =&gt; Shield 100% offline.
    ///
    /// FAIL-CLOSED: qualquer erro na verificação resulta em bloqueio.
    /// Seguro para chamada na UI Thread: lê apenas caches síncronos em memória.
    /// </summary>
    public sealed class ShieldLicenseGate : IDisposable
    {
        /// <summary>Slug da feature, o mesmo usado por LicenseService/LicenseDialogService.</summary>
        public const string FeatureId = "shield";

        private readonly ILoggingService? _logger;
        private bool _watching;
        private bool _disposed;
        private bool _lastKnownState;

        /// <summary>Dispara quando o estado de licença muda (usado para desligar o Shield automaticamente).</summary>
        public event EventHandler<ShieldLicenseChangedEventArgs>? LicenseChanged;

        public ShieldLicenseGate(ILoggingService? logger)
        {
            _logger = logger;
            _lastKnownState = IsLicensed;
        }

        /// <summary>
        /// true se existe licença PAGA (Standard/Pro/Enterprise) ativa e não expirada.
        /// </summary>
        public bool IsLicensed
        {
            get
            {
                try
                {
                    // Fonte 1: LicenseTokenStore (valida expiração sozinho, sem I/O)
                    if (LicenseTokenStore.IsProActive)
                    {
                        return true;
                    }

                    // Fonte 2: cache síncrono do orquestrador (nunca usar GetAwaiter().GetResult aqui)
                    var cached = LicenseOrchestrationService.Instance.CachedState;
                    if (cached?.IsActive == true && IsPaidTier(cached.LicenseType))
                    {
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    // FAIL-CLOSED
                    _logger?.LogWarning($"[ShieldLicenseGate] Erro ao verificar licença — BLOQUEANDO: {ex.Message}");
                }

                return false;
            }
        }

        /// <summary>Tipo de licença atualmente detectado (para logs e UI).</summary>
        public string CurrentLicenseType
        {
            get
            {
                try
                {
                    var type = LicenseTokenStore.LicenseType;
                    if (!string.IsNullOrEmpty(type) && !type.Equals("None", StringComparison.OrdinalIgnoreCase))
                    {
                        return type;
                    }

                    var cached = LicenseOrchestrationService.Instance.CachedState;
                    if (!string.IsNullOrEmpty(cached?.LicenseType))
                    {
                        return cached!.LicenseType!;
                    }
                }
                catch { }

                return "None";
            }
        }

        /// <summary>
        /// Começa a observar mudanças de licença. Idempotente.
        /// </summary>
        public void StartWatching()
        {
            if (_watching || _disposed) return;
            try
            {
                LicenseTokenStore.IsProActiveChanged += OnLicenseStateChanged;
                LicenseManager.Instance.LicenseStatusChanged += OnLicenseStateChanged;
                _watching = true;
                _logger?.LogInfo($"[ShieldLicenseGate] Observando licença — estado inicial: licensed={IsLicensed}, tipo={CurrentLicenseType}");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[ShieldLicenseGate] Erro ao iniciar observação de licença", ex);
            }
        }

        /// <summary>
        /// Verifica e REGISTRA o resultado. Usar no topo de toda operação do Shield.
        /// </summary>
        public bool IsAllowed(string operation)
        {
            if (IsLicensed)
            {
                _logger?.LogInfo($"[ShieldLicenseGate] LIBERADO — {operation} (licença: {CurrentLicenseType})");
                return true;
            }

            _logger?.LogWarning(
                $"[ShieldLicenseGate] BLOQUEADO — {operation} | requer Standard/Pro/Enterprise | atual: {CurrentLicenseType}");
            return false;
        }

        /// <summary>Executa a operação apenas se licenciada; caso contrário registra o bloqueio e não executa.</summary>
        public async Task ExecuteAsync(string operation, Func<Task> action)
        {
            if (!IsAllowed(operation)) return;
            await action().ConfigureAwait(false);
        }

        /// <summary>Executa a operação apenas se licenciada; caso contrário registra o bloqueio e devolve o padrão.</summary>
        public async Task<T?> ExecuteAsync<T>(string operation, Func<Task<T>> action)
        {
            if (!IsAllowed(operation)) return default;
            return await action().ConfigureAwait(false);
        }

        private static bool IsPaidTier(string? licenseType)
        {
            var t = licenseType?.ToLowerInvariant();
            return t is "standard" or "pro" or "enterprise";
        }

        private void OnLicenseStateChanged(object? sender, EventArgs e)
        {
            bool licensed = IsLicensed;
            if (licensed == _lastKnownState) return;
            _lastKnownState = licensed;

            _logger?.LogInfo(
                $"[ShieldLicenseGate] Mudança de licença detectada — licensed={licensed}, tipo={CurrentLicenseType}");

            try
            {
                LicenseChanged?.Invoke(this, new ShieldLicenseChangedEventArgs(licensed, CurrentLicenseType));
            }
            catch (Exception ex)
            {
                _logger?.LogError("[ShieldLicenseGate] Erro ao notificar mudança de licença", ex);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_watching)
            {
                try
                {
                    LicenseTokenStore.IsProActiveChanged -= OnLicenseStateChanged;
                    LicenseManager.Instance.LicenseStatusChanged -= OnLicenseStateChanged;
                }
                catch { }
                _watching = false;
            }

            LicenseChanged = null;
        }
    }

    public sealed class ShieldLicenseChangedEventArgs : EventArgs
    {
        public bool IsLicensed { get; }
        public string LicenseType { get; }

        public ShieldLicenseChangedEventArgs(bool isLicensed, string licenseType)
        {
            IsLicensed = isLicensed;
            LicenseType = licenseType;
        }
    }
}
