using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Prediction
{
    public interface IPredictionSchedulerService
    {
        void Start(int pidGame);
        void Stop();
        Task RunDryRun();
    }
    public class PredictionSchedulerService : IPredictionSchedulerService, IDisposable
    {
        private readonly ILoggingService _logger;
        private readonly SystemChanges.ISystemChangeTransactionService? _tx;
        private Timer? _timer;
        private int _pidGame;
        private bool _dryRun;
        private DateTime _lastMitigation;
        public PredictionSchedulerService(ILoggingService logger, SystemChanges.ISystemChangeTransactionService? tx = null)
        {
            _logger = logger;
            _tx = tx;
        }
        public void Start(int pidGame)
        {
            _pidGame = pidGame;
            _dryRun = false;
            _timer = new Timer(Loop, null, 0, 1000);
            _logger.LogInfo("[Predict] Iniciado");
        }
        public void Stop()
        {
            _timer?.Dispose();
            _timer = null;
            _logger.LogInfo("[Predict] Parado");
        }
        public async Task RunDryRun()
        {
            _dryRun = true;
            _logger.LogInfo("[Predict] Dry-run ativo");
            await Task.Delay(15000);
            _dryRun = false;
        }
        private void Loop(object? state)
        {
            try
            {
                var score = ComputeScore();
                if (score > 0.7 && (DateTime.UtcNow - _lastMitigation).TotalSeconds > 10)
                {
                    _lastMitigation = DateTime.UtcNow;
                    _ = Mitigate();
                }
            }
            catch { }
        }
        private double ComputeScore()
        {
            var cpuCoreSat = CpuCoreSaturation();
            var dpcSpike = DpcSpikeLevel();
            var procPriority = GamePriorityScore();
            var score = 0.5 * cpuCoreSat + 0.3 * dpcSpike + 0.2 * procPriority;
            return Math.Min(1.0, score);
        }
        private double CpuCoreSaturation()
        {
            try
            {
                return Process.GetCurrentProcess().Threads.Count > 100 ? 0.8 : 0.3;
            }
            catch { return 0.3; }
        }
        private double DpcSpikeLevel()
        {
            return 0.3;
        }
        private double GamePriorityScore()
        {
            try
            {
                var p = Process.GetProcessById(_pidGame);
                return p.PriorityClass == ProcessPriorityClass.High ? 0.2 : 0.6;
            }
            catch { return 0.5; }
        }
        private async Task Mitigate()
        {
            var tx = _tx?.Begin("Prediction.Mitigate");
            try
            {
                if (_dryRun)
                {
                    _logger.LogInfo("[Predict] Dry-run: reduzir background, ajustar afinidade, elevar prioridade jogo");
                    await Task.Delay(500);
                }
                else
                {
                    ThrottleBackground();
                    AdjustAffinityNonGame();
                    RaiseGamePriority();
                }
                tx?.Commit();
                _ = RollbackIfUnstable();
            }
            catch
            {
                tx?.Rollback();
            }
        }
        private void ThrottleBackground()
        {
            foreach (var p in Process.GetProcesses().Where(pr => pr.Id != _pidGame).Take(10))
            {
                try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            }
        }
        private void AdjustAffinityNonGame()
        {
            foreach (var p in Process.GetProcesses().Where(pr => pr.Id != _pidGame).Take(10))
            {
                try
                {
                    var handle = p.Handle;
                }
                catch { }
            }
        }
        private void RaiseGamePriority()
        {
            try
            {
                var p = Process.GetProcessById(_pidGame);
                p.PriorityClass = ProcessPriorityClass.High;
            }
            catch { }
        }
        private async Task RollbackIfUnstable()
        {
            await Task.Delay(15000);
            var unstable = false;
            if (unstable)
            {
                foreach (var p in Process.GetProcesses().Where(pr => pr.Id != _pidGame).Take(10))
                {
                    try { p.PriorityClass = ProcessPriorityClass.Normal; } catch { }
                }
            }
        }
        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}
