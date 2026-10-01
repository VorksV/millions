using System;
using System.Threading.Tasks;
using VoltrisOptimizer.Core.GameRecognition;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Core.GameRecognition
{
    public class GameSessionManager
    {
        private readonly GamerOptimizerService _gamer;
        public GameSessionManager(GamerOptimizerService gamer) { _gamer = gamer; }

        public async Task OnPhaseAsync(string gameName, string phase)
        {
            switch (phase)
            {
                case "load":
                    var profile = _gamer.GetGameProfile(gameName);
                    var exe = profile?.ExecutablePath;
                    await _gamer.ActivateGamerModeAsync(exe);
                    break;
                case "gameplay":
                    await _gamer.ApplyGameProfileAsync(gameName);
                    break;
                case "pause":
                    // Opcional: reduzir agressividade sem desativar totalmente
                    // Mantemos modo gamer ativo para rapidez no retorno
                    break;
                case "exit":
                    await _gamer.DeactivateGamerModeAsync();
                    break;
            }
        }
    }
}
