using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace VoltrisOptimizer.Services.Responsiveness
{
    /// <summary>
    /// Detecta processos pesados com base em CPU% real e Working Set.
    /// Usa heurística baseada em comportamento real — não achismo.
    /// Reutiliza a lista de processos protegidos do StabilityGuardEngine via string set.
    /// </summary>
    internal static class HeavyProcessDetector
    {
        // Processos que NUNCA devem ser throttled — mesma lista do StabilityGuardEngine
        private static readonly HashSet<string> _protected = new(StringComparer.OrdinalIgnoreCase)
        {
            "winlogon", "csrss", "smss", "lsass", "services", "svchost",
            "wininit", "dwm", "explorer", "System", "WUDFHost", "WmiPrvSE",
            "audiodg", "fontdrvhost", "conhost", "lsaiso", "MsMpEng",
            "MpDefenderCoreService", "NisSrv", "SecurityHealthService",
            "spoolsv", "taskhostw", "taskeng", "msdtc", "sppsvc",
            "TrustedInstaller", "TiWorker", "SearchIndexer", "SearchHost",
            "RuntimeBroker", "sihost", "ctfmon", "TextInputHost",
            // Streaming — nunca throttle
            "obs64", "obs32", "obs", "Streamlabs", "TwitchStudio",
            "XSplit.Core", "vMix64", "NVIDIA Share", "NVIDIA Broadcast",
            // Processos Intel/OEM — throttling causa emergency restore loop nos logs
            "IntelCpHDCPSvc",   // Intel Content Protection HDCP Service
            "IntelCpHeciSvc",   // Intel ME HECI Service
            "igfxEM",           // Intel Graphics Event Monitor
            "igfxHK",           // Intel Graphics Hotkey
            "igfxTray",         // Intel Graphics Tray
            "LMS",              // Intel Management Engine Local Management Service
            "jhi_service",      // Intel Dynamic Application Loader
            "esrv_svc",         // Intel Energy Server Service
            "esrv"              // Intel Energy Server
        };

        /// <summary>
        /// Retorna processos pesados de background (não foreground, não protegidos).
        /// Critério: CPU% > threshold OU WorkingSet > threshold.
        /// </summary>
        public static IEnumerable<ProcessCpuSample> GetHeavyBackgroundProcesses(
            IReadOnlyList<ProcessCpuSample> samples,
            ResponsivenessConfig config)
        {
            return samples.Where(s =>
                !s.IsForeground &&
                !_protected.Contains(s.Name) &&
                s.Pid > 4 &&
                (s.CpuPercent >= config.HeavyProcessCpuPercent ||
                 s.WorkingSetBytes >= config.HeavyProcessWorkingSetBytes));
        }

        public static bool IsProtected(string processName) =>
            _protected.Contains(processName);

        public static bool IsProtected(Process p)
        {
            try { return _protected.Contains(p.ProcessName); }
            catch { return true; } // Falha segura: tratar como protegido
        }
    }
}
