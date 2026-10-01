using System;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Optimization.Unification
{
    public class IntelligentDecisionEngine
    {
        private readonly ILoggingService _logger;

        public IntelligentDecisionEngine(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public OptimizationMode GetOptimizationMode()
        {
            var settings = SettingsService.Instance.Settings;
            return settings.IntelligentProfile switch
            {
                IntelligentProfileType.GamerCompetitive => OptimizationMode.Aggressive,
                IntelligentProfileType.GamerSinglePlayer => OptimizationMode.Balanced,
                IntelligentProfileType.WorkOffice => OptimizationMode.Conservative,
                IntelligentProfileType.CreativeVideoEditing => OptimizationMode.Balanced,
                IntelligentProfileType.DeveloperProgramming => OptimizationMode.Balanced,
                IntelligentProfileType.GeneralBalanced => OptimizationMode.Balanced,
                IntelligentProfileType.EnterpriseSecure => OptimizationMode.Conservative,
                _ => OptimizationMode.Balanced
            };
        }

        public IntelligentProfileType GetCurrentProfile()
        {
            return SettingsService.Instance.Settings.IntelligentProfile;
        }

        public bool ShouldEffectBurst(int pid, string processName, int priority)
        {
             var profile = GetCurrentProfile();
             if (profile == IntelligentProfileType.EnterpriseSecure) return false;
             
             // Por enquanto permitimos para os outros perfis
             return true;
        }
    }

    public enum OptimizationMode
    {
        Conservative,
        Balanced,
        Aggressive
    }
}
