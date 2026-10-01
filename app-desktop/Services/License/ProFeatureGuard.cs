using System;
using VoltrisOptimizer.Services.License.Interfaces;
using VoltrisOptimizer.Services.License.Models;
using Microsoft.Extensions.DependencyInjection;

namespace VoltrisOptimizer.Services.License
{
    /// <summary>
    /// Gate de recursos PRO para handlers code-behind (cliques).
    /// Verifica licença paga (Standard/Pro/Enterprise); se não paga,
    /// registra o clique e abre o modal de compra existente.
    ///
    /// IMPORTANT — Thread Safety:
    /// IsPaidActive() é SEGURO para chamada na UI Thread.
    /// Usa APENAS propriedades síncronas (leitura do cache em memória).
    /// Nunca chama .GetAwaiter().GetResult() — isso causaria deadlock no WPF.
    /// </summary>
    public static class ProFeatureGuard
    {
        /// <summary>
        /// true se uma licença paga (Standard/Pro/Enterprise) está ativa.
        /// SEGURO para UI Thread — lê apenas cache síncrono em memória.
        /// </summary>
        public static bool IsPaidActive()
        {
            // PRIORIDADE 1: LicenseTokenStore (read-only, thread-safe, sem I/O)
            if (LicenseTokenStore.IsProActive)
            {
                ProEngagementTracker.RecordConversion();
                return true;
            }

            // PRIORIDADE 2: Cache síncrono do orquestrador (propriedade, sem async, sem lock)
            // CORREÇÃO CRÍTICA: NUNCA usar .GetAwaiter().GetResult() aqui — causa deadlock
            // clássico no WPF quando chamado na UI Thread com SynchronizationContext ativo.
            try
            {
                var cachedState = LicenseOrchestrationService.Instance.CachedState;
                if (cachedState?.IsActive == true)
                {
                    var t = cachedState.LicenseType?.ToLowerInvariant();
                    if (t is "standard" or "pro" or "enterprise")
                    {
                        ProEngagementTracker.RecordConversion();
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                // Se falhar ao verificar o cache, trata como não pago (bloqueia o PRO).
                App.LoggingService?.LogWarning($"[ProFeatureGuard] Erro ao verificar cache de licença — bloqueando: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Exige licença paga para o recurso. Se bloqueado, registra o clique,
        /// abre o modal de compra existente e retorna false.
        /// </summary>
        public static bool RequirePaid(string feature)
        {
            if (IsPaidActive()) return true;
            ProEngagementTracker.RecordClick(feature);
            var dialog = App.Services?.GetService<ILicenseDialogService>();
            dialog?.ShowLicenseBlocked(new LicenseState { LicenseType = "None", IsActive = false }, feature);
            return false;
        }
    }
}
