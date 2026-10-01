using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services.Gamer.Data;
using VoltrisOptimizer.Services.Gamer.Interfaces;
using GamerModels = VoltrisOptimizer.Services.Gamer.Models;

namespace VoltrisOptimizer.Services.Gamer.Implementation
{
    public enum GameCriteriaSource
    {
        BuildMetadata,
        FilePath,
        SignatureDb,
        RuntimeBehavior,
        LauncherStore,
        FileSize,
        DirectoryContext,
        VisualResources,
        Dependencies,
        NamingPattern,
        AgeDigitalness
    }

    public class GameConfidenceResult
    {
        public int TotalScore { get; set; }
        public List<GameCriteriaContribution> Contributions { get; set; } = new();
        public bool IsGame => TotalScore >= 35;
        public bool IsHighConfidence => TotalScore >= 80;
        public string? Publisher { get; set; }
        public string? Engine { get; set; }
        public string? Platform { get; set; }
        public GameCategory Category { get; set; } = GameCategory.Unknown;
        public string? MatchedSignatureKey { get; set; }
        public bool IsExcluded { get; set; }
        public string? ExclusionReason { get; set; }

        public override string ToString() => $"[Confidence={TotalScore}/100] IsGame={IsGame} HighConf={IsHighConfidence}";
    }

    public class GameCriteriaContribution
    {
        public GameCriteriaSource Source { get; set; }
        public int Points { get; set; }
        public int MaxPoints { get; set; }
        public string Detail { get; set; } = "";
    }

    public class GameConfidenceEvaluator
    {
        private readonly ILoggingService _logger;
        private static readonly long MinGameFileSize = 5 * 1024 * 1024;
        private static readonly long MaxGameFileSize = 500L * 1024 * 1024 * 1024;

        private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".avi", ".mp4", ".mkv", ".mov", ".wmv", ".flv", ".webm",
            ".m4v", ".mpg", ".mpeg", ".3gp", ".ogv", ".ts", ".mts"
        };

        private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".wav", ".flac", ".ogg", ".wma", ".m4a", ".aac", ".opus"
        };

        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".bmp", ".dds", ".tga", ".tif", ".tiff",
            ".psd", ".svg", ".ico", ".gif", ".webp"
        };

        private static readonly HashSet<string> KnownNonGameExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".url", ".lnk", ".nfo", ".txt", ".log", ".md", ".pdf", ".doc", ".docx",
            ".xls", ".xlsx", ".ppt", ".pptx", ".rtf", ".csv", ".xml", ".json", ".yaml",
            ".html", ".htm", ".css", ".js", ".ts", ".vue", ".sln", ".csproj",
            ".sdf", ".suo", ".user", ".vs", ".vscode", ".iml", ".idea",
            ".db", ".sqlite", ".mdb", ".accdb",
            ".pdb", ".idb", ".iobj", ".ipdb",
            ".lib", ".exp", ".obj", ".o", ".a",
            ".res", ".rc", ".def", ".map"
        };

        private static readonly HashSet<string> NonGameResources = new(StringComparer.OrdinalIgnoreCase)
        {
            "unins000", "uninstall", "setup", "installer", "vcredist",
            "dxwebsetup", "directx", "dotnet", "vc_redist", "redist",
            "msi", "readme", "license", "eula", "credits",
            "crashpad", "breakpad", "mdmp", "dmp", "minidump",
            "vulkan-1", "opengl32", "d3dcompiler", "xinput1_3",
            "x3daudio1_7", "d3dx9", "d3dx10", "d3dx11"
        };

        public GameConfidenceEvaluator(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            // Sem logs no construtor para evitar flood inicial
        }

        public GameConfidenceResult Evaluate(string executablePath)
        {
            // Sem logging INFO para evitar flood - cada processo gera 20+ logs
            // Manter apenas logs ERROR/WARNING críticos

            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                return new GameConfidenceResult { TotalScore = 0, IsExcluded = true, ExclusionReason = "Arquivo não encontrado" };
            }

            try
            {
                var result = new GameConfidenceResult();
                var fileName = Path.GetFileNameWithoutExtension(executablePath).ToLowerInvariant();
                var extension = Path.GetExtension(executablePath).ToLowerInvariant();
                var fileInfo = new FileInfo(executablePath);
                var directory = Path.GetDirectoryName(executablePath) ?? "";
                var lowerPath = executablePath.ToLowerInvariant();
                var dirName = Path.GetFileName(directory).ToLowerInvariant();

                if (extension != ".exe")
                {
                    return new GameConfidenceResult { TotalScore = 0, IsExcluded = true, ExclusionReason = $"Extensão '{extension}' não é de executável" };
                }

                var exclusionCheck = CheckExclusions(executablePath, fileName, lowerPath);
                if (exclusionCheck != null)
                {
                    return new GameConfidenceResult { TotalScore = 0, IsExcluded = true, ExclusionReason = exclusionCheck };
                }

                if (NonGameResources.Contains(fileName))
                {
                    return new GameConfidenceResult { TotalScore = 0, IsExcluded = true, ExclusionReason = $"Recurso não-jogo: {fileName}" };
                }

                EvaluateSignatureDb(fileName, result);
                EvaluateFilePath(executablePath, lowerPath, result);
                EvaluateFileSize(fileInfo, result);
                EvaluateDirectoryContext(directory, lowerPath, result);
                EvaluateVisualResources(directory, result);
                EvaluateDependencies(directory, result);
                EvaluateNamingPattern(fileName, result);

                result.TotalScore = Math.Min(100, result.Contributions.Sum(c => c.Points));

                // Log apenas se for jogo confirmado (alta confiança >= 80)
                if (result.IsHighConfidence)
                {
                    _logger.LogSuccess($"[GAME-DETECT] ✅ JOGO CONFIRMADO: {fileName} | Score={result.TotalScore}/100 | Publisher={result.Publisher ?? "N/A"}");
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GAME-DETECT] Erro ao avaliar {executablePath}", ex);
                return new GameConfidenceResult { TotalScore = 0, IsExcluded = true, ExclusionReason = $"Erro: {ex.Message}" };
            }
        }

        public List<GameConfidenceResult> EvaluateBatch(IEnumerable<string> executables, int? maxParallel = null)
        {
            var list = executables.ToList();
            _logger.LogInfo($"[GAME-DETECT] [BatchEvaluate] Iniciando avaliação em lote de {list.Count} executáveis");
            _logger.LogInfo($"[GAME-DETECT] [BatchEvaluate] MaxParallel={maxParallel ?? Environment.ProcessorCount}");

            var results = new List<GameConfidenceResult>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int gamesFound = 0;

            foreach (var exe in list)
            {
                var r = Evaluate(exe);
                lock (results) results.Add(r);
                if (r.IsGame) gamesFound++;
            }

            sw.Stop();
            _logger.LogSuccess($"[GAME-DETECT] [BatchEvaluate] Lote concluído: {list.Count} avaliados, {gamesFound} jogos confirmados em {sw.ElapsedMilliseconds}ms");
            return results;
        }

        public bool IsExcluded(string executablePath)
        {
            if (string.IsNullOrWhiteSpace(executablePath)) return true;
            var fileName = Path.GetFileNameWithoutExtension(executablePath).ToLowerInvariant();
            var lowerPath = executablePath.ToLowerInvariant();
            return CheckExclusions(executablePath, fileName, lowerPath) != null;
        }

        private string? CheckExclusions(string executablePath, string fileName, string lowerPath)
        {
            foreach (var rule in GameSignatureBank.ExclusionRules.OrderByDescending(r => r.Priority))
            {
                if (rule.ProcessNames?.Contains(fileName) == true)
                {
                    _logger.LogInfo($"[GAME-DETECT] [Exclusion] Categoria={rule.Name} | Processo={fileName} | Prioridade={rule.Priority} | EXCLUÍDO");
                    return $"Excluído por regra '{rule.Name}': processo '{fileName}' é {rule.Name.ToLower()}";
                }

                if (rule.PathPatterns != null)
                {
                    foreach (var pattern in rule.PathPatterns)
                    {
                        if (lowerPath.Contains(pattern.Replace("\\", "\\").ToLowerInvariant()))
                        {
                            _logger.LogInfo($"[GAME-DETECT] [Exclusion] Categoria={rule.Name} | PathPattern={pattern} | EXCLUÍDO por caminho");
                            return $"Excluído por regra '{rule.Name}': caminho corresponde a padrão de {rule.Name.ToLower()}";
                        }
                    }
                }
            }

            if (GameSignatureBank.LauncherProcessNames.Contains(fileName))
            {
                _logger.LogInfo($"[GAME-DETECT] [Exclusion] Launcher | Processo={fileName} | EXCLUÍDO como launcher");
                return $"Excluído como launcher: '{fileName}' é um launcher conhecido";
            }

            return null;
        }

        private void EvaluateSignatureDb(string fileName, GameConfidenceResult result)
        {
            int maxPoints = 50;
            int points = 0;

            if (GameSignatureBank.KnownSignatures.TryGetValue(fileName, out var sig))
            {
                points = maxPoints;
                result.MatchedSignatureKey = fileName;
                result.Publisher = sig.Publisher;
                result.Engine = sig.Engine;
                result.Platform = sig.Platform;
                result.Category = sig.Category;

                result.Contributions.Add(new GameCriteriaContribution
                {
                    Source = GameCriteriaSource.SignatureDb,
                    Points = points,
                    MaxPoints = maxPoints,
                    Detail = $"Assinatura encontrada: {sig.DisplayName} | Publisher={sig.Publisher} | Engine={sig.Engine}"
                });
            }
            else
            {
                result.Contributions.Add(new GameCriteriaContribution
                {
                    Source = GameCriteriaSource.SignatureDb,
                    Points = 0,
                    MaxPoints = maxPoints,
                    Detail = "Nenhuma assinatura conhecida encontrada"
                });
            }

            _logger.LogInfo($"[GAME-DETECT] [SignatureDB] File={fileName} | Key={(sig != null ? "FOUND" : "NOT_FOUND")} | Points={points}/{maxPoints}" + (sig != null ? $" | Display={sig.DisplayName} | Pub={sig.Publisher} | Engine={sig.Engine}" : ""));
        }

        private void EvaluateFilePath(string executablePath, string lowerPath, GameConfidenceResult result)
        {
            int maxPoints = 15;
            int points = 0;
            var details = new List<string>();

            if (GameSignatureBank.PlatformStorePaths.Any(p => lowerPath.Contains(p)))
            {
                points += 8;
                details.Add("Em loja/distribuidora conhecida de jogos");

                foreach (var kv in GameSignatureBank.PlatformNameByFolder)
                {
                    if (lowerPath.Contains(kv.Key))
                    {
                        result.Platform = kv.Value;
                        details.Add($"Plataforma identificada: {kv.Value}");
                        break;
                    }
                }
            }

            if (lowerPath.Contains("common") && lowerPath.Contains("steamapps"))
            {
                points += 7;
                details.Add("Steam common directory");
            }

            if (FileSystemHelper.IsGameDirectory(executablePath))
            {
                points += 5;
                details.Add("Diretório classificado como game directory pelo FileSystemHelper");
            }

            var knownGameDirs = new[] { "games", "jogos", "game library", "gamecollection", "my games", "meus jogos", "biblioteca", "library" };
            if (knownGameDirs.Any(d => lowerPath.Contains(d)))
            {
                points += 3;
                details.Add("Diretório genérico de jogos");
            }

            if (lowerPath.Contains("epic") && lowerPath.Contains("content"))
            {
                points += 4;
                details.Add("Epic Games content directory");
            }

            points = Math.Min(maxPoints, points);
            result.Contributions.Add(new GameCriteriaContribution
            {
                Source = GameCriteriaSource.FilePath,
                Points = points,
                MaxPoints = maxPoints,
                Detail = string.Join("; ", details) + $" | Score={points}/{maxPoints}"
            });

            _logger.LogInfo($"[GAME-DETECT] [FilePath] Path={executablePath} | Points={points}/{maxPoints} | Details: {string.Join(", ", details)}");
        }

        private void EvaluateFileSize(FileInfo fileInfo, GameConfidenceResult result)
        {
            int maxPoints = 10;
            int points = 0;
            var sizeMb = fileInfo.Length / (1024.0 * 1024.0);
            string detail;

            if (fileInfo.Length < 1024 * 1024)
            {
                points = 0;
                detail = $"Arquivo muito pequeno: {sizeMb:F1} MB (< 1 MB)";
            }
            else if (fileInfo.Length < 5 * 1024 * 1024)
            {
                points = 2;
                detail = $"Arquivo pequeno: {sizeMb:F1} MB (1-5 MB)";
            }
            else if (fileInfo.Length < 50 * 1024 * 1024)
            {
                points = 5;
                detail = $"Tamanho médio: {sizeMb:F1} MB (5-50 MB)";
            }
            else if (fileInfo.Length < 500 * 1024 * 1024)
            {
                points = 8;
                detail = $"Grande: {sizeMb:F1} MB (50-500 MB)";
            }
            else
            {
                points = 10;
                detail = $"Muito grande: {sizeMb:F1} MB (> 500 MB)";
            }

            result.Contributions.Add(new GameCriteriaContribution
            {
                Source = GameCriteriaSource.FileSize,
                Points = points,
                MaxPoints = maxPoints,
                Detail = detail
            });

            _logger.LogInfo($"[GAME-DETECT] [FileSize] Size={sizeMb:F1}MB | Points={points}/{maxPoints}");
        }

        private void EvaluateDirectoryContext(string directory, string lowerPath, GameConfidenceResult result)
        {
            int maxPoints = 5;
            int points = 0;
            var details = new List<string>();

            try
            {
                if (!Directory.Exists(directory))
                {
                    result.Contributions.Add(new GameCriteriaContribution
                    {
                        Source = GameCriteriaSource.DirectoryContext,
                        Points = 0,
                        MaxPoints = maxPoints,
                        Detail = "Diretório não existe"
                    });
                    return;
                }

                var allFiles = Directory.EnumerateFiles(directory, "*.*", SearchOption.TopDirectoryOnly).ToList();
                var exeCount = allFiles.Count(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
                var dllCount = allFiles.Count(f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));

                if (exeCount >= 1)
                {
                    points += 1;
                    details.Add($"{exeCount} executáveis no diretório");
                }

                if (dllCount > 5)
                {
                    points += 1;
                    details.Add($"{dllCount} DLLs (jogo nativo)");
                }

                if (dllCount > 50)
                {
                    points += 2;
                    details.Add($"Muitas DLLs ({dllCount}): jogo grande/unreal/unity");
                }

                if (allFiles.Count > 100)
                {
                    points += 1;
                    details.Add($"Muitos arquivos ({allFiles.Count})");
                }

                if (details.Count == 0 && dllCount > 0)
                {
                    points = Math.Max(points, 1);
                    details.Add($"Mínimo de DLLs presentes ({dllCount})");
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[GAME-DETECT] [DirectoryContext] Erro ao analisar diretório: {ex.Message}");
            }

            points = Math.Min(maxPoints, points);
            result.Contributions.Add(new GameCriteriaContribution
            {
                Source = GameCriteriaSource.DirectoryContext,
                Points = points,
                MaxPoints = maxPoints,
                Detail = string.Join("; ", details)
            });

            _logger.LogInfo($"[GAME-DETECT] [DirectoryContext] Dir={directory} | Points={points}/{maxPoints} | {string.Join(", ", details)}");
        }

        private void EvaluateVisualResources(string directory, GameConfidenceResult result)
        {
            int maxPoints = 10;
            int points = 0;
            var details = new List<string>();

            try
            {
                if (!Directory.Exists(directory))
                {
                    result.Contributions.Add(new GameCriteriaContribution
                    {
                        Source = GameCriteriaSource.VisualResources,
                        Points = 0,
                        MaxPoints = maxPoints,
                        Detail = "Diretório não existe"
                    });
                    return;
                }

                var allFiles = Directory.EnumerateFiles(directory, "*.*", SearchOption.TopDirectoryOnly).ToList();

                var imageCount = allFiles.Count(f => ImageExtensions.Contains(Path.GetExtension(f)));
                var audioCount = allFiles.Count(f => AudioExtensions.Contains(Path.GetExtension(f)));
                var videoCount = allFiles.Count(f => VideoExtensions.Contains(Path.GetExtension(f)));

                if (imageCount > 10)
                {
                    points += 4;
                    details.Add($"Muitas imagens ({imageCount}): texturas/splash/ícones");
                }
                else if (imageCount > 0)
                {
                    points += 2;
                    details.Add($"Algumas imagens ({imageCount})");
                }

                if (audioCount > 5)
                {
                    points += 3;
                    details.Add($"Muitos áudios ({audioCount}): efeitos/música");
                }
                else if (audioCount > 0)
                {
                    points += 1;
                    details.Add($"Alguns áudios ({audioCount})");
                }

                if (videoCount > 0)
                {
                    points += 3;
                    details.Add($"Vídeos presentes ({videoCount}): cutscenes/intros");
                }

                if (imageCount == 0 && audioCount == 0 && videoCount == 0)
                {
                    points = 0;
                    details.Add("Nenhum recurso visual/áudio encontrado na raiz");
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[GAME-DETECT] [VisualResources] Erro: {ex.Message}");
            }

            points = Math.Min(maxPoints, points);
            result.Contributions.Add(new GameCriteriaContribution
            {
                Source = GameCriteriaSource.VisualResources,
                Points = points,
                MaxPoints = maxPoints,
                Detail = string.Join("; ", details)
            });

            _logger.LogInfo($"[GAME-DETECT] [VisualResources] Dir={directory} | Points={points}/{maxPoints} | {string.Join(", ", details)}");
        }

        private void EvaluateDependencies(string directory, GameConfidenceResult result)
        {
            int maxPoints = 10;
            int points = 0;
            var details = new List<string>();

            try
            {
                if (!Directory.Exists(directory))
                {
                    result.Contributions.Add(new GameCriteriaContribution
                    {
                        Source = GameCriteriaSource.Dependencies,
                        Points = 0,
                        MaxPoints = maxPoints,
                        Detail = "Diretório não existe"
                    });
                    return;
                }

                var dllFiles = Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly).ToList();
                if (dllFiles.Count == 0)
                {
                    result.Contributions.Add(new GameCriteriaContribution
                    {
                        Source = GameCriteriaSource.Dependencies,
                        Points = 0,
                        MaxPoints = maxPoints,
                        Detail = "Nenhuma DLL no diretório raiz"
                    });
                    return;
                }

                var dllNames = dllFiles.Select(Path.GetFileName).ToList();

                if (dllNames.Any(d => d != null && (d.StartsWith("Unity", StringComparison.OrdinalIgnoreCase) ||
                    d.Contains("unity", StringComparison.OrdinalIgnoreCase))))
                {
                    points += 5;
                    details.Add("Unity Engine detectada");
                    result.Engine ??= "Unity";
                }

                if (dllNames.Any(d => d != null && d.Contains("unreal", StringComparison.OrdinalIgnoreCase)))
                {
                    points += 5;
                    details.Add("Unreal Engine detectada");
                    result.Engine ??= "Unreal Engine";
                }

                if (dllNames.Any(d =>
                {
                    var lower = d?.ToLowerInvariant() ?? "";
                    return lower.StartsWith("d3d") || lower.StartsWith("d3dx") ||
                           lower.StartsWith("xinput") || lower.StartsWith("x3daudio") ||
                           lower.StartsWith("xactengine") || lower.StartsWith("d2d1") ||
                           lower.StartsWith("dwrite") || lower.Contains("directx");
                }))
                {
                    points += 3;
                    details.Add("DirectX libraries detectadas");
                }

                if (dllNames.Any(d =>
                {
                    var lower = d?.ToLowerInvariant() ?? "";
                    return lower.Contains("vulkan") || lower.Contains("vulkan-1");
                }))
                {
                    points += 3;
                    details.Add("Vulkan detectado");
                }

                if (dllNames.Any(d =>
                {
                    var lower = d?.ToLowerInvariant() ?? "";
                    return lower.Contains("openal") || lower.Contains("openvr") ||
                           lower.Contains("steam_api") || lower.Contains("steamclient") ||
                           lower.Contains("fmod") || lower.Contains("wwise") ||
                           lower.Contains("bink") || lower.Contains("easyanticheat") ||
                           lower.Contains("battleye") || lower.Contains("eossdk");
                }))
                {
                    points += 4;
                    details.Add("SDK de jogos detectada (Steamworks/FMOD/EAntiCheat)");
                }

                if (dllNames.Any(d =>
                {
                    var lower = d?.ToLowerInvariant() ?? "";
                    return lower.Contains("physx") || lower.Contains("nvapi") ||
                           lower.Contains("amd") || lower.Contains("ags") ||
                           lower.Contains("nvngx") || lower.Contains("dlss") ||
                           lower.Contains("fsr") || lower.Contains("xess");
                }))
                {
                    points += 3;
                    details.Add("Middleware gráfico (PhysX/NVIDIA/AMD)");
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[GAME-DETECT] [Dependencies] Erro: {ex.Message}");
            }

            points = Math.Min(maxPoints, points);
            result.Contributions.Add(new GameCriteriaContribution
            {
                Source = GameCriteriaSource.Dependencies,
                Points = points,
                MaxPoints = maxPoints,
                Detail = string.Join("; ", details)
            });

            _logger.LogInfo($"[GAME-DETECT] [Dependencies] Dir={directory} | Points={points}/{maxPoints} | {string.Join(", ", details)}");
        }

        private void EvaluateNamingPattern(string fileName, GameConfidenceResult result)
        {
            int maxPoints = 5;
            int points = 0;
            var details = new List<string>();

            if (fileName.Length <= 3)
            {
                points = 0;
                details.Add("Nome muito curto (<=3 chars): provável utilitário");
            }
            else if (fileName.All(char.IsLetter) || fileName.All(char.IsLower) || fileName.All(char.IsUpper))
            {
                points += 1;
                details.Add("Nome com padrão alfabético simples");
            }

            var knownGameSuffixes = new[] { "-win64-shipping", "-win32-shipping", "_x64", "_dx11", "_dx12", "_be", "_vk", "-game" };
            if (knownGameSuffixes.Any(s => fileName.EndsWith(s, StringComparison.OrdinalIgnoreCase) ||
                                           fileName.Contains(s, StringComparison.OrdinalIgnoreCase)))
            {
                points += 4;
                details.Add("Sufixo típico de jogo (win64-shipping, _x64, _dx11, etc)");
            }

            var knownGamePrefixes = new[] { "play", "launch", "game" };
            if (knownGamePrefixes.Any(p => fileName.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            {
                points += 1;
                details.Add("Prefixo típico de jogo (play/launch/game)");
            }

            if (!details.Any())
            {
                details.Add("Padrão de nome genérico, sem indicadores de jogo");
            }

            points = Math.Min(maxPoints, points);
            result.Contributions.Add(new GameCriteriaContribution
            {
                Source = GameCriteriaSource.NamingPattern,
                Points = points,
                MaxPoints = maxPoints,
                Detail = string.Join("; ", details)
            });

            _logger.LogInfo($"[GAME-DETECT] [NamingPattern] File={fileName} | Points={points}/{maxPoints} | {string.Join(", ", details)}");
        }
    }
}
