using System.Threading.Tasks;

namespace VoltrisOptimizer.Core.Intelligence;

public interface IBehaviorScoreEngine
{
	Task SaveAsync();

	Task LoadAsync();
}
