using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services
{
    public class WindowsUpdateInfo
    {
        public string Title { get; set; } = string.Empty;
        public string KBArticle { get; set; } = string.Empty;
        public string Severity { get; set; } = "Normal";
        public long SizeBytes { get; set; }
        public string SizeDisplay => SizeBytes > 0
            ? SizeBytes >= 1_073_741_824 ? $"{SizeBytes / 1_073_741_824.0:F1} GB"
            : SizeBytes >= 1_048_576 ? $"{SizeBytes / 1_048_576.0:F1} MB"
            : $"{SizeBytes / 1024.0:F0} KB"
            : "—";
        public bool IsCritical => Severity.Contains("Critical", StringComparison.OrdinalIgnoreCase)
                               || Severity.Contains("Crítica", StringComparison.OrdinalIgnoreCase);
    }

    public class WindowsUpdateCheckResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public List<WindowsUpdateInfo> PendingUpdates { get; set; } = new();
        public bool IsUpToDate => Success && PendingUpdates.Count == 0;
    }

    public class WindowsUpdateService
    {
        private readonly ILoggingService? _logger;
        private readonly LocalizationService _loc;

        private static string GetPsPath()
        {
            // Em processo 32-bit em Windows 64-bit, usar SysNative em vez de System32
            // para acessar o PowerShell 64-bit verdadeiro (necessário para COM Microsoft.Update.Session)
            var dir = Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess
                ? "SysNative"
                : "System32";
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                dir, "WindowsPowerShell", "v1.0", "powershell.exe");
        }

        public WindowsUpdateService()
        {
            try { _logger = App.LoggingService; } catch { _logger = null; }
            _loc = LocalizationService.Instance;
            _logger?.LogInfo($"[WU] WindowsUpdateService inicializado. PS path: {GetPsPath()}");
            LogSystemDiagnostics();
        }

        private void LogSystemDiagnostics()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"[WU_DIAG] OS: {Environment.OSVersion}");
                sb.AppendLine($"[WU_DIAG] 64-bit OS: {Environment.Is64BitOperatingSystem}, 64-bit Process: {Environment.Is64BitProcess}");
                sb.AppendLine($"[WU_DIAG] CurrentCulture: {System.Globalization.CultureInfo.CurrentCulture.Name}");
                sb.AppendLine($"[WU_DIAG] Localization Language: {_loc.CurrentLanguage}");

                using var wuKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU");
                var noAutoUpdate = wuKey?.GetValue("NoAutoUpdate");
                sb.AppendLine($"[WU_DIAG] NoAutoUpdate policy: {noAutoUpdate ?? "(not set)"}");

                using var svcKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\wuauserv");
                var svcStart = svcKey?.GetValue("Start");
                sb.AppendLine($"[WU_DIAG] wuauserv Start: {svcStart ?? "(not found)"}");

                _logger?.LogInfo(sb.ToString());
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[WU_DIAG] Failed to log system diagnostics: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Scripts PS1 — salvos em arquivo temp para evitar problemas de escape
        // Nota: aspas duplas dentro de @"..." devem ser escritas como ""
        // ─────────────────────────────────────────────────────────────────────
        private const string CheckScript =
            "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8\r\n" +
            "$OutputEncoding = [System.Text.Encoding]::UTF8\r\n" +
            "\r\n" +
            "function Ensure-Service {\r\n" +
            "    param([string]$Name)\r\n" +
            "    try {\r\n" +
            "        $svc = Get-Service -Name $Name -ErrorAction SilentlyContinue\r\n" +
            "        if ($svc -and $svc.Status -eq 'Stopped') {\r\n" +
            "            Start-Service $Name -ErrorAction SilentlyContinue\r\n" +
            "            Start-Sleep -Seconds 2\r\n" +
            "        }\r\n" +
            "    } catch { }\r\n" +
            "}\r\n" +
            "\r\n" +
            "function Try-CreateSession {\r\n" +
            "    $maxRetry = 3\r\n" +
            "    for ($attempt = 1; $attempt -le $maxRetry; $attempt++) {\r\n" +
            "        try {\r\n" +
            "            $s = New-Object -ComObject Microsoft.Update.Session -ErrorAction Stop\r\n" +
            "            if ($null -ne $s) { return $s }\r\n" +
            "        } catch {\r\n" +
            "            if ($attempt -lt $maxRetry) { Start-Sleep -Seconds ($attempt * 2) }\r\n" +
            "        }\r\n" +
            "    }\r\n" +
            "    return $null\r\n" +
            "}\r\n" +
            "\r\n" +
            "try {\r\n" +
            "    Ensure-Service 'wuauserv'\r\n" +
            "    Ensure-Service 'BITS'\r\n" +
            "\r\n" +
            "    $session = Try-CreateSession\r\n" +
            "    if ($null -eq $session) {\r\n" +
            "        Write-Error 'VOLTRIS_ERR: Nao foi possivel criar Microsoft.Update.Session apos 3 tentativas'\r\n" +
            "        exit 1\r\n" +
            "    }\r\n" +
            "\r\n" +
            "    $searcher = $null\r\n" +
            "    try {\r\n" +
            "        $searcher = $session.CreateUpdateSearcher()\r\n" +
            "    } catch {\r\n" +
            "        Write-Error \"VOLTRIS_ERR: Falha ao criar UpdateSearcher: $($_.Exception.Message)\"\r\n" +
            "        exit 1\r\n" +
            "    }\r\n" +
            "\r\n" +
            "    if ($null -eq $searcher) {\r\n" +
            "        Write-Error 'VOLTRIS_ERR: UpdateSearcher retornou nulo'\r\n" +
            "        exit 1\r\n" +
            "    }\r\n" +
            "\r\n" +
            "    # Usar Search() sincrono - mais estavel que BeginSearch/EndSearch\r\n" +
            "    $result = $null\r\n" +
            "    $searchAttempt = 0\r\n" +
            "    $searchMaxRetry = 3\r\n" +
            "    while ($searchAttempt -lt $searchMaxRetry) {\r\n" +
            "        $searchAttempt++\r\n" +
            "        try {\r\n" +
            "            $result = $searcher.Search('IsInstalled=0 and Type=''Software'' and IsHidden=0')\r\n" +
            "            break\r\n" +
            "        } catch {\r\n" +
            "            $errMsg = $_.Exception.Message\r\n" +
            "            if ($searchAttempt -ge $searchMaxRetry) {\r\n" +
            "                Write-Error \"VOLTRIS_ERR: Falha na busca (tentativa $searchAttempt): $errMsg\"\r\n" +
            "                exit 1\r\n" +
            "            }\r\n" +
            "            Start-Sleep -Seconds ($searchAttempt * 3)\r\n" +
            "        }\r\n" +
            "    }\r\n" +
            "\r\n" +
            "    if ($null -eq $result -or $null -eq $result.Updates -or $result.Updates.Count -eq 0) {\r\n" +
            "        Write-Output 'SISTEMA_ATUALIZADO'\r\n" +
            "        exit 0\r\n" +
            "    }\r\n" +
            "\r\n" +
            "    $updates = @()\r\n" +
            "    foreach ($u in $result.Updates) {\r\n" +
            "        if ($null -eq $u) { continue }\r\n" +
            "        $kb = ''\r\n" +
            "        try { if ($null -ne $u.KBArticleIDs -and $u.KBArticleIDs.Count -gt 0) { $kb = 'KB' + $u.KBArticleIDs[0] } } catch { }\r\n" +
            "        $sev = 'Normal'\r\n" +
            "        try { if ($null -ne $u.MsrcSeverity -and $u.MsrcSeverity -ne '') { $sev = $u.MsrcSeverity } } catch { }\r\n" +
            "        $size = 0\r\n" +
            "        try { if ($null -ne $u.MaxDownloadSize) { $size = [long]$u.MaxDownloadSize } } catch { }\r\n" +
            "        $title = 'Atualizacao sem titulo'\r\n" +
            "        try { if ($null -ne $u.Title) { $title = ($u.Title -replace '[^\\x20-\\x7E\\xC0-\\xFF]', '?').Trim() } } catch { }\r\n" +
            "        $updates += [PSCustomObject]@{ Title = $title; KB = $kb; Severity = $sev; Size = $size }\r\n" +
            "    }\r\n" +
            "\r\n" +
            "    if ($updates.Count -eq 0) {\r\n" +
            "        Write-Output 'SISTEMA_ATUALIZADO'\r\n" +
            "    } else {\r\n" +
            "        $updates | ConvertTo-Json -Depth 2 -Compress\r\n" +
            "    }\r\n" +
            "} catch {\r\n" +
            "    $msg = if ($null -ne $_.Exception) { $_.Exception.Message } else { $_.ToString() }\r\n" +
            "    Write-Error \"VOLTRIS_ERR: $msg\"\r\n" +
            "    exit 1\r\n" +
            "}\r\n";

        private const string InstallScript =
            "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8\r\n" +
            "$OutputEncoding = [System.Text.Encoding]::UTF8\r\n" +
            "$ProgressPreference = 'SilentlyContinue'\r\n" +
            "\r\n" +
            "function Ensure-Service {\r\n" +
            "    param([string]$Name)\r\n" +
            "    try {\r\n" +
            "        $svc = Get-Service -Name $Name -ErrorAction SilentlyContinue\r\n" +
            "        if ($svc -and $svc.Status -eq 'Stopped') {\r\n" +
            "            Write-Output \"SERVICE_STARTING:$Name\"\r\n" +
            "            Start-Service $Name -ErrorAction SilentlyContinue\r\n" +
            "            Start-Sleep -Seconds 2\r\n" +
            "            $svc = Get-Service -Name $Name -ErrorAction SilentlyContinue\r\n" +
            "            Write-Output \"SERVICE_STATUS:$Name=$($svc.Status)\"\r\n" +
            "        } else {\r\n" +
            "            Write-Output \"SERVICE_STATUS:$Name=$($svc.Status)\"\r\n" +
            "        }\r\n" +
            "    } catch { Write-Output \"SERVICE_ERROR:$Name=$($_.Exception.Message)\" }\r\n" +
            "}\r\n" +
            "\r\n" +
            "function Get-HResultString {\r\n" +
            "    param([int]$HResult)\r\n" +
            "    switch ($HResult) {\r\n" +
            "        (-2147024891) { '0x80070005 - ACCESS_DENIED' }\r\n" +
            "        (-2147024894) { '0x80070002 - FILE_NOT_FOUND' }\r\n" +
            "        (-2147024882) { '0x8007000E - OUT_OF_MEMORY' }\r\n" +
            "        (-2145124329) { '0x8024001E - WU_E_NO_UPDATE' }\r\n" +
            "        (-2145124330) { '0x8024001D - WU_E_NO_INTERACTIVE_USER' }\r\n" +
            "        (-2145124318) { '0x80240022 - WU_E_INSTALL_NOT_ALLOWED' }\r\n" +
            "        (-2145124308) { '0x8024002C - WU_E_NO_CONNECTION' }\r\n" +
            "        (-2145091583) { '0x80242BFF - WU_E_DRV_NO_METADATA' }\r\n" +
            "        (-2145124332) { '0x8024001C - WU_E_NO_SERVICE' }\r\n" +
            "        (-2145124283) { '0x80240045 - WU_E_CALL_CANCELLED' }\r\n" +
            "        (-2145124281) { '0x80240047 - WU_E_NO_CONNECTION' }\r\n" +
            "        default { \"0x$(('{0:X8}' -f ($HResult -band 0xFFFFFFFF)))\" }\r\n" +
            "    }\r\n" +
            "}\r\n" +
            "\r\n" +
            "function Is-DefenderDefinitionUpdate {\r\n" +
            "    param($update)\r\n" +
            "    $t = try { $update.Title } catch { '' }\r\n" +
            "    return ($t -match 'Security Intelligence|Defender Antivirus|KB2267602|Definition Update')\r\n" +
            "}\r\n" +
            "\r\n" +
            "function Check-WUBlockers {\r\n" +
            "    Write-Output \"WU_CHECK_BLOCKERS:START\"\r\n" +
            "    try {\r\n" +
            "        $wu = Get-ItemProperty -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate\\AU' -Name 'NoAutoUpdate' -ErrorAction SilentlyContinue\r\n" +
            "        if ($wu -and $wu.NoAutoUpdate -eq 1) {\r\n" +
            "            Write-Output \"WU_BLOCKER:NoAutoUpdate=1\"\r\n" +
            "            return $false\r\n" +
            "        }\r\n" +
            "    } catch { Write-Output \"WU_BLOCKER_CHECK:NoAutoUpdate=$($_.Exception.Message)\" }\r\n" +
            "    try {\r\n" +
            "        $wua = Get-ItemProperty -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate' -Name 'DisableWindowsUpdateAccess' -ErrorAction SilentlyContinue\r\n" +
            "        if ($wua -and $wua.DisableWindowsUpdateAccess -eq 1) {\r\n" +
            "            Write-Output \"WU_BLOCKER:DisableWindowsUpdateAccess=1\"\r\n" +
            "            return $false\r\n" +
            "        }\r\n" +
            "    } catch { }\r\n" +
            "    try {\r\n" +
            "        $wus = Get-Service -Name 'wuauserv' -ErrorAction SilentlyContinue\r\n" +
            "        if ($wus -and $wus.Status -ne 'Running') {\r\n" +
            "            Write-Output \"WU_BLOCKER:wuauserv=$($wus.Status)\"\r\n" +
            "            Start-Service -Name 'wuauserv' -ErrorAction SilentlyContinue\r\n" +
            "            Start-Sleep -Seconds 2\r\n" +
            "        }\r\n" +
            "    } catch { }\r\n" +
            "    try {\r\n" +
            "        $paused = Get-ItemProperty -Path 'HKLM:\\SOFTWARE\\Microsoft\\WindowsUpdate\\UX\\Settings' -Name 'PauseFeatureUpdatesStartTime' -ErrorAction SilentlyContinue\r\n" +
            "        if ($paused -and $paused.PauseFeatureUpdatesStartTime) {\r\n" +
            "            Write-Output \"WU_INFO:FeatureUpdatesPaused=$($paused.PauseFeatureUpdatesStartTime)\"\r\n" +
            "        }\r\n" +
            "    } catch { }\r\n" +
            "    try {\r\n" +
            "        $metered = Get-ItemProperty -Path 'HKLM:\\SOFTWARE\\Microsoft\\WindowsNT\\CurrentVersion\\NetworkList\\DefaultMediaCost' -Name '3G' -ErrorAction SilentlyContinue\r\n" +
            "        if ($metered -and $metered.'3G' -eq 2) {\r\n" +
            "            Write-Output \"WU_INFO:MeteredNetwork=2\"\r\n" +
            "        }\r\n" +
            "    } catch { }\r\n" +
            "    Write-Output \"WU_CHECK_BLOCKERS:OK\"\r\n" +
            "    return $true\r\n" +
            "}\r\n" +
            "\r\n" +
            "function Write-InstallResultDetail {\r\n" +
            "    param($index, $title, $rc, $hresultStr, $reboot)\r\n" +
            "    $shortTitle = if ($title.Length -gt 60) { $title.Substring(0, 57) + '...' } else { $title }\r\n" +
            "    $rebootFlag = if ($reboot) { ' [REBOOT]' } else { '' }\r\n" +
            "    Write-Output \"INSTALL_RESULT_DETAIL:$index|RC=$rc|HR=$hresultStr|$shortTitle$rebootFlag\"\r\n" +
            "}\r\n" +
            "\r\n" +
            "function Install-SingleUpdate {\r\n" +
            "    param($session, $update, $index, $maxRetries)\r\n" +
            "    $uTitle = try { $update.Title } catch { \"Update #$index\" }\r\n" +
            "    $installer = $session.CreateUpdateInstaller()\r\n" +
            "    $installer.AllowSourcePrompts = $false\r\n" +
            "    $isDefenderDef = Is-DefenderDefinitionUpdate -update $update\r\n" +
            "    Write-Output \"INSTALL_RETRY_MAX:$index|$maxRetries\"\r\n" +
            "\r\n" +
            "    for ($attempt = 1; $attempt -le $maxRetries; $attempt++) {\r\n" +
            "        Write-Output \"INSTALL_ATTEMPT:$index|$attempt\"\r\n" +
            "        try {\r\n" +
            "            $col = New-Object -ComObject Microsoft.Update.UpdateColl\r\n" +
            "            $col.Add($update) | Out-Null\r\n" +
            "            $installer.Updates = $col\r\n" +
            "            $r = $installer.Install()\r\n" +
            "\r\n" +
            "            if ($null -ne $r) {\r\n" +
            "                $rc = $r.ResultCode\r\n" +
            "                $hResult = $null\r\n" +
            "                try { $hResult = $r.HResult } catch { }\r\n" +
            "                $hResultStr = if ($null -ne $hResult) { Get-HResultString -HResult $hResult } else { 'N/A' }\r\n" +
            "                Write-InstallResultDetail -index $index -title $uTitle -rc $rc -hresultStr $hResultStr -reboot $r.RebootRequired\r\n" +
            "\r\n" +
            "                if ($rc -eq 2 -or $rc -eq 3) {\r\n" +
            "                    Write-Output \"INSTALADO_SUCESSO:$index\"\r\n" +
            "                    return @{ Success = $true; Reboot = $r.RebootRequired; Details = '' }\r\n" +
            "                }\r\n" +
            "\r\n" +
            "                if ($rc -eq 4) {\r\n" +
            "                    $isNotAllowed = ($null -ne $hResult -and $hResult -eq -2145124318) # WU_E_INSTALL_NOT_ALLOWED\r\n" +
            "                    $isAccessDenied = ($null -ne $hResult -and $hResult -eq -2147024891) # ACCESS_DENIED\r\n" +
            "\r\n" +
            "                    if ($isNotAllowed -and $isDefenderDef -and $attempt -lt $maxRetries) {\r\n" +
            "                        Write-Output \"INSTALL_FALLBACK_MP:$index|Tentando Update-MpSignature como fallback...\"\r\n" +
            "                        try {\r\n" +
            "                            Update-MpSignature -ErrorAction SilentlyContinue | Out-Null\r\n" +
            "                            Write-Output \"INSTALL_FALLBACK_MP_OK:$index\"\r\n" +
            "                            return @{ Success = $true; Reboot = $false; Details = 'Instalado via Update-MpSignature' }\r\n" +
            "                        } catch {\r\n" +
            "                            $mpErr = $_.Exception.Message\r\n" +
            "                            Write-Output \"INSTALL_FALLBACK_MP_FAIL:$index|$mpErr\"\r\n" +
            "                        }\r\n" +
            "                    }\r\n" +
            "\r\n" +
            "                    if ($isNotAllowed -or $isAccessDenied) {\r\n" +
            "                        Write-Output \"INSTALL_BLOCKED:$index|Nao sera reintentado\"\r\n" +
            "                        return @{ Success = $false; Reboot = $false; Details = \"$uTitle (RC=$rc, HR=$hResultStr) - BLOQUEADO\" }\r\n" +
            "                    }\r\n" +
            "                }\r\n" +
            "            } else {\r\n" +
            "                Write-Output \"INSTALL_RESULT_NULL:$index\"\r\n" +
            "                return @{ Success = $false; Reboot = $false; Details = \"$uTitle (Resultado nulo)\" }\r\n" +
            "            }\r\n" +
            "        } catch {\r\n" +
            "            $exMsg = $_.Exception.Message\r\n" +
            "            Write-Output \"INSTALL_EXCEPTION:$index|$attempt|$exMsg\"\r\n" +
            "            if ($attempt -lt $maxRetries) {\r\n" +
            "                Write-Output \"INSTALL_RETRY_WAIT:$index|$attempt\"\r\n" +
            "                Start-Sleep -Seconds ($attempt * 5)\r\n" +
            "            } else {\r\n" +
            "                $errMsg = \"$uTitle (excecao: $exMsg)\"\r\n" +
            "                return @{ Success = $false; Reboot = $false; Details = $errMsg }\r\n" +
            "            }\r\n" +
            "        }\r\n" +
            "        Start-Sleep -Seconds 2\r\n" +
            "    }\r\n" +
            "\r\n" +
            "    return @{ Success = $false; Reboot = $false; Details = \"$uTitle (falha apos $maxRetries tentativas)\" }\r\n" +
            "}\r\n" +
            "\r\n" +
            "try {\r\n" +
            "    Write-Output \"PS_VERSION:$($PSVersionTable.PSVersion)\"\r\n" +
            "    Write-Output \"OS_VERSION:$((Get-WmiObject Win32_OperatingSystem).Version)\"\r\n" +
            "    Write-Output \"OS_BUILD:$((Get-WmiObject Win32_OperatingSystem).BuildNumber)\"\r\n" +
            "    Write-Output \"PS_CULTURE:$((Get-Culture).Name)\"\r\n" +
            "    Write-Output \"PS_ELEVATED:$([bool](-not (New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)))\"\r\n" +
            "\r\n" +
            "    if (-not (Check-WUBlockers)) {\r\n" +
            "        Write-Error 'VOLTRIS_ERR: Instalacao bloqueada por politica do Windows Update (NoAutoUpdate ou DisableWindowsUpdateAccess)'\r\n" +
            "        exit 1\r\n" +
            "    }\r\n" +
            "\r\n" +
            "    Ensure-Service 'wuauserv'\r\n" +
            "    Ensure-Service 'BITS'\r\n" +
            "\r\n" +
            "    $session = $null\r\n" +
            "    for ($attempt = 1; $attempt -le 3; $attempt++) {\r\n" +
            "        try { $session = New-Object -ComObject Microsoft.Update.Session -ErrorAction Stop; break } catch {\r\n" +
            "            Write-Output \"SESSION_RETRY:$attempt $($_.Exception.Message)\"\r\n" +
            "            if ($attempt -lt 3) { Start-Sleep -Seconds ($attempt * 2) }\r\n" +
            "        }\r\n" +
            "    }\r\n" +
            "    if ($null -eq $session) { Write-Error 'VOLTRIS_ERR: Nao foi possivel criar Microsoft.Update.Session'; exit 1 }\r\n" +
            "    Write-Output \"SESSION_CREATED:OK\"\r\n" +
            "\r\n" +
            "    $searcher = $null\r\n" +
            "    try { $searcher = $session.CreateUpdateSearcher() } catch {\r\n" +
            "        Write-Error \"VOLTRIS_ERR: Falha ao criar UpdateSearcher: $($_.Exception.Message)\"\r\n" +
            "        exit 1\r\n" +
            "    }\r\n" +
            "    if ($null -eq $searcher) { Write-Error 'VOLTRIS_ERR: UpdateSearcher nulo'; exit 1 }\r\n" +
            "    Write-Output \"SEARCHER_CREATED:OK\"\r\n" +
            "\r\n" +
            "    $searchResult = $null\r\n" +
            "    try {\r\n" +
            "        $searchResult = $searcher.Search('IsInstalled=0 and Type=''Software'' and IsHidden=0')\r\n" +
            "    } catch {\r\n" +
            "        Write-Error \"VOLTRIS_ERR: Falha na busca: $($_.Exception.Message)\"\r\n" +
            "        exit 1\r\n" +
            "    }\r\n" +
            "\r\n" +
            "    if ($null -eq $searchResult -or $null -eq $searchResult.Updates -or $searchResult.Updates.Count -eq 0) {\r\n" +
            "        Write-Output 'NENHUMA_ATUALIZACAO'\r\n" +
            "        exit 0\r\n" +
            "    }\r\n" +
            "    Write-Output \"UPDATES_FOUND:$($searchResult.Updates.Count)\"\r\n" +
            "\r\n" +
            "    foreach ($u in $searchResult.Updates) {\r\n" +
            "        if ($null -ne $u -and -not $u.EulaAccepted) { try { $u.AcceptEula() } catch { Write-Output \"EULA_ERROR:$($u.Title):$($_.Exception.Message)\" } }\r\n" +
            "    }\r\n" +
            "\r\n" +
            "    $successfulUpdates = @()\r\n" +
            "    $failedUpdates = @()\r\n" +
            "    $downloader = $session.CreateUpdateDownloader()\r\n" +
            "\r\n" +
            "    for ($i = 0; $i -lt $searchResult.Updates.Count; $i++) {\r\n" +
            "        $u = $searchResult.Updates.Item($i)\r\n" +
            "        $uTitle = try { $u.Title } catch { \"Update #$i\" }\r\n" +
            "        Write-Output \"DOWNLOADING:$i|$uTitle\"\r\n" +
            "\r\n" +
            "        $downloaded = $false\r\n" +
            "        $downloadError = $null\r\n" +
            "        for ($retry = 0; $retry -lt 3; $retry++) {\r\n" +
            "            try {\r\n" +
            "                $col = New-Object -ComObject Microsoft.Update.UpdateColl\r\n" +
            "                $col.Add($u) | Out-Null\r\n" +
            "                $downloader.Updates = $col\r\n" +
            "                $dlResult = $downloader.Download()\r\n" +
            "                $dlCode = if ($null -ne $dlResult) { $dlResult.ResultCode } else { 'null' }\r\n" +
            "                Write-Output \"DOWNLOAD_RESULT:$i|$dlCode\"\r\n" +
            "                $downloaded = $true\r\n" +
            "                break\r\n" +
            "            } catch {\r\n" +
            "                $downloadError = $_.Exception.Message\r\n" +
            "                Write-Output \"DOWNLOAD_RETRY:$i|$retry|$downloadError\"\r\n" +
            "                if ($retry -lt 2) { Start-Sleep -Seconds (($retry + 1) * 3) }\r\n" +
            "            }\r\n" +
            "        }\r\n" +
            "\r\n" +
            "        if ($downloaded) {\r\n" +
            "            $successfulUpdates += $u\r\n" +
            "            Write-Output \"DOWNLOADED_OK:$i\"\r\n" +
            "        } else {\r\n" +
            "            $failedUpdates += @{ Title = $uTitle; Error = $downloadError }\r\n" +
            "            Write-Output \"DOWNLOAD_FAILED:$i|$downloadError\"\r\n" +
            "        }\r\n" +
            "    }\r\n" +
            "\r\n" +
            "    if ($successfulUpdates.Count -eq 0) {\r\n" +
            "        $errDetails = ($failedUpdates | ForEach-Object { \"$($_.Title): $($_.Error)\" }) -join '; '\r\n" +
            "        Write-Error \"VOLTRIS_ERR: Falha ao baixar todas as atualizacoes: $errDetails\"\r\n" +
            "        exit 1\r\n" +
            "    }\r\n" +
            "\r\n" +
            "    $reboot = $false\r\n" +
            "    $allInstallResults = @()\r\n" +
            "    Write-Output 'INSTALANDO'\r\n" +
            "\r\n" +
            "    $defenderFallbackUsed = $false\r\n" +
            "    for ($i = 0; $i -lt $successfulUpdates.Count; $i++) {\r\n" +
            "        $u = $successfulUpdates[$i]\r\n" +
            "        $uTitle = try { $u.Title } catch { \"Update #$i\" }\r\n" +
            "        Write-Output \"INSTALLING:$i|$uTitle\"\r\n" +
            "        $result = Install-SingleUpdate -session $session -update $u -index $i -maxRetries 2\r\n" +
            "        if ($result.Success) {\r\n" +
            "            if ($result.Reboot) { $reboot = $true }\r\n" +
            "            if ($result.Details -match 'Update-MpSignature') { $defenderFallbackUsed = $true }\r\n" +
            "            Write-Output \"INSTALLED_OK:$i\"\r\n" +
            "        } else {\r\n" +
            "            $allInstallResults += $result.Details\r\n" +
            "            Write-Output \"INSTALLED_FAIL:$i|$($result.Details)\"\r\n" +
            "        }\r\n" +
            "    }\r\n" +
            "\r\n" +
            "    Write-Output \"TOTAL_SUCCESS:$totalSuccess\"\r\n" +
            "    if ($failedUpdates.Count -gt 0) { Write-Output \"FAILED_DOWNLOAD:$($failedUpdates.Count)\" }\r\n" +
            "    if ($allInstallResults.Count -gt 0) { Write-Output \"FAILED_INSTALL:$($allInstallResults.Count)\" }\r\n" +
            "\r\n" +
            "    # ── VERIFICAÇÃO PÓS-INSTALAÇÃO ──\r\n" +
            "    Write-Output 'VERIFYING_AFTER_INSTALL'\r\n" +
            "    try {\r\n" +
            "        $verifySearch = $searcher.Search('IsInstalled=0 and Type=''Software'' and IsHidden=0')\r\n" +
            "        if ($verifySearch -and $verifySearch.Updates -and $verifySearch.Updates.Count -gt 0) {\r\n" +
            "            # Coletar títulos dos updates que ainda estão pendentes\r\n" +
            "            $rawPending = @()\r\n" +
            "            for ($vi = 0; $vi -lt $verifySearch.Updates.Count; $vi++) {\r\n" +
            "                $vu = $verifySearch.Updates.Item($vi)\r\n" +
            "                $vuTitle = try { $vu.Title } catch { \"Update #$vi\" }\r\n" +
            "                $rawPending += @{ Index = $vi; Title = $vuTitle }\r\n" +
            "                Write-Output \"RAW_PENDING:$vi|$vuTitle\"\r\n" +
            "            }\r\n" +
            "            Write-Output \"RAW_PENDING_COUNT:$($rawPending.Count)\"\r\n" +
            "\r\n" +
            "            # Se Update-MpSignature foi usado como fallback, pendências de definições Defender são ESPÚRIAS\r\n" +
            "            $mpSignatureRan = $defenderFallbackUsed\r\n" +
            "            if (-not $mpSignatureRan) {\r\n" +
            "                foreach ($ir in $allInstallResults) {\r\n" +
            "                    if ($ir -match 'Update-MpSignature') { $mpSignatureRan = $true; break }\r\n" +
            "                }\r\n" +
            "            }\r\n" +
            "            Write-Output \"DEFENDER_FALLBACK_RAN:$mpSignatureRan\"\r\n" +
            "\r\n" +
            "            $stillPending = @()\r\n" +
            "            foreach ($rp in $rawPending) {\r\n" +
            "                $isDefenderDef = $rp.Title -match 'defender|definition|antivirus|security intelligence|signature|KB2267602|kb2267602|protecao|proteção'\r\n" +
            "                if ($isDefenderDef -and $mpSignatureRan) {\r\n" +
            "                    # Verificar se as definições do Defender estão realmente atualizadas\r\n" +
            "                    try {\r\n" +
            "                        $mpStatus = Get-MpComputerStatus -ErrorAction SilentlyContinue\r\n" +
            "                        if ($mpStatus -and $mpStatus.AntivirusSignatureAge -le 1) {\r\n" +
            "                            Write-Output \"DEFENDER_ALREADY_CURRENT:$($rp.Index)|$($rp.Title) (SignatureAge=$($mpStatus.AntivirusSignatureAge)d)\"\r\n" +
            "                            continue\r\n" +
            "                        }\r\n" +
            "                    } catch {\r\n" +
            "                        Write-Output \"DEFENDER_VERIFY_FAIL:$($rp.Index)|$($_.Exception.Message)\"\r\n" +
            "                    }\r\n" +
            "                }\r\n" +
            "                $stillPending += $rp.Title\r\n" +
            "                Write-Output \"STILL_PENDING:$($rp.Index)|$($rp.Title)\"\r\n" +
            "            }\r\n" +
            "            Write-Output \"STILL_PENDING_COUNT:$($stillPending.Count)\"\r\n" +
            "\r\n" +
            "            # Recalcular totalSuccess: descontar updates que ainda estão pendentes\r\n" +
            "            $verifiedSuccess = $totalSuccess\r\n" +
            "            if ($stillPending.Count -ge $verifiedSuccess) {\r\n" +
            "                $verifiedSuccess = 0\r\n" +
            "            } else {\r\n" +
            "                $verifiedSuccess = $verifiedSuccess - $stillPending.Count\r\n" +
            "            }\r\n" +
            "            $totalSuccess = $verifiedSuccess\r\n" +
            "            Write-Output \"VERIFIED_TOTAL_SUCCESS:$totalSuccess\"\r\n" +
            "            if ($stillPending.Count -gt 0) {\r\n" +
            "                $pendingDetail = ($stillPending -join '; ').Substring(0, [Math]::Min(300, ($stillPending -join '; ').Length))\r\n" +
            "                $allInstallResults += \"Ainda pendente: $pendingDetail\"\r\n" +
            "            }\r\n" +
            "        } else {\r\n" +
            "            Write-Output \"VERIFY_ALL_INSTALLED:OK\"\r\n" +
            "        }\r\n" +
            "    } catch {\r\n" +
            "        Write-Output \"VERIFY_ERROR:$($_.Exception.Message)\"\r\n" +
            "    }\r\n" +
            "\r\n" +
            "    if ($totalSuccess -gt 0) {\r\n" +
            "        Write-Output 'CONCLUIDO'\r\n" +
            "        if ($reboot) { Write-Output 'REBOOT_REQUIRED' }\r\n" +
            "        if ($allInstallResults.Count -gt 0 -or $failedUpdates.Count -gt 0) {\r\n" +
            "            $warnings = @()\r\n" +
            "            if ($failedUpdates.Count -gt 0) { $warnings += \"$($failedUpdates.Count) falharam download\" }\r\n" +
            "            if ($allInstallResults.Count -gt 0) { $warnings += \"$($allInstallResults.Count) falharam instalacao\" }\r\n" +
            "            Write-Output \"PARCIAL:$($warnings -join '; ')\"\r\n" +
            "        }\r\n" +
            "    } else {\r\n" +
            "        $errDetail = if ($allInstallResults.Count -gt 0) { ($allInstallResults -join '; ').Substring(0, [Math]::Min(500, ($allInstallResults -join '; ').Length)) } else { 'Nenhuma atualizacao foi instalada' }\r\n" +
            "        Write-Error \"VOLTRIS_ERR: Nenhuma atualizacao pode ser instalada. Detalhes: $errDetail\"\r\n" +
            "        exit 1\r\n" +
            "    }\r\n" +
            "} catch {\r\n" +
            "    $msg = if ($null -ne $_.Exception) { $_.Exception.Message } else { $_.ToString() }\r\n" +
            "    $line = if ($null -ne $_.InvocationInfo) { $_.InvocationInfo.ScriptLineNumber } else { '?' }\r\n" +
            "    Write-Output \"CATCH_ERROR_AT:Line=$line\"\r\n" +
            "    Write-Error \"VOLTRIS_ERR: (Line $line) $msg\"\r\n" +
            "    exit 1\r\n" +
            "}\r\n";

        // ─────────────────────────────────────────────────────────────────────
        // CHECK
        // ─────────────────────────────────────────────────────────────────────
        public async Task<WindowsUpdateCheckResult> CheckForUpdatesAsync(CancellationToken ct = default)
        {
            _logger?.LogInfo("[WU] ▶ Iniciando verificação de atualizações do Windows...");
            var result = new WindowsUpdateCheckResult();

            try
            {
                var sw = Stopwatch.StartNew();
                string output;

                try
                {
                    output = await RunPsScriptAsync(CheckScript, ct);
                }
                catch (Exception psEx)
                {
                    _logger?.LogError($"[WU] ❌ Falha ao executar script PS de verificação: {psEx.Message}", psEx);
                    result.Success = false;
                    result.ErrorMessage = psEx.Message;
                    return result;
                }

                sw.Stop();
                _logger?.LogInfo($"[WU] Script concluído em {sw.ElapsedMilliseconds}ms");
                _logger?.LogInfo($"[WU] Output bruto ({output.Length} chars): {(output.Length > 500 ? output[..500] + "..." : output)}");

                if (string.IsNullOrWhiteSpace(output) || output.Contains("SISTEMA_ATUALIZADO"))
                {
                    _logger?.LogInfo("[WU] ✅ Sistema já está atualizado (sem atualizações pendentes)");
                    result.Success = true;
                    return result;
                }

                var json = output.Trim();
                if (!json.StartsWith("["))
                {
                    _logger?.LogInfo("[WU] Output não é array — envolvendo em array (1 atualização)");
                    json = $"[{json}]";
                }

                List<JsonElement>? items;
                try
                {
                    items = JsonSerializer.Deserialize<List<JsonElement>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                    _logger?.LogInfo($"[WU] JSON desserializado: {items?.Count ?? 0} item(ns)");
                }
                catch (JsonException jsonEx)
                {
                    _logger?.LogError($"[WU] ❌ Falha ao desserializar JSON: {jsonEx.Message}");
                    _logger?.LogError($"[WU] JSON problemático: {json[..Math.Min(300, json.Length)]}");
                    result.Success = false;
                    result.ErrorMessage = jsonEx.Message;
                    return result;
                }

                if (items != null)
                {
                    foreach (var item in items)
                    {
                        var info = new WindowsUpdateInfo
                        {
                            Title     = GetString(item, "Title"),
                            KBArticle = GetString(item, "KB"),
                            Severity  = GetString(item, "Severity", "Normal"),
                            SizeBytes = GetLong(item, "Size")
                        };
                        result.PendingUpdates.Add(info);
                        _logger?.LogInfo($"[WU]   • {info.Title} | {info.KBArticle} | {info.Severity} | {info.SizeDisplay}");
                    }
                }

                // ── PÓS-PROCESSAMENTO: filtrar definições do Defender já atualizadas via MpSignature ──
                try
                {
                    var defenderDefs = result.PendingUpdates
                        .Where(u => u.Title != null &&
                            (u.Title.Contains("Defender", StringComparison.OrdinalIgnoreCase) ||
                             u.Title.Contains("Definition", StringComparison.OrdinalIgnoreCase) ||
                             u.Title.Contains("Antivirus", StringComparison.OrdinalIgnoreCase) ||
                             u.Title.Contains("Security Intelligence", StringComparison.OrdinalIgnoreCase) ||
                             u.Title.Contains("KB2267602", StringComparison.OrdinalIgnoreCase)))
                        .ToList();

                    if (defenderDefs.Count > 0)
                    {
                        _logger?.LogInfo($"[WU] Verificando {defenderDefs.Count} definição(ões) do Defender via Get-MpComputerStatus...");
                        try
                        {
                            using var mpSearcher = new System.Management.ManagementObjectSearcher(
                                @"root\Microsoft\Windows\Defender",
                                "SELECT AntivirusSignatureAge FROM MSFT_MpComputerStatus");
                            foreach (System.Management.ManagementObject mpObj in mpSearcher.Get())
                            {
                                var sigAge = mpObj["AntivirusSignatureAge"];
                                if (sigAge != null && Convert.ToInt32(sigAge) <= 1)
                                {
                                    _logger?.LogInfo($"[WU] Definições do Defender já estão atualizadas (SignatureAge={sigAge}d) — removendo {defenderDefs.Count} item(ns) da lista de pendentes");
                                    foreach (var def in defenderDefs)
                                        result.PendingUpdates.Remove(def);
                                }
                                break;
                            }
                        }
                        catch (Exception mpEx)
                        {
                            _logger?.LogWarning($"[WU] Não foi possível verificar MpComputerStatus: {mpEx.Message}");
                        }
                    }
                }
                catch (Exception postEx)
                {
                    _logger?.LogWarning($"[WU] Erro no pós-processamento: {postEx.Message}");
                }

                _logger?.LogSuccess($"[WU] ✅ {result.PendingUpdates.Count} atualização(ões) encontrada(s)");
                result.Success = true;
            }
            catch (OperationCanceledException)
            {
                _logger?.LogWarning("[WU] ⚠ Verificação cancelada pelo usuário");
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[WU] ❌ Erro inesperado: {ex.GetType().Name}: {ex.Message}", ex);
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        // ─────────────────────────────────────────────────────────────────────
        // INSTALL
        // ─────────────────────────────────────────────────────────────────────
        public async Task<(bool success, bool rebootRequired, string message)> InstallUpdatesAsync(
            IProgress<(int percent, string message)>? progress = null,
            CancellationToken ct = default)
        {
            _logger?.LogInfo("[WU] ▶ Iniciando instalação de atualizações...");

            try
            {
                progress?.Report((5, _loc["WUInstalling"]));

                string? output   = null;
                string? error    = null;
                int     exitCode = -1;

                var sw = Stopwatch.StartNew();

                await Task.Run(async () =>
                {
                    var scriptPath = WriteTempScript(InstallScript);
                    _logger?.LogInfo($"[WU] Script de instalação salvo em: {scriptPath}");

                    var psi = new ProcessStartInfo
                    {
                        FileName               = GetPsPath(),
                        Arguments              = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                        UseShellExecute        = false,
                        CreateNoWindow         = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError  = true,
                        StandardOutputEncoding = System.Text.Encoding.UTF8,
                        StandardErrorEncoding  = System.Text.Encoding.UTF8
                    };

                    using var proc = new Process { StartInfo = psi };

                    try
                    {
                        proc.Start();
                        var procId = proc.Id;
                        _logger?.LogInfo($"[WU] Processo PS iniciado (PID: {procId})");
                    }
                    catch (Exception startEx)
                    {
                        _logger?.LogError($"[WU] ❌ Falha ao iniciar processo PS: {startEx.Message}", startEx);
                        throw;
                    }

                    var outTask = proc.StandardOutput.ReadToEndAsync();
                    var errTask = proc.StandardError.ReadToEndAsync();

                    using var progressCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

                    _ = Task.Run(async () =>
                    {
                        int p = 10;
                        try
                        {
                            while (!progressCts.Token.IsCancellationRequested)
                            {
                                await Task.Delay(3000, progressCts.Token);
                                if (progressCts.Token.IsCancellationRequested) break;
                                p = Math.Min(p + 5, 85);
                                progress?.Report((p, _loc["WUInstalling"]));
                                _logger?.LogInfo($"[WU] Instalação em andamento... (~{p}%)");
                            }
                        }
                        catch { /* Ignora TaskCanceledException ou exceptions lançadas por disposal */ }
                    }, progressCts.Token);

                    await proc.WaitForExitAsync(ct);
                    progressCts.Cancel();
                    exitCode = proc.ExitCode;
                    output   = await outTask;
                    error    = await errTask;

                    try { File.Delete(scriptPath); } catch { }
                }, ct);

                sw.Stop();
                _logger?.LogInfo($"[WU] Instalação finalizada em {sw.ElapsedMilliseconds}ms | ExitCode: {exitCode}");
                _logger?.LogInfo($"[WU] stdout: {output?.Trim() ?? "(vazio)"}");
                if (!string.IsNullOrWhiteSpace(error))
                    _logger?.LogWarning($"[WU] stderr: {error.Trim()}");

                progress?.Report((95, _loc["WUFinalizing"]));

                if (output?.Contains("NENHUMA_ATUALIZACAO") == true)
                {
                    _logger?.LogInfo("[WU] ✅ Sistema já atualizado");
                    return (true, false, _loc["WUAlreadyUpdated"]);
                }

                // Verificar se WU está bloqueado por política
                if (output?.Contains("WU_BLOCKER:") == true || error?.Contains("bloqueada por politica") == true)
                {
                    _logger?.LogWarning($"[WU] ⛔ Instalação bloqueada por política do Windows Update");
                    return (false, false, _loc["WUBlockedByPolicy"]);
                }

                // Verificar se há updates marcados como INSTALL_BLOCKED (WU_E_INSTALL_NOT_ALLOWED)
                if (output?.Contains("INSTALL_BLOCKED") == true)
                {
                    _logger?.LogWarning($"[WU] ⛔ Update(s) bloqueados com WU_E_INSTALL_NOT_ALLOWED");
                    _logger?.LogInfo($"[WU] Tentando fallback para Update-MpSignature...");
                }

                if (output?.Contains("CONCLUIDO") == true)
                {
                    bool reboot = output.Contains("REBOOT_REQUIRED");

                    string parcialMsg = string.Empty;
                    foreach (var line in (output ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (line.StartsWith("PARCIAL:"))
                        {
                            parcialMsg = line["PARCIAL:".Length..].Trim();
                            break;
                        }
                    }

                    string msg;
                    if (!string.IsNullOrEmpty(parcialMsg))
                    {
                        msg = reboot
                            ? string.Format(_loc["WUPartialReboot"], parcialMsg)
                            : string.Format(_loc["WUPartialSuccess"], parcialMsg);
                        _logger?.LogWarning($"[WU] ⚠️ {msg}");
                    }
                    else
                    {
                        msg = reboot
                            ? _loc["WUInstalledReboot"]
                            : _loc["WUInstalledSuccess"];
                        _logger?.LogSuccess($"[WU] ✅ {msg}");
                    }
                    return (true, reboot, msg);
                }

                var failMsg = string.Format(_loc["WUInstallError"], error?.Trim() ?? "erro desconhecido", exitCode);

                // Se o erro for WU_E_INSTALL_NOT_ALLOWED, mostrar mensagem específica
                if (error?.Contains("WU_E_INSTALL_NOT_ALLOWED") == true || output?.Contains("INSTALL_BLOCKED") == true)
                {
                    failMsg = _loc["WUInstallNotAllowed"];
                    _logger?.LogWarning($"[WU] ⛔ {failMsg}");
                    return (false, false, failMsg);
                }

                _logger?.LogError($"[WU] ❌ {failMsg}");
                return (false, false, failMsg);
            }
            catch (OperationCanceledException)
            {
                _logger?.LogWarning("[WU] ⚠ Instalação cancelada");
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[WU] ❌ Erro inesperado na instalação: {ex.GetType().Name}: {ex.Message}", ex);
                return (false, false, string.Format(_loc["WUUnknownError"], ex.Message));
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // HELPERS
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Salva o script em arquivo .ps1 temporário e executa com -File.
        /// Evita todos os problemas de escape de argumentos inline.
        /// </summary>
        private async Task<string> RunPsScriptAsync(string script, CancellationToken ct)
        {
            var scriptPath = WriteTempScript(script);
            _logger?.LogInfo($"[WU] Script salvo em: {scriptPath}");

            var psi = new ProcessStartInfo
            {
                FileName               = GetPsPath(),
                Arguments              = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding  = System.Text.Encoding.UTF8
            };

            _logger?.LogInfo($"[WU] Executando: {GetPsPath()} {psi.Arguments}");

            using var proc = new Process { StartInfo = psi };

            try
            {
                proc.Start();
                var procId = proc.Id;
                _logger?.LogInfo($"[WU] Processo PS iniciado (PID: {procId})");
            }
            catch (Exception startEx)
            {
                _logger?.LogError($"[WU] ❌ Não foi possível iniciar powershell.exe: {startEx.Message}", startEx);
                try { File.Delete(scriptPath); } catch { }
                throw;
            }

            var outTask = proc.StandardOutput.ReadToEndAsync();
            var errTask = proc.StandardError.ReadToEndAsync();

            // Timeout de 300s para a verificação (busca WU pode ser lenta)
            await Task.WhenAny(proc.WaitForExitAsync(ct), Task.Delay(TimeSpan.FromSeconds(300), ct));
            if (!proc.HasExited)
            {
                try { proc.Kill(true); } catch { }
                try { File.Delete(scriptPath); } catch { }
                throw new TimeoutException("Verificação de atualizações excedeu 300 segundos.");
            }

            var stdout = await outTask;
            var stderr = await errTask;

            _logger?.LogInfo($"[WU] PS ExitCode: {proc.ExitCode}");
            if (!string.IsNullOrWhiteSpace(stderr))
                _logger?.LogWarning($"[WU] PS stderr: {stderr.Trim()}");

            try { File.Delete(scriptPath); } catch { }

            if (proc.ExitCode != 0)
            {
                // Extrair mensagem limpa prefixada com VOLTRIS_ERR
                var errText   = stderr?.Trim() ?? string.Empty;
                var cleanMsg  = string.Empty;
                foreach (var line in errText.Split('\n'))
                {
                    if (line.Contains("VOLTRIS_ERR:"))
                    {
                        var idx = line.IndexOf("VOLTRIS_ERR:", StringComparison.Ordinal);
                        cleanMsg = line[(idx + 12)..].Trim();
                        break;
                    }
                }
                var finalMsg = !string.IsNullOrWhiteSpace(cleanMsg) ? cleanMsg
                             : !string.IsNullOrWhiteSpace(errText)   ? errText
                             : $"Script encerrado com código {proc.ExitCode}";

                _logger?.LogWarning($"[WU] ⚠️ Script PS falhou (ExitCode {proc.ExitCode}): {finalMsg}");
                throw new Exception(finalMsg);
            }

            // ExitCode == 0 mas há stderr? Apenas aviso, não erro fatal
            if (!string.IsNullOrWhiteSpace(stderr))
                _logger?.LogWarning($"[WU] ℹ️ PS stderr (não-fatal, ExitCode=0): {stderr.Trim()}");

            return stdout.Trim();
        }

        /// <summary>
        /// Escreve o script em um arquivo .ps1 temporário e retorna o caminho.
        /// Grava sem BOM — PowerShell 5.1 pode interpretar BOM como caractere literal.
        /// </summary>
        private static string WriteTempScript(string scriptContent)
        {
            var path = Path.Combine(Path.GetTempPath(), $"voltris_wu_{Guid.NewGuid():N}.ps1");
            // UTF-8 SEM BOM para compatibilidade com PowerShell 5.1
            var utf8NoBom = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            File.WriteAllText(path, scriptContent, utf8NoBom);
            return path;
        }

        private static string GetString(JsonElement el, string key, string def = "")
        {
            try { return el.GetProperty(key).GetString() ?? def; } catch { return def; }
        }

        private static long GetLong(JsonElement el, string key)
        {
            try { return el.GetProperty(key).GetInt64(); } catch { return 0; }
        }
    }
}
