using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using VoltrisOptimizer.Services.Gamer.Implementation;

namespace VoltrisOptimizer.Services.Gamer.Overlay
{
    /// <summary>
    /// 🚀 Overlay de estatísticas do Voltris RAM Cache
    /// </summary>
    public class RAMCacheOverlayService : IDisposable
    {
        private readonly DispatcherTimer _updateTimer;
        private VoltrisRAMCacheService? _cacheService;
        private bool _isVisible;

        public RAMCacheOverlayService()
        {
            _updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _updateTimer.Tick += UpdateOverlay;
        }

        public void Start(VoltrisRAMCacheService cacheService)
        {
            _cacheService = cacheService;
            _updateTimer.Start();
            _isVisible = true;
        }

        public void Stop()
        {
            _updateTimer.Stop();
            _isVisible = false;
        }

        private void UpdateOverlay(object? sender, EventArgs e)
        {
            if (_cacheService == null || !_isVisible) return;

            var stats = _cacheService.GetStatistics();
            
            // Mostrar no console/log (pode ser expandido para overlay visual)
            Console.WriteLine($"[RAM Cache Overlay] Entries: {stats.TotalEntries} | Hit Rate: {stats.HitRate:F1}% | Hits: {stats.TotalHits:N0} | Misses: {stats.TotalMisses:N0}");
        }

        public void Dispose()
        {
            Stop();
            _updateTimer.Tick -= UpdateOverlay;
        }
    }
}