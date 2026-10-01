using System;
using System.Globalization;
using System.Windows.Data;

namespace VoltrisOptimizer.UI.Converters
{
    /// <summary>
    /// Converte um valor booleano para o seu inverso.
    /// Usado principalmente em bindings XAML para visibilidade ou estado de botões.
    /// </summary>
    public class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
            {
                return !b;
            }
            return value; // Retorna o valor original se não for booleano
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
            {
                return !b;
            }
            return value; // Retorna o valor original se não for booleano
        }
    }
}
