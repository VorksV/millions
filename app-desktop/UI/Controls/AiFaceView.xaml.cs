using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using VoltrisOptimizer.Core;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.UI.Converters;
using VoltrisOptimizer.UI.ViewModels;

namespace VoltrisOptimizer.UI.Controls
{
    /// <summary>
    /// A representacao visual do VOLTRIS BRAIN.
    ///
    /// O QUE ESTE CONTROLE NAO E: nao e uma IA. Nao decide, nao mede hardware,
    /// nao deduce humor, nao cria contexto. Ele recebe um
    /// <see cref="BrainMood"/> - que o <c>DashboardViewModel</c> derivou de
    /// dados REAIS do <c>VoltrisBrainV2</c> - e o desenha. A fonte da verdade
    /// continua sendo o Brain; aqui so existe a camada de representacao.
    ///
    /// O QUE ELE E: um rosto. Expressoes compostas por numeros
    /// (<see cref="AiFaceExpression"/>), geometria derivada desses numeros
    /// (<see cref="AiFaceGeometry"/>), olhos com piscada organica, olhar que
    /// segue o mouse, lagrima e um icone de raio no hover.
    ///
    /// CUSTO (item 9 do pedido - obrigatorio num OTIMIZADOR):
    ///   - UM DispatcherTimer com periodo ADAPTATIVO de 33 a 133ms. A versao
    ///     anterior travava em 16ms (62,5 tiques/s) mesmo com o rosto parado.
    ///     Agora um rosto em repouso e amostrado ~7x/s, o que e imperceptivel
    ///     para uma respiracao de 3,4s e economiza 88% das amostras.
    ///   - Geometria so e reconstruida quando um parametro de FORMA muda de
    ///     verdade (epsILON 0,004). Em repouso: zero alocacoes por quadro.
    ///   - Pincel e gradiente sao cacheados por cor QUANTIZADA. Uma transicao
    ///     de humor pode pedir 30 cores por segundo e nenhuma alocacao
    ///     acontece depois das 256 primeiras.
    ///   - Iris, pupila, bochecha, auras e lente sao Ellipse com Transform:
    ///     resolvido na GPU, sem passar por measure/arrange.
    ///   - Nenhum Effect, BlurEffect, Bitmap, Video, WebView ou shader.
    ///   - Suspende por EVENTO (ciclo de vida), nunca por polling.
    /// </summary>
    public partial class AiFaceView : UserControl
    {
        #region Dependency Properties

        public static readonly DependencyProperty MoodProperty = DependencyProperty.Register(
            nameof(Mood), typeof(BrainMood), typeof(AiFaceView),
            new FrameworkPropertyMetadata(BrainMood.Idle, OnMoodPropertyChanged));

        /// <summary>Estado visual derivado do Brain. Entrada UNICA do humor.</summary>
        public BrainMood Mood
        {
            get => (BrainMood)GetValue(MoodProperty);
            set => SetValue(MoodProperty, value);
        }

        public static readonly DependencyProperty AnimationsEnabledProperty = DependencyProperty.Register(
            nameof(AnimationsEnabled), typeof(bool), typeof(AiFaceView),
            new FrameworkPropertyMetadata(true, OnAnimationsEnabledChanged));

        /// <summary>
        /// Portao herdado do Dashboard (<c>ShouldAnimationsRun</c>). Quando
        /// false, o relogio para e o rosto congela no ponto em que estava.
        /// </summary>
        public bool AnimationsEnabled
        {
            get => (bool)GetValue(AnimationsEnabledProperty);
            set => SetValue(AnimationsEnabledProperty, value);
        }

        public static readonly DependencyProperty IsHoverArmedProperty = DependencyProperty.Register(
            nameof(IsHoverArmed), typeof(bool), typeof(AiFaceView),
            new FrameworkPropertyMetadata(false, OnIsHoverArmedChanged));

        /// <summary>
        /// Portao de hover EXTERNO. O botao circular do Dashboard tem 290px e o
        /// rosto so 104px no centro: sem este portao, o icone de raio so aparecia
        /// quando o mouse estava em cima DA TESTA. Ligado, o hover pertence ao
        /// botao inteiro - o mouseleave do rosto nao desliga nada, porque quem
        /// desliga e o botao.
        /// </summary>
        public bool IsHoverArmed
        {
            get => (bool)GetValue(IsHoverArmedProperty);
            set => SetValue(IsHoverArmedProperty, value);
        }

        public static readonly DependencyProperty KeepAnimatingInBackgroundProperty = DependencyProperty.Register(
            nameof(KeepAnimatingInBackground), typeof(bool), typeof(AiFaceView),
            new FrameworkPropertyMetadata(false, OnKeepAnimatingInBackgroundChanged));

        /// <summary>
        /// [FIX:FACE-EFFORT] Amplifica o VIVACIDADE do movimento, sem mudar a
        /// expressão.
        ///
        /// O que é: um multiplicador aplicado a tremor, amplitude e frequência
        /// da respiração. 1.0 = expressão como o Brain a definiu. Valores acima
        /// de 1.0 fazem o rosto se mexer mais e mais rápido.
        ///
        /// POR QUE EXISTE, E POR QUE NÃO É UM NOVO PRESET
        /// ==================================================
        /// O pedido foi "Furious, mas mais expressivo e mais movido — trabalhando
        /// a milão". A primeira tentativa de AUMENTAR os parâmetros do preset
        /// Furious em <c>AiFaceExpression.For</c>, e isso foi RECUSADO por
        /// defeito: <c>BrainMood.Furious</c> não é exclusivo do botão circular. Ele
        /// também é o estado de SOBRECARGA TÉRMICA do VoltrisBrainV2, e aumentar
        /// o tremor dele mudaria o alerta térmico sem ninguém pedir.
        ///
        /// A separação correta é entre a FORMA do rosto (a expressão, que pertence
        /// ao Brain e não se mexe) e a INTENSIDADE do movimento (que pertence à
        /// cena que está pedindo o rosto). Um multiplicador separa as duas sem
        /// encostar no preset e sem inventar um BrainMood novo.
        /// </summary>
        public static readonly DependencyProperty EffortBoostProperty = DependencyProperty.Register(
            nameof(EffortBoost), typeof(double), typeof(AiFaceView),
            new FrameworkPropertyMetadata(1.0));

        /// <summary>Multiplicador de vivacidade do movimento. 1.0 = normal.</summary>
        public double EffortBoost
        {
            get => (double)GetValue(EffortBoostProperty);
            set => SetValue(EffortBoostProperty, value);
        }

        /// <summary>
        /// Faz o rosto continuar a Piscar, respirar e fazer o micro-reação MESMO
        /// com a janela em segundo plano.
        ///
        /// O padrão é <c>false</c> (o rosto suspende quando o app perde o
        /// foreground), e é o que o botão circular do Dashboard usa — lá a
        /// suspensão é desejada, porque ninguém está vendo e o gasto de CPU é
        /// puro desperdício.
        ///
        /// A tela de Perfil Inteligente liga esta flag porque o Vorlok IA é o
        /// elemento central daquela página: ele precisa continuar "vivo" como
        /// prova de que a IA está ativa, mesmo com o app atrás de outro
        /// programa. A-flag só afeta o ciclo de vida do app; esconder a janela
        /// (minimizar) continua suspendendo, porque ai nao ha o que desenhar.
        /// </summary>
        public bool KeepAnimatingInBackground
        {
            get => (bool)GetValue(KeepAnimatingInBackgroundProperty);
            set => SetValue(KeepAnimatingInBackgroundProperty, value);
        }

        private static void OnKeepAnimatingInBackgroundChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AiFaceView v) v.ApplyAnimationGate(v.AnimationsEnabled);
        }

        private static void OnMoodPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AiFaceView v) v.OnMoodChanged((BrainMood)e.OldValue, (BrainMood)e.NewValue);
        }

        private static void OnIsHoverArmedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AiFaceView v) v.SyncHover("botao");
        }

        private static void OnAnimationsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AiFaceView v) v.ApplyAnimationGate((bool)e.NewValue);
        }

        #endregion

        #region Constantes de ritmo

        /// <summary>Alvo de 30fps. So entra em cena em transicao, piscada ou estado energetico.</summary>
        private const int IntervalActiveMs = 33;

        /// <summary>Alvo de 22fps. Estado de trabalho calmo.</summary>
        private const int IntervalSteadyMs = 45;

        /// <summary>Alvo de 10fps. Repouso: uma respiracao de 3,4s ainda tem 34 amostras por ciclo.</summary>
        private const int IntervalCalmMs = 100;

        /// <summary>Alvo de 7,5fps. Repouso profundo sem piscada proxima.</summary>
        private const int IntervalHibernateMs = 133;

        /// <summary>Se uma piscada vence em menos que isto, acelera para nao corta-la.</summary>
        private const double BlinkLookahead = 0.45;

        /// <summary>Tempo caracteristico da transicao de expressao (~390ms para assentar).</summary>
        private const double BlendTau = 0.13;

        /// <summary>Epsilon de reconstrucao de geometria. Abaixo disto, o desenho nao muda.</summary>
        private const double GeoEpsilon = 0.004;

        /// <summary>Cor interpolada so e reaplicada quando muda este tanto (0..255 por canal).</summary>
        private const byte ColorEpsilon = 2;

        private const double BlinkCloseSec = 0.058;
        private const double BlinkHoldSec = 0.022;
        private const double BlinkOpenSec = 0.115;

        private const double MouseGazeFreshSec = 1.55;

        private const double EyeCxL = AiFaceGeometry.FaceCx - AiFaceGeometry.EyeOffsetX;
        private const double EyeCxR = AiFaceGeometry.FaceCx + AiFaceGeometry.EyeOffsetX;

        #endregion

        #region Estado de execucao

        private DispatcherTimer? _clock;
        private readonly Stopwatch _time = new();
        private readonly Random _rnd = new();
        private Window? _host;

        private bool _started;
        private bool _suspended = true;
        private int _intervalMs = IntervalActiveMs;

        private double _now;
        private double _breathPhase;

        // ── transicao de expressao ──
        private AiFaceExpression _from;
        private AiFaceExpression _to;
        private AiFaceExpression _cur = AiFaceExpression.Neutral;
        private BrainMood _moodFrom = BrainMood.Idle;
        private BrainMood _moodTo = BrainMood.Idle;
        private double _blend;
        private bool _blending;

        // ── assinatura de geometria (rebuild sob demanda) ──
        private ShapeSignature _sig;
        private bool _geoDirty = true;

        // ── cor ──
        private Color _color = Color.FromRgb(0x31, 0xA8, 0xFF);
        private Color _appliedColor = Color.FromArgb(0, 0, 0, 0);
        private SolidColorBrush _solid;
        private SolidColorBrush _soft;
        private SolidColorBrush _faint;
        private SolidColorBrush _socket;
        private SolidColorBrush _mouthFill;

        // ── piscada ──
        private readonly EyeBlink _blinkL = new() { NextAt = 0.9 };
        private readonly EyeBlink _blinkR = new() { NextAt = 2.6 };
        private bool _blinkActive;

        // ── olhar ──
        private double _gazeX, _gazeY;
        private double _mouseX, _mouseY, _mouseAt = -999;
        private double _sacX, _sacY, _sacAt;

        // ── hover: rosto -> raio ──
        // _hover     = estado EFETIVO (ja resolvido: OU das duas fontes)
        // _hoverSelf = mouse em cima do rosto; IsHoverArmed = mouse no botao
        private bool _hoverSelf;
        private bool _hover;
        private double _hoverT;

        // ── reacao a troca de humor ──
        private double _punchT = 1.0;
        private double _kick;

        private DateTime _lastLogAt = DateTime.MinValue;
        private int _renderErrors;

        private Geometry _tearGeoL = null!;
        private Geometry _tearGeoR = null!;

        #endregion

        public AiFaceView()
        {
            InitializeComponent();

            FaceField.Data = AiFaceGeometry.FacePlate;
            FaceContour.Data = AiFaceGeometry.FacePlate;
            Nose.Data = AiFaceGeometry.Nose;
            BoltBody.Data = AiFaceGeometry.Bolt;
            BoltCore.Data = AiFaceGeometry.BoltCore;
            BoltOutline.Data = AiFaceGeometry.Bolt;

            // Geometria da lagrima: fixa. O codigo so a MOVE. Se fosse
            // reconstruida por quadro seria uma alocacao a 30Hz durante o
            // choro - que e justamente quando o rosto ja esta no limite.
            _tearGeoL = AiFaceGeometry.TearDrop(EyeCxL + 1.7, 48.4, 1.55);
            _tearGeoR = AiFaceGeometry.TearDrop(EyeCxR - 1.7, 48.4, 1.55);
            TearL.Data = _tearGeoL;
            TearR.Data = _tearGeoR;

            _from = AiFaceExpressionLibrary.For(Mood);
            _to = _from;
            _cur = _from;
            _moodFrom = Mood;
            _moodTo = Mood;

            ScheduleBlinks(0.0);

            _clock = new DispatcherTimer(DispatcherPriority.Render, Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(IntervalActiveMs)
            };
            _clock.Tick += OnTick;

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            IsVisibleChanged += OnIsVisibleChanged;

            _solid = Brush(Color.FromArgb(255, 255, 255, 255));
            _soft = Brush(Color.FromArgb(120, 255, 255, 255));
            _faint = Brush(Color.FromArgb(46, 255, 255, 255));
            _socket = Brush(Color.FromRgb(0x06, 0x0A, 0x14));
            _mouthFill = Brush(Color.FromRgb(0x05, 0x08, 0x10));

            _started = true;
            ApplyColor(force: true);
            RebuildGeometry(force: true);
            FreezePose();
        }

        #region Ciclo de vida e suspensao

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _host = Window.GetWindow(this);
            if (_host != null)
                _host.MouseMove += OnHostMouseMove;

            _time.Restart();
            _now = 0;
            _breathPhase = 0;
            _mouseAt = -999;

            // Evento JA EXISTENTE do ciclo de vida da aplicacao. Sem timer
            // proprio para "descobrir" se o usuario esta olhando: minimizado,
            // segundo plano e tray chegam aqui por evento.
            ApplicationStateTracker.StateChanged += OnAppLifecycleChanged;

            Resume("loaded");
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            Suspend("unloaded");
            ApplicationStateTracker.StateChanged -= OnAppLifecycleChanged;

            if (_host != null)
            {
                _host.MouseMove -= OnHostMouseMove;
                _host = null;
            }
        }

        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!IsVisible) Suspend("view hidden");
            else Resume("view shown");
        }

        private void OnAppLifecycleChanged(object? sender, ApplicationLifecycleStateChangedEventArgs e)
        {
            // Unico lugar que decide se o rosto respira. Foreground = respira;
            // qualquer outro estado = congelado, sem relogio, sem alocacao.
            //
            // Excecao: KeepAnimatingInBackground. Na tela de Perfil Inteligente o
            // Vorlok IA e o elemento central e precisa continuar gesticulando com
            // a janela em segundo plano. O painel do Dashboard NAO liga a flag, e
            // continua congelando fora do foreground como sempre.
            if (e.NewState == ApplicationLifecycleState.Foreground)
            {
                Resume("lifecycle " + e.NewState);
                return;
            }

            if (KeepAnimatingInBackground && IsVisible)
            {
                // Só o ciclo de vida do app foi suspenso; a janela continua
                // visivel, entao o relogio continua. Nada de congelar a pose.
                LogInfo($"[AIFace] app em segundo plano, mas KeepAnimatingInBackground ligado — animacoes mantidas");
                return;
            }

            Suspend("lifecycle " + e.NewState);
        }

        private void ApplyAnimationGate(bool enabled)
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

            LogInfo($"[AIFace] animacoes retomadas ({reason}) | estado={_moodTo}");
        }

        private void Suspend(string reason)
        {
            if (!_started || _suspended) return;
            _suspended = true;

            _clock?.Stop();
            _time.Stop();
            FreezePose();

            LogInfo($"[AIFace] animacoes suspensas ({reason}) | estado preservado={_moodTo}");
        }

        /// <summary>
        /// Congela tudo no ponto atual. Deliberadamente NAO zera o olhar nem a
        /// abertura dos olhos: uma suspensao nao pode piscar o rosto, mas tambem
        /// nao pode mandar a cabeca voltar ao repouso a cada alt-tab.
        /// </summary>
        private void FreezePose()
        {
            Breath.ScaleX = Breath.ScaleY = 1.0;
            Tilt.Angle = 0;
            Bob.X = 0;
            Bob.Y = 0;
            AuraWide.Opacity = 0.08;
            AuraWideScale.ScaleX = AuraWideScale.ScaleY = 1.0;
            MotesCanvas.Opacity = 0;
            BoltPulse.Opacity = 0;
            BoltPulse2.Opacity = 0;
            BoltBloom.Opacity = 0;
            Ray1.Opacity = Ray2.Opacity = Ray3.Opacity = Ray4.Opacity = 0;
            ThinkDots.Opacity = 0;
            WorkBars.Opacity = 0;
            Bar1.Opacity = Bar2.Opacity = Bar3.Opacity = 0.25;
            AlertArc.Opacity = 0;
            BoltScale.ScaleX = BoltScale.ScaleY = 0.92;
            if (_hoverT > 0.001)
            {
                Root.Opacity = 1.0 - _hoverT;
                BoltLayer.Opacity = _hoverT;
                BoltBody.Opacity = _hoverT;
            }
        }

        #endregion

        #region Brain -> Visual State

        private void OnMoodChanged(BrainMood oldMood, BrainMood newMood)
        {
            if (oldMood == newMood) return;
            if (!_started) return;

            // O que esta na tela vira origem, no instante exato da troca. E por
            // isso que uma troca de humor DURANTE O HOVER nao perde nada: o
            // alvo e atualizado normalmente, so a exibicao esta oculta.
            _from = _cur;
            _moodFrom = oldMood;
            _to = AiFaceExpressionLibrary.For(newMood);
            _moodTo = newMood;
            _blend = 0.0;
            _blending = true;
            _geoDirty = true;

            // Micro-reacao: um soco curto de escala e um tranco de cabeca na
            // direcao emocional do humor novo. Sem isso a troca vira um snap e o
            // rosto para de parecer vivo.
            _punchT = 0.0;
            _kick = KickFor(newMood);

            // Piscada de reacao em 45% das trocas: e o detalhe que faz o rosto
            // REAGIR a mudanca em vez de trocar de expressao enquanto faz outra
            // coisa.
            if (_rnd.NextDouble() < 0.45)
                ScheduleBlinks(0.08 + _rnd.NextDouble() * 0.14);

            SetClockInterval(IntervalActiveMs);
            LogMood(oldMood, newMood);
        }

        /// <summary>Direcao do tranco de cabeca por humor. Pequeno de proposito.</summary>
        private static double KickFor(BrainMood mood) => mood switch
        {
            BrainMood.Happy => 0.9,
            BrainMood.Succeeded => 0.8,
            BrainMood.Satisfied => 0.6,
            BrainMood.Motivated => 0.7,
            BrainMood.Surprised => -1.0,
            BrainMood.Furious => -1.0,
            BrainMood.Frustrated => -0.7,
            BrainMood.Errored => -0.8,
            BrainMood.Sad => -0.6,
            BrainMood.Weeping => -0.5,
            BrainMood.Bored => -0.3,
            _ => 0.4
        };

        private void LogMood(BrainMood from, BrainMood to)
        {
            DateTime t = DateTime.UtcNow;
            bool rapid = (t - _lastLogAt).TotalSeconds < 2.0;
            _lastLogAt = t;

            string msg = $"[AIFace] BrainMood {from} -> {to}";
            if (rapid) LogDebug(msg + " (transicao rapida)");
            else LogInfo(msg);
        }

        #endregion

        #region Relogio adaptativo

        /// <summary>
        /// Decide o periodo do proximo tique. E o mecanismo de economia
        /// principal: um rosto em repouso nao justifica 60 amostras por segundo.
        /// So e decidido quando o ritmo muda - nunca a cada quadro.
        /// </summary>
        private void SetClockInterval(int ms)
        {
            if (ms == _intervalMs) return;
            _intervalMs = ms;
            if (_clock != null && _clock.IsEnabled)
                _clock.Interval = TimeSpan.FromMilliseconds(ms);
        }

        private void ChooseInterval()
        {
            // 1. Durante uma transicao a expressao tem que ser continua: e o
            //    unico momento em que 30fps e obrigatorio.
            if (_blending) { SetClockInterval(IntervalActiveMs); return; }

            // 2. Durante uma piscada - ou prestes a comecar uma. A palpebra e
            //    rapida demais para 10fps, senao pisca "em tres quadros".
            double nextBlink = Math.Min(_blinkL.NextAt, _blinkR.NextAt) - _now;
            if (_blinkActive || nextBlink < BlinkLookahead)
            {
                SetClockInterval(IntervalActiveMs);
                return;
            }

            // 3. No hover o unico movimento e o brilho do raio: 22fps e folgado.
            if (_hover) { SetClockInterval(IntervalActiveMs); return; }

            // 4. Repouso real. Respiracao de 3,4s a 7,5fps ainda tem 25 amostras
            //    por ciclo - liso demais para o olho, e 8x mais barato.
            if (IsCalm(_moodTo))
            {
                SetClockInterval(nextBlink > 1.2 ? IntervalHibernateMs : IntervalCalmMs);
                return;
            }

            SetClockInterval(IntervalSteadyMs);
        }

        private static bool IsCalm(BrainMood mood) =>
            mood == BrainMood.Sleeping || mood == BrainMood.Idle || mood == BrainMood.Bored;

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
                // Apos uma suspensao longa o primeiro quadro nao deve "pular"
                // a animacao inteira.
                if (dt > 0.25) dt = 0.033;

                ChooseInterval();
                StepBlend(dt);
                StepMotion(dt);
                StepGaze(dt);
                StepBlinks(dt);
                StepAura(dt);
                ApplyColor(force: false);
                StepAccents(dt);
                StepMotes();
                StepTears();
                StepHover(dt);
                StepReaction(dt);
            }
            catch (Exception ex)
            {
                // Erro de render nao pode virar laco de excecao a 30Hz.
                if (_renderErrors++ < 3)
                    LogError("[AIFace] erro no quadro; animacoes suspensas por seguranca", ex);
                _clock?.Stop();
                _suspended = true;
            }
        }

        private void StepBlend(double dt)
        {
            if (!_blending) return;

            _blend = Approach(_blend, 1.0, dt, BlendTau);
            _cur = AiFaceExpression.Lerp(_from, _to, _blend);

            if (_blend >= 0.9995)
            {
                _blend = 1.0;
                _blending = false;
                _cur = _to;
                _geoDirty = true;
            }
        }

        private void StepMotion(double dt)
        {
            // expressao corrente por copia: e um struct de 31 doubles, e o
            // compilador nao consegue provar que os campos nao mudam no meio
            _cur = _cur;

            double breathAmp = _cur.BreathAmp;
            double breathPeriod = _cur.BreathPeriod;
            double tremor = _cur.Tremor;

            // [FIX:FACE-EFFORT] Amplifica o movimento SEM tocar na expressão.
            //
            // Tremor e amplitude crescem; o PERÍODO da respiração é dividido, o
            // que faz a respiração ficar mais rápida (e não só maior) — é o que
            // faz a leitura ser "trabalhando a milão" em vez de "parado mas
            // irritado".
            //
            // O teto de 2.2 é proposital: acima disso o tremor começa a
            // embaralhar a leitura do rosto em vez de comunicar esforço, e o
            // usuário perceives como "bugando", não como Vivo.
            double effort = EffortBoost;
            if (double.IsNaN(effort) || effort < 0.1) effort = 1.0;
            if (effort > 2.2) effort = 2.2;

            if (effort != 1.0)
            {
                tremor *= effort;
                breathAmp *= effort;
                breathPeriod /= effort;
            }

            double headTilt = _cur.HeadTilt;

            double headLean = _cur.HeadLean;
            double headDrop = _cur.HeadDrop;
            double cheekOp = _cur.CheekOpacity;
            double cheekLift = _cur.CheekLift;
            double noseOp = _cur.NoseOpacity;
            double ringOp = _cur.IrisRing * 0.55;
            double irisS = _cur.IrisScale;
            double pupilS = _cur.PupilScale;
            double glint = 0.90 * _cur.GlintScale;

            // ── respiro ──
            _breathPhase += dt / Math.Max(0.35, breathPeriod);
            double wave = Math.Sin(_breathPhase * Math.PI * 2.0);

            double punch = Math.Sin(Math.PI * _punchT);
            double s = 1.0
                       + breathAmp * wave * (1.0 + 0.55 * _hoverT)
                       + 0.070 * punch
                       + _hoverT * 0.045;
            Breath.ScaleX = s;
            Breath.ScaleY = s;

            // ── inclinacao / tremor ──
            // Duas senoides de frequencia incommensuravel dao textura de tremor
            // sem virar um oscilador visivel (uma so denotaria "oscilador").
            double tilt = headTilt;
            if (tremor > 0.001)
                tilt += tremor * (Math.Sin(_now * 47.3) * 0.62 + Math.Sin(_now * 29.7 + 1.3) * 0.38);
            if (_punchT < 1.0)
                tilt += _kick * 3.4 * Math.Sin(Math.PI * _punchT);
            Tilt.Angle = tilt;

            // ── posicao: o arraste vem da RESPIRACAO, nao de uma segunda
            //    senoide independente. E o que faz cabeca e corpo se moverem
            //    como um so organismo. ──
            double drop = headDrop + breathAmp * 2.1 * wave;
            if (tremor > 0.001)
                drop += tremor * 0.9 * Math.Sin(_now * 53.1 + 0.7);
            if (_punchT < 1.0)
                drop -= _kick * 1.5 * Math.Sin(Math.PI * _punchT);
            Bob.Y = drop;
            Bob.X = headLean + tremor * 0.9 * Math.Sin(_now * 41.7 + 2.2);

            // ── detalhes do rosto ──
            CheekL.Opacity = cheekOp;
            CheekR.Opacity = cheekOp;
            CheekLiftL.Y = -cheekLift * 1.7;
            CheekLiftR.Y = -cheekLift * 1.7;
            Nose.Opacity = noseOp;

            IrisRingL.Opacity = ringOp;
            IrisRingR.Opacity = ringOp;

            IrisScaleL.ScaleX = IrisScaleL.ScaleY = irisS;
            IrisScaleR.ScaleX = IrisScaleR.ScaleY = irisS;
            PupilScaleL.ScaleX = PupilScaleL.ScaleY = pupilS;
            PupilScaleR.ScaleX = PupilScaleR.ScaleY = pupilS;
            GlintL.Opacity = glint;
            GlintR.Opacity = glint;

            if (_geoDirty) RebuildGeometry(force: false);
        }

        private void StepGaze(double dt)
        {
            double saccadeRate = _cur.SaccadeRate;
            double biasX = _cur.GazeBiasX;
            double biasY = _cur.GazeBiasY;
            double aperture = _cur.EyeAperture;

            // Saccade: micro-salto de olhar. E o que faz parecer que ela esta
            // olhando para ALGUMA COISA em vez de apenas encarando. O intervalo
            // e variavel e enviesado para o curto por construcao.
            if (saccadeRate > 0.02 && _now >= _sacAt)
            {
                double u = _rnd.NextDouble();
                _sacAt = _now + (1.35 + 0.85 * u + 1.15 * u * u) / Math.Max(0.12, saccadeRate);
                double ang = _rnd.NextDouble() * Math.PI * 2.0;
                double r = 0.5 + _rnd.NextDouble() * 1.55;
                _sacX = Math.Cos(ang) * r;
                _sacY = Math.Sin(ang) * r * 0.72;
            }

            double tx, ty;
            if (_now - _mouseAt < MouseGazeFreshSec)
            {
                tx = _mouseX;
                ty = _mouseY;
            }
            else
            {
                tx = biasX + _sacX;
                ty = biasY + _sacY;
            }

            // seguranca do item 13: a pupila NUNCA sai da esclera. O limite e
            // calculado a partir da abertura real do olho - num olho
            // arregalado a iris praticamente preenche a fenda, entao o alcance
            // do olhar encolhe sozinho. Sem isto, olhar para cima num Surprise
            // empurrava a iris para fora do rosto.
            double maxY = Math.Max(0.0, 6.3 - 4.6 * aperture);
            const double maxX = 2.2;
            tx = Clamp(tx, -maxX, maxX);
            ty = Clamp(ty, -maxY, maxY);

            const double tau = 0.055;   // sacada e balistica: rapida, nao deslizante
            double k = 1.0 - Math.Exp(-dt / tau);
            _gazeX += (tx - _gazeX) * k;
            _gazeY += (ty - _gazeY) * k;

            GazeL.X = _gazeX; GazeL.Y = _gazeY;
            GazeR.X = _gazeX; GazeR.Y = _gazeY;
        }

        private void OnHostMouseMove(object? sender, MouseEventArgs e)
        {
            if (_host == null || _suspended) return;

            try
            {
                var center = PointToScreen(new Point(ActualWidth * 0.5, ActualHeight * 0.5));
                var cursor = _host.PointToScreen(e.GetPosition(_host));

                double dx = cursor.X - center.X;
                double dy = cursor.Y - center.Y;
                double d = Math.Sqrt(dx * dx + dy * dy);
                if (d < 1.0) return;

                // Normalizado pela distancia: perto do rosto o olhar trava no
                // cursor, longe ele se aproxima do limite do olho.
                double k = Math.Min(1.0, d / 420.0) * 3.0;
                _mouseX = dx / d * k;
                _mouseY = dy / d * k;
                _mouseAt = _now;
            }
            catch
            {
                // PointToScreen lanca se o control ainda nao estiver no visual tree.
            }
        }

        #endregion

        #region Piscada organica

        private sealed class EyeBlink
        {
            public double NextAt;
            public int Phase;      // 0 aberto | 1 fechando | 2 fechado | 3 abrindo
            public double Pt;
            public double Close;   // 0 aberto .. 1 fechado
            public double Target = 1.0;
            public double Lead;    // adiantamento em segundos (assimetria)
            public bool DoubleNext;
        }

        /// <summary>
        /// Agenda as DUAS piscadas. Sao agendadas separadamente (o que permite
        /// a piscada assimetrica) mas a proxima janela e a mesma, entao nao
        /// viram dois metronomos.
        ///
        /// O intervalo NAO e fixo. A versao anterior usava
        /// <c>2.4 + random*3.6</c>, que e um random puro - e um random puro
        /// ainda parece maquina, porque a media e constante. Aqui o sorteio e
        /// enviesado para o curto (quadrado da uniforme), o que produz pausas
        /// longas e varias curtas, como gente de verdade.
        /// </summary>
        private void ScheduleBlinks(double delay)
        {
            double rate = Math.Max(0.15, _cur.BlinkRate);
            double gap = NextGap();
            double start = _now + delay;

            // 14% de chance de piscada ASSIMETRICA: um olho comeca ~40ms antes
            // do outro. Quase imperceptivel, e por isso mesmo funciona - e um
            // dos poucos detalhes que separam "piscada" de "vida".
            bool asym = _rnd.NextDouble() < 0.14;
            double lag = asym ? 0.025 + _rnd.NextDouble() * 0.050 : 0.0;

            _blinkL.NextAt = start;
            _blinkL.Lead = asym ? 0.0 : lag;
            _blinkL.DoubleNext = false;
            _blinkL.Target = RollIncompleteBlink();

            _blinkR.NextAt = start + lag;
            _blinkR.Lead = asym ? -lag : 0.0;
            _blinkR.DoubleNext = false;
            _blinkR.Target = RollIncompleteBlink();
        }

        private double NextGap()
        {
            double u = _rnd.NextDouble();
            double shaped = 0.42 + 1.35 * u * u;                 // 0.42..1.77
            double base0 = 1.90 + shaped * 2.40;                 // 2.9..6.1 s
            return base0 * (0.86 + _rnd.NextDouble() * 0.28) / Math.Max(0.15, _cur.BlinkRate);
        }

        /// <summary>18% de piscadas incompletas (fecha so metade e reabre).</summary>
        private double RollIncompleteBlink() =>
            _rnd.NextDouble() < 0.18 ? 0.40 + _rnd.NextDouble() * 0.22 : 1.0;

        private void StepBlinks(double dt)
        {
            StepBlink(_blinkL, dt);
            StepBlink(_blinkR, dt);

            bool any = _blinkL.Phase != 0 || _blinkR.Phase != 0;
            if (any == _blinkActive) return;

            _blinkActive = any;
            ChooseInterval();
        }

        private void StepBlink(EyeBlink b, double dt)
        {
            double t = _now + b.Lead;

            switch (b.Phase)
            {
                case 0:
                    b.Close = Approach(b.Close, 0.0, dt, 0.030);
                    if (t >= b.NextAt) { b.Phase = 1; b.Pt = 0; }
                    break;

                case 1: // fechando
                    b.Pt += dt / BlinkCloseSec;
                    if (b.Pt >= 1.0) { b.Pt = 1.0; b.Phase = 2; b.Close = b.Target; }
                    else b.Close = Smooth(b.Pt) * b.Target;
                    break;

                case 2: // fechado
                    b.Pt += dt / BlinkHoldSec;
                    if (b.Pt >= 1.0) { b.Pt = 0; b.Phase = 3; }
                    break;

                default: // abrindo
                    b.Pt += dt / BlinkOpenSec;
                    if (b.Pt < 1.0)
                    {
                        b.Close = b.Target * (1.0 - Smooth(b.Pt));
                        break;
                    }

                    b.Phase = 0;
                    b.Pt = 0;
                    b.Close = 0.0;

                    if (b.DoubleNext)
                    {
                        // Piscar duplo: ~180ms depois. E o que gente pensativa
                        // faz, e o detalhe que mais separa "piscada" de "vida".
                        b.DoubleNext = false;
                        b.NextAt = _now + 0.175 + _rnd.NextDouble() * 0.075;
                        b.Target = RollIncompleteBlink();
                    }
                    else
                    {
                        b.DoubleNext = _rnd.NextDouble() < 0.24;
                        b.NextAt = t + NextGap();
                    }
                    break;
            }
        }

        #endregion

        #region Hover: rosto -> raio (item 7)

        protected override void OnMouseEnter(MouseEventArgs e)
        {
            base.OnMouseEnter(e);
            if (_hoverSelf) return;
            _hoverSelf = true;
            SyncHover("rosto");
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            if (!_hoverSelf) return;
            _hoverSelf = false;
            SyncHover("rosto");
        }

        /// <summary>
        /// O hover e o OU de duas fontes: o mouse em cima do PROPRIO rosto e o
        /// portao <see cref="IsHoverArmed"/> do botao. Sair do rosto com o mouse
        /// ainda dentro do botao nao pode apagar o raio - por isso o estado
        /// efetivo so muda quando o OU muda.
        /// </summary>
        private void SyncHover(string origin)
        {
            bool on = _hoverSelf || IsHoverArmed;
            if (on == _hover) return;
            _hover = on;
            ChooseInterval();
            // O log carrega o estado do Brain de proposito: e a prova de que o
            // hover NAO resetou nada.
            LogInfo(on
                ? $"[AIFace] hover enter ({origin}) -> icone de raio | estado do Brain preservado={_moodTo}"
                : $"[AIFace] hover leave ({origin}) -> rosto | estado do Brain preservado={_moodTo}");
        }

        private void StepHover(double dt)
        {
            double target = _hover ? 1.0 : 0.0;
            bool settled = Math.Abs(_hoverT - target) < 0.0015;

            if (settled && _hoverT == target)
            {
                // O crossfade JA terminou. CUIDADO: aqui NAO se pode sair.
                // Um "return" neste ponto congelava bloom, arco e faiscas no
                // instante exato em que o hover completava - o rastro parava
                // de girar 300ms depois de aparecer e parecia elemento travado.
                // Os efeitos seguem rodando ate o mouse sair.
                if (_hoverT < 0.002) return;
                StepBoltFx(dt);
                return;
            }

            _hoverT = settled ? target : Approach(_hoverT, target, dt, 0.105);

            // Rosto e raio estao em camadas IRMAIS. Cruzar so a opacidade das
            // duas - e nao um Visibility - e o que faz o rosto se dissolver no
            // raio em vez de sumir e aparecer.
            Root.Opacity = 1.0 - _hoverT;
            BoltLayer.Opacity = _hoverT;
            BoltBody.Opacity = _hoverT;
            BoltCore.Opacity = _hoverT;
            BoltOutline.Opacity = _hoverT;
            // Raio MAIOR: 0,92 -> 1,12. O glifo ocupa 32,4..67,6 na malha, ou
            // seja +/-17,6 do centro; em 1,12 chega a +/-19,7 (30,3..69,7),
            // com folga larga para o bloom e as estrias. O crescimento de
            // 0,20 (era 0,14) e o que faz o hover ler como "carregando mais
            // forte", em vez de so trocar de icone.
            BoltScale.ScaleX = BoltScale.ScaleY = 0.92 + 0.20 * _hoverT;

            if (_hoverT < 0.002)
            {
                // Fora do hover o efeito some INTEIRO, para nao deixar pulsos e
                // estrias parados acesos por baixo do rosto.
                BoltPulse.Opacity = 0;
                BoltPulse2.Opacity = 0;
                BoltBloom.Opacity = 0;
                Ray1.Opacity = Ray2.Opacity = Ray3.Opacity = Ray4.Opacity = 0;
                return;
            }

            StepBoltFx(dt);
        }

        /// <summary>
        /// Efeitos do raio durante o hover: bloom, dois pulsos que EXPANDEM e
        /// quatro estrias de luz. Tudo por Transform e Opacity - nenhuma
        /// geometria e reconstruida, nenhum Effect. So enquanto o mouse esta
        /// em cima.
        ///
        /// NADA aqui descreve um circulo solido pelo botao. O arco que girava
        /// e as 3 faiscas redondas foram removidos de proposito: liam como
        /// "uma bola dando a volta". No lugar, o pulso nasce do raio e cresce,
        /// e as estrias sao filetes lineares que viajam para fora.
        /// </summary>
        private void StepBoltFx(double dt)
        {
            // ── bloom: respira em 1,9s, um pouco mais rapido que a cabeca, para
            //    nao parecer que belong ao mesmo ciclo (le como "carregando") ──
            double breathe = 0.5 + 0.5 * Math.Sin(_now * Math.PI * 2.0 / 1.9);
            BoltBloom.Opacity = _hoverT * (0.30 + 0.34 * breathe);
            BoltBloomScale.ScaleX = BoltBloomScale.ScaleY = 0.92 + 0.14 * breathe;

            // ── pulsos: dois anéis que CRESCEM a partir do raio e somem, com
            //    180 graus de defasagem entre si. Nenhum objeto percorre o
            //    contorno: a energia nasce pequena, se abre e morre. O segundo
            //    pulso e mais fraco e um pouco mais lento, o que da
            //    profundidade sem virar "dois circulos competindo".
            //
            //    endScale MAIOR que 0,70 e o que faz o anel EXPANDIR. O
            //    pulso 1 vai a 1,48: 64 x 1,48 = 94,7, ou seja 2,6..97,4 na
            //    malha 0..100 - encosta na borda sem cortar. O 2 vai a 1,30
            //    (83,2 -> 8,4..91,6), menor e mais interno, o que da a
            //    sensacao de profundidade. ──
            Pulse(BoltPulse, BoltPulseScale, 0.00, 0.62, 1.55, 1.48);
            Pulse(BoltPulse2, BoltPulse2Scale, 0.50, 0.46, 1.95, 1.30);

            // ── estrias: 4 filetes lineares em angulos opostos, com fases
            //    defasadas de um quarto de ciclo. Repousam entre ciclos
            //    (fadinha a 0), entao nao ficam piscando o tempo todo. ──
            Ray(Ray1, RayRotate1, RayMove1, 28.0, 0.00);
            Ray(Ray2, RayRotate2, RayMove2, 152.0, 0.25);
            Ray(Ray3, RayRotate3, RayMove3, 208.0, 0.50);
            Ray(Ray4, RayRotate4, RayMove4, 332.0, 0.75);

            void Pulse(FrameworkElement ring, ScaleTransform scale, double offset, double peak, double seconds, double endScale)
            {
                double ph = (_now / seconds + offset) % 1.0;
                if (ph < 0.0) ph += 1.0;

                // 0..0,10 nasce | 0,10..0,55 abre | 0,55..1 Some apagando
                double fade = ph < 0.10 ? ph / 0.10
                           : ph > 0.55 ? 1.0 - (ph - 0.55) / 0.45
                           : 1.0;
                ring.Opacity = _hoverT * peak * fade;
                double s = 0.70 + (endScale - 0.70) * ph;
                scale.ScaleX = scale.ScaleY = s;
            }

            void Ray(Rectangle streak, RotateTransform rot, TranslateTransform move, double angle, double offset)
            {
                // O angulo e FIXO por particula: a estria nasce vertical e so
                // gira. Sem isso cada quadro reescreveria a geometria.
                rot.Angle = angle;

                double ph = (_now / 1.9 + offset) % 1.0;
                if (ph < 0.0) ph += 1.0;

                // 0..0,12 some | 0,12..0,52 acende | 0,52..1 Some apagando
                double fade = ph < 0.12 ? ph / 0.12
                           : ph > 0.52 ? 1.0 - (ph - 0.52) / 0.48
                           : 1.0;
                streak.Opacity = _hoverT * 0.80 * fade;

                // As estrias SAEM do raio: 9 ate 36 unidades do centro, na
                // direcao do proprio angulo. O teto de 36 + meia estria (8,5)
                // fica em 44,5 do centro, ou seja 5,5..94,5 na malha 0..100 -
                // encosta na borda sem nunca sair.
                double r = 9.0 + 27.0 * ph;
                double rad = angle * Math.PI / 180.0;
                move.X = Math.Sin(rad) * r;
                move.Y = -Math.Cos(rad) * r;
            }
        }

        #endregion

        #region Geometria sob demanda

        /// <summary>
        /// Assinatura dos parametros que MUDAM FORMA. Se a expressao interpolada
        /// nao alterou nenhum deles, nenhuma geometria e reconstruida - e o
        /// rosto em repouso custa zero alocacoes por quadro, mesmo com o
        /// relogio ligado para a respiracao.
        /// </summary>
        private readonly struct ShapeSignature : IEquatable<ShapeSignature>
        {
            public readonly double Tilt, Width, Aperture, Curve, Squint;
            public readonly double BrowIn, BrowOut, BrowArc, BrowThick, BrowAsym;
            public readonly double MouthW, MouthC, MouthO;
            public readonly int Lids, Shuts, MouthOpen;

            public ShapeSignature(in AiFaceExpression e, bool lids, bool shuts, bool mouthOpen)
            {
                Tilt = e.EyeTilt; Width = e.EyeWidth; Aperture = e.EyeAperture;
                Curve = e.EyeCurve; Squint = e.EyeSquint;
                BrowIn = e.BrowInnerLift; BrowOut = e.BrowOuterLift; BrowArc = e.BrowCurve;
                BrowThick = e.BrowThickness; BrowAsym = e.BrowAsymmetry;
                MouthW = e.MouthWidth; MouthC = e.MouthCurve; MouthO = e.MouthOpen;
                Lids = lids ? 1 : 0; Shuts = shuts ? 1 : 0; MouthOpen = mouthOpen ? 1 : 0;
            }

            public bool Equals(ShapeSignature o) =>
                Near(Tilt, o.Tilt) && Near(Width, o.Width) && Near(Aperture, o.Aperture) &&
                Near(Curve, o.Curve) && Near(Squint, o.Squint) &&
                Near(BrowIn, o.BrowIn) && Near(BrowOut, o.BrowOut) && Near(BrowArc, o.BrowArc) &&
                Near(BrowThick, o.BrowThick) && Near(BrowAsym, o.BrowAsym) &&
                Near(MouthW, o.MouthW) && Near(MouthC, o.MouthC) && Near(MouthO, o.MouthO) &&
                Lids == o.Lids && Shuts == o.Shuts && MouthOpen == o.MouthOpen;

            public override bool Equals(object? o) => o is ShapeSignature s && Equals(s);
            public override int GetHashCode() => 0;

            private static bool Near(double a, double b) => Math.Abs(a - b) < GeoEpsilon;
        }

        private void RebuildGeometry(bool force)
        {
            // Abertos: por cima de ~0,17 a fenda e uma linha e a palpebra de
            // tracado vira o unico desenho. Abaixo disso, so o arco de olho
            // fechado - que e anatomicamente o certo, nao um recurso.
            bool lids = _cur.EyeAperture > 0.17;
            bool shut = !lids;
            bool mouthOpen = _cur.MouthOpen > 0.055;

            var sig = new ShapeSignature(_cur, lids, shut, mouthOpen);
            if (!force && sig.Equals(_sig)) return;
            _sig = sig;
            _geoDirty = false;

            // ── olhos ──
            // Cada lado recebe um desvio proprio: rosto humano nao e simetrico,
            // e o espelho perfeito da versao anterior era parte do que fazia o
            // rosto ler como "desenho de robo".
            double asymTilt = _cur.BrowAsymmetry * 1.1;
            double asymTiltR = _cur.BrowAsymmetry * 0.6;

            SetPath(EyeShapeL, lids ? AiFaceGeometry.EyeShape(EyeCxL, _cur.EyeTilt - asymTilt, _cur.EyeWidth, _cur.EyeAperture, _cur.EyeCurve, _cur.EyeSquint) : null);
            SetPath(EyeShapeR, lids ? AiFaceGeometry.EyeShape(EyeCxR, _cur.EyeTilt + asymTiltR, _cur.EyeWidth, _cur.EyeAperture, _cur.EyeCurve, _cur.EyeSquint) : null);
            SetPath(EyeLidL, lids ? AiFaceGeometry.EyeLidUpper(EyeCxL, _cur.EyeTilt - asymTilt, _cur.EyeWidth, _cur.EyeAperture, _cur.EyeCurve) : null);
            SetPath(EyeLidR, lids ? AiFaceGeometry.EyeLidUpper(EyeCxR, _cur.EyeTilt + asymTiltR, _cur.EyeWidth, _cur.EyeAperture, _cur.EyeCurve) : null);
            SetPath(EyeLidLowL, lids ? AiFaceGeometry.EyeLidLower(EyeCxL, _cur) : null);
            SetPath(EyeLidLowR, lids ? AiFaceGeometry.EyeLidLower(EyeCxR, _cur) : null);
            SetPath(EyeShutL, shut ? AiFaceGeometry.EyeShut(EyeCxL, _cur.EyeTilt, _cur.EyeWidth) : null);
            SetPath(EyeShutR, shut ? AiFaceGeometry.EyeShut(EyeCxR, _cur.EyeTilt, _cur.EyeWidth) : null);

            // ── sobrancelhas ──
            BrowL.Data = AiFaceGeometry.Brow(EyeCxL, true, _cur);
            BrowR.Data = AiFaceGeometry.Brow(EyeCxR, false, _cur);
            BrowL.Opacity = _cur.BrowOpacity;
            BrowR.Opacity = _cur.BrowOpacity;
            BrowL.Fill = _solid;
            BrowR.Fill = _solid;

            // ── boca ──
            // O selo labial e SEMPRE desenhado: ele e a borda do labio
            // superior quando a boca abre, e a boca inteira quando fecha.
            // Deixar ele condicional ao MouthOpen sumia com a boca em 17 dos
            // 18 estados (so Working/Furious/Surprised/Weeping/Errored tem
            // boca aberta) - o rosto ficava sem boca em Idle, Happy, Sad...
            MouthSeam.Data = AiFaceGeometry.MouthSeam(_cur);
            MouthSeam.Visibility = Visibility.Visible;
            SetPath(MouthShape, mouthOpen ? AiFaceGeometry.MouthOpen(_cur) : null);
            SetPath(MouthTeeth, mouthOpen && _cur.MouthOpen > 0.50 ? AiFaceGeometry.MouthTeeth(_cur) : null);
        }

        private static void SetPath(Path p, Geometry? geo)
        {
            if (geo == null) { p.Visibility = Visibility.Collapsed; return; }
            p.Data = geo;
            p.Visibility = Visibility.Visible;
        }

        #endregion

        #region Cor (cacheada por cor quantizada)

        /// <summary>
        /// Aplica cor e tudo que dela deriva. A cor e interpolada com o MESMO
        /// <c>t</c> da geometria, entao cor e forma mudam juntas: cor adiantada
        /// ou atrasada denunciaria dois sistemas separados.
        ///
        /// Guardiao de custo: so reassigna quando a cor mudou de verdade
        /// (<see cref="ColorEpsilon"/>). Durante uma transicao de 390ms a 30fps
        /// sao ~12 quadros, e o cache por cor quantizada faz com que depois das
        /// primeiras dezenas de transicoes nao ha mais uma unica alocacao.
        /// </summary>
        private void ApplyColor(bool force)
        {
            Color target = _blending
                ? BrainMoodToBrushConverter.Blend(_moodFrom, _moodTo, _blend)
                : BrainMoodToBrushConverter.Accent(_moodTo);

            if (!force && !ColorChanged(_appliedColor, target)) return;

            _color = target;
            _appliedColor = target;

            _solid = Brush(Color.FromArgb(255, target.R, target.G, target.B));
            _soft = Brush(Color.FromArgb(118, target.R, target.G, target.B));
            _faint = Brush(Color.FromArgb(46, target.R, target.G, target.B));
            _mouthFill = Brush(Color.FromRgb(
                (byte)(target.R * 0.30), (byte)(target.G * 0.30), (byte)(target.B * 0.34)));
            _socket = Brush(Color.FromRgb(
                (byte)(8 + target.R / 14), (byte)(10 + target.G / 14), (byte)(20 + target.B / 12)));

            RadialGradientBrush iris = IrisBrush(target);
            FaceContour.Stroke = _soft;
            FaceContour.Opacity = Clamp(_cur.Contour, 0.0, 1.0);

            EyeShapeL.Fill = _socket;
            EyeShapeR.Fill = _socket;

            EyeLidL.Stroke = _solid;
            EyeLidR.Stroke = _solid;
            EyeLidLowL.Stroke = _soft;
            EyeLidLowR.Stroke = _soft;
            EyeShutL.Stroke = _solid;
            EyeShutR.Stroke = _solid;
            Nose.Stroke = _faint;

            MouthSeam.Stroke = _solid;
            MouthShape.Fill = _mouthFill;

            BrowL.Fill = _solid;
            BrowR.Fill = _solid;
            GlintL.Fill = Brushes.White;
            GlintR.Fill = _solid;

            IrisL.Fill = iris;
            IrisR.Fill = iris;
            IrisRingBrushL.Color = MixWhite(target, 0.45);
            IrisRingBrushR.Color = MixWhite(target, 0.45);

            SolidColorBrush tear = Brush(Color.FromArgb(235,
                MixWhite(target, 0.55).R, MixWhite(target, 0.55).G, MixWhite(target, 0.55).B));
            TearL.Fill = tear;
            TearR.Fill = tear;

            ThinkDot1.Fill = _solid; ThinkDot2.Fill = _solid; ThinkDot3.Fill = _solid;
            Bar1.Fill = _solid; Bar2.Fill = _solid; Bar3.Fill = _solid;
            AlertArc.Stroke = _solid;

            // O corpo do raio NAO e pintado aqui: ele usa a brush do titulo
            // (VoltrisGradientHorizontalBrush) via DynamicResource, e qualquer
            // cor escrita aqui sobrescreveria a identidade da marca. So os
            // elementos de APOIO seguem o humor do Brain.
            BoltOutlineBrush.Color = MixWhite(target, 0.40);
            Color pulseC = MixWhite(target, 0.30);
            BoltPulseBrush.Color = pulseC;
            BoltPulse2Brush.Color = Color.FromArgb(
                (byte)(pulseC.A * 0.72), pulseC.R, pulseC.G, pulseC.B);

            // As 4 estrias compartilham UM unico brush. Duas razoes:
            // (a) aloca uma vez por mudanca de humor, nao quatro - e humor
            // muda no maximo 18 vezes, nunca por quadro;
            // (b) nao mexo em GradientStops[].Color dentro do brush declarado
            // no XAML. Brushes nomeados que vivem na arvore podem estar
            // congelados (Freezable), e mutar um congelado lanca
            // InvalidOperationException. Atribuir uma brush nova - ainda nao
            // congelada - nao tem esse risco. E o mesmo caminho que o resto do
            // arquivo ja usa com _solid.
            Color rayC = MixWhite(target, 0.55);
            var rayBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 1),
                EndPoint = new Point(0, 0),
                GradientStops =
                {
                    new GradientStop(Colors.Transparent, 0.0),
                    new GradientStop(Color.FromArgb(89, rayC.R, rayC.G, rayC.B), 0.45),
                    new GradientStop(Color.FromArgb(230, rayC.R, rayC.G, rayC.B), 1.0)
                }
            };
            // Congelada: as 4 estrias compartilham o MESMO objeto, e um Fill
            // apontando para brush congelada e o caminho mais barato para a GPU.
            rayBrush.Freeze();
            Ray1.Fill = Ray2.Fill = Ray3.Fill = Ray4.Fill = rayBrush;
        }


        private static bool ColorChanged(Color a, Color b) =>
            Math.Abs(a.R - b.R) > ColorEpsilon ||
            Math.Abs(a.G - b.G) > ColorEpsilon ||
            Math.Abs(a.B - b.B) > ColorEpsilon;

        private static readonly Dictionary<int, SolidColorBrush> BrushPool = new();
        private static readonly Dictionary<int, RadialGradientBrush> IrisPool = new();
        private static readonly object PoolGate = new object();

        private static SolidColorBrush Brush(Color c)
        {
            int key = (c.A << 24) | (c.R << 16) | (c.G << 8) | c.B;
            lock (PoolGate)
            {
                if (BrushPool.TryGetValue(key, out var b)) return b;
                b = new SolidColorBrush(c);
                b.Freeze();
                BrushPool[key] = b;
                return b;
            }
        }

        /// <summary>
        /// Gradiente da iris. Um unico plano de cor no olho faz a figura ler
        /// como "desenhada com caneta"; o degrade radial e o que da
        /// profundidade sem custar nada. Cacheado por cor quantizada.
        /// </summary>
        private static RadialGradientBrush IrisBrush(Color c)
        {
            int key = (c.R << 16) | (c.G << 8) | c.B;
            lock (PoolGate)
            {
                if (IrisPool.TryGetValue(key, out var cached)) return cached;
                var brush = new RadialGradientBrush
                {
                    GradientOrigin = new Point(0.42, 0.34),
                    Center = new Point(0.5, 0.5),
                    RadiusX = 0.72,
                    RadiusY = 0.72
                };
                brush.GradientStops.Add(new GradientStop(MixWhite(c, 0.62), 0.00));
                brush.GradientStops.Add(new GradientStop(MixWhite(c, 0.16), 0.55));
                brush.GradientStops.Add(new GradientStop(c, 1.00));
                brush.Freeze();
                IrisPool[key] = brush;
                return brush;
            }
        }

        private static Color MixWhite(Color c, double t) => Color.FromRgb(
            (byte)(c.R + (255 - c.R) * t),
            (byte)(c.G + (255 - c.G) * t),
            (byte)(c.B + (255 - c.B) * t));

        #endregion

        #region Auras, acentos, lagrimas

        private void StepAura(double dt)
        {
            // Aura: a MESMA respiracao da cabeca. Duas senoides independentes
            // fariam o rosto parecer uma coisa incerta em vez de um ser.
            double wave = Math.Sin(_breathPhase * Math.PI * 2.0);
            double h = _cur.Halo * (0.62 + 0.38 * wave) + _hoverT * 0.10;

            // SEM AUREOLA COM TRACO E SEM ANEL: um unico bloom radial, sem
            // borda,whose opacity e o unico "acento" de luz. O pedido foi
            // "so o rosto" - um brilho que nao desenha nenhuma forma circular.
            AuraWide.Opacity = Clamp(0.05 + h * 0.52, 0.0, 0.58);
            AuraWideScale.ScaleX = AuraWideScale.ScaleY = 1.0 + 0.05 * h;

            // As palpebras fecham SOBRE a iris: e a anatomia, e e o que faz a
            // piscada cobrir o olho de verdade em vez de encolher ele.
            double bl = _blinkL.Close;
            double br = _blinkR.Close;
            double closedBase = Closedness(_cur.EyeAperture);
            double closedL = Math.Max(closedBase, SmoothStep(0.50, 0.96, bl));
            double closedR = Math.Max(closedBase, SmoothStep(0.50, 0.96, br));

            ApertureL.ScaleY = Math.Max(0.035, 1.0 - 0.965 * bl);
            ApertureR.ScaleY = Math.Max(0.035, 1.0 - 0.965 * br);

            EyeShapeL.Opacity = 1.0 - closedL;
            EyeShapeR.Opacity = 1.0 - closedR;
            EyeLidL.Opacity = 1.0 - closedL;
            EyeLidR.Opacity = 1.0 - closedR;
            EyeShutL.Opacity = closedL;
            EyeShutR.Opacity = closedR;

            double lower = 0.30 * SmoothStep(0.30, 0.52, _cur.EyeAperture);
            EyeLidLowL.Opacity = lower * (1.0 - bl);
            EyeLidLowR.Opacity = lower * (1.0 - br);

            // Boca aberta: o selo labial vira a borda do labio superior, entao
            // ele continua visivel, so mais fino.
            MouthSeam.Opacity = _cur.MouthOpen > 0.055 ? 0.85 : 1.0;
        }

        private static double Closedness(double aperture) =>
            1.0 - Clamp((aperture - 0.06) / 0.22, 0.0, 1.0);

        private void StepAccents(double dt)
        {
            bool thinking = _moodTo == BrainMood.Thinking;
            bool working = _moodTo == BrainMood.Working;
            bool alert = _moodTo is BrainMood.Concerned or BrainMood.Frustrated
                              or BrainMood.Errored or BrainMood.Furious;

            ThinkDots.Opacity = Approach(ThinkDots.Opacity, thinking ? 1.0 : 0.0, dt, 0.16);
            if (thinking)
            {
                // Pulso viajando: o ponto maior acende primeiro e os outros
                // seguem. Le "dado chegando", nao "bolha de pensamento".
                double ph = _now * 2.2;
                ThinkDot1.Opacity = 0.30 + 0.70 * Wave(ph);
                ThinkDot2.Opacity = 0.18 + 0.55 * Wave(ph - 0.9);
                ThinkDot3.Opacity = 0.10 + 0.40 * Wave(ph - 1.8);
            }
            else
            {
                ThinkDot1.Opacity = 0.9; ThinkDot2.Opacity = 0.7; ThinkDot3.Opacity = 0.45;
            }

            WorkBars.Opacity = Approach(WorkBars.Opacity, working ? 1.0 : 0.0, dt, 0.16);
            if (working)
            {
                double ph = _now * 2.6;
                Bar1.Opacity = 0.18 + 0.82 * Wave(ph);
                Bar2.Opacity = 0.18 + 0.82 * Wave(ph - 0.9);
                Bar3.Opacity = 0.18 + 0.82 * Wave(ph - 1.8);
            }
            else
            {
                Bar1.Opacity = 0.25; Bar2.Opacity = 0.25; Bar3.Opacity = 0.25;
            }

            AlertArc.Opacity = Approach(AlertArc.Opacity,
                alert ? 0.50 + 0.22 * Wave(_now * 1.6) : 0.0, dt, 0.20);
        }

        /// <summary>
        /// Onda triangular suave em 0..1. Nao e senoide porque uma senoide
        /// repetida em tres pontos parece mecanica.
        /// </summary>
        private static double Wave(double phase)
        {
            double p = phase - Math.Floor(phase);
            if (p < 0.34) return Smooth(p / 0.34);
            if (p < 0.67) return 1.0 - Smooth((p - 0.34) / 0.33);
            return Smooth((1.0 - p) / 0.33);
        }

        /// <summary>
        /// Partículas de repouso: 5 motes subindo em torno do rosto.
        ///
        /// POR QUE 5 E NAO 18 (a versao anterior do circulo tinha 18 com 68
        /// animacoes): com o rosto visivel, 18 elementos viram ruido e
        /// competem com a expressao - que e o oposto do que uma IA com
        /// personalidade precisa. 5 motes dao profundidade sem virar fumaca.
        ///
        /// POR QUE OS PERIODOS SAO TODOS DIFERENTES: com o mesmo periodo, os
        /// 5 sobem em bloco e o conjunto le como uma unica coluna. Com
        /// periodos em primos entre si (7,3 / 9,1 / 11,7 / 13,9 / 17,3 s), o
        /// grupo nunca se alinha - e cada um sobe no SEU ritmo. Custo: uma
        /// divisao e um senoide por mote, zero alocacao.
        ///
        /// A posicao de partida tambem e fixa por mote (dx, dy) para nao
        /// nascerem todos do mesmo ponto.
        /// </summary>
        private void StepMotes()
        {
            // Fora do hover as partículas ganham presenca; no hover elas
            // cedem espaco ao raio (que e o protagonista do gesto).
            MotesCanvas.Opacity = _suspended ? 0 : Approach(MotesCanvas.Opacity, 1.0 - 0.55 * _hoverT, 0.12, 0.18);
            if (MotesCanvas.Opacity < 0.004) return;

            //        (mote,  move,  periodo,  x,    y,   fase,  pico)
            // x perto das BORDAS da face (9..22 e 83..91) e y subindo de ~34
            // ate 6: assim o mote acompanha o rosto pela lateral, em vez de
            // atravessar o meio dele e ficar escondido atrás da face. Os
            // periodos sao primos entre si (7,3 / 9,1 / 11,7 / 13,9 / 17,3)
            // para nunca se alinham.
            Mote(Mote1, MoteMove1, 7.3, 11.0, 34.0, 0.00, 0.95);
            Mote(Mote2, MoteMove2, 9.1, 16.0, 26.0, 0.41, 0.80);
            Mote(Mote3, MoteMove3, 11.7, 89.0, 30.0, 0.73, 0.70);
            Mote(Mote4, MoteMove4, 13.9, 84.0, 36.0, 0.18, 0.62);
            Mote(Mote5, MoteMove5, 17.3, 22.0, 30.0, 0.59, 0.88);

            void Mote(Ellipse dot, TranslateTransform move, double period, double startX, double startY, double phase0, double peak)
            {
                double ph = (_now / period + phase0) % 1.0;
                if (ph < 0.0) ph += 1.0;

                // Sobe de startY ate 6 (some no topo do rosto), com deriva
                // lateral senoidal de amplitude 3,5 - o suficiente para o
                // caminho parecer organico sem parecer turbulento.
                move.X = startX + 3.5 * Math.Sin(_now * 0.62 + phase0 * 6.283);
                move.Y = startY + (6.0 - startY) * ph;

                // Envelope: nasce apagado, acende no meio, some no topo.
                double fade = ph < 0.18 ? ph / 0.18
                           : ph > 0.68 ? 1.0 - (ph - 0.68) / 0.32
                           : 1.0;
                dot.Opacity = peak * fade * (0.55 + 0.45 * MotesCanvas.Opacity);
            }
        }

        private void StepTears()
        {
            double amount = _cur.Tear;
            if (amount <= 0.04)
            {
                if (TearL.Visibility != Visibility.Collapsed)
                {
                    TearL.Visibility = Visibility.Collapsed;
                    TearR.Visibility = Visibility.Collapsed;
                }
                return;
            }

            if (TearL.Visibility != Visibility.Visible)
            {
                TearL.Visibility = Visibility.Visible;
                TearR.Visibility = Visibility.Visible;
            }

            // Gota que se forma na palpebra inferior e escorre pela bochecha.
            // Duas fases defasadas de meio ciclo, para nao pingar em unissono.
            // Movimento deliberadamente MINIMO: 5,8 unidades em 2,6s. Lagrima
            // animada "de verdade" (compingo, olho fechando) seria uma
            // caricatura - o pedido e por uma MICROEXPRESSAO elegante.
            const double period = 2.6;
            TearFallL.Y = PhasedFall(_now, 0.0, period);
            TearFallR.Y = PhasedFall(_now, 0.5, period);

            TearL.Opacity = 0.80 * amount * Envelope(_now / period);
            TearR.Opacity = 0.80 * amount * Envelope(_now / period + 0.5);
        }

        private static double PhasedFall(double now, double offset, double period)
        {
            double ph = (now / period + offset) % 1.0;
            if (ph < 0.0) ph += 1.0;
            return 0.6 + ph * 5.8;
        }

        private static double Envelope(double ph)
        {
            double p = ph - Math.Floor(ph);
            // Aparece rapido, segura, some devagar - como agua de verdade.
            if (p < 0.14) return Smooth(p / 0.14);
            if (p > 0.72) return 1.0 - Smooth((p - 0.72) / 0.28);
            return 1.0;
        }

        private void StepReaction(double dt)
        {
            if (_punchT >= 1.0) return;
            _punchT = Math.Min(1.0, _punchT + dt / 0.32);
        }

        #endregion

        #region Helpers

        private static double Smooth(double t) => t * t * (3.0 - 2.0 * t);

        private static double SmoothStep(double a, double b, double x)
        {
            if (b <= a) return x >= b ? 1.0 : 0.0;
            double t = Clamp((x - a) / (b - a), 0.0, 1.0);
            return t * t * (3.0 - 2.0 * t);
        }

        private static double Approach(double cur, double target, double dt, double tau)
        {
            if (Math.Abs(target - cur) < 0.0006) return target;
            return cur + (target - cur) * (1.0 - Math.Exp(-dt / Math.Max(0.001, tau)));
        }

        private static double Clamp(double v, double min, double max) =>
            v < min ? min : (v > max ? max : v);

        #endregion

        #region Observabilidade (item 15)

        /// <summary>
        /// Logger resolvido UMA VEZ, no Load, e guardado. Duas razoes:
        ///
        ///  1. CUSTO. <c>ServiceLocator.Logger</c> passa por
        ///     <c>App.LoggingService</c>, que e um getter que toca o
        ///     inicializador estatico de <c>App</c>. Chamar isso a cada
        ///     transicao de humor e chamar uma property statica de dentro de
        ///     um getter a 30Hz.
        ///
        ///  2. ROBUSTEZ. Se o <c>App</c> nao inicializou (ou a inicializacao
        ///     estatico dele lancou), o getter lanca. Um log que derruba o
        ///     rosto e exatamente o oposto do que um log deve fazer. Por isso
        ///     a resolucao E as chamadas sao try/catch: log nunca quebra a
        ///     animacao.
        ///
        ///  Null e um resultado legitimo e permanente: em um arnes de teste
        ///  isolado, ou antes do App subir, o rosto simplesmente nao loga.
        /// </summary>
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

        private void LogInfo(string m)
        {
            try { Log?.LogInfo(m); } catch { }
        }

        private void LogDebug(string m)
        {
            try { Log?.LogDebug(m, nameof(AiFaceView)); } catch { }
        }

        private void LogError(string m, Exception ex)
        {
            try { Log?.LogError(m, ex); } catch { }
        }

        #endregion
    }
}
