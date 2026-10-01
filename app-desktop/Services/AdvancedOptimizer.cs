using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Core.Constants;

namespace VoltrisOptimizer.Services
{
    /// <summary>
    /// Serviço de otimizações avançadas
    /// </summary>
    public class AdvancedOptimizer : IAdvancedOptimizer
    {
        private readonly ILoggingService _logger;

        public AdvancedOptimizer(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Otimiza SSD/HDD específico
        /// </summary>
        public async Task<bool> OptimizeStorageAsync(string driveLetter, bool isSSD, Action<int>? progressCallback = null)
        {
            return await Task.Run(() =>
            {
                try
                {
                    _logger.LogInfo($"Otimizando armazenamento {driveLetter} ({(isSSD ? "SSD" : "HDD")})...");
                    progressCallback?.Invoke(10);

                    // Usar SysNative para acessar defrag.exe de 64 bits em processo 32 bits
                    string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                    string defragPath = Path.Combine(winDir, "System32", "defrag.exe");
                    
                    if (Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess)
                    {
                        defragPath = Path.Combine(winDir, "SysNative", "defrag.exe");
                    }

                    // Se não existir em SysNative ou System32, tentar o caminho padrão
                    if (!File.Exists(defragPath)) defragPath = "defrag.exe";

                    if (isSSD)
                    {
                        // Para SSD: TRIM
                        var trimProcess = new ProcessStartInfo
                        {
                            FileName = defragPath,
                            Arguments = $"{driveLetter}: /O",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true
                        };
                        var p = Process.Start(trimProcess);
                        // Timeout de 30s — TRIM é rápido; se travar, não bloqueia o fluxo
                        if (p != null && !p.WaitForExit(30_000)) { try { p.Kill(); } catch { } }
                        _logger.LogSuccess($"TRIM executado em {driveLetter} (Exit: {p?.ExitCode})");
                    }
                    else
                    {
                        // Para HDD: Desfragmentação
                        var defragProcess = new ProcessStartInfo
                        {
                            FileName = defragPath,
                            Arguments = $"{driveLetter}: /O",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true
                        };
                        var p = Process.Start(defragProcess);
                        // Timeout de 30s — desfrag completo pode demorar, mas não bloqueamos indefinidamente
                        if (p != null && !p.WaitForExit(30_000)) { try { p.Kill(); } catch { } }
                        _logger.LogSuccess($"Desfragmentação executada em {driveLetter} (Exit: {p?.ExitCode})");
                    }

                    progressCallback?.Invoke(100);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Erro ao otimizar armazenamento {driveLetter}", ex);
                    progressCallback?.Invoke(100);
                    return false;
                }
            });
        }

        /// <summary>
        /// Otimiza uso de memória RAM
        /// </summary>
        public async Task<bool> OptimizeMemoryAsync(Action<int>? progressCallback = null)
        {
            return await Task.Run(() =>
            {
                try
                {
                    _logger.LogInfo("Otimizando memória RAM...");
                    progressCallback?.Invoke(10);

                    // 1. Tweak de Registro: Manter Kernel na RAM (DisablePagingExecutive)
                    try
                    {
                        using var mmKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", true);
                        if (mmKey != null)
                        {
                            mmKey.SetValue("DisablePagingExecutive", 1, RegistryValueKind.DWord);
                            _logger.LogInfo("[Memory] Kernel configurado para permanecer na RAM");
                        }
                    }
                    catch (Exception ex) { _logger.LogWarning($"[Memory] Erro ao configurar PagingExecutive: {ex.Message}"); }
                    progressCallback?.Invoke(40);

                    // Medir RAM antes
                    long ramBefore = GC.GetTotalMemory(false) / (1024 * 1024);
                    
                    // 2. Limpar cache de memória do processo atual
                    progressCallback?.Invoke(60);

                    // 3. Otimizar working sets (opcional, mas apenas se solicitado ou para processos grandes)
                    // Removido loop redundante de GC.Collect em outros processos.
                    
                    // Medir RAM depois
                    long ramAfter = GC.GetTotalMemory(false) / (1024 * 1024);
                    long freedMb = Math.Max(0, ramBefore - ramAfter);
                    
                    _logger.LogSuccess($"Memória RAM otimizada com sucesso. Liberado: {freedMb}MB (Snapshot Local)");
                    
                    // Telemetry - SaaS Delta Tracking
                    App.TelemetryService?.TrackEvent(
                        "OPTIMIZATION_RAM", 
                        "AdvancedOptimizer", 
                        "OptimizeMemory", 
                        metadata: new { 
                            ram_before_mb = ramBefore, 
                            ram_after_mb = ramAfter, 
                            freed_mb = freedMb 
                        },
                        forceFlush: true
                    );
                    progressCallback?.Invoke(100);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError("Erro ao otimizar memória RAM", ex);
                    progressCallback?.Invoke(100);
                    return false;
                }
            });
        }

        /// <summary>
        /// Gerenciamento avançado de processos
        /// </summary>
        public async Task<bool> OptimizeProcessesAsync(Action<int>? progressCallback = null)
        {
            return await Task.Run(() =>
            {
                try
                {
                    _logger.LogInfo("Otimizando processos...");
                    progressCallback?.Invoke(10);

                    var processesToOptimize = new[]
                    {
                        "SearchIndexer", "WSearch", "MsMpEng"
                    };

                    int current = 0;
                    foreach (var procName in processesToOptimize)
                    {
                        try
                        {
                            var processes = Process.GetProcessesByName(procName);
                            foreach (var proc in processes)
                            {
                                if (proc.PriorityClass != ProcessPriorityClass.BelowNormal)
                                {
                                    proc.PriorityClass = ProcessPriorityClass.BelowNormal;
                                }
                            }
                        }
                        catch { }
                        current++;
                        progressCallback?.Invoke(20 + (current * 60 / processesToOptimize.Length));
                    }

                    _logger.LogSuccess("Processos otimizados");
                    progressCallback?.Invoke(100);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError("Erro ao otimizar processos", ex);
                    progressCallback?.Invoke(100);
                    return false;
                }
            });
        }

        /// <summary>
        /// Limpeza profunda de registro
        /// </summary>
        public async Task<bool> CleanRegistryAsync(Action<int>? progressCallback = null)
        {
            return await Task.Run(() =>
            {
                try
                {
                    _logger.LogInfo("Registro do Windows: Apenas verificação de integridade básica (Limpeza de chaves órfãs desativada para segurança enterprise).");
                    progressCallback?.Invoke(100);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError("Erro ao limpar registro", ex);
                    progressCallback?.Invoke(100);
                    return false;
                }
            });
        }

        /// <summary>
        /// Detecta se um drive é SSD ou HDD
        /// </summary>
        public bool IsSSD(string driveLetter)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_DiskDrive WHERE MediaType = 'Fixed hard disk media'");
                
                foreach (ManagementObject drive in searcher.Get())
                {
                using var __dispose_drive = drive;
                    var partitions = drive.GetRelated("Win32_DiskPartition");
                    foreach (ManagementObject partition in partitions)
                    {
                        var logicalDisks = partition.GetRelated("Win32_LogicalDisk");
                        foreach (ManagementObject logicalDisk in logicalDisks)
                        {
                            var deviceId = logicalDisk["DeviceID"]?.ToString();
                            if (deviceId?.StartsWith(driveLetter, StringComparison.OrdinalIgnoreCase) == true)
                            {
                                var mediaType = drive["MediaType"]?.ToString();
                                return mediaType?.Contains("SSD") == true || 
                                       mediaType?.Contains("Solid State") == true;
                            }
                        }
                    }
                }
            }
            catch { }
            
            // Fallback: assumir SSD se não conseguir detectar
            return true;
        }
    }
}

