using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Optimization.Unification
{
    public class TimerResolutionManager : IDisposable
    {
        private readonly ILoggingService _logger;
        private readonly ITimerResolutionService _timerService;
        private int _referenceCount = 0;
        private readonly object _lock = new object();
        private bool _isHighResActive = false;

        public TimerResolutionManager(ILoggingService logger, ITimerResolutionService timerService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _timerService = timerService ?? throw new ArgumentNullException(nameof(timerService));
        }

        public void RequestHighPrecision(string source)
        {
            lock (_lock)
            {
                _referenceCount++;
                _logger.LogInfo($"[TimerManager] Request +1 de {source}. Contagem atual: {_referenceCount}");

                if (!_isHighResActive)
                {
                    _timerService.SetMaximumResolution();
                    _isHighResActive = _timerService.IsMaxResolutionActive;
                    _logger.LogSuccess($"[TimerManager] Timer de Alta Precisão (via {source}) ATIVADO.");
                }
            }
        }

        public void ReleaseHighPrecision(string source)
        {
            lock (_lock)
            {
                if (_referenceCount > 0)
                {
                    _referenceCount--;
                    _logger.LogInfo($"[TimerManager] Release -1 de {source}. Contagem atual: {_referenceCount}");

                    if (_referenceCount == 0 && _isHighResActive)
                    {
                        _timerService.ReleaseResolution();
                        _isHighResActive = false;
                        _logger.LogInfo($"[TimerManager] Timer restaurado para default via {source}.");
                    }
                }
            }
        }
        
        public void ForceDefault()
        {
             lock (_lock)
            {
                _referenceCount = 0;
                if (_isHighResActive)
                {
                    _timerService.ReleaseResolution();
                    _isHighResActive = false;
                    _logger.LogInfo($"[TimerManager] Timer FORÇADO para default.");
                }
            }
        }

        public void Dispose()
        {
            ForceDefault();
        }
    }
}
