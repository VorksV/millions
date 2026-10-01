namespace VoltrisOptimizer.Services.Thermal.Models
{
    /// <summary>
    /// Contexto atual do sistema para análise inteligente
    /// </summary>
    public class SystemContext
    {
        public bool IsGameRunning { get; set; }
        public bool HasHeavyWorkload { get; set; }
        public bool IsIdle { get; set; }
        public IntelligentProfileType ActiveProfile { get; set; }
        public string[] RunningProcesses { get; set; } = System.Array.Empty<string>();
    }
}
