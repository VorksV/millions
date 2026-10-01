using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core;
using VoltrisOptimizer.Core.VMRG.Interfaces;
using VoltrisOptimizer.Core.VMRG.Models;

namespace VoltrisOptimizer.Services.VMRG.Services
{
    public class VmMonitorService : IVmMonitorService, IDisposable
    {
        private readonly IVmDetectionService _detection;
        private readonly ILoggingService _logger;

        private List<VmInfo> _activeVms = new();
        private List<VmInfo> _hostProcesses = new();

        public IReadOnlyList<VmInfo> ActiveVms => _activeVms.AsReadOnly();
        public IReadOnlyList<VmInfo> HostProcesses => _hostProcesses.AsReadOnly();
        public bool AnyVmDetected => _activeVms.Count > 0;

        public event EventHandler<VmInfo>? VmDetected;
        public event EventHandler<VmInfo>? VmStateChanged;
        public event EventHandler<VmInfo>? VmRemoved;

        public VmMonitorService(IVmDetectionService detection, ILoggingService logger)
        {
            _detection = detection;
            _logger = logger;
        }

        public async Task RefreshAsync(CancellationToken ct)
        {
            try
            {
                var result = await _detection.DetectAllAsync(ct);

                var previousVms = _activeVms.ToDictionary(v => v.ProcessId);
                var newVms = result.DetectedVms;

                foreach (var vm in newVms)
                {
                    UpdateWindowState(vm);

                    if (previousVms.TryGetValue(vm.ProcessId, out var existing))
                    {
                        var stateChanged = existing.WindowState != vm.WindowState;
                        vm.FirstDetectedAt = existing.FirstDetectedAt;
                        vm.OriginalPriority = existing.OriginalPriority;
                        vm.OriginalAffinityMask = existing.OriginalAffinityMask;
                        vm.CurrentPriority = existing.CurrentPriority;
                        vm.AffinityMask = existing.AffinityMask;

                        if (stateChanged)
                        {
                            vm.LastActiveAt = DateTime.UtcNow;
                            VmStateChanged?.Invoke(this, vm);
                        }
                    }
                    else
                    {
                        vm.FirstDetectedAt = DateTime.UtcNow;
                        vm.LastActiveAt = DateTime.UtcNow;
                        VmDetected?.Invoke(this, vm);
                    }
                }

                var removed = previousVms.Keys.Except(newVms.Select(v => v.ProcessId)).ToList();
                foreach (var pid in removed)
                {
                    if (previousVms.TryGetValue(pid, out var removedVm))
                    {
                        VmRemoved?.Invoke(this, removedVm);
                    }
                }

                _activeVms = newVms;
                _hostProcesses = result.HostProcesses;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[VMRG] Erro no monitoramento: {ex.Message}", ex);
            }
        }

        private static void UpdateWindowState(VmInfo vm)
        {
            try
            {
                var foregroundPid = GetForegroundProcessId();
                if (foregroundPid == vm.ProcessId)
                {
                    vm.WindowState = VmWindowState.Foreground;
                    return;
                }

                using var proc = Process.GetProcessById(vm.ProcessId);
                var mainWindow = proc.MainWindowHandle;
                if (mainWindow == IntPtr.Zero)
                {
                    vm.WindowState = VmWindowState.Minimized;
                    return;
                }

                var placement = new WINDOWPLACEMENT();
                placement.length = Marshal.SizeOf<WINDOWPLACEMENT>();
                if (GetWindowPlacement(mainWindow, ref placement))
                {
                    vm.WindowState = placement.showCmd switch
                    {
                        1 => VmWindowState.Background,
                        2 => VmWindowState.Minimized,
                        3 => VmWindowState.Background,
                        _ => VmWindowState.Background
                    };
                }
            }
            catch
            {
                vm.WindowState = VmWindowState.Closed;
            }
        }

        private static int GetForegroundProcessId()
        {
            var hWnd = GetForegroundWindow();
            if (hWnd == IntPtr.Zero) return -1;
            GetWindowThreadProcessId(hWnd, out int pid);
            return pid;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int processId);

        [DllImport("user32.dll")]
        private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWPLACEMENT
        {
            public int length;
            public int flags;
            public int showCmd;
            public int ptMinPosition_x;
            public int ptMinPosition_y;
            public int ptMaxPosition_x;
            public int ptMaxPosition_y;
            public int rcNormalLeft;
            public int rcNormalTop;
            public int rcNormalRight;
            public int rcNormalBottom;
        }

        public void Dispose()
        {
            _activeVms.Clear();
            _hostProcesses.Clear();
        }
    }
}
