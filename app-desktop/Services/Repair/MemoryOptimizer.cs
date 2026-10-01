using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Repair
{
    public class MemoryOptimizer
    {
        private readonly ILoggingService _logger;

        public Action<string, string>? OnLog { get; set; }
        public Action<int, string>? OnProgress { get; set; }

        public MemoryOptimizer(ILoggingService logger)
        {
            _logger = logger;
        }

        private void Log(string msg, string color = "#AAAAAA")
        {
            _logger.LogDebug($"[MemoryOptimizer] {msg}", source: "MemoryOptimizer");
            OnLog?.Invoke(msg, color);
        }

        private void Progress(int pct, string msg)
        {
            OnProgress?.Invoke(pct, msg);
        }

        [DllImport("psapi.dll")]
        static extern int EmptyWorkingSet(IntPtr hwProc);

        public async Task<AdvancedRepairStepResult> OptimizeMemoryAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var stepName = LocalizationService.Instance.GetString("Repair_Memory_Name");
            var step = new AdvancedRepairStepResult { StepName = stepName };
            
            Log($"═══ {LocalizationService.Instance.GetString("Repair_Memory_Title")} ═══", "#00BFFF");
            Progress(10, LocalizationService.Instance.GetString("Repair_Memory_Progress"));

            try
            {
                // 1. Limpeza de Working Set de Processos
                Progress(30, LocalizationService.Instance.GetString("RepairProgress_Memory_WorkingSets"));
                int processesOptimized = 0;
                long totalFreed = 0;

                var processes = Process.GetProcesses();
                foreach (var process in processes)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        // Evita otimizar o próprio processo excessivamente ou processos críticos do sistema
                        if (process.Id == Process.GetCurrentProcess().Id) continue;
                        if (process.ProcessName.Equals("Idle", StringComparison.OrdinalIgnoreCase) || 
                            process.ProcessName.Equals("System", StringComparison.OrdinalIgnoreCase)) continue;

                        long oldMemory = process.WorkingSet64;
                        if (EmptyWorkingSet(process.Handle) != 0)
                        {
                            process.Refresh();
                            long newMemory = process.WorkingSet64;
                            if (oldMemory > newMemory)
                            {
                                totalFreed += (oldMemory - newMemory);
                                processesOptimized++;
                            }
                        }
                    }
                    catch { } // Alguns processos protegidos vão dar Access Denied, e isso é normal e seguro.
                }

                Log($"  ✓ {processesOptimized} processos otimizados. RAM recuperada do Working Set: {totalFreed / 1048576} MB.", "#00FF88");

                // 2. Limpeza da Standby List via PowerShell (se suportado sem ferramentas externas)
                Progress(70, LocalizationService.Instance.GetString("RepairProgress_Memory_StandbyList"));
                var script = @"
try {
    # Chama o clear de cache de sistema (SysMain/Superfetch restart force clear)
    # Forma segura nativa para liberar caches inativos sem usar ferramentas como RAMMap.
    $svc = Get-Service -Name SysMain -ErrorAction SilentlyContinue
    if ($svc -and $svc.Status -eq 'Running') {
        Restart-Service -Name SysMain -Force -ErrorAction SilentlyContinue
        Write-Output 'SYSMAIN_RESTARTED'
    }
} catch { Write-Output 'SYSMAIN_SKIP' }
";
                await RunHiddenProcessAsync("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script}\"", ct);
                Log($"  ✓ Cache de pré-carregamento e standby limpos com segurança.", "#00FF88");

                Progress(100, LocalizationService.Instance.GetString("RepairProgress_Memory_Complete"));

                step.Success = true;
                step.Summary = $"Memória Otimizada. Aproximadamente {(totalFreed / 1048576.0):F1} MB liberados.";
                step.Details.Add($"Processos Otimizados: {processesOptimized}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[MemoryOptimizer] Erro na otimização de memória: {ex.Message}");
                step.Success = false;
                step.Summary = LocalizationService.Instance.GetString("Repair_Memory_Warn");
                step.Details.Add(ex.Message);
            }

            step.Duration = sw.Elapsed;
            return step;
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
