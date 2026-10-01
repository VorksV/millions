using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services.Gamer.Models;

namespace VoltrisOptimizer.Services.Gamer.Diagnostics
{
    /// <summary>
    /// VOLTRIS GAMER DIAGNOSTIC ENGINE
    /// Sistema completo de diagnóstico e monitoramento do Modo Gamer
    /// 
    /// OBJETIVO: Identificar com precisão qual otimização está causando
    /// microtravadas, spikes de FPS, perda de fluidez ou problemas de rede.
    /// 
    /// Este sistema APENAS OBSERVA E REGISTRA - não modifica o funcionamento atual.
    /// </summary>
    public class VoltrisGamerDiagnosticEngine : IDisposable
    {
        private static readonly Lazy<VoltrisGamerDiagnosticEngine> _instance = 
            new Lazy<VoltrisGamerDiagnosticEngine>(() => new VoltrisGamerDiagnosticEngine());

        public static VoltrisGamerDiagnosticEngine Instance => _instance.Value;

        private readonly ILoggingService _logger;
        private readonly ConcurrentQueue<DiagnosticEvent> _eventLog = new();
        private readonly ConcurrentDictionary<string, OptimizationSnapshot> _snapshots = new();
        private readonly Stopwatch _sessionStopwatch = new();
        private readonly object _lock = new();
        
        private bool _isDiagnosticActive;
        private DateTime _sessionStartTime;
        private string? _currentGame;
        private int _eventCounter;
        private CancellationTokenSource? _monitoringCts;
        private Task? _monitoringTask;
        
        // Métricas de performance em tempo real
        private PerformanceMetrics _currentMetrics = new();
        private readonly List<PerformanceMetrics> _metricsHistory = new();
        private const int MAX_HISTORY_SIZE = 1000;
        
        // Limites para detecção de problemas
        private const double FPS_DROP_THRESHOLD = 0.15; // 15% drop
        private const double FRAMETIME_SPIKE_THRESHOLD_MS = 20.0; // 20ms = 50 FPS spike
        private const double CPU_SPIKE_THRESHOLD = 0.25; // 25% sudden increase
        private const double MEMORY_SPIKE_THRESHOLD_MB = 500; // 500MB sudden increase

        private VoltrisGamerDiagnosticEngine()
        {
            _logger = App.LoggingService ?? throw new InvalidOperationException("LoggingService not initialized");
        }

        #region Public API

        /// <summary>
        /// Inicia o diagnóstico do Modo Gamer
        /// </summary>
        public void StartDiagnosticSession(string gameName)
        {
            lock (_lock)
            {
                if (_isDiagnosticActive)
                {
                    _logger.LogWarning("[Diagnostic] Sessão já ativa - ignorando");
                    return;
                }

                _isDiagnosticActive = true;
                _sessionStartTime = DateTime.Now;
                _currentGame = gameName;
                _eventCounter = 0;
                _sessionStopwatch.Restart();
                _metricsHistory.Clear();
                _eventLog.Clear();

                LogDiagnosticEvent(DiagnosticEventType.SessionStart, "Início da sessão de diagnóstico", 
                    new { Game = gameName, Timestamp = _sessionStartTime });

                _logger.LogInfo($"[Diagnostic] 🎯 SESSÃO INICIADA para: {gameName}");
                _logger.LogInfo($"[Diagnostic] 📊 Monitoramento de performance ativo - detecção de stutter/spikes");

                // Iniciar monitoramento contínuo
                StartContinuousMonitoring();
            }
        }

        /// <summary>
        /// Finaliza a sessão de diagnóstico e gera relatório
        /// </summary>
        public DiagnosticReport EndDiagnosticSession()
        {
            lock (_lock)
            {
                if (!_isDiagnosticActive)
                {
                    _logger.LogWarning("[Diagnostic] Sessão não ativa - ignorando");
                    return new DiagnosticReport();
                }

                _sessionStopwatch.Stop();
                _isDiagnosticActive = false;
                StopContinuousMonitoring();

                LogDiagnosticEvent(DiagnosticEventType.SessionEnd, "Fim da sessão de diagnóstico",
                    new { Duration = _sessionStopwatch.Elapsed });

                _logger.LogInfo($"[Diagnostic] 📊 SESSÃO ENCERRADA - Duração: {_sessionStopwatch.Elapsed}");

                // Gerar relatório
                var report = GenerateDiagnosticReport();
                
                // Salvar relatório em arquivo
                SaveReportToFile(report);

                // Resetar estado
                _currentGame = null;
                _eventLog.Clear();
                _metricsHistory.Clear();
                _snapshots.Clear();

                return report;
            }
        }

        /// <summary>
        /// Registra o início de uma otimização
        /// </summary>
        public void LogOptimizationStart(string optimizationName, string className, string methodName)
        {
            if (!_isDiagnosticActive) return;

            var timestamp = GetHighPrecisionTimestamp();
            var threadId = Thread.CurrentThread.ManagedThreadId;

            var eventData = new
            {
                Optimization = optimizationName,
                Class = className,
                Method = methodName,
                ThreadId = threadId,
                Timestamp = timestamp
            };

            LogDiagnosticEvent(DiagnosticEventType.OptimizationStart, 
                $"[OPT-IN] {optimizationName}.{methodName}", eventData);

            // Criar snapshot ANTES da otimização
            var snapshot = CaptureOptimizationSnapshot(optimizationName, "Before");
            _snapshots[$"{optimizationName}_Before"] = snapshot;
        }

        /// <summary>
        /// Registra o fim de uma otimização
        /// </summary>
        public void LogOptimizationEnd(string optimizationName, bool success, string result, TimeSpan duration)
        {
            if (!_isDiagnosticActive) return;

            var timestamp = GetHighPrecisionTimestamp();

            var eventData = new
            {
                Optimization = optimizationName,
                Success = success,
                Result = result,
                DurationMs = duration.TotalMilliseconds,
                Timestamp = timestamp
            };

            LogDiagnosticEvent(DiagnosticEventType.OptimizationEnd,
                $"[OPT-END] {optimizationName} - {(success ? "✅" : "❌")} {duration.TotalMilliseconds:F2}ms", eventData);

            // Criar snapshot DEPOIS da otimização
            var snapshot = CaptureOptimizationSnapshot(optimizationName, "After");
            _snapshots[$"{optimizationName}_After"] = snapshot;

            // Comparar snapshots e detectar anomalias
            DetectAnomalies(optimizationName);
        }

        /// <summary>
        /// Registra um evento de performance (FPS drop, stutter, spike)
        /// </summary>
        public void LogPerformanceEvent(string eventType, double value, double? baseline = null)
        {
            if (!_isDiagnosticActive) return;

            var timestamp = GetHighPrecisionTimestamp();
            var relativeTime = _sessionStopwatch.Elapsed;

            // Atualizar métricas atuais
            UpdateCurrentMetrics(eventType, value);

            var eventData = new
            {
                EventType = eventType,
                Value = value,
                Baseline = baseline,
                Delta = baseline.HasValue ? value - baseline.Value : 0,
                RelativeTimeMs = relativeTime.TotalMilliseconds,
                Timestamp = timestamp
            };

            string message = $"[PERF] {eventType}: {value:F2}";
            if (baseline.HasValue)
                message += $" (Δ: {eventData.Delta:+0.00;-0.00})";

            LogDiagnosticEvent(DiagnosticEventType.PerformanceEvent, message, eventData);

            // Verificar se é um problema crítico
            CheckForCriticalIssue(eventType, value, baseline);
        }

        /// <summary>
        /// Captura um snapshot completo do estado do sistema
        /// </summary>
        public OptimizationSnapshot CaptureSnapshot(string context)
        {
            var snapshot = CaptureOptimizationSnapshot("SystemState", context);
            _snapshots[$"Snapshot_{context}_{GetHighPrecisionTimestamp()}"] = snapshot;
            return snapshot;
        }

        #endregion

        #region Private Implementation

        private void LogDiagnosticEvent(DiagnosticEventType type, string message, object data)
        {
            var evt = new DiagnosticEvent
            {
                EventId = Interlocked.Increment(ref _eventCounter),
                EventType = type,
                Message = message,
                Timestamp = DateTime.Now,
                HighPrecisionTime = GetHighPrecisionTimestamp(),
                RelativeTimeMs = _sessionStopwatch.Elapsed.TotalMilliseconds,
                ThreadId = Thread.CurrentThread.ManagedThreadId,
                Data = data,
                GameName = _currentGame
            };

            _eventLog.Enqueue(evt);

            // Log no sistema principal
            _logger.LogInfo($"[DIAGNOSTIC] {message}");
        }

        private OptimizationSnapshot CaptureOptimizationSnapshot(string optimization, string phase)
        {
            try
            {
                var snapshot = new OptimizationSnapshot
                {
                    OptimizationName = optimization,
                    Phase = phase,
                    Timestamp = DateTime.Now,
                    HighPrecisionTime = GetHighPrecisionTimestamp(),
                    ThreadId = Thread.CurrentThread.ManagedThreadId,
                    
                    // Métricas de sistema
                    CpuUsage = GetCurrentCpuUsage(),
                    MemoryUsage = GC.GetTotalMemory(false) / 1024 / 1024, // MB
                    GpuUsage = GetCurrentGpuUsage(),
                    
                    // Timer resolution
                    TimerResolution = GetCurrentTimerResolution(),
                    
                    // Power plan
                    PowerPlan = GetCurrentPowerPlan(),
                    
                    // Process priority (se aplicável)
                    ProcessPriority = GetCurrentProcessPriority(),
                    
                    // Thread count
                    ThreadCount = Process.GetCurrentProcess().Threads.Count,
                    
                    // Handle count
                    HandleCount = Process.GetCurrentProcess().HandleCount,
                    
                    // DPC Latency (estimada)
                    EstimatedDpcLatencyMs = EstimateDpcLatency(),
                    
                    // Context switches
                    ContextSwitches = GetContextSwitches()
                };

                return snapshot;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[Diagnostic] Erro ao capturar snapshot: {ex.Message}");
                return new OptimizationSnapshot { Error = ex.Message };
            }
        }

        private void DetectAnomalies(string optimizationName)
        {
            if (!_snapshots.TryGetValue($"{optimizationName}_Before", out var before)) return;
            if (!_snapshots.TryGetValue($"{optimizationName}_After", out var after)) return;

            // Detectar mudanças significativas
            var anomalies = new List<string>();

            if (Math.Abs(after.CpuUsage - before.CpuUsage) > CPU_SPIKE_THRESHOLD * 100)
                anomalies.Add($"CPU Spike: {before.CpuUsage:F1}% → {after.CpuUsage:F1}%");

            if (Math.Abs(after.MemoryUsage - before.MemoryUsage) > MEMORY_SPIKE_THRESHOLD_MB)
                anomalies.Add($"Memory Spike: {before.MemoryUsage:F0}MB → {after.MemoryUsage:F0}MB");

            if (after.ThreadCount > before.ThreadCount + 5)
                anomalies.Add($"Thread Burst: +{after.ThreadCount - before.ThreadCount} threads");

            if (after.EstimatedDpcLatencyMs > before.EstimatedDpcLatencyMs * 2)
                anomalies.Add($"DPC Latency Spike: {before.EstimatedDpcLatencyMs:F2}ms → {after.EstimatedDpcLatencyMs:F2}ms");

            if (anomalies.Count > 0)
            {
                LogDiagnosticEvent(DiagnosticEventType.AnomalyDetected,
                    $"⚠️ ANOMALIA DETECTADA em {optimizationName}", new
                    {
                        Anomalies = anomalies,
                        Before = before,
                        After = after
                    });
            }
        }

        private void UpdateCurrentMetrics(string eventType, double value)
        {
            _currentMetrics = eventType switch
            {
                "FPS" => new PerformanceMetrics { CpuUsage = value },
                "CPU" => new PerformanceMetrics { CpuUsage = value },
                "Memory" => new PerformanceMetrics { MemoryUsage = value },
                _ => _currentMetrics
            };
        }

        private void CheckForCriticalIssue(string eventType, double value, double? baseline)
        {
            bool isCritical = false;
            string issueType = "";

            if (eventType == "FPS" && baseline.HasValue && value < baseline * (1 - FPS_DROP_THRESHOLD))
            {
                isCritical = true;
                issueType = "FPS Drop";
            }
            else if (eventType == "FrameTime" && value > FRAMETIME_SPIKE_THRESHOLD_MS)
            {
                isCritical = true;
                issueType = "Frame Time Spike";
            }
            else if (eventType == "CPU" && baseline.HasValue && value > baseline * (1 + CPU_SPIKE_THRESHOLD))
            {
                isCritical = true;
                issueType = "CPU Spike";
            }

            if (isCritical)
            {
                // Capturar snapshot completo do momento crítico
                var criticalSnapshot = CaptureSnapshot($"Critical_{eventType}");
                
                // Correlacionar com otimizações recentes
                var recentOptimizations = FindRecentOptimizations(TimeSpan.FromSeconds(5));

                LogDiagnosticEvent(DiagnosticEventType.CriticalIssue,
                    $"🚨 PROBLEMA CRÍTICO: {issueType} detectado!", new
                    {
                        IssueType = issueType,
                        Value = value,
                        Baseline = baseline,
                        Snapshot = criticalSnapshot,
                        RecentOptimizations = recentOptimizations
                    });
            }
        }

        private List<DiagnosticEvent> FindRecentOptimizations(TimeSpan window)
        {
            var now = _sessionStopwatch.Elapsed.TotalMilliseconds;
            return _eventLog
                .Where(e => e.EventType == DiagnosticEventType.OptimizationEnd &&
                           Math.Abs(now - e.RelativeTimeMs) <= window.TotalMilliseconds)
                .OrderByDescending(e => e.RelativeTimeMs)
                .Take(10)
                .ToList();
        }

        private void StartContinuousMonitoring()
        {
            _monitoringCts = new CancellationTokenSource();
            _monitoringTask = Task.Run(async () =>
            {
                var token = _monitoringCts.Token;
                var sw = Stopwatch.StartNew();

                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        // Coletar métricas a cada 100ms
                        await Task.Delay(100, token);

                        var metrics = new PerformanceMetrics
                        {
                            Timestamp = DateTime.Now,
                            RelativeTimeMs = sw.Elapsed.TotalMilliseconds,
                            CpuUsage = GetCurrentCpuUsage(),
                            MemoryUsage = GC.GetTotalMemory(false) / 1024 / 1024,
                            ThreadCount = Process.GetCurrentProcess().Threads.Count
                        };

                        lock (_metricsHistory)
                        {
                            _metricsHistory.Add(metrics);
                            if (_metricsHistory.Count > MAX_HISTORY_SIZE)
                                _metricsHistory.RemoveAt(0);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"[Diagnostic] Erro no monitoramento: {ex.Message}");
                    }
                }
            });
        }

        private void StopContinuousMonitoring()
        {
            _monitoringCts?.Cancel();
            _monitoringTask?.Wait(1000);
            _monitoringCts?.Dispose();
            _monitoringCts = null;
        }

        private DiagnosticReport GenerateDiagnosticReport()
        {
            var report = new DiagnosticReport
            {
                GameName = _currentGame,
                SessionStart = _sessionStartTime,
                SessionEnd = DateTime.Now,
                Duration = _sessionStopwatch.Elapsed,
                TotalEvents = _eventLog.Count,
                OptimizationCount = _eventLog.Count(e => e.EventType == DiagnosticEventType.OptimizationEnd),
                AnomalyCount = _eventLog.Count(e => e.EventType == DiagnosticEventType.AnomalyDetected),
                CriticalIssueCount = _eventLog.Count(e => e.EventType == DiagnosticEventType.CriticalIssue),
                
                Events = _eventLog.ToList(),
                Snapshots = _snapshots.Values.ToList(),
                MetricsHistory = _metricsHistory.ToList()
            };

            // Identificar otimizações problemáticas
            report.ProblematicOptimizations = IdentifyProblematicOptimizations();

            // Gerar recomendações
            report.Recommendations = GenerateRecommendations(report);

            return report;
        }

        private List<string> IdentifyProblematicOptimizations()
        {
            var problematic = new List<string>();

            // Encontrar otimizações seguidas de problemas críticos
            var issues = _eventLog.Where(e => e.EventType == DiagnosticEventType.CriticalIssue).ToList();
            
            foreach (var issue in issues)
            {
                // Olhar 2 segundos antes do problema
                var windowStart = issue.RelativeTimeMs - 2000;
                var optimizationsBefore = _eventLog
                    .Where(e => e.EventType == DiagnosticEventType.OptimizationEnd &&
                               e.RelativeTimeMs >= windowStart &&
                               e.RelativeTimeMs <= issue.RelativeTimeMs)
                    .Select(e => e.Data?.GetType().GetProperty("Optimization")?.GetValue(e.Data)?.ToString())
                    .Distinct();

                foreach (var opt in optimizationsBefore)
                {
                    if (!string.IsNullOrEmpty(opt) && !problematic.Contains(opt))
                        problematic.Add(opt);
                }
            }

            return problematic;
        }

        private List<string> GenerateRecommendations(DiagnosticReport report)
        {
            var recommendations = new List<string>();

            if (report.CriticalIssueCount > 0)
            {
                recommendations.Add("⚠️ Problemas críticos detectados. Revise as otimizações marcadas como problemáticas.");
            }

            if (report.AnomalyCount > 5)
            {
                recommendations.Add("⚠️ Múltiplas anomalias detectadas. Considere reduzir a agressividade das otimizações.");
            }

            if (report.ProblematicOptimizations.Count > 0)
            {
                recommendations.Add($"❌ Otimizações problemáticas identificadas: {string.Join(", ", report.ProblematicOptimizations)}");
                recommendations.Add("💡 Sugestão: Desative estas otimizações uma por uma para isolar a causa.");
            }

            if (report.MetricsHistory.Any(m => m.CpuUsage > 90))
            {
                recommendations.Add("⚠️ Uso de CPU muito elevado (>90%). Otimizações podem estar sobrecarregando o sistema.");
            }

            if (report.Snapshots.Any(s => s.EstimatedDpcLatencyMs > 5))
            {
                recommendations.Add("⚠️ DPC Latency elevada detectada. Isso pode causar microtravadas e áudio entrecortado.");
            }

            if (recommendations.Count == 0)
            {
                recommendations.Add("✅ Nenhum problema significativo detectado. O Modo Gamer está funcionando corretamente.");
            }

            return recommendations;
        }

        private void SaveReportToFile(DiagnosticReport report)
        {
            try
            {
                var logPath = Path.Combine(AppDataPaths.UnifiedRoot, "Diagnostics", $"GamerDiagnostic_{_currentGame}_{DateTime.Now:yyyyMMdd_HHmmss}.json");
                
                var json = System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    IncludeFields = true
                });

                File.WriteAllText(logPath, json);
                _logger.LogInfo($"[Diagnostic] 📄 Relatório salvo em: {logPath}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[Diagnostic] Erro ao salvar relatório: {ex.Message}");
            }
        }

        #region Helper Methods (Placeholders - implementar conforme necessário)

        private double GetHighPrecisionTimestamp() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        private double GetCurrentCpuUsage() => 0.0; // Implementar com PerformanceCounter
        private double GetCurrentGpuUsage() => 0.0; // Implementar com NVAPI/ADL
        private double GetCurrentTimerResolution() => 0.0; // Ler de NtQueryTimerResolution
        private string GetCurrentPowerPlan() => "Unknown"; // Ler de powercfg
        private string GetCurrentProcessPriority() => "Normal"; // Ler do processo do jogo
        private double EstimateDpcLatency() => 0.0; // Estimar baseado em timers
        private long GetContextSwitches() => (long)Process.GetCurrentProcess().PrivilegedProcessorTime.TotalMilliseconds;

        #endregion // Helper Methods

        #endregion // Private Implementation

        #region IDisposable

        public void Dispose()
        {
            StopContinuousMonitoring();
            
            if (_isDiagnosticActive)
            {
                EndDiagnosticSession();
            }
        }

        #endregion
    }

    #region Supporting Classes

    public enum DiagnosticEventType
    {
        SessionStart,
        SessionEnd,
        OptimizationStart,
        OptimizationEnd,
        PerformanceEvent,
        AnomalyDetected,
        CriticalIssue,
        Snapshot
    }

    public class DiagnosticEvent
    {
        public int EventId { get; set; }
        public DiagnosticEventType EventType { get; set; }
        public string Message { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public double HighPrecisionTime { get; set; }
        public double RelativeTimeMs { get; set; }
        public int ThreadId { get; set; }
        public object? Data { get; set; }
        public string? GameName { get; set; }
    }

    public class OptimizationSnapshot
    {
        public string OptimizationName { get; set; } = "";
        public string Phase { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public double HighPrecisionTime { get; set; }
        public int ThreadId { get; set; }
        
        // Métricas
        public double CpuUsage { get; set; }
        public double MemoryUsage { get; set; }
        public double GpuUsage { get; set; }
        public double TimerResolution { get; set; }
        public string PowerPlan { get; set; } = "";
        public string ProcessPriority { get; set; } = "";
        public int ThreadCount { get; set; }
        public int HandleCount { get; set; }
        public double EstimatedDpcLatencyMs { get; set; }
        public long ContextSwitches { get; set; }
        
        public string? Error { get; set; }
    }

    public class PerformanceMetrics
    {
        public DateTime Timestamp { get; set; }
        public double RelativeTimeMs { get; set; }
        public double CpuUsage { get; set; }
        public double MemoryUsage { get; set; }
        public int ThreadCount { get; set; }
    }

    public class DiagnosticReport
    {
        public string? GameName { get; set; }
        public DateTime SessionStart { get; set; }
        public DateTime SessionEnd { get; set; }
        public TimeSpan Duration { get; set; }
        public int TotalEvents { get; set; }
        public int OptimizationCount { get; set; }
        public int AnomalyCount { get; set; }
        public int CriticalIssueCount { get; set; }
        
        public List<DiagnosticEvent> Events { get; set; } = new();
        public List<OptimizationSnapshot> Snapshots { get; set; } = new();
        public List<PerformanceMetrics> MetricsHistory { get; set; } = new();
        
        // Análise
        public List<string> ProblematicOptimizations { get; set; } = new();
        public List<string> Recommendations { get; set; } = new();
    }

    #endregion
}