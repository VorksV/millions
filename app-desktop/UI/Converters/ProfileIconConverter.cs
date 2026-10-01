using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.UI.Converters
{
    /// <summary>
    /// Converte um IntelligentProfileType em uma geometria vetorial moderna
    /// (traço contínuo em grade 24x24) para substituir emojis nos cards e tooltips.
    /// </summary>
    public class ProfileIconConverter : IValueConverter
    {
        private static readonly Dictionary<IntelligentProfileType, Geometry> Cache = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var profile = value is IntelligentProfileType p ? p : IntelligentProfileType.GeneralBalanced;
            LogInfo($"ICON_CONVERT start; value={value ?? "null"} valueType={value?.GetType().FullName ?? "null"} profile={profile} targetType={targetType} parameter={parameter ?? "null"}");

            try
            {
                var geometry = GetGeometry(profile);
                LogInfo($"ICON_CONVERT success; profile={profile} geometry={geometry.GetType().FullName} bounds={geometry.Bounds}");
                return geometry;
            }
            catch (Exception ex)
            {
                LogError($"ICON_CONVERT failed; profile={profile}", ex);
                throw;
            }
        }

        /// <summary>
        /// Geometria vetorial de um perfil, com cache.
        /// Tornado público para que a página de Perfil Inteligente (que seleção
        /// por token de texto, não pelo enum) reaproveite EXATAMENTE os mesmos
        /// ícones do modal, em vez de duplicar os path data.
        /// </summary>
        public static Geometry GetGeometryFor(IntelligentProfileType profile) => GetGeometry(profile);

        private static Geometry GetGeometry(IntelligentProfileType profile)
        {
            if (Cache.TryGetValue(profile, out var cached))
            {
                LogInfo($"ICON_CACHE hit; profile={profile}");
                return cached;
            }

            LogInfo($"ICON_CACHE miss; profile={profile}; parse iniciado");
            var pathData = profile switch
            {
                IntelligentProfileType.GamerCompetitive => Target(),
                IntelligentProfileType.GamerSinglePlayer => Gamepad(),
                IntelligentProfileType.GamerSimulation => Speedometer(),
                IntelligentProfileType.GamerMMO => Network(),
                IntelligentProfileType.GamerStrategy => Crown(),
                IntelligentProfileType.WorkOffice => Briefcase(),
                IntelligentProfileType.CreativeVideoEditing => Film(),
                IntelligentProfileType.DeveloperProgramming => Code(),
                IntelligentProfileType.EnterpriseSecure => ShieldCheck(),
                _ => Balance()
            };
            LogInfo($"ICON_PATH; profile={profile} length={pathData.Length} data='{pathData}'");
            var geometry = Geometry.Parse(pathData);

            Cache[profile] = geometry;
            LogInfo($"ICON_CACHE stored; profile={profile} bounds={geometry.Bounds}");
            return geometry;
        }

        private static string Target()
            => "M12,5 A7,7 0 1,0 12,19 A7,7 0 1,0 12,5 M12,10 A2,2 0 1,0 12,14 A2,2 0 1,0 12,10 M12,1.5 V5 M12,19 V22.5 M1.5,12 H5 M19,12 H22.5";

        private static string Gamepad()
            => "M8,7 H16 A3,3 0 0,1 19,10 V14 A3,3 0 0,1 16,17 H8 A3,3 0 0,1 5,14 V10 A3,3 0 0,1 8,7 Z M8,10.5 V14.5 M6,12.5 H10 M15.5,11 V13 M17.5,10 V12";

        private static string Speedometer()
            => "M3,17 A9,9 0 0,1 21,17 M12,17 L16.8,10.2 M12,15.4 A1.6,1.6 0 1,0 12,18.6 A1.6,1.6 0 1,0 12,15.4 M12,17 H12.01";

        private static string Network()
            => "M12,3.5 A3,3 0 1,0 12,9.5 A3,3 0 1,0 12,3.5 M4.5,7 A3,3 0 1,0 4.5,13 A3,3 0 1,0 4.5,7 M19.5,7 A3,3 0 1,0 19.5,13 A3,3 0 1,0 19.5,7 M6.6,12.2 L9.8,11.4 M14.2,11.4 L17.4,12.2 M6.6,12.2 L6,15.4 M17.4,12.2 L18,15.4 M6,15.4 H18";

        private static string Crown()
            => "M4,18 H20 M4.5,18 L5.5,8.5 L9.5,12.5 L12,6.5 L14.5,12.5 L18.5,8.5 L19.5,18 Z M12,6.5 V4";

        private static string Briefcase()
            => "M4,8 H20 A1.5,1.5 0 0,1 21.5,9.5 V18 A1.5,1.5 0 0,1 20,19.5 H4 A1.5,1.5 0 0,1 2.5,18 V9.5 A1.5,1.5 0 0,1 4,8 Z M9,8 V6 A1.5,1.5 0 0,1 10.5,4.5 H13.5 A1.5,1.5 0 0,1 15,6 V8 M2.5,13 H21.5";

        private static string Film()
            => "M4,4 H20 A1,1 0 0,1 21,5 V19 A1,1 0 0,1 20,20 H4 A1,1 0 0,1 3,19 V5 A1,1 0 0,1 4,4 Z M7,4 V20 M17,4 V20 M3,9 H7 M3,15 H7 M17,9 H21 M17,15 H21 M10,9.5 L14.5,12 L10,14.5 Z";

        private static string Code()
            => "M8.5,6.5 L3.5,12 L8.5,17.5 M15.5,6.5 L20.5,12 L15.5,17.5 M13.8,4.5 L10.2,19.5";

        private static string Balance()
            => "M12,4.5 V19.5 M8,19.5 H16 M12,7 H4 M12,7 H20 M4,7 L1.5,12.5 A2.5,2.5 0 0,0 6.5,12.5 Z M20,7 L17.5,12.5 A2.5,2.5 0 0,0 22.5,12.5 Z M10,7 A2,2 0 1,0 14,7 A2,2 0 1,0 10,7";

        private static string ShieldCheck()
            => "M12,2.5 L20,5.5 V11.5 C20,16.5 16.8,20.8 12,22 C7.2,20.8 4,16.5 4,11.5 V5.5 Z M8.5,11.8 L11,14.3 L15.5,9.4";

        private static void LogInfo(string message)
        {
            VoltrisOptimizer.App.LoggingService?.LogInfo($"[PROFILE_MODAL][ICON] {message}");
        }

        private static void LogError(string message, Exception exception)
        {
            VoltrisOptimizer.App.LoggingService?.LogError(
                $"[PROFILE_MODAL][ICON] {message}: {exception}",
                exception);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
