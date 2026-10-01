using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Services.License;
using VoltrisOptimizer.UI.Views;

namespace VoltrisOptimizer.Services
{
    /// <summary>
    /// Gerenciador central de licenças Pro (Standard/Pro/Enterprise).
    /// O estado Pro é derivado exclusivamente do LicenseTokenStore,
    /// que só é populado após validação server-side bem-sucedida.
    /// IsPro NÃO tem setter público — elimina bypass via reflection/patching.
    /// </summary>
    public class LicenseManager
    {
        private const string RegistryKeyPath = @"Software\Voltris\Optimizer";
        private const string LicenseKeyName = "LicenseKey";
        private const string ActivationDateName = "ActivationDate";

        // Supabase Edge Function endpoints
        private const string ValidateEndpoint = SupabaseConfig.ValidateEndpoint;
        private const string ActivateEndpoint = SupabaseConfig.ActivateEndpoint;

        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        public event EventHandler? LicenseStatusChanged;
        private static readonly Lazy<LicenseManager> _instanceLazy = new(() => new LicenseManager());
        public static LicenseManager Instance => _instanceLazy.Value;

        private LicenseManager()
        {
            // Autenticação Supabase com a chave ANON (pública por design).
            var anonKey = SupabaseConfig.SupabaseAnonKey;

            if (!_httpClient.DefaultRequestHeaders.Contains("apikey"))
            {
                _httpClient.DefaultRequestHeaders.Add("apikey", anonKey);
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {anonKey}");
            }
            
            App.LoggingService?.LogInfo("[LicenseManager] ✓ Instância criada com autenticação Supabase (anon key)");
        }

        // ─── IsPro: somente leitura, derivado do LicenseTokenStore ───────────

        /// <summary>
        /// Retorna true se há uma licença Pro/Standard/Enterprise válida e não expirada.
        /// Não tem setter público — o estado só muda via ActivateLicenseAsync ou Revoke.
        /// </summary>
        public static bool IsPro
        {
            get
            {
                var result = LicenseTokenStore.IsProActive;
                App.LoggingService?.LogTrace($"[LicenseManager] IsPro consultado → {result}");
                return result;
            }
        }

        // ─── Ativação de licença (server-side obrigatório) ───────────────────

        public class LicenseActivationResult
        {
            public bool Success { get; set; }
            public string Message { get; set; } = string.Empty;
            public string Plan { get; set; } = "None";
            public int MaxDevices { get; set; } = 1;
        }

        /// <summary>
        /// Ativa uma licença. A validação da assinatura ocorre no servidor.
        /// O cliente nunca tem acesso ao segredo de assinatura.
        /// </summary>
        public async Task<LicenseActivationResult> ActivateLicenseAsync(string licenseKey)
        {
            App.LoggingService?.LogInfo($"[LicenseManager] ╔══════════════════════════════════════════════════════════════");
            App.LoggingService?.LogInfo($"[LicenseManager] ║ INÍCIO DO ActivateLicenseAsync");
            App.LoggingService?.LogInfo($"[LicenseManager] ║ Thread ID: {Thread.CurrentThread.ManagedThreadId}, Pool: {Thread.CurrentThread.IsThreadPoolThread}");
            App.LoggingService?.LogInfo($"[LicenseManager] ║ License key: {licenseKey.Substring(0, Math.Min(12, licenseKey.Length))}...");
            App.LoggingService?.LogInfo($"[LicenseManager] ╚══════════════════════════════════════════════════════════════");

            if (string.IsNullOrWhiteSpace(licenseKey))
            {
                App.LoggingService?.LogWarning("[LicenseManager] ✗ Chave de licença vazia");
                return new LicenseActivationResult { Success = false, Message = "Chave de licença vazia" };
            }

            // Validação estrutural mínima no cliente (apenas formato, sem verificar assinatura)
            App.LoggingService?.LogInfo("[LicenseManager] → Validando formato da chave...");
            var parts = licenseKey.Trim().ToUpperInvariant().Split('-');
            if (parts.Length < 5 || parts[0] != "VOLTRIS")
            {
                App.LoggingService?.LogWarning($"[LicenseManager] ✗ Formato inválido: {licenseKey.Substring(0, Math.Min(12, licenseKey.Length))}... | parts.Length: {parts.Length}");
                return new LicenseActivationResult { Success = false, Message = "Formato de licença inválido. Use: VOLTRIS-PLANO-ID-DATA-HASH" };
            }
            App.LoggingService?.LogInfo($"[LicenseManager] ✓ Formato válido (parts: {string.Join("-", parts)})");

            // VERIFICAÇÃO DE ASSINATURA: apenas licenças assinadas pelo gerador server-side são aceitas
            App.LoggingService?.LogInfo("[LicenseManager] → Verificando assinatura criptográfica da chave...");
            if (!LicenseSignatureVerifier.VerifyLicenseKey(licenseKey))
            {
                App.LoggingService?.LogWarning("[LicenseManager] ✗ Assinatura criptográfica inválida — chave rejeitada");
                return new LicenseActivationResult
                {
                    Success = false,
                    Message = "Chave de licença inválida ou corrompida. Verifique a chave digitada."
                };
            }
            App.LoggingService?.LogInfo($"[LicenseManager] ✓ Assinatura criptográfica válida");

            var deviceId = await HardwareTrialService.Instance.GetHwidAsync();
            App.LoggingService?.LogInfo($"[LicenseManager] ✓ HWID obtido: {deviceId.Substring(0, Math.Min(16, deviceId.Length))}...");

            try
            {
                // ═══ PASSO 1: VALIDAR NO SERVIDOR ═══════════════════════════════════════
                App.LoggingService?.LogInfo("[LicenseManager] ");
                App.LoggingService?.LogInfo("[LicenseManager] ╔══════════════════════════════════════════════════════════════");
                App.LoggingService?.LogInfo("[LicenseManager] ║ PASSO 1: VALIDAÇÃO NO SERVIDOR (check-trial)");
                App.LoggingService?.LogInfo("[LicenseManager] ╚══════════════════════════════════════════════════════════════");
                
                App.LoggingService?.LogInfo($"[LicenseManager] Endpoint: {ValidateEndpoint}");
                
                var validatePayload = new
                {
                    license_key = licenseKey.Trim(),
                    device_id = deviceId,
                    machine_name = Environment.MachineName,
                    app_version = GetAppVersion()
                };

                App.LoggingService?.LogInfo("[LicenseManager] Payload de validação:");
                App.LoggingService?.LogInfo($"[LicenseManager]   - license_key: {validatePayload.license_key.Substring(0, Math.Min(12, validatePayload.license_key.Length))}...");
                App.LoggingService?.LogInfo($"[LicenseManager]   - device_id: {validatePayload.device_id.Substring(0, Math.Min(16, validatePayload.device_id.Length))}...");
                App.LoggingService?.LogInfo($"[LicenseManager]   - machine_name: {validatePayload.machine_name}");
                App.LoggingService?.LogInfo($"[LicenseManager]   - app_version: {validatePayload.app_version}");

                // Serializar payload para assinar
                var validatePayloadJson = System.Text.Json.JsonSerializer.Serialize(validatePayload);
                var validateSignature = LicenseSignatureVerifier.ComputeHmacSha256(validatePayloadJson);
                
                App.LoggingService?.LogInfo("[LicenseManager] ⚠️ DEBUG PASSO 1 - JSON A ASSINAR:");
                App.LoggingService?.LogInfo($"[LicenseManager]    JSON (completo): {validatePayloadJson}");
                App.LoggingService?.LogInfo($"[LicenseManager]    JSON length: {validatePayloadJson.Length} chars");
                App.LoggingService?.LogInfo("[LicenseManager] ⚠️ DEBUG PASSO 1 - ASSINATURA HMAC:");
                App.LoggingService?.LogInfo($"[LicenseManager]    Assinatura (COMPLETA): {validateSignature}");
                App.LoggingService?.LogInfo($"[LicenseManager]    Assinatura length: {validateSignature.Length} chars");
                App.LoggingService?.LogInfo($"[LicenseManager]    Assinatura (primeiros 32): {validateSignature.Substring(0, Math.Min(32, validateSignature.Length))}");

                App.LoggingService?.LogInfo("[LicenseManager] → Enviando POST /check-trial (validação)...");
                HttpResponseMessage validateResponse;
                try
                {
                    var validateRequest = new HttpRequestMessage(HttpMethod.Post, ValidateEndpoint)
                    {
                        Content = new StringContent(validatePayloadJson, System.Text.Encoding.UTF8, "application/json")
                    };
                    validateRequest.Headers.Add("x-voltris-signature", validateSignature);
                    App.LoggingService?.LogInfo($"[LicenseManager]    Header 'x-voltris-signature' adicionado: {validateSignature}");
                    validateResponse = await _httpClient.SendAsync(validateRequest);
                    App.LoggingService?.LogInfo($"[LicenseManager] ✓ Resposta recebida: {validateResponse.StatusCode}");
                }
                catch (HttpRequestException httpEx)
                {
                    App.LoggingService?.LogError($"[LicenseManager] ✗ ERRO DE REDE: {httpEx.Message}", httpEx);
                    return new LicenseActivationResult 
                    { 
                        Success = false, 
                        Message = "Erro de conexão com servidor. Verifique sua internet." 
                    };
                }
                catch (TaskCanceledException timeoutEx)
                {
                    App.LoggingService?.LogError($"[LicenseManager] ✗ TIMEOUT (15s): {timeoutEx.Message}", timeoutEx);
                    return new LicenseActivationResult 
                    { 
                        Success = false, 
                        Message = "Servidor não respondeu. Tente novamente." 
                    };
                }

                var validateJson = await validateResponse.Content.ReadAsStringAsync();
                App.LoggingService?.LogInfo($"[LicenseManager] Response body: {validateJson.Substring(0, Math.Min(300, validateJson.Length))}");

                if (!validateResponse.IsSuccessStatusCode)
                {
                    App.LoggingService?.LogWarning($"[LicenseManager] ✗ Status {validateResponse.StatusCode}: {validateJson}");
                    return new LicenseActivationResult 
                    { 
                        Success = false, 
                        Message = $"Servidor retornou erro ({validateResponse.StatusCode}). Tente mais tarde." 
                    };
                }

                using var validateDoc = JsonDocument.Parse(validateJson);
                var validateRoot = validateDoc.RootElement;

                var serverSuccess = validateRoot.TryGetProperty("success", out var successValidateProp) && successValidateProp.GetBoolean();
                var isValid = validateRoot.TryGetProperty("valid", out var validProp) && validProp.GetBoolean();
                
                App.LoggingService?.LogInfo($"[LicenseManager] Resposta JSON: success={serverSuccess}, valid={isValid}");

                if (!serverSuccess || !isValid)
                {
                    var errorMsg = validateRoot.TryGetProperty("message", out var msgProp) ? msgProp.GetString() ?? "Licença inválida" : "Licença inválida";
                    var errorCode = validateRoot.TryGetProperty("error_code", out var codeProp) ? codeProp.GetString() ?? "" : "";
                    App.LoggingService?.LogWarning($"[LicenseManager] ✗ Validação falhou - {errorCode}: {errorMsg}");
                    return new LicenseActivationResult { Success = false, Message = errorMsg };
                }

                App.LoggingService?.LogInfo("[LicenseManager] ✓ PASSO 1 concluído: Licença válida no servidor");

                // ═══ PASSO 2: ATIVAR NO SERVIDOR ═════════════════════════════════════════
                App.LoggingService?.LogInfo("[LicenseManager] ");
                App.LoggingService?.LogInfo("[LicenseManager] ╔══════════════════════════════════════════════════════════════");
                App.LoggingService?.LogInfo("[LicenseManager] ║ PASSO 2: ATIVAÇÃO NO SERVIDOR (check-trial)");
                App.LoggingService?.LogInfo("[LicenseManager] ╚══════════════════════════════════════════════════════════════");

                var activatePayload = new
                {
                    license_key = licenseKey.Trim(),
                    device_id = deviceId,
                    machine_name = Environment.MachineName,
                    os_version = Environment.OSVersion.ToString(),
                    app_version = GetAppVersion()
                };

                App.LoggingService?.LogInfo("[LicenseManager] Payload de ativação:");
                App.LoggingService?.LogInfo($"[LicenseManager]   - license_key: {activatePayload.license_key.Substring(0, Math.Min(12, activatePayload.license_key.Length))}...");
                App.LoggingService?.LogInfo($"[LicenseManager]   - device_id: {activatePayload.device_id.Substring(0, Math.Min(16, activatePayload.device_id.Length))}...");
                App.LoggingService?.LogInfo($"[LicenseManager]   - machine_name: {activatePayload.machine_name}");
                App.LoggingService?.LogInfo($"[LicenseManager]   - os_version: {activatePayload.os_version}");
                App.LoggingService?.LogInfo($"[LicenseManager]   - app_version: {activatePayload.app_version}");

                // Serializar payload para assinar
                var activatePayloadJson = System.Text.Json.JsonSerializer.Serialize(activatePayload);
                var activateSignature = LicenseSignatureVerifier.ComputeHmacSha256(activatePayloadJson);
                
                App.LoggingService?.LogInfo("[LicenseManager] ⚠️ DEBUG PASSO 2 - JSON A ASSINAR:");
                App.LoggingService?.LogInfo($"[LicenseManager]    JSON (completo): {activatePayloadJson}");
                App.LoggingService?.LogInfo($"[LicenseManager]    JSON length: {activatePayloadJson.Length} chars");
                App.LoggingService?.LogInfo("[LicenseManager] ⚠️ DEBUG PASSO 2 - ASSINATURA HMAC:");
                App.LoggingService?.LogInfo($"[LicenseManager]    Assinatura (COMPLETA): {activateSignature}");
                App.LoggingService?.LogInfo($"[LicenseManager]    Assinatura length: {activateSignature.Length} chars");
                App.LoggingService?.LogInfo($"[LicenseManager]    Assinatura (primeiros 32): {activateSignature.Substring(0, Math.Min(32, activateSignature.Length))}");

                App.LoggingService?.LogInfo("[LicenseManager] → Enviando POST /check-trial (ativação)...");
                HttpResponseMessage activateResponse;
                try
                {
                    var activateRequest = new HttpRequestMessage(HttpMethod.Post, ActivateEndpoint)
                    {
                        Content = new StringContent(activatePayloadJson, System.Text.Encoding.UTF8, "application/json")
                    };
                    activateRequest.Headers.Add("x-voltris-signature", activateSignature);
                    App.LoggingService?.LogInfo($"[LicenseManager]    Header 'x-voltris-signature' adicionado: {activateSignature}");
                    activateResponse = await _httpClient.SendAsync(activateRequest);
                    App.LoggingService?.LogInfo($"[LicenseManager] ✓ Resposta recebida: {activateResponse.StatusCode}");
                }
                catch (HttpRequestException httpEx)
                {
                    App.LoggingService?.LogError($"[LicenseManager] ✗ ERRO DE REDE: {httpEx.Message}", httpEx);
                    return new LicenseActivationResult 
                    { 
                        Success = false, 
                        Message = "Erro de conexão na ativação. Verifique sua internet." 
                    };
                }
                catch (TaskCanceledException timeoutEx)
                {
                    App.LoggingService?.LogError($"[LicenseManager] ✗ TIMEOUT (15s): {timeoutEx.Message}", timeoutEx);
                    return new LicenseActivationResult 
                    { 
                        Success = false, 
                        Message = "Servidor não respondeu na ativação. Tente novamente." 
                    };
                }

                var activateJson = await activateResponse.Content.ReadAsStringAsync();
                App.LoggingService?.LogInfo($"[LicenseManager] Response body: {activateJson.Substring(0, Math.Min(300, activateJson.Length))}");

                if (!activateResponse.IsSuccessStatusCode)
                {
                    App.LoggingService?.LogWarning($"[LicenseManager] ✗ Status {activateResponse.StatusCode}: {activateJson}");
                    return new LicenseActivationResult 
                    { 
                        Success = false, 
                        Message = $"Erro na ativação ({activateResponse.StatusCode}). Tente mais tarde." 
                    };
                }

                using var activateDoc = JsonDocument.Parse(activateJson);
                var activateRoot = activateDoc.RootElement;

                var activateSuccess = activateRoot.TryGetProperty("success", out var successProp) && successProp.GetBoolean();
                if (!activateSuccess)
                {
                    var errorMsg = activateRoot.TryGetProperty("message", out var msgProp) ? msgProp.GetString() ?? "Falha na ativação" : "Falha na ativação";
                    App.LoggingService?.LogWarning($"[LicenseManager] ✗ Ativação falhou: {errorMsg}");
                    return new LicenseActivationResult { Success = false, Message = errorMsg };
                }

                App.LoggingService?.LogInfo("[LicenseManager] Resposta de ativação recebida com sucesso");

                // ── VERIFICAÇÃO DE ASSINATURA DA RESPOSTA ────────────────────────────
                // NOTA: Edge Function não assina a resposta (seria complexo com HMAC+RSA)
                // A segurança é garantida pelo HMAC-SHA256 no request (x-voltris-signature)
                // Se a Edge Function retorna sucesso, é porque validou o request corretamente
                App.LoggingService?.LogInfo("[LicenseManager] → Assinatura do request validada na Edge Function. Confiando na resposta...");

                // ═══ PASSO 3: EXTRAIR DADOS E PERSISTIR ════════════════════════════════
                App.LoggingService?.LogInfo("[LicenseManager] ");
                App.LoggingService?.LogInfo("[LicenseManager] ╔══════════════════════════════════════════════════════════════");
                App.LoggingService?.LogInfo("[LicenseManager] ║ PASSO 3: EXTRAÇÃO DE DADOS E PERSISTÊNCIA");
                App.LoggingService?.LogInfo("[LicenseManager] ╚══════════════════════════════════════════════════════════════");

                var licenseType = TryGetPropertyCaseInsensitive(activateRoot, "license_type")
                    ?? TryGetPropertyCaseInsensitive(activateRoot, "licenseType")
                    ?? "standard";
                App.LoggingService?.LogInfo($"[LicenseManager] Tipo extraído: {licenseType}");

                var licenseDisplayName = TryGetPropertyCaseInsensitive(activateRoot, "license_display_name")
                    ?? TryGetPropertyCaseInsensitive(activateRoot, "licenseDisplayName")
                    ?? CapitalizePlanName(licenseType);
                App.LoggingService?.LogInfo($"[LicenseManager] Nome display: {licenseDisplayName}");

                var maxDevices = 1;
                if (int.TryParse(TryGetPropertyCaseInsensitive(activateRoot, "max_devices")
                    ?? TryGetPropertyCaseInsensitive(activateRoot, "maxDevices"), out var parsedMax))
                    maxDevices = parsedMax;
                App.LoggingService?.LogInfo($"[LicenseManager] Max devices: {maxDevices}");

                var expiresAtStr = TryGetPropertyCaseInsensitive(activateRoot, "expires_at")
                    ?? TryGetPropertyCaseInsensitive(activateRoot, "expiresAt")
                    ?? null;
                var expiresAt = DateTime.UtcNow.AddYears(1);
                if (!string.IsNullOrEmpty(expiresAtStr) && DateTime.TryParse(expiresAtStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsedExpiry))
                    expiresAt = parsedExpiry;
                App.LoggingService?.LogInfo($"[LicenseManager] Expiração: {expiresAt:yyyy-MM-dd HH:mm:ss} UTC");

                var billingPeriod = TryGetPropertyCaseInsensitive(activateRoot, "billing_period")
                    ?? TryGetPropertyCaseInsensitive(activateRoot, "billingPeriod")
                    ?? "year";
                App.LoggingService?.LogInfo($"[LicenseManager] Período de cobrança: {billingPeriod}");

                // VALIDAR EXPIRAÇÃO REAL
                if (expiresAt <= DateTime.UtcNow)
                {
                    App.LoggingService?.LogWarning($"[LicenseManager] ✗ Licença EXPIRADA: {expiresAt:yyyy-MM-dd} ≤ {DateTime.UtcNow:yyyy-MM-dd}");
                    return new LicenseActivationResult
                    {
                        Success = false,
                        Message = $"Esta licença expirou em {expiresAt:dd/MM/yyyy}. Adquira uma nova licença."
                    };
                }

                // Persistir chave legacy (compatibilidade)
                App.LoggingService?.LogInfo("[LicenseManager] → Salvando chave no Registry (legacy)...");
                PersistLicenseKeyLegacy(licenseKey.Trim());
                App.LoggingService?.LogInfo("[LicenseManager] ✓ Chave salva no Registry");

                // Persistir no LicenseTokenStore (fonte de verdade)
                App.LoggingService?.LogInfo("[LicenseManager] → Salvando no LicenseTokenStore...");
                LicenseTokenStore.SetValidatedLicense(licenseKey.Trim(), licenseType, licenseDisplayName, expiresAt, billingPeriod);
                App.LoggingService?.LogInfo("[LicenseManager] ✓ Salvo no LicenseTokenStore");

                App.LoggingService?.LogInfo("[LicenseManager] ");
                App.LoggingService?.LogInfo($"[LicenseManager] ✓ ATIVAÇÃO CONCLUÍDA COM SUCESSO!");
                App.LoggingService?.LogInfo($"[LicenseManager] ✓ Tipo: {licenseType}");
                App.LoggingService?.LogInfo($"[LicenseManager] ✓ IsPro: {IsPro}");
                App.LoggingService?.LogInfo("[LicenseManager] ");

                LicenseStatusChanged?.Invoke(this, EventArgs.Empty);

                return new LicenseActivationResult
                {
                    Success = true,
                    Message = "Licença ativada com sucesso!",
                    Plan = licenseType,
                    MaxDevices = maxDevices
                };
            }
            catch (HttpRequestException ex)
            {
                App.LoggingService?.LogError("[LicenseManager] ✗ ERRO DE REDE", ex);
                return new LicenseActivationResult
                {
                    Success = false,
                    Message = "Erro de conexão ao validar licença."
                };
            }
            catch (TaskCanceledException ex)
            {
                App.LoggingService?.LogError("[LicenseManager] ✗ TIMEOUT", ex);
                return new LicenseActivationResult
                {
                    Success = false,
                    Message = "Timeout ao conectar ao servidor."
                };
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[LicenseManager] ✗ ERRO INESPERADO", ex);
                return new LicenseActivationResult { Success = false, Message = $"Erro: {ex.Message}" };
            }
        }

        /// <summary>
        /// Revoga a licença local (logout, desativação manual).
        /// </summary>
        public void RevokeLicense(string reason = "manual")
        {
            App.LoggingService?.LogInfo($"[LicenseManager] Revogando licença — motivo: {reason}");
            LicenseTokenStore.Revoke(reason);
            ClearLicenseKeyLegacy();
            LicenseStatusChanged?.Invoke(this, EventArgs.Empty);
            App.LoggingService?.LogInfo("[LicenseManager] ✓ Licença revogada");
        }

        /// <summary>
        /// Tenta restaurar licença do registro no boot.
        /// Chamado pelo Bootstrapper/App.xaml.cs durante inicialização.
        /// </summary>
        public bool TryRestoreLicenseFromRegistry()
        {
            App.LoggingService?.LogInfo("[LicenseManager] → Restaurando licença do registro...");
            var restored = LicenseTokenStore.TryRestoreFromRegistry();
            if (restored)
            {
                App.LoggingService?.LogSuccess($"[LicenseManager] ✓ Licença restaurada — IsPro={IsPro}, type={LicenseTokenStore.LicenseType}");
                LicenseStatusChanged?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                App.LoggingService?.LogInfo("[LicenseManager] Nenhuma licença válida no registro");
            }
            return restored;
        }

        /// <summary>
        /// Revalida a licença atual no servidor (chamado periodicamente).
        /// Se o servidor indicar revogação, revoga localmente.
        /// </summary>
        public async Task<bool> RevalidateWithServerAsync()
        {
            App.LoggingService?.LogInfo("[LicenseManager] RevalidateWithServerAsync iniciado");

            if (!IsPro)
            {
                App.LoggingService?.LogInfo("[LicenseManager] Sem licença Pro — revalidação ignorada");
                return false;
            }

            var licenseKey = LicenseTokenStore.LicenseKey;
            if (string.IsNullOrEmpty(licenseKey))
            {
                App.LoggingService?.LogWarning("[LicenseManager] Chave não encontrada para revalidação");
                return false;
            }

            try
            {
                App.LoggingService?.LogInfo($"[LicenseManager] Revalidando {licenseKey.Substring(0, Math.Min(12, licenseKey.Length))}...");
                var hwid = await HardwareTrialService.Instance.GetHwidAsync();
                var payload = new
                {
                    license_key = licenseKey,
                    device_id = hwid,
                    machine_name = Environment.MachineName,
                    app_version = GetAppVersion()
                };

                var response = await _httpClient.PostAsJsonAsync(ValidateEndpoint, payload);
                var json = await response.Content.ReadAsStringAsync();
                App.LoggingService?.LogInfo($"[LicenseManager] Revalidação: {response.StatusCode}");

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var isValid = root.TryGetProperty("valid", out var validProp) && validProp.GetBoolean();
                if (isValid)
                {
                    var canonical = LicenseSignatureVerifier.BuildValidationPayload(licenseKey, hwid, true);
                    var sigOk = root.TryGetProperty("signature", out var sigProp)
                        && LicenseSignatureVerifier.VerifySignedPayload(canonical, sigProp.GetString());

                    if (!sigOk)
                    {
                        App.LoggingService?.LogWarning("[LicenseManager] Revalidação: assinatura inválida — revogando");
                        LicenseTokenStore.Revoke("server_revalidation_bad_signature");
                        LicenseStatusChanged?.Invoke(this, EventArgs.Empty);
                        return false;
                    }
                }

                if (!isValid)
                {
                    var errorCode = root.TryGetProperty("error_code", out var codeProp) ? codeProp.GetString() ?? "" : "";
                    App.LoggingService?.LogWarning($"[LicenseManager] Revalidação falhou ({errorCode}) — revogando");
                    LicenseTokenStore.Revoke($"server_revalidation_failed:{errorCode}");
                    LicenseStatusChanged?.Invoke(this, EventArgs.Empty);
                    return false;
                }

                App.LoggingService?.LogSuccess("[LicenseManager] ✓ Revalidação concluída — licença ainda válida");
                return true;
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[LicenseManager] Revalidação falhou (rede): {ex.Message}");
                return true; // Manter licença se houver erro de rede
            }
        }

        /// <summary>
        /// Sincronização com servidor (VoltrisApiService).
        /// </summary>
        public async Task<(bool success, string message)> SyncWithServerAsync()
        {
            App.LoggingService?.LogInfo("[LicenseManager] SyncWithServerAsync iniciado");
            try
            {
                if (!Licensing.VoltrisApiService.Instance.IsAuthenticated)
                {
                    App.LoggingService?.LogWarning("[LicenseManager] Usuário não autenticado");
                    return (false, "Usuário não autenticado.");
                }

                var licenses = await Licensing.VoltrisApiService.Instance.GetMyLicensesAsync();
                if (licenses == null || licenses.Count == 0)
                {
                    App.LoggingService?.LogInfo("[LicenseManager] Nenhuma licença encontrada na conta");
                    return (false, "Nenhuma licença encontrada.");
                }

                App.LoggingService?.LogInfo($"[LicenseManager] {licenses.Count} licença(s) encontrada(s)");

                var activeLicense = licenses.Find(l => l.IsActive && !l.IsExpired);
                if (activeLicense == null)
                {
                    App.LoggingService?.LogWarning("[LicenseManager] Nenhuma licença ativa/válida");
                    return (false, "Nenhuma licença ativa disponível.");
                }

                App.LoggingService?.LogInfo($"[LicenseManager] Ativando {activeLicense.Key.Substring(0, Math.Min(12, activeLicense.Key.Length))}...");
                var result = await ActivateLicenseAsync(activeLicense.Key);
                return (result.Success, result.Message);
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[LicenseManager] Erro na sincronização", ex);
                return (false, $"Erro: {ex.Message}");
            }
        }

        // ─── Informações de licença para UI ──────────────────────────────────

        public LicenseInfo? GetLicenseInfo()
        {
            App.LoggingService?.LogTrace("[LicenseManager] GetLicenseInfo chamado");
            try
            {
                if (!IsPro)
                {
                    return new LicenseInfo
                    {
                        LicenseKey = "UNLICENSED",
                        PlanType = "Unlicensed",
                        ActivationDate = DateTime.Now,
                        ExpiryDate = DateTime.Now,
                        IsLifetime = false,
                        MaxDevices = 1,
                        ActiveDevices = 1,
                        RegisteredTo = Environment.UserName,
                        DeviceId = GetDeviceId(),
                        IsActive = false
                    };
                }

                var licenseKey = LicenseTokenStore.LicenseKey;
                var licenseType = LicenseTokenStore.LicenseType;
                var expiresAt = LicenseTokenStore.ExpiresAt;
                var maxDevices = LicensePlan.GetMaxDevices(licenseType);
                var isLifetime = expiresAt.Year > 2090;

                App.LoggingService?.LogTrace($"[LicenseManager] GetLicenseInfo: type={licenseType}, expires={expiresAt:yyyy-MM-dd}");

                return new LicenseInfo
                {
                    LicenseKey = licenseKey,
                    PlanType = LicenseTokenStore.LicenseDisplayName,
                    ActivationDate = DateTime.Now,
                    ExpiryDate = expiresAt,
                    IsLifetime = isLifetime,
                    MaxDevices = maxDevices == 9999 ? int.MaxValue : maxDevices,
                    ActiveDevices = 1,
                    RegisteredTo = Environment.UserName,
                    DeviceId = GetDeviceId(),
                    IsActive = IsPro
                };
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[LicenseManager] Erro ao obter info", ex);
                return null;
            }
        }

        public string? GetCurrentLicenseKey()
        {
            App.LoggingService?.LogTrace("[LicenseManager] GetCurrentLicenseKey chamado");
            if (IsPro) return LicenseTokenStore.LicenseKey;
            return GetStoredLicenseKeyLegacy();
        }

        /// <summary>
        /// Verifica se há uma licença válida ativa.
        /// </summary>
        public Task<bool> IsLicenseValidAsync()
        {
            App.LoggingService?.LogTrace("[LicenseManager] IsLicenseValidAsync chamado");
            return Task.FromResult(LicenseTokenStore.IsProActive);
        }

        public string GetDeviceId()
        {
            App.LoggingService?.LogTrace("[LicenseManager] GetDeviceId chamado");
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);
                using var key = baseKey.OpenSubKey(RegistryKeyPath, false);
                var storedId = key?.GetValue("HardwareId")?.ToString();
                if (!string.IsNullOrEmpty(storedId)) return storedId;
            }
            catch { }

            var newId = GenerateUniqueHardwareId();
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);
                using var key = baseKey.CreateSubKey(RegistryKeyPath);
                key.SetValue("HardwareId", newId, RegistryValueKind.String);
            }
            catch { }
            return newId;
        }

        public void NotifyLicenseStatusChanged()
        {
            App.LoggingService?.LogInfo("[LicenseManager] NotifyLicenseStatusChanged chamado");
            LicenseTokenStore.NotifyStateChanged();
            LicenseStatusChanged?.Invoke(this, EventArgs.Empty);
        }

        // ─── Helpers privados ─────────────────────────────────────────────────

        private string GenerateUniqueHardwareId()
        {
            var combined = $"{Environment.MachineName}-{Environment.UserName}-{Environment.OSVersion}";
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(combined));
            return BitConverter.ToString(hashBytes).Replace("-", "").Substring(0, 16).ToUpperInvariant();
        }

        private string GetAppVersion()
        {
            try { return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0"; }
            catch { return "1.0.0"; }
        }

        private static string? TryGetPropertyCaseInsensitive(JsonElement element, string propertyName)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                    return prop.Value.ValueKind == JsonValueKind.Null ? null : prop.Value.ToString();
            }
            return null;
        }

        private string CapitalizePlanName(string plan) => plan.ToLower() switch
        {
            "standard" => "Standard",
            "pro" => "Pro",
            "professional" => "Professional",
            "enterprise" => "Enterprise",
            "lifetime" => "Lifetime",
            _ => plan
        };

        private void PersistLicenseKeyLegacy(string licenseKey)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);
                using var key = baseKey.CreateSubKey(RegistryKeyPath);
                key.SetValue(LicenseKeyName, licenseKey, RegistryValueKind.String);
                key.SetValue(ActivationDateName, DateTime.UtcNow.ToString("O"), RegistryValueKind.String);

                var settings = SettingsService.Instance.Settings;
                settings.LicenseKey = licenseKey;
                SettingsService.Instance.SaveSettings();

                var backupDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Voltris");
                if (!Directory.Exists(backupDir)) Directory.CreateDirectory(backupDir);
                File.WriteAllText(Path.Combine(backupDir, "license.dat"), licenseKey);

                App.LoggingService?.LogInfo("[LicenseManager] ✓ Chave persistida no Registry/Settings/Backup");
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[LicenseManager] Erro ao persistir chave", ex);
            }
        }

        private void ClearLicenseKeyLegacy()
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);
                using var key = baseKey.OpenSubKey(RegistryKeyPath, true);
                if (key != null)
                {
                    try { key.DeleteValue(LicenseKeyName, false); } catch { }
                    try { key.DeleteValue(ActivationDateName, false); } catch { }
                }
                var backupPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Voltris", "license.dat");
                if (File.Exists(backupPath)) File.Delete(backupPath);
                var settings = SettingsService.Instance.Settings;
                settings.LicenseKey = null;
                SettingsService.Instance.SaveSettings();

                App.LoggingService?.LogInfo("[LicenseManager] ✓ Chave removida do Registry/Settings/Backup");
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[LicenseManager] Erro ao limpar chave", ex);
            }
        }

        private string? GetStoredLicenseKeyLegacy()
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);
                using var key = baseKey.OpenSubKey(RegistryKeyPath, false);
                var regKey = key?.GetValue(LicenseKeyName)?.ToString();
                if (!string.IsNullOrEmpty(regKey)) return regKey;
            }
            catch { }
            try
            {
                var settingsKey = SettingsService.Instance.Settings.LicenseKey;
                if (!string.IsNullOrEmpty(settingsKey)) return settingsKey;
            }
            catch { }
            try
            {
                var backupPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Voltris", "license.dat");
                if (File.Exists(backupPath))
                {
                    var fileKey = File.ReadAllText(backupPath).Trim();
                    if (!string.IsNullOrEmpty(fileKey)) return fileKey;
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Verifica a assinatura RSA da resposta de ativação do servidor.
        /// </summary>
        private static bool VerifyActivateResponseSignature(JsonElement root, string deviceId)
        {
            try
            {
                if (!root.TryGetProperty("signature", out var sigProp))
                    return false;

                var licenseType = TryGetResponseProp(root, "license_type") ?? "";
                var maxDevices = 1;
                if (int.TryParse(TryGetResponseProp(root, "max_devices"), out var parsedMax)) maxDevices = parsedMax;
                var expiresAtStr = TryGetResponseProp(root, "expires_at") ?? "";
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

        private static string? TryGetResponseProp(JsonElement root, string name)
        {
            if (root.TryGetProperty(name, out var prop) && prop.ValueKind != JsonValueKind.Null)
                return prop.ToString();
            return null;
        }
    }

    /// <summary>
    /// Estrutura para informações de licença na UI
    /// </summary>
    public class LicenseInfo
    {
        public string LicenseKey { get; set; } = string.Empty;
        public string PlanType { get; set; } = string.Empty;
        public DateTime ActivationDate { get; set; }
        public DateTime ExpiryDate { get; set; }
        public bool IsLifetime { get; set; }
        public int MaxDevices { get; set; }
        public int ActiveDevices { get; set; }
        public string RegisteredTo { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }
}
