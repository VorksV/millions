using System;

namespace VoltrisOptimizer.Services.Performance.CpuTuning.Core.Models
{
    // CpuVendor é reutilizado de VoltrisOptimizer.Services.Performance.CpuTuning.Models.CpuVendor
    // Importamos via alias para manter a arquitetura limpa sem duplicação

    public enum CpuGeneration
    {
        Unknown,
        IntelLegacy,
        IntelModern,
        AMDRyzen
    }

    public enum BackendStatus
    {
        NotInitialized,
        Ready,
        DllNotFound,
        DriverNotLoaded,
        AccessDenied,
        UnsupportedOS,
        SecurityBlocked
    }

    public struct PowerLimitState
    {
        public int Pl1Watts;
        public int Pl2Watts;
        public int TimeWindowSeconds;
        public bool IsLocked;
    }

    public struct PowerLimitRequest
    {
        public int Pl1Watts;
        public int Pl2Watts;
        public int TimeWindowSeconds;
        public bool ForceUnlock;
    }
}
