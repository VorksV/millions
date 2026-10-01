using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Core.Intelligence;

public sealed class BehaviorScoreEngine : IBehaviorScoreEngine, IAutoStartService
{
	private readonly ILoggingService _logger;

	private readonly string _storagePath;

	private UserBehaviorProfile _profile = new UserBehaviorProfile();

	private readonly object _lock = new object();

	public BehaviorScoreEngine(ILoggingService logger)
	{
		_logger = logger ?? throw new ArgumentNullException("logger");
		_storagePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Voltris", "Brain", "behavior_scores.json");
	}

	public async Task StartAsync(CancellationToken ct = default(CancellationToken))
	{
		_logger.LogInfo("[SCORE] Carregando perfil de comportamento...");
		await LoadAsync();
	}

	public UserBehaviorProfile GetProfile()
	{
		lock (_lock)
		{
			return _profile;
		}
	}

	public void UpdateFromSession(int gamingMinutes, int workMinutes)
	{
		lock (_lock)
		{
			_profile.TotalGamingHours += (double)gamingMinutes / 60.0;
			_profile.TotalWorkHours += (double)workMinutes / 60.0;
			_profile.TotalSessions++;
			_profile.LastCalculated = DateTime.UtcNow;
			Recalculate();
		}
		SaveAsync();
	}

	public void Recalculate()
	{
		lock (_lock)
		{
			double gamingRatio = _profile.GamingRatio;
			if (gamingRatio > 0.7 && _profile.TotalGamingHours > 50.0)
			{
				_profile.Category = UserCategory.Hardcore;
			}
			else if (gamingRatio < 0.3)
			{
				_profile.Category = UserCategory.Work;
			}
			else
			{
				_profile.Category = UserCategory.Casual;
			}
			
			UserCategory category = _profile.Category;
			
			double baseScore = category switch
			{
				UserCategory.Hardcore => 85.0, 
				UserCategory.Casual => 50.0, 
				UserCategory.Work => 30.0, 
				_ => 50.0};
			
			double score = baseScore;
			double avgDailyGaming = _profile.TotalSessions > 0 ? (_profile.TotalGamingHours / Math.Max(1.0, _profile.TotalSessions / 3.0)) : 0.0;
			
			if (avgDailyGaming > 2.0)
			{
				score += 5.0;
				_logger.LogDebug("[BEHAVIOR] +5 pontos: Gaming > 2h/dia média");
			}
			
			double totalHours = _profile.TotalGamingHours + _profile.TotalWorkHours;
			double workRatio = totalHours > 0.0 ? (_profile.TotalWorkHours / totalHours) : 0.0;
			
			if (workRatio > 0.6)
			{
				score -= 2.0;
				_logger.LogDebug("[BEHAVIOR] -2 pontos: WorkMode dominante (>60%)");
			}
			
			_profile.AggressionScore = Math.Clamp(score, 0.0, 100.0);
			
			_logger.LogInfo($"[BEHAVIOR] Perfil recalculado: {_profile.Category} | Score: {_profile.AggressionScore:F1} | GamingRatio: {gamingRatio:F2} | AvgDaily: {avgDailyGaming:F1}h");
		}
	}

	public async Task LoadAsync()
	{
		try
		{
			if (!File.Exists(_storagePath))
			{
				return;
			}
			using FileStream fs = new FileStream(_storagePath, FileMode.Open, FileAccess.Read);
			UserBehaviorProfile loaded = await JsonSerializer.DeserializeAsync<UserBehaviorProfile>(fs, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
			if (loaded != null)
			{
				lock (_lock)
				{
					_profile = loaded;
				}
				_logger.LogInfo($"[BEHAVIOR] Perfil carregado: {_profile.Category}");
			}
		}
		catch (Exception ex2)
		{
			Exception ex = ex2;
			_logger.LogError("[BEHAVIOR] Erro ao carregar perfil: " + ex.Message);
		}
	}

	public async Task SaveAsync()
	{
		try
		{
			UserBehaviorProfile toSave;
			lock (_lock)
			{
				toSave = _profile;
			}
			string dir = Path.GetDirectoryName(_storagePath);
			if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
			{
				Directory.CreateDirectory(dir);
			}
			using FileStream fs = new FileStream(_storagePath, FileMode.Create, FileAccess.Write);
			await JsonSerializer.SerializeAsync((Stream)fs, toSave, new JsonSerializerOptions { WriteIndented = true,
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase
			}, default(CancellationToken));
		}
		catch (Exception ex2)
		{
			Exception ex = ex2;
			_logger.LogError("[BEHAVIOR] Erro ao salvar perfil: " + ex.Message);
		}
	}
}
