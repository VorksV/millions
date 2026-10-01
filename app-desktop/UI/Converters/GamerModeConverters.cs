using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.UI.Converters
{
    /// <summary>
    /// Converte bool para cor de fundo (verde se ativo, cinza se inativo)
    /// </summary>
    public class BoolToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isActive && isActive)
            {
                return new LinearGradientBrush(
                    Color.FromRgb(16, 185, 129),  // #10B981
                    Color.FromRgb(5, 150, 105),   // #059669
                    new System.Windows.Point(0, 0),
                    new System.Windows.Point(1, 1)
                );
            }
            
            return new SolidColorBrush(Color.FromRgb(55, 65, 81)); // Gray-700
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converte bool para ícone de status
    /// </summary>
    public class BoolToStatusIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isActive && isActive)
            {
                return "✅"; // Ativo
            }
            
            return "⭕"; // Inativo
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converte bool para texto de status
    /// </summary>
    public class BoolToStatusTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isActive && isActive)
            {
                return LocalizationService.Instance.GetString("Active");
            }

            return LocalizationService.Instance.GetString("Inactive");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
