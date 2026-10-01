namespace VoltrisOptimizer.Core.Intelligence;

public interface IFpsMonitor
{
	void StartMonitoring();

	void StopMonitoring();

	float GetCurrentFps();

	float GetAverageFps();

	float Get1PercentLowFps();

	bool HasSignificantVariance();
}
