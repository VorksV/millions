using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.UI.Windows
{
    public partial class SplashWindow : Window
    {
        private const double ProgressBarMaxWidth = 400;
        private bool _allowClose = false;
        private double _lastProgressValue = -1;
        private DateTime _lastProgressTime = DateTime.MinValue;

        public SplashWindow()
        {
            App.LoggingService?.LogInfo("[SPLASH] Construtor chamado");

            // FIX: RoundedWindowHelper.Apply() moveu para o evento Loaded.
            // Motivo: no construtor (antes de InitializeComponent e da janela ser
            // mostrada), Apply → VisualEffectsManager.GetWindowHandle → EnsureHandle()
            // criava o HWND ANTES do XAML definir AllowsTransparency="True",
            // gerando: "Cannot change AllowsTransparency after a Window has been shown".
            // No Loaded o handle já existe e o W11 usa cantos nativos (DWMWCP_ROUND),
            // então o arredondamento é aplicado com segurança e sem perda visual.
            Loaded += (s, e) =>
            {
                try
                {
                    RoundedWindowHelper.Apply(this, 20);
                    App.LoggingService?.LogInfo("[SPLASH] RoundedWindowHelper.Apply aplicado no Loaded");
                }
                catch (Exception fx)
                {
                    App.LoggingService?.LogWarning($"[SPLASH] Não foi possível aplicar RoundedWindowHelper (não fatal): {fx.Message}");
                }
            };

            // Configurar manipulação de exceções não tratadas
            Dispatcher.UnhandledException += (s, e) => 
            { 
                App.LoggingService?.LogError($"[SPLASH] Dispatcher UnhandledException: {e.Exception}", e.Exception);
                e.Handled = true;
            };
            
            InitializeComponent();
            App.LoggingService?.LogInfo("[SPLASH] InitializeComponent concluído");
            
            // Flag para controlar se o fechamento é permitido
            _allowClose = false;
            
            // Adicionar evento de fechamento - PERMITIR fechamento quando autorizado
            Closing += (s, e) => 
            { 
                App.LoggingService?.LogInfo($"[SPLASH] Evento Closing disparado - _allowClose: {_allowClose}"); 
                
                // Só cancelar se ainda não foi autorizado o fechamento
                if (!_allowClose)
                {
                    App.LoggingService?.LogInfo("[SPLASH] Fechamento cancelado - ainda não autorizado");
                    e.Cancel = true;
                }
                else
                {
                    App.LoggingService?.LogInfo("[SPLASH] Fechamento permitido");
                }
            };
            
            Closed += (s, e) => 
            { 
                App.LoggingService?.LogInfo("[SPLASH] Evento Closed disparado - SplashWindow fechada com sucesso"); 
            };
            
            // Carregar imagem de forma assíncrona para evitar travamento
            Loaded += async (s, e) => 
            { 
                App.LoggingService?.LogInfo("[SPLASH] Evento Loaded disparado");
                await InitializeAsync(); 
                App.LoggingService?.LogInfo("[SPLASH] InitializeAsync concluído no evento Loaded");
            };
            
            // FIX: Failsafe aumentado de 15s para 45s para acomodar startups legítimos
            // mais lentos (verificação online de HWID, WMI lento, LibreHardwareMonitor).
            // O failsafe externo no App.xaml.cs também possui 45s, alinhados.
            var failsafe = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(45)
            };
            failsafe.Tick += (s, e) =>
            {
                failsafe.Stop();
                if (!_allowClose)
                {
                    App.LoggingService?.LogWarning("[SPLASH] ⏰ FAILSAFE: 45s sem AllowClose — forçando fechamento do splash.");
                    _allowClose = true;
                    try { Close(); } catch (Exception ex) { App.LoggingService?.LogError($"[SPLASH] Erro no failsafe Close: {ex.Message}", ex); }
                }
            };
            failsafe.Start();
            App.LoggingService?.LogInfo("[SPLASH] Construtor concluído");
        }

        private async Task InitializeAsync()
        {
            try
            {
                // Carregar logo e ajustar tamanho diretamente (já estamos na UI thread)
                LoadLogo();
                AdjustWindowSize();
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[SPLASH] Erro ao inicializar: {ex.Message}", ex);
            }
        }

        public void SetProgress(double value)
        {
            try
            {
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.BeginInvoke(() => SetProgressInternal(value));
                }
                else
                {
                    SetProgressInternal(value);
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[SplashWindow] SetProgress: {ex.Message}"); }
        }

        private void SetProgressInternal(double value)
        {
            var percentage = Math.Max(0, Math.Min(100, value));

            // Throttle: só atualiza se variou > 1% ou passou 500ms desde a última
            if (Math.Abs(percentage - _lastProgressValue) < 1.0 &&
                (DateTime.UtcNow - _lastProgressTime).TotalMilliseconds < 500)
                return;

            _lastProgressValue = percentage;
            _lastProgressTime = DateTime.UtcNow;

            var targetWidth = (ProgressBarMaxWidth / 100.0) * percentage;
            var currentWidth = ProgressFill.ActualWidth;
            var delta = Math.Abs(targetWidth - currentWidth);

            // Pula animação se delta for muito pequeno — seta direto
            if (delta < 1.0)
            {
                // Cancela a animação primeiro, depois seta o valor base final
                ProgressFill.BeginAnimation(WidthProperty, null);
                ProgressFill.Width = targetWidth;
                return;
            }

            // Atualiza o valor base para a largura atual, para que, ao limpar a animação, não volte a 0
            ProgressFill.Width = currentWidth;
            ProgressFill.BeginAnimation(WidthProperty, null);

            var durationMs = Math.Max(100, Math.Min(400, delta * 2));
            var animation = new DoubleAnimation
            {
                To = targetWidth,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            ProgressFill.BeginAnimation(WidthProperty, animation);
        }

        public void SetStatus(string text)
        {
            try
            {
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.BeginInvoke(() => StatusText.Text = text);
                }
                else
                {
                    StatusText.Text = text;
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[SplashWindow] SetStatus: {ex.Message}"); }
        }

        public async Task ShowForAsync(int milliseconds)
        {
            try
            {
                App.LoggingService?.LogInfo($"[SPLASH] ShowForAsync chamado com {milliseconds}ms");
                Show();
                App.LoggingService?.LogInfo("[SPLASH] Show() concluído");
                await Task.Delay(milliseconds);
                App.LoggingService?.LogInfo("[SPLASH] Task.Delay concluído");
                AllowClose();
                Close();
                App.LoggingService?.LogInfo("[SPLASH] Close() concluído");
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[SPLASH] Erro em ShowForAsync: {ex.Message}", ex);
            }
        }
        
        /// <summary>
        /// Permite que a janela seja fechada
        /// </summary>
        public void AllowClose()
        {
            _allowClose = true;
            App.LoggingService?.LogInfo("[SPLASH] AllowClose() chamado - fechamento agora permitido");
        }

        private void LoadLogo()
        {
            try 
            {
                App.LoggingService?.LogInfo("[SPLASH] Iniciando LoadLogo");
                
                // Verificar se LogoImage existe
                if (LogoImage == null)
                {
                    App.LoggingService?.LogWarning("[SPLASH] LogoImage é null");
                    return;
                }
                
                // Carregar diretamente do Recurso Embutido (Garantido pela build)
                var logoUri = new Uri("pack://application:,,/Images/logo.png", UriKind.Absolute);
                App.LoggingService?.LogInfo($"[SPLASH] URI criada: {logoUri}");
                
                var img = new BitmapImage();
                img.BeginInit();
                img.UriSource = logoUri;
                img.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                img.Freeze();
                
                LogoImage.Source = img;
                App.LoggingService?.LogInfo($"[SPLASH] Imagem carregada: {logoUri}");
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[SPLASH] Erro ao carregar logo: {ex.Message}");
                App.LoggingService?.LogError($"[SPLASH] StackTrace: {ex.StackTrace}", ex);
                
                // Tentar carregar uma imagem padrão ou deixar em branco
                try
                {
                    LogoImage.Source = null;
                    App.LoggingService?.LogInfo("[SPLASH] LogoImage definido como null");
                }
                catch (Exception ex2)
                {
                    App.LoggingService?.LogError($"[SPLASH] Erro ao definir LogoImage como null: {ex2.Message}", ex2);
                }
            }
        }

        private bool TryLoadImage(string path)
        {
            try
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.UriSource = new Uri(path, UriKind.Absolute);
                img.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                img.Freeze();
                LogoImage.Source = img;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryLoadImageFromUri(Uri uri)
        {
            try
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.UriSource = uri;
                img.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                img.Freeze();
                LogoImage.Source = img;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static int GetImageResolutionScoreSafe(string path)
        {
            try
            {
                using var stream = System.IO.File.OpenRead(path);
                var frame = BitmapFrame.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnLoad);
                return frame.PixelWidth * frame.PixelHeight;
            }
            catch { return 0; }
        }

        private static int GetNameBias(string path)
        {
            var name = System.IO.Path.GetFileName(path).ToLowerInvariant();
            int bias = 0;
            if (name.Contains("splash")) bias += 3;
            if (name.Contains("logo")) bias += 2;
            if (name.Contains("voltris")) bias += 1;
            if (name.Contains("@2x") || name.Contains("_2x") || name.Contains("hd")) bias += 2;
            if (name.Contains("@3x") || name.Contains("_3x") || name.Contains("4k")) bias += 3;
            return bias;
        }

        private void AdjustWindowSize()
        {
            try
            {
                var workArea = SystemParameters.WorkArea;
                App.LoggingService?.LogInfo($"[SPLASH] WorkArea: {workArea.Width}x{workArea.Height}");
                
                var shorterDimension = Math.Min(workArea.Width, workArea.Height);
                var targetHeight = Math.Max(340, Math.Min(450, shorterDimension * 0.35));

                Width = targetHeight * 1.55;
                Height = targetHeight;
                
                App.LoggingService?.LogInfo($"[SPLASH] Tamanho ajustado: {Width}x{Height}");

                Left = workArea.Left + (workArea.Width - Width) / 2;
                Top = workArea.Top + (workArea.Height - Height) / 2;
                
                App.LoggingService?.LogInfo($"[SPLASH] Posição: ({Left}, {Top})");
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[SPLASH] Erro em AdjustWindowSize: {ex.Message}", ex);
                // Usar valores padrão
                Width = 580;
                Height = 380;
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }
    }
}
