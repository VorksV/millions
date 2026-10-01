using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Gamer.Pipeline.Models;

namespace VoltrisOptimizer.Services.Gamer.Pipeline.Stages
{
    public class DecisionStage : IPipelineStage
    {
        private readonly ILoggingService _logger;
        private readonly IEnumerable<IOptimizationProvider> _providers;

        public string Name => "Decision";
        public int Order => 7;

        public DecisionStage(ILoggingService logger, IEnumerable<IOptimizationProvider> providers)
        {
            _logger = logger;
            _providers = providers;
        }

        public Task ExecuteAsync(GamerSessionContext context, CancellationToken ct)
        {
            _logger.LogEntry(nameof(ExecuteAsync));
            _logger.LogInfo($"[Pipeline] [GO] Estágio 7/11: Decision — selecionando otimizações via {_providers.Count()} providers...");

            var decisions = new List<OptimizationDecision>();
            var userOpts = context.UserOptions;

            foreach (var provider in _providers)
            {
                try
                {
                    if (!provider.CanHandle(context))
                    {
                        _logger.LogDebug($"[Decision] ⏭ {provider.DisplayName}: não aplicável neste contexto");
                        continue;
                    }

                    double benefit = provider.CalculateBenefitScore(context);
                    if (benefit < 2)
                    {
                        _logger.LogDebug($"[Decision] ⏭ {provider.DisplayName}: benefício muito baixo ({benefit:F1}/10)");
                        continue;
                    }

                    var providerRef = provider;
                    decisions.Add(new OptimizationDecision
                    {
                        Id = provider.Id,
                        DisplayName = provider.DisplayName,
                        Category = provider.Category,
                        ExpectedBenefitScore = benefit,
                        RiskScore = provider.BaseRiskScore,
                        HasRollback = provider.HasRollback,
                        ApplyAsync = async (t) => await providerRef.ApplyAsync(t),
                        RollbackAsync = async (t) => await providerRef.RollbackAsync(t)
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[Decision] ❌ Erro avaliando provider {provider.Id}: {ex.Message}");
                }
            }

            if (userOpts.CloseBackgroundApps && context.System.RunningProcessCount > 80)
            {
                decisions.Add(new OptimizationDecision
                {
                    Id = "close-background-apps",
                    DisplayName = "Fechar Apps em Segundo Plano",
                    Category = "Process",
                    ExpectedBenefitScore = 4,
                    RiskScore = 1,
                    ApplyAsync = _ => CloseNonEssentialProcessesAsync(),
                    RollbackAsync = _ => Task.FromResult(true)
                });
            }

            decisions = decisions
                .OrderByDescending(d => d.ExpectedBenefitScore)
                .ThenBy(d => d.RiskScore)
                .ToList();

            context.Decisions = decisions;

            _logger.LogInfo($"[Decision] 📋 {decisions.Count} otimizações selecionadas:");
            foreach (var d in decisions)
            {
                _logger.LogInfo($"[Decision]   [{d.Category}] {d.DisplayName} (benefício: {d.ExpectedBenefitScore:F1}/10, risco: {d.RiskScore}/10)");
            }

            _logger.LogExit(nameof(ExecuteAsync));
            return Task.CompletedTask;
        }

        private static async Task<bool> CloseNonEssentialProcessesAsync()
        {
            return await Task.Run(() =>
            {
                var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "explorer", "svchost", "csrss", "wininit", "winlogon",
                    "services", "lsass", "fontdrvhost", "spoolsv", "dwm",
                    "nvdisplay.container", "amdow", "igfx"
                };

                int closed = 0;
                foreach (var proc in System.Diagnostics.Process.GetProcesses()
                    .Where(p =>
                    {
                        try { return !keep.Contains(p.ProcessName) && p.SessionId == 1 && p.WorkingSet64 > 10 * 1024 * 1024; }
                        catch { return false; }
                    })
                    .Take(20))
                {
                    try
                    {
                        if (proc.MainWindowHandle == IntPtr.Zero)
                        {
                            proc.Kill();
                            closed++;
                        }
                    }
                    catch { }
                    finally { proc.Dispose(); }
                }

                return closed > 0;
            });
        }
    }
}
