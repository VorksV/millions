using System;
using System.Threading.Tasks;

namespace VoltrisOptimizer.UI.ViewModels
{
    /// <summary>
    /// Especialização do RelayCommand para garantir chamadas assíncronas de forma semântica.
    /// Resolve o erro CS0246 em ViewModels que esperam este tipo específico.
    /// </summary>
    public class AsyncRelayCommand : RelayCommand
    {
        public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null) 
            : base(execute, canExecute) { }

        public AsyncRelayCommand(Func<object?, Task> execute, Predicate<object?>? canExecute = null) 
            : base(execute, canExecute) { }


    }
}
