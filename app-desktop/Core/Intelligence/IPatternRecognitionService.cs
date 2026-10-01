using System.Threading.Tasks;

namespace VoltrisOptimizer.Core.Intelligence;

public interface IPatternRecognitionService
{
	Task LoadModelAsync();

	Task TrainModelAsync();

	float PredictSpikeRisk(SystemSpikeFeatures currentFeatures);

	Task RecordEventAsync(SystemSpikeFeatures features, bool wasNegativeSpike);
}
