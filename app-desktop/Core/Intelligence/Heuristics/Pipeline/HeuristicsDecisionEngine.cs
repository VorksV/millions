using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core.Intelligence.Heuristics.Abstractions;
using VoltrisOptimizer.Core.Intelligence.Heuristics.Models;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Core.Intelligence.Heuristics.Pipeline
{
    public class HeuristicsDecisionEngine : IHeuristicsPipeline
    {
        private readonly IEnumerable<IHeuristicRule> _allRules;
        private readonly ILoggingService _logger;

        public HeuristicsDecisionEngine(IEnumerable<IHeuristicRule> allRules, ILoggingService logger)
        {
            // DI Nativa injeta todas as regras registradas
            _allRules = allRules ?? Enumerable.Empty<IHeuristicRule>();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<OptimizationDecision> EvaluateOptimizationAsync(string targetOptimization, HeuristicsContext context, CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();
            
            // 1. Validação
            if (string.IsNullOrWhiteSpace(targetOptimization))
                throw new ArgumentNullException(nameof(targetOptimization));

            if (context == null || context.HardwareProfile == null)
            {
                _logger.LogWarning($"[Heuristics] Contexto nulo detectado ao avaliar {targetOptimization}. Recusando otimização.");
                return CreateSafeFallback(targetOptimization, "Contexto nulo ou perfil de hardware ausente.");
            }

            // 2. Filtro de Regras
            var targetRules = _allRules.Where(r => r.TargetOptimization.Equals(targetOptimization, StringComparison.OrdinalIgnoreCase) || r.TargetOptimization == "*").ToList();

            if (!targetRules.Any())
            {
                return CreateSafeFallback(targetOptimization, "Nenhuma heurística definida para esta otimização.");
            }

            // 3. Execução das Regras (Podem ser síncronas/assíncronas)
            var ruleResults = new List<RuleResult>(targetRules.Count);
            foreach (var rule in targetRules)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var ruleSw = Stopwatch.StartNew();
                try
                {
                    var result = await rule.EvaluateAsync(context, cancellationToken);
                    
                    var newResult = new RuleResult
                    {
                        RuleName = rule.RuleName,
                        ScoreAdditive = result.ScoreAdditive,
                        ScoreMultiplier = result.ScoreMultiplier,
                        ConfidenceImpact = result.ConfidenceImpact,
                        Reason = result.Reason,
                        IsVeto = result.IsVeto,
                        IsCriticalEnforcement = result.IsCriticalEnforcement,
                        Priority = result.Priority,
                        Severity = result.Severity,
                        Category = result.Category,
                        EvaluationTime = ruleSw.Elapsed
                    };
                    
                    ruleResults.Add(newResult);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[Heuristics] Erro ao executar regra {rule.RuleName}", ex);
                }
            }

            // 4. Agregação dos Resultados & Resolução de Conflitos
            var conflictLog = new List<string>();
            double baseScore = 0;
            double multiplier = 1.0;
            int confidence = 50; // Confiança base

            bool hasVeto = false;
            bool hasEnforcement = false;

            // Priorizamos as regras críticas para resolver conflitos (se houver Veto e Enforcement)
            var sortedRules = ruleResults.OrderByDescending(r => r.Priority).ToList();

            foreach (var r in sortedRules)
            {
                if (r.IsVeto)
                {
                    hasVeto = true;
                    conflictLog.Add($"VETO acionado por {r.RuleName}: {r.Reason}");
                }
                
                if (r.IsCriticalEnforcement)
                {
                    hasEnforcement = true;
                    conflictLog.Add($"ENFORCEMENT acionado por {r.RuleName}: {r.Reason}");
                }

                baseScore += r.ScoreAdditive;
                multiplier *= r.ScoreMultiplier;
                confidence += r.ConfidenceImpact;
            }

            // Confiança nunca passa de 100% nem cai de 0%
            confidence = Math.Clamp(confidence, 0, 100);
            
            // Calculo do Score Final
            double finalScore = baseScore * multiplier;

            // 5. Geração de Recommendation
            DecisionRecommendation finalRecommendation;

            if (hasVeto && hasEnforcement)
            {
                // Conflito crítico: Em caso de dúvida sobre mexer no SO, NUNCA mexe (Princípio do Mínimo Risco)
                conflictLog.Add("Conflito Crítico detectado (Veto vs Enforcement). Aplicando Fallback de Segurança (DoNothing).");
                finalRecommendation = DecisionRecommendation.DoNothing;
                finalScore = 0;
                confidence -= 20; // Perda de confiança pela ambiguidade
            }
            else if (hasVeto)
            {
                finalRecommendation = DecisionRecommendation.DoNothing;
                finalScore = Math.Min(finalScore, 10);
            }
            else if (hasEnforcement)
            {
                finalRecommendation = DecisionRecommendation.PauseTemporarily; // O VOLTRIS sempre prefere pausar a desabilitar permanentemente
                finalScore = Math.Max(finalScore, 80);
            }
            else
            {
                // Heurística matemática normal baseada no score
                if (finalScore < 20)
                    finalRecommendation = DecisionRecommendation.DoNothing;
                else if (finalScore < 50)
                    finalRecommendation = DecisionRecommendation.Monitor;
                else
                    finalRecommendation = DecisionRecommendation.PauseTemporarily;
            }

            // Penalidade por baixa confiança:
            if (confidence < 40 && finalRecommendation == DecisionRecommendation.PauseTemporarily)
            {
                conflictLog.Add("Downgrade de decisão: Confiança muito baixa para aplicar otimização pesada.");
                finalRecommendation = DecisionRecommendation.Monitor;
            }

            sw.Stop();

            // 6. Auditoria (Geração do rastro completo)
            var audit = new DecisionAuditTrail
            {
                TotalEvaluationTime = sw.Elapsed,
                EvaluatedRulesCount = ruleResults.Count,
                ExecutedRules = ruleResults,
                ConflictResolutions = conflictLog,
                // Salvando um dump enxuto do estado para aprendizado de máquina no futuro (Data Mining)
                ExecutionContextSnapshot = GenerateSnapshotForMl(context) 
            };

            // 7. Retorno
            return new OptimizationDecision
            {
                OptimizationTarget = targetOptimization,
                FinalScore = Math.Round(finalScore, 2),
                FinalConfidence = confidence,
                Recommendation = finalRecommendation,
                AuditTrail = audit,
                Timestamp = DateTime.UtcNow
            };
        }

        private OptimizationDecision CreateSafeFallback(string target, string reason)
        {
            return new OptimizationDecision
            {
                OptimizationTarget = target,
                FinalScore = 0,
                FinalConfidence = 0,
                Recommendation = DecisionRecommendation.DoNothing,
                AuditTrail = new DecisionAuditTrail
                {
                    ConflictResolutions = new List<string> { $"Fallback de Segurança: {reason}" }
                }
            };
        }

        private string GenerateSnapshotForMl(HeuristicsContext context)
        {
            try
            {
                // Geramos um DTO raso e veloz
                var mlData = new
                {
                    HasNVMe = context.HardwareProfile?.HasNVMe == true,
                    TotalRAMGB = context.HardwareProfile?.TotalRAMGB ?? 0,
                    CpuLoad = context.Telemetry?.CpuLoad ?? 0,
                    RamUsedGB = context.Telemetry?.SystemRamUsedGB ?? 0
                };
                return JsonSerializer.Serialize(mlData, new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            }
            catch
            {
                return "{}";
            }
        }
    }
}
