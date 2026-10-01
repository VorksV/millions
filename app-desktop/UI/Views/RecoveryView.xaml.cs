using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Drivers;
namespace VoltrisOptimizer.UI.Views
{
    /// <summary>
    /// Central de Recuperação.
    ///
    /// O layout está em <see cref="RecoveryView.xaml"/>; este code-behind cuida da
    /// criação da view, do guia e — a parte que XAML sozinho não resolve — dos
    /// TRÊS REGIMES DE LAYOUT.
    ///
    /// POR QUE HÁ CÓDIGO AQUI
    /// ─────────────────────
    /// WPF não tem media query. Tudo que depende da LARGURA DISPONÍVEL —
    /// colapsar a barra lateral, empilhar dois cards, trocar a barra de abas
    /// de lateral para horizontal — não tem como ser decidido por um Style ou
    /// por um Trigger de Property. Alguém precisa medir. Esse alguém é este
    /// arquivo, e só ele.
    ///
    /// O que este arquivo NÃO faz: conhecer o ViewModel, ler estado de
    /// negócio ou formatar texto. Ele mexe em ColumnDefinition.Width,
    /// Grid.Row/Grid.Column e Visibility. Se amanhã o layout mudar, quem muda
    /// é o XAML; este arquivo só continua apontando para os mesmos x:Name.
    ///
    /// OS TRÊS REGIMES
    /// ────────────────
    /// A janela mínima do app é 640x600 e a sidebar principal ocupa 240px
    /// (56px no modo trilho). Isso deixa a área de conteúdo entre ~400px e
    /// ~900px de largura, e uns 480px de altura útil. Três regimes cabem
    /// nesse intervalo:
    ///
    ///   WIDE     >= 1000px — barra lateral de 238px, abas com ícone + rótulo.
    ///   COMPACT  >=  720px — barra vira trilho de ~56px, SÓ ÍCONES. Sem
    ///             barra de acento e sem padding lateral, que no trilro
    ///             comeriam metade do ícone.
    ///   NARROW   <   720px — barra vira uma faixa HORIZONTAL no topo, com
    ///             rolagem própria, e o conteúdo desce para baixo.
    ///
    /// Além do chrome, o regime também decide o que é EMPILHÁVEL dentro das
    /// abas: em NARROW os dois cards da aba de arquivos viram um sobre o outro
    /// e o motor de varredura perde as descrições, porque a altura útil não
    /// comporta os dois. É a troca correta: menos texto, mais dado visível.
    /// </summary>
    public partial class RecoveryView : UserControl
    {
        #region Breakpoints

        /// <summary>Abaixo disto a barra lateral vira trilho de ícones.</summary>
        private const double CompactBreakpoint = 1000.0;

        /// <summary>Abaixo disto a barra lateral vira faixa horizontal no topo.</summary>
        private const double NarrowBreakpoint = 720.0;

        private const double SideWidthWide = 252.0;
        private const double SideWidthCompact = 56.0;
        private const double SideGap = 14.0;

        private enum LayoutMode { Wide, Compact, Narrow }

        private LayoutMode _mode = LayoutMode.Wide;
        private bool _modeApplied;

        #endregion

        public RecoveryView()
        {
            using var op = new DriverOperationScope("RECOVERY_VIEW_CREATE", nameof(RecoveryView));
            try
            {
                InitializeComponent();

                // ── CAMADA 3 (WPF) ──────────────────────────────────────
                // Esta camada não logava NADA no ciclo de vida da página, e
                // sem isso três causas muito diferentes apareciam com o mesmo
                // sintoma na tela ("não recuperou nada"):
                //   (a) a aba nem renderizou — DataContext nulo ou ViewModel
                //       ausente: problema de WPF;
                //   (b) o botão foi clicado mas o comando não disparou —
                //       problema de binding no XAML;
                //   (c) o comando disparou e a engine recusou — problema de
                //       C++/DLL.
                // O log abaixo separa os três em uma linha.
                Log($"view construida | DataContext={DataContext?.GetType().Name ?? "NULO"}" +
                    $" | ViewModel={(DataContext is ViewModels.RecoveryViewModel ? "OK" : "AUSENTE")}" +
                    $" | abas={TabHost.Items.Count}" +
                    $" | engine carregada={VoltrisOptimizer.Services.Recovery.RecoveryEngineInterop.IsEngineCompatible}");

                Loaded += OnPageLoaded;
                Unloaded += OnPageUnloaded;
                DataContextChanged += OnDataContextChanged;

                // O primeiro regime é aplicado no Loaded, e não no
                // construtor: no construtor o ActualWidth ainda é 0 e a
                // medição daria o regime errado.
                Loaded += (_, _) => ApplyMode(MeasureMode());
                op.Succeed("view criada");
            }
            catch (Exception ex)
            {
                op.Fail("falha ao criar a view de recuperação", ex);
                throw;
            }
        }

        private void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as ViewModels.RecoveryViewModel;
            Log($"pagina CARREGADA | modo={_mode} | largura={ActualWidth:0}" +
                $" | unidades={vm?.AvailableDrives.Count.ToString() ?? "?"}" +
                $" | pontosRestauracao={vm?.Operations.RestorePoints.Count.ToString() ?? "?"}" +
                $" | backups={vm?.Operations.Backups.Count.ToString() ?? "?"}" +
                $" | varreduraEmAndamento={vm?.IsScanning.ToString() ?? "?"}");
        }

        private void OnPageUnloaded(object sender, RoutedEventArgs e)
        {
            Log("pagina DESCARREGADA (navegou para outra tela)");
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            Log($"DataContext MUDOU: {e.OldValue?.GetType().Name ?? "nulo"} -> {e.NewValue?.GetType().Name ?? "nulo"}");
        }

        /// <summary>
        /// Log da camada WPF, em um método só — para o destino poder ser
        /// trocado sem tocar em todos os pontos de chamada. Nunca deixa a
        /// falha de log derrubar a UI.
        /// </summary>
        private static void Log(string message)
        {
            string line = "[WPF] " + message;
            try { App.LoggingService?.LogInfo("[RecoveryView] " + message); } catch { }
            System.Diagnostics.Debug.WriteLine("[RecoveryView] " + line);
        }

        /// <summary>
        /// Só a MOLDURA (barra lateral, trilho, faixa no topo) é cara: involves
        /// Template.FindName, CornerRadius, Padding e TabStripPlacement. Ela só
        /// roda quando o REGIME muda.
        ///
        /// Já o empilhamento dos cards dentro das abas é barato e depende da
        /// LARGURA, não do regime — então tem de rodar a cada resize. A versão
        /// anterior devolvia cedo quando o regime não mudava, e isso fazia a
        /// aba de arquivos continuar lado a lado a 430px: o regime já era
        /// "Narrow" desde 660px, então o segundo resize era descartado e o
        /// limiar de empilhamento nunca era reavaliado com a largura nova.
        /// </summary>
        private void OnRecoverySizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (ActualWidth <= 0) return;

            LayoutMode next = MeasureMode();
            if (next != _mode || !_modeApplied)
            {
                ApplyMode(next);
            }
            else
            {
                ApplyStackedLayout(_mode, AvailableWidth());
            }
        }

        /// <summary>
        /// Largura que o conteúdo das abas vai REALMENTE ter, calculada
        /// analiticamente a partir do regime.
        ///
        /// A primeira versão media <c>MainHost.ActualWidth</c> e estava errada
        /// por um quadro: o SizeChanged dispara ANTES do MainHost ser
        /// re-medido, então a decisão usava a largura do layout ANTERIOR. Na
        /// sequência 1120 -> 900 -> 660 -> 430, a última chamada media a
        /// largura de 660 e concluía que havia espaço para os dois cards lado a
        /// lado — estourando a página em 430.
        ///
        /// Cálculo fechado não tem quadro de atraso: é a mesma conta que o
        /// Grid vai fazer, então o limiar acerta sempre.
        /// </summary>
        private double AvailableWidth()
        {
            const double PageMargin = 44.0; // 22 + 22

            double sidebar = _mode switch
            {
                // Barra no topo: ela não ocupa nenhuma coluna do conteúdo.
                LayoutMode.Narrow => 0.0,
                LayoutMode.Compact => SideWidthCompact + 10.0,
                _ => SideWidthWide + SideGap
            };

            return Math.Max(0.0, ActualWidth - PageMargin - sidebar);
        }

        private LayoutMode MeasureMode()
        {
            double w = ActualWidth;
            if (w > 0 && w < NarrowBreakpoint) return LayoutMode.Narrow;
            if (w > 0 && w < CompactBreakpoint) return LayoutMode.Compact;
            return LayoutMode.Wide;
        }

        #region Aplicação do regime

        private void ApplyMode(LayoutMode mode)
        {
            _mode = mode;
            _modeApplied = true;

            // O template do TabControl só existe depois de ApplyTemplate. Sem
            // este guard, o primeiro SizeChanged (que dispara antes da
            // template aplicada) não encontraria as GridDefinitions e a
            // página ficaria no layout errado até o próximo redimensionamento.
            TabHost.ApplyTemplate();

            var sideCol = TabHost.Template.FindName("SideCol", TabHost) as ColumnDefinition;
            var sideGapCol = TabHost.Template.FindName("SideGapCol", TabHost) as ColumnDefinition;
            var mainCol = TabHost.Template.FindName("MainCol", TabHost) as ColumnDefinition;
            var sideRow = TabHost.Template.FindName("SideRow", TabHost) as RowDefinition;
            var sideGapRow = TabHost.Template.FindName("SideGapRow", TabHost) as RowDefinition;
            var mainRow = TabHost.Template.FindName("MainRow", TabHost) as RowDefinition;
            var sideCard = TabHost.Template.FindName("SideCard", TabHost) as Border;
            var sideHead = TabHost.Template.FindName("SideHead", TabHost) as FrameworkElement;
            var sideFoot = TabHost.Template.FindName("SideFoot", TabHost) as FrameworkElement;
            var tabs = TabHost.Template.FindName("Tabs", TabHost) as TabPanel;
            var sideTabsHost = TabHost.Template.FindName("SideTabsHost", TabHost) as ScrollViewer;
            var mainHost = TabHost.Template.FindName("MainHost", TabHost) as ContentPresenter;

            if (sideCol is null || mainCol is null || mainHost is null) return;

            switch (mode)
            {
                case LayoutMode.Narrow:
                    // Faixa no topo: o card ocupa a LINHA 0 inteira e o
                    // conteúdo desce para a linha 2. As colunas 1 e 2 vão a
                    // zero — MainCol INCLUSIVE, senão ela continua * e divide
                    // a largura com a faixa, e a barra de abas fica com
                    // metade da página.
                    if (sideGapCol is not null) sideGapCol.Width = new GridLength(0);
                    if (sideCol is not null) sideCol.Width = new GridLength(1, GridUnitType.Star);
                    if (mainCol is not null) mainCol.Width = new GridLength(0);
                    if (sideRow is not null) sideRow.Height = GridLength.Auto;
                    if (sideGapRow is not null) sideGapRow.Height = new GridLength(9);
                    if (mainRow is not null) mainRow.Height = new GridLength(1, GridUnitType.Star);

                    if (sideCard is not null)
                    {
                        Grid.SetColumn(sideCard, 0);
                        Grid.SetRow(sideCard, 0);
                        sideCard.Margin = new Thickness(0, 0, 0, 9);
                        sideCard.Padding = new Thickness(8, 7, 8, 7);
                        sideCard.CornerRadius = new CornerRadius(11);
                    }
                    if (mainHost is not null)
                    {
                        Grid.SetColumn(mainHost, 0);
                        Grid.SetRow(mainHost, 2);
                    }
                    if (sideHead is not null) sideHead.Visibility = Visibility.Collapsed;
                    if (sideFoot is not null) sideFoot.Visibility = Visibility.Collapsed;
                    TabHost.TabStripPlacement = Dock.Top;
                    if (sideTabsHost is not null)
                    {
                        sideTabsHost.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                        sideTabsHost.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
                    }
                    break;

                case LayoutMode.Compact:
                    if (sideCol is not null) sideCol.Width = new GridLength(SideWidthCompact);
                    if (sideGapCol is not null) sideGapCol.Width = new GridLength(10);
                    if (mainCol is not null) mainCol.Width = new GridLength(1, GridUnitType.Star);
                    if (sideRow is not null) sideRow.Height = new GridLength(1, GridUnitType.Star);
                    // MainRow tem que ser ZERO aqui, e não *. A linha 0 já é
                    // `*` e contém o card lateral E o conteúdo lado a lado; se a
                    // linha 2 também for `*`, o Grid divide a altura em duas
                    // metades e o conteúdo das abas aparece com metade da
                    // altura — que é exatamente o sintoma de "o card está
                    // cortado ao meio" sem nenhuma barra de rolagem envolvida.
                    if (sideGapRow is not null) sideGapRow.Height = new GridLength(0);
                    if (mainRow is not null) mainRow.Height = new GridLength(0);

                    if (sideCard is not null)
                    {
                        Grid.SetColumn(sideCard, 0);
                        Grid.SetRow(sideCard, 0);
                        sideCard.Margin = new Thickness(0, 0, 10, 0);
                        sideCard.Padding = new Thickness(6);
                        sideCard.CornerRadius = new CornerRadius(13);
                    }
                    if (mainHost is not null)
                    {
                        Grid.SetColumn(mainHost, 2);
                        Grid.SetRow(mainHost, 0);
                    }
                    if (sideHead is not null) sideHead.Visibility = Visibility.Collapsed;
                    if (sideFoot is not null) sideFoot.Visibility = Visibility.Collapsed;
                    TabHost.TabStripPlacement = Dock.Left;
                    if (sideTabsHost is not null)
                    {
                        sideTabsHost.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                        sideTabsHost.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                    }
                    break;

                default:
                    if (sideCol is not null) sideCol.Width = new GridLength(SideWidthWide);
                    if (sideGapCol is not null) sideGapCol.Width = new GridLength(SideGap);
                    if (mainCol is not null) mainCol.Width = new GridLength(1, GridUnitType.Star);
                    if (sideRow is not null) sideRow.Height = new GridLength(1, GridUnitType.Star);
                    if (sideGapRow is not null) sideGapRow.Height = new GridLength(0);
                    if (mainRow is not null) mainRow.Height = new GridLength(0);

                    if (sideCard is not null)
                    {
                        Grid.SetColumn(sideCard, 0);
                        Grid.SetRow(sideCard, 0);
                        sideCard.Margin = new Thickness(0, 0, 14, 0);
                        sideCard.Padding = new Thickness(11);
                        sideCard.CornerRadius = new CornerRadius(14);
                    }
                    if (mainHost is not null)
                    {
                        Grid.SetColumn(mainHost, 2);
                        Grid.SetRow(mainHost, 0);
                    }
                    if (sideHead is not null) sideHead.Visibility = Visibility.Visible;
                    if (sideFoot is not null) sideFoot.Visibility = Visibility.Visible;
                    TabHost.TabStripPlacement = Dock.Left;
                    if (sideTabsHost is not null)
                    {
                        sideTabsHost.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                        sideTabsHost.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                    }
                    break;
            }

            ApplyTabLabels(mode);
            ApplyProse(mode);
            ApplyStackedLayout(mode, AvailableWidth());
        }

        /// <summary>
        /// COLAPSO DE PROSA. No regime estreito as três descrições da aba de
        /// visão geral somam ~90px de altura. Como a regra da página é "sem
        /// scroll de aba", 90px é a diferença entre a última card caber e ser
        /// cortada no meio.
        ///
        /// O que some é a PROSA, não a INFORMAÇÃO: título, métricas e botões
        /// continuam todos visíveis. Texto explicativo é o primeiro item a
        /// ceder em layout responsivo — e é o único cujo conteúdo está
        /// repetido em outra aba.
        /// </summary>
        private void ApplyProse(LayoutMode mode)
        {
            var visibility = mode == LayoutMode.Narrow ? Visibility.Collapsed : Visibility.Visible;
            foreach (string name in new[] { "DescHealth", "DescActions", "DescFiles" })
            {
                if (this.FindName(name) is FrameworkElement el)
                    el.Visibility = visibility;
            }
        }

        /// <summary>
        /// Rótulos e chrome de cada aba. O ícone NUNCA some — ele é a
        /// identidade da aba e some o texto. No trilho, o que encolhe é a
        /// palavra, não o glifo.
        /// </summary>
        private void ApplyTabLabels(LayoutMode mode)
        {
            // Na faixa horizontal do topo há largura de sobra, então o rótulo
            // volta. No trilho de 56px ele não cabe e some.
            bool showLabels = mode != LayoutMode.Compact;
            string tag = mode switch
            {
                LayoutMode.Narrow => "narrow",
                LayoutMode.Compact => "compact",
                _ => "wide"
            };

            foreach (TabItem item in TabHost.Items)
                item.Tag = tag;

            // Os rótulos têm x:Name, então vivem no NameScope do próprio
            // UserControl e são resolvidos por FindName daqui — não é preciso
            // descer até o template de cada TabItem.
            foreach (string name in LabelNames)
            {
                if (this.FindName(name) is FrameworkElement label)
                    label.Visibility = showLabels ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private static readonly string[] LabelNames =
        {
            "TabLabel1", "TabLabel2", "TabLabel3",
            "TabLabel4", "TabLabel5"
        };

        /// <summary>
        /// Empilhamento dentro das abas, decidido pela LARGURA REAL disponível
        /// e não pelo regime.
        ///
        /// A diferença importa: no regime estreito a barra de abas sobe para o
        /// topo e devolve a largura INTEIRA ao conteúdo. Empilhar a aba de
        /// arquivos só porque o regime é "estreito" jogava fora ~250px de
        /// largura que estavam livres — e a lista de resultados ficava com
        /// 60px de altura, o que é pior que qualquer estouro horizontal.
        ///
        /// Os limiares são a soma das larguras mínimas dos cards lado a lado,
        /// mais a margem da página (22 de cada lado) e o gap entre eles.
        /// </summary>
        private void ApplyStackedLayout(LayoutMode mode, double available)
        {
            // Margem horizontal da página: 22 + 22.
            const double PageMargin = 44.0;
            double content = Math.Max(0, available - PageMargin);

            // Aba 1 — "Ações principais" | "Recuperação de arquivos".
            // Empilha abaixo de 720px de conteúdo: dois cards, cada um com um
            // botão de ~200px, precisam disso para não espremer o texto.
            Place(OverviewGrid, OverviewActionsCard, OverviewFilesCard, content < 720, 1.0);

            // Aba 2 — motor | resultados.
            //
            // Empilhar aqui é a decisão que parece óbvia e é a ERRADA. O motor
            // tem 3 cartões de modo + cartão de estado; empilhado, ele consome
            // ~410px dos ~500px de altura útil e a lista de arquivos fica com
            // 78px — um título de coluna e nada mais. Lado a lado com o motor
            // COMPACTO (sem as descrições, coluna de 200px), a lista fica com
            // altura inteira e ~174px de largura, que é o que uma tabela
            //Showing deve ter no pior caso.
            //
            // O limiar de empilhamento ficou abaixo da largura prática do app
            // de propósito: com a janela mínima de 640 e a sidebar principal
            // de 240, o conteúdo nunca chega a 340.
            ApplyFilesLayout(content < 340, content);

            // A antiga "Aba 5 — ferramentas | console" foi removida do
            // RecoveryView.xaml, mas esta chamada de layout continuou no
            // code-behind referenciando RepairGridLayout / RepairToolsCard /
            // RepairConsoleCard, que não existem no XAML. Referência morta:
            // quebrava o build (CS0103) sem efeito em runtime.
        }

        /// <summary>
        /// Aba 2. Duas decisões: se empilha (quase nunca) e quão largo fica o
        /// motor.
        ///
        /// A largura do motor é PROPORCIONAL com piso e teto, não fixa. Com
        /// 300px cravados (o valor antigo) a área de resultados recebia ZERO
        /// assim que a janela estreitava; com uma fatia de 46% do conteúdo,
        /// limitada a 200..330, os dois cards continuam existindo em qualquer
        /// largura.
        /// </summary>
        private void ApplyFilesLayout(bool stack, double content)
        {
            if (FilesGridLayout is null) return;

            var col0 = FilesGridLayout.ColumnDefinitions.Count > 0 ? FilesGridLayout.ColumnDefinitions[0] : null;
            var col1 = FilesGridLayout.ColumnDefinitions.Count > 1 ? FilesGridLayout.ColumnDefinitions[1] : null;
            var row0 = FilesGridLayout.RowDefinitions.Count > 0 ? FilesGridLayout.RowDefinitions[0] : null;
            var row1 = FilesGridLayout.RowDefinitions.Count > 1 ? FilesGridLayout.RowDefinitions[1] : null;

            if (stack)
            {
                if (col0 is not null) { col0.MinWidth = 0; col0.MaxWidth = double.PositiveInfinity; col0.Width = new GridLength(1, GridUnitType.Star); }
                if (col1 is not null) { col1.MinWidth = 0; col1.Width = new GridLength(0); }
                if (row0 is not null) row0.Height = GridLength.Auto;
                if (row1 is not null) row1.Height = new GridLength(1, GridUnitType.Star);

                Grid.SetColumn(ScanEngineCard, 0);
                Grid.SetRow(ScanEngineCard, 0);
                Grid.SetColumn(FilesResultsCard, 0);
                Grid.SetRow(FilesResultsCard, 1);

                ScanEngineCard.Margin = new Thickness(0, 0, 0, 12);
                FilesResultsCard.Margin = new Thickness(0, 0, 0, 0);
            }
            else
            {
                // 46% do conteúdo, com piso de 200 (abaixo disso o texto do
                // modo de varredura não cabe) e teto de 330 (acima disso o
                // motor fica largo demais para o que ele mostra).
                double engine = Clamp(content * 0.46, 200, 330);

                if (col0 is not null) { col0.MinWidth = 200; col0.MaxWidth = 330; col0.Width = new GridLength(engine); }
                if (col1 is not null) { col1.MinWidth = 160; col1.Width = new GridLength(1, GridUnitType.Star); }
                if (row0 is not null) row0.Height = new GridLength(1, GridUnitType.Star);
                if (row1 is not null) row1.Height = GridLength.Auto;

                Grid.SetColumn(ScanEngineCard, 0);
                Grid.SetRow(ScanEngineCard, 0);
                Grid.SetColumn(FilesResultsCard, 1);
                Grid.SetRow(FilesResultsCard, 0);

                ScanEngineCard.Margin = new Thickness(0, 0, 12, 0);
                FilesResultsCard.Margin = new Thickness(0, 0, 0, 0);
            }

            // COMPACIDADE DO MOTOR por largura, e não por regime. Os cartões de
            // modo perdem a descrição e viram "ícone + título": em 200px de
            // coluna a descrição quebraria em 5 linhas, e são ~150px de altura
            // que a lista de arquivos precisa.
            bool terse = content < 620;
            var vis = terse ? Visibility.Collapsed : Visibility.Visible;
            foreach (string name in new[] { "ScanDescQuick", "ScanDescDeep", "ScanDescVss" })
            {
                if (this.FindName(name) is FrameworkElement el) el.Visibility = vis;
            }
            if (this.FindName("ScanStatusText") is FrameworkElement status)
                status.MaxHeight = terse ? 30 : 46;
        }

        private static double Clamp(double v, double min, double max) =>
            v < min ? min : (v > max ? max : v);

        /// <summary>
        /// Coloca dois cards lado a lado ou empilhados na grade `host`.
        /// A coluna de gap é zerada no modo empilhado — senão sobra uma faixa
        /// vazia de 12px no meio da página, que é justamente o tipo de buraco
        /// que faz um layout parecer quebrado.
        /// </summary>
        private static void Place(Grid? host, FrameworkElement? first, FrameworkElement? second,
                                  bool stack, double secondRowWeight)
        {
            if (host is null || first is null || second is null) return;

            if (host.ColumnDefinitions.Count >= 2)
            {
                host.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
                host.ColumnDefinitions[1].Width = stack
                    ? new GridLength(0)
                    : new GridLength(1, GridUnitType.Star);
            }
            if (host.RowDefinitions.Count >= 2)
            {
                host.RowDefinitions[0].Height = stack ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
                host.RowDefinitions[1].Height = stack
                    ? new GridLength(secondRowWeight, GridUnitType.Star)
                    : GridLength.Auto;
            }

            Grid.SetRow(first, 0);
            Grid.SetRow(second, stack ? 1 : 0);
            Grid.SetColumn(first, 0);
            Grid.SetColumn(second, stack ? 0 : 1);

            first.Margin = stack ? new Thickness(0, 0, 0, 12) : new Thickness(0, 0, 6, 0);
            second.Margin = stack ? new Thickness(0) : new Thickness(6, 0, 0, 0);
        }

        #endregion

        /// <summary>
        /// Abre o guia de recuperação. Mantido acessível pelo atalho/fluxo existente.
        /// </summary>
        public void ShowRecoveryGuide()
        {
            try
            {
                var owner = Window.GetWindow(this) ?? Application.Current?.MainWindow;
                var page = new RecoveryInfoPage();
                if (owner != null) page.Owner = owner;
                page.ShowDialog();
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError(
                    $"[RecoveryView] Falha ao abrir o guia de recuperação. {ex.Describe()}", ex);
            }
        }
    }
}
