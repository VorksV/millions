using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Helpers
{
    /// <summary>
    /// Limpa atalhos órfãos do Menu Iniciar após desinstalação de aplicativos
    /// </summary>
    public static class StartMenuShortcutCleaner
    {
        private static readonly ILoggingService? _logger = App.LoggingService;

        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        private class CShellLink { }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            int GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
            int GetIDList(out IntPtr ppidl);
            int SetIDList(IntPtr pidl);
            int GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
            int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            int GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
            int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            int GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
            int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            int GetHotkey(out short pwHotkey);
            int SetHotkey(short wHotkey);
            int GetShowCmd(out int piShowCmd);
            int SetShowCmd(int iShowCmd);
            int GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
            int SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            int SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            int Resolve(IntPtr hwnd, uint fFlags);
            int SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [ComImport]
        [Guid("0000010B-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPersistFile
        {
            int GetClassID(out Guid pClassID);
            int IsDirty();
            int Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
            int Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
            int SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
            int GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
        }

        /// <summary>
        /// Remove atalhos órfãos do Menu Iniciar (atalhos que apontam para executáveis inexistentes)
        /// </summary>
        public static int CleanOrphanedShortcuts()
        {
            int removedCount = 0;

            try
            {
                _logger?.LogInfo("[ShortcutCleaner] Iniciando limpeza de atalhos órfãos do Menu Iniciar...");

                // Diretórios do Menu Iniciar
                var startMenuPaths = new List<string>
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs")
                };

                foreach (var basePath in startMenuPaths)
                {
                    if (!Directory.Exists(basePath))
                    {
                        _logger?.LogWarning($"[ShortcutCleaner] Diretório não encontrado: {basePath}");
                        continue;
                    }

                    _logger?.LogInfo($"[ShortcutCleaner] Escaneando: {basePath}");
                    removedCount += ScanAndCleanDirectory(basePath);
                }

                _logger?.LogSuccess($"[ShortcutCleaner] Limpeza concluída. {removedCount} atalhos órfãos removidos.");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[ShortcutCleaner] Erro durante limpeza: {ex.Message}", ex);
            }

            return removedCount;
        }

        private static int ScanAndCleanDirectory(string directoryPath)
        {
            int removedCount = 0;

            try
            {
                // Escanear todos os arquivos .lnk recursivamente
                var shortcutFiles = Directory.GetFiles(directoryPath, "*.lnk", SearchOption.AllDirectories);

                foreach (var shortcutPath in shortcutFiles)
                {
                    try
                    {
                        if (IsOrphanedShortcut(shortcutPath))
                        {
                            _logger?.LogInfo($"[ShortcutCleaner] Removendo atalho órfão: {shortcutPath}");
                            File.Delete(shortcutPath);
                            removedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning($"[ShortcutCleaner] Erro ao processar {shortcutPath}: {ex.Message}");
                    }
                }

                // Remover pastas vazias após limpeza
                CleanEmptyDirectories(directoryPath);
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[ShortcutCleaner] Erro ao escanear diretório {directoryPath}: {ex.Message}", ex);
            }

            return removedCount;
        }

        private static bool IsOrphanedShortcut(string shortcutPath)
        {
            try
            {
                var targetPath = GetShortcutTarget(shortcutPath);

                if (string.IsNullOrWhiteSpace(targetPath))
                {
                    _logger?.LogInfo($"[ShortcutCleaner] Atalho sem target: {shortcutPath}");
                    return true;
                }

                // Verificar se o arquivo/pasta de destino existe
                if (!File.Exists(targetPath) && !Directory.Exists(targetPath))
                {
                    _logger?.LogInfo($"[ShortcutCleaner] Target não existe: {targetPath}");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[ShortcutCleaner] Erro ao verificar atalho {shortcutPath}: {ex.Message}");
                return false; // Em caso de erro, não remover por segurança
            }
        }

        private static string? GetShortcutTarget(string shortcutPath)
        {
            try
            {
                var shellLink = (IShellLinkW)new CShellLink();
                var persistFile = (IPersistFile)shellLink;

                persistFile.Load(shortcutPath, 0);

                var targetPath = new StringBuilder(260);
                shellLink.GetPath(targetPath, targetPath.Capacity, IntPtr.Zero, 0);

                return targetPath.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static void CleanEmptyDirectories(string basePath)
        {
            try
            {
                foreach (var directory in Directory.GetDirectories(basePath, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
                {
                    try
                    {
                        if (!Directory.EnumerateFileSystemEntries(directory).Any())
                        {
                            _logger?.LogInfo($"[ShortcutCleaner] Removendo pasta vazia: {directory}");
                            Directory.Delete(directory, false);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
