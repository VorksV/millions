using System;
using System.Globalization;
using System.Windows.Data;

namespace VoltrisOptimizer.UI.Converters
{
    /// <summary>
    /// Converte o valor booleano IsWin32 para texto descritivo do tipo de aplicativo
    /// true = "Win32 (Desktop)", false = "UWP (Microsoft Store)"
    /// </summary>
    public class Win32ToTypeTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isWin32)
            {
                return isWin32 ? "Win32 (Desktop)" : "UWP (Microsoft Store)";
            }
            
            return "Desconhecido";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
