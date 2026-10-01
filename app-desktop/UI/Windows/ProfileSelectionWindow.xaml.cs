using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Notifications;
using VoltrisOptimizer.UI.Helpers;

namespace VoltrisOptimizer.UI.Windows
{
    public partial class ProfileSelectionWindow : Window
    {
        private readonly ObservableCollection<IntelligentProfileCatalog.ProfileOption> _options = new();
        private readonly DispatcherTimer _popupCloseTimer;
        private Button? _popupTarget;
        private bool _acrylicApplied;
        private bool _isClosing;

        public ProfileSelectionWindow()
        {
            InitializeComponent();

            _popupCloseTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(140)
            };
            _popupCloseTimer.Tick += PopupCloseTimer_Tick;

            ProfileItems.ItemsSource = _options;
            LoadOptions();

            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        }

        protected override void OnClosed(EventArgs e)
        {
            _isClosing = true;
            _popupCloseTimer.Stop();
            ProfileDetailsPopup.IsOpen = false;
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            base.OnClosed(e);
        }

        private void Window_SourceInitialized(object? sender, EventArgs e)
        {
            ApplyProfessionalAcrylic();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var workArea = SystemParameters.WorkArea;
                Width = Math.Min(Width, Math.Max(MinWidth, workArea.Width - 32));
                ApplyOsAwareCornerRadius();
                ApplyProfessionalAcrylic();
                ProfileItems.UpdateLayout();
            }
            catch (Exception ex)
            {
                LogException("Falha ao preparar o modal de perfis", ex);
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Close();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(LoadOptions));
        }

        private void LoadOptions()
        {
            try
            {
                var settings = SettingsService.Instance;
                var current = settings.Settings.IntelligentProfile;
                _options.Clear();

                foreach (var profile in IntelligentProfileCatalog.AllProfiles)
                {
                    var meta = IntelligentProfileCatalog.GetProfileMeta(profile);
                    var option = new IntelligentProfileCatalog.ProfileOption
                    {
                        Type = profile,
                        Glyph = IntelligentProfileCatalog.GetProfileGlyph(profile),
                        DisplayName = LocalizationService.Instance.GetString(meta.NameKey).Trim(),
                        GainText = LocalizationService.Instance.GetString(meta.GainKey).Trim(),
                        Description = LocalizationService.Instance.GetString(meta.DescKey).Trim(),
                        AccentBrush = IntelligentProfileCatalog.BrushFromHex(meta.Accent),
                        IsActive = profile == current
                    };

                    _options.Add(option);
                    if (option.IsActive)
                        CurrentProfileNameText.Text = option.DisplayName;
                }
            }
            catch (Exception ex)
            {
                LogException("Falha ao carregar os perfis", ex);
            }
        }

        private void CardOption_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not IntelligentProfileType profile)
                return;

            ApplyProfile(profile);
        }

        private void ProfileCard_MouseEnter(object sender, MouseEventArgs e)
        {
            ShowProfileDetails(sender as Button);
        }

        private void ProfileCard_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            ShowProfileDetails(sender as Button);
        }

        private void ProfileCard_MouseLeave(object sender, MouseEventArgs e)
        {
            if (ReferenceEquals(_popupTarget, sender))
                SchedulePopupClose();
        }

        private void ProfileCard_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (ReferenceEquals(_popupTarget, sender))
                SchedulePopupClose();
        }

        private void ShowProfileDetails(Button? button)
        {
            if (_isClosing || button?.DataContext is not IntelligentProfileCatalog.ProfileOption option)
                return;

            _popupCloseTimer.Stop();
            _popupTarget = button;
            ProfileDetailsPopup.PlacementTarget = button;
            ProfileDetailsPopup.DataContext = option;
            ProfileDetailsPopup.IsOpen = true;
        }

        private void ProfileDetailsPopup_MouseEnter(object sender, MouseEventArgs e)
        {
            _popupCloseTimer.Stop();
        }

        private void ProfileDetailsPopup_MouseLeave(object sender, MouseEventArgs e)
        {
            SchedulePopupClose();
        }

        private void ProfileDetailsPopup_Opened(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(PositionProfileDetails));
        }

        private void ProfileDetailsPopup_Closed(object sender, EventArgs e)
        {
            _popupTarget = null;
        }

        private void SchedulePopupClose()
        {
            if (_isClosing) return;
            _popupCloseTimer.Stop();
            _popupCloseTimer.Start();
        }

        private void PopupCloseTimer_Tick(object? sender, EventArgs e)
        {
            _popupCloseTimer.Stop();
            if (!_isClosing)
                ProfileDetailsPopup.IsOpen = false;
        }

        private void PositionProfileDetails()
        {
            if (_popupTarget == null || !ProfileDetailsPopup.IsOpen)
                return;

            var targetWidth = _popupTarget.ActualWidth;
            var popupWidth = ProfileDetailsContent.ActualWidth;
            if (targetWidth > 0 && popupWidth > 0)
                ProfileDetailsPopup.HorizontalOffset = (targetWidth - popupWidth) / 2d;

            ProfileDetailsPopup.VerticalOffset = -10;
        }

        private void ApplyOsAwareCornerRadius()
        {
            var radius = VoltrisOptimizer.Helpers.VisualEffectsManager.IsWindows11 ? 28d : 0d;
            ModalSurface.CornerRadius = new CornerRadius(radius);
            FooterBorder.CornerRadius = new CornerRadius(0, 0, radius, radius);
        }

        private void ApplyProfessionalAcrylic()
        {
            if (_acrylicApplied) return;

            try
            {
                var settings = SettingsService.Instance.Settings;
                if (settings.EnableTransparency)
                {
                    var isLight = settings.Theme?.Equals("Light", StringComparison.OrdinalIgnoreCase) == true;
                    BackdropHelper.ApplyModernBackdrop(
                        this,
                        BackdropHelper.SystemBackdropType.Acrylic,
                        isLight);
                    VoltrisOptimizer.Helpers.VisualEffectsManager.SetAcrylicState(true);
                }
                else
                {
                    BackdropHelper.RemoveBackdrop(this);
                    VoltrisOptimizer.Helpers.VisualEffectsManager.SetAcrylicState(false);
                }

                _acrylicApplied = true;
            }
            catch (Exception ex)
            {
                LogException("Falha ao aplicar o fundo Acrylic; usando fallback do tema", ex);
                ModalSurface.Background = Application.Current?.TryFindResource("WindowBackgroundBrush") as Brush
                    ?? new SolidColorBrush(Color.FromRgb(11, 15, 26));
            }
        }

        private void ApplyProfile(IntelligentProfileType profile)
        {
            try
            {
                var settings = SettingsService.Instance;
                var previous = settings.Settings.IntelligentProfile;
                if (previous == profile)
                {
                    Close();
                    return;
                }

                settings.Settings.IntelligentProfile = profile;
                settings.SaveSettings();
                settings.NotifyProfileChanged(profile);

                try
                {
                    var stabilizer = App.Services?.GetService(typeof(VoltrisOptimizer.Services.Optimization.IDynamicLoadStabilizer)) as VoltrisOptimizer.Services.Optimization.IDynamicLoadStabilizer;
                    stabilizer?.SetProfile(profile);
                }
                catch (Exception ex)
                {
                    LogException("Falha ao notificar o estabilizador dinâmico", ex);
                }

                try
                {
                    App.TelemetryService?.TrackEvent(
                        "DASHBOARD_PROFILE_CHANGE",
                        "Dashboard",
                        "Change",
                        metadata: new { Profile = profile.ToString() });
                }
                catch (Exception ex)
                {
                    LogException("Falha ao registrar a troca de perfil", ex);
                }

                try
                {
                    var title = LocalizationService.Instance.GetString("IntelligentProfile");
                    var message = string.Format(
                        LocalizationService.Instance.GetString("ProfileAppliedNotification"),
                        IntelligentProfileCatalog.GetLocalizedProfileName(profile));
                    GlobalNotificationService.ShowSuccess(title, message);
                }
                catch (Exception ex)
                {
                    LogException("Falha ao exibir a notificação de perfil", ex);
                }
            }
            catch (Exception ex)
            {
                LogException("Falha ao aplicar o perfil", ex);
            }
            finally
            {
                Close();
            }
        }

        private void LogException(string message, Exception exception)
        {
            App.LoggingService?.LogError($"[PROFILE_MODAL] {message}: {exception}", exception);
        }
    }
}
