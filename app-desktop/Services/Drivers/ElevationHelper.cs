using System;
using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;

namespace VoltrisOptimizer.Services.Drivers
{
    public static class ElevationHelper
    {
        public static bool IsAdministrator()
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        public static void EnsureElevated()
        {
            if (!IsAdministrator())
            {
                App.LoggingService?.LogWarning("Reiniciando aplicativo com privilégios de administrador para continuação de sistema dos Drivers...");
                
                var startInfo = new ProcessStartInfo
                {
                    FileName = Assembly.GetExecutingAssembly().Location,
                    UseShellExecute = true,
                    Verb = "runas"
                };

                try
                {
                    Process.Start(startInfo);
                }
                catch (Exception ex)
                {
                    App.LoggingService?.LogError("Falha ao elevar privilégios. O usuário pode ter cancelado o prompt UAC.", ex);
                }
                
                Environment.Exit(0);
            }
        }
    }
}
