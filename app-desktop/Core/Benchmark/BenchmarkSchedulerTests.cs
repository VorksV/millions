using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Core.Benchmark;

public sealed class BenchmarkSchedulerTests
{
	public async Task<(double Score, double AvgLatencyUs)> RunSchedulerBenchmarkAsync(IProgress<string>? progress = null, CancellationToken ct = default(CancellationToken))
	{
		progress?.Report("Scheduler: Medindo latência de troca de contexto...");
		App.LoggingService?.LogInfo("[BenchmarkScheduler] Iniciando teste de latência de troca de contexto (500 iterações)...");
		return await Task.Run(async delegate
		{
			List<double> latencies = new List<double>();
			int iterations = 500;
			for (int i = 0; i < iterations; i++)
			{
				ct.ThrowIfCancellationRequested();
				ManualResetEventSlim signal = new ManualResetEventSlim(initialState: false);
				Stopwatch sw = Stopwatch.StartNew();
				Task.Run(delegate
				{
					signal.Set();
				}, ct);
				signal.Wait(ct);
				sw.Stop();
				latencies.Add(sw.Elapsed.TotalMicroseconds);
				signal.Dispose();
			}
			List<double> sorted = latencies.OrderBy((double x) => x).ToList();
			int trim = sorted.Count / 10;
			List<double> trimmed = sorted.Skip(trim).Take(sorted.Count - 2 * trim).ToList();
			double avgLatencyUs = ((trimmed.Count > 0) ? trimmed.Average() : latencies.Average());
			double score2 = ((avgLatencyUs > 0.0) ? (1000.0 / avgLatencyUs) : 100.0);
			score2 = Math.Min(100.0, score2);
			ILoggingService? loggingService = App.LoggingService;
			if (loggingService != null)
			{
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(66, 2);
				defaultInterpolatedStringHandler.AppendLiteral("[BenchmarkScheduler] Teste concluído — Latência média: ");
				defaultInterpolatedStringHandler.AppendFormatted(avgLatencyUs, "F1");
				defaultInterpolatedStringHandler.AppendLiteral("µs, Score: ");
				defaultInterpolatedStringHandler.AppendFormatted(score2, "F2");
				loggingService!.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
			}
			return (score2, avgLatencyUs);
		}, ct);
	}
}
