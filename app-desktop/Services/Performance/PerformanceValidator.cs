using VoltrisOptimizer.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.Logging;

namespace VoltrisOptimizer.Services.Performance
{
    /// <summary>
    /// Validador de performance para provar que as otimizações funcionam
    /// </summary>
    public class PerformanceValidator
    {
        private readonly ILoggingService _logger;
        private readonly Dictionary<string, List<double>> _performanceHistory = new();
        
        public PerformanceValidator(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        /// <summary>
        /// Valida se uma otimização realmente melhorou a performance
        /// </summary>
        public ValidationResult ValidateOptimization(string optimizationType, Action before, Action after)
        {
            var result = new ValidationResult { OptimizationType = optimizationType };
            
            try
            {
                // Medir baseline
                var beforeMetrics = MeasurePerformance(before);
                Thread.Sleep(100); // Pequena pausa para estabilizar
                
                // Executar otimização
                var sw = Stopwatch.StartNew();
                after();
                sw.Stop();
                
                // Medir pós-otimização
                var afterMetrics = MeasurePerformance(() => { });
                
                // Análise comparativa
                result.CpuImprovement = beforeMetrics.AverageCpu - afterMetrics.AverageCpu;
                result.MemoryImprovement = beforeMetrics.AverageMemory - afterMetrics.AverageMemory;
                result.ExecutionTimeMs = sw.ElapsedMilliseconds;
                result.IsImproved = result.CpuImprovement > 1.0 || result.MemoryImprovement > 2.0;
                
                // Salvar histórico
                if (!_performanceHistory.ContainsKey(optimizationType))
                    _performanceHistory[optimizationType] = new List<double>();
                
                _performanceHistory[optimizationType].Add(result.CpuImprovement);
                
                _logger.LogInfo($"[PerformanceValidator] {optimizationType}: CPU {result.CpuImprovement:F1}%, Memory {result.MemoryImprovement:F1}%, Tempo {result.ExecutionTimeMs}ms");
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[PerformanceValidator] Erro ao validar {optimizationType}: {ex.Message}");
                result.Error = ex.Message;
                return result;
            }
        }
        
        private PerformanceMetrics MeasurePerformance(Action action)
        {
            var metrics = new PerformanceMetrics();
            var cpuSamples = new List<double>();
            var memorySamples = new List<double>();
            
            // Coletar amostras por 2 segundos
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 2000)
            {
                try
                {
                    using var cpuCounter = new VoltrisOptimizer.Utils.SafePerformanceCounter("Processor", "% Processor Time", "_Total");
                    using var memoryCounter = new VoltrisOptimizer.Utils.SafePerformanceCounter("Memory", "Available MBytes");
                    
                    cpuSamples.Add(cpuCounter.NextValue());
                    
                    var totalMemory = GC.GetTotalMemory(false) / 1024 / 1024; // MB
                    var availableMemory = memoryCounter.NextValue();
                    var usedMemory = totalMemory > 0 ? ((totalMemory - availableMemory) / totalMemory) * 100 : 0;
                    memorySamples.Add(usedMemory);
                }
                catch
                {
                    // Ignorar erros de performance counters
                }
                
                Thread.Sleep(100);
            }
            
            metrics.AverageCpu = cpuSamples.Any() ? cpuSamples.Average() : 0;
            metrics.AverageMemory = memorySamples.Any() ? memorySamples.Average() : 0;
            metrics.PeakCpu = cpuSamples.Any() ? cpuSamples.Max() : 0;
            metrics.PeakMemory = memorySamples.Any() ? memorySamples.Max() : 0;
            
            return metrics;
        }
        
        /// <summary>
        /// Gera relatório de performance das últimas otimizações
        /// </summary>
        public PerformanceReport GenerateReport()
        {
            var report = new PerformanceReport();
            
            foreach (var kvp in _performanceHistory)
            {
                var optimization = kvp.Key;
                var values = kvp.Value;
                
                if (values.Count > 0)
                {
                    report.Optimizations.Add(new OptimizationResult
                    {
                        Type = optimization,
                        AverageImprovement = values.Average(),
                        BestImprovement = values.Max(),
                        WorstImprovement = values.Min(),
                        TotalApplications = values.Count,
                        SuccessRate = values.Count(v => v > 0) / (double)values.Count * 100
                    });
                }
            }
            
            return report;
        }
    }
    
    public class ValidationResult
    {
        public string OptimizationType { get; set; } = "";
        public double CpuImprovement { get; set; }
        public double MemoryImprovement { get; set; }
        public long ExecutionTimeMs { get; set; }
        public bool IsImproved { get; set; }
        public string? Error { get; set; }
    }
    
    public class PerformanceMetrics
    {
        public double AverageCpu { get; set; }
        public double AverageMemory { get; set; }
        public double PeakCpu { get; set; }
        public double PeakMemory { get; set; }
    }
    
    public class PerformanceReport
    {
        public List<OptimizationResult> Optimizations { get; set; } = new();
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
        
        public string Summary => 
            $"Total: {Optimizations.Count} otimizações, " +
            $"Taxa de sucesso: {Optimizations.Average(o => o.SuccessRate):F1}%, " +
            $"Melhoria média: {Optimizations.Average(o => o.AverageImprovement):F1}% CPU";
    }
    
    public class OptimizationResult
    {
        public string Type { get; set; } = "";
        public double AverageImprovement { get; set; }
        public double BestImprovement { get; set; }
        public double WorstImprovement { get; set; }
        public int TotalApplications { get; set; }
        public double SuccessRate { get; set; }
    }
}


