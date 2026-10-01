using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Helpers
{
    public sealed class StartupDiagnosticLogger : IDisposable
    {
        private readonly string _logPath;
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private readonly object _lock = new();
        private string _lastPhase = "INIT";
        private long _lastTimestamp;
        private StreamWriter? _writer;
        private bool _disposed;

        public StartupDiagnosticLogger()
        {
            var logDir = LogDirectoryResolver.Resolve();
            Directory.CreateDirectory(logDir);
            _logPath = Path.Combine(logDir, $"StartupTrace_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");
            _lastTimestamp = _sw.ElapsedMilliseconds;

            try
            {
                _writer = new StreamWriter(_logPath, append: false, System.Text.Encoding.UTF8)
                {
                    AutoFlush = true
                };
                WriteLine("================================================================================");
                WriteLine($"STARTUP DIAGNOSTIC TRACE — {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                WriteLine($"Process: {Process.GetCurrentProcess().Id}, Thread: {Thread.CurrentThread.ManagedThreadId}");
                WriteLine("================================================================================");
            }
            catch { }
        }

        public void Phase(string phase, string detail = "")
        {
            var now = _sw.ElapsedMilliseconds;
            var delta = now - _lastTimestamp;
            var total = now;

            WriteLine($"[+{total,6}ms][+{delta,5}ms][T{Thread.CurrentThread.ManagedThreadId,2}] " +
                      $"{_lastPhase} → {phase} {detail}");
            _lastPhase = phase;
            _lastTimestamp = now;
        }

        public void Log(string level, string message)
        {
            var now = _sw.ElapsedMilliseconds;
            WriteLine($"[+{now,6}ms][T{Thread.CurrentThread.ManagedThreadId,2}] [{level}] {message}");
        }

        public void Error(string message, Exception? ex = null)
        {
            Log("ERROR", message);
            if (ex != null)
            {
                WriteLine($"  Exception: {ex.GetType().Name}: {ex.Message}");
                WriteLine($"  Stack: {ex.StackTrace?.Replace("\n", "\n  ")}");
            }
        }

        public void ServiceResolve(string serviceName, long elapsedMs)
        {
            var now = _sw.ElapsedMilliseconds;
            WriteLine($"[+{now,6}ms][T{Thread.CurrentThread.ManagedThreadId,2}] [DI] Resolve {serviceName} took {elapsedMs}ms");
        }

        private void WriteLine(string line)
        {
            if (_disposed || _writer == null) return;
            
            lock (_lock)
            {
                try
                {
                    _writer?.WriteLine(line);
                }
                catch { }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            var total = _sw.ElapsedMilliseconds;
            WriteLine("================================================================================");
            WriteLine($"STARTUP COMPLETE — Total: {total}ms");
            WriteLine($"================================================================================");
            
            try
            {
                _writer?.Flush();
                _writer?.Dispose();
            }
            catch { }
        }
    }
}
