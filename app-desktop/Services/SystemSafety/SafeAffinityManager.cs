using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using VoltrisOptimizer.Services.SystemChanges;
using VoltrisOptimizer.Core.Configuration;
using VoltrisOptimizer.Core.Hardware;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.SystemSafety
{
    public interface ISafeAffinityManager
    {
        bool TrySetAffinity(int processId, IntPtr affinityMask);
        bool TryPinToPerformanceCores(int processId);
    }

    /// <summary>
    /// Gerenciador Seguro de Afinidades de CPU.
    /// Rejeita a alocação baseada em cálculos perigosos (bit shifting cego).
    /// Utiliza o HardwareAwarenessManager para aplicar máscaras seguras extraídas da topologia real do Kernel.
    /// </summary>
    public sealed class SafeAffinityManager : ISafeAffinityManager
    {
        private readonly ICapabilityGuard _capabilityGuard;
        private readonly IVoltrisFeatureFlagManager _featureFlags;
        private readonly ILoggingService _logger;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessAffinityMask(IntPtr hProcess, IntPtr dwProcessAffinityMask);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetProcessAffinityMask(IntPtr hProcess, out IntPtr lpProcessAffinityMask, out IntPtr lpSystemAffinityMask);

        private const uint PROCESS_SET_INFORMATION = 0x0200;
        private const uint PROCESS_QUERY_INFORMATION = 0x0400;

        public SafeAffinityManager(ICapabilityGuard capabilityGuard, IVoltrisFeatureFlagManager featureFlags, ILoggingService logger)
        {
            _capabilityGuard = capabilityGuard;
            _featureFlags = featureFlags;
            _logger = logger;
        }

        public bool TrySetAffinity(int processId, IntPtr affinityMask)
        {
            if (!_featureFlags.UseSafeAffinityManager)
                return LegacySetAffinity(processId, affinityMask);

            IntPtr handle = IntPtr.Zero;
            try
            {
                handle = OpenProcess(PROCESS_SET_INFORMATION | PROCESS_QUERY_INFORMATION, false, processId);
                if (handle == IntPtr.Zero) return false;

                // Delta check
                if (GetProcessAffinityMask(handle, out IntPtr currentAffinity, out _))
                {
                    if (currentAffinity == affinityMask) return true; // Zero Placebo
                }

                return SetProcessAffinityMask(handle, affinityMask);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erro ao definir afinidade para {processId}", ex);
                return false;
            }
            finally
            {
                if (handle != IntPtr.Zero) CloseHandle(handle);
            }
        }

        public bool TryPinToPerformanceCores(int processId)
        {
            if (!HardwareAwarenessManager.Instance.HasEfficiencyCores)
            {
                // Se não tem P/E cores, ignora com sucesso.
                return true; 
            }

            // Em uma arquitetura real, o HardwareAwarenessManager exportaria a máscara de P-Cores.
            // Para simplificação de fundação:
            IntPtr pCoreMask = GeneratePCoreMaskFromTopology();
            if (pCoreMask == IntPtr.Zero) return false;

            return TrySetAffinity(processId, pCoreMask);
        }

        private IntPtr GeneratePCoreMaskFromTopology()
        {
            // O HardwareAwarenessManager (GetLogicalProcessorInformationEx) já sabe quais processadores
            // são EfficiencyClass > 0.
            // Esta função construirá a bitmask usando os dados salvos nele.
            // Placeholder temporário.
            return (IntPtr)0xFFFF; 
        }

        private bool LegacySetAffinity(int processId, IntPtr affinityMask)
        {
            try
            {
                using var p = Process.GetProcessById(processId);
                p.ProcessorAffinity = affinityMask;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
