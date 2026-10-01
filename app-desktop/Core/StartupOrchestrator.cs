using System.Threading.Tasks;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Core;

public class StartupOrchestrator : IStartupOrchestrator
{
	private readonly ILoggingService _logger;

	public StartupOrchestrator(ILoggingService logger)
	{
		_logger = logger;
	}

	public Task RunAsync()
	{
		_logger.LogInfo("[Startup] Telemetria remota desativada. Orquestração concluída.");
		return Task.CompletedTask;
	}
}
