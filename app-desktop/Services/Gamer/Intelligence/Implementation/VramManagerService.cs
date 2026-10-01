using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.Gamer.Intelligence.Interfaces;
using VoltrisOptimizer.Services.Gamer.Intelligence.Models;
using VoltrisOptimizer.Utils;

namespace VoltrisOptimizer.Services.Gamer.Intelligence.Implementation
{
    /// <summary>
    /// Gerenciador de VRAM - monitora e otimiza uso de memória de vídeo
    /// </summary>
    public class VramManagerService : IVramManager, IDisposable
    {
        private readonly ILoggingService _logger;
        private VramStatus _currentStatus = new();
        private CancellationTokenSource? _monitoringCts;
        private Task? _monitoringTask;
        private readonly object _lock = new();

        private const double CriticalThreshold = 90.0;
        private const double WarningThreshold = 75.0;

        // Processos que podem ser otimizados para liberar VRAM
        private static readonly HashSet<string> OptimizableProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "chrome", "firefox", "msedge", "opera", "brave", // Browsers
            "discord", "slack", "teams", // Chat
            "spotify", "itunes", // Media
            "wallpaperengine", "rainmeter", // Desktop customization
            "obs64", "streamlabs", // Streaming (quando não em uso)
            "dwm", "explorer" // Windows Shell (Otimização agressiva)
        };

        public VramStatus CurrentStatus => _currentStatus;
        public event EventHandler<VramStatus>? VramCritical;

        public VramManagerService(ILoggingService logger)
        {
            _logger = logger;
        }

        public void StartMonitoring(int intervalMs = 1000)
        {
            StopMonitoring();
            _monitoringCts = new CancellationTokenSource();
            _monitoringTask = MonitorLoop(intervalMs, _monitoringCts.Token);
            _logger.LogInfo("[VramManager] Iniciando monitoramento de VRAM");
        }

        public void StopMonitoring()
        {
            if (_monitoringCts != null)
            {
                _monitoringCts.Cancel();
                try { _monitoringTask?.Wait(1000); } catch { }
                _monitoringCts.Dispose();
                _monitoringCts = null;
            }
        }

        private async Task MonitorLoop(int intervalMs, CancellationToken ct)
        {
            bool wasCritical = false;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    UpdateVramStatus();

                    // Detecta transição para estado crítico
                    if (_currentStatus.IsCritical && !wasCritical)
                    {
                        wasCritical = true;
                        _logger.LogWarning($"[VramManager] ⚠️ VRAM CRÍTICA: {_currentStatus.UsagePercent:F1}%");
                        VramCritical?.Invoke(this, _currentStatus);
                    }
                    else if (!_currentStatus.IsCritical && wasCritical)
                    {
                        wasCritical = false;
                        _logger.LogInfo("[VramManager] VRAM normalizada");
                    }

                    await Task.Delay(intervalMs, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError($"[VramManager] Erro no monitoramento: {ex.Message}");
                    await Task.Delay(5000, ct);
                }
            }
        }

        private void UpdateVramStatus()
        {
            try
            {
                var (total, used) = GetVramUsage();
                var consumers = GetVramConsumers();

                lock (_lock)
                {
                    _currentStatus = new VramStatus
                    {
                        TotalBytes = total,
                        UsedBytes = used,
                        AvailableBytes = total - used,
                        TopConsumers = consumers
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[VramManager] Erro ao atualizar status: {ex.Message}");
            }
        }

        private (long Total, long Used) GetVramUsage()
        {
            long totalVram = 0;
            long usedVram = 0;

            try
            {
                // Tenta NVIDIA via WMI
                try
                {
                    using var searcher = new ManagementObjectSearcher(
                        @"root\CIMV2\NV", "SELECT * FROM NvFBMemoryUsage");
                    
                    foreach (ManagementObject obj in searcher.Get())
                    {
                using var __dispose_obj = obj;
                        totalVram = Convert.ToInt64(obj["TotalFBMemory"]) * 1024; // KB -> Bytes
                        usedVram = Convert.ToInt64(obj["UsedFBMemory"]) * 1024;
                        return (totalVram, usedVram);
                    }
                }
                catch { }

                // Fallback: Win32_VideoController
                using var videoSearcher = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_VideoController");
                
                foreach (ManagementObject obj in videoSearcher.Get())
                {
                using var __dispose_obj = obj;
                    var adapterRam = Convert.ToInt64(obj["AdapterRAM"] ?? 0);
                    if (adapterRam > totalVram)
                        totalVram = adapterRam;
                }

                // Estima uso baseado em processos gráficos
                if (totalVram > 0)
                {
                    var estimated = EstimateVramUsageFromProcesses();
                    usedVram = Math.Min(estimated, totalVram);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[VramManager] Erro ao obter VRAM: {ex.Message}");
            }

            return (totalVram, usedVram);
        }

        private long EstimateVramUsageFromProcesses()
        {
            long estimated = 0;

            try
            {
                // Processos conhecidos por usar muita VRAM
                var gpuProcesses = new[] { "dwm", "chrome", "firefox", "msedge", "discord" };
                
                foreach (var procName in gpuProcesses)
                {
                    try
                    {
                        var procs = Process.GetProcessesByName(procName);
                        foreach (var proc in procs)
                        {
                            // Estima VRAM como ~20% da memória privada para processos gráficos
                            estimated += proc.PrivateMemorySize64 / 5;
                        }
                    }
                    catch { }
                }

                // Adiciona uso do DWM (compositor do Windows)
                try
                {
                    var dwm = Process.GetProcessesByName("dwm").FirstOrDefault();
                    if (dwm != null)
                    {
                        // DWM pode usar bastante VRAM dependendo da resolução
                        estimated += dwm.WorkingSet64 / 2;
                    }
                }
                catch { }
            }
            catch { }

            return estimated;
        }

        private List<VramConsumer> GetVramConsumers()
        {
            var consumers = new List<VramConsumer>();

            try
            {
                // Obtém processos ordenados por uso de memória gráfica
                var processes = Process.GetProcesses()
                    .Where(p => !string.IsNullOrEmpty(p.MainWindowTitle) || 
                               IsKnownGpuProcess(p.ProcessName))
                    .Select(p =>
                    {
                        try
                        {
                            return new VramConsumer
                            {
                                ProcessName = p.ProcessName,
                                ProcessId = p.Id,
                                VramBytes = EstimateProcessVram(p),
                                IsGame = IsGameProcess(p.ProcessName),
                                CanBeOptimized = OptimizableProcesses.Contains(p.ProcessName)
                            };
                        }
                        catch
                        {
                            return null;
                        }
                    })
                    .Where(c => c != null && c.VramBytes > 0)
                    .OrderByDescending(c => c!.VramBytes)
                    .Take(10)
                    .Cast<VramConsumer>()
                    .ToList();

                return processes;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[VramManager] Erro ao obter consumidores: {ex.Message}");
            }

            return consumers;
        }

        private bool IsKnownGpuProcess(string name)
        {
            var gpuProcesses = new[] { 
                "dwm", "chrome", "firefox", "msedge", "discord", 
                "steam", "epicgameslauncher", "origin", "battlenet" 
            };
            return gpuProcesses.Contains(name.ToLowerInvariant());
        }

        private bool IsGameProcess(string name)
        {
            // Heurística simples - jogos geralmente não têm esses nomes
            var nonGames = new[] { 
                "chrome", "firefox", "msedge", "discord", "spotify",
                "steam", "origin", "epicgameslauncher", "dwm", "explorer"
            };
            return !nonGames.Contains(name.ToLowerInvariant());
        }

        private long EstimateProcessVram(Process process)
        {
            try
            {
                // Usa GDI objects como proxy para uso de GPU
                var gdiObjects = GetGuiResources(process.Handle, GR_GDIOBJECTS);
                var userObjects = GetGuiResources(process.Handle, GR_USEROBJECTS);

                // Estima VRAM baseado em recursos GUI + memória privada
                long baseEstimate = process.PrivateMemorySize64 / 10;
                long guiEstimate = (gdiObjects + userObjects) * 4096; // ~4KB por objeto

                return baseEstimate + guiEstimate;
            }
            catch
            {
                return 0;
            }
        }

        public IReadOnlyList<VramConsumer> GetTopConsumers()
        {
            lock (_lock)
            {
                return _currentStatus.TopConsumers.ToList();
            }
        }

        public async Task<long> FreeVramAsync(CancellationToken cancellationToken = default)
        {
            long freedBytes = 0;

            try
            {
                _logger.LogInfo("[VramManager] Iniciando liberação de VRAM...");

                var consumers = GetTopConsumers()
                    .Where(c => c.CanBeOptimized && !c.IsGame)
                    .ToList();

                foreach (var consumer in consumers)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    try
                    {
                        // [FIX:C-3] O ProcessId vem da lista de consumidores de VRAM
                // montada antes; entre a coleta e este ponto o processo pode ter
                // encerrado, e GetProcessById lançava ArgumentException dentro
                // do laço.
                using var process = SafeProcess.TryGet(consumer.ProcessId);
                if (process == null) continue;
                        
                        // Não fecha o processo, apenas tenta reduzir seu working set
                        if (MinimizeProcessMemory(process))
                        {
                            freedBytes += consumer.VramBytes / 2; // Estima que liberou metade
                            _logger.LogInfo($"[VramManager] Memória reduzida: {consumer.ProcessName}");
                        }
                    }
                    catch { }
                }

                // Força garbage collection do sistema

                // Limpa standby list (se tiver permissão)
                try
                {
                    ClearStandbyList();
                    freedBytes += 100 * 1024 * 1024; // Estima ~100MB
                }
                catch { }

                _logger.LogInfo($"[VramManager] VRAM liberada: ~{freedBytes / (1024 * 1024)}MB");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[VramManager] Erro ao liberar VRAM: {ex.Message}");
            }

            return freedBytes;
        }

        private bool MinimizeProcessMemory(Process process)
        {
            try
            {
                // Força o Windows a revisar o working set do processo
                SetProcessWorkingSetSize(process.Handle, (IntPtr)(-1), (IntPtr)(-1));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void ClearStandbyList()
        {
            try
            {
                global::VoltrisOptimizer.Services.Optimization.Memory.VoltrisMemoryCoordinator.Instance.CleanStandbyList();
            }
            catch { }
        }

        public void Dispose()
        {
            StopMonitoring();
            GC.SuppressFinalize(this);
        }

        #region Native Methods

        [DllImport("user32.dll")]
        private static extern uint GetGuiResources(IntPtr hProcess, uint uiFlags);
        
        private const uint GR_GDIOBJECTS = 0;
        private const uint GR_USEROBJECTS = 1;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);

        [DllImport("ntdll.dll")]
        private static extern int NtSetSystemInformation(int infoClass, ref SYSTEM_MEMORY_LIST_INFORMATION info, int length);

        private const int SystemMemoryListInformation = 80;
        private const int MemoryPurgeStandbyList = 4;

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_MEMORY_LIST_INFORMATION
        {
            public int Command;
        }

        #endregion
    }
}
