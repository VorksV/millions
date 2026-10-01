using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Intelligence
{
    /// <summary>
    /// Analisador de padrões de uso do sistema
    /// Detecta padrões reais baseados em métricas históricas
    /// </summary>
    public class UsagePatternAnalyzer
    {
        private readonly ILoggingService _logger;
        private readonly List<OptimizationFeedback> _feedbackHistory = new();

        public UsagePatternAnalyzer(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>Analisa padrões de uso a partir de métricas históricas</summary>
        public async Task<UsageAnalysisResult> AnalyzePatternsAsync(List<SystemMetricsSnapshot> metricsHistory)
        {
            _logger.LogInfo("[PatternAnalyzer] Analisando padrões de uso...");

            var result = new UsageAnalysisResult { Success = true };

            try
            {
                if (metricsHistory.Count < 20)
                {
                    result.ErrorMessage = "Dados insuficientes para análise (mínimo 20 medições)";
                    return result;
                }

                // Detectar diferentes tipos de padrões
                var patterns = new List<UsagePattern>();

                // 1. Padrão de alto uso de CPU
                var highCpuPattern = DetectHighCpuPattern(metricsHistory);
                if (highCpuPattern != null)
                {
                    patterns.Add(highCpuPattern);
                }

                // 2. Padrão de alto uso de memória
                var highMemoryPattern = DetectHighMemoryPattern(metricsHistory);
                if (highMemoryPattern != null)
                {
                    patterns.Add(highMemoryPattern);
                }

                // 3. Padrão de sessões de gaming
                var gamingPattern = DetectGamingPattern(metricsHistory);
                if (gamingPattern != null)
                {
                    patterns.Add(gamingPattern);
                }

                // 4. Padrão de uso em bateria
                var batteryPattern = DetectBatteryPattern(metricsHistory);
                if (batteryPattern != null)
                {
                    patterns.Add(batteryPattern);
                }

                // 5. Padrão de uso intensivo de rede
                var networkPattern = DetectNetworkPattern(metricsHistory);
                if (networkPattern != null)
                {
                    patterns.Add(networkPattern);
                }

                // 6. Padrão de uso intensivo de disco
                var diskPattern = DetectDiskPattern(metricsHistory);
                if (diskPattern != null)
                {
                    patterns.Add(diskPattern);
                }

                result.PatternsDetected = patterns;
                result.Metrics = CalculatePatternMetrics(patterns, metricsHistory);

                _logger.LogSuccess($"[PatternAnalyzer] {patterns.Count} padrões detectados");

                foreach (var pattern in patterns.Take(3))
                {
                    _logger.LogDebug($"[PatternAnalyzer] {pattern.Type}: Confiança {pattern.Confidence:P1}");
                }

                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                _logger.LogError("[PatternAnalyzer] Erro na análise", ex);
                return result;
            }
        }

        /// <summary>Detecta padrões em tempo real para o loop de análise</summary>
        public async Task<List<UsagePattern>> DetectPatternsAsync(List<SystemMetricsSnapshot> recentMetrics)
        {
            return await Task.Run(() =>
            {
                var patterns = new List<UsagePattern>();

                try
                {
                    // Análise rápida das últimas medições
                    var lastHour = recentMetrics.Where(m => (DateTime.UtcNow - m.Timestamp).TotalHours <= 1).ToList();

                    if (lastHour.Count < 10)
                        return patterns;

                    // Detectar picos recentes
                    var cpuSpike = DetectCpuSpike(lastHour);
                    if (cpuSpike != null)
                        patterns.Add(cpuSpike);

                    var memorySpike = DetectMemorySpike(lastHour);
                    if (memorySpike != null)
                        patterns.Add(memorySpike);

                    var recentGaming = DetectRecentGaming(lastHour);
                    if (recentGaming != null)
                        patterns.Add(recentGaming);

                    _logger.LogDebug($"[PatternAnalyzer] {patterns.Count} padrões recentes detectados");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[PatternAnalyzer] Erro na detecção rápida: {ex.Message}");
                }

                return patterns;
            });
        }

        /// <summary>Aprende com feedback das otimizações anteriores</summary>
        public async Task LearnFromFeedbackAsync(OptimizationFeedback feedback)
        {
            await Task.Run(() =>
            {
                try
                {
                    _feedbackHistory.Add(feedback);

                    // Manter apenas 100 feedbacks mais recentes
                    if (_feedbackHistory.Count > 100)
                    {
                        _feedbackHistory.RemoveAt(0);
                    }

                    // Analisar eficácia das recomendações
                    var similarRecommendations = _feedbackHistory.Where(f => f.Recommendation.Type == feedback.Recommendation.Type).ToList();

                    if (similarRecommendations.Count >= 5)
                    {
                        var effectivenessRate = similarRecommendations.Count(f => f.WasEffective) / (double)similarRecommendations.Count;

                        _logger.LogDebug($"[PatternAnalyzer] Taxa de eficácia para {feedback.Recommendation.Type}: {effectivenessRate:P1}");

                        // Ajustar futuras recomendações baseado na eficácia
                        if (effectivenessRate < 0.3)
                        {
                            _logger.LogWarning($"[PatternAnalyzer] Baixa eficácia detectada para {feedback.Recommendation.Type}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[PatternAnalyzer] Erro no aprendizado: {ex.Message}");
                }
            });
        }

        #region Detecção de Padrões Específicos

        private UsagePattern DetectHighCpuPattern(List<SystemMetricsSnapshot> metrics)
        {
            var highCpuReadings = metrics.Where(m => m.CpuUsage > 80).ToList();

            if (highCpuReadings.Count < metrics.Count * 0.1) // Pelo menos 10% das leituras
                return null;

            var confidence = (double)highCpuReadings.Count / metrics.Count;
            var avgCpu = highCpuReadings.Average(m => m.CpuUsage);
            var peakCpu = highCpuReadings.Max(m => m.CpuUsage);

            return new UsagePattern
            {
                Type = PatternType.HighCpuUsage,
                Confidence = Math.Min(confidence * 1.5, 1.0), // Bônus para consistência
                LastDetected = highCpuReadings.OrderByDescending(m => m.Timestamp).First().Timestamp,
                Attributes = new Dictionary<string, object>
                {
                    ["AverageCpu"] = avgCpu,
                    ["PeakCpu"] = peakCpu,
                    ["Frequency"] = highCpuReadings.Count,
                    ["TimeSpan"] = (metrics.Last().Timestamp - metrics.First().Timestamp).TotalHours
                }
            };
        }

        private UsagePattern DetectHighMemoryPattern(List<SystemMetricsSnapshot> metrics)
        {
            var highMemoryReadings = metrics.Where(m => m.MemoryUsage > 85).ToList();

            if (highMemoryReadings.Count < metrics.Count * 0.1)
                return null;

            var confidence = (double)highMemoryReadings.Count / metrics.Count;
            var avgMemory = highMemoryReadings.Average(m => m.MemoryUsage);
            var peakMemory = highMemoryReadings.Max(m => m.MemoryUsage);

            return new UsagePattern
            {
                Type = PatternType.HighMemoryUsage,
                Confidence = Math.Min(confidence * 1.5, 1.0),
                LastDetected = highMemoryReadings.OrderByDescending(m => m.Timestamp).First().Timestamp,
                Attributes = new Dictionary<string, object>
                {
                    ["AverageMemory"] = avgMemory,
                    ["PeakMemory"] = peakMemory,
                    ["Frequency"] = highMemoryReadings.Count
                }
            };
        }

        private UsagePattern DetectGamingPattern(List<SystemMetricsSnapshot> metrics)
        {
            var gamingReadings = metrics.Where(m => m.IsGamingMode).ToList();

            if (gamingReadings.Count < 5) // Pelo menos 5 leituras em modo gaming
                return null;

            var confidence = (double)gamingReadings.Count / metrics.Count;
            var avgCpuDuringGaming = gamingReadings.Average(m => m.CpuUsage);
            var avgMemoryDuringGaming = gamingReadings.Average(m => m.MemoryUsage);

            return new UsagePattern
            {
                Type = PatternType.GamingSession,
                Confidence = confidence,
                LastDetected = gamingReadings.OrderByDescending(m => m.Timestamp).First().Timestamp,
                Attributes = new Dictionary<string, object>
                {
                    ["AverageCpuDuringGaming"] = avgCpuDuringGaming,
                    ["AverageMemoryDuringGaming"] = avgMemoryDuringGaming,
                    ["SessionCount"] = gamingReadings.Count,
                    ["TotalGamingTime"] = gamingReadings.Count * 0.5 // Assumindo medições a cada 30 segundos
                }
            };
        }

        private UsagePattern DetectBatteryPattern(List<SystemMetricsSnapshot> metrics)
        {
            var batteryReadings = metrics.Where(m => m.IsOnBattery).ToList();

            if (batteryReadings.Count < metrics.Count * 0.05) // Pelo menos 5% em bateria
                return null;

            var confidence = (double)batteryReadings.Count / metrics.Count;
            var avgCpuOnBattery = batteryReadings.Average(m => m.CpuUsage);
            var avgMemoryOnBattery = batteryReadings.Average(m => m.MemoryUsage);

            return new UsagePattern
            {
                Type = PatternType.LowBatteryUsage,
                Confidence = confidence,
                LastDetected = batteryReadings.OrderByDescending(m => m.Timestamp).First().Timestamp,
                Attributes = new Dictionary<string, object>
                {
                    ["AverageCpuOnBattery"] = avgCpuOnBattery,
                    ["AverageMemoryOnBattery"] = avgMemoryOnBattery,
                    ["BatteryUsagePercentage"] = confidence * 100
                }
            };
        }

        private UsagePattern DetectNetworkPattern(List<SystemMetricsSnapshot> metrics)
        {
            var networkReadings = metrics.Where(m => m.NetworkLatency > 100 && m.NetworkLatency < 1000).ToList();

            if (networkReadings.Count < metrics.Count * 0.1)
                return null;

            var confidence = (double)networkReadings.Count / metrics.Count;
            var avgLatency = networkReadings.Average(m => m.NetworkLatency);
            var maxLatency = networkReadings.Max(m => m.NetworkLatency);

            return new UsagePattern
            {
                Type = PatternType.NetworkIntensive,
                Confidence = confidence,
                LastDetected = networkReadings.OrderByDescending(m => m.Timestamp).First().Timestamp,
                Attributes = new Dictionary<string, object>
                {
                    ["AverageLatency"] = avgLatency,
                    ["MaxLatency"] = maxLatency,
                    ["Frequency"] = networkReadings.Count
                }
            };
        }

        private UsagePattern DetectDiskPattern(List<SystemMetricsSnapshot> metrics)
        {
            var diskReadings = metrics.Where(m => m.DiskUsage > 70).ToList();

            if (diskReadings.Count < metrics.Count * 0.1)
                return null;

            var confidence = (double)diskReadings.Count / metrics.Count;
            var avgDiskUsage = diskReadings.Average(m => m.DiskUsage);
            var peakDiskUsage = diskReadings.Max(m => m.DiskUsage);

            return new UsagePattern
            {
                Type = PatternType.DiskIntensive,
                Confidence = confidence,
                LastDetected = diskReadings.OrderByDescending(m => m.Timestamp).First().Timestamp,
                Attributes = new Dictionary<string, object>
                {
                    ["AverageDiskUsage"] = avgDiskUsage,
                    ["PeakDiskUsage"] = peakDiskUsage,
                    ["Frequency"] = diskReadings.Count
                }
            };
        }

        // Mtodos para detecção rápida
        private UsagePattern DetectCpuSpike(List<SystemMetricsSnapshot> recentMetrics)
        {
            var avgCpu = recentMetrics.Average(m => m.CpuUsage);
            var maxCpu = recentMetrics.Max(m => m.CpuUsage);

            if (maxCpu > avgCpu * 2 && maxCpu > 80)
            {
                return new UsagePattern
                {
                    Type = PatternType.HighCpuUsage,
                    Confidence = 0.8,
                    LastDetected = DateTime.UtcNow,
                    Attributes = new Dictionary<string, object>
                    {
                        ["SpikeDetected"] = true,
                        ["MaxCpu"] = maxCpu,
                        ["AverageCpu"] = avgCpu
                    }
                };
            }

            return null;
        }

        private UsagePattern DetectMemorySpike(List<SystemMetricsSnapshot> recentMetrics)
        {
            var avgMemory = recentMetrics.Average(m => m.MemoryUsage);
            var maxMemory = recentMetrics.Max(m => m.MemoryUsage);

            if (maxMemory > avgMemory * 1.5 && maxMemory > 85)
            {
                return new UsagePattern
                {
                    Type = PatternType.HighMemoryUsage,
                    Confidence = 0.8,
                    LastDetected = DateTime.UtcNow,
                    Attributes = new Dictionary<string, object>
                    {
                        ["SpikeDetected"] = true,
                        ["MaxMemory"] = maxMemory,
                        ["AverageMemory"] = avgMemory
                    }
                };
            }

            return null;
        }

        private UsagePattern DetectRecentGaming(List<SystemMetricsSnapshot> recentMetrics)
        {
            var gamingMetrics = recentMetrics.Where(m => m.IsGamingMode).ToList();

            if (gamingMetrics.Any())
            {
                return new UsagePattern
                {
                    Type = PatternType.GamingSession,
                    Confidence = 0.9,
                    LastDetected = gamingMetrics.Max(m => m.Timestamp),
                    Attributes = new Dictionary<string, object>
                    {
                        ["ActiveGaming"] = true,
                        ["Duration"] = gamingMetrics.Count * 0.5
                    }
                };
            }

            return null;
        }

        #endregion

        private Dictionary<string, double> CalculatePatternMetrics(List<UsagePattern> patterns, List<SystemMetricsSnapshot> metrics)
        {
            var metricsDict = new Dictionary<string, double>
            {
                ["PatternDensity"] = patterns.Count / (double)metrics.Count,
                ["AverageConfidence"] = patterns.Any() ? patterns.Average(p => p.Confidence) : 0,
                ["HighConfidencePatterns"] = patterns.Count(p => p.Confidence > 0.7),
                ["RecentPatterns"] = patterns.Count(p => (DateTime.UtcNow - p.LastDetected).TotalHours <= 1)
            };

            return metricsDict;
        }
    }
}
