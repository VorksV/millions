using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.License
{
    /// <summary>
    /// Serviço de comunicação com a API de licenças
    /// </summary>
    public class LicenseApiService : IDisposable
    {
        private static LicenseApiService? _instance;
        public static LicenseApiService Instance => _instance ??= new LicenseApiService();
        
        private readonly HttpClient _httpClient;
        private const string DefaultApiBaseUrl = SupabaseConfig.DefaultApiBaseUrl;
        private readonly string _supabaseKey;
        private readonly string _apiBaseUrl;
        private readonly JsonSerializerOptions _jsonOptions;
        
        // Retry configuration
        private const int MaxRetries = 3;
        private const int RetryDelayMs = 1000;
        
        private LicenseApiService()
        {
            _apiBaseUrl = GetApiBaseUrl();
            _supabaseKey = SupabaseConfig.SupabaseAnonKey;

            // VALIDAÇÃO CRÍTICA: HttpClient com validação TLS e segurança
            var handler = new HttpClientHandler
            {
                // Exigir certificados válidos (prevenir MITM)
                ServerCertificateCustomValidationCallback = (sender, cert, chain, errors) =>
                {
                    if (errors == System.Net.Security.SslPolicyErrors.None)
                        return true;
                    
                    // Log de tentativa de certificado inválido
                    App.LoggingService?.LogWarning($"[LicenseAPI] Certificado inválido detectado: {errors}");
                    return false; // Rejeitar certificados inválidos
                }
            };
            
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            
            // Headers seguros sem expor informações sensíveis
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "VoltrisOptimizer/8.0");
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
            _httpClient.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
            
            // Adicionar Headers do Supabase (apenas a chave ANON — pública por design)
            _httpClient.DefaultRequestHeaders.Add("apikey", _supabaseKey);
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_supabaseKey}");
            
            _jsonOptions = new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, 
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
        }
        
        private string GetApiBaseUrl()
        {
            // Pode ser configurado via settings ou ambiente
            try
            {
                var settings = SettingsService.Instance.Settings;
                if (!string.IsNullOrEmpty(settings.LicenseApiUrl))
                    return settings.LicenseApiUrl;
            }
            catch { }
            
            return DefaultApiBaseUrl;
        }
        
        private string GetVersion()
        {
            try
            {
                return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
            }
            catch
            {
                return "1.0.0";
            }
        }
        
        /// <summary>
        /// Valida uma licença no servidor
        /// </summary>
        public async Task<LicenseValidationResponse> ValidateLicenseAsync(
            string licenseKey, 
            string deviceId, 
            CancellationToken ct = default)
        {
            return await ExecuteWithRetryAsync(async () =>
            {
                var checkRequest = new { license_key = licenseKey, device_id = deviceId };
                var response = await _httpClient.PostAsJsonAsync(
                    $"{_apiBaseUrl}/validate-license", 
                    checkRequest, 
                    _jsonOptions, 
                    ct);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync(ct);
                    App.LoggingService?.LogWarning($"[LicenseAPI] Erro HTTP {response.StatusCode}: {errorContent}");
                    
                    return new LicenseValidationResponse
                    {
                        Valid = false,
                        ErrorMessage = $"Erro de comunicação: {response.StatusCode}",
                        ErrorCode = "HTTP_ERROR"
                    };
                }
                
                var rawResponse = await response.Content.ReadAsStringAsync(ct);
                var jsonResponse = JsonDocument.Parse(rawResponse);
                
                if (jsonResponse.RootElement.TryGetProperty("success", out var successProp) && 
                    successProp.GetBoolean())
                {
                    if (jsonResponse.RootElement.TryGetProperty("valid", out var validProp) && validProp.GetBoolean())
                    {
                        if (jsonResponse.RootElement.TryGetProperty("license", out var licenseProp))
                        {
                            // ── VERIFICAÇÃO DE ASSINATURA (segurança) ──
                            // A resposta de validação também é assinada pelo servidor.
                            var sigOk = VerifyValidationSignature(licenseKey, deviceId, true, jsonResponse.RootElement);
                            if (!sigOk)
                            {
                                App.LoggingService?.LogWarning("[LicenseAPI] Validação rejeitada: assinatura da resposta inválida ou ausente.");
                                return new LicenseValidationResponse
                                {
                                    Valid = false,
                                    ErrorMessage = "Resposta do servidor inválida (assinatura). Contate o suporte.",
                                    ErrorCode = "INVALID_SERVER_SIGNATURE"
                                };
                            }

                            return new LicenseValidationResponse 
                            { 
                                Valid = true,
                                Type = licenseProp.GetProperty("license_type").GetString() ?? "pro",
                                MaxDevices = licenseProp.GetProperty("max_devices").GetInt32(),
                                ExpiresAt = DateTime.Parse(licenseProp.GetProperty("expires_at").GetString() ?? DateTime.Now.AddYears(1).ToString()),
                                DeviceRegistered = true
                            };
                        }
                    }
                }
                
                // Licença inválida
                return new LicenseValidationResponse 
                { 
                    Valid = false, 
                    ErrorMessage = "Licença inválida ou não encontrada",
                    ErrorCode = "INVALID_LICENSE"
                };
                
            }, ct);
        }
        
        /// <summary>
        /// Ativa uma licença no servidor (registra o dispositivo)
        /// </summary>
        public async Task<LicenseActivationResponse> ActivateLicenseAsync(
            string licenseKey,
            string deviceId,
            CancellationToken ct = default)
        {
            return await ExecuteWithRetryAsync(async () =>
            {
                var request = new { license_key = licenseKey, device_id = deviceId };
                var response = await _httpClient.PostAsJsonAsync(
                    $"{_apiBaseUrl}/activate-license",
                    request,
                    _jsonOptions,
                    ct);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync(ct);
                    App.LoggingService?.LogWarning($"[LicenseAPI] Erro HTTP {response.StatusCode}: {errorContent}");
                    
                    return new LicenseActivationResponse
                    {
                        Success = false,
                        ErrorMessage = $"Erro de comunicação: {response.StatusCode}",
                        ErrorCode = "HTTP_ERROR"
                    };
                }
                
                var rawResponse = await response.Content.ReadAsStringAsync(ct);
                var jsonResponse = JsonDocument.Parse(rawResponse);
                
                if (jsonResponse.RootElement.TryGetProperty("success", out var successProp) && successProp.GetBoolean())
                {
                    // ── VERIFICAÇÃO DE ASSINATURA (segurança) ──
                    // O servidor assina a resposta com a chave privada. Sem assinatura
                    // válida, a ativação é rejeitada (impede resposta forjada via MITM).
                    var signatureOk = VerifyActivationSignature(jsonResponse.RootElement, deviceId);
                    if (!signatureOk)
                    {
                        App.LoggingService?.LogWarning("[LicenseAPI] Ativação rejeitada: assinatura da resposta inválida ou ausente.");
                        return new LicenseActivationResponse
                        {
                            Success = false,
                            ErrorMessage = "Resposta do servidor inválida (assinatura). Contate o suporte.",
                            ErrorCode = "INVALID_SERVER_SIGNATURE"
                        };
                    }

                    return new LicenseActivationResponse
                    {
                        Success = true
                    };
                }
                else
                {
                    string errorMsg = "Ativação falhou";
                    if (jsonResponse.RootElement.TryGetProperty("message", out var msgProp))
                        errorMsg = msgProp.GetString() ?? errorMsg;
                        
                    return new LicenseActivationResponse
                    {
                        Success = false,
                        ErrorMessage = errorMsg
                    };
                }
            }, ct);
        }
        
        /// <summary>
        /// Desativa um dispositivo (libera slot)
        /// </summary>
        public async Task<LicenseDeactivationResponse> DeactivateDeviceAsync(
            string licenseKey,
            string deviceId,
            CancellationToken ct = default)
        {
            var request = new LicenseDeactivationRequest
            {
                LicenseKey = licenseKey,
                DeviceId = deviceId
            };
            
            return await ExecuteWithRetryAsync(async () =>
            {
                var response = await _httpClient.PostAsJsonAsync(
                    $"{_apiBaseUrl}/deactivate",
                    request,
                    _jsonOptions,
                    ct);
                
                if (!response.IsSuccessStatusCode)
                {
                    return new LicenseDeactivationResponse
                    {
                        Success = false,
                        ErrorMessage = $"Erro de comunicação: {response.StatusCode}"
                    };
                }
                
                var result = await response.Content.ReadFromJsonAsync<LicenseDeactivationResponse>(_jsonOptions, ct);
                return result ?? new LicenseDeactivationResponse { Success = false };
                
            }, ct);
        }
        
        /// <summary>
        /// Obtém informações detalhadas da licença
        /// </summary>
        public async Task<LicenseInfoResponse> GetLicenseInfoAsync(
            string licenseKey,
            CancellationToken ct = default)
        {
            return await ExecuteWithRetryAsync(async () =>
            {
                var response = await _httpClient.GetAsync(
                    $"{_apiBaseUrl}/info?key={Uri.EscapeDataString(licenseKey)}",
                    ct);
                
                if (!response.IsSuccessStatusCode)
                {
                    return new LicenseInfoResponse { Valid = false };
                }
                
                var result = await response.Content.ReadFromJsonAsync<LicenseInfoResponse>(_jsonOptions, ct);
                return result ?? new LicenseInfoResponse { Valid = false };
                
            }, ct);
        }
        
        /// <summary>
        /// Executa uma operação com retry inteligente
        /// </summary>
        private async Task<T> ExecuteWithRetryAsync<T>(
            Func<Task<T>> operation, 
            CancellationToken ct) where T : class, new()
        {
            Exception? lastException = null;
            
            for (int attempt = 0; attempt < MaxRetries; attempt++)
            {
                try
                {
                    if (attempt > 0)
                    {
                        var delay = RetryDelayMs * (int)Math.Pow(2, attempt - 1);
                        await Task.Delay(delay, ct);
                        App.LoggingService?.LogInfo($"[LicenseAPI] Tentativa {attempt + 1}/{MaxRetries}...");
                    }
                    
                    return await operation();
                }
                catch (TaskCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (HttpRequestException ex)
                {
                    lastException = ex;
                    App.LoggingService?.LogWarning($"[LicenseAPI] Erro de rede: {ex.Message}");
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    App.LoggingService?.LogError($"[LicenseAPI] Erro inesperado", ex);
                    break;
                }
            }
            
            App.LoggingService?.LogError($"[LicenseAPI] Falha após {MaxRetries} tentativas", lastException);
            return new T();
        }

        /// <summary>
        /// Verifica a assinatura da resposta de ativação.
        /// Payload canônico assinado pelo servidor (ServerFunctions/README.md).
        /// </summary>
        private static bool VerifyActivationSignature(JsonElement root, string deviceId)
        {
            try
            {
                if (!root.TryGetProperty("signature", out var sigProp))
                    return false;

                var licenseType = TryGetProp(root, "license_type") ?? "";
                var maxDevices = 1;
                if (int.TryParse(TryGetProp(root, "max_devices"), out var parsedMax)) maxDevices = parsedMax;
                var expiresAtStr = TryGetProp(root, "expires_at") ?? "";
                if (!DateTime.TryParse(expiresAtStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var expiresAt))
                    return false;

                var canonical = LicenseSignatureVerifier.BuildActivationPayload(licenseType, maxDevices, expiresAt, deviceId);
                return LicenseSignatureVerifier.VerifySignedPayload(canonical, sigProp.GetString());
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Verifica a assinatura da resposta de validação.
        /// </summary>
        private static bool VerifyValidationSignature(string licenseKey, string deviceId, bool isValid, JsonElement root)
        {
            try
            {
                if (!root.TryGetProperty("signature", out var sigProp))
                    return false;

                var canonical = LicenseSignatureVerifier.BuildValidationPayload(licenseKey, deviceId, isValid);
                return LicenseSignatureVerifier.VerifySignedPayload(canonical, sigProp.GetString());
            }
            catch
            {
                return false;
            }
        }

        private static string? TryGetProp(JsonElement root, string name)
        {
            if (root.TryGetProperty(name, out var prop) && prop.ValueKind != JsonValueKind.Null)
                return prop.ToString();
            return null;
        }

        /// <summary>
        /// Verifica se o servidor está acessível
        /// </summary>
        public async Task<bool> IsServerReachableAsync(CancellationToken ct = default)
        {
            App.LoggingService?.LogInfo($"[LicenseAPI] === INICIANDO TESTE DE CONECTIVIDADE ===");
            App.LoggingService?.LogInfo($"[LicenseAPI] URL Base: {_apiBaseUrl}");
            App.LoggingService?.LogInfo($"[LicenseAPI] Timeout configurado: {_httpClient.Timeout.TotalSeconds}s");
            
            try
            {
                // Verificar se o HttpClient está configurado corretamente
                if (_httpClient == null)
                {
                    App.LoggingService?.LogError($"[LicenseAPI] HttpClient é NULL!");
                    return false;
                }
                
                App.LoggingService?.LogInfo($"[LicenseAPI] HttpClient configurado corretamente");
                
                // Usar endpoint de validação com chave de teste para verificar conectividade
                var testKey = "VOLTRIS-LIC-TESTE-20260113-ABC123DEF456";
                var request = new { license_key = testKey };
                
                App.LoggingService?.LogInfo($"[LicenseAPI] Preparando requisição POST para {_apiBaseUrl}/validate");
                App.LoggingService?.LogInfo($"[LicenseAPI] Chave de teste: {testKey}");
                
                // Testar conectividade básica primeiro
                App.LoggingService?.LogInfo($"[LicenseAPI] Testando DNS/Conectividade básica...");
                var uri = new Uri($"{_apiBaseUrl}/validate");
                App.LoggingService?.LogInfo($"[LicenseAPI] URI completo: {uri}");
                
                App.LoggingService?.LogInfo($"[LicenseAPI] ENVIANDO REQUISIÇÃO...");
                var startTime = DateTime.Now;
                
                var response = await _httpClient.PostAsJsonAsync(
                    $"{_apiBaseUrl}/validate", 
                    request, 
                    _jsonOptions, 
                    ct);
                
                var elapsed = DateTime.Now - startTime;
                App.LoggingService?.LogInfo($"[LicenseAPI] RESPOSTA RECEBIDA em {elapsed.TotalMilliseconds:F0}ms");
                App.LoggingService?.LogInfo($"[LicenseAPI] Status code: {response.StatusCode}");
                App.LoggingService?.LogInfo($"[LicenseAPI] Reason phrase: {response.ReasonPhrase}");
                
                var content = await response.Content.ReadAsStringAsync(ct);
                App.LoggingService?.LogInfo($"[LicenseAPI] Conteúdo da resposta ({content.Length} caracteres): {content.Substring(0, Math.Min(300, content.Length))}...");
                
                if (!response.IsSuccessStatusCode)
                {
                    App.LoggingService?.LogWarning($"[LicenseAPI] Requisição falhou com status {response.StatusCode}, mas servidor respondeu em {elapsed.TotalMilliseconds:F0}ms");
                    // Mesmo com erro HTTP, se chegou aqui é porque o servidor respondeu
                    return true;
                }
                
                // Parsear a resposta para confirmar que é válida
                App.LoggingService?.LogInfo($"[LicenseAPI] Parseando resposta JSON...");
                var jsonResponse = JsonDocument.Parse(content);
                
                if (jsonResponse.RootElement.TryGetProperty("success", out var successProp))
                {
                    var successValue = successProp.GetBoolean();
                    App.LoggingService?.LogInfo($"[LicenseAPI] Propriedade 'success' encontrada: {successValue}");
                    // Se tem a propriedade success, o endpoint está funcionando
                    return successValue;
                }
                
                App.LoggingService?.LogWarning($"[LicenseAPI] Propriedade 'success' não encontrada na resposta");
                return false;
            }
            catch (TaskCanceledException tcex)
            {
                App.LoggingService?.LogError($"[LicenseAPI] TIMEOUT - Requisição cancelada após {_httpClient.Timeout.TotalSeconds}s: {tcex.Message}");
                return false;
            }
            catch (HttpRequestException hrex)
            {
                App.LoggingService?.LogError($"[LicenseAPI] ERRO DE REDE - Não foi possível conectar: {hrex.Message}");
                App.LoggingService?.LogError($"[LicenseAPI] Inner exception: {hrex.InnerException?.Message}");
                return false;
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[LicenseAPI] ERRO FATAL NO TESTE DE CONECTIVIDADE: {ex}");
                App.LoggingService?.LogError($"[LicenseAPI] Tipo da exceção: {ex.GetType().Name}");
                App.LoggingService?.LogError($"[LicenseAPI] Stack trace: {ex.StackTrace}");
                return false;
            }
            finally
            {
                App.LoggingService?.LogInfo($"[LicenseAPI] === FIM DO TESTE DE CONECTIVIDADE ===");
            }
        }
        
        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
    
    #region Request/Response Models
    
    public class LicenseValidationRequest
    {
        public string LicenseKey { get; set; } = "";
        public string DeviceId { get; set; } = "";
        public string Version { get; set; } = "";
        public string Platform { get; set; } = "";
        public string MachineName { get; set; } = "";
    }
    
    public class LicenseValidationResponse
    {
        public bool Valid { get; set; }
        public string Type { get; set; } = "Trial";
        public int MaxDevices { get; set; } = 1;
        public int DevicesInUse { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string? BillingPeriod { get; set; } = "year";
        public string? ErrorMessage { get; set; }
        public string? ErrorCode { get; set; }
        public bool DeviceRegistered { get; set; }
    }
    
    public class LicenseActivationRequest
    {
        public string LicenseKey { get; set; } = "";
        public string DeviceId { get; set; } = "";
        public string Version { get; set; } = "";
        public string Platform { get; set; } = "";
        public string MachineName { get; set; } = "";
        public string OSVersion { get; set; } = "";
        public int ProcessorCount { get; set; }
    }
    
    public class LicenseActivationResponse
    {
        public bool Success { get; set; }
        public string Type { get; set; } = "Trial";
        public int MaxDevices { get; set; } = 1;
        public int DevicesInUse { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string? BillingPeriod { get; set; } = "year";
        public string? ErrorMessage { get; set; }
        public string? ErrorCode { get; set; }
    }
    
    public class LicenseDeactivationRequest
    {
        public string LicenseKey { get; set; } = "";
        public string DeviceId { get; set; } = "";
    }
    
    public class LicenseDeactivationResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }
    
    public class LicenseInfoResponse
    {
        public bool Valid { get; set; }
        public string Type { get; set; } = "Trial";
        public int MaxDevices { get; set; } = 1;
        public int DevicesInUse { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public DateTime? ActivatedAt { get; set; }
        public string? CustomerEmail { get; set; }
        public string[] RegisteredDevices { get; set; } = Array.Empty<string>();
    }
    
    #endregion
}

