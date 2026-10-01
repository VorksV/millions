using VoltrisOptimizer.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Assistant
{
    /// <summary>
    /// Motor de contexto para análise situacional do sistema
    /// Implementa análise real de contexto para comandos inteligentes
    /// </summary>
    public class ContextEngine
    {
        private readonly ILoggingService _logger;

        public ContextEngine(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogInfo("[ContextEngine] Motor de contexto inicializado");
        }

        /// <summary>
        /// Analisa contexto do comando processado
        /// </summary>
        public async Task<SystemContext> AnalyzeContextAsync(ProcessedInput processedInput)
        {
            _logger.LogInfo("[ContextEngine] Analisando contexto do comando...");

            try
            {
                var context = await GetCurrentContextAsync();
                context = EnrichContextFromInput(context, processedInput);

                _logger.LogDebug($"[ContextEngine] Contexto analisado: Gaming = {context.IsGamingMode}, Load = {context.SystemLoad}, Battery = {context.IsOnBattery}");

                return context;
            }
            catch (Exception ex)
            {
                _logger.LogError("[ContextEngine] Erro na análise de contexto", ex);
                return GetDefaultContext();
            }
        }

        /// <summary>
        /// Obtém contexto atual do sistema
        /// </summary>
        public async Task<SystemContext> GetCurrentContextAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var context = new SystemContext { Timestamp = DateTime.UtcNow };

                    context.IsGamingMode = DetectGamingMode();
                    context.IsOnBattery = DetectBatteryStatus();
                    context.SystemLoad = AnalyzeSystemLoad();
                    context.TimeOfDay = DateTime.UtcNow.Hour;
                    context.DayOfWeek = DateTime.UtcNow.DayOfWeek;
                    context.ActiveProcesses = GetActiveProcesses();
                    context.ResourceUsage = AnalyzeResourceUsage();

                    _logger.LogDebug($"[ContextEngine] Contexto atual: {context}");

                    return context;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[ContextEngine] Erro ao obter contexto atual: {ex.Message}");
                    return GetDefaultContext();
                }
            });
        }

        #region Detecção de Contexto

        private bool DetectGamingMode()
        {
            try
            {
                var processes = System.Diagnostics.Process.GetProcesses();
                var gameProcesses = new[]
                {
                    "steam", "epicgameslauncher", "origin", "uplay", "battle.net", "league of legends",
                    "valorant", "csgo", "fortnite", "minecraft", "overwatch", "apex", "pubg", "cod",
                    "fifa", "nba2k", "dota2", "wow", "gta5", "skyrim", "fallout"
                };

                return processes.Any(p => gameProcesses.Contains(p.ProcessName.ToLowerInvariant()));
            }
            catch
            {
                return false;
            }
        }

        private bool DetectBatteryStatus()
        {
            try
            {
                var powerStatus = System.Windows.Forms.SystemInformation.PowerStatus.BatteryChargeStatus != System.Windows.Forms.BatteryChargeStatus.NoSystemBattery;
                return powerStatus;
            }
            catch
            {
                return false;
            }
        }

        private SystemLoad AnalyzeSystemLoad()
        {
            try
            {
                var cpuCounter = new VoltrisOptimizer.Utils.SafePerformanceCounter("Processor", "% Processor Time", "_Total");
                var memoryCounter = new VoltrisOptimizer.Utils.SafePerformanceCounter("Memory", "Available MBytes");

                cpuCounter.NextValue();
                memoryCounter.NextValue();
                System.Threading.Thread.Sleep(1000);

                var cpuUsage = cpuCounter.NextValue();
                var totalMemory = GC.GetTotalMemory(false) / (1024 * 1024);
                var availableMemory = memoryCounter.NextValue();
                var memoryUsage = totalMemory > 0 ? ((totalMemory - availableMemory) / totalMemory) * 100 : 0;

                cpuCounter.Dispose();
                memoryCounter.Dispose();

                var maxLoad = Math.Max(cpuUsage, memoryUsage);

                if (maxLoad > 90) return SystemLoad.Critical;
                if (maxLoad > 75) return SystemLoad.High;
                if (maxLoad > 50) return SystemLoad.Normal;
                return SystemLoad.Low;
            }
            catch
            {
                return SystemLoad.Normal;
            }
        }

        private List<string> GetActiveProcesses()
        {
            try
            {
                var processes = System.Diagnostics.Process.GetProcesses()
                    .Where(p => !string.IsNullOrEmpty(p.ProcessName))
                    .Select(p => p.ProcessName.ToLowerInvariant())
                    .Distinct()
                    .Take(20)
                    .ToList();

                return processes;
            }
            catch
            {
                return new List<string>();
            }
        }

        private ResourceUsage AnalyzeResourceUsage()
        {
            var usage = new ResourceUsage();

            try
            {
                // CPU Usage
                var cpuCounter = new VoltrisOptimizer.Utils.SafePerformanceCounter("Processor", "% Processor Time", "_Total");
                cpuCounter.NextValue();
                System.Threading.Thread.Sleep(500);
                usage.CpuUsage = cpuCounter.NextValue();
                cpuCounter.Dispose();

                // Memory Usage
                var totalMemory = GC.GetTotalMemory(false);
                usage.MemoryUsage = totalMemory / (1024 * 1024);
                usage.MemoryUsagePercent = (totalMemory / (double)Environment.WorkingSet) * 100;

                // Disk Usage
                var drive = new System.IO.DriveInfo(System.IO.Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
                if (drive.IsReady)
                {
                    usage.DiskUsagePercent = ((double)(drive.TotalSize - drive.AvailableFreeSpace) / drive.TotalSize) * 100;
                }

                // Network Usage (simplificado)
                usage.NetworkActive = IsNetworkActive();

                // GPU Usage (simplificado - requer APIs específicas)
                usage.GpuUsage = DetectGpuUsage();
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ContextEngine] Erro na análise de recursãos: {ex.Message}");
            }

            return usage;
        }

        private bool IsNetworkActive()
        {
            try
            {
                using var ping = new System.Net.NetworkInformation.Ping();
                var reply = ping.Send("8.8.8.8", 100);
                return reply.Status == System.Net.NetworkInformation.IPStatus.Success;
            }
            catch
            {
                return false;
            }
        }

        private double DetectGpuUsage()
        {
            // Implementação simplificada - na pr�tica usaria APIs específicas da GPU
            try
            {
                // Tentar obter via WMI se disponível
                using var searcher = new ManagementObjectSearcher("select * from Win32_VideoController");
                foreach (ManagementObject obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    // Informações básicas da GPU
                    var adapterRAM = Convert.ToInt64(obj["AdapterRAM"] ?? 0);
                    return adapterRAM > 0 ? 25.0 : 0.0; // Estimativa simplificada
                }
            }
            catch
            {
                // Ignorar erros
            }
            return 0.0;
        }

        #endregion

        private SystemContext EnrichContextFromInput(SystemContext context, ProcessedInput processedInput)
        {
            // Enriquecer contexto baseado nas entidades extra�das
            if (processedInput.Entities.Contains("gaming") || processedInput.Entities.Contains("jogos"))
            {
                context.IsGamingMode = true;
                context.RequestedGamingMode = true;
            }

            if (processedInput.Entities.Contains("bateria"))
            {
                context.IsOnBattery = true;
                context.RequestedBatteryOptimization = true;
            }

            // Enriquecer baseado nos parâmetros
            if (processedInput.Parameters.ContainsKey("gaming") && processedInput.Parameters["gaming"] is bool gaming && gaming)
            {
                context.IsGamingMode = true;
                context.RequestedGamingMode = true;
            }

            if (processedInput.Parameters.ContainsKey("battery") && processedInput.Parameters["battery"] is bool battery && battery)
            {
                context.IsOnBattery = true;
                context.RequestedBatteryOptimization = true;
            }

            // Enriquecer baseado na intenção
            switch (processedInput.Intent)
            {
                case CommandIntent.OptimizeForGaming:
                    context.IsGamingMode = true;
                    context.RequestedGamingMode = true;
                    break;

                case CommandIntent.ManagePower:
                    context.RequestedBatteryOptimization = true;
                    break;

                case CommandIntent.OptimizeLatency:
                    context.RequestedLatencyOptimization = true;
                    break;
            }

            return context;
        }

        private SystemContext GetDefaultContext()
        {
            return new SystemContext
            {
                Timestamp = DateTime.UtcNow,
                IsGamingMode = false,
                IsOnBattery = false,
                SystemLoad = SystemLoad.Normal,
                TimeOfDay = DateTime.UtcNow.Hour,
                DayOfWeek = DateTime.UtcNow.DayOfWeek,
                ActiveProcesses = new List<string>(),
                ResourceUsage = new ResourceUsage()
            };
        }
    }

    #region Classes de Suporte

    public class SystemContext
    {
        public DateTime Timestamp { get; set; }
        public bool IsGamingMode { get; set; }
        public bool IsOnBattery { get; set; }
        public SystemLoad SystemLoad { get; set; }
        public int TimeOfDay { get; set; }
        public DayOfWeek DayOfWeek { get; set; }
        public List<string> ActiveProcesses { get; set; } = new();
        public ResourceUsage ResourceUsage { get; set; } = new();

        // Flags de requisi��es expl�citas
        public bool RequestedGamingMode { get; set; }
        public bool RequestedBatteryOptimization { get; set; }
        public bool RequestedLatencyOptimization { get; set; }

        public override string ToString()
        {
            return $"Contexto: Gaming = {IsGamingMode}, Battery = {IsOnBattery}, Load = {SystemLoad}, Time = {TimeOfDay}h";
        }
    }

    public class ResourceUsage
    {
        public double CpuUsage { get; set; } // Percentual
        public double MemoryUsage { get; set; } // MB
        public double MemoryUsagePercent { get; set; } // Percentual
        public double DiskUsagePercent { get; set; } // Percentual
        public double GpuUsage { get; set; } // Percentual (estáimado)
        public bool NetworkActive { get; set; }

        public override string ToString()
        {
            return $"CPU: {CpuUsage:F1}%, RAM: {MemoryUsage:F0} MB ({MemoryUsagePercent:F1}%), Disco: {DiskUsagePercent:F1}%, GPU: {GpuUsage:F1}%, Rede: {(NetworkActive ? "Ativa" : "Inativa")}";
        }
    }

    #endregion
}
 


