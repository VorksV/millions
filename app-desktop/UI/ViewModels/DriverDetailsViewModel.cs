using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using VoltrisOptimizer.Services.Drivers;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.UI.ViewModels
{
    public class DriverDetailsViewModel : INotifyPropertyChanged
    {
        private bool _isOpen;
        private DriverUpdate _selectedDriver;
        private bool _isTransparencyEnabled;

        public bool IsOpen
        {
            get => _isOpen;
            set => SetProperty(ref _isOpen, value);
        }

        public DriverUpdate SelectedDriver
        {
            get => _selectedDriver;
            set => SetProperty(ref _selectedDriver, value);
        }

        public bool IsTransparencyEnabled
        {
            get => _isTransparencyEnabled;
            set => SetProperty(ref _isTransparencyEnabled, value);
        }

        public string CurrentDriverVersion => SelectedDriver?.Device?.DriverVersion ?? LocalizationService.Instance.GetString("Unknown");
        public string CurrentDriverDate => SelectedDriver?.Device?.DriverInstallDate?.ToString("dd/MM/yyyy") ?? LocalizationService.Instance.GetString("UnknownDate");
        public string NewDriverVersion => SelectedDriver?.NewDriver?.Version ?? LocalizationService.Instance.GetString("UnknownVersion");
        public string NewDriverDate => SelectedDriver?.NewDriver?.ReleaseDate.ToString("dd/MM/yyyy") ?? LocalizationService.Instance.GetString("UnknownDate");
        public string DriverName => SelectedDriver?.Device?.DeviceName ?? LocalizationService.Instance.GetString("UnknownDriver");
        public string VendorName => SelectedDriver?.Device?.Vendor ?? LocalizationService.Instance.GetString("UnknownVendor");
        public string UpdateReason 
        { 
            get 
            { 
                var reason = SelectedDriver?.UpdateReason ?? VoltrisOptimizer.Services.Drivers.UpdateReason.NoUpdateNeeded;
                return GetUpdateReasonDescription(reason);
            } 
        }
        public string StatusText => LocalizationService.Instance.GetString(SelectedDriver?.NewDriver != null ? "Outdated" : "Updated");
        public string StatusColor => SelectedDriver?.NewDriver != null ? "#FF6B35" : "#10B981";
        public string Changelog => SelectedDriver?.NewDriver?.Changelog ?? LocalizationService.Instance.GetString("ChangelogNotAvailable");
        public string SourceUrl => SelectedDriver?.NewDriver?.SourceUrl ?? SelectedDriver?.NewDriver?.DownloadUrl ?? "#";
        public string FileSize => FormatFileSize(SelectedDriver?.NewDriver?.FileSize ?? 0);
        public string ReleaseNotes => SelectedDriver?.NewDriver?.ReleaseNotes ?? LocalizationService.Instance.GetString("ReleaseNotesNotAvailable");

        public event PropertyChangedEventHandler PropertyChanged;

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value)) return false;
            var oldVal = field?.ToString() ?? "null";
            var newVal = value?.ToString() ?? "null";
            Debug.WriteLine($"[DriverDetailsViewModel] SetProperty - '{propertyName}' changing");
            App.LoggingService?.LogDebug($"[DriverDetailsViewModel] SetProperty - '{propertyName}' [{oldVal} -> {newVal}]");
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            Debug.WriteLine($"[DriverDetailsViewModel] OnPropertyChanged - '{propertyName}' raised");
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private string GetUpdateReasonDescription(VoltrisOptimizer.Services.Drivers.UpdateReason reason)
        {
            var sw = Stopwatch.StartNew();
            Debug.WriteLine($"[DriverDetailsViewModel] GetUpdateReasonDescription - reason={reason}");
            App.LoggingService?.LogInfo($"[DriverDetailsViewModel] GetUpdateReasonDescription - reason={reason}");

            string result;
            switch (reason)
            {
                case VoltrisOptimizer.Services.Drivers.UpdateReason.MajorVersionUpgrade:
                    result = LocalizationService.Instance.GetString("DriverMajorUpgrade");
                    break;
                case VoltrisOptimizer.Services.Drivers.UpdateReason.StabilityImprovement:
                    result = LocalizationService.Instance.GetString("DriverStabilityImprovement");
                    break;
                case VoltrisOptimizer.Services.Drivers.UpdateReason.MinorUpdate:
                    result = LocalizationService.Instance.GetString("DriverMinorUpdate");
                    break;
                case VoltrisOptimizer.Services.Drivers.UpdateReason.SecurityPatch:
                    result = LocalizationService.Instance.GetString("DriverSecurityPatch");
                    break;
                case VoltrisOptimizer.Services.Drivers.UpdateReason.HardwareRepair:
                    result = LocalizationService.Instance.GetString("DriverHardwareRepair");
                    break;
                case VoltrisOptimizer.Services.Drivers.UpdateReason.NoUpdateNeeded:
                    result = LocalizationService.Instance.GetString("DriverNoUpdateNeeded");
                    break;
                default:
                    result = LocalizationService.Instance.GetString("Unknown");
                    break;
            }

            sw.Stop();
            Debug.WriteLine($"[DriverDetailsViewModel] GetUpdateReasonDescription - result='{result}' duration={sw.ElapsedMilliseconds}ms");
            App.LoggingService?.LogInfo($"[DriverDetailsViewModel] GetUpdateReasonDescription - result='{result}' duration={sw.ElapsedMilliseconds}ms");
            return result;
        }

        private string FormatFileSize(long bytes)
        {
            var sw = Stopwatch.StartNew();
            Debug.WriteLine($"[DriverDetailsViewModel] FormatFileSize - bytes={bytes}");
            App.LoggingService?.LogInfo($"[DriverDetailsViewModel] FormatFileSize - bytes={bytes}");

            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            string result = $"{len:0.##} {sizes[order]}";

            sw.Stop();
            Debug.WriteLine($"[DriverDetailsViewModel] FormatFileSize - result='{result}' duration={sw.ElapsedMilliseconds}ms");
            App.LoggingService?.LogInfo($"[DriverDetailsViewModel] FormatFileSize - result='{result}' duration={sw.ElapsedMilliseconds}ms");
            return result;
        }

        /// <summary>Fecha o modal de detalhes do driver e limpa a seleção.</summary>
        public void CloseModal()
        {
            var sw = Stopwatch.StartNew();
            Debug.WriteLine($"[DriverDetailsViewModel] CloseModal - Enter");
            App.LoggingService?.LogInfo($"[DriverDetailsViewModel] CloseModal - Enter");
            IsOpen = false;
            SelectedDriver = null;
            sw.Stop();
            Debug.WriteLine($"[DriverDetailsViewModel] CloseModal - Exit duration={sw.ElapsedMilliseconds}ms");
            App.LoggingService?.LogInfo($"[DriverDetailsViewModel] CloseModal - Exit duration={sw.ElapsedMilliseconds}ms");
        }

        /// <summary>Abre o modal de detalhes para o driver especificado.</summary>
        /// <param name="driver">Driver update a ser exibido.</param>
        public void OpenModal(DriverUpdate driver)
        {
            var sw = Stopwatch.StartNew();
            string driverName = driver?.Device?.DeviceName ?? "null";
            Debug.WriteLine($"[DriverDetailsViewModel] OpenModal - Enter driver='{driverName}'");
            App.LoggingService?.LogInfo($"[DriverDetailsViewModel] OpenModal - Enter driver='{driverName}'");
            SelectedDriver = driver;
            IsOpen = true;
            sw.Stop();
            Debug.WriteLine($"[DriverDetailsViewModel] OpenModal - Exit duration={sw.ElapsedMilliseconds}ms");
            App.LoggingService?.LogInfo($"[DriverDetailsViewModel] OpenModal - Exit duration={sw.ElapsedMilliseconds}ms");
        }
    }
}
