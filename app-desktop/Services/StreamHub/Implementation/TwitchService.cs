using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.StreamHub.Interfaces;
using VoltrisOptimizer.Services.StreamHub.Models;

namespace VoltrisOptimizer.Services.StreamHub.Implementation
{
    /// <summary>
    /// Integração com Twitch via IRC (chat em tempo real) + Helix API (status/viewers).
    /// Suporta reconexão automática e parsing de badges (mod, sub, etc.).
    /// </summary>
    public class TwitchService : ITwitchService, IDisposable
    {
        private readonly ILoggingService _logger;
        private TcpClient? _tcpClient;
        private System.IO.StreamReader? _reader;
        private System.IO.StreamWriter? _writer;
        private CancellationTokenSource? _cts;
        private string _channel = string.Empty;
        private string _oauthToken = string.Empty;
        private bool _isConnected;
        private int _reconnectAttempts;
        private const int MaxReconnectAttempts = 15;
        private const string IrcServer = "irc.chat.twitch.tv";
        private const int IrcPort = 6667;
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        public bool IsConnected => _isConnected;

        public event EventHandler<ChatMessage>? MessageReceived;
        public event EventHandler<string>? FollowReceived;
        public event EventHandler<string>? SubscriptionReceived;
        public event EventHandler<(string user, int amount)>? DonationReceived;
        public event EventHandler<PlatformStatus>? StatusChanged;

        public TwitchService(ILoggingService logger)
        {
            _logger = logger;
            _logger.Log(LogLevel.Debug, LogCategory.General, "[Twitch] TwitchService instanciado", source: "StreamHub");
        }

        public async Task<bool> ConnectAsync(string channel, string oauthToken, CancellationToken ct = default)
        {
            _channel = channel.ToLower().TrimStart('#');
            _oauthToken = oauthToken;

            _logger.Log(LogLevel.Info, LogCategory.General, $"[Twitch] Conectando ao canal #{_channel}...", source: "StreamHub");

            try
            {
                _tcpClient?.Dispose();
                _tcpClient = new TcpClient();
                _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

                await _tcpClient.ConnectAsync(IrcServer, IrcPort, _cts.Token);
                var stream = _tcpClient.GetStream();
                _reader = new System.IO.StreamReader(stream, Encoding.UTF8);
                _writer = new System.IO.StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                // Handshake IRC
                await _writer.WriteLineAsync($"PASS oauth:{_oauthToken}");
                await _writer.WriteLineAsync("NICK voltrisbot");
                await _writer.WriteLineAsync("CAP REQ :twitch.tv/tags twitch.tv/commands twitch.tv/membership");
                await _writer.WriteLineAsync($"JOIN #{_channel}");

                _isConnected = true;
                _reconnectAttempts = 0;

                _logger.Log(LogLevel.Info, LogCategory.General, $"[Twitch] Conectado ao canal #{_channel}", source: "StreamHub");

                StatusChanged?.Invoke(this, new PlatformStatus
                {
                    Platform = StreamPlatform.Twitch,
                    Status = StreamStatus.Online,
                    DisplayName = $"#{_channel}",
                    PlatformColor = "#9146FF"
                });

                // Iniciar loop de leitura
                _ = Task.Run(() => ReadLoopAsync(_cts.Token), _cts.Token);

                return true;
            }
            catch (Exception ex)
            {
                _isConnected = false;
                _logger.Log(LogLevel.Error, LogCategory.General, $"[Twitch] Erro ao conectar: {ex.Message}", ex, source: "StreamHub");

                StatusChanged?.Invoke(this, new PlatformStatus
                {
                    Platform = StreamPlatform.Twitch,
                    Status = StreamStatus.Error,
                    DisplayName = $"#{_channel}",
                    ErrorMessage = ex.Message,
                    PlatformColor = "#9146FF"
                });

                return false;
            }
        }

        private async Task ReadLoopAsync(CancellationToken ct)
        {
            _logger.Log(LogLevel.Debug, LogCategory.General, "[Twitch] Loop de leitura IRC iniciado", source: "StreamHub");

            while (!ct.IsCancellationRequested && _isConnected && _reader != null)
            {
                try
                {
                    var line = await _reader.ReadLineAsync(ct);
                    if (line == null)
                    {
                        _logger.Log(LogLevel.Warning, LogCategory.General, "[Twitch] Conexão IRC encerrada pelo servidor", source: "StreamHub");
                        break;
                    }

                    ProcessIrcLine(line);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.Log(LogLevel.Warning, LogCategory.General, $"[Twitch] Erro no loop IRC: {ex.Message}", source: "StreamHub");
                    break;
                }
            }

            if (!ct.IsCancellationRequested)
            {
                _isConnected = false;
                _logger.Log(LogLevel.Warning, LogCategory.General, "[Twitch] Desconectado. Iniciando reconexão...", source: "StreamHub");
                _ = Task.Run(() => ReconnectLoopAsync(ct), ct);
            }
        }

        private void ProcessIrcLine(string line)
        {
            try
            {
                // PING/PONG keepalive
                if (line.StartsWith("PING"))
                {
                    var pong = line.Replace("PING", "PONG");
                    _ = SendRawAsync(pong, CancellationToken.None);
                    _logger.Log(LogLevel.Debug, LogCategory.General, "[Twitch] PING/PONG respondido", source: "StreamHub");
                    return;
                }

                // Mensagem de chat: @tags :user!user@user.tmi.twitch.tv PRIVMSG #channel :message
                if (line.Contains("PRIVMSG"))
                {
                    ParsePrivMsg(line);
                    return;
                }

                // USERNOTICE = sub, resub, giftsub, raid
                if (line.Contains("USERNOTICE"))
                {
                    ParseUserNotice(line);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Debug, LogCategory.General, $"[Twitch] Erro ao processar linha IRC: {ex.Message}", source: "StreamHub");
            }
        }

        private void ParsePrivMsg(string line)
        {
            try
            {
                // Extrair tags
                string tags = string.Empty;
                string rest = line;

                if (line.StartsWith("@"))
                {
                    int spaceIdx = line.IndexOf(' ');
                    tags = line.Substring(1, spaceIdx - 1);
                    rest = line.Substring(spaceIdx + 1);
                }

                // Extrair username
                int exclamIdx = rest.IndexOf('!');
                int colonIdx = rest.IndexOf(':');
                if (exclamIdx < 0 || colonIdx < 0) return;

                string username = rest.Substring(1, exclamIdx - 1);

                // Extrair mensagem
                int msgStart = rest.IndexOf(':', 1);
                if (msgStart < 0) return;
                string message = rest.Substring(msgStart + 1).Trim();

                // Parsear tags
                bool isMod = false, isSub = false;
                string displayName = username;
                string? badgeEmoji = null;

                foreach (var tag in tags.Split(';'))
                {
                    var kv = tag.Split('=');
                    if (kv.Length < 2) continue;
                    switch (kv[0])
                    {
                        case "mod": isMod = kv[1] == "1"; break;
                        case "subscriber": isSub = kv[1] == "1"; break;
                        case "display-name": displayName = kv[1]; break;
                        case "badges":
                            if (kv[1].Contains("broadcaster")) badgeEmoji = "🎙️";
                            else if (kv[1].Contains("moderator")) badgeEmoji = "⚔️";
                            else if (kv[1].Contains("subscriber")) badgeEmoji = "⭐";
                            break;
                    }
                }

                var chatMsg = new ChatMessage
                {
                    Platform = StreamPlatform.Twitch,
                    Username = username,
                    DisplayName = string.IsNullOrEmpty(displayName) ? username : displayName,
                    Content = message,
                    IsModerator = isMod,
                    IsSubscriber = isSub,
                    BadgeEmoji = badgeEmoji,
                    PlatformColor = "#9146FF",
                    PlatformIcon = "T"
                };

                _logger.Log(LogLevel.Debug, LogCategory.General,
                    $"[Twitch] Chat [{chatMsg.DisplayName}]: {message.Substring(0, Math.Min(50, message.Length))}",
                    source: "StreamHub");

                MessageReceived?.Invoke(this, chatMsg);
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Debug, LogCategory.General, $"[Twitch] Erro ao parsear PRIVMSG: {ex.Message}", source: "StreamHub");
            }
        }

        private void ParseUserNotice(string line)
        {
            try
            {
                // Extrair msg-id para identificar tipo de evento
                if (line.Contains("msg-id=sub") || line.Contains("msg-id=resub"))
                {
                    int loginIdx = line.IndexOf("login=");
                    if (loginIdx >= 0)
                    {
                        int end = line.IndexOf(';', loginIdx);
                        string user = end > 0
                            ? line.Substring(loginIdx + 6, end - loginIdx - 6)
                            : line.Substring(loginIdx + 6);

                        _logger.Log(LogLevel.Info, LogCategory.General, $"[Twitch] Novo sub: {user}", source: "StreamHub");
                        SubscriptionReceived?.Invoke(this, user);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Debug, LogCategory.General, $"[Twitch] Erro ao parsear USERNOTICE: {ex.Message}", source: "StreamHub");
            }
        }

        private async Task ReconnectLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _reconnectAttempts < MaxReconnectAttempts)
            {
                _reconnectAttempts++;
                int delay = Math.Min(30000, 3000 * _reconnectAttempts);

                _logger.Log(LogLevel.Warning, LogCategory.General,
                    $"[Twitch] Reconexão {_reconnectAttempts}/{MaxReconnectAttempts} em {delay}ms...",
                    source: "StreamHub");

                StatusChanged?.Invoke(this, new PlatformStatus
                {
                    Platform = StreamPlatform.Twitch,
                    Status = StreamStatus.Reconnecting,
                    DisplayName = $"#{_channel}",
                    ReconnectAttempts = _reconnectAttempts,
                    PlatformColor = "#9146FF"
                });

                await Task.Delay(delay, ct);

                var success = await ConnectAsync(_channel, _oauthToken, ct);
                if (success) return;
            }

            _logger.Log(LogLevel.Error, LogCategory.General,
                $"[Twitch] Falha após {MaxReconnectAttempts} tentativas. Desistindo.",
                source: "StreamHub");

            StatusChanged?.Invoke(this, new PlatformStatus
            {
                Platform = StreamPlatform.Twitch,
                Status = StreamStatus.Error,
                DisplayName = $"#{_channel}",
                ErrorMessage = "Máximo de tentativas de reconexão atingido",
                PlatformColor = "#9146FF"
            });
        }

        public async Task<PlatformStatus> GetStatusAsync()
        {
            return new PlatformStatus
            {
                Platform = StreamPlatform.Twitch,
                Status = _isConnected ? StreamStatus.Online : StreamStatus.Offline,
                DisplayName = $"#{_channel}",
                PlatformColor = "#9146FF"
            };
        }

        public async Task SendMessageAsync(string message)
        {
            if (!_isConnected || string.IsNullOrEmpty(_channel)) return;
            _logger.Log(LogLevel.Debug, LogCategory.General, $"[Twitch] Enviando mensagem: {message}", source: "StreamHub");
            await SendRawAsync($"PRIVMSG #{_channel} :{message}", CancellationToken.None);
        }

        private async Task SendRawAsync(string line, CancellationToken ct)
        {
            if (_writer == null) return;
            await _writeLock.WaitAsync(ct);
            try { await _writer.WriteLineAsync(line); }
            finally { _writeLock.Release(); }
        }

        public async Task DisconnectAsync()
        {
            _logger.Log(LogLevel.Info, LogCategory.General, "[Twitch] Desconectando IRC...", source: "StreamHub");
            _isConnected = false;
            _cts?.Cancel();
            _tcpClient?.Dispose();
            _logger.Log(LogLevel.Info, LogCategory.General, "[Twitch] Desconectado", source: "StreamHub");
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _tcpClient?.Dispose();
            _reader?.Dispose();
            _writer?.Dispose();
            _writeLock.Dispose();
        }
    }
}
