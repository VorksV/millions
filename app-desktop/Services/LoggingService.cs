using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services
{
    /// <summary>
    /// Logging Service - SILICON VALLEY GRADE PERFORMANCE
    /// 
    /// Arquitetura:
    /// - Log() encfileira a mensagem de forma não-bloqueante (O(1))
    /// - Uma única background thread consome a fila e escreve em disco em lotes
    /// - Evento LogEntryAdded é disparado com throttle para não inundar a UI thread
    /// - CPU idle esperada: &lt;0.1%
    /// </summary>
    public class LoggingService : ILoggingService, IDisposable
    {
        private readonly string _logDirectory;
        private readonly string _logFilePath;
        
        // Fila lock-free de alta performance para mensagens de log
        private readonly BlockingCollection<string> _logQueue = new(2000);
        
        // Controle de throttle para o evento de UI (evita inundar o Dispatcher)
        private volatile int _pendingUiNotifications = 0;
        private readonly System.Threading.Timer _uiNotifyThrottle;
        private string? _lastLineForUi;
        
        private Task? _writerTask;
        private CancellationTokenSource _cts = new();
        private bool _isDisposed;
        
        // Intervalo de escrita em disco em lote (ms)
        // 300ms é imperceptível para o usuário mas drasticamente mais eficiente
        private const int WriteBatchIntervalMs = 300;
        // Linhas máximas por lote de escrita
        private const int MaxLinesPerBatch = 50;
        
        public event EventHandler<string>? LogEntryAdded;

        public LoggingService(string logDirectory)
        {
            _logDirectory = logDirectory;
            _logFilePath = Path.Combine(_logDirectory, "voltris.log.txt");
            
            // ROTAÇÃO DE LOGS: Se o log atual exceder 10MB, fazer backup/rotacionar
            try
            {
                if (!Directory.Exists(_logDirectory))
                    Directory.CreateDirectory(_logDirectory);

                var fileInfo = new FileInfo(_logFilePath);
                if (fileInfo.Exists && fileInfo.Length > 10 * 1024 * 1024) // 10 MB
                {
                    var backupPath = Path.Combine(_logDirectory, "voltris.log.old.txt");
                    if (File.Exists(backupPath))
                        File.Delete(backupPath);
                    File.Move(_logFilePath, backupPath);
                }
            }
            catch { }

            // LIMPEZA AUTOMÁTICA DE LOGS ANTIGOS (mais de 7 dias)
            try
            {
                var cutoffDate = DateTime.Now.AddDays(-7);
                var di = new DirectoryInfo(_logDirectory);
                foreach (var file in di.GetFiles())
                {
                    // Não deletar o log ativo nem o backup imediato
                    if (file.Name == "voltris.log.txt" || file.Name == "voltris.log.old.txt")
                        continue;

                    if (file.LastWriteTime < cutoffDate)
                    {
                        file.Delete();
                    }
                }
            }
            catch { }
            
            // Throttle: dispara evento de UI no máximo 1x a cada 100ms
            // Isso impede que logging intenso do DLS inunde o Dispatcher
            _uiNotifyThrottle = new System.Threading.Timer(_ => FlushUiNotification(), null,
                Timeout.Infinite, Timeout.Infinite);
            
            // Worker único que drena a fila e grava em disco em lotes
            _writerTask = Task.Run(WriterLoopAsync);
        }

        public void LogInfo(string message)    => Log(LogLevel.Info,    LogCategory.General, message);
        public void LogSuccess(string message) => Log(LogLevel.Success,  LogCategory.General, message);
        public void LogWarning(string message) => Log(LogLevel.Warning,  LogCategory.General, message);
        public void LogError(string message, Exception? exception = null) => Log(LogLevel.Error, LogCategory.General, message, exception);
        public void LogDebug(string message, string? source = null)       => Log(LogLevel.Debug, LogCategory.General, message, source: source);
        public void LogTrace(string message, string? source = null)       => Log(LogLevel.Trace, LogCategory.General, message, source: source);
        public void LogCritical(string message, Exception? exception = null, string? source = null)
            => Log(LogLevel.Critical, LogCategory.General, message, exception, source);

        public void Log(LogLevel level, LogCategory category, string message, Exception? exception = null, string? source = null)
        {
            if (_isDisposed) return;
            
            // Filtrar Trace/Debug em produção para reduzir volume de log e CPU
            if (level == LogLevel.Trace || level == LogLevel.Debug)
                return;

            var sourceStr = source != null ? $" [{source}]" : "";
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level.ToString().ToUpper()}] [{category}]{sourceStr} {message}";
            
            // Encfileirar de forma não-bloqueante.
            // TryAdd não trava a thread de chamada — se a fila estiver cheia, descarta silenciosamente.
            _logQueue.TryAdd(line);

            if (exception != null)
            {
                // A EXCEÇÃO PRECISA SER COMPLETA: WPF/WMI embrulham a causa real em wrappers
                // e o log anterior registrava apenas exception.Message, perdendo stack + InnerException.
                // Cada linha da cadeia é enfileirada separadamente para que o formato de uma
                // linha por registro seja preservado no voltris.log.txt.
                foreach (var exLine in BuildExceptionLines(exception))
                    _logQueue.TryAdd(exLine);
            }
            
            // Integramos com o Telegram para erros críticos/normais
            if (level == LogLevel.Error || level == LogLevel.Critical)
            {
                string telegramMsg = $"🚨 VOLTRIS ERROR 🚨\n\n" +
                                     $"Máquina: {Environment.MachineName}\n" +
                                     $"Nível: {level}\n" +
                                     $"Categoria: {category}\n" +
                                     $"Mensagem: {message}";
                if (exception != null) telegramMsg += $"\n\n--- CAUSA EXATA (STACK TRACE) ---\n{exception.ToString()}";
                
                TelegramLogger.SendMessageFireAndForget(telegramMsg);
            }
            
            // Acionar notificação de UI com throttle.
            // Apenas 1 acesso de escrita volátil: custo praticamente zero.
            _lastLineForUi = line;
            
            // Agendar disparo único — se já houver um agendado, sobrescrever (coalescência)
            // Isso garante que eventos chegam à UI no máximo ~100ms após o log
            if (Interlocked.Exchange(ref _pendingUiNotifications, 1) == 0)
            {
                _uiNotifyThrottle.Change(100, Timeout.Infinite);
            }
        }
        
        /// <summary>
        /// Constrói as linhas de log de uma exceção preservando a cadeia completa de wrappers
        /// (XamlReader.RewrapException, ManagementException, TargetInvocationException, etc.)
        /// e os stack traces. Cada linha mantém o prefixo timestamp para não quebrar parsers.
        /// </summary>
        private static System.Collections.Generic.IEnumerable<string> BuildExceptionLines(Exception exception)
        {
            var stamp = DateTime.Now;
            int depth = 0;
            Exception? current = exception;
            while (current != null && depth < 8)
            {
                string prefix = depth == 0 ? "EX" : "IN";
                yield return $"[{stamp:HH:mm:ss.fff}] [{prefix}{depth}] {current.GetType().FullName}: {current.Message}";

                if (current is System.Management.ManagementException mgmt)
                {
                    foreach (var extra in DescribeWmiExtras(mgmt))
                        yield return $"[{stamp:HH:mm:ss.fff}] [IN{depth}] {extra}";
                }

                current = current.InnerException;
                depth++;
            }

            if (!string.IsNullOrWhiteSpace(exception.StackTrace))
            {
                foreach (var stackLine in exception.StackTrace.Split('\n'))
                {
                    string trimmed = stackLine.TrimEnd('\r');
                    if (!string.IsNullOrWhiteSpace(trimmed))
                        yield return $"[{stamp:HH:mm:ss.fff}] [STACK] {trimmed}";
                }
            }
        }

        private static System.Collections.Generic.IEnumerable<string> DescribeWmiExtras(System.Management.ManagementException mgmt)
        {
            var results = new System.Collections.Generic.List<string>();
            try
            {
                object? code = mgmt.GetType().GetProperty("ErrorCode")?.GetValue(mgmt);
                if (code != null) results.Add($"WMI ErrorCode: {code}");

                if (mgmt.GetType().GetProperty("Parameters")?.GetValue(mgmt) is System.Collections.IEnumerable parameters)
                {
                    foreach (var p in parameters)
                        results.Add($"WMI Parameter: {p}");
                }

                object? query = mgmt.GetType().GetProperty("Query")?.GetValue(mgmt);
                if (query != null) results.Add($"WMI Query: {query}");
            }
            catch
            {
                // best-effort: nunca mascarar a exceção original
            }
            return results;
        }

        private void FlushUiNotification()
        {
            Interlocked.Exchange(ref _pendingUiNotifications, 0);
            var line = _lastLineForUi;
            if (line != null)
            {
                try { LogEntryAdded?.Invoke(this, line); } catch { }
            }
        }

        private async Task WriterLoopAsync()
        {
            var ct = _cts.Token;
            var batch = new List<string>(MaxLinesPerBatch);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // Esperar uma mensagem (até WriteBatchIntervalMs ms)
                    if (_logQueue.TryTake(out var first, WriteBatchIntervalMs, ct))
                    {
                        batch.Add(first);
                        
                        // Drenar o restante disponível agora (sem esperar)
                        while (batch.Count < MaxLinesPerBatch && _logQueue.TryTake(out var next))
                            batch.Add(next);
                        
                        await FlushBatchToDiskAsync(batch);
                        batch.Clear();
                    }
                }
                catch (OperationCanceledException) { break; }
                catch { /* never crash writer */ }
            }
            
            // Flush final ao desligar
            while (_logQueue.TryTake(out var remaining))
                batch.Add(remaining);
            if (batch.Count > 0)
                await FlushBatchToDiskAsync(batch);
        }
        
        private async Task FlushBatchToDiskAsync(List<string> lines)
        {
            try
            {
                if (!Directory.Exists(_logDirectory))
                    Directory.CreateDirectory(_logDirectory);
                
                var sb = new StringBuilder(lines.Count * 120);
                foreach (var l in lines)
                    sb.AppendLine(l);
                
                // StreamWriter em modo append com buffer (muito mais eficiente que File.AppendAllText)
                RotateIfNeeded();
                await using var fs = new FileStream(_logFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 65536, useAsync: true);
await using var writer = new StreamWriter(fs, Encoding.UTF8);
                await writer.WriteAsync(sb.ToString()).ConfigureAwait(false);
            }
            catch { /* disk errors should not crash app */ }
        }

        private DateTime _ultimaRotacao = DateTime.MinValue;

        /// <summary>
        /// Serializa a rotação. RotacionCalled tanto do flush assíncrono quanto
        /// do síncrono, que rodam em threads diferentes: sem este lock as duas
        /// passam na checagem de 60 s ao mesmo tempo e a segunda File.Move
        /// falha (o catch engolia o erro e um ciclo de log se perdia).
        /// </summary>
        private readonly object _rotacaoLock = new();

        /// <summary>
        /// Quantos arquivos "voltris.log.*.txt" manter. A limpeza por idade (7
        /// dias) não é suficiente sozinha: o log cresce ~64 KB/min, então cada
        /// rotação de 10 MB leva ~2,6 h. Em 7 dias isso acumularia dezenas de
        /// arquivos de 10 MB, ou seja centenas de MB. O teto por quantidade é
        /// a garantia real de espaço em disco.
        /// </summary>
        private const int MaxArquivosRotacionados = 5;

        /// <summary>
        /// ROTAÇÃO DE LOG DURANTE A EXECUÇÃO.
        ///
        /// BUG CORRIGIDO: a rotação só era avaliada no construtor do serviço,
        /// ou seja, uma vez por inicialização do processo. Numa sessão longa o
        /// arquivo crescia sem limite: chegou a 22 MB numa sessão de ~6h,
        /// muito acima do limite de 10 MB que o próprio codigo pretendia
        /// respeitar. Agora o tamanho é conferido durante a escrita, com
        /// intervalo mínimo para não pagar uma chamada de disco por lote.
        /// </summary>
        private void RotateIfNeeded()
        {
            lock (_rotacaoLock)
            {
                // No máximo uma verificação por minuto.
                if ((DateTime.UtcNow - _ultimaRotacao).TotalSeconds < 60) return;
                _ultimaRotacao = DateTime.UtcNow;

                try
                {
                    if (!File.Exists(_logFilePath)) return;

                    var fileInfo = new FileInfo(_logFilePath);
                    if (fileInfo.Length <= 10 * 1024 * 1024) return;

                    var backupPath = Path.Combine(_logDirectory, "voltris.log.old.txt");
                    if (File.Exists(backupPath))
                    {
                        // Não apaga o anterior sem aviso: vira .1, .2, ... para
                        // preservar o historico recente.
                        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        var arquivado = Path.Combine(_logDirectory, $"voltris.log.{stamp}.txt");
                        File.Move(backupPath, arquivado);
                    }

                    File.Move(_logFilePath, backupPath);

                    // Teto por quantidade: mantém apenas os N mais recentes.
                    // É o que garante o espaço em disco, já que a limpeza por
                    // idade de 7 dias sozinha não dá conta do volume.
                    PodarArquivosRotacionados();
                }
                catch
                {
                    // Falha de disco nao pode derrubar o app; proxima tentativa em 1min.
                }
            }
        }

        /// <summary>
        /// Mantém no máximo <see cref="MaxArquivosRotacionados"/> backups
        /// "voltris.log.*.txt", apagando os mais antigos. A rotação só é
        /// disparada por tamanho de arquivo, e a limpeza por idade de 7 dias
        /// não limita quantidade: sem este teto, uma sessão longa acumularia
        /// centenas de MB em disco.
        /// </summary>
        private void PodarArquivosRotacionados()
        {
            try
            {
                var arquivos = new DirectoryInfo(_logDirectory)
                    .GetFiles("voltris.log.*.txt")
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .ToList();

                for (int i = MaxArquivosRotacionados; i < arquivos.Count; i++)
                {
                    try { arquivos[i].Delete(); } catch { /* arquivo pode estar em uso */ }
                }
            }
            catch
            {
                // Pruning é cortesia: se falhar, a rotação continua funcionando.
            }
        }

        private void FlushBatchToDiskSync(List<string> lines)
        {
            try
            {
                if (!Directory.Exists(_logDirectory))
                    Directory.CreateDirectory(_logDirectory);
                
                var sb = new StringBuilder(lines.Count * 120);
                foreach (var l in lines)
                    sb.AppendLine(l);
                
                // Gravação síncrona segura — 100% livre de deadlocks na UI thread
                RotateIfNeeded();
                using var fs = new FileStream(_logFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 65536, useAsync: false);
using var writer = new StreamWriter(fs, Encoding.UTF8);
                writer.Write(sb.ToString());
            }
            catch { /* disk errors should not crash app */ }
        }

        public void Flush()
        {
            try
            {
                // Drenar fila restante
                var remaining = new List<string>();
                while (_logQueue.TryTake(out var line))
                    remaining.Add(line);
                if (remaining.Count > 0)
                {
                    // Usa o método síncrono dedicado para evitar deadlock síncrono-sobre-assíncrono no WPF
                    FlushBatchToDiskSync(remaining);
                }
            }
            catch { }
        }

        public void ClearLogs()
        {
            try { if (File.Exists(_logFilePath)) File.Delete(_logFilePath); } catch { }
        }
        
        public string[] GetLogs()
        {
            try
            {
                if (File.Exists(_logFilePath))
                    return File.ReadAllLines(_logFilePath);
            }
            catch { }
            return Array.Empty<string>();
        }
        
        public void ExportLogs(string destPath)
        {
            try { if (File.Exists(_logFilePath)) File.Copy(_logFilePath, destPath, true); } catch { }
        }
        
        public string GetLogDirectory() => _logDirectory;
        
        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            
            _uiNotifyThrottle.Dispose();
            _cts.Cancel();
            _logQueue.CompleteAdding();
            
            try { _writerTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
            _cts.Dispose();
        }
    }
}
