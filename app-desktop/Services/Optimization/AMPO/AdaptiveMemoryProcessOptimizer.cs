using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.Optimization.AMPO;

namespace VoltrisOptimizer.Services.Optimization.AMPO
{
    /// <summary>
    /// Implementação do AMPO com heurísticas seguras de memória e moderação de IO interno.
    /// </summary>
    public class AdaptiveMemoryProcessOptimizer : IAdaptiveMemoryProcessOptimizer
    {
        private readonly object _lock = new();
        private CancellationTokenSource? _cts;
        private Task? _watchTask;
        private readonly AmpoLogger _logger = new();
        private int? _gamePid;

        // PERFORMANCE FIX: PerformanceCounters removidos — GlobalMemoryStatusEx (nativo) já fornece
        // os mesmos dados sem overhead PDH. Committed = TotalPageFile - AvailPageFile.

        private DateTime _lastIoModeration = DateTime.MinValue;
        private readonly TimeSpan _ioModerationWindow = TimeSpan.FromSeconds(3);

        private readonly HashSet<string> _sensitiveNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "dwm", "csrss", "winlogon", "explorer", "System", "nvcontainer", "amd", "intel",
            "chrome", "msedge", "firefox", "opera", "brave"};

        public bool IsRunning { get; private set; }
        public bool Enabled { get; set; } = true;

        public AdaptiveMemoryProcessOptimizer() { }

        public async Task StartWatcherAsync(int? gameProcessId = null, CancellationToken ct = default)
        {
            if (!Enabled) return;
            lock (_lock)
            {
                if (IsRunning) return;
                _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                _gamePid = gameProcessId;
                IsRunning = true;
                _watchTask = Task.Run(() => WatchLoop(_cts.Token));
            }
            await Task.CompletedTask;
        }

        public async Task StopWatcherAsync(CancellationToken ct = default)
        {
            lock (_lock)
            {
                if (!IsRunning) return;
                try { _cts?.Cancel(); } catch { }
            }
            try { if (_watchTask != null) await _watchTask; } catch { }
            lock (_lock)
            {
                IsRunning = false;
                _cts?.Dispose();
                _cts = null;
                _gamePid = null;
            }
        }

        public void CheckAndOptimizeSafely()
        {
            try
            {
                var info = GC.GetGCMemoryInfo();
                var committed = GetCommittedBytes();
                var commitLimit = GetCommitLimitBytes();
                var ratio = commitLimit > 0 ? (double)committed / commitLimit : 0.0;

                var pressure = ratio >= 0.85 || info.HighMemoryLoadThresholdBytes > 0 && info.MemoryLoadBytes >= info.HighMemoryLoadThresholdBytes;
                var nearSaturation = ratio >= 0.92;

                if (_gamePid.HasValue && pressure)
                {
                    CompactOwnMemory(nearSaturation);
                    // CORREÇÃO: .Wait() num contexto de loop bloqueante pode causar deadlock se o
                    // VoltrisMemoryCoordinator internamente usa ConfigureAwait(true) ou captura o
                    // SynchronizationContext. Fire-and-forget é seguro aqui porque o AMPO é um
                    // serviço de background sem dependência no resultado imediato.
                    _ = global::VoltrisOptimizer.Services.Optimization.Memory.VoltrisMemoryCoordinator.Instance.OptimizeMemoryAsync(isManualClick: false);
                    ModerateOwnIoIfNeeded();
                    _logger.Log($"[AMPO] Pressure detected ratio={(ratio*100):F1}% committed={committed/1024/1024}MB limit={commitLimit/1024/1024}MB");
                }
            }
            catch { }
        }

        private void WatchLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    CheckAndOptimizeSafely();
                    Thread.Sleep(3000);
                }
                catch { Thread.Sleep(3000); }
            }
        }

        private long GetCommittedBytes()
        {
            try
            {
                var ms = new MEMORYSTATUSEX();
                ms.dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
                if (GlobalMemoryStatusEx(ref ms)) return (long)ms.ullTotalPageFile - (long)ms.ullAvailPageFile;
            }
            catch { }
            return 0;
        }

        private long GetCommitLimitBytes()
        {
            try
            {
                var ms = new MEMORYSTATUSEX();
                ms.dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
                if (GlobalMemoryStatusEx(ref ms)) return (long)ms.ullTotalPageFile;
            }
            catch { }
            return 0;
        }

        private void CompactOwnMemory(bool aggressive)
        {
            try
            {
                if (aggressive)
                    System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                _logger.Log("[AMPO] Own memory compacted");
            }
            catch { }
        }

        private void ReduceLightWorkingSets()
        {
            // Substituído por VoltrisMemoryCoordinator para evitar hard faults
        }

        private void ModerateOwnIoIfNeeded()
        {
            try
            {
                var now = DateTime.Now;
                if ((now - _lastIoModeration) < _ioModerationWindow) return;
                _lastIoModeration = now;
                Thread.Sleep(50); // pequena desaceleração interna para evitar bursts
                _logger.Log("[AMPO] IO internal moderated");
            }
            catch { }
        }

        // TryEmptyWorkingSet removido para evitar Hard Faults.
        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
        [DllImport("psapi.dll")] private static extern bool EmptyWorkingSet(IntPtr hProcess);
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr hObject);
    }
}

