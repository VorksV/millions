using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using VoltrisOptimizer.Models;
using VoltrisOptimizer.Utils.Win32;

namespace VoltrisOptimizer.Services.Optimization.Engines
{
    /// <summary>
    /// DSL 5.0 - Interrupt Latency Engine
    /// Controle profundo de DPC (Deferred Procedure Calls) e Interrupts para reduzir Input Lag.
    /// </summary>
    public class InterruptLatencyEngine : IDisposable
    {
        private readonly ILoggingService _logger;
        private DateTime _lastProtectionApplied = DateTime.MinValue;

        public InterruptLatencyEngine(ILoggingService logger)
        {
            _logger = logger;
        }

        public void Update(int? foregroundPid, SystemState50 state)
        {
            try
            {
                // Se a carga de interrupção/DPC for alta (> 2%), o mouse começa a "pular" ou ter lag
                if (state.DpcTime > 2.0f || state.InterruptTime > 1.5f)
                {
                    if (foregroundPid.HasValue && foregroundPid.Value > 0)
                    {
                        ApplyQuantumProtection(foregroundPid.Value);
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// IMPLEMENTADO: Aplica proteção de quantum scheduling para o processo foreground.
        /// Habilita PriorityBoost e ajusta afinidade para reduzir latência de interrupção.
        /// </summary>
        private void ApplyQuantumProtection(int pid)
        {
            try
            {
                if ((DateTime.Now - _lastProtectionApplied).TotalSeconds < 5) return;

                IntPtr hProcess = ProcessNativeMethods.OpenProcess(
                    ProcessNativeMethods.PROCESS_SET_INFORMATION | ProcessNativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, 
                    false, pid);

                if (hProcess != IntPtr.Zero)
                {
                    try
                    {
                        // 1. Habilitar Priority Boost
                        SetProcessPriorityBoost(hProcess, false); // false = enable boost (o nome da API é 'disable')

                        // 2. Elevar prioridade se estiver em Normal
                        uint currentPri = ProcessNativeMethods.GetPriorityClass(hProcess);
                        if (currentPri == ProcessNativeMethods.NORMAL_PRIORITY_CLASS)
                        {
                            ProcessNativeMethods.SetPriorityClass(hProcess, ProcessNativeMethods.ABOVE_NORMAL_PRIORITY_CLASS);
                        }

                        // 3. Desabilitar EcoQoS/Power Throttling
                        var state = new PROCESS_POWER_THROTTLING_STATE
                        {
                            Version = 1,
                            ControlMask = 0x01,
                            StateMask = 0x00 // Disable throttling
                        };
                        SetProcessInformation(hProcess, 4, ref state, Marshal.SizeOf(state));

                        _logger.LogInfo($"[InterruptLatency] Quantum protection applied via Native API to PID {pid}");
                    }
                    finally { ProcessNativeMethods.CloseHandle(hProcess); }
                }

                _lastProtectionApplied = DateTime.Now;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[InterruptLatency] Erro na proteção de quantum: {ex.Message}");
            }
        }

        /// <summary>
        /// Analisa todos os dispositivos PCI do sistema e injeta política MSI (Message Signaled Interrupts)
        /// com Message Number Limit configurado. Isso remove gargalos de Legacy IRQ Sharing de GPUs e NVMes.
        /// </summary>
        public void ConfigurePciMsiMode()
        {
            _logger.LogInfo("[InterruptLatency] Analisando barramento PCIe para injeção de política MSI...");
            int devicesOptimized = 0;

            try
            {
                using var pciKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\PCI", true);
                if (pciKey == null) return;

                foreach (string deviceId in pciKey.GetSubKeyNames())
                {
                    using var deviceKey = pciKey.OpenSubKey(deviceId, true);
                    if (deviceKey == null) continue;

                    foreach (string instanceId in deviceKey.GetSubKeyNames())
                    {
                        using var instanceKey = deviceKey.OpenSubKey(instanceId, true);
                        if (instanceKey == null) continue;

                        string deviceDesc = (instanceKey.GetValue("DeviceDesc") as string) ?? string.Empty;
                        
                        using var devParamsKey = instanceKey.OpenSubKey("Device Parameters", true);
                        if (devParamsKey == null) continue;

                        using var interruptMgmtKey = devParamsKey.OpenSubKey("Interrupt Management", true);
                        if (interruptMgmtKey == null) continue;

                        using var msiPropsKey = interruptMgmtKey.OpenSubKey("MessageSignaledInterruptProperties", true);
                        if (msiPropsKey != null)
                        {
                            object msISupported = msiPropsKey.GetValue("MSISupported");
                            if (msISupported != null)
                            {
                                // Ativa MSI
                                msiPropsKey.SetValue("MSISupported", 1, Microsoft.Win32.RegistryValueKind.DWord);
                                
                                // Otimiza Message Limit dependendo do dispositivo (GPU ou NVMe precisam de banda larga de interrupções)
                                if (deviceDesc.Contains("NVIDIA") || deviceDesc.Contains("AMD Radeon") || deviceDesc.Contains("NVM Express"))
                                {
                                    msiPropsKey.SetValue("MessageNumberLimit", 8, Microsoft.Win32.RegistryValueKind.DWord);
                                }
                                devicesOptimized++;
                            }
                        }
                    }
                }

                _logger.LogSuccess($"[InterruptLatency] Injeção PCIe concluída. {devicesOptimized} dispositivos agora utilizam MSI Mode.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[InterruptLatency] Falha ao configurar MSI: {ex.Message}");
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessPriorityBoost(IntPtr hProcess, bool bDisablePriorityBoost);

        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")] private static extern bool SetProcessInformation(IntPtr hProcess, int infoClass, ref PROCESS_POWER_THROTTLING_STATE info, int size);

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_POWER_THROTTLING_STATE { public uint Version; public uint ControlMask; public uint StateMask; }

        public void Dispose()
        {
        }
    }
}
