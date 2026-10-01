namespace VoltrisOptimizer.Services.Gamer.GameCategorization.Models
{
    public enum GameCategory
    {
        Unknown,
        Competitive, // CS2, Valorant, R6 Siege (Foco em Input Lag e 1% Lows)
        AAA,         // Cyberpunk, RDR2 (Foco em Gráficos e Frametime Médio)
        Simulation,  // MSFS, Assetto Corsa (Foco em CPU e Memória)
        Lightweight  // Minecraft Vanilla, Terraria (Foco em Baixo Consumo)
    }

    public enum GameEngineType
    {
        Unknown,
        UnrealEngine4,
        UnrealEngine5,
        Unity,
        Source,
        Source2,
        Frostbite,
        REEngine,
        CreationEngine,
        Decima,
        CryEngine,
        IdTech,
        Custom
    }

    public class GameProfileAnalysis
    {
        public string ExecutablePath { get; set; } = string.Empty;
        public string GameName { get; set; } = string.Empty;
        public GameCategory Category { get; set; } = GameCategory.Unknown;
        public GameEngineType Engine { get; set; } = GameEngineType.Unknown;
        
        /// <summary>
        /// Confiança da detecção (0 a 100).
        /// </summary>
        public int ConfidenceLevel { get; set; }
    }
}
