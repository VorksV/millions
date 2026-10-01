using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Core.Benchmark;

public sealed class BenchmarkDiskTests
{
	public async Task<double> RunDiskBenchmarkAsync(IProgress<string>? progress = null, CancellationToken ct = default(CancellationToken))
	{
		progress?.Report("Disco: Iniciando teste de I/O sequencial...");
		App.LoggingService?.LogInfo("[BenchmarkDisk] Iniciando teste de I/O sequencial (256MB)...");
		return await Task.Run(delegate
		{
			string tempPath = Path.GetTempPath();
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(18, 1);
			defaultInterpolatedStringHandler.AppendLiteral("voltris_bench_");
			defaultInterpolatedStringHandler.AppendFormatted(Guid.NewGuid(), "N");
			defaultInterpolatedStringHandler.AppendLiteral(".tmp");
			string path = Path.Combine(tempPath, defaultInterpolatedStringHandler.ToStringAndClear());
			try
			{
				byte[] array = new byte[268435456];
				new Random(42).NextBytes(array);
				Stopwatch stopwatch = Stopwatch.StartNew();
				File.WriteAllBytes(path, array);
				File.ReadAllBytes(path);
				stopwatch.Stop();
				double num = 3000.0 / stopwatch.Elapsed.TotalMilliseconds;
				ILoggingService? loggingService = App.LoggingService;
				if (loggingService != null)
				{
					defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(57, 2);
					defaultInterpolatedStringHandler.AppendLiteral("[BenchmarkDisk] Teste sequencial concluído em ");
					defaultInterpolatedStringHandler.AppendFormatted(stopwatch.Elapsed.TotalSeconds, "F2");
					defaultInterpolatedStringHandler.AppendLiteral("s — Score: ");
					defaultInterpolatedStringHandler.AppendFormatted(num, "F2");
					loggingService!.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
				}
				return num;
			}
			catch (Exception ex)
			{
				App.LoggingService?.LogError("[BenchmarkDisk] Erro no teste de disco: " + ex.Message, ex);
				throw;
			}
			finally
			{
				try
				{
					if (File.Exists(path))
					{
						File.Delete(path);
					}
				}
				catch
				{
				}
			}
		}, ct);
	}

	public async Task<double> RunRandomReadBenchmarkAsync(IProgress<string>? progress = null, CancellationToken ct = default(CancellationToken))
	{
		progress?.Report("Disco: Teste de leitura aleatória 4K...");
		return await Task.Run(delegate
		{
			string tempPath = Path.GetTempPath();
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(22, 1);
			defaultInterpolatedStringHandler.AppendLiteral("voltris_bench_rnd_");
			defaultInterpolatedStringHandler.AppendFormatted(Guid.NewGuid(), "N");
			defaultInterpolatedStringHandler.AppendLiteral(".tmp");
			string path = Path.Combine(tempPath, defaultInterpolatedStringHandler.ToStringAndClear());
			try
			{
				byte[] array = new byte[67108864];
				new Random(42).NextBytes(array);
				File.WriteAllBytes(path, array);
				Random random = new Random(42);
				byte[] array2 = new byte[4096];
				int num = 10000;
				Stopwatch stopwatch = Stopwatch.StartNew();
				using FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None, 4096, FileOptions.RandomAccess);
				for (int i = 0; i < num; i++)
				{
					ct.ThrowIfCancellationRequested();
					long offset = random.NextInt64(0L, array.Length - 4096);
					fileStream.Seek(offset, SeekOrigin.Begin);
					fileStream.Read(array2, 0, array2.Length);
				}
				stopwatch.Stop();
				return 1500.0 / stopwatch.Elapsed.TotalMilliseconds;
			}
			finally
			{
				try
				{
					if (File.Exists(path))
					{
						File.Delete(path);
					}
				}
				catch
				{
				}
			}
		}, ct);
	}
}
