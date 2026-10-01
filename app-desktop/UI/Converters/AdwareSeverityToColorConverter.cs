using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using VoltrisOptimizer.Services.Shield;

namespace VoltrisOptimizer.UI.Converters
{
    public class AdwareSeverityToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is AdwareSeverity severity)
            {
                return severity switch
                {
                    AdwareSeverity.High => new SolidColorBrush(Color.FromRgb(255, 68, 102)),
                    AdwareSeverity.Medium => new SolidColorBrush(Color.FromRgb(255, 170, 0)),
                    _ => new SolidColorBrush(Color.FromRgb(255, 255, 136))
                };
            }
            
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
