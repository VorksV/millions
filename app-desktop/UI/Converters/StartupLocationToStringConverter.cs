using System;
using System.Globalization;
using System.Windows.Data;
using VoltrisOptimizer.Services.Shield;

namespace VoltrisOptimizer.UI.Converters
{
    public class StartupLocationToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is StartupLocation location)
            {
                return location switch
                {
                    StartupLocation.Registry => "Registry",
                    StartupLocation.StartupFolder => "Pasta de Inicialização",
                    _ => "Desconhecido"
                };
            }
            
            return "Desconhecido";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
