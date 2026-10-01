using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VoltrisOptimizer.UI.ViewModels;
using VoltrisOptimizer.UI.Controls;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Cloud;
using VoltrisOptimizer.Services.License;

namespace VoltrisOptimizer.UI.Views
{
    /// <summary>
    /// Interaction logic for DashboardView.xaml
    /// As animações de partículas são controladas diretamente pelo XAML via DataTriggers
    /// vinculados à propriedade ShouldAnimationsRun do DashboardViewModel.
    /// Quando ShouldAnimationsRun = False, os Grids/Canvas de partículas são colapsados,
    /// pausando as animações de forma nativa sem custo de CPU/GPU.
    /// </summary>
    public partial class DashboardView : UserControl
    {
        /// <summary>
        /// [FIX:RGB-LOG] Estado dos dois anéis e o motivo de cada decisão.
        ///
        /// Reimplementa a condição do MultiDataTrigger do XAML em código, e é
        /// justamente isso que a torna útil: se o log discordar do que se vê na
        /// tela, a divergência está na sincronia do binding, não no XAML.
        /// </summary>
        private void ProbeRings()
        {
            try
            {
                var vm = DataContext as DashboardViewModel;
                if (vm == null) return;

                bool gamer = vm.IsGamerModeActive;
                bool anims = vm.ShouldAnimationsRun;
                bool ringVisivel = vm.MasterRingVisible;
                bool sucesso = vm.MasterRingSuccess;
                bool rgbDeveRodar = gamer && anims;

                var rgb = this.GamerRgbRing;
                var anelBase = this.GamerRing;

                // Só loga quando o ESTADO EFETIVO muda, para não gerar uma
                // linha por quadro durante as animações.
                string state = $"rgbRodar={rgbDeveRodar} gamer={gamer} anims={anims} "
                             + $"| ringVisible={ringVisivel} success={sucesso}";

                if (state == _lastRingState) return;
                _lastRingState = state;

                App.LoggingService?.LogInfo(
                    $"[RING] {state} || anelBase: op={anelBase?.Opacity:F2} "
                    + $"| anelRgb: op={rgb?.Opacity:F2} vis={rgb?.Visibility} "
                    + $"| deveAnimar={rgbDeveRodar} "
                    + $"| motivo={(rgbDeveRodar ? "RGB LIGADO (gamer+foreground)" : !gamer ? "RGB DESLIGADO (sem gamer)" : "RGB DESLIGADO (pausado: fora do foreground ou view oculta)")}");
            }
            catch (Exception ex)
            {
                DiagLog($"[RING] erro: {ex.Message}");
            }
        }

        private string _lastRingState = string.Empty;

        /// <summary>
        /// [FIX:FACE-LOG] Sondagem do rosto. Só fica ligada enquanto a operação do
        /// botão circular está correndo.
        /// </summary>
        private System.ComponentModel.PropertyChangedEventHandler? _faceProbeHandler;
        private double _lastProbePct = -1;
        private string _lastProbeState = string.Empty;

        private void DashboardView_DataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
        {
            if (_faceProbeHandler != null)
            {
                DetachFaceProbe();
            }

            if (e.NewValue is System.ComponentModel.INotifyPropertyChanged vm)
            {
                _faceProbeHandler = (s, p) =>
                {
                    switch (p.PropertyName)
                    {
                        case "IsMasterRunning":
                        case "EffectiveFaceMood":
                        case "MasterFaceEffortBoost":
                        case "MasterProgress":
                            Dispatcher.BeginInvoke(new Action(ProbeFace), System.Windows.Threading.DispatcherPriority.Background);
                            break;

                        // [FIX:RGB-LOG] Motivos exatos de o anel RGB ligar e
                        // desligar. A animação é controlada por um
                        // MultiDataTrigger no XAML, invisível para o ViewModel —
                        // sem este log, "o RGB não acendeu" e "acendeu e não
                        // desligou" seriam indistinguíveis na leitura.
                        case "IsGamerModeActive":
                        case "ShouldAnimationsRun":
                        case "MasterRingVisible":
                        case "MasterRingSuccess":
                            Dispatcher.BeginInvoke(new Action(ProbeRings),
                                System.Windows.Threading.DispatcherPriority.Background);
                            break;
                    }
                };
                vm.PropertyChanged += _faceProbeHandler;
            }
        }

        private void DetachFaceProbe()
        {
            if (_faceProbeHandler == null) return;

            if (DataContext is System.ComponentModel.INotifyPropertyChanged vm)
            {
                vm.PropertyChanged -= _faceProbeHandler;
            }
            _faceProbeHandler = null;
        }

        /// <summary>
        /// [FIX:FACE-LOG] Registra o estado real do rosto e da % do círculo.
        /// O log só é escrito quando algo MUDA, para não inundar o arquivo.
        /// </summary>
        private void ProbeFace()
        {
            try
            {
                var vm = DataContext as DashboardViewModel;
                if (vm == null) return;

                bool running = vm.IsMasterRunning;

                // [FIX:FACE-LOG] Os dois rostos vivem dentro do ControlTemplate do
                // botão, ou seja, no NAMESPACE DO TEMPLATE — não no da View. Por
                // isso não dá para escrever `this.RunningFaceHost`: o code-behind
                // da View não enxerga elementos do template. A busca pela árvore
                // visual é o jeito que funciona de fato, e devolve os dois rostos
                // de qualquer jeito que a ordem do XAML venha a mudar.
                var faces = new System.Collections.Generic.List<VoltrisOptimizer.UI.Controls.AiFaceView>();
                CollectFaces(this, faces);

                string facesDesc;
                if (faces.Count == 0)
                {
                    facesDesc = "nenhum AiFaceView encontrado na arvore";
                }
                else
                {
                    var parts = new System.Collections.Generic.List<string>();
                    foreach (var f in faces)
                    {
                        parts.Add(
                            $"vis={f.Visibility}|mood={f.Mood}|eff={f.EffortBoost:F2}" +
                            $"|{f.ActualWidth:F0}x{f.ActualHeight:F0}");
                    }
                    facesDesc = string.Join("  ||  ", parts);
                }

                string state = $"faces[{faces.Count}]: {facesDesc}|arc%={vm.MasterProgress}|moodVM={vm.EffectiveFaceMood}";

                bool changed = state != _lastProbeState
                               || Math.Abs(vm.MasterProgress - _lastProbePct) >= 1.0;

                if (!changed) return;

                _lastProbeState = state;
                _lastProbePct = vm.MasterProgress;

                App.LoggingService?.LogInfo(
                    $"[FACE-PROBE] MasterProgress={vm.MasterProgress} IsMasterRunning={running} {state}");
            }
            catch (Exception ex)
            {
                DiagLog($"[FACE-PROBE] erro: {ex.Message}");
            }
        }

        /// <summary>
        /// [FIX:FACE-LOG] Percorre a árvore visual coletando todos os
        /// <see cref="VoltrisOptimizer.UI.Controls.AiFaceView"/>.
        /// </summary>
        private static void CollectFaces(System.Windows.DependencyObject node, System.Collections.Generic.List<VoltrisOptimizer.UI.Controls.AiFaceView> into)
        {
            int guard = 0;
            var current = node;
            while (current != null && guard++ < 20000)
            {
                if (current is VoltrisOptimizer.UI.Controls.AiFaceView f)
                {
                    into.Add(f);
                }

                int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(current);
                for (int i = 0; i < count; i++)
                {
                    CollectFaces(System.Windows.Media.VisualTreeHelper.GetChild(current, i), into);
                }
                break;
            }
        }

        public DashboardView()
        {
            // [LOG] DashboardView constructor entry
            var tid = System.Threading.Thread.CurrentThread.ManagedThreadId;
            DiagLog($"[DASHBOARD_VIEW][TID:{tid}] ==================== Construtor INÍCIO ====================");
            DiagLog($"[DASHBOARD_VIEW][TID:{tid}] Timestamp: {DateTime.Now:HH:mm:ss.ffffff}");
            try { VoltrisDiagnosticSystem.Instance.Timeline("DASHBOARD", "DashboardView: Construtor INÍCIO"); } catch { }

            // Log direto em arquivo (síncrono) para diagnosticar travamentos no InitializeComponent.
            // Não depende do LoggingService que pode estar em buffer/async.
            DiagLog($"[DASHBOARD_VIEW][TID:{tid}] ANTES de InitializeComponent");
            try { VoltrisDiagnosticSystem.Instance.Timeline("DASHBOARD", "DashboardView: ANTES de InitializeComponent"); } catch { }
            InitializeComponent();
            try { VoltrisDiagnosticSystem.Instance.Timeline("DASHBOARD", "DashboardView: DEPOIS de InitializeComponent"); } catch { }
            DiagLog($"[DASHBOARD_VIEW][TID:{tid}] DEPOIS de InitializeComponent");

            // [FIX:FACE-LOG] Sonda do estado REAL do rosto durante a operação.
            //
            // O ViewModel só sabe o que ELE acredita ter publicado. Se o binding
            // não chegar, se o controle estiver oculto ou se o tamanho estiver
            // errado, o único lugar que sabe é a View — que enxerga
            // Visibility, ActualWidth e o Mood efetivamente aplicado no
            // AiFaceView. Esta sonda é o que fecha o diagnóstico do
            // "rostinho cortado / não fica furioso".
            //
            // Fica ligada durante a execução e é desliga no fim, para não
            // poluir o log com 10 linhas por segundo fora dela.
            this.DataContextChanged += DashboardView_DataContextChanged;
            DiagLog($"[DASHBOARD_VIEW][TID:{tid}] DataContext no construtor: {DataContext?.GetType().Name ?? "NULL"}");

            App.LoggingService?.LogInfo($"[DASHBOARD_VIEW][TID:{tid}] ===== CONSTRUTOR DO DashboardView CHAMADO =====");
            App.LoggingService?.LogInfo($"[DASHBOARD_VIEW][TID:{tid}] DataContext atual: {DataContext?.GetType().Name ?? "NULL"}");

            // OTIMIZAÇÃO CRÍTICA: Não resolver o ViewModel no construtor para evitar lock de DI na UI thread.
            // O DataContext será definido no evento Loaded de forma assíncrona.

            // Log quando o controle for renderizado pela primeira vez
            this.IsVisibleChanged += (s, e) =>
            {
                NotifyViewVisibility();
                if (this.IsVisible)
                {
                    var vtid = System.Threading.Thread.CurrentThread.ManagedThreadId;
                    DiagLog($"[DASHBOARD_VIEW][TID:{vtid}] ⚡ IsVisibleChanged = TRUE (View ficou VISÍVEL)");
                    DiagLog($"[DASHBOARD_VIEW][TID:{vtid}] DataContext quando visível: {DataContext?.GetType().Name ?? "NULL"}");
                    if (DataContext != null)
                    {
                        try
                        {
                            var vm = DataContext as DashboardViewModel;
                            if (vm != null)
                            {
                                DiagLog($"[DASHBOARD_VIEW][TID:{vtid}] 🔍 ViewModel Stats no momento da visibilidade:");
                                DiagLog($"[DASHBOARD_VIEW][TID:{vtid}]    - HealthStatusText: '{vm.HealthStatusText}'");
                                DiagLog($"[DASHBOARD_VIEW][TID:{vtid}]    - ScoreLabel: '{vm.ScoreLabel}'");
                                DiagLog($"[DASHBOARD_VIEW][TID:{vtid}]    - CpuUsage: {vm.CpuUsage}%");
                                DiagLog($"[DASHBOARD_VIEW][TID:{vtid}]    - RamUsage: {vm.RamUsage}%");
                            }
                        }
                        catch (Exception ex)
                        {
                            DiagLog($"[DASHBOARD_VIEW][TID:{vtid}] Erro ao inspecionar ViewModel: {ex.Message}");
                        }
                    }
                }
            };

            // Track page view
            Loaded += (s, e) =>
            {
                var ltid = System.Threading.Thread.CurrentThread.ManagedThreadId;
                DiagLog($"[DASHBOARD_VIEW][TID:{ltid}] Evento Loaded INÍCIO");
                try { VoltrisDiagnosticSystem.Instance.Timeline("DASHBOARD", "DashboardView: Evento Loaded INÍCIO"); } catch { }
                try { VoltrisOptimizer.Helpers.StartupFreezeForensics.Instance?.MarkDashboardLoaded(); } catch { }

                // Chip de conta: assina o CloudAccountService (fonte de verdade
                // do estado de vínculo) e aplica o estado atual. Vai no início
                // para o chip já nascer correto, sem "piscar" o CTA de login.
                try
                {
                    SubscribeAccountEvents();
                    UpdateAccountChip(
                        CloudAccountService.Instance.IsLinked,
                        CloudAccountService.Instance.LinkedEmail);

                    // Faixa de licença: mesmo padrão, com LicenseTokenStore.
                    SubscribeLicenseEvents();
                    UpdateLicenseStrip();

                    // O nome amigável do Windows vem de WMI (lento). Repinta o
                    // chip quando chegar, para não ficar showing o login curto.
                    _ = PrefetchWindowsUserNameAsync();
                }
                catch (Exception ex)
                {
                    App.LoggingService?.LogWarning($"[DASHBOARD] Falha ao inicializar chip de conta: {ex.Message}");
                }

                // CARREGAMENTO ULTRA RÁPIDO: atribuição SÍNCRONA do DataContext no Loaded.
                // Antes era feito via DispatcherPriority.Background, o que renderizava um
                // quadro vazio (sem VM) e depois rebindava tudo (duplo render). Agora o
                // dashboard nasce já vinculado às propriedades iniciais do ViewModel.
                if (DataContext == null)
                {
                    DiagLog($"[DASHBOARD_VIEW][TID:{ltid}] 🔄 DataContext é NULL, criando NOVA instância...");
                    try
                    {
                        // ⚠️ CORREÇÃO: NÃO reutilizar singleton antigo do ViewModelLocator!
                        // Sempre criar uma NOVA instância fresca do DashboardViewModel
                        // para evitar flash de dados/UI antigas de navegações anteriores.
                        var dashboardVM = App.Services?.GetService(typeof(DashboardViewModel)) as DashboardViewModel;
                        if (dashboardVM != null)
                        {
                            DiagLog($"[DASHBOARD_VIEW][TID:{ltid}] ✅ Nova instância DashboardViewModel obtida (Hash: {dashboardVM.GetHashCode()})");
                            DataContext = dashboardVM;
                            DiagLog($"[DASHBOARD_VIEW][TID:{ltid}] DataContext definido com NOVA instância via Loaded event.");
                            try { VoltrisDiagnosticSystem.Instance.Timeline("DASHBOARD", "DashboardView: DataContext definido com nova instância"); } catch { }

                            // Sinalizar que a view está pronta e chamar inicialização assíncrona
                            DiagLog($"[DASHBOARD_VIEW][TID:{ltid}] 🚀 Chamando MarkViewReady() e InitializeAsync()...");
                            dashboardVM.MarkViewReady();
                            _ = dashboardVM.InitializeAsync();
                        }
                        else
                        {
                            DiagLog($"[DASHBOARD_VIEW][TID:{ltid}] ❌ GetService retornou NULL para DashboardViewModel!");
                        }
                    }
                    catch (Exception ex)
                    {
                        DiagLog($"[DASHBOARD_VIEW][TID:{ltid}] ❌ Erro ao obter ViewModel: {ex.Message}");
                        DiagLog($"[DASHBOARD_VIEW][TID:{ltid}] Stack: {ex.StackTrace}");
                    }
                }
                else
                {
                    DiagLog($"[DASHBOARD_VIEW][TID:{ltid}] ⚠️ DataContext JÁ EXISTE no Loaded! Tipo: {DataContext.GetType().Name}, Hash: {DataContext.GetHashCode()}");
                    try
                    {
                        var vm = DataContext as DashboardViewModel;
                        if (vm != null)
                        {
                            DiagLog($"[DASHBOARD_VIEW][TID:{ltid}] 🔍 Estado do ViewModel EXISTENTE:");
                            DiagLog($"[DASHBOARD_VIEW][TID:{ltid}]    - HealthStatusText: '{vm.HealthStatusText}'");
                            DiagLog($"[DASHBOARD_VIEW][TID:{ltid}]    - ScoreLabel: '{vm.ScoreLabel}'");
                        }
                    }
                    catch { }
                }

                NotifyViewVisibility();

                App.LoggingService?.LogInfo($"[DASHBOARD_VIEW][TID:{ltid}] Evento Loaded disparado");
                App.TelemetryService?.TrackEvent(
                    "PAGE_VIEW",
                    "Dashboard",
                    "Load",
                    success: true
                );
                DiagLog($"[DASHBOARD_VIEW][TID:{ltid}] Evento Loaded FIM");
                try { VoltrisDiagnosticSystem.Instance.Timeline("DASHBOARD", "DashboardView: Evento Loaded FIM"); } catch { }
            };

            // O Dashboard pode ser recriado a cada navegação. Sem o unsubscribe,
            // o CloudAccountService e o LicenseTokenStore guardariam handlers de
            // views mortas e cada troca de página vazaria mais um.
            Unloaded += (s, e) =>
            {
                try { UnsubscribeAccountEvents(); } catch { }
                try { UnsubscribeLicenseEvents(); } catch { }

                // [FIX:A-1] Descartar o ViewModel que ESTA view possui.
                //
                // BUG ORIGINAL: o comentário acima já reconhecia que "cada troca
                // de página vaziaria mais um", mas o descarte do ViewModel ficou
                // de fora. O DashboardViewModel se inscreve em 8 fontes
                // estáticas/singleton (SettingsService, BrainMetricsCache,
                // SystemMetricsCache, LicenseManager, CloudAccountService,
                // LocalizationService, ApplicationStateTracker, GamerViewModel) e
                // seu OnDisposing() — que desfaz TODAS elas corretamente — nunca
                // era chamado, porque o container registra o VM como Transient e
                // o MS DI só descarta na raiz, no encerramento do processo.
                //
                // EVIDENCIA (2026-09-27): 5 instâncias distintas em 11 s
                // (Hash 33624151, 22877402, 57198891, 55375379, 66729601),
                // todas vivas ao mesmo tempo. Cada uma recebia MetricsUpdated a
                // 4 Hz e fazia marshaling para a UI — era a origem direta dos 13
                // InvalidOperationException cross-thread e do log "[AIFace]
                // Brain->Visual" appearing 3x no mesmo milissegundo.
                //
                // Aqui a view que CRIOU o VM (via GetService no Loaded) é quem o
                // descarta. O DataContext é limpo para que, num próximo Loaded, a
                // ramificação "DataContext == null" resolva uma instância nova —
                // preservando exatamente a intenção de "instância fresca por
                // navegação" documentada no Loaded, agora COM descarte.
                try
                {
                    if (DataContext is DashboardViewModel vm && vm is IDisposable disposable)
                    {
                        DataContext = null;
                        disposable.Dispose();
                        App.LoggingService?.LogInfo(
                            "[FIX:A-1] DashboardViewModel descartado no Unloaded | " +
                            $"Hash={vm.GetHashCode()} | Dispose() agora executa OnDisposing() e " +
                            $"desfaz as 8 assinaturas em fontes estaticas");
                    }
                }
                catch (Exception ex)
                {
                    App.LoggingService?.LogWarning($"[FIX:A-1] Falha ao descartar DashboardViewModel no Unloaded: {ex.Message}");
                }
            };

            DiagLog($"[DASHBOARD_VIEW][TID:{tid}] Construtor FIM");
            try { VoltrisDiagnosticSystem.Instance.Timeline("DASHBOARD", "DashboardView: Construtor FIM"); } catch { }
        }

        private void NotifyViewVisibility()
        {
            try
            {
                (DataContext as DashboardViewModel)?.SetViewVisible(IsVisible);
            }
            catch { }
        }

        private static readonly object _diagLock = new object();
        private static void DiagLog(string msg)
        {
            try
            {
                var logDir = LogDirectoryResolver.Resolve();
                System.IO.Directory.CreateDirectory(logDir);
                var path = System.IO.Path.Combine(logDir, $"StartupDiag_{DateTime.Now:yyyy-MM-dd}.log");
                lock (_diagLock)
                {
                    System.IO.File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\r\n");
                }
            }
            catch { }
        }

        // ============================================================
        // CHIP DE CONTA VOLTRIS CLOUD
        //
        // Substitui o botão de nuvem do title bar (que era apenas um
        // ícone, sem indicar logado/deslogado). O chip tem dois estados
        // e reage ao CloudAccountService, que é a fonte de verdade —
        // o mesmo que o MainWindow já consome. Nenhuma lógica de
        // vínculo/desvínculo foi duplicada: só orquestramos o que
        // MainWindow, SettingsView e CloudAccountService já fazem.
        // ============================================================
        private bool _accountSubscribed;

        private void SubscribeAccountEvents()
        {
            if (_accountSubscribed) return;
            try
            {
                // PropertyChanged e nao AccountStateChanged: TODA mutacao de
                // IsLinked/LinkedEmail passa pelos property setters, que disparam
                // PropertyChanged. Ja ConfirmLinkedFromServer e UnlinkDeviceAsync
                // tambem disparam AccountStateChanged, mas SyncFromSettings e
                // OnSettingsLinkingStatusChanged NAO — e sao exatamente os
                // caminhos que o poll de 5 s usa. Assinar so AccountStateChanged
                // deixaria o chip desatualizado nesses casos.
                CloudAccountService.Instance.PropertyChanged += OnAccountPropertyChanged;
                _accountSubscribed = true;
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[DASHBOARD] Falha ao assinar eventos de conta: {ex.Message}");
            }
        }

        private void UnsubscribeAccountEvents()
        {
            if (!_accountSubscribed) return;
            try
            {
                CloudAccountService.Instance.PropertyChanged -= OnAccountPropertyChanged;
            }
            catch { }
            finally
            {
                _accountSubscribed = false;
            }
        }

        private void OnAccountPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // Só as duas propriedades que definem o chip; LinkedEmailDisplay e
            // IsNotLinked disparam junto e não interestam aqui.
            if (e.PropertyName != nameof(CloudAccountService.IsLinked) &&
                e.PropertyName != nameof(CloudAccountService.LinkedEmail))
                return;

            try
            {
                var service = CloudAccountService.Instance;
                bool isLinked = service.IsLinked;
                string? email = service.LinkedEmail;

                Dispatcher.BeginInvoke(new Action(() => UpdateAccountChip(isLinked, email)));
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[DASHBOARD] Erro ao atualizar chip de conta: {ex.Message}");
            }
        }

        /// <summary>
        /// Aplica o estado visual do chip.
        ///
        /// REGRA DE DIVISÃO DE RESPONSABILIDADE:
        ///   chip     -> identifica o USUÁRIO do Windows (nome amigável)
        ///   dropdown -> identifica a CONTA (email)
        ///
        /// O email no chip era ruim por dois motivos: é longo (truncava com
        /// reticências) e não responde "quem está usando este PC?", que é a
        /// pergunta que o chip faz. O email continua a um clique, no lugar onde
        /// se espera informação de conta.
        ///
        /// Deslogado mostra o CTA de login. Quando não há email (build antigo ou
        /// ainda não sincronizado), mantém o CTA visível em vez de mostrar vazio.
        /// </summary>
        private void UpdateAccountChip(bool isLinked, string? email)
        {
            try
            {
                if (AccountSignInButton == null || AccountLinkedButton == null)
                    return;

                bool hasEmail = isLinked && !string.IsNullOrWhiteSpace(email);

                AccountSignInButton.Visibility = hasEmail ? Visibility.Collapsed : Visibility.Visible;
                AccountLinkedButton.Visibility = hasEmail ? Visibility.Visible : Visibility.Collapsed;

                if (!hasEmail) return;

                // ── CHIP: o USUÁRIO ──
                var userText = FindInAccountTemplate("AccountMachineText") as TextBlock;
                if (userText != null) userText.Text = GetWindowsUserDisplayName();

                // ── DROPDOWN: a CONTA ──
                // O cabeçalho do menu é um StackPanel declarado no XAML, então os
                // nomes vivem no namescope DELE, não no template do botão.
                if (MenuAccountHeader?.Header is FrameworkElement headerRoot)
                {
                    var headerEmail = headerRoot.FindName("MenuHeaderEmail") as TextBlock;
                    if (headerEmail != null) headerEmail.Text = email!;
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[DASHBOARD] Erro ao aplicar visual do chip: {ex.Message}");
            }
        }

        private object? FindInAccountTemplate(string name)
        {
            try
            {
                if (AccountLinkedButton == null) return null;
                AccountLinkedButton.ApplyTemplate();
                return AccountLinkedButton.Template?.FindName(name, AccountLinkedButton);
            }
            catch
            {
                return null;
            }
        }

        // ============================================================
        // FAIXA DE LICENÇA NO DROPDOWN
        //
        // Só mostra o PLANO EXATO (Standard / Pro / Enterprise) e some
        // quando não há plano. Nada de contagem de dias: o dropdown
        // responde "qual licença eu tenho", e a data/validade continua
        // sendo da página LicenseActivationView, que é a fonte única.
        //
        // Os dados vêm de LicenseTokenStore, que é cache estático em
        // memória — leitura síncrona, instantânea e sem rede. A página
        // continua sendo a única que ativa/desativa.
        // ============================================================

        private bool _licenseSubscribed;

        private void SubscribeLicenseEvents()
        {
            if (_licenseSubscribed) return;
            try
            {
                LicenseTokenStore.StaticPropertyChanged += OnLicenseStoreChanged;
                LicenseTokenStore.IsProActiveChanged += OnLicenseStoreChanged;
                _licenseSubscribed = true;
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[DASHBOARD] Falha ao assinar eventos de licença: {ex.Message}");
            }
        }

        private void UnsubscribeLicenseEvents()
        {
            if (!_licenseSubscribed) return;
            try
            {
                LicenseTokenStore.StaticPropertyChanged -= OnLicenseStoreChanged;
                LicenseTokenStore.IsProActiveChanged -= OnLicenseStoreChanged;
            }
            catch { }
            finally
            {
                _licenseSubscribed = false;
            }
        }

        private void OnLicenseStoreChanged(object? sender, EventArgs e)
        {
            try
            {
                Dispatcher.BeginInvoke(new Action(UpdateLicenseStrip));
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[DASHBOARD] Erro ao atualizar faixa de licença: {ex.Message}");
            }
        }

        /// <summary>
        /// Aplica o plano na faixa. Some quando não há plano pago — nesse caso
        /// o item "Gerenciar licença" já cobre a ação, e a faixa só poluiria.
        /// </summary>
        private void UpdateLicenseStrip()
        {
            try
            {
                if (MenuLicenseStrip == null) return;

                bool hasPlan = TryGetLicensePlan(out string planName, out string accentHi, out string accentLo);

                // A faixa de plano some sem licença (None/Trial), mas o item
                // "Gerenciar licença" NÃO some: ele é justamente a porta de
                // entrada para ativar. Por isso a cor do ícone dele é aplicada
                // nos DOIS casos — antes, sem plano, ele ficava no azul padrão
                // herdado do XAML, o que não significava nada.
                if (hasPlan)
                {
                    MenuLicenseStrip.Visibility = Visibility.Visible;
                    if (MenuLicenseStrip.Header is FrameworkElement root)
                    {
                        ApplyPlanAccents(root, planName, accentHi, accentLo);
                    }
                }
                else
                {
                    MenuLicenseStrip.Visibility = Visibility.Collapsed;
                }

                if (MenuManageLicense?.Header is FrameworkElement manageRoot)
                {
                    // Ícone de CHAVE: o mesmo que identifica licença/ativação.
                    // A cor segue o plano quando existe; sem plano, cai numa
                    // cor neutra de licença em vez do azul padrão solto.
                    ApplyPlanAccents(manageRoot, null,
                        hasPlan ? accentHi : LicenseFallbackAccentHi,
                        hasPlan ? accentLo : LicenseFallbackAccentLo);
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[DASHBOARD] Erro ao aplicar faixa de licença: {ex.Message}");
            }
        }

        /// <summary>
        /// Cor usada no ícone de licença quando não há plano nenhum (usuário
        /// gratuito/trial). Dourado neutro: identifica licença sem fingir que
        /// existe um plano, e não é o azul padrão herdado do XAML.
        /// </summary>
        private const string LicenseFallbackAccentHi = "#FFD76A";
        private const string LicenseFallbackAccentLo = "#E08A18";

        /// <summary>
        /// Aplica as cores do plano a um tile de licença. Os elementos são
        /// procurados por nome, o mesmo contrato do header — assim o ícone da
        /// faixa e o de "Gerenciar licença" não podem divergir de cor.
        /// </summary>
        private static void ApplyPlanAccents(FrameworkElement root, string? planText, string accentHi, string accentLo)
        {
            if (planText != null && root.FindName("LicensePlanText") is TextBlock planLabel)
            {
                planLabel.Text = planText;
                planLabel.Foreground = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString(accentHi));
            }

            if (root.FindName("LicenseTile") is Border tile && tile.Background is LinearGradientBrush gradient)
            {
                gradient.GradientStops[0].Color = (Color)ColorConverter.ConvertFromString(accentHi);
                gradient.GradientStops[1].Color = (Color)ColorConverter.ConvertFromString(accentLo);
            }

            if (root.FindName("ManageLicenseStopHi") is GradientStop stopHi)
            {
                stopHi.Color = (Color)ColorConverter.ConvertFromString(accentHi);
            }
            if (root.FindName("ManageLicenseStopLo") is GradientStop stopLo)
            {
                stopLo.Color = (Color)ColorConverter.ConvertFromString(accentLo);
            }
            if (root.FindName("ManageLicenseTileGlow") is System.Windows.Media.Effects.DropShadowEffect glow)
            {
                glow.Color = (Color)ColorConverter.ConvertFromString(accentHi);
            }
        }

        /// <summary>
        /// Descobre o plano exato e suas cores. Só.Standard/Pro/Enterprise contam:
        /// "None" e "Trial" não são licença, então a faixa fica oculta.
        ///
        /// As cores NÃO são inventadas aqui — são as mesmas aurás que o
        /// LicenseShieldIcon já usa por plano (ciano / magenta / dourado),
        /// para o usuário reconhecer o plano em qualquer lugar do app.
        /// </summary>
        private static bool TryGetLicensePlan(out string planName, out string accentHi, out string accentLo)
        {
            planName = string.Empty;
            accentHi = "#38BDF8";
            accentLo = "#1EA8E0";

            try
            {
                string raw = (LicenseTokenStore.LicenseDisplayName ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(raw)) raw = (LicenseTokenStore.LicenseType ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(raw)) return false;

                switch (raw.ToLowerInvariant())
                {
                    case "standard":
                        planName = "Standard";
                        accentHi = "#7DE8FF";
                        accentLo = "#1EA8E0";
                        return true;

                    case "pro":
                        planName = "Pro";
                        accentHi = "#FFC4F3";
                        accentLo = "#D03CE8";
                        return true;

                    case "enterprise":
                        planName = "Enterprise";
                        accentHi = "#FFE3A8";
                        accentLo = "#E08A18";
                        return true;

                    default:
                        // "None", "Trial" ou qualquer valor desconhecido.
                        return false;
                }
            }
            catch
            {
                return false;
            }
        }

        private void MenuManageLicense_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Mesmo caminho do botão de chave do title bar. A página
                // LicenseActivationView é a única que ativa e desativa.
                GetMainWindow()?.NavigateToPageSafe("License");
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[DASHBOARD] Erro ao navegar para licença: {ex.Message}");
            }
        }

        /// <summary>
        /// Dispara a resolução do nome amigável do Windows e, quando ela chega,
        /// repinta o chip. Sem isso o chip ficaria preso no fallback
        /// (Environment.UserName), porque o WMI é lento demais para a thread da UI.
        /// </summary>
        private async Task PrefetchWindowsUserNameAsync()
        {
            try
            {
                // Primeira chamada agenda a resolução em segundo plano.
                _ = GetWindowsUserDisplayName();

                var deadline = DateTime.UtcNow.AddSeconds(4);
                while (!_windowsUserResolved && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(120).ConfigureAwait(false);
                }

                if (!_windowsUserResolved) return;

                await Dispatcher.InvokeAsync(() =>
                {
                    bool isLinked = CloudAccountService.Instance.IsLinked;
                    string? email = CloudAccountService.Instance.LinkedEmail;
                    UpdateAccountChip(isLinked, email);
                });
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[DASHBOARD] Falha ao pré-carregar nome do usuário: {ex.Message}");
            }
        }

        /// <summary>
        /// Nome do usuário do Windows como ele aparece para a pessoa.
        ///
        /// Ordem de preferência:
        ///   1. Nome amigável do Windows ("Doug Hansen") — é o que o usuário
        ///      reconhece como "o meu nome". Vem de Win32_UserAccount.FullName.
        ///   2. Environment.UserName sem o domínio ("DESKTOP-ABC\doug" -> "doug")
        ///   3. "Utilizador"
        ///
        /// O WMI é lento (dezenas de ms), então roda uma única vez, em thread de
        /// fundo, e o resultado fica em cache. Nunca na thread da UI.
        /// </summary>
        private static string? _cachedWindowsUser;
        private static bool _windowsUserResolved;
        private static int _windowsUserRefreshing;

        private static string GetWindowsUserDisplayName()
        {
            if (_windowsUserResolved && _cachedWindowsUser != null)
                return _cachedWindowsUser;

            // Dispara a resolução em segundo plano e usa o fallback já.
            if (Interlocked.CompareExchange(ref _windowsUserRefreshing, 1, 0) == 0)
            {
                _ = Task.Run(() =>
                {
                    try
                    {
                        string resolved = ResolveWindowsUserFullName() ?? StripDomain(Environment.UserName);
                        if (!string.IsNullOrWhiteSpace(resolved))
                        {
                            _cachedWindowsUser = resolved;
                        }
                    }
                    catch (Exception ex)
                    {
                        App.LoggingService?.LogWarning($"[DASHBOARD] Falha ao resolver nome do usuário: {ex.Message}");
                    }
                    finally
                    {
                        _windowsUserResolved = true;
                        Interlocked.Exchange(ref _windowsUserRefreshing, 0);
                    }
                });
            }

            return _cachedWindowsUser ?? StripDomain(Environment.UserName) ?? "Utilizador";
        }

        /// <summary>Nome amigável da conta local via WMI, ou null se não houver.</summary>
        private static string? ResolveWindowsUserFullName()
        {
            try
            {
                string login = Environment.UserName;
                if (string.IsNullOrWhiteSpace(login)) return null;

                using var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT FullName FROM Win32_UserAccount WHERE Name = '" + login.Replace("'", "''") + "'");

                using var results = searcher.Get();
                foreach (System.Management.ManagementObject obj in results)
                {
                    using (obj)
                    {
                        var full = obj["FullName"] as string;
                        if (!string.IsNullOrWhiteSpace(full))
                            return full.Trim();
                    }
                }
            }
            catch
            {
                // WMI indisponível (política, serviço parado, container):
                // o chamador cai no Environment.UserName.
            }

            return null;
        }

        /// <summary>"DOMINIO\doug" -> "doug".</summary>
        private static string? StripDomain(string? userName)
        {
            if (string.IsNullOrWhiteSpace(userName)) return null;

            int slash = userName.IndexOf('\\');
            if (slash >= 0 && slash < userName.Length - 1)
                return userName.Substring(slash + 1);

            return userName;
        }

        private MainWindow? GetMainWindow()
        {
            try
            {
                if (Window.GetWindow(this) is MainWindow direct) return direct;
                return Application.Current?.MainWindow as MainWindow;
            }
            catch
            {
                return null;
            }
        }

        // ── Ações ──────────────────────────────────────────────

        private void AccountSignIn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // O MainWindow já tem todo o fluxo (trava de onboarding,
                // WelcomeLinkWindow, refresh de status). Reutilizamos em vez
                // de duplicar — é o mesmo caminho do antigo botão do header.
                var main = GetMainWindow();
                if (main == null)
                {
                    App.LoggingService?.LogWarning("[DASHBOARD] MainWindow nao encontrado; login cancelado.");
                    return;
                }

                main.TriggerAccountLinkFlow();
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[DASHBOARD] Erro ao abrir fluxo de vinculo", ex);
                ModernMessageBox.Show(
                    LocalizationService.Instance.GetString("LinkActionProcessError"),
                    LocalizationService.Instance.GetString("Error"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AccountMenu_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (AccountContextMenu == null || sender is not FrameworkElement fe) return;

                AccountContextMenu.PlacementTarget = fe;
                AccountContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                AccountContextMenu.HorizontalOffset = 0;
                AccountContextMenu.VerticalOffset = 6;
                AccountContextMenu.IsOpen = true;
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[DASHBOARD] Erro ao abrir menu de conta: {ex.Message}");
            }
        }

        private void MenuOpenDashboard_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var url = SiteConfig.AccountDashboardUrl;
                App.LoggingService?.LogInfo($"[DASHBOARD] Abrindo conta no site ({LocalizationService.Instance.CurrentLanguage}): {url}");

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[DASHBOARD] Erro ao abrir dashboard da conta", ex);
                ModernMessageBox.Show(
                    string.Format(LocalizationService.Instance.GetString("OpenLinkError"), ex.Message),
                    LocalizationService.Instance.GetString("Error"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MenuAccountSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                GetMainWindow()?.NavigateToPageSafe("Settings");
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[DASHBOARD] Erro ao navegar para configuracoes: {ex.Message}");
            }
        }

        private async void MenuUnlink_Click(object sender, RoutedEventArgs e)
        {
            // Mesmo fluxo do SettingsView: confirmar, chamar o serviço, e só
            // mostrar sucesso com a confirmação real do servidor.
            if (sender is MenuItem mi) mi.IsEnabled = false;
            try
            {
                var confirm = ModernMessageBox.Show(
                    LocalizationService.Instance.GetString("ConfirmUnlinkMsg"),
                    LocalizationService.Instance.GetString("ConfirmUnlinkTitle"),
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (confirm != MessageBoxResult.Yes)
                    return;

                var unlinked = await CloudAccountService.Instance.UnlinkDeviceAsync();

                if (!unlinked)
                {
                    ModernMessageBox.Show(
                        string.Format(
                            LocalizationService.Instance.GetString("UnlinkErrorMsg"),
                            LocalizationService.Instance.GetString("UnlinkNotConfirmedReason")),
                        LocalizationService.Instance.GetString("UnlinkErrorTitle"),
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                GetMainWindow()?.ForceUpdateLinkingStatus();
                UpdateAccountChip(false, null);

                ModernMessageBox.Show(
                    LocalizationService.Instance.GetString("UnlinkSuccessMsg"),
                    LocalizationService.Instance.GetString("UnlinkSuccessTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Information);

                App.LoggingService?.LogInfo("[DASHBOARD] Dispositivo desvinculado pelo chip de conta.");
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[DASHBOARD] Erro ao desvincular pelo chip de conta", ex);
                ModernMessageBox.Show(
                    string.Format(LocalizationService.Instance.GetString("UnlinkErrorMsg"), ex.Message),
                    LocalizationService.Instance.GetString("UnlinkErrorTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (sender is MenuItem mi2) mi2.IsEnabled = true;
            }
        }
    }
}
