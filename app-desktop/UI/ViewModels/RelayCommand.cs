using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace VoltrisOptimizer.UI.ViewModels
{
    /// <summary>
    /// Implementação universal de ICommand para MVVM.
    /// Suporta comandos síncronos, assíncronos, com e sem parâmetros.
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action<object?>? _execute;
        private readonly Func<object?, Task>? _executeAsync;
        private readonly Predicate<object?>? _canExecute;
        private bool _isExecuting;

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        // ─── CONSTRUTORES SÍNCRONOS ──────────────────────────────────────────────

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
            : this(execute != null ? (Action<object?>)(_ => execute()) : null!, 
                   canExecute != null ? (Predicate<object?>)(_ => canExecute()) : null) { }

        public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        // ─── CONSTRUTORES ASSÍNCRONOS ────────────────────────────────────────────

        public RelayCommand(Func<Task> executeAsync, Func<bool>? canExecute = null)
            : this(executeAsync != null ? (Func<object?, Task>)(_ => executeAsync()) : null!, 
                   canExecute != null ? (Predicate<object?>)(_ => canExecute()) : null) { }

        public RelayCommand(Func<object?, Task> executeAsync, Predicate<object?>? canExecute = null)
        {
            _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
            _canExecute = canExecute;
        }

        // ─── MÉTODOS ICOMMAND ──────────────────────────────────────────────────

        public bool CanExecute(object? parameter)
        {
            // Evita reentrada em comandos assíncronos
            if (_isExecuting) return false;
            
            return _canExecute?.Invoke(parameter) ?? true;
        }

        public void Execute(object? parameter)
        {
            _ = ExecuteAsync(parameter);
        }

        public async Task ExecuteAsync(object? parameter)
        {
            if (_isExecuting) return;

            try
            {
                _isExecuting = true;
                RaiseCanExecuteChanged();

                if (_executeAsync != null)
                {
                    await _executeAsync(parameter);
                }
                else
                {
                    _execute?.Invoke(parameter);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RelayCommand] Exceção capturada: {ex.Message}");
                App.LoggingService?.LogError($"[RelayCommand] Erro ao executar comando: {ex.Message}", ex);
            }
            finally
            {
                _isExecuting = false;
                RaiseCanExecuteChanged();
            }
        }

        public void RaiseCanExecuteChanged()
        {
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
