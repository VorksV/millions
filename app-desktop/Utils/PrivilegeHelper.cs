using System;
using System.Runtime.InteropServices;

namespace VoltrisOptimizer.Utils
{
    /// <summary>
    /// Auxiliar para manipulação de privilégios do Windows (LUID, TokenPrivileges)
    /// </summary>
    public static class PrivilegeHelper
    {
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges, ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID_AND_ATTRIBUTES
        {
            public LUID Luid;
            public uint Attributes;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_PRIVILEGES
        {
            public uint PrivilegeCount;
            public LUID_AND_ATTRIBUTES Privilege;
        }

        private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        private const uint TOKEN_QUERY = 0x0008;
        private const uint SE_PRIVILEGE_ENABLED = 0x00000002;

              public const string SE_PROFILE_SINGLE_PROCESS_NAME = "SeProfileSingleProcessPrivilege";
              public const string SE_INCREASE_QUOTA_NAME = "SeIncreaseQuotaPrivilege";
              /// <summary>
              /// Necessário para <c>EmptyWorkingSet</c> em processos de outra conta.
              /// Sem ele a chamada falha com ERROR_ACCESS_DENIED (5) mesmo com o app
              /// elevado, porque o UAC entrega o token com os privilégios desabilitados.
              /// </summary>
              public const string SE_DEBUG_NAME = "SeDebugPrivilege";

        /// <summary>
        /// Habilita um privilégio específico para o processo atual
        /// </summary>
        public static bool EnablePrivilege(string privilegeName)
        {
            IntPtr hToken;
            LUID luid;
            TOKEN_PRIVILEGES tp;

            App.LoggingService?.LogDebug($"[PrivilegeHelper] Solicitando privilégio: {privilegeName}");

            if (!OpenProcessToken(System.Diagnostics.Process.GetCurrentProcess().Handle, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out hToken))
            {
                App.LoggingService?.LogWarning($"[PrivilegeHelper] Falha ao abrir token do processo para {privilegeName}");
                return false;
            }

            try
            {
                if (!LookupPrivilegeValue(null, privilegeName, out luid))
                {
                    App.LoggingService?.LogWarning($"[PrivilegeHelper] Falha ao localizar valor do privilégio {privilegeName}");
                    return false;
                }

                tp.PrivilegeCount = 1;
                tp.Privilege.Luid = luid;
                tp.Privilege.Attributes = SE_PRIVILEGE_ENABLED;

                if (!AdjustTokenPrivileges(hToken, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero))
                {
                    App.LoggingService?.LogWarning($"[PrivilegeHelper] Falha ao ajustar privilégio {privilegeName}");
                    return false;
                }

                var error = Marshal.GetLastWin32Error();
                if (error != 0)
                {
                    App.LoggingService?.LogWarning($"[PrivilegeHelper] Ajuste de {privilegeName} com erro Win32: {error}");
                    return false;
                }

                App.LoggingService?.LogSuccess($"[PrivilegeHelper] Privilégio {privilegeName} habilitado.");
                return true;
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[PrivilegeHelper] Erro ao habilitar {privilegeName}", ex);
                return false;
            }
            finally
            {
                CloseHandle(hToken);
            }
        }
    }
}
