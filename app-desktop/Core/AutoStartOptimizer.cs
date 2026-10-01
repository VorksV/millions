using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Core.Constants;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Utils.Win32;
using VoltrisOptimizer.Services.Power;

namespace VoltrisOptimizer.Core;

public sealed class AutoStartOptimizer : IAutoStartService
{
    private readonly ILoggingService _logger;

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    public AutoStartOptimizer(ILoggingService logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInfo("[AutoStartOptimizer] Iniciado.");

        try
        {
            _logger.LogInfo("[AutoStartOptimizer] Aguardando dados do SystemMetricsCache...");
            await WaitForMetricsAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInfo("[AutoStartOptimizer] SystemMetricsCache pronto.");

            var cpu = SystemMetricsCache.Instance.CpuPercent;
            var ram = SystemMetricsCache.Instance.MemoryUsedPercent;
            var thermal = SystemMetricsCache.Instance.CpuTemperature;
            _logger.LogInfo($"[AutoStartOptimizer] Estado atual: CPU={cpu:F1}% RAM={ram:F1}% Thermal={thermal:F1}°C");

            var actions = new List<string>();

            if (ram > 70)
            {
                _logger.LogInfo($"[AutoStartOptimizer] RAM {ram:F1}% > 70% — reduzindo working sets de processos não críticos.");
                var trimmed = TrimNonCriticalWorkingSets();
                _logger.LogSuccess($"[AutoStartOptimizer] Working set reduzido em {trimmed} processos.");
                actions.Add($"TrimWS:{trimmed}");
            }

            if (thermal > 80)
            {
                _logger.LogWarning($"[AutoStartOptimizer] Temperatura {thermal:F1}°C > 80°C — alternando para plano Balanced.");
                if (SetActivePowerPlan(SystemConstants.PowerPlans.Balanced))
                    actions.Add("PowerPlan:Balanced");
            }
            else if (thermal > 0 && thermal < 70)
            {
                _logger.LogInfo($"[AutoStartOptimizer] Temperatura {thermal:F1}°C < 70°C — alternando para High Performance.");
                if (SetActivePowerPlan(SystemConstants.PowerPlans.HighPerformance))
                    actions.Add("PowerPlan:HighPerformance");
            }

            var cleaned = await CleanTempFilesIfOverThresholdAsync(cancellationToken).ConfigureAwait(false);
            if (cleaned > 0)
                actions.Add($"TempCleaned:{cleaned}MB");

            sw.Stop();
            var summary = actions.Count > 0 ? string.Join(", ", actions) : "nenhuma ação necessária";
            _logger.LogSuccess($"[AutoStartOptimizer] Concluído em {sw.ElapsedMilliseconds}ms. Ações executadas: {summary}");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning($"[AutoStartOptimizer] Cancelado após {sw.ElapsedMilliseconds}ms.");
        }
        catch (Exception ex)
        {
            _logger.LogError($"[AutoStartOptimizer] Falha na execução: {ex.Message}", ex);
        }
    }

    private async Task WaitForMetricsAsync(CancellationToken ct)
    {
        // CORREÇÃO 5: Timeout aumentado de 10s para 15s para evitar abortos prematuros
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!ct.IsCancellationRequested && DateTime.UtcNow < deadline)
        {
            if (SystemMetricsCache.Instance.CpuPercent > 0)
                return;
            await Task.Delay(100, ct).ConfigureAwait(false);
        }
        if (SystemMetricsCache.Instance.CpuPercent <= 0)
            _logger.LogWarning("[AutoStartOptimizer] Timeout de 15s ao aguardar métricas — executando sem cache.");
    }

    private int TrimNonCriticalWorkingSets()
    {
        var protectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var list in new[] {
            SystemConstants.ProtectedProcesses.System,
            SystemConstants.ProtectedProcesses.Voltris,
            SystemConstants.ProtectedProcesses.Development
        })
        {
            foreach (var name in list) protectedNames.Add(name);
        }

        int count = 0;
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (protectedNames.Contains(proc.ProcessName)) continue;
                if (proc.HasExited) continue;
                EmptyWorkingSet(proc.Handle);
                count++;
            }
            catch (Win32Exception) { }
            catch (InvalidOperationException) { }
        }
        return count;
    }

    private bool SetActivePowerPlan(string guid)
    {
        try
        {
            // [FIX:UNICO-DONO-DE-ENERGIA] Este metodo recebia um GUID de plano e
            // devolvia sucesso depois de "aplicar". O plano nao e mais escolhido
            // aqui: o Perfil Inteligente e' o dono, e o unico pedido legitimo que
            // um inicializador tem e' "aplique o perfil".
            //
            // Manter o metodo (e o seu contrato) porque quem chama continua
            // precisando de um ponto de entrada, mas o que ele FAZ mudou de
            // natureza. O log antigo dizia "plano alterado para {guid}", o que
            // seria mentira: o GUID recebido e' ignorado de proposito.
            bool ok = ProfilePowerAuthority.RequestProfileApply(
                "AutoStartOptimizer", "inicializacao do app", _logger);
            _logger.LogInfo(
                "[AutoStartOptimizer] Perfil Inteligente reaplicado (pedido de inicializacao). " +
                "O GUID recebido foi ignorado: quem escolhe o plano e' o Perfil.");
            return ok;
        }
        catch (Exception ex)
        {
            _logger.LogError($"[AutoStartOptimizer] Erro ao alterar plano de energia: {ex.Message}", ex);
            return false;
        }
    }

    private async Task DisableBackgroundFeaturesAsync(CancellationToken ct)
    {
        try
        {
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\Windows Search", writable: true))
            {
                if (key != null)
                {
                    key.SetValue("AllowCortana", 0, RegistryValueKind.DWord);
                    key.SetValue("AllowSearchToUseLocation", 0, RegistryValueKind.DWord);
                    _logger.LogInfo("[AutoStartOptimizer] Cortana desabilitada.");
                }
            }
        }
        catch (UnauthorizedAccessException) { _logger.LogDebug("[AutoStartOptimizer] Sem privilégio para desabilitar Cortana."); }

        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\GameBar", writable: true))
            {
                if (key != null)
                {
                    key.SetValue("AutoGameModeEnabled", 0, RegistryValueKind.DWord);
                    key.SetValue("GameDVR_Enabled", 0, RegistryValueKind.DWord);
                    _logger.LogInfo("[AutoStartOptimizer] Xbox Game Bar desabilitada.");
                }
            }
        }
        catch (UnauthorizedAccessException) { }

        try
        {
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\GameDVR", writable: true))
            {
                if (key != null)
                {
                    key.SetValue("AllowGameDVR", 0, RegistryValueKind.DWord);
                    _logger.LogInfo("[AutoStartOptimizer] GameDVR desabilitado.");
                }
            }
        }
        catch (UnauthorizedAccessException) { }

        try
        {
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection", writable: true))
            {
                if (key != null)
                {
                    key.SetValue("AllowTelemetry", 0, RegistryValueKind.DWord);
                    key.SetValue("MaxTelemetryAllowed", 0, RegistryValueKind.DWord);
                    _logger.LogInfo("[AutoStartOptimizer] Telemetria desabilitada.");
                }
            }
        }
        catch (UnauthorizedAccessException) { }

        await Task.CompletedTask;
    }

    private async Task<long> CleanTempFilesIfOverThresholdAsync(CancellationToken ct)
    {
        long totalMb = 0;
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetTempPath(),
            Environment.GetFolderPath(Environment.SpecialFolder.InternetCache),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp")
        };

        foreach (var dir in dirs)
        {
            if (ct.IsCancellationRequested) break;
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;

            try
            {
                var sizeBytes = await Task.Run(() => ComputeDirectorySize(new DirectoryInfo(dir)), ct).ConfigureAwait(false);
                var sizeMb = sizeBytes / (1024L * 1024L);
                _logger.LogInfo($"[AutoStartOptimizer] Diretório temporário {dir}: {sizeMb}MB");

                if (sizeMb > 500)
                {
                    _logger.LogInfo($"[AutoStartOptimizer] {sizeMb}MB > 500MB — iniciando limpeza.");
                    var freed = await Task.Run(() =>
                    {
                        long freedBytes = 0;
                        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly))
                        {
                            try { var fi = new FileInfo(file); freedBytes += fi.Length; fi.Delete(); } catch { }
                        }
                        foreach (var sub in Directory.EnumerateDirectories(dir))
                        {
                            try { Directory.Delete(sub, true); } catch { }
                        }
                        return freedBytes;
                    }, ct).ConfigureAwait(false);

                    var freedMb = freed / (1024L * 1024L);
                    totalMb += freedMb;
                    _logger.LogSuccess($"[AutoStartOptimizer] Limpeza concluída: {freedMb}MB liberados.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[AutoStartOptimizer] Erro ao processar {dir}: {ex.Message}");
            }
        }

        return totalMb;
    }

    private static long ComputeDirectorySize(DirectoryInfo dir)
    {
        long size = 0;
        try
        {
            foreach (var file in dir.EnumerateFiles("*", SearchOption.TopDirectoryOnly))
            {
                try { size += file.Length; } catch { }
            }
        }
        catch { }
        return size;
    }
}
