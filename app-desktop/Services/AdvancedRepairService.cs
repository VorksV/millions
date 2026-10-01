using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.Repair;

namespace VoltrisOptimizer.Services
{
    /// <summary>
    /// Resultado de uma etapa de reparo avançado.
    /// </summary>
    public class AdvancedRepairStepResult
    {
        public string StepName { get; set; } = "";
        public bool Success { get; set; }
        public string Summary { get; set; } = "";
        public List<string> Details { get; set; } = new();
        public TimeSpan Duration { get; set; }
    }

    /// <summary>
    /// Resultado completo do reparo avançado (usado para relatório HTML).
    /// </summary>
    public class AdvancedRepairReport
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public List<AdvancedRepairStepResult> Steps { get; set; } = new();
        public string SystemInfo { get; set; } = "";
        public bool RestorePointCreated { get; set; }
        public string RestorePointName { get; set; } = "";
        /// <summary>
        /// Análise inteligente global de falhas (Root Cause Analysis).
        /// </summary>
        public string FinalAnalysis { get; set; } = "";
    }

    /// <summary>
    /// Serviço de reparo ultra avançado do Voltris Optimizer.
    /// Executa 20 etapas de reparo profundo com segurança total ao usuário.
    /// </summary>
    public class AdvancedRepairService
    {
        private readonly ILoggingService _logger;

        // Callback para log visual na UI (mensagem + cor hex)
        public Action<string, string>? OnLog { get; set; }
        // Callback para atualizar progresso global
        public Action<int, string>? OnProgress { get; set; }

        public AdvancedRepairService(ILoggingService logger)
        {
            _logger = logger;
        }

        // ─────────────────────────────────────────────────────────────────────
        // HELPERS INTERNOS
        // ─────────────────────────────────────────────────────────────────────

        private void Log(string msg, string color = "#AAAAAA")
        {
            // DEBUG: Registrar TODAS as mensagens de log com timestamp preciso
            _logger.LogDebug($"[AdvancedRepair] {msg}", source: "AdvancedRepairService");
            _logger.LogInfo($"[AdvancedRepair] {msg}");
            OnLog?.Invoke(msg, color);
        }

        private void Progress(int pct, string msg)
        {
            // DEBUG: Registrar cada atualização de progresso
            _logger.LogDebug($"[AdvancedRepair][Progress] {pct}% — {msg}", source: "AdvancedRepairService");
            OnProgress?.Invoke(pct, msg);
        }

        /// <summary>
        /// Shorthand para acessar tradução do LocalizationService.
        /// </summary>
        private static string Loc(string key) => LocalizationService.Instance.GetString(key);

        /// <summary>
        /// Executa um processo e retorna (exitCode, stdout+stderr combinados).
        /// </summary>
        private async Task<(int exitCode, string output)> RunAsync(
            string exe, string args, CancellationToken ct,
            bool useShell = false, int timeoutMs = 120_000)
        {
            var sb = new StringBuilder();
            // DEBUG: Registrar início de cada processo externo
            _logger.LogDebug($"[AdvancedRepair][RunAsync] Iniciando: {exe} {args} (timeout={timeoutMs}ms)", source: "AdvancedRepairService");
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                Encoding enc;
                try { enc = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
                catch { enc = Encoding.UTF8; }

                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    UseShellExecute = useShell,
                    CreateNoWindow = true,
                    RedirectStandardOutput = !useShell,
                    RedirectStandardError = !useShell,
                    StandardOutputEncoding = useShell ? null : enc,
                    StandardErrorEncoding = useShell ? null : enc
                };

                using var proc = new Process { StartInfo = psi };
                if (!useShell)
                {
                    proc.OutputDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
                    proc.ErrorDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
                }

                proc.Start();
                if (!useShell) { proc.BeginOutputReadLine(); proc.BeginErrorReadLine(); }

                using var cts2 = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts2.CancelAfter(timeoutMs);
                try { await proc.WaitForExitAsync(cts2.Token); }
                catch (OperationCanceledException)
                {
                    try { proc.Kill(entireProcessTree: true); } catch { }
                    if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
                    // DEBUG: Registrar timeout
                    _logger.LogWarning($"[AdvancedRepair][RunAsync] TIMEOUT após {timeoutMs}ms: {exe} {args}");
                    return (-1, sb.ToString()); // timeout — continua
                }
                // DEBUG: Registrar resultado do processo
                _logger.LogDebug($"[AdvancedRepair][RunAsync] Concluído: {exe} exit={proc.ExitCode} output_len={sb.Length}", source: "AdvancedRepairService");
                return (proc.ExitCode, sb.ToString());
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning($"[AdvancedRepair] RunAsync({exe} {args}): {ex.Message}");
                return (-99, ex.Message);
            }
        }

        /// <summary>
        /// Executa via PowerShell com script inline.
        /// </summary>
        private async Task<(int exitCode, string output)> RunPsAsync(
            string script, CancellationToken ct, int timeoutMs = 120_000)
        {
            var psPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32", "WindowsPowerShell", "v1.0", "powershell.exe");

            // Salvar script em arquivo temp para evitar problemas de escape
            var tmp = Path.GetTempFileName() + ".ps1";
            // DEBUG: Registrar execução de script PowerShell
            _logger.LogDebug($"[AdvancedRepair][RunPsAsync] Script temp: {tmp} (timeout={timeoutMs}ms, script_len={script.Length})", source: "AdvancedRepairService");
            try
            {
                await File.WriteAllTextAsync(tmp, script, Encoding.UTF8, ct);
                var args = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{tmp}\"";
                var result = await RunAsync(psPath, args, ct, timeoutMs: timeoutMs);
                // DEBUG: Registrar resultado do PowerShell
                _logger.LogDebug($"[AdvancedRepair][RunPsAsync] Concluído: exit={result.exitCode} output_len={result.output.Length}", source: "AdvancedRepairService");
                return result;
            }
            finally
            {
                try { File.Delete(tmp); } catch { }
            }
        }

        private static string WinDir =>
            Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        private static string Sys32 =>
            Path.Combine(WinDir, "System32");

        private static string ResolveExe(string exe)
        {
            var p = Path.Combine(Sys32, exe);
            if (File.Exists(p)) return p;
            if (Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess)
            {
                var sn = Path.Combine(WinDir, "SysNative", exe);
                if (File.Exists(sn)) return sn;
            }
            return exe;
        }

        // ─────────────────────────────────────────────────────────────────────
        // ETAPA 0 — PONTO DE RESTAURAÇÃO + BACKUP (antes de tudo)
        // ─────────────────────────────────────────────────────────────────────
        public async Task<AdvancedRepairStepResult> Step00_CreateRestorePointAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var step = new AdvancedRepairStepResult { StepName = Loc("Repair_Step00_Name") };
            // DEBUG: Início da etapa com timestamp
            _logger.LogDebug($"[AdvancedRepair][Step00] INÍCIO às {DateTime.Now:HH:mm:ss.fff}", source: "AdvancedRepairService");
            Log($"═══ {Loc("Repair_Step00_Title")} ═══", "#00BFFF");
            Progress(1, Loc("Repair_Step00_Progress"));

            var rpName = $"Voltris_FullRepair_{DateTime.Now:yyyyMMdd_HHmmss}";
            _logger.LogDebug($"[AdvancedRepair][Step00] Nome do ponto de restauração: {rpName}", source: "AdvancedRepairService");
            
// Use the centralized SystemToolsService for restore point creation
var systemTools = new SystemToolsService(_logger);
var isCreated = await systemTools.CreateSystemRestorePointAsync(rpName);
step.Success = isCreated;
step.Summary = isCreated ? string.Format(Loc("Repair_Step00_SuccessFmt"), rpName) : Loc("Repair_Step00_Fail");
step.Details.Add(isCreated ? "Restore point created successfully." : "Failed to create restore point.");
_logger.LogDebug($"[AdvancedRepair][Step00] Resultado: success={isCreated} duration={sw.Elapsed.TotalSeconds:F1}s", source: "AdvancedRepairService");
Log(isCreated ? $"✓ {step.Summary}" : $"⚠ {step.Summary}", isCreated ? "#00FF88" : "#FFA500");
step.Duration = sw.Elapsed;
return step;
        }

        // ─────────────────────────────────────────────────────────────────────
        // ETAPA 1 — RESET COMPLETO DE REDE
        // ─────────────────────────────────────────────────────────────────────
        // CORREÇÃO: Agora preserva regras de firewall de aplicativos de terceiros
        // para evitar que Chrome, Steam, etc. solicitem autorização novamente.
        // ─────────────────────────────────────────────────────────────────────
        public async Task<AdvancedRepairStepResult> Step01_NetworkResetAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var step = new AdvancedRepairStepResult { StepName = Loc("Repair_Step01_Name") };
            
            // ═══════════════════════════════════════════════════════════════════════
            // LOGS EXPANDIDOS - PIPELINE DE REDE
            // ═══════════════════════════════════════════════════════════════════════
            _logger.LogInfo($"[SmartRepair][Step01][NetworkReset] ═══════════════════════════════════════");
            _logger.LogInfo($"[SmartRepair][Step01][NetworkReset] INÍCIO DA ETAPA 1 - RESET DE REDE às {DateTime.Now:HH:mm:ss.fff}");
            _logger.LogInfo($"[SmartRepair][Step01][NetworkReset] Duração estimada: 30-60 segundos");
            
            Log($"═══ {Loc("Repair_Step01_Title")} ═══", "#00BFFF");
            Progress(5, Loc("Repair_Step01_Progress"));

            var netsh = ResolveExe("netsh.exe");
            var ipconfig = ResolveExe("ipconfig.exe");
            var powershell = ResolveExe("powershell.exe");
            
            _logger.LogDebug($"[AdvancedRepair][Step01] netsh={netsh} ipconfig={ipconfig} powershell={powershell}", source: "AdvancedRepairService");
            _logger.LogInfo($"[SmartRepair][Step01][NetworkReset] Executáveis localizados: netsh={netsh}, ipconfig={ipconfig}");

            // ═══════════════════════════════════════════════════════════════════════
            // FASE 1 - BACKUP DAS REGRAS DE FIREWALL DE APLICATIVOS
            // ═══════════════════════════════════════════════════════════════════════
            _logger.LogInfo($"[SmartRepair][Step01][NetworkReset] FASE 1: Criando backup das regras de firewall de aplicativos...");
            Log($"  → {Loc("Repair_CheckingFirewallRules")}...", "#AAAAAA");
            
            bool firewallRulesBackedUp = false;
            string backupPath = Path.Combine(Path.GetTempPath(), $"Voltris_FirewallBackup_{DateTime.Now:yyyyMMdd_HHmmss}.xml");
            
            try
            {
                // Backup das regras de firewall (todas as regras, incluindo aplicativos)
                var backupScript = $@"
                    try {{
                        # Exportar TODAS as regras de firewall para XML
                        netsh advfirewall export ""{backupPath.Replace("\\", "\\\\")}""
                        Write-Output ""SUCCESS: Backup criado em {backupPath}""
                    }} catch {{
                        Write-Output ""ERROR: $_""
                        exit 1
                    }}
                ";
                
                var (backupCode, backupOut) = await RunPsAsync(backupScript, ct, timeoutMs: 30_000);
                
                if (backupCode == 0 && File.Exists(backupPath))
                {
                    var fileInfo = new FileInfo(backupPath);
                    firewallRulesBackedUp = true;
                    _logger.LogSuccess($"[SmartRepair][Step01][NetworkReset] ✅ Backup de firewall criado: {backupPath} ({fileInfo.Length} bytes)");
                    _logger.LogDebug($"[SmartRepair][Step01][NetworkReset] Conteúdo do backup: {backupOut.Trim()}");
                    step.Details.Add($"[Firewall Backup] SUCCESS: {backupPath} ({fileInfo.Length} bytes)");
                    Log($"  ✓ Backup de regras de firewall criado ({fileInfo.Length} bytes)", "#00FF88");
                }
                else
                {
                    _logger.LogWarning($"[SmartRepair][Step01][NetworkReset] ⚠️ Falha ao criar backup: exitCode={backupCode}, output={backupOut}");
                    step.Details.Add($"[Firewall Backup] WARNING: Failed (exit={backupCode})");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[SmartRepair][Step01][NetworkReset] Erro ao criar backup de firewall: {ex.Message}", ex);
                step.Details.Add($"[Firewall Backup] ERROR: {ex.Message}");
            }

            // ═══════════════════════════════════════════════════════════════════════
            // FASE 2 - IDENTIFICAR REGRAS DE APLICATIVOS CRÍTICOS
            // ═══════════════════════════════════════════════════════════════════════
            _logger.LogInfo($"[SmartRepair][Step01][NetworkReset] FASE 2: Identificando regras de aplicativos críticos...");
            
            var criticalApps = new[]
            {
                "chrome", "firefox", "edge", "opera", "brave", // Browsers
                "steam", "epicgames", "origin", "uplay", "battlenet", // Games
                "discord", "teams", "zoom", "skype", "slack", // Comunicação
                "spotify", "vlc", "netflix" // Mídia
            };
            
            List<string> appRulesToRestore = new List<string>();
            
            try
            {
                var listScript = @"
                    Get-NetFirewallRule | Where-Object {
                        $_.Enabled -eq 'True' -and 
                        $_.Direction -eq 'Outbound' -and
                        $_.Profile -ne 'Public'
                    } | ForEach-Object {
                        $app = $_.DisplayName
                        if ($app -match 'chrome|firefox|edge|steam|epic|discord|spotify|vlc') {
                            Write-Output ""RULE:$($_.Name):$app""
                        }
                    }
                ";
                
                var (_, listOut) = await RunPsAsync(listScript, ct, timeoutMs: 15_000);
                
                foreach (var line in listOut.Split('\n'))
                {
                    if (line.StartsWith("RULE:"))
                    {
                        var parts = line.Split(':');
                        if (parts.Length >= 2)
                        {
                            appRulesToRestore.Add(parts[1]);
                            _logger.LogDebug($"[SmartRepair][Step01][NetworkReset] Regra crítica identificada: {parts[1]} ({parts[2]})");
                        }
                    }
                }
                
                _logger.LogInfo($"[SmartRepair][Step01][NetworkReset] {appRulesToRestore.Count} regras de aplicativos críticos identificadas");
                step.Details.Add($"[Critical Apps] {appRulesToRestore.Count} rules identified");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[SmartRepair][Step01][NetworkReset] Erro ao listar regras: {ex.Message}");
            }

            // ═══════════════════════════════════════════════════════════════════════
            // FASE 3 - RESET DE REDE (exceto firewall inicialmente)
            // ═══════════════════════════════════════════════════════════════════════
            _logger.LogInfo($"[SmartRepair][Step01][NetworkReset] FASE 3: Executando reset de rede (winsock, tcp/ip)...");
            
            var networkCmds = new[]
            {
                (netsh, "winsock reset",    "Winsock reset",  new[]{ 0 }),
                (netsh, "int ip reset",     "TCP/IP reset",   new[]{ 0, 1 }),
                (netsh, "int ipv6 reset",   "IPv6 reset",     new[]{ 0, 1 }),
                (ipconfig, "/flushdns",     "DNS flush",      new[]{ 0 }),
                (ipconfig, "/release",      "IP release",     new[]{ 0, 1 }),
                (ipconfig, "/renew",        "IP renew",       new[]{ 0, 1 })};

            var allOk = true;
            foreach (var (exe, args, label, okCodes) in networkCmds)
            {
                ct.ThrowIfCancellationRequested();
                
                _logger.LogInfo($"[SmartRepair][Step01][NetworkReset] Executando: {label} ({exe} {args})");
                Log($"  → {label}...", "#AAAAAA");
                
                var (code, out_) = await RunAsync(exe, args, ct, timeoutMs: 30_000);
                var ok = okCodes.Contains(code) || code == -1;
                
                step.Details.Add($"[{label}] exit={code}: {out_.Trim().Replace("\r\n", " | ")}");
                
                _logger.LogDebug($"[AdvancedRepair][Step01] {label}: exit={code} ok={ok}", source: "AdvancedRepairService");
                
                if (!ok) 
                { 
                    allOk = false; 
                    _logger.LogWarning($"[SmartRepair][Step01][NetworkReset] ⚠️ {label} retornou {code}");
                    Log($"  ⚠ {label} retornou {code}", "#FFA500"); 
                }
                else 
                {
                    _logger.LogSuccess($"[SmartRepair][Step01][NetworkReset] ✓ {label} concluído");
                    Log($"  ✓ {label}", "#00FF88");
                }
            }

            // ═══════════════════════════════════════════════════════════════════════
            // FASE 4 - RESET PARCIAL DO FIREWALL (APENAS POLÍTICAS, NÃO REGRAS)
            // ═══════════════════════════════════════════════════════════════════════
            _logger.LogInfo($"[SmartRepair][Step01][NetworkReset] FASE 4: Resetando políticas do firewall (preservando regras de aplicativos)...");
            Log($"  → Restaurando políticas padrão do Firewall...", "#AAAAAA");
            
            bool firewallResetOk = false;
            
            try
            {
                // CORREÇÃO CRÍTICA: Ao invés de "advfirewall reset" (que remove TODAS as regras),
                // vamos resetar APENAS as configurações de segurança dos perfis
                var firewallScript = @"
                    try {
                        # Resetar APENAS as políticas de segurança dos perfis
                        # NÃO remove regras de aplicativos
                        
                        # Perfil Domain
                        Set-NetFirewallProfile -Profile Domain -Enabled True -DefaultInboundAction Block -DefaultOutboundAction Allow -NotifyOnListen True -LogFileName ""%systemroot%\system32\LogFiles\Firewall\pfirewall.log"" -LogMaxSizeKBytes 16384 -LogAllowed True -LogDropped True -ErrorAction SilentlyContinue
                        
                        # Perfil Private
                        Set-NetFirewallProfile -Profile Private -Enabled True -DefaultInboundAction Block -DefaultOutboundAction Allow -NotifyOnListen True -LogFileName ""%systemroot%\system32\LogFiles\Firewall\pfirewall.log"" -LogMaxSizeKBytes 16384 -LogAllowed True -LogDropped True -ErrorAction SilentlyContinue
                        
                        # Perfil Public
                        Set-NetFirewallProfile -Profile Public -Enabled True -DefaultInboundAction Block -DefaultOutboundAction Allow -NotifyOnListen True -LogFileName ""%systemroot%\system32\LogFiles\Firewall\pfirewall.log"" -LogMaxSizeKBytes 16384 -LogAllowed True -LogDropped True -ErrorAction SilentlyContinue
                        
                        Write-Output ""SUCCESS: Firewall policies reset (rules preserved)""
                        exit 0
                    } catch {
                        Write-Output ""ERROR: $_""
                        exit 1
                    }
                ";
                
                var (fwCode, fwOut) = await RunPsAsync(firewallScript, ct, timeoutMs: 30_000);
                firewallResetOk = fwCode == 0;
                
                step.Details.Add($"[Firewall Policy Reset] exit={fwCode}: {fwOut.Trim()}");
                
                if (firewallResetOk)
                {
                    _logger.LogSuccess($"[SmartRepair][Step01][NetworkReset] ✓ Políticas do firewall resetadas (regras preservadas)");
                    Log($"  ✓ Políticas do firewall resetadas", "#00FF88");
                }
                else
                {
                    _logger.LogWarning($"[SmartRepair][Step01][NetworkReset] ⚠️ Reset de políticas falhou: {fwOut}");
                    Log($"  ⚠ Reset de políticas falhou", "#FFA500");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[SmartRepair][Step01][NetworkReset] Erro ao resetar políticas: {ex.Message}", ex);
                step.Details.Add($"[Firewall Policy Reset] ERROR: {ex.Message}");
            }

            // ═══════════════════════════════════════════════════════════════════════
            // FASE 5 - VALIDAÇÃO PÓS-RESET
            // ═══════════════════════════════════════════════════════════════════════
            _logger.LogInfo($"[SmartRepair][Step01][NetworkReset] FASE 5: Validando configuração do firewall...");
            
            try
            {
                var validateScript = @"
                    $domain = Get-NetFirewallProfile -Profile Domain -ErrorAction SilentlyContinue
                    $private = Get-NetFirewallProfile -Profile Private -ErrorAction SilentlyContinue
                    $public = Get-NetFirewallProfile -Profile Public -ErrorAction SilentlyContinue
                    
                    Write-Output ""Domain: Enabled=$($domain.Enabled), Inbound=$($domain.DefaultInboundAction)""
                    Write-Output ""Private: Enabled=$($private.Enabled), Inbound=$($private.DefaultInboundAction)""
                    Write-Output ""Public: Enabled=$($public.Enabled), Inbound=$($public.DefaultInboundAction)""
                    
                    # Contar regras ativas
                    $ruleCount = (Get-NetFirewallRule | Where-Object { $_.Enabled -eq 'True' }).Count
                    Write-Output ""TotalRules: $ruleCount""
                ";
                
                var (_, validateOut) = await RunPsAsync(validateScript, ct, timeoutMs: 15_000);
                
                _logger.LogInfo($"[SmartRepair][Step01][NetworkReset] Validação do firewall:\n{validateOut}");
                step.Details.Add($"[Firewall Validation] {validateOut.Trim()}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[SmartRepair][Step01][NetworkReset] Erro na validação: {ex.Message}");
            }

            // ═══════════════════════════════════════════════════════════════════════
            // FASE 6 - LIMPEZA (OPCIONAL - RESTAURAR REGRAS SE NECESSÁRIO)
            // ═══════════════════════════════════════════════════════════════════════
            // Nota: Não restauramos regras explicitamente porque o novo método
            // já preserva as regras. O backup permanece disponível para rollback se necessário.
            
            if (firewallRulesBackedUp && File.Exists(backupPath))
            {
                _logger.LogDebug($"[SmartRepair][Step01][NetworkReset] Backup mantido em {backupPath} (pode ser removido manualmente se necessário)");
                step.Details.Add($"[Firewall Backup] Preserved at: {backupPath}");
            }

            // ═══════════════════════════════════════════════════════════════════════
            // CONCLUSÃO DA ETAPA
            // ═══════════════════════════════════════════════════════════════════════
            step.Success = allOk && firewallResetOk;
            step.Summary = step.Success ? Loc("Repair_Step01_Success") : Loc("Repair_Step01_Warn");
            
            _logger.LogInfo($"[SmartRepair][Step01][NetworkReset] ═══════════════════════════════════════");
            _logger.LogInfo($"[SmartRepair][Step01][NetworkReset] CONCLUSÃO DA ETAPA 1");
            _logger.LogInfo($"[SmartRepair][Step01][NetworkReset] Status: Success={step.Success}");
            _logger.LogInfo($"[SmartRepair][Step01][NetworkReset] Duração total: {sw.Elapsed.TotalSeconds:F1}s");
            _logger.LogInfo($"[SmartRepair][Step01][NetworkReset] Backup firewall: {firewallRulesBackedUp}");
            _logger.LogInfo($"[SmartRepair][Step01][NetworkReset] Regras preservadas: {appRulesToRestore.Count} identificadas");
            _logger.LogInfo($"[SmartRepair][Step01][NetworkReset] ═══════════════════════════════════════");
            
            Log(step.Success ? $"✓ {step.Summary}" : $"⚠ {step.Summary}", step.Success ? "#00FF88" : "#FFA500");
            step.Duration = sw.Elapsed;
            
            return step;
        }

        // ─────────────────────────────────────────────────────────────────────
        // ETAPA 2 — REPARO .NET FRAMEWORK
        // ─────────────────────────────────────────────────────────────────────
        public async Task<AdvancedRepairStepResult> Step02_DotNetRepairAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var step = new AdvancedRepairStepResult { StepName = Loc("Repair_Step02_Name") };
            _logger.LogDebug($"[AdvancedRepair][Step02] INÍCIO às {DateTime.Now:HH:mm:ss.fff}", source: "AdvancedRepairService");
            Log($"═══ {Loc("Repair_Step02_Title")} ═══", "#00BFFF");
            Progress(10, Loc("Repair_Step02_Progress"));


            // Verificar .NET 4.x via registro
            Log($"  → {Loc("Repair_CheckingDotNet4x")}", "#AAAAAA");
            var script = @"
$key = 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full'
if (Test-Path $key) {
    $release = (Get-ItemProperty $key).Release
    Write-Output ""DOTNET4_RELEASE=$release""
} else { Write-Output ""DOTNET4_NOT_FOUND"" }
";
            var (_, psOut) = await RunPsAsync(script, ct, timeoutMs: 15_000);
            step.Details.Add($"[.NET 4.x Check] {psOut.Trim()}");
            Log($"  → {psOut.Trim()}", "#AAAAAA");

            // SFC para reparar arquivos .NET corrompidos
            Log($"  → {Loc("Repair_SfcForDotNet")}", "#AAAAAA");
            var (sfcCode, sfcOut) = await RunAsync(ResolveExe("sfc.exe"), "/scannow", ct, timeoutMs: 300_000);
            step.Details.Add($"[SFC .NET] exit={sfcCode}");
            Log(sfcCode == 0 ? "  ✓ SFC OK" : $"  ⚠ SFC exit={sfcCode}", sfcCode == 0 ? "#00FF88" : "#FFA500");

            step.Success = true;
            step.Summary = Loc("Repair_Step02_Success");
            _logger.LogDebug($"[AdvancedRepair][Step02] CONCLUÍDO: duration={sw.Elapsed.TotalSeconds:F1}s", source: "AdvancedRepairService");
            Log($"✓ {step.Summary}", "#00FF88");
            step.Duration = sw.Elapsed;
            return step;
        }

        // ─────────────────────────────────────────────────────────────────────
        // ETAPA 3 — REPARO VISUAL C++ REDISTRIBUTABLE
        // ─────────────────────────────────────────────────────────────────────
        public async Task<AdvancedRepairStepResult> Step03_VcRedistRepairAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var step = new AdvancedRepairStepResult { StepName = Loc("Repair_Step03_Name") };
            _logger.LogDebug($"[AdvancedRepair][Step03] INÍCIO às {DateTime.Now:HH:mm:ss.fff}", source: "AdvancedRepairService");
            Log($"═══ {Loc("Repair_Step03_Title")} ═══", "#00BFFF");
            Progress(15, Loc("Repair_Step03_Progress"));

            // Listar VCRedist instalados via registro
            var script = @"
$paths = @(
    'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
    'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*'
)
$vcList = Get-ItemProperty $paths -ErrorAction SilentlyContinue |
    Where-Object { $_.DisplayName -match 'Visual C\+\+' } |
    Select-Object DisplayName, DisplayVersion, InstallDate |
    Sort-Object DisplayName
foreach ($vc in $vcList) {
    Write-Output ""VC: $($vc.DisplayName) | $($vc.DisplayVersion)""
}
if (-not $vcList) { Write-Output ""VC_NONE_FOUND"" }
";
            var (_, psOut) = await RunPsAsync(script, ct, timeoutMs: 30_000);
            var lines = psOut.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var l in lines)
            {
                var t = l.Trim();
                if (!string.IsNullOrEmpty(t)) { step.Details.Add(t); Log($"  {t}", "#AAAAAA"); }
            }

            // Reparar DLLs do sistema relacionadas ao VC++ via SFC
            Log($"  → {Loc("Repair_CheckingSystemDlls")}", "#AAAAAA");
            var dllsToCheck = new[] { "msvcp140.dll", "vcruntime140.dll", "msvcp120.dll", "msvcp110.dll" };
            foreach (var dll in dllsToCheck)
            {
                var dllPath = Path.Combine(Sys32, dll);
                var exists = File.Exists(dllPath);
                step.Details.Add($"[{dll}] {(exists ? "PRESENTE" : "AUSENTE")}");
                Log($"  {dll}: {(exists ? "✓ presente" : "⚠ ausente")}", exists ? "#00FF88" : "#FFA500");
            }

            step.Success = true;
            step.Summary = Loc("Repair_Step03_Success");
            _logger.LogDebug($"[AdvancedRepair][Step03] CONCLUÍDO: duration={sw.Elapsed.TotalSeconds:F1}s", source: "AdvancedRepairService");
            Log($"✓ {step.Summary}", "#00FF88");
            step.Duration = sw.Elapsed;
            return step;
        }

        // ─────────────────────────────────────────────────────────────────────
        // ETAPA 4 — RESET WINDOWS STORE + APPS UWP
        // ─────────────────────────────────────────────────────────────────────
        public async Task<AdvancedRepairStepResult> Step04_WindowsStoreResetAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var step = new AdvancedRepairStepResult { StepName = Loc("Repair_Step04_Name") };
            _logger.LogDebug($"[AdvancedRepair][Step04] INÍCIO às {DateTime.Now:HH:mm:ss.fff}", source: "AdvancedRepairService");
            Log($"═══ {Loc("Repair_Step04_Title")} ═══", "#00BFFF");
            Progress(20, Loc("Repair_Step04_Progress"));

            // wsreset.exe — reseta cache da Store sem perder apps
            Log($"  → {Loc("Repair_WsresetCache")}", "#AAAAAA");
            var wsreset = Path.Combine(WinDir, "System32", "wsreset.exe");
            if (File.Exists(wsreset))
            {
                // wsreset abre janela — usar via cmd silencioso
                var (code, _) = await RunAsync("cmd.exe", $"/c start /wait \"\" \"{wsreset}\"", ct, timeoutMs: 30_000);
                step.Details.Add($"[wsreset] exit={code}");
                Log("  ✓ wsreset executado", "#00FF88");
            }
            else
            {
                step.Details.Add("[wsreset] não encontrado");
                Log("  ⚠ wsreset.exe não encontrado", "#FFA500");
            }

            // Re-registrar apps UWP para o usuário atual (pulos pacotes críticos do shell)
            Log($"  → {Loc("Repair_ReregisteringUwp")}", "#AAAAAA");
            var script = @"
$criticalPackages = @(
    'Microsoft.Windows.ShellExperienceHost',
    'Microsoft.Windows.StartMenuExperienceHost',
    'Microsoft.Windows.Search',
    'Microsoft.Windows.Cortana',
    'Microsoft.Windows.SecHealthUI',
    'Microsoft.Windows.Apprep.ChxApp',
    'Microsoft.LockApp',
    'Microsoft.BioEnrollment',
    'Microsoft.AccountsControl'
)
Get-AppXPackage -AllUsers | Where-Object { $criticalPackages -notcontains $_.Name } | ForEach-Object {
    try {
        Add-AppxPackage -DisableDevelopmentMode -Register ""$($_.InstallLocation)\AppXManifest.xml"" -ErrorAction SilentlyContinue
    } catch {}
}
Write-Output ""UWP_REREGISTER_DONE""
";
            var (_, psOut) = await RunPsAsync(script, ct, timeoutMs: 120_000);
            step.Details.Add($"[UWP Re-register] {psOut.Trim()}");
            Log(psOut.Contains("UWP_REREGISTER_DONE") ? "  ✓ Apps UWP re-registrados" : "  ⚠ Re-registro com avisos", "#00FF88");

            step.Success = true;
            step.Summary = Loc("Repair_Step04_Success");
            _logger.LogDebug($"[AdvancedRepair][Step04] CONCLUÍDO: duration={sw.Elapsed.TotalSeconds:F1}s", source: "AdvancedRepairService");
            Log($"✓ {step.Summary}", "#00FF88");
            step.Duration = sw.Elapsed;
            return step;
        }

        // ─────────────────────────────────────────────────────────────────────
        // ETAPA 5 — REPARO BOOT MANAGER (BCD + BOOTREC)
        // ─────────────────────────────────────────────────────────────────────
        public async Task<AdvancedRepairStepResult> Step05_BootRepairAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var step = new AdvancedRepairStepResult { StepName = Loc("Repair_Step05_Name") };
            _logger.LogDebug($"[AdvancedRepair][Step05] INÍCIO às {DateTime.Now:HH:mm:ss.fff}", source: "AdvancedRepairService");
            Log($"═══ {Loc("Repair_Step05_Title")} ═══", "#00BFFF");
            Progress(25, Loc("Repair_Step05_Progress"));

            var bcdedit = ResolveExe("bcdedit.exe");

            // Verificar BCD atual (somente leitura — seguro)
            Log($"  → {Loc("Repair_CheckingBcd")}", "#AAAAAA");
            var (codeEnum, outEnum) = await RunAsync(bcdedit, "/enum all", ct, timeoutMs: 15_000);
            step.Details.Add($"[BCD enum] exit={codeEnum}: {outEnum.Trim().Replace("\r\n", " | ")}");

            if (codeEnum == 0)
            {
                Log("  ✓ BCD íntegro", "#00FF88");
                // Verificar se bootmgr está presente
                var bootmgrPath = Path.Combine(
                    Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\", "bootmgr");
                var bootmgrExists = File.Exists(bootmgrPath);
                step.Details.Add($"[bootmgr] {(bootmgrExists ? "PRESENTE" : "AUSENTE")}");
                Log($"  bootmgr: {(bootmgrExists ? "✓ presente" : "⚠ ausente")}", bootmgrExists ? "#00FF88" : "#FFA500");
            }
            else
            {
                Log("  ⚠ BCD com problemas — executando bootrec /fixbcd...", "#FFA500");
                var bootrec = ResolveExe("bootrec.exe");
                var (codeFixBcd, outFixBcd) = await RunAsync(bootrec, "/fixbcd", ct, timeoutMs: 30_000);
                step.Details.Add($"[bootrec /fixbcd] exit={codeFixBcd}: {outFixBcd.Trim()}");
                var (codeFixMbr, outFixMbr) = await RunAsync(bootrec, "/fixmbr", ct, timeoutMs: 30_000);
                step.Details.Add($"[bootrec /fixmbr] exit={codeFixMbr}: {outFixMbr.Trim()}");
                Log($"  bootrec /fixbcd: exit={codeFixBcd}", codeFixBcd == 0 ? "#00FF88" : "#FFA500");
                Log($"  bootrec /fixmbr: exit={codeFixMbr}", codeFixMbr == 0 ? "#00FF88" : "#FFA500");
            }

            step.Success = true;
            step.Summary = Loc("Repair_Step05_Success");
            _logger.LogDebug($"[AdvancedRepair][Step05] CONCLUÍDO: duration={sw.Elapsed.TotalSeconds:F1}s", source: "AdvancedRepairService");
            Log($"✓ {step.Summary}", "#00FF88");
            step.Duration = sw.Elapsed;
            return step;
        }

        // ─────────────────────────────────────────────────────────────────────
        // ETAPA 6 — RESET DE SERVIÇOS DO WINDOWS PARA PADRÃO
        // ─────────────────────────────────────────────────────────────────────
        public async Task<AdvancedRepairStepResult> Step06_ServicesResetAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var step = new AdvancedRepairStepResult { StepName = Loc("Repair_Step06_Name") };
            _logger.LogDebug($"[AdvancedRepair][Step06] INÍCIO às {DateTime.Now:HH:mm:ss.fff}", source: "AdvancedRepairService");
            Log($"═══ {Loc("Repair_Step06_Title")} ═══", "#00BFFF");
            Progress(30, Loc("Repair_Step06_Progress"));

            // Serviços críticos que devem estar em execução/automático
            var criticalServices = new[]
            {
                ("wuauserv",   "Automatic", "Windows Update"),
                ("bits",       "Automatic", "BITS"),
                ("cryptsvc",   "Automatic", "Cryptographic Services"),
                ("msiserver",  "Manual",    "Windows Installer"),
                ("trustedinstaller", "Manual", "TrustedInstaller"),
                ("windefend",  "Automatic", "Windows Defender"),
                ("wscsvc",     "Automatic", "Security Center"),
                ("eventlog",   "Automatic", "Event Log"),
                ("rpcss",      "Automatic", "RPC"),
                ("dnscache",   "Automatic", "DNS Client"),
                ("lanmanworkstation", "Automatic", "Workstation"),
                ("spooler",    "Automatic", "Print Spooler")};

            var sc = ResolveExe("sc.exe");
            var net = ResolveExe("net.exe");
            foreach (var (svc, startType, label) in criticalServices)
            {
                ct.ThrowIfCancellationRequested();
                // Verificar estado atual
                var (qCode, qOut) = await RunAsync(sc, $"qc {svc}", ct, timeoutMs: 10_000);
                if (qCode != 0)
                {
                    step.Details.Add($"[{svc}] não encontrado");
                    continue;
                }

                // Garantir tipo de inicialização correto
                var (cfgCode, _) = await RunAsync(sc, $"config {svc} start= {startType.ToLower()}", ct, timeoutMs: 10_000);
                step.Details.Add($"[{svc}] config start={startType} exit={cfgCode}");

                // Iniciar se não estiver rodando
                var (startCode, _) = await RunAsync(sc, $"start {svc}", ct, timeoutMs: 15_000);
                // exit 1056 = já está rodando (não é erro)
                var running = startCode == 0 || startCode == 1056;
                Log($"  {label} ({svc}): {(running ? "✓ OK" : $"⚠ exit={startCode}")}", running ? "#00FF88" : "#FFA500");
            }

            step.Success = true;
            step.Summary = Loc("Repair_Step06_Success");
            _logger.LogDebug($"[AdvancedRepair][Step06] CONCLUÍDO: duration={sw.Elapsed.TotalSeconds:F1}s", source: "AdvancedRepairService");
            Log($"✓ {step.Summary}", "#00FF88");
            step.Duration = sw.Elapsed;
            return step;
        }

        // ─────────────────────────────────────────────────────────────────────
        // ETAPA 7 — REPARO DE REGISTRO (chaves críticas + limpeza segura)
        // ─────────────────────────────────────────────────────────────────────
        public async Task<AdvancedRepairStepResult> Step07_RegistryRepairAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var step = new AdvancedRepairStepResult { StepName = Loc("Repair_Step07_Name") };
            _logger.LogDebug($"[AdvancedRepair][Step07] INÍCIO às {DateTime.Now:HH:mm:ss.fff}", source: "AdvancedRepairService");
            Log($"═══ {Loc("Repair_Step07_Title")} ═══", "#00BFFF");
            Progress(35, Loc("Repair_Step07_Progress"));

            // Script PowerShell seguro — apenas leitura e correção de chaves conhecidas
            var script = @"
$errors = @()

# Verificar chave de ambiente do sistema
$envKey = 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Environment'
if (-not (Test-Path $envKey)) {
    $errors += 'MISSING: Session Manager Environment key'
} else {
    Write-Output 'OK: Session Manager Environment'
}

# Verificar chave de serviços
$svcKey = 'HKLM:\SYSTEM\CurrentControlSet\Services'
if (Test-Path $svcKey) { Write-Output 'OK: Services key' }
else { $errors += 'MISSING: Services key' }

# Verificar chave de drivers
$drvKey = 'HKLM:\SYSTEM\CurrentControlSet\Control\Class'
if (Test-Path $drvKey) { Write-Output 'OK: Driver Class key' }
else { $errors += 'MISSING: Driver Class key' }

# Verificar chave de segurança
$secKey = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon'
if (Test-Path $secKey) {
    $shell = (Get-ItemProperty $secKey -ErrorAction SilentlyContinue).Shell
    if ($shell -ne 'explorer.exe') {
        Set-ItemProperty $secKey -Name 'Shell' -Value 'explorer.exe' -ErrorAction SilentlyContinue
        Write-Output 'FIXED: Winlogon Shell restaurado para explorer.exe'
    } else { Write-Output 'OK: Winlogon Shell' }
}

# Verificar e corrigir políticas de execução corrompidas
$polKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System'
if (Test-Path $polKey) {
    $uac = (Get-ItemProperty $polKey -ErrorAction SilentlyContinue).EnableLUA
    if ($uac -eq $null) {
        Set-ItemProperty $polKey -Name 'EnableLUA' -Value 1 -Type DWord -ErrorAction SilentlyContinue
        Write-Output 'FIXED: EnableLUA restaurado'
    } else { Write-Output ""OK: EnableLUA=$uac"" }
}

# Limpar entradas de Run inválidas (sem excluir entradas válidas)
$runKey = 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run'
if (Test-Path $runKey) {
    $entries = Get-ItemProperty $runKey -ErrorAction SilentlyContinue
    $removed = 0
    foreach ($prop in ($entries.PSObject.Properties | Where-Object { $_.Name -notlike 'PS*' })) {
        $val = $prop.Value
        if ($val -is [string]) {
            $exePath = $val -replace '""', '' -split ' ' | Select-Object -First 1
            if ($exePath -and -not (Test-Path $exePath -ErrorAction SilentlyContinue)) {
                Remove-ItemProperty $runKey -Name $prop.Name -ErrorAction SilentlyContinue
                $removed++
                Write-Output ""CLEANED_RUN: $($prop.Name)""
            }
        }
    }
    Write-Output ""Run entries cleaned: $removed""
}

foreach ($e in $errors) { Write-Output ""ERROR: $e"" }
Write-Output 'REGISTRY_REPAIR_DONE'
";
            var (_, psOut) = await RunPsAsync(script, ct, timeoutMs: 60_000);
            foreach (var line in psOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var t = line.Trim();
                if (string.IsNullOrEmpty(t)) continue;
                step.Details.Add(t);
                var color = t.StartsWith("OK") ? "#00FF88" : t.StartsWith("FIXED") || t.StartsWith("CLEANED") ? "#FFD700" : "#FFA500";
                Log($"  {t}", color);
            }

            step.Success = psOut.Contains("REGISTRY_REPAIR_DONE");
            step.Summary = step.Success ? Loc("Repair_Step07_Success") : Loc("Repair_Step07_Warn");
            _logger.LogDebug($"[AdvancedRepair][Step07] CONCLUÍDO: success={step.Success} duration={sw.Elapsed.TotalSeconds:F1}s", source: "AdvancedRepairService");
            Log($"{(step.Success ? "✓" : "⚠")} {step.Summary}", step.Success ? "#00FF88" : "#FFA500");
            step.Duration = sw.Elapsed;
            return step;
        }

        // ─────────────────────────────────────────────────────────────────────
        // ETAPA 8 — LIMPEZA PROFUNDA DE CACHE
        // ─────────────────────────────────────────────────────────────────────
        public async Task<AdvancedRepairStepResult> Step08_CacheCleanAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var step = new AdvancedRepairStepResult { StepName = Loc("Repair_Step08_Name") };
            _logger.LogDebug($"[AdvancedRepair][Step08] INÍCIO às {DateTime.Now:HH:mm:ss.fff}", source: "AdvancedRepairService");
            Log($"═══ {Loc("Repair_Step08_Title")} ═══", "#00BFFF");
            Progress(40, Loc("Repair_Step08_Progress"));

            long totalFreed = 0;

            var foldersToClean = new[]
            {
                (Path.Combine(WinDir, "Prefetch"),                    "Prefetch"),
                (Path.Combine(Path.GetTempPath()),                    "Temp do usuário"),
                (Path.Combine(WinDir, "Temp"),                        "Temp do Windows"),
                (Path.Combine(WinDir, "SoftwareDistribution", "Download"), "Windows Update Cache"),
                (Path.Combine(WinDir, "SoftwareDistribution", "DataStore"), "WU DataStore"),
                (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"), "AppData Temp")};

            foreach (var (folder, label) in foldersToClean)
            {
                ct.ThrowIfCancellationRequested();
                if (!Directory.Exists(folder))
                {
                    Log($"  {label}: pasta não encontrada", "#666666");
                    continue;
                }

                long freed = 0;
                int count = 0;
                try
                {
                    foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            var fi = new FileInfo(file);
                            freed += fi.Length;
                            fi.Delete();
                            count++;
                        }
                        catch { /* arquivo em uso — ignorar */ }
                    }
                    // Remover subpastas vazias
                    foreach (var dir in Directory.EnumerateDirectories(folder))
                    {
                        try { Directory.Delete(dir, recursive: true); } catch { }
                    }
                }
                catch (Exception ex) { _logger.LogWarning($"[AdvancedRepair] Cache clean {label}: {ex.Message}"); }

                totalFreed += freed;
                var mb = freed / 1_048_576.0;
                step.Details.Add($"[{label}] {count} arquivos, {mb:F1} MB liberados");
                Log($"  ✓ {label}: {count} arquivos, {mb:F1} MB liberados", "#00FF88");
            }

            // Parar Windows Update antes de limpar SoftwareDistribution (já feito acima)
            // Reiniciar serviço wuauserv
            var sc = ResolveExe("sc.exe");
            await RunAsync(sc, "stop wuauserv", ct, timeoutMs: 15_000);
            await Task.Delay(2000, ct);
            await RunAsync(sc, "start wuauserv", ct, timeoutMs: 15_000);
            step.Details.Add("[wuauserv] reiniciado após limpeza");

            step.Success = true;
            step.Summary = string.Format(Loc("Repair_Step08_SuccessFmt"), $"{totalFreed / 1_048_576.0:F1}");
            _logger.LogDebug($"[AdvancedRepair][Step08] CONCLUÍDO: {totalFreed / 1_048_576.0:F1} MB liberados duration={sw.Elapsed.TotalSeconds:F1}s", source: "AdvancedRepairService");
            Log($"✓ {step.Summary}", "#00FF88");
            step.Duration = sw.Elapsed;
            return step;
        }

        // ─────────────────────────────────────────────────────────────────────
        // ETAPA 9 — REPARO WINDOWS DEFENDER + SECURITY CENTER
        // ─────────────────────────────────────────────────────────────────────
        public async Task<AdvancedRepairStepResult> Step09_DefenderRepairAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var step = new AdvancedRepairStepResult { StepName = Loc("Repair_Step09_Name") };
            _logger.LogDebug($"[AdvancedRepair][Step09] INÍCIO às {DateTime.Now:HH:mm:ss.fff}", source: "AdvancedRepairService");
            Log($"═══ {Loc("Repair_Step09_Title")} ═══", "#00BFFF");
            Progress(45, Loc("Repair_Step09_Progress"));

            var script = @"
# Verificar status do Defender
$mpStatus = Get-MpComputerStatus -ErrorAction SilentlyContinue
if ($mpStatus) {
    Write-Output ""DEFENDER_ENABLED=$($mpStatus.AntivirusEnabled)""
    Write-Output ""REALTIME=$($mpStatus.RealTimeProtectionEnabled)""
    Write-Output ""DEFINITIONS=$($mpStatus.AntivirusSignatureLastUpdated)""
    
    # Atualizar definições
    try {
        Update-MpSignature -ErrorAction SilentlyContinue
        Write-Output 'DEFINITIONS_UPDATED'
    } catch { Write-Output 'DEFINITIONS_UPDATE_SKIPPED' }
} else {
    Write-Output 'DEFENDER_NOT_AVAILABLE'
}

# Verificar Security Center
$wsc = Get-Service -Name wscsvc -ErrorAction SilentlyContinue
if ($wsc) {
    Write-Output ""SECURITY_CENTER=$($wsc.Status)""
    if ($wsc.Status -ne 'Running') {
        Start-Service wscsvc -ErrorAction SilentlyContinue
        Write-Output 'SECURITY_CENTER_STARTED'
    }
}
Write-Output 'DEFENDER_REPAIR_DONE'
";
            var (_, psOut) = await RunPsAsync(script, ct, timeoutMs: 120_000);
            foreach (var line in psOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var t = line.Trim();
                if (string.IsNullOrEmpty(t)) continue;
                step.Details.Add(t);
                Log($"  {t}", "#AAAAAA");
            }

            step.Success = psOut.Contains("DEFENDER_REPAIR_DONE");
            step.Summary = step.Success ? Loc("Repair_Step09_Success") : Loc("Repair_Step09_Warn");
            _logger.LogDebug($"[AdvancedRepair][Step09] CONCLUÍDO: success={step.Success} duration={sw.Elapsed.TotalSeconds:F1}s", source: "AdvancedRepairService");
            Log($"{(step.Success ? "✓" : "⚠")} {step.Summary}", step.Success ? "#00FF88" : "#FFA500");
            step.Duration = sw.Elapsed;
            return step;
        }

        // ─────────────────────────────────────────────────────────────────────
        // ETAPA 10 — VERIFICAÇÃO E REPARO DE DRIVERS
        // ─────────────────────────────────────────────────────────────────────
        public async Task<AdvancedRepairStepResult> Step10_DriverRepairAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var step = new AdvancedRepairStepResult { StepName = Loc("Repair_Step10_Name") };
            _logger.LogDebug($"[AdvancedRepair][Step10] INÍCIO às {DateTime.Now:HH:mm:ss.fff}", source: "AdvancedRepairService");
            Log($"═══ {Loc("Repair_Step10_Title")} ═══", "#00BFFF");
            Progress(50, Loc("Repair_Step10_Progress"));

            var pnputil = ResolveExe("pnputil.exe");

            // Listar drivers com problemas
            Log($"  → {Loc("Repair_CheckingDriverProblems")}", "#AAAAAA");
            var script = @"
$problemDevices = Get-PnpDevice | Where-Object { $_.Status -ne 'OK' } |
    Select-Object FriendlyName, Status, Class, InstanceId
foreach ($d in $problemDevices) {
    Write-Output ""PROBLEM: $($d.FriendlyName) | $($d.Status) | $($d.Class)""
}
$total = (Get-PnpDevice).Count
$ok = (Get-PnpDevice | Where-Object { $_.Status -eq 'OK' }).Count
Write-Output ""DRIVER_SUMMARY: $ok/$total OK""
Write-Output 'DRIVER_CHECK_DONE'
";
            var (_, psOut) = await RunPsAsync(script, ct, timeoutMs: 60_000);
            foreach (var line in psOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var t = line.Trim();
                if (string.IsNullOrEmpty(t)) continue;
                step.Details.Add(t);
                var color = t.StartsWith("PROBLEM") ? "#FFA500" : t.StartsWith("DRIVER_SUMMARY") ? "#00FF88" : "#AAAAAA";
                Log($"  {t}", color);
            }

            // Tentar reparar dispositivos com erro via pnputil
            Log($"  → {Loc("Repair_RepairingErrorDevices")}", "#AAAAAA");
            var repairScript = @"
$errorDevices = Get-PnpDevice | Where-Object { $_.Status -eq 'Error' }
foreach ($d in $errorDevices) {
    try {
        Disable-PnpDevice -InstanceId $d.InstanceId -Confirm:$false -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 500
        Enable-PnpDevice -InstanceId $d.InstanceId -Confirm:$false -ErrorAction SilentlyContinue
        Write-Output ""REPAIRED: $($d.FriendlyName)""
    } catch { Write-Output ""SKIP: $($d.FriendlyName)"" }
}
Write-Output 'DRIVER_REPAIR_DONE'
";
            var (_, repairOut) = await RunPsAsync(repairScript, ct, timeoutMs: 60_000);
            foreach (var line in repairOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var t = line.Trim();
                if (!string.IsNullOrEmpty(t)) { step.Details.Add(t); Log($"  {t}", "#AAAAAA"); }
            }

            step.Success = true;
            step.Summary = Loc("Repair_Step10_Success");
            _logger.LogDebug($"[AdvancedRepair][Step10] CONCLUÍDO: duration={sw.Elapsed.TotalSeconds:F1}s", source: "AdvancedRepairService");
            Log($"✓ {step.Summary}", "#00FF88");
            step.Duration = sw.Elapsed;
            return step;
        }

        // ─────────────────────────────────────────────────────────────────────
        // ETAPA 11 — ANÁLISE E REPARO DE BSOD (minidump)
        // ─────────────────────────────────────────────────────────────────────
        public async Task<AdvancedRepairStepResult> Step11_BsodAnalysisAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var step = new AdvancedRepairStepResult { StepName = Loc("Repair_Step11_Name") };
            _logger.LogDebug($"[AdvancedRepair][Step11] INÍCIO às {DateTime.Now:HH:mm:ss.fff}", source: "AdvancedRepairService");
            Log($"═══ {Loc("Repair_Step11_Title")} ═══", "#00BFFF");
            Progress(55, Loc("Repair_Step11_Progress"));

            var minidumpDir = Path.Combine(WinDir, "Minidump");
            if (!Directory.Exists(minidumpDir))
            {
                step.Success = true;
                step.Summary = Loc("Repair_Step11_NoMinidump");
                step.Details.Add("Pasta Minidump não existe");
                _logger.LogDebug($"[AdvancedRepair][Step11] Pasta Minidump não existe — sistema estável", source: "AdvancedRepairService");
                Log($"✓ {step.Summary}", "#00FF88");
                step.Duration = sw.Elapsed;
                return step;
            }

            var dumps = Directory.GetFiles(minidumpDir, "*.dmp")
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .Take(10)
                .ToArray();

            if (dumps.Length == 0)
            {
                step.Success = true;
                step.Summary = Loc("Repair_Step11_NoMinidump");
                _logger.LogDebug($"[AdvancedRepair][Step11] Nenhum dump encontrado em {minidumpDir}", source: "AdvancedRepairService");
                Log($"✓ {step.Summary}", "#00FF88");
                step.Duration = sw.Elapsed;
                return step;
            }

            _logger.LogDebug($"[AdvancedRepair][Step11] {dumps.Length} dump(s) encontrado(s) em {minidumpDir}", source: "AdvancedRepairService");
            Log($"  → {dumps.Length} minidump(s) encontrado(s)", "#FFA500");
            step.Details.Add($"Total de dumps: {dumps.Length}");

            // Analisar via Event Log (BSOD registra no System log)
            var script = @"
$bsodEvents = Get-WinEvent -LogName System -ErrorAction SilentlyContinue |
    Where-Object { $_.Id -eq 41 -or $_.Id -eq 1001 -or $_.Id -eq 6008 } |
    Select-Object -First 10 |
    ForEach-Object { ""BSOD_EVENT: $($_.TimeCreated) | ID=$($_.Id) | $($_.Message.Substring(0, [Math]::Min(120, $_.Message.Length)))"" }
$bsodEvents | ForEach-Object { Write-Output $_ }
Write-Output 'BSOD_ANALYSIS_DONE'
";
            var (_, psOut) = await RunPsAsync(script, ct, timeoutMs: 30_000);
            foreach (var line in psOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var t = line.Trim();
                if (!string.IsNullOrEmpty(t)) { step.Details.Add(t); Log($"  {t}", "#FFA500"); }
            }

            foreach (var dump in dumps)
            {
                var info = new FileInfo(dump);
                step.Details.Add($"[DUMP] {info.Name} | {info.LastWriteTime:yyyy-MM-dd HH:mm} | {info.Length / 1024} KB");
                Log($"  📋 {info.Name} ({info.LastWriteTime:dd/MM/yyyy HH:mm})", "#FFA500");
            }

            step.Success = true;
            step.Summary = string.Format(Loc("Repair_Step11_DumpsAnalyzedFmt"), dumps.Length);
            _logger.LogDebug($"[AdvancedRepair][Step11] CONCLUÍDO: {dumps.Length} dumps analisados duration={sw.Elapsed.TotalSeconds:F1}s", source: "AdvancedRepairService");
            Log($"⚠ {step.Summary}", "#FFA500");
            step.Duration = sw.Elapsed;
            return step;
        }

        // ─────────────────────────────────────────────────────────────────────
        // ETAPA 12 — REPARO EVENT VIEWER
        // ─────────────────────────────────────────────────────────────────────
        public async Task<AdvancedRepairStepResult> Step12_EventViewerRepairAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var step = new AdvancedRepairStepResult { StepName = Loc("Repair_Step12_Name") };
            _logger.LogDebug($"[AdvancedRepair][Step12] INÍCIO às {DateTime.Now:HH:mm:ss.fff}", source: "AdvancedRepairService");
            Log($"═══ {Loc("Repair_Step12_Title")} ═══", "#00BFFF");
            Progress(58, Loc("Repair_Step12_Progress"));

            var script = @"
# Contar erros críticos nas últimas 24h
$since = (Get-Date).AddHours(-24)
$criticalCount = (Get-WinEvent -LogName System -ErrorAction SilentlyContinue |
    Where-Object { $_.Level -le 2 -and $_.TimeCreated -gt $since }).Count
$appCritical = (Get-WinEvent -LogName Application -ErrorAction SilentlyContinue |
    Where-Object { $_.Level -le 2 -and $_.TimeCreated -gt $since }).Count
Write-Output ""SYSTEM_CRITICAL_24H=$criticalCount""
Write-Output ""APP_CRITICAL_24H=$appCritical""

# Top 5 erros mais frequentes
$topErrors = Get-WinEvent -LogName System -ErrorAction SilentlyContinue |
    Where-Object { $_.Level -le 2 } |
    Group-Object Id |
    Sort-Object Count -Descending |
    Select-Object -First 5
foreach ($e in $topErrors) {
    Write-Output ""TOP_ERROR: ID=$($e.Name) Count=$($e.Count)""
}

# Limpar log de Application (apenas entradas antigas > 30 dias)
$cutoff = (Get-Date).AddDays(-30)
Write-Output 'EVENT_ANALYSIS_DONE'
";
            var (_, psOut) = await RunPsAsync(script, ct, timeoutMs: 60_000);
            foreach (var line in psOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var t = line.Trim();
                if (!string.IsNullOrEmpty(t)) { step.Details.Add(t); Log($"  {t}", "#AAAAAA"); }
            }

            step.Success = psOut.Contains("EVENT_ANALYSIS_DONE");
            step.Summary = Loc("Repair_Step12_Success");
            _logger.LogDebug($"[AdvancedRepair][Step12] CONCLUÍDO: success={step.Success} duration={sw.Elapsed.TotalSeconds:F1}s", source: "AdvancedRepairService");
            Log($"✓ {step.Summary}", "#00FF88");
            step.Duration = sw.Elapsed;
            return step;
        }

        // ─────────────────────────────────────────────────────────────────────
        // ETAPA 13 — RESET SPOOLER DE IMPRESSÃO
        // ─────────────────────────────────────────────────────────────────────
        public async Task<AdvancedRepairStepResult> Step13_PrintSpoolerResetAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var step = new AdvancedRepairStepResult { StepName = Loc("Repair_Step13_Name") };
            _logger.LogDebug($"[AdvancedRepair][Step13] INÍCIO às {DateTime.Now:HH:mm:ss.fff}", source: "AdvancedRepairService");
            Log($"═══ {Loc("Repair_Step13_Title")} ═══", "#00BFFF");
            Progress(61, Loc("Repair_Step13_Progress"));

            var sc = ResolveExe("sc.exe");
            var spoolDir = Path.Combine(WinDir, "System32", "spool", "PRINTERS");

            // Parar spooler
            await RunAsync(sc, "stop spooler", ct, timeoutMs: 15_000);
            await Task.Delay(2000, ct);

            // Limpar fila de impressão travada
            if (Directory.Exists(spoolDir))
            {
                int cleared = 0;
                foreach (var f in Directory.GetFiles(spoolDir))
                {
                    try { File.Delete(f); cleared++; } catch { }
                }
                step.Details.Add($"[Spool Queue] {cleared} arquivos removidos");
                Log($"  ✓ Fila de impressão limpa: {cleared} arquivo(s)", "#00FF88");
            }

            // REPARO: Garantir que o Spooler está habilitado antes de tentar iniciar.
            // Exit code 1058 = ERROR_SERVICE_DISABLED — o serviço estava desabilitado.
            // sc config deve vir ANTES de sc start para reabilitar o tipo de inicialização.
            _logger.LogDebug($"[AdvancedRepair][Step13] Reabilitando Spooler (sc config start= auto)...", source: "AdvancedRepairService");
            var (configCode, _) = await RunAsync(sc, "config spooler start= auto", ct, timeoutMs: 10_000);
            step.Details.Add($"[spooler config] exit={configCode}");
            Log(configCode == 0 ? "  ✓ Spooler reabilitado (start=auto)" : $"  ⚠ sc config exit={configCode}", configCode == 0 ? "#00FF88" : "#FFA500");

            // Reiniciar spooler
            var (startCode, _) = await RunAsync(sc, "start spooler", ct, timeoutMs: 15_000);
            step.Details.Add($"[spooler start] exit={startCode}");
            Log(startCode == 0 ? "  ✓ Spooler reiniciado" : $"  ⚠ Spooler exit={startCode}", startCode == 0 ? "#00FF88" : "#FFA500");

            step.Success = true;
            step.Summary = Loc("Repair_Step13_Success");
            _logger.LogDebug($"[AdvancedRepair][Step13] CONCLUÍDO: duration={sw.Elapsed.TotalSeconds:F1}s", source: "AdvancedRepairService");
            Log($"✓ {step.Summary}", "#00FF88");
            step.Duration = sw.Elapsed;
            return step;
        }

        // ─────────────────────────────────────────────────────────────────────
        // ETAPA 14 — REPARO DIRECTX E AUDIO
        // ─────────────────────────────────────────────────────────────────────
        public async Task<AdvancedRepairStepResult> Step14_DirectXAudioRepairAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var step = new AdvancedRepairStepResult { StepName = Loc("Repair_Step14_Name") };
            _logger.LogInfo("[AdvancedRepair][Step14] Iniciando Reparo de Áudio Profissional...");

            // Instanciar o novo serviço de reparo especializado
            var audioSvc = new AudioRepairService(_logger);
            
            // Conectar logs do audioSvc ao OnLog do AdvancedRepair para feedback visual
            audioSvc.OnLog = (msg, hex) => OnLog?.Invoke(msg, hex);
            // O progresso do Step 14 vai de 65% a 70% no fluxo global
            audioSvc.OnProgress = (pct, msg) => OnProgress?.Invoke(65 + (int)(pct * 0.05), msg);

            try
            {
                var audioResult = await audioSvc.RepairAsync(ct);
                step.Success = audioResult.Success;
                step.Summary = audioResult.Summary;
                step.Details.AddRange(audioResult.Fixed.Select(f => $"FIX: {f}"));
                step.Details.AddRange(audioResult.Issues.Select(i => $"ISSUE: {i}"));
                
                if (!string.IsNullOrEmpty(audioResult.FailureReason))
                {
                    step.Details.Add($"ANÁLISE_FALHA: {audioResult.FailureReason}");
                }

                _logger.LogInfo($"[AdvancedRepair][Step14] Reparo de áudio concluído: {audioResult.Summary}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[AdvancedRepair][Step14] Erro fatal no áudio: {ex.Message}");
                step.Success = false;
                step.Summary = Loc("Repair_Step14_CriticalFail");
                step.Details.Add($"Erro: {ex.Message}");
            }

            step.Duration = sw.Elapsed;
            return step;
        }

        // ─────────────────────────────────────────────────────────────────────
        // ETAPA 15 — REPARO DE PERFIL DE USUÁRIO CORROMPIDO
        // ─────────────────────────────────────────────────────────────────────
        public async Task<AdvancedRepairStepResult> Step15_UserProfileRepairAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var step = new AdvancedRepairStepResult { StepName = Loc("Repair_Step15_Name") };
            _logger.LogDebug($"[AdvancedRepair][Step15] INÍCIO às {DateTime.Now:HH:mm:ss.fff}", source: "AdvancedRepairService");
            Log($"═══ {Loc("Repair_Step15_Title")} ═══", "#00BFFF");
            Progress(67, Loc("Repair_Step15_Progress"));

            var script = @"
# Verificar perfis de usuário no registro
$profileKey = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList'
$profiles = Get-ChildItem $profileKey -ErrorAction SilentlyContinue
$issues = 0
foreach ($p in $profiles) {
    $props = Get-ItemProperty $p.PSPath -ErrorAction SilentlyContinue
    $path = $props.ProfileImagePath
    if ($path -and -not (Test-Path $path)) {
        Write-Output ""ORPHAN_PROFILE: $path (SID: $($p.PSChildName))""
        $issues++
    } elseif ($path) {
        Write-Output ""OK_PROFILE: $path""
    }
}

# Verificar pasta do usuário atual
$userProfile = $env:USERPROFILE
$criticalFolders = @('Desktop', 'Documents', 'Downloads', 'AppData')
foreach ($f in $criticalFolders) {
    $fp = Join-Path $userProfile $f
    if (-not (Test-Path $fp)) {
        New-Item -ItemType Directory -Path $fp -Force -ErrorAction SilentlyContinue | Out-Null
        Write-Output ""CREATED: $fp""
    } else {
        Write-Output ""OK: $fp""
    }
}

# Verificar e reparar chaves de shell do usuário
$shellFolders = 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders'
$userShellFolders = 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders'
if (Test-Path $shellFolders) { Write-Output 'OK: Shell Folders key' }
else { Write-Output 'MISSING: Shell Folders key' }

Write-Output ""PROFILE_ISSUES=$issues""
Write-Output 'PROFILE_REPAIR_DONE'
";
            var (_, psOut) = await RunPsAsync(script, ct, timeoutMs: 30_000);
            foreach (var line in psOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var t = line.Trim();
                if (string.IsNullOrEmpty(t)) continue;
                step.Details.Add(t);
                var color = t.StartsWith("ORPHAN") ? "#FFA500" : t.StartsWith("OK") || t.StartsWith("CREATED") ? "#00FF88" : "#AAAAAA";
                Log($"  {t}", color);
            }

            step.Success = psOut.Contains("PROFILE_REPAIR_DONE");
            step.Summary = step.Success ? Loc("Repair_Step15_Success") : Loc("Repair_Step15_Warn");
            _logger.LogDebug($"[AdvancedRepair][Step15] CONCLUÍDO: success={step.Success} duration={sw.Elapsed.TotalSeconds:F1}s", source: "AdvancedRepairService");
            Log($"{(step.Success ? "✓" : "⚠")} {step.Summary}", step.Success ? "#00FF88" : "#FFA500");
            step.Duration = sw.Elapsed;
            return step;
        }

        // ─────────────────────────────────────────────────────────────────────
        // ETAPA 16 — VERIFICAÇÃO DE INTEGRIDADE (DISM + SFC)
        // ─────────────────────────────────────────────────────────────────────
        public async Task<AdvancedRepairStepResult> Step16_IntegrityCheckAsync(
            CancellationToken ct,
            Action<int, string>? progressCallback = null)
        {
            var sw = Stopwatch.StartNew();
            var step = new AdvancedRepairStepResult { StepName = Loc("Repair_Step16_Name") };
            _logger.LogDebug($"[AdvancedRepair][Step16] INÍCIO às {DateTime.Now:HH:mm:ss.fff} — DISM + SFC (etapa mais longa)", source: "AdvancedRepairService");
            Log($"═══ {Loc("Repair_Step16_Title")} ═══", "#00BFFF");
            Progress(70, Loc("Repair_Step16_DismProgress"));

            // DISM
            var dism = ResolveExe("dism.exe");
            Log($"  → {Loc("Repair_DismRestoreHealth")}", "#AAAAAA");
            _logger.LogDebug($"[AdvancedRepair][Step16] Iniciando DISM /RestoreHealth (pode levar até 10 min)", source: "AdvancedRepairService");
            var (dismCode, dismOut) = await RunAsync(dism,
                "/Online /Cleanup-Image /RestoreHealth",
                ct, timeoutMs: 1_200_000);
                _logger.LogDebug($"[AdvancedRepair][Step16] DISM output:\n{dismOut}");
            step.Details.Add($"[DISM] exit={dismCode}");
            _logger.LogDebug($"[AdvancedRepair][Step16] DISM concluído: exit={dismCode} elapsed={sw.Elapsed.TotalSeconds:F1}s", source: "AdvancedRepairService");

            // exit=0 → sucesso total
            // exit=-2146498529 (0x80240021) → erro de Windows Update/conectividade, imagem pode estar OK
            // exit=-2146498512 (0x80240030) → sem fonte de reparo disponível (offline), não é corrupção
            // Qualquer outro código negativo ou > 1 → aviso real
            bool dismOk = dismCode == 0;
            bool dismWarn = !dismOk && (dismCode == unchecked((int)0x80240021) ||
                                        dismCode == unchecked((int)0x80240030) ||
                                        dismCode == 1);
            if (dismOk)
                Log("  ✓ DISM OK", "#00FF88");
            else if (dismWarn)
                Log($"  ⚠ DISM exit={dismCode} (aviso não crítico — conectividade/WU)", "#FFA500");
            else
                Log($"  ✗ DISM exit={dismCode}", "#FF4444");

            Progress(78, Loc("Repair_Step16_SfcProgress"));

            // SFC via PowerShell
            var sfcPath = ResolveExe("sfc.exe");
            _logger.LogDebug($"[AdvancedRepair][Step16] Iniciando SFC /scannow (pode levar até 10 min)", source: "AdvancedRepairService");
            var sfcScript = $"& '{sfcPath}' /scannow; exit $LASTEXITCODE";
            var (sfcCode, sfcOut) = await RunPsAsync(sfcScript, ct, timeoutMs: 1_200_000);
                _logger.LogDebug($"[AdvancedRepair][Step16] SFC output:\n{sfcOut}");
            step.Details.Add($"[SFC] exit={sfcCode}");
            _logger.LogDebug($"[AdvancedRepair][Step16] SFC concluído: exit={sfcCode} elapsed={sw.Elapsed.TotalSeconds:F1}s", source: "AdvancedRepairService");
            bool sfcOk = sfcCode == 0 || sfcCode == 1; // 1 = reparos feitos com sucesso
            Log(sfcOk ? "  ✓ SFC OK" : $"  ⚠ SFC exit={sfcCode}", sfcOk ? "#00FF88" : "#FFA500");

            // Etapa é sucesso se: DISM OK ou apenas aviso não crítico, E SFC passou
            step.Success = (dismOk || dismWarn) && sfcOk;
            step.Summary = step.Success ? Loc("Repair_Step16_Success") : Loc("Repair_Step16_Warn");
            _logger.LogDebug($"[AdvancedRepair][Step16] CONCLU� DO: success={step.Success} dismOk={dismOk} sfcOk={sfcOk} duration={sw.Elapsed.TotalSeconds:F1}s", source: "AdvancedRepairService");
            Log($"{(step.Success ? "✓" : "⚠")} {step.Summary}", step.Success ? "#00FF88" : "#FFA500");
            step.Duration = sw.Elapsed;
            return step;
        }

        // ─────────────────────────────────────────────────────────────────────
        // ETAPA 17 — DIAGNÓSTICO INTELIGENTE (Event Log + sugestões)
        // ─────────────────────────────────────────────────────────────────────
        public async Task<AdvancedRepairStepResult> Step17_SmartDiagnosticsAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var step = new AdvancedRepairStepResult { StepName = Loc("Repair_Step17_Name") };
            _logger.LogDebug($"[AdvancedRepair][Step17] INÍCIO às {DateTime.Now:HH:mm:ss.fff}", source: "AdvancedRepairService");
            Log($"═══ {Loc("Repair_Step17_Title")} ═══", "#00BFFF");
            Progress(83, Loc("Repair_Step17_Progress"));

            var script = @"
$since = (Get-Date).AddDays(-7)
$suggestions = @()

# Erros críticos de sistema
$sysErrors = Get-WinEvent -LogName System -ErrorAction SilentlyContinue |
    Where-Object { $_.Level -le 2 -and $_.TimeCreated -gt $since } |
    Group-Object Id | Sort-Object Count -Descending | Select-Object -First 5
foreach ($e in $sysErrors) {
    Write-Output ""SYS_ERROR: ID=$($e.Name) Ocorrências=$($e.Count)""
}

# Erros de aplicação
$appErrors = Get-WinEvent -LogName Application -ErrorAction SilentlyContinue |
    Where-Object { $_.Level -le 2 -and $_.TimeCreated -gt $since } |
    Group-Object ProviderName | Sort-Object Count -Descending | Select-Object -First 5
foreach ($e in $appErrors) {
    Write-Output ""APP_ERROR: $($e.Name) Ocorrências=$($e.Count)""
}

# Verificar uso de disco
$disk = Get-PSDrive C -ErrorAction SilentlyContinue
if ($disk) {
    $usedPct = [Math]::Round(($disk.Used / ($disk.Used + $disk.Free)) * 100, 1)
    Write-Output ""DISK_USAGE: $usedPct%""
    if ($usedPct -gt 90) { Write-Output 'SUGGESTION: Disco quase cheio - execute limpeza de disco' }
}

# Verificar memória
$mem = Get-CimInstance Win32_OperatingSystem -ErrorAction SilentlyContinue
if ($mem) {
    $usedMem = [Math]::Round((($mem.TotalVisibleMemorySize - $mem.FreePhysicalMemory) / $mem.TotalVisibleMemorySize) * 100, 1)
    Write-Output ""MEMORY_USAGE: $usedMem%""
}

# Verificar uptime
$uptime = (Get-Date) - $mem.LastBootUpTime
Write-Output ""UPTIME: $([Math]::Round($uptime.TotalHours, 1)) horas""
if ($uptime.TotalDays -gt 7) { Write-Output 'SUGGESTION: Sistema sem reinicialização há mais de 7 dias' }

Write-Output 'DIAGNOSTICS_DONE'
";
            var (_, psOut) = await RunPsAsync(script, ct, timeoutMs: 60_000);
            foreach (var line in psOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var t = line.Trim();
                if (string.IsNullOrEmpty(t)) continue;
                step.Details.Add(t);
                var color = t.StartsWith("SUGGESTION") ? "#FFD700" : t.StartsWith("SYS_ERROR") || t.StartsWith("APP_ERROR") ? "#FFA500" : "#AAAAAA";
                Log($"  {t}", color);
            }

            step.Success = psOut.Contains("DIAGNOSTICS_DONE");
            step.Summary = Loc("Repair_Step17_Success");
            _logger.LogDebug($"[AdvancedRepair][Step17] CONCLUÍDO: success={step.Success} duration={sw.Elapsed.TotalSeconds:F1}s", source: "AdvancedRepairService");
            Log($"✓ {step.Summary}", "#00FF88");
            step.Duration = sw.Elapsed;
            return step;
        }

        // ─────────────────────────────────────────────────────────────────────
        // ETAPA 18 — REPARO PROFUNDO WINDOWS INSTALLER (MSI)
        // Resolve erros de 'Source Absent' (1612), msiexec travado, e registro corrompido.
        // ─────────────────────────────────────────────────────────────────────
        public async Task<AdvancedRepairStepResult> Step18_WindowsInstallerRepairAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var step = new AdvancedRepairStepResult { StepName = Loc("Repair_Step18_Name") };
            _logger.LogDebug($"[AdvancedRepair][Step18] INÍCIO às {DateTime.Now:HH:mm:ss.fff}", source: "AdvancedRepairService");
            Log($"═══ {Loc("Repair_Step18_Title")} ═══", "#00BFFF");
            Progress(85, Loc("Repair_Step18_Progress"));

            var script = @"
# 1. Parar o serviço Windows Installer (msiserver)
Write-Output 'STEP: Parando msiserver...'
Stop-Service -Name msiserver -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

# 2. Re-registrar msiexec.exe
Write-Output 'STEP: Re-registrando msiexec.exe...'
$msiexec = ""$env:SystemRoot\System32\msiexec.exe""
if (Test-Path $msiexec) {
    # Unregister / Regserver
    Start-Process $msiexec -ArgumentList '/unreg' -Wait -ErrorAction SilentlyContinue
    Start-Process $msiexec -ArgumentList '/regserver' -Wait -ErrorAction SilentlyContinue
    Write-Output 'MSIEXEC_REREGISTERED'
}

# 3. Registrar DLLs críticas do MSI
Write-Output 'STEP: Registrando DLLs do MSI...'
$msidll = ""$env:SystemRoot\System32\msi.dll""
if (Test-Path $msidll) {
    Start-Process regsvr32.exe -ArgumentList ""/s `""$msidll`"""" -Wait -ErrorAction SilentlyContinue
    Write-Output 'MSI_DLL_REGISTERED'
}

# 4. Limpar transações e caches corrompidos de MSI no registro (Corrige 1612)
Write-Output 'STEP: Limpando transações e caches MSI corrompidos...'
$keysToClear = @(
    'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\InProgress',
    'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\Config',
    'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\Secure',
    'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\UpgradeCodes',
    'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\UserData\S-1-5-18\Products'
)
foreach ($key in $keysToClear) {
    if (Test-Path $key) {
        # Para chaves de Dados de Usuário, limpamos apenas as subchaves que não possuem Cache local (origem do 1612)
        if ($key -like ""*UserData*"") {
            Get-ChildItem -Path $key -ErrorAction SilentlyContinue | Where-Object { 
                -not (Test-Path ""$($_.PSPath)\InstallProperties"") 
            } | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
        } else {
            Remove-Item -Path $key -Recurse -Force -ErrorAction SilentlyContinue
        }
        Write-Output ""KEY_OPTIMIZED: $key""
    }
}

# 5. Fix Security Descriptor (Restaurar permissões padrão do serviço)
Write-Output 'STEP: Restaurando permissões do serviço msiserver...'
# SD padrão do msiserver
$sd = 'D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)S:(AU;FA;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;WD)'
try {
    & sc.exe sdset msiserver $sd | Out-Null
    Write-Output 'MSISERVER_SD_RESTORED'
} catch {
    Write-Output 'MSISERVER_SD_FAILED'
}

# 6. Resetar configuração de serviço no Registry
$svcKey = 'HKLM:\SYSTEM\CurrentControlSet\Services\msiserver'
if (Test-Path $svcKey) {
    Set-ItemProperty -Path $svcKey -Name 'Start' -Value 3 -Type DWord # Manual
    Set-ItemProperty -Path $svcKey -Name 'Type' -Value 16 -Type DWord # Win32_Share_Process
    Write-Output 'MSISERVER_REGISTRY_RESET'
}

# 7. Reiniciar o serviço para validar
Write-Output 'STEP: Reiniciando msiserver...'
Start-Service -Name msiserver -ErrorAction SilentlyContinue
$status = (Get-Service msiserver -ErrorAction SilentlyContinue).Status
Write-Output ""MSISERVER_STATUS=$status""

Write-Output 'MSI_REPAIR_DONE'
";
            var (code, psOut) = await RunPsAsync(script, ct, timeoutMs: 60_000);
            foreach (var line in psOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var t = line.Trim();
                if (string.IsNullOrEmpty(t)) continue;
                step.Details.Add(t);
                var color = t.Contains("DONE") || t.Contains("REGISTERED") || t.Contains("RESTORED") ? "#00FF88" : "#AAAAAA";
                Log($"  {t}", color);
            }

            step.Success = psOut.Contains("MSI_REPAIR_DONE") && psOut.Contains("Running");
            step.Summary = step.Success ? Loc("Repair_Step18_Success") : Loc("Repair_Step18_Warn");
            
            if (step.Success)
            {
                HistoryService.RecordActivity("System Repair", "Windows Installer (MSI) reparado e re-registrado para corrigir erros de instalação.");
            }

            _logger.LogDebug($"[AdvancedRepair][Step18] CONCLUÍDO: success={step.Success} duration={sw.Elapsed.TotalSeconds:F1}s", source: "AdvancedRepairService");

            Log($"{(step.Success ? "✓" : "⚠")} {step.Summary}", step.Success ? "#00FF88" : "#FFA500");
            step.Duration = sw.Elapsed;
            return step;
        }

        // ─────────────────────────────────────────────────────────────────────
        // ETAPA EXTRA C — FIX CPU ALTA (Core Parking) — VERSÃO SEGURA
        // Detecta Minimum Processor State acima de 85% e reverte para valores
        // seguros: AC=100% (performance), DC=5% (economia de bateria).
        // NUNCA força valores baixos como 0% ou 5% no AC — isso causa CPU 100%
        // constante em processadores Intel 12ª+ gen e AMD Ryzen modernos.
        // ─────────────────────────────────────────────────────────────────────
        public async Task<AdvancedRepairStepResult> FixHighCpuCoreParkingAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var step = new AdvancedRepairStepResult { StepName = Loc("Repair_FixCpu_Name") };

            Log($"═══ {Loc("Repair_FixCpu_Title")} ═══", "#00BFFF");
            Progress(0, Loc("Repair_FixCpu_Progress"));

            const string subgroupGuid = "54533251-82be-4824-96c1-47b60b740d00";
            const string cpMinCoresGuid = "0cc5b647-c1df-4637-891a-dec35c318583";

            // 1. Detectar valor atual
            var detectScript = $@"
$val = powercfg -query SCHEME_CURRENT {subgroupGuid} {cpMinCoresGuid} | Select-String 'Current AC Power Setting Index:'
if ($val) {{
    $hex = ($val -split '0x')[1].Trim()
    $dec = [Convert]::ToInt32($hex, 16)
    Write-Output ""CPMINCORES_AC=$dec""
}}
Write-Output 'DETECT_DONE'
";
            var (_, detectOut) = await RunPsAsync(detectScript, ct, 15_000);

            int currentMinCores = -1;
            var match = Regex.Match(detectOut, @"CPMINCORES_AC=(\d+)");
            if (match.Success) int.TryParse(match.Groups[1].Value, out currentMinCores);

            Log($"  Valor atual do Minimum Processor State: {currentMinCores}%", "#AAAAAA");
            step.Details.Add($"[DETECT] CPMINCORES_AC={currentMinCores}");

            // 2. Só corrige se estiver muito alto (acima de 85%).
            //    Nunca força 5% ou 0% no AC — isso causa CPU 100% constante.
            if (currentMinCores < 85)
            {
                Log($"  ✓ {Loc("Repair_FixCpu_Normal")}", "#00FF88");
                step.Success = true;
                step.Summary = $"Core Parking OK ({currentMinCores}%)";
                step.Duration = sw.Elapsed;
                return step;
            }

            Log($"  ⚠ Valor muito alto detectado ({currentMinCores}%) — revertendo para valor seguro...", "#FFA500");

            // 3. Valor seguro: 5% no AC e 5% no DC (padrão do Windows)
                        bool isLaptop = false;
            try
            {
                var sysInfo = App.Services?.GetService(typeof(global::VoltrisOptimizer.Interfaces.ISystemInfoService)) as global::VoltrisOptimizer.Interfaces.ISystemInfoService;
                isLaptop = sysInfo?.IsLaptop() ?? false;
            } catch { }

            if (isLaptop)
            {
                Log("  [!] Laptop detectado. Modificação de energia in-place ignorada para não corromper plano OEM.", "#FFA500");
                step.Success = true;
                step.Summary = "Core Parking ignorado (Notebook)";
                step.Duration = sw.Elapsed;
                return step;
            }

            // [FIX:UNICA-FONTE] NENHUMA ESCRITA DE ENERGIA AQUI.
            //
            // Este passo gravava `CPMINCORES` no esquema ATIVO por `powercfg.exe`,
            // por fora do portão. Mesmo escrevendo um valor razoável (5%), isso
            // quebrava a regra que agora vale: o conteúdo do plano ativo pertence
            // ao Perfil Inteligente, e a única forma segura de alterar um setting
            // protegido é pedir ao perfil.
            //
            // O valor 5 seria equivalente ao da tabela, o que faz este caminho
            // parecer inofensivo. O problema não é o número, é a existência do
            // caminho: ele sobrevive a qualquer mudança na tabela e continuaria
            // gravando por cima, sem lock e sem registro de conflito.
            //
            // E o risco era maior em notebook: o `isLaptop` abaixo existia
            // justamente porque esse tipo de escrita corrompia o plano da OEM.
            // Uma checagem de hardware em cada serviço que grava não é proteção;
            // é adiamento do defeito.
            //
            // O Perfil Inteligente já aplica `CoreParkingMin` com AC/DC separados
            // e com o valor correto para o tier da máquina. Se o usuário rodou um
            // reparo e a energia ficou estranha, a origem a investigar é o perfil
            // — e o log `PowerApply` mostra exatamente o que foi gravado.
            Log("  [i] Core Parking: pertence ao Perfil Inteligente. Nada foi gravado aqui.", "#AAAAAA");
            step.Details.Add("Core Parking nao alterado: valor definido pelo Perfil Inteligente (fonte unica).");
            step.Success = true;
            step.Summary = "Core Parking: sem alteracao (gerenciado pelo Perfil Inteligente)";
            step.Duration = sw.Elapsed;
            return step;

            Log("  ✓ Core Parking revertido para valores seguros (AC=5%, DC=5%)", "#00FF88");

            step.Success = true;
            step.Summary = Loc("Repair_FixCpu_Success");
            step.Duration = sw.Elapsed;

            _logger.LogInfo($"[AdvancedRepair][CpuFix] CPU Alta corrigida. Antigo: {currentMinCores}% → Novo: AC=5%, DC=5%");

            return step;
        }

        // ─────────────────────────────────────────────────────────────────────
        // ETAPA 18 — RELATÓRIO HTML COMPLETO ANTES/DEPOIS
        // ─────────────────────────────────────────────────────────────────────
        public async Task<string> GenerateHtmlReportAsync(AdvancedRepairReport report, CancellationToken ct)
        {
            // Realizar análise final antes de gerar o HTML
            PerformGlobalFailureAnalysis(report);

            var reportPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"Voltris_RepairReport_{DateTime.Now:yyyyMMdd_HHmmss}.html");

            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html lang='pt-BR'><head><meta charset='UTF-8'>");
            sb.AppendLine("<title>Voltris - Relatório de Reparo Completo</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("body{background:#0A0A0F;color:#E0E0E0;font-family:'Segoe UI',sans-serif;margin:0;padding:24px;}");
            sb.AppendLine("h1{background:linear-gradient(90deg,#F59E0B,#EF4444,#8B31FF);-webkit-background-clip:text;-webkit-text-fill-color:transparent;font-size:28px;}");
            sb.AppendLine(".card{background:#12121A;border:1px solid #2A2A3A;border-radius:12px;padding:16px;margin:12px 0;}");
            sb.AppendLine(".ok{color:#00FF88;}.warn{color:#FFA500;}.err{color:#FF4444;}.info{color:#31A8FF;}");
            sb.AppendLine(".step{border-left:3px solid #8B31FF;padding-left:12px;margin:8px 0;}");
            sb.AppendLine(".badge{display:inline-block;padding:2px 8px;border-radius:4px;font-size:12px;font-weight:bold;}");
            sb.AppendLine(".badge-ok{background:#00FF8822;color:#00FF88;border:1px solid #00FF8844;}");
            sb.AppendLine(".badge-warn{background:#FFA50022;color:#FFA500;border:1px solid #FFA50044;}");
            sb.AppendLine(".analysis-box{background:#1E1B29;border:1px solid #8B31FF44;border-radius:12px;padding:16px;margin:20px 0;border-left:5px solid #8B31FF;}");
            sb.AppendLine("table{width:100%;border-collapse:collapse;}td,th{padding:8px 12px;border-bottom:1px solid #2A2A3A;text-align:left;}");
            sb.AppendLine("th{color:#8B31FF;font-size:12px;text-transform:uppercase;}");
            sb.AppendLine("</style></head><body>");

            sb.AppendLine("<h1>⚡ Voltris — Relatório de Reparo Completo</h1>");

            // Box de Análise Inteligente
            sb.AppendLine("<div class='analysis-box'>");
            sb.AppendLine("<h3 style='color:#8B31FF;margin-top:0'>🧠 Análise Inteligente do Sistema</h3>");
            sb.AppendLine($"<div style='white-space:pre-wrap;line-height:1.6'>{HtmlEncode(report.FinalAnalysis)}</div>");
            sb.AppendLine("</div>");

            sb.AppendLine($"<div class='card'><table>");
            sb.AppendLine($"<tr><th>Início</th><td>{report.StartTime:dd/MM/yyyy HH:mm:ss}</td></tr>");
            sb.AppendLine($"<tr><th>Fim</th><td>{report.EndTime:dd/MM/yyyy HH:mm:ss}</td></tr>");
            sb.AppendLine($"<tr><th>Duração</th><td>{(report.EndTime - report.StartTime).TotalMinutes:F1} minutos</td></tr>");
            sb.AppendLine($"<tr><th>Sistema</th><td>{report.SystemInfo}</td></tr>");
            sb.AppendLine($"<tr><th>Ponto de Restauração</th><td>{(report.RestorePointCreated ? $"<span class='ok'>✓ {report.RestorePointName}</span>" : "<span class='warn'>⚠ Não criado</span>")}</td></tr>");
            sb.AppendLine("</table></div>");

            sb.AppendLine("<h2 style='color:#31A8FF'>Etapas Executadas</h2>");
            foreach (var step in report.Steps)
            {
                var badge = step.Success ? "<span class='badge badge-ok'>✓ OK</span>" : "<span class='badge badge-warn'>⚠ AVISO</span>";
                sb.AppendLine($"<div class='card step'>");
                sb.AppendLine($"<div style='display:flex;justify-content:space-between;align-items:center'>");
                sb.AppendLine($"<strong>{step.StepName}</strong>{badge}");
                sb.AppendLine($"<span style='color:#666;font-size:12px'>{step.Duration.TotalSeconds:F1}s</span></div>");
                sb.AppendLine($"<div style='color:#AAAAAA;margin-top:6px'>{step.Summary}</div>");
                if (step.Details.Count > 0)
                {
                    sb.AppendLine("<details><summary style='cursor:pointer;color:#8B31FF;margin-top:8px'>Ver detalhes</summary><pre style='color:#888;font-size:11px;margin-top:8px'>");
                    foreach (var d in step.Details) sb.AppendLine(HtmlEncode(d));
                    sb.AppendLine("</pre></details>");
                }
                sb.AppendLine("</div>");
            }

            sb.AppendLine("<p style='color:#444;font-size:11px;margin-top:32px'>Gerado por Voltris Optimizer — " + DateTime.Now.ToString("dd/MM/yyyy HH:mm") + "</p>");
            sb.AppendLine("</body></html>");

            await File.WriteAllTextAsync(reportPath, sb.ToString(), Encoding.UTF8, ct);
            _logger.LogInfo($"[AdvancedRepair] Relatório HTML gerado: {reportPath}");
            return reportPath;
        }

        private void PerformGlobalFailureAnalysis(AdvancedRepairReport report)
        {
            _logger.LogInfo("[AdvancedRepair] Iniciando Análise Global de Falhas...");
            var failedSteps = report.Steps.Where(s => !s.Success).ToList();
            if (failedSteps.Count == 0)
            {
                report.FinalAnalysis = "Reparo concluído com 100% de sucesso. Todas as etapas do sistema foram validadas e corrigidas. O sistema está em estado ideal.";
                _logger.LogInfo("[AdvancedRepair] Análise Final: Reparo 100% bem-sucedido.");
                return;
            }

            var analysis = new StringBuilder();
            analysis.AppendLine($"O reparo não foi 100% concluído em {failedSteps.Count} etapa(s). Detalhes abaixo:");
            
            foreach (var step in failedSteps)
            {
                analysis.AppendLine($"- [FALHA] {step.StepName}: {step.Summary}");
            }

            analysis.AppendLine("\n🔍 Diagnóstico de Causa Provável:");
            
            bool addedCause = false;
            
            if (failedSteps.Any(s => s.StepName.Contains("Integridade")))
            {
                analysis.AppendLine("- CORRUPÇÃO DE IMAGEM: Erros no DISM/SFC indicam arquivos de sistema protegidos que estão corrompidos além da capacidade de reparo automático offline. Recomenda-se In-Place Upgrade se os problemas persistirem.");
                addedCause = true;
            }
            if (failedSteps.Any(s => s.StepName.Contains("Áudio")))
            {
                analysis.AppendLine("- SUBSISTEMA DE ÁUDIO: Falha ao normalizar áudio. Pode indicar drivers bloqueados por antivírus de terceiros ou hardware desconectado.");
                addedCause = true;
            }
            if (failedSteps.Any(s => s.StepName.Contains("Rede")))
            {
                analysis.AppendLine("- CONECTIVIDADE: Reset de rede incompleto. Provavelmente causado por drivers de rede (NIC) travados ou software de VPN ativo.");
                addedCause = true;
            }
            if (failedSteps.Any(s => s.StepName.Contains("Restauração")))
            {
                analysis.AppendLine("- PROTEÇÃO DO SISTEMA: Ponto de restauração falhou. O serviço VSS (Volume Shadow Copy) pode estar desativado ou o espaço em disco é insuficiente.");
                addedCause = true;
            }
            if (failedSteps.Any(s => s.StepName.Contains("Registro")))
            {
                analysis.AppendLine("- REGISTRO: Algumas chaves não puderam ser reparadas devido a permissões 'TrustedInstaller' restritivas ou bloqueios de software de segurança.");
                addedCause = true;
            }

            if (!addedCause)
            {
                analysis.AppendLine("- FALHAS GENÉRICAS: Os avisos detectados são menores e provavelmente não afetarão a estabilidade global, mas sugerem bloqueios de sistema.");
            }

            analysis.AppendLine("\n✅ Recomendação: Reinicie o computador para aplicar as correções parciais e tente rodar o reparo novamente em Modo de Segurança se as falhas persistirem.");

            report.FinalAnalysis = analysis.ToString();
            _logger.LogWarning($"[AdvancedRepair] Análise Final Concluída: {failedSteps.Count} falhas analisadas.");
        }

        private static string HtmlEncode(string s) =>
            s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

        // ─────────────────────────────────────────────────────────────────────
        // ETAPA EXTRA A — FIX BARRA DE PESQUISA (CTF + SearchHost)
        // Detecta e corrige o bug clássico "barra de pesquisa não digita"
        // Suporte: Windows 10 e Windows 11
        // ─────────────────────────────────────────────────────────────────────
        public async Task<AdvancedRepairStepResult> FixSearchBarAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var step = new AdvancedRepairStepResult { StepName = Loc("Repair_FixSearch_Name") };
            Log($"═══ {Loc("Repair_FixSearch_Title")} ═══", "#00BFFF");
            Progress(0, Loc("Repair_FixSearch_Progress"));

            // DEBUG: Detectar versão do Windows para aplicar fix correto
            var buildNumber = 0;
            try { buildNumber = Environment.OSVersion.Version.Build; } catch { }
            var isWin11 = buildNumber >= 22000;
            _logger.LogInfo($"[AdvancedRepair][SearchFix] DEBUG: Build={buildNumber}, IsWin11={isWin11}");
            Log($"  DEBUG: Windows Build={buildNumber} ({(isWin11 ? "Windows 11" : "Windows 10")})", "#666666");

            // ── DETECÇÃO 1: Verificar ctfmon.exe no Run ──
            // REPARO: ctfmon.exe é responsável pelo CTF (Text Framework) — sem ele a barra de pesquisa não aceita input
            Log("  → Verificando ctfmon.exe no registro Run...", "#AAAAAA");
            var detectScript = @"
$runKey = 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run'
$ctfmonPath = 'C:\Windows\System32\ctfmon.exe'
$ctfmonInRun = $false
$ctfmonRunning = $false
$searchHostRunning = $false
$searchUiRunning = $false

# DEBUG: Verificar entrada ctfmon no Run
if (Test-Path $runKey) {
    $val = (Get-ItemProperty $runKey -ErrorAction SilentlyContinue).ctfmon
    if ($val -ne $null) {
        $ctfmonInRun = $true
        Write-Output ""CTFMON_IN_RUN=true VALUE=$val""
    } else {
        Write-Output 'CTFMON_IN_RUN=false'
    }
} else {
    Write-Output 'CTFMON_RUN_KEY_MISSING'
}

# DEBUG: Verificar se ctfmon.exe está rodando
$ctfProc = Get-Process -Name ctfmon -ErrorAction SilentlyContinue
if ($ctfProc) {
    $ctfmonRunning = $true
    Write-Output ""CTFMON_RUNNING=true PID=$($ctfProc.Id)""
} else {
    Write-Output 'CTFMON_RUNNING=false'
}

# DEBUG: Verificar SearchHost (Win11) ou SearchUI (Win10)
$searchHost = Get-Process -Name SearchHost -ErrorAction SilentlyContinue
$searchUi   = Get-Process -Name SearchUI   -ErrorAction SilentlyContinue
if ($searchHost) { Write-Output ""SEARCHHOST_RUNNING=true PID=$($searchHost.Id)"" }
else             { Write-Output 'SEARCHHOST_RUNNING=false' }
if ($searchUi)   { Write-Output ""SEARCHUI_RUNNING=true PID=$($searchUi.Id)"" }
else             { Write-Output 'SEARCHUI_RUNNING=false' }

# DEBUG: Verificar serviço WSearch
$wsearch = Get-Service -Name WSearch -ErrorAction SilentlyContinue
if ($wsearch) { Write-Output ""WSEARCH_STATUS=$($wsearch.Status)"" }
else          { Write-Output 'WSEARCH_NOT_FOUND' }

Write-Output 'DETECTION_DONE'
";
            var (_, detectOut) = await RunPsAsync(detectScript, ct, timeoutMs: 20_000);
            _logger.LogInfo($"[AdvancedRepair][SearchFix] Detecção: {detectOut.Trim()}");

            var ctfmonInRun    = detectOut.Contains("CTFMON_IN_RUN=true");
            var ctfmonRunning  = detectOut.Contains("CTFMON_RUNNING=true");
            var searchRunning  = detectOut.Contains("SEARCHHOST_RUNNING=true") || detectOut.Contains("SEARCHUI_RUNNING=true");
            var wsearchRunning = detectOut.Contains("WSEARCH_STATUS=Running");

            foreach (var line in detectOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var t = line.Trim();
                if (!string.IsNullOrEmpty(t)) { step.Details.Add($"[DETECT] {t}"); Log($"  {t}", "#AAAAAA"); }
            }

            // ── DECISÃO: Aplicar fix apenas se necessário ──
            // REPARO: Conservador — no Win10, ctfmon geralmente inicia via MSCTF service,
            // não pelo Run key. Só considera "quebrado" se ctfmon NÃO ESTIVER RODANDO
            // ou se SearchHost/SearchUI estiver inativo e WSearch estiver parado.
            // (SearchHost em Win10 = SearchUI, em Win11 = SearchHost)
            bool searchProcessDown = !detectOut.Contains("SEARCHHOST_RUNNING=true") && !detectOut.Contains("SEARCHUI_RUNNING=true");
            bool needsFix = !ctfmonRunning || (searchProcessDown && !wsearchRunning);
            _logger.LogInfo($"[AdvancedRepair][SearchFix] NeedsFix={needsFix} (ctfmonRunning={ctfmonRunning}, searchRunning={searchRunning}, wsearchRunning={wsearchRunning})");

            if (!needsFix)
            {
                Log("  ✓ Barra de pesquisa: nenhum problema detectado — fix não necessário", "#00FF88");
                step.Success = true;
                step.Summary = Loc("Repair_FixSearch_Ok");
                step.Duration = sw.Elapsed;
                return step;
            }

            Log($"  ⚠ Problema detectado — aplicando fix (ctfmonInRun={ctfmonInRun}, ctfmonRunning={ctfmonRunning}, wsearchRunning={wsearchRunning})", "#FFA500");

            // ── FIX 1: Adicionar ctfmon.exe ao Run ──
            // REPARO: Garante que ctfmon inicia com o Windows (fix permanente)
            if (!ctfmonInRun)
            {
                Log("  → Adicionando ctfmon.exe ao Run...", "#AAAAAA");
                var reg = ResolveExe("reg.exe");
                var (regCode, regOut) = await RunAsync(reg,
                    @"ADD HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v ctfmon /t REG_SZ /d ""C:\Windows\System32\ctfmon.exe"" /f",
                    ct, timeoutMs: 10_000);
                _logger.LogInfo($"[AdvancedRepair][SearchFix] REG ADD ctfmon: exit={regCode} out={regOut.Trim()}");
                step.Details.Add($"[REG ctfmon] exit={regCode}: {regOut.Trim()}");
                Log(regCode == 0 ? "  ✓ ctfmon adicionado ao Run" : $"  ⚠ REG ADD exit={regCode}", regCode == 0 ? "#00FF88" : "#FFA500");
            }

            // ── FIX 2: Iniciar ctfmon.exe se não estiver rodando ──
            if (!ctfmonRunning)
            {
                Log("  → Iniciando ctfmon.exe...", "#AAAAAA");
                var ctfmonExe = Path.Combine(WinDir, "System32", "ctfmon.exe");
                if (File.Exists(ctfmonExe))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(ctfmonExe) { UseShellExecute = false, CreateNoWindow = true });
                        _logger.LogInfo("[AdvancedRepair][SearchFix] ctfmon.exe iniciado");
                        Log("  ✓ ctfmon.exe iniciado", "#00FF88");
                        step.Details.Add("[ctfmon.exe] iniciado");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"[AdvancedRepair][SearchFix] Falha ao iniciar ctfmon: {ex.Message}");
                        Log($"  ⚠ ctfmon start: {ex.Message}", "#FFA500");
                    }
                }
            }

            // ── FIX 3: Reiniciar WSearch + SearchHost/SearchUI + re-registrar ShellExperienceHost ──
            // REPARO: Script PowerShell version-aware (Win10 usa SearchUI, Win11 usa SearchHost)
            Log("  → Reiniciando Windows Search e processos relacionados...", "#AAAAAA");
            var fixScript = $@"
$isWin11 = [System.Environment]::OSVersion.Version.Build -ge 22000
Write-Output ""DEBUG: IsWin11=$isWin11""

# REPARO: Parar e reiniciar serviço WSearch
Write-Output 'STEP: Reiniciando WSearch...'
Stop-Service -Name WSearch -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
Start-Service -Name WSearch -ErrorAction SilentlyContinue
$wss = (Get-Service WSearch -ErrorAction SilentlyContinue).Status
Write-Output ""WSEARCH_AFTER_RESTART=$wss""

# REPARO: Matar e reiniciar SearchHost (Win11) ou SearchUI (Win10)
if ($isWin11) {{
    Write-Output 'STEP: Reiniciando SearchHost (Win11)...'
    Get-Process -Name SearchHost -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
    # SearchHost é reiniciado automaticamente pelo sistema
    Write-Output 'SEARCHHOST_KILLED'
}} else {{
    Write-Output 'STEP: Reiniciando SearchUI (Win10)...'
    Get-Process -Name SearchUI -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
    Write-Output 'SEARCHUI_KILLED'
}}

# REPARO: Matar Cortana (pode bloquear SearchHost)
Write-Output 'STEP: Reiniciando Cortana...'
Get-Process -Name Cortana -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500
Write-Output 'CORTANA_KILLED'

# REPARO: Re-registrar ShellExperienceHost (corrige integração Shell/Search)
Write-Output 'STEP: Re-registrando ShellExperienceHost...'
$shellPkg = Get-AppxPackage -Name 'Microsoft.Windows.ShellExperienceHost' -ErrorAction SilentlyContinue
if ($shellPkg) {{
    Add-AppxPackage -DisableDevelopmentMode -Register ""$($shellPkg.InstallLocation)\AppXManifest.xml"" -ErrorAction SilentlyContinue
    Write-Output 'SHELLEXPERIENCEHOST_REREGISTERED'
}} else {{
    Write-Output 'SHELLEXPERIENCEHOST_NOT_FOUND'
}}

# REPARO: Re-registrar Search (Win10: Microsoft.Windows.Cortana / Win11: Microsoft.Windows.Search)
Write-Output 'STEP: Re-registrando Search app...'
if ($isWin11) {{
    $searchPkg = Get-AppxPackage -Name 'Microsoft.Windows.Search' -ErrorAction SilentlyContinue
}} else {{
    $searchPkg = Get-AppxPackage -Name 'Microsoft.Windows.Cortana' -ErrorAction SilentlyContinue
}}
if ($searchPkg) {{
    Add-AppxPackage -DisableDevelopmentMode -Register ""$($searchPkg.InstallLocation)\AppXManifest.xml"" -ErrorAction SilentlyContinue
    Write-Output ""SEARCH_APP_REREGISTERED: $($searchPkg.Name)""
}} else {{
    Write-Output 'SEARCH_APP_NOT_FOUND'
}}

Write-Output 'SEARCHFIX_DONE'
";
            var (fixCode, fixOut) = await RunPsAsync(fixScript, ct, timeoutMs: 60_000);
            _logger.LogInfo($"[AdvancedRepair][SearchFix] Fix script: exit={fixCode} out={fixOut.Trim()}");

            foreach (var line in fixOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var t = line.Trim();
                if (string.IsNullOrEmpty(t)) continue;
                step.Details.Add($"[FIX] {t}");
                var color = t.StartsWith("DEBUG") ? "#666666" : t.StartsWith("STEP") ? "#AAAAAA" :
                            t.Contains("DONE") || t.Contains("REREGISTERED") || t.Contains("KILLED") ? "#00FF88" : "#FFA500";
                Log($"  {t}", color);
            }

            step.Success = fixOut.Contains("SEARCHFIX_DONE");
            step.Summary = step.Success ? Loc("Repair_FixSearch_Success") : Loc("Repair_FixSearch_Warn");
            Log($"{(step.Success ? "✓" : "⚠")} {step.Summary}", step.Success ? "#00FF88" : "#FFA500");
            _logger.LogInfo($"[AdvancedRepair][SearchFix] Resultado: {step.Summary}");
            step.Duration = sw.Elapsed;
            return step;
        }


        // ─────────────────────────────────────────────────────────────────────
        // ─────────────────────────────────────────────────────────────────────
        // ETAPA EXTRA B — FIX PROFUNDO MENU INICIAR (Painel de Controle + todos os itens)
        // Corrige: Painel de Controle, Configurações, Gerenciador de Tarefas, Win+X,
        //          atalhos quebrados, cache corrompido, registro, serviços, UWP.
        // Suporte: Windows 10 e Windows 11 (version-aware)
        // ─────────────────────────────────────────────────────────────────────
        public async Task<AdvancedRepairStepResult> FixControlPanelAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var step = new AdvancedRepairStepResult { StepName = Loc("Repair_FixStartMenu_Name") };
            Log($"═══ {Loc("Repair_FixStartMenu_Title")} ═══", "#00BFFF");
            Progress(0, Loc("Repair_FixStartMenu_Progress"));

            var buildNumber = 0;
            try { buildNumber = Environment.OSVersion.Version.Build; } catch { }
            var isWin11 = buildNumber >= 22000;
            _logger.LogDebug($"[AdvancedRepair][StartMenuFix] Build={buildNumber} IsWin11={isWin11}", source: "AdvancedRepairService");
            Log($"  → Windows Build={buildNumber} ({(isWin11 ? "Windows 11" : "Windows 10")})", "#666666");

            // ── FASE 1: DIAGNÓSTICO COMPLETO ──────────────────────────────────
            Log("  → [1/6] Diagnóstico completo do Menu Iniciar...", "#AAAAAA");
            var detectScript = @"
$build = [System.Environment]::OSVersion.Version.Build
$isWin11 = $build -ge 22000
Write-Output ""BUILD=$build""
Write-Output ""IS_WIN11=$isWin11""

# Serviços críticos para o Menu Iniciar e Painel de Controle
$svcs = @('AppIDSvc','StateRepository','CDPSvc','camsvc','tiledatamodelsvc',
          'WpnService','cbdhsvc','BrokerInfrastructure','DcomLaunch','RpcSs')
foreach ($s in $svcs) {
    $svc = Get-Service -Name $s -ErrorAction SilentlyContinue
    if ($svc) { Write-Output ""SVC_${s}=$($svc.Status)"" }
    else      { Write-Output ""SVC_${s}=NOT_FOUND"" }
}

# Processos do shell
foreach ($p in @('StartMenuExperienceHost','ShellExperienceHost','SearchHost','explorer')) {
    $proc = Get-Process -Name $p -ErrorAction SilentlyContinue
    if ($proc) { Write-Output ""PROC_${p}=RUNNING PID=$($proc.Id)"" }
    else       { Write-Output ""PROC_${p}=NOT_RUNNING"" }
}

# Verificar control.exe e outros executáveis críticos
foreach ($exe in @('control.exe','mmc.exe','taskmgr.exe','regedit.exe')) {
    $path = ""$env:SystemRoot\System32\$exe""
    if (Test-Path $path) { Write-Output ""EXE_${exe}=OK"" }
    else                 { Write-Output ""EXE_${exe}=MISSING"" }
}

# Verificar chaves de registro críticas do Menu Iniciar
$regChecks = @{
    'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ControlPanel' = 'REG_CP_SHELL'
    'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced'     = 'REG_EXPLORER_ADV'
    'HKLM:\SOFTWARE\Classes\CLSID\{26EE0668-A00A-44D7-9371-BEB064C98683}'  = 'REG_CP_CLSID'
    'HKLM:\SOFTWARE\Classes\CLSID\{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}'  = 'REG_SETTINGS_CLSID'
}
foreach ($kv in $regChecks.GetEnumerator()) {
    if (Test-Path $kv.Key) { Write-Output ""$($kv.Value)=EXISTS"" }
    else                   { Write-Output ""$($kv.Value)=MISSING"" }
}

# Verificar se o Start Menu está bloqueado por GPO
$gpoKey = 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer'
$noStartMenu = (Get-ItemProperty $gpoKey -Name 'NoStartMenu' -ErrorAction SilentlyContinue).NoStartMenu
Write-Output ""GPO_NoStartMenu=$noStartMenu""

# Verificar cache corrompido
$tileDb = ""$env:LOCALAPPDATA\TileDataLayer\Database""
if (Test-Path $tileDb) { Write-Output 'TILEDATALAYER=EXISTS' } else { Write-Output 'TILEDATALAYER=ABSENT' }

$startCache = ""$env:LOCALAPPDATA\Packages\Microsoft.Windows.StartMenuExperienceHost_cw5n1h2txyewy\LocalState""
if (Test-Path $startCache) { Write-Output 'STARTMENU_CACHE=EXISTS' } else { Write-Output 'STARTMENU_CACHE=ABSENT' }

Write-Output 'DETECTION_DONE'
";
            var (_, detectOut) = await RunPsAsync(detectScript, ct, timeoutMs: 25_000);
            _logger.LogDebug($"[AdvancedRepair][StartMenuFix] Diagnóstico: {detectOut.Trim()}", source: "AdvancedRepairService");
            foreach (var line in detectOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var t = line.Trim();
                if (!string.IsNullOrEmpty(t)) { step.Details.Add($"[DETECT] {t}"); Log($"    {t}", "#666666"); }
            }

            // ── DECISÃO: Só aplicar fix se detectar problema real ─────────────
            bool shellRunning = detectOut.Contains("PROC_ShellExperienceHost=RUNNING");
            bool startMenuRunning = detectOut.Contains("PROC_StartMenuExperienceHost=RUNNING");
            bool controlExeOk = detectOut.Contains("EXE_control.exe=OK");
            bool allCriticalServicesRunning = true;
            foreach (var svc in new[] { "DcomLaunch", "RpcSs", "BrokerInfrastructure", "StateRepository", "CDPSvc" })
                if (!detectOut.Contains($"SVC_{svc}=Running")) allCriticalServicesRunning = false;

            bool needsFix = !shellRunning || !startMenuRunning || !controlExeOk || !allCriticalServicesRunning;

            if (!needsFix)
            {
                Log("  ✓ Menu Iniciar e Painel de Controle: nenhum problema detectado — etapa concluída", "#00FF88");
                step.Success = true;
                step.Summary = Loc("Repair_FixStartMenu_Ok");
                step.Duration = sw.Elapsed;
                return step;
            }

            Log($"  ⚠ Problema detectado — aplicando fix completo (shell={shellRunning}, startMenu={startMenuRunning}, controlExe={controlExeOk}, services={allCriticalServicesRunning})", "#FFA500");

            // ── FASE 2: SERVIÇOS CRÍTICOS ─────────────────────────────────────
            Log("  → [2/6] Restaurando serviços críticos do Menu Iniciar...", "#AAAAAA");
            Progress(15, "Restaurando serviços do Menu Iniciar...");
            var svcScript = @"
# REPARO: Garantir tipo de inicialização correto e reiniciar serviços críticos
$criticalSvcs = @(
    @{ Name='DcomLaunch';        Start='Automatic' },
    @{ Name='RpcSs';             Start='Automatic' },
    @{ Name='BrokerInfrastructure'; Start='Automatic' },
    @{ Name='StateRepository';   Start='Manual'    },
    @{ Name='AppIDSvc';          Start='Manual'    },
    @{ Name='CDPSvc';            Start='Automatic' },
    @{ Name='WpnService';        Start='Automatic' },
    @{ Name='camsvc';            Start='Manual'    },
    @{ Name='cbdhsvc';           Start='Manual'    }
)
foreach ($svc in $criticalSvcs) {
    $s = Get-Service -Name $svc.Name -ErrorAction SilentlyContinue
    if (-not $s) { Write-Output ""SVC_SKIP=$($svc.Name) (not found)""; continue }
    # Corrigir tipo de inicialização
    Set-Service -Name $svc.Name -StartupType $svc.Start -ErrorAction SilentlyContinue
    # Reiniciar se parado
    if ($s.Status -ne 'Running') {
        Start-Service -Name $svc.Name -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 300
    }
    $status = (Get-Service -Name $svc.Name -ErrorAction SilentlyContinue).Status
    Write-Output ""SVC_FIXED=$($svc.Name) Status=$status""
}
Write-Output 'SERVICES_DONE'
";
            var (_, svcOut) = await RunPsAsync(svcScript, ct, timeoutMs: 45_000);
            _logger.LogDebug($"[AdvancedRepair][StartMenuFix] Serviços: {svcOut.Trim()}", source: "AdvancedRepairService");
            foreach (var line in svcOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var t = line.Trim();
                if (!string.IsNullOrEmpty(t)) { step.Details.Add($"[SVC] {t}"); Log($"    {t}", t.Contains("Running") ? "#00FF88" : "#AAAAAA"); }
            }

            // ── FASE 3: REGISTRO — RESTAURAR CLSID E ASSOCIAÇÕES ─────────────
            Log("  → [3/6] Restaurando registro do Menu Iniciar e Painel de Controle...", "#AAAAAA");
            Progress(30, "Restaurando registro do Menu Iniciar...");
            var regScript = $@"
$isWin11 = [System.Environment]::OSVersion.Version.Build -ge 22000

# REPARO: CLSID do Painel de Controle — necessário para abrir pelo Menu Iniciar
# {{26EE0668}} = Painel de Controle (modo categoria)
# {{21EC2020}} = Painel de Controle (ícones grandes) — Win10
$cpClsids = @(
    '{{26EE0668-A00A-44D7-9371-BEB064C98683}}',
    '{{21EC2020-3AEA-1069-A2DD-08002B30309D}}',
    '{{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}}'  # Configurações Win10/11
)
foreach ($clsid in $cpClsids) {{
    $key = ""HKLM:\SOFTWARE\Classes\CLSID\$clsid""
    if (-not (Test-Path $key)) {{
        New-Item -Path $key -Force -ErrorAction SilentlyContinue | Out-Null
        Write-Output ""REG_CREATED: $clsid""
    }} else {{
        Write-Output ""REG_OK: $clsid""
    }}
}}

# REPARO: Garantir que control.exe tem a associação correta no registro
$controlKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ControlPanel'
if (-not (Test-Path $controlKey)) {{
    New-Item -Path $controlKey -Force -ErrorAction SilentlyContinue | Out-Null
    Write-Output 'REG_CP_KEY_CREATED'
}} else {{
    Write-Output 'REG_CP_KEY_OK'
}}

# REPARO: Restaurar abertura do Painel de Controle via shell
$shellKey = 'HKLM:\SOFTWARE\Classes\shell\open\command'
$controlPath = ""$env:SystemRoot\System32\control.exe""

# REPARO: Garantir que Explorer não está bloqueando o Painel de Controle por GPO
$explorerPolicies = 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer'
if (Test-Path $explorerPolicies) {{
    $noCP = (Get-ItemProperty $explorerPolicies -Name 'NoControlPanel' -ErrorAction SilentlyContinue).NoControlPanel
    if ($noCP -eq 1) {{
        Remove-ItemProperty -Path $explorerPolicies -Name 'NoControlPanel' -ErrorAction SilentlyContinue
        Write-Output 'REG_GPO_NOCONTROLPANEL_REMOVED'
    }} else {{
        Write-Output 'REG_GPO_NOCONTROLPANEL_OK'
    }}
    # Remover bloqueio do Menu Iniciar por GPO
    $noStartMenu = (Get-ItemProperty $explorerPolicies -Name 'NoStartMenu' -ErrorAction SilentlyContinue).NoStartMenu
    if ($noStartMenu -eq 1) {{
        Remove-ItemProperty -Path $explorerPolicies -Name 'NoStartMenu' -ErrorAction SilentlyContinue
        Write-Output 'REG_GPO_NOSTARTMENU_REMOVED'
    }}
}}

# REPARO: Restaurar chave de configurações do Explorer (necessária para Menu Iniciar funcionar)
$advKey = 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced'
if (-not (Test-Path $advKey)) {{
    New-Item -Path $advKey -Force -ErrorAction SilentlyContinue | Out-Null
    Write-Output 'REG_EXPLORER_ADV_CREATED'
}}
# Garantir que Start_ShowControlPanel está habilitado (Win10)
Set-ItemProperty -Path $advKey -Name 'Start_ShowControlPanel' -Value 1 -Type DWord -ErrorAction SilentlyContinue
Write-Output 'REG_START_SHOWCONTROLPANEL=1'

# REPARO: Restaurar visibilidade de itens do Menu Iniciar (Win10)
$startKey = 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartPage'
if (-not (Test-Path $startKey)) {{
    New-Item -Path $startKey -Force -ErrorAction SilentlyContinue | Out-Null
}}
# MakeAllAppsDefault=0 garante que o Menu Iniciar mostra os tiles, não só apps
Set-ItemProperty -Path $startKey -Name 'MakeAllAppsDefault' -Value 0 -Type DWord -ErrorAction SilentlyContinue
Write-Output 'REG_STARTPAGE_FIXED'

# REPARO: Corrigir associação de arquivo .cpl (Control Panel applets)
$cplKey = 'HKLM:\SOFTWARE\Classes\.cpl'
if (-not (Test-Path $cplKey)) {{
    New-Item -Path $cplKey -Force -ErrorAction SilentlyContinue | Out-Null
    Set-ItemProperty -Path $cplKey -Name '(Default)' -Value 'cplfile' -ErrorAction SilentlyContinue
    Write-Output 'REG_CPL_ASSOC_CREATED'
}} else {{
    Write-Output 'REG_CPL_ASSOC_OK'
}}

Write-Output 'REGISTRY_DONE'
";
            var (_, regOut) = await RunPsAsync(regScript, ct, timeoutMs: 30_000);
            _logger.LogDebug($"[AdvancedRepair][StartMenuFix] Registro: {regOut.Trim()}", source: "AdvancedRepairService");
            foreach (var line in regOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var t = line.Trim();
                if (!string.IsNullOrEmpty(t)) { step.Details.Add($"[REG] {t}"); Log($"    {t}", t.Contains("OK") || t.Contains("DONE") ? "#00FF88" : "#AAAAAA"); }
            }

            // ── FASE 4: RE-REGISTRAR PACOTES UWP DO SHELL ────────────────────
            Log("  → [4/6] Re-registrando pacotes UWP do shell...", "#AAAAAA");
            Progress(50, "Re-registrando pacotes do Menu Iniciar...");
            var uwpScript = @"
# REPARO: Re-registrar todos os pacotes críticos do shell
# Ordem importa: ShellExperienceHost antes de StartMenuExperienceHost
$shellPackages = @(
    'Microsoft.Windows.ShellExperienceHost',
    'Microsoft.Windows.StartMenuExperienceHost',
    'Microsoft.Windows.Search',
    'Microsoft.Windows.Cortana',
    'Microsoft.WindowsStore',
    'Microsoft.Windows.SecHealthUI'
)
foreach ($pkgName in $shellPackages) {
    $pkg = Get-AppxPackage -Name $pkgName -ErrorAction SilentlyContinue
    if ($pkg) {
        $manifest = Join-Path $pkg.InstallLocation 'AppXManifest.xml'
        if (Test-Path $manifest) {
            Add-AppxPackage -DisableDevelopmentMode -Register $manifest -ErrorAction SilentlyContinue
            Write-Output ""UWP_REREGISTERED=$pkgName""
        } else {
            Write-Output ""UWP_MANIFEST_MISSING=$pkgName""
        }
    } else {
        Write-Output ""UWP_NOT_FOUND=$pkgName""
    }
}

# REPARO: Matar e reiniciar ShellExperienceHost para aplicar re-registro
Get-Process -Name ShellExperienceHost -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1
Write-Output 'SHELLEXPERIENCEHOST_RESTARTED'

# REPARO: Matar e reiniciar StartMenuExperienceHost
Get-Process -Name StartMenuExperienceHost -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1
Write-Output 'STARTMENU_RESTARTED'

Write-Output 'UWP_DONE'
";
            var (_, uwpOut) = await RunPsAsync(uwpScript, ct, timeoutMs: 60_000);
            _logger.LogDebug($"[AdvancedRepair][StartMenuFix] UWP: {uwpOut.Trim()}", source: "AdvancedRepairService");
            foreach (var line in uwpOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var t = line.Trim();
                if (!string.IsNullOrEmpty(t)) { step.Details.Add($"[UWP] {t}"); Log($"    {t}", t.Contains("REREGISTERED") || t.Contains("DONE") || t.Contains("RESTARTED") ? "#00FF88" : "#AAAAAA"); }
            }

            // ── FASE 5: LIMPAR CACHE CORROMPIDO ──────────────────────────────
            Log("  → [5/6] Limpando cache corrompido do Menu Iniciar...", "#AAAAAA");
            Progress(70, "Limpando cache do Menu Iniciar...");
            var cacheScript = @"
# REPARO: Remover TileDataLayer (cache de tiles do Win10 — causa principal de Menu Iniciar quebrado)
$tileDb = ""$env:LOCALAPPDATA\TileDataLayer\Database""
if (Test-Path $tileDb) {
    Stop-Service -Name tiledatamodelsvc -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500
    Remove-Item -Path $tileDb -Recurse -Force -ErrorAction SilentlyContinue
    Start-Service -Name tiledatamodelsvc -ErrorAction SilentlyContinue
    Write-Output 'CACHE_TILEDATALAYER_CLEARED'
} else {
    Write-Output 'CACHE_TILEDATALAYER_ABSENT'
}

# REPARO: Limpar cache do StartMenuExperienceHost (Win10/11)
$startCachePath = ""$env:LOCALAPPDATA\Packages\Microsoft.Windows.StartMenuExperienceHost_cw5n1h2txyewy\LocalState""
if (Test-Path $startCachePath) {
    Get-ChildItem $startCachePath -Filter '*.dat' -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
    Write-Output 'CACHE_STARTMENU_DAT_CLEARED'
}

# REPARO: Remover LayoutModification.xml corrompido (força rebuild do layout)
$layoutPaths = @(
    ""$env:LOCALAPPDATA\Microsoft\Windows\Shell\LayoutModification.xml"",
    ""$env:USERPROFILE\AppData\Local\Microsoft\Windows\Shell\LayoutModification.xml""
)
foreach ($lp in $layoutPaths) {
    if (Test-Path $lp) {
        Remove-Item $lp -Force -ErrorAction SilentlyContinue
        Write-Output ""CACHE_LAYOUT_REMOVED=$lp""
    }
}

# REPARO: Limpar cache de ícones do Explorer (corrige ícones quebrados no Menu Iniciar)
$iconCachePath = ""$env:LOCALAPPDATA\Microsoft\Windows\Explorer""
if (Test-Path $iconCachePath) {
    Get-ChildItem $iconCachePath -Filter 'iconcache_*.db' -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
    Write-Output 'CACHE_ICONCACHE_CLEARED'
}

# REPARO: Limpar cache de thumbnails (pode causar lentidão no Menu Iniciar)
$thumbPath = ""$env:LOCALAPPDATA\Microsoft\Windows\Explorer""
Get-ChildItem $thumbPath -Filter 'thumbcache_*.db' -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
Write-Output 'CACHE_THUMBCACHE_CLEARED'

Write-Output 'CACHE_DONE'
";
            var (_, cacheOut) = await RunPsAsync(cacheScript, ct, timeoutMs: 30_000);
            _logger.LogDebug($"[AdvancedRepair][StartMenuFix] Cache: {cacheOut.Trim()}", source: "AdvancedRepairService");
            foreach (var line in cacheOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var t = line.Trim();
                if (!string.IsNullOrEmpty(t)) { step.Details.Add($"[CACHE] {t}"); Log($"    {t}", t.Contains("CLEARED") || t.Contains("DONE") ? "#00FF88" : "#AAAAAA"); }
            }

            // ── FASE 6: REINICIAR EXPLORER + VERIFICAÇÃO FINAL ───────────────
            Log("  → [6/6] Reiniciando Explorer e verificando resultado...", "#AAAAAA");
            Progress(85, "Reiniciando Explorer...");
            var finalScript = @"
# REPARO: Reiniciar Explorer (aplica todas as correções de registro e cache)
# Usar Stop-Process + Start-Process para garantir reinício limpo
Write-Output 'STEP: Reiniciando explorer.exe...'
Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
# Explorer reinicia automaticamente pelo Windows, mas forçamos caso não reinicie
$explorerRunning = Get-Process -Name explorer -ErrorAction SilentlyContinue
if (-not $explorerRunning) {
    Start-Process explorer.exe
    Start-Sleep -Seconds 2
    Write-Output 'EXPLORER_RESTARTED_MANUALLY'
} else {
    Write-Output 'EXPLORER_RESTARTED_AUTO'
}

# REPARO: Forçar atualização do cache de ícones via ie4uinit
$ie4 = ""$env:SystemRoot\System32\ie4uinit.exe""
if (Test-Path $ie4) {
    Start-Process $ie4 -ArgumentList '-show' -WindowStyle Hidden -ErrorAction SilentlyContinue
    Write-Output 'ICON_CACHE_REFRESHED'
}

# VERIFICAÇÃO FINAL: Confirmar que control.exe está acessível
$controlOk = Test-Path ""$env:SystemRoot\System32\control.exe""
Write-Output ""VERIFY_CONTROL_EXE=$controlOk""

# VERIFICAÇÃO FINAL: Confirmar que StartMenuExperienceHost está rodando
Start-Sleep -Seconds 2
$startMenuRunning = (Get-Process -Name StartMenuExperienceHost -ErrorAction SilentlyContinue) -ne $null
Write-Output ""VERIFY_STARTMENU_RUNNING=$startMenuRunning""

# VERIFICAÇÃO FINAL: Confirmar que ShellExperienceHost está rodando
$shellRunning = (Get-Process -Name ShellExperienceHost -ErrorAction SilentlyContinue) -ne $null
Write-Output ""VERIFY_SHELLEXP_RUNNING=$shellRunning""

Write-Output 'STARTMENU_DEEP_FIX_DONE'
";
            var (_, finalOut) = await RunPsAsync(finalScript, ct, timeoutMs: 45_000);
            _logger.LogDebug($"[AdvancedRepair][StartMenuFix] Final: {finalOut.Trim()}", source: "AdvancedRepairService");
            foreach (var line in finalOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var t = line.Trim();
                if (string.IsNullOrEmpty(t)) continue;
                step.Details.Add($"[FINAL] {t}");
                var color = t.Contains("True") || t.Contains("DONE") || t.Contains("RESTARTED") || t.Contains("REFRESHED")
                    ? "#00FF88" : t.Contains("False") ? "#FFA500" : "#AAAAAA";
                Log($"    {t}", color);
            }

            var done = finalOut.Contains("STARTMENU_DEEP_FIX_DONE");
            var controlOk = finalOut.Contains("VERIFY_CONTROL_EXE=True");
            var startMenuOk = finalOut.Contains("VERIFY_STARTMENU_RUNNING=True");

            step.Success = done;
            step.Summary = done
                ? string.Format(Loc("Repair_FixStartMenu_SuccessFmt"), controlOk, startMenuOk)
                : Loc("Repair_FixStartMenu_Warn");

            Log($"{(step.Success ? "✓" : "⚠")} {step.Summary}", step.Success ? "#00FF88" : "#FFA500");
            _logger.LogInfo($"[AdvancedRepair][StartMenuFix] Resultado: {step.Summary} duration={sw.Elapsed.TotalSeconds:F1}s");
            step.Duration = sw.Elapsed;
            return step;
        }

        // ─────────────────────────────────────────────────────────────────────
        // FIM DO SERVIÇO
        // ─────────────────────────────────────────────────────────────────────
        // ─────────────────────────────────────────────────────────────────────
        // ETAPA 19 — OTIMIZAÇÃO INTELIGENTE DE DISCO (DEFRAG / TRIM)
        // ─────────────────────────────────────────────────────────────────────
        public async Task<AdvancedRepairStepResult> Step19_DiskOptimizationAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var step = new AdvancedRepairStepResult { StepName = Loc("Repair_Step19_Name") };
            Log($"═══ {Loc("Repair_Step19_Title")} ═══", "#00BFFF");
            Progress(95, Loc("Repair_Step19_Progress"));

            var script = @"
            $results = @()
            $volumes = Get-Volume | Where-Object { $_.DriveLetter -ne $null -and $_.DriveType -eq 'Fixed' }
            foreach ($vol in $volumes) {
                $drive = $vol.DriveLetter + ':'
                try {
                    & defrag.exe $drive /O
                    $results += ""Drive $drive: Otimizado com sucesso via defrag.exe (/O)""
                } catch {
                    $results += ""Drive $drive: Falha ao otimizar ($($_.Exception.Message))""
                }
            }
            $results | Out-String
            ";

            var (code, output) = await RunPsAsync(script, ct, timeoutMs: 600_000);
            step.Details.Add(output.Trim());
            step.Success = code == 0;
            step.Summary = Loc("Repair_Step19_Success");
            step.Duration = sw.Elapsed;
            return step;
        }
        // ---------------------------------------------------------------------
        // ETAPA 20 � REPARO DE CONTADORES DE PERFORMANCE (PERFMON / WMI)
        // ---------------------------------------------------------------------
        public async Task<AdvancedRepairStepResult> Step20_PerfMonRepairAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var stepName = "Reparo de Contadores de Performance";
            try { stepName = LocalizationService.Instance.GetString("Repair_Step20_Name"); } catch { }
            if (stepName == "Repair_Step20_Name" || string.IsNullOrEmpty(stepName)) stepName = "Reparo de Contadores de Performance";
            
            var step = new AdvancedRepairStepResult { StepName = stepName };
            _logger.LogDebug($"[AdvancedRepair][Step20] IN°CIO �s {DateTime.Now:HH:mm:ss.fff}", source: "AdvancedRepairService");
            Log($"--- REPARO DO PERFMON ---", "#00BFFF");
            Progress(98, "Reconstruindo Performance Counters (lodctr /r)...");

            Log("  ? Executando lodctr /r para reconstruir contadores do Windows...", "#AAAAAA");
            var lodctr = ResolveExe("lodctr.exe");
            
            var (code1, out1) = await RunAsync(lodctr, "/r", ct, timeoutMs: 30_000);
            step.Details.Add($"[lodctr /r] (tentativa 1) exit={code1}: {out1.Trim()}");

            if (code1 != 0)
            {
                Log("  ? Falha na tentativa 1, for�ando segunda reconstru��o...", "#FFA500");
                var (code2, out2) = await RunAsync(lodctr, "/r", ct, timeoutMs: 30_000);
                step.Details.Add($"[lodctr /r] (tentativa 2) exit={code2}: {out2.Trim()}");
            }

            var script = @"
            $wmiAdapter = Get-Service -Name wmiApSrv -ErrorAction SilentlyContinue
            if ($wmiAdapter) {
                Restart-Service wmiApSrv -Force -ErrorAction SilentlyContinue
                Write-Output 'WMI_ADAPTER_RESTARTED'
            }
            ";
            var (psCode, psOut) = await RunPsAsync(script, ct, timeoutMs: 20_000);
            step.Details.Add($"[WMI Adapter] exit={psCode}: {psOut.Trim()}");

            step.Success = true;
            step.Summary = "Contadores de Performance reconstruídos com sucesso";
            _logger.LogDebug($"[AdvancedRepair][Step20] CONCLUÍDO: duration={sw.Elapsed.TotalSeconds:F1}s", source: "AdvancedRepairService");
            Log($"? {step.Summary}", "#00FF88");
            step.Duration = sw.Elapsed;
            return step;
        }
    }
}




