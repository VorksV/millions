using System;
using System.IO;
using System.Text;
using VoltrisOptimizer.Core.Security;

namespace VoltrisOptimizer.Services.Cloud
{
    /// <summary>
    /// Guarda a credencial de dispositivo emitida pelo servidor.
    ///
    /// A credencial autoriza este PC a consultar o próprio vínculo e a se
    /// desvincular — é o que faz o botão "Desvincular deste Computador"
    /// funcionar, já que o app não tem sessão de navegador.
    ///
    /// SEGURANÇA
    /// O token é gravado com DPAPI (DataProtectionScope.CurrentUser): só o
    /// mesmo usuário do Windows, na mesma máquina, consegue descriptografar.
    /// Uma cópia do arquivo não serve em outro PC nem para outro usuário.
    ///
    /// O token é destruído ao desvincular e reemitido a cada novo vínculo.
    /// </summary>
    public static class DeviceCredentialStore
    {
        private static readonly object Gate = new();
        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Voltris",
            "device_credential.bin");

        private static string? _cached;
        private static bool _loaded;

        /// <summary>Credencial atual, ou null se ainda nao houve emissao.</summary>
        public static string? Current
        {
            get
            {
                lock (Gate)
                {
                    if (_loaded) return _cached;
                    _cached = ReadFromDisk();
                    _loaded = true;
                    return _cached;
                }
            }
        }

        public static bool HasCredential => !string.IsNullOrEmpty(Current);

        /// <summary>
        /// Guarda a credencial. Só aceita um token bem formado; uma resposta
        /// corrompida não sobrescreve uma credencial boa.
        /// </summary>
        public static bool TrySave(string? token)
        {
            if (!IsWellFormed(token))
            {
                App.LoggingService?.LogWarning("[CREDENTIAL] Token recusado: formato invalido.");
                return false;
            }

            try
            {
                lock (Gate)
                {
                    var dir = Path.GetDirectoryName(FilePath)!;
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                    var plain = Encoding.UTF8.GetBytes(token!);
                    var protectedBytes = SecureConfigProtection.ProtectData(plain);
                    File.WriteAllBytes(FilePath, protectedBytes);

                    _cached = token;
                    _loaded = true;
                }

                App.LoggingService?.LogInfo("[CREDENTIAL] Credencial de dispositivo salva (DPAPI).");
                return true;
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[CREDENTIAL] Falha ao salvar credencial: {ex.Message}");
                return false;
            }
        }

        /// <summary>Apaga a credencial. Chamado ao desvincular.</summary>
        public static void Clear()
        {
            try
            {
                lock (Gate)
                {
                    if (File.Exists(FilePath)) File.Delete(FilePath);
                    _cached = null;
                    _loaded = true;
                }
                App.LoggingService?.LogInfo("[CREDENTIAL] Credencial de dispositivo removida.");
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[CREDENTIAL] Falha ao remover credencial: {ex.Message}");
            }
        }

        /// <summary>
        /// Gera uma credencial nova, guarda com DPAPI e devolve.
        /// Usada na primeira execução (ou quando a credencial foi perdida).
        /// </summary>
        public static string? CreateNew()
        {
            var token = GenerateToken();
            return TrySave(token) ? token : null;
        }

        /// <summary>256 bits em base64url — mesmo formato que o servidor valida.</summary>
        private static string GenerateToken()
        {
            Span<byte> bytes = stackalloc byte[32];
            System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
            return Convert.ToBase64String(bytes)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        }

        /// <summary>Mesmo formato aceito pelo servidor (base64url, 256 bits).</summary>
        public static bool IsWellFormed(string? token)
        {
            if (string.IsNullOrEmpty(token)) return false;
            if (token.Length < 40 || token.Length > 128) return false;

            foreach (var c in token)
            {
                var ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')
                         || (c >= '0' && c <= '9') || c == '-' || c == '_';
                if (!ok) return false;
            }
            return true;
        }

        private static string? ReadFromDisk()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;

                var stored = File.ReadAllBytes(FilePath);
                if (stored.Length == 0) return null;

                var plain = SecureConfigProtection.UnprotectData(stored);
                var token = Encoding.UTF8.GetString(plain).Trim('\0', '\r', '\n', ' ');

                return IsWellFormed(token) ? token : null;
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[CREDENTIAL] Falha ao ler credencial: {ex.Message}");
                return null;
            }
        }
    }
}
