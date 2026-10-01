using System;
using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.License
{
    /// <summary>
    /// Armazenamento seguro do token de licença validado pelo servidor.
    /// Substitui o IsPro estático — o estado Pro só pode ser definido
    /// por uma resposta assinada do servidor, nunca por código local.
    /// </summary>
    internal static class LicenseTokenStore
    {
        private const string RegistryPath = @"SOFTWARE\Voltris\Optimizer\Token";
        private const string TokenValueName = "LT";
        private const string ExpiryValueName = "LE";
        private const string TypeValueName = "LTY";
        private const string DisplayNameValueName = "LDN";
        private const string BillingPeriodValueName = "BP";

        // Chave de proteção derivada do hardware — impede cópia entre máquinas
        private static readonly Lazy<byte[]> _machineKey = new(() => DeriveMachineKey());

        // ─── Estado em memória (única fonte de verdade em runtime) ───────────
        private static bool _isProActive;
        private static string _licenseType = "None";
        private static string _licenseDisplayName = "";
        private static DateTime _expiresAt = DateTime.MinValue;
        private static string _licenseKey = "";
        private static string _billingPeriod = "year";
        private static readonly object _lock = new();

        // ─── Notificação de Alteração de Propriedades Estáticas (WPF DataBinding) ───
        public static event EventHandler<PropertyChangedEventArgs>? StaticPropertyChanged;
        public static event EventHandler? IsProActiveChanged;

        internal static void NotifyStateChanged()
        {
            try
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.BeginInvoke(new Action(NotifyStateChanged));
                    return;
                }

                StaticPropertyChanged?.Invoke(null, new PropertyChangedEventArgs(nameof(IsProActive)));
                StaticPropertyChanged?.Invoke(null, new PropertyChangedEventArgs(nameof(LicenseType)));
                StaticPropertyChanged?.Invoke(null, new PropertyChangedEventArgs(nameof(LicenseDisplayName)));
                IsProActiveChanged?.Invoke(null, EventArgs.Empty);
            }
            catch { }
        }

        // ─── Propriedades públicas (somente leitura) ─────────────────────────

        /// <summary>Licença Pro/Standard/Enterprise ativa e não expirada.</summary>
        public static bool IsProActive
        {
            get
            {
                if (!_isProActive) return false;
                if (_expiresAt != DateTime.MinValue && DateTime.UtcNow > _expiresAt)
                {
                    lock (_lock)
                    {
                        if (_isProActive && DateTime.UtcNow > _expiresAt)
                        {
                            _isProActive = false;
                            _licenseType = "None";
                            NotifyStateChanged();
                        }
                    }
                    return false;
                }
                return _isProActive;
            }
        }

        public static string LicenseType { get { lock (_lock) return _licenseType; } }
        public static string LicenseDisplayName { get { lock (_lock) return string.IsNullOrEmpty(_licenseDisplayName) ? CapitalizePlanName(_licenseType) : _licenseDisplayName; } }
        public static DateTime ExpiresAt { get { lock (_lock) return _expiresAt; } }
        public static string LicenseKey { get { lock (_lock) return _licenseKey; } }
        public static string BillingPeriod { get { lock (_lock) return _billingPeriod; } }

        // ─── Métodos de escrita (apenas chamados após validação server-side) ──

        /// <summary>
        /// Persiste o token de licença validado pelo servidor.
        /// Só deve ser chamado por LicenseManager.ActivateLicenseAsync após
        /// confirmação positiva da Edge Function validate-license.
        /// </summary>
        internal static void SetValidatedLicense(string licenseKey, string licenseType, string licenseDisplayName, DateTime expiresAt, string billingPeriod = "year")
        {
            App.LoggingService?.LogInfo($"[LicenseTokenStore] SetValidatedLicense - Tipo: {licenseType}, Expira: {expiresAt:yyyy-MM-dd HH:mm:ss}, Período: {billingPeriod}");
            
            lock (_lock)
            {
                _isProActive = true;
                _licenseType = licenseType;
                _licenseDisplayName = licenseDisplayName;
                _expiresAt = expiresAt;
                _licenseKey = licenseKey;
                _billingPeriod = billingPeriod ?? "year";
                App.LoggingService?.LogInfo($"[LicenseTokenStore] Estado atualizado: IsPro={_isProActive}, Type={_licenseType}, ExpiresAt={_expiresAt:yyyy-MM-dd HH:mm:ss}, BillingPeriod={_billingPeriod}");
            }
            
            PersistToRegistry(licenseKey, licenseType, licenseDisplayName, expiresAt, billingPeriod);
            LicenseJsonStore.Save(licenseType, licenseDisplayName, licenseKey, expiresAt, billingPeriod);
            App.LoggingService?.LogSuccess($"[LicenseTokenStore] Licença ativada e persistida com sucesso (Registry + JSON)");
            
            // *** CORREÇÃO CRÍTICA: Invalidar cache do orquestrador imediatamente
            try
            {
                App.LoggingService?.LogInfo($"[LicenseTokenStore] Invalidando cache do LicenseOrchestrationService...");
                LicenseOrchestrationService.Instance.InvalidateCache();
                
                // Invalida também o backend service
                var licenseService = App.Services?.GetService(typeof(VoltrisOptimizer.Services.License.Interfaces.ILicenseService)) as VoltrisOptimizer.Services.License.Interfaces.ILicenseService;
                licenseService?.InvalidateCacheAsync();
                
                App.LoggingService?.LogSuccess($"[LicenseTokenStore] Caches invalidados com sucesso");
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[LicenseTokenStore] Erro ao invalidar cache: {ex.Message}", ex);
            }

            NotifyStateChanged();
        }

        /// <summary>
        /// Define licença temporária com validação pendente (fallback profissional).
        /// Usado quando servidor está offline - expira em 7 dias e requer validação online.
        /// </summary>
        internal static void SetPendingValidationLicense(string licenseKey, string licenseType, DateTime expiresAt)
        {
            App.LoggingService?.LogInfo($"[LicenseTokenStore] 🔥 SetPendingValidationLicense INICIADO");
            App.LoggingService?.LogInfo($"[LicenseTokenStore] Chave: {licenseKey.Substring(0, Math.Min(12, licenseKey.Length))}...");
            App.LoggingService?.LogInfo($"[LicenseTokenStore] Tipo: {licenseType}, Expira: {expiresAt:yyyy-MM-dd HH:mm:ss}");

            lock (_lock)
            {
                _isProActive = true;
                _licenseType = licenseType; // Tipo real sem "Pendente"
                _expiresAt = expiresAt; // 7 dias
                _licenseKey = licenseKey;

                App.LoggingService?.LogInfo($"[LicenseTokenStore] Estado temporário definido: IsPro={_isProActive}, Type={_licenseType}");

                // Persistir no registro (criptografado)
                PersistToRegistry(licenseKey, licenseType + "_PENDING", CapitalizePlanName(licenseType), expiresAt);
            }

            App.LoggingService?.LogWarning($"[LicenseTokenStore] ⚠️ Licença temporária definida - {licenseType} até {expiresAt:yyyy-MM-dd} (validação online pendente)");
            NotifyStateChanged();
        }

        /// <summary>Revoga o estado Pro (logout, revogação remota, expiração).</summary>
        internal static void Revoke(string reason = "manual")
        {
            App.LoggingService?.LogInfo($"[LicenseTokenStore] Revoke - motivo: {reason}");
            App.LoggingService?.LogInfo($"[LicenseTokenStore] Estado ANTES de Revoke: IsProActive={_isProActive}, Type={_licenseType}");
            
            lock (_lock)
            {
                _isProActive = false;
                _licenseType = "None";
                _expiresAt = DateTime.MinValue;
                _licenseKey = "";
                _licenseDisplayName = "";
            }
            
            // DELETAR PERSISTÊNCIA - Confirm deletion
            App.LoggingService?.LogInfo($"[LicenseTokenStore] Iniciando limpeza de persistência (Registry + JSON)...");
            ClearRegistry();
            LicenseJsonStore.Clear();
            App.LoggingService?.LogSuccess($"[LicenseTokenStore] Persistência deletada com sucesso");
            
            // Verificar se realmente foi deletado (debug)
            var (checkType, _, checkKey, _, _) = LicenseJsonStore.Load();
            if (!string.IsNullOrEmpty(checkKey))
            {
                App.LoggingService?.LogError($"[LicenseTokenStore] ⚠️ AVISO: Licença ainda existe em JSON após deletar! Key={checkKey.Substring(0, 12)}...");
            }
            else
            {
                App.LoggingService?.LogSuccess($"[LicenseTokenStore] ✓ JSON confirmado como vazio após deleção");
            }
            
            App.LoggingService?.LogInfo($"[LicenseTokenStore] Estado DEPOIS de Revoke: IsProActive={_isProActive}, Type={_licenseType}");
            
            // *** CORREÇÃO CRÍTICA: Invalidar cache do orquestrador e do backend imediatamente
            try
            {
                App.LoggingService?.LogInfo($"[LicenseTokenStore] Invalidando cache do LicenseOrchestrationService (Revoke)...");
                LicenseOrchestrationService.Instance.InvalidateCache();
                
                // Invalida também o backend service (garantindo que LicenseGuard falhe imediatamente)
                var licenseService = App.Services?.GetService(typeof(VoltrisOptimizer.Services.License.Interfaces.ILicenseService)) as VoltrisOptimizer.Services.License.Interfaces.ILicenseService;
                licenseService?.InvalidateCacheAsync();
                
                App.LoggingService?.LogSuccess($"[LicenseTokenStore] Caches invalidados com sucesso (Revoke)");
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[LicenseTokenStore] Erro ao invalidar caches (Revoke): {ex.Message}", ex);
            }

            NotifyStateChanged();
        }

        /// <summary>
        /// Tenta restaurar o estado a partir do Registro e JSON (com reparo cruzado).
        /// Chamado no boot para garantir que o estado Pro seja mantido.
        /// </summary>
        internal static bool TryRestoreFromRegistry()
        {
            App.LoggingService?.LogInfo("[LicenseTokenStore] Iniciando restauração de licença (Multi-Source)...");
            
            // VERIFICAÇÃO CRÍTICA: Se TryRestoreFromRegistry foi chamado múltiplas vezes,
            // devemos ignorar chamadas subsequentes para evitar restauração indesejada
            lock (_lock)
            {
                if (_isProActive)
                {
                    App.LoggingService?.LogInfo("[LicenseTokenStore] Licença já está ativa em memória, ignorando restauração");
                    return true;
                }
            }

            // ⚠️ GUARD: Detectar se licença foi explicitamente deletada/desativada
            // Se a licença foi revogada via LicenseManager.ResetTrial() -> TrialProtectionService.ClearTrialData(),
            // NÃO devemos restaurá-la do Registry, mesmo que dados ainda existam lá.
            if (WasLicenseExplicitlyCleared())
            {
                App.LoggingService?.LogWarning("[LicenseTokenStore] ⚠️ GUARD: Licença foi explicitamente deletada — bloqueando restauração");
                return false;
            }
            
            bool restoredFromJson = false;
            bool restoredFromRegistry = false;
            
            LicenseStorageData? jsonData = null;
            LicenseStorageData? registryData = null;

            try
            {
                // PASSO 1: Tentar carregar do JSON (Resiliente a atualizações)
                var (jsonType, jsonDisplay, jsonKey, jsonExpires, jsonBilling) = LicenseJsonStore.Load();
                if (!string.IsNullOrEmpty(jsonKey) && !string.IsNullOrEmpty(jsonType))
                {
                    // ── VERIFICAÇÃO DE ASSINATURA (segurança) ──
                    // A chave armazenada só é confiável se tiver assinatura RSA válida.
                    // Sem isso, qualquer edição manual do JSON não restaura Pro.
                    if (!LicenseSignatureVerifier.VerifyLicenseKey(jsonKey))
                    {
                        App.LoggingService?.LogWarning("[LicenseTokenStore] JSON ignorado: chave de licença com assinatura inválida (possível adulteração).");
                    }
                    else
                    {
                        jsonData = new LicenseStorageData 
                        { 
                            LicenseType = jsonType, 
                            LicenseDisplayName = jsonDisplay ?? "", 
                            LicenseKey = jsonKey, 
                            ExpiresAt = jsonExpires ?? DateTime.MinValue,
                            BillingPeriod = jsonBilling ?? "year"
                        };
                        
                        // A data embutida na chave é autoritativa como TETO (não pode ser
                        // forjada). A expiração armazenada nunca pode excedê-la.
                        if (TryExtractKeyExpiry(jsonKey, out var jsonKeyExpiry))
                            jsonData.ExpiresAt = MinNonZero(jsonData.ExpiresAt, jsonKeyExpiry);

                        if (jsonData.ExpiresAt > DateTime.UtcNow || jsonData.ExpiresAt == DateTime.MinValue)
                        {
                            restoredFromJson = true;
                            App.LoggingService?.LogInfo($"[LicenseTokenStore] Dados carregados do JSON (Expira: {jsonData.ExpiresAt:yyyy-MM-dd}, Período: {jsonData.BillingPeriod})");
                        }
                        else
                        {
                            App.LoggingService?.LogWarning($"[LicenseTokenStore] Licença no JSON expirou em {jsonData.ExpiresAt:yyyy-MM-dd}");
                        }
                    }
                }
                else
                {
                    App.LoggingService?.LogInfo("[LicenseTokenStore] Nenhuma licença encontrada em JSON ou chave/tipo vazios");
                }

                // PASSO 2: Tentar carregar do Registro (Criptografado e mais difícil de manipular)
                registryData = LoadFromRegistry();
                if (registryData != null)
                {
                    // ── VERIFICAÇÃO DE ASSINATURA (segurança) ──
                    if (!LicenseSignatureVerifier.VerifyLicenseKey(registryData.LicenseKey))
                    {
                        App.LoggingService?.LogWarning("[LicenseTokenStore] Registro ignorado: chave de licença com assinatura inválida (possível adulteração).");
                        registryData = null;
                    }
                    else
                    {
                        // Data embutida na chave é autoritativa como TETO
                        if (TryExtractKeyExpiry(registryData.LicenseKey, out var regKeyExpiry))
                            registryData.ExpiresAt = MinNonZero(registryData.ExpiresAt, regKeyExpiry);

                        if (registryData.ExpiresAt > DateTime.UtcNow)
                        {
                            restoredFromRegistry = true;
                            App.LoggingService?.LogInfo($"[LicenseTokenStore] Dados carregados do Registro (Expira: {registryData.ExpiresAt:yyyy-MM-dd})");
                        }
                        else
                        {
                            App.LoggingService?.LogWarning($"[LicenseTokenStore] Licença no Registro expirou em {registryData.ExpiresAt:yyyy-MM-dd}");
                        }
                    }
                }

                // PASSO 3: Lógica de Decisão e Reparo Cruzado
                if (restoredFromJson || restoredFromRegistry)
                {
                    // Priorizar Registry se ambos existirem (é criptografado por HWID)
                    var bestData = restoredFromRegistry ? (object)registryData! : (object)jsonData!;
                    
                    string type, display, key, billingPeriod;
                    DateTime expires;

                    if (restoredFromRegistry)
                    {
                        type = registryData!.LicenseType;
                        display = registryData.LicenseDisplayName;
                        key = registryData.LicenseKey;
                        expires = registryData.ExpiresAt;
                        billingPeriod = registryData.BillingPeriod;
                    }
                    else
                    {
                        type = jsonData!.LicenseType;
                        display = jsonData.LicenseDisplayName;
                        key = jsonData.LicenseKey;
                        expires = jsonData.ExpiresAt;
                        billingPeriod = jsonData.BillingPeriod;
                    }

                    lock (_lock)
                    {
                        _isProActive = true;
                        _licenseType = type;
                        _licenseDisplayName = display;
                        _expiresAt = expires;
                        _licenseKey = key;
                        _billingPeriod = billingPeriod ?? "year";
                    }

                    // REPARO: Se um falhou mas o outro funcionou, sincronizar agora
                    if (!restoredFromJson)
                    {
                        App.LoggingService?.LogInfo("[LicenseTokenStore] REPARO: Restaurado via Registro, salvando backup em JSON...");
                        LicenseJsonStore.Save(_licenseType, _licenseDisplayName, _licenseKey, _expiresAt, _billingPeriod);
                    }
                    else if (!restoredFromRegistry)
                    {
                        App.LoggingService?.LogInfo("[LicenseTokenStore] REPARO: Restaurado via JSON, restaurando entrada no Registro...");
                        PersistToRegistry(_licenseKey, _licenseType, _licenseDisplayName, _expiresAt, _billingPeriod);
                    }

                    App.LoggingService?.LogSuccess($"[LicenseTokenStore] ✅ Licença RESTAURADA com sucesso: {_licenseDisplayName} ({_licenseType})");
                    NotifyStateChanged();
                    return true;
                }

                App.LoggingService?.LogInfo("[LicenseTokenStore] Nenhuma licença válida encontrada em nenhuma fonte de persistência.");
                return false;
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[LicenseTokenStore] ❌ Erro crítico ao restaurar licença", ex);
                return false;
            }
        }

        private class LicenseStorageData
        {
            public string LicenseType { get; set; } = "";
            public string LicenseDisplayName { get; set; } = "";
            public string LicenseKey { get; set; } = "";
            public DateTime ExpiresAt { get; set; } = DateTime.MinValue;
            public string BillingPeriod { get; set; } = "year";
        }

        private static LicenseStorageData? LoadFromRegistry()
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);
                using var key = baseKey?.OpenSubKey(RegistryPath, false);
                if (key == null) return null;

                var encToken = key.GetValue(TokenValueName)?.ToString();
                var encExpiry = key.GetValue(ExpiryValueName)?.ToString();
                var encType = key.GetValue(TypeValueName)?.ToString();
                var encDisplay = key.GetValue(DisplayNameValueName)?.ToString();
                var encBillingPeriod = key.GetValue(BillingPeriodValueName)?.ToString();

                if (string.IsNullOrEmpty(encToken) || string.IsNullOrEmpty(encExpiry) || string.IsNullOrEmpty(encType))
                    return null;

                var licenseKey = DecryptValue(encToken);
                var expiryStr = DecryptValue(encExpiry);
                var licenseType = DecryptValue(encType);
                var licenseDisplayName = string.IsNullOrEmpty(encDisplay) ? "" : DecryptValue(encDisplay);
                var billingPeriod = string.IsNullOrEmpty(encBillingPeriod) ? "year" : DecryptValue(encBillingPeriod);

                if (string.IsNullOrEmpty(licenseKey) || string.IsNullOrEmpty(licenseType))
                    return null;

                if (!DateTime.TryParse(expiryStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var expiresAt))
                    return null;

                return new LicenseStorageData
                {
                    LicenseKey = licenseKey,
                    LicenseType = licenseType.Replace("_PENDING", ""),
                    LicenseDisplayName = licenseDisplayName.Contains("_PENDING") ? "" : licenseDisplayName,
                    ExpiresAt = expiresAt,
                    BillingPeriod = billingPeriod ?? "year"
                };
            }
            catch { return null; }
        }

        // ─── Persistência criptografada ───────────────────────────────────────

        private static void PersistToRegistry(string licenseKey, string licenseType, string licenseDisplayName, DateTime expiresAt, string billingPeriod = "year")
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);
                using var key = baseKey?.CreateSubKey(RegistryPath, true);
                if (key != null)
                {
                    var encryptedToken = EncryptValue(licenseKey);
                    var encryptedExpiry = EncryptValue(expiresAt.ToString("O"));
                    var encryptedType = EncryptValue(licenseType);
                    var encryptedDisplayName = EncryptValue(licenseDisplayName);
                    var encryptedBillingPeriod = EncryptValue(billingPeriod ?? "year");

                    key.SetValue(TokenValueName, encryptedToken, RegistryValueKind.String);
                    key.SetValue(ExpiryValueName, encryptedExpiry, RegistryValueKind.String);
                    key.SetValue(TypeValueName, encryptedType, RegistryValueKind.String);
                    key.SetValue(DisplayNameValueName, encryptedDisplayName, RegistryValueKind.String);
                    key.SetValue(BillingPeriodValueName, encryptedBillingPeriod, RegistryValueKind.String);
                    
                    App.LoggingService?.LogSuccess($"[LicenseTokenStore] Licença persistida no registro com sucesso");
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[LicenseTokenStore] Erro ao persistir licença no registro", ex);
                throw;
            }
        }

        private static void ClearRegistry()
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);
                using var key = baseKey.OpenSubKey(RegistryPath, true);
                if (key != null)
                {
                    try { key.DeleteValue(TokenValueName, false); } catch { }
                    try { key.DeleteValue(ExpiryValueName, false); } catch { }
                    try { key.DeleteValue(TypeValueName, false); } catch { }
                    try { key.DeleteValue(DisplayNameValueName, false); } catch { }
                }
                App.LoggingService?.LogSuccess("[LicenseTokenStore] Registro de licença limpo com sucesso");
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[LicenseTokenStore] Erro ao limpar registro de licença", ex);
            }
        }

        /// <summary>
        /// Limpa completamente o registro e força reativação para obter dados novos do backend
        /// </summary>
        internal static void ForceCleanReactivation()
        {
            App.LoggingService?.LogWarning("[LicenseTokenStore] ForceCleanReactivation - limpando dados antigos");
            
            lock (_lock)
            {
                _isProActive = false;
                _licenseType = "None";
                _licenseDisplayName = "";
                _expiresAt = DateTime.MinValue;
                _licenseKey = "";
            }
            
            ClearRegistry();
            App.LoggingService?.LogSuccess("[LicenseTokenStore] Dados antigos limpos - pronto para reativação com dados novos");
            NotifyStateChanged();
        }

        // ─── Criptografia derivada de hardware ───────────────────────────────

        private static byte[] DeriveMachineKey()
        {
            try
            {
                // Derivar chave a partir de identificadores de hardware estáveis
                var machineGuid = "";
                try
                {
                    using var regKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                    machineGuid = regKey?.GetValue("MachineGuid")?.ToString() ?? "";
                }
                catch { }

                var seed = $"VOLTRIS_TOKEN_{machineGuid}_{Environment.MachineName}";
                using var sha256 = SHA256.Create();
                return sha256.ComputeHash(Encoding.UTF8.GetBytes(seed));
            }
            catch
            {
                // Fallback determinístico
                using var sha256 = SHA256.Create();
                return sha256.ComputeHash(Encoding.UTF8.GetBytes($"VOLTRIS_FALLBACK_{Environment.MachineName}"));
            }
        }

        private static string EncryptValue(string plainText)
        {
            try
            {
                var key = _machineKey.Value;
                var iv = new byte[16];
                // IV derivado dos primeiros 16 bytes da chave + salt fixo
                Buffer.BlockCopy(key, 0, iv, 0, 16);
                iv[0] ^= 0xAB; iv[7] ^= 0xCD; iv[15] ^= 0xEF;

                using var aes = Aes.Create();
                aes.Key = key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using var encryptor = aes.CreateEncryptor();
                var plainBytes = Encoding.UTF8.GetBytes(plainText);
                var encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
                return Convert.ToBase64String(encryptedBytes);
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[LicenseTokenStore] Erro ao criptografar valor", ex);
                return "";
            }
        }

        private static string DecryptValue(string cipherText)
        {
            try
            {
                if (string.IsNullOrEmpty(cipherText)) return "";

                // FIX: Sanitizar Base64 antes de converter — previne FormatException
                // de dados corrompidos no Registro (padding inválido, whitespace, etc.)
                var sanitized = SanitizeBase64(cipherText);
                if (string.IsNullOrEmpty(sanitized)) return "";

                var key = _machineKey.Value;
                var iv = new byte[16];
                Buffer.BlockCopy(key, 0, iv, 0, 16);
                iv[0] ^= 0xAB; iv[7] ^= 0xCD; iv[15] ^= 0xEF;

                using var aes = Aes.Create();
                aes.Key = key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using var decryptor = aes.CreateDecryptor();
                var cipherBytes = Convert.FromBase64String(sanitized);
                var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch (FormatException fmtEx)
            {
                // Base64 corrompido no Registro — limpar entrada para forçar reativação limpa
                App.LoggingService?.LogWarning($"[LicenseTokenStore] Base64 corrompido no Registro (FormatException): {fmtEx.Message} — dados ignorados");
                return "";
            }
            catch (CryptographicException cryptoEx)
            {
                // Dados criptografados inválidos (tampering, mudança de hardware, etc.)
                App.LoggingService?.LogWarning($"[LicenseTokenStore] Erro de criptografia — possível tampering ou mudança de HWID: {cryptoEx.Message}");
                return "";
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[LicenseTokenStore] Erro ao descriptografar valor — possível tampering", ex);
                return "";
            }
        }

        /// <summary>
        /// Sanitiza string Base64: remove whitespace, caracteres de controle,
        /// e corrige padding (mod 4) para prevenir FormatException.
        /// </summary>
        private static string SanitizeBase64(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";

            // Remover whitespace e caracteres de controle
            var sb = new StringBuilder(input.Length);
            foreach (var c in input)
            {
                if (c == '+' || c == '/' || c == '=' || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                    sb.Append(c);
                // Ignorar silenciosamente whitespace e outros caracteres inválidos
            }

            var cleaned = sb.ToString();
            if (cleaned.Length == 0) return "";

            // Corrigir padding: Base64 requer comprimento múltiplo de 4
            var remainder = cleaned.Length % 4;
            if (remainder > 0)
                cleaned += new string('=', 4 - remainder);

            return cleaned;
        }

        /// <summary>
        /// Limpa todos os dados em memória e força resincronização
        /// </summary>
        internal static void ClearLocalData()
        {
            lock (_lock)
            {
                App.LoggingService?.LogInfo("[LicenseTokenStore] LIMPANDO DADOS LOCAIS...");
                App.LoggingService?.LogInfo($"[LicenseTokenStore] Estado ANTES: IsPro={_isProActive}, Type={_licenseType}, Expires={_expiresAt:yyyy-MM-dd HH:mm:ss}");
                
                // Resetar estado em memória
                _isProActive = false;
                _licenseType = "None";
                _licenseDisplayName = "";
                _expiresAt = DateTime.MinValue;
                _licenseKey = "";
                
                App.LoggingService?.LogInfo($"[LicenseTokenStore] Estado DEPOIS: IsPro={_isProActive}, Type={_licenseType}, Expires={_expiresAt:yyyy-MM-dd HH:mm:ss}");
                App.LoggingService?.LogInfo("[LicenseTokenStore] Dados locais limpos com sucesso - próximo acesso forçará sincronização");
            }
            NotifyStateChanged();
        }

        /// <summary>
        /// Capitaliza o nome do plano (fallback se não vier do backend)
        /// </summary>
        private static string CapitalizePlanName(string plan) => plan.ToLower() switch
        {
            "trial" => "Trial",
            "standard" => "Standard",
            "pro" => "Pro",
            "enterprise" => "Enterprise",
            _ => plan
        };

        /// <summary>
        /// Extrai a data de validade embutida numa chave de licença VÁLIDA.
        /// Formato: VOLTRIS-PLANO-ID-DATA-SIG. Deve ser chamado somente após
        /// LicenseSignatureVerifier.VerifyLicenseKey retornar true.
        /// </summary>
        private static bool TryExtractKeyExpiry(string licenseKey, out DateTime expiry)
        {
            expiry = DateTime.MinValue;
            try
            {
                var key = licenseKey.Trim().ToUpperInvariant();
                const string prefix = "VOLTRIS-";
                if (!key.StartsWith(prefix, StringComparison.Ordinal)) return false;

                var parts = key.Substring(prefix.Length).Split('-');
                if (parts.Length < 3) return false;
                var dateStr = parts[2];

                if (dateStr.Length == 8 && int.TryParse(dateStr, out var dateInt))
                {
                    var year = dateInt / 10000;
                    var month = (dateInt / 100) % 100;
                    var day = dateInt % 100;
                    if (year >= 2000 && year <= 9999 && month >= 1 && month <= 12 && day >= 1 && day <= 31)
                    {
                        expiry = new DateTime(year, month, day, 23, 59, 59, DateTimeKind.Utc);
                        return true;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Retorna o menor valor, ignorando DateTime.MinValue (vida ilimitada).
        /// Usado para impor a data da chave como teto sobre a expiração armazenada.
        /// </summary>
        private static DateTime MinNonZero(DateTime a, DateTime b)
        {
            if (a == DateTime.MinValue) return b;
            if (b == DateTime.MinValue) return a;
            return a < b ? a : b;
        }

        /// <summary>
        /// Detecta se uma licença foi explicitamente deletada/desativada via ClearTrialData() ou Revoke().
        /// Se trial data e JSON foram explicitamente apagados, não devemos restaurar do Registry.
        /// </summary>
        private static bool WasLicenseExplicitlyCleared()
        {
            try
            {
                // Se JSON Store está vazio E Registry está vazio, significa que foi explicitamente limpo
                var (jsonType, _, jsonKey, _, _) = LicenseJsonStore.Load();
                bool jsonEmpty = string.IsNullOrEmpty(jsonKey) && string.IsNullOrEmpty(jsonType);

                var registryData = LoadFromRegistry();
                bool registryEmpty = registryData == null;

                // Se AMBOS estão vazios, foi explicitamente limpo (não apenas "nunca ativado")
                // Verificamos também se trial files foram deletados
                bool jsonFileExists = File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Voltris", ".trialdata"));
                bool trialFileExists = File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Voltris", ".trial"));

                // Se ambos JSON e Registry estão vazios E ambos arquivos foram deletados,
                // então foi explicitamente limpo
                if (jsonEmpty && registryEmpty && !jsonFileExists && !trialFileExists)
                {
                    App.LoggingService?.LogWarning("[LicenseTokenStore] GUARD: Detectado estado de 'limpeza explícita' - JSON vazio, Registry vazio, arquivos deletados");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[LicenseTokenStore] Erro ao verificar se licença foi explicitamente limpa: {ex.Message}");
                return false;
            }
        }
    }
}
