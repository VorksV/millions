using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace VoltrisOptimizer.UI.Windows
{
    /// <summary>
    /// Hospeda o SplashWindow em uma thread STA dedicada com Dispatcher próprio.
    /// 
    /// CRÍTICO: Isto garante que o splash NUNCA fique congelado mesmo quando a UI thread
    /// principal está bloqueada (ex: carregamento síncrono pesado de MainWindow, deadlock,
    /// XAML parsing lento, etc.). É o padrão profissional para splash screens em WPF.
    /// 
    /// Sem este isolamento, qualquer travamento no MainWindow congela o splash junto
    /// pois ambos compartilhariam a mesma UI dispatcher.
    /// </summary>
    public sealed class SplashHost
    {
        private Thread? _thread;
        private SplashWindow? _splash;
        private Dispatcher? _dispatcher;
        private readonly ManualResetEventSlim _ready = new ManualResetEventSlim(false);
        private volatile bool _closed;
        private double _lastHostProgress = -1;
        private DateTime _lastHostProgressTime = DateTime.MinValue;

        /// <summary>
        /// Inicia o splash em sua própria thread STA. Retorna após o splash estar visível.
        /// </summary>
        public void Show()
        {
            _thread = new Thread(() =>
            {
                try
                {
                    _splash = new SplashWindow();
                    _dispatcher = _splash.Dispatcher;
                    _splash.Show();
                    _ready.Set();
                    // Roda o message loop dessa thread STA — isso permite que o splash
                    // processe seus próprios eventos (animação, fechamento) sem depender
                    // da UI thread principal.
                    Dispatcher.Run();
                }
                catch (Exception ex)
                {
                    try { App.LoggingService?.LogError($"[SPLASH-HOST] Erro na thread do splash: {ex.Message}", ex); } catch { }
                    _ready.Set(); // garantir que Show() não bloqueie indefinidamente
                }
            });
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.IsBackground = true;
            _thread.Name = "Voltris.SplashThread";
            _thread.Start();

// Não bloqueia a UI thread. Monitoramos readiness de forma assíncrona.
_ = Task.Run(() =>
{
    if (!_ready.Wait(TimeSpan.FromSeconds(5)))
    {
        try { App.LoggingService?.LogWarning("[SPLASH-HOST] ⚠️ Splash não ficou pronto em 5s — seguindo em frente."); } catch { }
    }
});
        }

        public bool IsClosed => _closed;

        /// <summary>
        /// Fecha o splash de forma segura. Pode ser chamado de qualquer thread.
        /// </summary>
        public void Close()
        {
            if (_closed) return;
            _closed = true;
            try
            {
                var disp = _dispatcher;
                var splash = _splash;
                if (disp == null || splash == null) return;

                // Postar a ação no dispatcher do splash (que roda em thread separada),
                // garantindo que o fechamento aconteça mesmo se a UI thread principal estiver travada.
                // CORREÇÃO CRÍTICA: Usar InvokeAsync para NÃO bloquear a UI Thread
                // enquanto a animação de fechamento do splash ocorre.
                disp.InvokeAsync(() =>
                {
                    try
                    {
                        splash.AllowClose();
                        splash.Closed += (s, e) => 
                        {
                            Dispatcher.CurrentDispatcher.InvokeShutdown();
                        };
                        splash.Close();
                    }
                    catch (Exception ex)
                    {
                        try { App.LoggingService?.LogError($"[SPLASH-HOST] Erro ao fechar splash: {ex.Message}", ex); } catch { }
                        Dispatcher.CurrentDispatcher.InvokeShutdown();
                    }
                });
            }
            catch (Exception ex)
            {
                try { App.LoggingService?.LogError($"[SPLASH-HOST] Erro ao agendar close: {ex.Message}", ex); } catch { }
            }
        }

        /// <summary>
        /// Define o progresso do splash. Pode ser chamado de qualquer thread.
        /// </summary>
        public void SetProgress(double value)
        {
            if (Math.Abs(value - _lastHostProgress) < 1.0 &&
                (DateTime.UtcNow - _lastHostProgressTime).TotalMilliseconds < 500)
                return;
            _lastHostProgress = value;
            _lastHostProgressTime = DateTime.UtcNow;
            try
            {
                _dispatcher?.BeginInvoke(new Action(() => _splash?.SetProgress(value)));
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[SplashHost] SetProgress: {ex.Message}"); }
        }

        /// <summary>
        /// Define o texto de status do splash. Pode ser chamado de qualquer thread.
        /// </summary>
        public void SetStatus(string text)
        {
            try
            {
                _dispatcher?.BeginInvoke(new Action(() => _splash?.SetStatus(text)));
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[SplashHost] SetStatus: {ex.Message}"); }
        }
    }
}
