using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VoltrisOptimizer.UI.ViewModels
{
    /// <summary>
    /// ViewModel base com implementação de INotifyPropertyChanged
    /// Fornece funcionalidade comum para todos ViewModels
    /// </summary>
    public class BaseViewModel : INotifyPropertyChanged, System.IDisposable
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T backingStore, T value, [CallerMemberName] string propertyName = "")
        {
            if (EqualityComparer<T>.Default.Equals(backingStore, value))
                return false;

            backingStore = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        public virtual void Dispose() { }
    }
}
