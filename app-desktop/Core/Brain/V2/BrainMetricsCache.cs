using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using VoltrisOptimizer.Core.Body;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Core.Brain.V2;

/// <summary>
/// Cache de metricas do Brain para a UI. JA EXISTIA antes da reconstrucao visual
/// do rosto; a reconstrucao apenas EXPONDE aqui mais estado que o Brain ja
/// produzia e que a UI simplesmente nao enxergava.
///
/// REGRA ARQUITETURAL (item 14 do pedido): nada nesta classe DESCUBRE nada.
/// Cada propriedade abaixo ja era produzida pelo VoltrisBrainV2, pelo
/// BrainStateOrchestrator, pelo IVoltrisBody ou por eventos que o Brain ja
/// publicava sem ninguem escutar. Nenhum timer novo, nenhum sensor novo,
/// nenhum polling de hardware.
///
///   - <see cref="OperationalState"/> vem de <c>Brain.Orchestrator</c>
///     (BrainStateOrchestrator), que ja aplicava histerese de 30s.
///   - <see cref="OperationalContext"/> vem de <c>Brain.Body.CurrentContext</c>.
///   - <see cref="LastActionKind"/> vem de <c>Brain.LastAction.Kind</c>.
///   - <see cref="StutterEvents"/> vem de <c>Brain.Stats</c>.
///   - <see cref="FailureStreak"/> vem do evento <c>Brain.ActionExecuted</c>,
///     que existia e tinha ZERO assinantes.
///   - <see cref="LastSuccessUtc"/> / <see cref="LastErrorUtc"/> vemem do evento
///     <c>BrainObservabilityHub.EventPublished</c>, que tambem existia e tinha
///     ZERO assinantes.
///
/// Os dois eventos assinados sao, portanto, ruido que o Brain ja produzia e
/// jogava fora. Assinar e um ganho de ZERO CPU para o Brain e uma enorme
/// riqueza de sinal para a camada visual.
/// </summary>
public sealed class BrainMetricsCache : INotifyPropertyChanged
{
    public static readonly BrainMetricsCache Instance = new BrainMetricsCache();

    private volatile VoltrisBrainV2 _brain;
    private Timer _timer;
    private long _tickCount;
    private bool _disposed;
    private int _hubHooked;

    public event PropertyChangedEventHandler PropertyChanged;

    private bool _brainRunning;
    public bool BrainRunning { get => _brainRunning; set { _brainRunning = value; OnPropertyChanged(); } }

    private string _brainStatus = "Inicializando...";
    public string BrainStatus { get => _brainStatus; set { _brainStatus = value; OnPropertyChanged(); } }

    private string _activeAction = "Nenhum";
    public string ActiveAction { get => _activeAction; set { _activeAction = value; OnPropertyChanged(); } }

    private int _qTableSize;
    public int QTableSize { get => _qTableSize; set { _qTableSize = value; OnPropertyChanged(); } }

    private long _totalDecisions;
    public long TotalDecisions { get => _totalDecisions; set { _totalDecisions = value; OnPropertyChanged(); } }

    private double _sessionReward;
    public double SessionReward { get => _sessionReward; set { _sessionReward = value; OnPropertyChanged(); } }

    private string _brainStateKey = "---";
    public string BrainStateKey { get => _brainStateKey; set { _brainStateKey = value; OnPropertyChanged(); } }

    private string _brainCpuBucket = "-";
    public string BrainCpuBucket { get => _brainCpuBucket; set { _brainCpuBucket = value; OnPropertyChanged(); } }

    private string _brainTempBucket = "-";
    public string BrainTempBucket { get => _brainTempBucket; set { _brainTempBucket = value; OnPropertyChanged(); } }

    private string _brainRamBucket = "-";
    public string BrainRamBucket { get => _brainRamBucket; set { _brainRamBucket = value; OnPropertyChanged(); } }

    private double _epsilon;
    public double Epsilon { get => _epsilon; set { _epsilon = value; OnPropertyChanged(); } }

    private long _episodes;
    public long Episodes { get => _episodes; set { _episodes = value; OnPropertyChanged(); } }

    private string _integrationStatus = "0/5 sistemas";
    public string IntegrationStatus { get => _integrationStatus; set { _integrationStatus = value; OnPropertyChanged(); } }

    // ══════════════════════════════════════════════════════════════════════
    //  CAMADA DE ESTADO VISUAL - tudo aqui ja era propriedade do Brain.
    //  Todas usam mudanca-comparada: so disparam PropertyChanged quando o
    //  valor muda de verdade, para nao aumentar o trafego de PropertyChanged
    //  que ja existe (o cache dispara 1x/s e o ViewModel faz hop de Dispatcher).
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Estado operacional decided pelo <c>BrainStateOrchestrator</c>. E o sinal
    /// mais forte de "a IA esta orientada para alguma coisa": ele so muda
    /// depois de 30s de histerese e exige score de confianca 60.
    /// </summary>
    private BrainOperationalState _operationalState = BrainOperationalState.Balanced;
    public BrainOperationalState OperationalState
    {
        get => _operationalState;
        private set { if (_operationalState != value) { _operationalState = value; OnPropertyChanged(); } }
    }

    /// <summary>Contexto detectado pelo VoltrisLegs e confirmado pelo VoltrisBody.</summary>
    private OperationalContext _operationalContext = OperationalContext.Idle;
    public OperationalContext OperationalContext
    {
        get => _operationalContext;
        private set { if (_operationalContext != value) { _operationalContext = value; OnPropertyChanged(); } }
    }

    /// <summary>Natureza da ultima acao que o Brain escolheu.</summary>
    private BrainActionKind _lastActionKind = BrainActionKind.NoAction;
    public BrainActionKind LastActionKind
    {
        get => _lastActionKind;
        private set { if (_lastActionKind != value) { _lastActionKind = value; OnPropertyChanged(); } }
    }

    /// <summary>Perfil ativo escolhido pelo Brain (EPP / plano / prioridade).</summary>
    private string _activeProfileName = "Balanced";
    public string ActiveProfileName
    {
        get => _activeProfileName;
        private set { if (_activeProfileName != value) { _activeProfileName = value; OnPropertyChanged(); } }
    }

    /// <summary>Stutters registrados pelo proprio Brain (contador cumulativo).</summary>
    private int _stutterEvents;
    public int StutterEvents
    {
        get => _stutterEvents;
        private set { if (_stutterEvents != value) { _stutterEvents = value; OnPropertyChanged(); } }
    }

    /// <summary>Stutters que o modulo de prevencao resolveu.</summary>
    private int _stutterEventsResolved;
    public int StutterEventsResolved
    {
        get => _stutterEventsResolved;
        private set { if (_stutterEventsResolved != value) { _stutterEventsResolved = value; OnPropertyChanged(); } }
    }

    /// <summary>Total de acoes efetivamente executadas na sessao.</summary>
    private long _actionsExecuted;
    public long ActionsExecuted
    {
        get => _actionsExecuted;
        private set { if (_actionsExecuted != value) { _actionsExecuted = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// Acoes que o Brain tentou e que NAO foram executadas, em sequencia.
    /// Fonte: evento <c>ActionExecuted</c> com <c>Executed == false</c>.
    /// E o sinal real de "frustracao" - nao de tiques de relogio.
    /// </summary>
    private int _failureStreak;
    public int FailureStreak
    {
        get => _failureStreak;
        private set { if (_failureStreak != value) { _failureStreak = value; OnPropertyChanged(); } }
    }

    /// <summary>Sequencia de acoes executadas E verificadas com impacto positivo.</summary>
    private int _successStreak;
    public int SuccessStreak
    {
        get => _successStreak;
        private set { if (_successStreak != value) { _successStreak = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// Balde numerico de CPU do proprio Q-Learning (<see cref="BrainStateKey"/>).
    /// Numerico de proposito: a versao em string ("CPU:3") obrigava a UI a
    /// fazer parse para detectar salto de carga, e parsing em caminho de UI e
    /// desperdicio. O balde ja era calculado pelo Brain - so mudou o formato
    /// do que JA era exposto.
    /// </summary>
    private byte _cpuBucket;
    public byte CpuBucket
    {
        get => _cpuBucket;
        private set { if (_cpuBucket != value) { _cpuBucket = value; OnPropertyChanged(); } }
    }

    /// <summary>Balde termico do proprio Q-Learning (0 normal, 1 morno, 2 quente).</summary>
    private byte _thermalBucket;
    public byte ThermalBucket
    {
        get => _thermalBucket;
        private set { if (_thermalBucket != value) { _thermalBucket = value; OnPropertyChanged(); } }
    }

    /// <summary>Ultimo instante em que o Brain publicou <c>Success</c>.</summary>
    private long _lastSuccessTicks;
    public DateTime? LastSuccessUtc
    {
        get => _lastSuccessTicks == 0 ? null : new DateTime(Interlocked.Read(ref _lastSuccessTicks), DateTimeKind.Utc);
        private set { Interlocked.Exchange(ref _lastSuccessTicks, value?.Ticks ?? 0); OnPropertyChanged(); }
    }

    /// <summary>Ultimo instante em que o Brain publicou <c>Error</c>.</summary>
    private long _lastErrorTicks;
    public DateTime? LastErrorUtc
    {
        get => _lastErrorTicks == 0 ? null : new DateTime(Interlocked.Read(ref _lastErrorTicks), DateTimeKind.Utc);
        private set { Interlocked.Exchange(ref _lastErrorTicks, value?.Ticks ?? 0); OnPropertyChanged(); }
    }

    /// <summary>Mensagem curta do ultimo evento do Brain. So para log/diagnostico.</summary>
    private string _lastBrainEvent = string.Empty;
    public string LastBrainEvent
    {
        get => _lastBrainEvent;
        private set { if (_lastBrainEvent != value) { _lastBrainEvent = value; OnPropertyChanged(); } }
    }

    private BrainMetricsCache()
    {
        _brain = null;
    }

    public void Initialize(VoltrisBrainV2 brain)
    {
        App.LoggingService.LogEntry(nameof(Initialize), ("brain", brain != null));
        _brain = brain;
        BrainRunning = brain?.IsRunning ?? false;
        BrainStatus = brain != null ? "Conectado" : "Indisponível";

        HookBrainEvents(brain);
        HubHook();

        // Initialize e chamado por DashboardViewModel, WidgetViewModel e GamerDiagnosticsViewModel.
        // Descartar o timer anterior evita manter varios timers de 1s ativos em paralelo
        // (multiplicando coletas e PropertyChanged duplicados). O comportamento e o mesmo:
        // continua havendo exatamente um timer de 1s lendo o brain mais recente.
        var previous = Interlocked.Exchange(ref _timer, null);
        if (previous != null)
        {
            try { previous.Dispose(); } catch { }
        }

        var created = new Timer(TimerCallback, null, 0, 1000);
        if (Interlocked.CompareExchange(ref _timer, created, null) != null)
        {
            try { created.Dispose(); } catch { }
        }
    }

    /// <summary>
    /// Assina <c>ActionExecuted</c>. O evento ja existia e nao tinha NINGUEM
    /// escutando - o Brain levantava e o resultado era descartado. Assinar e
    /// leitura pura de um valor ja calculado.
    /// </summary>
    private void HookBrainEvents(VoltrisBrainV2? brain)
    {
        if (brain == null) return;
        try
        {
            brain.ActionExecuted -= OnActionExecuted;
            brain.ActionExecuted += OnActionExecuted;
        }
        catch (Exception ex)
        {
            App.LoggingService?.LogError($"[BrainMetricsCache] falha ao assinar ActionExecuted: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Assina o hub de observabilidade do Brain uma unica vez por processo.
    /// O hub ja pumpava os eventos numa fila e invocava o assinante (nenhum) -
    /// ou seja, o custo ja era pago. Aqui apenas damos destino a essa saida.
    /// </summary>
    private void HubHook()
    {
        if (Interlocked.Exchange(ref _hubHooked, 1) == 1) return;
        try
        {
            BrainObservabilityHub.EventPublished += OnBrainEventPublished;
        }
        catch (Exception ex)
        {
            App.LoggingService?.LogError($"[BrainMetricsCache] falha ao assinar BrainObservabilityHub: {ex.Message}", ex);
        }
    }

    /// <summary>Roda em thread de pool. Por isso so campos volateis/Interlocked.</summary>
    private void OnActionExecuted(object? sender, BrainActionResultV2 r)
    {
        if (r == null) return;

        if (r.Executed)
        {
            Interlocked.Increment(ref _successStreak);
            Interlocked.Exchange(ref _failureStreak, 0);
            if (r.ImpactVerified)
                Interlocked.Exchange(ref _lastSuccessTicks, DateTime.UtcNow.Ticks);
        }
        else if (!r.Skipped)
        {
            // Skipped NAO conta como falha: pular uma acao e uma decisao
            // deliberada do Brain, nao um erro. Contar seria fabricar
            // "frustracao" onde nao houve nada.
            Interlocked.Increment(ref _failureStreak);
            Interlocked.Exchange(ref _successStreak, 0);
            Interlocked.Exchange(ref _lastErrorTicks, DateTime.UtcNow.Ticks);
        }
        else
        {
            // Skipped: nao altera nenhum dos dois contadores.
            return;
        }

        SuccessStreak = Volatile.Read(ref _successStreak);
        FailureStreak = Volatile.Read(ref _failureStreak);
    }

    /// <summary>Roda na task de pump do hub (thread de pool).</summary>
    private void OnBrainEventPublished(object? sender, BrainObservabilityEvent evt)
    {
        if (evt == null) return;
        try
        {
            switch (evt.Severity)
            {
                case BrainEventSeverity.Success:
                    Interlocked.Exchange(ref _lastSuccessTicks, (evt.TimestampUtc == default ? DateTime.UtcNow : evt.TimestampUtc).Ticks);
                    break;
                case BrainEventSeverity.Error:
                    Interlocked.Exchange(ref _lastErrorTicks, (evt.TimestampUtc == default ? DateTime.UtcNow : evt.TimestampUtc).Ticks);
                    break;
            }
        }
        catch
        {
            // Observabilidade nunca pode derrubar o Brain.
        }
    }

    private readonly object _snapshotLock = new object();

    private void TimerCallback(object state)
    {
        if (_disposed) return;
        try
        {
            _tickCount++;
            App.LoggingService.LogTimer("MetricsCacheTimer", 1000);
            if (_brain == null) { BrainStatus = "Indisponível"; return; }

            App.LoggingService.LogCache("MetricsCache", "Refresh", $"tick={_tickCount}");
            BrainRunning = _brain.IsRunning;
            BrainStatus = _brain.IsRunning ? "Rodando" : "Parado";
            ActiveAction = _brain.LastAction?.ActionId ?? "Nenhum";
            SessionReward = _brain.SessionImprovementScore;
            QTableSize = _brain.Decision.KnownStates;
            TotalDecisions = _brain.Stats.TotalCycles;
            Episodes = _brain.Decision.Episodes;
            Epsilon = _brain.Decision.Epsilon;
            ActionsExecuted = _brain.Stats.TotalActionsExecuted;
            StutterEvents = _brain.Stats.StutterEventsRegistered;
            StutterEventsResolved = _brain.Stats.StutterEventsResolved;

            OperationalState = _brain.Orchestrator?.CurrentState ?? BrainOperationalState.Balanced;
            LastActionKind = _brain.LastAction?.Kind ?? BrainActionKind.NoAction;
            ActiveProfileName = _brain.ActiveProfile.ToString();

            // O Body ja e propriedade do Brain (circular wiring feito no App).
            // Ler CurrentContext daqui nao e polling novo: e o MESMO objeto que
            // o Dashboard ja consome em outras telas.
            OperationalContext = _brain.Body?.CurrentContext ?? OperationalContext.Idle;

            lock (_snapshotLock)
            {
                if (_brain.CurrentState.CanonicalKey != null)
                {
                    BrainStateKey = _brain.CurrentState.CanonicalKey;
                    CpuBucket = _brain.CurrentState.CpuBucket;
                    ThermalBucket = _brain.CurrentState.ThermalBucket;
                    BrainCpuBucket = $"CPU:{_brain.CurrentState.CpuBucket}";
                    BrainTempBucket = $"Temp:{_brain.CurrentState.ThermalBucket}";
                    BrainRamBucket = $"RAM:{_brain.CurrentState.RamBucket}";
                }
            }

            IntegrationStatus = "5/5 sistemas integrados";
        }
        catch (Exception ex)
        {
            App.LoggingService?.LogError($"[BrainMetricsCache] {ex.Message}", ex);
        }
    }

    private void OnPropertyChanged([CallerMemberName] string name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public void Dispose()
    {
        _disposed = true;
        _timer?.Dispose();
    }
}
