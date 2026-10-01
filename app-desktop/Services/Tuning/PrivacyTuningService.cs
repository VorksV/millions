using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Tuning
{
    public class PrivacyTuningService : IPrivacyTuningService
    {
        private readonly ILoggingService _logger;
        private const string RegistryBaseKey = @"SOFTWARE\Voltris\Optimizations";

        public PrivacyTuningService(ILoggingService logger)
        {
            _logger = logger;
        }

        public async Task<bool> ApplyTweakAsync(string tag, bool enable)
        {
            try
            {
                _logger.LogInfo($"Aplicando tweak de privacidade: {tag} ({enable})");
                bool success = false;

                switch (tag)
                {
                    case "Telemetry":
                        if (enable) await DisableTelemetryAsync();
                        else await EnableTelemetryAsync();
                        success = true;
                        break;
                    case "Location":
                        if (enable)
                        {
                            await RunCommandAsync("REG ADD HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\CapabilityAccessManager\\ConsentStore\\location /v Value /t REG_SZ /d Deny /f");
                            await RunCommandAsync("REG ADD HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\LocationAndSensors /v DisableLocation /t REG_DWORD /d 1 /f");
                            await RunCommandAsync("REG ADD HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\LocationAndSensors /v DisableWindowsLocationProvider /t REG_DWORD /d 1 /f");
                        }
                        else
                        {
                            await RunCommandAsync("REG ADD HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\CapabilityAccessManager\\ConsentStore\\location /v Value /t REG_SZ /d Allow /f");
                            await RunCommandAsync("REG ADD HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\LocationAndSensors /v DisableLocation /t REG_DWORD /d 0 /f");
                            await RunCommandAsync("REG ADD HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\LocationAndSensors /v DisableWindowsLocationProvider /t REG_DWORD /d 0 /f");
                        }
                        success = true;
                        break;
                    case "AdvertisingID":
                        if (enable)
                        {
                            await RunCommandAsync("REG ADD HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\AdvertisingInfo /v Enabled /t REG_DWORD /d 0 /f");
                            await RunCommandAsync("REG ADD HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\AdvertisingInfo /v DisabledByGroupPolicy /t REG_DWORD /d 1 /f");
                        }
                        else
                        {
                            await RunCommandAsync("REG ADD HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\AdvertisingInfo /v Enabled /t REG_DWORD /d 1 /f");
                            await RunCommandAsync("REG ADD HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\AdvertisingInfo /v DisabledByGroupPolicy /t REG_DWORD /d 0 /f");
                        }
                        success = true;
                        break;
                    case "ActivityFeed":
                        if (enable) await RunCommandAsync("REG ADD HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\System /v EnableActivityFeed /t REG_DWORD /d 0 /f");
                        else await RunCommandAsync("REG ADD HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\System /v EnableActivityFeed /t REG_DWORD /d 1 /f");
                        success = true;
                        break;
                    case "Cortana":
                        if (enable) await RunCommandAsync("REG ADD HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\Windows Search /v AllowCortana /t REG_DWORD /d 0 /f");
                        else await RunCommandAsync("reg delete \"HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\Windows Search\" /v AllowCortana /f");
                        success = true;
                        break;
                    case "OneDrive":
                        if (enable) await RunCommandAsync("REG ADD HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\OneDrive /v DisableFileSyncNGSC /t REG_DWORD /d 1 /f");
                        else await RunCommandAsync("reg delete HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\OneDrive /v DisableFileSyncNGSC /f");
                        success = true;
                        break;
                    case "Recall":
                        if (enable) await DisableRecallAsync();
                        else await EnableRecallAsync();
                        success = true;
                        break;
                    case "CoPilot":
                        if (enable) await RunCommandAsync("reg add \"HKCU\\Software\\Policies\\Microsoft\\Windows\\Explorer\" /v DisableContextualSearch /t REG_DWORD /d 1 /f");
                        else await RunCommandAsync("reg delete \"HKCU\\Software\\Policies\\Microsoft\\Windows\\Explorer\" /v DisableContextualSearch /f");
                        success = true;
                        break;
                    case "BluetoothAdvertising":
                        if (enable) await RunCommandAsync("REG ADD HKLM\\SOFTWARE\\Microsoft\\PolicyManager\\current\\device\\Bluetooth /v AllowAdvertising /t REG_DWORD /d 0 /f");
                        else await RunCommandAsync("REG ADD HKLM\\SOFTWARE\\Microsoft\\PolicyManager\\current\\device\\Bluetooth /v AllowAdvertising /t REG_DWORD /d 1 /f");
                        success = true;
                        break;
                    case "HandwritingDataSharing":
                        if (enable) await RunCommandAsync("REG ADD HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\TabletPC /v PreventHandwritingDataSharing /t REG_DWORD /d 1 /f");
                        else await RunCommandAsync("REG ADD HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\TabletPC /v PreventHandwritingDataSharing /t REG_DWORD /d 0 /f");
                        success = true;
                        break;
                    case "TextInputDataCollection":
                        if (enable) await RunCommandAsync("REG ADD HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\TextInput /v AllowLinguisticDataCollection /t REG_DWORD /d 0 /f");
                        else await RunCommandAsync("REG ADD HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\TextInput /v AllowLinguisticDataCollection /t REG_DWORD /d 1 /f");
                        success = true;
                        break;
                    case "InputPersonalization":
                        if (enable) await RunCommandAsync("REG ADD HKLM\\SOFTWARE\\Policies\\Microsoft\\InputPersonalization /v AllowInputPersonalization /t REG_DWORD /d 0 /f");
                        else await RunCommandAsync("REG ADD HKLM\\SOFTWARE\\Policies\\Microsoft\\InputPersonalization /v AllowInputPersonalization /t REG_DWORD /d 1 /f");
                        success = true;
                        break;
                    case "ActivityUploads":
                        if (enable) await RunCommandAsync("REG ADD HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\System /v UploadUserActivities /t REG_DWORD /d 0 /f");
                        else await RunCommandAsync("REG ADD HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\System /v UploadUserActivities /t REG_DWORD /d 1 /f");
                        success = true;
                        break;
                    case "ClipboardSync":
                        if (enable) await RunCommandAsync("REG ADD HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\System /v AllowCrossDeviceClipboard /t REG_DWORD /d 0 /f");
                        else await RunCommandAsync("REG ADD HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\System /v AllowCrossDeviceClipboard /t REG_DWORD /d 1 /f");
                        success = true;
                        break;
                    case "DiagnosticsToast":
                        if (enable)
                        {
                            await RunCommandAsync("REG ADD HKCU\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Diagnostics\\DiagTrack /v ShowedToastAtLevel /t REG_DWORD /d 1 /f");
                            await RunCommandAsync("REG ADD HKEY_USERS\\.DEFAULT\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Diagnostics\\DiagTrack /v ShowedToastAtLevel /t REG_DWORD /d 1 /f");
                        }
                        else
                        {
                            await RunCommandAsync("REG ADD HKCU\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Diagnostics\\DiagTrack /v ShowedToastAtLevel /t REG_DWORD /d 0 /f");
                            await RunCommandAsync("REG ADD HKEY_USERS\\.DEFAULT\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Diagnostics\\DiagTrack /v ShowedToastAtLevel /t REG_DWORD /d 0 /f");
                        }
                        success = true;
                        break;
                    case "OnlineSpeechPrivacy":
                        if (enable) await RunCommandAsync("REG ADD HKCU\\Software\\Microsoft\\Speech_OneCore\\Settings\\OnlineSpeechPrivacy /v HasAccepted /t REG_DWORD /d 0 /f");
                        else await RunCommandAsync("REG ADD HKCU\\Software\\Microsoft\\Speech_OneCore\\Settings\\OnlineSpeechPrivacy /v HasAccepted /t REG_DWORD /d 1 /f");
                        success = true;
                        break;
                    case "EdgeTelemetry":
                        if (enable) {
                            await RunCommandAsync("reg add \"HKLM\\SOFTWARE\\Policies\\Microsoft\\Edge\" /v MetricsReportingEnabled /t REG_DWORD /d 0 /f");
                            await RunCommandAsync("reg add \"HKLM\\SOFTWARE\\Policies\\Microsoft\\Edge\" /v PersonalizationReportingEnabled /t REG_DWORD /d 0 /f");
                        } else {
                            await RunCommandAsync("reg delete \"HKLM\\SOFTWARE\\Policies\\Microsoft\\Edge\" /v MetricsReportingEnabled /f");
                            await RunCommandAsync("reg delete \"HKLM\\SOFTWARE\\Policies\\Microsoft\\Edge\" /v PersonalizationReportingEnabled /f");
                        }
                        success = true;
                        break;
                    case "NvidiaTelemetry":
                        if (enable) {
                            await RunCommandAsync("reg add \"HKLM\\SOFTWARE\\NVIDIA Corporation\\NvControlPanel2\\Client\" /v OptInStatus /t REG_DWORD /d 0 /f");
                            await RunCommandAsync("sc stop \"NvTelemetryContainer\"");
                            await RunCommandAsync("sc config \"NvTelemetryContainer\" start= disabled");
                        } else {
                            await RunCommandAsync("reg add \"HKLM\\SOFTWARE\\NVIDIA Corporation\\NvControlPanel2\\Client\" /v OptInStatus /t REG_DWORD /d 1 /f");
                            await RunCommandAsync("sc config \"NvTelemetryContainer\" start= auto");
                        }
                        success = true;
                        break;
                    case "ChromeTelemetry":
                        if (enable) await RunCommandAsync("reg add \"HKLM\\SOFTWARE\\Policies\\Google\\Chrome\" /v MetricsReportingEnabled /t REG_DWORD /d 0 /f");
                        else await RunCommandAsync("reg delete \"HKLM\\SOFTWARE\\Policies\\Google\\Chrome\" /v MetricsReportingEnabled /f");
                        success = true;
                        break;
                    case "FirefoxTelemetry":
                        if (enable) {
                            await RunCommandAsync("reg add \"HKLM\\SOFTWARE\\Policies\\Mozilla\\Firefox\" /v DisableTelemetry /t REG_DWORD /d 1 /f");
                            await RunCommandAsync("reg add \"HKLM\\SOFTWARE\\Policies\\Mozilla\\Firefox\" /v DisableFirefoxStudies /t REG_DWORD /d 1 /f");
                        } else {
                            await RunCommandAsync("reg delete \"HKLM\\SOFTWARE\\Policies\\Mozilla\\Firefox\" /v DisableTelemetry /f");
                            await RunCommandAsync("reg delete \"HKLM\\SOFTWARE\\Policies\\Mozilla\\Firefox\" /v DisableFirefoxStudies /f");
                        }
                        success = true;
                        break;
                    case "WindowsAI":
                        if (enable) 
                        {
                            _logger.LogInfo("Bloqueando IA em todo o sistema (Notepad, Paint, Search, GameBar)...");
                            await RunCommandAsync("reg add \"HKCU\\Software\\Policies\\Microsoft\\Windows\\WindowsAI\" /v DisableAIDataCollection /t REG_DWORD /d 1 /f");
                            await RunCommandAsync("reg add \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\GameBar\" /v UseGamingCopilot /t REG_DWORD /d 0 /f");
                            await RunCommandAsync("reg add \"HKLM\\SOFTWARE\\Policies\\WindowsNotepad\" /v DisableAIFeatures /T REG_DWORD /D 1 /F");
                            await RunCommandAsync("reg add \"HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\Paint\" /V DisableImageCreator /T REG_DWORD /D 1 /F");
                            await RunCommandAsync("reg add \"HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\Windows Search\" /V EnableDynamicContentInSearchBox /T REG_DWORD /D 0 /F");
                            await RunCommandAsync("reg add \"HKCU\\Software\\Policies\\Microsoft\\Windows\\Explorer\" /V DisableSearchBoxSuggestions /T REG_DWORD /D 1 /F");
                        }
                        else 
                        {
                            await RunCommandAsync("reg delete \"HKCU\\Software\\Policies\\Microsoft\\Windows\\WindowsAI\" /v DisableAIDataCollection /f");
                            await RunCommandAsync("reg delete \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\GameBar\" /v UseGamingCopilot /f");
                            await RunCommandAsync("reg delete \"HKLM\\SOFTWARE\\Policies\\WindowsNotepad\" /v DisableAIFeatures /f");
                            await RunCommandAsync("reg delete \"HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\Paint\" /v DisableImageCreator /f");
                            await RunCommandAsync("reg delete \"HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\Windows Search\" /v EnableDynamicContentInSearchBox /f");
                            await RunCommandAsync("reg delete \"HKCU\\Software\\Policies\\Microsoft\\Windows\\Explorer\" /v DisableSearchBoxSuggestions /f");
                        }
                        success = true;
                        break;
                }

                if (success)
                {
                    SaveTweakState(tag, enable);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erro ao aplicar tweak de privacidade {tag}", ex);
            }
            return false;
        }

        public Task<bool> GetTweakStateAsync(string tag)
        {
            try
            {
                return Task.FromResult(tag switch
                {
                    "Telemetry" => ReadDword(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry") == 0,
                    "Location" => ReadRegString(@"HKCU\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location", "Value") == "Deny",
                    "AdvertisingID" => ReadDword(@"HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled") == 0,
                    "ActivityFeed" => ReadDword(@"SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed") == 0,
                    "Cortana" => ReadDword(@"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana") == 0,
                    "OneDrive" => ReadDword(@"SOFTWARE\Policies\Microsoft\Windows\OneDrive", "DisableFileSyncNGSC") == 1,
                    "Recall" => ReadDword(@"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis") == 1,
                    "CoPilot" => ReadDword(@"HKCU\Software\Policies\Microsoft\Windows\Explorer", "DisableContextualSearch") == 1,
                    "BluetoothAdvertising" => ReadDword(@"SOFTWARE\Microsoft\PolicyManager\current\device\Bluetooth", "AllowAdvertising") == 0,
                    "HandwritingDataSharing" => ReadDword(@"SOFTWARE\Policies\Microsoft\Windows\TabletPC", "PreventHandwritingDataSharing") == 1,
                    "TextInputDataCollection" => ReadDword(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\TextInput", "AllowLinguisticDataCollection") == 0,
                    "InputPersonalization" => ReadDword(@"SOFTWARE\Policies\Microsoft\InputPersonalization", "AllowInputPersonalization") == 0,
                    "ActivityUploads" => ReadDword(@"SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities") == 0,
                    "ClipboardSync" => ReadDword(@"SOFTWARE\Policies\Microsoft\Windows\System", "AllowCrossDeviceClipboard") == 0,
                    "DiagnosticsToast" => ReadDword(@"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Diagnostics\DiagTrack", "ShowedToastAtLevel") == 1,
                    "OnlineSpeechPrivacy" => ReadDword(@"HKCU\Software\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy", "HasAccepted") == 0,
                    "EdgeTelemetry" => ReadDword(@"SOFTWARE\Policies\Microsoft\Edge", "MetricsReportingEnabled") == 0,
                    "NvidiaTelemetry" => ReadDword(@"SOFTWARE\NVIDIA Corporation\NvControlPanel2\Client", "OptInStatus") == 0,
                    "ChromeTelemetry" => ReadDword(@"SOFTWARE\Policies\Google\Chrome", "MetricsReportingEnabled") == 0,
                    "FirefoxTelemetry" => ReadDword(@"SOFTWARE\Policies\Mozilla\Firefox", "DisableTelemetry") == 1,
                    "WindowsAI" => ReadDword(@"HKCU\Software\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataCollection") == 1,
                    _ => false
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[GetTweakState] Erro ao ler estado real de '{tag}': {ex.Message}");
                return Task.FromResult(false);
            }
        }

        private static int? ReadDword(string regPath, string valName)
        {
            try
            {
                if (regPath.StartsWith("HKCU", StringComparison.OrdinalIgnoreCase))
                {
                    var actualPath = regPath.Substring(5);
                    using var cuKey = Registry.CurrentUser.OpenSubKey(actualPath);
                    if (cuKey?.GetValue(valName) is int cuVal) return cuVal;
                    return null;
                }
                using var lmKey = Registry.LocalMachine.OpenSubKey(regPath);
                if (lmKey?.GetValue(valName) is int lmVal) return lmVal;
                return null;
            }
            catch { return null; }
        }

        private static string? ReadRegString(string regPath, string valName)
        {
            try
            {
                if (regPath.StartsWith("HKCU", StringComparison.OrdinalIgnoreCase))
                {
                    var actualPath = regPath.Substring(5);
                    using var cuKey = Registry.CurrentUser.OpenSubKey(actualPath);
                    return cuKey?.GetValue(valName) as string;
                }
                using var lmKey = Registry.LocalMachine.OpenSubKey(regPath);
                return lmKey?.GetValue(valName) as string;
            }
            catch { return null; }
        }

        private void SaveTweakState(string tag, bool enable)
        {
            using var key = Registry.LocalMachine.CreateSubKey(RegistryBaseKey);
            key.SetValue(tag, enable ? 1 : 0, RegistryValueKind.DWord);
        }

        private async Task DisableTelemetryAsync()
        {
            _logger.LogInfo("Bloqueando Telemetria (Enterprise Mode)...");
            
            // Serviços de Telemetria
            string[] services = { "DiagTrack", "diagnosticshub.standardcollector.service", "dmwappushservice", "DcpSvc", "WdiServiceHost", "WdiSystemHost" };
            foreach (var svc in services)
            {
                await RunCommandAsync($"sc stop {svc}");
                await RunCommandAsync($"sc config {svc} start= disabled");
                await RunCommandAsync($"REG ADD HKLM\\SYSTEM\\CurrentControlSet\\Services\\{svc} /v Start /t REG_DWORD /d 4 /f");
            }

            // Chaves de Registro de Telemetria
            await RunCommandAsync("REG ADD HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection /v AllowTelemetry /t REG_DWORD /d 0 /f");
            await RunCommandAsync("REG ADD HKLM\\SOFTWARE\\Policies\\Microsoft\\SQMClient\\Windows /v CEIPEnable /t REG_DWORD /d 0 /f");
            await RunCommandAsync("REG ADD HKCU\\Software\\Microsoft\\Siuf\\Rules /v NumberOfSIUFInPeriod /t REG_DWORD /d 0 /f");
            
            // Tarefas Agendadas de Telemetria
            string[] tasks = {
                "Microsoft\\Windows\\Application Experience\\Microsoft Compatibility Appraiser",
                "Microsoft\\Windows\\Application Experience\\ProgramDataUpdater",
                "Microsoft\\Windows\\Autochk\\Proxy",
                "Microsoft\\Windows\\Customer Experience Improvement Program\\Consolidator",
                "Microsoft\\Windows\\Customer Experience Improvement Program\\UsbCeip"
            };
            foreach (var task in tasks)
            {
                await RunCommandAsync($"schtasks /Change /TN \"{task}\" /Disable");
            }

            // NOTA: Bloqueio via hosts removido intencionalmente.
            // O Windows Defender detecta modificações no hosts como
            // SettingsModifier:Win32/PossibleHostsFileHijack, gerando alertas
            // de segurança falsos para o usuário. A telemetria já é bloqueada
            // pelas chaves de registro e desativação de serviços acima.
        }

        private async Task EnableTelemetryAsync()
        {
            _logger.LogInfo("Restaurando Telemetria padrão...");
            
            string[] services = { "DiagTrack", "diagnosticshub.standardcollector.service", "dmwappushservice", "DcpSvc", "WdiServiceHost", "WdiSystemHost" };
            foreach (var svc in services)
            {
                await RunCommandAsync($"sc config {svc} start= auto");
                await RunCommandAsync($"sc start {svc}");
            }

            await RunCommandAsync("reg delete \"HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection\" /v AllowTelemetry /f");
            await RunCommandAsync("reg delete \"HKLM\\SOFTWARE\\Policies\\Microsoft\\SQMClient\\Windows\" /v CEIPEnable /f");
            
            // Re-habilitar Tarefas
            string[] tasks = {
                "Microsoft\\Windows\\Application Experience\\Microsoft Compatibility Appraiser",
                "Microsoft\\Windows\\Application Experience\\ProgramDataUpdater",
                "Microsoft\\Windows\\Autochk\\Proxy",
                "Microsoft\\Windows\\Customer Experience Improvement Program\\Consolidator",
                "Microsoft\\Windows\\Customer Experience Improvement Program\\UsbCeip"
            };
            foreach (var task in tasks)
            {
                await RunCommandAsync($"schtasks /Change /TN \"{task}\" /Enable");
            }
            
            // Limpar entradas de hosts que versões anteriores possam ter adicionado
            var hostsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");
            try
            {
                if (File.Exists(hostsPath))
                {
                    var lines = File.ReadAllLines(hostsPath).ToList();
                    int before = lines.Count;
                    lines.RemoveAll(line =>
                        !line.TrimStart().StartsWith("#") && (
                        line.Contains("telemetry.microsoft.com") ||
                        line.Contains("vortex-win") ||
                        line.Contains("watson.telemetry") ||
                        line.Contains("choice.microsoft.com") ||
                        line.Contains("settings-win.data.microsoft.com") ||
                        line.Contains("oca.telemetry") ||
                        line.Contains("sqm.telemetry") ||
                        line.Contains("diagnostics.support.microsoft.com") ||
                        line.Contains("ceuswatcab01.blob.core.windows.net")));
                    if (lines.Count != before)
                    {
                        File.WriteAllLines(hostsPath, lines);
                        _logger.LogInfo("[Privacy] Entradas de telemetria removidas do hosts.");
                    }
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[Privacy] Erro ao limpar hosts: {ex.Message}"); }
        }

        private async Task DisableRecallAsync()
        {
            _logger.LogInfo("Desativando Windows Recall (DISM + Registry)...");
            await RunCommandAsync("REG ADD HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsAI /v DisableAIDataAnalysis /t REG_DWORD /d 1 /f");
            await RunCommandAsync("REG ADD HKCU\\Software\\Policies\\Microsoft\\Windows\\WindowsAI /v DisableAIDataAnalysis /t REG_DWORD /d 1 /f");
            await RunCommandAsync("REG ADD HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsAI /v TurnOffSavingSnapshots /t REG_DWORD /d 1 /f");
            
            // Services & Tasks
            await RunPowerShellAsync("Stop-Service -Name 'WSAIFabricSvc' -ErrorAction SilentlyContinue; Set-Service -Name 'WSAIFabricSvc' -StartupType Disabled");

            var taskDisable = @"
                $tasks = @('\Microsoft\Windows\WindowsAI\Recall\InitialConfiguration', '\Microsoft\Windows\WindowsAI\Recall\PolicyConfiguration');
                foreach($t in $tasks) { 
                    if (Get-ScheduledTask -TaskName $t -ErrorAction SilentlyContinue) {
                        Disable-ScheduledTask -TaskName $t -ErrorAction SilentlyContinue 
                    }
                }
            ";
            await RunPowerShellAsync($"{taskDisable}");
            
            // DISM Feature & Appx
            await RunCommandAsync("dism /Online /Disable-Feature /FeatureName:Recall /Remove /NoRestart /Quiet");
            await RunPowerShellAsync("Get-AppxPackage -AllUsers *AiFabric* | Remove-AppxPackage -AllUsers -ErrorAction SilentlyContinue");
            await RunPowerShellAsync("Get-AppxPackage -AllUsers *WindowsIntelligence* | Remove-AppxPackage -AllUsers -ErrorAction SilentlyContinue");
        }

        private async Task EnableRecallAsync()
        {
            _logger.LogInfo("Restaurando Windows Recall...");
            await RunCommandAsync("REG DELETE HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsAI /v DisableAIDataAnalysis /F");
            await RunCommandAsync("REG DELETE HKCU\\Software\\Policies\\Microsoft\\Windows\\WindowsAI /v DisableAIDataAnalysis /F");
            await RunCommandAsync("REG DELETE HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsAI /v TurnOffSavingSnapshots /F");

            await RunPowerShellAsync("Set-Service -Name 'WSAIFabricSvc' -StartupType Manual");

            var taskEnable = @"
                $tasks = @('\Microsoft\Windows\WindowsAI\Recall\InitialConfiguration', '\Microsoft\Windows\WindowsAI\Recall\PolicyConfiguration');
                foreach($t in $tasks) { 
                    if (Get-ScheduledTask -TaskName $t -ErrorAction SilentlyContinue) {
                        Enable-ScheduledTask -TaskName $t -ErrorAction SilentlyContinue 
                    }
                }
            ";
            await RunPowerShellAsync($"{taskEnable}");

            await RunCommandAsync("dism /Online /Enable-Feature /FeatureName:Recall /NoRestart");

            var psScript = "Get-AppxPackage -allusers *AiFabric* | foreach {Add-AppxPackage -register \"$($_.InstallLocation)\\appxmanifest.xml\" -DisableDevelopmentMode}";
            await RunPowerShellAsync($"{psScript}");
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

        private async Task RunPowerShellAsync(string script)
        {
            try
            {
                var args = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script}\"";
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = args,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };

                process.Start();
                await process.WaitForExitAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erro ao executar PowerShell: {ex.Message}", ex);
            }
        }
    }
}
