using System;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services
{
    /// <summary>
    /// Serviço para obter informações reais do sistema
    /// </summary>
    public class SystemInfoService
    {
        /// <summary>
        /// Obtém informações do sistema operacional
        /// </summary>
        public static string GetOSVersion()
        {
            App.LoggingService?.LogTrace("[SystemInfo] Coletando versão do Sistema Operacional...");
            try
            {
                var hub = (VoltrisOptimizer.Services.Telemetry.ICentralTelemetryHub?)App.Services?.GetService(typeof(VoltrisOptimizer.Services.Telemetry.ICentralTelemetryHub));
                if (hub != null && !string.IsNullOrEmpty(hub.OsCaption))
                {
                    string name = hub.OsCaption.Replace("Microsoft ", "").Trim();
                    string archStr = Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit";
                    return $"{name} ({archStr})";
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[SystemInfo] Falha ao consultar hub para OS Version: {ex.Message}. Usando fallback.");
            }
            
            // Fallback para método simples
            string archFallback = Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit";
            return $"{Environment.OSVersion} ({archFallback})";
        }

        /// <summary>
        /// Verifica se o sistema operacional atual é o Windows 11
        /// </summary>
        public static bool IsWindows11
        {
            get
            {
                try
                {
                    var hub = (VoltrisOptimizer.Services.Telemetry.ICentralTelemetryHub?)App.Services?.GetService(typeof(VoltrisOptimizer.Services.Telemetry.ICentralTelemetryHub));
                    if (hub != null && !string.IsNullOrEmpty(hub.OsCaption))
                    {
                        return hub.OsCaption.Contains("Windows 11", StringComparison.OrdinalIgnoreCase);
                    }
                }
                catch { }
                return Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= 22000;
            }
        }

        /// <summary>
        /// Obtém nome do processador
        /// </summary>
        public static string GetProcessorName()
        {
            try
            {
                var hub = (VoltrisOptimizer.Services.Telemetry.ICentralTelemetryHub?)App.Services?.GetService(typeof(VoltrisOptimizer.Services.Telemetry.ICentralTelemetryHub));
                if (hub != null && !string.IsNullOrEmpty(hub.CpuName))
                {
                    return hub.CpuName;
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[SystemInfo] Falha ao consultar nome do processador: {ex.Message}");
            }
            
            return "Processador Desconhecido";
        }

        /// <summary>
        /// Obtém quantidade total de RAM instalada
        /// </summary>
        public static long GetTotalRAMBytes()
        {
            try
            {
                var hub = (VoltrisOptimizer.Services.Telemetry.ICentralTelemetryHub?)App.Services?.GetService(typeof(VoltrisOptimizer.Services.Telemetry.ICentralTelemetryHub));
                if (hub != null && hub.TotalPhysicalMemory > 0)
                {
                    return (long)hub.TotalPhysicalMemory;
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[SystemInfo] Falha ao consultar Total RAM: {ex.Message}");
            }
            
            return 0;
        }

        /// <summary>
        /// Obtém RAM disponível em bytes
        /// </summary>
        public static long GetAvailableRAMBytes()
        {
            try
            {
                var hub = (VoltrisOptimizer.Services.Telemetry.ICentralTelemetryHub?)App.Services?.GetService(typeof(VoltrisOptimizer.Services.Telemetry.ICentralTelemetryHub));
                if (hub != null && hub.AvailablePhysicalMemory > 0)
                {
                    return (long)hub.AvailablePhysicalMemory;
                }
            }
            catch { }
            
            return 0;
        }

        /// <summary>
        /// Obtém espaço livre do disco principal em bytes
        /// </summary>
        public static long GetFreeDiskSpaceBytes()
        {
            try
            {
                var root = Path.GetPathRoot(Environment.SystemDirectory);
                if (string.IsNullOrEmpty(root)) return 0;
                var drive = new DriveInfo(root);
                if (!drive.IsReady) return 0;
                return drive.AvailableFreeSpace;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Obtém espaço total do disco principal em bytes
        /// </summary>
        public static long GetTotalDiskSpaceBytes()
        {
            try
            {
                var root = Path.GetPathRoot(Environment.SystemDirectory);
                if (string.IsNullOrEmpty(root)) return 0;
                var drive = new DriveInfo(root);
                if (!drive.IsReady) return 0;
                return drive.TotalSize;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Obtém percentual de uso de CPU (média) - OTIMIZADO
        /// </summary>
        public static async Task<double> GetCPUUsagePercentAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var hub = (VoltrisOptimizer.Services.Telemetry.ICentralTelemetryHub?)App.Services?.GetService(typeof(VoltrisOptimizer.Services.Telemetry.ICentralTelemetryHub));
                    if (hub != null)
                    {
                        return hub.CpuUsagePercent;
                    }
                }
                catch { }
                return 0;
            });
        }

        /// <summary>
        /// Verifica status da conexão de rede
        /// </summary>
        public static bool IsNetworkConnected()
        {
            try
            {
                return NetworkInterface.GetIsNetworkAvailable();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Obtém nome da placa gráfica
        /// </summary>
        public static string GetGraphicsCardName()
        {
            try
            {
                var hub = (VoltrisOptimizer.Services.Telemetry.ICentralTelemetryHub?)App.Services?.GetService(typeof(VoltrisOptimizer.Services.Telemetry.ICentralTelemetryHub));
                if (hub != null && !string.IsNullOrEmpty(hub.GpuName))
                {
                    if (!hub.GpuName.Contains("Basic"))
                        return hub.GpuName;
                }
            }
            catch { }
            
            return "Placa Gráfica Desconhecida";
        }

        /// <summary>
        /// Verifica saúde geral do sistema
        /// </summary>
        public static SystemHealthStatus GetSystemHealthStatus()
        {
            try
            {
                // Verificar espaço em disco
                var totalDisk = GetTotalDiskSpaceBytes();
                var freeDisk = GetFreeDiskSpaceBytes();
                double diskFreePercent = totalDisk > 0 ? (double)freeDisk / totalDisk * 100 : 0;
                
                // Verificar RAM
                var totalRam = GetTotalRAMBytes();
                var availRam = GetAvailableRAMBytes();
                double ramUsagePercent = totalRam > 0 ? (1.0 - (double)availRam / totalRam) * 100 : 0;
                
                // Verificar conectividade
                bool networkOk = IsNetworkConnected();
                
                // Determinar status geral
                if (diskFreePercent < 10 || ramUsagePercent > 90)
                    return SystemHealthStatus.Critical;
                
                if (diskFreePercent < 20 || ramUsagePercent > 80 || !networkOk)
                    return SystemHealthStatus.Warning;
                
                return SystemHealthStatus.Good;
            }
            catch
            {
                return SystemHealthStatus.Unknown;
            }
        }

        /// <summary>
        /// Obtém espaço a ser liberado (arquivos temporários, lixeira, etc.)
        /// </summary>
        public static async Task<long> GetSpaceToCleanBytesAsync()
        {
            if (App.SystemCleaner == null)
                return 0;
            
            try
            {
                // Criar cancellation token com timeout de 30 segundos
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                return await App.SystemCleaner.CalculateTotalSizeAsync(
                    cleanTemp: true,
                    cleanRecycle: true,
                    cleanThumbnails: true,
                    cleanBrowsers: true,
                    cts.Token
                );
            }
            catch
            {
                return 0;
            }
        }
    }

    /// <summary>
    /// Enum para status de saúde do sistema
    /// </summary>
    public enum SystemHealthStatus
    {
        Good,
        Warning,
        Critical,
        Unknown
    }
}

