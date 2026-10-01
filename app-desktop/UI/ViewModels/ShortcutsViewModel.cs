using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using VoltrisOptimizer.Models;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.UI.ViewModels
{
    public class ShortcutsViewModel : INotifyPropertyChanged
    {
        public ShortcutsViewModel(HotkeyService? hotkeyService)
        {
            var items = hotkeyService?.DefaultShortcuts ?? new System.Collections.Generic.List<ShortcutItem>();
            foreach (var item in items)
            {
                var key = GetLocalizationKey(item.Id);
                item.Description = LocalizationService.Instance[key]?.ToString() ?? item.KeyCombo;
                Shortcuts.Add(item);
            }
        }

        public ObservableCollection<ShortcutItem> Shortcuts { get; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private static string GetLocalizationKey(int id) => id switch
        {
            // Navigation
            1  => "ShortcutAltDashboard",
            2  => "ShortcutAltCleanup",
            3  => "ShortcutAltPerformance",
            4  => "ShortcutAltGamer",
            5  => "ShortcutAltRepair",
            6  => "ShortcutAltShield",
            7  => "ShortcutAltSettings",
            // Gamer Mode
            8  => "ShortcutActivateGamer",
            9  => "ShortcutDeactivateGamer",
            10 => "ShortcutToggleFps",
            // Widget
            11 => "ShortcutToggleWidget",
            // Maintenance
            12 => "ShortcutSmartScan",
            13 => "ShortcutQuickCleanup",
            14 => "ShortcutPerformanceBoost",
            15 => "ShortcutSmartRepair",
            // Network
            16 => "ShortcutPingTest",
            17 => "ShortcutFlushDns",
            // Security
            18 => "ShortcutQuickScan",
            19 => "ShortcutFullScan",
            // Utilities
            20 => "ShortcutQuickOptimize",
            21 => "ShortcutRestorePoint",
            _  => "ShortcutUnknown",
        };
    }
}
