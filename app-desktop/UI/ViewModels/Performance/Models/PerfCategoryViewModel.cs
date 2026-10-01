using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Input;

namespace VoltrisOptimizer.UI.ViewModels.Performance.Models
{
    /// <summary>
    /// ViewModel para uma categoria de otimização de desempenho
    /// </summary>
    public class PerfCategoryViewModel : INotifyPropertyChanged
    {
        private bool _isExpanded;
        private string _recommendedCountText = "";

        public PerfCategoryViewModel()
        {
            // Logging para cada categoria criada na UI (para auditoria de carregamento de recursos)
            App.LoggingService?.LogTrace($"[PERF_MODEL] Categoria de performance criada");
        }

        public string Name { get; set; } = "";
        public string Icon { get; set; } = "";
        public string IconPath { get; set; } = "";
        public string Description { get; set; } = "";
        public Color CategoryColor { get; set; }
        public ObservableCollection<PerfOptimizationViewModel> Optimizations { get; set; } = new();

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged(nameof(IsExpanded));
                    OnPropertyChanged(nameof(ExpandedVisibility));
                }
            }
        }

        public Visibility ExpandedVisibility => IsExpanded ? Visibility.Visible : Visibility.Collapsed;

        public string RecommendedCountText
        {
            get => _recommendedCountText;
            set
            {
                if (_recommendedCountText != value)
                {
                    _recommendedCountText = value;
                    OnPropertyChanged(nameof(RecommendedCountText));
                }
            }
        }

        private bool _isWorking;
        private string _statusText = "";

        public ICommand? ApplyCommand { get; set; }
        public ICommand? RevertCommand { get; set; }

        public bool IsWorking
        {
            get => _isWorking;
            set
            {
                if (_isWorking != value)
                {
                    _isWorking = value;
                    OnPropertyChanged(nameof(IsWorking));
                    OnPropertyChanged(nameof(IsNotWorking));
                }
            }
        }

        public bool IsNotWorking => !IsWorking;

        public bool? IsSelectedAll
        {
            get
            {
                if (Optimizations.Count == 0) return false;
                
                bool allSelected = true;
                bool allUnselected = true;

                foreach (var opt in Optimizations)
                {
                    if (opt.IsSelected) allUnselected = false;
                    else allSelected = false;
                }

                if (allSelected) return true;
                if (allUnselected) return false;
                return null; // Indeterminado (mix de selecionadas/não selecionadas)
            }
            set
            {
                if (value.HasValue)
                {
                    foreach (var opt in Optimizations)
                    {
                        opt.IsSelected = value.Value;
                    }
                    OnPropertyChanged(nameof(IsSelectedAll));
                }
            }
        }

        public string StatusText
        {
            get => _statusText;
            set
            {
                if (_statusText != value)
                {
                    _statusText = value;
                    OnPropertyChanged(nameof(StatusText));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

