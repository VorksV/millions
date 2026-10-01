using System;
using System.Threading.Tasks;
using System.Windows;

namespace VoltrisOptimizer.UI.ViewModels
{
    /// <summary>
    /// Executa uma ação sobre um <see cref="DashboardViewModel"/> descartável e
    /// SEMPRE o descarta ao final.
    ///
    /// POR QUE ISTO EXISTE
    /// O DashboardViewModel é registrado como TRANSIENT de propósito: a View cria
    /// uma instância nova a cada navegação para não exibir dados antigos. Mas o
    /// construtor dele assina 10 eventos de longa duração (SettingsService,
    /// SystemMetricsCache, LicenseManager, CloudAccountService, LocalizationService,
    /// GamerViewModel, ApplicationStateTracker...) e ainda sobe um PeriodicTimer de
    /// 30 s. Os pontos de entrada que usavam o ViewModel para UMA ação — clique no
    /// tray, atalho global, comando remoto, mudança de licença — criavam a instância
    /// e a abandonavam sem Dispose, então cada uso deixava um órfão permanente.
    ///
    /// POR QUE NÃO É SIMPLESMENTE CHAMAR Dispose()
    /// Nos pontos do MainWindow a chamada era
    ///     await Dispatcher.InvokeAsync(async () => await vm.QuickCleanupAsync(...));
    /// Um lambda async nesse lugar converte para async void: o await retorna quando
    /// o Dispatcher despacha, NÃO quando a operação termina. Descartar ali marcaria
    /// _isDisposed durante a otimização, e ViewModelBase passa a ignorar toda
    /// notificação de propriedade — o progresso pararia de aparecer na UI.
    /// Por isso este helper captura a Task interna e a aguarda de verdade.
    ///
    /// SEGURANÇA
    /// Não altera tempo de vida, não compartilha instância, não mexe em nenhuma
    /// assinatura existente. Se a ação lançar, o finally ainda descarta. O
    /// DashboardView continua usando a sua própria instância nova, sem alteração.
    /// </summary>
    internal static class TransientDashboard
    {
        /// <summary>Executa uma ação SÍNCRONA e descarta a instância.</summary>
        internal static async Task RunAsync(Action<DashboardViewModel> syncAction)
        {
            if (syncAction == null) return;

            var vm = App.Services?.GetService(typeof(DashboardViewModel)) as DashboardViewModel;
            if (vm == null) return;

            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.CheckAccess())
                {
                    syncAction(vm);
                }
                else
                {
                    await dispatcher.InvokeAsync(() => syncAction(vm));
                }
            }
            catch
            {
                // A ação nunca deve derrubar o chamador (clique de tray, atalho).
            }
            finally
            {
                try { vm.Dispose(); } catch { }
            }
        }

        /// <summary>Executa uma ação ASSÍNCRONA, aguarda-a de verdade, e descarta.</summary>
        internal static async Task RunAsync(Func<DashboardViewModel, Task> asyncAction)
        {
            if (asyncAction == null) return;

            var vm = App.Services?.GetService(typeof(DashboardViewModel)) as DashboardViewModel;
            if (vm == null) return;

            try
            {
                var dispatcher = Application.Current?.Dispatcher;

                if (dispatcher == null)
                {
                    await asyncAction(vm).ConfigureAwait(false);
                    return;
                }

                // Captura a Task REAL: sem isso, o Dispose abaixo rodaria antes de a
                // operação terminar e a UI perderia o progresso.
                Task inner = null;

                if (dispatcher.CheckAccess())
                {
                    inner = asyncAction(vm);
                }
                else
                {
                    await dispatcher.InvokeAsync(() => { inner = asyncAction(vm); });
                }

                if (inner != null) await inner;
            }
            catch
            {
                // idem
            }
            finally
            {
                try { vm.Dispose(); } catch { }
            }
        }
    }
}
