using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;
using VoltrisUpdater.Services;

namespace VoltrisUpdater
{
    public partial class MainWindow : Window
    {
        private string _targetPath;
        private string _logPath;
        private string _restartExe;

        public MainWindow()
        {
            InitializeComponent();
            _targetPath = AppDomain.CurrentDomain.BaseDirectory;
            _logPath = Path.Combine(_targetPath, "update_log.txt");
            UpdaterTitle.Text = LocalizationService.Instance.GetString("UpdaterTitle");
            Loaded += MainWindow_Loaded;
        }

        private void Log(string message)
        {
            try
            {
                File.AppendAllText(_logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
            }
            catch { }
        }

        private void UpdateUI(string statusKey, double percent, string stepKey, string stepArg = null)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = LocalizationService.Instance.GetString(statusKey);
                
                if (stepArg != null)
                {
                    StepLabel.Text = string.Format(LocalizationService.Instance.GetString(stepKey), stepArg);
                }
                else
                {
                    StepLabel.Text = LocalizationService.Instance.GetString(stepKey);
                }

                PercentLabel.Text = $"{percent:0}%";
                
                var anim = new DoubleAnimation
                {
                    To = percent * 4.2,
                    Duration = TimeSpan.FromMilliseconds(300),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                ProgressBar.BeginAnimation(FrameworkElement.WidthProperty, anim);
            });
        }

        private static bool IsPathWithinRoot(string root, string candidate)
        {
            try
            {
                var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var fullPath = Path.GetFullPath(candidate);
                return fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool ContainsReparsePoint(string path)
        {
            try
            {
                var current = Path.GetFullPath(path);
                while (!string.IsNullOrWhiteSpace(current))
                {
                    if (File.Exists(current) || Directory.Exists(current))
                    {
                        if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                            return true;
                    }

                    var parent = Path.GetDirectoryName(current);
                    if (string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                        break;
                    current = parent ?? string.Empty;
                }

                return false;
            }
            catch
            {
                return true;
            }
        }

        private static void CopyExtractedFiles(string stagingRoot, string targetRoot)
        {
            var backupRoot = Path.Combine(Path.GetTempPath(), "VoltrisUpdaterBackup", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(backupRoot);
            var replacedFiles = new List<(string OriginalPath, string BackupPath)>();

            try
            {
                foreach (var sourcePath in Directory.EnumerateFiles(stagingRoot, "*", SearchOption.AllDirectories))
                {
                    var relativePath = Path.GetRelativePath(stagingRoot, sourcePath);
                    var destinationPath = Path.GetFullPath(Path.Combine(targetRoot, relativePath));
                    if (!IsPathWithinRoot(targetRoot, destinationPath) || ContainsReparsePoint(destinationPath))
                        throw new InvalidOperationException("Destino de atualização inválido.");

                    var destinationDirectory = Path.GetDirectoryName(destinationPath);
                    if (string.IsNullOrWhiteSpace(destinationDirectory))
                        throw new InvalidOperationException("Diretório de destino inválido.");
                    Directory.CreateDirectory(destinationDirectory);

                    string? backupPath = null;
                    if (File.Exists(destinationPath))
                    {
                        backupPath = Path.Combine(backupRoot, Guid.NewGuid().ToString("N"));
                        File.Move(destinationPath, backupPath);
                        replacedFiles.Add((destinationPath, backupPath));
                    }

                    File.Copy(sourcePath, destinationPath, overwrite: false);
                }
            }
            catch
            {
                foreach (var replaced in replacedFiles.AsEnumerable().Reverse())
                {
                    try
                    {
                        if (File.Exists(replaced.OriginalPath))
                            File.Delete(replaced.OriginalPath);
                        if (File.Exists(replaced.BackupPath))
                            File.Move(replaced.BackupPath, replaced.OriginalPath);
                    }
                    catch { }
                }
                try { Directory.Delete(backupRoot, recursive: true); } catch { }
                throw;
            }

            try { Directory.Delete(backupRoot, recursive: true); } catch { }
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Log("=== INICIANDO ATUALIZACAO ===");
            try
            {
                string payloadPath = null;

                var args = Environment.GetCommandLineArgs();
                string restartArgs = "";
                
                for (int i = 0; i < args.Length; i++)
                {
                    string arg = args[i];

                    // Suporte universal para --target
                    if (arg.StartsWith("--target="))
                        _targetPath = arg.Substring(9).Trim('"');
                    else if (arg == "--target" && i + 1 < args.Length)
                        _targetPath = args[i + 1].Trim('"');

                    // Suporte universal para --exe
                    if (arg.StartsWith("--exe="))
                        _restartExe = arg.Substring(6).Trim('"');
                    else if (arg == "--exe" && i + 1 < args.Length)
                        _restartExe = args[i + 1].Trim('"');

                    // Suporte universal para --restart (usado pela versão 1.0.1.7 e anteriores)
                    if (arg == "--restart" && i + 1 < args.Length)
                    {
                        string fullRestart = args[i + 1].Trim('"');
                        int exeIndex = fullRestart.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                        
                        if (exeIndex > 0)
                        {
                            _restartExe = fullRestart.Substring(0, exeIndex + 4).Trim('"');
                            if (fullRestart.Length > exeIndex + 4)
                            {
                                restartArgs = fullRestart.Substring(exeIndex + 4).Trim();
                            }
                        }
                        else
                        {
                            _restartExe = fullRestart;
                        }
                    }

                    // Suporte universal para --restart-args (novo padrão)
                    if (arg.StartsWith("--restart-args="))
                        restartArgs = arg.Substring(15).Trim('"');
                    else if (arg == "--restart-args" && i + 1 < args.Length)
                        restartArgs = args[i + 1].Trim('"');

                    // Suporte universal para --payload
                    if (arg.StartsWith("--payload="))
                        payloadPath = arg.Substring(10).Trim('"');
                    else if (arg == "--payload" && i + 1 < args.Length)
                        payloadPath = args[i + 1].Trim('"');
                }

                var targetRoot = Path.GetFullPath(_targetPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var expectedExecutable = Path.Combine(targetRoot, "VoltrisOptimizer.exe");
                if (!File.Exists(expectedExecutable))
                    throw new InvalidOperationException("Diretório de instalação inválido.");

                if (string.IsNullOrEmpty(_restartExe))
                    _restartExe = expectedExecutable;

                var restartPath = Path.GetFullPath(_restartExe);
                if (!string.Equals(restartPath, expectedExecutable, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Executável de reinício inválido.");

                if (!string.IsNullOrWhiteSpace(payloadPath))
                {
                    var updateRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "VoltrisOptimizer", "Updates");
                    if (!File.Exists(payloadPath) || !IsPathWithinRoot(updateRoot, payloadPath))
                        throw new InvalidOperationException("Payload externo inválido.");
                }

                UpdateUI("Preparing", 10, "WaitingAppClose");
                await Task.Delay(2000);

                UpdateUI("ExtractingFiles", 40, "ReadingPackage");
                
                string? temporaryPayloadPath = null;
                var stagingRoot = Path.Combine(Path.GetTempPath(), "VoltrisUpdaterPayload", Guid.NewGuid().ToString("N"));
                try
                {
                    if (string.IsNullOrWhiteSpace(payloadPath))
                    {
                        Log("Nenhum payload externo detectado. Tentando ler recurso embutido...");
                        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("VoltrisUpdater.Payload.zip");
                        if (stream == null)
                            throw new InvalidOperationException(LocalizationService.Instance.GetString("InvalidPayload"));

                        temporaryPayloadPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
                        await using (var fileStream = new FileStream(temporaryPayloadPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, true))
                        {
                            await stream.CopyToAsync(fileStream);
                            await fileStream.FlushAsync();
                            fileStream.Flush(true);
                        }
                        payloadPath = temporaryPayloadPath;
                        Log("Payload embutido extraido para: " + payloadPath);
                    }

                    UpdateUI("Installing", 60, "ReplacingFiles");
                    Directory.CreateDirectory(stagingRoot);
                    using (var archive = ZipFile.OpenRead(payloadPath))
                    {
                        var total = archive.Entries.Count;
                        if (total == 0 || total > 100000)
                            throw new InvalidOperationException("Payload inválido.");

                        long extractedBytes = 0;
                        var current = 0;
                        foreach (var entry in archive.Entries)
                        {
                            current++;
                            var entryName = entry.FullName.Replace('/', '\\');
                            var segments = entryName.Split('\\', StringSplitOptions.RemoveEmptyEntries);
                            if (string.IsNullOrWhiteSpace(entryName) || entryName.Contains(':') || segments.Any(segment => segment == ".."))
                                throw new InvalidOperationException("Entrada de payload inválida.");

                            extractedBytes += entry.Length;
                            if (extractedBytes > 4L * 1024 * 1024 * 1024)
                                throw new InvalidOperationException("Payload excede o limite permitido.");

                            var destination = Path.GetFullPath(Path.Combine(stagingRoot, entryName));
                            if (!IsPathWithinRoot(stagingRoot, destination) || ContainsReparsePoint(destination))
                                throw new InvalidOperationException("Destino de extração inválido.");

                            if (string.IsNullOrEmpty(entry.Name))
                            {
                                Directory.CreateDirectory(destination);
                                continue;
                            }

                            var destinationDirectory = Path.GetDirectoryName(destination);
                            if (string.IsNullOrWhiteSpace(destinationDirectory))
                                throw new InvalidOperationException("Diretório de extração inválido.");
                            Directory.CreateDirectory(destinationDirectory);
                            entry.ExtractToFile(destination, overwrite: false);

                            if (current % 10 == 0)
                            {
                                UpdateUI("Installing", 60 + (current * 30.0 / total), "Copying", entry.Name);
                                await Task.Delay(10);
                            }
                        }
                    }

                    CopyExtractedFiles(stagingRoot, targetRoot);
                }
                finally
                {
                    try { if (Directory.Exists(stagingRoot)) Directory.Delete(stagingRoot, recursive: true); } catch { }
                    try { if (temporaryPayloadPath != null && File.Exists(temporaryPayloadPath)) File.Delete(temporaryPayloadPath); } catch { }
                }

                UpdateUI("Finalizing", 100, "StartingApp");
                await Task.Delay(1000);

                if (!File.Exists(restartPath))
                    throw new FileNotFoundException("Executável principal não encontrado.", restartPath);

                Process.Start(new ProcessStartInfo
                {
                    FileName = restartPath,
                    Arguments = restartArgs,
                    WorkingDirectory = targetRoot,
                    UseShellExecute = true
                });
                
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                Log("ERRO FATAL: " + ex.Message + "\n" + ex.StackTrace);
                string errorMsg = string.Format(LocalizationService.Instance.GetString("ErrorMessage"), ex.Message);
                MessageBox.Show(errorMsg, LocalizationService.Instance.GetString("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
            }
        }
    }
}
