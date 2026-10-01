using System;
using System.Security.Cryptography;

namespace VoltrisOptimizer.Services
{
    /// <summary>
    /// Identificador de correlação entre o VOLTRIS OPTIMIZER, a API do site e o
    /// Supabase. O mesmo ID aparece no log do app, no log do servidor e na
    /// resposta HTTP, permitindo localizar em qual camada a operação falhou.
    ///
    /// Exemplo: VOLTRIS-LINK-20260926-A1B2C3D4
    /// </summary>
    public static class CorrelationId
    {
        public const string LinkPrefix = "VOLTRIS-LINK";

        /// <summary>Gera um correlation ID novo, com prefixo e data.</summary>
        public static string New(string prefix = LinkPrefix)
        {
            Span<byte> bytes = stackalloc byte[4];
            RandomNumberGenerator.Fill(bytes);

            var suffix = Convert.ToHexString(bytes);
            return $"{prefix}-{DateTime.UtcNow:yyyyMMdd}-{suffix}";
        }

        /// <summary>
        /// Valida um correlation ID recebido do servidor antes de echo-lo no log.
        /// Aceita apenas o formato que geramos, evitando log injection.
        /// </summary>
        public static bool IsValid(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            if (value.Length > 64) return false;

            foreach (var c in value)
            {
                var ok = (c >= 'A' && c <= 'Z')
                         || (c >= 'a' && c <= 'z')
                         || (c >= '0' && c <= '9')
                         || c == '-';
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>Devolve o ID se válido, senão um novo (nunca loga entrada não confiável).</summary>
        public static string Sanitize(string? value, string prefix = LinkPrefix)
            => IsValid(value) ? value! : New(prefix);
    }
}
