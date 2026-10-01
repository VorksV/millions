using System;
using System.Threading.Tasks;
using VoltrisOptimizer.Core.Body;

namespace VoltrisOptimizer.Core.Intelligence;

public interface ITemporalPatternEngine
{
	void RecordObservation(OperationalContext context);

	double GetProbability(DayOfWeek day, int hour, OperationalContext context);

	OperationalContext GetLikelyContext(DateTime time);

	Task SaveAsync();

	Task LoadAsync();
}
