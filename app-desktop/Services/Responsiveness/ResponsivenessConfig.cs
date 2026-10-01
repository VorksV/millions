namespace VoltrisOptimizer.Services.Responsiveness
{
    /// <summary>
    /// Configuração do IRE v2 — todos os thresholds e cooldowns em um lugar.
    /// Pode ser sobrescrita via DSL ou settings.
    /// </summary>
    public sealed class ResponsivenessConfig
    {
        // Startup Mode
        public int StartupBudgetMs { get; set; } = 200;

        // Adaptive Loop
        public int AdaptiveIntervalMs { get; set; } = 3000;   // 3s padrão
        public int AdaptiveIntervalHighLoadMs { get; set; } = 2000; // 2s sob carga
        public int AdaptiveIntervalIdleMs { get; set; } = 5000;    // 5s em idle

        // Thresholds para ação
        public double CpuThresholdPercent { get; set; } = 70.0;
        public float DiskQueueThreshold { get; set; } = 1.0f;
        public double HeavyProcessCpuPercent { get; set; } = 15.0; // processo pesado = >15% CPU
        public long HeavyProcessWorkingSetBytes { get; set; } = 200 * 1024 * 1024; // 200MB

        // Cooldowns por ação (evita flood)
        public int ForegroundBoostCooldownMs { get; set; } = 1500;
        public int IoPriorityCooldownMs { get; set; } = 4000;
        public int NormalizeBackgroundCooldownMs { get; set; } = 8000;

        // Decay System — duração dos boosts antes de expirar automaticamente
        // Input boost: curto (usuário pode mudar de foco rapidamente)
        public int InputBoostDurationMs { get; set; } = 1500;
        // Foreground change boost: um pouco mais longo (app precisa de tempo para carregar)
        public int ForegroundChangeDurationMs { get; set; } = 2000;

        // Focus Mode
        public bool EnableFocusMode { get; set; } = true;
        public double FocusModeFullscreenCpuThreshold { get; set; } = 5.0; // background cap em focus mode
    }
}
