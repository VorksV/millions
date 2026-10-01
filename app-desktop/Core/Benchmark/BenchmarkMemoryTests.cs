using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Core.Benchmark;

public sealed class BenchmarkMemoryTests
{
	public async Task<double> RunMemoryBenchmarkAsync(IProgress<string>? progress = null, CancellationToken ct = default(CancellationToken))
	{
		progress?.Report("Memória: Iniciando teste de acesso sequencial...");
		App.LoggingService?.LogInfo("[BenchmarkMemory] Iniciando teste de acesso sequencial (512MB)...");
		return await Task.Run(delegate
		{
			byte[] array = new byte[536870912];
			Stopwatch stopwatch = Stopwatch.StartNew();
			for (int i = 0; i < array.Length; i += 4096)
			{
				ct.ThrowIfCancellationRequested();
				array[i]++;
			}
			stopwatch.Stop();
			double num = 5000.0 / stopwatch.Elapsed.TotalMilliseconds;
			ILoggingService? loggingService = App.LoggingService;
			if (loggingService != null)
			{
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(59, 2);
				defaultInterpolatedStringHandler.AppendLiteral("[BenchmarkMemory] Teste sequencial concluído em ");
				defaultInterpolatedStringHandler.AppendFormatted(stopwatch.Elapsed.TotalSeconds, "F2");
				defaultInterpolatedStringHandler.AppendLiteral("s — Score: ");
				defaultInterpolatedStringHandler.AppendFormatted(num, "F2");
				loggingService!.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
			}
			return num;
		}, ct);
	}

	public async Task<double> RunMemoryLatencyBenchmarkAsync(IProgress<string>? progress = null, CancellationToken ct = default(CancellationToken))
	{
		progress?.Report("Memória: Teste de latência aleatória...");
		return await Task.Run(delegate
		{
			byte[] array = new byte[67108864];
			Random random = new Random(42);
			int num = 1000000;
			Stopwatch stopwatch = Stopwatch.StartNew();
			for (int i = 0; i < num; i++)
			{
				ct.ThrowIfCancellationRequested();
				int num2 = random.Next(0, 67108864);
				array[num2]++;
			}
			stopwatch.Stop();
			return 2000.0 / stopwatch.Elapsed.TotalMilliseconds;
		}, ct);
	}
}
