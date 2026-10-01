using System.Collections.Generic;

namespace VoltrisOptimizer.Services.Optimization
{
    /// <summary>
    /// Representa um perfil de cena de jogo com suas respectivas configurações de otimização.
    /// </summary>
    public class SceneProfile
    {
        /// <summary>
        /// Identificador único da cena.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Nome amigável da cena.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Descrição detalhada da cena.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Prioridade da cena (quanto maior, mais importante).
        /// </summary>
        public int Priority { get; set; }

        /// <summary>
        /// Indica se esta é uma cena crítica onde o desempenho é essencial.
        /// </summary>
        public bool IsPerformanceCritical { get; set; }

        /// <summary>
        /// Configurações específicas de CPU para esta cena.
        /// </summary>
        public CpuSettings CpuSettings { get; set; } = new CpuSettings();

        /// <summary>
        /// Configurações específicas de GPU para esta cena.
        /// </summary>
        public GpuSettings GpuSettings { get; set; } = new GpuSettings();

        /// <summary>
        /// Configurações específicas de memória para esta cena.
        /// </summary>
        public MemorySettings MemorySettings { get; set; } = new MemorySettings();

        /// <summary>
        /// Outras configurações personalizadas específicas desta cena.
        /// </summary>
        public Dictionary<string, object> CustomSettings { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// Configurações específicas de CPU para uma cena.
    /// </summary>
    public class CpuSettings
    {
        /// <summary>
        /// Frequência mínima da CPU em MHz.
        /// </summary>
        public int MinFrequencyMHz { get; set; } = 800;

        /// <summary>
        /// Frequência máxima da CPU em MHz.
        /// </summary>
        public int MaxFrequencyMHz { get; set; } = 4500;

        /// <summary>
        /// Percentual mínimo de núcleos a serem utilizados.
        /// </summary>
        public int MinCoreUsagePercent { get; set; } = 20;

        /// <summary>
        /// Percentual máximo de núcleos a serem utilizados.
        /// </summary>
        public int MaxCoreUsagePercent { get; set; } = 100;

        /// <summary>
        /// Política de energia da CPU ('performance', 'balanced', 'power_saving').
        /// </summary>
        public string PowerPolicy { get; set; } = "balanced";
    }

    /// <summary>
    /// Configurações específicas de GPU para uma cena.
    /// </summary>
    public class GpuSettings
    {
        /// <summary>
        /// Frequência mínima da GPU em MHz.
        /// </summary>
        public int MinFrequencyMHz { get; set; } = 300;

        /// <summary>
        /// Frequência máxima da GPU em MHz.
        /// </summary>
        public int MaxFrequencyMHz { get; set; } = 2000;

        /// <summary>
        /// Limite de temperatura máxima em graus Celsius.
        /// </summary>
        public int MaxTemperatureCelsius { get; set; } = 85;

        /// <summary>
        /// Modo de escalonamento ('synced', 'unsynced', 'adaptive').
        /// </summary>
        public string FrameTimingMode { get; set; } = "adaptive";

        /// <summary>
        /// Resolução preferida ('native', 'scaled', 'custom').
        /// </summary>
        public string PreferredResolution { get; set; } = "native";
    }

    /// <summary>
    /// Configurações específicas de memória para uma cena.
    /// </summary>
    public class MemorySettings
    {
        /// <summary>
        /// Quantidade mínima de RAM alocada em MB.
        /// </summary>
        public int MinRamMB { get; set; } = 1024;

        /// <summary>
        /// Quantidade máxima de RAM alocada em MB.
        /// </summary>
        public int MaxRamMB { get; set; } = 8192;

        /// <summary>
        /// Percentual mínimo de cache L3 utilizado.
        /// </summary>
        public int MinCacheUsagePercent { get; set; } = 10;

        /// <summary>
        /// Percentual máximo de cache L3 utilizado.
        /// </summary>
        public int MaxCacheUsagePercent { get; set; } = 100;

        /// <summary>
        /// Política de paginação ('aggressive', 'normal', 'conservative').
        /// </summary>
        public string PagingPolicy { get; set; } = "normal";
    }
}
