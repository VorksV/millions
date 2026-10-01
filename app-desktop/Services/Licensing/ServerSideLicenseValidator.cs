using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Licensing
{
    /// <summary>
    /// Validador de licença server-side.
    /// Delega para as Edge Functions do Supabase — o segredo nunca está no cliente.
    /// Cache em memória de 24h para reduzir chamadas ao servidor.
    /// </summary>
    public class ServerSideLicenseValidator
    {
        private readonly HttpClient _httpClient;
        private readonly ILoggingService _logger;
        private readonly string _validateEndpoint;
        private readonly string _activateEndpoint;

        private DateTime _lastValidation = DateTime.MinValue;
        private bool _lastValidationResult = false;
        private const int ValidationIntervalHours = 24;
        private const int ValidationTimeoutSeconds = 15;

        public ServerSideLicenseValidator(
            ILoggingService logger,
            string supabaseProjectUrl = "https://zamjyyzockbbugjepkhk.supabase.co")
        {
            _logger = logger;
            _validateEndpoint = $"{supabaseProjectUrl}/functions/v1/validate-license";
            _activateEndpoint = $"{supabaseProjectUrl}/functions/v1/activate-license";

            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(ValidationTimeoutSeconds)
            };

            var anonKey = Services.License.SupabaseConfig.SupabaseAnonKey;
            if (!_httpClient.DefaultRequestHeaders.Contains("apikey"))
            {
                _httpClient.DefaultRequestHeaders.Add("apikey", anonKey);
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {anonKey}");
            }

            _logger.LogInfo($"[ServerSideLicenseValidator] Inicializado — endpoint: {_validateEndpoint}");
        }

        /// <summary>
        /// Valida licença no servidor com cache de 24h.
        /// </summary>
        public async Task<LicenseValidationResult> ValidateAsync(string licenseKey, bool forceRevalidation = false)
        {
            _logger.LogInfo($"[ServerSideLicenseValidator] ValidateAsync chamado — key={licenseKey.Substring(0, Math.Min(12, licenseKey.Length))}..., force={forceRevalidation}");

            // Verificar cache
            if (!forceRevalidation && _lastValidation != DateTime.MinValue)
            {
                var elapsed = DateTime.UtcNow - _lastValidation;
                if (elapsed.TotalHours < ValidationIntervalHours)
                {
                    _logger.LogInfo($"[ServerSideLicenseValidator] Cache válido (age={elapsed.TotalHours:F1}h) — retornando resultado cacheado: {_lastValidationResult}");
                    return new LicenseValidationResult
                    {
                        IsValid = _lastValidationResult,
                        Message = "Validação em cache",
                        CachedResult = true
                    };
                }
            }

            _logger.LogInfo("[ServerSideLicenseValidator] Chamando Edge Function validate-license...");

            try
            {
                var deviceId = LicenseManager.Instance.GetDeviceId();
                var payload = new
                {
                    license_key = licenseKey,
                    device_id = deviceId,
                    machine_name = Environment.MachineName,
                    app_version = GetAppVersion()
                };

                var response = await _httpClient.PostAsJsonAsync(_validateEndpoint, payload);
                var json = await response.Content.ReadAsStringAsync();
                _logger.LogInfo($"[ServerSideLicenseValidator] Resposta: {response.StatusCode} — {json.Substring(0, Math.Min(200, json.Length))}");

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning($"[ServerSideLicenseValidator] Servidor retornou erro: {response.StatusCode}");
                    return HandleOfflineFallback("Erro HTTP: " + response.StatusCode);
                }

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var isValid = root.TryGetProperty("valid", out var validProp) && validProp.GetBoolean();
                var message = root.TryGetProperty("message", out var msgProp) ? msgProp.GetString() ?? "" : "";
                var licenseType = root.TryGetProperty("license_type", out var typeProp) ? typeProp.GetString() ?? "" : "";
                var maxDevices = root.TryGetProperty("max_devices", out var maxProp) ? maxProp.GetInt32() : 1;
                var devicesInUse = root.TryGetProperty("devices_in_use", out var devProp) ? devProp.GetInt32() : 0;
                DateTime? expiresAt = null;
                if (root.TryGetProperty("expires_at", out var expProp) && expProp.GetString() is string expStr)
                    DateTime.TryParse(expStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsedExp);

                _lastValidation = DateTime.UtcNow;
                _lastValidationResult = isValid;

                _logger.LogInfo($"[ServerSideLicenseValidator] Validação concluída: valid={isValid}, type={licenseType}, message={message}");

                return new LicenseValidationResult
                {
                    IsValid = isValid,
                    Message = message,
                    ExpirationDate = expiresAt,
                    PlanType = licenseType,
                    MaxDevices = maxDevices,
                    CurrentDevices = devicesInUse
                };
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogWarning($"[ServerSideLicenseValidator] Timeout na validação: {ex.Message}");
                return HandleOfflineFallback("Timeout na validação");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning($"[ServerSideLicenseValidator] Erro de rede: {ex.Message}");
                return HandleOfflineFallback("Erro de rede: " + ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError("[ServerSideLicenseValidator] Erro inesperado na validação", ex);
                return HandleOfflineFallback("Erro inesperado: " + ex.Message);
            }
        }

        /// <summary>
        /// Ativa licença no servidor (registra dispositivo).
        /// </summary>
        public async Task<LicenseActivationResult> ActivateAsync(string licenseKey)
        {
            _logger.LogInfo($"[ServerSideLicenseValidator] ActivateAsync chamado — key={licenseKey.Substring(0, Math.Min(12, licenseKey.Length))}...");

            try
            {
                var payload = new
                {
                    license_key = licenseKey,
                    device_id = LicenseManager.Instance.GetDeviceId(),
                    machine_name = Environment.MachineName,
                    os_version = Environment.OSVersion.ToString(),
                    app_version = GetAppVersion()
                };

                var response = await _httpClient.PostAsJsonAsync(_activateEndpoint, payload);
                var json = await response.Content.ReadAsStringAsync();
                _logger.LogInfo($"[ServerSideLicenseValidator] Ativação response: {response.StatusCode} — {json.Substring(0, Math.Min(200, json.Length))}");

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var success = root.TryGetProperty("success", out var successProp) && successProp.GetBoolean();
                var message = root.TryGetProperty("message", out var msgProp) ? msgProp.GetString() ?? "" : "";
                var token = root.TryGetProperty("activation_token", out var tokenProp) ? tokenProp.GetString() : null;

                if (success)
                    _logger.LogSuccess($"[ServerSideLicenseValidator] Licença ativada com sucesso: {message}");
                else
                    _logger.LogWarning($"[ServerSideLicenseValidator] Falha na ativação: {message}");

                return new LicenseActivationResult
                {
                    Success = success,
                    Message = message,
                    ActivationToken = token
                };
            }
            catch (Exception ex)
            {
                _logger.LogError("[ServerSideLicenseValidator] Erro na ativação", ex);
                return new LicenseActivationResult { Success = false, Message = $"Erro: {ex.Message}" };
            }
        }

        /// <summary>
        /// Desativa licença no servidor (libera slot de dispositivo).
        /// </summary>
        public async Task<bool> DeactivateAsync(string licenseKey)
        {
            _logger.LogInfo($"[ServerSideLicenseValidator] DeactivateAsync chamado — key={licenseKey.Substring(0, Math.Min(12, licenseKey.Length))}...");
            try
            {
                var payload = new
                {
                    license_key = licenseKey,
                    device_id = LicenseManager.Instance.GetDeviceId()
                };

                var response = await _httpClient.PostAsJsonAsync(
                    _validateEndpoint.Replace("validate-license", "deactivate-license"), payload);

                var success = response.IsSuccessStatusCode;
                _logger.LogInfo($"[ServerSideLicenseValidator] Desativação: {(success ? "sucesso" : "falha")} ({response.StatusCode})");
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError("[ServerSideLicenseValidator] Erro na desativação", ex);
                return false;
            }
        }

        private LicenseValidationResult HandleOfflineFallback(string reason)
        {
            if (_lastValidation != DateTime.MinValue)
            {
                _logger.LogWarning($"[ServerSideLicenseValidator] Servidor inacessível ({reason}) — usando última validação conhecida: {_lastValidationResult}");
                return new LicenseValidationResult
                {
                    IsValid = _lastValidationResult,
                    Message = $"Modo offline — {reason}",
                    ServerOffline = true
                };
            }

            _logger.LogWarning($"[ServerSideLicenseValidator] Servidor inacessível e sem cache — retornando inválido: {reason}");
            return new LicenseValidationResult
            {
                IsValid = false,
                Message = $"Não foi possível validar: {reason}"
            };
        }

        private string GetAppVersion()
        {
            try { return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0"; }
            catch { return "1.0.0"; }
        }
    }

    #region Response Models

    public class LicenseValidationResult
    {
        public bool IsValid { get; set; }
        public string Message { get; set; } = "";
        public DateTime? ExpirationDate { get; set; }
        public string PlanType { get; set; } = "";
        public int MaxDevices { get; set; }
        public int CurrentDevices { get; set; }
        public bool CachedResult { get; set; }
        public bool ServerOffline { get; set; }
    }

    public class LicenseActivationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string? ActivationToken { get; set; }
    }

    #endregion
}
