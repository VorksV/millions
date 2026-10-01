using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VoltrisOptimizer.UI.Converters
{
    public class ZeroToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int count)
            {
                var visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
                App.LoggingService?.LogTrace($"[CONVERTER] ZeroToVisibility: Valor={count} -> Visibilidade={visibility}");
                return visibility;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
