using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace VoltrisOptimizer.Services.Gamer.Data
{
    public static class GameSignatureBank
    {
        public static readonly Dictionary<string, GameSignature> KnownSignatures = new(StringComparer.OrdinalIgnoreCase)
        {
            { "cs2", new GameSignature { DisplayName = "Counter-Strike 2", Publisher = "Valve", Engine = "Source 2", Platform = "Steam", Category = GameCategory.EsportsFps } },
            { "csgo", new GameSignature { DisplayName = "Counter-Strike: Global Offensive", Publisher = "Valve", Engine = "Source", Platform = "Steam", Category = GameCategory.EsportsFps } },
            { "dota2", new GameSignature { DisplayName = "Dota 2", Publisher = "Valve", Engine = "Source 2", Platform = "Steam", Category = GameCategory.Moba } },
            { "valorant-win64-shipping", new GameSignature { DisplayName = "Valorant", Publisher = "Riot Games", Engine = "Unreal Engine 4", Platform = "Riot", Category = GameCategory.EsportsFps } },
            { "valorant", new GameSignature { DisplayName = "Valorant", Publisher = "Riot Games", Engine = "Unreal Engine 4", Platform = "Riot", Category = GameCategory.EsportsFps } },
            { "fortniteclient-win64-shipping", new GameSignature { DisplayName = "Fortnite", Publisher = "Epic Games", Engine = "Unreal Engine 5", Platform = "EpicGames", Category = GameCategory.BattleRoyale } },
            { "fortnite", new GameSignature { DisplayName = "Fortnite", Publisher = "Epic Games", Engine = "Unreal Engine 5", Platform = "EpicGames", Category = GameCategory.BattleRoyale } },
            { "r5apex", new GameSignature { DisplayName = "Apex Legends", Publisher = "Respawn Entertainment", Engine = "Source", Platform = "EA", Category = GameCategory.BattleRoyale } },
            { "overwatch", new GameSignature { DisplayName = "Overwatch 2", Publisher = "Blizzard Entertainment", Engine = "Titan", Platform = "Blizzard", Category = GameCategory.EsportsFps } },
            { "overwatchlauncher", new GameSignature { DisplayName = "Overwatch 2", Publisher = "Blizzard Entertainment", Engine = "Titan", Platform = "Blizzard", Category = GameCategory.EsportsFps } },
            { "tslgame", new GameSignature { DisplayName = "PUBG: Battlegrounds", Publisher = "KRAFTON", Engine = "Unreal Engine 4", Platform = "Steam", Category = GameCategory.BattleRoyale } },
            { "pubg", new GameSignature { DisplayName = "PUBG: Battlegrounds", Publisher = "KRAFTON", Engine = "Unreal Engine 4", Platform = "Steam", Category = GameCategory.BattleRoyale } },
            { "rocketleague", new GameSignature { DisplayName = "Rocket League", Publisher = "Psyonix", Engine = "Unreal Engine 3", Platform = "EpicGames", Category = GameCategory.Sports } },
            { "rainbowsix", new GameSignature { DisplayName = "Tom Clancy's Rainbow Six Siege", Publisher = "Ubisoft", Engine = "AnvilNext 2.0", Platform = "Ubisoft", Category = GameCategory.EsportsFps } },
            { "r6s", new GameSignature { DisplayName = "Tom Clancy's Rainbow Six Siege", Publisher = "Ubisoft", Engine = "AnvilNext 2.0", Platform = "Ubisoft", Category = GameCategory.EsportsFps } },
            { "rainbowsix_be", new GameSignature { DisplayName = "Tom Clancy's Rainbow Six Siege", Publisher = "Ubisoft", Engine = "AnvilNext 2.0", Platform = "Ubisoft", Category = GameCategory.EsportsFps } },
            { "deadbydaylight-win64-shipping", new GameSignature { DisplayName = "Dead by Daylight", Publisher = "Behaviour Interactive", Engine = "Unreal Engine 4", Platform = "Steam", Category = GameCategory.Horror } },
            { "deadbydaylight", new GameSignature { DisplayName = "Dead by Daylight", Publisher = "Behaviour Interactive", Engine = "Unreal Engine 4", Platform = "Steam", Category = GameCategory.Horror } },
            { "escapefromtarkov", new GameSignature { DisplayName = "Escape from Tarkov", Publisher = "Battlestate Games", Engine = "Unity", Platform = "Standalone", Category = GameCategory.TacticalFps } },
            { "rust", new GameSignature { DisplayName = "Rust", Publisher = "Facepunch Studios", Engine = "Unity", Platform = "Steam", Category = GameCategory.Survival } },
            { "destiny2", new GameSignature { DisplayName = "Destiny 2", Publisher = "Bungie", Engine = "Tiger", Platform = "Steam", Category = GameCategory.LooterShooter } },
            { "warframe", new GameSignature { DisplayName = "Warframe", Publisher = "Digital Extremes", Engine = "Evolution", Platform = "Steam", Category = GameCategory.LooterShooter } },
            { "warframe.x64", new GameSignature { DisplayName = "Warframe", Publisher = "Digital Extremes", Engine = "Evolution", Platform = "Steam", Category = GameCategory.LooterShooter } },
            { "gta5", new GameSignature { DisplayName = "Grand Theft Auto V", Publisher = "Rockstar Games", Engine = "RAGE", Platform = "Rockstar", Category = GameCategory.OpenWorld } },
            { "gtav", new GameSignature { DisplayName = "Grand Theft Auto V", Publisher = "Rockstar Games", Engine = "RAGE", Platform = "Rockstar", Category = GameCategory.OpenWorld } },
            { "rdr2", new GameSignature { DisplayName = "Red Dead Redemption 2", Publisher = "Rockstar Games", Engine = "RAGE", Platform = "Rockstar", Category = GameCategory.OpenWorld } },
            { "witcher3", new GameSignature { DisplayName = "The Witcher 3: Wild Hunt", Publisher = "CD Projekt Red", Engine = "REDengine 3", Platform = "Steam", Category = GameCategory.Rpg } },
            { "cyberpunk2077", new GameSignature { DisplayName = "Cyberpunk 2077", Publisher = "CD Projekt Red", Engine = "REDengine 4", Platform = "Steam", Category = GameCategory.Rpg } },
            { "eldenring", new GameSignature { DisplayName = "Elden Ring", Publisher = "FromSoftware", Engine = "Proprietary", Platform = "Steam", Category = GameCategory.Rpg } },
            { "bg3", new GameSignature { DisplayName = "Baldur's Gate 3", Publisher = "Larian Studios", Engine = "Divinity 4.0", Platform = "Steam", Category = GameCategory.Rpg } },
            { "bg3_dx11", new GameSignature { DisplayName = "Baldur's Gate 3", Publisher = "Larian Studios", Engine = "Divinity 4.0", Platform = "Steam", Category = GameCategory.Rpg } },
            { "starfield", new GameSignature { DisplayName = "Starfield", Publisher = "Bethesda Game Studios", Engine = "Creation Engine 2", Platform = "Xbox", Category = GameCategory.Rpg } },
            { "skyrimse", new GameSignature { DisplayName = "The Elder Scrolls V: Skyrim", Publisher = "Bethesda Game Studios", Engine = "Creation Engine", Platform = "Steam", Category = GameCategory.Rpg } },
            { "fallout4", new GameSignature { DisplayName = "Fallout 4", Publisher = "Bethesda Game Studios", Engine = "Creation Engine", Platform = "Steam", Category = GameCategory.Rpg } },
            { "fallout76", new GameSignature { DisplayName = "Fallout 76", Publisher = "Bethesda Game Studios", Engine = "Creation Engine", Platform = "Steam", Category = GameCategory.Rpg } },
            { "minecraft", new GameSignature { DisplayName = "Minecraft", Publisher = "Mojang Studios", Engine = "LWJGL", Platform = "Standalone", Category = GameCategory.Sandbox } },
            { "leagueclient", new GameSignature { DisplayName = "League of Legends", Publisher = "Riot Games", Engine = "Proprietary", Platform = "Riot", Category = GameCategory.Moba } },
            { "lol", new GameSignature { DisplayName = "League of Legends", Publisher = "Riot Games", Engine = "Proprietary", Platform = "Riot", Category = GameCategory.Moba } },
            { "worldofwarcraft", new GameSignature { DisplayName = "World of Warcraft", Publisher = "Blizzard Entertainment", Engine = "WoW", Platform = "Blizzard", Category = GameCategory.Mmo } },
            { "wow", new GameSignature { DisplayName = "World of Warcraft", Publisher = "Blizzard Entertainment", Engine = "WoW", Platform = "Blizzard", Category = GameCategory.Mmo } },
            { "ffxiv_dx11", new GameSignature { DisplayName = "Final Fantasy XIV", Publisher = "Square Enix", Engine = "Luminous", Platform = "Standalone", Category = GameCategory.Mmo } },
            { "eso64", new GameSignature { DisplayName = "The Elder Scrolls Online", Publisher = "ZeniMax Online Studios", Engine = "Proprietary", Platform = "Steam", Category = GameCategory.Mmo } },
            { "pathofexile", new GameSignature { DisplayName = "Path of Exile", Publisher = "Grinding Gear Games", Engine = "Proprietary", Platform = "Standalone", Category = GameCategory.ActionRpg } },
            { "lostark", new GameSignature { DisplayName = "Lost Ark", Publisher = "Smilegate RPG", Engine = "Unreal Engine 3", Platform = "Steam", Category = GameCategory.Mmo } },
            { "diablo", new GameSignature { DisplayName = "Diablo IV", Publisher = "Blizzard Entertainment", Engine = "Proprietary", Platform = "Blizzard", Category = GameCategory.ActionRpg } },
            { "diabloiv", new GameSignature { DisplayName = "Diablo IV", Publisher = "Blizzard Entertainment", Engine = "Proprietary", Platform = "Blizzard", Category = GameCategory.ActionRpg } },
            { "diablo3", new GameSignature { DisplayName = "Diablo III", Publisher = "Blizzard Entertainment", Engine = "Proprietary", Platform = "Blizzard", Category = GameCategory.ActionRpg } },
            { "hades", new GameSignature { DisplayName = "Hades", Publisher = "Supergiant Games", Engine = "Proprietary", Platform = "Steam", Category = GameCategory.Roguelike } },
            { "hades2", new GameSignature { DisplayName = "Hades II", Publisher = "Supergiant Games", Engine = "Proprietary", Platform = "Steam", Category = GameCategory.Roguelike } },
            { "deadcells", new GameSignature { DisplayName = "Dead Cells", Publisher = "Motion Twin", Engine = "Proprietary", Platform = "Steam", Category = GameCategory.Roguelike } },
            { "hollowknight", new GameSignature { DisplayName = "Hollow Knight", Publisher = "Team Cherry", Engine = "Unity", Platform = "Steam", Category = GameCategory.Metroidvania } },
            { "celeste", new GameSignature { DisplayName = "Celeste", Publisher = "Maddy Makes Games", Engine = "Monocle Engine", Platform = "Steam", Category = GameCategory.Platformer } },
            { "cuphead", new GameSignature { DisplayName = "Cuphead", Publisher = "Studio MDHR", Engine = "Unity", Platform = "Steam", Category = GameCategory.RunAndGun } },
            { "terraria", new GameSignature { DisplayName = "Terraria", Publisher = "Re-Logic", Engine = "XNA", Platform = "Steam", Category = GameCategory.Sandbox } },
            { "stardew valley", new GameSignature { DisplayName = "Stardew Valley", Publisher = "ConcernedApe", Engine = "XNA", Platform = "Steam", Category = GameCategory.Simulation } },
            { "stardewvalley", new GameSignature { DisplayName = "Stardew Valley", Publisher = "ConcernedApe", Engine = "XNA", Platform = "Steam", Category = GameCategory.Simulation } },
            { "palworld-win64-shipping", new GameSignature { DisplayName = "Palworld", Publisher = "Pocketpair", Engine = "Unreal Engine 5", Platform = "Steam", Category = GameCategory.Survival } },
            { "helldivers2", new GameSignature { DisplayName = "Helldivers 2", Publisher = "Arrowhead Game Studios", Engine = "Autodesk Stingray", Platform = "Steam", Category = GameCategory.TacticalFps } },
            { "satisfactory", new GameSignature { DisplayName = "Satisfactory", Publisher = "Coffee Stain Studios", Engine = "Unreal Engine 5", Platform = "Steam", Category = GameCategory.Factory } },
            { "satisfactorygame-win64-shipping", new GameSignature { DisplayName = "Satisfactory", Publisher = "Coffee Stain Studios", Engine = "Unreal Engine 5", Platform = "Steam", Category = GameCategory.Factory } },
            { "factorio", new GameSignature { DisplayName = "Factorio", Publisher = "Wube Software", Engine = "Proprietary", Platform = "Standalone", Category = GameCategory.Factory } },
            { "cities", new GameSignature { DisplayName = "Cities: Skylines", Publisher = "Colossal Order", Engine = "Unity", Platform = "Steam", Category = GameCategory.Simulation } },
            { "citiesskylines", new GameSignature { DisplayName = "Cities: Skylines", Publisher = "Colossal Order", Engine = "Unity", Platform = "Steam", Category = GameCategory.Simulation } },
            { "citiesskylines2", new GameSignature { DisplayName = "Cities: Skylines II", Publisher = "Colossal Order", Engine = "Unity", Platform = "Steam", Category = GameCategory.Simulation } },
            { "citiesskylinesii", new GameSignature { DisplayName = "Cities: Skylines II", Publisher = "Colossal Order", Engine = "Unity", Platform = "Steam", Category = GameCategory.Simulation } },
            { "rimworld", new GameSignature { DisplayName = "RimWorld", Publisher = "Ludeon Studios", Engine = "Unity", Platform = "Steam", Category = GameCategory.Simulation } },
            { "eurotrucks2", new GameSignature { DisplayName = "Euro Truck Simulator 2", Publisher = "SCS Software", Engine = "Prism3D", Platform = "Steam", Category = GameCategory.Simulation } },
            { "ets2", new GameSignature { DisplayName = "Euro Truck Simulator 2", Publisher = "SCS Software", Engine = "Prism3D", Platform = "Steam", Category = GameCategory.Simulation } },
            { "robloxplayerbeta", new GameSignature { DisplayName = "Roblox", Publisher = "Roblox Corporation", Engine = "Roblox", Platform = "Standalone", Category = GameCategory.Sandbox } },
            { "javaw", new GameSignature { DisplayName = "Java Application", Publisher = "Unknown", Engine = "Java", Platform = "Unknown", Category = GameCategory.Unknown } },
            { "fivem", new GameSignature { DisplayName = "FiveM", Publisher = "Cfx.re", Engine = "RAGE", Platform = "Standalone", Category = GameCategory.Multiplayer } },
            { "acvalhalla", new GameSignature { DisplayName = "Assassin's Creed Valhalla", Publisher = "Ubisoft", Engine = "AnvilNext 2.0", Platform = "Ubisoft", Category = GameCategory.OpenWorld } },
            { "acorigins", new GameSignature { DisplayName = "Assassin's Creed Origins", Publisher = "Ubisoft", Engine = "AnvilNext 2.0", Platform = "Ubisoft", Category = GameCategory.OpenWorld } },
            { "acodyssey", new GameSignature { DisplayName = "Assassin's Creed Odyssey", Publisher = "Ubisoft", Engine = "AnvilNext 2.0", Platform = "Ubisoft", Category = GameCategory.OpenWorld } },
            { "godofwar", new GameSignature { DisplayName = "God of War", Publisher = "Sony Interactive Entertainment", Engine = "Proprietary", Platform = "Steam", Category = GameCategory.ActionAdventure } },
            { "horizonzerodawn", new GameSignature { DisplayName = "Horizon Zero Dawn", Publisher = "Guerrilla Games", Engine = "Decima", Platform = "Steam", Category = GameCategory.OpenWorld } },
            { "spiderman", new GameSignature { DisplayName = "Marvel's Spider-Man", Publisher = "Sony Interactive Entertainment", Engine = "Proprietary", Platform = "Steam", Category = GameCategory.ActionAdventure } },
            { "tlou", new GameSignature { DisplayName = "The Last of Us Part I", Publisher = "Naughty Dog", Engine = "Proprietary", Platform = "Steam", Category = GameCategory.ActionAdventure } },
            { "haloinfinite", new GameSignature { DisplayName = "Halo Infinite", Publisher = "343 Industries", Engine = "Slipspace", Platform = "Xbox", Category = GameCategory.EsportsFps } },
            { "hogwartslegacy", new GameSignature { DisplayName = "Hogwarts Legacy", Publisher = "Warner Bros. Games", Engine = "Unreal Engine 4", Platform = "Steam", Category = GameCategory.Rpg } },
            { "doometernal", new GameSignature { DisplayName = "DOOM Eternal", Publisher = "Bethesda", Engine = "id Tech 7", Platform = "Steam", Category = GameCategory.Fps } },
            { "control_dx12", new GameSignature { DisplayName = "Control", Publisher = "Remedy Entertainment", Engine = "Northlight", Platform = "EpicGames", Category = GameCategory.ActionAdventure } },
            { "alanwake2", new GameSignature { DisplayName = "Alan Wake 2", Publisher = "Remedy Entertainment", Engine = "Northlight", Platform = "EpicGames", Category = GameCategory.Horror } },
            { "forzahorizon5", new GameSignature { DisplayName = "Forza Horizon 5", Publisher = "Playground Games", Engine = "ForzaTech", Platform = "Xbox", Category = GameCategory.Racing } },
            { "forzamotorsport", new GameSignature { DisplayName = "Forza Motorsport", Publisher = "Turn 10 Studios", Engine = "ForzaTech", Platform = "Xbox", Category = GameCategory.Racing } },
            { "assettocorsa", new GameSignature { DisplayName = "Assetto Corsa", Publisher = "Kunos Simulazioni", Engine = "Proprietary", Platform = "Steam", Category = GameCategory.Racing } },
            { "acc", new GameSignature { DisplayName = "Assetto Corsa Competizione", Publisher = "Kunos Simulazioni", Engine = "Unreal Engine 4", Platform = "Steam", Category = GameCategory.Racing } },
            { "f1_23", new GameSignature { DisplayName = "F1 23", Publisher = "EA Sports", Engine = "Ego", Platform = "EA", Category = GameCategory.Racing } },
            { "f1_24", new GameSignature { DisplayName = "F1 24", Publisher = "EA Sports", Engine = "Ego", Platform = "EA", Category = GameCategory.Racing } },
            { "nfsheat", new GameSignature { DisplayName = "Need for Speed Heat", Publisher = "EA", Engine = "Frostbite", Platform = "EA", Category = GameCategory.Racing } },
            { "nfsunbound", new GameSignature { DisplayName = "Need for Speed Unbound", Publisher = "EA", Engine = "Frostbite", Platform = "EA", Category = GameCategory.Racing } },
            { "fc24", new GameSignature { DisplayName = "EA Sports FC 24", Publisher = "EA", Engine = "Frostbite", Platform = "EA", Category = GameCategory.Sports } },
            { "fc25", new GameSignature { DisplayName = "EA Sports FC 25", Publisher = "EA", Engine = "Frostbite", Platform = "EA", Category = GameCategory.Sports } },
            { "nba2k", new GameSignature { DisplayName = "NBA 2K", Publisher = "2K Sports", Engine = "Proprietary", Platform = "Steam", Category = GameCategory.Sports } },
            { "sf6", new GameSignature { DisplayName = "Street Fighter 6", Publisher = "Capcom", Engine = "RE Engine", Platform = "Steam", Category = GameCategory.Fighting } },
            { "tekken8", new GameSignature { DisplayName = "Tekken 8", Publisher = "Bandai Namco", Engine = "Unreal Engine 5", Platform = "Steam", Category = GameCategory.Fighting } },
            { "mk12", new GameSignature { DisplayName = "Mortal Kombat 1", Publisher = "NetherRealm Studios", Engine = "Unreal Engine 4", Platform = "Steam", Category = GameCategory.Fighting } },
            { "genshinimpact", new GameSignature { DisplayName = "Genshin Impact", Publisher = "miHoYo", Engine = "Unity", Platform = "Standalone", Category = GameCategory.Gacha } },
            { "starrail", new GameSignature { DisplayName = "Honkai: Star Rail", Publisher = "miHoYo", Engine = "Unity", Platform = "Standalone", Category = GameCategory.Gacha } },
            { "zzz", new GameSignature { DisplayName = "Zenless Zone Zero", Publisher = "miHoYo", Engine = "Unity", Platform = "Standalone", Category = GameCategory.Gacha } },
            { "wutheringwaves", new GameSignature { DisplayName = "Wuthering Waves", Publisher = "Kuro Games", Engine = "Unreal Engine 4", Platform = "Standalone", Category = GameCategory.Gacha } },
            { "helldivers", new GameSignature { DisplayName = "Helldivers", Publisher = "Arrowhead Game Studios", Engine = "Autodesk Stingray", Platform = "Steam", Category = GameCategory.TacticalFps } },
            { "lethal company", new GameSignature { DisplayName = "Lethal Company", Publisher = "Zeekerss", Engine = "Unity", Platform = "Steam", Category = GameCategory.Horror } },
            { "lethalcompany", new GameSignature { DisplayName = "Lethal Company", Publisher = "Zeekerss", Engine = "Unity", Platform = "Steam", Category = GameCategory.Horror } },
            { "phasmophobia", new GameSignature { DisplayName = "Phasmophobia", Publisher = "Kinetic Games", Engine = "Unity", Platform = "Steam", Category = GameCategory.Horror } },
            { "sonsoftheforest", new GameSignature { DisplayName = "Sons of the Forest", Publisher = "Endnight Games", Engine = "Unity", Platform = "Steam", Category = GameCategory.Survival } },
            { "theforest", new GameSignature { DisplayName = "The Forest", Publisher = "Endnight Games", Engine = "Unity", Platform = "Steam", Category = GameCategory.Survival } },
            { "vampiresurvivors", new GameSignature { DisplayName = "Vampire Survivors", Publisher = "poncle", Engine = "GameMaker", Platform = "Steam", Category = GameCategory.Roguelike } },
            { "riskofrain2", new GameSignature { DisplayName = "Risk of Rain 2", Publisher = "Hopoo Games", Engine = "Unity", Platform = "Steam", Category = GameCategory.Roguelike } },
            { "deeprockgalactic", new GameSignature { DisplayName = "Deep Rock Galactic", Publisher = "Ghost Ship Games", Engine = "Unreal Engine 4", Platform = "Steam", Category = GameCategory.TacticalFps } },
            { "grounded", new GameSignature { DisplayName = "Grounded", Publisher = "Obsidian Entertainment", Engine = "Unreal Engine 4", Platform = "Xbox", Category = GameCategory.Survival } },
            { "valheim", new GameSignature { DisplayName = "Valheim", Publisher = "Iron Gate AB", Engine = "Unity", Platform = "Steam", Category = GameCategory.Survival } },
            { "projectzomboid", new GameSignature { DisplayName = "Project Zomboid", Publisher = "The Indie Stone", Engine = "Proprietary", Platform = "Steam", Category = GameCategory.Survival } },
            { "7daystodie", new GameSignature { DisplayName = "7 Days to Die", Publisher = "The Fun Pimps", Engine = "Unity", Platform = "Steam", Category = GameCategory.Survival } },
            { "7d2d", new GameSignature { DisplayName = "7 Days to Die", Publisher = "The Fun Pimps", Engine = "Unity", Platform = "Steam", Category = GameCategory.Survival } },
            { "starsector", new GameSignature { DisplayName = "Starsector", Publisher = "Fractal Softworks", Engine = "Proprietary", Platform = "Standalone", Category = GameCategory.Simulation } },
            { "x4", new GameSignature { DisplayName = "X4: Foundations", Publisher = "Egosoft", Engine = "Proprietary", Platform = "Steam", Category = GameCategory.Simulation } },
            { "kerbalspaceprogram", new GameSignature { DisplayName = "Kerbal Space Program", Publisher = "Private Division", Engine = "Unity", Platform = "Steam", Category = GameCategory.Simulation } },
            { "outerwilds", new GameSignature { DisplayName = "Outer Wilds", Publisher = "Annapurna Interactive", Engine = "Unity", Platform = "Steam", Category = GameCategory.Adventure } },
            { "subnautica", new GameSignature { DisplayName = "Subnautica", Publisher = "Unknown Worlds", Engine = "Unity", Platform = "Steam", Category = GameCategory.Survival } },
            { "discoelysium", new GameSignature { DisplayName = "Disco Elysium", Publisher = "ZA/UM", Engine = "Unity", Platform = "Steam", Category = GameCategory.Rpg } },
            { "silksong", new GameSignature { DisplayName = "Hollow Knight: Silksong", Publisher = "Team Cherry", Engine = "Unity", Platform = "Steam", Category = GameCategory.Metroidvania } },
            { "deadisland2", new GameSignature { DisplayName = "Dead Island 2", Publisher = "Deep Silver", Engine = "Unreal Engine 4", Platform = "Steam", Category = GameCategory.ActionAdventure } },
            { "dragonageveilguard", new GameSignature { DisplayName = "Dragon Age: The Veilguard", Publisher = "BioWare", Engine = "Frostbite", Platform = "EA", Category = GameCategory.Rpg } },
            { "kingdomcome2", new GameSignature { DisplayName = "Kingdom Come: Deliverance II", Publisher = "Warhorse Studios", Engine = "CryEngine", Platform = "Steam", Category = GameCategory.Rpg } },
            { "stalker2", new GameSignature { DisplayName = "S.T.A.L.K.E.R. 2", Publisher = "GSC Game World", Engine = "Unreal Engine 5", Platform = "Xbox", Category = GameCategory.TacticalFps } },
            { "starwarsoutlaws", new GameSignature { DisplayName = "Star Wars Outlaws", Publisher = "Ubisoft", Engine = "Snowdrop", Platform = "Ubisoft", Category = GameCategory.OpenWorld } },
            { "avatarfrontiers", new GameSignature { DisplayName = "Avatar: Frontiers of Pandora", Publisher = "Ubisoft", Engine = "Snowdrop", Platform = "Ubisoft", Category = GameCategory.OpenWorld } },
            { "callofduty", new GameSignature { DisplayName = "Call of Duty", Publisher = "Activision", Engine = "IW", Platform = "Steam", Category = GameCategory.Fps } },
            { "blackops6", new GameSignature { DisplayName = "Call of Duty: Black Ops 6", Publisher = "Activision", Engine = "IW", Platform = "Steam", Category = GameCategory.Fps } },
            { "mw3", new GameSignature { DisplayName = "Call of Duty: Modern Warfare III", Publisher = "Activision", Engine = "IW", Platform = "Steam", Category = GameCategory.Fps } },
            { "marvelrivals", new GameSignature { DisplayName = "Marvel Rivals", Publisher = "NetEase Games", Engine = "Unreal Engine 5", Platform = "Steam", Category = GameCategory.EsportsFps } },
            { "main", new GameSignature { DisplayName = "Mu Online", Publisher = "Webzen", Engine = "Proprietary", Platform = "Standalone", Category = GameCategory.Mmo } },
            { "game", new GameSignature { DisplayName = "Combat Arms", Publisher = "VALOFE", Engine = "Lithtech Jupiter", Platform = "Standalone", Category = GameCategory.Fps } },
            { "gta_sa", new GameSignature { DisplayName = "GTA San Andreas", Publisher = "Rockstar", Engine = "RenderWare", Platform = "Standalone", Category = GameCategory.OpenWorld } },
            { "gtasanandreas", new GameSignature { DisplayName = "GTA San Andreas", Publisher = "Rockstar", Engine = "RenderWare", Platform = "Standalone", Category = GameCategory.OpenWorld } }
        };

        public static readonly HashSet<string> EngineIndicators = new(StringComparer.OrdinalIgnoreCase)
        {
            "unreal", "unity", "frostbite", "rage", "source", "idtech", "id tech",
            "cryengine", "gamebryo", "creation", "snowdrop", "decima", "northlight",
            "anvil", "redengine", "luminous", "stingray", "ego", "forzatech",
            "iw", "slipspace", "tiger", "evolution", "lwjgl", "monocle",
            "prism3d", "robocraft", "xna", "game maker", "gamemaker", "unityplayer",
            "ue4", "ue5", "ue3", "unreal engine 4", "unreal engine 5"
        };

        public static readonly HashSet<string> KnownPublishers = new(StringComparer.OrdinalIgnoreCase)
        {
            "valve", "ubisoft", "ea", "activision", "blizzard", "rockstar",
            "bethesda", "square enix", "capcom", "bandai namco", "sega",
            "sony", "microsoft", "nintendo", "epic games", "riot games",
            "cd projekt", "warner bros", "2k", "deep silver", "thq",
            "paradox", "coffee stain", "mihoyo", "netease", "kuro",
            "mojang", "re-logic", "team cherry", "supergiant",
            "motion twin", "poncle", "larian", "fromsoftware",
            "arrowhead", "behaviour", "ghost ship", "obsidian",
            "fun pimps", "iron gate", "endnight", "zeekerss",
            "kinetic", "concernedape", "colossal order", "ludeon",
            "scs software", "kunos", "playground", "turn 10",
            "bungie", "digital extremes", "battlestate", "facepunch"
        };

        public static readonly HashSet<string> PlatformStorePaths = new(StringComparer.OrdinalIgnoreCase)
        {
            "steamapps", "epic games", "gog galaxy", "ubisoft",
            "origin", "eadesktop", "battle.net", "riot games",
            "xboxgames", "windowsapps", "microsoft store"
        };

        public static readonly Dictionary<string, string> PlatformNameByFolder = new(StringComparer.OrdinalIgnoreCase)
        {
            { "steamapps", "Steam" },
            { "steam", "Steam" },
            { "epic games", "Epic Games" },
            { "gog galaxy", "GOG" },
            { "ubisoft", "Ubisoft" },
            { "eadesktop", "EA" },
            { "origin", "EA" },
            { "battle.net", "Blizzard" },
            { "riot games", "Riot" },
            { "xboxgames", "Xbox" },
            { "windowsapps", "Microsoft Store" }
        };

        public static readonly HashSet<string> LauncherProcessNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "steam", "steamwebhelper", "steamservice",
            "epicgameslauncher", "epicwebhelper",
            "galaxyclient", "origin", "eadesktop",
            "ubisoftconnect", "ubisoft",
            "battlenet", "riotclient",
            "riotclientservices", "riotclientux",
            "xboxapp", "gamingservices",
            "discord", "discordcanary", "discordptb",
            "gog",
            "minecraftlauncher",
            "robloxplayerbeta", "robloxstudio"
        };

        public static readonly ExclusionCategory[] ExclusionRules = new[]
        {
            new ExclusionCategory
            {
                Name = "SystemTools",
                Priority = 100,
                ProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "cmd", "powershell", "conhost", "taskmgr", "explorer",
                    "regedit", "notepad", "calc", "mspaint", "snippingtool",
                    "control", "mmc", "msconfig", "msinfo32",
                    "netsh", "powercfg", "bcdedit", "reg", "fsutil",
                    "ipconfig", "ping", "tracert", "nslookup", "arp",
                    "sc", "net", "whoami", "systeminfo",
                    "wmic", "cscript", "wscript", "rundll32",
                    "chkdsk", "diskpart", "cleanmgr",
                    "logman", "perfmon", "typeperf", "resmon",
                    "shutdown", "logoff", "msg", "schtasks",
                    "findstr", "sort", "more", "tree", "where",
                    "timeout", "choice", "waitfor",
                    "winver", "appwiz", "consent"
                },
                PathPatterns = null
            },
            new ExclusionCategory
            {
                Name = "Browsers",
                Priority = 99,
                ProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "chrome", "firefox", "msedge", "msedgewebview2",
                    "msedgecp", "opera", "brave", "vivaldi",
                    "iexplore", "iexplorer",
                    "microsoftedgecp", "microsoftedgeupdate",
                    "edgeupdate", "edgewebview", "edgewebview2",
                    "firefoxesr", "waterfox", "librewolf", "tor",
                    "msedgecp.exe", "msedgewebview2.exe",
                    "chromium", "electron", "electronapp",
                    "cef", "cefsharp", "cefsharp.browsersubprocess",
                    "nwjs"
                },
                PathPatterns = new[] { @"\\chrome\\", @"\\firefox\\", @"\\msedge\\", @"\\opera\\", @"\\brave\\" }
            },
            new ExclusionCategory
            {
                Name = "Runtimes",
                Priority = 98,
                ProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "dotnet", "node", "node64", "python", "python3",
                    "pythonw", "java", "javaw", "ruby", "perl",
                    "php", "docker", "vcredist", "dxsetup",
                    "vcredist_x64", "vcredist_x86", "vulkanrt",
                    "vc_redist", "dxwebsetup", "directx",
                    "opencl", "cuda", "cudart"
                },
                PathPatterns = new[] { @"\\dotnet\\", @"\\node_modules\\", @"\\python\\", @"\\jre\\", @"\\jdk\\" }
            },
            new ExclusionCategory
            {
                Name = "Launchers",
                Priority = 97,
                ProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "steam", "steamwebhelper", "steamservice",
                    "epicgameslauncher", "epicwebhelper",
                    "galaxyclient", "origin", "eadesktop",
                    "ubisoftconnect", "battlenet",
                    "riotclient", "xboxapp", "gamingservices",
                    "microsoftstore", "msixpackagingtool",
                    "launcher", "gamelaunch", "gamelauncher",
                    "patcher", "updater", "crashpad_handler",
                    "rebornlauncher", "minecraftlauncher",
                    "overwatchlauncher", "playgtav",
                    "socialclubhelper", "socialclub",
                    "rockstarplatform", "epiconlineservices",
                    "eosoverlayrenderer-win64-shipping"
                },
                PathPatterns = new[] { @"\\launcher", @"\\updater", @"\\crashpad", @"\\patcher" }
            },
            new ExclusionCategory
            {
                Name = "DevelopersTools",
                Priority = 96,
                ProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "devenv", "code", "rider", "clion", "pycharm",
                    "intellij", "webstorm", "goland",
                    "visualstudio", "msbuild", "vstest",
                    "git", "git-remote-https", "git-lfs",
                    "svn", "hg", "curl", "wget",
                    "docker", "docker-compose", "minikube",
                    "kubectl", "helm", "terraform",
                    "node", "npm", "yarn", "pnpm",
                    "dotnet", "dotnet-svc", "nuget",
                    "cmake", "make", "nmake", "mingw32-make",
                    "gcc", "g++", "cl", "clang", "clang++",
                    "windbg", "procexp", "procmon", "dbgview",
                    "fiddler", "postman", "insomnia",
                    "vmware", "virtualbox", "qemu",
                    "vbox", "vagrant",
                    "wsl", "wslhost", "wslservice", "wsl2",
                    "ubuntu", "debian",
                    "cygwin", "mintty", "putty", "kitty",
                    "filezilla", "winpty-agent",
                    "openconsole", "wt", "windowsterminal",
                    "alacritty", "hyper",
                    "rg", "fd", "fzf", "bat", "delta",
                    "trae", "qoder", "cursor", "windsurf",
                    "copilot", "github",
                    "obsidian", "logseq", "notion",
                    "cmder", "conemu", "far", "totalcmd",
                    "everything", "wox", "flowlauncher",
                    "greenshot", "sharex", "screenpresso",
                    "antigravity"
                },
                PathPatterns = new[] { @"\\Program Files\\Microsoft Visual Studio", @"\\Program Files (x86)\\Microsoft Visual Studio" }
            },
            new ExclusionCategory
            {
                Name = "Services",
                Priority = 95,
                ProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "svchost", "csrss", "services", "lsass",
                    "wininit", "winlogon", "smss", "system",
                    "spoolsv", "audiodg", "fontdrvhost",
                    "sihost", "ctfmon", "searchindexer",
                    "ngentask", "ngen",
                    "dllhost", "taskhostw", "taskhost",
                    "w32time", "wudfhost", "runtimebroker",
                    "backgroundtransferhost", "compattelrunner",
                    "wuauclt", "musnotificationux", "werfault",
                    "sppsvc", "wmiapsrv", "mousocoreworker",
                    "backgroundtaskhost",
                    "samsungsystemsupportosd", "softlandingtask",
                    "kiro", "widgets",
                    "startmenuexperiencehost",
                    "searchapp", "searchui",
                    "shellexperiencehost",
                    "systemsettings",
                    "lockapp", "windowsinternal",
                    "securityhealthservice",
                    "securityhealthsystray",
                    "windowsdefender", "mssense",
                    "trustedinstaller", "msiexec",
                    "setup", "installer", "wusa", "dism", "sfc"
                },
                PathPatterns = null
            },
            new ExclusionCategory
            {
                Name = "OfficeProductivity",
                Priority = 94,
                ProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "outlook", "winword", "excel", "powerpnt",
                    "onenote", "onenoteim", "teams", "slack",
                    "zoom", "anydesk", "teamviewer", "skype",
                    "discord", "spotify", "telegram", "whatsapp",
                    "signal", "messenger",
                    "obs64", "obs", "xsplit",
                    "adobe", "photoshop", "illustrator",
                    "premiere", "afterfx", "indesign",
                    "acrobat", "acrord32",
                    "libreoffice", "soffice",
                    "wps", "wpp", "et",
                    "thunderbird", "emclient",
                    "keepass", "bitwarden"
                },
                PathPatterns = new[] { @"\\Microsoft Office\\", @"\\Adobe\\" }
            },
            new ExclusionCategory
            {
                Name = "Antivirus",
                Priority = 93,
                ProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "msmpeng", "mssense", "defender",
                    "securityhealthservice", "securityhealthsystray",
                    "norton", "symantec", "mcafee",
                    "kaspersky", "avast", "avg", "bitdefender",
                    "eset", "malwarebytes", "mbam", "mbamtray",
                    "panda", "trendmicro", "sophos",
                    "webroot", "f-secure", "comodo",
                    "samsungsecurity", "samsungknox",
                    "windowsdefender"
                },
                PathPatterns = null
            },
            new ExclusionCategory
            {
                Name = "UpdatersInstallers",
                Priority = 92,
                ProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "setup", "installer", "uninstall", "uninstaller",
                    "updater", "update", "repair", "patch",
                    "msiexec", "wusa", "wuauclt", "wuauclt",
                    "microsoftedgeupdate", "chromeupdater",
                    "firefoxupdater", "adobeupdater",
                    "spotifyupdater", "discordupdater",
                    "googledriveinstaller", "dropboxinstaller"
                },
                PathPatterns = new[] { "setup.exe", "install.exe", "update.exe", "uninstall.exe" }
            }
        };
    }

    public class GameSignature
    {
        public string DisplayName { get; set; } = "";
        public string Publisher { get; set; } = "";
        public string Engine { get; set; } = "";
        public string Platform { get; set; } = "";
        public GameCategory Category { get; set; } = GameCategory.Unknown;
    }

    public enum GameCategory
    {
        Unknown,
        Fps,
        EsportsFps,
        TacticalFps,
        BattleRoyale,
        Moba,
        Mmo,
        Rpg,
        ActionRpg,
        ActionAdventure,
        OpenWorld,
        Survival,
        Horror,
        Roguelike,
        Metroidvania,
        Platformer,
        RunAndGun,
        Sandbox,
        Simulation,
        Racing,
        Sports,
        Fighting,
        Gacha,
        LooterShooter,
        Multiplayer,
        Adventure,
        Factory,
        Strategy,
        Puzzle,
        Rhythm
    }

    public class ExclusionCategory
    {
        public string Name { get; set; } = "";
        public int Priority { get; set; }
        public HashSet<string>? ProcessNames { get; set; }
        public string[]? PathPatterns { get; set; }
    }
}
