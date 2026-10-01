using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace VoltrisOptimizer.Services.Logging
{
    /// <summary>
    /// Serviço de mascaramento de informações sensíveis em logs
    /// Protege contra exposição de chaves API, HWIDs, URLs e outros dados críticos
    /// </summary>
    public static class SecurityMaskingService
    {
        // Padrões de informações sensíveis que devem ser mascarados
        private static readonly Dictionary<string, (Regex Pattern, string Replacement)> _sensitivePatterns = new()
        {
            // Chaves JWT/Supabase (começam com eyJhbGciOiJIUzI1NiIs)
            {
                "JWT_TOKEN", 
                (new Regex(@"eyJ[a-zA-Z0-9_-]+\.[a-zA-Z0-9_-]+\.[a-zA-Z0-9_-]+", RegexOptions.Compiled), 
                 "eyJ***[JWT_TOKEN_MASCARADO]***")
            },
            
            // HWIDs longos (hashes de hardware)
            {
                "HWID_FULL",
                (new Regex(@"\b[a-fA-F0-9]{64,128}\b", RegexOptions.Compiled),
                 "***[HWID_TRUNCADO]***")
            },
            
            // URLs de APIs sensíveis
            {
                "SUPABASE_URL",
                (new Regex(@"https?://[a-zA-Z0-9-]+\.supabase\.co/functions/v1/[a-zA-Z0-9-]+", RegexOptions.Compiled),
                 "https://***[SUPABASE_ENDPOINT_MASCARADO]***")
            },
            
            // Chaves de API longas
            {
                "API_KEY_LONG",
                (new Regex(@"[a-zA-Z0-9]{32}", RegexOptions.Compiled),
                 "***[API_KEY_MASCARADA]***")
            },
            
            // Assembly hashes
            {
                "ASSEMBLY_HASH",
                (new Regex(@"[A-F0-9]{16}", RegexOptions.Compiled),
                 "***[ASSEMBLY_HASH_MASCARADO]***")
            },
            
            // URLs com parâmetros sensíveis
            {
                "URL_WITH_PARAMS",
                (new Regex(@"https?://[^\s\?]+\?[^\s]*", RegexOptions.Compiled),
                 "***[URL_COM_PARAMETROS_MASCARADA]***")
            },
            
            // Endereços de email
            {
                "EMAIL_ADDRESS",
                (new Regex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2}\b", RegexOptions.Compiled),
                 "***[EMAIL_MASCARADO]***")
            },
            
            // Tokens de autenticação
            {
                "AUTH_TOKEN",
                (new Regex(@"token[s]?[:=]\s*[a-zA-Z0-9_-]{20}", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                 "token: ***[AUTH_TOKEN_MASCARADO]***")
            }
        };

        // Padrões específicos para HWID (detectados pelo contexto)
        private static readonly string[] _hwidContextKeywords = {
            "HWID:", "🔑 HWID:", "🔑 Chave:", "CurrentHwid:", "_currentHwid",
            "Hardware ID:", "Device ID:", "Machine ID:"
        };

        // Padrões específicos para chaves API
        private static readonly string[] _apiKeyContextKeywords = {
            "🔑 Chave:", "Chave:", "API Key:", "Secret:", "Token:",
            "service_role key", "supabaseKey:", "_supabaseKey"
        };

        /// <summary>
        /// Mascara informações sensíveis em uma mensagem de log
        /// </summary>
        /// <param name="message">Mensagem original</param>
        /// <returns>Mensagem com informações sensíveis mascaradas</returns>
        public static string MaskSensitiveData(string message)
        {
            if (string.IsNullOrEmpty(message))
                return message;

            var maskedMessage = message;

            // Aplicar mascaramento para cada padrão sensível
            foreach (var (name, (pattern, replacement)) in _sensitivePatterns)
            {
                maskedMessage = pattern.Replace(maskedMessage, replacement);
            }

            // Mascaramento específico por contexto (HWIDs)
            maskedMessage = MaskHwidByContext(maskedMessage);

            // Mascaramento específico para chaves API por contexto
            maskedMessage = MaskApiKeyByContext(maskedMessage);

            return maskedMessage;
        }

        /// <summary>
        /// Mascara HWIDs detectados por contexto (mesmo que não correspondam ao padrão regex)
        /// </summary>
        private static string MaskHwidByContext(string message)
        {
            foreach (var keyword in _hwidContextKeywords)
            {
                var pattern = $@"({Regex.Escape(keyword)}\s*)([a-fA-F0-9]{{16}})";
                var regex = new Regex(pattern, RegexOptions.IgnoreCase);
                
                message = regex.Replace(message, m => 
                {
                    var prefix = m.Groups[1].Value;
                    var hwid = m.Groups[2].Value;
                    
                    // Manter apenas os primeiros 8 caracteres para identificação
                    var truncated = hwid.Length > 8 ? hwid.Substring(0, 8) + "..." : hwid;
                    return $"{prefix}{truncated}[HWID_MASCARADO]";
                });
            }

            return message;
        }

        /// <summary>
        /// Mascara chaves API detectadas por contexto
        /// </summary>
        private static string MaskApiKeyByContext(string message)
        {
            foreach (var keyword in _apiKeyContextKeywords)
            {
                var pattern = $@"({Regex.Escape(keyword)}\s*)([a-zA-Z0-9_-]{{20}})";
                var regex = new Regex(pattern, RegexOptions.IgnoreCase);
                
                message = regex.Replace(message, m => 
                {
                    var prefix = m.Groups[1].Value;
                    var key = m.Groups[2].Value;
                    
                    // Manter apenas os primeiros 12 caracteres
                    var truncated = key.Length > 12 ? key.Substring(0, 12) + "..." : key;
                    return $"{prefix}{truncated}[API_KEY_MASCARADA]";
                });
            }

            return message;
        }

        /// <summary>
        /// Mascara informações específicas de licença
        /// </summary>
        /// <param name="licenseKey">Chave de licença</param>
        /// <returns>Chave mascarada</returns>
        public static string MaskLicenseKey(string licenseKey)
        {
            if (string.IsNullOrEmpty(licenseKey))
                return licenseKey;

            if (licenseKey.Length <= 12)
                return "***[LICENCA_CURTA]***";

            return licenseKey.Substring(0, 12) + "...[LICENCA_MASCARADA]";
        }

        /// <summary>
        /// Mascara URLs mantendo apenas o domínio principal
        /// </summary>
        /// <param name="url">URL completa</param>
        /// <returns>URL mascarada</returns>
        public static string MaskUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return url;

            try
            {
                var uri = new Uri(url);
                var domain = uri.Host;
                
                // Se for um domínio conhecido (não sensível), manter
                var safeDomains = new[] { "voltris.com.br", "intel.com", "nvidia.com", "amd.com", "microsoft.com" };
                if (safeDomains.Any(d => domain.Contains(d)))
                {
                    return url; // URL segura, não mascarar
                }

                // Mascarar URLs sensíveis
                return $"{uri.Scheme}://***[URL_MASCARADA]***{uri.PathAndQuery}";
            }
            catch
            {
                // Se não for uma URL válida, mascarar completamente
                return "***[URL_INVALIDA_MASCARADA]***";
            }
        }

        /// <summary>
        /// Verifica se uma mensagem contém informações sensíveis
        /// </summary>
        /// <param name="message">Mensagem a verificar</param>
        /// <returns>True se contém informações sensíveis</returns>
        public static bool ContainsSensitiveData(string message)
        {
            if (string.IsNullOrEmpty(message))
                return false;

            foreach (var (name, (pattern, _)) in _sensitivePatterns)
            {
                if (pattern.IsMatch(message))
                    return true;
            }

            // Verificar contexto de HWID
            foreach (var keyword in _hwidContextKeywords)
            {
                if (message.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            // Verificar contexto de API Key
            foreach (var keyword in _apiKeyContextKeywords)
            {
                if (message.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Remove completamente informações sensíveis (substitui por placeholder genérico)
        /// </summary>
        /// <param name="message">Mensagem original</param>
        /// <returns>Mensagem sem informações sensíveis</returns>
        public static string RemoveSensitiveData(string message)
        {
            if (string.IsNullOrEmpty(message))
                return message;

            var cleanedMessage = message;

            // Remover completamente informações sensíveis
            foreach (var (name, (pattern, _)) in _sensitivePatterns)
            {
                cleanedMessage = pattern.Replace(cleanedMessage, "***[REMOVIDO]***");
            }

            // Remover HWIDs por contexto
            foreach (var keyword in _hwidContextKeywords)
            {
                var pattern = $@"{Regex.Escape(keyword)}\s*[a-fA-F0-9]{{16}}";
                cleanedMessage = Regex.Replace(cleanedMessage, pattern, $"{keyword} ***[REMOVIDO]***", RegexOptions.IgnoreCase);
            }

            return cleanedMessage;
        }

        /// <summary>
        /// Estatísticas de mascaramento para debugging
        /// </summary>
        /// <param name="originalMessage">Mensagem original</param>
        /// <param name="maskedMessage">Mensagem mascarada</param>
        /// <returns>Estatísticas do mascaramento</returns>
        public static Dictionary<string, int> GetMaskingStats(string originalMessage, string maskedMessage)
        {
            var stats = new Dictionary<string, int>();

            foreach (var (name, (pattern, _)) in _sensitivePatterns)
            {
                var originalMatches = pattern.Matches(originalMessage).Count;
                var maskedMatches = pattern.Matches(maskedMessage).Count;
                
                if (originalMatches > 0 || maskedMatches > 0)
                {
                    stats[name] = originalMatches;
                }
            }

            return stats;
        }
    }
}
