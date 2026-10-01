using System.Threading.Tasks;

namespace VoltrisOptimizer.Core;

public static class SystemMetricsCacheExtensions
{
	public static async Task<HardwareSummary> GetHardwareAsync(this SystemMetricsCache cache)
	{
		return await Task.Run(() => new HardwareSummary());
	}
}
