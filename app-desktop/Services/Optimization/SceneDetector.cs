using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.SystemChanges;

namespace VoltrisOptimizer.Services.Optimization
{
    /// <summary>
    /// Detector de cenas que analisa métricas do sistema para determinar a cena atual do jogo.
    /// </summary>
    public class SceneDetector
    {
        private readonly ISystemInfoService _systemInfoService;
        private readonly IProcessRunner _processRunner;
        
        // Métricas coletadas na última atualização
        private double _lastFrameTimeMs = 0;
        private int _lastGpuUsagePercent = 0;
        private int _lastCpuUsagePercent = 0;
        private long _lastMemoryUsageBytes = 0;
        private DateTime _lastUpdateTime = DateTime.MinValue;

        /// <summary>
        /// Intervalo mínimo entre atualizações em milissegundos.
        /// </summary>
        public int UpdateIntervalMs { get; set; } = 500;

        /// <summary>
        /// Inicializa uma nova instância de <see cref="SceneDetector"/>.
        /// </summary>
        /// <param name="systemInfoService">Serviço de informações do sistema.</param>
        /// <param name="processRunner">Executor de processos.</param>
        public SceneDetector(ISystemInfoService systemInfoService, IProcessRunner processRunner)
        {
            _systemInfoService = systemInfoService ?? throw new ArgumentNullException(nameof(systemInfoService));
            _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        }

        /// <summary>
        /// Detecta a cena atual com base nas métricas do sistema.
        /// </summary>
        /// <returns>Perfil da cena detectada.</returns>
        public async Task<SceneProfile> DetectCurrentSceneAsync()
        {
            var now = DateTime.UtcNow;
            
            // Verifica se é hora de atualizar as métricas
            if ((now - _lastUpdateTime).TotalMilliseconds < UpdateIntervalMs)
            {
                // Usa as métricas anteriores para evitar sobrecarga
                return ClassifySceneFromMetrics();
            }

            // Atualiza as métricas do sistema
            await UpdateSystemMetricsAsync();
            _lastUpdateTime = now;

            // Classifica a cena com base nas métricas atualizadas
            return ClassifySceneFromMetrics();
        }

        /// <summary>
        /// Atualiza as métricas do sistema obtendo dados em tempo real.
        /// </summary>
        /// <returns>Tarefa assíncrona.</returns>
        private async Task UpdateSystemMetricsAsync()
        {
            try
            {
                // Coleta métricas de desempenho
                // GetCpuUsageAsync retorna double (porcentagem)
                var cpuInfo = await _systemInfoService.GetCpuUsageAsync();
                // GetGpuUsageAsync retorna double (porcentagem)
                var gpuInfo = await _systemInfoService.GetGpuUsageAsync();
                // GetMemoryUsageAsync retorna double (MB)
                var memoryInfo = await _systemInfoService.GetMemoryUsageAsync();
                
                // Estima frame time baseado na taxa de atualização
                var frameTimeEstimate = EstimateFrameTime();
                
                // Atualiza as variáveis internas
                _lastCpuUsagePercent = (int)cpuInfo;
                _lastGpuUsagePercent = (int)gpuInfo;
                _lastMemoryUsageBytes = (long)(memoryInfo * 1024 * 1024); // Converte MB para bytes
                _lastFrameTimeMs = frameTimeEstimate;
            }
            catch (Exception ex)
            {
                // Em caso de erro, mantém as últimas métricas conhecidas
                Debug.WriteLine($"Erro ao atualizar métricas: {ex.Message}");
            }
        }

        /// <summary>
        /// Estima o tempo de frame com base em processos em execução.
        /// </summary>
        /// <returns>Tempo estimado de frame em ms.</returns>
        private double EstimateFrameTime()
        {
            try
            {
                // Tenta obter informações sobre o processo ativo
                var activeProcesses = Process.GetProcesses()
                    .Where(p => !string.IsNullOrEmpty(p.MainWindowTitle))
                    .OrderByDescending(p => p.Threads.Count)
                    .Take(5);

                // Se há muitos processos gráficos ativos, assume que é gameplay
                var graphicProcesses = activeProcesses.Count(p =>
                    p.ProcessName.Contains("game", StringComparison.OrdinalIgnoreCase) ||
                    p.ProcessName.Contains("steam", StringComparison.OrdinalIgnoreCase) ||
                    p.MainWindowTitle.Contains("game", StringComparison.OrdinalIgnoreCase));

                // Estimativa simples baseada em número de processos gráficos
                if (graphicProcesses > 2)
                    return 16.6; // ~60 FPS
                
                return 33.3; // ~30 FPS como padrão
            }
            catch
            {
                return 33.3; // Valor padrão em caso de falha
            }
        }

        /// <summary>
        /// Classifica a cena atual com base nas métricas coletadas.
        /// </summary>
        /// <returns>Perfil da cena classificada.</returns>
        private SceneProfile ClassifySceneFromMetrics()
        {
            // Determina o tipo de cena com base nas métricas
            if (IsLoadingScene())
                return CreateLoadingSceneProfile();
            
            if (IsCutsceneScene())
                return CreateCutsceneSceneProfile();
            
            if (IsMenuScene())
                return CreateMenuSceneProfile();
            
            // Por padrão, assume Gameplay Intenso
            return CreateGameplaySceneProfile();
        }

        /// <summary>
        /// Verifica se a cena atual parece ser uma tela de carregamento.
        /// </summary>
        /// <returns>True se for uma tela de carregamento.</returns>
        private bool IsLoadingScene()
        {
            // Alta variação de uso de memória + uso moderado de CPU/GPU
            var memoryFluctuation = Math.Abs(_lastMemoryUsageBytes - GetAverageMemoryUsage()) > 50 * 1024 * 1024; // 50MB
            
            return memoryFluctuation && 
                   _lastCpuUsagePercent > 30 && _lastCpuUsagePercent < 70 &&
                   _lastGpuUsagePercent > 20 && _lastGpuUsagePercent < 60;
        }

        /// <summary>
        /// Verifica se a cena atual parece ser uma cutscene.
        /// </summary>
        /// <returns>True se for uma cutscene.</returns>
        private bool IsCutsceneScene()
        {
            // Uso consistente de GPU com frame times estáveis
            return _lastGpuUsagePercent > 60 &&
                   _lastFrameTimeMs > 15 && _lastFrameTimeMs < 20 &&
                   _lastCpuUsagePercent > 20 && _lastCpuUsagePercent < 50;
        }

        /// <summary>
        /// Verifica se a cena atual parece ser um menu.
        /// </summary>
        /// <returns>True se for um menu.</returns>
        private bool IsMenuScene()
        {
            // Baixo uso geral de recursos
            return _lastCpuUsagePercent < 30 &&
                   _lastGpuUsagePercent < 40 &&
                   _lastFrameTimeMs > 30;
        }

        /// <summary>
        /// Obtém média histórica de uso de memória (simulação).
        /// </summary>
        /// <returns>Valor médio de uso de memória.</returns>
        private long GetAverageMemoryUsage()
        {
            // Esta seria uma implementação mais completa com histórico
            // Por enquanto retorna um valor estimado
            return _lastMemoryUsageBytes - 10 * 1024 * 1024; // 10MB menos que o atual
        }

        /// <summary>
        /// Cria um perfil para cena de gameplay intenso.
        /// </summary>
        /// <returns>Perfil de gameplay intenso.</returns>
        private SceneProfile CreateGameplaySceneProfile()
        {
            return new SceneProfile
            {
                Id = "gameplay_intense",
                Name = "Gameplay Intenso",
                Description = "Cena de gameplay principal com alta demanda de recursos",
                Priority = 100,
                IsPerformanceCritical = true,
                CpuSettings = new CpuSettings
                {
                    MinFrequencyMHz = 2000,
                    MaxFrequencyMHz = 4500,
                    MinCoreUsagePercent = 50,
                    MaxCoreUsagePercent = 100,
                    PowerPolicy = "performance"
                },
                GpuSettings = new GpuSettings
                {
                    MinFrequencyMHz = 800,
                    MaxFrequencyMHz = 2000,
                    MaxTemperatureCelsius = 85,
                    FrameTimingMode = "synced",
                    PreferredResolution = "native"
                },
                MemorySettings = new MemorySettings
                {
                    MinRamMB = 2048,
                    MaxRamMB = 8192,
                    MinCacheUsagePercent = 50,
                    MaxCacheUsagePercent = 100,
                    PagingPolicy = "aggressive"
                }
            };
        }

        /// <summary>
        /// Cria um perfil para cena de menu.
        /// </summary>
        /// <returns>Perfil de menu.</returns>
        private SceneProfile CreateMenuSceneProfile()
        {
            return new SceneProfile
            {
                Id = "menu",
                Name = "Menu Principal",
                Description = "Interface do menu principal com baixa demanda de recursos",
                Priority = 50,
                IsPerformanceCritical = false,
                CpuSettings = new CpuSettings
                {
                    MinFrequencyMHz = 800,
                    MaxFrequencyMHz = 3000,
                    MinCoreUsagePercent = 20,
                    MaxCoreUsagePercent = 60,
                    PowerPolicy = "balanced"
                },
                GpuSettings = new GpuSettings
                {
                    MinFrequencyMHz = 300,
                    MaxFrequencyMHz = 1200,
                    MaxTemperatureCelsius = 75,
                    FrameTimingMode = "adaptive",
                    PreferredResolution = "native"
                },
                MemorySettings = new MemorySettings
                {
                    MinRamMB = 1024,
                    MaxRamMB = 4096,
                    MinCacheUsagePercent = 20,
                    MaxCacheUsagePercent = 80,
                    PagingPolicy = "normal"
                }
            };
        }

        /// <summary>
        /// Cria um perfil para cena de cutscene.
        /// </summary>
        /// <returns>Perfil de cutscene.</returns>
        private SceneProfile CreateCutsceneSceneProfile()
        {
            return new SceneProfile
            {
                Id = "cutscene",
                Name = "Cutscene",
                Description = "Cena cinematográfica pré-renderizada com foco em qualidade visual",
                Priority = 80,
                IsPerformanceCritical = true,
                CpuSettings = new CpuSettings
                {
                    MinFrequencyMHz = 1000,
                    MaxFrequencyMHz = 3500,
                    MinCoreUsagePercent = 30,
                    MaxCoreUsagePercent = 80,
                    PowerPolicy = "performance"
                },
                GpuSettings = new GpuSettings
                {
                    MinFrequencyMHz = 600,
                    MaxFrequencyMHz = 1800,
                    MaxTemperatureCelsius = 80,
                    FrameTimingMode = "synced",
                    PreferredResolution = "native"
                },
                MemorySettings = new MemorySettings
                {
                    MinRamMB = 1536,
                    MaxRamMB = 6144,
                    MinCacheUsagePercent = 40,
                    MaxCacheUsagePercent = 90,
                    PagingPolicy = "normal"
                }
            };
        }

        /// <summary>
        /// Cria um perfil para cena de carregamento.
        /// </summary>
        /// <returns>Perfil de carregamento.</returns>
        private SceneProfile CreateLoadingSceneProfile()
        {
            return new SceneProfile
            {
                Id = "loading",
                Name = "Carregamento",
                Description = "Tela de carregamento com atividade intensa de disco e memória",
                Priority = 70,
                IsPerformanceCritical = true,
                CpuSettings = new CpuSettings
                {
                    MinFrequencyMHz = 1500,
                    MaxFrequencyMHz = 4000,
                    MinCoreUsagePercent = 40,
                    MaxCoreUsagePercent = 90,
                    PowerPolicy = "performance"
                },
                GpuSettings = new GpuSettings
                {
                    MinFrequencyMHz = 400,
                    MaxFrequencyMHz = 1500,
                    MaxTemperatureCelsius = 82,
                    FrameTimingMode = "adaptive",
                    PreferredResolution = "native"
                },
                MemorySettings = new MemorySettings
                {
                    MinRamMB = 2048,
                    MaxRamMB = 8192,
                    MinCacheUsagePercent = 60,
                    MaxCacheUsagePercent = 100,
                    PagingPolicy = "aggressive"
                }
            };
        }
    }
}
