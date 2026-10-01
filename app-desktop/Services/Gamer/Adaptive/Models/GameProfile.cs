using System;

namespace VoltrisOptimizer.Services.Gamer.Adaptive.Models
{
    /// <summary>
    /// Perfil de jogo detectado
    /// </summary>
    public class GameProfile
    {
        public string Name { get; set; } = "Unknown";
        public string ExecutablePath { get; set; } = string.Empty;
        public string ProcessName { get; set; } = string.Empty;
        public int ProcessId { get; set; }
        
        public GameType Type { get; set; } = GameType.Unknown;
        public GraphicsApi GraphicsApi { get; set; } = GraphicsApi.Unknown;
        public int? Year { get; set; }
        public bool IsMultiThreaded { get; set; } = true; // Assume modern games are multi-threaded
        public bool RequiresInternet { get; set; } = false;
        public bool HasFpsCap { get; set; } = false;
        public int? FpsCapValue { get; set; }
        
        // Launcher detection
        public bool IsLauncher { get; set; } = false;
        public string? ActualGameExecutable { get; set; }
        
        // Confidence score
        public double ConfidenceScore { get; set; } = 0.5; // 0.0 to 1.0
    }

    public enum GameType
    {
        Unknown,
        Competitive,    // CS2, Valorant, LoL, Overwatch
        AAA,            // Cyberpunk, Starfield, RDR2
        Singleplayer,   // Witcher 3, Elden Ring
        Casual,         // Minecraft, Terraria
        Esports,        // Rainbow Six Siege, Apex
        MMORPG,         // WoW, FF14
        Strategy,       // Civ 6, Total War
        Simulation      // Flight Sim, Euro Truck
    }

    public enum GraphicsApi
    {
        Unknown,
        DirectX9,
        DirectX10,
        DirectX11,
        DirectX12,
        Vulkan,
        OpenGL,
        Metal
    }
}
