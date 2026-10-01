using System;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Core.Body;

public interface IProcessArm : IDisposable
{
	Task<ProcessPriorityResult> SetProcessPriorityAsync(int pid, string name, ProcessPriorityClass priority);

	Task<bool> SetEcoQoSAsync(int pid, string name, bool throttle);

	Task<TrimResult> TrimWorkingSetAsync(int[]? pids = null);

	Task<bool> SetCpuAffinityAsync(int pid, string name, long mask);
}
