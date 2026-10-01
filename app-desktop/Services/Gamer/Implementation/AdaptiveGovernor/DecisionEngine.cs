using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services.Gamer.Models;

namespace VoltrisOptimizer.Services.Gamer.Implementation.AdaptiveGovernor
{
    /// <summary>
    /// Engine de decisão adaptativa baseada em métricas reais
    /// Decide se e o que fazer baseado em thresholds claros
    /// </summary>
    internal class DecisionEngine
    {
        private readonly ILoggingService _logger;
        
        // Thresholds PROFISSIONAIS (Ajustados para evitar falso-positivos em jogos pesados)
        private const double FpsLowThreshold = 20.0; // FPS abaixo de 20 é crítico; 30 é aceitável em cenas pesadas
        private const double FpsTargetThreshold = 60.0; // FPS alvo para normalidade
        private const double FrameTimeStutterThreshold = 50.0; // Frame time > 50ms (abaixo de 20 FPS) = Stutter real
        private const double CpuUsageHighThreshold = 95.0; // CPU > 95% = Saturação real
        private const double CpuUsageLowThreshold = 30.0; 
        private const double GpuUsageHighThreshold = 98.0; 
        private const double GpuUsageLowThreshold = 40.0; 
        private const double RamUsageHighThreshold = 90.0; 
        
        // Dinâmico: Será multiplicado pelo número de núcleos no futuro, mas 8 é um valor base mais seguro para CPUs modernas
        private const double ProcessorQueueLengthThreshold = 8.0; 
        
        private const double DpcPercentThreshold = 15.0; // Drivers muito problemáticos (latência severa)
        private const double InterruptPercentThreshold = 10.0; // Hardware/Interrupts severos
        private const double PageFaultsHighThreshold = 5000.0; // Page faults > 5000/s indica swap real de disco
        
        // Histórico para detecção de padrões
        private readonly Queue<AdaptiveSystemMetrics> _metricsHistory = new Queue<AdaptiveSystemMetrics>();
        private const int MaxHistorySize = 10; // Últimas 10 amostras
        
        public DecisionEngine(ILoggingService logger)
        {
            _logger.LogEntry(nameof(DecisionEngine), ("logger", logger != null));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogExit(nameof(DecisionEngine));
        }
        
        /// <summary>
        /// Analisa métricas e decide se alguma ação deve ser tomada
        /// </summary>
        public DecisionResult AnalyzeAndDecide(AdaptiveSystemMetrics metrics)
        {
            _logger.LogEntry(nameof(AnalyzeAndDecide), ("metrics.Fps", metrics.Fps), ("metrics.CpuUsagePercent", metrics.CpuUsagePercent), ("metrics.GpuUsagePercent", metrics.GpuUsagePercent));
            
            // Adicionar ao histórico
            _metricsHistory.Enqueue(metrics);
            if (_metricsHistory.Count > MaxHistorySize)
            {
                _metricsHistory.Dequeue();
            }
            
            var decision = new DecisionResult
            {
                Timestamp = DateTime.UtcNow,
                ShouldAct = false,
                Action = null,
                Reason = "Métricas normais"
            };
            
            // Verificar se há stutter detectado
            var stutterDetected = DetectStutter(metrics);
            if (stutterDetected != null)
            {
                decision.StutterIncident = stutterDetected;
                _logger.LogDebug($"[DecisionEngine] Stutter detectado: {stutterDetected.Cause}");
            }
            
            // Se FPS está baixo e CPU/GPU não estão saturados, podemos otimizar
            if (metrics.Fps > 0 && metrics.Fps < FpsLowThreshold)
            {
                // Verificar causa do FPS baixo
                if (metrics.CpuUsagePercent < CpuUsageLowThreshold && 
                    metrics.GpuUsagePercent < GpuUsageLowThreshold)
                {
                    // CPU e GPU subutilizados mas FPS baixo = problema de prioridade/scheduling
                    decision.ShouldAct = true;
                    decision.Action = new AdaptiveAction
                    {
                        Type = ActionType.IncreaseGamePriority,
                        Reason = $"FPS baixo ({metrics.Fps:F1}) com CPU ({metrics.CpuUsagePercent:F1}%) e GPU ({metrics.GpuUsagePercent:F1}%) subutilizados"
                    };
                    _logger.LogDecision(nameof(AnalyzeAndDecide), decision.Action.Type.ToString(), decision.Action.Reason);
                    return decision;
                }
                
                if (metrics.CpuUsagePercent > CpuUsageHighThreshold)
                {
                    // CPU saturado
                    decision.ShouldAct = true;
                    decision.Action = new AdaptiveAction
                    {
                        Type = ActionType.ReduceBackgroundPriorities,
                        Reason = $"FPS baixo ({metrics.Fps:F1}) com CPU saturado ({metrics.CpuUsagePercent:F1}%)"
                    };
                    _logger.LogDecision(nameof(AnalyzeAndDecide), decision.Action.Type.ToString(), decision.Action.Reason);
                    return decision;
                }
                
                if (metrics.GpuUsagePercent > GpuUsageHighThreshold)
                {
                    // GPU saturado - não podemos fazer muito, mas podemos limpar memória
                    if (metrics.RamUsagePercent > RamUsageHighThreshold)
                    {
                        decision.ShouldAct = true;
                        decision.Action = new AdaptiveAction
                        {
                            Type = ActionType.CleanStandbyList,
                            Reason = $"FPS baixo ({metrics.Fps:F1}) com GPU saturado ({metrics.GpuUsagePercent:F1}%) e RAM alta ({metrics.RamUsagePercent:F1}%)"
                        };
                        _logger.LogDecision(nameof(AnalyzeAndDecide), decision.Action.Type.ToString(), decision.Action.Reason);
                        return decision;
                    }
                }
            }
            
            // Verificar stutter (frame time alto)
            if (metrics.FrameTimeMs > FrameTimeStutterThreshold && metrics.Fps > 0)
            {
                // Stutter detectado - analisar causa
                if (metrics.ProcessorQueueLength > ProcessorQueueLengthThreshold)
                {
                    // CPU queue alta = CPU saturado
                    decision.ShouldAct = true;
                    decision.Action = new AdaptiveAction
                    {
                        Type = ActionType.ReduceBackgroundPriorities,
                        Reason = $"Stutter detectado (FrameTime: {metrics.FrameTimeMs:F2}ms) com CPU queue alta ({metrics.ProcessorQueueLength:F1})"
                    };
                    _logger.LogDecision(nameof(AnalyzeAndDecide), decision.Action.Type.ToString(), decision.Action.Reason);
                    return decision;
                }
                
                if (metrics.DpcPercent > DpcPercentThreshold)
                {
                    // DPC alto = drivers problemáticos
                    decision.ShouldAct = true;
                    decision.Action = new AdaptiveAction
                    {
                        Type = ActionType.ReduceBackgroundPriorities,
                        Reason = $"Stutter detectado (FrameTime: {metrics.FrameTimeMs:F2}ms) com DPC alto ({metrics.DpcPercent:F2}%)"
                    };
                    _logger.LogDecision(nameof(AnalyzeAndDecide), decision.Action.Type.ToString(), decision.Action.Reason);
                    return decision;
                }
                
                if (metrics.PageFaultsPerSec > PageFaultsHighThreshold)
                {
                    // Page faults alto = memória insuficiente
                    decision.ShouldAct = true;
                    decision.Action = new AdaptiveAction
                    {
                        Type = ActionType.CleanStandbyList,
                        Reason = $"Stutter detectado (FrameTime: {metrics.FrameTimeMs:F2}ms) com page faults alto ({metrics.PageFaultsPerSec:F0}/s)"
                    };
                    _logger.LogDecision(nameof(AnalyzeAndDecide), decision.Action.Type.ToString(), decision.Action.Reason);
                    return decision;
                }
            }
            
            // Verificar RAM alta
            if (metrics.RamUsagePercent > RamUsageHighThreshold && metrics.PageFaultsPerSec > 500)
            {
                decision.ShouldAct = true;
                decision.Action = new AdaptiveAction
                {
                    Type = ActionType.CleanStandbyList,
                    Reason = $"RAM alta ({metrics.RamUsagePercent:F1}%) com page faults ({metrics.PageFaultsPerSec:F0}/s)"
                };
                _logger.LogDecision(nameof(AnalyzeAndDecide), decision.Action.Type.ToString(), decision.Action.Reason);
                return decision;
            }
            
            // Se chegou aqui, não há ação necessária
            _logger.LogExit(nameof(AnalyzeAndDecide), decision.ShouldAct);
            return decision;
        }
        
        /// <summary>
        /// Detecta stutter baseado em métricas
        /// </summary>
        private StutterIncident? DetectStutter(AdaptiveSystemMetrics metrics)
        {
            _logger.LogEntry(nameof(DetectStutter), ("metrics.Fps", metrics.Fps), ("metrics.FrameTimeMs", metrics.FrameTimeMs));
            
            // Stutter = frame time > threshold OU variação grande de frame time
            if (metrics.FrameTimeMs > FrameTimeStutterThreshold && metrics.Fps > 0)
            {
                var cause = DetermineStutterCause(metrics);
                var incident = new StutterIncident
                {
                    Timestamp = DateTime.UtcNow,
                    Cause = cause,
                    Summary = GenerateStutterSummary(metrics, cause),
                    TotalCpuPercent = metrics.CpuUsagePercent,
                    GameCpuPercent = metrics.GameCpuPercent,
                    ProcessorQueueLength = metrics.ProcessorQueueLength,
                    DpcPercent = metrics.DpcPercent,
                    InterruptPercent = metrics.InterruptPercent,
                    PageFaultsPerSec = metrics.PageFaultsPerSec,
                    GpuUtilPercent = metrics.GpuUsagePercent,
                    CpuFreqCurrentMhz = metrics.CpuFreqCurrentMhz,
                    CpuFreqMaxMhz = metrics.CpuFreqMaxMhz,
                    FrameAvgMs = metrics.FrameTimeMs,
                    FrameJitterMs = 0, // Seria calculado com histórico
                    NetworkJitterMs = 0
                };
                _logger.LogExit(nameof(DetectStutter), incident.Cause);
                return incident;
            }
            
            _logger.LogExit(nameof(DetectStutter), "null");
            return null;
        }
        
        private StutterCause DetermineStutterCause(AdaptiveSystemMetrics metrics)
        {
            _logger.LogEntry(nameof(DetermineStutterCause));
            
            // Priorizar causas mais prováveis
            if (metrics.ProcessorQueueLength > ProcessorQueueLengthThreshold)
            {
                _logger.LogExit(nameof(DetermineStutterCause), StutterCause.CpuScheduling);
                return StutterCause.CpuScheduling;
            }
            
            if (metrics.DpcPercent > DpcPercentThreshold || metrics.InterruptPercent > InterruptPercentThreshold)
            {
                _logger.LogExit(nameof(DetermineStutterCause), StutterCause.DriversInterrupt);
                return StutterCause.DriversInterrupt;
            }
            
            if (metrics.PageFaultsPerSec > PageFaultsHighThreshold)
            {
                _logger.LogExit(nameof(DetermineStutterCause), StutterCause.MemoryPaging);
                return StutterCause.MemoryPaging;
            }
            
            if (metrics.CpuUsagePercent > CpuUsageHighThreshold)
            {
                _logger.LogExit(nameof(DetermineStutterCause), StutterCause.CpuScheduling);
                return StutterCause.CpuScheduling;
            }
            
            if (metrics.GpuUsagePercent > GpuUsageHighThreshold)
            {
                _logger.LogExit(nameof(DetermineStutterCause), StutterCause.GpuRender);
                return StutterCause.GpuRender;
            }
            
            if (metrics.FrameTimeMs > FrameTimeStutterThreshold * 2)
            {
                _logger.LogExit(nameof(DetermineStutterCause), StutterCause.FramePacing);
                return StutterCause.FramePacing;
            }
            
            _logger.LogExit(nameof(DetermineStutterCause), StutterCause.Unknown);
            return StutterCause.Unknown;
        }
        
        private string GenerateStutterSummary(AdaptiveSystemMetrics metrics, StutterCause cause)
        {
            return $"Stutter detectado: {cause} | FPS: {metrics.Fps:F1} | FrameTime: {metrics.FrameTimeMs:F2}ms | " +
                   $"CPU: {metrics.CpuUsagePercent:F1}% | GPU: {metrics.GpuUsagePercent:F1}% | " +
                   $"RAM: {metrics.RamUsagePercent:F1}%";
        }
        
        /// <summary>
        /// Limpa histórico de métricas
        /// </summary>
        public void ClearHistory()
        {
            _logger.LogEntry(nameof(ClearHistory));
            _metricsHistory.Clear();
            _logger.LogInfo("[DecisionEngine] Histórico de métricas limpo");
            _logger.LogExit(nameof(ClearHistory));
        }
    }
    
    /// <summary>
    /// Resultado da análise e decisão
    /// </summary>
    internal class DecisionResult
    {
        public DateTime Timestamp { get; set; }
        public bool ShouldAct { get; set; }
        public AdaptiveAction? Action { get; set; }
        public string Reason { get; set; } = string.Empty;
        public StutterIncident? StutterIncident { get; set; }
    }
    
    /// <summary>
    /// Ação adaptativa a ser executada
    /// </summary>
    internal class AdaptiveAction
    {
        public ActionType Type { get; set; }
        public string Reason { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
    
    /// <summary>
    /// Tipos de ações adaptativas (apenas ações seguras e reversíveis)
    /// </summary>
    internal enum ActionType
    {
        None,
        IncreaseGamePriority,      // Aumentar prioridade do processo do jogo
        ReduceBackgroundPriorities, // Reduzir prioridade de processos em background
        CleanStandbyList,           // Limpar standby list (se necessário)
        AdjustAffinity              // Ajustar afinidade de CPU (com limites)
    }
}

