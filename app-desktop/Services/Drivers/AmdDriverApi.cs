using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Drivers
{
    /// <summary>
    /// API de consulta ao centro de drivers AMD Adrenalin.
    /// Otimizada para resiliência de rede e DNS corporativo.
    /// </summary>
    public class AmdDriverApi
    {
        private readonly string _apiToken;

        public AmdDriverApi(string apiToken)
        {
            var sw = Stopwatch.StartNew();
            App.LoggingService?.LogInfo("[AmdApi] Constructor - Vinculando Token Bearer para Drivers Software Adrenalin...");
            Debug.WriteLine($"[AmdApi] Constructor - Inicializando com apiToken length={apiToken?.Length ?? 0}");

            try
            {
                _apiToken = apiToken;
                App.LoggingService?.LogInfo($"[AmdApi] Constructor - Credenciais AMD configuradas em {sw.ElapsedMilliseconds}ms");
                Debug.WriteLine($"[AmdApi] Constructor - Concluído em {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[AmdApi] Constructor - Corrupção ao inicializar credenciais HMAC/Bearer localmente.", ex);
                Debug.WriteLine($"[AmdApi] Constructor - Exceção: {ex.Message}");
            }
        }

        public async Task<DriverPackage> GetLatestDriverAsync(string hardwareId, string osVersion)
        {
            var sw = Stopwatch.StartNew();
            App.LoggingService?.LogInfo($"[AmdApi] GetLatestDriverAsync - Entry: hardwareId={hardwareId}, osVersion={osVersion}");
            Debug.WriteLine($"[AmdApi] GetLatestDriverAsync - Iniciando consulta para HWID={hardwareId}, OS={osVersion}");

            return await DriverHttpClient.ExecuteWithRetryAsync(async () =>
            {
                string url = $"https://api.amd.com/drivers/v1/{hardwareId}/{osVersion}";
                App.LoggingService?.LogInfo($"[AmdApi] GetLatestDriverAsync - Request GET via DriverHttpClient: {url}");
                Debug.WriteLine($"[AmdApi] GetLatestDriverAsync - URL={url}");

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiToken);

                try
                {
                    // Usa a instância compartilhada para evitar socket exhaustion
                    var response = await DriverHttpClient.Instance.SendAsync(request);
                    var statusCode = (int)response.StatusCode;
                    var contentLength = response.Content.Headers.ContentLength ?? -1;

                    App.LoggingService?.LogInfo($"[AmdApi] GetLatestDriverAsync - Resposta HTTP {statusCode}, tamanho={contentLength} bytes");
                    Debug.WriteLine($"[AmdApi] GetLatestDriverAsync - Status={statusCode}, Content-Length={contentLength}");

                    if (response.StatusCode == System.Net.HttpStatusCode.GatewayTimeout || 
                        response.StatusCode == System.Net.HttpStatusCode.BadGateway)
                    {
                        App.LoggingService?.LogWarning($"[AmdApi] GetLatestDriverAsync - Gateway AMD indisponível (HTTP {statusCode})");
                        Debug.WriteLine($"[AmdApi] GetLatestDriverAsync - Lançando HttpRequestException para gateway HTTP {statusCode}");
                        throw new HttpRequestException($"Gateway indisponível (HTTP {(int)response.StatusCode})", null, response.StatusCode);
                    }

                    response.EnsureSuccessStatusCode();

                    App.LoggingService?.LogInfo("[AmdApi] GetLatestDriverAsync - Resposta AMD Adrenalin Server 200 OK.");
                    Debug.WriteLine("[AmdApi] GetLatestDriverAsync - Lendo body da resposta");
                    var json = await response.Content.ReadAsStringAsync();
                    Debug.WriteLine($"[AmdApi] GetLatestDriverAsync - JSON recebido: {json.Length} caracteres");

                    using (JsonDocument doc = JsonDocument.Parse(json))
                    {
                        var root = doc.RootElement;
                        App.LoggingService?.LogInfo("[AmdApi] GetLatestDriverAsync - JSON parseado com sucesso, extraindo campos");
                        Debug.WriteLine("[AmdApi] GetLatestDriverAsync - JSON deserializado com sucesso");

                        var package = new DriverPackage
                        {
                            HardwareId = hardwareId,
                            Vendor = "AMD",
                            Version = root.GetProperty("version").GetString() ?? "Unknown",
                            ReleaseDate = root.TryGetProperty("releaseDate", out var rd) ? rd.GetDateTime() : DateTime.Now,
                            DownloadUrl = root.GetProperty("downloadUrl").GetString() ?? "",
                            Sha256 = root.TryGetProperty("sha256", out var sha) ? sha.GetString() : "",
                            Title = root.GetProperty("productName").GetString() ?? "AMD Adrenalin Driver"
                        };

                        App.LoggingService?.LogInfo($"[AmdApi] GetLatestDriverAsync - Driver computado: v{package.Version}, TotalRequestMs={sw.ElapsedMilliseconds}");
                        Debug.WriteLine($"[AmdApi] GetLatestDriverAsync - Driver v{package.Version} retornado em {sw.ElapsedMilliseconds}ms");
                        return package;
                    }
                }
                catch (HttpRequestException ex)
                {
                    App.LoggingService?.LogWarning($"[AmdApi] GetLatestDriverAsync - Falha na comunicação HTTP (Host AMD possivelmente inacessível): {ex.Message}");
                    Debug.WriteLine($"[AmdApi] GetLatestDriverAsync - HttpRequestException: {ex.Message}");
                    throw; // Repropaga para o retry handler do DriverHttpClient
                }
                catch (JsonException ex)
                {
                    App.LoggingService?.LogError("[AmdApi] GetLatestDriverAsync - Falha na deserialização JSON da AMD. A estrutura do endpoint pode ter mudado.", ex);
                    Debug.WriteLine($"[AmdApi] GetLatestDriverAsync - JsonException: {ex.Message}");
                    return null; // Não adianta dar retry em erro de JSON (schema mismatch)
                }
                catch (Exception ex)
                {
                    App.LoggingService?.LogError("[AmdApi] GetLatestDriverAsync - Erro estrutural não-catalogado na requisição.", ex);
                    Debug.WriteLine($"[AmdApi] GetLatestDriverAsync - Exceção não tratada: {ex.GetType().Name}: {ex.Message}");
                    return null;
                }
            });
        }
    }
}
