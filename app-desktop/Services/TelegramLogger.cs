using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Concurrent;

namespace VoltrisOptimizer.Services
{
    public static class TelegramLogger
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private static string BotToken => Environment.GetEnvironmentVariable("VOLTRIS_TELEGRAM_BOT_TOKEN") ?? string.Empty;
        private static string ChatId => Environment.GetEnvironmentVariable("VOLTRIS_TELEGRAM_CHAT_ID") ?? string.Empty;

        // FIX PRIVACIDADE (P0): respeita o opt-out TelemetryEnabled (GDPR).
        // Quando desativada, NENHUMA mensagem (incluindo PII como MachineName/UserName) é enviada.
        private static bool _telemetryBlockWarnedOnce;

        private static bool IsTelemetryAllowed()
        {
            try
            {
                bool allowed = SettingsService.Instance?.Settings?.TelemetryEnabled ?? true;
                if (!allowed && !_telemetryBlockWarnedOnce)
                {
                    _telemetryBlockWarnedOnce = true;
                    Debug.WriteLine("[TelegramLogger] Telemetria DESATIVADA (TelemetryEnabled=false). Envio de mensagens suprimido por privacidade.");
                }
                return allowed;
            }
            catch
            {
                return true;
            }
        }

        public struct TelegramLogMessage
        {
            public string Text;
            public bool IsHtml;
        }

        private static readonly BlockingCollection<TelegramLogMessage> _messageQueue = new BlockingCollection<TelegramLogMessage>(100);
        private static readonly Task _workerTask;

        static TelegramLogger()
        {
            _workerTask = Task.Run(ProcessQueueAsync);
        }

        private static async Task ProcessQueueAsync()
        {
            foreach (var message in _messageQueue.GetConsumingEnumerable())
            {
                await SendMessageAsync(message.Text, message.IsHtml);
            }
        }

        public static async Task SendMessageAsync(string message, bool isHtml = false)
        {
            if (!IsTelemetryAllowed() || string.IsNullOrWhiteSpace(BotToken) || string.IsNullOrWhiteSpace(ChatId)) return;

            try
            {
                var botToken = BotToken;
                var chatId = ChatId;
                var url = $"https://api.telegram.org/bot{botToken}/sendMessage";
                const int maxLength = 4000;
                
                for (int i = 0; i < message.Length; i += maxLength)
                {
                    int length = Math.Min(maxLength, message.Length - i);
                    string chunk = message.Substring(i, length);

                    object payload;
                    if (isHtml)
                        payload = new { chat_id = ChatId, text = chunk, parse_mode = "HTML" };
                    else
                        payload = new { chat_id = ChatId, text = chunk };
                    var json = System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions
                    {
                        // CORREÇÃO FORENSE #4: ReferenceHandler.IgnoreCycles previne JsonException
                        // "A possible object cycle was detected" registrada em múltiplas sessões.
                        ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
                        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                    });
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    await _httpClient.PostAsync(url, content);
                    await Task.Delay(250); // Delay anti-flood
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TelegramLogger] Falha ao enviar mensagem: {ex.Message}");
            }
        }

        public static void SendMessageFireAndForget(string message, bool isHtml = false)
        {
            if (!IsTelemetryAllowed()) return;

            // O(1) Queue Push - Virtualmente 0% CPU. Descarta silenciosamente se a fila estiver cheia (limite 100) para evitar estouro de RAM em caso de loop infinito de erros.
            if (_messageQueue.Count < 100)
            {
                _messageQueue.TryAdd(new TelegramLogMessage { Text = message, IsHtml = isHtml });
            }
        }
    }
}
