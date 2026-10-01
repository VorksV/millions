using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace VoltrisOptimizerInstaller
{
    public partial class App : Application
    {
        public App()
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) => LogFatalError(e.ExceptionObject as Exception);
            DispatcherUnhandledException += (s, e) =>
            {
                LogFatalError(e.Exception);
                e.Handled = true;
            };

            InitializeResources();
        }

        private void LogFatalError(Exception? ex)
        {
            string msg = ex?.ToString() ?? "Erro desconhecido";
            try
            {
                File.WriteAllText("startup_error.txt", msg);
            }
            catch { }
            MessageBox.Show("ERRO FATAL NO STARTUP:\n\n" + msg, "Voltris Optimizer Installer - Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            try
            {
                System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            }
            catch (Exception ex)
            {
                LogFatalError(ex);
                Shutdown();
            }
            base.OnStartup(e);
        }

        private void InitializeResources()
        {
            if (Resources == null)
                Resources = new ResourceDictionary();

            CreateBasicResources();
            // CreateBasicStyles removed - using XAML styles

            // Load shared UI styles (modern buttons, checkboxes, scrollbars, etc.)
            try
            {
                var styleDict = new ResourceDictionary { Source = new Uri("pack://application:,,/Themes/Styles.xaml", UriKind.Absolute) };
                // Merge into our resource dictionary
                Resources.MergedDictionaries.Add(styleDict);
            }
            catch { /* ignore if not found */ }

            if (Current != null && Current.Resources != null)
            {
                foreach (var key in Resources.Keys)
                {
                    if (!Current.Resources.Contains(key))
                        Current.Resources[key] = Resources[key];
                }
            }
        }

        private void CreateBasicResources()
        {
            var darkColor = Color.FromRgb(23, 19, 19);
            var darkPanelColor = Color.FromRgb(28, 28, 30);
            var darkPanelAltColor = Color.FromRgb(42, 42, 46);
            var darkBorderColor = Color.FromRgb(58, 58, 62);
            var accentColor = Color.FromRgb(255, 75, 107);
            var secondaryColor = Color.FromRgb(139, 49, 255);
            var primaryColor = Color.FromRgb(49, 168, 255);
            var textPrimaryColor = Color.FromRgb(255, 255, 255);
            var textSecondaryColor = Color.FromRgb(224, 224, 224);

            if (!Resources.Contains("DarkBrush"))
                Resources.Add("DarkBrush", new SolidColorBrush(darkColor));
            if (!Resources.Contains("DarkPanelBrush"))
                Resources.Add("DarkPanelBrush", new SolidColorBrush(darkPanelColor));
            if (!Resources.Contains("DarkPanelAltBrush"))
                Resources.Add("DarkPanelAltBrush", new SolidColorBrush(darkPanelAltColor));
            if (!Resources.Contains("DarkBorderBrush"))
                Resources.Add("DarkBorderBrush", new SolidColorBrush(darkBorderColor));
            if (!Resources.Contains("AccentBrush"))
                Resources.Add("AccentBrush", new SolidColorBrush(accentColor));
            if (!Resources.Contains("SecondaryBrush"))
                Resources.Add("SecondaryBrush", new SolidColorBrush(secondaryColor));
            if (!Resources.Contains("PrimaryBrush"))
                Resources.Add("PrimaryBrush", new SolidColorBrush(primaryColor));
            if (!Resources.Contains("TextPrimaryBrush"))
                Resources.Add("TextPrimaryBrush", new SolidColorBrush(textPrimaryColor));
            if (!Resources.Contains("TextSecondaryBrush"))
                Resources.Add("TextSecondaryBrush", new SolidColorBrush(textSecondaryColor));

            var windowBgColor = Color.FromArgb(128, 23, 19, 19);
            if (!Resources.Contains("WindowBackgroundBrush"))
                Resources.Add("WindowBackgroundBrush", new SolidColorBrush(windowBgColor));

            var accentBlur = accentColor;
            accentBlur.A = 38;
            if (!Resources.Contains("AccentBlurBrush"))
                Resources.Add("AccentBlurBrush", new SolidColorBrush(accentBlur));

            var secondaryBlur = secondaryColor;
            secondaryBlur.A = 38;
            if (!Resources.Contains("SecondaryBlurBrush"))
                Resources.Add("SecondaryBlurBrush", new SolidColorBrush(secondaryBlur));

            var voltrisGradient = new LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0, 0),
                EndPoint = new System.Windows.Point(1, 1)
            };
            voltrisGradient.GradientStops.Add(new GradientStop(accentColor, 0));
            voltrisGradient.GradientStops.Add(new GradientStop(secondaryColor, 0.5));
            voltrisGradient.GradientStops.Add(new GradientStop(primaryColor, 1));
            if (!Resources.Contains("VoltrisGradientBrush"))
                Resources.Add("VoltrisGradientBrush", voltrisGradient);

            var voltrisGradientHorizontal = new LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0, 0),
                EndPoint = new System.Windows.Point(1, 0)
            };
            voltrisGradientHorizontal.GradientStops.Add(new GradientStop(accentColor, 0));
            voltrisGradientHorizontal.GradientStops.Add(new GradientStop(secondaryColor, 0.5));
            voltrisGradientHorizontal.GradientStops.Add(new GradientStop(primaryColor, 1));
            if (!Resources.Contains("VoltrisGradientHorizontalBrush"))
                Resources.Add("VoltrisGradientHorizontalBrush", voltrisGradientHorizontal);
        }

        // Styles moved to Themes/Styles.xaml; method removed
    }
}
