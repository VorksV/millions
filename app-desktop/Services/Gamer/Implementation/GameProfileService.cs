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
    public class GameProfileService : IGameProfileService
    {
        private readonly ILoggingService _logger;
        private readonly IGamerModeOrchestrator _orchestrator;
        private readonly string _profilesPath;
        private List<GamerModels.GameProfile> _profiles = new();
        private readonly object _lock = new();

        public GameProfileService(ILoggingService logger, IGamerModeOrchestrator orchestrator)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogEntry(nameof(GameProfileService));
            _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));

            var gamesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Games");
            Directory.CreateDirectory(gamesDir);
            _profilesPath = Path.Combine(gamesDir, "profiles.json");

            Load();
            _logger.LogExit(nameof(GameProfileService));
        }

        public IReadOnlyList<GamerModels.GameProfile> GetAllProfiles()
        {
            _logger.LogEntry(nameof(GetAllProfiles));
            lock (_lock)
            {
                _logger.LogExit(nameof(GetAllProfiles));
                return _profiles.ToList().AsReadOnly();
            }
        }

        public GamerModels.GameProfile? GetProfile(string gameName)
        {
            _logger.LogEntry(nameof(GetProfile));
            lock (_lock)
            {
                _logger.LogExit(nameof(GetProfile));
                return _profiles.FirstOrDefault(p =>
                    p.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase));
            }
        }

        public GamerModels.GameProfile? GetProfileByPath(string executablePath)
        {
            _logger.LogEntry(nameof(GetProfileByPath));
            lock (_lock)
            {
                _logger.LogExit(nameof(GetProfileByPath));
                return _profiles.FirstOrDefault(p =>
                    p.ExecutablePath.Equals(executablePath, StringComparison.OrdinalIgnoreCase));
            }
        }

        public void SaveProfile(GamerModels.GameProfile profile)
        {
            _logger.LogEntry(nameof(SaveProfile));
            if (profile == null)
            {
                _logger.LogExit(nameof(SaveProfile));
                return;
            }

            lock (_lock)
            {
                var existing = _profiles.FirstOrDefault(p => p.Id == profile.Id);

                if (existing != null)
                {
                    existing.GameName = profile.GameName;
                    existing.ExecutablePath = profile.ExecutablePath;
                    existing.Settings = profile.Settings;
                    _logger.LogInfo($"[Profile] Perfil atualizado: {profile.GameName}");
                }
                else
                {
                    if (string.IsNullOrEmpty(profile.Id))
                    {
                        profile.Id = Guid.NewGuid().ToString();
                    }
                    profile.CreatedAt = DateTime.Now;
                    _profiles.Add(profile);
                    _logger.LogInfo($"[Profile] Perfil criado: {profile.GameName}");
                }

                Save();
            }
            _logger.LogExit(nameof(SaveProfile));
        }

        public bool DeleteProfile(string gameName)
        {
            _logger.LogEntry(nameof(DeleteProfile));
            lock (_lock)
            {
                var profile = _profiles.FirstOrDefault(p =>
                    p.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase));

                if (profile == null)
                {
                    _logger.LogExit(nameof(DeleteProfile));
                    return false;
                }

                _profiles.Remove(profile);
                Save();
                _logger.LogInfo($"[Profile] Perfil removido: {gameName}");
                _logger.LogExit(nameof(DeleteProfile));
                return true;
            }
        }

        public async Task<bool> ApplyProfileAsync(string gameName, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        {
            _logger.LogEntry(nameof(ApplyProfileAsync));
            try
            {
                var profile = GetProfile(gameName);
                if (profile == null)
                {
                    _logger.LogWarning($"[Profile] Perfil não encontrado: {gameName}");
                    _logger.LogExit(nameof(ApplyProfileAsync));
                    return false;
                }

                _logger.LogInfo($"[Profile] Aplicando perfil: {gameName}");
                progress?.Report(10);

                var options = new GamerModels.GamerOptimizationOptions
                {
                    OptimizeCpu = profile.Settings.OptimizeCPU,
                    OptimizeGpu = profile.Settings.OptimizeGPU,
                    OptimizeNetwork = profile.Settings.OptimizeNetwork,
                    OptimizeMemory = profile.Settings.OptimizeMemory,
                    EnableGameMode = profile.Settings.EnableGameMode,
                    ReduceLatency = profile.Settings.ReduceLatency,
                    CloseBackgroundApps = profile.Settings.CloseBackgroundApps,
                    ApplyFpsBoost = profile.Settings.ApplyFPSBoost,
                    EnableExtremeMode = profile.Settings.EnableExtremeMode,
                    EnableAntiStutter = profile.Settings.EnableAntiStutter,
                    EnableAdaptiveNetwork = profile.Settings.EnableAdaptiveNetwork
                };

                progress?.Report(30);

                var result = await _orchestrator.ActivateAsync(options, profile.ExecutablePath, progress, cancellationToken);

                if (result)
                {
                    profile.LastUsed = DateTime.Now;
                    SaveProfile(profile);
                    _logger.LogSuccess($"[Profile] Perfil aplicado: {gameName}");
                }

                progress?.Report(100);
                _logger.LogExit(nameof(ApplyProfileAsync));
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[Profile] Erro ao aplicar perfil {gameName}", ex);
                _logger.LogExit(nameof(ApplyProfileAsync));
                return false;
            }
        }

        private void Save()
        {
            _logger.LogEntry(nameof(Save));
            try
            {
                var options = new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                var json = JsonSerializer.Serialize(_profiles, options);
                File.WriteAllText(_profilesPath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError("[Profile] Erro ao salvar perfis", ex);
            }
            _logger.LogExit(nameof(Save));
        }

        private void Load()
        {
            _logger.LogEntry(nameof(Load));
            try
            {
                if (!File.Exists(_profilesPath))
                {
                    _profiles = new List<GamerModels.GameProfile>();
                    _logger.LogExit(nameof(Load));
                    return;
                }

                var json = File.ReadAllText(_profilesPath);
                var options = new JsonSerializerOptions {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                _profiles = JsonSerializer.Deserialize<List<GamerModels.GameProfile>>(json, options) ?? new List<GamerModels.GameProfile>();
                _logger.LogInfo($"[Profile] Carregados {_profiles.Count} perfis");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Profile] Erro ao carregar perfis: {ex.Message}");
                _profiles = new List<GamerModels.GameProfile>();
            }
            _logger.LogExit(nameof(Load));
        }
    }
}
