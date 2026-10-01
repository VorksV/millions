using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Gamer.Pipeline;
using VoltrisOptimizer.Services.Gamer.Models;
using VoltrisOptimizer.Services.Gamer.Pipeline.Models;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer.Orchestrator
{
    public class GamerOrchestrator : IGamerOrchestrator, IDisposable
    {
        private readonly ILoggingService _logger;
        private readonly IGamerPipeline _pipeline;
        private readonly object _lock = new();

        private GamerSessionContext? _currentSession;
        private CancellationTokenSource? _sessionCts;
        private bool _isDisposed;

        public bool IsActive
        {
            get
            {
                _logger.LogEntry(nameof(IsActive));
                _logger.LogDebug("[GamerOrchestrator.IsActive] Entry");
                lock (_lock)
                {
                    var result = _currentSession?.Decisions.Any(d => d.Applied) == true;
                    _logger.LogDebug($"[GamerOrchestrator.IsActive] Exit: {result}");
                    _logger.LogExit(nameof(IsActive), result);
                    return result;
                }
            }
        }

        public GamerSessionContext? CurrentSession
        {
            get
            {
                _logger.LogEntry(nameof(CurrentSession));
                _logger.LogDebug("[GamerOrchestrator.CurrentSession] Entry");
                lock (_lock)
                {
                    _logger.LogDebug("[GamerOrchestrator.CurrentSession] Exit");
                    _logger.LogExit(nameof(CurrentSession));
                    return _currentSession;
                }
            }
        }

        public event EventHandler<GamerSessionContext>? SessionStarted;
        public event EventHandler<GamerSessionContext>? SessionCompleted;

        public GamerOrchestrator(ILoggingService logger, IGamerPipeline pipeline)
        {
            _logger.LogEntry(nameof(GamerOrchestrator));
            _logger = logger;
            _pipeline = pipeline;
            _logger.LogInfo("[GamerOrchestrator] 🎮 Orquestrador inteligente inicializado");
            _logger.LogExit(nameof(GamerOrchestrator));
        }

        public async Task<bool> StartSessionAsync(
            string? gameExecutable = null,
            GamerOptimizationOptions? options = null,
            CancellationToken ct = default)
        {
            _logger.LogEntry(nameof(StartSessionAsync));
            _logger.LogInfo("[GamerOrchestrator.StartSessionAsync] Entry");

            // PRO-ENFORCEMENT FAIL-CLOSED: sessao gamer so inicia com licenca ativa.
            if (!VoltrisOptimizer.Services.License.ProFeatureGuard.IsPaidActive())
            {
                _logger.LogWarning("[GamerOrchestrator] Sessao bloqueada por licenca (fail-closed).");
                _logger.LogExit(nameof(StartSessionAsync), false);
                return false;
            }

            lock (_lock)
            {
                if (IsActive)
                {
                    _logger.LogWarning("[GamerOrchestrator] ⚠ Sessão já está ativa");
                    _logger.LogInfo("[GamerOrchestrator.StartSessionAsync] Exit (already active)");
                    _logger.LogExit(nameof(StartSessionAsync), false);
                    return false;
                }

                _sessionCts?.Cancel();
                _sessionCts?.Dispose();
                _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            }

            try
            {
                _logger.LogInfo("""
╔══════════════════════════════════╗
║  🎮 MODO GAMER — INICIANDO       ║
╚══════════════════════════════════╝
""");

                var linkedCt = _sessionCts.Token;

                var session = await _pipeline.ExecuteAsync(gameExecutable, options ?? new GamerOptimizationOptions(), linkedCt);

                lock (_lock)
                {
                    _currentSession = session;
                }

                SessionStarted?.Invoke(this, session);

                int applied = session.Decisions.Count(d => d.Applied);
                int validated = session.Results.Count(r => r.PassedValidation);

                if (applied > 0)
                {
                    _logger.LogSuccess($"[GamerOrchestrator] ✅ Sessão iniciada: {applied} otimizações, {validated} validadas");

                    // Priority: Voltris process goes BelowNormal so game gets CPU priority
                    try
                    {
                        var self = Process.GetCurrentProcess();
                        self.PriorityClass = ProcessPriorityClass.BelowNormal;
                        _logger.LogInfo("[GamerOrchestrator] 🔻 Voltris rebaixado para BelowNormal — prioridade para o jogo");
                    }
                    catch { }

                    _logger.LogExit(nameof(StartSessionAsync), true);
                    return true;
                }

                _logger.LogWarning("[GamerOrchestrator] ⚠ Sessão iniciada sem otimizações aplicadas");
                _logger.LogInfo("[GamerOrchestrator.StartSessionAsync] Exit (no optimizations)");
                _logger.LogExit(nameof(StartSessionAsync), false);
                return false;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("[GamerOrchestrator] ⏹ Sessão cancelada");
                _logger.LogInfo("[GamerOrchestrator.StartSessionAsync] Exit (cancelled)");
                _logger.LogExit(nameof(StartSessionAsync), false);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GamerOrchestrator] ❌ Erro ao iniciar sessão: {ex.Message}");
                await EmergencyRollbackAsync();
                _logger.LogInfo("[GamerOrchestrator.StartSessionAsync] Exit (error)");
                _logger.LogExit(nameof(StartSessionAsync), false);
                return false;
            }
        }

        public async Task EndSessionAsync(CancellationToken ct = default)
        {
            _logger.LogEntry(nameof(EndSessionAsync));
            _logger.LogInfo("[GamerOrchestrator.EndSessionAsync] Entry");
            lock (_lock)
            {
                if (_currentSession == null)
                {
                    _logger.LogWarning("[GamerOrchestrator] ⚠ Nenhuma sessão ativa para encerrar");
                    _logger.LogInfo("[GamerOrchestrator.EndSessionAsync] Exit (no session)");
                    _logger.LogExit(nameof(EndSessionAsync));
                    return;
                }
            }

            _logger.LogInfo("""
╔══════════════════════════════════╗
║  🛑 MODO GAMER — ENCERRANDO      ║
╚══════════════════════════════════╝
""");

            GamerSessionContext? session;
            lock (_lock)
            {
                session = _currentSession;
            }

            if (session != null)
            {
                var applied = session.Decisions.Where(d => d.Applied && d.HasRollback).ToList();

                _logger.LogInfo($"[GamerOrchestrator] 🔄 Revertendo {applied.Count} otimizações...");

                foreach (var decision in applied)
                {
                    try
                    {
                        ct.ThrowIfCancellationRequested();
                        _logger.LogInfo($"[GamerOrchestrator] ↩ Revertendo: {decision.DisplayName}...");
                        await decision.RollbackAsync(ct);
                        _logger.LogInfo($"[GamerOrchestrator] ✅ {decision.DisplayName} revertido");
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"[GamerOrchestrator] ⚠ Falha ao reverter {decision.DisplayName}: {ex.Message}");
                    }
                }
            }

            _sessionCts?.Cancel();

            lock (_lock)
            {
                SessionCompleted?.Invoke(this, _currentSession!);
                _currentSession = null;
            }

            _logger.LogSuccess("[GamerOrchestrator] ✅ Modo gamer encerrado com sucesso");
            _logger.LogInfo("[GamerOrchestrator.EndSessionAsync] Exit");
            _logger.LogExit(nameof(EndSessionAsync));
        }

        public async Task EmergencyRollbackAsync()
        {
            _logger.LogEntry(nameof(EmergencyRollbackAsync));
            _logger.LogInfo("[GamerOrchestrator.EmergencyRollbackAsync] Entry");
            _logger.LogCritical("""
╔══════════════════════════════════╗
║  🚨 ROLLBACK DE EMERGÊNCIA       ║
╚══════════════════════════════════╝
""");

            GamerSessionContext? session;
            lock (_lock)
            {
                session = _currentSession;
            }

            if (session != null)
            {
                var applied = session.Decisions.Where(d => d.Applied && d.HasRollback).ToList();

                foreach (var decision in applied)
                {
                    try
                    {
                        _logger.LogInfo($"[EmergencyRollback] ↩ Revertendo: {decision.DisplayName}...");
                        await decision.RollbackAsync(CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"[EmergencyRollback] ❌ Erro revertendo {decision.DisplayName}: {ex.Message}");
                    }
                }
            }

            lock (_lock)
            {
                _currentSession = null;
            }

            _logger.LogSuccess("[GamerOrchestrator] ✅ Rollback de emergência concluído");
            _logger.LogInfo("[GamerOrchestrator.EmergencyRollbackAsync] Exit");
            _logger.LogExit(nameof(EmergencyRollbackAsync));
        }

        public void Dispose()
        {
            _logger.LogEntry(nameof(Dispose));
            _logger.LogDebug("[GamerOrchestrator.Dispose] Entry");
            if (!_isDisposed)
            {
                _sessionCts?.Cancel();
                _sessionCts?.Dispose();
                _isDisposed = true;
            }
            _logger.LogDebug("[GamerOrchestrator.Dispose] Exit");
            _logger.LogExit(nameof(Dispose));
        }
    }
}
