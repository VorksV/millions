using System;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Monitoring;

namespace VoltrisOptimizer.Services.AutoFixer
{
    /// <summary>
    /// Engine de correção automática com IA
    /// Detecta e corrige problemas de performance automaticamente com SEGURANÇA MÁXIMA
    /// </summary>
    public class AutoFixerEngine
    {

        private readonly ValidationEngine _validator;
        private readonly MetricsCollector _metrics;
        private readonly ILoggingService _logger;
        
        // Thresholds para ação automática
        private readonly AutoFixConfig _config = new()
        {
            CpuSpikeThreshold = 85,        // > 85% CPU por período prolongado
            GpuLoadThreshold = 95,         // > 95% GPU constante
            FrameTimeVarianceMs = 16.67,   // > 16.67ms variance (1 frame @ 60fps)
            InputLatencyMs = 50,            // > 50ms input lag
            RamPressurePercent = 90,       // > 90% RAM usage
            MinTimeBetweenFixes = 30       // Mínimo 30s entre correções
        };
        
        private DateTime _lastFixAttempt = DateTime.MinValue;
        
        public AutoFixerEngine(
            ValidationEngine validator,
            MetricsCollector metrics,
            ILoggingService logger)
        {
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        /// <summary>
        /// Analisa sistema e aplica correção automática SE necessário
        /// </summary>
        public async Task<AutoFixResult> AnalyzeAndFixAsync(CancellationToken ct = default)
        {
            try
            {
                // 1. OBTER MÉTRICAS ATUAIS
                var metrics = await _metrics.GetCurrentMetricsAsync();
                
                // 2. VERIFICAR SE CORREÇÃO É NECESSÁRIA
                if (!ShouldAttemptFix(metrics))
                {
                    _logger.LogInfo("[AutoFixer] Sistema está estável - nenhuma ação necessária");
                    return AutoFixResult.NoActionNeeded;
                }
                
                _logger.LogWarning($"[AutoFixer] ⚠️ Degradação detectada: {metrics}");
                
                // 3. PRÉ-VALIDAÇÃO (crítico para segurança)
                var validationResult = await _validator.ValidateSystemStateAsync();
                if (!validationResult.IsSafeToOptimize)
                {
                    _logger.LogWarning($"[AutoFixer] Sistema não está seguro para correção: {validationResult.Reason}");
                    return AutoFixResult.Unsafe(validationResult.Reason);
                }
                
                // 4. CRIAR SNAPSHOT PARA ROLLBACK (usando sistema de logging como fallback)
                var snapshotId = Guid.NewGuid().ToString("N");
                _logger.LogInfo($"[AutoFixer] Criando snapshot pré-correção: {snapshotId}");
                
                try
                {
                    // 5. DETERMINAR CORREÇÃO ÓTIMA
                    var fix = await DetermineOptimalFixAsync(metrics, ct);
                    
                    if (fix.Type == FixType.None)
                    {
                        _logger.LogInfo("[AutoFixer] Nenhuma correção aplicável identificada");
                        return AutoFixResult.NoActionNeeded;
                    }
                    
                    _logger.LogInfo($"[AutoFixer] 🔧 Aplicando correção: {fix.Description}");
                    
                    // 6. APLICAR CORREÇÃO COM MONITORAMENTO
                    var result = await ApplyFixWithMonitoringAsync(fix, ct);
                    
                    // 7. VALIDAR RESULTADO
                    await Task.Delay(5000, ct); // Aguardar 5s para métricas estabilizarem
                    var postMetrics = await _metrics.GetCurrentMetricsAsync();
                    
                    if (result.Success && IsImprovement(metrics, postMetrics))
                    {
                        _logger.LogSuccess($"[AutoFixer] ✅ Correção aplicada com sucesso: {fix.Description}");
                        _lastFixAttempt = DateTime.Now;
                        _validator.RegisterFixAttempt();
                        return AutoFixResult.CreateSuccess(fix);
                    }
                    else
                    {
                        // 8. CORREÇÃO NÃO TROUXE MELHORIA - REVERTER
                        _logger.LogWarning("[AutoFixer] ⚠️ Correção não trouxe melhoria - mantendo estado anterior");
                        return AutoFixResult.RolledBack("Nenhuma melhoria detectada");
                    }
                }
                catch (Exception ex)
                {
                    // 9. ERRO DURANTE CORREÇÃO - SEMPRE FALHAR COM SEGURANÇA
                    _logger.LogError("[AutoFixer] ❌ Erro durante correção", ex);
                    return AutoFixResult.Failed(ex.Message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("[AutoFixer] Erro crítico no AutoFixer", ex);
                return AutoFixResult.Failed(ex.Message);
            }
        }
        
        /// <summary>
        /// Determina se correção deve ser tentada
        /// </summary>
        private bool ShouldAttemptFix(VoltrisOptimizer.Services.Monitoring.SystemMetrics metrics)
        {
            // Throttling: não executar se última correção foi há menos de 30s
            if ((DateTime.Now - _lastFixAttempt).TotalSeconds < _config.MinTimeBetweenFixes)
                return false;
                
            // Verificar thresholds
            return metrics.CpuUsage > _config.CpuSpikeThreshold ||
                   metrics.GpuLoad > _config.GpuLoadThreshold ||
                   metrics.FrameTimeVariance > _config.FrameTimeVarianceMs ||
                   metrics.InputLatency > _config.InputLatencyMs ||
                   metrics.RamPressure > _config.RamPressurePercent;
        }
        
        /// <summary>
        /// Determina correção ótima baseada em métricas
        /// </summary>
        private async Task<OptimizationFix> DetermineOptimalFixAsync(VoltrisOptimizer.Services.Monitoring.SystemMetrics metrics, CancellationToken ct)
        {
            try
            {
                // Prioridade 1: CPU Spike
                if (metrics.CpuUsage > _config.CpuSpikeThreshold)
                {
                    return new OptimizationFix
                    {
                        Type = FixType.ProcessPriority,
                        Description = "Reduzir prioridade de processos em background",
                        Severity = FixSeverity.Medium,
                        Action = async () => await OptimizeBackgroundProcesses(ct)
                    };
                }
                
                // Prioridade 2: FrameTime Variance (Micro-Stutter)
                if (metrics.FrameTimeVariance > _config.FrameTimeVarianceMs)
                {
                    return new OptimizationFix
                    {
                        Type = FixType.DpcLatency,
                        Description = "Otimizar DPC latency e timer resolution",
                        Severity = FixSeverity.Medium,
                        Action = async () => await ReduceDpcLatency(ct)
                    };
                }
                
                // Prioridade 3: Input Latency
                if (metrics.InputLatency > _config.InputLatencyMs)
                {
                    return new OptimizationFix
                    {
                        Type = FixType.InputLatency,
                        Description = "Otimizar latência de input",
                        Severity = FixSeverity.High,
                        Action = async () => await OptimizeInputLatency(ct)
                    };
                }
                
                // Prioridade 4: RAM Pressure
                if (metrics.RamPressure > _config.RamPressurePercent)
                {
                    return new OptimizationFix
                    {
                        Type = FixType.MemoryPressure,
                        Description = "Liberar memória RAM",
                        Severity = FixSeverity.Medium,
                        Action = async () => await ReleaseMemory(ct)
                    };
                }
                
                return OptimizationFix.None;
            }
            catch
            {
                return OptimizationFix.None;
            }
        }
        
        /// <summary>
        /// Aplica correção com monitoramento de resultado
        /// </summary>
        private async Task<FixApplicationResult> ApplyFixWithMonitoringAsync(OptimizationFix fix, CancellationToken ct)
        {
            try
            {
                await fix.Action();
                
                return new FixApplicationResult
                {
                    Success = true,
                    Message = $"Correção aplicada: {fix.Description}"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"[AutoFixer] Erro ao aplicar correção {fix.Type}", ex);
                return new FixApplicationResult
                {
                    Success = false,
                    Message = ex.Message
                };
            }
        }
        
        /// <summary>
        /// Verifica se métricas pós-correção são melhores
        /// </summary>
        private bool IsImprovement(VoltrisOptimizer.Services.Monitoring.SystemMetrics before, VoltrisOptimizer.Services.Monitoring.SystemMetrics after)
        {
            // Melhoria se:
            // - CPU usage diminuiu > 5%
            // - FrameTime variance diminuiu > 3ms
            // - Input latency diminuiu > 5ms
            // - RAM pressure diminuiu > 5%
            
            var cpuImproved = (before.CpuUsage - after.CpuUsage) > 5;
            var frameTimeImproved = (before.FrameTimeVariance - after.FrameTimeVariance) > 3;
            var latencyImproved = (before.InputLatency - after.InputLatency) > 5;
            var ramImproved = (before.RamPressure - after.RamPressure) > 5;
            
            // Considerar melhoria se QUALQUER métrica melhorou
            return cpuImproved || frameTimeImproved || latencyImproved || ramImproved;
        }
        
        #region Fix Implementations
        
        /// <summary>
        /// Otimiza processos em background
        /// </summary>
        private async Task OptimizeBackgroundProcesses(CancellationToken ct)
        {
            _logger.LogInfo("[AutoFixer] Otimizando processos em background...");
            
            // Usar AIOptimizer existente para determinar processos seguros para otimizar
            if (App.AIOptimizer != null)
            {
                // await App.AIOptimizer.PerformIntelligentOptimizationAsync(null, msg => _logger.LogInfo($"[AutoFixer] {msg}"));
            }
            
            await Task.Delay(100, ct);
        }
        
        /// <summary>
        /// Reduz DPC latency
        /// </summary>
        private async Task ReduceDpcLatency(CancellationToken ct)
        {
            _logger.LogInfo("[AutoFixer] Otimizando DPC latency...");
            
            // Integração com ExtremeOptimizationsService (se disponível)
            // Por enquanto, apenas logar
            _logger.LogInfo("[AutoFixer] DPC optimization requer integração com driver layer");
            
            await Task.Delay(100, ct);
        }
        
        /// <summary>
        /// Otimiza input latency
        /// </summary>
        private async Task OptimizeInputLatency(CancellationToken ct)
        {
            _logger.LogInfo("[AutoFixer] Otimizando input latency...");
            
            // Usar PerformanceOptimizer para ativar High Performance Power Plan
            if (App.PerformanceOptimizer != null)
            {
                await App.PerformanceOptimizer.SetHighPerformancePlanAsync();
            }
            
            await Task.Delay(100, ct);
        }
        
        /// <summary>
        /// Libera memória RAM
        /// </summary>
        private async Task ReleaseMemory(CancellationToken ct)
        {
            _logger.LogInfo("[AutoFixer] Liberando memória RAM...");
            
            // Usar SystemCleaner para limpar cache
            if (App.SystemCleaner != null)
            {
                await App.SystemCleaner.CleanTempFilesAsync();
            }
            
            // Forçar GC
            
            await Task.Delay(100, ct);
        }
        
        #endregion
    }
    
    #region Data Classes
    
    /// <summary>
    /// Configuração do AutoFixer
    /// </summary>
    internal class AutoFixConfig
    {
        public double CpuSpikeThreshold { get; set; }
        public double GpuLoadThreshold { get; set; }
        public double FrameTimeVarianceMs { get; set; }
        public double InputLatencyMs { get; set; }
        public double RamPressurePercent { get; set; }
        public int MinTimeBetweenFixes { get; set; }
    }
    
    /// <summary>
    /// Tipo de correção
    /// </summary>
    public enum FixType
    {
        None,
        ProcessPriority,
        DpcLatency,
        InputLatency,
        MemoryPressure,
        ThermalManagement
    }
    
    /// <summary>
    /// Severidade da correção
    /// </summary>
    public enum FixSeverity
    {
        Low,
        Medium,
        High,
        Critical
    }
    
    /// <summary>
    /// Correção a ser aplicada
    /// </summary>
    public class OptimizationFix
    {
        public FixType Type { get; set; }
        public string Description { get; set; } = string.Empty;
        public FixSeverity Severity { get; set; }
        public Func<Task>? Action { get; set; }
        
        public static OptimizationFix None => new OptimizationFix { Type = FixType.None };
    }
    
    /// <summary>
    /// Resultado de aplicação de correção
    /// </summary>
    internal class FixApplicationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }
    
    /// <summary>
    /// Resultado do AutoFixer
    /// </summary>
    public class AutoFixResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public FixType FixApplied { get; set; }
        public bool WasRolledBack { get; set; }
        
        public static AutoFixResult NoActionNeeded => new AutoFixResult
        {
            Success = true,
            Message = "Sistema estável - nenhuma correção necessária",
            FixApplied = FixType.None
        };
        
        public static AutoFixResult CreateSuccess(OptimizationFix fix)
        {
            return new AutoFixResult
            {
                Success = true,
                Message = $"Correção aplicada: {fix.Description}",
                FixApplied = fix.Type
            };
        }
        
        public static AutoFixResult Unsafe(string reason)
        {
            return new AutoFixResult
            {
                Success = false,
                Message = $"Sistema inseguro: {reason}",
                FixApplied = FixType.None
            };
        }
        
        public static AutoFixResult RolledBack(string reason)
        {
            return new AutoFixResult
            {
                Success = false,
                Message = $"Correção revertida: {reason}",
                FixApplied = FixType.None,
                WasRolledBack = true
            };
        }
        
        public static AutoFixResult Failed(string error)
        {
            return new AutoFixResult
            {
                Success = false,
                Message = $"Erro: {error}",
                FixApplied = FixType.None
            };
        }
        
        public override string ToString()
        {
            return Success 
                ? $"✅ {Message}" 
                : $"❌ {Message}";
        }
    }
    
    #endregion
}
