using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.License.Interfaces
{
    /// <summary>
    /// Interface do LicenseGuard - Application Layer (enforcement)
    /// </summary>
    public interface ILicenseGuard
    {
        /// <summary>
        /// Executa uma operação com verificação de licença obrigatória
        /// Lança LicenseBlockedException se não tiver acesso
        /// </summary>
        Task<T> ExecuteAsync<T>(string feature, System.Func<Task<T>> operation);

        /// <summary>
        /// Executa uma operação sem retorno com verificação de licença obrigatória
        /// Lança LicenseBlockedException se não tiver acesso
        /// </summary>
        Task ExecuteAsync(string feature, System.Func<Task> operation);

        /// <summary>
        /// Executa uma operação síncrona com verificação de licença obrigatória
        /// Lança LicenseBlockedException se não tiver acesso
        /// </summary>
        T Execute<T>(string feature, System.Func<T> operation);

        /// <summary>
        /// Executa uma operação síncrona sem retorno com verificação de licença obrigatória
        /// Lança LicenseBlockedException se não tiver acesso
        /// </summary>
        void Execute(string feature, System.Action operation);
    }
}
