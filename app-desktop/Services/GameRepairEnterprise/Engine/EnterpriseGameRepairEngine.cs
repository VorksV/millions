using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.GameRepairEnterprise.Interfaces;
using VoltrisOptimizer.Services.GameRepairEnterprise.Models;

namespace VoltrisOptimizer.Services.GameRepairEnterprise.Engine
{
    public interface IEnterpriseGameRepairEngine
    {
        Task<GameDependencyRepairResult> ExecutePipelineAsync(IGameDependencyProvider provider, GameDependencyDiagnosis issue, IProgress<(GameDependencyState state, int percentage, string message)> progress, CancellationToken ct);
    }

    public class EnterpriseGameRepairEngine : IEnterpriseGameRepairEngine
    {
        private readonly ILoggingService _logger;

        public EnterpriseGameRepairEngine(ILoggingService logger)
        {
            _logger = logger;
        }

        public async Task<GameDependencyRepairResult> ExecutePipelineAsync(IGameDependencyProvider provider, GameDependencyDiagnosis issue, IProgress<(GameDependencyState state, int percentage, string message)> progress, CancellationToken ct)
        {
            var result = new GameDependencyRepairResult();
            var sw = new Stopwatch();

            try
            {
                _logger.LogInfo($"[EnterpriseRepair] Início do Pipeline Atômico para: {issue.ComponentId} ({provider.ProviderName})");
                
                // 1. Diagnóstico e 2. Snapshot ocorrem dentro do próprio Provider.DiagnoseAsync, mas
                // neste ponto assumimos que a Issue já foi diagnosticada.
                // O reparo engloba: Download (3), Validação Download (4), Instalação (5), Estabilização (6)
                
                sw.Start();
                progress?.Report((GameDependencyState.Downloading, 0, $"Iniciando preparativos para {issue.Title}..."));
                
                // Executa reparo (inclui 3, 4, 5, 6)
                var repairResult = await provider.RepairAsync(issue, progress, ct);
                
                if (!repairResult.Success)
                {
                    throw new Exception(repairResult.Message);
                }
                
                result.DownloadTimeMs = repairResult.DownloadTimeMs;
                result.InstallTimeMs = repairResult.InstallTimeMs;
                
                // 7. Validação Técnica (Independente do ExitCode)
                _logger.LogInfo($"[EnterpriseRepair] Iniciando Validação Técnica para: {issue.ComponentId}");
                progress?.Report((GameDependencyState.Validating, 90, $"Validando instalação de {issue.Title}..."));
                
                long valStart = sw.ElapsedMilliseconds;
                bool isFullyFunctional = await provider.ValidateAsync(issue, ct);
                result.ValidationTimeMs = sw.ElapsedMilliseconds - valStart;

                if (!isFullyFunctional)
                {
                    throw new Exception("Validação Técnica falhou. O componente não passou nas evidências operacionais após instalação.");
                }

                // 8. Atualização de Interface, 9. Persistência, 10. Aprendizado
                // Isso será feito pelo consumidor final (ViewModel) que recebe o success.
                
                result.Success = true;
                result.Message = LocalizationService.Instance.GetString("GameRepairCertifiedSuccess");
                
                progress?.Report((GameDependencyState.Repaired, 100, "Componente funcional."));
                _logger.LogInfo($"[EnterpriseRepair] Pipeline finalizado com SUCESSO para: {issue.ComponentId}");
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning($"[EnterpriseRepair] Pipeline CANCELADO pelo usuário para: {issue.ComponentId}");
                result.Success = false;
                result.Message = LocalizationService.Instance.GetString("GameRepairCancelledByUser");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[EnterpriseRepair] Pipeline FALHOU para {issue.ComponentId}. Motivo: {ex.Message}");
                result.Success = false;
                result.Message = ex.Message;
                
                // 11. Rollback Atômico
                try
                {
                    _logger.LogWarning($"[EnterpriseRepair] Iniciando Rollback granular para: {issue.ComponentId}");
                    await provider.RollbackAsync(issue, CancellationToken.None); // Não cancelar o rollback
                    result.RequiredRollback = true;
                    result.RollbackReason = "Falha durante o pipeline de reparo.";
                    _logger.LogInfo($"[EnterpriseRepair] Rollback finalizado com sucesso.");
                }
                catch (Exception rEx)
                {
                    _logger.LogError($"[EnterpriseRepair] ERRO CRÍTICO no Rollback para {issue.ComponentId}: {rEx.Message}");
                }
                
                progress?.Report((GameDependencyState.Failed, 100, $"Falha: {ex.Message}"));
            }
            finally
            {
                sw.Stop();
            }

            return result;
        }
    }
}
