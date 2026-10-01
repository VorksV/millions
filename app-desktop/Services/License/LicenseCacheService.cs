using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace VoltrisOptimizer.Services.License
{
    /// <summary>
    /// Serviço de cache offline para licenças
    /// Permite uso do programa por 24h sem conexão com internet
    /// </summary>
    public class LicenseCacheService
    {
        private static LicenseCacheService? _instance;
        public static LicenseCacheService Instance => _instance ??= new LicenseCacheService();
        
        private const string CacheFileName = ".license_cache";
        private const string RegistryPath = @"SOFTWARE\Voltris\Optimizer\License";
        private const int CacheValidityHours = 24;
        
        private readonly string _cacheFilePath;
        private readonly string _encryptionKey;
        
        private LicenseCacheService()
        {
            _cacheFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Voltris", CacheFileName);
            
            _encryptionKey = GenerateEncryptionKey();
        }
        
        /// <summary>
        /// Salva dados da licença no cache
        /// </summary>
        public void SaveLicenseCache(LicenseCacheData data)
        {
            try
            {
                data.CachedAt = DateTime.UtcNow;
                data.DeviceId = DeviceFingerprintService.Instance.GetDeviceId();
                data.CacheVersion = 1;
                
                // Calcular hash de integridade
                data.IntegrityHash = ComputeIntegrityHash(data);
                
                var json = JsonSerializer.Serialize(data, VoltrisOptimizer.App.GlobalJsonOptions);
                var encrypted = Encrypt(json);
                
                // Salvar em arquivo
                SaveToFile(encrypted);
                
                // Salvar em registro (backup)
                SaveToRegistry(encrypted);
                
                App.LoggingService?.LogInfo("[LicenseCache] Cache salvo com sucesso");
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[LicenseCache] Erro ao salvar cache", ex);
            }
        }
        
        /// <summary>
        /// Carrega dados da licença do cache
        /// </summary>
        public LicenseCacheData? LoadLicenseCache()
        {
            try
            {
                // Tentar carregar do arquivo primeiro
                var encrypted = LoadFromFile();
                
                // Se não encontrar, tentar do registro
                if (string.IsNullOrEmpty(encrypted))
                    encrypted = LoadFromRegistry();
                
                if (string.IsNullOrEmpty(encrypted))
                    return null;
                
                var json = Decrypt(encrypted);
                
                // Remover BOM se existir (0xEF 0xBB 0xBF ou outros caracteres não JSON no início)
                if (!string.IsNullOrEmpty(json) && json[0] != '{')
                {
                    int firstBrace = json.IndexOf('{');
                    if (firstBrace > 0)
                    {
                        json = json.Substring(firstBrace);
                    }
                }

                var data = JsonSerializer.Deserialize<LicenseCacheData>(json);
                
                if (data == null)
                    return null;
                
                // Verificar integridade
                var expectedHash = ComputeIntegrityHash(data);
                if (data.IntegrityHash != expectedHash)
                {
                    App.LoggingService?.LogWarning("[LicenseCache] Hash de integridade inválido");
                    return null;
                }
                
                // Verificar device ID
                var currentDeviceId = DeviceFingerprintService.Instance.GetDeviceId();
                if (data.DeviceId != currentDeviceId)
                {
                    App.LoggingService?.LogWarning("[LicenseCache] Device ID não corresponde");
                    return null;
                }
                
                return data;
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[LicenseCache] Erro ao carregar cache", ex);
                return null;
            }
        }
        
        /// <summary>
        /// Verifica se o cache é válido (não expirado)
        /// </summary>
        public bool IsCacheValid()
        {
            var cache = LoadLicenseCache();
            if (cache == null)
                return false;
            
            return IsCacheDataValid(cache);
        }
        
        /// <summary>
        /// Verifica se os dados do cache são válidos
        /// </summary>
        public bool IsCacheDataValid(LicenseCacheData data)
        {
            if (data == null)
                return false;
            
            // Verificar se o cache não expirou (24h)
            var cacheAge = DateTime.UtcNow - data.CachedAt;
            if (cacheAge.TotalHours > CacheValidityHours)
            {
                App.LoggingService?.LogInfo($"[LicenseCache] Cache expirado ({cacheAge.TotalHours:F1}h)");
                return false;
            }
            
            // Verificar se a licença não expirou
            if (data.ExpiresAt.HasValue && data.ExpiresAt.Value < DateTime.UtcNow)
            {
                App.LoggingService?.LogInfo("[LicenseCache] Licença expirada");
                return false;
            }
            
            return data.IsValid;
        }
        
        /// <summary>
        /// Obtém horas restantes de cache válido
        /// </summary>
        public double GetCacheRemainingHours()
        {
            var cache = LoadLicenseCache();
            if (cache == null)
                return 0;
            
            var cacheAge = DateTime.UtcNow - cache.CachedAt;
            var remaining = CacheValidityHours - cacheAge.TotalHours;
            
            return Math.Max(0, remaining);
        }
        
        /// <summary>
        /// Limpa o cache
        /// </summary>
        public void ClearCache()
        {
            try
            {
                // Remover arquivo
                if (File.Exists(_cacheFilePath))
                    File.Delete(_cacheFilePath);
                
                // Remover do registro
                using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, true);
                if (key != null)
                {
                    try { key.DeleteValue("CacheData", false); } catch { }
                }
                
                App.LoggingService?.LogInfo("[LicenseCache] Cache limpo");
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[LicenseCache] Erro ao limpar cache", ex);
            }
        }
        
        #region Private Methods
        
        private void SaveToFile(string encrypted)
        {
            try
            {
                var dir = Path.GetDirectoryName(_cacheFilePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                
                File.WriteAllText(_cacheFilePath, encrypted);
                
                // Ocultar arquivo
                File.SetAttributes(_cacheFilePath, FileAttributes.Hidden | FileAttributes.System);
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[LicenseCache] Erro ao salvar arquivo: {ex.Message}");
            }
        }
        
        private string? LoadFromFile()
        {
            try
            {
                if (!File.Exists(_cacheFilePath))
                    return null;
                
                return File.ReadAllText(_cacheFilePath);
            }
            catch
            {
                return null;
            }
        }
        
        private void SaveToRegistry(string encrypted)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
                key.SetValue("CacheData", encrypted, RegistryValueKind.String);
                key.SetValue("CacheTime", DateTime.UtcNow.ToString("o"), RegistryValueKind.String);
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[LicenseCache] Erro ao salvar no registro: {ex.Message}");
            }
        }
        
        private string? LoadFromRegistry()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
                return key?.GetValue("CacheData")?.ToString();
            }
            catch
            {
                return null;
            }
        }
        
        private string GenerateEncryptionKey()
        {
            var deviceId = DeviceFingerprintService.Instance.GetDeviceId();
            return ComputeHash("VOLTRIS_CACHE_" + deviceId).Substring(0, 32);
        }
        
        private string ComputeHash(string input)
        {
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }
        
        private string ComputeIntegrityHash(LicenseCacheData data)
        {
            var content = $"{data.LicenseKey}|{data.LicenseType}|{data.DeviceId}|{data.CachedAt:o}|{data.IsValid}";
            return ComputeHash(content);
        }
        
        private string Encrypt(string plainText)
        {
            try
            {
                var key = Encoding.UTF8.GetBytes(_encryptionKey);
                var iv = new byte[16];
                Array.Copy(key, iv, 16);
                
                using var aes = Aes.Create();
                aes.Key = key;
                aes.IV = iv;
                
                using var encryptor = aes.CreateEncryptor();
                var plainBytes = Encoding.UTF8.GetBytes(plainText);
                var encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
                
                return Convert.ToBase64String(encryptedBytes);
            }
            catch
            {
                return Convert.ToBase64String(Encoding.UTF8.GetBytes(plainText));
            }
        }
        
        private string Decrypt(string cipherText)
        {
            try
            {
                var key = Encoding.UTF8.GetBytes(_encryptionKey);
                var iv = new byte[16];
                Array.Copy(key, iv, 16);
                
                using var aes = Aes.Create();
                aes.Key = key;
                aes.IV = iv;
                
                using var decryptor = aes.CreateDecryptor();
                var cipherBytes = Convert.FromBase64String(cipherText);
                var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
                
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch
            {
                try
                {
                    return Encoding.UTF8.GetString(Convert.FromBase64String(cipherText));
                }
                catch
                {
                    return cipherText;
                }
            }
        }
        
        #endregion
    }
    
    /// <summary>
    /// Dados do cache de licença
    /// </summary>
    public class LicenseCacheData
    {
        public string LicenseKey { get; set; } = "";
        public string LicenseType { get; set; } = "Trial";
        public bool IsValid { get; set; }
        public int MaxDevices { get; set; } = 1;
        public int DevicesInUse { get; set; }
        public DateTime CachedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string DeviceId { get; set; } = "";
        public string IntegrityHash { get; set; } = "";
        public int CacheVersion { get; set; }
    }
}

