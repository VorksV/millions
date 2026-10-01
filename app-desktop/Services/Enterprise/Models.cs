using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VoltrisOptimizer.Services.Enterprise.Models
{
    // --- Modelos de Identidade ---

    public class MachineIdentity
    {
        [JsonPropertyName("machine_id")]
        public string MachineId { get; set; } = string.Empty;

        [JsonPropertyName("hostname")]
        public string Hostname { get; set; } = string.Empty;

        [JsonPropertyName("os_version")]
        public string OsVersion { get; set; } = string.Empty;

        [JsonPropertyName("cpu_model")]
        public string CpuModel { get; set; } = string.Empty;

        [JsonPropertyName("ram_total_gb")]
        public int RamTotalGb { get; set; }

        [JsonPropertyName("disk_serial")]
        public string DiskSerial { get; set; } = string.Empty;
        
        [JsonPropertyName("mac_address")]
        public string MacAddress { get; set; } = string.Empty;

        [JsonPropertyName("architecture")]
        public string Architecture { get; set; } = string.Empty;
    }

    // --- Modelos de Comunicação ---

    public class RegisterPayload
    {
        [JsonPropertyName("identity")]
        public MachineIdentity Identity { get; set; } = new();

        [JsonPropertyName("app_version")]
        public string AppVersion { get; set; } = string.Empty;
    }

    public class HeartbeatPayload
    {
        [JsonPropertyName("machine_id")]
        public string MachineId { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = "OK"; // OK, WARNING, CRITICAL

        [JsonPropertyName("metrics")]
        public SystemMetrics Metrics { get; set; } = new();

        [JsonPropertyName("active_alerts")]
        public List<AlertEvent> ActiveAlerts { get; set; } = new();

        [JsonPropertyName("governance_actions")]
        public int GovernanceActions { get; set; }

        [JsonPropertyName("stutter_incidents")]
        public int StutterIncidents { get; set; }

        [JsonPropertyName("health_score")]
        public int HealthScore { get; set; }
    }

    public class SystemMetrics
    {
        [JsonPropertyName("cpu_usage")]
        public double CpuUsage { get; set; }

        [JsonPropertyName("ram_usage_percent")]
        public double RamUsagePercent { get; set; }

        [JsonPropertyName("disk_usage_percent")]
        public double DiskUsagePercent { get; set; }
        
        [JsonPropertyName("last_boot_time_seconds")]
        public double LastBootTimeSeconds { get; set; }
    }

    public class AlertEvent
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty; // RAM_HIGH, DISK_FULL, SLOW_BOOT

        [JsonPropertyName("level")]
        public string Level { get; set; } = "WARNING"; // WARNING, CRITICAL

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }
    }

    // --- Modelos Enterprise Intelligence ---

    public class PerformanceMetric
    {
        public string Name { get; set; } = string.Empty;
        public double Value { get; set; }
        public string Unit { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public double? Threshold { get; set; }
        public bool IsAnomalous { get; set; }
    }

    public class AnomalyDetection
    {
        public string Id { get; set; } = string.Empty;
        public string MetricName { get; set; } = string.Empty;
        public AnomalySeverity Severity { get; set; }
        public string Description { get; set; } = string.Empty;
        public double ActualValue { get; set; }
        public double ExpectedValue { get; set; }
        public double Confidence { get; set; }
        public DateTime DetectedAt { get; set; }
        public bool IsResolved { get; set; }
    }

    public enum AnomalySeverity
    {
        Low,
        Medium,
        High,
        Critical
    }

    public class SystemHealthScore
    {
        public double Overall { get; set; }
        public double CPU { get; set; }
        public double Memory { get; set; }
        public double Disk { get; set; }
        public double Network { get; set; }
        public double GPU { get; set; }
        public int ActiveAnomalies { get; set; }
        public DateTime LastCalculated { get; set; }
        public string Status { get; set; } = string.Empty;
        public List<string> Recommendations { get; set; } = new();
    }

    public class OptimizationRecommendation
    {
        public string Id { get; set; } = string.Empty;
        public OptimizationType Type { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public double ImpactScore { get; set; }
        public string TargetMetric { get; set; } = string.Empty;
        public Dictionary<string, object> Parameters { get; set; } = new();
        public bool IsAutoExecutable { get; set; }
        public int Priority { get; set; }
    }

    public enum OptimizationType
    {
        CPU_Optimization,
        Memory_Cleanup,
        Disk_Defragmentation,
        Network_Optimization,
        GPU_Tuning,
        Process_Priority,
        Power_Plan,
        Registry_Optimization,
        Service_Management,
        Game_Mode
    }

    public class AutoCorrectionAction
    {
        public string Id { get; set; } = string.Empty;
        public OptimizationType Type { get; set; }
        public string Description { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string Result { get; set; } = string.Empty;
        public DateTime ExecutedAt { get; set; }
        public TimeSpan ExecutionTime { get; set; }
        public Dictionary<string, object> Metrics { get; set; } = new();
    }

    public class AutoCorrectionResult
    {
        public bool Success { get; set; }
        public AutoCorrectionAction Action { get; set; } = new();
        public string ErrorMessage { get; set; } = string.Empty;
    }

    // --- Modelos de Resposta da API ---

    public class LicenseStatusResponse
    {
        [JsonPropertyName("valid")]
        public bool IsValid { get; set; }

        [JsonPropertyName("plan")]
        public string Plan { get; set; } = "Standard"; // Standard, Pro, Enterprise

        [JsonPropertyName("expiration")]
        public DateTime? ExpirationDate { get; set; }
        
        [JsonPropertyName("company_name")]
        public string? CompanyName { get; set; }
    }
}
