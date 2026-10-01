using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Optimization.Unification
{
    public class EcoQoSController
    {
        private readonly ILoggingService _logger;

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_POWER_THROTTLING_STATE { public uint Version; public uint ControlMask; public uint StateMask; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORY_PRIORITY_INFORMATION { public uint MemoryPriority; }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "SetProcessInformation")]
        private static extern bool SetProcessInformationPower(IntPtr hProcess, int processInformationClass, ref PROCESS_POWER_THROTTLING_STATE processInformation, int processInformationSize);

        [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "SetProcessInformation")]
        private static extern bool SetProcessInformationMem(IntPtr hProcess, int processInformationClass, ref MEMORY_PRIORITY_INFORMATION processInformation, int processInformationSize);

        public EcoQoSController(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        private void HandleError(int pid, string operation)
        {
            int err = Marshal.GetLastWin32Error();
            string reason = err switch
            {
                0 => "Operação Concluída com Sucesso (Status Retornado)", // Geralmente não deveria chamar erro pra 0, falsetrigger.
                5 => "AccessDenied (Sistema/Protegido)",
                6 => "InvalidHandle",
                87 => "InvalidParameter",
                _ => $"Win32Error_{err}"
            };
            if (err != 0)
                _logger.LogWarning($"[EcoQoSController] {operation} falhou no PID {pid}. Erro: {reason}");
        }

        public bool ApplyEcoQoS(int pid)
        {
            int maxRetries = 2;
            int delayMs = 50;

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                var h = OpenProcess(0x0200 | 0x0400, false, pid);
                if (h == IntPtr.Zero)
                {
                    HandleError(pid, "OpenProcess");
                    return false; // AccessDenied costuma ser permanente (processo de sistema)
                }

                try
                {
                    // 1. Aplica EcoQoS Power Throttling
                    var state = new PROCESS_POWER_THROTTLING_STATE { Version = 1, ControlMask = 0x1, StateMask = 0x1u };
                    bool successPower = SetProcessInformationPower(h, 4, ref state, Marshal.SizeOf(state));
                    
                    // 2. Aplica Memory Priority (infoClass = 1 = ProcessMemoryPriority)
                    var mem = new MEMORY_PRIORITY_INFORMATION { MemoryPriority = 1 }; // Very Low
                    bool successMem = SetProcessInformationMem(h, 1, ref mem, Marshal.SizeOf(mem));

                    // ROLLBACK ATÔMICO
                    if (!successPower || !successMem)
                    {
                        if (attempt == 0) _logger.LogWarning($"[EcoQoSController] Falha parcial no PID {pid}. Iniciando Rollback atômico...");
                        
                        if (successPower)
                        {
                            state.StateMask = 0x0u; // Reverte Power
                            SetProcessInformationPower(h, 4, ref state, Marshal.SizeOf(state));
                        }
                        if (successMem)
                        {
                            mem.MemoryPriority = 5; // Reverte Memória
                            SetProcessInformationMem(h, 1, ref mem, Marshal.SizeOf(mem));
                        }
                        
                        HandleError(pid, "SetProcessInformation");

                        if (attempt < maxRetries)
                        {
                            Thread.Sleep(delayMs);
                            delayMs *= 2; // Exponential backoff
                            continue;
                        }
                        return false;
                    }

                    _logger.LogInfo($"[EcoQoSController] EcoQoS ATIVADO no PID {pid}");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[EcoQoSController] Erro catastrofico PID={pid}: {ex.Message}");
                    return false;
                }
                finally
                {
                    CloseHandle(h);
                }
            }
            return false;
        }

        public bool RemoveEcoQoS(int pid)
        {
            int maxRetries = 2;
            int delayMs = 50;

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                var h = OpenProcess(0x0200 | 0x0400, false, pid);
                if (h == IntPtr.Zero)
                {
                    HandleError(pid, "OpenProcess (Remove)");
                    return false;
                }

                try
                {
                    var state = new PROCESS_POWER_THROTTLING_STATE { Version = 1, ControlMask = 0x1, StateMask = 0x0u };
                    bool successPower = SetProcessInformationPower(h, 4, ref state, Marshal.SizeOf(state));
                    
                    var mem = new MEMORY_PRIORITY_INFORMATION { MemoryPriority = 5 }; // Normal
                    bool successMem = SetProcessInformationMem(h, 1, ref mem, Marshal.SizeOf(mem));

                    if (!successPower || !successMem)
                    {
                        HandleError(pid, "SetProcessInformation (Remove)");
                        if (attempt < maxRetries)
                        {
                            Thread.Sleep(delayMs);
                            delayMs *= 2;
                            continue;
                        }
                        return false;
                    }

                    _logger.LogInfo($"[EcoQoSController] EcoQoS REMOVIDO do PID {pid}");
                    return true;
                }
                catch
                {
                    return false;
                }
                finally
                {
                    CloseHandle(h);
                }
            }
            return false;
        }
    }
}
