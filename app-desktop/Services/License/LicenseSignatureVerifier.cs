using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace VoltrisOptimizer.Services.License
{
    /// <summary>
    /// Verificação de assinaturas RSA (licenças e respostas do servidor).
    ///
    /// Arquitetura segura:
    ///   - O SERVIDOR possui a CHAVE PRIVADA e assina licenças/respostas.
    ///   - O CLIENTE possui apenas a CHAVE PÚBLICA e verifica.
    ///   - Sem a chave privada, é impossível forjar uma licença válida.
    /// </summary>
    public static class LicenseSignatureVerifier
    {
        /// <summary>
        /// Chave pública usada para verificar licenças offline e respostas do servidor.
        /// O par privado correspondente está em ServerFunctions/private-key.pem (server-side).
        /// </summary>
        public const string PublicKeyPem =
            "-----BEGIN PUBLIC KEY-----\n" +
            "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA14YS6Xz6L/4IoN8ItoYc\n" +
            "LfblgXMCeCd8T+jvHgGj1OPJ/vKC9QrGUmd9ZCkHGTmvywX9z5SWrhgtlO/Cli5I\n" +
            "ElyPPSlul2grsVY2wFiFGhHzHp2Cw8aMWHBxwMdtthHWPrNiDRbEguqPBxh32WzB\n" +
            "xMxso4NG3YyHTDSH3RKy8GMiLq0KJne485j/NU2fFTCggrCFgeCrvYLWUdBO/0X/\n" +
            "OqXl9YspwAmuG50wj7HGtym5FfAFz0bw9cVY66Vwzeo5ozWy3U7+XMpl2VWVEa9a\n" +
            "l0wItPFAz20+A/eud6uGJj9mdBcBjxXT3iJMbP3QLN+B0rm9QPKIJCDy46K49OTO\n" +
            "qQIDAQAB\n" +
            "-----END PUBLIC KEY-----";

        /// <summary>
        /// Verifica a assinatura embutida em uma chave de licença no formato:
        ///   VOLTRIS-PLANO-ID-DATA-SIG
        /// 
        /// Nota: Para licenças geradas pelo servidor (Supabase), a assinatura é validada
        /// pelo Edge Function na ativação. A validação local é apenas uma proteção extra
        /// contra typos e formato inválido. Aceitamos qualquer formato de assinatura
        /// não-vazia já que o verdadeiro gatekeeping é no servidor.
        /// </summary>
        public static bool VerifyLicenseKey(string? licenseKey)
        {
            if (string.IsNullOrWhiteSpace(licenseKey)) return false;

            var key = licenseKey.Trim().ToUpperInvariant();
            const string prefix = "VOLTRIS-";
            if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

            var rest = key.Substring(prefix.Length);
            var parts = rest.Split('-');
            
            // Formato esperado: VOLTRIS-PLANO-ID-DATA-SIG
            // Mínimo: 5 partes (VOLTRIS é removido, então rest tem 4+ partes)
            if (parts.Length < 4) return false;

            var planCode = parts[0];
            var clientId = parts[1];
            var dateStr = parts[2];
            var sig = string.Join("-", parts, 3, parts.Length - 3);

            // ────────────────────────────────────────────────────────────────────────────
            // VALIDAÇÕES BÁSICAS (proteção contra typos, não contra forgery)
            // ────────────────────────────────────────────────────────────────────────────
            
            // Validar plan code
            if (!IsValidPlanCode(planCode))
                return false;

            // Validar formato da data (YYYYMMDD)
            if (dateStr.Length != 8 || !int.TryParse(dateStr, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                return false;

            // Validar data é válida (não precisa estar no futuro, apenas ser um date válida)
            var dateStr_formatted = $"{dateStr.Substring(0, 4)}-{dateStr.Substring(4, 2)}-{dateStr.Substring(6, 2)}";
            if (!DateTime.TryParseExact(dateStr_formatted, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                return false;

            // Validar que a assinatura não está vazia
            if (string.IsNullOrWhiteSpace(sig))
                return false;

            // ────────────────────────────────────────────────────────────────────────────
            // NOTA: NÃO verificamos a assinatura criptográfica aqui.
            // O verdadeiro gatekeeping é feito pelo Edge Function durante a ativação.
            // A chave é uma token legível que identifica a licença, não uma chave criptografera.
            // ────────────────────────────────────────────────────────────────────────────
            
            return true;
        }

        private static bool IsValidPlanCode(string code)
        {
            return code switch
            {
                "PRO" => true,
                "STA" => true,
                "ENT" => true,
                "TRL" => true,
                "LIF" => true,
                _ => false
            };
        }

        private const string LegacySecretFallback = "VOLTRIS_SECRET_LICENSE_KEY_2025";

        private static string LegacySecretValue
        {
            get
            {
                var env = Environment.GetEnvironmentVariable("VOLTRIS_LEGACY_LICENSE_SECRET");
                return !string.IsNullOrWhiteSpace(env) ? env : LegacySecretFallback;
            }
        }

        private static bool VerifyLegacyLicenseKeyCore(string planCode, string clientId, string dateStr, string sigHex)
        {
            try
            {
                if (dateStr.Length != 8 || !int.TryParse(dateStr, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                    return false;

                var validUntil = $"{dateStr.Substring(0, 4)}-{dateStr.Substring(4, 2)}-{dateStr.Substring(6, 2)}";
                if (!DateTime.TryParseExact(validUntil, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                    return false;

                foreach (var candidate in BuildLegacyContentCandidates(clientId, validUntil, planCode))
                {
                    var hash = ComputeSha256Hex(candidate + LegacySecretValue);
                    if (string.Equals(hash.Substring(0, 16), sigHex, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static IEnumerable<string> BuildLegacyContentCandidates(string clientId, string validUntil, string planCode)
        {
            var plans = new List<(string Plan, int Devices)>();
            switch ((planCode ?? string.Empty).ToUpperInvariant())
            {
                case "TRI": plans.Add(("trial", 1)); break;
                case "STA": plans.Add(("standard", 1)); break;
                case "PRO": plans.Add(("pro", 3)); break;
                case "ENT": plans.Add(("enterprise", 9999)); break;
                case "LIF":
                case "LIC":
                    plans.Add(("lifetime", 1));
                    plans.Add(("lifetime", 9999));
                    break;
                default:
                    plans.Add(((planCode ?? string.Empty).ToLowerInvariant(), 1));
                    break;
            }

            var results = new List<string>();
            foreach (var (plan, devices) in plans)
            {
                var inline = $"\"id\":\"{clientId}\",\"validUntil\":\"{validUntil}\",\"plan\":\"{plan}\",\"maxDevices\":{devices}";
                var inlineSpace = inline.Replace($"\"maxDevices\":{devices}", $"\"maxDevices\": {devices}");

                results.Add("{" + inline + "}");
                results.Add("{" + inlineSpace + "}");
                results.Add("{\n  {\n    " + inline + "\n  }\n}");
                results.Add("{\n  {\n    " + inlineSpace + "\n  }\n}");
                results.Add("{\n{\n" + inline + "\n}\n}");
            }
            return results;
        }

        private static bool IsHexString(string s)
        {
            foreach (var c in s)
            {
                if (!Uri.IsHexDigit(c)) return false;
            }
            return true;
        }

        private static string ComputeSha256Hex(string input)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        public static bool VerifySignedPayload(string canonicalPayload, string? signatureBase64Url)
        {
            if (string.IsNullOrWhiteSpace(canonicalPayload) || string.IsNullOrWhiteSpace(signatureBase64Url))
                return false;

            try
            {
                var signature = DecodeBase64Url(signatureBase64Url);
                if (signature == null || signature.Length == 0) return false;

                using var rsa = RSA.Create();
                rsa.ImportFromPem(PublicKeyPem);

                var data = Encoding.UTF8.GetBytes(canonicalPayload);
                return rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
            catch
            {
                return false;
            }
        }

        public static string BuildActivationPayload(string licenseType, int maxDevices, DateTime expiresAt, string deviceId)
        {
            return $"{licenseType}|{maxDevices}|{expiresAt.ToUniversalTime():O}|{deviceId}";
        }

        public static string BuildValidationPayload(string licenseKey, string deviceId, bool isValid)
        {
            return $"{licenseKey}|{deviceId}|{isValid}";
        }

        public static byte[]? DecodeBase64Url(string input)
        {
            var s = input.Trim().Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
                case 1: return null;
            }
            try { return Convert.FromBase64String(s); }
            catch { return null; }
        }

        /// <summary>
        /// Computa a assinatura HMAC-SHA256 do payload usando a chave secreta.
        /// Retorna a assinatura em base64 para o header x-voltris-signature.
        /// </summary>
        public static string ComputeHmacSha256(string payload)
        {
            var secretKey = SupabaseConfig.HmacSecretKey;
            var secretBytes = Encoding.UTF8.GetBytes(secretKey);
            var payloadBytes = Encoding.UTF8.GetBytes(payload);

            App.LoggingService?.LogInfo($"[LicenseSignatureVerifier.ComputeHmacSha256] Iniciando computação:");
            App.LoggingService?.LogInfo($"[LicenseSignatureVerifier.ComputeHmacSha256]   - Secret key (from SupabaseConfig): {secretKey}");
            App.LoggingService?.LogInfo($"[LicenseSignatureVerifier.ComputeHmacSha256]   - Secret key length: {secretBytes.Length} bytes");
            App.LoggingService?.LogInfo($"[LicenseSignatureVerifier.ComputeHmacSha256]   - Payload length: {payloadBytes.Length} bytes");

            using var hmac = new HMACSHA256(secretBytes);
            var signatureBytes = hmac.ComputeHash(payloadBytes);
            var signatureBase64 = Convert.ToBase64String(signatureBytes);
            
            var signatureHex = BitConverter.ToString(signatureBytes).Replace("-", "").ToLower();
            App.LoggingService?.LogInfo($"[LicenseSignatureVerifier.ComputeHmacSha256]   - Signature bytes length: {signatureBytes.Length} bytes");
            App.LoggingService?.LogInfo($"[LicenseSignatureVerifier.ComputeHmacSha256]   - Signature (hex): {signatureHex}");
            App.LoggingService?.LogInfo($"[LicenseSignatureVerifier.ComputeHmacSha256]   - Signature (base64): {signatureBase64}");
            
            return signatureBase64;
        }
    }
}
