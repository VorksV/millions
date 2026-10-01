using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using VoltrisOptimizer.UI.ViewModels;
using VoltrisOptimizer.UI.Helpers;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.UI.Views
{
    /// <summary>
    /// Página de ativação/gerenciamento de licença (tela unificada)
    /// </summary>
    public partial class LicenseActivationView : UserControl
    {
        private readonly LicenseActivationViewModel _viewModel;
        private bool _isInitialized = false;

        public event EventHandler? ActivationSucceeded;

        public LicenseActivationView()
        {
            try
            {
                App.LoggingService?.LogInfo("[LicenseActivationView] ===== INÍCIO DO CONSTRUTOR (UserControl) =====");
                App.LoggingService?.LogInfo($"[LicenseActivationView] Thread ID: {Thread.CurrentThread.ManagedThreadId}");

                InitializeComponent();
                App.LoggingService?.LogInfo("[LicenseActivationView] InitializeComponent() concluído");

                App.LoggingService?.LogInfo("[LicenseActivationView] Criando novo LicenseActivationViewModel...");
                _viewModel = new LicenseActivationViewModel();
                App.LoggingService?.LogInfo("[LicenseActivationView] LicenseActivationViewModel criado com sucesso");

                App.LoggingService?.LogInfo("[LicenseActivationView] Configurando DataContext...");
                DataContext = _viewModel;
                App.LoggingService?.LogInfo("[LicenseActivationView] DataContext configurado");

                App.LoggingService?.LogInfo("[LicenseActivationView] Configurando eventos do ViewModel...");
                _viewModel.ActivationSucceeded += OnActivationSucceeded;
                _viewModel.LicenseStateChanged += OnLicenseStateChanged;
                App.LoggingService?.LogInfo("[LicenseActivationView] Eventos do ViewModel configurados");

                Loaded += async (s, e) =>
                {
                    if (_isInitialized) return;
                    _isInitialized = true;
                    
                    App.LoggingService?.LogInfo("[LicenseActivationView] ===== INÍCIO DO Loaded =====");
                    await InitializeAsync();
                    App.LoggingService?.LogInfo("[LicenseActivationView] ===== FIM DO Loaded =====");
                };

                App.LoggingService?.LogInfo("[LicenseActivationView] ===== FIM DO CONSTRUTOR =====");
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[LicenseActivationView] ERRO NO CONSTRUTOR", ex);
                throw;
            }
        }

        private async Task InitializeAsync()
        {
            try
            {
                App.LoggingService?.LogInfo("[LicenseActivationView] Iniciando InitializeAsync em background");

                // Inicializar o ViewModel
                App.LoggingService?.LogInfo("[LicenseActivationView] CHAMANDO _viewModel.InitializeAsync()...");
                await _viewModel.InitializeAsync();
                App.LoggingService?.LogInfo("[LicenseActivationView] _viewModel.InitializeAsync() CONCLUÍDO com sucesso");

                // Aplicar animação de entrada
                ApplyEntranceAnimation();

                // Focar no input de licença se não há licença ativa
                App.LoggingService?.LogInfo("[LicenseActivationView] Verificando se deve focar em input...");
                if (_viewModel.CanShowActivationForm && LicenseKeyInput != null)
                {
                    LicenseKeyInput.Focus();
                    App.LoggingService?.LogInfo("[LicenseActivationView] LicenseKeyInput.Focus() chamado com sucesso");
                }

                App.LoggingService?.LogInfo("[LicenseActivationView] InitializeAsync concluído com sucesso");
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[LicenseActivationView] Erro na inicialização", ex);
            }
        }

        /// <summary>
        /// Aplica animação de entrada (fade + slide)
        /// </summary>
        private void ApplyEntranceAnimation()
        {
            var fadeAnimation = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(400),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var slideAnimation = new ThicknessAnimation
            {
                From = new Thickness(0, 20, 0, 0),
                To = new Thickness(0),
                Duration = TimeSpan.FromMilliseconds(500),
                EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseOut }
            };

            BeginAnimation(OpacityProperty, fadeAnimation);
            BeginAnimation(MarginProperty, slideAnimation);
        }

        /// <summary>
        /// Monitora mudanças no estado da licença
        /// </summary>
        private void OnLicenseStateChanged(object? sender, EventArgs e)
        {
            App.LoggingService?.LogInfo($"[LicenseActivationView] LicenseStateChanged disparado - UiState: {_viewModel.UiState}");
            
            // Aplicar lógica de visibilidade/animação conforme necessário
            switch (_viewModel.UiState)
            {
                case LicenseUIState.Active:
                    App.LoggingService?.LogInfo("[LicenseActivationView] Estado mudou para ACTIVE");
                    ApplyTransitionAnimation();
                    break;
                    
                case LicenseUIState.NoLicense:
                    App.LoggingService?.LogInfo("[LicenseActivationView] Estado mudou para NO_LICENSE");
                    ApplyTransitionAnimation();
                    if (LicenseKeyInput != null)
                        LicenseKeyInput.Focus();
                    break;
                    
                case LicenseUIState.Changing:
                    App.LoggingService?.LogInfo("[LicenseActivationView] Estado mudou para CHANGING");
                    if (LicenseKeyInput != null)
                        LicenseKeyInput.Focus();
                    break;
                    
                case LicenseUIState.Error:
                    App.LoggingService?.LogError("[LicenseActivationView] Estado mudou para ERROR");
                    break;
            }
        }

        /// <summary>
        /// Aplica transição suave entre estados
        /// </summary>
        private void ApplyTransitionAnimation()
        {
            var fadeOut = new DoubleAnimation
            {
                From = 1,
                To = 0.5,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };

            var fadeIn = new DoubleAnimation
            {
                From = 0.5,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                BeginTime = TimeSpan.FromMilliseconds(200)
            };

            BeginAnimation(OpacityProperty, fadeOut);
            Task.Delay(200).ContinueWith(_ =>
            {
                Dispatcher.Invoke(() => BeginAnimation(OpacityProperty, fadeIn));
            });
        }

        /// <summary>
        /// Construtor com parâmetros de inicialização (para compatibilidade)
        /// </summary>
        public LicenseActivationView(bool trialExpired, int trialDaysRemaining) : this()
        {
            App.LoggingService?.LogInfo($"[LicenseActivationView] Construtor com parâmetros: trialExpired={trialExpired}, trialDaysRemaining={trialDaysRemaining}");

            if (trialExpired)
            {
                _viewModel.TrialDaysRemaining = 0;
                App.LoggingService?.LogInfo("[LicenseActivationView] Trial expirado - TrialDaysRemaining forçado para 0");
            }
            else if (trialDaysRemaining >= 0)
            {
                _viewModel.TrialDaysRemaining = trialDaysRemaining;
                App.LoggingService?.LogInfo($"[LicenseActivationView] TrialDaysRemaining definido: {trialDaysRemaining}");
            }
        }

        private void OnActivationSucceeded(object? sender, EventArgs e)
        {
            App.LoggingService?.LogInfo($"[LicenseActivationView] 🔥 OnActivationSucceeded INICIADO");

            try
            {
                // Disparar evento para notificar navegação se necessário
                ActivationSucceeded?.Invoke(this, EventArgs.Empty);
                App.LoggingService?.LogSuccess($"[LicenseActivationView] ✅ OnActivationSucceeded CONCLUÍDO com sucesso");
                
                // Aplicar transição para o novo estado
                ApplyTransitionAnimation();
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[LicenseActivationView] ❌ Erro ao processar sucesso da ativação", ex);
            }
        }

        private void BackToDashboard_Click(object sender, RoutedEventArgs e)
        {
            var mw = Window.GetWindow(this) as MainWindow;
            if (mw != null)
            {
                mw.NavigateToPageSafe("Dashboard");
            }
        }
    }
}