using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer
{
    /// <summary>
    /// Serviço especializado para bloquear notificações do sistema durante sessões de jogo
    /// Previne interrupções que causam travadas e stutter
    /// </summary>
    public class SystemNotificationBlockerService : IDisposable
    {
        private readonly ILoggingService _logger;
        private CancellationTokenSource? _blockingCts;
        private Task? _blockingTask;
        private int _gameProcessId;
        private readonly object _lock = new();
        
        // Estados originais para restauração
        private int _originalToastSetting = -1;
        private int _originalBannerSetting = -1;
        private int _originalSoundSetting = -1;
        private bool _originalFocusAssistState = false;
        
        public SystemNotificationBlockerService(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogEntry(nameof(SystemNotificationBlockerService));
            _logger.LogExit(nameof(SystemNotificationBlockerService));
        }
        
        /// <summary>
        /// Inicia bloqueio de notificações para um processo de jogo específico
        /// </summary>
        public void StartBlocking(int gameProcessId)
        {
            _logger.LogEntry(nameof(StartBlocking));
            StopBlocking();
            
            lock (_lock)
            {
                _gameProcessId = gameProcessId;
                _blockingCts = new CancellationTokenSource();
                _blockingTask = BlockNotifications(_blockingCts.Token);
                _logger.LogInfo($"[NotificationBlocker] Bloqueio de notificações iniciado para processo {gameProcessId}");
                _logger.LogExit(nameof(StartBlocking));
            }
        }
        
        /// <summary>
        /// Para bloqueio de notificações e restaura configurações originais
        /// </summary>
        public void StopBlocking()
        {
            _logger.LogEntry(nameof(StopBlocking));
            lock (_lock)
            {
                if (_blockingCts != null)
                {
                    _blockingCts.Cancel();
                    try { _blockingTask?.Wait(2000); } catch { }
                    _blockingCts.Dispose();
                    _blockingCts = null;
                }
                
                RestoreOriginalSettings();
                _logger.LogInfo("[NotificationBlocker] Bloqueio de notificações encerrado");
                _logger.LogExit(nameof(StopBlocking));
            }
        }
        
        /// <summary>
        /// Bloqueia notificações do sistema
        /// </summary>
        private async Task BlockNotifications(CancellationToken ct)
        {
            _logger.LogEntry(nameof(BlockNotifications));
            try
            {
                // Backup e desativação de notificações
                BackupAndDisableNotifications();
                
                // Manter bloqueio ativo enquanto o jogo roda
                while (!ct.IsCancellationRequested)
                {
                    // Verificar periodicamente e reforçar bloqueio se necessário
                    await Task.Delay(5000, ct);
                    ReinforceNotificationBlocking();
                }
            }
            catch (OperationCanceledException)
            {
                // Cancelamento esperado
            }
            catch (Exception ex)
            {
                _logger.LogError($"[NotificationBlocker] Erro no bloqueio: {ex.Message}");
            }
            _logger.LogExit(nameof(BlockNotifications));
        }
        
        /// <summary>
        /// Faz backup das configurações originais e desativa notificações
        /// </summary>
        private void BackupAndDisableNotifications()
        {
            _logger.LogEntry(nameof(BackupAndDisableNotifications));
            try
            {
                // 1. Desativar Toast Notifications
                using (var key = Registry.CurrentUser.CreateSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Notifications\Settings"))
                {
                    if (key != null)
                    {
                        // Backup do estado atual
                        var toastValue = key.GetValue("NOC_GLOBAL_SETTING_TOASTS_ENABLED");
                        _originalToastSetting = toastValue is int intValue ? intValue : 1;
                        
                        // Desativar todas as notificações toast
                        key.SetValue("NOC_GLOBAL_SETTING_TOASTS_ENABLED", 0, RegistryValueKind.DWord);
                    }
                }
                
                // 2. Desativar banners de notificação
                using (var key = Registry.CurrentUser.CreateSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\PushNotifications"))
                {
                    if (key != null)
                    {
                        var bannerValue = key.GetValue("ToastEnabled");
                        _originalBannerSetting = bannerValue is int intValue ? intValue : 1;
                        
                        key.SetValue("ToastEnabled", 0, RegistryValueKind.DWord);
                    }
                }
                
                // 3. Desativar sons de notificação
                using (var key = Registry.CurrentUser.CreateSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Notifications\Settings"))
                {
                    if (key != null)
                    {
                        var soundValue = key.GetValue("NOC_GLOBAL_SETTING_SOUND_ENABLED");
                        _originalSoundSetting = soundValue is int intValue ? intValue : 1;
                        
                        key.SetValue("NOC_GLOBAL_SETTING_SOUND_ENABLED", 0, RegistryValueKind.DWord);
                    }
                }
                
                // 4. Ativar Focus Assist (modo Não Perturbe)
                SetFocusAssist(true);
                
                // 5. Desativar notificações específicas do Chrome/Google
                DisableChromeNotifications();
                
                _logger.LogInfo("[NotificationBlocker] Notificações bloqueadas com sucesso");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[NotificationBlocker] Erro ao bloquear notificações: {ex.Message}");
            }
            _logger.LogExit(nameof(BackupAndDisableNotifications));
        }
        
        /// <summary>
        /// Desativa notificações específicas do Chrome/Google
        /// </summary>
        private void DisableChromeNotifications()
        {
            _logger.LogEntry(nameof(DisableChromeNotifications));
            try
            {
                // Caminhos comuns do Chrome
                var chromePaths = new[]
                {
                    @"SOFTWARE\Google\Chrome\PreferenceMACs",
                    @"SOFTWARE\Chromium\PreferenceMACs"
                };
                
                foreach (var basePath in chromePaths)
                {
                    using var key = Registry.CurrentUser.CreateSubKey(basePath);
                    if (key != null)
                    {
                        // Percorrer todos os perfis do Chrome
                        foreach (var profileName in key.GetSubKeyNames())
                        {
                            try
                            {
                                using var profileKey = key.CreateSubKey(profileName);
                                if (profileKey != null)
                                {
                                    // Desativar notificações do Chrome
                                    profileKey.SetValue("notification_enabled", 0, RegistryValueKind.DWord);
                                    profileKey.SetValue("notification_prompt_allowed", 0, RegistryValueKind.DWord);
                                }
                            }
                            catch
                            {
                                // Continuar com o próximo perfil
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[NotificationBlocker] Erro ao desativar notificações do Chrome: {ex.Message}");
            }
            _logger.LogExit(nameof(DisableChromeNotifications));
        }
        
        /// <summary>
        /// Reforça o bloqueio de notificações (chamado periodicamente)
        /// </summary>
        private void ReinforceNotificationBlocking()
        {
            _logger.LogEntry(nameof(ReinforceNotificationBlocking));
            try
            {
                // Verificar se as configurações ainda estão bloqueadas
                using (var key = Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Notifications\Settings"))
                {
                    if (key != null)
                    {
                        var currentValue = key.GetValue("NOC_GLOBAL_SETTING_TOASTS_ENABLED");
                        if (currentValue is int intValue && intValue != 0)
                        {
                            // Reaplicar bloqueio
                            key.SetValue("NOC_GLOBAL_SETTING_TOASTS_ENABLED", 0, RegistryValueKind.DWord);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[NotificationBlocker] Erro ao reforçar bloqueio: {ex.Message}");
            }
            _logger.LogExit(nameof(ReinforceNotificationBlocking));
        }

        private bool _focusAssistOriginalQueried;
        private readonly object _focusAssistLock = new();

        /// <summary>
        /// Define ou remove o modo Focus Assist (Não Perturbe)
        /// Usa Windows Runtime API quando disponível, sem PowerShell.
        /// </summary>
        private void SetFocusAssist(bool enable)
        {
            _logger.LogEntry(nameof(SetFocusAssist));
            lock (_focusAssistLock)
            {
                // Primeira chamada: armazenar estado ORIGINAL antes de qualquer alteração
                if (!_focusAssistOriginalQueried)
                {
                    _focusAssistOriginalQueried = true;
                    if (!TryQueryFocusAssistViaWinRt(out bool currentState))
                    {
                        // Não foi possível consultar — assumir false (desativado)
                        _originalFocusAssistState = false;
                    }
                    else
                    {
                        _originalFocusAssistState = currentState;
                    }
                }

                // Aplicar a mudança via WinRT API
                try
                {
                    bool applied = TrySetFocusAssistViaWinRt(enable);

                    if (applied)
                    {
                        _logger.LogInfo($"[NotificationBlocker] Focus Assist {(enable ? "ativado" : "desativado")} via WinRT API");
                    }
                    else
                    {
                        _logger.LogDebug("[NotificationBlocker] Focus Assist via registry apenas (toast suppression ativo)");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[NotificationBlocker] Erro ao configurar Focus Assist: {ex.Message}");
                }
            }
            _logger.LogExit(nameof(SetFocusAssist));
        }

        /// <summary>
        /// Tenta consultar o estado atual do Focus Assist via Windows Runtime API.
        /// </summary>
        private static bool TryQueryFocusAssistViaWinRt(out bool enabled)
        {
            enabled = false;
            try
            {
                // Windows 10 1809+ armazena configuração em Windows.System.QuietHours
                // ou via UserNotificationListener (leitura apenas)
                var notifType = Type.GetType(
                    "Windows.UI.Notifications.UserNotificationListener, Windows, ContentType=WindowsRuntime",
                    throwOnError: false);
                if (notifType != null)
                {
                    var currentMethod = notifType.GetMethod("get_Current", Type.EmptyTypes);
                    var listener = currentMethod?.Invoke(null, null);
                    if (listener != null)
                    {
                        var settingProp = listener.GetType().GetProperty("NotificationSetting");
                        if (settingProp != null)
                        {
                            var setting = settingProp.GetValue(listener);
                            int settingVal = (int)setting;
                            // 0 = Enabled, 1 = Disabled, 2 = PriorityOnly
                            enabled = settingVal == 2; // PriorityOnly ≈ Focus Assist ativo
                            return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Tenta ativar/desativar Focus Assist via Windows Runtime API.
        /// </summary>
        private static bool TrySetFocusAssistViaWinRt(bool enable)
        {
            try
            {
                // Tentativa 1: Windows.System.QuietHours (Windows 8.1+)
                var qhType = Type.GetType(
                    "Windows.System.QuietHours, Windows, ContentType=WindowsRuntime",
                    throwOnError: false);
                if (qhType != null)
                {
                    var userEnabledProp = qhType.GetProperty("UserEnabled");
                    if (userEnabledProp != null)
                    {
                        userEnabledProp.SetValue(null, enable);
                        return true;
                    }
                }

                // Tentativa 2: QuietHoursSettings (Windows 10 builds antigos)
                var qhsType = Type.GetType(
                    "Windows.System.QuietHours.QuietHoursSettings, Windows, ContentType=WindowsRuntime",
                    throwOnError: false);
                if (qhsType != null)
                {
                    var prop = qhsType.GetProperty("UserEnabled");
                    if (prop != null)
                    {
                        prop.SetValue(null, enable);
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }
        
        /// <summary>
        /// Restaura as configurações originais de notificação
        /// </summary>
        private void RestoreOriginalSettings()
        {
            _logger.LogEntry(nameof(RestoreOriginalSettings));
            try
            {
                // Restaurar Toast Notifications
                if (_originalToastSetting != -1)
                {
                    using var key = Registry.CurrentUser.CreateSubKey(
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Notifications\Settings");
                    key?.SetValue("NOC_GLOBAL_SETTING_TOASTS_ENABLED", _originalToastSetting, RegistryValueKind.DWord);
                }
                
                // Restaurar Banners
                if (_originalBannerSetting != -1)
                {
                    using var key = Registry.CurrentUser.CreateSubKey(
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\PushNotifications");
                    key?.SetValue("ToastEnabled", _originalBannerSetting, RegistryValueKind.DWord);
                }
                
                // Restaurar Sons
                if (_originalSoundSetting != -1)
                {
                    using var key = Registry.CurrentUser.CreateSubKey(
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Notifications\Settings");
                    key?.SetValue("NOC_GLOBAL_SETTING_SOUND_ENABLED", _originalSoundSetting, RegistryValueKind.DWord);
                }
                
                // Restaurar Focus Assist
                SetFocusAssist(_originalFocusAssistState);
                
                // Reativar notificações do Chrome
                ReenableChromeNotifications();
                
                _logger.LogInfo("[NotificationBlocker] Configurações originais restauradas");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[NotificationBlocker] Erro ao restaurar configurações: {ex.Message}");
            }
            _logger.LogExit(nameof(RestoreOriginalSettings));
        }
        
        /// <summary>
        /// Reativa notificações do Chrome/Google
        /// </summary>
        private void ReenableChromeNotifications()
        {
            _logger.LogEntry(nameof(ReenableChromeNotifications));
            try
            {
                var chromePaths = new[]
                {
                    @"SOFTWARE\Google\Chrome\PreferenceMACs",
                    @"SOFTWARE\Chromium\PreferenceMACs"
                };
                
                foreach (var basePath in chromePaths)
                {
                    using var key = Registry.CurrentUser.CreateSubKey(basePath);
                    if (key != null)
                    {
                        foreach (var profileName in key.GetSubKeyNames())
                        {
                            try
                            {
                                using var profileKey = key.CreateSubKey(profileName);
                                if (profileKey != null)
                                {
                                    // Reativar notificações (valores padrão)
                                    profileKey.DeleteValue("notification_enabled", false);
                                    profileKey.DeleteValue("notification_prompt_allowed", false);
                                }
                            }
                            catch
                            {
                                // Continuar com o próximo perfil
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[NotificationBlocker] Erro ao reativar notificações do Chrome: {ex.Message}");
            }
            _logger.LogExit(nameof(ReenableChromeNotifications));
        }
        
        public void Dispose()
        {
            _logger.LogEntry(nameof(Dispose));
            StopBlocking();
            _logger.LogExit(nameof(Dispose));
        }
    }
}
