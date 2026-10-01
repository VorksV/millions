namespace VoltrisOptimizer.Services.Telemetry
{
    /// <summary>
    /// HardwareProfiler — Telemetria de perfis de hardware remoto removida.
    /// Mantém apenas a declaração das classes para evitar erros de compilação
    /// caso alguma outra parte residual espere esse namespace.
    /// </summary>
    public class HardwareProfiler
    {
        public class DeviceProfile
        {
            public string Hostname { get; set; } = "Unknown";
            public string CpuModel { get; set; } = "Unknown CPU";
            public string GpuModel { get; set; } = "Unknown GPU";
            public double RamTotalGb { get; set; }
            public string DiskType { get; set; } = "Unknown";
            public string OsVersion { get; set; } = "Windows";
            public string WindowsBuild { get; set; } = "Unknown Build";
            public bool Is64Bit { get; set; }
            public bool IsAdmin { get; set; }
            public string ScreenResolution { get; set; } = "Unknown";
        }

        public class SystemVitals
        {
            public double CpuUsagePercent { get; set; }
            public double RamUsagePercent { get; set; }
            public double DiskUsagePercent { get; set; }
            public double FreeDiskSpaceGb { get; set; }
            public int ProcessCount { get; set; }
            public string Uptime { get; set; } = "0h 0m";
            public double HealthScore { get; set; } = 100;
        }

        public DeviceProfile GetProfile() => new DeviceProfile();
        public SystemVitals GetVitals() => new SystemVitals();
    }
}
