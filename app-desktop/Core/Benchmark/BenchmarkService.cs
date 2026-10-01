using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Core.Benchmark;

public sealed class BenchmarkService
{
	private readonly BenchmarkCpuTests _cpuTests = new BenchmarkCpuTests();

	private readonly BenchmarkMemoryTests _memoryTests = new BenchmarkMemoryTests();

	private readonly BenchmarkDiskTests _diskTests = new BenchmarkDiskTests();

	private readonly BenchmarkSchedulerTests _schedulerTests = new BenchmarkSchedulerTests();

	private readonly BenchmarkScoreCalculator _scoreCalculator = new BenchmarkScoreCalculator();

	private readonly BenchmarkHistoryStore _historyStore = new BenchmarkHistoryStore();

	public BenchmarkHistoryStore HistoryStore => _historyStore;

	public async Task<BenchmarkFullResult> RunFullBenchmarkAsync(string label, IProgress<(string Message, double Percent)>? progress = null, CancellationToken ct = default(CancellationToken))
	{
		IProgress<(string Message, double Percent)> progress2 = progress;
		BenchmarkFullResult result = new BenchmarkFullResult
		{
			Label = label
		};
		progress2 = ProgressBridge.WrapExisting("Executando benchmark...", progress2);
		Progress<string> msgProgress = new Progress<string>(delegate(string msg)
		{
			progress2?.Report((msg, 0.0));
		});
		App.LoggingService?.LogInfo("[BenchmarkService] RunFullBenchmarkAsync iniciado — Label: " + label);
		Stopwatch totalSw = Stopwatch.StartNew();
		progress2?.Report(("Executando benchmark de CPU...", 5.0));
		BenchmarkFullResult benchmarkFullResult = result;
		benchmarkFullResult.CpuScore = await _cpuTests.RunCpuBenchmarkAsync(msgProgress, ct);
		ILoggingService? loggingService = App.LoggingService;
		if (loggingService != null)
		{
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(30, 1);
			defaultInterpolatedStringHandler.AppendLiteral("[BenchmarkService] CPU Score: ");
			defaultInterpolatedStringHandler.AppendFormatted(result.CpuScore, "F2");
			loggingService!.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
		}
		IProgress<(string Message, double Percent)> progress3 = progress2;
		if (progress3 != null)
		{
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(11, 1);
			defaultInterpolatedStringHandler.AppendLiteral("CPU Score: ");
			defaultInterpolatedStringHandler.AppendFormatted(result.CpuScore, "F2");
			progress3.Report((defaultInterpolatedStringHandler.ToStringAndClear(), 20.0));
		}
		progress2?.Report(("Detectando throttling de CPU...", 25.0));
		(bool IsThrottling, double MinFreqPercent, double MaxFreqPercent) tuple = await _cpuTests.DetectThrottlingAsync(msgProgress, ct);
		bool throttling = tuple.IsThrottling;
		double minFreq = tuple.MinFreqPercent;
		double maxFreq = tuple.MaxFreqPercent;
		result.ThrottlingDetected = throttling;
		result.MinCpuFreqPercent = minFreq;
		result.MaxCpuFreqPercent = maxFreq;
		ILoggingService? loggingService2 = App.LoggingService;
		if (loggingService2 != null)
		{
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(48, 3);
			defaultInterpolatedStringHandler.AppendLiteral("[BenchmarkService] Throttling: ");
			defaultInterpolatedStringHandler.AppendFormatted(throttling ? "SIM" : "NÃO");
			defaultInterpolatedStringHandler.AppendLiteral(" (Min: ");
			defaultInterpolatedStringHandler.AppendFormatted(minFreq, "F0");
			defaultInterpolatedStringHandler.AppendLiteral("%, Max: ");
			defaultInterpolatedStringHandler.AppendFormatted(maxFreq, "F0");
			defaultInterpolatedStringHandler.AppendLiteral("%)");
			loggingService2!.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
		}
		progress2?.Report(("Throttling: " + (throttling ? "Detectado" : "Não detectado"), 30.0));
		progress2?.Report(("Executando benchmark de memória...", 35.0));
		BenchmarkFullResult benchmarkFullResult2 = result;
		benchmarkFullResult2.MemoryScore = await _memoryTests.RunMemoryBenchmarkAsync(msgProgress, ct);
		ILoggingService? loggingService3 = App.LoggingService;
		if (loggingService3 != null)
		{
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(33, 1);
			defaultInterpolatedStringHandler.AppendLiteral("[BenchmarkService] Memory Score: ");
			defaultInterpolatedStringHandler.AppendFormatted(result.MemoryScore, "F2");
			loggingService3!.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
		}
		IProgress<(string Message, double Percent)> progress4 = progress2;
		if (progress4 != null)
		{
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(14, 1);
			defaultInterpolatedStringHandler.AppendLiteral("Memory Score: ");
			defaultInterpolatedStringHandler.AppendFormatted(result.MemoryScore, "F2");
			progress4.Report((defaultInterpolatedStringHandler.ToStringAndClear(), 50.0));
		}
		progress2?.Report(("Executando benchmark de disco...", 55.0));
		BenchmarkFullResult benchmarkFullResult3 = result;
		benchmarkFullResult3.DiskScore = await _diskTests.RunDiskBenchmarkAsync(msgProgress, ct);
		ILoggingService? loggingService4 = App.LoggingService;
		if (loggingService4 != null)
		{
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(31, 1);
			defaultInterpolatedStringHandler.AppendLiteral("[BenchmarkService] Disk Score: ");
			defaultInterpolatedStringHandler.AppendFormatted(result.DiskScore, "F2");
			loggingService4!.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
		}
		IProgress<(string Message, double Percent)> progress5 = progress2;
		if (progress5 != null)
		{
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(12, 1);
			defaultInterpolatedStringHandler.AppendLiteral("Disk Score: ");
			defaultInterpolatedStringHandler.AppendFormatted(result.DiskScore, "F2");
			progress5.Report((defaultInterpolatedStringHandler.ToStringAndClear(), 70.0));
		}
		progress2?.Report(("Medindo latência do scheduler...", 75.0));
		(double Score, double AvgLatencyUs) tuple2 = await _schedulerTests.RunSchedulerBenchmarkAsync(msgProgress, ct);
		double schedScore = tuple2.Score;
		double schedLatency = tuple2.AvgLatencyUs;
		result.SchedulerScore = schedScore;
		result.SchedulerLatencyUs = schedLatency;
		ILoggingService? loggingService5 = App.LoggingService;
		if (loggingService5 != null)
		{
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(51, 2);
			defaultInterpolatedStringHandler.AppendLiteral("[BenchmarkService] Scheduler Score: ");
			defaultInterpolatedStringHandler.AppendFormatted(schedScore, "F2");
			defaultInterpolatedStringHandler.AppendLiteral(" (Latência: ");
			defaultInterpolatedStringHandler.AppendFormatted(schedLatency, "F1");
			defaultInterpolatedStringHandler.AppendLiteral("µs)");
			loggingService5!.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
		}
		IProgress<(string Message, double Percent)> progress6 = progress2;
		if (progress6 != null)
		{
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(21, 1);
			defaultInterpolatedStringHandler.AppendLiteral("Scheduler Latency: ");
			defaultInterpolatedStringHandler.AppendFormatted(schedLatency, "F1");
			defaultInterpolatedStringHandler.AppendLiteral("µs");
			progress6.Report((defaultInterpolatedStringHandler.ToStringAndClear(), 85.0));
		}
		progress2?.Report(("Medindo latência da UI...", 88.0));
		BenchmarkFullResult benchmarkFullResult4 = result;
		benchmarkFullResult4.UiScore = await MeasureUiLatencyAsync(ct);
		result.UiLatencyMs = ((result.UiScore > 0.0) ? (1000.0 / result.UiScore) : 0.0);
		ILoggingService? loggingService6 = App.LoggingService;
		if (loggingService6 != null)
		{
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(44, 2);
			defaultInterpolatedStringHandler.AppendLiteral("[BenchmarkService] UI Score: ");
			defaultInterpolatedStringHandler.AppendFormatted(result.UiScore, "F2");
			defaultInterpolatedStringHandler.AppendLiteral(" (Latência: ");
			defaultInterpolatedStringHandler.AppendFormatted(result.UiLatencyMs, "F1");
			defaultInterpolatedStringHandler.AppendLiteral("ms)");
			loggingService6!.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
		}
		IProgress<(string Message, double Percent)> progress7 = progress2;
		if (progress7 != null)
		{
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(10, 1);
			defaultInterpolatedStringHandler.AppendLiteral("UI Score: ");
			defaultInterpolatedStringHandler.AppendFormatted(result.UiScore, "F2");
			progress7.Report((defaultInterpolatedStringHandler.ToStringAndClear(), 95.0));
		}
		result.OverallScore = _scoreCalculator.Calculate(result.CpuScore, result.MemoryScore, result.DiskScore, result.SchedulerScore, result.UiScore);
		result.Timestamp = DateTime.Now;
		try
		{
			await _historyStore.SaveResultAsync(result);
			App.LoggingService?.LogInfo("[BenchmarkService] Resultado salvo no histórico.");
		}
		catch (Exception ex)
		{
			App.LoggingService?.LogWarning("[BenchmarkService] Falha ao salvar histórico: " + ex.Message);
		}
		totalSw.Stop();
		ILoggingService? loggingService7 = App.LoggingService;
		if (loggingService7 != null)
		{
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(63, 3);
			defaultInterpolatedStringHandler.AppendLiteral("[BenchmarkService] Benchmark '");
			defaultInterpolatedStringHandler.AppendFormatted(label);
			defaultInterpolatedStringHandler.AppendLiteral("' concluído em ");
			defaultInterpolatedStringHandler.AppendFormatted(totalSw.Elapsed.TotalSeconds, "F1");
			defaultInterpolatedStringHandler.AppendLiteral("s — Score Global: ");
			defaultInterpolatedStringHandler.AppendFormatted(result.OverallScore, "F2");
			loggingService7!.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
		}
		IProgress<(string Message, double Percent)> progress8 = progress2;
		if (progress8 != null)
		{
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(35, 1);
			defaultInterpolatedStringHandler.AppendLiteral("Benchmark concluído! Score Global: ");
			defaultInterpolatedStringHandler.AppendFormatted(result.OverallScore, "F2");
			progress8.Report((defaultInterpolatedStringHandler.ToStringAndClear(), 100.0));
		}
		return result;
	}

	private async Task<double> MeasureUiLatencyAsync(CancellationToken ct)
	{
		try
		{
			Stopwatch sw = Stopwatch.StartNew();
			await ((DispatcherObject)Application.Current).Dispatcher.InvokeAsync((Action)delegate
			{
			});
			sw.Stop();
			return (sw.Elapsed.TotalMilliseconds > 0.0) ? (1000.0 / sw.Elapsed.TotalMilliseconds) : 100.0;
		}
		catch
		{
			return 50.0;
		}
	}
}
