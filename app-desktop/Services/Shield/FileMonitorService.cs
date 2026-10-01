using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Shield
{
    /// <summary>
    /// Serviço de monitoramento de arquivos em tempo real.
    ///
    /// A DECISÃO foi delegada a FileAssessmentPipeline. Este serviço apenas
    /// observa o filesystem e reporta o veredito — ele não decide mais o que é
    /// suspeito. A heurística anterior (AnalyzeFile) vivia aqui como um
    /// `if/return` que retornava "suspeito" para qualquer executável em %TEMP%,
    /// sem consultar assinatura, e o NotificationManager não tinha cooldown para
    /// a classe Warning. O resultado foram 15 toasts em 2 segundos para DLLs
    /// assinadas do Windows e do Windscribe.
    /// </summary>
    public class FileMonitorService
    {
        private readonly ILoggingService _logger;
        private readonly FileAssessmentPipeline _pipeline;
        private readonly ConcurrentDictionary<string, string> _fileHashCache;
        private readonly List<FileSystemWatcher> _watchers;
        private readonly List<string> _watchedPaths;
        private bool _isMonitoring;
        private bool _lowActivityMode;
        private CancellationTokenSource? _healthCheckCts;
        private Task? _healthCheckTask;

        /// <summary>
        /// Contadores de decisão — a telemetria mínima para saber se o motor está
        /// OPERACIONAL. Um antivírus que só sabe quantos alertas mandou, e não
        /// quantos candidatos absolveu, é cego para o próprio falso positivo.
        /// </summary>
        private long _observed;
        private long _exonerated;
        private long _detected;
        private long _notified;
        private long _suppressed;

        /// <summary>
        /// Teto de avaliações de arquivo simultâneas. Cada avaliação pode disparar
        /// WinVerifyTrust e leitura do arquivo inteiro; %TEMP% durante um
        /// instalador grande gera eventos em rajada e cada um pagaria esse custo.
        /// O semaforo faz a rajada enfileirar em vez de Saturar o thread pool.
        /// </summary>
        private readonly SemaphoreSlim _assessmentGate = new(4, 16);

        public event EventHandler<SuspiciousFileEventArgs>? SuspiciousFileDetected;

        /// <summary>
        /// Emitido para cada arquivo que atinge o limiar de detecção, com o
        /// veredito completo — incluindo a lista de regras e evidências. Os
        /// consumidores decidem se vale notificar.
        /// </summary>
        public event EventHandler<FileThreatEventArgs>? FileAssessed;

        public FileMonitorService(ILoggingService logger)
            : this(logger, signatureService: null)
        {
        }

        public FileMonitorService(ILoggingService logger, SignatureVerificationService? signatureService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            signatureService ??= new SignatureVerificationService(logger);

            var locationPolicy = new TrustedLocationPolicy(logger);
            var reputation = new ThreatReputationCache(logger);
            var detectionMode = new ShieldDetectionMode(logger);

            _pipeline = new FileAssessmentPipeline(logger, signatureService, locationPolicy, reputation, detectionMode);

            _fileHashCache = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _watchers = new List<FileSystemWatcher>();
            _watchedPaths = new List<string>();
        }

        /// <summary>Pipeline em uso. Exposto para varreduras manuais e diagnóstico.</summary>
        public FileAssessmentPipeline Pipeline => _pipeline;

        /// <summary>Estatísticas de decisão do monitor.</summary>
        public FileMonitorStats Stats => new()
        {
            Observed = Interlocked.Read(ref _observed),
            Exonerated = Interlocked.Read(ref _exonerated),
            Detected = Interlocked.Read(ref _detected),
            Notified = Interlocked.Read(ref _notified),
            Suppressed = Interlocked.Read(ref _suppressed)
        };

        /// <summary>
        /// Inicia o monitoramento de arquivos
        /// </summary>
        public Task StartMonitoringAsync()
        {
            if (_isMonitoring)
                return Task.CompletedTask;

            try
            {
                _logger.LogInfo("[FileMonitor] Iniciando monitoramento de arquivos...");

                // Monitorar Downloads
                var downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (Directory.Exists(downloadsPath))
                    AddWatcher(downloadsPath, includeSubdirs: true);

                // %TEMP% é observado RECURSIVAMENTE porque é onde todo instalador
                // do Windows trabalha. Antes isso era uma fábrica de alertas; agora
                // o pipeline reconhece a categoria de staging e absolve o conteúdo
                // legítimo, e o que sobra entra como evidência fraca.
                var tempPath = Path.GetTempPath();
                if (Directory.Exists(tempPath))
                    AddWatcher(tempPath, includeSubdirs: true);

                // Monitorar Desktop (ransomware e droppers frequentemente usam)
                var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                if (Directory.Exists(desktopPath))
                    AddWatcher(desktopPath, includeSubdirs: false);

                _isMonitoring = true;

                var healthCheckCts = new CancellationTokenSource();
                _healthCheckCts = healthCheckCts;
                _healthCheckTask = Task.Run(async () =>
                {
                    try
                    {
                        await WatcherHealthCheckLoopAsync(healthCheckCts.Token);
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        _logger.LogError("[FileMonitor] Erro fatal no health-check", ex);
                    }
                }, CancellationToken.None);

                _logger.LogSuccess($"[FileMonitor] Monitoramento ativo em {_watchers.Count} diretórios");

                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError("[FileMonitor] Erro ao iniciar monitoramento", ex);
                throw;
            }
        }

        /// <summary>
        /// Para o monitoramento
        /// </summary>
        public async Task StopMonitoringAsync()
        {
            if (!_isMonitoring && _healthCheckTask == null)
                return;

            var healthCheckCts = _healthCheckCts;
            var healthCheckTask = _healthCheckTask;
            _healthCheckCts = null;
            _healthCheckTask = null;

            try
            {
                _logger.LogInfo("[FileMonitor] Parando monitoramento...");
                healthCheckCts?.Cancel();
                if (healthCheckTask != null)
                    await healthCheckTask;

                foreach (var watcher in _watchers)
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Dispose();
                }

                _watchers.Clear();
                _watchedPaths.Clear();
                _isMonitoring = false;
                _logger.LogInfo("[FileMonitor] Monitoramento parado");
            }
            catch (Exception ex)
            {
                _logger.LogError("[FileMonitor] Erro ao parar monitoramento", ex);
                throw;
            }
            finally
            {
                healthCheckCts?.Dispose();
            }
        }

        /// <summary>
        /// Define modo de baixa atividade (para modo gamer).
        ///
        /// No modo gamer, o pipeline passa a operar só com as regras de alta
        /// confiança: nome com indicador de malware, ADS, extensão disfarçada e
        /// conteúdo de framework de offense. Heurística de caminho e de metadados
        /// é silenciada. Isto substitui o `return (false, ...)` antecipado da
        /// versão anterior, que desligava a detecção inteira em vez de reduzir
        /// a sensibilidade.
        /// </summary>
        public void SetLowActivityMode(bool enabled)
        {
            _lowActivityMode = enabled;
            _pipeline.SetLowActivityMode(enabled);
            _logger.LogInfo($"[FileMonitor] Modo baixa atividade: {enabled}");
        }

        private void AddWatcher(string path, bool includeSubdirs)
        {
            try
            {
                var watcher = new FileSystemWatcher(path)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime,
                    IncludeSubdirectories = includeSubdirs,
                    InternalBufferSize = 32768 // 32KB buffer para evitar overflow
                };

                // Filtro nativo do SO antes de qualquer alocacao de managed code.
                // Descarta na borda o que o pipeline jamais avaliaria de forma
                // alguma (uma pasta renomeada para .dll, por exemplo).
                watcher.Filter = "*";

                watcher.Created += OnFileCreated;
                watcher.Error += OnWatcherError;
                watcher.EnableRaisingEvents = true;

                _watchers.Add(watcher);
                _watchedPaths.Add(path);
                _logger.LogInfo($"[FileMonitor] Monitorando: {path} (subdirs: {includeSubdirs})");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[FileMonitor] Não foi possível monitorar {path}: {ex.Message}");
            }
        }

        private void OnWatcherError(object sender, ErrorEventArgs e)
        {
            _logger.LogWarning($"[FileMonitor] Erro no FileSystemWatcher: {e.GetException().Message}. Será reiniciado pelo health-check.");
        }

        /// <summary>
        /// Health-check periódico: reinicia watchers que falharam
        /// </summary>
        private async Task WatcherHealthCheckLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(60_000, ct); // Verificar a cada 60 segundos

                    for (int i = 0; i < _watchers.Count; i++)
                    {
                        try
                        {
                            // Testar se o watcher ainda está funcional
                            var _ = _watchers[i].EnableRaisingEvents;
                        }
                        catch
                        {
                            // Watcher falhou — reiniciar
                            var path = _watchedPaths[i];
                            _logger.LogWarning($"[FileMonitor] Reiniciando watcher para: {path}");

                            try { _watchers[i].Dispose(); } catch { }

                            var newWatcher = new FileSystemWatcher(path)
                            {
                                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime,
                                IncludeSubdirectories = true,
                                InternalBufferSize = 32768,
                                Filter = "*"
                            };
                            newWatcher.Created += OnFileCreated;
                            newWatcher.Error += OnWatcherError;
                            newWatcher.EnableRaisingEvents = true;

                            _watchers[i] = newWatcher;
                            _logger.LogSuccess($"[FileMonitor] Watcher reiniciado: {path}");
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogError("[FileMonitor] Erro no health-check", ex);
                }
            }
        }

        /// <summary>
        /// Treat um arquivo recém-criado: normaliza, avalia e reporta.
        ///
        /// NOTA SOBRE ESPERA: o atraso de 500ms existia porque o watcher dispara em
        /// Created, antes do fechamento do handle de escrita. Espera fixa é
        /// corrida — em disco lento o arquivo ainda pode estar parcial, e o
        /// pipeline leria um PE truncado. Aqui esperamos pela ESTABILIZAÇÃO do
        /// tamanho, com teto, em vez de um delay cego.
        /// </summary>
        private async void OnFileCreated(object sender, FileSystemEventArgs e)
        {
            try
            {
                if (!await WaitForFileStabilityAsync(e.FullPath))
                    return;

                Interlocked.Increment(ref _observed);

                // A avaliacao faz I/O de verdade (WinVerifyTrust, leitura de 64KB
                // para entropia e headers PE, SHA-256 do arquivo inteiro). Sem
                // um teto de concorrencia, um instalador grande disparando
                // centenas de eventos em %TEMP% gaugeira o thread pool e trava o
                // aplicativo inteiro — o proprio sintoma que o produto promete
                // resolver.
                await _assessmentGate.WaitAsync().ConfigureAwait(false);
                ThreatVerdict verdict;
                try
                {
                    verdict = _pipeline.Assess(e.FullPath);
                }
                finally
                {
                    _assessmentGate.Release();
                }

                if (!verdict.ShouldRecord)
                {
                    Interlocked.Increment(ref _exonerated);

                    // Absolvição COM evidência vale um log de debug: é assim que se
                    // descobre que uma regra está com falso positivo antes do
                    // usuário reclamar.
                    if (verdict.Score > 0)
                    {
                        _logger.LogDebug($"[FileMonitor] Absolvido: {verdict.FileName} " +
                                         $"(score {verdict.Score}, conf {verdict.Confidence}) — {verdict.Reason}");
                    }
                    return;
                }

                Interlocked.Increment(ref _detected);

                _logger.LogWarning($"[FileMonitor] DETECÇÃO: {verdict.FileName} " +
                                   $"(score {verdict.Score}/100, conf {verdict.Confidence}%, {verdict.Severity}) " +
                                   $"regras=[{string.Join(",", verdict.RuleIds)}] :: {verdict.Reason}");

                if (!string.IsNullOrEmpty(verdict.FileHash))
                    _fileHashCache[verdict.FilePath] = verdict.FileHash;

                FileAssessed?.Invoke(this, new FileThreatEventArgs
                {
                    Verdict = verdict,
                    IsNotificationCandidate = verdict.ShouldNotify
                });

                if (verdict.ShouldNotify)
                {
                    Interlocked.Increment(ref _notified);
                    SuspiciousFileDetected?.Invoke(this, new SuspiciousFileEventArgs
                    {
                        FilePath = verdict.FilePath,
                        Reason = verdict.Reason,
                        FileHash = verdict.FileHash,
                        Severity = verdict.Severity,
                        Score = verdict.Score,
                        Confidence = verdict.Confidence,
                        RuleIds = verdict.RuleIds.ToArray()
                    });
                }
                else
                {
                    Interlocked.Increment(ref _suppressed);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[FileMonitor] Erro ao processar arquivo {e.Name}", ex);
            }
        }

        /// <summary>
        /// Aguarda o arquivo parar de crescer. Substitui o Task.Delay(500) fixo:
        /// 500ms é arbitrário e incorreto tanto para disco lento (lê arquivo
        /// parcial) quanto para disco rápido (espera sem necessidade).
        /// </summary>
        private static async Task<bool> WaitForFileStabilityAsync(string path, int maxAttempts = 12)
        {
            const int pollMs = 120;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    if (!File.Exists(path))
                    {
                        await Task.Delay(pollMs).ConfigureAwait(false);
                        continue;
                    }

                    var first = new FileInfo(path);
                    if (!first.Exists || first.Length == 0)
                    {
                        await Task.Delay(pollMs).ConfigureAwait(false);
                        continue;
                    }

                    await Task.Delay(pollMs).ConfigureAwait(false);

                    var second = new FileInfo(path);
                    if (!second.Exists) continue;

                    if (second.Length == first.Length &&
                        second.LastWriteTimeUtc == first.LastWriteTimeUtc)
                    {
                        return true;
                    }
                }
                catch (IOException)
                {
                    // Arquivo trancado por outro processo: tenta de novo.
                }
                catch (UnauthorizedAccessException)
                {
                    return false;
                }
            }

            // Não estabilizou no teto: avalia assim mesmo se ainda existir.
            return File.Exists(path);
        }

        /// <summary>
        /// Avalia um caminho sob demanda (scan manual, teste, diagnóstico).
        /// </summary>
        public ThreatVerdict AssessPath(string path) => _pipeline.Assess(path);
    }

    /// <summary>Estatísticas de decisão do FileMonitor.</summary>
    public sealed class FileMonitorStats
    {
        public long Observed { get; init; }
        public long Exonerated { get; init; }
        public long Detected { get; init; }
        public long Notified { get; init; }
        public long Suppressed { get; init; }

        /// <summary>Taxa de absolvição: proporção do que foi observado e aceito como legítimo.</summary>
        public double ExonerationRate => Observed == 0 ? 0 : (double)Exonerated / Observed * 100.0;

        public override string ToString() =>
            $"observados={Observed} absolvidos={Exonerated} ({ExonerationRate:F1}%) " +
            $"detectados={Detected} notificados={Notified} agrupados={Suppressed}";
    }

    /// <summary>Evento com o veredito completo de um arquivo avaliado.</summary>
    public sealed class FileThreatEventArgs : EventArgs
    {
        public ThreatVerdict Verdict { get; init; } = null!;
        public bool IsNotificationCandidate { get; init; }
    }
}
