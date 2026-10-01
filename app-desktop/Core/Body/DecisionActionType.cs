namespace VoltrisOptimizer.Core.Body;

public enum DecisionActionType
{
	None = 0,
	SetEpp = 10,
	SetPowerPlan = 11,
	SetSystemResponsiveness = 12,
	SetTurboBoostPolicy = 13,
	SetCoreParking = 14,
	SetProcessPriority = 20,
	SetEcoQoS = 21,
	TrimWorkingSet = 22,
	SetCpuAffinity = 23,
	EnableGamingMode = 30,
	SetTimerResolution = 31,
	EnableMmcssGaming = 32,
	FlushDnsCache = 33,
	TrimStandbyList = 34,
	ApplyGpuGamingProfile = 40,
	SetGpuLowLatency = 41,
	EnableHags = 42,
	OptimizeNetworkForGaming = 50,
	SetNetworkInterruptAffinity = 51,
	RestoreNetworkDefaults = 52,
	ApplyGamingProfile = 100,
	ApplyWorkProfile = 101,
	ApplyBatterySaver = 102,
	ApplyThermalEmergency = 103
}
