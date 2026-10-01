using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.Shield.Network;

namespace VoltrisOptimizer.Services.Shield
{
    public class RansomwareMonitorService
    {
        private readonly ILoggingService _logger;
        private readonly SecurityLogService _securityLog;
        private readonly List<FileSystemWatcher> _watchers;
        private readonly ConcurrentDictionary<string, FileActivityTracker> _activityTrackers;
        private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
        private readonly object _watchersLock = new();
        
        private volatile bool _isMonitoring;
        private volatile bool _lowActivityMode;
        private int _monitoringGeneration;
        
        // Thresholds para detecção (relaxados em modo gamer)
        private const int NORMAL_FILE_COUNT_THRESHOLD = 50;
        private const int GAMER_FILE_COUNT_THRESHOLD = 100;
        private const int SUSPICIOUS_TIME_WINDOW_SECONDS = 10;
        
        private readonly string[] _suspiciousExtensions = new[]
        {
            ".encrypted", ".locked", ".crypto", ".crypt", ".crypted",
            ".cerber", ".locky", ".zepto", ".odin",
            ".zzzzz", ".aaa", ".abc", ".xyz", ".exx", ".ezz",
            ".wncry", ".wcry", ".wncryt", ".lock", ".enc"
        };
        
        public event EventHandler<RansomwareAlertEventArgs> SuspiciousActivityDetected;
        public event EventHandler<MonitoringStatusChangedEventArgs> StatusChanged;
        
        public bool IsMonitoring => _isMonitoring;
        
        public RansomwareMonitorService(ILoggingService logger, SecurityLogService securityLog)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _securityLog = securityLog ?? throw new ArgumentNullException(nameof(securityLog));
            _watchers = new List<FileSystemWatcher>();
            _activityTrackers = new ConcurrentDictionary<string, FileActivityTracker>();
        }
        
        public async Task StartMonitoringAsync()
        {
            await _lifecycleGate.WaitAsync();
            try
            {
                if (_isMonitoring)
                    return;

                _logger.LogInfo("[RansomwareMonitor] Iniciando monitoramento...");
                var generation = Interlocked.Increment(ref _monitoringGeneration);
                var createdWatchers = new List<FileSystemWatcher>();

                try
                {
                    var foldersToMonitor = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                        Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
                    };

                    try
                    {
                        foreach (var drive in System.IO.DriveInfo.GetDrives())
                        {
                            if (drive.IsReady && drive.DriveType == DriveType.Fixed && drive.Name != @"C:\")
                            {
                                foldersToMonitor.Add(drive.RootDirectory.FullName);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"[RansomwareMonitor] Erro ao enumerar drives: {ex.Message}");
                    }

                    foreach (var folder in foldersToMonitor)
                    {
                        if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
                        {
                            AddWatcher(folder, createdWatchers, generation);
                        }
                    }

                    if (createdWatchers.Count == 0)
                    {
                        throw new InvalidOperationException("Nenhuma pasta pôde ser monitorada.");
                    }

                    lock (_watchersLock)
                    {
                        if (_isMonitoring)
                        {
                            CleanupWatchers(createdWatchers);
                            return;
                        }

                        _watchers.AddRange(createdWatchers);
                        _isMonitoring = true;
                    }

                    _securityLog.LogSecurityEvent("RansomwareDetector", "MONITORING_STARTED", $"Monitoring {_watchers.Count} folders");
                    _logger.LogSuccess($"[RansomwareMonitor] Monitoramento ativo em {_watchers.Count} pastas");
                    RaiseStatusChanged(true);
                    return;
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref _monitoringGeneration);
                    lock (_watchersLock)
                    {
                        _isMonitoring = false;
                        _watchers.RemoveAll(createdWatchers.Contains);
                    }
                    CleanupWatchers(createdWatchers);
                    _logger.LogError("[RansomwareMonitor] Erro ao iniciar monitoramento", ex);
                    throw;
                }
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        public async Task StopMonitoringAsync()
        {
            await _lifecycleGate.WaitAsync();
            try
            {
                _logger.LogInfo("[RansomwareMonitor] Parando monitoramento...");
                Interlocked.Increment(ref _monitoringGeneration);

                List<FileSystemWatcher> watchers;
                lock (_watchersLock)
                {
                    _isMonitoring = false;
                    watchers = _watchers.ToList();
                    _watchers.Clear();
                    _activityTrackers.Clear();
                }

                CleanupWatchers(watchers);
                _securityLog.LogSecurityEvent("RansomwareDetector", "MONITORING_STOPPED", "Monitoring disabled");
                _logger.LogInfo("[RansomwareMonitor] Monitoramento parado");
                RaiseStatusChanged(false);
            }
            catch (Exception ex)
            {
                _logger.LogError("[RansomwareMonitor] Erro ao parar monitoramento", ex);
                throw;
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        private void AddWatcher(string path, List<FileSystemWatcher> createdWatchers, int generation)
        {
            FileSystemWatcher? watcher = null;
            try
            {
                watcher = CreateWatcher(path, generation);
                watcher.EnableRaisingEvents = true;
                createdWatchers.Add(watcher);
                _logger.LogInfo($"[RansomwareMonitor] Monitorando: {path}");
            }
            catch (Exception ex)
            {
                if (watcher != null)
                {
                    CleanupWatchers(new[] { watcher });
                }

                _logger.LogWarning($"[RansomwareMonitor] Não foi possível monitorar {path}: {ex.Message}");
            }
        }

        private FileSystemWatcher CreateWatcher(string path, int generation)
        {
            var watcher = new FileSystemWatcher(path)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size,
                IncludeSubdirectories = true,
                InternalBufferSize = 32768
            };

            watcher.Created += (sender, args) => OnFileCreated(sender, args, generation);
            watcher.Changed += (sender, args) => OnFileChanged(sender, args, generation);
            watcher.Deleted += (sender, args) => OnFileDeleted(sender, args, generation);
            watcher.Renamed += (sender, args) => OnFileRenamed(sender, args, generation);
            watcher.Error += (sender, args) => OnWatcherError(sender, args, generation);
            return watcher;
        }

        private void CleanupWatchers(IEnumerable<FileSystemWatcher> watchers)
        {
            foreach (var watcher in watchers)
            {
                try
                {
                    watcher.EnableRaisingEvents = false;
                }
                catch { }
                try
                {
                    watcher.Dispose();
                }
                catch { }
            }
        }

        private bool IsCurrentWatcher(object? sender, int generation)
        {
            if (!_isMonitoring || generation != Volatile.Read(ref _monitoringGeneration) || sender is not FileSystemWatcher watcher)
            {
                return false;
            }

            lock (_watchersLock)
            {
                return _watchers.Contains(watcher);
            }
        }

        private void RaiseStatusChanged(bool isActive)
        {
            var handlers = StatusChanged;
            if (handlers == null)
                return;

            foreach (var subscriber in handlers.GetInvocationList())
            {
                try
                {
                    ((EventHandler<MonitoringStatusChangedEventArgs>)subscriber)(this, new MonitoringStatusChangedEventArgs { IsActive = isActive });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[RansomwareMonitor] Falha ao notificar status: {ex.Message}");
                }
            }
        }
        
        public void SetLowActivityMode(bool enabled)
        {
            _lowActivityMode = enabled;
            _logger.LogInfo($"[RansomwareMonitor] Modo baixa atividade: {enabled}");
        }
        
        private void OnWatcherError(object sender, ErrorEventArgs e, int generation)
        {
            try
            {
                if (!IsCurrentWatcher(sender, generation))
                {
                    if (sender is FileSystemWatcher watcher)
                    {
                        CleanupWatchers(new[] { watcher });
                    }
                    return;
                }

                var ex = e.GetException();
                if (ex is InternalBufferOverflowException)
                {
                    ReplaceWatcher((FileSystemWatcher)sender, generation);
                    return;
                }

                _logger.LogWarning($"[RansomwareMonitor] Erro no FileSystemWatcher: {ex.Message}");
            }
            catch (Exception callbackException)
            {
                _logger.LogWarning($"[RansomwareMonitor] Falha ao tratar erro do watcher: {callbackException.Message}");
            }
        }

        private void ReplaceWatcher(FileSystemWatcher faulted, int generation)
        {
            string path;
            lock (_watchersLock)
            {
                if (!_watchers.Remove(faulted))
                    return;
                path = faulted.Path;
            }

            CleanupWatchers(new[] { faulted });
            if (!_isMonitoring || generation != Volatile.Read(ref _monitoringGeneration) || !Directory.Exists(path))
                return;

            FileSystemWatcher? replacement = null;
            try
            {
                replacement = CreateWatcher(path, generation);
                replacement.EnableRaisingEvents = true;
                lock (_watchersLock)
                {
                    if (_isMonitoring && generation == Volatile.Read(ref _monitoringGeneration))
                    {
                        _watchers.Add(replacement);
                        replacement = null;
                        _logger.LogWarning($"[RansomwareMonitor] Buffer overflow; watcher foi recriado em {path}. Revalidar cobertura deste volume.");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[RansomwareMonitor] Falha ao recriar watcher em {path}", ex);
            }
            finally
            {
                if (replacement != null)
                {
                    CleanupWatchers(new[] { replacement });
                }
            }
        }
        
        private void OnFileCreated(object sender, FileSystemEventArgs e, int generation)
        {
            if (IsCurrentWatcher(sender, generation))
                TrackFileActivity(e.FullPath, "CREATED");
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e, int generation)
        {
            if (IsCurrentWatcher(sender, generation))
                TrackFileActivity(e.FullPath, "MODIFIED");
        }

        private void OnFileDeleted(object sender, FileSystemEventArgs e, int generation)
        {
            if (IsCurrentWatcher(sender, generation))
                TrackFileActivity(e.FullPath, "DELETED");
        }

        private void OnFileRenamed(object sender, RenamedEventArgs e, int generation)
        {
            if (!IsCurrentWatcher(sender, generation))
                return;

            try
            {
                TrackFileActivity(e.FullPath, "RENAMED");
                if (!IsSuspiciousExtension(e.FullPath))
                    return;

                _logger.LogWarning($"[RansomwareMonitor] Arquivo renomeado para extensão suspeita: {e.FullPath}");
                _securityLog.LogRansomwareAlert("Unknown", $"File renamed to suspicious extension: {e.Name}");
                RaiseSuspiciousActivityAlert("Suspicious file extension detected", e.FullPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[RansomwareMonitor] Falha ao processar renomeação: {ex.Message}");
            }
        }

        private void TrackFileActivity(string filePath, string activityType)
        {
            try
            {
                var directory = Path.GetDirectoryName(filePath);
                if (string.IsNullOrEmpty(directory) || IsDevelopmentPath(directory))
                    return;

                var tracker = _activityTrackers.GetOrAdd(directory, _ => new FileActivityTracker());
                var threshold = _lowActivityMode ? GAMER_FILE_COUNT_THRESHOLD : NORMAL_FILE_COUNT_THRESHOLD;
                if (!tracker.TryConsumeThreshold(threshold, SUSPICIOUS_TIME_WINDOW_SECONDS, out var count))
                    return;

                _logger.LogWarning($"[RansomwareMonitor] Atividade massiva detectada em: {directory}");
                _securityLog.LogRansomwareAlert("Unknown", $"Mass file activity ({activityType}) in {directory}: {count} events in {SUSPICIOUS_TIME_WINDOW_SECONDS}s");
                RaiseSuspiciousActivityAlert("Mass file modification detected", directory);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[RansomwareMonitor] Falha ao processar atividade de arquivo: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Verifica se o caminho pertence a uma pasta de desenvolvimento/build/IDE.
        /// Compilações geram centenas de arquivos em segundos — não é ransomware.
        /// </summary>
        private static bool IsDevelopmentPath(string directory)
        {
            var segments = directory.Replace('/', '\\').Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            return segments.Any(segment =>
            {
                switch (segment.ToLowerInvariant())
                {
                    case "obj":
                    case "bin":
                    case "artifacts_app":
                    case ".nuget":
                    case "packages":
                    case "node_modules":
                    case ".vs":
                    case ".vscode":
                    case ".kiro":
                    case "debug":
                    case "release":
                    case "publish":
                    case ".git":
                    case ".next":
                    case "dist":
                    case "build":
                        return true;
                    default:
                        return false;
                }
            });
        }

        private bool IsSuspiciousExtension(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return _suspiciousExtensions.Any(ext => ext == extension);
        }
        
        /// <summary>
        /// [FIX:SHIELD-CONTAGEM-ZERADA] HISTÓRICO DE ALERTAS DA SESSÃO.
        ///
        /// Este serviço detects ataques e emite evento, mas não guardava nada.
        /// A consequência era direta e grave: o contador de alertas de ransomware
        /// da Visão Geral **nunca tinha como ser preenchido**, porque não havia
        /// fonte alguma para ele. Ele ficava em zero para sempre — e a tela
        /// mostrava esse zero com o mesmo peso de um "não houve ataques".
        ///
        /// Um detector de ransomware sem histórico é um detector que esquece.
        /// O serviço é quem viu a atividade suspeita acontecer; a lista é dele.
        /// A UI apenas lê.
        ///
        /// O histórico também sobrevive ao ciclo de vida do evento: se ninguém
        /// estava inscrito no momento da detecção — a aba Shield fechada, por
        /// exemplo — o alerta ainda é registrado, e aparece quando o usuário
        /// abrir a tela. É a mesma razão pela qual o histórico de ameaças foi
        /// criado no `VoltrisShieldService`.
        /// </summary>
        private readonly List<RansomwareAlertEventArgs> _alertHistory = new();
        private readonly object _alertHistoryLock = new();

        /// <summary>
        /// Os alertas de atividade suspeita acumulados nesta sessão.
        /// </summary>
        public IReadOnlyList<RansomwareAlertEventArgs> GetAlertHistory()
        {
            lock (_alertHistoryLock)
            {
                return _alertHistory.ToList();
            }
        }

        private void RecordAlert(RansomwareAlertEventArgs args)
        {
            lock (_alertHistoryLock)
            {
                _alertHistory.Add(args);

                // Teto generoso e finito: o que interessa é a sessão, e um
                // histórico ilimitado seria um vazamento silencioso.
                const int maxAlerts = 500;
                if (_alertHistory.Count > maxAlerts)
                {
                    _alertHistory.RemoveRange(0, _alertHistory.Count - maxAlerts);
                }
            }
        }

        private void RaiseSuspiciousActivityAlert(string alertType, string details)
        {
            var args = new RansomwareAlertEventArgs
            {
                AlertType = alertType,
                Details = details,
                Timestamp = DateTime.Now
            };

            // [FIX:SHIELD-CONTAGEM-ZERADA] REGISTRAR ANTES DE NOTIFICAR, E
            // REGISTRAR SEMPRE.
            //
            // O `return` abaixo, quando não havia nenhum assinante, descartava o
            // alerta por completo. Com a aba Shield fechada — que é o estado
            // normal enquanto o usuário joga — não havia assinantes, e o ataque
            // detectado simplesmente sumia. Era o caso mais grave possível:
            // o detector funcionava e o resultado era invisível.
            //
            // A ordem importa pela mesma razão da história de ameaças:
            // registrar antes evita a janela em que o evento já saiu e o
            // contador ainda está em zero.
            RecordAlert(args);

            var handlers = SuspiciousActivityDetected;
            if (handlers == null)
            {
                return;
            }

            foreach (var subscriber in handlers.GetInvocationList())
            {
                try
                {
                    ((EventHandler<RansomwareAlertEventArgs>)subscriber)(this, args);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[RansomwareMonitor] Falha ao notificar atividade suspeita: {ex.Message}");
                }
            }
        }
    }
    
    public class FileActivityTracker
    {
        private readonly List<DateTime> _activities = new List<DateTime>();
        private readonly object _lock = new object();
        
        public bool TryConsumeThreshold(int threshold, int seconds, out int count)
        {
            lock (_lock)
            {
                var now = DateTime.Now;
                _activities.RemoveAll(a => (now - a).TotalSeconds > seconds);
                count = _activities.Count;
                if (count <= threshold)
                    return false;

                _activities.Clear();
                return true;
            }
        }

        public int GetRecentActivityCount(int seconds)
        {
            lock (_lock)
            {
                var cutoff = DateTime.Now.AddSeconds(-seconds);
                _activities.RemoveAll(a => a < cutoff);
                return _activities.Count;
            }
        }
        
        public void Reset()
        {
            lock (_lock)
            {
                _activities.Clear();
            }
        }
    }
    
    public class RansomwareAlertEventArgs : EventArgs
    {
        public string AlertType { get; set; }
        public string Details { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
