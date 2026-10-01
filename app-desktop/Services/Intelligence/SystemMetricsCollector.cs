using VoltrisOptimizer.Utils;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Intelligence
{
    /// <summary>
    /// Coletor de métricas do sistema em tempo real
    /// Implementa coleta precisa de CPU, RAM, disco, rede e processos
    /// </summary>
    public class SystemMetricsCollector
    {
        private readonly ILoggingService _logger;
        private readonly SafePerformanceCounter? _cpuCounter;
        private readonly SafePerformanceCounter? _memoryCounter;
        private readonly SafePerformanceCounter? _diskCounter;

        public SystemMetricsCollector(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            try
            {
                // Inicializar contadores de performance
                _cpuCounter = new SafePerformanceCounter("Processor", "% Processor Time", "_Total");
                _memoryCounter = new SafePerformanceCounter("Memory", "Available MBytes");
                _diskCounter = new SafePerformanceCounter("PhysicalDisk", "% Disk Time", "_Total");

                // Forçar primeira leitura para inicializar
                _cpuCounter.NextValue();
                _memoryCounter.NextValue();
                _diskCounter.NextValue();

                _logger.LogInfo("[MetricsCollector] Contadores de performance inicializados");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[MetricsCollector] Erro ao inicializar contadores: {ex.Message}");
            }
        }

        /// <summary>Obtém métricas atuais do sistema</summary>
        public async Task<SystemMetrics> GetCurrentMetricsAsync()
        {
            return await Task.Run(async () =>
            {
                try
                {
                    var metrics = new SystemMetrics
                    {
                        Timestamp = DateTime.UtcNow,
                        CpuUsage = GetCpuUsage(),
                        MemoryUsage = GetMemoryUsage(),
                        DiskUsage = GetDiskUsage(),
                        NetworkLatency = await GetNetworkLatencyAsync().ConfigureAwait(false),
                        ActiveProcesses = GetActiveProcessCount(),
                        IsGamingMode = DetectGamingMode(),
                        IsOnBattery = IsOnBattery()
                    };

                    _logger.LogDebug($"[MetricsCollector] Métricas: CPU {metrics.CpuUsage:F1}% | RAM {metrics.MemoryUsage:F1}% | Disco {metrics.DiskUsage:F1}% | Processos {metrics.ActiveProcesses}");

                    return metrics;
                }
                catch (Exception ex)
                {
                    _logger.LogError("[MetricsCollector] Erro ao obter métricas", ex);
                    return GetDefaultMetrics();
                }
            });
        }

        private double GetCpuUsage()
        {
            try
            {
                if (_cpuCounter != null)
                {
                    return _cpuCounter.NextValue();
                }

                // Fallback: WMI
                using var searcher = new ManagementObjectSearcher("select LoadPercentage from Win32_Processor");
                foreach (ManagementObject obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    return Convert.ToDouble(obj["LoadPercentage"]);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[SystemMetricsCollector] Erro ao obter CPU usage: {ex.Message}");
                using var process = Process.GetCurrentProcess();
                return process.TotalProcessorTime.TotalMilliseconds / Environment.ProcessorCount * 100;
            }

            return 0;
        }

        private double GetMemoryUsage()
        {
            try
            {
                if (_memoryCounter != null)
                {
                    var availableMB = _memoryCounter.NextValue();
                    var totalMB = GC.GetTotalMemory(false) / (1024 * 1024);
                    var totalSystemMemory = totalMB + availableMB;

                    return totalSystemMemory > 0 ? ((totalSystemMemory - availableMB) / totalSystemMemory) * 100 : 0;
                }

                // Fallback: GC
                var totalMemory = GC.GetTotalMemory(false);
                var workingSet = Environment.WorkingSet;

                return workingSet > 0 ? ((double)totalMemory / workingSet) * 100 : 0;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[SystemMetricsCollector] Erro ao obter memória: {ex.Message}");
                return 0;
            }
        }

        private double GetDiskUsage()
        {
            try
            {
                if (_diskCounter != null)
                {
                    return _diskCounter.NextValue();
                }

                // Fallback: Verificar espaço em disco em todas as unidades
                foreach (System.IO.DriveInfo drive in System.IO.DriveInfo.GetDrives())
                {
                    if (drive.IsReady)
                    {
                        var totalSpace = drive.TotalSize;
                        var freeSpace = drive.AvailableFreeSpace;

                        return totalSpace > 0 ? ((double)(totalSpace - freeSpace) / totalSpace) * 100 : 0;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[SystemMetricsCollector] Erro ao obter disco: {ex.Message}");
            }

            return 0;
        }

        private async Task<double> GetNetworkLatencyAsync()
        {
            try
            {
                // Ping para Google DNS como medida de latência de rede
                using var ping = new System.Net.NetworkInformation.Ping();
                var reply = await ping.SendPingAsync("8.8.8.8", 1000).ConfigureAwait(false);

                return reply.Status == System.Net.NetworkInformation.IPStatus.Success ? reply.RoundtripTime : 0;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[SystemMetricsCollector] Erro ao obter latência de rede: {ex.Message}");
                return 0;
            }
        }

        private int GetActiveProcessCount()
        {
            try
            {
                return Process.GetProcesses().Length;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[SystemMetricsCollector] Erro ao contar processos ativos: {ex.Message}");
                return 0;
            }
        }

        private bool DetectGamingMode()
        {
            try
            {
                var processes = Process.GetProcesses();
                var gameProcesses = new[]
                {
                    "steam", "epicgameslauncher", "origin", "uplay", "battle.net",
                    "league of legends", "valorant", "csgo", "fortnite", "minecraft",
                    "overwatch", "apex", "pubg", "cod", "fifa", "nba2k"
                };

                return processes.Any(p => gameProcesses.Contains(p.ProcessName.ToLowerInvariant()));
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[SystemMetricsCollector] Erro ao detectar gaming mode: {ex.Message}");
                return false;
            }
        }

        private bool IsOnBattery()
        {
            try
            {
                var powerStatus = System.Windows.Forms.SystemInformation.PowerStatus;
                return powerStatus.BatteryChargeStatus != System.Windows.Forms.BatteryChargeStatus.NoSystemBattery;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[SystemMetricsCollector] Erro ao verificar bateria: {ex.Message}");
                return false;
            }
        }

        private SystemMetrics GetDefaultMetrics()
        {
            return new SystemMetrics
            {
                Timestamp = DateTime.UtcNow,
                CpuUsage = 0,
                MemoryUsage = 0,
                DiskUsage = 0,
                NetworkLatency = 0,
                ActiveProcesses = 0,
                IsGamingMode = false,
                IsOnBattery = false
            };
        }

        public void Dispose()
        {
            _cpuCounter?.Dispose();
            _memoryCounter?.Dispose();
            _diskCounter?.Dispose();
        }
    }
}

