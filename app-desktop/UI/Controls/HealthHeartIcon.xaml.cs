using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using VoltrisOptimizer.Core;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.UI.ViewModels;

namespace VoltrisOptimizer.UI.Controls
{
    /// <summary>
    /// Ícone de SAÚDE do Dashboard: um coração que bate.
    ///
    /// A DIFERENÇA CENTRAL PARA O ÍCONE ANTERIOR: o antigo animava a OPACIDADE
    /// do Path (fade de 0,62 a 1, repetindo). Isso é uma luz piscando, não um
    /// batimento. Aqui o movimento vem de um MODELO DE CICLO CARDÍACO - a fase
    /// 0..1 do ciclo whole, com sístole, recoil elástico, onda dicrotica e
    /// sístole auricular - e cada um dos quatro eventos mexe em uma propriedade
    /// diferente (escala, rotação, aura, anel, especular). É a diferença entre
    /// um phosphor piscando e um coração.
    ///
    /// O ESTADO VEIO DO DADO REAL: <see cref="HealthIconState"/> é derivado do
    /// score de saúde calculado pela análise, não de um palpite. Ele troca a
    /// paleta E o ritmo:
    ///
    ///   Healthy  - 62 bpm, ritmo regular, sem tremor. Lento e calmo.
    ///   Warning  - 92 bpm, ±14% de variação por batimento e uma pausa
    ///              extras sistólica a cada 9 ciclos: o coração "pula" um tempo,
    ///              que é como instabilidade se manifesta de verdade.
    ///   Critical - 138 bpm, ±22%, fibrilação (tremor de ~10 Hz sobreposto),
    ///              fenda no miocárdio que acende a cada sístole.
    ///
    /// CUSTO (mesmo critério do AiFaceView - este app não pode queimar CPU):
    ///   - UM DispatcherTimer de período ADAPTATIVO: 30fps durante a sístole e
    ///     nos anéis de choque, 11fps na diastole. Um batimento de 62bpm gasta
    ///     ~80% do tempo em diastole, então o custo cai para ~1/3.
    ///   - Geometria CONSTRUÍDA UMA VEZ e congelada. Zero alocação por quadro:
    ///     o batimento mexe só em ScaleTransform/Opacity, que são resolvidos
    ///     na GPU sem passar por measure/arrange.
    ///   - Pincel por paleta, congelado, trocado só na MUDANÇA de estado.
    ///   - Nenhum Effect, BlurEffect, Bitmap, shader ou DropShadow aqui dentro.
    /// </summary>
    public partial class HealthHeartIcon : UserControl
    {
        #region Dependency Properties

        public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
            nameof(State), typeof(HealthIconState), typeof(HealthHeartIcon),
            new FrameworkPropertyMetadata(HealthIconState.Healthy, OnStatePropertyChanged));

        /// <summary>Estado do ícone. Vem do score real de saúde.</summary>
        public HealthIconState State
        {
            get => (HealthIconState)GetValue(StateProperty);
            set => SetValue(StateProperty, value);
        }

        public static readonly DependencyProperty AnimationsEnabledProperty = DependencyProperty.Register(
            nameof(AnimationsEnabled), typeof(bool), typeof(HealthHeartIcon),
            new FrameworkPropertyMetadata(true, OnAnimationsEnabledChanged));

        /// <summary>Portão herdado do Dashboard (<c>ShouldAnimationsRun</c>).</summary>
        public bool AnimationsEnabled
        {
            get => (bool)GetValue(AnimationsEnabledProperty);
            set => SetValue(AnimationsEnabledProperty, value);
        }

        private static void OnStatePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is HealthHeartIcon v) v.ApplyState((HealthIconState)e.NewValue);
        }

        private static void OnAnimationsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is HealthHeartIcon v) v.ApplyGate((bool)e.NewValue);
        }

        #endregion

        #region Constantes de ritmo

        /// <summary>30fps. Só na sístole e enquanto os anéis de choque estão vivos.</summary>
        private const int IntervalBeatMs = 33;

        /// <summary>11fps. Diastole: o miocárdio está em repouso, não há o que suavizar.</summary>
        private const int IntervalRestMs = 90;

        /// <summary>Janela da sístole dentro do ciclo (0..1). Também abre o anel A.</summary>
        private const double SystoleWindow = 0.30;

        /// <summary>Abertura do anel A, em fração do ciclo.</summary>
        private const double ShockAStart = 0.16;
        private const double ShockALen = 0.46;

        /// <summary>Abertura do anel B (onda dicrotica): mais curto e mais fraco.</summary>
        private const double ShockBStart = 0.46;
        private const double ShockBLen = 0.30;

        /// <summary>A cada 9 batimentos: uma sístole extrasistólica e a pausa que vem depois.</summary>
        private const int PrematureEvery = 9;

        #endregion

        #region Estado

        private DispatcherTimer? _clock;
        private readonly Stopwatch _time = new();
        private bool _started;
        private bool _suspended = true;
        private int _intervalMs = IntervalRestMs;

        /// <summary>Fase dentro do ciclo cardíaco, 0..1.</summary>
        private double _phase;

        /// <summary>Batimento corrente. Indexa a variabilidade e as extrasístoles.</summary>
        private int _beat;

        /// <summary>Duração do ciclo ATUAL, em segundos. Variada por batimento.</summary>
        private double _cycle = 60.0 / 62.0;

        private double _hoverT;
        private double _now;
        private int _renderErrors;

        // parâmetros do ritmo, escritos por ApplyState
        private double _bpm = 62.0;
        private double _irregular;
        private bool _hasExtrasystole;
        private double _tremor;
        private bool _hasFissure;

        private Palette _palette = null!;

        #endregion

        public HealthHeartIcon()
        {
            InitializeComponent();
            BuildStaticVisuals();
            ApplyState(State);
            FreezePose();

            _clock = new DispatcherTimer(DispatcherPriority.Render, Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(IntervalRestMs)
            };
            _clock.Tick += OnTick;

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            IsVisibleChanged += OnIsVisibleChanged;

            // Trava que separa "o construtor ainda está montando" de "o controle
            // existe e pode reagir a eventos". Sem ela, um Loaded disparado
            // durante o InitializeComponent chamaria Resume com a paleta nula.
            _started = true;
        }

        #region Geometria e pincelaria estática

        /// <summary>
        /// Silhueta do coração em vista FRONTAL: ápice deslocado para a direita,
        /// ventrículo direito maior que o esquerdo, cava interatrial assimétrica.
        /// Não é a curva Cardioid nem o Path material de duas metades - é o que
        /// separa "desenhado" de "ícone de banco".
        /// </summary>
        private static Geometry BuildHeart()
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(48, 26), true, true);
                c.BezierTo(new Point(43, 14), new Point(32, 4), new Point(20, 8), true, false);
                c.BezierTo(new Point(8, 12), new Point(3, 24), new Point(7, 35), true, false);
                c.BezierTo(new Point(11, 46), new Point(21, 55), new Point(30, 63), true, false);
                c.BezierTo(new Point(39, 71), new Point(48, 82), new Point(54, 90), true, false);
                c.BezierTo(new Point(58, 82), new Point(64, 73), new Point(72, 64), true, false);
                c.BezierTo(new Point(81, 54), new Point(90, 45), new Point(93, 33), true, false);
                c.BezierTo(new Point(96, 21), new Point(89, 10), new Point(77, 8), true, false);
                c.BezierTo(new Point(65, 6), new Point(54, 15), new Point(48, 26), true, false);
            }
            g.Freeze();
            return g;
        }

        /// <summary>
        /// Sulco interventricular: a ranhura que separa os ventrículos. Desce da
        /// base quase na vertical eDESVIA para a direita ao se aproximar do ápice,
        /// porque o ventrículo esquerdo é o que forma a ponta.
        /// </summary>
        private static Geometry BuildGroove()
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(43, 29), false, false);
                c.BezierTo(new Point(46, 42), new Point(49, 55), new Point(52, 68), true, false);
                c.BezierTo(new Point(54, 77), new Point(56, 85), new Point(55, 90), true, false);
            }
            g.Freeze();
            return g;
        }

        /// <summary>
        /// Luz de contorno: só o TRECHO da borda inferior-direita, do bulge
        /// atrial até o ápice. Um traço sobre a silhueta inteira acenderia as
        /// costas do órgão e denunciaria o truque; metade da borda, não.
        /// </summary>
        private static Geometry BuildRim()
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(93, 33), false, false);
                c.BezierTo(new Point(90, 45), new Point(81, 54), new Point(72, 64), true, false);
                c.BezierTo(new Point(64, 73), new Point(58, 82), new Point(54, 90), true, false);
            }
            g.Freeze();
            return g;
        }

        /// <summary>
        /// Fenda do miocárdio em CRITICAL. Polilinha pura, sem suavização: rachadura
        /// tem quina. A fissura principal desce do sulco e abre duas ramificações,
        /// uma para cada ventricle.
        /// </summary>
        private static Geometry BuildFissure()
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(49, 21), false, false);
                c.LineTo(new Point(45, 33), true, false);
                c.LineTo(new Point(52, 40), true, false);
                c.LineTo(new Point(43, 51), true, false);
                c.LineTo(new Point(51, 60), true, false);
                c.LineTo(new Point(45, 71), true, false);
                c.LineTo(new Point(53, 85), true, false);

                c.BeginFigure(new Point(52, 40), false, false);
                c.LineTo(new Point(63, 45), true, false);
                c.LineTo(new Point(69, 41), true, false);

                c.BeginFigure(new Point(45, 33), false, false);
                c.LineTo(new Point(34, 39), true, false);
                c.LineTo(new Point(29, 34), true, false);
            }
            g.Freeze();
            return g;
        }

        private void BuildStaticVisuals()
        {
            Geometry heart = BuildHeart();
            HeartBody.Data = heart;
            HeartShade.Data = heart;
            Flash.Data = heart;
            Aura.Data = heart;
            Groove.Data = BuildGroove();
            RimLight.Data = BuildRim();
            CrackGap.Data = BuildFissure();
            CrackHot.Data = BuildFissure();
            CrackHotGlow.Data = BuildFissure();

            // O rasgo tem de ser LARGO e PRETO. Um traço fino e escuro sobre um
            // coração escuro é invisível - e o estado Crítico é o único que o
            // usuário precisa enxergar de relance, num tile de 32px.
            CrackGap.Stroke = Frozen(Color.FromRgb(0x14, 0x01, 0x06), 215);
            CrackGap.StrokeThickness = 4.4;

            // ESPESSURAS. O palco é 140 unidades dentro de um ícone de 20px:
            // 0,143 px por unidade. A espessura default do WPF (1,0) daria
            // 0,14 px e o sulco simplesmente não existiria. O par do sulco é
            // calibrado para cair em ~0,43 px (escuro) e ~0,32 px (claro) -
            // nessa escala um sulco de verdade não TEM linha, tem sombra, e
            // por isso os dois traços são finos e de baixo alpha.
            Groove.StrokeThickness = 3.0;
            GrooveHi.StrokeThickness = 2.2;
            Groove.StrokeStartLineCap = PenLineCap.Round;
            Groove.StrokeEndLineCap = PenLineCap.Round;
            GrooveHi.StrokeStartLineCap = PenLineCap.Round;
            GrooveHi.StrokeEndLineCap = PenLineCap.Round;
            RimLight.StrokeThickness = 2.6;
            RimLight.StrokeStartLineCap = PenLineCap.Round;
            RimLight.StrokeEndLineCap = PenLineCap.Round;
            CrackHot.StrokeThickness = 1.2;
            CrackHotGlow.StrokeThickness = 3.2;
            foreach (Path p in new[] { CrackGap, CrackHot, CrackHotGlow })
            {
                p.StrokeStartLineCap = PenLineCap.Round;
                p.StrokeEndLineCap = PenLineCap.Round;
                p.StrokeLineJoin = PenLineJoin.Round;
            }
        }

        /// <summary>Paleta de um estado: cores, e o ritmo que vai com elas.</summary>
        private sealed class Palette
        {
            public Color Core, Mid, Deep, Edge;
            public Color Aura, Ring, Spec, Crack;
            public double Bpm, Irregular, Tremor;
            public bool Extrasystole, Fissure;
        }

        private static Palette PaletteFor(HealthIconState s) => s switch
        {
            // CRITICAL: o sangue fica escuro e dessaturado. Um vermelho vivo
            // gritaria "urgente" e o cartão já tem texto e barra vermelhos - o
            // ícone não precisa gritar junto, precisa parecer DOENTE.
            HealthIconState.Critical => new Palette
            {
                Core = Color.FromRgb(0xFF, 0x6E, 0x63),
                Mid = Color.FromRgb(0xC0, 0x18, 0x2C),
                Deep = Color.FromRgb(0x87, 0x0C, 0x1E),
                Edge = Color.FromRgb(0x3A, 0x04, 0x0E),
                Aura = Color.FromRgb(0xE1, 0x1D, 0x48),
                Ring = Color.FromRgb(0xFF, 0x2D, 0x45),
                Spec = Color.FromRgb(0xFF, 0xD9, 0xD2),
                Crack = Color.FromRgb(0xFF, 0xF0, 0xE6),
                Bpm = 138.0,
                Irregular = 0.22,
                Tremor = 0.016,
                Extrasystole = false,
                Fissure = true
            },

            // WARNING: ocre. O sangue "engrossou": o vermelho virou âmbar, que é
            // a cor do estado instável - instável, não morto.
            HealthIconState.Warning => new Palette
            {
                Core = Color.FromRgb(0xFF, 0xD4, 0x8A),
                Mid = Color.FromRgb(0xE2, 0x6E, 0x18),
                Deep = Color.FromRgb(0xA8, 0x44, 0x0C),
                Edge = Color.FromRgb(0x4A, 0x1E, 0x04),
                Aura = Color.FromRgb(0xF5, 0x9E, 0x0B),
                Ring = Color.FromRgb(0xFF, 0xB0, 0x20),
                Spec = Color.FromRgb(0xFF, 0xF2, 0xD8),
                Crack = Color.FromRgb(0xFF, 0xE8, 0xC0),
                Bpm = 92.0,
                Irregular = 0.14,
                Tremor = 0.004,
                Extrasystole = true,
                Fissure = false
            },

            // HEALTHY: o único vermelho do conjunto. Carne, não neon - e é
            // justamente o vermelho FAZ o miocárdio parecer vivo.
            _ => new Palette
            {
                Core = Color.FromRgb(0xFF, 0x77, 0x83),
                Mid = Color.FromRgb(0xE1, 0x1D, 0x3C),
                Deep = Color.FromRgb(0xA8, 0x0F, 0x28),
                Edge = Color.FromRgb(0x54, 0x04, 0x14),
                Aura = Color.FromRgb(0xFF, 0x3B, 0x54),
                Ring = Color.FromRgb(0xFF, 0x4D, 0x6B),
                Spec = Color.FromRgb(0xFF, 0xE4, 0xE2),
                Crack = Color.FromRgb(0xFF, 0xF2, 0xF0),
                Bpm = 62.0,
                Irregular = 0.0,
                Tremor = 0.0,
                Extrasystole = false,
                Fissure = false
            }
        };

        private void ApplyState(HealthIconState state)
        {
            _palette = PaletteFor(state);
            _bpm = _palette.Bpm;
            _irregular = _palette.Irregular;
            _tremor = _palette.Tremor;
            _hasExtrasystole = _palette.Extrasystole;
            _hasFissure = _palette.Fissure;

            HeartBody.Fill = BodyBrush(_palette);
            HeartShade.Fill = ShadeBrush();
            Aura.Fill = Frozen(_palette.Aura, 46);
            ShockA.Fill = RingBrush(_palette);
            ShockB.Fill = RingBrush(_palette);
            Flash.Fill = FlashBrush(_palette.Spec);
            Specular.Fill = SpecBrush(_palette.Spec);
            Specular2.Fill = SpecBrush(_palette.Spec);
            Groove.Stroke = Frozen(Color.FromRgb(0x3A, 0x03, 0x0C), 118);
            GrooveHi.Stroke = Frozen(Color.FromRgb(0xFF, 0xB8, 0xBE), 46);
            RimLight.Stroke = RimBrush(_palette);
            CrackHot.Stroke = Frozen(_palette.Crack, 255);
            CrackHotGlow.Stroke = Frozen(_palette.Ring, 120);

            // Recomeça o ritmo: um coração que muda de estado tem de bater no
            // ritmo novo desde o primeiro quadro, senão o primeiro tique é uma
            // interpolação do ritmo velho e parece um engasgo.
            _cycle = 60.0 / _bpm;
            _phase = 0.0;
            _beat = 0;
            _now = 0.0;
            _time.Restart();

            LogInfo($"[HealthIcon] estado={state} | {(_bpm, _irregular, _hasFissure)}");
        }

        private static Brush BodyBrush(Palette p)
        {
            var b = new RadialGradientBrush
            {
                Center = new Point(0.38, 0.28),
                GradientOrigin = new Point(0.38, 0.28),
                RadiusX = 0.82,
                RadiusY = 0.86
            };
            b.GradientStops.Add(new GradientStop(With(p.Core, 255), 0.00));
            b.GradientStops.Add(new GradientStop(With(p.Mid, 255), 0.38));
            b.GradientStops.Add(new GradientStop(With(p.Deep, 255), 0.72));
            b.GradientStops.Add(new GradientStop(With(p.Edge, 255), 1.00));
            b.Freeze();
            return b;
        }

        /// <summary>
        /// Sombra de forma em diagonal: transparente no alto-esquerda (onde a luz
        /// entra) e densa embaixo-direita (onde o ventrículo se apoia no diafragma).
        /// </summary>
        private static Brush ShadeBrush()
        {
            var b = new LinearGradientBrush
            {
                StartPoint = new Point(0.12, 0.05),
                EndPoint = new Point(0.92, 1.00)
            };
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.00));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.48));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(150, 0x1A, 0x00, 0x08), 1.00));
            b.Freeze();
            return b;
        }

        /// <summary>
        /// Anel de choque sem Stroke: gradiente radial que só acende entre 0,78 e
        /// 1,0 do raio. Um traço com SolidColorBrush teria que ter a espessura
        /// animada junto com o tamanho, e no desenho final (0,14 px por unidade)
        /// um traço de 1 unidade vira 0,14 px - sumiria. O anel de verdade tem
        /// espessura GEOMÉTRICA, então sobrevive a qualquer escala.
        /// </summary>
        private static Brush RingBrush(Palette p)
        {
            var b = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.5),
                GradientOrigin = new Point(0.5, 0.5),
                RadiusX = 0.5,
                RadiusY = 0.5
            };
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0, p.Ring.R, p.Ring.G, p.Ring.B), 0.00));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0, p.Ring.R, p.Ring.G, p.Ring.B), 0.74));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(255, p.Ring.R, p.Ring.G, p.Ring.B), 0.92));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0, p.Ring.R, p.Ring.G, p.Ring.B), 1.00));
            b.Freeze();
            return b;
        }

        private static Brush FlashBrush(Color spec)
        {
            var b = new RadialGradientBrush
            {
                Center = new Point(0.34, 0.26),
                GradientOrigin = new Point(0.34, 0.26),
                RadiusX = 0.78,
                RadiusY = 0.82
            };
            b.GradientStops.Add(new GradientStop(Color.FromArgb(215, spec.R, spec.G, spec.B), 0.00));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(70, spec.R, spec.G, spec.B), 0.46));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0, spec.R, spec.G, spec.B), 1.00));
            b.Freeze();
            return b;
        }

        private static Brush SpecBrush(Color spec)
        {
            var b = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.5),
                GradientOrigin = new Point(0.5, 0.5),
                RadiusX = 0.5,
                RadiusY = 0.5
            };
            b.GradientStops.Add(new GradientStop(Color.FromArgb(230, spec.R, spec.G, spec.B), 0.00));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(90, spec.R, spec.G, spec.B), 0.52));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0, spec.R, spec.G, spec.B), 1.00));
            b.Freeze();
            return b;
        }

        /// <summary>
        /// Rim light mais forte na ponta do ápice e apagando na junção com o
        /// átrio: é onde a luz de recorte realmente pega numa esfera.
        /// </summary>
        private static Brush RimBrush(Palette p)
        {
            var b = new LinearGradientBrush
            {
                StartPoint = new Point(0.0, 1.0),
                EndPoint = new Point(1.0, 0.0)
            };
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0, p.Spec.R, p.Spec.G, p.Spec.B), 0.00));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(170, p.Spec.R, p.Spec.G, p.Spec.B), 0.34));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(60, p.Spec.R, p.Spec.G, p.Spec.B), 0.78));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0, p.Spec.R, p.Spec.G, p.Spec.B), 1.00));
            b.Freeze();
            return b;
        }

        private static SolidColorBrush Frozen(Color c, byte alpha) => Frozen(With(c, alpha));

        private static SolidColorBrush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private static Color With(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

        #endregion

        #region Ciclo de vida

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            ApplicationStateTracker.StateChanged += OnAppLifecycleChanged;
            _time.Restart();
            Resume("loaded");
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            ApplicationStateTracker.StateChanged -= OnAppLifecycleChanged;
            Suspend("unloaded");
        }

        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!IsVisible) Suspend("view hidden");
            else Resume("view shown");
        }

        private void OnAppLifecycleChanged(object? sender, ApplicationLifecycleStateChangedEventArgs e)
        {
            if (e.NewState == ApplicationLifecycleState.Foreground) Resume("lifecycle " + e.NewState);
            else Suspend("lifecycle " + e.NewState);
        }

        private void ApplyGate(bool enabled)
        {
            if (!_started) return;
            if (enabled) Resume("gate open");
            else Suspend("gate closed");
        }

        private void Resume(string reason)
        {
            if (!_started || !IsLoaded) return;
            if (!_suspended) return;
            _suspended = false;
            _time.Start();
            SetClockInterval(IntervalBeatMs);
            _clock?.Start();
            LogDebug($"[HealthIcon] retomado ({reason}) | estado={State}");
        }

        private void Suspend(string reason)
        {
            if (!_started || _suspended) return;
            _suspended = true;
            _clock?.Stop();
            _time.Stop();
            FreezePose();
            LogDebug($"[HealthIcon] suspenso ({reason}) | fase preservada={_phase:0.00}");
        }

        /// <summary>
        /// Congela no ponto atual. Não zera a fase: uma suspensão não pode
        /// reiniciar o batimento, senão cada alt-tab daria um salto no miocárdio.
        /// </summary>
        private void FreezePose()
        {
            double c = Contraction(_phase) * BeatGain();
            double pos = Math.Max(0.0, c);

            BeatScale.ScaleX = 1.0 - 0.058 * c;
            BeatScale.ScaleY = 1.0 + 0.105 * c;
            BeatRotate.Angle = 1.15 * c;
            Aura.Opacity = 0.16 + 0.42 * Math.Pow(pos, 1.5);
            AuraScale.ScaleX = AuraScale.ScaleY = 1.06 + 0.16 * pos;
            Flash.Opacity = 0.30 * pos * pos * pos;
            Specular.Opacity = 0.42 + 0.34 * pos;
            Specular2.Opacity = 0.30 + 0.26 * pos;
            GrooveHi.Opacity = 0.30 + 0.55 * pos;
            RimLight.Opacity = 0.55 + 0.45 * pos;
            HoverScale.ScaleX = HoverScale.ScaleY = 1.0 + 0.07 * _hoverT;
            ShockA.Opacity = 0;
            ShockB.Opacity = 0;
            ApplyFissure(pos);
        }

        #endregion

        #region Modelo do ciclo cardíaco

        /// <summary>
        /// O CICLO, em fase normalizada (0..1 = sístole + diastole). É a
        /// a CONSTRUTORA do movimento: cada batimento percorre a MESMA curva,
        /// então o coração é periódico e parece vivo em vez de tremer.
        ///
        /// Os quatro termos, na ordem em que um coração real os produz:
        ///   - SÍSTOLE VENTRICULAR (0,16): o "LUB". A válvula aórtica abre, o
        ///     miocárdio se contrai. Subida rápida (sigma 0,042) e relaxamento
        ///     mais demorado (0,078) — a contração é um golpe, o relaxamento é
        ///     um retorno.
        ///   - RECOIL ELÁSTICO (0,32): negativo de propósito. O ventrículo volta
        ///     ALÉM do repouso antes de relaxar, e é esse overshoot que dá a
        ///     "clique" da segunda batida.
        ///   - ONDA DICRÓTICA (0,46): o "DUB". Fechamento da válvula aórtica,
        ///     cerca de 0,3 s depois do primeiro som. Menor (0,40) e mais largo.
        ///     Deliberadamente NÃO é uma cópia do LUB: se fossem iguais, o
        ///     batimento leria como um pulso duplo mecânico.
        ///   - SÍSTOLE AURICULAR (0,90): a onda "a" do ECG, que fecha o ciclo em
        ///     1,0 e faz a curva retomar sem degrau.
        /// </summary>
        private static double Contraction(double p)
        {
            double lub = Bell(p, 0.160, 0.042, 0.078) * 1.00;
            double recoil = Bell(p, 0.315, 0.050, 0.070) * -0.22;
            double dub = Bell(p, 0.455, 0.062, 0.085) * 0.40;
            double atrial = Bell(p, 0.905, 0.055, 0.050) * 0.11;
            return lub + recoil + dub + atrial;
        }

        /// <summary>
        /// Sino assimétrico: sigma diferente antes e depois do pico. Um gaussiano
        /// simétrico não modela contração muscular — contração é rápida e
        /// relaxamento é lento, e é essa assimetria que o olho lê como
        /// "orgânico".
        /// </summary>
        private static double Bell(double p, double mu, double sPre, double sPost)
        {
            double d = p - mu;
            double s = d < 0 ? sPre : sPost;
            double u = d / s;
            return Math.Exp(-0.5 * u * u);
        }

        /// <summary>
        /// Ganho do batimento. A EXTRASÍSTOLE (batimento prematuro) chega cedo
        /// e fraca: é a contração que sai antes da hora, e por isso não atinge
        /// a força máxima. Em Warning, uma a cada 9.
        /// </summary>
        private double BeatGain() =>
            (_hasExtrasystole && _beat % PrematureEvery == PrematureEvery - 3) ? 0.55 : 1.0;

        /// <summary>
        /// Duração do próximo ciclo. Três desvios, todos clínicos:
        ///   - variação aleatória ±Irregular (nunca aleatória de verdade: hash
        ///     determinístico do índice, sem alocar Random por quadro);
        ///   - a extrasíxtole ENCURTA o ciclo (o coração dispara antes);
        ///   - o ciclo seguinte é alongado em 46% (a PAUSA COMPENSATÓRIA, que é
        ///     a assinatura do batimento prematuro — sem ela, a arritmia vira só
        ///     "velocidade variável" e perde a coerência).
        /// </summary>
        private double CycleForBeat(int b)
        {
            double c = 60.0 / _bpm;
            if (_irregular > 0) c *= 1.0 + (Hash01(b) - 0.5) * 2.0 * _irregular;
            if (_hasExtrasystole)
            {
                int m = b % PrematureEvery;
                if (m == PrematureEvery - 3) c *= 0.74;
                else if (m == PrematureEvery - 2) c *= 1.46;
            }
            return c;
        }

        /// <summary>
        /// Ruído determinístico em 0..1. Math.Sin do índice bate sempre igual,
        /// então a irregularidade é estável entre quadros e entre execuções - um
        /// Random por batimento faria o coração "re-sortear" o ritmo a cada tique.
        /// </summary>
        private static double Hash01(int n)
        {
            double x = Math.Sin(n * 127.1) * 43758.5453;
            return x - Math.Floor(x);
        }

        /// <summary>
        /// Fase relativa dentro de uma janela do ciclo. Devolve 0..1 se a fase
        /// estiver na janela, e -1 fora - o que permite usar o valor direto como
        /// opacidade - e o -1 significa "fora da tela".
        /// </summary>
        private static double Window(double p, double start, double len)
        {
            double d = p - start;
            if (d < 0) d += 1.0;
            return d < len ? d / len : -1.0;
        }

        #endregion

        #region Relógio adaptativo

        private void SetClockInterval(int ms)
        {
            if (ms == _intervalMs) return;
            _intervalMs = ms;
            if (_clock != null && _clock.IsEnabled)
                _clock.Interval = TimeSpan.FromMilliseconds(ms);
        }

        /// <summary>
        /// 30fps durante a sístole e enquanto há anel de choque na tela; 11fps na
        /// diastole. Com 62bpm o coração passa ~80% do tempo em diastole, então
        /// isso corta o custo para cerca de um terço sem perda visível: o que
        /// importa é a suavidade do GOLPE, não do repouso.
        /// </summary>
        private void ChooseInterval()
        {
            double c = Contraction(_phase);
            bool active = c > 0.04 || c < -0.02
                || Window(_phase, ShockAStart, ShockALen) >= 0
                || Window(_phase, ShockBStart, ShockBLen) >= 0
                || _hoverT > 0.01;
            SetClockInterval(active ? IntervalBeatMs : IntervalRestMs);
        }

        #endregion

        #region Tique

        private void OnTick(object? sender, EventArgs e)
        {
            if (_suspended) return;

            try
            {
                double t = _time.Elapsed.TotalSeconds;
                double dt = _now <= 0 ? 0.033 : t - _now;
                _now = t;
                if (dt <= 0) return;
                if (dt > 0.30) dt = 0.033;

                // Avança a fase. O ciclo é o do batimento CORRENTE; quando a fase
                // vira 1,0 o próximo batimento entra e a duração é recalculada.
                _phase += dt / _cycle;
                if (_phase >= 1.0)
                {
                    _phase -= 1.0;
                    _beat++;
                    _cycle = CycleForBeat(_beat);
                }

                StepBeat(dt);
                ChooseInterval();
            }
            catch (Exception ex)
            {
                if (_renderErrors++ < 3)
                    LogError("[HealthIcon] erro no quadro; animacoes suspensas por seguranca", ex);
                Suspend("render error");
            }
        }

        private void StepBeat(double dt)
        {
            double c = Contraction(_phase) * BeatGain();

            // FIBRILAÇÃO: tremor de ~10 Hz somado à contração. Só em Critical.
            // A amplitude é minúscula (0,016) porque fibrilação real é uma
            // vibração fina — o que a torna crível é a FREQUÊNCIA, não a
            // amplitude. Um tremor grande pareceria "coração tremendo de medo".
            double tremor = _tremor * Math.Sin(_now * 61.0);
            double v = c + tremor;

            // SQUASH AND STRETCH em torno da BASE: o ventrículo encurta na
            // ejeção e alarga perpendicularmente (o volume de um miocárdio é
            // quase incompressível). 0,105 contra 0,058 não é a razão exata de
            // preservação de volume - a razão seria 0,09 - e nem deve ser: com
            // a razão exata o coração fica achatado demais para 20px.
            BeatScale.ScaleY = 1.0 + 0.105 * v;
            BeatScale.ScaleX = 1.0 - 0.058 * v;

            // O ápex também VARRE: o ventrículo não só encurta, ele gira um
            // pouco em torno da base. Sem essa rotação o batimento é simétrico
            // demais para um órgão assimétrico.
            BeatRotate.Angle = 1.15 * v + 0.6 * _tremor * Math.Sin(_now * 43.0);

            double pos = Math.Max(0.0, c);

            Aura.Opacity = 0.16 + 0.42 * Math.Pow(pos, 1.5);
            AuraScale.ScaleX = AuraScale.ScaleY = 1.06 + 0.16 * pos;

            // Clarão CÚBICO: tem de ser ^3, e não ^1, porque o clarão só pode
            // existir no instante da ejeção. Com expoente baixo ele apareceria
            // durante o relaxamento também, e aí seria "brilho piscando" - de
            // volta exatamente ao defeito do ícone antigo.
            Flash.Opacity = 0.30 * pos * pos * pos;
            Specular.Opacity = 0.42 + 0.34 * pos;
            Specular2.Opacity = 0.30 + 0.26 * pos;

            // A crista do sulco acende JUNTO com a sístole. Animar a OPACIDADE
            // (e não a espessura) é deliberado: StrokeThickness muda os limites
            // de render do Path e portanto provoca um passe de layout - dentro
            // de um Viewbox, isso re-mede a árvore inteira. Opacidade não.
            GrooveHi.Opacity = 0.30 + 0.55 * pos;
            RimLight.Opacity = 0.55 + 0.45 * pos;


            StepShock(ShockA, ShockAScale, Window(_phase, ShockAStart, ShockALen), 0.58);
            StepShock(ShockB, ShockBScale, Window(_phase, ShockBStart, ShockBLen), 0.30);

            ApplyFissure(pos);

            // Hover: o coração "acorda". 7% é o máximo - acima disso ele
            // estouraria o tile de 32px e pareceria um bug de layout.
            _hoverT = Approach(_hoverT, _hover ? 1.0 : 0.0, dt, 0.10);
            double h = 1.0 + 0.07 * _hoverT;
            HoverScale.ScaleX = HoverScale.ScaleY = h;
            Aura.Opacity += 0.10 * _hoverT;
        }

        private void StepShock(FrameworkElement ring, ScaleTransform scale, double p, double peak)
        {
            if (p < 0) { ring.Opacity = 0; return; }

            // Nasce instantaneamente e morre em expoente > 1: um anel de choque
            // que "nascesse" devagar pareceria um segundo coraçãozinho
            // inflando, não uma onda saindo do miocárdio.
            scale.ScaleX = scale.ScaleY = 0.94 + 0.48 * p;
            ring.Opacity = peak * (1.0 + 0.25 * _hoverT) * Math.Pow(1.0 - p, 1.7);
        }

        /// <summary>
        /// A fenda é PERMANENTE e tem TRÊS camadas, porque um rasgo só escuro
        /// sobre um coração escuro não existe visualmente - e o estado Crítico
        /// precisa ser lido NO REPOUSO, não só no pico de cada sístole.
        ///
        ///   Gap   - quase preto, largo. É o RASGO: separa o miocárdio em dois.
        ///   Glow  - cor do estado, larga. Brasa na fenda, sempre acesa.
        ///   Hot   - núcleo claro e fino. O interior incandescente.
        ///
        /// As duas últimas pulsam com a sístole; a primeira não. Se o brilho
        /// inteiro pulsasse, o "coração partido" só existiria 20% do tempo - e
        /// um diagnóstico que some entre batidas não é um diagnóstico.
        /// </summary>
        private void ApplyFissure(double pos)
        {
            if (!_hasFissure)
            {
                CrackGap.Opacity = 0;
                CrackHot.Opacity = 0;
                CrackHotGlow.Opacity = 0;
                return;
            }
            CrackGap.Opacity = 0.85;
            CrackHotGlow.Opacity = 0.22 + 0.50 * Math.Pow(pos, 1.5);
            CrackHot.Opacity = 0.40 + 0.60 * Math.Pow(pos, 1.6);
        }

        #endregion

        #region Hover

        private bool _hover;

        protected override void OnMouseEnter(MouseEventArgs e)
        {
            base.OnMouseEnter(e);
            if (_hover) return;
            _hover = true;
            ChooseInterval();
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            if (!_hover) return;
            _hover = false;
            ChooseInterval();
        }

        #endregion

        #region Utilitários

        private static double Approach(double cur, double target, double dt, double tau)
        {
            if (Math.Abs(target - cur) < 0.0006) return target;
            return cur + (target - cur) * (1.0 - Math.Exp(-dt / Math.Max(0.001, tau)));
        }

        // Um log nunca pode derrubar a animação: se o getter do ServiceLocator
        // lançar, a catch engole. Mesmo princípio do AiFaceView.
        private ILoggingService? _log;
        private bool _logResolved;

        private ILoggingService? Log
        {
            get
            {
                if (_logResolved) return _log;
                _logResolved = true;
                try { _log = ServiceLocator.Logger; }
                catch { _log = null; }
                return _log;
            }
        }

        private void LogInfo(string m) { try { Log?.LogInfo(m); } catch { } }
        private void LogDebug(string m) { try { Log?.LogDebug(m, nameof(HealthHeartIcon)); } catch { } }
        private void LogError(string m, Exception ex) { try { Log?.LogError(m, ex); } catch { } }

        #endregion
    }
}
