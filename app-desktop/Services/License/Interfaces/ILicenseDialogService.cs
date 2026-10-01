using VoltrisOptimizer.Services.License.Models;

namespace VoltrisOptimizer.Services.License.Interfaces
{
    /// <summary>
    /// Interface para serviços de diálogo de licença - UI Layer
    /// </summary>
    public interface ILicenseDialogService
    {
        /// <summary>
        /// Exibe modal de funcionalidade bloqueada
        /// </summary>
        void ShowLicenseBlocked(LicenseState state, string feature);

        /// <summary>
        /// Exibe modal de licença expirada
        /// </summary>
        void ShowLicenseExpired(LicenseState state);

        /// <summary>
        /// Exibe modal de compra de licença
        /// </summary>
        void ShowPurchaseLicense(LicenseState state, string feature);

        /// <summary>
        /// Navega para página de licença
        /// </summary>
        void NavigateToLicensePage();
    }
}
