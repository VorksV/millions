using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer.Intelligence.Implementation
{
    public class GameOptimizationHistoryService
    {
        private static readonly Lazy<GameOptimizationHistoryService> _instance = 
            new Lazy<GameOptimizationHistoryService>(() => new GameOptimizationHistoryService(App.LoggingService!));

        public static GameOptimizationHistoryService Instance => _instance.Value;

        private readonly ILoggingService _logger;
        private readonly string _historyFile;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
        private Dictionary<string, GameHistoricalData> _data = new Dictionary<string, GameHistoricalData>(StringComparer.OrdinalIgnoreCase);

        private GameOptimizationHistoryService(ILoggingService logger)
        {
            _logger = logger;
            _logger.LogEntry("[GameOptimizationHistoryService] .ctor");
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Voltris", "Intelligence");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            _historyFile = Path.Combine(dir, "GameOptimizationHistory.json");
            Load();
            _logger.LogExit("[GameOptimizationHistoryService] .ctor");
        }

        private void Load()
        {
            _logger.LogEntry(nameof(Load));
            if (File.Exists(_historyFile))
            {
                try
                {
                    var json = File.ReadAllText(_historyFile);
                    var dict = JsonSerializer.Deserialize<Dictionary<string, GameHistoricalData>>(json);
                    if (dict != null)
                        _data = dict;
                }
                catch
                {
                    _data = new Dictionary<string, GameHistoricalData>(StringComparer.OrdinalIgnoreCase);
                }
            }
            _logger.LogExit(nameof(Load));
        }

        private async Task SaveAsync()
        {
            _logger.LogEntry(nameof(SaveAsync));
            try
            {
                var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,  WriteIndented = true });
                await File.WriteAllTextAsync(_historyFile, json);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[HistoryService] Erro ao salvar histórico: {ex.Message}");
            }
            _logger.LogExit(nameof(SaveAsync));
        }

        public async Task RecordGainAsync(string processName, IntelligentProfileType profileUsed, string metric, double gainPercent)
        {
            _logger.LogEntry(nameof(RecordGainAsync), ("processName", processName), ("profileUsed", profileUsed), ("metric", metric));
            await _lock.WaitAsync();
            try
            {
                if (!_data.TryGetValue(processName, out var history))
                {
                    history = new GameHistoricalData { ProcessName = processName };
                    _data[processName] = history;
                }

                history.LastProfileUsed = profileUsed;
                
                if (!history.AverageGains.ContainsKey(metric))
                {
                    history.AverageGains[metric] = gainPercent;
                }
                else
                {
                    // Exponential Moving Average (EMA) suave
                    history.AverageGains[metric] = (history.AverageGains[metric] * 0.7) + (gainPercent * 0.3);
                }
                
                history.LastPlayedAt = DateTime.Now;
                history.TotalSessions++;

                await SaveAsync();
                
                _logger.LogInfo($"[HistoryService] Ganho registrado para {processName}: {metric} = +{gainPercent:F2}% (Média histórica: +{history.AverageGains[metric]:F2}%)");
                _logger.LogExit(nameof(RecordGainAsync));
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<GameHistoricalData?> GetHistoryAsync(string processName)
        {
            _logger.LogEntry(nameof(GetHistoryAsync), ("processName", processName));
            await _lock.WaitAsync();
            try
            {
                if (_data.TryGetValue(processName, out var history))
                {
                    _logger.LogExit(nameof(GetHistoryAsync), "found");
                    return history;
                }
                _logger.LogExit(nameof(GetHistoryAsync), "not found");
                return null;
            }
            finally
            {
                _lock.Release();
            }
        }
    }

    public class GameHistoricalData
    {
        public string ProcessName { get; set; } = string.Empty;
        public IntelligentProfileType LastProfileUsed { get; set; }
        public DateTime LastPlayedAt { get; set; }
        public int TotalSessions { get; set; }
        public Dictionary<string, double> AverageGains { get; set; } = new Dictionary<string, double>();
    }
}
