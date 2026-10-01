using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace VoltrisOptimizer.UI.Converters
{
    /// <summary>
    /// Resolve o ícone de um dispositivo da rede considerando, em ordem de prioridade,
    /// o sistema operacional, o tipo do dispositivo e por último o fabricante.
    /// Devolve a geometria e a cor de destaque da marca.
    /// </summary>
    public sealed class DeviceVisual
    {
        public Geometry Glyph { get; init; } = Geometry.Empty;
        public Color Accent { get; init; } = Colors.SlateGray;
    }

    public class DeviceGlyphConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var (glyph, _) = DeviceGlyphResolver.Resolve(values);
            return glyph;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public class DeviceAccentConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var (_, accent) = DeviceGlyphResolver.Resolve(values);
            return new SolidColorBrush(accent);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public static class DeviceGlyphResolver
    {
        private static readonly ConcurrentDictionary<string, DeviceVisual> Cache = new(StringComparer.OrdinalIgnoreCase);

        public static (Geometry, Color) Resolve(object?[] values)
        {
            var deviceType = values.Length > 0 ? values[0]?.ToString() ?? string.Empty : string.Empty;
            var os = values.Length > 1 ? values[1]?.ToString() ?? string.Empty : string.Empty;
            var vendor = values.Length > 2 ? values[2]?.ToString() ?? string.Empty : string.Empty;

            var key = $"{os}|{deviceType}|{vendor}";
            var visual = Cache.GetOrAdd(key, _ => Build(os, deviceType, vendor));
            return (visual.Glyph, visual.Accent);
        }

        private static DeviceVisual Build(string os, string deviceType, string vendor)
        {
            var O = os.ToUpperInvariant();
            var D = deviceType.ToUpperInvariant();
            var V = vendor.ToUpperInvariant();

            // ---- 1) Marcas de plataforma: tem prioridade sobre o tipo ----

            if (O.Contains("ANDROID"))
                return Make(P.Android, P.AndroidGreen);

            if (O.Contains("IOS") || O.Contains("IPADOS") || O.Contains("TVOS") || O.Contains("MACOS") || O.Contains("DARWIN"))
                return Make(P.Apple, P.AppleGray);

            if (O.Contains("FIRE"))
                return Make(P.Streaming, P.AmazonOrange);

            if (O.Contains("HARMONY"))
                return Make(P.Smartphone, P.HuaweiRed);

            if (O.Contains("CHROMEOS") || O.Contains("CHROME OS"))
                return Make(P.Chrome, P.ChromeBlue);

            if (O.Contains("PLAYSTATION"))
                return Make(P.Gamepad, P.PlayStationBlue);

            if (O.Contains("XBOX"))
                return Make(P.Gamepad, P.XboxGreen);

            if (O.Contains("NINTENDO"))
                return Make(P.Gamepad, P.NintendoRed);

            if (O.Contains("LINUX"))
                return Make(P.Penguin, P.LinuxGold);

            if (O.Contains("WINDOWS"))
                return Make(P.Windows, P.WindowsBlue);

            if (O.Contains("ROKU") || O.Contains("STREAMING"))
                return Make(P.Streaming, P.StreamingPurple);

            // ---- 2) Tipo do dispositivo (mais especifico que SO generico) ----

            if (D.Contains("CONSOLE")) return Make(P.Gamepad, P.NintendoRed);
            if (D.Contains("LAPTOP") || D.Contains("MACBOOK") || D.Contains("NOTEBOOK")) return Make(P.Laptop, P.LaptopViolet);
            if (D.Contains("TABLET") || D.Contains("IPAD")) return Make(P.Tablet, P.TabletTeal);
            if (D.Contains("PHONE") || D.Contains("MOBILE") || D.Contains("CELULAR")) return Make(P.Smartphone, P.AndroidGreen);
            if (D.Contains("SPEAKER") || D.Contains("ALEXA") || D.Contains("ECHO") || D.Contains("SOUND")) return Make(P.Speaker, P.SpeakerOrange);
            if (D.Contains("CAMERA") || D.Contains("CCTV")) return Make(P.Camera, P.CameraIndigo);
            if (D.Contains("ROUTER") || D.Contains("GATEWAY") || D.Contains("ACCESS POINT") || D.Contains("MODEM") || D.Contains("WIFI")) return Make(P.Router, P.RouterAmber);
            if (D.Contains("PRINTER") || D.Contains("SCANNER") || D.Contains("IMPRESSORA")) return Make(P.Printer, P.PrinterSlate);
            if (D.Contains("NAS") || D.Contains("SERVER") || D.Contains("STORAGE")) return Make(P.Server, P.ServerBlue);
            if (D.Contains("STREAMING") || D.Contains("CHROMECAST") || D.Contains("ROKU") || D.Contains("FIRESTICK")) return Make(P.Streaming, P.StreamingPurple);
            if (D.Contains("TV") || D.Contains("TELEVISION")) return Make(P.Television, P.TvCyan);
            if (D.Contains("SINGLE BOARD") || D.Contains("RASPBERRY") || D.Contains("ARDUINO") || D.Contains("SBC")) return Make(P.Board, P.BoardTeal);
            if (D.Contains("VEHICLE") || D.Contains("CAR")) return Make(P.Car, P.CarRed);
            if (D.Contains("IOT") || D.Contains("SMART HOME") || D.Contains("SMART") || D.Contains("WELLNESS")) return Make(P.SmartHome, P.SmartHomeAmber);
            if (D.Contains("DESKTOP") || D.Contains("PC") || D.Contains("IMAC") || D.Contains("COMPUTER") || D.Contains("WORKSTATION")) return Make(P.Desktop, P.DesktopBlue);

            // ---- 3) SO periferico generico ----

            if (O.Contains("PRINTER")) return Make(P.Printer, P.PrinterSlate);
            if (O.Contains("ROUTER")) return Make(P.Router, P.RouterAmber);
            if (O.Contains("CAMERA")) return Make(P.Camera, P.CameraIndigo);
            if (O.Contains("IOT") || O.Contains("SMART HOME")) return Make(P.SmartHome, P.SmartHomeAmber);
            if (O.Contains("NAS")) return Make(P.Server, P.ServerBlue);
            if (O.Contains("TV")) return Make(P.Television, P.TvCyan);
            if (O.Contains("TESLA") || O.Contains("CAR") || O.Contains("VEHICLE")) return Make(P.Car, P.CarRed);
            if (O.Contains("SPEAKER") || O.Contains("ALEXA") || O.Contains("ECHO")) return Make(P.Speaker, P.SpeakerOrange);

            // ---- 4) Fabricante (SO generico) ----

            if (D.Contains("APPLE") || V.Contains("APPLE")) return Make(P.Apple, P.AppleGray);
            if (V.Contains("SAMSUNG") || V.Contains("XIAOMI") || V.Contains("REDMI") || V.Contains("ONEPLUS") || V.Contains("MOTOROLA"))
                return Make(P.Android, P.AndroidGreen);
            if (V.Contains("SONY") || V.Contains("MICROSOFT")) return Make(P.Gamepad, P.XboxGreen);

            return Make(P.Generic, P.GenericGray);
        }

        private static DeviceVisual Make(string path, Color accent)
            => new() { Glyph = Geometry.Parse(path), Accent = accent };
    }

    /// <summary>Geometrias 24x24 e paleta por marca.</summary>
    internal static class P
    {
        // --- marcas ---
        // Robô Android: antenas, cabeça, corpo, braços e pernas.
        public const string Android =
            "M7,2.4 L7.7,5.3 M17,2.4 L16.3,5.3 " +
            "M6.2,6.2 H17.8 A1.2,1.2 0 0,1 19,7.4 V11.2 H5 V7.4 A1.2,1.2 0 0,1 6.2,6.2 Z " +
            "M5.3,13.6 V17 M18.7,13.6 V17 " +
            "M7.7,13.2 H16.3 V18.3 A1.7,1.7 0 0,1 14.6,20 H9.4 A1.7,1.7 0 0,1 7.7,18.3 Z " +
            "M10,20 V22.2 M14,20 V22.2";

        // Maçã: corpo com mordida e folha.
        public const string Apple =
            "M15.9,12.4 C15.9,10.6 17.3,9.3 17.4,9.2 C16.4,7.7 14.9,7.5 14.4,7.5 " +
            "C13,7.4 11.7,8.3 11,8.3 C10.3,8.3 9.2,7.5 8,7.5 " +
            "C5.5,7.5 3.2,9.2 3.2,12.4 C3.2,14 4.1,15.7 5.3,17.2 " +
            "C5.9,17.9 6.6,18.6 7.5,18.6 C8.4,18.6 8.8,18.1 9.9,18.1 " +
            "C11,18.1 11.4,18.6 12.3,18.6 C13.2,18.6 13.8,18 14.4,17.3 " +
            "C15.1,16.5 15.4,15.7 15.4,15.7 C15.4,15.7 14.1,15.1 14.1,13.1 " +
            "C14.1,11.4 15.1,10.6 15.9,12.4 Z " +
            "M15.4,5.6 C15.9,5 16.2,4.2 16.1,3.4 C15.4,3.4 14.6,4 14.1,4.6 " +
            "C13.7,5.1 13.4,5.9 13.5,6.7 C14.2,6.8 15,6.3 15.4,5.6 Z";

        // Janela do Windows: quatro quadriláteros inclinados.
        public const string Windows =
            "M3,5.4 L10.4,4.3 V11 H3 Z M11.3,4.1 L21,2.7 V11 H11.3 Z " +
            "M3,12 H10.4 V18.7 L3,17.6 Z M11.3,12 H21 V20.3 L11.3,18.9 Z";

        // Tux: corpo, cabeça, bico e pés.
        public const string Penguin =
            "M12,2.6 C13.9,2.6 15.3,4 15.3,5.8 C15.3,6.6 15,7.3 14.6,7.9 " +
            "C16.3,8.7 17.6,10.5 17.6,12.6 C17.6,15.9 15.1,18.6 12,18.6 " +
            "C8.9,18.6 6.4,15.9 6.4,12.6 C6.4,10.5 7.7,8.7 9.4,7.9 " +
            "C9,7.3 8.7,6.6 8.7,5.8 C8.7,4 10.1,2.6 12,2.6 Z " +
            "M10.6,4.9 H13.4 V6.2 H10.6 Z M9.6,11 C10.2,10.4 11,10.4 11.6,11 " +
            "M12.4,11 C13,10.4 13.8,10.4 14.4,11 " +
            "M10.6,13.6 H13.4 L12,14.8 Z M8.6,18.4 H11.4 L10.4,21.4 H8 Z " +
            "M12.6,18.4 H15.4 L16,21.4 H13.6 Z";

        // Controle de videogame: corpo com dois punhos, cruceta e botões.
        public const string Gamepad =
            "M6.4,8 H17.6 C19.7,8 21.2,9.6 21.4,11.7 L22.4,17.6 " +
            "C22.7,19.7 21.6,21.3 19.8,20.6 L17.3,19.7 C16.7,19.4 15.9,19.4 15.3,19.7 " +
            "L13.4,20.8 C12.7,21.1 11.9,21.1 11.2,20.8 L9.3,19.7 C8.7,19.4 7.9,19.4 7.3,19.7 " +
            "L4.8,20.6 C3,21.3 1.9,19.7 2.2,17.6 L3.2,11.7 C3.4,9.6 4.9,8 6.4,8 Z " +
            "M6.1,11.2 H8.2 M7.15,10.15 V12.25 " +
            "M15.4,10.6 A1.15,1.15 0 1,0 15.4,12.9 A1.15,1.15 0 1,0 15.4,10.6 Z " +
            "M18.3,12.5 A1.15,1.15 0 1,0 18.3,14.8 A1.15,1.15 0 1,0 18.3,12.5 Z";

        public const string Chrome =
            "M12,3.4 A8.6,8.6 0 1,0 20.6,12 H13.6 A2.6,2.6 0 1,1 10.4,7.6 L16.6,7.6 " +
            "A8.6,8.6 0 0,0 12,3.4 Z M12,9.4 A2.6,2.6 0 1,0 12,14.6 A2.6,2.6 0 1,0 12,9.4 Z";

        // --- tipos ---
        public const string Laptop =
            "M5,5.6 H19 V15.4 H5 Z M1.6,18.4 H22.4 L19.4,15.4 H4.6 Z M10,18.4 H14";

        public const string Desktop =
            "M4,4.6 H20 V15.4 H4 Z M2.6,19.4 H21.4 M8,19.4 V15.4 M16,19.4 V15.4";

        public const string Smartphone =
            "M8,2.6 H16 A1.2,1.2 0 0,1 17.2,3.8 V20.2 A1.2,1.2 0 0,1 16,21.4 H8 " +
            "A1.2,1.2 0 0,1 6.8,20.2 V3.8 A1.2,1.2 0 0,1 8,2.6 Z " +
            "M10.4,5.4 H13.6 M10.8,18.6 H13.2";

        public const string Tablet =
            "M6,2.6 H18 A1.2,1.2 0 0,1 19.2,3.8 V20.2 A1.2,1.2 0 0,1 18,21.4 H6 " +
            "A1.2,1.2 0 0,1 4.8,20.2 V3.8 A1.2,1.2 0 0,1 6,2.6 Z M10.8,18.6 H13.2";

        public const string Router =
            "M2.6,13.6 H21.4 V20 H2.6 Z M6,16.8 H6.01 M9.6,16.8 H9.61 " +
            "M2,9.4 C6,5 18,5 22,9.4 M5.6,12 C8,9 16,9 18.4,12";

        public const string Printer =
            "M7,8.6 V3.6 H17 V8.6 M7,17.4 H5 A1.2,1.2 0 0,1 3.8,16.2 V11.6 " +
            "A1.2,1.2 0 0,1 5,10.4 H19 A1.2,1.2 0 0,1 20.2,11.6 V16.2 " +
            "A1.2,1.2 0 0,1 19,17.4 H17 M7,14.4 H17 V20.4 H7 Z";

        public const string Television =
            "M3,4.6 H21 V16 H3 Z M8,20.4 L12,16 L16,20.4";

        public const string Camera =
            "M3,7.6 H8 L9.5,5.1 H14.5 L16,7.6 H21 A1.2,1.2 0 0,1 22.2,8.8 V18 " +
            "A1.2,1.2 0 0,1 21,19.2 H3 A1.2,1.2 0 0,1 1.8,18 V8.8 A1.2,1.2 0 0,1 3,7.6 Z " +
            "M12,16.6 A3.5,3.5 0 1,0 12,9.6 A3.5,3.5 0 1,0 12,16.6 Z";

        public const string Server =
            "M3,4.6 H21 V9.6 H3 Z M3,14.4 H21 V19.4 H3 Z M6.6,7.1 H6.61 M6.6,16.9 H6.61 M10,7.1 H14 M10,16.9 H14";

        public const string Speaker =
            "M12,2.6 A5,5 0 0,1 17,7.6 V16.4 A5,5 0 0,1 7,16.4 V7.6 A5,5 0 0,1 12,2.6 Z " +
            "M9.6,7 H14.4 M9.6,17 H14.4 M12,7.6 V16.4";

        public const string Streaming =
            "M3,15.6 A3.5,3.5 0 0,1 6.5,19.1 M3,11.6 A7.5,7.5 0 0,1 10.5,19.1 M3,19.6 H4 " +
            "M14,19.6 H21 V4.6 H14 Z";

        public const string SmartHome =
            "M12,2.6 L21.4,10.6 H19.8 V20.4 A1.2,1.2 0 0,1 18.6,21.6 H5.4 " +
            "A1.2,1.2 0 0,1 4.2,20.4 V10.6 H2.6 Z M9.6,21.6 V14 H14.4 V21.6";

        public const string Board =
            "M7,7 H17 V17 H7 Z M4,9.5 H7 M4,12 H7 M4,14.5 H7 M17,9.5 H20 M17,12 H20 M17,14.5 H20 " +
            "M9.5,4 V7 M12,4 V7 M14.5,4 V7 M9.5,17 V20 M12,17 V20 M14.5,17 V20";

        public const string Car =
            "M4,11.6 L6,6.6 H18 L20,11.6 V18.6 H4 Z M4,11.6 H20 " +
            "M7,18.6 V20.6 M17,18.6 V20.6 M7,15 H8.6 M15.4,15 H17";

        public const string Generic =
            "M6,2.6 H18 A1.2,1.2 0 0,1 19.2,3.8 V20.2 A1.2,1.2 0 0,1 18,21.4 H6 " +
            "A1.2,1.2 0 0,1 4.8,20.2 V3.8 A1.2,1.2 0 0,1 6,2.6 Z M8.6,6.6 H15.4 M9.6,10.4 H14.4";

        // --- paleta das marcas ---
        public static Color AndroidGreen => Color.FromRgb(0x3D, 0xDC, 0x84);
        public static Color AppleGray => Color.FromRgb(0xA2, 0xAA, 0xB3);
        public static Color WindowsBlue => Color.FromRgb(0x00, 0x78, 0xD4);
        public static Color LinuxGold => Color.FromRgb(0xE8, 0xA1, 0x3B);
        public static Color ChromeBlue => Color.FromRgb(0x42, 0x85, 0xF4);
        public static Color HuaweiRed => Color.FromRgb(0xC7, 0x00, 0x0B);
        public static Color AmazonOrange => Color.FromRgb(0xFF, 0x99, 0x00);
        public static Color PlayStationBlue => Color.FromRgb(0x00, 0x37, 0x91);
        public static Color XboxGreen => Color.FromRgb(0x10, 0x7C, 0x10);
        public static Color NintendoRed => Color.FromRgb(0xE6, 0x00, 0x12);

        public static Color RouterAmber => Color.FromRgb(0xF5, 0xB9, 0x42);
        public static Color PrinterSlate => Color.FromRgb(0x7A, 0x87, 0x94);
        public static Color TvCyan => Color.FromRgb(0x31, 0xA8, 0xFF);
        public static Color CameraIndigo => Color.FromRgb(0x8B, 0x5C, 0xF6);
        public static Color ServerBlue => Color.FromRgb(0x5B, 0x8D, 0xEF);
        public static Color SpeakerOrange => Color.FromRgb(0xFF, 0x8A, 0x3D);
        public static Color StreamingPurple => Color.FromRgb(0x66, 0x2D, 0x91);
        public static Color SmartHomeAmber => Color.FromRgb(0xF5, 0xB9, 0x42);
        public static Color BoardTeal => Color.FromRgb(0x2E, 0xC4, 0xB6);
        public static Color CarRed => Color.FromRgb(0xF0, 0x44, 0x66);
        public static Color LaptopViolet => Color.FromRgb(0x8B, 0x31, 0xFF);
        public static Color TabletTeal => Color.FromRgb(0x22, 0xC7, 0x7A);
        public static Color DesktopBlue => Color.FromRgb(0x31, 0xA8, 0xFF);
        public static Color GenericGray => Color.FromRgb(0x8A, 0x95, 0xA1);
    }
}
