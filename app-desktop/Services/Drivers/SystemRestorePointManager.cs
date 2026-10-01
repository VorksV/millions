using System;
using System.Management;
using System.Runtime.InteropServices;

namespace VoltrisOptimizer.Services.Drivers
{
    public class SystemRestorePointManager
    {
        [DllImport("Srclient.dll")]
        private static extern int SRSetRestorePointW(ref RESTOREPOINTINFO pRestorePtSpec, out STATEMGRSTATUS pSMgrStatus);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RESTOREPOINTINFO
        {
            public int dwEventType;
            public int dwRestorePtType;
            public long llSequenceNumber;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szDescription;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct STATEMGRSTATUS
        {
            public int nStatus;
            public long llSequenceNumber;
        }

        private const int BEGIN_SYSTEM_CHANGE = 100;
        private const int END_SYSTEM_CHANGE = 101;
        private const int MODIFY_SETTINGS = 12;
        private const int DEVICE_DRIVER_INSTALL = 10;

        public bool CreateRestorePoint(string description)
        {
            App.LoggingService?.LogInfo($"[SystemRestore] Invocando criação de Ponto de Restauração: '{description}'");
            try
            {
                var rpi = new RESTOREPOINTINFO
                {
                    dwEventType = BEGIN_SYSTEM_CHANGE,
                    dwRestorePtType = DEVICE_DRIVER_INSTALL,
                    llSequenceNumber = 0,
                    szDescription = description
                };

                STATEMGRSTATUS status;
                int result = SRSetRestorePointW(ref rpi, out status);

                if (result == 0) // ERROR_SUCCESS
                {
                    App.LoggingService?.LogInfo($"[SystemRestore] Ponto de Restauração criado com sucesso! SequencenNumber: {status.llSequenceNumber}");
                    return true;
                }
                else
                {
                    App.LoggingService?.LogError($"[SystemRestore] A API nativa do Windows (Srclient.dll) falhou ao criar o ponto. Erro Win32: {result}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[SystemRestore] Exceção crítica ao tentar invocar P/Invoke para SRSetRestorePointW.", ex);
                return false;
            }
        }
    }
}
