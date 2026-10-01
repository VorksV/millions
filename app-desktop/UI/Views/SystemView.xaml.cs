using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.UI.Controls;

namespace VoltrisOptimizer.UI.Views
{
    public partial class SystemView : UserControl
    {
        private readonly SystemToolsService _systemToolsService;
        private readonly ILoggingService _loggingService;

        public SystemView()
        {
            InitializeComponent();
            
            var logDirectory = LogDirectoryResolver.Resolve();
            _loggingService = App.LoggingService ?? new LoggingService(logDirectory);
            _systemToolsService = new SystemToolsService(_loggingService);
        }

        private async void CreateRestorePointButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CreateRestorePointButton.IsEnabled = false;
                CreateRestorePointButton.Content = LocalizationService.Instance.GetString("CreatingWord");

                // Telemetry
                App.TelemetryService?.TrackEvent("SYSTEM_CREATE_RESTORE", "System", "Start", forceFlush: true);

                // Iniciar operação prioritária
                GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("CreatingRestorePointProgressTitle"), isPriority: true);
                GlobalProgressService.Instance.UpdateProgress(10, LocalizationService.Instance.GetString("CreatingRestorePointMsg"));

                var result = await _systemToolsService.CreateSystemRestorePointAsync();
                
                if (result)
                {
                    _loggingService.LogSuccess(LocalizationService.Instance.GetString("RestorePointCreatedSuccess"));
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError(string.Format(LocalizationService.Instance.GetString("RestorePointCreateErrorLog"), ex.Message));
                ModernMessageBox.Show(
                    string.Format(LocalizationService.Instance.GetString("RestorePointCreateErrorMsg"), ex.Message),
                    LocalizationService.Instance.GetString("Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                // Completar operação
                GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("RestorePointCreatedCompletedShort"));
                
                CreateRestorePointButton.IsEnabled = true;
                CreateRestorePointButton.Content = LocalizationService.Instance.GetString("CreateRestorePointBtnText");
            }
        }

        private void RestoreSystemButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _systemToolsService.OpenSystemRestoreSelector();
                // Telemetry
                App.TelemetryService?.TrackEvent("SYSTEM_OPEN_RESTORE", "System", "Open", forceFlush: true);
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Erro ao abrir restauração: {ex.Message}");
                ModernMessageBox.Show(
                    string.Format(LocalizationService.Instance.GetString("OpenRestoreErrorMsg"), ex.Message),
                    LocalizationService.Instance.GetString("Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void PreparePcButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Encontrar a MainWindow para bloquear a sidebar e acessar o ContentFrame
                var mainWindow = Window.GetWindow(this) as MainWindow;
                if (mainWindow != null)
                {
                    // Bloquear navegação
                    mainWindow.DisableSidebarCompletely();
                    
                    // Criar controlador de fluxo (usando o ContentFrame da MainWindow)
                    var flowController = new VoltrisOptimizer.Core.PreparePc.PreparePcFlowController(
                        mainWindow.ContentFrame,
                        App.LoggingService,
                        onComplete: () =>
                        {
                            // Quando terminar, desbloquear e voltar para o Dashboard (ou ficar no System)
                            mainWindow.UnlockGate();
                            mainWindow.NavigateToPageFromOutside("System");
                        });
                    
                    // Iniciar o fluxo diretamente
                    flowController.StartManually();
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Erro ao iniciar Prepare PC: {ex.Message}");
            }
        }

        private async void RepairSystemButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = ModernMessageBox.Show(
                    LocalizationService.Instance.GetString("ConfirmSystemRepairMsg"),
                    LocalizationService.Instance.GetString("ConfirmSystemRepairTitle"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                    return;

                // Telemetry
                App.TelemetryService?.TrackEvent("SYSTEM_REPAIR", "System", "Start", forceFlush: true);

                // Iniciar operação prioritária
                GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("SystemRepairProgressTitle"), isPriority: true);
                GlobalProgressService.Instance.UpdateProgress(5, LocalizationService.Instance.GetString("SystemRepairStartingMsg"));

                await _systemToolsService.RepairSystemAsync((progress) =>
                {
                    GlobalProgressService.Instance.UpdateProgress(progress, string.Format(LocalizationService.Instance.GetString("SystemRepairProgressMsg"), progress));
                });
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Erro ao reparar sistema: {ex.Message}");
                ModernMessageBox.Show(
                    string.Format(LocalizationService.Instance.GetString("SystemRepairErrorMsg"), ex.Message),
                    LocalizationService.Instance.GetString("Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("SystemRepairCompletedShort"));
            }
        }

        private void UpdateDriversButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _systemToolsService.OpenDeviceManager();
                // Telemetry
                App.TelemetryService?.TrackEvent("SYSTEM_OPEN_DEVMGR", "System", "Open", forceFlush: true);
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Erro ao abrir Gerenciador de Dispositivos: {ex.Message}");
                ModernMessageBox.Show(
                    string.Format(LocalizationService.Instance.GetString("OpenDeviceManagerErrorMsg"), ex.Message),
                    LocalizationService.Instance.GetString("Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ResourceMonitorButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _systemToolsService.OpenResourceMonitor();
                // Telemetry
                App.TelemetryService?.TrackEvent("SYSTEM_OPEN_RESMON", "System", "Open", forceFlush: true);
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Erro ao abrir Monitor de Recursos: {ex.Message}");
                ModernMessageBox.Show(
                    string.Format(LocalizationService.Instance.GetString("OpenResourceMonitorErrorMsg"), ex.Message),
                    LocalizationService.Instance.GetString("Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        
        private async void DiskCleanupButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _loggingService.LogInfo("Limpeza Nativa iniciada (Substituindo cleanmgr.exe)");
                App.TelemetryService?.TrackEvent("SYSTEM_OPEN_CLEANMGR", "System", "Open");
                
                var ultraCleaner = App.Services?.GetService(typeof(VoltrisOptimizer.Services.UltraCleanerService)) as VoltrisOptimizer.Services.UltraCleanerService;
                if (ultraCleaner != null)
                {
                    await ultraCleaner.CleanWindowsDiskCleanupFullAsync();
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Erro ao abrir Limpeza de Disco: {ex.Message}");
                ModernMessageBox.Show(
                    string.Format(LocalizationService.Instance.GetString("OpenDiskCleanupErrorMsg"), ex.Message),
                    LocalizationService.Instance.GetString("Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        
        private void DefragButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "dfrgui.exe",
                    UseShellExecute = true
                });
                _loggingService.LogInfo("Desfragmentador do Windows aberto");
                // Telemetry
                App.TelemetryService?.TrackEvent("SYSTEM_OPEN_DEFRAG", "System", "Open");
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Erro ao abrir Desfragmentador: {ex.Message}");
                ModernMessageBox.Show(
                    string.Format(LocalizationService.Instance.GetString("OpenDefragErrorMsg"), ex.Message),
                    LocalizationService.Instance.GetString("Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        
        private void SystemInfoButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ms-settings:about",
                    UseShellExecute = true
                });
                _loggingService.LogInfo("Informações do Sistema abertas");
                // Telemetry
                App.TelemetryService?.TrackEvent("SYSTEM_OPEN_SYSINFO", "System", "Open");
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Erro ao abrir Informações do Sistema: {ex.Message}");
                ModernMessageBox.Show(
                    string.Format(LocalizationService.Instance.GetString("OpenSystemInfoErrorMsg"), ex.Message),
                    LocalizationService.Instance.GetString("Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        
        private void TaskManagerButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "taskmgr.exe",
                    UseShellExecute = true
                });
                _loggingService.LogInfo("Gerenciador de Tarefas aberto");
                // Telemetry
                App.TelemetryService?.TrackEvent("SYSTEM_OPEN_TASKMGR", "System", "Open");
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Erro ao abrir Gerenciador de Tarefas: {ex.Message}");
                ModernMessageBox.Show(
                    string.Format(LocalizationService.Instance.GetString("OpenTaskManagerErrorMsg"), ex.Message),
                    LocalizationService.Instance.GetString("Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void RestartButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = ModernMessageBox.Show(
                    LocalizationService.Instance.GetString("ConfirmRestartMsg"),
                    LocalizationService.Instance.GetString("ConfirmRestartTitle"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                    return;

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "shutdown",
                    Arguments = $"/r /t 10 /c \"{LocalizationService.Instance.GetString("ShutdownReasonMsg")}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                _loggingService.LogInfo("Comando de reinicialização enviado");
                App.TelemetryService?.TrackEvent("SYSTEM_RESTART", "System", "Execute");

                ModernMessageBox.Show(
                    LocalizationService.Instance.GetString("RestartScheduledMsg"),
                    LocalizationService.Instance.GetString("RestartScheduledTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Erro ao reiniciar: {ex.Message}");
                ModernMessageBox.Show(
                    string.Format(LocalizationService.Instance.GetString("RestartErrorMsg"), ex.Message),
                    LocalizationService.Instance.GetString("Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ShutdownButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = ModernMessageBox.Show(
                    LocalizationService.Instance.GetString("ConfirmShutdownMsg"),
                    LocalizationService.Instance.GetString("ConfirmShutdownTitle"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                    return;

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "shutdown",
                    Arguments = $"/s /t 10 /c \"{LocalizationService.Instance.GetString("ShutdownReasonMsgShutdown")}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                _loggingService.LogInfo("Comando de desligamento enviado");
                App.TelemetryService?.TrackEvent("SYSTEM_SHUTDOWN", "System", "Execute");

                ModernMessageBox.Show(
                    LocalizationService.Instance.GetString("ShutdownScheduledMsg"),
                    LocalizationService.Instance.GetString("ShutdownScheduledTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Erro ao desligar: {ex.Message}");
                ModernMessageBox.Show(
                    string.Format(LocalizationService.Instance.GetString("ShutdownErrorMsg"), ex.Message),
                    LocalizationService.Instance.GetString("Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}

