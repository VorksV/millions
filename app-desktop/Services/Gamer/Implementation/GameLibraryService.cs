using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services.Gamer.Interfaces;
using GamerModels = VoltrisOptimizer.Services.Gamer.Models;

namespace VoltrisOptimizer.Services.Gamer.Implementation
{
    public class GameLibraryService : IGameLibraryService
    {
        private readonly ILoggingService _logger;
        private readonly string _libraryPath;
        private List<GamerModels.DetectedGame> _games = new();
        private readonly object _lock = new();

        public GameLibraryService(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogEntry(nameof(GameLibraryService));

            var gamesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Games");
            Directory.CreateDirectory(gamesDir);
            _libraryPath = Path.Combine(gamesDir, "library.json");

            Load();
            _logger.LogExit(nameof(GameLibraryService));
        }

        public IReadOnlyList<GamerModels.DetectedGame> GetAllGames()
        {
            _logger.LogEntry(nameof(GetAllGames));
            lock (_lock)
            {
                _logger.LogExit(nameof(GetAllGames));
                return _games.ToList().AsReadOnly();
            }
        }

        public bool AddGame(GamerModels.DetectedGame game)
        {
            _logger.LogEntry(nameof(AddGame));
            if (game == null)
            {
                _logger.LogExit(nameof(AddGame));
                return false;
            }
            if (string.IsNullOrEmpty(game.ExecutablePath))
            {
                _logger.LogExit(nameof(AddGame));
                return false;
            }

            try
            {
                var fullPath = Path.GetFullPath(game.ExecutablePath);
            }
            catch
            {
                _logger.LogExit(nameof(AddGame));
                return false;
            }

            lock (_lock)
            {
                if (_games.Any(g => g.ExecutablePath.Equals(game.ExecutablePath, StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogExit(nameof(AddGame));
                    return false;
                }

                _games.Add(game);
                Save();
                _logger.LogInfo($"[Library] Jogo adicionado: {game.Name}");
                _logger.LogExit(nameof(AddGame));
                return true;
            }
        }

        public bool RemoveGame(string executablePath)
        {
            _logger.LogEntry(nameof(RemoveGame));
            if (string.IsNullOrEmpty(executablePath))
            {
                _logger.LogExit(nameof(RemoveGame));
                return false;
            }

            lock (_lock)
            {
                var game = _games.FirstOrDefault(g =>
                    g.ExecutablePath.Equals(executablePath, StringComparison.OrdinalIgnoreCase));

                if (game == null)
                {
                    _logger.LogExit(nameof(RemoveGame));
                    return false;
                }

                _games.Remove(game);
                Save();
                _logger.LogInfo($"[Library] Jogo removido: {game.Name}");
                _logger.LogExit(nameof(RemoveGame));
                return true;
            }
        }

        public bool RemoveGameByName(string gameName)
        {
            _logger.LogEntry(nameof(RemoveGameByName));
            if (string.IsNullOrEmpty(gameName))
            {
                _logger.LogExit(nameof(RemoveGameByName));
                return false;
            }

            lock (_lock)
            {
                var game = _games.FirstOrDefault(g =>
                    g.Name.Equals(gameName, StringComparison.OrdinalIgnoreCase));

                if (game == null)
                {
                    _logger.LogExit(nameof(RemoveGameByName));
                    return false;
                }

                _games.Remove(game);
                Save();
                _logger.LogInfo($"[Library] Jogo removido pelo nome: {game.Name}");
                _logger.LogExit(nameof(RemoveGameByName));
                return true;
            }
        }

        public void UpdateGameProfile(string gameName, GamerModels.GameProfile profile)
        {
            _logger.LogEntry(nameof(UpdateGameProfile));
            if (string.IsNullOrEmpty(gameName) || profile == null)
            {
                _logger.LogExit(nameof(UpdateGameProfile));
                return;
            }

            lock (_lock)
            {
                var game = _games.FirstOrDefault(g =>
                    g.Name.Equals(gameName, StringComparison.OrdinalIgnoreCase));

                if (game != null)
                {
                    game.HasProfile = true;
                    Save();
                    _logger.LogInfo($"[Library] Perfil atualizado para: {game.Name}");
                }
            }
            _logger.LogExit(nameof(UpdateGameProfile));
        }

        public bool ContainsGame(string executablePath)
        {
            _logger.LogEntry(nameof(ContainsGame));
            if (string.IsNullOrEmpty(executablePath))
            {
                _logger.LogExit(nameof(ContainsGame));
                return false;
            }

            lock (_lock)
            {
                var result = _games.Any(g =>
                    g.ExecutablePath.Equals(executablePath, StringComparison.OrdinalIgnoreCase));
                _logger.LogExit(nameof(ContainsGame));
                return result;
            }
        }

        public async Task<int> RefreshAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogEntry(nameof(RefreshAsync));
            _logger.LogExit(nameof(RefreshAsync));
            return await Task.FromResult(_games.Count);
        }

        public int CleanCache(int minimumConfidence = 45)
        {
            _logger.LogEntry(nameof(CleanCache));
            lock (_lock)
            {
                int before = _games.Count;
                var removed = new List<GamerModels.DetectedGame>();

                var filtered = _games.Where(g =>
                {
                    bool keep = true;
                    string? reason = null;

                    if (g.ConfidenceScore > 0 && g.ConfidenceScore < minimumConfidence)
                    {
                        reason = $"Confiança baixa ({g.ConfidenceScore} < {minimumConfidence})";
                        keep = false;
                    }

                    if (!string.IsNullOrEmpty(g.ExecutablePath) && !File.Exists(g.ExecutablePath))
                    {
                        reason = $"Executável não existe mais: {g.ExecutablePath}";
                        keep = false;
                    }

                    if (!keep) removed.Add(g);
                    return keep;
                }).ToList();

                _games = filtered;
                int removedCount = before - _games.Count;

                foreach (var r in removed)
                    _logger.LogInfo($"[Library] [CleanCache] Removido: {r.Name} | Path={r.ExecutablePath} | Score={r.ConfidenceScore}");

                if (removedCount > 0)
                {
                    Save();
                    _logger.LogSuccess($"[Library] [CleanCache] Cache limpo: {removedCount} entradas removidas (restam {_games.Count})");
                }
                else
                {
                    _logger.LogInfo($"[Library] [CleanCache] Nenhuma entrada para limpar ({_games.Count} jogos ok)");
                }

                _logger.LogExit(nameof(CleanCache));
                return removedCount;
            }
        }

        public void Save()
        {
            _logger.LogEntry(nameof(Save));
            try
            {
                lock (_lock)
                {
                    var options = new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
                        WriteIndented = true,
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    };
                    var json = JsonSerializer.Serialize(_games, options);
                    File.WriteAllText(_libraryPath, json);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("[Library] Erro ao salvar biblioteca", ex);
            }
            _logger.LogExit(nameof(Save));
        }

        public void Load()
        {
            _logger.LogEntry(nameof(Load));
            try
            {
                if (!File.Exists(_libraryPath))
                {
                    _games = new List<GamerModels.DetectedGame>();
                    _logger.LogExit(nameof(Load));
                    return;
                }

                lock (_lock)
                {
                    var json = File.ReadAllText(_libraryPath);
                    if (string.IsNullOrWhiteSpace(json) || json.Trim() == "[]" || json.Trim() == "")
                    {
                        _games = new List<GamerModels.DetectedGame>();
                        _logger.LogExit(nameof(Load));
                        return;
                    }

                    var options = new JsonSerializerOptions {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    };
                    _games = JsonSerializer.Deserialize<List<GamerModels.DetectedGame>>(json, options) ?? new List<GamerModels.DetectedGame>();

                    _games = _games.Where(g =>
                    {
                        if (string.IsNullOrEmpty(g.ExecutablePath))
                            return false;

                        try
                        {
                            var path = Path.GetFullPath(g.ExecutablePath);
                            return true;
                        }
                        catch
                        {
                            return false;
                        }
                    }).ToList();

                    _logger.LogInfo($"[Library] Carregados {_games.Count} jogos da biblioteca");
                }
            }
            catch (JsonException jsonEx)
            {
                _logger.LogWarning($"[Library] Erro de formato JSON ao carregar biblioteca: {jsonEx.Message}");
                _games = new List<GamerModels.DetectedGame>();
                Save();
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Library] Erro ao carregar biblioteca: {ex.Message}");
                _games = new List<GamerModels.DetectedGame>();
            }
            _logger.LogExit(nameof(Load));
        }
    }
}
