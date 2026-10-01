using System;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Optimization
{
    /// <summary>
    /// Interface para gerenciamento de cenas de jogo e otimizações associadas.
    /// </summary>
    public interface ISceneManagerService
    {
        /// <summary>
        /// Inicia o monitoramento contínuo de mudanças de cena.
        /// </summary>
        /// <returns>Tarefa assíncrona.</returns>
        Task StartMonitoringAsync();

        /// <summary>
        /// Para o monitoramento de cenas.
        /// </summary>
        /// <returns>Tarefa assíncrona.</returns>
        Task StopMonitoringAsync();

        /// <summary>
        /// Obtém a cena atualmente detectada.
        /// </summary>
        /// <returns>Cena atual.</returns>
        SceneProfile GetCurrentScene();

        /// <summary>
        /// Força uma atualização manual da cena atual.
        /// </summary>
        /// <returns>Tarefa assíncrona.</returns>
        Task ForceSceneUpdateAsync();

        /// <summary>
        /// Evento disparado quando ocorre uma mudança de cena.
        /// </summary>
        event EventHandler<SceneChangedEventArgs> SceneChanged;
    }

    /// <summary>
    /// Argumentos para eventos de mudança de cena.
    /// </summary>
    public class SceneChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Cena anterior antes da transição.
        /// </summary>
        public SceneProfile PreviousScene { get; }

        /// <summary>
        /// Nova cena após a transição.
        /// </summary>
        public SceneProfile NewScene { get; }

        /// <summary>
        /// Inicializa uma nova instância de <see cref="SceneChangedEventArgs"/>.
        /// </summary>
        /// <param name="previousScene">Cena anterior.</param>
        /// <param name="newScene">Nova cena.</param>
        public SceneChangedEventArgs(SceneProfile previousScene, SceneProfile newScene)
        {
            PreviousScene = previousScene;
            NewScene = newScene;
        }
    }
}
