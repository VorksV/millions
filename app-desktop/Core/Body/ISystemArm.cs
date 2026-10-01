using System;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Core.Body;

public interface ISystemArm : IDisposable
{
	Task<SystemActionResult> EnableGamingModeAsync(bool enable);

	Task<bool> SetTimerResolutionAsync(uint ticks);

	Task<bool> RestoreTimerResolutionAsync();

	Task<bool> EnableMmcssGamingAsync(bool enable);

	Task<bool> FlushDnsCacheAsync();

	Task<long> TrimStandbyListAsync();

	// NOVOS: P1 - Otimizações seguras com rollback
	Task<SystemActionResult> SetRegistryValueAsync(string keyPath, string valueName, object value, Microsoft.Win32.RegistryValueKind kind, bool requireAdmin = true, string? rollbackDescription = null);

	Task<SystemActionResult> SetVisualEffectsAsync(bool bestPerformance);

	Task<SystemActionResult> SetGameDvrAsync(bool enable);

	Task<SystemActionResult> SetHagsAsync(bool enable);
}
