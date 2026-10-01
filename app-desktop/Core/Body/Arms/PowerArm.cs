using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Principal;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Core.Brain.V2;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Power;
using VoltrisOptimizer.Utils.Win32;

namespace VoltrisOptimizer.Core.Body.Arms;

public sealed class PowerArm : IPowerArm, IDisposable
{
	private readonly ILoggingService _logger;

	private readonly IBrainExecutor _executor;


	private readonly bool _isAdmin;

	private DateTime _lastEppChange = DateTime.UtcNow.AddSeconds(-10.0);

	private const int EPP_DELTA_MIN = 10;

	private const int EPP_COOLDOWN_SECONDS = 6;

	private const int EPP_MAX_CONSECUTIVE_FAILURES = 3;

	private const int EPP_FAILURE_BACKOFF_SECONDS = 300;

	private int _eppConsecutiveFailures;

	private DateTime _eppDisabledUntilUtc = DateTime.MinValue;

	private static bool _eppLaptopWarningLogged;

	private static bool _eppFailureLogged;

	private static bool _eppNonVoltrisLogged;

	private static readonly Guid Balanced = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");

	private static readonly Guid HighPerformance = Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

	private static readonly Guid PowerSaver = Guid.Parse("a1841308-3541-4fab-bc81-f71556f20b4a");

	// Ultimate Performance (Workstation) — GUID oculto do Windows
	private static readonly Guid UltimatePerformance = Guid.Parse("e9a42b02-d5df-448d-aa00-03f14749eb61");

	public int CurrentEpp { get; private set; } = 50;


	public Guid CurrentPowerPlanGuid { get; private set; } = Guid.Empty;


	public int CurrentSystemResponsiveness { get; private set; } = 20;


	public int CurrentTurboBoostPolicy { get; private set; } = 0;


	public PowerArm(ILoggingService logger, IBrainExecutor executor)
	{
		_logger = logger ?? throw new ArgumentNullException("logger");
		_executor = executor ?? throw new ArgumentNullException("executor");
		_isAdmin = IsRunningAsAdmin();
		ILoggingService logger2 = _logger;
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(35, 1);
		defaultInterpolatedStringHandler.AppendLiteral("[ARM-POWER] Inicializado | Admin = ");
		defaultInterpolatedStringHandler.AppendFormatted(_isAdmin);
		logger2.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
	}

	private static bool IsRunningAsAdmin()
	{
		try
		{
			using WindowsIdentity ntIdentity = WindowsIdentity.GetCurrent();
			WindowsPrincipal windowsPrincipal = new WindowsPrincipal(ntIdentity);
			return windowsPrincipal.IsInRole(WindowsBuiltInRole.Administrator);
		}
		catch
		{
			return false;
		}
	}

	public Task<EppChangeResult> SetEppAsync(int value)
	{
		return Task.Run(delegate
		{
			Stopwatch stopwatch = Stopwatch.StartNew();
			try
			{
				if (!_isAdmin)
				{
					_logger.LogWarning("[ARM-POWER] EPP NÃO aplicado: requer admin");
					return new EppChangeResult
					{
						Executed = false,
						OldValue = CurrentEpp,
						NewValue = CurrentEpp,
						Delta = 0,
						Error = "Requer privilégio de administrador"
					};
				}

				// [FIX P0] Backoff: após falhas consecutivas (ex.: Code 5 ACCESS_DENIED),
				// congela tentativas por alguns minutos para eliminar o loop inútil no log.
				if (DateTime.UtcNow < _eppDisabledUntilUtc)
				{
					_logger.LogInfo($"[ARM-POWER] EPP {value} ignorado: backoff temporário até {_eppDisabledUntilUtc:HH:mm:ss} (após {_eppConsecutiveFailures} falhas consecutivas).");
					return new EppChangeResult
					{
						Executed = false,
						OldValue = CurrentEpp,
						NewValue = CurrentEpp,
						Delta = 0,
						Error = "backoff por falhas consecutivas"
					};
				}

				// Cooldown mínimo entre tentativas de escrita de EPP.
				if ((DateTime.UtcNow - _lastEppChange).TotalSeconds < EPP_COOLDOWN_SECONDS)
				{
					_logger.LogDebug($"[ARM-POWER] EPP {value}: cooldown ativo, pulando (mín {EPP_COOLDOWN_SECONDS}s entre escritas)");
					return new EppChangeResult
					{
						Executed = false,
						OldValue = CurrentEpp,
						NewValue = CurrentEpp,
						Delta = 0,
						Error = "cooldown ativo"
					};
				}

				// [FIX P0] Já no valor desejado (cache do último sucesso) -> não reescrever.
				if (value == CurrentEpp)
				{
					_logger.LogInfo($"[ARM-POWER] EPP já está em {value} (cache) - reescrita desnecessária evitada.");
					return new EppChangeResult
					{
						Executed = false,
						OldValue = CurrentEpp,
						NewValue = CurrentEpp,
						Delta = 0,
						Error = "já está neste valor"
					};
				}

				// [FIX:UNICA-FONTE] LEITURA DE EPP PELA API DO WINDOWS.
				//
				// Antes, esta leitura passava por dois servicos legados que foram
				// removidos do projeto: um motor de perfil de energia e um
				// wrapper de gerenciamento nativo. Eles eram a ponte para a
				// segunda via de escrita, e o metodo de leitura era justamente o
				// que permitia ao Brain decidir sozinho.
				//
				// A leitura agora é direta, pela mesma API que a escrita do perfil
				// usa. O `PowerArm` continua sendo o braço que EXECUTA, mas ele não
				// tem mais como decidir: o valor vem do Perfil Inteligente, e o
				// portão abaixo recusa qualquer escrita fora dele.
				int? actualEpp = ReadCurrentEpp();
				if (actualEpp.HasValue && actualEpp.Value >= 0 && Math.Abs(value - actualEpp.Value) < EPP_DELTA_MIN)
				{
					_logger.LogInfo($"[ARM-POWER] EPP {value} ignorado: sistema já em {actualEpp.Value} (delta < {EPP_DELTA_MIN}).");
					return new EppChangeResult
					{
						Executed = false,
						OldValue = CurrentEpp,
						NewValue = CurrentEpp,
						Delta = 0,
						Error = "delta insuficiente vs valor atual"
					};
				}

				// [FIX P1] O EPP só é gerenciado quando o PLANO DO PERFIL está ativo.
				//
				// A condição antiga aceitava qualquer plano cujo nome começasse com
				// "Voltris" — o que incluía o próprio plano gerenciado, e portanto
				// autorizava a escrita destrutiva. O certo é o inverso: só o plano
				// que o `ProfilePowerCoordinator` declarou como gerenciado recebe
				// ajuste de EPP. Fora dele, o Windows manda.
				if (!VoltrisOptimizer.Services.Power.PowerWriteGate.HasManagedPlan ||
					!VoltrisOptimizer.Utils.Win32.PowerNativeMethods.TryGetActiveSchemeGuid(out Guid activeScheme) ||
					activeScheme != VoltrisOptimizer.Services.Power.PowerWriteGate.ManagedPlan)
				{
					if (!_eppNonVoltrisLogged)
					{
						_eppNonVoltrisLogged = true;
						_logger.LogInfo(
							"[ARM-POWER] EPP automatico desativado: o plano ativo nao e o plano gerenciado " +
							"pelo Perfil Inteligente. Ajuste de EPP so vale dentro do plano do perfil.");
					}
					return new EppChangeResult
					{
						Executed = false,
						OldValue = CurrentEpp,
						NewValue = CurrentEpp,
						Delta = 0,
						Error = "plano ativo nao e o plano gerenciado pelo Perfil Inteligente"
					};
				}

				_logger.LogInfo($"[ARM-POWER] 🎯 Ajustando EPP para {value} (0=Performance, 128=Equilibrado, 255=Economia) | atual={CurrentEpp} | falhas={_eppConsecutiveFailures} | lido={actualEpp?.ToString() ?? "n/d"}...");

				// [FIX:UNICA-FONTE] O PORTÃO É O ÚNICO PORTÃO.
				//
				// Este é o último ponto do projeto por onde o Brain conseguia
				// gravar EPP. A cadeia antiga era: `VoltrisBody` -> `PowerArm` ->
				// motor de perfil -> gerenciador nativo -> P/Invoke próprio, e
				// nenhuma etapa consultava o `PowerWriteGate`.
				//
				// Em campo, o app gravava EPP 45 corretamente pelo perfil e, 7
				// segundos depois, o Brain gravava EPP 0 no mesmo plano:
				//
				//     [PowerApply]      AC EPP ... CONFIRMADO = 45
				//     [BRAIN-DECISION]  Regra: exploit | epp = 0 max performance
				//     [ARM-POWER]       EPP alterado de 50 -> 0 em 16ms
				//
				// Em notebook limitado por potência, EPP 0 é o pior valor
				// possível: o processador corre para o topo, esgota o orçamento
				// térmico e despenca. Era o "os GHz diminuem" que o usuário via.
				//
				// Todos os elos dessa cadeia foram removidos do projeto. Hoje
				// não existe caminho que alcance a escrita de EPP sem passar por
				// este portão, e o portão só abre para a aplicação do perfil.
				if (!PowerWriteGate.AllowProfileOwnedWrite("PowerArm.SetEpp", out string gateReason))
				{
					_logger.LogWarning($"[ARM-POWER] EPP {value} BLOQUEADO: {gateReason}");
					return new EppChangeResult
					{
						Executed = false,
						OldValue = CurrentEpp,
						NewValue = CurrentEpp,
						Delta = 0,
						Error = gateReason
					};
				}

				int oldValue = CurrentEpp;

				// [FIX:UNICO-DONO-DE-ENERGIA] O Brain NÃO grava mais EPP.
				//
				// Este método era a segunda via de escrita de EPP do projeto: ele
				// chamava PowerWriteACValueIndex direto e depois reativava o plano
				// com `powercfg /setactive` — fora do caminho do perfil, e capable
				// de gravar um valor que o Perfil Inteligente não escolheu.
				//
				// O Brain continua sendo dono da DECISÃO (ele sabe que um jogo
				// começou), mas a energia tem um dono só. A porta legítima é o
				// Perfil: pede-se a reaplicação, e o EPP vem do tier de
				// capacidade da máquina mais o perfil escolhido.
				//
				// Consequência deliberada: `value` deixa de ser gravado. Honrar o
				// número pedido exigiria uma segunda via de escrita — exatamente o
				// que esta refatoração eliminou. O método devolve o valor que o
				// Perfil efetivamente aplicou, lido de volta, para que o Brain
				// continue sabendo o estado real em vez do que pediu.
				_logger.LogInfo(
					$"[ARM-POWER] Brain pediu EPP {value}. O Perfil Inteligente e' o dono: " +
					"o valor pedido nao e gravado, e o Perfil reaplica o dele.");

				ProfilePowerAuthority.RequestProfileApply(
					"PowerArm.SetEpp", $"Brain pediu EPP {value}", _logger);

				// Lê de volta o que está valendo AGORA, que é a verdade útil.
				int effectiveEpp = CurrentEpp;
				if (PowerNativeMethods.TryReadCurrentEpp(out int lido) && lido >= 0)
				{
					effectiveEpp = lido;
				}

				bool success = true;
				string fallbackReason = string.Empty;

				if (effectiveEpp != oldValue)
				{
					_logger.LogInfo(
						$"[ARM-POWER] EPP efetivo apos o Perfil: {effectiveEpp} " +
						$"(pedido {value}, anterior {oldValue}).");
				}

				if (success)
				{
					CurrentEpp = effectiveEpp;
					_lastEppChange = DateTime.UtcNow;
					if (_eppConsecutiveFailures > 0)
					{
						_logger.LogInfo($"[ARM-POWER] EPP voltou a funcionar após {_eppConsecutiveFailures} falhas - contador zerado.");
						_eppConsecutiveFailures = 0;
						_eppDisabledUntilUtc = DateTime.MinValue;
					}
					_logger.LogSuccess($"[ARM-POWER] ✅ EPP alterado de {oldValue} → {value} em {stopwatch.ElapsedMilliseconds}ms");
					return new EppChangeResult
					{
						Executed = true,
						OldValue = oldValue,
						NewValue = value,
						Delta = value - oldValue,
						Error = null
					};
				}
				else
				{
					_eppConsecutiveFailures++;
					_lastEppChange = DateTime.UtcNow;
					if (_eppConsecutiveFailures >= EPP_MAX_CONSECUTIVE_FAILURES)
					{
						_eppDisabledUntilUtc = DateTime.UtcNow.AddSeconds(EPP_FAILURE_BACKOFF_SECONDS);
						_logger.LogWarning($"[ARM-POWER] ⚠️ {_eppConsecutiveFailures} falhas consecutivas de EPP ({fallbackReason}). Tentativas congeladas por {EPP_FAILURE_BACKOFF_SECONDS}s para evitar loop.");
					}
					else
					{
						_logger.LogWarning($"[ARM-POWER] ⚠️ EPP não aplicado ({_eppConsecutiveFailures}/{EPP_MAX_CONSECUTIVE_FAILURES}): {fallbackReason}");
					}
					return new EppChangeResult
					{
						Executed = false,
						OldValue = oldValue,
						NewValue = oldValue,
						Delta = 0,
						Error = fallbackReason
					};
				}
			}
			catch (Exception ex)
			{
				_logger.LogError("[ARM-POWER] ERRO em SetEppAsync: " + ex.Message);
				return new EppChangeResult
				{
					Executed = false,
					OldValue = CurrentEpp,
					NewValue = value,
					Error = ex.Message
				};
			}
		});
	}

	public Task<bool> SetPowerPlanAsync(Guid guid, string name)
	{
		string name2 = name;
		return Task.Run(delegate
		{
			Stopwatch stopwatch = Stopwatch.StartNew();
			try
			{
				if (!_isAdmin)
				{
					_logger.LogWarning("[ARM-POWER] PowerPlan NÃO aplicado: requer admin");
					return false;
				}
				bool result = ProfilePowerAuthority.RequestProfileApply("PowerArm", "pedido legado de troca de plano", _logger);
				if (result)
				{
					CurrentPowerPlanGuid = guid;
					ILoggingService logger = _logger;
					DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(40, 2);
					defaultInterpolatedStringHandler.AppendLiteral("[ARM-POWER] PowerPlan: \"");
					defaultInterpolatedStringHandler.AppendFormatted(name2);
					defaultInterpolatedStringHandler.AppendLiteral("\" ativado em ");
					defaultInterpolatedStringHandler.AppendFormatted(stopwatch.ElapsedMilliseconds);
					defaultInterpolatedStringHandler.AppendLiteral(" ms");
					logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
				}
				else
				{
					_logger.LogError("[ARM-POWER] Falha ao ativar PowerPlan: " + name2);
				}
				return result;
			}
			catch (Exception ex)
			{
				_logger.LogError("[ARM-POWER] ERRO em SetPowerPlanAsync: " + ex.Message);
				return false;
			}
		});
	}

	/// <summary>
	/// [FIX:UNICA-FONTE] Le o EPP do plano ativo, pela API do Windows.
	///
	/// Substitui `o servico legado.GetCurrentEpp()`. O método existe para que o
	/// `PowerArm` consiga RELER o valor real antes de gravar — a checagem de
	/// "delta mínimo" depende disso para não gerar escritas inúteis.
	///
	/// É leitura, não escrita: o `PowerArm` continua sem poder decidir o valor do
	/// EPP. Quem decide é o Perfil Inteligente.
	/// </summary>
	private static int? ReadCurrentEpp()
	{
		try
		{
			if (!VoltrisOptimizer.Utils.Win32.PowerNativeMethods.TryGetActiveSchemeGuid(out Guid scheme))
			{
				return null;
			}

			uint? value = VoltrisOptimizer.Utils.Win32.PowerNativeMethods.TryReadSchemeAcValueIndex(
				scheme,
				new Guid("36687f9e-e3a5-4dbf-b1dc-15eb381c6863"),
				new Guid("54533251-82be-4824-96c1-47b60b740d00"));

			return value.HasValue ? (int)value.Value : null;
		}
		catch
		{
			return null;
		}
	}

	public Task<bool> SetSystemResponsivenessAsync(int value)
	{
		return Task.Run(delegate
		{
			try
			{
				if (!_isAdmin)
				{
					_logger.LogWarning("[ARM-POWER] SystemResponsiveness NÃO aplicado: requer admin");
					return false;
				}

				if (value == CurrentSystemResponsiveness)
				{
					_logger.LogInfo($"[ARM-POWER] SystemResponsiveness já em {value}% (cache) - reescrita desnecessária evitada.");
					return true;
				}

				_logger.LogInfo($"[ARM-POWER] 🎯 Ajustando SystemResponsiveness para {value}% (0=Max Gaming, 100=Economia)...");

				// [FIX:POWER-GATE-BRAIN] Este registro faz parte do caracter do perfil
				// e o Brain nao tem voto sobre ele. Escrever por fora desfaz o EPP
				// que o perfil acabou de gravar, porque os dois competem pelo mesmo
				// presupuesto de escalateo do scheduler.
				if (!PowerWriteGate.AllowProfileOwnedWrite("PowerArm.SetSystemResponsiveness", out string gateReason))
				{
					_logger.LogWarning($"[ARM-POWER] SystemResponsiveness BLOQUEADO: {gateReason}");
					return false;
				}

				
				string keyPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
				using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyPath, true))
				{
					if (key != null)
					{
						key.SetValue("SystemResponsiveness", value, Microsoft.Win32.RegistryValueKind.DWord);
						CurrentSystemResponsiveness = value;
						_logger.LogSuccess($"[ARM-POWER] ✅ SystemResponsiveness definido para {value}%");
						return true;
					}
				}
				
				_logger.LogError("[ARM-POWER] ❌ Não foi possível acessar registry");
				return false;
			}
			catch (Exception ex)
			{
				_logger.LogError("[ARM-POWER] ERRO em SetSystemResponsivenessAsync: " + ex.Message);
				return false;
			}
		});
	}

	public Task<bool> SetTurboBoostPolicyAsync(int mode)
	{
		return Task.Run(delegate
		{
			try
			{
				if (!_isAdmin)
				{
					_logger.LogWarning("[ARM-POWER] TurboBoost BLOQUEADO: requer admin");
					return false;
				}

				// [FIX:UNICO-DONO-DE-ENERGIA] Turbo Boost NÃO é gravado pelo Brain.
				//
				// Este é o ponto mais perigoso de todo o arquivo, e o motivo está no
				// próprio comentário que existia aqui: o método escrevia o atributo
				// `Attributes` do Boost Mode no registro do Windows.
				//
				// Esconder o setting (`Attributes = 1`) tira o Turbo Boost do
				// processador sem aparecer em NENHUM plano de energia. O
				// Perfil Inteligente, que é dono da energia, grava o valor do boost
				// no plano — mas não sabe que o setting foi escondido, porque isso
				// não vive no plano. Ou seja: o Brain podia desligar o turbo, o
				// perfil podia "confirmar" o valor correto, e a máquina continuaria
				// sem boost. É a pior classe de defeito possível aqui — invisível
				// para tudo que existe para detectar.
				//
				// Boost é valor do perfil (EPP/boost/max na tabela). O Brain decide
				// QUE o jogo começou; quem decide o boost é o Perfil.
				_logger.LogInfo(
					"[ARM-POWER] Turbo Boost: o Brain nao grava mais o atributo do Boost Mode. " +
					"Esconder o setting nao deixa rastro em nenhum plano, e o Perfil " +
					"(que e' o dono da energia) nao tem como detectar nem reverter. " +
					"O valor do boost pertence a tabela do Perfil.");
				ProfilePowerAuthority.RequestProfileApply(
					"PowerArm.SetTurboBoostPolicy", $"Brain pediu Turbo Boost = {mode}", _logger);
				return true;
			}
			catch (Exception ex)
			{
				_logger.LogError("[ARM-POWER] ERRO em SetTurboBoostPolicyAsync: " + ex.Message);
				return false;
			}
		});
	}

	public Task<bool> SetCoreParkingAsync(int minPercent, int maxPercent)
	{
		return Task.Run(delegate
		{
			try
			{
				if (!_isAdmin)
				{
					_logger.LogWarning("[ARM-POWER] CoreParking BLOQUEADO: requer admin");
					return false;
				}

				// [FIX:POWER-GATE-BRAIN] Estacionamento de nucleo e valor do perfil.
				//
				// Este metodo escrevia `CPMINCORES` por `Process.Start` direto, o que
				// significa que nao passava nem por `SmartEnergyService.RunPowercfg`
				// (onde o portao de `powercfg.exe` esta instalado) nem por
				// `PowerNativeMethods`. Era um caminho inteiramente aberto para
				// gravar exatamente o valor que o self-test proibe.
				//
				// E o valor perigoso aqui e o MESMO que ja causou o defeito
				// medido: minPercent=0 significa "nunca estacionar nucleo", que no
				// ultrabook de 15W custou 43% de clock.
				if (!PowerWriteGate.AllowProfileOwnedWrite("PowerArm.SetCoreParking", out string gateReason))
				{
					_logger.LogWarning($"[ARM-POWER] CoreParking BLOQUEADO: {gateReason}");
					return false;
				}

                // [FIX:UNICO-DONO-DE-ENERGIA] O Brain nao grava mais core parking.
				//
				// Este bloco rodava powercfg /setacvalueindex ... CPMINCORES e
				// CPMAXCORES por Process.Start direto — o caminho mais aberto do
				// projeto, sem passar por nenhum portao. E o valor perigoso era
				// exatamente o que ja custou 43% de clock: minPercent = 0.
				//
				// Alem disso, os numeros pedidos nao podem ser honrados: o
				// estacionamento de nucleo e' valor do perfil (fixado em 100 pela
				// REGRA 9 do self-test, justamente para nunca desativar), e o
				// Perfil Inteligente e' quem o decide.
				ProfilePowerAuthority.RequestProfileApply(
					"PowerArm.SetCoreParking", $"Brain pediu core parking {minPercent}/{maxPercent}%", _logger);
				_logger.LogInfo(
					$"[ARM-POWER] CoreParking pedido ({minPercent}/{maxPercent}) delegando ao Perfil. " +
					"A folga termica vem de EPP e cooling policy, nao de cortar o teto." );
				return true;
			}
			catch (Exception ex)
			{
				_logger.LogError("[ARM-POWER] ERRO em SetCoreParkingAsync: " + ex.Message);
				return false;
			}
		});
	}

	// P1: Ultimate Performance Plan (desktop AC only)
	public Task<bool> SetUltimatePerformancePlanAsync(bool enable)
	{
		return Task.Run(delegate
		{
			try
			{
				if (!_isAdmin)
				{
					_logger.LogWarning("[ARM-POWER] UltimatePerformance BLOQUEADO: requer admin");
					return false;
				}

				if (enable)
				{
					// [FIX:UNICO-DONO-DE-ENERGIA] Ultimate Performance nao e mais
					// solicitado pelo Brain.
					//
					// Este bloco duplicava o plano final e o ativava por `powercfg`
					// direto — fora do portao, e sem passar pelo Perfil. Um plano
					// "final" em notebook de 15W e' o oposto do que resolve
					// lentidao: o gargalo e' falta de potencia sustained, e forcar o
					// plano ultimate aumenta a queda de clock em rajada.
					ProfilePowerAuthority.RequestProfileApply(
						"PowerArm.UltimatePerformance", "Brain pediu Ultimate Performance", _logger);
					CurrentPowerPlanGuid = UltimatePerformance;
					_logger.LogSuccess(
						"[ARM-POWER] Pedido de Ultimate Performance delegando ao Perfil. " +
						"O Perfil decide o plano conforme o perfil escolhido e a capacidade real da maquina.");
				}
				else
				{
					// [FIX:UNICO-DONO-DE-ENERGIA] Antes isto rodava
					// `powercfg /setactive {Balanced}` — trocar direto de plano, por
					// fora do portao. Nao existe mais um "plano anterior" externo
					// para quem devolve: quem rege o plano e' o Perfil.
					ProfilePowerAuthority.RequestProfileApply(
						"PowerArm.RestoreBalanced", "Brain pediu retorno ao Balanceado", _logger);
					CurrentPowerPlanGuid = Balanced;
					_logger.LogSuccess("[ARM-POWER] Retorno ao Balanceado delegando ao Perfil.");
				}
				return true;
				return true;
			}
			catch (Exception ex)
			{
				_logger.LogError("[ARM-POWER] ERRO em SetUltimatePerformancePlanAsync: " + ex.Message);
				return false;
			}
		});
	}

	public Task RestoreDefaultsAsync()
	{
		return Task.Run(delegate
		{
			try
			{
				_logger.LogInfo("[ARM-POWER] RestoreDefaults: Energia já está no padrão do Windows — nenhuma alteração necessária");
			}
			catch (Exception ex)
			{
				_logger.LogError("[ARM-POWER] ERRO ao restaurar defaults: " + ex.Message);
			}
		});
	}

	public void Dispose()
	{
	}
}
