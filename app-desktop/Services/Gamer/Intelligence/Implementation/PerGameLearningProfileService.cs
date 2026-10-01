using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Gamer.Intelligence.Implementation
{
    public class PerGameLearningProfileService
    {
        private readonly ILoggingService _logger;
        private readonly string _profilesPath;
        
        private Dictionary<string, GameLearningProfile> _profiles = new();
        private GameLearningProfile? _currentProfile;
        private DateTime _sessionStart;
        private List<PerformanceSnapshot> _sessionSnapshots = new();

        public GameLearningProfile? CurrentProfile => _currentProfile;
        public int TotalGamesLearned => _profiles.Count;

        public PerGameLearningProfileService(ILoggingService logger)
        {
            // 🧠 LOGGING MÁXIMO: Inicialização do serviço de aprendizado por jogo
            _logger = logger;
            _logger.LogInfo("🧠 [GameLearning] INICIANDO SERVIÇO DE APRENDIZADO POR JOGO");
            _logger.LogInfo($"🧠 [GameLearning] Data/Hora: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            _logger.LogInfo($"🧠 [GameLearning] Thread ID: {System.Threading.Thread.CurrentThread.ManagedThreadId}");
            
            try
            {
                // Caminho unificado para perfis de aprendizado (consolidado em %LOCALAPPDATA%\Voltris\Games)
                var gamesDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Voltris", "Games");
                Directory.CreateDirectory(gamesDir);
                _profilesPath = Path.Combine(gamesDir, "learning_profiles.json");
                
                _logger.LogInfo($"🧠 [GameLearning] Diretório de perfis unificado: {_profilesPath}");
                
                Directory.CreateDirectory(_profilesPath);
                LoadAllProfiles();
                
                _logger.LogSuccess($"🧠 [GameLearning] Serviço iniciado com {_profiles.Count} perfis carregados");
            }
            catch (Exception ex)
            {
                _logger.LogError($"🧠 [GameLearning] ERRO FATAL ao inicializar: {ex.Message}", ex);
                _logger.LogError($"🧠 [GameLearning] Stack Trace: {ex.StackTrace}");
                throw;
            }
        }

        public void StartLearningSession(string gameExecutable, string gameName)
        {
            try
            {
                string gameId = GetGameId(gameExecutable);
                
                if (!_profiles.ContainsKey(gameId))
                {
                    _profiles[gameId] = new GameLearningProfile
                    {
                        GameId = gameId,
                        GameName = gameName,
                        ExecutableName = gameExecutable,
                        FirstSeen = DateTime.UtcNow
                    };
                    
                    _logger.LogInfo($"[GameLearning] 🎮 Novo jogo detectado: {gameName}");
                }

                _currentProfile = _profiles[gameId];
                _sessionStart = DateTime.UtcNow;
                _sessionSnapshots.Clear();
                
                _currentProfile.SessionCount++;
                _currentProfile.LastPlayed = DateTime.UtcNow;

                _logger.LogSuccess($"[GameLearning] ✅ Sessão iniciada: {gameName} (Sessão #{_currentProfile.SessionCount})");
                
                // 🔥 DIAGNÓSTICO PROFISSIONAL - Enviar para Telegram
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var diagnosticMsg = $"🎯 <b>DIAGNÓSTICO GAMES LEARNED</b>\n" +
                            $"• <b>Data/Hora:</b> {DateTime.Now:HH:mm:ss.fff}\n" +
                            $"• <b>Jogo Detectado:</b> {gameName}\n" +
                            $"• <b>Executable:</b> {gameExecutable}\n" +
                            $"• <b>GameId:</b> {gameId}\n" +
                            $"• <b>Total Games Learned:</b> {_profiles.Count}\n" +
                            $"• <b>Current Profile:</b> {(_currentProfile?.GameName ?? "NULL")}\n" +
                            $"• <b>Session Count:</b> {_currentProfile?.SessionCount ?? 0}\n" +
                            $"• <b>Last Played:</b> {_currentProfile?.LastPlayed:yyyy-MM-dd HH:mm:ss}\n" +
                            $"• <b>Session Snapshots:</b> {_sessionSnapshots.Count}\n" +
                            $"• <b>Profiles Path:</b> {_profilesPath}\n" +
                            $"• <b>Status:</b> APRENDENDO";
                        

                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"[GameLearning] Erro no diagnóstico via Telegram: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GameLearning] Erro ao iniciar sessão: {ex.Message}");
            }
        }

        public void RecordPerformanceSnapshot(PerformanceSnapshot snapshot)
        {
            if (_currentProfile == null) return;

            try
            {
                _sessionSnapshots.Add(snapshot);
                _currentProfile.TotalFrames += snapshot.FrameCount;
                _currentProfile.TotalPlayTimeMinutes += (DateTime.UtcNow - _sessionStart).TotalMinutes;

                if (_sessionSnapshots.Count % 100 == 0)
                {
                    _logger.LogInfo($"[GameLearning] 📊 Snapshots coletados: {_sessionSnapshots.Count}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GameLearning] Erro ao registrar snapshot: {ex.Message}");
            }
        }

        public async Task<GameLearningProfile?> EndLearningSessionAsync()
        {
            if (_currentProfile == null || _sessionSnapshots.Count == 0)
                return null;

            try
            {
                _logger.LogInfo("═══════════════════════════════════════════════════════════");
                _logger.LogSuccess($"🎮 ANALISANDO SESSÃO: {_currentProfile.GameName}");
                _logger.LogInfo("═══════════════════════════════════════════════════════════");

                AnalyzePerformanceData();
                IdentifyBottlenecks();
                DetermineOptimalProfile();
                await SaveProfileAsync(_currentProfile);

                _logger.LogInfo($"[GameLearning] 📊 Snapshots analisados: {_sessionSnapshots.Count}");
                _logger.LogInfo($"[GameLearning] ⏱️ Tempo de jogo: {(DateTime.UtcNow - _sessionStart).TotalMinutes:F1} min");
                _logger.LogInfo($"[GameLearning] 🎯 Gargalo principal: {_currentProfile.PrimaryBottleneck}");
                _logger.LogInfo($"[GameLearning] 📈 FPS médio: {_currentProfile.AvgFps:F1}");
                _logger.LogInfo($"[GameLearning] 📉 1% Low: {_currentProfile.Fps1PercentLow:F1}");
                
                _logger.LogInfo("═══════════════════════════════════════════════════════════");
                _logger.LogSuccess($"✅ PERFIL ATUALIZADO: {_currentProfile.GameName}");
                _logger.LogInfo("═══════════════════════════════════════════════════════════");

                var profile = _currentProfile;
                _currentProfile = null;
                _sessionSnapshots.Clear();

                return profile;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GameLearning] Erro ao finalizar sessão: {ex.Message}");
                return null;
            }
        }

        private void AnalyzePerformanceData()
        {
            if (_sessionSnapshots.Count == 0 || _currentProfile == null) return;

            _currentProfile.AvgFps = _sessionSnapshots.Average(s => s.Fps);
            
            var sortedFps = _sessionSnapshots.Select(s => s.Fps).OrderBy(f => f).ToList();
            int onePercentIndex = Math.Max(0, (int)(sortedFps.Count * 0.01));
            _currentProfile.Fps1PercentLow = sortedFps[onePercentIndex];
            
            int zeroOnePercentIndex = Math.Max(0, (int)(sortedFps.Count * 0.001));
            _currentProfile.Fps01PercentLow = sortedFps[zeroOnePercentIndex];

            _currentProfile.AvgCpuUsage = _sessionSnapshots.Average(s => s.CpuUsage);
            _currentProfile.AvgGpuUsage = _sessionSnapshots.Average(s => s.GpuUsage);
            _currentProfile.AvgRamUsageMb = _sessionSnapshots.Average(s => s.RamUsageMb);
            _currentProfile.AvgVramUsageMb = _sessionSnapshots.Average(s => s.VramUsageMb);

            _currentProfile.MaxCpuUsage = _sessionSnapshots.Max(s => s.CpuUsage);
            _currentProfile.MaxGpuUsage = _sessionSnapshots.Max(s => s.GpuUsage);
            _currentProfile.MaxRamUsageMb = _sessionSnapshots.Max(s => s.RamUsageMb);
            _currentProfile.MaxVramUsageMb = _sessionSnapshots.Max(s => s.VramUsageMb);

            _currentProfile.StutterCount = _sessionSnapshots.Count(s => s.FrameTimeMs > 50);
        }

        private void IdentifyBottlenecks()
        {
            if (_currentProfile == null) return;

            var bottlenecks = new Dictionary<BottleneckType, double>();

            if (_currentProfile.AvgCpuUsage > 85 && _currentProfile.AvgGpuUsage < 70)
                bottlenecks[BottleneckType.CPU] = _currentProfile.AvgCpuUsage;

            if (_currentProfile.AvgGpuUsage > 85 && _currentProfile.AvgCpuUsage < 70)
                bottlenecks[BottleneckType.GPU] = _currentProfile.AvgGpuUsage;

            if (_currentProfile.MaxRamUsageMb > 12000)
                bottlenecks[BottleneckType.RAM] = _currentProfile.MaxRamUsageMb / 1024.0;

            if (_currentProfile.MaxVramUsageMb > 6000)
                bottlenecks[BottleneckType.VRAM] = _currentProfile.MaxVramUsageMb / 1024.0;

            if (Math.Abs(_currentProfile.AvgCpuUsage - _currentProfile.AvgGpuUsage) < 15)
                bottlenecks[BottleneckType.Balanced] = (_currentProfile.AvgCpuUsage + _currentProfile.AvgGpuUsage) / 2;

            if (bottlenecks.Count > 0)
            {
                _currentProfile.PrimaryBottleneck = bottlenecks.OrderByDescending(b => b.Value).First().Key;
            }
            else
            {
                _currentProfile.PrimaryBottleneck = BottleneckType.Unknown;
            }

            _currentProfile.Bottlenecks = bottlenecks;
        }

        private void DetermineOptimalProfile()
        {
            if (_currentProfile == null) return;

            var recommendations = new List<string>();

            switch (_currentProfile.PrimaryBottleneck)
            {
                case BottleneckType.CPU:
                    recommendations.Add("Priorizar otimizações de CPU");
                    recommendations.Add("Reduzir qualidade de sombras e física");
                    recommendations.Add("Desabilitar processos em background");
                    recommendations.Add("Aplicar CPU affinity para P-cores");
                    _currentProfile.RecommendedCpuPriority = "High";
                    _currentProfile.RecommendedGpuPriority = "Normal";
                    break;

                case BottleneckType.GPU:
                    recommendations.Add("Priorizar otimizações de GPU");
                    recommendations.Add("Reduzir resolução ou qualidade de texturas");
                    recommendations.Add("Desabilitar ray tracing se ativo");
                    recommendations.Add("Ativar DLSS/FSR se disponível");
                    _currentProfile.RecommendedCpuPriority = "Normal";
                    _currentProfile.RecommendedGpuPriority = "High";
                    break;

                case BottleneckType.RAM:
                    recommendations.Add("Otimizar uso de RAM");
                    recommendations.Add("Fechar aplicações em background");
                    recommendations.Add("Limpar standby list");
                    recommendations.Add("Considerar upgrade de RAM");
                    _currentProfile.RecommendedCpuPriority = "Normal";
                    _currentProfile.RecommendedGpuPriority = "Normal";
                    break;

                case BottleneckType.VRAM:
                    recommendations.Add("Otimizar uso de VRAM");
                    recommendations.Add("Reduzir qualidade de texturas");
                    recommendations.Add("Ativar preloading inteligente");
                    recommendations.Add("Fechar aplicações que usam GPU");
                    _currentProfile.RecommendedCpuPriority = "Normal";
                    _currentProfile.RecommendedGpuPriority = "High";
                    break;

                case BottleneckType.Balanced:
                    recommendations.Add("Sistema balanceado");
                    recommendations.Add("Aplicar otimizações gerais");
                    recommendations.Add("Focar em redução de stutters");
                    _currentProfile.RecommendedCpuPriority = "High";
                    _currentProfile.RecommendedGpuPriority = "High";
                    break;
            }

            if (_currentProfile.StutterCount > 100)
            {
                recommendations.Add("⚠️ Muitos stutters detectados");
                recommendations.Add("Ativar predição de stutters");
                recommendations.Add("Ativar VRAM preloading");
            }

            _currentProfile.Recommendations = recommendations;
        }

        public GameLearningProfile? GetProfile(string gameExecutable)
        {
            string gameId = GetGameId(gameExecutable);
            return _profiles.ContainsKey(gameId) ? _profiles[gameId] : null;
        }

        public bool HasLearnedProfile(string gameExecutable)
        {
            string gameId = GetGameId(gameExecutable);
            return _profiles.ContainsKey(gameId) && _profiles[gameId].SessionCount >= 3;
        }

        private string GetGameId(string executable)
        {
            return Path.GetFileNameWithoutExtension(executable).ToLowerInvariant();
        }

        private void LoadAllProfiles()
        {
            try
            {
                var files = Directory.GetFiles(_profilesPath, "*.json");
                
                foreach (var file in files)
                {
                    try
                    {
                        string json = File.ReadAllText(file);
                        var profile = JsonSerializer.Deserialize<GameLearningProfile>(json);
                        
                        if (profile != null)
                        {
                            _profiles[profile.GameId] = profile;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"[GameLearning] Erro ao carregar perfil {file}: {ex.Message}");
                    }
                }

                _logger.LogInfo($"[GameLearning] ✅ {_profiles.Count} perfis carregados");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GameLearning] Erro ao carregar perfis: {ex.Message}");
            }
        }

        private async Task SaveProfileAsync(GameLearningProfile profile)
        {
            try
            {
                string filePath = Path.Combine(_profilesPath, $"{profile.GameId}.json");
                var options = new JsonSerializerOptions { WriteIndented = true,
                    ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
                };
                string json = JsonSerializer.Serialize(profile, options);
                await File.WriteAllTextAsync(filePath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GameLearning] Erro ao salvar perfil: {ex.Message}");
            }
        }

        // Detectar e iniciar aprendizado do CS2 automaticamente
        public void DetectAndStartCounterStrikeLearning()
        {
            try
            {
                // Verificar se já está aprendendo CS2
                if (_currentProfile?.GameName?.Contains("Counter-Strike", StringComparison.OrdinalIgnoreCase) == true)
                {
                    _logger.LogInfo("[GameLearning] CS2 já está em aprendizado ativo");
                    return;
                }
                
                // Detectar processo CS2
                string[] cs2Names = { "cs2", "counterstrike2", "csgo" };
                
                foreach (string name in cs2Names)
                {
                    var processes = Process.GetProcessesByName(name);
                    foreach (var process in processes)
                    {
                        try
                        {
                            // Verificar se é realmente o jogo (não launcher)
                            if (process.WorkingSet64 > 500 * 1024 * 1024) // > 500MB
                            {
                                _logger.LogSuccess("[GameLearning] CS2 detectado! Iniciando aprendizado automático");
                                StartLearningSession(process.ProcessName, "Counter-Strike 2");
                                return;
                            }
                        }
                        finally
                        {
                            process.Dispose();
                        }
                    }
                }
                
                _logger.LogWarning("[GameLearning] CS2 não encontrado - aprendizado não iniciado");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GameLearning] Erro ao detectar CS2: {ex.Message}");
            }
        }
    }

    public class GameLearningProfile
    {
        public string GameId { get; set; } = "";
        public string GameName { get; set; } = "";
        public string ExecutableName { get; set; } = "";
        public DateTime FirstSeen { get; set; }
        public DateTime LastPlayed { get; set; }
        public int SessionCount { get; set; }
        public double TotalPlayTimeMinutes { get; set; }
        public long TotalFrames { get; set; }
        public double AvgFps { get; set; }
        public double Fps1PercentLow { get; set; }
        public double Fps01PercentLow { get; set; }
        public double AvgCpuUsage { get; set; }
        public double AvgGpuUsage { get; set; }
        public double AvgRamUsageMb { get; set; }
        public double AvgVramUsageMb { get; set; }
        public double MaxCpuUsage { get; set; }
        public double MaxGpuUsage { get; set; }
        public double MaxRamUsageMb { get; set; }
        public double MaxVramUsageMb { get; set; }
        public BottleneckType PrimaryBottleneck { get; set; }
        public Dictionary<BottleneckType, double> Bottlenecks { get; set; } = new();
        public int StutterCount { get; set; }
        public List<string> Recommendations { get; set; } = new();
        public string RecommendedCpuPriority { get; set; } = "Normal";
        public string RecommendedGpuPriority { get; set; } = "Normal";
    }

    public class PerformanceSnapshot
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public double Fps { get; set; }
        public double FrameTimeMs { get; set; }
        public int FrameCount { get; set; }
        public double CpuUsage { get; set; }
        public double GpuUsage { get; set; }
        public double RamUsageMb { get; set; }
        public double VramUsageMb { get; set; }
        public double CpuTemp { get; set; }
        public double GpuTemp { get; set; }
    }

    public enum BottleneckType
    {
        Unknown,
        CPU,
        GPU,
        RAM,
        VRAM,
        Disk,
        Network,
        Balanced
    }
}