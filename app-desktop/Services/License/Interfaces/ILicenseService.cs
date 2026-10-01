using System.Threading.Tasks;
using VoltrisOptimizer.Services.License.Models;

namespace VoltrisOptimizer.Services.License.Interfaces
{
    /// <summary>
    /// Interface do serviço de licença - Domain Layer (sem dependência de UI)
    /// </summary>
    public interface ILicenseService
    {
        /// <summary>
        /// Obtém o estado atual da licença do backend (Supabase)
        /// </summary>
        /// <param name="forceRefresh">Força atualização com backend, ignorando cache</param>
        Task<LicenseState> GetCurrentStateAsync(bool forceRefresh = false);

        /// <summary>
        /// Verifica se uma funcionalidade específica está disponível
        /// </summary>
        /// <param name="feature">Nome da funcionalidade (ex: "optimization", "cleanup")</param>
        /// <param name="forceRefresh">Força atualização com backend</param>
        Task<LicenseCheckResult> CheckFeatureAccessAsync(string feature, bool forceRefresh = false);

        /// <summary>
        /// Invalida o cache local para forçar próxima consulta ao backend
        /// </summary>
        Task InvalidateCacheAsync();

        /// <summary>
        /// Verifica periodicamente o estado da licença (background)
        /// </summary>
        Task<bool> RefreshIfNeededAsync();
    }
}
