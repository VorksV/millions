using Microsoft.ML.Data;

namespace VoltrisOptimizer.Core.Intelligence;

public class SpikePrediction
{
	[ColumnName("PredictedLabel")]
	public bool SpikeOcurred { get; set; }

	[ColumnName("Probability")]
	public float Probability { get; set; }

	[ColumnName("Score")]
	public float Score { get; set; }
}
