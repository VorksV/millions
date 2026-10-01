using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using VoltrisOptimizer.Core;
using VoltrisOptimizer.UI.ViewModels;

namespace VoltrisOptimizer.UI.Views
{
    public partial class PrivacyView : UserControl
    {
        public PrivacyView()
        {
            InitializeComponent();
            App.LoggingService?.LogTrace("[UI] Navegou para PrivacyView");
        }
    }
}
