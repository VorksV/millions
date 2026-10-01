using System;
using System.Globalization;
using System.Windows.Data;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.UI.Converters
{
    public class BooleanToStatusTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var loc = LocalizationService.Instance;
            if (value is bool b)
                return b ? loc["StatusActive"] : loc["StatusInactive"];
            return loc["StatusUnknown"];
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
