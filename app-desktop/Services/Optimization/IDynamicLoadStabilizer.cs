using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Optimization
{
    public interface IDynamicLoadStabilizer : IDisposable
    {
        bool IsRunning { get; }
        bool Enabled { get; set; }
        TimeSpan Interval { get; set; }
        IReadOnlyCollection<int> ThrottledProcessIds { get; }
        event EventHandler<ThrottledEventArgs>? OnProcessThrottled;
        event EventHandler<ThrottledEventArgs>? OnProcessReleased;
        
        /// <summary>
        /// Ativa/desativa modo seguro para anti-cheat (suspende DSL/ProcessLaunchAccelerator)
        /// </summary>
        void SetAntiCheatActive(bool active);

        /// <summary>
        /// Define o perfil inteligente atual para ajustar o comportamento do motor
        /// </summary>
        void SetProfile(IntelligentProfileType profile);

        /// <summary>
        /// Inicia o estabilizador em modo global (monitora todo o sistema, não apenas um jogo)
        /// </summary>
        Task StartGlobalAsync(CancellationToken ct = default);

        Task StartAsync(int? gameProcessId = null, CancellationToken ct = default);
        Task StopAsync(CancellationToken ct = default);
    }

    public class ThrottledEventArgs : EventArgs
    {
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public double CpuPercent { get; set; }
        public double GpuPercent { get; set; }
        public bool IsHeavy { get; set; }
    }
}

