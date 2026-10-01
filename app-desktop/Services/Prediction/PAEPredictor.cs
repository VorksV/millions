using System;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Prediction
{
    /// <summary>
    /// Preditor de stutter baseado em análise de dados do sistema.
    /// </summary>
    public class PAEPredictor
    {
        private readonly StutterPatternAnalyzer _patternAnalyzer;
        private readonly MachineLearningModel _mlModel;

        /// <summary>
        /// Inicializa uma nova instância de <see cref="PAEPredictor"/>.
        /// </summary>
        /// <param name="patternAnalyzer">Analisador de padrões de stutter.</param>
        /// <param name="mlModel">Modelo de machine learning para predição.</param>
        public PAEPredictor(StutterPatternAnalyzer patternAnalyzer, MachineLearningModel mlModel)
        {
            _patternAnalyzer = patternAnalyzer ?? throw new ArgumentNullException(nameof(patternAnalyzer));
            _mlModel = mlModel ?? throw new ArgumentNullException(nameof(mlModel));
        }

        /// <summary>
        /// Prediz se uma stutter vai ocorrer nos próximos segundos.
        /// </summary>
        /// <param name="systemData">Dados atuais do sistema.</param>
        /// <param name="predictionWindowSeconds">Janela de predição em segundos.</param>
        /// <returns>Predição de stutter.</returns>
        public async Task<StutterPrediction> PredictStutterAsync(SystemDataSnapshot systemData, int predictionWindowSeconds)
        {
            if (systemData == null)
                throw new ArgumentNullException(nameof(systemData));

            // Analisa padrões de stutter nos dados históricos
            var patternAnalysis = _patternAnalyzer.AnalyzePatterns(systemData);
            
            // Usa o modelo ML para fazer a predição
            var mlPrediction = await _mlModel.PredictAsync(systemData, predictionWindowSeconds);
            
            // Combina ambos os resultados para uma predição final
            var finalPrediction = CombinePredictions(patternAnalysis, mlPrediction, systemData);
            
            return finalPrediction;
        }

        /// <summary>
        /// Combina as predições do analisador de padrões e do modelo ML.
        /// </summary>
        /// <param name="patternAnalysis">Resultado da análise de padrões.</param>
        /// <param name="mlPrediction">Predição do modelo ML.</param>
        /// <param name="systemData">Dados do sistema.</param>
        /// <returns>Predição combinada.</returns>
        private StutterPrediction CombinePredictions(
            PatternAnalysisResult patternAnalysis,
            MLPredictionResult mlPrediction,
            SystemDataSnapshot systemData)
        {
            // Calcula pesos para cada fonte de predição
            double patternWeight = 0.4;
            double mlWeight = 0.6;
            
            // Combina as probabilidades
            double combinedProbability = 
                (patternAnalysis.StutterProbability * patternWeight) +
                (mlPrediction.StutterProbability * mlWeight);
                
            // Determina os fatores combinados
            var combinedFactors = CombineFactors(patternAnalysis.Factors, mlPrediction.Factors);
            
            // Estima o tempo até a stutter
            int estimatedTime = EstimateTimeToStutter(patternAnalysis, mlPrediction, systemData);
            
            return new StutterPrediction
            {
                Probability = Math.Min(1.0, Math.Max(0.0, combinedProbability)),
                EstimatedTimeToStutterMs = estimatedTime,
                Factors = combinedFactors
            };
        }

        /// <summary>
        /// Combina os fatores de ambas as predições.
        /// </summary>
        /// <param name="patternFactors">Fatores da análise de padrões.</param>
        /// <param name="mlFactors">Fatores da predição ML.</param>
        /// <returns>Fatores combinados.</returns>
        private StutterFactors CombineFactors(StutterFactors patternFactors, StutterFactors mlFactors)
        {
            return new StutterFactors
            {
                HighCpuUsage = patternFactors.HighCpuUsage || mlFactors.HighCpuUsage,
                HighGpuUsage = patternFactors.HighGpuUsage || mlFactors.HighGpuUsage,
                MemorySpikes = patternFactors.MemorySpikes || mlFactors.MemorySpikes,
                HighIoActivity = patternFactors.HighIoActivity || mlFactors.HighIoActivity,
                BlockingSyscalls = patternFactors.BlockingSyscalls || mlFactors.BlockingSyscalls,
                HighCacheMisses = patternFactors.HighCacheMisses || mlFactors.HighCacheMisses,
                ExcessiveContextSwitches = patternFactors.ExcessiveContextSwitches || mlFactors.ExcessiveContextSwitches
            };
        }

        /// <summary>
        /// Estima o tempo até a próxima stutter.
        /// </summary>
        /// <param name="patternAnalysis">Resultado da análise de padrões.</param>
        /// <param name="mlPrediction">Predição do modelo ML.</param>
        /// <param name="systemData">Dados do sistema.</param>
        /// <returns>Tempo estimado em milissegundos.</returns>
        private int EstimateTimeToStutter(
            PatternAnalysisResult patternAnalysis,
            MLPredictionResult mlPrediction,
            SystemDataSnapshot systemData)
        {
            // Usa uma média ponderada das estimativas
            double patternWeight = 0.3;
            double mlWeight = 0.7;
            
            double combinedEstimate =
                (patternAnalysis.EstimatedTimeToStutterMs * patternWeight) +
                (mlPrediction.EstimatedTimeToStutterMs * mlWeight);
                
            // Garante que o valor esteja em um intervalo razoável
            return Math.Max(10, Math.Min(5000, (int)combinedEstimate));
        }
    }

    /// <summary>
    /// Analisador de padrões de stutter baseado em dados históricos.
    /// </summary>
    public class StutterPatternAnalyzer
    {
        /// <summary>
        /// Analisa padrões de stutter nos dados fornecidos.
        /// </summary>
        /// <param name="systemData">Dados do sistema.</param>
        /// <returns>Resultado da análise de padrões.</returns>
        public PatternAnalysisResult AnalyzePatterns(SystemDataSnapshot systemData)
        {
            var factors = IdentifyStutterFactors(systemData);
            var probability = CalculateStutterProbability(factors, systemData);
            var timeEstimate = EstimateTimeToStutter(factors, systemData);
            
            return new PatternAnalysisResult
            {
                StutterProbability = probability,
                EstimatedTimeToStutterMs = timeEstimate,
                Factors = factors
            };
        }

        /// <summary>
        /// Identifica fatores que contribuem para stutter.
        /// </summary>
        /// <param name="systemData">Dados do sistema.</param>
        /// <returns>Fatores identificados.</returns>
        private StutterFactors IdentifyStutterFactors(SystemDataSnapshot systemData)
        {
            return new StutterFactors
            {
                HighCpuUsage = systemData.CpuUsage.AverageLoadPercentage > 0.85,
                HighGpuUsage = systemData.GpuUsage.UsagePercentage > 0.90,
                MemorySpikes = DetectMemorySpikes(systemData),
                HighIoActivity = systemData.IoActivity.ReadsPerSecond > 1000 || 
                                systemData.IoActivity.WritesPerSecond > 1000,
                BlockingSyscalls = systemData.ProcessInfo.SyscallsPerSecond > 5000,
                HighCacheMisses = systemData.CpuUsage.CacheMissRate > 0.05,
                ExcessiveContextSwitches = systemData.CpuUsage.ContextSwitchesPerSecond > 10000
            };
        }

        /// <summary>
        /// Detecta picos de uso de memória.
        /// </summary>
        /// <param name="systemData">Dados do sistema.</param>
        /// <returns>Verdadeiro se picos forem detectados.</returns>
        private bool DetectMemorySpikes(SystemDataSnapshot systemData)
        {
            // Verifica se há um aumento significativo no uso de memória
            if (systemData.MemoryUsage.TransferRateMBps > 100)
                return true;
                
            // Verifica page faults excessivos
            if (systemData.MemoryUsage.PageFaultsPerSecond > 1000)
                return true;
                
            return false;
        }

        /// <summary>
        /// Calcula a probabilidade de stutter baseada nos fatores.
        /// </summary>
        /// <param name="factors">Fatores identificados.</param>
        /// <param name="systemData">Dados do sistema.</param>
        /// <returns>Probabilidade de stutter (0.0 a 1.0).</returns>
        private double CalculateStutterProbability(StutterFactors factors, SystemDataSnapshot systemData)
        {
            double probability = 0.0;
            int factorCount = 0;
            
            if (factors.HighCpuUsage) { probability += 0.3; factorCount++; }
            if (factors.HighGpuUsage) { probability += 0.25; factorCount++; }
            if (factors.MemorySpikes) { probability += 0.2; factorCount++; }
            if (factors.HighIoActivity) { probability += 0.15; factorCount++; }
            if (factors.BlockingSyscalls) { probability += 0.1; factorCount++; }
            if (factors.HighCacheMisses) { probability += 0.05; factorCount++; }
            if (factors.ExcessiveContextSwitches) { probability += 0.05; factorCount++; }
            
            // Ajusta com base na estabilidade dos tempos de frame
            if (systemData.FrameTimeHistory.FrameTimeDeviation > 5.0)
            {
                probability += 0.1;
            }
            
            // Normaliza com base no número de fatores presentes
            if (factorCount > 0)
            {
                probability = Math.Min(1.0, probability);
            }
            
            return probability;
        }

        /// <summary>
        /// Estima o tempo até a próxima stutter.
        /// </summary>
        /// <param name="factors">Fatores identificados.</param>
        /// <param name="systemData">Dados do sistema.</param>
        /// <returns>Tempo estimado em milissegundos.</returns>
        private int EstimateTimeToStutter(StutterFactors factors, SystemDataSnapshot systemData)
        {
            // Começa com um tempo base
            int baseTimeMs = 500;
            
            // Ajusta com base nos fatores
            if (factors.HighCpuUsage) baseTimeMs -= 100;
            if (factors.HighGpuUsage) baseTimeMs -= 80;
            if (factors.MemorySpikes) baseTimeMs -= 120;
            if (factors.HighIoActivity) baseTimeMs -= 90;
            if (factors.BlockingSyscalls) baseTimeMs -= 70;
            
            // Ajusta com base na instabilidade dos frames
            if (systemData.FrameTimeHistory.FrameTimeDeviation > 10.0)
            {
                baseTimeMs -= 50;
            }
            
            // Garante um valor mínimo
            return Math.Max(50, baseTimeMs);
        }
    }

    /// <summary>
    /// Modelo de machine learning para predição de stutter.
    /// </summary>
    public class MachineLearningModel
    {
        /// <summary>
        /// Realiza uma predição usando o modelo ML.
        /// </summary>
        /// <param name="systemData">Dados do sistema.</param>
        /// <param name="predictionWindowSeconds">Janela de predição.</param>
        /// <returns>Resultado da predição ML.</returns>
        public async Task<MLPredictionResult> PredictAsync(SystemDataSnapshot systemData, int predictionWindowSeconds)
        {
            // Esta seria uma implementação real usando um modelo ML
            // Por enquanto, simulamos uma predição baseada em regras
            
            var factors = new StutterFactors
            {
                HighCpuUsage = systemData.CpuUsage.AverageLoadPercentage > 0.8,
                HighGpuUsage = systemData.GpuUsage.UsagePercentage > 0.85,
                MemorySpikes = systemData.MemoryUsage.PageFaultsPerSecond > 500,
                HighIoActivity = systemData.IoActivity.AverageLatencyMs > 10,
                BlockingSyscalls = systemData.ProcessInfo.SyscallsPerSecond > 3000,
                HighCacheMisses = systemData.CpuUsage.CacheMissRate > 0.03,
                ExcessiveContextSwitches = systemData.CpuUsage.ContextSwitchesPerSecond > 8000
            };
            
            double probability = CalculateMLProbability(factors, systemData);
            int timeEstimate = EstimateMLTimeToStutter(factors, systemData);
            
            return new MLPredictionResult
            {
                StutterProbability = probability,
                EstimatedTimeToStutterMs = timeEstimate,
                Factors = factors
            };
        }

        /// <summary>
        /// Calcula a probabilidade usando algoritmos ML.
        /// </summary>
        /// <param name="factors">Fatores identificados.</param>
        /// <param name="systemData">Dados do sistema.</param>
        /// <returns>Probabilidade calculada.</returns>
        private double CalculateMLProbability(StutterFactors factors, SystemDataSnapshot systemData)
        {
            // Simulação de cálculo ML
            // Em uma implementação real, isso usaria um modelo treinado
            
            double score = 0.0;
            
            if (factors.HighCpuUsage) score += 0.25;
            if (factors.HighGpuUsage) score += 0.2;
            if (factors.MemorySpikes) score += 0.15;
            if (factors.HighIoActivity) score += 0.1;
            if (factors.BlockingSyscalls) score += 0.1;
            if (factors.HighCacheMisses) score += 0.05;
            if (factors.ExcessiveContextSwitches) score += 0.05;
            
            // Ajusta com características temporais
            if (systemData.FrameTimeHistory.RecentFrameTimesMs.Length > 5)
            {
                double avgRecent = 0;
                for (int i = 0; i < Math.Min(5, systemData.FrameTimeHistory.RecentFrameTimesMs.Length); i++)
                {
                    avgRecent += systemData.FrameTimeHistory.RecentFrameTimesMs[i];
                }
                avgRecent /= Math.Min(5, systemData.FrameTimeHistory.RecentFrameTimesMs.Length);
                
                if (avgRecent > systemData.FrameTimeHistory.AverageFrameTimeMs * 1.5)
                {
                    score += 0.1;
                }
            }
            
            return Math.Min(1.0, score);
        }

        /// <summary>
        /// Estima o tempo até a stutter usando técnicas ML.
        /// </summary>
        /// <param name="factors">Fatores identificados.</param>
        /// <param name="systemData">Dados do sistema.</param>
        /// <returns>Tempo estimado em milissegundos.</returns>
        private int EstimateMLTimeToStutter(StutterFactors factors, SystemDataSnapshot systemData)
        {
            // Simulação de estimativa ML
            int estimate = 1000; // Base de 1 segundo
            
            if (factors.HighCpuUsage) estimate -= 200;
            if (factors.HighGpuUsage) estimate -= 150;
            if (factors.MemorySpikes) estimate -= 250;
            if (factors.HighIoActivity) estimate -= 180;
            if (factors.BlockingSyscalls) estimate -= 120;
            
            return Math.Max(50, estimate);
        }
    }

    /// <summary>
    /// Resultado da análise de padrões.
    /// </summary>
    public class PatternAnalysisResult
    {
        /// <summary>
        /// Probabilidade de stutter (0.0 a 1.0).
        /// </summary>
        public double StutterProbability { get; set; }

        /// <summary>
        /// Tempo estimado até a stutter em milissegundos.
        /// </summary>
        public int EstimatedTimeToStutterMs { get; set; }

        /// <summary>
        /// Fatores identificados.
        /// </summary>
        public StutterFactors Factors { get; set; }
    }

    /// <summary>
    /// Resultado da predição ML.
    /// </summary>
    public class MLPredictionResult
    {
        /// <summary>
        /// Probabilidade de stutter (0.0 a 1.0).
        /// </summary>
        public double StutterProbability { get; set; }

        /// <summary>
        /// Tempo estimado até a stutter em milissegundos.
        /// </summary>
        public int EstimatedTimeToStutterMs { get; set; }

        /// <summary>
        /// Fatores identificados.
        /// </summary>
        public StutterFactors Factors { get; set; }
    }
}
