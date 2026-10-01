using System.Windows;
using VoltrisOptimizer.UI.Controls;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services
{
    public class DialogService : IDialogService
    {
        public void ShowInfo(string title, string message)
        {
            ModernMessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public void ShowWarning(string title, string message)
        {
            ModernMessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        public void ShowError(string title, string message)
        {
            ModernMessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        public bool ShowConfirm(string title, string message)
        {
            var r = ModernMessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
            return r == MessageBoxResult.Yes;
        }
    }
}
