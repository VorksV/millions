using System.Collections.Generic;

namespace VoltrisOptimizer.Interfaces
{
    public interface IGameProfileRepository
    {
        bool Exists(string gameName);
        void Save(string gameName);
        IEnumerable<string> List();
    }
}
