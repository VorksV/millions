using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Optimization
{
    /// <summary>
    /// Motor de decisão para orçamentos de performance.
    /// </summary>
    public class BudgetDecisionEngine
    {
        private readonly BudgetRulesManager _rulesManager;
        private readonly Dictionary<string, BudgetRecommendation> _recommendationsCache;
        private readonly object _lockObject = new object();
        
        private const int CacheExpirationMinutes = 5;

        /// <summary>
        /// Inicializa uma nova instância de <see cref="BudgetDecisionEngine"/>.
        /// </summary>
        /// <param name="rulesManager">Gerenciador de regras de orçamento.</param>
        public BudgetDecisionEngine(BudgetRulesManager rulesManager)
        {
            _rulesManager = rulesManager ?? throw new ArgumentNullException(nameof(rulesManager));
            _recommendationsCache = new Dictionary<string, BudgetRecommendation>();
        }

        /// <summary>
        /// Gera uma recomendação de orçamento para um jogo.
        /// </summary>
        /// <param name="gameInfo">Informações do jogo.</param>
        /// <returns>Recomendação de orçamento.</returns>
        public async Task<BudgetRecommendation> GenerateRecommendationAsync(GameInfo gameInfo)
        {
            if (gameInfo == null)
                throw new ArgumentNullException(nameof(gameInfo));

            // Verifica se temos uma recomendação em cache
            BudgetRecommendation cachedRecommendation;
            lock (_lockObject)
            {
                if (_recommendationsCache.TryGetValue(gameInfo.Id, out cachedRecommendation))
                {
                    // Verifica se a recomendação em cache ainda é válida
                    if (DateTime.UtcNow.Subtract(cachedRecommendation.GeneratedAt).TotalMinutes < CacheExpirationMinutes)
                    {
                        return cachedRecommendation;
                    }
                    else
                    {
                        // Remove a recomendação expirada
                        _recommendationsCache.Remove(gameInfo.Id);
                    }
                }
            }

            // Gera uma nova recomendação
            var budget = _rulesManager.EvaluateRules(gameInfo);
            var systemProfile = await AnalyzeSystemProfileAsync();
            var optimizationOpportunities = await IdentifyOptimizationOpportunitiesAsync(gameInfo, systemProfile);
            
            var recommendation = new BudgetRecommendation
            {
                GameId = gameInfo.Id,
                GameName = gameInfo.Name,
                RecommendedBudget = budget,
                SystemProfile = systemProfile,
                OptimizationOpportunities = optimizationOpportunities,
                ConfidenceLevel = CalculateConfidenceLevel(gameInfo, systemProfile),
                GeneratedAt = DateTime.UtcNow
            };
            
            // Armazena no cache
            lock (_lockObject)
            {
                _recommendationsCache[gameInfo.Id] = recommendation;
            }
            
            return recommendation;
        }

        /// <summary>
        /// Avalia se um orçamento existente ainda é apropriado.
        /// </summary>
        /// <param name="gameId">ID do jogo.</param>
        /// <param name="currentBudget">Orçamento atual.</param>
        /// <returns>Avaliação do orçamento.</returns>
        public async Task<BudgetEvaluation> EvaluateExistingBudgetAsync(string gameId, PerformanceBudget currentBudget)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("O ID do jogo não pode ser vazio.", nameof(gameId));
            
            if (currentBudget == null)
                throw new ArgumentNullException(nameof(currentBudget));

            // Obtém informações do jogo (em uma implementação real, buscaria de uma fonte de dados)
            var gameInfo = await GetGameInfoAsync(gameId);
            var systemProfile = await AnalyzeSystemProfileAsync();
            
            // Avalia se o orçamento atual é apropriado
            var isAppropriate = IsBudgetAppropriate(currentBudget, gameInfo, systemProfile);
            var suggestedAdjustments = SuggestBudgetAdjustments(currentBudget, gameInfo, systemProfile);
            
            return new BudgetEvaluation
            {
                GameId = gameId,
                CurrentBudget = currentBudget,
                IsAppropriate = isAppropriate,
                SuggestedAdjustments = suggestedAdjustments,
                EvaluationReason = GenerateEvaluationReason(isAppropriate, suggestedAdjustments),
                EvaluatedAt = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Analisa o perfil do sistema.
        /// </summary>
        /// <returns>Perfil do sistema.</returns>
        private async Task<SystemProfile> AnalyzeSystemProfileAsync()
        {
            // Em uma implementação real, coletaria informações reais do sistema
            // Por enquanto, retorna um perfil de exemplo
            
            await Task.CompletedTask;
            
            return new SystemProfile
            {
                CpuCores = 8,
                CpuFrequencyGHz = 3.6,
                TotalMemoryGB = 16,
                GpuMemoryGB = 8,
                StorageType = StorageType.SSD,
                SystemAgeYears = 2
            };
        }

        /// <summary>
        /// Identifica oportunidades de otimização.
        /// </summary>
        /// <param name="gameInfo">Informações do jogo.</param>
        /// <param name="systemProfile">Perfil do sistema.</param>
        /// <returns>Lista de oportunidades de otimização.</returns>
        private async Task<List<OptimizationOpportunity>> IdentifyOptimizationOpportunitiesAsync(
            GameInfo gameInfo, SystemProfile systemProfile)
        {
            var opportunities = new List<OptimizationOpportunity>();
            
            // Exemplo de identificação de oportunidades
            if (systemProfile.StorageType == StorageType.HDD)
            {
                opportunities.Add(new OptimizationOpportunity
                {
                    Type = OpportunityType.StorageUpgrade,
                    Description = "Atualizar para SSD pode melhorar significativamente o tempo de carregamento",
                    EstimatedPerformanceGain = 30.0, // 30% de melhoria
                    Priority = OpportunityPriority.High
                });
            }
            
            if (systemProfile.SystemAgeYears > 3)
            {
                opportunities.Add(new OptimizationOpportunity
                {
                    Type = OpportunityType.DriverUpdate,
                    Description = "Atualizar drivers pode melhorar a estabilidade e performance",
                    EstimatedPerformanceGain = 10.0, // 10% de melhoria
                    Priority = OpportunityPriority.Medium
                });
            }
            
            await Task.CompletedTask;
            return opportunities;
        }

        /// <summary>
        /// Calcula o nível de confiança da recomendação.
        /// </summary>
        /// <param name="gameInfo">Informações do jogo.</param>
        /// <param name="systemProfile">Perfil do sistema.</param>
        /// <returns>Nível de confiança (0.0 a 1.0).</returns>
        private double CalculateConfidenceLevel(GameInfo gameInfo, SystemProfile systemProfile)
        {
            double confidence = 0.5; // Base média
            
            // Aumenta confiança se tivermos informações completas
            if (!string.IsNullOrEmpty(gameInfo.Genre))
                confidence += 0.1;
                
            if (gameInfo.GraphicsRequirement != GraphicsRequirement.Low)
                confidence += 0.1;
                
            if (gameInfo.ReleaseYear > 0)
                confidence += 0.1;
                
            // Ajusta com base no perfil do sistema
            if (systemProfile.CpuCores > 4)
                confidence += 0.05;
                
            if (systemProfile.TotalMemoryGB >= 16)
                confidence += 0.05;
                
            return Math.Min(1.0, confidence);
        }

        /// <summary>
        /// Verifica se um orçamento é apropriado.
        /// </summary>
        /// <param name="budget">Orçamento a ser verificado.</param>
        /// <param name="gameInfo">Informações do jogo.</param>
        /// <param name="systemProfile">Perfil do sistema.</param>
        /// <returns>Verdadeiro se o orçamento é apropriado.</returns>
        private bool IsBudgetAppropriate(PerformanceBudget budget, GameInfo gameInfo, SystemProfile systemProfile)
        {
            // Verifica se os limites são razoáveis para o jogo e sistema
            if (budget.CpuLimitPercent < 30 || budget.CpuLimitPercent > 95)
                return false;
                
            if (budget.GpuLimitPercent < 40 || budget.GpuLimitPercent > 95)
                return false;
                
            if (budget.MemoryLimitMB < 512 || budget.MemoryLimitMB > systemProfile.TotalMemoryGB * 1024)
                return false;
                
            return true;
        }

        /// <summary>
        /// Sugere ajustes para um orçamento.
        /// </summary>
        /// <param name="budget">Orçamento atual.</param>
        /// <param name="gameInfo">Informações do jogo.</param>
        /// <param name="systemProfile">Perfil do sistema.</param>
        /// <returns>Lista de ajustes sugeridos.</returns>
        private List<BudgetAdjustmentSuggestion> SuggestBudgetAdjustments(
            PerformanceBudget budget, GameInfo gameInfo, SystemProfile systemProfile)
        {
            var suggestions = new List<BudgetAdjustmentSuggestion>();
            
            // Sugere ajustes com base nas informações
            if (gameInfo.GraphicsRequirement == GraphicsRequirement.VeryHigh && 
                budget.GpuLimitPercent < 80)
            {
                suggestions.Add(new BudgetAdjustmentSuggestion
                {
                    ResourceType = ResourceType.Gpu,
                    SuggestedValue = 85.0,
                    Reason = "Jogo requer gráficos muito altos"
                });
            }
            
            if (systemProfile.TotalMemoryGB >= 16 && budget.MemoryLimitMB < 8192)
            {
                suggestions.Add(new BudgetAdjustmentSuggestion
                {
                    ResourceType = ResourceType.Memory,
                    SuggestedValue = 8192,
                    Reason = "Sistema tem memória suficiente para alocação maior"
                });
            }
            
            return suggestions;
        }

        /// <summary>
        /// Gera o motivo da avaliação.
        /// </summary>
        /// <param name="isAppropriate">Se o orçamento é apropriado.</param>
        /// <param name="adjustments">Ajustes sugeridos.</param>
        /// <returns>Motivo da avaliação.</returns>
        private string GenerateEvaluationReason(bool isAppropriate, List<BudgetAdjustmentSuggestion> adjustments)
        {
            if (isAppropriate && adjustments.Count == 0)
                return "Orçamento apropriado para o jogo e sistema";
                
            if (!isAppropriate)
                return "Orçamento fora dos limites recomendados";
                
            return $"Orçamento apropriado mas com {adjustments.Count} sugestões de otimização";
        }

        /// <summary>
        /// Obtém informações de um jogo.
        /// </summary>
        /// <param name="gameId">ID do jogo.</param>
        /// <returns>Informações do jogo.</returns>
        private async Task<GameInfo> GetGameInfoAsync(string gameId)
        {
            // Em uma implementação real, buscaria de uma fonte de dados
            // Por enquanto, retorna informações de exemplo
            
            await Task.CompletedTask;
            
            return new GameInfo
            {
                Id = gameId,
                Name = "Jogo Exemplo",
                Genre = "Ação",
                GraphicsRequirement = GraphicsRequirement.High,
                ReleaseYear = DateTime.Now.Year - 1,
                IsMultiplayer = true
            };
        }
    }

    /// <summary>
    /// Recomendação de orçamento de performance.
    /// </summary>
    public class BudgetRecommendation
    {
        /// <summary>
        /// ID do jogo.
        /// </summary>
        public string GameId { get; set; }

        /// <summary>
        /// Nome do jogo.
        /// </summary>
        public string GameName { get; set; }

        /// <summary>
        /// Orçamento recomendado.
        /// </summary>
        public PerformanceBudget RecommendedBudget { get; set; }

        /// <summary>
        /// Perfil do sistema.
        /// </summary>
        public SystemProfile SystemProfile { get; set; }

        /// <summary>
        /// Oportunidades de otimização identificadas.
        /// </summary>
        public List<OptimizationOpportunity> OptimizationOpportunities { get; set; } = new List<OptimizationOpportunity>();

        /// <summary>
        /// Nível de confiança da recomendação (0.0 a 1.0).
        /// </summary>
        public double ConfidenceLevel { get; set; }

        /// <summary>
        /// Data e hora de geração da recomendação.
        /// </summary>
        public DateTime GeneratedAt { get; set; }
    }

    /// <summary>
    /// Avaliação de orçamento existente.
    /// </summary>
    public class BudgetEvaluation
    {
        /// <summary>
        /// ID do jogo.
        /// </summary>
        public string GameId { get; set; }

        /// <summary>
        /// Orçamento atual.
        /// </summary>
        public PerformanceBudget CurrentBudget { get; set; }

        /// <summary>
        /// Indica se o orçamento é apropriado.
        /// </summary>
        public bool IsAppropriate { get; set; }

        /// <summary>
        /// Ajustes sugeridos.
        /// </summary>
        public List<BudgetAdjustmentSuggestion> SuggestedAdjustments { get; set; } = new List<BudgetAdjustmentSuggestion>();

        /// <summary>
        /// Motivo da avaliação.
        /// </summary>
        public string EvaluationReason { get; set; }

        /// <summary>
        /// Data e hora da avaliação.
        /// </summary>
        public DateTime EvaluatedAt { get; set; }
    }

    /// <summary>
    /// Sugestão de ajuste de orçamento.
    /// </summary>
    public class BudgetAdjustmentSuggestion
    {
        /// <summary>
        /// Tipo de recurso.
        /// </summary>
        public ResourceType ResourceType { get; set; }

        /// <summary>
        /// Valor sugerido.
        /// </summary>
        public double SuggestedValue { get; set; }

        /// <summary>
        /// Motivo da sugestão.
        /// </summary>
        public string Reason { get; set; }
    }

    /// <summary>
    /// Perfil do sistema.
    /// </summary>
    public class SystemProfile
    {
        /// <summary>
        /// Número de núcleos da CPU.
        /// </summary>
        public int CpuCores { get; set; }

        /// <summary>
        /// Frequência da CPU em GHz.
        /// </summary>
        public double CpuFrequencyGHz { get; set; }

        /// <summary>
        /// Memória total em GB.
        /// </summary>
        public int TotalMemoryGB { get; set; }

        /// <summary>
        /// Memória da GPU em GB.
        /// </summary>
        public int GpuMemoryGB { get; set; }

        /// <summary>
        /// Tipo de armazenamento.
        /// </summary>
        public StorageType StorageType { get; set; }

        /// <summary>
        /// Idade do sistema em anos.
        /// </summary>
        public int SystemAgeYears { get; set; }
    }

    /// <summary>
    /// Tipos de armazenamento.
    /// </summary>
    public enum StorageType
    {
        /// <summary>
        /// HDD.
        /// </summary>
        HDD,

        /// <summary>
        /// SSD.
        /// </summary>
        SSD,

        /// <summary>
        /// NVMe.
        /// </summary>
        NVMe
    }

    /// <summary>
    /// Oportunidade de otimização.
    /// </summary>
    public class OptimizationOpportunity
    {
        /// <summary>
        /// Tipo de oportunidade.
        /// </summary>
        public OpportunityType Type { get; set; }

        /// <summary>
        /// Descrição da oportunidade.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Ganho de performance estimado em porcentagem.
        /// </summary>
        public double EstimatedPerformanceGain { get; set; }

        /// <summary>
        /// Prioridade da oportunidade.
        /// </summary>
        public OpportunityPriority Priority { get; set; }
    }

    /// <summary>
    /// Tipos de oportunidades de otimização.
    /// </summary>
    public enum OpportunityType
    {
        /// <summary>
        /// Atualização de storage.
        /// </summary>
        StorageUpgrade,

        /// <summary>
        /// Atualização de drivers.
        /// </summary>
        DriverUpdate,

        /// <summary>
        /// Otimização de memória.
        /// </summary>
        MemoryOptimization,

        /// <summary>
        /// Configuração de CPU.
        /// </summary>
        CpuConfiguration,

        /// <summary>
        /// Configuração de GPU.
        /// </summary>
        GpuConfiguration
    }

    /// <summary>
    /// Prioridades de oportunidades.
    /// </summary>
    public enum OpportunityPriority
    {
        /// <summary>
        /// Baixa prioridade.
        /// </summary>
        Low,

        /// <summary>
        /// Média prioridade.
        /// </summary>
        Medium,

        /// <summary>
        /// Alta prioridade.
        /// </summary>
        High,

        /// <summary>
        /// Prioridade crítica.
        /// </summary>
        Critical
    }
}
