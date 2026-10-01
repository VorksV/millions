using System;
using System.Globalization;
using System.Windows.Data;

namespace VoltrisOptimizer.UI.Converters
{
    /// <summary>
    /// Formata uma temperatura para exibição em widget/card, tratando ausência de sensor.
    ///
    /// POR QUE ESTE CONVERSOR EXISTE:
    /// O widget usava <c>StringFormat='{}{0,3:F0}°C'</c> diretamente sobre a
    /// propriedade <c>double CpuTemperature</c>. Com sensor ausente, o valor passa a
    /// ser <see cref="double.NaN"/> (nunca um número inventado), e <c>StringFormat</c>
    /// não sabe tratar isso: o resultado literal na tela era <c>"NaN°C"</c>.
    ///
    /// Aqui a ausência vira "N/D", e uma leitura real é formatada normalmente.
    /// O parâmetro opcional permite ajustar o número de casas decimais
    /// (padrão 0, como no widget original).
    /// </summary>
    public class TemperatureTextConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (!TryGetTemperature(value, out var temp))
                return "N/D";

            int decimals = 0;
            if (parameter != null)
            {
                var raw = parameter.ToString();
                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    decimals = Math.Clamp(parsed, 0, 2);
            }

            return string.Format(culture, "{0," + (decimals == 0 ? "3" : "4") + ":F" + decimals + "}°C", temp);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Só aceita leituras em faixa plausível. Sensor ausente chega como
        /// <see cref="double.NaN"/>; valores fora de 0–150 °C indicam leitura
        /// corrompida e são tratados como "não foi possível verificar".
        /// </summary>
        private static bool TryGetTemperature(object? value, out double temp)
        {
            temp = double.NaN;
            if (value is null) return false;

            double parsed;
            if (value is double d) parsed = d;
            else
            {
                try { parsed = System.Convert.ToDouble(value, CultureInfo.InvariantCulture); }
                catch { return false; }
            }

            if (double.IsNaN(parsed) || parsed <= 0 || parsed >= 150) return false;
            temp = parsed;
            return true;
        }
    }
}
