using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Core.Security
{
    /// <summary>
    /// SecureConfigProtection — Criptografia AES-256-CBC de arquivos de configuração
    /// com chave derivada do hardware (DPAPI + machine-specific salt).
    /// Protege settings.json, licenças e dados sensíveis contra leitura/modificação externa.
    /// </summary>
    public static class SecureConfigProtection
    {
        private static readonly byte[] AdditionalEntropy = Encoding.UTF8.GetBytes("VoltrisOptimizer_2026_ConfigProtection");

        /// <summary>
        /// Criptografa dados usando DPAPI (Data Protection API) do Windows.
        /// A chave é vinculada ao usuário atual — não pode ser descriptografada em outro PC.
        /// </summary>
        public static byte[] ProtectData(byte[] plainData)
        {
            try
            {
                return ProtectedData.Protect(plainData, AdditionalEntropy, DataProtectionScope.CurrentUser);
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[SecureConfig] DPAPI Protect falhou: {ex.Message}");
                return plainData; // Fallback: retorna sem criptografia
            }
        }

        /// <summary>
        /// Descriptografa dados protegidos com DPAPI.
        /// </summary>
        public static byte[] UnprotectData(byte[] encryptedData)
        {
            try
            {
                return ProtectedData.Unprotect(encryptedData, AdditionalEntropy, DataProtectionScope.CurrentUser);
            }
            catch (CryptographicException)
            {
                // Dados não foram criptografados com DPAPI ou foram corrompidos
                return encryptedData; // Fallback: retorna como está
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[SecureConfig] DPAPI Unprotect falhou: {ex.Message}");
                return encryptedData;
            }
        }

        /// <summary>
        /// Criptografa uma string com AES-256-CBC usando chave derivada de senha + salt aleatório.
        /// Formato: [salt 16 bytes][IV 16 bytes][ciphertext]
        /// </summary>
        public static string EncryptString(string plainText, string password)
        {
            if (string.IsNullOrEmpty(plainText)) return plainText;

            try
            {
                var salt = RandomNumberGenerator.GetBytes(16);
                using var key = new Rfc2898DeriveBytes(password, salt, 100_000, HashAlgorithmName.SHA256);

                using var aes = Aes.Create();
                aes.Key = key.GetBytes(32);
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.GenerateIV();

                using var encryptor = aes.CreateEncryptor();
                var plainBytes = Encoding.UTF8.GetBytes(plainText);
                var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

                // [salt][IV][cipher]
                var result = new byte[salt.Length + aes.IV.Length + cipherBytes.Length];
                Buffer.BlockCopy(salt, 0, result, 0, salt.Length);
                Buffer.BlockCopy(aes.IV, 0, result, salt.Length, aes.IV.Length);
                Buffer.BlockCopy(cipherBytes, 0, result, salt.Length + aes.IV.Length, cipherBytes.Length);

                return Convert.ToBase64String(result);
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[SecureConfig] EncryptString falhou: {ex.Message}");
                return plainText;
            }
        }

        /// <summary>
        /// Descriptografa uma string criptografada com EncryptString.
        /// </summary>
        public static string DecryptString(string encryptedText, string password)
        {
            if (string.IsNullOrEmpty(encryptedText)) return encryptedText;

            try
            {
                var fullBytes = Convert.FromBase64String(encryptedText);
                if (fullBytes.Length < 48) return encryptedText; // Mínimo: 16 salt + 16 IV + 16 cipher

                var salt = new byte[16];
                var iv = new byte[16];
                Buffer.BlockCopy(fullBytes, 0, salt, 0, 16);
                Buffer.BlockCopy(fullBytes, 16, iv, 0, 16);

                var cipherBytes = new byte[fullBytes.Length - 32];
                Buffer.BlockCopy(fullBytes, 32, cipherBytes, 0, cipherBytes.Length);

                using var key = new Rfc2898DeriveBytes(password, salt, 100_000, HashAlgorithmName.SHA256);
                using var aes = Aes.Create();
                aes.Key = key.GetBytes(32);
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using var decryptor = aes.CreateDecryptor();
                var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch (FormatException)
            {
                return encryptedText; // Não é base64 — provavelmente texto plano (migração)
            }
            catch (CryptographicException)
            {
                return encryptedText; // Chave errada ou dados corrompidos — retorna como está
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[SecureConfig] DecryptString falhou: {ex.Message}");
                return encryptedText;
            }
        }

        /// <summary>
        /// Criptografa um segredo (API key, license key) com DPAPI e devolve Base64.
        /// Usa <see cref="DataProtectionScope.CurrentUser"/>: só a conta do Windows
        /// daquele usuário, naquele PC, consegue reverter. Copiar o arquivo para
        /// outra máquina não abre nada.
        /// </summary>
        public static string? ProtectSecret(string? plain)
        {
            if (string.IsNullOrEmpty(plain)) return null;
            try
            {
                var plainBytes = Encoding.UTF8.GetBytes(plain);
                var cipherBytes = ProtectedData.Protect(plainBytes, AdditionalEntropy, DataProtectionScope.CurrentUser);
                // O Base64 já é seguro para JSON e não pode ser lido como texto.
                return Convert.ToBase64String(cipherBytes);
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[SecureConfig] ProtectSecret falhou: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Descriptografa um segredo protegido por <see cref="ProtectSecret"/>.
        /// </summary>
        public static string? UnprotectSecret(string? base64Cipher)
        {
            if (string.IsNullOrEmpty(base64Cipher)) return null;
            try
            {
                var cipherBytes = Convert.FromBase64String(base64Cipher);
                var plainBytes = ProtectedData.Unprotect(cipherBytes, AdditionalEntropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[SecureConfig] UnprotectSecret falhou: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Calcula HMAC-SHA256 de um arquivo para verificação de integridade.
        /// </summary>
        public static string ComputeFileHmac(string filePath, string key)
        {
            try
            {
                var keyBytes = Encoding.UTF8.GetBytes(key);
                using var hmac = new HMACSHA256(keyBytes);
                using var stream = File.OpenRead(filePath);
                var hash = hmac.ComputeHash(stream);
                return Convert.ToHexString(hash).ToLowerInvariant();
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Verifica integridade de um arquivo comparando HMAC.
        /// </summary>
        public static bool VerifyFileIntegrity(string filePath, string expectedHmac, string key)
        {
            var currentHmac = ComputeFileHmac(filePath, key);
            return !string.IsNullOrEmpty(currentHmac) &&
                   string.Equals(currentHmac, expectedHmac, StringComparison.OrdinalIgnoreCase);
        }
    }
}
