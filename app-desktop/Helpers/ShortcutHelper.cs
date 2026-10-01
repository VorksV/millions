using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace VoltrisOptimizer.Helpers
{
    public static class ShortcutHelper
    {
        private const string AppUserModelId = "Voltris.Optimizer";

        public static void TryCreateShortcut()
        {
            try
            {
                App.LoggingService?.LogInfo("[AUMID] Início criação de atalho");
                var startMenu = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var programs = Path.Combine(startMenu, "Microsoft", "Windows", "Start Menu", "Programs");
                var shortcutName = "Voltris Optimizer.lnk";
                var shortcutPath = Path.Combine(programs, shortcutName);
                var exePath = System.Environment.ProcessPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VoltrisOptimizer.exe");
                var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "VosRENTE.ico");

                Directory.CreateDirectory(programs);

                bool needCreate = true;
                if (File.Exists(shortcutPath))
                {
                    try
                    {
                        var shellType = Type.GetTypeFromProgID("WScript.Shell");
                        if (shellType == null)
                        {
                            App.LoggingService?.LogWarning("[AUMID] WScript.Shell indisponível");
                            needCreate = true;
                        }
                        else
                        {
                            var shellObj = Activator.CreateInstance(shellType);
                            if (shellObj == null)
                            {
                                App.LoggingService?.LogWarning("[AUMID] Falha ao instanciar WScript.Shell");
                                needCreate = true;
                            }
                            else
                            {
                                dynamic shell = shellObj;
                                dynamic sh = shell.CreateShortcut(shortcutPath);
                                string currentTarget = sh.TargetPath as string ?? string.Empty;
                                App.LoggingService?.LogInfo($"[AUMID] Atalho existente Target={currentTarget}");
                                if (string.Equals(currentTarget, exePath, StringComparison.OrdinalIgnoreCase))
                                {
                                    needCreate = false;
                                }
                            }
                        }
                    }
                    catch { }
                }

                if (needCreate)
                {
                    var shellType = Type.GetTypeFromProgID("WScript.Shell");
                    if (shellType == null)
                    {
                        App.LoggingService?.LogWarning("[AUMID] WScript.Shell indisponível para criar atalho");
                    }
                    else
                    {
                        var shellObj = Activator.CreateInstance(shellType);
                        if (shellObj == null)
                        {
                            App.LoggingService?.LogWarning("[AUMID] Falha ao instanciar WScript.Shell para criar atalho");
                        }
                        else
                        {
                            dynamic shell = shellObj;
                            dynamic shortcut = shell.CreateShortcut(shortcutPath);
                            shortcut.TargetPath = exePath;
                            shortcut.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;
                            shortcut.IconLocation = File.Exists(iconPath) ? iconPath : exePath;
                            shortcut.Arguments = string.Empty;
                            shortcut.Description = "Voltris Optimizer";
                            shortcut.Save();
                            App.LoggingService?.LogInfo($"[AUMID] Atalho criado em {shortcutPath}");

                            SetAppUserModelId(shortcutPath, AppUserModelId);
                            App.LoggingService?.LogInfo($"[AUMID] Definido AppUserModelID='{AppUserModelId}'");
                        }
                    }
                }
                else
                {
                    try { SetAppUserModelId(shortcutPath, AppUserModelId); App.LoggingService?.LogInfo($"[AUMID] Atualizado AppUserModelID em atalho existente"); } catch { }
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[AUMID] Erro ao criar atalho: {ex.Message}", ex);
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack=4)]
        private struct PROPERTYKEY
        {
            public Guid fmtid;
            public uint pid;
            public PROPERTYKEY(Guid guid, uint id) { fmtid = guid; pid = id; }
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("886D8EEB-8CF2-4446-8A3E-081B06B4A70C")]
        private interface IPropertyStore
        {
            int GetCount(out uint cProps);
            int GetAt(uint iProp, out PROPERTYKEY pkey);
            int GetValue(ref PROPERTYKEY key, out PropVariant pv);
            int SetValue(ref PROPERTYKEY key, ref PropVariant pv);
            int Commit();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PropVariant
        {
            public ushort vt;
            public ushort wReserved1;
            public ushort wReserved2;
            public ushort wReserved3;
            public IntPtr ptr;
        }

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

        private static void SetAppUserModelId(string shortcutPath, string appId)
        {
            var key = new PROPERTYKEY(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
            var shellLink = (IShellLinkW)new CShellLink();
            var persistFile = (IPersistFile)shellLink;
            persistFile.Load(shortcutPath, 0);
            var propStore = (IPropertyStore)shellLink;
            var pv = new PropVariant();
            pv.vt = 31;
            pv.ptr = Marshal.StringToCoTaskMemUni(appId);
            propStore.SetValue(ref key, ref pv);
            propStore.Commit();
            Marshal.FreeCoTaskMem(pv.ptr);
        }
    }
}
