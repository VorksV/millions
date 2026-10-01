using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Gamer.Interfaces;
using VoltrisOptimizer.Services.Gamer.Models;

namespace VoltrisOptimizer.Services
{
    /// <summary>
    /// Serviço profissional e inteligente de detecção de jogos.
    ///
    /// ARQUITETURA (event-driven):
    ///   Usa WMI Win32_ProcessStartTrace para ser notificado quando qualquer processo
    ///   inicia — zero CPU quando nenhum processo está abrindo.
    ///   Fallback: polling leve a cada 10s apenas para detectar jogos já em execução
    ///   no momento em que o serviço é iniciado.
    /// </summary>
    public class GameDetectionService : IGameDetector, IDisposable
    {
        private readonly ILoggingService? _logger;
        private readonly HashSet<int> _detectedGameProcessIds = new HashSet<int>();
        private readonly object _lockObject = new object();
        private bool _isMonitoring = false;

        // WMI EventWatcher — notificado pelo SO quando processo inicia/termina
        private ManagementEventWatcher? _startWatcher;
        private ManagementEventWatcher? _stopWatcher;

        // Fallback polling para processos já em execução no startup
        private CancellationTokenSource? _fallbackCts;
        private Task? _fallbackTask;

        // Lista manual de processos de jogos (case-insensitive)
        private readonly HashSet<string> _manualGameProcessList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly string _manualGamesListPath;

        /// <summary>
        /// Evento disparado quando um jogo é detectado/iniciado
        /// </summary>
        public event EventHandler<VoltrisOptimizer.Services.Gamer.Models.DetectedGame>? GameStarted;

        /// <summary>
        /// Evento disparado quando um jogo é encerrado
        /// </summary>
        public event EventHandler<VoltrisOptimizer.Services.Gamer.Models.DetectedGame>? GameStopped;

        /// <summary>
        /// Evento disparado quando um jogo é detectado via método híbrido
        /// </summary>
        public event EventHandler<GameDetectedEventArgs>? OnGameStarted;

        /// <summary>
        /// Evento disparado quando um jogo é encerrado
        /// </summary>
        public event EventHandler<GameStoppedEventArgs>? OnGameStopped;

        /// <summary>
        /// Evento de progresso da detecção
        /// </summary>
        public event EventHandler<GameDetectionProgress>? ProgressChanged;

        public GameDetectionService(ILoggingService? logger = null)
        {
            _logger = logger;
            
            // Caminho para o arquivo de lista manual de jogos
            var gamesDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Games");
            Directory.CreateDirectory(gamesDirectory);
            _manualGamesListPath = Path.Combine(gamesDirectory, "manual_games_list.json");
            
            // Carregar lista manual de jogos
            LoadManualGamesList();
            
            // Adicionar alguns jogos conhecidos por padrão
            InitializeDefaultGames();
        }
        
        /// <summary>
        /// Carrega a lista manual de jogos do arquivo JSON
        /// </summary>
        private void LoadManualGamesList()
        {
            try
            {
                if (File.Exists(_manualGamesListPath))
                {
                    var json = File.ReadAllText(_manualGamesListPath);
                    var gameList = JsonSerializer.Deserialize<List<string>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                    if (gameList != null)
                    {
                        foreach (var gameName in gameList)
                        {
                            if (!string.IsNullOrWhiteSpace(gameName))
                            {
                                // Adicionar com e sem .exe
                                var name = gameName.Trim();
                                _manualGameProcessList.Add(name);
                                if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                {
                                    _manualGameProcessList.Add(name + ".exe");
                                }
                            }
                        }
                        _logger?.LogInfo($"📋 Lista manual carregada: {_manualGameProcessList.Count} jogos");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"Erro ao carregar lista manual de jogos: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Salva a lista manual de jogos no arquivo JSON
        /// </summary>
        private void SaveManualGamesList()
        {
            try
            {
                // Remover duplicatas (.exe e sem .exe)
                var uniqueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var name in _manualGameProcessList)
                {
                    var cleanName = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) 
                        ? name.Substring(0, name.Length - 4) 
                        : name;
                    uniqueNames.Add(cleanName);
                }
                
                var json = JsonSerializer.Serialize(uniqueNames.ToList(), new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                File.WriteAllText(_manualGamesListPath, json);
                _logger?.LogInfo($"💾 Lista manual salva: {uniqueNames.Count} jogos");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Erro ao salvar lista manual de jogos: {ex.Message}", ex);
            }
        }
        
        /// <summary>
        /// Inicializa alguns jogos conhecidos por padrão
        /// 
        /// ═══════════════════════════════════════════════════════════════
        /// 📝 ONDE ADICIONAR JOGOS MANUALMENTE:
        /// ═══════════════════════════════════════════════════════════════
        /// 
        /// MÉTODO 1 (Recomendado): Editar o arquivo JSON
        ///   Caminho: bin\Debug\net8.0-windows\Games\manual_games_list.json
        ///   Formato: ["main", "hl2", "csgo", "fortnite"]
        ///   Nota: Não precisa incluir .exe, o sistema adiciona automaticamente
        /// 
        /// MÉTODO 2: Adicionar no código abaixo (neste array)
        ///   Adicione o nome do processo (sem .exe) no array defaultGames
        ///   Exemplo: "main", "hl2", "csgo", "fortnite"
        /// 
        /// MÉTODO 3: Usar o método AddManualGame() programaticamente
        ///   App.GameDetectionService?.AddManualGame("nomeDoProcesso");
        /// 
        /// ═══════════════════════════════════════════════════════════════
        /// </summary>
        private void InitializeDefaultGames()
        {
            // ═══════════════════════════════════════════════════════════
            // 📝 ADICIONE SEUS JOGOS AQUI (para detecção em modo janela)
            // ═══════════════════════════════════════════════════════════
            // 
            // Adicione o nome do processo (sem .exe) neste array.
            // O sistema automaticamente adiciona também com .exe.
            // 
            // Exemplos:
            // - "main" para Mu Online (main.exe)
            // - "hl2" para Half-Life 2 (hl2.exe)
            // - "csgo" para CS:GO (csgo.exe)
            // - "fortnite" para Fortnite (FortniteClient-Win64-Shipping.exe)
            //
            // ═══════════════════════════════════════════════════════════
            
            var defaultGames = new[]
            {
                // MMORPGs / Jogos Online (Frequentes em Windowed Mode)
                "main", "Main",             // Mu Online
                "elementclient",            // Perfect World
                "Ragexe",                   // Ragnarok Online
                "Tibia",                    // Tibia
                "Wow", "Wow-64",            // World of Warcraft
                "client",                   // Vários MMOs
                "game",                     // Vários jogos genéricos
                "BlackDesert64",            // Black Desert
                "ArcheAge",                 // ArcheAge
                "ffxiv_dx11",               // Final Fantasy XIV
                "Gw2-64",                   // Guild Wars 2
                "eso64",                    // Elder Scrolls Online
                
                // Shooters / Competitive
                "csgo", "cs2",              // Counter-Strike
                "valorant", "valorant-win64-shipping", // Valorant
                "League of Legends",        // LoL
                "dota2",                    // Dota 2
                "FortniteClient-Win64-Shipping", // Fortnite
                "r5apex",                   // Apex Legends
                "Overwatch",                // Overwatch
                "RainbowSix",               // R6 Siege
                
                // Outros Populares
                "Minecraft.Windows",        // Minecraft Bedrock
                "javaw",                    // Minecraft Java
                "GTA5",
                "GTA5_Enhanced_BE",                     // GTA V
                "RobloxPlayerBeta",         // Roblox
                "RocketLeague",             // Rocket League
            };
            
            bool hasNewGames = false;
            foreach (var game in defaultGames)
            {
                if (!_manualGameProcessList.Contains(game))
                {
                    _manualGameProcessList.Add(game);
                    hasNewGames = true;
                }
            }
            
            if (hasNewGames)
            {
                SaveManualGamesList();
            }
        }
        
        /// <summary>
        /// Adiciona um processo à lista manual de jogos
        /// Use este método para adicionar jogos que rodam em modo janela
        /// </summary>
        public void AddManualGame(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName))
                return;
                
            var name = processName.Trim();
            lock (_lockObject)
            {
                _manualGameProcessList.Add(name);
                if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    _manualGameProcessList.Add(name + ".exe");
                }
            }
            
            SaveManualGamesList();
            _logger?.LogInfo($"➕ Jogo adicionado à lista manual: {name}");
        }
        
        /// <summary>
        /// Remove um processo da lista manual de jogos
        /// </summary>
        public void RemoveManualGame(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName))
                return;
                
            var name = processName.Trim();
            lock (_lockObject)
            {
                _manualGameProcessList.Remove(name);
                _manualGameProcessList.Remove(name + ".exe");
                _manualGameProcessList.Remove(name.Replace(".exe", ""));
            }
            
            SaveManualGamesList();
            _logger?.LogInfo($"➖ Jogo removido da lista manual: {name}");
        }
        
        /// <summary>
        /// Retorna a lista atual de jogos manuais
        /// </summary>
        public List<string> GetManualGamesList()
        {
            lock (_lockObject)
            {
                // Remover duplicatas (.exe e sem .exe)
                var uniqueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var name in _manualGameProcessList)
                {
                    var cleanName = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) 
                        ? name.Substring(0, name.Length - 4) 
                        : name;
                    uniqueNames.Add(cleanName);
                }
                return uniqueNames.ToList();
            }
        }

        /// <summary>
        /// Inicia o monitoramento em segundo plano
        /// </summary>
        public void StartMonitoring()
        {
            lock (_lockObject)
            {
                if (_isMonitoring)
                {
                    _logger?.LogWarning("⚠️ GameDetectionService já está monitorando!");
                    return;
                }

                _isMonitoring = true;

                // ── ARQUITETURA EVENT-DRIVEN ─────────────────────────────────────────
                // Em vez de um loop de polling que acorda a cada N segundos e varre
                // todos os processos, usamos WMI EventWatcher:
                //   - Win32_ProcessStartTrace  → SO notifica quando processo inicia
                //   - Win32_ProcessStopTrace   → SO notifica quando processo termina
                // CPU em idle: 0%. O thread só acorda quando algo realmente acontece.
                // ────────────────────────────────────────────────────────────────────
                bool wmiOk = TryStartWmiWatchers();

                if (wmiOk)
                {
                    _logger?.LogInfo("🎮 GameDetectionService iniciado — modo EVENT-DRIVEN (WMI). CPU idle: ~0%.");
                }
                else
                {
                    // Fallback: polling leve apenas se WMI não estiver disponível
                    // (ambientes sem privilégios de admin ou WMI desabilitado)
                    _fallbackCts = new CancellationTokenSource();
                    _fallbackTask = Task.Run(() => FallbackPollingAsync(_fallbackCts.Token), _fallbackCts.Token);
                    _logger?.LogWarning("🎮 GameDetectionService iniciado — modo FALLBACK (polling 15s). WMI indisponível.");
                }

                // Scan único dos processos já em execução no momento do startup.
                // Necessário porque o WMI só notifica processos que iniciam APÓS o watcher.
                _ = Task.Run(ScanRunningProcessesOnce);
            }
        }

        private bool TryStartWmiWatchers()
        {
            try
            {
                // Watcher de início de processo
                _startWatcher = new ManagementEventWatcher(
                    new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace"));
                _startWatcher.EventArrived += OnProcessStartedWmi;
                _startWatcher.Start();

                // Watcher de término de processo
                _stopWatcher = new ManagementEventWatcher(
                    new WqlEventQuery("SELECT * FROM Win32_ProcessStopTrace"));
                _stopWatcher.EventArrived += OnProcessStoppedWmi;
                _stopWatcher.Start();

                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[GameDetection] WMI indisponível: {ex.Message}");
                _startWatcher?.Dispose();
                _startWatcher = null;
                _stopWatcher?.Dispose();
                _stopWatcher = null;
                return false;
            }
        }

        private void OnProcessStartedWmi(object sender, EventArrivedEventArgs e)
        {
            try
            {
                var processName = e.NewEvent.Properties["ProcessName"]?.Value?.ToString() ?? "";
                var pid = Convert.ToInt32(e.NewEvent.Properties["ProcessID"]?.Value ?? 0);
                if (pid <= 4 || string.IsNullOrEmpty(processName)) return;

                // Remover .exe para comparação
                var cleanName = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? processName[..^4] : processName;

                // Verificação rápida na lista manual (O(1), sem abrir o processo)
                bool inManualList;
                lock (_lockObject)
                    inManualList = _manualGameProcessList.Contains(cleanName) ||
                                   _manualGameProcessList.Contains(processName);

                if (!inManualList)
                {
                    // Verificação heurística — abre o processo apenas se necessário
                    // Delay pequeno para o processo terminar de inicializar antes de inspecionar
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(800); // aguarda módulos carregarem
                        EvaluateProcessAsGame(pid, cleanName);
                    });
                }
                else
                {
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(800);
                        EvaluateProcessAsGame(pid, cleanName);
                    });
                }
            }
            catch { }
        }

        private void OnProcessStoppedWmi(object sender, EventArrivedEventArgs e)
        {
            try
            {
                var pid = Convert.ToInt32(e.NewEvent.Properties["ProcessID"]?.Value ?? 0);
                bool wasGame;
                lock (_lockObject)
                    wasGame = _detectedGameProcessIds.Remove(pid);

                if (wasGame)
                {
                    var processName = e.NewEvent.Properties["ProcessName"]?.Value?.ToString() ?? "Unknown";
                    _logger?.LogInfo($"🎮 Jogo encerrado: {processName} (PID {pid})");
                    try
                    {
                        GameStopped?.Invoke(this, new DetectedGame
                        {
                            Name = processName,
                            ProcessName = processName,
                            ProcessId = pid,
                            ExecutablePath = "",
                            DetectedAt = DateTime.Now
                        });
                        OnGameStopped?.Invoke(this, new GameStoppedEventArgs { ProcessId = pid, ProcessName = processName });
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void EvaluateProcessAsGame(int pid, string processName)
        {
            try
            {
                lock (_lockObject)
                    if (_detectedGameProcessIds.Contains(pid)) return;

                var process = Process.GetProcessById(pid);
                if (process.HasExited) return;

                int score = ComputeGameScore(process);
                bool detected = score >= 70;

                if (!detected)
                {
                    lock (_lockObject)
                    {
                        detected = (_manualGameProcessList.Contains(processName) ||
                                    _manualGameProcessList.Contains(processName + ".exe"))
                                   && ValidateAsGame(process);
                    }
                }

                if (detected)
                {
                    lock (_lockObject)
                        _detectedGameProcessIds.Add(pid);

                    _logger?.LogInfo($"🎮 Jogo detectado (WMI): {processName} PID={pid} score={score}");
                    OnGameDetected(process, $"WMI score={score}");
                }
            }
            catch (ArgumentException) { } // processo já encerrou
            catch { }
        }

        private void ScanRunningProcessesOnce()
        {
            // Scan único no startup para detectar jogos já em execução.
            // Não é um loop — roda uma vez e termina.
            try
            {
                var processes = Process.GetProcesses();
                foreach (var p in processes)
                {
                    try
                    {
                        if (p.HasExited || p.Id <= 4) continue;
                        int score = ComputeGameScore(p);
                        bool detected = score >= 70;
                        if (!detected)
                        {
                            lock (_lockObject)
                            {
                                detected = (_manualGameProcessList.Contains(p.ProcessName) ||
                                            _manualGameProcessList.Contains(p.ProcessName + ".exe"))
                                           && ValidateAsGame(p);
                            }
                        }
                        if (!detected) continue;

                        lock (_lockObject)
                        {
                            if (_detectedGameProcessIds.Contains(p.Id)) continue;
                            _detectedGameProcessIds.Add(p.Id);
                        }
                        _logger?.LogInfo($"🎮 Jogo já em execução detectado: {p.ProcessName} PID={p.Id} score={score}");
                        OnGameDetected(p, $"Startup scan score={score}");
                    }
                    catch { }
                }
            }
            catch { }
        }

        private async Task FallbackPollingAsync(CancellationToken ct)
        {
            // Polling leve — só ativo quando WMI não está disponível.
            // Intervalo de 15s: suficiente para detectar jogos sem overhead perceptível.
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(30000, ct);
                    ScanRunningProcessesOnce();
                }
                catch (OperationCanceledException) { break; }
                catch { }
            }
        }

        /// <summary>
        /// Diagnóstico avançado para CS2
        /// </summary>
        public void DiagnoseCs2DetectionPublic()
        {
            try
            {
                _logger?.LogInfo("[GameDetectionService] 🔍 Iniciando diagnóstico avançado para CS2...");
                
                // Listar todos os processos que contenham "cs2"
                var allProcs = Process.GetProcesses();
                var cs2Processes = allProcs.Where(p => 
                    p.ProcessName.Contains("cs2", StringComparison.OrdinalIgnoreCase) ||
                    p.ProcessName.Contains("counter", StringComparison.OrdinalIgnoreCase)
                ).ToList();
                
                _logger?.LogInfo($"[GameDetectionService] 🎮 Processos CS2 encontrados: {cs2Processes.Count}");
                foreach (var proc in cs2Processes)
                {
                    try
                    {
                        _logger?.LogInfo($"[GameDetectionService]   - {proc.ProcessName} (PID: {proc.Id})");
                        
                        // Tentar obter o caminho
                        try
                        {
                            var path = proc.MainModule?.FileName;
                            if (!string.IsNullOrEmpty(path))
                            {
                                // Tentar WMI
                                using var searcher = new ManagementObjectSearcher($"SELECT ExecutablePath FROM Win32_Process WHERE ProcessId = {proc.Id}");
                                foreach (ManagementObject obj in searcher.Get())
                                {
                using var __dispose_obj = obj;
                                    path = obj["ExecutablePath"]?.ToString();
                                    if (!string.IsNullOrEmpty(path)) break;
                                }
                            }
                            _logger?.LogInfo($"[GameDetectionService]     Caminho: {path ?? "N/A"}");
                        }
                        catch { }
                    }
                    catch { }
                    finally { proc.Dispose(); }
                }
                
                // Verificar se o Steam está rodando
                var steamProcs = allProcs.Where(p => 
                    p.ProcessName.Equals("steam", StringComparison.OrdinalIgnoreCase)
                ).ToList();
                
                _logger?.LogInfo($"[GameDetectionService] 🎮 Processos Steam encontrados: {steamProcs.Count}");
                foreach (var proc in steamProcs)
                {
                    try
                    {
                        _logger?.LogInfo($"[GameDetectionService]   - Steam (PID: {proc.Id})");
                    }
                    catch { }
                    finally { proc.Dispose(); }
                }
                
                // Descartar processos restantes
                foreach (var proc in allProcs)
                {
                    try { proc.Dispose(); }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[GameDetectionService] Erro no diagnóstico CS2: {ex.Message}");
            }
        }

        /// <summary>
        /// Para o monitoramento
        /// </summary>
        public void StopMonitoring()
        {
            lock (_lockObject)
            {
                if (!_isMonitoring) return;
                _isMonitoring = false;
            }

            // Parar WMI watchers
            try { _startWatcher?.Stop(); _startWatcher?.Dispose(); _startWatcher = null; } catch { }
            try { _stopWatcher?.Stop(); _stopWatcher?.Dispose(); _stopWatcher = null; } catch { }

            // Parar fallback polling se ativo
            _fallbackCts?.Cancel();
            try { _fallbackTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
            _fallbackCts?.Dispose();
            _fallbackCts = null;

            _logger?.LogInfo("🛑 GameDetectionService parado");
        }

        public bool IsMonitoring
        {
            get { lock (_lockObject) { return _isMonitoring; } }
        }

        /// <summary>
        /// Indica se há pelo menos um jogo detectado rodando no momento
        /// </summary>
        /// <summary>
        /// Retorna o jogo principal atualmente em execução, se houver
        /// </summary>
        public VoltrisOptimizer.Services.Gamer.Models.DetectedGame? CurrentRunningGame => GetRunningGames().FirstOrDefault();

        public bool HasActiveRunningGameSession
        {
            get
            {
                lock (_lockObject)
                {
                    return _detectedGameProcessIds.Count > 0;
                }
            }
        }

        /// <summary>
        /// Retorna lista de jogos atualmente em execução
        /// </summary>
        public List<VoltrisOptimizer.Services.Gamer.Models.DetectedGame> GetRunningGames()
        {
            var result = new List<VoltrisOptimizer.Services.Gamer.Models.DetectedGame>();
            
            lock (_lockObject)
            {
                foreach (var pid in _detectedGameProcessIds.ToList())
                {
                    try
                    {
                        var process = System.Diagnostics.Process.GetProcessById(pid);
                        if (!process.HasExited)
                        {
                            result.Add(new VoltrisOptimizer.Services.Gamer.Models.DetectedGame
                            {
                                Name = process.ProcessName,
                                ProcessName = process.ProcessName,
                                ExecutablePath = process.MainModule?.FileName,
                                ProcessId = pid,
                                DetectedAt = DateTime.Now
                            });
                        }
                    }
                    catch
                    {
                        // Processo não existe mais, remover da lista
                        _detectedGameProcessIds.Remove(pid);
                    }
                }
            }
            
            return result;
        }

        /// <summary>
        /// Verifica se um processo é um jogo conhecido
        /// </summary>
        public bool IsKnownGame(string processName, string? executablePath = null)
        {
            if (string.IsNullOrWhiteSpace(processName)) return false;

            // 1. Verificar lista manual
            lock (_lockObject)
            {
                if (_manualGameProcessList.Contains(processName) || 
                    _manualGameProcessList.Contains(processName + ".exe"))
                {
                    return true;
                }
            }

            // 2. Verificar executáveis conhecidos
            var knownExes = new[] { "csgo", "dota2", "valorant", "league of legends", "fortnite", "minecraft", "gta5", "witcher3", "cyberpunk2077", "r5apex" };
            if (knownExes.Any(x => processName.Contains(x, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            // 3. Verificar caminho se disponível
            if (!string.IsNullOrEmpty(executablePath))
            {
                var pathLower = executablePath.ToLowerInvariant();
                if (pathLower.Contains("steamapps") || 
                    pathLower.Contains("epic games") || 
                    pathLower.Contains("riot games") || 
                    pathLower.Contains("ubisoft") ||
                    pathLower.Contains("origin games"))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Detecta jogos instalados no sistema
        /// </summary>
        /// <summary>
        /// Detecta jogos instalados no sistema
        /// </summary>
        public async Task<IReadOnlyList<VoltrisOptimizer.Services.Gamer.Models.DetectedGame>> DetectInstalledGamesAsync(CancellationToken cancellationToken = default)
        {
            return await Task.Run<IReadOnlyList<VoltrisOptimizer.Services.Gamer.Models.DetectedGame>>(() =>
            {
                var games = new List<DetectedGame>();
                try
                {
                    _logger?.LogInfo("[GameDetection] Detectando jogos instalados...");
                    ReportProgress("Iniciando detecção...", 0, 0);

                    // 1. Locais comuns de instalação de jogos
                    var searchPaths = new List<string>();
                    
                    // Adicionar caminhos padrão do Windows
                    searchPaths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam", "steamapps", "common"));
                    searchPaths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steamapps", "common"));
                    searchPaths.Add(@"C:\Riot Games");
                    searchPaths.Add(@"C:\XboxGames");
                    searchPaths.Add(@"C:\Program Files\Epic Games");
                    searchPaths.Add(@"C:\Program Files (x86)\Epic Games");
                    searchPaths.Add(@"C:\Program Files\GOG Galaxy\Games");
                    searchPaths.Add(@"C:\Program Files (x86)\GOG Galaxy\Games");
                    searchPaths.Add(@"C:\Program Files\Ubisoft\Ubisoft Game Launcher\games");
                    searchPaths.Add(@"C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher\games");

                    // DETECÇÃO DINÂMICA DE DRIVES: Buscar bibliotecas de jogos em todos os discos (D:, E:, F:, etc)
                    try 
                    {
                        var drives = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed);
                        foreach (var drive in drives)
                        {
                            if (drive.Name.Equals(@"C:\", StringComparison.OrdinalIgnoreCase)) continue; // Já coberto acima

                            // Steam Libraries em outros discos
                            searchPaths.Add(Path.Combine(drive.Name, "SteamLibrary", "steamapps", "common"));
                            searchPaths.Add(Path.Combine(drive.Name, "Steam", "steamapps", "common"));
                            
                            // Pastas de jogos genéricas
                            searchPaths.Add(Path.Combine(drive.Name, "Games"));
                            searchPaths.Add(Path.Combine(drive.Name, "Jogos"));
                            searchPaths.Add(Path.Combine(drive.Name, "XboxGames"));
                            searchPaths.Add(Path.Combine(drive.Name, "Epic Games"));
                            searchPaths.Add(Path.Combine(drive.Name, "GOG Games"));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning($"[GameDetection] Erro ao detectar drives: {ex.Message}");
                    }

                    int totalPaths = searchPaths.Count;
                    int pathsChecked = 0;
                    
                    foreach (var path in searchPaths)
                    {
                        if (cancellationToken.IsCancellationRequested) break;
                        
                        pathsChecked++;
                        int progress = (int)((pathsChecked / (float)totalPaths) * 85);
                        ReportProgress($"Verificando {path}...", progress, games.Count);

                        if (Directory.Exists(path))
                        {
                            try
                            {
                                var detected = ScanDirectoryForGames(path);
                                foreach (var game in detected)
                                {
                                    if (!games.Any(g => g.ExecutablePath.Equals(game.ExecutablePath, StringComparison.OrdinalIgnoreCase)))
                                    {
                                        games.Add(game);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger?.LogWarning($"[GameDetection] Erro ao escanear {path}: {ex.Message}");
                            }
                        }
                    }

                    // 2. Adicionar jogos da lista manual que estejam instalados (verificar path se possível, ou apenas adicionar como placeholder)
                    // Como a lista manual só tem nomes, não podemos verificar instalação facilmente sem path.
                    // Mas podemos verificar processos em execução agora.

                    ReportProgress("Finalizando detecção...", 90, games.Count);

                    // 3. Detectar jogos rodando agora
                    var running = Process.GetProcesses();
                    foreach (var p in running)
                    {
                        if (cancellationToken.IsCancellationRequested) break;
                        
                        try 
                        {
                            if (IsKnownGame(p.ProcessName) || HasGameCharacteristics(p))
                            {
                                var path = GetProcessPath(p);
                                if (!string.IsNullOrEmpty(path) && !games.Any(g => g.ExecutablePath.Equals(path, StringComparison.OrdinalIgnoreCase)))
                                {
                                    if (ValidateAsGame(p))
                                    {
                                        games.Add(new DetectedGame
                                        {
                                            Name = ExtractGameName(p.ProcessName, path),
                                            ExecutablePath = path,
                                            DetectedAt = DateTime.Now,
                                            Launcher = DetermineLauncher(path)
                                        });
                                    }
                                }
                            }
                        }
                        catch { }
                    }

                    _logger?.LogSuccess($"[GameDetection] {games.Count} jogos detectados no total.");
                    ReportProgress("Concluído", 100, games.Count);
                    
                    return games;
                }
                catch (Exception ex)
                {
                    _logger?.LogError($"Erro ao detectar jogos instalados: {ex.Message}", ex);
                    return new List<DetectedGame>();
                }
            }, cancellationToken);
        }

        private void ReportProgress(string status, int percent, int found)
        {
            ProgressChanged?.Invoke(this, new GameDetectionProgress 
            { 
                Status = status, 
                PercentComplete = percent, 
                GamesFound = found 
            });
        }

        private List<DetectedGame> ScanDirectoryForGames(string directory)
        {
            var games = new List<DetectedGame>();
            try
            {
                // Busca executáveis na raiz de cada subpasta (estrutura típica de jogos)
                var subDirs = Directory.GetDirectories(directory);
                foreach (var gameDir in subDirs)
                {
                    try
                    {
                        var dirName = Path.GetFileName(gameDir);
                        
                        // Ignorar pastas comuns que não são jogos
                        if (dirName.Equals("CommonRedist", StringComparison.OrdinalIgnoreCase) || 
                            dirName.StartsWith(".")) continue;

                        // Procurar executável principal
                        // Estratégia: Maior executável ou executável com nome parecido com a pasta
                        var exeFiles = Directory.GetFiles(gameDir, "*.exe", SearchOption.TopDirectoryOnly);
                        
                        // Se não achar na raiz, olhar um nível abaixo (ex: Binaries/Win64)
                        if (exeFiles.Length == 0)
                        {
                            exeFiles = Directory.GetFiles(gameDir, "*.exe", SearchOption.AllDirectories)
                                .Where(f => !f.Contains("crash", StringComparison.OrdinalIgnoreCase) && 
                                           !f.Contains("setup", StringComparison.OrdinalIgnoreCase) &&
                                           !f.Contains("uninstall", StringComparison.OrdinalIgnoreCase) &&
                                           !f.Contains("unitycrash", StringComparison.OrdinalIgnoreCase))
                                .Take(20) // Limitar para não demorar demais
                                .ToArray();
                        }

                        if (exeFiles.Length > 0)
                        {
                            // Escolher o melhor candidato
                            var bestExe = exeFiles
                                .OrderByDescending(f => 
                                {
                                    try { return new FileInfo(f).Length; } catch { return 0; }
                                })
                                .FirstOrDefault();

                            if (bestExe != null)
                            {
                                var fileInfo = new FileInfo(bestExe);
                                // Filtro de tamanho mínimo (ex: > 1MB) para evitar launchers pequenos
                                if (fileInfo.Length > 1024 * 1024) 
                                {
                                    games.Add(new DetectedGame
                                    {
                                        Name = dirName, // Nome da pasta geralmente é o nome mais limpo do jogo
                                        ExecutablePath = bestExe,
                                        DetectedAt = DateTime.Now,
                                        Size = fileInfo.Length,
                                        Launcher = DetermineLauncher(bestExe)
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

        private GameLauncher DetermineLauncher(string path)
        {
            if (string.IsNullOrEmpty(path)) return GameLauncher.Unknown;
            var lower = path.ToLowerInvariant();
            if (lower.Contains("steam")) return GameLauncher.Steam;
            if (lower.Contains("epic")) return GameLauncher.EpicGames;
            if (lower.Contains("gog")) return GameLauncher.GOG;
            if (lower.Contains("ubisoft")) return GameLauncher.Ubisoft;
            if (lower.Contains("riot")) return GameLauncher.Riot;
            if (lower.Contains("xbox")) return GameLauncher.Xbox;
            if (lower.Contains("battle.net") || lower.Contains("starcraft") || lower.Contains("warcraft")) return GameLauncher.Blizzard;
            return GameLauncher.Unknown;
        }


        private int ComputeGameScore(Process process)
        {
            int score = 0;
            if (process == null || process.HasExited) return 0;
            var path = GetProcessPath(process)?.ToLowerInvariant() ?? string.Empty;
            if (!string.IsNullOrEmpty(path))
            {
                if (path.Contains("steamapps\\common") || path.Contains("\\epic games\\") || path.Contains("\\gog galaxy\\games\\") || path.Contains("\\riot games\\") || path.Contains("\\ubisoft\\"))
                {
                    score += 50;
                }
                if (path.Contains("\\appdata\\local\\programs\\"))
                {
                    score -= 20;
                }
            }
            bool exclusive = IsExclusiveFullScreen(process);
            bool borderless = IsFullscreenOrBorderless(process);
            if (exclusive) score += 30;
            if (borderless) score += 25;
            int moduleHits = 0;
            int overlayHits = 0;
            try
            {
                foreach (ProcessModule? m in process.Modules)
                {
                    if (m == null) continue;
                    var name = System.IO.Path.GetFileName(m.FileName).ToLowerInvariant();
                    if (name.Contains("d3d11") || name.Contains("d3d12") || name.Contains("dxgi") || name.Contains("vulkan")) moduleHits++;
                    if (name.Contains("unityplayer") || name.Contains("unreal") || name.Contains("cry") || name.Contains("sdl2")) moduleHits++;
                    if (name.Contains("gameoverlayrenderer")) overlayHits++;
                    if (name.Contains("chrome") || name.Contains("libcef") || name.Contains("electron")) score -= 20;
                }
            }
            catch { }
            score += Math.Min(moduleHits * 5, 20);
            score += Math.Min(overlayHits * 10, 20);
            try
            {
                IntPtr hWnd = process.MainWindowHandle;
                if (hWnd == IntPtr.Zero) hWnd = FindMainWindow((uint)process.Id);
                if (hWnd != IntPtr.Zero)
                {
                    var cls = GetWindowClass(hWnd)?.ToLowerInvariant() ?? string.Empty;
                    if (cls.Contains("unity") || cls.Contains("unreal") || cls.Contains("sdl") || cls.Contains("glfw")) score += 15;
                }
            }
            catch { }
            if (!ValidateAsGame(process)) score = 0;
            return Math.Max(0, score);
        }

        /// <summary>
        /// Obtém o processo em primeiro plano (foreground)
        /// </summary>
        private Process? GetForegroundProcess()
        {
            try
            {
                int processId = VoltrisOptimizer.Core.ForegroundWindowTracker.Instance.CurrentPid;
                if (processId <= 0)
                    return null;

                return Process.GetProcessById((int)processId);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Verifica se o processo está em modo Fullscreen Exclusivo
        /// Esta é a técnica mais confiável para detectar jogos
        /// </summary>
        private bool IsExclusiveFullScreen(Process process)
        {
            try
            {
                if (process.HasExited)
                    return false;

                IntPtr hWnd = process.MainWindowHandle;
                if (hWnd == IntPtr.Zero)
                {
                    // Tentar encontrar a janela principal
                    hWnd = FindMainWindow((uint)process.Id);
                    if (hWnd == IntPtr.Zero)
                        return false;
                }

                // Verificar se a janela está visível
                if (!IsWindowVisible(hWnd))
                    return false;

                // Obter dimensões da janela
                if (!GetWindowRect(hWnd, out RECT windowRect))
                    return false;

                // Obter dimensões da tela primária
                int screenWidth = GetSystemMetrics(SM_CXSCREEN);
                int screenHeight = GetSystemMetrics(SM_CYSCREEN);

                // Verificar se a janela cobre toda a tela (com margem mínima)
                int windowWidth = windowRect.Right - windowRect.Left;
                int windowHeight = windowRect.Bottom - windowRect.Top;

                // Fullscreen exclusivo geralmente cobre 99.9% da tela
                bool coversScreen = windowWidth >= screenWidth - 5 && windowHeight >= screenHeight - 5;

                // Verificar estilo da janela (fullscreen exclusivo não tem bordas)
                int style = GetWindowLong(hWnd, GWL_STYLE);
                bool hasNoBorders = (style & WS_BORDER) == 0 && (style & WS_CAPTION) == 0;

                // Verificar se está maximizado
                WINDOWPLACEMENT placement = new WINDOWPLACEMENT();
                placement.length = Marshal.SizeOf(placement);
                bool isMaximized = GetWindowPlacement(hWnd, ref placement) && placement.showCmd == SW_SHOWMAXIMIZED;

                // É fullscreen exclusivo se cobre a tela E não tem bordas
                return coversScreen && hasNoBorders && (isMaximized || windowWidth == screenWidth);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Verifica se o processo está em modo fullscreen ou borderless fullscreen
        /// </summary>
        private bool IsFullscreenOrBorderless(Process process)
        {
            try
            {
                if (process.HasExited)
                    return false;

                IntPtr hWnd = process.MainWindowHandle;
                if (hWnd == IntPtr.Zero)
                {
                    hWnd = FindMainWindow((uint)process.Id);
                    if (hWnd == IntPtr.Zero)
                        return false;
                }

                if (!IsWindowVisible(hWnd))
                    return false;

                if (!GetWindowRect(hWnd, out RECT windowRect))
                    return false;

                int screenWidth = GetSystemMetrics(SM_CXSCREEN);
                int screenHeight = GetSystemMetrics(SM_CYSCREEN);

                int windowWidth = windowRect.Right - windowRect.Left;
                int windowHeight = windowRect.Bottom - windowRect.Top;

                // Verificar se ocupa >= 95% da tela
                double coverageWidth = (double)windowWidth / screenWidth;
                double coverageHeight = (double)windowHeight / screenHeight;

                return coverageWidth >= 0.95 && coverageHeight >= 0.95;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Verifica se o processo tem características de jogo
        /// (DLLs de jogos, uso intensivo de recursos, etc.)
        /// </summary>
        private bool HasGameCharacteristics(Process process)
        {
            try
            {
                if (process.HasExited)
                    return false;

                // Verificar DLLs de jogos
                var gameDLLs = new[]
                {
                    "d3d11.dll", "d3d12.dll", "dxgi.dll", "d3d9.dll",
                    "vulkan-1.dll", "vulkan32.dll", "vulkan64.dll",
                    "steam_api.dll", "steam_api64.dll",
                    "eos_sdk.dll", "battlenet.dll", "riotclient.dll"
                };

                try
                {
                    foreach (ProcessModule? module in process.Modules)
                    {
                        if (module == null) continue;
                        var moduleName = System.IO.Path.GetFileName(module.FileName).ToLowerInvariant();
                        if (gameDLLs.Any(dll => moduleName.Contains(dll, StringComparison.OrdinalIgnoreCase)))
                        {
                            return true;
                        }
                    }
                }
                catch { }

                var processPath = GetProcessPath(process)?.ToLowerInvariant() ?? string.Empty;
                if (!string.IsNullOrEmpty(processPath))
                {
                    if (processPath.Contains("steamapps\\common") ||
                        processPath.Contains("\\epic games\\") ||
                        processPath.Contains("\\gog galaxy\\games\\") ||
                        processPath.Contains("\\riot games\\") ||
                        processPath.Contains("\\ubisoft\\"))
                    {
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Valida se o processo realmente parece ser um jogo (evita falsos positivos)
        /// </summary>
        private bool ValidateAsGame(Process process)
        {
            try
            {
                if (process.HasExited)
                    return false;

                var processName = process.ProcessName.ToLowerInvariant();
                var processPath = GetProcessPath(process);

                // Ignorar processos do sistema
                var systemProcesses = new[]
                {
                    "explorer", "dwm", "winlogon", "csrss", "lsass", "services",
                    "svchost", "chrome", "firefox", "edge", "notepad", "calc",
                    "taskmgr", "cmd", "powershell", "conhost", "runtimebroker"
                };

                if (systemProcesses.Contains(processName))
                    return false;

                // Processos que NÃO são jogos
                // ADICIONADO: Excluso de processos do sistema como TrustedInstaller
                var nonGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "system", "idle", "registry", "fontdrvhost", "dwm",
                    "csrss", "wininit", "winlogon", "services", "lsass",
                    "smss", "runtimebroker", "dllhost", "audiodg", "wlanext",
                    "searchindexer", "sihost", "taskhostw", "backgroundtransferhost",
                    "securityhealthservice", "settingssynchost", "gamebar",
                    "gamebarservices", "nvcontainer", "nvvsvc", "amdow",
                    "voltrisoptimizer", "explorer", "cmd", "powershell", "conhost",
                    "chrome", "firefox", "msedge", "opera", "brave",
                    "discord", "spotify", "steam", "steamwebhelper", "steamservice", "gameoverlayui",
                    "epicgameslauncher", "origin", "upc", "galaxyclient",
                    "battlenet", "riotclient", "nvidia", "amd", "intel",
                    // Processos do sistema que NÃO são jogos
                    "trustedinstaller", "msiexec", "setup", "installer", "wusa", "dism", "sfc",
                    "compattelrunner", "wuauclt", "ctfmon", "ngentask", "ngen", "mrt",
                    "musnotificationux", "werfault", "taskhost", "w32time", "wudfhost",
                    // Instaladores/redistribuíveis que NÃO são jogos
                    "vc_redist.x86", "vc_redist.x64", "vc_redist", "vcredist_x86", "vcredist_x64",
                    "vcredist", "dotnetfx", "ndp", "windowsdesktop", "aspnetcore",
                    "directx", "dxwebsetup", "dxsetup", "oalinst", "openal"
                };
                if (nonGames.Contains(processName))
                    return false;

                // Ignorar se estiver em pastas do sistema
                if (!string.IsNullOrEmpty(processPath))
                {
                    var pathLower = processPath.ToLowerInvariant();
                    if (pathLower.Contains("\\windows\\") || 
                        pathLower.Contains("\\system32\\") || 
                        pathLower.Contains("\\syswow64\\") ||
                        pathLower.Contains("\\program files\\windowsapps\\") ||
                        pathLower.Contains("\\microsoft\\windows\\"))
                    {
                        return false;
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Chamado quando um jogo é detectado
        /// </summary>
        private void OnGameDetected(Process process, string detectionMethod = "Desconhecido")
        {
            try
            {
                lock (_lockObject)
                {
                    if (_detectedGameProcessIds.Contains(process.Id))
                        return;

                    _detectedGameProcessIds.Add(process.Id);
                }

                var processName = process.ProcessName;
                var processPath = GetProcessPath(process);
                var gameName = ExtractGameName(processName, processPath);

                _logger?.LogInfo($"🎮 Jogo detectado: {gameName} (PID: {process.Id})");

                // Disparar evento
                var detectedGame = new DetectedGame
                {
                    Name = gameName,
                    ProcessName = processName,
                    ProcessId = process.Id,
                    ExecutablePath = processPath ?? "",
                    DetectedAt = DateTime.Now,
                    Launcher = DetermineLauncher(processPath ?? "")
                };

                // Disparar eventos para ambos os sistemas (backward compatibility)
                GameStarted?.Invoke(this, detectedGame);
                OnGameStarted?.Invoke(this, new GameDetectedEventArgs
                {
                    Process = process,
                    ProcessName = processName,
                    ProcessId = process.Id,
                    GameName = gameName,
                    ProcessPath = processPath
                });
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Erro ao processar jogo detectado: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Extrai o nome do jogo de forma inteligente
        /// </summary>
        private string ExtractGameName(string processName, string? processPath)
        {
            var lowerName = processName.ToLowerInvariant();
            if (lowerName.EndsWith(".exe")) lowerName = lowerName.Substring(0, lowerName.Length - 4);

            if (VoltrisOptimizer.Services.Gamer.Data.GameSignatureBank.KnownSignatures.TryGetValue(lowerName, out var sig))
            {
                return sig.DisplayName;
            }

            // Tentar extrair do caminho primeiro
            if (!string.IsNullOrEmpty(processPath))
            {
                try
                {
                    // Steam: ...\steamapps\common\NomeDoJogo\executable.exe
                    if (processPath.Contains("steamapps\\common", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = processPath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
                        var commonIndex = Array.FindIndex(parts, p => p.Equals("common", StringComparison.OrdinalIgnoreCase));
                        if (commonIndex >= 0 && commonIndex < parts.Length - 1)
                        {
                            return parts[commonIndex + 1];
                        }
                    }

                    // Tentar pegar do diretório pai
                    var dir = System.IO.Path.GetDirectoryName(processPath);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        var dirName = System.IO.Path.GetFileName(dir);
                        if (!string.IsNullOrEmpty(dirName) && dirName != processName)
                        {
                            return dirName;
                        }
                    }
                }
                catch { }
            }

            // Fallback: formatar nome do processo
            if (processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                processName = processName.Substring(0, processName.Length - 4);
            }

            if (processName.Length > 0)
            {
                return char.ToUpperInvariant(processName[0]) + processName.Substring(1);
            }

            return processName;
        }

        /// <summary>
        /// Obtém caminho do processo de forma segura
        /// </summary>
        private string? GetProcessPath(Process process)
        {
            try
            {
                return process.MainModule?.FileName;
            }
            catch
            {
                try
                {
                    using var searcher = new System.Management.ManagementObjectSearcher(
                        $"SELECT ExecutablePath FROM Win32_Process WHERE ProcessId = {process.Id}");
                    foreach (System.Management.ManagementObject obj in searcher.Get())
                    {
                        return obj["ExecutablePath"]?.ToString();
                    }
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Encontra a janela principal de um processo
        /// </summary>
        private IntPtr FindMainWindow(uint processId)
        {
            IntPtr mainWindow = IntPtr.Zero;
            var windows = new List<IntPtr>();

            EnumWindows((hWnd, lParam) =>
            {
                GetWindowThreadProcessId(hWnd, out uint windowProcessId);
                if (windowProcessId == processId && IsWindowVisible(hWnd))
                {
                    windows.Add(hWnd);
                }
                return true;
            }, IntPtr.Zero);

            // Retornar a maior janela (provavelmente a principal)
            if (windows.Count > 0)
            {
                mainWindow = windows.OrderByDescending(hWnd =>
                {
                    if (GetWindowRect(hWnd, out RECT rect))
                    {
                        return (rect.Right - rect.Left) * (rect.Bottom - rect.Top);
                    }
                    return 0;
                }).First();
            }

return mainWindow;
        }
        
        /// <summary>
        /// Força uma verificação imediata de jogos em execução (bypass throttle)
        /// Útil quando a licença é ativada e precisa detectar jogos já rodando
        /// </summary>
        public async Task ForceScanRunningGamesAsync(CancellationToken cancellationToken = default)
        {
            _logger?.LogInfo("[GameDetectionService] Forçando scan imediato de jogos em execução (license changed)");
            
            // Executar scan leve imediatamente
            await Task.Run(() => ScanRunningProcessesOnce(), cancellationToken);
            
            _logger?.LogInfo($"[GameDetectionService] Scan forçado concluído");
        }

        public void Dispose()
        {
            StopMonitoring();
        }

        #region Win32 API P/Invoke

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private const int GWL_STYLE = -16;
        private const int WS_BORDER = 0x00800000;
        private const int WS_CAPTION = 0x00C00000;
        private const uint SW_SHOWMAXIMIZED = 3;
        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWPLACEMENT
        {
            public int length;
            public int flags;
            public int showCmd;
            public System.Drawing.Point ptMinPosition;
            public System.Drawing.Point ptMaxPosition;
            public RECT rcNormalPosition;
        }

        #endregion

        private string? GetWindowClass(IntPtr hWnd)
        {
            try
            {
                var sb = new System.Text.StringBuilder(256);
                int len = GetClassName(hWnd, sb, sb.Capacity);
                if (len > 0) return sb.ToString();
            }
            catch { }
            return null;
        }
    }

/// <summary>
/// Argumentos do evento de jogo detectado
/// </summary>
public class GameDetectedEventArgs : EventArgs
{
    public Process? Process { get; set; }
    public string ProcessName { get; set; } = "";
    public int ProcessId { get; set; }
    public string GameName { get; set; } = "";
    public string? ProcessPath { get; set; }
}

/// <summary>
/// Argumentos do evento de jogo parado
/// </summary>
public class GameStoppedEventArgs : EventArgs
{
    public string ProcessName { get; set; } = "";
    public int ProcessId { get; set; }
}


}

