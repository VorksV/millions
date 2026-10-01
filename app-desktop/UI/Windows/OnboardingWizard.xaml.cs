using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using VoltrisOptimizer.Services;
using System.Windows.Controls.Primitives;

namespace VoltrisOptimizer.UI.Windows
{
    public partial class OnboardingWizard : Window
    {
        private int _currentStep;
        private int _scoreBefore;
        private int _scoreAfter;
        private readonly ILoggingService? _logger;

        public OnboardingWizard()
        {
            InitializeComponent();
            _logger = App.LoggingService;
            ShowStep(0);

            // Registrar evento para aplicar transparência do sistema Acrylic
            SourceInitialized += OnboardingWizard_SourceInitialized;
        }

        private void OnboardingWizard_SourceInitialized(object? sender, EventArgs e)
        {
            try
            {
                var settings = VoltrisOptimizer.Services.SettingsService.Instance.Settings;
                if (settings.EnableTransparency)
                {
                    bool isLight = settings.Theme?.Equals("Light", StringComparison.OrdinalIgnoreCase) == true;
                    VoltrisOptimizer.UI.Helpers.BackdropHelper.ApplyModernBackdrop(this, VoltrisOptimizer.UI.Helpers.BackdropHelper.SystemBackdropType.Acrylic, isLight);
                    _logger?.LogInfo("[OnboardingWizard] Backdrop de transparência Acrylic aplicado com sucesso");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError("[OnboardingWizard] Erro ao aplicar backdrop de transparência", ex);
            }
        }

        private void ShowStep(int step)
        {
            _currentStep = step;
            // Remover step 0 (análise neural diagnóstica) e step 1 (análise neural) - começar direto do step 2
            if (step == 0 || step == 1) step = 2;
            
            SkipButton.Visibility = step < 2 ? Visibility.Visible : Visibility.Collapsed;
            NextButton.Content = step < 2 ? LocalizationService.Instance.GetString("OnbNextBtn") : LocalizationService.Instance.GetString("OnbStartBtn");

            if (step == 2)
            {
                PulseRing.Visibility = Visibility.Collapsed;
                SuccessIndicator.Visibility = Visibility.Visible;
                try
                {
                    if (FindResource("SuccessPop") is Storyboard successPop)
                    {
                        successPop.Begin(this);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError("[OnboardingWizard] Erro ao iniciar animacao SuccessPop", ex);
                }
            }
            else
            {
                PulseRing.Visibility = Visibility.Visible;
                SuccessIndicator.Visibility = Visibility.Collapsed;
            }

            switch (step)
            {
                case 2: RenderResultStep(); break;
            }
        }

        private void RenderResultStep()
        {
            StepTitle.Text = LocalizationService.Instance.GetString("OnbStep2Title");
            StepDescription.Text = LocalizationService.Instance.GetString("OnbStep2Desc");

            var stack = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

            // 1. Neon Ritual Comparison Gauge
            var scoreGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            scoreGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            scoreGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            scoreGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var beforeBox = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(14, 255, 45, 85)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(50, 255, 45, 85)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16),
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = LocalizationService.Instance.GetString("OnbDiagLabel"), FontSize = 9, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)), HorizontalAlignment = HorizontalAlignment.Center, Typography = { Capitals = FontCapitals.AllSmallCaps } },
                        new TextBlock { Text = $"{_scoreBefore}/100", FontSize = 32, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x2D, 0x55)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) }
                    }
                }
            };
            Grid.SetColumn(beforeBox, 0);

            var arrow = new TextBlock
            {
                Text = "→",
                FontSize = 36,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("BrandGradient"),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(16, 0, 16, 0)
            };
            Grid.SetColumn(arrow, 1);

            var afterBox = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(16, 16, 185, 129)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 16, 185, 129)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16),
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = LocalizationService.Instance.GetString("OnbCalibLabel"), FontSize = 9, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)), HorizontalAlignment = HorizontalAlignment.Center, Typography = { Capitals = FontCapitals.AllSmallCaps } },
                        new TextBlock { Text = $"{_scoreAfter}/100", FontSize = 32, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) }
                    }
                }
            };
            Grid.SetColumn(afterBox, 2);

            scoreGrid.Children.Add(beforeBox);
            scoreGrid.Children.Add(arrow);
            scoreGrid.Children.Add(afterBox);
            stack.Children.Add(scoreGrid);

            // Dynamic gain pill container
            int delta = _scoreAfter - _scoreBefore;
            if (delta > 0)
            {
                var gainBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(20, 16, 185, 129)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(14),
                    Padding = new Thickness(16, 6, 16, 6),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 12)
                };
                gainBorder.Child = new TextBlock
                {
                    Text = string.Format(LocalizationService.Instance.GetString("OnbPerformanceGain"), delta),
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81))
                };
                stack.Children.Add(gainBorder);
            }

            // 2. Calibrated Details Items
            var detailsContainer = new StackPanel();
            
            // Display calibration results
            detailsContainer.Children.Add(MakeResultItem(LocalizationService.Instance.GetString("OnbResultLatency"), "Diagnóstico concluído", "#00E5FF"));
            detailsContainer.Children.Add(MakeResultItem(LocalizationService.Instance.GetString("OnbResultPower"), "Sistema pronto para uso", "#8B31FF"));

            stack.Children.Add(detailsContainer);

            StepContent.Content = stack;
            NextButton.Content = LocalizationService.Instance.GetString("OnbStartBtn");
        }


        private static Border MakeResultItem(string title, string value, string accentColor)
        {
            var item = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(10, 255, 255, 255)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 8, 14, 8),
                Margin = new Thickness(0, 4, 0, 4)
            };

            var layout = new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleBlock = new TextBlock
            {
                Text = title.ToUpper(),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(titleBlock, 0);
            layout.Children.Add(titleBlock);

            var valueBlock = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(25, 255, 255, 255)),
                BorderBrush = (Brush)new BrushConverter().ConvertFromString(accentColor)!,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 4, 10, 4),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = value,
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = (Brush)new BrushConverter().ConvertFromString(accentColor)!,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Grid.SetColumn(valueBlock, 1);
            layout.Children.Add(valueBlock);

            item.Child = layout;
            return item;
        }

        private async void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep < 2)
            {
                ShowStep(_currentStep + 1);
            }
            else
            {
                try { DialogResult = true; } catch { }
                Close();
            }
        }

        private void SkipButton_Click(object sender, RoutedEventArgs e)
        {
            try { DialogResult = false; } catch { }
            Close();
        }
    }
}
