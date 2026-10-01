using System;
using System.Collections.Generic;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer.Intelligence.Implementation
{
    public class AntiCheatCompatibilityService
    {
        private static readonly Lazy<AntiCheatCompatibilityService> _instance = 
            new Lazy<AntiCheatCompatibilityService>(() => new AntiCheatCompatibilityService(App.LoggingService!));

        public static AntiCheatCompatibilityService Instance => _instance.Value;

        private readonly ILoggingService _logger;

        // Anti-Cheats conhecidos em execução (nomes de processos ou serviços)
        private readonly HashSet<string> _knownAntiCheats = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "vgk", "vgc", // Vanguard
            "easyanticheat", "beservice", // EAC, BattlEye
            "xigncode", "uncheater", "ricochet", "punkbuster"
        };

        // Associa jogos a seus Anti-Cheats (Game Process -> AntiCheat Type)
        private readonly Dictionary<string, string> _gameToAntiCheatMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "valorant", "Vanguard" },
            { "valorant-win64-shipping", "Vanguard" },
            { "league of legends", "Vanguard" },
            { "leagueclient", "Vanguard" },
            { "fortniteclient-win64-shipping", "EasyAntiCheat" },
            { "apex", "EasyAntiCheat" },
            { "r5apex", "EasyAntiCheat" },
            { "rustclient", "EasyAntiCheat" },
            { "deadbydaylight-win64-shipping", "EasyAntiCheat" },
            { "rainbowsix", "BattlEye" },
            { "rainbowsix_be", "BattlEye" },
            { "tslgame", "BattlEye" }, // PUBG
            { "escapefromtarkov", "BattlEye" },
            { "destiny2", "BattlEye" },
            { "gta5_enhanced_be", "BattlEye" }, // GTA V Enhanced Edition
            { "gta5_enhanced", "BattlEye" },    // GTA V Enhanced (BattlEye service)
            { "warzone", "Ricochet" },
            { "modernwarfare", "Ricochet" },
            { "cod", "Ricochet" }
        };

        private AntiCheatCompatibilityService(ILoggingService logger)
        {
            _logger = logger;
            _logger.LogEntry("[AntiCheatCompatibilityService] .ctor");
            _logger.LogExit("[AntiCheatCompatibilityService] .ctor");
        }

        public string? GetAntiCheatForGame(string processName)
        {
            _logger.LogEntry(nameof(GetAntiCheatForGame), ("processName", processName));
            if (_gameToAntiCheatMap.TryGetValue(processName, out var antiCheat))
            {
                _logger.LogExit(nameof(GetAntiCheatForGame), antiCheat);
                return antiCheat;
            }
            _logger.LogExit(nameof(GetAntiCheatForGame), "null");
            return null;
        }

        /// <summary>
        /// Verifica se algum anti-cheat conhecido esta em execucao no sistema.
        /// Usado para suspender otimizacoes intrusivas (DLS / ProcessLaunchAccelerator).
        /// </summary>
        public string? DetectRunningAntiCheat()
        {
            try
            {
                var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var proc in System.Diagnostics.Process.GetProcesses())
                {
                    try
                    {
                        var name = proc.ProcessName;
                        if (!string.IsNullOrEmpty(name) && _knownAntiCheats.Contains(name))
                            running.Add(name);
                    }
                    catch { }
                    finally { try { proc.Dispose(); } catch { } }
                }

                if (running.Count == 0)
                    return null;

                var found = string.Join(", ", running);
                App.LoggingService?.LogWarning($"[AntiCheat] Anti-cheat detectado em execucao: {found}. Otimizacoes intrusivas suspensas.");
                return found;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[AntiCheat] Erro ao detectar anti-cheat: {ex.Message}");
                return null;
            }
        }

        public bool IsOptimizationSafe(string processName, OptimizationCategory category)
        {
            _logger.LogEntry(nameof(IsOptimizationSafe), ("processName", processName), ("category", category));
            var antiCheat = GetAntiCheatForGame(processName);
            
            // Se não tem anticheat, praticamente tudo é seguro
            if (string.IsNullOrEmpty(antiCheat))
            {
                _logger.LogExit(nameof(IsOptimizationSafe), true);
                return true;
            }

            // Vanguard é extremamente intrusivo e bloqueia manipulação de threads pesada
            if (antiCheat == "Vanguard")
            {
                if (category == OptimizationCategory.Unsafe || category == OptimizationCategory.Restricted)
                {
                    App.LoggingService?.LogWarning($"[AntiCheat] Otimização {category} bloqueada no {processName} devido ao Vanguard.");
                    _logger.LogExit(nameof(IsOptimizationSafe), false);
                    return false;
                }
            }
            
            // BattlEye bloqueia injeções e leitura de memória de processos do jogo
            if (antiCheat == "BattlEye" || antiCheat == "EasyAntiCheat")
            {
                if (category == OptimizationCategory.Unsafe)
                {
                    App.LoggingService?.LogWarning($"[AntiCheat] Otimização {category} bloqueada no {processName} devido ao {antiCheat}.");
                    _logger.LogExit(nameof(IsOptimizationSafe), false);
                    return false;
                }
            }

            _logger.LogExit(nameof(IsOptimizationSafe), true);
            return true;
        }
    }

    public enum OptimizationCategory
    {
        Safe,       // Ex: Limpar standby list, power plan, timer resolution
        Restricted, // Ex: Mudar afinidade de threads CPU agressivamente
        Unsafe      // Ex: Injetar hook, ler memória protegida, overclocks via ring0
    }
}
