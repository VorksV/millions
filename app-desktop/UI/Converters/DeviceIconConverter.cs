using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace VoltrisOptimizer.UI.Converters
{
    public class DeviceIconConverter : IValueConverter
    {
        private static readonly Dictionary<string, Geometry> Cache = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return GetGeometry(value?.ToString());
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        private static Geometry GetGeometry(string deviceType)
        {
            string key = string.IsNullOrWhiteSpace(deviceType) ? "DEFAULT" : deviceType.Trim().ToUpperInvariant();
            if (Cache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var geometry = Geometry.Parse(Resolve(key));
            Cache[key] = geometry;
            return geometry;
        }

        private static string Resolve(string t)
        {
            // A ordem importa: tipos compostos como "Console/TV" e "Smartphone/Router"
            // devem ser resolvidos pelo componente mais especifico primeiro.
            if (t.Contains("CONSOLE") || t.Contains("PLAYSTATION") || t.Contains("XBOX") || t.Contains("NINTENDO") || t.Contains("GAMING")) return Console();
            if (t.Contains("LAPTOP") || t.Contains("NOTEBOOK") || t.Contains("PORTABLE") || t.Contains("MACBOOK")) return Laptop();
            if (t.Contains("TABLET") || t.Contains("IPAD")) return Tablet();
            if (t.Contains("PHONE") || t.Contains("IPHONE") || t.Contains("ANDROID") || t.Contains("CELULAR") || t.Contains("MOBILE")) return Smartphone();
            if (t.Contains("CAMERA") || t.Contains("CCTV")) return Camera();
            if (t.Contains("ROUTER") || t.Contains("ACCESS POINT") || t.Contains("GATEWAY") || t.Contains("MODEM") || t.Contains("WIFI")) return Router();
            if (t.Contains("PRINTER") || t.Contains("SCANNER") || t.Contains("IMPRESSORA")) return Printer();
            if (t.Contains("NAS") || t.Contains("SERVER") || t.Contains("STORAGE")) return Nas();
            if (t.Contains("SPEAKER") || t.Contains("ALEXA") || t.Contains("ECHO") || t.Contains("SOUND")) return Speaker();
            if (t.Contains("STREAMING") || t.Contains("CHROMECAST") || t.Contains("ROKU") || t.Contains("FIRESTICK") || t.Contains("MEDIA")) return Streaming();
            if (t.Contains("TV") || t.Contains("TELEVISION")) return Television();
            if (t.Contains("SINGLE BOARD") || t.Contains("RASPBERRY") || t.Contains("ARDUINO") || t.Contains("SBC") || t.Contains("PI,")) return Board();
            if (t.Contains("IOT") || t.Contains("SMART HOME") || t.Contains("SMART") || t.Contains("WELLNESS")) return SmartHome();
            if (t.Contains("DESKTOP") || t.Contains("PC") || t.Contains("IMAC") || t.Contains("COMPUTER") || t.Contains("WORKSTATION")) return Desktop();
            if (t.Contains("VEHICLE") || t.Contains("CAR") || t.Contains("AUTO")) return Car();
            return Unknown();
        }

        private static string Unknown()
            => "M12,2.5 A9.5,9.5 0 1,1 11.99,2.5 Z M12,7.5 V13 M12,16.75 V17.25";

        private static string Car()
            => "M4,11.5 L6,6.5 H18 L20,11.5 V18.5 H4 Z M4,11.5 H20 M7,18.5 V20.5 M17,18.5 V20.5 M7,15 H8.5 M15.5,15 H17";

        private static string Desktop()
            => "M4,4.5 H20 V15.5 H4 Z M2.5,19.5 H21.5 M8,19.5 V15.5 M16,19.5 V15.5";

        private static string Laptop()
            => "M5,5.5 H19 V15.5 H5 Z M1.5,18.5 H22.5 L19.5,15.5 H4.5 Z M10,18.5 H14";

        private static string Smartphone()
            => "M8,2.5 H16 A1,1 0 0,1 17,3.5 V20.5 A1,1 0 0,1 16,21.5 H8 A1,1 0 0,1 7,20.5 V3.5 A1,1 0 0,1 8,2.5 Z M10.5,5.5 H13.5 M10.75,18.5 H13.25";

        private static string Tablet()
            => "M6,2.5 H18 A1,1 0 0,1 19,3.5 V20.5 A1,1 0 0,1 18,21.5 H6 A1,1 0 0,1 5,20.5 V3.5 A1,1 0 0,1 6,2.5 Z M10.75,18.5 H13.25";

        private static string Router()
            => "M2.5,13.5 H21.5 V20 H2.5 Z M6,16.75 H6.01 M9.5,16.75 H9.51 M2,9.5 C6,5 18,5 22,9.5 M5.5,12 C8,9 16,9 18.5,12";

        private static string Camera()
            => "M3,7.5 H8 L9.5,5 H14.5 L16,7.5 H21 A1,1 0 0,1 22,8.5 V18 A1,1 0 0,1 21,19 H3 A1,1 0 0,1 2,18 V8.5 A1,1 0 0,1 3,7.5 Z M12,16.5 A3.5,3.5 0 1,0 12,9.5 A3.5,3.5 0 1,0 12,16.5 Z";

        private static string Printer()
            => "M7,8.5 V3.5 H17 V8.5 M7,17.5 H5 A1,1 0 0,1 4,16.5 V11.5 A1,1 0 0,1 5,10.5 H19 A1,1 0 0,1 20,11.5 V16.5 A1,1 0 0,1 19,17.5 H17 M7,14.5 H17 V20.5 H7 Z";

        private static string Nas()
            => "M3,4.5 H21 V9.5 H3 Z M3,14.5 H21 V19.5 H3 Z M6.5,7 H6.51 M6.5,17 H6.51 M10,7 H14 M10,17 H14";

        private static string Television()
            => "M3,4.5 H21 V16 H3 Z M8,20.5 L12,16 L16,20.5";

        private static string Streaming()
            => "M3,15.5 A3.5,3.5 0 0,1 6.5,19 M3,11.5 A7.5,7.5 0 0,1 10.5,19 M3,19.5 H4 M14,19.5 H21 V4.5 H14 Z";

        private static string Speaker()
            => "M12,2.5 A5,5 0 0,1 17,7.5 V16.5 A5,5 0 0,1 7,16.5 V7.5 A5,5 0 0,1 12,2.5 Z M9.5,7 H14.5 M9.5,17 H14.5 M12,7.5 V16.5";

        private static string Console()
            => "M7,7 H17 A6,6 0 0,1 17,19 H7 A6,6 0 0,1 7,7 Z M8,10.5 V13.5 M6.5,12 H9.5 M15,11 V13 M17,10.5 V11.5";

        private static string Board()
            => "M7,7 H17 V17 H7 Z M4,9.5 H7 M4,12 H7 M4,14.5 H7 M17,9.5 H20 M17,12 H20 M17,14.5 H20 M9.5,4 V7 M12,4 V7 M14.5,4 V7 M9.5,17 V20 M12,17 V20 M14.5,17 V20";

        private static string SmartHome()
            => "M12,2.5 L21.5,10.5 H20 V20.5 A1,1 0 0,1 19,21.5 H5 A1,1 0 0,1 4,20.5 V10.5 H2.5 Z M9.5,21.5 V14 H14.5 V21.5";
    }
}
