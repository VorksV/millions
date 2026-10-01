using System;
using System.Collections.Generic;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer.Intelligence.Implementation
{
    public class GameClassificationService
    {
        private static readonly Lazy<GameClassificationService> _instance = 
            new Lazy<GameClassificationService>(() => new GameClassificationService());

        public static GameClassificationService Instance => _instance.Value;

        // Mapeamento em memória para alta performance (process name sem .exe -> Categoria)
        private readonly Dictionary<string, GameClassificationCategory> _gameDatabase = new Dictionary<string, GameClassificationCategory>(StringComparer.OrdinalIgnoreCase)
        {
            // Competitivos (Foco em latência)
            { "csgo", GameClassificationCategory.Competitive },
            { "cs2", GameClassificationCategory.Competitive },
            { "valorant", GameClassificationCategory.Competitive },
            { "valorant-win64-shipping", GameClassificationCategory.Competitive },
            { "r6s", GameClassificationCategory.Competitive },
            { "rainbowsix", GameClassificationCategory.Competitive },
            { "rainbowsix_be", GameClassificationCategory.Competitive },
            { "dota2", GameClassificationCategory.Competitive },
            { "league of legends", GameClassificationCategory.Competitive },
            { "leagueclient", GameClassificationCategory.Competitive },
            { "overwatch", GameClassificationCategory.Competitive },
            { "overwatchlauncher", GameClassificationCategory.Competitive },
            { "apex", GameClassificationCategory.Competitive },
            { "r5apex", GameClassificationCategory.Competitive },
            { "rocketleague", GameClassificationCategory.Competitive },
            { "fortniteclient-win64-shipping", GameClassificationCategory.Competitive },
            { "fortnite", GameClassificationCategory.Competitive },
            { "pubg", GameClassificationCategory.Competitive },
            { "tslgame", GameClassificationCategory.Competitive },
            { "cod", GameClassificationCategory.Competitive },
            { "modernwarfare", GameClassificationCategory.Competitive },
            { "warzone", GameClassificationCategory.Competitive },
            { "mw3", GameClassificationCategory.Competitive },
            { "blackops", GameClassificationCategory.Competitive },
            { "farlight84", GameClassificationCategory.Competitive },
            { "narakabytedown", GameClassificationCategory.Competitive },
            { "superpeople", GameClassificationCategory.Competitive },
            { "splitgate", GameClassificationCategory.Competitive },
            { "trackmania", GameClassificationCategory.Competitive },
            { "battlebit", GameClassificationCategory.Competitive },

            // SinglePlayer (Foco em FPS médio e GPU)
            { "gta5", GameClassificationCategory.Singleplayer },
            { "playgtav", GameClassificationCategory.Singleplayer },
            { "gtav", GameClassificationCategory.Singleplayer },
            { "cyberpunk2077", GameClassificationCategory.Singleplayer },
            { "witcher3", GameClassificationCategory.Singleplayer },
            { "rdr2", GameClassificationCategory.Singleplayer },
            { "starfield", GameClassificationCategory.Singleplayer },
            { "hogwartslegacy", GameClassificationCategory.Singleplayer },
            { "eldenring", GameClassificationCategory.Singleplayer },
            { "helldivers2", GameClassificationCategory.Singleplayer },
            { "palworld-win64-shipping", GameClassificationCategory.Singleplayer },
            { "skyrim", GameClassificationCategory.Singleplayer },
            { "skyrimse", GameClassificationCategory.Singleplayer },
            { "fallout4", GameClassificationCategory.Singleplayer },
            { "falloutnv", GameClassificationCategory.Singleplayer },
            { "farcry5", GameClassificationCategory.Singleplayer },
            { "farcry6", GameClassificationCategory.Singleplayer },
            { "doom", GameClassificationCategory.Singleplayer },
            { "doometernal", GameClassificationCategory.Singleplayer },
            { "doom64", GameClassificationCategory.Singleplayer },
            { "residentevil2", GameClassificationCategory.Singleplayer },
            { "residentevil3", GameClassificationCategory.Singleplayer },
            { "residentevil4", GameClassificationCategory.Singleplayer },
            { "re2", GameClassificationCategory.Singleplayer },
            { "re3", GameClassificationCategory.Singleplayer },
            { "re4", GameClassificationCategory.Singleplayer },
            { "re8", GameClassificationCategory.Singleplayer },
            { "re7", GameClassificationCategory.Singleplayer },
            { "deathstranding", GameClassificationCategory.Singleplayer },
            { "ds", GameClassificationCategory.Singleplayer },
            { "sekiro", GameClassificationCategory.Singleplayer },
            { "liesofp", GameClassificationCategory.Singleplayer },
            { "control", GameClassificationCategory.Singleplayer },
            { "alanwake2", GameClassificationCategory.Singleplayer },
            { "spiderman", GameClassificationCategory.Singleplayer },
            { "spiderman2", GameClassificationCategory.Singleplayer },
            { "godofwar", GameClassificationCategory.Singleplayer },
            { "horizonzerodawn", GameClassificationCategory.Singleplayer },
            { "horizonforbiddenwest", GameClassificationCategory.Singleplayer },
            { "lastofus", GameClassificationCategory.Singleplayer },
            { "tlou", GameClassificationCategory.Singleplayer },
            { "tombraider", GameClassificationCategory.Singleplayer },
            { "shadowofthetombraider", GameClassificationCategory.Singleplayer },
            { "riskoftrain2", GameClassificationCategory.Singleplayer },
            { "hades", GameClassificationCategory.Singleplayer },
            { "deadcells", GameClassificationCategory.Singleplayer },
            { "hollowknight", GameClassificationCategory.Singleplayer },
            { "stray", GameClassificationCategory.Singleplayer },
            { "atomicheart", GameClassificationCategory.Singleplayer },
            { "hogwarts", GameClassificationCategory.Singleplayer },
            { "avowed", GameClassificationCategory.Singleplayer },
            { "stalker2", GameClassificationCategory.Singleplayer },
            { "indianajones", GameClassificationCategory.Singleplayer },
            { "baldur3", GameClassificationCategory.Singleplayer },
            { "divinityoriginalsin2", GameClassificationCategory.Singleplayer },
            { "mass Effect", GameClassificationCategory.Singleplayer },
            { "masseffect", GameClassificationCategory.Singleplayer },
            { "dragonage", GameClassificationCategory.Singleplayer },
            { "assassinscreed", GameClassificationCategory.Singleplayer },
            { "acmirage", GameClassificationCategory.Singleplayer },
            { "acvalhalla", GameClassificationCategory.Singleplayer },
            { "acorigins", GameClassificationCategory.Singleplayer },
            { "acodyssey", GameClassificationCategory.Singleplayer },
            { "ghostoftsushima", GameClassificationCategory.Singleplayer },
            { "ghost", GameClassificationCategory.Singleplayer },
            { "daysgone", GameClassificationCategory.Singleplayer },
            { "madmax", GameClassificationCategory.Singleplayer },
            { "dysmantle", GameClassificationCategory.Singleplayer },
            
            // MMO / RPG (Foco em rede e CPU)
            { "wow", GameClassificationCategory.MMO },
            { "wowclassic", GameClassificationCategory.MMO },
            { "ffxiv", GameClassificationCategory.MMO },
            { "ffxiv_dx11", GameClassificationCategory.MMO },
            { "blackdesert64", GameClassificationCategory.MMO },
            { "lostark", GameClassificationCategory.MMO },
            { "gw2-64", GameClassificationCategory.MMO },
            { "eso64", GameClassificationCategory.MMO },
            { "albion-online", GameClassificationCategory.MMO },
            { "l2", GameClassificationCategory.MMO },
            { "lineage", GameClassificationCategory.MMO },
            { "lineng", GameClassificationCategory.MMO },
            { "main", GameClassificationCategory.MMO },
            { "neworld", GameClassificationCategory.MMO },
            { "throneandliberty", GameClassificationCategory.MMO },
            { "palworld", GameClassificationCategory.MMO },
            { "diablo3", GameClassificationCategory.MMO },
            { "diablo4", GameClassificationCategory.MMO },
            { "diablo ii", GameClassificationCategory.MMO },
            { "pathofexile", GameClassificationCategory.MMO },
            { "pathofexile_x64", GameClassificationCategory.MMO },
            { "poe", GameClassificationCategory.MMO },
            { "poe2", GameClassificationCategory.MMO },
            { "lastepoch", GameClassificationCategory.MMO },
            { "torchlight2", GameClassificationCategory.MMO },
            { "warframe", GameClassificationCategory.MMO },
            { "destiny2", GameClassificationCategory.MMO },
            { "destiny", GameClassificationCategory.MMO },
            { "starwarsold", GameClassificationCategory.MMO },
            { "swtor", GameClassificationCategory.MMO },
            { "theoldrepublic", GameClassificationCategory.MMO },
            { "guildwars2", GameClassificationCategory.MMO },
            { "terac", GameClassificationCategory.MMO },
            { "tera", GameClassificationCategory.MMO },
            { "ark", GameClassificationCategory.MMO },
            { "rust", GameClassificationCategory.MMO },
            { "vrising", GameClassificationCategory.MMO },
            { "valheim", GameClassificationCategory.MMO },
            { "conanexiles", GameClassificationCategory.MMO },
            { "foxhole", GameClassificationCategory.MMO },
            { "runelite", GameClassificationCategory.MMO },
            { "oldschool", GameClassificationCategory.MMO },
            
            // Simulação (Foco em streaming de assets e CPU)
            { "flightsimulator", GameClassificationCategory.Simulation },
            { "msfs", GameClassificationCategory.Simulation },
            { "eurotrucks2", GameClassificationCategory.Simulation },
            { "amtrucks", GameClassificationCategory.Simulation },
            { "assettocorsa", GameClassificationCategory.Simulation },
            { "acs", GameClassificationCategory.Simulation },
            { "ac1", GameClassificationCategory.Simulation },
            { "assettocorsaevo", GameClassificationCategory.Simulation },
            { "f122", GameClassificationCategory.Simulation },
            { "f123", GameClassificationCategory.Simulation },
            { "f124", GameClassificationCategory.Simulation },
            { "farmingsimulator2022", GameClassificationCategory.Simulation },
            { "farmingsimulator25", GameClassificationCategory.Simulation },
            { "fs25", GameClassificationCategory.Simulation },
            { "farming", GameClassificationCategory.Simulation },
            { "forza", GameClassificationCategory.Simulation },
            { "forzamotorsport", GameClassificationCategory.Simulation },
            { "forzahorizon5", GameClassificationCategory.Simulation },
            { "forzahorizon4", GameClassificationCategory.Simulation },
            { "dirt", GameClassificationCategory.Simulation },
            { "dirtrally", GameClassificationCategory.Simulation },
            { "dirt rally", GameClassificationCategory.Simulation },
            { "beamng", GameClassificationCategory.Simulation },
            { "beamngdrive", GameClassificationCategory.Simulation },
            { "cities", GameClassificationCategory.Simulation },
            { "citiesskylines", GameClassificationCategory.Simulation },
            { "citiesskylines2", GameClassificationCategory.Simulation },
            { "kerbal", GameClassificationCategory.Simulation },
            { "ksp", GameClassificationCategory.Simulation },
            { "startfield", GameClassificationCategory.Simulation },
            { "nms", GameClassificationCategory.Simulation },
            { "nomanssky", GameClassificationCategory.Simulation },
            { "starcitizen", GameClassificationCategory.Simulation },
            
            // Estratégia (Foco em thread scheduling)
            { "civilizationvi", GameClassificationCategory.Strategy },
            { "civilizationvii", GameClassificationCategory.Strategy },
            { "aoe2de_s", GameClassificationCategory.Strategy },
            { "aoe3de_s", GameClassificationCategory.Strategy },
            { "aoe4", GameClassificationCategory.Strategy },
            { "starcraft ii", GameClassificationCategory.Strategy },
            { "starcraft", GameClassificationCategory.Strategy },
            { "sc2", GameClassificationCategory.Strategy },
            { "totalwar", GameClassificationCategory.Strategy },
            { "eu4", GameClassificationCategory.Strategy },
            { "hoi4", GameClassificationCategory.Strategy },
            { "ck3", GameClassificationCategory.Strategy },
            { "victoria3", GameClassificationCategory.Strategy },
            { "stellaris", GameClassificationCategory.Strategy },
            { "factorio", GameClassificationCategory.Strategy },
            { "satisfactory", GameClassificationCategory.Strategy },
            { "shapez2", GameClassificationCategory.Strategy },
            { "dyson sphere", GameClassificationCategory.Strategy },
            { "dsp", GameClassificationCategory.Strategy },
            { "warcraft3", GameClassificationCategory.Strategy },
            { "warhammer3", GameClassificationCategory.Strategy },
            { "totalwarhammer", GameClassificationCategory.Strategy },
            { "ageofmythology", GameClassificationCategory.Strategy },
            { "aom", GameClassificationCategory.Strategy },
            { "anno1800", GameClassificationCategory.Strategy },
            { "anno", GameClassificationCategory.Strategy },
            { "frostpunk", GameClassificationCategory.Strategy },
            { "frostpunk2", GameClassificationCategory.Strategy },
            { "theyarebillions", GameClassificationCategory.Strategy },
            { "rimworld", GameClassificationCategory.Strategy },
            { "timberborn", GameClassificationCategory.Strategy }
        };

        private GameClassificationService()
        {
            App.LoggingService?.LogEntry(nameof(GameClassificationService));
            App.LoggingService?.LogExit(nameof(GameClassificationService));
        }

        public GameClassificationCategory ClassifyGame(string processName)
        {
            App.LoggingService?.LogEntry(nameof(ClassifyGame), ("processName", processName));
            if (string.IsNullOrWhiteSpace(processName))
            {
                App.LoggingService?.LogDebug($"[GameClassification] ⚠️ processName vazio/nulo -> General");
                App.LoggingService?.LogExit(nameof(ClassifyGame), GameClassificationCategory.General);
                return GameClassificationCategory.General;
            }

            // Remover extensão .exe se presente
            var name = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) 
                ? processName.Substring(0, processName.Length - 4) 
                : processName;

            App.LoggingService?.LogDebug($"[GameClassification] 📝 Classificando '{processName}' (normalizado: '{name}')...");

            // Match exato (case insensitive)
            if (_gameDatabase.TryGetValue(name, out var category))
            {
                App.LoggingService?.LogDebug($"[GameClassification] ✅ '{name}' -> {category} (match exato)");
                App.LoggingService?.LogExit(nameof(ClassifyGame), category);
                return category;
            }

            // Match parcial: verificar se o nome do processo CONTÉM algum jogo conhecido
            // (ex: "PlayGTAV.exe" contém "GTAV", "launcher-valorant.exe" contém "valorant")
            var nameLower = name.ToLowerInvariant();
            foreach (var kvp in _gameDatabase)
            {
                if (nameLower.Contains(kvp.Key.ToLowerInvariant()) || kvp.Key.ToLowerInvariant().Contains(nameLower))
                {
                    App.LoggingService?.LogDebug($"[GameClassification] ✅ '{name}' -> {kvp.Value} (match parcial: '{kvp.Key}')");
                    App.LoggingService?.LogExit(nameof(ClassifyGame), kvp.Value);
                    return kvp.Value;
                }
            }

            // Fallback: tentar ler FileDescription do executável se for um caminho completo
            if (processName.Contains(":\\") || processName.Contains("/"))
            {
                try
                {
                    var fileVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(processName);
                    var desc = fileVersion?.FileDescription ?? fileVersion?.ProductName ?? string.Empty;
                    if (!string.IsNullOrEmpty(desc))
                    {
                        var descLower = desc.ToLowerInvariant();
                        foreach (var kvp in _gameDatabase)
                        {
                            if (descLower.Contains(kvp.Key.ToLowerInvariant()))
                            {
                                App.LoggingService?.LogDebug($"[GameClassification] ✅ '{name}' -> {kvp.Value} (match por FileDescription: '{desc}' ~ '{kvp.Key}')");
                                App.LoggingService?.LogExit(nameof(ClassifyGame), kvp.Value);
                                return kvp.Value;
                            }
                        }
                    }
                }
                catch { }
            }

            // Fallback final
            App.LoggingService?.LogDebug($"[GameClassification] ⚠️ '{name}' não encontrado no DB -> General");
            App.LoggingService?.LogExit(nameof(ClassifyGame), GameClassificationCategory.General);
            return GameClassificationCategory.General;
        }

        public IntelligentProfileType GetProfileForCategory(GameClassificationCategory category)
        {
            App.LoggingService?.LogEntry(nameof(GetProfileForCategory), ("category", category));
            var result = category switch
            {
                GameClassificationCategory.Competitive => IntelligentProfileType.GamerCompetitive,
                GameClassificationCategory.Singleplayer => IntelligentProfileType.GamerSinglePlayer,
                GameClassificationCategory.Simulation => IntelligentProfileType.GamerSimulation,
                GameClassificationCategory.MMO => IntelligentProfileType.GamerMMO,
                GameClassificationCategory.Strategy => IntelligentProfileType.GamerStrategy,
                _ => IntelligentProfileType.GeneralBalanced // Para General ou desconhecido
            };
            App.LoggingService?.LogExit(nameof(GetProfileForCategory), result);
            return result;
        }
    }

    public enum GameClassificationCategory
    {
        Competitive,
        Singleplayer,
        Simulation,
        MMO,
        Strategy,
        General // Fallback
    }
}
