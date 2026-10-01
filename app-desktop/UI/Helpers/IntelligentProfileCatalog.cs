using System.Windows.Media;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.UI.Helpers
{
    /// <summary>
    /// Catálogo central dos perfis inteligentes (ordem, metadados, glyph e traduções)
    /// compartilhado entre o Dashboard e o seletor de perfil.
    /// </summary>
    public static class IntelligentProfileCatalog
    {
        public sealed class ProfileOption
        {
            public IntelligentProfileType Type { get; init; }
            public string Glyph { get; init; } = "";
            public string DisplayName { get; init; } = "";
            public string GainText { get; init; } = "";
            public string Description { get; init; } = "";
            public Brush AccentBrush { get; init; } = Brushes.Transparent;
            public bool IsActive { get; init; }
        }

        public static readonly IntelligentProfileType[] AllProfiles =
        {
            IntelligentProfileType.GamerCompetitive,
            IntelligentProfileType.GamerSinglePlayer,
            IntelligentProfileType.GamerSimulation,
            IntelligentProfileType.GamerMMO,
            IntelligentProfileType.GamerStrategy,
            IntelligentProfileType.WorkOffice,
            IntelligentProfileType.CreativeVideoEditing,
            IntelligentProfileType.DeveloperProgramming,
            IntelligentProfileType.GeneralBalanced,
            IntelligentProfileType.EnterpriseSecure
        };

        public static string GetLocalizedProfileName(IntelligentProfileType profile)
            => LocalizationService.Instance.GetString(GetProfileMeta(profile).NameKey);

        public static (string NameKey, string GainKey, string DescKey, string Accent) GetProfileMeta(IntelligentProfileType p) => p switch
        {
            IntelligentProfileType.GamerCompetitive => ("ProfileGamerCompetitive", "ProfileCompGamerGain", "ProfileDescGamerCompetitive", "#FF8C00"),
            IntelligentProfileType.GamerSinglePlayer => ("ProfileGamerSinglePlayer", "ProfileSpGamerGain", "ProfileDescGamerSinglePlayer", "#00B09B"),
            IntelligentProfileType.GamerSimulation => ("ProfileGamerSimulation", "ProfileSimGamerGain", "ProfileDescGamerSimulation", "#8B5CF6"),
            IntelligentProfileType.GamerMMO => ("ProfileGamerMMO", "ProfileMmoGamerGain", "ProfileDescGamerMMO", "#FACC15"),
            IntelligentProfileType.GamerStrategy => ("ProfileGamerStrategy", "ProfileStratGamerGain", "ProfileDescGamerStrategy", "#EF4444"),
            IntelligentProfileType.WorkOffice => ("ProfileWorkOffice", "ProfileOfficeGain", "ProfileDescWorkOffice", "#3B82F6"),
            IntelligentProfileType.CreativeVideoEditing => ("ProfileCreativeVideoEditing", "ProfileDesignGain", "ProfileDescCreativeVideoEditing", "#EC4899"),
            IntelligentProfileType.DeveloperProgramming => ("ProfileDeveloperProgramming", "ProfileDevGain", "ProfileDescDeveloperProgramming", "#06B6D4"),
            IntelligentProfileType.GeneralBalanced => ("ProfileGeneralBalanced", "ProfileGeneralGain", "ProfileDescGeneralBalanced", "#8B31FF"),
            IntelligentProfileType.EnterpriseSecure => ("ProfileEnterpriseSecure", "ProfileEnterpriseGain", "ProfileDescEnterpriseSecure", "#10B981"),
            _ => ("ProfileGeneralBalanced", "ProfileGeneralGain", "ProfileDescGeneralBalanced", "#8B31FF")
        };

        public static string GetProfileGlyph(IntelligentProfileType p) => p switch
        {
            IntelligentProfileType.GamerCompetitive => "🎯",
            IntelligentProfileType.GamerSinglePlayer => "🎮",
            IntelligentProfileType.GamerSimulation => "🏎️",
            IntelligentProfileType.GamerMMO => "🧙",
            IntelligentProfileType.GamerStrategy => "♟️",
            IntelligentProfileType.WorkOffice => "💼",
            IntelligentProfileType.CreativeVideoEditing => "🎬",
            IntelligentProfileType.DeveloperProgramming => "💻",
            IntelligentProfileType.GeneralBalanced => "⚖️",
            IntelligentProfileType.EnterpriseSecure => "🛡️",
            _ => "⚙️"
        };

        public static Brush BrushFromHex(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return Brushes.Transparent;
            if (BrushCache.TryGetValue(hex, out var cached)) return cached;

            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            BrushCache[hex] = brush;
            return brush;
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Brush> BrushCache = new();
    }
}