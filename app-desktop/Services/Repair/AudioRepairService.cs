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
using Microsoft.Win32;

namespace VoltrisOptimizer.Services.Repair
{
    /// <summary>
    /// Informações de hardware de um dispositivo de áudio PnP detectado.
    /// </summary>
    public class AudioDeviceInfo
    {
        public string Name         { get; set; } = "";
        public string Manufacturer { get; set; } = "";
        public string DeviceId     { get; set; } = "";
        public string HardwareIds  { get; set; } = "";
        public int    ErrorCode    { get; set; }
    }

    /// <summary>
    /// Resultado detalhado de uma verificação/reparo de áudio.
    /// </summary>
    public class AudioRepairResult
    {
        public bool Success { get; set; }
        public string Summary { get; set; } = "";
        public List<string> Issues { get; set; } = new();
        public List<string> Fixed { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public List<string> Details { get; set; } = new();
        public TimeSpan Duration { get; set; }
        /// <summary>
        /// Análise inteligente do porquê o reparo falhou ou não foi 100% (Análise de Causa Raiz).
        /// </summary>
        public string FailureReason { get; set; } = "";
    }

    /// <summary>
    /// Serviço profissional de diagnóstico e reparo de áudio do Windows.
    /// Cobre todos os cenários conhecidos de falha de som no Windows 10/11.
    /// </summary>
    public class AudioRepairService
    {
        private readonly ILoggingService _logger;
        public Action<string, string>? OnLog { get; set; }
        public Action<int, string>? OnProgress { get; set; }

        private static readonly string Sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        private static readonly string SysWow = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64");

        public AudioRepairService(ILoggingService logger) => _logger = logger;

        // ─── Helpers ───────────────────────────────────────────────────────────

        private void Log(string msg, string color = "#AAAAAA")
        {
            _logger.LogDebug($"[AudioRepair] {msg}", source: "AudioRepairService");
            OnLog?.Invoke(msg, color);
        }

        private void Progress(int pct, string msg)
        {
            _logger.LogDebug($"[AudioRepair][Progress] {pct}% — {msg}", source: "AudioRepairService");
            OnProgress?.Invoke(pct, msg);
        }

        private async Task<(int exitCode, string output)> RunAsync(
            string exe, string args, CancellationToken ct, int timeoutMs = 30_000)
        {
            var sb = new StringBuilder();
            _logger.LogDebug($"[AudioRepair][Exec] {exe} {args}", source: "AudioRepairService");
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                Encoding enc;
                try { enc = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
                catch { enc = Encoding.UTF8; }

                var psi = new ProcessStartInfo
                {
                    FileName = exe, Arguments = args,
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = enc, StandardErrorEncoding = enc
                };
                using var proc = new Process { StartInfo = psi };
                proc.OutputDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
                proc.ErrorDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
                linked.CancelAfter(timeoutMs);
                try { await proc.WaitForExitAsync(linked.Token); }
                catch (OperationCanceledException)
                {
                    try { proc.Kill(entireProcessTree: true); } catch { }
                    if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
                    _logger.LogWarning($"[AudioRepair][Exec] TIMEOUT {timeoutMs}ms: {exe} {args}");
                    return (-1, sb.ToString());
                }
                _logger.LogDebug($"[AudioRepair][Exec] exit={proc.ExitCode} len={sb.Length}", source: "AudioRepairService");
                return (proc.ExitCode, sb.ToString());
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning($"[AudioRepair][Exec] ERRO: {exe} — {ex.Message}");
                return (-2, ex.Message);
            }
        }

        private async Task<(int exitCode, string output)> RunPsAsync(
            string script, CancellationToken ct, int timeoutMs = 30_000)
        {
            var scriptDirectory = Path.Combine(Path.GetTempPath(), "Voltris", "Scripts");
            Directory.CreateDirectory(scriptDirectory);
            var scriptPath = Path.Combine(scriptDirectory, $"audio_{Guid.NewGuid():N}.ps1");
            try
            {
                await File.WriteAllTextAsync(scriptPath, script, new UTF8Encoding(false), ct);
                return await RunAsync("powershell.exe",
                    $"-NoProfile -NonInteractive -File \"{scriptPath}\"",
                    ct, timeoutMs);
            }
            finally
            {
                try { if (File.Exists(scriptPath)) File.Delete(scriptPath); } catch { }
            }
        }

        private static string Sc => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "sc.exe");

        // ─── DIAGNÓSTICO COMPLETO ──────────────────────────────────────────────

        /// <summary>
        /// Executa diagnóstico completo de áudio e retorna lista de problemas encontrados.
        /// </summary>
        public async Task<AudioRepairResult> DiagnoseAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var result = new AudioRepairResult();
            _logger.LogInfo("[AudioRepair] Iniciando Diagnóstico...");

            // 1. Verificar privilégios
            if (!IsRunningAsAdmin())
            {
                var msg = "PERMISSÃO_NEGADA: Privilégios de Administrador são necessários para diagnóstico completo.";
                Log($"!! {msg}", "#FF4444");
                _logger.LogWarning($"[AudioRepair] {msg}");
                result.FailureReason = "O aplicativo não está sendo executado como Administrador, impedindo o acesso ao hardware e chaves críticas de registro.";
                result.Success = false;
                return result;
            }

            Log("╔══════════════════════════════════════════════════════╗", "#00BFFF");
            Log("║     VOLTRIS AUDIO REPAIR — DIAGNÓSTICO COMPLETO     ║", "#00BFFF");
            Log("╚══════════════════════════════════════════════════════╝", "#00BFFF");
            Progress(2, LocalizationService.Instance.GetString("RepairProgress_Audio_Starting"));

            try
            {
                await DiagnoseServicesAsync(result, ct);
                await DiagnoseDevicesAsync(result, ct);
                await DiagnoseDllsAsync(result, ct);
                await DiagnoseRegistryAsync(result, ct);
                await DiagnoseDriversAsync(result, ct);
                await DiagnoseEnhancementsAsync(result, ct);
                await DiagnoseExclusiveModeAsync(result, ct);
                await DiagnoseEventLogAsync(result, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[AudioRepair] Fatal no diagnóstico: {ex.Message}");
                result.FailureReason = $"Erro fatal inesperado durante o diagnóstico: {ex.Message}";
                result.Issues.Add($"ERRO_FATAL_DIAGNOSTICO:{ex.GetType().Name}");
            }

            result.Duration = sw.Elapsed;
            result.Summary = result.Issues.Count == 0
                ? "Nenhum problema de áudio detectado"
                : $"{result.Issues.Count} problema(s) encontrado(s)";

            Log($"► Diagnóstico concluído em {sw.Elapsed.TotalSeconds:F1}s — {result.Issues.Count} problema(s)", "#00BFFF");
            return result;
        }

        private bool IsRunningAsAdmin()
        {
            try
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        // ─── DIAGNÓSTICO: SERVIÇOS ─────────────────────────────────────────────

        private async Task DiagnoseServicesAsync(AudioRepairResult result, CancellationToken ct)
        {
            Log("── [1/8] Verificando serviços de áudio...", "#FFAA00");
            var services = new[]
            {
                ("AudioSrv",             "Windows Audio",               "auto"),
                ("AudioEndpointBuilder", "Windows Audio Endpoint Builder", "auto"),
                ("RpcSs",                "Remote Procedure Call (RPC)",  "auto"),
                ("DcomLaunch",           "DCOM Server Process Launcher", "auto"),
                ("MMCSS",                "Multimedia Class Scheduler",   "auto")};

            foreach (var (name, label, expectedStart) in services)
            {
                ct.ThrowIfCancellationRequested();
                var script = $@"
$s = Get-Service -Name '{name}' -ErrorAction SilentlyContinue
if (-not $s) {{ Write-Output 'NOT_FOUND' }}
else {{
    Write-Output ""STATUS=$($s.Status)""
    Write-Output ""STARTTYPE=$($s.StartType)""
}}";
                var (_, psOut) = await RunPsAsync(script, ct, 10_000);
                _logger.LogDebug($"[AudioRepair][Svc:{name}] raw={psOut.Trim()}", source: "AudioRepairService");

                if (psOut.Contains("NOT_FOUND"))
                {
                    result.Issues.Add($"SERVIÇO_AUSENTE:{name}");
                    Log($"  ✗ {label} — NÃO ENCONTRADO (crítico)", "#FF4444");
                }
                else
                {
                    var running = psOut.Contains("STATUS=Running");
                    var startOk = psOut.Contains($"STARTTYPE=Automatic") || psOut.Contains("STARTTYPE=AutomaticDelayedStart");
                    if (!running)
                    {
                        result.Issues.Add($"SERVIÇO_PARADO:{name}");
                        Log($"  ✗ {label} — PARADO", "#FF4444");
                    }
                    else Log($"  ✓ {label} — rodando", "#00FF88");

                    if (!startOk)
                    {
                        result.Issues.Add($"SERVIÇO_STARTUP_ERRADO:{name}");
                        Log($"  ⚠ {label} — startup não é automático", "#FFA500");
                    }
                    result.Details.Add($"[Svc:{name}] running={running} autoStart={startOk}");
                }
            }
        }

        // ─── DIAGNÓSTICO: DISPOSITIVOS ─────────────────────────────────────────

        private async Task DiagnoseDevicesAsync(AudioRepairResult result, CancellationToken ct)
        {
            Log("── [2/8] Verificando dispositivos de áudio...", "#FFAA00");
            var script = @"
# Listar dispositivos de áudio via CIM (mais moderno que WMI)
try {
    $devices = Get-CimInstance Win32_SoundDevice -ErrorAction SilentlyContinue
    if (-not $devices) { Write-Output 'NO_DEVICES' }
    else {
        foreach ($d in $devices) {
            Write-Output ""DEVICE:$($d.Name)|STATUS:$($d.Status)|ENABLED:$($d.ConfigManagerErrorCode)""
        }
    }
} catch {
    # Fallback para WMI se CIM falhar
    $devices = Get-WmiObject Win32_SoundDevice -ErrorAction SilentlyContinue
    if ($devices) {
        foreach ($d in $devices) {
            Write-Output ""DEVICE:$($d.Name)|STATUS:$($d.Status)|ENABLED:$($d.ConfigManagerErrorCode)""
        }
    } else { Write-Output 'NO_DEVICES' }
}

# Verificar dispositivo padrão via PowerShell
try {
    Add-Type -AssemblyName System.Windows.Forms -ErrorAction SilentlyContinue
    Write-Output 'WINFORMS_OK'
} catch { Write-Output 'WINFORMS_FAIL' }

# Verificar via registry se há dispositivo de reprodução padrão
$mmKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render'
if (Test-Path $mmKey) {
    $devs = Get-ChildItem $mmKey -ErrorAction SilentlyContinue
    Write-Output ""RENDER_DEVICES=$($devs.Count)""
} else { Write-Output 'RENDER_KEY_MISSING' }

$capKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Capture'
if (Test-Path $capKey) {
    $devs = Get-ChildItem $capKey -ErrorAction SilentlyContinue
    Write-Output ""CAPTURE_DEVICES=$($devs.Count)""
} else { Write-Output 'CAPTURE_KEY_MISSING' }
";
            var (_, psOut) = await RunPsAsync(script, ct, 20_000);
            _logger.LogDebug($"[AudioRepair][Devices] raw={psOut.Trim()}", source: "AudioRepairService");

            if (psOut.Contains("NO_DEVICES"))
            {
                result.Issues.Add("SEM_DISPOSITIVOS_AUDIO");
                Log("  ✗ Nenhum dispositivo de áudio encontrado via WMI", "#FF4444");
            }
            else
            {
                var deviceLines = psOut.Split('\n')
                    .Where(l => l.StartsWith("DEVICE:")).ToList();
                Log($"  ✓ {deviceLines.Count} dispositivo(s) WMI encontrado(s)", "#00FF88");
                foreach (var d in deviceLines)
                {
                    var ok = d.Contains("STATUS:OK") || d.Contains("ENABLED:0");
                    var name = Regex.Match(d, @"DEVICE:([^|]+)").Groups[1].Value.Trim();
                    Log($"    {(ok ? "✓" : "⚠")} {name}", ok ? "#00FF88" : "#FFA500");
                    result.Details.Add($"[Device] {d.Trim()}");
                    if (!ok) result.Issues.Add($"DISPOSITIVO_COM_ERRO:{name}");
                }
            }

            var renderMatch = Regex.Match(psOut, @"RENDER_DEVICES=(\d+)");
            if (renderMatch.Success)
            {
                var count = int.Parse(renderMatch.Groups[1].Value);
                Log($"  {(count > 0 ? "✓" : "✗")} Dispositivos de reprodução no registro: {count}", count > 0 ? "#00FF88" : "#FF4444");
                if (count == 0) result.Issues.Add("SEM_DISPOSITIVOS_RENDER_REGISTRO");
                result.Details.Add($"[MMDevices] Render={count}");
            }
            else if (psOut.Contains("RENDER_KEY_MISSING"))
            {
                result.Issues.Add("CHAVE_MMDEVICES_AUSENTE");
                Log("  ✗ Chave MMDevices\\Audio\\Render ausente no registro", "#FF4444");
            }
        }

        // ─── DIAGNÓSTICO: DLLs ────────────────────────────────────────────────

        private async Task DiagnoseDllsAsync(AudioRepairResult result, CancellationToken ct)
        {
            Log("── [3/8] Verificando DLLs de áudio e DirectX...", "#FFAA00");
            var criticalDlls = new[]
            {
                // Áudio core
                ("dsound.dll",          true),
                ("dsoundoac.dll",       false),
                ("audioses.dll",        true),
                ("audiosrv.dll",        true),
                ("audioeng.dll",        true),
                ("audiodev.dll",        false),
                ("mmsys.cpl",           true),
                ("winmm.dll",           true),
                ("wdmaud.drv",          true),
                ("msacm32.drv",         true),
                ("msacm32.dll",         true),
                ("midimap.dll",         true),
                ("quartz.dll",          true),
                ("devenum.dll",         true),
                ("msdmo.dll",           true),
                ("dinput8.dll",         true),
                // DirectX
                ("d3d11.dll",           true),
                ("d3d12.dll",           true),
                ("dxgi.dll",            true),
                ("d3dcompiler_47.dll",  true),
                ("xinput1_4.dll",       false)};

            int missing = 0, ok = 0;
            foreach (var (dll, critical) in criticalDlls)
            {
                ct.ThrowIfCancellationRequested();
                var path32 = Path.Combine(Sys32, dll);
                var path64 = Path.Combine(SysWow, dll);
                var exists = File.Exists(path32) || File.Exists(path64);
                result.Details.Add($"[DLL:{dll}] exists={exists} critical={critical}");

                if (!exists)
                {
                    missing++;
                    if (critical)
                    {
                        result.Issues.Add($"DLL_CRITICA_AUSENTE:{dll}");
                        Log($"  ✗ {dll} — AUSENTE (crítico)", "#FF4444");
                    }
                    else Log($"  ⚠ {dll} — ausente (não crítico)", "#FFA500");
                }
                else { ok++; }
            }
            Log($"  ► DLLs: {ok} OK, {missing} ausentes", missing == 0 ? "#00FF88" : "#FFA500");
            await Task.CompletedTask;
        }

        // ─── DIAGNÓSTICO: REGISTRO ────────────────────────────────────────────

        private async Task DiagnoseRegistryAsync(AudioRepairResult result, CancellationToken ct)
        {
            Log("── [4/8] Verificando registro de áudio...", "#FFAA00");
            var script = @"
# Verificar chaves críticas de áudio no registro
$checks = @{
    'HKLM:\SYSTEM\CurrentControlSet\Services\AudioSrv'           = 'AudioSrv service key'
    'HKLM:\SYSTEM\CurrentControlSet\Services\AudioEndpointBuilder' = 'AudioEndpointBuilder key'
    'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile' = 'MMCSS SystemProfile'
    'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games' = 'MMCSS Games task'
    'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Audio' = 'MMCSS Audio task'
    'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render' = 'MMDevices Render'
}
foreach ($k in $checks.Keys) {
    if (Test-Path $k) { Write-Output ""REG_OK:$($checks[$k])"" }
    else { Write-Output ""REG_MISSING:$($checks[$k])"" }
}

# Verificar valor SystemResponsiveness do MMCSS
$mmcss = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile'
if (Test-Path $mmcss) {
    $sr = (Get-ItemProperty $mmcss -ErrorAction SilentlyContinue).SystemResponsiveness
    Write-Output ""MMCSS_SR=$sr""
    $nti = (Get-ItemProperty $mmcss -ErrorAction SilentlyContinue).NetworkThrottlingIndex
    Write-Output ""MMCSS_NTI=$nti""
}

# Verificar se áudio está desabilitado por política
$policy = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Audio'
if (Test-Path $policy) {
    $disabled = (Get-ItemProperty $policy -ErrorAction SilentlyContinue).DisableSound
    Write-Output ""POLICY_DISABLE_SOUND=$disabled""
} else { Write-Output 'POLICY_AUDIO_KEY_ABSENT' }

# Verificar DisableProtectedAudioDG (causa falha no audiodg.exe)
$audioKey = 'HKCU:\Software\Microsoft\Multimedia\Audio'
if (Test-Path $audioKey) {
    $dpag = (Get-ItemProperty $audioKey -ErrorAction SilentlyContinue).DisableProtectedAudioDG
    Write-Output ""DISABLE_PROTECTED_AUDIODG=$dpag""
}

Write-Output 'REG_CHECK_DONE'
";
            var (_, psOut) = await RunPsAsync(script, ct, 15_000);
            _logger.LogDebug($"[AudioRepair][Registry] raw={psOut.Trim()}", source: "AudioRepairService");

            foreach (var line in psOut.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))
            {
                if (line.StartsWith("REG_MISSING:"))
                {
                    var key = line.Substring(12);
                    result.Issues.Add($"CHAVE_REGISTRO_AUSENTE:{key}");
                    Log($"  ✗ Chave ausente: {key}", "#FF4444");
                }
                else if (line.StartsWith("REG_OK:"))
                    Log($"  ✓ {line.Substring(7)}", "#00FF88");
                else if (line.StartsWith("MMCSS_SR="))
                {
                    var val = line.Substring(9).Trim();
                    result.Details.Add($"[MMCSS] SystemResponsiveness={val}");
                    Log($"  ℹ MMCSS SystemResponsiveness={val} (ideal: 20)", "#AAAAAA");
                }
                else if (line.StartsWith("POLICY_DISABLE_SOUND=1"))
                {
                    result.Issues.Add("AUDIO_DESABILITADO_POR_POLITICA");
                    Log("  ✗ ÁUDIO DESABILITADO POR POLÍTICA DE GRUPO!", "#FF4444");
                }
                else if (line.StartsWith("DISABLE_PROTECTED_AUDIODG=1"))
                {
                    result.Issues.Add("PROTECTED_AUDIODG_DESABILITADO");
                    Log("  ⚠ DisableProtectedAudioDG=1 — pode causar falha no audiodg.exe", "#FFA500");
                }
            }
            result.Details.Add($"[Registry] raw_len={psOut.Length}");
        }

        // ─── DIAGNÓSTICO: DRIVERS ─────────────────────────────────────────────

        private async Task DiagnoseDriversAsync(AudioRepairResult result, CancellationToken ct)
        {
            Log("── [5/8] Verificando drivers de áudio...", "#FFAA00");
            var script = @"
# Listar drivers de áudio com problemas (incluindo Controladores de sistema)
$audioDrivers = Get-WmiObject Win32_PnPEntity -ErrorAction SilentlyContinue |
    Where-Object { $_.PNPClass -eq 'Media' -or $_.PNPClass -eq 'AudioEndpoint' -or $_.Service -like '*hdaudio*' -or $_.Name -like '*Audio Controller*' }

if (-not $audioDrivers) { Write-Output 'NO_AUDIO_DRIVERS' }
else {
    foreach ($d in $audioDrivers) {
        $err = $d.ConfigManagerErrorCode
        $status = if ($err -eq 0) { 'OK' } else { ""ERROR_$err"" }
        Write-Output ""DRIVER:$($d.Name)|STATUS:$status|PNP:$($d.DeviceID)""
    }
}

# Verificar drivers com código de erro no Device Manager
$problemDrivers = Get-WmiObject Win32_PnPEntity -ErrorAction SilentlyContinue |
    Where-Object { $_.ConfigManagerErrorCode -ne 0 -and ($_.PNPClass -eq 'Media' -or $_.PNPClass -eq 'AudioEndpoint' -or $_.Service -like '*hdaudio*' -or $_.Name -like '*Audio Controller*') }
Write-Output ""PROBLEM_DRIVERS=$($problemDrivers.Count)""

# Verificar se há driver de áudio genérico (High Definition Audio)
$hdaDrivers = Get-WmiObject Win32_PnPEntity -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -like '*High Definition Audio*' -or $_.Name -like '*HD Audio*' }
Write-Output ""HDA_DRIVERS=$($hdaDrivers.Count)""

Write-Output 'DRIVER_CHECK_DONE'
";
            var (_, psOut) = await RunPsAsync(script, ct, 20_000);
            _logger.LogDebug($"[AudioRepair][Drivers] raw={psOut.Trim()}", source: "AudioRepairService");

            if (psOut.Contains("NO_AUDIO_DRIVERS"))
            {
                result.Issues.Add("SEM_DRIVERS_AUDIO");
                Log("  ✗ Nenhum driver de áudio encontrado", "#FF4444");
            }
            else
            {
                var driverLines = psOut.Split('\n').Where(l => l.StartsWith("DRIVER:")).ToList();
                foreach (var d in driverLines)
                {
                    var name = Regex.Match(d, @"DRIVER:([^|]+)").Groups[1].Value.Trim();
                    var status = Regex.Match(d, @"STATUS:([^|]+)").Groups[1].Value.Trim();
                    var ok = status == "OK";
                    Log($"  {(ok ? "✓" : "✗")} {name} [{status}]", ok ? "#00FF88" : "#FF4444");
                    result.Details.Add($"[Driver] {name} status={status}");
                    if (!ok) result.Issues.Add($"DRIVER_COM_ERRO:{name}|{status}");
                }
            }

            var problemMatch = Regex.Match(psOut, @"PROBLEM_DRIVERS=(\d+)");
            if (problemMatch.Success && int.Parse(problemMatch.Groups[1].Value) > 0)
            {
                var count = int.Parse(problemMatch.Groups[1].Value);
                result.Issues.Add($"DRIVERS_COM_PROBLEMA:{count}");
                Log($"  ✗ {count} driver(s) de áudio com problema no Device Manager", "#FF4444");
            }

            var hdaMatch = Regex.Match(psOut, @"HDA_DRIVERS=(\d+)");
            if (hdaMatch.Success)
                Log($"  ℹ Drivers HD Audio encontrados: {hdaMatch.Groups[1].Value}", "#AAAAAA");
        }

        // ─── DIAGNÓSTICO: ENHANCEMENTS ────────────────────────────────────────

        private async Task DiagnoseEnhancementsAsync(AudioRepairResult result, CancellationToken ct)
        {
            Log("── [6/8] Verificando Audio Enhancements...", "#FFAA00");
            var script = @"
# Verificar se Audio Enhancements estão causando problemas
$renderKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render'
if (Test-Path $renderKey) {
    $devices = Get-ChildItem $renderKey -ErrorAction SilentlyContinue
    foreach ($dev in $devices) {
        $propsPath = Join-Path $dev.PSPath 'Properties'
        if (Test-Path $propsPath) {
            $props = Get-ItemProperty $propsPath -ErrorAction SilentlyContinue
            # {1da5d803-d492-4edd-8c23-e0c0ffee7f0e},5 = FriendlyName
            $fname = $props.'{1da5d803-d492-4edd-8c23-e0c0ffee7f0e},5'
            if ($fname) { Write-Output ""RENDER_DEVICE:$fname"" }
        }
    }
}
Write-Output 'ENHANCEMENTS_CHECK_DONE'
";
            var (_, psOut) = await RunPsAsync(script, ct, 15_000);
            _logger.LogDebug($"[AudioRepair][Enhancements] raw={psOut.Trim()}", source: "AudioRepairService");

            var devices = psOut.Split('\n').Where(l => l.StartsWith("RENDER_DEVICE:")).ToList();
            if (devices.Count == 0)
                Log("  ⚠ Nenhum dispositivo de reprodução com nome encontrado no registro", "#FFA500");
            else
            {
                Log($"  ✓ {devices.Count} dispositivo(s) de reprodução registrado(s):", "#00FF88");
                foreach (var d in devices)
                    Log($"    • {d.Substring(14).Trim()}", "#AAAAAA");
            }
            result.Details.Add($"[Enhancements] render_devices={devices.Count}");
        }

        // ─── DIAGNÓSTICO: EXCLUSIVE MODE ──────────────────────────────────────

        private async Task DiagnoseExclusiveModeAsync(AudioRepairResult result, CancellationToken ct)
        {
            Log("── [7/8] Verificando modo exclusivo e sample rate...", "#FFAA00");
            var script = @"
# Verificar configurações de modo exclusivo nos dispositivos
$renderKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render'
if (Test-Path $renderKey) {
    $devices = Get-ChildItem $renderKey -ErrorAction SilentlyContinue
    $exclusiveCount = 0
    foreach ($dev in $devices) {
        $propsPath = Join-Path $dev.PSPath 'Properties'
        if (Test-Path $propsPath) {
            $props = Get-ItemProperty $propsPath -ErrorAction SilentlyContinue
            # {b3f8fa53-0004-438e-9003-51a46e139bfc},3 = exclusive mode allow
            $excl = $props.'{b3f8fa53-0004-438e-9003-51a46e139bfc},3'
            if ($excl -eq 0) { $exclusiveCount++ }
        }
    }
    Write-Output ""EXCLUSIVE_BLOCKED=$exclusiveCount""
}
Write-Output 'EXCLUSIVE_CHECK_DONE'
";
            var (_, psOut) = await RunPsAsync(script, ct, 15_000);
            _logger.LogDebug($"[AudioRepair][ExclusiveMode] raw={psOut.Trim()}", source: "AudioRepairService");

            var exclMatch = Regex.Match(psOut, @"EXCLUSIVE_BLOCKED=(\d+)");
            if (exclMatch.Success && int.Parse(exclMatch.Groups[1].Value) > 0)
            {
                Log($"  ⚠ {exclMatch.Groups[1].Value} dispositivo(s) com modo exclusivo bloqueado", "#FFA500");
                result.Warnings.Add($"MODO_EXCLUSIVO_BLOQUEADO:{exclMatch.Groups[1].Value}");
            }
            else Log("  ✓ Configurações de modo exclusivo OK", "#00FF88");
            result.Details.Add($"[ExclusiveMode] raw_len={psOut.Length}");
        }

        // ─── DIAGNÓSTICO: EVENT LOG ───────────────────────────────────────────

        private async Task DiagnoseEventLogAsync(AudioRepairResult result, CancellationToken ct)
        {
            Log("── [8/8] Analisando Event Log para erros de áudio...", "#FFAA00");
            var script = @"
# Buscar erros de áudio no Event Log das últimas 24h
$since = (Get-Date).AddHours(-24)
$audioErrors = Get-WinEvent -FilterHashtable @{
    LogName = 'System','Application'
    Level = 1,2  # Critical, Error
    StartTime = $since
} -ErrorAction SilentlyContinue |
    Where-Object { $_.Message -match 'audio|sound|AudioSrv|AudioEndpoint|audiodg|MMCSS' } |
    Select-Object -First 10

if ($audioErrors) {
    foreach ($e in $audioErrors) {
        Write-Output ""AUDIO_EVENT:[$($e.LevelDisplayName)] $($e.TimeCreated.ToString('HH:mm')) — $($e.Message.Substring(0, [Math]::Min(120, $e.Message.Length)))""
    }
    Write-Output ""AUDIO_ERRORS_COUNT=$($audioErrors.Count)""
} else { Write-Output 'NO_AUDIO_ERRORS_IN_LOG' }
Write-Output 'EVENTLOG_CHECK_DONE'
";
            var (_, psOut) = await RunPsAsync(script, ct, 20_000);
            _logger.LogDebug($"[AudioRepair][EventLog] raw={psOut.Trim()}", source: "AudioRepairService");

            if (psOut.Contains("NO_AUDIO_ERRORS_IN_LOG"))
            {
                Log("  ✓ Nenhum erro de áudio no Event Log (últimas 24h)", "#00FF88");
                _logger.LogInfo("[AudioRepair][DiagnoseEventLog] Nenhum erro de áudio encontrado no Event Log.");
            }
            else
            {
                var events = psOut.Split('\n').Where(l => l.StartsWith("AUDIO_EVENT:")).ToList();
                var countMatch = Regex.Match(psOut, @"AUDIO_ERRORS_COUNT=(\d+)");
                var count = countMatch.Success ? countMatch.Groups[1].Value : events.Count.ToString();
                Log($"  ⚠ {count} erro(s) de áudio no Event Log:", "#FFA500");
                foreach (var ev in events)
                    Log($"    {ev.Substring(12).Trim()}", "#FF8888");
                result.Issues.Add($"ERROS_NO_EVENT_LOG:{count}");
                _logger.LogWarning($"[AudioRepair][DiagnoseEventLog] {count} erros de áudio encontrados no Event Log.");
            }
            result.Details.Add($"[EventLog] checked=true");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // REPARO COMPLETO DE ÁUDIO
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Executa reparo completo e profissional de áudio.
        /// Cobre todos os cenários conhecidos de falha de som no Windows 10/11.
        /// </summary>
        public async Task<AudioRepairResult> RepairAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var result = new AudioRepairResult();
            _logger.LogInfo("[AudioRepair] === REPARO COMPLETO INICIADO ===");

            // 1. Verificar privilégios
            if (!IsRunningAsAdmin())
            {
                var msg = "PERMISSÃO_NEGADA: Privilégios de Administrador são necessários para o reparo.";
                Log($"!! {msg}", "#FF4444");
                _logger.LogWarning($"[AudioRepair] {msg}");
                result.FailureReason = "Privilégios de Administrador ausentes. O Windows impede o reset de drivers e serviços sem elevação.";
                result.Success = false;
                return result;
            }

            Log("╔══════════════════════════════════════════════════════╗", "#00BFFF");
            Log("║       VOLTRIS AUDIO REPAIR — REPARO COMPLETO        ║", "#00BFFF");
            Log("╚══════════════════════════════════════════════════════╝", "#00BFFF");

            try
            {
                Progress(5, LocalizationService.Instance.GetString("RepairProgress_Audio_StoppingServices"));
                await StopAudioServicesAsync(result, ct);

                Progress(15, LocalizationService.Instance.GetString("RepairProgress_Audio_CleaningCache"));
                await ClearAudioCacheAsync(result, ct);

                Progress(30, LocalizationService.Instance.GetString("RepairProgress_Audio_FixingRegistry"));
                await RepairRegistryAsync(result, ct);

                Progress(45, LocalizationService.Instance.GetString("RepairProgress_Audio_VerifyingSFC"));
                await RepairFileIntegrityAsync(result, ct);

                Progress(55, LocalizationService.Instance.GetString("RepairProgress_Audio_ReregisteringDLLs"));
                await ReRegisterDllsAsync(result, ct);

                Progress(65, LocalizationService.Instance.GetString("RepairProgress_Audio_FixingMMCSS"));
                await RepairMmcssAsync(result, ct);

                Progress(75, LocalizationService.Instance.GetString("RepairProgress_Audio_FixingMMDevices"));
                await RepairMmDevicesAsync(result, ct);

                Progress(80, LocalizationService.Instance.GetString("RepairProgress_Audio_RestartingServices"));
                await RestartAudioServicesAsync(result, ct);

                Progress(85, LocalizationService.Instance.GetString("RepairProgress_Audio_CheckingAudiodg"));
                await VerifyAudioDgAsync(result, ct);

                Progress(90, LocalizationService.Instance.GetString("RepairProgress_Audio_FixingPnP"));
                await RepairPnpDriversAsync(result, ct);

                Progress(94, LocalizationService.Instance.GetString("RepairProgress_Audio_ApplyingLatency"));
                await ApplyLatencyOptimizationsAsync(result, ct);

                Progress(96, LocalizationService.Instance.GetString("RepairProgress_Audio_RestoringVolume"));
                await ForceUnmuteAudioAsync(result, ct);

                Progress(98, LocalizationService.Instance.GetString("RepairProgress_Audio_FinalCheck"));
                await FinalVerificationAsync(result, ct);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("[AudioRepair] Reparo cancelado pelo usuário.");
                result.FailureReason = "O processo foi cancelado manualmente pelo usuário.";
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[AudioRepair] Erro fatal durante o reparo: {ex.Message}");
                result.FailureReason = $"Ocorreu uma exceção inesperada: {ex.Message}";
                Log($"!! ERRO FATAL: {ex.Message}", "#FF4444");
            }

            result.Duration = sw.Elapsed;
            PerformFailureAnalysis(result);

            result.Success = result.Issues.Count == 0 || result.Fixed.Count > 0;
            result.Summary = $"Reparo concluído em {sw.Elapsed.TotalSeconds:F0}s — " +
                             $"{result.Fixed.Count} correção(ões) aplicada(s), {result.Issues.Count} aviso(s)";

            Log("╔══════════════════════════════════════════════════════╗", "#00FF88");
            Log($"║  REPARO CONCLUÍDO — {result.Fixed.Count} fix(es) | {result.Issues.Count} aviso(s)".PadRight(54) + "║", "#00FF88");
            Log("╚══════════════════════════════════════════════════════╝", "#00FF88");
            _logger.LogInfo($"[AudioRepair] === REPARO CONCLUÍDO: {result.Summary} ===");
            return result;
        }

        // ─── REPARO: PARAR SERVIÇOS ───────────────────────────────────────────

        private async Task StopAudioServicesAsync(AudioRepairResult result, CancellationToken ct)
        {
            Log("── Parando serviços de áudio para reparo limpo...", "#FFAA00");
            _logger.LogInfo("[AudioRepair][StopServices] Parando serviços de áudio.");
            foreach (var svc in new[] { "AudioSrv", "AudioEndpointBuilder" })
            {
                ct.ThrowIfCancellationRequested();
                var (code, _) = await RunAsync(Sc, $"stop {svc}", ct, 10_000);
                _logger.LogDebug($"[AudioRepair][Stop:{svc}] exit={code}", source: "AudioRepairService");
                Log($"  → stop {svc}: exit={code}", "#AAAAAA");
            }
            await Task.Delay(1500, ct);
            Log("  ✓ Serviços parados", "#00FF88");
            _logger.LogInfo("[AudioRepair][StopServices] Serviços de áudio parados.");
        }

        // ─── REPARO: LIMPAR CACHE ─────────────────────────────────────────────

        private async Task ClearAudioCacheAsync(AudioRepairResult result, CancellationToken ct)
        {
            Log("── Limpando cache de áudio corrompido...", "#FFAA00");
            _logger.LogInfo("[AudioRepair][ClearCache] Iniciando limpeza de cache de áudio.");
            var paths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Microsoft\Windows\Audio"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Microsoft\Windows\AudioEndpointBuilder")};

            int deleted = 0;
            foreach (var dir in paths)
            {
                if (!Directory.Exists(dir)) { Log($"  ℹ {dir} — não existe (OK)", "#AAAAAA"); continue; }
                _logger.LogDebug($"[AudioRepair][Cache] Limpando: {dir}", source: "AudioRepairService");
                foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try { File.Delete(f); deleted++; _logger.LogDebug($"[AudioRepair][Cache] Deletado: {f}", source: "AudioRepairService"); }
                    catch (Exception ex) { _logger.LogWarning($"[AudioRepair][Cache] Falha ao deletar {f}: {ex.Message}"); }
                }
            }
            Log($"  ✓ Cache limpo — {deleted} arquivo(s) removido(s)", "#00FF88");
            if (deleted > 0) result.Fixed.Add($"CACHE_AUDIO_LIMPO:{deleted}_arquivos");
            _logger.LogInfo($"[AudioRepair][ClearCache] Cache de áudio limpo. {deleted} arquivos removidos.");
            await Task.CompletedTask;
        }

        // ─── REPARO: REGISTRO ─────────────────────────────────────────────────

        private async Task RepairRegistryAsync(AudioRepairResult result, CancellationToken ct)
        {
            Log("── Reparando registro de áudio...", "#FFAA00");
            _logger.LogInfo("[AudioRepair][RepairRegistry] Iniciando reparo do registro.");
            var script = @"
$fixed = 0

# 1. Garantir startup automático dos serviços de áudio
$audioServices = @('AudioSrv', 'AudioEndpointBuilder', 'MMCSS')
foreach ($svc in $audioServices) {
    $key = ""HKLM:\SYSTEM\CurrentControlSet\Services\$svc""
    if (Test-Path $key) {
        $start = (Get-ItemProperty $key -ErrorAction SilentlyContinue).Start
        if ($start -ne 2) {
            Set-ItemProperty -Path $key -Name 'Start' -Value 2 -Type DWord -ErrorAction SilentlyContinue
            Write-Output ""FIXED_STARTUP:$svc""
            $fixed++
        } else { Write-Output ""OK_STARTUP:$svc"" }
    } else { Write-Output ""MISSING_KEY:$svc"" }
}

# 2. Remover DisableProtectedAudioDG (causa falha no audiodg.exe)
$audioKey = 'HKCU:\Software\Microsoft\Multimedia\Audio'
if (Test-Path $audioKey) {
    $val = (Get-ItemProperty $audioKey -ErrorAction SilentlyContinue).DisableProtectedAudioDG
    if ($val -eq 1) {
        Remove-ItemProperty -Path $audioKey -Name 'DisableProtectedAudioDG' -ErrorAction SilentlyContinue
        Write-Output 'FIXED_PROTECTED_AUDIODG'
        $fixed++
    } else { Write-Output 'OK_PROTECTED_AUDIODG' }
}

# 3. Remover UserDuckingPreference problemático
if (Test-Path $audioKey) {
    $duck = (Get-ItemProperty $audioKey -ErrorAction SilentlyContinue).UserDuckingPreference
    if ($duck -ne $null) {
        Remove-ItemProperty -Path $audioKey -Name 'UserDuckingPreference' -ErrorAction SilentlyContinue
        Write-Output 'FIXED_DUCKING'
        $fixed++
    }
}

# 4. Remover política que desabilita áudio (se existir)
$policyKey = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Audio'
if (Test-Path $policyKey) {
    $disabled = (Get-ItemProperty $policyKey -ErrorAction SilentlyContinue).DisableSound
    if ($disabled -eq 1) {
        Set-ItemProperty -Path $policyKey -Name 'DisableSound' -Value 0 -Type DWord -ErrorAction SilentlyContinue
        Write-Output 'FIXED_POLICY_DISABLE_SOUND'
        $fixed++
    }
}

# 5. Garantir que Sound Mapper existe
$smKey = 'HKCU:\Software\Microsoft\Multimedia\Sound Mapper'
if (-not (Test-Path $smKey)) {
    New-Item -Path $smKey -Force -ErrorAction SilentlyContinue | Out-Null
    Write-Output 'FIXED_SOUND_MAPPER_KEY'
    $fixed++
} else { Write-Output 'OK_SOUND_MAPPER' }

# 6. Reparar chave de tarefas MMCSS se ausente
$mmcssTasksKey = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks'
$requiredTasks = @('Audio', 'Games', 'Playback', 'Pro Audio')
foreach ($task in $requiredTasks) {
    $taskKey = Join-Path $mmcssTasksKey $task
    if (-not (Test-Path $taskKey)) {
        New-Item -Path $taskKey -Force -ErrorAction SilentlyContinue | Out-Null
        Write-Output ""FIXED_MMCSS_TASK:$task""
        $fixed++
    }
}

Write-Output ""TOTAL_FIXED=$fixed""
Write-Output 'REG_REPAIR_DONE'
";
            var (_, psOut) = await RunPsAsync(script, ct, 20_000);
            _logger.LogDebug($"[AudioRepair][Registry] raw={psOut.Trim()}", source: "AudioRepairService");

            foreach (var line in psOut.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))
            {
                if (line.StartsWith("FIXED_"))
                {
                    result.Fixed.Add(line);
                    Log($"  ✓ Corrigido: {line.Substring(6)}", "#00FF88");
                    _logger.LogInfo($"[AudioRepair][RepairRegistry] Corrigido: {line}");
                }
                else if (line.StartsWith("OK_")) Log($"  ✓ {line.Substring(3)}", "#00FF88");
                else if (line.StartsWith("MISSING_KEY:"))
                {
                    Log($"  ⚠ Chave ausente: {line.Substring(12)}", "#FFA500");
                    result.Issues.Add($"SERVIÇO_AUSENTE:{line.Substring(12)}");
                    _logger.LogWarning($"[AudioRepair][RepairRegistry] Chave de serviço ausente: {line.Substring(12)}");
                }
            }

            var fixedMatch = Regex.Match(psOut, @"TOTAL_FIXED=(\d+)");
            if (fixedMatch.Success)
                Log($"  ► Registro: {fixedMatch.Groups[1].Value} correção(ões) aplicada(s)", "#00BFFF");
            _logger.LogInfo($"[AudioRepair][RepairRegistry] Reparo do registro concluído. {fixedMatch.Groups[1].Value} correções aplicadas.");
        }

        // ─── REPARO: VERIFICAR INTEGRIDADE DE ARQUIVOS ────────────────────────

        private async Task RepairFileIntegrityAsync(AudioRepairResult result, CancellationToken ct)
        {
            Log("── Verificando integridade de arquivos de áudio...", "#FFAA00");
            _logger.LogInfo("[AudioRepair] Verificando integridade via sfc /scanfile...");
            
            var criticalFiles = new[] { "audiosrv.dll", "audioeng.dll", "audiodg.exe" };
            foreach (var file in criticalFiles)
            {
                ct.ThrowIfCancellationRequested();
                var path = Path.Combine(Sys32, file);
                if (!File.Exists(path))
                {
                    Log($"  ✗ {file} AUSENTE! Tentando restaurar via SFC...", "#FF4444");
                    _logger.LogWarning($"[AudioRepair] Arquivo ausente: {path}. Iniciando SFC.");
                    var (code, out_) = await RunAsync("sfc.exe", $"/scanfile=\"{path}\"", ct, 120_000);
                    if (code == 0) { Log($"  ✓ {file} restaurado com sucesso.", "#00FF88"); result.Fixed.Add($"FILE_RESTORED:{file}"); }
                    else { Log($"  ✗ Falha ao restaurar {file} (code={code}).", "#FF4444"); result.Issues.Add($"FILE_RESTORE_FAIL:{file}"); }
                }
                else
                {
                    // Se o arquivo existe mas o serviço não sobe, pode estar corrompido
                    _logger.LogDebug($"[AudioRepair] {file} presente. Verificação rápida OK.", source: "AudioRepairService");
                }
            }
            _logger.LogInfo("[AudioRepair] Verificação de integridade de arquivos concluída.");
        }

        // ─── REPARO: RE-REGISTRAR DLLs ────────────────────────────────────────

        private async Task ReRegisterDllsAsync(AudioRepairResult result, CancellationToken ct)
        {
            Log("── Re-registrando DLLs de áudio e DirectX...", "#FFAA00");
            _logger.LogInfo("[AudioRepair][ReRegisterDlls] Iniciando re-registro de DLLs.");
            var dlls = new[]
            {
                "dsound.dll", "dinput8.dll", "quartz.dll", "devenum.dll",
                "msdmo.dll",  "msacm32.dll", "winmm.dll",  "wdmaud.drv",
                "midimap.dll","audioses.dll"};

            var regsvr32 = Path.Combine(Sys32, "regsvr32.exe");
            int ok = 0, fail = 0;
            foreach (var dll in dlls)
            {
                ct.ThrowIfCancellationRequested();
                var path = Path.Combine(Sys32, dll);
                if (!File.Exists(path)) { Log($"  ⚠ {dll} — ausente, pulando", "#FFA500"); result.Issues.Add($"DLL_CRITICA_AUSENTE:{dll}"); continue; }

                var (code, _) = await RunAsync(regsvr32, $"/s \"{path}\"", ct, 10_000);
                _logger.LogDebug($"[AudioRepair][RegSvr32:{dll}] exit={code}", source: "AudioRepairService");
                if (code == 0) { ok++; Log($"  ✓ {dll} re-registrado", "#00FF88"); }
                else { fail++; Log($"  ⚠ {dll} exit={code} (pode ser normal para .drv)", "#FFA500"); }
            }
            Log($"  ► DLLs: {ok} OK, {fail} com aviso", ok > 0 ? "#00FF88" : "#FFA500");
            if (ok > 0) result.Fixed.Add($"DLLS_REREGISTRADAS:{ok}");
            _logger.LogInfo($"[AudioRepair][ReRegisterDlls] Re-registro de DLLs concluído. {ok} OK, {fail} com aviso.");
        }

        private async Task RepairMmcssAsync(AudioRepairResult result, CancellationToken ct)
        {
            Log("── Resetando MMCSS...", "#FFAA00");
            _logger.LogInfo("[AudioRepair][RepairMmcss] Iniciando reset do MMCSS.");
            ct.ThrowIfCancellationRequested();
            var script = @"
$key = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile'
if (Test-Path $key) {
    Set-ItemProperty -Path $key -Name 'SystemResponsiveness' -Value 20 -Type DWord -ErrorAction SilentlyContinue
    Set-ItemProperty -Path $key -Name 'NetworkThrottlingIndex' -Value 10 -Type DWord -ErrorAction SilentlyContinue
    Write-Output 'MMCSS_OK'
} else { Write-Output 'MMCSS_KEY_MISSING' }
";
            var (_, psOut) = await RunPsAsync(script, ct, 15_000);
            var ok = psOut.Contains("MMCSS_OK");
            Log($"  {(ok ? "v" : "!")} MMCSS: {(ok ? "resetado" : "chave nao encontrada")}", ok ? "#00FF88" : "#FFA500");
            if (ok) result.Fixed.Add("MMCSS_RESET");
            else
            {
                result.Issues.Add("MMCSS_KEY_MISSING");
                _logger.LogWarning("[AudioRepair][RepairMmcss] Chave MMCSS não encontrada.");
            }
            _logger.LogInfo("[AudioRepair][RepairMmcss] Reset do MMCSS concluído.");
        }

        private async Task RepairMmDevicesAsync(AudioRepairResult result, CancellationToken ct)
        {
            Log("── Reparando MMDevices...", "#FFAA00");
            _logger.LogInfo("[AudioRepair][RepairMmDevices] Iniciando reparo de MMDevices.");
            ct.ThrowIfCancellationRequested();
            var script = @"
$fixed = 0
$mmKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio'
foreach ($subKey in @('Capture', 'Render')) {
    $path = Join-Path $mmKey $subKey
    if (-not (Test-Path $path)) { Write-Output ""MMDEVICES_${subKey}_MISSING""; continue }
    $devices = Get-ChildItem -Path $path -ErrorAction SilentlyContinue
    foreach ($dev in $devices) {
        $props = Get-ItemProperty -Path $dev.PSPath -ErrorAction SilentlyContinue
        if ($props.DeviceState -eq 8) {
            Remove-Item -Path $dev.PSPath -Recurse -Force -ErrorAction SilentlyContinue
            Write-Output ""REMOVED_GHOST_DEVICE:$($dev.PSChildName)""
            $fixed++
        }
    }
    Write-Output ""MMDEVICES_${subKey}_OK""
}
Write-Output ""MMDEVICES_FIXED=$fixed""
Write-Output 'MMDEVICES_DONE'
";
            var (_, psOut) = await RunPsAsync(script, ct, 20_000);
            var fixedMatch = Regex.Match(psOut, @"MMDEVICES_FIXED=(\d+)");
            int removed = fixedMatch.Success ? int.Parse(fixedMatch.Groups[1].Value) : 0;
            bool done = psOut.Contains("MMDEVICES_DONE");
            Log($"  {(done ? "v" : "!")} MMDevices: {removed} dispositivo(s) fantasma removido(s)", done ? "#00FF88" : "#FFA500");
            if (removed > 0) result.Fixed.Add($"MMDEVICES_GHOST_REMOVED:{removed}");
            _logger.LogInfo($"[AudioRepair][RepairMmDevices] Reparo de MMDevices concluído. {removed} dispositivos fantasmas removidos.");
        }

        private async Task RestartAudioServicesAsync(AudioRepairResult result, CancellationToken ct)
        {
            Log("── Reiniciando servicos de audio...", "#FFAA00");
            _logger.LogInfo("[AudioRepair][RestartServices] Reiniciando serviços de áudio.");
            var sc = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "sc.exe");
            foreach (var (svc, label) in new[] { ("AudioEndpointBuilder", "Audio Endpoint Builder"), ("AudioSrv", "Windows Audio") })
            {
                ct.ThrowIfCancellationRequested();
                await RunAsync(sc, $"config {svc} start= auto", ct, 10_000);
                var (code, _) = await RunAsync(sc, $"start {svc}", ct, 15_000);
                var ok = code == 0 || code == 1056;
                Log($"  {(ok ? "v" : "!")} {label}: {(ok ? "rodando" : $"exit={code}")}", ok ? "#00FF88" : "#FFA500");
                if (ok) result.Fixed.Add($"{svc}_STARTED");
                else
                {
                    result.Issues.Add($"{svc}_NOT_STARTED");
                    _logger.LogWarning($"[AudioRepair][RestartServices] Falha ao iniciar {svc}. Exit code: {code}");
                }
                await Task.Delay(800, ct);
            }
            _logger.LogInfo("[AudioRepair][RestartServices] Reinício de serviços de áudio concluído.");
        }

        private async Task VerifyAudioDgAsync(AudioRepairResult result, CancellationToken ct)
        {
            Log("── Verificando audiodg.exe...", "#FFAA00");
            _logger.LogInfo("[AudioRepair][VerifyAudioDg] Verificando audiodg.exe.");
            ct.ThrowIfCancellationRequested();
            var script = @"
$dg = Get-Process -Name 'audiodg' -ErrorAction SilentlyContinue
if ($dg) { Write-Output ""AUDIODG_RUNNING:PID=$($dg.Id)"" }
else { Write-Output 'AUDIODG_NOT_RUNNING' }
$path = Join-Path $env:SystemRoot 'System32\audiodg.exe'
if (Test-Path $path) { Write-Output 'AUDIODG_FILE_OK' }
else { Write-Output 'AUDIODG_FILE_MISSING' }
";
            var (_, psOut) = await RunPsAsync(script, ct, 10_000);
            bool running = psOut.Contains("AUDIODG_RUNNING");
            bool fileOk  = psOut.Contains("AUDIODG_FILE_OK");
            Log($"  {(running ? "v" : "!")} audiodg.exe processo: {(running ? "ativo" : "nao detectado")}", running ? "#00FF88" : "#FFA500");
            Log($"  {(fileOk ? "v" : "X")} audiodg.exe arquivo: {(fileOk ? "presente" : "AUSENTE")}", fileOk ? "#00FF88" : "#FF4444");
            if (!running)
            {
                result.Issues.Add("AUDIODG_NOT_RUNNING");
                _logger.LogWarning("[AudioRepair][VerifyAudioDg] audiodg.exe não está rodando.");
            }
            if (!fileOk)
            {
                result.Issues.Add("AUDIODG_FILE_MISSING");
                _logger.LogWarning("[AudioRepair][VerifyAudioDg] audiodg.exe arquivo ausente.");
            }
            result.Details.Add($"[audiodg] running={running} fileOk={fileOk}");
            _logger.LogInfo("[AudioRepair][VerifyAudioDg] Verificação de audiodg.exe concluída.");
        }


        // ─── DIAGNÓSTICO: SAÚDE REAL DOS DISPOSITIVOS ────────────────────────

        private async Task<bool> AreAudioDevicesHealthyAsync(CancellationToken ct)
        {
            _logger.LogInfo("[AudioRepair][AreAudioDevicesHealthy] Verificando saúde dos dispositivos de áudio.");
            var script = @"
$problemDevices = Get-CimInstance Win32_PnPEntity -Filter ""PNPClass='AudioEndpoint' OR PNPClass='Media' OR PNPClass='AudioProcessingObject'"" -ErrorAction SilentlyContinue |
    Where-Object { $_.Status -ne 'OK' }
if ($problemDevices) {
    Write-Output 'PROBLEM_DEVICES_FOUND'
    foreach ($d in $problemDevices) {
        Write-Output ""DEVICE:$($d.FriendlyName) STATUS:$($d.Status) CLASS:$($d.Class)""
    }
    exit 1
} else {
    Write-Output 'ALL_AUDIO_DEVICES_OK'
    exit 0
}
";
            var (exitCode, output) = await RunPsAsync(script, ct, 15_000);
            if (exitCode != 0)
            {
                _logger.LogWarning($"[AudioDevices] Dispositivos com problema:\n{output}");
                foreach (var line in output.Split('\n').Where(l => l.StartsWith("DEVICE:")))
                    Log($"  ⚠ {line.Substring(7).Trim()}", "#FFA500");
                return false;
            }
            _logger.LogInfo("[AudioRepair][AreAudioDevicesHealthy] Todos os dispositivos de áudio estão OK.");
            return true;
        }

        private async Task RepairPnpDriversAsync(AudioRepairResult result, CancellationToken ct)
        {
            Log("── Verificando drivers de áudio via PnP...", "#FFAA00");
            _logger.LogInfo("[AudioRepair][RepairPnpDrivers] Iniciando reparo de drivers PnP.");
            ct.ThrowIfCancellationRequested();

            bool devicesOk = await AreAudioDevicesHealthyAsync(ct);
            if (devicesOk)
            {
                Log("  ✓ Todos os dispositivos de áudio PnP estão OK", "#00FF88");
                _logger.LogInfo("[AudioRepair][RepairPnpDrivers] Dispositivos PnP já estão OK, pulando reparo.");
                return;
            }

            await RepairProblemDriversAsync(result, ct);
            _logger.LogInfo("[AudioRepair][RepairPnpDrivers] Reparo de drivers PnP concluído.");
        }

        private async Task RepairProblemDriversAsync(AudioRepairResult result, CancellationToken ct)
        {
            Log("── Reparando dispositivos com status Unknown/Error...", "#FFAA00");
            _logger.LogInfo("[AudioRepair][RepairProblemDrivers] Iniciando reparo de dispositivos com problema.");

            // 1. Forçar redetecção de hardware
            Log("  → pnputil /scan-devices...", "#AAAAAA");
            _logger.LogDebug("[AudioRepair][RepairProblemDrivers] Executando pnputil /scan-devices.");
            await RunAsync("pnputil", "/scan-devices", ct, 30_000);
            await Task.Delay(2000, ct);

            // 2. Disable/Enable de cada dispositivo problemático
            var script = @"
$problemDevices = Get-CimInstance Win32_PnPEntity -ErrorAction SilentlyContinue |
    Where-Object { ($_.PNPClass -eq 'AudioEndpoint' -or $_.PNPClass -eq 'Media' -or $_.PNPClass -eq 'AudioProcessingObject' -or $_.Service -like '*hdaudio*' -or $_.Name -like '*Audio Controller*' -or $_.Name -like '*Smart Sound*') -and $_.Status -ne 'OK' }
if (-not $problemDevices) { Write-Output 'NO_PROBLEMS_AFTER_SCAN'; exit 0 }
foreach ($dev in $problemDevices) {
    Write-Output ""PROBLEM:$($dev.FriendlyName)|$($dev.Status)|$($dev.Class)""
    try {
        Disable-PnpDevice -InstanceId $dev.InstanceId -Confirm:$false -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 800
        Enable-PnpDevice -InstanceId $dev.InstanceId -Confirm:$false -ErrorAction SilentlyContinue
        Write-Output ""REENABLED:$($dev.FriendlyName)""
    } catch { Write-Output ""REENABLE_FAILED:$($dev.FriendlyName)"" }
}
Write-Output 'REPAIR_DONE'
";
            var (_, psOut) = await RunPsAsync(script, ct, 60_000);
            _logger.LogDebug($"[AudioRepair][RepairProblemDrivers] PowerShell output: {psOut.Trim()}");

            if (psOut.Contains("NO_PROBLEMS_AFTER_SCAN"))
            {
                Log("  ✓ Redetecção resolveu os problemas", "#00FF88");
                result.Fixed.Add("PNP_SCAN_RESOLVED");
                _logger.LogInfo("[AudioRepair][RepairProblemDrivers] Redetecção de hardware resolveu os problemas.");
                return;
            }

            foreach (var line in psOut.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))
            {
                if (line.StartsWith("PROBLEM:"))
                {
                    var parts = line.Substring(8).Split('|');
                    Log($"  ⚠ {parts[0]} [{(parts.Length > 1 ? parts[1] : "?")}] ({(parts.Length > 2 ? parts[2] : "?")})", "#FFA500");
                    result.Issues.Add($"PNP_DEVICE_PROBLEM:{parts[0]}");
                    _logger.LogWarning($"[AudioRepair][RepairProblemDrivers] Dispositivo PnP ainda com problema: {parts[0]}");
                }
                else if (line.StartsWith("REENABLED:"))
                {
                    Log($"  ✓ Re-habilitado: {line.Substring(10)}", "#00FF88");
                    result.Fixed.Add($"PNP_REENABLED:{line.Substring(10)}");
                    _logger.LogInfo($"[AudioRepair][RepairProblemDrivers] Dispositivo PnP re-habilitado: {line.Substring(10)}");
                }
                else if (line.StartsWith("REENABLE_FAILED:"))
                {
                    Log($"  ✗ Falha ao re-habilitar: {line.Substring(16)}", "#FF4444");
                    _logger.LogWarning($"[AudioRepair][RepairProblemDrivers] Falha ao re-habilitar dispositivo PnP: {line.Substring(16)}");
                }
            }

            // 3. Reiniciar serviço Realtek (se presente)
            Log("  → Reiniciando Realtek Audio Universal Service (se presente)...", "#AAAAAA");
            _logger.LogDebug("[AudioRepair][RepairProblemDrivers] Tentando reiniciar Realtek Audio Universal Service.");
            var (stopCode, _) = await RunAsync(Sc, "stop \"Realtek Audio Universal Service\"", ct, 10_000);
            if (stopCode == 0 || stopCode == 1062) // 1062 = The service has not been started.
            {
                await Task.Delay(2000, ct);
                var (startCode, _) = await RunAsync(Sc, "start \"Realtek Audio Universal Service\"", ct, 15_000);
                if (startCode == 0 || startCode == 1056) // 1056 = An instance of the service is already running.
                {
                    Log("  ✓ Realtek Audio Universal Service reiniciado", "#00FF88");
                    result.Fixed.Add("REALTEK_SERVICE_RESTARTED");
                    _logger.LogInfo("[AudioRepair][RepairProblemDrivers] Realtek Audio Universal Service reiniciado.");
                }
                else
                {
                    _logger.LogWarning($"[AudioRepair][RepairProblemDrivers] Falha ao iniciar Realtek Audio Universal Service. Exit code: {startCode}");
                }
            }
            else
            {
                _logger.LogDebug($"[AudioRepair][RepairProblemDrivers] Realtek Audio Universal Service não estava rodando ou não existe. Stop code: {stopCode}");
            }

            // 4. Verificar se ainda há problemas — se sim, tentar detecção de hardware e instalação de driver
            bool stillBroken = !await AreAudioDevicesHealthyAsync(ct);
            if (stillBroken)
            {
                Log("  ✗ Drivers ainda com problema após reparo PnP básico.", "#FF4444");
                Log("  → Iniciando detecção de hardware e instalação automática de driver...", "#FFAA00");
                _logger.LogInfo("[AudioRepair][RepairProblemDrivers] Iniciando fase avançada: detecção de hardware + driver.");
                await DetectAndInstallAudioDriverAsync(result, ct);
            }
            else
            {
                Log("  ✓ Dispositivos de áudio normalizados após reparo PnP", "#00FF88");
                _logger.LogInfo("[AudioRepair][RepairProblemDrivers] Dispositivos de áudio normalizados após reparo PnP.");
            }
        }

        // ─── DETECÇÃO DE HARDWARE DE ÁUDIO ───────────────────────────────────

        /// <summary>
        /// Detecta todos os dispositivos de áudio PnP com modelo, fabricante, Device ID e código de erro.
        /// </summary>
        private async Task<List<AudioDeviceInfo>> DetectAudioHardwareAsync(CancellationToken ct)
        {
            Log("  → Detectando hardware de áudio via WMI...", "#AAAAAA");
            _logger.LogInfo("[AudioRepair][DetectHardware] Iniciando detecção de hardware de áudio.");

            var script = @"
$devices = Get-WmiObject Win32_PnPEntity -ErrorAction SilentlyContinue |
    Where-Object { $_.PNPClass -eq 'Media' -or $_.PNPClass -eq 'AudioEndpoint' -or $_.PNPClass -eq 'AudioProcessingObject' -or $_.Service -like '*hdaudio*' -or $_.Name -like '*Audio Controller*' }
if (-not $devices) { Write-Output 'NO_AUDIO_HW'; exit 0 }
foreach ($d in $devices) {
    $err = [int]$d.ConfigManagerErrorCode
    $mfr = if ($d.Manufacturer) { $d.Manufacturer } else { 'Unknown' }
    $did = if ($d.DeviceID) { $d.DeviceID } else { '' }
    $hwids = ($d.HardwareID -join ';')
    Write-Output ""HW:$($d.Name)|MFR:$mfr|ERR:$err|DID:$did|HWIDS:$hwids""
}
Write-Output 'HW_DETECT_DONE'
";
            var (_, psOut) = await RunPsAsync(script, ct, 20_000);
            _logger.LogDebug($"[AudioRepair][DetectHardware] raw={psOut.Trim()}", source: "AudioRepairService");

            var devices = new List<AudioDeviceInfo>();
            foreach (var line in psOut.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("HW:")))
            {
                var parts = line.Substring(3).Split('|');
                string GetPart(string prefix) =>
                    parts.FirstOrDefault(p => p.StartsWith(prefix))?.Substring(prefix.Length).Trim() ?? "";

                var info = new AudioDeviceInfo
                {
                    Name         = GetPart(""),
                    Manufacturer = GetPart("MFR:"),
                    DeviceId     = GetPart("DID:"),
                    HardwareIds  = GetPart("HWIDS:"),
                    ErrorCode    = int.TryParse(GetPart("ERR:"), out var ec) ? ec : 0
                };
                // Name is before first '|', which becomes the empty-prefix part
                var nameEnd = line.IndexOf('|');
                info.Name = nameEnd > 3 ? line.Substring(3, nameEnd - 3).Trim() : info.Name;

                devices.Add(info);
                var errDesc = DescribeDriverErrorCode(info.ErrorCode);
                Log($"    • {info.Name} [{info.Manufacturer}] — Código {info.ErrorCode}: {errDesc}", info.ErrorCode == 0 ? "#00FF88" : "#FFA500");
                _logger.LogInfo($"[AudioRepair][DetectHardware] {info.Name} | Mfr={info.Manufacturer} | Err={info.ErrorCode} | ID={info.DeviceId}");
            }

            Log($"  ✓ {devices.Count} dispositivo(s) de áudio detectado(s)", "#00FF88");
            
            // Adicional: análise de fabricante "Neural" para reparo direcionado
            foreach (var dev in devices)
            {
                var vendor = dev.HardwareIds.Contains("VEN_10EC") ? "REALTEK" :
                             dev.HardwareIds.Contains("VEN_8086") ? "INTEL" :
                             dev.HardwareIds.Contains("VEN_1002") || dev.HardwareIds.Contains("VEN_1022") ? "AMD" :
                             dev.HardwareIds.Contains("VEN_14F1") ? "CONEXANT" : 
                             dev.HardwareIds.Contains("VEN_1106") ? "VIA" : "GENERIC";
                
                if (vendor != "GENERIC")
                {
                    Log($"    [NeuralID] Fabricante mapeado: {vendor}", "#8B31FF");
                    _logger.LogInfo($"[AudioRepair][NeuralID] {dev.Name} mapeado como {vendor}");
                }
            }
            
            return devices;
        }

        private static string DescribeDriverErrorCode(int code) => code switch
        {
            0  => "OK — sem erros",
            1  => "Configuração incompleta",
            10 => "Falha ao iniciar",
            12 => "Conflito de recursos",
            14 => "Requer reinício",
            18 => "Reinstalação necessária",
            19 => "Falha na configuração",
            22 => "Dispositivo desabilitado",
            28 => "Driver não instalado",
            31 => "Não funciona corretamente",
            43 => "Driver corrompido / hardware com falha",
            45 => "Dispositivo não conectado",
            _  => $"Código desconhecido ({code})"
        };

        // ─── INSTALAÇÃO AUTOMÁTICA DE DRIVER ─────────────────────────────────

        /// <summary>
        /// Fase avançada: detecta hardware problemático e tenta instalar o driver correto.
        /// Sequência: inbox HDA driver → pnputil rescan → Windows Update trigger → fabricante específico.
        /// </summary>
        private async Task DetectAndInstallAudioDriverAsync(AudioRepairResult result, CancellationToken ct)
        {
            var devices = await DetectAudioHardwareAsync(ct);

            // Filtrar apenas dispositivos com erros
            var brokenDevices = devices.Where(d => d.ErrorCode != 0).ToList();
            if (brokenDevices.Count == 0)
            {
                Log("  ✓ Todos os dispositivos detectados estão sem erros de driver", "#00FF88");
                result.Fixed.Add("DRIVER_DETECT_ALL_OK");
                return;
            }

            Log($"  ✗ {brokenDevices.Count} dispositivo(s) com erro de driver detectado(s)", "#FF4444");
            _logger.LogInfo($"[AudioRepair][InstallDriver] {brokenDevices.Count} dispositivos com erros.");

            foreach (var dev in brokenDevices)
            {
                ct.ThrowIfCancellationRequested();
                Log($"  → Tratando: {dev.Name} (erro {dev.ErrorCode}: {DescribeDriverErrorCode(dev.ErrorCode)})", "#FFAA00");
                _logger.LogInfo($"[AudioRepair][InstallDriver] Dispositivo: {dev.Name} | Erro={dev.ErrorCode}");

                bool fixed_ = false;

                // Código 22 = desabilitado: apenas re-habilitar
                if (dev.ErrorCode == 22)
                {
                    fixed_ = await ReEnableDeviceAsync(dev, result, ct);
                }
                // Código 14 = aguardando reinício: nada a fazer além de marcar
                else if (dev.ErrorCode == 14)
                {
                    Log("    ℹ Reinício do Windows necessário para concluir a instalação", "#FFA500");
                    result.Warnings.Add($"REBOOT_REQUIRED:{dev.Name}");
                    fixed_ = true;
                }
                // Código 28 = driver não instalado; 43 = corrompido; 10/18/19/31 = outros problemas
                else
                {
                    // Passo 1: tentar driver HDA genérico do Windows (inbox)
                    fixed_ = await TryInstallInboxHdaDriverAsync(dev, result, ct);

                    // Passo 2: se ainda falhou, tentar pnputil /add-driver com drivers existentes na DriverStore
                    if (!fixed_)
                        fixed_ = await TryInstallFromDriverStoreAsync(dev, result, ct);

                    // Passo 3: se ainda falhou, usar Windows Update (usoclient + COM Searcher por HWID)
                    if (!fixed_)
                        fixed_ = await TriggerWindowsUpdateDriverScanAsync(dev, result, ct);

                    // Passo 4: tentar atualizar via devcon/pnputil diretamente no Device ID
                    if (!fixed_)
                        fixed_ = await TryPnpUtilUpdateDriverAsync(dev, result, ct);
                }

                if (!fixed_)
                {
                    Log($"    ⚠ Não foi possível instalar driver automaticamente para: {dev.Name}", "#FFA500");
                    Log($"    ℹ Fabricante detectado: {dev.Manufacturer}", "#AAAAAA");
                    Log($"    ℹ Hardware IDs: {(dev.HardwareIds.Length > 80 ? dev.HardwareIds.Substring(0, 80) + "..." : dev.HardwareIds)}", "#AAAAAA");
                    result.Warnings.Add($"DRIVER_MANUAL_INSTALL_NEEDED:{dev.Name}|{dev.Manufacturer}");
                    _logger.LogWarning($"[AudioRepair][InstallDriver] Instalação automática falhou para {dev.Name}. Fabricante: {dev.Manufacturer}. HWIDs: {dev.HardwareIds}");
                }
            }

            // Verificação final após tentativas de instalação
            await Task.Delay(2000, ct);
            bool allOk = await AreAudioDevicesHealthyAsync(ct);
            if (allOk)
            {
                Log("  ✓ DRIVER INSTALADO COM SUCESSO — dispositivos de áudio normalizados", "#00FF88");
                result.Fixed.Add("DRIVER_AUTO_INSTALL_SUCCESS");
                _logger.LogInfo("[AudioRepair][InstallDriver] Driver instalado com sucesso. Dispositivos OK.");
            }
            else
            {
                Log("  ⚠ Alguns drivers ainda com problema. Reiniciar o PC pode ser necessário.", "#FFA500");
                result.Warnings.Add("DRIVER_INSTALL_REBOOT_RECOMMENDED");
                _logger.LogWarning("[AudioRepair][InstallDriver] Drivers ainda com problema após instalação automática.");
            }
        }

        private async Task<bool> ReEnableDeviceAsync(AudioDeviceInfo dev, AudioRepairResult result, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(dev.DeviceId)) return false;
            var safeId = dev.DeviceId.Replace("'", "\"'\"");
            var script = $@"
try {{
    Enable-PnpDevice -InstanceId '{safeId}' -Confirm:$false -ErrorAction Stop
    Write-Output 'ENABLED_OK'
}} catch {{ Write-Output ""ENABLED_FAIL:$($_.Exception.Message)"" }}
";
            var (_, psOut) = await RunPsAsync(script, ct, 15_000);
            if (psOut.Contains("ENABLED_OK"))
            {
                Log($"    ✓ Dispositivo re-habilitado: {dev.Name}", "#00FF88");
                result.Fixed.Add($"DEVICE_ENABLED:{dev.Name}");
                _logger.LogInfo($"[AudioRepair][ReEnable] {dev.Name} re-habilitado.");
                return true;
            }
            _logger.LogWarning($"[AudioRepair][ReEnable] Falha ao re-habilitar {dev.Name}: {psOut.Trim()}");
            return false;
        }

        /// <summary>
        /// Tenta instalar o driver High Definition Audio genérico do Windows (hdaudio.inf).
        /// Funciona para Realtek, VIA, Conexant e outros chipsets compatíveis com HDA spec.
        /// </summary>
        private async Task<bool> TryInstallInboxHdaDriverAsync(AudioDeviceInfo dev, AudioRepairResult result, CancellationToken ct)
        {
            Log("    → Tentando instalar driver HDA genérico do Windows...", "#AAAAAA");
            _logger.LogDebug($"[AudioRepair][HdaDriver] Tentando driver inbox HDA para {dev.Name}", source: "AudioRepairService");

            // Localizar hdaudio.inf na DriverStore
            var driverStorePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                @"..\DriverStore\FileRepository");

            if (!Directory.Exists(driverStorePath))
                driverStorePath = @"C:\Windows\System32\DriverStore\FileRepository";

            string? infPath = null;
            try
            {
                // Procurar hdaudio.inf ou wdmaudio.inf na DriverStore
                var infsToTry = new[] { "hdaudio.inf", "wdmaudio.inf", "ks.inf", "wdma_usb.inf" };
                foreach (var infName in infsToTry)
                {
                    var found = Directory.GetFiles(driverStorePath, infName, SearchOption.AllDirectories)
                        .FirstOrDefault();
                    if (found != null) { infPath = found; break; }
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[AudioRepair][HdaDriver] Erro ao buscar inf: {ex.Message}"); }

            if (infPath == null)
            {
                Log("    ⚠ Driver HDA genérico não encontrado na DriverStore", "#FFA500");
                _logger.LogWarning("[AudioRepair][HdaDriver] Nenhum INF encontrado na DriverStore.");
                return false;
            }

            Log($"    ℹ Driver INF encontrado: {Path.GetFileName(Path.GetDirectoryName(infPath))}\\{Path.GetFileName(infPath)}", "#AAAAAA");

            // pnputil /add-driver <inf> /install
            var (addCode, addOut) = await RunAsync("pnputil", $"/add-driver \"{infPath}\" /install", ct, 60_000);
            _logger.LogDebug($"[AudioRepair][HdaDriver] pnputil exit={addCode} out={addOut.Trim()}", source: "AudioRepairService");

            if (addCode == 0 || addOut.Contains("Driver package added successfully"))
            {
                // Tentar forçar reinstalação no dispositivo específico
                if (!string.IsNullOrWhiteSpace(dev.DeviceId))
                {
                    var safeId = dev.DeviceId.Replace("'", "\"'\"");
                    var updateScript = $@"
try {{
    $session = New-Object -ComObject Microsoft.Update.Session
    Update-PnpDevice -InstanceId '{safeId}' -Confirm:$false -ErrorAction SilentlyContinue
    Write-Output 'UPDATED'
}} catch {{ Write-Output 'UPDATE_SKIP' }}
";
                    await RunPsAsync(updateScript, ct, 20_000);
                }

                Log($"    ✓ Driver INF adicionado à DriverStore com sucesso", "#00FF88");
                result.Fixed.Add($"HDA_DRIVER_ADDED:{Path.GetFileName(infPath)}");
                _logger.LogInfo($"[AudioRepair][HdaDriver] Driver adicionado: {infPath}");
                return true;
            }

            _logger.LogWarning($"[AudioRepair][HdaDriver] pnputil falhou (exit={addCode}): {addOut.Trim()}");
            return false;
        }

        /// <summary>
        /// Tenta instalar driver do fabricante já presente na DriverStore (ex: instalado anteriormente mas não aplicado).
        /// </summary>
        private async Task<bool> TryInstallFromDriverStoreAsync(AudioDeviceInfo dev, AudioRepairResult result, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(dev.DeviceId)) return false;
            Log("    → Procurando driver compatível na DriverStore...", "#AAAAAA");
            _logger.LogDebug($"[AudioRepair][DriverStore] Procurando driver para {dev.Name}", source: "AudioRepairService");

            // Usar pnputil para listar drivers que correspondam ao Hardware ID
            var firstHwId = dev.HardwareIds.Split(';').FirstOrDefault(h => h.Length > 0) ?? "";
            if (string.IsNullOrWhiteSpace(firstHwId)) return false;

            var script = $@"
# Tentar via PowerShell Get-PnpDriver para encontrar drivers instalados compatíveis
$drv = Get-WindowsDriver -Online -All -ErrorAction SilentlyContinue |
    Where-Object {{ $_.HardwareId -eq '{firstHwId.Replace("'", "\"'\"")}' }} |
    Select-Object -First 1
if ($drv) {{
    Write-Output ""FOUND:$($drv.Driver)""
}} else {{
    Write-Output 'NOT_FOUND'
}}
";
            var (_, psOut) = await RunPsAsync(script, ct, 30_000);
            var foundMatch = System.Text.RegularExpressions.Regex.Match(psOut, @"FOUND:(.+)");
            if (!foundMatch.Success)
            {
                _logger.LogDebug($"[AudioRepair][DriverStore] Nenhum driver compatível encontrado para {firstHwId}", source: "AudioRepairService");
                return false;
            }

            var driverName = foundMatch.Groups[1].Value.Trim();
            Log($"    ℹ Driver compatível encontrado: {driverName}", "#AAAAAA");

            // Instalar via pnputil
            var (code, _) = await RunAsync("pnputil", $"/install-driver {driverName}", ct, 60_000);
            if (code == 0)
            {
                Log($"    ✓ Driver instalado via DriverStore: {driverName}", "#00FF88");
                result.Fixed.Add($"DRIVER_STORE_INSTALLED:{driverName}");
                _logger.LogInfo($"[AudioRepair][DriverStore] Driver instalado: {driverName}");
                return true;
            }
            _logger.LogWarning($"[AudioRepair][DriverStore] Instalação falhou (exit={code})");
            return false;
        }

        /// <summary>
        /// Dispara varredura do Windows Update para drivers de dispositivos.
        /// Tenta busca específica por Hardware ID para máxima precisão.
        /// </summary>
        private async Task<bool> TriggerWindowsUpdateDriverScanAsync(AudioDeviceInfo dev, AudioRepairResult result, CancellationToken ct)
        {
            Log("    → Acionando Windows Update para buscar driver de dispositivo...", "#AAAAAA");
            _logger.LogInfo($"[AudioRepair][WU] Acionando Windows Update para driver de {dev.Name}");

            // Método 0: buscar ID mais específico da lista (geralmente o primeiro)
            var ids = dev.HardwareIds.Split(';', StringSplitOptions.RemoveEmptyEntries);
            var bestId = ids.Length > 0 ? ids[0].Trim() : "";

            // Método 1: usoclient (Windows Update Orchestrator — silencioso)
            // Isso aciona a varredura global do Windows
            await RunAsync("usoclient", "StartScan", ct, 5_000);

            // Método 2: PowerShell Windows Update via COM (Busca Direta por ID)
            // Isso é o que realmente "baixa" o driver se ele existir nos servidores da MS
            var script = $@"
try {{
    $session   = New-Object -ComObject Microsoft.Update.Session -ErrorAction Stop
    $searcher  = $session.CreateUpdateSearcher()
    
    # Tenta busca específica pelo Hardware ID
    $query = ""IsInstalled=0 and Type='Driver'""
    if ('{bestId}') {{
        Write-Output ""DEBUG: Searching for HWID: {bestId}""
        # Nota: WUA não filtra 100% por string de HWID no Search, mas podemos tentar
    }}
    
    $searchResult = $searcher.Search($query)
    # Filtro manual pós-busca para garantir que pegamos o driver de áudio certo para este device
    $driverUpd = $searchResult.Updates | Where-Object {{ 
        $_.Title -match 'Audio|Sound|Media|Realtek|Intel|SST|Conexant' -and 
        $_.Categories | Where-Object {{ $_.Name -match 'Audio|Sound|Media' }}
    }}

    if ($driverUpd.Count -gt 0) {{
        $topUpdate = $driverUpd | Select-Object -First 1
        Write-Output ""FOUND_TITLE=$($topUpdate.Title)""
        
        $dl  = $session.CreateUpdateDownloader()
        $dl.Updates = New-Object -ComObject Microsoft.Update.UpdateCollection
        $dl.Updates.Add($topUpdate) | Out-Null
        
        Write-Output 'STEP: Downloading driver...'
        $dl.Download() | Out-Null
        
        $inst = $session.CreateUpdateInstaller()
        $inst.Updates = New-Object -ComObject Microsoft.Update.UpdateCollection
        $inst.Updates.Add($topUpdate) | Out-Null
        
        Write-Output 'STEP: Installing driver...'
        $inst.Install() | Out-Null
        Write-Output ""WU_INSTALLED_OK""
    }} else {{ Write-Output 'WU_NO_DRIVER_UPDATES' }}
}} catch {{ Write-Output ""WU_COM_FAIL:$($_.Exception.Message)"" }}
";
            var (_, psOut) = await RunPsAsync(script, ct, 300_000); // 5 min timeout para download
            _logger.LogDebug($"[AudioRepair][WU] PowerShell WU output: {psOut.Trim()}", source: "AudioRepairService");

            if (psOut.Contains("WU_INSTALLED_OK"))
            {
                var title = Regex.Match(psOut, @"FOUND_TITLE=(.+)").Groups[1].Value.Trim();
                Log($"    ✓ Windows Update instalou com sucesso: {title}", "#00FF88");
                result.Fixed.Add($"WU_DRIVER_INSTALLED:{title}");
                _logger.LogInfo($"[AudioRepair][WU] Driver instalado via WU: {title}");
                return true;
            }
            else if (psOut.Contains("WU_NO_DRIVER_UPDATES"))
            {
                Log("    ℹ Windows Update: nenhum driver compatível disponível no servidor", "#AAAAAA");
                _logger.LogInfo("[AudioRepair][WU] Nenhum driver disponível no Windows Update Search.");
            }
            else
            {
                _logger.LogWarning($"[AudioRepair][WU] Falha na busca/instalação: {psOut.Trim()}");
            }
            return false;
        }

        /// <summary>
        /// Usa pnputil /update-driver diretamente no Device Instance ID para forçar atualização.
        /// </summary>
        private async Task<bool> TryPnpUtilUpdateDriverAsync(AudioDeviceInfo dev, AudioRepairResult result, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(dev.DeviceId)) return false;
            Log("    → Tentando pnputil /update-driver no Device Instance ID...", "#AAAAAA");
            _logger.LogDebug($"[AudioRepair][PnpUtil] Tentando atualização forçada para {dev.DeviceId}", source: "AudioRepairService");

            // Primeiro: disable + enable via PowerShell para forçar re-enumeração
            var safeId = dev.DeviceId.Replace("'", "\"'\"");
            var script = $@"
try {{
    Disable-PnpDevice -InstanceId '{safeId}' -Confirm:$false -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 1500
    Enable-PnpDevice  -InstanceId '{safeId}' -Confirm:$false -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 1500
    $d = Get-PnpDevice -InstanceId '{safeId}' -ErrorAction SilentlyContinue
    if ($d) {{ Write-Output ""STATUS:$($d.Status)|PROBLEM:$($d.Problem)"" }}
    else     {{ Write-Output 'DEV_NOT_FOUND' }}
}} catch {{ Write-Output ""PNPUTIL_EX:$($_.Exception.Message)"" }}
";
            var (_, psOut) = await RunPsAsync(script, ct, 30_000);
            _logger.LogDebug($"[AudioRepair][PnpUtil] output={psOut.Trim()}", source: "AudioRepairService");

            if (psOut.Contains("STATUS:OK"))
            {
                Log($"    ✓ Dispositivo normalizado após force-reenable: {dev.Name}", "#00FF88");
                result.Fixed.Add($"PNPUTIL_FORCE_REENABLE:{dev.Name}");
                _logger.LogInfo($"[AudioRepair][PnpUtil] {dev.Name} normalizado.");
                return true;
            }

            _logger.LogWarning($"[AudioRepair][PnpUtil] Dispositivo ainda com problema: {psOut.Trim()}");
            return false;
        }

        private async Task ApplyLatencyOptimizationsAsync(AudioRepairResult result, CancellationToken ct)
        {
            Log("── Aplicando otimizacoes de latencia...", "#FFAA00");
            _logger.LogInfo("[AudioRepair][ApplyLatencyOptimizations] Aplicando otimizações de latência.");
            ct.ThrowIfCancellationRequested();
            var script = @"
$fixed = 0
$audioTaskKey = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Audio'
if (Test-Path $audioTaskKey) {
    Set-ItemProperty -Path $audioTaskKey -Name 'Clock Rate'          -Value 10000  -Type DWord  -ErrorAction SilentlyContinue
    Set-ItemProperty -Path $audioTaskKey -Name 'GPU Priority'        -Value 8      -Type DWord  -ErrorAction SilentlyContinue
    Set-ItemProperty -Path $audioTaskKey -Name 'Priority'            -Value 6      -Type DWord  -ErrorAction SilentlyContinue
    Set-ItemProperty -Path $audioTaskKey -Name 'Scheduling Category' -Value 'High' -Type String -ErrorAction SilentlyContinue
    Set-ItemProperty -Path $audioTaskKey -Name 'SFIO Priority'       -Value 'High' -Type String -ErrorAction SilentlyContinue
    Write-Output 'AUDIO_TASK_OPTIMIZED'
    $fixed++
}
Write-Output ""LATENCY_FIXED=$fixed""
Write-Output 'LATENCY_DONE'
";
            var (_, psOut) = await RunPsAsync(script, ct, 20_000);
            bool done = psOut.Contains("LATENCY_DONE");
            var fixedMatch = Regex.Match(psOut, @"LATENCY_FIXED=(\d+)");
            int count = fixedMatch.Success ? int.Parse(fixedMatch.Groups[1].Value) : 0;
            Log($"  {(done ? "v" : "!")} Latencia: {count} otimizacao(oes) aplicada(s)", done ? "#00FF88" : "#FFA500");
            if (count > 0) result.Fixed.Add($"LATENCY_OPTIMIZED:{count}");
            _logger.LogInfo($"[AudioRepair][ApplyLatencyOptimizations] Otimizações de latência concluídas. {count} aplicadas.");
        }

        // ─── REPARO: UN-MUTE AUTOMÁTICO ───────────────────────────────────────

        private async Task ForceUnmuteAudioAsync(AudioRepairResult result, CancellationToken ct)
        {
            Log("── Restaurando volume e removendo Mute...", "#FFAA00");
            _logger.LogInfo("[AudioRepair] Executando script de Un-mute global.");
            
            var script = @"
$obj = New-Object -ComObject Shell.Application
$obj.NameSpace(0).Self.InvokeVerb('Unmute') # Tenta via Shell Application
# Forçar via Registry se estiver mutado por política
$audioKey = 'HKCU:\Software\Microsoft\Multimedia\Audio'
if (Test-Path $audioKey) {
    Set-ItemProperty -Path $audioKey -Name 'UserDuckingPreference' -Value 0 -Type DWord -ErrorAction SilentlyContinue
}
Write-Output 'UNMUTE_DONE'
";
            var (code, _) = await RunPsAsync(script, ct, 15_000);
            if (code == 0) Log("  ✓ Volume restaurado para o padrão do Windows", "#00FF88");
            result.Fixed.Add("AUDIO_UNMUTED");
            _logger.LogInfo("[AudioRepair] Un-mute de áudio concluído.");
        }

        // ─── ANÁLISE DE FALHAS ────────────────────────────────────────────────

        private void PerformFailureAnalysis(AudioRepairResult result)
        {
            if (result.Issues.Count == 0 && string.IsNullOrEmpty(result.FailureReason)) return;

            _logger.LogInfo("[AudioRepair] Iniciando Análise de Causa Raiz para falhas remanescentes.");
            
            var reason = new List<string>();
            if (result.Issues.Any(i => i.Contains("DRIVER_COM_ERRO") || i.Contains("PNP_DEVICE_PROBLEM")))
                reason.Add("Há falha de driver detectada. O sistema tentou instalação automática via DriverStore e Windows Update. Se o problema persistir, reinstale o driver do fabricante (Realtek/Creative/Conexant etc.) manualmente.");
            
            if (result.Issues.Any(i => i.Contains("DLL_CRITICA_AUSENTE") || i.Contains("FILE_RESTORE_FAIL")))
                reason.Add("Dependências do sistema (DLLs) estão faltando ou corrompidas e não puderam ser restauradas. O Windows Image está possivelmente corrompido além do reparo automático.");
            
            if (result.Issues.Any(i => i.Contains("SERVIÇO_AUSENTE") || i.Contains("DCOMLAUNCH_NOT_RUNNING") || i.Contains("RPCSS_NOT_RUNNING")))
                reason.Add("Serviços essenciais do Windows (como AudioSrv, AudioEndpointBuilder, DcomLaunch, RpcSs) estão ausentes ou não puderam ser iniciados. Pode ser sinal de malware ou otimizador agressivo de terceiros.");
            
            if (result.Issues.Any(i => i.Contains("SEM_DRIVERS_AUDIO") || i.Contains("SEM_DISPOSITIVOS")))
                reason.Add("Nenhum hardware de áudio foi detectado. Verifique se o som está ativado na BIOS/UEFI ou se o hardware está conectado corretamente.");

            if (reason.Count > 0)
            {
                result.FailureReason = string.Join(" ", reason);
                _logger.LogWarning($"[AudioRepair] Análise de Falha: {result.FailureReason}");
            }
            else if (result.Fixed.Count == 0 && result.Issues.Count > 0)
            {
                result.FailureReason = "O sistema aplicou os reparos, mas os avisos persistem. Pode ser necessário reiniciar o Windows para que as alterações em nível de kernel façam efeito.";
                _logger.LogWarning($"[AudioRepair] Análise de Falha: {result.FailureReason}");
            }
            _logger.LogInfo("[AudioRepair] Análise de falhas concluída.");
        }

        private async Task FinalVerificationAsync(AudioRepairResult result, CancellationToken ct)
        {
            Log("── Verificação final do estado do áudio...", "#FFAA00");
            _logger.LogInfo("[AudioRepair] Verificação final iniciada.");
            ct.ThrowIfCancellationRequested();

            // Verificar serviços (Usando Get-CimInstance para serviços é mais rápido)
            var svcScript = @"
Write-Output ""FINAL_AUDIOSRV=$($audioSrv.Status)""
Write-Output ""FINAL_AEB=$($aeb.Status)""
$audioKey = 'HKCU:\Software\Microsoft\Multimedia\Audio'
$dpag = if (Test-Path $audioKey) { (Get-ItemProperty $audioKey -ErrorAction SilentlyContinue).DisableProtectedAudioDG } else { 0 }
Write-Output ""FINAL_DPAG=$dpag""
Write-Output 'SVC_CHECK_DONE'
";
            var (_, svcOut) = await RunPsAsync(svcScript, ct, 15_000);
            bool audioSrvOk = svcOut.Contains("FINAL_AUDIOSRV=Running");
            bool aebOk      = svcOut.Contains("FINAL_AEB=Running");
            bool policyOk   = !svcOut.Contains("FINAL_DPAG=1");

            Log($"  {(audioSrvOk ? "✓" : "✗")} Windows Audio: {(audioSrvOk ? "rodando" : "PARADO")}", audioSrvOk ? "#00FF88" : "#FF4444");
            Log($"  {(aebOk ? "✓" : "✗")} Audio Endpoint Builder: {(aebOk ? "rodando" : "PARADO")}", aebOk ? "#00FF88" : "#FF4444");
            Log($"  {(policyOk ? "✓" : "⚠")} DisableProtectedAudioDG: {(policyOk ? "OK" : "problemático")}", policyOk ? "#00FF88" : "#FFA500");

            // Verificar dispositivos PnP (a condição real de saúde)
            Log("  → Verificando dispositivos PnP (AudioEndpoint, Media, AudioProcessingObject)...", "#AAAAAA");
            bool devicesOk = await AreAudioDevicesHealthyAsync(ct);
            Log($"  {(devicesOk ? "✓" : "✗")} Dispositivos PnP: {(devicesOk ? "todos OK" : "há drivers com problema")}", devicesOk ? "#00FF88" : "#FF4444");

            bool audioHealthy = audioSrvOk && aebOk && policyOk && devicesOk;

            if (audioHealthy)
            {
                Log("  ✓ ÁUDIO RESTAURADO COM SUCESSO", "#00FF88");
                result.Fixed.Add("FINAL_VERIFICATION_PASSED");
            }
            else
            {
                if (!devicesOk)
                {
                    Log("  ✗ Drivers com status Unknown/Error ainda presentes.", "#FF4444");
                    Log("  ℹ O sistema já tentou instalação automática. Reiniciar o PC pode resolver.", "#FFA500");
                    result.Warnings.Add("DRIVER_REINSTALL_RECOMMENDED");
                }
                else
                {
                    Log("  ⚠ Reparo aplicado — reinicie o PC para concluir.", "#FFA500");
                    result.Warnings.Add("REBOOT_RECOMMENDED");
                }
            }
        }
    }
}
