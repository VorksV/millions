using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services.Drivers;
using VoltrisOptimizer.Services;


namespace VoltrisOptimizer.UI.ViewModels
{
    public class NativeDriverScanViewModel : ViewModelBase, IDisposable
    {
        private readonly NativeDriverEngineService _engine;
        private readonly DriverUpdateService       _updateService;
        private CancellationTokenSource?           _cts;

        private bool   _isScanning;
        private double _progress;
        private string _statusMessage = LocalizationService.Instance.GetString("Ready");
        private string _engineInfo    = LocalizationService.Instance.GetString("Loading");

        public ObservableCollection<NativeDriverDeviceInfo>  AllDevices    { get; } = new();
        public ObservableCollection<DriverEngineLogEntry>    EngineLogs    { get; } = new();
        public ObservableCollection<DriverUpdate>            PendingUpdates { get; } = new();

        public bool   IsScanning     { get => _isScanning;     set { SetProperty(ref _isScanning, value); RaiseCommandStates(); } }
        public double Progress       { get => _progress;       set { SetProperty(ref _progress, value); } }
        public string StatusMessage  { get => _statusMessage;  set { SetProperty(ref _statusMessage, value); } }
        public string EngineInfo     { get => _engineInfo;     set { SetProperty(ref _engineInfo, value); } }

        public bool NativeEngineAvailable => _engine.IsAvailable;

        public ICommand ScanCommand   { get; }
        public ICommand CancelCommand { get; }
        public ICommand ExportLogsCommand { get; }

        public NativeDriverScanViewModel()
        {
            var sw = Stopwatch.StartNew();
            Debug.WriteLine("[NativeDriverScanViewModel] Constructor - Enter");
            App.LoggingService?.LogInfo("[NativeDriverScanViewModel] Constructor - Enter");

            _engine        = new NativeDriverEngineService();
            _updateService = new DriverUpdateService();

            EngineInfo = _engine.IsAvailable
                ? string.Format(LocalizationService.Instance.GetString("DriverEngineAvailable"), _engine.EngineVersion, Environment.Is64BitProcess ? "x64" : "x86")
                : LocalizationService.Instance.GetString("DriverEngineUnavailable");

            _engine.LogReceived += entry =>
            {
                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    EngineLogs.Add(entry);
                    if (EngineLogs.Count > 500) EngineLogs.RemoveAt(0);
                });
            };

            ScanCommand       = new AsyncRelayCommand(RunScanAsync, () => !IsScanning);
            CancelCommand     = new AsyncRelayCommand(() => Task.Run(CancelScan), () => IsScanning);
            ExportLogsCommand = new AsyncRelayCommand(ExportLogsAsync, () => !IsScanning);

            sw.Stop();
            Debug.WriteLine($"[NativeDriverScanViewModel] Constructor - Exit duration={sw.ElapsedMilliseconds}ms");
            App.LoggingService?.LogInfo($"[NativeDriverScanViewModel] Constructor - Exit duration={sw.ElapsedMilliseconds}ms");
        }

        private async Task RunScanAsync()
        {
            var sw = Stopwatch.StartNew();
            Debug.WriteLine("[NativeDriverScanViewModel] RunScanAsync - Enter");
            App.LoggingService?.LogInfo("[NativeDriverScanViewModel] RunScanAsync - Enter");

            _cts = new CancellationTokenSource();
            IsScanning = true;
            Progress   = 0;
            AllDevices.Clear();
            PendingUpdates.Clear();
            EngineLogs.Clear();

            GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("ScanningDevicesAndDrivers"), true);

            try
            {
                StatusMessage = LocalizationService.Instance.GetString("EnumeratingDevices");
                Debug.WriteLine("[NativeDriverScanViewModel] RunScanAsync - Starting C++ SetupAPI enumeration");
                App.LoggingService?.LogInfo("[NativeDriverScanVM] Starting C++ SetupAPI enumeration...");
                GlobalProgressService.Instance.UpdateProgress(5, LocalizationService.Instance.GetString("EnumeratingDevices"));

                var progressReport = new Progress<(int Phase, int Current, int Total, string Device)>(p =>
                {
                    Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        Progress      = p.Total > 0 ? (double)p.Current / p.Total * 60.0 : 0;
                        StatusMessage = string.Format(LocalizationService.Instance.GetString("DetectingDeviceProgress"), p.Current, p.Total, p.Device);
                    });
                });

                var devices = await _engine.EnumerateDevicesAsync(progressReport, _cts.Token);

                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    foreach (var d in devices) AllDevices.Add(d);
                });

                Debug.WriteLine($"[NativeDriverScanViewModel] RunScanAsync - SetupAPI found {devices.Count} devices");
                App.LoggingService?.LogInfo(
                    $"[NativeDriverScanVM] SetupAPI found {devices.Count} devices.");

                StatusMessage = string.Format(LocalizationService.Instance.GetString("CheckingDriverVersions"), devices.Count);
                Progress = 65;
                GlobalProgressService.Instance.UpdateProgress(65, string.Format(LocalizationService.Instance.GetString("CheckingDriverVersions"), devices.Count));

                int checked2 = 0;
                foreach (var dev in devices)
                {
                    if (_cts.IsCancellationRequested) break;
                    checked2++;
                    Progress = 65 + (double)checked2 / devices.Count * 30.0;

                    if (string.IsNullOrEmpty(dev.DriverVersion) || dev.HasProblem) continue;

                    if (dev.IsUnsigned)
                    {
                        Debug.WriteLine($"[NativeDriverScanViewModel] RunScanAsync - Warning Unsigned driver: {dev.Description} | {dev.InfPath}");
                        App.LoggingService?.LogWarning(
                            $"[NativeDriverScanVM] Warning Unsigned driver: {dev.Description} | {dev.InfPath}");
                    }
                }

                int unsigned    = devices.Count(d => d.SignatureResult >= DE_Sig.SelfSigned);
                int problem     = devices.Count(d => d.HasProblem);
                int noDriver    = devices.Count(d => string.IsNullOrEmpty(d.DriverVersion));

                Progress      = 100;
                StatusMessage = string.Format(LocalizationService.Instance.GetString("ScanSummary"), devices.Count, problem, unsigned);

                Debug.WriteLine($"[NativeDriverScanViewModel] RunScanAsync - Scan completo — {devices.Count} devices, {problem} problemas, {unsigned} sem assinatura WHQL");
                App.LoggingService?.LogInfo(
                    $"[NativeDriverScanVM] Scan completo — {devices.Count} devices, " +
                    $"{problem} problemas, {unsigned} sem assinatura WHQL.");

                HistoryService.RecordActivity("Driver Scan", string.Format(LocalizationService.Instance.GetString("DriverScanHistoryRecord"), devices.Count), true);
                GlobalProgressService.Instance.UpdateProgress(100, LocalizationService.Instance.GetString("ScanComplete"));
                GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("ScanCompleteDevices"), devices.Count, problem));
                GlobalNotificationService.ShowSuccess(LocalizationService.Instance.GetString("DriverScan"), string.Format(LocalizationService.Instance.GetString("ScanCompleteNotification"), devices.Count, problem));

                sw.Stop();
                Debug.WriteLine($"[NativeDriverScanViewModel] RunScanAsync - Complete - duration={sw.ElapsedMilliseconds}ms");
                App.LoggingService?.LogInfo($"[NativeDriverScanVM] RunScanAsync - Complete - duration={sw.ElapsedMilliseconds}ms");
            }

            catch (OperationCanceledException)
            {
                StatusMessage = LocalizationService.Instance.GetString("ScanCancelled");
                GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("ScanCancelledOp"));
                Debug.WriteLine("[NativeDriverScanViewModel] RunScanAsync - Cancelled");
                App.LoggingService?.LogInfo("[NativeDriverScanVM] RunScanAsync - Cancelled by user");
            }
            catch (Exception ex)
            {
                StatusMessage = string.Format(LocalizationService.Instance.GetString("ScanError"), ex.Message);
                Debug.WriteLine($"[NativeDriverScanViewModel] RunScanAsync - Error: {ex.Message}");
                App.LoggingService?.LogError("[NativeDriverScanVM] Scan error.", ex);
                GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("ScanErrorOp"), ex.Message));
                GlobalNotificationService.ShowError(LocalizationService.Instance.GetString("DriverScan"), string.Format(LocalizationService.Instance.GetString("ScanErrorMsg"), ex.Message));
            }
            finally
            {
                IsScanning = false;
                _cts?.Dispose();
                _cts = null;
                Debug.WriteLine("[NativeDriverScanViewModel] RunScanAsync - Finally complete");
            }
        }

        private void CancelScan()
        {
            Debug.WriteLine("[NativeDriverScanViewModel] CancelScan - Enter");
            App.LoggingService?.LogInfo("[NativeDriverScanViewModel] CancelScan - Enter");
            _cts?.Cancel();
            Debug.WriteLine("[NativeDriverScanViewModel] CancelScan - Exit");
            App.LoggingService?.LogInfo("[NativeDriverScanViewModel] CancelScan - Exit");
        }

        private async Task ExportLogsAsync()
        {
            var sw = Stopwatch.StartNew();
            Debug.WriteLine("[NativeDriverScanViewModel] ExportLogsAsync - Enter");
            App.LoggingService?.LogInfo("[NativeDriverScanViewModel] ExportLogsAsync - Enter");

            GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("ExportingScanLogs"), false);
            try
            {
                string path = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    $"VoltrisDriverEngine_{DateTime.Now:yyyyMMdd_HHmmss}.log");

                Debug.WriteLine($"[NativeDriverScanViewModel] ExportLogsAsync - Exporting to: {path}");
                App.LoggingService?.LogInfo($"[NativeDriverScanViewModel] ExportLogsAsync - Exporting to: {path}");

                GlobalProgressService.Instance.UpdateProgress(50, LocalizationService.Instance.GetString("ExportingLogs"));
                bool ok = await Task.Run(() => _engine.ExportLogs(path));

                if (ok)
                {
                    StatusMessage = string.Format(LocalizationService.Instance.GetString("LogsExportedTo"), path);
                    HistoryService.RecordActivity("Driver Scan", LocalizationService.Instance.GetString("DriverExportHistoryRecord"), true);
                    GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("LogsExportedOp"), path));
                    GlobalNotificationService.ShowSuccess(LocalizationService.Instance.GetString("Export"), string.Format(LocalizationService.Instance.GetString("LogsExportedTo"), path));
                    Debug.WriteLine($"[NativeDriverScanViewModel] ExportLogsAsync - Success: {path}");
                    App.LoggingService?.LogSuccess($"[NativeDriverScanViewModel] ExportLogsAsync - Success: {path}");
                }
                else
                {
                    StatusMessage = LocalizationService.Instance.GetString("ExportFailed");
                    GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("ExportFailedOp"));
                    Debug.WriteLine("[NativeDriverScanViewModel] ExportLogsAsync - Failed");
                    App.LoggingService?.LogError("[NativeDriverScanViewModel] ExportLogsAsync - Failed");
                }
            }
            catch (Exception ex)
            {
                StatusMessage = string.Format(LocalizationService.Instance.GetString("ExportError"), ex.Message);
                GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("ExportErrorOp"), ex.Message));
                Debug.WriteLine($"[NativeDriverScanViewModel] ExportLogsAsync - Error: {ex.Message}");
                App.LoggingService?.LogError($"[NativeDriverScanViewModel] ExportLogsAsync - Error: {ex.Message}", ex);
            }

            sw.Stop();
            Debug.WriteLine($"[NativeDriverScanViewModel] ExportLogsAsync - Exit duration={sw.ElapsedMilliseconds}ms");
            App.LoggingService?.LogInfo($"[NativeDriverScanViewModel] ExportLogsAsync - Exit duration={sw.ElapsedMilliseconds}ms");
        }


        private void RaiseCommandStates()
        {
            Debug.WriteLine("[NativeDriverScanViewModel] RaiseCommandStates - Enter");
            ((AsyncRelayCommand)ScanCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)CancelCommand).RaiseCanExecuteChanged();
            Debug.WriteLine("[NativeDriverScanViewModel] RaiseCommandStates - Exit");
        }

        /// <summary>Libera os recursos do engine de scan nativo e do token de cancelamento.</summary>
        public void Dispose()
        {
            Debug.WriteLine("[NativeDriverScanViewModel] Dispose - Enter");
            App.LoggingService?.LogInfo("[NativeDriverScanViewModel] Dispose - Enter");
            _engine.Dispose();
            _cts?.Dispose();
            Debug.WriteLine("[NativeDriverScanViewModel] Dispose - Exit");
            App.LoggingService?.LogInfo("[NativeDriverScanViewModel] Dispose - Exit");
        }
    }
}
