using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;
using System.Windows;
using VoltrisOptimizer.UI.Controls;

namespace VoltrisOptimizer.Services.Shield.Advanced
{
    /// <summary>
    /// Monitoramento comporteamental em tempo real.
    /// Detecta criação de processos suspeitos e atividades em locais sensíveis.
    /// </summary>
    public class BehavioralMonitor : IDisposable
    {
        private readonly ILoggingService _logger;
        private readonly Dictionary<int, ProcessBehavior> _processBehaviors = new();
        private ManagementEventWatcher? _processWatcher;
        private List<FileSystemWatcher> _fileWatchers = new();
        private bool _disposed;
        private readonly object _lifecycleLock = new();
        private readonly string _voltrisPath;

        public event EventHandler<BehavioralThreatEventArgs>? ThreatDetected;

        public BehavioralMonitor(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _voltrisPath = AppDomain.CurrentDomain.BaseDirectory.ToLowerInvariant();
        }

        private AdvancedStaticAnalyzer? _staticAnalyzer;
        private HeuristicAnalyzer? _heuristicAnalyzer;

        public void SetAnalyzers(AdvancedStaticAnalyzer staticAnalyzer, HeuristicAnalyzer heuristicAnalyzer)
        {
            _staticAnalyzer = staticAnalyzer;
            _heuristicAnalyzer = heuristicAnalyzer;
        }

        public void StartMonitoring()
        {
            lock (_lifecycleLock)
            {
                if (_disposed || _processMonitorTimer != null) return;

                try
                {
                    foreach (var watcher in _fileWatchers)
                    {
                        watcher.EnableRaisingEvents = false;
                        watcher.Dispose();
                    }
                    _fileWatchers.Clear();

                    var initialProcesses = Process.GetProcesses();
                    lock (_knownPids)
                    {
                        _knownPids.Clear();
                        foreach (var p in initialProcesses)
                        {
                            try { _knownPids.Add(p.Id); } catch { }
                            finally { p.Dispose(); }
                        }
                    }

                    _processMonitorTimer = new System.Threading.Timer(MonitorProcessesLoop, null, 0, 30000);
                    MonitorSensitiveLocations();
                    _logger.LogInfo("[BehavioralMonitor] Monitoramento inteligente em tempo real iniciado (Polling Leve Otimizado - Ignorando processos legados)");
                }
                catch (Exception ex)
                {
                    _logger.LogError("[BehavioralMonitor] Erro ao iniciar monitor comportamental", ex);
                }
            }
        }

        private System.Threading.Timer? _processMonitorTimer;
        private readonly HashSet<int> _knownPids = new HashSet<int>();

        private void MonitorProcessesLoop(object? state)
        {
            try
            {
                var processes = Process.GetProcesses();
                var currentPids = new HashSet<int>();

                foreach (var proc in processes)
                {
                    try
                    {
                        currentPids.Add(proc.Id);
                        // Processo novo? Analisar
                        if (!_knownPids.Contains(proc.Id))
                        {
                            AnalyzeNewProcess(proc);
                        }
                    }
                    catch { }
                    finally { proc.Dispose(); }
                }

                lock (_knownPids)
                {
                    _knownPids.Clear();
                    foreach(var id in currentPids) _knownPids.Add(id);
                }
            }
            catch (Exception ex)
            {
                // Silence timeout ou erro isolado
            }
        }

        private void AnalyzeNewProcess(Process proc)
        {
            try
            {
                string processName = proc.ProcessName;
                int processId = proc.Id;
                
                // ParentID não é natural no .NET, usaremos mock seguro 0 caso precise pro método antigo de heurística
                int parentId = 0; 
                
                // 🔧 WHITELIST PROFISSIONAL: Processos legítimos conhecidos que não devem ser analisados
                var legitimateProcesses = new HashSet<string>
                {
                    // Gaming e Emuladores
                    "cs2.exe", "csgo.exe", "valorant.exe", "fortniteclient-win64-shipping.exe",
                    "dota2.exe", "leagueclient.exe", "lol.exe", "minecraft.exe", "gta5.exe",
                    "pcsx2.exe", "duckstation.exe", "rpcs3.exe", "cemu.exe", "yuzu.exe", "ryujinx.exe",
                    "project64.exe", "mupen64plus.exe", "dolphin.exe", "retroarch.exe",
                    "opl_manager.exe", "x86launcher.exe", // 🔧 FALSO POSITIVO COMUM
                    
                    // Launchers
                    "steam.exe", "epicgameslauncher.exe", "origin.exe", "uplay.exe",
                    "goggalaxy.exe", "battlenet.exe",
                    
                    // Desenvolvimento
                    "visualstudio.exe", "devenv.exe", "code.exe", "vscode.exe",
                    "node.exe", "python.exe", "java.exe", "javaw.exe", "git.exe",
                    "git-bash.exe", "docker.exe", "docker-desktop.exe",
                    
                    // Sistema e Hardware
                    "cpu-z.exe", "gpu-z.exe", "hwmonitor.exe", "aida64.exe",
                    "crystaldiskinfo.exe", "malwarebytes.exe", "ccleaner.exe",
                    "defraggler.exe", "recuva.exe", "afterburner.exe",
                    "precisionx.exe", "nvidia.exe", "amd.exe", "intel.exe",
                    "razer.exe", "corsair.exe", "logitech.exe",
                    
                    // Streaming e Comunicação
                    "obs.exe", "obs-studio.exe", "streamlabs.exe", "discord.exe",
                    "teams.exe", "slack.exe", "zoom.exe",
                    
                    // Navegadores
                    "chrome.exe", "firefox.exe", "msedge.exe", "opera.exe", "brave.exe",
                    
                    // Utilitários
                    "7zfm.exe", "winrar.exe", "notepad++.exe", "vlc.exe",
                    "mpc-hc.exe", "potplayer.exe",
                    
                    // Remote Desktop
                    "teamviewer.exe", "anydesk.exe", "rustdesk.exe", "splashtop.exe",
                    "parsec.exe", "sunshine.exe",
                    
                    // Rede e Ferramentas
                    "wireshark.exe", "nmap.exe", "putty.exe", "filezilla.exe", "winscp.exe",
                    
                    // Virtualização
                    "vmware.exe", "virtualbox.exe", "xming.exe", "vcxsrv.exe",
                    
                    // Shells
                    "powershell.exe", "cmd.exe", "wsl.exe", "ubuntu.exe", "debian.exe",
                    
                    // Instaladores
                    "msiexec.exe", "update.exe"
                };
                
                // Verificar whitelist por nome exato
                if (legitimateProcesses.Contains(processName.ToLowerInvariant()))
                {
                    _logger?.LogInfo($"[BehavioralMonitor] 🛡️ Processo legítimo detectado: {processName} - ignorando análise");
                    return;
                }
                
                // Verificar whitelist por prefixo (para compatibilidade)
                if (processName.StartsWith("cs", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("valorant", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("league", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("lol", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("dota", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("fortnite", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("minecraft", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("gta", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("steam", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("epic", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("origin", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("uplay", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("battle", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("discord", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("obs", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("teamviewer", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("anydesk", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("chrome", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("firefox", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("msedge", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("opera", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("brave", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("node", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("python", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("java", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("javaw", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("git", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("docker", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("visualstudio", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("devenv", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("code", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("vscode", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("powershell", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("cmd", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("wsl", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("ubuntu", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("debian", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("cpu-z", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("gpu-z", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("hwmonitor", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("aida64", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("crystaldiskinfo", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("malwarebytes", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("ccleaner", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("defraggler", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("recuva", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("afterburner", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("precisionx", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("nvidia", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("amd", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("intel", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("razer", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("corsair", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("logitech", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("7z", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("winrar", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("notepad++", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("vlc", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("mpc-hc", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("potplayer", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("teamviewer", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("anydesk", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("rustdesk", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("splashtop", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("parsec", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("sunshine", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("wireshark", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("nmap", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("putty", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("filezilla", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("winscp", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("vmware", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("virtualbox", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("xming", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("vcxsrv", StringComparison.OrdinalIgnoreCase) ||
                    (processName.StartsWith("msi", StringComparison.OrdinalIgnoreCase) && processName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) ||
                    processName.Equals("update.exe", StringComparison.OrdinalIgnoreCase))
                {
                    _logger?.LogInfo($"[BehavioralMonitor] 🛡️ Programa/Jogo confiável detectado: {processName} - ignorando análise");
                    return;
                }

                if (processId == Environment.ProcessId)
                    return;
                    
                // ✅ WHITELIST DE JOGOS CONFIÁVEIS
                if (processName.Equals("cs2.exe", StringComparison.OrdinalIgnoreCase) ||
                    processName.Equals("csgo.exe", StringComparison.OrdinalIgnoreCase) ||
                    processName.Equals("valorant.exe", StringComparison.OrdinalIgnoreCase) ||
                    processName.Equals("fortniteclient-win64-shipping.exe", StringComparison.OrdinalIgnoreCase) ||
                    processName.Equals("dota2.exe", StringComparison.OrdinalIgnoreCase) ||
                    processName.Equals("leagueclient.exe", StringComparison.OrdinalIgnoreCase) ||
                    processName.Equals("lol.exe", StringComparison.OrdinalIgnoreCase) ||
                    processName.Equals("javaw.exe", StringComparison.OrdinalIgnoreCase) ||
                    processName.Equals("minecraft.exe", StringComparison.OrdinalIgnoreCase) ||
                    processName.Equals("gta5.exe", StringComparison.OrdinalIgnoreCase) ||
                    processName.Equals("rdr2.exe", StringComparison.OrdinalIgnoreCase) ||
                    processName.Equals("r5apex.exe", StringComparison.OrdinalIgnoreCase) ||
                    processName.Equals("overwatch.exe", StringComparison.OrdinalIgnoreCase) ||
                    processName.Equals("tslgame.exe", StringComparison.OrdinalIgnoreCase) ||
                    processName.Equals("witcher3.exe", StringComparison.OrdinalIgnoreCase) ||
                    processName.Equals("cyberpunk2077.exe", StringComparison.OrdinalIgnoreCase) ||
                    processName.Equals("destiny2.exe", StringComparison.OrdinalIgnoreCase) ||
                    processName.Equals("adb.exe", StringComparison.OrdinalIgnoreCase) ||
                    processName.Equals("overwolfupdater.exe", StringComparison.OrdinalIgnoreCase) ||
                    processName.Equals("wa_3rd_party_host_32.exe", StringComparison.OrdinalIgnoreCase) ||
                    processName.Equals("SSDBooster.exe", StringComparison.OrdinalIgnoreCase) ||
                    processName.Equals("Avira.Spotlight.FallbackUpdater.exe", StringComparison.OrdinalIgnoreCase) ||
                    (processName.StartsWith("msi", StringComparison.OrdinalIgnoreCase) && processName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) ||
                    processName.Equals("update.exe", StringComparison.OrdinalIgnoreCase))
                {
                    _logger?.LogInfo($"[BehavioralMonitor] 🛡️ Programa/Jogo confiável detectado: {processName} - ignorando análise");
                    return;
                }

                // Tentar obter caminho do executável para análise profunda
                string? exePath = null;
                try
                {
                    exePath = proc.MainModule?.FileName;
                }
                catch { /* Processo de sistema ou recusou acesso */ }

                bool isMalicious = false;
                string threatDetails = "";

                // 1. ANÁLISE INTELIGENTE (ESTÁTICA + HEURÍSTICA EM TEMPO REAL)
                if (!string.IsNullOrEmpty(exePath) && _staticAnalyzer != null && _heuristicAnalyzer != null)
                {
                    var staticResult = _staticAnalyzer.AnalyzeFile(exePath);
                    if (staticResult.IsMalicious)
                    {
                        isMalicious = true;
                        threatDetails = staticResult.Threats.First().Details;
                    }
                    else
                    {
                        var score = _heuristicAnalyzer.CalculateMaliciousScore(exePath);
                        if (score > 0.7)
                        {
                            isMalicious = true;
                            threatDetails = $"Score heurístico crítico ({score:P0})";
                        }
                    }
                }

                // 2. ANÁLISE DE COMPORTAMENTO INTELIGENTE (Combate ao cmd.exe false positive)
                if (!isMalicious && IsSuspiciousProcessName(processName))
                {
                    string parentName = GetProcessName(parentId);
                    if (!IsTrustedParent(parentName))
                    {
                        var location = exePath != null ? Path.GetDirectoryName(exePath).ToLower() : "";
                        if (location.Contains("temp") || location.Contains("downloads") || location.Contains("appdata"))
                        {
                            isMalicious = true;
                            threatDetails = $"Shell ({processName}) executado de local não-padrão por pai suspeito ({parentName})";
                        }
                    }
                }

                if (isMalicious)
                {
                    _logger.LogWarning($"[BehavioralMonitor] AMEAÇA DETECTADA: {processName} (PID: {processId}) - {threatDetails}");
                    NotifyThreat(processName, threatDetails);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("[BehavioralMonitor] Erro no monitor inteligente", ex);
            }
        }

        private string GetProcessName(int pid)
        {
            try { return Process.GetProcessById(pid).ProcessName; }
            catch { return "unknown"; }
        }

        private bool IsTrustedParent(string name)
        {
            var trusted = new[] 
            { 
                "explorer", "services", "winlogon", "Voltris", "taskmgr", "devenv",
                // ✅ WHITELIST: Ferramentas de build do Visual Studio / MSBuild
                "msbuild", "cmake", "cl", "link", "tracker", "vctip",
                "devenv", "vstest", "dotnet", "nuget"
            };
            return trusted.Any(t => name.Contains(t, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsSuspiciousProcessName(string processName)
        {
            var suspicious = new[] { "powershell", "cmd.exe", "wscript", "cscript", "mshta", "regsvr32" };
            return suspicious.Any(s => processName.Contains(s, StringComparison.OrdinalIgnoreCase));
        }

        private void MonitorSensitiveLocations()
        {
            var locations = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Roaming", "Microsoft", "Windows", "Start Menu", "Programs", "Startup"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Temp")
            };

            foreach (var path in locations)
            {
                try
                {
                    if (!Directory.Exists(path)) continue;

                    var watcher = new FileSystemWatcher(path)
                    {
                        EnableRaisingEvents = true,
                        Filter = "*.exe"
                    };

                    watcher.Created += (s, e) =>
                    {
                        if (e.FullPath.ToLowerInvariant().Contains(_voltrisPath)) return;

                        if (_staticAnalyzer != null)
                        {
                            var res = _staticAnalyzer.AnalyzeFile(e.FullPath);
                            if (res.IsMalicious)
                            {
                                _logger.LogWarning($"[BehavioralMonitor] Binário malicioso criado em local sensível: {e.FullPath}");
                                NotifyThreat(e.Name ?? "Arquivo", $"Malware detectado: {res.Threats.First().Details}");
                            }
                        }
                    };

                    _fileWatchers.Add(watcher);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[BehavioralMonitor] Erro ao observar {path}: {ex.Message}");
                }
            }
        }

        private void NotifyThreat(string processName, string message)
        {
            ThreatDetected?.Invoke(this, new BehavioralThreatEventArgs(processName, message));
            
            try 
            {
                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    new ToastService().Show(
                        string.Format(LocalizationService.Instance.GetString("ThreatDetected"), processName),
                        message);
                }));
            }
            catch { }
        }

        public void StopMonitoring()
        {
            lock (_lifecycleLock)
            {
                try
                {
                    _processMonitorTimer?.Dispose();
                    _processMonitorTimer = null;
                    foreach (var watcher in _fileWatchers)
                    {
                        watcher.EnableRaisingEvents = false;
                        watcher.Dispose();
                    }
                    _fileWatchers.Clear();
                    _logger.LogInfo("[BehavioralMonitor] Monitoramento parado");
                }
                catch (Exception ex)
                {
                    _logger.LogDebug($"[BehavioralMonitor] Erro ao parar: {ex.Message}");
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            StopMonitoring();

            _processMonitorTimer?.Dispose();

            foreach (var watcher in _fileWatchers)
            {
                watcher.Dispose();
            }
            _fileWatchers.Clear();
            
            GC.SuppressFinalize(this);
        }
    }

    public class ProcessBehavior
    {
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
    }

    public class BehavioralThreatEventArgs : EventArgs
    {
        public string ProcessName { get; }
        public string Message { get; }

        public BehavioralThreatEventArgs(string name, string msg)
        {
            ProcessName = name;
            Message = msg;
        }
    }
}
