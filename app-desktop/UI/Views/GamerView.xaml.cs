using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Gamer.Implementation;
using VoltrisOptimizer.Services.Gamer.Interfaces;
using VoltrisOptimizer.UI.ViewModels;

namespace VoltrisOptimizer.UI.Views
{
    /// <summary>
    /// View para otimização de jogos usando MVVM
    /// </summary>
    public partial class GamerView : UserControl
    {
        public GamerView()
        {
            InitializeComponent();
            App.LoggingService?.LogTrace("[UI] Navegou para GamerView");
        }
    }
}
