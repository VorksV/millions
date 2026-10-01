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
    /// Ícone de LICENÇA do Dashboard: um escudo de metal que reage ao plano
    /// realmente ativo.
    ///
    /// POR QUE UM CONTROLE E NÃO UM PATH: o ícone antigo era um escudo com
    /// gradiente diagonal e um glifo branco, parado. Gradiente diagonal fixo não
    /// é metal - METAL É UM MATERIAL CUJO REFLEXO SE MOVE. Aqui o brilho é
    /// uma faixa que atravessa a face do escudo e volta, duas vezes em
    /// frequências diferentes, e o escudo inteiro balança de leve. É a
    /// diferença entre um adesivo e um objeto.
    ///
    /// O QUE CADA ESTADO FAZ, além da cor:
    ///   Inactive   - aço sem cor, sheen quase parado (25 s) e brilho fraco. O
    ///                plano inexistente não pode ter presença.
    ///   Standard   - aço ciano, sheen de 6,5 s e o visto se DESENHA traço a
    ///                traço (StrokeDashOffset) na troca de estado.
    ///   Pro        - aço magenta, sheen de 5 s, estrela entrando com MOLA
    ///                ( overshoot a 1,18 e assentamento) e duas faíscas
    ///                cintilando fora de fase.
    ///   Enterprise - aço âmbar, sheen de 5,6 s e duas camadas que sobem em
    ///                CASCATA, a de trás 90 ms depois da da frente.
    ///
    /// CUSTO (mesmo critério do AiFaceView e do HealthHeartIcon):
    ///   - UM DispatcherTimer de período ADAPTATIVO: 30fps com sheen ativo e
    ///     14fps no repouso.
    ///   - O sheen é o Transform do BRUSH, não um overlay com máscara: como o
    ///     brush é o preenchimento do Path do escudo, a faixa de luz nasce
    ///     recortada na silhueta de graça. Zero Clip, zero Geometry extra.
    ///   - Geometria e pincelaria construídos UMA vez e congelados. O laço
    ///     quente só mexe em Angle/Opacity/Translate.
    ///   - Nenhum Effect, BlurEffect, Bitmap, shader ou DropShadow aqui dentro.
    /// </summary>
    public partial class LicenseShieldIcon : UserControl
    {
        #region Dependency Properties

        public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
            nameof(State), typeof(LicenseIconState), typeof(LicenseShieldIcon),
            new FrameworkPropertyMetadata(LicenseIconState.Inactive, OnStatePropertyChanged));

        /// <summary>Plano realmente ativo. Vem do LicenseTokenStore.</summary>
        public LicenseIconState State
        {
            get => (LicenseIconState)GetValue(StateProperty);
            set => SetValue(StateProperty, value);
        }

        public static readonly DependencyProperty AnimationsEnabledProperty = DependencyProperty.Register(
            nameof(AnimationsEnabled), typeof(bool), typeof(LicenseShieldIcon),
            new FrameworkPropertyMetadata(true, OnAnimationsEnabledChanged));

        /// <summary>Portão herdado do Dashboard (<c>ShouldAnimationsRun</c>).</summary>
        public bool AnimationsEnabled
        {
            get => (bool)GetValue(AnimationsEnabledProperty);
            set => SetValue(AnimationsEnabledProperty, value);
        }

        private static void OnStatePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LicenseShieldIcon v) v.ApplyState((LicenseIconState)e.NewValue);
        }

        private static void OnAnimationsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LicenseShieldIcon v) v.ApplyGate((bool)e.NewValue);
        }

        #endregion

        #region Constantes de ritmo

        /// <summary>30fps. Só com sheen correndo ou glifo entrando.</summary>
        private const int IntervalActiveMs = 33;

        /// <summary>14fps. Repouso: só o balanço, que é lento e não pede mais.</summary>
        private const int IntervalRestMs = 70;

        /// <summary>Período do sheen por estado, em segundos.</summary>
        private const double SheenInactive = 9.0;
        private const double SheenStandard = 6.5;
        private const double SheenPro = 5.0;
        private const double SheenEnterprise = 5.6;

        /// <summary>Duração da entrada de cada glifo, em segundos.</summary>
        private const double GlyphDraw = 0.42;
        private const double GlyphSpring = 0.52;
        private const double GlyphCascade = 0.34;

        /// <summary>Deslocamento da cascata entre as camadas do Enterprise.</summary>
        private const double CascadeLag = 0.09;

        #endregion

        #region Estado

        private DispatcherTimer? _clock;
        private readonly Stopwatch _time = new();
        private bool _started;
        private bool _suspended = true;
        private int _intervalMs = IntervalRestMs;

        private double _now;
        private double _sheenPhase;
        private int _sheenDir = 1;
        private double _sweepPeriod = SheenInactive;

        /// <summary>0..1 da entrada do glifo do estado atual.</summary>
        private double _glyph;

        private double _hoverT;
        private bool _hover;
        private int _renderErrors;

        private Palette _palette = null!;
        private float _checkLen;

        #endregion

        public LicenseShieldIcon()
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

            // _started é a trava que separa "o construtor ainda está montando as
            // coisas" de "o controle existe e pode reagir a eventos". Sem ela,
            // um Loaded disparado durante o InitializeComponent chamaria Resume
            // com _palette ainda nulo.
            _started = true;
        }

        #region Geometria e pincelaria estática

        /// <summary>
        /// Escudo de aquecedor: ombros largos e arredondados, flancos que
        /// descem quase verticais e uma ponta em bico. Proporção 100x100 -
        /// ligeiramente larga, que é a proporção que continua legível a 20px
        /// (um escudo alto e estreito vira uma lista vertical de pixels).
        /// </summary>
        private static Geometry BuildShield()
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(50, 3.5), true, true);
                c.BezierTo(new Point(63, 3.5), new Point(77, 8.0), new Point(87, 13.5), true, false);
                c.BezierTo(new Point(88, 33), new Point(87, 54), new Point(81, 69), true, false);
                c.BezierTo(new Point(75, 83), new Point(63, 93), new Point(50, 97.5), true, false);
                c.BezierTo(new Point(37, 93), new Point(25, 83), new Point(19, 69), true, false);
                c.BezierTo(new Point(13, 54), new Point(12, 33), new Point(13, 13.5), true, false);
                c.BezierTo(new Point(23, 8.0), new Point(37, 3.5), new Point(50, 3.5), true, false);
            }
            g.Freeze();
            return g;
        }

        /// <summary>
        /// Face interna do escudo, recuada em 2,2 unidades. O recuo é
        /// PROPORCIONAL ao longo de toda a curva (e não um offset fixo) para
        /// que a espessura do chanfro fique constante - inclusive na ponta,
        /// que é onde um recuo fixo adelgaçaria o bisel até sumir.
        /// </summary>
        private static Geometry BuildFace()
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(50, 5.7), true, true);
                c.BezierTo(new Point(61.5, 6.2), new Point(74.5, 10.0), new Point(83.6, 15.0), true, false);
                c.BezierTo(new Point(84.5, 31), new Point(83.6, 50), new Point(78.0, 64.0), true, false);
                c.BezierTo(new Point(72.3, 76.5), new Point(61.5, 86.5), new Point(50, 91.0), true, false);
                c.BezierTo(new Point(38.5, 86.5), new Point(27.7, 76.5), new Point(22.0, 64.0), true, false);
                c.BezierTo(new Point(16.4, 50), new Point(15.5, 31), new Point(16.4, 15.0), true, false);
                c.BezierTo(new Point(25.5, 10.0), new Point(38.5, 6.2), new Point(50, 5.7), true, false);
            }
            g.Freeze();
            return g;
        }

        /// <summary>Aresta do chanfro: a silhueta, para o par claro/escuro.</summary>
        private static Geometry BuildGroove() => BuildShield();

        /// <summary>Glifo INACTIVE: um traço horizontal, com um eco acima.</summary>
        private static Geometry BuildDash()
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(38, 50), false, false);
                c.LineTo(new Point(62, 50), true, false);
            }
            g.Freeze();
            return g;
        }

        /// <summary>Glifo STANDARD: o visto, como polilinha (sem suavizar - a curva já é o traço).</summary>
        private static Geometry BuildCheck()
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(33, 51), false, false);
                c.LineTo(new Point(44, 62), true, false);
                c.LineTo(new Point(68, 38), true, false);
            }
            g.Freeze();
            return g;
        }

        /// <summary>Glifo PRO: estrela de 5 pontas, ponta para cima, circumscrevida no raio 20.</summary>
        private static Geometry BuildStar()
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                const double cx = 50, cy = 50, r = 21, rIn = 8.9;
                for (int i = 0; i < 10; i++)
                {
                    double a = -Math.PI / 2 + i * Math.PI / 5;
                    double rad = (i % 2 == 0) ? r : rIn;
                    double x = cx + Math.Cos(a) * rad;
                    double y = cy + Math.Sin(a) * rad;
                    if (i == 0) c.BeginFigure(new Point(x, y), true, true);
                    else c.LineTo(new Point(x, y), true, false);
                }
            }
            g.Freeze();
            return g;
        }

        /// <summary>Faísca de 4 pontas (estrela "brilhante"), usada ao redor da estrela do Pro.</summary>
        private static Geometry BuildSpark(double cx, double cy, double r, double rIn)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                for (int i = 0; i < 8; i++)
                {
                    double a = -Math.PI / 2 + i * Math.PI / 4;
                    double rad = (i % 2 == 0) ? r : rIn;
                    double x = cx + Math.Cos(a) * rad;
                    double y = cy + Math.Sin(a) * rad;
                    if (i == 0) c.BeginFigure(new Point(x, y), true, true);
                    else c.LineTo(new Point(x, y), true, false);
                }
            }
            g.Freeze();
            return g;
        }

        /// <summary>Glifo ENTERPRISE: duas lajes em perspectiva, uma atrás da outra.</summary>
        private static Geometry BuildTier(double y, double w, double h)
        {
            double x = 50 - w / 2;
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(x, y + h * 0.42), true, true);
                c.LineTo(new Point(x + w / 2, y), true, false);
                c.LineTo(new Point(x + w, y + h * 0.42), true, false);
                c.LineTo(new Point(x + w, y + h * 0.78), true, false);
                c.LineTo(new Point(x + w / 2, y + h * 0.36), true, false);
                c.LineTo(new Point(x, y + h * 0.78), true, false);
            }
            g.Freeze();
            return g;
        }

        private void BuildStaticVisuals()
        {
            Geometry shield = BuildShield();
            Shell.Data = shield;
            SheenBroad.Data = shield;
            SheenSharp.Data = shield;
            Aura.Data = shield;
            EngraveLight.Data = BuildGroove();
            TopGlint.Data = BuildTopGlint();

            Face.Data = BuildFace();
            Check.Data = BuildCheck();
            CheckGlow.Data = BuildCheck();
            Star.Data = BuildStar();
            StarGlow.Data = BuildStar();
            Spark1.Data = BuildSpark(22, 24, 7.0, 1.9);
            Spark2.Data = BuildSpark(78, 72, 5.0, 1.4);
            // PILHA ISOMÉTRICA: a laje de trás é MAIS LARGA e fica MAIS BAIXO, de
            // modo que ela aparece dos dois lados da da frente. Duas lajes do
            // mesmo tamanho, empilhadas, lêem como UMA barra inclinada - a
            // diferença de largura é o que faz o olho reconstruir o volume.
            TierFront.Data = BuildTier(36, 34, 15);
            TierBack.Data = BuildTier(50, 46, 15);
            DashMain.Data = BuildDash();
            DashGhost.Data = BuildDash();

            // ESPESSURAS DE TRAÇO. O palco é 140 unidades dentro de um ícone de
            // 20px, ou seja 0,143 px por unidade - a espessura default do WPF
            // (1,0) daria 0,14 px de traço e simplesmente não apareceria. Todos
            // os traços de detalhe foram dimensionados para CAIR entre 0,4 e
            // 0,8 px depois dessa conversão.
            EngraveLight.StrokeThickness = 3.0;
            TopGlint.StrokeThickness = 2.4;
            Check.StrokeThickness = 4.2;
            CheckGlow.StrokeThickness = 7.5;
            DashMain.StrokeThickness = 5.0;
            DashGhost.StrokeThickness = 2.2;

            Check.StrokeStartLineCap = PenLineCap.Round;
            Check.StrokeEndLineCap = PenLineCap.Round;
            Check.StrokeLineJoin = PenLineJoin.Round;
            CheckGlow.StrokeStartLineCap = PenLineCap.Round;
            CheckGlow.StrokeEndLineCap = PenLineCap.Round;
            CheckGlow.StrokeLineJoin = PenLineJoin.Round;
            DashMain.StrokeStartLineCap = PenLineCap.Round;
            DashMain.StrokeEndLineCap = PenLineCap.Round;
            DashGhost.StrokeStartLineCap = PenLineCap.Round;
            DashGhost.StrokeEndLineCap = PenLineCap.Round;
            EngraveLight.StrokeStartLineCap = PenLineCap.Round;
            EngraveLight.StrokeEndLineCap = PenLineCap.Round;
            TopGlint.StrokeStartLineCap = PenLineCap.Round;
            TopGlint.StrokeEndLineCap = PenLineCap.Round;

            // Comprimento do tracejado: o "visto" se desenhando é o
            // StrokeDashOffset indo do comprimento do traço até zero, e para
            // isso o array de tracejado precisa ter o tamanho CERTO - se for
            // maior, o traço some; se for menor, ele se repete.
            _checkLen = (float)PolylineLength(BuildCheck());
            Check.StrokeDashArray = new DoubleCollection(new double[] { _checkLen });
            CheckGlow.StrokeDashArray = new DoubleCollection(new double[] { _checkLen });
        }

        /// <summary>
        /// Comprimento do traço do visto. Soma das cordas de uma polilinha de 2
        /// segmentos - o caminho É uma polilinha reta de propósito, então a
        /// soma das cordas é o comprimento exato, sem تقريبação.
        /// </summary>
        private static double PolylineLength(Geometry geo)
        {
            _ = geo;
            double ax = 33, ay = 51, bx = 44, by = 62, cx = 68, cy = 38;
            return Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay))
                 + Math.Sqrt((cx - bx) * (cx - bx) + (cy - by) * (cy - by));
        }

        /// <summary>
        /// Arco do rebordo superior: só o terço de cima da aresta. Um traço na
        /// silhueta inteira acenderia a ponta inferior, e a luz de um objeto
        /// inclinado NÃO chega embaixo.
        /// </summary>
        private static Geometry BuildTopGlint()
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(13, 13.5), false, false);
                c.BezierTo(new Point(23, 8.0), new Point(37, 3.5), new Point(50, 3.5), true, false);
                c.BezierTo(new Point(63, 3.5), new Point(77, 8.0), new Point(87, 13.5), true, false);
            }
            g.Freeze();
            return g;
        }

        #endregion

        #region Paletas

        private sealed class Palette
        {
            public Color FaceHi, FaceMid, FaceLo;
            public Color ShellHi, ShellLo;
            public Color Aura, Sheen, Glyph, GlyphGlow;
            public double SheenPeriod;
            public double SheenGain;
        }

        private static Palette PaletteFor(LicenseIconState s) => s switch
        {
            LicenseIconState.Standard => new Palette
            {
                FaceHi = Color.FromRgb(0x7D, 0xE8, 0xFF),
                FaceMid = Color.FromRgb(0x1E, 0xA8, 0xE0),
                FaceLo = Color.FromRgb(0x1B, 0x4F, 0xA8),
                ShellHi = Color.FromRgb(0xBF, 0xEE, 0xFF),
                ShellLo = Color.FromRgb(0x0A, 0x2C, 0x4E),
                Aura = Color.FromRgb(0x38, 0xBD, 0xF8),
                Sheen = Color.FromRgb(0xE8, 0xFA, 0xFF),
                Glyph = Color.FromRgb(0xF2, 0xFD, 0xFF),
                GlyphGlow = Color.FromRgb(0x38, 0xBD, 0xF8),
                SheenPeriod = SheenStandard,
                SheenGain = 1.0
            },

            LicenseIconState.Pro => new Palette
            {
                FaceHi = Color.FromRgb(0xFF, 0xC4, 0xF3),
                FaceMid = Color.FromRgb(0xD0, 0x3C, 0xE8),
                FaceLo = Color.FromRgb(0x7A, 0x14, 0xA8),
                ShellHi = Color.FromRgb(0xFF, 0xE0, 0xFA),
                ShellLo = Color.FromRgb(0x3A, 0x06, 0x52),
                Aura = Color.FromRgb(0xD9, 0x46, 0xEF),
                Sheen = Color.FromRgb(0xFF, 0xF0, 0xFC),
                Glyph = Color.FromRgb(0xFF, 0xFF, 0xFF),
                GlyphGlow = Color.FromRgb(0xEC, 0x48, 0x99),
                SheenPeriod = SheenPro,
                SheenGain = 1.12
            },

            LicenseIconState.Enterprise => new Palette
            {
                FaceHi = Color.FromRgb(0xFF, 0xE3, 0xA8),
                FaceMid = Color.FromRgb(0xE0, 0x8A, 0x18),
                FaceLo = Color.FromRgb(0x8A, 0x33, 0x06),
                ShellHi = Color.FromRgb(0xFF, 0xF3, 0xD6),
                ShellLo = Color.FromRgb(0x40, 0x18, 0x02),
                Aura = Color.FromRgb(0xF5, 0x9E, 0x0B),
                Sheen = Color.FromRgb(0xFF, 0xF8, 0xE8),
                Glyph = Color.FromRgb(0xFF, 0xFF, 0xFF),
                GlyphGlow = Color.FromRgb(0xF5, 0x9E, 0x0B),
                SheenPeriod = SheenEnterprise,
                SheenGain = 1.05
            },

            // INACTIVE - aço AZUL-FRIO, e não cinza.
            //
            // A primeira versão deste paleta era cinza neutro com sheen de 25 s
            // a 30% de ganho, na ideia de que "plano inexistente não pode ter
            // presença de marca". O resultado na tela foi o oposto do
            // pretendido: um escudo morto ao lado de um coração batendo lê como
            // CONTROLE QUEBRADO, não como ausência de licença - e o cartão de
            // licença fica no topo direito do Dashboard o tempo todo, então o
            // usuário via aquilo em toda visita.
            //
            // O que separa o inativo dos planos pagos não é a ausência de cor,
            // é a DESATURAÇÃO e a LENTIDÃO: o azul é frio e de baixa saturação,
            // e o sheen é o mais demorado (9 s contra 5 a 6,5 s). Continua
            // claramente "menos" que um plano, mas continua VIVO.
            _ => new Palette
            {
                FaceHi = Color.FromRgb(0xA8, 0xC4, 0xE8),
                FaceMid = Color.FromRgb(0x4A, 0x6E, 0x9E),
                FaceLo = Color.FromRgb(0x1E, 0x2E, 0x4A),
                ShellHi = Color.FromRgb(0x9C, 0xBC, 0xE4),
                ShellLo = Color.FromRgb(0x0C, 0x14, 0x24),
                Aura = Color.FromRgb(0x3B, 0x6E, 0xA8),
                Sheen = Color.FromRgb(0xDA, 0xEA, 0xFF),
                Glyph = Color.FromRgb(0xB4, 0xCC, 0xE8),
                GlyphGlow = Color.FromRgb(0x4A, 0x7A, 0xB0),
                SheenPeriod = SheenInactive,
                SheenGain = 0.62
            }
        };

        private void ApplyState(LicenseIconState state)
        {
            _palette = PaletteFor(state);
            _sweepPeriod = _palette.SheenPeriod;

            Shell.Fill = ShellBrush(_palette);
            Face.Fill = FaceBrush(_palette);
            Aura.Fill = Frozen(_palette.Aura, 52);
            SheenBroad.Fill = SheenBrush(_palette, wide: true);
            SheenSharp.Fill = SheenBrush(_palette, wide: false);
            EngraveLight.Stroke = Frozen(_palette.ShellHi, 90);
            TopGlint.Stroke = Frozen(_palette.FaceHi, 120);
            Check.Stroke = Frozen(_palette.Glyph);
            CheckGlow.Stroke = Frozen(_palette.GlyphGlow, 150);
            Star.Fill = Frozen(_palette.Glyph);
            StarGlow.Fill = Frozen(_palette.GlyphGlow, 120);
            Spark1.Fill = Frozen(_palette.Glyph);
            Spark2.Fill = Frozen(_palette.Glyph, 210);
            TierFront.Fill = Frozen(_palette.Glyph);
            TierBack.Fill = Frozen(_palette.GlyphGlow, 190);
            DashMain.Stroke = Frozen(_palette.Glyph, 235);
            DashGhost.Stroke = Frozen(_palette.ShellHi, 110);

            // A entrada do glifo RECOMEÇA. Um corte de um frame para o outro é
            // a coisa que mais denuncia um ícone estático num dashboard.
            _glyph = 0.0;
            _sheenPhase = 0.0;
            _sheenDir = 1;
            _now = 0.0;
            _time.Restart();

            LogInfo($"[LicenseIcon] estado={state} | sheen={_sweepPeriod:0.0}s");
        }

        private static Brush FaceBrush(Palette p)
        {
            var b = new LinearGradientBrush
            {
                StartPoint = new Point(0.14, 0.02),
                EndPoint = new Point(0.88, 1.00)
            };
            b.GradientStops.Add(new GradientStop(p.FaceHi, 0.00));
            b.GradientStops.Add(new GradientStop(p.FaceMid, 0.46));
            b.GradientStops.Add(new GradientStop(p.FaceLo, 1.00));
            b.Freeze();
            return b;
        }

        /// <summary>
        /// A casca é o BISEL. Ela é mais clara no alto (a luz chega) e quase
        /// preta embaixo (a face interna projeta sombra sobre ela) - é esse par
        /// que faz o escudo ter espessura em vez de ser um recorte.
        /// </summary>
        private static Brush ShellBrush(Palette p)
        {
            var b = new LinearGradientBrush
            {
                StartPoint = new Point(0.20, 0.00),
                EndPoint = new Point(0.80, 1.00)
            };
            b.GradientStops.Add(new GradientStop(p.ShellHi, 0.00));
            b.GradientStops.Add(new GradientStop(With(p.ShellLo, 255), 0.52));
            b.GradientStops.Add(new GradientStop(p.ShellLo, 1.00));
            b.Freeze();
            return b;
        }

        /// <summary>
        /// A faixa de luz. DUAS faixas, com larguras e alphas diferentes, para
        /// o reflexo ter núcleo E halo - reflexo de metal anisotrópico é
        /// alongado e tem borda difusa, não é um ponto.
        ///
        /// O gradiente é DIAGONAL (0,0 -> 1,1) para a faixa atravessar a
        /// silhueta em vez de escorrer por um lado só.
        ///
        /// ESTE BRUSH NÃO É CONGELADO, e é o único do arquivo que não é. Congelar
        /// um Freezable congela também o seu Transform - e o Transform É o que
        /// se anima a cada quadro. Um brush congelado aqui daria InvalidOperation
        /// no primeiro tique. As GradientStops não são mais mexidas depois de
        /// criadas, então não há custo relevante em manter o brush vivo.
        ///
        /// E o truque que evita Clip: como o brush é o PREENCHIMENTO do próprio
        /// Path do escudo, a faixa de luz nasce recortada na silhueta de graça.
        /// Nenhuma máscara, nenhuma geometria extra, nenhum retângulo de corte.
        /// </summary>
        private static Brush SheenBrush(Palette p, bool wide)
        {
            var b = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1)
            };
            byte a = (byte)(wide ? 90 : 210);
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0, p.Sheen.R, p.Sheen.G, p.Sheen.B), 0.00));
            if (wide)
            {
                b.GradientStops.Add(new GradientStop(Color.FromArgb(0, p.Sheen.R, p.Sheen.G, p.Sheen.B), 0.26));
                b.GradientStops.Add(new GradientStop(Color.FromArgb(a, p.Sheen.R, p.Sheen.G, p.Sheen.B), 0.50));
                b.GradientStops.Add(new GradientStop(Color.FromArgb(0, p.Sheen.R, p.Sheen.G, p.Sheen.B), 0.74));
                b.GradientStops.Add(new GradientStop(Color.FromArgb(0, p.Sheen.R, p.Sheen.G, p.Sheen.B), 1.00));
            }
            else
            {
                b.GradientStops.Add(new GradientStop(Color.FromArgb(0, p.Sheen.R, p.Sheen.G, p.Sheen.B), 0.40));
                b.GradientStops.Add(new GradientStop(Color.FromArgb(a, p.Sheen.R, p.Sheen.G, p.Sheen.B), 0.50));
                b.GradientStops.Add(new GradientStop(Color.FromArgb(0, p.Sheen.R, p.Sheen.G, p.Sheen.B), 0.60));
            }
            b.Transform = new TranslateTransform();
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
            SetClockInterval(IntervalActiveMs);
            _clock?.Start();
            LogDebug($"[LicenseIcon] retomado ({reason}) | estado={State}");
        }

        private void Suspend(string reason)
        {
            if (!_started || _suspended) return;
            _suspended = true;
            _clock?.Stop();
            _time.Stop();
            FreezePose();
            LogDebug($"[LicenseIcon] suspenso ({reason})");
        }

        /// <summary>
        /// Congela no ponto atual - inclusive com o glifo PARCIALMENTE desenhado.
        /// Zerar a entrada do glifo a cada alt-tab faria o escudo recomeçar a
        /// animacão de ativação toda vez que o usuário voltasse para o app.
        /// </summary>
        private void FreezePose()
        {
            SwayRotate.Angle = 1.5 * Math.Sin(_now * 0.55);
            SwaySkew.AngleX = 2.2 * Math.Sin(_now * 0.37 + 1.1);
            SwayFloat.Y = -3.2 * Math.Sin(_now * 0.9);
            double breath = 1.0 + 0.014 * Math.Sin(_now * 1.43);
            SwayScale.ScaleX = breath;
            SwayScale.ScaleY = breath;
            Aura.Opacity = 0.20 + 0.08 * Math.Sin(_now * 1.4);
            ApplyGlyph(_glyph);
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

        private void ChooseInterval()
        {
            bool active = _glyph < 1.0 || _hoverT > 0.01 || State != LicenseIconState.Inactive;
            SetClockInterval(active ? IntervalActiveMs : IntervalRestMs);
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

                StepSheen(dt);
                StepSway();
                StepGlyph(dt);
                StepHover(dt);
                ChooseInterval();
            }
            catch (Exception ex)
            {
                if (_renderErrors++ < 3)
                    LogError("[LicenseIcon] erro no quadro; animacoes suspensas por seguranca", ex);
                Suspend("render error");
            }
        }

        /// <summary>
        /// A varredura. Ela ATRAVESSA e VOLTA, em vez de dar a volta num laço:
        /// um laço tem emenda, e a emenda é o ponto onde o olho pega o metal
        /// "teleportando" de um lado para o outro. Uma ida-e-volta é também o
        /// que a luz faz de verdade quando a fonte se move devagar.
        ///
        /// As duas faixas andam juntas, com a estreita um pouco adiantada - o
        /// núcleo do reflexo chega antes do halo, como numa fonte luminosa.
        /// </summary>
        private void StepSheen(double dt)
        {
            double rate = 1.0 / Math.Max(0.5, _sweepPeriod) * (1.0 + 0.6 * _hoverT);
            _sheenPhase += dt * rate;
            if (_sheenPhase >= 1.0)
            {
                _sheenPhase -= 1.0;
                _sheenDir = -_sheenDir;
            }

            double s = _sheenPhase;
            double x = _sheenDir > 0
                ? (s * 2.9 - 1.45)
                : (1.45 - s * 2.9);

            double gain = _palette.SheenGain * (1.0 + 0.35 * _hoverT);
            SetSheenX(SheenBroad, x);
            SetSheenX(SheenSharp, x + 0.14);
            SheenBroad.Opacity = 0.50 * gain;
            SheenSharp.Opacity = 0.70 * gain;

            // A aura respira com o MESMO curso do sheen: o brilho de fundo
            // adianta um pouco em relação à faixa, que é como um difusor
            // reage à fonte.
            Aura.Opacity = (0.20 + 0.09 * Math.Sin(_now * 1.4)) * gain;
            AuraScale.ScaleX = AuraScale.ScaleY = 1.12 + 0.05 * Math.Sin(_now * 1.4);
        }

        private static void SetSheenX(Path target, double x)
        {
            if (target.Fill is LinearGradientBrush lg && lg.Transform is TranslateTransform tt)
                tt.X = x;
        }

        /// <summary>
        /// O balanço. Quatro frequências diferentes e NENHUMA em fase com a
        /// outra - é o que impede o movimento de parecer um laço. A rotação e a
        /// torção defasadas entre si simulam o escudo girando muito devagar
        /// enquanto a fonte de luz se move: a "luz andando sobre metal" não
        /// precisa de sheen para existir, só precisa de uma superfície que não
        /// esteja parada.
        ///
        /// A ESCALA é o que dá a este ícone a mesma presença viva do coração.
        /// Sem ela o escudo fica dependente só do reflexo, e o reflexo é
        ///belos mas sutil demais para 20px - o usuário precisa ver QUE algo está
        /// respirando ali, não só concluir olhando duas vezes.
        /// </summary>
        private void StepSway()
        {
            SwayRotate.Angle = 1.5 * Math.Sin(_now * 0.55);
            SwaySkew.AngleX = 2.2 * Math.Sin(_now * 0.37 + 1.1);
            SwayFloat.Y = -3.2 * Math.Sin(_now * 0.9);

            // Respiração de 4,4 s, ±1,4%. Lenta o bastante para não ser
            // "bouncing" e rápida o bastante para o olho pegar ogoing quando
            // olha de relance.
            double breath = 1.0 + 0.014 * Math.Sin(_now * 1.43);
            SwayScale.ScaleX = breath;
            SwayScale.ScaleY = breath;
        }

        /// <summary>Entrada e a vida de cada glifo, conforme o plano.</summary>
        private void StepGlyph(double dt)
        {
            if (_glyph < 1.0)
            {
                double dur = State switch
                {
                    LicenseIconState.Standard => GlyphDraw,
                    LicenseIconState.Pro => GlyphSpring,
                    LicenseIconState.Enterprise => GlyphCascade,
                    _ => 0.30
                };
                _glyph = Math.Min(1.0, _glyph + dt / dur);
            }
            ApplyGlyph(_glyph);

            // As faíscas do Pro cintilam em duas frequências PRÓXIMAS (7,3 e
            // 6,1 Hz) e não iguais: duas faíscas na mesma frequência piscariam
            // juntas e o par pareceria um único ponto piscando.
            if (State == LicenseIconState.Pro)
            {
                Spark1.Opacity = 0.30 + 0.70 * Math.Pow(0.5 + 0.5 * Math.Sin(_now * 7.3), 2.0);
                Spark2.Opacity = 0.20 + 0.60 * Math.Pow(0.5 + 0.5 * Math.Sin(_now * 6.1 + 2.2), 2.0);
            }
        }

        private void ApplyGlyph(double t)
        {
            bool active = State != LicenseIconState.Inactive;

            GlyphInactive.Opacity = active ? 0 : 1;
            GlyphStandard.Opacity = State == LicenseIconState.Standard ? 1 : 0;
            GlyphPro.Opacity = State == LicenseIconState.Pro ? 1 : 0;
            GlyphEnterprise.Opacity = State == LicenseIconState.Enterprise ? 1 : 0;

            switch (State)
            {
                case LicenseIconState.Inactive:
                {
                    // O traço RESPIRA. Ele é o único sinal de vida de um plano
                    // que não existe, então deixá-lo estático faria o cartão
                    // inteiro parecer travado. A respiração é sincronizada com
                    // o curso do sheen (0,18 Hz, o mesmo slow-breathe do
                    // SwayFloat) e não com o batimento cardíaco: o escudo não
                    // tem coração, e imitar a pulsação dele seria um truque
                    // barato que dissolve a diferença entre os dois cartões.
                    // Só a OPACIDADE respira. Animar StrokeThickness pareceria
                    // ainda melhor, mas StrokeThickness muda os limites de
                    // render do Path e portanto provoca um passe de layout -
                    // e dentro de um Viewbox isso re-mede a árvore inteira a
                    // cada quadro. Opacidade não.
                    double b = 0.5 + 0.5 * Math.Sin(_now * 1.13);
                    DashMain.Opacity = 0.62 + 0.38 * b;
                    DashGhost.Opacity = 0.18 + 0.42 * b;
                    break;
                }

                case LicenseIconState.Standard:
                {
                    // O visto se DESENHA: o tracejado anda de um comprimento
                    // igual ao traço (medido no load) até zero. Efeito
                    // stroke-dashoffset - o desenho do traço surgindo, sem
                    // revelar por máscara e sem trocar de geometria.
                    double d = EaseOutCubic(t);
                    Check.StrokeDashOffset = _checkLen * (1.0 - d);
                    CheckGlow.StrokeDashOffset = _checkLen * (1.0 - d);
                    CheckGlow.Opacity = 0.55 * d;
                    break;
                }

                case LicenseIconState.Pro:
                {
                    // MOLA com overshoot: entra rápido, passa de 1,18 e assenta.
                    // É o gesto de "estrela aparecendo" - um ease-out puro
                    // chegaria em 1,0 e PARARIA, e parar em 1,0 é a marca de
                    // animação que foi desenhada por alguém que não testou.
                    double s = Spring(t);
                    StarScale.ScaleX = StarScale.ScaleY = s;
                    StarGlow.Opacity = 0.62 * Math.Min(1.0, t * 2.2);
                    break;
                }

                case LicenseIconState.Enterprise:
                {
                    // Cascata: a laje da frente sobe primeiro, a de trás 90 ms
                    // depois. O mesmo intervalo de um integrador de cadência -
                    // o par só existe como par quando chega junto.
                    double f = EaseOutCubic(Clamp01(t / 0.68));
                    double b = EaseOutCubic(Clamp01((t - CascadeLag / GlyphCascade) / 0.68));
                    TierFront.Opacity = 0.25 + 0.75 * f;
                    TierBack.Opacity = 0.20 + 0.60 * b;
                    break;
                }
            }
        }

        /// <summary>
        /// EaseOutBack normalizado: começa em 0 EXATAMENTE, passa de 1 no meio
        /// (overshoot de ~20%) e assenta em 1 exatamente.
        ///
        /// O motivo de não ser um ease-out comum: um ease-out chega em 1,0 e
        /// PARA. Parar em 1,0 é a assinatura de animação feita por quem não
        /// olhou rodando - o objeto "bate" na parede em vez de pousar nela. A
        /// mola é o que dá a entrada da estrela a sensação de energia.
        /// </summary>
        private static double Spring(double t)
        {
            if (t <= 0) return 0;
            if (t >= 1) return 1;
            const double c1 = 2.6, c3 = 3.6;
            double x = t - 1.0;
            return 1.0 + c3 * x * x * x + c1 * x * x;
        }


        private static double EaseOutCubic(double t)
        {
            if (t <= 0) return 0;
            if (t >= 1) return 1;
            double x = 1.0 - t;
            return 1.0 - x * x * x;
        }

        private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);

        private void StepHover(double dt)
        {
            _hoverT = Approach(_hoverT, _hover ? 1.0 : 0.0, dt, 0.11);
            double h = 1.0 + 0.08 * _hoverT;
            HoverScale.ScaleX = HoverScale.ScaleY = h;
        }

        #endregion

        #region Hover

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
        private void LogDebug(string m) { try { Log?.LogDebug(m, nameof(LicenseShieldIcon)); } catch { } }
        private void LogError(string m, Exception ex) { try { Log?.LogError(m, ex); } catch { } }

        #endregion
    }
}
