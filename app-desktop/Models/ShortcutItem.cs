using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VoltrisOptimizer.Models
{
    public class ShortcutItem : INotifyPropertyChanged
    {
        private bool _isRegistered;

        public int Id { get; set; }
        public string KeyCombo { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string IconGeometry { get; set; } = string.Empty;

        public bool IsRegistered
        {
            get => _isRegistered;
            set { _isRegistered = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
