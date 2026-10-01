namespace VoltrisOptimizer.Services.Thermal.Models
{
    /// <summary>
    /// Thresholds térmicos baseados no Perfil Inteligente
    /// </summary>
    public class ThermalThresholds
    {
        public double CpuWarningThreshold { get; set; }
        public double CpuCriticalThreshold { get; set; }
        public double GpuWarningThreshold { get; set; }
        public double GpuCriticalThreshold { get; set; }

        public static ThermalThresholds GetForProfile(IntelligentProfileType profile)
        {
            return profile switch
            {
                IntelligentProfileType.GamerCompetitive => new ThermalThresholds
                {
                    CpuWarningThreshold = 85,
                    CpuCriticalThreshold = 95,
                    GpuWarningThreshold = 88,
                    GpuCriticalThreshold = 97
                },
                IntelligentProfileType.GamerSimulation => new ThermalThresholds
                {
                    CpuWarningThreshold = 85,
                    CpuCriticalThreshold = 95,
                    GpuWarningThreshold = 88,
                    GpuCriticalThreshold = 97
                },
                IntelligentProfileType.GamerMMO => new ThermalThresholds
                {
                    CpuWarningThreshold = 85,
                    CpuCriticalThreshold = 95,
                    GpuWarningThreshold = 88,
                    GpuCriticalThreshold = 97
                },
                IntelligentProfileType.GamerStrategy => new ThermalThresholds
                {
                    CpuWarningThreshold = 85,
                    CpuCriticalThreshold = 95,
                    GpuWarningThreshold = 86,
                    GpuCriticalThreshold = 95
                },
                IntelligentProfileType.GamerSinglePlayer => new ThermalThresholds
                {
                    CpuWarningThreshold = 85,
                    CpuCriticalThreshold = 95,
                    GpuWarningThreshold = 86,
                    GpuCriticalThreshold = 95
                },
                IntelligentProfileType.WorkOffice => new ThermalThresholds
                {
                    CpuWarningThreshold = 70,
                    CpuCriticalThreshold = 80,
                    GpuWarningThreshold = 75,
                    GpuCriticalThreshold = 85
                },
                IntelligentProfileType.CreativeVideoEditing => new ThermalThresholds
                {
                    CpuWarningThreshold = 75,
                    CpuCriticalThreshold = 85,
                    GpuWarningThreshold = 80,
                    GpuCriticalThreshold = 90
                },
                IntelligentProfileType.DeveloperProgramming => new ThermalThresholds
                {
                    CpuWarningThreshold = 72,
                    CpuCriticalThreshold = 82,
                    GpuWarningThreshold = 77,
                    GpuCriticalThreshold = 87
                },
                IntelligentProfileType.EnterpriseSecure => new ThermalThresholds
                {
                    CpuWarningThreshold = 68,
                    CpuCriticalThreshold = 78,
                    GpuWarningThreshold = 73,
                    GpuCriticalThreshold = 83
                },
                _ => new ThermalThresholds // GeneralBalanced
                {
                    CpuWarningThreshold = 75,
                    CpuCriticalThreshold = 85,
                    GpuWarningThreshold = 80,
                    GpuCriticalThreshold = 90
                }
            };
        }
    }
}
