using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.StreamHub.Interfaces;
using VoltrisOptimizer.Services.StreamHub.Models;

namespace VoltrisOptimizer.Services.StreamHub.Implementation
{
    public class ObsWebSocketService : IObsService, IDisposable
    {
        private readonly ILoggingService _logger;
        private ClientWebSocket? _ws;
        private CancellationTokenSource? _cts;
        private string _url = "ws://localhost:4455";
        private string _password = string.Empty;
        private bool _disposed;
        private bool _isConnected;
        private int _reconnectAttempts;
        private const int MaxReconnectAttempts = 10;
        private const int ReconnectDelayMs = 3000;
        private StreamMetrics _lastMetrics = new();
        private string _activeScene = string.Empty;
        private bool _isStreaming;
        private bool _micMuted;

        public event EventHandler<StreamMetrics>? MetricsUpdated;
        public event EventHandler<string>? SceneChanged;
        public event EventHandler<bool>? StreamStatusChanged;
        public event EventHandler<bool>? MicrophoneStatusChanged;
        public bool IsConnected => _isConnected;

        public ObsWebSocketService(ILoggingService logger) { _logger = logger; }

        public async Task<bool> ConnectAsync(string url, string password, CancellationToken ct = default)
        {
            _url = url; _password = password; _reconnectAttempts = 0;
            _logger.Log(LogLevel.Info, LogCategory.General, "Conectando ao OBS WebSocket: " + url, source: "StreamHub");
            return await ConnectInternalAsync(ct);
        }

        private async Task<bool> ConnectInternalAsync(CancellationToken ct)
        {
            try
            {
                _ws?.Dispose();
                _ws = new ClientWebSocket();
                _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                await _ws.ConnectAsync(new Uri(_url), _cts.Token);
                _logger.Log(LogLevel.Info, LogCategory.General, "WebSocket conectado. Aguardando Hello...", source: "StreamHub");
                var helloTask = ReceiveMessageAsync(_cts.Token);
                if (await Task.WhenAny(helloTask, Task.Delay(5000, _cts.Token)) != helloTask)
                {
                    _logger.Log(LogLevel.Error, LogCategory.General, "Timeout esperando Hello do OBS.", source: "StreamHub");
                    return false;
                }
                var hello = await helloTask;
                if (hello == null) { _logger.Log(LogLevel.Error, LogCategory.General, "Nao recebeu Hello do OBS.", source: "StreamHub"); return false; }
                if (!await AuthenticateAsync(hello, _cts.Token)) return false;
                _isConnected = true; _reconnectAttempts = 0;
                _logger.Log(LogLevel.Info, LogCategory.General, "OBS autenticado com sucesso.", source: "StreamHub");
                _ = Task.Run(() => ReadLoopAsync(_cts.Token), _cts.Token);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, LogCategory.General, "Erro ao conectar OBS: " + ex.Message, source: "StreamHub");
                _isConnected = false; return false;
            }
        }

        private async Task<bool> AuthenticateAsync(string helloJson, CancellationToken ct)
        {
            try
            {
                using var doc = JsonDocument.Parse(helloJson);
                var root = doc.RootElement;
                if (!root.TryGetProperty("op", out var opProp) || opProp.GetInt32() != 0) return false;
                var d = root.GetProperty("d");
                if (!d.TryGetProperty("authentication", out var authProp))
                {
                    await SendIdentifyAsync(null, ct);
                    return await WaitForIdentifiedAsync(ct);
                }
                var challenge = authProp.GetProperty("challenge").GetString() ?? string.Empty;
                var salt = authProp.GetProperty("salt").GetString() ?? string.Empty;
                var authString = ComputeObsAuth(_password, salt, challenge);
                await SendIdentifyAsync(authString, ct);
                return await WaitForIdentifiedAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, LogCategory.General, "Erro na autenticacao OBS: " + ex.Message, source: "StreamHub");
                return false;
            }
        }

        private static string ComputeObsAuth(string password, string salt, string challenge)
        {
            var s1 = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
            return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(s1 + challenge)));
        }

        private async Task SendIdentifyAsync(string? auth, CancellationToken ct)
        {
            var payload = auth != null
                ? "{\"op\":1,\"d\":{\"rpcVersion\":1,\"authentication\":\"" + auth + "\",\"eventSubscriptions\":33}}"
                : "{\"op\":1,\"d\":{\"rpcVersion\":1,\"eventSubscriptions\":33}}";
            await SendRawAsync(payload, ct);
        }

        private async Task<bool> WaitForIdentifiedAsync(CancellationToken ct)
        {
            var msgTask = ReceiveMessageAsync(ct);
            if (await Task.WhenAny(msgTask, Task.Delay(5000, ct)) != msgTask)
            {
                _logger.Log(LogLevel.Error, LogCategory.General, "Timeout aguardando Identified do OBS.", source: "StreamHub");
                return false;
            }
            var msg = await msgTask;
            if (msg == null) return false;
            using var doc = JsonDocument.Parse(msg);
            var root = doc.RootElement;
            if (root.TryGetProperty("op", out var op) && op.GetInt32() == 2) { _logger.Log(LogLevel.Info, LogCategory.General, "OBS Identified.", source: "StreamHub"); return true; }
            _logger.Log(LogLevel.Error, LogCategory.General, "Falha na identificacao OBS.", source: "StreamHub"); return false;
        }

        private async Task ReadLoopAsync(CancellationToken ct)
        {
            _logger.Log(LogLevel.Info, LogCategory.General, "Loop de leitura OBS iniciado.", source: "StreamHub");
            while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
            {
                try
                {
                    var msg = await ReceiveMessageAsync(ct);
                    if (msg == null) break;
                    ProcessMessage(msg);
                }
                catch (OperationCanceledException) { break; }
                catch (WebSocketException ex) { _logger.Log(LogLevel.Warning, LogCategory.General, "WS desconectado: " + ex.Message, source: "StreamHub"); break; }
                catch (Exception ex) { _logger.Log(LogLevel.Error, LogCategory.General, "Erro loop OBS: " + ex.Message, source: "StreamHub"); }
            }
            _isConnected = false;
            _logger.Log(LogLevel.Warning, LogCategory.General, "Loop OBS encerrado.", source: "StreamHub");
            if (!ct.IsCancellationRequested) _ = Task.Run(() => ReconnectLoopAsync(ct), ct);
        }

        private async Task ReconnectLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested && _reconnectAttempts < MaxReconnectAttempts)
                {
                    _reconnectAttempts++;
                    _logger.Log(LogLevel.Warning, LogCategory.General, string.Format("Reconexao OBS {0}/{1}...", _reconnectAttempts, MaxReconnectAttempts), source: "StreamHub");
                    await Task.Delay(ReconnectDelayMs * _reconnectAttempts, ct);
                    if (await ConnectInternalAsync(ct)) { _logger.Log(LogLevel.Info, LogCategory.General, "Reconexao OBS ok.", source: "StreamHub"); return; }
                }
                _logger.Log(LogLevel.Error, LogCategory.General, "Reconexao OBS falhou.", source: "StreamHub");
            }
            catch (OperationCanceledException)
            {
                // Ignorar cancelamento
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, LogCategory.General, "Falha critica no ReconnectLoopAsync do OBS: " + ex.Message, source: "StreamHub");
            }
        }

        private void ProcessMessage(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("op", out var opProp)) return;
                switch (opProp.GetInt32())
                {
                    case 5: ProcessEvent(root); break;
                    case 7: ProcessRequestResponse(root); break;
                }
            }
            catch (Exception ex) { _logger.Log(LogLevel.Debug, LogCategory.General, "Erro processar msg OBS: " + ex.Message, source: "StreamHub"); }
        }

        private void ProcessEvent(JsonElement root)
        {
            if (!root.TryGetProperty("d", out var d)) return;
            if (!d.TryGetProperty("eventType", out var etProp)) return;
            var et = etProp.GetString() ?? string.Empty;
            var data = d.TryGetProperty("eventData", out var ed) ? ed : default;
            switch (et)
            {
                case "CurrentProgramSceneChanged":
                    if (data.ValueKind != JsonValueKind.Undefined && data.TryGetProperty("sceneName", out var sp))
                    { _activeScene = sp.GetString() ?? _activeScene; SceneChanged?.Invoke(this, _activeScene); }
                    break;
                case "StreamStateChanged":
                    if (data.ValueKind != JsonValueKind.Undefined && data.TryGetProperty("outputActive", out var ap))
                    { _isStreaming = ap.GetBoolean(); StreamStatusChanged?.Invoke(this, _isStreaming); }
                    break;
                case "InputMuteStateChanged":
                    if (data.ValueKind != JsonValueKind.Undefined && data.TryGetProperty("inputMuted", out var mp))
                    { _micMuted = mp.GetBoolean(); MicrophoneStatusChanged?.Invoke(this, _micMuted); }
                    break;
            }
        }

        private void ProcessRequestResponse(JsonElement root)
        {
            if (!root.TryGetProperty("d", out var d)) return;
            if (!d.TryGetProperty("requestId", out var rp)) return;
            if ((rp.GetString() ?? string.Empty).StartsWith("metrics_") && d.TryGetProperty("responseData", out var rd))
                ParseMetricsResponse(rd);
        }

        private void ParseMetricsResponse(JsonElement data)
        {
            try
            {
                var metrics = new StreamMetrics { LastUpdated = DateTime.Now, IsLive = _isStreaming, ActiveScene = _activeScene, IsMicrophoneActive = !_micMuted };
                long total = 0; int dropped = 0;
                if (data.TryGetProperty("outputTotalFrames", out var tf)) total = tf.GetInt64();
                if (data.TryGetProperty("outputSkippedFrames", out var sf)) { dropped = sf.GetInt32(); metrics.DroppedFrames = dropped; }
                if (data.TryGetProperty("outputActiveBitrate", out var br)) metrics.BitrateKbps = (int)br.GetDouble();
                if (total > 0) metrics.DroppedFramePercent = (double)dropped / total * 100.0;
                _lastMetrics = metrics;
                MetricsUpdated?.Invoke(this, metrics);
                _logger.Log(LogLevel.Debug, LogCategory.General, string.Format("Metricas OBS: {0}kbps, {1}% frames perdidos", metrics.BitrateKbps, metrics.DroppedFramePercent.ToString("F1")), source: "StreamHub");
            }
            catch (Exception ex) { _logger.Log(LogLevel.Warning, LogCategory.General, "Erro parsear metricas OBS: " + ex.Message, source: "StreamHub"); }
        }

        public async Task StartMonitoringAsync(CancellationToken ct)
        {
            _logger.Log(LogLevel.Info, LogCategory.General, "Monitoramento OBS iniciado.", source: "StreamHub");
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(2000, ct);
                    if (!_isConnected || _ws?.State != WebSocketState.Open) continue;
                    var rid = "metrics_" + Guid.NewGuid().ToString("N");
                    await SendRawAsync("{\"op\":6,\"d\":{\"requestType\":\"GetStreamStatus\",\"requestId\":\"" + rid + "\"}}", ct);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { _logger.Log(LogLevel.Warning, LogCategory.General, "Erro monitoramento OBS: " + ex.Message, source: "StreamHub"); }
            }
            _logger.Log(LogLevel.Info, LogCategory.General, "Monitoramento OBS encerrado.", source: "StreamHub");
        }

        public async Task<StreamMetrics> GetCurrentMetricsAsync()
        {
            if (_isConnected && _ws?.State == WebSocketState.Open)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var rid = "metrics_" + Guid.NewGuid().ToString("N");
                await SendRawAsync("{\"op\":6,\"d\":{\"requestType\":\"GetStreamStatus\",\"requestId\":\"" + rid + "\"}}", cts.Token);
                await Task.Delay(500, cts.Token);
            }
            return _lastMetrics;
        }

        public async Task<bool> SetSceneAsync(string sceneName)
        {
            if (!_isConnected) return false;
            try
            {
                var rid = "scene_" + Guid.NewGuid().ToString("N");
                await SendRawAsync("{\"op\":6,\"d\":{\"requestType\":\"SetCurrentProgramScene\",\"requestId\":\"" + rid + "\",\"requestData\":{\"sceneName\":\"" + sceneName + "\"}}}", CancellationToken.None);
                _logger.Log(LogLevel.Info, LogCategory.General, "Cena OBS: " + sceneName, source: "StreamHub"); return true;
            }
            catch (Exception ex) { _logger.Log(LogLevel.Error, LogCategory.General, "Erro trocar cena: " + ex.Message, source: "StreamHub"); return false; }
        }

        public async Task<List<string>> GetScenesAsync()
        {
            var scenes = new List<string>();
            if (!_isConnected) return scenes;
            try
            {
                var rid = "getscenes_" + Guid.NewGuid().ToString("N");
                await SendRawAsync("{\"op\":6,\"d\":{\"requestType\":\"GetSceneList\",\"requestId\":\"" + rid + "\"}}", CancellationToken.None);
                await Task.Delay(500);
            }
            catch (Exception ex) { _logger.Log(LogLevel.Warning, LogCategory.General, "Erro obter cenas: " + ex.Message, source: "StreamHub"); }
            return scenes;
        }

        public async Task<bool> ToggleMuteAsync(string sourceName)
        {
            if (!_isConnected) return false;
            try
            {
                var rid = "mute_" + Guid.NewGuid().ToString("N");
                await SendRawAsync("{\"op\":6,\"d\":{\"requestType\":\"ToggleInputMute\",\"requestId\":\"" + rid + "\",\"requestData\":{\"inputName\":\"" + sourceName + "\"}}}", CancellationToken.None);
                _logger.Log(LogLevel.Info, LogCategory.General, "Toggle mute: " + sourceName, source: "StreamHub"); return true;
            }
            catch (Exception ex) { _logger.Log(LogLevel.Error, LogCategory.General, "Erro toggle mute: " + ex.Message, source: "StreamHub"); return false; }
        }

        public async Task DisconnectAsync()
        {
            _logger.Log(LogLevel.Info, LogCategory.General, "Desconectando OBS...", source: "StreamHub");
            _cts?.Cancel();
            if (_ws?.State == WebSocketState.Open)
                try { await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Desconectando", CancellationToken.None); } catch { }
            _isConnected = false;
            _logger.Log(LogLevel.Info, LogCategory.General, "OBS desconectado.", source: "StreamHub");
        }

        private async Task SendRawAsync(string json, CancellationToken ct)
        {
            if (_ws?.State != WebSocketState.Open) return;
            var bytes = Encoding.UTF8.GetBytes(json);
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
        }

        private async Task<string?> ReceiveMessageAsync(CancellationToken ct)
        {
            if (_ws == null) return null;
            var buffer = new byte[65536];
            var sb = new StringBuilder();
            WebSocketReceiveResult result;
            do
            {
                result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close) return null;
                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            } while (!result.EndOfMessage);
            return sb.ToString();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cts?.Cancel(); _cts?.Dispose(); _ws?.Dispose();
            _logger.Log(LogLevel.Debug, LogCategory.General, "ObsWebSocketService disposed.", source: "StreamHub");
        }
    }
}
