using System;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Intelligence.VPIS.Telemetry
{
    public interface IFrametimeProvider
    {
        bool IsAvailable { get; }
        string ProviderName { get; }
        
        /// <summary>
        /// Inicia a captura em tempo real.
        /// O evento FrametimeCaptured é disparado a cada frame ou lote de frames.
        /// </summary>
        void StartCapture(string targetProcessName);
        
        void StopCapture();

        event EventHandler<FrametimeData> FrametimeCaptured;
    }
}
