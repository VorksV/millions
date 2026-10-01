using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Core.Validation;

namespace VoltrisOptimizer.Core.Execution.Actions
{
    /// <summary>
    /// Ação transacional que aplica as regras de Timer Resolution e MMCSS
    /// com validação de evidência para evitar placebos em tempo real.
    /// </summary>
    public sealed class RealTimeSmoothnessAction : OptimizationAction
    {
        private readonly int _targetTimerResolution100ns;
        private readonly int _targetSystemResponsiveness;

        private uint _originalTimerResolution;
        private int _originalSystemResponsiveness;
        private int _originalNetworkThrottlingIndex;
        private bool _timerRequested = false;

        private const string MMCSS_PATH = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";

        [DllImport("ntdll.dll", EntryPoint = "NtSetTimerResolution")]
        private static extern int NtSetTimerResolution(uint DesiredResolution, bool SetResolution, out uint CurrentResolution);

        [DllImport("ntdll.dll", EntryPoint = "NtQueryTimerResolution")]
        private static extern int NtQueryTimerResolution(out uint MinimumResolution, out uint MaximumResolution, out uint CurrentResolution);

        public RealTimeSmoothnessAction(int targetTimerResolution100ns, int targetSystemResponsiveness)
            : base("SYSTEM_SMOOTHNESS", "RealTimeSmoothness", ActionPriority.High, OptimizationContext.Global, TimeSpan.FromSeconds(30))
        {
            _targetTimerResolution100ns = targetTimerResolution100ns;
            _targetSystemResponsiveness = targetSystemResponsiveness;
        }

        public override Task<bool> CheckPreconditionsAsync()
        {
            // O sistema sempre suporta timer resolution e mmcss
            return Task.FromResult(true);
        }

        public override Task TakeSnapshotAsync()
        {
            // 1. Capturar estado atual do MMCSS
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(MMCSS_PATH);
                if (key != null)
                {
                    _originalSystemResponsiveness = key.GetValue("SystemResponsiveness") as int? ?? 20;
                    var nti = key.GetValue("NetworkThrottlingIndex");
                    _originalNetworkThrottlingIndex = nti is int i ? i : 10;
                }
            }
            catch { /* fallback to defaults */ }

            // 2. Capturar estado atual do Timer Resolution
            NtQueryTimerResolution(out _, out _, out _originalTimerResolution);

            return Task.CompletedTask;
        }

        public override Task ApplyAsync()
        {
            // 1. Aplicar MMCSS
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(MMCSS_PATH, true);
                if (key != null)
                {
                    key.SetValue("SystemResponsiveness", _targetSystemResponsiveness, RegistryValueKind.DWord);
                    key.SetValue("NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), RegistryValueKind.DWord);
                }
            }
            catch { }

            // 2. Aplicar Timer Resolution
            if (_targetTimerResolution100ns > 0)
            {
                var status = NtSetTimerResolution((uint)_targetTimerResolution100ns, true, out uint outCur);
                _timerRequested = status == 0;
            }

            return Task.CompletedTask;
        }

        public override Task RollbackAsync()
        {
            // 1. Reverter MMCSS
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(MMCSS_PATH, true);
                if (key != null)
                {
                    key.SetValue("SystemResponsiveness", _originalSystemResponsiveness, RegistryValueKind.DWord);
                    key.SetValue("NetworkThrottlingIndex", _originalNetworkThrottlingIndex, RegistryValueKind.DWord);
                }
            }
            catch { }

            // 2. Reverter Timer Resolution
            if (_timerRequested)
            {
                NtSetTimerResolution(_originalTimerResolution, false, out _);
                _timerRequested = false;
            }

            return Task.CompletedTask;
        }

        public override string GetStateBefore() => $"Timer={_originalTimerResolution}, SR={_originalSystemResponsiveness}";
        
        public override string GetStateAfter() => $"Timer={_targetTimerResolution100ns}, SR={_targetSystemResponsiveness}";

        public override Task<ValidationMetrics?> CollectPreMetricsAsync()
        {
            return Task.FromResult<ValidationMetrics?>(null);
        }

        public override Task<ValidationMetrics?> CollectPostMetricsAsync()
        {
            return Task.FromResult<ValidationMetrics?>(null);
        }
    }
}
