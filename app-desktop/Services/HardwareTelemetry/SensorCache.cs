using System.Threading;

namespace VoltrisOptimizer.Services.HardwareTelemetry
{
    /// <summary>
    /// Cache thread-safe de altíssimo desempenho para o último snapshot de hardware.
    /// Operações O(1) usando Interlocked.Exchange ou ReaderWriterLockSlim.
    /// </summary>
    public class SensorCache
    {
        private HardwareSnapshot _currentSnapshot = HardwareSnapshot.Empty;

        /// <summary>
        /// Atualiza atomicamente o snapshot atual.
        /// </summary>
        public void Update(HardwareSnapshot newSnapshot)
        {
            Interlocked.Exchange(ref _currentSnapshot, newSnapshot);
        }

        /// <summary>
        /// Obtém o snapshot mais recente para uso imediato pela UI (livre de lockings).
        /// </summary>
        public HardwareSnapshot GetCurrent()
        {
            // Lê atômicamente a referência atual.
            return Volatile.Read(ref _currentSnapshot);
        }
    }
}
