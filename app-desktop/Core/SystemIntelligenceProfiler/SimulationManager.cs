using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Core.SystemIntelligenceProfiler
{
    public class SimulationManager
    {
        public Task<SimulationResult> SimulateAsync(IEnumerable<ActionRecommendation> actions)
        {
            App.LoggingService?.LogInfo($"[SIMULADOR] Iniciando simulação para {actions.Count()} ações recomendadas");
            var list = actions.Where(a => a.Supported).ToList();
            var gain = list.Sum(a => a.ExpectedGainScore);
            var risk = list.Sum(a => a.RiskScore);
            
            App.LoggingService?.LogSuccess($"[SIMULADOR] Simulação concluída. Total: {list.Count} ações seguras. Ganho agregado esperado: {gain}");
            return Task.FromResult(new SimulationResult
            {
                TotalActions = list.Count,
                AggregateExpectedGain = gain,
                AggregateRiskScore = risk});
        }
    }

    public class SimulationResult
    {
        public int TotalActions { get; set; }
        public int AggregateExpectedGain { get; set; }
        public int AggregateRiskScore { get; set; }
    }
}

