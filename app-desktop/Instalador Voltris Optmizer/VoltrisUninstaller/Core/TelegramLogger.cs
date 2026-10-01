using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace VoltrisUninstaller.Core
{
    public static class TelegramLogger
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        public static async Task SendMessageAsync(string message)
        {
            var botToken = Environment.GetEnvironmentVariable("VOLTRIS_TELEGRAM_BOT_TOKEN");
            var chatId = Environment.GetEnvironmentVariable("VOLTRIS_TELEGRAM_CHAT_ID");
            if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(chatId)) return;

            try
            {
                var url = $"https://api.telegram.org/bot{botToken}/sendMessage";
                var payload = new
                {
                    chat_id = chatId,
                    text = message,
                    parse_mode = "HTML"
                };

                var json = System.Text.Json.JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                await _httpClient.PostAsync(url, content);
            }
            catch { }
        }

        public static void SendMessageFireAndForget(string message)
        {
            _ = Task.Run(async () => await SendMessageAsync(message));
        }
    }
}
