using System;
using System.Threading.Tasks;
using System.Windows.Input;
using VoltrisOptimizer.Services.License.Interfaces;
using VoltrisOptimizer.Services.License.Exceptions;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.UI.Commands
{
    /// <summary>
    /// Command wrapper profissional para padronização global de licenciamento
    /// </summary>
    public class LicensedCommand : ICommand
    {
        private readonly ICommand _innerCommand;
        private readonly ILicenseGuard _licenseGuard;
        private readonly ILicenseDialogService _dialogService;
        private readonly string _feature;

        public LicensedCommand(
            ICommand innerCommand, 
            ILicenseGuard licenseGuard, 
            string feature,
            ILicenseDialogService dialogService)
        {
            _innerCommand = innerCommand ?? throw new ArgumentNullException(nameof(innerCommand));
            _licenseGuard = licenseGuard ?? throw new ArgumentNullException(nameof(licenseGuard));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _feature = feature ?? throw new ArgumentNullException(nameof(feature));
        }

        public event EventHandler CanExecuteChanged
        {
            add => _innerCommand.CanExecuteChanged += value;
            remove => _innerCommand.CanExecuteChanged -= value;
        }

        public bool CanExecute(object parameter)
        {
            return _innerCommand.CanExecute(parameter);
        }

        public async void Execute(object parameter)
        {
            try
            {
                await _licenseGuard.ExecuteAsync(_feature, async () =>
                {
                    if (_innerCommand is VoltrisOptimizer.UI.ViewModels.AsyncRelayCommand asyncCommand) 
                    { 
                        await asyncCommand.ExecuteAsync(parameter); 
                    } 
                    else if (_innerCommand is System.Windows.Input.ICommand cmd) 
                    { 
                        cmd.Execute(parameter); 
                    }
                    else
                    {
                        _innerCommand.Execute(parameter);
                        await Task.CompletedTask;
                    }
                });
            }
            catch (LicenseBlockedException ex)
            {
                App.LoggingService?.LogInfo($"[LicensedCommand] Exibindo diálogo de bloqueio para funcionalidade: {_feature}");
                _dialogService.ShowLicenseBlocked(ex.State, ex.Feature);
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[LicensedCommand] Erro ao executar comando licenciado: {_feature}", ex);
                throw;
            }
        }
    }

    /// <summary>
    /// LicensedCommand genérico para operações com retorno
    /// </summary>
    public class LicensedCommand<T> : ICommand
    {
        private readonly Func<Task<T>> _operation;
        private readonly ILicenseGuard _licenseGuard;
        private readonly ILicenseDialogService _dialogService;
        private readonly string _feature;

        public LicensedCommand(
            Func<Task<T>> operation,
            ILicenseGuard licenseGuard,
            string feature,
            ILicenseDialogService dialogService)
        {
            _operation = operation ?? throw new ArgumentNullException(nameof(operation));
            _licenseGuard = licenseGuard ?? throw new ArgumentNullException(nameof(licenseGuard));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _feature = feature ?? throw new ArgumentNullException(nameof(feature));
        }

        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter) => true;

        public async void Execute(object parameter)
        {
            try
            {
                await _licenseGuard.ExecuteAsync(_feature, _operation);
            }
            catch (LicenseBlockedException ex)
            {
                App.LoggingService?.LogInfo($"[LicensedCommand] Exibindo diálogo de bloqueio para funcionalidade: {_feature}");
                _dialogService.ShowLicenseBlocked(ex.State, ex.Feature);
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[LicensedCommand] Erro ao executar comando licenciado: {_feature}", ex);
                throw;
            }
        }
    }
}

