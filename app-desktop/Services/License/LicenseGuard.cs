using System;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.License.Interfaces;
using VoltrisOptimizer.Services.License.Models;
using VoltrisOptimizer.Services.License.Exceptions;

namespace VoltrisOptimizer.Services.License
{
    /// <summary>
    /// LicenseGuard — Application Layer (enforcement real).
    /// Garante bloqueio mesmo que a UI falhe.
    /// FAIL-CLOSED: qualquer exceção interna = bloqueia.
    /// </summary>
    public class LicenseGuard : ILicenseGuard
    {
        private readonly ILicenseService _licenseService;

        public LicenseGuard(ILicenseService licenseService)
        {
            _licenseService = licenseService ?? throw new ArgumentNullException(nameof(licenseService));
            App.LoggingService?.LogInfo("[LicenseGuard] Instância criada");
        }

        public async Task<T> ExecuteAsync<T>(string feature, Func<Task<T>> operation)
        {
            App.LoggingService?.LogTrace($"[LicenseGuard] ExecuteAsync<T>({feature}) chamado");
            LicenseCheckResult checkResult;
            try
            {
                checkResult = await _licenseService.CheckFeatureAccessAsync(feature);
            }
            catch (Exception ex)
            {
                // FAIL-CLOSED: erro na verificação = bloqueia
                App.LoggingService?.LogError($"[LicenseGuard] EXCEÇÃO ao verificar acesso para '{feature}' — BLOQUEANDO por segurança", ex);
                throw new LicenseBlockedException(
                    new LicenseCheckResult
                    {
                        IsAllowed = false,
                        Feature = feature,
                        BlockReason = $"Erro interno ao verificar licença: {ex.Message}",
                        State = new Models.LicenseState()
                    });
            }

            if (!checkResult.IsAllowed)
            {
                App.LoggingService?.LogWarning($"[LicenseGuard] '{feature}' BLOQUEADO: {checkResult.BlockReason}");
                ProEngagementTracker.RecordClick(feature);
                throw new LicenseBlockedException(checkResult);
            }

            App.LoggingService?.LogTrace($"[LicenseGuard] '{feature}' LIBERADO — executando operação");
            try
            {
                return await operation();
            }
            catch (OperationCanceledException)
            {
                // Cancelamento NÃO é erro. O botão circular é cancelável, e o
                // cancelamento chega aqui como OperationCanceledException do
                // CancellationToken. Antes ele era registrado como
                // "[LicenseGuard] Erro na execução de 'master'" com stack completa,
                // o que: (1) poluía o log com ERROR em cancelamento intencional,
                // (2) mascarava o cancelamento como falha do LicenseGuard, e
                // (3) engolia o tratamento de cancelamento que existe em
                // MasterActionAsync — as etapas de rede e otimização nunca
                // executavam e o usuário via "quebrou".
                App.LoggingService?.LogInfo($"[LicenseGuard] '{feature}' cancelado pelo usuário (não é erro).");
                throw;
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[LicenseGuard] Erro na execução de '{feature}': {ex.GetType().Name}: {ex.Message}", ex);
                throw;
            }
        }

        public async Task ExecuteAsync(string feature, Func<Task> operation)
        {
            App.LoggingService?.LogTrace($"[LicenseGuard] ExecuteAsync(void)({feature}) chamado");
            LicenseCheckResult checkResult;
            try
            {
                checkResult = await _licenseService.CheckFeatureAccessAsync(feature);
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[LicenseGuard] EXCEÇÃO ao verificar acesso para '{feature}' — BLOQUEANDO por segurança", ex);
                throw new LicenseBlockedException(
                    new LicenseCheckResult
                    {
                        IsAllowed = false,
                        Feature = feature,
                        BlockReason = $"Erro interno ao verificar licença: {ex.Message}",
                        State = new Models.LicenseState()
                    });
            }

            if (!checkResult.IsAllowed)
            {
                App.LoggingService?.LogWarning($"[LicenseGuard] '{feature}' BLOQUEADO: {checkResult.BlockReason}");
                throw new LicenseBlockedException(checkResult);
            }

            App.LoggingService?.LogTrace($"[LicenseGuard] '{feature}' LIBERADO — executando operação");
            try
            {
                await operation();
            }
            catch (OperationCanceledException)
            {
                // [FIX:M-6] Cancelamento NÃO é erro.
                //
                // BUG ORIGINAL: este overload (Func<Task>) NÃO tinha o filtro
                // de OperationCanceledException que o overload genérico já
                // possuía. Todo cancelamento do botão circular caía no
                // catch (Exception) e era registrado como
                //   [ERROR] [LicenseGuard] Erro na execução de 'master'
                // com stack completa — logo depois de a operação ter sido
                // declarada concluída com sucesso ("Análise concluída em 7,4s:
                // 5,45 GB recuperável", TASK COMPLETED, State=Completed).
                //
                // O usuário via um ERROR para uma limpeza de 5,45 GB que tinha
                // funcionado, porque o cancelamento vinha da rotina de limpeza
                // e não da operação em si.
                App.LoggingService?.LogInfo(
                    $"[FIX:M-6] '{feature}' cancelado (não é erro). " +
                    $"O catch genérico não tinha filtro de cancelamento e reportava ERROR após TASK COMPLETED.");
                throw;
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError(
                    $"[LicenseGuard] Erro na execução de '{feature}': {ex.GetType().Name}: {ex.Message}", ex);
                throw;
            }
        }

        public T Execute<T>(string feature, Func<T> operation)
        {
            App.LoggingService?.LogTrace($"[LicenseGuard] Execute<T>({feature}) chamado (síncrono)");
            // CORREÇÃO: GetAwaiter().GetResult() diretamente na thread chamadora pode causar deadlock
            // se o SynchronizationContext da UI thread estiver ativo (ex: chamado de um event handler).
            // Task.Run garante execução em thread pool sem SynchronizationContext, eliminando o risco.
            return Task.Run(() => ExecuteAsync(feature, () => Task.FromResult(operation()))).GetAwaiter().GetResult();
        }

        public void Execute(string feature, Action operation)
        {
            App.LoggingService?.LogTrace($"[LicenseGuard] Execute(void)({feature}) chamado (síncrono)");
            // CORREÇÃO: mesmo motivo acima — desacoplar do SynchronizationContext da UI thread.
            Task.Run(() => ExecuteAsync(feature, () =>
            {
                operation();
                return Task.CompletedTask;
            })).GetAwaiter().GetResult();
        }
    }
}
