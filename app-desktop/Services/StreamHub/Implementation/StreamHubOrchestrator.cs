using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.StreamHub.Interfaces;
using VoltrisOptimizer.Services.StreamHub.Models;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.StreamHub.Implementation
{
    /// <summary>
    /// Orquestrador central do Stream Hub.
    /// Coordena OBS, Twitch, YouTube, Health Monitor, Highlight Detector e Engagement Assistant.
    /// Gerencia ciclo de vida, reconexões e fluxo de dados entre todos os módulos.
    /// </summary>
    public class StreamHubOrchestrator : IStreamHubService, IDisposable
    {
        private readonly ILoggingService _logger;
        private readonly IObsService _obs;
        private readonly ITwitchService _twitch;
        private readonly IYouTubeService _youtube;
        private readonly IStreamHealthMonitor _healthMonitor;
        private readonly IHighlightDetectorService _highlightDetector;
        private readonly EngagementAssistantService _engagementAssistant;

        private CancellationTokenSource? _cts;
        private bool _isRunning;
        private StreamHubSettings _settings = new();
        private StreamMetrics _currentMetrics = new();
        private readonly List<PlatformStatus> _platformStatuses = new();
        private DateTime _streamStartTime;
        private readonly object _statusLock = new();

        private static readonly string SettingsPath = AppDataPaths.StreamHubSettings;

        public bool IsRunning => _isRunning;
        public StreamHubSettings Settings => _settings;
        public StreamMetrics CurrentMetrics => _currentMetrics;
        public IReadOnlyList<PlatformStatus> PlatformStatuses
        {
            get { lock (_statusLock) return _platformStatuses.AsReadOnly(); }
        }

        public event EventHandler<StreamMetrics>? MetricsUpdated;
        public event EventHandler<ChatMessage>? ChatMessageReceived;
        public event EventHandler<StreamAlert>? AlertRaised;
        public event EventHandler<HighlightMoment>? HighlightDetected;
        public event EventHandler<EngagementSuggestion>? SuggestionGenerated;
        public event EventHandler<PlatformStatus>? PlatformStatusChanged;

        public StreamHubOrchestrator(
            ILoggingService logger,
            IObsService obs,
            ITwitchService twitch,
            IYouTubeService youtube,
            IStreamHealthMonitor healthMonitor,
            IHighlightDetectorService highlightDetector,
            EngagementAssistantService engagementAssistant)
        {
            _logger = logger;
            _obs = obs;
            _twitch = twitch;
            _youtube = youtube;
            _healthMonitor = healthMonitor;
            _highlightDetector = highlightDetector;
            _engagementAssistant = engagementAssistant;

            WireEvents();
            _logger.Log(LogLevel.Info, LogCategory.General, "[StreamHub] Orquestrador instanciado", source: "StreamHub");
        }

        private void WireEvents()
        {
            // OBS
            _obs.MetricsUpdated += (s, metrics) =>
            {
                _currentMetrics = metrics;
                _healthMonitor.UpdateMetrics(metrics);
                MetricsUpdated?.Invoke(this, metrics);
            };

            _obs.StreamStatusChanged += (s, isLive) =>
            {
                _currentMetrics.IsLive = isLive;
                if (isLive)
                {
                    _streamStartTime = DateTime.Now;
                    _highlightDetector.SetStreamStartTime(_streamStartTime);
                    _logger.Log(LogLevel.Info, LogCategory.General, "[StreamHub] Stream INICIADA", source: "StreamHub");
                }
                else
                {
                    _logger.Log(LogLevel.Info, LogCategory.General, "[StreamHub] Stream ENCERRADA", source: "StreamHub");
                }
            };

            _obs.SceneChanged += (s, scene) =>
            {
                _currentMetrics.ActiveScene = scene;
                _logger.Log(LogLevel.Info, LogCategory.General, $"[StreamHub] Cena OBS: {scene}", source: "StreamHub");
            };

            _obs.MicrophoneStatusChanged += (s, active) =>
            {
                _currentMetrics.IsMicrophoneActive = active;
                _logger.Log(LogLevel.Info, LogCategory.General,
                    $"[StreamHub] Microfone: {(active ? "ATIVO" : "MUDO")}", source: "StreamHub");
            };

            // Twitch
            _twitch.MessageReceived += (s, msg) =>
            {
                _engagementAssistant.OnChatMessage(msg);
                _highlightDetector.OnChatActivity(1);
                ChatMessageReceived?.Invoke(this, msg);
            };

            _twitch.FollowReceived += (s, user) =>
            {
                _logger.Log(LogLevel.Info, LogCategory.General, $"[StreamHub] Novo follow Twitch: {user}", source: "StreamHub");
                _highlightDetector.OnPlatformEvent(HighlightTrigger.FollowEvent, $"Novo follow: {user}");
            };

            _twitch.SubscriptionReceived += (s, user) =>
            {
                _logger.Log(LogLevel.Info, LogCategory.General, $"[StreamHub] Novo sub Twitch: {user}", source: "StreamHub");
                _highlightDetector.OnPlatformEvent(HighlightTrigger.SubscriptionEvent, $"Novo sub: {user}");
            };

            _twitch.DonationReceived += (s, donation) =>
            {
                _logger.Log(LogLevel.Info, LogCategory.General,
                    $"[StreamHub] Doação Twitch: {donation.user} - R${donation.amount}", source: "StreamHub");
                _highlightDetector.OnPlatformEvent(HighlightTrigger.DonationEvent,
                    $"Doação de {donation.user}: R${donation.amount}");
            };

            _twitch.StatusChanged += (s, status) => UpdatePlatformStatus(status);

            // YouTube
            _youtube.MessageReceived += (s, msg) =>
            {
                _engagementAssistant.OnChatMessage(msg);
                _highlightDetector.OnChatActivity(1);
                ChatMessageReceived?.Invoke(this, msg);
            };

            _youtube.StatusChanged += (s, status) => UpdatePlatformStatus(status);

            // Health Monitor
            _healthMonitor.AlertRaised += (s, alert) =>
            {
                _logger.Log(LogLevel.Warning, LogCategory.General,
                    $"[StreamHub] Alerta: {alert.Title}", source: "StreamHub");
                AlertRaised?.Invoke(this, alert);
            };

            // Highlight Detector
            _highlightDetector.HighlightDetected += (s, highlight) =>
            {
                _logger.Log(LogLevel.Info, LogCategory.General,
                    $"[StreamHub] Highlight: {highlight.Description}", source: "StreamHub");
                HighlightDetected?.Invoke(this, highlight);
            };

            // Engagement Assistant
            _engagementAssistant.SuggestionGenerated += (s, suggestion) =>
            {
                _logger.Log(LogLevel.Info, LogCategory.General,
                    $"[StreamHub] Sugestão: {suggestion.Title}", source: "StreamHub");
                SuggestionGenerated?.Invoke(this, suggestion);
            };
        }

        private void UpdatePlatformStatus(PlatformStatus status)
        {
            lock (_statusLock)
            {
                var existing = _platformStatuses.FirstOrDefault(p => p.Platform == status.Platform);
                if (existing != null)
                    _platformStatuses.Remove(existing);
                _platformStatuses.Add(status);
            }

            _logger.Log(LogLevel.Info, LogCategory.General,
                $"[StreamHub] Status {status.Platform}: {status.Status}", source: "StreamHub");
            PlatformStatusChanged?.Invoke(this, status);
        }

        public async Task StartAsync(StreamHubSettings settings, CancellationToken ct = default)
        {
            if (_isRunning)
            {
                _logger.Log(LogLevel.Warning, LogCategory.General, "[StreamHub] Já está rodando", source: "StreamHub");
                return;
            }

            _settings = settings;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _isRunning = true;

            _logger.Log(LogLevel.Info, LogCategory.General, "[StreamHub] Iniciando orquestrador...", source: "StreamHub");

            // Conectar plataformas configuradas em paralelo
            var tasks = new List<Task>();

            if (settings.ObsAutoConnect && !string.IsNullOrEmpty(settings.ObsWebSocketUrl))
                tasks.Add(ConnectObsAsync());

            if (!string.IsNullOrEmpty(settings.TwitchChannelName))
                tasks.Add(ConnectTwitchAsync());

            if (!string.IsNullOrEmpty(settings.YouTubeApiKey) && !string.IsNullOrEmpty(settings.YouTubeLiveChatId))
                tasks.Add(ConnectYouTubeAsync());

            await Task.WhenAll(tasks);

            // Iniciar loop de monitoramento periódico
            _ = Task.Run(() => MonitoringLoopAsync(_cts.Token), _cts.Token);

            _logger.Log(LogLevel.Info, LogCategory.General, "[StreamHub] Orquestrador iniciado com sucesso", source: "StreamHub");
        }

        private async Task MonitoringLoopAsync(CancellationToken ct)
        {
            _logger.Log(LogLevel.Debug, LogCategory.General, "[StreamHub] Loop de monitoramento iniciado", source: "StreamHub");

            while (!ct.IsCancellationRequested && _isRunning)
            {
                try
                {
                    // Tick do assistente de engajamento (verifica idle)
                    _engagementAssistant.Tick();

                    // Atualizar duração da stream
                    if (_currentMetrics.IsLive)
                        _currentMetrics.StreamDuration = DateTime.Now - _streamStartTime;

                    await Task.Delay(5000, ct); // Tick a cada 5s
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.Log(LogLevel.Warning, LogCategory.General,
                        $"[StreamHub] Erro no loop de monitoramento: {ex.Message}", source: "StreamHub");
                    await Task.Delay(10000, ct);
                }
            }

            _logger.Log(LogLevel.Debug, LogCategory.General, "[StreamHub] Loop de monitoramento encerrado", source: "StreamHub");
        }

        public async Task<bool> ConnectObsAsync()
        {
            _logger.Log(LogLevel.Info, LogCategory.General,
                $"[StreamHub] Conectando OBS em {_settings.ObsWebSocketUrl}...", source: "StreamHub");

            UpdatePlatformStatus(new PlatformStatus
            {
                Platform = StreamPlatform.OBS,
                Status = StreamStatus.Connecting,
                DisplayName = "OBS Studio",
                PlatformColor = "#1A1A2E"
            });

            var success = await _obs.ConnectAsync(_settings.ObsWebSocketUrl, _settings.ObsWebSocketPassword,
                _cts?.Token ?? CancellationToken.None);

            if (success)
            {
                UpdatePlatformStatus(new PlatformStatus
                {
                    Platform = StreamPlatform.OBS,
                    Status = StreamStatus.Online,
                    DisplayName = "OBS Studio",
                    PlatformColor = "#1A1A2E"
                });

                // Iniciar monitoramento de métricas OBS
                _ = Task.Run(() => _obs.StartMonitoringAsync(_cts?.Token ?? CancellationToken.None));
            }
            else
            {
                UpdatePlatformStatus(new PlatformStatus
                {
                    Platform = StreamPlatform.OBS,
                    Status = StreamStatus.Error,
                    DisplayName = "OBS Studio",
                    ErrorMessage = "Falha ao conectar. Verifique se o OBS está aberto e o WebSocket habilitado.",
                    PlatformColor = "#1A1A2E"
                });
            }

            return success;
        }

        public async Task<bool> ConnectTwitchAsync()
        {
            if (string.IsNullOrEmpty(_settings.TwitchChannelName))
            {
                _logger.Log(LogLevel.Warning, LogCategory.General,
                    "[StreamHub] Canal Twitch não configurado", source: "StreamHub");
                return false;
            }

            return await _twitch.ConnectAsync(
                _settings.TwitchChannelName,
                _settings.TwitchOAuthToken,
                _cts?.Token ?? CancellationToken.None);
        }

        public async Task<bool> ConnectYouTubeAsync()
        {
            if (string.IsNullOrEmpty(_settings.YouTubeApiKey))
            {
                _logger.Log(LogLevel.Warning, LogCategory.General,
                    "[StreamHub] YouTube API Key não configurada", source: "StreamHub");
                return false;
            }

            return await _youtube.ConnectAsync(
                _settings.YouTubeApiKey,
                _settings.YouTubeLiveChatId,
                _cts?.Token ?? CancellationToken.None);
        }

        public void MarkHighlight(string description = "Marcação manual")
        {
            var streamTimestamp = _currentMetrics.IsLive
                ? DateTime.Now - _streamStartTime
                : TimeSpan.Zero;

            _highlightDetector.MarkManualHighlight(streamTimestamp, description);
        }

        public async Task<bool> SwitchObsSceneAsync(string sceneName)
        {
            _logger.Log(LogLevel.Info, LogCategory.General,
                $"[StreamHub] Trocando cena OBS para: {sceneName}", source: "StreamHub");
            return await _obs.SetSceneAsync(sceneName);
        }

        public async Task<List<string>> GetObsScenesAsync()
        {
            return await _obs.GetScenesAsync();
        }

        public async Task StopAsync()
        {
            _logger.Log(LogLevel.Info, LogCategory.General, "[StreamHub] Parando orquestrador...", source: "StreamHub");
            _isRunning = false;
            _cts?.Cancel();

            await Task.WhenAll(
                _obs.DisconnectAsync(),
                _twitch.DisconnectAsync(),
                _youtube.DisconnectAsync()
            );

            _engagementAssistant.Reset();
            _healthMonitor.ClearAll();

            _logger.Log(LogLevel.Info, LogCategory.General, "[StreamHub] Orquestrador parado", source: "StreamHub");
        }

        public void SaveSettings(StreamHubSettings settings)
        {
            try
            {
                _settings = settings;
                var dir = Path.GetDirectoryName(SettingsPath)!;
                Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,  WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
                _logger.Log(LogLevel.Info, LogCategory.General, "[StreamHub] Configurações salvas", source: "StreamHub");
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, LogCategory.General,
                    $"[StreamHub] Erro ao salvar configurações: {ex.Message}", ex, source: "StreamHub");
            }
        }

        public StreamHubSettings LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    var settings = JsonSerializer.Deserialize<StreamHubSettings>(json);
                    if (settings != null)
                    {
                        _settings = settings;
                        _logger.Log(LogLevel.Info, LogCategory.General, "[StreamHub] Configurações carregadas", source: "StreamHub");
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Warning, LogCategory.General,
                    $"[StreamHub] Erro ao carregar configurações: {ex.Message}", source: "StreamHub");
            }

            return new StreamHubSettings();
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            (_obs as IDisposable)?.Dispose();
            (_twitch as IDisposable)?.Dispose();
            (_youtube as IDisposable)?.Dispose();
        }
    }
}
