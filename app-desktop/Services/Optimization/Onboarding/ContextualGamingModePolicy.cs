using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using VoltrisOptimizer.Core.Body;
using VoltrisOptimizer.Models;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Optimization.Onboarding
{
    public sealed class ContextualGamingModePolicy
    {
        private readonly ILoggingService _logger;
        private readonly ISystemArm _systemArm;

        public bool GamingModeApplied { get; private set; }
        public bool Skipped { get; private set; }
        public string Reason { get; private set; } = string.Empty;

        public ContextualGamingModePolicy(ILoggingService logger, ISystemArm systemArm)
        {
            _logger = logger;
            _systemArm = systemArm;
        }

        public async Task<ContextualGamingModePolicy> EvaluateAsync(HardwareProfile hardware, PowerSource power)
        {
            _logger?.LogInfo("[GamingModePolicy] Avaliando ativação do modo desempenho...");
            var sw = Stopwatch.StartNew();

            if (power == PowerSource.Battery && hardware.IsLowEnd)
            {
                Skipped = true;
                Reason = "Low-end em bateria — modo desempenho não ativado para preservar energia";
                _logger?.LogInfo($"[GamingModePolicy] {Reason}");
                return this;
            }

            if (power == PowerSource.Battery)
            {
                _logger?.LogInfo("[GamingModePolicy] Bateria detectado — ativação condicional");
            }


            // Lógica original continua abaixo
            try
            {
                _logger?.LogInfo("[GamingModePolicy] Ativando modo de desempenho do sistema...");
                var result = await _systemArm.EnableGamingModeAsync(true);
                if (result.Success)
                {
                    GamingModeApplied = true;
                    Reason = "Modo desempenho ativado para otimização responsiva";
                    _logger?.LogSuccess($"[GamingModePolicy] Ativado em {sw.ElapsedMilliseconds}ms");
                }
                else if (result.GuardBlocked)
                {
                    Skipped = true;
                    Reason = $"Modo desempenho bloqueado pelo guard: {result.GuardReason}";
                    _logger?.LogInfo($"[GamingModePolicy] {Reason}");
                }
                else
                {
                    _logger?.LogWarning($"[GamingModePolicy] Ativação retornou falha: {result.GuardReason}");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[GamingModePolicy] Erro: {ex.Message}");
            }

        return this;
    }
    }
}
