using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using VoltrisOptimizer.Services.SmartRepair; // for StepStatus

namespace VoltrisOptimizer.UI.ViewModels
{
    public class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class SmartRepairModuleItem : ObservableObject
    {
        public string ModuleId { get; set; } = string.Empty;
        
        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value; OnPropertyChanged(); } }
        }

        private string _description = string.Empty;
        public string Description
        {
            get => _description;
            set { if (_description != value) { _description = value; OnPropertyChanged(); } }
        }

        private StepStatus _status = StepStatus.Pending;
        public StepStatus Status
        {
            get => _status;
            set { if (_status != value) { _status = value; OnPropertyChanged(); } }
        }

        private int _progressPercent;
        public int ProgressPercent
        {
            get => _progressPercent;
            set { if (_progressPercent != value) { _progressPercent = value; OnPropertyChanged(); } }
        }

        private long _itemsFound;
        public long ItemsFound
        {
            get => _itemsFound;
            set { if (_itemsFound != value) { _itemsFound = value; OnPropertyChanged(); } }
        }

        private long _spaceRecoveredBytes;
        public long SpaceRecoveredBytes
        {
            get => _spaceRecoveredBytes;
            set { if (_spaceRecoveredBytes != value) { _spaceRecoveredBytes = value; OnPropertyChanged(); } }
        }

        public ObservableCollection<SmartRepairCategoryItem> Categories { get; } = new();
    }

    public class SmartRepairCategoryItem : ObservableObject
    {
        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value; OnPropertyChanged(); } }
        }

        public ObservableCollection<SmartRepairOperationItem> Operations { get; } = new();
    }

    public class SmartRepairOperationItem : ObservableObject
    {
        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value; OnPropertyChanged(); } }
        }

        private StepStatus _status = StepStatus.Pending;
        public StepStatus Status
        {
            get => _status;
            set { if (_status != value) { _status = value; OnPropertyChanged(); } }
        }
    }
}
