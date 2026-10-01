using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.StreamHub.Interfaces;
using VoltrisOptimizer.Services.StreamHub.Models;

namespace VoltrisOptimizer.Services.StreamHub.Implementation
{
    /// <summary>
    /// Integração com YouTube Live Chat via YouTube Data API v3.
    /// Faz polling da API a cada 5s (respeitando o pageToken para não duplicar mensagens).
    /// </summary>
    public class YouTubeService : IYouTubeService, IDisposable
    {
        private readonly ILoggingService _logger;
        private readonly HttpClient _http;
        private string _apiKey = string.Empty;
        private string _liveChatId = string.Empty;
        private string? _nextPageToken;
        private bool _isConnected;
        private CancellationTokenSource? _cts;
        private int _reconnectAttempts;
        private const int MaxReconnectAttempts = 10;

        public bool IsConnected => _isConnected;

        public event EventHandler<ChatMessage>? MessageReceived;
        public event EventHandler<PlatformStatus>? StatusChanged;

        public YouTubeService(ILoggingService logger)
        {
            _logger = logger;
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            _logger.Log(LogLevel.Debug, LogCategory.General, "[YouTube] YouTubeService instanciado", source: "StreamHub");
        }

        public async Task<bool> ConnectAsync(string apiKey, string liveChatId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(liveChatId))
            {
                _logger.Log(LogLevel.Warning, LogCategory.General,
                    "[YouTube] API Key ou LiveChatId não configurados. Pulando conexão.", source: "StreamHub");
                return false;
            }

            _apiKey = apiKey;
            _liveChatId = liveChatId;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            _logger.Log(LogLevel.Info, LogCategory.General,
                $"[YouTube] Conectando ao live chat {liveChatId}...", source: "StreamHub");

            try
            {
                // Testar conectividade com uma chamada inicial
                var url = BuildChatUrl(null);
                var response = await _http.GetAsync(url, _cts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(_cts.Token);
                    _logger.Log(LogLevel.Error, LogCategory.General,
                        $"[YouTube] Erro na API: {response.StatusCode} - {body.Substring(0, Math.Min(200, body.Length))}",
                        source: "StreamHub");

                    StatusChanged?.Invoke(this, new PlatformStatus
                    {
                        Platform = StreamPlatform.YouTube,
                        Status = StreamStatus.Error,
                        DisplayName = "YouTube Live",
                        ErrorMessage = $"HTTP {response.StatusCode}",
                        PlatformColor = "#FF0000"
                    });
                    return false;
                }

                _isConnected = true;
                _reconnectAttempts = 0;
                _logger.Log(LogLevel.Info, LogCategory.General, "[YouTube] Conectado com sucesso", source: "StreamHub");

                StatusChanged?.Invoke(this, new PlatformStatus
                {
                    Platform = StreamPlatform.YouTube,
                    Status = StreamStatus.Online,
                    DisplayName = "YouTube Live",
                    PlatformColor = "#FF0000"
                });

                // Iniciar polling
                _ = Task.Run(() => PollLoopAsync(_cts.Token), _cts.Token);
                return true;
            }
            catch (Exception ex)
            {
                _isConnected = false;
                _logger.Log(LogLevel.Error, LogCategory.General,
                    $"[YouTube] Erro ao conectar: {ex.Message}", ex, source: "StreamHub");

                StatusChanged?.Invoke(this, new PlatformStatus
                {
                    Platform = StreamPlatform.YouTube,
                    Status = StreamStatus.Error,
                    DisplayName = "YouTube Live",
                    ErrorMessage = ex.Message,
                    PlatformColor = "#FF0000"
                });
                return false;
            }
        }

        private async Task PollLoopAsync(CancellationToken ct)
        {
            _logger.Log(LogLevel.Debug, LogCategory.General, "[YouTube] Loop de polling iniciado", source: "StreamHub");

            while (!ct.IsCancellationRequested && _isConnected)
            {
                try
                {
                    await FetchMessagesAsync(ct);
                    await Task.Delay(5000, ct); // YouTube recomenda polling a cada 5s
                }
                catch (OperationCanceledException) { break; }
                catch (HttpRequestException httpEx)
                {
                    _logger.Log(LogLevel.Warning, LogCategory.General,
                        $"[YouTube] Erro HTTP no polling: {httpEx.Message}", source: "StreamHub");
                    _isConnected = false;
                    _ = Task.Run(() => ReconnectLoopAsync(ct), ct);
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Log(LogLevel.Warning, LogCategory.General,
                        $"[YouTube] Erro no polling: {ex.Message}", source: "StreamHub");
                    await Task.Delay(10000, ct);
                }
            }
        }

        private async Task FetchMessagesAsync(CancellationToken ct)
        {
            var url = BuildChatUrl(_nextPageToken);
            var response = await _http.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.Log(LogLevel.Warning, LogCategory.General,
                    $"[YouTube] Polling retornou {response.StatusCode}", source: "StreamHub");
                return;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Atualizar pageToken para próxima chamada
            if (root.TryGetProperty("nextPageToken", out var nextToken))
                _nextPageToken = nextToken.GetString();

            if (!root.TryGetProperty("items", out var items)) return;

            foreach (var item in items.EnumerateArray())
            {
                try
                {
                    var snippet = item.GetProperty("snippet");
                    var msgType = snippet.GetProperty("type").GetString();

                    if (msgType != "textMessageEvent") continue;

                    var details = snippet.GetProperty("textMessageDetails");
                    var text = details.GetProperty("messageText").GetString() ?? "";

                    var authorDetails = item.GetProperty("authorDetails");
                    var displayName = authorDetails.GetProperty("displayName").GetString() ?? "Unknown";
                    var channelId = authorDetails.GetProperty("channelId").GetString() ?? "";
                    bool isMod = authorDetails.TryGetProperty("isChatModerator", out var modEl) && modEl.GetBoolean();

                    var msg = new ChatMessage
                    {
                        Platform = StreamPlatform.YouTube,
                        Username = channelId,
                        DisplayName = displayName,
                        Content = text,
                        IsModerator = isMod,
                        PlatformColor = "#FF0000",
                        PlatformIcon = "Y"
                    };

                    _logger.Log(LogLevel.Debug, LogCategory.General,
                        $"[YouTube] Chat [{displayName}]: {text.Substring(0, Math.Min(50, text.Length))}",
                        source: "StreamHub");

                    MessageReceived?.Invoke(this, msg);
                }
                catch (Exception ex)
                {
                    _logger.Log(LogLevel.Debug, LogCategory.General,
                        $"[YouTube] Erro ao parsear item: {ex.Message}", source: "StreamHub");
                }
            }
        }

        private string BuildChatUrl(string? pageToken)
        {
            var url = $"https://www.googleapis.com/youtube/v3/liveChat/messages" +
                      $"?liveChatId={Uri.EscapeDataString(_liveChatId)}" +
                      $"&part=snippet,authorDetails" +
                      $"&key={_apiKey}" +
                      $"&maxResults=200";

            if (!string.IsNullOrEmpty(pageToken))
                url += $"&pageToken={Uri.EscapeDataString(pageToken)}";

            return url;
        }

        private async Task ReconnectLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _reconnectAttempts < MaxReconnectAttempts)
            {
                _reconnectAttempts++;
                int delay = Math.Min(60000, 5000 * _reconnectAttempts);

                _logger.Log(LogLevel.Warning, LogCategory.General,
                    $"[YouTube] Reconexão {_reconnectAttempts}/{MaxReconnectAttempts} em {delay}ms...",
                    source: "StreamHub");

                StatusChanged?.Invoke(this, new PlatformStatus
                {
                    Platform = StreamPlatform.YouTube,
                    Status = StreamStatus.Reconnecting,
                    DisplayName = "YouTube Live",
                    ReconnectAttempts = _reconnectAttempts,
                    PlatformColor = "#FF0000"
                });

                await Task.Delay(delay, ct);

                var success = await ConnectAsync(_apiKey, _liveChatId, ct);
                if (success) return;
            }

            _logger.Log(LogLevel.Error, LogCategory.General,
                "[YouTube] Máximo de reconexões atingido.", source: "StreamHub");
        }

        public async Task<PlatformStatus> GetStatusAsync()
        {
            return new PlatformStatus
            {
                Platform = StreamPlatform.YouTube,
                Status = _isConnected ? StreamStatus.Online : StreamStatus.Offline,
                DisplayName = "YouTube Live",
                PlatformColor = "#FF0000"
            };
        }

        public async Task DisconnectAsync()
        {
            _logger.Log(LogLevel.Info, LogCategory.General, "[YouTube] Desconectando...", source: "StreamHub");
            _isConnected = false;
            _cts?.Cancel();
            _logger.Log(LogLevel.Info, LogCategory.General, "[YouTube] Desconectado", source: "StreamHub");
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _http.Dispose();
        }
    }
}
