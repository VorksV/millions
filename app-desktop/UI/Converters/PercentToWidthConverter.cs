using System;
using System.Globalization;
using System.Windows.Data;

namespace VoltrisOptimizer.UI.Converters
{
    public class PercentToWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2) return 0d;
            if (values[0] is not double percent) return 0d;
            if (values[1] is not double containerWidth || double.IsNaN(containerWidth) || containerWidth <= 0) return 0d;
            double clamped = Math.Max(0, Math.Min(100, percent));
            return containerWidth * (clamped / 100.0);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
