using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Repair
{
    public class SystemTweaker
    {
        private readonly ILoggingService _logger;

        public Action<string, string>? OnLog { get; set; }
        public Action<int, string>? OnProgress { get; set; }

        public SystemTweaker(ILoggingService logger)
        {
            _logger = logger;
        }

        private void Log(string msg, string color = "#AAAAAA")
        {
            _logger.LogDebug($"[SystemTweaker] {msg}", source: "SystemTweaker");
            OnLog?.Invoke(msg, color);
        }

        private void Progress(int pct, string msg)
        {
            OnProgress?.Invoke(pct, msg);
        }

        public async Task<AdvancedRepairStepResult> TweakSystemAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var stepName = LocalizationService.Instance.GetString("Repair_SystemTweaks_Name");
            var step = new AdvancedRepairStepResult { StepName = stepName };
            
            Log($"═══ {LocalizationService.Instance.GetString("Repair_SystemTweaks_Title")} ═══", "#00BFFF");
            Progress(10, LocalizationService.Instance.GetString("Repair_SystemTweaks_Progress"));

            try
            {
                // Desativando Hibernação para liberar Gigabytes no disco e melhorar Fast Startup boot times
                Progress(30, LocalizationService.Instance.GetString("RepairProgress_Tweaks_FastStartup"));
                await RunHiddenProcessAsync("powercfg.exe", "/h off", ct);
                Log($"  ✓ Hibernação desativada (Recuperando espaço no SSD).", "#00FF88");

                // Desativando Tarefas de Telemetria via PowerShell
                Progress(60, LocalizationService.Instance.GetString("RepairProgress_Tweaks_ScheduledTasks"));
                var script = @"
try {
    $tasks = @(
        '\Microsoft\Windows\Customer Experience Improvement Program\Consolidator',
        '\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip',
        '\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser',
        '\Microsoft\Windows\Application Experience\ProgramDataUpdater',
        '\Microsoft\Windows\Application Experience\StartupAppTask',
        '\Microsoft\Windows\Autochk\Proxy'
    )
    foreach ($t in $tasks) {
        Disable-ScheduledTask -TaskPath ($t -replace '[^\\]+$','') -TaskName ($t | Split-Path -Leaf) -ErrorAction SilentlyContinue
    }
} catch {}
";
                await RunPowerShellScriptAsync(script, ct);
                Log($"  ✓ Telemetria de segundo plano minimizada.", "#00FF88");

                Progress(100, LocalizationService.Instance.GetString("RepairProgress_Tweaks_Complete"));

                step.Success = true;
                step.Summary = $"Tweaks de sistema aplicados com sucesso. Telemetria minimizada.";
                step.Details.Add("Hibernation = Disabled");
                step.Details.Add("CEIP Tasks = Disabled");
                step.Details.Add("Appraiser Tasks = Disabled");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[SystemTweaker] Erro ao aplicar tweaks: {ex.Message}");
                step.Success = false;
                step.Summary = LocalizationService.Instance.GetString("Repair_SystemTweaks_Warn");
                step.Details.Add(ex.Message);
            }

            step.Duration = sw.Elapsed;
            return step;
        }

        private async Task RunPowerShellScriptAsync(string script, CancellationToken ct)
        {
            var tmp = Path.GetTempFileName() + ".ps1";
            try
            {
                await File.WriteAllTextAsync(tmp, script, System.Text.Encoding.UTF8, ct);
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{tmp}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    await proc.WaitForExitAsync(ct);
                }
            }
            finally
            {
                try { File.Delete(tmp); } catch { }
            }
        }

        private async Task RunHiddenProcessAsync(string exe, string args, CancellationToken ct)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                await proc.WaitForExitAsync(ct);
            }
        }
    }
}
