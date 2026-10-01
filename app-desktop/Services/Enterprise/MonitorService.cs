using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.Enterprise.Models;

namespace VoltrisOptimizer.Services.Enterprise
{
    public class MonitorService
    {
        private readonly ISystemInfoService _systemInfoService;
        private readonly List<AlertEvent> _activeAlerts = new();

        // Limites para alertas
        private const double RAM_CRITICAL_PERCENT = 90.0;
        private const double RAM_WARNING_PERCENT = 80.0;
        private const double DISK_CRITICAL_PERCENT = 95.0; // Espaço usado

        public MonitorService(ISystemInfoService systemInfoService)
        {
            _systemInfoService = systemInfoService;
        }

        public async Task<SystemMetrics> CollectMetricsAsync()
        {
            var cpuUsage = await _systemInfoService.GetCpuUsageAsync();
            var ramUsage = await _systemInfoService.GetMemoryUsageAsync();
            
            // Calcular uso de disco médio (do drive C principal)
            double diskUsage = 0;
            var drives = await _systemInfoService.GetDrivesInfoAsync();
            if (drives != null && drives.Length > 0)
            {
                var mainDrive = drives[0]; // Assume C: primeiro
                if (mainDrive.TotalBytes > 0)
                {
                    double used = mainDrive.TotalBytes - mainDrive.FreeBytes;
                    diskUsage = (used / mainDrive.TotalBytes) * 100.0;
                }
            }

            // Simular tempo de boot (em app real usaria 'Environment.TickCount64')
            double bootTime = Environment.TickCount64 / 1000.0;

            return new SystemMetrics
            {
                CpuUsage = Math.Round(cpuUsage, 1),
                RamUsagePercent = Math.Round(ramUsage, 1),
                DiskUsagePercent = Math.Round(diskUsage, 1),
                LastBootTimeSeconds = bootTime
            };
        }

        public async Task<List<AlertEvent>> CheckForAlertsAsync(SystemMetrics metrics)
        {
            _activeAlerts.Clear();

            // Check RAM
            if (metrics.RamUsagePercent >= RAM_CRITICAL_PERCENT)
            {
                _activeAlerts.Add(new AlertEvent 
                { 
                    Type = "RAM_HIGH", 
                    Level = "CRITICAL", 
                    Message = $"Uso de RAM crítico: {metrics.RamUsagePercent}%",
                    Timestamp = DateTime.UtcNow 
                });
            }
            else if (metrics.RamUsagePercent >= RAM_WARNING_PERCENT)
            {
                _activeAlerts.Add(new AlertEvent 
                { 
                    Type = "RAM_HIGH", 
                    Level = "WARNING", 
                    Message = $"Uso de RAM elevado: {metrics.RamUsagePercent}%",
                    Timestamp = DateTime.UtcNow 
                });
            }

            // Check Disk
            if (metrics.DiskUsagePercent >= DISK_CRITICAL_PERCENT)
            {
                _activeAlerts.Add(new AlertEvent
                {
                    Type = "DISK_FULL",
                    Level = "CRITICAL",
                    Message = $"Disco quase cheio: {metrics.DiskUsagePercent}% usado",
                    Timestamp = DateTime.UtcNow
                });
            }

            // Check Boot (exemplo simples: se uptime < 5 min e cpu alta demais, pode ser boot lento sendo processado)
            // Lógica real exigiria persistência do tempo de boot anterior

            return new List<AlertEvent>(_activeAlerts);
        }
    }
}
