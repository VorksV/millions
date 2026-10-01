using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.VisualBasic.Devices;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Utils;

namespace VoltrisOptimizer.Core.Brain.V2.AntiStutter;

public sealed class AntiStutterProfiler : IDisposable
{
	private readonly ILoggingService _logger;

	private readonly SystemMetricsCache _cache;

	private CancellationTokenSource? _cts;

	private Task? _loop;

	private readonly object _lock = new object();

	private AntiStutterSnapshot? _last;

	/// <summary>
	/// [FIX:PROFILER-ANUNCIA-SE] Marca a primeira coleta, para o laço dizer uma
	/// vez que está vivo. Antes disso o laço era mudo e um defeito nele era
	/// indistinguível de um profiler que simplesmente não existe.
	/// </summary>
	private volatile bool _announced;

	private SafePerformanceCounter[]? _cpuCores;

	private double[] _lastCoreValues = Array.Empty<double>();

	private DateTime _lastCoreUpdate = DateTime.MinValue;

	private static readonly string[] GameSignatures = new string[18]
	{
		"cs2", "csgo", "valorant", "fortnite", "apex", "warzone", "cod", "leagueoflegends", "dota", "rocketleague",
		"overwatch", "r6", "rainbowsix", "pubg", "minecraft", "main", "l2", "l2.bin"
	};

	private static readonly string[] BrowserSignatures = new string[6] { "chrome", "msedge", "edge", "firefox", "opera", "brave" };

	public AntiStutterSnapshot? CurrentSnapshot
	{
		get
		{
			lock (_lock)
			{
				return _last;
			}
		}
	}

	public bool IsRunning => _loop != null && !_loop!.IsCompleted;

	public event EventHandler<AntiStutterSnapshot>? SnapshotProduced;

	public AntiStutterProfiler(ILoggingService logger)
	{
		_logger = logger;
		_cache = SystemMetricsCache.Instance;
		InitializeCoreCounters();
		PrimeCounters();
	}

	public Task StartAsync(CancellationToken ct = default(CancellationToken))
	{
		if (IsRunning)
		{
			return Task.CompletedTask;
		}
		_cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		_loop = Task.Run(() => Loop(_cts!.Token), _cts!.Token);
		return Task.CompletedTask;
	}

	public async Task StopAsync()
	{
		if (!IsRunning)
		{
			return;
		}
		_cts?.Cancel();
		try
		{
			await _loop;
		}
		catch
		{
		}
	}

	private async Task Loop(CancellationToken ct)
	{
		await Task.Delay(5000, ct);
		while (!ct.IsCancellationRequested)
		{
			try
			{
				AntiStutterSnapshot snap = Collect();
				lock (_lock)
				{
					_last = snap;
				}

				// [BRAIN-PONTE] Publica para o Perfil Inteligente.
				//
				// O Perfil decide energia e precisa saber o estado do INSTANTE, e
				// este é o único ponto do projeto onde temperatura, estrangulamento
				// e tomada chegam juntos. Sem esta linha, o Perfil resolve a linha
				// olhando só para o tier da máquina e nunca sabe se ela está no
				// limite agora.
				//
				// A publicação é separada da instância de propósito: o Perfil é
				// estático e pode ser chamado por caminhos em que este profiler
				// nem existe.
				AntiStutterSnapshotHub.Publish(snap);

				// [FIX:PROFILER-ANUNCIA-SE] O LAÇO DO PROFILER ERA 100% SILENCIOSO.
				//
				// Este laço nao escrevia NADA em log. Isso transformou um defeito
				// simples em um mistério: o regime de energia aparecia como
				// "SEM SNAPSHOT" no log do Perfil, e nao havia como saber se o
				// profiler nao estava rodando, se rodava e falhava, ou se a ponte
				// nao recebia. Tres hipoteses, nenhuma distinguivel.
				//
				// A primeira publicacao e a ultima com sao registradas em
				// `LogInfo` de proposito. A segunda vira `LogDebug` para nao
				// encher o log a cada 5 segundos, e porque depois da primeira o
				// sistema ja esta funcionando — se algo parar, o log para de
				// crescer, e isso tambem e sinal.
				//
				// O conteudo importa: sem a temperatura no log, um snapshot
				// "sem leitura" e um snapshot "com leitura ruim" ficariam
				// indistinguiveis.
				if (!_announced)
				{
					_announced = true;
					_logger.LogInfo(
						$"[AntiStutterProfiler] Primeira coleta: cpu={snap.CpuUsageTotal:F1}% " +
						$"| tempCPU={snap.CpuTemperatureC:F1}C tempGPU={snap.GpuTemperatureC:F1}C " +
						$"| throttle={snap.CpuThermalThrottling} | tomada={(snap.IsOnBattery ? "bateria" : "tomada")} " +
						$"| nucleos={snap.CpuUsagePerCore.Length} | frente={snap.ForegroundProcessName} " +
						$"| carga={snap.Workload}");
				}
				else
				{
					_logger.LogDebug(
						$"[AntiStutterProfiler] temp={snap.CpuTemperatureC:F1}C throttle={snap.CpuThermalThrottling}");
				}

				this.SnapshotProduced?.Invoke(this, snap);
				await Task.Delay(5000, ct);
			}
			catch (OperationCanceledException)
			{
				break;
			}
			catch (Exception ex)
			{
				_logger.LogDebug("Profiler error: " + ex.Message);
				await Task.Delay(5000, ct);
			}
		}
	}

	private AntiStutterSnapshot Collect()
	{
		double cpuTotal = _cache.CpuPercent;
		(double[] values, double max) cpuPerCoreSafe = GetCpuPerCoreSafe();
		double[] coreValues = cpuPerCoreSafe.values;
		double maxCore = cpuPerCoreSafe.max;
		long ramAvailableMB = (long)_cache.AvailableRamMb;
		long totalRamMB = GetTotalRamMB();
		double ramUsagePercent = _cache.MemoryUsedPercent;
		int foregroundPid = GetForegroundPid();
		string processName = GetProcessName(foregroundPid);
		return new AntiStutterSnapshot
		{
			CpuUsageTotal = cpuTotal,
			CpuUsagePerCore = coreValues,
			CpuMaxCoreUsage = maxCore,

			// [FIX:TEMPERATURA-NUNCA-LIDA] A TEMPERATURA ESTAVA FIXA EM -1.
			//
			// O snapshot entregava `CpuTemperatureC = -1.0` LITERAL, sem
			// nunca perguntar nada ao cache. E o classificador de regime trata
			// exatamente assim "temperatura menor que zero" como "não lida" — o
			// que é o comportamento correto do lado dele.
			//
			// O resultado era que o regime NUNCA podia sair de `Unknown`, por
			// mais que o profiler rodasse: a telemetria que ele deveria estar
			// medindo simplesmente não era medida. Foi preciso ligar este
			// profiler para o defeito ficar visível — antes dele, ninguém
			// lia nada, então o `-1` não tinha consequência observável.
			//
			// A leitura vem de `SystemMetricsCache`, que já mantém um sensor
			// atualizado por conta própria. Aqui é só uma propriedade — custo
			// zero, e é a MESMA fonte que a UI usa, o que evita duas
			// temperaturas discordando na tela e no log.
			CpuTemperatureC = _cache.CpuTemperature,
			GpuTemperatureC = _cache.GpuTemperature,
			GpuUsagePercent = _cache.GpuUsagePercent,
			RamUsagePercent = ramUsagePercent,
			RamAvailableMB = ramAvailableMB,
			RamTotalMB = totalRamMB,
			PageFileUsageMB = (long)(totalRamMB - (double)ramAvailableMB),
			DiskQueueLength = _cache.DiskUsagePercent,
			DiskReadMBps = 0.0,
			DiskWriteMBps = 0.0,
			DiskIsSsd = true,
			ForegroundPid = foregroundPid,
			ForegroundProcessName = processName,
			Workload = DetectWorkload(processName),
			FrameTimeMs = 0.0,
			FrameTimeVarianceMs = 0.0,
			StutterDetected = false,
			IsOnBattery = (SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline),
			BatteryPercent = (int)(SystemInformation.PowerStatus.BatteryLifePercent * 100f)
		};
	}

	private (double[] values, double max) GetCpuPerCoreSafe()
	{
		if (_cpuCores == null)
		{
			return (new double[1] { _cache.CpuPercent }, 0.0);
		}
		if ((DateTime.UtcNow - _lastCoreUpdate).TotalSeconds < 1.0)
		{
			return (_lastCoreValues, (_lastCoreValues.Length != 0) ? _lastCoreValues.Max() : 0.0);
		}
		double[] array = new double[_cpuCores!.Length];
		for (int i = 0; i < _cpuCores!.Length; i++)
		{
			array[i] = _cpuCores[i].NextValue();
		}
		_lastCoreValues = array;
		_lastCoreUpdate = DateTime.UtcNow;
		return (array, array.Max());
	}

	private void InitializeCoreCounters()
	{
		int processorCount = Environment.ProcessorCount;
		_cpuCores = new SafePerformanceCounter[processorCount];
		for (int i = 0; i < processorCount; i++)
		{
			_cpuCores[i] = new SafePerformanceCounter("Processor", "%Processor Time", i.ToString());
		}
	}

	private void PrimeCounters()
	{
		if (_cpuCores != null)
		{
			foreach (var counter in _cpuCores)
			{
				counter.NextValue();
			}
		}
	}

	/// <summary>
	/// [FIX:CPU-IDLE] A MEMÓRIA TOTAL É MEDIDA UMA VEZ, NÃO A CADA CICLO.
	///
	/// Este era o principal custo do coletor, e a medição do próprio consumo
	/// depois que o profiler foi ligado mostrou o preço: o consumo de CPU em
	/// repouso subiu de ~0,5% para mais de 8%.
	///
	/// A causa é `new ComputerInfo()`. Essa classe é apoiada em WMI: cada
	/// construção abre e consulta `Win32_ComputerSystem`, `Win32_Processor` e
	/// `Win32_PhysicalMemory`. Fazer isso a cada 5 segundos é trabalho de WMI
	/// puramente desperdiçado, para um valor que é constante.
	///
	/// Memória total não muda em runtime. Não há troca de módulo em máquina de
	/// consumo, e mesmo que houvesse, o caso seria "reiniciar o app".
	///
	/// A leitura ficou em um campo estático inicializado uma vez, no padrão que
	/// o próprio `SystemMetricsCache` já usa para hardware — o comentário dele
	/// é exatamente este: "hardware/OS: não muda em runtime. Sem este cache,
	/// toda leitura fazia [a consulta de novo]".
	/// </summary>
	private static readonly long TotalRamMbCached = (long)(new ComputerInfo().TotalPhysicalMemory / 1048576uL);

	private static long GetTotalRamMB()
	{
		return TotalRamMbCached;
	}

	private static int GetForegroundPid()
	{
		return ForegroundWindowTracker.Instance.CurrentPid;
	}

	private static string GetProcessName(int pid)
	{
		try
		{
			return Process.GetProcessById(pid).ProcessName;
		}
		catch
		{
			return "";
		}
	}

	private static WorkloadCategory DetectWorkload(string name)
	{
		string name2 = name;
		if (string.IsNullOrWhiteSpace(name2))
		{
			return WorkloadCategory.Idle;
		}
		name2 = name2.ToLowerInvariant();
		if (GameSignatures.Any((string sig) => name2.Contains(sig)))
		{
			return WorkloadCategory.Game;
		}
		if (BrowserSignatures.Any((string sig) => name2.Contains(sig)))
		{
			return WorkloadCategory.Browser;
		}
		return WorkloadCategory.Idle;
	}

	public void Dispose()
	{
		StopAsync();
		if (_cpuCores != null)
		{
			foreach (var counter in _cpuCores)
			{
				counter.Dispose();
			}
		}
		_cts?.Dispose();
	}
}
