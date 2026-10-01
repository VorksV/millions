using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Utils;

namespace VoltrisOptimizer.Core.Benchmark;

public sealed class BenchmarkCpuTests
{
	private SafePerformanceCounter? _processorPerformance;

	public async Task<double> RunCpuBenchmarkAsync(IProgress<string>? progress = null, CancellationToken ct = default(CancellationToken))
	{
		progress?.Report("CPU: Iniciando teste de cálculo intensivo...");
		App.LoggingService?.LogInfo("[BenchmarkCPU] Iniciando teste de cálculo intensivo multithread...");
		return await Task.Run(delegate
		{
			Stopwatch stopwatch = Stopwatch.StartNew();
			Parallel.For(0, 100000000, new ParallelOptions
			{
				CancellationToken = ct
			}, delegate(int i)
			{
				double num2 = Math.Sqrt(i);
				Math.Sin(num2);
				Math.Cos(num2);
			});
			stopwatch.Stop();
			double num = 100000.0 / stopwatch.Elapsed.TotalMilliseconds;
			ILoggingService? loggingService = App.LoggingService;
			if (loggingService != null)
			{
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(45, 2);
				defaultInterpolatedStringHandler.AppendLiteral("[BenchmarkCPU] Teste concluído em ");
				defaultInterpolatedStringHandler.AppendFormatted(stopwatch.Elapsed.TotalSeconds, "F2");
				defaultInterpolatedStringHandler.AppendLiteral("s — Score: ");
				defaultInterpolatedStringHandler.AppendFormatted(num, "F2");
				loggingService!.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
			}
			return num;
		}, ct);
	}

	public async Task<(bool IsThrottling, double MinFreqPercent, double MaxFreqPercent)> DetectThrottlingAsync(IProgress<string>? progress = null, CancellationToken ct = default(CancellationToken))
	{
		progress?.Report("CPU: Verificando throttling...");
		CancellationTokenSource cts;
		return await Task.Run(async delegate
		{
			double minFreq = 100.0;
			double maxFreq = 0.0;
			try
			{
				if (_processorPerformance == null)
				{
					_processorPerformance = new SafePerformanceCounter("Processor Information", "% Processor Performance", "_Total");
				}
				_processorPerformance!.NextValue();
				cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
				Task loadTask = Task.Run(delegate
				{
					Parallel.For(0, int.MaxValue, new ParallelOptions
					{
						CancellationToken = cts.Token
					}, delegate(int i)
					{
						double a = Math.Sqrt(i);
						Math.Sin(a);
					});
				}, cts.Token);
				for (int j = 0; j < 10; j++)
				{
					await Task.Delay(500, ct);
					float freq = _processorPerformance!.NextValue();
					if (freq > 0f)
					{
						minFreq = Math.Min(minFreq, freq);
						maxFreq = Math.Max(maxFreq, freq);
					}
				}
				cts.Cancel();
				try
				{
					await loadTask;
				}
				catch (OperationCanceledException)
				{
				}
			}
			catch
			{
				App.LoggingService?.LogWarning("[BenchmarkCPU] Falha ao acessar SafePerformanceCounter para detecção de throttling.");
				return (false, 0.0, 100.0);
			}
			bool isThrottling = maxFreq > 0.0 && (maxFreq - minFreq) / maxFreq > 0.15;
			return (isThrottling, minFreq, maxFreq);
		}, ct);
	}
}
