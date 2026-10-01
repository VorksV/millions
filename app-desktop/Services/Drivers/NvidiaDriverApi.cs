using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Drivers
{
    /// <summary>
    /// API de consulta ao centro de drivers NVIDIA.
    /// Otimizada para SSL/TLS 1.3 e resiliência contra BadGateways.
    /// </summary>
    public class NvidiaDriverApi
    {
        private readonly string _apiKey;

        public NvidiaDriverApi(string apiKey)
        {
            var sw = Stopwatch.StartNew();
            App.LoggingService?.LogInfo($"[NvidiaApi] Constructor - Conectando subsistema de telemetria HTTPS à NVIDIA Developer Program...");
            Debug.WriteLine($"[NvidiaApi] Constructor - Inicializando com apiKey length={apiKey?.Length ?? 0}");

            try
            {
                _apiKey = apiKey;
                App.LoggingService?.LogInfo($"[NvidiaApi] Constructor - Credenciais NVIDIA configuradas com sucesso em {sw.ElapsedMilliseconds}ms");
                Debug.WriteLine($"[NvidiaApi] Constructor - Concluído em {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[NvidiaApi] Constructor - Falha na construção das credenciais NVIDIA no subsistema.", ex);
                Debug.WriteLine($"[NvidiaApi] Constructor - Exceção: {ex.Message}");
            }
        }

        public async Task<DriverPackage> GetLatestDriverAsync(string hardwareId, string osVersion)
        {
            var sw = Stopwatch.StartNew();
            App.LoggingService?.LogInfo($"[NvidiaApi] GetLatestDriverAsync - Entry: hardwareId={hardwareId}, osVersion={osVersion}");
            Debug.WriteLine($"[NvidiaApi] GetLatestDriverAsync - Iniciando consulta para HWID={hardwareId}, OS={osVersion}");

            return await DriverHttpClient.ExecuteWithRetryAsync(async () =>
            {
                string url = $"https://api.nvidia.com/drivers/v1/latest?hardwareId={Uri.EscapeDataString(hardwareId)}&os={Uri.EscapeDataString(osVersion)}";
                App.LoggingService?.LogInfo($"[NvidiaApi] GetLatestDriverAsync - Request GET via DriverHttpClient: {url}");
                Debug.WriteLine($"[NvidiaApi] GetLatestDriverAsync - URL={url}");

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Api-Key", _apiKey);

                try
                {
                    var response = await DriverHttpClient.Instance.SendAsync(request);
                    var statusCode = (int)response.StatusCode;
                    var contentLength = response.Content.Headers.ContentLength ?? -1;

                    App.LoggingService?.LogInfo($"[NvidiaApi] GetLatestDriverAsync - Resposta HTTP {statusCode}, tamanho={contentLength} bytes");
                    Debug.WriteLine($"[NvidiaApi] GetLatestDriverAsync - Status={statusCode}, Content-Length={contentLength}");

                    if (response.StatusCode == System.Net.HttpStatusCode.GatewayTimeout || 
                        response.StatusCode == System.Net.HttpStatusCode.BadGateway)
                    {
                        App.LoggingService?.LogWarning($"[NvidiaApi] GetLatestDriverAsync - Gateway NVIDIA indisponível (HTTP {statusCode})");
                        Debug.WriteLine($"[NvidiaApi] GetLatestDriverAsync - Lançando HttpRequestException para gateway HTTP {statusCode}");
                        throw new HttpRequestException($"Gateway NVIDIA indisponível (HTTP {(int)response.StatusCode})", null, response.StatusCode);
                    }

                    response.EnsureSuccessStatusCode();

                    App.LoggingService?.LogInfo("[NvidiaApi] GetLatestDriverAsync - Sucesso na resposta! Parseando estrutura de serialização NVIDIA...");
                    Debug.WriteLine("[NvidiaApi] GetLatestDriverAsync - Lendo body da resposta");
                    var json = await response.Content.ReadAsStringAsync();
                    Debug.WriteLine($"[NvidiaApi] GetLatestDriverAsync - JSON recebido: {json.Length} caracteres");

                    using (JsonDocument doc = JsonDocument.Parse(json))
                    {
                        var root = doc.RootElement;
                        App.LoggingService?.LogInfo("[NvidiaApi] GetLatestDriverAsync - JSON parseado com sucesso, extraindo campos");
                        Debug.WriteLine("[NvidiaApi] GetLatestDriverAsync - JSON deserializado com sucesso");

                        var package = new DriverPackage
                        {
                            HardwareId = hardwareId,
                            Vendor = "NVIDIA",
                            Version = root.GetProperty("version").GetString() ?? "Unknown",
                            ReleaseDate = root.TryGetProperty("releaseDate", out var rd) ? rd.GetDateTime() : DateTime.Now,
                            DownloadUrl = root.GetProperty("downloadUrl").GetString() ?? "",
                            Sha256 = root.TryGetProperty("sha256", out var sha) ? sha.GetString() : "",
                            Title = root.GetProperty("productName").GetString() ?? "NVIDIA GeForce Driver"
                        };

                        App.LoggingService?.LogInfo($"[NvidiaApi] GetLatestDriverAsync - Driver capturado: {package.Version}, TotalRequestMs={sw.ElapsedMilliseconds}");
                        Debug.WriteLine($"[NvidiaApi] GetLatestDriverAsync - Driver {package.Version} retornado em {sw.ElapsedMilliseconds}ms");
                        return package;
                    }
                }
                catch (HttpRequestException ex)
                {
                    App.LoggingService?.LogWarning($"[NvidiaApi] GetLatestDriverAsync - Falha de comunicação (SSL/Timeout/Network): {ex.Message}");
                    Debug.WriteLine($"[NvidiaApi] GetLatestDriverAsync - HttpRequestException: {ex.Message}");
                    throw; // Repropaga para o retry do DriverHttpClient
                }
                catch (Exception ex)
                {
                    App.LoggingService?.LogError($"[NvidiaApi] GetLatestDriverAsync - O backend NGX da Nvidia rejeitou ou estourou uma exceção deserializando JSON.", ex);
                    Debug.WriteLine($"[NvidiaApi] GetLatestDriverAsync - Exceção não tratada: {ex.GetType().Name}: {ex.Message}");
                    return null;
                }
            });
        }
    }
}
