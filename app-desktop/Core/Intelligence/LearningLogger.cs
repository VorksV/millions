using System;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Core.Intelligence;

public class LearningLogger : ILearningLogger
{
	private readonly string _dbPath;

	private readonly string _connectionString;

	private readonly ILoggingService _logger;

	private readonly SemaphoreSlim _initLock = new SemaphoreSlim(1, 1);

	// Serializa TODAS as escritas para evitar SQLite Error 5 (database is locked).
	// WAL mode permite leituras concorrentes, mas escritas ainda precisam de serialização
	// no nível da aplicação para conexões múltiplas simultâneas.
	private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);

	private Task? _initTask;

	private bool _initialized;

	public LearningLogger(ILoggingService logger)
	{
		_logger = logger;
		string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
		string text = Path.Combine(folderPath, "Voltris", "Brain");
		if (!Directory.Exists(text))
		{
			Directory.CreateDirectory(text);
		}
		_dbPath = Path.Combine(text, "learning.db");
		// Cache=Shared permite que múltiplas conexões do mesmo processo compartilhem
		// o page cache e reduzam contenção de I/O
		_connectionString = "Data Source=" + _dbPath + ";Cache=Shared";
		_initTask = Task.Run(() => InitializeDatabaseAsync());
	}

	private async Task EnsureInitializedAsync()
	{
		if (_initialized)
		{
			return;
		}
		await _initLock.WaitAsync().ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			if (!_initialized)
			{
				if (_initTask != null)
				{
					await _initTask!.ConfigureAwait(continueOnCapturedContext: false);
					_initTask = null;
				}
				_initialized = true;
			}
		}
		finally
		{
			_initLock.Release();
		}
	}

	private async Task InitializeDatabaseAsync()
	{
		try
		{
			using SqliteConnection connection = new SqliteConnection(_connectionString);
			await connection.OpenAsync().ConfigureAwait(continueOnCapturedContext: false);

			// WAL (Write-Ahead Logging): elimina a maioria dos lock contention.
			// Permite leituras simultâneas enquanto escritas ocorrem e reduz drasticamente
			// a frequência de SQLite Error 5 em padrões de acesso concorrente.
			// NORMAL synchronous garante durabilidade suficiente sem penalidade de fsync em todo commit.
			var walCmd = connection.CreateCommand();
			walCmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
			await walCmd.ExecuteNonQueryAsync().ConfigureAwait(continueOnCapturedContext: false);

			SqliteCommand command = connection.CreateCommand();
			command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS brain_decisions (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Timestamp TEXT NOT NULL,
                        RuleName TEXT NOT NULL,
                        Score REAL NOT NULL,
                        Reason TEXT,
                        HardwareHash TEXT
                    );
                    CREATE TABLE IF NOT EXISTS pattern_events (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Timestamp TEXT NOT NULL,
                        EventType TEXT NOT NULL,
                        Severity REAL NOT NULL,
                        Details TEXT
                    );
                ";
			await command.ExecuteNonQueryAsync().ConfigureAwait(continueOnCapturedContext: false);
			_logger.LogInfo("[LEARN] Banco de dados de aprendizado inicializado com sucesso em: " + _dbPath);
		}
		catch (Exception ex2)
		{
			Exception ex = ex2;
			_logger.LogError("[LEARN] Erro crítico ao criar banco SQLite de aprendizado.", ex);
		}
	}

	public async Task LogBrainDecisionAsync(string ruleName, double score, string reason, string hardwareHash)
	{
		try
		{
			await EnsureInitializedAsync().ConfigureAwait(continueOnCapturedContext: false);

			// Serializar escritas: apenas uma operação de escrita por vez no banco
			await _writeLock.WaitAsync().ConfigureAwait(false);
			try
			{
				ILoggingService logger = _logger;
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(63, 3);
				defaultInterpolatedStringHandler.AppendLiteral("[BRAIN-DECISION] Salvando Memória - Regra: ");
				defaultInterpolatedStringHandler.AppendFormatted(ruleName);
				defaultInterpolatedStringHandler.AppendLiteral(" | Score: ");
				defaultInterpolatedStringHandler.AppendFormatted(score);
				defaultInterpolatedStringHandler.AppendLiteral(" | Razão: ");
				defaultInterpolatedStringHandler.AppendFormatted(reason);
				logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
				using SqliteConnection connection = new SqliteConnection(_connectionString);
				await connection.OpenAsync().ConfigureAwait(continueOnCapturedContext: false);
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = @"
                    INSERT INTO brain_decisions (Timestamp, RuleName, Score, Reason, HardwareHash)
                    VALUES ($timestamp, $ruleName, $score, $reason, $hardwareHash)
                ";
				command.Parameters.AddWithValue("$timestamp", DateTime.UtcNow.ToString("o"));
				command.Parameters.AddWithValue("$ruleName", ruleName);
				command.Parameters.AddWithValue("$score", score);
				command.Parameters.AddWithValue("$reason", reason);
				command.Parameters.AddWithValue("$hardwareHash", hardwareHash);
				await command.ExecuteNonQueryAsync().ConfigureAwait(continueOnCapturedContext: false);
			}
			finally
			{
				_writeLock.Release();
			}
		}
		catch (Exception ex2)
		{
			Exception ex = ex2;
			_logger.LogError("[LEARN] Erro ao salvar log de decisão do cérebro.", ex);
		}
	}

	public async Task LogPatternEventAsync(string eventType, double severity, string details)
	{
		try
		{
			await EnsureInitializedAsync().ConfigureAwait(continueOnCapturedContext: false);

			// Serializar escritas: apenas uma operação de escrita por vez no banco
			await _writeLock.WaitAsync().ConfigureAwait(false);
			try
			{
				ILoggingService logger = _logger;

				// BUG CORRIGIDO: TODA amostra de telemetria vira um "pattern event"
				// (PatternRecognitionService.RecordEventAsync chama isto a cada
				// amostra), e o log rotulava todas como "Evento Crítico" em nivel
				// WARNING. O caso comum e STABLE_SESSION com severidade 0.1 --
				// ou seja, o estado SAUDÁVEL. O resultado era um WARNING
				// "Evento Crítico" a cada ~30s com CPU a 19-30%, que alarmava
				// sem BASIS e poluia o log.
				//
				// Agora o rotulo e o nivel acompanham a severidade real:
				//   >= 0.7 -> Crítico  (WARNING)
				//   >= 0.3 -> Relevante (INFO)
				//   <  0.3 -> Rotina    (INFO, e nao e registrado no log)
				bool critico = severity >= 0.7;
				string rotulo = critico
					? "Evento Crítico"
					: (severity >= 0.3 ? "Evento Relevante" : "Amostra Rotina");

				string mensagem =
					"[PATTERN] " + rotulo +
					" - Tipo: " + eventType +
					" | Severidade: " + severity.ToString("F2", CultureInfo.InvariantCulture) +
					" | Detalhes: " + details;

				// Amostra de rotina nao e evento: oLearningLogger ja persiste no
				// SQLite, entao o log de texto seria apenas ruido repetido.
				if (critico)
				{
					logger.LogWarning(mensagem);
				}
				else if (severity >= 0.3)
				{
					logger.LogInfo(mensagem);
				}

				using SqliteConnection connection = new SqliteConnection(_connectionString);
				await connection.OpenAsync().ConfigureAwait(continueOnCapturedContext: false);
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = @"
                    INSERT INTO pattern_events (Timestamp, EventType, Severity, Details)
                    VALUES ($timestamp, $eventType, $severity, $details)
                ";
				command.Parameters.AddWithValue("$timestamp", DateTime.UtcNow.ToString("o"));
				command.Parameters.AddWithValue("$eventType", eventType);
				command.Parameters.AddWithValue("$severity", severity);
				command.Parameters.AddWithValue("$details", details);
				await command.ExecuteNonQueryAsync().ConfigureAwait(continueOnCapturedContext: false);
			}
			finally
			{
				_writeLock.Release();
			}
		}
		catch (Exception ex2)
		{
			Exception ex = ex2;
			_logger.LogError("[LEARN] Erro ao salvar log de evento de padrão.", ex);
		}
	}

	public async Task<string> GenerateReportAsync()
	{
		return "Relatório Forense de ML a ser gerado.";
	}
}
