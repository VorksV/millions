using System;
using System.Security.Principal;

namespace VoltrisOptimizer.Utils
{
    /// <summary>
    /// LEGITIMATE ADMINISTRATOR CHECK (NO PRIVILEGE ESCALATION)
    /// 
    /// Purpose: Verify if application is running with admin rights
    /// Compliance: Read-only check, no privilege modification
    /// Transparency: Shows clear message to user if admin required
    /// 
    /// WHY THIS IS LEGITIMATE:
    /// - Read-only operation (no system modification)
    /// - Standard .NET API (WindowsIdentity.GetCurrent)
    /// - User-friendly messaging
    /// - No silent elevation attempts
    /// - Documented Microsoft pattern
    /// </summary>
    public static class AdminChecker
    {
        /// <summary>
        /// Check if running as administrator (READ-ONLY CHECK)
        /// Does NOT attempt to elevate privileges
        /// </summary>
        public static bool IsAdministrator()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    WindowsPrincipal principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch (Exception)
            {
                // If we can't determine, assume not admin
                return false;
            }
        }
        
        /// <summary>
        /// Show user-friendly message if admin required (TRANSPARENT COMMUNICATION)
        /// </summary>
        public static void ShowAdminRequiredMessage(string feature = "system optimization")
        {
            string message = $"Voltris Optimizer requires administrator privileges for {feature}.\n\n" +
                           "This is necessary to:\n" +
                           "• Adjust system timer resolution (reduce input lag)\n" +
                           "• Optimize process priorities\n" +
                           "• Configure power plans\n" +
                           "• Apply performance tweaks\n\n" +
                           "How to run as administrator:\n" +
                           "1. Close this application\n" +
                           "2. Right-click on VoltrisOptimizer.exe\n" +
                           "3. Select 'Run as administrator'\n\n" +
                           "Your system will prompt for confirmation (UAC).";
            
            try
            {
                System.Windows.MessageBox.Show(
                    message,
                    "Administrator Privileges Required",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information
                );
            }
            catch
            {
                // Fallback to console if GUI not available
                Console.WriteLine("═══════════════════════════════════════════════════════");
                Console.WriteLine("ADMINISTRATOR PRIVILEGES REQUIRED");
                Console.WriteLine("═══════════════════════════════════════════════════════");
                Console.WriteLine(message);
                Console.WriteLine("═══════════════════════════════════════════════════════");
            }
        }
        
        /// <summary>
        /// Get current user name (for logging/transparency)
        /// </summary>
        public static string GetCurrentUserName()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    return identity.Name ?? "Unknown";
                }
            }
            catch
            {
                return "Unknown";
            }
        }
        
        /// <summary>
        /// Check if running in elevated context and log it (TRANSPARENCY)
        /// </summary>
        public static void LogPrivilegeStatus(VoltrisOptimizer.Services.ILoggingService logger)
        {
            try
            {
                bool isAdmin = IsAdministrator();
                string userName = GetCurrentUserName();
                
                logger.LogInfo("╔════════════════════════════════════════════════════════╗");
                logger.LogInfo("║           PRIVILEGE STATUS (TRANSPARENCY)              ║");
                logger.LogInfo("╚════════════════════════════════════════════════════════╝");
                logger.LogInfo($"[Privileges] Current user: {userName}");
                logger.LogInfo($"[Privileges] Administrator: {(isAdmin ? "YES" : "NO")}");
                
                if (isAdmin)
                {
                    logger.LogSuccess("[Privileges] ✓ Running with administrator privileges");
                    logger.LogInfo("[Privileges] Full optimization features available");
                }
                else
                {
                    logger.LogWarning("[Privileges] ⚠ Running without administrator privileges");
                    logger.LogWarning("[Privileges] Some optimization features may be limited");
                    logger.LogInfo("[Privileges] Restart as administrator for full functionality");
                }
            }
            catch (Exception ex)
            {
                logger.LogError($"[Privileges] Error checking status: {ex.Message}");
            }
        }
    }
}
