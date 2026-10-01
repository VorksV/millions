using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Windows;
using VoltrisUninstaller.Core;

namespace VoltrisUninstaller
{
    /// <summary>
    /// Ponto de entrada da aplicação
    /// </summary>
    public class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                // Parse de argumentos CLI
                var options = ParseArguments(args);

                // Criar logger. O log é gravado em %TEMP%\VoltrisUninstaller\Logs
                // para não ser apagado junto com a pasta de instalação durante a desinstalação.
                var logPath = options.LogPath;
                if (string.IsNullOrWhiteSpace(logPath))
                {
                    var logDir = Path.Combine(Path.GetTempPath(), "VoltrisUninstaller", "Logs");
                    Directory.CreateDirectory(logDir);
                    logPath = Path.Combine(logDir, $"uninstall-{DateTime.Now:yyyyMMdd-HHmmss}.log");
                }

                var logger = new Logger(logPath);
                logger.LogInfo("=== VOLTRIS UNINSTALLER v1.0 ===");
                logger.LogInfo($"Argumentos recebidos: {string.Join(" ", args)}");

                // Verificar Privilégios de administrador
                var isAdmin = IsAdministrator();
                if (!isAdmin)
                {
                    if (TryElevate(args))
                    {
                        return 0;
                    }
                    else
                    {
                        MessageBox.Show("Este desinstalador requer privilégios de administrador.", "Erro", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return 1;
                    }
                }

                // Se estivermos rodando de dentro da pasta de instalação (que será apagada),
                // copiamos o desinstalador para %TEMP% e o reexecutamos de lá. Isso evita que
                // o processo fique com a própria pasta e as DLLs nativas do WPF bloqueadas, e
                // também o erro "Dll was not found" (DllNotFoundException) quando o runtime
                // .NET precisa carregar alguma dependência nativa depois que a pasta foi apagada.
                if (!options.Relocated && TryRelocateToTemp(args))
                {
                    return 0;
                }

                // Diretório de trabalho seguro: nunca dentro da pasta que será removida.
                try { Directory.SetCurrentDirectory(Path.GetTempPath()); } catch { }

                // Modo silent
                if (options.Silent)
                {
                    return RunSilentMode(logger, options);
                }

                // Modo interativo
                var app = new App();
                var mainWindow = new MainWindow(logger, options);
                app.Run(mainWindow);

                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro fatal: {ex.Message}", "Erro Fatal", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
        }

        private static UninstallOptions ParseArguments(string[] args)
        {
            var options = new UninstallOptions();
            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i].ToLowerInvariant();
                if (arg == "/silent" || arg == "--silent" || arg == "/s") options.Silent = true;
                else if (arg == "/keep-user-data" || arg == "--keep-user-data") options.KeepUserData = true;
                else if ((arg == "/log" || arg == "--log") && i + 1 < args.Length) options.LogPath = args[++i];
                else if (arg == "/relocated" || arg == "--relocated") options.Relocated = true;
            }
            return options;
        }

        private static bool IsAdministrator()
        {
            try
            {
                var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        private static bool TryElevate(string[] args)
        {
            try
            {
                var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath)) return false;

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    UseShellExecute = true,
                    FileName = exePath,
                    Verb = "runas",
                    Arguments = string.Join(" ", args.Select(a => a.Contains(" ") ? $"\"{a}\"" : a))
                });
                return true;
            }
            catch { return false; }
        }

        private static bool TryRelocateToTemp(string[] args)
        {
            // Se o desinstalador já roda a partir de %TEMP% ou a copia/reexecução falhar,
            // retornamos false e o processo continua em seu diretório atual (comportamento antigo).
            try
            {
                var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return false;

                var exeDir = Path.GetDirectoryName(exePath) ?? string.Empty;
                if (exeDir.Length == 0) return false;

                // Só realoca quando estivermos na instalação real (dentro de Program Files).
                // Quando executado a partir da pasta de build/desenvolvimento mantemos o local.
                var pf64 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var pf32 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                bool insideProgramFiles =
                    (!string.IsNullOrEmpty(pf64) && exeDir.StartsWith(pf64, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(pf32) && exeDir.StartsWith(pf32, StringComparison.OrdinalIgnoreCase));
                if (!insideProgramFiles) return false;

                var tempRoot = Path.Combine(Path.GetTempPath(), "VoltrisUninstaller");
                Directory.CreateDirectory(tempRoot);
                var tempDir = Path.Combine(tempRoot, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                var tempExe = Path.Combine(tempDir, Path.GetFileName(exePath));

                File.Copy(exePath, tempExe, true);

                var argsWithFlag = new List<string>(args) { "/relocated" };
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    UseShellExecute = true,
                    FileName = tempExe,
                    WorkingDirectory = tempDir,
                    Arguments = string.Join(" ", argsWithFlag.Select(a => a.Contains(" ") ? $"\"{a}\"" : a))
                });
                return true;
            }
            catch { return false; }
        }

        private static int RunSilentMode(ILogger logger, UninstallOptions options)
        {
            try
            {
                var uninstaller = new Uninstaller(logger, options);
                var task = uninstaller.ExecuteAsync();
                task.Wait();
                return task.Result.Success ? 0 : 1;
            }
            catch { return 1; }
        }
    }
}
