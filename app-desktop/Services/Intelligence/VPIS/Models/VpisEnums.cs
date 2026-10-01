using System;

namespace VoltrisOptimizer.Services.Intelligence.VPIS.Models
{
    public enum EventCategory
    {
        Thermal,
        CPU,
        GPU,
        VRAM,
        RAM,
        Storage,
        Network,
        System,
        Unknown
    }

    public enum DiagnosticSeverity
    {
        Info,
        Warning,
        Critical
    }

    public enum ConfidenceLevel
    {
        Low,
        Medium,
        High
    }
}
