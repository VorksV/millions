using System;
using System.Text.Json.Serialization;

namespace VoltrisOptimizer.Core.Updater
{
    /// <summary>
    /// Informações de atualização (corresponde ao version.json no GitHub)
    /// </summary>
    public class UpdateInfo
    {
        /// <summary>
        /// Versão mais recente disponível
        /// </summary>
        [JsonPropertyName("latestVersion")]
        public string LatestVersion { get; set; } = "0.0.0";
        
        /// <summary>
        /// URL de download do executável
        /// </summary>
        [JsonPropertyName("downloadUrl")]
        public string DownloadUrl { get; set; } = "";
        
        /// <summary>
        /// Descri��o das mudanças
        /// </summary>
        /// <remarks>
        /// [FIX:C-3] O servidor pode devolver este campo como string OU como
        /// array de strings. O conversor normaliza as duas formas; sem ele, um
        /// array derrubava a desserializacao de toda a resposta de update.
        /// </remarks>
        [JsonPropertyName("changelog")]
        [JsonConverter(typeof(FlexibleChangelogConverter))]
        public string Changelog { get; set; } = "";
        
        /// <summary>
        /// Se a atualização é obrigatória
        /// </summary>
        [JsonPropertyName("mandatory")]
        public bool Mandatory { get; set; } = false;
        
        /// <summary>
        /// Verifica se há uma nova versão disponível comparada com a versão atual
        /// </summary>
        public bool IsNewerThan(string currentVersion)
        {
            return CompareVersions(LatestVersion, currentVersion) > 0;
        }
        
        /// <summary>
        /// Compara duas versões semanticamente
        /// </summary>
        public static int CompareVersions(string version1, string version2)
        {
            try
            {
                // Remover 'v' prefix se existir
                version1 = version1.TrimStart('v', 'V');
                version2 = version2.TrimStart('v', 'V');
                
                var v1 = ParseVersion(version1);
                var v2 = ParseVersion(version2);
                
                return v1.CompareTo(v2);
            }
            catch
            {
                return string.Compare(version1, version2, StringComparison.OrdinalIgnoreCase);
            }
        }
        
        private static Version ParseVersion(string versionString)
        {
            var cleanVersion = versionString.Split('-')[0];
            var parts = cleanVersion.Split('.');
            
            int major = parts.Length > 0 ? int.Parse(parts[0]) : 0;
            int minor = parts.Length > 1 ? int.Parse(parts[1]) : 0;
            int build = parts.Length > 2 ? int.Parse(parts[2]) : 0;
            int revision = parts.Length > 3 ? int.Parse(parts[3]) : 0;
            
            return new Version(major, minor, build, revision);
        }
    }
}
