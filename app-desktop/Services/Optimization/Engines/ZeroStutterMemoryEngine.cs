using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Optimization.Engines
{
    /// <summary>
    /// Pilar 2 da Ultra-Performance: Zero-Stutter Memory Lock & Standby List Cleaner
    /// Previne micro-stutters forçando o jogo a ficar travado na RAM (evitando page faults)
    /// e limpando a memória Standby (cache de disco do Windows) sem causar asfixia.
    /// </summary>
    public class ZeroStutterMemoryEngine
    {
        private readonly ILoggingService _logger;

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool LookupPrivilegeValue(string lpSystemName, string lpName, out LUID lpLuid);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, [MarshalAs(UnmanagedType.Bool)] bool DisableAllPrivileges, ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

        [DllImport("ntdll.dll")]
        private static extern int NtSetSystemInformation(int SystemInformationClass, ref int SystemInformation, int SystemInformationLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessWorkingSetSizeEx(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize, uint Flags);

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_PRIVILEGES
        {
            public uint PrivilegeCount;
            public LUID Luid;
            public uint Attributes;
        }

        private const int SystemMemoryListInformation = 80;
        private const int PurgeStandbyList = 4;
        private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        private const uint TOKEN_QUERY = 0x0008;
        private const uint SE_PRIVILEGE_ENABLED = 0x00000002;
        private const string SE_PROF_SINGLE_PROCESS_NAME = "SeProfileSingleProcessPrivilege";

        // Flags para SetProcessWorkingSetSizeEx
        private const uint QUOTA_LIMITS_HARDWS_MIN_ENABLE = 0x00000001;
        private const uint QUOTA_LIMITS_HARDWS_MAX_DISABLE = 0x00000008;

        public ZeroStutterMemoryEngine(ILoggingService logger)
        {
            _logger = logger;
            EnablePrivilege(SE_PROF_SINGLE_PROCESS_NAME);
        }

        /// <summary>
        /// Limpa o cache de Standby (arquivos em cache) do Windows sem mexer no Working Set dos processos.
        /// Isso previne "Memory Leaks" em jogos longos e stuttering por falta de RAM física.
        /// </summary>
        public void PurgeStandbyListSafe()
        {
            try
            {
                int command = PurgeStandbyList;
                int status = NtSetSystemInformation(SystemMemoryListInformation, ref command, Marshal.SizeOf(command));
                
                if (status == 0)
                {
                    _logger.LogInfo("[ZeroStutterMemory] Standby List (System Cache) expurgado com sucesso.");
                }
                else
                {
                    _logger.LogWarning($"[ZeroStutterMemory] Falha ao expurgar Standby List. NTSTATUS: {status:X}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ZeroStutterMemory] Erro no expurgo de memória: {ex.Message}");
            }
        }

        /// <summary>
        /// Define hard limit na RAM pro jogo: garante que a memória física do jogo nunca seja enviada ao arquivo de paginação.
        /// Elimina stutters de transição de cenários onde o Windows faria paging do jogo.
        /// </summary>
        public void LockGameWorkingSet(int pid)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                
                long currentWorkingSet = process.WorkingSet64;
                if (currentWorkingSet == 0) return;

                // Definimos o MinimumWorkingSet igual ao atual (HARD LOCK) 
                // e desabilitamos o Max limit para deixar o jogo crescer.
                bool success = SetProcessWorkingSetSizeEx(
                    process.Handle, 
                    (IntPtr)currentWorkingSet, 
                    (IntPtr)(-1), // Deixa infinito / gerenciado pelo OS
                    QUOTA_LIMITS_HARDWS_MIN_ENABLE | QUOTA_LIMITS_HARDWS_MAX_DISABLE
                );

                if (success)
                {
                    _logger.LogInfo($"[ZeroStutterMemory] RAM do PID {pid} blindada. Min WS: {currentWorkingSet / 1024 / 1024}MB. Anti-paging ativo.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[ZeroStutterMemory] Não foi possível blindar RAM do PID {pid}: {ex.Message}");
            }
        }

        private void EnablePrivilege(string privilege)
        {
            try
            {
                if (OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out IntPtr tokenHandle))
                {
                    if (LookupPrivilegeValue(null, privilege, out LUID luid))
                    {
                        TOKEN_PRIVILEGES tp = new TOKEN_PRIVILEGES
                        {
                            PrivilegeCount = 1,
                            Luid = luid,
                            Attributes = SE_PRIVILEGE_ENABLED
                        };

                        AdjustTokenPrivileges(tokenHandle, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
                    }
                    CloseHandle(tokenHandle);
                }
            }
            catch { }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);
    }
}
