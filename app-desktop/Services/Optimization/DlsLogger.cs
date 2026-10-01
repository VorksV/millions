using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace VoltrisOptimizer.Services.Optimization
{
    /// <summary>
    /// DlsLogger — logger de baixo overhead para o loop quente do DSL.
    ///
    /// PROBLEMA ANTERIOR: File.AppendAllText() síncrono a cada mensagem.
    /// Isso abria, escrevia e fechava o arquivo a cada chamada — dezenas de
    /// syscalls de I/O por segundo dentro do loop de otimização.
    ///
    /// SOLUÇÃO: Buffer em memória (ConcurrentQueue) + flush periódico em background.
    /// O loop quente nunca toca o disco. O flush acontece a cada 30s ou no Dispose.
    /// Custo por Log(): ~50ns (enqueue). Custo anterior: ~500µs (disk I/O).
    /// </summary>
    public class DlsLogger : IDisposable
    {
        private readonly string _textPath;
        private readonly string _jsonPath;
        private readonly Queue<string> _textBuffer = new();
        private readonly Queue<object> _session = new();
        private readonly object _lock = new();
        private readonly Timer _flushTimer;
        private bool _disposed;
        private readonly string _sessionId;
        private long _totalLines;

        public DlsLogger()
        {
            _sessionId = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "VoltrisOptimizer", "logs");
            Directory.CreateDirectory(dir);
            _textPath = Path.Combine(dir, "dls.log");
            _jsonPath = Path.Combine(dir, "dls.json");

            // Limpar logs DSL antigos (>7 dias) para não acumular
            try
            {
                foreach (var f in Directory.GetFiles(dir, "dls_*.log"))
                {
                    var fi = new FileInfo(f);
                    if (fi.LastWriteTime < DateTime.Now.AddDays(-7))
                        try { fi.Delete(); } catch { }
                }
            }
            catch { }

            // Flush a cada 5 segundos para logs quase em tempo real
            _flushTimer = new Timer(_ => FlushToDisk(), null,
                dueTime: TimeSpan.FromSeconds(5),
                period: TimeSpan.FromSeconds(5));
        }

        public void Log(string message)
        {
            var now = DateTime.Now;
            var line = $"[{now:HH:mm:ss.fff}] [T{Environment.CurrentManagedThreadId}] {message}";
            lock (_lock)
            {
                _textBuffer.Enqueue(line);
                _session.Enqueue(new { ts = now, msg = message, thread = Environment.CurrentManagedThreadId, tick = Environment.TickCount64 });
                _totalLines++;

                // A sessao em memoria so e serializada no Dispose (que o DLS nao chama),
                // entao manter 100.000 objetos anonimos retidos o tempo todo e desperdicio
                // puro de RAM. O texto continua sendo gravado em disco pelo _textBuffer.
                if (_textBuffer.Count > 50000) _textBuffer.Dequeue();
                if (_session.Count > 2000) _session.Dequeue();
            }
        }

        /// <summary>
        /// Flush explícito — chamar no shutdown ou a cada N ciclos.
        /// </summary>
        public void Flush() => FlushToDisk();

        private void FlushToDisk(bool ignoreDisposed = false)
        {
            if (_disposed && !ignoreDisposed) return;
            try
            {
                string[] lines;
                lock (_lock)
                {
                    if (_textBuffer.Count == 0) return;
                    lines = _textBuffer.ToArray();
                    _textBuffer.Clear();
                }

                // Escrever tudo de uma vez — uma única syscall de I/O
                File.AppendAllLines(_textPath, lines, Encoding.UTF8);
            }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _flushTimer.Dispose();
            FlushToDisk(true);
            _disposed = true;
            try
            {
                lock (_lock)
                {
                    var json = JsonSerializer.Serialize(_session, new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,  WriteIndented = true });
                    File.WriteAllText(_jsonPath, json);
                }
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(_jsonPath + ".err", ex.ToString()); } catch { }
            }
        }
    }
}
