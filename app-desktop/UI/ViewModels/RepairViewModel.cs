using System;
using System.Windows.Input;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Utils;
using VoltrisOptimizer.UI.Controls;
using VoltrisOptimizer.UI.Windows;

namespace VoltrisOptimizer.UI.ViewModels
{
    public class LogEntry
    {
        public string Message { get; set; } = string.Empty;
        public SolidColorBrush Color { get; set; } = Brushes.Gray;
    }

    public class RepairViewModel : ViewModelBase
    {
        private readonly ILoggingService _logger;
        private readonly AdvancedRepairService _repairService;
        private CancellationTokenSource? _cts;
        
        public ObservableCollection<LogEntry> Logs { get; } = new();
        
        private string _osVersion = "Windows";
        public string OsVersion
        {
            get => _osVersion;
            set => SetProperty(ref _osVersion, value);
        }

        private double _progressValue;
        public double ProgressValue
        {
            get => _progressValue;
            set => SetProperty(ref _progressValue, value);
        }

        private string _progressStatus = LocalizationService.Instance.GetString("RepairReady");
        public string ProgressStatus
        {
            get => _progressStatus;
            set => SetProperty(ref _progressStatus, value);
        }

        public ICommand FullRepairCommand { get; }
        public ICommand RepairSystemCommand { get; }
        public ICommand StartDefragCommand { get; }
        public ICommand DiskCleanupCommand { get; }

        public RepairViewModel()
        {
            _logger = App.LoggingService ?? new LoggingService(LogDirectoryResolver.Resolve());
            _repairService = new AdvancedRepairService(_logger);
            
            FullRepairCommand = new AsyncRelayCommand(ExecuteFullRepairAsync, () => !IsBusy);
            RepairSystemCommand = new AsyncRelayCommand(ExecuteRepairSystemAsync, () => !IsBusy);
            StartDefragCommand = new AsyncRelayCommand(ExecuteDefragAsync, () => !IsBusy);
            DiskCleanupCommand = new RelayCommand(ExecuteDiskCleanup);

            LoadSystemInfo();
            InitializeLogCapture();
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            var ready = LocalizationService.Instance.GetString("RepairReady");
            if (_progressStatus == "Pronto para Reparar" ||
                _progressStatus == "Listo para Reparar" ||
                _progressStatus == "Ready to Repair" ||
                _progressStatus == ready)
            {
                ProgressStatus = ready;
            }
        }

        private void InitializeLogCapture()
        {
            if (_logger is LoggingService logSvc)
            {
                logSvc.LogEntryAdded += (s, msg) => 
                {
                    if (msg.Contains("[Repair]") || msg.Contains("[DISM]") || msg.Contains("[SFC]") ||
                        msg.Contains("[CHKDSK]") || msg.Contains("[AdvancedRepair]"))
                    {
                        var color = msg.Contains("SUCCESS") ? Colors.LightGreen :
                                    msg.Contains("WARNING") ? Colors.Orange :
                                    msg.Contains("ERROR") ? Colors.Red : Colors.Gray;
                        
                        AddLog(msg, color);
                    }
                };
            }
            
            _repairService.OnLog = (msg, hexColor) =>
            {
                Color color = Colors.Gray;
                try { color = (Color)ColorConverter.ConvertFromString(hexColor); } catch { }
                AddLog(msg, color);
            };

            _repairService.OnProgress = (pct, msg) => 
            {
                ProgressValue = pct;
                ProgressStatus = msg;
                GlobalProgressService.Instance.UpdateProgress(pct, msg);
            };
        }

        private void AddLog(string message, Color color)
        {
            Application.Current.Dispatcher.BeginInvoke(() => 
            {
                Logs.Add(new LogEntry { Message = $"[{DateTime.Now:HH:mm:ss}] {message}", Color = new SolidColorBrush(color) });
                if (Logs.Count > 300) Logs.RemoveAt(0);
            });
        }

        private void LoadSystemInfo()
        {
            try
            {
                var os = Environment.OSVersion;
                string name = os.Version.Major == 10 ? (os.Version.Build >= 22000 ? "Windows 11" : "Windows 10") : "Windows";
                OsVersion = $"{name} ({os.Version.Build})";
            }
            catch { OsVersion = LocalizationService.Instance.GetString("WindowsUnknown"); }
        }

        private async Task ExecuteFullRepairAsync()
        {
            // SaaS-LEVEL: Feature Gate centralizado para bloqueio profissional
            _logger?.LogInfo("[RepairVM] INICIANDO VERIFICAÇÃO DE FEATURE GATE PARA REPARO COMPLETO...");
            
            var isRepairEnabled = VoltrisOptimizer.Services.License.LicenseOrchestrationService.Features.IsRepairEnabled;
            _logger?.LogInfo($"[RepairVM] Feature Gate Result: IsRepairEnabled={isRepairEnabled}");
            
            if (!isRepairEnabled)
            {
                _logger?.LogWarning("[RepairVM] FEATURE GATE BLOQUEADO - Reparo completo não está habilitado");
                
                var licenseState = await VoltrisOptimizer.Services.License.LicenseOrchestrationService.Instance.GetCurrentStateAsync();
                _logger?.LogInfo($"[RepairVM] Estado: {licenseState.FormattedStatus} ({licenseState.LicenseType})");
                
                var message = string.Format(LocalizationService.Instance.GetString("FeatureUnavailable"), licenseState.FormattedStatus);
                
                VoltrisOptimizer.UI.Controls.LicenseRequiredMessageBox.Show(
                    message,
                    LocalizationService.Instance.GetString("LicenseRequired"));
                
                _logger?.LogInfo("[RepairVM] Reparo completo BLOQUEADO e finalizado");
                return;
            }
            
            _logger?.LogInfo("[RepairVM] FEATURE GATE APROVADO - Reparo completo habilitado, continuando...");

            if (!AdminHelper.IsRunningAsAdministrator())
            {
                GlobalNotificationService.ShowWarning(LocalizationService.Instance.GetString("FullRepair"), LocalizationService.Instance.GetString("AdminRequiredFullRepair"));
                return;
            }

            var confirm = ModernMessageBox.Show(
                LocalizationService.Instance.GetString("FullRepairConfirm"),
                LocalizationService.Instance.GetString("Confirmation"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (confirm != MessageBoxResult.Yes) return;

            await ExecuteSafeAsync(async () =>
            {
                _cts = new CancellationTokenSource();
                var token = _cts.Token;
                GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("FullRepairOptimized"), isPriority: true);
                
                var report = new AdvancedRepairReport { StartTime = DateTime.Now };
                bool success = false;

                try
                {
                    // ETAPA 0: Restore Point (SEQUENCIAL)
                    var s0 = await _repairService.Step00_CreateRestorePointAsync(token);
                    report.Steps.Add(s0);
                    if (s0.Success)
                    {
                        report.RestorePointCreated = true;
                        var match = System.Text.RegularExpressions.Regex.Match(s0.Summary, @"Voltris_FullRepair_\d{8}_\d{6}");
                        if (match.Success) report.RestorePointName = match.Value;
                    }
                    token.ThrowIfCancellationRequested();

                    // OTIMIZAÇÃO: Executar etapas independentes em PARALELO (Limitado a 4 threads para evitar I/O bottleneck)
                    AddLog(">>> " + LocalizationService.Instance.GetString("StartingParallelRepairOrchestration"), Colors.Cyan);
                    
                    var independentTasks = new List<Func<CancellationToken, Task<AdvancedRepairStepResult>>>
                    {
                        _repairService.Step01_NetworkResetAsync,
                        _repairService.Step02_DotNetRepairAsync,
                        _repairService.Step03_VcRedistRepairAsync,
                        _repairService.Step04_WindowsStoreResetAsync,
                        _repairService.Step05_BootRepairAsync,
                        _repairService.Step06_ServicesResetAsync,
                        _repairService.Step07_RegistryRepairAsync,
                        _repairService.Step08_CacheCleanAsync,
                        _repairService.Step09_DefenderRepairAsync,
                        _repairService.Step10_DriverRepairAsync,
                        _repairService.Step11_BsodAnalysisAsync,
                        _repairService.Step12_EventViewerRepairAsync,
                        _repairService.Step13_PrintSpoolerResetAsync,
                        _repairService.Step14_DirectXAudioRepairAsync,
                        _repairService.Step15_UserProfileRepairAsync,
                        _repairService.Step17_SmartDiagnosticsAsync,
                        _repairService.Step18_WindowsInstallerRepairAsync,
                        _repairService.Step20_PerfMonRepairAsync,
                        _repairService.FixSearchBarAsync,
                        _repairService.FixControlPanelAsync
                    };

                    // Executar em batches de 3
                    var results = await RunTasksInBatches(independentTasks, 3, token);
                    report.Steps.AddRange(results);
                    
                    token.ThrowIfCancellationRequested();

                    // ETAPA 16: DISM + SFC (SEQUENCIAL - PESADO)
                    var s16 = await _repairService.Step16_IntegrityCheckAsync(token);
                    report.Steps.Add(s16);

                    // ETAPA FINAL: Relatório
                    report.EndTime = DateTime.Now;
                    await _repairService.GenerateHtmlReportAsync(report, token);
                    
                    success = true;
                    AddLog("✅ " + LocalizationService.Instance.GetString("FullRepairComplete"), Colors.LightGreen);
                }
                catch (OperationCanceledException) { AddLog("⚠ " + LocalizationService.Instance.GetString("OperationCancelled"), Colors.Orange); }
                finally
                {
                    if (success)
                    {
                        HistoryService.RecordActivity("System Repair", LocalizationService.Instance.GetString("FullRepairCompleteDesc"));
                        GlobalNotificationService.ShowSuccess(LocalizationService.Instance.GetString("RepairComplete"), LocalizationService.Instance.GetString("SystemRepairedSuccess"));
                    }
                    GlobalProgressService.Instance.CompleteOperation(success ? LocalizationService.Instance.GetString("RepairCompleted") : LocalizationService.Instance.GetString("RepairInterrupted"));

                    if (success) PromptRestart();
                }
            }, LocalizationService.Instance.GetString("RunningFullRepair"));
        }

        private async Task<List<AdvancedRepairStepResult>> RunTasksInBatches(
            List<Func<CancellationToken, Task<AdvancedRepairStepResult>>> tasks, 
            int batchSize, 
            CancellationToken ct)
        {
            var results = new List<AdvancedRepairStepResult>();
            for (int i = 0; i < tasks.Count; i += batchSize)
            {
                var batch = tasks.Skip(i).Take(batchSize).Select(t => t(ct));
                var batchResults = await Task.WhenAll(batch);
                results.AddRange(batchResults);
                ct.ThrowIfCancellationRequested();
            }
            return results;
        }

        private void PromptRestart()
        {
            Application.Current.Dispatcher.BeginInvoke(() => 
            {
                var modal = new RestartConfirmationModal { Owner = Application.Current.MainWindow };
                modal.SetCustomMessage(LocalizationService.Instance.GetString("RepairComplete"), LocalizationService.Instance.GetString("SystemRepairedRestartPrompt"));
                modal.ShowDialog();
                if (modal.ShouldRestart) Process.Start("shutdown.exe", "/r /t 5");
            });
        }

        private async Task ExecuteRepairSystemAsync()
        {
            if (!AdminHelper.IsRunningAsAdministrator())
            {
                GlobalNotificationService.ShowWarning(LocalizationService.Instance.GetString("IntegrityRepair"), LocalizationService.Instance.GetString("AdminRequiredIntegrityCheck"));
                return;
            }

            await ExecuteSafeAsync(async () =>
            {
                _cts = new CancellationTokenSource();
                var token = _cts.Token;
                GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("IntegrityRepair"), isPriority: true);
                
                try
                {
                    AddLog(">>> " + LocalizationService.Instance.GetString("StartingIntegrityCheck"), Colors.Cyan);
                    var result = await _repairService.Step16_IntegrityCheckAsync(token);
                    
                    if (result.Success)
                    {
                        AddLog("✅ " + LocalizationService.Instance.GetString("IntegrityCheckSuccess"), Colors.LightGreen);
                        HistoryService.RecordActivity("Repair", LocalizationService.Instance.GetString("IntegrityCheckSuccessDesc"));
                        GlobalNotificationService.ShowSuccess(LocalizationService.Instance.GetString("SystemIntegrity"), LocalizationService.Instance.GetString("DismSfcSuccess"));
                        PromptRestart();
                    }
                    else
                    {
                        AddLog("⚠ " + LocalizationService.Instance.GetString("IntegrityCheckWarnings"), Colors.Orange);
                        GlobalNotificationService.ShowWarning(LocalizationService.Instance.GetString("SystemIntegrity"), LocalizationService.Instance.GetString("IntegrityCheckWarningsDesc"));
                    }
                }
                catch (OperationCanceledException) { AddLog("⚠ " + LocalizationService.Instance.GetString("OperationCancelled"), Colors.Orange); }
                finally
                {
                    GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("IntegrityCompleted"));
                }
            }, LocalizationService.Instance.GetString("RepairingIntegrity"));
        }

        private async Task ExecuteDefragAsync()
        {
            if (!AdminHelper.IsRunningAsAdministrator())
            {
                GlobalNotificationService.ShowWarning(LocalizationService.Instance.GetString("DiskOptimization"), LocalizationService.Instance.GetString("AdminRequiredStorageOptimization"));
                return;
            }

            await ExecuteSafeAsync(async () =>
            {
                _cts = new CancellationTokenSource();
                GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("StorageOptimization"), isPriority: true);
                
                try
                {
                    AddLog("══════════════════════════════════════════", Colors.Cyan);
                    AddLog("🔵 " + LocalizationService.Instance.GetString("StorageOptimizationHeader"), Colors.Cyan);
                    AddLog("══════════════════════════════════════════", Colors.Cyan);

                    var optimizer = new VoltrisOptimizer.Services.Hardware.StorageOptimizerService();
                    
                    var progress = new Progress<string>(msg => 
                    {
                        AddLog($"→ {msg}", Colors.LightGray);
                    });

                    await optimizer.OptimizeAllDrivesAsync(progress);
                    
                    AddLog("══════════════════════════════════════════", Colors.Cyan);
                    AddLog("✅ " + LocalizationService.Instance.GetString("StorageOptimizationComplete"), Colors.LightGreen);
                    AddLog("══════════════════════════════════════════", Colors.Cyan);
                    
                    HistoryService.RecordActivity("Storage Optimization", LocalizationService.Instance.GetString("AllDrivesOptimized"));
                    GlobalNotificationService.ShowSuccess(LocalizationService.Instance.GetString("OptimizationComplete"), LocalizationService.Instance.GetString("AllDrivesOptimizedSuccess"));
                    GlobalProgressService.Instance.CompleteOperation("✅ " + LocalizationService.Instance.GetString("StorageOptimizationComplete"));
                }
                catch (OperationCanceledException)
                {
                    AddLog("⚠ " + LocalizationService.Instance.GetString("OperationCancelled"), Colors.Orange);
                    GlobalProgressService.Instance.CompleteOperation("⚠ " + LocalizationService.Instance.GetString("OptimizationCancelled"));
                }
                catch (Exception ex)
                {
                    AddLog(string.Format(LocalizationService.Instance.GetString("CriticalError"), ex.Message), Colors.Red);
                    _logger.LogError($"[Repair.Defrag] {ex.Message}");
                    GlobalNotificationService.ShowError(LocalizationService.Instance.GetString("DiskOptimization"), string.Format(LocalizationService.Instance.GetString("Error"), ex.Message));
                    GlobalProgressService.Instance.CompleteOperation("❌ " + LocalizationService.Instance.GetString("StorageOptimizationError"));
                }
            }, LocalizationService.Instance.GetString("OptimizingDisk"), skipIsBusy: true);
        }

        private void ExecuteDiskCleanup(object? _)
        {
            try
            {
                AddLog(">>> " + LocalizationService.Instance.GetString("DiskCleanupStarted"), Colors.LightGreen);
                GlobalNotificationService.ShowInfo(LocalizationService.Instance.GetString("DiskCleanup"), LocalizationService.Instance.GetString("StartingNativeCleaner"));

                var psi = new ProcessStartInfo
                {
                    FileName = "cleanmgr.exe",
                    Arguments = "/d C:",
                    UseShellExecute = true,
                    Verb = "runas"
                };
                Process.Start(psi);
                AddLog(LocalizationService.Instance.GetString("RepairCleanMgrOpened"), Colors.LightGreen);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[Repair] Erro ao abrir cleanmgr.exe: {ex.Message}");
                GlobalNotificationService.ShowError(LocalizationService.Instance.GetString("DiskCleanup"), string.Format(LocalizationService.Instance.GetString("DiskCleanupFailed"), ex.Message));
            }
            finally
            {
                HistoryService.RecordActivity("Disk Cleanup", LocalizationService.Instance.GetString("DiskCleanupStartedDesc"));
            }
        }

        protected override void OnDisposing()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            base.OnDisposing();
        }
    }
}
