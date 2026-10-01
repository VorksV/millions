using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Drivers
{
    public class DriverBackupManager
    {
        private readonly string _backupRoot;

        public DriverBackupManager()
        {
            System.Diagnostics.Debug.WriteLine("[DriverBackupManager] Construtor - Entry");
            try
            {
                App.LoggingService?.LogInfo("[BackupManager] Inicializando serviço de restauração vital do SO...");
                _backupRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DriverUpdater", "Backups");
                Directory.CreateDirectory(_backupRoot);
                App.LoggingService?.LogInfo($"[BackupManager] Raiz de diretório estabilizada em: {_backupRoot}");
                System.Diagnostics.Debug.WriteLine($"[DriverBackupManager] Construtor - Backup root set to {_backupRoot}");
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[BackupManager] Falha ao injetar e criar as pastas de retenção críticas para disco.", ex);
                System.Diagnostics.Debug.WriteLine($"[DriverBackupManager] Construtor - Exception creating backup directory: {ex.Message}");
                throw;
            }
        }

        public async Task<string> BackupAllDriversAsync()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            System.Diagnostics.Debug.WriteLine("[DriverBackupManager] BackupAllDriversAsync - Entry");
            App.LoggingService?.LogInfo("[BackupManager] Solicitando expurgo e montagem total dos drivers via DISM...");
            
            try
            {
                string backupFolder = Path.Combine(_backupRoot, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                Directory.CreateDirectory(backupFolder);
                System.Diagnostics.Debug.WriteLine($"[DriverBackupManager] BackupAllDriversAsync - Backup folder created: {backupFolder}");

                string dismArgs = $"/online /export-driver /destination:\"{backupFolder}\"";
                var exitCode = await RunDismAsync(dismArgs);
                System.Diagnostics.Debug.WriteLine($"[DriverBackupManager] BackupAllDriversAsync - DISM exit code: {exitCode}");
                
                if (exitCode != 0)
                {
                    App.LoggingService?.LogError($"[BackupManager] Catástrofe sistêmica ao exportar imagem. DISM abortou com: {exitCode}");
                    System.Diagnostics.Debug.WriteLine($"[DriverBackupManager] BackupAllDriversAsync - DISM failed with code {exitCode}");
                    throw new Exception($"DISM export failed with exit code {exitCode}");
                }

                int fileCount = Directory.GetFiles(backupFolder, "*.*", SearchOption.AllDirectories).Length;
                App.LoggingService?.LogInfo($"[BackupManager] A API MS DISM confirmou a clonagem bem sucedida para: {backupFolder}");
                System.Diagnostics.Debug.WriteLine($"[DriverBackupManager] BackupAllDriversAsync - Success: {backupFolder}, files={fileCount}, duration={sw.ElapsedMilliseconds}ms");
                return backupFolder;
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[BackupManager] Aborto crítico. O ambiente foi comprometido tentando efetivar backup (DISM).", ex);
                System.Diagnostics.Debug.WriteLine($"[DriverBackupManager] BackupAllDriversAsync - Exception: {ex.Message}, duration={sw.ElapsedMilliseconds}ms");
                throw;
            }
        }

        public async Task<bool> RestoreAllDriversAsync(string backupFolder)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            System.Diagnostics.Debug.WriteLine($"[DriverBackupManager] RestoreAllDriversAsync - Entry backupFolder={backupFolder}");
            App.LoggingService?.LogWarning($"[BackupManager] ESTADO DE ROLLBACK ACIONADO! Direcionando para versão estável: {backupFolder}");

            if (!Directory.Exists(backupFolder))
            {
                App.LoggingService?.LogError($"[BackupManager] O diretório provido para o rollback sumiu ou corrompeu ({backupFolder}). Restauração terminada em falha física.");
                System.Diagnostics.Debug.WriteLine("[DriverBackupManager] RestoreAllDriversAsync - Backup folder not found, returning false");
                return false;
            }

            int infCount = Directory.GetFiles(backupFolder, "*.inf", SearchOption.AllDirectories).Length;
            System.Diagnostics.Debug.WriteLine($"[DriverBackupManager] RestoreAllDriversAsync - Found {infCount} INF files in backup");

            try
            {
                string pnpArgs = $"/add-driver \"{backupFolder}\\*.inf\" /subdirs /install";
                System.Diagnostics.Debug.WriteLine($"[DriverBackupManager] RestoreAllDriversAsync - PnPUtil args: {pnpArgs}");
                var exitCode = await RunPnPUtilAsync(pnpArgs);
                
                if (exitCode == 0)
                {
                    App.LoggingService?.LogInfo("[BackupManager] Sucesso monumental! Rollback executado na Store nativamente.");
                    System.Diagnostics.Debug.WriteLine($"[DriverBackupManager] RestoreAllDriversAsync - Rollback success, duration={sw.ElapsedMilliseconds}ms");
                    return true;
                }
                
                App.LoggingService?.LogError($"[BackupManager] Rollback via PNP bloqueou ou apresentou erros vitais. Código {exitCode}");
                System.Diagnostics.Debug.WriteLine($"[DriverBackupManager] RestoreAllDriversAsync - PnPUtil failed, code={exitCode}, duration={sw.ElapsedMilliseconds}ms");
                return false;
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[BackupManager] Exceção inusitada no PnPUtil ao atuar como Recovery Mode.", ex);
                System.Diagnostics.Debug.WriteLine($"[DriverBackupManager] RestoreAllDriversAsync - Exception: {ex.Message}, duration={sw.ElapsedMilliseconds}ms");
                return false;
            }
        }

        private async Task<int> RunDismAsync(string arguments)
        {
            using (var process = new Process())
            {
                process.StartInfo.FileName = "dism.exe";
                process.StartInfo.Arguments = arguments;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.Verb = "runas";

                App.LoggingService?.LogInfo($"[BackupManager] Lançando nova thread host para DISM: dism.exe {arguments}");
                System.Diagnostics.Debug.WriteLine($"[DriverBackupManager] RunDismAsync - Starting process: dism.exe {arguments}");
                process.Start();

                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                System.Diagnostics.Debug.WriteLine($"[DriverBackupManager] RunDismAsync - Exit code: {process.ExitCode}");

                if (!string.IsNullOrWhiteSpace(output))
                {
                    App.LoggingService?.LogInfo($"[BackupManager/DISM] {output}");
                    System.Diagnostics.Debug.WriteLine($"[DriverBackupManager] RunDismAsync - StdOut: {output.Trim()}");
                }

                if (!string.IsNullOrWhiteSpace(error)) 
                {
                    App.LoggingService?.LogError($"[BackupManager/DISM] StdError Fatal: {error}");
                    System.Diagnostics.Debug.WriteLine($"[DriverBackupManager] RunDismAsync - StdErr: {error.Trim()}");
                }

                return process.ExitCode;
            }
        }

        private async Task<int> RunPnPUtilAsync(string arguments)
        {
            using (var process = new Process())
            {
                process.StartInfo.FileName = "pnputil.exe";
                process.StartInfo.Arguments = arguments;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.Verb = "runas";

                App.LoggingService?.LogInfo($"[BackupManager] Lançando instâncias ocultas injetadas sob runas PNPUtil: pnputil.exe {arguments}");
                System.Diagnostics.Debug.WriteLine($"[DriverBackupManager] RunPnPUtilAsync - Starting process: pnputil.exe {arguments}");
                process.Start();

                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                System.Diagnostics.Debug.WriteLine($"[DriverBackupManager] RunPnPUtilAsync - Exit code: {process.ExitCode}");

                if (!string.IsNullOrWhiteSpace(output))
                {
                    App.LoggingService?.LogInfo($"[BackupManager/PNPUtil] {output}");
                    System.Diagnostics.Debug.WriteLine($"[DriverBackupManager] RunPnPUtilAsync - StdOut: {output.Trim()}");
                }

                if (!string.IsNullOrWhiteSpace(error)) 
                {
                    App.LoggingService?.LogError($"[BackupManager/PNPUtil] StdError Fatal: {error}");
                    System.Diagnostics.Debug.WriteLine($"[DriverBackupManager] RunPnPUtilAsync - StdErr: {error.Trim()}");
                }

                return process.ExitCode;
            }
        }
    }
}
