using System;
using System.Windows;
using System.Windows.Controls;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.License;

namespace VoltrisOptimizer.UI.Controls
{
    /// <summary>
    /// Selo PRO reutilizável (pill dourada) para botões premium.
    /// Uso: <controls:ProBadge/> dentro do conteúdo do botão (canto direito).
    /// </summary>
    public partial class ProBadge : UserControl
    {
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register(
                nameof(Text),
                typeof(string),
                typeof(ProBadge),
                new PropertyMetadata("PRO", OnTextChanged));

        public static readonly DependencyProperty TipProperty =
            DependencyProperty.Register(
                nameof(Tip),
                typeof(string),
                typeof(ProBadge),
                new PropertyMetadata(null, OnTipChanged));

        /// <summary>Texto exibido no selo (padrão: PRO).</summary>
        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        /// <summary>Dica (tooltip) exibida ao passar o mouse (pode ser localizada).</summary>
        public string Tip
        {
            get => (string)GetValue(TipProperty);
            set => SetValue(TipProperty, value);
        }

        public ProBadge()
        {
            InitializeComponent();
            Loaded += ProBadge_Loaded;
            Unloaded += ProBadge_Unloaded;
        }

        private void ProBadge_Loaded(object sender, RoutedEventArgs e)
        {
            LicenseTokenStore.IsProActiveChanged += OnLicenseStateChanged;
            LicenseManager.Instance.LicenseStatusChanged += OnLicenseStateChanged;
            RefreshVisibility();
        }

        private void ProBadge_Unloaded(object sender, RoutedEventArgs e)
        {
            LicenseTokenStore.IsProActiveChanged -= OnLicenseStateChanged;
            LicenseManager.Instance.LicenseStatusChanged -= OnLicenseStateChanged;
        }

        private void OnLicenseStateChanged(object? sender, EventArgs e)
        {
            if (Dispatcher.CheckAccess())
            {
                RefreshVisibility();
            }
            else
            {
                Dispatcher.BeginInvoke(new Action(RefreshVisibility));
            }
        }

        private void RefreshVisibility()
        {
            // O selo PRO é para usuários gratuitos (exigindo licença para comprar/ativar).
            // Quando a licença estiver ativa, o selo é oculto (Collapsed).
            // Quando desativada/gratuita, o selo DEVE aparecer imediatamente (Visible).
            SetCurrentValue(VisibilityProperty, LicenseTokenStore.IsProActive ? Visibility.Collapsed : Visibility.Visible);
        }

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ProBadge badge && badge.BadgeText != null)
                badge.BadgeText.Text = (string)(e.NewValue ?? "PRO");
        }

        private static void OnTipChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ProBadge badge)
                badge.ToolTip = string.IsNullOrWhiteSpace(e.NewValue as string) ? null : e.NewValue as string;
        }
    }
}
