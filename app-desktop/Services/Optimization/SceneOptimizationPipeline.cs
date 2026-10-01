using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.SystemChanges;

namespace VoltrisOptimizer.Services.Optimization
{
    /// <summary>
    /// Pipeline de otimizações que aplica configurações específicas de cena ao sistema operacional.
    /// </summary>
    public class SceneOptimizationPipeline
    {
        private readonly ISystemInfoService _systemInfoService;
        private readonly IProcessRunner _processRunner;
        private readonly List<IOptimizationStep> _optimizationSteps;

        /// <summary>
        /// Inicializa uma nova instância de <see cref="SceneOptimizationPipeline"/>.
        /// </summary>
        /// <param name="systemInfoService">Serviço de informações do sistema.</param>
        /// <param name="processRunner">Executor de processos.</param>
        public SceneOptimizationPipeline(ISystemInfoService systemInfoService, IProcessRunner processRunner)
        {
            _systemInfoService = systemInfoService ?? throw new ArgumentNullException(nameof(systemInfoService));
            _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
            _optimizationSteps = new List<IOptimizationStep>();
            
            // Registra os passos de otimização padrão
            RegisterDefaultOptimizationSteps();
        }

        /// <summary>
        /// Registra os passos de otimização padrão.
        /// </summary>
        private void RegisterDefaultOptimizationSteps()
        {
            _optimizationSteps.Add(new CpuOptimizationStep(_systemInfoService, _processRunner));
            _optimizationSteps.Add(new GpuOptimizationStep(_systemInfoService, _processRunner));
            _optimizationSteps.Add(new MemoryOptimizationStep(_systemInfoService, _processRunner));
            _optimizationSteps.Add(new ProcessPriorityOptimizationStep(_systemInfoService, _processRunner));
            _optimizationSteps.Add(new PowerManagementOptimizationStep(_systemInfoService, _processRunner));
        }

        /// <summary>
        /// Adiciona um novo passo de otimização ao pipeline.
        /// </summary>
        /// <param name="step">Passo de otimização a adicionar.</param>
        public void AddOptimizationStep(IOptimizationStep step)
        {
            if (step != null)
            {
                _optimizationSteps.Add(step);
            }
        }

        /// <summary>
        /// Remove um passo de otimização do pipeline.
        /// </summary>
        /// <param name="step">Passo de otimização a remover.</param>
        public void RemoveOptimizationStep(IOptimizationStep step)
        {
            _optimizationSteps.Remove(step);
        }

        /// <summary>
        /// Aplica todas as otimizações para uma determinada cena.
        /// </summary>
        /// <param name="sceneProfile">Perfil da cena para a qual aplicar otimizações.</param>
        /// <returns>Tarefa assíncrona.</returns>
        public async Task ApplyOptimizationsAsync(SceneProfile sceneProfile)
        {
            if (sceneProfile == null)
                throw new ArgumentNullException(nameof(sceneProfile));

            foreach (var step in _optimizationSteps)
            {
                try
                {
                    await step.ApplyAsync(sceneProfile);
                }
                catch (Exception ex)
                {
                    // Log de erro mas continua com os próximos passos
                    System.Diagnostics.Debug.WriteLine($"Erro ao aplicar otimização '{step.Name}': {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Reverte todas as otimizações aplicadas.
        /// </summary>
        /// <returns>Tarefa assíncrona.</returns>
        public async Task RevertOptimizationsAsync()
        {
            // Aplica os passos na ordem reversa para reverter corretamente
            for (int i = _optimizationSteps.Count - 1; i >= 0; i--)
            {
                try
                {
                    await _optimizationSteps[i].RevertAsync();
                }
                catch (Exception ex)
                {
                    // Log de erro mas continua com os próximos passos
                    System.Diagnostics.Debug.WriteLine($"Erro ao reverter otimização '{_optimizationSteps[i].Name}': {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Interface para um passo individual no pipeline de otimização.
    /// </summary>
    public interface IOptimizationStep
    {
        /// <summary>
        /// Nome do passo de otimização.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Aplica a otimização para o perfil de cena especificado.
        /// </summary>
        /// <param name="sceneProfile">Perfil da cena.</param>
        /// <returns>Tarefa assíncrona.</returns>
        Task ApplyAsync(SceneProfile sceneProfile);

        /// <summary>
        /// Reverte a otimização aplicada.
        /// </summary>
        /// <returns>Tarefa assíncrona.</returns>
        Task RevertAsync();
    }

    /// <summary>
    /// Passo de otimização de CPU.
    /// </summary>
    public class CpuOptimizationStep : IOptimizationStep
    {
        private readonly ISystemInfoService _systemInfoService;
        private readonly IProcessRunner _processRunner;
        private CpuSettings _appliedSettings;

        /// <summary>
        /// Nome do passo de otimização.
        /// </summary>
        public string Name => "Otimização de CPU";

        /// <summary>
        /// Inicializa uma nova instância de <see cref="CpuOptimizationStep"/>.
        /// </summary>
        /// <param name="systemInfoService">Serviço de informações do sistema.</param>
        /// <param name="processRunner">Executor de processos.</param>
        public CpuOptimizationStep(ISystemInfoService systemInfoService, IProcessRunner processRunner)
        {
            _systemInfoService = systemInfoService;
            _processRunner = processRunner;
        }

        /// <summary>
        /// Aplica as otimizações de CPU para o perfil de cena especificado.
        /// </summary>
        /// <param name="sceneProfile">Perfil da cena.</param>
        /// <returns>Tarefa assíncrona.</returns>
        public async Task ApplyAsync(SceneProfile sceneProfile)
        {
            if (sceneProfile?.CpuSettings == null)
                return;

            _appliedSettings = sceneProfile.CpuSettings;

            // Aqui iriam os comandos reais para ajustar as configurações de CPU
            // Por exemplo:
            // - Ajustar frequências mínimas/máximas
            // - Alterar política de energia
            // - Configurar afinidade de núcleos
            
            // Exemplo fictício:
            // await _processRunner.RunProcessAsync("powercfg", $"/setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCFREQMAX {_appliedSettings.MaxFrequencyMHz}");
            
            await Task.CompletedTask;
        }

        /// <summary>
        /// Reverte as otimizações de CPU aplicadas.
        /// </summary>
        /// <returns>Tarefa assíncrona.</returns>
        public async Task RevertAsync()
        {
            if (_appliedSettings == null)
                return;

            // Aqui iriam os comandos para restaurar as configurações originais
            // Por exemplo:
            // - Restaurar frequências padrão
            // - Voltar à política de energia balanceada
            
            _appliedSettings = null;
            await Task.CompletedTask;
        }
    }

    /// <summary>
    /// Passo de otimização de GPU.
    /// </summary>
    public class GpuOptimizationStep : IOptimizationStep
    {
        private readonly ISystemInfoService _systemInfoService;
        private readonly IProcessRunner _processRunner;
        private GpuSettings _appliedSettings;

        /// <summary>
        /// Nome do passo de otimização.
        /// </summary>
        public string Name => "Otimização de GPU";

        /// <summary>
        /// Inicializa uma nova instância de <see cref="GpuOptimizationStep"/>.
        /// </summary>
        /// <param name="systemInfoService">Serviço de informações do sistema.</param>
        /// <param name="processRunner">Executor de processos.</param>
        public GpuOptimizationStep(ISystemInfoService systemInfoService, IProcessRunner processRunner)
        {
            _systemInfoService = systemInfoService;
            _processRunner = processRunner;
        }

        /// <summary>
        /// Aplica as otimizações de GPU para o perfil de cena especificado.
        /// </summary>
        /// <param name="sceneProfile">Perfil da cena.</param>
        /// <returns>Tarefa assíncrona.</returns>
        public async Task ApplyAsync(SceneProfile sceneProfile)
        {
            if (sceneProfile?.GpuSettings == null)
                return;

            _appliedSettings = sceneProfile.GpuSettings;

            // Aqui iriam os comandos reais para ajustar as configurações de GPU
            // Por exemplo:
            // - Ajustar frequências mínimas/máximas
            // - Alterar modos de sincronização
            // - Configurar resoluções preferenciais
            
            await Task.CompletedTask;
        }

        /// <summary>
        /// Reverte as otimizações de GPU aplicadas.
        /// </summary>
        /// <returns>Tarefa assíncrona.</returns>
        public async Task RevertAsync()
        {
            if (_appliedSettings == null)
                return;

            // Aqui iriam os comandos para restaurar as configurações originais
            
            _appliedSettings = null;
            await Task.CompletedTask;
        }
    }

    /// <summary>
    /// Passo de otimização de memória.
    /// </summary>
    public class MemoryOptimizationStep : IOptimizationStep
    {
        private readonly ISystemInfoService _systemInfoService;
        private readonly IProcessRunner _processRunner;
        private MemorySettings _appliedSettings;

        /// <summary>
        /// Nome do passo de otimização.
        /// </summary>
        public string Name => "Otimização de Memória";

        /// <summary>
        /// Inicializa uma nova instância de <see cref="MemoryOptimizationStep"/>.
        /// </summary>
        /// <param name="systemInfoService">Serviço de informações do sistema.</param>
        /// <param name="processRunner">Executor de processos.</param>
        public MemoryOptimizationStep(ISystemInfoService systemInfoService, IProcessRunner processRunner)
        {
            _systemInfoService = systemInfoService;
            _processRunner = processRunner;
        }

        /// <summary>
        /// Aplica as otimizações de memória para o perfil de cena especificado.
        /// </summary>
        /// <param name="sceneProfile">Perfil da cena.</param>
        /// <returns>Tarefa assíncrona.</returns>
        public async Task ApplyAsync(SceneProfile sceneProfile)
        {
            if (sceneProfile?.MemorySettings == null)
                return;

            _appliedSettings = sceneProfile.MemorySettings;

            // Aqui iriam os comandos reais para ajustar as configurações de memória
            // Por exemplo:
            // - Ajustar políticas de paginação
            // - Configurar prioridades de alocação
            // - Otimizar uso de cache
            
            await Task.CompletedTask;
        }

        /// <summary>
        /// Reverte as otimizações de memória aplicadas.
        /// </summary>
        /// <returns>Tarefa assíncrona.</returns>
        public async Task RevertAsync()
        {
            if (_appliedSettings == null)
                return;

            // Aqui iriam os comandos para restaurar as configurações originais
            
            _appliedSettings = null;
            await Task.CompletedTask;
        }
    }

    /// <summary>
    /// Passo de otimização de prioridades de processos.
    /// </summary>
    public class ProcessPriorityOptimizationStep : IOptimizationStep
    {
        private readonly ISystemInfoService _systemInfoService;
        private readonly IProcessRunner _processRunner;

        /// <summary>
        /// Nome do passo de otimização.
        /// </summary>
        public string Name => "Otimização de Prioridades de Processos";

        /// <summary>
        /// Inicializa uma nova instância de <see cref="ProcessPriorityOptimizationStep"/>.
        /// </summary>
        /// <param name="systemInfoService">Serviço de informações do sistema.</param>
        /// <param name="processRunner">Executor de processos.</param>
        public ProcessPriorityOptimizationStep(ISystemInfoService systemInfoService, IProcessRunner processRunner)
        {
            _systemInfoService = systemInfoService;
            _processRunner = processRunner;
        }

        /// <summary>
        /// Aplica as otimizações de prioridade de processos para o perfil de cena especificado.
        /// </summary>
        /// <param name="sceneProfile">Perfil da cena.</param>
        /// <returns>Tarefa assíncrona.</returns>
        public async Task ApplyAsync(SceneProfile sceneProfile)
        {
            // Aqui iriam os comandos reais para ajustar prioridades de processos
            // Por exemplo:
            // - Elevar prioridade de processos de jogo
            // - Reduzir prioridade de processos em segundo plano
            
            await Task.CompletedTask;
        }

        /// <summary>
        /// Reverte as otimizações de prioridade de processos aplicadas.
        /// </summary>
        /// <returns>Tarefa assíncrona.</returns>
        public async Task RevertAsync()
        {
            // Aqui iriam os comandos para restaurar as prioridades originais
            
            await Task.CompletedTask;
        }
    }

    /// <summary>
    /// Passo de otimização de gerenciamento de energia.
    /// </summary>
    public class PowerManagementOptimizationStep : IOptimizationStep
    {
        private readonly ISystemInfoService _systemInfoService;
        private readonly IProcessRunner _processRunner;

        /// <summary>
        /// Nome do passo de otimização.
        /// </summary>
        public string Name => "Otimização de Gerenciamento de Energia";

        /// <summary>
        /// Inicializa uma nova instância de <see cref="PowerManagementOptimizationStep"/>.
        /// </summary>
        /// <param name="systemInfoService">Serviço de informações do sistema.</param>
        /// <param name="processRunner">Executor de processos.</param>
        public PowerManagementOptimizationStep(ISystemInfoService systemInfoService, IProcessRunner processRunner)
        {
            _systemInfoService = systemInfoService;
            _processRunner = processRunner;
        }

        /// <summary>
        /// Aplica as otimizações de gerenciamento de energia para o perfil de cena especificado.
        /// </summary>
        /// <param name="sceneProfile">Perfil da cena.</param>
        /// <returns>Tarefa assíncrona.</returns>
        public async Task ApplyAsync(SceneProfile sceneProfile)
        {
            // Aqui iriam os comandos reais para ajustar políticas de energia
            // Por exemplo:
            // - Definir esquema de energia adequado
            // - Desativar economia de energia em cenas críticas
            
            await Task.CompletedTask;
        }

        /// <summary>
        /// Reverte as otimizações de gerenciamento de energia aplicadas.
        /// </summary>
        /// <returns>Tarefa assíncrona.</returns>
        public async Task RevertAsync()
        {
            // Aqui iriam os comandos para restaurar as políticas de energia originais
            
            await Task.CompletedTask;
        }
    }
}
