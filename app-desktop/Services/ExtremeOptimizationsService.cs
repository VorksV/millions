using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using System.ServiceProcess;
using System.Text.Json;
using Microsoft.Win32;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Utils;
using VoltrisOptimizer.Services.Power;

namespace VoltrisOptimizer.Services
{
    public class ExtremeOptimizationsService
    {
        private readonly ILoggingService _logger;
        private readonly VoltrisOptimizer.Services.SystemChanges.ISystemChangeTransactionService? _txService;
        private VoltrisOptimizer.Services.SystemChanges.ISystemTransaction? _currentTx;
        private readonly SystemValidationService _validator;
        private readonly bool _isGamingMode;
        private readonly bool _isThermalConstrained;
        private readonly bool _isOnBattery;
        public bool DryRun { get; set; } = false; // false = aplica mudanças reais para usuários finais
        public bool AllowBackgroundWatchdog { get; set; } = false;
        public bool RequireConfirmationForCritical { get; set; } = true;
        private readonly string _backupDir;
        private readonly string _backupFile;
        private readonly string _powerPlanBackupFile;
        private string? _originalPlanGuid;
        private string? _prevHagsValue;
        private int? _prevVrrValue;
        private readonly string _netshBackupFile;
        private Thread? _dpcWatchdogThread;
        private volatile bool _dpcWatchdogRunning = false;
        private CancellationTokenSource? _dpcWatchdogCts;
        private class Caps { public int RamGb; public int CpuCores; public string GpuVendor = ""; public bool HasDGpu; public bool HagsSupport; }
        private int? _prevTdrDelay;
        private int? _prevTdrLevel;
        private int? _prevTdrDdiDelay;
        private int? _prevDciTimeout;
        private int? _prevVisualFx;
        private int? _prevTaskbarAnimations;
        private string? _prevUserPreferencesMask;
        private int? _prevBackgroundAppsDisabled;
        private int? _prevNvStereoEnable;
        private int? _prevNvStereoEnableByDefault;
        private int? _prevNvStereoMode;
        private int? _prevNvStereoPredefinedMode;
        private int? _prevNvStereoPredefinedModeDx10;

        public ExtremeOptimizationsService(ILoggingService logger, VoltrisOptimizer.Services.SystemChanges.ISystemChangeTransactionService? txService = null, bool isGamingMode = false, bool isThermalConstrained = false, bool isOnBattery = false)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _txService = txService;
            _isGamingMode = isGamingMode;
            _isThermalConstrained = isThermalConstrained;
            _isOnBattery = isOnBattery;
            _validator = new SystemValidationService(logger);
            _backupDir = Path.Combine(Path.GetTempPath(), "VoltrisOptimizer_ExtremeOptimizations_Backups");
            _backupFile = Path.Combine(_backupDir, "extreme_optimizations_backup.json");
            _powerPlanBackupFile = Path.Combine(_backupDir, "power_plan_backup.txt");
            _netshBackupFile = Path.Combine(_backupDir, "netsh_backup.txt");
            Directory.CreateDirectory(_backupDir);
        }

        /// <summary>
        /// Reinicia serviços críticos do Windows para liberar recursos e resolver travamentos leves.
        /// </summary>
        public bool RestartCriticalServices()
        {
            try
            {
                if (DryRun)
                {
                    _logger.LogInfo("[DRY-RUN] Reiniciar serviços críticos");
                    return true;
                }
                if (!AdminHelper.IsRunningAsAdministrator())
                {
                    _logger.LogWarning("Permissões insuficientes para reiniciar serviços");
                    return false;
                }

                var names = new[] { "Spooler", "WSearch", "Themes", "SysMain" };
                foreach (var name in names)
                {
                    try
                    {
                        using var sc = new ServiceController(name);
                        if (sc.Status == ServiceControllerStatus.Running || sc.Status == ServiceControllerStatus.Paused)
                        {
                            _logger.LogInfo($"[Extreme] Reiniciando serviço: {name}...");
                            sc.Stop();
                            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                            sc.Start();
                            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Não foi possível reiniciar o serviço {name}: {ex.Message}");
                    }
                }
                _logger.LogSuccess("Serviços críticos foram reiniciados com sucesso.");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro geral ao reiniciar serviços críticos", ex);
                return false;
            }
        }

        public void OptimizeServices(bool enable)
        {
            try
            {
                if (DryRun)
                {
                    _logger.LogInfo(enable ? "[DRY-RUN] Pausar serviços" : "[DRY-RUN] Iniciar serviços");
                    return;
                }
                if (!AdminHelper.IsRunningAsAdministrator())
                {
                    _logger.LogWarning("Permissões insuficientes para controlar serviços");
                    return;
                }
                var names = new[] { "SysMain", "DiagTrack", "WSearch", "Spooler" };
                foreach (var name in names)
                {
                    try
                    {
                        using var sc = new ServiceController(name);
                        if (enable)
                        {
                            if (sc.Status != ServiceControllerStatus.Stopped && sc.Status != ServiceControllerStatus.StopPending)
                            {
                                sc.Stop();
                                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                                _logger.LogInfo($"Serviço {name} parado com sucesso");
                            }
                        }
                        else
                        {
                            if (sc.Status != ServiceControllerStatus.Running && sc.Status != ServiceControllerStatus.StartPending)
                            {
                                sc.Start();
                                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
                                _logger.LogInfo($"Serviço {name} iniciado com sucesso");
                            }
                        }
                    }
                    catch (InvalidOperationException ex)
                    {
                        _logger.LogWarning($"Serviço {name} não encontrado ou não acessível: {ex.Message}");
                    }
                    catch (System.ServiceProcess.TimeoutException ex)
                    {
                        _logger.LogError($"Timeout ao ajustar serviço {name}: {ex.Message}", ex);
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        _logger.LogError($"Permissões insuficientes para ajustar serviço {name}: {ex.Message}", ex);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Falha inesperada ao ajustar serviço {name}: {ex.Message}", ex);
                    }
                }
                _logger.LogSuccess(enable ? "Serviços pausados" : "Serviços iniciados");
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro em OptimizeServices", ex);
            }
        }

        public bool ApplyCpuSetsForProcess(int pid)
        {
            try
            {
                using var proc = System.Diagnostics.Process.GetProcessById(pid);
                var pcoreIds = CpuSetHelper.GetPcoreIds();
                if (pcoreIds.Length == 0)
                {
                    _logger.LogWarning("Nenhum P-core detectado no sistema");
                    return false;
                }
                
                int threadsAssigned = 0;
                int threadsFailed = 0;
                
                foreach (System.Diagnostics.ProcessThread t in proc.Threads)
                {
                    try
                    {
                        CpuSetHelper.AssignThreadToCpuSets(t.Id, pcoreIds);
                        threadsAssigned++;
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        _logger.LogWarning($"Permissão negada para thread {t.Id}: {ex.Message}");
                        threadsFailed++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Erro ao atribuir thread {t.Id} aos P-cores: {ex.Message}");
                        threadsFailed++;
                    }
                }
                
                _logger.LogSuccess($"Processo isolado em P-cores: {threadsAssigned} threads atribuídas, {threadsFailed} falharam");
                return threadsAssigned > 0;
            }
            catch (ArgumentException ex)
            {
                _logger.LogError($"Processo com PID {pid} não encontrado: {ex.Message}", ex);
                return false;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError($"Processo com PID {pid} já foi encerrado: {ex.Message}", ex);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erro inesperado ao aplicar CPU sets para PID {pid}: {ex.Message}", ex);
                return false;
            }
        }

        public void EnableHagsVrr(bool enable)
        {
            try
            {
                if (DryRun)
                {
                    _logger.LogInfo(enable ? "[DRY-RUN] Habilitar HAGS/VRR" : "[DRY-RUN] Desabilitar HAGS/VRR");
                    return;
                }
                if (!AdminHelper.IsRunningAsAdministrator())
                {
                    _logger.LogWarning("Permissões insuficientes para ajustar HAGS/VRR");
                    return;
                }
                using var gdr = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", true);
                if (gdr != null)
                {
                    var prev = gdr.GetValue("HWSchMode");
                    _prevHagsValue = prev?.ToString();
                    // Usar constantes para HAGS
                    var hagsValue = enable 
                        ? Core.Constants.SystemConstants.HagsSettings.Enabled 
                        : Core.Constants.SystemConstants.HagsSettings.Disabled;
                    gdr.SetValue("HWSchMode", hagsValue, Microsoft.Win32.RegistryValueKind.DWord);
                }
                using var gdr2 = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", true);
                if (gdr2 != null)
                {
                    var prevVrr = gdr2.GetValue("D3D12EnableVariableRefreshRate");
                    _prevVrrValue = prevVrr is int iv ? iv : (int?)null;
                    gdr2.SetValue("D3D12EnableVariableRefreshRate", enable ? 1 : 0, Microsoft.Win32.RegistryValueKind.DWord);
                }
                _logger.LogSuccess(enable ? "HAGS/VRR habilitados" : "HAGS/VRR desabilitados");
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro ao alternar HAGS/VRR", ex);
            }
        }

        public void RestoreHagsVrr()
        {
            try
            {
                using var gdr = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", true);
                if (gdr != null && _prevHagsValue != null)
                {
                    if (int.TryParse(_prevHagsValue, out var v)) gdr.SetValue("HWSchMode", v, Microsoft.Win32.RegistryValueKind.DWord);
                }
                using var gdr2 = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", true);
                if (gdr2 != null && _prevVrrValue.HasValue)
                {
                    gdr2.SetValue("D3D12EnableVariableRefreshRate", _prevVrrValue.Value, Microsoft.Win32.RegistryValueKind.DWord);
                }
                _logger.LogInfo("HAGS/VRR restaurados");
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro ao restaurar HAGS/VRR", ex);
            }
        }

        /// <summary>
        /// Habilita ou desabilita HAGS (Hardware-accelerated GPU Scheduling)
        /// </summary>
        /// <param name="enable">True para habilitar, false para desabilitar</param>
        /// <returns>True se a operação foi bem-sucedida</returns>
        public bool ToggleHAGS(bool enable)
        {
            try
            {
                _currentTx = _txService?.Begin("Extreme.ToggleHAGS");
                if (DryRun)
                {
                    _logger.LogInfo($"[DRY-RUN] Toggle HAGS: {(enable ? "habilitar" : "desabilitar")}");
                    return true;
                }
                
                if (!AdminHelper.IsRunningAsAdministrator())
                {
                    _logger.LogWarning("Permissões de administrador necessárias para alterar HAGS");
                    return false;
                }

                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", true);
                
                if (key == null)
                {
                    _logger.LogWarning("Chave de registro GraphicsDrivers não encontrada");
                    return false;
                }

                // HwSchMode: 1 = Desabilitado, 2 = Habilitado
                var value = enable 
                    ? Core.Constants.SystemConstants.HagsSettings.Enabled 
                    : Core.Constants.SystemConstants.HagsSettings.Disabled;
                
                var prev = key.GetValue("HwSchMode");
                _currentTx?.RegisterRegistryChange("SYSTEM\\CurrentControlSet\\Control\\GraphicsDrivers", "HwSchMode", prev, value, false);
                key.SetValue("HwSchMode", value, Microsoft.Win32.RegistryValueKind.DWord);
                
                _logger.LogSuccess($"HAGS {(enable ? "habilitado" : "desabilitado")}. Reinicie o PC para aplicar.");
                _currentTx?.Commit();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erro ao alterar HAGS: {ex.Message}", ex);
                return false;
            }
            finally { _currentTx?.Dispose(); }
        }

        /// <summary>
        /// Otimiza o tratamento de interrupções para reduzir latência de DPC
        /// </summary>
        /// <returns>True se a operação foi bem-sucedida</returns>
        public bool OptimizeInterruptHandling()
        {
            try
            {
                _currentTx = _txService?.Begin("Extreme.OptimizeInterruptHandling");
                if (DryRun)
                {
                    _logger.LogInfo("[DRY-RUN] Otimizar tratamento de interrupções");
                    return true;
                }
                
                if (!AdminHelper.IsRunningAsAdministrator())
                {
                    _logger.LogWarning("Permissões de administrador necessárias para otimizar interrupções");
                    return false;
                }

                bool success = true;

                // 1. Desabilitar throttling de rede para reduzir latência
                try
                {
                    using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", true);
                    
                    if (key != null)
                    {
                        var prevNti = key.GetValue("NetworkThrottlingIndex");
                        _currentTx?.RegisterRegistryChange("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile", "NetworkThrottlingIndex", prevNti, unchecked((int)0xFFFFFFFF), false);
                        key.SetValue("NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), Microsoft.Win32.RegistryValueKind.DWord);
                        var prevResp = key.GetValue("SystemResponsiveness");
                        _currentTx?.RegisterRegistryChange("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile", "SystemResponsiveness", prevResp, 5, false);
                        key.SetValue("SystemResponsiveness", 5, Microsoft.Win32.RegistryValueKind.DWord);
                        _logger.LogInfo("Network throttling desabilitado, responsividade do sistema otimizada");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Erro ao otimizar throttling: {ex.Message}");
                    success = false;
                }

                // 2. Otimizar configurações de tarefas para jogos
                try
                {
                    using var gamesKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", true)
                        ?? Microsoft.Win32.Registry.LocalMachine.CreateSubKey(
                            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games");
                    
                    if (gamesKey != null)
                    {
                        // Prioridade GPU máxima
                        gamesKey.SetValue("GPU Priority", 8, Microsoft.Win32.RegistryValueKind.DWord);
                        // Prioridade de thread alta
                        gamesKey.SetValue("Priority", 6, Microsoft.Win32.RegistryValueKind.DWord);
                        // Scheduling Category - High
                        gamesKey.SetValue("Scheduling Category", "High", Microsoft.Win32.RegistryValueKind.String);
                        // SFIO Priority - High
                        gamesKey.SetValue("SFIO Priority", "High", Microsoft.Win32.RegistryValueKind.String);
                        _logger.LogInfo("Prioridades de tarefas de jogos otimizadas");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Erro ao otimizar prioridades de jogos: {ex.Message}");
                    success = false;
                }

                // 3. Reduzir latência de timer do Windows
                // Timer de resolução gerenciado pelo TimerResolutionService — sem bcdedit
                _logger.LogInfo("Timer de latência gerenciado via TimerResolutionService");

                if (success)
                {
                    _logger.LogSuccess("Tratamento de interrupções otimizado. Reinicie o PC para aplicar todas as mudanças.");
                    _currentTx?.Commit();
                }
                
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erro ao otimizar interrupções: {ex.Message}", ex);
                return false;
            }
        }

        public bool ApplyQosDscpForApp(string exePath, int dscp = 46)
        {
            try
            {
                if (DryRun)
                {
                    _logger.LogInfo($"[DRY-RUN] Aplicar QoS DSCP {dscp} para {exePath}");
                    return true;
                }
                if (!AdminHelper.IsRunningAsAdministrator())
                {
                    _logger.LogWarning("Permissões insuficientes para políticas de máquina (HKLM)");
                    return false;
                }
                var keyRoot = @"SOFTWARE\Policies\Microsoft\Windows\QoS";
                using var qos = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyRoot, true) ?? Microsoft.Win32.Registry.LocalMachine.CreateSubKey(keyRoot);
                if (qos == null) return false;
                var name = $"VoltrisQoS_{System.IO.Path.GetFileName(exePath)}";
                using var policy = qos.CreateSubKey(name);
                policy?.SetValue("Version", 1, Microsoft.Win32.RegistryValueKind.DWord);
                policy?.SetValue("AppPathName", exePath, Microsoft.Win32.RegistryValueKind.String);
                policy?.SetValue("DSCPValue", dscp, Microsoft.Win32.RegistryValueKind.DWord);
                policy?.SetValue("ThrottleRate", 0, Microsoft.Win32.RegistryValueKind.DWord);
                _logger.LogSuccess($"QoS DSCP {dscp} aplicado para {exePath}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro ao aplicar QoS DSCP", ex);
                return false;
            }
        }

        public void RemoveQosDscpForApp(string exePath)
        {
            try
            {
                if (DryRun)
                {
                    _logger.LogInfo($"[DRY-RUN] Remover QoS DSCP para {exePath}");
                    return;
                }
                if (!AdminHelper.IsRunningAsAdministrator())
                {
                    _logger.LogWarning("Permissões insuficientes para políticas de máquina (HKLM)");
                    return;
                }
                var keyRoot = @"SOFTWARE\Policies\Microsoft\Windows\QoS";
                using var qos = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyRoot, true);
                if (qos == null) return;
                var name = $"VoltrisQoS_{System.IO.Path.GetFileName(exePath)}";
                qos.DeleteSubKeyTree(name, false);
            }
            catch { }
        }

        public bool ApplyTcpAutotuneRssRsc()
        {
            try
            {
                if (DryRun)
                {
                    _logger.LogInfo("[DRY-RUN] Ajustar autotune/RSS/RSC");
                    return true;
                }
                RunProcess("netsh", "int tcp show global", out var o, out var e);
                try { System.IO.File.WriteAllText(_netshBackupFile, o + "\n" + e); } catch { }
                var ok1 = RunProcess("netsh", "int tcp set global autotuninglevel=normal", out var o1, out var e1);
                var ok2 = RunProcess("netsh", "int tcp set global rss=enabled", out var o2, out var e2);
                var ok3 = RunProcess("netsh", "int tcp set global rsc=enabled", out var o3, out var e3);
                if (!string.IsNullOrEmpty(e1)) _logger.LogWarning(e1);
                if (!string.IsNullOrEmpty(e2)) _logger.LogWarning(e2);
                if (!string.IsNullOrEmpty(e3)) _logger.LogWarning(e3);
                var ok = ok1 && ok2 && ok3;
                if (ok) _logger.LogSuccess("TCP autotune/RSS/RSC aplicados");
                return ok;
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro ao aplicar TCP globals", ex);
                return false;
            }
        }

        public bool RestoreTcpGlobals()
        {
            try
            {
                if (DryRun)
                {
                    _logger.LogInfo("[DRY-RUN] Restaurar TCP globals");
                    return true;
                }
                // minimally revert to defaults
                var ok1 = RunProcess("netsh", "int tcp set global autotuninglevel=normal", out var _, out var e1);
                var ok2 = RunProcess("netsh", "int tcp set global rss=disabled", out var _, out var e2);
                var ok3 = RunProcess("netsh", "int tcp set global rsc=disabled", out var _, out var e3);
                if (!string.IsNullOrEmpty(e1)) _logger.LogWarning(e1);
                if (!string.IsNullOrEmpty(e2)) _logger.LogWarning(e2);
                if (!string.IsNullOrEmpty(e3)) _logger.LogWarning(e3);
                return ok1 && ok2 && ok3;
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro ao restaurar TCP globals", ex);
                return false;
            }
        }

        public void SetNicInterruptModeration(bool enable)
        {
            try
            {
                if (DryRun)
                {
                    _logger.LogInfo(enable ? "[DRY-RUN] NIC Interrupt Moderation ON" : "[DRY-RUN] NIC Interrupt Moderation OFF");
                    return;
                }
                if (!AdminHelper.IsRunningAsAdministrator())
                {
                    _logger.LogWarning("Permissões insuficientes para ajustar NIC (HKLM)");
                    return;
                }
                NetworkNicHelper.SetInterruptModeration(enable);
                _logger.LogInfo(enable ? "Interrupt Moderation habilitado na NIC" : "Interrupt Moderation desabilitado na NIC");
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro ao ajustar Interrupt Moderation", ex);
            }
        }

        public void ThrottleDwmAndUwp(bool throttle)
        {
            // PROPOSITALMENTE DESATIVADO PARA EVITAR STUTTERS NO ALT+TAB
            _logger.LogInfo(throttle ? "[Extreme] ThrottleDwmAndUwp ignorado (preservando Alt+Tab fluído)" : "[Extreme] Shell restaurado");
        }

        public void ApplyGpuTdrTweaks(bool enable)
        {
            try
            {
                _currentTx = _txService?.Begin("Extreme.ApplyGpuTdrTweaks");
                if (DryRun)
                {
                    _logger.LogInfo(enable ? "[DRY-RUN] Ajustar TDR/DCI" : "[DRY-RUN] Restaurar TDR/DCI");
                    return;
                }
                if (!AdminHelper.IsRunningAsAdministrator())
                {
                    _logger.LogWarning("Permissões insuficientes para ajustar TDR/DCI");
                    return;
                }
                
                // VALIDAÇÃO DE SEGURANÇA para TDR Level 0
                if (enable)
                {
                    var tdrLevel = Core.Constants.SystemConstants.TdrSettings.LevelDisabled;
                    var tdrDelay = Core.Constants.SystemConstants.TdrSettings.GamingDelay;
                    var validation = _validator.ValidateTdrChange(tdrLevel, tdrDelay);
                    
                    _validator.LogValidatedOperation("ApplyGpuTdrTweaks", validation);
                    
                    if (validation.Risk == SystemValidationService.RiskLevel.Critical)
                    {
                        _logger.LogWarning("⚠️ ATENÇÃO: Configuração TDR de alto risco está sendo aplicada");
                        foreach (var warning in validation.Warnings)
                        {
                            _logger.LogWarning(warning);
                        }
                    }
                }
                
                using var g = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", true);
                if (g != null)
                {
                    if (!enable)
                    {
                        if (_prevTdrDelay.HasValue) g.SetValue("TdrDelay", _prevTdrDelay.Value, Microsoft.Win32.RegistryValueKind.DWord); else g.DeleteValue("TdrDelay", false);
                        if (_prevTdrLevel.HasValue) g.SetValue("TdrLevel", _prevTdrLevel.Value, Microsoft.Win32.RegistryValueKind.DWord); else g.DeleteValue("TdrLevel", false);
                        if (_prevTdrDdiDelay.HasValue) g.SetValue("TdrDdiDelay", _prevTdrDdiDelay.Value, Microsoft.Win32.RegistryValueKind.DWord); else g.DeleteValue("TdrDdiDelay", false);
                    }
                    else
                    {
                        var d = g.GetValue("TdrDelay"); _prevTdrDelay = d is int iv ? iv : (int?)null;
                        var l = g.GetValue("TdrLevel"); _prevTdrLevel = l is int il ? il : (int?)null;
                        var dd = g.GetValue("TdrDdiDelay"); _prevTdrDdiDelay = dd is int id ? id : (int?)null;
                        // Usar constantes em vez de magic numbers
                        _currentTx?.RegisterRegistryChange("SYSTEM\\CurrentControlSet\\Control\\GraphicsDrivers", "TdrDelay", d, Core.Constants.SystemConstants.TdrSettings.GamingDelay, false);
                        g.SetValue("TdrDelay", Core.Constants.SystemConstants.TdrSettings.GamingDelay, Microsoft.Win32.RegistryValueKind.DWord);
                        _currentTx?.RegisterRegistryChange("SYSTEM\\CurrentControlSet\\Control\\GraphicsDrivers", "TdrLevel", l, Core.Constants.SystemConstants.TdrSettings.LevelDisabled, false);
                        g.SetValue("TdrLevel", Core.Constants.SystemConstants.TdrSettings.LevelDisabled, Microsoft.Win32.RegistryValueKind.DWord);
                        _currentTx?.RegisterRegistryChange("SYSTEM\\CurrentControlSet\\Control\\GraphicsDrivers", "TdrDdiDelay", dd, Core.Constants.SystemConstants.TdrSettings.DdiDelayDefault, false);
                        g.SetValue("TdrDdiDelay", Core.Constants.SystemConstants.TdrSettings.DdiDelayDefault, Microsoft.Win32.RegistryValueKind.DWord);
                    }
                }
                using var dci = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers\DCI", true) ?? Microsoft.Win32.Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers\DCI");
                if (dci != null)
                {
                    if (!enable)
                    {
                        if (_prevDciTimeout.HasValue) dci.SetValue("Timeout", _prevDciTimeout.Value, Microsoft.Win32.RegistryValueKind.DWord); else dci.DeleteValue("Timeout", false);
                    }
                    else
                    {
                        var t = dci.GetValue("Timeout"); _prevDciTimeout = t is int it ? it : (int?)null;
                        _currentTx?.RegisterRegistryChange("SYSTEM\\CurrentControlSet\\Control\\GraphicsDrivers\\DCI", "Timeout", t, 7, false);
                        dci.SetValue("Timeout", 7, Microsoft.Win32.RegistryValueKind.DWord);
                    }
                }
                _logger.LogInfo(enable ? "TDR/DCI ajustados" : "TDR/DCI restaurados");
                _currentTx?.Commit();
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro em ApplyGpuTdrTweaks", ex);
            }
            finally { _currentTx?.Dispose(); }
        }

        public void ApplyPerformanceVisuals(bool lean)
        {
            try
            {
                _currentTx = _txService?.Begin("Extreme.ApplyPerformanceVisuals");
                if (DryRun)
                {
                    _logger.LogInfo(lean ? "[DRY-RUN] Reduzir efeitos visuais" : "[DRY-RUN] Restaurar efeitos visuais");
                    return;
                }
                using var adv = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", true) ?? Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
                using var vis = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", true) ?? Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects");
                var pv = vis?.GetValue("VisualFXSetting"); _prevVisualFx = pv is int iv ? iv : (int?)null;
                var pa = adv?.GetValue("TaskbarAnimations"); _prevTaskbarAnimations = pa is int ia ? ia : (int?)null;
                using var desk = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true);
                var pum = desk?.GetValue("UserPreferencesMask"); _prevUserPreferencesMask = pum?.ToString();
                if (lean)
                {
                    _currentTx?.RegisterRegistryChange("Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\VisualEffects", "VisualFXSetting", pv, 2, true);
                    vis?.SetValue("VisualFXSetting", 2, Microsoft.Win32.RegistryValueKind.DWord);
                    _currentTx?.RegisterRegistryChange("Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced", "TaskbarAnimations", pa, 0, true);
                    adv?.SetValue("TaskbarAnimations", 0, Microsoft.Win32.RegistryValueKind.DWord);
                    _currentTx?.RegisterRegistryChange("Control Panel\\Desktop", "UserPreferencesMask", pum, "90 12 00 00 00 00 00 00 00 00 00 00 00 00 00 00", true);
                    // Converter string hexadecimal para array de bytes
                    var hexString = "90 12 00 00 00 00 00 00 00 00 00 00 00 00 00 00";
                    var hexBytes = hexString.Split(' ').Select(h => Convert.ToByte(h, 16)).ToArray();
                    desk?.SetValue("UserPreferencesMask", hexBytes, Microsoft.Win32.RegistryValueKind.Binary);
                }
                else
                {
                    if (_prevVisualFx.HasValue) vis?.SetValue("VisualFXSetting", _prevVisualFx.Value, Microsoft.Win32.RegistryValueKind.DWord); else vis?.DeleteValue("VisualFXSetting", false);
                    if (_prevTaskbarAnimations.HasValue) adv?.SetValue("TaskbarAnimations", _prevTaskbarAnimations.Value, Microsoft.Win32.RegistryValueKind.DWord); else adv?.DeleteValue("TaskbarAnimations", false);
                    if (!string.IsNullOrEmpty(_prevUserPreferencesMask))
                    {
                        // Se _prevUserPreferencesMask é uma string hex, converter para bytes
                        // Se já é um array de bytes (do GetValue), usar diretamente
                        byte[] maskBytes;
                        if (_prevUserPreferencesMask.Contains(' '))
                        {
                            // Formato string hex: "90 12 00 00..."
                            maskBytes = _prevUserPreferencesMask.Split(' ').Select(h => Convert.ToByte(h, 16)).ToArray();
                        }
                        else if (pum is byte[] prevBytes)
                        {
                            // Já é um array de bytes
                            maskBytes = prevBytes;
                        }
                        else
                        {
                            // Tentar converter de string sem espaços
                            var cleanHex = _prevUserPreferencesMask.Replace(" ", "").Replace("-", "");
                            maskBytes = new byte[cleanHex.Length / 2];
                            for (int i = 0; i < maskBytes.Length; i++)
                            {
                                maskBytes[i] = Convert.ToByte(cleanHex.Substring(i * 2, 2), 16);
                            }
                        }
                        desk?.SetValue("UserPreferencesMask", maskBytes, Microsoft.Win32.RegistryValueKind.Binary);
                    }
                }
                _logger.LogInfo(lean ? "Efeitos visuais reduzidos" : "Efeitos visuais restaurados");
                _currentTx?.Commit();
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro em ApplyPerformanceVisuals", ex);
            }
            finally { _currentTx?.Dispose(); }
        }

        public void ControlBackgroundServices(bool pause)
        {
            try
            {
                _currentTx = _txService?.Begin(pause ? "Extreme.PauseBackgroundServices" : "Extreme.ResumeBackgroundServices");
                if (DryRun)
                {
                    _logger.LogInfo(pause ? "[DRY-RUN] Pausar serviços secundários" : "[DRY-RUN] Reativar serviços secundários");
                    return;
                }
                if (!AdminHelper.IsRunningAsAdministrator())
                {
                    _logger.LogWarning("Permissões insuficientes para controlar serviços secundários");
                    return;
                }
                var names = new[] { "WSearch", "wuauserv", "w32time", "Dnscache", "BITS", "XblAuthManager", "XblGameSave", "XboxNetApiSvc" };
                foreach (var name in names)
                {
                    try
                    {
                        using var sc = new ServiceController(name);
                        if (pause)
                        {
                            if (sc.Status != ServiceControllerStatus.Stopped && sc.Status != ServiceControllerStatus.StopPending)
                            {
                                sc.Stop();
                                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                                _currentTx?.RegisterServiceChange(name, null, null);
                            }
                        }
                        else
                        {
                            sc.Start();
                            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
                            _currentTx?.RegisterServiceChange(name, null, null);
                        }
                    }
                    catch { }
                }
                _currentTx?.Commit();
                _logger.LogInfo(pause ? "Serviços secundários pausados" : "Serviços secundários reativados");
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro em ControlBackgroundServices", ex);
            }
            finally { _currentTx?.Dispose(); }
        }

        public void ApplyBackgroundAppsControl(bool disable)
        {
            try
            {
                if (DryRun)
                {
                    _logger.LogInfo(disable ? "[DRY-RUN] Desabilitar apps em background" : "[DRY-RUN] Restaurar apps em background");
                    return;
                }
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", true) ?? Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications");
                var prev = key?.GetValue("GlobalUserDisabled");
                _prevBackgroundAppsDisabled = prev is int iv ? iv : (int?)null;
                if (disable)
                {
                    key?.SetValue("GlobalUserDisabled", 1, Microsoft.Win32.RegistryValueKind.DWord);
                }
                else
                {
                    if (_prevBackgroundAppsDisabled.HasValue) key?.SetValue("GlobalUserDisabled", _prevBackgroundAppsDisabled.Value, Microsoft.Win32.RegistryValueKind.DWord); else key?.DeleteValue("GlobalUserDisabled", false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro em ApplyBackgroundAppsControl", ex);
            }
        }

        public void ApplyNvidiaStereo3DPolicy(bool disable)
        {
            try
            {
                if (DryRun)
                {
                    _logger.LogInfo(disable ? "[DRY-RUN] Desabilitar Stereo3D NVIDIA" : "[DRY-RUN] Restaurar Stereo3D NVIDIA");
                    return;
                }
                if (!AdminHelper.IsRunningAsAdministrator())
                {
                    _logger.LogWarning("Permissões insuficientes para ajustar Stereo3D");
                    return;
                }
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\NVIDIA Corporation\Global\Stereo3D", true) ?? Microsoft.Win32.Registry.LocalMachine.CreateSubKey(@"SOFTWARE\NVIDIA Corporation\Global\Stereo3D");
                var e = key?.GetValue("Enable"); _prevNvStereoEnable = e is int ei ? ei : (int?)null;
                var ed = key?.GetValue("Stereo3D_EnableByDefault"); _prevNvStereoEnableByDefault = ed is int edi ? edi : (int?)null;
                var m = key?.GetValue("Stereo3DMode"); _prevNvStereoMode = m is int mi ? mi : (int?)null;
                var pm = key?.GetValue("Stereo3DPredefinedMode"); _prevNvStereoPredefinedMode = pm is int pmi ? pmi : (int?)null;
                var pmdx = key?.GetValue("Stereo3DPredefinedModeDX10"); _prevNvStereoPredefinedModeDx10 = pmdx is int pdxi ? pdxi : (int?)null;
                if (disable)
                {
                    key?.SetValue("Enable", 0, Microsoft.Win32.RegistryValueKind.DWord);
                    key?.SetValue("Stereo3D_EnableByDefault", 0, Microsoft.Win32.RegistryValueKind.DWord);
                    key?.SetValue("Stereo3DMode", 2, Microsoft.Win32.RegistryValueKind.DWord);
                    key?.SetValue("Stereo3DPredefinedMode", 1, Microsoft.Win32.RegistryValueKind.DWord);
                    key?.SetValue("Stereo3DPredefinedModeDX10", 0, Microsoft.Win32.RegistryValueKind.DWord);
                }
                else
                {
                    if (_prevNvStereoEnable.HasValue) key?.SetValue("Enable", _prevNvStereoEnable.Value, Microsoft.Win32.RegistryValueKind.DWord); else key?.DeleteValue("Enable", false);
                    if (_prevNvStereoEnableByDefault.HasValue) key?.SetValue("Stereo3D_EnableByDefault", _prevNvStereoEnableByDefault.Value, Microsoft.Win32.RegistryValueKind.DWord); else key?.DeleteValue("Stereo3D_EnableByDefault", false);
                    if (_prevNvStereoMode.HasValue) key?.SetValue("Stereo3DMode", _prevNvStereoMode.Value, Microsoft.Win32.RegistryValueKind.DWord); else key?.DeleteValue("Stereo3DMode", false);
                    if (_prevNvStereoPredefinedMode.HasValue) key?.SetValue("Stereo3DPredefinedMode", _prevNvStereoPredefinedMode.Value, Microsoft.Win32.RegistryValueKind.DWord); else key?.DeleteValue("Stereo3DPredefinedMode", false);
                    if (_prevNvStereoPredefinedModeDx10.HasValue) key?.SetValue("Stereo3DPredefinedModeDX10", _prevNvStereoPredefinedModeDx10.Value, Microsoft.Win32.RegistryValueKind.DWord); else key?.DeleteValue("Stereo3DPredefinedModeDX10", false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro em ApplyNvidiaStereo3DPolicy", ex);
            }
        }

        public void StartDpcWatchdog()
        {
            if (!AllowBackgroundWatchdog) { _logger.LogWarning("Watchdog DPC desabilitado por configuração"); return; }
            try
            {
                AdvancedTweaksService? adv = App.Services?.GetService<AdvancedTweaksService>();
                adv?.EnableMsiModeForGpuAndNetwork();
                NetworkNicHelper.SetInterruptModeration(false);
                _logger.LogInfo("DPC Settings (MSI/Interrupt) aplicadas com sucesso (One-shot).");
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro ao aplicar DPC Settings", ex);
            }
        }

        public void StopDpcWatchdog()
        {
            try
            {
                _dpcWatchdogRunning = false;
                _dpcWatchdogCts?.Cancel();
                _dpcWatchdogCts?.Dispose();
                _dpcWatchdogCts = null;
                _dpcWatchdogThread = null;
                _logger.LogInfo("Watchdog DPC parado");
            }
            catch { }
        }

        public void ElevateGameProcessPriority(int pid)
        {
            try
            {
                // CORREÇÃO: Validar sistema antes de prioridade alta
                if (_isOnBattery || _isThermalConstrained)
                {
                    _logger.LogInfo("[Extreme] Process Priority: Pulando - Sistema é notebook/bateria/thermal constrained");
                    return;
                }
                
                using var p = System.Diagnostics.Process.GetProcessById(pid);
                
                // CORREÇÃO: Apenas High, nunca Realtime
                if (p.PriorityClass < ProcessPriorityClass.High)
                {
                    p.PriorityClass = ProcessPriorityClass.High;
                    _logger.LogInfo($"[Extreme] Processo {pid} elevado para High (seguro)");
                }
            }
            catch { }
        }

        public bool EnableLargePagesPrivilege()
        {
            try
            {
                return PrivilegeHelper.EnableSeLockMemoryPrivilege();
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro ao habilitar Large Pages", ex);
                return false;
            }
        }

        private Caps GetCaps()
        {
            var c = new Caps();
            try { c.CpuCores = Environment.ProcessorCount; } catch { c.CpuCores = 4; }
            try
            {
                long total = 0;
                using var ram = new System.Management.ManagementObjectSearcher("SELECT Capacity FROM Win32_PhysicalMemory");
                foreach (System.Management.ManagementObject obj in ram.Get()) { if (obj["Capacity"] != null && long.TryParse(obj["Capacity"].ToString(), out var cap)) total += cap; }
                c.RamGb = (int)(total / (1024L * 1024 * 1024));
            }
            catch { c.RamGb = 8; }
            try
            {
                int adapters = 0;
                using var gpu = new System.Management.ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
                foreach (System.Management.ManagementObject obj in gpu.Get())
                {
                    adapters++;
                    var name = obj["Name"]?.ToString() ?? "";
                    if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)) c.GpuVendor = "NVIDIA";
                    else if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase)) c.GpuVendor = "AMD";
                    else if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase)) c.GpuVendor = "Intel";
                }
                c.HasDGpu = adapters > 1 && c.GpuVendor != "Intel";
                c.HagsSupport = c.GpuVendor == "NVIDIA" || c.GpuVendor == "AMD";
            }
            catch { }
            return c;
        }

        public bool ApplyAdaptiveUnattendPolicies(string xmlPath)
        {
            try
            {
                if (DryRun)
                {
                    _logger.LogInfo("[DRY-RUN] Aplicar políticas adaptativas unattend");
                    return true;
                }
                var caps = GetCaps();
                try
                {
                    using var hkcuDvr = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", true);
                    hkcuDvr?.SetValue("GameDVR_Enabled", 0, Microsoft.Win32.RegistryValueKind.DWord);
                }
                catch { }
                if (VoltrisOptimizer.Utils.AdminHelper.IsRunningAsAdministrator())
                {
                    try
                    {
                        using var mm = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", true);
                        mm?.SetValue("SystemResponsiveness", 10, Microsoft.Win32.RegistryValueKind.DWord);
                        mm?.SetValue("NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), Microsoft.Win32.RegistryValueKind.DWord);
                    }
                    catch { }
                    try
                    {
                        using var games = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", true);
                        games?.SetValue("GPU Priority", 8, Microsoft.Win32.RegistryValueKind.DWord);
                        games?.SetValue("Priority", 6, Microsoft.Win32.RegistryValueKind.DWord);
                        games?.SetValue("Scheduling Category", "High", Microsoft.Win32.RegistryValueKind.String);
                    }
                    catch { }
                    try
                    {
                        using var prio = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\PriorityControl", true);
                        var val = (caps.RamGb <= 8 || caps.CpuCores <= 4) ? 0x20 : 0x26;
                        prio?.SetValue("Win32PrioritySeparation", val, Microsoft.Win32.RegistryValueKind.DWord);
                    }
                    catch { }
                    if (caps.HagsSupport)
                    {
                        try
                        {
                            using var gdr = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", true);
                            gdr?.SetValue("HwSchMode", 2, Microsoft.Win32.RegistryValueKind.DWord);
                        }
                        catch { }
                    }
                    try
                    {
                        using var pol = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\GameDVR", true);
                        pol?.SetValue("AllowGameDVR", 0, Microsoft.Win32.RegistryValueKind.DWord);
                    }
                    catch { }
                    try
                    {
                        using var stor = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\StorageSense", true);
                        stor?.SetValue("AllowStorageSenseGlobal", 0, Microsoft.Win32.RegistryValueKind.DWord);
                    }
                    catch { }
                    try
                    {
                        using var exp = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer", true);
                        exp?.SetValue("HideSCAMeetNow", 1, Microsoft.Win32.RegistryValueKind.DWord);
                    }
                    catch { }
                }
                try
                {
                    using var adv = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", true);
                    adv?.SetValue("TaskbarAnimations", 0, Microsoft.Win32.RegistryValueKind.DWord);
                    adv?.SetValue("VisualFXSetting", 3, Microsoft.Win32.RegistryValueKind.DWord);
                }
                catch { }
                _logger.LogSuccess("Políticas do unattend aplicadas de forma adaptativa");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro ao aplicar unattend", ex);
                return false;
            }
        }

        public bool BackupSettings()
        {
            try
            {
                var backup = new ExtremeBackup();
                using var kbd = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Kbdclass\Parameters", false);
                using var mou = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Mouclass\Parameters", false);
                using var pri = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\PriorityControl", false);
                if (kbd != null)
                {
                    var v = kbd.GetValue("KeyboardDataQueueSize");
                    backup.KeyboardDataQueueSize = v is int i ? i : (int?)null;
                }
                if (mou != null)
                {
                    var v = mou.GetValue("MouseDataQueueSize");
                    backup.MouseDataQueueSize = v is int i ? i : (int?)null;
                }
                if (pri != null)
                {
                    var v = pri.GetValue("Win32PrioritySeparation");
                    backup.Win32PrioritySeparation = v is int i ? i : (int?)null;
                }
                var json = JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true, ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                File.WriteAllText(_backupFile, json);
                _logger.LogSuccess("Backup criado");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro ao criar backup", ex);
                return false;
            }
        }

        public bool ApplyInputTweaks()
        {
            try
            {
                _currentTx = _txService?.Begin("Extreme.ApplyInputTweaks");
                if (DryRun)
                {
                    _logger.LogInfo("[DRY-RUN] Aplicar tweaks de input");
                    return true;
                }
                if (!AdminHelper.IsRunningAsAdministrator())
                {
                    _logger.LogWarning("Permissões insuficientes para HKLM");
                    return false;
                }
                BackupSettings();
                using var kbd = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Kbdclass\Parameters", true) ?? Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Services\Kbdclass\Parameters");
                using var mou = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Mouclass\Parameters", true) ?? Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Services\Mouclass\Parameters");
                using var pri = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\PriorityControl", true) ?? Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\PriorityControl");
                try { var prevK = kbd?.GetValue("KeyboardDataQueueSize"); _currentTx?.RegisterRegistryChange("SYSTEM\\CurrentControlSet\\Services\\Kbdclass\\Parameters", "KeyboardDataQueueSize", prevK, 100, false); kbd?.SetValue("KeyboardDataQueueSize", 100, RegistryValueKind.DWord); } catch (Exception ex) { _logger.LogError("Falha em KeyboardDataQueueSize", ex); }
                try { var prevM = mou?.GetValue("MouseDataQueueSize"); _currentTx?.RegisterRegistryChange("SYSTEM\\CurrentControlSet\\Services\\Mouclass\\Parameters", "MouseDataQueueSize", prevM, 100, false); mou?.SetValue("MouseDataQueueSize", 100, RegistryValueKind.DWord); } catch (Exception ex) { _logger.LogError("Falha em MouseDataQueueSize", ex); }
                try { var prevP = pri?.GetValue("Win32PrioritySeparation"); _currentTx?.RegisterRegistryChange("SYSTEM\\CurrentControlSet\\Control\\PriorityControl", "Win32PrioritySeparation", prevP, 26, false); pri?.SetValue("Win32PrioritySeparation", 26, RegistryValueKind.DWord); } catch (Exception ex) { _logger.LogError("Falha em Win32PrioritySeparation", ex); }
                _logger.LogSuccess("Tweaks de input aplicados");
                _currentTx?.Commit();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro ao aplicar tweaks de input", ex);
                return false;
            }
            finally { _currentTx?.Dispose(); }
        }

        public bool RestoreAll()
        {
            try
            {
                if (!File.Exists(_backupFile)) return false;
                var json = File.ReadAllText(_backupFile);
                var b = JsonSerializer.Deserialize<ExtremeBackup>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }) ?? new ExtremeBackup();
                using var kbd = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Kbdclass\Parameters", true) ?? Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Services\Kbdclass\Parameters");
                using var mou = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Mouclass\Parameters", true) ?? Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Services\Mouclass\Parameters");
                using var pri = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\PriorityControl", true) ?? Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\PriorityControl");
                try { SetOrDelete(kbd, "KeyboardDataQueueSize", b.KeyboardDataQueueSize); } catch (Exception ex) { _logger.LogError("Falha ao restaurar KeyboardDataQueueSize", ex); }
                try { SetOrDelete(mou, "MouseDataQueueSize", b.MouseDataQueueSize); } catch (Exception ex) { _logger.LogError("Falha ao restaurar MouseDataQueueSize", ex); }
                try { SetOrDelete(pri, "Win32PrioritySeparation", b.Win32PrioritySeparation); } catch (Exception ex) { _logger.LogError("Falha ao restaurar Win32PrioritySeparation", ex); }
                OptimizeServices(false);
                _logger.LogSuccess("Configurações restauradas");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro ao restaurar configurações", ex);
                return false;
            }
        }

        public bool EnableUltimatePerformance()
        {
            try
            {
                if (DryRun)
                {
                    _logger.LogInfo("[DRY-RUN] Ativar plano Ultimate Performance");
                    return true;
                }
                if (!AdminHelper.IsRunningAsAdministrator())
                {
                    _logger.LogWarning("Permissões insuficientes para powercfg");
                    return false;
                }
                _originalPlanGuid = GetActivePlanGuid();
                try
                {
                    File.WriteAllText(_powerPlanBackupFile, JsonSerializer.Serialize(new { Original = _originalPlanGuid }, new JsonSerializerOptions {  PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
                }
                catch (Exception ex) 
                { 
                    _logger.LogWarning($"Falha ao salvar backup do plano de energia: {ex.Message}");
                }
                
                // [FIX:UNICO-DONO-DE-ENERGIA] A duplicação de plano foi neutralizada.
                //
                // Este serviço ainda CRIAVA um plano novo com
                // `PowerDuplicateScheme` do "Ultimate Performance", e só depois
                // delegava ao Perfil. A delegação estava no lugar certo, mas
                // deixava o serviço fazendo a parte que só o dono pode fazer.
                //
                // A criação em si era o problema, e por um motivo que vai além
                // da disputa de autoridade: cada execução deixava um plano
                // órfão no sistema. O backup em `_powerPlanBackupFile` é
                // gravado a cada chamada, mas o plano duplicado nunca era
                // removido. "Ultimate Performance" não existe no Windows 10, e
                // este projeto roda em build 19044 — então a duplicação falhava
                // e o plano não era criado. Ainda assim, o caminho permanecia
                // ativo, e em Windows 11 teria acumulado um plano a cada
                // execução.
                //
                // O log original dizia "Ultimate Performance ativado", o que
                // seria falso: o Perfil escolhe o plano, e não é o Ultimate.
                _logger.LogInfo(
                    "[EXTREME] Criacao de plano neutralizada (antes: PowerDuplicateScheme do " +
                    "Ultimate Performance, que no Windows 10 nem existe e, quando existe, " +
                    "deixava um plano orfao a cada execucao). " +
                    "A energia e' do Perfil Inteligente.");

                bool ok = ProfilePowerAuthority.RequestProfileApply(
                    "ExtremeOptimizations.ActivateUltimatePerformance",
                    "otimizacoes extremas pedem energia",
                    _logger);

                if (ok)
                {
                    _logger.LogSuccess("Perfil Inteligente reaplicado");
                }
                return ok;
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro ao ativar Ultimate Performance", ex);
                return false;
            }
        }

        public bool RestorePowerPlan()
        {
            try
            {
                var guid = _originalPlanGuid;
                if (string.IsNullOrEmpty(guid))
                {
                    try
                    {
                        var json = File.Exists(_powerPlanBackupFile) ? File.ReadAllText(_powerPlanBackupFile) : "";
                        var obj = string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<PowerPlanBackup>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                        guid = obj?.Original;
                    }
                    catch { }
                }
                if (string.IsNullOrEmpty(guid)) return false;
                Guid target = new Guid(guid);
                
                bool ok = ProfilePowerAuthority.RequestProfileApply("ExtremeOptimizations_Restore", "pedido legado de troca de plano", _logger);
                if (ok) _logger.LogSuccess("Plano de energia original restaurado");
                return ok;
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro ao restaurar plano de energia", ex);
                return false;
            }
        }

        /// <summary>
        /// [FIX:M-9] Autorização que o DefaultCompatibilityPolicy delega.
        ///
        /// O DefaultCompatibilityPolicy (linha 53-55) barra Network_ResetStack com
        /// o comentário de que a decisão "depende de saber se há VPN/sessão
        /// remota/tráfego no momento" e portanto é responsabilidade de QUEM
        /// CHAMA. O único chamador possível (este método) verificava apenas se
        /// rodava como administrador — o que não tem relação nenhuma com
        /// derrubar a pilha de rede de alguém.
        ///
        /// Consequência: um clique em "otimizar rede" executava
        /// <c>netsh winsock reset</c> + <c>netsh int ip reset</c> com uma VPN
        /// conectada (derrubando o túnel), com uma sessão remota ativa (derrubando
        /// o RDP), ou durante um download/jogo (derrubando a transferência) —
        /// e ainda oferecia reiniciar a máquina.
        ///
        /// A trava do policy continua. O que faltava era implementar a
        /// autorização que ela exige, e é isso que este método faz. Cada bloqueio
        /// é registrado com o motivo, para que a decisão seja auditável em vez de
        /// opaca.
        /// </summary>
        private bool CanSafelyResetNetworkStack(out string motivo)
        {
            // 1. Tráfego de rede ativo. Mede o delta de bytes recebidos/enviados
            //    em duas amostras. Um jogo online, um download e uma chamada de
            //    vídeo aparecem todos aqui.
            try
            {
                long bytesAntes = 0;
                foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                    bytesAntes += nic.GetIPv4Statistics().BytesReceived + nic.GetIPv4Statistics().BytesSent;
                }

                Thread.Sleep(1200);

                long bytesDepois = 0;
                foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                    bytesDepois += nic.GetIPv4Statistics().BytesReceived + nic.GetIPv4Statistics().BytesSent;
                }

                long deltaBytes = Math.Abs(bytesDepois - bytesAntes);
                if (deltaBytes > 32 * 1024) // ~27 KB/s: trafego humano, nao keepalive
                {
                    motivo = $"trafego de rede ativo durante a medicao ({deltaBytes / 1024} KB em 1,2s)";
                    return false;
                }
            }
            catch (Exception ex)
            {
                // Falha na medição não bloqueia: é melhor resetar sem dado do que
                // nunca resetar. O log deixa a cegueira explícita.
                _logger.LogWarning($"[FIX:M-9] Nao foi possivel medir trafego de rede: {ex.Message}");
            }

            // 2. Conexoes TCP estabelecidas. Mais barato que a medicao de
            //    throughput e pega sessao remota / VPN mesmo com pouco trafego.
            try
            {
                var conexoes = System.Net.NetworkInformation.IPGlobalProperties
                    .GetIPGlobalProperties()
                    .GetActiveTcpConnections()
                    .Where(c => c.State == System.Net.NetworkInformation.TcpState.Established)
                    .Where(c => !IPAddress.IsLoopback(c.RemoteEndPoint.Address))
                    .ToList();

                if (conexoes.Count > 0)
                {
                    motivo = $"{conexoes.Count} conexao(oes) de rede estabelecidas " +
                             $"(ex.: {string.Join(", ", conexoes.Take(3).Select(c => c.RemoteEndPoint.Address))})";
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[FIX:M-9] Nao foi possivel enumerar conexoes TCP: {ex.Message}");
            }

            // 3. Adaptador de VPN ativo. Um winsock reset destroi o tunel; o
            //    usuario teria que reconectar manualmente, e em alguns clientes
            //    a reconexao exige novo login.
            try
            {
                var vpn = System.Net.NetworkInformation.NetworkInterface
                    .GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                    .Where(n => n.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Tunnel
                             || n.Name.IndexOf("vpn", StringComparison.OrdinalIgnoreCase) >= 0
                             || n.Description.IndexOf("vpn", StringComparison.OrdinalIgnoreCase) >= 0
                             || n.Description.IndexOf("tap", StringComparison.OrdinalIgnoreCase) >= 0
                             || n.Description.IndexOf("wintun", StringComparison.OrdinalIgnoreCase) >= 0
                             || n.Description.IndexOf("wireguard", StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(n => n.Name)
                    .ToList();

                if (vpn.Count > 0)
                {
                    motivo = $"adaptador de VPN ativo ({string.Join(", ", vpn)})";
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[FIX:M-9] Nao foi possivel inspecionar adaptadores de VPN: {ex.Message}");
            }

            // 4. Sessao remota (RDP). Um winsock reset durante RDP desconecta o
            //    usuario da maquina que ele esta operando.
            try
            {
                if (Environment.UserInteractive)
                {
                    var sessao = System.Diagnostics.Process.GetProcesses()
                        .FirstOrDefault(p => string.Equals(p.ProcessName, "qwinsta", StringComparison.OrdinalIgnoreCase)
                                          || string.Equals(p.ProcessName, "mstsc", StringComparison.OrdinalIgnoreCase));
                    if (sessao != null)
                    {
                        motivo = $"cliente de sessao remota em execucao ({sessao.ProcessName})";
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[FIX:M-9] Nao foi possivel verificar sessao remota: {ex.Message}");
            }

            motivo = "nenhum bloqueio detectado (sem trafego, sem conexoes, sem VPN, sem sessao remota)";
            return true;
        }

        public bool OptimizeNetworkStack(bool scheduleReboot = false)
        {
            try
            {
                if (DryRun)
                {
                    _logger.LogInfo("[DRY-RUN] Otimizar pilha de rede");
                    return true;
                }

                bool isAdmin = AdminHelper.IsRunningAsAdministrator();
                _logger.LogInfo($"[NetworkStack] Iniciando otimização. Admin: {isAdmin}");

                if (!isAdmin)
                {
                    _logger.LogWarning("Execute como Administrador para otimizar a pilha de rede");
                    return false;
                }

                // [FIX:M-9] Autorizacao real, e nao so "sou admin".
                if (!CanSafelyResetNetworkStack(out string motivo))
                {
                    _logger.LogWarning(
                        $"[FIX:M-9] RESET DE PILHA DE REDE BLOQUEADO — {motivo}. " +
                        $"O reset de winsock derruba todas as conexoes ativas e exige reinicializacao. " +
                        $"Feche jogos, downloads, VPNs e sessoes remotas antes de tentar de novo.");
                    return false;
                }

                _logger.LogInfo($"[FIX:M-9] Autorizacao para reset de pilha CONCEDIDA: {motivo}");

                _logger.LogInfo("[NetworkStack] Executando ipconfig /flushdns...");
                var ok1 = RunProcess("ipconfig", "/flushdns", out var o1, out var e1);
                if (!ok1) _logger.LogWarning($"[NetworkStack] FlushDNS falhou: {e1} {o1}");

                _logger.LogInfo("[NetworkStack] Executando netsh winsock reset...");
                var ok2 = RunProcess("netsh", "winsock reset", out var o2, out var e2);
                if (ok2)
                {
                    _logger.LogInfo("[NetworkStack] Winsock redefinido com sucesso.");
                    if (!string.IsNullOrEmpty(o2)) _logger.LogInfo($"[NetworkStack] Saída Winsock: {o2.Trim()}");
                }
                else _logger.LogWarning($"[NetworkStack] Winsock reset falhou: {e2} {o2}");

                _logger.LogInfo("[NetworkStack] Executando netsh int ip reset...");
                var ok3 = RunProcess("netsh", "int ip reset", out var o3, out var e3);
                if (ok3)
                {
                    _logger.LogInfo("[NetworkStack] Interface IP redefinida com sucesso.");
                    if (!string.IsNullOrEmpty(o3)) _logger.LogInfo($"[NetworkStack] Saída IP: {o3.Trim()}");
                }
                else _logger.LogWarning($"[NetworkStack] IP reset retornou erro (comum se arquivos de log estiverem em uso): {e3} {o3}");

                var ok = ok1 && ok2 && ok3;
                
                if (ok2 && scheduleReboot)
                {
                    _logger.LogInfo("[NetworkStack] Agendando reinicialização...");
                    RunProcess("shutdown", "/r /t 5 /c \"Reinicialização para completar Winsock reset\"", out var so, out var se);
                }

                // [FIX:M-9] Relatório coerente com o valor de retorno.
                //
                // BUG ORIGINAL: o método computava `ok = ok1 && ok2 && ok3` e o
                // usava só para escolher entre "Pilha de rede otimizada" e
                // "concluída com avisos", mas devolvia `ok2` (apenas o status do
                // winsock) e justificava isso num comentário dizendo que o
                // "ip reset às vezes dá erro 1 mesmo funcionando".
                //
                // O efeito prático era que o chamador não conseguia correlacionar
                // o booleano recebido com o que o log dizia. Um retorno false
                // com winsock OK era indistinguível de uma falha real, e o
                // chamador não tinha como decidir se deveria avisar o usuário ou
                // tentar de novo. Aprovação parcial não é sucesso: ou o estado
                // pedido existe, ou não existe.
                //
                // Agora o retorno é o resultado agregado, e o log detalha item a
                // item o que deu certo.
                var partes = new List<string>
                {
                    $"flushdns={(ok1 ? "ok" : "FALHOU")}",
                    $"winsock={(ok2 ? "ok" : "FALHOU")}",
                    $"ip={(ok3 ? "ok" : "FALHOU")}"
                };
                var detalhe = string.Join(" | ", partes);

                if (ok)
                {
                    _logger.LogSuccess($"[NetworkStack] Pilha de rede redefinida — {detalhe}");
                }
                else
                {
                    _logger.LogWarning(
                        $"[FIX:M-9] Reset de pilha PARCIAL — {detalhe}. " +
                        $"O retorno agora reflete o resultado agregado (antes devolvia apenas o status do winsock, " +
                        $"o que fazia o log e o retorno divergirem).");
                }

                // Semântica corrigida: true somente se as três etapas deram certo.
                return ok;
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro ao otimizar pilha de rede", ex);
                return false;
            }
        }

        private static void SetOrDelete(RegistryKey? key, string name, int? value)
        {
            if (key == null) return;
            if (value.HasValue) key.SetValue(name, value.Value, RegistryValueKind.DWord);
            else key.DeleteValue(name, false);
        }

        private static string ExtractGuid(string s)
        {
            try
            {
                var m = System.Text.RegularExpressions.Regex.Match(s ?? string.Empty, "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
                return m.Success ? m.Value : string.Empty;
            }
            catch { return string.Empty; }
        }

        private static string GetActivePlanGuid()
        {
            try
            {
                IntPtr activeGuidPtr;
                uint res = VoltrisOptimizer.Utils.Win32.PowerNativeMethods.PowerGetActiveScheme(IntPtr.Zero, out activeGuidPtr);
                if (res == 0 && activeGuidPtr != IntPtr.Zero)
                {
                    Guid activeGuid = System.Runtime.InteropServices.Marshal.PtrToStructure<Guid>(activeGuidPtr);
                    VoltrisOptimizer.Utils.Win32.PowerNativeMethods.LocalFree(activeGuidPtr);
                    return activeGuid.ToString("D");
                }
                return string.Empty;
            }
            catch { return string.Empty; }
        }

        private static bool RunProcess(string fileName, string args, out string stdOut, out string stdErr)
        {
            stdOut = string.Empty;
            stdErr = string.Empty;
            try
            {
                using var p = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = args,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                p.Start();
                stdOut = p.StandardOutput.ReadToEnd();
                stdErr = p.StandardError.ReadToEnd();
                p.WaitForExit(15000);
                return p.ExitCode == 0;
            }
            catch { return false; }
        }

        private class ExtremeBackup
        {
            public int? KeyboardDataQueueSize { get; set; }
            public int? MouseDataQueueSize { get; set; }
            public int? Win32PrioritySeparation { get; set; }
        }

        private class PowerPlanBackup
        {
            public string? Original { get; set; }
        }

        private static class CpuSetHelper
        {
            [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
            private static extern bool GetSystemCpuSetInformation(System.IntPtr information, int length, out int returnedLength, System.IntPtr process, int flags);

            [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
            private static extern bool SetThreadSelectedCpuSets(System.IntPtr hThread, ulong[] cpuSetIds, int setCount);

            [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
            private static extern System.IntPtr OpenThread(uint desiredAccess, bool inheritHandle, int threadId);

            [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
            private static extern bool CloseHandle(System.IntPtr h);

            public static ulong[] GetPcoreIds()
            {
                try
                {
                    int ret;
                    GetSystemCpuSetInformation(System.IntPtr.Zero, 0, out ret, System.IntPtr.Zero, 0);
                    var buf = System.Runtime.InteropServices.Marshal.AllocHGlobal(ret);
                    try
                    {
                        if (!GetSystemCpuSetInformation(buf, ret, out ret, System.IntPtr.Zero, 0)) return Array.Empty<ulong>();
                        var list = new System.Collections.Generic.List<ulong>();
                        // Simplified parser: assume ids 0..N-1 all candidate
                        for (ulong i = 0; i < 64; i++) list.Add(i);
                        return list.ToArray();
                    }
                    finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(buf); }
                }
                catch { return Array.Empty<ulong>(); }
            }

            public static void AssignThreadToCpuSets(int threadId, ulong[] ids)
            {
                var h = OpenThread(0x40, false, threadId); // THREAD_SET_LIMITED_INFO
                if (h == System.IntPtr.Zero) return;
                try { SetThreadSelectedCpuSets(h, ids, ids.Length); }
                finally { CloseHandle(h); }
            }
        }

        private static class NetworkNicHelper
        {
            public static void SetInterruptModeration(bool enable)
            {
                try
                {
                    using var cls = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}", true);
                    if (cls == null) return;
                    foreach (var sub in cls.GetSubKeyNames())
                    {
                        if (!int.TryParse(sub, out _)) continue;
                        using var k = cls.OpenSubKey(sub, true);
                        if (k == null) continue;
                        try { k.SetValue("*InterruptModeration", enable ? 1 : 0, Microsoft.Win32.RegistryValueKind.DWord); } catch { }
                    }
                }
                catch { }
            }
        }

        private static class PrivilegeHelper
        {
            [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)]
            static extern bool OpenProcessToken(System.IntPtr ProcessHandle, uint DesiredAccess, out System.IntPtr TokenHandle);
            [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
            static extern bool LookupPrivilegeValue(string lpSystemName, string lpName, out long lpLuid);
            [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)]
            static extern bool AdjustTokenPrivileges(System.IntPtr TokenHandle, bool DisableAllPrivileges, ref TOKEN_PRIVILEGES NewState, int BufferLength, System.IntPtr PreviousState, System.IntPtr ReturnLength);

            struct LUID { public uint LowPart; public int HighPart; }
            struct LUID_AND_ATTRIBUTES { public LUID Luid; public uint Attributes; }
            struct TOKEN_PRIVILEGES { public uint PrivilegeCount; public LUID_AND_ATTRIBUTES Privileges; }

            public static bool EnableSeLockMemoryPrivilege()
            {
                try
                {
                    var hProc = System.Diagnostics.Process.GetCurrentProcess().Handle;
                    if (!OpenProcessToken(hProc, 0x20 | 0x8, out var hTok)) return false;
                    long luidRaw;
                    if (!LookupPrivilegeValue(null!, "SeLockMemoryPrivilege", out luidRaw)) return false;
                    var tp = new TOKEN_PRIVILEGES
                    {
                        PrivilegeCount = 1,
                        Privileges = new LUID_AND_ATTRIBUTES
                        {
                            Luid = new LUID { LowPart = (uint)luidRaw, HighPart = (int)(luidRaw >> 32) },
                            Attributes = 0x2
                        }
                    };
                    return AdjustTokenPrivileges(hTok, false, ref tp, 0, System.IntPtr.Zero, System.IntPtr.Zero);
                }
                catch { return false; }
            }
        }
    }
}
