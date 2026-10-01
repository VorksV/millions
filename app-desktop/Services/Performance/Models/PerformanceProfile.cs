using System;

namespace VoltrisOptimizer.Services.Performance.Models
{
    /// <summary>
    /// Perfil de performance que define o nível de agressividade das otimizações
    /// Baseado no Perfil Inteligente do sistema
    /// </summary>
    public class PerformanceProfile
    {
        /// <summary>
        /// Nome do perfil
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Nível de agressividade (0-100)
        /// 0 = Desativado, 100 = Máximo desempenho
        /// </summary>
        public int AggressivenessLevel { get; set; }

        /// <summary>
        /// Core Parking: Número mínimo de cores ativos (0-100%)
        /// 0 = Todos os cores podem ser desligados
        /// 100 = Todos os cores sempre ativos
        /// </summary>
        public int CoreParkingMinCores { get; set; }

        /// <summary>
        /// Frequency Scaling: Frequência mínima do processador (0-100%)
        /// 0 = Frequência mínima permitida
        /// 100 = Frequência máxima sempre
        /// </summary>
        public int MinProcessorState { get; set; }

        /// <summary>
        /// Frequency Scaling: Frequência máxima do processador (0-100%)
        /// </summary>
        public int MaxProcessorState { get; set; }

        /// <summary>
        /// Turbo Boost habilitado
        /// </summary>
        public bool TurboBoostEnabled { get; set; }

        /// <summary>
        /// Política de Turbo Boost (0-100)
        /// 0 = Conservador, 100 = Agressivo
        /// </summary>
        public int TurboBoostPolicy { get; set; }

        /// <summary>
        /// Hetero Policy (para CPUs híbridas Intel 12th gen+)
        /// 0 = Automático, 1 = Preferir P-cores, 2 = Preferir E-cores
        /// </summary>
        public int HeteroPolicy { get; set; }

        /// <summary>
        /// Threshold de temperatura para reduzir agressividade (°C)
        /// </summary>
        public double ThermalThrottleThreshold { get; set; }

        /// <summary>
        /// Se true, desativa otimizações quando temperatura crítica
        /// </summary>
        public bool DisableOnThermalAlert { get; set; }

        /// <summary>
        /// Descrição do perfil
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Perfis pré-definidos baseados no Perfil Inteligente
        /// </summary>
        public static class Presets
        {
            /// <summary>
            /// Perfil para GamerCompetitive - Máximo desempenho
            /// </summary>
            public static PerformanceProfile GamerCompetitive => new()
            {
                Name = "Gamer Competitivo",
                AggressivenessLevel = 100,
                CoreParkingMinCores = 100, // Todos os cores sempre ativos
                MinProcessorState = 100, // Frequência máxima sempre
                MaxProcessorState = 100,
                TurboBoostEnabled = true,
                TurboBoostPolicy = 100, // Turbo agressivo
                HeteroPolicy = 1, // Preferir P-cores (performance)
                ThermalThrottleThreshold = 85.0,
                DisableOnThermalAlert = true,
                Description = "Máximo desempenho para jogos competitivos. Todos os cores ativos, frequência máxima, turbo agressivo."
            };

            /// <summary>
            /// Perfil para GamerSinglePlayer - Alto desempenho balanceado
            /// </summary>
            public static PerformanceProfile GamerBalanced => new()
            {
                Name = "Gamer Balanceado",
                AggressivenessLevel = 75,
                CoreParkingMinCores = 50, // 50% dos cores sempre ativos
                MinProcessorState = 75, // 75% da frequência mínima
                MaxProcessorState = 100,
                TurboBoostEnabled = true,
                TurboBoostPolicy = 75,
                HeteroPolicy = 0, // Automático
                ThermalThrottleThreshold = 80.0,
                DisableOnThermalAlert = true,
                Description = "Alto desempenho balanceado para jogos single-player. Bom equilíbrio entre performance e temperatura."
            };

            /// <summary>
            /// Perfil para GeneralBalanced - Moderado
            /// Threshold elevado para 85°C (Tjmax de CPUs modernas = 95-105°C)
            /// 75°C causava throttling constante em hardware moderno
            /// </summary>
            public static PerformanceProfile GeneralBalanced => new()
            {
                Name = "Balanceado Geral",
                AggressivenessLevel = 50,
                CoreParkingMinCores = 0, // Permitir parking
                MinProcessorState = 50, // 50% da frequência
                MaxProcessorState = 100,
                TurboBoostEnabled = true,
                TurboBoostPolicy = 50,
                HeteroPolicy = 0,
                ThermalThrottleThreshold = 85.0,
                DisableOnThermalAlert = true,
                Description = "Desempenho moderado para uso geral. Permite parking de cores e frequência variável."
            };

            /// <summary>
            /// Perfil para WorkOffice - Conservador
            /// </summary>
            public static PerformanceProfile WorkOffice => new()
            {
                Name = "Trabalho/Escritório",
                AggressivenessLevel = 25,
                CoreParkingMinCores = 0,
                MinProcessorState = 5, // Frequência mínima permitida
                MaxProcessorState = 90,
                TurboBoostEnabled = false, // Turbo desativado
                TurboBoostPolicy = 0,
                HeteroPolicy = 2, // Preferir E-cores (eficiência)
                ThermalThrottleThreshold = 70.0,
                DisableOnThermalAlert = true,
                Description = "Conservador para trabalho. Prioriza eficiência energética e baixa temperatura."
            };

            /// <summary>
            /// Perfil para Enterprise - Mínimo ou desativado
            /// </summary>
            public static PerformanceProfile EnterpriseSecure => new()
            {
                Name = "Enterprise Seguro",
                AggressivenessLevel = 0,
                CoreParkingMinCores = 0,
                MinProcessorState = 5,
                MaxProcessorState = 100,
                TurboBoostEnabled = false,
                TurboBoostPolicy = 0,
                HeteroPolicy = 0,
                ThermalThrottleThreshold = 65.0,
                DisableOnThermalAlert = true,
                Description = "Otimizações desativadas. Configurações padrão do Windows para ambientes corporativos."
            };

            /// <summary>
            /// Perfil desativado (estado original do sistema)
            /// </summary>
            public static PerformanceProfile Disabled => new()
            {
                Name = "Desativado",
                AggressivenessLevel = 0,
                CoreParkingMinCores = 0,
                MinProcessorState = 5,
                MaxProcessorState = 100,
                TurboBoostEnabled = true,
                TurboBoostPolicy = 50,
                HeteroPolicy = 0,
                ThermalThrottleThreshold = 90.0,
                DisableOnThermalAlert = false,
                Description = "Otimizações desativadas. Sistema retorna às configurações padrão do Windows."
            };
        }
    }
}
