using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Threading.Tasks;
using System.Windows.Threading;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.UI.Helpers;
using VoltrisOptimizer.UI.Widgets.ViewModels;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.UI.Widgets 
{
    public class InvertedBoolToVisibilityConverter : IValueConverter 
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is bool b && b ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is Visibility v && v == Visibility.Collapsed;
    }

    public partial class WidgetWindow : Window, IDisposable 
    {
        private WidgetViewModel _viewModel;
        private WidgetConfig _config;
        private ILoggingService _logger;

        // TRANSPARÊNCIA: Sistema de sincronização em tempo real com MainWindow
        private bool _lastTransparencyState = true;
        private DispatcherTimer? _transparencyCheckTimer;
        private bool _transparencySystemInitialized;
        private bool _monitoringStarted;

        // Drag state
        private bool _isDragging;
        private Point _dragStartPoint;

        // Posição inicial do mouse
        private const double MIN_DRAG_DISTANCE = 5;

        // Mínimo para considerar drag real
        private const int SNAP_DISTANCE = 15;
        private const double SCREEN_MARGIN = 0;
        private DateTime _dragStartTime;
        private bool _hasMovedSignificantly;

        [DllImport("user32.dll")]private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")]private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOSENDCHANGING = 0x0400;
        private const uint SWP_NOSIZE = 0x0001;

        private const int GWL_EXSTYLE = - 20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        public event EventHandler OpenDashboardRequested;

        private DispatcherTimer _layoutDebounceTimer;

        public WidgetWindow(ILoggingService logger, global::VoltrisOptimizer.VoltrisGlobalInsightService insightService) 
        {
            InitializeComponent();
            try { VoltrisDiagnosticSystem.Instance.Timeline("WIDGET", "WidgetWindow: InitializeComponent concluído"); } catch { }

            _layoutDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _layoutDebounceTimer.Tick += (s, e) =>
            {
                _layoutDebounceTimer.Stop();
                RefreshAdaptiveLayout();
            };

            _logger = logger;
            _config = WidgetConfig.Load();

            //Detectar versão do Windows e aplicar bordas apropriadas
            ApplyWindowsVersionSpecificStyling();

            _viewModel = new WidgetViewModel(logger, insightService);

            _viewModel.CloseRequested += OnViewModelCloseRequested;

            _viewModel.OpenDashboardRequested += (_, __) => OpenDashboardRequested?.Invoke(this, EventArgs.Empty);

            // OTIMIZAÇÃO: Sincronização de layout adaptativo quando métricas aparecem/somem
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

            DataContext = _viewModel;

            //Eventos de lifecycle
            // AVISO: Loaded é vinculado no XAML (Loaded="Window_Loaded"), não vincular aqui novamente para evitar duplo disparo/memory leak.
            SourceInitialized += Window_SourceInitialized;
            ContentRendered += Window_ContentRendered;
            IsVisibleChanged += Window_IsVisibleChanged;

            // Seta inferior visível somente no hover (e sempre quando colapsado, para permitir reabrir)
            MainContainer.MouseEnter += MainContainer_MouseEnter;
            MainContainer.MouseLeave += MainContainer_MouseLeave;
            UpdateBottomUi();

            // ── GEOMETRIA FINAL, ANTES DO PRIMEIRO Show() ─────────────────────
            //
            // BUG ORIGINAL (o widget abria gigante na horizontal e depois encolhia
            // para ~213 px em menos de um segundo):
            //
            // A janela era declarada com Width="Auto" + SizeToContent="Height".
            // No WPF, quando Width é NaN, Window.MeasureOverride passa
            // double.PositiveInfinity como restrição de LARGURA ao conteúdo. Uma
            // janela de largura automática medida sem limite é o pior caso
            // possível: qualquer elemento da árvore cujo DesiredSize dependa do
            // estado ainda não resolvido (textos de localização, valores de
            // métrica, visibilidade das colunas) entra direto na largura final.
            //
            // Pior: o conteúdo ficava dentro de um ScrollViewer com
            // HorizontalScrollBarVisibility="Auto", que mede o filho com espaço
            // INFINITO — o que amplifica ainda mais a largura reportada. E o
            // MetricsGrid tem 14 colunas de Width="Auto", ou seja, a largura
            // final é a SOMA de todos os chips; um rótulo traduzido mais largo
            // ou uma métrica a mais já estica a janela.
            //
            // A sequência antiga piorava tudo: o construtor deixava a janela em
            // Left=-10000, o WidgetManagerService tentava recentralizar usando
            // window.Width (que era NaN, produzindo NaN), e o Window_Loaded
            // forçava UpdateLayout() DUAS vezes — alterando a geometria antes
            // do primeiro quadro, que era exatamente o quadro visto pelo usuário.
            //
            // CORREÇÃO: a largura é medida UMA vez aqui, com restrição FINITA e
            // clampada em [MinWidgetWidth, MaxWidgetWidth], e a posição é
            // resolvida no mesmo instante. A partir daí a geometria não muda
            // mais até o usuário pedir um resize adaptativo. Loaded e
            // ContentRendered apenas REVELAM a janela — não calculam nada.
            ApplyInitialGeometry();

            _logger?.LogInfo($"[Widget] Construtor concluído - Geometria inicial: ({Left:F1}, {Top:F1}) {Width:F1}x{Height:F1}");
            try { VoltrisDiagnosticSystem.Instance.Timeline("WIDGET", $"WidgetWindow: Construtor concluído - Geometria inicial: ({Left:F1}, {Top:F1}) {Width:F1}x{Height:F1}"); } catch { }
        }

        /// <summary>
        /// Largura máxima que o widget pode ocupar. Também espelhada em MaxWidth no
        /// XAML; a duplicação é deliberada — o XAML protege a árvore de layout e
        /// esta constante protege o cálculo de medida, que roda antes do XAML ter
        /// qualquer efeito.
        /// </summary>
        private const double MaxWidgetWidth = 560;

        /// <summary>
        /// Largura mínima. Abaixo disso os rótulos das métricas ficam ilegíveis ou
        /// o conteúdo é cortado sem indicação visual.
        /// </summary>
        private const double MinWidgetWidth = 180;

        /// <summary>
        /// Indica que o estado colapsado salvo pelo usuário ainda não foi aplicado
        /// porque a altura real da janela só fica disponível após o primeiro layout.
        /// Consumido em <see cref="Window_ContentRendered"/>, antes da revelação.
        /// </summary>
        private bool _collapsePendingOnReveal;

        /// <summary>
        /// Deslocamento do topo do widget em relação à borda superior da ÁREA DE
        /// TRABALHO. Zero: o widget encosta no topo, como uma overlay.
        ///
        /// Importante: a referência é <c>workArea.Top</c>, e não o topo do monitor.
        /// A área de trabalho já exclui a barra de tarefas, então um monitor com a
        /// barra no topo posiciona o widget logo abaixo dela — que é o
        /// comportamento correto. Usar o topo do monitor buryiria o widget atrás
        /// da barra, invisível e inacessível.
        /// </summary>
        private const double WidgetTopMargin = 0;

        /// <summary>
        /// Resolve largura e posição do widget ANTES de a janela ser apresentada.
        ///
        /// Este é o ÚNICO ponto onde a geometria de abertura é decidida. Tudo o
        /// que vier depois (Loaded, ContentRendered, o fade-in) assume que
        /// Width/Left/Top já são finais e não os recalcula — é isso que elimina
        /// o salto visual de "gigante → tamanho padrão".
        /// </summary>
        private void ApplyInitialGeometry()
        {
            try
            {
                double width = MeasureNaturalWidth();
                _targetWidth = width;
                _isFirstLayout = false;

                // ApplyWidth (e não a atribuição direta) para que MainContainer e
                // LayerHost recebam a largura explícita. Sem LayerHost travado, o
                // Grid hospedeiro esticaria até o DesiredSize do Canvas
                // decorativo e a janela nasceria com a faixa transparente extra
                // à direita.
                ApplyWidth(width);

                //Resolve a área de trabalho do monitor CORRETO em DIPs.
                //SystemParameters.PrimaryScreen* é o monitor primário e está
                //errado em multi-monitor; WindowPlacementHelper consulta o
                //monitor real da janela.
                if (!WindowPlacementHelper.TryGetWorkAreaInDips(this, out var workArea))
                {
                    workArea = SystemParameters.WorkArea;
                }

                double height = ActualHeight > 0 ? ActualHeight : double.NaN;

                //Posição salva pelo usuário tem precedência, desde que ainda
                //esteja visível no monitor atual.
                bool useSaved = _config.HasSavedPosition
                                && IsPositionValid(_config.PositionX, _config.PositionY,
                                                   workArea, width,
                                                   ActualHeight > 0 ? ActualHeight : 90);

                if (useSaved)
                {
                    Left = _config.PositionX;
                    Top = _config.PositionY;
                    _logger?.LogInfo($"[Widget] Posição salva restaurada: ({Left:F1}, {Top:F1})");
                }
                else
                {
                    //Padrão: topo, centralizado horizontalmente no monitor atual.
                    Left = Math.Round(workArea.Left + (workArea.Width - width) / 2.0);
                    Top = Math.Round(workArea.Top + WidgetTopMargin);
                    _logger?.LogInfo($"[Widget] Posição padrão (topo central): ({Left:F1}, {Top:F1})");
                }

                if (_config.IsCollapsed)
                {
                    // O estado colapsado NÃO é aplicado aqui, e essa é uma decisão
                    // deliberada.
                    //
                    // Colapsar depende da ALTURA REAL da janela, porque a operação
                    // é "subir a janela de forma que sobre apenas a faixa visível":
                    //     Top = -altura + alturaVisível
                    // No construtor a janela ainda não passou por layout, então
                    // ActualHeight é 0 e o cálculo cairia no fallback de 25 px,
                    // deixando o widget na posição errada (Top ≈ 0, ou seja,
                    // inteiramente visível, em vez de recolhido).
                    //
                    // A aplicação é feita em Window_ContentRendered, ANTES de
                    // Opacity = 1.0: a essa altura a altura já é real e a janela
                    // ainda está invisível, então o usuário não vê nenhum quadro
                    // intermediário. O mesmo invariante do restante — nada muda
                    // depois que o widget fica visível.
                    _collapsePendingOnReveal = true;
                    _logger?.LogInfo("[Widget] Estado colapsado pendente: será aplicado na revelação.");
                }

                if (double.IsNaN(height)) height = ActualHeight;
                _logger?.LogInfo($"[Widget] Geometria inicial resolvida: {width:F1}x{height:F1} em ({Left:F1}, {Top:F1})");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[Widget] Falha ao aplicar geometria inicial: {ex.Message}", ex);

                //Nunca abrir fora da tela: um fallback previsível é melhor do que
                //uma janela invisível a dez mil pixels de distância.
                try
                {
                    if (double.IsNaN(Width) || Width <= 0) Width = 300;
                    if (double.IsNaN(Left) || Left <= -9000)
                        Left = SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - Width) / 2.0;
                    if (double.IsNaN(Top) || Top <= -9000)
                        Top = SystemParameters.WorkArea.Top + WidgetTopMargin;
                }
                catch { }
            }
        }

        /// <summary>
        /// Mede a largura natural do conteúdo do widget com uma restrição FINITA.
        ///
        /// POR QUE FINITA, E NÃO double.PositiveInfinity
        /// ============================================
        /// A versão anterior media com Size(∞, ∞). Com restrição infinita:
        ///
        ///  a) qualquer texto temporário (estado de carregamento, valor "--", ou
        ///     ainda sem métrica) entra integralmente na largura final;
        ///  b) um ScrollViewer com barra de rolagem automática, medido sem
        ///     limite, reporta o espaço do conteúdo e não o espaço visível —
        ///     foi o que produzia a largura absurda;
        ///  c) colunas Width="Auto" somam larguras sem teto, então basta um
        ///     rótulo traduzido largo para a janela esticar.
        ///
        /// Medindo contra MaxWidgetWidth, a restrição já é o teto desejado, e o
        /// resultado é clampado de novo para [MinWidgetWidth, MaxWidgetWidth] por
        /// segurança. O resultado é determinístico e igual em todos os quadros.
        /// </summary>
        private double MeasureNaturalWidth()
        {
            try
            {
                if (ContentRoot == null) return 300;

                // [FIX:WIDGET-LARGURA] MEDE-SE O CONTEÚDO, NÃO O MainContainer.
                //
                // MainContainer é um Border cujo Grid tem DOIS filhos: o Canvas
                // decorativo dos brilhos e o ContentRoot com as métricas. O
                // DesiredSize de um Grid é o MAIOR entre os filhos, e o Canvas
                // pede ~270 px (elipses de 260/220/180 px, com os deslocamentos
                // de Canvas.Left/Canvas.Right somados) enquanto o conteúdo real
                // pede ~213 px.
                //
                // Ou seja: medir MainContainer devolvia a largura do BRILHO, e a
                // janela nascia com uma faixa transparente de ~57 px à direita.
                // No primeiro layout real o Grid arranja o Canvas no espaço
                // disponível e o excedente some — daí o sintoma "o lado direito
                // vem um pouco maior e depois se ajusta sozinho".
                //
                // A camada decorativa NÃO pode influenciar a janela. O tamanho
                // é definido pelo conteúdo; o brilho é recortado por ele
                // (MainContainer tem ClipToBounds=True).
                ContentRoot.Width = double.NaN;

                ContentRoot.Measure(new Size(MaxWidgetWidth, double.PositiveInfinity));

                double natural = ContentRoot.DesiredSize.Width;

                if (double.IsNaN(natural) || double.IsInfinity(natural) || natural <= 0)
                {
                    _logger?.LogWarning($"[Widget] Medição retornou largura inválida ({natural}), usando fallback.");
                    natural = 300;
                }

                double clamped = Math.Max(MinWidgetWidth, Math.Min(MaxWidgetWidth, natural));

                if (Math.Abs(clamped - natural) > 0.5)
                {
                    _logger?.LogInfo(
                        $"[Widget] Largura do conteúdo {natural:F1} limitada a {clamped:F1} " +
                        $"(limites {MinWidgetWidth:F0}..{MaxWidgetWidth:F0}).");
                }

                return Math.Round(clamped);
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[Widget] Erro ao medir largura natural: {ex.Message}");
                return 300;
            }
        }

        /// <summary>
        /// Aplica estilos específicos baseados na versão do Windows
        /// Windows 10: Bordas quadradas (CornerRadius = 0)
        /// Windows 11: Bordas arredondadas (CornerRadius = 16)
        /// </summary>
        private void ApplyWindowsVersionSpecificStyling()
        {
            try
            {
                var version = Environment.OSVersion.Version;

                bool isWindows11 = version.Major >= 10 && version.Build >= 22000;
                var cornerRadius = isWindows11 ? new CornerRadius(16) : new CornerRadius(0);

                if (MainContainer != null)
                {
                    MainContainer.CornerRadius = cornerRadius;
                }
                _logger?.LogInfo($"[Widget] Windows {version.Build} detectado - Bordas: {(isWindows11 ? "Arredondadas (16)" : "Quadradas (0)")}");
            }
            catch (Exception ex)
            {
                _logger?.LogError("[Widget] Erro ao aplicar estilos de versão do Windows", ex);
            }
        }

        //Removido ApplySettings - configurações aplicadas diretamente onde necessário
        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            try { VoltrisDiagnosticSystem.Instance.Timeline("WIDGET", "WidgetWindow: Window_SourceInitialized executado, posicionando janela"); } catch { }
            
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                
                // Configurar ToolWindow
                int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
                
                // CORREÇÃO WINDOWS 11: NÃO mover para off-screen antes do ContentRendered!
                // Isso impede o DWM de aplicar Acrylic corretamente.
                // A janela já nasce com Visibility=Collapsed no XAML, então não será visível.
                _logger?.LogInfo("[Widget] SourceInitialized: Janela configurada como ToolWindow (sem off-screen).");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[Widget] Erro ao configurar ToolWindow: {ex.Message}");
            }
        }

        /// <summary>
        /// Aplica transparência Acrylic/Mica na janela do widget sincronizada com MainWindow
        /// </summary>
        public void ApplyTransparency(bool enabled)
        {
            _logger?.LogInfo($"[Appearance] Acrylic state changed: {(enabled ? "ON" : "OFF")}");
            _logger?.LogInfo($"[Widget] Acrylic {(enabled ? "applied" : "disabled")}");
            _logger?.LogInfo($"[Widget] ApplyTransparency: INICIANDO | enabled={enabled}");
            
            try
            {
                if (enabled)
                {
                    VoltrisOptimizer.Helpers.VisualEffectsManager.Initialize();
                    VoltrisOptimizer.Helpers.VisualEffectsManager.ApplyVisualEffects(
                        this,
                        VoltrisOptimizer.Helpers.VisualEffectsManager.WindowType.Widget,
                        VoltrisOptimizer.Helpers.VisualEffectsManager.BackdropType.Acrylic,
                        roundedCorners: true);
                }
                else
                {
                    VoltrisOptimizer.Helpers.VisualEffectsManager.RemoveVisualEffects(this);
                }

                if (MainContainer != null)
                {
                    try
                    {
                        MainContainer.Background = FindResource("WindowBackgroundBrush") as Brush
                            ?? FindResource(enabled ? "TransparentBackground" : "SolidBackground") as Brush;
                    }
                    catch { }
                }
                
                _lastTransparencyState = enabled;
                _logger?.LogInfo($"[Widget] ApplyTransparency: Concluído (enabled={enabled})");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[Widget] ApplyTransparency: ERRO: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// [FIX:WIDGET-M8] ATUALMENTE SEM CHAMADORES — não reintroduzir a chamada.
        ///
        /// HISTÓRICO: este método tinha um chamador, e a restauração do estado
        /// colapsado ficava presa nele. Como ninguém o chamava, o estado colapsado
        /// salvo nunca era aplicado. A restauração foi para
        /// <see cref="ApplyInitialGeometry"/> e a geometria de abertura também.
        ///
        /// Está preservado apenas como registro da lógica pretendida, e foi
        /// atualizado para usar a medição FINITA e a área de trabalho do monitor
        /// correto, de modo que não reintroduzir a chamada não reintroduza o bug
        /// do widget abrir gigante.
        /// </summary>
        private void CalculateAndApplyPosition()
        {
            try
            {
                if (!WindowPlacementHelper.TryGetWorkAreaInDips(this, out var workArea))
                {
                    workArea = SystemParameters.WorkArea;
                }

                double w = MeasureNaturalWidth();
                double h = ActualHeight > 0 ? ActualHeight : 90;
                ApplyWidth(w);

                if (_config.HasSavedPosition
                    && IsPositionValid(_config.PositionX, _config.PositionY, workArea, w, h))
                {
                    Left = _config.PositionX;
                    Top = _config.PositionY;
                    _logger?.LogInfo($"[Widget] Posição restaurada: ({Left:F1}, {Top:F1})");
                }
                else
                {
                    Left = Math.Round(workArea.Left + (workArea.Width - w) / 2.0);
                    Top = workArea.Top + WidgetTopMargin;
                    _logger?.LogInfo($"[Widget] Posição padrão aplicada: ({Left:F1}, {Top:F1})");
                }

                EnsureOnScreen();
                RestoreCollapseState();
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[Widget] Position error: {ex.Message}");
                Left = 100;
                Top = 100;
            }
        }

        /// <summary>
        /// Valida se uma posição é utilizável, isto é, se o widget fica
        /// visível dentro da área de trabalho informada.
        ///
        /// [FIX:WIDGET-MONITOR] Recebe a área de trabalho como <see cref="Rect"/>
        /// (coordenadas absolutas) e não como largura/altura. A versão anterior
        /// comparava uma posição ABSOLUTA contra uma LARGURA relativa
        /// (`x < screenW - margin`), o que só por acaso funcionava no monitor
        /// primário com origem em 0,0. Em um monitor secundário à esquerda
        /// (coordenadas X negativas) qualquer posição válida era reprovada, e o
        /// widget era reposicionado para o topo central do monitor primário —
        /// longe de onde o usuário o colocou.
        /// </summary>
        private bool IsPositionValid(double x, double y, Rect workArea, double winW, double winH)
        {
            //Verificar NaN/Infinity
            if (double.IsNaN(x) || double.IsNaN(y) || double.IsInfinity(x) || double.IsInfinity(y))
            {
                _logger?.LogWarning($"[Widget] Posição inválida detectada: X={x}, Y={y}");
                return false;
            }

            if (workArea.Width <= 0 || workArea.Height <= 0)
                return false;

            //Verificar se está dentro da tela com margem de segurança
            const double margin = 50;

            // [FIX:M-8] A margem visível NÃO pode exceder a própria dimensão da
            // janela, ou a restrição fica insatisfazível.
            //
            // O código original usava `x > -(winW - margin)`, que equivale a
            // `x + winW > margin` — ou seja, "existem pelo menos `margin` pixels
            // visíveis". Para o widget colapsado (altura real ~36px) e margin=50,
            // essa condição é impossível de satisfazer: 36 < 50, então TODA
            // posição era considerada fora da tela, inclusive Top = 0.
            // Era por isso que o log disparava "Posição Y muito negativa" com
            // Top = 0, que é uma posição perfeitamente válida.
            //
            // Correção: limita a exigência a no máximo metade da dimensão, o que
            // torna a condição satisfezível por qualquer posição dentro da tela.
            double minVisibleX = Math.Min(margin, winW / 2.0);
            double minVisibleY = Math.Min(margin, winH / 2.0);

            // A janela precisa ter uma fatia visível dentro da área de trabalho...
            bool xValid = x + winW >= workArea.Left + minVisibleX
                       && x < workArea.Right - minVisibleX;
            bool yValid = y + winH >= workArea.Top + minVisibleY
                       && y < workArea.Bottom - minVisibleY;

            if (!xValid) _logger?.LogWarning($"[Widget] Posição X inválida: {x:F1} (workArea={workArea.X:F0}..{workArea.Right:F0}, winW={winW:F0}, minVisible={minVisibleX:F0})");
            if (!yValid) _logger?.LogWarning($"[Widget] Posição Y inválida: {y:F1} (workArea={workArea.Y:F0}..{workArea.Bottom:F0}, winH={winH:F0}, minVisible={minVisibleY:F0})");

            return xValid && yValid;
        }

        //AUTO-HIDE (COLLAPSE) - IMPLEMENTAÇÃO PROFISSIONAL E DETERMINÍSTICA
        private bool _isCollapsed = false;
        private bool _isCollapseAnimating;
        private const int COLLAPSE_ANIMATION_MS = 250;
        private const int EXPAND_ANIMATION_MS = 300;

        //Altura visível quando colapsado: barra progresso + seta de recolhimento
        private const double COLLAPSED_VISIBLE_HEIGHT = 25;

//Altura visível quando colapsado SEM hover: a barra continua aparecendo (strip visível),
//apenas sem a seta e sem sobra abaixo dela (para o widget não sumir por completo)
private const double COLLAPSED_SLIVER_HEIGHT = 20;

        /// <summary>
        /// Posição atual do widget (atualizada em tempo real durante drag)
        /// </summary>
        private double CurrentWidgetTop => Top;
        private double CurrentWidgetLeft => Left;

        private void CollapseToggle_Click(object sender, RoutedEventArgs e)
        {
            ToggleCollapse();
        }

        /// <summary>
        /// Alterna entre estado colapsado e expandido
        /// </summary>
        public void ToggleCollapse()
        {
            try
            {
                if (_isCollapsed)
                {
                    ExpandWidget();
                }
                else
                {
                    CollapseWidget();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[Widget] Erro em ToggleCollapse: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// COLOCA O WIDGET: Esconde para o topo mantendo apenas a ala visível
        /// </summary>
        private void CollapseWidget()
        {
            try
            {
                //Blindar contra reposicionamentos do hover durante a animação de colapso
                _isCollapseAnimating = true;

                //Salvar posição atual (para referência de movimento futuro)
                _config.PositionX = Left;
                _config.PositionY = Top;
                _config.IsCollapsed = true;
                _config.Save();

                // FORÇA O CÁLCULO DE LAYOUT CASO SEJA A PRIMEIRA INTERAÇÃO (Evita targetTop = 30)
                this.UpdateLayout();
                double currentHeight = ActualHeight > 0 ? ActualHeight : 90;

                //Calcular destino: topo da tela, deixando apenas a ala visível
                double targetTop = -currentHeight + COLLAPSED_VISIBLE_HEIGHT;

                // Limpar qualquer animação anterior que possa travar a propriedade Top
                this.BeginAnimation(Window.TopProperty, null);
                
                // Determinar From (pode ser NaN se não foi inicializado corretamente)
                double currentTop = double.IsNaN(Top) ? 0 : Top;

                //Animar para posição colapsada
                var topAnimation = new DoubleAnimation
                {
                    From = currentTop,
                    To = targetTop,
                    Duration = TimeSpan.FromMilliseconds(COLLAPSE_ANIMATION_MS),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
                    FillBehavior = FillBehavior.HoldEnd
                };

                _isCollapseAnimating = true;
                topAnimation.Completed += (s, e) =>
                {
                    if (_isCollapsed)
                    {
                        MainContent.Visibility = Visibility.Hidden;
                        double targetVisible = _isHovering ? COLLAPSED_VISIBLE_HEIGHT : COLLAPSED_SLIVER_HEIGHT;
                        this.UpdateLayout();
                        double collapsedHeight = ActualHeight > 0 ? ActualHeight : targetVisible;
                        
                        // Libera a propriedade Top removendo a animação ativa
                        this.BeginAnimation(Window.TopProperty, null);
                        
                        // Reposiciona a janela usando a altura real encolhida
                        Top = -collapsedHeight + targetVisible;
                    }

                    _isCollapseAnimating = false;
                };

                this.BeginAnimation(Window.TopProperty, topAnimation);

                //Atualizar seta (aponta para BAIXO = expandir)
                UpdateCollapseArrow(true);
                _isCollapsed = true;
                UpdateBottomUi();

                _logger?.LogInfo($"[Widget] Colapsado: Top {currentTop:F0} -> {targetTop:F0} (Height: {currentHeight:F0})");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[Widget] Erro em CollapseWidget: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// MOSTRA O WIDGET: SEMPRE aparece no TOPO CENTRALIZADO da tela
        /// Regra: Ao expandir, NUNCA volta para posição antiga, sempre topo centro
        /// </summary>
        private void ExpandWidget()
        {
            try
            {
                MainContent.Visibility = Visibility.Visible;

                // [FIX:WIDGET-MONITOR] Centraliza no monitor em que o widget está,
                // não no primário (SystemParameters.PrimaryScreenWidth).
                if (!WindowPlacementHelper.TryGetWorkAreaInDips(this, out var workArea))
                {
                    workArea = new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
                }

                // Medir com restrição FINITA e clampada: a versão anterior media com
                // Size(∞, ∞), o que fazia a largura tender a valores absurdos e a
                // animação de posição correr para um destino errado.
                double widgetWidth = MeasureNaturalWidth();
                ApplyWidth(widgetWidth);

                double targetLeft = workArea.Left + (workArea.Width - widgetWidth) / 2;
                double targetTop = workArea.Top + WidgetTopMargin;

                //Garantir que não sai da tela
                targetLeft = Math.Max(workArea.Left, Math.Min(targetLeft, workArea.Left + workArea.Width - widgetWidth));

                // Limpar qualquer animação anterior que possa travar propriedades
                this.BeginAnimation(Window.TopProperty, null);
                this.BeginAnimation(Window.LeftProperty, null);

                double currentTop = double.IsNaN(Top) ? 0 : Top;
                double currentLeft = double.IsNaN(Left) ? targetLeft : Left;

                //Animar para posição expandida (topo centro)
                var topAnimation = new DoubleAnimation
                {
                    From = currentTop,
                    To = targetTop,
                    Duration = TimeSpan.FromMilliseconds(EXPAND_ANIMATION_MS),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.HoldEnd
                };

                var leftAnimation = new DoubleAnimation
                {
                    From = currentLeft,
                    To = targetLeft,
                    Duration = TimeSpan.FromMilliseconds(EXPAND_ANIMATION_MS),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.HoldEnd
                };

                this.BeginAnimation(Window.TopProperty, topAnimation);
                this.BeginAnimation(Window.LeftProperty, leftAnimation);

                //Atualizar seta (aponta para CIMA = colapsar)
                UpdateCollapseArrow(false);

                //Atualizar config e estado
                _config.IsCollapsed = false;
                _config.PositionX = targetLeft;
                _config.PositionY = targetTop;
                _config.Save();
                _isCollapsed = false;
                UpdateBottomUi();

                // Forçar atualização do layout e redimensionamento da janela de volta ao tamanho expandido
                RefreshAdaptiveLayout();

                _logger?.LogInfo($"[Widget] Expandido: Topo Centro ({targetLeft:F0}, {targetTop:F0})");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[Widget] Erro em ExpandWidget: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Atualiza a rotação da seta indicadora
        /// </summary>
        /// <param name="isCollapsed">true = widget está escondido, seta aponta para BAIXO</param>
        private void UpdateCollapseArrow(bool isCollapsed)
        {
            try
            {
                var toggleButton = this.FindName("CollapseToggle") as Button;
                if (toggleButton != null)
                {
                    toggleButton.ApplyTemplate();
                    var rotateTransform = toggleButton.Template.FindName("ArrowRotation", toggleButton) as RotateTransform;
                    if (rotateTransform != null)
                    {
                        double targetAngle = isCollapsed ? 180 : 0;
                        // 180 = para cima (colapsado), 0 = para baixo (expandido)
                        var animation = new DoubleAnimation
                        {
                            From = rotateTransform.Angle,
                            To = targetAngle,
                            Duration = TimeSpan.FromMilliseconds(200),
                            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
                            FillBehavior = FillBehavior.HoldEnd
                        };
                        rotateTransform.BeginAnimation(RotateTransform.AngleProperty, animation);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[Widget] Erro em UpdateCollapseArrow: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Aplica estado colapsado ao iniciar (sem animação)
        /// </summary>
        private void RestoreCollapseState()
        {
            try
            {
                if (_config.IsCollapsed)
                {
                    // Agora sim ocultar o conteúdo principal sem colapsar o layout
                    MainContent.Visibility = Visibility.Hidden;

                    // Forçar o cálculo do layout com o conteúdo principal ocultado
                    this.UpdateLayout();
                    double collapsedHeight = ActualHeight > 0 ? ActualHeight : COLLAPSED_VISIBLE_HEIGHT;

                    // Limpar qualquer animação anterior
                    this.BeginAnimation(Window.TopProperty, null);
                    
                    // Posicionar usando a altura real encolhida
                    double targetVisible = _isHovering ? COLLAPSED_VISIBLE_HEIGHT : COLLAPSED_SLIVER_HEIGHT;
                    Top = -collapsedHeight + targetVisible;
                    _isCollapsed = true;
                    UpdateCollapseArrow(true);
                    _logger?.LogInfo($"[Widget] Estado colapsado restaurado: Top = {Top:F0} (CollapsedHeight = {collapsedHeight:F0})");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[Widget] Erro em RestoreCollapseState: {ex.Message}", ex);
            }

            UpdateBottomUi();
        }

        #region BOTTOM UI - Seta visível apenas no hover

        private bool _isHovering;

        private void MainContainer_MouseEnter(object sender, MouseEventArgs e)
        {
            _isHovering = true;
            UpdateBottomUi();
        }

        private void MainContainer_MouseLeave(object sender, MouseEventArgs e)
        {
            _isHovering = false;
            UpdateBottomUi();
        }

        /// <summary>
        /// Ajusta a área inferior do widget: a seta só aparece no hover (ou quando colapsado,
        /// pois é o único jeito de reabrir). Sem hover, o rodapé encolhe para não sobrar
        /// espaço abaixo da barra de progresso.
        /// </summary>
        private void UpdateBottomUi()
        {
            try
            {
                bool showHandle = _isHovering;

                if (showHandle)
                {
                    if (VisibleHandle.Visibility != Visibility.Visible)
                    {
                        VisibleHandle.Opacity = 0;
                        VisibleHandle.Visibility = Visibility.Visible;
                    }

                    if (VisibleHandle.Opacity < 1)
                    {
                        var fadeIn = new DoubleAnimation(VisibleHandle.Opacity, 1, TimeSpan.FromMilliseconds(150))
                        {
                            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                            FillBehavior = FillBehavior.HoldEnd
                        };
                        VisibleHandle.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                    }

                    AnimateProgressTrackMargin(new Thickness(10, 4, 10, 4));
                }
                else
                {
                    if (VisibleHandle.Visibility == Visibility.Visible && VisibleHandle.Opacity > 0)
                    {
                        var fadeOut = new DoubleAnimation(VisibleHandle.Opacity, 0, TimeSpan.FromMilliseconds(150))
                        {
                            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                            FillBehavior = FillBehavior.Stop
                        };
                        fadeOut.Completed += (s, a) =>
                        {
                            if (!_isHovering)
                            {
                                VisibleHandle.Visibility = Visibility.Collapsed;
                                VisibleHandle.Opacity = 0;
                            }
                        };
                        VisibleHandle.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                    }
                    else
                    {
                        VisibleHandle.Visibility = Visibility.Collapsed;
                        VisibleHandle.Opacity = 0;
                    }

                    AnimateProgressTrackMargin(new Thickness(10, 3, 10, 1));
                }

                RepositionCollapsedAfterUiChange();
            }
            catch (Exception ex)
            {
                _logger?.LogError("[Widget] Erro em UpdateBottomUi", ex);
            }
        }

        /// <summary>
        /// Reancora a janela quando colapsada após mudar a área inferior (seta/sliver).
        /// Sem hover mostra só a sliver da barra; no hover expande para barra + seta.
        /// </summary>
        private void RepositionCollapsedAfterUiChange()
        {
            if (!_isCollapsed || _isCollapseAnimating) return;

            try
            {
                this.UpdateLayout();
                double h = ActualHeight > 0 ? ActualHeight : COLLAPSED_SLIVER_HEIGHT;
                double targetTop = -h + (_isHovering ? COLLAPSED_VISIBLE_HEIGHT : COLLAPSED_SLIVER_HEIGHT);

                if (Math.Abs(Top - targetTop) > 0.5)
                {
                    this.BeginAnimation(Window.TopProperty, null);
                    var anim = new DoubleAnimation(Top, targetTop, TimeSpan.FromMilliseconds(120))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                        FillBehavior = FillBehavior.HoldEnd
                    };
                    this.BeginAnimation(Window.TopProperty, anim);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError("[Widget] Erro em RepositionCollapsedAfterUiChange", ex);
            }
        }

        private void AnimateProgressTrackMargin(Thickness target)
        {
            if (ProgressTrack == null || ProgressTrack.Margin == target) return;

            var anim = new ThicknessAnimation(ProgressTrack.Margin, target, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.HoldEnd
            };
            ProgressTrack.BeginAnimation(Border.MarginProperty, anim);
        }

        #endregion

        /// <summary>
        /// Garante que o widget esteja sempre visível na tela (safeguard)
        /// </summary>
        private void EnsureWidgetOnScreen()
        {
            try
            {
                if (!WindowPlacementHelper.TryGetWorkAreaInDips(this, out var work))
                {
                    work = new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
                }

                double screenWidth = work.Width;
                double screenHeight = work.Height;
                double w = ActualWidth > 0 ? ActualWidth : 320;
                double h = ActualHeight > 0 ? ActualHeight : 80;

                //Verificar se está completamente fora da tela
                bool isOffScreen = Left + w < work.Left || Left > work.Left + screenWidth
                                || Top + h < work.Top || Top > work.Top + screenHeight;

                if (isOffScreen && !_isCollapsed)
                {
                    //Recuperar: mover para topo centro
                    _logger?.LogWarning("[Widget] Widget fora da tela detectado, recuperando para topo centro");
                    ExpandWidget();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[Widget] Erro em EnsureWidgetOnScreen: {ex.Message}", ex);
            }
        }

        private bool _loadedHandled = false;

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _logger?.LogInfo("[Widget] Window_Loaded INICIADO");
                
                // Evitar execução múltipla de inicialização pesada
                if (_loadedHandled)
                {
                    Opacity = 1.0;
                    return;
                }
                _loadedHandled = true;

            // ── NADA DE GEOMETRIA AQUI ───────────────────────────────────
            // A versão anterior fazia, neste ponto:
            //   1. se Left/Top ainda fossem o placeholder -10000, recentralizar
            //      usando `Width` — que era NaN, produzindo Left = NaN;
            //   2. dois UpdateLayout() seguidos;
            //   3. Opacity = 0 e Visibility = Visible.
            //
            // Os dois UpdateLayout() eram o pior detalhe: eles rodavam ANTES do
            // primeiro quadro, e como SizeToContent/Width=Auto pode refazer a
            // medida, o primeiro quadro já saía com a largura errada. Era
            // exatamente o "abre gigante e depois encolhe" que o usuário via.
            //
            // Agora toda a geometria foi resolvida em ApplyInitialGeometry(), no
            // construtor. Este método só garante que a janela esteja visível e
            // Opacity = 0 (revelamento fica a cargo de ContentRendered). A
            // única correção que resta é a rede de segurança para o caso de
            // alguém reintroduzir um caminho que deixe a janela off-screen.
            if (Left <= -9000 || Top <= -9000 || double.IsNaN(Left) || double.IsNaN(Top))
            {
                try
                {
                    double w = WindowPlacementHelper.ResolveManagedWidth(this);
                    if (w <= 0 || double.IsNaN(w)) w = MeasureNaturalWidth();

                    if (WindowPlacementHelper.TryGetWorkAreaInDips(this, out var area))
                    {
                        Left = Math.Round(area.Left + (area.Width - w) / 2.0);
                        Top = Math.Round(area.Top + WidgetTopMargin);
                        _logger?.LogWarning(
                            $"[Widget] Loaded: posição inválida, corrigida para ({Left:F0}, {Top:F0}).");
                    }
                }
                catch (Exception posEx)
                {
                    _logger?.LogWarning($"[Widget] Loaded: falha ao corrigir posição: {posEx.Message}");
                }
            }

            Opacity = 0;
            Visibility = Visibility.Visible;
            WindowState = WindowState.Normal;
            RemoveFromAltTab();


                if (!_monitoringStarted)
                {
                    _monitoringStarted = true;
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            _viewModel?.StartMonitoring();
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError($"[Widget] Erro ao iniciar monitoring: {ex.Message}", ex);
                        }
                    }), DispatcherPriority.Background);
                }

                _layoutDebounceTimer.Stop();
                _layoutDebounceTimer.Start();

                _logger?.LogInfo($"[Widget] Loaded - Geometria: ({Left:F1}, {Top:F1}) {Width:F1}x{Height:F1}");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[Widget] Load error: {ex.Message}", ex);
            }
        }

        private void Window_ContentRendered(object? sender, EventArgs e)
        {
            ContentRendered -= Window_ContentRendered;
            
            try
            {
                // ── REVELA O WIDGET ──────────────────────────────────────
                // Este é o ÚNICO lugar onde a janela fica visível.
                //
                // Como a largura já é FINITA e CONHECIDA desde o construtor
                // (ApplyInitialGeometry), não existe mais medida pendente para
                // este evento resolver: ele não altera Width/Left/Top. O
                // fade-in parte de uma geometria estável, então não há o
                // salto visual de "gigante → tamanho padrão".
                if (_collapsePendingOnReveal)
                {
                    _collapsePendingOnReveal = false;
                    try
                    {
                        // Ainda com Opacity = 0: a altura real já está disponível,
                        // então o collapse é calculado corretamente e o usuário
                        // nunca vê um quadro intermediário.
                        RestoreCollapseState();
                        _logger?.LogInfo(
                            $"[Widget] Estado colapsado aplicado na revelação: " +
                            $"Top={Top:F0} Height={ActualHeight:F0}");
                    }
                    catch (Exception collapseEx)
                    {
                        _logger?.LogError($"[Widget] Falha ao aplicar collapse na revelação: {collapseEx.Message}", collapseEx);
                    }
                }

                if (WindowPlacementHelper.TryGetWorkAreaInDips(this, out var area))
                {
                    _logger?.LogInfo(
                        $"[Widget] ContentRendered — {Width:F0}x{Height:F0} " +
                        $"em ({Left:F0}, {Top:F0}) | work area {area.X:F0},{area.Y:F0} " +
                        $"{area.Width:F0}x{area.Height:F0}");
                }
                else
                {
                    _logger?.LogInfo(
                        $"[Widget] ContentRendered — {Width:F0}x{Height:F0} em ({Left:F0}, {Top:F0})");
                }

                // Revelação com fade. Só é seguro AGORA porque a geometria já é
                // final: antes, o fade era disparado por RefreshAdaptiveLayout
                // (~100 ms depois do Loaded), o que produzia o pior dos dois
                // mundos — o widget aparecia instantaneamente e então "reaparecia"
                // fading de 0 para 1. Animar a partir de uma geometria estável
                // não tem custo visual nenhum.
                ApplyFadeIn();

                ApplyTransparency(SettingsService.Instance.Settings.EnableTransparency);
                InitializeTransparencySystem();
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[Widget] ContentRendered error: {ex.Message}", ex);
                // Nunca deixar o widget invisível por causa de um erro aqui.
                try { Opacity = 1.0; } catch { }
            }
        }

        /// <summary>
        /// Sincronização ao alternar visibilidade:
        /// Ao reexibir, garante Opacity total, reativa atualizações e recalcula layout suavemente.
        /// Ao ocultar, pausa as atualizações do ViewModel para economizar recursos.
        /// </summary>
        private void Window_IsVisibleChanged(object? sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is not bool isVisible) return;

            if (isVisible)
            {
                Opacity = 1.0;
                if (_viewModel != null)
                {
                    _viewModel.IsVisible = true;
                }

                _layoutDebounceTimer.Stop();
                _layoutDebounceTimer.Start();
            }
            else
            {
                if (_viewModel != null)
                {
                    _viewModel.IsVisible = false;
                }
            }
        }

        #region TRANSPARÊNCIA - Sistema sincronizado com MainWindow

        /// <summary>
        /// Inicializa o sistema de transparência com monitoramento em tempo real
        /// </summary>
        private void InitializeTransparencySystem()
        {
            try
            {
                if (_transparencySystemInitialized) return;
                _transparencySystemInitialized = true;

                // Aplicar estado inicial
                ApplyTransparency(SettingsService.Instance?.Settings?.EnableTransparency ?? true);

                // PERFORMANCE OPTIMIZATION: Substituir polling de 3s por subscrição a evento de settings
                // O MainWindow.TransparencyToggleButton_Click já notifica via App.WidgetManager.UpdateTransparency()
                // portanto o polling é redundante e desperdiça CPU.
                // Mantemos apenas a aplicação inicial:
                _logger?.LogInfo("[Widget] Transparency system initialized (event-driven, no polling)");
                _logger?.LogInfo($"[Widget] Acrylic applied (enabled={(SettingsService.Instance?.Settings?.EnableTransparency ?? true)})");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[Widget] Erro ao inicializar sistema de transparência: {ex.Message}");
            }
        }

        /// <summary>
        /// Verifica se houve mudança no estado de transparência e aplica se necessário
        /// </summary>
        private void CheckAndApplyTransparencyChanges()
        {
            try
            {
                bool currentTransparency = SettingsService.Instance?.Settings?.EnableTransparency ?? true;

                if (currentTransparency != _lastTransparencyState)
                {
                    _logger?.LogInfo($"[Widget] Mudança de transparência detectada: {_lastTransparencyState} -> {currentTransparency}");
                    ApplyTransparency(currentTransparency);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug($"[Widget] Erro ao verificar transparência: {ex.Message}");
            }
        }

        private void ApplyFadeIn()
        {
            //Iniciar invisível e fazer fade-in suave (evita flash)
            Opacity = 0;
            var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new QuadraticEase(),
                FillBehavior = FillBehavior.HoldEnd
            };
            // CORREÇÃO: Após o fade, fixar Opacity base = 1 e parar a animação.
            // Evita que um Hide()/Show() posterior perca o clock da animação e
            // reverta a janela para o valor base (0) do XAML, ficando invisível.
            anim.Completed += (s, e) =>
            {
                BeginAnimation(OpacityProperty, null);
                Opacity = 1.0;
            };
            BeginAnimation(OpacityProperty, anim);
        }

        private void RemoveFromAltTab()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var style = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        }

        private void EnsureOnScreen()
        {
            if (_isCollapsed) return;
            try
            {
                // [FIX:WIDGET-MONITOR] A área de trabalho precisa ser a do monitor em
                // que o widget ESTÁ, não a do monitor primário.
                // SystemParameters.PrimaryScreen* é sempre o primário: em um setup
                // com dois monitores o widget era empurrado para fora da tela ao
                // abrir no secundário.
                if (!WindowPlacementHelper.TryGetWorkAreaInDips(this, out var work))
                {
                    work = SystemParameters.WorkArea;
                }

                double sw = work.Width;
                double sh = work.Height;

                //CORREÇÃO: Aguardar layout se necessário
                if (ActualWidth <= 1 || ActualHeight <= 1)
                {
                    UpdateLayout();
                }

                double w = ActualWidth > 0 ? ActualWidth : 320;
                double h = ActualHeight > 0 ? ActualHeight : 90;

                //CORREÇÃO: Garantir que pelo menos parte da janela esteja visível
                const double visibleMargin = 50;

                // [FIX:M-8] A "margem visível" não pode ser maior que a própria
                // janela. O widget colapsado tem altura real de ~36px; com
                // visibleMargin = 50, o teste original virava
                //   Top < -(36 - 50)  =>  Top < +14
                // e por isso reiniciava para SCREEN_MARGIN uma posição válida
                // (Top = 0), disparando "Posição Y muito negativa" a cada
                // passe de layout sem que houvesse qualquer problema real.
                //
                // A exigência é limitada a no máximo metade da dimensão, o que
                // torna a condição satisfeitível dentro da tela.
                double minVisibleX = Math.Min(visibleMargin, w / 2.0);
                double minVisibleY = Math.Min(visibleMargin, h / 2.0);

                //Esquerda
                if (Left + w < minVisibleX)
                {
                    _logger?.LogWarning($"[Widget] Posição X muito negativa ({Left:F1}), corrigindo... [FIX:M-8] limiarVisivel={minVisibleX:F0}px");
                    Left = work.Left;
                }

                //Topo
                if (Top + h < minVisibleY)
                {
                    _logger?.LogWarning($"[Widget] Posição Y muito negativa ({Top:F1}), corrigindo... [FIX:M-8] limiarVisivel={minVisibleY:F0}px altura={h:F0}px");
                    Top = work.Top;
                }

                //Direita
                if (Left + visibleMargin > work.Left + sw)
                {
                    _logger?.LogWarning($"[Widget] Posição X muito direita ({Left:F1}), corrigindo...");
                    Left = work.Left + sw - w - SCREEN_MARGIN;
                }

                //Baixo
                if (Top + visibleMargin > work.Top + sh)
                {
                    _logger?.LogWarning($"[Widget] Posição Y muito abaixo ({Top:F1}), corrigindo...");
                    Top = work.Top + sh - h - SCREEN_MARGIN;
                }

                //Garantir mínimos absolutos
                if (Left < work.Left)
                    Left = work.Left;
                if (Top < work.Top)
                    Top = work.Top;

                _logger?.LogInfo($"[Widget] EnsureOnScreen: WorkArea = ({work.X:F0}, {work.Y:F0}) {sw:F0} x {sh:F0}, Window = {w:F1} x {h:F1}, FinalPos = ({Left:F1}, {Top:F1})");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[Widget] EnsureOnScreen error: {ex.Message}");
                Left = 100;
                Top = 100;
            }
        }

        /// <summary>
        /// Salva a posição atual do widget (apenas se não estiver colapsado)
        /// </summary>
        private void SavePosition()
        {
            //NÃO salvar posição se widget estiver colapsado (estaria fora da tela)
            if (_isCollapsed)
                return;

            _config.PositionX = Left;
            _config.PositionY = Top;
            _config.HasSavedPosition = true;
            _config.IsCollapsed = false;

            //Garantir que estado está correto
            _config.Save();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            SavePosition();
            _viewModel?.StopMonitoring();
            _viewModel?.Dispose();
            base.OnClosing(e);
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            //Event handler para XAML - chama o mesmo código do OnClosing
            SavePosition();
            _viewModel?.StopMonitoring();
            _viewModel?.Dispose();
        }

        //DRAG - Usando DragMove() nativo do WPF (mais confiável, sem cálculos manuais)
        private void DragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            //NÃO permitir drag se widget estiver colapsado
            if (_isCollapsed)
            {
                _logger?.LogDebug("[Widget] Drag bloqueado - widget colapsado");
                return;
            }
            //Marcar que iniciou drag (para detectar click vs drag no MouseUp)
            _isDragging = true;
            _dragStartPoint = e.GetPosition(this);
            _dragStartTime = DateTime.Now;

            //Iniciar drag nativo do WPF - trata todo o movimento automaticamente
            try
            {
                DragMove();
            }
            catch
            {
            }
            _logger?.LogDebug($"[Widget] Drag iniciado em ({_dragStartPoint.X:F0}, {_dragStartPoint.Y:F0})");
        }

        private void DragArea_MouseMove(object sender, MouseEventArgs e)
        {
            //DragMove() trata o movimento automaticamente
            //Este evento só verifica se houve movimento significativo
            if (_isDragging && e.LeftButton == MouseButtonState.Pressed)
            {
                var currentPos = e.GetPosition(this);
                var distance = Math.Sqrt(Math.Pow(currentPos.X - _dragStartPoint.X, 2) + Math.Pow(currentPos.Y - _dragStartPoint.Y, 2));
                if (distance > MIN_DRAG_DISTANCE)
                    _hasMovedSignificantly = true;
            }
        }

        private void DragArea_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;

            //NÃO processar drag se widget estiver colapsado
            if (_isCollapsed)
            {
                _logger?.LogDebug("[Widget] Drag ignorado - widget está colapsado");
                _hasMovedSignificantly = false;
                return;
            }

            var dragDuration = DateTime.Now - _dragStartTime;

            //Só aplicar snap e salvar se foi um drag real (não um click rápido)
            if (_hasMovedSignificantly || dragDuration.TotalMilliseconds > 200)
            {
                ApplySnapIfNearEdge();
                SavePosition();
                _logger?.LogDebug($"[Widget] Drag finalizado - Pos: ({Left:F1}, {Top:F1}), Duration: {dragDuration.TotalMilliseconds:F0} ms");
            }
            else
            {
                _logger?.LogDebug("[Widget] Click detectado, ignorando snap");
            }

            // Resetar flag para próximo drag
            _hasMovedSignificantly = false;
        }

        /// <summary>
        /// Aplica snap SUAVE apenas se estiver muito próximo da borda
        /// (não força para margem, apenas ajusta se estiver quase lá)
        /// </summary>
        private void ApplySnapIfNearEdge()
        {
            // [FIX:WIDGET-MONITOR] O snap precisa conhecer as bordas do monitor em
            // que o widget está, e não as do primário. A versão anterior usava
            // SystemParameters.PrimaryScreen* e testava `Left >= 0`, o que é
            // literalmente falso em qualquer monitor secundário: um monitor à
            // esquerda do primário tem coordenadas X negativas, então o snap da
            // esquerda nunca disparava e o snap da direita encravava o widget
            // dentro do monitor errado.
            if (!WindowPlacementHelper.TryGetWorkAreaInDips(this, out var work))
            {
                work = new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
            }

            double right = work.Left + work.Width;
            double bottom = work.Top + work.Height;

            double w = ActualWidth > 0 ? ActualWidth : 320;
            double h = ActualHeight > 0 ? ActualHeight : 90;
            bool snapped = false;

            // Snap para esquerda (se estiver muito próximo)
            if (Left - work.Left < SNAP_DISTANCE && Left >= work.Left)
            {
                Left = work.Left;
                snapped = true;
            }

            // Snap para topo (se estiver muito próximo)
            if (Top - work.Top < SNAP_DISTANCE && Top >= work.Top)
            {
                Top = work.Top;
                snapped = true;
            }

            // Snap para direita
            if (right - (Left + w) < SNAP_DISTANCE && right - (Left + w) > -SNAP_DISTANCE)
            {
                Left = right - w;
                snapped = true;
            }

            // Snap para baixo
            if (bottom - (Top + h) < SNAP_DISTANCE && bottom - (Top + h) > -SNAP_DISTANCE)
            {
                Top = bottom - h;
                snapped = true;
            }

            if (snapped)
                _logger?.LogDebug($"[Widget] Snap aplicado: ({Left:F1}, {Top:F1})");
        }

        // CONTEXT MENU
        private void DragArea_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            var vm = _viewModel;
            if (vm == null) return;

            var menuBg      = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E81A1A2E"));
            var menuBorder  = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#40FFFFFF"));
            var headerBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B31FF"));
            var itemFg      = new SolidColorBrush(Colors.White);
            var dangerBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6B6B"));

            string TranslateWidgetText(string text)
            {
                if (string.IsNullOrEmpty(text)) return text;
                
                var lang = VoltrisOptimizer.Services.LocalizationService.Instance.CurrentLanguage;
                if (lang == VoltrisOptimizer.Services.Language.Portuguese)
                    return text;

                var dict = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<VoltrisOptimizer.Services.Language, string>>(StringComparer.OrdinalIgnoreCase)
                {
                    // Headers
                    { "OVERLAY / VISIBILIDADE", new() {
                        { VoltrisOptimizer.Services.Language.English, "OVERLAY / VISIBILITY" },
                        { VoltrisOptimizer.Services.Language.Spanish, "SUPERPOSICIÓN / VISIBILIDAD" }
                    }},
                    { "MODO GAMER", new() {
                        { VoltrisOptimizer.Services.Language.English, "GAMER MODE" },
                        { VoltrisOptimizer.Services.Language.Spanish, "MODO GAMER" }
                    }},
                    { "MÉTRICAS VISÍVEIS", new() {
                        { VoltrisOptimizer.Services.Language.English, "VISIBLE METRICS" },
                        { VoltrisOptimizer.Services.Language.Spanish, "MÉTRICAS VISIBLES" }
                    }},
                    { "POSIÇÃO", new() {
                        { VoltrisOptimizer.Services.Language.English, "POSITION" },
                        { VoltrisOptimizer.Services.Language.Spanish, "POSICIÓN" }
                    }},
                    { "SISTEMA", new() {
                        { VoltrisOptimizer.Services.Language.English, "SYSTEM" },
                        { VoltrisOptimizer.Services.Language.Spanish, "SISTEMA" }
                    }},
                    { "AÇÕES", new() {
                        { VoltrisOptimizer.Services.Language.English, "ACTIONS" },
                        { VoltrisOptimizer.Services.Language.Spanish, "ACCIONES" }
                    }},

                    // Items
                    { "Mostrar sobre outros aplicativos", new() {
                        { VoltrisOptimizer.Services.Language.English, "Show Over Other Apps" },
                        { VoltrisOptimizer.Services.Language.Spanish, "Mostrar sobre outras aplicações" }
                    }},

                    { "Modo inteligente (auto ocultar)", new() {
                        { VoltrisOptimizer.Services.Language.English, "Smart Mode (Auto Hide)" },
                        { VoltrisOptimizer.Services.Language.Spanish, "Modo inteligente (auto ocultar)" }
                    }},
                    { "Ativar apenas durante jogos", new() {
                        { VoltrisOptimizer.Services.Language.English, "Activate Only During Games" },
                        { VoltrisOptimizer.Services.Language.Spanish, "Activar solo durante jogos" }
                    }},
                    { "CPU", new() {
                        { VoltrisOptimizer.Services.Language.English, "CPU" },
                        { VoltrisOptimizer.Services.Language.Spanish, "CPU" }
                    }},
                    { "RAM", new() {
                        { VoltrisOptimizer.Services.Language.English, "RAM" },
                        { VoltrisOptimizer.Services.Language.Spanish, "RAM" }
                    }},
                    { "Disco", new() {
                        { VoltrisOptimizer.Services.Language.English, "Disk" },
                        { VoltrisOptimizer.Services.Language.Spanish, "Disco" }
                    }},
                    { "Temperatura", new() {
                        { VoltrisOptimizer.Services.Language.English, "Temperature" },
                        { VoltrisOptimizer.Services.Language.Spanish, "Temperatura" }
                    }},
                    { "FPS (durante jogos)", new() {
                        { VoltrisOptimizer.Services.Language.English, "FPS (During Games)" },
                        { VoltrisOptimizer.Services.Language.Spanish, "FPS (durante juegos)" }
                    }},

                    { "Abrir Voltris Dashboard", new() {
                        { VoltrisOptimizer.Services.Language.English, "Open Voltris Dashboard" },
                        { VoltrisOptimizer.Services.Language.Spanish, "Abrir Voltris Dashboard" }
                    }},
                    { "Iniciar com o Windows", new() {
                        { VoltrisOptimizer.Services.Language.English, "Start with Windows" },
                        { VoltrisOptimizer.Services.Language.Spanish, "Iniciar con Windows" }
                    }},
                    { "Fechar Widget", new() {
                        { VoltrisOptimizer.Services.Language.English, "Close Widget" },
                        { VoltrisOptimizer.Services.Language.Spanish, "Cerrar Widget" }
                    }}
                };

                if (dict.TryGetValue(text, out var languages) && languages.TryGetValue(lang, out var translated))
                {
                    return translated;
                }

                return text;
            }

            System.Windows.Shapes.Path CreateCheckMark()
            {
                return new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M9.75 17.27L4.53 12.05 3 13.59 9.75 20.34 21 9.09 19.47 7.66z"),
                    Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B31FF")),
                    Stretch = Stretch.Uniform,
                    Width = 14,
                    Height = 14};
            }

            MenuItem MakeItem(string header, ICommand? cmd, bool checkable = false, bool isChecked = false, Brush? fg = null)
            {
                var item = new MenuItem
                {
                    Header      = TranslateWidgetText(header),
                    IsCheckable = checkable,
                    IsChecked   = isChecked,
                    Command     = cmd,
                    Background  = Brushes.Transparent,
                    FontSize    = 12,
                    FontFamily  = new FontFamily("Segoe UI"),
                    Padding     = new Thickness(12, 8, 12, 8),
                    Height      = 36};

                if (checkable)
                {
                    item.Icon = isChecked ? CreateCheckMark() : null;
                    item.Opacity = isChecked ? 1.0 : 0.5;
                    item.Foreground = fg ?? (isChecked
                        ? Brushes.White
                        : new SolidColorBrush(Color.FromArgb(160, 180, 180, 190)));
                }
                else
                {
                    item.Foreground = fg ?? itemFg;
                }

                item.MouseEnter += (s, _) => ((MenuItem)s).Background = new SolidColorBrush(Color.FromArgb(48, 255, 255, 255));
                item.MouseLeave += (s, _) => ((MenuItem)s).Background = Brushes.Transparent;
                return item;
            }

            TextBlock MakeHeader(string text) => new TextBlock
            {
                Text       = TranslateWidgetText(text),
                Foreground = headerBrush,
                FontSize   = 10,
                FontWeight = FontWeights.SemiBold,
                Margin     = new Thickness(12, 8, 12, 4),
                Opacity    = 0.9};

            Separator MakeSep() => new Separator
            {
                Background = new SolidColorBrush(Color.FromArgb(32, 255, 255, 255)),
                Height     = 1,
                Margin     = new Thickness(12, 4, 12, 4),
                Opacity    = 0.5};

            var menu = new ContextMenu
            {
                Background      = menuBg,
                BorderBrush     = menuBorder,
                BorderThickness = new Thickness(1),
                Padding         = new Thickness(4),
                Effect          = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color       = Colors.Black,
                    BlurRadius  = 20,
                    ShadowDepth = 8,
                    Opacity     = 0.5
                }
            };

            // OVERLAY / VISIBILIDADE
            menu.Items.Add(MakeHeader("OVERLAY / VISIBILIDADE"));
            menu.Items.Add(MakeItem("Modo inteligente (auto ocultar)",  vm.ToggleSmartModeCommand,      checkable: true, isChecked: vm.SmartMode));
            menu.Items.Add(MakeSep());

            // MODO GAMER
            menu.Items.Add(MakeHeader("MODO GAMER"));
            menu.Items.Add(MakeItem("Ativar apenas durante jogos",      vm.ToggleGamerOnlyModeCommand,  checkable: true, isChecked: vm.GamerOnlyMode));
            menu.Items.Add(MakeSep());

            // MÉTRICAS VISÍVEIS
            menu.Items.Add(MakeHeader("MÉTRICAS VISÍVEIS"));
            menu.Items.Add(MakeItem("CPU",                              vm.ToggleCpuCommand,       checkable: true, isChecked: vm.ShowCpu));
            menu.Items.Add(MakeItem("RAM",                              vm.ToggleRamCommand,       checkable: true, isChecked: vm.ShowRam));
            menu.Items.Add(MakeItem("Disco",                            vm.ToggleDiskCommand,      checkable: true, isChecked: vm.ShowDisk));
            menu.Items.Add(MakeItem("Temperatura",                      vm.ToggleTempCommand,      checkable: true, isChecked: vm.ShowTemp));
            menu.Items.Add(MakeItem("FPS (durante jogos)",              vm.ToggleFpsMetricCommand, checkable: true, isChecked: vm.ShowFpsMetric));
            menu.Items.Add(MakeSep());



            // SISTEMA
            menu.Items.Add(MakeHeader("SISTEMA"));
            menu.Items.Add(MakeItem("Abrir Voltris Dashboard",          vm.OpenDashboardCommand));
            menu.Items.Add(MakeItem("Iniciar com o Windows",            vm.ToggleStartupCommand,   checkable: true, isChecked: vm.StartWithWindows));
            menu.Items.Add(MakeSep());

            // AÇÕES
            menu.Items.Add(MakeHeader("AÇÕES"));
            menu.Items.Add(MakeItem("Fechar Widget",                    new RelayCommand(_ => Close()), fg: dangerBrush));

            menu.PlacementTarget = this;
            menu.IsOpen          = true;
        }

        #endregion

        #region ADAPTIVE LAYOUT SYSTEM

        private double _targetWidth = 0;

        private bool _isFirstLayout = true;

        /// <summary>
        /// Ajusta o tamanho da janela dinamicamente para caber o conteúdo visível (CPU/RAM/DISK + TEMP/FPS)
        /// sem deixar espaços vazios.
        /// </summary>
        private bool _isRefreshingLayout;
        private DateTime _lastLayoutRefresh = DateTime.MinValue;
        private static readonly TimeSpan LayoutRefreshMinInterval = TimeSpan.FromSeconds(1);
        private volatile bool _gamingModeActive;

        public void SetGamingModeActive(bool active)
        {
            _gamingModeActive = active;
        }

        public void StopFpsMonitorForAntiCheat()
        {
            _viewModel?.StopFpsMonitorForAntiCheat();
        }

        private void RefreshAdaptiveLayout()
        {
            if (MainContainer == null) return;
            if (_isCollapsed) return;
            if (_isRefreshingLayout) return;
            if (_gamingModeActive) return;

            _isRefreshingLayout = true;

            try
            {
                // A geometria de abertura já foi resolvida em ApplyInitialGeometry.
                // Este método existe apenas para RECOLHER ou EXPANDIR a largura
                // quando o usuário liga/desliga métricas no menu de contexto, e é
                // a partir daqui que a janela volta a poder mudar de tamanho.
                double endWidth = MeasureNaturalWidth();

                if (_isFirstLayout)
                {
                    _isFirstLayout = false;
                    _targetWidth = endWidth;
                    ApplyWidth(endWidth);
                    return;
                }

                double startWidth = _targetWidth > 0 ? _targetWidth : ActualWidth;

                if (Math.Abs(startWidth - endWidth) < 1.0)
                {
                    _targetWidth = endWidth;
                    return;
                }

                _targetWidth = endWidth;

                // Se o usuário NUNCA moveu o widget, ele permanece centralizado no
                // topo ao redor do MESMO centro quando a largura muda — como toda
                // overlay profissional. Se já foi movido, a posição é do usuário e
                // é preservada; só se garante que não fique parcialmente fora.
                if (!_config.HasSavedPosition)
                {
                    double center = Left + startWidth / 2.0;
                    ApplyWidth(endWidth);
                    Left = Math.Round(center - endWidth / 2.0);
                }
                else
                {
                    ApplyWidth(endWidth);
                }

                EnsureOnScreen();
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[Widget] Erro no RefreshAdaptiveLayout: {ex.Message}", ex);
            }
            finally
            {
                _isRefreshingLayout = false;
            }
        }

        /// <summary>
        /// Aplica uma largura ao widget e à sua árvore, mantendo tudo em acordo.
        ///
        /// <see cref="LayerHost"/> recebe a largura EXPLÍCITA de propósito. Ele é
        /// o Grid que hospeda o Canvas decorativo; sem largura declarada ele
        /// esticaria para o DesiredSize do Canvas (~270 px) e a janela voltaria a
        /// ter a faixa transparente extra à direita. Com a largura travada aqui, a
        /// camada decorativa é recortada pelo conteúdo e nunca mais participa do
        /// cálculo de tamanho.
        ///
        /// Tudo é arredondado para pixel inteiro: largura fracionária numa janela
        /// faz o DWM arredondar para o pixel seguinte e o conteúdo cortar meio
        /// pixel na borda.
        /// </summary>
        private void ApplyWidth(double width)
        {
            double rounded = Math.Round(width);
            Width = rounded;
            if (MainContainer != null) MainContainer.Width = rounded;
            if (LayerHost != null) LayerHost.Width = rounded;
        }

        #endregion

        private void OnViewModelCloseRequested(object sender, EventArgs e) => Close();

        private double _lastAnimatedProgress = -1;

        private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(WidgetViewModel.CpuVisible)
                or nameof(WidgetViewModel.RamVisible)
                or nameof(WidgetViewModel.DiskVisible)
                or nameof(WidgetViewModel.TempMenuVisible)
                or nameof(WidgetViewModel.TemperatureVisible)
                or nameof(WidgetViewModel.FpsVisible))
            {
                _layoutDebounceTimer.Stop();
                _layoutDebounceTimer.Start();
            }
            else if (e.PropertyName == nameof(WidgetViewModel.ProgressValue))
            {
                if (!this.IsVisible || ProgressScale == null) return;

                double val = _viewModel.ProgressValue;
                if (Math.Abs(val - _lastAnimatedProgress) < 0.5) return;
                _lastAnimatedProgress = val;

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (ProgressScale != null && this.IsVisible)
                    {
                        double targetScale = Math.Max(0.0, Math.Min(1.0, val / 100.0));
                        int durationMs = (_viewModel.IsOptimizing || GlobalProgressService.Instance.IsOperationRunning) ? 300 : 500;
                        var anim = new DoubleAnimation(targetScale, TimeSpan.FromMilliseconds(durationMs))
                        {
                            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                        };
                        ProgressScale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
                    }
                }), DispatcherPriority.Normal);
            }
        }

        #region IDisposable

        public void Dispose()
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.CloseRequested -= OnViewModelCloseRequested;

            // Parar timers
            _transparencySystemInitialized = false;
            _monitoringStarted = false;
            _transparencyCheckTimer?.Stop();
            _transparencyCheckTimer = null;
            _layoutDebounceTimer?.Stop();

            _viewModel?.Dispose();
            Close();
        }

        private void ApplyTransparencyEffect()
        {
            bool enableTransparency = VoltrisOptimizer.Services.SettingsService.Instance?.Settings?.EnableTransparency ?? true;
            ApplyTransparency(enableTransparency);
        }

        #endregion
    }
}
