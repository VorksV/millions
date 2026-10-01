using System;
using System.Windows;

namespace VoltrisOptimizer.UI.Views
{
    public partial class RecoveryInfoPage : Window
    {
        public RecoveryInfoPage()
        {
            App.LoggingService?.LogInfo("=== RecoveryInfoPage constructor - ENTER ===");
            InitializeComponent();
            App.LoggingService?.LogInfo("=== RecoveryInfoPage constructor - EXIT ===");
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            App.LoggingService?.LogInfo("=== BtnClose_Click - ENTER ===");
            try
            {
                App.LoggingService?.LogInfo($"BtnClose_Click - sender: {sender?.GetType().Name}, e: {e?.RoutedEvent?.Name}");

                Close();

                App.LoggingService?.LogInfo("=== BtnClose_Click - EXIT (success) ===");
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogInfo($"BtnClose_Click - ERROR: {ex.Message}");
                throw;
            }
        }
    }
}
