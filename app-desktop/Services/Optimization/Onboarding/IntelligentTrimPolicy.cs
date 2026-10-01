using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using VoltrisOptimizer.Core;
using VoltrisOptimizer.Core.Body;
using VoltrisOptimizer.Models;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Optimization.Onboarding
{
    public sealed class IntelligentTrimPolicy
    {
        private readonly ILoggingService _logger;
        private readonly IProcessArm _processArm;

        public bool TrimApplied { get; private set; }
        public bool Skipped { get; private set; }
        public string Reason { get; private set; } = string.Empty;
        public long MbRecovered { get; private set; }
        public bool IsAlreadyOptimized { get; private set; }

        private static readonly string[] ProtectedProcesses =
        {
            "csrss", "wininit", "services", "lsass", "svchost", "system",
            "idle", "dwm", "voltrisoptimizer", "explorer",
            "discord", "obs64", "obs32", "browser", "chrome", "firefox",
            "msedge", "opera", "brave", "devenv", "code", "rider",
            "winword", "excel", "powerpnt", "outlook", "teams",
            "spotify", "audiodg"
        };

        public IntelligentTrimPolicy(ILoggingService logger, IProcessArm processArm)
        {
            _logger = logger;
            _processArm = processArm;
        }

        public async Task<IntelligentTrimPolicy> EvaluateAsync(HardwareProfile hardware, ThermalState thermal)
        {
            _logger?.LogInfo("[TrimPolicy] Avaliando liberação de memória...");
            var sw = Stopwatch.StartNew();

            var cache = SystemMetricsCache.Instance;
            double availableRamGb = cache.AvailableRamMb / 1024.0;
            double totalRamGb = hardware.TotalRamGb;

            _logger?.LogInfo($"[TrimPolicy] RAM disponível: {availableRamGb:F1}GB / {totalRamGb:F1}GB total");

            if (totalRamGb >= 32 && availableRamGb > totalRamGb * 0.5)
            {
                Skipped = true;
                IsAlreadyOptimized = true;
                Reason = $"RAM disponível saudável ({availableRamGb:F1}GB disponível de {totalRamGb:F1}GB) — trim desnecessário";
                _logger?.LogInfo($"[TrimPolicy] {Reason}");
                return this;
            }

            if (availableRamGb >= 6)
            {
                Skipped = true;
                IsAlreadyOptimized = true;
                Reason = $"RAM suficiente ({availableRamGb:F1}GB disponível) — trim não necessário";
                _logger?.LogInfo($"[TrimPolicy] {Reason}");
                return this;
            }

            if (thermal == ThermalState.Throttling || thermal == ThermalState.Hot)
            {
                Skipped = true;
                Reason = "Temperatura elevada — trim adiado para evitar páginamento agressivo";
                _logger?.LogInfo($"[TrimPolicy] {Reason}");
                return this;
            }

            try
            {
                _logger?.LogInfo($"[TrimPolicy] Iniciando trim inteligente (alvo: processos background >50MB)...");
                var result = await _processArm.TrimWorkingSetAsync(null);
                if (result != null)
                {
                    TrimApplied = result.ProcessCount > 0;
                    MbRecovered = result.MbReleased;
                    if (TrimApplied)
                    {
                        Reason = $"{result.ProcessCount} processos otimizados, {result.MbReleased}MB liberados";
                        _logger?.LogSuccess($"[TrimPolicy] {Reason} em {sw.ElapsedMilliseconds}ms");
                    }
                    else if (result.RequiresAdmin)
                    {
                        Reason = "Trim requer admin — pulando";
                        _logger?.LogWarning("[TrimPolicy] Requer admin");
                    }
                    else
                    {
                        Reason = "Nenhum processo elegível para trim";
                        _logger?.LogInfo($"[TrimPolicy] {Reason}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[TrimPolicy] Erro: {ex.Message}");
            }

            return this;
        }
    }
}
