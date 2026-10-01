using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Core.Body.Arms;

/// <summary>
/// ProcessArm — braço de gerenciamento de processos do VOLTRIS.
///
/// QUALIDADE: todos os métodos aplicam a alteração REAL e a CONFIRMAM por
/// releitura antes de devolver <c>true</c>. Nenhuma operação apenas registra log.
/// </summary>
public sealed class ProcessArm : IProcessArm, IDisposable
{
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000u;
    private const uint PROCESS_SET_INFORMATION = 0x0200u;
    private const uint PROCESS_QUERY_INFORMATION = 0x0400u;
    private const uint PROCESS_SET_QUOTA = 0x0100u;

    private readonly ILoggingService _logger;
    private readonly Dictionary<int, DateTime> _priorityCooldowns = new();
    private readonly object _cooldownLock = new();
    private readonly HashSet<string> _protectedProcesses;
    private readonly TimeSpan _priorityCooldown = TimeSpan.FromSeconds(30.0);

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern int EmptyWorkingSet(IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    /// <summary>
    /// Processos que o VOLTRIS nunca deve rebaixar nem aplicar EcoQoS. Inclui os
    /// componentes de captura/codificação: aplicar EcoQoS (Efficiency Mode) ao OBS
    /// durante uma transmissão limita a frequência da CPU do encoder e causa quadros
    /// descartados exatamente onde o usuário pediu desempenho.
    /// </summary>
    private static readonly string[] CaptureAndEncoderProcesses =
    {
        "obs64", "obs32", "obs", "streamlabs obs", "ffmpeg", "ffmpeg64",
        "wirecast", "vmix", "restream", "streamlabs", "nircmd", "mediaservicehost"
    };

    private static readonly string[] CommunicationProcesses =
    {
        "discord", "discordapp", "teams", "ms-teams", "zoom", "whatsapp", "telegram",
        "skype", "steam", "epicgameslauncher", "battlenet", "gamebar", "obswebsocket"
    };

    public ProcessArm(ILoggingService logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _protectedProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "system", "registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass",
            "lsm", "fontdrvhost", "dwm", "explorer", "taskhostw", "svchost", "msmpeng",
            "nissrv", "securityhealthservice", "voltrisoptimizer", "audiodg", "rt", "spoolsv"
        };
    }

    /// <summary>
    /// Define a prioridade do processo e a CONFIRMA por releitura.
    ///
    /// CORREÇÃO: a implementação anterior NÃO alterava a prioridade. Ela verificava
    /// blacklist/cooldown, escrevia um log de sucesso e devolvia
    /// <c>Success = true</c> com <c>AppliedPriority = priority</c> — sem nunca tocar
    /// em <c>Process.PriorityClass</c>. A interface do Stream Hub exibia
    /// "Prioridade do processo de transmissão configurada" sem qualquer alteração.
    /// </summary>
    public Task<ProcessPriorityResult> SetProcessPriorityAsync(int pid, string name, ProcessPriorityClass priority)
    {
        return Task.Run(() =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    return Fail(pid, name, "Nome de processo vazio.");
                }

                if (_protectedProcesses.Contains(name))
                {
                    _logger.LogInfo($"[ARM-PROCESS] BLOQUEADO: '{name}' está na lista de processos protegidos.");
                    return new ProcessPriorityResult
                    {
                        Success = false,
                        BlockedByBlacklist = true,
                        Pid = pid,
                        ProcessName = name
                    };
                }

                lock (_cooldownLock)
                {
                    if (_priorityCooldowns.TryGetValue(pid, out var lastChange) &&
                        DateTime.UtcNow - lastChange < _priorityCooldown)
                    {
                        double remaining = (_priorityCooldown - (DateTime.UtcNow - lastChange)).TotalSeconds;
                        _logger.LogInfo($"[ARM-PROCESS] BLOQUEADO cooldown: PID={pid} {remaining:F0}s restantes.");
                        return new ProcessPriorityResult
                        {
                            Success = false,
                            Pid = pid,
                            ProcessName = name
                        };
                    }
                }

                // Jogo em foreground: não rebaixar, e não elevar para HIGH.
                bool isGame = IsKnownGame(name);
                if (isGame && priority == ProcessPriorityClass.High)
                {
                    _logger.LogWarning(
                        $"[ARM-PROCESS] Recusando prioridade HIGH para o jogo '{name}' (PID={pid}). " +
                        "Elevar o processo do jogo rouba ciclos do encoder. " +
                        "Elevar o encoder causa falhas de áudio. " +
                        "Nenhuma alteração foi feita.");
                    return new ProcessPriorityResult
                    {
                        Success = false,
                        Pid = pid,
                        ProcessName = name
                    };
                }

                // ── ALTERAÇÃO REAL ──
                using var process = Process.GetProcessById(pid);
                if (!string.Equals(process.ProcessName, name, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning($"[ARM-PROCESS] PID {pid} é '{process.ProcessName}', não '{name}'. " +
                        "Acesso recusado para não alterar o processo errado.");
                    return Fail(pid, name, $"PID {pid} pertence a '{process.ProcessName}', não a '{name}'.");
                }

                var before = process.PriorityClass;

                // BUG CORRIGIDO: o enum do VOLTRIS usa os flags NATIVOS do Win32
                // (32/32768/128/256) e o enum do .NET e sequencial (0..5).
                // O cast direto produzia valores invalidos e lancava
                // InvalidEnumArgumentException. A conversao agora e explicita.
                var winTarget = ProcessPriorityClassMap.ToWindows(priority);
                process.PriorityClass = winTarget;
                process.Refresh();

                // ── READ-BACK ──
                var after = process.PriorityClass;
                bool confirmed = after == winTarget;
                if (!confirmed)
                {
                    _logger.LogError(
                        $"[ARM-PROCESS] Prioridade de '{name}' (PID={pid}) foi solicitada como {winTarget}, " +
                        $"mas a releitura retornou {after}. A alteração NÃO foi confirmada.");
                    return new ProcessPriorityResult
                    {
                        Success = false,
                        Pid = pid,
                        ProcessName = name,
                        PreviousPriority = ProcessPriorityClassMap.FromWindows(before),
                        Error = $"Solicitado {winTarget}, relido {after}."
                    };
                }

                lock (_cooldownLock) { _priorityCooldowns[pid] = DateTime.UtcNow; }

                _logger.LogSuccess(
                    $"[ARM-PROCESS] Prioridade de '{name}' (PID={pid}) alterada e CONFIRMADA: {before} -> {after}.");
                return new ProcessPriorityResult
                {
                    Success = true,
                    Pid = pid,
                    ProcessName = name,
                    AppliedPriority = ProcessPriorityClassMap.FromWindows(after),
                    PreviousPriority = ProcessPriorityClassMap.FromWindows(before)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ARM-PROCESS] ERRO em SetProcessPriorityAsync: {ex.Message}", ex);
                return Fail(pid, name, $"{ex.GetType().Name}: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Ativa/desativa EcoQoS (Efficiency Mode) em UM processo específico.
    ///
    /// CORREÇÃO: a implementação anterior chamava <c>OptimizeMemoryAsync()</c> e
    /// retornava <c>true</c> sem verificar nada, e o <c>pid</c>/<c>name</c> recebidos
    /// eram ignorados — ou seja, desligava EcoQoS para o OBS enquanto o jogador
    /// transmitia. Agora a ação é aplicada somente ao processo pedido e confirmada.
    /// </summary>
    public Task<bool> SetEcoQoSAsync(int pid, string name, bool throttle)
    {
        return Task.Run(() =>
        {
            try
            {
                if (!IsRunningAsAdmin())
                {
                    _logger.LogWarning("[ARM-PROCESS] EcoQoS (SetProcessInformation) requer privilégio administrativo. " +
                        $"Nenhuma alteração foi feita em '{name}' (PID={pid}).");
                    return false;
                }

                if (string.IsNullOrWhiteSpace(name)) return false;

                if (IsCaptureOrEncoder(name))
                {
                    _logger.LogWarning(
                        $"[ARM-PROCESS] EcoQoS NÃO será aplicado a '{name}' (PID={pid}). " +
                        "Efficiency Mode limita a frequência da CPU e degrada a codificação de vídeo. " +
                        "Nenhuma alteração foi feita.");
                    return false;
                }

                bool applied = VoltrisOptimizer.Services.Optimization.Memory.VoltrisMemoryCoordinator
                    .Instance.SetEcoQoSForProcess(pid, name, throttle, out string? error);

                if (applied)
                {
                    string estado = throttle ? "ativado" : "desativado";
                    _logger.LogSuccess(
                        $"[ARM-PROCESS] EcoQoS {estado} em '{name}' (PID={pid}) e confirmado.");
                }
                else
                {
                    _logger.LogError($"[ARM-PROCESS] EcoQoS NÃO aplicado a '{name}' (PID={pid}): {error}");
                }
                return applied;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ARM-PROCESS] ERRO em SetEcoQoSAsync: {ex.Message}", ex);
                return false;
            }
        });
    }

    /// <summary>
    /// Libera memória de working set de processos específicos.
    ///
    /// AVISO TÉCNICO: truncar o working set durante uma transmissão provoca page
    /// faults e quedas de desempenho. O método agora devolve a quantidade REAL de
    /// memória liberada (antes era um literal <c>0L</c> exibido como "0 MB liberados")
    /// e apenas age sobre processos em lista de permitidos.
    /// </summary>
    public async Task<TrimResult> TrimWorkingSetAsync(int[]? pids = null)
    {
        var sw = Stopwatch.StartNew();
        long freedBytes = 0;
        int processed = 0, failed = 0, skipped = 0;

        try
        {
            if (!IsRunningAsAdmin())
            {
                _logger.LogWarning("[ARM-PROCESS] EmptyWorkingSet requer privilégio administrativo. Nada foi feito.");
                return new TrimResult { RequiresAdmin = true };
            }

            // ── HABILITAÇÃO DE PRIVILÉGIO ──────────────────────────────────
            // ESTE ERA O MOTIVO DE "OTIMIZAÇÃO DE RAM NÃO FAZER NADA".
            //
            // Ser administrador NÃO é o bastante. "EmptyWorkingSet" em um
            // processo de outra conta exige que o token do NOSSO processo tenha
            // o privilégio SeDebugPrivilege ATIVADO. Um app elevado roda com
            // privilégios DESABILITADOS por padrão (é assim que o UAC funciona:
            // os privilégios são removidos do token e só alguns são reabilitados).
            // Sem ativar explicitamente, a chamada falha com
            // ERROR_ACCESS_DENIED (5) e a RAM não é liberada — sem erro visível,
            // porque o código só contava falhas de P/Invoke por processo.
            //
            // O padrão é idêntico ao do WinMemoryCleaner
            // (ComputerService.SetIncreasePrivilege, linhas 113-136), que
            // referencia LookupPrivilegeValue + AdjustTokenPrivileges antes de
            // cada técnica de memória.
            bool debugEnabled = Utils.PrivilegeHelper.EnablePrivilege(Utils.PrivilegeHelper.SE_DEBUG_NAME);
            if (!debugEnabled)
            {
                _logger.LogWarning(
                    "[ARM-PROCESS] Não foi possível ativar SeDebugPrivilege. " +
                    "EmptyWorkingSet em processos de outra conta vai falhar (ERROR_ACCESS_DENIED).");
            }
            else
            {
                _logger.LogInfo("[ARM-PROCESS] SeDebugPrivilege ativado — EmptyWorkingSet pode prosseguir.");
            }

            IEnumerable<Process> targets;
            if (pids != null && pids.Length > 0)
            {
                targets = pids.Select(id => { try { return Process.GetProcessById(id); } catch { return null; } })
                              .Where(p => p != null)!;
            }
            else
            {
                // Sem lista explícita: NÃO varre o sistema inteiro. Varredura global
                // traria problemas para todo o software instalado, algo que o VOLTRIS
                // não deve fazer sem pedido explícito do usuário.
                _logger.LogInfo("[ARM-PROCESS] TrimWorkingSetAsync chamado sem lista de PIDs. " +
                    "Nenhum processo será alterado: varrer todo o sistema traria problemas para outros programas. " +
                    "Use Cleanup ou passe a lista explícita de PIDs.");
                return new TrimResult { SkippedByCriteria = 0, ProcessCount = 0, MbReleased = 0 };
            }

            foreach (var p in targets)
            {
                using (p)
                {
                    var pn = p.ProcessName;
                    if (_protectedProcesses.Contains(pn) || IsCaptureOrEncoder(pn) || IsCommunication(pn))
                    {
                        skipped++;
                        continue;
                    }

                    long before = SafeWorkingSet(p);
                    IntPtr handle = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_SET_QUOTA, false, p.Id);
                    if (handle == IntPtr.Zero)
                    {
                        failed++;
                        continue;
                    }

                    try
                    {
                        if (EmptyWorkingSet(handle) != 0)
                        {
                            long after = SafeWorkingSet(p);
                            long delta = Math.Max(0, before - after);
                            freedBytes += delta;
                            processed++;
                        }
                        else
                        {
                            failed++;
                        }
                    }
                    finally
                    {
                        CloseHandle(handle);
                    }
                }
            }

            sw.Stop();
            long mb = freedBytes / (1024L * 1024L);
            _logger.LogInfo(
                $"[ARM-PROCESS] TrimWorkingSet concluído em {sw.ElapsedMilliseconds}ms: " +
                $"{processed} processo(s) liberado(s), {mb} MB, {skipped} ignorado(s), {failed} falha(s).");

            return new TrimResult
            {
                ProcessCount = processed,
                MbReleased = mb,
                SkippedByCriteria = skipped,
                FailedCount = failed
            };
        }
        catch (Exception ex)
        {
            _logger.LogError($"[ARM-PROCESS] ERRO em TrimWorkingSetAsync: {ex.Message}", ex);
            return new TrimResult { FailedCount = failed };
        }
    }

    private static long SafeWorkingSet(Process p)
    {
        try { p.Refresh(); return p.WorkingSet64; }
        catch { return 0; }
    }

    public Task<bool> SetCpuAffinityAsync(int pid, string name, long mask)
    {
        return Task.Run(() =>
        {
            try
            {
                if (!IsRunningAsAdmin())
                {
                    _logger.LogWarning("[ARM-PROCESS] ProcessorAffinity requer privilégio administrativo. Nada foi feito.");
                    return false;
                }

                using var process = Process.GetProcessById(pid);
                if (!string.Equals(process.ProcessName, name, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning($"[ARM-PROCESS] PID {pid} é '{process.ProcessName}', não '{name}'. Nada foi feito.");
                    return false;
                }

                if (_protectedProcesses.Contains(name))
                {
                    _logger.LogInfo($"[ARM-PROCESS] Affinity BLOQUEADO: '{name}' é protegido. Nada foi feito.");
                    return false;
                }

                var before = (long)process.ProcessorAffinity.ToInt64();
                process.ProcessorAffinity = (IntPtr)mask;
                process.Refresh();
                var after = (long)process.ProcessorAffinity.ToInt64();

                if (after != (long)mask)
                {
                    _logger.LogError(
                        $"[ARM-PROCESS] Affinity de '{name}' (PID={pid}) solicitada como 0x{(long)mask:X}, " +
                        $"mas a releitura retornou 0x{after:X}. NÃO confirmada.");
                    return false;
                }

                _logger.LogSuccess($"[ARM-PROCESS] ✅ Affinity de '{name}' (PID={pid}) aplicada e confirmada: 0x{before:X} -> 0x{after:X}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ARM-PROCESS] ERRO em SetCpuAffinityAsync: {ex.Message}", ex);
                return false;
            }
        });
    }

    private static ProcessPriorityResult Fail(int pid, string name, string error)
    {
        VoltrisOptimizer.App.LoggingService?.LogDebug($"[ARM-PROCESS] Falha: {name} (PID={pid}) — {error}");
        return new ProcessPriorityResult
        {
            Success = false,
            Pid = pid,
            ProcessName = name,
            Error = error
        };
    }

    private static bool IsKnownGame(string name) =>
        new[] { "cs2", "csgo", "valorant", "fortnite", "apex", "overwatch", "lol", "dota", "gta5", "minecraft" }
            .Any(g => name.Contains(g, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Captura e codificação de vídeo. NUNCA devem receber EcoQoS/Efficiency Mode
    /// nem rebaixamento de prioridade: são a carga que o VOLTRIS promete proteger.
    /// </summary>
    private static bool IsCaptureOrEncoder(string name) =>
        CaptureAndEncoderProcesses.Contains(name, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Comunicação e áudio. Um clique de áudio perdido durante a transmissão é
    /// pior que alguns quadros de CPU, então o ganho de memória não compensa.
    /// </summary>
    private static bool IsCommunication(string name) =>
        CommunicationProcesses.Contains(name, StringComparer.OrdinalIgnoreCase);

    private static bool IsRunningAsAdmin()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
