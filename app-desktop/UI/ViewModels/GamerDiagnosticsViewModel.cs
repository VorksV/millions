using VoltrisOptimizer.Services;

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using VoltrisOptimizer.Services.Gamer.Diagnostics.Interfaces;
using VoltrisOptimizer.Services.Gamer.Diagnostics.Models;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.UI.ViewModels
{
    public class GamerDiagnosticsViewModel : ViewModelBase
    {
        private readonly IGamerSelfProfiler _profiler;
        private readonly DispatcherTimer _updateTimer; // Deprecated – replaced by UiScheduler
        private IDisposable? _updateTimerHandle;
        private ProfilingSnapshot? _currentSnapshot;
        private ProfilingReport? _currentReport;
        private bool _isDeveloperModeEnabled;
        private bool _isMonitoringActive = false;

        public GamerDiagnosticsViewModel(IGamerSelfProfiler profiler)
        {
            _profiler = profiler ?? throw new ArgumentNullException(nameof(profiler));
            App.LoggingService?.LogTrace("[GAMER_DIAG] ViewModel inicializado");

            ModuleStatistics = new ObservableCollection<ModuleStatisticsViewModel>();
            Anomalies = new ObservableCollection<PerformanceAnomaly>();
            OrchestratorLoopHistory = new ObservableCollection<LoopTimePoint>();

            // NÃO iniciar o timer aqui — será iniciado quando a View for ativada
            // Register periodic update via UiScheduler (replaces DispatcherTimer)
            // _updateTimerHandle iniciado em StartMonitoring()

            _profiler.SnapshotUpdated += Profiler_SnapshotUpdated;
            _profiler.AnomalyDetected += Profiler_AnomalyDetected;

            ExportReportCommand = new AsyncRelayCommand(async () => await ExportReportAsync());
            ClearHistoryCommand = new RelayCommand(() => ClearHistory());
            ToggleDeveloperModeCommand = new RelayCommand(() => IsDeveloperModeEnabled = !IsDeveloperModeEnabled);

            VoltrisOptimizer.Services.LocalizationService.Instance.LanguageChanged += OnLanguageChanged;

            // FASE 7: Brain V2 Metrics
            try
            {
                var brainMetrics = Core.Brain.V2.BrainMetricsCache.Instance;
                var brain = App.Services?.GetService(typeof(Core.Brain.V2.VoltrisBrainV2)) as Core.Brain.V2.VoltrisBrainV2;
                if (brain != null)
                {
                    brainMetrics.Initialize(brain);
                    BrainStatus = brainMetrics.BrainStatus;
                    brainMetrics.PropertyChanged += (s, e) =>
                    {
                        App.Current.Dispatcher.BeginInvoke(() =>
                        {
                            if (e.PropertyName == nameof(Core.Brain.V2.BrainMetricsCache.BrainStatus))
                                BrainStatus = brainMetrics.BrainStatus;
                            else if (e.PropertyName == nameof(Core.Brain.V2.BrainMetricsCache.ActiveAction))
                                BrainAction = brainMetrics.ActiveAction;
                            else if (e.PropertyName == nameof(Core.Brain.V2.BrainMetricsCache.QTableSize))
                                BrainQTable = brainMetrics.QTableSize;
                            else if (e.PropertyName == nameof(Core.Brain.V2.BrainMetricsCache.IntegrationStatus))
                                BrainIntegration = brainMetrics.IntegrationStatus;
                        });
                    };
                }
            }
            catch { }
        }

        // ─── PERFORMANCE: Gerenciamento de ciclo de vida ───────────────────────
        /// <summary>
        /// Inicia os monitoramentos. Chamado quando a View é ativada/exibida.
        /// </summary>
        public void StartMonitoring()
        {
            if (_isMonitoringActive) return;
            
            try
            {
                App.LoggingService?.LogInfo("[Diagnostics] View activated");
                App.LoggingService?.LogInfo("[Diagnostics] Monitoring started");
                
                _updateTimerHandle = UiScheduler.Register(
                    () => UpdateTimer_Tick(this, EventArgs.Empty),
                    TimeSpan.FromMilliseconds(2000),
                    DispatcherPriority.Background);
                
                _isMonitoringActive = true;
                
                // Atualização imediata ao ativar
                UpdateData();
                
                App.LoggingService?.LogInfo("[Diagnostics] Initial data loaded after activation");
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[Diagnostics] Error starting monitoring: {ex.Message}");
            }
        }

        /// <summary>
        /// Para os monitoramentos. Chamado quando a View é desativada/oculta.
        /// Os dados em memória são preservados.
        /// </summary>
        public void StopMonitoring()
        {
            if (!_isMonitoringActive) return;
            
            try
            {
                App.LoggingService?.LogInfo("[Diagnostics] View deactivated");
                App.LoggingService?.LogInfo("[Diagnostics] Monitoring paused");
                App.LoggingService?.LogInfo("[Diagnostics] Timer stopped");
                App.LoggingService?.LogInfo("[Diagnostics] Background work suspended");
                
                _updateTimerHandle?.Dispose();
                _updateTimerHandle = null;
                _isMonitoringActive = false;
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[Diagnostics] Error stopping monitoring: {ex.Message}");
            }
        }
        // ──────────────────────────────────────────────────────────────────────

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            App.Current.Dispatcher.BeginInvoke(() =>
            {
                ModuleStatistics.Clear();
                Anomalies.Clear();
                OrchestratorLoopHistory.Clear();
            });
        }

        #region Properties

        public ObservableCollection<ModuleStatisticsViewModel> ModuleStatistics { get; }
        public ObservableCollection<PerformanceAnomaly> Anomalies { get; }
        public ObservableCollection<LoopTimePoint> OrchestratorLoopHistory { get; }

        public ProfilingSnapshot? CurrentSnapshot
        {
            get => _currentSnapshot;
            set => SetProperty(ref _currentSnapshot, value);
        }

        public ProfilingReport? CurrentReport
        {
            get => _currentReport;
            set => SetProperty(ref _currentReport, value);
        }

        public bool IsDeveloperModeEnabled
        {
            get => _isDeveloperModeEnabled;
            set => SetProperty(ref _isDeveloperModeEnabled, value);
        }

        public double TotalCpuUsage => CurrentSnapshot?.TotalCpuUsagePercent ?? 0;
        public double OverlayGpuUsage => CurrentSnapshot?.OverlayGpuUsagePercent ?? 0;
        public double TotalMemoryUsage => CurrentSnapshot?.TotalMemoryUsageMb ?? 0;
        public double OrchestratorLoopTime => CurrentSnapshot?.OrchestratorLoopTimeMs ?? 0;
        public int ActiveThreads => CurrentSnapshot?.ActiveThreads ?? 0;
        public int PendingTasks => CurrentSnapshot?.PendingAsyncTasks ?? 0;
        public int CriticalAnomalies => CurrentSnapshot?.Anomalies.Count(a => a.Severity == AnomalySeverity.Critical) ?? 0;
        public int HighAnomalies => CurrentSnapshot?.Anomalies.Count(a => a.Severity == AnomalySeverity.High) ?? 0;
        public double InternalLatency => CurrentSnapshot?.InternalLatencyMs ?? 0;

        // FASE 7: Brain V2 Metrics
        private string _brainStatus = "---";
        public string BrainStatus { get => _brainStatus; set => SetProperty(ref _brainStatus, value); }

        private string _brainAction = "";
        public string BrainAction { get => _brainAction; set => SetProperty(ref _brainAction, value); }

        private int _brainQTable;
        public int BrainQTable { get => _brainQTable; set => SetProperty(ref _brainQTable, value); }

        private string _brainIntegration = "0/5";
        public string BrainIntegration { get => _brainIntegration; set => SetProperty(ref _brainIntegration, value); }

        #endregion

        #region Commands

       public ICommand ExportReportCommand { get; }
       public ICommand ClearHistoryCommand { get; }
       public ICommand ToggleDeveloperModeCommand { get; }

       #endregion

        private void UpdateTimer_Tick(object? sender, EventArgs e)
        {
            UpdateData();
        }

        private void Profiler_SnapshotUpdated(object? sender, ProfilingSnapshot snapshot)
        {
            CurrentSnapshot = snapshot;
            if (snapshot.Anomalies.Count > 0)
            {
                App.LoggingService?.LogTrace($"[GAMER_DIAG] Snapshot atualizado. {snapshot.Anomalies.Count} anomalias presentes no momento.");
            }
            OnPropertyChanged(nameof(TotalCpuUsage));
            OnPropertyChanged(nameof(OverlayGpuUsage));
            OnPropertyChanged(nameof(TotalMemoryUsage));
            OnPropertyChanged(nameof(OrchestratorLoopTime));
            OnPropertyChanged(nameof(ActiveThreads));
            OnPropertyChanged(nameof(PendingTasks));
            OnPropertyChanged(nameof(CriticalAnomalies));
            OnPropertyChanged(nameof(HighAnomalies));
            OnPropertyChanged(nameof(InternalLatency));
        }

        private void Profiler_AnomalyDetected(object? sender, PerformanceAnomaly anomaly)
        {
            App.LoggingService?.LogWarning($"[GAMER_DIAG] Anomalia detectada: {anomaly.Name} - Severidade: {anomaly.Severity}");
            App.Current.Dispatcher.BeginInvoke(() =>
            {
                Anomalies.Insert(0, anomaly);
                while (Anomalies.Count > 100)
                {
                    Anomalies.RemoveAt(Anomalies.Count - 1);
                }
            });
        }

        private void UpdateData()
        {
            try
            {
                var report = _profiler.GetCurrentReport();
                CurrentReport = report;

                App.Current.Dispatcher.BeginInvoke(() =>
                {
                    var newStats = report.ModuleStatistics
                        .OrderByDescending(x => x.Value.AverageExecutionTimeMs)
                        .Select(kvp => new ModuleStatisticsViewModel
                        {
                            ModuleName = kvp.Key,
                            AverageExecutionTime = kvp.Value.AverageExecutionTimeMs,
                            MaxExecutionTime = kvp.Value.MaxExecutionTimeMs,
                            CpuUsage = kvp.Value.AverageCpuUsagePercent,
                            GpuUsage = kvp.Value.AverageGpuUsagePercent,
                            ThreadBlocks = kvp.Value.TotalThreadBlocks,
                            TotalExecutions = kvp.Value.TotalExecutions,
                            ExecutionsAboveThreshold = kvp.Value.ExecutionsAboveThreshold
                        });
                    CollectionDiffUpdater.ReplaceInPlace(ModuleStatistics, newStats, s => s.ModuleName);

                    if (report.CurrentSnapshot.OrchestratorLoopTimeMs > 0)
                    {
                        OrchestratorLoopHistory.Add(new LoopTimePoint
                        {
                            Time = DateTime.Now,
                            LoopTimeMs = report.CurrentSnapshot.OrchestratorLoopTimeMs
                        });
                        while (OrchestratorLoopHistory.Count > 120)
                        {
                            OrchestratorLoopHistory.RemoveAt(0);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GamerDiagnostics] Erro ao atualizar dados: {ex.Message}");
            }
        }

        private async Task ExportReportAsync()
        {
            bool started = GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("ExportGamerReport"), false);
            try
            {
                var json = await _profiler.ExportReportAsync();
                var fileName = $"VoltrisDiagnostics_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                var filePath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    fileName
                );
                await System.IO.File.WriteAllTextAsync(filePath, json);
                App.LoggingService?.LogSuccess($"[GAMER_DIAG] Relatório de diagnóstico exportado com sucesso em: {filePath}");
                GlobalNotificationService.ShowSuccess(LocalizationService.Instance.GetString("ExportReport"), LocalizationService.Instance.GetString("ReportExportedTo") + "\n" + filePath);
                if (started) GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("ReportExportedOp"));
            }
            catch (Exception ex)
            {
                GlobalNotificationService.ShowError(LocalizationService.Instance.GetString("Error"), string.Format(LocalizationService.Instance.GetString("ErrorExportingReport"), ex.Message));
                if (started) GlobalProgressService.Instance.FailOperation(string.Format(LocalizationService.Instance.GetString("ErrorExportingReportOp"), ex.Message));
            }
            finally
            {
                VoltrisOptimizer.Services.HistoryService.RecordActivity("Gamer Diagnostics", LocalizationService.Instance.GetString("GamerDiagnosticsExportHistoryRecord"));
            }
        }


        private void ClearHistory()
        {
            bool started = GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("ClearGamerHistory"), false);
            try
            {
                _profiler.ClearHistory();
                App.Current.Dispatcher.BeginInvoke(() =>
                {
                    ModuleStatistics.Clear();
                    Anomalies.Clear();
                    OrchestratorLoopHistory.Clear();
                });
                GlobalNotificationService.ShowSuccess(LocalizationService.Instance.GetString("GamerDiagnostics"), LocalizationService.Instance.GetString("GamerDiagnosticsCleared"));
                if (started) GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("GamerDiagnosticsClearedOp"));
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("Erro ao limpar histórico gamer", ex);
                GlobalNotificationService.ShowError(LocalizationService.Instance.GetString("Error"), string.Format(LocalizationService.Instance.GetString("ErrorClearingGamerHistory"), ex.Message));
                if (started) GlobalProgressService.Instance.FailOperation(string.Format(LocalizationService.Instance.GetString("ErrorClearingGamerHistoryOp"), ex.Message));
            }
        }
        
        protected override void OnDisposing()
        {
            _updateTimerHandle?.Dispose(); // Dispose registered UiScheduler callback
            _profiler.SnapshotUpdated -= Profiler_SnapshotUpdated;
            _profiler.AnomalyDetected -= Profiler_AnomalyDetected;
            VoltrisOptimizer.Services.LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            base.OnDisposing();
        }
    }

    public class ModuleStatisticsViewModel
    {
        public string ModuleName { get; set; } = "";
        public double AverageExecutionTime { get; set; }
        public double MaxExecutionTime { get; set; }
        public double CpuUsage { get; set; }
        public double GpuUsage { get; set; }
        public int ThreadBlocks { get; set; }
        public int TotalExecutions { get; set; }
        public int ExecutionsAboveThreshold { get; set; }
    }

    public class LoopTimePoint
    {
        public DateTime Time { get; set; }
        public double LoopTimeMs { get; set; }
    }
}
