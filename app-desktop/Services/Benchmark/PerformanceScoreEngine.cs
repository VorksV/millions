using System;
using OnbModel = VoltrisOptimizer.Models;

namespace VoltrisOptimizer.Services.Benchmark
{
    /// <summary>
    /// Calcula score de performance de forma isolada e testável.
    /// Separa score ESTRUTURAL (persistente) de DINÂMICO (sessão).
    /// Usa média móvel de 5 amostras para suavizar oscilações.
    /// Score 100 = "melhor estado seguro para ESTE hardware e ESTE contexto"
    /// </summary>
    public sealed class PerformanceScoreEngine
    {
        private const int MaxSamples = 5;
        private readonly double[] _dynamicSamples = new double[MaxSamples];
        private int _sampleIndex;
        private int _sampleCount;

        /// <summary>
        /// Calcula score ESTRUTURAL (0-70). Muda apenas com otimizações.
        /// Pontuação contextual: o que é "bom" depende do hardware.
        /// </summary>
        public int CalculateStructural(PerformanceSnapshot snap, OnbModel.HardwareProfile? hardware = null)
        {
            int score = 0;

            // ── Timer Resolution (0-20 pts) ──
            // 0.5ms = 20pts, 1ms = 18pts, 5ms = 12pts, 15.6ms = 0pts
            // Timer muito agressivo (<0.5ms) não pontua mais — inseguro
            double timerMs = snap.TimerResolutionMs;
            if (timerMs <= 1.0)
                score += 20;
            else if (timerMs <= 2.0)
                score += 18;
            else if (timerMs <= 5.0)
                score += 12;
            else if (timerMs <= 10.0)
                score += 6;
            // >10ms = 0pts (padrão)

            // ── EPP (0-20 pts) ──
            // Pontuação contextual baseada no hardware
            int epp = snap.Epp;
            if (hardware != null)
            {
                if (hardware.MachineClass == OnbModel.MachineClass.Laptop)
                {
                    if (epp <= 35) score += 20;
                    else if (epp <= 50) score += 16;
                    else if (epp <= 75) score += 8;
                }
                else if (hardware.IsLowEnd)
                {
                    if (epp <= 35) score += 18;
                    else if (epp <= 50) score += 16;
                    else if (epp <= 75) score += 10;
                }
                else
                {
                    if (epp <= 15) score += 20;
                    else if (epp <= 25) score += 18;
                    else if (epp <= 50) score += 12;
                    else if (epp <= 75) score += 6;
                }
            }
            else
            {
                // Fallback: 0=20pts, 100=0pts
                double eppScore = Math.Max(0, Math.Min(20, 20 - (epp / 100.0 * 20)));
                score += (int)Math.Round(eppScore);
            }

            // ── HAGS (0-8 pts) ──
            score += snap.HagsEnabled ? 8 : 0;

            // ── Gaming Mode / Performance Mode (0-8 pts) ──
            if (snap.PowerPlanName.Contains("High Performance", StringComparison.OrdinalIgnoreCase)
                || snap.Epp <= 25)
                score += 8;

            // ── SSD / NVMe (0-6 pts) ──
            score += snap.HasSsd ? 6 : 0;

            // ── GPU Tier bonus (0-8 pts) ──
            if (hardware != null)
            {
                if ((int)hardware.GpuTier >= (int)OnbModel.GpuTier.HighEnd)
                    score += 8;
                else if ((int)hardware.GpuTier >= (int)OnbModel.GpuTier.MidRange)
                    score += 4;
            }

            return Math.Min(70, Math.Max(0, score));
        }

        /// <summary>
        /// Calcula score DINÂMICO (0-30). Varia durante a sessão.
        /// Suavizado com média móvel de 5 amostras.
        /// </summary>
        public int CalculateDynamic(PerformanceSnapshot snap)
        {
            int score = 0;

            // RAM disponível: >8GB=15pts, <1GB=0pts
            double ram = Math.Max(0, Math.Min(15, snap.AvailableRamGb / 8.0 * 15));
            score += (int)Math.Round(ram);

            // Processos: <60=10pts, >120=0pts
            double proc = Math.Max(0, Math.Min(10, 10 - (Math.Max(0, snap.ProcessCount - 60) / 60.0 * 10)));
            score += (int)Math.Round(proc);

            // Disk queue: <1.0=5pts, >3.0=0pts
            double disk = Math.Max(0, Math.Min(5, 5 - (Math.Max(0, snap.DiskQueueLength - 1.0) / 2.0 * 5)));
            score += (int)Math.Round(disk);

            return Math.Min(30, Math.Max(0, score));
        }

        /// <summary>
        /// Atualiza média móvel e retorna score dinâmico suavizado.
        /// </summary>
        public int SmoothDynamic(PerformanceSnapshot snap)
        {
            int raw = CalculateDynamic(snap);
            _dynamicSamples[_sampleIndex % MaxSamples] = raw;
            _sampleIndex++;
            _sampleCount = Math.Min(MaxSamples, _sampleCount + 1);

            double sum = 0;
            for (int i = 0; i < _sampleCount; i++)
                sum += _dynamicSamples[i];

            return (int)Math.Round(sum / _sampleCount);
        }

        public void ResetSmoothing()
        {
            _sampleIndex = 0;
            _sampleCount = 0;
            Array.Clear(_dynamicSamples, 0, MaxSamples);
        }
    }
}
