using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using Microsoft.Win32;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Helpers
{
    public static class AppIconExtractor
    {
        private static readonly string IconCacheDirectory = Path.Combine(Path.GetTempPath(), "VoltrisOptimizer_AppIcons");

        /// <summary>
        /// Clears the icon cache directory to force fresh icon extraction
        /// </summary>
        public static void ClearIconCache()
        {
            try
            {
                if (Directory.Exists(IconCacheDirectory))
                {
                    foreach (var file in Directory.GetFiles(IconCacheDirectory, "*.png"))
                    {
                        try
                        {
                            File.Delete(file);
                        }
                        catch { }
                    }
                    Console.WriteLine("[IconExtractor] Cache de ícones limpo.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[IconExtractor] Erro ao limpar cache: {ex.Message}");
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern int ExtractIconEx(string lpszFile, int nIconIndex, IntPtr[] phiconLarge, IntPtr[]? phiconSmall, int nIcons);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        private const uint SHGFI_ICON = 0x100;
        private const uint SHGFI_LARGEICON = 0x0;
        private const uint SHGFI_USEFILEATTRIBUTES = 0x10;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private static string GetSafeIconFileName(string identity)
        {
            var safe = new string(identity.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());
            if (safe.Length > 60) safe = safe[..60];
            return $"{safe}_{Math.Abs(identity.GetHashCode())}.png";
        }

        public static async Task<string> ExtractLogoPathAsync(AppInfo app)
        {
            // Ensure cache directory exists
            if (!Directory.Exists(IconCacheDirectory))
                Directory.CreateDirectory(IconCacheDirectory);

            // Create default icon if not exists
            var defaultIcon = Path.Combine(IconCacheDirectory, "defaulticon.png");
            if (!File.Exists(defaultIcon))
            {
                try
                {
                    var largeIcons = new IntPtr[1];
                    ExtractIconEx(@"C:\Windows\System32\imageres.dll", 152, largeIcons, null, 1);
                    var hIcon = largeIcons[0];
                    if (hIcon != IntPtr.Zero)
                    {
                        using var clonedIcon = (Icon)Icon.FromHandle(hIcon).Clone();
                        DestroyIcon(hIcon);
                        using var bmp = clonedIcon.ToBitmap();
                        bmp.Save(defaultIcon, ImageFormat.Png);
                    }
                }
                catch { }
            }

            string appIdentity = $"{app.Name}|{app.InstallLocation ?? ""}";
            string cachedPath = Path.Combine(IconCacheDirectory, GetSafeIconFileName(appIdentity));

            // Check cache - prevent poisoned cache (files < 2KB are likely corrupted)
            if (File.Exists(cachedPath))
            {
                if (new FileInfo(cachedPath).Length > 2048)
                    return cachedPath;
                
                // Delete corrupted cache
                try { File.Delete(cachedPath); } catch { }
            }

            try
            {
                if (app.IsWin32)
                {
                    var path = await ExtractWin32Icon(app, cachedPath);
                    return path;
                }
                else
                {
                    return await ExtractUwpIcon(app.InstallLocation, cachedPath, defaultIcon);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[IconExtractor] Exceção ao processar ícone de {app.Name}: {ex.Message}");
                return defaultIcon;
            }
        }

        private static async Task<string> ExtractWin32Icon(AppInfo app, string cachedPath)
        {
            var defaultIcon = Path.Combine(IconCacheDirectory, "defaulticon.png");
            
            if (string.IsNullOrEmpty(app.InstallLocation) && string.IsNullOrEmpty(app.DisplayIcon))
                return defaultIcon;

            // 1. Try DisplayIcon from registry first
            if (!string.IsNullOrEmpty(app.DisplayIcon))
            {
                string iconPath = CleanPath(app.DisplayIcon);
                Console.WriteLine($"[IconExtractor] Tentando DisplayIcon para {app.Name}: {iconPath}");
                
                if (File.Exists(iconPath))
                {
                    var large = new IntPtr[1];
                    try
                    {
                        ExtractIconEx(iconPath, 0, large, null, 1);
                        if (large[0] != IntPtr.Zero)
                        {
                            using var icon = (Icon)Icon.FromHandle(large[0]).Clone();
                            DestroyIcon(large[0]);
                            await SaveIcon(icon, cachedPath);
                            Console.WriteLine($"[IconExtractor] Ícone extraído com sucesso de DisplayIcon: {cachedPath}");
                            return cachedPath;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[IconExtractor] Erro ao extrair de DisplayIcon: {ex.Message}");
                    }
                }
            }

            // 2. Scan InstallLocation for largest EXE
            if (!string.IsNullOrEmpty(app.InstallLocation) && Directory.Exists(app.InstallLocation))
            {
                Console.WriteLine($"[IconExtractor] Escaneando pasta de instalação para {app.Name} em {app.InstallLocation}");
                
                try
                {
                    var exe = Directory
                        .EnumerateFiles(app.InstallLocation, "*.exe", SearchOption.AllDirectories)
                        .OrderByDescending(f => new FileInfo(f).Length)
                        .FirstOrDefault();

                    if (exe != null)
                    {
                        Console.WriteLine($"[IconExtractor] Maior EXE encontrado: {exe}");
                        var large = new IntPtr[1];
                        ExtractIconEx(exe, 0, large, null, 1);

                        if (large[0] != IntPtr.Zero)
                        {
                            using var icon = (Icon)Icon.FromHandle(large[0]).Clone();
                            DestroyIcon(large[0]);
                            await SaveIcon(icon, cachedPath);
                            Console.WriteLine($"[IconExtractor] Ícone extraído com sucesso do EXE: {cachedPath}");
                            return cachedPath;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[IconExtractor] Erro ao escanear InstallLocation: {ex.Message}");
                }
            }

            // 3. Try UninstallString executable
            if (!string.IsNullOrEmpty(app.UninstallString))
            {
                string uninstExe = CleanPath(app.UninstallString);
                if (uninstExe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(uninstExe))
                {
                    try
                    {
                        var large = new IntPtr[1];
                        ExtractIconEx(uninstExe, 0, large, null, 1);
                        if (large[0] != IntPtr.Zero)
                        {
                            using var icon = (Icon)Icon.FromHandle(large[0]).Clone();
                            DestroyIcon(large[0]);
                            await SaveIcon(icon, cachedPath);
                            Console.WriteLine($"[IconExtractor] Ícone extraído de UninstallString: {cachedPath}");
                            return cachedPath;
                        }
                    }
                    catch { }
                }
            }

            Console.WriteLine($"[IconExtractor] Usando ícone padrão para {app.Name}");
            return defaultIcon;
        }

        private static string CleanPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            
            // CRITICAL: Remove icon index after comma (e.g., "App.exe,0" -> "App.exe")
            int commaIndex = path.IndexOf(',');
            if (commaIndex > 0)
            {
                path = path.Substring(0, commaIndex);
                Console.WriteLine($"[IconExtractor] Removido índice de ícone, caminho limpo: {path}");
            }

            // Remove quotes
            path = path.Replace("\"", "").Trim();

            // Expand environment variables
            path = Environment.ExpandEnvironmentVariables(path);

            // Handle spaces/arguments (for UninstallString parsing)
            if (path.Contains(" ") && !File.Exists(path))
            {
                // Simple heuristic: take the first part that ends with .exe and exists
                string[] parts = path.Split(' ');
                string current = "";
                foreach (var part in parts)
                {
                    current = string.IsNullOrEmpty(current) ? part : current + " " + part;
                    if (current.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(current))
                    {
                        path = current;
                        break;
                    }
                }
            }

            return path;
        }

        private static async Task SaveIcon(Icon icon, string path)
        {
            try
            {
                using var bmp = icon.ToBitmap();
                using var ms = new MemoryStream();
                bmp.Save(ms, ImageFormat.Png);
                await File.WriteAllBytesAsync(path, ms.ToArray());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[IconExtractor] Erro ao salvar ícone: {ex.Message}");
            }
        }

        private static int GetScale(string file)
        {
            var m = Regex.Match(file, @"Scale-(\d+)");
            return m.Success ? int.Parse(m.Groups[1].Value) : 100;
        }

        private static async Task<string> ExtractUwpIcon(string? installLocation, string cachedPath, string defaultIcon)
        {
            if (string.IsNullOrEmpty(installLocation) || !Directory.Exists(installLocation)) 
                return defaultIcon;

            try
            {
                var manifest = Directory.GetFiles(installLocation, "AppxManifest.xml", SearchOption.TopDirectoryOnly)
                    .Concat(Directory.GetFiles(installLocation, "appxmanifest.xml", SearchOption.TopDirectoryOnly))
                    .FirstOrDefault();

                if (manifest == null)
                    return defaultIcon;

                var doc = XDocument.Load(manifest);

                // Namespaces used by UWP manifests
                XNamespace foundation = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
                XNamespace uap = "http://schemas.microsoft.com/appx/manifest/uap/windows10";

                // Prefer VisualElements icons
                var visual = doc.Descendants(uap + "VisualElements").FirstOrDefault();

                var logoPath =
                    visual?.Attribute("Square44x44Logo")?.Value ??
                    visual?.Attribute("Square150x150Logo")?.Value;

                // Fallback to old <Logo> element
                if (string.IsNullOrEmpty(logoPath))
                {
                    logoPath = doc.Descendants(foundation + "Logo").FirstOrDefault()?.Value;
                }

                if (string.IsNullOrEmpty(logoPath))
                    return defaultIcon;

                logoPath = logoPath.Replace('/', '\\');
                var logoDir = Path.Combine(installLocation, Path.GetDirectoryName(logoPath) ?? "");
                var baseName = Path.GetFileNameWithoutExtension(logoPath);

                if (!Directory.Exists(logoDir))
                    return defaultIcon;

                // Collect all possible logo candidates
                var candidates = Directory.GetFiles(logoDir, baseName + "*.png");

                if (candidates.Length == 0)
                    return defaultIcon;

                // Prefer targetsize-48/64 then Scale-200
                var selected = candidates
                    .OrderByDescending(f => f.Contains("targetsize-48"))
                    .ThenByDescending(f => f.Contains("targetsize-64"))
                    .ThenBy(f => Math.Abs(GetScale(f) - 200))
                    .FirstOrDefault();

                if (selected != null && File.Exists(selected))
                {
                    File.Copy(selected, cachedPath, true);
                    return cachedPath;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[IconExtractor] Erro ao extrair ícone UWP: {ex.Message}");
            }
            
            return defaultIcon;
        }
    }
}
