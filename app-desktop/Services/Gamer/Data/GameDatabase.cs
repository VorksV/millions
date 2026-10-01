using System;
using System.Collections.Generic;

namespace VoltrisOptimizer.Services.Gamer.Data
{
    public static class GameDatabase
    {
        /// <summary>
        /// Nomes de processos conhecidos como jogos (sem .exe)
        /// </summary>
        public static readonly HashSet<string> KnownGames = new(StringComparer.OrdinalIgnoreCase)
        {
            // ----------------------------------------------------------------------
            // ESPORTS, COMPETITIVOS & MULTIPLAYER EM MASSA
            // ----------------------------------------------------------------------
            "csgo", "cs2", "dota2", "lol", "leagueclient", "league of legends", "valorant", "valorant-win64-shipping",
            "fortnite", "fortniteclient-win64-shipping", "apexlegends", "r5apex", "overwatch", "pubg", "tslgame", 
            "rocketleague", "rainbowsix", "rainbowsix_be", "r6s", "fallguys_client", "smite", "paladins", 
            "brawlhalla", "deadbydaylight-win64-shipping", "dbd", "deadbydaylight", "tarkov", "escapefromtarkov",
            "rust", "rustclient", "ark", "shootergame", "destiny2", "warframe", "warframe.x64", "huntgame", "hunt",
            "chivalry2", "chivalry2-win64-shipping", "mordhau", "mordhau-win64-shipping", "hellletloose", "hll",
            "hll-win64-shipping", "insurgency", "insurgencyclient-win64-shipping", "sandstorm", "squad", "squadgame",
            "squadgame-win64-shipping", "postscriptum", "dayz", "dayz_x64", "arma3", "arma3_x64", "scum", 
            "scum-win64-shipping", "plutonium", "warzone", "cod", "modernwarfare", "blackops", "coldwar", "vanguard",
            "sp22-cod", "cod_beta", "cod_ship", "iw8_ship", "iw9_ship", "bocw", "blackops3", "blackops4", "t6mp", 
            "t6zm", "t4mp", "iw3mp", "iw4x", "plutonium-bootstrapper-win32", "h1_mp64", "h2-mod", "titanfall2",
            "battlefrontii", "starwarsbattlefrontii", "bf1", "bfv", "bf2042", "bf4", "bf3", "left4dead2", "l4d2",
            "portal2", "hl2", "bms", "garrysmod", "hl1", "dota", "payday2", "payday3", "payday3client-win64-shipping",
            "halo", "mcc-win64-shipping", "haloinfinite", "seaofthieves", "sotgame", "stateofdecay2", 
            "stateofdecay2-win64-shipping",

            // ----------------------------------------------------------------------
            // RPGs, MMORPGs, MUNDO ABERTO & SOBREVIVÊNCIA
            // ----------------------------------------------------------------------
            "minecraft", "javaw", "gta5", "gtav", "gta5_enhanced", "gta5_enhanced_be", "rdr2", "reddeadredemption2", 
            "witcher3", "cyberpunk2077", "cyberpunk",             "fivem", "gta_sa", "gtasanandreas", "wow", "wowb", "worldofwarcraft", "ffxiv_dx11", "ffxiv",  
            "eso64", "eso", "gw2-64", "gw2", "blackdesert64", "blackdesert", "pathofexile", "pathofexile_x64", 
            "pathofexilesteam", "lostark", "newworld", "albion-online", "eve", "exefile", "runescape", "osclient", 
            "old school runescape", "temtem", "vampiresurvivors", "palworld-win64-shipping", "palworld", "enshrouded", 
            "lethal company", "lethalcompany", "stardew valley", "stardewvalley", "terraria", "robloxplayerbeta", 
            "roblox", "helldivers2", "helldivers", "starfield", "skyrimse", "skyrim", "fallout4", "fallout76", 
            "falloutnv", "fallout3", "oblivion", "projectzomboid", "pz", "7daystodie", "7d2d", "theforest", 
            "sonsoftheforest", "raft", "grounded", "strandeddeep", "greenhell", "icurus", "icarus-win64-shipping",
            "l2", "lineage", "main", "l2.bin", "muraves2", "murave", "bg3", "bg3_dx11", "divinityoriginalsin2", "dos2", 
            "witcher2", "eldenring", "darksoulsiii", "darksoulsremastered", "darksoulsii", "sekiro", "armoredcore6", 
            "remnant2", "remnant2-win64-shipping", "remnant", "conanexiles", "conansandbox", "conansandbox-win64-shipping",

            // ----------------------------------------------------------------------
            // SINGLE-PLAYER, AAA, AÇÃO, AVENTURA & INDIES
            // ----------------------------------------------------------------------
            "acvalhalla", "acorigins", "acodyssey", "acmirage", "farcry3", "farcry4", "farcry5", "farcry6", "watch_dogs", 
            "watch_dogs2", "watchdogslegion", "ghostrecon", "grw", "grb", "thedivision", "thedivision2", "forhonor", 
            "steep", "ridersrepublic", "rainbowsix_vulkan", "trackmania", "jedifallenorder", "jedisurvivor", "masseffect", 
            "masseffect2", "masseffect3", "masseffectandromeda", "masseffectlegendary", "dragonage", "dragonageinquisition", 
            "deadspace", "hades", "hades2", "celeste", "hollowknight", "oriandthewillofthewhisps", "oriandtheblindforest", 
            "cuphead", "deadcells", "spelunky2", "isaac-ng", "entertherungeon", "undertale", "deltarune", "discoelysium", 
            "outerwilds", "subnautica", "subnauticazero", "dontstarve_steam", "dontstarve", "valheim", "dragonsdogma2", 
            "horizonzerodawn", "horizonforbiddenwest", "spiderman", "spiderman_milesmorales", "godofwar", "returnal", 
            "thelastofus", "uncharted4", "daysgone", "ratchetandclank", "ghostofohtsushima", "tlou", "tombbraider", 
            "rottr", "sottr", "hitman", "hitman2", "hitman3", "justcause3", "justcause4", "madmax", "batmanak", "batmanac", 
            "batmanaa", "shadowofmordor", "shadowofwar", "hogwartslegacy", "borderlands", "borderlands2", "borderlands3", 
            "tiny_tinas_wonderlands", "doometernal", "doom", "doom64", "quakechampions", "quake", "wolfneworder", 
            "wolfoldblood", "wolf2", "deathloop", "dishonored", "dishonored2", "prey", "bioshock", "bioshockhd", 
            "bioshock2hd", "bioshockinfinite", "control_dx11", "control_dx12", "alanwake2", "quantumbreak", "yakuza0", 
            "yakuza_kiwami", "yakuza_kiwami_2", "yakuza3", "yakuza4", "yakuza5", "yakuza6", "yakuza_likeadragon", 
            "likeadragon8", "judgment", "lostjudgment", "persona3reload", "persona4golden", "persona5royal", "amnesia", 
            "amnesia_rebirth", "soma", "outlast", "outlast2", "outlasttrials", "phasmophobia", "devour", "demonologist", 
            "pacify", "forewarned", "deadisland", "deadislandgame-win64-shipping",

            // ----------------------------------------------------------------------
            // ESPORTES, CORRIDA, SIMULADORES E ESTRATÉGIA
            // ----------------------------------------------------------------------
            "efootball", "fifa", "nba2k", "fc24", "fc25", "pes2021", "footballmanager2024", "fm24", "madden24", "wwe2k24", 
            "wwe2k23", "nhl", "thecrew2", "thecrew2_be", "thecrew", "thecrew3", "nfs", "nfsheat", "nfsunbound", 
            "needforspeed", "forzahorizon4", "forzahorizon5", "forzamotorsport", "assetto", "acs", "assettocorsacompetizione", 
            "acc", "dirtrally2", "f1_22", "f1_23", "f1_24", "ams2", "iracing", "iracingsim64dx11", "flight_simulator", 
            "flightsimulator", "sims4", "ts4_x64", "civ5", "civilizationv", "civ6", "civilizationvi", "stellaris", "hoi4", 
            "eu4", "ck3", "crusaderkingsiii", "cities", "citiesskylines", "citiesskylines2", "citiesskylinesii", "anno1800", 
            "rimworld", "factorio", "satisfactory", "satisfactorygame-win64-shipping", "dspgame", "dyson_sphere_program", 
            "frostpunk", "frostpunk2", "planetzoo", "planetcoaster", "aoe2de", "aoe3de", "aoe4", "gears5",

            // ----------------------------------------------------------------------
            // FIGHTING GAMES E GACHAS
            // ----------------------------------------------------------------------
            "sf6", "streetfighter6", "tekken7", "tekken8", "tekken8-win64-shipping", "mk11", "mk12", "guiltygearstrive", 
            "ggst", "dbfighterz", "genshinimpact", "starrail", "bh3", "zzz", "wutheringwaves", "toweroffantasy",

            // ----------------------------------------------------------------------
            // EXTRAS E LEGACY
            // ----------------------------------------------------------------------
            "engine", "game", "counterstrike"
        };

        /// <summary>
        /// Nomes de processos que NÃO são jogos
        /// </summary>
        public static readonly HashSet<string> SystemProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "voltrisoptimizer", "code", "devenv", "explorer", "svchost",
            "csrss", "dwm", "taskmgr", "cmd", "powershell", "conhost",
            "chrome", "firefox", "edge", "msedge", "opera", "brave",
            "discord", "spotify", "epicgameslauncher", "origin",
            "eadesktop", "ubisoftconnect", "galaxyclient", "battlenet",
            "winpty-agent", "openconsole", "rg", "fd", "vsce-sign", "code-tunnel",
            "trae", "qoder", "mmc", "regedit", "notepad",
            "steam", "steamwebhelper", "steamservice", "gameoverlayui", "steamsysinfo", 
            "epicwebhelper", "epicgamesupdater", "epiconlineservicesuserhelper", "epiconlineservicesinstallhelper", "epiconlineservices", "eosoverlayrenderer-win64-shipping",
            "dxsetup", "vcredist", "vcredist_x64", "vcredist_x86",
            "trustedinstaller", "msiexec", "setup", "installer", "wusa", "dism", "sfc",
            "backgroundtransferhost", "runtimebroker", "compattelrunner", "wuauclt",
            "audiodg", "fontdrvhost", "sihost", "ctfmon", "searchindexer", "socialclubhelper",
            "ngentask", "ngen", "mrt", "musnotificationux", "werfault",
            "dllhost", "taskhostw", "taskhost", "w32time", "wudfhost",
            "outlook", "onenote", "excel", "winword", "powerpnt", "teams", "slack",
            "zoom", "anydesk", "teamviewer", "skype", "obs64", "obs",
            "git", "git-remote-https", "git-lfs", "wsl", "wslhost", "wslservice", "sh", "bash", "antigravity",
            // Utilitários do sistema (detectados nos logs como ruído)
            "netsh", "powercfg", "bcdedit", "reg", "defrag", "fsutil",
            "ipconfig", "ping", "tracert", "nslookup", "arp", "route",
            "sc", "net", "net1", "whoami", "hostname", "systeminfo",
            "wmic", "cscript", "wscript", "mshta", "rundll32",
            "robocopy", "xcopy", "attrib", "icacls", "takeown",
            "chkdsk", "diskpart", "mountvol", "cleanmgr",
            "gpupdate", "gpresult", "secedit", "auditpol",
            "certutil", "cipher", "compact", "expand",
            "logman", "perfmon", "typeperf", "resmon",
            "shutdown", "logoff", "msg", "query", "qwinsta",
            "findstr", "sort", "more", "tree", "where",
            "timeout", "choice", "waitfor", "at", "schtasks",
            "msconfig", "msinfo32", "winver", "control", "appwiz",
            "consent",
            // Edge / WebView2 — nunca são jogos, independente do caminho
            "msedgewebview2", "msedge", "msedgecp", "msedgecrashhpad",
            "edgewebview", "edgewebview2", "edgeupdate", "microsoftedge",
            "microsoftedgecp", "microsoftedgeupdate",
            // Runtimes e frameworks embarcados em launchers
            "dotnet", "node", "node64", "electron", "electronapp",
            "nwjs", "cef", "cefsharp", "cefsharp.browsersubprocess",
            "javaw", "java", "python", "python3", "pythonw",
            // Launchers e helpers que NÃO são o jogo em si
            "rebornlauncher", "launcher", "gamelaunch", "gamelauncher",
            "updater", "patcher", "crashpad_handler", "crashreporter",
            "uninstall", "uninstaller", "repair", "playgtav", "minecraftlauncher", "overwatchlauncher",
            // Processos de sistema adicionais observados nos logs
            "samsungsystemsupportosd", "softlandingtask",
            "wmiapsrv", "mousocoreworker", "backgroundtaskhost", "sppsvc",
            "kiro"
        };
    }
}
