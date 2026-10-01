using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.UI.Converters
{
    /// <summary>
    /// Converte um booleano para o inverso da visibilidade.
    /// true -> Collapsed, false -> Visible.
    /// </summary>
    public class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Collapsed : Visibility.Visible;
            }
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
            {
                return visibility != Visibility.Visible;
            }
            return true;
        }
    }

    /// <summary>
    /// Converte uma string Hex (#RRGGBB ou #AARRGGBB) para um SolidColorBrush.
    /// </summary>
    public class HexStringToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string hex && !string.IsNullOrWhiteSpace(hex))
            {
                try
                {
                    return (SolidColorBrush)new BrushConverter().ConvertFrom(hex.Trim());
                }
                catch { }
            }
            return Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converte um valor nulo em visibilidade.
    /// null -> Collapsed, not null -> Visible.
    /// </summary>
    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isNull = value == null || (value is string s && string.IsNullOrWhiteSpace(s));
            bool invert = parameter?.ToString() == "Invert";
            
            if (invert) return isNull ? Visibility.Visible : Visibility.Collapsed;
            return isNull ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converte um valor de temperatura (double) para uma largura proporcional (double).
    /// </summary>
    public class TemperatureToWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double temp)
            {
                // Limita entre 0 e 100 por padrão, ou usa o parâmetro como fator
                double factor = double.TryParse(parameter?.ToString(), out double f) ? f : 1.0;
                return Math.Max(0, Math.Min(100 * factor, temp * factor));
            }
            return 0.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converte um objeto Color para um SolidColorBrush.
    /// </summary>
    public class ColorToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Color color)
            {
                return new SolidColorBrush(color);
            }
            return Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converte um tipo de recomendação em um Path Data (Ícone SVG).
    /// </summary>
    public class ActionToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string type = value?.ToString()?.ToUpper() ?? "";

            return type switch
            {
                "PERFORMANCE" => "M21,16.5C21,16.88 20.79,17.21 20.47,17.38L12.57,21.82C12.41,21.94 12.21,22 12,22C11.79,22 11.59,21.94 11.43,21.82L3.53,17.38C3.21,17.21 3,16.88 3,16.5V7.5C3,7.12 3.21,6.79 3.53,6.62L11.43,2.18C11.59,2.06 11.79,2 12,2C12.21,2 12.41,2.06 12.57,2.18L20.47,6.62C20.79,6.79 21,7.12 21,7.5V16.5Z",
                "SECURITY" => "M12,1L3,5V11C3,16.55 6.84,21.74 12,23C17.16,21.74 21,16.55 21,11V5L12,1Z",
                "NETWORK" => "M21,21H3V3H21V21M19,19V5H5V19H19M14.5,13.5V10.5H9.5V13.5H14.5M10.5,12L12,10.5L13.5,12H10.5Z",
                "CLEANUP" => "M19.36,2.72L20.78,4.14L15.06,9.85C16.13,11.39 16.28,13.24 15.38,14.44L9.06,8.12C10.26,7.22 12.11,7.37 13.65,8.44L19.36,2.72Z",
                _ => "M12,2L4.5,20.29L5.21,21L12,18L18.79,21L19.5,20.29L12,2Z" // Default arrow/rocket
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converte um valor (Progresso) em uma string de DashArray para círculos.
    /// Exemplo de uso: <Binding Path="Score"/>
    /// </summary>
    public class CircularProgressConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length > 0 && values[0] is double progress)
            {
                // Circunferência de um círculo com StrokeThickness 18 e largura 200 é aproximadamente 182*PI
                double circumference = 182 * Math.PI;
                double offset = (progress / 100.0) * circumference;
                return new DoubleCollection { offset, circumference };
            }
            return new DoubleCollection { 0, 1000 };
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converte o progresso REAL (0-100, em double) em um arco de anel de progresso
    /// desenhado a partir das 12 horas (topo), no sentido horário.
    /// Retorna uma PathGeometry congelada em coordenadas ABSOLUTAS de um canvas
    /// 320x320 (centro 160,160 / raio 145), que coincide exatamente com a borda
    /// do botão circular de 290px centrado nesse canvas — o anel cresce fielmente
    /// sobre a borda do botão, sem escalas indesejadas (Stretch=None no Path).
    /// Nunca gera animação artificial: reflete exatamente o valor real do progresso.
    /// </summary>
    public class ArcProgressConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double progress = 0;
            if (value is double d) progress = d;
            else if (value is float f) progress = f;
            else if (value is int i) progress = i;

            progress = Math.Max(0, Math.Min(100, progress));
            if (progress <= 0)
            {
                var empty = new PathGeometry();
                empty.Freeze();
                return empty;
            }

            // Canvas 320x320: centro (160,160), raio 145 (= exatamente a borda do botão de 290px).
            // O arco de carregamento acompanha o contorno azul do NÚCLEO — mesmo círculo, nem
            // dentro, nem fora, nem maior (a espessura limita-se a traçar a borda).
            const double center = 160;
            const double radius = 145;
            // Start às 12h (ângulo -90° em coordenadas de tela), sentido horário.
            const double startDegrees = -90.0;
            double sweepDegrees = (progress / 100.0) * 360.0;
            if (sweepDegrees >= 360.0) sweepDegrees = 359.999; // evita degeneração

            double toRad = Math.PI / 180.0;
            double startRad = startDegrees * toRad;
            double endRad = (startDegrees + sweepDegrees) * toRad;
            double startX = center + radius * Math.Cos(startRad);
            double startY = center + radius * Math.Sin(startRad);
            double endX = center + radius * Math.Cos(endRad);
            double endY = center + radius * Math.Sin(endRad);

            bool isLargeArc = sweepDegrees > 180.00;

            var figure = new PathFigure { StartPoint = new Point(startX, startY), IsClosed = false, IsFilled = false };
            figure.Segments.Add(new ArcSegment(new Point(endX, endY), new Size(radius, radius), 0, isLargeArc, SweepDirection.Clockwise, true));

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            geometry.Freeze();
            return geometry;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Alias para BoolToColorConverter para compatibilidade com XAML.
    /// </summary>
    public class BooleanToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b) return new SolidColorBrush(Color.FromRgb(34, 197, 94)); // Green
            return new SolidColorBrush(Color.FromRgb(107, 114, 128)); // Gray
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class PercentageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => $"{value}%";
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class BooleanToThrottleBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b) return new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red-500
            return new SolidColorBrush(Color.FromRgb(34, 197, 94)); // Green-500
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class GamerModeStatusConverter : IValueConverter, IMultiValueConverter
    {
        // IValueConverter implementation (for backward compatibility if used in single bindings)
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b) return LocalizationService.Instance.GetString("GamerModeActive");
            return LocalizationService.Instance.GetString("GamerModeInactive");
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();

        // IMultiValueConverter implementation (Fix for MultiBinding in GamerView.xaml)
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 2 && values[0] is bool active)
            {
                if (active)
                {
                    string gameName = values[1]?.ToString();
                    var activeText = LocalizationService.Instance.GetString("GamerModeActive");
                    return !string.IsNullOrWhiteSpace(gameName) 
                        ? $"{activeText}: {gameName.ToUpper()}" 
                        : activeText;
                }
                return LocalizationService.Instance.GetString("GamerModeInactive");
            }
            
            if (values.Length > 0) return Convert(values[0], targetType, parameter, culture);
            return LocalizationService.Instance.GetString("GamerModeInactive");
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class InactiveToRedShadowConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && !b) return new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Red, BlurRadius = 10, Opacity = 0.5 };
            return null!;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class PercentageToWidthConverter : IValueConverter, IMultiValueConverter
    {
        // Caso usado em Binding simples (IValueConverter)
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d) return d;
            if (value is float f) return (double)f;
            if (value is int i) return (double)i;
            return 0.0;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();

        // Caso usado em MultiBinding (IMultiValueConverter)
        // Valores esperados: [0] Porcentagem (0-100), [1] Largura Total
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 2 && values[0] is double percentage && values[1] is double totalWidth)
            {
                return Math.Max(0, (percentage / 100.0) * totalWidth);
            }
            if (values.Length > 0 && values[0] is double val) return val;
            return 0.0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class DeviceTypeToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string type = value?.ToString()?.ToUpper() ?? "";
            if (type.Contains("LAPTOP") || type.Contains("PORTABLE")) return new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Amber
            return new SolidColorBrush(Color.FromRgb(59, 130, 246)); // Blue
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class EqualToIntConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value != null && parameter != null)
            {
                return value.ToString() == parameter.ToString();
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b && parameter != null)
            {
                if (int.TryParse(parameter.ToString(), out int intValue)) return intValue;
                return parameter;
            }
            return Binding.DoNothing;
        }
    }

    public class BoolToOpacityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b) return b ? 1.0 : 0.4;
            return 1.0;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class StringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool hasText = !string.IsNullOrWhiteSpace(value?.ToString());
            return hasText ? Visibility.Visible : Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class ObjectEqualsConverter : IValueConverter, IMultiValueConverter
    {
        // IValueConverter (Binding simples)
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null) return value == parameter;
            return value.ToString() == parameter.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();

        // IMultiValueConverter (MultiBinding - Usado em PersonalizeView.xaml para comparacao de temas)
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2) return false;
            
            var val1 = values[0];
            var val2 = values[1];

            if (val1 == null || val2 == null) return val1 == val2;

            // Comparacao de igualdade de objeto ou string
            return val1.Equals(val2) || val1.ToString() == val2.ToString();
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converte o status de um arquivo recuperado em uma cor.
    /// </summary>
    public class StatusToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string status = value?.ToString()?.ToUpper() ?? "";
            
            if (status.Contains("EXCELENTE") || status.Contains("ÓTIMO") || status.Contains("BOM"))
                return new SolidColorBrush(Color.FromRgb(34, 197, 94)); // Green
            
            if (status.Contains("MÉDIO") || status.Contains("RAZOÁVEL"))
                return new SolidColorBrush(Color.FromRgb(234, 179, 8)); // Yellow/Amber
                
            return new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    /// <summary>
    /// Converte um inteiro para visibilidade.
    /// Por padrão: 0 -> Collapsed, >0 -> Visible.
    /// Se o parâmetro for "Invert": 0 -> Visible, >0 -> Collapsed.
    /// </summary>
    public class IntToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int i)
            {
                bool invert = parameter?.ToString() == "Invert";
                bool isZero = i == 0;
                
                if (invert) return isZero ? Visibility.Visible : Visibility.Collapsed;
                return isZero ? Visibility.Collapsed : Visibility.Visible;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
