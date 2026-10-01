using System;
using VoltrisOptimizer.Core.Models;

namespace VoltrisOptimizer.Services.Power
{
    /// <summary>
    /// [FIX:UNICO-DONO-DE-ENERGIA] O Perfil Inteligente é o ÚNICO dono do plano
    /// de energia do Windows.
    ///
    /// O QUE ESTE SUBSTITUI
    /// ====================
    /// Este é o substituto do <c>PowerPlanOrchestrator.RequestPlan(requester,
    /// planGuid, priority)</c>. A auditoria do projeto inteiro encontrou quinze
    /// chamadores pedindo TROCA DE PLANO, com prioridades que chegavam a
    /// <c>Emergency = 0</c> — ou seja, acima do <c>IntelligentProfile = 3</c>.
    /// Uma chamada de emergência já tirava o plano gerenciado do ar, e o
    /// resultado era o app brigando com ele mesmo: o perfil reassertava, o
    /// outro pedia de volta, e o usuário via o plano oscilar.
    ///
    /// A arbitragem não resolveu o problema porque o problema não era
    /// arbitragem: era existirem quinze jeitos de escolher o plano. Enquanto
    /// qualquer um deles puder escolher, o perfil não é dono de nada.
    ///
    /// POR QUE A ASSINATURA MUDOU
    /// ==========================
    /// Antes: pedia-se um GUID de plano. Agora pede-se que o PERFIL seja
    /// aplicado. A diferença é o ponto inteiro da correção — o plano deixa de
    /// ser algo que se escolhe e passa a ser algo que o perfil deduz, a partir
    /// do tier de capacidade da máquina e do perfil escolhido. Chamar com um
    /// GUID seria devolver a porta que acabamos de fechar.
    ///
    /// O que os chamadores legados fazem agora é pedido para o dono CURRENT
    /// reaplicar o que ele já teria aplicado de qualquer forma. É idempotente
    /// e é a única ação honesta disponível para eles.
    /// </summary>
    public static class ProfilePowerAuthority
    {
        /// <summary>
        /// Pede que o perfil inteligente ativo seja (re)aplicado.
        ///
        /// É o substituto direto de <c>RequestPlan</c> para os componentes que
        /// antigamente escolhiam um plano. Não escolhe plano: pede ao dono que
        /// aplique o dele.
        /// </summary>
        /// <param name="requesterId">Quem pediu. Fica no log para diagnóstico.</param>
        /// <param name="reason">Por quê. Também vai para o log.</param>
        /// <param name="logger">Log opcional; usa o log global quando omitido.</param>
        /// <returns><c>true</c> se o pedido foi aceito e o perfil está aplicado.</returns>
        public static bool RequestProfileApply(
            string requesterId,
            string reason,
            ILoggingService? logger = null)
        {
            try
            {
                var log = logger ?? App.LoggingService;

                log?.LogInfo(
                    $"[POWER-AUTH] '{requesterId}' pediu reaplicar o Perfil Inteligente ({reason}). " +
                    "O plano nao e mais escolhido pelo chamador: quem decide e' o Perfil.");

                ProfilePowerCoordinator.ApplyCurrentProfile(log);
                return true;
            }
            catch (Exception ex)
            {
                (logger ?? App.LoggingService)?.LogWarning(
                    $"[POWER-AUTH] Falha ao reaplicar o perfil a pedido de '{requesterId}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Troca o PERFIL (não o plano) e aplica imediatamente.
        ///
        /// Usado pelo Brain e pelo Modo Gamer, que são as duas portas legítimas
        /// de entrada do Perfil Inteligente: eles mudam o perfil, e o plano vem
        /// como consequência.
        /// </summary>
        /// <param name="profile">Perfil a aplicar.</param>
        /// <param name="requesterId">Quem pediu. Fica no log para diagnóstico.</param>
        /// <param name="reason">Por quê. Também vai para o log.</param>
        /// <param name="logger">Log opcional; usa o log global quando omitido.</param>
        /// <returns><c>true</c> se o perfil foi aplicado.</returns>
        public static bool RequestProfile(
            IntelligentProfileType profile,
            string requesterId,
            string reason,
            ILoggingService? logger = null)
        {
            try
            {
                var log = logger ?? App.LoggingService;

                log?.LogInfo(
                    $"[POWER-AUTH] '{requesterId}' pediu o perfil '{profile}' ({reason}).");

                ProfilePowerCoordinator.ApplyNow(profile, log);
                return true;
            }
            catch (Exception ex)
            {
                (logger ?? App.LoggingService)?.LogWarning(
                    $"[POWER-AUTH] Falha ao aplicar o perfil '{profile}' a pedido de '{requesterId}': {ex.Message}");
                return false;
            }
        }
    }
}
