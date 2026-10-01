using System.Windows.Controls;
using VoltrisOptimizer.UI.ViewModels;
using VoltrisOptimizer.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace VoltrisOptimizer.UI.Views
{
    /// <summary>
    /// Interaction logic for DeviceInfoView.xaml
    /// </summary>
    public partial class DeviceInfoView : UserControl
    {
        public DeviceInfoView()
        {
            InitializeComponent();
            
            // Injeção de dependência via ServiceLocator/App.Services
            var systemInfoService = App.Services?.GetService<ISystemInfoService>();
            var loggingService = App.Services?.GetService<VoltrisOptimizer.Services.ILoggingService>();
            
            if (systemInfoService != null)
            {
                DataContext = new DeviceInfoViewModel(systemInfoService, loggingService);
            }

            // Track page view
            Loaded += (s, e) =>
            {
                App.TelemetryService?.TrackEvent(
                    "PAGE_VIEW",
                    "DeviceInfo",
                    "Load",
                    success: true
                );
            };
        }
    }
}
