using System;

namespace VoltrisOptimizer.Helpers
{
    /// <summary>
    /// Configuração centralizada do sistema de diagnóstico.
    /// Permite ajustar thresholds e comportamentos sem recompilar.
    /// </summary>
    public static class DiagnosticConfig
    {
        // ── Freeze Detection ──────────────────────────────────────────────────
        public static int FreezeThresholdMs { get; set; } = 600; // 0.6s
        public static int WatchdogIntervalMs { get; set; } = 250; // 0.25s
        public static int SlowOperationThresholdMs { get; set; } = 600; // 0.6s — deve ser > que o license wait (500ms) em BootSequenceAsync

        // ── Deadlock Detection ────────────────────────────────────────────────
        public static bool EnableDeadlockMonitoring { get; set; } = true;
        public static int DeadlockMonitorIntervalMs { get; set; } = 1000; // 1s

        // ── First-Chance Exceptions ───────────────────────────────────────────
        public static bool LogFirstChanceExceptions { get; set; } = true;
        public static int MaxFirstChanceExceptions { get; set; } = 50; // Limite para não sobrecarregar

        // ── Thread Dump ───────────────────────────────────────────────────────
        public static bool EnableThreadDumpOnFreeze { get; set; } = true;
        public static int MaxThreadsInDump { get; set; } = 20;

        // ── Startup Timeline ──────────────────────────────────────────────────
        public static bool EnableStartupTimeline { get; set; } = true;
        public static int TimelineMaxEvents { get; set; } = 1000;

        // ── UI Thread Violation Detection ─────────────────────────────────────
        public static bool DetectUIThreadViolations { get; set; } = true;
        public static string[] UIThreadViolationWhitelist { get; set; } = new[]
        {
            "InitializeComponent",
            "OnStartup",
            "OnLoaded",
            "OnActivated",
            "OnClosing"
        };

        // ── WMI Safety ────────────────────────────────────────────────────────
        public static bool DetectWmiOnUIThread { get; set; } = true;
        public static int WmiQueryTimeoutMs { get; set; } = 10000; // 10s

        // ── Logging ───────────────────────────────────────────────────────────
        public static string LogDirectory { get; set; } = "Logs";
        public static bool EnableVerboseLogging { get; set; } = false;

        // ── Performance Counters ──────────────────────────────────────────────
        public static bool MonitorDispatcherQueueDepth { get; set; } = true;
        public static int MaxDispatcherQueueDepth { get; set; } = 50;

        // ── Startup Timeout Protection ────────────────────────────────────────
        public static int StartupTimeoutSeconds { get; set; } = 30;
        public static bool EnableStartupTimeout { get; set; } = true;

        // ── Static Constructor ────────────────────────────────────────────────
        static DiagnosticConfig()
        {
            // Valores padrão otimizados para diagnóstico de freeze intermitente
            // São conservadores para não sobrecarregar o sistema durante o freeze
        }

        /// <summary>
        /// Retorna configuração otimizada para produção (menos verboso).
        /// </summary>
        public static void UseProductionSettings()
        {
            FreezeThresholdMs = 800; // Mais tolerante
            WatchdogIntervalMs = 500; // Menos frequente
            LogFirstChanceExceptions = false;
            EnableVerboseLogging = false;
            MaxFirstChanceExceptions = 10;
        }

        /// <summary>
        /// Retorna configuração otimizada para debug (mais detalhado).
        /// </summary>
        public static void UseDebugSettings()
        {
            FreezeThresholdMs = 300; // Mais sensível
            WatchdogIntervalMs = 100; // Mais frequente
            LogFirstChanceExceptions = true;
            EnableVerboseLogging = true;
            MaxFirstChanceExceptions = 200;
            EnableThreadDumpOnFreeze = true;
        }

        /// <summary>
        /// Retorna configuração para análise forense (máximo detalhe).
        /// </summary>
        public static void UseForensicSettings()
        {
            UseDebugSettings();
            FreezeThresholdMs = 150; // Extremamente sensível
            WatchdogIntervalMs = 50; // Muito frequente
            MaxFirstChanceExceptions = 1000;
            TimelineMaxEvents = 5000;
        }
    }
}
