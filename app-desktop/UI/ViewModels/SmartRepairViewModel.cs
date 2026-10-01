using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Collections.Generic;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.SmartRepair;
using VoltrisOptimizer.Services.SmartRepair.Architecture;
using VoltrisOptimizer.Services.SmartRepair.Modules;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.UI.ViewModels.Utils;

namespace VoltrisOptimizer.UI.ViewModels
{
    public class SmartRepairLogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Message { get; set; } = string.Empty;
        public LogMessageType Type { get; set; }
        public string FormattedTimestamp => Timestamp.ToString("HH:mm:ss.fff");
        public LogMessageType LogType => Type;
    }

    public class SmartRepairViewModel : ViewModelBase
    {
        private readonly ILoggingService _logger;
        private readonly ModularSmartRepairEngine _engine;
        private readonly ISmartRepairEventBus _eventBus;
        private readonly IExclusionService _exclusionService;
        
private CancellationTokenSource? _cts;
        private IDisposable? _uiTimerHandle;
        private readonly Stopwatch _sw = new();
        private DateTime _lastUiUpdate = DateTime.MinValue; // OTIMIZAÇÃO: Throttling de UI
        private readonly object _uiLock = new object(); // OTIMIZAÇÃO: Lock para atualizações de UI
        
        public ObservableCollection<SmartRepairModuleItem> Modules { get; } = new();
        public ObservableRingBuffer<SmartRepairLogEntry> LogEntries { get; } = new(10000);

        private Dictionary<string, ModuleScanResult> _scanResults = new();
        private Dictionary<string, ModuleSimulationResult> _simResults = new();

        private bool _isScanning;
        public bool IsScanning { get => _isScanning; set { SetProperty(ref _isScanning, value); UpdateCommandStates(); } }

        private bool _isSimulated;
        public bool IsSimulated { get => _isSimulated; set { SetProperty(ref _isSimulated, value); UpdateCommandStates(); } }

        private bool _isExecuting;
        public bool IsExecuting { get => _isExecuting; set { SetProperty(ref _isExecuting, value); UpdateCommandStates(); } }

        private bool _isCompleted;
        public bool IsCompleted { get => _isCompleted; set { SetProperty(ref _isCompleted, value); UpdateCommandStates(); } }

        private bool _isCancelled;
        public bool IsCancelled { get => _isCancelled; set { SetProperty(ref _isCancelled, value); UpdateCommandStates(); } }

        public bool IsIdle => !IsScanning && !IsExecuting;
        public bool CanStartScan => IsIdle && !IsSimulated && !IsCompleted;
        public bool CanConfirmExecution => IsSimulated && !IsExecuting && !IsCompleted;

        private string _currentStepName = string.Empty;
        public string CurrentStepName { get => _currentStepName; set => SetProperty(ref _currentStepName, value); }
        
        private string _currentStepDescription = string.Empty;
        public string CurrentStepDescription { get => _currentStepDescription; set => SetProperty(ref _currentStepDescription, value); }

        private string _totalElapsed = "00:00";
        public string TotalElapsed { get => _totalElapsed; set { SetProperty(ref _totalElapsed, value); OnPropertyChanged(nameof(TotalElapsedLabel)); } }
        
        public string TotalElapsedLabel => $"{LocalizationService.Instance.GetString("ElapsedWord")}: {TotalElapsed}";

        private int _overallPercent;
        public int OverallPercent 
        { 
            get => _overallPercent; 
            set 
            {
                if (SetProperty(ref _overallPercent, value))
                {
                    if (IsScanning || IsExecuting)
                    {
                        string msg;
                        if (_isExecutingPhase)
                        {
                            msg = !string.IsNullOrEmpty(CurrentStepDescription) ? CurrentStepDescription : 
                                  (!string.IsNullOrEmpty(CurrentStepName) ? CurrentStepName : LocalizationService.Instance.GetString("SmartRepairRepairing") ?? "Reparando...");
                        }
                        else
                        {
                            msg = LocalizationService.Instance.GetString("SmartRepairAnalyzing") ?? "Analisando...";
                        }
                        VoltrisOptimizer.Services.GlobalProgressService.Instance.UpdateProgress(value, msg);
                    }
                }
            } 
        }
        
        private long _spaceRecoveredBytes;
        public long SpaceRecoveredBytes { get => _spaceRecoveredBytes; set { SetProperty(ref _spaceRecoveredBytes, value); OnPropertyChanged(nameof(TotalSpaceRecovered)); } }

        public long TotalFilesChecked => ItemsFound;
        public int TotalFilesFixed => Modules.Count(m => m.Status == StepStatus.Completed);
        public string TotalSpaceRecovered
    {
        get
        {
            // Use GB when size is >= 1⁠GB, otherwise MB. Never use MiB.
            const double MB = 1024.0 * 1024.0;
            const double GB = MB * 1024.0;
            if (SpaceRecoveredBytes >= GB)
                return $"{SpaceRecoveredBytes / GB:F2} GB";
            return $"{SpaceRecoveredBytes / MB:F2} MB";
        }
    }

        private long _itemsFound;
        public long ItemsFound { get => _itemsFound; set { SetProperty(ref _itemsFound, value); OnPropertyChanged(nameof(TotalFilesChecked)); } }

        public int CompletedSteps => Modules.Count(m => m.Status == StepStatus.Completed);
        public int TotalSteps => Modules.Count;
        public string StepCounter => $"{CompletedSteps}/{TotalSteps}";
        public int ProblemsFound => Modules.Count(m => m.Status == StepStatus.Failed);
        public string StepCountLabel { get; private set; } = string.Empty;
        private bool _isExecutingPhase = false;
        private string _currentExecutingModuleId = string.Empty;
        private int _startedStepCount = 0;

        private string _summaryTitle = string.Empty;
        public string SummaryTitle { get => _summaryTitle; set => SetProperty(ref _summaryTitle, value); }
        
        private string _summaryMessage = string.Empty;
        public string SummaryMessage { get => _summaryMessage; set => SetProperty(ref _summaryMessage, value); }

        private string _summaryIcon = "Success";
        public string SummaryIcon { get => _summaryIcon; set => SetProperty(ref _summaryIcon, value); }

        private string _summaryReport = string.Empty;
        public string SummaryReport { get => _summaryReport; set => SetProperty(ref _summaryReport, value); }

        private string _logCountLabel = string.Empty;
        public string LogCountLabel { get => _logCountLabel; set => SetProperty(ref _logCountLabel, value); }

        public ICommand StartScanCommand { get; }
        public ICommand ConfirmExecutionCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand CloseCommand { get; }
        public ICommand ViewReportCommand { get; }
        public ICommand CopySummaryCommand { get; }

        public SmartRepairViewModel()
        {
            _logger = App.LoggingService ?? new LoggingService(LogDirectoryResolver.Resolve());
            _eventBus = new SmartRepairEventBus();
            _exclusionService = new ExclusionManager();
            
            _engine = new ModularSmartRepairEngine(_logger, _eventBus, _exclusionService);
            
            var registry = SmartRepairStepRegistry.Instance;
            var legacyEngine = new SmartRepairEngine(_logger);
            var legacyStepsCache = legacyEngine.Steps
                .Where(s => s.Id != 0 && s.Id != 19 && s.Id != 7 && s.Id != 16)
                .ToList();

            foreach (var reg in registry.GetOrderedDedicated())
            {
                if (reg.IsLegacyInjectionPoint)
                {
                    foreach (var step in legacyStepsCache)
                    {
                        _engine.RegisterModule(new LegacyStepAdapterModule(step, _logger, _eventBus, Guid.NewGuid().ToString("N")));
                    }
                    _logger.LogInfo($"[SmartRepairVM] {legacyStepsCache.Count} etapas legadas injetadas via registry.");
                }
                else
                {
                    var module = reg.Factory!(_logger, _eventBus, _exclusionService, Guid.NewGuid().ToString("N"));
                    _engine.RegisterModule(module);
                }
            }

            foreach (var mod in _engine.Modules)
            {
                _logger.LogDebug($"[SmartRepairVM] Módulo registrado: {mod.ModuleId} - {mod.Name}");
            }
            
            StartScanCommand = new AsyncRelayCommand(ExecuteScanAsync, () => CanStartScan);
            ConfirmExecutionCommand = new AsyncRelayCommand(ExecuteRepairAsync, () => CanConfirmExecution);
            CancelCommand = new AsyncRelayCommand(ExecuteCancelAsync, () => IsScanning || IsExecuting);
            CloseCommand = new RelayCommand(ExecuteClose);
            ViewReportCommand = new RelayCommand(ExecuteViewReport);
            CopySummaryCommand = new RelayCommand(ExecuteCopySummary);

            _eventBus.EventPublished += OnEventBusMessage;
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
            
            _uiTimerHandle = UiScheduler.Register(() => OnUiTimerTick(null, EventArgs.Empty), TimeSpan.FromMilliseconds(200));

            InitializeUI();
        }

        private void InitializeUI()
        {
            Modules.Clear();
            foreach (var mod in _engine.GetExecutionPlan())
            {
                Modules.Add(new SmartRepairModuleItem
                {
                    ModuleId = mod.ModuleId,
                    Name = mod.Name,
                    Description = mod.Description,
                    Status = StepStatus.Pending
                });
            }
            OnPropertyChanged(nameof(CompletedSteps));
            OnPropertyChanged(nameof(TotalSteps));
            OnPropertyChanged(nameof(StepCounter));
            OnPropertyChanged(nameof(ProblemsFound));
            OnPropertyChanged(nameof(TotalSpaceRecovered));
            OnLanguageChanged(this, EventArgs.Empty);
        }

        private static string RiskToImpactKey(RiskLevel risk) => risk switch
        {
            RiskLevel.High => "High",
            RiskLevel.Moderate => "Medium",
            _ => "Low"
        };

        private void UpdateCommandStates()
        {
            OnPropertyChanged(nameof(CompletedSteps));
            OnPropertyChanged(nameof(TotalSteps));
            OnPropertyChanged(nameof(StepCounter));
            OnPropertyChanged(nameof(ProblemsFound));
            OnPropertyChanged(nameof(TotalSpaceRecovered));

            OnPropertyChanged(nameof(IsIdle));
            OnPropertyChanged(nameof(CanStartScan));
            OnPropertyChanged(nameof(CanConfirmExecution));
            
            if (StartScanCommand is AsyncRelayCommand s) s.RaiseCanExecuteChanged();
            if (ConfirmExecutionCommand is AsyncRelayCommand c) c.RaiseCanExecuteChanged();
            if (CancelCommand is AsyncRelayCommand x) x.RaiseCanExecuteChanged();
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            StepCountLabel = LocalizationService.Instance.GetString("SmartRepairStepsCount");

            var plan = _engine.GetExecutionPlan();
            foreach (var modItem in Modules)
            {
                var originalMod = plan.FirstOrDefault(p => p.ModuleId == modItem.ModuleId);
                if (originalMod != null)
                {
                    modItem.Name = originalMod.Name;
                    modItem.Description = originalMod.Description;
                }
            }

            if (IsIdle && !IsSimulated && !IsCompleted)
            {
                CurrentStepName = LocalizationService.Instance.GetString("SmartRepairPreparing");
                CurrentStepDescription = LocalizationService.Instance.GetString("SmartRepairInitializing");
            }
            else if (IsCompleted && IsCancelled)
            {
                SummaryTitle = LocalizationService.Instance.GetString("SmartRepairCancelledTitle");
                SummaryMessage = LocalizationService.Instance.GetString("SmartRepairCancelledMessage");
            }
            else if (IsCompleted)
            {
                SummaryTitle = LocalizationService.Instance.GetString("SmartRepairCompletedTitle");
                SummaryMessage = LocalizationService.Instance.GetString("SmartRepairCompletedMessage");
            }
            else if (IsExecuting || IsScanning)
            {
                var activeModule = Modules.FirstOrDefault(m => m.ModuleId == _currentExecutingModuleId && m.Status == StepStatus.Running)
                                ?? Modules.FirstOrDefault(m => m.Status == StepStatus.Running);
                if (activeModule != null)
                {
                    CurrentStepName = activeModule.Name;
                    CurrentStepDescription = activeModule.Description;
                    VoltrisOptimizer.Services.GlobalProgressService.Instance.UpdateProgress(OverallPercent, CurrentStepDescription);
                }
                else
                {
                    if (IsScanning)
                        CurrentStepName = LocalizationService.Instance.GetString("SmartRepairAnalyzing");
                    else if (IsExecuting)
                        CurrentStepName = LocalizationService.Instance.GetString("SmartRepairRepairing");
                    
                    VoltrisOptimizer.Services.GlobalProgressService.Instance.UpdateProgress(OverallPercent, CurrentStepName);
                }
            }
        }

private void OnEventBusMessage(object? sender, SmartRepairEventArgs e)
        {
            // Log assíncrono para não travar a thread do evento
            _ = Task.Run(() => {
                var physicalLogMsg = $"[EventBus:{e.ModuleId}] {e.Message}";
                if (e.EventType == SmartRepairEventType.ErrorRaised)
                    _logger.LogError(physicalLogMsg);
                else if (e.EventType == SmartRepairEventType.OperationFinished || e.EventType == SmartRepairEventType.ModuleFinished)
                    _logger.LogInfo(physicalLogMsg + " (Success)");
                else
                    _logger.LogInfo(physicalLogMsg);
            });

            // Throttling de atualizações da UI (máximo 10 vezes por segundo)
            var now = DateTime.Now;
            if ((now - _lastUiUpdate).TotalMilliseconds < 100) return;
            _lastUiUpdate = now;

            Application.Current.Dispatcher.BeginInvoke(() => 
            {
                var logType = e.EventType == SmartRepairEventType.ErrorRaised ? LogMessageType.Error
                    : e.EventType == SmartRepairEventType.OperationFinished || e.EventType == SmartRepairEventType.ModuleFinished ? LogMessageType.Success
                    : LogMessageType.Info;

                var isAsciiBar = e.Message.Contains("[==") || e.Message.Contains("---") || e.Message.Contains("===");
                
                // OTIMIZAÇÃO: Evitar FirstOrDefault repetido
                var modItem = Modules.FirstOrDefault(m => m.ModuleId == e.ModuleId);
                
                // Adicionar log APENAS se não for barra de progresso ASCII
                if (!isAsciiBar && !string.IsNullOrWhiteSpace(e.Message))
                {
                    string displayName = modItem != null ? modItem.Name : (e.ModuleId == "Engine" ? LocalizationService.Instance.GetString("SmartRepairEngineName") ?? "Motor" : e.ModuleId);
                    
                    LogEntries.Add(new SmartRepairLogEntry 
                    { 
                        Timestamp = e.Timestamp, 
                        Message = $"[{displayName}] {e.Message}",
                        Type = logType
                    });
                }

                // Atualizações da UI APENAS durante fase de execução
                if (modItem != null && _isExecutingPhase)
                {
                    bool needsPropertyChange = false;
                    string displayName = modItem.Name;
                    
                    if (e.EventType == SmartRepairEventType.ModuleStarted || e.EventType == SmartRepairEventType.OperationStarted)
                    {
                        modItem.Status = StepStatus.Running;
                        _currentExecutingModuleId = e.ModuleId;
                        CurrentStepName = displayName;
                        needsPropertyChange = true;

                        if (e.StepIndex >= 0)
                            _startedStepCount = e.StepIndex;
                        else
                        {
                            int idx = Modules.IndexOf(modItem);
                            if (idx >= 0)
                                _startedStepCount = idx;
                        }
                    }
                    else if (e.EventType == SmartRepairEventType.ModuleFinished || e.EventType == SmartRepairEventType.OperationFinished)
                    {
                        modItem.Status = StepStatus.Completed;
                        modItem.ProgressPercent = 100;
                        CurrentStepName = displayName;
                        CurrentStepDescription = e.Message;
                        needsPropertyChange = true;
                        
                        if (e.StepIndex >= 0)
                            _startedStepCount = e.StepIndex + 1;
                        else
                        {
                            int idx = Modules.IndexOf(modItem);
                            if (idx >= 0)
                                _startedStepCount = idx + 1;
                        }
                    }
                    else if (e.EventType == SmartRepairEventType.ErrorRaised)
                    {
                        modItem.Status = StepStatus.Failed;
                        needsPropertyChange = true;
                    }
                    else if (e.EventType == SmartRepairEventType.ProgressChanged)
                    {
                        // OTIMIZAÇÃO: Só atualizar se houver mudança significativa (> 5%)
                        if (e.ProgressPercentage > 0 && Math.Abs(e.ProgressPercentage - modItem.ProgressPercent) > 5)
                        {
                            modItem.ProgressPercent = e.ProgressPercentage;
                        }
                        
                        if (!string.IsNullOrWhiteSpace(e.ModuleId))
                            _currentExecutingModuleId = e.ModuleId;
                        
                        if (!string.IsNullOrWhiteSpace(e.Message))
                            CurrentStepDescription = e.Message;
                    }
                    
                    if (needsPropertyChange)
                    {
                        OnPropertyChanged(nameof(CompletedSteps));
                        OnPropertyChanged(nameof(TotalSteps));
                        OnPropertyChanged(nameof(StepCounter));
                    }
                }

                if (_isExecutingPhase && TotalSteps > 0)
                {
                    int denominator = Math.Max(1, TotalSteps);
                    double stepWeight = 100.0 / denominator;

                    double percent = _startedStepCount * stepWeight;

                    if (e.EventType == SmartRepairEventType.ProgressChanged && e.ProgressPercentage > 0)
                    {
                        percent += (e.ProgressPercentage * stepWeight) / 100.0;
                    }

                    percent = Math.Min(100, Math.Max(0, percent));
                    if (percent > OverallPercent)
                        OverallPercent = (int)percent;
                }

                OnPropertyChanged(nameof(CompletedSteps));
                OnPropertyChanged(nameof(TotalSteps));
                OnPropertyChanged(nameof(StepCounter));
                OnPropertyChanged(nameof(ProblemsFound));
                OnPropertyChanged(nameof(TotalSpaceRecovered));
                OnPropertyChanged(nameof(LogCountLabel));
            });
        }

        private void OnUiTimerTick(object? sender, EventArgs e)
        {
            if (IsScanning || IsExecuting)
            {
                TotalElapsed = _sw.Elapsed.ToString(@"mm\:ss");
                OnPropertyChanged(nameof(TotalElapsedLabel));
            }
        }

        private async Task ExecuteScanAsync()
        {
            if (!CanStartScan) return;

            // Garante que a UI tenha tempo de renderizar antes do scan começar
            // (Task.Run dos módulos completa rápido e suas continuações rodam em prioridade Normal > Render)
            await Task.Delay(50);

            _logger.LogInfo("[SmartRepairVM] ExecuteScanAsync iniciado.");
            
            VoltrisOptimizer.Services.GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("SmartRepairAnalyzing"), isPriority: true);
            
            IsScanning = true;
            IsExecuting = true;
            IsCancelled = false;
            LogEntries.Clear();
            _sw.Restart();
            OverallPercent = 0;
            
            _cts = new CancellationTokenSource();

            LogAdd(LogMessageType.Info, LocalizationService.Instance.GetString("SmartRepairLogStartAnalysis"));
            _logger.LogInfo("[SmartRepairVM] Iniciando scan completo do sistema.");
            
try
            {
                // OTIMIZAÇÃO: Throttling mais agressivo (300ms) para evitar sobrecarga do Dispatcher
                var lastUpdate = DateTime.MinValue;
                var updateInterval = TimeSpan.FromMilliseconds(300);
                
                // OTIMIZAÇÃO: Progress report com throttling rigoroso e Dispatcher prioritário baixo
                var progress = new Progress<RepairProgress>(p => {
                    var now = DateTime.Now;
                    if ((now - lastUpdate) < updateInterval) return;
                    lastUpdate = now;
                    
                    // Usar DispatcherPriority.Background para NÃO bloquear renderização
                    Application.Current.Dispatcher.BeginInvoke(() => {
                        CurrentStepDescription = p.StatusMessage;
                        if (p.OverallPercent > 0)
                            OverallPercent = p.OverallPercent;
                    }, DispatcherPriority.Background);
                });
                
                CurrentStepName = LocalizationService.Instance.GetString("SmartRepairAnalyzing");
                LogAdd(LogMessageType.Info, LocalizationService.Instance.GetString("SmartRepairLogScanAll"));
                _logger.LogInfo("[SmartRepairVM] Chamando _engine.ScanAllAsync...");
                // Executa em background thread para não travar a UI com WMI queries
                _scanResults = await Task.Run(() => _engine.ScanAllAsync(progress, _cts.Token), _cts.Token);
                _logger.LogInfo($"[SmartRepairVM] ScanAllAsync concluído. {_scanResults.Count} módulos escaneados.");

                CurrentStepName = LocalizationService.Instance.GetString("SmartRepairPreparingFixes");
                LogAdd(LogMessageType.Info, LocalizationService.Instance.GetString("SmartRepairLogSimulateAll"));
                _logger.LogInfo("[SmartRepairVM] Chamando _engine.SimulateAllAsync...");
_simResults = await Task.Run(() => _engine.SimulateAllAsync(_scanResults, _cts.Token), _cts.Token);
                _logger.LogInfo($"[SmartRepairVM] SimulateAllAsync concluído. {_simResults.Count} simulações.");
                
// OTIMIZAÇÃO: Calcular totais em background e atualizar UI de uma vez
                long finalTotalSpace = 0;
                long finalTotalItems = 0;
                
                await Task.Run(() => {
                    long totalSpace = 0;
                    long totalItems = 0;
                    
                    foreach(var sim in _simResults.Values)
                    {
                        totalSpace += sim.EstimatedStatistics.SpaceRecoveredBytes;
                        totalItems += sim.EstimatedStatistics.ItemsFound;
                        _logger.LogInfo($"[SmartRepairVM] Sim: SpaceRecovered={sim.EstimatedStatistics.SpaceRecoveredBytes}, Itens={sim.EstimatedStatistics.ItemsFound}");
                    }
                    
                    finalTotalSpace = totalSpace;
                    finalTotalItems = totalItems;
                    
                    // Atualizar UI em bloco único no Dispatcher
                    Application.Current.Dispatcher.InvokeAsync(() => {
                        SpaceRecoveredBytes = totalSpace;
                        ItemsFound = totalItems;
                        IsSimulated = true;
                    });
                });

                // CORREÇÃO: Finalizar GlobalProgressService para widget não travar
                VoltrisOptimizer.Services.GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("SmartRepairAnalysisCompleted"));

                _logger.LogInfo($"[SmartRepairVM] Scan+Simulação completos.");
                 
                // AGUARDAR RENDERIZAÇÃO DA UI ANTES DE MOSTRAR MODAL
                await Task.Delay(100);
                await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                
                // EXIBIR MODAL DE CONFIRMAÇÃO PROFISSIONAL
                _logger.LogInfo("[SmartRepairVM] Abrindo modal de confirmação...");
                
                var confirmationModal = new VoltrisOptimizer.UI.Windows.SmartRepairConfirmationModal
                {
                    Owner = Application.Current.MainWindow
                };
                
                // Carregar módulos no modal (com risco real e seleção padrão segura)
                var executionPlan = _engine.GetExecutionPlan();
                var modulesForConfirmation = Modules.Select(m =>
                {
                    var originalMod = executionPlan.FirstOrDefault(p => p.ModuleId == m.ModuleId);
                    var risk = originalMod?.Capabilities.MaxRiskLevel ?? RiskLevel.Safe;
                    return (
                        Id: m.ModuleId,
                        Name: m.Name,
                        Description: m.Description,
                        Impact: RiskToImpactKey(risk),
                        SelectedByDefault: risk != RiskLevel.High
                    );
                }).ToList();
                
                confirmationModal.LoadModules(modulesForConfirmation, finalTotalSpace, (int)finalTotalItems);
                
                var modalResult = confirmationModal.ShowDialog();
                
                // VERIFICAR RESULTADO DO MODAL
                if (modalResult != true)
                {
                    // USUÁRIO CANCELOU NO MODAL
                    _logger.LogInfo("[SmartRepairVM] Usuário cancelou no modal de confirmação.");
                    LogAdd(LogMessageType.Warning, LocalizationService.Instance.GetString("SmartRepairLogCancelByUser"));
                    SetCancelState();
                    IsScanning = false;
                    IsExecuting = false;
                    _isExecutingPhase = false;
                    _sw.Stop();
                    VoltrisOptimizer.Services.GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("CommonOperationCanceled"));
                    return;
                }
                
                var result = confirmationModal.GetResult();
                if (result == null || !result.Confirmed)
                {
                    _logger.LogInfo("[SmartRepairVM] Confirmação não recebida ou negada.");
                    SetCancelState();
                    IsScanning = false;
                    IsExecuting = false;
                    _isExecutingPhase = false;
                    _sw.Stop();
                    VoltrisOptimizer.Services.GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("CommonOperationCanceled"));
                    return;
                }
                
_logger.LogInfo($"[SmartRepairVM] Usuário confirmou. {result.TotalModules} módulos selecionados.");
                LogAdd(LogMessageType.Info, $"{result.TotalModules} módulos confirmados para reparação.");
                 
                // INICIAR EXECUÇÃO APÓS CONFIRMAÇÃO
                CurrentStepName = LocalizationService.Instance.GetString("SmartRepairRepairing");
                LogAdd(LogMessageType.Info, LocalizationService.Instance.GetString("SmartRepairLogExecuteAll"));
                _logger.LogInfo("[SmartRepairVM] Chamando _engine.ExecuteAllAsync...");
                  
// Reset the UI states before the real execution so the progress bar is clean
                   var execLastUpdate = DateTime.MinValue;
                   var execUpdateInterval = TimeSpan.FromMilliseconds(250);
                   
                   var now = DateTime.Now;
                   if ((now - execLastUpdate) < execUpdateInterval) return;
                   execLastUpdate = now;
                   
                   Application.Current.Dispatcher.BeginInvoke(() => {
                       foreach (var mod in Modules)
                       {
                           mod.Status = StepStatus.Pending;
                           mod.ProgressPercent = 0;
                       }
                       OverallPercent = 0;
                       _startedStepCount = 0;
                       _currentExecutingModuleId = string.Empty;
                       OnPropertyChanged(nameof(CompletedSteps));
                   });
                   _isExecutingPhase = true;
                   
                   var execProgress = new Progress<RepairProgress>(p => {
                       var now = DateTime.Now;
                       if ((now - execLastUpdate) < execUpdateInterval) return;
                       execLastUpdate = now;
                       
                       Application.Current.Dispatcher.BeginInvoke(() => {
                          var isAsciiBar = p.StatusMessage?.Contains("[==") == true || p.StatusMessage?.Contains("---") == true || p.StatusMessage?.Contains("===") == true;
                          if (!isAsciiBar && !string.IsNullOrWhiteSpace(p.StatusMessage))
                          {
                              CurrentStepDescription = p.StatusMessage;
                          }
                          
                          if (!string.IsNullOrEmpty(_currentExecutingModuleId))
                          {
                              var activeMod = Modules.FirstOrDefault(m => m.ModuleId == _currentExecutingModuleId);
                              if (activeMod != null && p.StepPercent > 0)
                              {
                                  activeMod.ProgressPercent = p.StepPercent;
                              }
                          }
                      });
                  });
                  
                  var execResults = await Task.Run(() => _engine.ExecuteAllAsync(_simResults, execProgress, _cts.Token), _cts.Token);
                 _logger.LogInfo($"[SmartRepairVM] ExecuteAllAsync concluído. {execResults.Count} módulos executados.");
                
                IsCompleted = true;
                IsCancelled = false;
                SummaryIcon = "Success";
                LogAdd(LogMessageType.Success, LocalizationService.Instance.GetString("SmartRepairLogSuccess"));
                _logger.LogInfo("[SmartRepairVM] SmartRepair concluído com sucesso.");
                OnLanguageChanged(this, EventArgs.Empty);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInfo("[SmartRepairVM] Reparação cancelada pelo usuário.");
                LogAdd(LogMessageType.Warning, LocalizationService.Instance.GetString("SmartRepairLogCancelByUser"));
                SetCancelState();
            }
            catch (Exception ex)
            {
                _logger.LogError($"[SmartRepairVM] Erro durante reparação: {ex.Message}", ex);
                LogAdd(LogMessageType.Error, string.Format(LocalizationService.Instance.GetString("SmartRepairErrorLog") ?? "Erro: {0}", ex.Message));
                SetCancelState();
            }
            finally
            {
                VoltrisOptimizer.Services.GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("SmartRepairCompleted") ?? "Reparação Inteligente Concluída");
                IsScanning = false;
                IsExecuting = false;
                _isExecutingPhase = false;
                _sw.Stop();
                _logger.LogInfo($"[SmartRepairVM] ExecuteScanAsync finalizado. Tempo total: {_sw.Elapsed.TotalSeconds:F1}s");
                OnPropertyChanged(nameof(LogCountLabel));
            }
        }

        private async Task ExecuteRepairAsync()
        {
            if (!CanConfirmExecution) return;

            _logger.LogInfo("[SmartRepairVM] ExecuteRepairAsync iniciado.");
            
            IsExecuting = true;
            IsCancelled = false;
            _sw.Restart();
            
            _cts = new CancellationTokenSource();

            LogAdd(LogMessageType.Info, LocalizationService.Instance.GetString("SmartRepairLogManualExecute"));
            _isExecutingPhase = true;
            
try
            {
                var lastUpdate = DateTime.MinValue;
                var updateInterval = TimeSpan.FromMilliseconds(250);
                
                var progress = new Progress<RepairProgress>(p => {
                    var now = DateTime.Now;
                    if ((now - lastUpdate) < updateInterval) return;
                    lastUpdate = now;
                    
                    Application.Current.Dispatcher.BeginInvoke(() => {
                        var isAsciiBar = p.StatusMessage?.Contains("[==") == true || p.StatusMessage?.Contains("---") == true || p.StatusMessage?.Contains("===") == true;
                        if (!isAsciiBar && !string.IsNullOrWhiteSpace(p.StatusMessage))
                        {
                            CurrentStepDescription = p.StatusMessage;
                        }
                        
                        if (!string.IsNullOrEmpty(_currentExecutingModuleId))
                        {
                            var activeMod = Modules.FirstOrDefault(m => m.ModuleId == _currentExecutingModuleId);
                            if (activeMod != null && p.StepPercent > 0)
                            {
                                activeMod.ProgressPercent = p.StepPercent;
                            }
                        }
                    });
                });
                
                _logger.LogInfo("[SmartRepairVM] Chamando _engine.ExecuteAllAsync (manual)...");
                var execResults = await Task.Run(() => _engine.ExecuteAllAsync(_simResults, progress, _cts.Token), _cts.Token);
                _logger.LogInfo($"[SmartRepairVM] ExecuteAllAsync concluído (manual). {execResults.Count} módulos.");
                
                IsCompleted = true;
                IsCancelled = false;
                SummaryIcon = "Success";
                LogAdd(LogMessageType.Success, LocalizationService.Instance.GetString("SmartRepairLogSuccess"));
                _logger.LogInfo("[SmartRepairVM] Reparo manual concluído.");
                OnLanguageChanged(this, EventArgs.Empty);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInfo("[SmartRepairVM] Execução manual cancelada pelo usuário.");
                LogAdd(LogMessageType.Warning, LocalizationService.Instance.GetString("SmartRepairLogCancelByUser"));
                SetCancelState();
            }
            catch (Exception ex)
            {
                _logger.LogError($"[SmartRepairVM] Erro na execução manual: {ex.Message}", ex);
                LogAdd(LogMessageType.Error, string.Format(LocalizationService.Instance.GetString("SmartRepairErrorLog") ?? "Erro: {0}", ex.Message));
                SetCancelState();
            }
            finally
            {
                IsExecuting = false;
                _isExecutingPhase = false;
                _sw.Stop();
                _logger.LogInfo($"[SmartRepairVM] ExecuteRepairAsync finalizado. Tempo: {_sw.Elapsed.TotalSeconds:F1}s");
                OnPropertyChanged(nameof(LogCountLabel));
            }
        }

        private async Task ExecuteCancelAsync()
        {
            _logger.LogInfo("[SmartRepairVM] ExecuteCancelAsync chamado pelo usuário.");

            if (_cts != null && !_cts.IsCancellationRequested)
            {
                _logger.LogInfo("[SmartRepairVM] Cancelando CancellationTokenSource...");
                LogAdd(LogMessageType.Warning, LocalizationService.Instance.GetString("SmartRepairLogCancelOp"));
                _cts.Cancel();
            }
            else
            {
                _logger.LogInfo("[SmartRepairVM] Nenhuma operação em andamento para cancelar. Exibindo resumo.");
                SetCancelState();
            }

            await Task.CompletedTask;
        }

        private void ExecuteClose()
        {
            _logger.LogInfo("[SmartRepairVM] ExecuteClose - Navegando para Dashboard.");
            try
            {
                var navService = App.Services?.GetService(typeof(VoltrisOptimizer.Interfaces.INavigationService)) as VoltrisOptimizer.Interfaces.INavigationService;
                if (navService != null)
                {
                    navService.NavigateTo(VoltrisOptimizer.Services.AppPage.Dashboard);
                    _logger.LogInfo("[SmartRepairVM] Navegação para Dashboard executada com sucesso.");
                }
                else
                {
                    _logger.LogWarning("[SmartRepairVM] NavigationService não encontrado via DI. Tentando fallback...");
                    var fallbackNav = new VoltrisOptimizer.Services.NavigationService();
                    fallbackNav.NavigateTo(VoltrisOptimizer.Services.AppPage.Dashboard);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[SmartRepairVM] Erro ao navegar para Dashboard: {ex.Message}", ex);
            }
        }

        private void ExecuteViewReport()
        {
            _logger.LogInfo("[SmartRepairVM] ViewReportCommand executado.");

            // Relatório em formato de texto para manter compatibilidade com a propriedade SummaryReport
            var report = new System.Text.StringBuilder();
            report.AppendLine($"=== {LocalizationService.Instance.GetString("SmartRepairReportHeader").ToUpper()} ===");
            var statusStr = IsCancelled ? LocalizationService.Instance.GetString("SmartRepairReportStatusCancelled").ToUpper() : LocalizationService.Instance.GetString("SmartRepairReportStatusSuccess").ToUpper();
            report.AppendLine($"Status: {statusStr}");
            report.AppendLine(string.Format(LocalizationService.Instance.GetString("SmartRepairReportTime"), _sw.Elapsed.TotalSeconds.ToString("F1")));
            report.AppendLine($"{LocalizationService.Instance.GetString("SmartRepairStepsCount")}: {CompletedSteps}/{Modules.Count}");
            report.AppendLine($"{LocalizationService.Instance.GetString("SmartRepairSpaceRecovered")}: {TotalSpaceRecovered}");
            report.AppendLine($"{LocalizationService.Instance.GetString("SmartRepairProblemsFound")}: {ProblemsFound}");
            report.AppendLine("");

            foreach (var module in Modules)
            {
                var statusIcon = module.Status == StepStatus.Completed ? "âœ“" :
                                 module.Status == StepStatus.Failed ? "âœ—" :
                                 module.Status == StepStatus.Running ? "âŸ³" :
                                 module.Status == StepStatus.Cancelled ? "âŠ˜" :
                                 module.Status == StepStatus.Skipped ? "â†’" : "â—‹";
                report.AppendLine($"  {statusIcon} {module.Name} - {module.Status}");
            }

            SummaryReport = report.ToString();

            try
            {
                // Criar Janela Moderna via C# UI Elements
                var dlg = new System.Windows.Window
                {
                    Title = LocalizationService.Instance.GetString("SmartRepairReportTitle"),
                    Width = 800,
                    Height = 600,
                    WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                    Owner = System.Windows.Application.Current?.MainWindow,
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(13, 17, 23)),
                    WindowStyle = System.Windows.WindowStyle.ToolWindow
                };

                var border = new System.Windows.Controls.Border
                {
                    Padding = new System.Windows.Thickness(25)
                };

                var panel = new System.Windows.Controls.StackPanel();

                var title = new System.Windows.Controls.TextBlock
                {
                    Text = LocalizationService.Instance.GetString("SmartRepairReportHeader"),
                    FontSize = 26,
                    FontWeight = System.Windows.FontWeights.SemiBold,
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White),
                    Margin = new System.Windows.Thickness(0, 0, 0, 20)
                };
                panel.Children.Add(title);

                var scrollViewer = new System.Windows.Controls.ScrollViewer
                {
                    VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled,
                    Height = 460
                };

                var itemsPanel = new System.Windows.Controls.StackPanel();

                // Cabeçalho de Status
                var statusText = new System.Windows.Controls.TextBlock
                {
                    Text = string.Format(LocalizationService.Instance.GetString("SmartRepairReportStatus"), 
                            IsCancelled ? LocalizationService.Instance.GetString("SmartRepairReportStatusCancelled") : LocalizationService.Instance.GetString("SmartRepairReportStatusSuccess"),
                            $"{CompletedSteps}/{Modules.Count}", ProblemsFound),
                    FontSize = 14,
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 255, 127)),
                    Margin = new System.Windows.Thickness(0, 0, 0, 15)
                };
                itemsPanel.Children.Add(statusText);

                foreach (var entry in LogEntries)
                {
                    // Remover strings que sejam puramente '=' ou traços de ASCII ruins
                    if (entry.Message.Contains("=======") || entry.Message.Contains("-------"))
                        continue;

                    var msg = new System.Windows.Controls.TextBlock
                    {
                        Text = $"[{entry.FormattedTimestamp}] {entry.Message}",
                        Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(139, 148, 158)),
                        FontSize = 12,
                        FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                        TextWrapping = System.Windows.TextWrapping.Wrap,
                        Margin = new System.Windows.Thickness(0, 2, 0, 2)
                    };
                    
                    if (entry.Type == LogMessageType.Error)
                        msg.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 99, 71));
                    else if (entry.Type == LogMessageType.Success)
                        msg.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(80, 200, 120));
                        
                    itemsPanel.Children.Add(msg);
                }

                scrollViewer.Content = itemsPanel;
                panel.Children.Add(scrollViewer);
                border.Child = panel;
                dlg.Content = border;

                dlg.ShowDialog();
            }
            catch (Exception ex)
            {
                _logger.LogError($"[SmartRepairVM] Erro ao exibir relatório: {ex.Message}", ex);
            }
        }

        private void ExecuteCopySummary()
        {
            _logger.LogInfo("[SmartRepairVM] CopySummaryCommand executado.");
            try
            {
                var summary = $"{LocalizationService.Instance.GetString("SmartRepairTitle")} {(IsCancelled ? LocalizationService.Instance.GetString("SmartRepairReportStatusCancelled") : LocalizationService.Instance.GetString("SmartRepairReportStatusSuccess"))}\n" +
                              $"{LocalizationService.Instance.GetString("SmartRepairStepsCount")}: {CompletedSteps}/{Modules.Count}\n" +
                              $"{LocalizationService.Instance.GetString("SmartRepairProblemsFound")}: {ProblemsFound}\n" +
                              $"{LocalizationService.Instance.GetString("SmartRepairSpaceRecovered")}: {TotalSpaceRecovered}\n" +
                              $"{LocalizationService.Instance.GetString("ElapsedWord")}: {_sw.Elapsed.TotalSeconds:F1}s";

                System.Windows.Clipboard.SetText(summary);
                LogAdd(LogMessageType.Success, LocalizationService.Instance.GetString("SmartRepairClipboardSuccess"));
                _logger.LogInfo("[SmartRepairVM] Resumo copiado para clipboard.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[SmartRepairVM] Erro ao copiar resumo: {ex.Message}", ex);
                LogAdd(LogMessageType.Error, LocalizationService.Instance.GetString("SmartRepairClipboardError"));
            }
        }

        private void SetCancelState()
        {
            IsCompleted = true;
            IsCancelled = true;
            SummaryIcon = "Warning";
            _startedStepCount = 0;
            _currentExecutingModuleId = string.Empty;
            foreach (var mod in Modules.Where(m => m.Status == StepStatus.Running))
            {
                mod.Status = StepStatus.Cancelled;
            }
            OnPropertyChanged(nameof(CompletedSteps));
            OnPropertyChanged(nameof(TotalSteps));
            OnPropertyChanged(nameof(StepCounter));
            OnPropertyChanged(nameof(ProblemsFound));
            OnLanguageChanged(this, EventArgs.Empty);
        }

        private void LogAdd(LogMessageType type, string message)
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                LogEntries.Add(new SmartRepairLogEntry
                {
                    Timestamp = DateTime.Now,
                    Message = message,
                    Type = type
                });
                LogCountLabel = string.Format(LocalizationService.Instance.GetString("SmartRepairLogEvents"), LogEntries.Count);
            });
        }

        public void Reset()
        {
            _logger.LogInfo("[SmartRepairVM] Resetando estado do SmartRepair para nova execução.");
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }
            IsScanning = false;
            IsExecuting = false;
            IsSimulated = false;
            IsCompleted = false;
            IsCancelled = false;
            OverallPercent = 0;
            SpaceRecoveredBytes = 0;
            ItemsFound = 0;
            CurrentStepName = string.Empty;
            CurrentStepDescription = string.Empty;
            SummaryIcon = "Success";
            SummaryTitle = string.Empty;
            SummaryMessage = string.Empty;
            _startedStepCount = 0;
            _currentExecutingModuleId = string.Empty;
            _scanResults.Clear();
            _simResults.Clear();
            LogEntries.Clear();
            _sw.Reset();
            _logger.LogInfo("[SmartRepairVM] Estado resetado com sucesso.");
            OnLanguageChanged(this, EventArgs.Empty);
        }

        protected override void OnDisposing()
        {
            _uiTimerHandle?.Dispose();
            _eventBus.EventPublished -= OnEventBusMessage;
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            _cts?.Cancel();
            _cts?.Dispose();
            base.OnDisposing();
        }
    }
}


