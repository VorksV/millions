using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Drivers
{
    /// <summary>
    /// Download seguro e VALIDADO de pacotes de driver.
    ///
    /// CAUSA RAIZ CORRIGIDA (FASE 1/2):
    /// A implementação anterior devolvia a string literal "MOCK_SUCCESS" para qualquer URL de
    /// intel.com / nvidia.com / amd.com / realtek.com / driver.com, e o sistema tratava isso como
    /// "instalado com sucesso". Na prática NENHUM driver era baixado: o usuário era informado de
    /// um sucesso inexistente. Além disso o caminho "real" só sabia extrair .zip, o que nunca
    /// ocorre com pacotes de fabricante.
    ///
    /// Este resolvedor:
    ///  - Baixa de verdade, com progresso, timeout, retry e cancelamento;
    ///  - Escreve em .part e só renomeia após conclusão (nunca deixa arquivo incompleto);
    ///  - Calcula e confere SHA-256 quando o fabricante publica o hash;
    ///  - Valida Assinatura Digital (Authenticode) via WinVerifyTrust;
    ///  - Valida o PUBLICADOR contra a lista de fabricantes reais;
    ///  - Extrai INF de .zip e .cab (formatos que realmente contêm descritores de driver);
    ///  - NUNCA executa o pacote e NUNCA contorna assinatura de driver.
    /// </summary>
    public sealed class SecureDriverDownloader : IDisposable
    {
        private const int MaxRetries = 3;
        private static readonly TimeSpan PerAttemptTimeout = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan TotalTimeout = TimeSpan.FromMinutes(20);
        private const long MinimumPlausibleSizeBytes = 8 * 1024; // Abaixo disso não é um pacote de driver

        private readonly HttpClient _client;
        private bool _disposed;

        /// <summary>
        /// Publicadores aceitáveis. Baseado em certificado real de cada fabricante; a comparação é
        /// por substring do nome distinguished do certificado, o que é exatamente o que o Windows
        /// apresenta em "Assinado por". Barrar publicadores desconhecidos é o que impede que um
        /// arquivo malicioso se passe por driver.
        /// </summary>
        private static readonly string[] AllowedPublisherTokens =
        {
            "Microsoft Corporation",
            "Microsoft Windows",
            "Intel Corporation",
            "NVIDIA Corporation",
            "Advanced Micro Devices",
            "AMD",
            "Realtek Semiconductor",
            "Realtek",
            "Broadcom Inc.",
            "Qualcomm",
            "MediaTek",
            "Marvell Technology",
            "Dell Inc.",
            "Hewlett-Packard",
            "Lenovo",
            "ASUSTeK",
            "Micro-Star International",
            "Hewlett Packard Enterprise",
            "Neta Systems",
            "Kingston Technology",
            "Seagate Technology",
            "Western Digital",
            "JMicron Technology",
            "Logitech",
            "Razer",
            "Sonos",
            "SAMSUNG",
        };

        public SecureDriverDownloader()
        {
            var handler = new HttpClientHandler
            {
                // Validação TLS ESTRITA. Nunca aceitar certificado inválido — a callback exige
                // cadeia válida E ausência de erros de política, o que é o padrão correto.
                ServerCertificateCustomValidationCallback = ValidateServerCertificate,
                AutomaticDecompression = DecompressionMethods.All,
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 8
            };

            _client = new HttpClient(handler)
            {
                Timeout = PerAttemptTimeout
            };
            _client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) VoltrisOptimizer/1.0");

            App.LoggingService?.LogInfo("[DriverDownload] Cliente de download configurado com TLS estrito e descompressao habilitada.");
        }

        private static bool ValidateServerCertificate(HttpRequestMessage request, X509Certificate2? certificate, X509Chain? chain, SslPolicyErrors errors)
        {
            bool valid = errors == SslPolicyErrors.None && chain != null && chain.Build(certificate);
            if (!valid)
            {
                App.LoggingService?.LogWarning(
                    $"[DriverDownload] Certificado TLS rejeitado para {request.RequestUri?.Host}: erros={errors} | " +
                    $"assunto={certificate?.Subject}");
            }
            return valid;
        }

        /// <summary>
        /// Baixa e valida um pacote de driver.
        /// </summary>
        /// <param name="url">URL direta do pacote.</param>
        /// <param name="expectedSha256">Hash anunciado pelo fabricante (vazio se não publicado).</param>
        /// <param name="version">Versão do driver, registrada no log.</param>
        /// <param name="vendor">Fabricante, para o log de auditoria.</param>
        /// <param name="expectedSizeBytes">Tamanho anunciado, quando conhecido (0 = desconhecido).</param>
        /// <param name="progress">Callback de progresso (0..1, bytes recebidos, total).</param>
        /// <param name="token">Cancelamento.</param>
        public async Task<DriverDownloadResult> DownloadDriverAsync(
            string? url,
            string? expectedSha256,
            string? version,
            string? vendor,
            long expectedSizeBytes = 0,
            IProgress<(double Fraction, long Received, long Total)>? progress = null,
            CancellationToken token = default)
        {
            var totalWatch = Stopwatch.StartNew();

            using var ctsTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            ctsTimeout.CancelAfter(TotalTimeout);

            using var op = new DriverOperationScope("DRIVER_DOWNLOAD", $"{vendor ?? "?"} v{version ?? "?"}");

            if (string.IsNullOrWhiteSpace(url) ||
                !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                op.Fail("URL de download ausente ou inválida");
                return DriverDownloadResult.Fail(DriverFailureReason.InvalidUrl, totalWatch.Elapsed);
            }

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                op.Fail($"URL não é HTTPS: {uri}");
                return DriverDownloadResult.Fail(DriverFailureReason.InvalidUrl, totalWatch.Elapsed);
            }

            string tempDir = string.Empty;
            try
            {
                tempDir = CreateStagingDirectory();
                string stagingPath = Path.Combine(tempDir, SanitizeFileName(Path.GetFileName(uri.LocalPath)) is { Length: > 0 } leaf
                    ? leaf
                    : "driver-package.bin");

                op.Stage("INICIO", $"origem={uri.Host} | arquivo={Path.GetFileName(stagingPath)} | tamanhoAnunciado={(expectedSizeBytes > 0 ? expectedSizeBytes.ToString() : "desconhecido")}");

                long downloaded = 0;
                DriverFailureReason lastFailure = DriverFailureReason.NetworkFailure;
                Exception? lastException = null;

                for (int attempt = 1; attempt <= MaxRetries; attempt++)
                {
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        ctsTimeout.Token.ThrowIfCancellationRequested();

                        using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ctsTimeout.Token, token);
                        attemptCts.CancelAfter(PerAttemptTimeout);

                        op.Stage("TENTATIVA", $"{attempt}/{MaxRetries}");

                        downloaded = await DownloadOnceAsync(uri, stagingPath, progress, op, attemptCts.Token).ConfigureAwait(false);

                        // O .part só é renomeado aqui: até este ponto um arquivo incompleto
                        // NUNCA fica no disco com o nome final.
                        string finalPath = Path.Combine(tempDir, "package" + Path.GetExtension(stagingPath));
                        File.Move(stagingPath, finalPath, overwrite: true);
                        stagingPath = finalPath;

                        op.Stage("DOWNLOAD", $"concluido | bytes={downloaded} | origem={uri.Host}");

                        // Validações de integridade
                        var sizeCheck = ValidateSize(downloaded, expectedSizeBytes, op);
                        if (sizeCheck != DriverFailureReason.None)
                            return DriverDownloadResult.Fail(sizeCheck, totalWatch.Elapsed);

                        string sha256 = await ComputeSha256Async(finalPath, token).ConfigureAwait(false);
                        op.Stage("HASH", $"sha256={sha256}");

                        var hashCheck = ValidateHash(sha256, expectedSha256, op);
                        if (hashCheck != DriverFailureReason.None)
                            return DriverDownloadResult.Fail(hashCheck, totalWatch.Elapsed);

                        // Validações de assinatura e publicador
                        var signature = new DriverSignatureVerifier();
                        var signatureReport = signature.VerifyWithPublisher(finalPath);
                        if (!signatureReport.IsSigned)
                        {
                            op.StageFailed("ASSINATURA", $"pacote sem assinatura válida: {signatureReport.Summary}", null);
                            return DriverDownloadResult.Fail(DriverFailureReason.SignatureMissing, totalWatch.Elapsed);
                        }
                        if (!signatureReport.IsTrustedPublisher)
                        {
                            op.StageFailed("PUBLICADOR", $"assinatura válida mas publicador não reconhecido: {signatureReport.Publisher}", null);
                            return DriverDownloadResult.Fail(DriverFailureReason.PublisherNotAllowed, totalWatch.Elapsed);
                        }
                        op.Stage("ASSINATURA", $"válida | assinante={signatureReport.SignerSubject} | publicador={signatureReport.Publisher}");

                        // Extração e descoberta de INF
                        var kind = DetectPackageKind(finalPath);
                        string? extractedFolder = null;
                        var infFiles = new List<string>();

                        switch (kind)
                        {
                            case DriverPackageKind.Zip:
                                extractedFolder = Path.Combine(tempDir, "extracted");
                                Directory.CreateDirectory(extractedFolder);
                                ZipFile.ExtractToDirectory(finalPath, extractedFolder, overwriteFiles: true);
                                infFiles = SafeInfFiles(extractedFolder);
                                op.Stage("EXTRACAO", $"zip | infs={infFiles.Count}");
                                break;

                            case DriverPackageKind.Cab:
                                extractedFolder = Path.Combine(tempDir, "extracted");
                                Directory.CreateDirectory(extractedFolder);
                                if (!await ExpandCabAsync(finalPath, extractedFolder, op, token).ConfigureAwait(false))
                                {
                                    CleanupDirectory(tempDir);
                                    return DriverDownloadResult.Fail(DriverFailureReason.UnsupportedPackageFormat, totalWatch.Elapsed);
                                }
                                infFiles = SafeInfFiles(extractedFolder);
                                op.Stage("EXTRACAO", $"cab | infs={infFiles.Count}");
                                break;

                            case DriverPackageKind.VendorInstaller:
                                // Não executamos instaladores de fabricante automaticamente.
                                // Eles ficam disponíveis para ação manual explícita do usuário.
                                op.Stage("EXTRACAO", "instalador de fabricante (.exe/.msi) — execução automática bloqueada por segurança");
                                break;

                            default:
                                op.StageFailed("EXTRACAO", $"formato não reconhecido: {Path.GetFileName(finalPath)}", null);
                                CleanupDirectory(tempDir);
                                return DriverDownloadResult.Fail(DriverFailureReason.UnsupportedPackageFormat, totalWatch.Elapsed);
                        }

                        if (kind is DriverPackageKind.Zip or DriverPackageKind.Cab && infFiles.Count == 0)
                        {
                            op.StageFailed("PAYLOAD", "nenhum .INF encontrado no pacote extraído", null);
                            CleanupDirectory(tempDir);
                            return DriverDownloadResult.Fail(DriverFailureReason.NoDriverPayload, totalWatch.Elapsed);
                        }

                        op.Succeed($"pacote validado | host={uri.Host} | bytes={downloaded} | tipo={kind} | infs={infFiles.Count} | sha256={sha256[..Math.Min(16, sha256.Length)]}...");

                        return DriverDownloadResult.Ok(finalPath, extractedFolder, infFiles, kind, downloaded,
                            sha256, uri.Host, uri.ToString(), signatureReport.SignerSubject, signatureReport.Publisher, totalWatch.Elapsed);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        op.Fail("cancelado pelo usuário");
                        CleanupDirectory(tempDir);
                        return DriverDownloadResult.Fail(DriverFailureReason.Cancelled, totalWatch.Elapsed);
                    }
                    catch (OperationCanceledException)
                    {
                        lastFailure = DriverFailureReason.NetworkTimeout;
                        lastException = null;
                        op.StageFailed("TIMEOUT", $"tentativa {attempt} excedeu {PerAttemptTimeout.TotalMinutes:0}min", null);
                    }
                    catch (HttpRequestException ex)
                    {
                        lastFailure = DriverFailureReason.NetworkFailure;
                        lastException = ex;
                        op.StageFailed("REDE", $"tentativa {attempt}: {ex.Message}", ex);
                    }
                    catch (IOException ex)
                    {
                        lastFailure = DriverFailureReason.IncompleteDownload;
                        lastException = ex;
                        op.StageFailed("DISCO", $"tentativa {attempt}: {ex.Message}", ex);
                    }

                    if (attempt < MaxRetries)
                    {
                        int backoffMs = 1500 * attempt;
                        op.Trace($"aguardando {backoffMs}ms antes da próxima tentativa");
                        try { await Task.Delay(backoffMs, token).ConfigureAwait(false); }
                        catch (OperationCanceledException) { break; }
                    }
                }

                op.Fail($"todas as {MaxRetries} tentativas falharam");
                return DriverDownloadResult.Fail(lastFailure, totalWatch.Elapsed);
            }
            catch (OperationCanceledException)
            {
                op.Fail("cancelado");
                CleanupDirectory(tempDir);
                return DriverDownloadResult.Fail(DriverFailureReason.Cancelled, totalWatch.Elapsed);
            }
            catch (Exception ex)
            {
                op.Fail($"erro inesperado: {ex.GetType().Name}", ex);
                CleanupDirectory(tempDir);
                return DriverDownloadResult.Fail(DriverFailureReason.NetworkFailure, totalWatch.Elapsed);
            }
        }

        private async Task<long> DownloadOnceAsync(Uri uri, string destinationPath,
            IProgress<(double, long, long)>? progress, DriverOperationScope op, CancellationToken token)
        {
            // .part: garante que um cancelamento/queda nunca deixe um arquivo com nome final
            // incompleto em disco.
            string partPath = destinationPath + ".part";

            using var response = await _client
                .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"HTTP {(int)response.StatusCode} ({response.ReasonPhrase}) ao acessar {uri.Host}",
                    null, response.StatusCode);
            }

            long total = response.Content.Headers.ContentLength ?? -1L;
            if (total > 0 && expectedSizeGuard(total))
            {
                throw new HttpRequestException($"Tamanho anunciado inválido: {total} bytes");
            }

            await using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            await using var destination = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true);

            var buffer = new byte[128 * 1024];
            long received = 0;
            int read;
            var lastReport = Stopwatch.StartNew();

            while ((read = await source.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                received += read;

                if (total > 0 && received > total)
                    throw new IOException($"Download excedeu o Content-Length anunciado ({received} > {total}).");

                // Progresso no máximo ~10x/segundo: suficiente para a UI e sem inundar o Dispatcher.
                if (progress != null && (lastReport.ElapsedMilliseconds >= 100 || (total > 0 && received == total)))
                {
                    lastReport.Restart();
                    double fraction = total > 0 ? Math.Clamp((double)received / total, 0, 1) : 0;
                    progress.Report((fraction, received, total));
                }
            }

            await destination.FlushAsync(token).ConfigureAwait(false);
            destination.Close();
            await Task.CompletedTask;

            if (received < MinimumPlausibleSizeBytes)
            {
                SafeDelete(partPath);
                throw new IOException($"Arquivo baixado tem apenas {received} bytes — pacote inválido/incompleto.");
            }

            if (total > 0 && received != total)
            {
                SafeDelete(partPath);
                throw new IOException($"Download incompleto: {received} de {total} bytes.");
            }

            File.Move(partPath, destinationPath, overwrite: true);
            op.Trace($"recebidos {received} bytes");
            return received;

            static bool expectedSizeGuard(long value) => value <= 0;
        }

        private static DriverFailureReason ValidateSize(long actual, long expected, DriverOperationScope op)
        {
            if (expected <= 0)
            {
                op.Trace("tamanho não anunciado pelo fabricante — validação de tamanho ignorada");
                return DriverFailureReason.None;
            }

            // Tolerância de 1% para diferenças de empacotamento do CDN.
            long tolerance = Math.Max(1024, expected / 100);
            if (Math.Abs(actual - expected) > tolerance)
            {
                op.StageFailed("TAMANHO", $"recebido={actual} anunciado={expected} tolerancia={tolerance}", null);
                return DriverFailureReason.SizeMismatch;
            }
            return DriverFailureReason.None;
        }

        private static DriverFailureReason ValidateHash(string actual, string? expected, DriverOperationScope op)
        {
            if (string.IsNullOrWhiteSpace(expected))
            {
                op.Trace("hash não publicado pelo fabricante — conferência de hash ignorada (assinatura continua obrigatória)");
                return DriverFailureReason.None;
            }

            if (!actual.Equals(expected.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                op.StageFailed("HASH", $"recebido={actual} esperado={expected}", null);
                return DriverFailureReason.HashMismatch;
            }
            return DriverFailureReason.None;
        }

        private static async Task<string> ComputeSha256Async(string path, CancellationToken token)
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, useAsync: true);
            using var sha = SHA256.Create();
            var hash = await sha.ComputeHashAsync(stream, token).ConfigureAwait(false);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static DriverPackageKind DetectPackageKind(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            switch (ext)
            {
                case ".zip": return DriverPackageKind.Zip;
                case ".cab": return DriverPackageKind.Cab;
                case ".exe":
                case ".msi": return DriverPackageKind.VendorInstaller;
            }

            // Fallback por assinatura binária (CD0082 para ZIP, MSCF para CAB, MZ para PE).
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                Span<byte> header = stackalloc byte[4];
                int read = fs.Read(header);
                if (read >= 4)
                {
                    if (header[0] == 0x50 && header[1] == 0x4B) return DriverPackageKind.Zip;
                    if (header[0] == 0x4D && header[1] == 0x53 && header[2] == 0x43 && header[3] == 0x46) return DriverPackageKind.Cab;
                    if (header[0] == 0x4D && header[1] == 0x5A) return DriverPackageKind.VendorInstaller;
                }
            }
            catch (IOException ex)
            {
                App.LoggingService?.LogWarning($"[DriverDownload] Não foi possível ler a assinatura do arquivo: {ex.Message}");
            }
            return DriverPackageKind.Unknown;
        }

        private static async Task<bool> ExpandCabAsync(string cabPath, string targetFolder, DriverOperationScope op, CancellationToken token)
        {
            // expand.exe é a ferramenta nativa do Windows para cabines — sem dependência externa.
            var psi = new ProcessStartInfo
            {
                FileName = "expand.exe",
                Arguments = $"-F:\"*\" \"{cabPath}\" \"{Path.Combine(targetFolder, "payload")}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            Directory.CreateDirectory(Path.Combine(targetFolder, "payload"));

            try
            {
                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    op.StageFailed("CAB", "expand.exe não pôde ser iniciado", null);
                    return false;
                }

                // Ler stdout e stderr em paralelo: um único await pode bloquear se o outro
                // buffer encher (deadlock clássico de redirecionamento de processo).
                var stdoutTask = proc.StandardOutput.ReadToEndAsync();
                var stderrTask = proc.StandardError.ReadToEndAsync();

                using (token.Register(() => { try { if (!proc.HasExited) proc.Kill(true); } catch { } }))
                {
                    await proc.WaitForExitAsync().ConfigureAwait(false);
                }

                string stdout = await stdoutTask.ConfigureAwait(false);
                string stderr = await stderrTask.ConfigureAwait(false);

                if (proc.ExitCode != 0)
                {
                    op.StageFailed("CAB", $"expand.exe saiu com {proc.ExitCode}: {stderr.Trim()} | {stdout.Trim()}", null);
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                op.StageFailed("CAB", $"falha ao expandir: {ex.Message}", ex);
                return false;
            }
        }

        private static List<string> SafeInfFiles(string folder)
        {
            try
            {
                return Directory.GetFiles(folder, "*.inf", SearchOption.AllDirectories)
                                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                                .ToList();
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[DriverDownload] Falha ao listar INF em '{folder}': {ex.Message}");
                return new List<string>();
            }
        }

        private static bool IsAllowedPublisher(string? publisher)
        {
            if (string.IsNullOrWhiteSpace(publisher)) return false;
            return AllowedPublisherTokens.Any(t => publisher.Contains(t, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Verifica se um publicador é aceito. Público paraallow que a camada de UI e os testes
        /// consultem a mesma política usada no download.
        /// </summary>
        public static bool IsPublisherAllowed(string? publisher) => IsAllowedPublisher(publisher);

        private static string CreateStagingDirectory()
        {
            string dir = Path.Combine(Path.GetTempPath(), "VoltrisDriverUpdater", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            foreach (var invalid in Path.GetInvalidFileNameChars())
                name = name.Replace(invalid, '_');
            return name.Length > 120 ? name[..120] : name;
        }

        private static void SafeDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort */ }
        }

        private static void CleanupDirectory(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return;
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[DriverDownload] Não foi possível limpar '{dir}': {ex.Message}");
            }
        }

        /// <summary>Remove uma pasta de staging após a instalação (ou cancelamento).</summary>
        public static void ReleaseStagingFolder(string? folder) => CleanupDirectory(folder ?? string.Empty);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _client.Dispose();
        }
    }
}
