using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.Gamer.Implementation;

namespace VoltrisOptimizer.Services.Optimization.Engines
{
    /// <summary>
    /// DSL 5.0 - Process Launch Accelerator
    /// Detecta novos processos via WMI e aplica boost temporário de CPU + I/O
    /// para acelerar inicialização de programas.
    /// </summary>
    public class ProcessLaunchAccelerator : IDisposable
    {
        private readonly ILoggingService _logger;
        private ManagementEventWatcher? _processWatcher;
        private readonly ConcurrentDictionary<int, BoostEntry> _boostedProcesses = new();
        private readonly HashSet<string> _systemProcesses;
        private readonly int _ownPid;
        private Timer? _cleanupTimer;
        private bool _disposed;
        private volatile bool _antiCheatActive;

        public void SetAntiCheatActive(bool active)
        {
            _antiCheatActive = active;
        }

        // Debounce por nome de processo: evita boost flood quando um app abre múltiplos filhos
        // (ex: msedge, chrome, code — cada um spawna 5-15 processos filhos em sequência)
        // Chave: nome do processo, Valor: timestamp do último boost aplicado para esse nome
        private readonly ConcurrentDictionary<string, DateTime> _lastBoostByName = new(StringComparer.OrdinalIgnoreCase);
        private const int BoostDebounceSeconds = 5; // Só aplica boost 1x por nome a cada 5s

        private class BoostEntry
        {
            public int Pid { get; set; }
            public string Name { get; set; } = "";
            public DateTime BoostAppliedAt { get; set; }
            public ProcessPriorityClass OriginalPriority { get; set; }
            public bool Restored { get; set; }
        }

        /// <summary>
        /// Duração do boost de startup em segundos
        /// </summary>
        public int BoostDurationSeconds { get; set; } = 8;

        public ProcessLaunchAccelerator(ILoggingService logger)
        {
            _logger = logger;
            _ownPid = Process.GetCurrentProcess().Id;
            _systemProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "System", "Idle", "csrss", "wininit", "services", "lsass",
                "svchost", "dwm", "fontdrvhost", "WmiPrvSE", "RuntimeBroker",
                "conhost", "dllhost", "taskhostw", "sihost", "SearchProtocolHost",
                "SearchFilterHost", "backgroundTaskHost", "SecurityHealthSystray",
                "ctfmon", "CompPkgSrv", "TextInputHost",
                // Processos críticos do kernel/sistema que NUNCA devem receber boost
                "Registry", "smss", "winlogon", "wininit", "lsaiso",
                "Memory Compression", "SecurityHealthService", "MsMpEng",
                "MpDefenderCoreService", "NisSrv", "audiodg", "spoolsv",
                "WUDFHost", "wlanext", "wlms", "smartscreen",
                // Processos Intel/OEM — não precisam de startup boost, causam ruído nos logs
                "IntelCpHDCPSvc", "IntelCpHeciSvc", "igfxEM", "igfxHK", "igfxTray",
                "LMS", "jhi_service", "esrv_svc", "esrv",
                
                // ✅ ANTI-CHEATS & GAMES (ISOLATION TEST)
                // NUNCA aplicar boost de CPU/EcoQoS em Anti-Cheats ou executáveis de jogos,
                // pois as chamadas OpenProcess e SetProcessInformation são interceptadas
                // pelo Anti-Cheat, que derruba o jogo instantaneamente por suspeita de injeção.
                "BEService", "BEService_x64", "BEService_gta", // BattlEye
                "EasyAntiCheat", "EasyAntiCheat_EOS", // EAC
                "vgc", "vgtray", "vanguard", // Riot Vanguard
                "anticheat", "GameGuard", "nProtect", 
                "gta5", "gta5_enhanced", "gta5_enhanced_be", "playgtav", "gtavlauncher", // GTA V
                "steam", "steamwebhelper", "epicgameslauncher", "origin", "upc", "eadesktop" // Launchers
            };
        }

        /// <summary>
        /// Inicia detecção de novos processos via WMI (Win32_ProcessStartTrace).
        /// Requer privilégios de administrador.
        /// </summary>
        public void Start()
        {
            try
            {
                // WMI Win32_ProcessStartTrace — detecção quase instantânea via kernel callback
                _processWatcher = new ManagementEventWatcher(
                    new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace"));
                _processWatcher.EventArrived += OnProcessStarted;
                _processWatcher.Start();

                // Timer para remover boosts expirados
                _cleanupTimer = new Timer(CleanupExpiredBoosts, null, 
                    TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));

                _logger.LogInfo("[LaunchAccelerator] WMI process detection ACTIVE — startup boost enabled");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[LaunchAccelerator] WMI detection failed ({ex.Message}), falling back to polling mode");
                // Fallback: polling será feito pelo MonitorLoop via CheckNewProcesses
            }
        }

        private void OnProcessStarted(object sender, EventArrivedEventArgs e)
        {
            try
            {
                var processName = e.NewEvent.Properties["ProcessName"]?.Value?.ToString() ?? "";
                var pid = Convert.ToInt32(e.NewEvent.Properties["ProcessID"]?.Value ?? 0);

                if (pid <= 4 || pid == _ownPid) return;
                if (string.IsNullOrEmpty(processName)) return;

                // Remover extensão .exe
                var cleanName = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? processName[..^4] : processName;

                // Ignorar processos do sistema que iniciam frequentemente
                if (_systemProcesses.Contains(cleanName)) return;

                // Suspender DSL completamente quando anti-cheat (BattlEye/EAC) estiver ativo
                if (_antiCheatActive) return;

                ApplyStartupBoost(pid, cleanName);
            }
            catch { }
        }

        /// <summary>
        /// Fallback: detecta novos processos comparando com snapshot anterior.
        /// Chamado pelo MonitorLoop quando WMI não está disponível.
        /// </summary>
        public void CheckNewProcesses(Process[] currentProcesses, HashSet<int> previousPids)
        {
            foreach (var p in currentProcesses)
            {
                try
                {
                    if (p.HasExited || p.Id <= 4 || p.Id == _ownPid) continue;
                    if (previousPids.Contains(p.Id)) continue;
                    if (_systemProcesses.Contains(p.ProcessName)) continue;
                    if (_boostedProcesses.ContainsKey(p.Id)) continue;
                    if (_antiCheatActive) continue;

                    ApplyStartupBoost(p.Id, p.ProcessName);
                }
                catch { }
            }
        }

        private void ApplyStartupBoost(int pid, string processName)
        {
            try
            {
                // DEBOUNCE POR NOME: evita boost flood quando um app abre múltiplos filhos
                // Ex: msedge abre 10+ processos filhos em <1s — sem debounce, CPU vai a 100%
                var now = DateTime.Now;
                if (_lastBoostByName.TryGetValue(processName, out var lastBoost) &&
                    (now - lastBoost).TotalSeconds < BoostDebounceSeconds)
                {
                    return; // Já aplicamos boost para este nome recentemente
                }
                _lastBoostByName[processName] = now;

                // FIX: Process.GetProcessById lança ArgumentException se PID não existe.
                // Processos podem encerrar entre a detecção e esta chamada.
                Process p;
                try { p = Process.GetProcessById(pid); }
                catch (ArgumentException) { return; } // processo já encerrou
                if (p.HasExited) return;

                var originalPriority = ProcessPriorityClass.Normal;
                try { originalPriority = p.PriorityClass; } catch { }

                // 1. Elevar prioridade de CPU temporariamente
                try { p.PriorityClass = ProcessPriorityClass.AboveNormal; } catch { }

                // 2. Habilitar PriorityBoost para scheduling mais agressivo
                try { p.PriorityBoostEnabled = true; } catch { }

                // 3. Elevar prioridade de I/O para acelerar leitura de disco
                try
                {
                    IntPtr hProcIo = OpenProcess(0x0200 | 0x0400, false, pid);
                    if (hProcIo != IntPtr.Zero)
                    {
                        try
                        {
                            GamerNativeMethods.SetProcessIoPriority(hProcIo, GamerNativeMethods.IoPriorityLevel.IoPriorityHigh, out _);
                        }
                        finally { CloseHandle(hProcIo); }
                    }
                }
                catch { }

                // 4. Desabilitar EcoQoS durante startup
                try
                {
                    IntPtr hProcess = OpenProcess(0x0200 | 0x0400, false, pid);
                    if (hProcess != IntPtr.Zero)
                    {
                        try
                        {
                            var state = new PROCESS_POWER_THROTTLING_STATE
                            {
                                Version = 1,
                                ControlMask = 0x01,
                                StateMask = 0x00 // Disable throttling
                            };
                            SetProcessInformation(hProcess, 4, ref state, Marshal.SizeOf(state));
                        }
                        finally { CloseHandle(hProcess); }
                    }
                }
                catch { }

                // 5. Elevar prioridade de memória
                // CORREÇÃO: infoClass era 0x00 (ProcessMemoryExhaustionInfo) — ERRADO!
                // O correto é 1 (ProcessMemoryPriority). Passar struct errado causa corrupção de heap.
                try
                {
                    IntPtr hProcess = OpenProcess(0x0200 | 0x0400, false, pid);
                    if (hProcess != IntPtr.Zero)
                    {
                        try
                        {
                            var memInfo = new MEMORY_PRIORITY_INFORMATION { MemoryPriority = 5 }; // High
                            SetProcessInformation(hProcess, 1, ref memInfo, Marshal.SizeOf(memInfo));
                        }
                        finally { CloseHandle(hProcess); }
                    }
                }
                catch { }

                _boostedProcesses[pid] = new BoostEntry
                {
                    Pid = pid,
                    Name = processName,
                    BoostAppliedAt = DateTime.Now,
                    OriginalPriority = originalPriority
                };

                _logger.LogInfo($"[DSL] New process detected: {processName} (PID: {pid})");
                _logger.LogInfo($"[DSL] Startup boost applied — CPU: AboveNormal, IO: High, EcoQoS: Off");
            }
            catch { }
        }

        private void CleanupExpiredBoosts(object? state)
        {
            var now = DateTime.Now;
            var expired = _boostedProcesses.Values
                .Where(b => !b.Restored && (now - b.BoostAppliedAt).TotalSeconds > BoostDurationSeconds)
                .ToList();

            foreach (var entry in expired)
            {
                try
                {
                    var p = Process.GetProcessById(entry.Pid);
                    if (!p.HasExited)
                    {
                        // Restaurar prioridade original
                        try { p.PriorityClass = entry.OriginalPriority; } catch { }

                        // Restaurar I/O para Normal
                        try
                        {
                            IntPtr hProcIo = OpenProcess(0x0200 | 0x0400, false, entry.Pid);
                            if (hProcIo != IntPtr.Zero)
                            {
                                try
                                {
                                    GamerNativeMethods.SetProcessIoPriority(hProcIo, GamerNativeMethods.IoPriorityLevel.IoPriorityNormal, out _);
                                }
                                finally { CloseHandle(hProcIo); }
                            }
                        }
                        catch { }

                        _logger.LogInfo($"[DSL] Startup boost removed: {entry.Name} (PID: {entry.Pid}) after {BoostDurationSeconds}s");
                    }
                    entry.Restored = true;
                }
                catch
                {
                    entry.Restored = true; // Processo já terminou
                }
            }

            // Limpar entradas antigas (> 60s)
            var toRemove = _boostedProcesses.Where(kv => 
                (now - kv.Value.BoostAppliedAt).TotalSeconds > 60).Select(kv => kv.Key).ToList();
            foreach (var key in toRemove)
                _boostedProcesses.TryRemove(key, out _);
        }

        /// <summary>
        /// Número de processos atualmente com boost ativo
        /// </summary>
        public int ActiveBoostCount => _boostedProcesses.Count(kv => !kv.Value.Restored);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _processWatcher?.Stop(); } catch { }
            _processWatcher?.Dispose();
            _cleanupTimer?.Dispose();

            // Restaurar todos os boosts pendentes
            foreach (var entry in _boostedProcesses.Values.Where(b => !b.Restored))
            {
                try
                {
                    var p = Process.GetProcessById(entry.Pid);
                    if (!p.HasExited)
                    {
                        p.PriorityClass = entry.OriginalPriority;
                        IntPtr hProcIo = OpenProcess(0x0200 | 0x0400, false, entry.Pid);
                        if (hProcIo != IntPtr.Zero)
                        {
                            try
                            {
                                GamerNativeMethods.SetProcessIoPriority(hProcIo, GamerNativeMethods.IoPriorityLevel.IoPriorityNormal, out _);
                            }
                            finally { CloseHandle(hProcIo); }
                        }
                    }
                }
                catch { }
            }
        }

        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")] private static extern bool SetProcessInformation(IntPtr h, int infoClass, ref PROCESS_POWER_THROTTLING_STATE state, int size);
        [DllImport("kernel32.dll")] private static extern bool SetProcessInformation(IntPtr h, int infoClass, ref MEMORY_PRIORITY_INFORMATION state, int size);

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_POWER_THROTTLING_STATE { public uint Version; public uint ControlMask; public uint StateMask; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORY_PRIORITY_INFORMATION { public uint MemoryPriority; }
    }
}
