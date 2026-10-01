using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using VoltrisOptimizer.Core.SystemIntelligenceProfiler;
using VoltrisOptimizer.Core.SystemIntelligenceProfiler.Models;
using ISystemProfiler = VoltrisOptimizer.Interfaces.ISystemProfiler;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.UI.ViewModels
{
    public class ApplyAllViewModel : INotifyPropertyChanged
    {
        private readonly StateDetectionEngine _stateDetector;
        private readonly IntelligentOptimizationExecutor _executor;
        private readonly ILoggingService _logger;
        private readonly ISystemProfiler _systemProfiler;
        private readonly IDecisionEngine _decisionEngine;
        private readonly VoltrisOptimizer.Core.Optimizers.OptimizationProvider _optimizationProvider;
        
        private CancellationTokenSource _cancellationTokenSource;
        private bool _isExecuting;
        private string _applyButtonText = LocalizationService.Instance.GetString("ApplyAllSmart");
        private double _overallProgress;
        private string _progressText = LocalizationService.Instance.GetString("ReadyToOptimize");
        private string _analysisSummary = LocalizationService.Instance.GetString("AnalyzingSystemState");
        private string _hardwareProfileText = LocalizationService.Instance.GetString("DetectingHardware");
        private Brush _hardwareProfileColor = Brushes.Gray;
        private OptimizationMode _selectedExecutionMode = OptimizationMode.Smart;
        private HardwareProfile _detectedHardware;

        public ApplyAllViewModel(
            StateDetectionEngine stateDetector,
            IntelligentOptimizationExecutor executor,
            ILoggingService logger,
            ISystemProfiler systemProfiler,
            IDecisionEngine decisionEngine)
        {
            _stateDetector = stateDetector ?? throw new ArgumentNullException(nameof(stateDetector));
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _systemProfiler = systemProfiler ?? throw new ArgumentNullException(nameof(systemProfiler));
            _decisionEngine = decisionEngine ?? throw new ArgumentNullException(nameof(decisionEngine));
            _optimizationProvider = new VoltrisOptimizer.Core.Optimizers.OptimizationProvider();

            Optimizations = new ObservableCollection<OptimizationItemViewModel>();
            ApplyAllCommand = new RelayCommand(async () => await ExecuteApplyAllAsync(), () => !_isExecuting);
            CancelCommand = new RelayCommand(CancelExecution, () => _isExecuting);
        }

        public ObservableCollection<OptimizationItemViewModel> Optimizations { get; }
        
        public ICommand ApplyAllCommand { get; }
        public ICommand CancelCommand { get; }

        public bool IsExecuting
        {
            get => _isExecuting;
            set
            {
                _isExecuting = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ApplyButtonText));
                ((RelayCommand)ApplyAllCommand).RaiseCanExecuteChanged();
                ((RelayCommand)CancelCommand).RaiseCanExecuteChanged();
            }
        }

        public string ApplyButtonText => IsExecuting ? LocalizationService.Instance.GetString("Applying") : LocalizationService.Instance.GetString("ApplyAllSmart");

        public double OverallProgress
        {
            get => _overallProgress;
            set
            {
                _overallProgress = value;
                OnPropertyChanged();
            }
        }

        public string ProgressText
        {
            get => _progressText;
            set
            {
                _progressText = value;
                OnPropertyChanged();
            }
        }

        public string AnalysisSummary
        {
            get => _analysisSummary;
            set
            {
                _analysisSummary = value;
                OnPropertyChanged();
            }
        }

        public string HardwareProfileText
        {
            get => _hardwareProfileText;
            set
            {
                _hardwareProfileText = value;
                OnPropertyChanged();
            }
        }

        public Brush HardwareProfileColor
        {
            get => _hardwareProfileColor;
            set
            {
                _hardwareProfileColor = value;
                OnPropertyChanged();
            }
        }

        public OptimizationMode SelectedExecutionMode
        {
            get => _selectedExecutionMode;
            set
            {
                _selectedExecutionMode = value;
                OnPropertyChanged();
                UpdateEstimatedTime();
            }
        }

        public int TotalCount => Optimizations.Count;
        public int ToApplyCount => Optimizations.Count(x => x.IsEnabled && x.IsChecked);
        public int AlreadyAppliedCount => Optimizations.Count(x => x.Status == OptimizationItemStatus.AlreadyApplied);
        public int IncompatibleCount => Optimizations.Count(x => x.Status == OptimizationItemStatus.Incompatible);

        public string EstimatedTime => CalculateEstimatedTime();

        public async Task InitializeAsync(CancellationToken ct = default)
        {
            GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("AnalyzingSystemForOptimizations"), false);
            try
            {
                _logger.Log(LogLevel.Info, LogCategory.Optimization,
                    "Initializing Professional Apply All dialog", source: "ApplyAllViewModel");

                ProgressText = LocalizationService.Instance.GetString("AuditingSystemConfiguration");
                GlobalProgressService.Instance.UpdateProgress(10, LocalizationService.Instance.GetString("AuditingSystemConfiguration"));
                var audit = await _systemProfiler.AnalyzeAsync(ct);
                
                ProgressText = LocalizationService.Instance.GetString("GeneratingOptimizationRecommendations");
                GlobalProgressService.Instance.UpdateProgress(30, LocalizationService.Instance.GetString("GeneratingOptimizationRecommendations"));
                
                var currentProfile = SettingsService.Instance.Settings.IntelligentProfile;
                var userProfile = MapIntelligentProfileToUserProfile(currentProfile);
                
                // [FIX:STRATEGY-3-STATES] A ESCOLHA DO USUÁRIO NÃO É MAIS SOBRESCRITA
                // =====================================================================
                // BUG ORIGINAL (grave, e silencioso):
                //
                //     Priority = (currentProfile == GamerCompetitive || GamerSinglePlayer)
                //         ? "Performance" : "Balanced"
                //
                // O "Aplicar tudo" INVENTAVA a estratégia a partir do perfil e
                // jogava fora a que o usuário tinha escolhido na tela. E o pior
                // caso era o mais caro: quem escolheu "Gamer" + "Máxima Retenção de
                // Bateria" recebia "Performance" — agressividade máxima, exatamente
                // o oposto do pedido. É um dos caminhos para a limpeza apagar cache
                // demais (inclusive o do NuGet, que já quebrou builds).
                //
                // Agora a estratégia vem de onde ela foi realmente escolhida. A
                // ordem de leitura é deliberada:
                //
                //   1. `Settings.PerformanceStrategyToken` — a escolha da tela,
                //      gravada no questionário.
                //   2. `Answers.Priority` do último relatório do profiler — para
                //      quem já tinha escolhido antes desta versão.
                //   3. "Equilibrado" — o estado que não promete nada extremo.
                string strategyToken = ResolveStrategyToken();
                var userAnswers = new UserAnswers
                {
                    Profile = userProfile,
                    Priority = strategyToken
                };

                _logger.LogInfo(
                    $"[ApplyAll] estrategia resolvida: token='{strategyToken}' " +
                    $"-> '{VoltrisOptimizer.Services.Performance.PerformanceStrategyMap.ToCanonical(VoltrisOptimizer.Services.Performance.PerformanceStrategyMap.ToStrategy(strategyToken))}' " +
                    $"(perfil={currentProfile}). A escolha do usuario e respeitada; antes este metodo a sobrescrevia.");

                // [FIX:UNICA-FONTE] A estratégia NÃO é aplicada aqui.
                //
                // Este bloco chamava o `o servico legado`, que gravava
                // EPP 0/128/255 e trocava o plano de energia por conta própria.
                // Era a segunda via de escrita, e ela rodava DEPOIS do perfil
                // inteligente ter aplicado a tabela — sobrescrevendo o resultado.
                //
                // A tela "Aplicar Tudo" é um atalho para executar as otimizações,
                // e a energia é uma delas. Quem a executa agora é o
                // `ProfilePowerCoordinator`, para o perfil atual e para o tier
                // real da máquina. O token da estratégia continua registrado em
                // `AppSettings` como escolha do usuário e aparece no resumo, mas
                // ele não vira mais instrução de escrita por fora do perfil.
                _logger.LogInfo(
                    $"[ApplyAll] Estrategia '{strategyToken}' registrada para o perfil {currentProfile}. " +
                    "Energia aplicada pelo Perfil Inteligente (fonte unica).");

                try
                {
                    VoltrisOptimizer.Services.Power.ProfilePowerCoordinator.ApplyNow(
                        currentProfile, _logger);
                }
                catch (Exception strategyEx)
                {
                    _logger.LogWarning($"[ApplyAll] falha ao aplicar a energia do perfil: {strategyEx.Message}");
                }

                var auditData = audit as ProfilerReport;
                var recommendations = _decisionEngine.Evaluate(auditData?.Audit ?? new AuditData(), userAnswers);
                
                ProgressText = LocalizationService.Instance.GetString("AnalyzingOptimizationRequirements");
                GlobalProgressService.Instance.UpdateProgress(50, LocalizationService.Instance.GetString("AnalyzingOptimizationRequirements"));
                var categorized = CategorizeOptimizations(recommendations);
                
                int totalGroups = categorized.Length;
                int groupIdx = 0;
                foreach (var group in categorized)
                {
                    ct.ThrowIfCancellationRequested();
                    
                    var state = await _stateDetector.CaptureCurrentStateAsync(group.Category, ct);
                    _detectedHardware = state.HardwareProfile;
                    
                    foreach (var opt in group.Optimizations)
                    {
                        var status = _stateDetector.AnalyzeOptimizationStatus(opt, state);
                        var itemVm = new OptimizationItemViewModel
                        {
                            Optimization = opt,
                            Status = MapStatus(status),
                            StatusIcon = GetStatusIcon(status),
                            StatusColor = GetStatusColor(status),
                            StatusDisplay = GetStatusDisplay(status),
                            StatusBadgeBackground = GetStatusBadgeBackground(status),
                            StatusBadgeForeground = GetStatusBadgeForeground(status),
                            IsEnabled = status == OptimizationStatus.NeedsApplication,
                            IsChecked = status == OptimizationStatus.NeedsApplication,
                            Description = opt.Description ?? LocalizationService.Instance.GetString("SystemOptimization")
                        };
                        
                        Optimizations.Add(itemVm);
                    }

                    groupIdx++;
                    GlobalProgressService.Instance.UpdateProgress(50 + (int)((double)groupIdx / totalGroups * 40), string.Format(LocalizationService.Instance.GetString("CategoryAnalyzed"), groupIdx, totalGroups));
                }

                UpdateHardwareProfileInfo();
                
                AnalysisSummary = string.Format(LocalizationService.Instance.GetString("AnalysisCompleteSummary"), ToApplyCount, AlreadyAppliedCount, IncompatibleCount);
                
                ProgressText = LocalizationService.Instance.GetString("AnalysisComplete");
                
                _logger.Log(LogLevel.Success, LogCategory.Optimization,
                    $"Analysis completed: {TotalCount} total, {ToApplyCount} to apply", 
                    source: "ApplyAllViewModel");
                    
                OnPropertyChanged(nameof(TotalCount));
                OnPropertyChanged(nameof(ToApplyCount));
                OnPropertyChanged(nameof(AlreadyAppliedCount));
                OnPropertyChanged(nameof(IncompatibleCount));
                OnPropertyChanged(nameof(EstimatedTime));

                GlobalProgressService.Instance.UpdateProgress(100, LocalizationService.Instance.GetString("AnalysisComplete"));
                GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("SystemAnalysisComplete"));
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to initialize ApplyAllViewModel: {ex.Message}", ex);
                AnalysisSummary = string.Format(LocalizationService.Instance.GetString("ErrorDuringAnalysis"), ex.Message);
                GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("ErrorDuringAnalysisOp"), ex.Message));
            }
        }

        public async Task ExecuteApplyAllAsync()
        {
            if (IsExecuting) return;

            GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("ApplyingAllOptimizations"), true);

            try
            {
                IsExecuting = true;
                _cancellationTokenSource = new CancellationTokenSource();
                var ct = _cancellationTokenSource.Token;

                _logger.Log(LogLevel.Info, LogCategory.Optimization,
                    $"Starting optimization execution in {SelectedExecutionMode} mode", 
                    source: "ApplyAllViewModel");

                var selected = Optimizations
                    .Where(x => x.IsEnabled && x.IsChecked)
                    .Select(x => x.Optimization)
                    .ToList();

                if (!selected.Any())
                {
                    ProgressText = LocalizationService.Instance.GetString("NoOptimizationsSelected");
                    GlobalNotificationService.ShowInfo(LocalizationService.Instance.GetString("ApplyAll"), LocalizationService.Instance.GetString("NoOptimizationsSelectedMsg"));
                    GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("NoOptimizationsSelectedOp"));
                    return;
                }

                GlobalProgressService.Instance.UpdateProgress(20, string.Format(LocalizationService.Instance.GetString("ExecutingOptimizations"), selected.Count));

                var result = await _executor.ApplyOptimizationsAsync(
                    selected,
                    SelectedExecutionMode,
                    ct);

                App.TelemetryService?.TrackEvent("OPTIMIZATION_EXECUTE", "SmartSummary", SelectedExecutionMode.ToString(), 
                    metadata: new { Count = selected.Count, Success = result.TotalApplied, Failed = result.TotalFailed });

                GlobalProgressService.Instance.UpdateProgress(90, LocalizationService.Instance.GetString("FinalizingInterfaceUpdate"));
                UpdateResults(result);
                GlobalNotificationService.ShowSuccess(LocalizationService.Instance.GetString("ApplyAll"), LocalizationService.Instance.GetString("AllOptimizationsApplied"));

                _logger.Log(LogLevel.Success, LogCategory.Optimization,
                    $"Execution completed: {result.TotalApplied} applied, {result.TotalFailed} failed",
                    source: "ApplyAllViewModel");

                GlobalProgressService.Instance.UpdateProgress(100, string.Format(LocalizationService.Instance.GetString("AppliedAndFailed"), result.TotalApplied, result.TotalFailed));
                GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("OptimizationsApplied"), result.TotalApplied));
            }
            catch (OperationCanceledException)
            {
                App.TelemetryService?.TrackEvent("OPTIMIZATION_CANCEL", "SmartSummary", SelectedExecutionMode.ToString());

                ProgressText = LocalizationService.Instance.GetString("ExecutionCancelled");
                GlobalNotificationService.ShowWarning(LocalizationService.Instance.GetString("ApplyAll"), LocalizationService.Instance.GetString("ExecutionCancelledMsg"));
                _logger.Log(LogLevel.Warning, LogCategory.Optimization, 
                    "Optimization execution cancelled", source: "ApplyAllViewModel");
                GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("ExecutionCancelledOp"));
            }
            catch (Exception ex)
            {
                ProgressText = string.Format(LocalizationService.Instance.GetString("Error"), ex.Message);
                GlobalNotificationService.ShowError(LocalizationService.Instance.GetString("ApplyAll"), string.Format(LocalizationService.Instance.GetString("ErrorInExecution"), ex.Message));
                _logger.LogError($"Optimization execution failed: {ex.Message}", ex);
                GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("ErrorInExecutionOp"), ex.Message));
            }
            finally
            {
                IsExecuting = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        public void CancelExecution()
        {
            _cancellationTokenSource?.Cancel();
            ProgressText = LocalizationService.Instance.GetString("CancellingExecution");
        }

        private void UpdateResults(IntelligentOptimizationResult result)
        {
            OverallProgress = 100;
            ProgressText = string.Format(LocalizationService.Instance.GetString("CompleteSummary"), result.TotalApplied, result.TotalFailed);

            foreach (var executionResult in result.ExecutionResults)
            {
                var item = Optimizations.FirstOrDefault(x => x.Name == executionResult.OptimizationName);
                if (item != null)
                {
                    item.IsProcessing = false;
                    item.Progress = 100;
                    
                    switch (executionResult.Status)
                    {
                        case OptimizationExecutionStatus.Success:
                            item.Status = OptimizationItemStatus.Applied;
                            item.StatusIcon = "✓";
                            item.StatusColor = Brushes.Green;
                            item.StatusDisplay = LocalizationService.Instance.GetString("StatusApplied");
                            item.StatusBadgeBackground = Brushes.LightGreen;
                            item.StatusBadgeForeground = Brushes.DarkGreen;
                            break;
                            
                        case OptimizationExecutionStatus.Failed:
                        case OptimizationExecutionStatus.ValidationFailedNoRollback:
                            item.Status = OptimizationItemStatus.Failed;
                            item.StatusIcon = "✗";
                            item.StatusColor = Brushes.Red;
                            item.StatusDisplay = LocalizationService.Instance.GetString("StatusFailed");
                            item.StatusBadgeBackground = Brushes.LightPink;
                            item.StatusBadgeForeground = Brushes.DarkRed;
                            item.ErrorMessage = executionResult.ErrorMessage;
                            break;
                            
                        case OptimizationExecutionStatus.ValidationFailedWithRollback:
                            item.Status = OptimizationItemStatus.Rollback;
                            item.StatusIcon = "↺";
                            item.StatusColor = Brushes.Orange;
                            item.StatusDisplay = LocalizationService.Instance.GetString("StatusRolledBack");
                            item.StatusBadgeBackground = Brushes.LightYellow;
                            item.StatusBadgeForeground = Brushes.DarkOrange;
                            item.ErrorMessage = executionResult.ErrorMessage;
                            break;
                    }
                }
            }

            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(ToApplyCount));
            OnPropertyChanged(nameof(AlreadyAppliedCount));
            OnPropertyChanged(nameof(IncompatibleCount));
        }

        private void UpdateHardwareProfileInfo()
        {
            if (Optimizations.Any() && _detectedHardware != null)
            {
                var hardware = _detectedHardware;
                
                HardwareProfileText = hardware?.Classification.ToString() ?? LocalizationService.Instance.GetString("Unknown");
                HardwareProfileColor = hardware?.Classification switch
                {
                    HardwareClass.LowEnd => Brushes.Red,
                    HardwareClass.MidRange => Brushes.Orange,
                    HardwareClass.HighEnd => Brushes.Yellow,
                    HardwareClass.Workstation => Brushes.Green,
                    HardwareClass.Server => Brushes.Blue,
                    _ => Brushes.Gray
                };
            }
        }

        private void UpdateEstimatedTime()
        {
            OnPropertyChanged(nameof(EstimatedTime));
        }

        private string CalculateEstimatedTime()
        {
            var selectedCount = Optimizations.Count(x => x.IsEnabled && x.IsChecked);
            var avgTimePerOptimization = 2.5;
            
            var totalTimeSeconds = selectedCount * avgTimePerOptimization;
            
            return totalTimeSeconds switch
            {
                < 60 => string.Format(LocalizationService.Instance.GetString("TimeSeconds"), totalTimeSeconds),
                < 3600 => string.Format(LocalizationService.Instance.GetString("TimeMinutes"), totalTimeSeconds / 60),
                _ => string.Format(LocalizationService.Instance.GetString("TimeHours"), totalTimeSeconds / 3600)
            };
        }

        private OptimizationCategoryGroup[] CategorizeOptimizations(ProfilerReport report)
        {
            var groups = new Dictionary<string, OptimizationCategoryGroup>();

            foreach (var recommendation in report.Recommendations)
            {
                var optimization = _optimizationProvider.GetOptimization(recommendation);
                if (optimization == null) continue;

                var categoryName = optimization.Category?.Name ?? "General";
                if (!groups.TryGetValue(categoryName, out var group))
                {
                    group = new OptimizationCategoryGroup
                    {
                        Category = optimization.Category ?? new VoltrisOptimizer.Core.SystemIntelligenceProfiler.Models.OptimizationCategory { Name = categoryName }
                    };
                    
                    if (group.Category.RegistryKeys == null) group.Category.RegistryKeys = new List<string>();
                    if (group.Category.Services == null) group.Category.Services = new List<string>();
                    
                    groups[categoryName] = group;
                }

                if (optimization.TargetRegistryValues != null)
                {
                    foreach (var key in optimization.TargetRegistryValues.Keys)
                    {
                        if (!group.Category.RegistryKeys.Contains(key))
                            group.Category.RegistryKeys.Add(key);
                    }
                }

                if (optimization.TargetServiceStates != null)
                {
                    foreach (var service in optimization.TargetServiceStates.Keys)
                    {
                        if (!group.Category.Services.Contains(service))
                            group.Category.Services.Add(service);
                    }
                }

                group.Optimizations.Add(optimization);
            }

            return groups.Values.ToArray();
        }

        private OptimizationItemStatus MapStatus(OptimizationStatus status)
        {
            return status switch
            {
                OptimizationStatus.AlreadyApplied => OptimizationItemStatus.AlreadyApplied,
                OptimizationStatus.NeedsApplication => OptimizationItemStatus.Pending,
                OptimizationStatus.Incompatible => OptimizationItemStatus.Incompatible,
                OptimizationStatus.ConflictsDetected => OptimizationItemStatus.Conflict,
                OptimizationStatus.RequiresElevation => OptimizationItemStatus.RequiresAdmin,
                _ => OptimizationItemStatus.Unknown
            };
        }

        private string GetStatusIcon(OptimizationStatus status)
        {
            return status switch
            {
                OptimizationStatus.AlreadyApplied => "✓",
                OptimizationStatus.NeedsApplication => "○",
                OptimizationStatus.Incompatible => "⊘",
                OptimizationStatus.ConflictsDetected => "⚠",
                OptimizationStatus.RequiresElevation => "⚡",
                _ => "?"
            };
        }

        private Brush GetStatusColor(OptimizationStatus status)
        {
            return status switch
            {
                OptimizationStatus.AlreadyApplied => Brushes.Green,
                OptimizationStatus.NeedsApplication => Brushes.Blue,
                OptimizationStatus.Incompatible => Brushes.Red,
                OptimizationStatus.ConflictsDetected => Brushes.Orange,
                OptimizationStatus.RequiresElevation => Brushes.Purple,
                _ => Brushes.Gray
            };
        }

        private string GetStatusDisplay(OptimizationStatus status)
        {
            return status switch
            {
                OptimizationStatus.AlreadyApplied => LocalizationService.Instance.GetString("StatusApplied"),
                OptimizationStatus.NeedsApplication => LocalizationService.Instance.GetString("StatusPending"),
                OptimizationStatus.Incompatible => LocalizationService.Instance.GetString("StatusIncompatible"),
                OptimizationStatus.ConflictsDetected => LocalizationService.Instance.GetString("StatusConflict"),
                OptimizationStatus.RequiresElevation => LocalizationService.Instance.GetString("StatusNeedsAdmin"),
                _ => LocalizationService.Instance.GetString("Unknown")
            };
        }

        private Brush GetStatusBadgeBackground(OptimizationStatus status)
        {
            return status switch
            {
                OptimizationStatus.AlreadyApplied => Brushes.LightGreen,
                OptimizationStatus.NeedsApplication => Brushes.LightBlue,
                OptimizationStatus.Incompatible => Brushes.LightPink,
                OptimizationStatus.ConflictsDetected => Brushes.LightYellow,
                OptimizationStatus.RequiresElevation => Brushes.MediumPurple,
                _ => Brushes.LightGray
            };
        }

        private Brush GetStatusBadgeForeground(OptimizationStatus status)
        {
            return status switch
            {
                OptimizationStatus.AlreadyApplied => Brushes.DarkGreen,
                OptimizationStatus.NeedsApplication => Brushes.DarkBlue,
                OptimizationStatus.Incompatible => Brushes.DarkRed,
                OptimizationStatus.ConflictsDetected => Brushes.DarkOrange,
                OptimizationStatus.RequiresElevation => Brushes.Purple,
                _ => Brushes.DarkGray
            };
        }

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// [FIX:STRATEGY-3-STATES] Obtém o motor de energia do contêiner, se
        /// estiver registrado. Devolve nulo em vez de estourar: a aplicação das
        /// otimizações continua mesmo sem poder mexer no EPP, e o log do
        /// <c>o servico legado</c> registra que o motor faltou.
        /// </summary>
        private VoltrisOptimizer.Core.Body.IPowerArm? ResolvePowerArm()
        {
            try
            {
                return Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                    .GetService<VoltrisOptimizer.Core.Body.IPowerArm>(App.Services);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ApplyAll] nao foi possivel obter o IPowerArm: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// [FIX:STRATEGY-3-STATES] Descobre a estratégia que o USUÁRIO escolheu.
        ///
        /// Três fontes, na ordem de confiança, cada uma registrada no log:
        ///
        ///   1. A escolha da tela de Perfil Inteligente, gravada em
        ///      `Settings.PerformanceStrategyToken` pelo questionário.
        ///   2. O `Answers.Priority` do último relatório do profiler — cobre quem
        ///      já escolheu antes desta versão existir.
        ///   3. "Equilibrado". Nunca "Performance Extrema": um dado ausente não
        ///      pode virar a escolha mais agressiva do conjunto.
        /// </summary>
        private string ResolveStrategyToken()
        {
            string fromSettings = SettingsService.Instance.Settings.PerformanceStrategyToken;
            if (!string.IsNullOrWhiteSpace(fromSettings))
            {
                _logger.LogInfo($"[ApplyAll] estrategia origem: tela do questionario ('{fromSettings}')");
                return fromSettings;
            }

            // [FIX:STRATEGY-3-STATES] A segunda fonte é o estado SALVO do
            // questionário (`Profiler/state.json`), e não o profiler em memória:
            // é o mesmo lugar onde a escolha foi gravada, então não depende de o
            // relatório existir nesta sessão — o que acontecia antes e fazia a
            // estratégia cair silenciosamente no padrão.
            try
            {
                var state = new VoltrisOptimizer.Core.SystemIntelligenceProfiler.ProfileStore().Load();
                string? fromReport = state?.Answers?.Priority;
                if (!string.IsNullOrWhiteSpace(fromReport))
                {
                    _logger.LogInfo($"[ApplyAll] estrategia origem: estado salvo do questionario ('{fromReport}')");
                    return fromReport!;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ApplyAll] nao foi possivel ler o estado do questionario para a estrategia: {ex.Message}");
            }

            _logger.LogInfo("[ApplyAll] estrategia origem: padrao 'Equilibrado' (nenhuma escolha salva)");
            return VoltrisOptimizer.Services.Performance.PerformanceStrategyMap.TokenBalanced;
        }

        private UserProfile MapIntelligentProfileToUserProfile(IntelligentProfileType profile)
        {
            return profile switch
            {
                IntelligentProfileType.GamerCompetitive => UserProfile.GamerCompetitive,
                IntelligentProfileType.GamerSinglePlayer => UserProfile.GamerSinglePlayer,
                IntelligentProfileType.GamerSimulation => UserProfile.GamerSimulation,
                IntelligentProfileType.GamerMMO => UserProfile.GamerMMO,
                IntelligentProfileType.GamerStrategy => UserProfile.GamerStrategy,
                IntelligentProfileType.WorkOffice => UserProfile.WorkOffice,
                IntelligentProfileType.CreativeVideoEditing => UserProfile.CreativeVideoEditing,
                IntelligentProfileType.DeveloperProgramming => UserProfile.DeveloperProgramming,
                IntelligentProfileType.GeneralBalanced => UserProfile.GeneralBalanced,
                IntelligentProfileType.EnterpriseSecure => UserProfile.EnterpriseSecure,
                _ => UserProfile.GeneralBalanced
            };
        }

        private IntelligentProfileType MapUserProfileToIntelligentProfile(UserProfile profile)
        {
            return profile switch
            {
                UserProfile.GamerCompetitive => IntelligentProfileType.GamerCompetitive,
                UserProfile.GamerSinglePlayer => IntelligentProfileType.GamerSinglePlayer,
                UserProfile.GamerSimulation => IntelligentProfileType.GamerSimulation,
                UserProfile.GamerMMO => IntelligentProfileType.GamerMMO,
                UserProfile.GamerStrategy => IntelligentProfileType.GamerStrategy,
                UserProfile.WorkOffice => IntelligentProfileType.WorkOffice,
                UserProfile.CreativeVideoEditing => IntelligentProfileType.CreativeVideoEditing,
                UserProfile.DeveloperProgramming => IntelligentProfileType.DeveloperProgramming,
                UserProfile.GeneralBalanced => IntelligentProfileType.GeneralBalanced,
                UserProfile.EnterpriseSecure => IntelligentProfileType.EnterpriseSecure,
                _ => IntelligentProfileType.GeneralBalanced
            };
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class OptimizationItemViewModel : INotifyPropertyChanged
    {
        private bool _isChecked = true;
        private bool _isProcessing;
        private double _progress;
        private string _errorMessage;
        private OptimizationItemStatus _status;

        public Optimization Optimization { get; set; }
        public string Name => Optimization?.Name ?? LocalizationService.Instance.GetString("Unknown");
        public string Description { get; set; }
        public string StatusIcon { get; set; }
        public Brush StatusColor { get; set; }
        public string StatusDisplay { get; set; }
        public Brush StatusBadgeBackground { get; set; }
        public Brush StatusBadgeForeground { get; set; }

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                _isChecked = value;
                OnPropertyChanged();
            }
        }

        public bool IsEnabled { get; set; }
        public bool IsProcessing
        {
            get => _isProcessing;
            set
            {
                _isProcessing = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ProgressText));
            }
        }

        public double Progress
        {
            get => _progress;
            set
            {
                _progress = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ProgressText));
            }
        }

        public string ProgressText => IsProcessing ? $"{Progress:F0}%" : "";

        public string ErrorMessage
        {
            get => _errorMessage;
            set
            {
                _errorMessage = value;
                OnPropertyChanged();
            }
        }

        public OptimizationItemStatus Status
        {
            get => _status;
            set
            {
                _status = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public enum OptimizationItemStatus
    {
        Unknown,
        Pending,
        Applied,
        AlreadyApplied,
        Incompatible,
        Conflict,
        RequiresAdmin,
        Failed,
        Rollback
    }

    public class OptimizationCategoryGroup
    {
        public VoltrisOptimizer.Core.SystemIntelligenceProfiler.Models.OptimizationCategory Category { get; set; }
        public System.Collections.Generic.List<VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization> Optimizations { get; set; } = new();
    }
}
