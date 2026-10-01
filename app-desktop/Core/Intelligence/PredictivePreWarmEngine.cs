using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core.Body;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Core.Intelligence;

public sealed class PredictivePreWarmEngine : IPredictivePreWarmEngine
{
	private readonly ILoggingService _logger;

	private readonly ITemporalPatternEngine _temporal;

	private readonly IVoltrisLegs _legs;

	private readonly IPowerArm _powerArm;

	private bool _isPreWarming;

	private OperationalContext _lastPreWarmContext = OperationalContext.Idle;

	public bool IsPreWarming => _isPreWarming;

	public PredictivePreWarmEngine(ILoggingService logger, ITemporalPatternEngine temporal, IVoltrisLegs legs, IPowerArm powerArm)
	{
		_logger = logger ?? throw new ArgumentNullException("logger");
		_temporal = temporal ?? throw new ArgumentNullException("temporal");
		_legs = legs ?? throw new ArgumentNullException("legs");
		_powerArm = powerArm ?? throw new ArgumentNullException("powerArm");
	}

	public Task StartAsync(CancellationToken ct)
	{
		BackgroundScheduler.Instance.Register("Intelligence.PreWarm", async delegate
		{
			await CheckAndPreWarmAsync();
		}, TimeSpan.FromMinutes(2.0), BackgroundScheduler.TaskPriority.Low, TimeSpan.FromMinutes(1.0));
		_logger.LogInfo("[PRE-WARM] Motor preditivo iniciado via BackgroundScheduler.");
		return Task.CompletedTask;
	}

	public Task StopAsync()
	{
		BackgroundScheduler.Instance.Unregister("Intelligence.PreWarm");
		_isPreWarming = false;
		return Task.CompletedTask;
	}

	public async Task CheckAndPreWarmAsync()
	{
		try
		{
			OperationalContext currentCtx = _legs.DetectedContext;
			ILoggingService logger = _logger;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(49, 2);
			defaultInterpolatedStringHandler.AppendLiteral("[PRE-WARM] Check: contexto_atual=");
			defaultInterpolatedStringHandler.AppendFormatted(currentCtx);
			defaultInterpolatedStringHandler.AppendLiteral(" | isPreWarming=");
			defaultInterpolatedStringHandler.AppendFormatted(_isPreWarming);
			logger.LogDebug(defaultInterpolatedStringHandler.ToStringAndClear());
			if (currentCtx == OperationalContext.Gaming || currentCtx == OperationalContext.SystemStress)
			{
				if (_isPreWarming)
				{
					_logger.LogDebug("[PRE-WARM] Sistema já está em carga real. Finalizando estado de pre-warm.");
					_isPreWarming = false;
				}
				return;
			}
			DateTime futureTime = DateTime.Now.AddMinutes(10.0);
			OperationalContext predictedCtx = _temporal.GetLikelyContext(futureTime);
			double prob = _temporal.GetProbability(futureTime.DayOfWeek, futureTime.Hour, predictedCtx);
			ILoggingService logger2 = _logger;
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(62, 3);
			defaultInterpolatedStringHandler.AppendLiteral("[PRE-WARM] Predição: futuro=");
			defaultInterpolatedStringHandler.AppendFormatted(futureTime, "HH:mm");
			defaultInterpolatedStringHandler.AppendLiteral(" | contexto_previsto=");
			defaultInterpolatedStringHandler.AppendFormatted(predictedCtx);
			defaultInterpolatedStringHandler.AppendLiteral(" | confiança=");
			defaultInterpolatedStringHandler.AppendFormatted(prob, "P1");
			logger2.LogDebug(defaultInterpolatedStringHandler.ToStringAndClear());
			if (prob >= 0.55 && predictedCtx != currentCtx)
			{
				ILoggingService logger3 = _logger;
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(54, 2);
				defaultInterpolatedStringHandler.AppendLiteral("[PRE-WARM] Decisão: ATIVAR pre-warm para ");
				defaultInterpolatedStringHandler.AppendFormatted(predictedCtx);
				defaultInterpolatedStringHandler.AppendLiteral(" (confiança=");
				defaultInterpolatedStringHandler.AppendFormatted(prob, "P1");
				defaultInterpolatedStringHandler.AppendLiteral(")");
				logger3.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
				await ApplyPreWarmAsync(predictedCtx, prob);
			}
			else if (_isPreWarming)
			{
				_isPreWarming = false;
				_lastPreWarmContext = OperationalContext.Idle;
				await _powerArm.SetEppAsync(100);
				ILoggingService logger4 = _logger;
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(100, 2);
				defaultInterpolatedStringHandler.AppendLiteral("[PRE-WARM] Pre-warm cancelado: confiança caiu para ");
				defaultInterpolatedStringHandler.AppendFormatted(prob, "P1");
				defaultInterpolatedStringHandler.AppendLiteral(" ou contexto mudou para ");
				defaultInterpolatedStringHandler.AppendFormatted(predictedCtx);
				defaultInterpolatedStringHandler.AppendLiteral(". EPP revertido para 100.");
				logger4.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
			}
		}
		catch (Exception ex2)
		{
			Exception ex = ex2;
			_logger.LogWarning("[PRE-WARM] Erro durante verificação: " + ex.Message);
		}
	}

	private async Task ApplyPreWarmAsync(OperationalContext target, double confidence)
	{
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler;
		if (_lastPreWarmContext == target && _isPreWarming)
		{
			ILoggingService logger = _logger;
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(53, 1);
			defaultInterpolatedStringHandler.AppendLiteral("[PRE-WARM] Já em pre-warm para ");
			defaultInterpolatedStringHandler.AppendFormatted(target);
			defaultInterpolatedStringHandler.AppendLiteral(", ignorando duplicata.");
			logger.LogDebug(defaultInterpolatedStringHandler.ToStringAndClear());
			return;
		}
		ILoggingService logger2 = _logger;
		defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(70, 2);
		defaultInterpolatedStringHandler.AppendLiteral("[PRE-WARM] ⚡ ATIVANDO: confiança=");
		defaultInterpolatedStringHandler.AppendFormatted(confidence, "P0");
		defaultInterpolatedStringHandler.AppendLiteral(" | transição prevista para ");
		defaultInterpolatedStringHandler.AppendFormatted(target);
		defaultInterpolatedStringHandler.AppendLiteral(" em ~10min");
		logger2.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
		_isPreWarming = true;
		_lastPreWarmContext = target;
		switch (target)
		{
		case OperationalContext.Gaming:
		{
			EppChangeResult eppResult = await _powerArm.SetEppAsync(50);
			_logger.LogSuccess("[PRE-WARM] Gaming Preparation: EPP=50 | powercfg result: " + ((eppResult?.Executed ?? false) ? "OK" : "bloqueado/ignorado"));
			return;
		}
		case OperationalContext.Work:
		case OperationalContext.VideoEditing:
		{
			EppChangeResult eppResult2 = await _powerArm.SetEppAsync(70);
			_logger.LogSuccess("[PRE-WARM] Work Preparation: EPP=70 | powercfg result: " + ((eppResult2?.Executed ?? false) ? "OK" : "bloqueado/ignorado"));
			return;
		}
		}
		ILoggingService logger3 = _logger;
		defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(53, 1);
		defaultInterpolatedStringHandler.AppendLiteral("[PRE-WARM] Nenhuma preparação definida para contexto ");
		defaultInterpolatedStringHandler.AppendFormatted(target);
		logger3.LogDebug(defaultInterpolatedStringHandler.ToStringAndClear());
	}
}
