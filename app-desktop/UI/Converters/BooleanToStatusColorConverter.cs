using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace VoltrisOptimizer.UI.Converters
{
    /// <summary>
    /// Converte um valor booleano para cor de status (Verde/Vermelho).
    /// </summary>
    public class BooleanToStatusColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
            {
                return b 
                    ? new SolidColorBrush(Color.FromRgb(16, 185, 129)) // Verde (#10B981)
                    : new SolidColorBrush(Color.FromRgb(239, 68, 68));  // Vermelho (#EF4444)
            }
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
