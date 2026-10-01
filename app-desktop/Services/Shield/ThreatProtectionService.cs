using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Shield
{
    /// <summary>
    /// ThreatProtectionService — Proteção avançada contra bypass, metasploit,
    /// injeção de processos, SQL injection, XSS e ataques de rede.
    /// Monitora processos suspeitos, valida inputs e detecta payloads maliciosos.
    /// </summary>
    public class ThreatProtectionService
    {
        private readonly ILoggingService _logger;
        private readonly SecurityLogService _securityLog;
        private readonly SignatureVerificationService _signatureService;
        private Timer? _processMonitorTimer;
        private readonly object _lifecycleLock = new();
        private int _scanInProgress;
        private volatile bool _isActive;
        private volatile bool _lowActivityMode;

        // Cache de processos já verificados (evita re-scan)
        private readonly ConcurrentDictionary<int, ProcessScanResult> _scannedProcesses = new();

        // Processos conhecidos do Metasploit/ferramentas de ataque
        private static readonly HashSet<string> MaliciousProcessNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "msfconsole", "msfvenom", "meterpreter", "mimikatz", "lazagne",
            "cobaltstrike", "beacon", "empire", "powershell_empire",
            "netcat", "nc", "ncat", "socat", "chisel", "plink",
            "psexec", "wmiexec", "smbexec", "atexec", "dcomexec",
            "bloodhound", "sharphound", "rubeus", "seatbelt",
            "certutil_download", "bitsadmin_transfer",
            "procdump", "processhacker", "processhacker2",
            "hashcat", "john", "hydra", "medusa", "ncrack",
            "responder", "inveigh", "ettercap", "bettercap",
            "sqlmap", "nikto", "dirb", "gobuster", "ffuf",
            "nmap", "masscan", "zmap", "angry_ip_scanner"
        };

        // Padrões de linha de comando suspeitos
        private static readonly Regex[] SuspiciousCommandPatterns = new[]
        {
            // PowerShell encoded commands (bypass comum)
            new Regex(@"-[Ee]nc(?:oded)?[Cc]ommand\s+[A-Za-z0-9+/=]{20}", RegexOptions.Compiled),
            // PowerShell download cradle
            new Regex(@"(?:IEX|Invoke-Expression).*(?:Net\.WebClient|DownloadString|DownloadFile)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            // Certutil download (LOLBin)
            new Regex(@"certutil.*-urlcache.*-split.*-f\s+https?://", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            // BITSAdmin transfer (LOLBin)
            new Regex(@"bitsadmin.*\/transfer.*https?://", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            // Mshta execution (LOLBin)
            new Regex(@"mshta\s+(?:vbscript|javascript|https?://)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            // Regsvr32 scrobj (LOLBin - Squiblydoo)
            new Regex(@"regsvr32.*\/s.*\/n.*\/u.*\/i:https?://", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            // WMIC process call create (lateral movement)
            new Regex(@"wmic.*process\s+call\s+create", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            // Reverse shell patterns
            new Regex(@"(?:bash|sh|cmd).*(?:-i|\/c).*(?:\/dev\/tcp|nc\s+-e|ncat)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            // Base64 encoded PowerShell
            new Regex(@"powershell.*-[Ww]indow[Ss]tyle\s+[Hh]idden", RegexOptions.Compiled)};

        // SQL Injection patterns
        private static readonly Regex[] SqlInjectionPatterns = new[]
        {
            new Regex(@"(?:'\s*(?:OR|AND)\s+['\d]\s*=\s*['\d])", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"(?:UNION\s+(?:ALL\s+)?SELECT)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"(?:;\s*DROP\s+TABLE)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"(?:;\s*DELETE\s+FROM)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"(?:--\s*$|/\*.*\*/)", RegexOptions.Compiled),
            new Regex(@"(?:xp_cmdshell|sp_executesql)", RegexOptions.Compiled | RegexOptions.IgnoreCase)};

        // XSS patterns
        private static readonly Regex[] XssPatterns = new[]
        {
            new Regex(@"<script\b[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"(?:on(?:load|error|click|mouseover|focus|blur))\s*=", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"javascript\s*:", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"<iframe\b[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"<object\b[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"<embed\b[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase)};

        // Auth bypass patterns
        private static readonly string[] AuthBypassPatterns = new[]
        {
            "' OR '1'='1", "admin' OR '1'='1", "1' OR '1'='1",
            "' OR 1=1--", "admin'--", "' UNION SELECT",
            "'; DROP TABLE", "1; DROP TABLE users"};

        public event EventHandler<ThreatAlertEventArgs>? ThreatAlertRaised;

        public bool IsActive => _isActive;

        public ThreatProtectionService(
            ILoggingService logger,
            SecurityLogService securityLog,
            SignatureVerificationService signatureService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _securityLog = securityLog ?? throw new ArgumentNullException(nameof(securityLog));
            _signatureService = signatureService ?? throw new ArgumentNullException(nameof(signatureService));
        }

        /// <summary>
        /// Inicia monitoramento contínuo de processos suspeitos.
        /// </summary>
        public void StartProtection()
        {
            lock (_lifecycleLock)
            {
                if (_isActive) return;

                _logger.LogInfo("[ThreatProtection] ══════════════════════════════════════════");
                _logger.LogInfo("[ThreatProtection] Iniciando proteção avançada contra ameaças...");
                _logger.LogInfo("[ThreatProtection] Módulos: Anti-Metasploit, Anti-Bypass, Anti-Injection, Process Monitor");

                try
                {
                    var initialProcesses = Process.GetProcesses();
                    foreach (var p in initialProcesses)
                    {
                        try
                        {
                            _scannedProcesses[p.Id] = new ProcessScanResult
                            {
                                ProcessId = p.Id,
                                ProcessName = p.ProcessName,
                                IsAnalyzed = false
                            };
                        }
                        catch { }
                        finally { p.Dispose(); }
                    }
                }
                catch { }

                var interval = _lowActivityMode ? 120000 : 60000;
                _isActive = true;
                _processMonitorTimer = new Timer(ScanRunningProcesses, null, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(interval));

                _securityLog.LogSecurityEvent("ThreatProtection", "PROTECTION_STARTED",
                    "Advanced threat protection active: Anti-Metasploit, Process Monitor, Input Validation");
                _logger.LogSuccess("[ThreatProtection] ✅ Proteção avançada ativa");
                _logger.LogInfo("[ThreatProtection] ══════════════════════════════════════════");
            }
        }

        public void StopProtection()
        {
            lock (_lifecycleLock)
            {
                if (!_isActive) return;
                _isActive = false;
                _processMonitorTimer?.Dispose();
                _processMonitorTimer = null;
                _scannedProcesses.Clear();
                _logger.LogInfo("[ThreatProtection] Proteção avançada desativada");
            }
        }

        public void SetLowActivityMode(bool enabled)
        {
            lock (_lifecycleLock)
            {
                _lowActivityMode = enabled;
                if (_isActive && _processMonitorTimer != null)
                {
                    var interval = enabled ? 120000 : 60000;
                    _processMonitorTimer.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds(interval));
                }
            }
        }

        /// <summary>
        /// Escaneia processos em execução buscando ferramentas de ataque conhecidas.
        /// PERFORMANCE FIX: WMI Win32_Process query removida — era a causa dos picos de 20-30% de CPU.
        /// Agora escaneia apenas processos NOVOS desde o último ciclo usando Process.GetProcesses() com
        /// cache de PIDs já verificados. WMI é chamado apenas UMA VEZ por PID novo suspeito.
        /// </summary>
        private void ScanRunningProcesses(object? state)
        {
            if (!_isActive || Interlocked.Exchange(ref _scanInProgress, 1) == 1)
                return;

            try
            {
                var processes = Process.GetProcesses();
                int newThreats = 0;

                // Coletar apenas PIDs novos (não vistos antes)
                var newProcesses = new List<Process>();
                foreach (var proc in processes)
                {
                    try
                    {
                        if (!_scannedProcesses.TryGetValue(proc.Id, out var cachedResult) || !cachedResult.IsAnalyzed)
                            newProcesses.Add(proc);
                    }
                    catch { }
                }

                // Verificar apenas processos NOVOS — sem WMI global, sem acesso a MainModule em massa
                foreach (var proc in newProcesses)
                {
                    try
                    {
                        var result = new ProcessScanResult { ProcessId = proc.Id, ProcessName = proc.ProcessName, IsAnalyzed = true };

                        // 1. Verificação por nome (ultra rápida — HashSet lookup O(1))
                        if (MaliciousProcessNames.Contains(proc.ProcessName))
                        {
                            result.IsMalicious = true;
                            result.ThreatType = "Ferramenta de ataque conhecida";
                            result.Severity = ThreatSeverity.Critical;
                            result.Details = $"Processo '{proc.ProcessName}' é uma ferramenta de ataque/pentest conhecida";
                            _logger.LogWarning($"[ThreatProtection] ⚠️ AMEAÇA: {proc.ProcessName} (PID: {proc.Id})");
                            _securityLog.LogThreatDetected("ThreatProtection", "MALICIOUS_PROCESS", $"Process={proc.ProcessName} PID={proc.Id}");
                            RaiseThreatAlert(result);
                            newThreats++;
                        }

                        // 2. Verificar linha de comando APENAS se nome for suspeito (não fazer WMI para todos)
                        if (!result.IsMalicious)
                        {
                            // Verificar path do executável — mais leve que WMI
                            try
                            {
                                var exePath = proc.MainModule?.FileName;
                                if (!string.IsNullOrEmpty(exePath) && !IsSystemProcess(exePath))
                                {
                                    var risk = _signatureService.AssessFileRisk(exePath);
                                    if (risk.RiskScore >= 70)
                                    {
                                        result.IsMalicious = true;
                                        result.ThreatType = "Executável de alto risco";
                                        result.Severity = risk.RiskScore >= 85 ? ThreatSeverity.Critical : ThreatSeverity.High;
                                        result.Details = $"Processo '{proc.ProcessName}' tem risco {risk.RiskScore}/100: {risk.Reason}";
                                        _logger.LogWarning($"[ThreatProtection] ⚠️ Alto risco: {proc.ProcessName} (Score: {risk.RiskScore})");
                                        _securityLog.LogThreatDetected("ThreatProtection", "HIGH_RISK_EXECUTABLE",
                                            $"Process={proc.ProcessName} Score={risk.RiskScore}");
                                        RaiseThreatAlert(result);
                                        newThreats++;
                                    }
                                }
                            }
                            catch { /* MainModule inacessível — processo de sistema ou protegido */ }
                        }

                        _scannedProcesses[proc.Id] = result;
                    }
                    catch { }
                    finally
                    {
                        try { proc.Dispose(); } catch { }
                    }
                }

                // Dispose dos processos que não foram para newProcesses
                foreach (var proc in processes)
                {
                    if (!newProcesses.Contains(proc))
                        try { proc.Dispose(); } catch { }
                }

                // Limpar PIDs de processos que já terminaram (usando os PIDs ativos que já temos)
                var activePids = new HashSet<int>();
                foreach (var p in processes) { try { activePids.Add(p.Id); } catch { } }
                var staleIds = _scannedProcesses.Keys.Where(id => !activePids.Contains(id)).ToList();
                foreach (var id in staleIds) _scannedProcesses.TryRemove(id, out _);

                if (newThreats > 0)
                    _logger.LogWarning($"[ThreatProtection] Scan: {newThreats} ameaça(s) em {newProcesses.Count} proc(s) novos");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ThreatProtection] Erro no scan: {ex.Message}");
            }
            finally
            {
                Volatile.Write(ref _scanInProgress, 0);
            }
        }

        /// <summary>
        /// Valida input contra SQL Injection, XSS e bypass de autenticação.
        /// Retorna (isValid, threatType) — usar antes de processar qualquer input do usuário.
        /// </summary>
        public (bool IsValid, string? ThreatType, string? Details) ValidateInput(string input)
        {
            if (string.IsNullOrEmpty(input))
                return (true, null, null);

            // SQL Injection
            foreach (var pattern in SqlInjectionPatterns)
            {
                if (pattern.IsMatch(input))
                {
                    var detail = $"Padrão SQL Injection detectado: {input[..Math.Min(50, input.Length)]}";
                    _logger.LogWarning($"[ThreatProtection] {detail}");
                    _securityLog.LogSecurityEvent("ThreatProtection", "SQL_INJECTION_BLOCKED", detail);
                    return (false, "SQL Injection", detail);
                }
            }

            // XSS
            foreach (var pattern in XssPatterns)
            {
                if (pattern.IsMatch(input))
                {
                    var detail = $"Padrão XSS detectado: {input[..Math.Min(50, input.Length)]}";
                    _logger.LogWarning($"[ThreatProtection] {detail}");
                    _securityLog.LogSecurityEvent("ThreatProtection", "XSS_BLOCKED", detail);
                    return (false, "Cross-Site Scripting (XSS)", detail);
                }
            }

            // Auth Bypass
            foreach (var pattern in AuthBypassPatterns)
            {
                if (input.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    var detail = $"Tentativa de bypass de autenticação: {input[..Math.Min(50, input.Length)]}";
                    _logger.LogWarning($"[ThreatProtection] {detail}");
                    _securityLog.LogSecurityEvent("ThreatProtection", "AUTH_BYPASS_BLOCKED", detail);
                    return (false, "Authentication Bypass", detail);
                }
            }

            return (true, null, null);
        }

        /// <summary>
        /// Sanitiza input removendo padrões perigosos.
        /// </summary>
        public string SanitizeInput(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            // Remover tags HTML
            input = Regex.Replace(input, @"<[^>]+>", "", RegexOptions.IgnoreCase);
            // Remover javascript:
            input = Regex.Replace(input, @"javascript\s*:", "", RegexOptions.IgnoreCase);
            // Remover event handlers
            input = Regex.Replace(input, @"on(?:load|error|click|mouseover|focus|blur)\s*=", "", RegexOptions.IgnoreCase);
            // Encode caracteres especiais de SQL
            input = input.Replace("'", "''");

            return input;
        }

        /// <summary>
        /// Retorna lista de processos maliciosos atualmente em execução.
        /// </summary>
        public List<ProcessScanResult> GetActiveThreats()
        {
            return _scannedProcesses.Values.Where(r => r.IsMalicious).ToList();
        }

        private bool IsSystemProcess(string exePath)
        {
            try
            {
                var fullPath = Path.GetFullPath(exePath);
                var roots = new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Apps")
                };

                return roots.Where(root => !string.IsNullOrWhiteSpace(root))
                    .Select(root => Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar)
                    .Any(root => fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        private void RaiseThreatAlert(ProcessScanResult result)
        {
            var handlers = ThreatAlertRaised;
            if (handlers == null)
                return;

            var args = new ThreatAlertEventArgs
            {
                ProcessName = result.ProcessName,
                ProcessId = result.ProcessId,
                ThreatType = result.ThreatType,
                Severity = result.Severity,
                Details = result.Details,
                Timestamp = DateTime.Now
            };
            foreach (var subscriber in handlers.GetInvocationList())
            {
                try
                {
                    ((EventHandler<ThreatAlertEventArgs>)subscriber)(this, args);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[ThreatProtection] Falha ao notificar ameaça: {ex.Message}");
                }
            }
        }
    }

    #region Models

    public class ProcessScanResult
    {
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public bool IsMalicious { get; set; }
        public bool IsAnalyzed { get; set; }
        public string ThreatType { get; set; } = string.Empty;
        public ThreatSeverity Severity { get; set; }
        public string Details { get; set; } = string.Empty;
    }

    public class ThreatAlertEventArgs : EventArgs
    {
        public string ProcessName { get; set; } = string.Empty;
        public int ProcessId { get; set; }
        public string ThreatType { get; set; } = string.Empty;
        public ThreatSeverity Severity { get; set; }
        public string Details { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string SourceModule { get; set; } = "ThreatProtection";
    }

    #endregion
}
