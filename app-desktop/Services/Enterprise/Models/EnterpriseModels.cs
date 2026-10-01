using System;
using System.Collections.Generic;

namespace VoltrisOptimizer.Services.Enterprise.Models
{
    // Modelos para comandos remotos
    public class RemoteCommandModel
    {
        public string id { get; set; } = string.Empty;
        public string command_type { get; set; } = string.Empty;
        public object payload { get; set; } = new { };
        public string status { get; set; } = string.Empty;
        public DateTime created_at { get; set; }
    }

    public class RemoteCommandResponseModel
    {
        public List<RemoteCommandModel> commands { get; set; } = new List<RemoteCommandModel>();
    }

    public class LicenseStatusResponseModel
    {
        public bool IsValid { get; set; }
        public string Plan { get; set; } = string.Empty;
        public DateTime ExpiryDate { get; set; }
    }

    // Modelos para registro de dispositivo
    public class RegisterPayloadModel
    {
        public MachineIdentityModel Identity { get; set; } = new MachineIdentityModel();
        public string AppVersion { get; set; } = string.Empty;
    }

    public class MachineIdentityModel
    {
        public string MachineId { get; set; } = string.Empty;
        public string Hostname { get; set; } = string.Empty;
        public string OsVersion { get; set; } = string.Empty;
        public string Architecture { get; set; } = string.Empty;
    }

    // Modelos para telemetria
    public class HeartbeatPayloadModel
    {
        public string MachineId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public SystemMetricsModel Metrics { get; set; } = new SystemMetricsModel();
        public List<ActiveAlertModel> ActiveAlerts { get; set; } = new List<ActiveAlertModel>();
    }

    public class SystemMetricsModel
    {
        public double CpuUsagePercent { get; set; }
        public double RamUsagePercent { get; set; }
        public double DiskUsagePercent { get; set; }
        public long AvailableRamMb { get; set; }
        public long TotalRamMb { get; set; }
    }

    public class ActiveAlertModel
    {
        public string Type { get; set; } = string.Empty;
        public string Level { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }
}
