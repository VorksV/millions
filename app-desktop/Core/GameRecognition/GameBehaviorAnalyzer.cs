using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Timers;

namespace VoltrisOptimizer.Core.GameRecognition
{
    public class GameBehaviorAnalyzer
    {
        private readonly Process _process;
        private readonly System.Timers.Timer _timer;
        private DateTime _start;
        public event EventHandler<string>? PhaseChanged; // load, gameplay, pause, exit

        public GameBehaviorAnalyzer(Process process)
        {
            _process = process;
            _timer = new System.Timers.Timer(1000);
            _timer.Elapsed += OnTick;
        }

        public void Start()
        {
            _start = DateTime.UtcNow;
            PhaseChanged?.Invoke(this, "load");
            _timer.Start();
        }

        public void Stop()
        {
            _timer.Stop();
            PhaseChanged?.Invoke(this, "exit");
        }

        private void OnTick(object? sender, ElapsedEventArgs e)
        {
            if (_process.HasExited) { Stop(); return; }
            var handle = _process.MainWindowHandle;
            if (handle == IntPtr.Zero) return;
            if (IsIconic(handle))
            {
                PhaseChanged?.Invoke(this, "pause");
                return;
            }
            if ((DateTime.UtcNow - _start).TotalSeconds < 10)
            {
                PhaseChanged?.Invoke(this, "load");
                return;
            }
            PhaseChanged?.Invoke(this, "gameplay");
        }

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);
    }
}

