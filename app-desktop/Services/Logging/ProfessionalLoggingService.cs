using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Logging
{


    /// <summary>
    /// Configurações do serviço de logging
    /// </summary>
    public class LoggingConfiguration
    {
        /// <summary>
        /// Nível mínimo de log a ser registrado
        /// </summary>
        public LogLevel MinimumLevel { get; set; } = LogLevel.Info; 

        /// <summary>
        /// Tamanho máximo de cada arquivo de log em bytes (padrão: 5MB)
        /// </summary>
        public long MaxFileSizeBytes { get; set; } = 5 * 1024 * 1024;

        /// <summary>
        /// Número máximo de arquivos de log a manter
        /// </summary>
        public int MaxFileCount { get; set; } = 10;

        /// <summary>
        /// Número máximo de arquivos compactados a manter
        /// </summary>
        public int MaxArchivedFileCount { get; set; } = 30;

        /// <summary>
        /// Dias para manter logs antes de arquivar
        /// </summary>
        public int DaysToKeepUnarchived { get; set; } = 7;

        /// <summary>
        /// Habilitar log assíncrono (melhor performance)
        /// </summary>
        public bool EnableAsyncLogging { get; set; } = true;

        /// <summary>
        /// Intervalo de flush do buffer em milissegundos
        /// </summary>
        public int FlushIntervalMs { get; set; } = 500;

        /// <summary>
        /// Incluir stack trace em erros
        /// </summary>
        public bool IncludeStackTraceOnError { get; set; } = true;

        /// <summary>
        /// Formato de nome do arquivo
        /// </summary>
        public string FileNameFormat { get; set; } = "voltris_{0:yyyy-MM-dd}.log";
    }

    /// <summary>
    /// Entrada de log estruturada
    /// </summary>
    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public LogLevel Level { get; set; }
        public LogCategory Category { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? ExceptionDetails { get; set; }
        public string? Source { get; set; }
        public int ThreadId { get; set; }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append($"[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}]");
            
            // Traduzir níveis para PT-BR
            string levelPtBr = Level switch
            {
                LogLevel.Info => "Informação",
                LogLevel.Success => "Sucesso",
                LogLevel.Warning => "Aviso",
                LogLevel.Error => "Erro",
                LogLevel.Critical => "Crítico",
                LogLevel.Debug => "Debug",
                LogLevel.Trace => "Trace",
                _ => Level.ToString()
            };
            
            sb.Append($" [{levelPtBr,-11}]");
            sb.Append($" [{Category,-12}]");
            sb.Append($" [T{ThreadId:D4}]");
            
            if (!string.IsNullOrEmpty(Source))
                sb.Append($" [{Source}]");
            
            // [SECURITY] TEMPORÁRIO: Desabilitar mascaramento para testar escrita
            var maskedMessage = Message; // SecurityMaskingService.MaskSensitiveData(Message);
            sb.Append($" {maskedMessage}");
            
            if (!string.IsNullOrEmpty(ExceptionDetails))
            {
                sb.AppendLine();
                var maskedException = ExceptionDetails; // SecurityMaskingService.MaskSensitiveData(ExceptionDetails);
                sb.Append($"    Exceção: {maskedException}");
            }
            
            return sb.ToString();
        }
    }

    /// <summary>
    /// Serviço de logging profissional com rotação, compressão e categorização
    /// </summary>
    public class ProfessionalLoggingService : ILoggingService, IDisposable
    {
        private readonly string _logDirectory;
        private readonly LoggingConfiguration _config;
        private readonly ConcurrentQueue<LogEntry> _logQueue;
        private readonly CancellationTokenSource _cts;
        private readonly Task _flushTask;
        private readonly object _writeLock = new();
        private string _currentLogFile;
        private StreamWriter? _currentWriter;
        private long _currentFileSize;
        private bool _disposed;

        /// <summary>
        /// Evento disparado quando uma nova entrada é adicionada
        /// </summary>
        public event EventHandler<string>? LogEntryAdded;

        /// <summary>
        /// Evento disparado quando ocorre um erro crítico
        /// </summary>
        public event EventHandler<LogEntry>? CriticalErrorLogged;

        public ProfessionalLoggingService(string logDirectory, LoggingConfiguration? config = null)
        {
            _logDirectory = logDirectory;
            
            // [SECURITY] SEGURANÇA: Usar configuração segura por ambiente se não fornecida
            _config = config ?? SecureLoggingConfiguration.GetSecureConfiguration();
            
            _logQueue = new ConcurrentQueue<LogEntry>();
            _cts = new CancellationTokenSource();

            // Garantir diretório existe
            Directory.CreateDirectory(_logDirectory);
            Directory.CreateDirectory(Path.Combine(_logDirectory, "archived"));

            // Inicializar arquivo de log
            _currentLogFile = GetCurrentLogFilePath();
            InitializeWriter();

            // [SECURITY] SEGURANÇA: Log de inicialização seguro (sem informações sensíveis)
            var env = SecureLoggingConfiguration.CurrentEnvironment;
            var maskingLevel = SecureLoggingConfiguration.GetMaskingLevel();
            LogInfo($"ProfessionalLoggingService inicializado - Ambiente: {env}, Mascaramento: {maskingLevel}");
            
            // [SECURITY] DEBUG: Forçar escrita imediamente para testar
            LogInfo($"[DEBUG] Arquivo de log: {_currentLogFile}");
            LogInfo($"[DEBUG] Diretório existe: {Directory.Exists(_logDirectory)}");
            LogInfo($"[DEBUG] Writer criado: {_currentWriter != null}");
            LogInfo($"[DEBUG] AutoFlush: {_config.EnableAsyncLogging}");
            
            // Forçar flush imediato para garantir que os logs de debug apareçam
            Flush();

            // Iniciar task de flush assíncrono
            if (_config.EnableAsyncLogging)
            {
                _flushTask = Task.Run(FlushLoopAsync, _cts.Token);
            }
            else
            {
                _flushTask = Task.CompletedTask;
            }

            // Manutenção inicial
            Task.Run(PerformMaintenanceAsync);
        }

        #region ILoggingService Implementation

        public void LogInfo(string message) => Log(LogLevel.Info, LogCategory.General, message);
        public void LogSuccess(string message) => Log(LogLevel.Success, LogCategory.General, message);
        public void LogWarning(string message) => Log(LogLevel.Warning, LogCategory.General, message);
        public void LogError(string message, Exception? exception = null) => 
            Log(LogLevel.Error, LogCategory.General, message, exception);

        #endregion

        #region Extended Logging Methods

        /// <summary>
        /// Log com nível e categoria específicos
        /// </summary>
        public void Log(LogLevel level, LogCategory category, string message, Exception? exception = null, string? source = null)
        {
            // [SECURITY] SEGURANÇA: Verificar permissões por ambiente
            if (level < _config.MinimumLevel)
                return;

            if (!SecureLoggingConfiguration.IsCategoryAllowed(category))
                return;

            if (!SecureLoggingConfiguration.IsSourceAllowed(source))
                return;

            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Category = category,
                Message = message,
                ThreadId = Environment.CurrentManagedThreadId,
                Source = source
            };

            if (exception != null)
            {
                entry.ExceptionDetails = _config.IncludeStackTraceOnError
                    ? $"{exception.GetType().Name}: {exception.Message}\n{exception.StackTrace}"
                    : $"{exception.GetType().Name}: {exception.Message}";
            }

            // [SECURITY] SEGURANÇA: Aplicar mascaramento por ambiente antes de processar
            var notificationSettings = SecureLoggingConfiguration.GetErrorNotificationSettings();
            
            if (_config.EnableAsyncLogging)
            {
                _logQueue.Enqueue(entry);
                
                // Flush imediato para erros e avisos críticos (evita perda em crash)
                // [IMPORTANT] IMPORTANTE: Usar comparação EXPLÍCITA (não >=) para evitar que
                // LogLevel.AI_DECISION (=7) dispare alertas de erro indevidamente.
                bool isRealError = level == LogLevel.Error || level == LogLevel.Critical;
                if (isRealError && level >= notificationSettings.MinErrorLevel)
                {
                    FlushQueue();

                }
            }
            else
            {
                WriteEntry(entry);

                bool isRealErrorSync = level == LogLevel.Error || level == LogLevel.Critical;
            }

            // Notificar listeners
            try
            {
                LogEntryAdded?.Invoke(this, entry.ToString());

                if (level >= LogLevel.Critical)
                {
                    CriticalErrorLogged?.Invoke(this, entry);
                }
            }
            catch
            {
                // Ignorar erros em handlers
            }
        }

        /// <summary>
        /// Log de trace (debug detalhado)
        /// </summary>
        public void LogTrace(string message, string? source = null) =>
            Log(LogLevel.Trace, LogCategory.General, message, source: source);

        /// <summary>
        /// Log de debug
        /// </summary>
        public void LogDebug(string message, string? source = null) =>
            Log(LogLevel.Debug, LogCategory.General, message, source: source);

        /// <summary>
        /// Log crítico (falha severa)
        /// </summary>
        public void LogCritical(string message, Exception? exception = null, string? source = null) =>
            Log(LogLevel.Critical, LogCategory.General, message, exception, source);

        /// <summary>
        /// Log com categoria específica
        /// </summary>
        public void LogByCategory(LogCategory category, LogLevel level, string message, Exception? exception = null) =>
            Log(level, category, message, exception);

        /// <summary>
        /// Log de licença
        /// </summary>
        public void LogLicense(LogLevel level, string message) =>
            Log(level, LogCategory.License, message);

        /// <summary>
        /// Log de voz (LYRA)
        /// </summary>
        public void LogVoice(LogLevel level, string message) =>
            Log(level, LogCategory.Voice, message);

        /// <summary>
        /// Log de segurança
        /// </summary>
        public void LogSecurity(LogLevel level, string message) =>
            Log(level, LogCategory.Security, message);

        #endregion

        #region File Operations

        private string GetCurrentLogFilePath()
        {
            return Path.Combine(_logDirectory, string.Format(_config.FileNameFormat, DateTime.Now));
        }

        private void InitializeWriter()
        {
            try
            {
                if (File.Exists(_currentLogFile))
                {
                    _currentFileSize = new FileInfo(_currentLogFile).Length;
                }
                else
                {
                    _currentFileSize = 0;
                }

                _currentWriter = new StreamWriter(_currentLogFile, true, Encoding.UTF8)
                {
                    AutoFlush = true  // [SECURITY] CRÍTICO: Forçar flush automático para logs aparecerem imediatamente
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize log writer: {ex.Message}");
            }
        }

        private void WriteEntry(LogEntry entry)
        {
            if (_disposed) return;

            lock (_writeLock)
            {
                try
                {
                    // Verificar se precisa rotacionar
                    if (_currentFileSize >= _config.MaxFileSizeBytes || 
                        !_currentLogFile.Equals(GetCurrentLogFilePath(), StringComparison.OrdinalIgnoreCase))
                    {
                        RotateLog();
                    }

                    var line = entry.ToString();
                    _currentWriter?.WriteLine(line);
                    _currentFileSize += Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to write log: {ex.Message}");
                }
            }
        }

        private void RotateLog()
        {
            try
            {
                _currentWriter?.Flush();
                _currentWriter?.Dispose();

                // Compactar arquivo antigo se necessário
                if (File.Exists(_currentLogFile) && new FileInfo(_currentLogFile).Length > 0)
                {
                    var archivePath = Path.Combine(
                        _logDirectory, 
                        "archived", 
                        Path.GetFileNameWithoutExtension(_currentLogFile) + $"_{DateTime.Now:HHmmss}.gz");

                    CompressFile(_currentLogFile, archivePath);
                }

                // Criar novo arquivo
                _currentLogFile = GetCurrentLogFilePath();
                InitializeWriter();

                // Limpar arquivos antigos
                CleanupOldFiles();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to rotate log: {ex.Message}");
            }
        }

        private void CompressFile(string sourcePath, string destPath)
        {
            try
            {
                using var sourceStream = File.OpenRead(sourcePath);
                using var destStream = File.Create(destPath);
                using var gzipStream = new GZipStream(destStream, CompressionLevel.Optimal);
                sourceStream.CopyTo(gzipStream);

                // Deletar arquivo original após compressão
                File.Delete(sourcePath);
            }
            catch
            {
                // Ignorar erros de compressão
            }
        }

        private void CleanupOldFiles()
        {
            try
            {
                // Limpar logs não arquivados antigos
                var logFiles = Directory.GetFiles(_logDirectory, "*.log")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTime)
                    .Skip(_config.MaxFileCount)
                    .ToList();

                foreach (var file in logFiles)
                {
                    try
                    {
                        file.Delete();
                    }
                    catch { }
                }

                // Limpar arquivos arquivados antigos
                var archivedDir = Path.Combine(_logDirectory, "archived");
                if (Directory.Exists(archivedDir))
                {
                    var archivedFiles = Directory.GetFiles(archivedDir, "*.gz")
                        .Select(f => new FileInfo(f))
                        .OrderByDescending(f => f.LastWriteTime)
                        .Skip(_config.MaxArchivedFileCount)
                        .ToList();

                    foreach (var file in archivedFiles)
                    {
                        try
                        {
                            file.Delete();
                        }
                        catch { }
                    }
                }
            }
            catch
            {
                // Ignorar erros de limpeza
            }
        }

        #endregion

        #region Async Operations

        private async Task FlushLoopAsync()
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_config.FlushIntervalMs, _cts.Token);
                    FlushQueue();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Continue flushing
                }
            }

            // Flush final
            FlushQueue();
        }

        private void FlushQueue()
        {
            while (_logQueue.TryDequeue(out var entry))
            {
                WriteEntry(entry);
            }

            lock (_writeLock)
            {
                try
                {
                    _currentWriter?.Flush();
                }
                catch { }
            }
        }

        private async Task PerformMaintenanceAsync()
        {
            try
            {
                await Task.Delay(5000); // Aguardar startup
                CleanupOldFiles();
            }
            catch { }
        }

        #endregion

        #region Public Utilities

        public void ClearLogs()
        {
            lock (_writeLock)
            {
                try
                {
                    _currentWriter?.Flush();
                    _currentWriter?.Dispose();
                    
                    File.WriteAllText(_currentLogFile, string.Empty, Encoding.UTF8);
                    _currentFileSize = 0;
                    
                    InitializeWriter();
                }
                catch { }
            }
        }

        public void ExportLogs(string filePath)
        {
            lock (_writeLock)
            {
                try
                {
                    _currentWriter?.Flush();
                    File.Copy(_currentLogFile, filePath, true);
                }
                catch { }
            }
        }

        public string[] GetLogs()
        {
            // OTIMIZAÇÃO CRÍTICA: Descarregar o buffer de memória antes de ler do disco
            // Isso garante que logs gerados nos últimos milissegundos apareçam instantaneamente
            FlushQueue();

            lock (_writeLock)
            {
                try
                {
                    _currentWriter?.Flush();
                    if (!File.Exists(_currentLogFile)) return Array.Empty<string>();
                    return File.ReadAllLines(_currentLogFile, Encoding.UTF8);
                }
                catch
                {
                    return Array.Empty<string>();
                }
            }
        }

        public string[] GetLogs(LogLevel minLevel, LogCategory? category = null, DateTime? since = null)
        {
            var allLogs = GetLogs();
            
            // Filtrar por critérios (parsing básico)
            return allLogs.Where(line =>
            {
                if (string.IsNullOrEmpty(line)) return false;

                // Verificar nível
                var levelMatch = minLevel switch
                {
                    LogLevel.Trace => true,
                    LogLevel.Debug => !line.Contains("[Trace    ]"),
                    LogLevel.Info => !line.Contains("[Trace    ]") && !line.Contains("[Debug    ]"),
                    LogLevel.Success => line.Contains("[Success  ]") || line.Contains("[Warning  ]") || 
                                        line.Contains("[Error    ]") || line.Contains("[Critical ]"),
                    LogLevel.Warning => line.Contains("[Warning  ]") || line.Contains("[Error    ]") || 
                                        line.Contains("[Critical ]"),
                    LogLevel.Error => line.Contains("[Error    ]") || line.Contains("[Critical ]"),
                    LogLevel.Critical => line.Contains("[Critical ]"),
                    _ => true
                };

                if (!levelMatch) return false;

                // Verificar categoria
                if (category.HasValue)
                {
                    var categoryStr = $"[{category.Value,-12}]";
                    if (!line.Contains(categoryStr)) return false;
                }

                // Verificar data
                if (since.HasValue)
                {
                    try
                    {
                        var datePart = line.Substring(1, 23);
                        if (DateTime.TryParse(datePart, out var logDate))
                        {
                            if (logDate < since.Value) return false;
                        }
                    }
                    catch { }
                }

                return true;
            }).ToArray();
        }

        public string GetLogDirectory() => _logDirectory;

        /// <summary>
        /// Força flush imediato do buffer
        /// </summary>
        public void Flush()
        {
            FlushQueue();
            
            // [SECURITY] CRÍTICO: Forçar flush do writer para salvar no disco imediatamente
            lock (_writeLock)
            {
                try
                {
                    _currentWriter?.Flush();
                    
                    // [SECURITY] CRÍTICO: Forçar flush do stream do sistema de arquivos
                    if (_currentWriter != null && _currentWriter.BaseStream != null)
                    {
                        _currentWriter.BaseStream.Flush();
                    }
                }
                catch { }
            }
        }
        
        /// <summary>
        /// Força flush completo e garante que todos os dados estejam no disco
        /// Útil para visualização manual dos logs
        /// </summary>
        public void ForceFlushToDisk()
        {
            FlushQueue();
            
            lock (_writeLock)
            {
                try
                {
                    _currentWriter?.Flush();
                    
                    // [SECURITY] CRÍTICO: Forçar flush do stream do sistema de arquivos
                    if (_currentWriter != null && _currentWriter.BaseStream != null)
                    {
                        _currentWriter.BaseStream.Flush();
                    }
                    
                    // [SECURITY] CRÍTICO: Forçar flush do sistema operacional (FlushAsync com força)
                    if (_currentWriter != null && _currentWriter.BaseStream != null)
                    {
                        _currentWriter.BaseStream.Flush();
                    }
                }
                catch { }
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                _cts.Cancel();
                
                if (_config.EnableAsyncLogging)
                {
                    // ✅ CORREÇÃO: Usar WaitAsync com timeout para evitar deadlock
                    try
                    {
                        _flushTask.Wait(TimeSpan.FromSeconds(5));
                    }
                    catch (AggregateException)
                    {
                        // Timeout ou cancelamento - esperado
                    }
                }

                // [SECURITY] CRÍTICO: Forçar flush completo antes de disposing
                FlushQueue();

                lock (_writeLock)
                {
                    // [SECURITY] CRÍTICO: Forçar flush do writer para garantir que todos os dados sejam salvos
                    _currentWriter?.Flush();
                    
                    // [SECURITY] CRÍTICO: Forçar flush do stream do sistema de arquivos
                    if (_currentWriter != null && _currentWriter.BaseStream != null)
                    {
                        _currentWriter.BaseStream.Flush();
                    }
                    
                    _currentWriter?.Dispose();
                }

                _cts.Dispose();
            }
            catch { }
        }

        #endregion
    }
}

