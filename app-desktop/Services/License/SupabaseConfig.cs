using System;

namespace VoltrisOptimizer.Services.License
{
    /// <summary>
    /// Configuração central de acesso ao Supabase.
    /// REGRA DE SEGURANÇA: o cliente JAMAIS deve conter a chave service_role.
    /// Aqui usamos apenas a chave "anon" (pública por design no Supabase).
    /// A service_role vive exclusivamente nas Edge Functions (server-side).
    /// </summary>
    public static class SupabaseConfig
    {
        /// <summary>Referência do projeto Supabase (identificador público).</summary>
        public const string ProjectRef = "zamjyyzockbbugjepkhk";

        /// <summary>Base das Edge Functions.</summary>
        public const string DefaultApiBaseUrl = "https://" + ProjectRef + ".supabase.co/functions/v1";

        /// <summary>
        /// ENDPOINTS - Usar os nomes das funções que realmente existem no Supabase
        /// Funções disponíveis:
        /// - clever-endpoint: Valida e ativa licenças (modelo Freemium Standard/Pro/Enterprise)
        /// </summary>
        public const string ValidateEndpoint = DefaultApiBaseUrl + "/clever-endpoint";
        public const string ActivateEndpoint = DefaultApiBaseUrl + "/clever-endpoint"; // Usar mesma função para ativação (server armazena estado)
        public const string DeactivateEndpoint = DefaultApiBaseUrl + "/clever-endpoint";
        public const string InfoEndpoint = DefaultApiBaseUrl + "/clever-endpoint";

        /// <summary>
        /// Chave secreta HMAC-SHA256 para assinar payloads enviados à Edge Function.
        /// DEVE ser IDÊNTICA à chave usada na Edge Function (server-side).
        /// Prioridade: variável de ambiente VOLTRIS_HMAC_SECRET > constante abaixo.
        /// </summary>
        public static string HmacSecretKey
        {
            get
            {
                var env = Environment.GetEnvironmentVariable("VOLTRIS_HMAC_SECRET");
                if (!string.IsNullOrWhiteSpace(env)) return env;
                return FallbackHmacSecretKey;
            }
        }

        /// <summary>
        /// Chave secreta HMAC-SHA256 fallback.
        /// CRÍTICO: Esta chave deve estar SINCRONIZADA com a Edge Function!
        /// 
        /// ALTERNATIVA: Defina a variável de ambiente VOLTRIS_HMAC_SECRET
        /// set VOLTRIS_HMAC_SECRET=V0ltr1s_Hw1d_S3cr3t_2026_!@#
        /// </summary>
        private const string FallbackHmacSecretKey = "V0ltr1s_Hw1d_S3cr3t_2026_!@#";

        /// <summary>
        /// Chave "anon" pública do projeto (exibida no painel Supabase: Settings > API).
        /// É segura estar no cliente, pois o Supabase a trata como pública.
        /// Prioridade: variável de ambiente VOLTRIS_SUPABASE_ANON_KEY > constante abaixo.
        /// </summary>
        public static string SupabaseAnonKey
        {
            get
            {
                var env = Environment.GetEnvironmentVariable("VOLTRIS_SUPABASE_ANON_KEY");
                if (!string.IsNullOrWhiteSpace(env)) return env;
                return FallbackAnonKey;
            }
        }

        /// <summary>
        /// Chave ANON pública do Supabase (zamjyyzockbbugjepkhk).
        /// Obtida de: Dashboard > Settings > API > Project API keys > anon public
        /// 
        /// ALTERNATIVA: Use variável de ambiente VOLTRIS_SUPABASE_ANON_KEY
        /// set VOLTRIS_SUPABASE_ANON_KEY=eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
        /// </summary>
        private const string FallbackAnonKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InphbWp5eXpvY2tiYnVnamVwa2hrIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NDkxODAzNTQsImV4cCI6MjA2NDc1NjM1NH0.1yMQX0XMgXCYuS6MvAfa8OEB-_3tNTtNhKeBP4EzOTY";
    }
}
