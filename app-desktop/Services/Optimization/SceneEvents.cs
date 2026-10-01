using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Optimization
{
    /// <summary>
    /// Serviço para gerenciamento de eventos relacionados às cenas do jogo.
    /// </summary>
    public class SceneEvents
    {
        private readonly List<ISceneEventListener> _listeners = new List<ISceneEventListener>();
        private readonly object _lockObject = new object();

        /// <summary>
        /// Adiciona um ouvinte de eventos de cena.
        /// </summary>
        /// <param name="listener">Ouvinte a ser adicionado.</param>
        public void AddListener(ISceneEventListener listener)
        {
            if (listener == null)
                throw new ArgumentNullException(nameof(listener));

            lock (_lockObject)
            {
                if (!_listeners.Contains(listener))
                {
                    _listeners.Add(listener);
                }
            }
        }

        /// <summary>
        /// Remove um ouvinte de eventos de cena.
        /// </summary>
        /// <param name="listener">Ouvinte a ser removido.</param>
        public void RemoveListener(ISceneEventListener listener)
        {
            lock (_lockObject)
            {
                _listeners.Remove(listener);
            }
        }

        /// <summary>
        /// Notifica todos os ouvintes sobre uma mudança de cena.
        /// </summary>
        /// <param name="previousScene">Cena anterior.</param>
        /// <param name="newScene">Nova cena.</param>
        /// <returns>Tarefa assíncrona.</returns>
        public async Task NotifySceneChangedAsync(SceneProfile previousScene, SceneProfile newScene)
        {
            List<ISceneEventListener> listenersCopy;
            
            lock (_lockObject)
            {
                listenersCopy = new List<ISceneEventListener>(_listeners);
            }

            foreach (var listener in listenersCopy)
            {
                try
                {
                    await listener.OnSceneChangedAsync(previousScene, newScene);
                }
                catch (Exception ex)
                {
                    // Log de erro mas continua notificando os demais ouvintes
                    System.Diagnostics.Debug.WriteLine($"Erro ao notificar ouvinte de evento de cena: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Notifica todos os ouvintes sobre o início de uma nova cena.
        /// </summary>
        /// <param name="scene">Cena iniciada.</param>
        /// <returns>Tarefa assíncrona.</returns>
        public async Task NotifySceneStartedAsync(SceneProfile scene)
        {
            List<ISceneEventListener> listenersCopy;
            
            lock (_lockObject)
            {
                listenersCopy = new List<ISceneEventListener>(_listeners);
            }

            foreach (var listener in listenersCopy)
            {
                try
                {
                    await listener.OnSceneStartedAsync(scene);
                }
                catch (Exception ex)
                {
                    // Log de erro mas continua notificando os demais ouvintes
                    System.Diagnostics.Debug.WriteLine($"Erro ao notificar início de cena: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Notifica todos os ouvintes sobre o fim de uma cena.
        /// </summary>
        /// <param name="scene">Cena finalizada.</param>
        /// <returns>Tarefa assíncrona.</returns>
        public async Task NotifySceneEndedAsync(SceneProfile scene)
        {
            List<ISceneEventListener> listenersCopy;
            
            lock (_lockObject)
            {
                listenersCopy = new List<ISceneEventListener>(_listeners);
            }

            foreach (var listener in listenersCopy)
            {
                try
                {
                    await listener.OnSceneEndedAsync(scene);
                }
                catch (Exception ex)
                {
                    // Log de erro mas continua notificando os demais ouvintes
                    System.Diagnostics.Debug.WriteLine($"Erro ao notificar fim de cena: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Interface para ouvintes de eventos de cena.
    /// </summary>
    public interface ISceneEventListener
    {
        /// <summary>
        /// Chamado quando ocorre uma mudança de cena.
        /// </summary>
        /// <param name="previousScene">Cena anterior.</param>
        /// <param name="newScene">Nova cena.</param>
        /// <returns>Tarefa assíncrona.</returns>
        Task OnSceneChangedAsync(SceneProfile previousScene, SceneProfile newScene);

        /// <summary>
        /// Chamado quando uma nova cena é iniciada.
        /// </summary>
        /// <param name="scene">Cena iniciada.</param>
        /// <returns>Tarefa assíncrona.</returns>
        Task OnSceneStartedAsync(SceneProfile scene);

        /// <summary>
        /// Chamado quando uma cena é finalizada.
        /// </summary>
        /// <param name="scene">Cena finalizada.</param>
        /// <returns>Tarefa assíncrona.</returns>
        Task OnSceneEndedAsync(SceneProfile scene);
    }

    /// <summary>
    /// Implementação base para ouvintes de eventos de cena.
    /// </summary>
    public abstract class SceneEventListenerBase : ISceneEventListener
    {
        /// <summary>
        /// Chamado quando ocorre uma mudança de cena.
        /// </summary>
        /// <param name="previousScene">Cena anterior.</param>
        /// <param name="newScene">Nova cena.</param>
        /// <returns>Tarefa assíncrona.</returns>
        public virtual async Task OnSceneChangedAsync(SceneProfile previousScene, SceneProfile newScene)
        {
            await Task.CompletedTask;
        }

        /// <summary>
        /// Chamado quando uma nova cena é iniciada.
        /// </summary>
        /// <param name="scene">Cena iniciada.</param>
        /// <returns>Tarefa assíncrona.</returns>
        public virtual async Task OnSceneStartedAsync(SceneProfile scene)
        {
            await Task.CompletedTask;
        }

        /// <summary>
        /// Chamado quando uma cena é finalizada.
        /// </summary>
        /// <param name="scene">Cena finalizada.</param>
        /// <returns>Tarefa assíncrona.</returns>
        public virtual async Task OnSceneEndedAsync(SceneProfile scene)
        {
            await Task.CompletedTask;
        }
    }

    /// <summary>
    /// Ouvinte de eventos que registra mudanças de cena em logs.
    /// </summary>
    public class SceneLoggingEventListener : SceneEventListenerBase
    {
        /// <summary>
        /// Chamado quando ocorre uma mudança de cena.
        /// </summary>
        /// <param name="previousScene">Cena anterior.</param>
        /// <param name="newScene">Nova cena.</param>
        /// <returns>Tarefa assíncrona.</returns>
        public override async Task OnSceneChangedAsync(SceneProfile previousScene, SceneProfile newScene)
        {
            System.Diagnostics.Debug.WriteLine($"[SCENE] Mudança de cena: {previousScene?.Name ?? "N/A"} -> {newScene.Name}");
            await base.OnSceneChangedAsync(previousScene, newScene);
        }

        /// <summary>
        /// Chamado quando uma nova cena é iniciada.
        /// </summary>
        /// <param name="scene">Cena iniciada.</param>
        /// <returns>Tarefa assíncrona.</returns>
        public override async Task OnSceneStartedAsync(SceneProfile scene)
        {
            System.Diagnostics.Debug.WriteLine($"[SCENE] Cena iniciada: {scene.Name}");
            await base.OnSceneStartedAsync(scene);
        }

        /// <summary>
        /// Chamado quando uma cena é finalizada.
        /// </summary>
        /// <param name="scene">Cena finalizada.</param>
        /// <returns>Tarefa assíncrona.</returns>
        public override async Task OnSceneEndedAsync(SceneProfile scene)
        {
            System.Diagnostics.Debug.WriteLine($"[SCENE] Cena finalizada: {scene.Name}");
            await base.OnSceneEndedAsync(scene);
        }
    }

    /// <summary>
    /// Ouvinte de eventos que ajusta configurações de áudio com base na cena.
    /// </summary>
    public class AudioSceneEventListener : SceneEventListenerBase
    {
        /// <summary>
        /// Chamado quando ocorre uma mudança de cena.
        /// </summary>
        /// <param name="previousScene">Cena anterior.</param>
        /// <param name="newScene">Nova cena.</param>
        /// <returns>Tarefa assíncrona.</returns>
        public override async Task OnSceneChangedAsync(SceneProfile previousScene, SceneProfile newScene)
        {
            // Ajusta configurações de áudio com base na nova cena
            await AdjustAudioSettingsAsync(newScene);
            await base.OnSceneChangedAsync(previousScene, newScene);
        }

        /// <summary>
        /// Ajusta as configurações de áudio com base no perfil da cena.
        /// </summary>
        /// <param name="scene">Perfil da cena.</param>
        /// <returns>Tarefa assíncrona.</returns>
        private async Task AdjustAudioSettingsAsync(SceneProfile scene)
        {
            // Exemplo de ajuste de áudio:
            // - Em cenas de gameplay intenso, aumentar volume de efeitos
            // - Em cutscenes, equilibrar áudio entre diálogos e música
            // - Em menus, reduzir volume geral
            
            switch (scene.Id)
            {
                case "gameplay_intense":
                    // Aumenta volume de efeitos sonoros
                    break;
                case "cutscene":
                    // Equilibra áudio entre diálogo e música
                    break;
                case "menu":
                    // Reduz volume geral
                    break;
                case "loading":
                    // Silencia áudio ou reproduz música ambiente
                    break;
            }
            
            await Task.CompletedTask;
        }
    }

    /// <summary>
    /// Ouvinte de eventos que ajusta iluminação RGB com base na cena.
    /// </summary>
    public class RgbLightingSceneEventListener : SceneEventListenerBase
    {
        /// <summary>
        /// Chamado quando ocorre uma mudança de cena.
        /// </summary>
        /// <param name="previousScene">Cena anterior.</param>
        /// <param name="newScene">Nova cena.</param>
        /// <returns>Tarefa assíncrona.</returns>
        public override async Task OnSceneChangedAsync(SceneProfile previousScene, SceneProfile newScene)
        {
            // Ajusta iluminação RGB com base na nova cena
            await AdjustRgbLightingAsync(newScene);
            await base.OnSceneChangedAsync(previousScene, newScene);
        }

        /// <summary>
        /// Ajusta a iluminação RGB com base no perfil da cena.
        /// </summary>
        /// <param name="scene">Perfil da cena.</param>
        /// <returns>Tarefa assíncrona.</returns>
        private async Task AdjustRgbLightingAsync(SceneProfile scene)
        {
            // Exemplo de ajuste de iluminação:
            // - Em gameplay intenso, cores vibrantes e efeitos dinâmicos
            // - Em cutscenes, cores suaves e constantes
            // - Em menus, cores neutras
            // - Em telas de carregamento, efeito de "carregando"
            
            switch (scene.Id)
            {
                case "gameplay_intense":
                    // Cores vibrantes e efeitos dinâmicos
                    break;
                case "cutscene":
                    // Cores suaves e constantes
                    break;
                case "menu":
                    // Cores neutras
                    break;
                case "loading":
                    // Efeito de "carregando"
                    break;
            }
            
            await Task.CompletedTask;
        }
    }
}
