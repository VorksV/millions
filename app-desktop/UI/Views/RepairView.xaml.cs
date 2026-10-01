using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Hardware;
using VoltrisOptimizer.UI.Controls;
using VoltrisOptimizer.UI.Windows;
using VoltrisOptimizer.Utils;

namespace VoltrisOptimizer.UI.Views
{
    public partial class RepairView : UserControl
    {
        private readonly ILoggingService _loggingService;
        private readonly ObservableCollection<LogEntry> _logs = new();
        private bool _isRepairRunning;
        private CancellationTokenSource? _cts;
        private CancellationTokenSource? _defragCts;
        private bool _isDefragRunning;
        private static readonly SemaphoreSlim _defragLock = new(1, 1);

        public RepairView()
        {
            InitializeComponent();

            // Registrar CodePages para suportar encoding OEM (ex: 850, 437)
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            var logDirectory = LogDirectoryResolver.Resolve();
            _loggingService = App.LoggingService ?? new LoggingService(logDirectory);

            LogViewerItems.ItemsSource = _logs;
            Loaded += OnLoaded;

            if (App.LoggingService is LoggingService logSvc)
            {
                logSvc.LogEntryAdded += (s, msg) => Dispatcher.BeginInvoke(() =>
                {
                    if (msg.Contains("[Repair]") || msg.Contains("[DISM]") || msg.Contains("[SFC]") ||
                        msg.Contains("[CHKDSK]") || msg.Contains("[PreparePc]") || msg.Contains("[AdvancedRepair]"))
                    {
                        var color = Colors.Gray;
                        if (msg.Contains("SUCCESS")) color = Colors.LightGreen;
                        if (msg.Contains("WARNING")) color = Colors.Orange;
                        if (msg.Contains("ERROR")) color = Colors.Red;
                        AddLog(msg, color);
                    }
                });
            }
        }

        private string Loc(string key) => LocalizationService.Instance.GetString(key);

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            await LoadSystemInfoAsync();
            Log(Loc("RepairView_LoadedSystem"), Colors.Cyan);
            Log(Loc("RepairView_LoadedReady"), Colors.White);
        }

        private async Task LoadSystemInfoAsync()
        {
            try
            {
                await Task.Run(() =>
                {
                    string osName = "Windows";
                    var os = Environment.OSVersion;
                    if (os.Version.Major == 10)
                        osName = os.Version.Build >= 22000 ? "Windows 11" : "Windows 10";
                    Dispatcher.Invoke(() => OsVersionText.Text = $"{osName} ({os.Version.Build})");
                });
            }
            catch { }
        }

        private void Log(string message, Color color)
        {
            AddLog($"[{DateTime.Now:HH:mm:ss}] {message}", color);
        }

        private void AddLog(string message, Color color)
        {
            _logs.Add(new LogEntry { Message = message, Color = new SolidColorBrush(color) });
            if (_logs.Count > 200) _logs.RemoveAt(0);
            LogScrollViewer.ScrollToEnd();
        }

        private void SetBusy(bool busy, string status)
        {
            _isRepairRunning = busy;
            RepairStatusText.Text = status;
            RepairStatusText.Foreground = busy
                ? new SolidColorBrush(Color.FromRgb(251, 191, 36))
                : new SolidColorBrush(Color.FromRgb(16, 185, 129));
            FullRepairButton.IsEnabled = !busy;
            RepairSystemButton.IsEnabled = !busy;
        }

        private bool ShowBusyModal()
        {
            if (!_isRepairRunning) return false;
            ModernMessageBox.Show(
                Loc("RepairView_BusyModalText"),
                Loc("RepairView_BusyModalTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return true;
        }

        // ═══════════════════════════════════════════════════════════
        // BOTÃO: REPARO COMPLETO ULTRA AVANÇADO (20 etapas)
        // ═══════════════════════════════════════════════════════════
        private async void FullRepairButton_Click(object sender, RoutedEventArgs e)
        {
            if (ShowBusyModal()) return;

            var result = ModernMessageBox.Show(
                Loc("RepairView_FullRepairWarningText"),
                Loc("RepairView_FullRepairWarningTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            if (!AdminHelper.IsRunningAsAdministrator())
            {
                ModernMessageBox.Show(
                    Loc("RepairView_AdminRequired"),
                    Loc("RepairView_AdminRequiredTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _cts = new CancellationTokenSource();
            SetBusy(true, Loc("RepairView_FullRepairInProgress"));
            
            var op = ProgressTrackingService.Instance.StartOperation(Loc("RepairView_FullRepairOp"));
            ProgressTrackingService.Instance.RegisterSubTasks(op.Id,
                ("Ponto de Restauração", 2),
                ("Reset de Rede", 6),
                (".NET Framework", 6),
                ("Visual C++", 4),
                ("Windows Store", 5),
                ("Boot Manager", 5),
                ("Serviços do Windows", 5),
                ("Registro", 5),
                ("Limpeza de Cache", 6),
                ("Windows Defender", 5),
                ("Drivers", 5),
                ("BSOD Analysis", 4),
                ("Event Viewer", 4),
                ("Print Spooler", 3),
                ("DirectX + Áudio", 4),
                ("Perfil de Usuário", 3),
                ("DISM + SFC", 14),
                ("Diagnóstico Inteligente", 6),
                ("Corrigir Erros nos Jogos", 4),
                ("Fix Barra de Pesquisa", 1),
                ("Fix Painel de Controle", 0),
                ("Fix CPU Alta", 1),
                ("Windows Update", 1),
                ("Relatório HTML", 1)
            );
            
            App.TelemetryService?.TrackEvent("FULL_REPAIR_ULTRA", "Repair", "Start", forceFlush: true);

            // ✅ FIX BUG #3: Flag local de concluso bem-sucedida (substitui _isRepairRunning que nunca era true)
            bool repairCompletedSuccessfully = false;

            // DEBUG: Início do reparo completo com timestamp
            _loggingService.LogDebug($"[Repair][FullRepair] INÍCIO às {DateTime.Now:HH:mm:ss.fff} — {Environment.OSVersion} | {Environment.MachineName}", source: "RepairView");

            var report = new AdvancedRepairReport { StartTime = DateTime.Now };
            var svc = new AdvancedRepairService(_loggingService);

            // Conectar callbacks do serviço à UI
            svc.OnLog = (msg, hexColor) =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    var color = Colors.Gray;
                    try { color = (Color)ColorConverter.ConvertFromString(hexColor); } catch { }
                    Log(msg, color);
                });
            };
            
            string currentSubTask = "";
            svc.OnProgress = (pct, msg) => 
            {
                if (!string.IsNullOrEmpty(currentSubTask))
                    ProgressTrackingService.Instance.UpdateSubTaskProgress(op.Id, currentSubTask, pct);
            };

            // Coletar info do sistema para o relatório
            report.SystemInfo = $"{Environment.OSVersion} | {Environment.MachineName} | {Environment.ProcessorCount} CPUs";

            string? reportPath = null;

            try
            {
                // ── ETAPA 0: Ponto de Restauração (0-2%) ──
                currentSubTask = "Ponto de Restauração";
                ProgressTrackingService.Instance.StartSubTask(op.Id, currentSubTask);
                _loggingService.LogDebug("[Repair][FullRepair] Iniciando ETAPA 0: Ponto de Restauração", source: "RepairView");
                var s0 = await svc.Step00_CreateRestorePointAsync(_cts.Token);
                ProgressTrackingService.Instance.CompleteSubTask(op.Id, currentSubTask);
                report.Steps.Add(s0);
                report.RestorePointCreated = s0.Success;
                report.RestorePointName = s0.Summary;
                _loggingService.LogDebug($"[Repair][FullRepair] ETAPA 0 concluída: success={s0.Success} duration={s0.Duration.TotalSeconds:F1}s", source: "RepairView");
                _cts.Token.ThrowIfCancellationRequested();

                // ── ETAPA 1: Reset de Rede (2-8%) ──
                currentSubTask = "Reset de Rede";
                ProgressTrackingService.Instance.StartSubTask(op.Id, currentSubTask);
                _loggingService.LogDebug("[Repair][FullRepair] Iniciando ETAPA 1: Reset de Rede", source: "RepairView");
                var s1 = await svc.Step01_NetworkResetAsync(_cts.Token);
                ProgressTrackingService.Instance.CompleteSubTask(op.Id, currentSubTask);
                report.Steps.Add(s1);
                _loggingService.LogDebug($"[Repair][FullRepair] ETAPA 1 concluída: success={s1.Success} duration={s1.Duration.TotalSeconds:F1}s", source: "RepairView");
                _cts.Token.ThrowIfCancellationRequested();

                // ── ETAPA 2: .NET Framework (8-14%) ──
                currentSubTask = ".NET Framework";
                ProgressTrackingService.Instance.StartSubTask(op.Id, currentSubTask);
                _loggingService.LogDebug("[Repair][FullRepair] Iniciando ETAPA 2: .NET Framework", source: "RepairView");
                var s2 = await svc.Step02_DotNetRepairAsync(_cts.Token);
                ProgressTrackingService.Instance.CompleteSubTask(op.Id, currentSubTask);
                report.Steps.Add(s2);
                _loggingService.LogDebug($"[Repair][FullRepair] ETAPA 2 concluída: success={s2.Success} duration={s2.Duration.TotalSeconds:F1}s", source: "RepairView");
                _cts.Token.ThrowIfCancellationRequested();

                // ── ETAPA 3: Visual C++ (14-18%) ──
                currentSubTask = "Visual C++";
                ProgressTrackingService.Instance.StartSubTask(op.Id, currentSubTask);
                _loggingService.LogDebug("[Repair][FullRepair] Iniciando ETAPA 3: Visual C++", source: "RepairView");
                var s3 = await svc.Step03_VcRedistRepairAsync(_cts.Token);
                ProgressTrackingService.Instance.CompleteSubTask(op.Id, currentSubTask);
                report.Steps.Add(s3);
                _loggingService.LogDebug($"[Repair][FullRepair] ETAPA 3 concluída: success={s3.Success} duration={s3.Duration.TotalSeconds:F1}s", source: "RepairView");
                _cts.Token.ThrowIfCancellationRequested();

                // ── ETAPA 4: Windows Store + UWP (18-23%) ──
                currentSubTask = "Windows Store";
                ProgressTrackingService.Instance.StartSubTask(op.Id, currentSubTask);
                _loggingService.LogDebug("[Repair][FullRepair] Iniciando ETAPA 4: Windows Store + UWP", source: "RepairView");
                var s4 = await svc.Step04_WindowsStoreResetAsync(_cts.Token);
                ProgressTrackingService.Instance.CompleteSubTask(op.Id, currentSubTask);
                report.Steps.Add(s4);
                _loggingService.LogDebug($"[Repair][FullRepair] ETAPA 4 concluída: success={s4.Success} duration={s4.Duration.TotalSeconds:F1}s", source: "RepairView");
                _cts.Token.ThrowIfCancellationRequested();

                // ── ETAPA 5: Boot Manager (23-28%) ──
                currentSubTask = "Boot Manager";
                ProgressTrackingService.Instance.StartSubTask(op.Id, currentSubTask);
                _loggingService.LogDebug("[Repair][FullRepair] Iniciando ETAPA 5: Boot Manager", source: "RepairView");
                var s5 = await svc.Step05_BootRepairAsync(_cts.Token);
                ProgressTrackingService.Instance.CompleteSubTask(op.Id, currentSubTask);
                report.Steps.Add(s5);
                _loggingService.LogDebug($"[Repair][FullRepair] ETAPA 5 concluída: success={s5.Success} duration={s5.Duration.TotalSeconds:F1}s", source: "RepairView");
                _cts.Token.ThrowIfCancellationRequested();

                // ── ETAPA 6: Serviços do Windows (28-33%) ──
                currentSubTask = "Serviços do Windows";
                ProgressTrackingService.Instance.StartSubTask(op.Id, currentSubTask);
                _loggingService.LogDebug("[Repair][FullRepair] Iniciando ETAPA 6: Serviços do Windows", source: "RepairView");
                var s6 = await svc.Step06_ServicesResetAsync(_cts.Token);
                ProgressTrackingService.Instance.CompleteSubTask(op.Id, currentSubTask);
                report.Steps.Add(s6);
                _loggingService.LogDebug($"[Repair][FullRepair] ETAPA 6 concluída: success={s6.Success} duration={s6.Duration.TotalSeconds:F1}s", source: "RepairView");
                _cts.Token.ThrowIfCancellationRequested();

                // ── ETAPA 7: Registro (33-38%) ──
                currentSubTask = "Registro";
                ProgressTrackingService.Instance.StartSubTask(op.Id, currentSubTask);
                _loggingService.LogDebug("[Repair][FullRepair] Iniciando ETAPA 7: Registro", source: "RepairView");
                var s7 = await svc.Step07_RegistryRepairAsync(_cts.Token);
                ProgressTrackingService.Instance.CompleteSubTask(op.Id, currentSubTask);
                report.Steps.Add(s7);
                _loggingService.LogDebug($"[Repair][FullRepair] ETAPA 7 concluída: success={s7.Success} duration={s7.Duration.TotalSeconds:F1}s", source: "RepairView");
                _cts.Token.ThrowIfCancellationRequested();

                // ── ETAPA 8: Limpeza de Cache (38-44%) ──
                currentSubTask = "Limpeza de Cache";
                ProgressTrackingService.Instance.StartSubTask(op.Id, currentSubTask);
                _loggingService.LogDebug("[Repair][FullRepair] Iniciando ETAPA 8: Limpeza de Cache", source: "RepairView");
                var s8 = await svc.Step08_CacheCleanAsync(_cts.Token);
                ProgressTrackingService.Instance.CompleteSubTask(op.Id, currentSubTask);
                report.Steps.Add(s8);
                _loggingService.LogDebug($"[Repair][FullRepair] ETAPA 8 concluída: success={s8.Success} duration={s8.Duration.TotalSeconds:F1}s", source: "RepairView");
                _cts.Token.ThrowIfCancellationRequested();

                // ── ETAPA 9: Windows Defender (44-49%) ──
                currentSubTask = "Windows Defender";
                ProgressTrackingService.Instance.StartSubTask(op.Id, currentSubTask);
                _loggingService.LogDebug("[Repair][FullRepair] Iniciando ETAPA 9: Windows Defender", source: "RepairView");
                var s9 = await svc.Step09_DefenderRepairAsync(_cts.Token);
                ProgressTrackingService.Instance.CompleteSubTask(op.Id, currentSubTask);
                report.Steps.Add(s9);
                _loggingService.LogDebug($"[Repair][FullRepair] ETAPA 9 concluída: success={s9.Success} duration={s9.Duration.TotalSeconds:F1}s", source: "RepairView");
                _cts.Token.ThrowIfCancellationRequested();

                // ── ETAPA 10: Drivers (49-54%) ──
                currentSubTask = "Drivers";
                ProgressTrackingService.Instance.StartSubTask(op.Id, currentSubTask);
                _loggingService.LogDebug("[Repair][FullRepair] Iniciando ETAPA 10: Drivers", source: "RepairView");
                var s10 = await svc.Step10_DriverRepairAsync(_cts.Token);
                ProgressTrackingService.Instance.CompleteSubTask(op.Id, currentSubTask);
                report.Steps.Add(s10);
                _loggingService.LogDebug($"[Repair][FullRepair] ETAPA 10 concluída: success={s10.Success} duration={s10.Duration.TotalSeconds:F1}s", source: "RepairView");
                _cts.Token.ThrowIfCancellationRequested();

                // ── ETAPA 11: BSOD Analysis (54-58%) ──
                currentSubTask = "BSOD Analysis";
                ProgressTrackingService.Instance.StartSubTask(op.Id, currentSubTask);
                _loggingService.LogDebug("[Repair][FullRepair] Iniciando ETAPA 11: BSOD Analysis", source: "RepairView");
                var s11 = await svc.Step11_BsodAnalysisAsync(_cts.Token);
                ProgressTrackingService.Instance.CompleteSubTask(op.Id, currentSubTask);
                report.Steps.Add(s11);
                _loggingService.LogDebug($"[Repair][FullRepair] ETAPA 11 concluída: success={s11.Success} duration={s11.Duration.TotalSeconds:F1}s", source: "RepairView");
                _cts.Token.ThrowIfCancellationRequested();

                // ── ETAPA 12: Event Viewer (58-62%) ──
                currentSubTask = "Event Viewer";
                ProgressTrackingService.Instance.StartSubTask(op.Id, currentSubTask);
                _loggingService.LogDebug("[Repair][FullRepair] Iniciando ETAPA 12: Event Viewer", source: "RepairView");
                var s12 = await svc.Step12_EventViewerRepairAsync(_cts.Token);
                ProgressTrackingService.Instance.CompleteSubTask(op.Id, currentSubTask);
                report.Steps.Add(s12);
                _loggingService.LogDebug($"[Repair][FullRepair] ETAPA 12 concluída: success={s12.Success} duration={s12.Duration.TotalSeconds:F1}s", source: "RepairView");
                _cts.Token.ThrowIfCancellationRequested();

                // ── ETAPA 13: Print Spooler (62-65%) ──
                currentSubTask = "Print Spooler";
                ProgressTrackingService.Instance.StartSubTask(op.Id, currentSubTask);
                _loggingService.LogDebug("[Repair][FullRepair] Iniciando ETAPA 13: Print Spooler", source: "RepairView");
                var s13 = await svc.Step13_PrintSpoolerResetAsync(_cts.Token);
                ProgressTrackingService.Instance.CompleteSubTask(op.Id, currentSubTask);
                report.Steps.Add(s13);
                _loggingService.LogDebug($"[Repair][FullRepair] ETAPA 13 concluída: success={s13.Success} duration={s13.Duration.TotalSeconds:F1}s", source: "RepairView");
                _cts.Token.ThrowIfCancellationRequested();

                // ── ETAPA 14: DirectX + Áudio (65-69%) ──
                currentSubTask = "DirectX + Áudio";
                ProgressTrackingService.Instance.StartSubTask(op.Id, currentSubTask);
                _loggingService.LogDebug("[Repair][FullRepair] Iniciando ETAPA 14: DirectX + Áudio", source: "RepairView");
                var s14 = await svc.Step14_DirectXAudioRepairAsync(_cts.Token);
                ProgressTrackingService.Instance.CompleteSubTask(op.Id, currentSubTask);
                report.Steps.Add(s14);
                _loggingService.LogDebug($"[Repair][FullRepair] ETAPA 14 concluída: success={s14.Success} duration={s14.Duration.TotalSeconds:F1}s", source: "RepairView");
                _cts.Token.ThrowIfCancellationRequested();

                // ── ETAPA 15: Perfil de Usuário (69-72%) ──
                currentSubTask = "Perfil de Usuário";
                ProgressTrackingService.Instance.StartSubTask(op.Id, currentSubTask);
                _loggingService.LogDebug("[Repair][FullRepair] Iniciando ETAPA 15: Perfil de Usuário", source: "RepairView");
                var s15 = await svc.Step15_UserProfileRepairAsync(_cts.Token);
                ProgressTrackingService.Instance.CompleteSubTask(op.Id, currentSubTask);
                report.Steps.Add(s15);
                _loggingService.LogDebug($"[Repair][FullRepair] ETAPA 15 concluída: success={s15.Success} duration={s15.Duration.TotalSeconds:F1}s", source: "RepairView");
                _cts.Token.ThrowIfCancellationRequested();

                // ── ETAPA 16: DISM + SFC (72-86%) ──
                currentSubTask = "DISM + SFC";
                ProgressTrackingService.Instance.StartSubTask(op.Id, currentSubTask);
                _loggingService.LogDebug("[Repair][FullRepair] Iniciando ETAPA 16: DISM + SFC (etapa mais longa ~10-20min)", source: "RepairView");
                var s16 = await svc.Step16_IntegrityCheckAsync(_cts.Token);
                ProgressTrackingService.Instance.CompleteSubTask(op.Id, currentSubTask);
                report.Steps.Add(s16);
                _loggingService.LogDebug($"[Repair][FullRepair] ETAPA 16 concluída: success={s16.Success} duration={s16.Duration.TotalSeconds:F1}s", source: "RepairView");
                _cts.Token.ThrowIfCancellationRequested();

                // ── ETAPA 17: Diagnóstico Inteligente (86-92%) ──
                currentSubTask = "Diagnóstico Inteligente";
                ProgressTrackingService.Instance.StartSubTask(op.Id, currentSubTask);
                _loggingService.LogDebug("[Repair][FullRepair] Iniciando ETAPA 17: Diagnóstico Inteligente", source: "RepairView");
                var s17 = await svc.Step17_SmartDiagnosticsAsync(_cts.Token);
                ProgressTrackingService.Instance.CompleteSubTask(op.Id, currentSubTask);
                report.Steps.Add(s17);
                _loggingService.LogDebug($"[Repair][FullRepair] ETAPA 17 concluída: success={s17.Success} duration={s17.Duration.TotalSeconds:F1}s", source: "RepairView");
                _cts.Token.ThrowIfCancellationRequested();

                // ── ETAPA 17B: Corrigir Erros nos Jogos (92-96%) ──
                currentSubTask = "Corrigir Erros nos Jogos";
                ProgressTrackingService.Instance.StartSubTask(op.Id, currentSubTask);
                Log($"═══ {Loc("RepairView_Step17B_Title")} ═══", Colors.Cyan);
                _loggingService.LogDebug("[Repair][FullRepair] Iniciando ETAPA 17B: Corrigir Erros nos Jogos", source: "RepairView");
                try
                {
                    // CORREÇÃO: Usar a instância singleton do ServiceLocator para evitar
                    // scan/reparo duplo em paralelo com o GamerViewModel.
                    var gameRepairSvc = VoltrisOptimizer.Core.ServiceLocator.GetService<VoltrisOptimizer.Services.GameRepairService>();
                    if (gameRepairSvc == null)
                        gameRepairSvc = new VoltrisOptimizer.Services.GameRepairService(_loggingService);

                    gameRepairSvc.OnLog = (msg, _) => Dispatcher.BeginInvoke(() => Log(msg, Colors.Gray));
                    gameRepairSvc.OnProgress = (pct, msg) => Dispatcher.BeginInvoke(() =>
                        ProgressTrackingService.Instance.UpdateSubTaskProgress(op.Id, currentSubTask, (int)pct));

                    var scanProgress = new Progress<(int pct, string msg)>(p =>
                        Dispatcher.BeginInvoke(() => ProgressTrackingService.Instance.UpdateSubTaskProgress(op.Id, currentSubTask, p.pct / 2)));

                    var scanResult = await gameRepairSvc.ScanAsync(scanProgress, _cts.Token);
                    var gameRepairStep = new AdvancedRepairStepResult { StepName = "Corrigir Erros nos Jogos" };

                    if (scanResult.AllOk)
                    {
                        Log($"✓ {Loc("RepairView_Game_AllOk")}", Colors.LightGreen);
                        gameRepairStep.Success = true;
                        gameRepairStep.Summary = Loc("RepairView_Game_NoIssues");
                    }
                    else
                    {
                        Log($"→ {string.Format(Loc("RepairView_Game_IssuesFoundFmt"), scanResult.Issues.Count)}", Colors.White);
                        var repairProgress = new Progress<(int pct, string msg)>(p =>
                            Dispatcher.BeginInvoke(() => ProgressTrackingService.Instance.UpdateSubTaskProgress(op.Id, currentSubTask, 50 + p.pct / 2)));

                        var repairResult = await gameRepairSvc.RepairAsync(scanResult.Issues, repairProgress, _cts.Token);
                        gameRepairStep.Success = repairResult.FixedCount > 0 || repairResult.TotalIssues == 0;
                        gameRepairStep.Summary = string.Format(Loc("RepairView_Game_FixedFmt"), repairResult.FixedCount, repairResult.TotalIssues);
                        Log($"✓ {gameRepairStep.Summary}", Colors.LightGreen);
                    }

                    report.Steps.Add(gameRepairStep);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception gameRepairEx)
                {
                    Log($"⚠ Erros nos Jogos: {gameRepairEx.Message}", Colors.Orange);
                    _loggingService.LogWarning($"[Repair] GameRepair: {gameRepairEx.Message}");
                }
                _cts.Token.ThrowIfCancellationRequested();

                // ── ETAPA 17C: Fix Barra de Pesquisa (CTF + SearchHost) ──
                currentSubTask = "Fix Barra de Pesquisa";
                ProgressTrackingService.Instance.StartSubTask(op.Id, currentSubTask);
                Log($"═══ {Loc("RepairView_Step17C_Title")} ═══", Colors.Cyan);
                _loggingService.LogDebug("[Repair][FullRepair] Iniciando ETAPA 17C: Fix Barra de Pesquisa", source: "RepairView");
                try
                {
                    var searchStep = await svc.FixSearchBarAsync(_cts.Token);
                    report.Steps.Add(searchStep);
                    Log(searchStep.Success
                        ? $"✓ {searchStep.Summary}"
                        : $"⚠ {searchStep.Summary}",
                        searchStep.Success ? Colors.LightGreen : Colors.Orange);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception searchEx)
                {
                    Log($"⚠ Fix Barra de Pesquisa: {searchEx.Message}", Colors.Orange);
                    _loggingService.LogWarning($"[Repair] FixSearchBar: {searchEx.Message}");
                }
                _cts.Token.ThrowIfCancellationRequested();
                ProgressTrackingService.Instance.CompleteSubTask(op.Id, "Fix Barra de Pesquisa");

                // ── ETAPA 17D: Fix Painel de Controle (Menu Iniciar) ──
                currentSubTask = "Fix Painel de Controle";
                ProgressTrackingService.Instance.StartSubTask(op.Id, currentSubTask);
                Log($"═══ {Loc("RepairView_Step17D_Title")} ═══", Colors.Cyan);
                _loggingService.LogDebug("[Repair][FullRepair] Iniciando ETAPA 17D: Fix Painel de Controle", source: "RepairView");
                try
                {
                    var cpStep = await svc.FixControlPanelAsync(_cts.Token);
                    report.Steps.Add(cpStep);
                    Log(cpStep.Success
                        ? $"✓ {cpStep.Summary}"
                        : $"⚠ {cpStep.Summary}",
                        cpStep.Success ? Colors.LightGreen : Colors.Orange);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception cpEx)
                {
                    Log($"⚠ Fix Painel de Controle: {cpEx.Message}", Colors.Orange);
                    _loggingService.LogWarning($"[Repair] FixControlPanel: {cpEx.Message}");
                }
                _cts.Token.ThrowIfCancellationRequested();
                ProgressTrackingService.Instance.CompleteSubTask(op.Id, "Fix Painel de Controle");

                // ── ETAPA 17E: Fix CPU Alta (Core Parking) ──
                currentSubTask = "Fix CPU Alta";
                ProgressTrackingService.Instance.StartSubTask(op.Id, currentSubTask);
                Log($"═══ {Loc("RepairView_Step17E_Title")} ═══", Colors.Cyan);
                _loggingService.LogDebug("[Repair][FullRepair] Iniciando ETAPA 17E: Fix CPU Alta", source: "RepairView");
                try
                {
                    var cpuStep = await svc.FixHighCpuCoreParkingAsync(_cts.Token);
                    report.Steps.Add(cpuStep);
                    Log(cpuStep.Success
                        ? $"✓ {cpuStep.Summary}"
                        : $"⚠ {cpuStep.Summary}",
                        cpuStep.Success ? Colors.LightGreen : Colors.Orange);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception cpuEx)
                {
                    Log($"⚠ Fix CPU Alta: {cpuEx.Message}", Colors.Orange);
                    _loggingService.LogWarning($"[Repair] FixHighCpuCoreParking: {cpuEx.Message}");
                }
                _cts.Token.ThrowIfCancellationRequested();
                ProgressTrackingService.Instance.CompleteSubTask(op.Id, "Fix CPU Alta");

                // ── ETAPA 18: Windows Update (98%) ──
                currentSubTask = "Windows Update";
                ProgressTrackingService.Instance.StartSubTask(op.Id, currentSubTask);
                Log($"═══ {Loc("RepairView_Step18_Title")} ═══", Colors.Cyan);
                _loggingService.LogDebug("[Repair][FullRepair] Iniciando ETAPA 18: Windows Update", source: "RepairView");
                try
                {
                    var wuService = new WindowsUpdateService();
                    var wuCheck = await wuService.CheckForUpdatesAsync(_cts.Token);
                    var wuStep = new AdvancedRepairStepResult { StepName = "Windows Update" };
                    if (!wuCheck.Success)
                    {
                        Log($"⚠ Windows Update: {wuCheck.ErrorMessage}", Colors.Orange);
                        wuStep.Summary = wuCheck.ErrorMessage;
                    }
                    else if (wuCheck.IsUpToDate)
                    {
                        Log($"✓ {Loc("RepairView_WU_UpToDate")}", Colors.LightGreen);
                        wuStep.Success = true; wuStep.Summary = Loc("RepairView_WU_SystemUpdated");
                        ProgressTrackingService.Instance.CompleteSubTask(op.Id, currentSubTask);
                    }
                    else
                    {
                        Log($"→ {string.Format(Loc("RepairView_WU_UpdatesFmt"), wuCheck.PendingUpdates.Count)}", Colors.White);
                        var wuProgress = new Progress<(int percent, string message)>(p =>
                        {
                            // ✅ FIX: Progresso linear
                            ProgressTrackingService.Instance.UpdateSubTaskProgress(op.Id, currentSubTask, p.percent);
                            Dispatcher.BeginInvoke(() => Log(p.message, Colors.Gray));
                        });
                        var (wuOk, reboot, wuMsg) = await wuService.InstallUpdatesAsync(wuProgress, _cts.Token);
                        wuStep.Success = wuOk; wuStep.Summary = wuMsg;
                        Log(wuOk ? $"✓ {wuMsg}" : $"⚠ {wuMsg}", wuOk ? Colors.LightGreen : Colors.Orange);
                    }
                    report.Steps.Add(wuStep);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception wuEx)
                {
                    Log($"⚠ Windows Update: {wuEx.Message}", Colors.Orange);
                    _loggingService.LogWarning($"[Repair] Windows Update: {wuEx.Message}");
                }

                // ── ETAPA 19: Relatório HTML (98-100%) ──
                ProgressTrackingService.Instance.CompleteSubTask(op.Id, "Windows Update");
                currentSubTask = "Relatório HTML";
                ProgressTrackingService.Instance.StartSubTask(op.Id, currentSubTask);
                Log($"═══ {Loc("RepairView_Step19_Title")} ═══", Colors.Cyan);
                _loggingService.LogDebug("[Repair][FullRepair] Iniciando ETAPA 19: Gerando Relatório HTML", source: "RepairView");
                report.EndTime = DateTime.Now;
                try
                {
                    reportPath = await svc.GenerateHtmlReportAsync(report, _cts.Token);
                    Log($"✓ {string.Format(Loc("RepairView_ReportSavedFmt"), reportPath)}", Colors.LightGreen);
                }
                catch (Exception repEx)
                {
                    Log($"⚠ Relatório: {repEx.Message}", Colors.Orange);
                }

                // ── FINALIZAÇÃO ──
                Log("═══════════════════════════════════════════════════", Colors.Cyan);
                Log($"✅ {Loc("RepairView_FullRepairFinished")}", Colors.LightGreen);
                var successCount = report.Steps.Count(s => s.Success);
                Log($"   {string.Format(Loc("RepairView_StepsSuccessFmt"), successCount, report.Steps.Count)}", Colors.LightGreen);
                Log($"   {string.Format(Loc("RepairView_DurationFmt"), (report.EndTime - report.StartTime).TotalMinutes)}", Colors.Gray);
                _loggingService.LogDebug($"[Repair][FullRepair] CONCLUÍDO: {successCount}/{report.Steps.Count} etapas OK, duração={(report.EndTime - report.StartTime).TotalMinutes:F1}min", source: "RepairView");
                ProgressTrackingService.Instance.CompleteOperation(op.Id);

                NotificationManager.ShowSuccess(Loc("RepairView_FullRepairTitle"),
                    string.Format(Loc("RepairView_StepsSuccessFmt"), successCount, report.Steps.Count));

                // Mostrar análise de falhas se houver problemas
                if (successCount < report.Steps.Count && !string.IsNullOrEmpty(report.FinalAnalysis))
                {
                    ModernMessageBox.Show(
                        Loc("RepairView_AnalysisWarningText") + "\n\n" +
                        Loc("RepairView_AnalysisPrefix") + "\n" + report.FinalAnalysis + 
                        "\n\n" + Loc("RepairView_ReportSavedDesktop"),
                        Loc("RepairView_AnalysisTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                }

                // ✅ FIX BUG #3: Marcar concluso bem-sucedida AQUI (dentro do try, após todas as etapas)
                // O modal de reinicialização só deve aparecer se o reparo chegou até aqui sem exceção/cancelamento
                repairCompletedSuccessfully = true;
                _loggingService.LogDebug("[Repair][FullRepair] repairCompletedSuccessfully = true — reparo concluído sem exceções.", source: "RepairView");
            }
            catch (OperationCanceledException)
            {
                _loggingService.LogWarning("[Repair][FullRepair] ⚠ Operação CANCELADA pelo usuário");
                Log(Loc("RepairView_OperationCanceled"), Colors.Orange);
                report.EndTime = DateTime.Now;
                ProgressTrackingService.Instance.CancelOperation(op.Id);
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"[Repair] Erro no reparo ultra avançado: {ex.Message}");
                _loggingService.LogDebug($"[Repair][FullRepair] EXCEÇÃO: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}", source: "RepairView");
                Log($"{Loc("RepairView_ErrorPrefix")}: {ex.Message}", Colors.Red);
                ProgressTrackingService.Instance.CancelOperation(op.Id);
                ModernMessageBox.Show($"{Loc("RepairView_ErrorDuringRepair")}:\n\n{ex.Message}", Loc("RepairView_ErrorTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
                report.EndTime = DateTime.Now;
            }
            finally
            {
                _loggingService.LogDebug("[Repair][FullRepair] Finalizando — liberando recursos e restaurando UI", source: "RepairView");
                SetBusy(false, Loc("RepairView_Ready"));
                _cts?.Dispose();
                _cts = null;
            }

            // ── MODAL DE REINICIALIZAÇÃO (somente se reparo concluiu com sucesso) ──
            // ✅ FIX BUG #3: Usar flag local repairCompletedSuccessfully em vez de _isRepairRunning
            // _isRepairRunning é setado como false pelo SetBusy(false) no finally — nunca era true aqui.
            // repairCompletedSuccessfully só é true se todas as etapas foram executadas sem exceção/cancelamento.
            _loggingService.LogDebug($"[Repair][FullRepair] Verificando modal de restart: repairCompletedSuccessfully={repairCompletedSuccessfully}", source: "RepairView");
            if (repairCompletedSuccessfully)
            {
                Dispatcher.Invoke(() =>
                {
                    var modal = new VoltrisOptimizer.UI.Windows.RestartConfirmationModal
                    {
                        Owner = Application.Current.MainWindow
                    };
                    modal.SetCustomMessage(
                        Loc("RepairView_RestartModalTitle"),
                        Loc("RepairView_RestartModalText"));
                    modal.ShowDialog();

                    if (modal.ShouldRestart)
                    {
                        _loggingService.LogInfo("[Repair] Usuário escolheu reiniciar agora após reparo completo.");
                        Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 10 /c \"Voltris: Reparo Completo concluído\"")
                        {
                            UseShellExecute = false,
                            CreateNoWindow = true
                        });
                    }
                    else if (modal.ShouldApplyOptimizations)
                    {
                        _loggingService.LogInfo("[Repair] Usuário escolheu reiniciar depois.");
                    }
                    else
                    {
                        _loggingService.LogInfo("[Repair] Usuário cancelou reinicialização.");
                    }
                });
            }
        }

        // ═══════════════════════════════════════════════════════════
        // BOTÃO: REPARO DISM + SFC (sem CHKDSK)
        // ═══════════════════════════════════════════════════════════
        private async void RepairSystemButton_Click(object sender, RoutedEventArgs e)
        {
            if (ShowBusyModal()) return;

            var result = ModernMessageBox.Show(
                Loc("RepairView_DismWarningText"),
                Loc("RepairView_DismWarningTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            if (!AdminHelper.IsRunningAsAdministrator())
            {
                ModernMessageBox.Show(
                    Loc("RepairView_AdminRequired"),
                    Loc("RepairView_AdminRequiredTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _cts = new CancellationTokenSource();
            SetBusy(true, Loc("RepairView_DismInProgress"));
            
            var op = ProgressTrackingService.Instance.StartOperation(Loc("RepairView_DismOp"));
            ProgressTrackingService.Instance.RegisterSubTasks(op.Id,
                ("DISM", 50),
                ("SFC", 50)
            );
            
            App.TelemetryService?.TrackEvent("SYSTEM_REPAIR", "Repair", "Start", forceFlush: true);

            try
            {
                // ── STEP 1: DISM (0-50%) ──
                Log($"═══ {Loc("RepairView_DismStep1Title")} ═══", Colors.Cyan);
                ProgressTrackingService.Instance.StartSubTask(op.Id, "DISM");

                var dismResult = await RunSystemToolAsync(
                    "dism.exe", "/Online /Cleanup-Image /RestoreHealth",
                    "DISM", op.Id, _cts.Token);

                if (dismResult.ExitCode == 0)
                    Log($"✓ {Loc("RepairView_DismSuccess")}", Colors.LightGreen);
                else
                    Log($"⚠ {string.Format(Loc("RepairView_DismCodeFmt"), dismResult.ExitCode)}", Colors.Orange);

                if (_cts.Token.IsCancellationRequested) return;

                // ── STEP 2: SFC (50-100%) ──
                Log($"═══ {Loc("RepairView_SfcStep2Title")} ═══", Colors.Cyan);
                ProgressTrackingService.Instance.StartSubTask(op.Id, "SFC");

                var sfcResult = await RunSystemToolAsync(
                    "sfc.exe", "/scannow",
                    "SFC", op.Id, _cts.Token);

                if (sfcResult.ExitCode == 0)
                    Log($"✓ {Loc("RepairView_SfcSuccess")}", Colors.LightGreen);
                else if (sfcResult.ExitCode == 1)
                    Log($"⚠ {Loc("RepairView_SfcPendingReboot")}", Colors.Orange);
                else
                    Log($"⚠ {string.Format(Loc("RepairView_SfcCodeFmt"), sfcResult.ExitCode)}", Colors.Orange);

                Log(Loc("RepairView_DismSfcCompletedMsg"), Colors.LightGreen);
                ProgressTrackingService.Instance.CompleteOperation(op.Id);

                NotificationManager.ShowSuccess(Loc("RepairView_CompletedTitle"),
                    Loc("RepairView_DismSfcCompletedDetailed"));
            }
            catch (OperationCanceledException)
            {
                Log(Loc("RepairView_OperationCanceled"), Colors.Orange);
                ProgressTrackingService.Instance.CancelOperation(op.Id);
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"[Repair] Erro no reparo: {ex.Message}");
                Log($"{Loc("RepairView_ErrorPrefix")}: {ex.Message}", Colors.Red);
                ProgressTrackingService.Instance.CancelOperation(op.Id);
                ModernMessageBox.Show($"{Loc("RepairView_ErrorRepairing")}:\n\n{ex.Message}", Loc("RepairView_ErrorTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false, Loc("RepairView_Ready"));
                _cts?.Dispose();
                _cts = null;
            }
        }

        // ═══════════════════════════════════════════════════════════
        // MOTOR DE EXECUÇÃO: Roda ferramenta do sistema com progresso real
        // ═══════════════════════════════════════════════════════════
        private async Task<ToolResult> RunSystemToolAsync(
            string executable, string arguments, string toolName,
            Guid operationId, CancellationToken ct)
        {
            var output = new StringBuilder();
            var exitCode = -1;

            // Resolver caminho correto (SysNative para processos 32-bit em OS 64-bit)
            var toolPath = ResolveSystemToolPath(executable);

            // SFC não funciona bem com RedirectStandardOutput + Encoding.Unicode
            // Ele usa um mecanismo especial de output que não é capturado corretamente.
            // Solução: executar via cmd.exe com pipe para arquivo temporário, depois ler.
            if (toolName == "SFC")
            {
                return await RunSfcViaFileAsync(toolPath, arguments, operationId, ct);
            }

            var oemEncoding = GetOemEncodingSafe();

            var psi = new ProcessStartInfo
            {
                FileName = toolPath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = oemEncoding
            };

            _loggingService.LogInfo($"[Repair] Executando: {toolPath} {arguments}");

            using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

            proc.OutputDataReceived += (s, e) =>
            {
                if (e.Data == null) return;
                output.AppendLine(e.Data);
                ParseAndUpdateProgress(toolName, e.Data, operationId);
            };

            proc.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null)
                    _loggingService.LogWarning($"[{toolName}] stderr: {e.Data}");
            };

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            try
            {
                await proc.WaitForExitAsync(ct);
                exitCode = proc.ExitCode;
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                throw;
            }

            return new ToolResult { ExitCode = exitCode, Output = output.ToString() };
        }

        /// <summary>
        /// SFC não funciona com RedirectStandardOutput nem com cmd /c redirect.
        /// Usa PowerShell para executar e capturar o output corretamente.
        /// Monitora CBS.log para progresso em tempo real.
        /// </summary>
        private async Task<ToolResult> RunSfcViaFileAsync(
            string sfcPath, string arguments,
            Guid operationId, CancellationToken ct)
        {
            var output = new StringBuilder();
            var exitCode = -1;

            try
            {
                // PowerShell executa SFC corretamente e captura output
                var psPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "System32", "WindowsPowerShell", "v1.0", "powershell.exe");

                // -Command com & para executar e capturar output
                var psArgs = $"-NoProfile -NonInteractive -Command \"& '{sfcPath}' {arguments}; exit $LASTEXITCODE\"";

                var psi = new ProcessStartInfo
                {
                    FileName = psPath,
                    Arguments = psArgs,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.Unicode,
                    StandardErrorEncoding = Encoding.Unicode
                };

                _loggingService.LogInfo($"[Repair] Executando SFC via PowerShell: {sfcPath} {arguments}");

                // REPARO: Usar TaskCompletionSource para aguardar o processo sem WaitForExitAsync.
                // WaitForExitAsync() registra um EventHandler interno que acessa proc.HasExited
                // APÓS o proc.Dispose() ser chamado no finally — causando UNOBSERVED TASK EXCEPTION:
                // "No process is associated with this object" (confirmado no log 19:37:44).
                // A solução: aguardar via Task.Run(proc.WaitForExit) que é síncrono e não deixa
                // handlers pendentes, garantindo que o proc só seja disposed depois de tudo.
                var proc = new Process { StartInfo = psi, EnableRaisingEvents = false };

                // Monitorar CBS.log para progresso em tempo real (SFC escreve progresso lá)
                var cbsLogPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "Logs", "CBS", "CBS.log");
                var cbsBaseSize = 0L;
                try { if (File.Exists(cbsLogPath)) cbsBaseSize = new FileInfo(cbsLogPath).Length; } catch { }

                var lastPercent = 0;
                // REPARO: flag volátil para bloquear callbacks após o processo encerrar
                var procFinished = false;

                proc.OutputDataReceived += (s, e) =>
                {
                    if (e.Data == null || procFinished) return;
                    // SFC output em Unicode pode ter null chars e espaços extras
                    var line = e.Data.Replace("\0", "").Trim();
                    // Remover espaços duplos entre caracteres (artefato de encoding)
                    while (line.Contains("  ")) line = line.Replace("  ", " ");
                    output.AppendLine(line);

                    if (string.IsNullOrWhiteSpace(line)) return;

                    // SFC output em PT-BR: "Verificação 45% concluída."
                    var match = Regex.Match(line, @"(\d+)\s*%");
                    if (match.Success && int.TryParse(match.Groups[1].Value, out var pct) && pct > lastPercent)
                    {
                        lastPercent = pct;
                        ProgressTrackingService.Instance.UpdateSubTaskProgress(operationId, "SFC", pct);

                        Dispatcher.BeginInvoke(() =>
                        {
                            RepairStatusText.Text = string.Format(LocalizationService.Instance.GetString("RepairSfcPercentFormat"), pct);
                        });
                    }
                    else if (line.Length > 5)
                    {
                        _loggingService.LogInfo($"[SFC] {line}");
                        Dispatcher.BeginInvoke(() => Log(line, Colors.Gray));
                    }
                };

                proc.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null && !procFinished)
                        _loggingService.LogWarning($"[SFC] stderr: {e.Data}");
                };

                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                using var monitorCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

                // Monitorar CBS.log como fallback para progresso
                var monitorTask = Task.Run(async () =>
                {
                    try
                    {
                        while (!monitorCts.Token.IsCancellationRequested)
                        {
                            await Task.Delay(3000, monitorCts.Token);
                            try
                            {
                                if (!File.Exists(cbsLogPath)) continue;
                                var currentSize = new FileInfo(cbsLogPath).Length;
                                if (currentSize > cbsBaseSize && lastPercent == 0)
                                {
                                    var growth = currentSize - cbsBaseSize;
                                    // CBS.log cresce ~2-5MB durante SFC scan
                                    var estimatedPct = Math.Min(90, (int)(growth / 50000));
                                    if (estimatedPct > 5)
                                    {
                                        ProgressTrackingService.Instance.UpdateSubTaskProgress(operationId, "SFC", estimatedPct);
                                        Dispatcher.BeginInvoke(() =>
                                        {
                                            RepairStatusText.Text = string.Format(LocalizationService.Instance.GetString("RepairSfcCheckingFormat"), estimatedPct);
                                        });
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                    catch (OperationCanceledException) { }
                }, monitorCts.Token);

                // REPARO: Task.Run(WaitForExit) em vez de WaitForExitAsync.
                // WaitForExit() síncrono não registra EventHandlers internos que ficam
                // pendentes após Dispose — elimina a race condition do UNOBSERVED TASK EXCEPTION.
                var waitTask = Task.Run(() => proc.WaitForExit(), CancellationToken.None);

                try
                {
                    // Aguardar o processo OU cancelamento
                    var completedTask = await Task.WhenAny(waitTask, Task.Delay(Timeout.Infinite, ct));
                    if (ct.IsCancellationRequested)
                    {
                        try { proc.Kill(entireProcessTree: true); } catch { }
                        await waitTask.ConfigureAwait(false); // aguardar o kill completar
                        ct.ThrowIfCancellationRequested();
                    }
                    // DEBUG: capturar exitCode ANTES de sinalizar procFinished e antes do dispose
                    exitCode = proc.ExitCode;
                    _loggingService.LogDebug($"[SFC] WaitForExit concluído: exitCode={exitCode}", source: "RepairView");
                }
                catch (OperationCanceledException)
                {
                    try { proc.Kill(entireProcessTree: true); } catch { }
                    try { await waitTask.ConfigureAwait(false); } catch { }
                    throw;
                }
                finally
                {
                    // REPARO: sinalizar procFinished ANTES de cancelar o monitor
                    procFinished = true;
                    monitorCts.Cancel();
                    // Aguardar o monitor CBS terminar completamente antes do dispose
                    try { await monitorTask.ConfigureAwait(false); } catch { }
                    // Aguardar waitTask (caso ainda não tenha terminado)
                    try { await waitTask.ConfigureAwait(false); } catch { }
                    // Agora é seguro dispor — nenhum handler acessa proc
                    try { proc.Dispose(); } catch { }
                    _loggingService.LogDebug("[SFC] Recursos liberados (proc disposed)", source: "RepairView");
                }

                _loggingService.LogInfo($"[SFC] Concluído com código: {exitCode}");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _loggingService.LogError($"[SFC] Erro ao executar: {ex.Message}");
            }

            return new ToolResult { ExitCode = exitCode, Output = output.ToString() };
        }


        /// <summary>
        /// Parseia a saída do DISM/SFC/CHKDSK e atualiza o progresso global em tempo real
        /// </summary>
        private void ParseAndUpdateProgress(string toolName, string line, Guid operationId)
        {
            if (string.IsNullOrWhiteSpace(line)) return;

            int? parsedPercent = null;

            if (toolName == "DISM")
            {
                // DISM output: [==                 10.0%                    ] ou [==                 10,0%                    ]
                var match = Regex.Match(line, @"(\d+([.,]\d+)?)%");
                if (match.Success)
                {
                    string val = match.Groups[1].Value.Replace(",", ".");
                    if (double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var pct))
                        parsedPercent = (int)Math.Round(pct);
                }
            }
            else if (toolName == "SFC")
            {
                // SFC output: "Verificação 45% concluída." or just "45%"
                var match = Regex.Match(line, @"(\d+)\s*%");
                if (match.Success && int.TryParse(match.Groups[1].Value, out var pct))
                    parsedPercent = pct;
            }
            else if (toolName == "CHKDSK")
            {
                // CHKDSK output: "Total: 12%" or "12 percent complete"
                var match = Regex.Match(line, @"(\d+)\s*(%|percent)", RegexOptions.IgnoreCase);
                if (match.Success && int.TryParse(match.Groups[1].Value, out var pct))
                    parsedPercent = pct;
            }

            if (parsedPercent.HasValue)
            {
                var p = parsedPercent.Value;
                ProgressTrackingService.Instance.UpdateSubTaskProgress(operationId, toolName, p);

                Dispatcher.BeginInvoke(() =>
                {
                    RepairStatusText.Text = $"{toolName}: {p}%";
                });
            }
            else
            {
                // Log non-progress lines that contain useful info
                var trimmed = line.Trim();
                if (trimmed.Length > 5 && !trimmed.StartsWith("[="))
                {
                    Dispatcher.BeginInvoke(() => Log(trimmed, Colors.Gray));
                }
            }
        }


        private static Encoding GetOemEncodingSafe()
        {
            try
            {
                return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
            }
            catch
            {
                return Encoding.UTF8;
            }
        }

        private static string ResolveSystemToolPath(string toolExecutable)
        {
            var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (string.IsNullOrEmpty(winDir)) return toolExecutable;

            // Para processos 32-bit em OS 64-bit, usar SysNative
            if (Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess)
            {
                var sysNativePath = Path.Combine(winDir, "SysNative", toolExecutable);
                if (File.Exists(sysNativePath)) return sysNativePath;
            }

            var system32Path = Path.Combine(winDir, "System32", toolExecutable);
            if (File.Exists(system32Path)) return system32Path;

            return toolExecutable;
        }

        // ═══════════════════════════════════════════════════════════
        // TOOL BUTTONS (Ferramentas do Windows)
        // ═══════════════════════════════════════════════════════════
        private async void DiskCleanupButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var ultraCleaner = App.Services?.GetService(typeof(VoltrisOptimizer.Services.UltraCleanerService)) as VoltrisOptimizer.Services.UltraCleanerService;
                if (ultraCleaner != null)
                {
                    await ultraCleaner.CleanWindowsDiskCleanupFullAsync();
                }
                Log(Loc("RepairView_DiskCleanupOpened"), Colors.LightGreen);
            }
            catch (Exception ex) { Log($"{Loc("RepairView_ErrorPrefix")}: {ex.Message}", Colors.Red); }
        }

        private async void DefragButton_Click(object sender, RoutedEventArgs e)
        {
            // ── Proteção contra execução concorrente ──
            if (!await _defragLock.WaitAsync(0))
            {
                Log($"⚠ {Loc("RepairView_DefragRunning")}", Colors.Orange);
                return;
            }

            if (_isDefragRunning)
            {
                Log($"⚠ {Loc("RepairView_DefragAlreadyRunning")}", Colors.Orange);
                _defragLock.Release();
                return;
            }

            var confirm = ModernMessageBox.Show(
                Loc("RepairView_DefragWarningText"),
                Loc("RepairView_DefragWarningTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
            {
                _defragLock.Release();
                return;
            }

            _isDefragRunning = true;

            try
            {
                Log("══════════════════════════════════════════", Colors.Cyan);
                Log($"🔵 {Loc("RepairView_DefragTitle")}", Colors.Cyan);
                Log("══════════════════════════════════════════", Colors.Cyan);

                var optimizer = new StorageOptimizerService();
                
                var progress = new Progress<string>(msg => 
                {
                    Dispatcher.Invoke(() => Log($"→ {msg}", Colors.LightGray));
                });

                await optimizer.OptimizeAllDrivesAsync(progress);

                Log("══════════════════════════════════════════", Colors.Cyan);
                Log($"✅ {Loc("RepairView_DefragCompleted")}", Colors.LightGreen);
                Log("══════════════════════════════════════════", Colors.Cyan);
                NotificationManager.ShowSuccess(Loc("RepairView_DefragCompletedTitle"),
                    Loc("RepairView_DefragCompletedDetailed"));
            }
            catch (Exception ex)
            {
                Log($"❌ {Loc("RepairView_CriticalErrorPrefix")}: {ex.Message}", Colors.Red);
                App.LoggingService?.LogError($"[Repair.Defrag] {ex.Message}");
            }
            finally
            {
                _isDefragRunning = false;
                _defragLock.Release();
            }
        }

        private class LogEntry
        {
            public string Message { get; set; } = "";
            public SolidColorBrush Color { get; set; } = Brushes.Gray;
        }

        private class ToolResult
        {
            public int ExitCode { get; set; }
            public string Output { get; set; } = "";
        }
    }
}
