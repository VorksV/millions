using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Gaming
{
    /// <summary>
    /// Game compatibility protection system
    /// Prevents optimizations that could negatively impact gaming performance
    /// </summary>
    public class GameCompatibilityProtector
    {
        private readonly ILoggingService _logger;
        private readonly ISystemInfoService _systemInfoService;
        private readonly HashSet<string> _protectedGames;
        private readonly HashSet<string> _protectedProcesses;
        private readonly Dictionary<string, GameOptimizationProfile> _gameProfiles;

        public GameCompatibilityProtector(ILoggingService logger, ISystemInfoService systemInfoService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _systemInfoService = systemInfoService ?? throw new ArgumentNullException(nameof(systemInfoService));

            _protectedGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _protectedProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _gameProfiles = new Dictionary<string, GameOptimizationProfile>(StringComparer.OrdinalIgnoreCase);

            InitializeProtectedLists();
            LoadGameProfiles();
        }

        /// <summary>
        /// Checks if an optimization is safe to apply during gaming
        /// </summary>
        public async Task<GameCompatibilityResult> CheckOptimizationCompatibilityAsync(
            VoltrisOptimizer.Core.SystemIntelligenceProfiler.ActionRecommendation optimization,
            CancellationToken ct = default)
        {
            var result = new GameCompatibilityResult
            {
                OptimizationName = optimization.Name,
                IsCompatible = true,
                CheckTimestamp = DateTime.UtcNow
            };

            try
            {
                _logger.Log(LogLevel.Debug, LogCategory.Gamer,
                    $"Checking compatibility for optimization: {optimization.Name}",
                    source: "GameCompatibilityProtector");

                // Check if games are currently running
                var runningGames = await GetRunningProtectedGamesAsync(ct);
                if (runningGames.Any())
                {
                    result.RunningGames = runningGames;
                    result.IsGamingSession = true;

                    // Check optimization against running games
                    var compatibilityIssues = await CheckAgainstRunningGamesAsync(optimization, runningGames, ct);
                    if (compatibilityIssues.Any())
                    {
                        result.IsCompatible = false;
                        result.CompatibilityIssues = compatibilityIssues;
                        result.Recommendation = CompatibilityRecommendation.DeferUntilNoGaming;

                        _logger.Log(LogLevel.Warning, LogCategory.Gamer, 
                            $"Optimization {optimization.Name} blocked due to running games: {string.Join(", ", runningGames)}", 
                            null, "GameCompatibilityProtector");
                    }
                }

                // Check optimization registry targets
                var registryIssues = CheckRegistryTargets(optimization);
                if (registryIssues.Any())
                {
                    result.IsCompatible = false;
                    result.CompatibilityIssues.AddRange(registryIssues);
                    result.Recommendation = CompatibilityRecommendation.ModifyOptimization;

                    _logger.Log(LogLevel.Warning, LogCategory.Gamer, 
                        $"Optimization {optimization.Name} has problematic registry targets",
                        null, "GameCompatibilityProtector");
                }

                // Check optimization service targets
                var serviceIssues = CheckServiceTargets(optimization);
                if (serviceIssues.Any())
                {
                    result.IsCompatible = false;
                    result.CompatibilityIssues.AddRange(serviceIssues);
                    result.Recommendation = CompatibilityRecommendation.ModifyOptimization;

                    _logger.Log(LogLevel.Warning, LogCategory.Gamer, 
                        $"Optimization {optimization.Name} affects critical gaming services",
                        null, "GameCompatibilityProtector");
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Compatibility check failed for {optimization.Name}: {ex.Message}", ex);
                result.IsCompatible = false;
                result.CompatibilityIssues.Add($"Check failed: {ex.Message}");
                result.Recommendation = CompatibilityRecommendation.SkipOptimization;
                return result;
            }
        }

        /// <summary>
        /// Gets currently running protected games
        /// </summary>
        public async Task<List<string>> GetRunningProtectedGamesAsync(CancellationToken ct = default)
        {
            var runningGames = new List<string>();

            try
            {
                var processes = Process.GetProcesses();
                
                foreach (var process in processes)
                {
                    try
                    {
                        if (ct.IsCancellationRequested) break;

                        var processName = process.ProcessName.ToLower();
                        var fileName = process.MainModule?.FileName ?? string.Empty;

                        // Check against protected process names
                        if (_protectedProcesses.Contains(processName))
                        {
                            runningGames.Add(processName);
                            continue;
                        }

                        // Check against protected game names in file paths
                        if (_protectedGames.Any(game => fileName.Contains(game, StringComparison.OrdinalIgnoreCase)))
                        {
                            runningGames.Add(Path.GetFileNameWithoutExtension(fileName) ?? processName);
                        }
                    }
                    catch
                    {
                        // Ignore access denied or other process access errors
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to check running games: {ex.Message}", ex);
            }

            return runningGames.Distinct().ToList();
        }

        /// <summary>
        /// Adds a game to the protected list
        /// </summary>
        public void AddProtectedGame(string gameName)
        {
            if (!string.IsNullOrEmpty(gameName))
            {
                _protectedGames.Add(gameName.ToLower());
                _logger.Log(LogLevel.Info, LogCategory.Gamer,
                    $"Added protected game: {gameName}", source: "GameCompatibilityProtector");
            }
        }

        /// <summary>
        /// Removes a game from the protected list
        /// </summary>
        public void RemoveProtectedGame(string gameName)
        {
            if (!string.IsNullOrEmpty(gameName))
            {
                _protectedGames.Remove(gameName.ToLower());
                _logger.Log(LogLevel.Info, LogCategory.Gamer,
                    $"Removed protected game: {gameName}", source: "GameCompatibilityProtector");
            }
        }

        /// <summary>
        /// Gets optimization profile for a specific game
        /// </summary>
        public GameOptimizationProfile GetGameProfile(string gameName)
        {
            if (_gameProfiles.TryGetValue(gameName.ToLower(), out var profile))
                return profile;

            // Return default profile for unknown games
            return new GameOptimizationProfile
            {
                GameName = gameName,
                IsProtected = true,
                RecommendedOptimizations = new List<string>(),
                BlockedOptimizations = new List<string>
                {
                    // Generally unsafe optimizations for gaming
                    "DisableSuperfetch",
                    "AggressiveMemoryTrimming",
                    "ReduceBackgroundServices"
                },
                PerformanceSensitiveSettings = new List<string>
                {
                    "PowerPlanOptimization",
                    "GpuScheduling",
                    "CpuScheduling"
                }
            };
        }

        // Private helper methods
        private void InitializeProtectedLists()
        {
            // Popular game process names
            var gameProcesses = new[]
            {
                "steam", "epicgameslauncher", "upc", "origin", "ubisoftconnect",
                "gamelauncher", "battle.net", "rockstar", "eaapp",
                // Game engines
                "unity", "unreal", "godot",
                // Specific games (common names)
                "valorant", "fortnite", "minecraft", "csgo", "dota2",
                "wow", "lol", "apexlegends", "pubg", "rust",
                "cyberpunk2077", "assassinscreed", "gta5", "reddead",
                "cod", "battlefield", "destiny2", "overwatch", "rocketleague"
            };

            foreach (var process in gameProcesses)
            {
                _protectedProcesses.Add(process);
            }

            // Protected game names
            var protectedGames = new[]
            {
                "valorant", "fortnite", "minecraft", "counter-strike", "dota 2",
                "world of warcraft", "league of legends", "apex legends", "pubg",
                "cyberpunk 2077", "assassin's creed", "grand theft auto", "red dead",
                "call of duty", "battlefield", "destiny 2", "overwatch", "rocket league"
            };

            foreach (var game in protectedGames)
            {
                _protectedGames.Add(game.ToLower());
            }

            _logger.Log(LogLevel.Info, LogCategory.Gamer,
                $"Initialized with {_protectedProcesses.Count} protected processes and {_protectedGames.Count} protected games",
                source: "GameCompatibilityProtector");
        }

        private void LoadGameProfiles()
        {
            // Load predefined profiles for popular games
            var profiles = new[]
            {
                new GameOptimizationProfile
                {
                    GameName = "Valorant",
                    IsProtected = true,
                    BlockedOptimizations = new List<string> { "DisableSuperfetch", "AggressivePowerSaving" },
                    PerformanceSensitiveSettings = new List<string> { "LowLatencyGpuScheduling", "HighPerformancePowerPlan" }
                },
                new GameOptimizationProfile
                {
                    GameName = "Fortnite",
                    IsProtected = true,
                    BlockedOptimizations = new List<string> { "ReduceBackgroundServices", "AggressiveMemoryTrimming" },
                    PerformanceSensitiveSettings = new List<string> { "GpuSchedulingOptimization", "CpuPriorityBoost" }
                },
                new GameOptimizationProfile
                {
                    GameName = "Counter-Strike",
                    IsProtected = true,
                    BlockedOptimizations = new List<string> { "DisableMMCSS", "AggressivePowerManagement" },
                    PerformanceSensitiveSettings = new List<string> { "LowLatencyMode", "RealTimeProcessPriority" }
                }
            };

            foreach (var profile in profiles)
            {
                _gameProfiles[profile.GameName.ToLower()] = profile;
            }
        }

        private async Task<List<string>> CheckAgainstRunningGamesAsync(
            VoltrisOptimizer.Core.SystemIntelligenceProfiler.ActionRecommendation optimization,
            List<string> runningGames,
            CancellationToken ct)
        {
            var issues = new List<string>();

            foreach (var game in runningGames)
            {
                ct.ThrowIfCancellationRequested();
                
                var profile = GetGameProfile(game);
                
                // Check if optimization is explicitly blocked for this game
                if (profile.BlockedOptimizations.Contains(optimization.Name))
                {
                    issues.Add($"Blocked for {game} - known compatibility issue");
                }

                // Check if optimization affects performance-sensitive settings during gameplay
                if (profile.PerformanceSensitiveSettings.Any(setting => 
                    optimization.RegistryKeys?.Any(key => key.Contains(setting, StringComparison.OrdinalIgnoreCase)) == true ||
                    optimization.Services?.Any(service => service.Contains(setting, StringComparison.OrdinalIgnoreCase)) == true))
                {
                    issues.Add($"Affects performance-sensitive settings for {game}");
                }
            }

            return issues;
        }

        private List<string> CheckRegistryTargets(VoltrisOptimizer.Core.SystemIntelligenceProfiler.ActionRecommendation optimization)
        {
            var issues = new List<string>();

            if (optimization.RegistryKeys == null) return issues;

            // Registry keys that are generally problematic for gaming
            var problematicKeys = new[]
            {
                @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters",
                @"HKLM\SYSTEM\CurrentControlSet\Services\SysMain", // Superfetch
                @"HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerSettings",
                @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"
            };

            foreach (var key in optimization.RegistryKeys)
            {
                if (problematicKeys.Any(problematic => key.StartsWith(problematic, StringComparison.OrdinalIgnoreCase)))
                {
                    issues.Add($"Problematic registry key: {key}");
                }
            }

            return issues;
        }

        private List<string> CheckServiceTargets(VoltrisOptimizer.Core.SystemIntelligenceProfiler.ActionRecommendation optimization)
        {
            var issues = new List<string>();

            if (optimization.Services == null) return issues;

            // Services that are critical for gaming
            var criticalServices = new[]
            {
                "AudioSrv", "Audiosrv", // Windows Audio
                "FontCache", // Windows Font Cache
                "Themes", // Desktop Window Manager
                "Schedule", // Task Scheduler (some game launchers depend on it)
                "BITS", // Background Intelligent Transfer Service
                "MSiSCSI" // Some games use network storage
            };

            foreach (var service in optimization.Services)
            {
                if (criticalServices.Contains(service, StringComparer.OrdinalIgnoreCase))
                {
                    issues.Add($"Affects critical service: {service}");
                }
            }

            return issues;
        }
    }

    // Data Models
    public class GameCompatibilityResult
    {
        public string OptimizationName { get; set; }
        public bool IsCompatible { get; set; }
        public bool IsGamingSession { get; set; }
        public DateTime CheckTimestamp { get; set; }
        public List<string> RunningGames { get; set; } = new();
        public List<string> CompatibilityIssues { get; set; } = new();
        public CompatibilityRecommendation Recommendation { get; set; }
    }

    public class GameOptimizationProfile
    {
        public string GameName { get; set; }
        public bool IsProtected { get; set; }
        public List<string> RecommendedOptimizations { get; set; } = new();
        public List<string> BlockedOptimizations { get; set; } = new();
        public List<string> PerformanceSensitiveSettings { get; set; } = new();
    }

    public enum CompatibilityRecommendation
    {
        SafeToApply,
        DeferUntilNoGaming,
        ModifyOptimization,
        SkipOptimization,
        MonitorDuringApplication
    }
}
