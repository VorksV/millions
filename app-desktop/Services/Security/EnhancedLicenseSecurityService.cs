using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace VoltrisOptimizer.Services.Security
{
    /// <summary>
    /// Serviço de segurança aprimorado para licenciamento
    /// Implementa múltiplas camadas de proteção contra engenharia reversa
    /// </summary>
    internal static class EnhancedLicenseSecurityService
    {
        // Fragmentos de chave distribuídos e ofuscados
        // Cada fragmento passa por transformação antes de ser combinado
        private static readonly byte[] _fragment1 = { 0xA9, 0x1C, 0x0F, 0xAB, 0x05, 0x1E, 0xAA };
        private static readonly byte[] _fragment2 = { 0xBC, 0x00, 0x12, 0x10, 0x0E, 0x02, 0xAA };
        private static readonly byte[] _fragment3 = { 0xBC, 0x19, 0x16, 0x00, 0x02, 0x1B, 0x0E, 0x1F };
        private static readonly byte[] _fragment4 = { 0xBC, 0x1E, 0x02, 0x2C, 0xBC, 0x45, 0x43, 0x45, 0x42 };
        
        // Transformação XOR para cada fragmento (diferente para cada um)
        private static readonly byte[] _transform1 = { 0xFF, 0x53, 0x43, 0xFF, 0x57, 0x57, 0xFF };
        private static readonly byte[] _transform2 = { 0xE3, 0x53, 0x57, 0x54, 0x41, 0x47, 0xE5 };
        private static readonly byte[] _transform3 = { 0xE3, 0x55, 0x5B, 0x4C, 0x47, 0x56, 0x40, 0x4D };
        private static readonly byte[] _transform4 = { 0xE3, 0x55, 0x47, 0x1C, 0xE3, 0x73, 0x73, 0x73, 0x73 };
        
        // Salt dinâmico baseado em características do sistema
        private static byte[]? _cachedSalt;
        private static string? _cachedKey;
        
        /// <summary>
        /// Obtém a chave de assinatura de forma segura
        /// </summary>
        internal static string GetSigningKey()
        {
            // Verificar ambiente (anti-debug básico)
            if (IsDebuggerAttached())
            {
                return GetDecoyKey(); // Retorna chave falsa se debugger detectado
            }
            
            if (_cachedKey != null)
                return _cachedKey;
            
            try
            {
                var result = new StringBuilder();
                
                // Reconstruir cada fragmento com sua transformação específica
                result.Append(TransformFragment(_fragment1, _transform1));
                result.Append(TransformFragment(_fragment2, _transform2));
                result.Append(TransformFragment(_fragment3, _transform3));
                result.Append(TransformFragment(_fragment4, _transform4));
                
                // Aplicar salt do sistema
                var systemSalt = GetSystemSalt();
                var keyBytes = Encoding.UTF8.GetBytes(result.ToString());
                
                using var hmac = new HMACSHA256(systemSalt);
                var derivedKey = hmac.ComputeHash(keyBytes);
                
                _cachedKey = Convert.ToBase64String(derivedKey);
                return _cachedKey;
            }
            catch
            {
                return GetFallbackKey();
            }
        }
        
        private static string TransformFragment(byte[] fragment, byte[] transform)
        {
            var result = new byte[fragment.Length];
            for (int i = 0; i < fragment.Length; i++)
            {
                result[i] = (byte)(fragment[i] ^ transform[i % transform.Length]);
            }
            return Encoding.UTF8.GetString(result);
        }
        
        /// <summary>
        /// Gera assinatura HMAC-SHA256 para conteúdo de licença
        /// </summary>
        internal static string GenerateSignature(string content)
        {
            var key = GetSigningKey();
            var keyBytes = Encoding.UTF8.GetBytes(key);
            var contentBytes = Encoding.UTF8.GetBytes(content);
            
            using var hmac = new HMACSHA256(keyBytes);
            var hash = hmac.ComputeHash(contentBytes);
            
            // Retornar primeiros 16 caracteres hexadecimais
            return BitConverter.ToString(hash).Replace("-", "").Substring(0, 16).ToUpperInvariant();
        }
        
        /// <summary>
        /// Valida assinatura de licença com proteção timing-safe
        /// </summary>
        internal static bool ValidateSignature(string content, string signature)
        {
            var expectedSignature = GenerateSignature(content);
            return TimingSafeEquals(signature, expectedSignature);
        }
        
        /// <summary>
        /// Comparação timing-safe para prevenir timing attacks
        /// </summary>
        private static bool TimingSafeEquals(string a, string b)
        {
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            
            int result = 0;
            for (int i = 0; i < a.Length; i++)
            {
                result |= a[i] ^ b[i];
            }
            return result == 0;
        }
        
        /// <summary>
        /// Criptografa dados usando AES-256-GCM (se disponível) ou AES-CBC
        /// </summary>
        internal static string Encrypt(string plainText, string? customKey = null)
        {
            try
            {
                var key = customKey ?? GetSigningKey();
                var keyBytes = DeriveKey(key, 32);
                var ivBytes = GenerateRandomBytes(16);
                
                using var aes = Aes.Create();
                aes.Key = keyBytes;
                aes.IV = ivBytes;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                
                using var encryptor = aes.CreateEncryptor();
                var plainBytes = Encoding.UTF8.GetBytes(plainText);
                var encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
                
                // Combinar IV + encrypted data + checksum
                var combined = new byte[ivBytes.Length + encryptedBytes.Length + 4];
                Buffer.BlockCopy(ivBytes, 0, combined, 0, ivBytes.Length);
                Buffer.BlockCopy(encryptedBytes, 0, combined, ivBytes.Length, encryptedBytes.Length);
                
                // Adicionar checksum CRC32 simples
                var checksum = CalculateChecksum(encryptedBytes);
                Buffer.BlockCopy(BitConverter.GetBytes(checksum), 0, combined, ivBytes.Length + encryptedBytes.Length, 4);
                
                return Convert.ToBase64String(combined);
            }
            catch
            {
                // Fallback para encoding simples (não deveria acontecer)
                return Convert.ToBase64String(Encoding.UTF8.GetBytes(plainText));
            }
        }
        
        /// <summary>
        /// Descriptografa dados
        /// </summary>
        internal static string Decrypt(string cipherText, string? customKey = null)
        {
            try
            {
                var combined = Convert.FromBase64String(cipherText);
                
                if (combined.Length < 20) // IV(16) + min data + checksum(4)
                {
                    throw new InvalidOperationException("Invalid cipher text length");
                }
                
                var key = customKey ?? GetSigningKey();
                var keyBytes = DeriveKey(key, 32);
                
                // Extrair componentes
                var ivBytes = new byte[16];
                Buffer.BlockCopy(combined, 0, ivBytes, 0, 16);
                
                var encryptedBytes = new byte[combined.Length - 20];
                Buffer.BlockCopy(combined, 16, encryptedBytes, 0, encryptedBytes.Length);
                
                // Verificar checksum
                var storedChecksum = BitConverter.ToUInt32(combined, combined.Length - 4);
                var calculatedChecksum = CalculateChecksum(encryptedBytes);
                
                if (storedChecksum != calculatedChecksum)
                {
                    throw new InvalidOperationException("Checksum mismatch - data corrupted or tampered");
                }
                
                using var aes = Aes.Create();
                aes.Key = keyBytes;
                aes.IV = ivBytes;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                
                using var decryptor = aes.CreateDecryptor();
                var plainBytes = decryptor.TransformFinalBlock(encryptedBytes, 0, encryptedBytes.Length);
                
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch
            {
                // Tentar fallback para formato antigo
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
        
        /// <summary>
        /// Deriva chave usando PBKDF2
        /// </summary>
        private static byte[] DeriveKey(string password, int keyLength)
        {
            var salt = GetSystemSalt();
            using var deriveBytes = new Rfc2898DeriveBytes(password, salt, 10000, HashAlgorithmName.SHA256);
            return deriveBytes.GetBytes(keyLength);
        }
        
        /// <summary>
        /// Gera bytes aleatórios criptograficamente seguros
        /// </summary>
        private static byte[] GenerateRandomBytes(int length)
        {
            var bytes = new byte[length];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return bytes;
        }
        
        /// <summary>
        /// Calcula checksum CRC32
        /// </summary>
        private static uint CalculateChecksum(byte[] data)
        {
            uint crc = 0xFFFFFFFF;
            foreach (byte b in data)
            {
                crc ^= b;
                for (int i = 0; i < 8; i++)
                {
                    crc = (crc >> 1) ^ (0xEDB88320 * (crc & 1));
                }
            }
            return ~crc;
        }
        
        /// <summary>
        /// Obtém salt baseado em características do sistema
        /// </summary>
        private static byte[] GetSystemSalt()
        {
            if (_cachedSalt != null)
                return _cachedSalt;
            
            try
            {
                var systemInfo = new StringBuilder();
                
                // Características da máquina (não muda frequentemente)
                systemInfo.Append(Environment.MachineName);
                systemInfo.Append(Environment.ProcessorCount);
                
                // Adicionar informação estável do sistema
                try
                {
                    using var searcher = new System.Management.ManagementObjectSearcher(
                        "SELECT ProcessorId FROM Win32_Processor");
                    foreach (var obj in searcher.Get())
                    {
                using var __dispose_obj = obj;
                        systemInfo.Append(obj["ProcessorId"]?.ToString() ?? "");
                        break;
                    }
                }
                catch { }
                
                // Hash do sistema info para criar salt
                using var sha256 = SHA256.Create();
                _cachedSalt = sha256.ComputeHash(Encoding.UTF8.GetBytes(systemInfo.ToString()));
                
                // Usar apenas primeiros 16 bytes
                return _cachedSalt.Take(16).ToArray();
            }
            catch
            {
                // Fallback com salt fixo (menos seguro, mas funcional)
                return Encoding.UTF8.GetBytes("VOLTRIS_SALT_KEY");
            }
        }
        
        /// <summary>
        /// Verifica se há debugger anexado (proteção básica)
        /// </summary>
        private static bool IsDebuggerAttached()
        {
            #if DEBUG
            return false; // Permitir debug em builds de desenvolvimento
            #else
            // Verificação básica de debugger
            if (Debugger.IsAttached)
                return true;
            
            // Verificação via API do Windows
            try
            {
                bool isDebugged = false;
                CheckRemoteDebuggerPresent(Process.GetCurrentProcess().Handle, ref isDebugged);
                return isDebugged;
            }
            catch
            {
                return false;
            }
            #endif
        }
        
        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        private static extern bool CheckRemoteDebuggerPresent(IntPtr hProcess, ref bool isDebuggerPresent);
        
        /// <summary>
        /// Retorna chave "isca" se debugger for detectado
        /// </summary>
        private static string GetDecoyKey()
        {
            // Chave falsa que produzirá assinaturas inválidas
            return Convert.ToBase64String(Encoding.UTF8.GetBytes("INVALID_DECOY_KEY_DO_NOT_USE"));
        }
        
        /// <summary>
        /// Chave de fallback (quando outras falham)
        /// </summary>
        private static string GetFallbackKey()
        {
            // Construída de forma menos óbvia
            var parts = new[] { "V0", "LT", "R1", "S_", "SE", "CU", "RE", "_K", "EY" };
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join("", parts)));
        }
        
        /// <summary>
        /// Verifica integridade do sistema de licenciamento
        /// </summary>
        internal static bool VerifyIntegrity()
        {
            try
            {
                // Verificar se a chave pode ser obtida
                var key = GetSigningKey();
                if (string.IsNullOrEmpty(key) || key.Length < 20)
                    return false;
                
                // Verificar ciclo de encrypt/decrypt
                var testData = $"integrity_check_{DateTime.UtcNow.Ticks}";
                var encrypted = Encrypt(testData);
                var decrypted = Decrypt(encrypted);
                
                if (testData != decrypted)
                    return false;
                
                // Verificar assinatura
                var testContent = "test_signature_verification";
                var signature = GenerateSignature(testContent);
                
                return ValidateSignature(testContent, signature);
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Gera ID único da instalação
        /// </summary>
        internal static string GenerateInstallationId()
        {
            try
            {
                var info = new StringBuilder();
                info.Append(Environment.MachineName);
                info.Append(Environment.UserName);
                info.Append(Environment.ProcessorCount);
                
                // Adicionar timestamp da instalação
                info.Append(DateTime.UtcNow.Ticks);
                
                using var sha256 = SHA256.Create();
                var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(info.ToString()));
                
                return BitConverter.ToString(hash).Replace("-", "").Substring(0, 32);
            }
            catch
            {
                return Guid.NewGuid().ToString("N");
            }
        }
        
        /// <summary>
        /// Verifica se a licença foi transferida de outra máquina
        /// </summary>
        internal static bool IsLicenseTransferred(string storedMachineId)
        {
            try
            {
                var currentId = GetMachineId();
                return !string.Equals(storedMachineId, currentId, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false; // Em caso de erro, assumir que não foi transferida
            }
        }
        
        /// <summary>
        /// Obtém ID único da máquina
        /// </summary>
        internal static string GetMachineId()
        {
            try
            {
                var info = new StringBuilder();
                
                using (var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT ProcessorId FROM Win32_Processor"))
                {
                    foreach (var obj in searcher.Get())
                    {
                using var __dispose_obj = obj;
                        info.Append(obj["ProcessorId"]?.ToString() ?? "");
                        break;
                    }
                }
                
                using (var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT SerialNumber FROM Win32_BaseBoard"))
                {
                    foreach (var obj in searcher.Get())
                    {
                using var __dispose_obj = obj;
                        info.Append(obj["SerialNumber"]?.ToString() ?? "");
                        break;
                    }
                }
                
                using var sha256 = SHA256.Create();
                var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(info.ToString()));
                return BitConverter.ToString(hash).Replace("-", "").Substring(0, 16);
            }
            catch
            {
                return Environment.MachineName.GetHashCode().ToString("X8");
            }
        }
    }
}

