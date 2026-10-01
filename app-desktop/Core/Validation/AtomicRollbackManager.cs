using System;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core.Configuration;
using VoltrisOptimizer.Core.Execution;
using VoltrisOptimizer.Core.Logging;

namespace VoltrisOptimizer.Core.Validation
{
    /// <summary>
    /// Gerencia o rollback automático de ações cujas validações de evidência falharam.
    /// </summary>
    public sealed class AtomicRollbackManager
    {
        private static readonly Lazy<AtomicRollbackManager> _instance = new(() => new AtomicRollbackManager());
        public static AtomicRollbackManager Instance => _instance.Value;

        /// <summary>
        /// [FIX:M-5] Gate de rollback compartilhado por TODO o processo.
        ///
        /// BUG ORIGINAL: existem DOIS motores de rollback independentes neste
        /// projeto, ambos ativos ao mesmo tempo:
        ///   1. este (Core.Validation), reverte uma única OptimizationAction;
        ///   2. Services.Gamer.Implementation.AtomicRollbackManager (518 linhas),
        ///      mantém um registro próprio de estados aplicados e reverte todos.
        ///
        /// Cada um tem o seu próprio SemaphoreSlim e o seu próprio flag
        /// _isRollbackInProgress, e NENHUM deles enxerga o estado do outro. Na
        /// prática, o ExecutionQueue.cs dispara o motor (1) enquanto o
        /// GamerOptimizationCoordinator está no motor (2) — os dois reverts
        /// interleavam sobre o mesmo estado do sistema sem qualquer coordenação.
        ///
        /// Como as ações de otimizaçãotocam registro, serviços e jobs do
        /// Windows, dois reverts simultâneos podem deixar o sistema num estado
        /// intermediário que nenhum dos dois knew how desfazer. Este gate
        /// serializa os dois motores; não muda o que é revertido, só impede
        /// quehappam ao mesmo tempo.
        ///
        /// É público de propósito: o outro motor o consome. Namespace Core de
        /// propósito: é o único ponto neutro entre Services e Core.
        /// </summary>
        public static readonly SemaphoreSlim GlobalRollbackGate = new(1, 1);

        private AtomicRollbackManager() { }

        /// <summary>
        /// Invoca o rollback de forma segura e registra no log.
        /// </summary>
        public async Task ExecuteRollbackAsync(OptimizationAction action, string reason)
        {
            if (!VoltrisFeatureFlags.Instance.UseAtomicRollback)
            {
                // Sem atomic rollback, o estado modificado permanece (legado)
                return;
            }

            try
            {
                ProfessionalLogger.Instance.LogSystemState("AtomicRollbackManager", $"Revertendo {action.ActionName} devido a: {reason}");

                // [FIX:M-5] Serializar com o outro motor de rollback do processo.
                var aguardouGate = !GlobalRollbackGate.Wait(0);
                if (aguardouGate)
                {
                    // Não se pode falhar aqui: um rollback pendente é justamente
                    // o caminho de recuperação, então espera em vez de abortar.
                    ProfessionalLogger.Instance.LogSystemState(
                        "AtomicRollbackManager",
                        $"Outro rollback em andamento; aguardando o gate antes de reverter {action.ActionName}");
                    GlobalRollbackGate.Wait();
                }

                try
                {
                    ProfessionalLogger.Instance.LogSystemState(
                        "AtomicRollbackManager",
                        $"[FIX:M-5] Adquiriu o gate global de rollback (motor=Core.Validation, waited={aguardouGate}) para {action.ActionName}");
                    await action.RollbackAsync();
                }
                finally
                {
                    // O gate foi adquirido nos dois caminhos (Wait(0) bem-sucedido
                    // ou Wait() bloqueante), então o Release é incondicional.
                    GlobalRollbackGate.Release();
                }
            }
            catch (Exception ex)
            {
                ProfessionalLogger.Instance.LogError("AtomicRollbackManager", $"Falha crítica ao reverter {action.ActionName}", ex);
                // Aqui no futuro podemos integrar com o SmartRepairService caso o rollback falhe.
            }
        }
    }
}
