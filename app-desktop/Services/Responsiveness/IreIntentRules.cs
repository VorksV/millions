using System;

namespace VoltrisOptimizer.Services.Responsiveness
{
    /// <summary>
    /// DSL orientado a intenção para o IRE v2.
    ///
    /// Em vez de "ação direta", as regras descrevem QUANDO e O QUÊ fazer.
    /// O engine avalia as regras em ordem de prioridade e executa a primeira que dispara.
    ///
    /// Exemplo de uso:
    ///   WHEN user_input_detected (< 300ms) THEN boost_foreground(1500ms)
    ///   WHEN cpu > 70% AND disk_queue > 1 THEN throttle_background_aggressive
    ///   WHEN foreground_changed THEN boost_foreground(2000ms)
    ///   WHEN system_idle THEN normalize_background
    /// </summary>
    internal sealed class IreIntentRules
    {
        private readonly ResponsivenessConfig _config;

        public IreIntentRules(ResponsivenessConfig config)
        {
            _config = config;
        }

        /// <summary>
        /// Avalia o snapshot e retorna a intenção de maior prioridade.
        /// Retorna null se nenhuma regra disparar (sistema estável, sem ação necessária).
        /// </summary>
        public IreIntent? Evaluate(SystemTelemetrySnapshot snapshot, int lastForegroundPid)
        {
            // REGRA 1 (prioridade máxima): Input recente detectado
            // Usuário interagiu → boost imediato no foreground
            // Justificativa: GetLastInputInfo < 300ms = usuário acabou de clicar/digitar
            // Isso cria a sensação de "PC instantâneo"
            if (snapshot.HasRecentInput(300) && snapshot.ForegroundPid > 0)
            {
                return new IreIntent
                {
                    Action = IreAction.BoostForeground,
                    TargetPid = snapshot.ForegroundPid,
                    BoostDuration = TimeSpan.FromMilliseconds(_config.InputBoostDurationMs),
                    Reason = BoostReason.UserInput,
                    Description = $"user_input_detected lastInputMs={snapshot.LastInputMs}ms → boost_foreground({_config.InputBoostDurationMs}ms)"
                };
            }

            // REGRA 2: Foreground mudou
            // Usuário trocou de janela → boost no novo foreground
            if (snapshot.ForegroundPid != lastForegroundPid && snapshot.ForegroundPid > 0)
            {
                return new IreIntent
                {
                    Action = IreAction.BoostForeground,
                    TargetPid = snapshot.ForegroundPid,
                    BoostDuration = TimeSpan.FromMilliseconds(_config.ForegroundChangeDurationMs),
                    Reason = BoostReason.ForegroundChange,
                    Description = $"foreground_changed pid={lastForegroundPid}→{snapshot.ForegroundPid} → boost_foreground({_config.ForegroundChangeDurationMs}ms)"
                };
            }

            // REGRA 3: CPU alta E disco congestionado → throttle agressivo
            // Ambas as condições juntas indicam contenção real de recursos
            if (snapshot.IsCpuHigh(_config.CpuThresholdPercent) &&
                snapshot.IsDiskContended(_config.DiskQueueThreshold))
            {
                return new IreIntent
                {
                    Action = IreAction.ThrottleBackgroundAggressive,
                    Description = $"cpu={snapshot.CpuTotalPercent:F1}%>{_config.CpuThresholdPercent}% AND disk_queue={snapshot.DiskQueueLength:F2}>{_config.DiskQueueThreshold} → throttle_background_aggressive"
                };
            }

            // REGRA 4: Apenas disco congestionado → throttle de I/O
            // Disco saturado sem CPU alta: reduzir I/O de background é suficiente
            if (snapshot.IsDiskContended(_config.DiskQueueThreshold))
            {
                return new IreIntent
                {
                    Action = IreAction.ThrottleBackgroundIo,
                    Description = $"disk_queue={snapshot.DiskQueueLength:F2}>{_config.DiskQueueThreshold} → throttle_background_io"
                };
            }

            // REGRA 5: Apenas CPU alta → throttle leve de CPU
            if (snapshot.IsCpuHigh(_config.CpuThresholdPercent))
            {
                return new IreIntent
                {
                    Action = IreAction.ThrottleBackgroundCpu,
                    Description = $"cpu={snapshot.CpuTotalPercent:F1}%>{_config.CpuThresholdPercent}% → throttle_background_cpu"
                };
            }

            // REGRA 6: Sistema idle → normalizar background
            // Sem pressão, sem input recente: restaurar prioridades
            bool systemIdle = !snapshot.IsCpuHigh(_config.CpuThresholdPercent * 0.5) &&
                              !snapshot.IsDiskContended(_config.DiskQueueThreshold * 0.5f) &&
                              !snapshot.HasRecentInput(5000);

            if (systemIdle)
            {
                return new IreIntent
                {
                    Action = IreAction.NormalizeBackground,
                    Description = $"system_idle cpu={snapshot.CpuTotalPercent:F1}% disk={snapshot.DiskQueueLength:F2} lastInput={snapshot.LastInputMs}ms → normalize_background"
                };
            }

            // Nenhuma regra disparou — sistema estável
            return null;
        }
    }

    /// <summary>
    /// Intenção avaliada pelo engine de regras.
    /// Descreve O QUÊ fazer e POR QUÊ — não como.
    /// </summary>
    internal sealed class IreIntent
    {
        public IreAction Action { get; init; }
        public int TargetPid { get; init; }
        public TimeSpan BoostDuration { get; init; }
        public BoostReason Reason { get; init; }
        public string Description { get; init; } = "";
    }

    internal enum IreAction
    {
        BoostForeground,
        ThrottleBackgroundAggressive,  // CPU alta + disco congestionado
        ThrottleBackgroundIo,          // Apenas disco congestionado
        ThrottleBackgroundCpu,         // Apenas CPU alta
        NormalizeBackground
    }
}
