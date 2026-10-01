using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace VoltrisOptimizer.Utils.Win32
{
    public static class Win32FileHelper
    {
        private const int RmRebootReasonNone = 0;
        private const int CCH_RM_MAX_APP_NAME = 255;
        private const int CCH_RM_MAX_SVC_NAME = 63;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RM_PROCESS_INFO
        {
            public RM_UNIQUE_PROCESS Process;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_APP_NAME + 1)]
            public string strAppName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_SVC_NAME + 1)]
            public string strServiceShortName;
            public RM_APP_TYPE ApplicationType;
            public uint AppStatus;
            public uint TSSessionId;
            [MarshalAs(UnmanagedType.Bool)]
            public bool bRestartable;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RM_UNIQUE_PROCESS
        {
            public int dwProcessId;
            public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
        }

        private enum RM_APP_TYPE
        {
            RmUnknownApp = 0,
            RmMainWindow = 1,
            RmOtherWindow = 2,
            RmService = 3,
            RmExplorer = 4,
            RmConsole = 5,
            RmCritical = 1000
        }

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmStartSession(out uint pSessionHandle, uint dwSessionFlags, string strSessionKey);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmEndSession(uint pSessionHandle);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmRegisterResources(uint pSessionHandle, uint nFiles, string[] rgsFileNames, 
            uint nApplications, RM_UNIQUE_PROCESS[] rgApplications, uint nServices, string[] rgsServiceNames);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmGetList(uint pSessionHandle, out uint pnProcInfoNeeded, ref uint pnProcInfo,
            [In, Out] RM_PROCESS_INFO[] rgAffectedApps, ref uint lpdwRebootReasons);

        [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool MoveFileEx(string lpExistingFileName, string? lpNewFileName, uint dwFlags);

        private const uint MOVEFILE_DELAY_UNTIL_REBOOT = 0x04;

        /// <summary>
        /// Obtém uma lista de processos que estão bloqueando um arquivo específico.
        /// </summary>
        public static List<Process> GetProcessesLockingFile(string filePath)
        {
            var lockingProcesses = new List<Process>();
            var sessionKey = Guid.NewGuid().ToString();

            if (RmStartSession(out uint handle, 0, sessionKey) != 0)
                return lockingProcesses;

            try
            {
                var resources = new string[] { filePath };
                if (RmRegisterResources(handle, (uint)resources.Length, resources, 0, null, 0, null) != 0)
                    return lockingProcesses;

                uint pnProcInfoNeeded = 0;
                uint pnProcInfo = 0;
                uint rebootReasons = RmRebootReasonNone;

                // Primeira chamada para obter o tamanho necessário
                int res = RmGetList(handle, out pnProcInfoNeeded, ref pnProcInfo, null, ref rebootReasons);
                if (res == 234) // ERROR_MORE_DATA
                {
                    var processInfo = new RM_PROCESS_INFO[pnProcInfoNeeded];
                    pnProcInfo = pnProcInfoNeeded;

                    if (RmGetList(handle, out pnProcInfoNeeded, ref pnProcInfo, processInfo, ref rebootReasons) == 0)
                    {
                        foreach (var info in processInfo)
                        {
                            try
                            {
                                var process = Process.GetProcessById(info.Process.dwProcessId);
                                lockingProcesses.Add(process);
                            }
                            catch (ArgumentException) { /* Processo já encerrou */ }
                            catch (Exception) { /* Outros erros de acesso */ }
                        }
                    }
                }
            }
            finally
            {
                RmEndSession(handle);
            }

            return lockingProcesses;
        }

        /// <summary>
        /// Agenda a exclusão de um arquivo para o próximo reboot do sistema.
        /// </summary>
        public static bool ScheduleDeleteOnReboot(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;
            return MoveFileEx(filePath, null, MOVEFILE_DELAY_UNTIL_REBOOT);
        }

        /// <summary>
        /// Tenta forçar a remoção de um arquivo, encerrando bloqueadores se necessário.
        /// </summary>
        public static bool ForceDeleteFile(string filePath, out string error)
        {
            error = string.Empty;
            if (!System.IO.File.Exists(filePath)) return true;

            try
            {
                // 1. Tentar deleção simples
                System.IO.File.Delete(filePath);
                return true;
            }
            catch (Exception firstEx)
            {
                try
                {
                    // 2. Tentar matar processos bloqueadores
                    var lockers = GetProcessesLockingFile(filePath);
                    foreach (var locker in lockers)
                    {
                        try
                        {
                            locker.Kill(true);
                            locker.WaitForExit(1000);
                        }
                        catch { }
                    }

                    // 3. Tentar deleção novamente
                    System.IO.File.Delete(filePath);
                    return true;
                }
                catch (Exception secondEx)
                {
                    // 4. Falha persistente - agendar para reboot
                    if (ScheduleDeleteOnReboot(filePath))
                    {
                        error = "Arquivo em uso crítico. Agendado para remoção no próximo reinício.";
                        return true;
                    }
                    
                    error = secondEx.Message;
                    return false;
                }
            }
        }
    }
}
