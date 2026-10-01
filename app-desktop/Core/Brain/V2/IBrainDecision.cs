using System.Threading.Tasks;

namespace VoltrisOptimizer.Core.Brain.V2;

public interface IBrainDecision
{
	double Epsilon { get; }

	long Episodes { get; }

	int KnownStates { get; }

	Task<BrainActionV2> DecideAsync(BrainStateKey state, SensorSnapshot snapshot);

	void Learn(BrainStateKey from, BrainActionV2 action, double reward, BrainStateKey to);

	Task LoadAsync();

	Task SaveAsync();
}
