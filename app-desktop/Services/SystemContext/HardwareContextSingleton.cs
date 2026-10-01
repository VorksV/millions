using System;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.Gamer.Interfaces;
using VoltrisOptimizer.Services.Gamer.Models;

namespace VoltrisOptimizer.Services.SystemContext
{
    public interface IHardwareContextSingleton
    {
        Task<HardwareCapabilities> GetHardwareAsync(CancellationToken ct = default);
        HardwareCapabilities? GetCachedHardware();
    }

    public class HardwareContextSingleton : IHardwareContextSingleton
    {
        private readonly IHardwareDetector _detector;
        private HardwareCapabilities? _cachedHardware;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        public HardwareContextSingleton(IHardwareDetector detector)
        {
            _detector = detector ?? throw new ArgumentNullException(nameof(detector));
        }

        public async Task<HardwareCapabilities> GetHardwareAsync(CancellationToken ct = default)
        {
            if (_cachedHardware != null)
                return _cachedHardware;

            await _semaphore.WaitAsync(ct);
            try
            {
                if (_cachedHardware == null)
                {
                    // Detecta via WMI ou API de baixo nível UMA ÚNICA VEZ
                    _cachedHardware = await _detector.GetCapabilitiesAsync(ct);
                }
                return _cachedHardware;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public HardwareCapabilities? GetCachedHardware()
        {
            return _cachedHardware;
        }
    }
}
