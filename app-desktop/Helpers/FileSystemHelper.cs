using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VoltrisOptimizer.Helpers
{
    /// <summary>
    /// Helper compartilhado para operações de sistema de arquivos
    /// Extrai código duplicado de SystemCleaner e GamerOptimizerService
    /// </summary>
    public static class FileSystemHelper
    {
        /// <summary>
        /// Enumera arquivos de forma iterativa (não recursiva via stack) para evitar StackOverflow
        /// em diretórios muito profundos
        /// </summary>
        /// <param name="rootDirectory">Diretório raiz para iniciar a busca</param>
        /// <param name="searchPattern">Padrão de busca (ex: "*.exe", "*.*")</param>
        /// <param name="maxDepth">Profundidade máxima (0 = sem limite)</param>
        /// <returns>Enumerável de caminhos de arquivos</returns>
        public static IEnumerable<string> EnumerateFilesIterative(string rootDirectory, string searchPattern = "*", int maxDepth = 0)
        {
            if (string.IsNullOrEmpty(rootDirectory) || !Directory.Exists(rootDirectory))
                yield break;

            var stack = new Stack<(string path, int depth)>();
            stack.Push((rootDirectory, 0));

            while (stack.Count > 0)
            {
                var (currentDir, depth) = stack.Pop();

                // Obter arquivos no diretório atual
                string[] files;
                try
                {
                    files = Directory.GetFiles(currentDir, searchPattern, SearchOption.TopDirectoryOnly);
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }
                catch (PathTooLongException)
                {
                    continue;
                }
                catch (Exception)
                {
                    files = Array.Empty<string>();
                }

                foreach (var file in files)
                {
                    yield return file;
                }

                // Verificar profundidade máxima
                if (maxDepth > 0 && depth >= maxDepth)
                    continue;

                // Adicionar subdiretórios à pilha
                string[] subdirs;
                try
                {
                    subdirs = Directory.GetDirectories(currentDir);
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }
                catch (PathTooLongException)
                {
                    continue;
                }
                catch (Exception)
                {
                    subdirs = Array.Empty<string>();
                }

                foreach (var subdir in subdirs)
                {
                    try
                    {
                        // Ignorar pontos de reparse (symlinks, junctions)
                        var di = new DirectoryInfo(subdir);
                        if ((di.Attributes & FileAttributes.ReparsePoint) != 0)
                            continue;

                        stack.Push((subdir, depth + 1));
                    }
                    catch
                    {
                        // Ignorar diretórios que não podem ser acessados
                    }
                }
            }
        }

        /// <summary>
        /// Calcula o tamanho total de um diretório de forma otimizada com TIMEOUT
        /// </summary>
        /// <param name="directoryPath">Caminho do diretório</param>
        /// <param name="maxSeconds">Tempo máximo de execução (padrão 3s)</param>
        /// <param name="maxFiles">Limite de arquivos para processar (padrão 10000)</param>
        /// <returns>Tamanho em bytes (pode ser parcial se houver timeout)</returns>
        public static long GetDirectorySize(string directoryPath, int maxSeconds = 3, int maxFiles = 10000)
        {
            if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
                return 0;

            long totalSize = 0;
            int fileCount = 0;
            var startTime = DateTime.Now;

            try
            {
                // OTIMIZAÇÃO: Usar EnumerationOptions para evitar exceções de acesso e recursão infinita
                var options = new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = true,
                    ReturnSpecialDirectories = false,
                    AttributesToSkip = FileAttributes.System | FileAttributes.ReparsePoint,
                    MaxRecursionDepth = 10
                };

                foreach (var file in Directory.EnumerateFiles(directoryPath, "*", options))
                {
                    // Verificar TIMEOUT
                    if ((DateTime.Now - startTime).TotalSeconds > maxSeconds)
                        break;

                    // Verificar LIMITE DE ARQUIVOS
                    if (fileCount++ > maxFiles)
                        break;

                    try
                    {
                        var info = new FileInfo(file);
                        totalSize += info.Length;
                    }
                    catch { /* Ignorar arquivos que sumiram ou ficaram bloqueados */ }
                }
            }
            catch
            {
                // Erro genérico na enumeração
            }

            return totalSize;
        }

        /// <summary>
        /// Formata bytes em formato legível (KB, MB, GB)
        /// </summary>
        /// <param name="bytes">Quantidade de bytes</param>
        /// <returns>String formatada</returns>
        public static string FormatBytes(long bytes)
        {
            if (bytes < 0)
                return "0 B";

            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;

            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }

        /// <summary>
        /// Deleta arquivos de um diretório de forma segura
        /// </summary>
        /// <param name="directoryPath">Caminho do diretório</param>
        /// <param name="searchPattern">Padrão de busca</param>
        /// <param name="isSystemTemp">Se true, aplica proteção ativa para preservar sessões ativas do Chromium no %TEMP%</param>
        /// <returns>Tupla com (arquivos deletados, bytes liberados)</returns>
        public static (int filesDeleted, long bytesFreed) CleanDirectory(string directoryPath, string searchPattern = "*", bool isSystemTemp = false)
        {
            int filesDeleted = 0;
            long bytesFreed = 0;

            if (!Directory.Exists(directoryPath))
                return (0, 0);

            foreach (var file in EnumerateFilesIterative(directoryPath, searchPattern))
            {
                if (isSystemTemp)
                {
                    var lowerFile = file.ToLowerInvariant();
                    // Proteção Enterprise: Ignorar sessões ativas do Chromium no %TEMP%
                    // scoped_dir* = usado por extensões e renderização ativa
                    // CRX_INSTALL = instalação de extensões in-flight
                    // .com.google.Chrome.* = arquivos de troca de sessão
                    if (lowerFile.Contains("\\scoped_dir") ||
                        lowerFile.Contains("\\crx_install") ||
                        lowerFile.Contains("\\.com.google.chrome") ||
                        lowerFile.Contains("\\.org.chromium"))
                    {
                        continue;
                    }
                }

                try
                {
                    var fileInfo = new FileInfo(file);
                    var fileSize = fileInfo.Length;

                    File.Delete(file);

                    filesDeleted++;
                    bytesFreed += fileSize;
                }
                catch
                {
                    // Ignorar arquivos bloqueados ou protegidos
                }
            }

            return (filesDeleted, bytesFreed);
        }

        // Executáveis que NUNCA são jogos, mesmo que estejam em pastas com nome "games"
        private static readonly HashSet<string> _nonGameExecutables = new(StringComparer.OrdinalIgnoreCase)
        {
            "msedgewebview2", "msedge", "msedgecp", "msedgecrashhpad",
            "chrome", "firefox", "opera", "brave", "iexplore",
            "dotnet", "node", "node64", "electron", "nwjs",
            "javaw", "java", "python", "python3", "pythonw",
            "crashpad_handler", "crashreporter", "cef", "cefsharp",
            "cefsharp.browsersubprocess", "updater", "patcher",
            "launcher", "gamelauncher", "gamelaunch", "rebornlauncher",
            "uninstall", "uninstaller", "repair", "setup", "installer",
            "msiexec", "dxsetup", "vcredist", "vcredist_x64", "vcredist_x86"
        };

        /// <summary>
        /// Verifica se um caminho é um diretório de jogos conhecido.
        /// Padrões genéricos como \games\ só são aceitos se o executável não for
        /// um runtime, browser ou helper conhecido.
        /// </summary>
        public static bool IsGameDirectory(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            var lowerPath = path.ToLowerInvariant();

            // Verificar primeiro se o próprio executável é um não-jogo conhecido
            var exeName = Path.GetFileNameWithoutExtension(lowerPath);
            if (_nonGameExecutables.Contains(exeName))
                return false;

            // Padrões específicos de launchers — alta confiança, aceitar sempre
            var highConfidencePatterns = new[]
            {
                "steamapps\\common",
                "steamapps/common",
                "\\epic games\\",
                "/epic games/",
                "\\gog galaxy\\games\\",
                "/gog galaxy/games/",
                "\\riot games\\",
                "/riot games/",
                "\\ubisoft\\",
                "/ubisoft/",
                "\\origin games\\",
                "/origin games/",
                "\\ea games\\",
                "/ea games/",
                "\\xbox games\\",
                "/xbox games/",
                "\\battlenet\\",
                "/battlenet/"};

            if (highConfidencePatterns.Any(p => lowerPath.Contains(p)))
                return true;

            // Padrões genéricos — só aceitar se o executável não for um runtime/helper
            // (já verificado acima via _nonGameExecutables)
            var genericPatterns = new[]
            {
                "\\games\\",
                "\\jogos\\",
                "\\game\\",
                "\\jogo\\",
                "/games/",
                "/jogos/",
                "/game/",
                "/jogo/"
            };

            return genericPatterns.Any(p => lowerPath.Contains(p));
        }

        /// <summary>
        /// Verifica se o nome do executável (sem .exe) é um runtime, browser ou helper
        /// que nunca deve ser tratado como jogo, independente do caminho.
        /// </summary>
        public static bool IsKnownNonGameExecutable(string exeNameWithoutExtension)
        {
            return _nonGameExecutables.Contains(exeNameWithoutExtension);
        }
        public static bool IsNonGameProcess(string processName)
        {
            if (string.IsNullOrEmpty(processName))
                return true;

            var lowerName = processName.ToLowerInvariant();

            var nonGameProcesses = new[]
            {
                "trae", "code", "qoder", "voltrisoptimizer", "devenv",
                "winpty-agent", "openconsole", "rg", "fd", "vsce-sign",
                "code-tunnel", "explorer", "svchost", "csrss", "dwm",
                "taskmgr", "cmd", "powershell", "conhost", "mmc",
                "regedit", "notepad", "chrome", "firefox", "edge",
                "msedge", "opera", "brave", "vivaldi",
                "steam", "steamwebhelper", "steamservice", "gameoverlayui", "steamsysinfo",
                "epicwebhelper", "epicgamesupdater", "epicgameslauncher",
                "dxsetup", "vcredist", "vcredist_x64", "vcredist_x86"
            };

            return nonGameProcesses.Contains(lowerName);
        }

        /// <summary>
        /// Obtém o caminho base do aplicativo
        /// </summary>
        /// <returns>Caminho do diretório base</returns>
        public static string GetApplicationBasePath()
        {
            return AppDomain.CurrentDomain.BaseDirectory;
        }

        /// <summary>
        /// Combina caminhos de forma segura
        /// </summary>
        /// <param name="paths">Partes do caminho</param>
        /// <returns>Caminho combinado</returns>
        public static string CombinePath(params string[] paths)
        {
            return Path.Combine(paths.Where(p => !string.IsNullOrEmpty(p)).ToArray());
        }

        /// <summary>
        /// Garante que um diretório existe, criando se necessário
        /// </summary>
        /// <param name="directoryPath">Caminho do diretório</param>
        /// <returns>True se o diretório existe ou foi criado</returns>
        public static bool EnsureDirectoryExists(string directoryPath)
        {
            try
            {
                if (!Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}

