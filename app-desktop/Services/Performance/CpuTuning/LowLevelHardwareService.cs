using System;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using VoltrisOptimizer.Services;
using System.Linq;
using System.ServiceProcess;
using VoltrisOptimizer.Services.Hardware;

namespace VoltrisOptimizer.Services.Performance.CpuTuning
{
    /// <summary>
    /// MASTER-GRADE LOW LEVEL HARDWARE SERVICE
    /// INDUSTRIAL IMPLEMENTATION FOR KERNEL-LEVEL CPU CONTROL
    /// COMMUNICATIONS: DIRECT IOCTL VIA LibreHardwareMonitor.sys
    /// </summary>
    public class LowLevelHardwareService : IDisposable
    {
        private readonly ILoggingService _logger;
        private readonly SemaphoreSlim _accessMutex = new SemaphoreSlim(1, 1);
        private SafeFileHandle? _driverHandle;
        private bool _isLoaded;

        // CTL_CODE(0x22, 0x800 + ID, METHOD_BUFFERED, FILE_ANY_ACCESS)
        // Valores padrão driver de kernel / PawnIO (LibreHardwareMonitor)
        private const uint IOCTL_RDMSR = 0x9C402084;
        private const uint IOCTL_WRMSR = 0x9C402088;
        private const uint IOCTL_READ_PHYSICAL_MEMORY = 0x9C402050;
        private const uint IOCTL_WRITE_PHYSICAL_MEMORY = 0x9C402054;
        private const uint IOCTL_READ_PCI_CONFIG = 0x9C402064;

        // MSR Addresses (Architectural Intel)
        private const uint MSR_IA32_POWER_CTL = 0x1FC;
        private const uint MSR_RAPL_POWER_UNIT = 0x606;
        private const uint MSR_PKG_POWER_LIMIT = 0x610;
        private const uint MSR_PKG_POWER_INFO = 0x614;
        private const uint MSR_PL4_LIMIT = 0x601;                // NOVO: Core Peak Limit
        private const uint MSR_POWER_BALANCE = 0x63E;            // NOVO: CPU/GPU Balance (PP0/PP1)
        private const uint MSR_IA32_HWP_REQUEST = 0x774;         // NOVO: SpeedShift Control
        private const uint MSR_IA32_THERM_STATUS = 0x19C;
        private const uint MSR_IA32_PACKAGE_THERM_STATUS = 0x1B1;
        private const uint MSR_TEMPERATURE_TARGET = 0x1A2;       // NOVO: TjMax e PROCHOT Offset

        // MMIO Constants
        private const uint MCHBAR_PCI_ADDR = 0x80000048; // Bus 0, Dev 0, Func 0, Offset 0x48
        private const uint PACKAGE_POWER_LIMIT_MMIO_OFFSET = 0x59A0;

        // Units
        private double _powerUnit;
        private double _energyUnit;
        private double _timeUnit;
        private ulong _mchBarPhysAddr;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct MSR_STRUCTURE
        {
            public uint Register;
            public uint Eax;
            public uint Edx;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct PHYS_MEM_STRUCTURE
        {
            public ulong PhysicalAddress;
            public uint Size;
            public ulong Data;
        }

        private static Task? _initializationTask;
        private static readonly object _initLock = new object();

        public LowLevelHardwareService(ILoggingService logger)
        {
            _logger = logger;
            // Disparar a inicialização de forma controlada
            Task.Run(() => EnsureInitializedAsync());
        }

        private void UnblockFiles(string directory)
        {
            try
            {
                if (!Directory.Exists(directory)) return;
                var files = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    // Remover o "Mark of the Web" (Zone.Identifier) via P/Invoke delete stream
                    DeleteFile(file + ":Zone.Identifier");
                }
            }
            catch { /* Ignorar falhas silenciosamente */ }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteFile(string lpFileName);

        public async Task<bool> EnsureInitializedAsync()
        {
            if (_isLoaded) return true;

            lock (_initLock)
            {
                if (_initializationTask == null || _initializationTask.IsFaulted)
                {
                    _initializationTask = Task.Run(async () => await InitializeKernelInterfaceInternalAsync());
                }
            }

            await _initializationTask;
            return _isLoaded;
        }

        private async Task InitializeKernelInterfaceInternalAsync()
        {
            // Garantir que apenas um thread por vez tente inicializar fisicamente, 
            // mesmo que o Task.Run orquestre acima.
            if (!_accessMutex.Wait(30000)) 
            {
                _logger.LogWarning("[LowLevelHW] Mutex timeout waiting for initialization lock.");
                return;
            }

            try
            {
                if (_isLoaded) return;

                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var lhmDir = Path.Combine(baseDir, "LibreHardwareMonitor");

                _logger.LogInfo("[LowLevelHW] 🛠️ Initializing Master Kernel Interface...");

                // 0) Desbloquear arquivos para evitar avisos do Windows
                UnblockFiles(lhmDir);

                // 1) Primeira tentativa: abrir o device já existente no kernel
                _driverHandle = TryOpenDriverHandle();

                // 2) Se não conseguiu handle, dependemos que algum serviço in-process (como HardwareTelemetryService) tenha inicializado o driver.
                if (_driverHandle == null || _driverHandle.IsInvalid)
                {
                    _logger.LogInfo("[LowLevelHW] Kernel driver indisponível localmente. Aguardando a inicialização do HardwareTelemetryService...");
                    await Task.Delay(2000);
                    _driverHandle = TryOpenDriverHandle();
                    
                    if (_driverHandle != null && !_driverHandle.IsInvalid)
                    {
                         _logger.LogSuccess($"[LowLevelHW] ✅ Driver loaded successfully after delay.");
                    }
                }

                if (_driverHandle == null || _driverHandle.IsInvalid)
                {
                    _logger.LogInfo("[LowLevelHW] Kernel driver indisponível — Ring 0 optimizations desativadas.");
                    _logger.LogInfo("[LowLevelHW] 💡 Isso é normal em sistemas com Secure Boot ativo ou driver signing enforcement.");
                    _logger.LogInfo("[LowLevelHW] Otimizações de Camada 1 (powrprof.dll) continuam funcionando normalmente.");
                    return;
                }

                // IMPORTANTE: Definir como carregado ANTES de ler parâmetros, 
                // pois ReadMsr e ReadPciConfig verificam essa flag.
                _isLoaded = true;

                InitializeRaplParameters();
                DiscoverMchBar();
                
                _logger.LogSuccess("[LowLevelHW] 🚀 MASTER INTERFACE ACTIVE (MSR + MMIO Sync Ready)");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[LowLevelHW] Interface initialization failed: {ex.Message}");
            }
            finally
            {
                _accessMutex.Release();
            }
        }

        private SafeFileHandle? TryOpenDriverHandle()
        {
            // Ordem de tentativa:
            // 1. PawnIO — driver usado pelo LibreHardwareMonitor 0.9.x, assinado pela Microsoft
            // 2. driver de kernel_1_2_0 — driver legado usado pelo LibreHardwareMonitor < 0.9 e ThrottleStop
            // 3. driver de kernel — variante mais antiga
            // 4. LibreHardwareMonitor — device criado pelo executável standalone
            string[] deviceNames = new[]
            {
                @"\\.\PawnIO",
                @"\\.\driver de kernel_1_2_0",
                @"\\.\driver de kernel",
                @"\\.\LibreHardwareMonitor"};

            foreach (var deviceName in deviceNames)
            {
                try
                {
                    var handle = CreateFile(deviceName,
                        NativeFileAccess.ReadWrite,
                        FileShare.ReadWrite,
                        IntPtr.Zero,
                        FileMode.Open,
                        0,
                        IntPtr.Zero);

                    if (handle != null && !handle.IsInvalid && !handle.IsClosed)
                    {
                        _logger.LogInfo($"[LowLevelHW] ✓ Driver handle aberto via: {deviceName}");
                        return handle;
                    }
                }
                catch { }
            }

            return null;
        }




        private void InitializeRaplParameters()
        {
            if (ReadMsr(MSR_RAPL_POWER_UNIT, out ulong value))
            {
                _powerUnit = 1.0 / Math.Pow(2, (byte)(value & 0xF));
                _energyUnit = 1.0 / Math.Pow(2, (byte)((value >> 8) & 0x1F));
                _timeUnit = 1.0 / Math.Pow(2, (byte)((value >> 16) & 0xF));
            }
        }

        private void DiscoverMchBar()
        {
            // Read MCHBAR from PCI config to enable MMIO sync
            // Typically requires Bus 0, Dev 0, Func 0
            if (ReadPciConfig(MCHBAR_PCI_ADDR, out uint mchBarValue))
            {
                _mchBarPhysAddr = (ulong)(mchBarValue & 0xFFFFFFF0); // Mask low bits
                if (_mchBarPhysAddr > 0)
                    _logger.LogDebug($"[LowLevelHW] MCHBAR discovered at 0x{_mchBarPhysAddr:X}");
            }
        }

        public bool IsAvailable => _isLoaded;

        public bool ReadMsr(uint register, out ulong value)
        {
            value = 0;
            if (_driverHandle == null || _driverHandle.IsInvalid) return false;
            
            // Regra de segurança: Não ler antes de carregar, exceto para autodetecção de unidades
            if (!_isLoaded && register != MSR_RAPL_POWER_UNIT) return false;

            var input = new MSR_STRUCTURE { Register = register };
            int sizeIn = Marshal.SizeOf(input);
            IntPtr ptrIn = Marshal.AllocHGlobal(sizeIn);
            IntPtr ptrOut = Marshal.AllocHGlobal(8);

            try
            {
                Marshal.StructureToPtr(input, ptrIn, false);
                uint bytesReturned;
                if (DeviceIoControl(_driverHandle, IOCTL_RDMSR, ptrIn, sizeIn, ptrOut, 8, out bytesReturned, IntPtr.Zero))
                {
                    value = (ulong)Marshal.ReadInt64(ptrOut);
                    return true;
                }
            }
            catch (Exception ex) { _logger.LogError($"[LowLevelHW] RDMSR 0x{register:X} failed: {ex.Message}"); }
            finally
            {
                Marshal.FreeHGlobal(ptrIn);
                Marshal.FreeHGlobal(ptrOut);
            }
            return false;
        }

        public bool WriteMsr(uint register, ulong value)
        {
            if (_driverHandle == null || _driverHandle.IsInvalid) return false;

            var input = new MSR_STRUCTURE { 
                Register = register,
                Eax = (uint)(value & 0xFFFFFFFF),
                Edx = (uint)(value >> 32)
            };

            int sizeIn = Marshal.SizeOf(input);
            IntPtr ptrIn = Marshal.AllocHGlobal(sizeIn);

            try
            {
                Marshal.StructureToPtr(input, ptrIn, false);
                uint bytesReturned;
                return DeviceIoControl(_driverHandle, IOCTL_WRMSR, ptrIn, sizeIn, IntPtr.Zero, 0, out bytesReturned, IntPtr.Zero);
            }
            catch (Exception ex) { _logger.LogError($"[LowLevelHW] WRMSR 0x{register:X} failed: {ex.Message}"); return false; }
            finally { Marshal.FreeHGlobal(ptrIn); }
        }

        public struct HardwareAuditReport
        {
            public double PL1_MSR;
            public double PL2_MSR;
            public double PL1_MMIO;
            public double PL2_MMIO;
            public double PL4;
            public int ProchotOffset;
            public bool MsrLocked;
            public bool BdProchotActive;
            public int PP0_Balance;
            public int PP1_Balance;
        }

        public HardwareAuditReport GetFullHardwareAudit()
        {
            var report = new HardwareAuditReport();
            if (!_accessMutex.Wait(2000)) return report;
            try
            {
                // PKG Power Limit (MSR 0x610)
                if (ReadMsr(MSR_PKG_POWER_LIMIT, out ulong msrVal))
                {
                    report.PL1_MSR = (msrVal & 0x7FFF) * _powerUnit;
                    report.PL2_MSR = ((msrVal >> 32) & 0x7FFF) * _powerUnit;
                    report.MsrLocked = (msrVal & (1UL << 63)) != 0;
                }

                // PKG Power Limit (MMIO)
                if (_mchBarPhysAddr > 0)
                {
                    ReadPhysicalMemory(_mchBarPhysAddr + PACKAGE_POWER_LIMIT_MMIO_OFFSET, 8, out ulong mmioVal);
                    report.PL1_MMIO = (mmioVal & 0x7FFF) * _powerUnit;
                    report.PL2_MMIO = ((mmioVal >> 32) & 0x7FFF) * _powerUnit;
                }

                // PL4 (MSR 0x601)
                if (ReadMsr(MSR_PL4_LIMIT, out ulong pl4Val))
                    report.PL4 = (pl4Val & 0x1FFF) * _powerUnit;

                // Temperature Target (MSR 0x1A2)
                if (ReadMsr(MSR_TEMPERATURE_TARGET, out ulong tempVal))
                {
                    report.ProchotOffset = (int)((tempVal >> 24) & 0x3F);
                }

                // Power Control (MSR 0x1FC)
                if (ReadMsr(MSR_IA32_POWER_CTL, out ulong ctlVal))
                {
                    report.BdProchotActive = (ctlVal & 0x1UL) != 0;
                }

                // Power Balance (MSR 0x63E)
                if (ReadMsr(MSR_POWER_BALANCE, out ulong balVal))
                {
                    report.PP0_Balance = (int)(balVal & 0x1F);
                    report.PP1_Balance = (int)((balVal >> 8) & 0x1F);
                }

                return report;
            }
            finally { _accessMutex.Release(); }
        }

        public void LogHardwareAudit(string context = "SINCRO")
        {
            var r = GetFullHardwareAudit();
            _logger.LogInfo($"[LowLevelHW][{context}] 🔎 AUDITORIA INDUSTRIAL DE HARDWARE (NO-PLACEBO)");
            _logger.LogInfo($"[LowLevelHW] ├─ PKG MSR: PL1={r.PL1_MSR:F1}W | PL2={r.PL2_MSR:F1}W | Lock={r.MsrLocked}");
            _logger.LogInfo($"[LowLevelHW] ├─ PKG MMIO: PL1={r.PL1_MMIO:F1}W | PL2={r.PL2_MMIO:F1}W");
            _logger.LogInfo($"[LowLevelHW] ├─ PEAK PL4: {r.PL4:F1}W");
            _logger.LogInfo($"[LowLevelHW] ├─ THERMAL: Offset={r.ProchotOffset}°C | BD_PROCHOT={r.BdProchotActive}");
            _logger.LogInfo($"[LowLevelHW] └─ BALANCE: CPU={r.PP0_Balance} | GPU={r.PP1_Balance}");
        }

        public (int pl1, int pl2, double tau, bool locked, ulong mmioVal) GetDetailedPowerLimits()
        {
            var r = GetFullHardwareAudit();
            return ((int)r.PL1_MSR, (int)r.PL2_MSR, 0, r.MsrLocked, 0); // Mantendo compatibilidade legada
        }

        public bool SetPowerLimitsManaged(double pl1, double pl2, double tauSeconds, bool requestLock = false)
        {
            if (!_accessMutex.Wait(2000)) return false;
            try
            {
                if (!ReadMsr(MSR_PKG_POWER_LIMIT, out ulong currentVal)) 
                {
                    _logger.LogError("[LowLevelHW] ❌ Falha crítica ao ler MSR 0x610 (Power Limits).");
                    return false;
                }

                // Preserve reserved bits
                ulong newVal = currentVal;
                
                // Build Raw values
                byte tauRaw = SecondsToTauRaw(tauSeconds);
                ulong pl1Raw = (ulong)(pl1 / _powerUnit) & 0x7FFF;
                ulong pl2Raw = (ulong)(pl2 / _powerUnit) & 0x7FFF;

                // PL1 Config
                newVal &= ~0x7FFFUL; // Clear PL1
                newVal |= pl1Raw;
                newVal |= (1UL << 15); // Enable
                newVal |= (1UL << 16); // Clamp

                // Tau Config
                newVal &= ~(0x7FUL << 17);
                newVal |= ((ulong)tauRaw << 17);

                // PL2 Config
                newVal &= ~(0x7FFFUL << 32); 
                newVal |= (pl2Raw << 32);
                newVal |= (1UL << 47); // Enable
                newVal |= (1UL << 48); // Clamp

                // Lock Bit (63) - Se solicitado e não estava travado
                if (requestLock) newVal |= (1UL << 63);

                // 1) ESCRITA MSR
                _logger.LogInfo($"[LowLevelHW] Tentando aplicar PL1={pl1}W, PL2={pl2}W no MSR (Lock={requestLock})");
                bool msrSuccess = WriteMsr(MSR_PKG_POWER_LIMIT, newVal);

                // 2) MMIO SYNC (Crucial para contornar BIOS e System Software)
                bool mmioWritten = false;
                if (_mchBarPhysAddr > 0)
                {
                    _logger.LogInfo("[LowLevelHW] Sincronizando MMIO para evitar override do BIOS...");
                    mmioWritten = WritePhysicalMemory(_mchBarPhysAddr + PACKAGE_POWER_LIMIT_MMIO_OFFSET, newVal);
                }

                // 3) VERIFICAÇÃO REAL (ANTI-PLACEBO)
                ReadMsr(MSR_PKG_POWER_LIMIT, out ulong verifyMsr);
                ulong verifyMmio = 0;
                if (_mchBarPhysAddr > 0) ReadPhysicalMemory(_mchBarPhysAddr + PACKAGE_POWER_LIMIT_MMIO_OFFSET, 8, out verifyMmio);

                int actualPl1 = (int)((verifyMsr & 0x7FFF) * _powerUnit);
                bool isLocked = (verifyMsr & (1UL << 63)) != 0;

                if (actualPl1 == (int)pl1)
                {
                    _logger.Log(LogLevel.AI_DECISION, LogCategory.Gamer, $"[Power] ✅ NO-PLACEBO: PL1 configurado em {actualPl1}W com sucesso.");
                    if (isLocked) _logger.LogInfo("[Power] 🔒 Power Limits travados no Hardware (Locked=True).");
                }
                else
                {
                    _logger.LogWarning($"[Power] ⚠️ Hardware recusou PL1 de {pl1}W. Valor atual: {actualPl1}W. (BIOS Lock detectado)");
                }

                return msrSuccess;
            }
            finally { _accessMutex.Release(); }
        }

        public (bool thermal, bool power, bool current) GetSiliconThrottlingFlags()
        {
            if (ReadMsr(MSR_IA32_THERM_STATUS, out ulong val))
            {
                bool thermal = (val & 1UL) != 0; // Bit 0
                bool power = (val & (1UL << 10)) != 0; // Bit 10 (Package Power Limit)
                bool current = (val & (1UL << 3)) != 0; // Bit 3 (Critical Temperature/Current)
                return (thermal, power, current);
            }
            return (false, false, false);
        }

        public bool GetThermalThrottlingStatus()
        {
            if (ReadMsr(MSR_IA32_THERM_STATUS, out ulong val))
            {
                return (val & 0x1UL) != 0; // Bit 0 indicates ACTIVE throttling
            }
            return false;
        }

        public bool SetBdProchot(bool enabled)
        {
            if (!_accessMutex.Wait(1000)) return false;
            try
            {
                if (!ReadMsr(MSR_IA32_POWER_CTL, out ulong val)) return false;
                
                if (enabled) val |= 0x1UL;
                else val &= ~0x1UL;
                
                bool result = WriteMsr(MSR_IA32_POWER_CTL, val);
                
                // Verificação Real
                ReadMsr(MSR_IA32_POWER_CTL, out ulong verify);
                bool currentlyEnabled = (verify & 0x1UL) != 0;
                
                if (currentlyEnabled == enabled)
                {
                    _logger.Log(LogLevel.AI_DECISION, LogCategory.Gamer, enabled ? "[Power] 🛡️ BD PROCHOT Ativado." : "[Power] 🚀 BD PROCHOT Desativado com sucesso (Sem placebo).");
                }
                else
                {
                    _logger.LogWarning("[Power] ⚠️ Falha ao alterar BD PROCHOT. O Hardware ou BIOS está forçando o valor.");
                }

                return result;
            }
            finally { _accessMutex.Release(); }
        }

        public bool SetProchotOffset(int offset)
        {
            if (offset < 0 || offset > 63) return false;
            if (!_accessMutex.Wait(1000)) return false;
            try
            {
                if (!ReadMsr(MSR_TEMPERATURE_TARGET, out ulong val)) return false;
                
                val &= ~(0x3FUL << 24); // Limpar bits 24-29
                val |= ((ulong)offset << 24);
                
                bool result = WriteMsr(MSR_TEMPERATURE_TARGET, val);
                
                ReadMsr(MSR_TEMPERATURE_TARGET, out ulong verify);
                int currentOffset = (int)((verify >> 24) & 0x3F);
                
                if (currentOffset == offset)
                    _logger.LogInfo($"[Power] ✅ PROCHOT Offset: {offset}°C (CONFIRMADO)");
                else
                    _logger.LogWarning($"[Power] ⚠️ PROCHOT Offset bloqueado em {currentOffset}°C pelo BIOS.");
                
                return result;
            }
            finally { _accessMutex.Release(); }
        }

        public bool SetPl4Limit(double watts)
        {
            if (!_accessMutex.Wait(1000)) return false;
            try
            {
                uint raw = (uint)(watts / _powerUnit) & 0x1FFF;
                ulong val = raw | (1UL << 15); // Enable bit
                
                bool result = WriteMsr(MSR_PL4_LIMIT, val);
                
                ReadMsr(MSR_PL4_LIMIT, out ulong verify);
                double actual = (verify & 0x1FFF) * _powerUnit;
                
                if (Math.Abs(actual - watts) < 1.0)
                    _logger.LogInfo($"[Power] ✅ Peak Power (PL4): {actual}W (NO-PLACEBO)");
                
                return result;
            }
            finally { _accessMutex.Release(); }
        }

        public bool SetPowerBalance(int cpuPriority, int gpuPriority)
        {
            if (!_accessMutex.Wait(1000)) return false;
            try
            {
                if (!ReadMsr(MSR_POWER_BALANCE, out ulong val)) return false;
                
                val &= ~0x1F1FUL; // Limpar campos PP0 e PP1
                val |= (ulong)(cpuPriority & 0x1F);
                val |= (ulong)((gpuPriority & 0x1F) << 8);
                
                bool result = WriteMsr(MSR_POWER_BALANCE, val);
                if (result) _logger.LogInfo($"[Power] ✅ Power Balance Simétrico: CPU({cpuPriority}) | GPU({gpuPriority})");
                return result;
            }
            finally { _accessMutex.Release(); }
        }

        public bool SetSpeedShift(int min, int max, int epp)
        {
            if (!_accessMutex.Wait(1000)) return false;
            try
            {
                ulong val = 0;
                val |= (ulong)(min & 0xFF);
                val |= (ulong)((max & 0xFF) << 8);
                val |= (ulong)((255) << 16); // Desired -> Max for Performance
                val |= (ulong)((epp & 0xFF) << 24);
                
                bool result = WriteMsr(MSR_IA32_HWP_REQUEST, val);
                if (result) _logger.LogInfo($"[Power] ✅ SpeedShift HWP: Min={min}|Max={max}|EPP={epp}");
                return result;
            }
            finally { _accessMutex.Release(); }
        }

        #region Helpers
        private bool ReadPciConfig(uint address, out uint value)
        {
            value = 0;
            if (!_isLoaded || _driverHandle == null) return false;
            IntPtr ptrOut = Marshal.AllocHGlobal(4);
            try
            {
                uint bytesReturned;
                bool success = DeviceIoControl(_driverHandle, IOCTL_READ_PCI_CONFIG, (IntPtr)address, 4, ptrOut, 4, out bytesReturned, IntPtr.Zero);
                if (success) value = (uint)Marshal.ReadInt32(ptrOut);
                return success;
            }
            finally { Marshal.FreeHGlobal(ptrOut); }
        }

        public bool ReadPhysicalMemory(ulong address, uint size, out ulong value)
        {
            value = 0;
            if (_driverHandle == null || _driverHandle.IsInvalid) return false;

            // Padrão driver de kernel / PawnIO para ReadPhysicalMemory (0x9C402050):
            // O driver exige um input de 12 bytes (Address + Size)
            byte[] inBuf = new byte[12];
            Buffer.BlockCopy(BitConverter.GetBytes(address), 0, inBuf, 0, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(size), 0, inBuf, 8, 4);

            byte[] outBuf = new byte[size];

            try
            {
                uint bytesReturned;
                // Usando o overload de byte[] para facilitar, mas garantindo os tamanhos
                if (DeviceIoControl(_driverHandle, IOCTL_READ_PHYSICAL_MEMORY, inBuf, (uint)inBuf.Length, outBuf, (uint)outBuf.Length, out bytesReturned, IntPtr.Zero))
                {
                    if (size == 4) value = BitConverter.ToUInt32(outBuf, 0);
                    else if (size == 8) value = BitConverter.ToUInt64(outBuf, 0);
                    return true;
                }
            }
            catch (Exception ex) { _logger.LogError($"[LowLevelHW] ReadPhysicalMemory failed: {ex.Message}"); }
            return false;
        }

        private bool WritePhysicalMemory(ulong address, ulong data)
        {
            if (_driverHandle == null || _driverHandle.IsInvalid) return false;
            
            // Padrão driver de kernel / PawnIO para WritePhysicalMemory (0x9C402054):
            // Input Buffer: PHYS_MEM_STRUCTURE (16 bytes ou mais conforme o driver)
            var input = new PHYS_MEM_STRUCTURE { PhysicalAddress = address, Size = 8, Data = data };
            int sizeIn = Marshal.SizeOf(input);
            IntPtr ptrIn = Marshal.AllocHGlobal(sizeIn);
            
            try
            {
                Marshal.StructureToPtr(input, ptrIn, false);
                uint bytesReturned;
                return DeviceIoControl(_driverHandle, IOCTL_WRITE_PHYSICAL_MEMORY, ptrIn, sizeIn, IntPtr.Zero, 0, out bytesReturned, IntPtr.Zero);
            }
            catch (Exception ex) { _logger.LogError($"[LowLevelHW] WritePhysicalMemory @ 0x{address:X} failed: {ex.Message}"); return false; }
            finally { Marshal.FreeHGlobal(ptrIn); }
        }

        public byte SecondsToTauRaw(double seconds)
        {
            for (byte y = 0; y < 32; y++)
            {
                for (byte x = 0; x < 4; x++)
                {
                    double test = (1.0 + x / 4.0) * Math.Pow(2, y) * _timeUnit;
                    if (test >= seconds) return (byte)((x << 5) | y);
                }
            }
            return 0x6E; 
        }

        public double TauRawToSeconds(byte raw)
        {
            uint x = (uint)((raw >> 5) & 0x3);
            uint y = (uint)(raw & 0x1F);
            return (1.0 + x / 4.0) * Math.Pow(2, y) * _timeUnit;
        }
        #endregion

        #region Native P/Invoke
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern SafeFileHandle CreateFile(string lpFileName, NativeFileAccess dwDesiredAccess, FileShare dwShareMode, IntPtr lpSecurityAttributes, FileMode dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(SafeHandle hDevice, uint dwIoControlCode, [In] byte[] lpInBuffer, uint nInBufferSize, [Out] byte[] lpOutBuffer, uint nOutBufferSize, out uint lpBytesReturned, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(SafeHandle hDevice, uint dwIoControlCode, IntPtr lpInBuffer, int nInBufferSize, IntPtr lpOutBuffer, int nOutBufferSize, out uint lpBytesReturned, IntPtr lpOverlapped);

        [Flags] public enum NativeFileAccess : uint { ReadDevice = 0x80000000, WriteDevice = 0x40000000, ReadWrite = ReadDevice | WriteDevice }
        #endregion

        public void Dispose()
        {
            try
            {
                // Fechar a instância do Computer se estiver aberta

            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[LowLevelHW] Error closing Computer: {ex.Message}");
            }
            
            _driverHandle?.Dispose();
            _accessMutex.Dispose();
        }
    }
}
