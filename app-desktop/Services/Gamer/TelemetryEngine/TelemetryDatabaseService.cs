using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Gamer.StrategyFactory.Models;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer.TelemetryEngine
{
    public interface ITelemetryDatabaseService
    {
        Task InitializeDatabaseAsync();
        Task SaveOptimizationResultAsync(OptimizationConfidence confidence);
        Task<OptimizationConfidence?> GetOptimizationConfidenceAsync(string optimizationId);
        Task LogSessionTelemetryAsync(string gameName, double avgFps, double p99Frametime, double onePercentLow);
    }

    public class TelemetryDatabaseService : ITelemetryDatabaseService
    {
        private readonly ILoggingService _logger;
        private readonly string _dbPath;

        public TelemetryDatabaseService(ILoggingService logger)
        {
            _logger.LogEntry(nameof(TelemetryDatabaseService));
            _logger = logger;
            
            // Salvar no AppData do VOLTRIS
            var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "VoltrisOptimizer");
            if (!Directory.Exists(appData)) Directory.CreateDirectory(appData);
            
            _dbPath = Path.Combine(appData, "VoltrisML.db");
            _logger.LogExit(nameof(TelemetryDatabaseService));
        }

        public async Task InitializeDatabaseAsync()
        {
            _logger.LogEntry(nameof(InitializeDatabaseAsync));
            _logger.LogInfo("[TelemetryDB] Inicializando Banco de Dados Local de Machine Learning...");
            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var command = connection.CreateCommand();
                command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS OptimizationsConfidence (
                        OptimizationId TEXT PRIMARY KEY,
                        ConfidenceScore INTEGER,
                        SuccessCount INTEGER,
                        FailureCount INTEGER,
                        AverageFpsDeltaPercent REAL,
                        AverageFrametimeDeltaPercent REAL
                    );

                    CREATE TABLE IF NOT EXISTS SessionTelemetry (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Timestamp DATETIME DEFAULT CURRENT_TIMESTAMP,
                        GameName TEXT,
                        AvgFps REAL,
                        P99Frametime REAL,
                        OnePercentLow REAL
                    );
                ";
                await command.ExecuteNonQueryAsync();
                _logger.LogSuccess("[TelemetryDB] Banco de Dados Inicializado com Sucesso.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[TelemetryDB] Falha ao criar banco de dados: {ex.Message}");
            }
            _logger.LogExit(nameof(InitializeDatabaseAsync));
        }

        public async Task SaveOptimizationResultAsync(OptimizationConfidence confidence)
        {
            _logger.LogEntry(nameof(SaveOptimizationResultAsync));
            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO OptimizationsConfidence (OptimizationId, ConfidenceScore, SuccessCount, FailureCount, AverageFpsDeltaPercent, AverageFrametimeDeltaPercent)
                    VALUES ($id, $score, $success, $fail, $fpsDelta, $frameDelta)
                    ON CONFLICT(OptimizationId) DO UPDATE SET
                        ConfidenceScore = $score,
                        SuccessCount = $success,
                        FailureCount = $fail,
                        AverageFpsDeltaPercent = $fpsDelta,
                        AverageFrametimeDeltaPercent = $frameDelta;
                ";
                command.Parameters.AddWithValue("$id", confidence.OptimizationId);
                command.Parameters.AddWithValue("$score", confidence.ConfidenceScore);
                command.Parameters.AddWithValue("$success", confidence.SuccessCount);
                command.Parameters.AddWithValue("$fail", confidence.FailureCount);
                command.Parameters.AddWithValue("$fpsDelta", confidence.AverageFpsDeltaPercent);
                command.Parameters.AddWithValue("$frameDelta", confidence.AverageFrametimeDeltaPercent);

                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"[TelemetryDB] Erro salvando resultado: {ex.Message}");
            }
            _logger.LogExit(nameof(SaveOptimizationResultAsync));
        }

        public async Task<OptimizationConfidence?> GetOptimizationConfidenceAsync(string optimizationId)
        {
            _logger.LogEntry(nameof(GetOptimizationConfidenceAsync));
            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var command = connection.CreateCommand();
                command.CommandText = "SELECT * FROM OptimizationsConfidence WHERE OptimizationId = $id";
                command.Parameters.AddWithValue("$id", optimizationId);

                using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return new OptimizationConfidence
                    {
                        OptimizationId = reader.GetString(0),
                        ConfidenceScore = reader.GetInt32(1),
                        SuccessCount = reader.GetInt32(2),
                        FailureCount = reader.GetInt32(3),
                        AverageFpsDeltaPercent = reader.GetDouble(4),
                        AverageFrametimeDeltaPercent = reader.GetDouble(5)
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[TelemetryDB] Erro lendo confiança: {ex.Message}");
            }
            _logger.LogExit(nameof(GetOptimizationConfidenceAsync));
            return null; // Não existe ainda (começa com 50% default se instanciado fora)
        }

        public async Task LogSessionTelemetryAsync(string gameName, double avgFps, double p99Frametime, double onePercentLow)
        {
            _logger.LogEntry(nameof(LogSessionTelemetryAsync));
            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO SessionTelemetry (GameName, AvgFps, P99Frametime, OnePercentLow)
                    VALUES ($name, $fps, $p99, $onePercent)
                ";
                command.Parameters.AddWithValue("$name", gameName);
                command.Parameters.AddWithValue("$fps", avgFps);
                command.Parameters.AddWithValue("$p99", p99Frametime);
                command.Parameters.AddWithValue("$onePercent", onePercentLow);

                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"[TelemetryDB] Erro salvando telemetria: {ex.Message}");
            }
            _logger.LogExit(nameof(LogSessionTelemetryAsync));
        }
    }
}
