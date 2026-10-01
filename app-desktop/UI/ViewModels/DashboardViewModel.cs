using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using VoltrisOptimizer.Core;
using VoltrisOptimizer.Core.Brain.V2;
using VoltrisOptimizer.Core.Body;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Models;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Benchmark;
using VoltrisOptimizer.Services.Cloud;
using VoltrisOptimizer.Services.Gamer.Interfaces;
using VoltrisOptimizer.Services.License;
using VoltrisOptimizer.Services.License.Interfaces;
using VoltrisOptimizer.Services.License.Models;
using VoltrisOptimizer.Services.Thermal;
using VoltrisOptimizer.Services.Thermal.Models;
using VoltrisOptimizer.UI.Commands;
using VoltrisOptimizer.UI.Controls;
using VoltrisOptimizer.UI.Helpers;
using VoltrisOptimizer.UI.ViewModels.Base;
using VoltrisOptimizer.UI.Windows;
using VoltrisOptimizer.Utils;

namespace VoltrisOptimizer.UI.ViewModels;

public class DashboardViewModel : LicensedViewModel
{
	// [FIX:A-1] Contadores de auditoria de ciclo de vida.
	//
	// BUG ORIGINAL: cada criacao de DashboardViewModel se inscrevia em 8 fontes
	// estaticas/singleton, e nada chamava Dispose() durante a sessao (o
	// container e Transient e o MS DI so descarta na raiz, no encerramento do
	// processo). Resultado: multiplos VMs orfaos simultaneos, todos recebendo
	// MetricsUpdated a 4 Hz.
	//
	// Estes contadores existem para que a correcao seja VERIFICAVEL pelo log:
	// o numero de "vivas" deve voltar a 1 apos cada navegacao. Se voltar a
	// crescer, ha um caminho de descarte ausente.
	private static int _liveInstances;
	private static int _totalCreated;
	private static int _totalDisposed;

	private static readonly object _diagLock = new object();

	private static readonly ConcurrentQueue<string> _diagQueue = new ConcurrentQueue<string>();

	private static int _diagWriterRunning;

	private static bool _diagDirReady;

	private readonly ILoggingService? _logger;

	private readonly INavigationService? _navigationService;

	private readonly WmiCacheService? _wmiCache;

	private readonly SystemCleaner _cleaner;

	private readonly VoltrisPerformanceOptimizer _perf;

	private readonly BrainMetricsCache _brainMetrics;

	private CancellationTokenSource? _settingsMonitorCts;

	private CancellationTokenSource? _masterCts;

	/// <summary>
	/// Handler de <see cref="ProgressTrackingService.ProgressUpdated"/> que
	/// alimenta o botão circular com a mesma porcentagem do rodapé.
	/// Guardado em campo para poder desassinar no <c>finally</c> — um handler
	/// local não poderia ser removido, e vazar um por clique faria o botão
	/// reagir a operações de outras telas.
	/// </summary>
	private EventHandler<HierarchicalProgressEventArgs>? _masterProgressHandler;

	/// <summary>Tickers por tempo das etapas, criados e descartados por etapa.</summary>
	private StageProgressTicker? dnaTicker;
	private StageProgressTicker? analyzeTicker;
	private StageProgressTicker? cleanupTicker;

	private readonly VoltrisOptimizer.Services.LicenseManager _licenseManager;

	private readonly IPerformanceBenchmarkService? _benchmarkService;

	private readonly SemaphoreSlim _quickOptimizeLock = new SemaphoreSlim(1, 1);

	private readonly SemaphoreSlim _quickCleanupLock = new SemaphoreSlim(1, 1);

	private readonly SemaphoreSlim _masterLock = new SemaphoreSlim(1, 1);

	private SmartRepairViewModel? _smartRepairVM;

	private int _diskTickCounter = 0;

/// <summary>
/// Ultimo conjunto de componentes de saude que nao puderam ser avaliados,
/// ja registrado no log. Em maquinas sem sensor termico a lista nunca muda
/// (sempre "Termica"), e o WARNING repetia a cada recalculo (~30s) sem
/// trazer informacao nova. Agora so e emitido quando o conjunto MUDA.
/// </summary>
private string _ultimosComponentesAusentesLogados = "\u0000";

	private const int DiskUpdateEveryNTicks = 30;

	private string _cachedDiskInfo = LocalizationService.Instance["StatusCalculating"];

	private const int HistoryPoints = 30;

	private const int UpdateIntervalMs = 1000;

	private bool _shouldAnimationsRun = true;

	private bool _viewVisible = true;

	private bool _appForeground = true;

	private bool _masterClockRequested;

	private double _diskUsage;

	private string _diskInfo = LocalizationService.Instance["StatusCalculating"];

	private ObservableCollection<DiskItemViewModel> _disks = new ObservableCollection<DiskItemViewModel>();

	private string _healthStatusText = LocalizationService.Instance["StatusAnalyzing"];

	private static SolidColorBrush? _diskNormalBrush;

	private static SolidColorBrush? _diskWarningBrush;

	private static SolidColorBrush? _diskHighBrush;

	private string _hardwareProfileName = "Auto";

	private string _healthStatusColor = "#F59E0B";

	private string _healthDescription = LocalizationService.Instance["DashboardHealthDescriptionAnalyzing"];

	private readonly object _healthLock = new object();

	private DateTime _lastHealthAnalysisUtc = DateTime.MinValue;

	private int _lastHealthBand = -1;

	private string _lastOptimizationTime = LocalizationService.Instance["DashboardLastActionNone"];

	private string _licenseType = string.Empty;

	private string _supportLevel = string.Empty;

	private bool _isLicenseStateLoading = true;

	private DateTime _lastUIMetricsUpdate = DateTime.UtcNow;

	private const int UIUpdateThrottleMs = 500;

	private DateTime _lastDiskInfoUpdate = DateTime.UtcNow;

	private const int DiskInfoThrottleMs = 60000;

	private int _performanceScore;

	private string _scoreColor = "#6B7280";

	private string _scoreLabel = LocalizationService.Instance["StatusLoading"];

	private string _gamerServiceStatus = LocalizationService.Instance["StatusReady"];

	private bool _isGamerModeActive;

	private GamerViewModel? _gamerViewModel;

	private string _networkServiceStatus = LocalizationService.Instance["StatusOptimized"];

	private string _cleanupServiceStatus = LocalizationService.Instance["StatusWaiting"];

	private string _cleanupServiceColor = "#F59E0B";

	private string _brainStatus = LocalizationService.Instance.GetString("BrainStatusUnavailable");

	private string _brainAction = "---";

	private int _brainQTableSize;

	private long _brainCycles;

	private double _brainSessionReward;

	private string _brainStateKey = "---";

	private double _brainEpsilon;

	private string _brainIntegrationStatus = "0/5";

	private bool _brainRunning;

	private BrainMood _brainMood = BrainMood.Idle;

	private long _lastBrainDecisions;

	private DateTime _lastBrainDecisionAt = DateTime.UtcNow;

	private double _brainDecisionRate;

	private DateTime _lastBrainMoodCalc = DateTime.MinValue;

	private const double MoodFocusedEnter = 1.6;

	private const double MoodFocusedExit = 1.05;

	private const double MoodWorkingEnter = 0.35;

	private const double MoodWorkingExit = 0.18;

	private const double MoodThermalHoldSeconds = 2.5;

	private const double MoodConcernHoldSeconds = 3.0;

	private const double MoodConcernClearSeconds = 6.0;

	private const double MoodBoredSeconds = 50.0;

	private const double MoodHappyHoldSeconds = 5.0;

	private const double MoodSadHoldSeconds = 6.0;

	private const double MoodCpuConcernPercent = 93.0;

	// ── janelas dos estados acrescentados na reconstrucao visual ──
	// REGRA: TODO estado emocional novo precisa de uma JANELA de seguranca.
	// Sem ela, um unico tique de cache (800ms) com reward para baixo ja
	// viraria "triste" e o rosto passaria a oscilar. Estas janelas sao o que
	// separa uma expressao de uma luz de natal.
	private const double MoodErrorHoldSeconds = 9.0;       // erro do Brain
	private const double MoodSuccessHoldSeconds = 7.0;     // sucesso do Brain
	private const double MoodSurpriseHoldSeconds = 1.6;    // pico de carga
	private const double MoodMotivatedHoldSeconds = 14.0;  // aprendizado sustentado
	private const double MoodSatisfiedHoldSeconds = 6.0;   // run concluida com melhora
	private const double MoodAttentionHoldSeconds = 2.5;   // contexto novo
	private const double MoodWeepingHoldSeconds = 12.0;    // choro exige reward negativo

	private const int MoodFrustratedStreak = 2;  // acoes falhadas em sequencia
	private const int MoodWeepingStreak = 4;      // falhas + recompensa negativa
	private const int MoodMotivatedStreak = 2;    // acoes com impacto verificado

	/// <summary>Salto minimo de balde de CPU para contar como "pico".</summary>
	private const byte MoodSurpriseCpuJump = 2;

	private DateTime _lastBrainActivityAt = DateTime.UtcNow;

	private DateTime? _thermalAlertSince;

	private DateTime? _concernSince;

	private DateTime? _concernClearedAt;

	private DateTime _lastRewardUpAt = DateTime.MinValue;

	private DateTime _lastRewardDownAt = DateTime.MinValue;

	private double _rewardMark;

	private bool _rewardPrimed;

	// ── memoria dos sinais do Brain (derivados, nunca inventados) ──
	private byte _cpuBucketMark;
	private bool _cpuBucketPrimed;
	private DateTime _lastCpuJumpAt = DateTime.MinValue;
	private int _stutterMark;
	private bool _stutterPrimed;
	private DateTime _lastStutterJumpAt = DateTime.MinValue;
	private BrainOperationalState _operationalStateMark = BrainOperationalState.Balanced;
	private DateTime _lastOperationalChangeAt = DateTime.MinValue;
	private DateTime _lastMasterSuccessAt = DateTime.MinValue;
	private DateTime _lastMoodLogAt = DateTime.MinValue;
	private string _moodReason = string.Empty;

	/// <summary>Ultima razao textual da expressao corrente. So para log/diagnostico.</summary>
	public string BrainMoodReason
	{
		get { return _moodReason; }
	}

	private bool _isMasterRunning;

	private bool _isCancelling;

	private double _masterProgress;

	private string _masterStatusText = LocalizationService.Instance.GetString("MasterReadyState");

	private string _masterSubText = LocalizationService.Instance.GetString("MasterSubTitle");

	private int _masterPhase;

	private string _masterClockText = "00:00";

	private long _masterFreedBytes;
	/// <summary>
	/// Memória devolvida ao sistema nesta execução, em BYTES.
	/// <para>
	/// É uma grandeza diferente de <see cref="MasterFreedBytes"/> e por isso NUNCA
	/// é somada ao espaço em disco: bytes de RAM e bytes de disco não se somam.
	/// Memória "liberada" também não é espaço permanente — o Windows a realoca
	/// por demanda. Por isso aparece em linha própria, rotulada.
	/// </para>
	/// </summary>
	private long _masterRamReclaimedBytes;
	public long MasterRamReclaimedBytes
	{
		get { return _masterRamReclaimedBytes; }
		set
		{
			if (SetProperty(ref _masterRamReclaimedBytes, value, "MasterRamReclaimedBytes"))
			{
				OnPropertyChanged(nameof(MasterRamReclaimedDisplay));
			}
		}
	}
	/// <summary>Texto da linha de RAM. Vazio quando não houve ganho mensurável,
	/// para não poluir a comemoração com "+0 MB".</summary>
	public string MasterRamReclaimedDisplay
	{
		get { return _masterRamReclaimedDisplay; }
		set { SetProperty(ref _masterRamReclaimedDisplay, value, "MasterRamReclaimedDisplay"); }
	}
	private string _masterRamReclaimedDisplay = string.Empty;

	private string _masterRunSummary = string.Empty;

	private string _masterLastRunText = string.Empty;

	private readonly DispatcherTimer _masterClockTimer;

	private readonly Stopwatch _masterClockSw = new Stopwatch();

	private int _overallScore;

	private HealthIconState _healthIconState = HealthIconState.Warning;

	private string _scoreStatus = LocalizationService.Instance["StatusAnalyzing"];

	private double _cpuUsage;

	private double _ramUsage;

	private double _gpuUsage;

	private string _networkDownText = "0 B/s";

	private string _networkUpText = "0 B/s";

	private ObservableCollection<double>? _cpuHistory;

	private ObservableCollection<double>? _ramHistory;

	private string _cpuName = LocalizationService.Instance["StatusProcessor"];

	private string _ramInfo = LocalizationService.Instance["StatusCalculating"];

	private double _cpuTemperature;

	private double _gpuTemperature;

	private bool _isTemperatureEstimated;

	private string _thermalStatus = LocalizationService.Instance["StatusNormal"];

	private string _thermalStatusColor = "#10B981";

	private bool _hasThermalAlert;

	private bool _isThermalElevated;

	private string _linkedEmail = LocalizationService.Instance["DashboardLinkedEmailNone"];

	private string _activeProfileName = string.Empty;

	private bool _isInitializedAsync = false;

	private bool _viewReady = false;

	private bool _pendingGamerModeActive;

	// [FIX:M-1] Ultima leitura termica recebida do thread produtor.
	//
	// BUG ORIGINAL: estes dois campos eram bool/Referencia comuns, lidos e
	// escritos simultaneamente pelo thread produtor do ThermalMonitorService e
	// pelo thread da UI, sem nenhuma barreira de memoria. Pior: os early-returns
	// de OnThermalMetricsUpdated (Application.Current == null, Dispatcher == null)
	// SAIAM sem zerar _thermalUiUpdatePending, deixando-o travado em true para
	// sempre — dai "a temperatura congela e nunca mais atualiza".
	//
	// Correcao: referencia publicada com Volatile.Write/Read e a posse do update
	// de UI adquirida por Interlocked.CompareExchange, sempre liberada em finally.
	private ThermalMetrics? _pendingThermalMetrics;

	private int _thermalUiUpdatePending;

	private int _thermalStuckFlagRescues;

	private int _thermalDroppedCoalesced;

	private bool _isThermalSubscribed;

	private readonly object _thermalLock = new object();

	private string? _cachedDiskInfoString;

	private DateTime _nextDiskInfoRefresh = DateTime.MinValue;

	private static Dictionary<string, string>? _cachedDriveToTypeMap;

	private int _isDiskInfoUpdating = 0;

	public bool ShouldAnimationsRun
	{
		get
		{
			return _shouldAnimationsRun;
		}
		private set
		{
			SetProperty(ref _shouldAnimationsRun, value, "ShouldAnimationsRun");
		}
	}

	public double DiskUsage
	{
		get
		{
			return _diskUsage;
		}
		set
		{
			SetProperty(ref _diskUsage, value, "DiskUsage");
		}
	}

	public string DiskInfo
	{
		get
		{
			return _diskInfo;
		}
		set
		{
			SetProperty(ref _diskInfo, value, "DiskInfo");
		}
	}

	public ObservableCollection<DiskItemViewModel> Disks
	{
		get
		{
			return _disks;
		}
		set
		{
			SetProperty(ref _disks, value, "Disks");
		}
	}

	public string HealthStatusText
	{
		get
		{
			return _healthStatusText;
		}
		set
		{
			SetProperty(ref _healthStatusText, value, "HealthStatusText");
		}
	}

	public string HealthStatusColor
	{
		get
		{
			return _healthStatusColor;
		}
		set
		{
			SetProperty(ref _healthStatusColor, value, "HealthStatusColor");
		}
	}

	public string HealthDescription
	{
		get
		{
			return _healthDescription;
		}
		set
		{
			SetProperty(ref _healthDescription, value, "HealthDescription");
		}
	}

	public string LastOptimizationTime
	{
		get
		{
			return _lastOptimizationTime;
		}
		set
		{
			SetProperty(ref _lastOptimizationTime, value, "LastOptimizationTime");
		}
	}

	public string LicenseType
	{
		get
		{
			return _licenseType;
		}
		set
		{
			SetProperty(ref _licenseType, value, "LicenseType");
		}
	}

	public string SupportLevel
	{
		get
		{
			return _supportLevel;
		}
		set
		{
			SetProperty(ref _supportLevel, value, "SupportLevel");
		}
	}

	public bool IsLicenseStateLoading
	{
		get
		{
			return _isLicenseStateLoading;
		}
		set
		{
			SetProperty(ref _isLicenseStateLoading, value, "IsLicenseStateLoading");
		}
	}

	public int PerformanceScore
	{
		get
		{
			return _performanceScore;
		}
		set
		{
			if (SetProperty(ref _performanceScore, value, "PerformanceScore"))
			{
				UpdateScoreColor();
			}
		}
	}

	public string ScoreColor
	{
		get
		{
			return _scoreColor;
		}
		set
		{
			SetProperty(ref _scoreColor, value, "ScoreColor");
		}
	}

	public string ScoreLabel
	{
		get
		{
			return _scoreLabel;
		}
		set
		{
			SetProperty(ref _scoreLabel, value, "ScoreLabel");
		}
	}

	public string GamerServiceStatus
	{
		get
		{
			return _gamerServiceStatus;
		}
		set
		{
			SetProperty(ref _gamerServiceStatus, value, "GamerServiceStatus");
		}
	}

	public bool IsGamerModeActive
	{
		get
		{
			return _isGamerModeActive;
		}
		set
		{
			SetProperty(ref _isGamerModeActive, value, "IsGamerModeActive");
		}
	}

	public string NetworkServiceStatus
	{
		get
		{
			return _networkServiceStatus;
		}
		set
		{
			SetProperty(ref _networkServiceStatus, value, "NetworkServiceStatus");
		}
	}

	public string CleanupServiceStatus
	{
		get
		{
			return _cleanupServiceStatus;
		}
		set
		{
			SetProperty(ref _cleanupServiceStatus, value, "CleanupServiceStatus");
		}
	}

	public string CleanupServiceColor
	{
		get
		{
			return _cleanupServiceColor;
		}
		set
		{
			SetProperty(ref _cleanupServiceColor, value, "CleanupServiceColor");
		}
	}

	public string BrainStatus
	{
		get
		{
			return _brainStatus;
		}
		set
		{
			SetProperty(ref _brainStatus, value, "BrainStatus");
		}
	}

	public string BrainAction
	{
		get
		{
			return _brainAction;
		}
		set
		{
			SetProperty(ref _brainAction, value, "BrainAction");
		}
	}

	public int BrainQTableSize
	{
		get
		{
			return _brainQTableSize;
		}
		set
		{
			SetProperty(ref _brainQTableSize, value, "BrainQTableSize");
		}
	}

	public long BrainCycles
	{
		get
		{
			return _brainCycles;
		}
		set
		{
			SetProperty(ref _brainCycles, value, "BrainCycles");
		}
	}

	public double BrainSessionReward
	{
		get
		{
			return _brainSessionReward;
		}
		set
		{
			SetProperty(ref _brainSessionReward, value, "BrainSessionReward");
		}
	}

	public string BrainStateKey
	{
		get
		{
			return _brainStateKey;
		}
		set
		{
			SetProperty(ref _brainStateKey, value, "BrainStateKey");
		}
	}

	public double BrainEpsilon
	{
		get
		{
			return _brainEpsilon;
		}
		set
		{
			SetProperty(ref _brainEpsilon, value, "BrainEpsilon");
		}
	}

	public string BrainIntegrationStatus
	{
		get
		{
			return _brainIntegrationStatus;
		}
		set
		{
			SetProperty(ref _brainIntegrationStatus, value, "BrainIntegrationStatus");
		}
	}

	public bool BrainRunning
	{
		get
		{
			return _brainRunning;
		}
		set
		{
			if (SetProperty(ref _brainRunning, value, "BrainRunning"))
			{
				UpdateBrainMood();
			}
		}
	}

	public BrainMood BrainMood
	{
		get
		{
			return _brainMood;
		}
		set
		{
			if (SetProperty(ref _brainMood, value, "BrainMood"))
			{
				OnPropertyChanged("BrainMoodLabel");
				OnPropertyChanged("BrainTooltip");
				// O Brain só é dono do rosto quando não há operação por cima.
				// Durante a operação o humor efetivo é o override da fase, e essa
				// notificação faz a face sair de Furious de volta para o humor do
				// Brain no mesmo instante em que a fase deixa de ser a de limpeza.
				OnPropertyChanged("EffectiveFaceMood");
				OnPropertyChanged("MasterFaceEffortBoost");
			}
		}
	}

	/// <summary>
	/// [FIX:FACE-MOOD] Humor do rosto no botão circular.
	///
	/// Este é o ÚNICO humor que a <c>AiFaceView</c> consome. É o do Brain
	/// normalmente, e um override durante a fase de limpeza.
	///
	/// O preset <see cref="BrainMood.Furious"/> (sobrancelha brava, boca
	/// pressionada, bochecha, tremor, respiração rápida, glow vermelho) JÁ
	/// existia em <c>AiFaceExpression.For</c> — mas só aparecia quando o
	/// VoltrisBrainV2 decidia que havia sobrecarga térmica. Durante a limpeza o
	/// Brain não tinha motivo para mudar de humor, então o rosto ficava
	/// indiferente enquanto o app fazia o trabalho pesado.
	///
	/// Não se mexe em <see cref="BrainMood"/>: ele é a leitura do Brain, e
	/// sobrescrevê-lo faria a leitura deixar de refletir a verdade.
	/// </summary>
	public BrainMood EffectiveFaceMood
	{
		get
		{
			return _masterFaceOverride ?? _brainMood;
		}
	}

	private BrainMood? _masterFaceOverride;

	/// <summary>
	/// [FIX:FACE-EFFORT] Quanto o rosto deve se mexer além da expressão.
	///
	/// A limpeza pede 1.8 — o pedido explícito de "trabalhando a milão", com
	/// respiração rápida e tremor visível. Análise e otimização recebem 1.25:
	/// vivos o bastante para não parecerem parados, sem a agitação da limpeza,
	/// que é o que reserva o 1.8. Fora da operação, 1.0 (movimento normal do
	/// Brain).
	///
	/// O teto de 2.2 fica no AiFaceView: acima disso o tremor embaralha a
	/// leitura do rosto em vez de comunicar esforço.
	/// </summary>
	public double MasterFaceEffortBoost
	{
		get
		{
			return _masterFaceOverride switch
			{
				null => 1.0,
				BrainMood.Furious => 1.8,
				_ => 1.25
			};
		}
	}

	/// <summary>
	/// Define (ou limpa, com <c>null</c>) o humor forçado do rosto durante a
	/// operação do botão circular.
	/// </summary>
	private void SetMasterFaceOverride(BrainMood? mood)
	{
		RunOnUiThread(delegate
		{
			if (_masterFaceOverride == mood)
			{
				return;
			}
			_masterFaceOverride = mood;
			OnPropertyChanged("EffectiveFaceMood");
			// O boost de movimento anda junto com o humor: sem esta notificação o
			// rosto mudaria de expressão mas continuaria com a vivacidade antiga
			// (ou vice-versa) até o próximo repaint.
			OnPropertyChanged("MasterFaceEffortBoost");

			_logger?.LogInfo(
				$"[FACE] Override={(_masterFaceOverride?.ToString() ?? "(nenhum -> Brain)")} " +
				$"| Brain={_brainMood} | Efetivo={EffectiveFaceMood} " +
				$"| EffortBoost={MasterFaceEffortBoost:F2} | IsMasterRunning={IsMasterRunning}");
		});
	}

	/// <summary>
	/// Traduz a fase da operação no humor do rosto.
	///
	/// O mapeamento NÃO é inventado: cada humor foi escolhido pela própria
	/// descrição que já existe no enum <see cref="BrainMood"/>:
	///
	///   fase 1  Analisando  -> Thinking  "PENSANDO: analisando métricas para decidir"
	///   fase 2  Limpando    -> Furious   "RAIVA/ALERTA" — o que o usuário pediu
	///   fase 3  Otimizando  -> Working   "PROCESSANDO: executando otimizações"
	///   outras               -> Brain     o rosto volta a refletir o Brain
	///
	/// POR QUE A ANALISSE PRECISAVA DE HURMO PRÓPRIO
	/// ===============================================
	/// O override existia só na limpeza. Em "Analisando" ele era nulo, então o
	/// rosto caía no humor do Brain — que, com a IA ociosa, é <c>Idle</c>. Daí
	/// o sintoma: rosto parado, na cor azul do repouso, durante a fase que
	/// deveria ser a de "estou pensando".
	/// </summary>
	private void ApplyMasterFaceMoodForPhase(int phase)
	{
		switch (phase)
		{
			case 1:
				SetMasterFaceOverride(BrainMood.Thinking);
				break;
			case 2:
				SetMasterFaceOverride(BrainMood.Furious);
				break;
			case 3:
				SetMasterFaceOverride(BrainMood.Working);
				break;
			default:
				// Fase 0 (reset), 4 e 5 (finalização) e qualquer outra: o rosto
				// volta a ser o do Brain.
				SetMasterFaceOverride(null);
				break;
		}
	}

	public string BrainMoodLabel
	{
		get
		{
			LocalizationService instance = LocalizationService.Instance;
			BrainMood brainMood = _brainMood;
			if (1 == 0)
			{
			}
			string result = brainMood switch
			{
				BrainMood.Sleeping => instance.GetString("AiMoodSleeping"), 
				BrainMood.Idle => instance.GetString("AiMoodIdle"), 
				BrainMood.Thinking => instance.GetString("AiMoodThinking"), 
				BrainMood.Working => instance.GetString("AiMoodWorking"), 
				BrainMood.Focused => instance.GetString("AiMoodFocused"), 
				BrainMood.Furious => instance.GetString("AiMoodFurious"), 
				BrainMood.Happy => instance.GetString("AiMoodHappy"), 
				BrainMood.Sad => instance.GetString("AiMoodSad"), 
				BrainMood.Bored => instance.GetString("AiMoodBored"), 
				BrainMood.Concerned => instance.GetString("AiMoodConcerned"), 
				BrainMood.Attentive => instance.GetString("AiMoodAttentive"), 
				BrainMood.Satisfied => instance.GetString("AiMoodSatisfied"), 
				BrainMood.Motivated => instance.GetString("AiMoodMotivated"), 
				BrainMood.Frustrated => instance.GetString("AiMoodFrustrated"), 
				BrainMood.Surprised => instance.GetString("AiMoodSurprised"), 
				BrainMood.Weeping => instance.GetString("AiMoodWeeping"), 
				BrainMood.Succeeded => instance.GetString("AiMoodSucceeded"), 
				BrainMood.Errored => instance.GetString("AiMoodErrored"), 
				_ => instance.GetString("AiMoodIdle"), 
			};
			if (1 == 0)
			{
			}
			return result;
		}
	}

	public string BrainTooltip
	{
		get
		{
			LocalizationService instance = LocalizationService.Instance;
			string text = string.Format(CultureInfo.InvariantCulture, instance.GetString("AiTooltipStats"), BrainCycles, BrainEpsilon, BrainIntegrationStatus);
			return instance.GetString("AiTooltipTitle") + " · " + BrainMoodLabel + "\n" + text;
		}
	}

	public bool IsMasterRunning
	{
		get
		{
			return _isMasterRunning;
		}
		set
		{
			if (SetProperty(ref _isMasterRunning, value, "IsMasterRunning"))
			{
				OnPropertyChanged("MasterButtonLabel");
				// [FIX:RING] A borda_COLORIDA some enquanto a operação corre, para
				// não competir com a barra do arco. Derivada deste estado.
				OnPropertyChanged("MasterRingVisible");
			}
		}
	}

	public bool IsCancelling
	{
		get
		{
			return _isCancelling;
		}
		set
		{
			SetProperty(ref _isCancelling, value, "IsCancelling");
		}
	}

	public string MasterButtonLabel => IsMasterRunning ? LocalizationService.Instance["StatusOptimizing"] : LocalizationService.Instance["DashboardLaunchOptimization"];

	public double MasterProgress
	{
		get
		{
			return _masterProgress;
		}
		set
		{
			if (SetProperty(ref _masterProgress, value, "MasterProgress") && value >= 99.5)
			{
				MarkMasterSucceeded();
			}
		}
	}

	public string MasterStatusText
	{
		get
		{
			return _masterStatusText;
		}
		set
		{
			SetProperty(ref _masterStatusText, value, "MasterStatusText");
			// [FIX:TOOLTIP-VAZIO] O ToolTip depende deste texto, então o aviso de
			// mudança precisa sair junto. Sem isto, um texto vazio que chegue
			// por outro caminho continuaria abrindo o popup vazio.
			OnPropertyChanged("MasterStatusTooltip");
		}
	}

	/// <summary>
	/// [FIX:TOOLTIP-VAZIO] Conteúdo do ToolTip do botão circular.
	///
	/// Devolve <c>null</c> quando não há texto, e é isso que resolve o popup
	/// vazio: no WPF, <c>ToolTip="{Binding X}"</c> com X vazio ou nulo AINDA
	/// ABRE o popup — mostra uma caixa sem conteúdo. Só <c>null</c> suprime de
	/// fato.
	///
	/// Existe em dois níveis: <see cref="UpdateMasterProgress"/> já evita publicar
	/// status em branco, e isto garante que nenhuma outra origem consiga abrir
	/// um popup sem texto. A regra é única ("tooltip só abre com o que dizer") e
	/// está nos dois lados, onde cada um pega o seu.
	/// </summary>
	public string MasterStatusTooltip =>
		string.IsNullOrWhiteSpace(_masterStatusText) ? null : _masterStatusText;

	public string MasterSubText
	{
		get
		{
			return _masterSubText;
		}
		set
		{
			SetProperty(ref _masterSubText, value, "MasterSubText");
		}
	}

	// =====================================================================
	// COMEMORAÇÃO DO BOTÃO CIRCULAR
	//
	// Aposa ao FINALIZAR de TODOS os cliques de limpeza, não só do primeiro.
	// O número exibido é o espaço REAL liberado pela limpeza (CleanResult),
	// nunca um estimado. No primeiro clique entra também o score medido.
	// =====================================================================

	private bool _masterCelebrationVisible;
	private double _masterRealScore;
	private string _masterCelebrationTitle = string.Empty;
	private string _masterCelebrationDetail = string.Empty;
	private string _masterFreedDisplay = string.Empty;
	private long _masterLastFreedBytes;
	private bool _masterShowScoreLine;

	/// <summary>Exibe a comemoração + total liberado no centro do botão.</summary>
	public bool MasterCelebrationVisible
	{
		get { return _masterCelebrationVisible; }
		set
		{
			if (SetProperty(ref _masterCelebrationVisible, value, "MasterCelebrationVisible"))
			{
				// [FIX:RING] Borda VERDE durante a comemoração. Amarurada ao
				// estado da comemoração de propósito: ela dura 4800 ms
				// (4200 + 600 em ShowMasterCelebrationAsync), que é o "alguns
				// segundos" pedidos, e some junto com a festa — sem timer
				// paralelo para dessincronizar.
				OnPropertyChanged("MasterRingSuccess");
			}
		}
	}

	/// <summary>
	/// [FIX:RING] A borda colorida é SEMPRE visível fora da operação.
	///
	/// Antes ela só aparecia com o Modo Gamer ligado, e o pedido foi
	/// invertê-lo: o anel é parte da identidade do botão, não um indicador de
	/// modo. O único estado em que ela some é durante a otimização, onde o que
	/// deve ler é a barra do arco.
	/// </summary>
	public bool MasterRingVisible
	{
		get { return !IsMasterRunning; }
	}

	/// <summary>
	/// [FIX:RING] Borda em verde como confirmação de sucesso.
	///
	/// Só é verdadeira na comemoração — logo, um cancelamento devolve a borda
	/// colorida direto, sem passar por verde. Cancelar não é concluir.
	/// </summary>
	public bool MasterRingSuccess
	{
		get { return MasterCelebrationVisible; }
	}

	/// <summary>Score real medido (mostrado apenas na 1ª otimização).</summary>
	public double MasterRealScore
	{
		get { return _masterRealScore; }
		set { SetProperty(ref _masterRealScore, value, "MasterRealScore"); }
	}

	/// <summary>True na 1ª otimização, quando o score também é mostrado.</summary>
	public bool MasterShowScoreLine
	{
		get { return _masterShowScoreLine; }
		set { SetProperty(ref _masterShowScoreLine, value, "MasterShowScoreLine"); }
	}

	public string MasterCelebrationTitle
	{
		get { return _masterCelebrationTitle; }
		set { SetProperty(ref _masterCelebrationTitle, value, "MasterCelebrationTitle"); }
	}

	public string MasterCelebrationDetail
	{
		get { return _masterCelebrationDetail; }
		set { SetProperty(ref _masterCelebrationDetail, value, "MasterCelebrationDetail"); }
	}

	/// <summary>Total REAL liberado, já formatado (ex.: "12,4 GB").</summary>
	public string MasterFreedDisplay
	{
		get { return _masterFreedDisplay; }
		set { SetProperty(ref _masterFreedDisplay, value, "MasterFreedDisplay"); }
	}

	/// <summary>Bytes realmente liberados na última limpeza.</summary>
	public long MasterLastFreedBytes
	{
		get { return _masterLastFreedBytes; }
		set
		{
			if (SetProperty(ref _masterLastFreedBytes, value, "MasterLastFreedBytes"))
			{
				OnPropertyChanged(nameof(MasterFreedDisplay));
			}
		}
	}

	/// <summary>True enquanto a animação de festa está rodando (halo).</summary>
	public bool MasterConfettiActive
	{
		get { return _masterConfettiVisible; }
		set { SetProperty(ref _masterConfettiVisible, value, "MasterConfettiActive"); }
	}

	private bool _masterConfettiVisible;

	/// <summary>
	/// Comemora o fim da limpeza: mostra no centro do botão o total REAL
	/// liberado, com o rosto da IA sorrindo e a animação de festa.
	/// some sozinho depois de alguns segundos.
	/// </summary>
	private async Task ShowMasterCelebrationAsync(long freedBytes, bool isFirstRun, double realScore, int verifiedCount)
	{
		try
		{
			var loc = LocalizationService.Instance;

			RunOnUiThread(delegate
			{
				// Número real, formatado. Se não liberou nada, diz 0 — não inventa.
				MasterLastFreedBytes = freedBytes;
				MasterFreedDisplay = FormatBytesSafe(freedBytes);
				MasterRealScore = realScore;
				MasterShowScoreLine = isFirstRun;
				MasterCelebrationTitle = loc.GetString("MasterFreed");
				MasterCelebrationDetail = isFirstRun
					? string.Format(loc.GetString("ProfilerOptimizationsApplied"), verifiedCount.ToString())
					: string.Empty;
				MasterCelebrationVisible = true;
				MasterConfettiActive = true;
				MasterSubText = string.Empty;
				// Rosto da IA feliz e sorrindo
				_brainMood = BrainMood.Happy;
				OnPropertyChanged("BrainMood");
				OnPropertyChanged("BrainMoodLabel");
			});

			// Deixa a festa aparecer
			await Task.Delay(4200);

			RunOnUiThread(delegate
			{
				MasterConfettiActive = false;
			});
			await Task.Delay(600);

			RunOnUiThread(delegate
			{
				MasterCelebrationVisible = false;
				MasterShowScoreLine = false;
				MasterCelebrationDetail = string.Empty;
				// Atualiza o resumo textual com o total real desta rodada
				UpdateMasterRunSummary();
				UpdateBrainMood();
			});

			_logger?.LogSuccess($"[MasterAction] Comemoração exibida. Total real liberado: {freedBytes} bytes ({(freedBytes / 1048576.0):N2} MB){(isFirstRun ? $" | 1ª otimização, score {realScore}, verificados {verifiedCount}" : "")}");
		}
		catch (Exception ex)
		{
			_logger?.LogWarning("[MasterAction] Falha ao mostrar comemoração: " + ex.Message);
		}
	}

	private static string FormatBytesSafe(long bytes)
	{
		try
		{
			double gb = bytes / 1073741824.0;
			if (gb >= 1.0) return gb.ToString("N2") + " GB";
			double mb = bytes / 1048576.0;
			if (mb >= 1.0) return mb.ToString("N0") + " MB";
			return (bytes / 1024.0).ToString("N0") + " KB";
		}
		catch { return string.Empty; }
	}

	public int MasterPhase
	{
		get
		{
			return _masterPhase;
		}
		set
		{
			if (SetProperty(ref _masterPhase, value, "MasterPhase"))
			{
				for (int i = 1; i <= 5; i++)
				{
					OnPropertyChanged($"PhaseDot{i}");
				}
			}
		}
	}

	public bool PhaseDot1 => _masterPhase >= 1;

	public bool PhaseDot2 => _masterPhase >= 2;

	public bool PhaseDot3 => _masterPhase >= 3;

	public bool PhaseDot4 => _masterPhase >= 4;

	public bool PhaseDot5 => _masterPhase >= 5;

	public string MasterClockText
	{
		get
		{
			return _masterClockText;
		}
		set
		{
			SetProperty(ref _masterClockText, value, "MasterClockText");
		}
	}

	public long MasterFreedBytes
	{
		get
		{
			return _masterFreedBytes;
		}
		set
		{
			if (SetProperty(ref _masterFreedBytes, value, "MasterFreedBytes"))
			{
				UpdateMasterRunSummary();
			}
		}
	}

	public string MasterRunSummary
	{
		get
		{
			return _masterRunSummary;
		}
		set
		{
			SetProperty(ref _masterRunSummary, value, "MasterRunSummary");
		}
	}

	public string MasterLastRunText
	{
		get
		{
			return _masterLastRunText;
		}
		set
		{
			SetProperty(ref _masterLastRunText, value, "MasterLastRunText");
		}
	}

	public int OverallScore
	{
		get
		{
			return _overallScore;
		}
		set
		{
			if (SetProperty(ref _overallScore, value, "OverallScore"))
			{
				HealthIconState = ResolveHealthIconState(value);
			}
		}
	}

	public HealthIconState HealthIconState
	{
		get
		{
			return _healthIconState;
		}
		private set
		{
			SetProperty(ref _healthIconState, value, "HealthIconState");
		}
	}

	public string ScoreStatus
	{
		get
		{
			return _scoreStatus;
		}
		set
		{
			SetProperty(ref _scoreStatus, value, "ScoreStatus");
		}
	}

	public double CpuUsage
	{
		get
		{
			return _cpuUsage;
		}
		set
		{
			SetProperty(ref _cpuUsage, value, "CpuUsage");
		}
	}

	public double RamUsage
	{
		get
		{
			return _ramUsage;
		}
		set
		{
			SetProperty(ref _ramUsage, value, "RamUsage");
		}
	}

	public double GpuUsage
	{
		get
		{
			return _gpuUsage;
		}
		set
		{
			SetProperty(ref _gpuUsage, value, "GpuUsage");
		}
	}

	public string NetworkDownText
	{
		get
		{
			return _networkDownText;
		}
		set
		{
			SetProperty(ref _networkDownText, value, "NetworkDownText");
		}
	}

	public string NetworkUpText
	{
		get
		{
			return _networkUpText;
		}
		set
		{
			SetProperty(ref _networkUpText, value, "NetworkUpText");
		}
	}

	public ObservableCollection<double> CpuHistory
	{
		get
		{
			if (_cpuHistory == null)
			{
				_cpuHistory = new ObservableCollection<double>();
			}
			return _cpuHistory;
		}
	}

	public ObservableCollection<double> RamHistory
	{
		get
		{
			if (_ramHistory == null)
			{
				_ramHistory = new ObservableCollection<double>();
			}
			return _ramHistory;
		}
	}

	public string CpuName
	{
		get
		{
			return _cpuName;
		}
		set
		{
			SetProperty(ref _cpuName, value, "CpuName");
		}
	}

	public string RamInfo
	{
		get
		{
			return _ramInfo;
		}
		set
		{
			SetProperty(ref _ramInfo, value, "RamInfo");
		}
	}

	public double CpuTemperature
	{
		get
		{
			return _cpuTemperature;
		}
		set
		{
			SetProperty(ref _cpuTemperature, value, "CpuTemperature");
		}
	}

	public double GpuTemperature
	{
		get
		{
			return _gpuTemperature;
		}
		set
		{
			SetProperty(ref _gpuTemperature, value, "GpuTemperature");
		}
	}

	public bool IsTemperatureEstimated
	{
		get
		{
			return _isTemperatureEstimated;
		}
		set
		{
			SetProperty(ref _isTemperatureEstimated, value, "IsTemperatureEstimated");
		}
	}

	public string ThermalStatus
	{
		get
		{
			return _thermalStatus;
		}
		set
		{
			SetProperty(ref _thermalStatus, value, "ThermalStatus");
		}
	}

	public string ThermalStatusColor
	{
		get
		{
			return _thermalStatusColor;
		}
		set
		{
			SetProperty(ref _thermalStatusColor, value, "ThermalStatusColor");
		}
	}

	public bool HasThermalAlert
	{
		get
		{
			return _hasThermalAlert;
		}
		set
		{
			SetProperty(ref _hasThermalAlert, value, "HasThermalAlert");
		}
	}

	public bool IsThermalElevated
	{
		get
		{
			return _isThermalElevated;
		}
		set
		{
			SetProperty(ref _isThermalElevated, value, "IsThermalElevated");
		}
	}

	public string LinkedEmail
	{
		get
		{
			return _linkedEmail;
		}
		set
		{
			SetProperty(ref _linkedEmail, value, "LinkedEmail");
		}
	}

	public ICommand QuickOptimizeCommand { get; }

	public ICommand QuickCleanupCommand { get; }

	public ICommand SmartRepairCommand { get; }

	public ICommand OpenProfileCommand { get; }

	/// <summary>
	/// Chave do recurso no <c>LicenseService.ProOnlyFeatures</c>. Compartilhada
	/// entre o botão do Dashboard e o item do ModernTray: os dois bloqueiam
	/// pelo MESMO nome, entao um recurso novo ou removido de uma so vez quebra
	/// os dois pontos de entrada juntos, nunca um so.
	/// </summary>
	public const string IntelligentProfileFeature = "intelligent_profile";

	public string ActiveProfileName
	{
		get
		{
			return _activeProfileName;
		}
		private set
		{
			SetProperty(ref _activeProfileName, value, "ActiveProfileName");
		}
	}

	public bool IsSmartRepairRunning => _smartRepairVM != null && !_smartRepairVM.IsIdle;

	public ICommand NavigateToPerformanceCommand { get; }

	public ICommand NavigateToNetworkCommand { get; }

	public ICommand NavigateToGamerCommand { get; }

	public ICommand NavigateToSystemCommand { get; }

	public ICommand OpenLicenseActivationCommand { get; }

	public ICommand MasterActionCommand { get; }

	public bool IsInitialized => _isInitializedAsync;

	private static void DiagLog(string msg)
	{
		try
		{
			_diagQueue.Enqueue($"[{DateTime.Now:HH:mm:ss.fff}] {msg}\r\n");
			EnsureDiagWriter();
		}
		catch
		{
		}
	}

	private static void EnsureDiagWriter()
	{
		if (Interlocked.CompareExchange(ref _diagWriterRunning, 1, 0) == 0)
		{
			ThreadPool.QueueUserWorkItem(delegate
			{
				DrainDiagQueue();
			}, null);
		}
	}

	private static void DrainDiagQueue()
	{
		try
		{
			string result;
			while (_diagQueue.TryDequeue(out result))
			{
				try
				{
					string text = LogDirectoryResolver.Resolve();
					if (!_diagDirReady)
					{
						Directory.CreateDirectory(text);
						_diagDirReady = true;
					}
					string path = Path.Combine(text, $"StartupDiag_{DateTime.Now:yyyy-MM-dd}.log");
					lock (_diagLock)
					{
						File.AppendAllText(path, result);
					}
				}
				catch
				{
				}
			}
		}
		finally
		{
			Interlocked.Exchange(ref _diagWriterRunning, 0);
			if (!_diagQueue.IsEmpty)
			{
				EnsureDiagWriter();
			}
		}
	}

	public void SetViewVisible(bool visible)
	{
		if (_viewVisible != visible)
		{
			_viewVisible = visible;
			ApplyRuntimeState();
		}
	}

	private void ApplyRuntimeState()
	{
		bool flag = _appForeground && _viewVisible;
		if (ShouldAnimationsRun != flag)
		{
			ShouldAnimationsRun = flag;

			// [FIX:RGB-LOG] Este é o ÚNICO ponto do aplicativo que desliga as
			// animações decorativas, e o RGB do botão circular é uma delas.
			// Registrar o valor de cada entrada deixa registrado não só que
			// pausou, mas POR QUE — que é a informação que falta quando alguém
			// pergunta "por que a borda parou de piscar?" horas depois.
			_logger?.LogInfo(
				$"[GFX] Dashboard visible={_viewVisible} foreground={_appForeground} " +
				$"-> animações={(flag ? "ATIVAS" : "PAUSADAS")} " +
				$"| foreground={_appForeground} view={_viewVisible} " +
				$"| GamerMode={IsGamerModeActive} (RGB do anel " +
				$"{(flag && IsGamerModeActive ? "CONTINUA" : "PARADO")})");
		}
		UpdateMasterClockTimerState();
	}

	private void UpdateMasterClockTimerState()
	{
		DispatcherTimer masterClockTimer = _masterClockTimer;
		if (masterClockTimer == null)
		{
			return;
		}
		try
		{
			if (ShouldAnimationsRun)
			{
				if (_masterClockRequested && !masterClockTimer.IsEnabled)
				{
					masterClockTimer.Start();
					UpdateMasterRunSummary();
				}
			}
			else if (masterClockTimer.IsEnabled)
			{
				masterClockTimer.Stop();
			}
		}
		catch (Exception ex)
		{
			_logger?.LogTrace("[Dashboard] Falha ao alternar timer do relógio: " + ex.Message);
		}
	}

	private void UpdateScoreColor()
	{
		string key;
		if (_performanceScore >= 80)
		{
			ScoreColor = "#10B981";
			key = "HealthStatusOptimum";
		}
		else if (_performanceScore >= 60)
		{
			ScoreColor = "#F59E0B";
			key = "ScoreStatusGood";
		}
		else if (_performanceScore >= 40)
		{
			ScoreColor = "#F97316";
			key = "ScoreStatusRegular";
		}
		else
		{
			ScoreColor = "#EF4444";
			key = "ScoreStatusAttention";
		}
		ScoreLabel = LocalizationService.Instance[key];
	}

	private void OnAppSettingsChanged(object? sender, EventArgs e)
	{
	}

	/// <summary>
	/// O Dashboard acabou de concluir uma otimizacao. Este e um EVENTO REAL do
	/// botao circular (o proprio pipeline de otimizacao), nao um tique de
	/// relogio: e o unico sinal de "missao cumprida" que o Brain nao produz
	/// sozinho. Por isso e a entrada de Satisfied/Succeeded.
	/// </summary>
	private void MarkMasterSucceeded()
	{
		_lastMasterSuccessAt = DateTime.UtcNow;
		UpdateBrainMood(true);
	}

	private static bool IsQuietContext(OperationalContext ctx)
	{
		return ctx == OperationalContext.Idle || ctx == OperationalContext.Startup || ctx == OperationalContext.UserAway;
	}

	/// <param name="force">
	/// true = recalcula agora, ignorando o throttle de 800ms. Usado somente por
	/// Eventos discretos (run concluida, mudanca de estado do Brain), onde
	/// esperar 800ms para a expressao aparecer seria visivel.
	/// </param>
	private void UpdateBrainMood(bool force = false)
	{
		DateTime now = DateTime.UtcNow;
		if (!force && (now - _lastBrainMoodCalc).TotalMilliseconds < 800.0)
		{
			return;
		}
		_lastBrainMoodCalc = now;

		BrainMetricsCache brainMetrics = _brainMetrics;
		double rate = _brainDecisionRate;
		bool acting = rate > 0.05 || HasBrainAction();
		if (acting)
		{
			_lastBrainActivityAt = now;
		}

		if (HasThermalAlert)
		{
			if (!_thermalAlertSince.HasValue)
			{
				_thermalAlertSince = now;
			}
		}
		else
		{
			_thermalAlertSince = null;
		}

		bool concernSignal = IsThermalElevated || CpuUsage >= MoodCpuConcernPercent;
		if (concernSignal)
		{
			if (!_concernSince.HasValue)
			{
				_concernSince = now;
			}
			_concernClearedAt = null;
		}
		else if (_concernSince.HasValue)
		{
			if (!_concernClearedAt.HasValue)
			{
				_concernClearedAt = now;
			}
			if ((now - _concernClearedAt.Value).TotalSeconds >= MoodConcernClearSeconds)
			{
				_concernSince = null;
				_concernClearedAt = null;
			}
		}

		TrackBrainReward();
		TrackBrainSignals(now, brainMetrics);

		string reason;
		BrainMood mood = ResolveMood(now, brainMetrics, rate, acting, concernSignal, out reason);
		_moodReason = reason;

		if (mood != _brainMood)
		{
			// "Brain State -> Visual State". Unico log do caminho, com throttle
			// de 2s para transicoes rapidas nao virarem flood de log.
			bool rapid = (now - _lastMoodLogAt).TotalSeconds < 2.0;
			_lastMoodLogAt = now;
			string line = "[AIFace] Brain -> Visual: " + mood + " (" + reason + ")";
			if (rapid)
			{
				_logger?.LogDebug(line);
			}
			else
			{
				_logger?.LogInfo(line);
			}
		}

		BrainMood = mood;
	}

	/// <summary>
	/// Traducao estado real do Brain -> estado visual. Nenhuma decisao aqui: e
	/// uma tabela de prioridade sobre sinais que o Brain ja produz. O Brain
	/// continua sendo a unica inteligencia; isto so escolhe qual expressao
	/// mostra o que ele ja resolveu.
	/// </summary>
	private BrainMood ResolveMood(DateTime now, BrainMetricsCache m, double rate, bool acting, bool concernSignal, out string reason)
	{
		// ── 1. Brain desligado: nada acima disso importa ──
		if (!_brainRunning)
		{
			reason = "brain parado";
			return BrainMood.Sleeping;
		}

		// ── 2. falha real do Brain (eventos ActionExecuted e
		//        BrainObservabilityHub, que ele ja publicava sem ninguem
		//        escutar) ──
		if (m != null && m.LastErrorUtc.HasValue)
		{
			DateTime err = m.LastErrorUtc.Value;
			if ((now - err).TotalSeconds <= MoodErrorHoldSeconds)
			{
				reason = "erro do brain ha " + (now - err).TotalSeconds.ToString("0") + "s";
				return m.FailureStreak >= MoodFrustratedStreak ? BrainMood.Frustrated : BrainMood.Errored;
			}
		}

		// ── 3. sobrecarga termica: so depois de estabilidade, e so se a IA ainda
		//        esta agindo. Parado + quente e "repouso quente", nao raiva. ──
		if (_thermalAlertSince.HasValue)
		{
			DateTime hot = _thermalAlertSince.Value;
			if ((now - hot).TotalSeconds >= MoodThermalHoldSeconds && acting)
			{
				reason = "sobrecarga termica";
				return BrainMood.Furious;
			}
		}

		// ── 4. frustracao sustentada: varias acoes falharam E a recompensa caiu.
		//        So daqui sai a unica expressao com lagrima. ──
		if (m != null && m.FailureStreak >= MoodWeepingStreak && (now - _lastRewardDownAt).TotalSeconds <= MoodWeepingHoldSeconds)
		{
			reason = m.FailureStreak + " acoes falhadas + recompensa negativa";
			return BrainMood.Weeping;
		}

		if (m != null && m.FailureStreak >= MoodFrustratedStreak)
		{
			reason = m.FailureStreak + " acoes falhadas em sequencia";
			return BrainMood.Frustrated;
		}

		// ── 5. surpresa: pico de carga ou stutter detectado pelo proprio Brain.
		//        Janela curta de proposito - surpresa e breve. ──
		if ((now - _lastCpuJumpAt).TotalSeconds <= MoodSurpriseHoldSeconds || (now - _lastStutterJumpAt).TotalSeconds <= MoodSurpriseHoldSeconds)
		{
			reason = "pico de carga detectado";
			return BrainMood.Surprised;
		}

		// ── 6. sucesso confirmado pelo Brain ──
		if (m != null && m.LastSuccessUtc.HasValue)
		{
			DateTime ok = m.LastSuccessUtc.Value;
			if ((now - ok).TotalSeconds <= MoodSuccessHoldSeconds)
			{
				reason = "brain reportou sucesso";
				return BrainMood.Succeeded;
			}
		}

		// ── 7. a missao do botao circular terminou com melhora ──
		if ((now - _lastMasterSuccessAt).TotalSeconds <= MoodSatisfiedHoldSeconds && (now - _lastRewardDownAt).TotalSeconds > MoodSatisfiedHoldSeconds)
		{
			reason = "otimizacao concluida com melhora";
			return BrainMood.Satisfied;
		}

		// ── 8. intensidade: carga alta = concentracao; carga media =
		//        processando. Histerese assimetrica, senao o rosto fica preso. ──
		if (rate >= MoodFocusedEnter)
		{
			reason = "taxa de decisao " + rate.ToString("0.00") + "/s";
			return BrainMood.Focused;
		}
		if (_brainMood == BrainMood.Focused && rate >= MoodFocusedExit)
		{
			reason = "taxa de decisao " + rate.ToString("0.00") + "/s (histerese)";
			return BrainMood.Focused;
		}
		if (rate >= MoodWorkingEnter)
		{
			reason = "taxa de decisao " + rate.ToString("0.00") + "/s";
			return BrainMood.Working;
		}
		if (_brainMood == BrainMood.Working && rate >= MoodWorkingExit)
		{
			reason = "taxa de decisao " + rate.ToString("0.00") + "/s (histerese)";
			return BrainMood.Working;
		}

		// ── 9. MOTIVADO: aprendizado consistente. Vem de SuccessStreak (acoes
		//        com impacto verificado) + recompensa ainda subindo. ──
		if (m != null && m.SuccessStreak >= MoodMotivatedStreak && (now - _lastRewardUpAt).TotalSeconds <= MoodMotivatedHoldSeconds)
		{
			reason = "aprendizado sustentado (" + m.SuccessStreak + " acoes com impacto)";
			return BrainMood.Motivated;
		}

		// ── 10. problema detectado, sem severidade critica ──
		if (ConcernActive(now))
		{
			reason = concernSignal ? "anomalia termica / cpu alta" : "anomalia";
			return BrainMood.Concerned;
		}

		// ── 11. recompensa subiu ──
		if ((now - _lastRewardUpAt).TotalSeconds < MoodHappyHoldSeconds)
		{
			reason = "recompensa do Q-learning subindo";
			return BrainMood.Happy;
		}

		// ── 12. ATENTO: o orquestrador mudou de estado operacional, ou o
		//        contexto deixou de ser ocioso. O orquestrador so muda depois de
		//        30s de histerese e score de confianca 60, entao isto significa
		//        "a IA se reorientou", nao "ruido". ──
		if (m != null && IsAttentionWorthy(m, now))
		{
			reason = "contexto " + m.OperationalContext + " / estado " + m.OperationalState;
			return BrainMood.Attentive;
		}

		// ── 13. o Brain esta escolhendo uma acao mas ainda nao executou ──
		if (HasBrainAction())
		{
			reason = "analisando: " + BrainAction;
			return BrainMood.Thinking;
		}

		// ── 14. recompensa caiu ──
		if ((now - _lastRewardDownAt).TotalSeconds < MoodSadHoldSeconds)
		{
			reason = "recompensa do Q-learning caindo";
			return BrainMood.Sad;
		}

		// ── 15. ocioso ha muito tempo ──
		if ((now - _lastBrainActivityAt).TotalSeconds >= MoodBoredSeconds)
		{
			reason = "sem atividade ha " + (now - _lastBrainActivityAt).TotalSeconds.ToString("0") + "s";
			return BrainMood.Bored;
		}

		reason = "nenhum sinal relevante";
		return BrainMood.Idle;
	}

	private bool IsAttentionWorthy(BrainMetricsCache m, DateTime now)
	{
		if ((now - _lastOperationalChangeAt).TotalSeconds > MoodAttentionHoldSeconds * 4.0)
		{
			return false;
		}
		if (m.OperationalState != BrainOperationalState.Balanced)
		{
			return true;
		}
		return !IsQuietContext(m.OperationalContext);
	}

	/// <summary>
	/// Detecta variacao nos sinais que o Brain ja publica, marcando-os para
	/// atencao. Nao e deteccao nova: e diferenca entre duas leituras de numeros
	/// que o proprio Brain ja escreveu no cache.
	/// </summary>
	private void TrackBrainSignals(DateTime now, BrainMetricsCache m)
	{
		if (m == null)
		{
			return;
		}

		// salto de balde de CPU (o proprio Q-Learning quantiza a carga)
		if (!_cpuBucketPrimed)
		{
			_cpuBucketPrimed = true;
			_cpuBucketMark = m.CpuBucket;
		}
		else if (m.CpuBucket >= _cpuBucketMark + MoodSurpriseCpuJump)
		{
			_lastCpuJumpAt = now;
			_cpuBucketMark = m.CpuBucket;
		}
		else if (m.CpuBucket < _cpuBucketMark)
		{
			_cpuBucketMark = m.CpuBucket;
		}

		// stutter registrado pelo modulo de prevencao
		if (!_stutterPrimed)
		{
			_stutterPrimed = true;
			_stutterMark = m.StutterEvents;
		}
		else if (m.StutterEvents > _stutterMark)
		{
			_stutterMark = m.StutterEvents;
			_lastStutterJumpAt = now;
		}

		// troca de estado operacional (ja vem com histerese do orquestrador)
		if (m.OperationalState != _operationalStateMark)
		{
			_operationalStateMark = m.OperationalState;
			_lastOperationalChangeAt = now;
		}
	}

	private bool HasBrainAction()
	{
		string brainAction = BrainAction;
		if (string.IsNullOrEmpty(brainAction))
		{
			return false;
		}
		if (brainAction.Equals("Nenhum", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (brainAction.Equals("---", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		return true;
	}

	private bool ConcernActive(DateTime now)
	{
		DateTime? concernSince = _concernSince;
		int result;
		if (concernSince.HasValue)
		{
			DateTime valueOrDefault = concernSince.GetValueOrDefault();
			result = (((now - valueOrDefault).TotalSeconds >= 3.0) ? 1 : 0);
		}
		else
		{
			result = 0;
		}
		return (byte)result != 0;
	}

	private void TrackBrainReward()
	{
		double num;
		try
		{
			num = _brainMetrics?.SessionReward ?? 0.0;
		}
		catch
		{
			return;
		}
		if (!_rewardPrimed)
		{
			_rewardPrimed = true;
			_rewardMark = num;
			return;
		}
		double num2 = num - _rewardMark;
		if (!(Math.Abs(num2) < 1E-09))
		{
			_rewardMark = num;
			if (num2 > 0.0)
			{
				_lastRewardUpAt = DateTime.UtcNow;
			}
			else if (Math.Abs(num) > 1E-09)
			{
				_lastRewardDownAt = DateTime.UtcNow;
			}
		}
	}

	private static HealthIconState ResolveHealthIconState(int score)
	{
		return (score >= 70) ? HealthIconState.Healthy : ((score >= 55) ? HealthIconState.Warning : HealthIconState.Critical);
	}

	private void RefreshActiveProfileName()
	{
		try
		{
			ActiveProfileName = IntelligentProfileCatalog.GetLocalizedProfileName(SettingsService.Instance.Settings.IntelligentProfile);
		}
		catch
		{
			ActiveProfileName = string.Empty;
		}
	}

	private void OnProfileChanged(object? sender, IntelligentProfileType profile)
	{
		Application current = Application.Current;
		if (current != null)
		{
			Dispatcher dispatcher = ((DispatcherObject)current).Dispatcher;
			if (dispatcher != null)
			{
				dispatcher.BeginInvoke((Delegate)new Action(RefreshActiveProfileName), Array.Empty<object>());
			}
		}
		RefreshActiveProfileName();
	}

	public DashboardViewModel(ILicenseGuard licenseGuard, ILicenseDialogService dialogService, WmiCacheService wmiCache, SystemCleaner cleaner, VoltrisPerformanceOptimizer performanceOptimizer, INavigationService? navigationService = null)
		: base(licenseGuard, dialogService)
	{
		//IL_0676: Unknown result type (might be due to invalid IL or missing references)
		//IL_067b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0695: Expected O, but got Unknown
		StartupStepTracker.Instance?.Begin("DASHBOARDVM_CTOR");

// [UPDATE-FLUXO] Assina o estado de atualização já no construtor.
//
// A assinatura é feita aqui, e não no Loaded, porque a atualização pode
// começar e terminar antes de este ViewModel ser construído — o usuário clica
// em "verificar atualizações" nas Configurações, a tela troca para o
// Dashboard, e o ViewModel novo precisa JÁ saber o estado atual em vez de
// esperar o próximo evento.
EnsureUpdateFlowSubscribed();

		int managedThreadId = Thread.CurrentThread.ManagedThreadId;
		DiagLog($"[DASHBOARD_VM][TID:{managedThreadId}] ==================== Construtor INÍCIO ====================");
		DiagLog($"[DASHBOARD_VM][TID:{managedThreadId}] Timestamp: {DateTime.Now:HH:mm:ss.ffffff}");
		DiagLog($"[DASHBOARD_VM][TID:{managedThreadId}] HashCode desta instância: {GetHashCode()}");
		DiagLog($"[DASHBOARD_VM][TID:{managedThreadId}] \ud83d\udd0d VALORES INICIAIS DAS PROPRIEDADES:");
		DiagLog($"[DASHBOARD_VM][TID:{managedThreadId}]    - LicenseType: '{_licenseType}'");
		DiagLog($"[DASHBOARD_VM][TID:{managedThreadId}]    - IsLicenseStateLoading: {_isLicenseStateLoading}");
		DiagLog($"[DASHBOARD_VM][TID:{managedThreadId}]    - HealthStatusText: '{_healthStatusText}'");
		DiagLog($"[DASHBOARD_VM][TID:{managedThreadId}]    - ScoreLabel: '{_scoreLabel}'");
		try
		{
			VoltrisDiagnosticSystem.Instance.Timeline("DASHBOARD", "DashboardViewModel: Construtor INÍCIO");
		}
		catch
		{
		}

		// [FIX:A-1] Registro da nova instancia viva, para auditoria pelo log.
		int liveNow = Interlocked.Increment(ref _liveInstances);
		int createdTotal = Interlocked.Increment(ref _totalCreated);

		_logger = App.LoggingService;
		_logger?.LogInfo(
			$"[FIX:A-1] DashboardViewModel criado | Hash={GetHashCode()} | vivas={liveNow} | criadasTotal={createdTotal} | " +
			$"fontesEstaticasAssinadas=8(SettingsService x2, BrainMetricsCache, SystemMetricsCache, LicenseManager, " +
			$"CloudAccountService, LocalizationService, ApplicationStateTracker, GamerViewModel)");
		_navigationService = navigationService ?? App.Services?.GetService<INavigationService>();
		_wmiCache = wmiCache;
		_cleaner = cleaner;
		_perf = performanceOptimizer;
		_licenseManager = VoltrisOptimizer.Services.LicenseManager.Instance;
		InitializeLicenseStateSync();
		_masterClockTimer = new DispatcherTimer((DispatcherPriority)4)
		{
			Interval = TimeSpan.FromSeconds(1.0)
		};
		_masterClockTimer.Tick += OnMasterClockTick;
		RefreshMasterLastRunText();
		_benchmarkService = App.Services?.GetService<IPerformanceBenchmarkService>();
		_smartRepairVM = App.Services?.GetService<SmartRepairViewModel>();
		_brainMetrics = BrainMetricsCache.Instance;
		try
		{
			SettingsService.Instance.SettingsChanged += OnAppSettingsChanged;
		}
		catch
		{
		}
		VoltrisBrainV2 voltrisBrainV = App.Services?.GetService<VoltrisBrainV2>();
		if (voltrisBrainV != null)
		{
			_brainMetrics.Initialize(voltrisBrainV);
			_brainMetrics.PropertyChanged -= OnBrainMetricsPropertyChanged;
			_brainMetrics.PropertyChanged += OnBrainMetricsPropertyChanged;
			DiagLog("[DASHBOARD_VM] BrainMetricsCache inicializado - Brain V2 conectado");
		}
		if (_smartRepairVM != null)
		{
			_smartRepairVM.PropertyChanged -= OnSmartRepairPropertyChanged;
			_smartRepairVM.PropertyChanged += OnSmartRepairPropertyChanged;
		}
		DiagLog($"[DASHBOARD_VM][TID:{managedThreadId}] Dependências inicializadas (Legacy/Mixed mode)");
		if (_benchmarkService != null)
		{
			Task.Run(delegate
			{
				int score = _benchmarkService.TotalScore;
				Application current2 = Application.Current;
				if (current2 != null)
				{
					Dispatcher dispatcher = ((DispatcherObject)current2).Dispatcher;
					if (dispatcher != null)
					{
						dispatcher.InvokeAsync((Action)delegate
						{
							PerformanceScore = score;
							DiagLog($"DashboardViewModel ctor: Score inicial (async) = {score}");
						});
					}
				}
			});
		}
		for (int i = 0; i < 30; i++)
		{
			CpuHistory.Add(0.0);
			RamHistory.Add(0.0);
		}
		QuickOptimizeCommand = new AsyncRelayCommand((Func<object?, Task>)async delegate
		{
			await QuickOptimizeAsync();
		}, (Predicate<object?>?)null);
		QuickCleanupCommand = new AsyncRelayCommand((Func<object?, Task>)async delegate
		{
			await QuickCleanupAsync();
		}, (Predicate<object?>?)null);
		// PERFIL INTELIGENTE = recurso PRO. Mesmo embrulho do Smart Repair
		// (LicensedCommand): o botao NUNCA fica desabilitado - continua
		// clicavel e abre ajanela de licenca. Desabilitar daria a impressao
		// de botao quebrado e esconderia o motivo. O badge PRO no XAML
		// visivel apenas sem licenca e quem comunica o bloqueio.
		OpenProfileCommand = new LicensedCommand(new RelayCommand((Action<object?>)delegate
		{
			OpenProfileSelection();
		}, (Predicate<object?>?)null), App.Services?.GetService<ILicenseGuard>() ?? throw new InvalidOperationException("ILicenseGuard não registrado"), IntelligentProfileFeature, App.Services?.GetService<ILicenseDialogService>() ?? throw new InvalidOperationException("ILicenseDialogService não registrado"));
		RefreshActiveProfileName();
		try
		{
			SettingsService.Instance.ProfileChanged += OnProfileChanged;
		}
		catch
		{
		}
		MasterActionCommand = new RelayCommand(delegate(object? _)
		{
			if (IsMasterRunning)
			{
				CancelMasterExecution();
			}
			else
			{
				_ = MasterActionAsync();
			}
		});
		SmartRepairCommand = new LicensedCommand(new RelayCommand(() => NavigateToAsync(AppPage.SmartRepair)), App.Services?.GetService<ILicenseGuard>() ?? throw new InvalidOperationException("ILicenseGuard não registrado"), "smart_repair", App.Services?.GetService<ILicenseDialogService>() ?? throw new InvalidOperationException("ILicenseDialogService não registrado"));
		OpenLicenseActivationCommand = new RelayCommand((Action)OpenLicenseActivation, (Func<bool>?)null);
		NavigateToPerformanceCommand = new RelayCommand(() => NavigateToAsync(AppPage.Performance));
		NavigateToNetworkCommand = new RelayCommand(() => NavigateToAsync(AppPage.Network));
		NavigateToGamerCommand = new RelayCommand(() => NavigateToAsync(AppPage.Gamer));
		NavigateToSystemCommand = new RelayCommand((Action)delegate
		{
			try
			{
				AppSettings settings = SettingsService.Instance.Settings;
				if (settings.IsDeviceLinked && !string.IsNullOrEmpty(settings.LinkedUserEmail))
				{
					// Mesmo destino do ícone do header: dashboard no idioma do
					// app, já na aba "Meu Computador" (tab=pc).
					var dashboardUrl = VoltrisOptimizer.Services.SiteConfig.AccountDashboardUrl;
					_logger?.LogInfo($"Conta vinculada - abrindo dashboard web ({VoltrisOptimizer.Services.LocalizationService.Instance.CurrentLanguage}): {dashboardUrl}");
					Process.Start(new ProcessStartInfo
					{
						FileName = dashboardUrl,
						UseShellExecute = true
					});
				}
				else
				{
					_logger?.LogInfo("Conta não vinculada - abrindo Welcome Window");
					Application current = Application.Current;
					if (current != null)
					{
						((DispatcherObject)current).Dispatcher.BeginInvoke((Delegate)(Action)delegate
						{
							WelcomeLinkWindow welcomeLinkWindow = new WelcomeLinkWindow();
							Window mainWindow = Application.Current.MainWindow;
							if (mainWindow != null)
							{
								welcomeLinkWindow.Owner = mainWindow;
								welcomeLinkWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
							}
							welcomeLinkWindow.ShowDialog();
						}, Array.Empty<object>());
					}
				}
			}
			catch (Exception ex)
			{
				_logger?.LogError("Falha ao processar ação de gerenciar conta: " + ex.Message);
			}
		}, (Func<bool>?)null);
		SystemMetricsCache.Instance.MetricsUpdated += OnGlobalMetricsUpdated;
		_settingsMonitorCts = new CancellationTokenSource();
		StartSettingsMonitorLoopAsync(_settingsMonitorCts.Token);
		UpdateLinkedEmailFromSettings();
		StartupStepTracker.Instance?.End("DASHBOARDVM_CTOR");
		_licenseManager.LicenseStatusChanged += OnLicenseStatusChanged;
		CloudAccountService.Instance.AccountStateChanged += OnCloudAccountStateChanged;
		SubscribeThermalEvents();
		LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
		try
		{
			_gamerViewModel = App.Services?.GetService<GamerViewModel>();
			if (_gamerViewModel != null)
			{
				_gamerViewModel.PropertyChanged += OnGamerViewModelPropertyChanged;
				SyncGamerModeFromViewModel();
			}
		}
		catch
		{
		}
		ApplicationStateTracker.StateChanged += OnApplicationLifecycleStateChanged;
		UpdateAnimationsBasedOnLifecycleState(ApplicationStateTracker.CurrentLifecycleState);
		DiagLog("DashboardViewModel ctor: FIM");
		try
		{
			VoltrisDiagnosticSystem.Instance.Timeline("DASHBOARD", "DashboardViewModel: Construtor FIM");
		}
		catch
		{
		}
	}

	private void OpenProfileSelection()
	{
		try
		{
			Application current = Application.Current;
			if (current == null)
			{
				return;
			}
			((DispatcherObject)current).Dispatcher.BeginInvoke((Delegate)(Action)delegate
			{
				ProfileSelectionWindow profileSelectionWindow = new ProfileSelectionWindow();
				Window mainWindow = Application.Current.MainWindow;
				if (mainWindow != null)
				{
					profileSelectionWindow.Owner = mainWindow;
					profileSelectionWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
				}
				profileSelectionWindow.ShowDialog();
			}, Array.Empty<object>());
		}
		catch (Exception ex)
		{
			_logger?.LogError("Falha ao abrir o seletor de Perfil Inteligente: " + ex.Message);
		}
	}

	internal void MarkViewReady()
	{
		_viewReady = true;
		SyncGamerModeFromViewModel();
	}

	private void OnMasterClockTick(object? sender, EventArgs e)
	{
		MasterClockText = $"{_masterClockSw.Elapsed:mm\\:ss}";
		UpdateMasterRunSummary();
	}

	private void OnBrainMetricsPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		PropertyChangedEventArgs e2 = e;
		Application current = Application.Current;
		if (current == null)
		{
			return;
		}
		Dispatcher dispatcher = ((DispatcherObject)current).Dispatcher;
		if (dispatcher == null)
		{
			return;
		}
		dispatcher.InvokeAsync((Action)delegate
		{
			switch (e2.PropertyName)
			{
			case "BrainStatus":
				BrainStatus = _brainMetrics.BrainStatus;
				break;
			case "ActiveAction":
				BrainAction = _brainMetrics.ActiveAction;
				UpdateBrainMood();
				break;
			case "QTableSize":
				BrainQTableSize = _brainMetrics.QTableSize;
				break;
			case "TotalDecisions":
			{
				long totalDecisions = _brainMetrics.TotalDecisions;
				DateTime utcNow = DateTime.UtcNow;
				double totalSeconds = (utcNow - _lastBrainDecisionAt).TotalSeconds;
				if (totalSeconds >= 0.25)
				{
					_brainDecisionRate = Math.Max(0.0, (double)(totalDecisions - _lastBrainDecisions) / totalSeconds);
					_lastBrainDecisions = totalDecisions;
					_lastBrainDecisionAt = utcNow;
				}
				BrainCycles = totalDecisions;
				UpdateBrainMood();
				break;
			}
			case "BrainRunning":
				BrainRunning = _brainMetrics.BrainRunning;
				break;
			case "SessionReward":
				BrainSessionReward = _brainMetrics.SessionReward;
				break;
			case "BrainStateKey":
				BrainStateKey = _brainMetrics.BrainStateKey;
				break;
			case "Epsilon":
				BrainEpsilon = _brainMetrics.Epsilon;
				break;
			case "IntegrationStatus":
				BrainIntegrationStatus = _brainMetrics.IntegrationStatus;
				break;
			// Sinais novos que a camada visual consome. Nenhum deles vira uma
			// property do ViewModel: sao lidos direto do cache dentro de
			// UpdateBrainMood, o que evita inflar a superficie publica do VM
			// com 8 propiedades que so a UI interna leria.
			//
			// force: true porque estes sao EVENTOS discretos (erro, sucesso,
			// troca de contexto, falha de acao). Esperar o throttle de 800ms
			// para reagir a um erro do Brain seria visivel a olho nu.
			case "FailureStreak":
			case "SuccessStreak":
			case "LastSuccessUtc":
			case "LastErrorUtc":
			case "OperationalState":
			case "OperationalContext":
			case "StutterEvents":
			case "CpuBucket":
			case "LastActionKind":
			case "ActionsExecuted":
				UpdateBrainMood(true);
				break;
			}
		});
	}

	private void OnSmartRepairPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (!(e.PropertyName == "IsIdle") && !(e.PropertyName == "IsScanning") && !(e.PropertyName == "IsExecuting"))
		{
			return;
		}
		Application current = Application.Current;
		if (current == null)
		{
			return;
		}
		Dispatcher dispatcher = ((DispatcherObject)current).Dispatcher;
		if (dispatcher != null)
		{
			dispatcher.InvokeAsync((Action)delegate
			{
				OnPropertyChanged("IsSmartRepairRunning");
			});
		}
	}

	private static void RunOnUiThread(Action action)
	{
		if (action == null)
		{
			return;
		}
		Application current = Application.Current;
		Dispatcher val = ((current != null) ? ((DispatcherObject)current).Dispatcher : null);
		if (val != null)
		{
			if (val.CheckAccess())
			{
				action();
			}
			else
			{
				val.BeginInvoke((Delegate)action, Array.Empty<object>());
			}
		}
	}

	private void SetMasterPhaseSafe(int phase)
	{
		RunOnUiThread(delegate
		{
			MasterPhase = phase;
			// [FIX:FACE-MOOD] O rosto segue a fase. Só a limpeza força Furious.
			ApplyMasterFaceMoodForPhase(phase);
		});
	}

	private void SetMasterFreedBytesSafe(long bytes)
	{
		RunOnUiThread(delegate
		{
			MasterFreedBytes = bytes;
		});
	}

	private void SyncGamerModeFromViewModel()
	{
		bool flag = _gamerViewModel?.IsGamerModeActive ?? false;
		_pendingGamerModeActive = flag;
		if (_viewReady && IsGamerModeActive != flag)
		{
			IsGamerModeActive = flag;
		}
	}

	public async Task InitializeAsync()
	{
		Stopwatch initSw = Stopwatch.StartNew();
		if (_isInitializedAsync)
		{
			return;
		}
		_isInitializedAsync = true;
		int tid = Thread.CurrentThread.ManagedThreadId;
		DiagLog($"[DASHBOARD_VM][TID:{tid}] InitializeAsync INÍCIO");
		try
		{
			if (!_viewReady)
			{
				bool viewAccessible = false;
				Application current = Application.Current;
				int num;
				if (current == null)
				{
					num = 0;
				}
				else
				{
					Dispatcher dispatcher = ((DispatcherObject)current).Dispatcher;
					num = (((dispatcher != null) ? new bool?(dispatcher.CheckAccess()) : null).GetValueOrDefault() ? 1 : 0);
				}
				if (num != 0)
				{
					viewAccessible = (Application.Current.MainWindow?.IsLoaded ?? false) && (Application.Current.MainWindow?.IsVisible ?? false);
				}
				else
				{
					Application current2 = Application.Current;
					if (((current2 != null) ? ((DispatcherObject)current2).Dispatcher : null) != null)
					{
						await ((DispatcherObject)Application.Current).Dispatcher.InvokeAsync((Action)delegate
						{
							Window mainWindow = Application.Current.MainWindow;
							viewAccessible = mainWindow != null && mainWindow.IsLoaded && (Application.Current.MainWindow?.IsVisible ?? false);
						});
					}
				}
				if (!viewAccessible)
				{
					DiagLog("[DASHBOARD_VM] InitializeAsync ignorado: view MainWindow não está pronta");
					_isInitializedAsync = false;
					return;
				}
			}
		}
		catch
		{
		}
		try
		{
			Task.Run(() => InitializeStaticInfo());
			Task.Run(() => InitializeSystemStatus());
			UpdateMetricsFastAsync();
		}
		catch (Exception ex2)
		{
			Exception ex = ex2;
			_logger?.LogError("Erro no InitializeAsync do Dashboard: " + ex.Message);
		}
		finally
		{
			initSw.Stop();
			DiagLog($"[DASHBOARD_VM] InitializeAsync concluído em {initSw.ElapsedMilliseconds}ms");
			if (initSw.ElapsedMilliseconds > 500)
			{
				_logger?.LogWarning($"[DASHBOARD] InitializeAsync demorou {initSw.ElapsedMilliseconds}ms (>500ms)");
				VoltrisDiagnosticSystem.Instance.Timeline("DASHBOARD-WARN", $"InitializeAsync lento: {initSw.ElapsedMilliseconds}ms");
			}
		}
	}

	private void OnGamerViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName != "IsGamerModeActive")
		{
			return;
		}
		Application current = Application.Current;
		if (current == null)
		{
			return;
		}
		Dispatcher dispatcher = ((DispatcherObject)current).Dispatcher;
		if (dispatcher != null)
		{
			dispatcher.BeginInvoke((Delegate)(Action)delegate
			{
				SyncGamerModeFromViewModel();
				RefreshMasterButtonTexts();
			}, Array.Empty<object>());
		}
	}

	private void OnLanguageChanged(object? sender, EventArgs e)
	{
		Application current = Application.Current;
		if (current == null)
		{
			return;
		}
		Dispatcher dispatcher = ((DispatcherObject)current).Dispatcher;
		if (dispatcher == null)
		{
			return;
		}
		dispatcher.BeginInvoke((Delegate)(Action)delegate
		{
			SyncGamerModeFromViewModel();
			RefreshMasterButtonTexts();
			RefreshActiveProfileName();
			OnPropertyChanged("BrainMoodLabel");
			OnPropertyChanged("BrainTooltip");
			try
			{
				UpdateHealthStatus(force: true, "Idioma");
			}
			catch (Exception exception)
			{
				_logger?.LogError("Erro ao atualizar health status após troca de idioma", exception);
			}
			try
			{
				UpdateLinkedEmailFromSettings();
			}
			catch (Exception exception2)
			{
				_logger?.LogError("Erro ao atualizar e-mail após troca de idioma", exception2);
			}
			UpdateLicenseStatus();
			UpdateLastOptimizationInfo();
			UpdateScoreColor();
			if (string.IsNullOrEmpty(CpuName) || CpuName.Contains("..."))
			{
				CpuName = LocalizationService.Instance["StatusProcessor"];
			}
			UpdateMetricsFastAsync();
		}, Array.Empty<object>());
	}

	private void RefreshMasterButtonTexts()
	{
		if (!IsMasterRunning)
		{
			if (IsGamerModeActive)
			{
				MasterStatusText = LocalizationService.Instance.GetString("GamerModeActiveTooltip");
				MasterSubText = LocalizationService.Instance.GetString("GamerModeActiveSub");
			}
			else
			{
				MasterStatusText = LocalizationService.Instance.GetString("MasterReadyState");
				MasterSubText = LocalizationService.Instance.GetString("MasterSubTitle");
			}
		}
		RefreshMasterLastRunText();
		UpdateMasterRunSummary();
		OnPropertyChanged("MasterButtonLabel");
	}

	private async Task StartSettingsMonitorLoopAsync(CancellationToken token)
	{
		try
		{
			using PeriodicTimer timer = new PeriodicTimer(TimeSpan.FromSeconds(30.0));
			while (await timer.WaitForNextTickAsync(token) && !token.IsCancellationRequested)
			{
				Application current = Application.Current;
				if (current == null)
				{
					continue;
				}
				Dispatcher dispatcher = ((DispatcherObject)current).Dispatcher;
				if (dispatcher != null)
				{
					dispatcher.BeginInvoke((Delegate)(Action)delegate
					{
						UpdateLinkedEmailFromSettings();
					}, (DispatcherPriority)4, Array.Empty<object>());
				}
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	private void OnLicenseStatusChanged(object? sender, EventArgs e)
	{
		Application current = Application.Current;
		if (current != null)
		{
			((DispatcherObject)current).Dispatcher.BeginInvoke((Delegate)(Func<Task>)async delegate
			{
				await UpdateLicenseStatus();
			}, Array.Empty<object>());
		}
	}

	private void OnCloudAccountStateChanged(object? sender, AccountStateChangedEventArgs args)
	{
		AccountStateChangedEventArgs args2 = args;
		Application current = Application.Current;
		if (current == null)
		{
			return;
		}
		((DispatcherObject)current).Dispatcher.BeginInvoke((Delegate)(Action)delegate
		{
			string text = ((args2.IsLinked && !string.IsNullOrEmpty(args2.Email)) ? args2.Email : LocalizationService.Instance["DashboardLinkedEmailNone"]);
			if (LinkedEmail != text)
			{
				LinkedEmail = text;
				_logger?.LogInfo("[Dashboard] LinkedEmail atualizado em tempo real: " + text);
			}
		}, Array.Empty<object>());
	}

	private int GetSubscriberCount()
	{
		try
		{
			if (App.ThermalMonitorService == null)
			{
				return 0;
			}
			EventInfo @event = typeof(IGlobalThermalMonitorService).GetEvent("MetricsUpdated");
			if (@event == null)
			{
				return 0;
			}
			FieldInfo field = typeof(GlobalThermalMonitorService).GetField("MetricsUpdated", BindingFlags.Instance | BindingFlags.NonPublic);
			if (field == null)
			{
				return 0;
			}
			object value = field.GetValue(App.ThermalMonitorService);
			if (value == null)
			{
				return 0;
			}
			if (value is Delegate @delegate)
			{
				Delegate[] invocationList = @delegate.GetInvocationList();
				return (invocationList != null) ? invocationList.Length : 0;
			}
			return 0;
		}
		catch
		{
			return -1;
		}
	}

	private void OnThermalMetricsUpdated(object? sender, ThermalMetrics metrics)
	{
		// [FIX:M-1] Publica o valor mais recente ANTES de tentar adquirir a posse
		// de UI. Assim, coalescer um evento nunca perde a leitura mais nova.
		Volatile.Write(ref _pendingThermalMetrics, metrics);

		// Adquire a posse do update de UI de forma atomica (0 -> 1).
		// CompareExchange devolve o valor anterior: 1 significa "ja em voo".
		if (Interlocked.CompareExchange(ref _thermalUiUpdatePending, 1, 0) == 1)
		{
			Interlocked.Increment(ref _thermalDroppedCoalesced);
			return;
		}

		// A partir daqui a posse e OBRIGATORIA de ser liberada, mesmo se nao
		// houver Dispatcher. Sem isto o flag travava em 1 permanentemente.
		bool dispatched = false;
		try
		{
			Application current = Application.Current;
			if (current == null)
			{
				Interlocked.Increment(ref _thermalStuckFlagRescues);
				_logger?.LogWarning(
					"[FIX:M-1] Application.Current == null na recepcao termica; posse de UI liberada " +
					"(antes o flag ficava travado e a temperatura parava de atualizar)");
				return;
			}

			Dispatcher dispatcher = ((DispatcherObject)current).Dispatcher;
			if (dispatcher == null)
			{
				Interlocked.Increment(ref _thermalStuckFlagRescues);
				_logger?.LogWarning(
					"[FIX:M-1] Dispatcher == null na recepcao termica; posse de UI liberada " +
					"(antes o flag ficava travado e a temperatura parava de atualizar)");
				return;
			}

			dispatcher.BeginInvoke((Delegate)(Action)delegate
			{
				try
				{
					ThermalMetrics? pendingThermalMetrics = Volatile.Read(ref _pendingThermalMetrics);
					if (pendingThermalMetrics != null)
					{
						double cpuTemperature = CpuTemperature;
						double gpuTemperature = GpuTemperature;
					// Propaga o NaN em vez de convertê-lo em 0.0.
					//
					// A versão anterior fazia `IsNaN(x) ? 0.0 : x`, ou seja, transformava
					// "não foi possível verificar" em 0 °C — uma temperatura assertada e
					// errada. Pior: 0.0 alimenta o score de saúde, que tratava <= 0 como
					// "sem dado", mas também aparecia como leitura válida em qualquer outro
					// consumidor. Mantendo NaN, a distinção entre ausência de sensor e
					// temperatura medida continua explícita até a tela.
					CpuTemperature = pendingThermalMetrics.CpuTemperature;
					GpuTemperature = pendingThermalMetrics.GpuTemperature;
						IsTemperatureEstimated = pendingThermalMetrics.IsCpuTemperatureEstimated;
						bool flag = Math.Abs(cpuTemperature - CpuTemperature) > 1.0;
						bool flag2 = Math.Abs(gpuTemperature - GpuTemperature) > 1.0;
						if (flag || flag2)
						{
							_logger?.LogTrace($"[Dashboard] Temperatura atualizada - CPU: {CpuTemperature:F1}°C GPU: {GpuTemperature:F1}°C");
						}
						UpdateThermalStatus(pendingThermalMetrics);
					}
				}
				catch (Exception ex)
				{
					_logger?.LogError("[Dashboard] Falha ao aplicar atualização térmica: " + ex.Message, ex);
				}
				finally
				{
					// [FIX:M-1] Libera a posse mesmo se o corpo lancar.
					Interlocked.Exchange(ref _thermalUiUpdatePending, 0);
				}
			}, (DispatcherPriority)9, Array.Empty<object>());

			dispatched = true;
		}
		catch (Exception ex)
		{
			_logger?.LogError("[Dashboard] Falha ao despachar atualização térmica: " + ex.Message, ex);
		}
		finally
		{
			// Se o BeginInvoke nem chegou a ser enfileirado, a posse fica orfa.
			if (!dispatched)
			{
				Interlocked.Exchange(ref _thermalUiUpdatePending, 0);
			}
		}
	}

	private void OnThermalAlertGenerated(object? sender, ThermalAlert alert)
	{
		ThermalAlert alert2 = alert;
		Application current = Application.Current;
		if (current != null)
		{
			((DispatcherObject)current).Dispatcher.BeginInvoke((Delegate)(Action)delegate
			{
				HasThermalAlert = true;
				GlobalNotificationService.ShowInfo(alert2.Message, alert2.Recommendation);
				_logger?.LogWarning("[Dashboard] Alerta térmico: " + alert2.Message);
			}, Array.Empty<object>());
		}
	}

	private void SubscribeThermalEvents()
	{
		if (_isThermalSubscribed)
		{
			return;
		}
		Task.Run(delegate
		{
			lock (_thermalLock)
			{
				if (_isThermalSubscribed)
				{
					return;
				}
				try
				{
					GlobalThermalMonitorService instance = GlobalThermalMonitorService.Instance;
					instance.MetricsUpdated -= OnThermalMetricsUpdated;
					instance.MetricsUpdated += OnThermalMetricsUpdated;
					instance.AlertGenerated -= OnThermalAlertGenerated;
					instance.AlertGenerated += OnThermalAlertGenerated;
					_isThermalSubscribed = true;
					DiagLog("DashboardViewModel: Subscrito aos eventos do ThermalMonitorService");
				}
				catch (Exception ex)
				{
					_logger?.LogError("[Dashboard] Erro ao inscrever no serviço térmico: " + ex.Message);
				}
			}
		});
	}

	private void UnsubscribeThermalEvents()
	{
		lock (_thermalLock)
		{
			if (!_isThermalSubscribed)
			{
				return;
			}
			try
			{
				GlobalThermalMonitorService instance = GlobalThermalMonitorService.Instance;
				instance.MetricsUpdated -= OnThermalMetricsUpdated;
				instance.AlertGenerated -= OnThermalAlertGenerated;
				_isThermalSubscribed = false;
				DiagLog("DashboardViewModel: Desinscrito dos eventos do ThermalMonitorService");
			}
			catch
			{
			}
		}
	}

	protected override void OnActiveChanged()
	{
		if (base.IsActive)
		{
			SystemMetricsCache.Instance.SetUpdateSpeed(MetricsUpdateSpeed.Normal);
			SystemMetricsCache.Instance.MetricsUpdated -= OnGlobalMetricsUpdated;
			SystemMetricsCache.Instance.MetricsUpdated += OnGlobalMetricsUpdated;
			UpdateMetricsFast();
		}
		else
		{
			SystemMetricsCache.Instance.SetUpdateSpeed(MetricsUpdateSpeed.Normal);
			SystemMetricsCache.Instance.MetricsUpdated -= OnGlobalMetricsUpdated;
		}
	}

	private void InitializeLicenseStateSync()
	{
		try
		{
			bool isProActive = LicenseTokenStore.IsProActive;
			string licenseType = LicenseTokenStore.LicenseType;
			_logger?.LogInfo($"[LicenseUI] Initial license state: IsProActive={isProActive}, Type={licenseType}");
			if (isProActive && !string.Equals(licenseType, "None", StringComparison.OrdinalIgnoreCase) && !string.Equals(licenseType, "Trial", StringComparison.OrdinalIgnoreCase))
			{
				string licenseDisplayName = LicenseTokenStore.LicenseDisplayName;
				_licenseType = (string.IsNullOrEmpty(licenseDisplayName) ? licenseType : licenseDisplayName);
				_supportLevel = LocalizationService.Instance["DashboardLicenseSupportPremium"];
				_isLicenseStateLoading = false;
				_logger?.LogInfo($"[LicenseUI] License loaded: {_licenseType} ({licenseType})");
				_logger?.LogInfo("[LicenseUI] Plans card visibility: HIDDEN");
				_logger?.LogInfo("[LicenseUI] No visual state transition required");
			}
			else
			{
				_licenseType = LocalizationService.Instance["DashboardLicenseTypeUnknown"];
				_supportLevel = LocalizationService.Instance["DashboardLicenseSupportBasic"];
				_isLicenseStateLoading = false;
				_logger?.LogInfo("[LicenseUI] Initial license state: no active license found");
				_logger?.LogInfo("[LicenseUI] Plans card visibility: VISIBLE");
			}
		}
		catch (Exception ex)
		{
			_licenseType = LocalizationService.Instance["DashboardLicenseTypeUnknown"];
			_supportLevel = LocalizationService.Instance["DashboardLicenseSupportBasic"];
			_isLicenseStateLoading = false;
			_logger?.LogError("[LicenseUI] Error during sync license initialization: " + ex.Message);
		}
	}

	private void OnGlobalMetricsUpdated(object? sender, EventArgs e)
	{
		try
		{
			DateTime utcNow = DateTime.UtcNow;
			if ((utcNow - _lastUIMetricsUpdate).TotalMilliseconds < 500.0)
			{
				return;
			}
			_lastUIMetricsUpdate = utcNow;
			Application current = Application.Current;
			if (((current != null) ? ((DispatcherObject)current).Dispatcher : null) == null)
			{
				return;
			}
			((DispatcherObject)Application.Current).Dispatcher.BeginInvoke((Delegate)(Action)delegate
			{
				try
				{
					UpdateMetricsFast();
					UpdateBrainMood();
				}
				catch (Exception ex2)
				{
					_logger?.LogTrace("[Dashboard] OnGlobalMetricsUpdated inner error: " + ex2.Message);
				}
			}, (DispatcherPriority)4, Array.Empty<object>());
		}
		catch (Exception ex)
		{
			_logger?.LogTrace("[Dashboard] OnGlobalMetricsUpdated dispatch error: " + ex.Message);
		}
	}

	private void OnApplicationLifecycleStateChanged(object? sender, ApplicationLifecycleStateChangedEventArgs e)
	{
		try
		{
			UpdateAnimationsBasedOnLifecycleState(e.NewState);
			switch (e.NewState)
			{
			case ApplicationLifecycleState.Foreground:
				_logger?.LogInfo($"[Performance] Component: DashboardViewModel | State: {e.OldState} -> {e.NewState} | Reason: Application foregrounded");
				if (base.IsActive)
				{
					SystemMetricsCache.Instance.MetricsUpdated -= OnGlobalMetricsUpdated;
					SystemMetricsCache.Instance.MetricsUpdated += OnGlobalMetricsUpdated;
				}
				break;
			case ApplicationLifecycleState.Background:
				_logger?.LogInfo($"[Performance] Component: DashboardViewModel | State: {e.OldState} -> {e.NewState} | Reason: Application backgrounded");
				break;
			case ApplicationLifecycleState.Minimized:
				_logger?.LogInfo($"[Performance] Component: DashboardViewModel | State: {e.OldState} -> {e.NewState} | Reason: Application minimized");
				SystemMetricsCache.Instance.MetricsUpdated -= OnGlobalMetricsUpdated;
				break;
			case ApplicationLifecycleState.Tray:
				_logger?.LogInfo($"[Performance] Component: DashboardViewModel | State: {e.OldState} -> {e.NewState} | Reason: Application sent to tray");
				SystemMetricsCache.Instance.MetricsUpdated -= OnGlobalMetricsUpdated;
				break;
			}
		}
		catch (Exception ex)
		{
			_logger?.LogError("[Performance] Error in OnApplicationLifecycleStateChanged: " + ex.Message);
		}
	}

	private void UpdateAnimationsBasedOnLifecycleState(ApplicationLifecycleState state)
	{
		bool flag = state == ApplicationLifecycleState.Foreground;
		if (_appForeground != flag)
		{
			_appForeground = flag;
			ApplyRuntimeState();
			if (flag)
			{
				_logger?.LogInfo("[GFX] Application foregrounded");
				_logger?.LogInfo("[GFX] Visual effects resumed");
			}
			else
			{
				_logger?.LogInfo("[GFX] Application backgrounded");
				_logger?.LogInfo("[GFX] Visual effects paused");
				_logger?.LogInfo("[GFX] Timers suspended");
			}
		}
	}

	private void UpdateThermalStatus(ThermalMetrics metrics)
	{
		ThermalMetrics metrics2 = metrics;
		Application current = Application.Current;
		if (current != null)
		{
			Dispatcher dispatcher = ((DispatcherObject)current).Dispatcher;
			if (((dispatcher != null) ? new bool?(dispatcher.CheckAccess()) : null) == false)
			{
				((DispatcherObject)Application.Current).Dispatcher.BeginInvoke((Delegate)(Action)delegate
				{
					UpdateThermalStatus(metrics2);
				}, Array.Empty<object>());
				return;
			}
		}
		// Temperatura efetiva: maior entre CPU e GPU COM LEITURA VÁLIDA.
		//
		// A expressão anterior era
		//     double.IsNaN(Gpu) ? Cpu : Math.Max(Cpu, Gpu)
		// que só protegia o caso "GPU ausente". No caso inverso — CPU sem sensor e
		// GPU legível — Math.Max(NaN, Gpu) devolvia NaN em .NET, e uma GPU a 85 °C
		// era apresentada como "sem informação" em vez de gerar alerta. Além disso,
		// valores absurdos (fora de 0–150 °C) entravam sem filtro.
		double num = EffectiveTemperature(metrics2.CpuTemperature, metrics2.GpuTemperature);
		if (double.IsNaN(num))
		{
			// Sem sensor: exibir "indisponível" e NÃO marcar como temperatura normal,
			// porque "normal" aqui significaria ter medido algo.
			ThermalStatus = LocalizationService.Instance["ThermalStatusUnknown"];
			ThermalStatusColor = "#9CA3AF";
			HasThermalAlert = false;
			IsThermalElevated = false;
			return;
		}
		if (num >= 90.0)
		{
			ThermalStatus = LocalizationService.Instance["ThermalStatusCritical"];
			ThermalStatusColor = "#EF4444";
			HasThermalAlert = true;
			IsThermalElevated = true;
		}
		else if (num >= 80.0)
		{
			ThermalStatus = LocalizationService.Instance["ThermalStatusAlert"];
			ThermalStatusColor = "#F59E0B";
			HasThermalAlert = true;
			IsThermalElevated = true;
		}
		else if (num >= 70.0)
		{
			ThermalStatus = LocalizationService.Instance["ThermalStatusElevated"];
			ThermalStatusColor = "#F59E0B";
			HasThermalAlert = false;
			IsThermalElevated = true;
		}
		else
		{
			ThermalStatus = LocalizationService.Instance["ThermalStatusNormal"];
			ThermalStatusColor = "#10B981";
			HasThermalAlert = false;
			IsThermalElevated = false;
		}
		UpdateBrainMood();
	}

	private async Task InitializeStaticInfo()
	{
		try
		{
			DiagLog("DashboardViewModel: InitializeStaticInfo INÍCIO");
			VoltrisDiagnosticSystem.Instance.Timeline("DASHBOARD", "InitializeStaticInfo iniciado");
			Stopwatch sw = Stopwatch.StartNew();
			Application current = Application.Current;
			int num;
			if (current == null)
			{
				num = 0;
			}
			else
			{
				Dispatcher dispatcher = ((DispatcherObject)current).Dispatcher;
				num = (((dispatcher != null) ? new bool?(dispatcher.CheckAccess()) : null).GetValueOrDefault() ? 1 : 0);
			}
			if (num != 0)
			{
				DiagLog("[CRITICAL] InitializeStaticInfo chamado na UI thread! Isso causa freeze.");
				VoltrisDiagnosticSystem.Instance.Timeline("DASHBOARD-VIOLATION", "InitializeStaticInfo chamado na UI thread — CAUSA FREEZE");
			}
			string cpuName = null;
			try
			{
				using RegistryKey key = Registry.LocalMachine.OpenSubKey("HARDWARE\\DESCRIPTION\\System\\CentralProcessor\\0");
				if (key != null)
				{
					string name = key.GetValue("ProcessorNameString")?.ToString();
					if (!string.IsNullOrWhiteSpace(name))
					{
						cpuName = Regex.Replace(name, "\\s+", " ").Trim();
					}
				}
			}
			catch
			{
			}
			if (string.IsNullOrEmpty(cpuName))
			{
				try
				{
					string cpuNameRaw = (await WmiHelper.QuerySafeAsync("SELECT Name FROM Win32_Processor").ConfigureAwait(continueOnCapturedContext: false))?.FirstOrDefault()?["Name"]?.ToString();
					cpuName = ((!string.IsNullOrWhiteSpace(cpuNameRaw)) ? cpuNameRaw : LocalizationService.Instance.GetString("GenericProcessor"));
				}
				catch
				{
					cpuName = LocalizationService.Instance.GetString("GenericProcessor");
				}
			}
			Application current2 = Application.Current;
			if (current2 != null)
			{
				((DispatcherObject)current2).Dispatcher.BeginInvoke((Delegate)(Action)delegate
				{
					CpuName = cpuName;
				}, Array.Empty<object>());
			}
			DiagLog($"DashboardViewModel: InitializeStaticInfo - CPU resolvida em {sw.ElapsedMilliseconds}ms");
			VoltrisDiagnosticSystem.Instance.Timeline("DASHBOARD", $"CPU resolvida em {sw.ElapsedMilliseconds}ms");
			if (_wmiCache != null)
			{
				double totalRam = await Task.Run(() => _wmiCache.GetOrUpdate("total_ram", () => SystemMetricsCache.Instance.Hardware.TotalRamGb, TimeSpan.FromHours(24.0))).ConfigureAwait(continueOnCapturedContext: false);
				string ramInfo = $"Total: {totalRam:F1} GB";
				Application current3 = Application.Current;
				if (current3 != null)
				{
					((DispatcherObject)current3).Dispatcher.BeginInvoke((Delegate)(Action)delegate
					{
						RamInfo = ramInfo;
					}, Array.Empty<object>());
				}
			}
			DiagLog($"DashboardViewModel: InitializeStaticInfo - FIM em {sw.ElapsedMilliseconds}ms");
			VoltrisDiagnosticSystem.Instance.Timeline("DASHBOARD", $"InitializeStaticInfo concluído em {sw.ElapsedMilliseconds}ms");
		}
		catch (Exception ex2)
		{
			Exception ex = ex2;
			DiagLog("DashboardViewModel: InitializeStaticInfo ERRO: " + ex.Message);
			_logger?.LogError("Erro ao inicializar informações estáticas: " + ex.Message);
			VoltrisDiagnosticSystem.Instance.Timeline("DASHBOARD-ERROR", "InitializeStaticInfo falhou: " + ex.Message);
		}
	}

	private async Task InitializeSystemStatus()
	{
		try
		{
			DiagLog("DashboardViewModel: InitializeSystemStatus INÍCIO");
			VoltrisDiagnosticSystem.Instance.Timeline("DASHBOARD", "InitializeSystemStatus iniciado");
			await UpdateLicenseStatus();
			DiagLog("DashboardViewModel: InitializeSystemStatus - Licença atualizada");
			UpdateHealthStatus();
			DiagLog("DashboardViewModel: InitializeSystemStatus - Health atualizado");
			try
			{
				if (App.ThermalMonitorService?.CurrentMetrics != null)
				{
					ThermalMetrics initialMetrics = App.ThermalMonitorService.CurrentMetrics;
					Application current = Application.Current;
					if (current != null)
					{
						((DispatcherObject)current).Dispatcher.BeginInvoke((Delegate)(Action)delegate
						{
							// NaN propagado (ver comentário no caminho de atualização): 0.0 seria
							// uma temperatura afirmada e errada, não "sem sensor".
							CpuTemperature = initialMetrics.CpuTemperature;
							GpuTemperature = initialMetrics.GpuTemperature;
							IsTemperatureEstimated = initialMetrics.IsCpuTemperatureEstimated;
							UpdateThermalStatus(initialMetrics);
							_logger?.LogInfo($"[DASHBOARD] Temperaturas iniciais: CPU={FormatTemperature(CpuTemperature)} GPU={FormatTemperature(GpuTemperature)}");
						}, Array.Empty<object>());
					}
				}
			}
			catch (Exception ex3)
			{
				Exception ex2 = ex3;
				_logger?.LogError("[DASHBOARD] Erro ao ler métricas térmicas iniciais: " + ex2.Message);
			}
			DiagLog("DashboardViewModel: InitializeSystemStatus FIM");
			VoltrisDiagnosticSystem.Instance.Timeline("DASHBOARD", "InitializeSystemStatus concluído");
		}
		catch (Exception ex3)
		{
			Exception ex = ex3;
			DiagLog("DashboardViewModel: InitializeSystemStatus ERRO: " + ex.Message);
			VoltrisDiagnosticSystem.Instance.Timeline("DASHBOARD-ERROR", "InitializeSystemStatus falhou: " + ex.Message);
			_logger?.LogError("[DASHBOARD] Erro ao inicializar status do sistema", ex);
		}
	}

	private void UpdateMetricsFast()
	{
		Application current = Application.Current;
		if (((current != null) ? ((DispatcherObject)current).Dispatcher : null) == null)
		{
			return;
		}
		if (!((DispatcherObject)Application.Current).Dispatcher.CheckAccess())
		{
			((DispatcherObject)Application.Current).Dispatcher.BeginInvoke((Delegate)new Action(UpdateMetricsFast), (DispatcherPriority)4, Array.Empty<object>());
			return;
		}
		try
		{
			SystemMetricsCache instance = SystemMetricsCache.Instance;
			double cpuPercent = instance.CpuPercent;
			double memoryUsedPercent = instance.MemoryUsedPercent;
			double diskUsagePercent = instance.DiskUsagePercent;
			double gpuUsagePercent = instance.GpuUsagePercent;
			double totalRamGb = instance.Hardware.TotalRamGb;
			double value = totalRamGb * (memoryUsedPercent / 100.0);
			DateTime utcNow = DateTime.UtcNow;
			if ((utcNow - _lastDiskInfoUpdate).TotalMilliseconds >= 60000.0)
			{
				_lastDiskInfoUpdate = utcNow;
				if (Interlocked.CompareExchange(ref _isDiskInfoUpdating, 1, 0) == 0)
				{
					GetDiskInfoAsync().ContinueWith(delegate(Task<string> t)
					{
						Task<string> t2 = t;
						Interlocked.Exchange(ref _isDiskInfoUpdating, 0);
						if (t2.IsCompletedSuccessfully && !string.IsNullOrEmpty(t2.Result))
						{
							Application current2 = Application.Current;
							if (current2 != null)
							{
								Dispatcher dispatcher = ((DispatcherObject)current2).Dispatcher;
								if (dispatcher != null)
								{
									dispatcher.BeginInvoke((Delegate)(Action)delegate
									{
										_cachedDiskInfo = t2.Result;
										if (DiskInfo != _cachedDiskInfo)
										{
											DiskInfo = _cachedDiskInfo;
										}
										DiagLog("[Dashboard] DiskInfo atualizado: " + t2.Result);
									}, Array.Empty<object>());
								}
							}
						}
					}, TaskContinuationOptions.None);
				}
			}
			CpuUsage = Math.Round(cpuPercent, 1);
			RamUsage = Math.Round(memoryUsedPercent, 1);
			GpuUsage = Math.Round(gpuUsagePercent, 0);
			DiskUsage = Math.Round(diskUsagePercent, 0);
			try
			{
				NetworkRateSample networkRateSample = NetworkUsageTracker.Sample();
				NetworkDownText = "↓ " + FileSystemHelper.FormatBytes((long)networkRateSample.DownloadBytesPerSec) + "/s";
				NetworkUpText = "↑ " + FileSystemHelper.FormatBytes((long)networkRateSample.UploadBytesPerSec) + "/s";
			}
			catch
			{
			}
			if (_diskNormalBrush == null)
			{
				_diskNormalBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#FF4B6B");
				((Freezable)_diskNormalBrush).Freeze();
				_diskWarningBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#F59E0B");
				((Freezable)_diskWarningBrush).Freeze();
				_diskHighBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#EF4444");
				((Freezable)_diskHighBrush).Freeze();
			}
			ObservableCollection<DiskMetricInfo> disks = instance.Disks;
			int count = disks.Count;
			bool flag = Disks.Count != count;
			if (!flag)
			{
				for (int i = 0; i < count; i++)
				{
					if (!string.Equals(Disks[i].Name, disks[i].Name, StringComparison.Ordinal))
					{
						flag = true;
						break;
					}
				}
			}
			if (flag)
			{
				Disks.Clear();
				for (int j = 0; j < count; j++)
				{
					DiskMetricInfo diskMetricInfo = disks[j];
					Disks.Add(new DiskItemViewModel
					{
						Name = diskMetricInfo.Name,
						UsagePercent = diskMetricInfo.UsagePercent,
						UsageText = $"{diskMetricInfo.UsagePercent:F0}%",
						Color = _diskNormalBrush
					});
				}
			}
			if (Disks.Count > 0 && count > 0)
			{
				for (int k = 0; k < Disks.Count; k++)
				{
					DiskItemViewModel diskItemViewModel = Disks[k];
					DiskMetricInfo diskMetricInfo2 = disks[k];
					if (!string.Equals(diskMetricInfo2.Name, diskItemViewModel.Name, StringComparison.Ordinal))
					{
						DiskMetricInfo diskMetricInfo3 = null;
						for (int l = 0; l < count; l++)
						{
							if (string.Equals(disks[l].Name, diskItemViewModel.Name, StringComparison.Ordinal))
							{
								diskMetricInfo3 = disks[l];
								break;
							}
						}
						diskMetricInfo2 = diskMetricInfo3;
					}
					if (diskMetricInfo2 != null)
					{
						double num = (diskItemViewModel.UsagePercent = diskMetricInfo2.UsagePercent);
						diskItemViewModel.UsageText = $"{num:F0}%";
						diskItemViewModel.Color = ((num > 90.0) ? _diskHighBrush : ((num > 75.0) ? _diskWarningBrush : _diskNormalBrush));
					}
				}
			}
			string text = $"{value:F1}GB / {totalRamGb:F1}GB";
			if (_ramInfo != text)
			{
				RamInfo = text;
			}
			int num2 = ((CpuHistory.Count < 30) ? CpuHistory.Count : 0);
			if (num2 == 30)
			{
				num2 = (int)(DateTime.UtcNow.Ticks % 30);
				CpuHistory[num2] = cpuPercent;
				RamHistory[num2] = memoryUsedPercent;
			}
			else
			{
				if (CpuHistory.Count < 30)
				{
					CpuHistory.Add(cpuPercent);
				}
				if (RamHistory.Count < 30)
				{
					RamHistory.Add(memoryUsedPercent);
				}
			}
			UpdateHealthStatus();
		}
		catch (Exception ex)
		{
			_logger?.LogTrace("[Dashboard] Erro em UpdateMetricsFast: " + ex.Message);
		}
	}

	private async Task UpdateMetricsFastAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		try
		{
			_ = SystemMetricsCache.Instance;
			await Task.Run(delegate
			{
			}, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			Application current = Application.Current;
			if (current == null)
			{
				return;
			}
			Dispatcher dispatcher = ((DispatcherObject)current).Dispatcher;
			if (dispatcher != null)
			{
				dispatcher.InvokeAsync((Action)delegate
				{
					UpdateMetricsFast();
				}, (DispatcherPriority)4);
			}
		}
		catch (Exception ex2)
		{
			Exception ex = ex2;
			_logger?.LogError("[Dashboard] Erro ao atualizar métricas assíncronas", ex);
		}
	}

	private async Task UpdateSystemHealthAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		try
		{
			UpdateHealthStatus(force: true, "Refresh");
		}
		catch (Exception ex2)
		{
			Exception ex = ex2;
			_logger?.LogError("[Dashboard] Erro ao atualizar saúde do sistema", ex);
		}
	}

	private async Task<string> GetDiskInfoAsync()
	{
		if (DateTime.UtcNow < _nextDiskInfoRefresh && _cachedDiskInfoString != null)
		{
			return _cachedDiskInfoString;
		}
		try
		{
			string result = (_cachedDiskInfoString = await Task.Run(delegate
			{
				try
				{
					System.IO.DriveInfo driveInfo = new System.IO.DriveInfo("C");
					if (driveInfo.IsReady)
					{
						double num = (double)driveInfo.TotalSize / 1073741824.0;
						double num2 = (double)driveInfo.TotalFreeSpace / 1073741824.0;
						double value = num - num2;
						return $"{value:F1}GB / {num:F1}GB";
					}
				}
				catch
				{
				}
				return "Indisponível";
			}).WaitAsync(TimeSpan.FromSeconds(3.0)));
			_nextDiskInfoRefresh = DateTime.UtcNow.AddSeconds(30.0);
			return result;
		}
		catch
		{
			return _cachedDiskInfoString ?? "Indisponível";
		}
	}

		/// <summary>
		/// Formata temperatura para LOG, sem nunca imprimir "NaN°C".
		/// No log a ausência precisa ser legível como ausência.
		/// </summary>
		private static string FormatTemperature(double value) =>
			double.IsNaN(value) || value <= 0 || value >= 150 ? "N/D" : $"{value:F1}°C";

		/// <summary>
		/// Maior temperatura entre as leituras realmente disponíveis.
	/// Devolve <see cref="double.NaN"/> se nenhuma existir — nunca 0, nunca um
	/// default, porque um número inventado aqui viraria score de saúde.
	/// </summary>
	private static double MaxAvailableTemperature(double cpuTemp, bool cpuTempOk, double gpuTemp, bool gpuTempOk)
	{
		double best = double.NaN;
		if (cpuTempOk) best = cpuTemp;
		if (gpuTempOk && (double.IsNaN(best) || gpuTemp > best)) best = gpuTemp;
		return best;
	}

	/// <summary>
	/// Temperatura efetiva para exibição e alertas: a maior entre CPU e GPU que
	/// tenham leitura válida. Sem sensor em nenhum dos dois, <see cref="double.NaN"/>.
	/// </summary>
	private static double EffectiveTemperature(double cpuTemp, double gpuTemp)
	{
		var cpuOk = !double.IsNaN(cpuTemp) && cpuTemp > 0 && cpuTemp < 150;
		var gpuOk = !double.IsNaN(gpuTemp) && gpuTemp > 0 && gpuTemp < 150;
		return MaxAvailableTemperature(cpuTemp, cpuOk, gpuTemp, gpuOk);
	}

	private void UpdateHealthStatus(bool force = false, string source = "Métricas")
	{
		string source2 = source;
		Application current = Application.Current;
		if (current != null)
		{
			Dispatcher dispatcher = ((DispatcherObject)current).Dispatcher;
			if (((dispatcher != null) ? new bool?(dispatcher.CheckAccess()) : null) == false)
			{
				((DispatcherObject)Application.Current).Dispatcher.BeginInvoke((Delegate)(Action)delegate
				{
					UpdateHealthStatus(force, source2);
				}, Array.Empty<object>());
				return;
			}
		}
		lock (_healthLock)
		{
			if (!force && DateTime.UtcNow - _lastHealthAnalysisUtc < TimeSpan.FromSeconds(30.0))
			{
				_logger?.LogTrace("[HEALTH] Análise ignorada (janela de 30s ativa) · fonte: " + source2);
				return;
			}
			_lastHealthAnalysisUtc = DateTime.UtcNow;
			try
			{
				_logger?.LogInfo("[HEALTH] ——— Análise de saúde iniciada (fonte: " + source2 + ") ———");
				double num = HealthClamp01(CpuUsage / 100.0);
				double num2 = HealthClamp01(RamUsage / 100.0);
				double num3 = HealthClamp(100.0 - (num - 0.7) / 0.3 * 100.0, 0.0, 100.0);
				double num4 = HealthClamp(100.0 - (num2 - 0.7) / 0.3 * 100.0, 0.0, 100.0);
				_logger?.LogInfo($"[HEALTH][SINAL] CPU = {num * 100.0:F1}% → score {num3:F0}/100 (peso 25%)");
				_logger?.LogInfo($"[HEALTH][SINAL] RAM = {num2 * 100.0:F1}% → score {num4:F0}/100 (peso 25%)");

				// ── DISCO ──
				// A versão anterior usava Math.Min sobre TODOS os volumes fixos, de modo
				// que uma partição de recuperação de 100 MB a 2% livre zerava o
				// componente de disco mesmo com o C: saudável (falso positivo
				// garantido), e devolvia 100.0 quando não havia nenhum disco fixo.
				//
				// Agora considera apenas volumes com espaço ÚTIL (>= 1 GB) e, quando
				// nenhum é encontrado, declara o componente como INDISPONÍVEL em vez
				// de assumir HEALTHY=100.
				var usableDisks = GetUsableFixedDisks();
				double num5;
				bool diskAvailable = usableDisks.Count > 0;
				if (!diskAvailable)
				{
					num5 = double.NaN;
					_logger?.LogInfo("[HEALTH][SINAL] Disco = NENHUM volume fixo utilizável encontrado → componente INDISPONÍVEL.");
				}
				else
				{
					double minFixedDiskFreePercent = usableDisks.Min(d => d.FreePercent);
					num5 = ((!(minFixedDiskFreePercent < 5.0)) ? ((minFixedDiskFreePercent < 10.0) ? 30 : ((minFixedDiskFreePercent < 15.0) ? 60 : ((minFixedDiskFreePercent < 20.0) ? 80 : 100))) : 0);
					var usados = string.Join(", ", usableDisks.Select(d => $"{d.Label}:{d.FreePercent:F0}%"));
					_logger?.LogInfo($"[HEALTH][SINAL] Disco = menor volume com espaço útil {minFixedDiskFreePercent:F1}% livre ({usados}) → score {num5:F0}/100 (peso 20%)");
				}

				// ── TÉRMICA ──
				// Temperatura sem sensor real agora chega como NaN. A versão anterior
				// substituía por estimativas (45 °C / max(35, CPU-3) / 30+carga*50) e,
				// quando mesmo assim não havia dado, assumia score 70 — ou seja,
				// FALHA DE SENSOR PRODUZIA A MELHOR NOTA POSSÍVEL. O componente
				// térmico era, na prática, 100% da temperatura da CPU.
				double cpuTemp = CpuTemperature;
				double gpuTemp = GpuTemperature;
				bool cpuTempOk = !double.IsNaN(cpuTemp) && cpuTemp > 0 && cpuTemp < 150;
				bool gpuTempOk = !double.IsNaN(gpuTemp) && gpuTemp > 0 && gpuTemp < 150;

				// MÁXIMO CORRETO SOBRE AS LEITURAS DISPONÍVEIS.
				//
				// A versão anterior usava:
				//     Math.Max(cpuTempOk ? cpuTemp : double.NaN,
				//              gpuTempOk ? gpuTemp : double.NaN)
				// Em .NET, Math.Max devolve NaN se QUALQUER operando for NaN. Logo,
				// bastava um único sensor ausente para descartar também a leitura
				// VÁLIDA do outro: uma GPU com sensor em 80 °C era tratada como
				// "sem sensor", o componente térmico saía da ponderação e o score
				// de saúde deixava de refletir a temperatura real. O máximo tem de
				// ser calculado apenas sobre as leituras que existem.
				double num6 = MaxAvailableTemperature(cpuTemp, cpuTempOk, gpuTemp, gpuTempOk);
				bool thermalAvailable = !double.IsNaN(num6);
				double num7;
				if (!thermalAvailable)
				{
					num7 = double.NaN;
					_logger?.LogInfo("[HEALTH][SINAL] Térmica = SEM SENSOR REAL → componente INDISPONÍVEL " +
						$"(CPU:{(cpuTempOk ? $"{cpuTemp:F1}°C" : "N/D")}, GPU:{(gpuTempOk ? $"{gpuTemp:F1}°C" : "N/D")}). " +
						"O VOLTRIS não estima temperaturas ausentes.");
				}
				else
				{
					num7 = ((num6 <= 70.0) ? 100 : ((num6 <= 75.0) ? 80 : ((num6 <= 82.0) ? 55 : ((num6 <= 88.0) ? 30 : 10))));
					_logger?.LogInfo($"[HEALTH][SINAL] Térmica = máx {num6:F1}°C → score {num7:F0}/100 (peso 20%)");
				}

				double totalDays = TimeSpan.FromMilliseconds(Environment.TickCount64).TotalDays;
				double num8 = ((totalDays >= 7.0) ? 50 : ((totalDays >= 5.0) ? 65 : ((totalDays >= 3.0) ? 80 : 100)));
				_logger?.LogInfo($"[HEALTH][SINAL] Uptime = {totalDays:F1} dias → score {num8:F0}/100 (peso 10%)");

				// ── SCORE COM PONDERAÇÃO SOMENTE SOBRE COMPONENTES DISPONÍVEIS ──
				// A versão anterior somava um valor neutro inventado (70) para disco e
				// térmica indisponíveis, inflando artificialmente o total. Agora os
				// componentes sem dado são EXCLUÍDOS e o peso é renormalizado, com o
				// número de métricas efetivamente avaliadas exposto no log e na
				// descrição, para que o usuário saiba o que o score cobre.
				var componentes = new List<(string Nome, double Score, double Peso)>
				{
					("CPU", num3, 0.25),
					("RAM", num4, 0.25),
					("Uptime", num8, 0.10)
				};
				if (diskAvailable) componentes.Add(("Disco", num5, 0.20));
				if (thermalAvailable) componentes.Add(("Térmica", num7, 0.20));

				double pesoTotal = componentes.Sum(c => c.Peso);
				int num9 = (int)Math.Round(componentes.Sum(c => c.Score * c.Peso) / pesoTotal);
				string componentesAvaliados = string.Join(", ", componentes.Select(c => c.Nome));
				string componentesAusentes = string.Join(", ",
					new[] { ("Disco", diskAvailable), ("Térmica", thermalAvailable) }
						.Where(x => !x.Item2).Select(x => x.Item1));

				_logger?.LogInfo($"[HEALTH][TOTAL] Score {num9}/100 · avaliados: {componentesAvaliados}" +
					(string.IsNullOrEmpty(componentesAusentes) ? "" : $" · NÃO AVALIADOS (sem dado real): {componentesAusentes}"));

				string key = ((num9 >= 85) ? "HealthStatusOptimum" : ((num9 >= 70) ? "ScoreStatusGood" : ((num9 >= 55) ? "ScoreStatusRegular" : ((num9 >= 40) ? "HealthStatusAlert" : "HealthStatusCritical"))));
				string text = ((num9 >= 85) ? "#10B981" : ((num9 >= 70) ? "#22C55E" : ((num9 >= 55) ? "#F59E0B" : ((num9 >= 40) ? "#F97316" : "#EF4444"))));
				string key2 = ((num9 >= 70) ? "DashboardHealthDescriptionOptimum" : ((num9 >= 40) ? "DashboardHealthDescriptionAlert" : "DashboardHealthDescriptionCritical"));
				string text2 = LocalizationService.Instance[key];
				string text3 = LocalizationService.Instance[key2];

				// Informa explicitamente o que NÃO foi possível verificar.
				if (!string.IsNullOrEmpty(componentesAusentes))
				{
					text3 = string.Format(LocalizationService.Instance["DashboardHealthPartialVerification"],
						string.Join(", ", componentesAusentes.Split(", ")));

					// BUG CORRIGIDO: WARNING repetido a cada recalculo (~30s).
					// Numa maquina sem sensor termico "Termica" esta sempre
					// ausente, entao a mesma linha aparecia para sempre. Agora o
					// log so acontece quando a lista de componentes ausentes
					// muda (ou quando deixa de haver componentes ausentes).
					if (!string.Equals(componentesAusentes, _ultimosComponentesAusentesLogados, StringComparison.Ordinal))
					{
						_ultimosComponentesAusentesLogados = componentesAusentes;
						_logger?.LogWarning(
							$"[HEALTH] Score parcial: {componentesAusentes} não puderam ser verificados por falta de dado real. " +
							"A descrição passa a informar isso ao usuário.");
					}
				}
				else if (!string.Equals(_ultimosComponentesAusentesLogados, "\u0000", StringComparison.Ordinal))
				{
					// Todos os voltaram a ser avaliáveis: limpa o estado para que
					// um novo componente ausente volte a ser registrado.
					_ultimosComponentesAusentesLogados = "\u0000";
				}
				_logger?.LogInfo($"[HEALTH][TOTAL] Score {num9}/100 → '{text2}' ({text}) · descrição: {text3}");
				if (num9 < 40)
				{
					_logger?.LogWarning($"[HEALTH][CRÍTICO] Score {num9}/100 - sistema abaixo do saudável (CPU={num * 100.0:F1}% RAM={num2 * 100.0:F1}%)");
				}
				else if (num9 < 55)
				{
					_logger?.LogWarning($"[HEALTH][ATENÇÃO] Score {num9}/100 - sinais de degradação (CPU={num * 100.0:F1}% RAM={num2 * 100.0:F1}% térmica={num6:F1}°C)");
				}
				if (_healthStatusText != text2 || _healthStatusColor != text || _healthDescription != text3)
				{
					HealthStatusText = text2;
					HealthStatusColor = text;
					HealthDescription = text3;
					_logger?.LogInfo($"[HEALTH][ESTADO] Estado atualizado → '{text2}' ({text}) · descrição: {text3}");
				}
				if (_overallScore != num9 || _scoreStatus != text2 || _scoreColor != text)
				{
					OverallScore = num9;
					ScoreStatus = text2;
					ScoreColor = text;
					_logger?.LogInfo($"[HEALTH][SCORE] Score atualizado → {num9}/100 ('{text2}')");
				}
				int num10 = num9 / 10;
				if (_lastHealthBand >= 0 && _lastHealthBand != num10)
				{
					_logger?.LogInfo($"[HEALTH][MUDANÇA] Faixa de saúde alterada → '{text2}' (score {num9}/100 · fonte {source2})");
				}
				_lastHealthBand = num10;
				_logger?.LogInfo($"[HEALTH] ——— Análise concluída: score {num9}/100 · estado '{text2}' ———");
			}
			catch (Exception ex)
			{
				_logger?.LogError("[HEALTH] Falha na análise de saúde: " + ex.Message, ex);
				HealthStatusText = LocalizationService.Instance["HealthStatusError"];
				HealthStatusColor = "#EF4444";
				HealthDescription = LocalizationService.Instance["DashboardHealthError"];
			}
		}
	}

	private static double HealthClamp(double value, double min, double max)
	{
		return (value < min) ? min : ((value > max) ? max : value);
	}

	private static double HealthClamp01(double value)
	{
		return (value < 0.0) ? 0.0 : ((value > 1.0) ? 1.0 : value);
	}

	/// <summary>
	/// Um volume fixo considerado na avaliação de saúde.
	/// </summary>
	private readonly struct UsableDisk
	{
		public string Label { get; init; }
		public double FreePercent { get; init; }
	}

	/// <summary>
	/// Lista os volumes fixos com espaço ÚTIL para a análise de saúde.
	///
	/// CORREÇÃO DE AUDITORIA (dois defeitos reais):
	///  1. A versão anterior aplicava <c>Math.Min</c> sobre TODOS os volumes fixos,
	///     inclusive partições de recuperação de ~100 MB. Uma partição dessas a 2%
	///     livre zerava o componente de disco mesmo com o C: saudável — falso
	///     positivo garantido em qualquer máquina com partição de recuperação.
	///  2. Quando não havia NENHUM volume fixo pronto, devolvia <c>100.0</c>, ou
	///     seja, "disco perfeito" sem ter medido nada.
	///
	/// Agora só entram volumes com pelo menos 1 GB, e uma lista vazia significa
	/// "indisponível", tratada como tal pelo score.
	///
	/// IMPORTANTE: <see cref="DriveInfo.IsReady"/> é uma chamada ao sistema de
	/// arquivos e pode bloquear dezenas de milissegundos por volume. Por isso este
	/// método roda em thread de fundo (ver <see cref="UpdateHealthStatus"/>), e não
	/// na UI thread como antes.
	/// </summary>
	private static List<UsableDisk> GetUsableFixedDisks()
	{
		var result = new List<UsableDisk>();
		try
		{
			const long MinUsefulBytes = 1024L * 1024L * 1024L; // 1 GB

			foreach (var drive in System.IO.DriveInfo.GetDrives())
			{
				if (drive.DriveType != DriveType.Fixed) continue;
				if (!drive.IsReady) continue;

				long total = drive.TotalSize;
				if (total < MinUsefulBytes) continue;

				double freePercent = (double)drive.TotalFreeSpace / total * 100.0;
				result.Add(new UsableDisk
				{
					Label = string.IsNullOrEmpty(drive.VolumeLabel) ? drive.Name : drive.VolumeLabel,
					FreePercent = freePercent
				});
			}
		}
		catch (Exception ex)
		{
			// Enumeração de volumes é apenas best-effort: uma falha aqui resulta em
			// lista vazia, e lista vazia significa "componente indisponível" — que é
			// exatamente o comportamento correto (nada é inventado).
			App.LoggingService?.LogWarning("[HEALTH] Falha ao enumerar volumes: " + ex.Message);
		}
		return result;
	}

	internal Task UpdateLicenseStatusAsync(string callerName, CancellationToken cancellationToken = default(CancellationToken))
	{
		_logger?.LogInfo("[Dashboard] UpdateLicenseStatusAsync chamado por: " + callerName);
		return UpdateLicenseStatusAsync(cancellationToken);
	}

	internal async Task UpdateLicenseStatusAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		try
		{
			LicenseState licenseState = await LicenseOrchestrationService.Instance.GetCurrentStateAsync(cancellationToken);
			string licenseType = MapLicenseTypeForDisplay(licenseState);
			string supportLevel = MapSupportLevelForDisplay(licenseState.SupportLevel);
			_logger?.LogInfo($"[Dashboard] Licença: {licenseState.FormattedStatus} ({licenseState.ValidationSource})");
			RunOnUiThread(delegate
			{
				LicenseType = licenseType;
				SupportLevel = supportLevel;
			});
		}
		catch (Exception ex2)
		{
			Exception ex = ex2;
			_logger?.LogError("[Dashboard] Erro ao atualizar status da licença: " + ex.Message);
			RunOnUiThread(delegate
			{
				LicenseType = LocalizationService.Instance["DashboardLicenseTypeUnknown"];
				SupportLevel = LocalizationService.Instance["DashboardLicenseSupportNone"];
			});
		}
	}

	private string MapSupportLevelForDisplay(string support)
	{
		if (string.IsNullOrEmpty(support))
		{
			return LocalizationService.Instance["DashboardLicenseSupportNone"];
		}
		if (string.Equals(support, "Basic", StringComparison.OrdinalIgnoreCase) || string.Equals(support, LocalizationService.Instance["DashboardLicenseSupportBasic"], StringComparison.OrdinalIgnoreCase))
		{
			return LocalizationService.Instance["DashboardLicenseSupportBasic"];
		}
		if (string.Equals(support, "None", StringComparison.OrdinalIgnoreCase) || string.Equals(support, "Nenhum", StringComparison.OrdinalIgnoreCase))
		{
			return LocalizationService.Instance["DashboardLicenseSupportNone"];
		}
		return support;
	}

	private async Task UpdateLicenseStatus()
	{
		await UpdateLicenseStatusAsync();
	}

	private static string MapLicenseTypeForDisplay(LicenseState licenseState)
	{
		string text = licenseState?.LicenseType ?? "None";
		if (LicenseTokenStore.IsProActive)
		{
			string licenseDisplayName = LicenseTokenStore.LicenseDisplayName;
			if (!string.IsNullOrEmpty(licenseDisplayName))
			{
				string text2 = licenseDisplayName.ToUpperInvariant();
				string[] array = new string[3] { "LICENÇA ", "LICENSE ", "LICENCA " };
				string[] array2 = array;
				foreach (string text3 in array2)
				{
					if (text2.StartsWith(text3, StringComparison.OrdinalIgnoreCase))
					{
						text2 = text2.Substring(text3.Length);
						break;
					}
				}
				string[] array3 = new string[3] { " ATIVA", " ACTIVE", " ATIVO" };
				string[] array4 = array3;
				foreach (string text4 in array4)
				{
					if (text2.EndsWith(text4, StringComparison.OrdinalIgnoreCase))
					{
						text2 = text2.Substring(0, text2.Length - text4.Length);
						break;
					}
				}
				text2 = text2.Trim();
				if (!string.IsNullOrEmpty(text2))
				{
					App.LoggingService?.LogInfo("[LICENSE] Pro ativo → " + text2);
					return text2;
				}
			}
		}
		string text5 = text.ToUpperInvariant();
		if (1 == 0)
		{
		}
		string text6;
		switch (text5)
		{
		default:
			if (text5.Length != 0)
			{
				goto case null;
			}
			text6 = "TRIAL";
			break;
		case "TRIAL":
			text6 = "TRIAL";
			break;
		case "STANDARD":
			text6 = "STANDARD";
			break;
		case "PRO":
			text6 = "PRO";
			break;
		case "ENTERPRISE":
			text6 = "ENTERPRISE";
			break;
		case "NONE":
			text6 = "TRIAL";
			break;
		case null:
			text6 = text.ToUpperInvariant();
			break;
		}
		if (1 == 0)
		{
		}
		string text7 = text6;
		if (text7 == "TRIAL" && text.Equals("None", StringComparison.OrdinalIgnoreCase))
		{
			App.LoggingService?.LogInfo("[LICENSE] Nenhuma licença ativa detectada → fallback=TRIAL");
		}
		else
		{
			App.LoggingService?.LogInfo("[LICENSE] Active license: " + text7);
		}
		return text7;
	}

	private async Task UpdateLastOptimizationInfoAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		try
		{
			HistoryService historyService = HistoryService.Instance;
			DateTime? lastOptimization = await Task.Run(() => historyService.GetLastOptimizationTimestamp(), cancellationToken);
			if (lastOptimization.HasValue)
			{
				LastOptimizationTime = lastOptimization.Value.ToString("dd/MM/yyyy HH:mm");
			}
			else
			{
				LastOptimizationTime = LocalizationService.Instance["DashboardLastActionNone"];
			}
		}
		catch
		{
			LastOptimizationTime = LocalizationService.Instance["DashboardLastActionError"];
		}
	}

	private async Task UpdateLastOptimizationInfo()
	{
		await UpdateLastOptimizationInfoAsync();
	}

	private void OpenLicenseActivation()
	{
		try
		{
			_logger?.LogInfo("[Dashboard] Abrindo janela de ativação de licença");
			MainWindow mainWindow = Application.Current.MainWindow as MainWindow;
			if (mainWindow == null)
			{
				return;
			}
			((DispatcherObject)mainWindow).Dispatcher.BeginInvoke((Delegate)(Action)delegate
			{
				try
				{
					((object)mainWindow).GetType().GetMethod("ActivateLicenseHeaderButton_Click", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(mainWindow, new object[2]);
				}
				catch (TargetInvocationException ex)
				{
					Exception ex2 = ex.InnerException ?? ex;
					_logger?.LogError("[Dashboard] Erro ao invocar ActivateLicense via reflexão: " + ex2.GetType().Name + ": " + ex2.Message, ex2);
				}
				catch (Exception ex3)
				{
					_logger?.LogError("[Dashboard] Erro ao invocar ActivateLicense: " + ex3.Message, ex3);
				}
			}, Array.Empty<object>());
		}
		catch (Exception exception)
		{
			_logger?.LogError("[Dashboard] Erro ao abrir ativação", exception);
		}
	}

	public async Task QuickOptimizeAsync(string origin = "Dashboard", CancellationToken cancellationToken = default(CancellationToken))
	{
		int tid = Thread.CurrentThread.ManagedThreadId;
		DiagLog($"[DASHBOARD_VM][TID:{tid}] QuickOptimizeAsync INÍCIO (Origin: {origin})");
		if (!(await _quickOptimizeLock.WaitAsync(TimeSpan.FromSeconds(10.0)).ConfigureAwait(continueOnCapturedContext: false)))
		{
			DiagLog($"[DASHBOARD_VM][TID:{tid}] QuickOptimizeAsync TIMEOUT — lock não adquirido em 10s, ignorando");
			_logger?.LogWarning("[Dashboard] QuickOptimize ignorado: lock não adquirido em 10s (origin: " + origin + ")");
			return;
		}
		DiagLog($"[DASHBOARD_VM][TID:{tid}] QuickOptimizeAsync Adquiriu Lock");
		try
		{
			await RunQuickOptimizeCoreAsync(origin, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		}
		finally
		{
			_quickOptimizeLock.Release();
		}
	}

	private async Task RunQuickOptimizeCoreAsync(string origin, CancellationToken cancellationToken)
	{
		string origin2 = origin;
		_ = Thread.CurrentThread.ManagedThreadId;
		await Task.Run(async delegate
		{
			await ExecuteWithLicenseCheckAsync("optimization", async delegate
			{
				_logger?.LogInfo("[Dashboard] LICENSEGUARD APROVADO - Executando otimização real");
				cancellationToken.ThrowIfCancellationRequested();
				Stopwatch benchTimer = Stopwatch.StartNew();
				try
				{
					_benchmarkService?.BeginOptimization();
					GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("DashboardQuickOptimize"), isPriority: true);
					GlobalProgressService.Instance.UpdateProgress(10, LocalizationService.Instance.GetString("DashboardStartingOptimization"));
					IntelligentProfileType currentProfile = SettingsService.Instance.Settings.IntelligentProfile;
					_logger?.LogInfo($"[{origin2}.QuickOptimize] {LocalizationService.Instance.GetString("DashboardActiveProfile")}: {currentProfile}");
					Application current = Application.Current;
					if (current != null)
					{
						((DispatcherObject)current).Dispatcher.BeginInvoke((Delegate)(Func<string>)(() => GamerServiceStatus = LocalizationService.Instance["StatusOptimizing"]), Array.Empty<object>());
					}
					switch (currentProfile)
					{
					case IntelligentProfileType.EnterpriseSecure:
					{
						_logger?.LogWarning("[" + origin2 + ".QuickOptimize] " + string.Format(LocalizationService.Instance.GetString("OptimizationEnterpriseBlock"), currentProfile));
						Application current2 = Application.Current;
						if (current2 != null)
						{
							((DispatcherObject)current2).Dispatcher.BeginInvoke((Delegate)(Func<string>)(() => GamerServiceStatus = LocalizationService.Instance["StatusBlocked"]), Array.Empty<object>());
						}
						GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("DashboardOptimizationBlocked"));
						await Task.Delay(3000);
						bool active = (App.Services?.GetService<IGamerModeOrchestrator>())?.Status.IsActive ?? false;
						Application current3 = Application.Current;
						if (current3 != null)
						{
							((DispatcherObject)current3).Dispatcher.BeginInvoke((Delegate)(Func<string>)(() => GamerServiceStatus = (active ? LocalizationService.Instance["StatusActivated"] : LocalizationService.Instance["StatusReady"])), Array.Empty<object>());
						}
						return;
					}
					case IntelligentProfileType.WorkOffice:
						_logger?.LogInfo("[" + origin2 + ".QuickOptimize] " + string.Format(LocalizationService.Instance.GetString("OptimizationConservative"), currentProfile));
						break;
					default:
						_logger?.LogInfo("[" + origin2 + ".QuickOptimize] " + string.Format(LocalizationService.Instance.GetString("OptimizationFullAuthorized"), currentProfile));
						break;
					}
					if (App.PerformanceOptimizer != null)
					{
						cancellationToken.ThrowIfCancellationRequested();
						OperationResult ramResult = await App.PerformanceOptimizer.OptimizeRAMAsync(delegate(int p)
						{
							int percentage = 30 + (int)((double)p * 0.3);
							GlobalProgressService.Instance.UpdateProgress(percentage, LocalizationService.Instance.GetString("Loc_CleaningRAM"));
						}).ConfigureAwait(continueOnCapturedContext: false);
						if (!ramResult.Success)
						{
							_logger?.LogWarning("[" + origin2 + ".QuickOptimize] Falha na otimização de RAM");
						}
						GlobalProgressService.Instance.UpdateProgress(70, LocalizationService.Instance.GetString("DashboardOptimizingProcesses"));
						GlobalProgressService.Instance.UpdateProgress(90, LocalizationService.Instance.GetString("DashboardFinishing"));
						if (ramResult.Success)
						{
							string successMsg = LocalizationService.Instance.GetString("CleanupOptimizationSuccessPT");
							string titleMsg = LocalizationService.Instance.GetString("DashboardOptimizationCompleted");
							_logger?.LogSuccess($"[{origin2}.QuickOptimize] {successMsg} ({LocalizationService.Instance.GetString("OptimizationConservative").Replace("{0}", currentProfile.ToString())})");
							GlobalNotificationService.ShowSuccess(titleMsg, successMsg);
						}
						else
						{
							_logger?.LogWarning("[" + origin2 + ".QuickOptimize] Otimização parcial: " + ramResult.ErrorMessage);
						}
						App.TelemetryService?.TrackEvent("QUICK_OPTIMIZE", origin2, "System");
					}
					else
					{
						_logger?.LogWarning("[" + origin2 + ".QuickOptimize] Serviço de otimização de performance não disponível.");
					}
					cancellationToken.ThrowIfCancellationRequested();
					GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("DashboardOptimizationCompleted"));
					App.TelemetryService?.TrackEvent("DASHBOARD_OPTIMIZE", "QuickOptimize", origin2);
					Application current4 = Application.Current;
					if (current4 != null)
					{
						((DispatcherObject)current4).Dispatcher.BeginInvoke((Delegate)(Func<string>)(() => GamerServiceStatus = LocalizationService.Instance["StatusOptimized"]), Array.Empty<object>());
					}
					HistoryService historyService = HistoryService.Instance;
					await Task.Run(delegate
					{
						historyService.RecordOptimization(DateTime.Now);
					}).ConfigureAwait(continueOnCapturedContext: false);
					await UpdateLastOptimizationInfo().ConfigureAwait(continueOnCapturedContext: false);
					await Task.Delay(3000);
					Application current5 = Application.Current;
					if (current5 != null)
					{
						((DispatcherObject)current5).Dispatcher.BeginInvoke((Delegate)(Action)delegate
						{
							GamerServiceStatus = LocalizationService.Instance["StatusWaiting"];
						}, Array.Empty<object>());
					}
				}
				catch (OperationCanceledException)
				{
					_logger?.LogInfo("[" + origin2 + ".QuickOptimize] Otimização cancelada pelo usuário");
					throw;
				}
				catch (Exception ex3)
				{
					Exception ex = ex3;
					_logger?.LogError("[" + origin2 + ".QuickOptimize] " + LocalizationService.Instance.GetString("OptimizationErrorDetail").Replace("{0}", ex.Message), ex);
					GlobalNotificationService.ShowError(LocalizationService.Instance.GetString("DashboardQuickOptimize"), LocalizationService.Instance.GetString("Error") + ": " + ex.Message);
				}
				finally
				{
					if (_benchmarkService != null)
					{
						long elapsedMs = benchTimer?.ElapsedMilliseconds ?? 0;
						_benchmarkService.EndOptimization(elapsedMs);
						int score = _benchmarkService.TotalScore;
						Application current6 = Application.Current;
						if (current6 != null)
						{
							((DispatcherObject)current6).Dispatcher.BeginInvoke((Delegate)(Func<int>)(() => PerformanceScore = score), Array.Empty<object>());
						}
						_logger?.LogInfo($"[BENCHMARK] Score pós-otimização: {score}/100");
					}
					DiagLog($"[DASHBOARD_VM][TID:{Thread.CurrentThread.ManagedThreadId}] QuickOptimizeAsync FINALIZADO");
				}
			});
		}).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task QuickCleanupAsync(string origin = "Dashboard")
	{
		string origin2 = origin;
		int tid = Thread.CurrentThread.ManagedThreadId;
		DiagLog($"[DASHBOARD_VM][TID:{tid}] QuickCleanupAsync INÍCIO (Origin: {origin2})");
		if (!(await _quickCleanupLock.WaitAsync(TimeSpan.FromSeconds(10.0)).ConfigureAwait(continueOnCapturedContext: false)))
		{
			DiagLog($"[DASHBOARD_VM][TID:{tid}] QuickCleanupAsync TIMEOUT — lock não adquirido em 10s, ignorando");
			_logger?.LogWarning("[Dashboard] QuickCleanup ignorado: lock não adquirido em 10s (origin: " + origin2 + ")");
			return;
		}
		DiagLog($"[DASHBOARD_VM][TID:{tid}] QuickCleanupAsync Adquiriu Lock");
		try
		{
			await Task.Run(async delegate
			{
				await ExecuteWithLicenseCheckAsync("cleanup", async delegate
				{
					try
					{
						GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("DashboardQuickCleanup"), isPriority: true);
						RunOnUiThread(delegate
						{
							CleanupServiceStatus = LocalizationService.Instance.GetString("Loc_CleaningNormal");
							CleanupServiceColor = "#8B31FF";
						});
						try
						{
							if (App.SystemCleaner != null)
							{
								GlobalProgressService.Instance.UpdateProgress(20, LocalizationService.Instance.GetString("Loc_CleaningTempFiles"));
								long tempCleaned = await App.SystemCleaner.CleanTempFilesAsync();
								GlobalProgressService.Instance.UpdateProgress(40, LocalizationService.Instance.GetString("CleanupBtnEmptyRecycle"));
								await App.SystemCleaner.EmptyRecycleBinAsync();
								GlobalProgressService.Instance.UpdateProgress(60, LocalizationService.Instance.GetString("Loc_CleaningThumbnails"));
								long thumbCleaned = await App.SystemCleaner.CleanThumbnailsAsync();
								GlobalProgressService.Instance.UpdateProgress(80, LocalizationService.Instance.GetString("Loc_CleaningBrowserCache"));
								long totalCleaned = tempCleaned + thumbCleaned + await App.SystemCleaner.CleanBrowserCacheAsync();
								string bytesFormatted = FileSystemHelper.FormatBytes(totalCleaned);
								GlobalProgressService.Instance.UpdateProgress(90, LocalizationService.Instance.GetString("DashboardFinishingCleanup"));
								_logger?.LogSuccess($"[{origin2}.QuickCleanup] {LocalizationService.Instance.GetString("CleanupComplete")}: {bytesFormatted} {LocalizationService.Instance.GetString("CleanupBytesFreed").Replace("{0}", bytesFormatted)}");
								GlobalNotificationService.ShowSuccess(message: LocalizationService.Instance.GetString("DashboardCleanupSuccessMessage").Replace("{0}", bytesFormatted), title: LocalizationService.Instance.GetString("DashboardCleanupComplete"));
								App.TelemetryService?.TrackEvent("QUICK_CLEANUP", origin2, "Disk");
							}
							else
							{
								_logger?.LogWarning("[" + origin2 + ".QuickCleanup] " + LocalizationService.Instance.GetString("CleanupServiceNotAvailable"));
							}
						}
						catch (Exception ex)
						{
							_logger?.LogError($"[{origin2}.QuickCleanup] {LocalizationService.Instance.GetString("CleanupError")}: {ex.Message}", ex);
							GlobalNotificationService.ShowError(LocalizationService.Instance.GetString("DashboardQuickCleanup"), LocalizationService.Instance.GetString("DashboardCleanupError") + ": " + ex.Message);
						}
						GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("DashboardCleanupComplete"));
						App.TelemetryService?.TrackEvent("DASHBOARD_CLEANUP", "QuickCleanup", origin2);
						RunOnUiThread(delegate
						{
							CleanupServiceStatus = LocalizationService.Instance.GetString("StatusClean");
							CleanupServiceColor = "#10B981";
						});
						await Task.Delay(3000);
						Application current = Application.Current;
						if (current != null)
						{
							((DispatcherObject)current).Dispatcher.BeginInvoke((Delegate)(Action)delegate
							{
								CleanupServiceStatus = LocalizationService.Instance["StatusWaiting"];
								CleanupServiceColor = "#F59E0B";
							}, Array.Empty<object>());
						}
					}
					finally
					{
						DiagLog($"[DASHBOARD_VM][TID:{Thread.CurrentThread.ManagedThreadId}] QuickCleanupAsync FINALIZADO");
					}
				});
			}).ConfigureAwait(continueOnCapturedContext: false);
		}
		finally
		{
			_quickCleanupLock.Release();
		}
	}

	public async Task MasterActionAsync(string origin = "CircularButton")
	{
		string origin2 = origin;
		int tid = Thread.CurrentThread.ManagedThreadId;
		DiagLog($"[DASHBOARD_VM][TID:{tid}] MasterActionAsync INÍCIO (origin: {origin2})");
		if (IsMasterRunning)
		{
			DiagLog($"[DASHBOARD_VM][TID:{tid}] MasterActionAsync já em execução — ignorando");
			return;
		}
		if (!(await _masterLock.WaitAsync(TimeSpan.FromSeconds(10.0)).ConfigureAwait(continueOnCapturedContext: false)))
		{
			_logger?.LogWarning("[" + origin2 + ".MasterAction] Lock de otimização não adquirido em 10s, ignorando");
			return;
		}
		if (IsMasterRunning)
		{
			_masterLock.Release();
			DiagLog($"[DASHBOARD_VM][TID:{tid}] MasterActionAsync já em execução (detectado pós-lock) — ignorando");
			return;
		}
		if (!(await _quickOptimizeLock.WaitAsync(TimeSpan.FromSeconds(30.0)).ConfigureAwait(continueOnCapturedContext: false)))
		{
			_masterLock.Release();
			_logger?.LogWarning("[" + origin2 + ".MasterAction] Otimização rápida ainda em execução, ação mestre ignorada");
			return;
		}
		RunOnUiThread(delegate
		{
			IsMasterRunning = true;
		});
		RunOnUiThread(delegate
		{
			IsCancelling = false;
		});
		SetMasterPhaseSafe(1);
		RunOnUiThread(delegate
		{
			MasterFreedBytes = 0L;
		});
		SetMasterClockRunning(running: true);
		// Nova execução: o piso da barra volta a zero, senão a nova execução
		// começaria no valor máximo da anterior.
		ResetMasterProgressFloor();
		MasterRamReclaimedBytes = 0L;
		MasterRamReclaimedDisplay = string.Empty;
		try
		{
			_masterCts?.Dispose();
		}
		catch
		{
		}
		_masterCts = new CancellationTokenSource();
		CancellationToken token = _masterCts.Token;

		// =================================================================
		// PRIMEIRA OTIMIZAÇÃO (pacote que morava na página "Relatório DNA")
		//
		// Roda UMA ÚNICA VEZ e FORA do gate de licença: o requisito é que
		// estas otimizações sejam aplicadas para qualquer usuário. As páginas
		// e botões que exigem PRO continuam exigindo.
		//
		// A medição inicial é feita aqui, antes de qualquer coisa, para que o
		// score final considere a corrida INTEIRA (DNA + limpeza + otimização).
		// =================================================================
		Core.SystemIntelligenceProfiler.FirstRunOptimizationService firstRunService = null;
		Core.SystemIntelligenceProfiler.FirstRunOptimizationResult firstRunResult = null;
		Core.SystemIntelligenceProfiler.FirstRunOptimizationService.SystemSnapshot runStartSnapshot = null;
		bool isFirstEverRun = false;
		long realFreedBytes = 0L;

		// =================================================================
		// BARRA DE PROGRESSO GLOBAL (rodapé)
		//
		// A barra do rodapé é pintada por ProgressTrackingService, e não pelo
		// GlobalProgressService. O botão circular só usava o segundo — por isso
		// as etapas (análise/limpeza/otimização) NÃO apareciam na barra.
		// Aqui a corrida é registrada como operação com sub-etapas nomeadas,
		// e cada etapa reporta seu próprio texto e percentual.
		// =================================================================
		Guid progressOpId = Guid.Empty;
		// [FIX:STAGE-ELLIPSIS] Os identificadores das etapas passam a ser os
		// textos DE STATUS (com os três pontinhos), e não os nomes limpos.
		//
		// O rodapé exibe `CurrentSubTask.Name` diretamente, então o que é
		// registrado aqui É o que o usuário lê. Antes era "Analisando" sem
		// pontinhos; agora é "Analisando..." nos três idiomas.
		//
		// Não quebra o rastreamento das etapas porque `RegisterSubTasks`,
		// `StartSubTask`, `UpdateSubTaskProgress` e `CompleteSubTask` usam
		// sempre as MESMAS variáveis — o casamento é por igualdade de string,
		// então o texto pode ser o que for.
		//
		// O botão circular continua usando os nomes limpos (MasterAnalyzing) na
		// composição "Etapa + item...", que precisa ser uma frase só.
		string stageDna = LocalizationService.Instance.GetString("MasterDnaStatus");
		string stageAnalyze = LocalizationService.Instance.GetString("MasterAnalyzingStatus");
		string stageCleanup = LocalizationService.Instance.GetString("MasterCleaningStatus");
		string stageOptimize = LocalizationService.Instance.GetString("MasterOptimizingStatus");

		// ─────────────────────────────────────────────────────────────
		// ORDEM CORRIGIDA: o flag de primeira execução precisa ser
		// determinado ANTES de montar a barra de progresso.
		//
		// BUG: a barra era configurada lendo `isFirstEverRun` aqui, mas o
		// flag só era atribuído ~28 linhas abaixo, dentro do try seguinte.
		// Na primeira execução o `if` portanto via `false` e registrava
		// apenas 3 etapas (sem `stageDna`); em seguida o pacote do perfil
		// era executado mesmo assim, e o
		//   ProgressTrackingService.CompleteSubTask(op.Id, stageDna)
		// não encontrava a etapa (FirstOrDefault => null) e saía em
		// silêncio. Resultado: trabalho executado sem_move_a_barra.
		// ─────────────────────────────────────────────────────────────
		try
		{
			firstRunService = new Core.SystemIntelligenceProfiler.FirstRunOptimizationService(_logger);
			if (!firstRunService.AlreadyApplied())
			{
				isFirstEverRun = true;
				runStartSnapshot = Core.SystemIntelligenceProfiler.FirstRunOptimizationService.Measure();
				_logger?.LogInfo("[" + origin2 + ".MasterAction] PRIMEIRA otimização detectada — aplicando o pacote do perfil inteligente");
			}
			else
			{
				_logger?.LogInfo("[" + origin2 + ".MasterAction] Otimização já aplicada anteriormente — regime enxuto (limpeza + otimização rápida)");
			}
		}
		catch (Exception ex)
		{
			_logger?.LogWarning("[" + origin2 + ".MasterAction] Falha ao verificar estado da primeira otimização: " + ex.Message);
		}

		try
		{
			var tracker = ProgressTrackingService.Instance;
			var op = tracker.StartOperation(LocalizationService.Instance.GetString("DashboardOptimizationInProgress"));
			progressOpId = op.Id;

			// [FIX:MASTER-SINGLE-SOURCE] O botão circular passa a ler o MESMO
			// número que o rodapé lê.
			//
			// O PROBLEMA QUE ISTO RESOLVE
			// --------------------------
			// A % do círculo e a % do rodapé eram dois cálculos independentes, com
			// mapas diferentes:
			//   círculo  = faixas fixas por etapa (análise 32..47, limpeza 50..80,
			//              otimização 82..100)
			//   rodapé   = média PONDERADA das etapas (25/20/40/15 ou 25/55/20)
			// Numa execução real isso significava o círculo já em 32% enquanto o
			// rodapé ainda estava em 0% no começo da análise, e os dois se
			// invertendo depois. Duas barras de progresso discordando na mesma
			// tela é pior do que uma barra imprecisa: o usuário não sabe qual
			// acreditar.
			//
			// A CORREÇÃO
			// -----------
			// Assinar o evento de progresso e copiar o GlobalPercentage. Uma única
			// fonte, então os dois não podem divergir por construção — e se um
			// dia os pesos mudarem, os dois mudam junto sem nenhum ajuste em dois
			// lugares.
			//
			// A assinatura é restrita a esta operação: criada no início e removida
			// no `finally`, para não vazar handler a cada clique nem reagir a
			// operações de outras telas.
			_masterProgressHandler = delegate (object? sender, HierarchicalProgressEventArgs e)
			{
				try
				{
					// `propagateToGlobal: false` — este caminho JÁ É a barra global.
					// Repassar aqui criaria um laço de realimentação entre os dois.
					UpdateMasterProgress(e.GlobalPercentage, string.Empty, propagateToGlobal: false);
				}
				catch { }
			};
			tracker.ProgressUpdated += _masterProgressHandler;
			_logger?.LogInfo($"[MasterProgress] Assinado ProgressTrackingService.ProgressUpdated | opId={progressOpId}");

			if (isFirstEverRun)
			{
				tracker.RegisterSubTasks(op.Id,
					(stageDna, 25),
					(stageAnalyze, 20),
					(stageCleanup, 40),
					(stageOptimize, 15));
				tracker.StartSubTask(op.Id, stageDna);
			}
			else
			{
				tracker.RegisterSubTasks(op.Id,
					(stageAnalyze, 25),
					(stageCleanup, 55),
					(stageOptimize, 20));
				tracker.StartSubTask(op.Id, stageAnalyze);
			}
		}
		catch (Exception ex)
		{
			_logger?.LogWarning("[" + origin2 + ".MasterAction] Falha ao iniciar a barra de progresso global: " + ex.Message);
		}

		try
		{
			await Task.Run(async delegate
			{
				// ---------- ETAPA 1: pacote do perfil inteligente (1a vez only) ----------
					if (isFirstEverRun && firstRunService != null)
					{
						try
						{
							// [FIX:PROGRESS-PCT] Ticker por tempo da etapa DNA.
							// Estimativa de 55s vinda da medição de 2026-09-28 (a
							// etapa levou 54s). Define só a INCLINAÇÃO: se demorar
							// mais, a % segura em 95% até o fim de verdade.
							dnaTicker = new StageProgressTicker(55_000, pct =>
							{
								try
								{
									ProgressTrackingService.Instance.UpdateSubTaskProgress(
										progressOpId, stageDna, pct);
								}
								catch { }
							});

							// [FIX:PROGRESS-PCT] A % da DNA era um contador em PASSOS
							// FIXOS (+4 até o teto de 30), e a sub-etapa é
							// `captured * 100 / 30`. Ou seja: bastavam ~7 reports
							// para a sub-etapa marcar 100%, enquanto a RunOnceAsync
							// continuava trabalhando por quase um minuto.
							//
							// Efeito observável: o rodapé passava a operação INTEIRA
							// mostrando "- DNA (100%)" (confirmado no log de
							// 2026-09-28: a etapa DNA rodou de 14:32:54 a 14:33:48,
							// 54 segundos). O usuário via 100% enquanto ainda havia
							// quase tudo pela frente — que é exatamente a
							// "porcentagem travada em 100%" relatada.
							//
							// A % agora é derivada do TEMPO decorrido em relação a uma
							// estimativa, com teto em 95%. Não é perfeita — sem
							// informação do total de itens não dá para ser exata — mas
							// é HONESTA: sobe devagar, não satura, e só chega a 100%
							// quando a etapa realmente termina (por CompleteSubTask).
							// O 5% final é reservado de propósito: a barra global
							// ponderada ainda tem a etapa de otimização à frente, e
							// mostrar 100% cedo ali seria mentira.
							GlobalProgressService.Instance.StartOperation(
								LocalizationService.Instance.GetString("ProfilerAnalyzingRecommendations"), isPriority: true);
							// [FIX:MASTER-30PCT] Texto apenas. A % é do evento
							// ProgressUpdated (fonte única com o rodapé).
							UpdateMasterStatusTextOnly(LocalizationService.Instance.GetString("ProfilerAnalyzingRecommendations"));

							// Os textos traduzidos da página antiga alimentam a barra de
							// progresso global, junto com os textos de limpeza/otimização.
							var progressAdapter = new Progress<string>(delegate(string text)
							{
								if (!string.IsNullOrWhiteSpace(text))
								{
									// [FIX:PROGRESS-ITEM] O texto da etapa DNA também vai
									// para o rodapé.
									ProgressTrackingService.Instance.SetOperationDetail(progressOpId, text);

									// [FIX:PROGRESS-PCT] % da sub-etapa por TEMPO decorrido,
									// teto de 95. O caminho antigo somava +4 por report até
									// 30 e converteva para a sub-etapa, o que a saturava
									// em 100% nos primeiros segundos: a DNA levou 54s e
									// o rodapé mostrou "DNA (100%)" quase tudo isso.
									//
									// O `dnaStep`/`captured` saíram daqui junto: existiam
									// só para desenhar no botão circular, e a % do
									// círculo agora vem do ProgressUpdated — o mesmo
									// número do rodapé. Mantê-los seria código morto.
									try
									{
										ProgressTrackingService.Instance.UpdateSubTaskProgress(
											progressOpId, stageDna, dnaTicker.CurrentPercentage());
									}
									catch { }

									UpdateMasterStatusTextOnly(text);
								}
							});


						firstRunResult = await firstRunService.RunOnceAsync(progressAdapter, token).ConfigureAwait(continueOnCapturedContext: false);

						try
						{
							ProgressTrackingService.Instance.CompleteSubTask(progressOpId, stageDna);
							ProgressTrackingService.Instance.StartSubTask(progressOpId, stageAnalyze);
							GlobalProgressService.Instance.CompleteOperation(
								LocalizationService.Instance.GetString("ProfilerCalibrationCompleted"));
						}
						catch { }
						UpdateMasterStatusTextOnly(LocalizationService.Instance.GetString("ProfilerCalibrationCompleted"));

						if (firstRunResult != null)
						{
							_logger?.LogSuccess($"[" + origin2 + ".MasterAction] Pacote do perfil aplicado e VERIFICADO: {firstRunResult.VerifiedCount} confirmada(s), {firstRunResult.NotVerified.Count} não confirmada(s), puladas: {firstRunResult.Skipped.Count}");
							foreach (var s in firstRunResult.Skipped)
							{
								_logger?.LogInfo("[" + origin2 + ".MasterAction] Pulado por segurança: " + s);
							}
						}
					}
					catch (OperationCanceledException)
					{
						throw;
					}
					catch (Exception ex6)
					{
						_logger?.LogWarning("[" + origin2 + ".MasterAction] Falha no pacote do perfil (continuando com limpeza): " + ex6.Message);
					}
				}

				await ExecuteWithLicenseCheckAsync("master", async delegate
				{
					_logger?.LogInfo("[" + origin2 + ".MasterAction] LICENSEGUARD APROVADO — Iniciando otimização completa (limpeza + otimização)");
					try
					{
					GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("MasterDeepClean"), isPriority: true);
					// [FIX:MASTER-30PCT] Este era o "começa já em 30%".
					//
					// A linha empurrava 32% no PRIMEIRO INSTANTE da operação. Como a
					// % do círculo agora vem do ProgressTrackingService — e ele
					// começa em 0% — o piso monotônico segurava os 32% e o evento
					// não conseguia mais baixar. O botão abria em ~30% antes de
					// qualquer trabalho acontecer.
					//
					// A % pertence ao evento; aqui só entra o texto da etapa.
					UpdateMasterStatusTextOnly(LocalizationService.Instance.GetString("MasterAnalyzing"));
					token.ThrowIfCancellationRequested();


						// A limpeza profunda deixou de ser um checkbox na página de
						// perfil e virou ETAPA do botão circular. Mas continua
						// respeitando a escolha: se o usuário desmarcou
						// "CleanSystem", a varredura e a limpeza sao puladas e nada
						// é apagado. A análise também é pulada — varrer o disco
						// inteiro só para não limpar nada não faz sentido.
						bool wantsDeepClean = true;
						try
						{
							wantsDeepClean = new Core.SystemIntelligenceProfiler.ProfileStore().Load().Answers.CleanSystem;
						}
						catch { wantsDeepClean = true; }

						if (!wantsDeepClean)
						{
						_logger?.LogInfo("[" + origin2 + ".MasterAction] Limpeza profunda NAO solicitada — etapa pulada");
						UpdateMasterStatusTextOnly(LocalizationService.Instance.GetString("MasterCleanSkipped"));

						}
					else if (App.UltraCleaner != null)
					{
					// [FIX:PROGRESS-PCT] Tickers por tempo para análise e limpeza.
					// Estimativas vindas da medição de 2026-09-28 (análise 13s,
					// limpeza 50s). Servem só para a INCLINAÇÃO da curva: se a
					// máquina for mais lenta, a % segura em 95% até a etapa
					// terminar de verdade — crawl honesto em vez de 100% mentiroso.
					analyzeTicker = new StageProgressTicker(13_000, pct =>
					{
						try
						{
							ProgressTrackingService.Instance.UpdateSubTaskProgress(
								progressOpId, stageAnalyze, pct);
						}
						catch { }
					});

					UltraCleanAnalysis analysis = await App.UltraCleaner.AnalyzeAllAsync(new Progress<AnalysisProgress>(delegate(AnalysisProgress p)

						{
							int percentage2 = 32 + (int)((double)p.PercentComplete * 0.15);
UpdateMasterStatusTextOnly(LocalizationService.Instance.GetString("MasterAnalyzing") + " " + p.CurrentItem + "...");
							try
							{
								ProgressTrackingService.Instance.UpdateSubTaskProgress(
									progressOpId, stageAnalyze, analyzeTicker?.CurrentPercentage() ?? 0);

								// [FIX:PROGRESS-ITEM] "o que está sendo analisado"
								ProgressTrackingService.Instance.SetOperationDetail(
									progressOpId, p.CurrentItem);
							}
							catch { }
						}), null, token).ConfigureAwait(continueOnCapturedContext: false);

						analyzeTicker?.Dispose();
						analyzeTicker = null;

						token.ThrowIfCancellationRequested();
						try
						{
							ProgressTrackingService.Instance.CompleteSubTask(progressOpId, stageAnalyze);
							ProgressTrackingService.Instance.StartSubTask(progressOpId, stageCleanup);

							// [FIX:PROGRESS-ITEM] ZERA o detalhe ao TROCAR de etapa.
							// Sem isto o rodapé continuaria mostrando o último item
							// da etapa anterior: foi o que fez a fase "Otimizando"
							// aparecer com "Cache do NuGet" (último item da
							// limpeza) e, por consequência, com o ÍCONE DE
							// LIMPEZA em vez do de otimização.
							ProgressTrackingService.Instance.SetOperationDetail(progressOpId, string.Empty);
						}
						catch { }

							List<ItemAnalysis> allItems = (from i in analysis?.Categories.SelectMany((CategoryAnalysis c) => c.Items)
								where i.IsSafe
								select i).ToList();
							if (allItems != null && allItems.Count > 0)
							{
							_logger?.LogInfo($"[{origin2}.MasterAction] {allItems.Count} itens autorizados encontrados na limpeza profunda");
							SetMasterPhaseSafe(2);

							cleanupTicker = new StageProgressTicker(50_000, pct =>
							{
								try
								{
									ProgressTrackingService.Instance.UpdateSubTaskProgress(
										progressOpId, stageCleanup, pct);
								}
								catch { }
							});

							CleanupResult cleanResult = await App.UltraCleaner.CleanSelectedAsync(allItems, new Progress<CleanupProgress>(delegate(CleanupProgress p)
							{
								// Faixa 50..80: a limpeza ocupa o miolo do fluxo.
								// A faixa 76..85 que existia antes era da etapa de
								// REDE, removida do botão circular (ver nota abaixo):
								// agora a limpeza vai até 80 e a otimização assume
								// 80..100, sem buraco e sem retrocesso.
							int percentage = 50 + (int)((double)p.PercentComplete * 0.30);
							SetMasterFreedBytesSafe(p.SpaceCleanedSoFar);
UpdateMasterStatusTextOnly(LocalizationService.Instance.GetString("MasterCleaning") + " " + p.CurrentItem + "...");
							try
							{
								// [FIX:PROGRESS-PCT] NÃO usa mais o
								// `p.PercentComplete` da limpeza. Ele marca 100% em
								// ~2,3s e o trabalho real leva ~50s (medido em
								// 2026-09-28 15:38) — o item final sozinho consumiu
								// 47s. O sub-tasked % agora vem do ticker por tempo,
								// com teto em 95%.
								ProgressTrackingService.Instance.UpdateSubTaskProgress(
									progressOpId, stageCleanup, cleanupTicker?.CurrentPercentage() ?? 0);

								// [FIX:PROGRESS-ITEM] É o item que faltava: o rodapé
								// passava a mostrar "- Limpando (54%)" e o usuário não
								// via o que estava sendo limpo. Este é o texto que
								// também faz o rodapé trocar o ícone de raio pelo de
								// limpeza, porque o seletor casa "cache"/"temporários"/
								// "rede" aqui.
								ProgressTrackingService.Instance.SetOperationDetail(
									progressOpId, p.CurrentItem);
							}
							catch { }

							}), token).ConfigureAwait(continueOnCapturedContext: false);

							cleanupTicker?.Dispose();
							cleanupTicker = null;

							_logger?.LogSuccess($"[{origin2}.MasterAction] Limpeza profunda concluída: {cleanResult.SpaceCleaned.ToString("N0")} bytes (itens: {cleanResult.ItemsCleaned})");


								// Total REAL liberado. É este número que vai aparecer
								// dentro do círculo ao final, junto da comemoração.
								// Vem do CleanupResult, não de estimativa.
								Interlocked.Add(ref realFreedBytes, cleanResult.SpaceCleaned);
								MasterLastFreedBytes = realFreedBytes;
								SetMasterFreedBytesSafe(realFreedBytes);

								if (isFirstEverRun)
								{
									runStartSnapshot ??= Core.SystemIntelligenceProfiler.FirstRunOptimizationService.Measure();
									runStartSnapshot.FreeSpaceBytes -= cleanResult.SpaceCleaned;
								}
							}
							else
							{
								_logger?.LogWarning("[" + origin2 + ".MasterAction] Nenhum item seguro encontrado para limpeza profunda");
							}
						}
						else
						{
							_logger?.LogWarning("[" + origin2 + ".MasterAction] " + LocalizationService.Instance.GetString("DashboardOptimizationServiceNotAvailable"));
						}
					}
					catch (OperationCanceledException)
					{
						throw;
					}
					catch (Exception ex5)
					{
						Exception deepEx = ex5;
						_logger?.LogWarning("[" + origin2 + ".MasterAction] Falha na limpeza profunda (continuando com otimização): " + deepEx.Message);
					}
					finally
					{
						// [FIX:END-STATE] NÃO publica mais conclusão aqui.
						//
						// Este era o "mostra concluída mas continua otimizando": o
						// rodapé recebia "Limpeza profunda concluída" no instante
						// em que a limpeza acabava, mas a etapa OTIMIZAR ainda ia
						// rodar ~12s depois. O usuário lia "concluída" como fim da
						// operação inteira, e o rodapé depois ficava vazio — os
						// dois defeitos juntos.
						//
						// A limpeza é uma ETAPA, não a operação. Quem anuncia o
						// fim é a ProgressTrackingService, uma vez, no fim real —
						// e com a mensagem de sucesso traduzida. Aqui só se
						// registra no log, que não é visível na interface.
						_logger?.LogInfo(
							"[" + origin2 + ".MasterAction] Etapa de limpeza profunda encerrada; " +
							"a operação do botão circular ainda está em andamento.");
					}
					token.ThrowIfCancellationRequested();


					// ─── ETAPA DE REDE REMOVIDA DO BOTÃO CIRCULAR ───────────────
					// Ela executava `netsh winsock reset` + `netsh int ip reset` +
					// `ipconfig /renew` a CADA clique, condicionada apenas por
					// `Answers.ResetNetwork` — flag com default `true` cujo
					// checkbox já não existe na tela, então o usuário não tinha
					// como desligar.
					//
					// Dois problemas:
					//  1. SEGURANÇA: reset de pilha DERRUBA todas as conexões de
					//     rede ativas, incluindo jogo online. Não há como o botão
					//     fazer isso de forma segura por_accidente.
					//  2. INEFICAZ: o proprio DefaultCompatibilityPolicy
					//     (linha 55) barra `Network_ResetStack` delegando a
					//     decisão a quem chama — e o botão nunca a implementou.
					//     Resultado no log: "Reset da pilha nao confirmado:
					//     blocked", todo clique, gastando 76..85% da barra.
					//
					// O reset de rede continua disponível, com toda a atenção que
					// merece, na página de REDE do sidebar.
				SetMasterPhaseSafe(3);
				UpdateMasterStatusTextOnly(LocalizationService.Instance.GetString("MasterOptimizing"));
				try
				{
					ProgressTrackingService.Instance.CompleteSubTask(progressOpId, stageCleanup);
					ProgressTrackingService.Instance.StartSubTask(progressOpId, stageOptimize);

					// [FIX:PROGRESS-ITEM] Zera o detalhe da limpeza. Era o que
					// deixava a fase "Otimizando" exibindo "Cache do NuGet" (o
					// último item da limpeza) e exibindo o ÍCONE DE LIMPEZA.
					ProgressTrackingService.Instance.SetOperationDetail(progressOpId, string.Empty);
				}
				catch { }

				// [FIX:PROGRESS-PCT] A etapa de otimização ficava SEM NENHUM
				// reporte: entre o StartSubTask acima e o `... , 100)` do fim,
				// não havia uma única chamada de UpdateSubTaskProgress. A barra
				// ficava parada na % que a limpeza entregou durante toda a
				// otimização (8 segundos na medição de 2026-09-28), e a sub-etapa
				// aparecia como "Otimizando (0%)" — número que não se movia e
				// contradizia a barra.
				//
				// Não há granularidade real para reportar aqui: o serviço de
				// otimização é um await sem callback. O que dá para fazer — e é
				// honesto — é advancing a sub-etapa pelo tempo decorrido com teto
				// em 95%, reserving os 5% finais para o CompleteSubTask de
				// verdade. A barra volta a se mover e nunca mostra 100% antes de
				// a etapa acabar.
				var optimizeWatch = Stopwatch.StartNew();
				const int optimizeExpectedMs = 10_000;

				// [FIX:PROGRESS-PCT] O timer PRECISA ser construído no Dispatcher da
				// UI. `new DispatcherTimer()` sem argumento usa
				// Dispatcher.CurrentDispatcher, que nesta thread (dentro do
				// Task.Run) é um dispatcher novo que NUNCA roda — o Tick não
				// dispararia uma única vez e a barra voltaria a ficar parada,
				// com todo o ar de estar consertada. Criar com o Dispatcher da UI
				// é o que garante que ele efetivamente conta.
				var uiDispatcher = Application.Current?.Dispatcher
					?? System.Windows.Threading.Dispatcher.CurrentDispatcher;

				var optimizeTicker = new DispatcherTimer(DispatcherPriority.Background, uiDispatcher)
				{
					Interval = TimeSpan.FromMilliseconds(400)
				};
				optimizeTicker.Tick += delegate
				{
					try
					{
						int pct = (int)(optimizeWatch.ElapsedMilliseconds * 95L / optimizeExpectedMs);
						if (pct > 95) pct = 95;
						if (pct < 0) pct = 0;

						ProgressTrackingService.Instance.UpdateSubTaskProgress(
							progressOpId, stageOptimize, pct);

						// [FIX:MASTER-SINGLE-SOURCE] A % do botão circular NÃO é
						// calculada aqui. Ela vem do evento ProgressUpdated, o
						// mesmo número do rodapé. Este ticker cuida só de
						// manter a etapa de otimização se movendo — sem ele a
						// sub-etapa ficava parada, e sem isso a barra parava.
						UpdateMasterStatusTextOnly(LocalizationService.Instance.GetString("MasterOptimizing"));
					}

					catch { }
				};
				optimizeTicker.Start();

				try
				{
					await RunQuickOptimizeCoreAsync(origin2, token).ConfigureAwait(continueOnCapturedContext: false);
				}
				finally
				{
					// O ticker é parado em TODOS os caminhos. Sem isto ele
					// continuaria publicando progresso de uma etapa já
					// concluída, e o UpdateSubTaskProgress ignoraria por causa da
					// trava de regressão — mas o botão circular continuaria
					// sendo empurrado para baixo depois da conclusão.
					optimizeTicker.Stop();
				}
				token.ThrowIfCancellationRequested();


					// ── Memória devolvida, mostrada em LINHA PRÓPRIA ──────────
					// Não é somada ao espaço em disco: bytes de RAM e bytes de
					// disco não se somam, e memória liberada não é espaço
					// permanente — o Windows realoca por demanda. Por isso vai
					// como informação separada, e só aparece se houve ganho
					// real (evita um "+0 MB" que só polui a comemoração).
					var ramSummary = VoltrisOptimizer.Services.Performance.MemoryReclaimService.LastRun;
					if (ramSummary != null && ramSummary.FreedMb >= 1.0)
					{
						long ramBytes = (long)(ramSummary.FreedMb * 1024L * 1024L);
						MasterRamReclaimedBytes = ramBytes;
						MasterRamReclaimedDisplay =
							LocalizationService.Instance.GetString("MasterRamReclaimed") + ": " + FileSystemHelper.FormatBytes(ramBytes);
						_logger?.LogSuccess(
							$"[{origin2}.MasterAction] Memória devolvida: {ramSummary.FreedMb:N0} MB " +
							$"(ModifiedPageList={ramSummary.ModifiedPageList.Ok}, FileCache={ramSummary.SystemFileCache.Ok}).");
					}
					else
					{
						MasterRamReclaimedBytes = 0L;
						MasterRamReclaimedDisplay = string.Empty;
					}

					SetMasterPhaseSafe(4);
					SetMasterPhaseSafe(5);

					// [FIX:END-STATE] Publica 100% no BOTÃO CIRCULAR, sem
					// repassar para o rodapé.
					//
					// `propagateToGlobal: true` mandava "Otimizado" (um
					// particípio, que não afirma que a operação acabou) para o
					// rodapé — competes com a ProgressTrackingService, que é quem
					// detém a mensagem de conclusão correta. O botão precisa do
					// 100% para o arco fechar; o rodapé não precisa deste texto.
					//
					// A operação ainda NÃO terminou aqui: falta o `finally`
					// concluir o tracker, e é dele que sai "Otimização concluída
					// com sucesso!".
					UpdateMasterProgress(100,
						LocalizationService.Instance.GetString("MasterOptimizationSuccess"),
						propagateToGlobal: false);
					try
					{
						ProgressTrackingService.Instance.UpdateSubTaskProgress(progressOpId, stageOptimize, 100);
						ProgressTrackingService.Instance.CompleteSubTask(progressOpId, stageOptimize);
					}
					catch { }
					_logger?.LogSuccess("[" + origin2 + ".MasterAction] Otimização completa finalizada com sucesso");
					App.TelemetryService?.TrackEvent("DASHBOARD_MASTER_OPTIMIZE", "Cleanup+Optimize", origin2);
				}).ConfigureAwait(continueOnCapturedContext: false);
			}).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (OperationCanceledException)
		{
			DiagLog($"[DASHBOARD_VM][TID:{tid}] MasterActionAsync CANCELADO pelo usuário");
			// [FIX:MASTER-CANCEL] Zera a barra de verdade. `force: true` derrete o
			// piso monotônico; sem ele, o cancelamento deixaria o arco e o número
			// parados no valor da última etapa.
			UpdateMasterProgress(0, LocalizationService.Instance.GetString("MasterCanceled"), force: true);
			_logger?.LogWarning(
				$"[MasterAction] CANCELADO pelo usuário | MasterProgress={MasterProgress} | " +
				$"piso={Interlocked.CompareExchange(ref _masterProgressFloor, 0, 0)}");
		}
		catch (Exception ex3)
		{
			Exception ex = ex3;
			_logger?.LogError("[" + origin2 + ".MasterAction] Erro fatal na otimização completa: " + ex.Message, ex);
			GlobalNotificationService.ShowError(LocalizationService.Instance.GetString("DashboardQuickOptimize"), LocalizationService.Instance.GetString("Error") + ": " + ex.Message);
		}
		finally
		{
			// [FIX:END-STATE] FECHA A OPERAÇÃO DO GlobalProgressService.
			//
			// VAZAMENTO REAL: o botão circular abre uma operação aqui
			// ("MasterDeepClean") e nada no caminho da operação a fechava. O
			// gerenciador mantinha uma tarefa "em andamento" para sempre, o que
			// quebrava DUAS coisas no rodapé:
			//
			//  1) `UpdateStatus` só arma o auto-ocultar quando
			//     `!IsOperationRunning` — então, com a operação fantasma, o
			//     rodapé nunca mais se auto-ocultava por esse caminho;
			//  2) qualquer checagem de "existe operação ativa" no rodapé ficava
			//     permanentemente verdadeira.
			//
			// Fechar aqui resolve os dois de uma vez, e o `finally` garante que
			// feche no sucesso, no cancelamento e na exceção.
			try
			{
				GlobalProgressService.Instance.CompleteOperation(
					LocalizationService.Instance.GetString("MasterDeepCleanComplete"));
				_logger?.LogInfo(
					$"[MasterAction] GlobalProgressService encerrado | " +
					$"IsOperationRunning={GlobalProgressService.Instance.IsOperationRunning}");
			}
			catch (Exception gpsEx)
			{
				_logger?.LogWarning("[MasterAction] Falha ao encerrar GlobalProgressService: " + gpsEx.Message);
			}

			// [FIX:PROGRESS-PCT] Descarta os tickers de etapa. Se um sobreviver à
			// operação, ele continuaria publicando progresso de uma etapa que já
			// acabou. O `Dispose` só para o timer, e o `null` garante que uma
			// execução seguinte não reaproveite um ticker velho.
			try { dnaTicker?.Dispose(); dnaTicker = null; } catch { }
			try { analyzeTicker?.Dispose(); analyzeTicker = null; } catch { }
			try { cleanupTicker?.Dispose(); cleanupTicker = null; } catch { }

			// [FIX:MASTER-SINGLE-SOURCE] Desassina ANTES de qualquer coisa. Se
			// ficasse assinado, ele continuaria empurrando a % do botão depois
			// do fim da operação, e o arco voltaria a aparecer sozinho.
			try
			{
				if (_masterProgressHandler != null)
				{
					ProgressTrackingService.Instance.ProgressUpdated -= _masterProgressHandler;
					_logger?.LogInfo("[MasterProgress] Handler do ProgressTrackingService desassinado.");
					_masterProgressHandler = null;
				}
			}
			catch { }

			// [FIX:FACE-MOOD] Devolve o rosto ao Brain ANTES de encerrar, e no
			// `finally` de propósito: só este bloco roda em TODOS os caminhos
			// (sucesso, cancelamento e exceção). Sem ele, o rosto ficaria travado
			// em Furious para sempre depois de uma limpeza interrompida.
			SetMasterFaceOverride(null);

			_logger?.LogInfo(
				$"[MasterAction] ENCERRADO | IsMasterRunning={IsMasterRunning} " +
				$"MasterProgress={MasterProgress} piso={Interlocked.CompareExchange(ref _masterProgressFloor, 0, 0)} " +
				$"EffectiveFaceMood={EffectiveFaceMood} EffortBoost={MasterFaceEffortBoost}");

			// Encerra a corrida na barra de progresso global do rodapé.
			try
			{
				if (progressOpId != Guid.Empty)
				{
					ProgressTrackingService.Instance.CompleteOperation(progressOpId);
				}
			}
			catch { }

			RunOnUiThread(delegate
			{
				IsMasterRunning = false;
			});

			// Comemoração: ao final de TODOS os cliques de limpeza.
			// O número mostrado é o espaço REAL liberado (CleanResult),
			// nunca um estimado.
			try
			{
				double realScore = 0;
				int verified = 0;
				if (isFirstEverRun)
				{
					var runEnd = Core.SystemIntelligenceProfiler.FirstRunOptimizationService.Measure();
					var startSnap = runStartSnapshot ?? runEnd;
					verified = firstRunResult?.VerifiedCount ?? 0;
					realScore = Core.SystemIntelligenceProfiler.FirstRunOptimizationService.ComputeRealScore(
						startSnap, runEnd, verified);

					// O que o Windows realmente devolveu em espaço, para
					// conferir contra o que a limpeza declarou.
					long measuredGain = runEnd.FreeSpaceBytes - startSnap.FreeSpaceBytes;
					_logger?.LogSuccess($"[MasterAction] 1ª otimização concluída. Score real: {realScore} | declarado: {realFreedBytes} bytes | medido no disco: {measuredGain} bytes | verificados: {verified}");
				}

				MasterLastFreedBytes = realFreedBytes;
				await ShowMasterCelebrationAsync(realFreedBytes, isFirstEverRun, realScore, verified);
			}
			catch (Exception ex7)
			{
				_logger?.LogWarning("[MasterAction] Falha ao comemorar: " + ex7.Message);
			}

			SetMasterClockRunning(running: false);
			RunOnUiThread(delegate
			{
				RefreshMasterLastRunText();
			});
			try
			{
				_masterCts?.Dispose();
			}
			catch
			{
			}
			_masterCts = null;
			_quickOptimizeLock.Release();
			_masterLock.Release();
			RunOnUiThread(delegate
			{
				IsCancelling = false;
			});
			DiagLog($"[DASHBOARD_VM][TID:{tid}] MasterActionAsync FINALIZADO");
		}
		await Task.Delay(1500);
		ResetMasterProgress();
	}

	private void CancelMasterExecution()
	{
		try
		{
			_masterCts?.Cancel();
		}
		catch
		{
		}
		IsCancelling = true;
		MasterStatusText = LocalizationService.Instance.GetString("MasterCanceling");
		GlobalProgressService.Instance.UpdateProgress((int)Math.Max(0.0, Math.Min(100.0, MasterProgress)), LocalizationService.Instance.GetString("MasterCanceling"));
	}

		/// <param name="propagateToGlobal">
		/// false quando o texto é um estado ocioso do BOTÃO (ex.: "Pronto para
		/// otimizar seu PC") e não uma etapa real. Empurrar isso para dentro da
		/// barra global deixava o texto preso no rodapé: como o rodapé só agenda
		/// o auto-ocultar quando não há operação em andamento, uma operação viva
		/// mantinha a mensagem na tela até o app fechar.
		/// </param>
		/// <summary>
		/// Piso monotônico da barra do botão circular.
		/// <para>
		/// A barra é a <c>MasterProgress</c> (DashboardView.xaml: o número grande e o
		/// arco). Ela é alimentada por esta função, e o problema original era que
		/// CADA ETAPA publicava um percentual relativo a si mesma:
		///   análise  = 32 + pct*0,15   ->  32..47
		///   limpeza  = 50 + pct*0,24   ->  50..74
		///   rede     = 76 / 79 / 82 / 85
		///   otimiz.  = 86
		/// Como <c>Progress&lt;T&gt;</c> pode reportar fora de ordem (e o
		/// <c>Dispatcher.BeginInvoke</c> enfileira as chamadas vindas de threads
		/// de progresso junto com as do fluxo principal), a barra subia e voltava a
		/// cada etapa.
		/// </para>
		/// <para>
		/// O piso abaixo garante, por contrato, que o valor NUNCA retrocede,
		/// venha de onde vier. Reinicia apenas quando uma nova execução começa.
		/// </para>
		/// </summary>
		private int _masterProgressFloor;

	private void ResetMasterProgressFloor()
	{
		Interlocked.Exchange(ref _masterProgressFloor, 0);
	}

	// ==================================================================
	// [UPDATE-FLUXO] O BOTÃO CIRCULAR ASSUME A ATUALIZAÇÃO
	// ==================================================================
	//
	// Este ViewModel já tinha `MasterProgress` e `MasterStatusText`, e o XAML
	// já tinha o arco ligado ao `MasterProgress` pelo `ArcProgressConverter`.
	// Ou seja: a peça visual existia e estava pronta — o que faltava era
	// alguém publicar o número certo nela.
	//
	// A publicação é feita por ASSINATURA do estado, e não por chamada direta.
	// A diferença importa: a atualização começa numa tela (Configurações),
	// termina em outra (Dashboard), e pode começar antes de o Dashboard
	// existir. Com assinatura, o estado atual é aplicado na hora em que este
	// ViewModel nasce, e não é preciso "repassar" nada quando o usuário troca
	// de tela no meio da atualização.
	//
	// `Unsubscribe` no descarte é obrigatório: o `UpdateFlowState` é estático
	// e vive até o fim do processo. Um ViewModel que não se desinscreve fica
	// preso na memória por um delegate, e o Dashboard é recriado a cada
	// navegação.
	private bool _updateFlowSubscribed;

	/// <summary>
	/// [UPDATE-FLUXO] O texto que aparece DENTRO do botão circular.
	///
	/// [FIX:TEXTO-INVISIVEL] ESTE É O MOTIVO DE VOCÊ NÃO VER NADA.
	/// ======================================================================
	/// O `MasterStatusText` já era publicado por este ViewModel, e o `XAML`
	/// também já tinha um `Path` para o arco. Mas o texto estava ligado APENAS
	/// em `MasterStatusTooltip` — que é o BALÃO que aparece quando se passa o
	/// mouse. Nenhum `TextBlock` mostrava o status na tela.
	///
	/// O resultado era: o número do progresso subia, o arco desenhava, e o
	/// usuário não conseguia ler em lugar nenhum QUAL atualização estava
	/// acontecendo nem de que versão se tratava. A informação existia; só não
	/// estava na tela.
	///
	/// Esta propriedade é dedicada ao fluxo de atualização e existe separada do
	/// `MasterStatusText` por um motivo prático: o botão circular tem outros
	/// usos, e durante a otimização o texto é o da otimização. Uma única
	/// propriedade para os dois faria um sobrescrever o outro, e o usuário veria
	/// o texto de otimização no meio de uma atualização.
	/// </summary>
	public string UpdateFlowStatusText { get; private set; } = string.Empty;

	/// <summary>
	/// Se o botão circular está mostrando o fluxo de atualização.
	///
	/// O `IsMasterRunning` (usado pela otimização) NÃO serve aqui: durante a
	/// atualização nenhum comando de otimização está rodando, então o
	/// `TextBlock` da porcentagem continuava com `Collapsed` e o número não
	/// aparecia. Cada situação precisa do seu flag, e usar o flag errado é
	/// exatamente o que esconde a informação.
	/// </summary>
	public bool IsUpdateFlowActive { get; private set; }

	/// <summary>
	/// Liga ou desliga a exibição do fluxo de atualização no botão circular.
	///
	/// Passa por <see cref="OnPropertyChanged"/> em vez de um
	/// <c>SetProperty</c> porque o valor também depende de
	/// <see cref="UpdateFlowStatusText"/>: os dois precisam mudar juntos, e
	/// qualquer um deles mudando sozinho deixaria a tela num estado que
	/// corresponde a nenhum deles.
	/// </summary>
	private void SetUpdateFlowVisible(bool visible)
	{
		if (IsUpdateFlowActive == visible) return;

		IsUpdateFlowActive = visible;
		OnPropertyChanged(nameof(IsUpdateFlowActive));
		OnPropertyChanged(nameof(UpdateFlowStatusText));
		OnPropertyChanged(nameof(MasterStatusTooltip));
	}

	/// <summary>
	/// Por que o texto do fluxo precisa existir em 3 idiomas separados.
	///
	/// As frases sãoCURTAS de propósito: cabem no círculo de 290px sem quebrar
	/// em três linhas, que é o que acontece com um texto de frase completa. A
	/// versão é o dado variável, e por isso vai em {0} — assim a posição dela
	/// no texto fica sob controle da tradução, e não da concatenação em código.
	/// </summary>
	private void EnsureUpdateFlowSubscribed()
	{
		if (_updateFlowSubscribed) return;
		_updateFlowSubscribed = true;
		VoltrisOptimizer.Services.UpdateFlow.UpdateFlowState.Changed += OnUpdateFlowChanged;

		// Aplica o estado atual imediatamente: o fluxo pode ter começado
		// antes de este ViewModel existir.
		OnUpdateFlowChanged(null, EventArgs.Empty);
	}

	private void OnUpdateFlowChanged(object? sender, EventArgs e)
	{
		try
		{
			var stage = VoltrisOptimizer.Services.UpdateFlow.UpdateFlowState.Stage;

			if (stage == VoltrisOptimizer.Services.UpdateFlow.UpdateFlowStage.Idle)
			{
				// Nada acontecendo: devolve o botão ao estado de repouso, com o
				// piso zerado para o arco não ficar preso no valor anterior.
				ResetMasterProgressFloor();
				MasterProgress = 0;
				MasterStatusText = LocalizationService.Instance.GetString("MasterReadyState");
				SetUpdateFlowVisible(false);

				// Devolve o rosto ao do Brain. Sem isto, um rosto furioso
				// ficaria na tela com o programa já ocioso — e ele não
				// estaria mais trabalhando em nada.
				SetMasterFaceOverride(null);
				return;
			}

			// [UPDATE-FLUXO] A VERSÃO APARECE NO STATUS.
			//
			// [FIX:FRASE-CURTA] O botão aceita UMA linha curta. A frase
			// completa ("Atualização detectada: versão 1.0.3.9") quebra em três
			// linhas dentro de um círculo de 290px e vira ruído — e a
			// porcentagem, que já está logo acima, é o número que importa.
			// A versão continua presente, só que compacta.
			// [FIX:VERSAO-SOME-NA-DOWNLOAD] A VERSÃO VAI EM TODOS OS ESTADOS.
			//
			// A versão era lida do estado UMA VEZ, aqui, e usada só no caso
			// "detectada" — que dura uma fração de segundo. De-pois disso o
			// botão mostrava "Baixando... 47%" e a versão sumia, mesmo com a
			// atualização ainda em andamento.
			//
			// A regra é simples: enquanto houver atualização, o número da versão
			// está no texto. Sem ele, o usuário não tem como saber de qual
			// versão se trata, e a pergunta "espero ou cancelo?" fica sem dado.
			string versao = VoltrisOptimizer.Services.UpdateFlow.UpdateFlowState.LatestVersion;
			string status;

			switch (stage)
			{
				case VoltrisOptimizer.Services.UpdateFlow.UpdateFlowStage.Detected:
					status = string.Format(
						LocalizationService.Instance.GetString("UpdateBtnDetected"),
						versao);
					break;

				case VoltrisOptimizer.Services.UpdateFlow.UpdateFlowStage.Downloading:
					status = string.Format(
						LocalizationService.Instance.GetString("UpdateBtnDownloading"),
						versao,
						VoltrisOptimizer.Services.UpdateFlow.UpdateFlowState.Progress.ToString("0"));
					break;

				case VoltrisOptimizer.Services.UpdateFlow.UpdateFlowStage.ReadyToApply:
					// [UPDATE-FLUXO] NO 100%, O TEXTO PASSA A PEDIR AÇÃO.
					//
					// Dizer "pronto para aplicar" sem dizer o que fazer é um beco
					// sem saída: o usuário chega ao fim, vê a interface destravada e
					// não sabe se é para reiniciar agora, esperar, ou fechar. A
					// pergunta precisa estar escrita NA TELA.
					status = string.Format(
						LocalizationService.Instance.GetString("UpdateBtnReady"),
						versao);
					break;

				case VoltrisOptimizer.Services.UpdateFlow.UpdateFlowStage.Failed:
					status = string.Format(
						LocalizationService.Instance.GetString("UpdateBtnFailed"),
						versao);
					break;

				default:
					status = VoltrisOptimizer.Services.UpdateFlow.UpdateFlowState.StatusText;
					break;
			}

			SetUpdateFlowVisible(true);
			UpdateFlowStatusText = status;

			// [UPDATE-FLUXO] O ROSTO FICA FURIOSO, COMO NA OTIMIZAÇÃO.
			//
			// A fase 2 da otimização (`ApplyMasterFaceMoodForPhase`) usa
			// `BrainMood.Furious`, que é o que produz a expressão vermelha e o
			// `EffortBoost` 1.8. Reusar o MESMO override é o que faz o usuário
			// reconhecer a atualização como mais um trabalho em curso, em vez de
			// um estado novo e desconhecido.
			//
			// Chamar `SetMasterFaceOverride` (e não atribuir o campo) é
			// obrigatório: o método dispara `OnPropertyChanged` para
			// `EffectiveFaceMood` E `MasterFaceEffortBoost`. Sem as duas
			// notificações, o rosto mudaria de expressão mas continuaria com a
			// vivacidade antiga — que é exatamente o defeito que o método
			// documenta e existe para evitar.
			SetMasterFaceOverride(BrainMood.Furious);

			// O piso é zerado a cada evento porque este valor NÃO é uma barra
			// hierárquica de otimizações: é o download de um arquivo, que anda
			// devagar no começo e quase para no fim. Sem zerar, o piso reteria
			// um valor baixo e o arco pararia de acompanhar.
			ResetMasterProgressFloor();
			MasterProgress = VoltrisOptimizer.Services.UpdateFlow.UpdateFlowState.Progress;
			MasterStatusText = status;
		}
		catch (Exception ex)
		{
			// Falha em observar o estado de atualização nunca pode derrubar o
			// botão circular, que serve a todas as outras funções do app.
			DiagLog($"[DASHBOARD_VM] Falha ao aplicar estado de atualização: {ex.Message}");
		}
	}

		/// <param name="force">
		/// IGNORA o piso monotônico e ZERA o piso. Use só para os dois casos em
		/// que a barra tem legitimamente de voltar atrás: o usuário cancelou, ou
		/// a execução terminou e o botão voltou ao estado de repouso.
		/// <para>
		/// Sem isto, <c>UpdateMasterProgress(0)</c> era permanentemente inútil:
		/// com o piso em 85, <c>effective = max(0, 85) = 85</c>, e o arco da
		/// borda ficava congelado em 85% depois do cancelamento, com o número
		/// grande no centro junto. O piso existe para resolver um problema real
		/// (relatórios fora de ordem do <c>Progress&lt;T&gt;</c> faziam a barra
		/// subir e descer), mas ele não pode se sobrepor a um cancelamento — um
		/// cancelamento não é "atraso de relatório", é o fim da operação.
		/// </para>
		/// </param>
		private void UpdateMasterProgress(int percentage, string status, bool propagateToGlobal = true, bool force = false)
		{
			// [FIX:TOOLTIP-VAZIO] Status em branco NÃO pode apagar o texto do botão.
			//
			// Diversos pontos da otimização chamavam isto com string.Empty para
			// dizer "não há etapa nova para announcear" — entre uma etapa e outra,
			// por exemplo (UpdateMasterProgress(..., string.Empty, ...) e
			// SetOperationDetail(id, string.Empty)). Até aqui o texto VIRAVA
			// vazio de verdade.
			//
			// O botão circular tem ToolTip="{Binding MasterStatusText}". Um ToolTip
			// com conteúdo vazio NÃO é ignorado pelo WPF: o popup abre do mesmo
			// jeito, mostrando uma caixa sem nada dentro. Era o "quadradinho"
			// que aparecia sobre o botão enquanto a otimização corria.
			//
			// "Sem etapa nova" e "apagar o que estava escrito" são coisas
			// diferentes. Este método só publica texto que diz algo; em branco,
			// mantém o último texto válido — que é o que descreve o que está
			// acontecendo agora.
			string status2 = string.IsNullOrWhiteSpace(status) ? _masterStatusText : status;
			// Aplica o piso ANTES de despachar para a UI, para que a comparação
			// seja feita uma única vez e na ordem de chegada.
			int requested = Math.Max(0, Math.Min(100, percentage));
			int current = Interlocked.CompareExchange(ref _masterProgressFloor, 0, 0);

			int effective;
			if (force)
			{
				// Zera o piso ANTES de publicar, senão o próximo relatório que
				// chegar (um callback atrasado da etapa cancelada) reconstruiria
				// o valor antigo e a barra voltaria a subir depois do reset.
				Interlocked.Exchange(ref _masterProgressFloor, requested);
				effective = requested;
			}
			else
			{
				effective = requested > current ? requested : current;
				if (effective > current) Interlocked.Exchange(ref _masterProgressFloor, effective);
			}

			_logger?.LogInfo(
				$"[MasterProgress] force={force} | solicitado={requested} piso={current} " +
				$"-> efetivo={effective} | MasterProgress={MasterProgress} IsMasterRunning={IsMasterRunning} " +
				$"| status=\"{status2}\"");

			Application current2 = Application.Current;
			Dispatcher val = ((current2 != null) ? ((DispatcherObject)current2).Dispatcher : null);
			if (val != null)
			{
				Action action = delegate
				{
					MasterProgress = effective;
					MasterStatusText = status2;
					if (propagateToGlobal)
					{
						GlobalProgressService.Instance.UpdateProgress(effective, status2);
					}
				};
				if (val.CheckAccess())
				{
					action();
				}
				else
				{
					val.BeginInvoke((Delegate)action, Array.Empty<object>());
				}
			}
		}

		/// <summary>
		/// [FIX:MASTER-SINGLE-SOURCE] Atualiza SÓ o texto de status do botão,
		/// sem tocar na porcentagem.
		///
		/// Existe porque a % do botão circular tem UMA dona agora: o evento
		/// <c>ProgressTrackingService.ProgressUpdated</c>. Os mapas fixos por
		/// etapa que existiam antes (análise 32..47, limpeza 50..80,
		/// otimização 82..100) continuavam alimentando <c>UpdateMasterProgress</c>
		/// e voltavam a divergir da barra do rodapé — o piso monotônico às vezes
		/// segurava o evento e às vezes era o valor fixo que vencia, o que
		/// tornava a divergência intermitente e difícil de diagnosticar.
		///
		/// Separar as duas responsabilidades é o que torna a correção
		/// definitiva: o texto vem da etapa, a % vem da barra, e ninguém mais
		/// disputa o mesmo número.
		/// </summary>
		private void UpdateMasterStatusTextOnly(string status)
		{
			// [FIX:TOOLTIP-VAZIO] Mesma regra de UpdateMasterProgress: status em
			// branco não apaga o que está escrito. Aqui a origem é o
			// ProgressTrackingService, que emite OperationDetail/SubTaskName
			// vazios entre etapas — exatamente durante a execução, que é quando
			// o quadrinho aparecia.
			if (string.IsNullOrWhiteSpace(status))
			{
				return;
			}

			RunOnUiThread(delegate
			{
				MasterStatusText = status;
			});
		}

		/// <summary>
		/// [FIX:PROGRESS-PCT] Progresso de etapa por TEMPO decorrido, com teto
		/// em 95%.
		///
		/// POR QUE EXISTE — a medição que motivou tudo
		/// =========================================
		/// O log de 2026-09-28 15:37 mediu o defeito com precisão:
		///
		///   Analisando: iniciou 15:37:46.32, marcou 100% às 15:37:48.86
		///                (2,5s) e só TERMINOU às 15:37:59.14 (13,0s)
		///                -> 10,5s parado em 100%
		///
		///   Limpando:   iniciou 15:37:59.14, marcou 100% às 15:38:01.46
		///                (2,3s) e só TERMINOU às 15:38:49.09 (49,9s)
		///                -> 47,6s parado em 100%
		///
		/// Como a barra do rodapé é média PONDERADA, Analyze=25 e Limpeza=55
		///Weights davam 80% no instante em que as duas saturaram — e a barra
		/// ficou parada em 80% por 47 segundos. Era o "fica sempre em 100%".
		///
		/// CAUSA: o <c>PercentComplete</c> que <c>AnalyzeAllAsync</c> e
		/// <c>CleanSelectedAsync</c> reportam NÃO é progresso de trabalho. Ele
		/// conta itens despachados, não itens terminados: o item final de cada
		/// varredura ("Defender Definition Backup") sozinho consumiu 47s depois
		/// de a contagem já ter chegado ao fim.
		///
		/// CORREÇÃO
		/// Não confiar no PercentComplete. O tempo decorrido é a única grandeur
		/// que o app conhece com honestidade, então é ela que alimenta a barra.
		/// O teto de 95% reserva os 5% finais para o <c>CompleteSubTask</c> de
		/// verdade: assim a barra nunca mostra 100% antes da etapa acabar, e se a
		/// etapa estourar a estimativa ela fica em 95% até terminar — crawl
		/// honesto, e não um 100% mentiroso.
		/// </summary>
		private sealed class StageProgressTicker : IDisposable
		{
			private readonly DispatcherTimer _timer;
			private readonly Stopwatch _watch = Stopwatch.StartNew();
			private readonly Action<int> _report;
			private readonly int _expectedMs;

			/// <param name="expectedMs">Estimativa da etapa. Só define a
			/// INCLINAÇÃO da curva, não o fim: passar do tempo segura em 95%.</param>
			/// <param name="report">Recebe 0..95.</param>
			public StageProgressTicker(int expectedMs, Action<int> report)
			{
				_expectedMs = Math.Max(1000, expectedMs);
				_report = report;

				// [FIX:PROGRESS-PCT] O timer PRECISA nascer no Dispatcher da UI.
				// `new DispatcherTimer()` sem argumento usa
				// Dispatcher.CurrentDispatcher, que dentro do Task.Run desta
				// operação é um dispatcher novo que NUNCA roda: o Tick não
				// dispararia uma vez e a barra ficaria parada, com ar de estar
				// consertada. Foi exatamente esse bug na primeira tentativa.
				var ui = Application.Current?.Dispatcher
					?? System.Windows.Threading.Dispatcher.CurrentDispatcher;

				_timer = new DispatcherTimer(DispatcherPriority.Background, ui)
				{
					Interval = TimeSpan.FromMilliseconds(400)
				};
				_timer.Tick += (s, e) =>
				{
					try
					{
						_report(CurrentPercentage());
					}
					catch { }
				};
				_timer.Start();
			}

			/// <summary>Percentual atual, 0..95.</summary>
			public int CurrentPercentage()
			{
				int pct = (int)(_watch.ElapsedMilliseconds * 95L / _expectedMs);
				if (pct > 95) pct = 95;
				if (pct < 0) pct = 0;
				return pct;
			}

			public void Dispose()
			{
				try { _timer.Stop(); } catch { }
			}
		}

	private void ResetMasterProgress()
	{
		SetMasterPhaseSafe(0);
		// Estado ocioso do botão: NÃO vai para a barra global. Era essa mensagem
		// que ficava presa no rodapé depois da 1ª otimização.
		//
		// [FIX:MASTER-CANCEL] `force: true` é OBRIGATÓRIO aqui. Sem ele o piso
		// monotônico segurava o valor da última etapa e o arco da borda nunca
		// sumia: o botão voltava "pronto" com 85% desenhado na borda e 85% no
		// centro, esperando pela próxima execução.
		UpdateMasterProgress(0, LocalizationService.Instance.GetString("MasterReadyState"),
			propagateToGlobal: false, force: true);
		// Garante que o rodapé não fique com o texto da última etapa preso,
		// mesmo que sobre alguma operação em andamento (o rodapé só agenda o
		// auto-ocultar quando IsOperationRunning == false).
		try { GlobalProgressService.Instance.ClearDisplayedStatus(); } catch { }
		SetMasterClockRunning(running: false);
		RunOnUiThread(delegate
		{
			RefreshMasterLastRunText();
		});
	}

	private void SetMasterClockRunning(bool running)
	{
		Application current = Application.Current;
		Dispatcher val = ((current != null) ? ((DispatcherObject)current).Dispatcher : null);
		if (val == null)
		{
			return;
		}
		val.BeginInvoke((Delegate)(Action)delegate
		{
			if (_masterClockTimer != null)
			{
				_masterClockRequested = running;
				if (running && !_masterClockTimer.IsEnabled)
				{
					_masterClockSw.Restart();
					_masterClockTimer.Start();
					UpdateMasterRunSummary();
				}
				else if (!running && _masterClockTimer.IsEnabled)
				{
					_masterClockTimer.Stop();
					_masterClockSw.Stop();
					MasterClockText = "00:00";
					UpdateMasterRunSummary();
				}
			}
		}, Array.Empty<object>());
	}

	private void UpdateMasterRunSummary()
	{
		// Antes isto mostrava o total só DURANTE a execução e limpava no fim,
		// que é exatamente por isso que o "Liberado" nunca aparecia.
		// Agora o valor real da última rodada fica visível.
		if (IsMasterRunning)
		{
			MasterRunSummary = $"{MasterClockText}   {LocalizationService.Instance.GetString("MasterFreed")}: {FileSystemHelper.FormatBytes(MasterFreedBytes)}";
		}
		else if (MasterLastFreedBytes > 0)
		{
			MasterRunSummary = LocalizationService.Instance.GetString("MasterFreed") + ": " + FileSystemHelper.FormatBytes(MasterLastFreedBytes);
		}
		else
		{
			MasterRunSummary = string.Empty;
		}
	}

	private void RefreshMasterLastRunText()
	{
		MasterLastRunText = LocalizationService.Instance.GetString("MasterLastRun") + ": " + LastOptimizationTime;
	}

	private async Task UpdateSystemHealth()
	{
		_logger?.LogInfo("[DASHBOARD] Health analysis started...");
		try
		{
			using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(15.0));
			await Task.Run(delegate
			{
				UpdateHealthStatus(force: true, "Background");
			}, cts.Token);
			_logger?.LogInfo($"[DASHBOARD] Health analysis completed - Status: {HealthStatusText}, Score: {OverallScore}");
		}
		catch (OperationCanceledException)
		{
			_logger?.LogError("[DASHBOARD] Health analysis timeout after 15s");
			HealthStatusText = LocalizationService.Instance["HealthStatusTimeout"];
			HealthStatusColor = "#EF4444";
			HealthDescription = LocalizationService.Instance["DashboardHealthTimeout"];
		}
		catch (Exception ex)
		{
			_logger?.LogError("[DASHBOARD] Health analysis failed: " + ex.Message, ex);
			HealthStatusText = LocalizationService.Instance["HealthStatusError"];
			HealthStatusColor = "#EF4444";
			HealthDescription = LocalizationService.Instance["DashboardHealthError"];
		}
	}

	private void UpdateLinkedEmailFromSettings()
	{
		try
		{
			AppSettings settings = SettingsService.Instance.Settings;
			string text = ((settings.IsDeviceLinked && !string.IsNullOrEmpty(settings.LinkedUserEmail) && settings.LinkedUserEmail != "skip@voltris.local") ? settings.LinkedUserEmail : ((!settings.IsDeviceLinked) ? LocalizationService.Instance["DashboardLinkedEmailNone"] : LocalizationService.Instance["DashboardLinkedEmailMissing"]));
			if (LinkedEmail != text)
			{
				LinkedEmail = text;
			}
		}
		catch (Exception exception)
		{
			_logger?.LogError("Erro ao atualizar email vinculado", exception);
		}
	}

	private async Task NavigateToAsync(AppPage page)
	{
		if (!LicenseOrchestrationService.Features.IsNavigationEnabled)
		{
			_logger?.LogWarning($"[Dashboard] Navegação para {page} bloqueada (licença)");
			string message = string.Format(arg0: (await LicenseOrchestrationService.Instance.GetCurrentStateAsync()).FormattedStatus, format: LocalizationService.Instance.GetString("NavigationBlockedMessage"));
			LicenseRequiredMessageBox.Show(message, LocalizationService.Instance.GetString("NavigationBlockedTitle"));
		}
		else
		{
			_logger?.LogInfo($"[Dashboard] Navegando para: {page}");
			_navigationService?.NavigateTo(page.ToString());
		}
	}

	protected override void OnDisposing()
	{
		try
		{
			if (_settingsMonitorCts != null)
			{
				_settingsMonitorCts.Cancel();
				_settingsMonitorCts.Dispose();
				_settingsMonitorCts = null;
			}
			try
			{
				if (_masterClockTimer != null)
				{
					_masterClockTimer.Stop();
					_masterClockTimer.Tick -= OnMasterClockTick;
				}
			}
			catch
			{
			}
			try
			{
				_brainMetrics.PropertyChanged -= OnBrainMetricsPropertyChanged;
			}
			catch
			{
			}
			try
			{
				if (_smartRepairVM != null)
				{
					_smartRepairVM.PropertyChanged -= OnSmartRepairPropertyChanged;
				}
			}
			catch
			{
			}
			SystemMetricsCache.Instance.MetricsUpdated -= OnGlobalMetricsUpdated;
			LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
			try
			{
				SettingsService.Instance.ProfileChanged -= OnProfileChanged;
			}
			catch
			{
			}
			try
			{
				SettingsService.Instance.SettingsChanged -= OnAppSettingsChanged;
			}
			catch
			{
			}
			if (_gamerViewModel != null)
			{
				_gamerViewModel.PropertyChanged -= OnGamerViewModelPropertyChanged;
			}
			if (_licenseManager != null)
			{
				_licenseManager.LicenseStatusChanged -= OnLicenseStatusChanged;
			}
			CloudAccountService.Instance.AccountStateChanged -= OnCloudAccountStateChanged;
			UnsubscribeThermalEvents();
			ApplicationStateTracker.StateChanged -= OnApplicationLifecycleStateChanged;

			// [FIX:A-1] Auditoria: este era o ponto onde as 8 assinaturas
			// estaticas eram liberadas, mas o metodo nunca era chamado durante a
			// sessao. Agora e, via Unloaded da DashboardView.
			int liveAfter = Interlocked.Decrement(ref _liveInstances);
			int disposedTotal = Interlocked.Increment(ref _totalDisposed);
			_logger?.LogInfo(
				$"[FIX:A-1] DashboardViewModel descartado | Hash={GetHashCode()} | vivas={liveAfter} | " +
				$"descartadosTotal={disposedTotal} | assinaturas estaticas desfeitas=OK");
		}
		catch
		{
		}
		base.OnDisposing();
	}
}

// Esta classe vivia no mesmo arquivo do DashboardViewModel e foi perdida
// junto com ele. Recuperada do assembly compilado; comportamento identico ao original.
public class DiskItemViewModel : ViewModelBase
{
	private string _name = string.Empty;

	private double _usagePercent;

	private string _usageText = string.Empty;

	private Brush _color = Brushes.Orange;

	public string Name
	{
		get { return _name; }
		set { SetProperty(ref _name, value, "Name"); }
	}

	public double UsagePercent
	{
		get { return _usagePercent; }
		set { SetProperty(ref _usagePercent, value, "UsagePercent"); }
	}

	public string UsageText
	{
		get { return _usageText; }
		set { SetProperty(ref _usageText, value, "UsageText"); }
	}

	public Brush Color
	{
		get { return _color; }
		set { SetProperty(ref _color, value, "Color"); }
	}
}
