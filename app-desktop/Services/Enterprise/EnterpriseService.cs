using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Cloud;
using VoltrisOptimizer.Core.Security;

namespace VoltrisOptimizer.Services.Enterprise
{
    /// <summary>
    /// Resultado da consulta de estado de vinculo.
    ///
    /// Distingue "nao vinculado" de "nao deu para consultar". Sem essa distincao
    /// o app apagava o estado local sempre que a rede falhava, e o usuario via
    /// "desvinculado" sem ninguem ter desvinculado.
    /// </summary>
    public enum LinkCheckOutcome
    {
        /// <summary>O servidor respondeu e a maquina NAO esta vinculada.</summary>
        NotLinked,

        /// <summary>O servidor respondeu e a maquina ESTA vinculada.</summary>
        Linked,

        /// <summary>A maquina nao existe no servidor ainda.</summary>
        NotRegistered,

        /// <summary>Falha de rede, timeout, HTTP 5xx: estado local deve ser mantido.</summary>
        Unreachable,

        /// <summary>
        /// O servidor tem um vinculo, mas a credencial deste PC nao bate.
        /// O app esta desatualizado ou a credencial foi perdida: precisa
        /// desvincular/revincular pela conta. O estado local NAO deve ser
        /// apagado (o vinculo continua valendo no site).
        /// </summary>
        CredentialMismatch
    }

    public sealed class LinkStatusResult
    {
        public LinkCheckOutcome Outcome { get; init; } = LinkCheckOutcome.Unreachable;
        public string? Email { get; init; }
        public string? CorrelationId { get; init; }
        public int? HttpStatus { get; init; }
        public string? Error { get; init; }

        /// <summary>True quando o servidor disse, sem ambiguidade, que nao ha vinculo.</summary>
        public bool IsConclusiveNotLinked => Outcome == LinkCheckOutcome.NotLinked;

        /// <summary>True quando o servidor disse, sem ambiguidade, que ha vinculo.</summary>
        public bool IsConclusiveLinked => Outcome == LinkCheckOutcome.Linked;

        public bool IsLinked => Outcome == LinkCheckOutcome.Linked;
    }

    /// <summary>
    /// EnterpriseService - comunica com o backend para vinculo de conta e licenca.
    /// </summary>
    public class EnterpriseService
    {
        private static EnterpriseService? _instance;
        public static EnterpriseService Instance => _instance ??= new EnterpriseService();

        private readonly ILoggingService? _logger;
        private const string ApiBaseUrl = "https://www.voltris.com.br/api/v1";

        public EnterpriseService()
        {
            _instance = this;
            _logger = null;
        }

        public EnterpriseService(
            object identityService,
            object monitorService,
            object intelligenceOrchestrator,
            ILoggingService? logger = null)
        {
            _instance = this;
            _logger = logger;
        }

        public Task InitializeAsync() => Task.CompletedTask;
        public void StartBackgroundServices() { }
        public Task StopBackgroundServicesAsync() => Task.CompletedTask;

        public Task SyncLicenseStatusAsync() => Task.CompletedTask;

        /// <summary>
        /// Desvinculacao: notifica o backend e so limpa o estado local depois de
        /// confirmacao. O installation_id vai no BODY e a autorizacao vai no
        /// header de credencial de dispositivo (o app nao tem sessao de cookie).
        /// </summary>
        public async Task<bool> UnlinkThisDeviceAsync()
        {
            var settings = SettingsService.Instance.Settings;
            var installationId = settings.InstallationId;

            if (string.IsNullOrWhiteSpace(installationId))
            {
                settings.IsDeviceLinked = false;
                settings.LinkedUserEmail = null;
                settings.LinkedAt = null;
                SettingsService.Instance.SaveSettings();
                return true;
            }

            try
            {
                using var client = SecureHttpClientFactory.CreateSecureClient();
                var payload = JsonSerializer.Serialize(
                    new { installation_id = installationId },
                    App.ApiJsonOptions);

                var url = $"{ApiBaseUrl}/install/unlink";
                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
                };
                request.Headers.TryAddWithoutValidation("x-correlation-id", CorrelationId.New("VOLTRIS-UNLINK"));
                AttachCredential(request);

                using var response = await client.SendAsync(request).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    // A credencial vale para UM vinculo: descarta local.
                    DeviceCredentialStore.Clear();

                    _logger?.LogInfo($"[ENTERPRISE] Desvinculacao confirmada no backend: {installationId}");

                    settings.IsDeviceLinked = false;
                    settings.LinkedUserEmail = null;
                    settings.LinkedAt = null;
                    SettingsService.Instance.SaveSettings();
                    return true;
                }

                _logger?.LogWarning(
                    $"[ENTERPRISE] Backend recusou a desvinculacao: HTTP {(int)response.StatusCode} - {Truncate(body)}");

                // O estado local NAO e alterado: se o servidor recusou, o vinculo
                // continua valendo. Mentir aqui gerava o inverso do bug anterior.
                return false;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[ENTERPRISE] Falha de rede ao desvincular: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Registra a credencial DESTE dispositivo no servidor.
        ///
        /// O token é gerado aqui e guardado com DPAPI; o servidor recebe apenas
        /// o token e guarda o SHA-256. Nenhum segredo trafega servidor -&gt; app.
        ///
        /// Precisa rodar antes de abrir o navegador de vínculo, e é idempotente:
        /// se já estiver registrada com o mesmo token, o servidor devolve 200.
        /// </summary>
        /// <summary>
        /// BUG CORRIGIDO: o servidor responde 409 CREDENTIAL_ALREADY_REGISTERED para
        /// sempre neste dispositivo, porque a credencial gravada veio de um build
        /// antigo. O metodo devolvia false e logava WARNING, e o chamador repetia
        /// — o log do app mostrava o mesmo 409 a cada ~31s, gastando chamada de
        /// rede sem nunca conseguir o 200 que o comentario abaixo promete.
        ///
        /// Agora o 409 e reconhecido pelo <c>code</c> do corpo e tratado como
        /// ESTADO TERMINAL valido (nao e falha: o /install/link rotaciona o hash
        /// depois). E um flag impede nova tentativa enquanto o processo viver.
        /// </summary>
        private static int _credentialAlreadyRegistered;

        public static async Task<bool> RegisterDeviceCredentialAsync(string? installationId = null)
        {
            // Ja sabemos que o servidor tem uma credencial para este dispositivo.
            // Nao ha o que fazer ate o usuario revincular a conta.
            if (Interlocked.CompareExchange(ref _credentialAlreadyRegistered, 1, 1) == 1)
            {
                return true;
            }

            var deviceId = string.IsNullOrWhiteSpace(installationId)
                ? SettingsService.Instance.Settings.InstallationId
                : installationId;

            if (string.IsNullOrWhiteSpace(deviceId))
                return false;

            var token = DeviceCredentialStore.Current ?? DeviceCredentialStore.CreateNew();
            if (string.IsNullOrEmpty(token))
            {
                App.LoggingService?.LogWarning("[ENTERPRISE] Nao foi possivel gerar a credencial do dispositivo.");
                return false;
            }

            try
            {
                using var client = SecureHttpClientFactory.CreateSecureClient();
                var payload = JsonSerializer.Serialize(
                    new { installation_id = deviceId, device_credential = token },
                    App.ApiJsonOptions);

                using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/install/credential")
                {
                    Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
                };
                request.Headers.TryAddWithoutValidation("x-correlation-id", CorrelationId.New("VOLTRIS-CRED"));

                using var response = await client.SendAsync(request).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    App.LoggingService?.LogInfo("[ENTERPRISE] Credencial do dispositivo registrada.");
                    return true;
                }

                // 409 = o servidor JA tem credencial para este dispositivo (gravada
                // por um build antigo, que emitia o token para o navegador e o
                // navegador nunca lia). Nao e falha deste app: o /install/link
                // rotaciona o hash e o proximo poll de /install/status registra o
                // token gerado aqui. E terminal, entao e marcado como tal para nao
                // repetir a chamada a cada polling.
                if ((int)response.StatusCode == 409
                    && body.Contains("CREDENTIAL_ALREADY_REGISTERED", StringComparison.Ordinal))
                {
                    Interlocked.Exchange(ref _credentialAlreadyRegistered, 1);
                    App.LoggingService?.LogInfo(
                        "[ENTERPRISE] Credencial ja consta no servidor (409 CREDENTIAL_ALREADY_REGISTERED). " +
                        "Tratado como registrado — novas tentativas suprimidas nesta sessao. " +
                        $"Resposta: {Truncate(body)}");
                    return true;
                }

                App.LoggingService?.LogWarning(
                    $"[ENTERPRISE] Registro de credencial: HTTP {(int)response.StatusCode} - {Truncate(body)}");
                return false;
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[ENTERPRISE] Falha ao registrar credencial: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Garante que o dispositivo tenha credencial local E registrada.
        /// Usado na inicialização e antes de abrir o fluxo de vínculo.
        /// </summary>
        public static async Task EnsureDeviceCredentialAsync(string? installationId = null)
        {
            var deviceId = string.IsNullOrWhiteSpace(installationId)
                ? SettingsService.Instance.Settings.InstallationId
                : installationId;

            if (string.IsNullOrWhiteSpace(deviceId)) return;

            if (!DeviceCredentialStore.HasCredential)
            {
                if (DeviceCredentialStore.CreateNew() == null) return;
            }

            await RegisterDeviceCredentialAsync(deviceId).ConfigureAwait(false);
        }

        /// <summary>Adiciona o header de credencial de dispositivo, se houver.</summary>
        private static void AttachCredential(HttpRequestMessage request)
        {
            var credential = DeviceCredentialStore.Current;
            if (!string.IsNullOrEmpty(credential))
            {
                request.Headers.TryAddWithoutValidation("x-voltris-device-credential", credential);
            }
        }

        /// <summary>
        /// Consulta o estado de vinculo no servidor.
        /// Retorna sempre um resultado tipado — nunca "null significa desvinculado".
        /// </summary>
        public async Task<LinkStatusResult> GetLinkStatusAsync(string? installationId = null)
        {
            var settings = SettingsService.Instance.Settings;
            var deviceId = string.IsNullOrWhiteSpace(installationId)
                ? settings.InstallationId
                : installationId;

            if (string.IsNullOrWhiteSpace(deviceId))
            {
                _logger?.LogWarning("[ENTERPRISE] Sem installation_id para consultar o vinculo");
                return new LinkStatusResult
                {
                    Outcome = LinkCheckOutcome.Unreachable,
                    Error = "INSTALLATION_ID_AUSENTE"
                };
            }

            settings.LastLinkCheckAt = DateTime.UtcNow;

            try
            {
                using var client = SecureHttpClientFactory.CreateSecureClient();
                var url = $"{ApiBaseUrl}/install/status?installation_id={Uri.EscapeDataString(deviceId)}";

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("x-correlation-id", CorrelationId.New("VOLTRIS-STATUS"));
                AttachCredential(request);

                _logger?.LogDebug($"[ENTERPRISE] Consultando vinculo: {url}");

                using var response = await client.SendAsync(request).ConfigureAwait(false);
                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger?.LogInfo("[ENTERPRISE] Dispositivo ainda nao registrado no servidor");
                    return new LinkStatusResult
                    {
                        Outcome = LinkCheckOutcome.NotRegistered,
                        HttpStatus = 404
                    };
                }

                if (!response.IsSuccessStatusCode)
                {
                    // 5xx / 401 / 403 aqui significam "nao deu para saber".
                    _logger?.LogWarning(
                        $"[ENTERPRISE] Falha ao consultar vinculo: HTTP {(int)response.StatusCode} - {Truncate(content)}");
                    return new LinkStatusResult
                    {
                        Outcome = LinkCheckOutcome.Unreachable,
                        HttpStatus = (int)response.StatusCode,
                        CorrelationId = ReadCorrelationId(content),
                        Error = content
                    };
                }

                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                var isLinked = root.TryGetProperty("is_linked", out var isLinkedProp)
                               && isLinkedProp.ValueKind == JsonValueKind.True;

                if (!isLinked)
                {
                    // Resposta CONCLUSIVA de "nao vinculado": e a unica
                    // situacao em que o app pode limpar o estado local.

                    return new LinkStatusResult
                    {
                        Outcome = LinkCheckOutcome.NotLinked,
                        HttpStatus = 200,
                        CorrelationId = ReadCorrelationId(content)
                    };
                }

                // Emissao/bootstrap: o servidor devolve a credencial uma unica
                // vez. Persiste com DPAPI para os próximos usos.
                if (root.TryGetProperty("device_credential", out var issued)
                    && issued.ValueKind == JsonValueKind.String)
                {
                    var token = issued.GetString();
                    if (DeviceCredentialStore.TrySave(token))
                    {
                        _logger?.LogInfo("[ENTERPRISE] Credencial de dispositivo recebida e salva.");
                    }
                }

                var credentialInvalid = root.TryGetProperty("credential_invalid", out var ci)
                                        && ci.ValueKind == JsonValueKind.True;

                string? email = null;
                if (root.TryGetProperty("email", out var emailProp) && emailProp.ValueKind == JsonValueKind.String)
                    email = emailProp.GetString();
                if (string.IsNullOrWhiteSpace(email) && root.TryGetProperty("user_email", out var userEmailProp))
                    email = userEmailProp.GetString();

                if (credentialInvalid)
                {
                    // O servidor tem vinculo, mas esta credencial nao bate.
                    // Nao reemitir e nao apagar o estado local: o vinculo continua
                    // valendo no site e o app precisa revincular.
                    _logger?.LogWarning(
                        "[ENTERPRISE] Credencial do dispositivo nao confere. Revincule o computador pela conta.");
                    return new LinkStatusResult
                    {
                        Outcome = LinkCheckOutcome.CredentialMismatch,
                        HttpStatus = 200,
                        CorrelationId = ReadCorrelationId(content),
                        Error = "CREDENTIAL_MISMATCH"
                    };
                }

                if (string.IsNullOrWhiteSpace(email))
                {
                    // Vinculado, mas sem email: estado inconclusivo para a UI.
                    _logger?.LogWarning("[ENTERPRISE] Vinculo confirmado sem email na resposta");
                    return new LinkStatusResult
                    {
                        Outcome = LinkCheckOutcome.Unreachable,
                        HttpStatus = 200,
                        CorrelationId = ReadCorrelationId(content),
                        Error = "EMAIL_AUSENTE"
                    };
                }

                _logger?.LogSuccess($"[ENTERPRISE] Vinculo confirmado: {email}");
                return new LinkStatusResult
                {
                    Outcome = LinkCheckOutcome.Linked,
                    Email = email,
                    HttpStatus = 200,
                    CorrelationId = ReadCorrelationId(content)
                };
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[ENTERPRISE] Erro de rede ao consultar vinculo: {ex.Message}");
                return new LinkStatusResult
                {
                    Outcome = LinkCheckOutcome.Unreachable,
                    Error = ex.Message
                };
            }
        }

        private static string? ReadCorrelationId(string content)
        {
            try
            {
                using var doc = JsonDocument.Parse(content);
                return doc.RootElement.TryGetProperty("correlation_id", out var c) && c.ValueKind == JsonValueKind.String
                    ? c.GetString()
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private static string Truncate(string value, int max = 300)
            => value.Length <= max ? value : value[..max] + "…";
    }
}
