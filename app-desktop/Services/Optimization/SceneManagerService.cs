using System;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.SystemChanges;

namespace VoltrisOptimizer.Services.Optimization
{
    /// <summary>
    /// Serviço principal para gerenciamento de cenas de jogo e aplicação de otimizações associadas.
    /// </summary>
    public class SceneManagerService : ISceneManagerService
    {
        private readonly SceneDetector _sceneDetector;
        private readonly ISystemInfoService _systemInfoService;
        private readonly IProcessRunner _processRunner;
        
        private SceneProfile _currentScene;
        private CancellationTokenSource _monitoringCancellationTokenSource;
        private Task _monitoringTask;
        private readonly object _lockObject = new object();

        /// <summary>
        /// Intervalo entre verificações de mudança de cena em milissegundos.
        /// </summary>
        public int SceneCheckIntervalMs { get; set; } = 3000;

        /// <summary>
        /// Evento disparado quando ocorre uma mudança de cena.
        /// </summary>
        public event EventHandler<SceneChangedEventArgs> SceneChanged;

        /// <summary>
        /// Inicializa uma nova instância de <see cref="SceneManagerService"/>.
        /// </summary>
        /// <param name="sceneDetector">Detector de cenas.</param>
        /// <param name="systemInfoService">Serviço de informações do sistema.</param>
        /// <param name="processRunner">Executor de processos.</param>
        public SceneManagerService(
            SceneDetector sceneDetector,
            ISystemInfoService systemInfoService,
            IProcessRunner processRunner)
        {
            _sceneDetector = sceneDetector ?? throw new ArgumentNullException(nameof(sceneDetector));
            _systemInfoService = systemInfoService ?? throw new ArgumentNullException(nameof(systemInfoService));
            _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
            
            // Define a cena inicial como menu por padrão
            _currentScene = CreateDefaultMenuScene();
        }

        /// <summary>
        /// Inicia o monitoramento contínuo de mudanças de cena.
        /// </summary>
        /// <returns>Tarefa assíncrona.</returns>
        public async Task StartMonitoringAsync()
        {
            lock (_lockObject)
            {
                if (_monitoringTask != null && !_monitoringTask.IsCompleted)
                {
                    // Já está em execução
                    return;
                }

                _monitoringCancellationTokenSource = new CancellationTokenSource();
                _monitoringTask = MonitorScenesAsync(_monitoringCancellationTokenSource.Token);
            }

            // Aguarda a primeira detecção de cena
            await ForceSceneUpdateAsync();
        }

        /// <summary>
        /// Para o monitoramento de cenas.
        /// </summary>
        /// <returns>Tarefa assíncrona.</returns>
        public async Task StopMonitoringAsync()
        {
            lock (_lockObject)
            {
                _monitoringCancellationTokenSource?.Cancel();
            }

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
        }

        /// <summary>
        /// Obtém a cena atualmente detectada.
        /// </summary>
        /// <returns>Cena atual.</returns>
        public SceneProfile GetCurrentScene()
        {
            lock (_lockObject)
            {
                return _currentScene;
            }
        }

        /// <summary>
        /// Força uma atualização manual da cena atual.
        /// </summary>
        /// <returns>Tarefa assíncrona.</returns>
        public async Task ForceSceneUpdateAsync()
        {
            var newScene = await _sceneDetector.DetectCurrentSceneAsync();
            await UpdateCurrentSceneAsync(newScene);
        }

        /// <summary>
        /// Loop principal de monitoramento de cenas.
        /// </summary>
        /// <param name="cancellationToken">Token de cancelamento.</param>
        /// <returns>Tarefa assíncrona.</returns>
        private async Task MonitorScenesAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var newScene = await _sceneDetector.DetectCurrentSceneAsync();
                    await UpdateCurrentSceneAsync(newScene);

                    // Aguarda o intervalo definido antes da próxima verificação
                    await Task.Delay(SceneCheckIntervalMs, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Tarefa cancelada, sair do loop
                    break;
                }
                catch (Exception ex)
                {
                    // Log de erro mas continua executando
                    System.Diagnostics.Debug.WriteLine($"Erro no monitoramento de cena: {ex.Message}");
                    
                    // Aguarda mesmo em caso de erro para evitar loops muito rápidos
                    try
                    {
                        await Task.Delay(SceneCheckIntervalMs, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Atualiza a cena atual e dispara evento se houve mudança.
        /// </summary>
        /// <param name="newScene">Nova cena detectada.</param>
        /// <returns>Tarefa assíncrona.</returns>
        private async Task UpdateCurrentSceneAsync(SceneProfile newScene)
        {
            SceneProfile previousScene = null;
            
            lock (_lockObject)
            {
                // Verifica se houve mudança real de cena
                if (_currentScene == null || _currentScene.Id != newScene.Id)
                {
                    previousScene = _currentScene;
                    _currentScene = newScene;
                }
            }

            // Se houve mudança, dispara o evento
            if (previousScene != null)
            {
                OnSceneChanged(previousScene, newScene);
                
                // Aplica otimizações para a nova cena
                await ApplySceneOptimizationsAsync(newScene);
            }
        }

        /// <summary>
        /// Dispara o evento de mudança de cena.
        /// </summary>
        /// <param name="previousScene">Cena anterior.</param>
        /// <param name="newScene">Nova cena.</param>
        protected virtual void OnSceneChanged(SceneProfile previousScene, SceneProfile newScene)
        {
            SceneChanged?.Invoke(this, new SceneChangedEventArgs(previousScene, newScene));
        }

        /// <summary>
        /// Aplica otimizações específicas para a cena atual.
        /// </summary>
        /// <param name="scene">Cena para a qual aplicar otimizações.</param>
        /// <returns>Tarefa assíncrona.</returns>
        private async Task ApplySceneOptimizationsAsync(SceneProfile scene)
        {
            try
            {
                // Aplica otimizações de CPU
                await ApplyCpuOptimizationsAsync(scene.CpuSettings);
                
                // Aplica otimizações de GPU
                await ApplyGpuOptimizationsAsync(scene.GpuSettings);
                
                // Aplica otimizações de memória
                await ApplyMemoryOptimizationsAsync(scene.MemorySettings);
                
                // Aplica otimizações personalizadas
                await ApplyCustomOptimizationsAsync(scene.CustomSettings);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erro ao aplicar otimizações para cena '{scene.Name}': {ex.Message}");
            }
        }

        /// <summary>
        /// Aplica otimizações específicas de CPU para a cena.
        /// </summary>
        /// <param name="settings">Configurações de CPU.</param>
        /// <returns>Tarefa assíncrona.</returns>
        private async Task ApplyCpuOptimizationsAsync(CpuSettings settings)
        {
            // Este método seria implementado com chamadas reais ao sistema
            // para ajustar frequências, políticas de energia, etc.
            
            // Exemplo de comando fictício:
            // await _processRunner.RunProcessAsync("powercfg", $"/setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCFREQMAX {settings.MaxFrequencyMHz}");
            
            await Task.CompletedTask;
        }

        /// <summary>
        /// Aplica otimizações específicas de GPU para a cena.
        /// </summary>
        /// <param name="settings">Configurações de GPU.</param>
        /// <returns>Tarefa assíncrona.</returns>
        private async Task ApplyGpuOptimizationsAsync(GpuSettings settings)
        {
            // Este método seria implementado com chamadas reais ao driver da GPU
            // para ajustar frequências, modos de sincronização, etc.
            
            await Task.CompletedTask;
        }

        /// <summary>
        /// Aplica otimizações específicas de memória para a cena.
        /// </summary>
        /// <param name="settings">Configurações de memória.</param>
        /// <returns>Tarefa assíncrona.</returns>
        private async Task ApplyMemoryOptimizationsAsync(MemorySettings settings)
        {
            // Este método seria implementado com chamadas ao gerenciador de memória
            // para ajustar políticas de paginação, alocação, etc.
            
            await Task.CompletedTask;
        }

        /// <summary>
        /// Aplica otimizações personalizadas definidas na cena.
        /// </summary>
        /// <param name="customSettings">Dicionário de configurações personalizadas.</param>
        /// <returns>Tarefa assíncrona.</returns>
        private async Task ApplyCustomOptimizationsAsync(System.Collections.Generic.Dictionary<string, object> customSettings)
        {
            // Este método poderia aplicar otimizações específicas definidas pelo usuário
            // ou por perfis de jogo específicos
            
            await Task.CompletedTask;
        }

        /// <summary>
        /// Cria uma cena padrão de menu.
        /// </summary>
        /// <returns>Perfil de cena de menu padrão.</returns>
        private SceneProfile CreateDefaultMenuScene()
        {
            return new SceneProfile
            {
                Id = "menu_default",
                Name = "Menu Padrão",
                Description = "Cena de menu padrão antes da detecção precisa",
                Priority = 50,
                IsPerformanceCritical = false,
                CpuSettings = new CpuSettings
                {
                    MinFrequencyMHz = 800,
                    MaxFrequencyMHz = 2500,
                    MinCoreUsagePercent = 20,
                    MaxCoreUsagePercent = 50,
                    PowerPolicy = "balanced"
                },
                GpuSettings = new GpuSettings
                {
                    MinFrequencyMHz = 300,
                    MaxFrequencyMHz = 1000,
                    MaxTemperatureCelsius = 70,
                    FrameTimingMode = "adaptive",
                    PreferredResolution = "native"
                },
                MemorySettings = new MemorySettings
                {
                    MinRamMB = 1024,
                    MaxRamMB = 2048,
                    MinCacheUsagePercent = 20,
                    MaxCacheUsagePercent = 60,
                    PagingPolicy = "normal"
                }
            };
        }
    }
}
