using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Interfaces
{
    public interface INavigationService
    {
        void NavigateTo(string viewKey);
        bool NavigateTo(AppPage page, object? parameter = null);
        void ShowModal(string viewKey);
        void CloseModal();
        bool GoBack();
    }
}
