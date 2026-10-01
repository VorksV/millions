using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Core.Brain.V2;

public sealed class StutterPreventionModule
{
	private readonly ILoggingService _logger;

	private readonly IBrainExecutor _executor;

	private DateTime _lastFireUtc = DateTime.MinValue;

	private readonly double _thresholdMs;

	private readonly int _targetRefreshRate;

	private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(15.0);

	public int Resolved { get; private set; }

	public StutterPreventionModule(ILoggingService logger, IBrainExecutor executor, double thresholdMs = 15.0, int targetRefreshRate = 60)
	{
		_logger = logger ?? throw new ArgumentNullException("logger");
		_executor = executor ?? throw new ArgumentNullException("executor");
		_thresholdMs = thresholdMs;
		_targetRefreshRate = targetRefreshRate;
	}

	public bool DetectStutter(SensorSnapshot snap)
	{
		// [FIX:STUTTER-SEM-JOGO] NÃO HÁ STUTTER FORA DE UM JOGO.
		//
		// Este método media variação de tempo de quadro, e_media um jogo. A
		// variação que ele mede durante o uso normal do Windows é a variação da
		// SONEGAÇÃO do compositor do Windows — que varia o tempo todo, por
		// design, e não tem nada a ver com desempenho de jogo.
		//
		// O log do usuário mostra o que isso produzia (29/09, 02:14:55):
		//
		//     [BRAIN] Stutter detectado! CPU=42,1% GPU=15,3% FG=explorer
		//     [BRAIN-DECISION] Regra: sysresp = 10 (gaming)
		//
		// `explorer` é o Windows Explorer. O app estava no desktop, sem jogo
		// nenhum, e mesmo assim: registrou stutter, contou na estatística, e
		// disparou uma decisão de otimização de JOGO. O preco disso e real —
		// o app passa a mexer em coisas de jogo quando o usuário está apenas
		// usando o computador, e a estatística de stutter fica corrompida por
		// eventos que nunca foram stutter.
		//
		// A guarda é a verificação de que a decisão faz sentido: uma reação a
		// problema de frame só tem significado se existir um renderizador de
		// quadro em jogo. Sem ela, qualquer oscilação do sistema vira "stutter".
		//
		// O que NÃO muda: a detecção dentro de jogo continua idêntica, com o
		// mesmo limiar adaptativo. A única diferença é que o sinal deixou de
		// ser interpretado onde ele não significa nada.
		if (string.IsNullOrWhiteSpace(snap.ForegroundProcessName))
		{
			return false;
		}

		if (!IsGameForeground(snap.ForegroundProcessName))
		{
			return false;
		}

		double adaptiveThreshold = _thresholdMs;
		if (_targetRefreshRate > 60)
		{
			adaptiveThreshold = Math.Min(_thresholdMs, 1000.0 / _targetRefreshRate * 1.5);
		}
		return snap.FrameTimeVarianceMs > adaptiveThreshold;
	}

	/// <summary>
	/// [FIX:STUTTER-SEM-JOGO] O PROCESSO EM PRIMEIRO PLANO É UM JOGO?
	///
	/// A resposta vem do `GameDetectorService`, que é o componente que JÁ
	/// decide essa pergunta para o resto do app — ele tem a lista de jogos
	/// conhecidos, a biblioteca de executáveis, e uma lista explícita de
	/// processos de sistema que nunca devem ser tratados como jogo.
	///
	/// Reutilizá-lo em vez de criar uma lista nova é deliberate. Duas listas de
	/// "o que é jogo" divergem na primeira atualização, e a divergência aparece
	/// como stutter fantasma justamente no caso mais difícil de diagnosticar:
	/// o app reagindo a algo que ele mesmo decidiu ser jogo.
	///
	/// A resolução é por `ServiceLocator`, com degradação explícita: se o
	/// serviço não estiver disponível, a detecção de stutter é desligada em vez
	/// de voltar ao comportamento problemático de antes. Um sistema de proteção
	/// que não consegue identificar o jogo deve ficar quieto — ele não deve
	/// reagir ao desktop.
	/// </summary>
	private static bool IsGameForeground(string? processName)
	{
		if (string.IsNullOrWhiteSpace(processName))
		{
			return false;
		}

		try
		{
			var detector = VoltrisOptimizer.Core.ServiceLocator.GetService<
				VoltrisOptimizer.Services.Gamer.Interfaces.IGameDetector>();

			if (detector == null)
			{
				return false;
			}

			return detector.IsKnownGame(processName);
		}
		catch (Exception ex)
		{
		 VoltrisOptimizer.App.LoggingService?.LogWarning(
				$"[STUTTER] Nao foi possivel confirmar se '{processName}' e jogo: {ex.Message}. " +
				"Deteccao de stutter desativada neste ciclo.");
			return false;
		}
	}

	public async Task<bool> TriggerAsync(SensorSnapshot snap)
	{
		if (DateTime.UtcNow - _lastFireUtc < Cooldown)
		{
			return false;
		}
		_lastFireUtc = DateTime.UtcNow;
		Stopwatch sw = Stopwatch.StartNew();
		ILoggingService logger = _logger;
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(68, 1);
		defaultInterpolatedStringHandler.AppendLiteral("[STUTTER] Detectado! Variância: ");
		defaultInterpolatedStringHandler.AppendFormatted(snap.FrameTimeVarianceMs, "F1");
		defaultInterpolatedStringHandler.AppendLiteral(" ms iniciando protocolo anti-stutter");
		logger.LogWarning(defaultInterpolatedStringHandler.ToStringAndClear());
		await _executor.ExecuteAsync(new BrainActionV2
		{
			Kind = BrainActionKind.SetEpp,
			Param = 0,
			Reason = "stutter: epp0"
		}, snap);
		await _executor.ExecuteAsync(new BrainActionV2
		{
			Kind = BrainActionKind.SetSystemResponsiveness,
			Param = 10,
			Reason = "stutter: sysresp10"
		}, snap);
		if (snap.ForegroundPid > 4)
		{
			await _executor.ExecuteAsync(new BrainActionV2
			{
				Kind = BrainActionKind.SetForegroundPriority,
				Param = 2,
				TargetPid = snap.ForegroundPid,
				TargetProcessName = snap.ForegroundProcessName,
				Reason = "stutter: fg-high"
			}, snap);
		}
		sw.Stop();
		ILoggingService logger2 = _logger;
		defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(36, 1);
		defaultInterpolatedStringHandler.AppendLiteral("[STUTTER] Protocolo executado em ");
		defaultInterpolatedStringHandler.AppendFormatted(sw.Elapsed.TotalMilliseconds, "F1");
		defaultInterpolatedStringHandler.AppendLiteral(" ms");
		logger2.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
		Resolved++;
		return true;
	}
}
