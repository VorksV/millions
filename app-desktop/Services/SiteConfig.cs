using System;

namespace VoltrisOptimizer.Services
{
    /// <summary>
    /// URLs dos sites do Voltris, escolhidas pelo idioma atual do aplicativo.
    ///
    /// Regra única (mesma já usada em LicenseDialogService, PurchasePlansWindow,
    /// LicensePurchaseModal, LicenseRequiredMessageBox e MainWindow):
    ///
    ///   Português  -> site nacional      https://www.voltris.com.br
    ///   Espanhol   -> site internacional https://www.voltrisoptimizer.com
    ///   Inglês       -> site internacional https://www.voltrisoptimizer.com
    ///
    /// O idioma vem de <see cref="LocalizationService.Instance"/>.CurrentLanguage,
    /// que por sua vez é preenchido por LanguageDetector (cultura do Windows) e
    /// pode ser trocado pelo usuário em Configurações. Como a propriedade é
    /// lida a cada chamada, trocar o idioma em tempo de execução já reflete na
    /// próxima URL aberta.
    ///
    /// Os dois sites compartilham o mesmo projeto Supabase
    /// (zamjyyzockbbugjepkhk), portanto os endpoints de API continuam sendo
    /// www.voltris.com.br/api/v1 nos dois idiomas — ver SupabaseConfig.
    /// </summary>
    public static class SiteConfig
    {
        /// <summary>Site nacional (português).</summary>
        public const string BrazilianSiteUrl = "https://www.voltris.com.br";

        /// <summary>Site internacional (inglês e espanhol).</summary>
        public const string InternationalSiteUrl = "https://www.voltrisoptimizer.com";

        /// <summary>True somente para português (pt-BR / pt-PT).</summary>
        public static bool IsPortuguese =>
            LocalizationService.Instance.CurrentLanguage == Language.Portuguese;

        /// <summary>Raiz do site correspondente ao idioma atual.</summary>
        public static string BaseUrl => IsPortuguese ? BrazilianSiteUrl : InternationalSiteUrl;

        /// <summary>
        /// Dashboard do usuário já aberto na aba "Meu Computador" (id da aba: pc).
        /// É para onde o ícone de conta vinculada no header leva o usuário.
        /// </summary>
        public static string AccountDashboardUrl => $"{BaseUrl}/dashboard?tab=pc";

        /// <summary>
        /// Página de vinculação de dispositivo. É o destino que o app abre no
        /// navegador para o usuário entrar na conta e autorizar a máquina.
        /// </summary>
        public static string LinkDeviceUrl(string installationId)
        {
            if (string.IsNullOrWhiteSpace(installationId))
                throw new ArgumentException("installation_id é obrigatório.", nameof(installationId));

            return $"{BaseUrl}/auth/link-device?installation_id={Uri.EscapeDataString(installationId)}";
        }

        /// <summary>Página de compra de licença, no idioma do app.</summary>
        public static string PurchaseLicenseUrl =>
            IsPortuguese ? $"{BrazilianSiteUrl}/adquirir-licenca" : $"{InternationalSiteUrl}/buy-license";

        /// <summary>Domínios liberados para abrir no navegador.</summary>
        public static bool IsKnownSiteUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            return url.StartsWith(BrazilianSiteUrl, StringComparison.OrdinalIgnoreCase)
                || url.StartsWith(InternationalSiteUrl, StringComparison.OrdinalIgnoreCase);
        }
    }
}
