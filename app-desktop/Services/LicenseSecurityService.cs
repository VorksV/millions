using System;

namespace VoltrisOptimizer.Services
{
    /// <summary>
    /// LicenseSecurityService — STUB de compatibilidade.
    /// 
    /// A validação de assinatura de licenças foi movida para o servidor
    /// (Edge Function validate-license). O cliente NÃO tem mais acesso
    /// ao segredo de assinatura.
    /// 
    /// Esta classe existe apenas para não quebrar referências legadas.
    /// Todos os métodos retornam valores neutros/seguros.
    /// </summary>
    internal static class LicenseSecurityService
    {
        /// <summary>
        /// REMOVIDO: O segredo de assinatura não existe mais no cliente.
        /// A validação ocorre exclusivamente no servidor (Supabase Edge Function).
        /// </summary>
        internal static string GetSigningKey()
        {
            App.LoggingService?.LogWarning("[LicenseSecurityService] GetSigningKey chamado — método obsoleto. A validação de assinatura é server-side.");
            // Retorna string vazia — não expõe segredo
            return string.Empty;
        }

        /// <summary>
        /// REMOVIDO: Geração de assinatura é server-side.
        /// </summary>
        internal static string GenerateSignature(string content)
        {
            App.LoggingService?.LogWarning("[LicenseSecurityService] GenerateSignature chamado — método obsoleto. Use a Edge Function validate-license.");
            return string.Empty;
        }

        /// <summary>
        /// REMOVIDO: Validação de assinatura é server-side.
        /// Sempre retorna false para forçar validação no servidor.
        /// </summary>
        internal static bool ValidateSignature(string content, string signature)
        {
            App.LoggingService?.LogWarning("[LicenseSecurityService] ValidateSignature chamado — método obsoleto. A validação ocorre no servidor.");
            return false;
        }

        /// <summary>
        /// Verifica integridade do sistema de licenciamento.
        /// Com validação server-side, a integridade é garantida pelo servidor.
        /// </summary>
        internal static bool VerifyIntegrity()
        {
            App.LoggingService?.LogInfo("[LicenseSecurityService] VerifyIntegrity chamado — validação delegada ao servidor");
            return true;
        }
    }
}
