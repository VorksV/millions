using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Drivers
{
    /// <summary>
    /// Singleton centralizado para requisições de rede dos serviços de Driver.
    /// Implementa Connection Pooling, DNS refresh e resiliência contra falhas transitórias de rede.
    /// </summary>
    public static class DriverHttpClient
    {
        private static readonly HttpClient _instance;

        static DriverHttpClient()
        {
            var sw = Stopwatch.StartNew();
            App.LoggingService?.LogInfo("[DriverHttpClient] Static Constructor - Inicializando singleton HttpClient com SocketsHttpHandler otimizado");
            Debug.WriteLine("[DriverHttpClient] Static Constructor - Configurando PooledConnectionLifetime=5min, IdleTimeout=2min, MaxConnections=10");

            try
            {
                var handler = new SocketsHttpHandler
                {
                    // ✅ Resolve o problema de DNS cache (Host AMD/NVIDIA indisponível):
                    // Força re-resolução de DNS a cada 5 minutos, evitando que o endereço
                    // expirado fique em cache e cause "NameResolutionFailure" persistente.
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5),

                    // Fecha conexões ociosas após 2 minutos para evitar sockets "zumbis"
                    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),

                    // Máximo de 10 conexões simultâneas por servidor
                    MaxConnectionsPerServer = 10,

                    // Aceita compressão gzip/brotli para economizar banda
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
                };

                _instance = new HttpClient(handler)
                {
                    // 15s por request individual — suficiente para APIs de drivers
                    Timeout = TimeSpan.FromSeconds(15)
                };

                // User-Agent padrão para evitar bloqueios de Cloudflare/WAF
                _instance.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

                App.LoggingService?.LogInfo($"[DriverHttpClient] Static Constructor - HttpClient configurado com sucesso em {sw.ElapsedMilliseconds}ms");
                Debug.WriteLine($"[DriverHttpClient] Static Constructor - Concluído em {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[DriverHttpClient] Static Constructor - Falha ao inicializar HttpClient singleton", ex);
                Debug.WriteLine($"[DriverHttpClient] Static Constructor - Exceção fatal: {ex.Message}");
                throw;
            }
        }

        /// <summary>Instância singleton compartilhada.</summary>
        public static HttpClient Instance => _instance;

        /// <summary>
        /// Executa uma tarefa com política de retentativa exponencial (Backoff).
        /// Ideal para falhas de Gateway Timeout e DNS temporário.
        /// </summary>
        public static async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> action, int maxRetries = 3)
        {
            var sw = Stopwatch.StartNew();
            int retryCount = 0;
            App.LoggingService?.LogInfo($"[DriverHttpClient] ExecuteWithRetryAsync - Entry: maxRetries={maxRetries}");
            Debug.WriteLine($"[DriverHttpClient] ExecuteWithRetryAsync - Iniciando com maxRetries={maxRetries}");

            while (true)
            {
                try
                {
                    App.LoggingService?.LogInfo($"[DriverHttpClient] ExecuteWithRetryAsync - Executando ação (tentativa {retryCount + 1})");
                    Debug.WriteLine($"[DriverHttpClient] ExecuteWithRetryAsync - Attempt {retryCount + 1} iniciando");
                    var result = await action();
                    App.LoggingService?.LogInfo($"[DriverHttpClient] ExecuteWithRetryAsync - Ação executada com sucesso na tentativa {retryCount + 1} em {sw.ElapsedMilliseconds}ms");
                    Debug.WriteLine($"[DriverHttpClient] ExecuteWithRetryAsync - Sucesso na tentativa {retryCount + 1} em {sw.ElapsedMilliseconds}ms");
                    return result;
                }
                catch (Exception ex) when (IsTransient(ex) && retryCount < maxRetries)
                {
                    retryCount++;
                    int delay = (int)Math.Pow(2, retryCount) * 1000; // 2s, 4s, 8s...
                    App.LoggingService?.LogWarning($"[DriverHttpClient] ExecuteWithRetryAsync - Falha transitória detectada. Tentativa {retryCount}/{maxRetries} em {delay}ms... Erro: {ex.Message}");
                    Debug.WriteLine($"[DriverHttpClient] ExecuteWithRetryAsync - Transient failure, retry {retryCount}/{maxRetries}, delay={delay}ms, error={ex.Message}");
                    await Task.Delay(delay);
                }
                catch (Exception ex)
                {
                    App.LoggingService?.LogError($"[DriverHttpClient] ExecuteWithRetryAsync - Falha não transitória ou retries esgotados após {retryCount} tentativas e {sw.ElapsedMilliseconds}ms", ex);
                    Debug.WriteLine($"[DriverHttpClient] ExecuteWithRetryAsync - Non-transient/exhausted failure após {sw.ElapsedMilliseconds}ms: {ex.GetType().Name}: {ex.Message}");
                    throw;
                }
            }
        }

        private static bool IsTransient(Exception ex)
        {
            var sw = Stopwatch.StartNew();
            bool result;

            if (ex is HttpRequestException httpEx)
            {
                // Erros de DNS, Timeout de Conexão ou SSL costumam ser transitórios
                result = true;
                Debug.WriteLine($"[DriverHttpClient] IsTransient - HttpRequestException detectada como transitória (DNS/SSL/Timeout) em {sw.Elapsed.TotalMicroseconds:F0}µs");
            }
            else if (ex is TaskCanceledException)
            {
                result = true; // Timeout
                Debug.WriteLine($"[DriverHttpClient] IsTransient - TaskCanceledException (timeout) detectada como transitória em {sw.Elapsed.TotalMicroseconds:F0}µs");
            }
            else
            {
                result = false;
                Debug.WriteLine($"[DriverHttpClient] IsTransient - Exceção do tipo {ex.GetType().Name} NÃO é transitória em {sw.Elapsed.TotalMicroseconds:F0}µs");
            }

            App.LoggingService?.LogInfo($"[DriverHttpClient] IsTransient - Tipo={ex.GetType().Name}, Transitório={result} ({sw.Elapsed.TotalMicroseconds:F0}µs)");
            return result;
        }
    }
}
