using VoltrisOptimizer.Utils;
using VoltrisOptimizer.Helpers;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.ServiceProcess;
using VoltrisOptimizer.Services.Gamer.Interfaces;

namespace VoltrisOptimizer.Services.Gamer.GamerModeManager.Services
{
    /// <summary>
    /// Kernel Optimization Service - Otimizações em kernel mode para gaming
    /// Desabilita serviços desnecessários, otimiza scheduler, I/O priority
    /// </summary>
    public class KernelOptimizerService : IKernelOptimizerService
    {
        private readonly ILoggingService _logger;
        
        // Backup de configurações
        private readonly System.Collections.Generic.List<string> _disabledServices = new();
        private int? _originalSystemResponsiveness;
        private int? _originalIoPriority;
        
        // APIs nativas
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenThread(uint dwDesiredAccess, bool bInheritHandle, uint dwThreadId);
        
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetThreadPriority(IntPtr hThread, int nPriority);
        
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetPriorityClass(IntPtr hProcess, uint dwPriorityClass);
        
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetCurrentProcess();
        
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, int dwMinimumWorkingSetSize, int dwMaximumWorkingSetSize);
        
        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtSetInformationProcess(IntPtr hProcess, int processInformationClass, ref int processInformation, int processInformationLength);
        
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);
        
        // Constantes
        private const int THREAD_BASE_PRIORITY_LOWEST = -2;
        private const int THREAD_BASE_PRIORITY_MAX = 2;
        private const int PROCESS_PRIORITY_CLASS_HIGH = 0x00000080;
        private const int PROCESS_INFORMATION_PRIORITY_CLASS = 0x00000008;
        private const int THREAD_INFORMATION_PRIORITY = 0x00000004;
        private const int PROCESS_PRIORITY_CLASS_REALTIME = 0x00000100;
        
        // CORRIGIDO: Removidos serviços críticos que causam crash/instabilidade.
        // Audiosrv: necessário para áudio do jogo
        // Themes+DWM: necessários para composição de tela cheia
        // Dhcp: necessário para conectividade de rede
        // PrintSpooler: seguro parar
        // Serviços desnecessários para gaming
        private static readonly string[] UnnecessaryServices = new[]
        {
"SysMain",           // Superfetch/Prefetch
            "BITS",             // Background Intelligent Transfer Service
            "WindowsUpdate",    // Windows Update
            "wuauserv",         // Windows Update
            "TabletInputService", // Tablet PC
            "WSearch",          // Windows Search
            "HomeGroupListener", // HomeGroup
            "HomeGroupProvider", // HomeGroup
            "lfsvc",            // Geolocation Service
            "MapsBroker",       // Downloaded Maps Manager
            "Fax",              // Fax Service
            "PrintSpooler",     // Impressão
            "WbioSrvc",         // Windows Biometric Service
            "DiagTrack",        // Telemetria
            "dmwappushservice", // WAP Push Message Routing Service
            "SharedAccess",     // Internet Connection Sharing
            "TrkWks",           // Distributed Link Tracking Client
            "WdiServiceHost",   // Diagnostic Service Host
            "WdiSystemHost",    // Diagnostic System Host
            "DPS",              // Diagnostic Policy Service
            "Wercplsupport",    // Windows Error Reporting
            "WerSvc"            // Windows Error Reporting Service
};
        
        public KernelOptimizerService(ILoggingService logger)
        {
            _logger.LogEntry(nameof(KernelOptimizerService));
_logger = logger;
            _logger.LogExit(nameof(KernelOptimizerService));
}
        
        /// <summary>
        /// Otimiza kernel para gaming
        /// </summary>
        public async Task<bool> OptimizeKernelAsync()
        {
            _logger.LogEntry(nameof(OptimizeKernelAsync));
try
            {
                _logger.LogInfo("[Kernel] Iniciando otimização em kernel mode...");
                
                // 1. Desabilitar serviços desnecessários
                await DisableUnnecessaryServicesAsync();
                
                // 2. Otimizar thread scheduler
                OptimizeThreadScheduler();
                
                // 3. Elevar prioridade de I/O
                OptimizeIoPriority();
                
                // 4. Otimizar memory management
                OptimizeMemoryManagement();
                
                // 5. Configurar real-time scheduling
                ConfigureRealTimeScheduling();
                
                // 6. Otimizar interrupt handling
                OptimizeInterruptHandling();
                
                // 7. Desabilitar power saving features
                DisablePowerSavingFeatures();
                
                _logger.LogSuccess("[Kernel] ✅ Otimização kernel concluída - Sistema em modo gaming!");
return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("[Kernel] Erro na otimização kernel", ex);
return false;
            }
            _logger.LogExit(nameof(OptimizeKernelAsync));
}
        
        /// <summary>
        /// Desabilita serviços desnecessários para gaming
        /// </summary>
        private async Task DisableUnnecessaryServicesAsync()
        {
            _logger.LogEntry(nameof(DisableUnnecessaryServicesAsync));
try
            {
                foreach (var serviceName in UnnecessaryServices)
                {
                    try
                    {
                        using var service = new ServiceController(serviceName);
                        if (service.Status == ServiceControllerStatus.Running)
                        {
                            // Backup do estado original
                            var startType = GetServiceStartType(serviceName);
                            if (startType != null)
                            {
                                _disabledServices.Add($"{serviceName}:{startType}");
                            }
                            
                            // Parar serviço
                            service.Stop();
                            service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                            
                            // Desabilitar startup
                            SetServiceStartType(serviceName, "Disabled");
                            
                            _logger.LogInfo($"[Kernel] ✅ Serviço desabilitado: {
serviceName}");
}
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug($"[Kernel] Serviço não encontrado ou sem permissão: {serviceName} - {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Kernel] Erro ao desabilitar serviços: {ex.Message}");
            }
            _logger.LogExit(nameof(DisableUnnecessaryServicesAsync));
}
        
        /// <summary>
        /// Otimiza thread scheduler para gaming
        /// </summary>
        private void OptimizeThreadScheduler()
        {
            _logger.LogEntry(nameof(OptimizeThreadScheduler));
try
            {
                // Elevar prioridade do processo atual
                var currentProcess = GetCurrentProcess();
                SetPriorityClass(currentProcess, PROCESS_PRIORITY_CLASS_HIGH);
                
                // Configurar quantum de threads para gaming
                using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\PriorityControl");
                
                // Backup
                _originalSystemResponsiveness = key.GetValue("Win32PrioritySeparation") as int?;
                
                // Otimizar para foreground boost
                key.SetValue("Win32PrioritySeparation", 0x18, RegistryValueKind.DWord);
                
                // Configurar quantum para threads de gaming
                key.SetValue("QuantumLength", 6, RegistryValueKind.DWord); // 6ms quantum
                
                _logger.LogInfo("[Kernel] ✅ Thread scheduler otimizado");
            }
            catch (Exception ex)
            {
_logger.LogWarning($"[Kernel] Erro ao otimizar scheduler: {ex.Message}");
}
            _logger.LogExit(nameof(OptimizeThreadScheduler));
}
        
        /// <summary>
        /// Otimiza prioridade de I/O para gaming
        /// </summary>
        private void OptimizeIoPriority()
        {
            _logger.LogEntry(nameof(OptimizeIoPriority));
try
            {
                // Configurar I/O priority para alta
                using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management");
                
                // Backup
                _originalIoPriority = key.GetValue("IoPriority") as int?;
                
                // Configurar I/O priority alta
                key.SetValue("IoPriority", 3, RegistryValueKind.DWord); // High priority
                
                // Desabilitar I/O throttling
                key.SetValue("IoPageLockLimit", -1, RegistryValueKind.DWord);
                
                _logger.LogInfo("[Kernel] ✅ I/O priority otimizada");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Kernel] Erro ao otimizar I/O: {ex.Message}");
            }
            _logger.LogExit(nameof(OptimizeIoPriority));
}
        
        /// <summary>
        /// Otimiza gerenciamento de memória
        /// </summary>
        private void OptimizeMemoryManagement()
        {
            _logger.LogEntry(nameof(OptimizeMemoryManagement));
try
            {
                using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management");
                
                // Desabilitar paging executivo
                key.SetValue("DisablePagingExecutive", 1, RegistryValueKind.DWord);
                
                // Aumentar tamanho do pool
                key.SetValue("PagedPoolSize", 0x300000, RegistryValueKind.DWord);
                key.SetValue("NonPagedPoolSize", 0x200000, RegistryValueKind.DWord);
                
                // Desabilitar memory compression
                key.SetValue("EnableCompression", 0, RegistryValueKind.DWord);
                
                _logger.LogInfo("[Kernel] ✅ Memory management otimizado");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Kernel] Erro ao otimizar memory: {ex.Message}");
            }
            _logger.LogExit(nameof(OptimizeMemoryManagement));
}
        
        /// <summary>
        /// Configura real-time scheduling para gaming
        /// </summary>
        private void ConfigureRealTimeScheduling()
        {
            _logger.LogEntry(nameof(ConfigureRealTimeScheduling));
try
            {
                // Configurar processo para real-time (com cuidado)
                var currentProcess = GetCurrentProcess();
                
                // Usar High priority em vez de Real-time para evitar instabilidade
                SetPriorityClass(currentProcess, PROCESS_PRIORITY_CLASS_HIGH);
                
                // Configurar threads principais para alta prioridade
                foreach (ProcessThread thread in Process.GetCurrentProcess().Threads)
                {
var hThread = OpenThread(0x0200, false, (uint)thread.Id); // THREAD_SET_INFORMATION
                    if (hThread != IntPtr.Zero)
                    {
                        SetThreadPriority(hThread, THREAD_BASE_PRIORITY_MAX);
                        CloseHandle(hThread);
                    }
}
                
                _logger.LogInfo("[Kernel] ✅ Real-time scheduling configurado");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Kernel] Erro ao configurar real-time: {ex.Message}");
            }
            _logger.LogExit(nameof(ConfigureRealTimeScheduling));
}
        
        /// <summary>
        /// Otimiza handling de interrupções
        /// </summary>
        private void OptimizeInterruptHandling()
        {
            _logger.LogEntry(nameof(OptimizeInterruptHandling));
try
            {
                // Configurar interrupt affinity para CPUs de gaming
                using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\PriorityControl");
                
                // Otimizar interrupt handling
                key.SetValue("IRQ8Priority", 1, RegistryValueKind.DWord);
                key.SetValue("IRQ16Priority", 1, RegistryValueKind.DWord);
                
                // Desabilitar interrupt sharing
                key.SetValue("IRQPriority", 1, RegistryValueKind.DWord);
                
                _logger.LogInfo("[Kernel] ✅ Interrupt handling otimizado");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Kernel] Erro ao otimizar interrupts: {ex.Message}");
            }
            _logger.LogExit(nameof(OptimizeInterruptHandling));
}
        
        /// <summary>
        /// Desabilita recursos de economia de energia
        /// </summary>
        private void DisablePowerSavingFeatures()
        {
            _logger.LogEntry(nameof(DisablePowerSavingFeatures));
try
            {
                using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Power");
                
                // Desabilitar C-states
                key.SetValue("CpuLatency", 0, RegistryValueKind.DWord);
                key.SetValue("Latency", 0, RegistryValueKind.DWord);
                
                // Desabilitar deep sleep
                key.SetValue("SleepInactivityTimeout", 0, RegistryValueKind.DWord);
                
                // Desabilitar adaptive brightness
                using var videoKey = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Video");
                foreach (var subKeyName in videoKey.GetSubKeyNames())
                {
                    try
                    {
                        using var subKey = videoKey.OpenSubKey($@"{subKeyName}\0000", true);
                        if (subKey != null)
                        {
                            subKey.SetValue("AdaptiveBrightness", 0, RegistryValueKind.DWord);
                        }
                    }
                    catch { }
                }
                
                _logger.LogInfo("[Kernel] ✅ Power saving desabilitado");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Kernel] Erro ao desabilitar power saving: {ex.Message}");
            }
            _logger.LogExit(nameof(DisablePowerSavingFeatures));
}
        
        /// <summary>
        /// Obtém tipo de startup do serviço
        /// </summary>
        private string? GetServiceStartType(string serviceName)
        {
            _logger.LogEntry(nameof(GetServiceStartType));
try
            {
                using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
return key?.GetValue("Start")?.ToString();
            }
            catch { }
return null;
            _logger.LogExit(nameof(GetServiceStartType));
}
        
        /// <summary>
        /// Define tipo de startup do serviço
        /// </summary>
        private void SetServiceStartType(string serviceName, string startType)
        {
            _logger.LogEntry(nameof(SetServiceStartType));
try
            {
                using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}", true);
                key?.SetValue("Start", startType == "Disabled" ? 4 : 2, RegistryValueKind.DWord);
            }
            catch { }
            _logger.LogExit(nameof(SetServiceStartType));
}
        
        /// <summary>
        /// Restaura configurações originais do kernel
        /// </summary>
        public async Task<bool> RestoreKernelAsync()
        {
            _logger.LogEntry(nameof(RestoreKernelAsync));
try
            {
                _logger.LogInfo("[Kernel] Restaurando configurações do kernel...");
                
                // Restaurar serviços
                foreach (var serviceInfo in _disabledServices)
                {
                    var parts = serviceInfo.Split(':');
                    if (parts.Length == 2)
                    {
                        var serviceName = parts[0];
                        var originalStartType = parts[1];
                        
                        try
                        {
                            SetServiceStartType(serviceName, originalStartType);
                            using var service = new ServiceController(serviceName);
                            if (originalStartType == "2" || originalStartType == "3") // Auto/Demand
                            {
service.Start();
}
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"[Kernel] Erro ao restaurar serviço {serviceName}: {ex.Message}");
                        }
                    }
                }
                
                // Restaurar scheduler
                if (_originalSystemResponsiveness.HasValue)
                {
                    using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\PriorityControl");
                    key.SetValue("Win32PrioritySeparation", _originalSystemResponsiveness.Value, RegistryValueKind.DWord);
                }
                
                // Restaurar I/O priority
                if (_originalIoPriority.HasValue)
                {
                    using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management");
                    key.SetValue("IoPriority", _originalIoPriority.Value, RegistryValueKind.DWord);
                }
                
                // Restaurar priority class
                var currentProcess = GetCurrentProcess();
                SetPriorityClass(currentProcess, 0x00000020); // NORMAL_PRIORITY_CLASS

                // ROLLBACK das chaves do registry gravadas em OptimizeKernelAsync.
                // Antes ficavam persistidas PARA SEMPRE mesmo após desativar o modo gaming.
                var memoryKey = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management");
                memoryKey.DeleteValue("DisablePagingExecutive", false);
                memoryKey.DeleteValue("PagedPoolSize", false);
                memoryKey.DeleteValue("NonPagedPoolSize", false);
                memoryKey.DeleteValue("EnableCompression", false);
                if (_originalIoPriority.HasValue)
                {
                    memoryKey.SetValue("IoPriority", _originalIoPriority.Value, RegistryValueKind.DWord);
                }
                else
                {
                    memoryKey.DeleteValue("IoPriority", false);
                }
                memoryKey.DeleteValue("IoPageLockLimit", false);
                memoryKey.Dispose();
                _logger.LogInfo("[Kernel] ✅ Memória revertida (DisablePagingExecutive, PagedPoolSize, NonPagedPoolSize, EnableCompression, IoPriority, IoPageLockLimit)");

                using (var priorityKey = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\PriorityControl"))
                {
                    priorityKey.DeleteValue("QuantumLength", false);
                    priorityKey.DeleteValue("IRQ8Priority", false);
                    priorityKey.DeleteValue("IRQ16Priority", false);
                    priorityKey.DeleteValue("IRQPriority", false);
                    if (_originalSystemResponsiveness.HasValue)
                    {
                        priorityKey.SetValue("Win32PrioritySeparation", _originalSystemResponsiveness.Value, RegistryValueKind.DWord);
                    }
                    else
                    {
                        priorityKey.DeleteValue("Win32PrioritySeparation", false);
                    }
                    _logger.LogInfo("[Kernel] ✅ Scheduler/Interrupts revertidos (QuantumLength, IRQ8Priority, IRQ16Priority, IRQPriority, Win32PrioritySeparation)");
                }

                using (var powerKey = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Power"))
                {
                    powerKey.DeleteValue("CpuLatency", false);
                    powerKey.DeleteValue("Latency", false);
                    powerKey.DeleteValue("SleepInactivityTimeout", false);
                    _logger.LogInfo("[Kernel] ✅ Power saving revertido (CpuLatency, Latency, SleepInactivityTimeout)");
                }

                using (var videoKey = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Video"))
                {
                    foreach (var subKeyName in videoKey.GetSubKeyNames())
                    {
                        try
                        {
                            using var subKey = videoKey.OpenSubKey($@"{subKeyName}\0000", true);
                            if (subKey != null && subKey.GetValue("AdaptiveBrightness") != null)
                            {
                                subKey.DeleteValue("AdaptiveBrightness", false);
                                _logger.LogInfo($@"[Kernel] ✅ AdaptiveBrightness revertida em Video\{subKeyName}\0000");
                            }
                        }
                        catch { }
                    }
                }

                _logger.LogInfo("[Kernel] ✅ Configurações do kernel restauradas");
return true;
            }
            catch (Exception ex)
            {
_logger.LogError("[Kernel] Erro ao restaurar kernel", ex);
                return false;
}
            _logger.LogExit(nameof(RestoreKernelAsync));
}
        
        /// <summary>
        /// Obtém métricas do kernel
        /// </summary>
        public (int ActiveThreads, double CpuUsage, int ContextSwitches, int Interrupts) GetKernelMetrics()
        {
            try
            {
                var process = Process.GetCurrentProcess();
                var activeThreads = process.Threads.Count;
                
                // CPU usage
                using var cpuCounter = new SafePerformanceCounter("Processor", "% Processor Time", "_Total");
                var cpuUsage = cpuCounter.NextValue();
                
                // Context switches (via Performance Counters)
                using var contextCounter = new SafePerformanceCounter("System", "Context Switches/sec");
                var contextSwitches = (int)contextCounter.NextValue();
                
                // Interrupts
                using var interruptCounter = new SafePerformanceCounter("System", "Interrupts/sec");
                var interrupts = (int)interruptCounter.NextValue();
                
                return (activeThreads, cpuUsage, contextSwitches, interrupts);
            }
            catch
            {
                return (0, 0, 0, 0);
            }
        }
    }
}

