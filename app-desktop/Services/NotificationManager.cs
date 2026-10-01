using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Toolkit.Uwp.Notifications;

namespace VoltrisOptimizer.Services
{
    /// <summary>
    /// Gerenciador profissional de notificações Toast nativas do Windows 10/11.
    /// Correção crítica: registra AUMID via atalho no Start Menu para apps não-MSIX
    /// rodando como administrador, garantindo compatibilidade com Windows 10.
    /// </summary>
    public static class NotificationManager
    {
        private static readonly string AppId = "VoltrisOptimizer";
        private static readonly string AppDisplayName = "Voltris Optimizer";
        private static DateTime _lastNotificationTime = DateTime.MinValue;
        private const int NotificationCooldownSeconds = 3;
        private static readonly object _syncLock = new();
        private static ILoggingService? _logger;
        private static bool _initialized;

        /// <summary>
        /// Inicializa o NotificationManager, registrando o atalho com AUMID
        /// necessário para Toast Notifications em apps Win32 não-MSIX.
        /// </summary>
        public static void Initialize(ILoggingService logger)
        {
            _logger = logger;
            try
            {
                EnsureShortcutWithAumid();
                _initialized = true;
                _logger?.LogInfo("[Notification] NotificationManager inicializado com sucesso");
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[Notification] Falha na inicialização do AUMID: {ex.Message}");
                _initialized = true; // Continuar mesmo assim — Show() tenta de qualquer forma
            }
        }

        /// <summary>
        /// Verifica se notificações estão habilitadas nas configurações do usuário.
        /// </summary>
        public static bool AreNotificationsEnabled()
        {
            try
            {
                var settings = SettingsService.Instance?.Settings;
                return settings?.NotificationsEnabled ?? true;
            }
            catch
            {
                return true; // Default: habilitado
            }
        }

        /// <summary>
        /// Exibe uma notificação Toast nativa do Windows 10/11.
        /// Respeita a configuração de notificações do usuário.
        /// </summary>
        /// <param name="critical">
    /// Alerta de segurança crítico. Ignora a supressão de toast do Modo Gamer:
    /// o usuário não quer ser incomodado jogando, mas silenciar uma ameaça ativa
    /// seria uma falha de segurança. Só os chamadores de segurança marcam true.
    /// </param>
    public static void Show(string title, string message, NotificationType type = NotificationType.Info, bool critical = false)
        {
            try
            {
                if (!AreNotificationsEnabled())
                {
                    _logger?.LogInfo($"[Notification] Notificações desabilitadas pelo usuário. Ignorando: {title}");
                    return;
                }

                lock (_syncLock)
                {
                    var elapsed = DateTime.Now - _lastNotificationTime;

                    // CORREÇÃO DE POLÍTICA DE COOLDOWN.
                    //
                    // A lógica anterior era: `bypassCooldown = Error || Warning`.
                    // Isso está INVERTIDO em relação à intenção declarada no
                    // comentário ("permitir bypass para ameaças críticas").
                    //
                    // `Warning` é a classe MENOS confiável de todas: é onde cai
                    // heurística estática — que é exatamente o que o pipeline do
                    // Shield produz. Dar a ela o único caminho que ignora o
                    // cooldown significa que a categoria mais ruidosa é a única
                    // que nunca é freada. Somado ao `IsSpam` do
                    // GlobalNotificationService (que também retornava `false`
                    // antes de consultar o dicionário para Warning), a cadeia
                    // inteira ficava sem limite nenhum para esta classe.
                    //
                    // evidenced no log de 27/09/2026: 15 toasts em 2 segundos
                    // para DLLs assinadas do Windows e do Windscribe.
                    //
                    // CORREÇÃO: o bypass pertence a `Error` — evidência
                    // confirmada, que não espera nenhum. `Warning` respeita o
                    // cooldown como toda classe de baixa confiança deve.
                    bool bypassCooldown = type == NotificationType.Error;

                    if (!bypassCooldown && elapsed.TotalSeconds < NotificationCooldownSeconds)
                    {
                        _logger?.LogInfo($"[Notification] Cooldown ativo ({elapsed.TotalSeconds:F1}s). Ignorando: {title}");
                        return;
                    }

                    // Atualiza sempre, inclusive no bypass: um Error deve
                    // CONSUMIR o cooldown, para que um Warning em seguida seja
                    // suprimido em vez de sair logo atrás.
                    _lastNotificationTime = DateTime.Now;
                }

                _logger?.LogInfo($"[Notification] Enviando Toast: [{type}] {title}");

                // Disparar em thread separada para não bloquear a UI
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        ShowToastDirect(title, message, type, critical);
                        _logger?.LogInfo($"[Notification] Toast exibido com sucesso: {title}");
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning($"[Notification] Falha ao exibir Toast: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[Notification] Erro crítico: {ex.Message}", ex);
            }
        }

        public static void ShowSuccess(string title, string message) => Show(title, message, NotificationType.Success);
        public static void ShowError(string title, string message) => Show(title, message, NotificationType.Error);
        public static void ShowWarning(string title, string message) => Show(title, message, NotificationType.Warning);
        public static void ShowInfo(string title, string message) => Show(title, message, NotificationType.Info);

    /// <summary>
    /// Notificação de segurança: sempre exibe o toast, mesmo com o Modo Gamer
    /// ligado. Usado para adware, ameaças e ransomware.
    /// </summary>
    public static void ShowCritical(string title, string message) => Show(title, message, NotificationType.Warning, critical: true);

        /// <summary>
        /// Exibe Toast usando Microsoft.Toolkit.Uwp.Notifications diretamente.
        /// </summary>
        private static void ShowToastDirect(string title, string message, NotificationType type, bool critical = false)
        {
            try
            {
                // Substituído Windows Toast nativo pelo Custom Toast original do VOLTRIS
                GlobalNotificationService.Show(title, message, type, null, null, critical);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[Notification] ShowToastDirect (Custom) falhou: {ex.Message}");
            }
        }

        /// <summary>
        /// Cria/atualiza atalho no Start Menu com AppUserModelId (AUMID).
        /// Isso é OBRIGATÓRIO para Toast Notifications em apps Win32 não-MSIX no Windows 10.
        /// Sem isso, CreateToastNotifier() falha silenciosamente ou não exibe nada.
        /// </summary>
        private static void EnsureShortcutWithAumid()
        {
            try
            {
                var startMenuPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                    "Programs",
                    $"{AppDisplayName}.lnk");

                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath)) return;

                // Verificar se atalho já existe e está correto
                if (File.Exists(startMenuPath))
                {
                    // Verificar se o AUMID está configurado corretamente
                    if (VerifyShortcutAumid(startMenuPath))
                    {
                        _logger?.LogInfo("[Notification] Atalho com AUMID já existe e está correto");
                        return;
                    }
                }

                // Criar atalho com AUMID usando IShellLink COM
                CreateShortcutWithAumid(startMenuPath, exePath);
                _logger?.LogInfo($"[Notification] Atalho criado/atualizado em: {startMenuPath}");
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[Notification] Erro ao criar atalho AUMID: {ex.Message}");
            }
        }

        private static bool VerifyShortcutAumid(string shortcutPath)
        {
            try
            {
                var shell = (IShellLinkW)new CShellLink();
                var persistFile = (System.Runtime.InteropServices.ComTypes.IPersistFile)shell;
                persistFile.Load(shortcutPath, 0);

                var propertyStore = (IPropertyStore)shell;
                var aumidKey = new PROPERTYKEY
                {
                    fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
                    pid = 5
                };

                propertyStore.GetValue(ref aumidKey, out var propVariant);
                var currentAumid = propVariant.pwszVal;
                PropVariantClear(ref propVariant);

                return string.Equals(currentAumid, AppId, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static void CreateShortcutWithAumid(string shortcutPath, string targetPath)
        {
            var shell = (IShellLinkW)new CShellLink();
            shell.SetPath(targetPath);
            shell.SetDescription(AppDisplayName);

            var propertyStore = (IPropertyStore)shell;
            var aumidKey = new PROPERTYKEY
            {
                fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
                pid = 5
            };

            var propVariant = new PROPVARIANT { vt = 31, pwszVal = AppId }; // VT_LPWSTR = 31
            propertyStore.SetValue(ref aumidKey, ref propVariant);
            propertyStore.Commit();

            var persistFile = (System.Runtime.InteropServices.ComTypes.IPersistFile)shell;
            persistFile.Save(shortcutPath, true);
        }

        /// <summary>
        /// Limpa recursos de notificação ao encerrar o aplicativo.
        /// </summary>
        public static void Cleanup()
        {
            try
            {
                ToastNotificationManagerCompat.Uninstall();
            }
            catch { /* Ignorar erros de cleanup */ }
        }

        #region COM Interop para criação de atalho com AUMID

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PROPVARIANT pvar);

        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        private class CShellLink { }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile, int cch, IntPtr pfd, int fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath, int cch, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
            void Resolve(IntPtr hwnd, int fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
        private interface IPropertyStore
        {
            void GetCount(out uint cProps);
            void GetAt(uint iProp, out PROPERTYKEY pkey);
            void GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
            void SetValue(ref PROPERTYKEY key, ref PROPVARIANT propvar);
            void Commit();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROPERTYKEY
        {
            public Guid fmtid;
            public uint pid;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROPVARIANT
        {
            public ushort vt;
            public ushort wReserved1;
            public ushort wReserved2;
            public ushort wReserved3;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pwszVal;
        }

        #endregion
    }

    /// <summary>
    /// Tipos de notificação disponíveis
    /// </summary>
    public enum NotificationType
    {
        Info,
        Success,
        Warning,
        Error,
        Processing
    }
}
