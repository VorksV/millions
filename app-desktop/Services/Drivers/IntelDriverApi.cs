using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Services.Telemetry;

namespace VoltrisOptimizer.Services.Drivers
{
    public class IntelDriverApi
    {
        private readonly HttpClient _httpClient;
        private bool _isServiceUnavailable = false;

        public bool IsDsaInstalled => !_isServiceUnavailable;

        public IntelDriverApi()
        {
            var sw = Stopwatch.StartNew();
            App.LoggingService?.LogInfo("[IntelApi] Constructor - Vinculando localhost Socket ao Intel DSA CLI local.");
            Debug.WriteLine("[IntelApi] Constructor - Inicializando IntelDriverApi");

            try
            {
                _httpClient = new HttpClient();
                _httpClient.Timeout = TimeSpan.FromSeconds(15);
                App.LoggingService?.LogInfo($"[IntelApi] Constructor - HttpClient configurado com timeout=15s em {sw.ElapsedMilliseconds}ms");
                Debug.WriteLine($"[IntelApi] Constructor - Concluído em {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[IntelApi] Constructor - Engavetamento no DSA handler local.", ex);
                Debug.WriteLine($"[IntelApi] Constructor - Exceção: {ex.Message}");
            }
        }

        public async Task<DriverPackage?> GetLatestDriverAsync(CurrentDriverInfo currentInfo, System.Threading.CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();
            App.LoggingService?.LogInfo($"[IntelApi] GetLatestDriverAsync - Entry: device={currentInfo.DeviceName}, version={currentInfo.Version}, hwid={currentInfo.HardwareId}");
            Debug.WriteLine($"[IntelApi] GetLatestDriverAsync - Iniciando para {currentInfo.DeviceName} v{currentInfo.Version}");

            try
            {
                // 1. Consultar API Intel Support para versão mais recente
                App.LoggingService?.LogInfo($"[IntelApi] GetLatestDriverAsync - Consultando Intel Support para {currentInfo.DeviceName}");
                Debug.WriteLine("[IntelApi] GetLatestDriverAsync - Chamando GetLatestDriverFromIntelSupport");
                var latestDriverInfo = await GetLatestDriverFromIntelSupport(currentInfo.DeviceName, currentInfo.HardwareId, cancellationToken);

                if (latestDriverInfo == null)
                {
                    App.LoggingService?.LogInfo($"[IntelApi] GetLatestDriverAsync - Nenhuma atualização online encontrada para {currentInfo.DeviceName} em {sw.ElapsedMilliseconds}ms");
                    Debug.WriteLine($"[IntelApi] GetLatestDriverAsync - Sem atualizações online, retornando null em {sw.ElapsedMilliseconds}ms");
                    return null;
                }

                App.LoggingService?.LogInfo($"[IntelApi] GetLatestDriverAsync - Versão online encontrada: v{latestDriverInfo.Version}, comparando com atual v{currentInfo.Version}");
                Debug.WriteLine($"[IntelApi] GetLatestDriverAsync - Comparando latest={latestDriverInfo.Version} vs current={currentInfo.Version}");

                // 2. Comparar versões (usando a mesma lógica centralizada para consistência)
                if (IsNewerVersion(latestDriverInfo.Version, currentInfo.Version))
                {
                    App.LoggingService?.LogSuccess($"[IntelApi] GetLatestDriverAsync - ATUALIZAÇÃO ENCONTRADA: {currentInfo.DeviceName} v{currentInfo.Version} → v{latestDriverInfo.Version} em {sw.ElapsedMilliseconds}ms");
                    Debug.WriteLine($"[IntelApi] GetLatestDriverAsync - Atualização disponível! {currentInfo.Version} -> {latestDriverInfo.Version}]");

                    return new DriverPackage
                    {
                        HardwareId = currentInfo.HardwareId,
                        Vendor = "Intel",
                        Version = latestDriverInfo.Version,
                        ReleaseDate = latestDriverInfo.Date,
                        DownloadUrl = latestDriverInfo.DownloadUrl,
                        Sha256 = latestDriverInfo.Sha256 ?? "",
                        Title = latestDriverInfo.Title
                    };
                }
                else
                {
                    App.LoggingService?.LogInfo($"[IntelApi] GetLatestDriverAsync - Driver atual já é o mais recente (v{currentInfo.Version}) em {sw.ElapsedMilliseconds}ms");
                    Debug.WriteLine($"[IntelApi] GetLatestDriverAsync - Versão atual já é a mais recente, sem atualização necessária");
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[IntelApi] GetLatestDriverAsync - Erro na verificação: {ex.Message}", ex);
                Debug.WriteLine($"[IntelApi] GetLatestDriverAsync - Exceção: {ex.GetType().Name}: {ex.Message}");
            }

            App.LoggingService?.LogInfo($"[IntelApi] GetLatestDriverAsync - Exit: retornando null após {sw.ElapsedMilliseconds}ms");
            Debug.WriteLine($"[IntelApi] GetLatestDriverAsync - Finalizado em {sw.ElapsedMilliseconds}ms, resultado=null");
            return null;
        }


        private async Task<LatestDriverInfo?> GetLatestDriverFromIntelSupport(string deviceName, string hardwareId, System.Threading.CancellationToken cancellationToken)
        {
            var sw = Stopwatch.StartNew();
            App.LoggingService?.LogInfo($"[IntelApi] GetLatestDriverFromIntelSupport - Entry: deviceName={deviceName}, hardwareId={hardwareId}");
            Debug.WriteLine($"[IntelApi] GetLatestDriverFromIntelSupport - Consultando Intel Support");

            try
            {
                App.LoggingService?.LogInfo($"[IntelApi] GetLatestDriverFromIntelSupport - Acionando ExecuteWithRetryAsync para scraping de {deviceName}");
                Debug.WriteLine("[IntelApi] GetLatestDriverFromIntelSupport - Chamando ScrapeIntelDownloadCenter via retry wrapper");

                // 🌐 Web Scraping Intel Download Center (Método Principal)
                return await DriverHttpClient.ExecuteWithRetryAsync(async () =>
                {
                    return await ScrapeIntelDownloadCenter(deviceName, hardwareId, cancellationToken);
                });
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[IntelApi] GetLatestDriverFromIntelSupport - Erro na consulta Intel Support: {ex.Message}", ex);
                Debug.WriteLine($"[IntelApi] GetLatestDriverFromIntelSupport - Exceção: {ex.GetType().Name}: {ex.Message}");
            }

            App.LoggingService?.LogInfo($"[IntelApi] GetLatestDriverFromIntelSupport - Exit: retornando null após {sw.ElapsedMilliseconds}ms");
            Debug.WriteLine($"[IntelApi] GetLatestDriverFromIntelSupport - Finalizado em {sw.ElapsedMilliseconds}ms, resultado=null");
            return null;
        }


        private async Task<LatestDriverInfo?> ScrapeIntelDownloadCenter(string deviceName, string hardwareId, System.Threading.CancellationToken cancellationToken)
        {
            var sw = Stopwatch.StartNew();
            App.LoggingService?.LogInfo($"[IntelApi] ScrapeIntelDownloadCenter - Entry: deviceName={deviceName}, hardwareId={hardwareId}");
            Debug.WriteLine($"[IntelApi] ScrapeIntelDownloadCenter - Iniciando web scraping para {deviceName}");

            try
            {
                var searchUrl = $"https://www.intel.com/content/www/us/en/download-center/search.html?text={Uri.EscapeDataString(deviceName)}";
                App.LoggingService?.LogInfo($"[IntelApi] ScrapeIntelDownloadCenter - URL de busca: {searchUrl}");
                Debug.WriteLine($"[IntelApi] ScrapeIntelDownloadCenter - searchUrl={searchUrl}");

                using (var linkedCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    linkedCts.CancelAfter(TimeSpan.FromSeconds(15));
                    using var response = await DriverHttpClient.Instance.GetAsync(searchUrl, linkedCts.Token);
                    var statusCode = (int)response.StatusCode;
                    var contentLength = response.Content.Headers.ContentLength ?? -1;

                    App.LoggingService?.LogInfo($"[IntelApi] ScrapeIntelDownloadCenter - Resposta HTTP {statusCode}, tamanho={contentLength} bytes");
                    Debug.WriteLine($"[IntelApi] ScrapeIntelDownloadCenter - Status={statusCode}, Content-Length={contentLength}");

                    if (!response.IsSuccessStatusCode)
                    {
                        App.LoggingService?.LogWarning($"[IntelApi] ScrapeIntelDownloadCenter - Intel Download Center retornou HTTP {statusCode}, abortando scraping");
                        Debug.WriteLine($"[IntelApi] ScrapeIntelDownloadCenter - StatusCode={statusCode} não é sucesso, retornando null");
                        return null;
                    }

                    var html = await response.Content.ReadAsStringAsync();
                    App.LoggingService?.LogInfo($"[IntelApi] ScrapeIntelDownloadCenter - HTML recebido: {html.Length} caracteres");
                    Debug.WriteLine($"[IntelApi] ScrapeIntelDownloadCenter - HTML size={html.Length} chars");

                    // Extrair informações do driver usando regex
                    var versionRegex = new Regex(@"Version:\s*([\d.]+)", RegexOptions.IgnoreCase);
                    var dateRegex = new Regex(@"(\d{1,2}/\d{1,2}/\d{4})", RegexOptions.IgnoreCase);
                    var downloadRegex = new Regex("href=[\"']([^\"']*download[^\"']*)[\"']", RegexOptions.IgnoreCase);

                    var versionMatch = versionRegex.Match(html);
                    var dateMatch = dateRegex.Match(html);
                    var downloadMatch = downloadRegex.Match(html);

                    App.LoggingService?.LogInfo($"[IntelApi] ScrapeIntelDownloadCenter - Regex results - version={versionMatch.Success}, date={dateMatch.Success}, download={downloadMatch.Success}");
                    Debug.WriteLine($"[IntelApi] ScrapeIntelDownloadCenter - versionMatch={versionMatch.Success}, dateMatch={dateMatch.Success}, downloadMatch={downloadMatch.Success}");

                    if (versionMatch.Success && downloadMatch.Success)
                    {
                        var downloadUrl = downloadMatch.Groups[1].Value;
                        if (!downloadUrl.StartsWith("http")) downloadUrl = $"https://www.intel.com{downloadUrl}";

                        var result = new LatestDriverInfo
                        {
                            Title = deviceName,
                            Version = versionMatch.Groups[1].Value,
                            Date = dateMatch.Success ? DateTime.Parse(dateMatch.Groups[1].Value) : DateTime.Now,
                            DownloadUrl = downloadUrl,
                            Sha256 = null
                        };

                        App.LoggingService?.LogInfo($"[IntelApi] ScrapeIntelDownloadCenter - Driver encontrado: v{result.Version}, url={result.DownloadUrl}, tempo={sw.ElapsedMilliseconds}ms");
                        Debug.WriteLine($"[IntelApi] ScrapeIntelDownloadCenter - Sucesso: v{result.Version} em {sw.ElapsedMilliseconds}ms");
                        return result;
                    }
                    else
                    {
                        App.LoggingService?.LogWarning($"[IntelApi] ScrapeIntelDownloadCenter - Regex não encontrou dados de driver no HTML (version={versionMatch.Success}, download={downloadMatch.Success})");
                        Debug.WriteLine("[IntelApi] ScrapeIntelDownloadCenter - Regex falhou em extrair informações do driver");
                    }
                }
            }
            catch (OperationCanceledException ex)
            {
                App.LoggingService?.LogWarning($"[IntelApi] ScrapeIntelDownloadCenter - Timeout no download da página Intel para {deviceName}: {ex.Message}");
                Debug.WriteLine($"[IntelApi] ScrapeIntelDownloadCenter - Timeout: {ex.Message}");
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[IntelApi] ScrapeIntelDownloadCenter - Erro no web scraping: {ex.Message}", ex);
                Debug.WriteLine($"[IntelApi] ScrapeIntelDownloadCenter - Exceção: {ex.GetType().Name}: {ex.Message}");
            }

            App.LoggingService?.LogInfo($"[IntelApi] ScrapeIntelDownloadCenter - Exit: retornando null após {sw.ElapsedMilliseconds}ms");
            Debug.WriteLine($"[IntelApi] ScrapeIntelDownloadCenter - Finalizado em {sw.ElapsedMilliseconds}ms, resultado=null");
            return null;
        }

        private bool IsNewerVersion(string latestVersion, string currentVersion)
        {
            var sw = Stopwatch.StartNew();
            App.LoggingService?.LogInfo($"[IntelApi] IsNewerVersion - Comparando latest={latestVersion} vs current={currentVersion}");
            Debug.WriteLine($"[IntelApi] IsNewerVersion - latest={latestVersion}, current={currentVersion}");

            if (string.IsNullOrEmpty(latestVersion) || string.IsNullOrEmpty(currentVersion))
            {
                App.LoggingService?.LogWarning($"[IntelApi] IsNewerVersion - Versão vazia detectada (latest='{latestVersion}', current='{currentVersion}'), retornando false");
                Debug.WriteLine("[IntelApi] IsNewerVersion - String vazia, retornando false");
                return false;
            }

            try
            {
                var v1 = new string(latestVersion.Where(c => char.IsDigit(c) || c == '.').ToArray());
                var v2 = new string(currentVersion.Where(c => char.IsDigit(c) || c == '.').ToArray());
                var latestParts = v1.Split('.', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
                var currentParts = v2.Split('.', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();

                App.LoggingService?.LogInfo($"[IntelApi] IsNewerVersion - Parsed: latest=[{string.Join(",", latestParts)}], current=[{string.Join(",", currentParts)}]");
                Debug.WriteLine($"[IntelApi] IsNewerVersion - latestParts=[{string.Join(",", latestParts)}], currentParts=[{string.Join(",", currentParts)}]");

                for (int i = 0; i < Math.Min(latestParts.Length, currentParts.Length); i++)
                {
                    if (latestParts[i] > currentParts[i])
                    {
                        App.LoggingService?.LogInfo($"[IntelApi] IsNewerVersion - Resultado: true (pos {i}: {latestParts[i]} > {currentParts[i]}) em {sw.ElapsedMilliseconds}ms");
                        Debug.WriteLine($"[IntelApi] IsNewerVersion - true em {sw.ElapsedMilliseconds}ms");
                        return true;
                    }
                    if (latestParts[i] < currentParts[i])
                    {
                        App.LoggingService?.LogInfo($"[IntelApi] IsNewerVersion - Resultado: false (pos {i}: {latestParts[i]} < {currentParts[i]}) em {sw.ElapsedMilliseconds}ms");
                        Debug.WriteLine($"[IntelApi] IsNewerVersion - false em {sw.ElapsedMilliseconds}ms");
                        return false;
                    }
                }

                var result = latestParts.Length > currentParts.Length;
                App.LoggingService?.LogInfo($"[IntelApi] IsNewerVersion - Resultado: {result} (mesmos prefixos, tam={latestParts.Length} vs {currentParts.Length}) em {sw.ElapsedMilliseconds}ms");
                Debug.WriteLine($"[IntelApi] IsNewerVersion - {result} em {sw.ElapsedMilliseconds}ms");
                return result;
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[IntelApi] IsNewerVersion - Falha no parsing numérico, usando fallback string comparison: {ex.Message}", ex);
                Debug.WriteLine($"[IntelApi] IsNewerVersion - Exceção no parsing: {ex.Message}, usando fallback ordinal");
                var fallbackResult = string.Compare(latestVersion, currentVersion, StringComparison.OrdinalIgnoreCase) > 0;
                App.LoggingService?.LogInfo($"[IntelApi] IsNewerVersion - Fallback result: {fallbackResult}");
                return fallbackResult;
            }
        }

        private class LatestDriverInfo
        {
            public string Title { get; set; }
            public string Version { get; set; }
            public DateTime Date { get; set; }
            public string DownloadUrl { get; set; }
            public string Sha256 { get; set; }
        }

        private DriverPackage? RunDsaFallbackCLI(string hardwareId)
        {
            var sw = Stopwatch.StartNew();
            App.LoggingService?.LogInfo($"[IntelApi] RunDsaFallbackCLI - Entry: hardwareId={hardwareId}");
            Debug.WriteLine("[IntelApi] RunDsaFallbackCLI - Iniciando fallback DSA CLI");

            try
            {
                App.LoggingService?.LogInfo("[IntelApi] RunDsaFallbackCLI - Spawning CLI 'IntelDSA.exe /scan' no Shell NT...");
                Debug.WriteLine("[IntelApi] RunDsaFallbackCLI - Executando IntelDSA.exe");
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "IntelDSA.exe",
                        Arguments = $"/scan /output json /target \"{hardwareId}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                App.LoggingService?.LogInfo($"[IntelApi] RunDsaFallbackCLI - Processo finalizado com ExitCode={process.ExitCode}, output length={output.Length}");
                Debug.WriteLine($"[IntelApi] RunDsaFallbackCLI - ExitCode={process.ExitCode}, output={output.Length} chars");

                if (process.ExitCode != 0)
                {
                    App.LoggingService?.LogError($"[IntelApi] RunDsaFallbackCLI - O executável nativo do DSA rejeitou comando retornando: {process.ExitCode}");
                    Debug.WriteLine($"[IntelApi] RunDsaFallbackCLI - ExitCode não-zero: {process.ExitCode}, retornando null");
                    return null;
                }

                App.LoggingService?.LogInfo("[IntelApi] RunDsaFallbackCLI - Leitura StandartOutput da CLI finalizada (Json stream).");
                Debug.WriteLine("[IntelApi] RunDsaFallbackCLI - Parseando JSON de saída");

                using (JsonDocument doc = JsonDocument.Parse(output))
                {
                    var root = doc.RootElement;
                    var package = new DriverPackage
                    {
                        HardwareId = hardwareId,
                        Vendor = "Intel",
                        Version = root.GetProperty("latest_version").GetString(),
                        ReleaseDate = DateTime.Now,
                        DownloadUrl = root.GetProperty("download_link").GetString(),
                        Title = root.GetProperty("driver_title").GetString()
                    };

                    App.LoggingService?.LogInfo($"[IntelApi] RunDsaFallbackCLI - Driver DSA encontrado: v{package.Version} em {sw.ElapsedMilliseconds}ms");
                    Debug.WriteLine($"[IntelApi] RunDsaFallbackCLI - Sucesso: v{package.Version} em {sw.ElapsedMilliseconds}ms");
                    return package;
                }
            }
            catch (JsonException ex)
            {
                App.LoggingService?.LogError($"[IntelApi] RunDsaFallbackCLI - Falha ao parsear JSON de saída do DSA CLI: {ex.Message}", ex);
                Debug.WriteLine($"[IntelApi] RunDsaFallbackCLI - JsonException: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[IntelApi] RunDsaFallbackCLI - Intel DSA CLI indisponível no sistema do usuário. Erro: {ex.Message}. Migrando fallback completo para o WUA...");
                Debug.WriteLine($"[IntelApi] RunDsaFallbackCLI - Exceção: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }
    }
}
