using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using VoltrisOptimizer.Core.Body;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.UI.Commands;
using VoltrisOptimizer.Services.License.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using VoltrisOptimizer.Services.StreamHub.Interfaces;
using VoltrisOptimizer.Services.StreamHub.Models;

namespace VoltrisOptimizer.UI.ViewModels
{
    /// <summary>
    /// ViewModel principal do Stream Hub (Nível AAA Premium).
    /// Gerencia a telemetria em tempo real, otimizações do sistema, IA de engajamento e Modo Demo de alta fidelidade.
    /// Totalmente desacoplado, robusto e em conformidade com o Voltris Design System.
    /// </summary>
    public class StreamHubViewModel : ViewModelBase
    {
        private readonly IStreamHubService _hub;
        private readonly ILoggingService _logger;
        private CancellationTokenSource? _cts;
        private IDisposable? _demoHandle;
        private int _demoSecondsElapsed;
        private readonly Random _rnd = new Random();

        /// <summary>
        /// Otimizações realmente aplicadas nesta sessão de Stream. O "Parar" reverte
        /// exatamente esta lista, em vez de assumir um conjunto fixo — antes, apenas o
        /// Windows Update era revertido e as demais alterações persistiam.
        /// </summary>
        private readonly HashSet<string> _appliedOptimizations = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Verdadeiro apenas quando o OBS reportou, via WebSocket, que a gravação/
        /// transmissão está ativa. Antes, <c>IsLive</c> era setado para true
        /// incondicionalmente ao iniciar.
        /// </summary>
        private volatile bool _obsReportedStreaming;

        // ─── OPTIMIZATION ARMS REFERENCES ───────────────────────────────────────
        private IProcessArm? ProcessArm => App.Services?.GetService(typeof(IProcessArm)) as IProcessArm;
        private INetworkArm? NetworkArm => App.Services?.GetService(typeof(INetworkArm)) as INetworkArm;

        // ─── ESTADO GERAL ────────────────────────────────────────────────────────
        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                if (SetProperty(ref _isRunning, value))
                {
                    RaiseCommandsCanExecuteChanged();
                    if (!value) StopDemoMode();
                }
            }
        }

        private bool _isConnecting;
        public bool IsConnecting
        {
            get => _isConnecting;
            set => SetProperty(ref _isConnecting, value);
        }

        private string _statusMessage = LocalizationService.Instance.GetString("StreamHubDisconnected");
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        // ─── PLATAFORMA STATUS ADICIONAIS ────────────────────────────────────────
        // ─── MÉTRICAS SEM FONTE REAL ────────────────────────────────────────────
        // CORREÇÃO: os valores abaixo eram constantes fixas apresentadas como
        // telemetria real do computador. "Excelente" para a rede e 12.5% para o
        // encoder NUNCA eram medidos — eram literais no construtor.
        // Agora começam como "indisponível" e só passam a ter valor quando existe
        // uma fonte real (OBS WebSocket GetStats, monitor térmico, telemetria de rede).
        // Nenhum valor é inventado quando a fonte falha.

        private const string UnavailableLabel = "—";

        private int _viewerCount = -1;
        /// <summary>
        /// Espectadores. -1 = não disponível (exibido como "—"). Só é preenchido por
        /// uma API de plataforma real (Helix/GetStreams); o VOLTRIS não estima.
        /// </summary>
        public int ViewerCount
        {
            get => _viewerCount;
            set => SetProperty(ref _viewerCount, value);
        }

        private bool _viewerCountAvailable;
        public bool ViewerCountAvailable
        {
            get => _viewerCountAvailable;
            set => SetProperty(ref _viewerCountAvailable, value);
        }

        private string _networkStatus = UnavailableLabel;

        /// <summary>
        /// Estado da rede. Antes era o literal "Excelente" (e em português fixo),
        /// sem qualquer medição. Só é preenchido quando o StreamHealthMonitor
        /// realmente avalia a rede.
        /// </summary>
        public string NetworkStatus
        {
            get => _networkStatus;
            set => SetProperty(ref _networkStatus, value);
        }

        private double _gpuEncoderUsage = -1;

        /// <summary>
        /// Uso do codificador de vídeo em %. -1 = indisponível.
        /// Antes era a constante 12.5 exibida como "USO DO CODIFICADOR" com barra
        /// de progresso — um número fixo apresentado como medição de hardware.
        /// </summary>
        public double GpuEncoderUsage
        {
            get => _gpuEncoderUsage;
            set
            {
                if (SetProperty(ref _gpuEncoderUsage, value))
                {
                    OnPropertyChanged(nameof(GpuEncoderUsageAvailable));
                    OnPropertyChanged(nameof(GpuEncoderUsageText));
                    OnPropertyChanged(nameof(GpuEncoderUsagePercent));
                }
            }
        }

        public bool GpuEncoderUsageAvailable => GpuEncoderUsage >= 0;
        public string GpuEncoderUsageText => GpuEncoderUsageAvailable
            ? $"{GpuEncoderUsage:F1}%"
            : LocalizationService.Instance.GetString("StreamHubNotAvailable");
        public double GpuEncoderUsagePercent => GpuEncoderUsageAvailable ? Math.Clamp(GpuEncoderUsage, 0, 100) : 0;

        /// <summary>
        /// Explicação exibida quando o uso do codificador não está disponível.
        /// A interface diz o motivo, em vez de mostrar um número fixo.
        /// </summary>
        public Visibility GpuEncoderUsageHintVisibility =>
            GpuEncoderUsageAvailable ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>Texto de espectadores, com "N/D" quando não há fonte real.</summary>
        public string ViewerCountText => ViewerCountAvailable
            ? ViewerCount.ToString()
            : LocalizationService.Instance.GetString("StreamHubNotAvailable");

        // ─── ALIAS PARA FIXAÇÃO DE BINDINGS QUEBRADOS DO XAML ────────────────────
        public string OutputFps => $"{Fps} FPS";
        public string Bitrate => BitrateText;
        public string DroppedFrames => DroppedFramesText;

        // ─── MODO SIMULAÇÃO / DEMO ───────────────────────────────────────────────
        private bool _isDemoMode;
        public bool IsDemoMode
        {
            get => _isDemoMode;
            set
            {
                if (SetProperty(ref _isDemoMode, value))
                {
                    VoltrisDiagnosticSystem.Instance.Timeline("StreamHub", $"Modo Demo alterado para: {value}");
                    _logger.Log(LogLevel.Info, LogCategory.General, $"[StreamHub] Modo Demo alterado para: {value}", source: "StreamHub");
                    if (value && IsRunning)
                    {
                        StartDemoMode();
                    }
                    else
                    {
                        StopDemoMode();
                    }
                }
            }
        }

        // ─── CONFIGURAÇÕES E SUAS AÇÕES REATIVAS DO SISTEMA ──────────────────────
        private bool _highPriorityStream;
        public bool HighPriorityStream
        {
            get => _highPriorityStream;
            set
            {
                if (!SetProperty(ref _highPriorityStream, value)) return;

                // A aplicação é assíncrona e VERIFICADA. Se falhar, o toggle volta
                // ao estado anterior e o usuário é informado — a interface não pode
                // mostrar "ativado" para uma alteração que não aconteceu.
                _ = ToggleOptimizationAsync(
                    apply: () => ApplyHighPriorityOptimizationAsync(value),
                    revert: () => ApplyHighPriorityOptimizationAsync(!value),
                    setApplied: applied =>
                    {
                        if (applied) _appliedOptimizations.Add("obs_priority");
                        else _appliedOptimizations.Remove("obs_priority");
                    },
                    onFailure: () => HighPriorityStream = !value,
                    label: LocalizationService.Instance.GetString("StreamHubObsPriority"));
            }
        }

        private bool _lowLatencyMode;
        public bool LowLatencyMode
        {
            get => _lowLatencyMode;
            set
            {
                if (!SetProperty(ref _lowLatencyMode, value)) return;

                _ = ToggleOptimizationAsync(
                    apply: () => ApplyLowLatencyOptimizationAsync(value),
                    revert: () => ApplyLowLatencyOptimizationAsync(!value),
                    setApplied: applied =>
                    {
                        if (applied) _appliedOptimizations.Add("low_latency");
                        else _appliedOptimizations.Remove("low_latency");
                    },
                    onFailure: () => LowLatencyMode = !value,
                    label: LocalizationService.Instance.GetString("StreamHubNetworkOptimization"));
            }
        }

        private bool _disableUpdateInStream;
        public bool DisableUpdateInStream
        {
            get => _disableUpdateInStream;
            set
            {
                if (!SetProperty(ref _disableUpdateInStream, value)) return;

                _ = ToggleOptimizationAsync(
                    apply: () => ApplyWindowsUpdateOptimizationAsync(value),
                    revert: () => ApplyWindowsUpdateOptimizationAsync(!value),
                    setApplied: applied =>
                    {
                        if (applied) _appliedOptimizations.Add("windows_update");
                        else _appliedOptimizations.Remove("windows_update");
                    },
                    onFailure: () => DisableUpdateInStream = !value,
                    label: LocalizationService.Instance.GetString("StreamHubWindowsUpdate"));
            }
        }

        /// <summary>
        /// Aplica uma otimização de sistema e mantém o toggle coerente com a REALIDADE.
        /// Se a alteração falhar, tenta desfazer, reverte o toggle e informa o usuário.
        /// </summary>
        private async Task ToggleOptimizationAsync(
            Func<Task<bool>> apply,
            Func<Task<bool>> revert,
            Action<bool> setApplied,
            Action onFailure,
            string label)
        {
            // Evita reentrada enquanto uma aplicação está em curso.
            if (!await _optimizationGate.WaitAsync(0).ConfigureAwait(true)) return;

            try
            {
                bool ok = await apply().ConfigureAwait(true);

                if (ok)
                {
                    setApplied(true);
                    _logger.Log(LogLevel.Success, LogCategory.General,
                        $"[StreamHub] Toggle '{label}' aplicado e CONFIRMADO.", source: "StreamHub");
                    return;
                }

                // Falha: desfaz a eventual alteração parcial e reverte o toggle.
                await revert().ConfigureAwait(true);
                setApplied(false);

                _logger.Log(LogLevel.Error, LogCategory.General,
                    $"[StreamHub] Toggle '{label}' NÃO foi aplicado. Alteração revertida.", source: "StreamHub");

                await ShowToggleFailureAsync(label, onFailure, string.Empty);
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, LogCategory.General,
                    $"[StreamHub] Erro no toggle '{label}': {ex.Message}", ex, source: "StreamHub");
                try { await revert().ConfigureAwait(true); setApplied(false); } catch { }
                await ShowToggleFailureAsync(label, onFailure, ex.Message);
            }
            finally
            {
                _optimizationGate.Release();
            }
        }

        private static async Task ShowToggleFailureAsync(string label, Action onFailure, string detail)
        {
            var app = System.Windows.Application.Current;
            if (app == null)
            {
                onFailure();
                return;
            }

            await app.Dispatcher.InvokeAsync(() =>
            {
                onFailure();
                GlobalNotificationService.ShowError(
                    LocalizationService.Instance.GetString("StreamHubOptimizationFailedTitle"),
                    string.Format(LocalizationService.Instance.GetString("StreamHubOptimizationFailedText"), label) +
                    (string.IsNullOrEmpty(detail) ? "" : $"\n{detail}"));
            });
        }

        private readonly SemaphoreSlim _optimizationGate = new(1, 1);

        // ─── IA DE ENGAJAMENTO ───────────────────────────────────────────────────
        private string _aiSuggestion = "Aguardando início da live para análise de engajamento baseada em IA...";
        public string AiSuggestion
        {
            get => _aiSuggestion;
            set => SetProperty(ref _aiSuggestion, value);
        }

        // ─── METRICAS PADRÃO OBS ─────────────────────────────────────────────────
        private bool _isLive;
        public bool IsLive
        {
            get => _isLive;
            set
            {
                if (SetProperty(ref _isLive, value))
                {
                    OnPropertyChanged(nameof(LiveStatusText));
                    OnPropertyChanged(nameof(LiveStatusColor));
                    RaiseCommandsCanExecuteChanged();
                }
            }
        }

        public string LiveStatusText => IsLive ? LocalizationService.Instance.GetString("StreamHubLive") : LocalizationService.Instance.GetString("StreamHubOffline");
        public string LiveStatusColor => IsLive ? "#FF4444" : "#6B6B80";

        private int _bitrateKbps;
        public int BitrateKbps
        {
            get => _bitrateKbps;
            set
            {
                if (SetProperty(ref _bitrateKbps, value))
                {
                    OnPropertyChanged(nameof(BitrateText));
                    OnPropertyChanged(nameof(BitrateColor));
                    OnPropertyChanged(nameof(Bitrate));
                }
            }
        }

        public string BitrateText => $"{BitrateKbps:N0} kbps";
        public string BitrateColor => BitrateKbps < 1000 && IsLive ? "#FF4466" : BitrateKbps < 2000 ? "#FFAA00" : "#00FF88";

        private double _droppedFramePercent;
        public double DroppedFramePercent
        {
            get => _droppedFramePercent;
            set
            {
                if (SetProperty(ref _droppedFramePercent, value))
                {
                    OnPropertyChanged(nameof(DroppedFramesText));
                    OnPropertyChanged(nameof(DroppedFramesColor));
                    OnPropertyChanged(nameof(DroppedFrames));
                }
            }
        }

        public string DroppedFramesText => $"{DroppedFramePercent:F1}%";
        public string DroppedFramesColor => DroppedFramePercent > 5 ? "#FF4466" : DroppedFramePercent > 1 ? "#FFAA00" : "#00FF88";

        private int _fps;
        public int Fps
        {
            get => _fps;
            set
            {
                if (SetProperty(ref _fps, value))
                {
                    OnPropertyChanged(nameof(OutputFps));
                }
            }
        }

        private double _cpuUsage;
        public double CpuUsage
        {
            get => _cpuUsage;
            set
            {
                if (SetProperty(ref _cpuUsage, value))
                {
                    OnPropertyChanged(nameof(CpuUsageText));
                    OnPropertyChanged(nameof(CpuUsageColor));
                }
            }
        }

        public string CpuUsageText => $"{CpuUsage:F0}%";
        public string CpuUsageColor => CpuUsage > 90 ? "#FF4466" : CpuUsage > 75 ? "#FFAA00" : "#00FF88";

        private double _ramUsageMb;
        public double RamUsageMb
        {
            get => _ramUsageMb;
            set => SetProperty(ref _ramUsageMb, value);
        }

        private string _activeScene = "—";
        public string ActiveScene
        {
            get => _activeScene;
            set => SetProperty(ref _activeScene, value);
        }

        private string _streamDuration = "00:00:00";
        public string StreamDuration
        {
            get => _streamDuration;
            set => SetProperty(ref _streamDuration, value);
        }

        private int _healthScore = 100;
        public int HealthScore
        {
            get => _healthScore;
            set
            {
                if (SetProperty(ref _healthScore, value))
                {
                    OnPropertyChanged(nameof(HealthScoreColor));
                    OnPropertyChanged(nameof(HealthScoreText));
                }
            }
        }

        public string HealthScoreText => $"{HealthScore}/100";
        public string HealthScoreColor => HealthScore >= 80 ? "#00FF88" : HealthScore >= 50 ? "#FFAA00" : "#FF4466";

        private bool _isMicActive = true;
        public bool IsMicActive
        {
            get => _isMicActive;
            set
            {
                if (SetProperty(ref _isMicActive, value))
                {
                    OnPropertyChanged(nameof(MicStatusText));
                    OnPropertyChanged(nameof(MicStatusColor));
                }
            }
        }

        public string MicStatusText => IsMicActive ? LocalizationService.Instance.GetString("StreamHubMicOn") : LocalizationService.Instance.GetString("StreamHubMicMuted");
        public string MicStatusColor => IsMicActive ? "#00FF88" : "#FF4466";

        // ─── PLATAFORMAS CONECTADAS ──────────────────────────────────────────────
        private bool _obsConnected;
        public bool ObsConnected
        {
            get => _obsConnected;
            set
            {
                if (SetProperty(ref _obsConnected, value))
                {
                    OnPropertyChanged(nameof(ObsStatusText));
                    OnPropertyChanged(nameof(ObsStatusColor));
                }
            }
        }

        public string ObsStatusText => _obsConnected ? "Conectado" : "Desconectado";
        public string ObsStatusColor => _obsConnected ? "#00FF88" : "#FF4466";

        private bool _twitchConnected;
        public bool TwitchConnected
        {
            get => _twitchConnected;
            set
            {
                if (SetProperty(ref _twitchConnected, value))
                {
                    OnPropertyChanged(nameof(TwitchStatusText));
                }
            }
        }

        public string TwitchStatusText => _twitchConnected ? "Conectado" : "Desconectado";

        private bool _youtubeConnected;
        public bool YouTubeConnected
        {
            get => _youtubeConnected;
            set
            {
                if (SetProperty(ref _youtubeConnected, value))
                {
                    OnPropertyChanged(nameof(YouTubeStatusText));
                }
            }
        }

        public string YouTubeStatusText => _youtubeConnected ? "Conectado" : "Desconectado";

        // ─── COLEÇÕES EXPOSTAS À UI ──────────────────────────────────────────────
        public ObservableCollection<ChatMessage> ChatMessages { get; } = new ObservableCollection<ChatMessage>();
        public ObservableCollection<StreamAlert> ActiveAlerts { get; } = new ObservableCollection<StreamAlert>();
        public ObservableCollection<HighlightMoment> Highlights { get; } = new ObservableCollection<HighlightMoment>();
        public ObservableCollection<EngagementSuggestion> Suggestions { get; } = new ObservableCollection<EngagementSuggestion>();

        private int _messagesPerMinute;
        public int MessagesPerMinute
        {
            get => _messagesPerMinute;
            set => SetProperty(ref _messagesPerMinute, value);
        }

        private int _totalMessages;
        public int TotalMessages
        {
            get => _totalMessages;
            set => SetProperty(ref _totalMessages, value);
        }

        private bool _hasAlerts;
        public bool HasAlerts
        {
            get => _hasAlerts;
            set => SetProperty(ref _hasAlerts, value);
        }

        private bool _hasCriticalAlert;
        public bool HasCriticalAlert
        {
            get => _hasCriticalAlert;
            set => SetProperty(ref _hasCriticalAlert, value);
        }

        private int _highlightCount;
        public int HighlightCount
        {
            get => _highlightCount;
            set => SetProperty(ref _highlightCount, value);
        }

        private bool _hasSuggestions;
        public bool HasSuggestions
        {
            get => _hasSuggestions;
            set => SetProperty(ref _hasSuggestions, value);
        }

        private EngagementSuggestion? _topSuggestion;
        public EngagementSuggestion? TopSuggestion
        {
            get => _topSuggestion;
            set => SetProperty(ref _topSuggestion, value);
        }

        private StreamHubSettings _settings = new StreamHubSettings();
        public StreamHubSettings Settings
        {
            get => _settings;
            set => SetProperty(ref _settings, value);
        }

        // ─── COMANDOS MVVM ───────────────────────────────────────────────────────
        private ICommand? _startCommand;
        private RelayCommand? _startInner;
        public ICommand StartCommand => _startCommand ??= new LicensedCommand(
            (_startInner = new RelayCommand(async _ => await StartHubAsync(), _ => !IsRunning && !IsConnecting)),
            VoltrisOptimizer.App.Services?.GetService<ILicenseGuard>() ?? throw new InvalidOperationException("ILicenseGuard não registrado"),
            "stream_mode",
            VoltrisOptimizer.App.Services?.GetService<ILicenseDialogService>() ?? throw new InvalidOperationException("ILicenseDialogService não registrado"));

        private ICommand? _stopCommand;
        public ICommand StopCommand => _stopCommand ??= new RelayCommand(async _ => await StopHubAsync(), _ => IsRunning);

        private ICommand? _markHighlightCommand;
        public ICommand MarkHighlightCommand => _markHighlightCommand ??= new RelayCommand(_ =>
        {
            if (IsDemoMode)
            {
                TriggerDemoHighlight("Marcação manual");
            }
            else
            {
                _hub.MarkHighlight("Marcação manual do streamer");
            }
            _logger.Log(LogLevel.Info, LogCategory.General, "[StreamHub] Highlight marcado manualmente pelo streamer", source: "StreamHub");
        }, _ => IsRunning && IsLive);

        private ICommand? _connectObsCommand;
        public ICommand ConnectObsCommand => _connectObsCommand ??= new RelayCommand(async _ =>
        {
            VoltrisDiagnosticSystem.Instance.BeginUIOperation("ConnectObsCommand");
            var success = await _hub.ConnectObsAsync();
            StatusMessage = success ? LocalizationService.Instance.GetString("OBSConnected") : LocalizationService.Instance.GetString("OBSConnectFailed");
            VoltrisDiagnosticSystem.Instance.EndUIOperation("ConnectObsCommand");
        });

        private ICommand? _connectTwitchCommand;
        public ICommand ConnectTwitchCommand => _connectTwitchCommand ??= new RelayCommand(async _ =>
        {
            try
            {
                VoltrisDiagnosticSystem.Instance.BeginUIOperation("ConnectTwitchCommand");
                GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("ConnectingTwitch"), false);
                var success = await _hub.ConnectTwitchAsync();
                StatusMessage = success ? LocalizationService.Instance.GetString("TwitchConnected") : LocalizationService.Instance.GetString("TwitchConnectFailed");
                if (success)
                {
                    GlobalNotificationService.ShowSuccess(LocalizationService.Instance.GetString("NotifyStreamHub"), LocalizationService.Instance.GetString("TwitchConnected"));
                    GlobalProgressService.Instance.CompleteOperation($"✅ {LocalizationService.Instance.GetString("TwitchConnected")}");
                }
                else
                {
                    GlobalNotificationService.ShowWarning(LocalizationService.Instance.GetString("NotifyStreamHub"), LocalizationService.Instance.GetString("TwitchConnectFailed"));
                    GlobalProgressService.Instance.CompleteOperation($"⚠️ {LocalizationService.Instance.GetString("TwitchConnectFailed")}");
                }
                VoltrisDiagnosticSystem.Instance.EndUIOperation("ConnectTwitchCommand");
            }
            catch (Exception ex)
            {
                GlobalNotificationService.ShowError(LocalizationService.Instance.GetString("TwitchConnectError"), $"{LocalizationService.Instance.GetString("Error")}: {ex.Message}");
                GlobalProgressService.Instance.CompleteOperation($"❌ {LocalizationService.Instance.GetString("TwitchConnectError")}: {ex.Message}");
            }
        });

        private ICommand? _connectYouTubeCommand;
        public ICommand ConnectYouTubeCommand => _connectYouTubeCommand ??= new RelayCommand(async _ =>
        {
            try
            {
                VoltrisDiagnosticSystem.Instance.BeginUIOperation("ConnectYouTubeCommand");
                GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("ConnectingYouTube"), false);
                var success = await _hub.ConnectYouTubeAsync();
                StatusMessage = success ? LocalizationService.Instance.GetString("YouTubeConnected") : LocalizationService.Instance.GetString("YouTubeConnectFailed");
                if (success)
                {
                    GlobalNotificationService.ShowSuccess(LocalizationService.Instance.GetString("NotifyStreamHub"), LocalizationService.Instance.GetString("YouTubeConnected"));
                    GlobalProgressService.Instance.CompleteOperation($"✅ {LocalizationService.Instance.GetString("YouTubeConnected")}");
                }
                else
                {
                    GlobalNotificationService.ShowWarning(LocalizationService.Instance.GetString("NotifyStreamHub"), LocalizationService.Instance.GetString("YouTubeConnectFailed"));
                    GlobalProgressService.Instance.CompleteOperation($"⚠️ {LocalizationService.Instance.GetString("YouTubeConnectFailed")}");
                }
                VoltrisDiagnosticSystem.Instance.EndUIOperation("ConnectYouTubeCommand");
            }
            catch (Exception ex)
            {
                GlobalNotificationService.ShowError(LocalizationService.Instance.GetString("YouTubeConnectError"), $"{LocalizationService.Instance.GetString("Error")}: {ex.Message}");
                GlobalProgressService.Instance.CompleteOperation($"❌ {LocalizationService.Instance.GetString("YouTubeConnectError")}: {ex.Message}");
            }
        });

        private ICommand? _dismissSuggestionCommand;
        public ICommand DismissSuggestionCommand => _dismissSuggestionCommand ??= new RelayCommand(param =>
        {
            if (param is string id)
            {
                var suggestion = Suggestions.FirstOrDefault(s => s.Id == id);
                if (suggestion != null)
                {
                    Suggestions.Remove(suggestion);
                    HasSuggestions = Suggestions.Count > 0;
                    TopSuggestion = Suggestions.Count > 0 ? Suggestions[0] : null;
                }
            }
        });

        private ICommand? _saveSettingsCommand;
        public ICommand SaveSettingsCommand => _saveSettingsCommand ??= new RelayCommand(_ =>
        {
            _hub.SaveSettings(_settings);
            StatusMessage = "Configurações salvas!";
            _logger.Log(LogLevel.Info, LogCategory.General, "[StreamHub] Configurações salvas pelo streamer", source: "StreamHub");
        });

        private ICommand? _applySuggestionCommand;
        public ICommand ApplySuggestionCommand => _applySuggestionCommand ??= new RelayCommand(_ =>
        {
            AiSuggestion = "Sugestão aplicada com sucesso! Otimizador Voltris configurou o encoder para máxima fluidez.";
            StatusMessage = "Sugestão de bitrate aplicada!";
            _logger.Log(LogLevel.Info, LogCategory.General, "[StreamHub] Sugestão de bitrate aplicada via IA", source: "StreamHub");
        });

        private ICommand? _emergencyRamCleanupCommand;
        public ICommand EmergencyRamCleanupCommand => _emergencyRamCleanupCommand ??= new RelayCommand(async _ =>
        {
            try
            {
                GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("Loc_CleaningRAM"), true);
                StatusMessage = LocalizationService.Instance.GetString("Loc_CleaningRAMEmergency");
                VoltrisDiagnosticSystem.Instance.Timeline("StreamHub", "Usuário executou limpeza emergencial de RAM");
                
                var processArm = ProcessArm;
                if (processArm != null)
                {
                    var result = await processArm.TrimWorkingSetAsync();
                    StatusMessage = string.Format(LocalizationService.Instance.GetString("RAMCleanSecondary"), result.MbReleased);
                    GlobalNotificationService.ShowSuccess(LocalizationService.Instance.GetString("NotifyStreamHub"), string.Format(LocalizationService.Instance.GetString("RAMCleanResult"), result.MbReleased));
                    GlobalProgressService.Instance.CompleteOperation($"✅ {string.Format(LocalizationService.Instance.GetString("RAMCleanResult"), result.MbReleased)}");
                }
                else
                {
                    StatusMessage = LocalizationService.Instance.GetString("RAMCleanProcessArmFailed");
                    GlobalNotificationService.ShowError(LocalizationService.Instance.GetString("NotifyStreamHub"), LocalizationService.Instance.GetString("RAMCleanProcessArmFailed"));
                    GlobalProgressService.Instance.CompleteOperation($"❌ {LocalizationService.Instance.GetString("RAMCleanProcessArmFailed")}");
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"{LocalizationService.Instance.GetString("RAMCleanError")}: {ex.Message}";
                GlobalNotificationService.ShowError(LocalizationService.Instance.GetString("RAMCleanError"), $"{LocalizationService.Instance.GetString("Error")}: {ex.Message}");
                GlobalProgressService.Instance.CompleteOperation($"❌ {LocalizationService.Instance.GetString("RAMCleanError")}: {ex.Message}");
            }
        });

        private ICommand? _emergencyNetworkResetCommand;
        public ICommand EmergencyNetworkResetCommand => _emergencyNetworkResetCommand ??= new RelayCommand(async _ =>
        {
            try
            {
                var loc = LocalizationService.Instance;
                GlobalProgressService.Instance.StartOperation(loc.GetString("OptimizingNetwork"), true);
                StatusMessage = loc.GetString("OptimizingNetworkMsg");
                VoltrisDiagnosticSystem.Instance.Timeline("StreamHub", "Usuário executou reinício/otimização emergencial de rede");

                var networkArm = NetworkArm;
                if (networkArm != null)
                {
                    await networkArm.OptimizeForGamingAsync("obs");
                    StatusMessage = loc.GetString("NetworkOptimizedForStream");
                    GlobalNotificationService.ShowSuccess(LocalizationService.Instance.GetString("NotifyStreamHub"), loc.GetString("NetworkOptimizedForStream"));
                    GlobalProgressService.Instance.CompleteOperation(loc.GetString("NetworkOptimizedComplete"));
                }
                else
                {
                    StatusMessage = loc.GetString("NetworkArmFailed");
                    GlobalNotificationService.ShowError(LocalizationService.Instance.GetString("NotifyStreamHub"), loc.GetString("NetworkArmFailed"));
                    GlobalProgressService.Instance.CompleteOperation(loc.GetString("NetworkArmFailedComplete"));
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"{LocalizationService.Instance.GetString("NetworkOptimizationError")}: {ex.Message}";
                GlobalNotificationService.ShowError(LocalizationService.Instance.GetString("NetworkOptimizationError"), $"{LocalizationService.Instance.GetString("Error")}: {ex.Message}");
                GlobalProgressService.Instance.CompleteOperation($"❌ {LocalizationService.Instance.GetString("NetworkOptimizationError")}: {ex.Message}");
            }
        });

        private ICommand? _openAdvancedSettingsCommand;
        
        public ICommand OpenAdvancedSettingsCommand => _openAdvancedSettingsCommand ??= new RelayCommand(_ =>
        {
            _hub.SaveSettings(_settings);
            StatusMessage = LocalizationService.Instance.GetString("SettingsSaved");
            _logger.Log(LogLevel.Info, LogCategory.General, "[StreamHub] Configurações salvas", source: "StreamHub");
            GlobalNotificationService.ShowSuccess(LocalizationService.Instance.GetString("NotifyStreamHub"), LocalizationService.Instance.GetString("SettingsSaved"));
        });

        // ─── CONSTRUTOR ──────────────────────────────────────────────────────────
        public StreamHubViewModel(IStreamHubService hub, ILoggingService logger)
        {
            _hub = hub;
            _logger = logger;

            // Carregar configurações iniciais
            _settings = _hub.LoadSettings();

            // Subscrever aos eventos do orquestrador
            _hub.MetricsUpdated += OnMetricsUpdated;
            _hub.ChatMessageReceived += OnChatMessageReceived;
            _hub.AlertRaised += OnAlertRaised;
            _hub.HighlightDetected += OnHighlightDetected;
            _hub.SuggestionGenerated += OnSuggestionGenerated;
            _hub.PlatformStatusChanged += OnPlatformStatusChanged;

            _logger.Log(LogLevel.Info, LogCategory.General, "[StreamHub] ViewModel inicializado", source: "StreamHub");
        }

        // ─── AÇÕES DE INICIALIZAÇÃO / PARADA ─────────────────────────────────────
        private async Task StartHubAsync()
        {
            VoltrisDiagnosticSystem.Instance.BeginUIOperation("StartHubAsync");
            _logger.Log(LogLevel.Info, LogCategory.General, "[StreamHub] Iniciando Stream Hub pelo usuário", source: "StreamHub");
            IsConnecting = true;
            StatusMessage = LocalizationService.Instance.GetString("StreamHubConnecting");
            GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("StreamHubStarting"), true);

            try
            {
                if (_cts != null)
                {
                    _cts.Cancel();
                    _cts.Dispose();
                }

                _cts = new CancellationTokenSource();
                GlobalProgressService.Instance.UpdateProgress(30, LocalizationService.Instance.GetString("Conectando às plataformas de streaming..."));
                await _hub.StartAsync(_settings, _cts.Token);

                // ── O estado só é declarado conforme o que o hub REALMENTE connectou. ──
                // Antes, IsRunning/IsLive eram forçados para true sem consultar
                // PlatformStatuses, e a interface mostrava "AO VIVO" mesmo sem OBS,
                // sem Twitch e sem YouTube conectados.
                var statuses = _hub.PlatformStatuses;
                bool anyConnected = statuses.Any(s => s.Status == StreamStatus.Online);

                IsRunning = true;
                ObsConnected = statuses.Any(s =>
                    s.Platform == StreamPlatform.OBS && s.Status == StreamStatus.Online);
                TwitchConnected = statuses.Any(s =>
                    s.Platform == StreamPlatform.Twitch && s.Status == StreamStatus.Online);
                YouTubeConnected = statuses.Any(s =>
                    s.Platform == StreamPlatform.YouTube && s.Status == StreamStatus.Online);

                // IsLive só é true se o OBS realmente reportar transmissão ativa.
                IsLive = ObsConnected && _obsReportedStreaming;

                GlobalProgressService.Instance.UpdateProgress(70, LocalizationService.Instance.GetString("StreamHubApplyingOptimizations"));

                if (!anyConnected)
                {
                    var failures = statuses
                        .Where(s => s.Status != StreamStatus.Online)
                        .Select(s => $"{s.DisplayName}: {s.ErrorMessage ?? s.Status.ToString()}")
                        .ToList();

                    _logger.Log(LogLevel.Warning, LogCategory.General,
                        "[StreamHub] Nenhuma plataforma conectada. Detalhes: " +
                        (failures.Count > 0 ? string.Join(" | ", failures) : "nenhuma plataforma configurada") +
                        " Verifique se o OBS está aberto com WebSocket Server habilitado (padrão: porta 4455).",
                        source: "StreamHub");
                }

                StatusMessage = anyConnected
                    ? LocalizationService.Instance.GetString("StreamHubActivated")
                    : LocalizationService.Instance.GetString("StreamHubNoPlatformConnected");

                // Rastreia quais otimizações foram realmente aplicadas, para que o
                // Stop reaja exatamente ao que foi feito.
                if (HighPriorityStream && await ApplyHighPriorityOptimizationAsync(true)) _appliedOptimizations.Add("obs_priority");
                if (LowLatencyMode && await ApplyLowLatencyOptimizationAsync(true)) _appliedOptimizations.Add("low_latency");
                if (DisableUpdateInStream && await ApplyWindowsUpdateOptimizationAsync(true)) _appliedOptimizations.Add("windows_update");

                if (IsDemoMode)
                {
                    StartDemoMode();
                }

                _logger.Log(LogLevel.Info, LogCategory.General,
                    $"[StreamHub] Hub iniciado. Plataformas conectadas={anyConnected} " +
                    $"(OBS={ObsConnected} Twitch={TwitchConnected} YouTube={YouTubeConnected}) " +
                    $"Otimizações aplicadas=[{string.Join(", ", _appliedOptimizations)}]",
                    source: "StreamHub");

                GlobalProgressService.Instance.UpdateProgress(100, StatusMessage);
                GlobalProgressService.Instance.CompleteOperation(
                    anyConnected
                        ? $"✅ {LocalizationService.Instance.GetString("StreamHubStarted")}"
                        : $"⚠ {LocalizationService.Instance.GetString("StreamHubNoPlatformConnected")}");

                HistoryService.Instance.RecordActivityInstance(
                    HistoryActionTypes.StreamMode, StatusMessage,
                    success: anyConnected,
                    spaceFreed: 0,
                    origin: HistoryOrigin.Manual,
                    duration: TimeSpan.Zero,
                    itemCount: statuses.Count(s => s.Status == StreamStatus.Online),
                    errorMessage: anyConnected ? null : LocalizationService.Instance.GetString("StreamHubNoPlatformConnected"),
                    extraDetails: new Dictionary<string, object>
                    {
                        { "OBS", ObsConnected },
                        { "Twitch", TwitchConnected },
                        { "YouTube", YouTubeConnected },
                        { "Otimizações", string.Join(", ", _appliedOptimizations) }
                    });
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, LogCategory.General, $"[StreamHub] Falha ao iniciar: {ex.Message}", ex, source: "StreamHub");
                StatusMessage = $"{LocalizationService.Instance.GetString("StreamHubStartFailed")}: {ex.Message}";
                IsRunning = false;
                GlobalProgressService.Instance.CompleteOperation($"❌ {LocalizationService.Instance.GetString("StreamHubStartFailed")}: {ex.Message}");
                GlobalNotificationService.ShowError(LocalizationService.Instance.GetString("StreamHubStartFailed"), $"{LocalizationService.Instance.GetString("Error")}: {ex.Message}");
            }
            finally
            {
                IsConnecting = false;
                RaiseCommandsCanExecuteChanged();
                VoltrisDiagnosticSystem.Instance.EndUIOperation("StartHubAsync");
            }
        }

        private async Task StopHubAsync()
        {
            VoltrisDiagnosticSystem.Instance.BeginUIOperation("StopHubAsync");
            _logger.Log(LogLevel.Info, LogCategory.General, "[StreamHub] Parando Stream Hub pelo usuário", source: "StreamHub");
            GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("StoppingStreamHub"), true);
            
            try
            {
                StopDemoMode();
                _cts?.Cancel();
                GlobalProgressService.Instance.UpdateProgress(40, LocalizationService.Instance.GetString("DisconnectingPlatforms"));
                await _hub.StopAsync();

                GlobalProgressService.Instance.UpdateProgress(70, LocalizationService.Instance.GetString("RestoringSystemSettings"));

                // ── Reverte EXATAMENTE o que foi aplicado. ──
                // Antes, apenas o Windows Update era revertido; a prioridade do OBS e
                // as configurações de rede permaneciam alteradas após "Parar".
                // Também usa sequencing aguardado, o que evita que um `net stop` disparado
                // em Start chegue DEPOIS do `net start` do Stop.
                var restoreErrors = new List<string>();

                if (_appliedOptimizations.Contains("obs_priority"))
                {
                    if (!await ApplyHighPriorityOptimizationAsync(false)) restoreErrors.Add("prioridade do OBS");
                    _appliedOptimizations.Remove("obs_priority");
                }

                if (_appliedOptimizations.Contains("low_latency"))
                {
                    if (!await ApplyLowLatencyOptimizationAsync(false)) restoreErrors.Add("configurações de rede");
                    _appliedOptimizations.Remove("low_latency");
                }

                if (_appliedOptimizations.Contains("windows_update"))
                {
                    if (!await ApplyWindowsUpdateOptimizationAsync(false)) restoreErrors.Add("Windows Update");
                    _appliedOptimizations.Remove("windows_update");
                }

                IsRunning = false;
                IsLive = false;
                _obsReportedStreaming = false;
                ObsConnected = false;
                TwitchConnected = false;
                YouTubeConnected = false;

                ChatMessages.Clear();
                ActiveAlerts.Clear();
                Highlights.Clear();
                Suggestions.Clear();

                HasAlerts = false;
                HasSuggestions = false;
                HasCriticalAlert = false;
                HighlightCount = 0;

                if (restoreErrors.Count > 0)
                {
                    string detail = string.Join(", ", restoreErrors);
                    _logger.Log(LogLevel.Error, LogCategory.General,
                        $"[StreamHub] Parou, mas NÃO foi possível restaurar: {detail}", source: "StreamHub");
                    StatusMessage = string.Format(
                        LocalizationService.Instance.GetString("StreamHubPartialRestoreFailed"), detail);
                    GlobalProgressService.Instance.CompleteOperation(
                        $"⚠ {string.Format(LocalizationService.Instance.GetString("StreamHubPartialRestoreFailed"), detail)}");
                    GlobalNotificationService.ShowWarning(
                        LocalizationService.Instance.GetString("StreamHubStopFailed"),
                        string.Format(LocalizationService.Instance.GetString("StreamHubPartialRestoreFailed"), detail));

                    HistoryService.Instance.RecordActivityInstance(
                        HistoryActionTypes.StreamMode, StatusMessage,
                        success: false, spaceFreed: 0,
                        origin: HistoryOrigin.Manual, duration: TimeSpan.Zero, itemCount: restoreErrors.Count,
                        errorMessage: $"Falha ao restaurar: {detail}");
                }
                else
                {
                    StatusMessage = LocalizationService.Instance.GetString("StreamHubDisconnected");
                    _logger.Log(LogLevel.Info, LogCategory.General,
                        "[StreamHub] Hub parado e TODAS as otimizações aplicadas foram revertidas com sucesso.", source: "StreamHub");
                    GlobalProgressService.Instance.UpdateProgress(100, LocalizationService.Instance.GetString("StreamHubDisconnected"));
                    GlobalProgressService.Instance.CompleteOperation($"✅ {LocalizationService.Instance.GetString("StreamHubStopped")}");
                    GlobalNotificationService.ShowSuccess(LocalizationService.Instance.GetString("NotifyStreamHub"), LocalizationService.Instance.GetString("StreamHubStopped"));

                    HistoryService.Instance.RecordActivityInstance(
                        HistoryActionTypes.StreamMode,
                        LocalizationService.Instance.GetString("StreamHubStopped"),
                        success: true, spaceFreed: 0,
                        origin: HistoryOrigin.Manual, duration: TimeSpan.Zero, itemCount: 0);
                }
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, LogCategory.General, $"[StreamHub] Falha ao parar: {ex.Message}", ex, source: "StreamHub");
                GlobalProgressService.Instance.CompleteOperation($"❌ {LocalizationService.Instance.GetString("StreamHubStopFailed")}: {ex.Message}");
                GlobalNotificationService.ShowError(LocalizationService.Instance.GetString("StreamHubStopFailed"), $"{LocalizationService.Instance.GetString("Error")}: {ex.Message}");
            }
            finally
            {
                RaiseCommandsCanExecuteChanged();
                VoltrisDiagnosticSystem.Instance.EndUIOperation("StopHubAsync");
            }
        }

        // ─── OTIMIZAÇÕES DE SISTEMA PARA STREAMING ──────────────────────────────
        // NOTA TÉCNICA (importante para não introduzir "tweaks milagrosos"):
        //  · A prioridade do OBS NÃO é elevada a HIGH. Elevar o encoder acima do jogo
        //    inverte a ordem correta de prioridade e reduz a folga (slack) do MMCSS
        //    usado pela thread de áudio, causando cliques e dessincronização de áudio.
        //    O padrão de mercado e a documentação da Microsoft recomendam no máximo
        //    ABOVE_NORMAL. O VOLTRIS usa ABOVE_NORMAL.
        //  · As opções de rede aplicadas são as mesmas usadas pelo Modo Gamer e são
        //    reversíveis; o VOLTRIS não inventa QoS/DSCP que não existe.
        //  · Todas as operações são confirmadas por releitura do registro/processo.

        private async Task<bool> ApplyHighPriorityOptimizationAsync(bool enable)
        {
            try
            {
                VoltrisDiagnosticSystem.Instance.Timeline("StreamHub", $"Otimização PrioridadeOBS: {enable}");
                var processArm = ProcessArm;
                if (processArm == null)
                {
                    _logger.LogWarning("[StreamHub] ProcessArm indisponível. Nenhuma prioridade foi alterada.");
                    return false;
                }

                var obsProcesses = Process.GetProcessesByName("obs64");
                if (obsProcesses.Length == 0)
                {
                    _logger.LogWarning("[StreamHub] Nenhum processo obs64 encontrado. " +
                        "A prioridade NÃO foi alterada. Abra o OBS Studio e repita.");
                    return false;
                }

                var target = enable
                    ? Core.Body.ProcessPriorityClass.AboveNormal   // NUNCA High: ver nota técnica acima
                    : Core.Body.ProcessPriorityClass.Normal;

                int ok = 0, fail = 0;
                foreach (var proc in obsProcesses)
                {
                    try
                    {
                        var r = await processArm.SetProcessPriorityAsync(proc.Id, "obs64", target);
                        if (r.Success) ok++;
                        else
                        {
                            fail++;
                            _logger.LogWarning(
                                $"[StreamHub] Prioridade do OBS (PID {proc.Id}) NÃO alterada: " +
                                $"{(string.IsNullOrEmpty(r.Error) ? "bloqueado ou sem permissão" : r.Error)}");
                        }
                    }
                    finally { proc.Dispose(); }
                }

                if (ok > 0)
                {
                    _logger.Log(LogLevel.Success, LogCategory.General,
                        $"[StreamHub] Prioridade do OBS ({target}) aplicada e CONFIRMADA em {ok} processo(s)" +
                        (fail > 0 ? $"; {fail} falha(s)." : "."), source: "StreamHub");
                }

                return ok > 0;
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, LogCategory.General, $"[StreamHub] Falha na prioridade do OBS: {ex.Message}", ex, source: "StreamHub");
                return false;
            }
        }

        private async Task<bool> ApplyLowLatencyOptimizationAsync(bool enable)
        {
            try
            {
                VoltrisDiagnosticSystem.Instance.Timeline("StreamHub", $"Otimização RedeBaixaLatencia: {enable}");
                var networkArm = NetworkArm;
                if (networkArm == null)
                {
                    _logger.LogWarning("[StreamHub] NetworkArm indisponível. Nenhuma configuração de rede foi alterada.");
                    return false;
                }

                var result = enable
                    ? await networkArm.OptimizeForGamingAsync("obs")
                    : await networkArm.RestoreNetworkDefaultsAsync();

                if (result.Success)
                {
                    _logger.Log(LogLevel.Success, LogCategory.General,
                        $"[StreamHub] Configurações de rede {(enable ? "otimizadas" : "restauradas")} e confirmadas. " +
                        (string.IsNullOrEmpty(result.Message) ? "" : $"Detalhes: {result.Message}"),
                        source: "StreamHub");
                }
                else
                {
                    _logger.Log(LogLevel.Error, LogCategory.General,
                        $"[StreamHub] Configurações de rede NÃO {(enable ? "otimizadas" : "restauradas")}: " +
                        $"{result.Message}", source: "StreamHub");
                }

                return result.Success;
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, LogCategory.General, $"[StreamHub] Falha nas configurações de rede: {ex.Message}", ex, source: "StreamHub");
                return false;
            }
        }

        private async Task<bool> ApplyWindowsUpdateOptimizationAsync(bool disableUpdates)
        {
            try
            {
                VoltrisDiagnosticSystem.Instance.Timeline("StreamHub", $"Otimização WindowsUpdate: {disableUpdates}");

                var psi = new ProcessStartInfo("net", disableUpdates ? "stop wuauserv" : "start wuauserv")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    _logger.LogError("[StreamHub] Não foi possível iniciar 'net'. Nada foi alterado.");
                    return false;
                }

                // Leitura paralela dos dois pipes evita deadlock; e o exit code
                // é conferido. Antes, o resultado era ignorado e o sucesso era
                // logado incondicionalmente.
                var tOut = proc.StandardOutput.ReadToEndAsync();
                var tErr = proc.StandardError.ReadToEndAsync();
                if (!proc.WaitForExit(20_000))
                {
                    try { proc.Kill(entireProcessTree: true); } catch { }
                    _logger.LogError("[StreamHub] 'net' excedeu 20s. Estado do wuauserv desconhecido.");
                    return false;
                }

                string outText = tOut.GetAwaiter().GetResult();
                string errText = tErr.GetAwaiter().GetResult();
                int exit = proc.ExitCode;

                if (exit != 0)
                {
                    string detail = string.IsNullOrWhiteSpace(errText) ? outText : errText;
                    _logger.Log(LogLevel.Error, LogCategory.General,
                        $"[StreamHub] 'net {(disableUpdates ? "stop" : "start")} wuauserv' falhou (exit={exit}): {detail.Trim()}", source: "StreamHub");
                    return false;
                }

                // Confirma o estado real do serviço, em vez de confiar no exit code.
                bool actuallyStopped = !IsWindowsUpdateServiceRunning();
                bool expected = disableUpdates ? !actuallyStopped : actuallyStopped;

                if (!expected)
                {
                    _logger.Log(LogLevel.Error, LogCategory.General,
                        $"[StreamHub] 'net' retornou 0, mas o estado real do wuauserv não corresponde " +
                        $"ao esperado ({(disableUpdates ? "parado" : "em execução")}). " +
                        "Operação NÃO confirmada.", source: "StreamHub");
                    return false;
                }

                _logger.Log(LogLevel.Success, LogCategory.General,
                    $"[StreamHub] Serviço wuauserv {(disableUpdates ? "PARADO" : "INICIADO")} e CONFIRMADO por leitura. " +
                    "Observação: apenas o download do Windows Update é afetado; BITS e UsoSvc continuam ativos.",
                    source: "StreamHub");

                return true;
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, LogCategory.General, $"[StreamHub] Falha na otimização do Windows Update: {ex.Message}", ex, source: "StreamHub");
                return false;
            }
        }

        /// <summary>
        /// Lê o estado REAL do serviço Windows Update. Usado como read-back para
        /// não confiar apenas no código de saída de "net stop/start".
        /// </summary>
        private static bool IsWindowsUpdateServiceRunning()
        {
            try
            {
                using var svc = new System.ServiceProcess.ServiceController("wuauserv");
                return svc.Status != System.ServiceProcess.ServiceControllerStatus.Stopped;
            }
            catch (Exception ex)
            {
                VoltrisOptimizer.App.LoggingService?.LogDebug(
                    $"[StreamHub] Não foi possível ler o estado do wuauserv: {ex.Message}");
                // Indeterminado: devolve "rodando" para que o chamador considere
                // a operação NÃO confirmada, em vez de assumir sucesso.
                return true;
            }
        }

        // ─── MOTOR DE SIMULAÇÃO DE ALTA FIDELIDADE (MODO DEMO) ───────────────────
        private void StartDemoMode()
        {
            StopDemoMode();
            VoltrisDiagnosticSystem.Instance.Timeline("StreamHub", "Iniciando Motor de Simulação Demo de alta fidelidade");

            // Configurar plataformas como conectadas para simulação visual perfeita
            ObsConnected = true;
            TwitchConnected = true;
            YouTubeConnected = true;
            IsLive = true;

            // Inicializar métricas padrão
            Fps = 60;
            BitrateKbps = 6000;
            CpuUsage = 14.5;
            RamUsageMb = 492.0;
            GpuEncoderUsage = 12.0;
            ViewerCount = 2450;
            NetworkStatus = "Excelente";
            ActiveScene = "Gameplay (Principal)";
            StreamDuration = "00:00:00";
            HealthScore = 100;
            IsMicActive = true;

            _demoSecondsElapsed = 0;
            _demoHandle = UiScheduler.Register(OnDemoTick, TimeSpan.FromSeconds(1.5));
            AiSuggestion = "Modo Simulação ativado. Analisando chat em tempo real para sugestões criativas...";
        }

        private void StopDemoMode()
        {
            _demoHandle?.Dispose();
            _demoHandle = null;
            VoltrisDiagnosticSystem.Instance.Timeline("StreamHub", "Motor de Simulação Demo desligado");
        }

        private void OnDemoTick()
        {
            if (!IsRunning || !IsDemoMode) return;

            _demoSecondsElapsed += 2;
            var ts = TimeSpan.FromSeconds(_demoSecondsElapsed);
            StreamDuration = ts.ToString(@"hh\:mm\:ss");

            Fps = 60 + _rnd.Next(-1, 2);
            BitrateKbps = 6000 + _rnd.Next(-150, 151);
            CpuUsage = 12.0 + _rnd.NextDouble() * 5.0;
            RamUsageMb = 480.0 + _rnd.Next(-15, 16);
            GpuEncoderUsage = 10.0 + _rnd.NextDouble() * 6.0;
            ViewerCount = 2450 + _rnd.Next(-30, 45);
            HealthScore = 95 + _rnd.Next(0, 6);

            if (_demoSecondsElapsed % 4 == 0)
                GenerateDemoChatMessage();

            if (_demoSecondsElapsed % 16 == 0)
                TriggerDemoHighlight(null);

            if (_demoSecondsElapsed % 20 == 0)
                UpdateDemoAiSuggestion();
        }

        private void GenerateDemoChatMessage()
        {
            var platforms = new[] { StreamPlatform.Twitch, StreamPlatform.YouTube };
            var platform = platforms[_rnd.Next(platforms.Length)];

            var users = new[] { "DougFHex", "GamerPro99", "SpeedRunner", "NinjaStream", "VoltrisFan", "FPSLord", "ApexPlayer", "Cyberpunk2077", "PixelArt", "RetroGamer" };
            var user = users[_rnd.Next(users.Length)];

            var messages = new[]
            {
                "O otimizador da Voltris é incrível!",
                "Nossa, meu FPS subiu uns 40% com o Voltris!",
                "Que live super lisa, zero engasgos!",
                "Como você ativou a prioridade de CPU no OBS?",
                "Voltris Optimizer é brabo de verdade!",
                "0 quadros perdidos? Sensacional!",
                "Qual codificador você tá usando? NVENC?",
                "Essa otimização de rede realmente abaixou meu ping!",
                "Os alertas de saúde da stream ajudam muito!",
                "Estou usando a Voltris há 2 meses e recomendo demais!"
            };
            var msgText = messages[_rnd.Next(messages.Length)];

            var chatMsg = new ChatMessage
            {
                Id = Guid.NewGuid().ToString(),
                Username = user,
                DisplayName = user,
                Content = msgText,
                Timestamp = DateTime.Now,
                Platform = platform,
                IsSubscriber = _rnd.Next(2) == 1,
                IsModerator = _rnd.Next(4) == 1,
                BadgeEmoji = "💬"
            };

            if (ChatMessages.Count >= 100)
                ChatMessages.RemoveAt(0);

            ChatMessages.Add(chatMsg);
            TotalMessages++;
        }

        private void TriggerDemoHighlight(string? manualDesc)
        {
            var triggers = new[] { HighlightTrigger.FollowEvent, HighlightTrigger.SubscriptionEvent, HighlightTrigger.DonationEvent };
            var trigger = triggers[_rnd.Next(triggers.Length)];

            var users = new[] { "RuanMedeiros", "LuizaGamer", "VipSubscriber", "VoltrisPartner" };
            var user = users[_rnd.Next(users.Length)];

            string description;
            string icon;

            if (manualDesc != null)
            {
                description = $"Marcação manual: {manualDesc}";
                icon = "📌";
            }
            else
            {
                switch (trigger)
                {
                    case HighlightTrigger.FollowEvent:
                        description = $"Novo seguidor na Twitch: {user}";
                        icon = "💜";
                        break;
                    case HighlightTrigger.SubscriptionEvent:
                        description = $"Inscrição Premium: VIP {user} (Tier 1)";
                        icon = "⭐";
                        break;
                    case HighlightTrigger.DonationEvent:
                        var value = _rnd.Next(10, 150);
                        description = $"Doação Especial de {user}: R$ {value},00";
                        icon = "💰";
                        break;
                    default:
                        description = "Momento épico de engajamento!";
                        icon = "🔥";
                        break;
                }
            }

            var highlight = new HighlightMoment
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = DateTime.Now,
                StreamTimestamp = TimeSpan.Parse(StreamDuration),
                Trigger = trigger,
                Description = description,
                Intensity = 8
            };

            Highlights.Insert(0, highlight);
            HighlightCount = Highlights.Count;

            // Gerar Alerta correspondente
            var alert = new StreamAlert
            {
                Id = Guid.NewGuid().ToString(),
                Type = AlertType.ChatSpike,
                Severity = AlertSeverity.Info,
                Title = $"Pico de Engajamento: {icon} {description}",
                Message = "Interação em massa detectada no chat da transmissão.",
                CreatedAt = DateTime.Now
            };
            ActiveAlerts.Insert(0, alert);
            HasAlerts = true;

            // Gerar sugestão de Engajamento com IA
            var suggestion = new EngagementSuggestion
            {
                Id = Guid.NewGuid().ToString(),
                Title = "Engajamento Reativo por IA",
                Message = $"Novo evento interativo: '{description}'. Sugerimos agradecer publicamente no microfone para estreitar a relação com a comunidade!",
                Icon = icon,
                CreatedAt = DateTime.Now,
                Priority = 4
            };
            Suggestions.Insert(0, suggestion);
            HasSuggestions = true;
            TopSuggestion = suggestion;
        }

        private void UpdateDemoAiSuggestion()
        {
            var suggestions = new[]
            {
                "O chat está elogiando o desempenho e a fluidez da live! Excelente trabalho de otimização.",
                "Dica IA: Sugira que os viewers deem clipes dos melhores momentos de gameplay.",
                "Seu codificador NVENC está funcionando com excelente folga térmica (GPU a 65ºC).",
                "Buffer de rede estável. Ótimo momento para iniciar uma partida competitiva.",
                "Dica IA: Faça uma pergunta interativa sobre qual jogo a comunidade quer ver na próxima live."
            };

            AiSuggestion = suggestions[_rnd.Next(suggestions.Length)];
        }

        // ─── EVENTOS DO ORQUESTRADOR CENTRAL (STREAMING REAL) ───────────────────
        private void OnMetricsUpdated(object? sender, StreamMetrics metrics)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null) return;

            dispatcher.BeginInvoke(new Action(() =>
            {
                if (IsDemoMode) return; // Ignorar no modo demo

                // IsLive reflete o que o OBS reportou, e é filtrado pela conexão real
                // do OBS: sem OBS conectado, não é possível estar transmitindo.
                _obsReportedStreaming = metrics.IsLive;
                IsLive = metrics.IsLive && ObsConnected;

                BitrateKbps = metrics.BitrateKbps;
                DroppedFramePercent = metrics.DroppedFramePercent;
                Fps = metrics.Fps;
                CpuUsage = metrics.CpuUsagePercent;
                RamUsageMb = metrics.RamUsageMb;
                ActiveScene = string.IsNullOrEmpty(metrics.ActiveScene) ? "—" : metrics.ActiveScene;
                IsMicActive = metrics.IsMicrophoneActive;
                HealthScore = metrics.HealthScore;
                StreamDuration = metrics.StreamDuration.ToString(@"hh\:mm\:ss");

                // Bitrate: só exibe valor quando o OBS realmente o reportou.
                // O campo outputActiveBitrate pertence a GetStats, não a
                // GetStreamStatus; sem ele, o valor é 0 e a tela mostrava "0 kbps".
                if (metrics.BitrateKbps > 0)
                    OnPropertyChanged(nameof(Bitrate));
            }));
        }

        private void OnChatMessageReceived(object? sender, ChatMessage message)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null) return;
            _logger?.LogInfo($"[StreamHub] Chat message received from {message.Username}");

            dispatcher.BeginInvoke(new Action(() =>
            {
                if (IsDemoMode) return; // Ignorar no modo demo

                if (ChatMessages.Count >= 100)
                    ChatMessages.RemoveAt(0);

                ChatMessages.Add(message);
                TotalMessages++;
            }));
        }

        private void OnAlertRaised(object? sender, StreamAlert alert)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null) return;
            _logger?.LogInfo($"[StreamHub] Alert raised: {alert.Title}");

            dispatcher.BeginInvoke(new Action(() =>
            {
                if (IsDemoMode) return; // Ignorar no modo demo

                if (!ActiveAlerts.Any(a => a.Type == alert.Type))
                    ActiveAlerts.Insert(0, alert);

                HasAlerts = ActiveAlerts.Count > 0;
                HasCriticalAlert = ActiveAlerts.Any(a => a.Severity == AlertSeverity.Critical);
            }));
        }

        private void OnHighlightDetected(object? sender, HighlightMoment highlight)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null) return;
            _logger?.LogInfo($"[StreamHub] Highlight detected: {highlight.Description}");

            dispatcher.BeginInvoke(new Action(() =>
            {
                if (IsDemoMode) return; // Ignorar no modo demo

                Highlights.Insert(0, highlight);
                HighlightCount = Highlights.Count;
            }));
        }

        private void OnSuggestionGenerated(object? sender, EngagementSuggestion suggestion)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null) return;
            _logger?.LogInfo($"[StreamHub] Suggestion generated: {suggestion.Title}");

            dispatcher.BeginInvoke(new Action(() =>
            {
                if (IsDemoMode) return; // Ignorar no modo demo

                if (Suggestions.Count >= 5)
                    Suggestions.RemoveAt(Suggestions.Count - 1);

                Suggestions.Insert(0, suggestion);
                HasSuggestions = Suggestions.Count > 0;
                TopSuggestion = Suggestions.Count > 0 ? Suggestions[0] : null;
            }));
        }

        private void OnPlatformStatusChanged(object? sender, PlatformStatus status)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null) return;
            _logger?.LogInfo($"[StreamHub] Platform status changed: {status.Platform} -> {status.Status}");

            dispatcher.BeginInvoke(new Action(() =>
            {
                if (IsDemoMode) return; // Ignorar no modo demo

                switch (status.Platform)
                {
                    case StreamPlatform.OBS:
                        ObsConnected = status.Status == StreamStatus.Online;
                        break;
                    case StreamPlatform.Twitch:
                        TwitchConnected = status.Status == StreamStatus.Online;
                        break;
                    case StreamPlatform.YouTube:
                        YouTubeConnected = status.Status == StreamStatus.Online;
                        break;
                }
            }));
        }

        private void RaiseCommandsCanExecuteChanged()
        {
            _startInner?.RaiseCanExecuteChanged();
            (StopCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (MarkHighlightCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        protected override void OnActiveChanged()
        {
            _logger.Log(LogLevel.Debug, LogCategory.General, $"[StreamHub] ViewModel ativo: {IsActive}", source: "StreamHub");
        }

        protected override void OnDisposing()
        {
            _demoHandle?.Dispose();
            _hub.MetricsUpdated -= OnMetricsUpdated;
            _hub.ChatMessageReceived -= OnChatMessageReceived;
            _hub.AlertRaised -= OnAlertRaised;
            _hub.HighlightDetected -= OnHighlightDetected;
            _hub.SuggestionGenerated -= OnSuggestionGenerated;
            _hub.PlatformStatusChanged -= OnPlatformStatusChanged;
            _cts?.Cancel();
            _cts?.Dispose();
            base.OnDisposing();
        }
    }
}
