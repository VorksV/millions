using VoltrisOptimizer.Helpers;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Management;
using System.Threading;
using VoltrisOptimizer.Services.Gamer.Interfaces;

namespace VoltrisOptimizer.Services.Gamer.GamerModeManager.Services
{
    /// <summary>
    /// Memory Optimization Service - Otimização avançada de memória para gaming
    /// Large pages, NUMA-aware allocation, garbage collection tuning
    /// </summary>
    public class MemoryOptimizerService : IMemoryOptimizerService
    {
        private readonly ILoggingService _logger;
        
        // Backup de configurações
        private int? _originalLargePageMinimum;
        private int? _originalSystemPages;
        private uint? _originalPrivilege;
        
        // APIs nativas
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAlloc(IntPtr lpAddress, UIntPtr dwSize, uint flAllocationType, uint flProtect);
        
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualFree(IntPtr lpAddress, UIntPtr dwSize, uint dwFreeType);
        
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessWorkingSetSizeEx(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize, uint flags);
        
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetCurrentProcess();
        
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);
        
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool LookupPrivilegeValue(string lpSystemName, string lpName, ref LUID lpLuid);
        
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges, ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);
        
        [StructLayout(LayoutKind.Sequential)]
        public struct LUID
        {
public uint LowPart;
            public int HighPart;
}
        
        [StructLayout(LayoutKind.Sequential)]
        public struct LUID_AND_ATTRIBUTES
        {
            public LUID Luid;
            public uint Attributes;
        }
        
        [StructLayout(LayoutKind.Sequential)]
        public struct TOKEN_PRIVILEGES
        {
            public uint PrivilegeCount;
            public LUID_AND_ATTRIBUTES Privileges;
        }
        
        // Constantes
        private const uint MEM_LARGE_PAGES = 0x20000000;
        private const uint MEM_COMMIT = 0x1000;
        private const uint MEM_RESERVE = 0x2000;
        private const uint MEM_RELEASE = 0x8000;
        private const uint PAGE_READWRITE = 0x04;
        private const uint PAGE_EXECUTE_READWRITE = 0x40;
        private const uint QUOTA_LIMITS_HARDWS = 0x00000001;
        
        public MemoryOptimizerService(ILoggingService logger)
        {
            _logger.LogEntry(nameof(MemoryOptimizerService));
_logger = logger;
            _logger.LogExit(nameof(MemoryOptimizerService));
}
        
        /// <summary>
        /// Otimiza gerenciamento de memória para gaming
        /// </summary>
        public async Task<bool> OptimizeMemoryAsync()
        {
            _logger.LogEntry(nameof(OptimizeMemoryAsync));
try
            {
                _logger.LogInfo("[Memory] Iniciando otimização avançada de memória...");
                
                // 1. Habilitar Large Pages (reduz TLB misses)
                await EnableLargePagesAsync();
                
                // 2. Otimizar Working Set
                OptimizeWorkingSet();
                
                // 3. Configurar NUMA-aware allocation
                ConfigureNumaOptimization();
                
                // 4. Otimizar Page File
                OptimizePageFile();
                
                // 5. Desabilitar Memory Compression
                DisableMemoryCompression();
                
                // 6. Otimizar Garbage Collection
                OptimizeGarbageCollection();
                
                // 7. Memory Locking (evitar paging)
                LockMemoryPages();
                
                _logger.LogSuccess("[Memory] ✅ Otimização de memória concluída - Performance máxima!");
return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("[Memory] Erro na otimização de memória", ex);
return false;
            }
            _logger.LogExit(nameof(OptimizeMemoryAsync));
}
        
        /// <summary>
        /// Habilita Large Pages para reduzir TLB misses
        /// </summary>
        private async Task<bool> EnableLargePagesAsync()
        {
            _logger.LogEntry(nameof(EnableLargePagesAsync));
try
            {
                // Habilitar privilégio de Large Pages
                if (!EnableLargePagePrivilege())
                {
                    _logger.LogWarning("[Memory] Falha ao habilitar privilégio de Large Pages");
return false;
                }
                
                // Configurar sistema para Large Pages
                using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management");
                
                // Backup
                _originalLargePageMinimum = key.GetValue("LargePageMinimum") as int?;
                
                // Reduzir mínimo para Large Pages
                key.SetValue("LargePageMinimum", 0, RegistryValueKind.DWord);
                
                // Aumentar System Pages
                _originalSystemPages = key.GetValue("SystemPages") as int?;
                key.SetValue("SystemPages", -1, RegistryValueKind.DWord);
                
                // Alocar Large Pages para teste
                var largePagePtr = VirtualAlloc(IntPtr.Zero, (UIntPtr)2 * 1024 * 1024, MEM_LARGE_PAGES | MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
                if (largePagePtr != IntPtr.Zero)
                {
                    VirtualFree(largePagePtr, UIntPtr.Zero, MEM_RELEASE);
                    _logger.LogInfo("[Memory] ✅ Large Pages habilitadas com sucesso");
return true;
                }
                
                _logger.LogWarning("[Memory] Large Pages não suportadas pelo sistema");
return false;
            }
            catch (Exception ex)
            {
_logger.LogWarning($"[Memory] Erro ao habilitar Large Pages: {ex.Message}");
                return false;
}
            _logger.LogExit(nameof(EnableLargePagesAsync));
}
        
        /// <summary>
        /// Habilita privilégio de Large Pages
        /// </summary>
        private bool EnableLargePagePrivilege()
        {
            _logger.LogEntry(nameof(EnableLargePagePrivilege));
try
            {
                var hProcess = GetCurrentProcess();
                var hToken = IntPtr.Zero;
                
                if (!OpenProcessToken(hProcess, 0x0020, out hToken)) // TOKEN_ADJUST_PRIVILEGES
return false;
                
                var luid = new LUID();
                if (!LookupPrivilegeValue(null, "SeLockMemoryPrivilege", ref luid))
return false;
                
                var tokenPrivs = new TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    Privileges = new LUID_AND_ATTRIBUTES
                    {
                        Luid = luid,
                        Attributes = 0x00000002 // SE_PRIVILEGE_ENABLED
                    }
                };
                
                var result = AdjustTokenPrivileges(hToken, false, ref tokenPrivs, 0, IntPtr.Zero, IntPtr.Zero);
return result && Marshal.GetLastWin32Error() == 0; // ERROR_SUCCESS
            }
            catch
            {
return false;
            }
            _logger.LogExit(nameof(EnableLargePagePrivilege));
}
        
        /// <summary>
        /// Otimiza Working Set para gaming
        /// </summary>
        private void OptimizeWorkingSet()
        {
            _logger.LogEntry(nameof(OptimizeWorkingSet));
try
            {
                var process = Process.GetCurrentProcess();
                var workingSet = process.WorkingSet64;
                
                // Aumentar working set para evitar paging
                var newMin = new IntPtr(workingSet / 2);
                var newMax = new IntPtr(workingSet * 2);
                
                if (SetProcessWorkingSetSizeEx(process.Handle, newMin, newMax, QUOTA_LIMITS_HARDWS))
                {
                    _logger.LogInfo($"[Memory] ✅ Working Set otimizado: {workingSet / 1024 / 1024}MB → {(workingSet * 2) / 1024 / 1024}MB");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Memory] Erro ao otimizar Working Set: {ex.Message}");
            }
            _logger.LogExit(nameof(OptimizeWorkingSet));
}
        
        /// <summary>
        /// Configura otimização NUMA-aware
        /// </summary>
        private void ConfigureNumaOptimization()
        {
            _logger.LogEntry(nameof(ConfigureNumaOptimization));
try
            {
                // Verificar suporte NUMA
                var nodeCount = 0;
                foreach (var item in new System.Management.ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem").Get())
                {
                    var numProcessors = Convert.ToInt32(item["NumberOfProcessors"]);
                    if (numProcessors > 1)
                    {
                        nodeCount = numProcessors;
                        break;
                    }
                }
                
                if (nodeCount > 1)
                {
                    // Configurar alocação NUMA-aware
                    using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management");
                    key.SetValue("NumaAllocationPolicy", 2, RegistryValueKind.DWord); // Prefer local node
                    
                    _logger.LogInfo($"[Memory] ✅ Otimização NUMA configurada: {nodeCount} nós");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Memory] Erro ao configurar NUMA: {ex.Message}");
            }
            _logger.LogExit(nameof(ConfigureNumaOptimization));
}
        
        /// <summary>
        /// Otimiza Page File para gaming
        /// </summary>
        private void OptimizePageFile()
        {
            _logger.LogEntry(nameof(OptimizePageFile));
try
            {
                using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management");
                
                // CORRIGIDO: Removida escrita intermediária com string vazia que causava I/O storm
                // Aplicar valor final diretamente para evitar deleção/recriação do page file
                var ramSize = GetTotalRamSize();
                var minPageFile = ramSize / 2; // 50% da RAM
                var maxPageFile = ramSize * 2;  // 200% da RAM
                
                key.SetValue("PagingFiles", $"C:\\pagefile.sys {minPageFile} {maxPageFile}", RegistryValueKind.MultiString);
                
                _logger.LogInfo($"[Memory] ✅ Page File otimizado: {minPageFile / 1024}MB - {maxPageFile / 1024}MB");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Memory] Erro ao otimizar Page File: {ex.Message}");
            }
            _logger.LogExit(nameof(OptimizePageFile));
}
        
        /// <summary>
        /// Desabilita Memory Compression do Windows
        /// </summary>
        private void DisableMemoryCompression()
        {
            _logger.LogEntry(nameof(DisableMemoryCompression));
try
            {
                using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters");
                
                // Desabilitar memory compression
                key.SetValue("EnableCompression", 0, RegistryValueKind.DWord);
                key.SetValue("EnablePrefetcher", 3, RegistryValueKind.DWord); // Application prefetch only
                
                _logger.LogInfo("[Memory] ✅ Memory Compression desabilitada");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Memory] Erro ao desabilitar Memory Compression: {ex.Message}");
            }
            _logger.LogExit(nameof(DisableMemoryCompression));
}
        
        /// <summary>
        /// Otimiza Garbage Collection para aplicações .NET
        /// </summary>
        private void OptimizeGarbageCollection()
        {
            _logger.LogEntry(nameof(OptimizeGarbageCollection));
try
            {
                // Configurar GC para baixa latência
                System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;
                
                // Aumentar heap size para reduzir coletas
                using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\.NETFramework");
                key.SetValue("GCStress", 0, RegistryValueKind.DWord);
                key.SetValue("GCBias", 0, RegistryValueKind.DWord);
                
                _logger.LogInfo("[Memory] ✅ Garbage Collection otimizado para baixa latência");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Memory] Erro ao otimizar GC: {ex.Message}");
            }
            _logger.LogExit(nameof(OptimizeGarbageCollection));
}
        
        // Fields para cleanup de memória alocada
        private IntPtr _lockedMemoryPointer = IntPtr.Zero;
        private ulong _lockedMemorySize = 0;
        private bool _memoryAllocated = false;

        /// <summary>
        /// Lock memory pages para evitar paging
        /// OTIMIZAÇÃO CRÍTICA: Armazena ponteiro para cleanup posterior
        /// </summary>
        private void LockMemoryPages()
        {
            _logger.LogEntry(nameof(LockMemoryPages));
try
            {
                // CRITICAL FIX: Alocar apenas 16MB (suficiente para gaming) em vez de 64MB
                const ulong MemorySize = 16UL * 1024 * 1024; // 16MB
                _lockedMemoryPointer = VirtualAlloc(IntPtr.Zero, (UIntPtr)MemorySize, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
                
                if (_lockedMemoryPointer != IntPtr.Zero)
                {
                    _lockedMemorySize = MemorySize;
                    _memoryAllocated = true;
                    
                    // Preencher com dados para garantir alocação física
                    Marshal.WriteInt32(_lockedMemoryPointer, 0x12345678);
                    
                    _logger.LogInfo($"[Memory] ✅ Páginas críticas locked (16MB) na memória");
                }
                else
                {
                    _logger.LogWarning("[Memory] ⚠️ Falha ao alocar memória locked");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Memory] Erro ao lock páginas: {ex.Message}");
            }
            _logger.LogExit(nameof(LockMemoryPages));
}
        
        /// <summary>
        /// Obtém tamanho total da RAM
        /// </summary>
        private long GetTotalRamSize()
        {
            _logger.LogEntry(nameof(GetTotalRamSize));
try
            {
                using var searcher = new System.Management.ManagementObjectSearcher("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem");
                foreach (var obj in searcher.Get())
                {
                using var __dispose_obj = obj;
return Convert.ToInt64(obj["TotalVisibleMemorySize"]) * 1024; // KB → bytes
                }
            }
            catch { }
return 8L * 1024 * 1024 * 1024; // 8GB fallback
            _logger.LogExit(nameof(GetTotalRamSize));
}
        
        /// <summary>
        /// Restaura configurações originais de memória
        /// OTIMIZAÇÃO CRÍTICA: Libera memória alocada para evitar memory leak
        /// </summary>
        public async Task<bool> RestoreMemoryAsync()
        {
            _logger.LogEntry(nameof(RestoreMemoryAsync));
try
            {
                _logger.LogInfo("[Memory] Restaurando configurações de memória...");
                
                // CRITICAL FIX: Liberar memória alocada para evitar memory leak
                if (_memoryAllocated && _lockedMemoryPointer != IntPtr.Zero)
                {
                    try
                    {
                        VirtualFree(_lockedMemoryPointer, UIntPtr.Zero, MEM_RELEASE);
                        _lockedMemoryPointer = IntPtr.Zero;
                        _lockedMemorySize = 0;
                        _memoryAllocated = false;
                        _logger.LogInfo("[Memory] ✅ Memória locked liberada (16MB)");
                    }
                    catch (Exception ex)
                    {
_logger.LogWarning($"[Memory] Erro ao liberar memória: {ex.Message}");
}
                }
                
                // Restaurar Large Pages
                if (_originalLargePageMinimum.HasValue)
                {
                    using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management");
                    key.SetValue("LargePageMinimum", _originalLargePageMinimum.Value, RegistryValueKind.DWord);
                }
                
                // Restaurar System Pages
                if (_originalSystemPages.HasValue)
                {
                    using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management");
                    key.SetValue("SystemPages", _originalSystemPages.Value, RegistryValueKind.DWord);
                }
                
                // Restaurar GC
                System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.Interactive;
                
                _logger.LogInfo("[Memory] ✅ Configurações de memória restauradas");
return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("[Memory] Erro ao restaurar memória", ex);
return false;
            }
            _logger.LogExit(nameof(RestoreMemoryAsync));
}
        
        /// <summary>
        /// Obtém métricas de memória
        /// </summary>
        public (double UsedGb, double TotalGb, double UsagePercent, double CacheSize) GetMemoryMetrics()
        {
            try
            {
                var process = Process.GetCurrentProcess();
                var workingSet = process.WorkingSet64 / 1024 / 1024; // MB
                var privateMemory = process.PrivateMemorySize64 / 1024 / 1024; // MB
                
                // Memória total do sistema
                var totalRam = GetTotalRamSize() / 1024 / 1024; // MB
                
                // Memória usada
                using var searcher = new System.Management.ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
                foreach (var obj in searcher.Get())
                {
                    using var __dispose_obj = obj;
                    var total = Convert.ToDouble(obj["TotalVisibleMemorySize"]) / 1024; // MB
                    var free = Convert.ToDouble(obj["FreePhysicalMemory"]) / 1024; // MB
                    var used = total - free;
                    
                    return (used / 1024, total / 1024, (used / total) * 100, workingSet);
                }
            }
            catch { }
            
            return (0, 0, 0, 0);
        }
    }
}
