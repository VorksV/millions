using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Monitoring
{
    /// <summary>
    /// Monitor de auto-impacto: detecta quando o PRÓPRIO VOLTRIS está causando stutter/degradação
    /// CRÍTICO: Previne que o otimizador se torne o problema
    /// </summary>
    public class SelfImpactMonitor
    {
        private readonly MetricsCollector _metrics;
        private readonly ProcessCacheService _processCache;
        private readonly ILoggingService _logger;
        
        // Tracking de ações internas com histórico limitado
        private readonly Queue<InternalAction> _actionHistory = new(capacity: 50);
        private readonly object _lock = new();
        
        // Thresholds para detecção de impacto
        private readonly SelfImpactThresholds _thresholds = new()
        {
            FrameTimeDeltaMs = 5.0,      // > 5ms aumento = suspeito
            FpsDelta = -10.0,             // > 10 FPS queda = suspeito
            InputLatencyDeltaMs = 10.0,   // > 10ms aumento = suspeito
            SuspiciousActionCount = 3,    // 3+ ações suspeitas = culpado
            MinTimeBetweenChecksMs = 5000 // Verificar no máximo a cada 5s
        };
        
        private DateTime _lastImpactCheck = DateTime.MinValue;
        
        public SelfImpactMonitor(
            MetricsCollector metrics,
            ProcessCacheService processCache,
            ILoggingService logger)
        {
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            _processCache = processCache ?? throw new ArgumentNullException(nameof(processCache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        /// <summary>
        /// Registra uma ação ANTES de executá-la (para correlação futura)
        /// </summary>
        public void TrackInternalAction(string module, string description, SystemMetrics metricsBefore)
        {
            lock (_lock)
            {
                _actionHistory.Enqueue(new InternalAction
                {
                    Module = module,
                    Description = description,
                    Timestamp = DateTime.Now,
                    MetricsBefore = metricsBefore
                });
                
                // Manter apenas últimas 50 ações (evita memory leak)
                while (_actionHistory.Count > 50)
                    _actionHistory.Dequeue();
            }
            
            _logger.LogInfo($"[SelfImpact] Tracking: {module} - {description}");
        }
        
        /// <summary>
        /// Analisa se ações recentes causaram degradação de performance
        /// </summary>
        public async Task<SelfImpactReport> AnalyzeSelfImpactAsync()
        {
            // Throttling: não verificar mais de 1x a cada 5s
            if ((DateTime.Now - _lastImpactCheck).TotalMilliseconds < _thresholds.MinTimeBetweenChecksMs)
                return SelfImpactReport.NoImpact;
                
            _lastImpactCheck = DateTime.Now;
            
            var report = new SelfImpactReport();
            
            try
            {
                // 1. OBTER MÉTRICAS ATUAIS
                var currentMetrics = await _metrics.GetCurrentMetricsAsync();
                
                // 2. CORRELACIONAR AÇÕES RECENTES COM DEGRADAÇÃO
                var recentActions = GetRecentActions(TimeSpan.FromSeconds(10));
                
                foreach (var action in recentActions)
                {
                    var impact = CalculateImpact(action, currentMetrics);
                    
                    if (impact.IsSuspicious)
                    {
                        report.SuspiciousActions.Add(new SuspiciousAction
                        {
                            Module = action.Module,
                            Timestamp = action.Timestamp,
                            Description = action.Description,
                            FrameTimeBefore = action.MetricsBefore.FrameTime,
                            FrameTimeAfter = currentMetrics.FrameTime,
                            ImpactMs = currentMetrics.FrameTime - action.MetricsBefore.FrameTime,
                            FpsBefore = action.MetricsBefore.Fps,
                            FpsAfter = currentMetrics.Fps,
                            InputLatencyBefore = action.MetricsBefore.InputLatency,
                            InputLatencyAfter = currentMetrics.InputLatency
                        });
                        
                        _logger.LogWarning(
                            $"[SelfImpact] SUSPEITO: {action.Module} causou " +
                            $"FrameTime:{impact.FrameTimeDelta:+0.0;-0.0}ms " +
                            $"FPS:{impact.FpsDelta:+0;-0} " +
                            $"InputLag:{impact.InputLatencyDelta:+0.0;-0.0}ms");
                    }
                }
                
                // 3. IDENTIFICAR MÓDULO CULPADO
                if (report.SuspiciousActions.Count >= _thresholds.SuspiciousActionCount)
                {
                    var culpritGroup = report.SuspiciousActions
                        .GroupBy(a => a.Module)
                        .OrderByDescending(g => g.Sum(a => a.ImpactMs))
                        .FirstOrDefault();
                        
                    if (culpritGroup != null)
                    {
                        report.CulpritModule = culpritGroup.Key;
                        report.TotalImpactMs = culpritGroup.Sum(a => a.ImpactMs);
                        report.ImpactDetected = true;
                        
                        _logger.LogError(
                            $"[SelfImpact] ⚠️ MÓDULO PROBLEMÁTICO DETECTADO: {report.CulpritModule} " +
                            $"(+{report.TotalImpactMs:F1}ms frametime total)");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("[SelfImpact] Erro ao analisar auto-impacto", ex);
            }
            
            return report;
        }
        
        /// <summary>
        /// Calcula impacto de uma ação específica
        /// </summary>
        private ActionImpact CalculateImpact(InternalAction action, SystemMetrics current)
        {
            var frameTimeDelta = current.FrameTime - action.MetricsBefore.FrameTime;
            var fpsDelta = current.Fps - action.MetricsBefore.Fps;
            var inputLatencyDelta = current.InputLatency - action.MetricsBefore.InputLatency;
            
            // Considerar suspeito se qualquer threshold for violado
            var isSuspicious = 
                frameTimeDelta > _thresholds.FrameTimeDeltaMs ||
                fpsDelta < _thresholds.FpsDelta ||
                inputLatencyDelta > _thresholds.InputLatencyDeltaMs;
                
            return new ActionImpact
            {
                FrameTimeDelta = frameTimeDelta,
                FpsDelta = fpsDelta,
                InputLatencyDelta = inputLatencyDelta,
                IsSuspicious = isSuspicious
            };
        }
        
        /// <summary>
        /// Obtém ações executadas recentemente
        /// </summary>
        private List<InternalAction> GetRecentActions(TimeSpan window)
        {
            lock (_lock)
            {
                var cutoff = DateTime.Now - window;
                return _actionHistory
                    .Where(a => a.Timestamp >= cutoff)
                    .ToList();
            }
        }
        
        /// <summary>
        /// Aplica mitigação automática quando módulo problemático é detectado
        /// </summary>
        public async Task MitigateImpactAsync(string culpritModule)
        {
            _logger.LogWarning($"[SelfImpact] 🛡️ Iniciando mitigação de: {culpritModule}");
            
            try
            {
                switch (culpritModule)
                {
                    case "AdaptiveGovernor":
                        // DESABILITAR adaptive governor (conhecido por causar stutter)
                        // LEGACY KILL SWITCHED
                        // if (App.GamerOptimizer != null)
                        // {
                        //     App.GamerOptimizer.SetAdaptiveGovernorEnabled(false);
                        //     _logger.LogSuccess("[SelfImpact] ✅ Adaptive Governor desabilitado");
                        // }
                        break;
                        
                    case "WmiQuery":
                        // Aumentar cache TTL para reduzir frequência de WMI
                        _logger.LogInfo("[SelfImpact] ℹ️ Recomendação: Aumentar TTL de cache WMI");
                        break;
                        
                    case "ProcessMonitoring":
                        // Reduzir frequência de monitoramento
                        _logger.LogInfo("[SelfImpact] ℹ️ Recomendação: Reduzir frequência de monitoramento de processos");
                        break;
                        
                    case "GameDetection":
                        // Aumentar intervalo de polling
                        _logger.LogInfo("[SelfImpact] ℹ️ Recomendação: Aumentar intervalo de polling de detecção");
                        break;
                        
                    default:
                        _logger.LogWarning($"[SelfImpact] ⚠️ Módulo desconhecido: {culpritModule}");
                        _logger.LogInfo("[SelfImpact] ℹ️ Recomendação: Monitorar manualmente e reportar aos desenvolvedores");
                        break;
                }
                
                await Task.Delay(100); // Aguardar mitigação estabilizar
            }
            catch (Exception ex)
            {
                _logger.LogError($"[SelfImpact] Erro ao mitigar impacto de {culpritModule}", ex);
            }
        }
    }
    
    #region Data Classes
    
    /// <summary>
    /// Ação interna rastreada para correlação
    /// </summary>
    internal class InternalAction
    {
        public string Module { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public SystemMetrics MetricsBefore { get; set; } = new();
    }
    
    /// <summary>
    /// Impacto calculado de uma ação
    /// </summary>
    internal class ActionImpact
    {
        public double FrameTimeDelta { get; set; }
        public double FpsDelta { get; set; }
        public double InputLatencyDelta { get; set; }
        public bool IsSuspicious { get; set; }
    }
    
    /// <summary>
    /// Relatório de auto-impacto
    /// </summary>
    public class SelfImpactReport
    {
        public bool ImpactDetected { get; set; }
        public string CulpritModule { get; set; } = string.Empty;
        public double TotalImpactMs { get; set; }
        public List<SuspiciousAction> SuspiciousActions { get; set; } = new();
        
        public static SelfImpactReport NoImpact => new SelfImpactReport { ImpactDetected = false };
        
        public override string ToString()
        {
            if (!ImpactDetected)
                return "✅ Nenhum auto-impacto detectado";
                
            return $"⚠️ Auto-impacto: {CulpritModule} (+{TotalImpactMs:F1}ms, {SuspiciousActions.Count} ações suspeitas)";
        }
    }
    
    /// <summary>
    /// Ação suspeita de causar degradação
    /// </summary>
    public class SuspiciousAction
    {
        public string Module { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string Description { get; set; } = string.Empty;
        public double FrameTimeBefore { get; set; }
        public double FrameTimeAfter { get; set; }
        public double ImpactMs { get; set; }
        public double FpsBefore { get; set; }
        public double FpsAfter { get; set; }
        public double InputLatencyBefore { get; set; }
        public double InputLatencyAfter { get; set; }
    }
    
    /// <summary>
    /// Thresholds para detecção de impacto
    /// </summary>
    internal class SelfImpactThresholds
    {
        public double FrameTimeDeltaMs { get; set; }
        public double FpsDelta { get; set; }
        public double InputLatencyDeltaMs { get; set; }
        public int SuspiciousActionCount { get; set; }
        public double MinTimeBetweenChecksMs { get; set; }
    }
    
    #endregion
}
