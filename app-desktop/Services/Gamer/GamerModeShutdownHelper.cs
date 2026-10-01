using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using VoltrisOptimizer.Services.Gamer.GamerModeManager;
using VoltrisOptimizer.Services.Gamer.Interfaces;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer
{
    /// <summary>
    /// Encerramento determinístico do ecossistema Modo Gamer (Real Boost, Manager legado, Orquestrador).
    /// Deve ser chamado de forma <b>síncrona</b> antes de <see cref="System.Windows.Application.Shutdown"/>,
    /// <c>Dispatcher.InvokeShutdown</c> ou <c>Dispose</c> do <see cref="IServiceProvider"/>, com timeout explícito.
    /// </summary>
    public static class GamerModeShutdownHelper
    {
        private static int _inProgress;

        /// <summary>
        /// Desativa RealGameBooster, depois <see cref="IGamerModeOrchestrator"/>, depois <see cref="IGamerModeManager"/> se ativos.
        /// Ordem alinhada ao <see cref="UI.ViewModels.GamerViewModel.DeactivateGamerModeAsync"/>.
        /// CORREção CRÍTICA: Timeouts reduzidos para evitar travamento ao fechar
        /// </summary>
        public static void ShutdownAllBlocking(IServiceProvider? services, ILoggingService? log, string reason, int timeoutMs = 15000) // CORREção: Default aumentado para 15000ms para evitar timeouts precipitados
        {
            if (services == null)
            {
                log?.LogWarning("[GamerShutdown] IServiceProvider null não possível resolver serviços.");
                return;
            }

            if (Interlocked.CompareExchange(ref _inProgress, 1, 0) != 0)
            {
                log?.LogWarning($"[GamerShutdown] Chamada concorrente ignorada (reason = {reason})");
                return;
            }

            var swTotal = Stopwatch.StartNew();

            try
            {
                var pid = Process.GetCurrentProcess().Id;
                log?.LogInfo($"[GamerShutdown] INÍCIO pid = {pid} reason = {reason} timeout = {timeoutMs} ms");

                // CORREção: Usar tempo restante em vez de limites fixos rígidos para evitar abortar o Orchestrator prematuramente
                int GetRemaining() => Math.Max(500, timeoutMs - (int)swTotal.ElapsedMilliseconds);

                var boosterTimeout = Math.Min(2000, GetRemaining());

                try
                {
                    // 1) Real Game Booster (timer/plano/flags próprios)
                    try
                    {
                        var booster = services.GetService(typeof(IRealGameBoosterService)) as IRealGameBoosterService;

                        if (booster == null)
                            log?.LogInfo("[GamerShutdown] (1) IRealGameBoosterService não registrado.");
                        else if (!booster.IsActive)
                            log?.LogInfo("[GamerShutdown] (1) RealGameBooster já inativo.");
                        else
                        {
                            log?.LogInfo("[GamerShutdown] (1) RealGameBooster IsActive = true DeactivateAsync");
                            var sw = Stopwatch.StartNew();
                            using var cts = new CancellationTokenSource(boosterTimeout);
                            var task = Task.Run(async () => await booster.DeactivateAsync(cts.Token));

                            if (!task.Wait(boosterTimeout))
                                log?.LogError($"[GamerShutdown] (1) TIMEOUT após {boosterTimeout} ms RealGameBooster pode não ter revertido tudo.");
                            else if (task.IsFaulted)
                                log?.LogError($"[GamerShutdown] (1) Falha: {task.Exception?.GetBaseException()?.Message}", task.Exception?.GetBaseException());
                            else
                                log?.LogInfo($"[GamerShutdown] (1) RealGameBooster OK em {sw.ElapsedMilliseconds} ms (result = {task.Result})");
                        }
                    }
                    catch (Exception ex)
                    {
                        log?.LogError($"[GamerShutdown] (1) Exceção: {ex.Message}", ex);
                    }

                    // 2) Orquestrador principal (restore pre-optimizer, overlay, power, prioridades, Brain bridge)
                    try
                    {
                        var orch = services.GetService(typeof(IGamerModeOrchestrator)) as IGamerModeOrchestrator;

                        if (orch == null)
                            log?.LogInfo("[GamerShutdown] (2) IGamerModeOrchestrator não registrado.");
                        else
                        {
                            log?.LogInfo($"[GamerShutdown] (2) Orchestrator presente IsActive = {orch.IsActive}");

                            if (orch.IsActive)
                            {
                                log?.LogInfo("[GamerShutdown] (2) DeactivateAsync (bloqueante)");
                                var sw = Stopwatch.StartNew();
                                var orchTimeoutVal = GetRemaining();
                                using var cts = new CancellationTokenSource(orchTimeoutVal);
                                var task = Task.Run(async () => await orch.DeactivateAsync(null, cts.Token));

                                if (!task.Wait(orchTimeoutVal))
                                    log?.LogError($"[GamerShutdown] (2) TIMEOUT após {orchTimeoutVal} ms revert parcial possível.");
                                else if (task.IsFaulted)
                                    log?.LogError($"[GamerShutdown] (2) Falha: {task.Exception?.GetBaseException()?.Message}", task.Exception?.GetBaseException());
                                else
                                    log?.LogInfo($"[GamerShutdown] (2) Orchestrator OK em {sw.ElapsedMilliseconds} ms (result = {task.Result})");
                            }
                            else
                                log?.LogInfo("[GamerShutdown] (2) Orchestrator já inativo.");
                        }
                    }
                    catch (Exception ex)
                    {
                        log?.LogError($"[GamerShutdown] (2) Exceção: {ex.Message}", ex);
                    }

                    // 3) GamerModeManager (dashboard/pipeline legado), se ativo em paralelo
                    try
                    {
                        var mgr = services.GetService(typeof(IGamerModeManager)) as IGamerModeManager;

                        if (mgr == null)
                            log?.LogInfo("[GamerShutdown] (3) IGamerModeManager não registrado.");
                        else if (!mgr.IsActive)
                            log?.LogInfo("[GamerShutdown] (3) GamerModeManager já inativo.");
                        else
                        {
                            log?.LogInfo("[GamerShutdown] (3) IGamerModeManager IsActive = true DeactivateAsync");
                            var sw = Stopwatch.StartNew();
                            var mgrTimeoutVal = GetRemaining();
                            using var cts = new CancellationTokenSource(mgrTimeoutVal);
                            var task = Task.Run(async () => await mgr.DeactivateAsync(cts.Token));

                            if (!task.Wait(mgrTimeoutVal))
                                log?.LogError($"[GamerShutdown] (3) TIMEOUT após {mgrTimeoutVal} ms.");
                            else if (task.IsFaulted)
                                log?.LogError($"[GamerShutdown] (3) Falha: {task.Exception?.GetBaseException()?.Message}", task.Exception?.GetBaseException());
                            else
                                log?.LogInfo($"[GamerShutdown] (3) GamerModeManager OK em {sw.ElapsedMilliseconds} ms (result = {task.Result})");
                        }
                    }
                    catch (Exception ex)
                    {
                        log?.LogError($"[GamerShutdown] (3) Exceção: {ex.Message}", ex);
                    }
                }
                finally
                {
                    swTotal.Stop();
                    log?.LogInfo($"[GamerShutdown] FIM reason = {reason} elapsed = {swTotal.ElapsedMilliseconds} ms");
                }
            }
            finally
            {
                Interlocked.Exchange(ref _inProgress, 0);
            }
        }
    }
}
 
