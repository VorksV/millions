using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services
{
    /// <summary>
    /// Serviço para gerenciamento de versão do aplicativo
    /// Obtém versão atual e verifica atualizações disponíveis
    /// </summary>
    public class VersionService
    {
        private static VersionService? _instance;
        public static VersionService Instance => _instance ??= new VersionService();

        private readonly string _versionFilePath;
        private VersionInfo? _currentVersion;
        private readonly object _lock = new object();

        private VersionService()
        {
            // Caminho do arquivo version.json na pasta do aplicativo
            var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            _versionFilePath = Path.Combine(appDirectory, "version.json");
            
            LoadCurrentVersion();
        }

        /// <summary>
        /// Informações de versão
        /// </summary>
        public class VersionInfo
        {
            public string Version { get; set; } = "1.0.2.5";
            public string DownloadUrl { get; set; } = string.Empty;
            public string Changelog { get; set; } = string.Empty;
            public bool IsMandatory { get; set; } = false;
            public DateTime ReleaseDate { get; set; } = DateTime.Now;
            
            /// <summary>
            /// Versão formatada para exibição
            /// </summary>
            public string FormattedVersion => $"v{Version}";
            
            /// <summary>
            /// Versão curta (sem prefixo v)
            /// </summary>
            public string ShortVersion => Version;
        }

        /// <summary>
        /// Carrega a versão atual do arquivo version.json
        /// </summary>
        private void LoadCurrentVersion()
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(_versionFilePath))
                    {
                        var json = File.ReadAllText(_versionFilePath);
                        var versionData = JsonSerializer.Deserialize<VersionData>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                        
                        if (versionData != null)
                        {
                            _currentVersion = new VersionInfo
                            {
                                Version = versionData.LatestVersion ?? "1.0.2.5",
                                DownloadUrl = versionData.DownloadUrl ?? string.Empty,
                                Changelog = versionData.Changelog ?? string.Empty,
                                IsMandatory = versionData.Mandatory ?? false
                            };
                        }
                    }
                    else
                    {
                        // Fallback se o arquivo não existir
                        _currentVersion = new VersionInfo
                        {
                            Version = "1.0.2.5", // Versão padrão
                            Changelog = "Versão padrão - arquivo version.json não encontrado"
                        };
                    }
                }
                catch (Exception ex)
                {
                    // Em casão de erro, usar versão padrão
                    _currentVersion = new VersionInfo
                    {
                        Version = "1.0.2.5",
                        Changelog = $"Erro ao carregar versão: {ex.Message}"
                    };
                }
            }
        }

        /// <summary>
        /// Obtém a versão atual do aplicativo
        /// </summary>
        /// <returns>Versão atual</returns>
        public VersionInfo GetCurrentVersion()
        {
            lock (_lock)
            {
                return _currentVersion ?? new VersionInfo { Version = "1.0.2.5" };
            }
        }

        /// <summary>
        /// Obtém a versão atual formatada (v1.0.2.2)
        /// </summary>
        /// <returns>Versão formatada</returns>
        public string GetCurrentVersionFormatted()
        {
            return GetCurrentVersion().FormattedVersion;
        }

        /// <summary>
        /// Obtém a versão atual curta (1.0.2.2)
        /// </summary>
        /// <returns>Versão curta</returns>
        public string GetCurrentVersionShort()
        {
            return GetCurrentVersion().ShortVersion;
        }

        /// <summary>
        /// Verifica se há atualizações disponíveis (versão remota vs local)
        /// </summary>
        /// <param name="remoteVersion">Versão remota para comparar</param>
        /// <returns>True se a versão remota for mais recente</returns>
        public bool IsUpdateAvailable(string remoteVersion)
        {
            try
            {
                var current = GetCurrentVersion().Version;
                return CompareVersions(remoteVersion, current) > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Compara duas versões (formato X.Y.Z.W)
        /// </summary>
        /// <param name="version1">Primeira versão</param>
        /// <param name="version2">Segunda versão</param>
        /// <returns>
        /// -1 se version1 < version2
        /// 0 se version1 == version2
        /// 1 se version1 > version2
        /// </returns>
        private int CompareVersions(string version1, string version2)
        {
            try
            {
                var v1Parts = version1.Split('.');
                var v2Parts = version2.Split('.');

                for (int i = 0; i < Math.Max(v1Parts.Length, v2Parts.Length); i++)
                {
                    var v1Part = i < v1Parts.Length ? int.Parse(v1Parts[i]) : 0;
                    var v2Part = i < v2Parts.Length ? int.Parse(v2Parts[i]) : 0;

                    if (v1Part < v2Part) return -1;
                    if (v1Part > v2Part) return 1;
                }

                return 0;
            }
            catch
            {
                return string.Compare(version1, version2, StringComparison.Ordinal);
            }
        }

        /// <summary>
        /// Recarrega a versão do arquivo (útil após atualizações)
        /// </summary>
        public void RefreshVersion()
        {
            LoadCurrentVersion();
        }

        /// <summary>
        /// Verifica se a versão atual é válida
        /// </summary>
        /// <returns>True se a versão for válida</returns>
        public bool IsValidVersion()
        {
            try
            {
                var version = GetCurrentVersion().Version;
                var parts = version.Split('.');
                
                if (parts.Length < 2) return false;
                
                foreach (var part in parts)
                {
                    if (!int.TryParse(part, out _)) return false;
                }
                
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Obtém informações detalhadas da versão para debugging
        /// </summary>
        /// <returns>Informações detalhadas</returns>
        public string GetVersionInfo()
        {
            var version = GetCurrentVersion();
            return $"Versão: {version.FormattedVersion}\n" +
                   $"Arquivo: {_versionFilePath}\n" +
                   $"Download URL: {version.DownloadUrl}\n" +
                   $"Mandatory: {version.IsMandatory}\n" +
                   $"Changelog: {version.Changelog}\n" +
                   $"Válida: {IsValidVersion()}";
        }

        /// <summary>
        /// Classe para desseráialização do JSON
        /// </summary>
        private class VersionData
        {
            public string? LatestVersion { get; set; }
            public string? DownloadUrl { get; set; }
            public string? Changelog { get; set; }
            public bool? Mandatory { get; set; }
        }
    }
}

