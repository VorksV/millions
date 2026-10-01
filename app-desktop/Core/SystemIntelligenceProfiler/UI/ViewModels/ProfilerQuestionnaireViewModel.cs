#define DEBUG
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.UI.ViewModels;

namespace VoltrisOptimizer.Core.SystemIntelligenceProfiler.UI.ViewModels;

public class ProfilerQuestionnaireViewModel : INotifyPropertyChanged, IDisposable
{
	// ===== PONTE DE HUMOR PARA O ROSTO DO VORLOK IA =====
	//
	// O rosto da pagina de Perfil Inteligente precisa se comportar
	// LITERALMENTE como o do botao circular do Dashboard: mesma expressao,
	// mesma cor, mesma respiracao, mesma piscada e — o que faltava — as
	// MUDANCAS DE HUMOR ao vivo (o "Happy" que ShowMasterCelebrationAsync
	// dispara quando a otimizacao termina).
	//
	// O Dashboard usa binding vivo:
	//     Mood="{Binding BrainMood}"
	//     AnimationsEnabled="{Binding ShouldAnimationsRun}"
	//
	// Esta view tem outro DataContext, entao nao dava para repetir o binding
	// direto. A solucao abaixo e uma PONTE: reassiste o PropertyChanged do
	// DashboardViewModel e reemite aqui. O XAML fica com binding de verdade,
	// entao o fluxo de dados e o mesmo do Dashboard — nada de copia unica
	// (que congelaria o rosto num humor so).
	private DashboardViewModel? _dashboardVm;
	private bool _vorlokMoodBound;

	private void EnsureVorlokMoodBound()
	{
		if (_vorlokMoodBound)
		{
			return;
		}

		try
		{
			_dashboardVm = App.Services?.GetService<DashboardViewModel>();
			if (_dashboardVm != null)
			{
				_dashboardVm.PropertyChanged += OnDashboardPropertyChanged;
				_vorlokMoodBound = true;
			}
		}
		catch
		{
			// Sem Dashboard resolvido, o rosto simplesmente fica no humor padrao.
		}
	}

	private void OnDashboardPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(DashboardViewModel.BrainMood))
		{
			OnPropertyChanged(nameof(VorlokMood));
		}
		else if (e.PropertyName == nameof(DashboardViewModel.ShouldAnimationsRun))
		{
			OnPropertyChanged(nameof(VorlokAnimations));
		}
	}

	/// <summary>
	/// Humor atual do Brain. Espelha <c>DashboardViewModel.BrainMood</c> ao
	/// vivo — e o que faz o Vorlok IA sorrir (Happy) quando a otimizacao
	/// termina, exatamente como o rosto do botao circular.
	/// </summary>
	public BrainMood VorlokMood
	{
		get
		{
			EnsureVorlokMoodBound();
			return _dashboardVm?.BrainMood ?? BrainMood.Idle;
		}
	}

	/// <summary>
	/// Espelha <c>DashboardViewModel.ShouldAnimationsRun</c> ao vivo.
	/// </summary>
	public bool VorlokAnimations
	{
		get
		{
			EnsureVorlokMoodBound();
			return _dashboardVm?.ShouldAnimationsRun ?? true;
		}
	}

	private string _useCase = string.Empty;

	private string _strategy = "Equilibrado";

	private bool _isLaptop;

	private bool _optimizeGPU = true;

	private bool _resetNetwork = true;

	private bool _optimizeDisk = true;

	private bool _cleanSystem = true;

	private bool _autoRestartServices = true;

	private bool _allowRegistryChanges = true;

	private string[] _problems = Array.Empty<string>();

	private string _applyMode = "Manual";

	private int _totalProgressStep = 5;

	/// <summary>
	/// [FIX:PROGRESS-MONOTONICO] Maior porcentagem já exibida na barra global
	/// desta página. É o piso que impede o retrocesso — ver
	/// <see cref="UpdateGlobalProgress"/>.
	/// </summary>
	private int _progressFloor = 0;

	private readonly RelayCommand _confirmCommand;

	private bool _disposed;

	private OperationToken? _progressToken;

	/// <summary>
	/// [FIX:GAMER-VARIANTS] Nome exibido de um perfil, nos 3 idiomas.
	///
	/// Fica aqui, e não como um switch no setter, para que a sub-escolha gamer
	/// e o texto do card chamem a MESMA função. Um switch paralelo seria a
	/// terceira fonte de verdade do mesmo dado — a divergência entre elas já
	/// causou este bug uma vez.
	/// </summary>
	private static string GetProfileDisplayName(IntelligentProfileType profile)
	{
		try
		{
			var meta = VoltrisOptimizer.UI.Helpers.IntelligentProfileCatalog.GetProfileMeta(profile);
			return LocalizationService.Instance.GetString(meta.NameKey);
		}
		catch
		{
			return profile.ToString();
		}
	}

	/// <summary>
	/// [FIX:GAMER-VARIANTS] Traduz o perfil canônico para o enum que o
	/// <c>UserAnswers</c> carrega.
	///
	/// Existe para centralizar a conversão num único lugar. O caminho inverso
	/// (<c>UserProfile</c> → <c>IntelligentProfileType</c>) já tem um switch
	/// explícito no fluxo de conclusão; este é o caminho de ida. Com os dois
	/// explícitos, o perfil do questionário e o perfil de Settings nunca podem
	/// divergir — que era o defeito quando ambos eram inferidos por
	/// substring.
	/// </summary>
	private static UserProfile ToUserProfile(IntelligentProfileType profile) => profile switch
	{
		IntelligentProfileType.GamerCompetitive => UserProfile.GamerCompetitive,
		IntelligentProfileType.GamerSinglePlayer => UserProfile.GamerSinglePlayer,
		IntelligentProfileType.GamerSimulation => UserProfile.GamerSimulation,
		IntelligentProfileType.GamerMMO => UserProfile.GamerMMO,
		IntelligentProfileType.GamerStrategy => UserProfile.GamerStrategy,
		IntelligentProfileType.WorkOffice => UserProfile.WorkOffice,
		IntelligentProfileType.CreativeVideoEditing => UserProfile.CreativeVideoEditing,
		IntelligentProfileType.DeveloperProgramming => UserProfile.DeveloperProgramming,
		IntelligentProfileType.EnterpriseSecure => UserProfile.EnterpriseSecure,
		_ => UserProfile.GeneralBalanced
	};

		/// <summary>
		/// [FIX:GAMER-VARIANTS] As cinco variantes gamer, na ordem em que aparecem
		/// na sub-escolha. Os tokens sao estaveis em portugues por contrato — e o
		/// mesmo que o <c>StringToBoolConverter</c> grava em <see cref="UseCase"/>.
		///
		/// A lista é derivada do MAPA, não escrita à mão de novo. O mapa é a
		/// fonte de verdade (é ele que a UI inteira consulta), e copiar os
		/// tokens para cá criaria uma quinta cópia do mesmo dado — a receita
		/// exata que escondeu estas variantes até agora.
		/// </summary>
		public IReadOnlyList<string> GamerVariants { get; } =
			VoltrisOptimizer.UI.Converters.UseCaseProfileMap.GamerVariantTokens;

	/// <summary>
	/// [FIX:GAMER-VARIANTS] Verdadeiro quando o card "Gamer Competitivo" esta
	/// selecionado — e so entao a sub-escolha aparece.
	///
	/// REGRA DE OURO DA SUB-ESCOLHA: a variante selecionada e a UNICA forma de
	/// escolher um perfil gamer. Enquanto o card gamer nao estiver marcado, a
	/// sub-escolha fica oculta; quando marcar, o padrao e Competitivo. Isso
	/// mantem o caminho de sempre funcionando (um clique e ja e "Gamer
	/// Competitivo") e nao exige uma decisao extra de quem so quer jogar
	/// competitivo.
	/// </summary>
	public bool IsGamerFamilySelected
	{
		get
		{
			string current = UseCase ?? string.Empty;

			// Compara pelo PERFIL, não pelos cinco tokens. Mapear o token de volta
			// para enum e testar os cinco à mão duplicaria a lista pela quarta
			// vez — e é a duplicação que deixou as variantes invisíveis da
			// última vez. A lista vive em GamerVariants.
			foreach (string variant in GamerVariants)
			{
				if (string.Equals(current, variant, StringComparison.Ordinal))
				{
					return true;
				}
			}

			// Rede de segurança: se um dia um token gamer novo entrar no mapa e
			// for esquecido na lista, o mapa ainda sabe dizer que é gamer.
			// Sem isto, um token novo deixaria a sub-escolha oculta e o usuário
			// não veria nenhuma indicação de que a família tem mais opções.
			return VoltrisOptimizer.UI.Converters.UseCaseProfileMap.ToProfile(current) switch
			{
				IntelligentProfileType.GamerCompetitive => true,
				IntelligentProfileType.GamerSinglePlayer => true,
				IntelligentProfileType.GamerSimulation => true,
				IntelligentProfileType.GamerMMO => true,
				IntelligentProfileType.GamerStrategy => true,
				_ => false
			};
		}
	}

	/// <summary>
	/// [FIX:CARDS-DIM] Verdadeiro quando o usuário já escolheu um perfil.
	///
	/// Serve para a única coisa que a página precisa saber e que cada card não
	/// consegue saber sozinho: "tem alguma escolha feita?". Um card só conhece o
	/// próprio IsChecked, então sem esta propriedade não existe como saber se os
	/// CINCO IRMÃOS devem ser desbotados. Com ela, o desbotamento vira um
	/// MultiDataTrigger no estilo — sem conversor, sem código.
	///
	/// Distingue "nada escolhido" de "outro escolhido": no primeiro caso os
	/// cards ficam todos normais (a página não deve parecer morta antes da
	/// primeira escolha), no segundo os não escolhidos recuam.
	/// </summary>
	public bool HasAnyCardSelected => !string.IsNullOrEmpty(_useCase);

	/// <summary>
	/// [FIX:CARDS-DIM] Verdadeiro quando a PRIORIDADE já foi escolhida.
	///
	/// É o par de <see cref="HasAnyCardSelected"/> para a linha de estratégia
	/// (Extrema / Equilibrado / Bateria). São duas decisões independentes, em
	/// dois grupos diferentes, e cada grupo precisa saber sozinho se já tem
	/// escolha — senão desbotar os três segmentos de estratégia junto com os
	/// cards de uso, ou não desbotar nenhum.
	/// </summary>
	public bool HasAnyStrategySelected => !string.IsNullOrEmpty(_strategy);

	public string UseCase
	{
		get
		{
			return _useCase;
		}
		set
		{
			if (_useCase != value)
			{
				_useCase = value;
				OnPropertyChanged("UseCase");

				// [FIX:GAMER-VARIANTS] Avisos de UI que dependem da seleção.
				// Sem o segundo, a sub-escolha gamer não apareceria ao clicar no
				// card — que é justamente o caso que ela existe para atender.
				OnPropertyChanged("IsGamerFamilySelected");

				// [FIX:CARDS-DIM] O desbotamento dos cards não escolhidos depende
				// deste aviso. Sem ele, escolher um cartão marcava um e deixava os
				// outros cinco em estado normal — o usuário perdia a noção de que
				// escolheram algo, porque nada no cartão indicava exclusivity.
				OnPropertyChanged("HasAnyCardSelected");

				_confirmCommand.RaiseCanExecuteChanged();

				// [FIX:GAMER-VARIANTS] O TEXTO EXIBIDO vem do catálogo, não de um
				// switch paralelo.
				//
				// Havia um switch de 6 casos traduzindo o token para o nome, e ele
				// precisava de uma linha nova por perfil adicionado — foi
				// exatamente por ficar para trás que as quatro variantes gamer
				// apareciam com o texto cru do token, ou nem apareciam.
				// `IntelligentProfileCatalog` já sabe o nome de TODOS os dez
				// perfis e o AccessPoint de onde a UI (modal do Dashboard) lê.
				string text = GetProfileDisplayName(VoltrisOptimizer.UI.Converters.UseCaseProfileMap.ToProfile(value));
				if (1 == 0)
				{
				}
				string text2 = text;
				UpdateGlobalProgress(40, LocalizationService.Instance.GetString("ProfilerProgressProfilePrefix") + text2);
				try
				{
					App.LoggingService?.LogInfo("[QUESTIONARIO] UseCase alterado para: '" + value + "'");
				}
				catch
				{
				}
			}
		}
	}

	public string Strategy
	{
		get
		{
			return _strategy;
		}
		set
		{
			if (_strategy != value)
			{
				_strategy = value;
				OnPropertyChanged("Strategy");

				// [FIX:CARDS-DIM] Sem este aviso, escolher a prioridade não
				// desbotaria os outros dois segmentos da linha.
				OnPropertyChanged("HasAnyStrategySelected");

				_confirmCommand.RaiseCanExecuteChanged();
				if (1 == 0)
				{
				}
				string text = value switch
				{
					"Performance Extrema" => LocalizationService.Instance.GetString("ProfilerExtremePerformance"), 
					"Equilibrado" => LocalizationService.Instance.GetString("ProfilerBalanced"), 
					"Máxima Retenção de Bateria" => LocalizationService.Instance.GetString("ProfilerMaxBatteryRetention"), 
					_ => value};
				if (1 == 0)
				{
				}
				string text2 = text;
				UpdateGlobalProgress(70, LocalizationService.Instance.GetString("ProfilerProgressPriorityPrefix") + text2);
				try
				{
					App.LoggingService?.LogInfo("[QUESTIONARIO] Strategy alterado para: '" + value + "'");
				}
				catch
				{
				}
			}
		}
	}

	public bool IsLaptop
	{
		get
		{
			return _isLaptop;
		}
		set
		{
			_isLaptop = value;
			OnPropertyChanged("IsLaptop");
			UpdateGlobalProgress(_totalProgressStep + 1, LocalizationService.Instance.GetString("ProfilerProgressPersonalizing"));
		}
	}

	public bool OptimizeGPU
	{
		get
		{
			return _optimizeGPU;
		}
		set
		{
			_optimizeGPU = value;
			OnPropertyChanged("OptimizeGPU");
			UpdateGlobalProgress(_totalProgressStep + 1, LocalizationService.Instance.GetString("ProfilerProgressPersonalizing"));
		}
	}

	public bool ResetNetwork
	{
		get
		{
			return _resetNetwork;
		}
		set
		{
			_resetNetwork = value;
			OnPropertyChanged("ResetNetwork");
			UpdateGlobalProgress(_totalProgressStep + 1, LocalizationService.Instance.GetString("ProfilerProgressPersonalizing"));
		}
	}

	public bool OptimizeDisk
	{
		get
		{
			return _optimizeDisk;
		}
		set
		{
			_optimizeDisk = value;
			OnPropertyChanged("OptimizeDisk");
			UpdateGlobalProgress(_totalProgressStep + 1, LocalizationService.Instance.GetString("ProfilerProgressPersonalizing"));
		}
	}

	public bool CleanSystem
	{
		get
		{
			return _cleanSystem;
		}
		set
		{
			_cleanSystem = value;
			OnPropertyChanged("CleanSystem");
			UpdateGlobalProgress(_totalProgressStep + 1, LocalizationService.Instance.GetString("ProfilerProgressPersonalizing"));
		}
	}

	public bool AutoRestartServices
	{
		get
		{
			return _autoRestartServices;
		}
		set
		{
			_autoRestartServices = value;
			OnPropertyChanged("AutoRestartServices");
			UpdateGlobalProgress(_totalProgressStep + 1, LocalizationService.Instance.GetString("ProfilerProgressPersonalizing"));
		}
	}

	public bool AllowRegistryChanges
	{
		get
		{
			return _allowRegistryChanges;
		}
		set
		{
			_allowRegistryChanges = value;
			OnPropertyChanged("AllowRegistryChanges");
			UpdateGlobalProgress(_totalProgressStep + 1, LocalizationService.Instance.GetString("ProfilerProgressPersonalizing"));
		}
	}

	public string[] Problems
	{
		get
		{
			return _problems;
		}
		set
		{
			_problems = value;
			OnPropertyChanged("Problems");
			UpdateGlobalProgress(_totalProgressStep + 2, LocalizationService.Instance.GetString("ProfilerProgressAnalyzingProblems"));
		}
	}

	public string ApplyMode
	{
		get
		{
			return _applyMode;
		}
		set
		{
			_applyMode = value;
			OnPropertyChanged("ApplyMode");
		}
	}

	public ICommand ConfirmCommand => _confirmCommand;

	public event EventHandler? Completed;

	public event PropertyChangedEventHandler? PropertyChanged;

	private void UpdateGlobalProgress(int value, string message)
	{
		try
		{
			// [FIX:PROGRESS-MONOTONICO] A barra NUNCA pode voltar atrás.
			//
			// O defeito: cada escolha escrevia um número FIXO — perfil 40,
			// estratégia 70 — e as opções extras usavam um contador corrente
			// (`_totalProgressStep + 1`). Em qualquer ordem, isso permitia
			// retrocesso:
			//
			//   marcar 3 extras   -> 6%, 7%, 8%
			//   escolher perfil   -> 40%
			//   marcar mais 1     -> 41%
			//   trocar o perfil   -> 40%     <-- REGREDIU de 41 para 40
			//
			// Bastava o usuário trocar de ideia depois de mexer nas opções
			// extras para ver a barra andar para trás. Isso é ilegível: a pessoa
			// fez trabalho e a barra desfez sozinha.
			//
			// A correção é um PISO — o maior valor já alcançado. A porcentagem
			// não recua, mas o TEXTO continua sendo atualizado: quem trocou de
			// ideia precisa ver para qual opção está indo agora, senão a troca
			// parece não ter funcionado.
			//
			// Piso, e não "recalcular do zero", porque a barra responde ao
			// PERCURSO da configuração. Trocar de perfil não devolve o trabalho
			// de ter respondido às perguntas anteriores.
			bool recuou = value < _progressFloor;
			if (!recuou)
			{
				_progressFloor = value;
			}

			_totalProgressStep = _progressFloor;

			if (_progressToken == null)
			{
				_progressToken = GlobalProgressService.Instance.BeginOperation(LocalizationService.Instance.GetString("ProfilerProgressAnalysisTitle"), isPriority: true);
			}

			_progressToken!.UpdateProgress(_progressFloor, message);

			if (_progressFloor >= 100)
			{
				_progressToken.Complete(message);
				_progressToken = null;
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[ProfilerQuestionnaireViewModel] " + ex.Message);
		}
	}

	/// <summary>
	/// Encerra o token de progresso do questionário, se ainda estiver vivo.
	///
	/// O token só fechava ao chegar em 100. Se o usuário abandonasse o
	/// questionário, a tarefa ficava ativa com prioridade 1 — e, como
	/// operação com prioridade alta domina a exibição, o rodapé passava a
	/// mostrar a mensagem errada e "Carregando Dashboard" nunca saía.
	/// </summary>
	public void CompleteProgress()
	{
		try
		{
			if (_progressToken != null)
			{
				_progressToken.Complete(LocalizationService.Instance.GetString("ProfilerProgressProfileCompleted"));
				_progressToken = null;
			}
		}
		catch
		{
		}
	}

	public ProfilerQuestionnaireViewModel()
	{
		_confirmCommand = new RelayCommand(delegate
		{
			ExecuteConfirm();
		}, (object? _) => CanConfirm());
		try
		{
			App.LoggingService?.LogInfo("[QUESTIONARIO] ViewModel inicializado");
		}
		catch
		{
		}
	}

	private async void ExecuteConfirm()
	{
		try
		{
			ILoggingService? loggingService = App.LoggingService;
			if (loggingService != null)
			{
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(57, 2);
				defaultInterpolatedStringHandler.AppendLiteral("[QUESTIONARIO] Iniciando CONFIRM... (UseCase=");
				defaultInterpolatedStringHandler.AppendFormatted(UseCase);
				defaultInterpolatedStringHandler.AppendLiteral(", Strategy=");
				defaultInterpolatedStringHandler.AppendFormatted(Strategy);
				defaultInterpolatedStringHandler.AppendLiteral(")");
				loggingService!.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
			}
			await SubmitAsync();
			App.LoggingService?.LogSuccess("[QUESTIONARIO] CONFIRM executado com sucesso.");
		}
		catch (Exception ex2)
		{
			Exception ex = ex2;
			App.LoggingService?.LogError("[QUESTIONARIO] Erro fatal ao processar confirmação: " + ex.Message, ex);
		}
	}

	public async Task SubmitAsync()
	{
		UpdateGlobalProgress(90, LocalizationService.Instance.GetString("ProfilerProgressMappingIntelligence"));
		if (string.IsNullOrWhiteSpace(UseCase))
		{
			UseCase = "Uso Familiar Casual";
		}
		if (string.IsNullOrWhiteSpace(Strategy))
		{
			Strategy = "Equilibrado";
		}
		UserAnswers answers = new UserAnswers
		{
			UseCase = UseCase,
			Priority = Strategy,
			IsLaptop = IsLaptop,
			OptimizeGPU = OptimizeGPU,
			ResetNetwork = ResetNetwork,
			OptimizeDisk = OptimizeDisk,
			CleanSystem = CleanSystem,
			AutoRestartServices = AutoRestartServices,
			AllowRegistryChanges = AllowRegistryChanges,
			Problems = Problems,
			ApplyMode = ApplyMode
		};
		// [FIX:GAMER-VARIANTS] A INFERÊNCIA POR `Contains()` FOI REMOVIDA.
		//
		// BUG ORIGINAL: o perfil era deduzido do texto do token com busca de
		// substring, em português:
		//     if (u.Contains("gamer") ...)
		//         answers.Profile = u.Contains("single") ? GamerSinglePlayer
		//                                                : GamerCompetitive;
		//
		// Consequências, todas reais:
		//  1. As variantes Simulação, MMO e Estratégia NÃO TINHAM RAMO. Caíam
		//     todas em GamerCompetitive — que é justamente o perfil ERRADO
		//     para elas. Só a variante "Single" era reconhecível.
		//  2. A regra dependia de ACENTO e de REDIGÊNCIA: "vídeo" com acento
		//     não casaria com um token sem acento. Um token novo escrito
		//     diferente viraria "Uso Geral" silenciosamente.
		//  3. "Criador de Conteúdo" e "Uso Familiar Casual" dependiam das
		//     palavras "criador"/"conteúdo" e "familiar"/"casual" — um
		//     retrabalho de copy quebrava o perfil sem erro nenhum.
		//  4. Havia DUAS fontes de verdade do mesmo mapeamento: esta cadeia de
		//     `Contains` e o `UseCaseProfileMap`, que é o que a UI (ícone, cor,
		//     nome) já usava. Foi a divergência entre as duas que deixou as
		//     quatro variantes invisíveis.
		//
		// AGORA há uma fonte só. `UseCaseProfileMap.ToProfile` é a tabela
		// oficial — a mesma que decide ícone e cor — e o ViewModel apenas
		// traduz o enum de volta para o `UserProfile` que o resto do profiler
		// consome. Trocar o texto de um card não pode mais mudar o perfil.
		answers.Profile = ToUserProfile(VoltrisOptimizer.UI.Converters.UseCaseProfileMap.ToProfile(UseCase));
		try
		{
			ILoggingService? loggingService = App.LoggingService;
			if (loggingService != null)
			{
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(44, 2);
				defaultInterpolatedStringHandler.AppendLiteral("[QUESTIONARIO] Perfil detectado: ");
				defaultInterpolatedStringHandler.AppendFormatted(answers.Profile);
				defaultInterpolatedStringHandler.AppendLiteral(" (input='");
				defaultInterpolatedStringHandler.AppendFormatted(UseCase);
				defaultInterpolatedStringHandler.AppendLiteral("')");
				loggingService!.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
			}
		}
		catch
		{
		}
		ProfileStore store = new ProfileStore();
		ProfilerState s = store.Load();
		s.Answers = answers;
		s.QuestionnaireCompleted = true;
		store.Save(s);
		UpdateGlobalProgress(100, LocalizationService.Instance.GetString("ProfilerProgressProfileCompleted"));
		try
		{
			App.LoggingService?.LogSuccess("[QUESTIONARIO] Respostas salvas e questionário marcado como concluído");
		}
		catch
		{
		}
		try
		{
			UserProfile profile = answers.Profile;
			if (1 == 0)
			{
			}
			// [FIX:GAMER-VARIANTS] Os três ramos que faltavam.
			//
			// O switch original mapeava 6 dos 10 perfis e jogava
			// GamerSimulation, GamerMMO e GamerStrategy no `default`, que é
			// GeneralBalanced. Ou seja: mesmo que o usuário escolhesse uma
			// variante gamer, o perfil gravado em Settings seria "Uso Geral".
			// A escolha aparecia na tela e não valia nada — o pior tipo de bug,
			// porque nada dá sinal de erro.
			IntelligentProfileType intelligentProfileType = profile switch
			{
				UserProfile.GamerCompetitive => IntelligentProfileType.GamerCompetitive, 
				UserProfile.GamerSinglePlayer => IntelligentProfileType.GamerSinglePlayer, 
				UserProfile.GamerSimulation => IntelligentProfileType.GamerSimulation, 
				UserProfile.GamerMMO => IntelligentProfileType.GamerMMO, 
				UserProfile.GamerStrategy => IntelligentProfileType.GamerStrategy, 
				UserProfile.WorkOffice => IntelligentProfileType.WorkOffice, 
				UserProfile.CreativeVideoEditing => IntelligentProfileType.CreativeVideoEditing, 
				UserProfile.DeveloperProgramming => IntelligentProfileType.DeveloperProgramming, 
				UserProfile.EnterpriseSecure => IntelligentProfileType.EnterpriseSecure, 
				_ => IntelligentProfileType.GeneralBalanced};
			if (1 == 0)
			{
			}
			IntelligentProfileType settingsProfile = intelligentProfileType;
			SettingsService.Instance.Settings.IntelligentProfile = settingsProfile;

			// [FIX:STRATEGY-3-STATES] GRAVA TAMBÉM A ESTRATÉGIA DE PERFORMANCE.
			//
			// O perfil já era sincronizado com Settings, mas a estratégia ficava só
			// dentro de `UserAnswers.Priority` — e esse campo não era lido por
			// quase nada. Sem gravar aqui, o "Aplicar tudo" não tem como saber o
			// que o usuário escolheu e acaba adivinhando pelo perfil.
			SettingsService.Instance.Settings.PerformanceStrategyToken = Strategy;
			SettingsService.Instance.SaveSettings();

			App.LoggingService?.LogInfo(
				$"[QUESTIONARIO] Estrategia gravada: token='{Strategy}' -> " +
				$"'{VoltrisOptimizer.Services.Performance.PerformanceStrategyMap.ToCanonical(VoltrisOptimizer.Services.Performance.PerformanceStrategyMap.ToStrategy(Strategy))}' " +
				$"(EPP alvo={VoltrisOptimizer.Services.Performance.PerformanceStrategyMap.GetEppValue(VoltrisOptimizer.Services.Performance.PerformanceStrategyMap.ToStrategy(Strategy))})");

			// [FIX:STRATEGY-3-STATES] APLICA AGORA, E NÃO SÓ NO "APLICAR TUDO".
			//
			// Sem esta chamada, quem terminasse o questionárioania com "Performance
			// Extrema" veria a escolha gravada e, no Windows, o plano ainda
			// "Equilibrado" e o EPP em 50. Só apareceria o efeito depois do
			// "Aplicar tudo" — e a leitura natural do usuário é que escolher na
			// tela já valeu alguma coisa.
			//
			// A aplicação é fire-and-forget de propósito: falhar ao gravar energia
			// não pode impedir o questionário de ser concluído e o perfil de ser
			// salvo. O `o servico legado` registra o motivo da falha.
			_ = ApplyPerformanceStrategyAsync(Strategy);
			SettingsService.Instance.NotifyProfileChanged(settingsProfile);
			ILoggingService? loggingService2 = App.LoggingService;
			if (loggingService2 != null)
			{
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(49, 1);
				defaultInterpolatedStringHandler.AppendLiteral("[QUESTIONARIO] Perfil sincronizado com Settings: ");
				defaultInterpolatedStringHandler.AppendFormatted(settingsProfile);
				loggingService2!.LogSuccess(defaultInterpolatedStringHandler.ToStringAndClear());
			}
		}
		catch (Exception ex)
		{
			App.LoggingService?.LogError("[QUESTIONARIO] Erro ao sincronizar perfil com Settings: " + ex.Message);
		}
		try
		{
			App.LoggingService?.LogInfo("[QUESTIONARIO] Disparando evento Completed...");
		}
		catch
		{
		}
		this.Completed?.Invoke(this, EventArgs.Empty);
		try
		{
			ILoggingService? loggingService3 = App.LoggingService;
			if (loggingService3 != null)
			{
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(54, 1);
				defaultInterpolatedStringHandler.AppendLiteral("[QUESTIONARIO] Evento Completed disparado! Listeners: ");
				EventHandler? completed = this.Completed;
				defaultInterpolatedStringHandler.AppendFormatted((completed != null) ? completed!.GetInvocationList().Length : 0);
				loggingService3!.LogSuccess(defaultInterpolatedStringHandler.ToStringAndClear());
			}
		}
		catch
		{
		}
		await Task.CompletedTask;
	}

		/// <summary>
		/// [FIX:UNICA-FONTE] A estratégia de energia escolhida NÃO é aplicada aqui.
		///
		/// Este método chamava o `o servico legado`, que gravava EPP
		/// (0/128/255) e trocava o plano para Ultimate/PowerSaver/Balanced — por
		/// um caminho próprio, com seus próprios planos, e sem passar pelo
		/// Perfil Inteligente.
		///
		/// Isso criava uma contradição direta: o questionário perguntava o perfil
		/// (gamer, escritório, criativo...) e logo em seguida uma camada separada
		/// sobrescrevia a energia com três valores genéricos, ignorando o perfil
		/// escolhido e a capacidade real da máquina. Era a segunda fonte de
		/// escrita, agora removida do projeto.
		///
		/// O token da estratégia continua SALVO em `AppSettings`, porque é
		/// escolha do usuário e aparece no resumo. Ele virou o que deveria ter
		/// sido desde o início: um registro do que a pessoa pediu, dentro do
		/// perfil, e não uma instrução de escrita.
		///
		/// Quem aplica a energia é o `ProfilePowerCoordinator`, acionado pelo
		/// evento `ProfileChanged` que este próprio ViewModel dispara ao salvar o
		/// perfil. Um caminho só, e ele já está aceso.
		/// </summary>
		private Task ApplyPerformanceStrategyAsync(string token)
		{
			try
			{
				var settings = SettingsService.Instance.Settings;
				var profile = settings.IntelligentProfile;

				App.LoggingService?.LogInfo(
					$"[QUESTIONARIO] Estrategia '{token}' registrada para o perfil {profile}. " +
					"A energia e aplicada pelo Perfil Inteligente (fonte unica); " +
					"nenhum valor e gravado aqui.");

				// Garante que a aplicação do perfil aconteça exatamente uma vez,
				// por este motivo, depois que o perfil foi salvo.
				VoltrisOptimizer.Services.Power.ProfilePowerCoordinator.ApplyNow(
					profile, App.LoggingService);
			}
			catch (Exception ex)
			{
				App.LoggingService?.LogWarning(
					$"[QUESTIONARIO] falha ao registrar a estrategia '{token}': {ex.Message}");
			}

			return Task.CompletedTask;
		}

		private bool CanConfirm()
	{
		string text = (UseCase ?? string.Empty).Trim();
		string text2 = (Strategy ?? string.Empty).Trim();
		bool flag = text.Length > 0 && text2.Length > 0;
		if (!flag)
		{
		}
		return flag;
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_disposed = true;
			if (_vorlokMoodBound && _dashboardVm != null)
			{
				_dashboardVm.PropertyChanged -= OnDashboardPropertyChanged;
				_dashboardVm = null;
				_vorlokMoodBound = false;
			}
			if (_progressToken != null)
			{
				_progressToken!.Complete(LocalizationService.Instance.GetString("ProfilerProgressAnalysisCanceled"));
				_progressToken = null;
			}
		}
	}

	private void OnPropertyChanged([CallerMemberName] string? name = null)
	{
		this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
	}
}
