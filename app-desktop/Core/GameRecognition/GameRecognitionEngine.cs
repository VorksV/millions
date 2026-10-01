using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using VoltrisOptimizer.Core.GameRecognition;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Core.GameRecognition
{
    public class GameRecognitionEngine : IGameRecognitionEngine
    {
        private readonly GamerOptimizerService _gamer;
        private readonly GameDetectionService _detector;
        private readonly VoltrisOptimizer.Interfaces.IGameProfileRepository _profiles;
        private GameBehaviorAnalyzer? _analyzer;
        private readonly GameSessionManager _session;
        public bool SuppressApplyProfile { get; set; }

        public event EventHandler<GameDetectedEventArgs>? GameDetected;
        public event EventHandler<GamePhaseChangedEventArgs>? GamePhaseChanged;

        public GameRecognitionEngine(GamerOptimizerService gamer, GameDetectionService detector, VoltrisOptimizer.Interfaces.IGameProfileRepository profiles)
        {
            _gamer = gamer;
            _detector = detector;
            _profiles = profiles;
            _session = new GameSessionManager(_gamer);
            _detector.GameStarted += (s, e) =>
            {
                try 
                { 
                    var name = System.IO.Path.GetFileNameWithoutExtension(e.ExecutablePath);
                    var p = Process.GetProcessesByName(name).FirstOrDefault();
                    HandleGameStarted(e.Name, p?.Id ?? 0, e.ExecutablePath, p); 
                }
                catch { }
            };
            _detector.GameStopped += (s, e) =>
            {
                try { HandleGameStopped(e.Name); }
                catch { }
            };
        }

        public void Start()
        {
            try { _detector.StartMonitoring(); } catch { }
        }

        public void Stop()
        {
            try { _detector.StopMonitoring(); } catch { }
        }

        public async Task<bool> ApplyProfileAsync(string gameName)
        {
                    if (!_profiles.Exists(gameName)) _profiles.Save(gameName);
                    try
                    {
                        var engine = FingerprintEngine(gameName);
                        var gp = _gamer.GetGameProfile(gameName);
                        var ok = await _gamer.ActivateGamerModeAsync(gp?.ExecutablePath);
                        if (!ok) return false;
                        ok = await _gamer.ApplyGameProfileAsync(gameName, null);
                        return ok;
                    }
                    catch { return false; }
                }

        public void HandleGameStarted(string gameName, int processId, string? processPath, Process? process)
        {
            try
            {
                GameDetected?.Invoke(this, new GameDetectedEventArgs
                {
                    GameName = gameName,
                    ProcessId = processId,
                    ProcessPath = processPath ?? string.Empty,
                    Process = process
                });
                if (!SuppressApplyProfile) { _ = ApplyProfileAsync(gameName); }
                if (process != null)
                {
                    _analyzer = new GameBehaviorAnalyzer(process);
                    _analyzer.PhaseChanged += async (s2, phase) =>
                    {
                        try
                        {
                            GamePhaseChanged?.Invoke(this, new GamePhaseChangedEventArgs { GameName = gameName, Phase = phase });
                            await _session.OnPhaseAsync(gameName, phase);
                        }
                        catch { }
                    };
                    _analyzer.Start();
                }
            }
            catch { }
        }

        public void HandleGameStopped(string processName)
        {
            try
            {
                _analyzer?.Stop();
                GamePhaseChanged?.Invoke(this, new GamePhaseChangedEventArgs { GameName = processName, Phase = "exit" });
            }
            catch { }
        }

        private string FingerprintEngine(string gameName)
        {
            try
            {
                var proc = Process.GetProcesses().FirstOrDefault(pr => pr.ProcessName.Contains(gameName, StringComparison.OrdinalIgnoreCase));
                if (proc == null) return "unknown";
                foreach (ProcessModule m in proc.Modules)
                {
                    var n = m.ModuleName.ToLowerInvariant();
                    if (n.Contains("unityplayer")) return "unity";
                    if (n.Contains("d3d12") || n.Contains("dxgi")) return "directx";
                    if (n.Contains("vulkan-1")) return "vulkan";
                    if (n.Contains("ue4") || n.Contains("unrealengine")) return "unreal";
                }
            }
            catch { }
            return "unknown";
        }
    }
}
