using System;
using System.Collections.Generic;
using System.Linq;

namespace VoltrisOptimizer.Services.Optimization
{
    /// <summary>
    /// Gerenciador de regras de orçamento de performance.
    /// </summary>
    public class BudgetRulesManager
    {
        private readonly List<BudgetRule> _rules;
        private readonly object _lockObject = new object();

        /// <summary>
        /// Inicializa uma nova instância de <see cref="BudgetRulesManager"/>.
        /// </summary>
        public BudgetRulesManager()
        {
            _rules = new List<BudgetRule>();
            InitializeDefaultRules();
        }

        /// <summary>
        /// Adiciona uma nova regra de orçamento.
        /// </summary>
        /// <param name="rule">Regra a ser adicionada.</param>
        public void AddRule(BudgetRule rule)
        {
            if (rule == null)
                throw new ArgumentNullException(nameof(rule));

            lock (_lockObject)
            {
                _rules.Add(rule);
            }
        }

        /// <summary>
        /// Remove uma regra de orçamento.
        /// </summary>
        /// <param name="ruleId">ID da regra a ser removida.</param>
        public void RemoveRule(string ruleId)
        {
            if (string.IsNullOrEmpty(ruleId))
                throw new ArgumentException("O ID da regra não pode ser vazio.", nameof(ruleId));

            lock (_lockObject)
            {
                _rules.RemoveAll(r => r.Id == ruleId);
            }
        }

        /// <summary>
        /// Obtém todas as regras ativas.
        /// </summary>
        /// <returns>Lista de regras ativas.</returns>
        public List<BudgetRule> GetActiveRules()
        {
            lock (_lockObject)
            {
                return _rules.Where(r => r.IsActive).ToList();
            }
        }

        /// <summary>
        /// Avalia as regras para um jogo específico e retorna um orçamento sugerido.
        /// </summary>
        /// <param name="gameInfo">Informações do jogo.</param>
        /// <returns>Orçamento sugerido.</returns>
        public PerformanceBudget EvaluateRules(GameInfo gameInfo)
        {
            if (gameInfo == null)
                throw new ArgumentNullException(nameof(gameInfo));

            var applicableRules = GetApplicableRules(gameInfo);
            var budget = CreateBaseBudget(gameInfo);
            
            // Aplica cada regra relevante
            foreach (var rule in applicableRules)
            {
                ApplyRule(budget, rule, gameInfo);
            }
            
            return budget;
        }

        /// <summary>
        /// Obtém as regras aplicáveis para um jogo.
        /// </summary>
        /// <param name="gameInfo">Informações do jogo.</param>
        /// <returns>Lista de regras aplicáveis.</returns>
        private List<BudgetRule> GetApplicableRules(GameInfo gameInfo)
        {
            lock (_lockObject)
            {
                return _rules.Where(r => 
                    r.IsActive && 
                    IsRuleApplicable(r, gameInfo)).ToList();
            }
        }

        /// <summary>
        /// Verifica se uma regra é aplicável para um jogo.
        /// </summary>
        /// <param name="rule">Regra a ser verificada.</param>
        /// <param name="gameInfo">Informações do jogo.</param>
        /// <returns>Verdadeiro se a regra é aplicável.</returns>
        private bool IsRuleApplicable(BudgetRule rule, GameInfo gameInfo)
        {
            // Verifica condições do jogo
            if (rule.GameConditions != null)
            {
                foreach (var condition in rule.GameConditions)
                {
                    if (!EvaluateGameCondition(condition, gameInfo))
                        return false;
                }
            }
            
            // Verifica condições do sistema
            if (rule.SystemConditions != null)
            {
                foreach (var condition in rule.SystemConditions)
                {
                    if (!EvaluateSystemCondition(condition))
                        return false;
                }
            }
            
            return true;
        }

        /// <summary>
        /// Avalia uma condição de jogo.
        /// </summary>
        /// <param name="condition">Condição a ser avaliada.</param>
        /// <param name="gameInfo">Informações do jogo.</param>
        /// <returns>Verdadeiro se a condição é satisfeita.</returns>
        private bool EvaluateGameCondition(GameCondition condition, GameInfo gameInfo)
        {
            switch (condition.Field)
            {
                case GameField.Genre:
                    return CompareStringValues(gameInfo.Genre, condition.Value, condition.Operator);
                    
                case GameField.GraphicsRequirement:
                    if (Enum.TryParse<GraphicsRequirement>(condition.Value, out var req) &&
                        Enum.TryParse<GraphicsRequirement>(gameInfo.GraphicsRequirement.ToString(), out var gameReq))
                    {
                        return CompareNumericValues((int)gameReq, (int)req, condition.Operator);
                    }
                    return false;
                    
                case GameField.ReleaseYear:
                    if (int.TryParse(condition.Value, out var year) &&
                        int.TryParse(gameInfo.ReleaseYear.ToString(), out var gameYear))
                    {
                        return CompareNumericValues(gameYear, year, condition.Operator);
                    }
                    return false;
                    
                case GameField.Multiplayer:
                    if (bool.TryParse(condition.Value, out var multiplayer) &&
                        bool.TryParse(gameInfo.IsMultiplayer.ToString(), out var gameMultiplayer))
                    {
                        return multiplayer == gameMultiplayer;
                    }
                    return false;
                    
                default:
                    return true;
            }
        }

        /// <summary>
        /// Avalia uma condição de sistema.
        /// </summary>
        /// <param name="condition">Condição a ser avaliada.</param>
        /// <returns>Verdadeiro se a condição é satisfeita.</returns>
        private bool EvaluateSystemCondition(SystemCondition condition)
        {
            // Em uma implementação real, obteria informações reais do sistema
            // Por enquanto, retorna verdadeiro para simplificar
            return true;
        }

        /// <summary>
        /// Cria um orçamento base para um jogo.
        /// </summary>
        /// <param name="gameInfo">Informações do jogo.</param>
        /// <returns>Orçamento base.</returns>
        private PerformanceBudget CreateBaseBudget(GameInfo gameInfo)
        {
            return new PerformanceBudget
            {
                GameId = gameInfo.Id,
                GameName = gameInfo.Name,
                CpuLimitPercent = 80.0,
                GpuLimitPercent = 85.0,
                MemoryLimitMB = 4096,
                IoLimitMBps = 100.0,
                PeriodDurationSeconds = 60,
                ExceededActions = new List<BudgetExceededAction> { BudgetExceededAction.Log, BudgetExceededAction.Notify }
            };
        }

        /// <summary>
        /// Aplica uma regra ao orçamento.
        /// </summary>
        /// <param name="budget">Orçamento a ser modificado.</param>
        /// <param name="rule">Regra a ser aplicada.</param>
        /// <param name="gameInfo">Informações do jogo.</param>
        private void ApplyRule(PerformanceBudget budget, BudgetRule rule, GameInfo gameInfo)
        {
            foreach (var adjustment in rule.Adjustments)
            {
                switch (adjustment.ResourceType)
                {
                    case ResourceType.Cpu:
                        budget.CpuLimitPercent = ApplyAdjustment(budget.CpuLimitPercent, adjustment);
                        break;
                        
                    case ResourceType.Gpu:
                        budget.GpuLimitPercent = ApplyAdjustment(budget.GpuLimitPercent, adjustment);
                        break;
                        
                    case ResourceType.Memory:
                        budget.MemoryLimitMB = (long)ApplyAdjustment(budget.MemoryLimitMB, adjustment);
                        break;
                        
                    case ResourceType.Io:
                        budget.IoLimitMBps = ApplyAdjustment(budget.IoLimitMBps, adjustment);
                        break;
                }
            }
            
            // Adiciona ações extras se especificadas
            if (rule.AdditionalActions != null)
            {
                foreach (var action in rule.AdditionalActions)
                {
                    if (!budget.ExceededActions.Contains(action))
                    {
                        budget.ExceededActions.Add(action);
                    }
                }
            }
        }

        /// <summary>
        /// Aplica um ajuste a um valor.
        /// </summary>
        /// <param name="currentValue">Valor atual.</param>
        /// <param name="adjustment">Ajuste a ser aplicado.</param>
        /// <returns>Valor ajustado.</returns>
        private double ApplyAdjustment(double currentValue, ResourceAdjustment adjustment)
        {
            switch (adjustment.Type)
            {
                case AdjustmentType.Absolute:
                    return adjustment.Value;
                    
                case AdjustmentType.Relative:
                    return currentValue * (1 + adjustment.Value / 100);
                    
                case AdjustmentType.Additive:
                    return currentValue + adjustment.Value;
                    
                default:
                    return currentValue;
            }
        }

        /// <summary>
        /// Compara valores numéricos de acordo com o operador.
        /// </summary>
        /// <param name="left">Valor à esquerda.</param>
        /// <param name="right">Valor à direita.</param>
        /// <param name="op">Operador.</param>
        /// <returns>Resultado da comparação.</returns>
        private bool CompareNumericValues(double left, double right, ConditionOperator op)
        {
            switch (op)
            {
                case ConditionOperator.Equals:
                    return Math.Abs(left - right) < 0.001;
                case ConditionOperator.NotEquals:
                    return Math.Abs(left - right) >= 0.001;
                case ConditionOperator.GreaterThan:
                    return left > right;
                case ConditionOperator.LessThan:
                    return left < right;
                case ConditionOperator.GreaterThanOrEqual:
                    return left >= right;
                case ConditionOperator.LessThanOrEqual:
                    return left <= right;
                default:
                    return true;
            }
        }

        /// <summary>
        /// Compara valores de texto de acordo com o operador.
        /// </summary>
        /// <param name="left">Texto à esquerda.</param>
        /// <param name="right">Texto à direita.</param>
        /// <param name="op">Operador.</param>
        /// <returns>Resultado da comparação.</returns>
        private bool CompareStringValues(string left, string right, ConditionOperator op)
        {
            if (left == null) left = string.Empty;
            if (right == null) right = string.Empty;
            
            switch (op)
            {
                case ConditionOperator.Equals:
                    return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
                case ConditionOperator.NotEquals:
                    return !string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
                case ConditionOperator.Contains:
                    return left.IndexOf(right, StringComparison.OrdinalIgnoreCase) >= 0;
                case ConditionOperator.StartsWith:
                    return left.StartsWith(right, StringComparison.OrdinalIgnoreCase);
                case ConditionOperator.EndsWith:
                    return left.EndsWith(right, StringComparison.OrdinalIgnoreCase);
                default:
                    return true;
            }
        }

        /// <summary>
        /// Inicializa regras padrão.
        /// </summary>
        private void InitializeDefaultRules()
        {
            // Regra para jogos AAA modernos
            _rules.Add(new BudgetRule
            {
                Id = "aaa-modern",
                Name = "Jogos AAA Modernos",
                Description = "Orçamento para jogos AAA lançados nos últimos 2 anos",
                IsActive = true,
                GameConditions = new List<GameCondition>
                {
                    new GameCondition
                    {
                        Field = GameField.ReleaseYear,
                        Operator = ConditionOperator.GreaterThanOrEqual,
                        Value = (DateTime.Now.Year - 2).ToString()
                    }
                },
                Adjustments = new List<ResourceAdjustment>
                {
                    new ResourceAdjustment
                    {
                        ResourceType = ResourceType.Cpu,
                        Type = AdjustmentType.Relative,
                        Value = 10.0 // Aumenta 10%
                    },
                    new ResourceAdjustment
                    {
                        ResourceType = ResourceType.Gpu,
                        Type = AdjustmentType.Relative,
                        Value = 15.0 // Aumenta 15%
                    },
                    new ResourceAdjustment
                    {
                        ResourceType = ResourceType.Memory,
                        Type = AdjustmentType.Relative,
                        Value = 25.0 // Aumenta 25%
                    }
                }
            });
            
            // Regra para jogos casuais
            _rules.Add(new BudgetRule
            {
                Id = "casual-games",
                Name = "Jogos Casuais",
                Description = "Orçamento reduzido para jogos casuais",
                IsActive = true,
                GameConditions = new List<GameCondition>
                {
                    new GameCondition
                    {
                        Field = GameField.Genre,
                        Operator = ConditionOperator.Contains,
                        Value = "casual"
                    }
                },
                Adjustments = new List<ResourceAdjustment>
                {
                    new ResourceAdjustment
                    {
                        ResourceType = ResourceType.Cpu,
                        Type = AdjustmentType.Relative,
                        Value = -20.0 // Reduz 20%
                    },
                    new ResourceAdjustment
                    {
                        ResourceType = ResourceType.Gpu,
                        Type = AdjustmentType.Relative,
                        Value = -25.0 // Reduz 25%
                    },
                    new ResourceAdjustment
                    {
                        ResourceType = ResourceType.Memory,
                        Type = AdjustmentType.Relative,
                        Value = -30.0 // Reduz 30%
                    }
                }
            });
        }
    }

    /// <summary>
    /// Regra de orçamento de performance.
    /// </summary>
    public class BudgetRule
    {
        /// <summary>
        /// ID único da regra.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Nome da regra.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Descrição da regra.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Indica se a regra está ativa.
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Condições para aplicação da regra com base em características do jogo.
        /// </summary>
        public List<GameCondition> GameConditions { get; set; } = new List<GameCondition>();

        /// <summary>
        /// Condições para aplicação da regra com base em características do sistema.
        /// </summary>
        public List<SystemCondition> SystemConditions { get; set; } = new List<SystemCondition>();

        /// <summary>
        /// Ajustes a serem aplicados aos recursos.
        /// </summary>
        public List<ResourceAdjustment> Adjustments { get; set; } = new List<ResourceAdjustment>();

        /// <summary>
        /// Ações adicionais a serem tomadas quando a regra é aplicada.
        /// </summary>
        public List<BudgetExceededAction> AdditionalActions { get; set; } = new List<BudgetExceededAction>();
    }

    /// <summary>
    /// Condição para aplicação de regra com base em características do jogo.
    /// </summary>
    public class GameCondition
    {
        /// <summary>
        /// Campo do jogo a ser verificado.
        /// </summary>
        public GameField Field { get; set; }

        /// <summary>
        /// Operador de comparação.
        /// </summary>
        public ConditionOperator Operator { get; set; }

        /// <summary>
        /// Valor para comparação.
        /// </summary>
        public string Value { get; set; }
    }

    /// <summary>
    /// Campos de informações de jogo.
    /// </summary>
    public enum GameField
    {
        /// <summary>
        /// Gênero do jogo.
        /// </summary>
        Genre,

        /// <summary>
        /// Requisitos gráficos.
        /// </summary>
        GraphicsRequirement,

        /// <summary>
        /// Ano de lançamento.
        /// </summary>
        ReleaseYear,

        /// <summary>
        /// Jogo multiplayer.
        /// </summary>
        Multiplayer
    }

    /// <summary>
    /// Condição para aplicação de regra com base em características do sistema.
    /// </summary>
    public class SystemCondition
    {
        /// <summary>
        /// Componente do sistema a ser verificado.
        /// </summary>
        public SystemComponent Component { get; set; }

        /// <summary>
        /// Operador de comparação.
        /// </summary>
        public ConditionOperator Operator { get; set; }

        /// <summary>
        /// Valor para comparação.
        /// </summary>
        public string Value { get; set; }
    }

    /// <summary>
    /// Componentes do sistema.
    /// </summary>
    public enum SystemComponent
    {
        /// <summary>
        /// CPU.
        /// </summary>
        Cpu,

        /// <summary>
        /// GPU.
        /// </summary>
        Gpu,

        /// <summary>
        /// Memória RAM.
        /// </summary>
        Ram,

        /// <summary>
        /// Armazenamento.
        /// </summary>
        Storage
    }

    /// <summary>
    /// Operadores de condição.
    /// </summary>
    public enum ConditionOperator
    {
        /// <summary>
        /// Igual.
        /// </summary>
        Equals,

        /// <summary>
        /// Diferente.
        /// </summary>
        NotEquals,

        /// <summary>
        /// Maior que.
        /// </summary>
        GreaterThan,

        /// <summary>
        /// Menor que.
        /// </summary>
        LessThan,

        /// <summary>
        /// Maior ou igual.
        /// </summary>
        GreaterThanOrEqual,

        /// <summary>
        /// Menor ou igual.
        /// </summary>
        LessThanOrEqual,

        /// <summary>
        /// Contém.
        /// </summary>
        Contains,

        /// <summary>
        /// Começa com.
        /// </summary>
        StartsWith,

        /// <summary>
        /// Termina com.
        /// </summary>
        EndsWith
    }

    /// <summary>
    /// Ajuste de recurso.
    /// </summary>
    public class ResourceAdjustment
    {
        /// <summary>
        /// Tipo de recurso a ser ajustado.
        /// </summary>
        public ResourceType ResourceType { get; set; }

        /// <summary>
        /// Tipo de ajuste.
        /// </summary>
        public AdjustmentType Type { get; set; }

        /// <summary>
        /// Valor do ajuste.
        /// </summary>
        public double Value { get; set; }
    }

    /// <summary>
    /// Tipos de ajuste.
    /// </summary>
    public enum AdjustmentType
    {
        /// <summary>
        /// Valor absoluto.
        /// </summary>
        Absolute,

        /// <summary>
        /// Valor relativo (porcentagem).
        /// </summary>
        Relative,

        /// <summary>
        /// Valor aditivo.
        /// </summary>
        Additive
    }

    /// <summary>
    /// Informações de um jogo.
    /// </summary>
    public class GameInfo
    {
        /// <summary>
        /// ID do jogo.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Nome do jogo.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gênero do jogo.
        /// </summary>
        public string Genre { get; set; }

        /// <summary>
        /// Requisitos gráficos.
        /// </summary>
        public GraphicsRequirement GraphicsRequirement { get; set; }

        /// <summary>
        /// Ano de lançamento.
        /// </summary>
        public int ReleaseYear { get; set; }

        /// <summary>
        /// Indica se é um jogo multiplayer.
        /// </summary>
        public bool IsMultiplayer { get; set; }
    }

    /// <summary>
    /// Níveis de requisitos gráficos.
    /// </summary>
    public enum GraphicsRequirement
    {
        /// <summary>
        /// Baixo.
        /// </summary>
        Low,

        /// <summary>
        /// Médio.
        /// </summary>
        Medium,

        /// <summary>
        /// Alto.
        /// </summary>
        High,

        /// <summary>
        /// Muito alto.
        /// </summary>
        VeryHigh
    }
}
