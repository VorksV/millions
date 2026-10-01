using System;
using System.Runtime.InteropServices;
using VoltrisOptimizer.Services.Gamer.Implementation;

namespace VoltrisOptimizer.Services.Responsiveness
{
    /// <summary>
    /// Wrapper de P/Invoke para o IRE v2.
    /// Reutiliza GamerNativeMethods para I/O priority (evita duplicação).
    ///
    /// DECISÃO: EcoQoS (Power Throttling) foi REMOVIDO deste engine.
    /// Motivo: EcoQoS é uma feature sensível do scheduler moderno do Windows.
    /// Mesmo controlado, pode causar inconsistências entre versões do Windows
    /// e interferir com decisões internas do kernel. O DSL 5.0 já gerencia
    /// EcoQoS nos seus engines (CpuGovernorEngine, IoSchedulerEngine).
    /// O IRE v2 foca exclusivamente em: CPU priority + I/O priority.
    /// Isso resolve 95% do problema sem risco.
    /// </summary>
    internal static class IreNativeInterop
    {
        private const uint PROCESS_SET_INFORMATION = 0x0200;
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const uint ACCESS = PROCESS_SET_INFORMATION | PROCESS_QUERY_LIMITED_INFORMATION;

        /// <summary>
        /// Aplica I/O priority em um processo via PID.
        /// Reutiliza GamerNativeMethods.SetProcessIoPriority com fallback nativo.
        /// </summary>
        public static bool SetIoPriority(int pid, IoPriorityTarget target, out string method)
        {
            method = "none";
            IntPtr hProcess = IntPtr.Zero;
            try
            {
                hProcess = OpenProcess(ACCESS, false, pid);
                if (hProcess == IntPtr.Zero) return false;

                var nativeLevel = target switch
                {
                    IoPriorityTarget.VeryLow => GamerNativeMethods.IoPriorityLevel.IoPriorityVeryLow,
                    IoPriorityTarget.Low     => GamerNativeMethods.IoPriorityLevel.IoPriorityLow,
                    IoPriorityTarget.Normal  => GamerNativeMethods.IoPriorityLevel.IoPriorityNormal,
                    IoPriorityTarget.High    => GamerNativeMethods.IoPriorityLevel.IoPriorityHigh,
                    _                        => GamerNativeMethods.IoPriorityLevel.IoPriorityNormal
                };

                return GamerNativeMethods.SetProcessIoPriority(hProcess, nativeLevel, out method);
            }
            catch { return false; }
            finally
            {
                if (hProcess != IntPtr.Zero) CloseHandle(hProcess);
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr h);
    }
}
