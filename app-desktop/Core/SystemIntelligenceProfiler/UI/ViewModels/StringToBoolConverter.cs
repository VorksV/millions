using System;
using System.Globalization;
using System.Windows.Data;

namespace VoltrisOptimizer.Core.SystemIntelligenceProfiler.UI.ViewModels;

public class StringToBoolConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		if (value == null || parameter == null)
		{
			return false;
		}
		return value.ToString() == parameter.ToString();
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
	{
		if (value != null && (bool)value)
		{
			return parameter.ToString() ?? string.Empty;
		}
		return Binding.DoNothing;
	}
}
