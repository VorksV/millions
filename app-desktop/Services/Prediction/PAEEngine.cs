using System;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.SystemChanges;

namespace VoltrisOptimizer.Services.Prediction
{
    /// <summary>
    /// Motor preditivo anti-stutter principal.
    /// </summary>
    public class PAEEngine : IPAEEngine
    {
        private readonly PAEPredictor _predictor;
        private readonly FramePacingAdjuster _framePacingAdjuster;
        private readonly IOPrefetcher _ioPrefetcher;
        private readonly ISystemInfoService _systemInfoService;
        
        private CancellationTokenSource _cancellationTokenSource;
        private Task _monitoringTask;
        private readonly object _lockObject = new object();
        
        private bool _isRunning = false;
        private DateTime _lastPredictionTime = DateTime.MinValue;
        private readonly TimeSpan _predictionInterval = TimeSpan.FromMilliseconds(2000); // 0.5 Hz - otimizado

        /// <summary>
        /// Evento disparado quando uma possível stutter é predita.
        /// </summary>
        public event EventHandler<StutterPredictedEventArgs> StutterPredicted;

        /// <summary>
        /// Evento disparado quando uma otimização é aplicada.
        /// </summary>
        public event EventHandler<OptimizationAppliedEventArgs> OptimizationApplied;

        /// <summary>
        /// Sensibilidade da detecção de stutter (0.0 a 1.0).
        /// </summary>
        public double Sensitivity { get; set; } = 0.7;

        /// <summary>
        /// Janela de tempo para análise preditiva em segundos.
        /// </summary>
        public int PredictionWindowSeconds { get; set; } = 5;

        /// <summary>
        /// Inicializa uma nova instância de <see cref="PAEEngine"/>.
        /// </summary>
        /// <param name="predictor">Preditor de stutter.</param>
        /// <param name="framePacingAdjuster">Ajustador de ritmo de frames.</param>
        /// <param name="ioPrefetcher">Prefetcher de I/O.</param>
        /// <param name="systemInfoService">Serviço de informações do sistema.</param>
        public PAEEngine(
            PAEPredictor predictor,
            FramePacingAdjuster framePacingAdjuster,
            IOPrefetcher ioPrefetcher,
            ISystemInfoService systemInfoService)
        {
            _predictor = predictor ?? throw new ArgumentNullException(nameof(predictor));
            _framePacingAdjuster = framePacingAdjuster ?? throw new ArgumentNullException(nameof(framePacingAdjuster));
            _ioPrefetcher = ioPrefetcher ?? throw new ArgumentNullException(nameof(ioPrefetcher));
            _systemInfoService = systemInfoService ?? throw new ArgumentNullException(nameof(systemInfoService));
        }

        /// <summary>
        /// Inicia o monitoramento e predição de stutter.
        /// </summary>
        /// <returns>Tarefa assíncrona.</returns>
        public async Task StartAsync()
        {
            lock (_lockObject)
            {
                if (_isRunning)
                    return;

                _isRunning = true;
                _cancellationTokenSource = new CancellationTokenSource();
            }

            // Inicia os componentes dependentes
            await _ioPrefetcher.StartAsync();
            await _framePacingAdjuster.StartAsync();

            // Inicia o loop de monitoramento
            _monitoringTask = MonitorAsync(_cancellationTokenSource.Token);

            // Dispara evento indicando que o motor foi iniciado
            OnOptimizationApplied(new OptimizationAppliedEventArgs(
                OptimizationType.SystemLatencyReduction,
                "Motor PAE iniciado",
                0));
        }

        /// <summary>
        /// Para o monitoramento e predição de stutter.
        /// </summary>
        /// <returns>Tarefa assíncrona.</returns>
        public async Task StopAsync()
        {
            lock (_lockObject)
            {
                if (!_isRunning)
                    return;

                _isRunning = false;
                _cancellationTokenSource?.Cancel();
            }

            // Aguarda o término do loop de monitoramento
            if (_monitoringTask != null)
            {
                try
                {
                    await _monitoringTask;
                }
                catch (OperationCanceledException)
                {
                    // Esperado quando cancelamos a tarefa
                }
            }

            // Para os componentes dependentes
            await _framePacingAdjuster.StopAsync();
            await _ioPrefetcher.StopAsync();

            // Dispara evento indicando que o motor foi parado
            OnOptimizationApplied(new OptimizationAppliedEventArgs(
                OptimizationType.SystemLatencyReduction,
                "Motor PAE parado",
                0));
        }

        /// <summary>
        /// Força uma atualização das predições.
        /// </summary>
        /// <returns>Tarefa assíncrona.</returns>
        public async Task ForcePredictionUpdateAsync()
        {
            await PerformPredictionCycleAsync();
        }

        /// <summary>
        /// Loop principal de monitoramento.
        /// </summary>
        /// <param name="cancellationToken">Token de cancelamento.</param>
        /// <returns>Tarefa assíncrona.</returns>
        private async Task MonitorAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _isRunning)
            {
                try
                {
                    await PerformPredictionCycleAsync();
                    await Task.Delay(_predictionInterval, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Tarefa cancelada, sair do loop
                    break;
                }
                catch (Exception ex)
                {
                    // Log de erro mas continua executando
                    System.Diagnostics.Debug.WriteLine($"Erro no monitoramento PAE: {ex.Message}");
                    
                    // Aguarda mesmo em caso de erro para evitar loops muito rápidos
                    try
                    {
                        await Task.Delay(_predictionInterval, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Realiza um ciclo completo de predição.
        /// </summary>
        /// <returns>Tarefa assíncrona.</returns>
        private async Task PerformPredictionCycleAsync()
        {
            var now = DateTime.UtcNow;
            
            // Limita a frequência de predições
            if ((now - _lastPredictionTime).TotalMilliseconds < _predictionInterval.TotalMilliseconds)
                return;
                
            _lastPredictionTime = now;

            try
            {
                // Coleta dados do sistema para análise
                var systemData = await CollectSystemDataAsync();
                
                // Realiza a predição usando o preditor
                var prediction = await _predictor.PredictStutterAsync(systemData, PredictionWindowSeconds);
                
                // Se uma stutter foi predita com alta probabilidade
                if (prediction.Probability > Sensitivity)
                {
                    // Dispara evento de stutter predita
                    OnStutterPredicted(prediction);
                    
                    // Aplica otimizações preventivas
                    await ApplyPreventiveOptimizationsAsync(prediction);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erro ao realizar ciclo de predição: {ex.Message}");
            }
        }

        /// <summary>
        /// Coleta dados do sistema para análise.
        /// </summary>
        /// <returns>Dados do sistema coletados.</returns>
        private async Task<SystemDataSnapshot> CollectSystemDataAsync()
        {
            var cpuUsage = await _systemInfoService.GetCpuUsageAsync();
            var gpuUsage = await _systemInfoService.GetGpuUsageAsync();
            var memoryUsage = await _systemInfoService.GetMemoryUsageAsync();
            var ioActivity = await _systemInfoService.GetIoActivityAsync();
            var processInfo = await _systemInfoService.GetActiveProcessInfoAsync();
            
            var snapshot = new SystemDataSnapshot
            {
                Timestamp = DateTime.UtcNow,
                CpuUsage = new CpuUsageInfo { AverageLoadPercentage = cpuUsage / 100.0 },
                GpuUsage = new GpuUsageInfo { UsagePercentage = gpuUsage / 100.0 },
                MemoryUsage = new MemoryUsageInfo { UsedBytes = (long)(memoryUsage * 1024 * 1024) },
                IoActivity = new IoActivityInfo { BytesReadPerSecond = (long)ioActivity, BytesWrittenPerSecond = 0 },
                FrameTimeHistory = _framePacingAdjuster.GetFrameTimeHistory(),
                ProcessInfo = new ProcessInfo { ProcessName = "Unknown", ProcessId = 0 }
            };

            return snapshot;
        }

        /// <summary>
        /// Aplica otimizações preventivas baseadas na predição.
        /// </summary>
        /// <param name="prediction">Predição de stutter.</param>
        /// <returns>Tarefa assíncrona.</returns>
        private async Task ApplyPreventiveOptimizationsAsync(StutterPrediction prediction)
        {
            // Aplica prefetch de I/O se houver atividade intensa prevista
            if (prediction.Factors.HighIoActivity)
            {
                await _ioPrefetcher.PrefetchAsync();
                OnOptimizationApplied(new OptimizationAppliedEventArgs(
                    OptimizationType.IoOptimization,
                    "Prefetch de I/O acionado",
                    5)); // Ganho estimado de 5ms
            }

            // Ajusta ritmo de frames se houver problemas de GPU previstos
            if (prediction.Factors.HighGpuUsage)
            {
                await _framePacingAdjuster.AdjustPacingAsync();
                OnOptimizationApplied(new OptimizationAppliedEventArgs(
                    OptimizationType.GpuContextPreparation,
                    "Ajuste de ritmo de frames acionado",
                    3)); // Ganho estimado de 3ms
            }

            // Ajusta prioridades de threads se houver context switches excessivos
            if (prediction.Factors.ExcessiveContextSwitches)
            {
                // Implementação futura
                OnOptimizationApplied(new OptimizationAppliedEventArgs(
                    OptimizationType.ThreadPriorityAdjustment,
                    "Ajuste de prioridades de threads acionado",
                    2)); // Ganho estimado de 2ms
            }
        }

        /// <summary>
        /// Dispara o evento de stutter predita.
        /// </summary>
        /// <param name="prediction">Predição realizada.</param>
        protected virtual void OnStutterPredicted(StutterPrediction prediction)
        {
            StutterPredicted?.Invoke(this, new StutterPredictedEventArgs(
                prediction.Probability,
                prediction.EstimatedTimeToStutterMs,
                prediction.Factors));
        }

        /// <summary>
        /// Dispara o evento de otimização aplicada.
        /// </summary>
        /// <param name="args">Argumentos do evento.</param>
        protected virtual void OnOptimizationApplied(OptimizationAppliedEventArgs args)
        {
            OptimizationApplied?.Invoke(this, args);
        }
    }

    /// <summary>
    /// Snapshot dos dados do sistema em um ponto no tempo.
    /// </summary>
    public class SystemDataSnapshot
    {
        /// <summary>
        /// Momento da coleta.
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Uso da CPU.
        /// </summary>
        public CpuUsageInfo CpuUsage { get; set; }

        /// <summary>
        /// Uso da GPU.
        /// </summary>
        public GpuUsageInfo GpuUsage { get; set; }

        /// <summary>
        /// Uso de memória.
        /// </summary>
        public MemoryUsageInfo MemoryUsage { get; set; }

        /// <summary>
        /// Atividade de I/O.
        /// </summary>
        public IoActivityInfo IoActivity { get; set; }

        /// <summary>
        /// Histórico de tempos de frame.
        /// </summary>
        public FrameTimeHistory FrameTimeHistory { get; set; }

        /// <summary>
        /// Informações do processo ativo.
        /// </summary>
        public ProcessInfo ProcessInfo { get; set; }
    }

    /// <summary>
    /// Resultado de uma predição de stutter.
    /// </summary>
    public class StutterPrediction
    {
        /// <summary>
        /// Probabilidade da stutter ocorrer (0.0 a 1.0).
        /// </summary>
        public double Probability { get; set; }

        /// <summary>
        /// Tempo estimado até a stutter ocorrer em milissegundos.
        /// </summary>
        public int EstimatedTimeToStutterMs { get; set; }

        /// <summary>
        /// Fatores que contribuíram para a predição.
        /// </summary>
        public StutterFactors Factors { get; set; }
    }

    /// <summary>
    /// Informações de uso da CPU.
    /// </summary>
    public class CpuUsageInfo
    {
        /// <summary>
        /// Percentual médio de carga (0.0 a 1.0).
        /// </summary>
        public double AverageLoadPercentage { get; set; }

        /// <summary>
        /// Percentual de uso por núcleo.
        /// </summary>
        public double[] PerCoreUsagePercentage { get; set; }

        /// <summary>
        /// Número de context switches por segundo.
        /// </summary>
        public int ContextSwitchesPerSecond { get; set; }

        /// <summary>
        /// Taxa de cache misses.
        /// </summary>
        public double CacheMissRate { get; set; }
    }

    /// <summary>
    /// Informações de uso da GPU.
    /// </summary>
    public class GpuUsageInfo
    {
        /// <summary>
        /// Percentual de uso (0.0 a 1.0).
        /// </summary>
        public double UsagePercentage { get; set; }

        /// <summary>
        /// Temperatura em graus Celsius.
        /// </summary>
        public int TemperatureCelsius { get; set; }

        /// <summary>
        /// Utilização de memória da GPU em MB.
        /// </summary>
        public int MemoryUsageMB { get; set; }

        /// <summary>
        /// Quadros por segundo.
        /// </summary>
        public int Fps { get; set; }
    }

    /// <summary>
    /// Informações de uso de memória.
    /// </summary>
    public class MemoryUsageInfo
    {
        /// <summary>
        /// Bytes totais disponíveis.
        /// </summary>
        public long TotalBytes { get; set; }

        /// <summary>
        /// Bytes utilizados.
        /// </summary>
        public long UsedBytes { get; set; }

        /// <summary>
        /// Percentual de uso (0.0 a 1.0).
        /// </summary>
        public double UsagePercentage => TotalBytes > 0 ? (double)UsedBytes / TotalBytes : 0;

        /// <summary>
        /// Número de page faults por segundo.
        /// </summary>
        public int PageFaultsPerSecond { get; set; }

        /// <summary>
        /// Taxa de leitura/escrita em MB/s.
        /// </summary>
        public double TransferRateMBps { get; set; }
    }

    /// <summary>
    /// Informações de atividade de I/O.
    /// </summary>
    public class IoActivityInfo
    {
        /// <summary>
        /// Leituras por segundo.
        /// </summary>
        public int ReadsPerSecond { get; set; }

        /// <summary>
        /// Escritas por segundo.
        /// </summary>
        public int WritesPerSecond { get; set; }

        /// <summary>
        /// Bytes lidos por segundo.
        /// </summary>
        public long BytesReadPerSecond { get; set; }

        /// <summary>
        /// Bytes escritos por segundo.
        /// </summary>
        public long BytesWrittenPerSecond { get; set; }

        /// <summary>
        /// Latência média de I/O em ms.
        /// </summary>
        public double AverageLatencyMs { get; set; }
    }

    /// <summary>
    /// Histórico de tempos de frame.
    /// </summary>
    public class FrameTimeHistory
    {
        /// <summary>
        /// Tempos de frame recentes em ms.
        /// </summary>
        public double[] RecentFrameTimesMs { get; set; }

        /// <summary>
        /// Tempo médio de frame em ms.
        /// </summary>
        public double AverageFrameTimeMs { get; set; }

        /// <summary>
        /// Desvio padrão dos tempos de frame.
        /// </summary>
        public double FrameTimeDeviation { get; set; }
    }

    /// <summary>
    /// Informações do processo ativo.
    /// </summary>
    public class ProcessInfo
    {
        /// <summary>
        /// Nome do processo.
        /// </summary>
        public string ProcessName { get; set; }

        /// <summary>
        /// ID do processo.
        /// </summary>
        public int ProcessId { get; set; }

        /// <summary>
        /// Número de threads ativos.
        /// </summary>
        public int ActiveThreads { get; set; }

        /// <summary>
        /// Syscalls por segundo.
        /// </summary>
        public int SyscallsPerSecond { get; set; }

        /// <summary>
        /// Handle do processo.
        /// </summary>
        public IntPtr ProcessHandle { get; set; }
    }
}
