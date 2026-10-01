using System;
using System.Linq;

namespace VoltrisOptimizer.Services.SystemIntelligenceProfiler
{
    /// <summary>
    /// Índice de risco de drivers do sistema.
    /// </summary>
    public class DriverRiskIndex
    {
        /// <summary>
        /// Momento da última atualização.
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Número total de drivers instalados.
        /// </summary>
        public int TotalDrivers { get; set; }

        /// <summary>
        /// Número de drivers com risco identificado.
        /// </summary>
        public int RiskyDrivers { get; set; }

        /// <summary>
        /// Índice geral de risco do sistema (0.0 a 1.0).
        /// </summary>
        public double OverallRisk { get; set; }

        /// <summary>
        /// Categoria de risco do sistema.
        /// </summary>
        public DriverRiskCategory RiskCategory { get; set; }

        /// <summary>
        /// Percentual de drivers com risco.
        /// </summary>
        public double RiskPercentage => TotalDrivers > 0 ? (double)RiskyDrivers / TotalDrivers : 0.0;

        /// <summary>
        /// Cria uma cópia deste índice de risco.
        /// </summary>
        /// <returns>Cópia do índice de risco.</returns>
        public DriverRiskIndex Clone()
        {
            return new DriverRiskIndex
            {
                Timestamp = Timestamp,
                TotalDrivers = TotalDrivers,
                RiskyDrivers = RiskyDrivers,
                OverallRisk = OverallRisk,
                RiskCategory = RiskCategory
            };
        }

        /// <summary>
        /// Obtém uma descrição textual da categoria de risco.
        /// </summary>
        /// <returns>Descrição da categoria de risco.</returns>
        public string GetRiskCategoryDescription()
        {
            switch (RiskCategory)
            {
                case DriverRiskCategory.Safe:
                    return "Sistema seguro - nenhum driver problemático identificado";
                case DriverRiskCategory.LowRisk:
                    return "Risco baixo - poucos drivers com problemas menores";
                case DriverRiskCategory.ModerateRisk:
                    return "Risco moderado - vários drivers com potenciais problemas";
                case DriverRiskCategory.HighRisk:
                    return "Risco alto - múltiplos drivers problemáticos identificados";
                case DriverRiskCategory.Dangerous:
                    return "Sistema perigoso - drivers críticos com sérios problemas";
                default:
                    return "Categoria de risco desconhecida";
            }
        }

        /// <summary>
        /// Obtém uma recomendação com base no índice de risco.
        /// </summary>
        /// <returns>Recomendação para o sistema.</returns>
        public string GetRecommendation()
        {
            if (OverallRisk < 0.1)
                return "Manter o sistema atualizado e monitorar periodicamente";
            
            if (OverallRisk < 0.3)
                return "Considerar atualização de drivers marcados como problemáticos";
            
            if (OverallRisk < 0.6)
                return "Priorizar atualização de drivers de risco moderado e alto";
            
            if (OverallRisk < 0.8)
                return "Atualizar imediatamente drivers de alto risco e considerar substituição de perigosos";
            
            return "Ação imediata necessária - remover ou substituir drivers perigosos";
        }

        /// <summary>
        /// Obtém uma cor representativa do nível de risco (para uso em UI).
        /// </summary>
        /// <returns>Código hexadecimal da cor.</returns>
        public string GetRiskColor()
        {
            switch (RiskCategory)
            {
                case DriverRiskCategory.Safe:
                    return "#4CAF50"; // Verde
                case DriverRiskCategory.LowRisk:
                    return "#8BC34A"; // Verde claro
                case DriverRiskCategory.ModerateRisk:
                    return "#FFC107"; // Amarelo
                case DriverRiskCategory.HighRisk:
                    return "#FF9800"; // Laranja
                case DriverRiskCategory.Dangerous:
                    return "#F44336"; // Vermelho
                default:
                    return "#9E9E9E"; // Cinza
            }
        }
    }

    /// <summary>
    /// Relatório detalhado de risco do sistema.
    /// </summary>
    public class DriverRiskReport
    {
        /// <summary>
        /// Índice de risco principal.
        /// </summary>
        public DriverRiskIndex RiskIndex { get; set; }

        /// <summary>
        /// Drivers mais problemáticos identificados.
        /// </summary>
        public DriverRiskProfile[] TopRiskyDrivers { get; set; }

        /// <summary>
        /// Distribuição de riscos por categoria.
        /// </summary>
        public RiskDistribution Distribution { get; set; }

        /// <summary>
        /// Histórico de índices de risco.
        /// </summary>
        public DriverRiskIndex[] History { get; set; }

        /// <summary>
        /// Recomendações específicas para cada categoria de risco.
        /// </summary>
        public RiskRecommendations Recommendations { get; set; }
    }

    /// <summary>
    /// Distribuição de riscos por categoria.
    /// </summary>
    public class RiskDistribution
    {
        /// <summary>
        /// Número de drivers seguros.
        /// </summary>
        public int SafeDrivers { get; set; }

        /// <summary>
        /// Número de drivers com risco baixo.
        /// </summary>
        public int LowRiskDrivers { get; set; }

        /// <summary>
        /// Número de drivers com risco moderado.
        /// </summary>
        public int ModerateRiskDrivers { get; set; }

        /// <summary>
        /// Número de drivers com risco alto.
        /// </summary>
        public int HighRiskDrivers { get; set; }

        /// <summary>
        /// Número de drivers perigosos.
        /// </summary>
        public int DangerousDrivers { get; set; }
    }

    /// <summary>
    /// Recomendações específicas por categoria de risco.
    /// </summary>
    public class RiskRecommendations
    {
        /// <summary>
        /// Recomendações para drivers seguros.
        /// </summary>
        public string[] SafeRecommendations { get; set; }

        /// <summary>
        /// Recomendações para drivers com risco baixo.
        /// </summary>
        public string[] LowRiskRecommendations { get; set; }

        /// <summary>
        /// Recomendações para drivers com risco moderado.
        /// </summary>
        public string[] ModerateRiskRecommendations { get; set; }

        /// <summary>
        /// Recomendações para drivers com risco alto.
        /// </summary>
        public string[] HighRiskRecommendations { get; set; }

        /// <summary>
        /// Recomendações para drivers perigosos.
        /// </summary>
        public string[] DangerousRecommendations { get; set; }
    }

    /// <summary>
    /// Serviço para gerar relatórios de risco de drivers.
    /// </summary>
    public class DriverRiskReportingService
    {
        private readonly IDriverRiskMapper _driverRiskMapper;

        /// <summary>
        /// Inicializa uma nova instância de <see cref="DriverRiskReportingService"/>.
        /// </summary>
        /// <param name="driverRiskMapper">Mapeador de riscos de drivers.</param>
        public DriverRiskReportingService(IDriverRiskMapper driverRiskMapper)
        {
            _driverRiskMapper = driverRiskMapper ?? throw new ArgumentNullException(nameof(driverRiskMapper));
            App.LoggingService?.LogTrace("[DRIVER_RISK] Serviço de relatório inicializado");
        }

        /// <summary>
        /// Gera um relatório completo de risco do sistema.
        /// </summary>
        /// <returns>Relatório de risco.</returns>
        public async System.Threading.Tasks.Task<DriverRiskReport> GenerateRiskReportAsync()
        {
            App.LoggingService?.LogInfo("[DRIVER_RISK] Iniciando geração de relatório de risco completo...");
            var riskIndex = await _driverRiskMapper.GetSystemRiskIndexAsync();
            var riskyDrivers = await _driverRiskMapper.ScanDriversAsync();
            
            App.LoggingService?.LogInfo($"[DRIVER_RISK] Scan concluído. Drivers com risco detectados: {riskyDrivers.Count}. Índice geral: {riskIndex.OverallRisk:F1}");
            
            // Ordena os drivers por nível de risco
            var sortedDrivers = new System.Collections.Generic.List<DriverRiskProfile>(riskyDrivers);
            sortedDrivers.Sort((a, b) => b.RiskIndex.CompareTo(a.RiskIndex));
            
            // Pega os top 10 drivers mais problemáticos
            var topRiskyDrivers = sortedDrivers.Take(10).ToArray();
            
            // Calcula a distribuição de riscos
            var distribution = CalculateRiskDistribution(riskyDrivers);
            
            // Gera recomendações
            var recommendations = GenerateRecommendations(riskIndex, riskyDrivers);
            
            App.LoggingService?.LogSuccess($"[DRIVER_RISK] Relatório gerado com sucesso para {riskyDrivers.Count} drivers problemáticos.");
            return new DriverRiskReport
            {
                RiskIndex = riskIndex,
                TopRiskyDrivers = topRiskyDrivers,
                Distribution = distribution,
                History = new DriverRiskIndex[0], // Histórico seria preenchido com dados anteriores
                Recommendations = recommendations
            };
        }

        /// <summary>
        /// Calcula a distribuição de riscos.
        /// </summary>
        /// <param name="riskyDrivers">Lista de drivers com risco.</param>
        /// <returns>Distribuição de riscos.</returns>
        private RiskDistribution CalculateRiskDistribution(System.Collections.Generic.List<DriverRiskProfile> riskyDrivers)
        {
            var distribution = new RiskDistribution();
            
            foreach (var driver in riskyDrivers)
            {
                switch (driver.RiskCategory)
                {
                    case DriverRiskCategory.Safe:
                        distribution.SafeDrivers++;
                        break;
                    case DriverRiskCategory.LowRisk:
                        distribution.LowRiskDrivers++;
                        break;
                    case DriverRiskCategory.ModerateRisk:
                        distribution.ModerateRiskDrivers++;
                        break;
                    case DriverRiskCategory.HighRisk:
                        distribution.HighRiskDrivers++;
                        break;
                    case DriverRiskCategory.Dangerous:
                        distribution.DangerousDrivers++;
                        break;
                }
            }
            
            return distribution;
        }

        /// <summary>
        /// Gera recomendações com base no índice de risco e drivers problemáticos.
        /// </summary>
        /// <param name="riskIndex">Índice de risco.</param>
        /// <param name="riskyDrivers">Lista de drivers com risco.</param>
        /// <returns>Recomendações.</returns>
        private RiskRecommendations GenerateRecommendations(DriverRiskIndex riskIndex, System.Collections.Generic.List<DriverRiskProfile> riskyDrivers)
        {
            var recommendations = new RiskRecommendations();
            
            // Recomendações para drivers seguros
            recommendations.SafeRecommendations = new[]
            {
                "Continuar monitorando periodicamente",
                "Manter atualizações automáticas habilitadas"
            };
            
            // Recomendações para drivers com risco baixo
            recommendations.LowRiskRecommendations = new[]
            {
                "Monitorar comportamento do sistema",
                "Considerar atualização nas próximas manutenções"
            };
            
            // Recomendações para drivers com risco moderado
            recommendations.ModerateRiskRecommendations = new[]
            {
                "Agendar atualização em janela de manutenção",
                "Verificar compatibilidade antes de atualizar"
            };
            
            // Recomendações para drivers com risco alto
            recommendations.HighRiskRecommendations = new[]
            {
                "Priorizar atualização imediata",
                "Preparar plano de rollback",
                "Testar em ambiente isolado primeiro"
            };
            
            // Recomendações para drivers perigosos
            recommendations.DangerousRecommendations = new[]
            {
                "Remover ou substituir imediatamente",
                "Isolar sistema se crítico para operações",
                "Contatar suporte técnico especializado"
            };
            
            return recommendations;
        }
    }
}
