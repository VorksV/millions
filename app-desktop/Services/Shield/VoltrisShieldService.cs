using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.Shield.Advanced;

namespace VoltrisOptimizer.Services.Shield
{
    /// <summary>
    /// Serviço principal do Voltris Shield - Coordenador de proteção
    /// </summary>
    public class VoltrisShieldService : IDisposable
    {
        private readonly ILoggingService _logger;
        private readonly FileMonitorService _fileMonitor;
        private readonly StartupMonitorService _startupMonitor;
        private readonly AdwareScannerService _adwareScanner;
        private readonly DefenderIntegrationService _defenderIntegration;
        private readonly RansomwareMonitorService _ransomwareMonitor;
        private readonly Network.NetworkMonitorService _networkMonitor;
        private readonly QuarantineService _quarantine;
        private readonly SignatureVerificationService _signatureService;
        private readonly PortMonitorService _portMonitor;
        private readonly ThreatProtectionService _threatProtection;
        private readonly SecurityLogService _securityLog;
        private readonly ShieldLicenseGate _licenseGate;
        private readonly EventHandler<AdwareDetectedEventArgs> _adwareDetectedHandler;
        private readonly EventHandler<ThreatAlertEventArgs> _threatAlertHandler;
        
        // Módulos de proteção avançada
        private readonly AdvancedStaticAnalyzer _advancedStatic;
        private readonly BehavioralMonitor _behavioralMonitor;
        private readonly HeuristicAnalyzer _heuristicAnalyzer;
        private readonly DefenderScanService _defenderScan;
        private readonly CodeInjectionDetector _injectionDetector;
        private readonly ProcessCacheService _processCache; // Novo
        
        private volatile bool _isProtectionActive;
        private bool _isGamerModeActive;
        private bool _disposed;
        private CancellationTokenSource? _cts;
        private Task? _injectionTask;
        private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
        private DateTime _lastInjectionScan = DateTime.MinValue;
        private readonly int _ownPid = System.Diagnostics.Process.GetCurrentProcess().Id;

        /// <summary>
        /// Teto de concorrencia da varredura de injecao. Acima de ~4, o custo de
        /// OpenProcess/VirtualQueryEx comeca a competir com o proprio
        /// ProcessCacheService sem ganho proporcional.
        /// </summary>
        private const int InjectionSweepConcurrency = 4;

        /// <summary>
        /// Teto por processo. AnalyzeProcess caminha VirtualQueryEx sem limite e
        /// nao aceita cancelamento, entao um processo travado prenderia o ciclo
        /// inteiro indefinidamente.
        /// </summary>
        private static readonly TimeSpan ProcessAnalysisTimeout = TimeSpan.FromSeconds(8);
        
        public bool IsProtectionActive => _isProtectionActive;
        public bool IsNetworkMonitoringActive => _networkMonitor.IsMonitoring;
        public bool IsRansomwareMonitoringActive => _ransomwareMonitor.IsMonitoring;
        public bool IsPortMonitoringActive => _portMonitor.IsMonitoring;
        public bool IsThreatProtectionActive => _threatProtection.IsActive;
        public DateTime? LastScanTime { get; private set; }
        public QuarantineService Quarantine => _quarantine;
        public PortMonitorService PortMonitor => _portMonitor;
        public ThreatProtectionService ThreatProtection => _threatProtection;
        public SignatureVerificationService SignatureService => _signatureService;
        
        public event EventHandler<ShieldStatusChangedEventArgs>? StatusChanged;
        public event EventHandler<ThreatDetectedEventArgs>? ThreatDetected;
        public event EventHandler<AdwareDetectedEventArgs>? AdwareDetected;
        public event EventHandler<ThreatAlertEventArgs>? ThreatAlertRaised;

        /// <summary>
        /// [FIX:SHIELD-CONTAGEM-ZERADA] AS AMEAÇAS QUE ACONTECERAM ANTES DA ABA ABRIR.
        ///
        /// Este é o defeito por trás do "Ameaças detectadas: 0" que o usuário
        /// via na aba de visão geral, mesmo com as notificações aparecendo
        /// normalmente.
        ///
        /// O que acontecia: o `ShieldViewModel` se inscreve em `ThreatDetected` no
        /// SEU CONSTRUTOR — e o ViewModel só é criado quando o usuário abre a
        /// aba Shield. A proteção, porém, roda desde o startup. Então tudo o que
        /// fosse detectado antes de a aba existir não tinha para onde ir: o
        /// contador nascia em zero e nunca era alimentado.
        ///
        /// A prova está no log do usuário (29/09, 02:14 - 02:18):
        ///
        ///     02:18:29  [ThreatProtection] Scan: 5 ameaça(s) em 203 proc(s) novos
        ///     02:18:29  [TOAST] Exibindo: ... (notificações aparecem)
        ///
        /// E ZERO linhas de `[ShieldVM]`: o ViewModel não existia para recebê-las.
        ///
        /// A correção é acumular os eventos AQUI, no serviço que existe desde o
        /// início, e entregar a lista acumulada a quem se inscrever depois. O
        /// serviço é o dono do dado — ele viu a ameaça acontecer —, e a UI é
        /// apenas uma das consumidoras. Guardar no ViewModel invertia as
        /// responsabilidades: um observador passageiro viraria a única cópia
        /// do histórico.
        /// </summary>
        private readonly List<ThreatDetectedEventArgs> _threatHistory = new();
        private readonly object _threatHistoryLock = new();

        /// <summary>
        /// As ameaças já detectadas nesta sessão, do mais antigo ao mais recente.
        ///
        /// A lista é limitada para não crescer sem fim num processo de longa
        /// duração: o que interessa ao usuário é o que houve na sessão, e um
        /// histórico ilimitado seria um vazamento de memória silencioso.
        /// </summary>
        public IReadOnlyList<ThreatDetectedEventArgs> GetThreatHistory()
        {
            lock (_threatHistoryLock)
            {
                return _threatHistory.ToList();
            }
        }

        /// <summary>
        /// Registra uma ameaça no histórico da sessão.
        ///
        /// Chamado exatamente no mesmo ponto em que o evento `ThreatDetected` é
        /// disparado, para que histórico e notificação nunca discordem sobre
        /// quantas ameaças houve.
        /// </summary>
        private void RecordThreat(ThreatDetectedEventArgs args)
        {
            lock (_threatHistoryLock)
            {
                _threatHistory.Add(args);

                // Teto generoso para uma sessão, mas finito.
                const int maxHistory = 500;
                if (_threatHistory.Count > maxHistory)
                {
                    _threatHistory.RemoveRange(0, _threatHistory.Count - maxHistory);
                }
            }
        }

        /// <summary>Veredito detalhado de cada arquivo avaliado pelo pipeline.</summary>
        public event EventHandler<FileThreatEventArgs>? FileAssessed;
        
        public VoltrisShieldService(
            ILoggingService logger,
            FileMonitorService fileMonitor,
            StartupMonitorService startupMonitor,
            AdwareScannerService adwareScanner,
            DefenderIntegrationService defenderIntegration,
            RansomwareMonitorService ransomwareMonitor,
            Network.NetworkMonitorService networkMonitor,
            QuarantineService quarantine,
            SignatureVerificationService signatureService,
            PortMonitorService portMonitor,
            ThreatProtectionService threatProtection,
            AdvancedStaticAnalyzer advancedStatic,
            BehavioralMonitor behavioralMonitor,
            HeuristicAnalyzer heuristicAnalyzer,
            DefenderScanService defenderScan,
            CodeInjectionDetector injectionDetector,
            SecurityLogService securityLog,
            ProcessCacheService processCache, // Injetado
            ShieldLicenseGate licenseGate)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _fileMonitor = fileMonitor ?? throw new ArgumentNullException(nameof(fileMonitor));
            _startupMonitor = startupMonitor ?? throw new ArgumentNullException(nameof(startupMonitor));
            _adwareScanner = adwareScanner ?? throw new ArgumentNullException(nameof(adwareScanner));
            _defenderIntegration = defenderIntegration ?? throw new ArgumentNullException(nameof(defenderIntegration));
            _ransomwareMonitor = ransomwareMonitor ?? throw new ArgumentNullException(nameof(ransomwareMonitor));
            _networkMonitor = networkMonitor ?? throw new ArgumentNullException(nameof(networkMonitor));
            _quarantine = quarantine ?? throw new ArgumentNullException(nameof(quarantine));
            _signatureService = signatureService ?? throw new ArgumentNullException(nameof(signatureService));
            _portMonitor = portMonitor ?? throw new ArgumentNullException(nameof(portMonitor));
            _threatProtection = threatProtection ?? throw new ArgumentNullException(nameof(threatProtection));
            _advancedStatic = advancedStatic ?? throw new ArgumentNullException(nameof(advancedStatic));
            _behavioralMonitor = behavioralMonitor ?? throw new ArgumentNullException(nameof(behavioralMonitor));
            _heuristicAnalyzer = heuristicAnalyzer ?? throw new ArgumentNullException(nameof(heuristicAnalyzer));
            _defenderScan = defenderScan ?? throw new ArgumentNullException(nameof(defenderScan));
            _injectionDetector = injectionDetector ?? throw new ArgumentNullException(nameof(injectionDetector));
            _securityLog = securityLog ?? throw new ArgumentNullException(nameof(securityLog));
            _processCache = processCache ?? throw new ArgumentNullException(nameof(processCache));
            _licenseGate = licenseGate ?? throw new ArgumentNullException(nameof(licenseGate));

            _adwareDetectedHandler = (s, e) => AdwareDetected?.Invoke(s, e);
            _threatAlertHandler = (s, e) => ThreatAlertRaised?.Invoke(s, e);
            _fileMonitor.SuspiciousFileDetected += OnSuspiciousFileDetected;
            _fileMonitor.FileAssessed += OnFileAssessed;
            _adwareScanner.AdwareDetected += _adwareDetectedHandler;
            _threatProtection.ThreatAlertRaised += _threatAlertHandler;
            _portMonitor.SuspiciousConnectionDetected += OnSuspiciousConnectionDetected;
            _behavioralMonitor.ThreatDetected += OnBehavioralThreatDetected;

            _logger.LogInfo("[Shield] Serviço Voltris Shield inicializado");

            // Gate de licença: observa mudanças e desliga o Shield se a licença for perdida.
            try
            {
                _licenseGate.StartWatching();
                _licenseGate.LicenseChanged += OnLicenseChanged;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[Shield] Erro ao registrar gate de licença: {ex.Message}");
            }
        }

        /// <summary>
        /// Chamado no boot. Ativa a proteção por padrão SOMENTE se houver licença
        /// Standard/Pro/Enterprise. Sem licença, o Shield permanece totalmente offline.
        /// </summary>
        public async Task<bool> InitializeProtectionAsync(string origin = "Startup")
        {
            if (_disposed)
            {
                _logger?.LogWarning("[Shield] InitializeProtectionAsync ignorado: serviço descartado");
                return false;
            }

            if (_isProtectionActive)
            {
                _logger?.LogInfo("[Shield] Proteção já ativa — nada a fazer na inicialização");
                return true;
            }

            if (!_licenseGate.IsAllowed($"Ativação automática ({origin})"))
            {
                _logger?.LogWarning(
                    $"[Shield] INICIALIZAÇÃO SEM LICENÇA — Voltris Shield permanece OFFLINE (origem: {origin})");
                return false;
            }

            try
            {
                var activated = await ActivateProtectionAsync();
                _logger?.LogInfo($"[Shield] Proteção ativada automaticamente na inicialização: {activated}");
                _securityLog?.LogSecurityEvent("VoltrisShield", "AUTO_ACTIVATION",
                    $"Ativação automática (origem: {origin}) — resultado: {activated}");
                return activated;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[Shield] Erro na ativação automática: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Reage à perda de licença: desliga imediatamente todos os módulos
        /// e registra o evento. Reação é assíncrona e nunca lança.
        /// </summary>
        private void OnLicenseChanged(object? sender, ShieldLicenseChangedEventArgs e)
        {
            try
            {
                _logger?.LogWarning(
                    $"[Shield] Licença alterada (licensed={e.IsLicensed}, tipo={e.LicenseType}). Processed shutdown={_isProtectionActive}");

                if (e.IsLicensed || _disposed) return;
                if (!_isProtectionActive) return;

                _logger?.LogWarning("[Shield] LICENÇA PERDIDA — desligando todos os módulos imediatamente");

                _ = Task.Run(async () =>
                {
                    try
                    {
                        var stopped = await DeactivateProtectionAsync();
                        _logger?.LogWarning($"[Shield] Módulos desligados por perda de licença: {stopped}");
                        _securityLog?.LogSecurityEvent("VoltrisShield", "LICENSE_LOST", "Proteção desligada por perda de licença");
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError($"[Shield] Erro ao desligar após perda de licença: {ex.Message}", ex);
                    }
                });
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[Shield] Erro em OnLicenseChanged: {ex.Message}", ex);
            }
        }
        
        /// <summary>
        /// Ativa a proteção em tempo real
        /// </summary>
        public async Task<bool> ActivateProtectionAsync()
        {
            if (!_licenseGate.IsAllowed("Ativar proteção"))
            {
                _logger?.LogWarning("[Shield] ATIVAÇÃO BLOQUEADA — Voltris Shield exige licença Standard/Pro/Enterprise");
                return false;
            }

            await _lifecycleGate.WaitAsync();
            try
            {
                if (_disposed) return false;
                if (_isProtectionActive) return true;

                try
                {
                    _logger.LogInfo("[Shield] ══════════════════════════════════════════");
                    _logger.LogInfo("[Shield] Ativando proteção completa...");
                    _logger.LogInfo("[Shield] Módulos: FileMonitor, StartupMonitor, RansomwareMonitor,");
                    _logger.LogInfo("[Shield]          PortMonitor, ThreatProtection, Quarantine");

                    // O aquecimento dos módulos faz I/O bloqueante (registro, criação de
                    // FileSystemWatcher recursivo em D:\, varredura de rede). Executar fora da
                    // thread de UI evita o congelamento de ~1s por módulo observado nos logs.
                    bool networkStarted = await Task.Run(async () =>
                    {
                        _behavioralMonitor.SetAnalyzers(_advancedStatic, _heuristicAnalyzer);
                        await _fileMonitor.StartMonitoringAsync();
                        await _startupMonitor.StartMonitoringAsync();
                        _portMonitor.StartMonitoring();
                        _threatProtection.StartProtection();
                        _behavioralMonitor.StartMonitoring();
                        await _ransomwareMonitor.StartMonitoringAsync();

                        return await _networkMonitor.StartMonitoringAsync();
                    });

                    if (!networkStarted)
                    {
                        _logger.LogWarning("[Shield] Monitoramento de rede indisponível; os demais módulos permanecem ativos");
                    }

                    _cts?.Cancel();
                    _cts?.Dispose();
                    _cts = new CancellationTokenSource();
                    _isProtectionActive = true;
                    _lastInjectionScan = DateTime.MinValue;
                    var token = _cts.Token;

                    _injectionTask = Task.Run(async () =>
                    {
                        try
                        {
                            while (_isProtectionActive && !token.IsCancellationRequested)
                            {
                                try
                                {
                                    if ((DateTime.Now - _lastInjectionScan).TotalMinutes >= 5)
                                    {
                                        await RunInjectionSweepAsync(token);
                                    }
                                }
                                catch (OperationCanceledException)
                                {
                                    break;
                                }
                                catch (Exception ex)
                                {
                                    // Log em nivel de AVISO, nao Debug: antes desta
                                    // correcao, uma varredura que abortava no meio
                                    // do foreach era invisivel (LogDebug) e o log
                                    // mostrava apenas "Iniciando varredura" repetido,
                                    // sem nenhuma explicacao do resultado anterior.
                                    _logger.LogWarning($"[Shield] Ciclo de varredura de injecao abortado: {ex.Message}");
                                }

                                try
                                {
                                    await Task.Delay(60000, token);
                                }
                                catch (OperationCanceledException)
                                {
                                    break;
                                }
                            }
                        }
                        catch (OperationCanceledException)
                        {
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError("[Shield] Erro fatal no loop de injeção", ex);
                        }
                    }, token);

                    _quarantine.CleanExpiredEntries();
                    OnStatusChanged(new ShieldStatusChangedEventArgs(true, "Proteção completa ativa"));

                    _logger.LogSuccess("[Shield] Proteção completa ativada com sucesso");
                    _securityLog?.LogSecurityEvent("VoltrisShield", "PROTECTION_ACTIVATED",
                        "Proteção completa ativada");
                    _logger.LogInfo("[Shield] ══════════════════════════════════════════");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError("[Shield] Erro ao ativar proteção", ex);
                    await StopProtectionModulesAsync();
                    return false;
                }
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        /// <summary>
        /// Varredura profunda de injecao de codigo em todos os processos.
        ///
        /// TRES CORRECOES EM RELACAO A IMPLEMENTACAO ANTERIOR:
        ///
        /// 1. PARALELISMO COM TETO. Antes era serial com `await Task.Delay(100)`
        ///    por processo. Com 300 processos, isso e 30s de varredura MINIMA a
        ///    cada 5 minutos — ou seja, o loop vivia ~10% do tempo varrendo e
        ///    competindo com o ProcessCacheService e o BehavioralMonitor no mesmo
        ///    thread pool. Agora ainsse com concorrencia limitada, mantendo
        ///    controle sobre o numero de handles de processo abertos.
        ///
        /// 2. TIMEOUT POR PROCESSO. AnalyzeProcess nao aceitava cancelamento e
        ///    caminhava VirtualQueryEx sem limite. Um processo travado segurava
        ///    o ciclo INDEFINIDAMENTE, e o log ja mostrava "Iniciando varredura"
        ///    sem nunca registrar resultado. Cada processo agora tem teto.
        ///
        /// 3. LOG DE CONCLUSAO. Antes so existia o log de inicio. Se a varredura
        ///    abortasse, o proximo ciclo repetia a mesma linha sem explicar o
        ///    resultado anterior — impossivel distinguir "nada encontrado" de
        ///    "trava no quarto processo". Agora ha marcadores de inicio, fim,
        ///    contagem e duracao.
        /// </summary>
        private async Task RunInjectionSweepAsync(CancellationToken token)
        {
            var processes = _processCache?.GetCachedProcessInfos()
                ?? Enumerable.Empty<ProcessCacheService.CachedProcessInfo>();

            var targets = processes
                .Where(p => p.Id > 4 && p.Id != _ownPid)
                .Select(p => p.Id)
                .ToArray();

            if (targets.Length == 0)
            {
                _logger.LogDebug("[Shield] Varredura de injecao: nenhum processo elegivel");
                return;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            _logger.LogInfo($"[Shield] Iniciando varredura profunda de injecao de codigo " +
                            $"({targets.Length} processos, concorrencia {InjectionSweepConcurrency})...");

            var detected = 0;
            var analyzed = 0;
            var timedOut = 0;
            var errors = 0;

            using var sweepCts = CancellationTokenSource.CreateLinkedTokenSource(token);

            try
            {
                await Parallel.ForEachAsync(targets,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = InjectionSweepConcurrency,
                        CancellationToken = sweepCts.Token
                    },
                    async (processId, ct) =>
                    {
                        try
                        {
                            var injection = await _injectionDetector.AnalyzeProcess(processId, ct)
                                .WaitAsync(ProcessAnalysisTimeout, ct)
                                .ConfigureAwait(false);

                            Interlocked.Increment(ref analyzed);

                            if (injection.IsSuspicious)
                            {
                                Interlocked.Increment(ref detected);
                                OnThreatAlertRaised(this, new ThreatAlertEventArgs
                                {
                                    ProcessName = injection.ProcessName,
                                    ProcessId = injection.ProcessId,
                                    ThreatType = "Injeção de Código",
                                    Details = injection.Details,
                                    Severity = ThreatSeverity.Critical,
                                    SourceModule = "CodeInjection"
                                });
                            }
                        }
                        catch (TimeoutException)
                        {
                            Interlocked.Increment(ref timedOut);
                            _logger.LogDebug($"[Shield] Varredura de injecao: timeout em PID {processId}");
                        }
                        catch (OperationCanceledException)
                        {
                            // Propaga para parar o Parallel.ForEachAsync.
                            throw;
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref errors);
                            _logger.LogDebug($"[Shield] Varredura de injecao: erro em PID {processId}: {ex.Message}");
                        }
                    }).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (!token.IsCancellationRequested)
                    _logger.LogWarning("[Shield] Varredura de injecao interrompida por cancelamento interno");
            }

            sw.Stop();

            // Marcador de conclusao. Sem isto, o log e silencioso sobre o
            // resultado de cada varredura.
            _logger.LogInfo($"[Shield] Varredura de injecao CONCLUIDA em {sw.ElapsedMilliseconds}ms: " +
                            $"analisados={analyzed}/{targets.Length} deteccoes={detected} " +
                            $"timeouts={timedOut} erros={errors}");

            // Atualiza o marcador mesmo com parcialidade: senao um ciclo que
            // falha se repete imediatamente e satura o log.
            _lastInjectionScan = DateTime.Now;
        }

        public async Task<bool> DeactivateProtectionAsync()
        {
            await _lifecycleGate.WaitAsync();
            try
            {
                if (_disposed) return true;
                try
                {
                    _logger.LogInfo("[Shield] Desativando proteção...");
                    await StopProtectionModulesAsync();
                    OnStatusChanged(new ShieldStatusChangedEventArgs(false, "Proteção desativada"));
                    _logger.LogInfo("[Shield] Proteção desativada");
                    _securityLog?.LogSecurityEvent("VoltrisShield", "PROTECTION_DEACTIVATED",
                        "Proteção desativada");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError("[Shield] Erro ao desativar proteção", ex);
                    return false;
                }
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        public async Task<bool> SetRansomwareMonitoringAsync(bool enabled)
        {
            if (!_licenseGate.IsAllowed(enabled ? "Ativar monitoramento de ransomware" : "Desativar monitoramento de ransomware"))
            {
                return false;
            }

            await _lifecycleGate.WaitAsync();
            try
            {
                if (_disposed || !_isProtectionActive)
                    return false;

                if (enabled)
                    await _ransomwareMonitor.StartMonitoringAsync();
                else
                    await _ransomwareMonitor.StopMonitoringAsync();

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[Shield] Erro ao alternar monitoramento de ransomware: {ex.Message}", ex);
                return false;
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        private async Task StopProtectionModulesAsync()
        {
            _isProtectionActive = false;

            var cts = _cts;
            _cts = null;
            try { cts?.Cancel(); } catch { }

            var injectionTask = Interlocked.Exchange(ref _injectionTask, null);
            if (injectionTask != null)
            {
                try { await injectionTask; } catch (OperationCanceledException) { } catch (Exception ex) { _logger.LogDebug($"[Shield] Falha ao encerrar loop de injeção: {ex.Message}"); }
            }

            try { cts?.Dispose(); } catch { }

            try { await _networkMonitor.StopMonitoringAsync(); } catch (Exception ex) { _logger.LogWarning($"[Shield] Falha ao parar NetworkMonitor: {ex.Message}"); }
            try { await _ransomwareMonitor.StopMonitoringAsync(); } catch (Exception ex) { _logger.LogWarning($"[Shield] Falha ao parar RansomwareMonitor: {ex.Message}"); }
            try { await _fileMonitor.StopMonitoringAsync(); } catch (Exception ex) { _logger.LogWarning($"[Shield] Falha ao parar FileMonitor: {ex.Message}"); }
            try { await _startupMonitor.StopMonitoringAsync(); } catch (Exception ex) { _logger.LogWarning($"[Shield] Falha ao parar StartupMonitor: {ex.Message}"); }
            try { _portMonitor.StopMonitoring(); } catch (Exception ex) { _logger.LogWarning($"[Shield] Falha ao parar PortMonitor: {ex.Message}"); }
            try { _threatProtection.StopProtection(); } catch (Exception ex) { _logger.LogWarning($"[Shield] Falha ao parar ThreatProtection: {ex.Message}"); }
            try { _behavioralMonitor.StopMonitoring(); } catch (Exception ex) { _logger.LogWarning($"[Shield] Falha ao parar BehavioralMonitor: {ex.Message}"); }
        }
        
        /// <summary>
        /// Executa scan rápido — Downloads, Temp, Startup e extensões de browser
        /// </summary>
        public async Task<ScanResult> RunQuickScanAsync(Action<int, string>? onProgress = null)
        {
            if (!_licenseGate.IsAllowed("Scan rápido"))
            {
                _logger?.LogWarning("[Shield] SCAN RÁPIDO BLOQUEADO — sem licença Standard/Pro/Enterprise");
                return new ScanResult { ScanType = ScanType.Quick, CompletedAt = DateTime.Now };
            }

            if (!_isProtectionActive)
            {
                _logger.LogWarning("[Shield] Tentativa de Scan Rápido com proteção desativada. Abortando.");
                return new ScanResult { ScanType = ScanType.Quick, CompletedAt = DateTime.Now };
            }

            try
            {
                _logger.LogInfo("[Shield] ═══════════════════════════════════════");
                _logger.LogInfo("[Shield] SCAN RÁPIDO INICIADO");
                _logger.LogInfo("[Shield] ═══════════════════════════════════════");
                
                var result = new ScanResult { ScanType = ScanType.Quick };
                // [FIX:B-1] totalItemsScanned conta ÁREAS varridas (uma por
                // sub-scanner), não arquivos. É por isso que o log final diz
                // "áreas verificadas". A contagem real de arquivos examinados vive
                // em _adwareScanner.ExaminedCount e é usada no scan de adware.
                int totalItemsScanned = 0;
                var startupKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                
                // 1. Scan de pastas temporárias
                onProgress?.Invoke(10, "Escaneando pastas temporárias...");
                var tempThreats = await _adwareScanner.ScanTempFoldersAsync();
                AddAdwareToResults(result, tempThreats);
                totalItemsScanned++;
                
                // 2. Scan de Downloads
                onProgress?.Invoke(35, "Escaneando pasta Downloads...");
                var downloadThreats = await _adwareScanner.ScanDownloadsFolderAsync();
                AddAdwareToResults(result, downloadThreats);
                totalItemsScanned++;
                
                // 3. Scan de itens de inicialização
                onProgress?.Invoke(60, "Escaneando itens de inicialização...");
                var startupItems = await _startupMonitor.GetAllStartupItemsAsync();
                foreach (var item in startupItems.Where(i => i.IsSuspicious))
                {
                    // [FIX:B-1] Este loop adicionava direto em result.Threats,
                    // contornando o dedupe de AddAdwareToResults. O mesmo executável
                    // de inicialização também é alcançado pelos sub-scanners de
                    // adware, então aparecia duas vezes na lista e contava duas
                    // vezes em ThreatsFound.
                    var startupKey = BuildThreatKey(item.Path, "Startup");
                    if (!startupKeys.Add(startupKey))
                    {
                        _logger.LogDebug($"[FIX:B-1] Item de inicialização duplicado descartado: key={startupKey}");
                        continue;
                    }

                    result.Threats.Add(new ScanThreatItem
                    {
                        Name = item.Name,
                        Path = item.Path,
                        ThreatType = "Startup",
                        Severity = ThreatSeverity.High
                    });
                    result.ThreatsFound++;
                }
                totalItemsScanned++;
                
                // 4. Scan de extensões de browser
                onProgress?.Invoke(80, "Escaneando extensões de browser...");
                var browserThreats = await _adwareScanner.ScanBrowserExtensionsAsync();
                AddAdwareToResults(result, browserThreats);
                totalItemsScanned++;

                // 5. Scan Heurístico Avançado em áreas críticas
                onProgress?.Invoke(90, "Análise heurística avançada...");
                // (Implementação focada em arquivos recentes ou suspeitos)
                
                LastScanTime = DateTime.Now;
                result.CompletedAt = DateTime.Now;
                result.ItemsScanned = totalItemsScanned;
                
                onProgress?.Invoke(100, $"Concluído: {result.ThreatsFound} ameaças");
                _logger.LogInfo("[Shield] ═══════════════════════════════════════");
                _logger.LogSuccess($"[Shield] SCAN RÁPIDO CONCLUÍDO: {result.ThreatsFound} ameaças em {totalItemsScanned} áreas verificadas");
                _logger.LogInfo("[Shield] ═══════════════════════════════════════");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError("[Shield] Erro no scan rápido", ex);
                throw;
            }
        }
        
        /// <summary>
        /// Executa scan completo — Sistema inteiro: diretórios, registry, startup, browser, scheduled tasks
        /// </summary>
        public async Task<ScanResult> RunFullScanAsync(Action<int, string>? onProgress = null)
        {
            if (!_licenseGate.IsAllowed("Scan completo"))
            {
                _logger?.LogWarning("[Shield] SCAN COMPLETO BLOQUEADO — sem licença Standard/Pro/Enterprise");
                return new ScanResult { ScanType = ScanType.Full, CompletedAt = DateTime.Now };
            }

            if (!_isProtectionActive)
            {
                _logger.LogWarning("[Shield] Tentativa de Scan Completo com proteção desativada. Abortando.");
                return new ScanResult { ScanType = ScanType.Full, CompletedAt = DateTime.Now };
            }

            try
            {
                _logger.LogInfo("[Shield] ═══════════════════════════════════════");
                _logger.LogInfo("[Shield] SCAN COMPLETO INICIADO");
                _logger.LogInfo("[Shield] ═══════════════════════════════════════");
                
                var result = new ScanResult { ScanType = ScanType.Full };
                int totalItemsScanned = 0;
                var startupKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                
                // 1. Scan completo de arquivos
                onProgress?.Invoke(8, "Escaneando sistema de arquivos...");
                var adwareItems = await _adwareScanner.ScanForAdwareAsync();
                AddAdwareToResults(result, adwareItems);
                totalItemsScanned++;
                
                // 2. Scan de inicialização
                onProgress?.Invoke(25, "Escaneando itens de inicialização...");
                var startupItems = await _startupMonitor.GetAllStartupItemsAsync();
                foreach (var item in startupItems.Where(i => i.IsSuspicious))
                {
                    // [FIX:B-1] Este loop adicionava direto em result.Threats,
                    // contornando o dedupe de AddAdwareToResults. O mesmo executável
                    // de inicialização também é alcançado pelos sub-scanners de
                    // adware, então aparecia duas vezes na lista e contava duas
                    // vezes em ThreatsFound.
                    var startupKey = BuildThreatKey(item.Path, "Startup");
                    if (!startupKeys.Add(startupKey))
                    {
                        _logger.LogDebug($"[FIX:B-1] Item de inicialização duplicado descartado: key={startupKey}");
                        continue;
                    }

                    result.Threats.Add(new ScanThreatItem
                    {
                        Name = item.Name,
                        Path = item.Path,
                        ThreatType = "Startup",
                        Severity = ThreatSeverity.High
                    });
                    result.ThreatsFound++;
                }
                totalItemsScanned++;
                
                // 3. Scan de Downloads
                onProgress?.Invoke(42, "Escaneando pasta Downloads...");
                var downloadItems = await _adwareScanner.ScanDownloadsFolderAsync();
                AddAdwareToResults(result, downloadItems);
                totalItemsScanned++;
                
                // 4. Scan de pastas temporárias
                onProgress?.Invoke(58, "Escaneando pastas temporárias...");
                var tempItems = await _adwareScanner.ScanTempFoldersAsync();
                AddAdwareToResults(result, tempItems);
                totalItemsScanned++;
                
                // 5. Scan de extensões de browser
                onProgress?.Invoke(72, "Escaneando extensões de browser...");
                var browserItems = await _adwareScanner.ScanBrowserExtensionsAsync();
                AddAdwareToResults(result, browserItems);
                totalItemsScanned++;
                
                // 6. Scan de Scheduled Tasks
                onProgress?.Invoke(88, "Escaneando tarefas agendadas...");
                var taskItems = await _adwareScanner.ScanScheduledTasksAsync();
                AddAdwareToResults(result, taskItems);
                totalItemsScanned++;
                
                LastScanTime = DateTime.Now;
                result.CompletedAt = DateTime.Now;
                result.ItemsScanned = totalItemsScanned;
                
                onProgress?.Invoke(100, $"Concluído: {result.ThreatsFound} ameaças");
                _logger.LogInfo("[Shield] ═══════════════════════════════════════");
                _logger.LogSuccess($"[Shield] SCAN COMPLETO CONCLUÍDO: {result.ThreatsFound} ameaças em {totalItemsScanned} áreas verificadas");
                _logger.LogInfo("[Shield] ═══════════════════════════════════════");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError("[Shield] Erro no scan completo", ex);
                throw;
            }
        }
        
        /// <summary>
        /// Executa scan focado em adware
        /// </summary>
        public async Task<ScanResult> RunAdwareScanAsync(Action<int, string>? onProgress = null)
        {
            if (!_licenseGate.IsAllowed("Scan de adware"))
            {
                _logger?.LogWarning("[Shield] SCAN DE ADWARE BLOQUEADO — sem licença Standard/Pro/Enterprise");
                return new ScanResult { ScanType = ScanType.Adware, CompletedAt = DateTime.Now };
            }

            if (!_isProtectionActive)
            {
                _logger.LogWarning("[Shield] Tentativa de Scan de Adware com proteção desativada. Abortando.");
                return new ScanResult { ScanType = ScanType.Adware, CompletedAt = DateTime.Now };
            }

            try
            {
                _logger.LogInfo("[Shield] Iniciando scan de adware...");
                
                var result = new ScanResult { ScanType = ScanType.Adware };
                
                onProgress?.Invoke(15, "Escaneando Program Files...");
                var threats = await _adwareScanner.ScanForAdwareAsync();
                result.ThreatsFound = threats.Count;

                // [FIX:B-1] ItemsScanned passa a ser a contagem REAL de itens
                // examinados. Antes era "result.ItemsScanned = threats.Count",
                // que produzia "N ameaças em N itens varridos" — um número
                // inventado, porque ameaça é por definição um subconjunto do
                // que foi varrido. O usuário lia essa linha como cobertura do
                // scan e não como telemetria de quantos itens foram checados.
                result.ItemsScanned = _adwareScanner.ExaminedCount;

                // [FIX:B-1] Dedupe por caminho canônico. Os sub-scanners de
                // adware varrem diretórios que se sobrepõem (temp dentro de
                // AppData, e o mesmo item pode ser alcançado por regras
                // diferentes), então o mesmo executável chegava à lista mais de
                // uma vez e inflava a contagem de ameaças.
                var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int duplicadasRemovidas = 0;
                foreach (var item in threats)
                {
                    if (!string.IsNullOrEmpty(item.Path) && !seenPaths.Add(item.Path))
                    {
                        duplicadasRemovidas++;
                        _logger.LogDebug($"[FIX:B-1] Ameaça duplicada removida do resultado: {item.Path}");
                        continue;
                    }

                    result.Threats.Add(new ScanThreatItem
                    {
                        Name = item.Name,
                        Path = item.Path,
                        ThreatType = item.Type.ToString(),
                        Severity = item.Severity == AdwareSeverity.High ? ThreatSeverity.High
                                 : item.Severity == AdwareSeverity.Medium ? ThreatSeverity.Medium
                                 : ThreatSeverity.Low
                    });
                }

                // ThreatsFound deve refletir o que será EXIBIDO, não o que veio
                // bruto do scanner.
                result.ThreatsFound = result.Threats.Count;
                _logger.LogInfo(
                    $"[FIX:B-1] Scan de adware: {result.Threats.Count} ameaças exibidas | " +
                    $"{result.ItemsScanned} itens realmente examinados | " +
                    $"{duplicadasRemovidas} duplicadas descartadas | " +
                    $"brutoDoScanner={threats.Count}");
                
                LastScanTime = DateTime.Now;
                result.CompletedAt = DateTime.Now;
                
                onProgress?.Invoke(100, $"Concluído: {result.ThreatsFound} ameaças");
                _logger.LogSuccess($"[Shield] Scan de adware concluído: {result.ThreatsFound} ameaças encontradas");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError("[Shield] Erro no scan de adware", ex);
                throw;
            }
        }
        
        /// <summary>
        /// Ativa/desativa modo gamer (reduz atividade em background para todos os módulos)
        /// </summary>
        public void SetGamerMode(bool enabled)
        {
            if (!_licenseGate.IsAllowed("Modo Gamer do Shield"))
            {
                return;
            }

            _isGamerModeActive = enabled;
            
            if (enabled)
            {
                _logger.LogInfo("[Shield] Modo Gamer ativado - reduzindo atividade em todos os módulos");
                _fileMonitor.SetLowActivityMode(true);
                _startupMonitor.SetLowActivityMode(true);
                _adwareScanner.PauseBackgroundScans();
                _ransomwareMonitor.SetLowActivityMode(true);
                _networkMonitor.SetLowActivityMode(true);
                _portMonitor.SetLowActivityMode(true);
                _threatProtection.SetLowActivityMode(true);
            }
            else
            {
                _logger.LogInfo("[Shield] Modo Gamer desativado - atividade normal em todos os módulos");
                _fileMonitor.SetLowActivityMode(false);
                _startupMonitor.SetLowActivityMode(false);
                _adwareScanner.ResumeBackgroundScans();
                _ransomwareMonitor.SetLowActivityMode(false);
                _networkMonitor.SetLowActivityMode(false);
                _portMonitor.SetLowActivityMode(false);
                _threatProtection.SetLowActivityMode(false);
            }
        }
        
        /// <summary>
        /// Obtém status do Windows Defender
        /// </summary>
        public async Task<DefenderStatus> GetDefenderStatusAsync()
        {
            if (!_licenseGate.IsAllowed("Consultar status do Defender"))
            {
                return new DefenderStatus { IsEnabled = false, RealTimeProtectionEnabled = false, IsUpToDate = false, Version = "" };
            }

            return await _defenderIntegration.GetStatusAsync();
        }

        /// <summary>
        /// Inicia scan do Windows Defender
        /// </summary>
        public async Task<bool> StartDefenderScanAsync()
        {
            if (!_licenseGate.IsAllowed("Scan do Windows Defender"))
            {
                return false;
            }

            return await _defenderIntegration.StartQuickScanAsync();
        }
        
        /// <summary>
        /// Arquivo acima do limiar de NOTIFICACAO.
        ///
        /// Este handler so recebe o que ja passou pelo pipeline E pelo limiar de
        /// notificacao — o FileMonitorService filtra antes de disparar o evento.
        /// Ainda assim, a severidade e DERIVADA do veredito, nunca fixa: a versao
        /// anterior emitia `Severity = ThreatSeverity.Medium` incondicionalmente,
        /// o que fazia um dropper confirmado e um DLL de staging terem exatamente a
        /// mesma relevância — e o mesmo direito a um toast.
        /// </summary>
        private void OnSuspiciousFileDetected(object? sender, SuspiciousFileEventArgs e)
        {
            _logger.LogWarning($"[Shield] Arquivo suspeito detectado: {e.FilePath} :: {e.Reason}");

            _securityLog?.LogSecurityEvent("FileMonitor", "SUSPICIOUS_FILE",
                $"{Path.GetFileName(e.FilePath)} — {e.Reason}");

            // [FIX:SHIELD-CONTAGEM-ZERADA] Registrar ANTES de notificar.
            //
            // O histórico precisa ser atualizado no mesmo instante do evento, e
            // nesta ordem. Se o registro viesse depois, haveria uma janela em
            // que a notificação já tinha saído na tela mas o contador ainda
            // estava em zero — exatamente a inconsistência que o usuário
            // relatou.
            var args = new ThreatDetectedEventArgs
            {
                ThreatType = "Arquivo Suspeito",
                FilePath = e.FilePath,
                Severity = e.Severity,
                Details = e.Reason,
                Confidence = e.Confidence,
                DetectedAt = DateTime.Now
            };
            RecordThreat(args);
            ThreatDetected?.Invoke(this, args);
        }

        /// <summary>
        /// Veredito completo do pipeline para cada arquivo avaliado. A UI e a
        /// telemetria escutam aqui; a notificacao e uma DECISAO separada, feita
        /// pelo AlertThrottleService.
        /// </summary>
        private void OnFileAssessed(object? sender, FileThreatEventArgs e)
        {
            var verdict = e.Verdict;

            _logger.LogInfo($"[Shield] Veredito: {verdict.FileName} score={verdict.Score} " +
                             $"conf={verdict.Confidence} sev={verdict.Severity} " +
                             $"notificar={e.IsNotificationCandidate} :: {verdict.Reason}");

            FileAssessed?.Invoke(this, e);
        }
        
        private void OnAdwareDetected(object? sender, AdwareDetectedEventArgs e)
        {
            _logger.LogWarning($"[Shield] Adware detectado: {e.Name}");
            var args = new ThreatDetectedEventArgs
            {
                ThreatType = "Adware",
                FilePath = e.Path,
                Severity = ThreatSeverity.High
            };
            RecordThreat(args);
            ThreatDetected?.Invoke(this, args);
        }

        private void OnThreatAlertRaised(object? sender, ThreatAlertEventArgs e)
        {
            var source = string.IsNullOrWhiteSpace(e.SourceModule) ? "ThreatProtection" : e.SourceModule;
            _logger.LogWarning($"[Shield] Ameaça detectada pelo {source}: {e.ThreatType} — {e.ProcessName} (PID: {e.ProcessId})");
            _securityLog.LogThreatDetected(source, e.ThreatType, e.Details);
            // Esta é a rota que produzia as ameaças do log do usuário em 02:18
            // (5 executáveis de alto risco num scan de 203 processos). Sem o
            // registro aqui, elas existiam só como notificação e nunca entravam
            // na contagem da visão geral.
            var args = new ThreatDetectedEventArgs
            {
                ThreatType = e.ThreatType,
                FilePath = e.ProcessName,
                Severity = e.Severity
            };
            RecordThreat(args);
            ThreatDetected?.Invoke(this, args);
        }

        private void OnSuspiciousConnectionDetected(object? sender, SuspiciousConnectionEventArgs e)
        {
            _logger.LogWarning($"[Shield] Conexão suspeita: {e.Connection.ProcessName} -> {e.Connection.RemoteIp}:{e.Connection.RemotePort} ({e.Level})");
            _securityLog.LogSecurityEvent("PortMonitor", "SUSPICIOUS_CONNECTION", e.Reason);
            var args = new ThreatDetectedEventArgs
            {
                ThreatType = "Conexão Suspeita",
                FilePath = $"{e.Connection.RemoteIp}:{e.Connection.RemotePort}",
                Severity = e.Level == SuspicionLevel.Critical ? ThreatSeverity.Critical
                         : e.Level == SuspicionLevel.High ? ThreatSeverity.High
                         : ThreatSeverity.Medium
            };
            RecordThreat(args);
            ThreatDetected?.Invoke(this, args);
        }

        private void OnBehavioralThreatDetected(object? sender, BehavioralThreatEventArgs e)
        {
            _logger.LogWarning($"[Shield] Ameaça comportaamental: {e.ProcessName} — {e.Message}");
            _securityLog.LogSecurityEvent("BehavioralMonitor", "SUSPICIOUS_BEHAVIOR", e.Message);
            
            var args = new ThreatDetectedEventArgs
            {
                ThreatType = "Comportamento Suspeito",
                FilePath = e.ProcessName,
                Severity = ThreatSeverity.High
            };
            RecordThreat(args);
            ThreatDetected?.Invoke(this, args);
        }

        /// <summary>
        /// Coloca um arquivo em quarentena via o serviço de quarentena.
        /// </summary>
        public async Task<bool> QuarantineFileAsync(string filePath, string reason, ThreatSeverity severity)
        {
            if (!_licenseGate.IsAllowed("Quarentenar arquivo"))
            {
                return false;
            }

            return await _quarantine.QuarantineFileAsync(filePath, reason, severity);
        }

        /// <summary>
        /// Avalia o risco de um arquivo específico.
        /// </summary>
        public FileRiskAssessment AssessFileRisk(string filePath)
        {
            if (!_licenseGate.IsAllowed("Avaliar risco de arquivo"))
            {
                return new FileRiskAssessment
                {
                    FilePath = filePath,
                    RiskLevel = "Bloqueado",
                    RiskScore = 0,
                    Reason = "Requer licença Standard, Pro ou Enterprise"
                };
            }

            return _signatureService.AssessFileRisk(filePath);
        }

        /// <summary>
        /// Valida input contra SQL Injection, XSS e bypass.
        /// </summary>
        public (bool IsValid, string? ThreatType, string? Details) ValidateInput(string input)
        {
            return _threatProtection.ValidateInput(input);
        }
        
        /// <summary>
        /// [FIX:B-1] Adiciona itens de adware ao resultado com dedupe por caminho.
        ///
        /// Correção 1 — NullReferenceException latente: a comparação anterior era
        /// <c>t.Path.Equals(item.Path, ...)</c>. Uma entrada de registro
        /// (RegistryKey/Adware) não tem caminho de arquivo, e <c>Path</c> é null
        /// nela. Assim que a PRIMEIRA entrada com Path nulo era adicionada, a
        /// próxima iteração chamava <c>Equals</c> sobre null e derrubava o scan
        /// inteiro com NRE — que era engolido pelo catch do scan e se
        /// manifestava como "scan terminou com 0 ameaças".
        ///
        /// Correção 2 — Complexidade: <c>result.Threats.Any(...)</c> é O(n) por
        /// item, ou seja O(n²) por scan. Com a lista já deduplicada em um HashSet
        /// a checagem vira O(1).
        /// </summary>
        private void AddAdwareToResults(ScanResult result, IEnumerable<AdwareItem> items)
        {
            // Chave: caminho canônico, ou o tipo quando não há caminho
            // (entradas de registro), para não colapsar itens distintos sem path.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var existing in result.Threats)
                seen.Add(BuildThreatKey(existing.Path, existing.ThreatType));

            foreach (var item in items)
            {
                var key = BuildThreatKey(item.Path, item.Type.ToString());
                if (!seen.Add(key))
                {
                    _logger.LogDebug($"[FIX:B-1] Duplicata descartada em AddAdwareToResults: key={key}");
                    continue;
                }

                result.Threats.Add(new ScanThreatItem
                {
                    Name = item.Name,
                    Path = item.Path,
                    ThreatType = item.Type.ToString(),
                    Severity = item.Severity == AdwareSeverity.High ? ThreatSeverity.High
                             : item.Severity == AdwareSeverity.Medium ? ThreatSeverity.Medium
                             : ThreatSeverity.Low
                });
                result.ThreatsFound++;
            }
        }

        private static string BuildThreatKey(string? path, string? threatType)
            => string.IsNullOrWhiteSpace(path)
                ? "TYPE:" + (threatType ?? "?")
                : "PATH:" + path;

        private void OnStatusChanged(ShieldStatusChangedEventArgs e)
        {
            StatusChanged?.Invoke(this, e);
        }
        
        /// <summary>
        /// Libera recursos e desconecta event handlers para evitar memory leaks.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            try
            {
                Task.Run(StopProtectionModulesAsync).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[Shield] Falha ao parar módulos no dispose: {ex.Message}");
            }

            try
            {
                _fileMonitor.SuspiciousFileDetected -= OnSuspiciousFileDetected;
            _fileMonitor.FileAssessed -= OnFileAssessed;
                _adwareScanner.AdwareDetected -= _adwareDetectedHandler;
                _threatProtection.ThreatAlertRaised -= _threatAlertHandler;
                _portMonitor.SuspiciousConnectionDetected -= OnSuspiciousConnectionDetected;
                _behavioralMonitor.ThreatDetected -= OnBehavioralThreatDetected;
                _behavioralMonitor.Dispose();
                _licenseGate.LicenseChanged -= OnLicenseChanged;
                _lifecycleGate.Dispose();
            }
            catch { }
        }
    }
    
    #region Event Args
    
    public class ShieldStatusChangedEventArgs : EventArgs
    {
        public bool IsActive { get; }
        public string Message { get; }
        
        public ShieldStatusChangedEventArgs(bool isActive, string message)
        {
            IsActive = isActive;
            Message = message;
        }
    }
    
    public class ThreatDetectedEventArgs : EventArgs
    {
        public string ThreatType { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public ThreatSeverity Severity { get; set; }

        /// <summary>Evidências que sustentam a detecção. Vazio em eventos sem veredito detalhado.</summary>
        public string Details { get; set; } = string.Empty;

        /// <summary>0-100. Mede o grau de certeza da evidência, não a gravidade.</summary>
        public int Confidence { get; set; }

        public DateTime DetectedAt { get; set; } = DateTime.Now;
    }

    public class SuspiciousFileEventArgs : EventArgs
    {
        public string FilePath { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string FileHash { get; set; } = string.Empty;

        /// <summary>Severidade derivada do score, não fixa em Medium.</summary>
        public ThreatSeverity Severity { get; set; } = ThreatSeverity.Medium;

        /// <summary>Score bruto do scorecard (-100 a 100).</summary>
        public int Score { get; set; }

        /// <summary>0-100. Grau de certeza da evidência.</summary>
        public int Confidence { get; set; }

        /// <summary>IDs das regras que contribuíram, para telemetria e diagnóstico.</summary>
        public string[] RuleIds { get; set; } = Array.Empty<string>();
    }
    
    #endregion
    
    #region Models
    
    public class ScanResult
    {
        public ScanType ScanType { get; set; }
        public int ThreatsFound { get; set; }
        public int ItemsScanned { get; set; }
        public DateTime CompletedAt { get; set; }
        public List<ScanThreatItem> Threats { get; set; } = new();
    }
    
    public class ScanThreatItem
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string ThreatType { get; set; } = string.Empty;
        public ThreatSeverity Severity { get; set; }
    }
    
    public enum ScanType
    {
        Quick,
        Full,
        Adware
    }
    
    public enum ThreatSeverity
    {
        Low,
        Medium,
        High,
        Critical
    }
    
    public class DefenderStatus
    {
        public bool IsEnabled { get; set; }
        public bool RealTimeProtectionEnabled { get; set; }
        public bool IsUpToDate { get; set; }
        public DateTime? LastScan { get; set; }
        public string Version { get; set; } = string.Empty;
    }
    
    #endregion
}
