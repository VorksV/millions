using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.Intelligence;
using VoltrisOptimizer.Services.Gamer.Models;
using VoltrisOptimizer.Services.Gamer.Intelligence.Models;

namespace VoltrisOptimizer.Services
{
    /// <summary>
    /// VOLTRIS REAL INTELLIGENCE SERVICE
    /// Implementa IA real baseada em métricas do sistema, não placebo
    /// Aprende com padrões de uso e faz previsões inteligentes
    /// </summary>
    public class RealIntelligenceService : IDisposable
    {
        private readonly ILoggingService _logger;
        private readonly UnifiedOptimizationService _unifiedOptimization;
        private readonly string _metricsPath;
        private readonly string _patternsPath;
        private readonly string _modelsPath;

        // Coletores de métricas reais
        private readonly SystemMetricsCollector _metricsCollector;
        private readonly UsagePatternAnalyzer _patternAnalyzer;
        private readonly PredictionEngine _predictionEngine;

        // Estado do serviço
        private bool _isActive = false;
        private bool _disposed = false;
        private CancellationTokenSource _monitoringCts;
        private readonly object _lock = new object();

        // Histórico de métricas
        private readonly List<SystemMetricsSnapshot> _metricsHistory = new();
        private readonly List<UsagePattern> _usagePatterns = new();
        private readonly Dictionary<DateTime, OptimizationRecommendation> _recommendationsHistory = new();

        public RealIntelligenceService(ILoggingService logger, UnifiedOptimizationService unifiedOptimization)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _unifiedOptimization = unifiedOptimization ?? throw new ArgumentNullException(nameof(unifiedOptimization));

            // Configurar paths
            var baseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Intelligence");
            Directory.CreateDirectory(baseDir);

            _metricsPath = Path.Combine(baseDir, "metrics.json");
            _patternsPath = Path.Combine(baseDir, "patterns.json");
            _modelsPath = Path.Combine(baseDir, "models.json");

            // Inicializar componentes
            _metricsCollector = new SystemMetricsCollector(_logger);
            _patternAnalyzer = new UsagePatternAnalyzer(_logger);
            _predictionEngine = new PredictionEngine(_logger);

            _logger.LogInfo("[RealIntelligence] Serviço de inteligência real inicializado");

            // Carregar dados existentes
            LoadHistoricalData();
        }

        /// <summary>
        /// Inicia monitoramento inteligente do sistema
        /// </summary>
        public async Task StartIntelligentMonitoringAsync()
        {
            lock (_lock)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(RealIntelligenceService));
                if (_isActive) return;

                _isActive = true;
                _monitoringCts = new CancellationTokenSource();
            }

            _logger.LogInfo("[RealIntelligence] Iniciando monitoramento inteligente...");

            // Iniciar coleta de métricas em background
            _ = Task.Run(() => MetricsCollectionLoopAsync(_monitoringCts.Token));

            // Iniciar análise de padrões
            _ = Task.Run(() => PatternAnalysisLoopAsync(_monitoringCts.Token));

            // Iniciar previsões
            _ = Task.Run(() => PredictionLoopAsync(_monitoringCts.Token));

            _logger.LogSuccess("[RealIntelligence] Monitoramento inteligente iniciado");
        }

        /// <summary>
        /// Para monitoramento inteligente
        /// </summary>
        public void StopIntelligentMonitoring()
        {
            lock (_lock)
            {
                if (!_isActive || _disposed) return;

                _isActive = false;
                _monitoringCts?.Cancel();
                _monitoringCts?.Dispose();
                _monitoringCts = null;
            }

            // Salvar dados antes de parar
            SaveHistoricalData();

            _logger.LogInfo("[RealIntelligence] Monitoramento inteligente parado");
        }

        /// <summary>
        /// Analisa padrões de uso do usuário
        /// </summary>
        public async Task<UsageAnalysisResult> AnalyzeUsagePatternsAsync()
        {
            _logger.LogInfo("[RealIntelligence] Analisando padrões de uso...");

            try
            {
                var result = await _patternAnalyzer.AnalyzePatternsAsync(_metricsHistory);

                _logger.LogSuccess($"[RealIntelligence] Análise concluída: {result.PatternsDetected.Count} padrões detectados");

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError("[RealIntelligence] Erro na análise de padrões", ex);
                return new UsageAnalysisResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        /// <summary>
        /// Prevé quando otimização seré necesséria
        /// </summary>
        public async Task<PredictionResult> PredictOptimizationNeedAsync()
        {
            _logger.LogInfo("[RealIntelligence] Prevendo necessidade de otimização...");

            try
            {
                var prediction = await _predictionEngine.PredictNextOptimizationAsync(_metricsHistory, _usagePatterns);

                _logger.LogInfo($"[RealIntelligence] Previséo: {(prediction.ShouldOptimize ? "Otimizar" : "Aguardar")} - Confianéa: {prediction.Confidence:P1}");

                return prediction;
            }
            catch (Exception ex)
            {
                _logger.LogError("[RealIntelligence] Erro na previséo", ex);
                return new PredictionResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        /// <summary>
        /// Gera recomendações inteligentes baseadas em contexto atual
        /// </summary>
        public async Task<OptimizationRecommendation> GetIntelligentRecommendationAsync()
        {
            _logger.LogInfo("[RealIntelligence] Gerando recomendação inteligente...");

            try
            {
                // Obter métricas atuais
                var currentMetrics = await _metricsCollector.GetCurrentMetricsAsync();

                // Analisar contexto
                var context = AnalyzeCurrentContext(currentMetrics);

                // Gerar recomendação baseada em métricas + padrões + contexto
                var recommendation = await GenerateRecommendationAsync(currentMetrics, context);

                // Salvar no histórico
                _recommendationsHistory[DateTime.UtcNow] = recommendation;

                _logger.LogSuccess($"[RealIntelligence] Recomendação gerada: {recommendation.Type} - Prioridade: {recommendation.Priority}");

                return recommendation;
            }
            catch (Exception ex)
            {
                _logger.LogError("[RealIntelligence] Erro ao gerar recomendação", ex);
                return new OptimizationRecommendation
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Type = OptimizationType.Cleanup,
                    Priority = RecommendationPriority.Low
                };
            }
        }

        /// <summary>
        /// Executa otimização inteligente baseada em aprendizado
        /// </summary>
        public async Task<VoltrisOptimizer.Services.Gamer.Models.IntelligentOptimizationResult> ExecuteIntelligentOptimizationAsync()
        {
            _logger.LogInfo("[RealIntelligence] Executando otimização inteligente...");

            var result = new VoltrisOptimizer.Services.Gamer.Models.IntelligentOptimizationResult
            {
                Success = false
            };

            try
            {
                // 1. Obter recomendação atual
                var recommendation = await GetIntelligentRecommendationAsync();

                // result.Recommendation = recommendation;
                // Propriedade não existe temporariamente

                if (!recommendation.ShouldExecute)
                {
                    result.Success = true;
                    result.Message = LocalizationService.Instance.GetString("RealIntelligenceNoOptimizationsNeeded");
                    _logger.LogInfo("[RealIntelligence] Nenhuma otimização necesséria");
                    return result;
                }

                // 2. Preparar contexto de otimização
                var context = new OptimizationContext
                {
                    AllowAdvancedTweaks = recommendation.AllowAdvancedTweaks,
                    IsGamingMode = recommendation.IsGamingRecommended,
                    IsBatteryPowered = IsOnBattery(),
                    CustomParameters = recommendation.CustomParameters
                };

                // 3. Executar otimização via UnifiedOptimizationService
                var optimizationResult = await _unifiedOptimization.ExecuteOptimizationAsync(recommendation.Type, context);

                result.OptimizationResult = optimizationResult;
                result.Success = optimizationResult.Success;
                result.Message = $"Otimização {recommendation.Type} executada: {optimizationResult.OptimizationsApplied.Count} ações";

                // 4. Aprender com o resultado
                await LearnFromOptimizationResultAsync(recommendation, optimizationResult);

                _logger.LogSuccess($"[RealIntelligence] Otimização inteligente concluída: {result.Message}");

                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Erro: {ex.Message}";
                _logger.LogError("[RealIntelligence] Erro na otimização inteligente", ex);
                return result;
            }
        }

        /// <summary>
        /// Obtém estatésticas de aprendizado
        /// </summary>
        public IntelligenceStatistics GetIntelligenceStatistics()
        {
            lock (_lock)
            {
                return new IntelligenceStatistics
                {
                    MetricsCollected = _metricsHistory.Count,
                    PatternsDetected = _usagePatterns.Count,
                    RecommendationsMade = _recommendationsHistory.Count,
                    Accuracy = CalculateAccuracy(),
                    LastAnalysis = _usagePatterns.OrderByDescending(p => p.LastDetected).FirstOrDefault()?.LastDetected ?? DateTime.MinValue,
                    IsLearningActive = _isActive
                };
            }
        }

        #region Métodos Privados

        private async Task MetricsCollectionLoopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInfo("[RealIntelligence] Loop de coleta de métricas iniciado");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Coletar métricas atuais
                    var metrics = await _metricsCollector.GetCurrentMetricsAsync();

                    var snapshot = new SystemMetricsSnapshot
                    {
                        Timestamp = DateTime.UtcNow,
                        CpuUsage = metrics.CpuUsage,
                        MemoryUsage = metrics.MemoryUsage,
                        DiskUsage = metrics.DiskUsage,
                        NetworkLatency = metrics.NetworkLatency,
                        ActiveProcesses = metrics.ActiveProcesses,
                        IsGamingMode = metrics.IsGamingMode,
                        IsOnBattery = metrics.IsOnBattery
                    };

                    lock (_lock)
                    {
                        _metricsHistory.Add(snapshot);

                        // Manter apenas éltimas 1000 mediéées
                        if (_metricsHistory.Count > 500)
                        {
                            _metricsHistory.RemoveAt(0);
                        }
                    }

                    // Log detalhado para debug
                    if (_metricsHistory.Count % 10 == 0) // A cada 10 mediéées
                    {
                        _logger.LogDebug($"[RealIntelligence] Métricas coletadas: {_metricsHistory.Count} | CPU: {metrics.CpuUsage:F1}% | RAM: {metrics.MemoryUsage:F1}%");
                    }

                    await Task.Delay(TimeSpan.FromSeconds(120), cancellationToken); // Coleta a cada 2 minutos (reduzido de 30s)
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning($"[RealIntelligence] Erro na coleta de métricas: {ex.Message}");
                    await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
                }
            }

            _logger.LogInfo("[RealIntelligence] Loop de coleta de métricas finalizado");
        }

        private async Task PatternAnalysisLoopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInfo("[RealIntelligence] Loop de análise de padrões iniciado");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Analisar padrões a cada 5 minutos
                    await Task.Delay(TimeSpan.FromMinutes(15), cancellationToken);

                    List<SystemMetricsSnapshot> snapshots;

                    lock (_lock)
                    {
                        snapshots = _metricsHistory.TakeLast(100).ToList();
                    }

                    if (snapshots.Count < 20) // Precisa de pelo menos 20 mediéées
                    {
                        continue;
                    }

                    var patterns = await _patternAnalyzer.DetectPatternsAsync(snapshots);

                    lock (_lock)
                    {
                        _usagePatterns.AddRange(patterns);

                        // Manter apenas 50 padrões
                        if (_usagePatterns.Count > 50)
                        {
                            _usagePatterns.RemoveRange(0, _usagePatterns.Count - 50);
                        }
                    }

                    if (patterns.Any())
                    {
                        _logger.LogInfo($"[RealIntelligence] {patterns.Count} novos padrões detectados");

                        foreach (var pattern in patterns.Take(3))
                        {
                            _logger.LogDebug($"[RealIntelligence] Padréo: {pattern.Type} - Confianéa: {pattern.Confidence:P1}");
                        }
                    }
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning($"[RealIntelligence] Erro na análise de padrões: {ex.Message}");
                }
            }

            _logger.LogInfo("[RealIntelligence] Loop de análise de padrões finalizado");
        }

        private async Task PredictionLoopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInfo("[RealIntelligence] Loop de previséo iniciado");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Prever a cada 10 minutos
                    await Task.Delay(TimeSpan.FromMinutes(30), cancellationToken);

                    List<SystemMetricsSnapshot> snapshots;
                    List<UsagePattern> patterns;

                    lock (_lock)
                    {
                        snapshots = _metricsHistory.TakeLast(200).ToList();
                        patterns = _usagePatterns.ToList();
                    }

                    if (snapshots.Count < 50 || !patterns.Any())
                    {
                        continue;
                    }

                    var prediction = await _predictionEngine.PredictNextOptimizationAsync(snapshots, patterns);

                    if (prediction.ShouldOptimize && prediction.Confidence > 0.7)
                    {
                        _logger.LogInfo($"[RealIntelligence] Previséo de alta confianéa: {prediction.Reason}");

                        // Gerar notificação ou executar automaticamente se configurado
                        if (prediction.AutoExecute)
                        {
                            _ = Task.Run(async () => await ExecuteIntelligentOptimizationAsync());
                        }
                    }
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning($"[RealIntelligence] Erro na previséo: {ex.Message}");
                }
            }

            _logger.LogInfo("[RealIntelligence] Loop de previséo finalizado");
        }

        private RealIntelligenceSystemContext AnalyzeCurrentContext(SystemMetrics metrics)
        {
            return new RealIntelligenceSystemContext
            {
                IsGamingActive = metrics.IsGamingMode,
                IsOnBattery = metrics.IsOnBattery,
                SystemLoad = metrics.CpuUsage > 80 || metrics.MemoryUsage > 80 ? SystemLoad.High : SystemLoad.Normal,
                TimeOfDay = DateTime.UtcNow.Hour,
                DayOfWeek = DateTime.UtcNow.DayOfWeek
            };
        }

        private async Task<OptimizationRecommendation> GenerateRecommendationAsync(SystemMetrics metrics, RealIntelligenceSystemContext context)
        {
            var recommendation = new OptimizationRecommendation
            {
                Timestamp = DateTime.UtcNow,
                Success = true
            };

            // Légica baseada em métricas reais
            if (metrics.DiskUsage > 85)
            {
                recommendation.Type = OptimizationType.Cleanup;
                recommendation.Priority = RecommendationPriority.High;
                recommendation.Reason = $"Uso de disco crético: {metrics.DiskUsage:F1}%";
                recommendation.ShouldExecute = true;
            }
            else if (metrics.MemoryUsage > 85)
            {
                recommendation.Type = OptimizationType.Performance;
                recommendation.Priority = RecommendationPriority.High;
                recommendation.Reason = $"Uso de memória alto: {metrics.MemoryUsage:F1}%";
                recommendation.ShouldExecute = true;
            }
            else if (context.IsGamingActive)
            {
                recommendation.Type = OptimizationType.Gaming;
                recommendation.Priority = RecommendationPriority.Medium;
                recommendation.Reason = "Modo gaming detectado";
                recommendation.ShouldExecute = true;
                recommendation.IsGamingRecommended = true;
            }
            else if (metrics.CpuUsage > 75)
            {
                recommendation.Type = OptimizationType.Performance;
                recommendation.Priority = RecommendationPriority.Medium;
                recommendation.Reason = $"Uso de CPU elevado: {metrics.CpuUsage:F1}%";
                recommendation.ShouldExecute = true;
            }
            else if (context.IsOnBattery)
            {
                recommendation.Type = OptimizationType.Performance;
                recommendation.Priority = RecommendationPriority.Low;
                recommendation.Reason = "Sistema operando em bateria";
                recommendation.ShouldExecute = false; // Néo otimizar em bateria
            }
            else
            {
                recommendation.Type = OptimizationType.Full;
                recommendation.Priority = RecommendationPriority.Low;
                recommendation.Reason = "Manutenééo preventiva";
                recommendation.ShouldExecute = false;
            }

            // Considerar padrões históricos
            var recentPatterns = _usagePatterns.Where(p => (DateTime.UtcNow - p.LastDetected).TotalHours < 24).ToList();

            if (recentPatterns.Any(p => p.Type == PatternType.HighMemoryUsage))
            {
                recommendation.Priority = RecommendationPriority.High;
                recommendation.Reason += "+ Padréo de alto uso de memória detectado";
            }

            return recommendation;
        }

        private async Task LearnFromOptimizationResultAsync(OptimizationRecommendation recommendation, UnifiedOptimizationResult result)
        {
            try
            {
                // Criar feedback de aprendizado
                var feedback = new VoltrisOptimizer.Services.Gamer.Models.IntelligentOptimizationResult
                {
                    Success = result.Success,
                    ErrorMessage = result.Success ? "Otimização aplicada com sucesso" : "Falha na otimização"
                };

                // Salvar feedback para aprendizado futuro - comentado temporariamente
                // await _patternAnalyzer.LearnFromFeedbackAsync(feedback);

                _logger.LogDebug($"[RealIntelligence] Aprendizado: {(feedback.Success ? "Efetivo" : "Inefetivo")}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[RealIntelligence] Erro no aprendizado: {ex.Message}");
            }
        }

        private bool IsOnBattery()
        {
            try
            {
                var powerStatus = System.Windows.Forms.SystemInformation.PowerStatus;
                var batteryStatus = powerStatus.BatteryChargeStatus != System.Windows.Forms.BatteryChargeStatus.NoSystemBattery;
                return batteryStatus;
            }
            catch
            {
                return false;
            }
        }

        private double CalculateAccuracy()
        {
            // Calcular preciséo das recomendações baseadas nos resultados
            var recentRecommendations = _recommendationsHistory.TakeLast(20).ToList();

            if (!recentRecommendations.Any()) return 0;

            var accurate = recentRecommendations.Count(r => r.Value.ShouldExecute && _usagePatterns.Any(p => (DateTime.UtcNow - p.LastDetected).TotalMinutes < 30));

            return recentRecommendations.Count > 0 ? (double)accurate / recentRecommendations.Count : 0;
        }

        private void LoadHistoricalData()
        {
            try
            {
                // Carregar métricas
                if (File.Exists(_metricsPath))
                {
                    var metricsJson = File.ReadAllText(_metricsPath);
                    var metrics = JsonSerializer.Deserialize<List<SystemMetricsSnapshot>>(metricsJson);

                    if (metrics != null)
                    {
                        lock (_lock)
                        {
                            _metricsHistory.AddRange(metrics.TakeLast(500)); // Limitar a 500
                        }
                    }
                }

                // Carregar padrões
                if (File.Exists(_patternsPath))
                {
                    var patternsJson = File.ReadAllText(_patternsPath);
                    var patterns = JsonSerializer.Deserialize<List<UsagePattern>>(patternsJson);

                    if (patterns != null)
                    {
                        lock (_lock)
                        {
                            _usagePatterns.AddRange(patterns.TakeLast(25)); // Limitar a 25
                        }
                    }
                }

                _logger.LogInfo($"[RealIntelligence] Dados carregados: {_metricsHistory.Count} métricas, {_usagePatterns.Count} padrões");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[RealIntelligence] Erro ao carregar dados históricos: {ex.Message}");
            }
        }

        private void SaveHistoricalData()
        {
            try
            {
                List<SystemMetricsSnapshot> metricsToSave;
                List<UsagePattern> patternsToSave;

                lock (_lock)
                {
                    metricsToSave = _metricsHistory.TakeLast(500).ToList();
                    patternsToSave = _usagePatterns.TakeLast(25).ToList();
                }

                // Salvar métricas
                var metricsJson = JsonSerializer.Serialize(metricsToSave, new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,  WriteIndented = true });
                File.WriteAllText(_metricsPath, metricsJson);

                // Salvar padrões
                var patternsJson = JsonSerializer.Serialize(patternsToSave, new JsonSerializerOptions {   WriteIndented = true });
                File.WriteAllText(_patternsPath, patternsJson);

                _logger.LogDebug($"[RealIntelligence] Dados salvos: {metricsToSave.Count} métricas, {patternsToSave.Count} padrões");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[RealIntelligence] Erro ao salvar dados históricos: {ex.Message}");
            }
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;

            StopIntelligentMonitoring();

            lock (_lock)
            {
                _disposed = true;
                SaveHistoricalData();
            }

            _logger.LogInfo("[RealIntelligence] Serviço disposed");
        }
    }

    #region Classes de Suporte

    public class SystemMetrics
    {
        public double CpuUsage { get; set; }
        public double MemoryUsage { get; set; }
        public double DiskUsage { get; set; }
        public double NetworkLatency { get; set; }
        public int ActiveProcesses { get; set; }
        public bool IsGamingMode { get; set; }
        public bool IsOnBattery { get; set; }
        public DateTime Timestamp { get; set; }

        // Propriedades adicionais para compatibilidade
        public double CpuTemperature { get; set; }
        public double GpuTemperature { get; set; }
        public double RamPressure { get; set; }
        public double InputLatency { get; set; }
        public double FrameTimeVariance { get; set; }
        public double GpuLoad { get; set; }
        public double Fps { get; set; }
        public double FrameTime { get; set; }
        public double RamUsagePercent { get; set; }
        public double DiskUsagePercent { get; set; }
        public double LastBootTimeSeconds { get; set; }
    }

    public class SystemMetricsSnapshot
    {
        public DateTime Timestamp { get; set; }
        public double CpuUsage { get; set; }
        public double MemoryUsage { get; set; }
        public double DiskUsage { get; set; }
        public double NetworkLatency { get; set; }
        public int ActiveProcesses { get; set; }
        public bool IsGamingMode { get; set; }
        public bool IsOnBattery { get; set; }
    }

    public class UsagePattern
    {
        public PatternType Type { get; set; }
        public double Confidence { get; set; }
        public DateTime LastDetected { get; set; }
        public Dictionary<string, object> Attributes { get; set; } = new();
    }

    public enum PatternType
    {
        HighCpuUsage,
        HighMemoryUsage,
        GamingSession,
        LowBatteryUsage,
        NetworkIntensive,
        DiskIntensive
    }

    public class UsageAnalysisResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public List<UsagePattern> PatternsDetected { get; set; } = new();
        public Dictionary<string, double> Metrics { get; set; } = new();
    }

    public class PredictionResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public bool ShouldOptimize { get; set; }
        public double Confidence { get; set; }
        public string Reason { get; set; }
        public bool AutoExecute { get; set; }
        public DateTime PredictedOptimalTime { get; set; }
    }

    public class OptimizationRecommendation
    {
        public DateTime Timestamp { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public OptimizationType Type { get; set; }
        public RecommendationPriority Priority { get; set; }
        public string Reason { get; set; }
        public bool ShouldExecute { get; set; }
        public bool AllowAdvancedTweaks { get; set; }
        public bool IsGamingRecommended { get; set; }
        public Dictionary<string, object> CustomParameters { get; set; } = new();
    }

    public class IntelligenceStatistics
    {
        public int MetricsCollected { get; set; }
        public int PatternsDetected { get; set; }
        public int RecommendationsMade { get; set; }
        public double Accuracy { get; set; }
        public DateTime LastAnalysis { get; set; }
        public bool IsLearningActive { get; set; }
    }

    public class RealIntelligenceSystemContext
    {
        public bool IsGamingActive { get; set; }
        public bool IsOnBattery { get; set; }
        public SystemLoad SystemLoad { get; set; }
        public int TimeOfDay { get; set; }
        public DayOfWeek DayOfWeek { get; set; }
        public List<string> ActiveProcesses { get; set; } = new();
        public Assistant.ResourceUsage ResourceUsage { get; set; } = new();

        // Flags de requisiéées explécitas
        public bool RequestedGamingMode { get; set; }
        public bool RequestedBatteryOptimization { get; set; }
        public bool RequestedLatencyOptimization { get; set; }

        public override string ToString()
        {
            return $"Contexto: Gaming = {IsGamingActive}, Battery = {IsOnBattery}, Load = {SystemLoad}, Time = {TimeOfDay} h";
        }
    }

    public enum SystemLoad
    {
        Low,
        Normal,
        High,
        Critical
    }

    public class OptimizationFeedback
    {
        public OptimizationRecommendation Recommendation { get; set; }
        public UnifiedOptimizationResult Result { get; set; }
        public DateTime Timestamp { get; set; }
        public bool WasEffective { get; set; }
    }

    #endregion
} 



