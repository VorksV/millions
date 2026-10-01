using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using Microsoft.ML;
using Microsoft.ML.Calibrators;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers.FastTree;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Core.Brain.V2;

namespace VoltrisOptimizer.Core.Intelligence;

public class PatternRecognitionService : IPatternRecognitionService, IAutoStartService
{
	private readonly MLContext _mlContext;

	private ITransformer _trainedModel;

	private PredictionEngine<SystemSpikeFeatures, SpikePrediction> _predictionEngine;

	private readonly string _modelPath;

	private readonly string _syntheticModelPath;

	private readonly string _trainingDataPath;

	private readonly ILoggingService _logger;

	private readonly ILearningLogger _learningDb;

	private readonly List<SystemSpikeFeatures> _sessionDataBuffer = new List<SystemSpikeFeatures>();

	private static readonly SemaphoreSlim _fileLock = new SemaphoreSlim(1, 1);

	private const int MaxBufferSize = 1000;

	private CancellationTokenSource? _monitoringCts;

	private Task? _monitoringTask;

	private SystemSpikeFeatures? _previousFeatures;

	private float _previousPredictionRisk;

	public async Task StartAsync(CancellationToken ct = default(CancellationToken))
	{
		_logger.LogInfo("[PATTERN] Iniciando monitoramento de risco de spike (intervalo = 5s)");
		await LoadModelAsync();
		_monitoringCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		_monitoringTask = Task.Run(() => MonitoringLoopAsync(_monitoringCts!.Token), _monitoringCts!.Token);
	}

	private async Task MonitoringLoopAsync(CancellationToken ct)
	{
		while (!ct.IsCancellationRequested)
		{
			try
			{
				await Task.Delay(5000, ct).ConfigureAwait(continueOnCapturedContext: false);
				SystemMetricsCache cache = SystemMetricsCache.Instance;
				float cpuLoad = (float)cache.CpuPercent;
				float ramPct = (float)cache.MemoryUsedPercent;
				double totalRamGB = cache.Hardware?.TotalRamGb ?? 16.0;
				float currentFps = (float)cache.Fps;
				var currentFeatures = new SystemSpikeFeatures
				{
					CpuLoad = cpuLoad,
					RamUsedGB = ramPct * 0.01f * (float)totalRamGB,
					GpuLoad = (float)cache.GpuUsagePercent,
					CurrentFps = currentFps,
					DiskQueueLength = cache.DiskQueueLength
				};

				if (_previousFeatures != null)
				{
					bool spikeOcorreu = DetectActualSpike(_previousFeatures, currentFeatures);
					if (_previousPredictionRisk > 0.5f || spikeOcorreu)
					{
						await RecordEventAsync(_previousFeatures, spikeOcorreu);
					}
				}

				_previousFeatures = currentFeatures;
				_previousPredictionRisk = PredictSpikeRisk(currentFeatures);
				if (_previousPredictionRisk > 0.7f)
				{
					_logger.LogWarning($"[PATTERN-MONITOR] Risco de spike elevado: {_previousPredictionRisk * 100f:F1}% | CPU={currentFeatures.CpuLoad:F1}% RAM={currentFeatures.RamUsedGB:F1}GB GPU={currentFeatures.GpuLoad:F1}%");
					
					// 🚨 NOTIFICAR BRAIN V2 PARA AÇÃO PREVENTIVA!
					try
					{
						var brain = VoltrisOptimizer.Core.ServiceLocator.GetService<VoltrisBrainV2>();
						if (brain != null)
						{
							_ = Task.Run(async () =>
							{
								var result = await brain.RequestStutterPreventionAsync(_previousPredictionRisk, currentFeatures);
								if (result.Success)
								{
									_logger.LogSuccess($"[PATTERN] ✅ Stutter prevention aplicado: {result.ExecutedActions.Count} ações");
								}
								else
								{
									_logger.LogWarning($"[PATTERN] ⚠️ Stutter prevention falhou: {result.Reason}");
								}
							});
						}
					}
					catch (Exception ex)
					{
						_logger.LogError($"[PATTERN] Erro ao notificar Brain: {ex.Message}");
					}
				}
			}
			catch (OperationCanceledException)
			{
				break;
			}
			catch (Exception ex)
			{
				_logger.LogError("[PATTERN] Erro no monitoring loop: " + ex.Message);
			}
		}
	}

	private static bool DetectActualSpike(SystemSpikeFeatures previous, SystemSpikeFeatures current)
	{
		if (previous.CurrentFps > 30f && current.CurrentFps < previous.CurrentFps * 0.6f)
			return true;
		if (previous.CpuLoad > 20f && current.CpuLoad < previous.CpuLoad * 0.6f)
			return true;
		if (previous.DiskQueueLength > 1f && current.DiskQueueLength > previous.DiskQueueLength * 3f)
			return true;
		if (previous.GpuLoad > 20f && current.GpuLoad < previous.GpuLoad * 0.4f)
			return true;
		return false;
	}

	public PatternRecognitionService(ILoggingService logger, ILearningLogger learningDb)
	{
		_logger = logger;
		_learningDb = learningDb;
		_mlContext = new MLContext(0);
		string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Voltris", "Brain");
		_modelPath = Path.Combine(path, "spike_model.zip");
		_syntheticModelPath = Path.Combine(path, "spike_model_synthetic.zip");
		_trainingDataPath = Path.Combine(path, "training_data.csv");
		
		_fileLock.Wait();
		try
		{
			if (!File.Exists(_trainingDataPath))
			{
				File.WriteAllText(_trainingDataPath, "CpuLoad,RamUsedGB,GpuLoad,CurrentFps,DiskQueueLength,SpikeOcurred\n");
			}
		}
		catch (Exception ex)
		{
			_logger.LogWarning($"[PATTERN] Não foi possível criar o arquivo de treinamento '{_trainingDataPath}': {ex.Message}");
		}
		finally
		{
			_fileLock.Release();
		}
	}

	public async Task LoadModelAsync()
	{
		try
		{
			if (File.Exists(_modelPath))
			{
				_logger.LogInfo("[PATTERN] Carregando modelo FastTree pre-treinado de ML.NET...");
				_trainedModel = _mlContext.Model.Load(_modelPath, out var _);
				_predictionEngine = _mlContext.Model.CreatePredictionEngine<SystemSpikeFeatures, SpikePrediction>(_trainedModel);
				_logger.LogInfo("[PATTERN] Modelo de ML carregado e operante.");
			}
			else if (File.Exists(_syntheticModelPath))
			{
				_logger.LogWarning("[PATTERN] Modelo REAL ausente. Carregando modelo SINTÉTICO de reserva. Previsões NÃO são confiáveis (placeholder)!");
				_trainedModel = _mlContext.Model.Load(_syntheticModelPath, out var _);
				_predictionEngine = _mlContext.Model.CreatePredictionEngine<SystemSpikeFeatures, SpikePrediction>(_trainedModel);
				_logger.LogWarning("[PATTERN] Modelo SINTÉTICO de reserva carregado (spike_model_synthetic.zip). O modelo real só existe após >= 10 eventos válidos na CSV.");
			}
			else
			{
				_logger.LogInfo("[PATTERN] Modelo ML inexistente. Iniciando treinamento primário...");
				await TrainModelAsync();
			}
		}
		catch (Exception ex2)
		{
			Exception ex = ex2;
			_logger.LogError("[PATTERN] Falha crítica ao carregar modelo de ML.", ex);
		}
	}

public async Task TrainModelAsync()
{
    await _fileLock.WaitAsync();
    try
    {
        _logger.LogInfo("[PATTERN] Iniciando Pipeline de Treinamento de Machine Learning...");
        
        // Verificar se arquivo de treinamento existe
        if (!File.Exists(_trainingDataPath))
        {
            _logger.LogInfo("[PATTERN] Arquivo de treinamento não encontrado. Criando arquivo vazio...");
            File.WriteAllText(_trainingDataPath, "CpuLoad,RamUsedGB,GpuLoad,CurrentFps,DiskQueueLength,SpikeOcurred\n");
        }
        
        // Validar e limpar dados corrompidos
        var validLines = new List<string>();
        try
        {
            var allLines = File.ReadAllLines(_trainingDataPath);
            if (allLines.Length > 0)
            {
                validLines.Add(allLines[0]); // Manter header
                
                for (int i = 1; i < allLines.Length; i++)
                {
                    var line = allLines[i].Trim();
                    if (string.IsNullOrEmpty(line)) continue;
                    
                    var parts = line.Split(',');
                    if (parts.Length != 6) continue;
                    
                    // Validar se o Label é true/false válido
                    if (parts[5].Trim().Equals("true", StringComparison.OrdinalIgnoreCase) || 
                        parts[5].Trim().Equals("false", StringComparison.OrdinalIgnoreCase))
                    {
                        validLines.Add(line);
                    }
                    else
                    {
                        _logger.LogWarning($"[PATTERN] Linha {i} ignorada: Label inválido '{parts[5]}'");
                    }
                }
                
                // Reescrever arquivo apenas com dados válidos se houver linhas inválidas
                if (validLines.Count != allLines.Length)
                {
                    File.WriteAllLines(_trainingDataPath, validLines);
                    _logger.LogInfo($"[PATTERN] Arquivo de treinamento limpo: {validLines.Count - 1} dados válidos");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"[PATTERN] Erro ao validar arquivo de treinamento: {ex.Message}");
        }
        
        // Contar linhas de dados (excluindo header)
        int dataLines = validLines.Count > 0 ? validLines.Count - 1 : 0;
        
        // Se não há dados suficientes, criar modelo default sem treinar
        if (dataLines < 10)
        {
            _logger.LogInfo($"[PATTERN] Dados insuficientes para treino ({dataLines} linhas). Mínimo: 10. Criando modelo default...");
            
            // Criar dados sintéticos mínimos para evitar erro "missing features"
            var syntheticData = new List<SystemSpikeFeatures>();
            for (int i = 0; i < 10; i++)
            {
                syntheticData.Add(new SystemSpikeFeatures
                {
                    CpuLoad = 50f,
                    RamUsedGB = 8f,
                    GpuLoad = 50f,
                    CurrentFps = 60f,
                    DiskQueueLength = 0.5f,
                    SpikeOcurred = false
                });
            }
            
            var syntheticDataView = _mlContext.Data.LoadFromEnumerable(syntheticData);
            var syntheticPipeline = _mlContext.Transforms.Concatenate("Features", "CpuLoad", "RamUsedGB", "GpuLoad", "CurrentFps", "DiskQueueLength")
                .Append(_mlContext.BinaryClassification.Trainers.FastTree());
            _trainedModel = syntheticPipeline.Fit(syntheticDataView);
            _predictionEngine = _mlContext.Model.CreatePredictionEngine<SystemSpikeFeatures, SpikePrediction>(_trainedModel);
            _mlContext.Model.Save(_trainedModel, syntheticDataView.Schema, _syntheticModelPath);
            
            _logger.LogInfo("[PATTERN] MODELO SINTÉTICO criado e salvo em spike_model_synthetic.zip (PLACEHOLDER)!");
            _logger.LogWarning("[PATTERN] Previsões com modelo sintético NÃO são confiáveis. Necessários >= 10 eventos reais na CSV para treinar modelo real.");
            return;
        }
        
        // Recriar DataView apenas com dados válidos
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(tempFile, validLines);
            IDataView dataView = _mlContext.Data.LoadFromTextFile<SystemSpikeFeatures>(tempFile, ',', hasHeader: true);
            EstimatorChain<BinaryPredictionTransformer<CalibratedModelParametersBase<FastTreeBinaryModelParameters, PlattCalibrator>>> pipeline = _mlContext.Transforms.Concatenate("Features", "CpuLoad", "RamUsedGB", "GpuLoad", "CurrentFps", "DiskQueueLength").Append(_mlContext.BinaryClassification.Trainers.FastTree());
            _trainedModel = pipeline.Fit(dataView);
            _predictionEngine = _mlContext.Model.CreatePredictionEngine<SystemSpikeFeatures, SpikePrediction>(_trainedModel);
            _mlContext.Model.Save(_trainedModel, dataView.Schema, _modelPath);
            _logger.LogInfo("[PATTERN] Treinamento Concluído. FastTree Model salvo no disco.");
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }
    catch (Exception ex)
    {
        _logger.LogError("[PATTERN] Erro durante o treinamento do ML.", ex);
    }
    finally
    {
        _fileLock.Release();
    }
}

	public float PredictSpikeRisk(SystemSpikeFeatures currentFeatures)
	{
		if (_predictionEngine == null)
		{
			return 0f;
		}
		try
		{
			SpikePrediction spikePrediction = _predictionEngine.Predict(currentFeatures);
			ILoggingService logger = _logger;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(78, 2);
			defaultInterpolatedStringHandler.AppendLiteral("[PATTERN-PREDICTION] Previsão de Stutter -> Probabilidade: ");
			defaultInterpolatedStringHandler.AppendFormatted(spikePrediction.Probability * 100f, "F2");
			defaultInterpolatedStringHandler.AppendLiteral("% | Classificação: ");
			defaultInterpolatedStringHandler.AppendFormatted(spikePrediction.SpikeOcurred ? "ALTO RISCO" : "SEGURO");
			logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
			return spikePrediction.Probability;
		}
		catch
		{
			return 0f;
		}
	}

public async Task RecordEventAsync(SystemSpikeFeatures features, bool wasNegativeSpike)
{
    features.SpikeOcurred = wasNegativeSpike;
    _sessionDataBuffer.Add(features);
    string csvLine = string.Format(CultureInfo.InvariantCulture, "{0:F1},{1:F2},{2:F1},{3:F1},{4:F3},{5}",
        features.CpuLoad, features.RamUsedGB, features.GpuLoad, features.CurrentFps, features.DiskQueueLength,
        wasNegativeSpike ? "true" : "false");
    
    await _fileLock.WaitAsync();
    try
    {
        await File.AppendAllTextAsync(_trainingDataPath, csvLine + "\n");
    }
    finally
    {
        _fileLock.Release();
    }
    
    ILoggingService logger = _logger;
    logger.LogInfo($"[PATTERN] Evento Telemetrico Guardado (Spike: {wasNegativeSpike}). Total Buffer: {_sessionDataBuffer.Count}. CSV: {csvLine}");
    ILearningLogger learningDb = _learningDb;
    string eventType = (wasNegativeSpike ? "NEGATIVE_SPIKE" : "STABLE_SESSION");
    double severity = (wasNegativeSpike ? 0.9 : 0.1);
    await learningDb.LogPatternEventAsync(eventType, severity, $"CPU:{features.CpuLoad:F1} RAM:{features.RamUsedGB:F1} GPU:{features.GpuLoad:F1} FPS:{features.CurrentFps:F0}");
    if (_sessionDataBuffer.Count >= 100)
    {
        _sessionDataBuffer.Clear();
        _logger.LogInfo("[PATTERN] Buffer atingiu gatilho de re-treinamento autônomo. Iniciando...");
        _ = Task.Run(() => TrainModelAsync());
    }
}
}
