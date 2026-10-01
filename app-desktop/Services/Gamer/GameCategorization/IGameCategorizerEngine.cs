using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.Gamer.GameCategorization.Models;

namespace VoltrisOptimizer.Services.Gamer.GameCategorization
{
    public interface IGameCategorizerEngine
    {
        /// <summary>
        /// Analisa um executável de jogo e retorna seu perfil (Categoria e Engine)
        /// com base em heurísticas avançadas de varredura de diretórios e nomenclatura.
        /// </summary>
        Task<GameProfileAnalysis> AnalyzeGameAsync(string executablePath, CancellationToken cancellationToken = default);
    }
}
