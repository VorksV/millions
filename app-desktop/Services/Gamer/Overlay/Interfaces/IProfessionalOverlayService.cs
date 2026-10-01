using System;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.Gamer.Overlay.Models;

namespace VoltrisOptimizer.Services.Gamer.Overlay.Interfaces
{
    /// <summary>
    /// Interface para serviço profissional de overlay
    /// Implementa captura REAL de FPS e hardware via ETW
    /// </summary>
    public interface IProfessionalOverlayService : IOverlayService
    {
        /// <summary>
        /// Inicia overlay profissional com ETW
        /// </summary>
        Task<bool> StartProfessionalAsync(int gameProcessId, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Verifica se o sistema tem suporte para ETW (requer admin)
        /// </summary>
        bool IsEtwSupported();
        
        /// <summary>
        /// Obtém status da captura profissional
        /// </summary>
        ProfessionalCaptureStatus GetCaptureStatus();
    }
    
    /// <summary>
    /// Status da captura profissional
    /// </summary>
    public class ProfessionalCaptureStatus
    {
        public bool IsEtwActive { get; set; }
        public bool IsHardwareMonitorActive { get; set; }
        public bool IsFpsCaptureActive { get; set; }
        public DateTime StartTime { get; set; }
        public long TotalFramesCaptured { get; set; }
        public string ActiveDirectXVersion { get; set; } = "";
        
        public override string ToString()
        {
            return $"ETW: {IsEtwActive}, HW: {IsHardwareMonitorActive}, FPS: {IsFpsCaptureActive}, Frames: {TotalFramesCaptured}, DirectX: {ActiveDirectXVersion}";
        }
    }
}
