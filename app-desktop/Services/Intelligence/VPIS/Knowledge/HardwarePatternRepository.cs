using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace VoltrisOptimizer.Services.Intelligence.VPIS.Knowledge
{
    public class HardwarePatternRepository
    {
        // Mocking the database representation
        private readonly ConcurrentDictionary<string, HardwarePatternStats> _globalStats = new ConcurrentDictionary<string, HardwarePatternStats>();

        public void RegisterSessionResult(string hardwareSku, string mainProblemTitle)
        {
            _globalStats.AddOrUpdate(hardwareSku, 
                // Add
                _ => new HardwarePatternStats { TotalSessions = 1, ProblemCounts = new Dictionary<string, int> { { mainProblemTitle, 1 } } },
                // Update
                (_, stats) => 
                {
                    stats.TotalSessions++;
                    if (stats.ProblemCounts.ContainsKey(mainProblemTitle))
                        stats.ProblemCounts[mainProblemTitle]++;
                    else
                        stats.ProblemCounts[mainProblemTitle] = 1;
                    return stats;
                });
        }

        public string GetMostCommonProblem(string hardwareSku)
        {
            if (_globalStats.TryGetValue(hardwareSku, out var stats))
            {
                var mostCommon = string.Empty;
                var maxCount = 0;

                foreach (var kvp in stats.ProblemCounts)
                {
                    if (kvp.Value > maxCount && kvp.Key != "Nenhum gargalo detectado")
                    {
                        maxCount = kvp.Value;
                        mostCommon = kvp.Key;
                    }
                }

                if (maxCount > 0)
                {
                    double percentage = ((double)maxCount / stats.TotalSessions) * 100;
                    return $"{mostCommon} ({percentage:F0}%)";
                }
            }
            return "Dados insuficientes";
        }
    }

    public class HardwarePatternStats
    {
        public int TotalSessions { get; set; }
        public Dictionary<string, int> ProblemCounts { get; set; } = new Dictionary<string, int>();
    }
}
