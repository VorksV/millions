using System.Threading.Tasks;

namespace VoltrisOptimizer.Core.Intelligence;

public interface ILearningLogger
{
	Task LogBrainDecisionAsync(string ruleName, double score, string reason, string hardwareHash);

	Task LogPatternEventAsync(string eventType, double severity, string details);

	Task<string> GenerateReportAsync();
}
