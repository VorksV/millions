using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.UI.Converters
{
    /// <summary>
    /// Retorna um CornerRadius arredondado apenas no Windows 11 e quadrado (0) no
    /// Windows 10. O raio vem de ConverterParameter. O value de entrada é ignorado:
    /// use com Source={x:Static} apontando para VisualEffectsManager.
    /// </summary>
    public class OsCornerRadiusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double radius = 0d;
            LogInfo($"CORNER_CONVERT start; value={value ?? "null"} valueType={value?.GetType().FullName ?? "null"} targetType={targetType} parameter={parameter ?? "null"} parameterType={parameter?.GetType().FullName ?? "null"}");

            try
            {
                if (parameter != null)
                {
                    var text = parameter as string;
                    if (text != null)
                    {
                        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out radius))
                            radius = 0d;
                    }
                    else if (parameter is double d) radius = d;
                    else if (parameter is int i) radius = i;
                }

                LogInfo($"CORNER_CONVERT parsed; radiusParameter={radius}");
                bool isWindows11 = VisualEffectsManager.IsWindows11;
                var result = !isWindows11
                    ? new CornerRadius(0)
                    : new CornerRadius(radius);

                LogInfo($"CORNER_CONVERT success; windows11={isWindows11} result={result}");
                return result;
            }
            catch (Exception ex)
            {
                LogError("CORNER_CONVERT failed", ex);
                throw;
            }
        }

        private static void LogInfo(string message)
        {
            VoltrisOptimizer.App.LoggingService?.LogInfo($"[PROFILE_MODAL][CORNER] {message}");
        }

        private static void LogError(string message, Exception exception)
        {
            VoltrisOptimizer.App.LoggingService?.LogError(
                $"[PROFILE_MODAL][CORNER] {message}: {exception}",
                exception);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
