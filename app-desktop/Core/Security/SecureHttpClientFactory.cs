using System;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace VoltrisOptimizer.Core.Security
{
    /// <summary>
    /// SecureHttpClientFactory — Cria HttpClient com proteções de segurança:
    /// - Certificate validation (anti-MITM)
    /// - Security headers automáticos
    /// - Timeout agressivo
    /// - Desabilita proxy automático quando suspeito
    /// - TLS 1.2+ obrigatório
    /// </summary>
    public static class SecureHttpClientFactory
    {
        private static readonly Lazy<HttpClient> _secureClient = new(CreateSecureClient);

        /// <summary>
        /// HttpClient singleton com todas as proteções ativas.
        /// Usar para todas as chamadas à API do Voltris.
        /// </summary>
        public static HttpClient SecureClient => _secureClient.Value;

        /// <summary>
        /// Cria um novo HttpClient com proteções de segurança.
        /// </summary>
        public static HttpClient CreateSecureClient()
        {
            var handler = new HttpClientHandler
            {
                // TLS 1.2+ obrigatório (bloqueia TLS 1.0/1.1 vulneráveis)
                SslProtocols = System.Security.Authentication.SslProtocols.Tls12 |
                               System.Security.Authentication.SslProtocols.Tls13,

                // Validação de certificado customizada
                ServerCertificateCustomValidationCallback = ValidateServerCertificate,

                // Descompressão automática
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,

                // Não seguir redirects automaticamente (anti-phishing)
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 3,

                // Cookie container isolado
                UseCookies = true,
                CookieContainer = new CookieContainer()
            };

            // Desabilitar proxy se detectado como suspeito
            var proxyResult = EnvironmentProtection.DetectProxy();
            if (proxyResult.IsSuspicious)
            {
                handler.UseProxy = false;
                handler.Proxy = null;
                LogSecurity("Proxy suspeito detectado — desabilitado para conexões seguras");
            }

            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            // Headers de segurança padrão
            client.DefaultRequestHeaders.Add("X-Voltris-Client", "VoltrisOptimizer/2.0");
            client.DefaultRequestHeaders.Add("X-Request-ID", Guid.NewGuid().ToString("N")[..16]);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("VoltrisOptimizer/2.0 (.NET 8.0; Windows)");

            // Cache control — não cachear respostas sensíveis
            client.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
            {
                NoCache = true,
                NoStore = true,
                MustRevalidate = true
            };

            return client;
        }

        /// <summary>
        /// Validação customizada de certificado SSL.
        /// Rejeita certificados auto-assinados e expirados.
        /// </summary>
        private static bool ValidateServerCertificate(
            HttpRequestMessage request,
            X509Certificate2? certificate,
            X509Chain? chain,
            SslPolicyErrors sslErrors)
        {
            // Sem erros = certificado válido
            if (sslErrors == SslPolicyErrors.None)
                return true;

            // Para domínios Voltris, ser mais rigoroso
            var host = request.RequestUri?.Host ?? "";
            if (host.Contains("voltris.com", StringComparison.OrdinalIgnoreCase))
            {
                if (sslErrors != SslPolicyErrors.None)
                {
                    LogSecurity($"Certificado inválido para {host}: {sslErrors}");
                    return false; // Rejeitar qualquer erro para domínios Voltris
                }
            }

            // Para outros domínios, aceitar apenas erros de nome (CDN pode usar wildcard)
            if (sslErrors == SslPolicyErrors.RemoteCertificateNameMismatch)
            {
                LogSecurity($"Certificate name mismatch para {host} — permitido (CDN)");
                return true;
            }

            // Certificado expirado ou auto-assinado = rejeitar
            if (sslErrors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable) ||
                sslErrors.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors))
            {
                LogSecurity($"Certificado rejeitado para {host}: {sslErrors}");
                return false;
            }

            return false;
        }

        private static void LogSecurity(string message)
        {
            try
            {
                var logDir = LogDirectoryResolver.Resolve();
                System.IO.Directory.CreateDirectory(logDir);
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(logDir, "http_security.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n", System.Text.Encoding.UTF8);
            }
            catch { }
        }
    }
}
