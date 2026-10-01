using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VoltrisOptimizer.UI.Converters
{
    /// <summary>
    /// Converter para booleano para Visibility
    /// </summary>
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                bool invert = parameter?.ToString() == "Invert" || parameter?.ToString() == "Inverse";
                return (invert ? !boolValue : boolValue) ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
            {
                bool invert = parameter?.ToString() == "Invert";
                return invert ? visibility != Visibility.Visible : visibility == Visibility.Visible;
            }
            return false;
        }
    }
}
