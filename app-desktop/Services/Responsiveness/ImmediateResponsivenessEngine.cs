using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.Optimization;

namespace VoltrisOptimizer.Services.Responsiveness
{
    /// <summary>
    /// Immediate Responsiveness Engine v2 (IRE v2) — Revisão 2
    ///
    /// CORREÇÕES APLICADAS vs v1:
    ///   1. EcoQoS REMOVIDO — foco exclusivo em CPU priority + I/O priority
    ///   2. Startup Mode usa Idle Heuristic real (WS + whitelist + janela + idade)
    ///   3. Input Awareness: GetLastInputInfo < 300ms → boost imediato
    ///   4. Decay System: PriorityBoostToken com expiração automática
    ///   5. DSL orientado a intenção: IreIntentRules avalia QUANDO agir
    ///
    /// ARQUITETURA:
    ///   Startup Mode (0–200ms) → age antes do DSL 5.0 (que tem delay de 2s)
    ///   Adaptive Loop (2–5s)   → age apenas quando regras disparam
    ///   Decay Loop (500ms)     → expira boosts automaticamente
    ///   Focus Mode             → detecta fullscreen e reforça foreground
    ///
    /// SEGURANÇA:
    ///   - Nunca usa Realtime priority
    ///   - Nunca aplica mudanças irreversíveis (Decay System garante restauração)
    ///   - Valida processo antes de qualquer operação
    ///   - Lista de protegidos: mesma do StabilityGuardEngine
    /// </summary>
    public sealed class ImmediateResponsivenessEngine : IImmediateResponsivenessEngine
    {
        private readonly ILoggingService _logger;
        private readonly IreSystemTelemetryService _telemetry;
        private readonly CriticalProcessDetector _criticalDetector;
        private readonly ResponsivenessConfig _config;
        private readonly IreIntentRules _rules;

        // Estado do loop
        private CancellationTokenSource? _loopCts;
        private Task? _loopTask;
        private Task? _decayTask;
        private volatile bool _isRunning;
        private readonly object _stateLock = new();

        // Cooldowns por ação
        private readonly Stopwatch _ioPriorityCooldown = new();
        private readonly Stopwatch _normalizeCooldown = new();

        // Decay System: tokens de boost ativos
        private readonly ConcurrentDictionary<int, PriorityBoostToken> _activeBoosts = new();

        // I/O throttles aplicados (para restauração)
        private readonly ConcurrentDictionary<int, IoPriorityTarget> _ioThrottled = new();

        private int _lastForegroundPid;
        private readonly int _ownPid = Process.GetCurrentProcess().Id;

        // Métricas de impacto
        private long _startupActionsApplied;
        private long _adaptiveCycles;
        private long _foregroundBoostsApplied;
        private long _ioThrottlesApplied;
        private long _decayRestorations;

        public bool IsRunning => _isRunning;

        public ImmediateResponsivenessEngine(
            ILoggingService logger,
            CriticalProcessDetector criticalDetector,
            ProcessCacheService processCache,
            ResponsivenessConfig? config = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _criticalDetector = criticalDetector ?? throw new ArgumentNullException(nameof(criticalDetector));
            _config = config ?? new ResponsivenessConfig();
            _telemetry = new IreSystemTelemetryService(logger, processCache);
            _rules = new IreIntentRules(_config);
        }

        // =====================================================================
        // STARTUP MODE — impacto imediato em até 200ms
        // =====================================================================

        public async Task RunStartupModeAsync(CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInfo("[IRE] === STARTUP MODE INICIADO ===");

            try
            {
                _telemetry.Initialize();

                var processes = GetProcessesSafe();
                if (processes.Length == 0) return;

                // Primeira coleta para inicializar os contadores de CPU por processo.
                // CPU% não é confiável aqui (precisa de dois pontos no tempo),
                // mas precisamos chamar Collect para popular _prevCpuTimes.
                var snapshot = _telemetry.Collect(processes);
                int actionsApplied = 0;

                foreach (var p in processes)
                {
                    if (sw.ElapsedMilliseconds >= _config.StartupBudgetMs)
                    {
                        _logger.LogDebug($"[IRE] Startup budget atingido ({_config.StartupBudgetMs}ms).", "IRE");
                        break;
                    }
                    if (ct.IsCancellationRequested) break;

                    try
                    {
                        if (p.HasExited || p.Id <= 4 || p.Id == _ownPid) continue;
                        if (HeavyProcessDetector.IsProtected(p)) continue;
                        if (p.Id == snapshot.ForegroundPid) continue;

                        // IDLE HEURISTIC: WS + whitelist + janela visível + idade do processo
                        // Não throttlar processos que o usuário provavelmente vai usar logo
                        if (!ProcessIdleHeuristic.IsHeavyAndIdle(p, _config.HeavyProcessWorkingSetBytes))
                            continue;

                        // Aplicar apenas I/O VeryLow — sem EcoQoS
                        bool ioApplied = IreNativeInterop.SetIoPriority(p.Id, IoPriorityTarget.VeryLow, out string method);

                        if (ioApplied)
                        {
                            _ioThrottled[p.Id] = IoPriorityTarget.Normal; // Guardar para restauração
                            actionsApplied++;
                            _startupActionsApplied++;
                            _logger.LogInfo(
                                $"[IRE] [STARTUP] process={p.ProcessName} pid={p.Id} " +
                                $"ws={p.WorkingSet64 / 1024 / 1024}MB " +
                                $"io=VeryLow({method}) " +
                                $"reason=heavy_and_idle(noWindow,age>30s,ws>{_config.HeavyProcessWorkingSetBytes / 1024 / 1024}MB)");
                        }
                    }
                    catch (InvalidOperationException) { _logger?.LogWarning($"[IRE] Processo {p.Id} encerrado durante startup"); }
                    catch (Exception ex) { _logger?.LogWarning($"[IRE] Erro no startup mode para {p.ProcessName}: {ex.Message}"); }
                }

                sw.Stop();
                _logger.LogInfo(
                    $"[IRE] === STARTUP MODE CONCLUÍDO === " +
                    $"actions={actionsApplied} elapsed={sw.ElapsedMilliseconds}ms budget={_config.StartupBudgetMs}ms");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[IRE] Startup mode error: {ex.Message}");
            }

            await Task.CompletedTask;
        }

        // =====================================================================
        // ADAPTIVE LOOP — orientado a intenção
        // =====================================================================

        public async Task StartAdaptiveLoopAsync(CancellationToken ct = default)
        {
            lock (_stateLock)
            {
                if (_isRunning) return;
                _loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                _isRunning = true;
                _loopTask = Task.Run(() => AdaptiveLoopAsync(_loopCts.Token), _loopCts.Token);
                _decayTask = Task.Run(() => DecayLoopAsync(_loopCts.Token), _loopCts.Token);
            }
            _logger.LogInfo("[IRE] Adaptive Loop + Decay Loop INICIADOS.");
            await Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            lock (_stateLock)
            {
                if (!_isRunning) return;
                _isRunning = false;
                _loopCts?.Cancel();
            }

            try
            {
                var tasks = new List<Task>();
                if (_loopTask != null) tasks.Add(_loopTask);
                if (_decayTask != null) tasks.Add(_decayTask);
                if (tasks.Count > 0)
                    await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(3));
            }
            catch (OperationCanceledException) { }
            catch (TimeoutException) { }

            RestoreAll();
            _logger.LogInfo(
                $"[IRE] PARADO. startupActions={_startupActionsApplied} " +
                $"cycles={_adaptiveCycles} boosts={_foregroundBoostsApplied} " +
                $"ioThrottles={_ioThrottlesApplied} decayRestores={_decayRestorations}");
        }

        private async Task AdaptiveLoopAsync(CancellationToken ct)
        {
            // 500ms de delay para não competir com o startup da UI
            await Task.Delay(500, ct);

            while (!ct.IsCancellationRequested)
            {
                // Boundary Tracking: Registra o nome da thread
                VoltrisOptimizer.Services.Diagnostics.ThreadNameRegistry.Register("IRE-AdaptiveLoop");
                using var profiler = VoltrisOptimizer.Services.Diagnostics.CpuSelfProfiler.Instance.BeginSection("IRE.AdaptiveProcess");

                var cycleSw = Stopwatch.StartNew();
                try
                {
                    var processes = GetProcessesSafe();
                    var snapshot = _telemetry.Collect(processes);

                    // Avaliar intenção via regras declarativas
                    var intent = _rules.Evaluate(snapshot, _lastForegroundPid);

                    if (intent != null)
                    {
                        _logger.LogInfo($"[IRE] INTENT: {intent.Description}");
                        ExecuteIntent(intent, snapshot, processes);
                    }
                    else
                    {
                        _logger.LogDebug(
                            $"[IRE] No action — cpu={snapshot.CpuTotalPercent:F1}% " +
                            $"disk={snapshot.DiskQueueLength:F2} lastInput={snapshot.LastInputMs}ms",
                            "IRE");
                    }

                    // Atualizar foreground para próximo ciclo
                    if (snapshot.ForegroundPid > 0)
                        _lastForegroundPid = snapshot.ForegroundPid;

                    // Focus Mode — avaliado independentemente das regras
                    if (_config.EnableFocusMode)
                        ApplyFocusModeIfNeeded(snapshot, processes);

                    _adaptiveCycles++;
                    cycleSw.Stop();

                    // Intervalo adaptativo
                    int intervalMs = DetermineInterval(snapshot);
                    _logger.LogDebug($"[IRE] Cycle {_adaptiveCycles} done in {cycleSw.ElapsedMilliseconds}ms. Next in {intervalMs}ms.", "IRE");

                    await Task.Delay(intervalMs, ct);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[IRE] Adaptive loop error: {ex.Message}");
                    try { await Task.Delay(2000, ct); } catch { break; }
                }
                finally
                {
                    VoltrisOptimizer.Services.Diagnostics.ThreadNameRegistry.Unregister();
                }
            }
        }

        // =====================================================================
        // DECAY LOOP — expira boosts automaticamente
        // =====================================================================

        private async Task DecayLoopAsync(CancellationToken ct)
        {
            // Roda a cada 500ms para expirar boosts com precisão
            while (!ct.IsCancellationRequested)
            {
                // Boundary Tracking: Registra o nome da thread
                VoltrisOptimizer.Services.Diagnostics.ThreadNameRegistry.Register("IRE-DecayLoop");
                
                try
                {
                    await Task.Delay(500, ct);
                    
                    using var profiler = VoltrisOptimizer.Services.Diagnostics.CpuSelfProfiler.Instance.BeginSection("IRE.Decay");
                    ExpireBoosts();
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"IRE Decay Error: {ex.Message}");
                }
                finally
                {
                    VoltrisOptimizer.Services.Diagnostics.ThreadNameRegistry.Unregister();
                }
            }
        }

        private void ExpireBoosts()
        {
            var expired = _activeBoosts.Values.Where(t => t.IsExpired).ToList();
            foreach (var token in expired)
            {
                try
                {
                    _activeBoosts.TryRemove(token.Pid, out _);

                    var p = Process.GetProcessById(token.Pid);
                    if (p.HasExited) continue;

                    // Restaurar CPU priority
                    if (p.PriorityClass != token.OriginalCpuPriority)
                    {
                        p.PriorityClass = token.OriginalCpuPriority;
                    }

                    // Restaurar I/O priority
                    IreNativeInterop.SetIoPriority(token.Pid, token.OriginalIoPriority, out _);

                    _decayRestorations++;
                    _logger.LogInfo(
                        $"[IRE] [DECAY] Boost expired: process={token.ProcessName} pid={token.Pid} " +
                        $"reason={token.Reason} restored cpu={token.OriginalCpuPriority} io={token.OriginalIoPriority}");
                }
                catch (ArgumentException) { _activeBoosts.TryRemove(token.Pid, out _); } // Processo não existe mais
                catch (Exception ex) { _logger?.LogWarning($"[IRE] Erro no decay de boost: {ex.Message}"); }
            }
        }

        // =====================================================================
        // EXECUÇÃO DE INTENÇÕES
        // =====================================================================

        private void ExecuteIntent(IreIntent intent, SystemTelemetrySnapshot snapshot, Process[] processes)
        {
            switch (intent.Action)
            {
                case IreAction.BoostForeground:
                    ApplyForegroundBoostWithDecay(intent.TargetPid, intent.BoostDuration, intent.Reason);
                    break;

                case IreAction.ThrottleBackgroundAggressive:
                    ApplyIoThrottle(snapshot, IoPriorityTarget.VeryLow);
                    break;

                case IreAction.ThrottleBackgroundIo:
                    ApplyIoThrottle(snapshot, IoPriorityTarget.Low);
                    break;

                case IreAction.ThrottleBackgroundCpu:
                    // Apenas I/O throttle leve — não mexer em CPU priority sem CPU% por processo confiável
                    ApplyIoThrottle(snapshot, IoPriorityTarget.Low);
                    break;

                case IreAction.NormalizeBackground:
                    TryNormalizeBackground();
                    break;
            }
        }

        // =====================================================================
        // FOREGROUND BOOST COM DECAY
        // =====================================================================

        private bool ApplyForegroundBoostWithDecay(int pid, TimeSpan duration, BoostReason reason)
        {
            if (pid <= 0 || pid == _ownPid) return false;

            // Se já tem boost ativo para este PID, renovar duração
            if (_activeBoosts.TryGetValue(pid, out var existing) && !existing.IsExpired)
            {
                _logger.LogDebug($"[IRE] Boost already active for pid={pid} remaining={existing.Remaining.TotalMilliseconds:F0}ms", "IRE");
                return false;
            }

            try
            {
                var p = Process.GetProcessById(pid);
                if (p.HasExited || HeavyProcessDetector.IsProtected(p)) return false;

                var originalCpu = p.PriorityClass;
                var originalIo = IoPriorityTarget.Normal;

                // CPU: AboveNormal — NUNCA Realtime ou High
                if (p.PriorityClass < ProcessPriorityClass.AboveNormal)
                    p.PriorityClass = ProcessPriorityClass.AboveNormal;

                // I/O: High para foreground
                IreNativeInterop.SetIoPriority(pid, IoPriorityTarget.High, out string method);

                // PriorityBoost do scheduler
                try { p.PriorityBoostEnabled = true; } catch (Exception exPb) { _logger?.LogWarning($"[IRE] Erro ao set PriorityBoostEnabled para {pid}: {exPb.Message}"); }

                // Registrar token de decay
                _activeBoosts[pid] = new PriorityBoostToken
                {
                    Pid = pid,
                    ProcessName = p.ProcessName,
                    OriginalCpuPriority = originalCpu,
                    OriginalIoPriority = originalIo,
                    AppliedAt = DateTime.UtcNow,
                    Duration = duration,
                    Reason = reason
                };

                _foregroundBoostsApplied++;
                _logger.LogInfo(
                    $"[IRE] ForegroundBoost applied process={p.ProcessName} pid={pid} " +
                    $"oldPriority={originalCpu} newPriority=AboveNormal " +
                    $"io=High({method}) duration={duration.TotalMilliseconds:F0}ms reason={reason}");

                return true;
            }
            catch (ArgumentException) { return false; } // Processo não existe
            catch (Exception ex)
            {
                _logger.LogDebug($"[IRE] ForegroundBoost pid={pid} error: {ex.Message}", "IRE");
                return false;
            }
        }

        // =====================================================================
        // I/O THROTTLE
        // =====================================================================

        private void ApplyIoThrottle(SystemTelemetrySnapshot snapshot, IoPriorityTarget level)
        {
            if (_ioPriorityCooldown.IsRunning &&
                _ioPriorityCooldown.ElapsedMilliseconds < _config.IoPriorityCooldownMs)
                return;

            var heavyProcesses = HeavyProcessDetector
                .GetHeavyBackgroundProcesses(snapshot.ProcessSamples, _config)
                .ToList();

            if (heavyProcesses.Count == 0) return;

            int throttled = 0;
            foreach (var sample in heavyProcesses)
            {
                // Não throttlar processos com boost ativo
                if (_activeBoosts.ContainsKey(sample.Pid)) continue;

                bool applied = IreNativeInterop.SetIoPriority(sample.Pid, level, out string method);
                if (applied)
                {
                    _ioThrottled[sample.Pid] = IoPriorityTarget.Normal;
                    throttled++;
                    _ioThrottlesApplied++;
                    _logger.LogInfo(
                        $"[IRE] IoPriority set process={sample.Name} pid={sample.Pid} " +
                        $"cpu={sample.CpuPercent:F1}% ws={sample.WorkingSetBytes / 1024 / 1024}MB " +
                        $"io={level}({method})");
                }
            }

            if (throttled > 0)
                _ioPriorityCooldown.Restart();
        }

        // =====================================================================
        // FOCUS MODE
        // =====================================================================

        private void ApplyFocusModeIfNeeded(SystemTelemetrySnapshot snapshot, Process[] processes)
        {
            try
            {
                var fgSample = snapshot.ProcessSamples.FirstOrDefault(s => s.IsForeground);
                if (fgSample == null) return;

                var fgProcess = processes.FirstOrDefault(p => p.Id == fgSample.Pid);
                if (fgProcess == null || fgProcess.HasExited) return;

                bool isFocusApp = _criticalDetector.IsFullscreen(fgProcess);
                if (!isFocusApp) return;

                int focusThrottled = 0;
                foreach (var sample in snapshot.ProcessSamples)
                {
                    if (sample.IsForeground || sample.Pid <= 4 || sample.Pid == _ownPid) continue;
                    if (HeavyProcessDetector.IsProtected(sample.Name)) continue;
                    if (_activeBoosts.ContainsKey(sample.Pid)) continue;
                    if (sample.CpuPercent < _config.FocusModeFullscreenCpuThreshold) continue;

                    bool applied = IreNativeInterop.SetIoPriority(sample.Pid, IoPriorityTarget.VeryLow, out _);
                    if (applied)
                    {
                        _ioThrottled[sample.Pid] = IoPriorityTarget.Normal;
                        focusThrottled++;
                    }
                }

                if (focusThrottled > 0)
                {
                    _logger.LogInfo(
                        $"[IRE] [FOCUS MODE] foreground={fgSample.Name} pid={fgSample.Pid} " +
                        $"fullscreen=True backgroundThrottled={focusThrottled}");
                }
            }
            catch (Exception ex) { _logger?.LogWarning($"[IRE] Erro no FocusMode: {ex.Message}"); }
        }

        // =====================================================================
        // DSL COMMANDS — chamáveis externamente
        // =====================================================================

        public bool ApplyIoPriority(int pid, IoPriorityTarget target)
        {
            try
            {
                var p = Process.GetProcessById(pid);
                if (p.HasExited || HeavyProcessDetector.IsProtected(p)) return false;

                bool result = IreNativeInterop.SetIoPriority(pid, target, out string method);
                _logger.LogInfo(
                    $"[IRE] [DSL] ApplyIoPriority process={p.ProcessName} pid={pid} " +
                    $"target={target} method={method} success={result}");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[IRE] ApplyIoPriority pid={pid} error: {ex.Message}", "IRE");
                return false;
            }
        }

        public bool BoostForeground()
        {
            try
            {
                int pid = VoltrisOptimizer.Core.ForegroundWindowTracker.Instance.CurrentPid;
                if (pid <= 0 || pid == _ownPid) return false;

                return ApplyForegroundBoostWithDecay(
                    pid,
                    TimeSpan.FromMilliseconds(_config.ForegroundBoostCooldownMs),
                    BoostReason.DslCommand);
            }
            catch { return false; }
        }

        public void NormalizeBackground()
        {
            if (_normalizeCooldown.IsRunning &&
                _normalizeCooldown.ElapsedMilliseconds < _config.NormalizeBackgroundCooldownMs)
                return;

            RestoreIoThrottled();
            _normalizeCooldown.Restart();
        }

        // =====================================================================
        // RESTORE
        // =====================================================================

        private void TryNormalizeBackground()
        {
            if (_ioThrottled.IsEmpty) return;
            if (_normalizeCooldown.IsRunning &&
                _normalizeCooldown.ElapsedMilliseconds < _config.NormalizeBackgroundCooldownMs)
                return;

            RestoreIoThrottled();
            _normalizeCooldown.Restart();
        }

        private void RestoreIoThrottled()
        {
            int restored = 0;
            foreach (var kvp in _ioThrottled)
            {
                try
                {
                    IreNativeInterop.SetIoPriority(kvp.Key, IoPriorityTarget.Normal, out _);
                    restored++;
                }
                catch (Exception ex) { _logger?.LogWarning($"[IRE] Erro ao restaurar IO priority do processo {kvp.Key}: {ex.Message}"); }
            }
            _ioThrottled.Clear();

            if (restored > 0)
                _logger.LogInfo($"[IRE] NormalizeBackground: restored={restored} io priorities");
        }

        private void RestoreAll()
        {
            foreach (var token in _activeBoosts.Values)
            {
                try
                {
                    // FIX: Process.GetProcessById lança ArgumentException se PID não existe
                    Process p;
                    try { p = Process.GetProcessById(token.Pid); }
                    catch (ArgumentException) { continue; }

                    if (!p.HasExited)
                    {
                        p.PriorityClass = token.OriginalCpuPriority;
                        IreNativeInterop.SetIoPriority(token.Pid, token.OriginalIoPriority, out _);
                    }
                }
                catch (Exception ex) { _logger?.LogWarning($"[IRE] Erro ao restaurar boost {token.ProcessName}: {ex.Message}"); }
            }
            _activeBoosts.Clear();
            RestoreIoThrottled();
        }

        // =====================================================================
        // HELPERS
        // =====================================================================

        private int DetermineInterval(SystemTelemetrySnapshot snapshot)
        {
            if (snapshot.IsCpuHigh(_config.CpuThresholdPercent) ||
                snapshot.IsDiskContended(_config.DiskQueueThreshold))
                return _config.AdaptiveIntervalHighLoadMs;

            if (snapshot.HasRecentInput(1000))
                return _config.AdaptiveIntervalMs;

            return _config.AdaptiveIntervalIdleMs;
        }

        // Cache de processos para evitar Process.GetProcesses() a cada ciclo do adaptive loop
        private Process[] _cachedProcesses = [];
        private DateTime _lastProcessRefresh = DateTime.MinValue;
        private const int ProcessCacheIntervalMs = 2000;

        private Process[] GetProcessesSafe()
        {
            try
            {
                // Reutiliza o cache se ainda estiver dentro do intervalo
                if ((DateTime.UtcNow - _lastProcessRefresh).TotalMilliseconds < ProcessCacheIntervalMs
                    && _cachedProcesses.Length > 0)
                    return _cachedProcesses;

                _cachedProcesses = Process.GetProcesses();
                _lastProcessRefresh = DateTime.UtcNow;
                return _cachedProcesses;
            }
            catch { return _cachedProcesses.Length > 0 ? _cachedProcesses : []; }
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        public void Dispose()
        {
            _ = StopAsync();
            _telemetry.Dispose();
            _loopCts?.Dispose();
        }
    }
}
