using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Gamer.Pipeline.Models;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer.Pipeline
{
    public class SessionMemoryService : ISessionMemory
    {
        private readonly ILoggingService _logger;
        private static readonly string StoragePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VoltrisOptimizer", "GamerMemory");

        private readonly Dictionary<string, List<StoredSessionData>> _gameHistory = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, OptimizationHistoryData> _optHistory = new(StringComparer.OrdinalIgnoreCase);

        public SessionMemoryService(ILoggingService logger)
        {
            _logger.LogEntry(nameof(SessionMemoryService));
            _logger = logger;
            _logger.LogDebug("[SessionMemoryService.ctor] Entry");
            LoadFromDisk();
            _logger.LogDebug("[SessionMemoryService.ctor] Exit");
            _logger.LogExit(nameof(SessionMemoryService));
        }

        public async Task SaveSessionAsync(GamerSessionContext context)
        {
            _logger.LogEntry(nameof(SaveSessionAsync));
            _logger.LogInfo("[SessionMemoryService.SaveSessionAsync] Entry");
            try
            {
                var gameName = context.Game.Name;
                if (string.IsNullOrEmpty(gameName)) gameName = context.GameProcessName ?? "unknown";

                var data = new StoredSessionData
                {
                    GameName = gameName,
                    TotalOptimizations = context.Decisions.Count,
                    Validated = context.Results.Count(r => r.PassedValidation),
                    Placebos = context.Results.Count(r => !r.PassedValidation),
                    AvgFpsImprovement = context.Results
                        .Where(r => r.PassedValidation)
                        .Select(r => r.MeasuredFpsDelta)
                        .DefaultIfEmpty(0)
                        .Average(),
                    PlayedAt = DateTime.UtcNow
                };

                lock (_gameHistory)
                {
                    if (!_gameHistory.ContainsKey(gameName))
                        _gameHistory[gameName] = new List<StoredSessionData>();
                    _gameHistory[gameName].Add(data);

                    if (_gameHistory[gameName].Count > 50)
                        _gameHistory[gameName].RemoveAt(0);
                }

                foreach (var result in context.Results)
                {
                    lock (_optHistory)
                    {
                        if (!_optHistory.ContainsKey(result.OptimizationId))
                            _optHistory[result.OptimizationId] = new OptimizationHistoryData();

                        var h = _optHistory[result.OptimizationId];
                        h.TotalApplications++;
                        if (result.PassedValidation)
                        {
                            h.PassCount++;
                            h.AvgFpsDeltaPercent = (h.AvgFpsDeltaPercent * (h.TotalApplications - 1) + result.MeasuredFpsDelta) / h.TotalApplications;
                        }
                        else
                        {
                            h.FailCount++;
                        }
                        h.Confidence = h.TotalApplications > 0
                            ? (double)h.PassCount / h.TotalApplications * 100
                            : 0;
                    }
                }

                SaveToDisk();

                if (data.TotalOptimizations > 0)
                {
                    _logger.LogInfo($"[SessionMemory] 💾 Sessão salva: {gameName} | {data.Validated}/{data.TotalOptimizations} OK | Placebos: {data.Placebos} | ΔFPS: {data.AvgFpsImprovement:F1}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[SessionMemory] ⚠ Erro ao salvar sessão: {ex.Message}");
            }

            await Task.CompletedTask;
            _logger.LogInfo("[SessionMemoryService.SaveSessionAsync] Exit");
            _logger.LogExit(nameof(SaveSessionAsync));
        }

        public Task<StoredSessionData?> GetLastSessionForGameAsync(string gameName)
        {
            _logger.LogEntry(nameof(GetLastSessionForGameAsync));
            _logger.LogDebug("[SessionMemoryService.GetLastSessionForGameAsync] Entry");
            lock (_gameHistory)
            {
                if (_gameHistory.TryGetValue(gameName, out var sessions) && sessions.Count > 0)
                {
                    _logger.LogDebug("[SessionMemoryService.GetLastSessionForGameAsync] Exit (found)");
                    _logger.LogExit(nameof(GetLastSessionForGameAsync));
                    return Task.FromResult<StoredSessionData?>(sessions.Last());
                }
            }
            _logger.LogDebug("[SessionMemoryService.GetLastSessionForGameAsync] Exit (not found)");
            _logger.LogExit(nameof(GetLastSessionForGameAsync));
            return Task.FromResult<StoredSessionData?>(null);
        }

        public Task<OptimizationHistoryData> GetOptimizationHistoryAsync(string optimizationId)
        {
            _logger.LogEntry(nameof(GetOptimizationHistoryAsync));
            _logger.LogDebug("[SessionMemoryService.GetOptimizationHistoryAsync] Entry");
            lock (_optHistory)
            {
                if (_optHistory.TryGetValue(optimizationId, out var data))
                {
                    _logger.LogDebug("[SessionMemoryService.GetOptimizationHistoryAsync] Exit (found)");
                    _logger.LogExit(nameof(GetOptimizationHistoryAsync));
                    return Task.FromResult(data);
                }
            }
            _logger.LogDebug("[SessionMemoryService.GetOptimizationHistoryAsync] Exit (not found)");
            _logger.LogExit(nameof(GetOptimizationHistoryAsync));
            return Task.FromResult(new OptimizationHistoryData());
        }

        private void SaveToDisk()
        {
            _logger.LogEntry(nameof(SaveToDisk));
            _logger.LogDebug("[SessionMemoryService.SaveToDisk] Entry");
            try
            {
                Directory.CreateDirectory(StoragePath);
                var gamePath = Path.Combine(StoragePath, "game_sessions.json");
                var optPath = Path.Combine(StoragePath, "optimization_history.json");

                lock (_gameHistory)
                {
                    var json = JsonSerializer.Serialize(_gameHistory, new JsonSerializerOptions { WriteIndented = true, ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                    File.WriteAllText(gamePath, json);
                }

                lock (_optHistory)
                {
                    var json2 = JsonSerializer.Serialize(_optHistory, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                    File.WriteAllText(optPath, json2);
                }
            }
            catch { }
            _logger.LogDebug("[SessionMemoryService.SaveToDisk] Exit");
            _logger.LogExit(nameof(SaveToDisk));
        }

        private void LoadFromDisk()
        {
            _logger.LogEntry(nameof(LoadFromDisk));
            _logger.LogDebug("[SessionMemoryService.LoadFromDisk] Entry");
            try
            {
                var gamePath = Path.Combine(StoragePath, "game_sessions.json");
                if (File.Exists(gamePath))
                {
                    var json = File.ReadAllText(gamePath);
                    var data = JsonSerializer.Deserialize<Dictionary<string, List<StoredSessionData>>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                    if (data != null)
                    {
                        lock (_gameHistory)
                        {
                            foreach (var kv in data)
                                _gameHistory[kv.Key] = kv.Value;
                        }
                    }
                }

                var optPath = Path.Combine(StoragePath, "optimization_history.json");
                if (File.Exists(optPath))
                {
                    var json2 = File.ReadAllText(optPath);
                    var data2 = JsonSerializer.Deserialize<Dictionary<string, OptimizationHistoryData>>(json2, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                    if (data2 != null)
                    {
                        lock (_optHistory)
                        {
                            foreach (var kv in data2)
                                _optHistory[kv.Key] = kv.Value;
                        }
                    }
                }

                _logger.LogInfo($"[SessionMemory] 💾 Carregado: {_gameHistory.Count} jogos, {_optHistory.Count} histórico de otimizações");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[SessionMemory] ⚠ Erro ao carregar: {ex.Message}");
            }
            _logger.LogDebug("[SessionMemoryService.LoadFromDisk] Exit");
            _logger.LogExit(nameof(LoadFromDisk));
        }
    }
}
