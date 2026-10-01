using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using VoltrisOptimizer.Services.Drivers;

namespace VoltrisOptimizer.UI.Converters
{
    /// <summary>
    /// Traduz o estado real do driver (<see cref="DriverInstallState"/>) na cor da etiqueta.
    /// Substitui a abordagem anterior de DataTrigger sobre string traduzida, que quebrava
    /// silenciosamente quando o texto não coincidia com o gatilho.
    /// </summary>
    public sealed class DriverStateToBrushConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not DriverInstallState state) return BrushFor(DriverInstallState.Unknown);

            // Parâmetro permite inverter a paleta (fundo em vez de texto).
            bool isBackground = string.Equals(parameter as string, "Background", StringComparison.OrdinalIgnoreCase);
            return BrushFor(state, isBackground);
        }

        private static Brush BrushFor(DriverInstallState state, bool isBackground = false)
        {
            string hex = state switch
            {
                DriverInstallState.Installed => "#00E58F",
                DriverInstallState.UpToDate => "#31A8FF",
                DriverInstallState.UpdateAvailable => "#FFB020",
                DriverInstallState.Downloading => "#8B31FF",
                DriverInstallState.ReadyToInstall => "#8B31FF",
                DriverInstallState.Installing => "#8B31FF",
                DriverInstallState.Failed => "#FF4D5E",
                DriverInstallState.AttentionRequired => "#FF3D3D",
                DriverInstallState.ManualOnly => "#9AA7BD",
                _ => "#9AA7BD"
            };

            byte alpha = isBackground ? (byte)0x1A : (byte)0xFF;
            var color = (Color)ColorConverter.ConvertFromString(hex)!;
            color.A = alpha;
            return new SolidColorBrush(color);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }

    /// <summary>
    /// Controla a opacidade do botão de ação: visível sempre, apenas esmaecido quando
    /// desabilitado. Garante que o botão nunca "desapareça" por causa do estado.
    /// </summary>
    public sealed class BoolToDimConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is bool b && !b ? 0.45 : 1.0;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }

    /// <summary>
    /// Converte um valor double em Visibility usando um limiar. Útil para barras de progresso
    /// que só devem aparecer quando há progresso real.
    /// </summary>
    public sealed class ProgressToVisibilityConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool visible = value is double d && d > 0.001;
            if (string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase)) visible = !visible;
            return visible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }
}
