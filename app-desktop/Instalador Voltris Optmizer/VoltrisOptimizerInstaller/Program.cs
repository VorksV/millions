using System;
using System.IO;
using System.Windows;

namespace VoltrisOptimizerInstaller
{
    public class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                var logPath = Path.Combine(Path.GetTempPath(), "voltris_installer_launch.log");
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] --- STARTUP ---\n");

                var app = new App();

                if (Application.Current != null && Application.Current.Resources == null)
                {
                    Application.Current.Resources = app.Resources;
                }

                var mainWindow = new MainWindow();

                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Executando App.Run()\n");
                return app.Run(mainWindow);
            }
            catch (Exception ex)
            {
                string errorMsg = $"ERRO FATAL AO INICIAR INSTALADOR:\n\n{ex.Message}\n\n{ex.StackTrace}";
                try
                {
                    File.WriteAllText(Path.Combine(Path.GetTempPath(), "voltris_installer_crash.log"), errorMsg);
                }
                catch { }

                MessageBox.Show(errorMsg, "Erro Crítico", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
        }
    }
}
