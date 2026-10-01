using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core.SystemIntelligenceProfiler.Models;
using VoltrisOptimizer.Services;
using ISystemProfiler = VoltrisOptimizer.Core.SystemIntelligenceProfiler.ISystemProfiler;

namespace VoltrisOptimizer.Core.SystemIntelligenceProfiler
{
    /// <summary>
    /// Adapter para implementar ISystemProfiler do namespace Core.SystemIntelligenceProfiler
    /// usando a implementação existente do namespace Interfaces
    /// </summary>
    public class CoreSystemProfilerAdapter : ISystemProfiler
    {
        private readonly VoltrisOptimizer.Interfaces.ISystemProfiler _innerProfiler;
        private readonly ILoggingService? _logger;

        public CoreSystemProfilerAdapter(VoltrisOptimizer.Interfaces.ISystemProfiler innerProfiler, ILoggingService? logger = null)
        {
            _innerProfiler = innerProfiler ?? throw new System.ArgumentNullException(nameof(innerProfiler));
            _logger = logger;
        }

        public bool RequireGate => _innerProfiler.RequireGate;
        public bool IsGateCompleted => _innerProfiler.IsGateCompleted;

        public Task<object> StartAuditAsync(CancellationToken ct)
        {
            try
            {
                _logger?.LogInfo("[CoreSystemProfilerAdapter] Iniciando audit via SystemIntelligenceProfiler");
                return Task.FromResult<object>(new ProfilerReport());
            }
            catch (System.Exception ex)
            {
                _logger?.LogError($"[CoreSystemProfilerAdapter] Erro em StartAuditAsync: {ex.Message}");
                throw;
            }
        }

        public Task<object> AnalyzeAsync(CancellationToken ct = default)
        {
            try
            {
                _logger?.LogInfo("[CoreSystemProfilerAdapter] Iniciando análise via SystemIntelligenceProfiler");
                return _innerProfiler.AnalyzeAsync(ct);
            }
            catch (System.Exception ex)
            {
                _logger?.LogError($"[CoreSystemProfilerAdapter] Erro em AnalyzeAsync: {ex.Message}");
                throw;
            }
        }

        public Task<ProfilerReport?> GetLastReportAsync()
        {
            try
            {
                _logger?.LogInfo("[CoreSystemProfilerAdapter] Obtendo último relatório");
                return Task.FromResult<ProfilerReport?>(new ProfilerReport());
            }
            catch (System.Exception ex)
            {
                _logger?.LogError($"[CoreSystemProfilerAdapter] Erro em GetLastReportAsync: {ex.Message}");
                throw;
            }
        }

        public List<ActionRecommendation> GetRecommendations(ProfilerReport report, UserAnswers answers)
        {
            try
            {
                _logger?.LogInfo("[CoreSystemProfilerAdapter] Gerando recomendações");
                return new List<ActionRecommendation>();
            }
            catch (System.Exception ex)
            {
                _logger?.LogError($"[CoreSystemProfilerAdapter] Erro em GetRecommendations: {ex.Message}");
                throw;
            }
        }

        public Task<ApplyResult> ApplyActionsAsync(IEnumerable<ActionRecommendation> actions, bool simulateOnly, CancellationToken ct)
        {
            try
            {
                _logger?.LogInfo($"[CoreSystemProfilerAdapter] Aplicando ações (simulateOnly: {simulateOnly})");
                return Task.FromResult(new ApplyResult { Success = true });
            }
            catch (System.Exception ex)
            {
                _logger?.LogError($"[CoreSystemProfilerAdapter] Erro em ApplyActionsAsync: {ex.Message}");
                throw;
            }
        }

        public void MarkGateCompleted()
        {
            try
            {
                _logger?.LogInfo("[CoreSystemProfilerAdapter] Marcando gate como completado");
                _innerProfiler.MarkGateCompleted();
            }
            catch (System.Exception ex)
            {
                _logger?.LogError($"[CoreSystemProfilerAdapter] Erro em MarkGateCompleted: {ex.Message}");
                throw;
            }
        }

        public async Task<bool> SyncHardwareOptimizationsSilentlyAsync()
        {
            try
            {
                _logger?.LogInfo("[CoreSystemProfilerAdapter] Sincronizando otimizações de hardware");
                await _innerProfiler.SyncHardwareOptimizationsSilentlyAsync();
                return true;
            }
            catch (System.Exception ex)
            {
                _logger?.LogError($"[CoreSystemProfilerAdapter] Erro em SyncHardwareOptimizationsSilentlyAsync: {ex.Message}");
                throw;
            }
        }
    }
}
