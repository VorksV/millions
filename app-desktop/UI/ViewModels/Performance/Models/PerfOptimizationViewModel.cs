using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Performance.Models;
using VoltrisOptimizer.UI.ViewModels;

namespace VoltrisOptimizer.UI.ViewModels.Performance.Models
{
    /// <summary>
    /// ViewModel para uma otimização individual de desempenho
    /// </summary>
    public class PerfOptimizationViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;
        private bool _isEnabled = true;
        private bool _isApplied;
        private bool _isWorking;
        private string? _incompatibilityReason;

        public string Name { get; set; } = "";
        public string BackendName { get; set; } = "";
        public string Description { get; set; } = "";
        public bool IsRecommended { get; set; }
        public OptimizationImpact Impact { get; set; }
        public Action? ApplyAction { get; set; }
        public Action? RevertAction { get; set; }

        public ICommand ApplyIndividualCommand { get; }
        public ICommand RevertIndividualCommand { get; }

        public PerfOptimizationViewModel()
        {
            ApplyIndividualCommand = new AsyncRelayCommand(ApplyIndividualAsync, () => !IsWorking && ApplyAction != null);
            RevertIndividualCommand = new AsyncRelayCommand(RevertIndividualAsync, () => !IsWorking && RevertAction != null);
        }

        private async Task ApplyIndividualAsync()
        {
            // SaaS-LEVEL: Feature Gate centralizado para bloqueio profissional
            App.LoggingService?.LogInfo($"[PerfOptimizationVM] INICIANDO VERIFICAÇÃO DE FEATURE GATE PARA APLICAÇÃO INDIVIDUAL: {Name}");
            
            var isOptimizationEnabled = VoltrisOptimizer.Services.License.LicenseOrchestrationService.Features.IsOptimizationEnabled;
            App.LoggingService?.LogInfo($"[PerfOptimizationVM] Feature Gate Result: IsOptimizationEnabled={isOptimizationEnabled}");
            
            if (!isOptimizationEnabled)
            {
                App.LoggingService?.LogWarning($"[PerfOptimizationVM] FEATURE GATE BLOQUEADO - Aplicação individual não está habilitada: {Name}");
                
                var licenseState = await VoltrisOptimizer.Services.License.LicenseOrchestrationService.Instance.GetCurrentStateAsync();
                App.LoggingService?.LogInfo($"[PerfOptimizationVM] Estado: {licenseState.FormattedStatus} ({licenseState.LicenseType})");
                
                var message = string.Format(LocalizationService.Instance.GetString("FeatureUnavailable"), licenseState.FormattedStatus);
                
                VoltrisOptimizer.UI.Controls.LicenseRequiredMessageBox.Show(
                    message,
                    LocalizationService.Instance.GetString("LicenseRequired"));
                
                App.LoggingService?.LogInfo($"[PerfOptimizationVM] Aplicação individual BLOQUEADA: {Name}");
                return;
            }
            
            App.LoggingService?.LogInfo($"[PerfOptimizationVM] FEATURE GATE APROVADO - Aplicação individual habilitada: {Name}");

            if (ApplyAction == null || IsWorking) return;
            try
            {
                IsWorking = true;
                await Task.Run(() => ApplyAction.Invoke());
                IsApplied = true;
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[PerfOptVM] Erro ao aplicar '{Name}': {ex.Message}", ex);
            }
            finally
            {
                IsWorking = false;
            }
        }

        private async Task RevertIndividualAsync()
        {
            // SaaS-LEVEL: Feature Gate centralizado para bloqueio profissional
            App.LoggingService?.LogInfo($"[PerfOptimizationVM] INICIANDO VERIFICAÇÃO DE FEATURE GATE PARA REVERSÃO INDIVIDUAL: {Name}");
            
            var isOptimizationEnabled = VoltrisOptimizer.Services.License.LicenseOrchestrationService.Features.IsOptimizationEnabled;
            App.LoggingService?.LogInfo($"[PerfOptimizationVM] Feature Gate Result: IsOptimizationEnabled={isOptimizationEnabled}");
            
            if (!isOptimizationEnabled)
            {
                App.LoggingService?.LogWarning($"[PerfOptimizationVM] FEATURE GATE BLOQUEADO - Reversão individual não está habilitada: {Name}");
                
                var licenseState = await VoltrisOptimizer.Services.License.LicenseOrchestrationService.Instance.GetCurrentStateAsync();
                App.LoggingService?.LogInfo($"[PerfOptimizationVM] Estado: {licenseState.FormattedStatus} ({licenseState.LicenseType})");
                
                var message = string.Format(LocalizationService.Instance.GetString("FeatureUnavailable"), licenseState.FormattedStatus);
                
                VoltrisOptimizer.UI.Controls.LicenseRequiredMessageBox.Show(
                    message,
                    LocalizationService.Instance.GetString("LicenseRequired"));
                
                App.LoggingService?.LogInfo($"[PerfOptimizationVM] Reversão individual BLOQUEADA: {Name}");
                return;
            }
            
            App.LoggingService?.LogInfo($"[PerfOptimizationVM] FEATURE GATE APROVADO - Reversão individual habilitada: {Name}");

            if (RevertAction == null || IsWorking) return;
            try
            {
                IsWorking = true;
                await Task.Run(() => RevertAction.Invoke());
                IsApplied = false;
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[PerfOptVM] Erro ao reverter '{Name}': {ex.Message}", ex);
            }
            finally
            {
                IsWorking = false;
            }
        }

        public bool IsWorking
        {
            get => _isWorking;
            set
            {
                if (_isWorking != value)
                {
                    _isWorking = value;
                    OnPropertyChanged(nameof(IsWorking));
                }
            }
        }

        public bool IsApplied
        {
            get => _isApplied;
            set
            {
                if (_isApplied != value)
                {
                    _isApplied = value;
                    OnPropertyChanged(nameof(IsApplied));
                    OnPropertyChanged(nameof(AppliedVisibility));
                    OnPropertyChanged(nameof(NotAppliedVisibility));
                }
            }
        }

        public Visibility AppliedVisibility => IsApplied ? Visibility.Visible : Visibility.Collapsed;
        public Visibility NotAppliedVisibility => IsApplied ? Visibility.Collapsed : Visibility.Visible;
        
        /// <summary>
        /// Indica se a otimização está habilitada (compatível com hardware)
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    OnPropertyChanged(nameof(IsEnabled));
                }
            }
        }
        
        /// <summary>
        /// Razão da incompatibilidade (se houver)
        /// </summary>
        public string? IncompatibilityReason
        {
            get => _incompatibilityReason;
            set
            {
                if (_incompatibilityReason != value)
                {
                    _incompatibilityReason = value;
                    OnPropertyChanged(nameof(IncompatibilityReason));
                    OnPropertyChanged(nameof(HasIncompatibilityReason));
                }
            }
        }
        
        /// <summary>
        /// Indica se há razão de incompatibilidade
        /// </summary>
        public bool HasIncompatibilityReason => !string.IsNullOrEmpty(_incompatibilityReason);

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        public string ImpactText => Impact switch
        {
            OptimizationImpact.High => "ALTO",
            OptimizationImpact.Medium => "MÉDIO",
            OptimizationImpact.Low => "BAIXO",
            _ => "?"
        };

        public SolidColorBrush ImpactColor => Impact switch
        {
            OptimizationImpact.High => new SolidColorBrush(Color.FromRgb(16, 185, 129)),
            OptimizationImpact.Medium => new SolidColorBrush(Color.FromRgb(245, 158, 11)),
            OptimizationImpact.Low => new SolidColorBrush(Color.FromRgb(107, 114, 128)),
            _ => new SolidColorBrush(Colors.Gray)
        };

        public Visibility RecommendedVisibility => IsRecommended ? Visibility.Visible : Visibility.Collapsed;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
