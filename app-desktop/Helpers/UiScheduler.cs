using System;
using System.Collections.Generic;
using System.Windows.Threading;
using System.Windows;

namespace VoltrisOptimizer.Helpers
{
    /// <summary>
    /// Scheduler simples que consolida múltiplos DispatcherTimers em um único timer.
    /// Cada callback pode especificar seu intervalo (em milissegundos) e prioridade.
    /// O agendador roda na UI thread (Application.Current.Dispatcher).
    /// </summary>
    public static class UiScheduler
    {
        private class ScheduledItem
        {
            public Action Callback { get; set; }
            public TimeSpan Interval { get; set; }
            public DispatcherPriority Priority { get; set; }
            public DateTime LastRun { get; set; }
        }

        private static readonly List<ScheduledItem> _items = new();
        private static readonly DispatcherTimer _masterTimer;
        private static readonly object _lock = new();

        static UiScheduler()
        {
            // Resolução de 100 ms – suficiente para a maioria dos intervalos usados no projeto.
            // O timer só é iniciado quando existe pelo menos um callback registrado e é
            // parado assim que o último é removido. Os intervalos de execução permanecem
            // idênticos; apenas deixamos de pagar 10 tiques/s quando não há nada a agendar.
            _masterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _masterTimer.Tick += MasterTick;
        }

        private static void MasterTick(object? sender, EventArgs e)
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                foreach (var item in _items)
                {
                    if ((now - item.LastRun) >= item.Interval)
                    {
                        // Execute no dispatcher com a prioridade desejada — BeginInvoke para não bloquear o pump de mensagens
                        Application.Current?.Dispatcher?.BeginInvoke(item.Callback, item.Priority);
                        item.LastRun = now;
                    }
                }
            }
        }

        /// <summary>
        /// Registra um callback para execução periódica.
        /// </summary>
        /// <param name="callback">Método a ser chamado.</param>
        /// <param name="interval">Intervalo entre execuções.</param>
        /// <param name="priority">Prioridade do dispatcher (default Background).</param>
        /// <returns>Um IDisposable que, ao ser descartado, remove o registro.</returns>
        public static IDisposable Register(Action callback, TimeSpan interval, DispatcherPriority priority = DispatcherPriority.Background)
        {
            var item = new ScheduledItem
            {
                Callback = callback,
                Interval = interval,
                Priority = priority,
                LastRun = DateTime.UtcNow // execute após o primeiro intervalo
            };
            lock (_lock)
            {
                bool wasEmpty = _items.Count == 0;
                _items.Add(item);
                if (wasEmpty && !_masterTimer.IsEnabled)
                    _masterTimer.Start();
            }
            return new UnregisterHandle(item);
        }

        private class UnregisterHandle : IDisposable
        {
            private readonly ScheduledItem _item;
            private bool _disposed;
            public UnregisterHandle(ScheduledItem item) => _item = item;
            public void Dispose()
            {
                if (_disposed) return;
                lock (_lock)
                {
                    _items.Remove(_item);
                    if (_items.Count == 0 && _masterTimer.IsEnabled)
                        _masterTimer.Stop();
                }
                _disposed = true;
            }
        }
    }
}
