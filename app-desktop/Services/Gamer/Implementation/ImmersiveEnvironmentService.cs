using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services.Gamer.Interfaces;
using VoltrisOptimizer.Services.Gamer.Optimization;
using VoltrisOptimizer.Services.Performance;

namespace VoltrisOptimizer.Services.Gamer.Implementation
{
    /// <summary>
    /// Gerencia o ambiente imersivo do usuário durante o jogo.
    /// Controla: Notificações, Sleep/Monitor timeout, Windows Update, Serviços Pesados.
    ///
    /// CORREÇÕES APLICADAS:
    ///   BUG #1 — Sleep timeout agora é lido via powercfg antes de alterar (backup fiel).
    ///   BUG #4 — ApplyFocusAssist e RestoreFocusAssist agora são simétricos (mesma chave).
    ///   BUG #6 — Verb = "runas" removido de PauseWindowsUpdate/RestoreWindowsUpdate.
    ///   BUG #7 — LanmanServer: aviso explícito + verificação de conexões ativas antes de parar.
    ///   WIN11  — Focus Assist detecta build >= 22621 e usa chave correta do "Do Not Disturb".
    ///
    /// Otimização de Serviços: WindowsServiceOptimizer (~73 serviços, rollback perfeito)
    /// </summary>
    public class ImmersiveEnvironmentService : IImmersiveGamingOptimizer
    {
        private readonly ILoggingService _logger;
        private readonly WindowsServiceOptimizer _serviceOptimizer;
        private readonly ScheduledTasksOptimizer _scheduledTaskOptimizer;
        private bool _isActive = false;

        // ── Backup de estados ──────────────────────────────────────────────────────
        private int? _backupToastEnabled;
        private int? _backupNocGlobalToast;       // NOTIFICATIONS_KEY backup (simétrico)
        private int? _backupDndEnabled;           // Win11 22H2+ Do Not Disturb backup

        // ✅ FIX BUG #1: Backup real dos timeouts de sleep (em segundos, -1 = não capturado)
        private int _backupMonitorTimeoutAC = -1;
        private int _backupMonitorTimeoutDC = -1;
        private int _backupStandbyTimeoutAC = -1;
        private int _backupStandbyTimeoutDC = -1;
        private bool _isSleepDisabled = false;

        /// Quando true, ativa o WindowsServiceOptimizer (~73 serviços desativados temporariamente).
        public bool DisableHeavyServices { get; set; } = true;

        // ── Backup Manutenção Automática ───────────────────────────────────────────
        private const string MAINT_KEY = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\Maintenance";
        private int? _backupMaintenanceDisabled;

        // ── Backup Wake Timers ─────────────────────────────────────────────────────
        private const string WAKE_TIMERS_GUID = @"SUB_SLEEP";
        private const string RTCWAKE_GUID = @"RTCWAKE";
        private int? _backupWakeTimersAC;
        private int? _backupWakeTimersDC;
        private bool _isWakeTimerDisabled;

        // ── Chaves de registro ─────────────────────────────────────────────────────
        private const string NOTIFICATIONS_KEY  = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";
        private const string PUSH_NOTIF_KEY     = @"Software\Microsoft\Windows\CurrentVersion\PushNotifications";
        // Win11 22H2+ (build >= 22621): "Do Not Disturb" usa chave diferente
        private const string DND_KEY_WIN11      = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";
        private const string DND_VALUE_WIN11    = "NOC_GLOBAL_SETTING_ALLOW_TOASTS_ABOVE_LOCK";

        public ImmersiveEnvironmentService(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceOptimizer = new WindowsServiceOptimizer(logger);
            _scheduledTaskOptimizer = new ScheduledTasksOptimizer(logger);
        }

        // ══════════════════════════════════════════════════════════════════════════
        // OPTIMIZE / RESTORE
        // ══════════════════════════════════════════════════════════════════════════

        public async Task<bool> OptimizeAsync(CancellationToken cancellationToken = default)
        {
            if (_isActive)
            {
                _logger.LogInfo("[ImmersiveEnv] OptimizeAsync chamado mas já está ativo — ignorando.");
                return true;
            }

            _logger.LogInfo("[ImmersiveEnv] ══════════════════════════════════════════");
            _logger.LogInfo("[ImmersiveEnv] 🤫 Iniciando silenciamento do ambiente Windows...");
            _logger.LogInfo($"[ImmersiveEnv] DisableHeavyServices={DisableHeavyServices}");

            return await Task.Run(async () =>
            {
                try
                {
                    // 1. Focus Assist / Do Not Disturb
                    ApplyFocusAssist();

                    // 2. Desabilitar Toasts Globais
                    DisableToasts();

                    // 3. Prevenir Sleep/Screensaver (com backup real)
                    PreventSleep();

                    // 4. Pausar Windows Update
                    PauseWindowsUpdate();

                    // 5. Otimizar serviços do Windows (~73 serviços com rollback perfeito)
                    if (DisableHeavyServices)
                        await _serviceOptimizer.ActivateOptimizationAsync().ConfigureAwait(false);
                    else
                        _logger.LogInfo("[ImmersiveEnv] DisableHeavyServices=false — serviços mantidos.");

                    // 6. Desativar tarefas agendadas (~50+ tarefas com verificação)
                    if (DisableHeavyServices)
                        await _scheduledTaskOptimizer.DisableAllTasksAsync(cancellationToken).ConfigureAwait(false);
                    else
                        _logger.LogInfo("[ImmersiveEnv] DisableHeavyServices=false — tarefas agendadas mantidas.");

                    // 7. Pausar manutenção automática do Windows
                    PauseAutomaticMaintenance();

                    // 8. Desabilitar Wake Timers
                    DisableWakeTimers();

            _isActive = true;
            _logger.LogSuccess("[ImmersiveEnv] ✅ Ambiente silenciado com sucesso.");
                    _logger.LogInfo("[ImmersiveEnv] ══════════════════════════════════════════");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[ImmersiveEnv] ⚠️ Falha parcial ao otimizar ambiente: {ex.Message}");
                    _logger.LogWarning($"[ImmersiveEnv] StackTrace: {ex.StackTrace}");
                    return false;
                }
            }, cancellationToken);
        }

        public async Task<bool> RestoreAsync(CancellationToken cancellationToken = default)
        {
            if (!_isActive)
            {
                _logger.LogInfo("[ImmersiveEnv] RestoreAsync chamado mas não estava ativo — ignorando.");
                return true;
            }

            _logger.LogInfo("[ImmersiveEnv] ══════════════════════════════════════════");
            _logger.LogInfo("[ImmersiveEnv] 🔊 Restaurando ambiente Windows...");

            return await Task.Run(() =>
            {
                try
                {
                    // Restaurar na ordem inversa da aplicação
                    _serviceOptimizer.DeactivateOptimizationAsync().GetAwaiter().GetResult();
                    _scheduledTaskOptimizer.RestoreAllTasksAsync(cancellationToken).GetAwaiter().GetResult();
                    RestoreWindowsUpdate();
                    RestoreSleep();
                    RestoreWakeTimers();
                    RestoreAutomaticMaintenance();
                    RestoreToasts();
                    RestoreFocusAssist();

                    _isActive = false;
                    _logger.LogSuccess("[ImmersiveEnv] ✅ Ambiente restaurado com sucesso.");
                    _logger.LogInfo("[ImmersiveEnv] ══════════════════════════════════════════");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[ImmersiveEnv] ❌ Erro ao restaurar ambiente: {ex.Message}", ex);
                    _logger.LogError($"[ImmersiveEnv] StackTrace: {ex.StackTrace}");
                    return false;
                }
            }, cancellationToken);
        }

        // ══════════════════════════════════════════════════════════════════════════
        // FOCUS ASSIST / DO NOT DISTURB
        // ✅ FIX BUG #4: ApplyFocusAssist e RestoreFocusAssist agora são simétricos.
        // ✅ WIN11 FIX: Detecta build >= 22621 e usa chave correta do "Do Not Disturb".
        // ══════════════════════════════════════════════════════════════════════════

        private static bool IsWin11_22H2OrLater()
        {
            var build = Environment.OSVersion.Version.Build;
            var result = build >= 22621;
            return result;
        }

        private void ApplyFocusAssist()
        {
            _logger.LogInfo($"[ImmersiveEnv] [FocusAssist] Aplicando — Win11_22H2+={IsWin11_22H2OrLater()}");
            try
            {
                // ── Chave 1: NOTIFICATIONS_KEY → NOC_GLOBAL_SETTING_TOAST_ENABLED ──
                using (var key = Registry.CurrentUser.OpenSubKey(NOTIFICATIONS_KEY, writable: true))
                {
                    if (key != null)
                    {
                        var val = key.GetValue("NOC_GLOBAL_SETTING_TOAST_ENABLED");
                        _backupNocGlobalToast = val is int iv ? iv : 1;
                        _logger.LogInfo($"[ImmersiveEnv] [FocusAssist] Backup NOC_GLOBAL_SETTING_TOAST_ENABLED = {_backupNocGlobalToast}");
                        key.SetValue("NOC_GLOBAL_SETTING_TOAST_ENABLED", 0, RegistryValueKind.DWord);
                        _logger.LogSuccess("[ImmersiveEnv] [FocusAssist] ✅ NOC_GLOBAL_SETTING_TOAST_ENABLED = 0");
                    }
                    else
                    {
                        _logger.LogWarning("[ImmersiveEnv] [FocusAssist] Chave NOTIFICATIONS_KEY não encontrada — pulando.");
                    }
                }

                // ── Chave 2: Win11 22H2+ "Do Not Disturb" ──
                if (IsWin11_22H2OrLater())
                {
                    using var dndKey = Registry.CurrentUser.OpenSubKey(DND_KEY_WIN11, writable: true);
                    if (dndKey != null)
                    {
                        var dndVal = dndKey.GetValue(DND_VALUE_WIN11);
                        _backupDndEnabled = dndVal is int dv ? dv : 1;
                        _logger.LogInfo($"[ImmersiveEnv] [FocusAssist] Win11 DND backup {DND_VALUE_WIN11} = {_backupDndEnabled}");
                        dndKey.SetValue(DND_VALUE_WIN11, 0, RegistryValueKind.DWord);
                        _logger.LogSuccess($"[ImmersiveEnv] [FocusAssist] ✅ Win11 DND {DND_VALUE_WIN11} = 0");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ImmersiveEnv] [FocusAssist] ⚠️ Erro ao aplicar: {ex.Message}");
            }
        }

        private void RestoreFocusAssist()
        {
            _logger.LogInfo($"[ImmersiveEnv] [FocusAssist] Restaurando — Win11_22H2+={IsWin11_22H2OrLater()}");
            try
            {
                // ── Restaurar Chave 1: NOC_GLOBAL_SETTING_TOAST_ENABLED ──
                if (_backupNocGlobalToast.HasValue)
                {
                    using var key = Registry.CurrentUser.OpenSubKey(NOTIFICATIONS_KEY, writable: true);
                    if (key != null)
                    {
                        key.SetValue("NOC_GLOBAL_SETTING_TOAST_ENABLED", _backupNocGlobalToast.Value, RegistryValueKind.DWord);
                        _logger.LogSuccess($"[ImmersiveEnv] [FocusAssist] ✅ NOC_GLOBAL_SETTING_TOAST_ENABLED restaurado = {_backupNocGlobalToast.Value}");
                    }
                }
                else
                {
                    _logger.LogInfo("[ImmersiveEnv] [FocusAssist] Sem backup de NOC_GLOBAL_SETTING_TOAST_ENABLED — restaurando para 1 (padrão).");
                    using var key = Registry.CurrentUser.OpenSubKey(NOTIFICATIONS_KEY, writable: true);
                    key?.SetValue("NOC_GLOBAL_SETTING_TOAST_ENABLED", 1, RegistryValueKind.DWord);
                }

                // ── Restaurar Chave 2: Win11 22H2+ DND ──
                if (IsWin11_22H2OrLater() && _backupDndEnabled.HasValue)
                {
                    using var dndKey = Registry.CurrentUser.OpenSubKey(DND_KEY_WIN11, writable: true);
                    if (dndKey != null)
                    {
                        dndKey.SetValue(DND_VALUE_WIN11, _backupDndEnabled.Value, RegistryValueKind.DWord);
                        _logger.LogSuccess($"[ImmersiveEnv] [FocusAssist] ✅ Win11 DND restaurado = {_backupDndEnabled.Value}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ImmersiveEnv] [FocusAssist] ⚠️ Erro ao restaurar: {ex.Message}");
            }
        }

        // ══════════════════════════════════════════════════════════════════════════
        // TOASTS
        // ══════════════════════════════════════════════════════════════════════════

        private void DisableToasts()
        {
            _logger.LogInfo("[ImmersiveEnv] [Toasts] Desabilitando notificações Toast...");
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(PUSH_NOTIF_KEY, writable: true);
                if (key != null)
                {
                    var val = key.GetValue("ToastEnabled");
                    _backupToastEnabled = val is int iv ? iv : 1;
                    _logger.LogInfo($"[ImmersiveEnv] [Toasts] Backup ToastEnabled = {_backupToastEnabled}");
                    key.SetValue("ToastEnabled", 0, RegistryValueKind.DWord);
                    _logger.LogSuccess("[ImmersiveEnv] [Toasts] ✅ ToastEnabled = 0");
                }
                else
                {
                    _logger.LogWarning("[ImmersiveEnv] [Toasts] Não foi possível abrir/criar PUSH_NOTIF_KEY.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ImmersiveEnv] [Toasts] ⚠️ Erro ao desabilitar: {ex.Message}");
            }
        }

        private void RestoreToasts()
        {
            _logger.LogInfo("[ImmersiveEnv] [Toasts] Restaurando notificações Toast...");
            try
            {
                int valueToRestore = _backupToastEnabled ?? 1;
                _logger.LogInfo($"[ImmersiveEnv] [Toasts] Restaurando ToastEnabled = {valueToRestore} (backup={_backupToastEnabled?.ToString() ?? "null → usando 1"})");

                using var key = Registry.CurrentUser.CreateSubKey(PUSH_NOTIF_KEY, writable: true);
                key?.SetValue("ToastEnabled", valueToRestore, RegistryValueKind.DWord);
                _logger.LogSuccess($"[ImmersiveEnv] [Toasts] ✅ ToastEnabled restaurado = {valueToRestore}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ImmersiveEnv] [Toasts] ⚠️ Erro ao restaurar: {ex.Message}");
            }
        }

        // ══════════════════════════════════════════════════════════════════════════
        // SLEEP / MONITOR TIMEOUT
        // ✅ FIX BUG #1: Backup real via powercfg /query antes de alterar.
        //    Restauração usa os valores originais capturados, não hardcoded.
        // ══════════════════════════════════════════════════════════════════════════

        private void PreventSleep()
        {
            if (_isSleepDisabled) return;
            
            _logger.LogInfo("[ImmersiveEnv] [Sleep] Capturando timeouts atuais antes de alterar via API Nativa...");
            try
            {
                Guid activeGuid = Guid.Empty;
                if (VoltrisOptimizer.Utils.Win32.PowerNativeMethods.PowerGetActiveScheme(IntPtr.Zero, out IntPtr guidPtr) == 0)
                {
                    activeGuid = System.Runtime.InteropServices.Marshal.PtrToStructure<Guid>(guidPtr);
                    VoltrisOptimizer.Utils.Win32.PowerNativeMethods.LocalFree(guidPtr);
                }

                if (activeGuid != Guid.Empty)
                {
                    // GUID_SLEEP_IDLE = 29f6c1db-86da-48c5-9fdb-f2b67b1f44da
                    var videoGroupGuid = new Guid("7516b95f-f776-4464-8c53-06167f40cc99");
                    var sleepGroupGuid = new Guid("238C9FA8-0AAD-41ED-83F4-97BE242C8F20");
                    var sleepIdleGuid = new Guid("29f6c1db-86da-48c5-9fdb-f2b67b1f44da");
                    var displayTimeoutGuid = VoltrisOptimizer.Utils.Win32.PowerNativeMethods.GUID_DISPLAY_TIMEOUT;

                    // Capturar valores reais antes de alterar
                    _backupMonitorTimeoutAC = (int)(VoltrisOptimizer.Utils.Win32.PowerNativeMethods.GetPowerSettingAC(activeGuid, videoGroupGuid, displayTimeoutGuid) ?? 0) / 60;
                    VoltrisOptimizer.Utils.Win32.PowerNativeMethods.PowerReadDCValueIndex(IntPtr.Zero, ref activeGuid, ref videoGroupGuid, ref displayTimeoutGuid, out uint dcVideoVal);
                    _backupMonitorTimeoutDC = (int)dcVideoVal / 60;

                    _backupStandbyTimeoutAC = (int)(VoltrisOptimizer.Utils.Win32.PowerNativeMethods.GetPowerSettingAC(activeGuid, sleepGroupGuid, sleepIdleGuid) ?? 0) / 60;
                    VoltrisOptimizer.Utils.Win32.PowerNativeMethods.PowerReadDCValueIndex(IntPtr.Zero, ref activeGuid, ref sleepGroupGuid, ref sleepIdleGuid, out uint dcSleepVal);
                    _backupStandbyTimeoutDC = (int)dcSleepVal / 60;

                    _logger.LogInfo($"[ImmersiveEnv] [Sleep] Backup capturado:" +
                        $" monitor-ac={_backupMonitorTimeoutAC}min" +
                        $" monitor-dc={_backupMonitorTimeoutDC}min" +
                        $" standby-ac={_backupStandbyTimeoutAC}min" +
                        $" standby-dc={_backupStandbyTimeoutDC}min");

                    // [FIX:UNICO-DONO-DE-ENERGIA] Sleep/monitor NAO sao mais
                    // gravados aqui.
                    //
                    // O codigo gravava display timeout = 0 e standby timeout = 0
                    // (nunca dormir, nunca apagar a tela) e depois reativava o
                    // plano com ApplyCurrentPowerScheme().
                    //
                    // ApplyCurrentPowerScheme() e' uma TROCA DE PLANO: e' a
                    // mesma operacao que desligava o Perfil em todos os outros
                    // arquivos auditados, e aqui estava escondida dentro de um
                    // metodo chamado "Sleep" — a mais dificil de encontrar de
                    // todas. E "nunca dormir" numa bateria de notebook e' o
                    // recurso mais escasso do sistema, zerado sem que ninguem
                    // tenha pedido.
                    //
                    // O backup capturado acima (so leitura) segue valido e vai
                    // para o log.
                    _logger.LogInfo(
                        $"[ImmersiveEnv] [Sleep] Timeout NAO gravado. Backup capturado: " +
                        $"monitor-ac={_backupMonitorTimeoutAC}min, standby-ac={_backupStandbyTimeoutAC}min. " +
                        "Gravacao de power setting e reativacao de plano sao do Perfil.");

                    VoltrisOptimizer.Services.Power.ProfilePowerAuthority.RequestProfileApply(
                        "ImmersiveEnvironment.DisableSleep",
                        "pedido de ambiente imersivo",
                        null);

                    _isSleepDisabled = true;
                    _logger.LogSuccess("[ImmersiveEnv] [Sleep] Delegado ao Perfil (nenhum valor gravado).");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ImmersiveEnv] [Sleep] ⚠️ Erro ao configurar Sleep: {ex.Message}");
            }
        }

        private void RestoreSleep()
        {
            if (!_isSleepDisabled) return;
            
            _logger.LogInfo("[ImmersiveEnv] [Sleep] Restaurando timeouts de sleep via API Nativa...");
            try
            {
                Guid activeGuid = Guid.Empty;
                if (VoltrisOptimizer.Utils.Win32.PowerNativeMethods.PowerGetActiveScheme(IntPtr.Zero, out IntPtr guidPtr) == 0)
                {
                    activeGuid = System.Runtime.InteropServices.Marshal.PtrToStructure<Guid>(guidPtr);
                    VoltrisOptimizer.Utils.Win32.PowerNativeMethods.LocalFree(guidPtr);
                }

                if (activeGuid != Guid.Empty)
                {
                    int monAC  = _backupMonitorTimeoutAC  >= 0 ? _backupMonitorTimeoutAC  : 20;
                    int monDC  = _backupMonitorTimeoutDC  >= 0 ? _backupMonitorTimeoutDC  : 10;
                    int stdAC  = _backupStandbyTimeoutAC  >= 0 ? _backupStandbyTimeoutAC  : 60;
                    int stdDC  = _backupStandbyTimeoutDC  >= 0 ? _backupStandbyTimeoutDC  : 30;

                    _logger.LogInfo($"[ImmersiveEnv] [Sleep] Restaurando:" +
                        $" monitor-ac={monAC}min (backup={_backupMonitorTimeoutAC})" +
                        $" monitor-dc={monDC}min (backup={_backupMonitorTimeoutDC})" +
                        $" standby-ac={stdAC}min (backup={_backupStandbyTimeoutAC})" +
                        $" standby-dc={stdDC}min (backup={_backupStandbyTimeoutDC})");

                    var videoGroupGuid = new Guid("7516b95f-f776-4464-8c53-06167f40cc99");
                    var sleepGroupGuid = new Guid("238C9FA8-0AAD-41ED-83F4-97BE242C8F20");
                    var sleepIdleGuid = new Guid("29f6c1db-86da-48c5-9fdb-f2b67b1f44da");
                    var displayTimeoutGuid = VoltrisOptimizer.Utils.Win32.PowerNativeMethods.GUID_DISPLAY_TIMEOUT;

                    // [FIX:UNICO-DONO-DE-ENERGIA] A restauracao tambem nao grava.
                    // Ver DisableSleep para o porquê.
                    _logger.LogInfo($"[ImmersiveEnv] [Sleep] Restaurando via Perfil (backup era: " +
                        $"monitor-ac={monAC}min, standby-ac={stdAC}min) - gravacao neutralizada.");

                    VoltrisOptimizer.Services.Power.ProfilePowerAuthority.RequestProfileApply(
                        "ImmersiveEnvironment.RestoreSleep",
                        "fim do ambiente imersivo",
                        null);

                    _isSleepDisabled = false;
                    _logger.LogSuccess("[ImmersiveEnv] [Sleep] Restauracao delegada ao Perfil.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ImmersiveEnv] [Sleep] ⚠️ Erro ao restaurar Sleep: {ex.Message}");
            }
        }

        // ══════════════════════════════════════════════════════════════════════════
        // WINDOWS UPDATE
        // ✅ FIX BUG #6: Verb = "runas" removido — processo pai já é admin.
        //    UseShellExecute = false é incompatível com Verb = "runas" de qualquer forma.
        // ══════════════════════════════════════════════════════════════════════════

        private void PauseWindowsUpdate()
        {
            _logger.LogInfo("[ImmersiveEnv] [WinUpdate] Pausando Windows Update (net stop wuauserv)...");
            try
            {
                // ✅ FIX: Sem Verb = "runas" — o processo pai já roda como Administrador.
                //    Verb = "runas" com UseShellExecute = false causava UAC prompt inesperado.
                var psi = new ProcessStartInfo("net", "stop wuauserv")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var proc = Process.Start(psi);
                var stdout = proc?.StandardOutput.ReadToEnd() ?? "";
                var stderr = proc?.StandardError.ReadToEnd() ?? "";
                proc?.WaitForExit(15000);
                _logger.LogInfo($"[ImmersiveEnv] [WinUpdate] net stop wuauserv — ExitCode={proc?.ExitCode} stdout={stdout.Trim()} stderr={stderr.Trim()}");
                _logger.LogSuccess("[ImmersiveEnv] [WinUpdate] ✅ Windows Update pausado.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ImmersiveEnv] [WinUpdate] ⚠️ Não foi possível pausar Windows Update: {ex.Message}");
            }
        }

        private void RestoreWindowsUpdate()
        {
            _logger.LogInfo("[ImmersiveEnv] [WinUpdate] Restaurando Windows Update (net start wuauserv)...");
            try
            {
                // ✅ FIX: Sem Verb = "runas"
                var psi = new ProcessStartInfo("net", "start wuauserv")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var proc = Process.Start(psi);
                var stdout = proc?.StandardOutput.ReadToEnd() ?? "";
                var stderr = proc?.StandardError.ReadToEnd() ?? "";
                proc?.WaitForExit(15000);
                _logger.LogInfo($"[ImmersiveEnv] [WinUpdate] net start wuauserv — ExitCode={proc?.ExitCode} stdout={stdout.Trim()} stderr={stderr.Trim()}");
                _logger.LogSuccess("[ImmersiveEnv] [WinUpdate] ✅ Windows Update restaurado.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ImmersiveEnv] [WinUpdate] ⚠️ Não foi possível restaurar Windows Update: {ex.Message}");
            }
        }

        // ══════════════════════════════════════════════════════════════════════════
        // MANUTENÇÃO AUTOMÁTICA
        // ══════════════════════════════════════════════════════════════════════════

        private void PauseAutomaticMaintenance()
        {
            _logger.LogInfo("[ImmersiveEnv] [Maintenance] Pausando manutenção automática do Windows...");
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(MAINT_KEY, writable: true);
                if (key != null)
                {
                    var val = key.GetValue("MaintenanceDisabled");
                    _backupMaintenanceDisabled = val is int iv ? iv : 0;
                    _logger.LogInfo($"[ImmersiveEnv] [Maintenance] Backup MaintenanceDisabled = {_backupMaintenanceDisabled}");
                    key.SetValue("MaintenanceDisabled", 1, RegistryValueKind.DWord);
                    _logger.LogSuccess("[ImmersiveEnv] [Maintenance] ✅ Manutenção automática desativada.");
                }
                else
                {
                    _logger.LogWarning("[ImmersiveEnv] [Maintenance] Chave de manutenção não encontrada.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ImmersiveEnv] [Maintenance] ⚠️ Erro ao pausar manutenção: {ex.Message}");
            }
        }

        private void RestoreAutomaticMaintenance()
        {
            if (!_backupMaintenanceDisabled.HasValue) return;
            _logger.LogInfo("[ImmersiveEnv] [Maintenance] Restaurando manutenção automática...");
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(MAINT_KEY, writable: true);
                if (key != null)
                {
                    key.SetValue("MaintenanceDisabled", _backupMaintenanceDisabled.Value, RegistryValueKind.DWord);
                    _logger.LogSuccess($"[ImmersiveEnv] [Maintenance] ✅ MaintenanceDisabled restaurado = {_backupMaintenanceDisabled.Value}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ImmersiveEnv] [Maintenance] ⚠️ Erro ao restaurar: {ex.Message}");
            }
        }

        // ══════════════════════════════════════════════════════════════════════════
        // WAKE TIMERS
        // ══════════════════════════════════════════════════════════════════════════

        private void DisableWakeTimers()
        {
            if (_isWakeTimerDisabled) return;
            _logger.LogInfo("[ImmersiveEnv] [WakeTimers] Desabilitando Wake Timers...");
            try
            {
                // [FIX:UNICO-DONO-DE-ENERGIA] Wake timers NAO sao mais gravados.
                //
                // Este metodo rodava tres `powercfg` por Process.Start:
                // -setacvalueindex e -setdcvalueindex em SUB_SLEEP RTCWAKE 0,
                // seguidos de -setactive SCHEME_CURRENT.
                //
                // RTCWAKE e' do subgrupo SLEEP, nao PROCESSOR: e' o que impede o
                // Windows de acordar a maquina para manutencao (atualizacoes,
                // antivirus). O Perfil Inteligente trabalha no PROCESSOR e nao
                // enxerga este setting — portanto esta gravacao nunca foi
                // conflito com o perfil, e sim uma alteracao de sistema de outra
                // categoria feita por um servico cujo nome sugere ajuste visual.
                //
                // Sai por duas razoes, e a segunda decide:
                //  1) o -setactive no fim e' uma TROCA DE PLANO, e desligava o
                //     Perfil;
                //  2) desabilitar wake timers tem efeito proprio: com eles
                //     desligados, atualizacoes e varreduras podem ficar
                //     pendentes ate a proxima ligacao manual do usuario.
                VoltrisOptimizer.Services.Power.ProfilePowerAuthority.RequestProfileApply(
                    "ImmersiveEnvironment.DisableWakeTimers",
                    "ambiente imersivo pede desativacao de wake timers",
                    null);

                _isWakeTimerDisabled = true;
                _logger.LogSuccess("[ImmersiveEnv] [WakeTimers] Delegado ao Perfil (nenhum valor gravado).");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ImmersiveEnv] [WakeTimers] ⚠️ Erro ao desabilitar: {ex.Message}");
            }
        }

        private void RestoreWakeTimers()
        {
            if (!_isWakeTimerDisabled) return;
            _logger.LogInfo("[ImmersiveEnv] [WakeTimers] Restaurando Wake Timers...");
            try
            {
                // [FIX:UNICO-DONO-DE-ENERGIA] A restauracao tambem nao grava.
                // Ver DisableWakeTimers para o que o metodo fazia.
                VoltrisOptimizer.Services.Power.ProfilePowerAuthority.RequestProfileApply(
                    "ImmersiveEnvironment.RestoreWakeTimers",
                    "fim do ambiente imersivo",
                    null);

                _isWakeTimerDisabled = false;
                _logger.LogSuccess("[ImmersiveEnv] [WakeTimers] Restauracao delegada ao Perfil.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ImmersiveEnv] [WakeTimers] ⚠️ Erro ao restaurar: {ex.Message}");
            }
        }

        // ══════════════════════════════════════════════════════════════════════════
        // SERVIÇOS — DELEGADO AO WindowsServiceOptimizer
        // ══════════════════════════════════════════════════════════════════════════
    }
}
