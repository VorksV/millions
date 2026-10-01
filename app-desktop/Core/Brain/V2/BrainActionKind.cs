namespace VoltrisOptimizer.Core.Brain.V2;

public enum BrainActionKind
{
	NoAction,
	SetEpp,
	SetForegroundPriority,
	SetSystemResponsiveness,
	EnableGamingMode,
	TrimWorkingSet,
	LockMemoryHard,
	ForceTopologyPcores,
	SuspendJitterProcesses
}
