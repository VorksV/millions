using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services.Gamer.Data;
using VoltrisOptimizer.Services.Gamer.Helpers;
using VoltrisOptimizer.Services.Gamer.Interfaces;
using VoltrisOptimizer.Utils;
using GamerModels = VoltrisOptimizer.Services.Gamer.Models;

namespace VoltrisOptimizer.Services.Gamer.Implementation
{
    /// <summary>
    /// Implementação do detector de jogos
    /// </summary>
    public class GameDetectorService : IGameDetector
    {
        private readonly ILoggingService _logger;
        private readonly IGameLibraryService _library;
        private readonly GameConfidenceEvaluator _evaluator;
        private CancellationTokenSource? _monitorCts;
        private Task? _monitorTask;
        private readonly object _monitorLock = new();
        private GamerModels.DetectedGame? _lastRunningGame;
        
        // Watcher de Janela em Foreground (0% CPU, passivo)
        private ForegroundWindowDetector? _foregroundDetector;
        private readonly SemaphoreSlim _wmiLock = new(1, 1);

        private static readonly HashSet<string> KnownGames = Gamer.Data.GameDatabase.KnownGames;
        private static readonly HashSet<string> SystemProcesses = Gamer.Data.GameDatabase.SystemProcesses;

        public event EventHandler<GamerModels.DetectedGame>? GameStarted;
        public event EventHandler<GamerModels.DetectedGame>? GameStopped;
        public bool IsMonitoring { get; private set; }

        public event EventHandler<GameDetectionProgress>? ProgressChanged;
        protected virtual void OnProgressChanged(GameDetectionProgress e)
        {
            ProgressChanged?.Invoke(this, e);
        }

        public bool HasActiveRunningGameSession => _lastRunningGame != null;

        /// <summary>
        /// Retorna o jogo principal atualmente em execução, se houver
        /// </summary>
        public GamerModels.DetectedGame? CurrentRunningGame => _lastRunningGame ?? _runningGamesCache?.FirstOrDefault();

        /// <summary>
        /// Retorna lista de jogos atualmente em execução
        /// </summary>
        private List<GamerModels.DetectedGame>? _runningGamesCache;
        private DateTime _runningGamesCacheTime = DateTime.MinValue;
        private static readonly TimeSpan RunningGamesCacheTtl = TimeSpan.FromSeconds(5);

        public List<GamerModels.DetectedGame> GetRunningGames()
        {
            if (_runningGamesCache != null && (DateTime.Now - _runningGamesCacheTime) < RunningGamesCacheTtl)
                return _runningGamesCache;

            if (_lastRunningGame != null)
            {
                _runningGamesCache = new List<GamerModels.DetectedGame> { _lastRunningGame };
            }
            else
            {
                _runningGamesCache = DetectRunningGames();
            }
            _runningGamesCacheTime = DateTime.Now;
            return _runningGamesCache;
        }

        public void InvalidateRunningGamesCache()
        {
            _runningGamesCache = null;
        }

        /// <summary>
        /// Força uma verificação imediata de jogos em execução (bypass throttle de 30s)
        /// Útil quando a licença é ativada e precisa detectar jogos já rodando
        /// </summary>
        public async Task ForceScanRunningGamesAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInfo("[GameDetector] Forçando scan imediato de jogos em execução (license changed)");
            
            // Resetar o throttle para forçar scan imediato
            _lastFullScanTime = DateTime.MinValue;
            _runningGamesCache = null;
            
            // Executar scan leve imediatamente
            await ScanRunningGamesLightAsync(cancellationToken);
            
            _logger.LogInfo($"[GameDetector] Scan forçado concluído - {_runningGamesCache?.Count ?? 0} jogo(s) detectado(s)");
        }

        public GameDetectorService(ILoggingService logger, IGameLibraryService library)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _library = library ?? throw new ArgumentNullException(nameof(library));
            _evaluator = new GameConfidenceEvaluator(logger);
            _logger.LogInfo("[GameDetector] GameDetectorService inicializado com GameConfidenceEvaluator");
            _logger.LogInfo($"[GameDetector] Banco de assinaturas: {GameSignatureBank.KnownSignatures.Count} jogos conhecidos");
            _logger.LogInfo($"[GameDetector] Regras de exclusão: {GameSignatureBank.ExclusionRules.Length} categorias");
        }

        public async Task<IReadOnlyList<GamerModels.DetectedGame>> DetectInstalledGamesAsync(CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                var games = new List<GamerModels.DetectedGame>();

                try
                {
                    _logger.LogInfo("[GameDetector] Iniciando detecção de jogos...");
                    OnProgressChanged(new GameDetectionProgress { Status = "Iniciando detecção...", PercentComplete = 0, GamesFound = 0 });

                    var searchPaths = GetGameSearchPaths().ToList();
                    int totalPaths = searchPaths.Count;
                    int pathsProcessed = 0;

                    foreach (var path in searchPaths)
                    {
                        if (!Directory.Exists(path)) continue;

                        cancellationToken.ThrowIfCancellationRequested();

                        pathsProcessed++;
                        OnProgressChanged(new GameDetectionProgress
                        {
                            Status = $"Escaneando: {Path.GetFileName(path)}...",
                            PercentComplete = (int)((double)pathsProcessed / totalPaths * 100),
                            GamesFound = games.Count
                        });

                        try
                        {
                            // 1. Detecção Inteligente via AppManifest (Steam)
                            if (path.EndsWith("common", StringComparison.OrdinalIgnoreCase))
                            {
                                var steamAppsPath = Path.GetDirectoryName(path);
                                if (!string.IsNullOrEmpty(steamAppsPath) && Directory.Exists(steamAppsPath))
                                {
                                    var steamGames = DetectSteamGamesFromManifests(steamAppsPath);
                                    foreach (var sg in steamGames)
                                    {
                                        if (!games.Any(existing => existing.ExecutablePath.Equals(sg.ExecutablePath, StringComparison.OrdinalIgnoreCase)))
                                            games.Add(sg);
                                    }
                                }
                            }

                            // 2. Scan de Diretório (Geral) - Profundidade aumentada para 8
                            var detected = ScanDirectory(path, 8);
                            games.AddRange(detected);
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"[GameDetector] Erro ao escanear {path}: {ex.Message}");
                        }
                    }

                    // Validar executáveis
                    OnProgressChanged(new GameDetectionProgress { Status = "Validando executáveis...", PercentComplete = 80, GamesFound = games.Count });

                    // Detectar jogos em execução
                    OnProgressChanged(new GameDetectionProgress { Status = "Detectando jogos em execução...", PercentComplete = 85, GamesFound = games.Count });
                    var runningGames = DetectRunningGames();
                    foreach (var game in runningGames)
                    {
                        if (!games.Any(g => g.ExecutablePath.Equals(game.ExecutablePath, StringComparison.OrdinalIgnoreCase)))
                        {
                            games.Add(game);
                        }
                    }

                    // Filtrar e remover duplicatas
                    OnProgressChanged(new GameDetectionProgress { Status = "Ignorando componentes auxiliares...", PercentComplete = 90, GamesFound = games.Count });
                    games = games
                        .Where(g => IsValidGame(g))
                        .GroupBy(g => g.ExecutablePath.ToLowerInvariant())
                        .Select(g => g.First())
                        .ToList();

                    // Atualizar biblioteca
                    OnProgressChanged(new GameDetectionProgress { Status = "Atualizando biblioteca...", PercentComplete = 95, GamesFound = games.Count });

                    _logger.LogSuccess($"[GameDetector] Detectados {games.Count} jogos (de {games.Count + games.Count} candidatos)");
                    _logger.LogInfo($"[GameDetector] Resumo: {games.Count(g => g.ConfidenceScore >= 80)} alta confiança, {games.Count(g => g.ConfidenceScore >= 35 && g.ConfidenceScore < 80)} média confiança");
                    OnProgressChanged(new GameDetectionProgress { Status = "Detecção concluída!", PercentComplete = 100, GamesFound = games.Count });
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInfo("[GameDetector] Detecção cancelada");
                    OnProgressChanged(new GameDetectionProgress { Status = "Detecção cancelada.", PercentComplete = 0, GamesFound = games.Count });
                }
                catch (Exception ex)
                {
                    _logger.LogError("[GameDetector] Erro na detecção de jogos", ex);
                    OnProgressChanged(new GameDetectionProgress { Status = "Erro na detecção.", PercentComplete = 0, GamesFound = games.Count });
                }

                return games;
            }, cancellationToken);
        }

        private IEnumerable<string> GetGameSearchPaths()
        {
            var paths = new List<string>();
            
            // Adicionar diretórios de jogo padrão para cada drive fixo
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
            {
                var driveRoot = drive.RootDirectory.FullName;

                // Steam
                paths.Add(Path.Combine(driveRoot, "Program Files", "Steam"));
                paths.Add(Path.Combine(driveRoot, "Program Files (x86)", "Steam"));
                paths.Add(Path.Combine(driveRoot, "Steam"));
                paths.Add(Path.Combine(driveRoot, "SteamLibrary"));

                // Epic
                paths.Add(Path.Combine(driveRoot, "Program Files", "Epic Games"));
                paths.Add(Path.Combine(driveRoot, "Program Files (x86)", "Epic Games"));
                paths.Add(Path.Combine(driveRoot, "Epic Games"));
                paths.Add(Path.Combine(driveRoot, "EpicGames"));

                // GOG
                paths.Add(Path.Combine(driveRoot, "Program Files", "GOG Galaxy", "Games"));
                paths.Add(Path.Combine(driveRoot, "Program Files (x86)", "GOG Galaxy", "Games"));
                paths.Add(Path.Combine(driveRoot, "GOG Galaxy", "Games"));
                paths.Add(Path.Combine(driveRoot, "GOG Games"));

                // Riot
                paths.Add(Path.Combine(driveRoot, "Riot Games"));

                // Ubisoft
                paths.Add(Path.Combine(driveRoot, "Program Files", "Ubisoft", "Ubisoft Game Launcher", "games"));
                paths.Add(Path.Combine(driveRoot, "Program Files (x86)", "Ubisoft", "Ubisoft Game Launcher", "games"));
                paths.Add(Path.Combine(driveRoot, "Ubisoft Games"));

                // EA / Origin
                paths.Add(Path.Combine(driveRoot, "Program Files", "EA Games"));
                paths.Add(Path.Combine(driveRoot, "Program Files (x86)", "Origin Games"));
                paths.Add(Path.Combine(driveRoot, "EA Games"));

                // Outros diretórios comuns de jogos
                paths.Add(Path.Combine(driveRoot, "Games"));
                paths.Add(Path.Combine(driveRoot, "Jogos"));
                paths.Add(Path.Combine(driveRoot, "GameLibrary"));

                // SCAN DINÂMICO PARA MMORPGs ANTIGOS E SERVIDORES ALTERNATIVOS
                try
                {
                    var rootDirs = Directory.GetDirectories(driveRoot);
                    foreach (var dir in rootDirs)
                    {
                        var folderName = Path.GetFileName(dir).ToLowerInvariant();
                        if (folderName.Contains("mu ") || folderName.StartsWith("mu") || folderName.Contains("rave") || folderName.Contains("online") || folderName.Contains("server") || folderName.Contains("game") || folderName.Contains("jogo") || folderName.Contains("client"))
                        {
                            // Evitar pastas de sistema muito genéricas
                            if (!folderName.Contains("windows") && !folderName.Contains("program") && !folderName.Contains("users") && !folderName.Contains("perflogs"))
                            {
                                paths.Add(dir);
                            }
                        }
                    }
                }
                catch { }
            }
            
            // Diretórios do Usuário (Desktop e Documentos) - Onde muitos instalam jogos alternativos
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (Directory.Exists(desktop)) paths.Add(desktop);

            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (Directory.Exists(docs)) paths.Add(docs);
            
            // Xbox/Microsoft Store (caminho fixo no AppData)
            var xboxPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
            if (Directory.Exists(xboxPath)) paths.Add(xboxPath);

            // Adicionar também o caminho da biblioteca Steam via VDF (é mais preciso)
            paths.AddRange(GetSteamLibraryPaths());
            
            return paths.Where(Directory.Exists).Distinct().ToList();
        }

        /// <summary>
        /// Detecta todas as bibliotecas Steam usando libraryfolders.vdf
        /// </summary>
        private List<string> GetSteamLibraryPaths()
        {
            var paths = new List<string>();
            var commonPaths = new[]
            {
                @"C:\Program Files (x86)\Steam",
                @"C:\Program Files\Steam",
                @"D:\Steam",
                @"E:\Steam"
            };

            // Tentar localizar a instalação principal do Steam
            string? steamPath = null;
            
            // 1. Tentar via registro
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                if (key?.GetValue("SteamPath") is string regPath)
                {
                    steamPath = regPath.Replace("/", "\\");
                }
            }
            catch { }

            // 2. Tentar caminhos comuns se registro falhar
            if (string.IsNullOrEmpty(steamPath))
            {
                foreach (var path in commonPaths)
                {
                    if (Directory.Exists(path))
                    {
                        steamPath = path;
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(steamPath)) return paths;

            // Adicionar biblioteca principal
            var mainLib = Path.Combine(steamPath, "steamapps", "common");
            if (Directory.Exists(mainLib)) paths.Add(mainLib);

            // Ler libraryfolders.vdf para bibliotecas adicionais
            try
            {
                var vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdfPath))
                {
                    var content = File.ReadAllText(vdfPath);
                    // Parsing simples de VDF (busca por "path" "...")
                    var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        if (line.Trim().StartsWith("\"path\""))
                        {
                            var parts = line.Split(new[] { '"' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 4)
                            {
                                var libraryPath = parts[3].Replace("\\\\", "\\");
                                var commonPath = Path.Combine(libraryPath, "steamapps", "common");
                                if (Directory.Exists(commonPath))
                                {
                                    paths.Add(commonPath);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[GameDetector] Erro ao ler libraryfolders.vdf: {ex.Message}");
            }

            return paths;
        }

        /// <summary>
        /// Detecta jogos Steam lendo os arquivos appmanifest_*.acf
        /// Isso é muito mais rápido e preciso que o scan de arquivos
        /// </summary>
        private List<GamerModels.DetectedGame> DetectSteamGamesFromManifests(string steamAppsPath)
        {
            var games = new List<GamerModels.DetectedGame>();
            try
            {
                var manifestFiles = Directory.GetFiles(steamAppsPath, "appmanifest_*.acf");
                foreach (var manifest in manifestFiles)
                {
                    try
                    {
                        var content = File.ReadAllText(manifest);
                        string? name = null;
                        string? installDir = null;

                        var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var line in lines)
                        {
                            var trimmed = line.Trim();
                            if (trimmed.StartsWith("\"name\""))
                            {
                                var parts = trimmed.Split(new[] { '"' }, StringSplitOptions.RemoveEmptyEntries);
                                if (parts.Length >= 2) name = parts[^1];
                            }
                            else if (trimmed.StartsWith("\"installdir\""))
                            {
                                var parts = trimmed.Split(new[] { '"' }, StringSplitOptions.RemoveEmptyEntries);
                                if (parts.Length >= 2) installDir = parts[^1];
                            }
                        }

                        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(installDir))
                        {
                            var fullPath = Path.Combine(steamAppsPath, "common", installDir);
                            if (Directory.Exists(fullPath))
                            {
                                // Localizar o executável principal dentro da pasta
                                var exes = FileSystemHelper.EnumerateFilesIterative(fullPath, "*.exe", 3);
                                // Prioriza o maior exe ou que contenha o nome do jogo
                                var mainExe = exes.OrderByDescending(f => 
                                    (Path.GetFileNameWithoutExtension(f).ToLower().Contains(name.ToLower().Replace(" ", "")) ? 1000 : 0) + 
                                    new FileInfo(f).Length / 1024 / 1024
                                ).FirstOrDefault();

                                if (mainExe != null)
                                {
                                    games.Add(new GamerModels.DetectedGame
                                    {
                                        Name = name,
                                        ExecutablePath = mainExe,
                                        DetectedAt = DateTime.Now,
                                        Launcher = GamerModels.GameLauncher.Steam
                                    });
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return games;
        }

        private List<GamerModels.DetectedGame> ScanDirectory(string directory, int maxDepth = 4)
        {
            var games = new List<GamerModels.DetectedGame>();
            var dirName = Path.GetFileName(directory);

            _logger.LogInfo($"[GAME-DETECT] [ScanDir] Iniciando scan de diretório: {directory} | MaxDepth={maxDepth}");
            int evaluatedCount = 0;
            int excludedCount = 0;
            int addedCount = 0;

            foreach (var exePath in FileSystemHelper.EnumerateFilesIterative(directory, "*.exe", maxDepth))
            {
                try
                {
                    var fileName = Path.GetFileNameWithoutExtension(exePath).ToLowerInvariant();

                    var result = _evaluator.Evaluate(exePath);
                    evaluatedCount++;

                    if (result.IsExcluded)
                    {
                        excludedCount++;
                        _logger.LogInfo($"[GAME-DETECT] [ScanDir={dirName}] EXCLUÍDO: {fileName} | Motivo={result.ExclusionReason}");
                        continue;
                    }

                    if (!result.IsGame)
                    {
                        _logger.LogInfo($"[GAME-DETECT] [ScanDir={dirName}] REJEITADO: {fileName} | Score={result.TotalScore} (<35)");
                        if (result.TotalScore > 20)
                            _logger.LogInfo($"[GAME-DETECT] [ScanDir={dirName}] REJEITADO (detalhes): {fileName} | Critérios: {string.Join(" | ", result.Contributions.Where(c => c.Points > 0).Select(c => $"{c.Source}={c.Points}"))}");
                        continue;
                    }

                    var fileInfo = new FileInfo(exePath);
                    var launcher = DetectLauncher(exePath);

                    games.Add(new GamerModels.DetectedGame
                    {
                        Name = FormatGameName(Path.GetFileNameWithoutExtension(exePath)),
                        ExecutablePath = exePath,
                        DetectedAt = DateTime.Now,
                        Size = fileInfo.Length,
                        Launcher = launcher,
                        ConfidenceScore = result.TotalScore,
                        Publisher = result.Publisher,
                        Engine = result.Engine,
                        Platform = result.Platform ?? (launcher != GamerModels.GameLauncher.Unknown ? launcher.ToString() : null),
                        Category = result.Category.ToString()
                    });
                    addedCount++;

                    if (result.IsHighConfidence)
                        _logger.LogSuccess($"[GAME-DETECT] [ScanDir={dirName}] ✅ ALTA CONFIANÇA: {fileName} | Score={result.TotalScore}");
                    else
                        _logger.LogInfo($"[GAME-DETECT] [ScanDir={dirName}] ✅ MÉDIA CONFIANÇA: {fileName} | Score={result.TotalScore}");
                }
                catch (Exception ex)
                {
                    _logger.LogDebug($"[GAME-DETECT] [ScanDir={dirName}] Erro ao avaliar: {ex.Message}");
                }
            }

            _logger.LogInfo($"[GAME-DETECT] [ScanDir={dirName}] Concluído: {evaluatedCount} avaliados, {excludedCount} excluídos, {addedCount} adicionados (limite 100)");
            return games.Take(100).ToList();
        }

        private List<GamerModels.DetectedGame> DetectRunningGames()
        {
            // ⛔ DESATIVADO - Scan completo consome CPU e gera logs excessivos
            // Detecção agora feita apenas via eventos de processo (ProcessStarted/Stopped)
            return new List<GamerModels.DetectedGame>();
        }

        private static GamerModels.GameLauncher DetectLauncher(string path)
        {
            var lowerPath = path.ToLowerInvariant();

            if (lowerPath.Contains("steamapps")) return GamerModels.GameLauncher.Steam;
            if (lowerPath.Contains("epic games")) return GamerModels.GameLauncher.EpicGames;
            if (lowerPath.Contains("gog galaxy")) return GamerModels.GameLauncher.GOG;
            if (lowerPath.Contains("ubisoft") || lowerPath.Contains("uplay")) return GamerModels.GameLauncher.Ubisoft;
            if (lowerPath.Contains("origin") || lowerPath.Contains("\\ea\\")) return GamerModels.GameLauncher.EA;
            if (lowerPath.Contains("riot games")) return GamerModels.GameLauncher.Riot;
            if (lowerPath.Contains("battle.net") || lowerPath.Contains("blizzard")) return GamerModels.GameLauncher.Blizzard;
            if (lowerPath.Contains("xbox") || lowerPath.Contains("windowsapps")) return GamerModels.GameLauncher.Xbox;

            return GamerModels.GameLauncher.Unknown;
        }

        private static string FormatGameName(string rawName)
        {
            // Converter "game_name" ou "GameName" para "Game Name"
            var name = rawName.Replace("_", " ").Replace("-", " ");
            
            // Adicionar espaços antes de maiúsculas (CamelCase)
            name = System.Text.RegularExpressions.Regex.Replace(name, "([a-z])([A-Z])", "$1 $2");
            
            // Capitalizar primeira letra de cada palavra
            return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name.ToLower());
        }

        public bool IsKnownGame(string processName, string? executablePath = null)
        {
            var lowerName = processName.ToLowerInvariant();

            if (SystemProcesses.Contains(lowerName)) return false;

            if (!string.IsNullOrEmpty(executablePath) && File.Exists(executablePath))
            {
                var result = _evaluator.Evaluate(executablePath);
                _logger.LogInfo($"[GAME-DETECT] [IsKnownGame] Processo={processName} | Caminho={executablePath} | Score={result.TotalScore} | IsGame={result.IsGame} | Excluded={result.IsExcluded}");
                return result.IsGame && !result.IsExcluded;
            }

            if (KnownGames.Contains(lowerName))
            {
                _logger.LogInfo($"[GAME-DETECT] [IsKnownGame] Processo={processName} | Conhecido pelo nome | Score=assumido-alto");
                return true;
            }

            if (_library.ContainsGame(executablePath ?? processName))
            {
                _logger.LogInfo($"[GAME-DETECT] [IsKnownGame] Processo={processName} | Na biblioteca | Score=assumido-alto");
                return true;
            }

            return false;
        }

        private bool IsValidGame(GamerModels.DetectedGame game)
        {
            if (string.IsNullOrEmpty(game.ExecutablePath)) return false;
            if (string.IsNullOrEmpty(game.Name)) return false;

            if (game.ConfidenceScore >= 35) return true;

            var result = _evaluator.Evaluate(game.ExecutablePath);
            _logger.LogInfo($"[GAME-DETECT] [IsValidGame] Jogo={game.Name} | Path={game.ExecutablePath} | Score={result.TotalScore} | Válido={result.IsGame}");
            return result.IsGame && !result.IsExcluded;
        }

        // Layer 1: WMI Event Watchers


        public void StartMonitoring()
        {
            lock (_monitorLock)
            {
                if (IsMonitoring) return;
                IsMonitoring = true;

                _monitorCts = new CancellationTokenSource();
                var token = _monitorCts.Token;

                _logger.LogInfo("[GameDetector] Iniciando Poller Inteligente de Detecção (0% Idle CPU)...");

                _monitorTask = Task.Run(async () =>
                {
                    int idleTimeMinutes = 0;
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            await FullSyncRunningGamesAsync(token);
                            
                            if (_lastRunningGame != null)
                            {
                                idleTimeMinutes = 0;
                                await Task.Delay(5000, token); // Rápido enquanto joga
                            }
                            else
                            {
                                idleTimeMinutes++;
                                int delay = Math.Min(30000, 5000 + (idleTimeMinutes * 1000));
                                await Task.Delay(delay, token); // Backoff Exponencial
                            }
                        }
                        catch (OperationCanceledException) { break; }
                        catch { await Task.Delay(5000, token); }
                    }
                }, token);
            }
        }

        /// <summary>
        /// ✅ NOVO: Aguarda o processo estar pronto para análise
        /// </summary>
        private async Task<bool> WaitForProcessReady(string processName, uint pid, int timeoutMs = 3000)
        {
            // OTIMIZAÇÃO: Loop mais eficiente com menos iterações
            var maxAttempts = timeoutMs / 500; // Verificar a cada 500ms em vez de 100ms
            var attempts = 0;
            
            while (attempts < maxAttempts)
            {
                try
                {
                    var processes = pid > 0
                        // [FIX:C-3] SafeProcess evita a ArgumentException de
                        // processo encerrado entre a checagem e o uso.
                        ? (SafeProcess.TryGet((int)pid) is { } porPid
                            ? new[] { porPid }
                            : Array.Empty<Process>())
                        : Process.GetProcessesByName(processName);

                    foreach (var proc in processes)
                    {
                        try
                        {
                            if (!proc.HasExited)
                            {
                                // OTIMIZAÇÃO: Não dar Refresh() desnecessariamente
                                if (proc.Responding)
                                {
                                    try
                                    {
                                        var module = proc.MainModule;
                                        if (module?.FileName != null)
                                        {
                                            return true;
                                        }
                                    }
                                    catch
                                    {
                                        // Access negado para processos de sistema - ignorar
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // Processo pode ter terminado
                        }
                        finally
                        {
                            proc.Dispose();
                        }
                    }
                }
                catch
                {
                    // Ignorar erros durante a espera
                }
                
                attempts++;
                await Task.Delay(500); // Aumentado de 100ms para 500ms
            }
            return false;
        }

        /// <summary>
        /// ✅ NOVO: Método alternativo para obter caminho do processo via WMI
        /// </summary>
        private string GetProcessPathAlternative(int processId)
        {
            try
            {
                // Fallback para obter o caminho do processo sem WMI
                using var proc = SafeProcess.TryGet(processId);
                return proc?.MainModule?.FileName ?? "";
            }
            catch { }
            return "";
        }

        /// <summary>
        /// ✅ NOVO: Busca avançada para processo do jogo com fallback
        /// </summary>
        private async Task<Process[]> FindGameProcessRobustAsync(string processName, int maxWaitMs = 3000)
        {
            var maxAttempts = Math.Max(1, maxWaitMs / 1000);
            var attempts = 0;
            
            while (attempts < maxAttempts)
            {
                try
                {
                    var processes = Process.GetProcessesByName(processName);
                    if (processes.Length > 0)
                    {
                        return processes;
                    }
                }
                catch { }
                
                attempts++;
                await Task.Delay(1000);
            }
            
            return new Process[0];
        }

        /// <summary>
        /// ✅ PÚBLICO: Diagnóstico avançado para CS2 (chamado pelo Orchestrator)
        /// </summary>
        public void DiagnoseCs2DetectionPublic()
        {
            DiagnoseCs2Detection();
        }

        /// <summary>
        /// ✅ NOVO: Diagnóstico avançado para CS2
        /// </summary>
        private void DiagnoseCs2Detection()
        {
            _logger.LogInfo("[GameDetector] 🔍 Iniciando diagnóstico avançado para CS2...");
            
            try
            {
                // 1. Verificar se cs2.exe está nos jogos conhecidos
                bool cs2InKnownGames = KnownGames.Contains("cs2");
                _logger.LogInfo($"[GameDetector] 📋 cs2 está em KnownGames: {cs2InKnownGames}");
                
                // 2. Listar todos os processos que contenham "cs2"
                var allProcs = Process.GetProcesses();
                var cs2Processes = allProcs.Where(p => 
                    p.ProcessName.Contains("cs2", StringComparison.OrdinalIgnoreCase) ||
                    p.ProcessName.Contains("counter", StringComparison.OrdinalIgnoreCase)
                ).ToList();
                
                _logger.LogInfo($"[GameDetector] 🎮 Processos CS2 encontrados: {cs2Processes.Count}");
                foreach (var proc in cs2Processes)
                {
                    try
                    {
                        _logger.LogInfo($"[GameDetector]   - {proc.ProcessName} (PID: {proc.Id})");
                        
                        // Tentar obter o caminho
                        try
                        {
                            var path = proc.MainModule?.FileName;
                            if (!string.IsNullOrEmpty(path))
                            {
                                path = GetProcessPathAlternative(proc.Id);
                            }
                            _logger.LogInfo($"[GameDetector]     Caminho: {path ?? "N/A"}");
                        }
                        catch (Exception ex) when (ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception)
                        {
                            _logger.LogDebug($"[GameDetector]     Caminho: Inacessível ({ex.Message})");
                        }
                        catch { }
                    }
                    catch { }
                    finally { proc.Dispose(); }
                }
                
                // 3. Verificar se o Steam está rodando
                var steamProcs = allProcs.Where(p => 
                    p.ProcessName.Equals("steam", StringComparison.OrdinalIgnoreCase)
                ).ToList();
                
                _logger.LogInfo($"[GameDetector] 🎮 Processos Steam encontrados: {steamProcs.Count}");
                foreach (var proc in steamProcs)
                {
                    try
                    {
                        _logger.LogInfo($"[GameDetector]   - Steam (PID: {proc.Id})");
                    }
                    catch { }
                    finally { proc.Dispose(); }
                }
                
                // 4. Descartar processos restantes
                foreach (var proc in allProcs)
                {
                    try { proc.Dispose(); }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GameDetector] Erro no diagnóstico CS2: {ex.Message}");
            }
        }

        private async Task HandleProcessStartedAsync(string processName, uint pid)
        {
            await _wmiLock.WaitAsync();
            try
            {
                string nameLower = processName.Replace(".exe", "").ToLowerInvariant();
                
                // 🛡️ OTIMIZAÇÃO: Filtro rápido de processos do sistema — sem log para reduzir ruído
                if (SystemProcesses.Contains(nameLower))
                {
                    return;
                }
                
                // Apenas logar processos que passaram pelo filtro do sistema
                _logger.LogDebug($"[GameDetector] WMI PROCESS START: '{processName}' (PID: {pid})");

                bool isKnown = KnownGames.Contains(nameLower);
                
                // ✅ LOG CRÍTICO: Se for CS2, log extremo detalhado
                if (nameLower.Equals("cs2", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInfo($"[GameDetector] 🚨 CS2 DETECTADO! Iniciando análise completa...");
                    _logger.LogInfo($"[GameDetector] 📁 Aguardando 500ms para processo estabilizar...");
                    await Task.Delay(500); // Esperar processo estabilizar
                }
                
                try
                {
                    using var proc = pid > 0
                        ? SafeProcess.TryGet((int)pid)
                        : Process.GetProcessesByName(nameLower).FirstOrDefault();
                    if (proc == null || proc.HasExited)
                    {
                        _logger.LogDebug($"[GameDetector] Processo '{nameLower}' já saiu ou não encontrado");
                        return;
                    }
                    
                    // ✅ LOG DE CAMINHO: Tentativa 1 - MainModule
                    string exePath = "";
                    try 
                    { 
                        exePath = proc.MainModule?.FileName ?? "";
                    } 
                    catch (Exception ex) 
                    { 
                        _logger.LogDebug($"[GameDetector] ❌ Falha MainModule: {ex.Message}");
                    }
                    
                    // ❌ LOG DE CAMINHO: Tentativa 2 - WMI (REMOVIDO POR CAUSAR STUTTERING)
                    if (string.IsNullOrEmpty(exePath))
                    {
                        // try
                        // {
                        //     using var searcher = new ManagementObjectSearcher($"SELECT ExecutablePath FROM Win32_Process WHERE ProcessId = {proc.Id}");
                        //     foreach (ManagementObject obj in searcher.Get())
                        //     {
                        //         exePath = obj["ExecutablePath"]?.ToString() ?? "";
                        //         break;
                        //     }
                        // }
                        // catch (Exception ex)
                        // {
                        //     _logger.LogDebug($"[GameDetector] ❌ Falha WMI: {ex.Message}");
                        // }
                    }
                    
                    // ✅ LOG DE CAMINHO: Tentativa 3 - GetProcessPathAlternative
                    if (string.IsNullOrEmpty(exePath))
                    {
                        try
                        {
                            exePath = GetProcessPathAlternative(proc.Id);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug($"[GameDetector] ❌ Falha GetProcessPathAlternative: {ex.Message}");
                        }
                    }
                    
                    _logger.LogInfo($"[GAME-DETECT] [ProcessStart] Caminho final: '{exePath}'");

                    var result = _evaluator.Evaluate(exePath);

                    if (result.IsExcluded)
                    {
                        _logger.LogInfo($"[GAME-DETECT] [ProcessStart] ⏭️ Excluído: {nameLower} | Motivo={result.ExclusionReason}");
                        return;
                    }

                    if (!result.IsGame)
                    {
                        if (result.TotalScore > 20)
                            _logger.LogInfo($"[GAME-DETECT] [ProcessStart] ⏭️ Rejeitado: {nameLower} | Score={result.TotalScore} (<45)");
                        return;
                    }

                    var detected = new GamerModels.DetectedGame
                    {
                        Name = GetGameDisplayName(nameLower, exePath),
                        ExecutablePath = string.IsNullOrEmpty(exePath) ? nameLower : exePath,
                        DetectedAt = DateTime.Now,
                        ProcessId = proc.Id,
                        Launcher = string.IsNullOrEmpty(exePath) ? GamerModels.GameLauncher.Unknown : DetectLauncher(exePath),
                        ConfidenceScore = result.TotalScore,
                        Publisher = result.Publisher,
                        Engine = result.Engine,
                        Platform = result.Platform,
                        Category = result.Category.ToString()
                    };

                    if (_lastRunningGame != null && _lastRunningGame.Name == detected.Name)
                    {
                        _logger.LogInfo($"[GAME-DETECT] [ProcessStart] ⏭️ Já em execução (secundário): {detected.Name} | Score={result.TotalScore}");
                        _lastRunningGame = detected;
                        if (pid == 0 && proc != null) proc.Dispose();
                        return;
                    }

                    _lastRunningGame = detected;

                    if (result.IsHighConfidence)
                        _logger.LogSuccess($"[GAME-DETECT] [ProcessStart] ✅ ALTA CONFIANÇA: {detected.Name} | Score={result.TotalScore} | PID={proc.Id} | Pub={result.Publisher} | Engine={result.Engine}");
                    else
                        _logger.LogSuccess($"[GAME-DETECT] [ProcessStart] ✅ MÉDIA CONFIANÇA: {detected.Name} | Score={result.TotalScore} | PID={proc.Id}");

                    _logger.LogSuccess($"[GAME-DETECT] [ProcessStart] 🚀 DISPARANDO GameStarted: {detected.Name}");
                    GameStarted?.Invoke(this, detected);
                    _logger.LogSuccess($"[GAME-DETECT] [ProcessStart] ✅ GameStarted disparado: {detected.Name}");
                    
                    if (pid == 0) proc.Dispose(); // Se pegamos por nome no Sync, descartar
                }
                catch (Exception ex)
                {
                    // Ignorar erros de processos que terminaram rapidamente (race condition normal)
                    // Processos como reg.exe, netsh.exe, powercfg.exe, fsutil.exe são de curta duração
                    if (ex.Message.Contains("is not running") || ex is System.ComponentModel.Win32Exception)
                    {
                        // Log apenas em nível de debug para não poluir os logs
                        _logger.LogDebug($"[GameDetector] Processo '{nameLower}' terminou antes da análise completa");
                        return; // Silenciosamente ignorar
                    }
                    
                    _logger.LogWarning($"[GameDetector] ⚠️ Erro ao processar '{nameLower}': {ex.Message}");
                }
            }
            finally { _wmiLock.Release(); }
        }

        private async Task HandleProcessStoppedAsync(string processName)
        {
            await _wmiLock.WaitAsync();
            try
            {
                if (_lastRunningGame == null) return;

                string stoppedName = processName.Replace(".exe", "").ToLowerInvariant();
                string activeName = Path.GetFileNameWithoutExtension(_lastRunningGame.ExecutablePath).ToLowerInvariant();

                if (stoppedName == activeName)
                {
                    // Delay para confirmar se não foi um restart/crash loop
                    await Task.Delay(1500);
                    
                    var procs = Process.GetProcessesByName(stoppedName);
                    if (procs.Length == 0)
                    {
                        _logger.LogInfo($"[GameDetector] Jogo encerrado: {_lastRunningGame.Name}");
                        GameStopped?.Invoke(this, _lastRunningGame);
                        _lastRunningGame = null;
                    }
                    else
                    {
                        foreach (var p in procs) p.Dispose();
                    }
                }
            }
            finally { _wmiLock.Release(); }
        }

        private async Task FullSyncRunningGamesAsync(CancellationToken token)
        {
            // Scan leve apenas para detectar jogos iniciados antes do app ou fora dos eventos
            // Fazer scan apenas a cada 30 segundos para reduzir CPU/logs
            
            if (_lastFullScanTime == DateTime.MinValue || (DateTime.Now - _lastFullScanTime).TotalSeconds >= 30)
            {
                await ScanRunningGamesLightAsync(token);
                _lastFullScanTime = DateTime.Now;
            }
            
            // Verificação leve se jogo ativo ainda está rodando (sempre)
            if (_lastRunningGame != null)
            {
                try
                {
                    var procs = GameProcessResolver.FindProcesses(_lastRunningGame.ExecutablePath);
                    if (procs.Length == 0)
                    {
                        _lastRunningGame = null;
                        await HandleProcessStoppedAsync("unknown.exe");
                    }
                    else
                    {
                        foreach (var p in procs) p.Dispose();
                    }
                }
                catch { }
            }
        }
        
        private DateTime _lastFullScanTime = DateTime.MinValue;
        
        private async Task ScanRunningGamesLightAsync(CancellationToken token)
        {
            // Scan SUPER leve - apenas processos que parecem jogos
            try
            {
                var allProcesses = Process.GetProcesses();
                var games = new List<GamerModels.DetectedGame>();
                
                foreach (var proc in allProcesses)
                {
                    try
                    {
                        var path = proc.MainModule?.FileName;
                        if (string.IsNullOrEmpty(path))
                        {
                            proc.Dispose();
                            continue;
                        }
                        
                        // Filtrar processos do Windows ANTES de avaliar (economia massiva)
                        var procName = proc.ProcessName.ToLowerInvariant();
                        if (IsWindowsSystemService(procName, path))
                        {
                            proc.Dispose();
                            continue;
                        }
                        
                        // Avaliar apenas se NÃO está na lista de excluídos óbvios
                        var result = _evaluator.Evaluate(path);
                        
                        if (result.IsHighConfidence || (result.IsGame && result.TotalScore >= 35))
                        {
                            // É jogo!
                            if (!games.Any(g => g.ExecutablePath.Equals(path, StringComparison.OrdinalIgnoreCase)))
                            {
                                games.Add(new GamerModels.DetectedGame
                                {
                                    Name = GetGameDisplayName(procName, path),
                                    ExecutablePath = path,
                                    ProcessName = proc.ProcessName,
                                    ProcessId = proc.Id,
                                    DetectedAt = DateTime.Now,
                                    Launcher = DetectLauncher(path),
                                    ConfidenceScore = result.TotalScore
                                });
                                
                                // Detecção e eventos consolidados após o scan completo
                                if (false && _lastRunningGame == null)
                                {
                                    _logger.LogSuccess($"[GAME-DETECT] 🎮 Jogo detectado: {GetGameDisplayName(procName, path)} (Score={result.TotalScore})");
                                    _lastRunningGame = games[0];
                                    GameStarted?.Invoke(this, games[0]);
                                }
                            }
                        }
                        
                        proc.Dispose();
                    }
                    catch
                    {
                        proc.Dispose();
                    }
                }

                // Consolidar resultados do scan no cache e sincronizar jogo ativo
                _runningGamesCache = new List<GamerModels.DetectedGame>(games);
                _runningGamesCacheTime = DateTime.Now;

                if (games.Count > 0)
                {
                    var primaryGame = games[0];
                    bool isNewGame = _lastRunningGame == null || 
                        !string.Equals(_lastRunningGame.ExecutablePath, primaryGame.ExecutablePath, StringComparison.OrdinalIgnoreCase);

                    _lastRunningGame = primaryGame;

                    if (isNewGame)
                    {
                        _logger.LogSuccess($"[GAME-DETECT] 🎮 Jogo detectado: {primaryGame.Name} (Score={primaryGame.ConfidenceScore})");
                        GameStarted?.Invoke(this, primaryGame);
                    }
                }
                else
                {
                    if (_lastRunningGame != null)
                    {
                        var stoppedGame = _lastRunningGame;
                        _lastRunningGame = null;
                        _logger.LogInfo($"[GAME-DETECT] ⏹️ Jogo encerrado (scan leve): {stoppedGame.Name}");
                        GameStopped?.Invoke(this, stoppedGame);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("[GAME-DETECT] Erro no scan leve", ex);
            }
        }
        
        private static bool IsWindowsSystemService(string processName, string path)
        {
            // Lista de processos do Windows que NUNCA são jogos (filtro ultra-rápido)
            var systemProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "svchost", "taskhostw", "services", "lsass", "csrss", "wininit", "winlogon",
                "explorer", "searchindexer", "searchprotocolhost", "searchfilterhost",
                "smartscreen", "defender", "mssecsvc", "wmiprvse", "conhost", "ctfmon",
                "runtimebroker", "shellinfrastructurehost", "applicationframehost",
                "dataexchangehost", "audiodg", "dwm", "taskmgr", "regedit", "cmd", "powershell",
                "notepad", "wordpad", "calc", "msedge", "chrome", "firefox", "opera", "brave"
            };
            
            if (systemProcesses.Contains(processName))
                return true;
                
            // Filtrar tudo em C:\Windows
            if (path.StartsWith(@"C:\Windows\", StringComparison.OrdinalIgnoreCase))
                return true;
                
            // Filtrar tudo em Program Files que não seja launcher de jogo
            if (path.StartsWith(@"C:\Program Files\", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(@"C:\Program Files (x86)\", StringComparison.OrdinalIgnoreCase))
            {
                // Exceto Steam, Epic, etc
                if (!path.Contains("Steam", StringComparison.OrdinalIgnoreCase) &&
                    !path.Contains("Epic", StringComparison.OrdinalIgnoreCase) &&
                    !path.Contains("GOG", StringComparison.OrdinalIgnoreCase) &&
                    !path.Contains("Origin", StringComparison.OrdinalIgnoreCase) &&
                    !path.Contains("Battle.net", StringComparison.OrdinalIgnoreCase) &&
                    !path.Contains("Riot", StringComparison.OrdinalIgnoreCase) &&
                    !path.Contains("Xbox", StringComparison.OrdinalIgnoreCase) &&
                    !path.Contains("Ubisoft", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            
            return false;
        }
                
                /// <summary>
                /// Verificação robusta específica para CS2 com cooldown de 5 minutos
                /// para evitar scans WMI pesados a cada 30s.
                /// </summary>
                private DateTime _lastCs2RobustCheck = DateTime.MinValue;
                private static readonly TimeSpan Cs2RobustCooldown = TimeSpan.FromMinutes(5);
                
private async Task CheckCs2RobustAsync()
        {
            // ⛔ DESATIVADO - Scan ultra-robusto do CS2 consome CPU e gera logs excessivos
            // Detecção do CS2 agora feita apenas via eventos de processo
            await Task.CompletedTask;
        }

        public void StopMonitoring()
        {
            lock (_monitorLock)
            {
                if (!IsMonitoring) return;

                try
                {
                    _monitorCts?.Cancel();
                    _monitorCts?.Dispose();
                    _monitorCts = null;
                    _monitorTask = null;

                    IsMonitoring = false;
                    _lastRunningGame = null;
                    
                    _logger.LogInfo("[GameDetector] Monitoramento Híbrido desativado.");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[GameDetector] Erro ao parar monitoramento: {ex.Message}");
                }
            }
        }

        private static bool IsNonGameProcess(string processName)
        {
            return SystemProcesses.Contains(processName.ToLowerInvariant());
        }

        /// <summary>
        /// Obtém o nome de exibição do jogo baseado no nome do processo ou caminho
        /// </summary>
        private string GetGameDisplayName(string processName, string executablePath)
        {
            // Primeiro tentar obter da biblioteca se disponível
            if (!string.IsNullOrEmpty(executablePath))
            {
                try
                {
                    var game = _library.GetAllGames()
                        .FirstOrDefault(g => g.ExecutablePath.Equals(executablePath, StringComparison.OrdinalIgnoreCase));
                    
                    if (game != null)
                        return game.Name;
                }
                catch { }
            }

            // Mapear nomes conhecidos para nomes amigáveis via GameSignatureBank
            var lowerName = processName.ToLowerInvariant();
            if (lowerName.EndsWith(".exe")) lowerName = lowerName.Substring(0, lowerName.Length - 4);

            if (VoltrisOptimizer.Services.Gamer.Data.GameSignatureBank.KnownSignatures.TryGetValue(lowerName, out var sig))
            {
                return sig.DisplayName;
            }

            return FormatGameName(processName);
        }
        
        /// <summary>
        /// ✅ NOVO: Scan híbrido ultra-robusto para CS2
        /// Usa múltiplos métodos para garantir detecção
        /// </summary>
        private async Task<Process?> FindCs2RobustAsync()
        {
            _logger?.LogDebug("[GameDetector] Iniciando busca robusta para CS2...");
            
            // Método 1: WMI (padrão) - ❌ REMOVIDO POR CAUSAR STUTTERING (WmiPrvSE.exe CPU spikes)
            // var wmiProcess = await FindCs2ViaWmiAsync();
            // if (wmiProcess != null)
            // {
            //     _logger?.LogSuccess($"[GameDetector] ✅ CS2 encontrado via WMI: PID {wmiProcess.Id}");
            //     return wmiProcess;
            // }
            
            // Método 2: Process.GetProcessesByName (direto)
            var directProcess = await FindCs2ViaDirectScanAsync();
            if (directProcess != null)
            {
                _logger?.LogSuccess($"[GameDetector] ✅ CS2 encontrado via scan direto: PID {directProcess.Id}");
                return directProcess;
            }
            
            // Método 3: Enumeração completa (fallback)
            var enumProcess = await FindCs2ViaEnumerationAsync();
            if (enumProcess != null)
            {
                _logger?.LogSuccess($"[GameDetector] ✅ CS2 encontrado via enumeração: PID {enumProcess.Id}");
                return enumProcess;
            }
            
            // Método 4: Scan de processos Steam (se CS2 é filho do Steam)
            var steamProcess = await FindCs2ViaSteamAsync();
            if (steamProcess != null)
            {
                _logger?.LogSuccess($"[GameDetector] ✅ CS2 encontrado via Steam: PID {steamProcess.Id}");
                return steamProcess;
            }
            
            _logger?.LogDebug("[GameDetector] CS2 não encontrado.");
            return null;
        }
        
        /// <summary>
        /// Método 1: Busca via WMI
        /// </summary>
        private async Task<Process?> FindCs2ViaWmiAsync()
        {
            try
            {
                _logger?.LogDebug("[GameDetector] CS2: Tentando WMI...");
                
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Process WHERE Name = 'cs2.exe'");
                foreach (ManagementObject obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    var processId = Convert.ToInt32(obj["ProcessId"]);

                    // [FIX:C-3] Este é o ponto mais frágil do arquivo: o PID vem
                    // do WMI, ou seja, de uma consulta feita há instantes. O
                    // processo pode ter encerrado entre a consulta e esta linha —
                    // e GetProcessById lançava ArgumentException, derrubando o
                    // scan inteiro de cs2.exe.
                    var process = SafeProcess.TryGet(processId);
                    if (process == null) continue; // encerrou: pular para o proximo

                    if (!process.HasExited)
                    {
                        _logger?.LogInfo($"[GameDetector] 📡 WMI encontrou cs2.exe PID: {processId}");
                        return process;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[GameDetector] ❌ Falha WMI: {ex.Message}");
            }
            
            return null;
        }
        
        /// <summary>
        /// Método 2: Scan direto por nome
        /// </summary>
        private async Task<Process?> FindCs2ViaDirectScanAsync()
        {
            try
            {
                _logger?.LogDebug("[GameDetector] CS2: Tentando scan direto...");
                
                var processes = Process.GetProcessesByName("cs2");
                foreach (var process in processes)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            _logger?.LogInfo($"[GameDetector] 🔍 Scan direto encontrou cs2.exe PID: {process.Id}");
                            return process;
                        }
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[GameDetector] ❌ Falha scan direto: {ex.Message}");
            }
            
            return null;
        }
        
        /// <summary>
        /// Método 3: Enumeração completa de todos os processos
        /// </summary>
        private async Task<Process?> FindCs2ViaEnumerationAsync()
        {
            try
            {
                _logger?.LogDebug("[GameDetector] CS2: Tentando enumeração completa...");
                
                var allProcesses = Process.GetProcesses();
                foreach (var process in allProcesses)
                {
                    try
                    {
                        if (string.Equals(process.ProcessName, "cs2", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!process.HasExited)
                            {
                                _logger?.LogInfo($"[GameDetector] 🔍 Enumeração encontrou cs2.exe PID: {process.Id}");
                                return process;
                            }
                        }
                    }
                    catch
                    {
                        // Ignorar processos que não podem ser acessados
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[GameDetector] ❌ Falha enumeração: {ex.Message}");
            }
            
            return null;
        }
        
        /// <summary>
        /// Método 4: Scan de processos Steam (processos filhos)
        /// </summary>
        private async Task<Process?> FindCs2ViaSteamAsync()
        {
            try
            {
                _logger?.LogDebug("[GameDetector] CS2: Tentando scan Steam...");
                
                // Encontrar processo Steam
                var steamProcesses = Process.GetProcessesByName("steam");
                foreach (var steamProcess in steamProcesses)
                {
                    try
                    {
                        // Enumerar processos filhos do Steam
                        var childProcesses = GetChildProcesses(steamProcess.Id);
                        foreach (var childPid in childProcesses)
                        {
                            try
                            {
                                using var childProcess = SafeProcess.TryGet(childPid);
                                if (childProcess != null && string.Equals(childProcess.ProcessName, "cs2", StringComparison.OrdinalIgnoreCase))
                                if (string.Equals(childProcess.ProcessName, "cs2", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (!childProcess.HasExited)
                                    {
                                        _logger?.LogInfo($"[GameDetector] 🎮 Steam scan encontrou cs2.exe PID: {childProcess.Id} (filho do Steam PID: {steamProcess.Id})");
                                        return childProcess;
                                    }
                                }
                            }
                            catch
                            {
                                // Ignorar processos que não podem ser acessados
                            }
                        }
                    }
                    finally
                    {
                        steamProcess.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[GameDetector] ❌ Falha scan Steam: {ex.Message}");
            }
            
            return null;
        }
        
        /// <summary>
        /// Obtém processos filhos de um processo pai
        /// </summary>
        private List<int> GetChildProcesses(int parentPid)
        {
            var childPids = new List<int>();
            
            try
            {
                // ❌ REMOVIDO: Busca WMI causa stuttering devido ao WmiPrvSE.exe. O detector direto e a enumeração nativa são suficientes e mais rápidos.
                // using var searcher = new ManagementObjectSearcher($"SELECT * FROM Win32_Process WHERE ParentProcessId = {parentPid}");
                // foreach (ManagementObject obj in searcher.Get())
                // {
                //     var processId = Convert.ToInt32(obj["ProcessId"]);
                //     childPids.Add(processId);
                // }
            }
            catch
            {
                // Ignorar falhas
            }
            
            return childPids;
        }
    }
}

