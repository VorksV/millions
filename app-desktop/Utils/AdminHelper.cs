using System;
using System.Security.Principal;

namespace VoltrisOptimizer.Utils
{
    /// <summary>
    /// Helper para verificação de privilégios de administrador
    /// </summary>
    public static class AdminHelper
    {
        /// <summary>
        /// Verifica se o aplicativo está sendo executado como administrador
        /// </summary>
        public static bool IsRunningAsAdministrator()
        {
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Solicita elevação de privilégios (reinicia o aplicativo como admin)
        /// </summary>
        public static bool RequestElevation()
        {
            try
            {
                App.LoggingService?.LogInfo("[AdminHelper] Solicitando elevação de privilégios...");
                
                var exePath = System.Environment.ProcessPath ?? 
                             (System.Windows.Application.ResourceAssembly != null 
                                 ? System.Windows.Application.ResourceAssembly.Location 
                                 : null);
                if (string.IsNullOrEmpty(exePath))
                {
                    exePath = System.IO.Path.Combine(System.AppContext.BaseDirectory, "VoltrisOptimizer.exe");
                }
                
                System.Diagnostics.ProcessStartInfo processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    UseShellExecute = true,
                    WorkingDirectory = Environment.CurrentDirectory,
                    FileName = exePath,
                    Verb = "runas" 
                };

                System.Diagnostics.Process.Start(processInfo);
                App.LoggingService?.LogSuccess("[AdminHelper] Processo de elevação iniciado com sucesso.");
                return true;
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[AdminHelper] Falha ao solicitar elevação: {ex.Message}");
                return false;
            }
        }
    }
}
