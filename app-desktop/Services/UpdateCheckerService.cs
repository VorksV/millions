using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.Logging;

namespace VoltrisOptimizer.Services
{
    /// <summary>
    /// Serviço para verificação automática de atualizações
    /// Detecta novas versões e notifica o usuário
    /// </summary>
    public class UpdateCheckerService
    {
        private static UpdateCheckerService? _instance;
        public static UpdateCheckerService Instance => _instance ??= new UpdateCheckerService();

        private readonly HttpClient _httpClient;
        private readonly string _localVersionPath;
        private readonly string _remoteVersionUrl;
        
        // URLs de verificação (pode ser configurado)
        private static readonly string[] UpdateUrls = {
            "https://raw.githubusercontent.com/DougFHansen/voltris-seo-optimized/main/version.json",
            "https://api.github.com/repos/DougFHansen/voltris-seo-optimized/releases/latest",
            "https://dougfhansen.github.io/voltris-seo-optimized/version.json"
        };

        private UpdateCheckerService()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            
            // Caminho do arquivo version.json local
            var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            _localVersionPath = Path.Combine(appDirectory, "version.json");
            
            // URL remota para verificação
            _remoteVersionUrl = UpdateUrls[0]; // Primary URL
            
            // Configurar TLS para segurança
            try { System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12; } catch { }
        }

        /// <summary>
        /// Verifica se há atualizações disponíveis
        /// </summary>
        /// <returns>Informações sobre atualizações disponíveis</returns>
        public async Task<UpdateCheckResult> CheckForUpdatesAsync()
        {
            try
            {
                App.LoggingService?.LogInfo("[UpdateChecker] Iniciando verificação de atualizações...");
                
                // Obter versão local
                var localVersion = VersionService.Instance.GetCurrentVersion();
                
                // Tentar obter versão remota
                var remoteVersion = await GetRemoteVersionAsync();
                
                if (remoteVersion == null)
                {
                    App.LoggingService?.LogWarning("[UpdateChecker] Não foi possível obter versão remota");
                    return new UpdateCheckResult
                    {
                        IsUpdateAvailable = false,
                        LocalVersion = localVersion,
                        ErrorMessage = "Não foi possível conectar ao servidor de atualizações"
                    };
                }

                // Comparar versões
                var isUpdateAvailable = VersionService.Instance.IsUpdateAvailable(remoteVersion.Version);
                
                App.LoggingService?.LogInfo($"[UpdateChecker] Versão local: {localVersion.FormattedVersion}, Remota: {remoteVersion.FormattedVersion}, Update: {isUpdateAvailable}");
                
                return new UpdateCheckResult
                {
                    IsUpdateAvailable = isUpdateAvailable,
                    LocalVersion = localVersion,
                    RemoteVersion = remoteVersion,
                    ErrorMessage = null
                };
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[UpdateChecker] Erro ao verificar atualizações", ex);
                return new UpdateCheckResult
                {
                    IsUpdateAvailable = false,
                    LocalVersion = VersionService.Instance.GetCurrentVersion(),
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Obtém a versão remota do servidor
        /// </summary>
        /// <returns>Versão remota ou null se falhar</returns>
        private async Task<VersionService.VersionInfo?> GetRemoteVersionAsync()
        {
            foreach (var url in UpdateUrls)
            {
                try
                {
                    App.LoggingService?.LogInfo($"[UpdateChecker] Tentando URL: {url}");
                    
                    var response = await _httpClient.GetAsync(url);
                    if (!response.IsSuccessStatusCode) continue;

                    var json = await response.Content.ReadAsStringAsync();
                    
                    if (url.Contains("api.github.com"))
                    {
                        // Resposta da API do GitHub
                        var githubRelease = JsonSerializer.Deserialize<GitHubRelease>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                        if (githubRelease != null)
                        {
                            // Extrair versão do tag_name (ex: "v1.0.1.2")
                            var tagName = githubRelease.TagName?.TrimStart('v');
                            if (!string.IsNullOrEmpty(tagName))
                            {
                                return new VersionService.VersionInfo
                                {
                                    Version = tagName,
                                    DownloadUrl = githubRelease.HtmlUrl ?? "",
                                    Changelog = githubRelease.Body ?? "",
                                    IsMandatory = false
                                };
                            }
                        }
                    }
                    else
                    {
                        // Resposta JSON direta (version.json)
                        var versionData = JsonSerializer.Deserialize<VersionData>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                        if (versionData != null)
                        {
                            return new VersionService.VersionInfo
                            {
                                Version = versionData.LatestVersion ?? "",
                                DownloadUrl = versionData.DownloadUrl ?? "",
                                Changelog = versionData.Changelog ?? "",
                                IsMandatory = versionData.Mandatory ?? false
                            };
                        }
                    }
                }
                catch (Exception ex)
                {
                    App.LoggingService?.LogWarning($"[UpdateChecker] Falha na URL {url}: {ex.Message}");
                    continue; // Tentar próxima URL
                }
            }

            return null;
        }

        /// <summary>
        /// Verifica atualizações em background (não bloqueante)
        /// </summary>
        public async Task CheckForUpdatesInBackgroundAsync()
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(2000); // Aguardar inicialização completa
                    
                    var result = await CheckForUpdatesAsync();
                    
                    if (result.IsUpdateAvailable)
                    {
                        App.LoggingService?.LogSuccess($"[UpdateChecker] Nova versão disponível: {result.RemoteVersion?.FormattedVersion}");
                        
                        // Notificar via Telegram sobre nova versão
                        await NotifyUpdateAvailableAsync(result);
                    }
                    else
                    {
                        App.LoggingService?.LogInfo("[UpdateChecker] Sistema atualizado - nenhuma atualização disponível");
                    }
                }
                catch (Exception ex)
                {
                    App.LoggingService?.LogError("[UpdateChecker] Erro na verificação em background", ex);
                }
            });
        }

        /// <summary>
        /// Notifica sobre nova versão disponível via Telegram
        /// </summary>
        private async Task NotifyUpdateAvailableAsync(UpdateCheckResult result)
        {
            try
            {
                var remoteVersion = result.RemoteVersion;
                if (remoteVersion == null) return;

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("🆕 <b>NOVA ATUALIZAÇÃO DISPONÍVEL!</b>");
                sb.AppendLine($"<b>Versão Atual:</b> {result.LocalVersion.FormattedVersion}");
                sb.AppendLine($"<b>Nova Versão:</b> {remoteVersion.FormattedVersion}");
                sb.AppendLine($"<b>Usuário:</b> {Environment.UserName} ({Environment.MachineName})");
                sb.AppendLine($"<b>Data/Hora:</b> {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
                
                if (remoteVersion.IsMandatory)
                {
                    sb.AppendLine();
                    sb.AppendLine("🚨 <b>ATUALIZAÇÃO OBRIGATÓRIA!</b>");
                }
                
                if (!string.IsNullOrEmpty(remoteVersion.DownloadUrl))
                {
                    sb.AppendLine();
                    sb.AppendLine($"<b>Download:</b> <a href=\"{remoteVersion.DownloadUrl}\">Clique aqui para baixar</a>");
                }
                
                if (!string.IsNullOrEmpty(remoteVersion.Changelog))
                {
                    sb.AppendLine();
                    sb.AppendLine("<b>Novidades:</b>");
                    sb.AppendLine($"<pre>{System.Net.WebUtility.HtmlEncode(remoteVersion.Changelog)}</pre>");
                }

            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[UpdateChecker] Erro ao notificar sobre atualização", ex);
            }
        }

        /// <summary>
        /// Força a atualização do arquivo version.json local
        /// </summary>
        public async Task<bool> UpdateLocalVersionAsync()
        {
            try
            {
                var remoteVersion = await GetRemoteVersionAsync();
                if (remoteVersion == null) return false;

                var versionData = new VersionData
                {
                    LatestVersion = remoteVersion.Version,
                    DownloadUrl = remoteVersion.DownloadUrl,
                    Changelog = remoteVersion.Changelog,
                    Mandatory = remoteVersion.IsMandatory
                };

                var json = JsonSerializer.Serialize(versionData, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    // CORREÇÃO FORENSE #4: Previne JsonException por ciclo de referência
                    ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                await File.WriteAllTextAsync(_localVersionPath, json);
                
                // Recarregar versão no serviço
                VersionService.Instance.RefreshVersion();
                
                App.LoggingService?.LogSuccess($"[UpdateChecker] Arquivo version.json atualizado para {remoteVersion.FormattedVersion}");
                return true;
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[UpdateChecker] Erro ao atualizar arquivo version.json", ex);
                return false;
            }
        }

        /// <summary>
        /// Resultado da verificação de atualizações
        /// </summary>
        public class UpdateCheckResult
        {
            public bool IsUpdateAvailable { get; set; }
            public VersionService.VersionInfo LocalVersion { get; set; } = new();
            public VersionService.VersionInfo? RemoteVersion { get; set; }
            public string? ErrorMessage { get; set; }
            
            public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
            public string StatusMessage => IsUpdateAvailable ? 
                $"Nova versão disponível: {RemoteVersion?.FormattedVersion}" : 
                "Sistema atualizado";
        }

        /// <summary>
        /// Classe para desserialização do JSON local
        /// </summary>
        private class VersionData
        {
            public string? LatestVersion { get; set; }
            public string? DownloadUrl { get; set; }
            public string? Changelog { get; set; }
            public bool? Mandatory { get; set; }
        }

        /// <summary>
        /// Classe para desserialização da API do GitHub
        /// </summary>
        private class GitHubRelease
        {
            public string? TagName { get; set; }
            public string? HtmlUrl { get; set; }
            public string? Body { get; set; }
            public bool Prerelease { get; set; }
            public bool Draft { get; set; }
            public DateTime PublishedAt { get; set; }
        }
    }
}
