using System.Collections.Generic;
using System.Linq;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Gamer.Models;

namespace VoltrisOptimizer.Core.GameRecognition
{
    public class GameProfileRepositoryAdapter : IGameProfileRepository
    {
        private readonly GamerOptimizerService _gamer;

        public GameProfileRepositoryAdapter(GamerOptimizerService gamer)
        {
            _gamer = gamer;
        }

        public bool Exists(string gameName)
        {
            return _gamer.GetGameProfile(gameName) != null;
        }

        public void Save(string gameName)
        {
            var existing = _gamer.GetGameProfile(gameName);
            if (existing == null)
            {
                var s = new GameProfileSettings();
                _gamer.CreateGameProfile(gameName, string.Empty, s);
            }
        }

        public IEnumerable<string> List()
        {
            return _gamer.GetGameLibrary().Select(g => g.Name);
        }
    }
}
