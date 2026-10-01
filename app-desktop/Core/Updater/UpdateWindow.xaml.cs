using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Core.Updater
{
    public partial class UpdateWindow : Window
    {
        private readonly UpdateInfo _updateInfo;
        private bool _isDownloading = false;
        private string? _downloadedFilePath;
        
        public UpdateWindow(UpdateInfo updateInfo)
        {
            InitializeComponent();
            _updateInfo = updateInfo;
            
            // Configurar informações na UI
            VersionText.Text = $"v{UpdateService.GetCurrentVersion()} → v{updateInfo.LatestVersion}";
            
            // Formatar changelog para exibição limpa
            if (!string.IsNullOrEmpty(updateInfo.Changelog))
            {
                // Limpar e formatar changelog
                var lines = updateInfo.Changelog.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                var formattedLines = new System.Collections.Generic.List<string>();
                
                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();
                    if (string.IsNullOrWhiteSpace(trimmedLine)) continue;
                    
                    // Se já começa com bullet point, manter
                    if (trimmedLine.StartsWith("•") || trimmedLine.StartsWith("-") || trimmedLine.StartsWith("*"))
                    {
                        formattedLines.Add(trimmedLine.Replace("-", "•").Replace("*", "•"));
                    }
                    else
                    {
                        formattedLines.Add("• " + trimmedLine);
                    }
                }
                
                ChangelogText.Text = string.Join("\n", formattedLines);
            }
            
            // Permitir arrastar a janela
            MouseDown += Window_MouseDown;
            
            // Aplicar cantos arredondados
            VoltrisOptimizer.Helpers.RoundedWindowHelper.Apply(this, 20);
            
            // Animação de entrada
            Opacity = 0;
            Loaded += async (s, e) =>
            {
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500));
                BeginAnimation(OpacityProperty, fadeIn);
                
                // Aguardar 1 segundo e iniciar download automaticamente
                await Task.Delay(1000);
                StartDownloadProcess();
            };
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && e.LeftButton == MouseButtonState.Pressed)
            {
                try
                {
                    DragMove();
                }
                catch (InvalidOperationException)
                {
                    // Evitar crash se o botão for solto muito rapidamente ou estado inválido
                }
            }
        }

        private async void StartDownloadProcess()
        {
            if (_isDownloading) return;
            
            _isDownloading = true;
            ProgressPanel.Visibility = Visibility.Visible;
            UpdateButton.IsEnabled = false;
            UpdateButton.Content = LocalizationService.Instance.GetString("UpdateWinPreparing");
            
            try
            {
                var progress = new Progress<double>(percentage =>
                {
                    UpdateProgress(percentage);
                });
                
                _downloadedFilePath = await UpdateService.DownloadUpdateAsync(_updateInfo, progress);
                
                if (!string.IsNullOrEmpty(_downloadedFilePath))
                {
                    ProgressStatusText.Text = LocalizationService.Instance.GetString("UpdateWinReadyToApply");
                    UpdateProgress(100);
                    
                    UpdateButton.Content = LocalizationService.Instance.GetString("UpdateWinRestartFinish");
                    UpdateButton.IsEnabled = true;
                    

                }
                else
                {
                    ShowError(LocalizationService.Instance.GetString("UpdateWinDownloadFailed"));
                }
            }
            catch (Exception ex)
            {
                ShowError($"Erro: {ex.Message}");
            }
        }

        private void ShowError(string message)
        {
            ProgressStatusText.Text = message;
            ProgressStatusText.Foreground = System.Windows.Media.Brushes.Red;
            UpdateButton.Content = LocalizationService.Instance.GetString("UpdateWinTryAgain");
            UpdateButton.IsEnabled = true;
            _isDownloading = false;
        }

        private async void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_downloadedFilePath))
            {
                ApplyUpdate();
            }
            else
            {
                StartDownloadProcess();
            }
        }

        private async void ApplyUpdate()
        {
            try
            {
                if (!UpdateButton.IsEnabled) return;
                
                UpdateButton.IsEnabled = false;
                UpdateButton.Content = LocalizationService.Instance.GetString("UpdateWinFinalizing");
                ProgressStatusText.Text = LocalizationService.Instance.GetString("UpdateWinLaunchingCore");
                
                await Task.Delay(800);
                
                // Iniciar o VoltrisUpdater.exe
                // Passar o caminho do arquivo já baixado para evitar download duplicado
                bool success = await UpdateService.StartAutoUpdateAsync(_updateInfo, _downloadedFilePath);
                
                if (!success)
                {
                    ShowError(LocalizationService.Instance.GetString("UpdateWinDownloadFailed"));
                    return;
                }

                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                UpdateService.LogUpdate($"ERRO CRÍTICO NA TRANSIÇÃO: {ex.Message}");
                NotificationManager.ShowError("SaaS Update Engine", string.Format(LocalizationService.Instance.GetString("UpdateCriticalTransitionError"), ex.Message));
                UpdateButton.IsEnabled = true;
            }
        }

        private void UpdateProgress(double percentage)
        {
            Dispatcher.Invoke(() => {
                ProgressPercentText.Text = $"{percentage:F0}%";
                ProgressStatusText.Text = percentage < 100 ? LocalizationService.Instance.GetString("UpdateWinDownloadingResources") : LocalizationService.Instance.GetString("UpdateWinDownloadComplete");
                
                // Animação suave da barra progressiva
                double targetWidth = (percentage / 100.0) * (Width - 64); // Margem de 32 de cada lado
                if (targetWidth < 0) targetWidth = 0;

                var animation = new DoubleAnimation
                {
                    To = targetWidth,
                    Duration = TimeSpan.FromMilliseconds(400),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                ProgressBar.BeginAnimation(WidthProperty, animation);
            });
        }
    }

    // WindowRoundedCornersHelper removido — bordas arredondadas gerenciadas pelo
    // RoundedWindowHelper.Apply() chamado no construtor do UpdateWindow.
}
