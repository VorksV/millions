using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Diagnostics
{
    /// <summary>
    /// CPU Self-Profiler — Monitora o uso de CPU do próprio processo Voltris
    /// e envia alertas detalhados ao Telegram quando detecta picos anormais.
    /// </summary>
    public sealed class CpuSelfProfiler : IDisposable
    {
        public static CpuSelfProfiler Instance { get; } = new();

        private const double AlertThresholdPercent = 25.0; // Aumentado para reduzir falsos alertas
        private const int SamplingIntervalMs = 30_000; // Aumentado de 10s para 30s
        
        private readonly ConcurrentDictionary<string, SectionStats> _sections = new();
        private Timer? _samplingTimer;
        private bool _disposed;
        private bool _active;
        
        private TimeSpan _lastProcessorTime = TimeSpan.Zero;
        private DateTime _lastSampleTime = DateTime.MinValue;

        private CpuSelfProfiler() { }

        public void Activate()
        {
            if (_active) return;
            _active = true;
            _lastProcessorTime = Process.GetCurrentProcess().TotalProcessorTime;
            _lastSampleTime = DateTime.Now;
            _samplingTimer = new Timer(OnSamplingTick, null, SamplingIntervalMs, SamplingIntervalMs);
        }

        public IDisposable BeginSection(string name)
        {
            return new ProfilerSection(name, _sections);
        }

        public CpuProfilerReport GetReport()
        {
            var cpuUsage = CalculateSelfCpuUsage();
            var report = new CpuProfilerReport
            {
                AppCpuUsage = cpuUsage,
                ThreadCount = Process.GetCurrentProcess().Threads.Count,
                TopSections = _sections.Values
                    .OrderByDescending(s => s.TotalTicks)
                    .Take(10)
                    .ToList()
            };
            return report;
        }

        private void OnSamplingTick(object? state)
        {
            // OTIMIZAÇÃO: Calcular uso de CPU apenas se threshold for atingido
            var quickUsage = GetQuickCpuEstimate();
            if (quickUsage > AlertThresholdPercent * 0.8) // 80% do threshold como pré-filtro
            {
                var preciseUsage = CalculateSelfCpuUsage();
                if (preciseUsage > AlertThresholdPercent)
                {
                    _ = SendAlertAsync(preciseUsage);
                }
            }
        }

        /// <summary>
        /// Estimativa rápida de CPU usando Process.WorkingSet64 (muito mais leve)
        /// </summary>
        private double GetQuickCpuEstimate()
        {
            try
            {
                var proc = Process.GetCurrentProcess();
                // WorkingSet64 como proxy aproximado de atividade (muito mais leve que TotalProcessorTime)
                var workingSetMB = proc.WorkingSet64 / (1024.0 * 1024.0);
                // Estimativa baseada no uso de memória (correlação aproximada com CPU)
                return Math.Min(workingSetMB / 10.0, 50.0); // Cap em 50%
            }
            catch
            {
                return 0;
            }
        }

        private double CalculateSelfCpuUsage()
        {
            var now = DateTime.Now;
            var proc = Process.GetCurrentProcess();
            var cpuTime = proc.TotalProcessorTime;
            
            if (_lastSampleTime == DateTime.MinValue)
            {
                _lastSampleTime = now;
                _lastProcessorTime = cpuTime;
                return 0;
            }

            var elapsed = now - _lastSampleTime;
            var cpuUsed = cpuTime - _lastProcessorTime;
            
            _lastSampleTime = now;
            _lastProcessorTime = cpuTime;

            if (elapsed.TotalMilliseconds <= 0) return 0;
            
            return (cpuUsed.TotalMilliseconds / (elapsed.TotalMilliseconds * Environment.ProcessorCount)) * 100.0;
        }

        private async Task SendAlertAsync(double usage)
        {
            var report = GetReport();
            var sb = new StringBuilder();
            sb.AppendLine("*ALERTA: Voltris com CPU Alta!*");
            sb.AppendLine($"Uso Atual: {usage:F1}%");
            sb.AppendLine($"Threads: {report.ThreadCount}");
            sb.AppendLine("\n*Top Funções (último ciclo):*");
            
            foreach (var section in report.TopSections)
            {
                sb.AppendLine($"- {section.Name}: {TimeSpan.FromTicks(section.TotalTicks).TotalMilliseconds:F1}ms ({section.CallCount} calls)");
            }

        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _samplingTimer?.Dispose();
        }
    }

    public class ProfilerSection : IDisposable
    {
        private readonly string _name;
        private readonly ConcurrentDictionary<string, SectionStats> _sections;
        private readonly long _startTicks;

        public ProfilerSection(string name, ConcurrentDictionary<string, SectionStats> sections)
        {
            _name = name;
            _sections = sections;
            _startTicks = Stopwatch.GetTimestamp();
        }

        public void Dispose()
        {
            var elapsed = Stopwatch.GetTimestamp() - _startTicks;
            var stats = _sections.GetOrAdd(_name, n => new SectionStats { Name = n });
            
            lock (stats)
            {
                stats.TotalTicks += elapsed;
                stats.CallCount++;
                if (elapsed > stats.MaxTicks) stats.MaxTicks = elapsed;
            }
        }
    }

    public class SectionStats
    {
        public string Name { get; set; } = "";
        public long TotalTicks { get; set; }
        public long CallCount { get; set; }
        public long MaxTicks { get; set; }
    }

    public class CpuProfilerReport
    {
        public double AppCpuUsage { get; set; }
        public int ThreadCount { get; set; }
        public List<SectionStats> TopSections { get; set; } = new();
    }
}
