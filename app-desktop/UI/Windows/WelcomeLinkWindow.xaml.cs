using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Diagnostics;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Enterprise;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.Cloud;

using System.Windows.Interop;
using VoltrisOptimizer.UI.Helpers;
using VoltrisOptimizer.Helpers;
using System.Threading.Tasks;

namespace VoltrisOptimizer.UI.Windows
{
    public partial class WelcomeLinkWindow : Window
    {
        public bool LinkRequested { get; private set; }

        /// <summary>
        /// Identificador de correlação desta tentativa de vínculo. O mesmo ID vai
        /// para o header de todas as requisições e para o log, permitindo
        /// rastrear a operação no servidor.
        /// </summary>
        private readonly string _correlationId = CorrelationId.New();

        public WelcomeLinkWindow()
        {
            InitializeComponent();
            SourceInitialized += WelcomeLinkWindow_SourceInitialized;
            RoundedWindowHelper.Apply(this, 24);
            Loaded += (s, e) => 
            {
                var helper = new WindowInteropHelper(this);
                Win32WindowHelper.ForceForegroundWindow(helper.Handle);
                CloudAccountService.Instance.AccountStateChanged += OnCloudAccountStateChanged;
            };
        }

        /// <summary>
        /// [FIX:A-2] Fecha a janela independentemente de ela estar modal ou não.
        ///
        /// BUG ORIGINAL (introduzido pela correção A-2): esta janela era aberta
        /// com <c>ShowDialog()</c> e, ao terminar, definia <c>DialogResult</c>.
        /// Com a correção A-2 ela passou a ser aberta com <c>Show()</c> — para
        /// não travar o startup num loop modal — mas os seis pontos de saída
        /// continuaram atribuindo <c>DialogResult</c>.
        ///
        /// <see cref="Window.DialogResult"/> só pode ser atribuído quando a
        /// janela foi aberta como caixa de diálogo. Numa janela modeless isso
        /// lança:
        ///
        ///   InvalidOperationException: DialogResult somente pode ser definido
        ///   após Window ser criado e exibido como caixa de diálogo.
        ///
        /// E não era uma exceção contida: a validação em runtime mostrou que ela
        /// escalava até <c>App_DispatcherUnhandledException</c> — ou seja, a
        /// correção A-2 passou a derrubar o app toda vez que o usuário pulava a
        /// vinculação. O log provou isso:
        ///
        ///   [WELCOME] Usuário pulou a vinculação
        ///   [FIX:C-3] FIRST-CHANCE ... InvalidOperationException: DialogResult ...
        ///   [App] DispatcherUnhandledException: DialogResult somente pode ser definido ...
        ///
        /// Este helper decide pelo estado real da janela, então os dois caminhos
        /// de exibição passam a funcionar e nenhum deles lança.
        /// </summary>
        private void CloseWindow(bool? dialogResult)
        {
            try
            {
                if (dialogResult.HasValue)
                {
                    // Atribuir DialogResult é a forma CORRETA de fechar uma caixa
                    // de diálogo modal, e lança InvalidOperationException se a
                    // janela não for modal. Em vez de consultar um flag (que não
                    // é confiável antes da primeira exibição), tentamos e
                    // tratamos: o proprio WPF nos diz se a janela é modal.
                    try
                    {
                        DialogResult = dialogResult;
                        return;
                    }
                    catch (InvalidOperationException)
                    {
                        // Modeless: seguir por Close(), que é o caminho válido.
                        App.LoggingService?.LogDebug(
                            $"[FIX:A-2] Welcome é modeless: usando Close() em vez de DialogResult " +
                            $"(o valor sugerido era {dialogResult}).");
                    }
                }

                Close();
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[WELCOME] Falha ao fechar a janela: {ex.Message}");
                try { Close(); } catch { /* nada a fazer */ }
            }
        }

        private void OnCloudAccountStateChanged(object? sender, AccountStateChangedEventArgs args)
        {
            if (args.IsLinked)
            {
                Dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        if (!IsVisible) return;

                        if (Application.Current.MainWindow is MainWindow mainWindow)
                            mainWindow.ForceUpdateLinkingStatus();

                        string userPlaceholder = LocalizationService.Instance.GetString("WelcomeUserPlaceholder");
                        string successMsgFormat = LocalizationService.Instance.GetString("WelcomeSuccessMsg");
                        string successTitle = LocalizationService.Instance.GetString("WelcomeSuccessTitle");
                        var successAlert = new ModernAlertWindow(
                            string.Format(successMsgFormat, args.Email ?? userPlaceholder),
                            successTitle,
                            true
                        );

                        if (Application.Current.MainWindow != null && Application.Current.MainWindow.IsVisible)
                        {
                            successAlert.Owner = Application.Current.MainWindow;
                            successAlert.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                        }
                        else
                        {
                            successAlert.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                        }

                        successAlert.ShowDialog();

                        CloseWindow(true);
                    }
                    catch (Exception ex)
                    {
                        App.LoggingService?.LogWarning($"[WELCOME] Erro no OnCloudAccountStateChanged: {ex.Message}");
                        try { CloseWindow(true); } catch { }
                    }
                });
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            try
            {
                CloudAccountService.Instance.AccountStateChanged -= OnCloudAccountStateChanged;

                var settings = SettingsService.Instance.Settings;
                if (!settings.WelcomePromptShown)
                {
                    settings.IsFirstRun = false;
                    settings.WelcomePromptShown = true;
                    // If they didn't explicitly link, ensure IsDeviceLinked is false
                    if (string.IsNullOrEmpty(settings.LinkedUserEmail))
                    {
                        settings.IsDeviceLinked = false;
                    }
                    SettingsService.Instance.SaveSettings();
                    App.LoggingService?.LogInfo("[WELCOME] Window closed (fallback save) - Usuário pulou a vinculao ou fechou a janela");
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[WELCOME] Erro no OnClosed: {ex.Message}", ex);
            }
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private async void LinkButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                App.LoggingService?.LogInfo("[WELCOME] Iniciando LinkButton_Click");
                
                // Verificar se é verificação manual (botão mudou o texto)
                string manualCheckText = LocalizationService.Instance.GetString("WelcomeManualCheck");
                if (LinkButton.Content.ToString().Contains(manualCheckText))
                {
                    LinkButton.IsEnabled = false;
                    LinkButton.Content = LocalizationService.Instance.GetString("WelcomeChecking");
                    
                    var currentSettings = SettingsService.Instance.Settings;
                    var currentInstallationId = currentSettings.InstallationId;
                    
                    if (!string.IsNullOrEmpty(currentInstallationId))
                    {
                        // Verificar imediatamente
                        await CheckLinkStatusNow(currentInstallationId);
                    }
                    else
                    {
                        new ModernAlertWindow(
                            LocalizationService.Instance.GetString("WelcomeNoDeviceFound"), 
                            LocalizationService.Instance.GetString("WelcomeErrorTitle"), 
                            false
                        ).ShowDialog();
                        LinkButton.IsEnabled = true;
                        LinkButton.Content = LocalizationService.Instance.GetString("WelcomeBtnLinkNow");
                    }
                    return;
                }
                
                // 1. Obter o ID de instalacao. O ID e PERSISTENTE e vem do
                //    MachineIdentityService (cache local + WMI/servicos). Nunca
                //    gerar um Guid novo aqui: o app perderia o vinculo a cada
                //    abertura e o backend criaria uma maquina fantasma por vez.
                var settings = SettingsService.Instance.Settings;
                var identityService = App.Services?.GetService(typeof(MachineIdentityService)) as MachineIdentityService;

                string installationId;
                if (identityService != null)
                {
                    var identity = await identityService.GetMachineIdentityAsync();
                    installationId = identity.MachineId;
                }
                else
                {
                    // Sem DI: resolve/gera um ID PERSISTENTE e determinístico.
                    installationId = MachineIdentityService.ResolvePersistedInstallationId(settings);
                }

                if (string.IsNullOrWhiteSpace(installationId))
                {
                    new ModernAlertWindow(
                        LocalizationService.Instance.GetString("WelcomeNoDeviceFound"),
                        LocalizationService.Instance.GetString("WelcomeErrorTitle"),
                        false
                    ).ShowDialog();
                    LinkButton.IsEnabled = true;
                    LinkButton.Content = LocalizationService.Instance.GetString("WelcomeBtnLink");
                    return;
                }

                if (settings.InstallationId != installationId)
                {
                    settings.InstallationId = installationId;
                    SettingsService.Instance.SaveSettings();
                }

                // Registra a credencial DESTE dispositivo ANTES de abrir o
                // navegador. Sem isso o servidor fica sem hash e o app não
                // consegue provar que é o dono da máquina ao desvincular.
                await EnterpriseService.EnsureDeviceCredentialAsync(installationId);

                // 2. ABRIR NAVEGADOR IMEDIATAMENTE (Prioridade Máxima para UX)
                // O destino acompanha o idioma do app: português entra no site
                // nacional, inglês/espanhol no site internacional.
                var url = SiteConfig.LinkDeviceUrl(installationId);
                App.LoggingService?.LogInfo(
                    $"[WELCOME] [{_correlationId}] Abrindo browser ({LocalizationService.Instance.CurrentLanguage}): {url}");

                
                // Mudar estado visual IMEDIATAMENTE para feedback tátil ao usuário
                LinkButton.IsEnabled = false;
                LinkButton.Content = LocalizationService.Instance.GetString("WelcomeOpeningLink");

                bool browserOpened = false;
                try
                {
                    // Tentativa 1: ShellExecute
                    Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                    browserOpened = true;
                }
                catch 
                {
                    // Fallback rápido para cmd
                    try
                    {
                        Process.Start(new ProcessStartInfo { FileName = "cmd", Arguments = $"/c start \"\" \"{SanitizeUrl(url)}\"", CreateNoWindow = true, UseShellExecute = false });
                        browserOpened = true;
                    }
                    catch { browserOpened = false; }
                }

                if (!browserOpened)
                {
                    System.Windows.Clipboard.SetText(url);
                    new ModernAlertWindow(
                        string.Format(LocalizationService.Instance.GetString("WelcomeNoBrowserMsg"), url), 
                        LocalizationService.Instance.GetString("WelcomeWarningTitle"), 
                        false
                    ).ShowDialog();
                }

                // 3. Registrar hardware EM SEGUNDO PLANO (não bloqueia o browser)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await RegisterInstallationWithHardware(installationId);
                    }
                    catch (Exception ex)
                    {
                        // Antes a exceção era "silenciada": o registro falhava e
                        // ninguém soube. Agora fica no log com correlation ID.
                        App.LoggingService?.LogError(
                            $"[WELCOME] [{_correlationId}] Falha no registro de hardware em background: {ex.Message}");
                    }
                });

                // 4. Iniciar verificação em loop
                LinkButton.Content = LocalizationService.Instance.GetString("WelcomeAwaitingLink");
                LinkRequested = true;

                // Aguardar um pouco para o usuário chegar no site antes de começar a pollar intensamente
                await Task.Delay(3000);
                await WaitForLinking(installationId);
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[WELCOME] Erro crítico no LinkButton_Click: {ex.Message}", ex);
                string linkErrorMsgFormat = LocalizationService.Instance.GetString("WelcomeLinkErrorMsg");
                string errorTitle = LocalizationService.Instance.GetString("Error");
                new ModernAlertWindow(string.Format(linkErrorMsgFormat, ex.Message), errorTitle, false).ShowDialog();
                LinkButton.IsEnabled = true;
                LinkButton.Content = LocalizationService.Instance.GetString("WelcomeBtnLink");
            }
        }

        private async System.Threading.Tasks.Task WaitForLinking(string installationId)
        {
            App.LoggingService?.LogInfo($"[WELCOME] [{_correlationId}] WaitForLinking iniciado (polling do backend).");

            // 5 minutos com intervalo de 2s.
            //
            // Era 1s (300 requisicoes por tentativa de vinculo). O usuario esta
            // olhando a tela de "vinculando" e so precisa ver a confirmacao em
            // poucos segundos; 2s mantem a sensacao de imediato e corta o custo
            // pela metade. O fluxo real NAO depende disso: quem conclude o
            // vinculo e o /api/v1/install/link, chamado pelo navegador — este
            // loop so LÊ o estado para fechar a janela.
            const int maxRetries = 150;
            const int delayMs = 4000; // 4s — o fluxo real (navegador chama /install/link) não depende deste poll; 4s reduz o custo à metade sem degradar UX
            int retryCount = 0;
            string? linkedEmail = null;
            bool linkingSucceeded = false;
            bool lastPollUnreachable = false;

            // Intervalo inicial para o usuario chegar ao site
            await System.Threading.Tasks.Task.Delay(3000);

            while (retryCount < maxRetries && !linkingSucceeded)
            {
                try
                {
                    // Consulta tipada: distingue "nao vinculado" de "servidor fora".
                    var result = await EnterpriseService.Instance.GetLinkStatusAsync(installationId);
                    lastPollUnreachable = result.Outcome == LinkCheckOutcome.Unreachable;

                    if (result.IsLinked && !string.IsNullOrEmpty(result.Email))
                    {
                        App.LoggingService?.LogSuccess(
                            $"[WELCOME] [{_correlationId}] Vinculacao confirmada pelo backend: {result.Email}");
                        linkedEmail = result.Email;
                        linkingSucceeded = true;
                        break;
                    }

                    retryCount++;
                    if (retryCount % 10 == 0)
                    {
                        var note = lastPollUnreachable
                            ? "servidor inacessivel - o vinculo pode ja ter sido feito"
                            : "aguardando";
                        App.LoggingService?.LogInfo(
                            $"[WELCOME] [{_correlationId}] Ainda aguardando ({retryCount}/{maxRetries}) - {note}");
                    }

                    await System.Threading.Tasks.Task.Delay(delayMs);
                }
                catch (Exception ex)
                {
                    App.LoggingService?.LogWarning(
                        $"[WELCOME] [{_correlationId}] Erro no polling (tentativa {retryCount}): {ex.Message}");
                    retryCount++;
                    await System.Threading.Tasks.Task.Delay(delayMs);
                }
            }

            await Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    this.Hide();
                    if (Application.Current.MainWindow is MainWindow mainWindow)
                        mainWindow.ForceUpdateLinkingStatus();

                    if (linkingSucceeded && !string.IsNullOrEmpty(linkedEmail))
                    {
                        // Espelha localmente SOMENTE o que o backend ja confirmou.
                        CloudAccountService.Instance.ConfirmLinkedFromServer(linkedEmail);

                        await ShowAlertAsync(
                            string.Format(LocalizationService.Instance.GetString("WelcomeSuccessMsg"), linkedEmail),
                            LocalizationService.Instance.GetString("WelcomeSuccessTitle"),
                            true);

                        CloseWindow(true);
                    }
                    else if (lastPollUnreachable)
                    {
                        // Nao afirmar sucesso nem desvinculacao: o estado real e desconhecido.
                        App.LoggingService?.LogWarning(
                            $"[WELCOME] [{_correlationId}] Verificacao inconclusiva: servidor inacessivel.");

                        await ShowAlertAsync(
                            LocalizationService.Instance.GetString("WelcomeNotLinkedYetMsg") ??
                                "Nao foi possivel confirmar o vinculo com o servidor. Verifique a internet e clique em verificar novamente."
                                + $" (id: {_correlationId})",
                            LocalizationService.Instance.GetString("WelcomeErrorTitle") ?? "Verificacao inconclusiva",
                            false);

                        LinkButton.IsEnabled = true;
                        LinkButton.Content = LocalizationService.Instance.GetString("WelcomeBtnLinkNow");
                        this.Show();
                    }
                    else
                    {
                        // Tempo esgotado com resposta conclusiva de "nao vinculado".
                        await ShowAlertAsync(
                            LocalizationService.Instance.GetString("WelcomeLinkTimeoutMsg") ?? "Link request timed out. Please try again.",
                            LocalizationService.Instance.GetString("WelcomeWarningTitle") ?? "Linking Timeout",
                            false);

                        LinkButton.IsEnabled = true;
                        LinkButton.Content = LocalizationService.Instance.GetString("WelcomeBtnLink");
                        this.Show();
                    }
                }
                catch (Exception ex)
                {
                    App.LoggingService?.LogError($"[WELCOME] [{_correlationId}] Erro ao exibir resultado: {ex.Message}");
                    CloseWindow(true);
                }
            });
        }

        private System.Threading.Tasks.Task ShowAlertAsync(string message, string title, bool success)
        {
            var alert = new ModernAlertWindow(message, title, success);

            if (Application.Current.MainWindow != null && Application.Current.MainWindow.IsVisible)
            {
                alert.Owner = Application.Current.MainWindow;
                alert.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                alert.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            alert.ShowDialog();
            return System.Threading.Tasks.Task.CompletedTask;
        }

        private void ManualLoginButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var loginWin = new LoginWindow();
                if (loginWin.ShowDialog() == true)
                {
                    // Login bem-sucedido via API (token já salvo no Registro)
                    var settings = SettingsService.Instance.Settings;
                    settings.IsFirstRun = false;
                    settings.WelcomePromptShown = true;
                    
                    // NÃO marcar como vinculado - apenas indicar que o usuário já viu o Welcome
                    // IsDeviceLinked deve permanecer false quando o usuário pula
                    settings.IsDeviceLinked = false;
                    settings.LinkedUserEmail = null;
                    
                    SettingsService.Instance.SaveSettings();
                    
                    App.LoggingService?.LogInfo("[WELCOME] Usuário optou por continuar sem vincular - WelcomePromptShown = true, IsDeviceLinked = false");
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[WELCOME] Erro ao salvar skip: {ex.Message}", ex);
            }
            
            CloseWindow(false);

        }

        private void SkipButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var settings = SettingsService.Instance.Settings;
                settings.IsFirstRun = false;
                settings.WelcomePromptShown = true;
                settings.IsDeviceLinked = false;
                SettingsService.Instance.SaveSettings();
                App.LoggingService?.LogInfo("[WELCOME] Usuário pulou a vinculação");
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[WELCOME] Erro ao salvar skip: {ex.Message}", ex);
            }
            
            CloseWindow(false);

        }

        /// <summary>
        /// Valida se a URL é segura para uso: apenas http/https e apenas nos
        /// domínios oficiais do Voltris (nacional ou internacional).
        /// </summary>
        private static bool IsValidUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
            if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return false;
            return SiteConfig.IsKnownSiteUrl(url);
        }

        /// <summary>
        /// Sanitiza a URL removendo caracteres perigosos para uso em ArgumentStrings de processãos
        /// </summary>
        private static string SanitizeUrl(string url)
        {
            // Remover aspas duplas e caracteres de controle para evitar command injection
            return url.Replace("\"", "").Replace("&", "^").Replace("|", "").Replace(";", "");
        }

        private async System.Threading.Tasks.Task RegisterInstallationWithHardware(string installationId)
        {
            try
            {
                App.LoggingService?.LogInfo($"[WELCOME] Registrando instalação com hardware: {installationId}");
                
                // Obter informações de hardware
                var systemInfoService = App.Services?.GetService(typeof(ISystemInfoService)) as ISystemInfoService;
                if (systemInfoService == null)
                {
                    App.LoggingService?.LogWarning("[WELCOME] ISystemInfoService não disponível");
                    return;
                }
                
                var cpuInfo = await systemInfoService.GetCpuInfoAsync();
                var ramInfo = await systemInfoService.GetRamInfoAsync();
                var gpuInfo = await systemInfoService.GetGpuInfoAsync();
                var drives = await systemInfoService.GetDrivesInfoAsync();
                
                // Detectar tipo de disco principal (C:)
                string diskType = "HDD";
                if (drives != null && drives.Length > 0)
                {
                    var mainDrive = drives.FirstOrDefault(d => d.Letter.StartsWith("C", System.StringComparison.OrdinalIgnoreCase));
                    if (mainDrive != null)
                    {
                        diskType = mainDrive.IsSsd ? "SSD" : "HDD";
                    }
                }
                
                // Obter versão e edição do Windows
                string windowsVersion = systemInfoService.GetWindowsVersion();
                string windowsEdition = systemInfoService.GetWindowsEdition();
                int windowsBuild = systemInfoService.GetWindowsBuild();
                
                // Preparar payload de hardware
                var hardwareData = new
                {
                    pc_name = Environment.MachineName,
                    cpu_name = cpuInfo?.Name ?? "Unknown CPU",
                    ram_gb_total = ramInfo != null ? (int)Math.Round(ramInfo.TotalBytes / (1024.0 * 1024 * 1024)) : 0,
                    gpu_name = gpuInfo?.Name ?? "Unknown GPU",
                    disk_type = diskType,
                    os_name = windowsVersion,
                    os_build = windowsBuild.ToString(),
                    windows_edition = windowsEdition,
                    architecture = Environment.Is64BitOperatingSystem ? "x64" : "x86"
                };
                
                var payload = new
                {
                    installation_id = installationId,
                    app_version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0",
                    hardware = hardwareData
                };

                // Enviar para API. snake_case é obrigatório: com CamelCase o
                // servidor recebia `installationId` e respondia 400
                // "Missing installation_id".
                using var httpClient = new System.Net.Http.HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(30);

                var json = System.Text.Json.JsonSerializer.Serialize(payload, App.ApiJsonOptions);

                using var request = new System.Net.Http.HttpRequestMessage(
                    System.Net.Http.HttpMethod.Post,
                    "https://www.voltris.com.br/api/v1/install")
                {
                    Content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json")
                };
                request.Headers.TryAddWithoutValidation("x-correlation-id", _correlationId);

                using var response = await httpClient.SendAsync(request);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    App.LoggingService?.LogSuccess(
                        $"[WELCOME] [{_correlationId}] Instalacao registrada no servidor.");
                }
                else
                {
                    App.LoggingService?.LogWarning(
                        $"[WELCOME] [{_correlationId}] Falha ao registrar instalacao: " +
                        $"HTTP {(int)response.StatusCode} - {responseBody}");
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError(
                    $"[WELCOME] [{_correlationId}] Erro ao registrar instalacao: {ex.Message}", ex);
            }
        }

        private async System.Threading.Tasks.Task CheckLinkStatusNow(string installationId)
        {
            App.LoggingService?.LogInfo($"[WELCOME] [{_correlationId}] Verificacao manual iniciada.");

            try
            {
                LinkButton.IsEnabled = false;

                var result = await EnterpriseService.Instance.GetLinkStatusAsync(installationId);

                await Dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        if (result.IsLinked && !string.IsNullOrEmpty(result.Email))
                        {
                            // Vinculo confirmado pelo servidor: agora sim espelha local.
                            CloudAccountService.Instance.ConfirmLinkedFromServer(result.Email!);

                            this.Hide();
                            if (Application.Current.MainWindow is MainWindow mainWindow)
                                mainWindow.ForceUpdateLinkingStatus();

                            await ShowAlertAsync(
                                string.Format(LocalizationService.Instance.GetString("WelcomeSuccessMsg"), result.Email!),
                                LocalizationService.Instance.GetString("WelcomeSuccessTitle"),
                                true);

                            CloseWindow(true);
                        }
                        else if (result.Outcome == LinkCheckOutcome.Unreachable)
                        {
                            // Inconclusivo: nao dizer "nao vinculado".
                            App.LoggingService?.LogWarning(
                                $"[WELCOME] [{_correlationId}] Verificacao manual inconclusiva: {result.Error}");

                            await ShowAlertAsync(
                                string.Format(
                                    LocalizationService.Instance.GetString("WelcomeCheckErrorMsg") ??
                                        "Nao foi possivel consultar o servidor. Verifique a internet. (id: {0})",
                                    result.CorrelationId ?? _correlationId),
                                LocalizationService.Instance.GetString("WelcomeWarningTitle") ?? "Servidor inacessivel",
                                false);

                            LinkButton.IsEnabled = true;
                            LinkButton.Content = LocalizationService.Instance.GetString("WelcomeBtnLinkNow");
                        }
                        else
                        {
                            await ShowAlertAsync(
                                LocalizationService.Instance.GetString("WelcomeNotLinkedYetMsg") ??
                                    "Device is not linked yet. Please complete the linking on the website.",
                                LocalizationService.Instance.GetString("WelcomeWarningTitle") ?? "Not Linked",
                                false);

                            LinkButton.IsEnabled = true;
                            LinkButton.Content = LocalizationService.Instance.GetString("WelcomeBtnLinkNow");
                        }
                    }
                    catch (Exception ex)
                    {
                        App.LoggingService?.LogError($"[WELCOME] [{_correlationId}] Erro ao exibir resultado manual: {ex.Message}");
                        LinkButton.IsEnabled = true;
                        LinkButton.Content = LocalizationService.Instance.GetString("WelcomeBtnLink");
                    }
                });
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[WELCOME] [{_correlationId}] Erro em CheckLinkStatusNow: {ex.Message}", ex);

                await Dispatcher.InvokeAsync(() =>
                {
                    new ModernAlertWindow(
                        string.Format(LocalizationService.Instance.GetString("WelcomeCheckErrorMsg") ?? "Error checking link status: {0}", ex.Message),
                        LocalizationService.Instance.GetString("WelcomeErrorTitle") ?? "Error",
                        false
                    ).ShowDialog();

                    LinkButton.IsEnabled = true;
                    LinkButton.Content = LocalizationService.Instance.GetString("WelcomeBtnLink");
                });
            }
        }
    
        private void WelcomeLinkWindow_SourceInitialized(object? sender, System.EventArgs e)
        {
            try
            {
                var settings = VoltrisOptimizer.Services.SettingsService.Instance.Settings;
                if (settings.EnableTransparency)
                {
                    bool isLight = settings.Theme?.Equals("Light", System.StringComparison.OrdinalIgnoreCase) == true;
                    VoltrisOptimizer.UI.Helpers.BackdropHelper.ApplyModernBackdrop(this, VoltrisOptimizer.UI.Helpers.BackdropHelper.SystemBackdropType.Acrylic, isLight);
                }
            }
            catch (System.Exception ex)
            {
                VoltrisOptimizer.App.LoggingService?.LogError($"[WelcomeLinkWindow] Erro ao aplicar backdrop de transparencia", ex);
            }
        }}
}
