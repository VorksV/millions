using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.UI.Views
{
    public partial class OnboardingView : UserControl
    {
        private int _currentStep = 0;
        private readonly OnboardingService _onboardingService;

        public event EventHandler? OnboardingCompleted;

        public OnboardingView()
        {
            InitializeComponent();
            _onboardingService = new OnboardingService();
            Loaded += OnboardingView_Loaded;
            Unloaded += OnboardingView_Unloaded;
        }

        private void OnboardingView_Loaded(object sender, RoutedEventArgs e)
        {
            // Desativar scroll do parent para garantir que o Viewbox funcione (fit-to-screen)
            var scrollViewer = FindParent<ScrollViewer>(this);
            if (scrollViewer != null)
            {
                scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            }
            
            ShowStep(0);
            
            // FIX: Forçar re-layout após carregamento para corrigir bug de renderização inicial
            // O bug ocorre porque o clipping não está ativo na primeira renderização
            Dispatcher.InvokeAsync(() =>
            {
                UpdateLayout();
                if (Application.Current?.MainWindow != null)
                {
                    Application.Current.MainWindow.InvalidateVisual();
                    Application.Current.MainWindow.UpdateLayout();
                }
            }, System.Windows.Threading.DispatcherPriority.Render);
            
            // Iniciar feedback visual na barra global
            try { GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("OnboardingInitialSetup")); } catch { }
        }

        private void OnboardingView_Unloaded(object sender, RoutedEventArgs e)
        {
            // Restaurar scroll quando sair desta tela
            var scrollViewer = FindParent<ScrollViewer>(this);
            if (scrollViewer != null)
            {
                scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            }
        }

        public static T? FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;
            if (parentObject is T parent) return parent;
            return FindParent<T>(parentObject);
        }

        private void ShowStep(int step)
        {
            _currentStep = step;

            // Atualizar indicadores de progresso
            UpdateProgressIndicators(step);
            
            // Atualizar barra de progresso global
            try 
            {
                int progress = (step + 1) * 20; // 0->20%, 1->40%, 2->60%, 3->80%
                GlobalProgressService.Instance.UpdateProgress(progress, string.Format(LocalizationService.Instance.GetString("OnboardingStepProgress"), step + 1));
            } 
            catch { }

            // Atualizar botões
            BackButton.Visibility = step > 0 ? Visibility.Visible : Visibility.Collapsed;
            NextButton.Content = step == 3 ? LocalizationService.Instance.GetString("OnboardingCompleteButton") + " ✓" : LocalizationService.Instance.GetString("OnboardingNextButton");

            // Limpar conteúdo anterior
            StepContentControl.Content = null;

            // Mostrar conteúdo do passo atual
            switch (step)
            {
                case 0:
                    ShowWelcomeStep();
                    break;
                case 1:
                    ShowFeaturesStep();
                    break;
                case 2:
                    ShowQuickStartStep();
                    break;
                case 3:
                    ShowFinalStep();
                    break;
            }
        }

        private Brush GetBrush(string key)
        {
            return (Brush)FindResource(key);
        }

        private void ShowWelcomeStep()
        {
            StepTitleText.Text = LocalizationService.Instance.GetString("OnboardingWelcomeTitle");
            StepDescriptionText.Text = LocalizationService.Instance["Loc_OSistemaProfissionalDeOtimizacaoParaWindows1011"];

            // Usar Grid principal para garantir alinhamento central correto
            var rootGrid = new Grid 
            { 
                Margin = new Thickness(0, 24, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Ícone
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Funcionalidades

            // Ícone central com gradiente
            var iconBorder = new Border
            {
                Width = 90,
                Height = 90,
                CornerRadius = new CornerRadius(24),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 42)
            };
            iconBorder.Background = CreateGradientBrush("#FF4B6B", "#8B31FF");
            iconBorder.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 30,
                ShadowDepth = 0,
                Color = (Color)ColorConverter.ConvertFromString("#8B31FF"),
                Opacity = 0.5
            };
            
            var boltIcon = CreateIconPath("BoltIcon", 48, Brushes.White);
            iconBorder.Child = boltIcon;
            
            Grid.SetRow(iconBorder, 0);
            rootGrid.Children.Add(iconBorder);

            // Container das funcionalidades usando Grid para alinhamento perfeito (Icon | Text)
            var featuresGrid = new Grid 
            { 
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 0)
            };
            
            // Colunas: Ícone (Auto) | Espaçamento (20) | Texto (*)
            featuresGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            featuresGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            featuresGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var features = new[]
            {
                ("CleanupIcon", LocalizationService.Instance.GetString("OnboardingCleanTemp"), "#10B981"),
                ("PerformanceIcon", LocalizationService.Instance.GetString("OnboardingOptimizePerf"), "#F59E0B"),
                ("NetworkIcon", LocalizationService.Instance.GetString("OnboardingNetworkImprove"), "#3B82F6"),
                ("HistoryIcon", LocalizationService.Instance.GetString("OnboardingHistoryTracking"), "#8B5CF6"),
                ("SchedulerIcon", LocalizationService.Instance.GetString("OnboardingAutoSchedule"), "#EC4899")
            };

            for (int i = 0; i < features.Length; i++)
            {
                var (iconKey, text, color) = features[i];
                var colorBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
                
                featuresGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                // Ícone com fundo colorido
                var emojiBorder = new Border
                {
                    Width = 42,
                    Height = 42,
                    CornerRadius = new CornerRadius(12),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)) { Opacity = 0.15 },
                    Margin = new Thickness(0, 0, 0, 18) // Espaçamento vertical aumentado
                };
                
                var iconPath = CreateIconPath(iconKey, 22, colorBrush);
                emojiBorder.Child = iconPath;
                
                Grid.SetRow(emojiBorder, i);
                Grid.SetColumn(emojiBorder, 0);
                featuresGrid.Children.Add(emojiBorder);
                
                // Texto
                var featureText = new TextBlock
                {
                    Text = text,
                    FontSize = 20, // Aumentado de 16 para 20
                    FontWeight = FontWeights.Medium,
                    Foreground = GetBrush("TextPrimaryBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 18) // Espaçamento vertical igual ao ícone
                };
                
                Grid.SetRow(featureText, i);
                Grid.SetColumn(featureText, 2);
                featuresGrid.Children.Add(featureText);
            }

            Grid.SetRow(featuresGrid, 1);
            rootGrid.Children.Add(featuresGrid);
            
            StepContentControl.Content = rootGrid;
        }

        private Path CreateIconPath(string resourceKey, double size, Brush fillBrush)
        {
            try 
            {
                var geometry = (Geometry)FindResource(resourceKey);
                return new Path
                {
                    Data = geometry,
                    Stretch = Stretch.Uniform,
                    Width = size,
                    Height = size,
                    Fill = fillBrush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
            catch
            {
                // Fallback caso a resource não exista
                return new Path();
            }
        }
        
        private LinearGradientBrush CreateGradientBrush(string color1, string color2)
        {
            return new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop((Color)ColorConverter.ConvertFromString(color1), 0),
                    new GradientStop((Color)ColorConverter.ConvertFromString(color2), 1)
                }
            };
        }

        private void ShowFeaturesStep()
        {
            StepTitleText.Text = LocalizationService.Instance.GetString("OnbKeyFeaturesTitle");
            StepDescriptionText.Text = LocalizationService.Instance["Loc_ConhecaAsFerramentasPoderosasASuaDisposicao"];

            var content = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };

            var features = new[]
            {
                ("HomeIcon", "Dashboard", "Visão geral completa do sistema", "#3B82F6", "#1D4ED8"),
                ("PerformanceIcon", "Desempenho", "Otimize velocidade e responsividade", "#F59E0B", "#D97706"),
                ("NetworkIcon", "Rede", "Melhore sua conexão de internet", "#06B6D4", "#0891B2"),
                ("SystemIcon", "Sistema", "Ferramentas avançadas de manutenção", "#6366F1", "#4F46E5"),
                ("GamerIcon", "Modo Gamer", "Otimizações específicas para jogos", "#EC4899", "#DB2777")
            };

            foreach (var (iconKey, title, desc, color1, color2) in features)
            {
                var featureCard = new Border
                {
                    Background = GetBrush("DarkPanelAltBrush"),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(16, 12, 16, 12),
                    Margin = new Thickness(0, 0, 0, 8),
                    BorderBrush = GetBrush("DarkBorderBrush"),
                    BorderThickness = new Thickness(1)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // Ícone com gradiente
                var iconBorder = new Border
                {
                    Width = 44,
                    Height = 44,
                    CornerRadius = new CornerRadius(12),
                    Background = CreateGradientBrush(color1, color2),
                    VerticalAlignment = VerticalAlignment.Center
                };
                iconBorder.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 12,
                    ShadowDepth = 0,
                    Color = (Color)ColorConverter.ConvertFromString(color1),
                    Opacity = 0.3
                };
                
                var iconPath = CreateIconPath(iconKey, 24, Brushes.White);
                iconBorder.Child = iconPath;
                
                Grid.SetColumn(iconBorder, 0);
                grid.Children.Add(iconBorder);

                var textPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
                textPanel.Children.Add(new TextBlock
                {
                    Text = title,
                    FontSize = 20, // Aumentado de 18
                    FontWeight = FontWeights.SemiBold,
                    Foreground = GetBrush("TextPrimaryBrush")
                });
                textPanel.Children.Add(new TextBlock
                {
                    Text = desc,
                    FontSize = 16, // Aumentado de 14
                    Foreground = GetBrush("TextSecondaryBrush"),
                    Margin = new Thickness(0, 3, 0, 0)
                });
                Grid.SetColumn(textPanel, 1);
                grid.Children.Add(textPanel);

                featureCard.Child = grid;
                content.Children.Add(featureCard);
            }

            StepContentControl.Content = content;
        }

        private void ShowQuickStartStep()
        {
            StepTitleText.Text = LocalizationService.Instance["Loc_InicioRapido"];
            StepDescriptionText.Text = LocalizationService.Instance["Loc_DicasEssenciaisParaComecarAUsar"];

            var content = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };

            var tips = new[]
            {
                ("1", LocalizationService.Instance.GetString("OnboardingStep1Title"), LocalizationService.Instance.GetString("OnboardingStep1Desc"), "#10B981", "#059669"),
                ("2", LocalizationService.Instance.GetString("OnboardingStep2Title"), LocalizationService.Instance.GetString("OnboardingStep2Desc"), "#3B82F6", "#1D4ED8"),
                ("3", LocalizationService.Instance.GetString("OnboardingStep3Title"), LocalizationService.Instance.GetString("OnboardingStep3Desc"), "#8B5CF6", "#6D28D9"),
                ("4", LocalizationService.Instance.GetString("OnboardingStep4Title"), LocalizationService.Instance.GetString("OnboardingStep4Desc"), "#EC4899", "#DB2777")
            };

            foreach (var (num, title, description, color1, color2) in tips)
            {
                var tipCard = new Border
                {
                    Background = GetBrush("DarkPanelAltBrush"),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(16, 16, 16, 16),
                    Margin = new Thickness(0, 0, 0, 14),
                    BorderBrush = GetBrush("DarkBorderBrush"),
                    BorderThickness = new Thickness(1)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // Número em círculo com gradiente
                var numBorder = new Border
                {
                    Width = 40,
                    Height = 40,
                    Background = CreateGradientBrush(color1, color2),
                    CornerRadius = new CornerRadius(20),
                    VerticalAlignment = VerticalAlignment.Top
                };
                numBorder.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 12,
                    ShadowDepth = 0,
                    Color = (Color)ColorConverter.ConvertFromString(color1),
                    Opacity = 0.3
                };
                var numText = new TextBlock
                {
                    Text = num,
                    FontSize = 20,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                numBorder.Child = numText;
                Grid.SetColumn(numBorder, 0);
                grid.Children.Add(numBorder);

                // Conteúdo
                var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                textStack.Children.Add(new TextBlock
                {
                    Text = title,
                    FontSize = 18,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = GetBrush("TextPrimaryBrush"),
                    TextWrapping = TextWrapping.Wrap
                });
                textStack.Children.Add(new TextBlock
                {
                    Text = description,
                    FontSize = 16,
                    Foreground = GetBrush("TextSecondaryBrush"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0)
                });
                Grid.SetColumn(textStack, 1);
                grid.Children.Add(textStack);

                tipCard.Child = grid;
                content.Children.Add(tipCard);
            }

            StepContentControl.Content = content;
        }

        private void ShowFinalStep()
        {
            StepTitleText.Text = LocalizationService.Instance.GetString("OnbAllReadyTitle");
            StepDescriptionText.Text = LocalizationService.Instance["Loc_CliqueEmConcluirParaIrAoQuestionarioDePerfil"];

            var content = new StackPanel 
            { 
                Margin = new Thickness(0, 24, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            // Ícone com gradiente
            var iconBorder = new Border
            {
                Width = 110,
                Height = 110,
                CornerRadius = new CornerRadius(28),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 32)
            };
            iconBorder.Background = CreateGradientBrush("#10B981", "#059669");
            iconBorder.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 30,
                ShadowDepth = 0,
                Color = (Color)ColorConverter.ConvertFromString("#10B981"),
                Opacity = 0.5
            };
            
            // Checkmark Path
            var checkPath = new Path
            {
                Data = Geometry.Parse("M9 16.17L4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41z"),
                Fill = Brushes.White,
                Stretch = Stretch.Uniform,
                Width = 64,
                Height = 64,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            iconBorder.Child = checkPath;
            
            content.Children.Add(iconBorder);

            var message = new TextBlock
            {
                Text = LocalizationService.Instance.GetString("OnboardingTutorialComplete"),
                FontSize = 22,
                Foreground = GetBrush("TextPrimaryBrush"),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 32),
                LineHeight = 30
            };
            content.Children.Add(message);

            // Card de próximos passos
            var nextStepsCard = new Border
            {
                Background = GetBrush("DarkPanelAltBrush"),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(24, 20, 24, 20),
                HorizontalAlignment = HorizontalAlignment.Center,
                MinWidth = 400,
                Margin = new Thickness(0, 0, 0, 8)
            };
            var nextStepsPanel = new StackPanel();
            nextStepsPanel.Children.Add(new TextBlock
            {
                Text = "📋 Próximos Passos:",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = GetBrush("TextPrimaryBrush"),
                Margin = new Thickness(0, 0, 0, 16)
            });
            
            var steps = new[]
            {
                ("1", "Responder questionário de perfil"),
                ("2", "Revisar recomendações inteligentes"),
                ("3", "Aplicar otimizações personalizadas")
            };
            foreach (var (num, step) in steps)
            {
                var stepPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
                
                // Bullet number style
                var numBorder = new Border
                {
                    Width = 24, Height = 24,
                    CornerRadius = new CornerRadius(12),
                    Background = GetBrush("PrimaryBrush"),
                    Margin = new Thickness(0, 0, 12, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                numBorder.Child = new TextBlock 
                { 
                    Text = num, 
                    Foreground = Brushes.White, 
                    FontSize = 14, 
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                
                stepPanel.Children.Add(numBorder);
                stepPanel.Children.Add(new TextBlock { Text = step, FontSize = 18, Foreground = GetBrush("TextSecondaryBrush"), VerticalAlignment = VerticalAlignment.Center });
                nextStepsPanel.Children.Add(stepPanel);
            }
            nextStepsCard.Child = nextStepsPanel;
            content.Children.Add(nextStepsCard);

            // Dica de atalho
            var tipBorder = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B31FF")) { Opacity = 0.1 },
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(20, 12, 20, 12),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            var tipText = new TextBlock
            {
                Text = "💡 Dica: Use Ctrl + 1-9 para navegação rápida",
                FontSize = 15,
                Foreground = GetBrush("TextMutedBrush"),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            tipBorder.Child = tipText;
            content.Children.Add(tipBorder);

            StepContentControl.Content = content;
        }
        
        private void GoToDashboardButton_Click(object sender, RoutedEventArgs e)
        {
            // Marcar onboarding como completo e disparar evento
            // Marcar onboarding como completo e disparar evento
            try { GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("OnboardingWelcomeDashboard")); } catch { }
            _onboardingService.MarkOnboardingComplete();
            OnboardingCompleted?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateProgressIndicators(int currentStep)
        {
            var activeBrush = GetBrush("PrimaryBrush");
            var inactiveBrush = GetBrush("DarkBorderBrush");

            Step1Indicator.Fill = currentStep >= 0 ? activeBrush : inactiveBrush;
            Step2Indicator.Fill = currentStep >= 1 ? activeBrush : inactiveBrush;
            Step3Indicator.Fill = currentStep >= 2 ? activeBrush : inactiveBrush;
            Step4Indicator.Fill = currentStep >= 3 ? activeBrush : inactiveBrush;
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep < 3)
            {
                ShowStep(_currentStep + 1);
            }
            else
            {
                CompleteOnboarding();
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep > 0)
            {
                ShowStep(_currentStep - 1);
            }
        }

        private void SkipButton_Click(object sender, RoutedEventArgs e)
        {
            CompleteOnboarding();
        }

        private void CompleteOnboarding()
        {
            // Finalizar a operação de tutorial e iniciar a de Perfil com 0%
            try 
            { 
                GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("OnboardingTutorialCompleted")); 
                GlobalProgressService.Instance.BeginOperation(LocalizationService.Instance.GetString("OnboardingUsageProfile"), isPriority: false);
                GlobalProgressService.Instance.UpdateProgress(5, LocalizationService.Instance.GetString("OnboardingStartingIntelligentProfile"));
            } 
            catch { }
            
            // Navegar para o Questionário de Perfil
            try
            {
                if (Application.Current.MainWindow is MainWindow mainWindow)
                {
                    var contentFrame = mainWindow.FindName("ContentFrame") as ContentControl;
                    if (contentFrame != null)
                    {
                        var questionnaireView = new Core.SystemIntelligenceProfiler.UI.Views.ProfilerQuestionnaireView();
                        contentFrame.Content = questionnaireView;
                        App.LoggingService?.LogInfo("[ONBOARDING] Navegando para Questionário de Perfil");
                    }
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[ONBOARDING] Erro ao navegar: {ex.Message}");
                // Fallback: marcar como completo e ir para Dashboard
                _onboardingService.MarkOnboardingComplete();
                OnboardingCompleted?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
