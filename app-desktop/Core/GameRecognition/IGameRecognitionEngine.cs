using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Core.GameRecognition
{
    public interface IGameRecognitionEngine
    {
        event EventHandler<GameDetectedEventArgs> GameDetected;
        event EventHandler<GamePhaseChangedEventArgs> GamePhaseChanged;
        void Start();
        void Stop();
        Task<bool> ApplyProfileAsync(string gameName);
    }

    public class GameDetectedEventArgs : EventArgs
    {
        public string GameName { get; set; } = string.Empty;
        public int ProcessId { get; set; }
        public string ProcessPath { get; set; } = string.Empty;
        public Process? Process { get; set; }
    }

    public class GamePhaseChangedEventArgs : EventArgs
    {
        public string GameName { get; set; } = string.Empty;
        public string Phase { get; set; } = "unknown"; // load, gameplay, pause, exit
    }
}

