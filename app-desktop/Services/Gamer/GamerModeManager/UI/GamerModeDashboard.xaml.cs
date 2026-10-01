using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using VoltrisOptimizer.Services.Thermal;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Helpers;
namespace VoltrisOptimizer.Services.Gamer.GamerModeManager.UI
{
    /// <summary>
    /// Dashboard do Modo Gamer
    /// Interface de monitoramento e controle em tempo real
    /// </summary>
    public partial class GamerModeDashboard : UserControl
    {
        private ILoggingService? _logger => App.LoggingService;
        private IGamerModeManager? _manager;
        private DispatcherTimer? _updateTimer;
        private DispatcherTimer? _sessionTimer;
        private readonly ObservableCollection<LogEntryViewModel> _logEntries = new();
        
        public GamerModeDashboard()
        {
            _logger.LogEntry(nameof(GamerModeDashboard));
            InitializeComponent();
            
            LogsList.ItemsSource = _logEntries;
            
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            _logger.LogExit(nameof(GamerModeDashboard));
        }
        
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _logger.LogEntry(nameof(OnLoaded));
            _logger.LogEntry(nameof(OnLoaded));
            _logger.LogEntry(nameof(OnLoaded));
            // Obter manager do App
            _manager = GetGamerModeManager();
            
            if (_manager != null)
            {
                // Subscrever eventos
                _manager.StateChanged += OnStateChanged;
                _manager.MetricsUpdated += OnMetricsUpdated;
                _manager.SafetyRollbackTriggered += OnSafetyRollback;
                _manager.GameDetected += OnGameDetected;
                _manager.LogEntry += OnLogEntry;
                
                // Carregar configurações
                LoadSettings();
                
                // Atualizar UI inicial
                UpdateUI(_manager.CurrentState);
                
                // Iniciar timer de sessão
                _sessionTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                _sessionTimer.Tick += UpdateSessionTime;
                _sessionTimer.Start();
                
                // Iniciar detecção de jogos se configurado
                if (_manager.Config.AutoActivateOnGameStart)
                {
                    _manager.StartGameDetection();
                }
                
                AddLog(LogLevel.Info, LocalizationService.Instance.GetString("GamerDashboardLoaded"));
            }
            else
            {
AddLog(LogLevel.Error, LocalizationService.Instance.GetString("GamerManagerUnavailable"));
}
            _logger.LogExit(nameof(OnLoaded));
            _logger.LogExit(nameof(OnLoaded));
            _logger.LogExit(nameof(OnLoaded));
        }
        
        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _logger.LogEntry(nameof(OnUnloaded));
            _logger.LogEntry(nameof(OnUnloaded));
            _logger.LogEntry(nameof(OnUnloaded));
            if (_manager != null)
            {
                _manager.StateChanged -= OnStateChanged;
                _manager.MetricsUpdated -= OnMetricsUpdated;
                _manager.SafetyRollbackTriggered -= OnSafetyRollback;
                _manager.GameDetected -= OnGameDetected;
                _manager.LogEntry -= OnLogEntry;
            }
            
            _sessionTimer?.Stop();
            _updateTimer?.Stop();
            _logger.LogExit(nameof(OnUnloaded));
            _logger.LogExit(nameof(OnUnloaded));
            _logger.LogExit(nameof(OnUnloaded));
        }
        
        private IGamerModeManager? GetGamerModeManager()
        {
            _logger.LogEntry(nameof(GetGamerModeManager));
            _logger.LogEntry(nameof(GetGamerModeManager));
            _logger.LogEntry(nameof(GetGamerModeManager));
// Tentar obter do App ou criar novo
            try
            {
                // Verificar se já existe no App
                if (Application.Current is App app)
                {
                    // Você pode adicionar uma propriedade GamerModeManager no App.xaml.cs
                    // Por enquanto, criar uma instância
                }
                
                // Criar nova instância com dependências
                var logger = App.LoggingService;
                var gameDetectionService = App.Services?.GetService(typeof(IGameDetectionService)) as IGameDetectionService;
                var thermalMonitor = App.Services?.GetService(typeof(IThermalMonitorService)) as IThermalMonitorService;
                if (logger != null && gameDetectionService != null && thermalMonitor != null)
                {
_logger.LogExit(nameof(GetGamerModeManager));
_logger.LogExit(nameof(GetGamerModeManager));
_logger.LogExit(nameof(GetGamerModeManager));
return new GamerModeManager(logger, gameDetectionService, thermalMonitor);
                }
_logger.LogExit(nameof(GetGamerModeManager));
_logger.LogExit(nameof(GetGamerModeManager));
_logger.LogExit(nameof(GetGamerModeManager));
return null;
            }
            catch
            {
_logger.LogExit(nameof(GetGamerModeManager));
_logger.LogExit(nameof(GetGamerModeManager));
_logger.LogExit(nameof(GetGamerModeManager));
return null;
            }
            _logger.LogExit(nameof(GetGamerModeManager));
            _logger.LogExit(nameof(GetGamerModeManager));
            _logger.LogExit(nameof(GetGamerModeManager));
}
        
        #region Event Handlers
        
        private void OnStateChanged(object? sender, GamerModeState state)
        {
            _logger.LogEntry(nameof(OnStateChanged));
            _logger.LogEntry(nameof(OnStateChanged));
            _logger.LogEntry(nameof(OnStateChanged));
Dispatcher.InvokeAsync(() => UpdateUI(state), System.Windows.Threading.DispatcherPriority.Background);
            _logger.LogExit(nameof(OnStateChanged));
            _logger.LogExit(nameof(OnStateChanged));
            _logger.LogExit(nameof(OnStateChanged));
}
        
        private void OnMetricsUpdated(object? sender, HardwareMetrics metrics)
        {
            _logger.LogEntry(nameof(OnMetricsUpdated));
            _logger.LogEntry(nameof(OnMetricsUpdated));
            _logger.LogEntry(nameof(OnMetricsUpdated));
Dispatcher.InvokeAsync(() => UpdateMetrics(metrics), System.Windows.Threading.DispatcherPriority.Background);
            _logger.LogExit(nameof(OnMetricsUpdated));
            _logger.LogExit(nameof(OnMetricsUpdated));
            _logger.LogExit(nameof(OnMetricsUpdated));
}
        
        private void OnSafetyRollback(object? sender, SafetyRollbackEventArgs e)
        {
            _logger.LogEntry(nameof(OnSafetyRollback));
            _logger.LogEntry(nameof(OnSafetyRollback));
            _logger.LogEntry(nameof(OnSafetyRollback));
Dispatcher.InvokeAsync(() =>
            {
                AddLog(LogLevel.Critical, $"[WARN] ROLLBACK: {
                    e.Reason}");
MessageBox.Show(
                    $"Rollback de segurança ativado!\n\nMotivo: {e.Reason}\n\nTodas as configurações foram restauradas.",
                    "Rollback de Segurança",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            _logger.LogExit(nameof(OnSafetyRollback));
            _logger.LogExit(nameof(OnSafetyRollback));
            _logger.LogExit(nameof(OnSafetyRollback));
}, System.Windows.Threading.DispatcherPriority.Normal);
}
        
        private void OnGameDetected(object? sender, GameDetectedEventArgs e)
        {
            _logger.LogEntry(nameof(OnGameDetected));
Dispatcher.InvokeAsync(() =>
            {
                if (e.IsStarting)
                {
                    AddLog(LogLevel.Info, $"[GAME] Jogo detectado: {
e.GameName}");
}
                else
                {
                    AddLog(LogLevel.Info, $"[GAME] Jogo encerrado: {
e.GameName}");
            _logger.LogExit(nameof(OnGameDetected));
}
            }, System.Windows.Threading.DispatcherPriority.Background);
}
        
        private void OnLogEntry(object? sender, GamerModeLogEntry entry)
        {
Dispatcher.InvokeAsync(() => AddLog(entry.Level, entry.Message), System.Windows.Threading.DispatcherPriority.Background);
}
        
        #endregion
        
        #region UI Updates
        
        private void UpdateUI(GamerModeState state)
        {
// Status
            if (state.IsActive)
            {
                StatusText.Text = "Ativo";
                StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // Green
                ToggleButton.Content = LocalizationService.Instance.GetString("GamerModeDashboardDeactivate");
            }
            else
            {
                _logger.LogEntry(nameof(SolidColorBrush));
                StatusText.Text = "Inativo";
                StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(107, 114, 128)); // Gray
                ToggleButton.Content = LocalizationService.Instance.GetString("GamerModeDashboardActivate");
                _logger.LogExit(nameof(SolidColorBrush));
            }
            
            // Game
            if (!string.IsNullOrEmpty(state.ActiveGameName))
            {
                _logger.LogEntry(nameof(SolidColorBrush));
                CurrentGameText.Text = $"🎮 {state.ActiveGameName}";
                _logger.LogExit(nameof(SolidColorBrush));
            }
            else
            {
                CurrentGameText.Text = LocalizationService.Instance.GetString("GamerModeDashboardNoGameDetected");
            }
            
            // Power Plan
            PowerPlanText.Text = state.CurrentPowerPlanName ?? "-";
            
            // GPU
            GpuText.Text = state.GpuOptimized ? (state.GpuPowerMode ?? "Otimizado") : "-";
}
        
        private void UpdateMetrics(HardwareMetrics metrics)
        {
// CPU Temperature
            CpuTempText.Text = metrics.CpuTemperature > 0 ? $"{metrics.CpuTemperature:F0}" : "--";
            CpuTempBar.Value = Math.Min(100, metrics.CpuTemperature);
            UpdateProgressBarColor(CpuTempBar, metrics.CpuTemperature, 70, 85);
            
            // GPU Temperature
            GpuTempText.Text = metrics.GpuTemperature > 0 ? $"{
                metrics.GpuTemperature:F0}" : "--";
GpuTempBar.Value = Math.Min(100, metrics.GpuTemperature);
            UpdateProgressBarColor(GpuTempBar, metrics.GpuTemperature, 70, 80);
            
            // CPU Usage
            CpuUsageText.Text = $"{
                metrics.CpuUsage:F0}";
CpuUsageBar.Value = metrics.CpuUsage;
            
            // GPU Usage
            GpuUsageText.Text = metrics.GpuUsage > 0 ? $"{metrics.GpuUsage:F0}" : "--";
            GpuUsageBar.Value = metrics.GpuUsage;
            
            // RAM Usage
            RamUsageText.Text = $"{metrics.RamUsagePercent:F0}";
            RamUsageBar.Value = metrics.RamUsagePercent;
}
        
        private void UpdateProgressBarColor(ProgressBar bar, double value, double warnThreshold, double dangerThreshold)
        {
if (value >= dangerThreshold)
            {
                bar.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red
            }
            else if (value >= warnThreshold)
            {
                bar.Foreground = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Orange
            }
            else
            {
                bar.Foreground = new SolidColorBrush(Color.FromRgb(124, 58, 237)); // Purple
            }
}
        
        private void UpdateSessionTime(object? sender, EventArgs e)
        {
if (_manager?.CurrentState.ActivatedAt.HasValue == true)
            {
                var duration = DateTime.Now - _manager.CurrentState.ActivatedAt.Value;
                SessionTimeText.Text = duration.ToString(@"hh\:mm\:ss");
            }
            else
            {
                SessionTimeText.Text = "00:00:00";
            }
}
        
        #endregion
        
        #region Button Handlers
        
        private async void ToggleButton_Click(object sender, RoutedEventArgs e)
        {
if (_manager == null) return;
            
            ToggleButton.IsEnabled = false;
            
            try
            {
                if (_manager.IsActive)
                {
                    await _manager.DeactivateAsync();
                }
                else
                {
                    await _manager.ActivateAsync();
                }
            }
            catch (Exception ex)
            {
                AddLog(LogLevel.Error, $"Erro: {
ex.Message}");
}
            finally
            {
                ToggleButton.IsEnabled = true;
            }
}
        
        private async void EmergencyButton_Click(object sender, RoutedEventArgs e)
        {
if (_manager == null) return;
            
            var result = MessageBox.Show(
                "Executar rollback de emergência?\n\nIsso irá restaurar todas as configurações imediatamente.",
                "Rollback de Emergência",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            
            if (result == MessageBoxResult.Yes)
            {
                await _manager.ForceEmergencyRollbackAsync();
            }
}
        
        private void ClearLogsButton_Click(object sender, RoutedEventArgs e)
        {
_logEntries.Clear();
}
        
        private async void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
        {
if (_manager == null) return;
            
            try
            {
                // Aplicar configurações
                _manager.Config.AutoActivateOnGameStart = AutoActivateCheck.IsChecked ?? true;
                _manager.Config.AutoDeactivateOnGameEnd = AutoDeactivateCheck.IsChecked ?? true;
                _manager.Config.OptimizePowerPlan = OptimizePowerCheck.IsChecked ?? true;
                _manager.Config.OptimizeGpu = OptimizeGpuCheck.IsChecked ?? true;
                _manager.Config.SetHighPriority = HighPriorityCheck.IsChecked ?? true;
                _manager.Config.AutoRollbackOnOverheat = AutoRollbackCheck.IsChecked ?? true;
                
                if (int.TryParse(CpuMaxTempBox.Text, out var cpuMax))
                {
                    _manager.Config.CpuMaxTemp = Math.Clamp(cpuMax, 60, 100);
                }
                
                if (int.TryParse(GpuMaxTempBox.Text, out var gpuMax))
                {
                    _manager.Config.GpuMaxTemp = Math.Clamp(gpuMax, 60, 95);
                }
                
                await _manager.SaveConfigAsync();
                
                AddLog(LogLevel.Info, "[OK] Configurações salvas");
            }
            catch (Exception ex)
            {
AddLog(LogLevel.Error, $"Erro ao salvar: {
ex.Message}");
            }
}
        
        #endregion
        
        #region Helpers
        
        private void LoadSettings()
        {
            _logger.LogEntry(nameof(LoadSettings));
            _logger.LogEntry(nameof(LoadSettings));
if (_manager == null) return;
            
            AutoActivateCheck.IsChecked = _manager.Config.AutoActivateOnGameStart;
            AutoDeactivateCheck.IsChecked = _manager.Config.AutoDeactivateOnGameEnd;
            OptimizePowerCheck.IsChecked = _manager.Config.OptimizePowerPlan;
            OptimizeGpuCheck.IsChecked = _manager.Config.OptimizeGpu;
            HighPriorityCheck.IsChecked = _manager.Config.SetHighPriority;
            AutoRollbackCheck.IsChecked = _manager.Config.AutoRollbackOnOverheat;
            CpuMaxTempBox.Text = _manager.Config.CpuMaxTemp.ToString();
            GpuMaxTempBox.Text = _manager.Config.GpuMaxTemp.ToString();
            _logger.LogExit(nameof(LoadSettings));
            _logger.LogExit(nameof(LoadSettings));
}
        
        private void AddLog(LogLevel level, string message)
        {
            _logger.LogEntry(nameof(AddLog));
            _logger.LogEntry(nameof(AddLog));
var entry = new LogEntryViewModel
            {
                Timestamp = DateTime.Now,
                Level = level,
                Message = message
            };
            
            _logEntries.Insert(0, entry);
            
            // Limitar a 100 entradas
            while (_logEntries.Count > 100)
            {
                _logEntries.RemoveAt(_logEntries.Count - 1);
            }
            _logger.LogExit(nameof(AddLog));
            _logger.LogExit(nameof(AddLog));
}
        
        #endregion
    }
    
    /// <summary>
    /// ViewModel para entradas de log
    /// </summary>
    public class LogEntryViewModel
    {
        public DateTime Timestamp { get; set; }
        public LogLevel Level { get; set; }
        public string Message { get; set; } = "";
        
        public string LevelText => Level switch
        {
            LogLevel.Debug => "DBG",
            LogLevel.Info => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "???"
        };
        
        public Brush LevelBrush => Level switch
        {
            LogLevel.Debug => new SolidColorBrush(Color.FromRgb(107, 114, 128)),
            LogLevel.Info => new SolidColorBrush(Color.FromRgb(59, 130, 246)),
            LogLevel.Warning => new SolidColorBrush(Color.FromRgb(245, 158, 11)),
            LogLevel.Error => new SolidColorBrush(Color.FromRgb(239, 68, 68)),
            LogLevel.Critical => new SolidColorBrush(Color.FromRgb(239, 68, 68)),
            _ => new SolidColorBrush(Color.FromRgb(107, 114, 128))
        };
    }
}

