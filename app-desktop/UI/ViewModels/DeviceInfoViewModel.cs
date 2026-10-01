using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.UI.ViewModels
{
    public class DeviceInfoViewModel : ViewModelBase
    {
        private readonly ISystemInfoService _systemInfoService;
        private readonly ILoggingService? _logger;
        private readonly IDisposable _powerTimerHandle;
        private bool _isLaptop;

        private bool _isLoading = true;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        #region Hardware Properties

        private string _osName = LocalizationService.Instance.GetString("Device_Detecting");
        public string OsName { get => _osName; set => SetProperty(ref _osName, value); }

        private string _osVersion = "...";
        public string OsVersion { get => _osVersion; set => SetProperty(ref _osVersion, value); }

        private string _cpuName = LocalizationService.Instance.GetString("Device_Detecting");
        public string CpuName { get => _cpuName; set => SetProperty(ref _cpuName, value); }

        private string _cpuDetails = "...";
        public string CpuDetails { get => _cpuDetails; set => SetProperty(ref _cpuDetails, value); }

        private string _gpuName = LocalizationService.Instance.GetString("Device_Detecting");
        public string GpuName { get => _gpuName; set => SetProperty(ref _gpuName, value); }

        private string _gpuDetails = "...";
        public string GpuDetails { get => _gpuDetails; set => SetProperty(ref _gpuDetails, value); }

        private string _ramTotal = LocalizationService.Instance.GetString("Device_Detecting");
        public string RamTotal { get => _ramTotal; set => SetProperty(ref _ramTotal, value); }

        private string _ramType = "DDRx"; // Fallback as WMI is flaky for RAM type
        public string RamType { get => _ramType; set => SetProperty(ref _ramType, value); }

        private ObservableCollection<DriveInfoModel> _drives = new();
        public ObservableCollection<DriveInfoModel> Drives { get => _drives; set => SetProperty(ref _drives, value); }

        private ObservableCollection<NetworkInfoModel> _networks = new();
        public ObservableCollection<NetworkInfoModel> Networks { get => _networks; set => SetProperty(ref _networks, value); }

        private string _batteryStatus = LocalizationService.Instance.GetString("Device_NotDetected");
        public string BatteryStatus { get => _batteryStatus; set => SetProperty(ref _batteryStatus, value); }

        #endregion

        public DeviceInfoViewModel(ISystemInfoService systemInfoService, ILoggingService? logger = null)
        {
            _systemInfoService = systemInfoService;
            _logger = logger;

            // Timer para atualizar status de energia em tempo real (a cada 3 segundos)
            // Use consolidated scheduler instead of a dedicated DispatcherTimer
            _powerTimerHandle = UiScheduler.Register(RefreshPowerStatus, TimeSpan.FromSeconds(3));

            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;

            _ = LoadDeviceInfoAsync();
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                _ = LoadDeviceInfoAsync();
            });
        }

        private void RefreshPowerStatus()
        {
            try
            {
                if (!_isLaptop) return;
                bool onBattery = _systemInfoService.IsOnBattery();
                BatteryStatus = onBattery ? LocalizationService.Instance.GetString("Device_OnBattery") : LocalizationService.Instance.GetString("Device_PluggedIn");
            }
            catch { }
        }

        public void Dispose()
        {
            _powerTimerHandle?.Dispose();
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        }

        public async Task LoadDeviceInfoAsync()
        {
            try
            {
                IsLoading = true;
                
                // OS Info
                OsName = _systemInfoService.GetWindowsVersion();
                OsVersion = $"Build {_systemInfoService.GetWindowsBuild()} ({(_systemInfoService.Is64BitOperatingSystem() ? "64-bit" : "32-bit")})";

                // CPU Info
                var cpu = await _systemInfoService.GetCpuInfoAsync();
                CpuName = cpu.Name;
                CpuDetails = $"{cpu.CoreCount} Cores / {cpu.ThreadCount} Threads | {cpu.MaxClockSpeedMHz / 1000:0.##} GHz | Socket: {cpu.Socket}";
                if (cpu.L3CacheKB > 0)
                    CpuDetails += $" | L3 Cache: {cpu.L3CacheKB / 1024} MB";

                // GPU Info
                var gpus = await _systemInfoService.GetAllGpusAsync();
                if (gpus.Length > 0)
                {
                    var mainGpu = gpus.FirstOrDefault(g => g.IsDiscrete) ?? gpus[0];
                    GpuName = mainGpu.Name;
                    GpuDetails = $"{mainGpu.Vendor} | VRam: {FormatBytes(mainGpu.VideoMemoryBytes)} | Driver: {mainGpu.DriverVersion}";
                }

                // RAM Info
                var ram = await _systemInfoService.GetRamInfoAsync();
                RamTotal = $"{ram.TotalGB} GB Total";
                RamType = $"{ram.MemoryType} | {ram.SpeedMHz} MHz";

                // Drives Info
                var drives = await _systemInfoService.GetDrivesInfoAsync();
                Drives.Clear();
                foreach (var d in drives)
                {
                    Drives.Add(new DriveInfoModel
                    {
                        Label = string.IsNullOrEmpty(d.Label) ? LocalizationService.Instance.GetString("Device_LocalDisk") : d.Label,
                        Letter = d.Letter,
                        TotalSize = FormatBytes(d.TotalBytes),
                        FreeSpace = FormatBytes(d.FreeBytes),
                        Type = d.IsSsd ? "SSD" : "HDD",
                        Model = string.IsNullOrWhiteSpace(d.Model) ? (d.IsSsd ? "SSD" : "HDD") : d.Model,
                        UsagePercent = (double)(d.TotalBytes - d.FreeBytes) / d.TotalBytes * 100
                    });
                }

                // Network Info
                var networks = await _systemInfoService.GetNetworkInfoAsync();
                Networks.Clear();
                foreach (var n in networks.Where(net => net.IsEnabled))
                {
                    Networks.Add(new NetworkInfoModel
                    {
                        Name = n.Name,
                        Mac = n.MacAddress,
                        Speed = n.SpeedBps > 0 ? $"{n.SpeedBps / 1000000} Mbps" : LocalizationService.Instance.GetString("Device_Unknown")
                    });
                }

                // Battery Info
                _isLaptop = _systemInfoService.IsLaptop();
                if (_isLaptop)
                {
                    bool onBattery = _systemInfoService.IsOnBattery();
                    BatteryStatus = onBattery ? LocalizationService.Instance.GetString("Device_OnBattery") : LocalizationService.Instance.GetString("Device_PluggedIn");
                }
                else
                {
                    BatteryStatus = LocalizationService.Instance.GetString("Device_DesktopNoBattery");
                    _powerTimerHandle?.Dispose(); // Desktop não precisa de polling
                }

                IsLoading = false;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Erro ao carregar informações do dispositivo: {ex.Message}", ex);
                IsLoading = false;
            }
        }

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }

    public class DriveInfoModel
    {
        public string Label { get; set; } = string.Empty;
        public string Letter { get; set; } = string.Empty;
        public string TotalSize { get; set; } = string.Empty;
        public string FreeSpace { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public double UsagePercent { get; set; }
    }

    public class NetworkInfoModel
    {
        public string Name { get; set; } = string.Empty;
        public string Mac { get; set; } = string.Empty;
        public string Speed { get; set; } = string.Empty;
    }
}
