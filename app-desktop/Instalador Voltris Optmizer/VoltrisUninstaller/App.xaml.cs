using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Collections.Generic;

namespace VoltrisUninstaller
{
    public partial class App : Application
    {
        public App()
        {
            // CRÍTICO: Criar recursos ANTES de qualquer XAML ser processado
            InitializeResources();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            // Garantir que recursos estejam disponíveis
            if (Resources == null)
            {
                Resources = new ResourceDictionary();
                InitializeResources();
            }

            // NÃO carregar recursos XAML - tudo criado programaticamente para evitar erros de StaticResource
            base.OnStartup(e);

            // Tratamento global de exceções
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        private void InitializeResources()
        {
            if (Resources == null)
            {
                Resources = new ResourceDictionary();
            }

            // Criar recursos fallback IMEDIATAMENTE
            CreateFallbackResources();

            // Garantir que Application.Current.Resources também esteja definido
            if (Current != null && Current.Resources != Resources)
            {
                Current.Resources = Resources;
            }
        }

        private void CreateFallbackResources()
        {
            // Cores base
            var darkColor = Color.FromRgb(23, 19, 19);
            var darkPanelColor = Color.FromRgb(28, 28, 30);
            var darkPanelAltColor = Color.FromRgb(42, 42, 46);
            var darkBorderColor = Color.FromRgb(58, 58, 62);
            var accentColor = Color.FromRgb(255, 75, 107);
            var secondaryColor = Color.FromRgb(139, 49, 255);
            var primaryColor = Color.FromRgb(49, 168, 255);
            var textPrimaryColor = Color.FromRgb(255, 255, 255);
            var textSecondaryColor = Color.FromRgb(224, 224, 224);

            // Brushes básicos
            Resources.Add("DarkBrush", new SolidColorBrush(darkColor));
            Resources.Add("DarkPanelBrush", new SolidColorBrush(darkPanelColor));
            Resources.Add("DarkPanelAltBrush", new SolidColorBrush(darkPanelAltColor));
            Resources.Add("DarkBorderBrush", new SolidColorBrush(darkBorderColor));
            Resources.Add("AccentBrush", new SolidColorBrush(accentColor));
            Resources.Add("SecondaryBrush", new SolidColorBrush(secondaryColor));
            Resources.Add("PrimaryBrush", new SolidColorBrush(primaryColor));
            Resources.Add("TextPrimaryBrush", new SolidColorBrush(textPrimaryColor));
            Resources.Add("TextSecondaryBrush", new SolidColorBrush(textSecondaryColor));

            // Brushes com blur
            var accentBlur = accentColor;
            accentBlur.A = 38;
            Resources.Add("AccentBlurBrush", new SolidColorBrush(accentBlur));

            var secondaryBlur = secondaryColor;
            secondaryBlur.A = 38;
            Resources.Add("SecondaryBlurBrush", new SolidColorBrush(secondaryBlur));

            // Gradiente Voltris
            var voltrisGradient = new LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0, 0),
                EndPoint = new System.Windows.Point(1, 1)
            };
            voltrisGradient.GradientStops.Add(new GradientStop(accentColor, 0));
            voltrisGradient.GradientStops.Add(new GradientStop(secondaryColor, 0.5));
            voltrisGradient.GradientStops.Add(new GradientStop(primaryColor, 1));
            Resources.Add("VoltrisGradientBrush", voltrisGradient);

            var voltrisGradientHorizontal = new LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0, 0),
                EndPoint = new System.Windows.Point(1, 0)
            };
            voltrisGradientHorizontal.GradientStops.Add(new GradientStop(accentColor, 0));
            voltrisGradientHorizontal.GradientStops.Add(new GradientStop(secondaryColor, 0.5));
            voltrisGradientHorizontal.GradientStops.Add(new GradientStop(primaryColor, 1));
            Resources.Add("VoltrisGradientHorizontalBrush", voltrisGradientHorizontal);

            // Gradiente Marca (Pink -> Purple)
            var brandGradient = new LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0, 0),
                EndPoint = new System.Windows.Point(1, 0)
            };
            brandGradient.GradientStops.Add(new GradientStop(accentColor, 0));
            brandGradient.GradientStops.Add(new GradientStop(secondaryColor, 1));
            Resources.Add("VoltrisBrandGradientBrush", brandGradient);

            // Estilos básicos
            CreateBasicStyles();
        }

        private void CreateBasicStyles()
        {
            // Estilo de botão moderno
            var buttonStyle = new Style(typeof(Button));
            buttonStyle.Setters.Add(new Setter(Button.BackgroundProperty, Resources["VoltrisGradientBrush"]));
            buttonStyle.Setters.Add(new Setter(Button.ForegroundProperty, Brushes.White));
            buttonStyle.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(0)));
            buttonStyle.Setters.Add(new Setter(Button.PaddingProperty, new Thickness(24, 12, 24, 12)));
            buttonStyle.Setters.Add(new Setter(Button.FontFamilyProperty, new FontFamily("Segoe UI")));
            buttonStyle.Setters.Add(new Setter(Button.FontSizeProperty, 14.0));
            buttonStyle.Setters.Add(new Setter(Button.FontWeightProperty, FontWeights.SemiBold));
            buttonStyle.Setters.Add(new Setter(Button.MinHeightProperty, 44.0));
            buttonStyle.Setters.Add(new Setter(Button.MinWidthProperty, 120.0));
            buttonStyle.Setters.Add(new Setter(Button.CursorProperty, System.Windows.Input.Cursors.Hand));

            var buttonTemplate = new ControlTemplate(typeof(Button));
            var buttonBorderFactory = new FrameworkElementFactory(typeof(Border));
            buttonBorderFactory.Name = "border";
            buttonBorderFactory.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            buttonBorderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            buttonBorderFactory.SetBinding(Border.PaddingProperty, new Binding("Padding") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });

            var dropShadow = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 0, Opacity = 0.5, Color = Color.FromRgb(49, 168, 255) };
            buttonBorderFactory.SetValue(Border.EffectProperty, dropShadow);

            var contentPresenterFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenterFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentPresenterFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            buttonBorderFactory.AppendChild(contentPresenterFactory);
            buttonTemplate.VisualTree = buttonBorderFactory;

            buttonStyle.Setters.Add(new Setter(Button.TemplateProperty, buttonTemplate));
            Resources.Add("ModernButtonStyle", buttonStyle);

            // Estilo de botão secundário
            var secondaryButtonStyle = new Style(typeof(Button));
            secondaryButtonStyle.Setters.Add(new Setter(Button.BackgroundProperty, Resources["DarkPanelAltBrush"]));
            secondaryButtonStyle.Setters.Add(new Setter(Button.ForegroundProperty, Resources["TextPrimaryBrush"]));
            secondaryButtonStyle.Setters.Add(new Setter(Button.BorderBrushProperty, Resources["DarkBorderBrush"]));
            secondaryButtonStyle.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(1)));
            secondaryButtonStyle.Setters.Add(new Setter(Button.PaddingProperty, new Thickness(24, 12, 24, 12)));
            secondaryButtonStyle.Setters.Add(new Setter(Button.FontFamilyProperty, new FontFamily("Segoe UI")));
            secondaryButtonStyle.Setters.Add(new Setter(Button.FontSizeProperty, 14.0));
            secondaryButtonStyle.Setters.Add(new Setter(Button.FontWeightProperty, FontWeights.SemiBold));
            secondaryButtonStyle.Setters.Add(new Setter(Button.MinHeightProperty, 44.0));
            secondaryButtonStyle.Setters.Add(new Setter(Button.MinWidthProperty, 100.0));
            secondaryButtonStyle.Setters.Add(new Setter(Button.CursorProperty, System.Windows.Input.Cursors.Hand));

            var secondaryButtonTemplate = new ControlTemplate(typeof(Button));
            var secondaryBorderFactory = new FrameworkElementFactory(typeof(Border));
            secondaryBorderFactory.Name = "border";
            secondaryBorderFactory.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            secondaryBorderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            secondaryBorderFactory.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            secondaryBorderFactory.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            secondaryBorderFactory.SetBinding(Border.PaddingProperty, new Binding("Padding") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });

            var secondaryContentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            secondaryContentFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            secondaryContentFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            secondaryBorderFactory.AppendChild(secondaryContentFactory);
            secondaryButtonTemplate.VisualTree = secondaryBorderFactory;

            var secondaryHoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            secondaryHoverTrigger.Setters.Add(new Setter(Button.BackgroundProperty, Resources["VoltrisGradientBrush"]));
            secondaryHoverTrigger.Setters.Add(new Setter(Button.ForegroundProperty, Brushes.White));
            secondaryHoverTrigger.Setters.Add(new Setter(Button.BorderBrushProperty, Brushes.Transparent));
            secondaryButtonTemplate.Triggers.Add(secondaryHoverTrigger);

            secondaryButtonStyle.Setters.Add(new Setter(Button.TemplateProperty, secondaryButtonTemplate));
            Resources.Add("SecondaryButtonStyle", secondaryButtonStyle);

            // Estilos de texto
            var titleStyle = new Style(typeof(TextBlock));
            titleStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, Resources["TextPrimaryBrush"]));
            titleStyle.Setters.Add(new Setter(TextBlock.FontFamilyProperty, new FontFamily("Segoe UI")));
            titleStyle.Setters.Add(new Setter(TextBlock.FontSizeProperty, 28.0));
            titleStyle.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.Bold));
            titleStyle.Setters.Add(new Setter(TextBlock.MarginProperty, new Thickness(0, 0, 0, 8)));
            Resources.Add("TitleTextStyle", titleStyle);

            var descStyle = new Style(typeof(TextBlock));
            descStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, Resources["TextSecondaryBrush"]));
            descStyle.Setters.Add(new Setter(TextBlock.FontFamilyProperty, new FontFamily("Segoe UI")));
            descStyle.Setters.Add(new Setter(TextBlock.FontSizeProperty, 14.0));
            descStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
            descStyle.Setters.Add(new Setter(TextBlock.MarginProperty, new Thickness(0, 0, 0, 24)));
            Resources.Add("DescriptionTextStyle", descStyle);

            var checkboxStyle = new Style(typeof(CheckBox));
            checkboxStyle.Setters.Add(new Setter(CheckBox.ForegroundProperty, Resources["TextPrimaryBrush"]));
            checkboxStyle.Setters.Add(new Setter(CheckBox.FontFamilyProperty, new FontFamily("Segoe UI")));
            checkboxStyle.Setters.Add(new Setter(CheckBox.FontSizeProperty, 13.0));
            checkboxStyle.Setters.Add(new Setter(CheckBox.MarginProperty, new Thickness(0, 8, 0, 8)));
            Resources.Add("ModernCheckBoxStyle", checkboxStyle);

            // Estilo de RadioButton
            var radioStyle = new Style(typeof(RadioButton));
            radioStyle.Setters.Add(new Setter(RadioButton.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            radioStyle.Setters.Add(new Setter(RadioButton.CursorProperty, System.Windows.Input.Cursors.Hand));
            var radioTemplate = new ControlTemplate(typeof(RadioButton));
            var radioGrid = new FrameworkElementFactory(typeof(Grid));
            radioGrid.SetValue(Grid.MarginProperty, new Thickness(0, 4, 0, 4));
            var col1 = new FrameworkElementFactory(typeof(ColumnDefinition));
            col1.SetValue(ColumnDefinition.WidthProperty, new GridLength(30));
            var col2 = new FrameworkElementFactory(typeof(ColumnDefinition));
            col2.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
            radioGrid.AppendChild(col1);
            radioGrid.AppendChild(col2);
            var outerCircle = new FrameworkElementFactory(typeof(Border));
            outerCircle.Name = "OuterCircle";
            outerCircle.SetValue(Border.WidthProperty, 20.0);
            outerCircle.SetValue(Border.HeightProperty, 20.0);
            outerCircle.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            outerCircle.SetValue(Border.BorderThicknessProperty, new Thickness(2));
            outerCircle.SetValue(Border.BorderBrushProperty, Resources["DarkBorderBrush"]);
            outerCircle.SetValue(Grid.ColumnProperty, 0);
            outerCircle.SetValue(Grid.VerticalAlignmentProperty, VerticalAlignment.Center);
            var innerCircle = new FrameworkElementFactory(typeof(Border));
            innerCircle.Name = "InnerCircle";
            innerCircle.SetValue(Border.WidthProperty, 10.0);
            innerCircle.SetValue(Border.HeightProperty, 10.0);
            innerCircle.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            innerCircle.SetValue(Border.BackgroundProperty, Resources["VoltrisBrandGradientBrush"]);
            innerCircle.SetValue(Border.OpacityProperty, 0.0);
            innerCircle.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            innerCircle.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            outerCircle.AppendChild(innerCircle);
            radioGrid.AppendChild(outerCircle);
            var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenter.SetValue(Grid.ColumnProperty, 1);
            contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            contentPresenter.SetValue(ContentPresenter.MarginProperty, new Thickness(8, 0, 0, 0));
            radioGrid.AppendChild(contentPresenter);
            radioTemplate.VisualTree = radioGrid;
            var checkedTrigger = new Trigger { Property = RadioButton.IsCheckedProperty, Value = true };
            checkedTrigger.Setters.Add(new Setter(Border.OpacityProperty, 1.0) { TargetName = "InnerCircle" });
            checkedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, Resources["AccentBrush"]) { TargetName = "OuterCircle" });
            radioTemplate.Triggers.Add(checkedTrigger);
            radioStyle.Setters.Add(new Setter(RadioButton.TemplateProperty, radioTemplate));
            Resources.Add("ModernRadioButtonStyle", radioStyle);

            CreateScrollBarStylesProgrammatically();
        }

        private void CreateScrollBarStylesProgrammatically()
        {
            var thumbStyle = new Style(typeof(Thumb));
            var thumbTemplate = new ControlTemplate(typeof(Thumb));
            var thumbBorderFactory = new FrameworkElementFactory(typeof(Border));
            thumbBorderFactory.Name = "ThumbBorder";
            thumbBorderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            thumbBorderFactory.SetValue(Border.OpacityProperty, 0.85);
            var thumbGradient = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(0, 1) };
            thumbGradient.GradientStops.Add(new GradientStop(Color.FromRgb(139, 49, 255), 0));
            thumbGradient.GradientStops.Add(new GradientStop(Color.FromRgb(255, 75, 107), 1));
            thumbBorderFactory.SetValue(Border.BackgroundProperty, thumbGradient);
            thumbTemplate.VisualTree = thumbBorderFactory;
            var thumbHoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            thumbHoverTrigger.Setters.Add(new Setter(Border.OpacityProperty, 1.0) { TargetName = "ThumbBorder" });
            thumbTemplate.Triggers.Add(thumbHoverTrigger);
            thumbStyle.Setters.Add(new Setter(Control.TemplateProperty, thumbTemplate));
            Resources.Add(typeof(Thumb), thumbStyle);

            var scrollBarStyle = new Style(typeof(ScrollBar));
            scrollBarStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            scrollBarStyle.Setters.Add(new Setter(FrameworkElement.WidthProperty, 8.0));
            var scrollBarTemplate = new ControlTemplate(typeof(ScrollBar));
            var scrollBarGridFactory = new FrameworkElementFactory(typeof(Grid));
            var trackBackgroundFactory = new FrameworkElementFactory(typeof(Border));
            trackBackgroundFactory.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(28, 28, 30)));
            trackBackgroundFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            trackBackgroundFactory.SetValue(Border.OpacityProperty, 0.15);
            scrollBarGridFactory.AppendChild(trackBackgroundFactory);
            var trackFactory = new FrameworkElementFactory(typeof(Track));
            trackFactory.Name = "PART_Track";
            trackFactory.SetValue(Track.IsDirectionReversedProperty, true);
            scrollBarGridFactory.AppendChild(trackFactory);
            scrollBarTemplate.VisualTree = scrollBarGridFactory;
            scrollBarStyle.Setters.Add(new Setter(Control.TemplateProperty, scrollBarTemplate));
            Resources.Add(typeof(ScrollBar), scrollBarStyle);
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            // WORKAROUND: Ignorar NullReferenceException disparado por bugs internos do WPF 
            // no InputManager quando AllowsTransparency="True".
            if (e.Exception is NullReferenceException && e.Exception.StackTrace != null &&
                (e.Exception.StackTrace.Contains("System.Windows.Input") || 
                 e.Exception.StackTrace.Contains("HwndMouseInputProvider") ||
                 e.Exception.StackTrace.Contains("HwndKeyboardInputProvider")))
            {
                e.Handled = true;
                return;
            }

            try
            {
                var logPath = Path.Combine(Path.GetTempPath(), "VoltrisUninstaller", "error.log");
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, $"{DateTime.UtcNow:O} Exception: {e.Exception.Message}\n{e.Exception.StackTrace}\n\n");
            }
            catch { }
            MessageBox.Show($"Erro: {e.Exception.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            MessageBox.Show($"Erro Crítico: {ex?.Message}", "Erro Crítico", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
