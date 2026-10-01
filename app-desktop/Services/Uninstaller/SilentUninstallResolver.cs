using System;
using System.IO;

namespace VoltrisOptimizer.Services.Uninstaller
{
    public class SilentUninstallResolver
    {
        public string Resolve(string uninstallString, string? installLocation)
        {
            if (string.IsNullOrWhiteSpace(uninstallString)) return string.Empty;

            App.LoggingService?.LogDebug($"[Uninstaller] Resolvendo comando silencioso para: {uninstallString}");
            
            uninstallString = uninstallString.Trim();

            string resolved = ProcessHeuristics(uninstallString);
            
            App.LoggingService?.LogInfo($"[Uninstaller] Comando resolvido: {resolved}");
            return resolved;
        }

        private string ProcessHeuristics(string uninstallString)
        {
            // 1. MSI Executable
            // Requirement 4: msiexec /x {GUID} /qn /norestart
            if (uninstallString.Contains("msiexec", StringComparison.OrdinalIgnoreCase) || 
                uninstallString.Contains(".msi", StringComparison.OrdinalIgnoreCase) ||
                (uninstallString.Contains("{") && uninstallString.Contains("}")))
            {
                return ResolveMsi(uninstallString);
            }

            // 2. EXE Executable
            // Requirement 5: Use EXACT command if it's an EXE and already has silent parameters
            // Requirement 6: Only apply heuristics if no silent parameters exist
            
            string[] silentFlags = { "/s", "/silent", "/quiet", "/q", "/qn", "/passive", "--silent", "--quiet", "--uninstall" };
            bool hasSilentFlag = false;
            foreach (var flag in silentFlags)
            {
                if (uninstallString.Contains(flag, StringComparison.OrdinalIgnoreCase))
                {
                    hasSilentFlag = true;
                    break;
                }
            }

            if (hasSilentFlag)
            {
                return uninstallString; // Return exactly as requested for EXE with existing params
            }

            // 3. Heuristics (Fallback when no silent params identified)
            
            // Inno Setup
            if (uninstallString.Contains("unins000", StringComparison.OrdinalIgnoreCase))
            {
                return $"{uninstallString} /VERYSILENT /SUPPRESSMSGBOXES /NORESTART";
            }

            // NSIS
            if (uninstallString.Contains("uninstall.exe", StringComparison.OrdinalIgnoreCase) || 
                uninstallString.Contains("uninst.exe", StringComparison.OrdinalIgnoreCase))
            {
                return $"{uninstallString} /S";
            }

            // InstallShield
            if (uninstallString.Contains("setup.exe", StringComparison.OrdinalIgnoreCase))
            {
                return $"{uninstallString} /s /v\"/qn /norestart\"";
            }

            return uninstallString; // Default: exact command
        }

        private string ResolveMsi(string uninstallString)
        {
            // Extract the GUID or reformat to /x {GUID} /qn /norestart
            if (uninstallString.Contains("/I", StringComparison.OrdinalIgnoreCase))
            {
                uninstallString = uninstallString.Replace("/I", "/X", StringComparison.OrdinalIgnoreCase);
            }
            
            if (!uninstallString.Contains("/X", StringComparison.OrdinalIgnoreCase))
            {
                int braceIndex = uninstallString.IndexOf('{');
                if (braceIndex != -1)
                {
                    uninstallString = $"msiexec.exe /X{uninstallString.Substring(braceIndex)}";
                }
                else if (!uninstallString.Contains("msiexec", StringComparison.OrdinalIgnoreCase))
                {
                    uninstallString = $"msiexec.exe /X {uninstallString}";
                }
            }

            if (!uninstallString.Contains("/qn", StringComparison.OrdinalIgnoreCase))
            {
                uninstallString += " /qn /norestart";
            }

            return uninstallString;
        }
    }
}
