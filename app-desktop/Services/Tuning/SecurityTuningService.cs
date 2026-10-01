using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using Microsoft.Win32;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Utils;

namespace VoltrisOptimizer.Services.Tuning
{
    public class SecurityTuningService : ISecurityTuningService
    {
        private readonly ILoggingService _logger;
        private const string RegistryBaseKey = @"SOFTWARE\Voltris\Optimizations";

        // ── Cache de status (TTL: 60 s) ───────────────────────────────────────
        private SecurityStatus? _cachedStatus;
        private DateTime _cacheExpiry = DateTime.MinValue;
        private readonly SemaphoreSlim _cacheLock = new SemaphoreSlim(1, 1);
        
        // ── Cache rápido para verificações instantâneas ───────────────────────────────
        private static readonly Dictionary<string, (bool value, DateTime expiry)> _quickCache = new();
        private static readonly TimeSpan _quickCacheTtl = TimeSpan.FromSeconds(30);

        public SecurityTuningService(ILoggingService logger)
        {
            _logger = logger;
        }

        public async Task<SecurityStatus> GetSecurityStatusAsync(IProgress<SecurityStatus>? progress = null)
        {
            var swTotal = Stopwatch.StartNew();

            // Retornar do cache se ainda válido (TTL: 60 s)
            if (_cachedStatus != null && DateTime.UtcNow < _cacheExpiry)
            {
                _logger.Log(LogLevel.Debug, LogCategory.Security, $"Retornando status do cache (restam: {(_cacheExpiry - DateTime.UtcNow).TotalSeconds:F1}s)");
                return _cachedStatus;
            }

            await _cacheLock.WaitAsync();
            try
            {
                // Double-check após adquirir o lock
                if (_cachedStatus != null && DateTime.UtcNow < _cacheExpiry)
                {
                    _logger.Log(LogLevel.Debug, LogCategory.Security, "Retornando status do cache após lock");
                    return _cachedStatus;
                }

                _logger.Log(LogLevel.Info, LogCategory.Security, "▶ Iniciando coleta de status de segurança...");
                
                var status = new SecurityStatus();
                try
                {
                    // ── FASE 1: Verificações ULTRA RÁPIDAS (cache + registry) ──
                    _logger.Log(LogLevel.Debug, LogCategory.Security, "Fase 1: Registry checks iniciando...");
                    var swPhase1 = Stopwatch.StartNew();

                    var checks = new Dictionary<string, (Stopwatch sw, Task<bool> task)>
                    {
                        ["WinUpdate"]       = (Stopwatch.StartNew(), GetCachedOrCheckAsync("WinUpdate", () => IsWindowsUpdateEnabledAsync())),
                        ["SmartScreen"]     = (Stopwatch.StartNew(), GetCachedOrCheckAsync("SmartScreen", () => IsSmartScreenEnabledAsync())),
                        ["RTP"]             = (Stopwatch.StartNew(), GetCachedOrCheckAsync("RTP", () => IsRealTimeProtectionEnabledAsync())),
                        ["UAC"]             = (Stopwatch.StartNew(), GetCachedOrCheckAsync("UAC", () => IsUACEnabledAsync())),
                        ["Tamper"]          = (Stopwatch.StartNew(), GetCachedOrCheckAsync("Tamper", () => IsTamperProtectionEnabledAsync())),
                        ["DefenderSvc"]     = (Stopwatch.StartNew(), GetCachedOrCheckAsync("DefenderSvc", () => IsDefenderServiceEnabledAsync())),
                        ["AV"]              = (Stopwatch.StartNew(), GetCachedOrCheckAsync("AV", () => IsAntivirusEnabledAsync())),
                        ["Firewall"]        = (Stopwatch.StartNew(), GetCachedOrCheckAsync("Firewall", () => IsFirewallEnabledAsync()))
                    };

                    await Task.WhenAll(checks.Values.Select(c => c.task));

                    foreach (var (key, (sw, task)) in checks)
                    {
                        sw.Stop();
                        bool value = await task;
                        _logger.Log(LogLevel.Debug, LogCategory.Security, $"  {key} = {value} ({sw.ElapsedMilliseconds}ms)");
                        switch (key)
                        {
                            case "WinUpdate":   status.WindowsUpdateEnabled = value; break;
                            case "SmartScreen": status.SmartScreenEnabled = value; break;
                            case "RTP":         status.RealTimeProtectionEnabled = value; break;
                            case "UAC":         status.UacEnabled = value; break;
                            case "Tamper":      status.TamperProtectionEnabled = value; break;
                            case "DefenderSvc": status.DefenderServiceEnabled = value; break;
                            case "AV":          status.AntivirusEnabled = value; status.AntivirusProduct = "Windows Defender"; break;
                            case "Firewall":    status.FirewallEnabled = value; break;
                        }
                    }

                    swPhase1.Stop();
                    _logger.Log(LogLevel.Info, LogCategory.Security, $"Fase 1 concluída em {swPhase1.ElapsedMilliseconds}ms");

                    // Reportar progresso parcial (Fase 1) para UI carregar rápido
                    progress?.Report(status);

                    // ── FASE 2: Verificações LENTAS (PowerShell/WMI combinadas em 1 script) ──
                    _logger.Log(LogLevel.Debug, LogCategory.Security, "Fase 2: Script PowerShell único iniciando...");
                    var swPhase2 = Stopwatch.StartNew();

                    await RunCombinedPhase2Async(status);

                    swPhase2.Stop();
                    _logger.Log(LogLevel.Debug, LogCategory.Security, $"Fase 2 concluída em {swPhase2.ElapsedMilliseconds}ms");
                    _logger.Log(LogLevel.Info, LogCategory.Security, $"  AV={status.AntivirusProduct} (enabled={status.AntivirusEnabled}), Firewall={status.FirewallEnabled}");
                    _logger.Log(LogLevel.Info, LogCategory.Security, $"  CFA={status.ControlledFolderAccessEnabled}, BitLocker={status.BitLockerEnabled}");
                }
                catch (Exception ex)
                {
                    _logger.LogError("Erro ao obter status de segurança", ex);
                }

                swTotal.Stop();
                _logger.Log(LogLevel.Success, LogCategory.Security, $"Status de segurança coletado em {swTotal.ElapsedMilliseconds}ms");

                // Armazenar no cache
                _cachedStatus = status;
                _cacheExpiry = DateTime.UtcNow.AddSeconds(60);

                // Report final status
                progress?.Report(status);

                // ⚠️ Retornar um NOVO objeto para quebrar a comparação de referência no ViewModel
                // (o progress callback já definiu _status = status, e Phase 2 modificou o mesmo objeto)
                return new SecurityStatus
                {
                    AntivirusEnabled = status.AntivirusEnabled,
                    AntivirusProduct = status.AntivirusProduct,
                    LastSignatureUpdate = status.LastSignatureUpdate,
                    FirewallEnabled = status.FirewallEnabled,
                    WindowsUpdateEnabled = status.WindowsUpdateEnabled,
                    SmartScreenEnabled = status.SmartScreenEnabled,
                    RealTimeProtectionEnabled = status.RealTimeProtectionEnabled,
                    UacEnabled = status.UacEnabled,
                    TamperProtectionEnabled = status.TamperProtectionEnabled,
                    ControlledFolderAccessEnabled = status.ControlledFolderAccessEnabled,
                    BitLockerEnabled = status.BitLockerEnabled,
                    DefenderServiceEnabled = status.DefenderServiceEnabled
                };
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        public async Task RunQuickScanAsync()
        {
            try
            {
                _logger.LogInfo("Iniciando Verificação Rápida do Windows Defender...");
                await RunPowerShellAsync("Start-MpScan -ScanType QuickScan");
                _logger.LogSuccess("Verificação Rápida concluída.");
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro ao executar verificação rápida", ex);
            }
        }

        public async Task UpdateSignaturesAsync()
        {
            try
            {
                _logger.LogInfo("Atualizando definições do Windows Defender...");
                await RunPowerShellAsync("Update-MpSignature");
                _logger.LogSuccess("Definições atualizadas com sucesso.");
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro ao atualizar definições", ex);
            }
        }

        public async Task<bool> ApplyTweakAsync(string tag, bool enable)
        {
            try
            {
                _logger.LogInfo($"Aplicando tweak de segurança: {tag} ({enable})");
                bool success = false;
                switch (tag)
                {
                    case "SmartScreen":
                        if (!enable) await RunCommandAsync("reg add \"HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\System\" /v EnableSmartScreen /t REG_DWORD /d 0 /f");
                        else await RunCommandAsync("reg delete \"HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\System\" /v EnableSmartScreen /f");
                        success = true;
                        break;
                    case "VBS":
                        if (!enable) await RunCommandAsync("reg add \"HKLM\\SYSTEM\\CurrentControlSet\\Control\\DeviceGuard\\Scenarios\\HypervisorEnforcedCodeIntegrity\" /v Enabled /t REG_DWORD /d 0 /f");
                        else await RunCommandAsync("reg delete \"HKLM\\SYSTEM\\CurrentControlSet\\Control\\DeviceGuard\\Scenarios\\HypervisorEnforcedCodeIntegrity\" /v Enabled /f");
                        success = true;
                        break;
                }

                if (success)
                {
                    SaveTweakState(tag, enable);
                    // Invalidar cache para forçar leitura atualizada
                    _cacheExpiry = DateTime.MinValue;
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erro ao aplicar tweak {tag}", ex);
            }
            return false;
        }

        /// <summary>
        /// Cache rápido para verificações de registry que mudam raramente
        /// </summary>
        private async Task<bool> GetCachedOrCheckAsync(string key, Func<Task<bool>> checker)
        {
            var now = DateTime.UtcNow;
            
            lock (_quickCache)
            {
                if (_quickCache.TryGetValue(key, out var cached) && now < cached.expiry)
                {
                    _logger.LogDebug($"[Security] Cache rápido hit: {key}");
                    return cached.value;
                }
            }
            
            _logger.LogDebug($"[Security] Cache rápido miss: {key} - executando verificação");
            var result = await checker();
            
            lock (_quickCache)
            {
                _quickCache[key] = (result, now.Add(_quickCacheTtl));
                
                // Limpar cache antigo periodicamente
                if (_quickCache.Count > 20)
                {
                    var expiredKeys = _quickCache.Where(kvp => now >= kvp.Value.expiry).Select(kvp => kvp.Key).ToList();
                    foreach (var expiredKey in expiredKeys)
                        _quickCache.Remove(expiredKey);
                }
            }
            
            return result;
        }

        public void InvalidateCache()
        {
            lock (_cacheLock)
            {
                _cachedStatus = null;
                _cacheExpiry = DateTime.MinValue;
                _logger.LogInfo("[Security] Cache principal invalidado");
            }
            
            lock (_quickCache)
            {
                _quickCache.Clear();
                _logger.LogInfo("[Security] Cache rápido invalidado");
            }
        }

        public Task<bool> GetTweakStateAsync(string tag)
        {
            using var key = Registry.LocalMachine.OpenSubKey(RegistryBaseKey);
            if (key?.GetValue(tag) is int state)
            {
                return Task.FromResult(state == 1);
            }
            return Task.FromResult(false);
        }

        private void SaveTweakState(string tag, bool enable)
        {
            using var key = Registry.LocalMachine.CreateSubKey(RegistryBaseKey);
            key.SetValue(tag, enable ? 1 : 0, RegistryValueKind.DWord);
        }

        /// <summary>
        /// Fase 2 combinada: executa um ÚNICO script PowerShell que coleta todos os dados lentos
        /// (antivírus, firewall, CFA, BitLocker) em vez de 4 processos separados.
        /// </summary>
        private async Task RunCombinedPhase2Async(SecurityStatus status)
        {
            const string script = @"
$WarningPreference = 'SilentlyContinue'
$ErrorActionPreference = 'SilentlyContinue'
$r = @{}

# ═══════════════════════════════════════════════════════════════
# ANTIVIRUS
# ═══════════════════════════════════════════════════════════════
try {
    Write-Output ""PHASE2_AV_START""
    $mpAv = Get-MpComputerStatus -ErrorAction SilentlyContinue
    $defenderAv = $false
    if ($mpAv) {
        $defenderAv = ($mpAv.AntivirusEnabled -eq $true) -or ($mpAv.AMServiceEnabled -eq $true) -or ($mpAv.AMProductEnabled -eq $true)
        Write-Output ""PHASE2_AV_MP: AntivirusEnabled=$($mpAv.AntivirusEnabled) AMServiceEnabled=$($mpAv.AMServiceEnabled) AMProductEnabled=$($mpAv.AMProductEnabled) -> defenderAv=$defenderAv""
    } else {
        Write-Output ""PHASE2_AV_MP: Get-MpComputerStatus returned null""
    }

    $scAv = Get-CimInstance -Namespace 'root\SecurityCenter2' -ClassName 'AntiVirusProduct' -ErrorAction SilentlyContinue | Select-Object -First 1
    $scAvEnabled = $false
    $scAvName = $null
    if ($scAv) {
        $ps = $scAv.productState
        $scAvName = $scAv.displayName
        $r.AVState = $ps
        $r.AVRawState = '0x' + $ps.ToString('X8')
        $bit16 = ($ps -band 0x10000) -ne 0
        $bit12 = ($ps -band 0x1000) -ne 0
        $bits01 = $ps -band 0x03
        $scAvEnabled = $bit16 -or $bit12 -or ($bits01 -eq 2)
        Write-Output ""PHASE2_AV_SC: productState=0x$($ps.ToString('X8')) bit16=$bit16 bit12=$bit12 bits01=$bits01 scEnabled=$scAvEnabled name=$scAvName""
    } else {
        Write-Output ""PHASE2_AV_SC: No SecurityCenter2 product found""
    }

    if ($defenderAv) {
        $r.AVEnabled = $true
        $r.AVName = if ($scAvName) { $scAvName } else { 'Windows Defender' }
        Write-Output ""PHASE2_AV_DECISION: Defender reports enabled -> AVEnabled=true""
    } elseif ($scAv) {
        $r.AVEnabled = $scAvEnabled
        $r.AVName = $scAvName
        Write-Output ""PHASE2_AV_DECISION: Using SecurityCenter2 -> AVEnabled=$scAvEnabled""
    } else {
        $r.AVName = 'Windows Defender'
        $r.AVEnabled = $true
        Write-Output ""PHASE2_AV_DECISION: No source found, defaulting to enabled""
    }
} catch {
    $r.AVName = 'Windows Defender'
    $r.AVState = 0
    $r.AVEnabled = $true
    Write-Output ""PHASE2_AV_EXCEPTION: $($_.Exception.Message)""
}

# ═══════════════════════════════════════════════════════════════
# SIGNATURE DATE
# ═══════════════════════════════════════════════════════════════
try {
    $mp = Get-MpComputerStatus -ErrorAction SilentlyContinue
    if ($mp -and $mp.AntivirusSignatureLastUpdated) {
        $r.AVSig = $mp.AntivirusSignatureLastUpdated.ToString('o')
        Write-Output ""PHASE2_SIG: LastUpdated=$($r.AVSig)""
    } else {
        $r.AVSig = ''
        Write-Output ""PHASE2_SIG: No data from Get-MpComputerStatus""
    }
} catch { $r.AVSig = ''; Write-Output ""PHASE2_SIG_EXCEPTION: $($_.Exception.Message)"" }

# ═══════════════════════════════════════════════════════════════
# FIREWALL
# ═══════════════════════════════════════════════════════════════
try {
    Write-Output ""PHASE2_FW_START""
    $fwEnabled = $false
    $fwSources = @()

    # 1. Try via NetSecurity module
    try {
        Import-Module NetSecurity -ErrorAction SilentlyContinue | Out-Null
        $profiles = Get-NetFirewallProfile -ErrorAction SilentlyContinue
        if ($profiles) {
            $enabledCount = 0
            $totalCount = 0
            foreach ($p in $profiles) {
                $totalCount++
                $isEnabled = if ($p.Enabled -is [bool]) { $p.Enabled } else { $p.Enabled -eq 'True' }
                Write-Output ""PHASE2_FW_PROFILE: $($p.Name) Enabled=$isEnabled""
                if ($isEnabled) { $enabledCount++ }
            }
            $fwEnabled = $enabledCount -gt 0
            $fwSources += ""NetFwProfile""
            Write-Output ""PHASE2_FW_PROFILES: $enabledCount/$totalCount enabled -> fwEnabled=$fwEnabled""
        } else {
            Write-Output ""PHASE2_FW_PROFILES: Get-NetFirewallProfile returned empty""
        }
    } catch {
        Write-Output ""PHASE2_FW_NETSECURITY_EXCEPTION: $($_.Exception.Message)""
    }

    # 2. Fallback via Registry
    if (-not $fwEnabled) {
        try {
            $domainFw = (Get-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\DomainProfile' -Name 'EnableFirewall' -ErrorAction SilentlyContinue).EnableFirewall
            $pubFw = (Get-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\PublicProfile' -Name 'EnableFirewall' -ErrorAction SilentlyContinue).EnableFirewall
            $privFw = (Get-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\StandardProfile' -Name 'EnableFirewall' -ErrorAction SilentlyContinue).EnableFirewall
            Write-Output ""PHASE2_FW_REG: Domain=$domainFw Public=$pubFw Private=$privFw""
            $regEnabled = ($domainFw -eq 1) -or ($pubFw -eq 1) -or ($privFw -eq 1)
            if ($regEnabled) {
                $fwEnabled = $true
                $fwSources += ""Reg""
            }
        } catch {
            Write-Output ""PHASE2_FW_REG_EXCEPTION: $($_.Exception.Message)""
        }
    }

    # 3. Last resort: check via Netsh
    if (-not $fwEnabled) {
        try {
            $netshOut = netsh advfirewall show allprofiles state 2>&1 | Out-String
            Write-Output ""PHASE2_FW_NETSH: $netshOut""
            if ($netshOut -match 'ON') { $fwEnabled = $true; $fwSources += ""Netsh"" }
        } catch {
            Write-Output ""PHASE2_FW_NETSH_EXCEPTION: $($_.Exception.Message)""
        }
    }

    $r.Firewall = $fwEnabled
    Write-Output ""PHASE2_FW_DECISION: fwEnabled=$fwEnabled sources=$(if ($fwSources.Count -gt 0) { $fwSources -join ',' } else { 'none' })""
} catch {
    $r.Firewall = $false
    Write-Output ""PHASE2_FW_EXCEPTION: $($_.Exception.Message)""
}

# ═══════════════════════════════════════════════════════════════
# REAL-TIME PROTECTION & TAMPER PROTECTION (via WMI)
# ═══════════════════════════════════════════════════════════════
try {
    $mpStatus = Get-MpComputerStatus -ErrorAction SilentlyContinue
    if ($mpStatus) {
        $rtp = $mpStatus.RealTimeProtectionEnabled
        $r.RTP = if ($null -ne $rtp) { [bool]$rtp } else { $true }
        Write-Output ""PHASE2_RTP: RealTimeProtectionEnabled=$($r.RTP)""
        $tp = $mpStatus.IsTamperProtected
        $r.TamperProtection = if ($null -ne $tp) { [bool]$tp } else { $false }
        Write-Output ""PHASE2_TP: IsTamperProtected=$($r.TamperProtection)""
        $r.DefenderSvc = $true
    } else {
        $r.RTP = $true
        $r.TamperProtection = $false
        $r.DefenderSvc = $true
        Write-Output ""PHASE2_RTP: Get-MpComputerStatus returned null""
    }
} catch {
    $r.RTP = $true
    $r.TamperProtection = $false
    $r.DefenderSvc = $true
    Write-Output ""PHASE2_RTP_EXCEPTION: $($_.Exception.Message)""
}

# ═══════════════════════════════════════════════════════════════
# CONTROLLED FOLDER ACCESS
# ═══════════════════════════════════════════════════════════════
try {
    $cfa = Get-MpPreference -ErrorAction SilentlyContinue
    if ($cfa) {
        $r.CFA = $cfa.EnableControlledFolderAccess
        Write-Output ""PHASE2_CFA: EnableControlledFolderAccess=$($r.CFA)""
    } else {
        $r.CFA = 0
        Write-Output ""PHASE2_CFA: Get-MpPreference returned null""
    }
} catch { $r.CFA = 0; Write-Output ""PHASE2_CFA_EXCEPTION: $($_.Exception.Message)"" }

# ═══════════════════════════════════════════════════════════════
# BITLOCKER
# ═══════════════════════════════════════════════════════════════
try {
    $blVolumes = Get-BitLockerVolume -ErrorAction SilentlyContinue
    if ($blVolumes) {
        $protectedCount = ($blVolumes | Where-Object { $_.ProtectionStatus -eq 'On' }).Count
        $r.BitLocker = $protectedCount -gt 0
        Write-Output ""PHASE2_BL: $protectedCount volume(s) protected -> enabled=$($r.BitLocker)""
    } else {
        $r.BitLocker = $false
        Write-Output ""PHASE2_BL: No BitLocker volumes found""
    }
} catch { $r.BitLocker = $false; Write-Output ""PHASE2_BL_EXCEPTION: $($_.Exception.Message)"" }

# ═══════════════════════════════════════════════════════════════
# UAC & SMARTSCREEN (Registry cross-check)
# ═══════════════════════════════════════════════════════════════
try {
    $uac = Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System' -Name 'EnableLUA' -ErrorAction SilentlyContinue
    $r.UAC = ($uac.EnableLUA -eq 1)
    Write-Output ""PHASE2_UAC: EnableLUA=$($uac.EnableLUA) -> enabled=$($r.UAC)""
} catch { Write-Output ""PHASE2_UAC_EXCEPTION: $($_.Exception.Message)"" }

try {
    $ss = Get-ItemProperty -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\System' -Name 'EnableSmartScreen' -ErrorAction SilentlyContinue
    if ($null -ne $ss -and $ss.EnableSmartScreen -eq 0) {
        $r.SmartScreen = $false
        Write-Output ""PHASE2_SS: EnableSmartScreen=0 (disabled via policy)""
    } else {
        $r.SmartScreen = $true
        Write-Output ""PHASE2_SS: Default (enabled)""
    }
} catch { Write-Output ""PHASE2_SS_EXCEPTION: $($_.Exception.Message)"" }

# ═══════════════════════════════════════════════════════════════
# OUTPUT
# ═══════════════════════════════════════════════════════════════
Write-Output ""PHASE2_JSON_START""
$r | ConvertTo-Json -Depth 3 -Compress
Write-Output ""PHASE2_JSON_END""
";
            string? rawStdout = null;
            string? jsonPart = null;
            try
            {
                _logger.LogInfo($"[Phase2] Script length: {script.Length} chars — executing via temp file");

                rawStdout = await RunPowerShellFileAsync(script, "SecurityPhase2");
                _logger.LogInfo($"[Phase2] Raw stdout ({rawStdout?.Length ?? 0} chars)");

                if (string.IsNullOrWhiteSpace(rawStdout))
                {
                    _logger.LogWarning("[Phase2] stdout vazio — provavelmente falhou silenciosamente");
                    if (!status.AntivirusEnabled) status.AntivirusEnabled = true;
                    if (string.IsNullOrEmpty(status.AntivirusProduct)) status.AntivirusProduct = "Windows Defender";
                    if (!status.FirewallEnabled) status.FirewallEnabled = true;
                    return;
                }

                // Logar todas as linhas PHASE2_
                foreach (var line in rawStdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("PHASE2_"))
                        _logger.LogInfo($"[Phase2] {trimmed}");
                }

                // Extrair JSON entre marcadores
                var jsonStart = rawStdout.IndexOf("PHASE2_JSON_START");
                var jsonEnd = rawStdout.IndexOf("PHASE2_JSON_END");
                if (jsonStart >= 0 && jsonEnd > jsonStart)
                {
                    jsonPart = rawStdout.Substring(jsonStart + "PHASE2_JSON_START".Length, jsonEnd - jsonStart - "PHASE2_JSON_START".Length).Trim();
                    var preview = jsonPart?.Length > 300 ? jsonPart[..300] + "..." : jsonPart;
                    _logger.LogInfo($"[Phase2] JSON extraído ({jsonPart?.Length ?? 0} chars): {preview}");
                }
                else
                {
                    jsonPart = rawStdout.Trim();
                    _logger.LogWarning("[Phase2] Marcadores JSON não encontrados — tentando parsear completo");
                }

                if (string.IsNullOrWhiteSpace(jsonPart))
                {
                    _logger.LogWarning("[Phase2] JSON vazio — defaults");
                    if (!status.AntivirusEnabled) status.AntivirusEnabled = true;
                    if (string.IsNullOrEmpty(status.AntivirusProduct)) status.AntivirusProduct = "Windows Defender";
                    if (!status.FirewallEnabled) status.FirewallEnabled = true;
                    return;
                }

                using var doc = JsonDocument.Parse(jsonPart);
                var root = doc.RootElement;

                var avName = root.TryGetProperty("AVName", out var n) ? n.GetString() ?? "Windows Defender" : "Windows Defender";
                status.AntivirusProduct = avName;
                if (root.TryGetProperty("AVEnabled", out var avEn)) status.AntivirusEnabled = avEn.GetBoolean();
                else status.AntivirusEnabled = true;
                _logger.LogInfo($"[Phase2] AV name='{avName}' enabled={status.AntivirusEnabled}");

                if (root.TryGetProperty("AVRawState", out var rawSt))
                    _logger.LogInfo($"[Phase2] AV rawState={rawSt.GetString()}");

                if (root.TryGetProperty("AVSig", out var sig) && DateTime.TryParse(sig.GetString(), out var sigDate))
                { status.LastSignatureUpdate = sigDate; _logger.LogInfo($"[Phase2] AV Signature: {sigDate:yyyy-MM-dd HH:mm}"); }

                if (root.TryGetProperty("Firewall", out var fw)) { status.FirewallEnabled = fw.GetBoolean(); _logger.LogInfo($"[Phase2] Firewall enabled={status.FirewallEnabled}"); }
                else _logger.LogWarning("[Phase2] Firewall NOT in JSON");

                if (root.TryGetProperty("RTP", out var rtp)) { status.RealTimeProtectionEnabled = rtp.GetBoolean(); _logger.LogInfo($"[Phase2] RealTimeProtection enabled={status.RealTimeProtectionEnabled}"); }
                if (root.TryGetProperty("TamperProtection", out var tp)) { status.TamperProtectionEnabled = tp.GetBoolean(); _logger.LogInfo($"[Phase2] TamperProtection enabled={status.TamperProtectionEnabled}"); }
                if (root.TryGetProperty("CFA", out var cfa)) { status.ControlledFolderAccessEnabled = cfa.GetInt32() != 0; _logger.LogInfo($"[Phase2] CFA enabled={status.ControlledFolderAccessEnabled}"); }
                if (root.TryGetProperty("BitLocker", out var bl)) { status.BitLockerEnabled = bl.GetBoolean(); _logger.LogInfo($"[Phase2] BitLocker enabled={status.BitLockerEnabled}"); }
                if (root.TryGetProperty("UAC", out var uac)) _logger.LogInfo($"[Phase2] UAC cross={uac.GetBoolean()}");
                if (root.TryGetProperty("SmartScreen", out var ss)) _logger.LogInfo($"[Phase2] SS cross={ss.GetBoolean()}");
                if (root.TryGetProperty("DefenderSvc", out var dsvc)) _logger.LogInfo($"[Phase2] DefSvc={dsvc.GetBoolean()}");

                _logger.LogSuccess($"[Phase2] All status collected successfully");
            }
            catch (JsonException jsonEx)
            {
                _logger.LogWarning($"[Phase2] JSON parse error: {jsonEx.Message}");
                _logger.LogInfo($"[Phase2] jsonPart={(jsonPart?.Length > 300 ? jsonPart[..300] + "..." : jsonPart)}");
                _logger.LogInfo($"[Phase2] rawStdout={(rawStdout?.Length > 300 ? rawStdout[..300] + "..." : rawStdout)}");
                if (!status.AntivirusEnabled) status.AntivirusEnabled = true;
                if (string.IsNullOrEmpty(status.AntivirusProduct)) status.AntivirusProduct = "Windows Defender";
                if (!status.FirewallEnabled) status.FirewallEnabled = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Phase2] Fase 2 falhou: {ex.GetType().Name}: {ex.Message}");
                if (!status.AntivirusEnabled) status.AntivirusEnabled = true;
                if (string.IsNullOrEmpty(status.AntivirusProduct)) status.AntivirusProduct = "Windows Defender";
                if (!status.FirewallEnabled) status.FirewallEnabled = true;
            }
        }

        private static string TruncateJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return json;
            return json.Length <= 500 ? json : json[..500] + "...";
        }

        #region Helper Methods (RyTuneX Logic)

        private async Task<(string ProductName, bool IsEnabled, DateTime? SignatureUpdated)> GetAntivirusInfoAsync()
        {
            string product = "Windows Defender";
            bool enabled = false;
            DateTime? updated = null;

            try
            {
                var command = @"
                    $av = Get-CimInstance -Namespace 'root\SecurityCenter2' -ClassName 'AntiVirusProduct' -ErrorAction SilentlyContinue | Select-Object -First 1
                    if ($av) { $av.displayName; $av.productState } else { 'Windows Defender'; '0' }";
                
                var output = await RunPowerShellAsync(command);
                var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                if (lines.Length >= 2)
                {
                    product = lines[0];
                    if (int.TryParse(lines[1], out var state))
                    {
                        enabled = (state & 0xF000) == 0x1000;
                    }
                }

                var sigOutput = await RunPowerShellAsync("(Get-MpComputerStatus -ErrorAction SilentlyContinue).AntivirusSignatureLastUpdated.ToString('o')");
                if (DateTime.TryParse(sigOutput, out var sigDate))
                {
                    updated = sigDate;
                }
            }
            catch { }
            return (product, enabled, updated);
        }

        private async Task<bool> IsFirewallEnabledAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var profiles = new[] { "DomainProfile", "StandardProfile", "PublicProfile" };
                    foreach (var profile in profiles)
                    {
                        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\{profile}");
                        if (key?.GetValue("EnableFirewall") is int val && val == 1)
                        {
                            _logger.LogInfo($"[Security] Firewall: {profile} EnableFirewall=1 → ativo");
                            return true;
                        }
                    }
                    _logger.LogInfo("[Security] Firewall: Nenhum perfil EnableFirewall=1");
                    using var svcKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\MpsSvc");
                    if (svcKey?.GetValue("Start") is int start && start == 4)
                    {
                        _logger.LogInfo("[Security] Firewall: MpsSvc desabilitado — inativo");
                        return false;
                    }
                    _logger.LogInfo("[Security] Firewall: Assumindo ativo");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[Security] Erro Firewall Phase1: {ex.Message}");
                    return true;
                }
            });
        }

        private async Task<bool> IsWindowsUpdateEnabledAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var key1 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU");
                    if (key1?.GetValue("NoAutoUpdate") is int noAutoUpdate && noAutoUpdate == 1)
                    {
                        _logger.LogInfo("[Security] WindowsUpdate: Desabilitado via NoAutoUpdate policy");
                        return false;
                    }

                    using var key2 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate");
                    if (key2?.GetValue("DisableWindowsUpdateAccess") is int disabled && disabled == 1)
                    {
                        _logger.LogInfo("[Security] WindowsUpdate: Desabilitado via DisableWindowsUpdateAccess policy");
                        return false;
                    }

                    using var key3 = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\wuauserv");
                    if (key3?.GetValue("Start") is int start && start == 4)
                    {
                        _logger.LogInfo("[Security] WindowsUpdate: Serviço wuauserv desabilitado (Start=4)");
                        return false;
                    }

                    _logger.LogInfo("[Security] WindowsUpdate: Ativo");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[Security] Erro ao verificar WindowsUpdate: {ex.Message}");
                    return false;
                }
            });
        }

        private async Task<bool> IsSmartScreenEnabledAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var policyKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\System");
                    if (policyKey?.GetValue("EnableSmartScreen") is int policyValue && policyValue == 0)
                    {
                        _logger.LogInfo("[Security] SmartScreen: Desabilitado via Group Policy");
                        return false;
                    }

                    using var explorerKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer");
                    if (explorerKey?.GetValue("SmartScreenEnabled") as string == "Off")
                    {
                        _logger.LogInfo("[Security] SmartScreen: Desabilitado via Explorer key");
                        return false;
                    }

                    _logger.LogInfo("[Security] SmartScreen: Ativo");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[Security] Erro ao verificar SmartScreen: {ex.Message}");
                    return true;
                }
            });
        }

        private async Task<bool> IsRealTimeProtectionEnabledAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows Defender\Real-Time Protection");
                    var value = key?.GetValue("DisableRealtimeMonitoring");
                    bool enabled;
                    if (value == null)
                    {
                        enabled = true; // Se a chave não existe, RTP está ativo (padrão do Windows)
                    }
                    else
                    {
                        enabled = Convert.ToInt32(value) == 0;
                    }
                    _logger.LogInfo($"[Security] RealTimeProtection: DisableRealtimeMonitoring={value}, enabled={enabled}");
                    return enabled;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[Security] Erro ao verificar RealTimeProtection: {ex.Message}");
                    return false;
                }
            });
        }

        private async Task<bool> IsUACEnabledAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System");
                    var value = key?.GetValue("EnableLUA");
                    bool enabled = value != null && Convert.ToInt32(value) == 1;
                    _logger.LogInfo($"[Security] UAC: EnableLUA={value}, enabled={enabled}");
                    return enabled;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[Security] Erro ao verificar UAC: {ex.Message}");
                    return false;
                }
            });
        }

        private async Task<bool> IsTamperProtectionEnabledAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows Defender\Features");
                    if (key == null)
                    {
                        _logger.LogWarning("[Security] Chave HKLM\\...\\Windows Defender\\Features não encontrada — tentando WMI");
                        return TryGetTamperProtectionViaPowerShell();
                    }

                    var value = key.GetValue("TamperProtection");
                    if (value == null)
                    {
                        _logger.LogWarning("[Security] Valor TamperProtection não encontrado no registro — tentando WMI");
                        return TryGetTamperProtectionViaPowerShell();
                    }

                    int tamperValue;
                    try { tamperValue = Convert.ToInt32(value); }
                    catch
                    {
                        _logger.LogWarning($"[Security] Valor TamperProtection não é numérico: {value} (tipo: {value.GetType().Name})");
                        return TryGetTamperProtectionViaPowerShell();
                    }

                    // Valores comuns: 5=Ativado, 4=Desativado, 1=Ativado (Win11), 0=Desativado
                    // Alguns sistemas usam bitmask: bit 0 = enabled
                    bool isEnabled = tamperValue == 5 || tamperValue == 1 || tamperValue == 3;
                    _logger.LogInfo($"[Security] Tamper Protection registry value={tamperValue} (0x{tamperValue:X8}), enabled={isEnabled}");

                    // Se o valor for ambíguo (ex: 2, 6+), confirmar via WMI
                    if (tamperValue > 5 || (tamperValue != 0 && tamperValue != 1 && tamperValue != 4 && tamperValue != 5))
                    {
                        _logger.LogInfo($"[Security] Valor Tamper Protection ambíguo ({tamperValue}) — confirmando via WMI");
                        return TryGetTamperProtectionViaPowerShell();
                    }

                    return isEnabled;
                }
                catch (System.Security.SecurityException secEx)
                {
                    _logger.LogWarning($"[Security] Sem permissão para ler Tamper Protection via registro: {secEx.Message}");
                    return TryGetTamperProtectionViaPowerShell();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[Security] Erro ao verificar Tamper Protection: {ex.Message}");
                    return TryGetTamperProtectionViaPowerShell();
                }
            });
        }

        private bool TryGetTamperProtectionViaPowerShell()
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    @"root\Microsoft\Windows\Defender",
                    "SELECT IsTamperProtected FROM MSFT_MpComputerStatus");

                foreach (System.Management.ManagementObject obj in searcher.Get())
                {
                    var isTamperProtected = obj["IsTamperProtected"];
                    if (isTamperProtected != null)
                    {
                        bool result = Convert.ToBoolean(isTamperProtected);
                        _logger.LogInfo($"[Security] Tamper Protection via WMI: {result}");
                        return result;
                    }
                }

                _logger.LogWarning("[Security] WMI MSFT_MpComputerStatus não retornou IsTamperProtected");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Security] Fallback WMI para Tamper Protection falhou: {ex.Message}");
            }

            return false;
        }

        private async Task<bool> IsControlledFolderAccessEnabledAsync()
        {
            try
            {
                var output = await RunPowerShellAsync("(Get-MpPreference).EnableControlledFolderAccess");
                if (int.TryParse(output.Trim(), out var status))
                {
                    bool enabled = status != 0;
                    _logger.LogInfo($"[Security] ControlledFolderAccess: PowerShell value={status}, enabled={enabled}");
                    return enabled;
                }

                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows Defender\Windows Defender Exploit Guard\Controlled Folder Access");
                var regValue = key?.GetValue("EnableControlledFolderAccess");
                bool regEnabled = regValue is int regStatus && regStatus != 0;
                _logger.LogInfo($"[Security] ControlledFolderAccess: Registry value={regValue}, enabled={regEnabled}");
                return regEnabled;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Security] Erro ao verificar ControlledFolderAccess: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> IsBitLockerEnabledAsync()
        {
            try
            {
                var output = await RunPowerShellAsync("(Get-BitLockerVolume -ErrorAction SilentlyContinue | Where-Object { $_.ProtectionStatus -eq 'On' }).Count -gt 0");
                bool enabled = output.Trim().Equals("True", StringComparison.OrdinalIgnoreCase);
                _logger.LogInfo($"[Security] BitLocker: output='{output.Trim()}', enabled={enabled}");
                return enabled;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Security] Erro ao verificar BitLocker: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> IsDefenderServiceEnabledAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\WinDefend");
                    var value = key?.GetValue("Start");
                    bool enabled = value != null && Convert.ToInt32(value) != 4; // 4 = Disabled
                    _logger.LogInfo($"[Security] DefenderService: Start={value}, enabled={enabled}");
                    return enabled;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[Security] Erro ao verificar DefenderService: {ex.Message}");
                    return false;
                }
            });
        }

        private async Task<bool> IsAntivirusEnabledAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows Defender");
                    var isEnabled = key?.GetValue("IsEnabled");
                    if (isEnabled is int i)
                    {
                        bool enabled = i == 1;
                        _logger.LogInfo($"[Security] Antivirus: Defender IsEnabled={i}, enabled={enabled}");
                        return enabled;
                    }
                    _logger.LogInfo("[Security] Antivirus: Chave Defender não encontrada — assumindo ativo");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[Security] Erro ao verificar Antivirus: {ex.Message}");
                    return true;
                }
            });
        }

        private async Task<string> RunPowerShellAsync(string command)
        {
            var psPath = Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysNative", "WindowsPowerShell", "v1.0", "powershell.exe")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = psPath,
                    Arguments = $"-NoProfile -NonInteractive -Command \"{command}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            
            process.Start();
            
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning($"[Security] PowerShell timeout (10s) para comando: {command.Substring(0, Math.Min(80, command.Length))}...");
                try { process.Kill(true); } catch { }
                return string.Empty;
            }
            
            var output = await outputTask;
            var error = await errorTask;
            
            if (!string.IsNullOrWhiteSpace(error))
            {
                _logger.LogWarning($"[Security] PowerShell stderr: {error.Trim().Substring(0, Math.Min(200, error.Trim().Length))}");
            }
            
            return output;
        }

        /// <summary>
        /// Executa um script PowerShell SALVO EM ARQUIVO .ps1 (via -File).
        /// Evita o limite de 8191 caracteres do cmd.exe e problemas de quoting.
        /// </summary>
        private async Task<string> RunPowerShellFileAsync(string script, string prefix = "Script")
        {
            var psPath = Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysNative", "WindowsPowerShell", "v1.0", "powershell.exe")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");

            var tempDir = Path.Combine(Path.GetTempPath(), "VoltrisOptimizer");
            Directory.CreateDirectory(tempDir);
            var scriptPath = Path.Combine(tempDir, $"{prefix}_{Guid.NewGuid():N}.ps1");

            try
            {
                await File.WriteAllTextAsync(scriptPath, script, Encoding.UTF8);
                _logger.LogInfo($"[Security] Script salvo em: {scriptPath} ({script.Length} chars)");

                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = psPath,
                        Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    }
                };

                process.Start();
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                try
                {
                    await process.WaitForExitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning($"[Security] PowerShell timeout (15s) para script {prefix}");
                    try { process.Kill(true); } catch { }
                    return string.Empty;
                }

                var output = await outputTask;
                var error = await errorTask;

                if (!string.IsNullOrWhiteSpace(error))
                    _logger.LogWarning($"[Security] PS stderr: {(error.Length > 500 ? error[..500] + "..." : error)}");

                _logger.LogInfo($"[Security] PS exit code: {process.ExitCode}, stdout: {(output?.Length ?? 0)} chars");
                return output ?? string.Empty;
            }
            finally
            {
                try { if (File.Exists(scriptPath)) File.Delete(scriptPath); } catch { }
            }
        }

        private async Task RunCommandAsync(string command)
        {
            var cmdPath = Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysNative", "cmd.exe")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "cmd.exe");

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = cmdPath,
                    Arguments = $"/c {command}",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            await process.WaitForExitAsync();
        }

        #endregion
    }
}
