using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace VoltrisOptimizer.UI.Converters
{
    public class ThermalIndicatorConverter : IMultiValueConverter
    {
        private static readonly Color ColorUnknown = Color.FromRgb(107, 114, 128);
        private static readonly Color ColorCritical = Color.FromRgb(255, 59, 48);
        private static readonly Color ColorHigh = Color.FromRgb(239, 68, 68);
        private static readonly Color ColorWarm = Color.FromRgb(251, 146, 60);
        private static readonly Color ColorMild = Color.FromRgb(245, 158, 11);
        private static readonly Color ColorGood = Color.FromRgb(16, 185, 129);

        private static readonly SolidColorBrush BrushUnknown = Frozen(ColorUnknown);
        private static readonly SolidColorBrush BrushCritical = Frozen(ColorCritical);
        private static readonly SolidColorBrush BrushHigh = Frozen(ColorHigh);
        private static readonly SolidColorBrush BrushWarm = Frozen(ColorWarm);
        private static readonly SolidColorBrush BrushMild = Frozen(ColorMild);
        private static readonly SolidColorBrush BrushGood = Frozen(ColorGood);

        private static readonly SolidColorBrush SoftUnknown = FrozenSoft(ColorUnknown);
        private static readonly SolidColorBrush SoftCritical = FrozenSoft(ColorCritical);
        private static readonly SolidColorBrush SoftHigh = FrozenSoft(ColorHigh);
        private static readonly SolidColorBrush SoftWarm = FrozenSoft(ColorWarm);
        private static readonly SolidColorBrush SoftMild = FrozenSoft(ColorMild);
        private static readonly SolidColorBrush SoftGood = FrozenSoft(ColorGood);

        private static SolidColorBrush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private static SolidColorBrush FrozenSoft(Color c)
        {
            var b = new SolidColorBrush(Color.FromArgb(90, c.R, c.G, c.B));
            b.Freeze();
            return b;
        }

        private static SolidColorBrush BrushFor(Color c)
        {
            if (c == ColorUnknown) return BrushUnknown;
            if (c == ColorCritical) return BrushCritical;
            if (c == ColorHigh) return BrushHigh;
            if (c == ColorWarm) return BrushWarm;
            if (c == ColorMild) return BrushMild;
            return BrushGood;
        }

        private static SolidColorBrush SoftBrushFor(Color c)
        {
            if (c == ColorUnknown) return SoftUnknown;
            if (c == ColorCritical) return SoftCritical;
            if (c == ColorHigh) return SoftHigh;
            if (c == ColorWarm) return SoftWarm;
            if (c == ColorMild) return SoftMild;
            return SoftGood;
        }

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // Os cards passam o MESMO valor duas vezes (CPU no cartão de CPU, GPU no
            // cartão de GPU); o widget pode passar as duas. Ler cada slot e decidir a
            // disponibilidade individualmente é o que permite que a CPU continua
            // aparecendo quando a GPU não tem sensor — e vice-versa.
            double cpu = ToTemp(values.Length > 0 ? values[0] : null);
            double gpu = ToTemp(values.Length > 1 ? values[1] : null);

            // Ambos os slots com a mesma fonte: trata como um único sensor para não
            // gerar "CPU 45°  GPU 45°" no cartão de CPU.
            bool singleSource = !double.IsNaN(cpu) && Math.Abs(cpu - gpu) < 0.0001;

            var cpuOk = IsValid(cpu);
            var gpuOk = IsValid(gpu);

            // MÁXIMO SOBRE AS LEITURAS DISPONÍVEIS.
            // Math.Max devolve NaN se qualquer operando for NaN; usar isso aqui
            // descartava a leitura VÁLIDA da CPU sempre que a GPU não tinha sensor,
            // e o cartão passava a exibir "NaN°C" em vez da temperatura real.
            double max = double.NaN;
            if (cpuOk) max = cpu;
            if (gpuOk && (double.IsNaN(max) || gpu > max)) max = gpu;

            string mode = parameter as string ?? "Text";

            if (mode == "Brush")
                return BrushFor(GetColor(max));

            if (mode == "Color")
                return GetColor(max);

            if (mode == "ColorHalo")
            {
                Color c = GetColor(max);
                return Color.FromArgb((byte)77, c.R, c.G, c.B);
            }

            if (mode == "ColorHaloSoft")
            {
                Color c = GetColor(max);
                return Color.FromArgb((byte)36, c.R, c.G, c.B);
            }

            if (mode == "BrushSoft")
                return SoftBrushFor(GetColor(max));

            // Sem nenhum sensor: "N/D" explícito, nunca um número e nunca cor verde
            // (verde aqui significaria "medido e saudável", que é uma afirmação falsa).
            if (double.IsNaN(max))
                return NotAvailableText;

            if (cpuOk && gpuOk && !singleSource && Math.Abs(gpu - cpu) >= 1)
                return string.Format(culture, "CPU {0:F0}°  GPU {1:F0}°", cpu, gpu);

            // Só um dos dois sensores tem leitura: mostra a que existe, identificada.
            if (cpuOk && !gpuOk)
                return string.Format(culture, "CPU {0:F0}°C", cpu);

            if (gpuOk && !cpuOk)
                return string.Format(culture, "GPU {0:F0}°C", gpu);

            return string.Format(culture, "{0:F0}°C", max);
        }

        /// <summary>Texto exibido quando não há sensor algum. Curto o bastante para o widget.</summary>
        public const string NotAvailableText = "N/D";

        /// <summary>Temperatura plausível de um sensor real: 0–150 °C.</summary>
        private static bool IsValid(double t) => !double.IsNaN(t) && t > 0 && t < 150;

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Lê um valor de temperatura preservando a ausência de sensor.
        /// A versão anterior devolvia 0 para nulo/erro e usava <c>Convert.ToDouble</c>
        /// para o resto — o que devolvia <c>double.NaN</c> e, combinado com
        /// <c>Math.Max</c>, contaminava o outro sensor.
        /// </summary>
        private static double ToTemp(object? value)
        {
            if (value is null) return double.NaN;
            if (value is double d) return double.IsNaN(d) ? double.NaN : d;
            try
            {
                var converted = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return double.IsNaN(converted) ? double.NaN : converted;
            }
            catch { return double.NaN; }
        }

        private static Color GetColor(double maxTemp)
        {
            // FONTE ÚNICA DE VERDADE.
            //
            // A severidade térmica vivia duplicada: aqui nos cartões do Dashboard
            // e dentro de ThermalGlyphResolver, usada pelas notificações. Duas
            // listas de limiares sempre divergem com o tempo — e quando divergem,
            // o cartão mostra laranja enquanto a notificação diz "vermelho", ou
            // pior, o inverso.
            //
            // A lista agora existe em um único lugar, ThermalGlyphResolver, e
            // ambos os consumidores delegam para ela. Mudou o limiar? Muda em um
            // lugar só e cartão, widget e notificação acompanham juntos.
            return ThermalGlyphResolver.ColorForTemperature(maxTemp);
        }
    }
}
