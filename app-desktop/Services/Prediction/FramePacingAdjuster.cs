using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Prediction
{
    /// <summary>
    /// Ajustador de ritmo de frames para otimização de desempenho e redução de stutter.
    /// </summary>
    public class FramePacingAdjuster
    {
        private readonly ISystemInfoService _systemInfoService;
        private readonly List<double> _frameTimeHistory;
        private readonly object _lockObject = new object();
        
        private CancellationTokenSource _cancellationTokenSource;
        private Task _monitoringTask;
        private bool _isRunning = false;
        
        private const int MaxHistorySize = 100;
        private const double TargetFrameTimeMs = 16.667; // 60 FPS
        private const double FrameTimeThresholdMs = 5.0; // Limiar para considerar stutter

        /// <summary>
        /// Intervalo entre ajustes em milissegundos.
        /// </summary>
        public int AdjustmentIntervalMs { get; set; } = 2000;

        /// <summary>
        /// Inicializa uma nova instância de <see cref="FramePacingAdjuster"/>.
        /// </summary>
        /// <param name="systemInfoService">Serviço de informações do sistema.</param>
        public FramePacingAdjuster(ISystemInfoService systemInfoService)
        {
            _systemInfoService = systemInfoService ?? throw new ArgumentNullException(nameof(systemInfoService));
            _frameTimeHistory = new List<double>();
        }

        /// <summary>
        /// Inicia o monitoramento e ajuste de ritmo de frames.
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

            // Inicia o loop de monitoramento
            _monitoringTask = MonitorFramePacingAsync(_cancellationTokenSource.Token);
            
            await Task.CompletedTask;
        }

        /// <summary>
        /// Para o monitoramento e ajuste de ritmo de frames.
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
            
            await Task.CompletedTask;
        }

        /// <summary>
        /// Ajusta o ritmo de frames com base na análise atual.
        /// </summary>
        /// <returns>Tarefa assíncrona.</returns>
        public async Task AdjustPacingAsync()
        {
            var analysis = AnalyzeFramePacing();
            await ApplyFramePacingAdjustmentsAsync(analysis);
        }

        /// <summary>
        /// Obtém o histórico de tempos de frame.
        /// </summary>
        /// <returns>Histórico de tempos de frame.</returns>
        public FrameTimeHistory GetFrameTimeHistory()
        {
            lock (_lockObject)
            {
                var recentFrames = new double[_frameTimeHistory.Count];
                _frameTimeHistory.CopyTo(recentFrames);
                
                double average = 0;
                double sumSquares = 0;
                
                foreach (var time in recentFrames)
                {
                    average += time;
                    sumSquares += time * time;
                }
                
                if (recentFrames.Length > 0)
                {
                    average /= recentFrames.Length;
                    double variance = (sumSquares / recentFrames.Length) - (average * average);
                    double deviation = Math.Sqrt(variance);
                    
                    return new FrameTimeHistory
                    {
                        RecentFrameTimesMs = recentFrames,
                        AverageFrameTimeMs = average,
                        FrameTimeDeviation = deviation
                    };
                }
                
                return new FrameTimeHistory
                {
                    RecentFrameTimesMs = new double[0],
                    AverageFrameTimeMs = 0,
                    FrameTimeDeviation = 0
                };
            }
        }

        /// <summary>
        /// Loop principal de monitoramento de ritmo de frames.
        /// </summary>
        /// <param name="cancellationToken">Token de cancelamento.</param>
        /// <returns>Tarefa assíncrona.</returns>
        private async Task MonitorFramePacingAsync(CancellationToken cancellationToken)
        {
            var lastFrameTime = DateTime.UtcNow;
            
            while (!cancellationToken.IsCancellationRequested && _isRunning)
            {
                try
                {
                    var now = DateTime.UtcNow;
                    var frameTimeMs = (now - lastFrameTime).TotalMilliseconds;
                    lastFrameTime = now;
                    
                    // Adiciona o tempo de frame ao histórico
                    AddFrameTimeToHistory(frameTimeMs);
                    
                    // Se detectar problemas de pacing, faz ajustes
                    if (ShouldAdjustPacing())
                    {
                        await AdjustPacingAsync();
                    }
                    
                    // Aguarda o intervalo definido
                    await Task.Delay(AdjustmentIntervalMs, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Tarefa cancelada, sair do loop
                    break;
                }
                catch (Exception ex)
                {
                    // Log de erro mas continua executando
                    System.Diagnostics.Debug.WriteLine($"Erro no monitoramento de ritmo de frames: {ex.Message}");
                    
                    // Aguarda mesmo em caso de erro para evitar loops muito rápidos
                    try
                    {
                        await Task.Delay(AdjustmentIntervalMs, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Adiciona um tempo de frame ao histórico.
        /// </summary>
        /// <param name="frameTimeMs">Tempo de frame em milissegundos.</param>
        private void AddFrameTimeToHistory(double frameTimeMs)
        {
            lock (_lockObject)
            {
                _frameTimeHistory.Add(frameTimeMs);
                
                // Mantém o tamanho máximo do histórico
                if (_frameTimeHistory.Count > MaxHistorySize)
                {
                    _frameTimeHistory.RemoveAt(0);
                }
            }
        }

        /// <summary>
        /// Determina se é necessário ajustar o ritmo de frames.
        /// </summary>
        /// <returns>Verdadeiro se for necessário ajustar.</returns>
        private bool ShouldAdjustPacing()
        {
            var history = GetFrameTimeHistory();
            
            // Se não temos dados suficientes, não ajusta
            if (history.RecentFrameTimesMs.Length < 10)
                return false;
                
            // Verifica se há muita variação nos tempos de frame
            if (history.FrameTimeDeviation > FrameTimeThresholdMs)
                return true;
                
            // Verifica se o tempo médio está muito acima do alvo
            if (history.AverageFrameTimeMs > TargetFrameTimeMs + FrameTimeThresholdMs)
                return true;
                
            return false;
        }

        /// <summary>
        /// Analisa o ritmo atual de frames.
        /// </summary>
        /// <returns>Análise do ritmo de frames.</returns>
        private FramePacingAnalysis AnalyzeFramePacing()
        {
            var history = GetFrameTimeHistory();
            
            var analysis = new FramePacingAnalysis
            {
                CurrentAverageFrameTimeMs = history.AverageFrameTimeMs,
                FrameTimeDeviationMs = history.FrameTimeDeviation,
                TargetFrameTimeMs = TargetFrameTimeMs,
                StutterDetected = history.FrameTimeDeviation > FrameTimeThresholdMs,
                FramesAboveThreshold = CountFramesAboveThreshold(history.RecentFrameTimesMs, 
                    TargetFrameTimeMs + FrameTimeThresholdMs)
            };
            
            // Calcula recomendações
            if (analysis.StutterDetected)
            {
                if (analysis.FrameTimeDeviationMs > FrameTimeThresholdMs * 2)
                {
                    analysis.Recommendation = PacingAdjustment.Aggressive;
                }
                else
                {
                    analysis.Recommendation = PacingAdjustment.Moderate;
                }
            }
            else
            {
                analysis.Recommendation = PacingAdjustment.None;
            }
            
            return analysis;
        }

        /// <summary>
        /// Conta quantos frames estão acima de um limiar.
        /// </summary>
        /// <param name="frameTimes">Tempos de frame.</param>
        /// <param name="threshold">Limiar.</param>
        /// <returns>Número de frames acima do limiar.</returns>
        private int CountFramesAboveThreshold(double[] frameTimes, double threshold)
        {
            int count = 0;
            foreach (var time in frameTimes)
            {
                if (time > threshold)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Aplica ajustes de ritmo de frames.
        /// </summary>
        /// <param name="analysis">Análise do ritmo de frames.</param>
        /// <returns>Tarefa assíncrona.</returns>
        private async Task ApplyFramePacingAdjustmentsAsync(FramePacingAnalysis analysis)
        {
            switch (analysis.Recommendation)
            {
                case PacingAdjustment.Aggressive:
                    await ApplyAggressiveAdjustmentsAsync();
                    break;
                case PacingAdjustment.Moderate:
                    await ApplyModerateAdjustmentsAsync();
                    break;
                case PacingAdjustment.Light:
                    await ApplyLightAdjustmentsAsync();
                    break;
            }
        }

        /// <summary>
        /// Aplica ajustes agressivos de ritmo de frames.
        /// </summary>
        /// <returns>Tarefa assíncrona.</returns>
        private async Task ApplyAggressiveAdjustmentsAsync()
        {
            // Ajustes agressivos:
            // 1. Priorizar threads de renderização
            // 2. Reduzir carga em threads não essenciais
            // 3. Ajustar buffer de frames
            
            // Exemplo de comando (seria implementado com chamadas reais ao sistema):
            // await _systemInfoService.SetThreadPriority(gameRenderThread, ThreadPriority.Highest);
            // await _systemInfoService.SetThreadPriority(backgroundThreads, ThreadPriority.BelowNormal);
            
            await Task.CompletedTask;
        }

        /// <summary>
        /// Aplica ajustes moderados de ritmo de frames.
        /// </summary>
        /// <returns>Tarefa assíncrona.</returns>
        private async Task ApplyModerateAdjustmentsAsync()
        {
            // Ajustes moderados:
            // 1. Ajustar timing de apresentação
            // 2. Otimizar uso de buffers
            // 3. Balancear carga entre CPU e GPU
            
            await Task.CompletedTask;
        }

        /// <summary>
        /// Aplica ajustes leves de ritmo de frames.
        /// </summary>
        /// <returns>Tarefa assíncrona.</returns>
        private async Task ApplyLightAdjustmentsAsync()
        {
            // Ajustes leves:
            // 1. Pequenos ajustes no timing
            // 2. Otimizações menores de buffer
            
            await Task.CompletedTask;
        }
    }

    /// <summary>
    /// Análise do ritmo de frames.
    /// </summary>
    public class FramePacingAnalysis
    {
        /// <summary>
        /// Tempo médio atual de frame em ms.
        /// </summary>
        public double CurrentAverageFrameTimeMs { get; set; }

        /// <summary>
        /// Desvio padrão dos tempos de frame em ms.
        /// </summary>
        public double FrameTimeDeviationMs { get; set; }

        /// <summary>
        /// Tempo alvo de frame em ms.
        /// </summary>
        public double TargetFrameTimeMs { get; set; }

        /// <summary>
        /// Indica se stutter foi detectado.
        /// </summary>
        public bool StutterDetected { get; set; }

        /// <summary>
        /// Número de frames acima do limiar.
        /// </summary>
        public int FramesAboveThreshold { get; set; }

        /// <summary>
        /// Recomendação de ajuste.
        /// </summary>
        public PacingAdjustment Recommendation { get; set; }
    }

    /// <summary>
    /// Tipos de ajuste de ritmo de frames.
    /// </summary>
    public enum PacingAdjustment
    {
        /// <summary>
        /// Nenhum ajuste necessário.
        /// </summary>
        None,

        /// <summary>
        /// Ajuste leve.
        /// </summary>
        Light,

        /// <summary>
        /// Ajuste moderado.
        /// </summary>
        Moderate,

        /// <summary>
        /// Ajuste agressivo.
        /// </summary>
        Aggressive
    }
}
