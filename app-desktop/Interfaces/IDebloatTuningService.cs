using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VoltrisOptimizer.Interfaces
{
    public interface IDebloatTuningService
    {
        Task<List<AppInfo>> GetInstalledAppsAsync(bool uninstallableOnly);
        Task<bool> UninstallAppAsync(string appName, bool isWin32, CancellationToken cancellationToken = default);
        Task<bool> RemoveTempFilesAsync();
    }

    public class AppInfo : INotifyPropertyChanged
    {
        public string Name { get; set; } = string.Empty;
        
        private string _iconPath = string.Empty;
        public string IconPath 
        { 
            get => _iconPath; 
            set { _iconPath = value; OnPropertyChanged(); } 
        }

        public bool IsWin32 { get; set; }
        
        // Advanced Metadata
        public string? DisplayIcon { get; set; }
        public string? Publisher { get; set; }
        public string? Version { get; set; }
        public string? InstallLocation { get; set; }
        public string? UninstallString { get; set; }
        public string? QuietUninstallString { get; set; }
        public string? RegistryPath { get; set; }
        public string? Guid { get; set; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    // Não permitir seleção se estiver bloqueado
                    if (value && IsSelectionLocked) return;
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _isSelectionLocked;
        /// <summary>
        /// Quando true, bloqueia a seleção/desseleção do checkbox.
        /// Usado durante a desinstalação para impedir que o usuário altere a seleção.
        /// </summary>
        public bool IsSelectionLocked
        {
            get => _isSelectionLocked;
            set
            {
                if (_isSelectionLocked != value)
                {
                    _isSelectionLocked = value;
                    OnPropertyChanged();
                    // IsEnabled do checkbox é o inverso
                    OnPropertyChanged(nameof(IsCheckboxEnabled));
                }
            }
        }

        /// <summary>Controla IsEnabled do CheckBox no XAML.</summary>
        public bool IsCheckboxEnabled => !_isSelectionLocked;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
