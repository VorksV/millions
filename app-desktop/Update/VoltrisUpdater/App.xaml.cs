using System;
using System.IO;
using System.Windows;

namespace VoltrisUpdater
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            try
            {
                var mainWindow = new MainWindow();
                mainWindow.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao iniciar o atualizador:\n{ex.Message}\n\n{ex.StackTrace}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            string errorMsg = $"Ocorreu um erro inesperado na interface (Dispatcher):\n\n{e.Exception.Message}\n\n{e.Exception.StackTrace}";
            File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "updater_error.log"), errorMsg + "\n");
            MessageBox.Show(errorMsg, "Erro Crítico", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
            Shutdown();
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                string errorMsg = $"Ocorreu um erro inesperado (AppDomain):\n\n{ex.Message}\n\n{ex.StackTrace}";
                File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "updater_error.log"), errorMsg + "\n");
                MessageBox.Show(errorMsg, "Erro Fatal", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}