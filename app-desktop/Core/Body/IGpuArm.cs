using System;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Core.Body;

public interface IGpuArm : IDisposable
{
	GpuVendor DetectedVendor { get; }

	string GpuName { get; }

	Task<ArmActionResult> ApplyGamingProfileAsync(string processName);

	Task<ArmActionResult> SetLowLatencyModeAsync(bool enable);

	Task<ArmActionResult> EnableHagsAsync();

	Task<ArmActionResult> RestoreGpuDefaultsAsync();
}
