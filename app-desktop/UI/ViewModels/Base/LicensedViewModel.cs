using System;
using System.Threading.Tasks;
using System.Windows.Input;
using VoltrisOptimizer.Services.License.Interfaces;
using VoltrisOptimizer.Services.License.Exceptions;

namespace VoltrisOptimizer.UI.ViewModels.Base
{
    /// <summary>
    /// ViewModel base com proteção padrão de licenciamento
    /// </summary>
    public abstract class LicensedViewModel : ViewModelBase
    {
        protected readonly ILicenseGuard LicenseGuard;
        protected readonly ILicenseDialogService DialogService;

        protected LicensedViewModel(ILicenseGuard licenseGuard, ILicenseDialogService dialogService)
        {
            LicenseGuard = licenseGuard ?? throw new ArgumentNullException(nameof(licenseGuard));
            DialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        }

        /// <summary>
        /// Cria um comando com proteção automática de licença
        /// </summary>
        protected ICommand CreateLicensedCommand(Action action, string feature)
        {
            var innerCommand = new RelayCommand(action);
            return new UI.Commands.LicensedCommand(innerCommand, LicenseGuard, feature, DialogService);
        }

        /// <summary>
        /// Cria um comando assíncrono com proteção automática de licença
        /// </summary>
        protected ICommand CreateLicensedAsyncCommand(Func<Task> asyncAction, string feature)
        {
            var innerCommand = new AsyncRelayCommand(asyncAction);
            return new UI.Commands.LicensedCommand(innerCommand, LicenseGuard, feature, DialogService);
        }

        /// <summary>
        /// Executa uma operação com verificação automática de licença
        /// </summary>
        protected async Task<T> ExecuteWithLicenseCheckAsync<T>(string feature, Func<Task<T>> operation)
        {
            try
            {
                return await LicenseGuard.ExecuteAsync(feature, operation);
            }
            catch (LicenseBlockedException ex)
            {
                App.LoggingService?.LogInfo($"[LicensedViewModel] Exibindo diálogo de bloqueio para funcionalidade: {feature}");
                DialogService.ShowLicenseBlocked(ex.State, ex.Feature);
                return default(T);
            }
        }

        /// <summary>
        /// Executa uma operação sem retorno com verificação automática de licença
        /// </summary>
        protected async Task ExecuteWithLicenseCheckAsync(string feature, Func<Task> operation)
        {
            try
            {
                await LicenseGuard.ExecuteAsync(feature, operation);
            }
            catch (LicenseBlockedException ex)
            {
                App.LoggingService?.LogInfo($"[LicensedViewModel] Exibindo diálogo de bloqueio para funcionalidade: {feature}");
                DialogService.ShowLicenseBlocked(ex.State, ex.Feature);
            }
        }

        /// <summary>
        /// Executa uma operação síncrona com verificação automática de licença
        /// </summary>
        protected T ExecuteWithLicenseCheck<T>(string feature, Func<T> operation)
        {
            try
            {
                return LicenseGuard.Execute(feature, operation);
            }
            catch (LicenseBlockedException ex)
            {
                App.LoggingService?.LogInfo($"[LicensedViewModel] Exibindo diálogo de bloqueio para funcionalidade: {feature}");
                DialogService.ShowLicenseBlocked(ex.State, ex.Feature);
                return default(T);
            }
        }

        /// <summary>
        /// Executa uma operação síncrona sem retorno com verificação automática de licença
        /// </summary>
        protected void ExecuteWithLicenseCheck(string feature, Action operation)
        {
            try
            {
                LicenseGuard.Execute(feature, operation);
            }
            catch (LicenseBlockedException ex)
            {
                App.LoggingService?.LogInfo($"[LicensedViewModel] Exibindo diálogo de bloqueio para funcionalidade: {feature}");
                DialogService.ShowLicenseBlocked(ex.State, ex.Feature);
            }
        }
    }
}
