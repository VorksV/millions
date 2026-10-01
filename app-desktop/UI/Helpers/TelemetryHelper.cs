using System.Windows.Controls;

namespace VoltrisOptimizer.UI.Helpers
{
    /// <summary>
    /// TelemetryHelper — sistema de telemetria removido.
    /// Mantido como stub para compatibilidade com código existente.
    /// </summary>
    public static class TelemetryHelper
    {
        public static void TrackPageView(this UserControl view, string pageName) { }
        public static void TrackFeature(string featureName, string actionName, bool success = true, object? metadata = null) { }
        public static void TrackButtonClick(string buttonName, string featureName) { }
        public static void TrackOptimizationStart(string optimizationType) { }
        public static void TrackOptimizationEnd(string optimizationType, bool success, object? metadata = null) { }
        public static void TrackGameSelect(string gameName) { }
        public static void TrackAIQuery(string query, string context = "Dashboard") { }
    }
}
