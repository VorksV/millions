using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.UI.ViewModels
{
    /// <summary>
    /// Classe base para todos os ViewModels
    /// Implementa INotifyPropertyChanged para data binding
    /// </summary>
    public abstract class ViewModelBase : INotifyPropertyChanged, IDisposable
    {
        private bool _isDisposed;
        private bool _isBusy;
        private bool _isActive = true;
        private string _busyMessage = string.Empty;

        /// <summary>
        /// Indica se o ViewModel está ativo (view visível).
        /// Usado para pausar timers e processamento de background.
        /// </summary>
        public bool IsActive
        {
            get => _isActive;
            set => SetProperty(ref _isActive, value, OnActiveChanged);
        }

        /// <summary>
        /// Chamado quando IsActive muda.
        /// Override para pausar/resumir timers.
        /// </summary>
        protected virtual void OnActiveChanged() { }

        /// <summary>
        /// Indica se o ViewModel está ocupado executando uma operação
        /// </summary>
        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        /// <summary>
        /// Mensagem a exibir enquanto ocupado
        /// </summary>
        public string BusyMessage
        {
            get => _busyMessage;
            set => SetProperty(ref _busyMessage, value);
        }

        /// <summary>
        /// [LEGADO] Redireciona para OnDisposing (usado por versões antigas)
        /// </summary>
        [Obsolete("Use OnDisposing instead")]
        protected virtual void OnDispose() { OnDisposing(); }

        /// <summary>
        /// Chamado quando o ViewModel está sendo descartado.
        /// Override para limpar assinaturas de eventos, timers e recursos de background.
        /// </summary>
        protected virtual void OnDisposing()
        {
        }

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Notifica que uma propriedade foi alterada
        /// </summary>
        /// <param name="propertyName">Nome da propriedade (auto-preenchido pelo compilador)</param>
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            // VALIDAÇÃO CRÍTICA: Não notificar se disposed
            if (_isDisposed) return;
            
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Notifica TODAS as propriedades, forcing os bindings a reavaliar.
        /// Usado ao trocar o idioma, pois os textos traduzidos são calculados nos getters.
        /// Nome vazio/null faz o WPF tratar como "todas as propriedades mudaram".
        /// </summary>
        public void RefreshAllLocalizedProperties()
        {
            if (_isDisposed) return;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }

        /// <summary>
        /// Define o valor de uma propriedade e notifica a mudança
        /// </summary>
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null, Action? onChanged = null)
        {
            // VALIDAÇÃO CRÍTICA: Não modificar se disposed
            if (_isDisposed) return false;

            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            onChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Define o valor de uma propriedade com callback adicional
        /// </summary>
        protected bool SetProperty<T>(ref T field, T value, Action onChanged, [CallerMemberName] string? propertyName = null)
        {
            if (SetProperty(ref field, value, propertyName))
            {
                onChanged?.Invoke();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Executa uma ação de forma segura, tratando exceções
        /// </summary>
        /// <param name="action">Ação a executar</param>
        /// <param name="busyMessage">Mensagem de progresso</param>
        /// <param name="onError">Callback de erro</param>
        /// <param name="skipIsBusy">Se TRUE, não mexe no estado de IsBusy do ViewModel (útil para GlobalProgressService)</param>
        protected async System.Threading.Tasks.Task ExecuteSafeAsync(
            Func<System.Threading.Tasks.Task> action,
            string? busyMessage = null,
            Action<Exception>? onError = null,
            bool skipIsBusy = false)
        {
            if (!skipIsBusy && IsBusy) return;

            try
            {
                if (!skipIsBusy)
                {
                    IsBusy = true;
                    BusyMessage = busyMessage ?? LocalizationService.Instance.GetString("Processing");
                }
                
                await System.Threading.Tasks.Task.Run(() => action());
            }
            catch (Exception ex)
            {
                onError?.Invoke(ex);
                App.LoggingService?.LogError($"[ViewModel] Erro: {ex.Message}", ex);
            }
            finally
            {
                if (!skipIsBusy)
                {
                    IsBusy = false;
                    BusyMessage = string.Empty;
                }
            }
        }

        #endregion


        #region IDisposable

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_isDisposed) return;

            if (disposing)
            {
                // Liberar recursos gerenciados
                OnDisposing();
            }

            _isDisposed = true;
        }


        #endregion
    }
}
