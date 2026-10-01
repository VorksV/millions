using System;
using System.Collections.Generic;
using System.Linq;
using VoltrisOptimizer.Services.Intelligence.VPIS.Models;

namespace VoltrisOptimizer.Services.Intelligence.VPIS.Engines
{
    public class RootCauseRankingEngine
    {
        public PerformanceInsightEvent DetermineMainProblem(IReadOnlyList<PerformanceInsightEvent> sessionEvents)
        {
            if (sessionEvents == null || !sessionEvents.Any())
                return null;

            var groupedEvents = sessionEvents.GroupBy(e => e.Category).ToList();

            var rankedCategories = groupedEvents.Select(group =>
            {
                var firstEvent = group.OrderBy(e => e.Timestamp).First();

                return new
                {
                    Category = group.Key,
                    MaxSeverity = group.Max(e => e.Severity),
                    EventCount = group.Count(),
                    MaxConfidence = group.Max(e => e.ConfidenceScore),
                    FirstOccurrence = group.Min(e => e.Timestamp),
                    RepresentativeEvent = group
                        .OrderBy(e => e.Timestamp)
                        .ThenByDescending(e => e.Severity)
                        .ThenByDescending(e => e.ConfidenceScore)
                        .First()
                };
            })
            .OrderByDescending(r => r.MaxSeverity)
            .ThenBy(r => r.FirstOccurrence)
            .ThenByDescending(r => r.EventCount)
            .ThenByDescending(r => r.MaxConfidence)
            .ToList();

            return rankedCategories.First().RepresentativeEvent;
        }
    }
}
