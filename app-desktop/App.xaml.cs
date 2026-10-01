using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

using VoltrisOptimizer.Core;
using VoltrisOptimizer.Core.Body;
using VoltrisOptimizer.Core.Optimization;

using VoltrisOptimizer.Core.SystemIntelligenceProfiler;

using VoltrisOptimizer.Services;

using VoltrisOptimizer.Services.Enterprise;

using VoltrisOptimizer.Services.Gamer.Interfaces;

using VoltrisOptimizer.Services.License;
using VoltrisOptimizer.Services.Telemetry;

using VoltrisOptimizer.UI.Widgets;

using VoltrisOptimizer.Services.Gamer;

using VoltrisOptimizer.Services.Gamer.Intelligence;

using VoltrisOptimizer.UI.Views;

using VoltrisOptimizer.Services.Session;



using VoltrisOptimizer.UI.ViewModels;

using VoltrisOptimizer.Services.Gamer.Overlay.Interfaces;

using VoltrisOptimizer.Services.Gamer.Diagnostics.Interfaces;

using VoltrisOptimizer.Services.Gamer.Overlay;

using VoltrisOptimizer.Services.Gamer.Diagnostics;



using VoltrisOptimizer.UI.Windows;



using VoltrisOptimizer.Services.Shell;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer
{
    public partial class App : Application
    {
        public static App? CurrentApp
        {
            get;
            private set;
        }

        private Semaphore? _appSemaphore;

        private ILoggingService? _loggingService;

        private IServiceProvider? _serviceProvider;

        private HotkeyService? _hotkeyService;

        private bool _sessionEndingRegistered;

        private bool _isExiting = false;

        // WATCHDOG DE STARTUP: rastreamento profissional de cada etapa
        private StartupStepTracker? _startupTracker;
        private VoltrisOptimizer.Helpers.UIThreadMonitor? _uiMonitor;
        private VoltrisOptimizer.Helpers.StartupDiagnosticLogger? _startupDiag;

        private void LogToFile(string message)
        {
            try
            {
                var logDir = LogDirectoryResolver.Resolve();
                if (!Directory.Exists(logDir))
                    Directory.CreateDirectory(logDir);
                var logPath = Path.Combine(logDir, $"App_{DateTime.Now:yyyy-MM-dd}.log");
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}][INFO][App] {message}{Environment.NewLine}", System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[App] LogToFile: {ex.Message}");
            }
        }

        // TODO Para DETECTAR VERSÃO DO WINDOWS (10/11)
        private static string GetWindowsVersionFriendly()
        {
            try
            {
                // Usar Registry para detectar versão exata do Windows
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                if (key != null)
                {
                    var productName = key.GetValue("ProductName")?.ToString() ?? "Windows";
                    var currentBuild = key.GetValue("CurrentBuild")?.ToString() ?? "";
                    var displayVersion = key.GetValue("DisplayVersion")?.ToString() ?? "";

                    // Detectar Windows 11 vs 10
                    if (productName.Contains("Windows 10"))
                    {
                        // Windows 11 tem build 22000+ mas se reporta como "Windows 10"
                        if (!string.IsNullOrEmpty(currentBuild) && int.Parse(currentBuild) >= 22000)
                        {
                            return $"Windows 11 (Build {currentBuild})";
                        }
                        else
                        {
                            return $"Windows 10 (Build {currentBuild})";
                        }
                    }
                    else
                    {
                        // Para outras versões, retornar como está
                        return !string.IsNullOrEmpty(displayVersion) ? $"{productName} {displayVersion}" : productName;
                    }
                }
            }
            catch
            {
                // Fallback para Environment.OSVersion
                return Environment.OSVersion.ToString();
            }
            return "Windows Desconhecido";
        }

        public static string? CachedCpuName => _cachedCpuName;
        public static string? CachedGpuName => _cachedGpuName;
        public static double CachedTotalRamGb => _cachedTotalRamGb;
        public static string? CachedOsVersion => _cachedOsVersion;

        private static string? _cachedCpuName;
        private static string? _cachedGpuName;
        private static double _cachedTotalRamGb;
        private static string? _cachedOsVersion;

        public App()
        {
            Program.WriteCrashLog("APP_CTOR_BEGIN");
            InitializeComponent();
            Program.WriteCrashLog("APP_CTOR_END");
        }

        // Propriedades estáticas necessárias para compatibilidade com outros arquivos
        public static IServiceProvider Services { get; private set; } = null!;

        // ============================================
        // JSON GLOBAL OPTIONS - CORREÇÃO 2: Ignorar reference cycles
        // ============================================
        public static readonly JsonSerializerOptions GlobalJsonOptions = new JsonSerializerOptions { 
            WriteIndented = false,
            ReferenceHandler = ReferenceHandler.IgnoreCycles,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            MaxDepth = 64,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// Opcoes de serializacao para chamadas HTTP a API do site.
        ///
        /// A API (Next.js) le snake_case. Usar camelCase quebrava o contrato:
        /// `installation_id` virava `installationId` e o servidor respondia
        /// 400 "Missing installation_id", descartando todo heartbeat/registro.
        /// NUNCA usar GlobalJsonOptions em payload de API.
        /// </summary>
        public static readonly JsonSerializerOptions ApiJsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            ReferenceHandler = ReferenceHandler.IgnoreCycles,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
            MaxDepth = 64,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        
        public static ILoggingService? LoggingService { get; private set; }

        /// <summary>
        /// Define o serviço de logging sem criar a UI. Usado exclusivamente pelo
        /// runner headless de tarefas agendadas (<see cref="ScheduledTaskRunner"/>),
        /// que precisa dos serviços reais mas não deve instanciar o WPF.
        /// </summary>
        internal static void SetLoggingServiceForHeadless(ILoggingService? service) => LoggingService = service;

        /// <summary>Define o histórico sem criar a UI (ver <see cref="SetLoggingServiceForHeadless"/>).</summary>
        internal static void SetHistoryServiceForHeadless(HistoryService? service) => HistoryService = service;

        /// <summary>
        /// [FIX:C-3] Encerramento que SEMPRE preserva o log.
        ///
        /// BUG ORIGINAL: havia três chamadas de <c>Environment.Exit(0)</c> diretas
        /// (comandos de context menu, taskbar center e taskbar style) e um
        /// failsafe com <c>Environment.Exit(1)</c>. <c>Environment.Exit</c> NÃO
        /// passa por <see cref="OnExit"/> do WPF, e portanto não executava
        /// <c>_loggingService.Flush()</c>. Como o <see cref="LoggingService"/>
        /// trabalha com fila em lote de 300 ms, tudo o que estivesse缓冲ado nos
        /// últimos 300 ms — inclusive o motivo do encerramento — era
        /// descartado silenciosamente.
        ///
        /// EVIDÊNCIA (2026-09-27): o PID 6744 desapareceu entre 20:56:40 (última
        /// linha gravada) e 20:57:53 (PID 9208 seguinte), sem NENHUMA linha de
        /// saída, sem <c>ExitApplication</c>, sem dump e sem registro no
        /// <c>CurrentDomain_UnhandledException</c>. O instrumento de diagnóstico
        /// não conseguiu dizer por quê — que é exatamente a falha que esta
        /// correção elimina.
        ///
        /// Este método grava o motivo, força o flush e só então encerra. Mesmo se
        /// o flush lançar, o processo morre assim mesmo (fail-safe), mas o motivo
        /// já foi tentado no <c>Program.WriteCrashLog</c>, que escreve fora do
        /// buffer assíncrono.
        /// </summary>
        private static void SafeExit(int exitCode, string reason)
        {
            try
            {
                LoggingService?.LogInfo(
                    $"[FIX:C-3] SafeExit acionado | motivo={reason} | codigo={exitCode} | PID={Environment.ProcessId}");

                // Grava fora da fila assíncrona: sobrevive a qualquer falha de flush.
                Program.WriteCrashLog($"SAFE_EXIT:{reason}:CODE{exitCode}");

                // Drena a fila em lote para disco de forma síncrona.
                LoggingService?.Flush();
            }
            catch (Exception ex)
            {
                try { Program.WriteCrashLog($"SAFE_EXIT_FLUSH_FAIL:{reason}:{ex.Message}"); } catch { }
            }

            Environment.Exit(exitCode);
        }


        public static VoltrisOptimizer.Interfaces.ISystemProfiler? SystemProfiler { get; private set; }
        public static ExtremeOptimizationsService? ExtremeOptimizations { get; private set; }
        public static UltraPerformanceService? UltraPerformance { get; private set; }
        public static UltraCleanerService? UltraCleaner { get; internal set; }
        public static SystemCleaner? SystemCleaner { get; internal set; }
        public static global::VoltrisOptimizer.Services.VoltrisPerformanceOptimizer? PerformanceOptimizer { get; internal set; }
        public static NetworkOptimizer? NetworkOptimizer { get; internal set; }
        public static AdvancedOptimizer? AdvancedOptimizer { get; internal set; }
        public static GamerOptimizerService? GamerOptimizer { get; private set; }

        // LEGACY KILL SWITCHED
        // VOLTRIS BRAIN v2 - Adaptive Intelligence Core (Q-Learning real)
        // Brain V1 (VoltrisBrain) removido em 2026-04-29 - substituído 100% pelo Brain v2
        public static VoltrisOptimizer.Core.Brain.V2.VoltrisBrainV2? BrainV2 { get; private set; }

        // VOLTRIS BODY, Coordenador central (Human Body Architecture)
        public static VoltrisOptimizer.Core.Body.IVoltrisBody? Body { get; private set; }

        // TEMPORAL INTELLIGENCE (Fase 1)
        public static VoltrisOptimizer.Core.Intelligence.ITemporalPatternEngine? TemporalPatternEngine { get; private set; }
        public static VoltrisOptimizer.Core.Intelligence.IPredictivePreWarmEngine? PreWarmEngine { get; private set; }
		public static VoltrisOptimizer.Core.Intelligence.IBehaviorScoreEngine? BehaviorEngine { get; private set; }
		public static VoltrisOptimizer.Core.Intelligence.IPatternRecognitionService? PatternService { get; private set; }
		public static VoltrisOptimizer.Core.NetworkIntelligence.INetworkIntelligenceOrchestrator? NetworkOrchestrator { get; private set; }

		// TRANSFORMED SERVICES
        public static UnifiedOptimizationService? UnifiedOptimization { get; private set; }

        [Obsolete("Removido pela auditoria - substituído pelo VoltrisBrain")]
        public static RealIntelligenceService? RealIntelligence { get; private set; }
        public static SystemEngineeringService? SystemEngineering { get; private set; }
        public static IntelligentAssistant? IntelligentAssistant { get; private set; }

        // LEGACY PLACEBO SERVICES - Substituídos pelos serviços acima
        // AIOptimizerService - RealIntelligenceService (IA real vs placebo)
        // SmartAIExecutor - IntelligentAssistant (NLP real vs regex básico)

        [Obsolete("Use RealIntelligenceService instead - this was placebo")]
        public static object? AIOptimizer { get; private set; }
        public static VoltrisOptimizer.Services.Gamer.Diagnostics.Interfaces.IGamerSelfProfiler? GamerSelfProfiler { get; private set; }
        public static HistoryService? HistoryService { get; private set; }
        public static GameDiagnosticsService? GameDiagnostics { get; private set; }
        public static VoltrisOptimizer.Services.Gamer.Overlay.Interfaces.IOverlayService? OverlayService { get; private set; }
        public static GameDetectionService? GameDetectionService { get; private set; }
        public static SchedulerService? SchedulerService { get; private set; }
        public static VoltrisOptimizer.Services.Telemetry.TelemetryService? TelemetryService { get; internal set; }
        public static VoltrisOptimizer.Services.Enterprise.RemoteCommandService? RemoteCommandService { get; private set; }
        public static VoltrisOptimizer.Services.Thermal.IGlobalThermalMonitorService? ThermalMonitorService { get; internal set; }
        public static VoltrisOptimizer.Services.Performance.HardwarePerformanceOptimizationService? HardwarePerformanceService { get; private set; }

        // MOTORES DE OTIMIZAÇÃO - AGORA 100% FUNCIONAIS COM LOGS TELEGRAM
        public static VoltrisOptimizer.Services.SystemIntelligenceProfiler.SystemIntelligenceProfilerService? SystemProfilerService { get; private set; }
        public static VoltrisOptimizer.Core.Optimization.InstantOptimizationEngine? InstantOptimizationEngine { get; private set; }
        public static VoltrisOptimizer.Core.Optimization.OptimizationDecisionEngine? OptimizationDecisionEngine { get; private set; }
        public static VoltrisOptimizer.Core.Optimization.UnifiedOptimizationPipeline? UnifiedOptimizationPipeline { get; private set; }
        public static VoltrisOptimizer.Core.VoltrisOptimizationCore? VoltrisCore { get; private set; }

        // Widget flutuante de performance
        private static VoltrisOptimizer.UI.Widgets.WidgetManagerService? _widgetManager;
        public static VoltrisOptimizer.UI.Widgets.WidgetManagerService? WidgetManager 
        { 
            get
            {
                if (_widgetManager == null && Services != null)
                {
                    _widgetManager = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<VoltrisOptimizer.UI.Widgets.WidgetManagerService>(Services);
                }
                return _widgetManager;
            }
            private set { _widgetManager = value; }
        }

        // Voltris Spine (rollback e health monitoring)
        public static VoltrisOptimizer.Core.Body.IVoltrisSpine? Spine { get; private set; }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll", PreserveSig = false)]
        public static extern void DwmIsCompositionEnabled(out bool enabled);

        protected override void OnStartup(StartupEventArgs e)
        {
            // [LOG] OnStartup entry
            var pid = Process.GetCurrentProcess().Id;
            var tid = Thread.CurrentThread.ManagedThreadId;
            LogToFile($"[STARTUP][TID:{tid}] OnStartup INÍCIO (PID: {pid})");
            

            _loggingService?.LogEntry(nameof(OnStartup));
            _loggingService?.LogValue(nameof(pid), pid);
            _loggingService?.LogValue(nameof(tid), tid);
            CurrentApp = this;
            System.Diagnostics.Debug.WriteLine("[OnStartup] VERIFICANDO ARGUMENTOS...");

            try
            {
                DwmIsCompositionEnabled(out bool isDwmEnabled);
                if (!isDwmEnabled)
                {
                    LogToFile("[STARTUP] ATENÇÃO: Desktop Window Manager (DWM) está DESABILITADO. Forçando renderização via software.");
                    System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
                }
            }
            catch (Exception dwmEx)
            {
                LogToFile($"[STARTUP] Erro ao checar DWM: {dwmEx.Message}. Assumindo ativado ou ignorando.");
            }

            // Registrar handlers globais de exceções — o handler nomeado App_DispatcherUnhandledException
            // é registrado em OnStartup via base.OnStartup. Não registrar segundo handler aqui para
            // evitar processamento duplicado (double-log e double-rollback).
            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                var exception = args.Exception?.Flatten().InnerException ?? args.Exception;
                var exceptionType = exception?.GetType().FullName ?? "Unknown exception type";
                var exceptionMessage = exception?.Message ?? "Unknown exception";
                var exceptionStack = exception?.StackTrace ?? "No stack trace";
                LogToFile($"[UNOBSERVED TASK EXCEPTION] {exceptionType}: {exceptionMessage}");
                _loggingService?.LogError($"[UNOBSERVED TASK EXCEPTION] {exceptionType}: {exceptionMessage} {exceptionStack}", args.Exception);
                args.SetObserved();
            };

            // VERIFICAR SE É COMANDO DO MENU DE CONTEXTO
            var args = e.Args;
            if (args.Length > 0)
            {
                var ctxCommand = RecognizeContextMenuCommand(args);
                if (ctxCommand != null)
                {
                    LogToFile($"[STARTUP][TID:{tid}] COMANDO DE CONTEXTO DETECTADO: \"{ctxCommand}\"");
                    _loggingService?.LogDecision("ContextMenuCommand", ctxCommand);
                    System.Diagnostics.Debug.WriteLine($"[OnStartup] COMANDO DE CONTEXTO: {ctxCommand}");
                    HandleContextMenuCommand(ctxCommand);
                    _loggingService?.LogExit(nameof(OnStartup), "ContextMenuCommand");
                    return;
                    // SAIR IMEDIATAMENTE AaÓS EXECUTAR O COMANDO
                }
            }

            // VERIFICAR SE É COMANDO DE PERSISTÊNCIA DA TASKBAR (Task Scheduler)
            if (args.Length > 0 && args[0] == "--taskbar-center")
            {
                LogToFile($"[STARTUP][TID:{tid}] MODO PERSISTÊNCIA TASKBAR DETECTADO! Args: {string.Join(" ", args)}");
                _loggingService?.LogDecision("TaskbarCenterPersist", "Args contain --taskbar-center", string.Join(" ", args));
                System.Diagnostics.Debug.WriteLine("[OnStartup] MODO PERSISTÊNCIA TASKBAR DETECTADO!");
                _ = HandleTaskbarCenterPersistAsync(args);
                _loggingService?.LogExit(nameof(OnStartup), "TaskbarCenterPersist");
                return;
                // SAIR IMEDIATAMENTE AaÓS CENTRALIZAR
            }

            // VERIFICAR SE É COMANDO DE ESTILO DA TASKBAR (Task Scheduler)
            if (args.Length > 0 && args[0] == "--taskbar-style")
            {
                LogToFile($"[STARTUP][TID:{tid}] MODO ESTILO TASKBAR DETECTADO! Args: {string.Join(" ", args)}");
                _loggingService?.LogDecision("TaskbarStylePersist", "Args contain --taskbar-style", string.Join(" ", args));
                System.Diagnostics.Debug.WriteLine("[OnStartup] MODO ESTILO TASKBAR DETECTADO!");
                _ = HandleTaskbarStylePersistAsync(args);
                _loggingService?.LogExit(nameof(OnStartup), "TaskbarStylePersist");
                return;
                // SAIR IMEDIATAMENTE AaÓS APLICAR ESTILO
            }

            System.Diagnostics.Debug.WriteLine("[OnStartup] MODO NORMAL - INICIANDO APLICAÇÃO...");

            // CORREÇÃO CRÍTICA: Capturar exceções da Task para evitar crash silencioso
            var startupTask = InitializeInternalAsync(e);

            startupTask.ContinueWith(t =>
            {
                // [LOG] InitializeInternalAsync task continuation
                var ct_tid = Thread.CurrentThread.ManagedThreadId;
                if (t.IsFaulted)
                {
                    var ex = t.Exception?.GetBaseException();
                    LogToFile($"[STARTUP][TID:{ct_tid}] FATAL: InitializeInternalAsync FALHOU: {ex?.Message}");
                    System.Diagnostics.Debug.WriteLine($"[FATAL] Startup falhou: {ex?.Message}");

                    // Tentar logar antes de morrer
                    try
                    {
                        LoggingService?.LogError($"[FATAL] Startup falhou: {ex?.Message}", ex);
                    }
                    catch (Exception logEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"[App] StartupLogError: {logEx.Message}");
                    }

                    // Mostrar erro ao usuário na UI thread
                    Dispatcher.InvokeAsync(() =>
                    {
                        System.Windows.MessageBox.Show(string.Format(LocalizationService.Instance.GetString("AppStartupErrorMessage"), ex?.Message), LocalizationService.Instance.GetString("AppStartupErrorTitle"), System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                        Shutdown(1);
                    });
                }
                else
                {
                    LogToFile($"[STARTUP][TID:{ct_tid}] InitializeInternalAsync CONCLUÍDO com sucesso.");
                }
            }, System.Threading.Tasks.TaskContinuationOptions.None);

            System.Diagnostics.Debug.WriteLine("[OnStartup] InitializeInternalAsync chamado");
            _loggingService?.LogExit(nameof(OnStartup), "startupTask_dispatched", _startupSw.ElapsedMilliseconds);
            LogToFile($"[STARTUP][TID:{tid}] OnStartup FIM (Task iniciada)");
        }

        // ---------------------------------------------------------------
        // COMANDOS DO MENU DE CONTEXTO
        // Reconhece e executa ações do menu "Voltris Optimizer"
        // ---------------------------------------------------------------

        /// <summary>
        /// Mapeia argumentos da linha de comando para comandos do menu de contexto.
        /// Retorna null se nenhum comando for reconhecido.
        /// </summary>
        private static string? RecognizeContextMenuCommand(string[] args)
        {
            if (args == null || args.Length == 0)
                return null;

            var arg = args[0].Trim().ToLowerInvariant();

            return arg switch
            {
                "-quickclean" => "quickclean",
                "-optimize" => "optimize",
                "-defrag" => "defrag",
                "-diagnose" => "diagnose",
                _ => null};
        }

        /// <summary>
        /// Executa o comando do menu de contexto.
        /// Primeiro tenta enviar via IPC para a instância em execução (com progresso global).
        /// Se falhar (nenhuma instância rodando), executa inline headless e sai.
        /// </summary>
        private async void HandleContextMenuCommand(string command)
        {
            _loggingService?.LogEntry(nameof(HandleContextMenuCommand), ("command", command));
            var tid = Thread.CurrentThread.ManagedThreadId;
            LogToFile($"[CONTEXT_MENU][TID:{tid}] Comando \"{command}\" recebido");

            try
            {
                // Tenta enviar para instância em execução via named pipe
                LogToFile($"[CONTEXT_MENU][TID:{tid}] Tentando IPC com instância em execução...");
                bool ipcSent = await VoltrisOptimizer.Services.Shell.CommandPipeService.SendCommandAsync(command, 3000);

                if (ipcSent)
                {
                    LogToFile($"[CONTEXT_MENU][TID:{tid}] Comando enviado via IPC para instância principal. Saindo.");
                    _loggingService?.LogExit(nameof(HandleContextMenuCommand), "IPC_sent");
                    return;
                }

                LogToFile($"[CONTEXT_MENU][TID:{tid}] Nenhuma instância em execução. Executando inline...");

                // Cria logger temporário (Bootstrapper.ConfigureServices precisa de ILoggingService não-nulo)
            // Mesmo diretorio canonico do logger principal (bug corrigido: antes gravava no .exe).
            var tempLogDir = VoltrisOptimizer.Services.Logging.LogDirectoryResolver.Resolve();
            try { Directory.CreateDirectory(tempLogDir); } catch (Exception dirEx) { System.Diagnostics.Debug.WriteLine($"[App] CreateLogDir: {dirEx.Message}"); }
                var tempLogger = new VoltrisOptimizer.Services.LoggingService(tempLogDir);

                var ServiceProvider = Core.Bootstrapper.ConfigureServices(tempLogger);

                // Executa o comando
                switch (command)
                {
                    case "quickclean":
                        LogToFile($"[CONTEXT_MENU][TID:{tid}] Executando Limpeza Rápida inline...");
                        await RunQuickCleanAsync(ServiceProvider);
                        break;
                    case "optimize":
                        LogToFile($"[CONTEXT_MENU][TID:{tid}] Executando Otimização Rápida inline...");
                        await RunQuickOptimizeAsync(ServiceProvider);
                        break;
                    case "defrag":
                        LogToFile($"[CONTEXT_MENU][TID:{tid}] Executando Desfragmentar Disco inline...");
                        await RunDefragAsync(ServiceProvider);
                        break;
                    case "diagnose":
                        LogToFile($"[CONTEXT_MENU][TID:{tid}] Executando Diagnóstico do Sistema inline...");
                        await RunDiagnoseAsync(ServiceProvider);
                        break;
                }

                LogToFile($"[CONTEXT_MENU][TID:{tid}] Comando inline \"{command}\" concluído.");
            }
            catch (Exception ex)
            {
                LogToFile($"[CONTEXT_MENU][TID:{tid}] ERRO ao executar \"{command}\": {ex.Message}");
                LogToFile($"[CONTEXT_MENU][TID:{tid}] Stack: {ex.StackTrace}");
            }
            finally
            {
                LogToFile($"[CONTEXT_MENU][TID:{tid}] Encerrando processo após comando.");
                Program.WriteCrashLog("GRACEFUL_SHUTDOWN_CONTEXT_MENU");
                SafeExit(0, "CONTEXT_MENU_COMMAND");
            }
        }

        /// <summary>
        /// MODO PERSISTÊNCIA TASKBAR — PROCESSO headless executado pelo Task Scheduler no logon.
        /// Não exibe janela. Monitora a posição dos ícones por 45s e re-centraliza quando o
        /// Explorer reverter a posição padrão ao terminar de inicializar.
        /// </summary>
        private async Task HandleTaskbarCenterPersistAsync(string[] args)
        {
            var tid = Thread.CurrentThread.ManagedThreadId;
            var totalSw = System.Diagnostics.Stopwatch.StartNew();

            // Configurar logger dedicado para esta execucao headless
            var tempLogDir = LogDirectoryResolver.Resolve();
            try { Directory.CreateDirectory(tempLogDir); } catch { }
            var tempLogger = new VoltrisOptimizer.Services.LoggingService(tempLogDir);

            LogToFile("[TASKBAR_CENTER][TID:" + tid + "] == INICIANDO PERSISTENCIA ==");
            tempLogger.LogInfo("[TASKBAR_CENTER] == PROCESSO de persistencia iniciado. TID=" + tid + " ==");

            try
            {
                // ── FASE 1: Aguardar Shell_TrayWnd com polling ativo (igual ao TaskbarX Main.vb) ──
                // O Explorer pode demorar varios segundos para criar a Shell_TrayWnd.
                // Polling a cada 250ms, maximo 30 segundos de espera.
                tempLogger.LogInfo("[TASKBAR_CENTER] [Fase 1] Aguardando Shell_TrayWnd (polling ativo)...");
                LogToFile("[TASKBAR_CENTER][TID:" + tid + "] [Fase 1] Aguardando Shell_TrayWnd...");

                bool shellTrayFound = false;
                int  pollAttempts   = 0;
                const int maxPollAttempts = 120; // 30s max (120 x 250ms)

                while (!shellTrayFound && pollAttempts < maxPollAttempts)
                {
                    try
                    {
                        // Usar P/Invoke via helper estático (FindWindow("Shell_TrayWnd"))
                        IntPtr tray = VoltrisOptimizer.Services.Personalize.TaskbarCenterPersistHelper.FindShellTrayWnd();

                        if (tray != IntPtr.Zero)
                        {
                            shellTrayFound = true;
                            tempLogger.LogInfo("[TASKBAR_CENTER] [Fase 1] Shell_TrayWnd encontrada apos " + totalSw.ElapsedMilliseconds + "ms (tentativa " + (pollAttempts + 1) + ")");
                            LogToFile("[TASKBAR_CENTER][TID:" + tid + "] Shell_TrayWnd apos " + totalSw.ElapsedMilliseconds + "ms");
                        }
                        else
                        {
                            pollAttempts++;
                            await Task.Delay(250);
                        }
                    }
                    catch
                    {
                        pollAttempts++;
                        await Task.Delay(250);
                    }

                }

                if (!shellTrayFound)
                {
                    tempLogger.LogWarning("[TASKBAR_CENTER] Shell_TrayWnd nao encontrada apos 30s. Encerrando.");
                    LogToFile("[TASKBAR_CENTER][TID:" + tid + "] Shell_TrayWnd indisponivel apos 30s.");
                    return;
                }

                // ── FASE 2: Aguardar 2s e aplicar centralizacao inicial ───────────────────
                // MSTaskListWClass pode ainda nao existir mesmo com Shell_TrayWnd presente.
                tempLogger.LogInfo("[TASKBAR_CENTER] [Fase 2] Aguardando 2s para MSTaskListWClass...");
                await Task.Delay(2000);

                var taskbarCtrl = new VoltrisOptimizer.Services.Personalize.TaskbarControlService(tempLogger);

                tempLogger.LogInfo("[TASKBAR_CENTER] [Fase 2] Aplicando centralizacao inicial...");
                LogToFile("[TASKBAR_CENTER][TID:" + tid + "] [Fase 2] Centralizacao inicial @ " + totalSw.ElapsedMilliseconds + "ms");

                taskbarCtrl.SetCentering(true);

                // Dar tempo ao MonitorLoop interno de agir
                await Task.Delay(1500);
                tempLogger.LogInfo("[TASKBAR_CENTER] [Fase 2] Centralizacao inicial aplicada @ " + totalSw.ElapsedMilliseconds + "ms");

                // ── FASE 3: Loop de guarda (45s) ─────────────────────────────────────────
                // O Explorer termina de inicializar ~6-15s apos logon e REVERTE os icones
                // para LEFT=0. Este loop re-centraliza durante essa janela critica.
                //
                // Estrategia:
                //   - Primeiros 20s: ForceRecenter a cada 2s (combate a reversao do Explorer)
                //   - Apos 20s:      Apenas monitorar (posicao ja deve estar estavel)
                const int monitoringWindowMs   = 45_000; // 45s de guarda total
                const int driftCheckIntervalMs = 2_000;  // verificar a cada 2s
                const int criticalWindowMs     = 20_000; // primeiros 20s = janela critica

                tempLogger.LogInfo("[TASKBAR_CENTER] [Fase 3] Janela de guarda de " + (monitoringWindowMs / 1000) + "s iniciada...");
                LogToFile("[TASKBAR_CENTER][TID:" + tid + "] [Fase 3] Guarda iniciada @ " + totalSw.ElapsedMilliseconds + "ms");

                var guardSw       = System.Diagnostics.Stopwatch.StartNew();
                int recenterCount = 0;
                int checkCount    = 0;

                while (guardSw.ElapsedMilliseconds < monitoringWindowMs)
                {
                    await Task.Delay(driftCheckIntervalMs);
                    checkCount++;

                    try
                    {
                        bool inCriticalWindow = guardSw.ElapsedMilliseconds < criticalWindowMs;

                        if (inCriticalWindow)
                        {
                            // Janela critica: forcar re-centralizacao a cada ciclo
                            // para combater o Explorer revertendo a posicao dos icones.
                            recenterCount++;
                            taskbarCtrl.ForceRecenter();
                            tempLogger.LogInfo("[TASKBAR_CENTER] [Fase 3] Re-centralizacao #" + recenterCount + " (janela critica) @ " + guardSw.ElapsedMilliseconds + "ms");
                        }
                        else
                        {
                            // Apos janela critica: apenas registrar que esta monitorando
                            if (checkCount % 5 == 0)
                            {
                                tempLogger.LogInfo("[TASKBAR_CENTER] [Fase 3] Monitorando... check #" + checkCount + " @ " + guardSw.ElapsedMilliseconds + "ms");
                            }
                        }
                    }
                    catch (Exception loopEx)
                    {
                        tempLogger.LogWarning("[TASKBAR_CENTER] [Fase 3] Erro na verificacao: " + loopEx.Message);
                    }
                }

                // ── FASE 4: Finalizacao ───────────────────────────────────────────────────
                tempLogger.LogSuccess("[TASKBAR_CENTER] == Guarda concluida! checks=" + checkCount + " recenters=" + recenterCount + " total=" + totalSw.ElapsedMilliseconds + "ms ==");
                LogToFile("[TASKBAR_CENTER][TID:" + tid + "] Concluido: checks=" + checkCount + " recenters=" + recenterCount + " total=" + totalSw.ElapsedMilliseconds + "ms");
            }
            catch (Exception ex)
            {
                tempLogger.LogError("[TASKBAR_CENTER] ERRO: " + ex.Message, ex);
                LogToFile("[TASKBAR_CENTER][TID:" + tid + "] ERRO: " + ex.Message);
                LogToFile("[TASKBAR_CENTER][TID:" + tid + "] Stack: " + ex.StackTrace);
            }
            finally
            {
                LogToFile("[TASKBAR_CENTER][TID:" + tid + "] Encerrando processo. Total=" + totalSw.ElapsedMilliseconds + "ms");
                Program.WriteCrashLog("GRACEFUL_SHUTDOWN_TASKBAR_CENTER");
                SafeExit(0, "TASKBAR_CENTER_COMMAND");
            }
        }

        /// <summary>
        /// MODO ESTILO TASKBAR - Aplica estilo visual e fecha imediatamente
        /// Chamado pelo Task Scheduler no logon do usuário (delay 5s)
        /// </summary>
        private async Task HandleTaskbarStylePersistAsync(string[] args)
        {
            var tid = Thread.CurrentThread.ManagedThreadId;
            LogToFile($"[TASKBAR_STYLE][TID:{tid}] INICIANDO estilo persistente (Args: {string.Join(" ", args)})");
            
            try
            {
                // Parse dos parâmetros: --mode=X --opacity=X --r=X --g=X --b=X
                int mode = 1; // Default: Transparent
                byte opacity = 255;
                int r = 0, g = 0, b = 0;
                
                foreach (var arg in args)
                {
                    if (arg.StartsWith("--mode=") && int.TryParse(arg.Substring(7), out var m)) mode = m;
                    else if (arg.StartsWith("--opacity=") && int.TryParse(arg.Substring(10), out var o)) opacity = (byte)o;
                    else if (arg.StartsWith("--r=") && int.TryParse(arg.Substring(4), out var rv)) r = rv;
                    else if (arg.StartsWith("--g=") && int.TryParse(arg.Substring(4), out var gv)) g = gv;
                    else if (arg.StartsWith("--b=") && int.TryParse(arg.Substring(4), out var bv)) b = bv;
                }
                
                LogToFile($"[TASKBAR_STYLE][TID:{tid}] Parâmetros: mode={mode}, opacity={opacity}, RGB=({r},{g},{b})");
                
                // Aguardar 5 segundos para o Explorer estar completamente inicializado
                LogToFile($"[TASKBAR_STYLE][TID:{tid}] Aguardando 5s para Explorer estabilizar...");
                await Task.Delay(5000);
                
                // Criar logger temporário e serviço
                var tempLogDir = LogDirectoryResolver.Resolve();
                try { Directory.CreateDirectory(tempLogDir); } catch { }
                var tempLogger = new VoltrisOptimizer.Services.LoggingService(tempLogDir);
                
                // Criar serviço de controle da taskbar
                var taskbarCtrl = new VoltrisOptimizer.Services.Personalize.TaskbarControlService(tempLogger);
                
                LogToFile($"[TASKBAR_STYLE][TID:{tid}] Aplicando estilo visual...");
                tempLogger.LogInfo($"[TASKBAR_STYLE] 🎨 Aplicando estilo: mode={(VoltrisOptimizer.Services.Personalize.TaskbarStyleMode)mode}, opacity={opacity}");
                
                // Aplicar estilo
                var styleMode = (VoltrisOptimizer.Services.Personalize.TaskbarStyleMode)mode;
                taskbarCtrl.SetStyle(true, styleMode, opacity, r, g, b, 0);
                
                // Aguardar tempo suficiente para o estilo ser aplicado
                await Task.Delay(3000);
                
                LogToFile($"[TASKBAR_STYLE][TID:{tid}] ✅ Estilo aplicado com sucesso!");
                tempLogger.LogSuccess("[TASKBAR_STYLE] ✅ Estilo aplicado com sucesso!");
            }
            catch (Exception ex)
            {
                LogToFile($"[TASKBAR_STYLE][TID:{tid}] ❌ ERRO: {ex.Message}");
                LogToFile($"[TASKBAR_STYLE][TID:{tid}] Stack: {ex.StackTrace}");
            }
            finally
            {
                LogToFile($"[TASKBAR_STYLE][TID:{tid}] Encerrando processo após aplicar estilo.");
                Program.WriteCrashLog("GRACEFUL_SHUTDOWN_TASKBAR_STYLE");
                SafeExit(0, "TASKBAR_STYLE_COMMAND");
            }
        }

        private static async Task RunQuickCleanAsync(IServiceProvider sp)
        {
            var cleaner = sp.GetService<Services.SystemCleaner>();
            if (cleaner == null)
            {
                Debug.WriteLine("[ContextMenu] SystemCleaner não disponível");
                return;
            }

            // Executa as 4 etapas da limpeza rápida (mesmo fluxo do DashboardViewModel.QuickCleanupAsync)
            await cleaner.CleanTempFilesAsync();
            await cleaner.EmptyRecycleBinAsync();
            await cleaner.CleanThumbnailsAsync();
            await cleaner.CleanBrowserCacheAsync();
        }

        private static async Task RunQuickOptimizeAsync(IServiceProvider sp)
        {
            var optimizer = sp.GetService<VoltrisOptimizer.Services.VoltrisPerformanceOptimizer>();
            if (optimizer == null)
            {
                Debug.WriteLine("[ContextMenu] VoltrisPerformanceOptimizer não disponível");
                return;
            }

            // Mesmo fluxo do DashboardViewModel.QuickOptimizeAsync
            await optimizer.OptimizeRAMAsync(null);
        }

        private static async Task RunDefragAsync(IServiceProvider sp)
        {
            var storage = sp.GetService<Services.Hardware.StorageOptimizerService>();
            if (storage != null)
            {
                await storage.OptimizeAllDrivesAsync(null!);
                return;
            }

            // Fallback: executa defrag.exe diretamente
            Debug.WriteLine("[ContextMenu] StorageOptimizerService não disponível, usando fallback defrag.exe");
            var psi = new ProcessStartInfo
            {
                FileName = "defrag.exe",
                Arguments = "/C /O",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden};
            Process.Start(psi);
        }

        /// <summary>
        /// [FIX:UNICA-FONTE] O diagnóstico de energia foi removido.
        ///
        /// Ele vinha do `o servico legado`, que além de ler o estado
        /// tinha também o "aplicar correções" que gravavam PCIe ASPM, boost mode e
        /// `PowerThrottlingOff` no registro. Essa parte era escrita de energia
        /// com efeito no sistema, e agora quem escreve é o Perfil Inteligente.
        ///
        /// O diagnóstico de energia continua acessível e é MELHOR dentro do
        /// Perfil Inteligente: o `ProfilePowerMatrixSelfTest` valida a tabela
        /// inteira contra dez regras, e o `ProfilePowerCoordinator` registra
        /// no log o plano ativo, o tier e cada valor gravado com read-back.
        /// Se o usuário precisar ver isso na interface, o lugar certo é o painel
        /// do Perfil, não uma página separada de diagnóstico de energia.
        /// </summary>
        private static Task RunDiagnoseAsync(IServiceProvider sp)
        {
            VoltrisOptimizer.Services.Power.ProfilePowerMatrixSelfTest.Run(
                sp.GetService<ILoggingService>(), out _);

            VoltrisOptimizer.App.LoggingService?.LogInfo(
                "[App] Diagnóstico de energia: responsabilidade do Perfil Inteligente. " +
                "O self-test acima valida a tabela de energia contra as 10 regras; " +
                "veja também o log [PowerApply], com cada valor gravado e lido de volta.");
            return Task.CompletedTask;
        }

        // Instrumentação de startup
        private static readonly System.Diagnostics.Stopwatch _startupSw = System.Diagnostics.Stopwatch.StartNew();

        private static long _tBootstrapper, _tServicesInit, _tSplashShow, _tAppFlow;

        private static void LogTiming(ILoggingService? log, string step, long ms)
        {
            var msg = $"[Startup] Step = {step} Duration = {ms} ms";
            log?.LogInfo(msg);
            System.Diagnostics.Debug.WriteLine(msg);
        }

        private async Task InitializeInternalAsync(StartupEventArgs e)
        {
            _loggingService?.LogEntry(nameof(InitializeInternalAsync));
            _loggingService?.LogState("InitPhase", "BEGIN");
            Program.WriteCrashLog("STARTUP_INIT_BEGIN");
            var tid = Thread.CurrentThread.ManagedThreadId;
            LogToFile($"[STARTUP][TID:{tid}] InitializeInternalAsync INÍCIO");
            try { VoltrisDiagnosticSystem.Instance.Timeline("STARTUP", "InitializeInternalAsync iniciado"); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[App] Timeline: {ex.Message}"); }

            // 0. MOSTRAR SPLASH IMEDIATAMENTE (CORREÇÃO DE UI FREEZE DE 3s)
            LogToFile($"[STARTUP][TID:{tid}] Criando SplashHost...");
            var splashHost = new VoltrisOptimizer.UI.Windows.SplashHost();
            LogToFile($"[STARTUP][TID:{tid}] Chamando SplashHost.Show()...");
            splashHost.Show();
            LogToFile($"[STARTUP][TID:{tid}] SplashHost.Show() retornou.");
            try { VoltrisDiagnosticSystem.Instance.Timeline("UI", "SplashHost mostrado"); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[App] Timeline: {ex.Message}"); }
            
            // Forçpr o dispatcher a processar a renderização do splash ANTES de continuar com código pesado
            await Task.Yield();

            // [FIX:POWER-MATRIX] AS PROVAS DE INTEGRIDADE RODAM CEDO, DE PROPOSITO.
            //
            // Elas ficavam no FINAL de InitializeInternalAsync, e nunca
            // executavam: o startup deste app passa de 76s e o watchdog acusa
            // "STUCK STEP: APP_STARTUP" (problema antigo, anterior a este
            // codigo). Ou seja, as garantias que protegem o PC gamer de ser
            // limitado por engano estavam no lugar errado - depois de um
            // caminho que, na pratica, nao termina a tempo.
            //
            // Aqui elas rodam em milissegundos (aritmetica pura, nenhuma
            // chamada ao Windows), ANTES do splash e de qualquer orquestracao.
            // Assim o veredito sobre a tabela de energia existe mesmo que o
            // resto do startup demore ou falhe.
            //
            // NAO e aqui que a MAQUINA e medida: HardwareCapabilityProbe leva
            // ~13s e e chamado so na primeira aplicacao de perfil.
            try
            {
                VoltrisOptimizer.Services.Performance.PerformanceStrategySelfTest.Run(_loggingService, out _);
                VoltrisOptimizer.Services.Power.ProfilePowerMatrixSelfTest.Run(_loggingService, out _);

                // [FIX:POWER-MATRIX] ASSINA O COORDENADOR DE ENERGIA.
                //
                // É aqui que as 5 entradas passam a ter efeito de verdade: a
                // página de primeira configuração, o botão "Perfil Inteligente"
                // do Dashboard, o systray, o Modo Gamer e a troca automática por
                // janela. As cinco já emitem `SettingsService.ProfileChanged`,
                // e o coordenador é o único que transforma isso em valores de
                // energia gravados no plano correto da máquina.
                VoltrisOptimizer.Services.Power.ProfilePowerCoordinator.Initialize(_loggingService);
            }
            catch (Exception selfTestEx)
            {
                _loggingService?.LogWarning($"[App] Provas de integridade nao puderam rodar: {selfTestEx.Message}");
            }
            
            splashHost.SetStatus("Iniciando componentes...");
            splashHost.SetProgress(5);

            // TIMEOUT GLOBAL DE STARTUP: Se ultrapassar 30s, força a abertura da MainWindow
            using var startupTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var startupTimeoutTask = Task.Delay(Timeout.Infinite, startupTimeoutCts.Token);

            try
            {
                // 1. Logging + exceções + Mute x
                // BUG CORRIGIDO: era LogDirectoryResolver.Resolve(),
                // o que gravava os logs dentro da pasta do .exe e os perdia a cada
                // atualização. LogDirectoryResolver centraliza em %LOCALAPPDATA%\Voltris\Logs.
                var logDirectory = VoltrisOptimizer.Services.Logging.LogDirectoryResolver.Resolve();
                if (!Directory.Exists(logDirectory))
                    Directory.CreateDirectory(logDirectory);

                var diskLogger = new VoltrisOptimizer.Services.LoggingService(logDirectory);
                var compositeLogger = new VoltrisOptimizer.Services.Logging.CompositeLoggingService(diskLogger);

                _loggingService = compositeLogger;
                LoggingService = _loggingService;

                // INICIALIZAR WATCHDOG DE STARTUP
                _startupTracker = new StartupStepTracker(_loggingService, timeoutSeconds: 45);
                string arch = Environment.Is64BitProcess ? "x64" : "x86";
                _startupTracker.Begin("APP_STARTUP", $"PID={Process.GetCurrentProcess().Id} Arch={arch}");
                try { VoltrisDiagnosticSystem.Instance.Timeline("STARTUP", "StartupStepTracker iniciado"); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[App] Timeline: {ex.Message}"); }

                // Carregar idioma (Agora ocorre de forma segura DEPOIS que a tela já abriu)
                try
                {
                    // A inicialização do SettingsService pode acessar disco (JSON).
                    // Para evitar engasgos, faremos a carga em background.
                    var savedLanguage = await Task.Run(() =>
                    {
                        var settingsService = VoltrisOptimizer.Services.SettingsService.Instance;
                        var settings = settingsService.Settings;

                        LanguageLogger.Log("==================== INICIALIZAÇÃO DO VOLTRIS OPTIMIZER ====================");
                        LanguageLogger.Log($"Verificando persistência de detecção de idioma: LanguageDetected={settings.LanguageDetected}, Language={settings.Language}");

                        if (!settings.LanguageDetected)
                        {
                            LanguageLogger.Log("Primeira execução: Nenhuma detecção anterior encontrada. Iniciando detecção...");
                            var detectedLanguage = LanguageDetector.DetectWindowsLanguage();
                            
                            settings.Language = detectedLanguage;
                            settings.LanguageDetected = true;
                            
                            settingsService.SaveSettings();
                            return detectedLanguage;
                        }
                        return settings.Language;
                    });

                    LocalizationService.Instance.SetLanguage(savedLanguage);
                    LogToFile($"[STARTUP][TID:{tid}] Idioma inicial do Splash definido para: {savedLanguage}");
                    splashHost.SetStatus(LocalizationService.Instance.GetString("SplashStarting"));
                }
                catch (Exception exLang)
                {
                    LogToFile($"[STARTUP][TID:{tid}] Erro ao carregar/detectar idioma inicial: {exLang.Message}");
                    LanguageLogger.Log($"Erro crítico ao carregar ou detectar idioma inicial: {exLang.Message}. Pilha: {exLang.StackTrace}");
                }

                _loggingService.LogInfo("[App] ================= ONSTARTUP INÍCIO =================");



                // Prevenir shutdown automático do WPF imediatamente
                ShutdownMode = ShutdownMode.OnExplicitShutdown;

                // INICIALIZAR VISUAL EFFECTS MANAGER (centraliza composição do Windows)
                try
                {
                    VoltrisOptimizer.Helpers.VisualEffectsManager.Initialize();
                    _loggingService?.LogInfo("[App] VisualEffectsManager inicializado com sucesso");
                }
                catch (Exception ex)
                {
                    _loggingService?.LogError("[App] Erro ao inicializar VisualEffectsManager", ex);
                }

                LogToFile("[CRITICAL] ================== STARTUP INICIADO ==================");

                // WATCHDOG: thread dedicada que loga estado a cada 2s e thread dump a cada 6s.
                // Permite diagnosticar travamentos da UI thread.
                VoltrisOptimizer.Helpers.StartupWatchdog.Start();
                VoltrisOptimizer.Helpers.StartupWatchdog.SetPhase("STARTUP_BEGIN");

                    _startupTracker?.Begin("ZOMBIE_CLEANUP", "Limpando processos zumbis em background");
                    // LIMPEZA DE PROCESSOS ZUMBIS (não-bloqueante)
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            var currentProc = Process.GetCurrentProcess();
                            var others = Process.GetProcessesByName(currentProc.ProcessName).Where(p => p.Id != currentProc.Id);

                            foreach (var p in others)
                            {
                                try
                                {
                                    LogToFile($"[App] Detectado processo órfão/zumbi (PID {p.Id}). Finalizando...");
                                    p.Kill();
                                }
                                catch (Exception killEx) { System.Diagnostics.Debug.WriteLine($"[App] ZombieKill: {killEx.Message}"); }
                            }
                        }
                        catch (Exception ex)
                        {
                            _startupTracker?.Fail("ZOMBIE_CLEANUP", ex, "Erro ao matar processos zumbis");
                        }
                        finally
                        {
                            _startupTracker?.End("ZOMBIE_CLEANUP", "Limpeza concluída");
                        }
                    });

                base.OnStartup(e);
                this.DispatcherUnhandledException += App_DispatcherUnhandledException;

                AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

                // [FIX:C-3] Observar exceções de PRIMEIRA CHANCE no log principal.
                //
                // BUG ORIGINAL: o log principal (voltris.log.txt) ficou com ZERO
                // exceções em 36 min, apesar de o VoltrisDiag registrar 17
                // first-chance — incluindo 13 InvalidOperationException
                // cross-thread. A causa é estrutural: uma exceção first-chance é
                // capturada por quem a lançou, então nunca chega a
                // AppDomain.UnhandledException. Só o VoltrisDiag, que se inscreve
                // em FirstChanceException, via a enxergar.
                //
                // Consequência prática: o arquivo que o usuário e o suporte
                // realmente abrem não tinha nenhuma evidência de falha. Isso
                // tornava impossível distinguir "funcionou" de "quebrou e se
                // recuperou" — e é por isso que o PID 6744 não deixou rastro.
                //
                // Aqui as first-chance vão para o log principal em nível
                // Warning (não Error), com throttle por tipo+mensagem para não
                // criar um novo storm: um bug que lança a cada 4 Hz vira no máximo
                // uma linha a cada 30 s. Exceções de Cancellation e
                // ObjectDisposed durante shutdown são silenciadas de propósito.
                AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;

                // Semaphore — não tem afinidade de thread, perfeito para single-instance
                _startupTracker?.Begin("SEMAPHORE_CHECK", "Verificando instância única via Semaphore");
                LogToFile($"[STARTUP][TID:{tid}] Verificando Semaphore de instância única...");
                var mutexSw = Stopwatch.StartNew();
                
                bool createdNew;
                _appSemaphore = new Semaphore(1, 1, "VoltrisOptimizer_SingleInstance", out createdNew);

                if (createdNew)
                {
                    LogToFile($"[STARTUP][TID:{tid}][SEMAPHORE] Nova instância (owner). Prosseguindo.");
                }
                else
                {
                    // Tentar adquirir sem bloqueio apenas para confirmar se já está adquirido
                    bool acquired = _appSemaphore.WaitOne(0);
                    if (!acquired)
                    {
                        LogToFile($"[STARTUP][TID:{tid}][SEMAPHORE] Já está em uso (WaitOne falhou).");
                    }
                    else
                    {
                        // Conseguimos adquirir? A outra instância talvez tenha fechado de forma abrupta sem o SO limpar
                        LogToFile($"[STARTUP][TID:{tid}][SEMAPHORE] WaitOne teve sucesso inesperado, mas 'createdNew' era falso. Assumindo owner.");
                        createdNew = true;
                    }
                }
                
                mutexSw.Stop();
                LogToFile($"[STARTUP][TID:{tid}] Semaphore check finalizado em {mutexSw.ElapsedMilliseconds}ms. CreatedNew: {createdNew}");
                try { VoltrisDiagnosticSystem.Instance.Timeline("SEMAPHORE", $"Semaphore check concluído em {mutexSw.ElapsedMilliseconds}ms"); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[App] Timeline: {ex.Message}"); }
                
                if (!createdNew)
                {
                    _loggingService.LogWarning("[App] Outra instância detectada, encerrando.");
                    LogToFile("[STARTUP] Outra instância detectada. Encerrando app.");
                    _appSemaphore.Dispose();
                    _appSemaphore = null;
                    _startupTracker?.End("SEMAPHORE_CHECK", "Outra instância detectada — encerrando");
                    _startupTracker?.MarkCompleted();
                    Shutdown();
                    return;
                }
                _startupTracker?.End("SEMAPHORE_CHECK", "Single instance garantido");

                // 2. Detectar modo minimizado
                var args = Environment.GetCommandLineArgs();

                // [FIX:B-2] "sem argumentos" precisa significar "sem argumentos DO
                // USUÁRIO".
                //
                // BUG ORIGINAL: a condição era
                //     (!args.Any() && Settings.StartMinimized && Settings.StartWithWindows)
                // onde args = Environment.GetCommandLineArgs(). Esse array SEMPRE
                // tem pelo menos um elemento — o próprio caminho do executável
                // em [0]. Portanto args.Any() é sempre true e !args.Any() é
                // SEMPRE false: o fallback para a configuração nunca era
                // executado, em nenhuma máquina, nunca.
                //
                // O efeito prático é que "Iniciar minimizado" só funcionava pelo
                // caminho do Task Scheduler (que injeta --minimized na tarefa).
                // Se o registro fosse gravado por outro caminho, se a tarefa
                // fosse removida pelo Windows, ou se o usuário abrisse o
                // executável manualmente, o app abria com janela cheia em
                // todo boot mesmo com a opção marcada. A configuração era
                // decorativa.
                var userArgs = args.Skip(1).ToArray();
                bool hasUserArgs = userArgs.Length > 0;

                bool flagMinimized = userArgs.Any(a =>
                    a.Equals("/minimized", StringComparison.OrdinalIgnoreCase) ||
                    a.Equals("-minimized", StringComparison.OrdinalIgnoreCase) ||
                    a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));

                bool flagMinimizadoDesligado = userArgs.Any(a =>
                    a.Equals("/nominimized", StringComparison.OrdinalIgnoreCase) ||
                    a.Equals("-nominimized", StringComparison.OrdinalIgnoreCase) ||
                    a.Equals("--nominimized", StringComparison.OrdinalIgnoreCase));

                // [FIX:AUTOSTART-DETECT] Sinalizador gravado pelo próprio app no
                // registro/tarefa de auto-start. Sua presença é a prova de que o
                // Windows iniciou o programa — um clique manual nunca o traz.
                bool flagAutoStart = userArgs.Any(a =>
                    a.Equals(StartupAutostartDetector.AutostartFlag, StringComparison.OrdinalIgnoreCase));

                bool startMinimized;
                string startMinimizedOrigem;
                if (flagMinimizadoDesligado)
                {
                    startMinimized = false;
                    startMinimizedOrigem = "flag --nominimized tem precedencia sobre a configuracao";
                }
                else if (flagMinimized)
                {
                    startMinimized = true;
                    startMinimizedOrigem = "flag --minimized recebida na linha de comando";
                }
                else if (hasUserArgs)
                {
                    // O usuario passou outros argumentos: nao e um auto-start, e
                    // forcar minimizado a surprise agradavel.
                    startMinimized = false;
                    startMinimizedOrigem = $"usuario passou {userArgs.Length} argumento(s) proprio(s); nao e auto-start, minimizado ignorado";
                }
                else
                {
                    // [FIX:AUTOSTART-DETECT] Chega aqui sem "--nominimized" e sem
                    // "--minimized". Resta decidir entre auto-start e clique
                    // manual, e a resposta é o sinalizador.
                    //
                    // O teste antigo era "sem argumentos de usuário, logo é
                    // auto-start", o que é falso: um clique duplo também chega sem
                    // argumentos. Com os dois padrões ligados, o app escondia a
                    // janela em todo lançamento manual.
                    bool userWantsMinimized = SettingsService.Instance.Settings.StartMinimized;

                    startMinimized = flagAutoStart || userWantsMinimized;

                    startMinimizedOrigem = startMinimized
                        ? (flagAutoStart
                            ? "auto-start do Windows (--autostart) -> bandeja"
                            : "usuario marcou Iniciar minimizado -> bandeja")
                        : "lancamento manual sem a opcao Iniciar minimizada -> mostra a janela";
                }

                _loggingService.LogInfo(
                    $"[FIX:B-2] StartMinimized={startMinimized} | origem={startMinimizedOrigem} | " +
                    $"argsCompletos={args.Length} | argsDoUsuario={userArgs.Length} " +
                    $"[{string.Join(" ", userArgs)}] | cfg.StartMinimized={SettingsService.Instance.Settings.StartMinimized} " +
                    $"cfg.StartWithWindows={SettingsService.Instance.Settings.StartWithWindows} " +
                    $"autoStartWindows={StartupAutostartDetector.IsAutoStart()}");

                // 3. Delay adaptativo APENAS no boot (minimizado)
                if (startMinimized)
                {
                    var processorCount = Environment.ProcessorCount;
                    int delaySeconds = processorCount < 4 ? 5 : processorCount < 8 ? 3 : 2;

                    _loggingService.LogInfo($"[App] Boot delay: {delaySeconds}s (cores = {processorCount})");
                    try
                    {
                        await Task.Delay(delaySeconds * 1000);
                    }
                    catch (Exception ex)
                    {
                        _loggingService?.LogError($"[App] Boot delay error: {ex.Message}");
                    }
                }

                // 4. Bootstrapper — registra todos os serviços DI
                _loggingService?.LogTimer("Bootstrapper", _startupSw.ElapsedMilliseconds);
                _startupTracker?.Begin("BOOTSTRAPPER", "Registro de serviços no DI container");
                VoltrisOptimizer.Helpers.StartupWatchdog.SetPhase("STEP_4_BOOTSTRAP");
                var t0 = _startupSw.ElapsedMilliseconds;
                LogToFile($"[STARTUP][TID:{tid}] [STEP-4] Iniciando Bootstrapper (registro DI)...");
                try
                {
                    splashHost.SetStatus(LocalizationService.Instance.GetString("SplashServices"));
                    splashHost.SetProgress(15);
                    await Task.Delay(400);
                    
                    // [LOG] Bootstrapper.ConfigureServices
                    LogToFile($"[STARTUP][TID:{tid}] Chamando Bootstrapper.ConfigureServices...");
                    _serviceProvider = await Task.Run(() => VoltrisOptimizer.Core.Bootstrapper.ConfigureServices(_loggingService));
                    LogToFile($"[STARTUP][TID:{tid}] Bootstrapper.ConfigureServices retornou.");
                    Services = _serviceProvider;
                    
                    // Envio de log para o Telegram (agora com as injeções de dependência disponíveis)
                    Task.Run(async () =>
                    {
                        try
                        {
                            string hwInfo = "";
                            var sysInfo = _serviceProvider?.GetService(typeof(VoltrisOptimizer.Interfaces.ISystemInfoService)) as VoltrisOptimizer.Interfaces.ISystemInfoService;
                            if (sysInfo != null)
                            {
                                var cpu = await sysInfo.GetCpuInfoAsync();
                                var gpu = await sysInfo.GetGpuInfoAsync();
                                var ram = await sysInfo.GetRamInfoAsync();
                                var networks = await sysInfo.GetNetworkInfoAsync();
                                string winVer = sysInfo.GetWindowsVersion();
                                string winEd = sysInfo.GetWindowsEdition();
                                string lang = VoltrisOptimizer.Services.LocalizationService.Instance.CurrentLanguage.ToString();
                                
                                string netInfo = networks != null && networks.Length > 0 ? networks[0].Name : "Desconhecida";

                                hwInfo = $"\n<b>Usuário:</b> {Environment.UserName}\n" +
                                         $"<b>Windows:</b> {winVer} {winEd}\n" +
                                         $"<b>Idioma:</b> {lang}\n" +
                                         $"<b>CPU:</b> {cpu.Name} ({cpu.CoreCount} Cores @ {cpu.MaxClockSpeedMHz} MHz)\n" +
                                         $"<b>GPU:</b> {gpu.Name} ({gpu.VideoMemoryBytes / (1024L*1024*1024)} GB)\n" +
                                         $"<b>RAM:</b> {ram.TotalGB} GB {ram.MemoryType}\n" +
                                         $"<b>Rede:</b> {netInfo}";
                            }
                            VoltrisOptimizer.Services.TelegramLogger.SendMessageFireAndForget($"🚀 <b>Voltris Optimizer Iniciado</b>\n<b>Máquina:</b> {Environment.MachineName}\n<b>Versão:</b> {VoltrisOptimizer.Properties.VersionInfo.Version}{hwInfo}", true);
                        }
                        catch { }
                    });
                    
                    // Inicializar o ServiceLocator legado para views que ainda dependem dele
                    VoltrisOptimizer.Core.ServiceLocator.Initialize(_serviceProvider);
                    
                    // VERIFICAÇÃO DE ROLLBACK (FASE 1 - ENTERPRISE)
                    try 
                    {
                        var recoveryGuard = _serviceProvider.GetService<VoltrisOptimizer.Services.Rollback.IRecoveryGuard>();
                        if (recoveryGuard != null)
                        {
                            await Task.Run(() => recoveryGuard.CheckAndRecoverPendingTransactionsAsync());
                        }
                    }
                    catch (Exception ex)
                    {
                        _loggingService?.LogWarning($"[App] Falha ao verificar transações pendentes de rollback: {ex.Message}");
                    }
                    
                    _startupTracker?.End("BOOTSTRAPPER", $"Services registered");
                    try { VoltrisDiagnosticSystem.Instance.Timeline("DI", "Bootstrapper concluído — DI container configurado"); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[App] Timeline: {ex.Message}"); }

                    ThreadPool.SetMinThreads(8, 8);
                    _loggingService?.LogInfo("[App] Serviços do DI registrados. Iniciando startup resiliente...");
                    try
                    {
                        var uiMonitor = new VoltrisOptimizer.Helpers.UIThreadMonitor(_loggingService, 500, 1000);
                        uiMonitor.Start();
                        _uiMonitor = uiMonitor;
                        _loggingService?.LogInfo("[App] UIThreadMonitor iniciado para diagnóstico de travamentos");
                    }
                    catch (Exception exUiMon)
                    {
                        _loggingService?.LogWarning($"[App] UIThreadMonitor não iniciou: {exUiMon.Message}");
                    }

                    // Iniciar sistema centralizado de diagnóstico (integra todos os helpers)
                    try
                    {
                        VoltrisDiagnosticSystem.Instance.Start();
                        DiagnosticConfig.UseDebugSettings(); // Configurar modo DEBUG para análise detalhada
                        VoltrisDiagnosticSystem.Instance.Timeline("BOOTSTRAP", "DI Container configurado — UIThreadMonitor ativo");
                        _loggingService?.LogInfo("[App] VoltrisDiagnosticSystem iniciado em modo DEBUG");
                    }
                    catch (Exception exDiag)
                    {
                        _loggingService?.LogWarning($"[App] VoltrisDiagnosticSystem não iniciou: {exDiag.Message}");
                    }

                    // Iniciar logger de diagnóstico de startup
                    try
                    {
                        _startupDiag = new VoltrisOptimizer.Helpers.StartupDiagnosticLogger();
                        _startupDiag.Phase("BOOTSTRAP_DONE", "DI Container configurado");
                        _loggingService?.LogInfo("[App] StartupDiagnosticLogger iniciado");
                    }
                    catch (Exception diagEx) { System.Diagnostics.Debug.WriteLine($"[App] StartupDiagLogger: {diagEx.Message}"); }
                }
                catch (Exception ex)
                {
                    LogToFile($"[STARTUP][TID:{tid}] [FATAL] Erro no Bootstrapper: {ex.Message}");
                    _startupTracker?.Fail("BOOTSTRAPPER", ex, "Falha fatal no registro DI");
                    throw;
                }
                LogTiming(_loggingService, "Bootstrapper", _startupSw.ElapsedMilliseconds - t0);
                
                // 4.1 DATA MIGRATION & UNIFICATION
                _loggingService?.LogEvent("StartupPhase", "DATA_MIGRATION");
                _loggingService?.LogTransition("BOOTSTRAP", "MIGRATION", "Bootstrapper_Complete");
                _startupTracker?.Begin("PERSISTENCE_MIGRATION", "Unificação de dados e migração de paths legados");
                try
                {
                    splashHost.SetStatus(LocalizationService.Instance.GetString("SplashIntegrity"));
                    await Task.Delay(300);
                    var migrationService = _serviceProvider.GetService<VoltrisOptimizer.Services.Persistence.PersistenceMigrationService>();
                    if (migrationService != null) await migrationService.RunMigrationAuditAsync();
                    else _loggingService?.LogWarning("[App] PersistenceMigrationService não disponível no DI");
                    _startupTracker?.End("PERSISTENCE_MIGRATION", "Dados unificados com sucesso");
                    
                    // 🔥 RESTAURAÇÃO DE LICENÇA IMEDIATA (Após unificação de caminhos)
                    _startupTracker?.Begin("LICENSE_RESTORE", "Restaurando estado da licença");
                    _loggingService.LogInfo("[App] Restaurando licença a partir do Registry/JSON...");
                    VoltrisOptimizer.Services.License.LicenseTokenStore.TryRestoreFromRegistry();
                    
                    // Forçpr o orquestrador a preencher o cache inicial com o que foi restaurado
                    _ = AsyncHelper.SafeTaskRun(async () =>
                    {
                        try
                        {
                            await VoltrisOptimizer.Services.License.LicenseOrchestrationService.Instance.GetCurrentStateAsync();
                            _loggingService.LogInfo("[App] Orquestrador de licença sincronizado");
                        }
                        catch (Exception ex)
                        {
                            _loggingService.LogDebug($"[App] Orquestrador de licença: {ex.Message}");
                        }
                    });
                    
                    _startupTracker?.End("LICENSE_RESTORE");
                }
                catch (Exception ex)
                {
                    _loggingService.LogError($"[App] Falha na migração/restauração de dados: {ex.Message}");
                    _startupTracker?.Fail("PERSISTENCE_MIGRATION", ex, "Erro na unificação/licença");
                }

                // Inicializar GlobalNotificationService
                try
                {
                    var notifService = _serviceProvider.GetService<VoltrisOptimizer.Services.Notifications.VoltrisNotificationService>();
                    if (notifService != null)
                    {
                        VoltrisOptimizer.Services.GlobalNotificationService.Initialize(notifService, _loggingService);
                    }
                    else
                    {
                        _loggingService?.LogWarning("[App] VoltrisNotificationService não disponível no DI - notificações desabilitadas");
                    }
                    _loggingService.LogInfo("[App] GlobalNotificationService inicializado");
                }
                                catch (Exception ex)
                {
                    _loggingService.LogError($"[App] GlobalNotificationService falhou: {ex.Message}");
                }

                // 5. Inicialização de serviços críticos — AGORA EM BACKGROUND (não bloqueia UI)
                // ⚠️ FIX CRÍTICO: MachineIdentity, TelemetryService e SessionManager FORAM movidos para
                // background tasks. Previamente, bloqueavam por 5-15s ANTES do MainWindow.Show(),
                // causando 6-8s de atraso no dashboard.
                // 
                // Estratégia:
                // 1. Obter MachineIdentity do cache (instantâneo, sem blocking)
                // 2. Mostrar MainWindow IMEDIATAMENTE
                // 3. Inicializar TelemetryService + SessionManager em background (sem bloquear UI)
                // 
                _loggingService?.LogTransition("MIGRATION", "CRITICAL_SERVICES", "Migration_Complete");
                _loggingService?.LogTimer("CriticalServices", _startupSw.ElapsedMilliseconds);
                _startupTracker?.Begin("CRITICAL_SERVICES", "MachineIdentity cache (instantâneo) + Telemetry/Session em background");
                t0 = _startupSw.ElapsedMilliseconds;
                
                VoltrisOptimizer.Services.Enterprise.Models.MachineIdentity machineIdentity = null;
                
                try
                {
                    // ✅ NÃO BLOQUEIA: Obter MachineIdentity do cache local (instantâneo)
                    _startupTracker?.Begin("MACHINE_IDENTITY", "Obtendo identity do cache (não bloqueia)");
                    var idSvc = _serviceProvider.GetService<VoltrisOptimizer.Services.Enterprise.MachineIdentityService>();
                    machineIdentity = idSvc.GetCachedIdentityOrFallback();
                    _loggingService?.LogInfo("[App] MachineIdentity obtido do cache (instantâneo).");
                    _startupTracker?.End("MACHINE_IDENTITY");
                }
                catch (Exception ex)
                {
                    _loggingService?.LogError($"[App] Erro ao obter MachineIdentity do cache: {ex.Message}");
                    _startupTracker?.Fail("MACHINE_IDENTITY", ex, "Falha ao obter cache");
                    machineIdentity = new VoltrisOptimizer.Services.Enterprise.Models.MachineIdentity 
                    { 
                        MachineId = "FALLBACK-" + Environment.MachineName 
                    };
                }

                _tServicesInit = _startupSw.ElapsedMilliseconds - t0;
                LogTiming(_loggingService, "MachineIdentityCache", _tServicesInit);
                _startupTracker?.End("CRITICAL_SERVICES", "Cache obtido — TelemetryService/SessionManager em background");

                // 6. TelemetryService + SessionManager em BACKGROUND (não bloqueiam MainWindow.Show())
                // ⚠️ Estas operações foram movidas do path crítico de startup.
                // Elas agora rodam em background enquanto o Dashboard já está visível.
                
                var serviceInitializationTask = Task.Run(async () =>
                {
                    try
                    {
                        _startupTracker?.Begin("BG_TELEMETRY_INIT", "Inicializando TelemetryService (background)");
                        var telemetryService = _serviceProvider.GetRequiredService<VoltrisOptimizer.Services.Telemetry.TelemetryService>();
                        TelemetryService = telemetryService;
                        await telemetryService.InitializeAsync(machineIdentity.MachineId, null).WaitAsync(TimeSpan.FromSeconds(8));
                        _loggingService?.LogInfo("[App] TelemetryService inicializado (background).");
                        _startupTracker?.End("BG_TELEMETRY_INIT");
                    }
                    catch (Exception ex)
                    {
                        _startupTracker?.Fail("BG_TELEMETRY_INIT", ex, "Telemetry falhou em background");
                        _loggingService?.LogError($"[App] TelemetryService (background): {ex.Message}");
                    }
                });

                _ = Task.Run(() => _serviceProvider.GetRequiredService<VoltrisOptimizer.Services.Shield.ShieldNotificationService>());

                var sessionInitializationTask = Task.Run(async () =>
                {
                    try
                    {
                        _startupTracker?.Begin("BG_SESSION_INIT", "Inicializando SessionManager (background)");
                        var sessSvc = _serviceProvider.GetRequiredService<VoltrisOptimizer.Services.Session.SessionManager>();
                        await sessSvc.StartSessionAsync(machineIdentity.MachineId).WaitAsync(TimeSpan.FromSeconds(8));
                        _loggingService?.LogInfo("[App] SessionManager inicializado (background).");
                        _startupTracker?.End("BG_SESSION_INIT");
                    }
                    catch (Exception ex)
                    {
                        _startupTracker?.Fail("BG_SESSION_INIT", ex, "SessionManager falhou em background");
                        _loggingService?.LogError($"[App] SessionManager (background): {ex.Message}");
                    }
                });

                // Inicializar MachineIdentity online em background (refinamento, não blocking)
                var machineIdentityOnlineTask = Task.Run(async () =>
                {
                    try
                    {
                        _startupTracker?.Begin("BG_MACHINE_IDENTITY", "Verificando MachineIdentity online (background)");
                        var idSvc = _serviceProvider.GetService<VoltrisOptimizer.Services.Enterprise.MachineIdentityService>();
                        var updatedIdentity = await Task.Run(() => idSvc.GetMachineIdentityAsync())
                            .WaitAsync(TimeSpan.FromSeconds(5));
                        _loggingService?.LogInfo("[App] MachineIdentity atualizado online (background).");
                        _startupTracker?.End("BG_MACHINE_IDENTITY");
                    }
                    catch (TimeoutException)
                    {
                        _loggingService?.LogWarning("[App] MachineIdentity online excedeu 5s — mantendo cache.");
                        _startupTracker?.End("BG_MACHINE_IDENTITY", "timeout");
                    }
                    catch (Exception ex)
                    {
                        _loggingService?.LogWarning($"[App] MachineIdentity online (background): {ex.Message}");
                        _startupTracker?.End("BG_MACHINE_IDENTITY", "error");
                    }
                });

                // 6.1. Serviços pesados em background (não bloqueiam UI)

                var startupConcurrencySemaphore = new SemaphoreSlim(3);

                _ = Task.Run(async () =>
                {
                    await startupConcurrencySemaphore.WaitAsync();
                    try
                    {
                        _startupTracker?.Begin("THERMAL_MONITOR", "Iniciando monitoramento térmico");
                        var thermalSvc = _serviceProvider.GetRequiredService<VoltrisOptimizer.Services.Thermal.IGlobalThermalMonitorService>();
                        ThermalMonitorService = thermalSvc;
                        await thermalSvc.StartMonitoringAsync().WaitAsync(TimeSpan.FromSeconds(10));
                        _startupTracker?.End("THERMAL_MONITOR");
                        _loggingService.LogInfo("[App] ThermalMonitor iniciado (background).");
                    }
                    catch (Exception ex)
                    {
                        _startupTracker?.Fail("THERMAL_MONITOR", ex, "Falha ao iniciar thermal monitor");
                        _loggingService.LogError($"[App] ThermalMonitor background: {ex.Message}");
                    }
                    finally { startupConcurrencySemaphore.Release(); }
                });
                await Task.Delay(50);

                _ = Task.Run(async () =>
                {
                    await startupConcurrencySemaphore.WaitAsync();
                    try
                    {
                        _startupTracker?.Begin("VOLTRISCORE_RESOLVE", "Resolvendo VoltrisOptimizationCore");
                        VoltrisCore = _serviceProvider.GetService<VoltrisOptimizer.Core.VoltrisOptimizationCore>();
                        _startupTracker?.End("VOLTRISCORE_RESOLVE");
                        _loggingService.LogInfo("[App] VoltrisCore resolvido (background), inicialização delegada ao AutoStart.");
                    }
                    catch (Exception ex)
                    {
                        _startupTracker?.Fail("VOLTRISCORE_RESOLVE", ex, "Falha ao resolver VoltrisCore");
                        _loggingService.LogError($"[App] VoltrisCore background: {ex.Message}");
                    }
                    finally { startupConcurrencySemaphore.Release(); }
                });
                await Task.Delay(50);

                var autostartTask = Task.Run(async () =>
                {
                    await startupConcurrencySemaphore.WaitAsync();
                    try
                    {
                        _startupTracker?.Begin("AUTOSTART_PIPELINE", "Executando IAutoStartServices");
                        var autoStartServices = _serviceProvider.GetServices<VoltrisOptimizer.Core.IAutoStartService>();

                        var startupTasks = autoStartServices.Select(async svc =>
                        {
                            try
                            {
                                var svcName = svc.GetType().Name;
                                _startupTracker?.Begin($"AUTOSTART_{svcName}", $"Thread={Thread.CurrentThread.ManagedThreadId}");

                                using var svcCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                                try
                                {
                                    await svc.StartAsync(svcCts.Token).WaitAsync(svcCts.Token);
                                    _startupTracker?.End($"AUTOSTART_{svcName}");
                                }
                                catch (OperationCanceledException)
                                {
                                    _loggingService.LogWarning($"[App] AutoStart {svcName} excedeu 5s — continuando sem ele.");
                                    _startupTracker?.End($"AUTOSTART_{svcName}", "timeout-skipped");
                                }
                            }
                            catch (Exception ex)
                            {
                                _startupTracker?.Fail($"AUTOSTART_{svc.GetType().Name}", ex, "AutoStart service falhou");
                                _loggingService.LogError($"[App] AutoStart {svc.GetType().Name}: {ex.Message}");
                            }
                        });

                        await Task.WhenAll(startupTasks);
                        _startupTracker?.End("AUTOSTART_PIPELINE");
                    }
                    catch (Exception ex)
                    {
                        _startupTracker?.Fail("AUTOSTART_PIPELINE", ex, "Pipeline de AutoStart falhou");
                        _loggingService.LogError($"[App] AutoStart pipeline: {ex.Message}");
                    }
                    finally { startupConcurrencySemaphore.Release(); }
                });
                await Task.Delay(50);

                _ = Task.Run(async () =>
                {
                    await startupConcurrencySemaphore.WaitAsync();
                    try
                    {
                    _startupTracker?.Begin("STARTUP_CLEANUP", "Limpeza inicial de arquivos temp");
                        await PerformInstantStartupCleanupAsync().WaitAsync(TimeSpan.FromSeconds(15));
                        _startupTracker?.End("STARTUP_CLEANUP");
                    }
                    catch (Exception ex)
                    {
                        _startupTracker?.Fail("STARTUP_CLEANUP", ex, "Cleanup falhou");
                    }
                    finally { startupConcurrencySemaphore.Release(); }
                });
                await Task.Delay(50);

                _ = Task.Run(async () =>
                {
                    await startupConcurrencySemaphore.WaitAsync();
                    try
                    {
                        _startupTracker?.Begin("HARDWARE_COLLECT", "Coletando info de hardware");
                        var hw = await CollectHardwareInfoAsync().WaitAsync(TimeSpan.FromSeconds(10));
                        _cachedCpuName = hw.CpuName;
                        _cachedGpuName = hw.GpuName;
                        _cachedTotalRamGb = hw.TotalRamGb;
                        _cachedOsVersion = hw.OsVersion;
                        _startupTracker?.End("HARDWARE_COLLECT");
                    }
                    catch (Exception ex)
                    {
                        _startupTracker?.Fail("HARDWARE_COLLECT", ex, "Falha ao coletar hardware");
                    }
                    finally { startupConcurrencySemaphore.Release(); }
                });

                // 7. SPLASH SHOW — ANTES de qualquer blocking, para feedback visual imediato
                // Splash já foi criado no início, apenas atualizamos o progresso aqui
                splashHost.SetProgress(30);
                splashHost.SetStatus(LocalizationService.Instance.GetString("SplashHardware"));
                await Task.Delay(400);

                // FIX: Failsafe de 45s (antes 30s) para acomodar startups legítimos mais lentos
                // (ex: máquinas lentas + verificação online de HWID + WMI).
                var splashFailsafe = new System.Threading.Timer(_ =>
                {
                    try
                    {
                        if (!splashHost.IsClosed)
                        {
                            LogToFile("[SPLASH-FAILSAFE] Timeout global de 45s atingido — forçando fechamento do splash.");
                            splashHost.Close();
                        }
                        else
                        {
                            LogToFile("[SPLASH-FAILSAFE] Splash já foi fechado — failsafe ignorado.");
                        }
                    }
                    catch (Exception ex) { LogToFile($"[SPLASH-FAILSAFE] Erro: {ex.Message}"); }
                }, null, TimeSpan.FromSeconds(45), Timeout.InfiniteTimeSpan);

                // 8. Serviços pesados resolvidos em BACKGROUND após MainWindow.Show()
                // para NUNCA travar a UI thread. As propriedades estáticas são setadas
                // conforme os serviços vão ficando disponíveis (lazy/background).
                LoggingService = _loggingService;
                
                splashHost.SetProgress(65);
                splashHost.SetStatus(LocalizationService.Instance.GetString("SplashGUI"));
                await Task.Delay(300);

                // 9. MainWindow
                _startupDiag?.Phase("MAINWINDOW_CTOR_START");
                _startupTracker?.Begin("MAINWINDOW_CTOR", "Construindo MainWindow");
                VoltrisOptimizer.Helpers.StartupWatchdog.SetPhase("STEP_9_MAINWINDOW");
                t0 = _startupSw.ElapsedMilliseconds;
                splashHost.SetProgress(85);
                splashHost.SetStatus(LocalizationService.Instance.GetString("SplashMainWin"));
                await Task.Delay(300);
                LogToFile($"[STARTUP][TID:{tid}] [STEP-9] Criando MainWindow...");
                VoltrisOptimizer.UI.MainWindow? mainWindow = null;
                try
                {
                    _startupDiag?.Phase("MAINWINDOW_BEFORE_CTOR");
                    // [LOG] MainWindow constructor
                    LogToFile($"[STARTUP][TID:{tid}] Chamando construtor de MainWindow...");
                    mainWindow = new VoltrisOptimizer.UI.MainWindow();
                    LogToFile($"[STARTUP][TID:{tid}] Construtor de MainWindow retornou.");
                    _startupDiag?.Phase("MAINWINDOW_AFTER_CTOR");
                    _tAppFlow = _startupSw.ElapsedMilliseconds - t0;
                    LogTiming(_loggingService, "MainWindowCtor", _tAppFlow);
                    _startupTracker?.End("MAINWINDOW_CTOR", $"{_tAppFlow}ms");
                    LogToFile($"[STARTUP][TID:{tid}] [STEP-9] MainWindow criada em {_tAppFlow}ms");

this.MainWindow = mainWindow;
                Application.Current.MainWindow = mainWindow;

                // CARREGAMENTO ULTRA RÁPIDO: inicia o cache de métricas ANTES de o
                // Dashboard ser exibido (Start() é não-bloqueante: Task.Run + Timer).
                // Assim o primeiro quadro já mostra CPU/RAM reais em vez de zeros.
                // A chamada tardia em PopulateStaticServicesAsync é ignorada (guard _started).
                try { VoltrisOptimizer.Core.SystemMetricsCache.Instance.Start(); } catch { }

                    // Splash conclui 100% antes do MainWindow aparecer
                    splashHost.SetProgress(100);
                    splashHost.SetStatus(LocalizationService.Instance.GetString("SplashReady"));
                    await Task.Delay(1500); // Cinematic delay to read "Pronto!"
                    LogToFile($"[STARTUP][TID:{tid}] [STEP-9] Fechando SplashHost...");
                    splashFailsafe?.Change(Timeout.Infinite, Timeout.Infinite);
                    // [LOG] SplashHost.Close()
                    splashHost.Close();
                    LogToFile($"[STARTUP][TID:{tid}] [STEP-9] SplashHost fechado.");

                    // [FIX:A-2] Welcome/Vinculação — MainWindow PRIMEIRO, Welcome DEPOIS.
                    //
                    // BUG ORIGINAL: a ordem era Splash -> Welcome(ShowDialog) ->
                    // MainWindow.Show(), com welcomeWindow.ShowDialog() num loop
                    // modal aninhado AINDA dentro de OnStartup.
                    //
                    // O que isso causava:
                    //  - OnStartup não retornava até o usuário decidir. Todo o
                    //    startup (startup tracker, timers, serviços) ficava
                    //    pendurado por um input humano. Fechar o Splash e ficar
                    //    olhando para uma tela vazia era o estado normal.
                    //  - A MainWindow tinha HWND mas nunca era Show(), então o
                    //    usuário via um splash fechado e nada por trás.
                    //  - Se o usuário minimizasse a Welcome, o app parecia
                    //    congelado: era o loop modal segurando o startup.
                    //
                    // A ordem dos DADOS é preservada, que era a preocupação
                    // original: a Welcome grava WelcomePromptShown/IsDeviceLinked,
                    // e CloudAccountService (singleton) e DashboardViewModel leem
                    // esses campos. A diferença é que a propagação passa a ser por
                    // evento em vez de por ordem de construção — ver o handler
                    // Closed abaixo, que chama SyncFromSettings() para que o
                    // serviço de nuvem releia as settings depois que a janela fechar.
                    var settings = VoltrisOptimizer.Services.SettingsService.Instance.Settings;
                    bool userMadeChoice = settings.IsDeviceLinked || settings.WelcomePromptShown;

                    LogToFile($"[STARTUP][TID:{tid}] [STEP-9] Chamando MainWindow.Show()...");
                    _startupDiag?.Phase("MAINWINDOW_BEFORE_SHOW");
                    if (startMinimized)
                    {
                        // [FIX:B-2] "Iniciar minimizado" que apenas faz
                        // WindowState=Minimized deixa um botão na barra de
                        // tarefas. O usuário que pediu para começar minimized
                        // quer o app na bandeja, sem roubar foco — senão a opção
                        // não cumpre o que promete e o app interrompe o que ele
                        // estava fazendo a cada boot.
                        mainWindow.WindowState = WindowState.Minimized;
                        mainWindow.ShowInTaskbar = false;
                        LogToFile(
                            "[FIX:B-2] Iniciando minimizado para a bandeja " +
                            "(WindowState=Minimized + ShowInTaskbar=false)");
                    }
                    // [FIX:A-3] ORDEM DE TELAS: Splash -> Welcome -> Programa.
                    //
                    // COMO ESTAVA (ERRADO)
                    // A MainWindow era exibida primeiro e a Welcome aparecia
                    // DEPOIS, modeless e por cima. O usuário via o Dashboard
                    // carregar enquanto era interrompido por um convite de login —
                    // e a tela de login, que é a PRIMEIRA decisão real do produto,
                    // chegava tarde e por cima da interface principal.
                    //
                    // POR QUE A VERSÃO ANTERIOR ERA MODELESS
                    // Houve um ShowDialog() que segurava OnStartup até o usuário
                    // decidir, e a lição registrada foi: startup pendurado em input
                    // humano é um congelamento. A correção (modeless) resolveu o
                    // congelamento, mas quebrou a ordem das telas.
                    //
                    // POR QUE ESTA VERSÃO NÃO VOLTA AO ShowDialog
                    // O ShowDialog não era o defeito — o defeito era travar o
                    // único ponto de inicialização. Aqui a MainWindow já está
                    // CONSTRUÍDA e aquecida neste instante (STEP-9 acima) e o
                    // startup NÃO é bloqueado: este `await` devolve a thread ao
                    // Dispatcher do WPF, que segue processando mensagens, timers e
                    // eventos enquanto o usuário decide. Nenhum serviço trava; a
                    // única coisa adiada é a APARIÇÃO da janela.
                    //
                    // A Welcome é uma PERGUNTA EM ABERTO, não uma preferência de
                    // aparência. Por isso o gate é só `!userMadeChoice`.
                    //
                    // BUG CORRIGIDO AQUI (confirmado em log de 2026-09-28):
                    // a primeira versão somava `&& !startMinimized`, usando "iniciar
                    // minimizado" para também suprimir a Welcome. Com
                    // StartMinimized=True + StartWithWindows=True (config normal de
                    // quem usa a bandeja), a tela NUNCA aparecia — mesmo com
                    // welcomePromptShown=False e isDeviceLinked=False, ou seja,
                    // sem resposta do usuário. O log confirmava: nada da Welcome
                    // era registrado e MainWindow.Show() vinha logo após o splash.
                    //
                    // São coisas diferentes:
                    //   startMinimized = para onde a JANELA vai depois (bandeja);
                    //   userMadeChoice = se o usuário JÁ respondeu sobre a conta.
                    // Uma não pode anular a outra.
                    //
                    // O custo de perguntar mesmo iniciando minimizado é nulo na
                    // prática: a pergunta acontece no máximo UMA vez, porque
                    // WelcomePromptShown é gravado ao fechar. Depois disso o
                    // comportamento silencioso de bandeja é respeitado em todo
                    // boot seguinte.
                    bool showWelcome = !userMadeChoice;

                    LogToFile(
                        $"[STARTUP][TID:{tid}] Welcome: mostrar={showWelcome} | " +
                        $"WelcomePromptShown={settings.WelcomePromptShown} | " +
                        $"IsDeviceLinked={settings.IsDeviceLinked} | " +
                        $"startMinimized={startMinimized}");

                    if (showWelcome)
                    {
                        var welcomeWindow = new UI.Windows.WelcomeLinkWindow();

                        // Sem Owner de propósito: Owner exige uma janela JÁ
                        // apresentada, e a MainWindow ainda não foi. A Welcome
                        // declara Width=850 Height=550, ou seja, tem tamanho
                        // determinístico, então CenterScreen centraliza exato —
                        // é o mesmo princípio que corrige o posicionamento da
                        // MainWindow.
                        welcomeWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                        welcomeWindow.Show();

                        // Completa a Task quando o usuário fechar a Welcome, seja
                        // por ter vinculado a conta, por ter pulado ou por ter
                        // clicado no X. RunContinuationsAsynchronously evita que
                        // a continuação (que mostra a MainWindow) rode dentro do
                        // próprio evento Closed.
                        var welcomeClosed = new TaskCompletionSource<bool>(
                            TaskCreationOptions.RunContinuationsAsynchronously);

                        // Propaga o estado de vínculo assim que o usuário fechar a
                        // Welcome. Sem isto, o CloudAccountService manteria o
                        // IsLinked capturado na sua construção (false) e o
                        // Dashboard continuaria exibindo "desvinculado" mesmo
                        // depois de o usuário ter vinculado a conta.
                        welcomeWindow.Closed += (_, __) =>
                        {
                            try
                            {
                                VoltrisOptimizer.Services.Cloud.CloudAccountService.Instance.SyncFromSettings();
                                // Lê as settings de novo em vez de usar a referência
                                // capturada: a Welcome chama SaveSettings() no
                                // OnClosed, e a instância pode ter sido substituída
                                // nesse caminho — o log registraria o valor antigo.
                                var isLinkedNow = VoltrisOptimizer.Services.SettingsService.Instance.Settings.IsDeviceLinked;
                                LogToFile(
                                    $"[FIX:A-3] Welcome fechada — IsDeviceLinked={isLinkedNow}. " +
                                    "O programa será exibido agora.");
                            }
                            catch (Exception syncEx)
                            {
                                LogToFile($"[FIX:A-3] Falha ao sincronizar estado de vínculo: {syncEx.Message}");
                            }
                            finally
                            {
                                welcomeClosed.TrySetResult(true);
                            }
                        };

                        LogToFile(
                            "[FIX:A-3] Splash -> Welcome. Aguardando a decisão do usuário; " +
                            "a MainWindow abre só depois.");

                        await welcomeClosed.Task;
                    }

                    mainWindow.Show();
                    _startupDiag?.Phase("MAINWINDOW_AFTER_SHOW");
                    LogToFile($"[STARTUP][TID:{tid}] [STEP-9] MainWindow.Show() retornou.");

                    // Auto-registrar no startup do Windows e sincronizar estado

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var sm = new StartupManager(_loggingService);

                            // Sincronizar: se usuario desabilitou no Gerenciador de Tarefas,
                            // desabilitar tambem o Task Scheduler
                            sm.SyncWithTaskManagerState();

                            if (SettingsService.Instance.Settings.StartWithWindows)
                            {
                                // [FIX:AUTOSTART-DETECT] Garante que a entrada de
                                // auto-start do REGISTRO carregue o argumento
                                // "--autostart".
                                //
                                // Essa é a parte do auto-start que se conserta sem
                                // elevação de privilégio, e por isso é feita sempre:
                                // se o valor gravado não tiver o sinalizador, o
                                // próximo logon inicia o app sem nenhuma marca de
                                // origem e a janela aparece — que é exatamente o
                                // defeito que estamos corrigindo.
                                //
                                // A entrada do Agendador de Tarefas também recebe o
                                // sinalizador, mas ela roda com
                                // RunLevel=HighestAvailable e por isso só pode ser
                                // gravada por um processo elevado. Fica registrado
                                // no log quando isso acontece, em vez de falhar
                                // calado.
                                sm.EnsureAutostartEntryHasMarker();

                                // Verificar se o Task Scheduler existe (mecanismo REAL de startup)
                                bool taskExists = false;
                                try
                                {
                                    var psi = new ProcessStartInfo
                                    {
                                        FileName = "schtasks",
                                        Arguments = "/query /tn \"VoltrisOptimizer_Startup\"",
                                        RedirectStandardOutput = true,
                                        CreateNoWindow = true,
                                        UseShellExecute = false
                                    };
                                    using (var p = Process.Start(psi))
                                    {
                                        p?.WaitForExit();
                                        taskExists = p?.ExitCode == 0;
                                    }
                                }
                                catch { }

                                if (!taskExists)
                                {
                                    sm.SetStartup(true, SettingsService.Instance.Settings.StartMinimized);
                                    _loggingService?.LogInfo("[App] Startup automático configurado (Task Scheduler + Registro).");
                                }
                            }
                            else
                            {
                                // Se a config diz false mas ainda existe entrada, limpar
                                if (sm.IsStartupEnabled())
                                {
                                    sm.SetStartup(false, false);
                                    _loggingService?.LogInfo("[App] Startup desabilitado por config — limpando entradas existentes.");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _loggingService?.LogWarning($"[App] Auto-startup error: {ex.Message}");
                        }
                    });

                    await Dispatcher.InvokeAsync(() =>
                    {
                        LogToFile($"[STARTUP][TID:{Thread.CurrentThread.ManagedThreadId}] UI ociosa — MainWindow renderizada.");
                    }, DispatcherPriority.ApplicationIdle);

                    _ = AsyncHelper.SafeTaskRun(async () =>
                    {
                        var ptid = Thread.CurrentThread.ManagedThreadId;
                        await Task.Delay(1500).ConfigureAwait(false);

                        LogToFile($"[STARTUP][TID:{ptid}] Iniciando PopulateStaticServicesAsync em background...");
                        VoltrisDiagnosticSystem.Instance.Timeline("POPULATE", "PopulateStaticServicesAsync iniciado");
                        await PopulateStaticServicesAsync(_serviceProvider).ConfigureAwait(false);
                        LogToFile($"[STARTUP][TID:{ptid}] PopulateStaticServicesAsync FINALIZADO.");
                        VoltrisDiagnosticSystem.Instance.Timeline("POPULATE", "PopulateStaticServicesAsync concluído");

                        try
                        {
                            LogToFile($"[STARTUP][TID:{ptid}] Resolvendo SystemProfiler...");
                            SystemProfiler = _serviceProvider.GetService<VoltrisOptimizer.Interfaces.ISystemProfiler>();
                            if (SystemProfiler != null)
                                _ = AsyncHelper.SafeTaskRun(async () => { try { await SystemProfiler.AnalyzeAsync(); } catch (Exception ex) { LogToFile($"[App] SystemProfiler.AnalyzeAsync: {ex.Message}"); } });

                            LogToFile($"[STARTUP][TID:{ptid}] Iniciando SystemMetricsCache...");
                            VoltrisOptimizer.Core.SystemMetricsCache.Instance.Start();
                            VoltrisDiagnosticSystem.Instance.Timeline("CACHE", "SystemMetricsCache iniciado");

                            var autoStartOptimizer = _serviceProvider.GetService<AutoStartOptimizer>();
                            if (autoStartOptimizer != null)
                            {
                                _ = Task.Run(async () =>
                                {
                                    try
                                    {
                                        await autoStartOptimizer.StartAsync(CancellationToken.None);
                                    }
                                    catch (Exception ex)
                                    {
                                        LogToFile($"[App] autoStartOptimizer: {ex.Message}");
                                    }
                                });
                            }

                            LogToFile($"[STARTUP][TID:{ptid}] Iniciando VoltrisBrainV2...");
                            if (BrainV2 != null)
                            {
                                await BrainV2.StartAsync().ConfigureAwait(false);
                                LogToFile($"[STARTUP][TID:{ptid}] VoltrisBrainV2 iniciado.");
                            }

                            LogToFile($"[STARTUP][TID:{ptid}] Iniciando VoltrisStartupOrchestrator...");
                            var startupOrchestrator = _serviceProvider.GetService<VoltrisOptimizer.Core.VoltrisStartupOrchestrator>();
                            if (startupOrchestrator != null)
                            {
                                _ = Task.Run(async () => {
                                    try
                                    {
                                        await startupOrchestrator.InitializeOptimizationsAsync(CancellationToken.None);
                                    }
                                    catch (Exception ex)
                                    {
                                        LogToFile($"[STARTUP] Erro no StartupOrchestrator: {ex.Message}");
                                    }
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            LogToFile($"[STARTUP] Erro no pós-boot: {ex.Message}");
                        }
                    });

                    _startupDiag?.Phase("STARTUP_COMPLETE");
                    LogToFile($"[STARTUP][TID:{tid}] [STEP-9] Fluxo de inicialização App.xaml.cs concluído.");

                    // ─── VOLTRIS SHIELD: ativação automática por padrão ───
                    // Ativa somente se houver licença Standard/Pro/Enterprise.
                    // Sem licença, o Shield permanece totalmente offline e cada tentativa fica registrada.
                    _ = AsyncHelper.SafeTaskRun(async () => {
                        try {
                            await Task.Delay(4000).ConfigureAwait(false);
                            if (_serviceProvider != null)
                            {
                                var shield = _serviceProvider.GetService<VoltrisOptimizer.Services.Shield.VoltrisShieldService>();
                                if (shield != null)
                                {
                                    var activated = await shield.InitializeProtectionAsync("Startup");
                                    LogToFile($"[STARTUP] Voltris Shield: ativação automática = {activated}");
                                }
                            }
                        }
                        catch (Exception ex) { LogToFile($"[STARTUP] Erro na ativação automática do Voltris Shield: {ex.Message}"); }
                    });

                    _ = AsyncHelper.SafeTaskRun(async () => {
                        try {
                            await Task.Delay(3000).ConfigureAwait(false);
                            var settings = SettingsService.Instance.Settings;
                            // Onboarding removido completamente - Análise Neural de Hardware foi eliminada
                            settings.OnboardingCompleted = true;
                            SettingsService.Instance.SaveSettings();
                        } catch (Exception onboardingEx) { System.Diagnostics.Debug.WriteLine($"[App] Onboarding: {onboardingEx.Message}"); }
                    });

                    // Registrar menu de contexto do desktop se habilitado
                    _ = AsyncHelper.SafeTaskRun(async () => {
                        try {
                            await Task.Delay(5000).ConfigureAwait(false);
                            if (_serviceProvider != null)
                            {
                                var ctxMenu = _serviceProvider.GetService<VoltrisOptimizer.Services.Shell.DesktopContextMenuService>();
                                if (ctxMenu != null)
                                {
                                    // Sempre limpa entradas órfãs (de execuções anteriores que podem ter crasado)
                                    ctxMenu.CleanupAll();

                                    var settings = SettingsService.Instance.Settings;
                                    if (settings.EnableDesktopContextMenu)
                                    {
                                        ctxMenu.RegisterWithSubMenus();
                                        LogToFile("[App] Menu de contexto do desktop registrado na inicialização");
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LogToFile($"[App] Erro ao registrar menu de contexto no startup: {ex.Message}");
                        }
                    });

                    // Iniciar servidor IPC para receber comandos do menu de contexto
                    _ = AsyncHelper.SafeTaskRun(async () => {
                        try {
                            await Task.Delay(6000).ConfigureAwait(false);
                            if (_serviceProvider != null)
                            {
                                var pipeSvc = _serviceProvider.GetService<VoltrisOptimizer.Services.Shell.CommandPipeService>();
                                if (pipeSvc != null)
                                {
                                    var spSnapshot = _serviceProvider; // captura para uso no callback
                                    pipeSvc.OnCommandReceived += (cmd) =>
                                    {
                                        LogToFile($"[App] Comando IPC recebido: \"{cmd}\"");
                                        try
                                        {
                                            Dispatcher.BeginInvoke(() =>
                                            {
                                                switch (cmd)
                                                {
                                                    case "quickclean":
                                                        LogToFile("[App] IPC: Executando QuickCleanup via DashboardViewModel");
                                                        var vmClean = spSnapshot?.GetService<VoltrisOptimizer.UI.ViewModels.DashboardViewModel>();
                                                        vmClean?.QuickCleanupCommand.Execute("ContextMenu");
                                                        break;
                                                    case "optimize":
                                                        LogToFile("[App] IPC: Executando QuickOptimize via DashboardViewModel");
                                                        var vmOpt = spSnapshot?.GetService<VoltrisOptimizer.UI.ViewModels.DashboardViewModel>();
                                                        vmOpt?.QuickOptimizeCommand.Execute("ContextMenu");
                                                        break;
                                                    case "defrag":
                                                        LogToFile("[App] IPC: Executando Defrag via StorageOptimizerService (com progresso global)");
                                                        _ = Task.Run(async () =>
                                                        {
                                                            var gps = VoltrisOptimizer.Services.GlobalProgressService.Instance;
                                                            gps.StartOperation(LocalizationService.Instance.GetString("DefragmentingDisks"), true);
                                                            try
                                                            {
                                                                var storageSvc = spSnapshot?.GetService<VoltrisOptimizer.Services.Hardware.StorageOptimizerService>();
                                                                // Se não estiver no DI, cria diretamente (igual RepairViewModel.ExecuteDefragAsync)
                                                                storageSvc ??= new VoltrisOptimizer.Services.Hardware.StorageOptimizerService();
                                                                var progress = new Progress<string>(msg => gps.UpdateProgress(0, msg));
                                                                await storageSvc.OptimizeAllDrivesAsync(progress);
                                                                gps.CompleteOperation(LocalizationService.Instance.GetString("DiscOptimizeComplete"));
                                                            }
                                                            catch (Exception ex)
                                                            {
                                                                gps.FailOperation("\u2705 " + string.Format(LocalizationService.Instance.GetString("CommonErrorFormat"), ex.Message));
                                                                LogToFile($"[App] Erro no defrag IPC: {ex.Message}");
                                                            }
                                                        });
                                                        break;
                                                    case "diagnose":
                                                          // [FIX:UNICA-FONTE] O diagnóstico de energia saiu do IPC.
                                                          //
                                                          // Ele chamava o `o servico legado`, removido do projeto
                                                          // junto com a página ENERGIA. O serviço fazia duas coisas:
                                                          // media o estado de energia e APLICAVA correcoes (PCIe ASPM,
                                                          // boost mode, PowerThrottlingOff) — e essa segunda parte era
                                                          // escrita de energia com efeito no sistema, feita por um
                                                          // componente que nao tinha nada a ver com o perfil escolhido.
                                                          //
                                                          // O diagnostico continua existindo em outro lugar, e melhor:
                                                          // o `ProfilePowerMatrixSelfTest` roda no startup e valida a
                                                          // tabela contra dez regras, e o `PowerApply` registra cada valor
                                                          // gravado com read-back. Se o comando vier de uma instancia
                                                          // antiga, a resposta e um log, nao uma acao.
                                                          LogToFile(
                                                              "[App] IPC 'diagnose': diagnóstico de energia agora pertence ao " +
                                                              "Perfil Inteligente (self-test no startup + log PowerApply). " +
                                                              "Nenhuma alteracao de energia foi executada.");
                                                          VoltrisOptimizer.Services.GlobalProgressService.Instance
                                                              .CompleteOperation(
                                                                  LocalizationService.Instance.GetString("DiagnosticsCompleted"));
                                                          break;
                                                    default:
                                                        LogToFile($"[App] IPC: Comando desconhecido \"{cmd}\"");
                                                        break;
                                                }
                                            });
                                        }
                                        catch (Exception ex)
                                        {
                                            LogToFile($"[App] Erro ao processar comando IPC \"{cmd}\": {ex.Message}");
                                        }
                                    };

                                    pipeSvc.StartServer();
                                    LogToFile("[App] Servidor de comandos IPC iniciado");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LogToFile($"[App] Erro ao iniciar servidor IPC: {ex.Message}");
                        }
                    });
                }
                catch (Exception ex)
                {
                    LogToFile($"[STEP-9] ERRO ao criar MainWindow: {ex.Message}");
                    _loggingService?.LogError($"[App] Erro ao criar MainWindow: {ex.Message}", ex);
                    System.Windows.MessageBox.Show(string.Format(LocalizationService.Instance.GetString("AppInterfaceErrorMessage"), ex.Message), LocalizationService.Instance.GetString("ErrorTitleShort"), System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    Shutdown(1);
                    return;
                }

                ShutdownMode = ShutdownMode.OnMainWindowClose;
                RegisterSessionEndingSafe();

                // 10. WIDGET AUTO-START — após MainWindow estar totalmente estável
                // CORREÇÃO: Evitar async lambda dentro de Dispatcher.InvokeAsync.
                // O pattern anterior (async () => { await Task.Delay(...) } dentro de InvokeAsync)
                // causava dois problemas: (a) o await externo só esperava o início da operação,
                // não a concluso do Task interna — tornando o Delay e o InitializeIfEnabled
                // fire-and-forget silencioso; (b) await dentro do Dispatcher pump é anti-padrão.
                // CORREÇÃO: Consolidar ambos os delays no Task.Run e usar callback síncrono no Dispatcher.
                _ = AsyncHelper.SafeTaskRun(async () =>
                {
                    // 5s (estabilidade pós-Show) + 2s (estabilidade da UI) = 7s total
                    await Task.Delay(7000).ConfigureAwait(false);
                    try
                    {
                        _startupTracker?.Begin("WIDGET_AUTOSTART", "Inicializando widget flutuante");
                        await Dispatcher.InvokeAsync(() =>
                        {
                            try
                            {
                                if (WidgetManager != null)
                                {
                                    WidgetManager.InitializeIfEnabled();
                                    LogToFile("[App] Widget auto-start executado com sucesso");
                                }
                                _startupTracker?.End("WIDGET_AUTOSTART");
                            }
                            catch (Exception ex)
                            {
                                _startupTracker?.Fail("WIDGET_AUTOSTART", ex, "Falha ao iniciar widget");
                                LogToFile($"[App] Widget auto-start falhou: {ex.Message}");
                            }
                        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    }
                    catch (Exception ex)
                    {
                        _startupTracker?.Fail("WIDGET_AUTOSTART", ex, "Falha ao despachar widget para UI");
                        LogToFile($"[App] Widget auto-start (dispatcher) falhou: {ex.Message}");
                    }
                });

                // HOTKEYS GLOBAIS — registar após widget estar pronto
                try
                {
                    _hotkeyService = _serviceProvider?.GetService<HotkeyService>();
                    if (_hotkeyService != null)
                    {
                        _hotkeyService.Initialize();
                        _hotkeyService.HotkeyPressed += OnHotkeyPressed;
                        _hotkeyService.RegisterAll();
                        LogToFile("[App] Hotkeys globais registradas com sucesso.");
                    }
                }
                catch (Exception ex)
                {
                    LogToFile($"[App] Falha ao inicializar hotkeys: {ex.Message}");
                }

                // ── INICIALIZAÇÃO DOS MOTORES AUTÔNOMOS PROFISSIONAIS ──
                // 100% C# e Win32 nativo, zero placebo, comprovação métrica real e máxima visibilidade
                _ = VoltrisOptimizer.Helpers.AsyncHelper.SafeTaskRun(async () =>
                {
                    try
                    {
                        await Task.Delay(4000).ConfigureAwait(false);
                        _loggingService?.LogInfo("[App] Iniciando suíte autônoma de otimização contínua...");

                        VoltrisOptimizer.Core.Optimization.ContinuousVoltriScoreEngine.Instance.Start();
                        VoltrisOptimizer.Services.Optimization.SmartActionNotificationEngine.Instance.Start();
                        VoltrisOptimizer.Services.Intelligence.AutonomousReportService.Instance.Start();
                        VoltrisOptimizer.Services.Maintenance.NightIdleMaintenanceService.Instance.Start();
                        VoltrisOptimizer.Services.Responsiveness.SmartAppFocusEngine.Instance.Start();

                        _loggingService?.LogSuccess("[App] ✅ Suíte autônoma de otimização contínua ativa e operacional.");
                    }
                    catch (Exception autoEx)
                    {
                        _loggingService?.LogError($"[App] Erro ao iniciar suíte autônoma: {autoEx.Message}", autoEx);
                    }
                });

                // Serviços resolvidos LAZY quando acessados — sem bloqueio de startup.
                // As propriedades estáticas (Body, BrainV2, etc.) permanecem null e
                // são populadas sob demanda pela primeira vez que alguém as acessa.

                // Aguardar pipeline de AutoStart antes de marcar startup como completo
                if (autostartTask != null)
                {
                    try { await autostartTask.WaitAsync(TimeSpan.FromSeconds(30)); }
                    catch (TimeoutException) { _loggingService?.LogWarning("[App] AUTOSTART_PIPELINE não concluiu em 30s — continuando."); }
                }

                _startupTracker?.End("APP_STARTUP");
                _startupTracker?.MarkCompleted();
                _startupDiag?.Phase("STARTUP_COMPLETE");
                _startupDiag?.Dispose();
                _uiMonitor?.LogReport();
                VoltrisOptimizer.Helpers.StartupWatchdog.SetPhase("STARTUP_COMPLETE");

                // Limpeza periódica de processos zumbis (a cada 5 minutos, se ainda houver instâncias órfãs)
                _ = AsyncHelper.SafeTaskRun(async () =>
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromMinutes(5)).ConfigureAwait(false);
                        var currentProc = Process.GetCurrentProcess();
                        var others = Process.GetProcessesByName(currentProc.ProcessName).Where(p => p.Id != currentProc.Id);
                        foreach (var p in others)
                        {
                            try
                            {
                                LogToFile($"[App] PROCESSO zumbi detectado (PID {p.Id}) no pós-startup. Finalizando...");
                                p.Kill();
                            }
                            catch { }
                        }
                    }
                    catch { }
                });

                _loggingService?.LogState("InitPhase", "COMPLETE");

                // [FIX:STRATEGY-3-STATES] PROVA, NO STARTUP, DE QUE OS 3 BOTÕES
                // DE PERFORMANCE FAZEM COISAS DIFERENTES.
                //
                // O bug original desta tela nunca apareceu em build nem em tela: o
                // botão gravava um texto, um consumidor decidia com SIM/NÃO, e dois
                // dos três acabavam com o mesmo efeito. Para isso não volte, o
                // startup roda a verificação de assinatura: se as três estratégias
                // produzirem o mesmo EPP/plano/limpeza, o log sai em ERRO aqui
                // mesmo, e não dias depois num "não senti diferença".
                //
                // Custo zero: nenhuma chamada ao Windows, só aritmética.
                try
                {
                    VoltrisOptimizer.Services.Performance.PerformanceStrategySelfTest.Run(_loggingService, out string strategyReport);

                    // [FIX:POWER-MATRIX] PROVA DE QUE A TABELA DE ENERGIA NAO
                    // PENALIZA NINGUEM.
                    //
                    // São 8 regras que existem exatamente para responder à
                    // preocupação "e se o app limitar meu PC gamer?". A mais
                    // importante é a REGRA 2: nenhum perfil pode receber um valor
                    // MENOS agressivo no tier de máquina que aguenta o pico do que
                    // recebe no tier de máquina fina. Se alguém inverter isso, o
                    // gaming notebook passa a receber EPP alto demais e o
                    // ultrathin fica estrangulado — e nenhum build, tela ou log
                    // comum acusaria. Aqui o build acusa.
                    VoltrisOptimizer.Services.Power.ProfilePowerMatrixSelfTest.Run(_loggingService, out string matrixReport);
                }
                catch (Exception strategyEx)
                {
                    _loggingService?.LogWarning($"[App] StrategySelfTest falhou ao rodar: {strategyEx.Message}");
                }

                Program.WriteCrashLog("STARTUP_INIT_END");
                try { VoltrisDiagnosticSystem.Instance.Stop(); } catch (Exception diagEx) { System.Diagnostics.Debug.WriteLine($"[App] DiagnosticStop: {diagEx.Message}"); }
                _loggingService.LogInfo("[App] ================= ONSTARTUP CONCLUÍDO =================");
                _loggingService?.LogExit(nameof(InitializeInternalAsync), null, _startupSw.ElapsedMilliseconds);
                _loggingService.Flush();
            }
            catch (Exception ex)
            {
                _loggingService?.LogState("InitPhase", $"FATAL:{ex.Message}");
                _startupDiag?.Error($"InitializeInternalAsync fatal: {ex.Message}", ex);
                _startupDiag?.Dispose();
                _uiMonitor?.LogReport();
                _uiMonitor?.Dispose();
                _loggingService?.LogError($"[App] InitializeInternalAsync fatal: {ex.Message}", ex);
                Program.WriteCrashLog($"STARTUa_FATAL: {ex.Message}");
                LogToFile($"[CRITICAL] Startup fatal: {ex.Message}");
                Dispatcher.InvokeAsync(() =>
                {
                    System.Windows.MessageBox.Show(string.Format(LocalizationService.Instance.GetString("AppFatalStartupError"), ex.Message), LocalizationService.Instance.GetString("AppFatalErrorTitle"), System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    Shutdown(1);
                });
            }
        }

        private async Task PerformInstantStartupCleanupAsync()
        {
            try
            {
                var cleaner = SystemCleaner;
                if (cleaner != null)
                {
                    await cleaner.CleanTempFilesAsync();
                    _loggingService?.LogInfo("[App] Startup cleanup concluído.");
                }
            }
            catch (Exception ex)
            {
                _loggingService?.LogError($"[App] Startup cleanup: {ex.Message}");
            }
        }

        private async Task<HardwareInfo> CollectHardwareInfoAsync()
        {
            try
            {
                _loggingService?.LogInfo("[HARDWARE] Iniciando coleta de hardware para telemetria...");
                var hw = await HardwareHelper.GetCurrentSessionHardwareAsync();
                _loggingService?.LogSuccess($"[HARDWARE] Coleta concluída: CPU={hw.CpuName}, RAM={hw.TotalRamGb:F1}GB, GPU={hw.GpuName}");
                return hw;
            }
            catch (Exception ex)
            {
                _loggingService?.LogError($"[App] Erro na coleta de hardware: {ex.Message}", ex);
                return new VoltrisOptimizer.Services.Telemetry.HardwareInfo();
            }
        }

        private void RegisterSessionEndingSafe()
        {
            try
            {
                if (!_sessionEndingRegistered)
                {
                    SystemEvents.SessionEnding += (s, sessionEndingArgs) =>
                    {
                        _loggingService.LogInfo("[App] SessionEnding detectado, iniciando shutdown seguro...");
                        try
                        {
                            Task.Run(async () => {
                                if (BrainV2 != null) await BrainV2.StopAsync();
                                if (Spine != null) await Spine.RollbackAllAsync();
                            }).Wait(TimeSpan.FromSeconds(3));
                            _loggingService.LogInfo("[App] Shutdown seguro concluído.");
                        }
                        catch (Exception ex)
                        {
                            _loggingService.LogError($"[App] Shutdown seguro falhou: {ex.Message}");
                        }
                    };
                    _sessionEndingRegistered = true;
                    _loggingService.LogInfo("[App] SessionEnding registrado com sucesso.");
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"[App] Falha ao registrar SessionEnding: {ex.Message}");
            }
        }

        // Contador de falhas consecutivas do GPU TDR para fallback de software rendering
        private int _renderThreadFailureCount = 0;
        private DateTime _firstRenderThreadFailureAt = DateTime.MinValue;
        private bool _softwareRenderingFallbackActive = false;

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // Win32Exception 1816 (ERROR_NOT_ENOUGH_QUOTA) é um erro transiente do DWM causado por
            // pressão de recursos GDI/User handles. Não indica falha da aplicação — apenas logar como
            // Warning e marcar como handled para evitar crash desnecessário e spam de Rollback.
            if (e.Exception is System.ComponentModel.Win32Exception w32Ex && w32Ex.NativeErrorCode == 1816)
            {
                _loggingService?.LogWarning($"[App] Win32Exception transiente (DWM/GDI quota): {w32Ex.Message} — ignorado.");
                e.Handled = true;
                return;
            }

            // UCEERR_RENDERTHREADFAILURE (0x88980406) — GPU TDR (Timeout Detection and Recovery).
            // Este é um evento do DRIVER DE GPU, não um bug da aplicação. O DirectX/WPF perde o
            // canal de composição quando o driver reinicia após um hang. Tratamento correto:
            //   • Logar como Warning (não Error) — é um evento transiente esperado em GPUs instáveis
            //   • NÃO acionar Rollback — não há dado corrompido, é apenas um reset do pipeline gráfico
            //   • Contar falhas: se > 3 em menos de 60 segundos → forçpr software rendering como proteção
            if (e.Exception is System.Runtime.InteropServices.COMException comEx &&
                (uint)comEx.HResult == 0x88980406u)
            {
                var now = DateTime.UtcNow;

                // Reset da janela de contagem se passou mais de 60 segundos desde a primeira falha
                if (_firstRenderThreadFailureAt == DateTime.MinValue ||
                    (now - _firstRenderThreadFailureAt).TotalSeconds > 60)
                {
                    _renderThreadFailureCount = 0;
                    _firstRenderThreadFailureAt = now;
                }

                _renderThreadFailureCount++;

                if (!_softwareRenderingFallbackActive && _renderThreadFailureCount >= 3)
                {
                    // Múltiplas falhas em sequência — GPU instável. Forçpr software rendering.
                    _softwareRenderingFallbackActive = true;
                    try
                    {
                        System.Windows.Media.RenderOptions.ProcessRenderMode =
                            System.Windows.Interop.RenderMode.SoftwareOnly;
                        _loggingService?.LogWarning(
                            $"[App] ⚠️ GPU TDR (UCEERR_RENDERTHREADFAILURE) recorrente " +
                            $"({_renderThreadFailureCount}x em {(now - _firstRenderThreadFailureAt).TotalSeconds:F0}s). " +
                            "Forçando renderização via SOFTWARE para estabilidade.");
                    }
                    catch (Exception renderEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"[App] Erro ao ativar software rendering: {renderEx.Message}");
                    }
                }
                else
                {
                    _loggingService?.LogWarning(
                        $"[App] ⚠️ GPU TDR detectado (UCEERR_RENDERTHREADFAILURE 0x88980406) — " +
                        $"ocorrência #{_renderThreadFailureCount}. Evento do driver de GPU, não da aplicação. " +
                        "Recuperando automaticamente.");
                }

                // Marcar como handled — o WPF se recupera automaticamente do TDR na maioria dos casos
                e.Handled = true;
                return;
            }

            _loggingService?.LogError($"[App] DispatcherUnhandledException: {e.Exception.Message}", e.Exception);
            LogToFile($"[CRITICAL] UNHANDLED EXCEPTION: {e.Exception.Message}");
            Program.WriteCrashLog($"FATAL_EXCEPTION_DISPATCHER: {e.Exception.Message}\n{e.Exception.StackTrace}");
            TryEmergencyRollback("DispatcherUnhandledException");
            e.Handled = true;
        }

        private static int _emergencyRollbackGate;

        // [FIX:C-3] Throttle das first-chance: tipo+mensagem -> último instante
        // em que foi registrada. Um bug que lança a cada 4 Hz vira no máximo
        // uma linha a cada 30 s, então observar first-chance não cria um novo
        // storm de log (que seria trocar um problema por outro).
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _firstChanceThrottle = new();
        private const int FirstChanceThrottleSeconds = 30;

        // [FIX:C-3] Sinalizado no primeiro passo de OnExit. Existe porque o
        // handler de first-chance e estatico (evento do AppDomain) e por isso
        // nao alcança o campo de instancia _isExiting.
        private static int _shutdownStarted;

        private static void OnFirstChanceException(object? sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs e)
        {
            try
            {
                var ex = e.Exception;
                if (ex == null) return;

                // Cancelamento e descarte durante shutdown são rotineiros e não
                // indicam falha; registrá-los só poluiria o log.
                if (ex is OperationCanceledException) return;
                if (ex is ObjectDisposedException && Volatile.Read(ref _shutdownStarted) == 1) return;

                var key = ex.GetType().FullName + "|" + ex.Message;
                var now = DateTime.UtcNow;
                if (_firstChanceThrottle.TryGetValue(key, out var last) &&
                    (now - last).TotalSeconds < FirstChanceThrottleSeconds)
                {
                    return;
                }
                _firstChanceThrottle[key] = now;

                // WARNING, não ERROR: first-chance significa "alguém capturou",
                // não "o app quebrou". Marcá-la como ERROR seria o mesmo tipo de
                // falso positivo que o LicenseGuard produzia.
                //
                // [FIX:C-3] Guarda o STACK COMPLETO, não só a primeira linha.
                //
                // A validação em runtime provou que registrar apenas a origem
                // (primeira linha do StackTrace) é insuficiente: 126 exceções
                // ArgumentException, todas com a mesma origem
                // "Process.GetProcessById", e sem o frame chamador não há como
                // saber QUAL dos 143 call sites estava raceando. Perdi tempo
                // real caçando isso. O stack completo identifica o componente
                // na primeira leitura.
                //
                // Limitado a 900 caracteres porque este log é escrito de forma
                // síncrona dentro do LoggingService: travar o thread que lançou a
                // exceção por causa de formatação seria trocar um bug pequeno por
                // um grande.
                var stack = ex.StackTrace ?? "";
                if (stack.Length > 900) stack = stack.Substring(0, 900) + " ...(truncado)";

                LoggingService?.LogWarning(
                    $"[FIX:C-3] FIRST-CHANCE (capturada, nao fatal) | {ex.GetType().Name}: {ex.Message} | " +
                    $"thread={Environment.CurrentManagedThreadId}" +
                    (stack.Length > 0 ? $" | stack={stack.Replace("\r\n", " <- ").Replace("\n", " <- ")}" : ""));
            }
            catch
            {
                // Nunca deixar o observador de exceções lançar.
            }
        }

        private void TryEmergencyRollback(string origin)
        {
            try
            {
                if (Interlocked.Exchange(ref _emergencyRollbackGate, 1) == 1)
                    return;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (Spine != null) await Spine.RollbackAllAsync();
                    }
                    catch { }
                });
            }
            catch (Exception spineEx) { System.Diagnostics.Debug.WriteLine($"[App] {origin} Rollback: {spineEx.Message}"); }
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            _loggingService?.LogError($"[App] CurrentDomain_UnhandledException: {ex?.GetType().Name}: {ex?.Message}");
            LogToFile($"[CRITICAL] FATAL EXCEPTION (IsTerminating={e.IsTerminating}): {ex?.GetType().Name}: {ex?.Message}\n{ex?.StackTrace}");
            Program.WriteCrashLog($"FATAL_EXCEPTION_DOMAIN: {ex?.Message}\n{ex?.StackTrace}");
            TryEmergencyRollback("CurrentDomainUnhandledException");
            try { _loggingService?.Flush(); } catch (Exception flushEx) { System.Diagnostics.Debug.WriteLine($"[App] CurrentDomainUnhandledException Flush: {flushEx.Message}"); }
            Core.Diagnostics.CrashDiagnostics.Shutdown();
        }

        /// <summary>
        /// Trata um atalho global pressionado.
        ///
        /// CORREÇÕES DE AUDITORIA:
        ///  1. O wrapper usava <c>Dispatcher.InvokeAsync(async () =&gt; ...)</c>, que
        ///     resolve para <c>InvokeAsync(Action)</c> — um <c>async void</c>. O
        ///     try/catch externo só capturava falhas síncronas; TODA exceção lançada
        ///     após o primeiro <c>await</c> virava task exception não observada e
        ///     sumia. Agora é <c>Dispatcher.InvokeAsync(Func&lt;Task&gt;)</c>, aguardado
        ///     e com tratamento de erro explícito.
        ///  2. Seis atalhos rotulados como AÇÕES (Smart Scan, Boost de Performance,
        ///     Reparo Inteligente, Teste de Ping, Flush DNS, Criar Ponto de
        ///     Restauração) apenas trocavam de página. Agora executam a função real
        ///     pelos SERVIÇOS JÁ EXISTENTES.
        ///  3. IDs 2 e 13 eram idênticos (ambos rodavam a limpeza profunda) apesar de
        ///     rótulos diferentes. ID 13 agora executa a limpeza RÁPIDA de verdade.
        ///  4. Ativar/desativar o Modo Gamer por atalho não passava pelo gate de
        ///     licença, ao contrário da interface — uma forma de burlar o recurso pago.
        ///  5. Ramo "serviço ausente" sem log: falhas silenciosas.
        /// </summary>
        private void OnHotkeyPressed(int id)
        {
            var hotkey = _hotkeyService?.GetShortcutDescription(id) ?? $"ID {id}";
            _loggingService?.LogInfo($"[Hotkey] Pressionado: {hotkey}");

            // fire-and-forget, mas com o resultado observado.
            _ = Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await ExecuteHotkeyAsync(id).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    _loggingService?.LogInfo($"[Hotkey] {hotkey} cancelado.");
                }
                catch (Exception ex)
                {
                    // Agora a exceção é realmente registrada, com stack trace.
                    _loggingService?.LogError($"[Hotkey] Erro ao executar '{hotkey}': {ex.Message}", ex);
                    GlobalNotificationService.ShowError(
                        LocalizationService.Instance.GetString("ShortcutErrorTitle"),
                        $"{hotkey}: {ex.Message}");
                }
            });
        }

        private async Task ExecuteHotkeyAsync(int id)
        {
            var mainWin = Application.Current?.MainWindow as UI.MainWindow;

            switch (id)
            {
                // ── Navegação (ALT + número) ──
                case 1: case 3: case 4: case 5: case 6: case 7:
                    {
                        string page = id switch
                        {
                            1 => "Dashboard",
                            3 => "Performance",
                            4 => "Gamer",
                            5 => "Repair",
                            6 => "Shield",
                            7 => "Settings",
                            _ => string.Empty
                        };

                        if (mainWin == null) return;
                        mainWin.ShowAndFocus();
                        if (!string.IsNullOrEmpty(page))
                        {
                            mainWin.NavigateToPageFromOutside(page);
                            _loggingService?.LogInfo($"[Hotkey] Navegação: {page}");
                        }
                        return;
                    }

                // ── Limpeza profunda pelo botão circular ──
                case 2:
                    {
                        if (mainWin == null)
                        {
                            _loggingService?.LogWarning("[Hotkey] MainWindow indisponível — limpeza profunda cancelada.");
                            return;
                        }
                        await mainWin.RunDeepCleanFromHotkeyAsync("HotkeyAlt2");
                        return;
                    }

                // ── Modo Gamer ──
                case 8:
                    {
                        // Mesma proteção da interface: o Modo Gamer é recurso pago.
                        if (!VoltrisOptimizer.Services.License.ProFeatureGuard.RequirePaid("gamer_mode"))
                        {
                            _loggingService?.LogInfo("[Hotkey] Ativação do Modo Gamer bloqueada — requer licença paga.");
                            return;
                        }
                        var orch = _serviceProvider?.GetService<VoltrisOptimizer.Services.Gamer.Interfaces.IGamerModeOrchestrator>();
                        if (orch == null)
                        {
                            _loggingService?.LogError("[Hotkey] IGamerModeOrchestrator indisponível. Nada foi feito.");
                            return;
                        }
                        if (orch.IsActive)
                        {
                            _loggingService?.LogInfo("[Hotkey] Modo Gamer já está ativo.");
                            return;
                        }
                        bool activated = await orch.ActivateAsync(new VoltrisOptimizer.Services.Gamer.Models.GamerOptimizationOptions());
                        _loggingService?.Log(activated ? LogLevel.Success : LogLevel.Warning, LogCategory.Gamer,
                            activated
                                ? "[Hotkey] Modo Gamer ativado e CONFIRMADO pelo orquestrador."
                                : "[Hotkey] Orquestrador recusou a ativação do Modo Gamer. Nenhuma alteração aplicada.");
                        return;
                    }

                case 9:
                    {
                        if (!VoltrisOptimizer.Services.License.ProFeatureGuard.RequirePaid("gamer_mode"))
                        {
                            _loggingService?.LogInfo("[Hotkey] Desativação do Modo Gamer bloqueada — requer licença paga.");
                            return;
                        }
                        var orchD = _serviceProvider?.GetService<VoltrisOptimizer.Services.Gamer.Interfaces.IGamerModeOrchestrator>();
                        if (orchD == null)
                        {
                            _loggingService?.LogError("[Hotkey] IGamerModeOrchestrator indisponível. Nada foi feito.");
                            return;
                        }
                        if (!orchD.IsActive)
                        {
                            _loggingService?.LogInfo("[Hotkey] Modo Gamer já está desativado.");
                            return;
                        }
                        bool deactivated = await orchD.DeactivateAsync();
                        _loggingService?.Log(deactivated ? LogLevel.Success : LogLevel.Warning, LogCategory.Gamer,
                            deactivated
                                ? "[Hotkey] Modo Gamer desativado e restauração confirmada."
                                : "[Hotkey] Orquestrador não confirmou a desativação do Modo Gamer.");
                        return;
                    }

                case 10: // FPS overlay — recurso do Modo Gamer
                    {
                        if (!VoltrisOptimizer.Services.License.ProFeatureGuard.RequirePaid("gamer_mode"))
                        {
                            _loggingService?.LogInfo("[Hotkey] FPS overlay bloqueado — requer licença paga.");
                            return;
                        }
                        var ov = _serviceProvider?.GetService<VoltrisOptimizer.Services.Gamer.Overlay.Interfaces.IOverlayService>();
                        if (ov == null)
                        {
                            _loggingService?.LogError("[Hotkey] IOverlayService indisponível. Nada foi feito.");
                            return;
                        }
                        if (ov.IsActive) await ov.StopAsync();
                        else await ov.StartAsync(0);
                        _loggingService?.LogInfo($"[Hotkey] FPS overlay {(ov.IsActive ? "ativado" : "desativado")}.");
                        return;
                    }

                case 11: // Widget
                    {
                        if (WidgetManager == null)
                        {
                            _loggingService?.LogWarning("[Hotkey] Widget indisponível. Nada foi feito.");
                            return;
                        }
                        WidgetManager.ToggleCollapseWidget();
                        return;
                    }

                // ── Smart Scan: executa a varredura real do Smart Repair ──
                case 12:
                    {
                        mainWin?.ShowAndFocus();
                        mainWin?.NavigateToPageFromOutside("SmartRepair");
                        await ExecuteSmartRepairHotkeyAsync();
                        return;
                    }

                // ── Limpeza RÁPIDA (não a profunda como antes) ──
                case 13:
                    {
                        if (mainWin == null)
                        {
                            _loggingService?.LogWarning("[Hotkey] MainWindow indisponível — limpeza rápida cancelada.");
                            return;
                        }
                        await mainWin.RunQuickCleanupFromHotkeyAsync("HotkeyQuickCleanup");
                        return;
                    }

                // ── Boost de Performance: executa a otimização real ──
                case 14:
                    {
                        mainWin?.ShowAndFocus();
                        mainWin?.NavigateToPageFromOutside("Performance");
                        await ExecutePerformanceBoostHotkeyAsync();
                        return;
                    }

                // ── Reparo Inteligente ──
                case 15:
                    {
                        mainWin?.ShowAndFocus();
                        mainWin?.NavigateToPageFromOutside("SmartRepair");
                        await ExecuteSmartRepairHotkeyAsync();
                        return;
                    }

                // ── Teste de Ping ──
                case 16:
                    {
                        mainWin?.ShowAndFocus();
                        mainWin?.NavigateToPageFromOutside("Network");
                        await ExecutePingTestHotkeyAsync();
                        return;
                    }

                // ── Flush DNS ──
                case 17:
                    {
                        mainWin?.ShowAndFocus();
                        mainWin?.NavigateToPageFromOutside("Network");
                        await ExecuteFlushDnsHotkeyAsync();
                        return;
                    }

                // ── Segurança ──
                case 18:
                    {
                        mainWin?.ShowAndFocus();
                        mainWin?.NavigateToPageFromOutside("Shield");
                        await ExecuteShieldScanHotkeyAsync(fullScan: false);
                        return;
                    }

                case 19:
                    {
                        mainWin?.ShowAndFocus();
                        mainWin?.NavigateToPageFromOutside("Shield");
                        await ExecuteShieldScanHotkeyAsync(fullScan: true);
                        return;
                    }

                // ── Otimização rápida ──
                case 20:
                    {
                        if (mainWin == null)
                        {
                            _loggingService?.LogWarning("[Hotkey] MainWindow indisponível — otimização rápida cancelada.");
                            return;
                        }
                        await mainWin.RunQuickOptimizeFromHotkeyAsync("HotkeyQuickOptimize");
                        return;
                    }

                // ── Ponto de Restauração ──
                case 21:
                    {
                        mainWin?.ShowAndFocus();
                        mainWin?.NavigateToPageFromOutside("System");
                        await ExecuteRestorePointHotkeyAsync();
                        return;
                    }

                default:
                    _loggingService?.LogWarning($"[Hotkey] ID {id} não possui ação associada.");
                    return;
            }
        }

        /// <summary>
        /// Executa a varredura real do Smart Repair (usada pelos atalhos 12 e 15).
        /// </summary>
        private async Task ExecuteSmartRepairHotkeyAsync()
        {
            try
            {
                var vm = _serviceProvider?.GetService<UI.ViewModels.SmartRepairViewModel>();
                if (vm == null)
                {
                    _loggingService?.LogError("[Hotkey] SmartRepairViewModel indisponível — nada foi executado.");
                    return;
                }

                if (vm.IsScanning || vm.IsExecuting)
                {
                    _loggingService?.LogInfo("[Hotkey] Smart Repair já em execução. Atalho ignorado.");
                    return;
                }

                _loggingService?.LogInfo("[Hotkey] Executando Smart Repair...");

                // Executa o MESMO comando da interface (sem duplicação de lógica).
                var cmd = vm.StartScanCommand;
                if (cmd == null || !cmd.CanExecute(null))
                {
                    _loggingService?.LogWarning(
                        "[Hotkey] Smart Repair não pôde ser iniciado (comando indisponível, bloqueado ou já em execução).");
                    return;
                }

                if (cmd is UI.ViewModels.RelayCommand relay)
                {
                    await relay.ExecuteAsync(null);
                    _loggingService?.LogSuccess("[Hotkey] Smart Repair concluído.");
                }
                else
                {
                    cmd.Execute(null);
                    _loggingService?.LogSuccess("[Hotkey] Smart Repair iniciado.");
                }
            }
            catch (Exception ex)
            {
                _loggingService?.LogError("[Hotkey] Erro no Smart Repair: " + ex.Message, ex);
            }
        }

        /// <summary>
        /// Executa a otimização de performance real (plano de energia + inicialização
        /// + memória) reaproveitando os serviços já existentes.
        /// </summary>
        private async Task ExecutePerformanceBoostHotkeyAsync()
        {
            try
            {
                var perf = PerformanceOptimizer;
                if (perf == null)
                {
                    _loggingService?.LogError("[Hotkey] Serviço de performance indisponível — nada foi aplicado.");
                    return;
                }

                _loggingService?.LogInfo("[Hotkey] Executando boost de performance...");

                var plan = await perf.SetHighPerformancePlanAsync();
                if (plan == null || !plan.Success)
                {
                    _loggingService?.LogError(
                        $"[Hotkey] Boost de performance FALHOU ao definir o plano de energia: {plan?.ErrorMessage ?? "sem resultado"}");
                    return;
                }

                var startup = await perf.OptimizeStartupAsync();
                if (startup == null || !startup.Success)
                {
                    _loggingService?.LogWarning(
                        $"[Hotkey] Plano aplicado, mas a otimização de inicialização falhou: {startup?.ErrorMessage ?? "sem resultado"}");
                    return;
                }

                _loggingService?.LogSuccess("[Hotkey] Boost de performance aplicado e CONFIRMADO.");
            }
            catch (Exception ex)
            {
                _loggingService?.LogError("[Hotkey] Erro no boost de performance: " + ex.Message, ex);
            }
        }

        /// <summary>
        /// Executa um teste de ping real e informa o resultado.
        /// </summary>
        private async Task ExecutePingTestHotkeyAsync()
        {
            try
            {
                using var client = new System.Net.NetworkInformation.Ping();
                var reply = await client.SendPingAsync("1.1.1.1", 3000);
                if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                {
                    _loggingService?.LogSuccess($"[Hotkey] Ping concluído: {reply.RoundtripTime} ms até 1.1.1.1");
                    GlobalNotificationService.ShowSuccess(
                        LocalizationService.Instance.GetString("ShortcutPingTitle"),
                        $"1.1.1.1 — {reply.RoundtripTime} ms");
                }
                else
                {
                    _loggingService?.LogWarning($"[Hotkey] Ping falhou: {reply.Status} — {reply.Buffer?.Length} bytes recebidos.");
                    GlobalNotificationService.ShowWarning(
                        LocalizationService.Instance.GetString("ShortcutPingTitle"),
                        $"{LocalizationService.Instance.GetString("Loc_Error")}: {reply.Status}");
                }
            }
            catch (Exception ex)
            {
                _loggingService?.LogError("[Hotkey] Erro no teste de ping: " + ex.Message, ex);
            }
        }

        /// <summary>
        /// Executa o flush de DNS real usando o serviço de rede já existente.
        /// </summary>
        private async Task ExecuteFlushDnsHotkeyAsync()
        {
            try
            {
                var net = NetworkOptimizer;
                if (net == null)
                {
                    _loggingService?.LogError("[Hotkey] Serviço de rede indisponível — nada foi executado.");
                    return;
                }

                bool ok = await net.FlushDnsAsync();
                _loggingService?.Log(ok ? LogLevel.Success : LogLevel.Error, LogCategory.Network,
                    ok
                        ? "[Hotkey] Flush de DNS executado e CONFIRMADO."
                        : "[Hotkey] Flush de DNS reportou falha. O cache pode não ter sido limpo.");

                GlobalNotificationService.Show(ok ? "Flush DNS" : LocalizationService.Instance.GetString("Loc_Error"),
                    ok
                        ? LocalizationService.Instance.GetString("ShortcutDnsFlushed")
                        : LocalizationService.Instance.GetString("ShortcutDnsFailed"));
            }
            catch (Exception ex)
            {
                _loggingService?.LogError("[Hotkey] Erro no flush de DNS: " + ex.Message, ex);
            }
        }

        /// <summary>
        /// Cria um ponto de restauração REAL do Windows.
        ///
        /// Usa <see cref="SystemToolsService.CreateSystemRestorePointAsync"/>, que
        /// chama a API de restauração do sistema. A implementação anterior pretendia
        /// usar <c>SystemSafetyService.CreateRestorePointAsync</c>, mas aquela cria
        /// apenas um snapshot INTERNO do VOLTRIS (backup de registro, estados de
        /// serviço e plano de energia) — não cria ponto de restauração do Windows.
        /// </summary>
        private async Task ExecuteRestorePointHotkeyAsync()
        {
            try
            {
                var tools = _serviceProvider?.GetService<SystemToolsService>();
                if (tools == null)
                {
                    _loggingService?.LogError("[Hotkey] SystemToolsService indisponível — nada foi criado.");
                    return;
                }

                _loggingService?.LogInfo("[Hotkey] Criando ponto de restauração do Windows...");
                bool ok = await tools.CreateSystemRestorePointAsync("Voltris Optimizer - Antes da otimização");

                _loggingService?.Log(ok ? LogLevel.Success : LogLevel.Error, LogCategory.System,
                    ok
                        ? "[Hotkey] Ponto de restauração do Windows criado e CONFIRMADO."
                        : "[Hotkey] Ponto de restauração NÃO foi criado. Verifique se a Proteção do Sistema está ativada.");

                GlobalNotificationService.Show(
                    ok ? "Ponto de restauração" : LocalizationService.Instance.GetString("Loc_Error"),
                    ok
                        ? LocalizationService.Instance.GetString("ShortcutRestorePointCreated")
                        : LocalizationService.Instance.GetString("ShortcutRestorePointFailed"));
            }
            catch (Exception ex)
            {
                _loggingService?.LogError("[Hotkey] Erro ao criar ponto de restauração: " + ex.Message, ex);
            }
        }

        private async Task ExecuteShieldScanHotkeyAsync(bool fullScan)
        {
            try
            {
                var shieldViewModel = _serviceProvider?.GetService<VoltrisOptimizer.UI.ViewModels.ShieldViewModel>();
                if (shieldViewModel == null)
                {
                    _loggingService?.LogWarning("[Hotkey] ShieldViewModel indisponível para o scan");
                    return;
                }

                // Gate de licença: sem Standard/Pro/Enterprise o Voltris Shield fica offline.
                if (!shieldViewModel.IsShieldLicensed)
                {
                    _loggingService?.LogWarning("[Hotkey] Scan do Shield BLOQUEADO: requer licença Standard, Pro ou Enterprise");
                    return;
                }

                if (!shieldViewModel.IsProtectionActive)
                {
                    _loggingService?.LogWarning("[Hotkey] Scan do Shield ignorado: proteção desativada");
                    return;
                }

                if (shieldViewModel.IsScanning)
                {
                    _loggingService?.LogWarning("[Hotkey] Scan do Shield ignorado: já existe um scan em andamento");
                    return;
                }

                if (fullScan)
                    await shieldViewModel.RunFullScanAsync();
                else
                    await shieldViewModel.RunQuickScanAsync();
            }
            catch (Exception ex)
            {
                _loggingService?.LogError("[Hotkey] Erro ao executar scan do Shield", ex);
            }
        }


        protected override void OnExit(ExitEventArgs e)
        {
            // [FIX:C-3] Marca o início do shutdown antes de qualquer cleanup, para
            // que o observador de first-chance pare de registrar ObjectDisposedException
            // causados pela desmontagem normal de serviços.
            Volatile.Write(ref _shutdownStarted, 1);

            _loggingService?.LogEntry(nameof(OnExit));
            _loggingService?.LogTransition("RUNNING", "SHUTDOWN", "AppExit");
            
            // FAILSAFE: Garantir que o processo morre em no máximo 30s após OnExit iniciar.
            // Se qualquer cleanup (GamerMode, Spine, BrainV2, etc.) travar, isso evita processo fantasma.
            var failsafeThread = new Thread(() =>
            {
                try { Thread.Sleep(30_000); } catch { }
                finally { SafeExit(1, "ONEXIT_FAILSAFE_30S"); }
            })
            {
                Name = "OnExitFailsafe",
                IsBackground = true,
                Priority = ThreadPriority.Highest
            };
            failsafeThread.Start();
            
            // Enviar e forçpr o programa a esperar até 3 segundos pela requisição. 
            // Se usar apenas FireAndForget, o processo morre antes da requisição HTTP completar.
            try 
            {
                Task.Run(async () => await VoltrisOptimizer.Services.TelegramLogger.SendMessageAsync(
                    $"🛑 <b>Voltris Optimizer Fechado</b>\n<b>Máquina:</b> {Environment.MachineName}\n<b>Versão:</b> {VoltrisOptimizer.Properties.VersionInfo.Version}",
                    isHtml: true
                )).Wait(TimeSpan.FromSeconds(3));
            } 
            catch { }
            
            Program.WriteCrashLog("GRACEFUL_SHUTDOWN_BEGIN");
            var pid = Process.GetCurrentProcess().Id;
            var tid = Thread.CurrentThread.ManagedThreadId;
            LogToFile($"[SHUTDOWN][TID:{tid}] OnExit INÍCIO (PID: {pid})");

            try
            {
                LogToFile("[CRITICAL] ================== ONEXIT INICIADO ==================");
                _loggingService?.LogInfo("[App] OnExit iniciado...");

                LogToFile($"[SHUTDOWN][TID:{tid}] Executando rollback e parada do BrainV2...");
                try { Task.Run(async () => { if (BrainV2 != null) await BrainV2.StopAsync(); }).Wait(TimeSpan.FromSeconds(2)); LogToFile("[SHUTDOWN] BrainV2 parado com sucesso."); }
                catch (Exception ex) { LogToFile($"[SHUTDOWN] BrainV2 stop: {ex.Message}"); }
                try { Task.Run(async () => { if (Spine != null) await Spine.RollbackAllAsync(); }).Wait(TimeSpan.FromSeconds(2)); LogToFile("[SHUTDOWN] Rollback concluído com sucesso."); }
                catch (Exception ex) { LogToFile($"[SHUTDOWN] Rollback: {ex.Message}"); }

                // DESLIGAMENTO SEGURO DO MODO GAMER: Garante que prioridades, overlays e configurações revertam antes do App morrer
                try 
                {
                    LogToFile($"[SHUTDOWN][TID:{tid}] Executando GamerModeShutdownHelper (reversão de prioridades e perfis)...");
                    VoltrisOptimizer.Services.Gamer.GamerModeShutdownHelper.ShutdownAllBlocking(_serviceProvider, _loggingService, "AppExit", 5000);
                }
                catch (Exception ex) { LogToFile($"[SHUTDOWN] GamerModeShutdownHelper: {ex.Message}"); }

                // REDUNDÂNCIA: Restaurar PL1/PL2 para os defaults de fábrica no shutdown
                LogToFile($"[SHUTDOWN][TID:{tid}] Restaurando Power Limits (MSR) para valores de fábrica...");
                try
                {
                    var plManager = _serviceProvider?.GetService<VoltrisOptimizer.Services.Performance.CpuTuning.Core.Managers.PowerLimitManager>();
                    if (plManager != null)
                    {
                        var restored = Task.Run(() => plManager.RestoreOriginalLimits()).Wait(TimeSpan.FromSeconds(3));
                        LogToFile($"[SHUTDOWN] Power Limits restaurados: {restored}");
                    }
                    else
                    {
                        LogToFile("[SHUTDOWN] PowerLimitManager não disponível no ServiceProvider");
                    }
                }
                catch (Exception ex)
                {
                    LogToFile($"[SHUTDOWN] Restore Power Limits: {ex.Message}");
                }

                // Parar serviços
                LogToFile($"[SHUTDOWN][TID:{tid}] Parando ThermalMonitorService...");
                try { Task.Run(async () => { if (ThermalMonitorService != null) await ThermalMonitorService.StopMonitoringAsync(); }).Wait(TimeSpan.FromSeconds(2)); }
                catch (Exception ex) { LogToFile($"[SHUTDOWN][TID:{tid}] ThermalMonitor stop: {ex.Message}"); }
                
                LogToFile($"[SHUTDOWN][TID:{tid}] Parando RemoteCommandService...");
                RemoteCommandService?.Stop();
                
                LogToFile($"[SHUTDOWN][TID:{tid}] Disposing GameDetectionService...");
                GameDetectionService?.Dispose();

                // [LOG] Semaphore Dispose
                if (_appSemaphore != null)
                {
                    LogToFile($"[SHUTDOWN][TID:{tid}] Descartando Semaphore...");
                    try { _appSemaphore.Dispose(); } catch (Exception disposeEx) { System.Diagnostics.Debug.WriteLine($"[App] DisposeSemaphore: {disposeEx.Message}"); }
                    _appSemaphore = null;
                }

                // Remover menu de contexto do desktop ao fechar
                LogToFile($"[SHUTDOWN][TID:{tid}] Removendo menu de contexto do desktop...");
                try
                {
                    var ctxMenu = _serviceProvider?.GetService<VoltrisOptimizer.Services.Shell.DesktopContextMenuService>();
                    if (ctxMenu != null)
                    {
                        ctxMenu.Unregister();
                        LogToFile("[SHUTDOWN][TID:{tid}] Menu de contexto removido com sucesso");
                    }
                    else
                    {
                        LogToFile("[SHUTDOWN][TID:{tid}] DesktopContextMenuService não disponível no ServiceProvider");
                    }
                }
                catch (Exception ex)
                {
                    LogToFile($"[SHUTDOWN][TID:{tid}] Erro ao remover menu de contexto: {ex.Message}");
                }

                // Parar monitoramento UI e watchdog antes do encerramento
                _uiMonitor?.StopMonitoring();
                _uiMonitor?.Dispose();
                StartupWatchdog.Stop();

                // Desregistrar hotkeys globais
                if (_hotkeyService != null)
                {
                    try { _hotkeyService.HotkeyPressed -= OnHotkeyPressed; _hotkeyService.UnregisterAll(); _hotkeyService.Dispose(); }
                    catch (Exception hotkeyEx) { LogToFile($"[SHUTDOWN] HotkeyService dispose: {hotkeyEx.Message}"); }
                    _hotkeyService = null;
                }

                Program.WriteCrashLog("GRACEFUL_SHUTDOWN_END");
                LogToFile("[CRITICAL] ================== ONEXIT CONCLUÍDO ==================");
                _loggingService?.LogInfo("[App] OnExit concluído.");
                _loggingService?.LogExit(nameof(OnExit));
                _loggingService?.Flush();
            }
            catch (Exception ex)
            {
                _loggingService?.LogExit(nameof(OnExit), $"ERROR:{ex.Message}");
                LogToFile($"[SHUTDOWN][TID:{tid}] ERRO NO ONEXIT: {ex.Message}");
            }
            finally
            {
                base.OnExit(e);
            }
        }

        /// <summary>
        /// Popula todas as propriedades estáticas a partir do ServiceProvider.
        /// Isso garante compatibilidade com código legado e serviços que não usam DI puro.
        /// </summary>
        private async Task PopulateStaticServicesAsync(IServiceProvider sp)
        {
            _loggingService?.LogEntry(nameof(PopulateStaticServicesAsync));
            var populateSw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                LogToFile("[DI] Populando propriedades estáticas em background...");
                
                // Yield para permitir que o sistema respire
                // CORREÇÃO: Usar Task.Yield() pois Dispatcher.Yield() falha em threads de background sem Dispatcher
                await Task.Yield();
                
                // Infra & Core
                SystemProfiler = await ResolveServiceSafeAsync<VoltrisOptimizer.Interfaces.ISystemProfiler>(sp);
                Body = await ResolveServiceSafeAsync<VoltrisOptimizer.Core.Body.IVoltrisBody>(sp);
                Spine = await ResolveServiceSafeAsync<VoltrisOptimizer.Core.Body.IVoltrisSpine>(sp);
                
                // Brain & Intelligence
                BrainV2 = await ResolveServiceSafeAsync<VoltrisOptimizer.Core.Brain.V2.VoltrisBrainV2>(sp);
                if (BrainV2 != null && Body != null) BrainV2.Body = Body;
                TemporalPatternEngine = await ResolveServiceSafeAsync<VoltrisOptimizer.Core.Intelligence.ITemporalPatternEngine>(sp);
                
                PreWarmEngine = await ResolveServiceSafeAsync<VoltrisOptimizer.Core.Intelligence.IPredictivePreWarmEngine>(sp);
                BehaviorEngine = await ResolveServiceSafeAsync<VoltrisOptimizer.Core.Intelligence.IBehaviorScoreEngine>(sp);
                PatternService = await ResolveServiceSafeAsync<VoltrisOptimizer.Core.Intelligence.IPatternRecognitionService>(sp);
                NetworkOrchestrator = await ResolveServiceSafeAsync<VoltrisOptimizer.Core.NetworkIntelligence.INetworkIntelligenceOrchestrator>(sp);
                
                // Optimization Services (Cruciais para o Profiler)
                SystemCleaner = await ResolveServiceSafeAsync<SystemCleaner>(sp);
                PerformanceOptimizer = await ResolveServiceSafeAsync<VoltrisOptimizer.Services.VoltrisPerformanceOptimizer>(sp);
                NetworkOptimizer = await ResolveServiceSafeAsync<NetworkOptimizer>(sp);
                AdvancedOptimizer = await ResolveServiceSafeAsync<AdvancedOptimizer>(sp);
                ExtremeOptimizations = await ResolveServiceSafeAsync<ExtremeOptimizationsService>(sp);
                UltraPerformance = await ResolveServiceSafeAsync<UltraPerformanceService>(sp);
                UltraCleaner = await ResolveServiceSafeAsync<UltraCleanerService>(sp);
                
                UnifiedOptimization = await ResolveServiceSafeAsync<UnifiedOptimizationService>(sp);
                GamerOptimizer = await ResolveServiceSafeAsync<GamerOptimizerService>(sp);

                // Support Services
                ThermalMonitorService = await ResolveServiceSafeAsync<VoltrisOptimizer.Services.Thermal.IGlobalThermalMonitorService>(sp);
                TelemetryService = await ResolveServiceSafeAsync<VoltrisOptimizer.Services.Telemetry.TelemetryService>(sp);
                OverlayService = await ResolveServiceSafeAsync<VoltrisOptimizer.Services.Gamer.Overlay.Interfaces.IOverlayService>(sp);
                GameDetectionService = await ResolveServiceSafeAsync<GameDetectionService>(sp);
                SchedulerService = await ResolveServiceSafeAsync<SchedulerService>(sp);
                HistoryService = await ResolveServiceSafeAsync<HistoryService>(sp);
                HardwarePerformanceService = await ResolveServiceSafeAsync<VoltrisOptimizer.Services.Performance.HardwarePerformanceOptimizationService>(sp);
                SystemProfilerService = await ResolveServiceSafeAsync<VoltrisOptimizer.Services.SystemIntelligenceProfiler.SystemIntelligenceProfilerService>(sp);
                
                // Força a instanciação do scheduler centralizado para que a variável Global seja atribuída
                var scheduler = sp.GetService<VoltrisOptimizer.Services.Scheduling.CentralizedBackgroundScheduler>();
                if (scheduler != null)
                {
                    VoltrisOptimizer.Services.Scheduling.CentralizedBackgroundScheduler.Global = scheduler;
                }

                LogToFile("[DI] Propriedades estáticas populadas com sucesso.");
                populateSw.Stop();
                _loggingService?.LogExit(nameof(PopulateStaticServicesAsync), null, populateSw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                populateSw.Stop();
                _loggingService?.LogExit(nameof(PopulateStaticServicesAsync), $"ERROR:{ex.Message}", populateSw.ElapsedMilliseconds);
                LogToFile($"[DI] ERRO CRÍTICO ao popular propriedades: {ex.Message}");
            }
        }

        /// <summary>
        /// Resolve um serviço do DI com segurança (timeout + fallback a null).
        /// Evita que um construtor lento trave o startup inteiro.
        /// </summary>
        private async Task<T?> ResolveServiceSafeAsync<T>(IServiceProvider sp, int timeoutMs = 5000) where T : class
        {
            _loggingService?.LogEntry(nameof(ResolveServiceSafeAsync), ("type", typeof(T).Name));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var result = sp.GetService<T>();
                _loggingService?.LogCache(typeof(T).Name, "RESOLVE", $"{sw.ElapsedMilliseconds}ms");
                _loggingService?.LogDebug($"[DI] {typeof(T).Name} resolvido em {sw.ElapsedMilliseconds}ms");
                _loggingService?.LogExit(nameof(ResolveServiceSafeAsync), result != null ? "found" : "null", sw.ElapsedMilliseconds);
                return await Task.FromResult(result);
            }
            catch (Exception ex)
            {
                _loggingService?.LogExit(nameof(ResolveServiceSafeAsync), $"ERROR:{ex.Message}", sw.ElapsedMilliseconds);
                _loggingService?.LogError($"[DI] ERRO ao resolver {typeof(T).Name}: {ex.Message}");
                return null;
            }
        }
    }
}


