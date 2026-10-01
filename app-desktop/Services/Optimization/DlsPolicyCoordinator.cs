using System;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Gamer.Intelligence.Implementation;
using VoltrisOptimizer.Services.Optimization;

namespace VoltrisOptimizer.Services.Optimization
{
    /// <summary>
    /// Coordena o Dynamic Load Stabilizer de forma totalmente autonoma.
    /// O usuario nao interage: o perfil inteligente e o Brain definem o comportamento.
    ///
    /// Fluxo:
    ///  1. Boot: registra, aplica a politica e inicia o monitor
    ///  2. Perfil inteligente: sincroniza em tempo real (SetProfile)
    ///  3. Anti-cheat: suspende acoes intrusivas imediatamente
    ///  4. Politia: Auto (5s) / Conservative (15s) / Off
    ///
    /// Toda transicao gera log dedicado em [DLS-Policy].
    /// </summary>
    public sealed class DlsPolicyCoordinator : IAutoStartService, IDisposable
    {
        private readonly ILoggingService _logger;
        private readonly SettingsService _settings;
        private IDynamicLoadStabilizer? _dls;
        private System.Threading.Timer? _antiCheatTimer;
        private bool? _antiCheatActive;
        private bool _disposed;

        public DlsPolicyCoordinator(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _settings = SettingsService.Instance;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInfo("[DLS-Policy] ============================================================");
            _logger.LogInfo("[DLS-Policy] Inicializando coordenador autonomo do estabilizador");

            try
            {
                _dls = Core.ServiceLocator.GetService<IDynamicLoadStabilizer>();
                if (_dls == null)
                {
                    _logger.LogWarning("[DLS-Policy] IDynamicLoadStabilizer indisponivel - DLS nao sera iniciado.");
                    _logger.LogWarning("[DLS-Policy] Abortando: DLS nao sera iniciado.");
                    return;
                }

                _logger.LogInfo($"[DLS-Policy] Servico resolvido | Implementacao={_dls.GetType().Name}");

                _settings.ProfileChanged += OnProfileChanged;
                _settings.SettingsChanged += OnSettingsChanged;
                _logger.LogInfo("[DLS-Policy] Assinaturas registradas (ProfileChanged, SettingsChanged)");

                StartAntiCheatWatch();
                _logger.LogInfo("[DLS-Policy] Vigilante de anti-cheat iniciado (3s inicial, 10s periodo)");

                await ApplyPolicyAsync("boot");
                _logger.LogSuccess("[DLS-Policy] Coordenador pronto - usuario nao precisa intervir.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[DLS-Policy] Falha na inicializacao: {ex.Message}", ex);
                _logger.LogError("[DLS-Policy] Falha na inicializacao (ver acima).");
            }
        }

        private async Task ApplyPolicyAsync(string origin)
        {
            if (_dls == null) return;

            var policy = _settings.Settings.DlsPolicy;
            _settings.Settings.EnableDynamicLoadStabilizer = policy != DlsPolicy.Off;

            _logger.LogInfo($"[DLS-Policy] Aplicando politica '{policy}' (origem: {origin})");

            if (policy == DlsPolicy.Off)
            {
                bool wasRunning = _dls.IsRunning;
                _dls.Enabled = false;
                if (wasRunning) await _dls.StopAsync();
                _logger.LogInfo($"[DLS-Policy] DESLIGADO - estabilizador parado (estava rodando: {wasRunning})");
                return;
            }

            _dls.Enabled = true;
            _dls.SetProfile(_settings.Settings.IntelligentProfile);
            ApplyIntervalForPolicy(policy);

            if (!_dls.IsRunning)
            {
                await _dls.StartGlobalAsync();
                _logger.LogInfo("[DLS-Policy] Monitor global iniciado");
            }

            _logger.LogSuccess($"[DLS-Policy] ATIVO | Politica={policy} | Intervalo={_dls.Interval.TotalSeconds:F0}s | Perfil={_settings.Settings.IntelligentProfile} | AntiCheat={(_antiCheatActive ?? false)}");
        }

        /// <summary>
        /// SetProfile redefine o Intervalo conforme o perfil (3s em GamerCompetitivo, 8s no
        /// GeneralBalanced, etc). A politica Auto respeita esse ajuste do proprio DLS; a
        /// politica Conservador apenas impoe um PISO de 15s, sem rebaixar o tuning do motor.
        /// </summary>
        private void ApplyIntervalForPolicy(DlsPolicy policy)
        {
            if (_dls == null) return;
            if (policy != DlsPolicy.Conservative) return;
            if (_dls.Interval < TimeSpan.FromSeconds(15))
                _dls.Interval = TimeSpan.FromSeconds(15);
        }

        private void OnProfileChanged(object? sender, IntelligentProfileType profile)
        {
            try
            {
                _dls?.SetProfile(profile);
                if (_dls != null)
                {
                    ApplyIntervalForPolicy(_settings.Settings.DlsPolicy);
                    _logger?.LogInfo($"[DLS-Policy] PERFIL SINCRONIZADO automaticamente: {profile} | Intervalo={_dls.Interval.TotalSeconds:F0}s");
                }
                else
                {
                    _logger?.LogInfo($"[DLS-Policy] PERFIL SINCRONIZADO automaticamente: {profile}");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[DLS-Policy] Erro ao sincronizar perfil: {ex.Message}");
            }
        }

        private void OnSettingsChanged(object? sender, EventArgs e)
        {
            _ = ApplyPolicyAsync("settings-change");
        }

        private void StartAntiCheatWatch()
        {
            _antiCheatTimer = new System.Threading.Timer(_ =>
            {
                try
                {
                    // DetectRunningAntiCheat enumera TODOS os processos do sistema. Quando o
                    // DLS esta desligado (politica Off) ele nao reage a nada, entao a varredura
                    // e apenas desperdicio. Com o DLS ligado o comportamento e 100% igual.
                    bool dlsAtivo = _dls != null && _dls.Enabled == true;
                    if (!dlsAtivo)
                    {
                        if (_antiCheatActive == true)
                        {
                            _antiCheatActive = false;
                            _dls?.SetAntiCheatActive(false);
                        }
                        return;
                    }

                    var detected = AntiCheatCompatibilityService.Instance.DetectRunningAntiCheat();
                    bool active = !string.IsNullOrEmpty(detected);

                    if (_antiCheatActive == active) return;

                    _antiCheatActive = active;
                    _dls?.SetAntiCheatActive(active);
                    _logger?.LogInfo(active
                        ? $"[DLS-Policy] ANTI-CHEAT DETECTADO ({detected}) - DLS suspenso por seguranca."
                        : "[DLS-Policy] Anti-cheat liberado - DLS reativo.");
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning($"[DLS-Policy] Erro no vigilante de anti-cheat: {ex.Message}");
                }
            }, null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(10));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _antiCheatTimer?.Dispose();
            try
            {
                _settings.ProfileChanged -= OnProfileChanged;
                _settings.SettingsChanged -= OnSettingsChanged;
            }
            catch { }
        }
    }
}
