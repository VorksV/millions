using System;

namespace VoltrisOptimizer.Interfaces
{
    public interface IDialogService
    {
        void ShowInfo(string title, string message);
        void ShowWarning(string title, string message);
        void ShowError(string title, string message);
        bool ShowConfirm(string title, string message);
    }
}
