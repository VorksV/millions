using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;
using VoltrisOptimizer.UI.Controls;

namespace VoltrisOptimizer.Services
{
    public class SystemToolsService
    {
        private readonly ILoggingService _loggingService;

        public SystemToolsService(ILoggingService loggingService)
        {
            _loggingService = loggingService;
        }

        /// <summary>
        /// Verifica se está executando como administrador
        /// </summary>
        public bool IsRunningAsAdmin()
        {
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Cria um ponto de restauração do sistema
        /// CORREÇÃO CRÍTICA: Agora valida Perfil Inteligente antes de criar
        /// </summary>
        public async Task<bool> CreateSystemRestorePointAsync(string description = "Backup Voltris Optimizer", bool silent = false)
        {
            try
            {
                // CORREÇÃO CRÍTICA: Validar Perfil Inteligente antes de criar ponto de restauração
                var currentProfile = SettingsService.Instance.Settings.IntelligentProfile;
                _loggingService.LogInfo($"[SystemTools.CreateRestorePoint] Perfil Inteligente Ativo: {currentProfile}");
                
                // Perfil Enterprise: Requer auditoria ou aprovação especial
                if (currentProfile == IntelligentProfileType.EnterpriseSecure)
                {
                    _loggingService.LogWarning($"[SystemTools.CreateRestorePoint] Perfil {currentProfile} requer auditoria para criar ponto de restauração");
                    
                    var result = ModernMessageBox.Show(
                        LocalizationService.Instance.GetString("EnterpriseSecureRestorePointPrompt"),
                        LocalizationService.Instance.GetString("EnterpriseApprovalRequired"),
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);
                    
                    if (result != MessageBoxResult.Yes)
                    {
                        _loggingService.LogInfo($"[SystemTools.CreateRestorePoint] Criação de ponto de restauração cancelada pelo usuário (perfil {currentProfile})");
                        return false;
                    }
                    
                    _loggingService.LogInfo($"[SystemTools.CreateRestorePoint] Usuário aprovou criação de ponto de restauração (perfil {currentProfile})");
                }
                
                if (!IsRunningAsAdmin())
                {
                    if (!silent)
                    {
                        ModernMessageBox.Show(
                            LocalizationService.Instance.GetString("AdminRequiredRestorePoint"),
                            LocalizationService.Instance.GetString("InsufficientPermission"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                    return false;
                }

                _loggingService.LogInfo($"[SystemTools.CreateRestorePoint] Criando ponto de restauração do sistema... Perfil: {currentProfile}");

                // CORREÇÃO: Garantir que processo PowerShell seja terminado corretamente
                // Usar PowerShell para criar o ponto de restauração
                var psScript = $@"
try {{
    # Ensure System Restore service runs and is set to Automatic
    Set-Service -Name 'srsvc' -StartupType Automatic -ErrorAction SilentlyContinue
    Start-Service -Name 'srsvc' -ErrorAction SilentlyContinue

    # Enable System Restore via registry (required on some systems)
    $regPath = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore'
    if (-not (Test-Path $regPath)) {{ New-Item -Path $regPath -Force | Out-Null }}
    Set-ItemProperty -Path $regPath -Name 'Enable' -Value 1 -Force -ErrorAction SilentlyContinue
    # Allow frequent restore point creation (set interval to 0 minutes)
    Set-ItemProperty -Path $regPath -Name 'SystemRestorePointCreationFrequency' -Value 0 -Force -ErrorAction SilentlyContinue

    # Enable restore point creation for the system drive (C:)
    Enable-ComputerRestore -Drive 'C:' -ErrorAction SilentlyContinue

    # Create the restore point
    Checkpoint-Computer -Description '{description}' -RestorePointType 'MODIFY_SETTINGS' -ErrorAction Stop

    # Verify that the restore point was created
    $found = Get-ComputerRestorePoint -ErrorAction SilentlyContinue | Where-Object {{ $_.Description -eq '{description}' }}
    if ($found) {{
        Write-Output 'SUCCESS'
    }} else {{
        Write-Output 'NOT_FOUND'
    }}
}} catch {{
    Write-Output ""ERROR: $($_.Exception.Message)"" 
}}
";

                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psScript}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    Verb = "runas"
                };

                using var process = Process.Start(processStartInfo);
                if (process != null)
                {
                    // CORREÇÃO: Aguardar com timeout e garantir término do processo
                    var timeoutTask = Task.Delay(120000); // 2 minutos timeout
                    var exitTask = process.WaitForExitAsync();
                    var completedTask = await Task.WhenAny(exitTask, timeoutTask);
                    
                    if (completedTask == timeoutTask)
                    {
                        // Timeout - forçar término
                        try
                        {
                            process.Kill();
                            await process.WaitForExitAsync();
                        }
                        catch { }
                        _loggingService.LogWarning("[SystemTools.CreateRestorePoint] Timeout ao criar ponto de restauração");
                        return false;
                    }
                    
                    var output = await process.StandardOutput.ReadToEndAsync();

                    if (output.Contains("SUCCESS"))
                    {
                        _loggingService.LogSuccess($"[SystemTools.CreateRestorePoint] Ponto de restauração criado com sucesso! Perfil: {currentProfile}");
                        if (!silent)
                        {
                            ModernMessageBox.Show(
                                string.Format(LocalizationService.Instance.GetString("SysToolsRestorePointCreatedMsg"), description),
                                LocalizationService.Instance.GetString("SysToolsRestorePointCreatedTitle"),
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                        }
                        return true;
                    }
                    else
                    {
                        _loggingService.LogError($"[SystemTools.CreateRestorePoint] Erro ao criar ponto de restauração: {output}");
                        if (!silent)
                        {
                            ModernMessageBox.Show(
                                string.Format(LocalizationService.Instance.GetString("RestorePointCreationError"), output),
                                LocalizationService.Instance.GetString("Error"),
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                        }
                        return false;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"[SystemTools.CreateRestorePoint] Erro crítico ao criar ponto de restauração: {ex.Message}");
                if (!silent)
                {
                    ModernMessageBox.Show(
                        string.Format(LocalizationService.Instance.GetString("RestorePointCriticalError"), ex.Message),
                        LocalizationService.Instance.GetString("CriticalError"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                return false;
            }
        }

        /// <summary>
        /// Abre o seletor de pontos de restauração do Windows
        /// </summary>
        public void OpenSystemRestoreSelector()
        {
            try
            {
                if (!IsRunningAsAdmin())
                {
                    ModernMessageBox.Show(
                        LocalizationService.Instance.GetString("AdminRequiredRestoreSelector"),
                        LocalizationService.Instance.GetString("InsufficientPermission"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                _loggingService.LogInfo("Abrindo seletor de pontos de restauração...");

                // Abrir rstrui.exe (System Restore)
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "rstrui.exe",
                    UseShellExecute = true,
                    Verb = "runas"
                };

                Process.Start(processStartInfo);
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Erro ao abrir seletor de restauração: {ex.Message}");
                ModernMessageBox.Show(
                    string.Format(LocalizationService.Instance.GetString("RestoreSelectorError"), ex.Message),
                    LocalizationService.Instance.GetString("Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Repara o sistema usando DISM e SFC
        /// CORREÇÃO CRÍTICA: Agora valida Perfil Inteligente antes de reparar
        /// </summary>
        public async Task<bool> RepairSystemAsync(Action<int>? progressCallback = null)
        {
            try
            {
                // CORREÇÃO CRÍTICA: Validar Perfil Inteligente antes de reparar sistema
                var currentProfile = SettingsService.Instance.Settings.IntelligentProfile;
                _loggingService.LogInfo($"[SystemTools.RepairSystem] Perfil Inteligente Ativo: {currentProfile}");
                
                // Perfis conservadores: Não permitir reparo automático
                if (currentProfile == IntelligentProfileType.EnterpriseSecure ||
                    currentProfile == IntelligentProfileType.WorkOffice)
                {
                    _loggingService.LogWarning($"[SystemTools.RepairSystem] Perfil {currentProfile} não permite reparo automático do sistema");
                    
                    ModernMessageBox.Show(
                        string.Format(LocalizationService.Instance.GetString("ProfileBlockedRepair"), currentProfile),
                        LocalizationService.Instance.GetString("OperationBlockedByProfile"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    
                    return false;
                }
                
                if (!IsRunningAsAdmin())
                {
                    ModernMessageBox.Show(
                        LocalizationService.Instance.GetString("AdminRequiredRepair"),
                        LocalizationService.Instance.GetString("InsufficientPermission"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return false;
                }

                _loggingService.LogInfo($"[SystemTools.RepairSystem] Iniciando reparo do sistema... Perfil: {currentProfile}");
                progressCallback?.Invoke(10);

                // Executar DISM
                _loggingService.LogInfo("[SystemTools.RepairSystem] Executando DISM... (Isso pode levar alguns minutos)");
                progressCallback?.Invoke(25);

                var dismProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "dism.exe",
                        Arguments = "/Online /Cleanup-Image /RestoreHealth",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        Verb = "runas"
                    }
                };

                dismProcess.Start();
                await dismProcess.WaitForExitAsync();

                if (dismProcess.ExitCode == 0)
                {
                    _loggingService.LogSuccess("[SystemTools.RepairSystem] DISM executado com sucesso");
                }
                else
                {
                    _loggingService.LogWarning($"[SystemTools.RepairSystem] DISM retornou código: {dismProcess.ExitCode}");
                }

                progressCallback?.Invoke(50);

                // Executar SFC
                _loggingService.LogInfo("[SystemTools.RepairSystem] Executando SFC... (Isso pode levar alguns minutos)");
                progressCallback?.Invoke(75);

                var sfcProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "sfc.exe",
                        Arguments = "/scannow",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        Verb = "runas"
                    }
                };

                sfcProcess.Start();
                await sfcProcess.WaitForExitAsync();

                if (sfcProcess.ExitCode == 0)
                {
                    _loggingService.LogSuccess("[SystemTools.RepairSystem] SFC executado com sucesso");
                }
                else
                {
                    _loggingService.LogWarning($"[SystemTools.RepairSystem] SFC retornou código: {sfcProcess.ExitCode}");
                }

                progressCallback?.Invoke(100);
                _loggingService.LogSuccess($"[SystemTools.RepairSystem] Reparo do sistema concluído! Perfil: {currentProfile}");
                
                ModernMessageBox.Show(
                    LocalizationService.Instance.GetString("SystemRepairCompleted"),
                    LocalizationService.Instance.GetString("RepairCompleted"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return true;
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"[SystemTools.RepairSystem] Erro ao reparar sistema: {ex.Message}");
                ModernMessageBox.Show(
                    string.Format(LocalizationService.Instance.GetString("SystemRepairError"), ex.Message),
                    LocalizationService.Instance.GetString("Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>
        /// Abre o Gerenciador de Dispositivos para atualizar drivers
        /// </summary>
        public void OpenDeviceManager()
        {
            try
            {
                _loggingService.LogInfo("Abrindo Gerenciador de Dispositivos...");

                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "devmgmt.msc",
                    UseShellExecute = true
                };

                Process.Start(processStartInfo);
                _loggingService.LogSuccess("Gerenciador de Dispositivos aberto");
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Erro ao abrir Gerenciador de Dispositivos: {ex.Message}");
                ModernMessageBox.Show(
                    string.Format(LocalizationService.Instance.GetString("DeviceManagerError"), ex.Message),
                    LocalizationService.Instance.GetString("Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Abre o Monitor de Recursos do Windows
        /// </summary>
        public void OpenResourceMonitor()
        {
            try
            {
                _loggingService.LogInfo("Abrindo Monitor de Recursos...");

                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "resmon.exe",
                    UseShellExecute = true
                };

                Process.Start(processStartInfo);
                _loggingService.LogSuccess("Monitor de Recursos aberto");
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Erro ao abrir Monitor de Recursos: {ex.Message}");
                ModernMessageBox.Show(
                    string.Format(LocalizationService.Instance.GetString("ResourceMonitorError"), ex.Message),
                    LocalizationService.Instance.GetString("Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}

