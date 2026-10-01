using VoltrisOptimizer.Core.Body;

namespace VoltrisOptimizer.Core.NetworkIntelligence;

public sealed class SystemContextSnapshot
{
    public double CpuPercent { get; init; }

    public double RamPercent { get; init; }

    public double GpuPercent { get; init; }

    public double CpuTempCelsius { get; init; }

    public string ForegroundProcess { get; init; } = string.Empty;

    public OperationalContext CurrentContext { get; init; } = OperationalContext.Idle;

    public int ActiveNicSpeedMbps { get; init; }

    public string ConnectionType { get; init; } = "desconhecido";

    public bool IsOnBattery { get; init; }

    public bool IsGamingModeActive { get; init; }

    public string ToSummary()
    {
        return $"CPU={CpuPercent:F1}% RAM={RamPercent:F1}% GPU={GpuPercent:F1}% TEMP={CpuTempCelsius:F0}C " +
               $"CTX={CurrentContext} FG={ForegroundProcess} NIC={ActiveNicSpeedMbps}Mbps " +
               $"BAT={IsOnBattery} GM={IsGamingModeActive}";
    }
}
