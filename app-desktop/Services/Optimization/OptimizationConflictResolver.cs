using System;
using System.Collections.Concurrent;
using System.Linq;
using VoltrisOptimizer.Services.Gamer.Adaptive;

namespace VoltrisOptimizer.Services.Optimization
{
    public enum OptimizationSource
    {
        SystemDefault = 0,
        RealTimeSmoothness = 1,
        VoltrisBrain = 2,
        BodyArms = 3,
        GamerMode = 4,
        ManualUser = 5
    }

    public enum OptimizationDomain
    {
        EPP,
        SystemResponsiveness,
        GamingMode,
        TimerResolution,
        ProcessPriority,
        PowerPlan,
        CpuAffinity,
        GpuProfile,
        NetworkQoS,
        MemoryTrim,
        AudioIsolation,
        MMCSSSettings
    }

    public class ConflictResolvedAction
    {
        public OptimizationDomain Domain { get; set; }
        public object? Value { get; set; }
        public OptimizationSource Source { get; set; }
        public string? Reason { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class OptimizationConflictResolver
    {
        private readonly ILoggingService _logger;
        private readonly ConcurrentDictionary<OptimizationDomain, (object? Value, OptimizationSource Source, DateTime Timestamp, string Reason)> _state = new();

        public OptimizationConflictResolver(ILoggingService logger)
        {
            _logger = logger;
        }

        public void SetState(OptimizationDomain domain, object? value, OptimizationSource source, string reason)
        {
            var timestamp = DateTime.UtcNow;
            _state.AddOrUpdate(domain,
                _ => (value, source, timestamp, reason),
                (_, existing) =>
                {
                    if (source >= existing.Source)
                    {
                        LogDebug($"[ConflictResolver] {domain}: {existing.Source} -> {source} (novo valor: {value}), razão: {reason}");
                        return (value, source, timestamp, reason);
                    }
                    LogDebug($"[ConflictResolver] {domain}: mantido {existing.Source} (recusado {source} - prioridade menor)");
                    return existing;
                });
        }

        public (object? Value, OptimizationSource Source, string Reason)? GetState(OptimizationDomain domain)
        {
            if (_state.TryGetValue(domain, out var entry))
                return (entry.Value, entry.Source, entry.Reason);
            return null;
        }

        public T? Resolve<T>(OptimizationDomain domain, T? proposedValue, OptimizationSource proposedSource, string reason)
        {
            var timestamp = DateTime.UtcNow;

            if (!_state.TryGetValue(domain, out var existing))
            {
                _state[domain] = (proposedValue, proposedSource, timestamp, reason);
                LogDebug($"[ConflictResolver] {domain}: primeiro valor = {proposedValue} ({proposedSource})");
                return proposedValue;
            }

            if (proposedSource >= existing.Source)
            {
                _state[domain] = (proposedValue, proposedSource, timestamp, reason);
                LogDebug($"[ConflictResolver] {domain}: {existing.Source} -> {proposedSource} = {proposedValue}");
                return proposedValue;
            }

            LogDebug($"[ConflictResolver] {domain}: mantido {existing.Value} ({existing.Source}) - {proposedSource} rejeitado");
            return (T?)existing.Value;
        }

        public void ReleaseSource(OptimizationSource source)
        {
            var domainsToRelease = _state
                .Where(kvp => kvp.Value.Source == source)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var domain in domainsToRelease)
            {
                _state.TryRemove(domain, out _);
                LogDebug($"[ConflictResolver] Liberado {domain} da fonte {source}");
            }
        }

        public void ReleaseAll()
        {
            _state.Clear();
            LogDebug("[ConflictResolver] Todos os estados liberados");
        }

        public bool IsLockedByHigherSource(OptimizationDomain domain, OptimizationSource requestingSource)
        {
            if (_state.TryGetValue(domain, out var existing))
                return existing.Source > requestingSource;
            return false;
        }

        public string GetDiagnosticsReport()
        {
            var entries = _state
                .OrderBy(kvp => kvp.Key.ToString())
                .Select(kvp =>
                    $"  {kvp.Key}: Valor={kvp.Value.Value}, Fonte={kvp.Value.Source}, Timestamp={kvp.Value.Timestamp:HH:mm:ss.fff}, Razão={kvp.Value.Reason}");
            return $"[ConflictResolver] Estado atual ({_state.Count} domínios):\n{string.Join("\n", entries)}";
        }

        private void LogDebug(string message)
        {
            _logger.LogDebug(message, "ConflictResolver");
        }
    }
}
