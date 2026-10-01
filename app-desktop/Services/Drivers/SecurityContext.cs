using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;

namespace VoltrisOptimizer.Services.Drivers
{
    /// <summary>
    /// Gerencia contexto de segurança para diferenciar processos legítimos
    /// Evita falsos positivos em atualizações próprias
    /// </summary>
    public static class SecurityContext
    {
        private static readonly string[] _ownProcessNames = {
            "VoltrisOptimizer",
            "DriverSecurityService", 
            "DriverUpdateService",
            "SecureDownloader"
        };

        private static readonly string[] _ownPaths = {
            "APLICATIVO VOLTRIS",
            "VoltrisOptimizer"
        };

        /// <summary>
        /// Verifica se o processo é do próprio Voltris
        /// </summary>
        public static bool IsOwnProcess(int processId)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                var processName = Path.GetFileNameWithoutExtension(process.ProcessName);
                var processPath = process.MainModule?.FileName ?? "";

                return _ownProcessNames.Contains(processName) ||
                       _ownPaths.Any(path => processPath.Contains(path));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Verifica se o driver é parte de atualização própria
        /// </summary>
        public static bool IsOwnDriverUpdate(string driverPath)
        {
            if (string.IsNullOrEmpty(driverPath))
                return false;

            return _ownPaths.Any(path => driverPath.Contains(path)) ||
                   driverPath.Contains("temp") || 
                   driverPath.Contains("download") ||
                   driverPath.Contains("update");
        }

        /// <summary>
        /// Verifica se está em contexto de atualização de drivers
        /// </summary>
        public static bool IsInDriverUpdateContext()
        {
            try
            {
                var currentProcess = Process.GetCurrentProcess();
                var processName = currentProcess.ProcessName;
                var commandLine = GetCommandLine(currentProcess);

                return _ownProcessNames.Contains(processName) ||
                       commandLine.Contains("--update-drivers") ||
                       commandLine.Contains("--driver-update");
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Verifica se System Restore é legítimo (criado por atualização própria)
        /// </summary>
        public static bool IsLegitimateSystemRestore(string restoreDescription)
        {
            if (string.IsNullOrEmpty(restoreDescription))
                return false;

            var legitimateKeywords = new[]
            {
                "Atualização Lote Drivers",
                "Intel(R) Wireless",
                "Voltris Driver Update",
                "Driver Update"
            };

            return legitimateKeywords.Any(keyword => 
                restoreDescription.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Obtém linha de comando do processo
        /// </summary>
        private static string GetCommandLine(Process process)
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {process.Id}");
                
                foreach (System.Management.ManagementObject obj in searcher.Get())
                {
                    return obj["CommandLine"]?.ToString() ?? "";
                }
            }
            catch
            {
                return "";
            }

            return "";
        }

        /// <summary>
        /// Verifica se o usuário atual tem privilégios de administrador
        /// </summary>
        public static bool IsRunningAsAdministrator()
        {
            try
            {
                var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Obtém modo de segurança baseado no contexto atual
        /// </summary>
        public static SecurityMode GetCurrentSecurityMode()
        {
            if (IsInDriverUpdateContext())
                return SecurityMode.Maintenance;

            if (IsRunningAsAdministrator())
                return SecurityMode.Administrative;

            return SecurityMode.Standard;
        }
    }

    /// <summary>
    /// Modos de segurança do sistema
    /// </summary>
    public enum SecurityMode
    {
        /// <summary>
        /// Modo padrão - verificação rigorosa
        /// </summary>
        Standard,

        /// <summary>
        /// Modo administrativo - privilégios elevados
        /// </summary>
        Administrative,

        /// <summary>
        /// Modo manutenção - verificação relaxada para atualizações próprias
        /// </summary>
        Maintenance
    }
}
