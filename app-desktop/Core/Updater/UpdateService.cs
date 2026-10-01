using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Core.Updater
{
    /// <summary>
    /// Serviço de atualização automática - busca, baixa e aplica atualizações
    /// </summary>
    public class UpdateService
    {
        // =====================================================
        // CONFIGURAÇÃO - ALTERE O REPOSITÓRIO AQUI
        // =====================================================
        // Para repositórios privados, usamos GitHub Releases API
        // Releases públicos podem ser acessados mesmo de repositórios privados
        private const string GITHUB_REPO_OWNER = "DougFHansen";
        private const string GITHUB_REPO_NAME = "voltris-releases"; // Único repositório (instalador e updater)
        private const string GITHUB_RELEASES_API = $"https://api.github.com/repos/{GITHUB_REPO_OWNER}/{GITHUB_REPO_NAME}/releases/latest";
        
        // Repositório de releases (mantido para compatibilidade de código interno)
        private const string GITHUB_RELEASES_REPO_NAME = "voltris-releases";
        private const string GITHUB_RELEASES_REPO_API = $"https://api.github.com/repos/{GITHUB_REPO_OWNER}/{GITHUB_RELEASES_REPO_NAME}/releases/latest";
        private const string TRUSTED_UPDATER_SIGNER_THUMBPRINT = "";
        
        // Fallback: tentar version.json do repositório público de releases
        private const string VERSION_JSON_URL = "https://raw.githubusercontent.com/DougFHansen/voltris-releases/main/version.json";
        
        // OPÇÃO: Se o repositório for completamente privado, você pode usar um token de acesso pessoal
        // Crie um token em: https://github.com/settings/tokens (com permissão "public_repo" ou "repo")
        // ATENÇÃO: Não coloque o token diretamente no código! Use uma variável de ambiente ou arquivo de configuração
        // private const string GITHUB_TOKEN = ""; // Deixe vazio se não usar
        
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        
        static UpdateService()
        {
            // Adicionar User-Agent (requerido pela GitHub API)
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "VoltrisOptimizer-Updater/1.0");
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
            
            // Se você tiver um token, descomente e configure:
            // if (!string.IsNullOrEmpty(GITHUB_TOKEN))
            // {
            //     _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("token", GITHUB_TOKEN);
            // }
        }
        
        private static readonly string _updateFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "VoltrisOptimizer", "Updates");
        
        /// <summary>
        /// Obtém a versão atual do aplicativo
        /// </summary>
        public static string GetCurrentVersion()
        {
            try
            {
                // Usar VersionInfo.Version para garantir consistência com o restante da aplicação
                var version = Properties.VersionInfo.Version;
                Debug.WriteLine($"[UpdateService] GetCurrentVersion() retornando: {version} (de VersionInfo.Version)");
                return version;
            }
            catch { }
            
            Debug.WriteLine($"[UpdateService] GetCurrentVersion() usando fallback: 1.0.1.2");
            return "1.0.1.2"; // Versão fallback atualizada
        }
        
        /// <summary>
        /// Verifica se há atualizações disponíveis
        /// </summary>
        public static async Task<UpdateInfo?> CheckForUpdatesAsync()
        {
            GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("UpdateVerifying"), true);
            try
            {
                // Tentar primeiro usar GitHub Releases API (funciona com repositórios privados se o release for público)
                UpdateInfo? updateInfo = await CheckViaGitHubReleasesApiAsync();
                
                // Se falhar, tentar version.json como fallback (para repositórios públicos)
                if (updateInfo == null)
                {
                    updateInfo = await CheckViaVersionJsonAsync();
                }
                
                if (updateInfo != null)
                {
                    var currentVersion = GetCurrentVersion();
                    Debug.WriteLine($"[UpdateService] Versão atual: {currentVersion}");
                    Debug.WriteLine($"[UpdateService] Versão disponível: {updateInfo.LatestVersion}");
                    Debug.WriteLine($"[UpdateService] Comparação: {UpdateInfo.CompareVersions(updateInfo.LatestVersion, currentVersion)}");
                    
                    if (updateInfo.IsNewerThan(currentVersion))
                    {
                        Debug.WriteLine($"[UpdateService] Nova versão detectada!");
                        GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("UpdateNewVersionFound"));
                        return updateInfo;
                    }
                    else
                    {
                        Debug.WriteLine($"[UpdateService] Versão disponível não é mais nova que p atual");
                    }
                }
                else
                {
                    Debug.WriteLine($"[UpdateService] Nenhuma informação de atualização encontrada");
                }
                
                GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("UpdateCheckComplete"));
                return null; // Nenhuma atualização disponível
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateService] Erro ao verificar atualizações: {ex.Message}");
                GlobalProgressService.Instance.FailOperation(string.Format(LocalizationService.Instance.GetString("CommonErrorFormat"), ex.Message));
                return null;
            }
        }
        
        /// <summary>
        /// Verifica atualizações via GitHub Releases API (funciona com repositórios privados)
        /// </summary>
        private static async Task<UpdateInfo?> CheckViaGitHubReleasesApiAsync()
        {
            try
            {
                Debug.WriteLine($"[UpdateService] Tentando acessar: {GITHUB_RELEASES_API}");
                var response = await _httpClient.GetStringAsync(GITHUB_RELEASES_API);
                
                Debug.WriteLine($"[UpdateService] Resposta recebida, tamanho: {response.Length} bytes");
                
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;
                
                // Extrair informações do release
                var tagName = root.GetProperty("tag_name").GetString() ?? "";
                var version = tagName.TrimStart('v', 'V'); // Remove prefixo 'v' se existir
                var body = root.GetProperty("body").GetString() ?? "";
                
                Debug.WriteLine($"[UpdateService] Release encontrado: {tagName} (versão: {version})");
                
                // Buscar o asset do instalador
                var assets = root.GetProperty("assets");
                string? downloadUrl = null;
                
                Debug.WriteLine($"[UpdateService] Procurando assets... ({assets.GetArrayLength()} encontrados)");
                
                string archSuffix = Environment.Is64BitProcess ? "x64" : "x86";
                Debug.WriteLine($"[UpdateService] Arquitetura detectada: {archSuffix}");
                
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    Debug.WriteLine($"[UpdateService] Asset: {name}");
                    
                    // PRIORIDADE MÁXIMA: Buscar o Updater EXE da arquitetura (que tem o ZIP integrado)
                    if (name.Equals($"VoltrisUpdater_{archSuffix}.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = asset.GetProperty("browser_download_url").GetString();
                        Debug.WriteLine($"[UpdateService] Atualizador integrado ({archSuffix}) encontrado: {downloadUrl}");
                        break;
                    }
                }
                
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    Debug.WriteLine("[UpdateService] Nenhum updater executável correspondente foi publicado.");
                    return null;
                }

                var updateInfo = new UpdateInfo
                {
                    LatestVersion = version,
                    DownloadUrl = downloadUrl,
                    Changelog = body,
                    Mandatory = false // Você pode adicionar lógica para determinar se é obrigatório
                };
                
                Debug.WriteLine($"[UpdateService] UpdateInfo criado: Versão={version}, URL={downloadUrl}");
                return updateInfo;
            }
            catch (HttpRequestException httpEx) when (httpEx.Message.Contains("404"))
            {
                Debug.WriteLine($"[UpdateService] Erro 404: Nenhum release encontrado ou repositório não acessível.");
                Debug.WriteLine($"[UpdateService] Possíveis causas:");
                Debug.WriteLine($"[UpdateService] 1. Não há releases criados no GitHub");
                Debug.WriteLine($"[UpdateService] 2. O release não está público (mesmo em repositório privado, o release deve ser público)");
                Debug.WriteLine($"[UpdateService] 3. O repositório não permite acesso público aos releases");
                Debug.WriteLine($"[UpdateService] 4. O release está como 'Draft' ou 'Pre-release'");
                Debug.WriteLine($"[UpdateService] URL tentada: {GITHUB_RELEASES_API}");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateService] Erro ao verificar via GitHub Releases API: {ex.GetType().Name} - {ex.Message}");
                Debug.WriteLine($"[UpdateService] StackTrace: {ex.StackTrace}");
                return null;
            }
        }
        
        /// <summary>
        /// Verifica atualizações via version.json (fallback para repositórios públicos)
        /// </summary>
        private static async Task<UpdateInfo?> CheckViaVersionJsonAsync()
        {
            try
            {
                var response = await _httpClient.GetStringAsync(VERSION_JSON_URL);
                
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true,
                    ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                
                var updateInfo = JsonSerializer.Deserialize<UpdateInfo>(response, options);
                return updateInfo;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateService] Erro ao verificar via version.json: {ex.Message}");
                return null;
            }
        }
        
        private static bool IsAllowedUpdateUrl(string? url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                return false;

            return uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsTrustedUpdaterArtifact(string filePath)
        {
            if (string.IsNullOrWhiteSpace(TRUSTED_UPDATER_SIGNER_THUMBPRINT))
            {
                LogUpdate("Atualização bloqueada: nenhum thumbprint de assinatura confiável está configurado.");
                return false;
            }

            if (!File.Exists(filePath) || !VerifyAuthenticode(filePath))
            {
                LogUpdate("Atualização bloqueada: o updater não possui Authenticode válida.");
                return false;
            }

            try
            {
                using var certificate = X509Certificate.CreateFromSignedFile(filePath);
                return certificate.GetCertHashString().Equals(TRUSTED_UPDATER_SIGNER_THUMBPRINT, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                LogUpdate($"Atualização bloqueada: não foi possível validar o certificado do updater: {ex.Message}");
                return false;
            }
        }

        private static bool VerifyAuthenticode(string filePath)
        {
            var fileInfo = new WINTRUST_FILE_INFO
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
                pcwszFilePath = Marshal.StringToCoTaskMemUni(filePath),
                hFile = IntPtr.Zero,
                pgKnownSubject = IntPtr.Zero
            };
            var fileInfoPointer = IntPtr.Zero;
            var trustDataPointer = IntPtr.Zero;
            var verificationStarted = false;

            try
            {
                fileInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
                Marshal.StructureToPtr(fileInfo, fileInfoPointer, false);
                var trustData = new WINTRUST_DATA
                {
                    cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                    pPolicyCallbackData = IntPtr.Zero,
                    pSIPClientData = IntPtr.Zero,
                    dwUIChoice = 2,
                    fdwRevocationChecks = 0,
                    dwUnionChoice = 1,
                    pFile = fileInfoPointer,
                    dwStateAction = 1,
                    hWVTStateData = IntPtr.Zero,
                    pwszURLReference = IntPtr.Zero,
                    dwProvFlags = 0,
                    dwUIContext = 0
                };
                trustDataPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_DATA>());
                Marshal.StructureToPtr(trustData, trustDataPointer, false);
                verificationStarted = true;
                return WinVerifyTrust(IntPtr.Zero, WinTrustActionGenericVerifyV2, trustDataPointer) == 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (trustDataPointer != IntPtr.Zero)
                {
                    if (verificationStarted)
                    {
                        var closeData = Marshal.PtrToStructure<WINTRUST_DATA>(trustDataPointer);
                        closeData.dwStateAction = 2;
                        Marshal.StructureToPtr(closeData, trustDataPointer, false);
                        WinVerifyTrust(IntPtr.Zero, WinTrustActionGenericVerifyV2, trustDataPointer);
                    }
                    Marshal.FreeHGlobal(trustDataPointer);
                }

                if (fileInfoPointer != IntPtr.Zero)
                {
                    Marshal.DestroyStructure<WINTRUST_FILE_INFO>(fileInfoPointer);
                    Marshal.FreeHGlobal(fileInfoPointer);
                }

                if (fileInfo.pcwszFilePath != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(fileInfo.pcwszFilePath);
                }
            }
        }

        private static readonly Guid WinTrustActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID, IntPtr pWVTData);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            public IntPtr pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WINTRUST_DATA
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
        }

        /// <summary>
        /// Baixa a atualização com progresso
        /// </summary>
        public static async Task<string?> DownloadUpdateAsync(UpdateInfo updateInfo, IProgress<double>? progress = null)
        {
            var globalProgress = ProgressBridge.WrapExisting<double>("Baixando atualização...", progress);
            progress = globalProgress;
            try
            {
                LogUpdate("INICIANDO DOWNLOAD DA ATUALIZAÇÃO...");
                
                // --- DETECÇÃO DE ARQUITETURA BLINDADA ---
                string archSuffix = Environment.Is64BitProcess ? "x64" : "x86";
                string targetName = $"VoltrisUpdater_{archSuffix}.exe";
                string actualUrl = updateInfo.DownloadUrl;

                // Se a URL no JSON não bate com a nossa arquitetura, tentamos corrigir
                if (!actualUrl.Contains(targetName, StringComparison.OrdinalIgnoreCase))
                {
                    LogUpdate($"AVISO: URL do JSON ({actualUrl}) não bate com arquitetura {archSuffix}. Tentando resolver...");
                    string resolvedUrl = await GetUpdaterUrlAsync(updateInfo);
                    if (!string.IsNullOrEmpty(resolvedUrl))
                    {
                        actualUrl = resolvedUrl;
                        LogUpdate($"URL Resolvida dinamicamente: {actualUrl}");
                    }
                    else
                    {
                        LogUpdate("Não foi possível resolver p URL por arquitetura. Tentando URL original do JSON...");
                    }
                }

                if (!IsAllowedUpdateUrl(actualUrl))
                {
                    LogUpdate("Download bloqueado: URL de atualização não pertence p um domínio permitido.");
                    return null;
                }

                TrySecureUpdateDirectory();
                
                var fileName = $"VoltrisOptimizer_Update_{updateInfo.LatestVersion}_{archSuffix}.exe";
                var filePath = Path.Combine(_updateFolder, fileName);
                var tempFilePath = filePath + "." + Guid.NewGuid().ToString("N") + ".download";
                
                LogUpdate($"Baixando de: {actualUrl}");
                LogUpdate($"Destino temporário: {tempFilePath}");

                try
                {
                    using var response = await _httpClient.GetAsync(actualUrl, HttpCompletionOption.ResponseHeadersRead);
                    
                    if (!response.IsSuccessStatusCode)
                    {
                        string error = $"Falha no download (HTTP {(int)response.StatusCode}). Verifique se o arquivo existe no GitHub.";
                        ReportUpdateError("SaaS Update Engine", error);
                        return null;
                    }

                    var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                    using var contentStream = await response.Content.ReadAsStreamAsync();
                    using var fileStream = new FileStream(tempFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, true);
                    
                    var buffer = new byte[8192];
                    long totalBytesRead = 0;
                    int bytesRead;
                    
                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                        totalBytesRead += bytesRead;
                        
                        if (totalBytes > 0)
                        {
                            var percentage = (double)totalBytesRead / totalBytes * 100;
                            progress?.Report(percentage);
                        }
                    }

                    await fileStream.FlushAsync();
                    fileStream.Flush(true);
                    if (!IsTrustedUpdaterArtifact(tempFilePath))
                    {
                        return null;
                    }

                    File.Move(tempFilePath, filePath, overwrite: false);
                    progress?.Report(100);
                    LogUpdate("Download concluído e assinatura validada.");
                    return filePath;
                }
                finally
                {
                    try
                    {
                        if (File.Exists(tempFilePath))
                            File.Delete(tempFilePath);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                string errorMsg = $"ERRO CRÍTICO NO DOWNLOAD: {ex.Message}\n\nURL Tentada: {updateInfo.DownloadUrl}";
                ReportUpdateError("Erro de Download - Voltris", errorMsg);
                return null;
            }
        }
        
        // =====================================================
        // FORENSIC LOGGING SYSTEM
        // =====================================================
        public static void LogUpdate(string message)
        {
            try
            {
                string logDir = Path.Combine(LogDirectoryResolver.Resolve(), "UpdateLogs");
                try
                {
                    if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
                }
                catch
                {
                    logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Voltris_Debug_Logs");
                    if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
                }
                
                string logFile = Path.Combine(logDir, $"update_session_{DateTime.Now:yyyy-MM-dd}.log");
                string logEntry = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
                File.AppendAllText(logFile, logEntry, System.Text.Encoding.UTF8);
                Debug.WriteLine($"[UPDATE_LOG] {message}");
            }
            catch { }
        }

        /// <summary>
        /// Reporta erro da atualização de forma NÃO-BLOQUEANTE.
        /// Nunca usa MessageBox.Show síncrono na UI Thread — isso congelava o app
        /// quando um jogo em tela cheia exclusiva cobria o diálogo modal invisível.
        /// </summary>
        private static void ReportUpdateError(string title, string message)
        {
            LogUpdate($"[ERRO] {title}: {message}");
            NotificationManager.ShowError(title, message);
        }

        private static bool IsPathWithinUpdateFolder(string path)
        {
            try
            {
                var root = Path.GetFullPath(_updateFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var FullPath = Path.GetFullPath(path);
                return FullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static void TrySecureUpdateDirectory()
        {
            try
            {
                Directory.CreateDirectory(_updateFolder);
                var security = new DirectorySecurity();
                security.SetAccessRuleProtection(true, false);
                var inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
                security.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                    FileSystemRights.FullControl,
                    inheritance,
                    PropagationFlags.None,
                    AccessControlType.Allow));
                security.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                    FileSystemRights.FullControl,
                    inheritance,
                    PropagationFlags.None,
                    AccessControlType.Allow));
                new DirectoryInfo(_updateFolder).SetAccessControl(security);
            }
            catch (Exception ex)
            {
                LogUpdate($"Não foi possível proteger o diretório de atualização: {ex.Message}");
            }
        }

        /// <summary>
        /// Baixa o Updater.exe e inicia o processo de atualização automática
        /// ENTERPRISE-LEVEL: Detecção de arquitetura, validação e shutdown controlado
        /// </summary>
        public static async Task<bool> StartAutoUpdateAsync(UpdateInfo updateInfo, string? localFilePath = null)
        {
            try
            {
                LogUpdate("========== INICIANDO PROCESSO DE ATUALIZAÇÃO BLINDADA ==========");
                LogUpdate($"Versão Alvo: {updateInfo.LatestVersion}");
                
                string architecture = Environment.Is64BitProcess ? "x64" : "x86";
                LogUpdate($"Arquitetura detectada: {architecture}");
                
                TrySecureUpdateDirectory();
                var updaterDir = Path.Combine(_updateFolder, "Bin");
                Directory.CreateDirectory(updaterDir);
                
                var updaterExePath = Path.Combine(updaterDir, $"VoltrisUpdater_{architecture}.exe");
                
                if (File.Exists(updaterExePath)) 
                {
                    try { File.Delete(updaterExePath); } catch { }
                }

                if (!string.IsNullOrEmpty(localFilePath) && File.Exists(localFilePath))
                {
                    if (!IsPathWithinUpdateFolder(localFilePath))
                    {
                        LogUpdate("Arquivo local rejeitado: está fora do staging protegido.");
                        return false;
                    }

                    LogUpdate($"Usando arquivo local já baixado: {localFilePath}");
                    File.Copy(localFilePath, updaterExePath, false);
                }
                else
                {
                    if (!IsAllowedUpdateUrl(updateInfo.DownloadUrl))
                    {
                        LogUpdate("Download remoto bloqueado: URL fora dos domínios permitidos.");
                        return false;
                    }

                    LogUpdate("Arquivo local não fornecido, iniciando download...");
                    LogUpdate($"URL de origem: {updateInfo.DownloadUrl}");
                
                    using (var response = await _httpClient.GetAsync(updateInfo.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();
                        using var stream = await response.Content.ReadAsStreamAsync();
                        using var fs = new FileStream(updaterExePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                        await stream.CopyToAsync(fs);
                        await fs.FlushAsync();
                        fs.Flush(true);
                    }
                }

                if (!IsTrustedUpdaterArtifact(updaterExePath))
                {
                    try { File.Delete(updaterExePath); } catch { }
                    return false;
                }
                
                LogUpdate($"Updater pronto: {updaterExePath}");
                LogUpdate($"Tamanho: {new FileInfo(updaterExePath).Length / 1024 / 1024:F2} MB");

                // ETAPA 2: Preparar Argumentos
                var currentExe = Process.GetCurrentProcess().MainModule?.FileName;
                var installPath = string.IsNullOrEmpty(currentExe) 
                    ? AppDomain.CurrentDomain.BaseDirectory 
                    : Path.GetDirectoryName(currentExe);
                
                if (string.IsNullOrEmpty(currentExe))
                {
                    currentExe = Path.Combine(installPath, "VoltrisOptimizer.exe");
                }
                
                // ETAPA 3: Executar Updater (Safe Execution)
                var startInfo = new ProcessStartInfo
                {
                    FileName = updaterExePath,
                    UseShellExecute = true,
                    Verb = "runas", // Forçar elevação para garantir que possa substituir arquivos
                    WorkingDirectory = Path.GetDirectoryName(updaterExePath)
                };

                // Argumentos com aspas reforçadas e changelog
                string currentVersion = GetCurrentVersion();
                string safeChangelog = updateInfo.Changelog?.Replace("\"", "\\\"") ?? "";
                startInfo.Arguments = $"--target \"{installPath.TrimEnd('\\')}\" --exe \"{currentExe}\" --restart-args \"--updated-from {currentVersion}\" --changelog \"{safeChangelog}\" --from-version \"{currentVersion}\"";
                
                LogUpdate($"Lançando Atualizador: {updaterExePath}");
                LogUpdate($"Argumentos: {startInfo.Arguments}");

                try 
                {
                    var process = Process.Start(startInfo);
                    if (process != null)
                    {
                        LogUpdate($"SUCESSO: Atualizador lançado com PID {process.Id}.");
                        // Aguardar um pouco para garantir que o Windows iniciou o processo antes de fechar o pai
                        await Task.Delay(1000);
                        System.Windows.Application.Current.Dispatcher.BeginInvoke(() => System.Windows.Application.Current.Shutdown());
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    string launchError = $"ERRO AO LANÇAR PROCESSO: {ex.Message}";
                    ReportUpdateError("Falha de Execução", launchError);
                }
                
                return false;
            }
            catch (Exception ex)
            {
                LogUpdate($"ERRO GERAL: {ex.Message}");
                return false;
            }
        }
        
        private static async Task<string?> GetUpdaterUrlAsync(UpdateInfo updateInfo)
        {
            try
            {
                LogUpdate("Iniciando busca do Updater no GitHub...");
                
                string archSuffix = Environment.Is64BitProcess ? "x64" : "x86";
                string targetName = $"VoltrisUpdater_{archSuffix}.exe";
                
                // Tenta buscar no repo de releases
                LogUpdate($"Procurando {targetName} em voltris-releases...");
                
                var response = await _httpClient.GetStringAsync(GITHUB_RELEASES_REPO_API);
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                if (root.TryGetProperty("assets", out var assets))
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        string name = asset.GetProperty("name").GetString() ?? "";
                        if (name.Equals(targetName, StringComparison.OrdinalIgnoreCase))
                        {
                            var url = asset.GetProperty("browser_download_url").GetString();
                            return IsAllowedUpdateUrl(url) ? url : null;
                        }
                    }
                }

                LogUpdate("Nenhum updater correspondente encontrado no release mais recente.");
                return null;
            }
            catch (Exception ex)
            {
                string error = $"Erro ao obter URL: {ex.Message}";
                ReportUpdateError("Update Debug", error);
                return null;
            }
        }
        
        /// <summary>
        /// Obtém o caminho de instalação do registro
        /// </summary>
        private static string? GetInstallPath()
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
                using var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\VoltrisOptimizer");
                return key?.GetValue("InstallLocation") as string;
            }
            catch
            {
                return null;
            }
        }
        
        /// <summary>
        /// Aplica a atualização - substitui o executável e reinicia o app (MÉTODO LEGADO - usar StartAutoUpdateAsync)
        /// </summary>
        [Obsolete("Use StartAutoUpdateAsync instead")]
        public static void ApplyUpdateAndRestart(string newExePath)
        {
            throw new NotSupportedException("O método legado de atualização foi desativado.");
        }
        
        /// <summary>
        /// Limpa arquivos de atualização antigos
        /// </summary>
        public static void CleanupOldUpdates()
        {
            try
            {
                if (!Directory.Exists(_updateFolder))
                    return;
                
                foreach (var file in Directory.GetFiles(_updateFolder))
                {
                    try
                    {
                        var info = new FileInfo(file);
                        if (info.LastWriteTime < DateTime.Now.AddDays(-7))
                        {
                            File.Delete(file);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
