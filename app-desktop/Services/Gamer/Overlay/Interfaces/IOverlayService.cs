using System;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.Gamer.Overlay.Models;

namespace VoltrisOptimizer.Services.Gamer.Overlay.Interfaces
{
    /// <summary>
    /// Interface para o serviço de overlay OSD
    /// </summary>
    public interface IOverlayService : IDisposable
    {
        /// <summary>
        /// Indica se o overlay está ativo
        /// </summary>
        bool IsActive { get; }

        /// <summary>
        /// Configurações atuais do overlay
        /// </summary>
        OverlaySettings Settings { get; }

        /// <summary>
        /// Evento disparado quando o overlay é ativado
        /// </summary>
        event EventHandler? OverlayActivated;

        /// <summary>
        /// Evento disparado quando o overlay é desativado
        /// </summary>
        event EventHandler? OverlayDeactivated;

        /// <summary>
        /// Evento disparado quando as configurações são atualizadas
        /// </summary>
        event EventHandler? SettingsUpdated;

        /// <summary>
        /// Inicia o overlay para um processo de jogo
        /// </summary>
        Task<bool> StartAsync(int gameProcessId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Para o overlay
        /// </summary>
        Task StopAsync();

        /// <summary>
        /// Atualiza as configurações do overlay
        /// </summary>
        void UpdateSettings(OverlaySettings settings);

        /// <summary>
        /// Carrega as configurações salvas
        /// </summary>
        Task LoadSettingsAsync();

        /// <summary>
        /// Salva as configurações atuais
        /// </summary>
        Task SaveSettingsAsync();
    }
}


