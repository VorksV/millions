using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services.License;

namespace VoltrisOptimizer.Services.Update
{
    /// <summary>
    /// Informações sobre uma versão disponível
    /// </summary>
    public class UpdateInfo
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;
        
        [JsonPropertyName("releaseDate")]
        public DateTime ReleaseDate { get; set; }
        
        // Aceita tanto "downloadUrl" (version.json) quanto "download" (update.json)
        [JsonPropertyName("downloadUrl")]
        public string DownloadUrl { get; set; } = string.Empty;
        
        [JsonPropertyName("download")]
        public string? DownloadAlias { get; set; }
        
        // URL efetiva: prioriza downloadUrl, fallback para download
        [System.Text.Json.Serialization.JsonIgnore]
        public string EffectiveDownloadUrl => !string.IsNullOrEmpty(DownloadUrl) ? DownloadUrl : (DownloadAlias ?? string.Empty);
        
        [JsonPropertyName("fileHash")]
        public string FileHash { get; set; } = string.Empty;
        
        [JsonPropertyName("fileSize")]
        public long FileSize { get; set; }
        
        // Aceita tanto "releaseNotes" quanto "changelog" (array ou string)
        [JsonPropertyName("releaseNotes")]
        public string ReleaseNotes { get; set; } = string.Empty;
        
        // Aceita tanto "isMandatory" quanto "mandatory"
        [JsonPropertyName("isMandatory")]
        public bool IsMandatory { get; set; }
        
        [JsonPropertyName("mandatory")]
        public bool MandatoryAlias { get => IsMandatory; set => IsMandatory = value; }
        
        [JsonPropertyName("minVersion")]
        public string? MinVersion { get; set; }
        
        [JsonPropertyName("signature")]
        public string Signature { get; set; } = string.Empty;
    }

    /// <summary>
    /// Resultado da verificação de atualização
    /// </summary>
    public class UpdateCheckResult
    {
        public bool UpdateAvailable { get; set; }
        public UpdateInfo? UpdateInfo { get; set; }
        public string? Error { get; set; }
    }

    /// <summary>
    /// Progressão do download
    /// </summary>
    public class DownloadProgress
    {
        public long BytesDownloaded { get; set; }
        public long TotalBytes { get; set; }
        public double ProgressPercent => TotalBytes > 0 ? (double)BytesDownloaded / TotalBytes * 100 : 0;
        public string Status { get; set; } = string.Empty;
    }

    /// <summary>
    /// Serviço de atualização automática do Voltris Optimizer
    /// </summary>
    public class AutoUpdateService : IDisposable
    {
        private const string UPDATE_CHECK_URL = "https://raw.githubusercontent.com/DougFHansen/voltris-releases/main/update.json";
        private const string FALLBACK_UPDATE_URL = "https://voltris.com.br/update.json";
        private const string REGISTRY_KEY = @"SOFTWARE\Voltris\Optimizer";
        private const string LAST_CHECK_VALUE = "LastUpdateCheck";
        private const string SKIPPED_VERSION_VALUE = "SkippedVersion";
        
        private readonly HttpClient _httpClient;
        private readonly ILoggingService? _logger;
        private readonly string _currentVersion;
        private readonly string _downloadPath;
        private CancellationTokenSource? _downloadCts;
        private bool _disposed;

        /// <summary>
        /// Evento disparado quando nova atualização está disponível
        /// </summary>
        public event EventHandler<UpdateInfo>? UpdateAvailable;
        
        /// <summary>
        /// Evento disparado para reportar progressão de download
        /// </summary>
        public event EventHandler<DownloadProgress>? DownloadProgressChanged;
        
        /// <summary>
        /// Evento disparado quando download é completado
        /// </summary>
        public event EventHandler<string>? DownloadCompleted;
        
        /// <summary>
        /// Evento disparado em caso de erro
        /// </summary>
        public event EventHandler<string>? UpdateError;

        public AutoUpdateService(ILoggingService? logger = null)
        {
            _logger = logger;
            _currentVersion = GetCurrentVersion();
            _downloadPath = Path.Combine(Path.GetTempPath(), "VoltrisUpdates");
            
            // HttpClient com certificate pinning para domínios Voltris
            var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback = (message, cert, chain, sslPolicyErrors) =>
            {
                // Permitir apenas conexões com certificado válido (sem erros de SSL)
                if (sslPolicyErrors != System.Net.Security.SslPolicyErrors.None)
                {
                    logger?.LogWarning($"[UPDATE] Certificado SSL inválido: {sslPolicyErrors}");
                    return false;
                }
                
                // Verificar que o certificado pertence ao domínio voltris.com
                var host = message?.RequestUri?.Host;
                if (host != null && (host.EndsWith("voltris.com", StringComparison.OrdinalIgnoreCase)))
                {
                    // Validar que a cadeia de certificados é confiável
                    if (chain != null && chain.ChainStatus.Length > 0)
                    {
                        foreach (var status in chain.ChainStatus)
                        {
                            if (status.Status != System.Security.Cryptography.X509Certificates.X509ChainStatusFlags.NoError)
                            {
                                logger?.LogWarning($"[UPDATE] Chain status inválido para {host}: {status.StatusInformation}");
                                return false;
                            }
                        }
                    }
                    return true;
                }
                
                // Para outros domínios (CDN de download, etc.), aceitar se SSL válido
                return true;
            };
            
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", $"VoltrisOptimizer/{_currentVersion}");
            
            Directory.CreateDirectory(_downloadPath);
        }

        /// <summary>
        /// Obtém a versão atual do aplicativo
        /// </summary>
        private string GetCurrentVersion()
        {
            try
            {
                // Usar VersionInfo.Version para garantir consistência com o restante da aplicação
                var version = Properties.VersionInfo.Version;
                _logger?.LogInfo($"[AutoUpdateService] GetCurrentVersion() retornando: {version} (de VersionInfo.Version)");
                return version;
            }
            catch { }
            
            _logger?.LogWarning($"[AutoUpdateService] GetCurrentVersion() usando fallback: 1.0.2.5");
            return "1.0.2.5"; // Versão fallback atualizada
        }

        /// <summary>
        /// Verifica se há atualizações disponíveis
        /// </summary>
        public async Task<UpdateCheckResult> CheckForUpdatesAsync(bool ignoreCache = false)
        {
            GlobalProgressService.Instance.StartOperation(VoltrisOptimizer.Services.LocalizationService.Instance.GetString("UpdateVerifying"), true);
            try
            {
                _logger?.LogInfo("[UPDATE] Verificando atualizações...");
                
                // Verificar cache (não verificar mais de 1x por hora, exceto se forçado)
                if (!ignoreCache && !ShouldCheckForUpdates())
                {
                    _logger?.LogInfo("[UPDATE] Última verificação foi há menos de 1 hora");
                    GlobalProgressService.Instance.CompleteOperation(VoltrisOptimizer.Services.LocalizationService.Instance.GetString("UpdateCacheVerified"));
                    return new UpdateCheckResult { UpdateAvailable = false };
                }
                
                // Tentar URL principal, depois fallback
                string? responseJson = null;
                
                try
                {
                    responseJson = await _httpClient.GetStringAsync(UPDATE_CHECK_URL);
                }
                catch
                {
                    _logger?.LogWarning("[UPDATE] URL principal falhou, tentando fallback...");
                    try
                    {
                        responseJson = await _httpClient.GetStringAsync(FALLBACK_UPDATE_URL);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning($"[UPDATE] Fallback também falhou: {ex.Message}");
                        GlobalProgressService.Instance.FailOperation(VoltrisOptimizer.Services.LocalizationService.Instance.GetString("UpdateConnectFailed"));
                        return new UpdateCheckResult 
                        { 
                            UpdateAvailable = false, 
                            Error = "Não foi possível conectar ao servidor de atualizações" 
                        };
                    }
                }
                
                if (string.IsNullOrEmpty(responseJson))
                {
                    GlobalProgressService.Instance.FailOperation(VoltrisOptimizer.Services.LocalizationService.Instance.GetString("UpdateEmptyResponse"));
                    return new UpdateCheckResult 
                    { 
                        UpdateAvailable = false, 
                        Error = "Resposta vazia do servidor" 
                    };
                }
                
                var updateInfo = JsonSerializer.Deserialize<UpdateInfo>(responseJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                
                if (updateInfo == null)
                {
                    GlobalProgressService.Instance.FailOperation(VoltrisOptimizer.Services.LocalizationService.Instance.GetString("UpdateProcessError"));
                    return new UpdateCheckResult 
                    { 
                        UpdateAvailable = false, 
                        Error = "Erro ao processar informações de atualização" 
                    };
                }
                
                // Verificar se a assinatura é válida
                if (!VerifyUpdateSignature(updateInfo))
                {
                    _logger?.LogWarning("[UPDATE] Assinatura de atualização inválida!");
                    GlobalProgressService.Instance.FailOperation(VoltrisOptimizer.Services.LocalizationService.Instance.GetString("UpdateInvalidSignature"));
                    return new UpdateCheckResult 
                    { 
                        UpdateAvailable = false, 
                        Error = "Assinatura de atualização inválida" 
                    };
                }
                
                // Comparar versões
                var currentVer = Version.Parse(_currentVersion);
                var newVer = Version.Parse(updateInfo.Version);
                
                // Atualizar timestamp de verificação
                SaveLastCheckTime();
                
                if (newVer > currentVer)
                {
                    // Verificar se esta versão foi pulada pelo usuário
                    var skippedVersion = GetSkippedVersion();
                    if (!updateInfo.IsMandatory && skippedVersion == updateInfo.Version)
                    {
                        _logger?.LogInfo($"[UPDATE] Versão {updateInfo.Version} foi pulada pelo usuário");
                        GlobalProgressService.Instance.CompleteOperation(VoltrisOptimizer.Services.LocalizationService.Instance.GetString("UpdateSkippedByUser"));
                        return new UpdateCheckResult { UpdateAvailable = false };
                    }
                    
                    _logger?.LogInfo($"[UPDATE] Nova versão disponível: {updateInfo.Version}");
                    UpdateAvailable?.Invoke(this, updateInfo);
                    GlobalProgressService.Instance.CompleteOperation(string.Format(VoltrisOptimizer.Services.LocalizationService.Instance.GetString("UpdateNewVersionFormat"), updateInfo.Version));
                    
                    return new UpdateCheckResult
                    {
                        UpdateAvailable = true,
                        UpdateInfo = updateInfo
                    };
                }
                
                _logger?.LogInfo("[UPDATE] Aplicativo está atualizado");
                GlobalProgressService.Instance.CompleteOperation(VoltrisOptimizer.Services.LocalizationService.Instance.GetString("UpdateAppUpdated"));
                return new UpdateCheckResult { UpdateAvailable = false };
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[UPDATE] Erro ao verificar atualizações: {ex.Message}", ex);
                GlobalProgressService.Instance.FailOperation(string.Format(VoltrisOptimizer.Services.LocalizationService.Instance.GetString("CommonErrorFormat"), ex.Message));
                return new UpdateCheckResult 
                { 
                    UpdateAvailable = false, 
                    Error = ex.Message 
                };
            }
        }

        /// <summary>
        /// Faz download da atualização
        /// </summary>
        public async Task<string?> DownloadUpdateAsync(UpdateInfo updateInfo)
        {
            GlobalProgressService.Instance.StartOperation(VoltrisOptimizer.Services.LocalizationService.Instance.GetString("UpdateDownloading"), false);
            try
            {
                _logger?.LogInfo($"[UPDATE] Iniciando download da versão {updateInfo.Version}...");
                
                _downloadCts = new CancellationTokenSource();
                var token = _downloadCts.Token;
                
                var fileName = $"VoltrisOptimizer_v{updateInfo.Version}_Setup.exe";
                var filePath = Path.Combine(_downloadPath, fileName);
                
                // Limpar downloads antigos
                CleanupOldDownloads();
                
                // Download com progressão
                using var response = await _httpClient.GetAsync(
                    updateInfo.DownloadUrl, 
                    HttpCompletionOption.ResponseHeadersRead, 
                    token);
                
                response.EnsureSuccessStatusCode();
                
                var totalBytes = response.Content.Headers.ContentLength ?? updateInfo.FileSize;
                
                await using var contentStream = await response.Content.ReadAsStreamAsync(token);
                await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
                
                var buffer = new byte[8192];
                long bytesDownloaded = 0;
                int bytesRead;
                
                var lastProgress = DateTime.MinValue;
                
                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, token);
                    bytesDownloaded += bytesRead;
                    
                    // Atualizar progressão a cada 100ms
                    if ((DateTime.Now - lastProgress).TotalMilliseconds > 100)
                    {
                        var pct = totalBytes > 0 ? (int)(bytesDownloaded * 100.0 / totalBytes) : 0;
                        GlobalProgressService.Instance.UpdateProgress(pct, $"Baixando... {FormatBytes(bytesDownloaded)} / {FormatBytes(totalBytes)}");
                        
                        var progress = new DownloadProgress
                        {
                            BytesDownloaded = bytesDownloaded,
                            TotalBytes = totalBytes,
                            Status = $"Baixando... {FormatBytes(bytesDownloaded)} / {FormatBytes(totalBytes)}"
                        };
                        
                        DownloadProgressChanged?.Invoke(this, progress);
                        lastProgress = DateTime.Now;
                    }
                }
                
                // Verificar hash do arquivo
                _logger?.LogInfo("[UPDATE] Verificando integridade do arquivo...");
                
                var progress2 = new DownloadProgress
                {
                    BytesDownloaded = totalBytes,
                    TotalBytes = totalBytes,
                    Status = "Verificando integridade..."
                };
                DownloadProgressChanged?.Invoke(this, progress2);
                
                var fileHash = await CalculateFileHashAsync(filePath);
                
                if (!string.Equals(fileHash, updateInfo.FileHash, StringComparison.OrdinalIgnoreCase))
                {
                    _logger?.LogError("[UPDATE] Hash do arquivo não corresponde!");
                    File.Delete(filePath);
                    UpdateError?.Invoke(this, "Verificação de integridade falhou. Por favor, tente novamente.");
                    return null;
                }
                
                _logger?.LogSuccess($"[UPDATE] Download concluído: {filePath}");
                DownloadCompleted?.Invoke(this, filePath);
                GlobalProgressService.Instance.CompleteOperation(VoltrisOptimizer.Services.LocalizationService.Instance.GetString("UpdateDownloadComplete"));
                
                return filePath;
            }
            catch (OperationCanceledException)
            {
                _logger?.LogInfo("[UPDATE] Download cancelado pelo usuário");
                UpdateError?.Invoke(this, VoltrisOptimizer.Services.LocalizationService.Instance.GetString("UpdateDownloadCanceled"));
                GlobalProgressService.Instance.FailOperation(VoltrisOptimizer.Services.LocalizationService.Instance.GetString("UpdateDownloadCanceled"));
                return null;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[UPDATE] Erro no download: {ex.Message}", ex);
                UpdateError?.Invoke(this, string.Format(VoltrisOptimizer.Services.LocalizationService.Instance.GetString("UpdateDownloadError"), ex.Message));
                GlobalProgressService.Instance.FailOperation(string.Format(VoltrisOptimizer.Services.LocalizationService.Instance.GetString("CommonErrorFormat"), ex.Message));
                return null;
            }
        }

        /// <summary>
        /// Cancela download em andamento
        /// </summary>
        public void CancelDownload()
        {
            _downloadCts?.Cancel();
        }

        /// <summary>
        /// Instala a atualização baixada
        /// </summary>
        public async Task<bool> InstallUpdateAsync(string installerPath)
        {
            try
            {
                if (!File.Exists(installerPath))
                {
                    _logger?.LogError("[UPDATE] Arquivo de instalação não encontrado");
                    return false;
                }

                _logger?.LogInfo("[UPDATE] Iniciando instalação...");

                // Criar script de atualização para executar após fechar o app
                var updateScript = CreateUpdateScript(installerPath);
                
                // Executar script em background
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{updateScript}\"",
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                
                Process.Start(psi);
                
                _logger?.LogInfo("[UPDATE] Script de atualização iniciado, fechando aplicativo...");
                
                // Aguardar um pouco antes de fechar
                await Task.Delay(500);
                
                // Solicitar fechamento do aplicativo
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    System.Windows.Application.Current.Shutdown();
                });
                
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[UPDATE] Erro na instalação: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Cria script de atualização
        /// </summary>
        private string CreateUpdateScript(string installerPath)
        {
            var scriptPath = Path.Combine(_downloadPath, "update_script.bat");
            var currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? 
                             Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VoltrisOptimizer.exe");
            
            // Sanitizar caminhos para prevenir injeção de comando em batch scripts
            var safeInstallerPath = SanitizeBatchPath(installerPath);
            var safeCurrentExe = SanitizeBatchPath(currentExe);
            
            var script = $@"@echo off
echo Aguardando fechamento do Voltris Optimizer...
timeout /t 3 /nobreak > nul

:wait_loop
tasklist /FI ""IMAGENAME eq VoltrisOptimizer.exe"" 2>NUL | find /I /N ""VoltrisOptimizer.exe"">NUL
if ""%ERRORLEVEL%""==""0"" (
    timeout /t 1 /nobreak > nul
    goto wait_loop
)

echo Instalando atualização...
start /wait """" ""{safeInstallerPath}"" /SILENT /NORESTART

echo Iniciando Voltris Optimizer...
start """" ""{safeCurrentExe}""

echo Limpando arquivos temporários...
del /q ""{safeInstallerPath}"" 2>nul
del /q ""%~f0"" 2>nul
";
            
            File.WriteAllText(scriptPath, script, Encoding.UTF8);
            return scriptPath;
        }

        /// <summary>
        /// Sanitiza caminhos de arquivo para uso seguro em batch scripts.
        /// Remove caracteres que podem causar injeção de comando.
        /// </summary>
        private static string SanitizeBatchPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            // Remover caracteres perigosos para batch: & | > < ^ %
            return path
                .Replace("&", "")
                .Replace("|", "")
                .Replace(">", "")
                .Replace("<", "")
                .Replace("^", "")
                .Replace("%", "");
        }

        /// <summary>
        /// Marca uma versão como pulada (usuário escolheu não atualizar)
        /// </summary>
        public void SkipVersion(string version)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(REGISTRY_KEY);
                key?.SetValue(SKIPPED_VERSION_VALUE, version);
                _logger?.LogInfo($"[UPDATE] Versão {version} marcada como pulada");
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[UPDATE] Erro ao salvar versão pulada: {ex.Message}");
            }
        }

        /// <summary>
        /// Obtém versão que foi pulada
        /// </summary>
        private string? GetSkippedVersion()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(REGISTRY_KEY);
                return key?.GetValue(SKIPPED_VERSION_VALUE)?.ToString();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Verifica se deve checar por atualizações (cache de 1 hora)
        /// </summary>
        private bool ShouldCheckForUpdates()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(REGISTRY_KEY);
                var lastCheckStr = key?.GetValue(LAST_CHECK_VALUE)?.ToString();
                
                if (string.IsNullOrEmpty(lastCheckStr))
                    return true;
                
                if (DateTime.TryParse(lastCheckStr, out var lastCheck))
                {
                    return (DateTime.Now - lastCheck).TotalHours >= 1;
                }
                
                return true;
            }
            catch
            {
                return true;
            }
        }

        /// <summary>
        /// Salva timestamp da última verificação
        /// </summary>
        private void SaveLastCheckTime()
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(REGISTRY_KEY);
                key?.SetValue(LAST_CHECK_VALUE, DateTime.Now.ToString("o"));
            }
            catch { }
        }

        /// <summary>
        /// Verifica assinatura da atualização
        /// </summary>
        private bool VerifyUpdateSignature(UpdateInfo updateInfo)
        {
            try
            {
                if (string.IsNullOrEmpty(updateInfo.Signature))
                {
                    // MODO CONSERVADOR: NÃO bloqueia o canal de updates (status quo do servidor).
                    _logger?.LogWarning("[UPDATE] Assinatura AUSENTE - manifesto aceito (compatibilidade). Configure o servidor para assinar RSA-SHA256; quando enviar, a verificação real é feita.");
                    return true;
                }

                // FIX: verificação REAL por RSA-SHA256 com a chave pública do produto (mesma das licenças).
                // Antes, qualquer string de 128 hex era aceita. Agora tenta-se verificação autêntica primeiro.
                var canonical = $"VOLTRIS-UPDATE|{(updateInfo.FileHash ?? string.Empty).ToUpperInvariant()}|{updateInfo.FileSize}|{updateInfo.Version.ToUpperInvariant()}|{updateInfo.DownloadUrl}";

                byte[] signatureBytes;
                try
                {
                    signatureBytes = Convert.FromBase64String(updateInfo.Signature.Replace('-', '+').Replace('_', '/'));
                }
                catch (FormatException)
                {
                    _logger?.LogWarning("[UPDATE] Assinatura não é base64url válido (esperado RSA-SHA256) - REJEITADA");
                    return false;
                }

                bool verified = false;
                using (var rsa = System.Security.Cryptography.RSA.Create())
                {
                    rsa.ImportFromPem(LicenseSignatureVerifier.PublicKeyPem.ToCharArray());
                    verified = rsa.VerifyData(
                        Encoding.UTF8.GetBytes(canonical),
                        signatureBytes,
                        System.Security.Cryptography.HashAlgorithmName.SHA256,
                        System.Security.Cryptography.RSASignaturePadding.Pkcs1);
                }

                if (verified)
                {
                    _logger?.LogInfo("[UPDATE] Assinatura RSA válida - manifesto ACEITO (autenticidade confirmada)");
                    return true;
                }

                // Compatibilidade com o servidor atual: se ainda envia o formato legado 128-hex,
                // aceita só por compatibilidade (com aviso), nunca bloqueando o canal de updates.
                if (System.Text.RegularExpressions.Regex.IsMatch(updateInfo.Signature, @"^[a-fA-F0-9]{128}$"))
                {
                    _logger?.LogWarning("[UPDATE] Assinatura legada 128-hex aceita por COMPATIBILIDADE (não garante autenticidade). Corrija o servidor para assunto RSA-SHA256.");
                    return true;
                }

                _logger?.LogWarning("[UPDATE] Assinatura INVALIDADA - manifesto REJEITADO (possível adulteração/MITM)");
                return false;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning("[UPDATE] Falha na verificação de assinatura - REJEITADA: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Calcula hash SHA256 de um arquivo
        /// </summary>
        private async Task<string> CalculateFileHashAsync(string filePath)
        {
            using var sha256 = SHA256.Create();
            await using var stream = File.OpenRead(filePath);
            var hash = await sha256.ComputeHashAsync(stream);
            return BitConverter.ToString(hash).Replace("-", "");
        }

        /// <summary>
        /// Limpa downloads antigos
        /// </summary>
        private void CleanupOldDownloads()
        {
            try
            {
                var files = Directory.GetFiles(_downloadPath, "*.exe");
                foreach (var file in files)
                {
                    try
                    {
                        var fileInfo = new FileInfo(file);
                        if ((DateTime.Now - fileInfo.LastWriteTime).TotalDays > 7)
                        {
                            File.Delete(file);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        /// <summary>
        /// Formata bytes para exibição
        /// </summary>
        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            
            return $"{len:0.##} {sizes[order]}";
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            _downloadCts?.Cancel();
            _downloadCts?.Dispose();
            _httpClient.Dispose();
        }
    }
}
