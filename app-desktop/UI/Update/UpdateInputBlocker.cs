using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.UpdateFlow;

namespace VoltrisOptimizer.UI.Update
{
    /// <summary>
    /// [UPDATE-BLOQUEIO] A TRAVA DE INTERFACE DURANTE A ATUALIZAÇÃO.
    ///
    /// O QUE ELE FAZ
    /// =============
    /// Enquanto o pacote baixa, cobre a janela inteira com uma camada que
    /// captura mouse e teclado. Clicar em qualquer lugar — sidebar, header,
    /// botão do Dashboard — mostra o aviso de que o programa está atualizando.
    ///
    /// POR QUE UM COMPONENTE E NÃO UM ESTADO EM CADA TELA
    /// ===================================================
    /// Porque a trava precisa ser ÚNICA. Se cada tela tivesse a sua, bastaria a
    /// sidebar destravar por um erro de um caractere para o usuário descobrir
    /// que pode trocar de perfil no meio de uma atualização — e aí o plano de
    /// energia muda com o instalador a caminho.
    ///
    /// Por isso a camada é montada em CIMA do contêiner raiz, e não dentro de
    /// nenhuma tela. Ela não conhece sidebar, header nem Dashboard: ela cobre
    /// a janela, e pronto. Uma tela nova não pode "esquecer" de participates,
    /// porque ela nem sabe que a trava existe.
    ///
    /// POR QUE A CAMADA É TRANSPARENTE
    /// ==============================
    /// A interface continua visível por trás. Isso é deliberado: uma tela
    /// escurecida e sem informação deixa o usuário sem saber o que está
    /// acontecendo, e a primeira reação é achar que o programa travou — quando
    /// o que está acontecendo é que ele está trabalhando.
    ///
    /// UM PONTO QUE PARECE CONTRADITÓRIO
    /// ===============================
    /// A camada tem <c>Background = Transparent</c> e não "nulo". Em WPF, um
    /// elemento sem fundo e sem opacidade não recebe cliques: o clique atravessa
    /// para o que está debaixo. A trava seria decorativa, e o usuário clicaria
    /// nos botões exatamente como antes.
    /// </summary>
    public static class UpdateInputBlocker
    {
        private static Grid? _layer;
        private static Border? _card;
        private static TextBlock? _message;
        private static Panel? _host;

        /// <summary>
        /// [FIX:TOOLTIP-5S] Temporizador que tira o aviso sozinho.
        ///
        /// Precisa ser criado no <c>Dispatcher</c> do elemento visual, e não
        /// num `Task.Delay`: o aviso é um objeto WPF, e mexer nele de fora da
        /// thread da UI é acesso proibido que derruba o processo.
        /// </summary>
        private static System.Windows.Threading.DispatcherTimer? _hideTimer;

        /// <summary>
        /// Instala a trava e passa a acompanhar o estado do fluxo.
        ///
        /// Seguro para chamar mais de uma vez: se já houver uma camada, apenas
        /// reaproveita. Isso importa porque a janela é recriada em alguns
        /// fluxos, e uma segunda camada por cima da primeira deixaria a
        /// interface mais escura a cada recriação.
        /// </summary>
        public static void Attach(Panel host, ILoggingService? logger)
        {
            if (host == null)
            {
                // Registrado em arquivo, e não só no log: este é o ponto onde
                // a trava nasce, e um ponto que não pode ser verificado é um
                // ponto em que se vai caçar defeito na tela em vez de olhar o
                // log certo.
                Mark("Attach recebido com host NULO");
                return;
            }

            if (_layer != null && _host == host)
            {
                return;
            }

            Detach();
            _host = host;

            _layer = BuildLayer(logger);
            host.Children.Add(_layer);

            // [FIX:DIAGNOSTICO-DE-CLIQUE] ONDE O CLIQUE REALMENTE CAI?
            // ==========================================================
            // O log mostrava a camada `Visible` e nenhuma linha `clique`. Entre
            // "o clique não chega ao handler" e "o clique nunca chegou na
            // janela" há uma diferença enorme, e sem saber qual das duas é,
            // qualquer correção é chute.
            //
            // Este handler roda com `handledEventsToo: true`, que é o detalhe
            // que o torna útil: ele é chamado MESMO que algo acima tenha
            // marcado o evento como tratado — exatamente o caso em que a
            // camada perde o clique. E responde às três perguntas que
            // separaram "handler quebrado" de "camada invisível ao mouse":
            // a posição, o tamanho real da camada, e QUEM está no topo.
            host.AddHandler(
                UIElement.PreviewMouseDownEvent,
                new MouseButtonEventHandler(DiagnoseClick),
                handledEventsToo: true);

            UpdateFlowState.Changed += OnStateChanged;
            Apply(UpdateFlowState.BlocksInput);

            // [FIX:ONDE-A-TRAVA-NASCE] O ESTADO DA TRAVA NO STARTUP.
            // ===================================================
            // Este é o primeiro ponto da vida do programa em que o estado do
            // fluxo de atualização pode ser observado, e ele estava sem
            // registro nenhum.
            //
            // A consequência foi um log que se contradizia: a reserva seguinte
            // aparecia com `flag_antes=1`, sem que nenhuma reserva anterior
            // tivesse sido registrada. Faltava justamente o número que
            // permitiria dizer "a trava já estava ocupada quando o programa
            // começou", em vez de "algo a ocupou durante os primeiros
            // segundos", que é o que eu supus durante três rodadas.
            Mark($"STARTUP | flag={UpdateFlowController.IsRunning} " +
                 $"| estagio={UpdateFlowState.Stage} " +
                 $"| simulado={UpdateFlowState.SimulationEnabled}");

            Mark($"trava INSTALADA no host '{host.GetType().Name}' | visivel={_layer.Visibility} | bloqueando={UpdateFlowState.BlocksInput}");
        }

        /// <summary>
        /// Carimbo direto em arquivo, sem passar pelo serviço de log.
        ///
        /// O mesmo motivo do `ProfilePowerCoordinator`: durante o startup o
        /// `App.LoggingService` pode ainda não existir, e um componente que
        /// não deixou rastro é indistinguível de um componente que não rodou.
        /// </summary>
        private static void Mark(string message)
        {
            // [FIX:DIAGNOSTICO-ENGOLINDO-LINHAS]
            // ==========================================
            // Este `catch { }` vazio permitia que uma LINHA INTEIRA de
            // diagnóstico sumisse sem aviso, e foi assim que um
            // "pedido ignorado" apareceu no log sem que houvesse reserva
            // vencedora nenhuma antes dele — combinação impossível pelo
            // código, que só se explica por uma linha perdida na corrida
            // entre a thread de download e a thread da interface.
            //
            // `AppendAllText` abre o arquivo com compartilhamento exclusivo:
            // perdedor da corrida lançava exceção e a evidência ia embora.
            // Aqui o que se perde não é o programa, é justamente a prova —
            // que é o que transformou uma leitura de log em adivinhação.
            lock (LogSync)
            {
                try
                {
                    string dir = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Voltris", "Logs");

                    System.IO.Directory.CreateDirectory(dir);
                    System.IO.File.AppendAllText(
                        System.IO.Path.Combine(dir, "update_blocker.log"),
                        $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        "[UpdateBlocker] falha ao gravar log: " + ex.Message);
                }
            }
        }

        private static readonly object LogSync = new object();

        /// <summary>Remove a trava e o registro. Usado ao fechar a janela.</summary>
        public static void Detach()
        {
            UpdateFlowState.Changed -= OnStateChanged;

            if (_host != null)
            {
                // O handler de diagnóstico também é um handler, e um handler
                // registrado duas vezes em `Detach`/`Attach` duplica as linhas
                // do log — que é a mesma armadilha de "a camada escurece a
                // cada recriação" descrita no `Attach`.
                _host.RemoveHandler(
                    UIElement.PreviewMouseDownEvent,
                    new MouseButtonEventHandler(DiagnoseClick));
            }

            if (_layer != null && _host != null)
            {
                _host.Children.Remove(_layer);
            }

            _layer = null;
            _card = null;
            _message = null;
            _host = null;
            _hideTimer?.Stop();
            _hideTimer = null;
        }

        private static Grid BuildLayer(ILoggingService? logger)
        {
            var grid = new Grid
            {
                // O ponto que faz a trava existir. Ver a nota na documentação.
                Background = Brushes.Transparent,

                Visibility = Visibility.Collapsed,
                IsHitTestVisible = true,
                Focusable = true
            };

            // `ZIndex` é propriedade ACOPLADA de `Panel`: define-se pela
            // estática `Panel.SetZIndex`, e não como campo do inicializador.
            // Sem esta linha a camada entraria no ZIndex 0, ao lado do
            // conteúdo, e a trava dependeria da ordem de inserção — que é
            // exatamente o tipo de coisa que funciona na sua máquina e falha
            // depois de uma refatoração.
            Panel.SetZIndex(grid, 9999);

            // Escurece levemente, sem esconder. Ver a nota na documentação.
            grid.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(64, 0, 0, 0)),
                IsHitTestVisible = false
            });

            // O cartão central com a mensagem. Começa escondido e aparece no
            // primeiro clique: mostrar desde o início seria mais um texto
            // competindo com o botão circular, que é onde o usuário precisa
            // estar olhando.
            _message = new TextBlock
            {
                Text = LocalizationService.Instance.GetString("UpdateBusyWait"),
                Foreground = Brushes.White,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(28, 20, 28, 20),
                IsHitTestVisible = false,
                Opacity = 0
            };

            _card = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(238, 18, 18, 24)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(120, 120, 130, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(8, 0, 8, 0),
                MaxWidth = 460,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Hidden,
                IsHitTestVisible = false,
                Child = _message
            };

            grid.Children.Add(_card);

            // O clique é capturado aqui e NÃO é repassado. É o que impede que
            // o botão atrás dele seja acionado.
            grid.PreviewMouseDown += OnBlockedInput;
            grid.PreviewKeyDown += OnBlockedKey;

            (logger ?? App.LoggingService)?.LogInfo("[UpdateBlocker] Trava de interface instalada.");
            Mark($"camada criada | filhos={grid.Children.Count} | z={Panel.GetZIndex(grid)}");

            return grid;
        }

        /// <summary>
        /// [FIX:TOOLTIP-SO-NO-CLIQUE] QUANDO O AVISO APARECE.
        /// =============================================
        /// A primeira versão mostrava o aviso ao MOVER o mouse sobre a tela
        /// travada. O usuário pediu que apareça só quando ele tenta clicar.
        ///
        /// A diferença é de fundamentals. Passar o mouse acontece sem querer —
        /// o cursor está no meio da janela, ele se mexe, e de repente aparece
        /// um cartão no meio da tela. Isso faz o usuário achar que o programa
        /// travou de vez, quando o que ele fez foi mover o cursor. Aviso que
        /// aparece sem ser pedido é lido como erro, não como informação.
        ///
        /// Só no clique, o aviso significa exatamente o que deve significar:
        /// "você tentou usar o programa e ele está ocupado".
        /// </summary>
        private static void OnBlockedInput(object sender, MouseButtonEventArgs e)
        {
            // O clique NUNCA é repassado. É isto que mantém a trava.
            e.Handled = true;

            // [FIX:TOOLTIP-AO-VIVO] O clique precisa ser VISÍVEL.
            //
            // Sem este registro, um clique que não abre o aviso e um clique que
            // abre e fecha são indistinguíveis de fora — e a diferença entre
            // eles está toda dentro destes dois `if`.
            Mark($"clique | visivel_antes={_card?.Visibility.ToString() ?? "(sem card)"}");

            // Um clique com o aviso já na tela o dispensa. Sem esta inversão,
            // o aviso nunca sumiria: ele reapareceria no clique seguinte, e o
            // usuário ficaria sem nenhuma forma de tirá-lo da frente.
            if (_card?.Visibility == Visibility.Visible)
            {
                HideMessage("clique com o aviso visivel");
                return;
            }

            ShowMessage();
        }

        private static void OnBlockedKey(object sender, KeyEventArgs e)
        {
            e.Handled = true;

            Mark($"tecla {e.Key} | visivel_antes={_card?.Visibility.ToString() ?? "(sem card)"}");

            if (_card?.Visibility == Visibility.Visible)
            {
                HideMessage("tecla com o aviso visivel");
                return;
            }

            ShowMessage();
        }

        /// <summary>
        /// [FIX:DIAGNOSTICO-DE-CLIQUE] REGISTRA ONDE O CLIQUE CAI.
        ///
        /// Responde, num único registro, o trio de fatos que decide entre as
        /// hipóteses possíveis:
        ///
        /// - a camada está `Visible`? Se não, o clique foi para o vazio e
        ///   nenhum handler seria chamado — e a culpa não é do handler;
        /// - a camada tem tamanho? Um `Grid` sem tamanho não recebe mouse
        ///   mesmo com `Background`, e é o defeito mais silencioso que existe
        ///   em WPF;
        /// - quem está no topo no ponto clicado? Se for algo que não seja a
        ///   camada, existe um elemento acima dela roubando o clique, e a
        ///   correção é de ZOrder e não do evento.
        /// </summary>
        private static void DiagnoseClick(object sender, MouseButtonEventArgs e)
        {
            try
            {
                Point p = e.GetPosition(null);
                string noTopo = "(host nulo)";

                if (_host != null)
                {
                    HitTestResult? hit = VisualTreeHelper.HitTest(_host, p);

                    if (hit?.VisualHit != null)
                    {
                        noTopo = hit.VisualHit is FrameworkElement fe && !string.IsNullOrEmpty(fe.Name)
                            ? $"{fe.Name} <{fe.GetType().Name}>"
                            : $"<{hit.VisualHit.GetType().Name}>";
                    }
                    else
                    {
                        noTopo = "(nada)";
                    }
                }

// O tamanho vai para variáveis antes de entrar na interpolação.
                // `{_layer?.ActualWidth:F0}` não compila: o especificador de
                // formato não pode ser combinado com o acesso condicional, e a
                // rejeição do compilador é justa — ela está dizendo que a
                // expressão misturou duas sintaxes que não convivem.
                double largura = _layer?.ActualWidth ?? 0;
                double altura = _layer?.ActualHeight ?? 0;

                Mark($"DIAG clique | pos={p.X:F0},{p.Y:F0} " +
                     $"| camada={_layer?.Visibility} " +
                     $"| tamanho={largura:F0}x{altura:F0} " +
                     $"| bloqueando={UpdateFlowState.BlocksInput} " +
                     $"| noTopo={noTopo}");
            }
            catch (Exception ex)
            {
                Mark("DIAG falhou: " + ex.Message);
            }
        }

        /// <summary>Some com o aviso e desliga o temporizador de 5 segundos.</summary>
        private static void HideMessage(string motivo = "(sem motivo)")
        {
            if (_card != null) _card.Visibility = Visibility.Hidden;
            if (_message != null) _message.Opacity = 0;

            _hideTimer?.Stop();
            Mark($"escondido: {motivo}");
        }

        private static void ShowMessage()
        {
            if (_card == null || _message == null)
            {
                Mark("NAO PODE MOSTRAR: card ou message nulo");
                return;
            }

            string text = LocalizationService.Instance.GetString("UpdateBusyWait");
            string title = LocalizationService.Instance.GetString("UpdateClickBlocked");

            _message.Text = text + "\n" + title;

            // [FIX:ORDEM-DE-RENDER] A OPACIDADE ANTES DA VISIBILIDADE.
            //
            // A ordem original era texto -> `Opacity = 1` -> `Visible`. Com o
            // cartão em `Hidden`, mudar `Opacity` de um filho que ainda não
            // participates do layout não provoca o repaint do texto: o WPF
            // pula o repaint de um elemento que não estava visível. O cartão
            // aparecia com o texto invisível — e o usuário via um cartão vazio,
            // que é pior do que não ver nada, porque parece um defeito de
            // layout.
            //
            // Tornar o cartão visível ANTES de mexer na opacidade garante que o
            // texto entre participates do layout e seja pintado.
            _card.Visibility = Visibility.Visible;
            _message.Opacity = 1;

            _message.UpdateLayout();
            Mark($"mostrado: texto='{_message.Text}' | card={_card.Visibility} | opacidade={_message.Opacity} | largura_real={_card.ActualWidth}");

            // [FIX:TOOLTIP-5S] SOME SOZINHO.
            //
            // Sem o temporizador, o aviso fica na tela até a atualização
            // terminar — que pode ser minutos. Um aviso que não sai é
            // decoração, e decoração no meio da tela atrapalha: o usuário
            // precisa enxergar o botão circular, que é onde o progresso está.
            //
            // O timer é REINICIADO a cada novo clique, para que quem está
            // tentando usar o programinha tenha o aviso sempre à mão em vez de
            // ver ele sumir bem no momento em que ia ler.
            StartHideTimer();
        }

        private static void StartHideTimer()
        {
            // [FIX:TIMER-SEM-DISPATCHER] `_layer` PODE SER NULO AQUI.
            //
            // A primeira versão retornava cedo quando `_layer` era nulo, o que
            // significava que o temporizador NUNCA era criado — e o aviso
            // ficava na tela até a atualização terminar, que pode ser minutos.
            // O retorno era uma tentativa de segurança que na prática
            // desligava a função.
            //
            // O temporizador é criado no Dispatcher do APPLICATION, e não do
            // elemento: o elemento pode não existir ainda, e o Dispatcher da
            // aplicação existe desde o primeiro frame. O `HideMessage` só
            // mexe em propriedades visuais, e ele roda sempre na thread da UI
            // porque é o Dispatcher que dispara.
            var dispatcher = _layer?.Dispatcher
                             ?? System.Windows.Application.Current?.Dispatcher;

            if (dispatcher == null)
            {
                Mark("SEM DISPATCHER: o aviso pode nao sumir sozinho");
                return;
            }

            if (_hideTimer == null)
            {
                _hideTimer = new System.Windows.Threading.DispatcherTimer(
                    System.Windows.Threading.DispatcherPriority.Normal,
                    dispatcher);

                // [FIX:INTERVALO-NUNCA-DEFINIDO] O TIMER DISPARAVA A CADA 100 MS.
                //
                // Este é o defeito, e ele é inteiramente meu: eu criei o
                // temporizador, escrevi o "Some sozinho em 5 segundos" no
                // comentário, e nunca defini `Interval`.
                //
                // O padrão do `DispatcherTimer` é 100 ms. O que o log do
                // usuário mostrou, milissegundo a milissegundo:
                //
                //     19:10:10.273  clique    | visivel_antes=Hidden
                //     19:10:10.273  mostrado  | card=Visible | opacidade=1
                //     19:10:10.274  escondido: 5 segundos
                //
                // Um milissegundo. O aviso aparecia e sumia antes de o olho
                // humano registrar que ele existiu — e a conclusão correta
                // ("não aparece") era a mesma de um cartão invisível.
                //
                // A lição é a mesma das outras: um temporizador sem
                // intervalo declarado é um temporizador com um valor que
                // NINGUÉM escolheu. `Interval` não tem cara de opção, e é
                // exatamente por isso que passa.
                _hideTimer.Interval = TimeSpan.FromSeconds(5);

                _hideTimer.Tick += (s, e) =>
                {
                    HideMessage("5 segundos");
                    _hideTimer?.Stop();
                };

                Mark("temporizador criado com intervalo de 5s");
            }

            _hideTimer.Stop();
            _hideTimer.Start();
        }

        private static void OnStateChanged(object? sender, EventArgs e)
        {
            if (_layer == null) return;

            // O estado muda em thread de fundo (o download roda fora da UI), e
            // a camada é um objeto visual: tocar nele de fora da thread da UI
            // é acesso a propriedade visual fora do contexto, que o WPF não
            // permite e que pode derrubar o processo.
            var dispatcher = _layer.Dispatcher;

            if (dispatcher.CheckAccess())
            {
                Apply(UpdateFlowState.BlocksInput);
            }
            else
            {
                dispatcher.BeginInvoke(new Action(() => Apply(UpdateFlowState.BlocksInput)));
            }
        }

        private static void Apply(bool block)
        {
            if (_layer == null) return;

            _layer.Visibility = block ? Visibility.Visible : Visibility.Collapsed;

            Mark($"aplicado: bloqueando={block} | visibilidade={_layer.Visibility}");

            if (!block)
            {
                // Some junto com a trava. Deixar o aviso na tela depois que o
                // programa já pode ser usado seria informação velha.
                if (_card != null) _card.Visibility = Visibility.Hidden;
                if (_message != null) _message.Opacity = 0;
            }
        }
    }
}
