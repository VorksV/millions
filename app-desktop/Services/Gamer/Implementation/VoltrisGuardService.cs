using VoltrisOptimizer.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services.Gamer.Interfaces;
using VoltrisOptimizer.Services.Optimization.Unification;
using VoltrisOptimizer.Services; // ProcessCacheService

namespace VoltrisOptimizer.Services.Gamer.Implementation
{
    /// <summary>
    /// VOLTRIS GUARD SERVICE - "Process Lasso Killer"
    /// 
    /// Watchdog autônomo que monitora e neutraliza processos em background
    /// que tentam roubar CPU do jogo em tempo real.
    /// 
    /// Diferenciais vs Process Lasso ProBalance:
    /// - Usa prioridade IDLE (mais agressiva que BelowNormal do Lasso)
    /// - Integrado ao ecossistema Voltris (exibe no dashboard)
    /// - Respeita softwares de streaming automaticamente
    /// - Restauração garantida ao encerrar sessão gamer
    /// - Relatório detalhado: qual processo causou interferência e quando
    /// - Cooldown inteligente para não exaurir processos legítimos
    /// </summary>
    public class VoltrisGuardService : IDisposable
    {
        private readonly ILoggingService _logger;
        private readonly IProcessPrioritizer _processPrioritizer;
        private readonly SharedThrottleRegistry _throttleRegistry;    // ← BRIDGE #1: Anti-conflito com DSL
        private readonly IntelligentDecisionEngine? _decisionEngine;    // ← BRIDGE #2: Threshold dinâmico por perfil
        private readonly ProcessCacheService? _processCache;            // ← Cache global de processos (evita GetProcesses() a cada 2s)

        private CancellationTokenSource? _watchdogCts;
        private Task? _watchdogTask;
        private int _gameProcessId;

        // Estado de throttle: PID -> prioridade original
        private readonly Dictionary<int, ProcessPriorityClass> _throttledProcesses = new();
        private readonly object _lock = new();

        // Cooldown por processo: não throttle o mesmo processo mais de 1x a cada X segundos
        private readonly Dictionary<int, DateTime> _lastThrottleTime = new();
        private const int ThrottleCooldownSeconds = 30;

        // Histórico de eventos para exibição no dashboard
        public readonly List<GuardEvent> EventHistory = new();
        private const int MaxEventHistory = 50;

        // Configurações
        private const int WatchdogIntervalMs = 2000;           // Verifica a cada 2s
        private const double CpuTotalThreshold = 75.0;         // CPU geral > 75% para ativar guarda
        private const double IntruderCpuThreshold = 8.0;       // Processo com > 8% de CPU = intruso
        private const int MinCoreCount = 4;                    // Não atuar em PCs com < 4 cores

        // Processos do sistema que nunca devem ser throttlados
        private static readonly HashSet<string> SystemWhitelist = new(StringComparer.OrdinalIgnoreCase)
        {
            "System", "Idle", "Registry", "smss", "csrss", "wininit",
            "winlogon", "lsass", "services", "svchost", "dwm", "fontdrvhost",
            "explorer", "taskmgr", "taskhostw", "RuntimeBroker",
            "VoltrisOptimizer", "VoltrisOptimizer.exe",

            // FIX: Runtime e processos de sistema legítimos que não devem ser throttlados
            // dotnet é o runtime .NET - pode ser usado por apps systemáticos legítimos
            "dotnet", "dotnet.exe",
            // Shells e ambiente
            "powershell", "pwsh", "cmd", "conhost", "wsl", "bash",
            // NVIDIA/AMD background services
            "nvcontainer", "nvsphelper64", "nvspcaps64", "nvdisplay.container",
            "RadeonSoftware", "AMDRSSrcExt",
            // Antivírus - nunca throttle por segurança
            "MsMpEng", "NisSrv", "SecurityHealthService", "mbamservice",
            // Windows Update - não deve ser interrompido mid-update
            "TiWorker", "TrustedInstaller", "usocoreworker",
            // Audio - throttle de audiodg causa problemas de som
            "audiodg",
            // OSD e monitoramento de hardware (usuarios freq. usam junto com jogos)
            "MSIAfterburner", "RTSS", "RivaTunerStatisticsServer", "HWiNFO64"
        };

        public bool IsActive { get; private set; }
        public int TotalThrottleEvents { get; private set; }
        public event EventHandler<GuardEvent>? IntruderDetected;

        public VoltrisGuardService(
            ILoggingService logger, 
            IProcessPrioritizer processPrioritizer,
            SharedThrottleRegistry throttleRegistry,
            IntelligentDecisionEngine? decisionEngine = null,
            ProcessCacheService? processCache = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _processPrioritizer = processPrioritizer ?? throw new ArgumentNullException(nameof(processPrioritizer));
            _throttleRegistry = throttleRegistry ?? throw new ArgumentNullException(nameof(throttleRegistry));
            _decisionEngine = decisionEngine;
            _processCache = processCache;
            _logger.LogEntry("VoltrisGuardService");
        }

        /// <summary>
        /// Inicia o Voltris Guard para proteger um processo de jogo.
        /// </summary>
        public void Start(int gameProcessId)
        {
            _logger.LogEntry(nameof(Start), ("gameProcessId", gameProcessId));
            if (IsActive)
            {
                _logger.LogWarning("[VoltrisGuard] Watchdog já está ativo.");
                _logger.LogExit(nameof(Start));
                return;
            }

            if (Environment.ProcessorCount < MinCoreCount)
            {
                _logger.LogWarning($"[VoltrisGuard] CPU com apenas {Environment.ProcessorCount} cores. Guard não ativado (mínimo: {MinCoreCount}).");
                _logger.LogExit(nameof(Start));
                return;
            }

            _gameProcessId = gameProcessId;
            _watchdogCts = new CancellationTokenSource();
            _watchdogTask = Task.Run(() => WatchdogLoopAsync(_watchdogCts.Token), _watchdogCts.Token);
            IsActive = true;

            _logger.LogSuccess($"[VoltrisGuard] 🛡️ ═══════════════════════════════════════");
            _logger.LogSuccess($"[VoltrisGuard] 🛡️ Voltris Guard ATIVO (PID do Jogo: {gameProcessId})");
            _logger.LogSuccess($"[VoltrisGuard] 🛡️ Threshold CPU: {CpuTotalThreshold}% | Intruso: {IntruderCpuThreshold}%");
            _logger.LogSuccess($"[VoltrisGuard] 🛡️ ═══════════════════════════════════════");
            _logger.LogExit(nameof(Start));
        }

        /// <summary>
        /// Para o Voltris Guard e restaura TODAS as prioridades modificadas.
        /// </summary>
        public void Stop()
        {
            _logger.LogEntry(nameof(Stop));
            if (!IsActive)
            {
                _logger.LogExit(nameof(Stop));
                return;
            }

            _logger.LogInfo("[VoltrisGuard] Parando watchdog e restaurando prioridades...");

            _watchdogCts?.Cancel();
            try
            {
                if (_watchdogTask != null)
                    Task.Run(() => _watchdogTask).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogWarning($"[VoltrisGuard] Erro ao aguardar watchdog: {ex.Message}");
            }

            RestoreAllThrottled();

            IsActive = false;
            _logger.LogSuccess($"[VoltrisGuard] Guard encerrado. Total de eventos: {TotalThrottleEvents}");
            _logger.LogExit(nameof(Stop));
        }

        private async Task WatchdogLoopAsync(CancellationToken ct)
        {
            _logger.LogEntry(nameof(WatchdogLoopAsync));
            // Warm-up: aguardar jogo inicializar (5s)
            await Task.Delay(5000, ct);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await ScanAndThrottleAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[VoltrisGuard] Erro no watchdog loop: {ex.Message}", ex);
                }

                await Task.Delay(WatchdogIntervalMs, ct);
            }
            _logger.LogExit(nameof(WatchdogLoopAsync));
        }

        private async Task ScanAndThrottleAsync(CancellationToken ct)
        {
            _logger.LogEntry(nameof(ScanAndThrottleAsync));
            // 1. Verificar se jogo ainda está ativo
            try
            {
                using var gameProcess = SafeProcess.TryGet(_gameProcessId);
                if (gameProcess.HasExited)
                {
                    _logger.LogInfo("[VoltrisGuard] Processo do jogo encerrou. Parando guard.");
                    _watchdogCts?.Cancel();
                    _logger.LogExit(nameof(ScanAndThrottleAsync));
                    return;
                }
            }
            catch (ArgumentException)
            {
                _logger.LogInfo("[VoltrisGuard] Processo do jogo não encontrado. Parando guard.");
                _watchdogCts?.Cancel();
                _logger.LogExit(nameof(ScanAndThrottleAsync));
                return;
            }

            // 2. Coleta CPU total (via Performance Counter rápido)
            double totalCpu = await GetTotalCpuAsync(ct);

            // BRIDGE #2: Threshold dinâmico por Perfil Inteligente
            // GamerCompetitive → mais agressivo (60%), WorkOffice → mais conservador (85%)
            double activeThreshold = CpuTotalThreshold; // fallback para o valor fixo
            if (_decisionEngine != null)
            {
                var profile = VoltrisOptimizer.Services.SettingsService.Instance.Settings.IntelligentProfile;
                activeThreshold = profile switch
                {
                    VoltrisOptimizer.Services.IntelligentProfileType.GamerCompetitive    => 60.0,
                    VoltrisOptimizer.Services.IntelligentProfileType.GamerSinglePlayer   => 65.0,
                    VoltrisOptimizer.Services.IntelligentProfileType.GeneralBalanced     => 75.0,
                    VoltrisOptimizer.Services.IntelligentProfileType.WorkOffice          => 85.0,
                    VoltrisOptimizer.Services.IntelligentProfileType.EnterpriseSecure    => 90.0,
                    _                                                                    => CpuTotalThreshold
                };
            }

            if (totalCpu < activeThreshold)
            {
                // CPU ok, verificar se devemos restaurar processos throttlados há muito tempo
                await CheckAndRestoreStaleThrottlesAsync(ct);
                _logger.LogExit(nameof(ScanAndThrottleAsync));
                return;
            }

            _logger.LogInfo($"[VoltrisGuard] ⚠️ CPU alta: {totalCpu:F1}% (threshold: {activeThreshold:F0}%). Iniciando varredura de intrusos...");

            // 3. Identificar intrusos com uso acima do threshold
            var intruders = await FindIntrudersAsync(ct);

            if (!intruders.Any())
            {
                _logger.LogInfo("[VoltrisGuard] Nenhum intruso identificado. Aguardando.");
                _logger.LogExit(nameof(ScanAndThrottleAsync));
                return;
            }

            // 4. Throttle intrusos identificados
            foreach (var (pid, name, cpuUsage) in intruders)
            {
                await ThrottleProcessAsync(pid, name, cpuUsage, ct);
            }
            _logger.LogExit(nameof(ScanAndThrottleAsync));
        }

        private async Task<double> GetTotalCpuAsync(CancellationToken ct)
        {
            _logger.LogEntry(nameof(GetTotalCpuAsync));
            var result = await Task.Run(() =>
            {
                try
                {
                    using var cpuCounter = new SafePerformanceCounter("Processor", "% Processor Time", "_Total");
                    cpuCounter.NextValue();
                    Thread.Sleep(200);
                    var value = cpuCounter.NextValue();
                    _logger.LogExit(nameof(GetTotalCpuAsync), value);
                    return value;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug($"[VoltrisGuard] GetTotalCpuAsync error: {ex.Message}");
                    _logger.LogExit(nameof(GetTotalCpuAsync), 0.0);
                    return 0;
                }
            }, ct);
            return result;
        }

        private async Task<List<(int pid, string name, double cpu)>> FindIntrudersAsync(CancellationToken ct)
        {
            _logger.LogEntry(nameof(FindIntrudersAsync));
            var result = await Task.Run(() =>
            {
                var intruders = new List<(int, string, double)>();
                var now = DateTime.UtcNow;

                // Usar cache de processos se disponível (evita Process.GetProcesses() a cada 2s)
                IEnumerable<Process> processes = _processCache != null
                    ? _processCache.GetCachedProcesses()
                    : Process.GetProcesses();

                foreach (var p in processes)
                {
                    try
                    {
                        if (p.Id == _gameProcessId) continue;
                        if (SystemWhitelist.Contains(p.ProcessName)) continue;
                        if (_processPrioritizer.IsProtectedProcess(p.ProcessName)) continue;

                        // Verificar cooldown de throttle
                        lock (_lock)
                        {
                            if (_lastThrottleTime.TryGetValue(p.Id, out var lastTime))
                            {
                                if ((now - lastTime).TotalSeconds < ThrottleCooldownSeconds)
                                    continue;
                            }
                        }

                        // Medir uso de CPU do processo (heurística baseada em CPU total acumulado)
                        var elapsed = (DateTime.Now - p.StartTime).TotalSeconds;
                        if (elapsed <= 0) continue;

                        var cpuUsagePct = (p.TotalProcessorTime.TotalSeconds / elapsed / Environment.ProcessorCount) * 100.0;

                        if (cpuUsagePct >= IntruderCpuThreshold)
                        {
                            intruders.Add((p.Id, p.ProcessName, cpuUsagePct));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug($"[VoltrisGuard] FindIntruders process error: {ex.Message}");
                    }
                    // Não fazer Dispose aqui se veio do cache — o cache gerencia o ciclo de vida
                }

                var ordered = intruders.OrderByDescending(i => i.Item3).ToList();
                _logger.LogExit(nameof(FindIntrudersAsync), ordered.Count);
                return ordered;
            }, ct);
            return result;
        }

        private async Task ThrottleProcessAsync(int pid, string name, double cpuUsage, CancellationToken ct)
        {
            await Task.Run(() =>
            {
                try
                {
                    using var p = SafeProcess.TryGet(pid);

                    var originalPriority = p.PriorityClass;

                    // Não throttle se já está em prioridade baixa ou abaixo
                    if (originalPriority == ProcessPriorityClass.Idle ||
                        originalPriority == ProcessPriorityClass.BelowNormal)
                        return;

                    // BRIDGE #1: Consultar SharedThrottleRegistry — evita conflito com o DSL
                    bool isFirstThrottler = _throttleRegistry.TryRegisterThrottle(pid, "VoltrisGuard", originalPriority);

                    if (!isFirstThrottler)
                    {
                        // DSL já está gerenciando — apenas registrar interesse, não alterar novamente
                        _logger.LogInfo($"[VoltrisGuard] Co-throttle: PID {pid} ({name}) já gerenciado pelo DSL. Registrando co-interesse.");
                        lock (_lock)
                        {
                            if (!_throttledProcesses.ContainsKey(pid))
                                _throttledProcesses[pid] = originalPriority;
                            _lastThrottleTime[pid] = DateTime.UtcNow;
                        }
                        TotalThrottleEvents++;
                        return;
                    }

                    // CORREÇÃO CRÍTICA: usar BelowNormal, NUNCA Idle.
                    p.PriorityClass = ProcessPriorityClass.BelowNormal;

                    lock (_lock)
                    {
                        if (!_throttledProcesses.ContainsKey(pid))
                            _throttledProcesses[pid] = originalPriority;
                        _lastThrottleTime[pid] = DateTime.UtcNow;
                    }

                    TotalThrottleEvents++;

                    var evt = new GuardEvent
                    {
                        Timestamp = DateTime.Now,
                        ProcessId = pid,
                        ProcessName = name,
                        CpuUsageAtDetection = cpuUsage,
                        Action = "Throttled to BelowNormal",
                        OriginalPriority = originalPriority
                    };

                    AddEvent(evt);
                    IntruderDetected?.Invoke(this, evt);

                    _logger.LogWarning($"[VoltrisGuard] 🛡️ INTRUSO NEUTRALIZADO: '{name}' (PID:{pid}) " +
                                       $"estava usando {cpuUsage:F1}% CPU. Rebaixado para BelowNormal.");
                }
                catch (ArgumentException)
                {
                    _logger.LogInfo($"[VoltrisGuard] PID {pid} ({name}) encerrou antes do throttle.");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[VoltrisGuard] Não foi possível throttle '{name}' (PID:{pid}): {ex.Message}");
                }
            }, ct);
        }

        /// <summary>
        /// Restaura processos throttlados há mais de 120 segundos quando CPU normalizar.
        /// Evita que processos legítimos fiquem em Idle para sempre.
        /// </summary>
        private async Task CheckAndRestoreStaleThrottlesAsync(CancellationToken ct)
        {
            var staleThreshold = TimeSpan.FromSeconds(120);
            var now = DateTime.UtcNow;

            var stalePids = new List<int>();

            lock (_lock)
            {
                stalePids.AddRange(
                    _lastThrottleTime
                        .Where(kv => (now - kv.Value) > staleThreshold)
                        .Select(kv => kv.Key));
            }

            if (!stalePids.Any()) return;

            _logger.LogInfo($"[VoltrisGuard] Restaurando {stalePids.Count} processo(s) idle há mais de 120s (CPU normalizada).");

            foreach (var pid in stalePids)
            {
                await RestoreProcessAsync(pid, ct);
            }
        }

        private async Task RestoreProcessAsync(int pid, CancellationToken ct)
        {
            await Task.Run(() =>
            {
                ProcessPriorityClass original;

                lock (_lock)
                {
                    if (!_throttledProcesses.TryGetValue(pid, out original)) return;
                }

                try
                {
                    using var p = SafeProcess.TryGet(pid);
                    p.PriorityClass = original;
                    _logger.LogInfo($"[VoltrisGuard] ♻️ '{p.ProcessName}' (PID:{pid}) restaurado: BelowNormal -> {original}");
                }
                catch (ArgumentException)
                {
                    _logger.LogInfo($"[VoltrisGuard] PID {pid} já encerrou, removendo do registro.");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[VoltrisGuard] Falha ao restaurar PID {pid}: {ex.Message}");
                }
                finally
                {
                    lock (_lock)
                    {
                        _throttledProcesses.Remove(pid);
                        _lastThrottleTime.Remove(pid);
                    }
                }
            }, ct);
        }

        private void RestoreAllThrottled()
        {
            Dictionary<int, ProcessPriorityClass> snapshot;

            lock (_lock)
            {
                snapshot = new Dictionary<int, ProcessPriorityClass>(_throttledProcesses);
                _throttledProcesses.Clear();
                _lastThrottleTime.Clear();
            }

            int restored = 0;
            int missed = 0;

            foreach (var (pid, originalPriority) in snapshot)
            {
                try
                {
                    // BRIDGE #1: Liberar do registry central — prioridade retornada é a original real
                    var registryOriginal = _throttleRegistry.TryRelease(pid, "VoltrisGuard");
                    var finalPriority = registryOriginal ?? originalPriority;

                    // Só restaurar se o registry permitir (retornar não-null)
                    // Se DSL ainda é co-dono, ele vai restaurar quando liberar
                    if (registryOriginal.HasValue)
                    {
                        using var p = SafeProcess.TryGet(pid);
                        p.PriorityClass = finalPriority;
                        _logger.LogInfo($"[VoltrisGuard] ♻️ '{p.ProcessName}' (PID:{pid}) restaurado: BelowNormal -> {finalPriority}");
                        restored++;
                    }
                    else
                    {
                        _logger.LogInfo($"[VoltrisGuard] PID {pid} — DSL ainda é co-dono. Restauração delegada ao DSL.");
                        missed++;
                    }
                }
                catch (ArgumentException) { missed++; }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[VoltrisGuard] Falha ao restaurar PID {pid}: {ex.Message}");
                    missed++;
                }
            }

            _logger.LogSuccess($"[VoltrisGuard] ✅ Sessão encerrada: {restored} restaurados, {missed} delgados/encerrados.");
        }

        private void AddEvent(GuardEvent evt)
        {
            lock (_lock)
            {
                EventHistory.Add(evt);
                if (EventHistory.Count > MaxEventHistory)
                    EventHistory.RemoveAt(0);
            }
        }

        public void Dispose()
        {
            Stop();
            _watchdogCts?.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Evento de detecção pelo Voltris Guard
    /// </summary>
    public class GuardEvent
    {
        public DateTime Timestamp { get; set; }
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public double CpuUsageAtDetection { get; set; }
        public string Action { get; set; } = string.Empty;
        public ProcessPriorityClass OriginalPriority { get; set; }
    }
}

