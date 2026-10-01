using System;
using System.Runtime.InteropServices;
using System.Text;

namespace VoltrisOptimizer.Services.Drivers
{
    public static class SetupApiHelper
    {
        private const uint DIF_PROPERTYCHANGE = 0x00000012;
        private const uint DICS_ENABLE = 0x00000001;
        private const uint DICS_DISABLE = 0x00000002;
        private const uint DICS_FLAG_GLOBAL = 0x00000001;
        private const uint DIGCF_ALLCLASSES = 0x00000004;
        private const uint DIGCF_PRESENT = 0x00000002;

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVINFO_DATA
        {
            public uint cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_PROPCHANGE_PARAMS
        {
            public SP_CLASSINSTALL_HEADER ClassInstallHeader;
            public uint StateChange;
            public uint Scope;
            public uint HwProfile;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_CLASSINSTALL_HEADER
        {
            public uint cbSize;
            public uint InstallFunction;
        }

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(IntPtr ClassGuid, IntPtr Enumerator, IntPtr hwndParent, uint Flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInfo(IntPtr DeviceInfoSet, uint MemberIndex, ref SP_DEVINFO_DATA DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SetupDiGetDeviceInstanceId(IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData, StringBuilder DeviceInstanceId, uint DeviceInstanceIdSize, out uint RequiredSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiSetClassInstallParams(IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData, ref SP_PROPCHANGE_PARAMS ClassInstallParams, uint ClassInstallParamsSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiCallClassInstaller(uint InstallFunction, IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        public static bool RestartDevice(string deviceInstanceId)
        {
            if (string.IsNullOrEmpty(deviceInstanceId)) return false;

            IntPtr infoSet = SetupDiGetClassDevs(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, DIGCF_ALLCLASSES | DIGCF_PRESENT);
            if (infoSet == new IntPtr(-1)) return false;

            try
            {
                SP_DEVINFO_DATA devData = new SP_DEVINFO_DATA();
                devData.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA));
                uint index = 0;

                while (SetupDiEnumDeviceInfo(infoSet, index, ref devData))
                {
                    StringBuilder sbId = new StringBuilder(2048);
                    if (SetupDiGetDeviceInstanceId(infoSet, ref devData, sbId, (uint)sbId.Capacity, out _))
                    {
                        string currentId = sbId.ToString();
                        if (currentId.Equals(deviceInstanceId, StringComparison.OrdinalIgnoreCase))
                        {
                            // 1. DESATIVA
                            SetDeviceState(infoSet, devData, DICS_DISABLE);
                            System.Threading.Thread.Sleep(1000);
                            // 2. ATIVA (Isso força a carga do novo driver)
                            SetDeviceState(infoSet, devData, DICS_ENABLE);
                            return true;
                        }
                    }
                    index++;
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[SetupApiHelper] Erro ao reiniciar hardware: {ex.Message}");
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(infoSet);
            }

            return false;
        }

        private static void SetDeviceState(IntPtr infoSet, SP_DEVINFO_DATA devData, uint state)
        {
            SP_PROPCHANGE_PARAMS pcp = new SP_PROPCHANGE_PARAMS();
            pcp.ClassInstallHeader = new SP_CLASSINSTALL_HEADER();
            pcp.ClassInstallHeader.cbSize = (uint)Marshal.SizeOf(typeof(SP_CLASSINSTALL_HEADER));
            pcp.ClassInstallHeader.InstallFunction = DIF_PROPERTYCHANGE;
            pcp.StateChange = state;
            pcp.Scope = DICS_FLAG_GLOBAL;
            pcp.HwProfile = 0;

            if (SetupDiSetClassInstallParams(infoSet, ref devData, ref pcp, (uint)Marshal.SizeOf(typeof(SP_PROPCHANGE_PARAMS))))
            {
                SetupDiCallClassInstaller(DIF_PROPERTYCHANGE, infoSet, ref devData);
            }
        }
    }
}
