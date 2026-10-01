using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Gamer.Helpers
{
    /// <summary>
    /// Resolve processos de jogos com aliases e retry — evita falhas como "l2" vs "L2.bin".
    /// </summary>
    public static class GameProcessResolver
    {
        private static readonly Dictionary<string, string[]> Aliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["l2"] = new[] { "l2", "L2", "l2.bin", "L2.bin", "lineage2", "Lineage2" },
            ["l2.bin"] = new[] { "l2", "L2", "l2.bin", "L2.bin", "lineage2", "Lineage2" },
            ["cs2"] = new[] { "cs2", "csgo" },
            ["csgo"] = new[] { "cs2", "csgo" }};

        public static IEnumerable<string> GetCandidateProcessNames(string? gameExecutableOrName)
        {
            if (string.IsNullOrWhiteSpace(gameExecutableOrName))
                yield break;

            var fileName = Path.GetFileName(gameExecutableOrName);
            var primary = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrWhiteSpace(primary))
                primary = fileName;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var names = new List<string>();

            void TryAdd(string? name)
            {
                if (!string.IsNullOrWhiteSpace(name) && seen.Add(name))
                    names.Add(name);
            }

            TryAdd(primary);
            TryAdd(fileName);

            var lookupKey = primary.ToLowerInvariant();
            if (Aliases.TryGetValue(lookupKey, out var aliases))
            {
                foreach (var alias in aliases)
                    TryAdd(alias);
            }

            foreach (var name in names)
                yield return name;
        }

        public static Process[] FindProcesses(string? gameExecutableOrName)
        {
            foreach (var name in GetCandidateProcessNames(gameExecutableOrName))
            {
                try
                {
                    var processes = Process.GetProcessesByName(name);
                    if (processes.Length > 0)
                        return processes;
                }
                catch { }
            }

            return Array.Empty<Process>();
        }

        public static async Task<Process[]> FindProcessesAsync(string? gameExecutableOrName, int maxWaitMs = 3000)
        {
            var maxAttempts = Math.Max(1, maxWaitMs / 500);
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                var processes = FindProcesses(gameExecutableOrName);
                if (processes.Length > 0)
                    return processes;

                if (attempt < maxAttempts - 1)
                    await Task.Delay(500).ConfigureAwait(false);
            }

            return Array.Empty<Process>();
        }

        public static string GetPrimaryProcessName(string? gameExecutableOrName)
        {
            return GetCandidateProcessNames(gameExecutableOrName).FirstOrDefault()
                ?? Path.GetFileNameWithoutExtension(gameExecutableOrName ?? string.Empty);
        }
    }
}
