using System;

using System.Collections.Generic;

using System.Linq;

using System.Threading.Tasks;

using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Intelligence 
{
    /// <summary>
    /// Motor de previsão baseado em métricas e padrões históricos
    /// Implementa algoritmos reais de previsão de necessidade de otimização
    /// </summary>
    public class PredictionEngine
 {
private readonly ILoggingService _logger;

 private readonly Dictionary < string, double > _predictionAccuracy = new();

 public PredictionEngine(ILoggingService logger) {
_logger = logger??throw new ArgumentNullException(nameof(logger));

 _logger.LogInfo("[PredictionEngine]Motor de previsão inicializado");

 }
    /// <summary>
    /// Prevê quando a próxima otimização será necessária
    /// </summary>
    public async Task<PredictionResult> PredictNextOptimizationAsync(List<SystemMetricsSnapshot> metricsHistory, List<UsagePattern> patterns)
    {
        _logger.LogInfo("[PredictionEngine]Gerando previsão de otimização...");

 try {
var result = new PredictionResult {
Success = true};

//Análise de tendências
        var trendAnalysis = AnalyzeTrends(metricsHistory);

 var patternAnalysis = AnalyzePatternsForPrediction(patterns);

 var seasonalAnalysis = AnalyzeSeasonalTrends(metricsHistory);

//Calcular probabilidade de necessidade de otimização
        var optimizationProbability = CalculateOptimizationProbability(trendAnalysis, patternAnalysis, seasonalAnalysis);

 result.ShouldOptimize = optimizationProbability > 0.6;

 result.Confidence = CalculatePredictionConfidence(trendAnalysis, patternAnalysis, seasonalAnalysis);

 result.PredictedOptimalTime = PredictOptimalTime(metricsHistory, patterns);

 result.Reason = GeneratePredictionReason(trendAnalysis, patternAnalysis, seasonalAnalysis, optimizationProbability);

 result.AutoExecute = result.ShouldOptimize&&result.Confidence > 0.8;

 _logger.LogInfo($"[PredictionEngine]Previsão: {(result.ShouldOptimize ? "Otimizar" : "Aguardar")} - Confiança: {result.Confidence:P1}");

 _logger.LogDebug($"[PredictionEngine]Razão: {result.Reason}");

 return result;

 }
 catch(Exception ex) {
_logger.LogError("[PredictionEngine]Erro na previsão", ex);

 return new PredictionResult {
Success = false, ErrorMessage = ex.Message, ShouldOptimize = false, Confidence = 0};

 }
 }
 #region Análise de Tendências
    private TrendAnalysis AnalyzeTrends(List<SystemMetricsSnapshot> metrics) {
var recentMetrics = metrics.TakeLast(50).ToList();

//últimas 50 medições
        if (recentMetrics.Count < 10) {
return new TrendAnalysis {
HasValidData = false};

 }
 var analysis = new TrendAnalysis {
HasValidData = true};

//Tendência de CPU
        analysis.CpuTrend = CalculateTrend(recentMetrics.Select(m => m.CpuUsage));

 analysis.CpuSlope = CalculateSlope(recentMetrics.Select((m, i) => new {
X = i, Y = m.CpuUsage}
 ));

//Tendência de Memória
        analysis.MemoryTrend = CalculateTrend(recentMetrics.Select(m => m.MemoryUsage));

 analysis.MemorySlope = CalculateSlope(recentMetrics.Select((m, i) => new {
X = i, Y = m.MemoryUsage}
 ));

//Tendência de Disco
        analysis.DiskTrend = CalculateTrend(recentMetrics.Select(m => m.DiskUsage));

 analysis.DiskSlope = CalculateSlope(recentMetrics.Select((m, i) => new {
X = i, Y = m.DiskUsage}
 ));

//Volatilidade
        analysis.CpuVolatility = CalculateVolatility(recentMetrics.Select(m => m.CpuUsage));

 analysis.MemoryVolatility = CalculateVolatility(recentMetrics.Select(m => m.MemoryUsage));

 _logger.LogDebug($"[PredictionEngine]Tendências - CPU: {analysis.CpuTrend} ({analysis.CpuSlope:F3}) | RAM: {analysis.MemoryTrend} ({analysis.MemorySlope:F3})");

 return analysis;

 }
 private TrendDirection CalculateTrend(IEnumerable < double > values) {
var valuesList = values.ToList();

 if(valuesList.Count < 2)return TrendDirection.Stable;

 var firstHalf = valuesList.Take(valuesList.Count/2).Average();

 var secondHalf = valuesList.Skip(valuesList.Count/2).Average();

 var difference = secondHalf - firstHalf;

//Calcular desvio padrão manualmente
        var mean = valuesList.Average();

 var variance = valuesList.Sum(x => Math.Pow(x - mean, 2))/valuesList.Count;

 var standardDeviation = Math.Sqrt(variance);

 var threshold = standardDeviation*0.1;

//10% do desvio padrão
        if (difference > threshold) return TrendDirection.Increasing;

 if(difference < - threshold)return TrendDirection.Decreasing;

 return TrendDirection.Stable;

 }
 private double CalculateSlope(IEnumerable < object > points) {
var pointsList = points.ToList();

 if(pointsList.Count < 2)return 0;

//Usar reflection para acessar as propriedades X e Y
        var xValues = pointsList.Select(p => Convert.ToDouble(p.GetType().GetProperty("X")?.GetValue(p) ?? 0)).ToList();

 var yValues = pointsList.Select(p => Convert.ToDouble(p.GetType().GetProperty("Y")?.GetValue(p) ?? 0)).ToList();

 double xMean = xValues.Average();

 double yMean = yValues.Average();

 double numerator = 0;

 double denominator = 0;

 for(int i = 0;

 i < pointsList.Count;

 i++) {
numerator += (xValues[i] - xMean)*(yValues[i] - yMean);

 denominator += (xValues[i] - xMean)*(xValues[i] - xMean);

 }
 return denominator != 0 ? numerator/denominator : 0;

 }
 private double CalculateVolatility(IEnumerable < double > values) {
var valuesList = values.ToList();

 if(valuesList.Count < 2)return 0;

 var mean = valuesList.Average();

 var squaredDiffs = valuesList.Select(v => Math.Pow(v - mean, 2));

 var variance = squaredDiffs.Average();

 return Math.Sqrt(variance);

 }
 #endregion
    #region Análise de Padrões
    private PatternAnalysis AnalyzePatternsForPrediction(List<UsagePattern> patterns) {
var analysis = new PatternAnalysis();

 if(!patterns.Any()) {
return analysis;

 }
//Padrões recentes (últimas 24 horas)
        var recentPatterns = patterns.Where(p => (DateTime.UtcNow - p.LastDetected).TotalHours <= 24).ToList();

 analysis.RecentPatternCount = recentPatterns.Count;

 analysis.HighConfidencePatterns = recentPatterns.Count(p => p.Confidence > 0.7);

 analysis.CriticalPatterns = recentPatterns.Count(p => p.Confidence > 0.9);

//Análise por tipo de padrão
        analysis.HighCpuPatternCount = recentPatterns.Count(p => p.Type == PatternType.HighCpuUsage);

 analysis.HighMemoryPatternCount = recentPatterns.Count(p => p.Type == PatternType.HighMemoryUsage);

 analysis.GamingPatternCount = recentPatterns.Count(p => p.Type == PatternType.GamingSession);

 analysis.BatteryPatternCount = recentPatterns.Count(p => p.Type == PatternType.LowBatteryUsage);

//Frequência de padrões
        analysis.PatternFrequency = CalculatePatternFrequency(recentPatterns);

//Tendência de padrões
        analysis.PatternTrend = CalculatePatternTrend(patterns);

 _logger.LogDebug($"[PredictionEngine]Padrões recentes: {analysis.RecentPatternCount} | Alta confiança: {analysis.HighConfidencePatterns}");

 return analysis;

 }
 private double CalculatePatternFrequency(List < UsagePattern > patterns) {
if(!patterns.Any())return 0;

 var timeSpan = (patterns.Max(p => p.LastDetected) - patterns.Min(p => p.LastDetected)).TotalHours;

 return timeSpan > 0?patterns.Count/timeSpan: 0;

 }
 private TrendDirection CalculatePatternTrend(List < UsagePattern > patterns) {
var sortedPatterns = patterns.OrderBy(p => p.LastDetected).ToList();

 if(sortedPatterns.Count < 4)return TrendDirection.Stable;

 var firstQuarter = sortedPatterns.Take(sortedPatterns.Count/4).ToList();

 var lastQuarter = sortedPatterns.Skip(sortedPatterns.Count*3/4).ToList();

 var firstQuarterAvg = firstQuarter.Average(p => p.Confidence);

 var lastQuarterAvg = lastQuarter.Average(p => p.Confidence);

 var difference = lastQuarterAvg - firstQuarterAvg;

 var threshold = 0.1;

 if(difference > threshold)return TrendDirection.Increasing;

 if(difference < - threshold)return TrendDirection.Decreasing;

 return TrendDirection.Stable;

 }
 #endregion
    #region Análise Sazonal
    private SeasonalAnalysis AnalyzeSeasonalTrends(List<SystemMetricsSnapshot> metrics) {
var analysis = new SeasonalAnalysis();

 if(metrics.Count < 100)return analysis;

//Análise por hora do dia
        analysis.HourlyPatterns = AnalyzeHourlyPatterns(metrics);

//Análise por dia da semana
        analysis.WeekdayPatterns = AnalyzeWeekdayPatterns(metrics);

//Análise por período (manhã/tarde/noite)
        analysis.TimeOfDayPatterns = AnalyzeTimeOfDayPatterns(metrics);

 _logger.LogDebug($"[PredictionEngine]Padrões sazonais: Horas {analysis.HourlyPatterns.Count} | Dias {analysis.WeekdayPatterns.Count}");

 return analysis;

 }
 private Dictionary < int, double > AnalyzeHourlyPatterns(List < SystemMetricsSnapshot > metrics) {
var hourlyData = new Dictionary < int, List < double >  > ();

 foreach(var metric in metrics) {
var hour = metric.Timestamp.Hour;

 if(!hourlyData.ContainsKey(hour))hourlyData[hour] = new List < double > ();

 hourlyData[hour].Add(metric.CpuUsage + metric.MemoryUsage);

 }
 return hourlyData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Average());

 }
 private Dictionary < DayOfWeek, double > AnalyzeWeekdayPatterns(List < SystemMetricsSnapshot > metrics) {
var weekdayData = new Dictionary < DayOfWeek, List < double >  > ();

 foreach(var metric in metrics) {
var weekday = metric.Timestamp.DayOfWeek;

 if(!weekdayData.ContainsKey(weekday))weekdayData[weekday] = new List < double > ();

 weekdayData[weekday].Add(metric.CpuUsage + metric.MemoryUsage);

 }
 return weekdayData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Average());

 }
 private Dictionary < TimeOfDay, double > AnalyzeTimeOfDayPatterns(List < SystemMetricsSnapshot > metrics) {
var timeData = new Dictionary < TimeOfDay, List < double >  > ();

 foreach(var metric in metrics) {
var hour = metric.Timestamp.Hour;

 var timeOfDay = hour < 12?TimeOfDay.Morning: hour < 18?TimeOfDay.Afternoon: TimeOfDay.Evening;

 if(!timeData.ContainsKey(timeOfDay))timeData[timeOfDay] = new List < double > ();

 timeData[timeOfDay].Add(metric.CpuUsage + metric.MemoryUsage);

 }
 return timeData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Average());

 }
 #endregion
    #region Cálculos de Previsão
    private double CalculateOptimizationProbability(TrendAnalysis trends, PatternAnalysis patterns, SeasonalAnalysis seasonal) {
double probability = 0;

//Fator de tendências (40% do pesão)
        if (trends.HasValidData) {
var trendScore = 0;

 if (trends.CpuTrend == TrendDirection.Increasing && trends.CpuSlope > 0.5) trendScore = (int)(trendScore + 0.3);

 if (trends.MemoryTrend == TrendDirection.Increasing && trends.MemorySlope > 0.3) trendScore = (int)(trendScore + 0.3);

 if (trends.CpuVolatility > 20) trendScore = (int)(trendScore + 0.2);

 if (trends.MemoryVolatility > 15) trendScore = (int)(trendScore + 0.2);

 probability += trendScore * 0.4;

 }
//Fator de padrões (40% do pesão)
        if (patterns.RecentPatternCount > 0) {
var patternScore = Math.Min(patterns.RecentPatternCount/10.0, 1.0);

//Normalizar para 0 - 1
        patternScore += patterns.HighConfidencePatterns * 0.1;

 patternScore += patterns.CriticalPatterns * 0.2;

 patternScore = Math.Min(patternScore, 1.0);

 probability += patternScore * 0.4;

 }
//Fator sazonal (20% do pesão)
        if (seasonal.HourlyPatterns.Any()) {
var currentHour = DateTime.UtcNow.Hour;

 var currentLoad = seasonal.HourlyPatterns.GetValueOrDefault(currentHour, 0);

 var avgLoad = seasonal.HourlyPatterns.Values.Average();

 var seasonalScore = currentLoad > avgLoad * 1.2 ? 0.5 : 0.1;

 probability += seasonalScore * 0.2;

 }
 return Math.Min(probability, 1.0);

 }
 private double CalculatePredictionConfidence(TrendAnalysis trends, PatternAnalysis patterns, SeasonalAnalysis seasonal) {
double confidence = 0.5;

//Base confiança
        //Confiança baseada na quantidade de dados
        if (trends.HasValidData) confidence += 0.1;

 if (patterns.RecentPatternCount > 5) confidence += 0.1;

 if (patterns.HighConfidencePatterns > 0) confidence += 0.1;

 if (seasonal.HourlyPatterns.Count > 10) confidence += 0.1;

//Confiança baseada na consistência
        if (trends.CpuVolatility < 30) confidence += 0.1;

 if (trends.MemoryVolatility < 20) confidence += 0.1;

 return Math.Min(confidence, 1.0);

 }
 private DateTime PredictOptimalTime(List < SystemMetricsSnapshot > metrics, List < UsagePattern > patterns) {
var now = DateTime.UtcNow;

//Se há padrões gaming recentes, otimizar antes do próximo horário de pico
        var gamingPatterns = patterns.Where(p => p.Type == PatternType.GamingSession).ToList();

 if(gamingPatterns.Any()) {
var lastGaming = gamingPatterns.Max(p => p.LastDetected);

 var hoursSinceGaming = (now - lastGaming).TotalHours;

 if(hoursSinceGaming < 6) {
return now.AddHours(6 - hoursSinceGaming);

//Otimizar 6 horas após gaming
        }
 }
//Prever baseado em padrões horários
        var currentHour = now.Hour;

//Se for in�cio do dia (6 - 9), otimizar para o dia de trabalho
        if (currentHour >= 6 && currentHour <= 9) {
return now.AddHours(1);

 }
//Se for fim do dia (17 - 20), otimizar para uso noturno
        if (currentHour >= 17 && currentHour <= 20) {
return now.AddHours(2);

 }
//Padrão: otimizar em 2 horas se não houver padrões específicos
        return now.AddHours(2);

 }
 private string GeneratePredictionReason(TrendAnalysis trends, PatternAnalysis patterns, SeasonalAnalysis seasonal, double probability) {
var reasãons = new List < string > ();

 if (trends.HasValidData) {
        if (trends.CpuTrend == TrendDirection.Increasing) reasãons.Add("Tendência de aumento no uso de CPU");

 if (trends.MemoryTrend == TrendDirection.Increasing) reasãons.Add("Tendência de aumento no uso de memória");

 if (trends.CpuVolatility > 25) reasãons.Add("Alta volatilidade de CPU detectada");

 }
 if (patterns.RecentPatternCount > 0) {
        reasãons.Add($"{patterns.RecentPatternCount} padrões de uso detectados nas últimas 24h");

 if (patterns.CriticalPatterns > 0) reasãons.Add($"{patterns.CriticalPatterns} padrões críticos identificados");

 }
 if(seasonal.HourlyPatterns.Any()) {
var currentHour = DateTime.UtcNow.Hour;

 var currentLoad = seasonal.HourlyPatterns.GetValueOrDefault(currentHour, 0);

 var avgLoad = seasonal.HourlyPatterns.Values.Average();

 if (currentLoad > avgLoad * 1.3) reasãons.Add("Carga atual acima da mdia histórica para estáe horário");

 }
 return reasãons.Any() ? string.Join("; ", reasãons) : "Análise preditiva baseada em métricas históricas";

 }
 #endregion
    }
    #region Classes de Suporte
    public class TrendAnalysis
 {
public bool HasValidData {
get;

 set;

 }
 public TrendDirection CpuTrend {
get;

 set;

 }
 public TrendDirection MemoryTrend {
get;

 set;

 }
 public TrendDirection DiskTrend {
get;

 set;

 }
 public double CpuSlope {
get;

 set;

 }
 public double MemorySlope {
get;

 set;

 }
 public double DiskSlope {
get;

 set;

 }
 public double CpuVolatility {
get;

 set;

 }
 public double MemoryVolatility {
get;

 set;

 }
 }
 public class PatternAnalysis
 {
public int RecentPatternCount {
get;

 set;

 }
 public int HighConfidencePatterns {
get;

 set;

 }
 public int CriticalPatterns {
get;

 set;

 }
 public int HighCpuPatternCount {
get;

 set;

 }
 public int HighMemoryPatternCount {
get;

 set;

 }
 public int GamingPatternCount {
get;

 set;

 }
 public int BatteryPatternCount {
get;

 set;

 }
 public double PatternFrequency {
get;

 set;

 }
 public TrendDirection PatternTrend {
get;

 set;

 }
 }
 public class SeasonalAnalysis
 {
public Dictionary < int, double > HourlyPatterns {
get;

 set;

 }
 = new();

 public Dictionary < DayOfWeek, double > WeekdayPatterns {
get;

 set;

 }
 = new();

 public Dictionary < TimeOfDay, double > TimeOfDayPatterns {
get;

 set;

 }
 = new();

 }
 public enum TrendDirection
 {
Decreasing, Stable, Increasing}
 public enum TimeOfDay
 {
Morning, Afternoon, Evening}
 #endregion
}
 
