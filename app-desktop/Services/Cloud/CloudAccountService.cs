using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.Enterprise;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Cloud
{
    /// <summary>
    /// Estado local da conta vinculada.
    ///
    /// REGRA CRITICA: o estado local e um ESPELHO do servidor, nunca a fonte da
    /// verdade. `ConfirmLinkedFromServer` so pode ser chamado com um email que o
    /// backend confirmou. Escrever "vinculado" sem confirmacao do backend era a
    /// causa do sucesso falso.
    /// </summary>
    public sealed class CloudAccountService : INotifyPropertyChanged, IDisposable
    {
        private static readonly Lazy<CloudAccountService> _instance = new(() => new CloudAccountService());
        public static CloudAccountService Instance => _instance.Value;

        private bool _isLinked;
        private string? _linkedEmail;
        private bool _disposed;
        private readonly object _stateLock = new object();

        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<AccountStateChangedEventArgs>? AccountStateChanged;

        public bool IsLinked
        {
            get => _isLinked;
            private set
            {
                if (_isLinked != value)
                {
                    _isLinked = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(LinkedEmailDisplay));
                    OnPropertyChanged(nameof(IsNotLinked));
                }
            }
        }

        public string? LinkedEmail
        {
            get => _linkedEmail;
            private set
            {
                if (_linkedEmail != value)
                {
                    _linkedEmail = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(LinkedEmailDisplay));
                }
            }
        }

        public string LinkedEmailDisplay => IsLinked && !string.IsNullOrEmpty(LinkedEmail)
            ? LinkedEmail!
            : LocalizationService.Instance["DashboardLinkedEmailNone"];

        public bool IsNotLinked => !IsLinked;

        private CloudAccountService()
        {
            var settings = SettingsService.Instance.Settings;

            // Auto-fix para nome corrompido carregado de sessões antigas.
            // Antes o código salvava a string localizada "Usuário vinculado" como email.
            if (settings.LinkedUserEmail != null)
            {
                var corrupted = settings.LinkedUserEmail;
                if ((corrupted.StartsWith("usu", StringComparison.OrdinalIgnoreCase) && corrupted.EndsWith("rio", StringComparison.OrdinalIgnoreCase))
                    || corrupted.Equals("Linked user", StringComparison.OrdinalIgnoreCase)
                    || corrupted.Equals("Usuario vinculado", StringComparison.OrdinalIgnoreCase))
                {
                    settings.LinkedUserEmail = null;
                    SettingsService.Instance.SaveSettings();
                }
            }

            _isLinked = settings.IsDeviceLinked && !string.IsNullOrEmpty(settings.LinkedUserEmail);
            _linkedEmail = _isLinked ? settings.LinkedUserEmail : null;

            SettingsService.Instance.LinkingStatusChanged += OnSettingsLinkingStatusChanged;
        }

        /// <summary>
        /// Espelha localmente um vinculo JA CONFIRMADO pelo backend.
        /// </summary>
        public void ConfirmLinkedFromServer(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return;

            lock (_stateLock)
            {
                var settings = SettingsService.Instance.Settings;
                settings.LinkedUserEmail = email;
                settings.IsDeviceLinked = true;
                settings.LinkedAt ??= DateTime.UtcNow;
                settings.WelcomePromptShown = true;
                settings.IsFirstRun = false;
                SettingsService.Instance.SaveSettings();

                IsLinked = true;
                LinkedEmail = email;

                SettingsService.Instance.NotifyLinkingStatusChanged(true, email);
                AccountStateChanged?.Invoke(this, new AccountStateChangedEventArgs(true, email, AccountChangeType.Linked));
            }
        }

        /// <summary>
        /// Desvincula. So limpa o estado local se o backend confirmar a remocao
        /// (por credencial de dispositivo). Retorna false quando o servidor
        /// recusou ou ficou inacessivel — nesse caso o vinculo continua valendo
        /// e a UI deve mostrar o erro, nao "sucesso".
        /// </summary>
        public async Task<bool> UnlinkDeviceAsync()
        {
            var confirmed = await EnterpriseService.Instance.UnlinkThisDeviceAsync();

            if (!confirmed)
            {
                App.LoggingService?.LogWarning("[CLOUD] Desvinculacao nao confirmada pelo backend; estado local preservado.");
                return false;
            }

            lock (_stateLock)
            {
                var settings = SettingsService.Instance.Settings;
                settings.IsDeviceLinked = false;
                settings.LinkedUserEmail = null;
                settings.LinkedAt = null;
                SettingsService.Instance.SaveSettings();

                IsLinked = false;
                LinkedEmail = null;

                SettingsService.Instance.NotifyLinkingStatusChanged(false, null);
                AccountStateChanged?.Invoke(this, new AccountStateChangedEventArgs(false, null, AccountChangeType.Unlinked));
            }

            return true;
        }

        /// <summary>
        /// Replica o estado do servidor no espelho local.
        ///
        /// Só desloga quando o servidor responde CONCLUSIVAMENTE que não há
        /// vínculo. Em falha de rede (Unreachable) ou credencial divergente
        /// (CredentialMismatch) o estado local é preservado — o vínculo
        /// continua valendo no site.
        /// </summary>
        public async Task<LinkCheckOutcome> RefreshStatusAsync()
        {
            var result = await EnterpriseService.Instance.GetLinkStatusAsync();
            var settings = SettingsService.Instance.Settings;

            lock (_stateLock)
            {
                settings.LastLinkCheckAt = DateTime.UtcNow;

                if (result.Outcome == LinkCheckOutcome.Linked && !string.IsNullOrEmpty(result.Email))
                {
                    ConfirmLinkedFromServer(result.Email!);
                    return LinkCheckOutcome.Linked;
                }

                if (result.IsConclusiveNotLinked)
                {
                    settings.IsDeviceLinked = false;
                    settings.LinkedUserEmail = null;
                    settings.LinkedAt = null;
                    SettingsService.Instance.SaveSettings();

                    IsLinked = false;
                    LinkedEmail = null;
                    SettingsService.Instance.NotifyLinkingStatusChanged(false, null);
                    AccountStateChanged?.Invoke(this, new AccountStateChangedEventArgs(false, null, AccountChangeType.Unlinked));
                    return LinkCheckOutcome.NotLinked;
                }

                // Unreachable / NotRegistered / CredentialMismatch: nada muda.
                App.LoggingService?.LogDebug(
                    $"[CLOUD] Vinculo nao alterado (motivo: {result.Outcome}).");
                return result.Outcome;
            }
        }

        public void SyncFromSettings()
        {
            lock (_stateLock)
            {
                var settings = SettingsService.Instance.Settings;
                bool shouldBeLinked = settings.IsDeviceLinked && !string.IsNullOrEmpty(settings.LinkedUserEmail);

                IsLinked = shouldBeLinked;
                LinkedEmail = shouldBeLinked ? settings.LinkedUserEmail : null;
            }
        }

        private void OnSettingsLinkingStatusChanged(object? sender, (bool IsLinked, string? Email) args)
        {
            lock (_stateLock)
            {
                IsLinked = args.IsLinked;
                LinkedEmail = args.IsLinked ? args.Email : null;
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                SettingsService.Instance.LinkingStatusChanged -= OnSettingsLinkingStatusChanged;
                _disposed = true;
            }
        }
    }

    public enum AccountChangeType
    {
        Linked,
        Unlinked,
        Refreshed
    }

    public class AccountStateChangedEventArgs : EventArgs
    {
        public bool IsLinked { get; }
        public string? Email { get; }
        public AccountChangeType ChangeType { get; }

        public AccountStateChangedEventArgs(bool isLinked, string? email, AccountChangeType changeType)
        {
            IsLinked = isLinked;
            Email = email;
            ChangeType = changeType;
        }
    }
}
