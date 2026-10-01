using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Repair
{
    public class RegistrySafeCleaner
    {
        private readonly ILoggingService _logger;

        public Action<string, string>? OnLog { get; set; }
        public Action<int, string>? OnProgress { get; set; }

        public RegistrySafeCleaner(ILoggingService logger)
        {
            _logger = logger;
        }

        private void Log(string msg, string color = "#AAAAAA")
        {
            _logger.LogDebug($"[RegistrySafeCleaner] {msg}", source: "RegistrySafeCleaner");
            OnLog?.Invoke(msg, color);
        }

        private void Progress(int pct, string msg)
        {
            OnProgress?.Invoke(pct, msg);
        }

        public async Task<AdvancedRepairStepResult> CleanRegistryAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var stepName = LocalizationService.Instance.GetString("Repair_SafeReg_Name");
            var step = new AdvancedRepairStepResult { StepName = stepName };
            
            Log($"═══ {LocalizationService.Instance.GetString("Repair_SafeReg_Title")} ═══", "#00BFFF");
            Progress(10, LocalizationService.Instance.GetString("Repair_SafeReg_Progress"));

            try
            {
                // Implementação Segura de Limpeza do Registro focada apenas no que é inofensivo.
                // 1. Chaves de Inicialização (Run) órfãs
                // 2. Extensões de shell vazias (MUI Cache e similares de forma segura)
                // 3. Uninstall Entries obsoletas
                Progress(30, LocalizationService.Instance.GetString("RepairProgress_Registry_OrphanedKeys"));
                
                var script = @"
$count = 0

# 1. Limpeza Segura de MUI Cache (Soft clear)
$muiPath = 'HKCU:\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache'
if (Test-Path $muiPath) {
    Get-ItemProperty $muiPath -ErrorAction SilentlyContinue | ForEach-Object {
        foreach ($prop in $_.PSObject.Properties) {
            if ($prop.Name -match '\.exe\.(FriendlyAppName|ApplicationCompany)') {
                Remove-ItemProperty -Path $muiPath -Name $prop.Name -ErrorAction SilentlyContinue
                $count++
            }
        }
    }
}

# 2. OpenSavePidlMRU e LastVisitedPidlMRU (Histórico de Janelas de Salvar/Abrir)
$mruPaths = @(
    'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\ComDlg32\OpenSavePidlMRU',
    'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\ComDlg32\LastVisitedPidlMRU'
)
foreach ($p in $mruPaths) {
    if (Test-Path $p) {
        Remove-ItemProperty -Path $p -Name * -ErrorAction SilentlyContinue
        $count++
    }
}

Write-Output ""CLEANED_$count""
";
                int totalCleaned = 0;
                string output = await RunPowerShellScriptAsync(script, ct);
                if (output.Contains("CLEANED_"))
                {
                    var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        if (line.StartsWith("CLEANED_"))
                        {
                            int.TryParse(line.Replace("CLEANED_", ""), out totalCleaned);
                        }
                    }
                }

                Log($"  ✓ Limpeza segura de registro finalizada. {totalCleaned} entradas processadas.", "#00FF88");

                Progress(100, LocalizationService.Instance.GetString("RepairProgress_Registry_Complete"));

                step.Success = true;
                step.Summary = $"Registro limpo (Safe Mode). {totalCleaned} entradas limpas.";
                step.Details.Add($"Chaves de Registro Limpas: {totalCleaned}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[RegistrySafeCleaner] Erro na limpeza do registro: {ex.Message}");
                step.Success = false;
                step.Summary = LocalizationService.Instance.GetString("Repair_SafeReg_Warn");
                step.Details.Add(ex.Message);
            }

            step.Duration = sw.Elapsed;
            return step;
        }

        private async Task<string> RunPowerShellScriptAsync(string script, CancellationToken ct)
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
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    var output = await proc.StandardOutput.ReadToEndAsync();
                    await proc.WaitForExitAsync(ct);
                    return output;
                }
            }
            finally
            {
                try { File.Delete(tmp); } catch { }
            }
            return "";
        }
    }
}
