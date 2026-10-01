using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using VoltrisOptimizer.Core.SystemIntelligenceProfiler.UI.ViewModels;
using VoltrisOptimizer.UI;
using VoltrisOptimizer.UI.Controls;

namespace VoltrisOptimizer.Core.SystemIntelligenceProfiler.UI.Views;

public partial class ProfilerQuestionnaireView : UserControl
{
	public ProfilerQuestionnaireView()
	{
		// Log de diagnóstico com caminho FIXO (ver ProfilerViewDebug). É criado
		// ANTES de qualquer outra coisa: se o arquivo não existir depois de rodar
		// o app, a view nem chegou a ser construída.
		ProfilerViewDebug.Log("=== CONSTRUTOR DA VIEW INICIADO ===");
		try
		{
			ProfilerViewDebug.Log("  Calling InitializeComponent()...");
			InitializeComponent();
			ProfilerViewDebug.Log("  InitializeComponent() OK");
		}
		catch (Exception ex)
		{
			// Se o XAML falhar (recurso faltando, tag desbalanceada), a exceção
			// estoura AQUI e a view inteira nunca entra no ContentFrame — o
			// sintoma é exatamente "página vazia".
			ProfilerViewDebug.LogExceptionChain("  !!! InitializeComponent FALHOU:", ex);
			ProfilerViewDebug.Log($"  !!! STACK: {ex.StackTrace}");
			throw;
		}

		// BUG CORRIGIDO (grave): esta view NÃO tinha DataContext em lugar nenhum.
		// O XAML antigo não declarava DataContext, o ViewModelLocator não expõe
		// nenhuma propriedade para ele e o ProfilerQuestionnaireViewModel não está
		// registrado no DI. O resultado: a página aparecia (todos os textos usam
		// x:Static no LocalizationService, que não depende de DataContext), mas
		// NENHUMA seleção funcionava — os RadioButton ficavam sem IsChecked e o
		// code-behind caía no LogError "DataContext não é ProfilerQuestionnaireViewModel".
		// Na prática o perfil escolhido nunca era salvo.
		//
		// O ViewModel tem construtor sem parâmetros, então instanciar aqui é mais
		// simples e mais robusto que registrar no contêiner: funciona igual se a
		// view for hosted pelo ContentFrame, pelo NavigationService ou por qualquer
		// outro caminho, sem depender de registro prévio.
		try
		{
			base.DataContext = new ProfilerQuestionnaireViewModel();
			ProfilerViewDebug.Log("  DataContext atribuido: " + base.DataContext?.GetType().FullName);
		}
		catch (Exception ex)
		{
			ProfilerViewDebug.Log($"  !!! Falha ao criar DataContext: {ex.GetType().FullName}: {ex.Message}");
			throw;
		}

		try
		{
			App.LoggingService?.LogInfo("[QUESTIONARIO] View inicializando...");
		}
		catch
		{
		}
		if (base.DataContext is ProfilerQuestionnaireViewModel profilerQuestionnaireViewModel)
		{
			profilerQuestionnaireViewModel.Completed -= Vm_Completed;
			profilerQuestionnaireViewModel.Completed += Vm_Completed;

			// [DEBUG:GAMER-SUBSELECT] Rastreia a seleção para o log de diagnóstico.
			//
			// Sem isto, "cliquei no card gamer e a sub-escolha não apareceu" é um
			// problema sem nenhuma evidência: a tela mostra um card marcado e nada
			// mais, e não há como distinguir se o ViewModel não falou, se o
			// DataTrigger não disparou, ou se o painel ficou visível com altura
			// zero. As três causas exigem correções diferentes.
			profilerQuestionnaireViewModel.PropertyChanged -= Vm_PropertyChanged;
			profilerQuestionnaireViewModel.PropertyChanged += Vm_PropertyChanged;
		}
		base.Loaded += OnLoaded;
		base.DataContextChanged += new DependencyPropertyChangedEventHandler(OnDataContextChanged);
		base.SizeChanged += OnViewSizeChanged;
		// Se o usuário sair do questionário sem confirmar, o token de progresso
		// precisa morrer: sozinho ele ficava ativo com prioridade 1 e prendia o
		// texto do rodapé.
		base.Unloaded += delegate
		{
			try
			{
				if (base.DataContext is ProfilerQuestionnaireViewModel vm)
				{
					vm.Completed -= Vm_Completed;
					vm.CompleteProgress();
				}
			}
			catch
			{
			}
		};
		ProfilerViewDebug.Log("=== CONSTRUTOR CONCLUIDO ===");
	}

	/// <summary>
	/// [DEBUG:GAMER-SUBSELECT] Reage a mudanças de UseCase e mede a sub-escolha.
	///
	/// O Dump roda no PRIORITÁRIO DE FUNDO (BeginInvoke) de propósito: no
	/// momento do PropertyChanged, o WPF ainda não aplicou o Binding do
	/// DataTrigger nem concluiu o layout, então Visibility e ActualHeight ainda
	/// seriam os valores antigos e o log mentiria.
	/// </summary>
	private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName != nameof(ProfilerQuestionnaireViewModel.UseCase) &&
			e.PropertyName != nameof(ProfilerQuestionnaireViewModel.IsGamerFamilySelected))
		{
			return;
		}

		string token = (sender as ProfilerQuestionnaireViewModel)?.UseCase ?? "(null)";
		ProfilerViewDebug.Log($"  >> UseCase='{token}' IsGamerFamilySelected={(sender as ProfilerQuestionnaireViewModel)?.IsGamerFamilySelected}");

		// O desfoque dos cards não escolhidos é síncrono e SEMPRE reavaliado: se
		// ficasse só no Binding, um clique que volta ao estado inicial (escolher,
		// depois trocar) deixaria o card anterior marcado à força.
		RefreshCardFocus();

		base.Dispatcher.BeginInvoke(new Action(() => DumpGamerPanel()), System.Windows.Threading.DispatcherPriority.Background);
	}

	/// <summary>
	/// [DEBUG:GAMER-SUBSELECT] Estado real da sub-escolha gamer.
	/// </summary>
	private void DumpGamerPanel()
	{
		try
		{
			var vm = base.DataContext as ProfilerQuestionnaireViewModel;
			int items = vm?.GamerVariants?.Count ?? -1;

			var panel = this.FindName("GamerVariantPanel") as FrameworkElement;

			ProfilerViewDebug.Log($"  -- GAMER PANEL: IsGamerFamilySelected={vm?.IsGamerFamilySelected} " +
				$"GamerVariants={items} " +
				$"PanelVis={(panel == null ? "CAMPO AUSENTE" : panel.Visibility.ToString())} " +
				$"PanelW={panel?.ActualWidth} PanelH={panel?.ActualHeight} " +
				$"DesirH={panel?.DesiredSize.Height}");

			if (panel == null)
			{
				ProfilerViewDebug.Log("  !!! GamerVariantPanel nao foi encontrado na view (x:Name ausente ou removido).");
			}
		}
		catch (Exception ex)
		{
			ProfilerViewDebug.Log("  !!! DumpGamerPanel falhou: " + ex.Message);
		}
	}

	/// <summary>
	/// [FIX:CARDS-DIM] DESFOCA OS CARDS QUE NÃO FORAM ESCOLHIDOS.
	///
	/// O QUE ESTE HACK SUBSTITUI, E POR QUE FOI FEITO ASSIM
	/// ====================================================
	/// A versão declarativa usava um `MultiDataTrigger` no estilo `ProfileCard`:
	///
	///     IsChecked == False   E   DataContext.HasAnyCardSelected == True
	///         → Opacity 0.38 + BlurEffect
	///
	/// Isso é a resposta certa no papel, mas NÃO CARREGA no WPF. `ProfileCard`
	/// mora num `ResourceDictionary`, e `Condition` dentro de um trigger de Style
	/// é "selado" no momento em que o dicionário é carregado — sem DataContext e
	/// sem elemento. Aí o WPF exige uma origem para o binding e ABORTA A VIEW
	/// INTEIRA:
	///
	///     A propriedade definida 'FrameworkElement.Style' iniciou uma exceção.
	///     InvalidOperationException: É necessário ter um valor não nulo para
	///     'Binding'.   ( System.Windows.Condition.Seal )
	///
	/// Detalhe que custou duas tentativas: passar o binding como ATRIBUTO
	/// (`<Condition Binding="..." Value="True"/>`) é silenciosamente ignorado,
	/// porque `Condition.Binding` é somente-leitura, e o `Binding` chega nulo
	/// producing exatamente a exceção acima. A sintaxe aninhada
	/// (`<Condition.Binding>`) também não resolve: o problema é o momento do
	/// Selar, não a forma de escrever.
	///
	/// Isto aqui é imperativo, mas é DETERMINÍSTICO: um card que não está
	/// selecionado e o usuário ainda não escolheu nada continua com aparência
	/// normal. Sem isso, um `Trigger IsChecked=False` puro escureceria os cinco
	/// cards errados logo na abertura, e a página nasceria parecendo desativada.
	/// </summary>
	private void RefreshCardFocus()
	{
		try
		{
			string selected = (base.DataContext as ProfilerQuestionnaireViewModel)?.UseCase ?? string.Empty;
			bool anySelected = !string.IsNullOrEmpty(selected);

			RefreshCardFocus(CardGamer, selected, anySelected);
			RefreshCardFocus(CardOffice, selected, anySelected);
			RefreshCardFocus(CardVideo, selected, anySelected);
			RefreshCardFocus(CardDev, selected, anySelected);
			RefreshCardFocus(CardEnterprise, selected, anySelected);
			RefreshCardFocus(CardGeneral, selected, anySelected);
		}
		catch (Exception ex)
		{
			ProfilerViewDebug.Log("  !!! RefreshCardFocus falhou: " + ex.Message);
		}
	}

	private static void RefreshCardFocus(FrameworkElement? card, string selectedToken, bool anySelected)
	{
		if (card == null)
		{
			return;
		}

		bool isChosen = string.Equals(card.Tag as string, selectedToken, StringComparison.Ordinal);
		bool recuar = anySelected && !isChosen;

		// `Opacity` sozinho clareia o card, mas o texto continua nítido demais e
		// a leitura passa a ser "card desabilitado", não "card não escolhido".
		// O BlurEffect é o que produz a sensação de recuo, e o raio é pequeno
		// de propósito: separa o suficiente paraguidar o olho, sem esconder o
		// que o card oferece.
		card.Opacity = recuar ? 0.38 : 1.0;
		card.Effect = recuar
			? new System.Windows.Media.Effects.BlurEffect
			{
				Radius = 2.6,
				KernelType = System.Windows.Media.Effects.KernelType.Gaussian
			}
			: null;
	}

	// ==================================================================
	// LAYOUT — deliberadamente minimalista.
	//
	// ANTES esta view usava Viewbox e depois LayoutTransform/ScaleTransform,
	// com UpdateLayout() dentro de SizeChanged. As duas abordagens quebraram
	// a página (coluna invisível / layout travado), porque:
	//   - Viewbox entrega tamanho INFINITO ao filho, então a largura amarrada
	//     a ActualWidth valia 0 no primeiro passe de medida;
	//   - UpdateLayout() dentro de SizeChanged é reentrância de layout no WPF.
	//
	// AGORA o encaixe é por CONSTRUÇÃO: as linhas do Grid principal estão em
	// Height="*", então os cards ESTICAM para preencher a altura. Não há o que
	// rolar, não há o que encolher, e telas maiores dão cards maiores.
	// A única coisa que sobra em código é recolher o painel do Vorlok IA
	// quando a largura é pequena — e isso só mexe em Visibility/Width,
	// sem medir nada.
	// ==================================================================

	private bool _vorlokCollapsed;

	/// <summary>
	/// Grava no log de diagnóstico o tamanho REAL de cada elemento estrutural.
	/// É o dado que faltava para decidir entre "a view não carregou" e
	/// "carregou com tamanho zero" — as duas falhas parecem idênticas na tela,
	/// mas têm causas completamente diferentes.
	/// </summary>
	private void DumpLayoutDiagnostics(string stage)
	{
		try
		{
			ProfilerViewDebug.Log($"--- LAYOUT DUMP [{stage}] ---");
			ProfilerViewDebug.LogElement("View", this);
			ProfilerViewDebug.LogElement("PageRoot", PageRoot);
			ProfilerViewDebug.Log($"  View.DataContext = {base.DataContext?.GetType().FullName ?? "NULL"}");
			ProfilerViewDebug.Log($"  View.IsLoaded = {IsLoaded}  ActualWidth={ActualWidth} ActualHeight={ActualHeight}");

			ProfilerViewDebug.LogElement("ContentRoot", ContentRoot);
			ProfilerViewDebug.LogElement("MainColumn", MainColumn);
			ProfilerViewDebug.LogElement("UsageCards", UsageCards);
			ProfilerViewDebug.LogElement("StrategyCards", StrategyCards);
			ProfilerViewDebug.LogElement("StrategyTitle", StrategyTitle);
			ProfilerViewDebug.LogElement("HeaderPanel", HeaderPanel);
			ProfilerViewDebug.LogElement("ActionRow", ActionRow);
			ProfilerViewDebug.LogElement("VorlokPanel", VorlokPanel);
			ProfilerViewDebug.LogElement("VorlokFace", VorlokFace);
			ProfilerViewDebug.LogElement("ConfirmButton", ConfirmButton);

			// Quantos RadioButton existem e quais têm tamanho?
			var radios = new System.Collections.Generic.List<System.Windows.Controls.RadioButton>();
			CollectRadioButtons(this, radios);
			ProfilerViewDebug.Log($"  RadioButtons encontrados: {radios.Count}");
			for (int i = 0; i < radios.Count; i++)
			{
				System.Windows.Controls.RadioButton rb = radios[i];
				string token = rb.Tag as string ?? "(sem tag)";
				ProfilerViewDebug.Log($"    RB[{i}] tag='{token}' W={rb.ActualWidth} H={rb.ActualHeight} Vis={rb.Visibility} IsChecked={rb.IsChecked} Parent={rb.Parent?.GetType().Name ?? "null"}");
			}
			ProfilerViewDebug.Log($"--- FIM LAYOUT DUMP [{stage}] ---");
		}
		catch (Exception ex)
		{
			ProfilerViewDebug.Log("  !!! DumpLayoutDiagnostics falhou: " + ex.Message);
		}
	}

	private static void CollectRadioButtons(System.Windows.DependencyObject root, System.Collections.Generic.List<System.Windows.Controls.RadioButton> found)
	{
		int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
		for (int i = 0; i < count; i++)
		{
			System.Windows.DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
			if (child is System.Windows.Controls.RadioButton rb)
			{
				found.Add(rb);
			}
			CollectRadioButtons(child, found);
		}
	}


	private void OnViewSizeChanged(object sender, SizeChangedEventArgs e)
	{
		ApplyResponsiveCollapse();
	}

	private void ApplyResponsiveCollapse()
	{
		try
		{
			if (ContentRoot == null || MainColumn == null)
			{
				return;
			}

			double availW = ContentRoot.ActualWidth;
			if (double.IsNaN(availW) || availW <= 0)
			{
				return;
			}

			// O painel do Vorlok IA é decorativo. Em telas estreitas ele rouba
			// 300px da coluna principal e espreme os cards.
			bool shouldCollapse = availW < 1040;
			if (shouldCollapse == _vorlokCollapsed)
			{
				return;
			}

			_vorlokCollapsed = shouldCollapse;
			if (VorlokPanel != null)
			{
				VorlokPanel.Visibility = shouldCollapse ? Visibility.Collapsed : Visibility.Visible;
			}
			if (VorlokColumn != null)
			{
				VorlokColumn.Width = shouldCollapse ? new GridLength(0) : new GridLength(264);
			}
			MainColumn.Margin = shouldCollapse ? new Thickness(0) : new Thickness(0, 0, 16, 0);

			App.LoggingService?.LogInfo($"[QUESTIONARIO] Layout: largura={availW:F0}px painelVorlokRecolhido={shouldCollapse}");
		}
		catch (Exception ex)
		{
			try
			{
				App.LoggingService?.LogWarning("[QUESTIONARIO] Falha no ajuste responsivo: " + ex.Message);
			}
			catch
			{
			}
		}
	}

	private async void ConfirmButton_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			object dataContext = base.DataContext;
			if (dataContext is ProfilerQuestionnaireViewModel vm)
			{
				// [FIX:BLOCK-ADVANCE] O AVANÇO FICA TRAVADO ATÉ AS DUAS ESCOLHAS
				// =================================================================
				// A tela pede DUAS decisões: o perfil de uso (os 6 cards) e o
				// comportamento do sistema (os 3 botões de performance). Antes,
				// `SubmitAsync` era chamado sem verificar nada, e o后果ado era um
				// perfil salvo pela metade — o pior tipo de bug, porque parece
				// funcionar e só aparece depois, quando o usuário percebe que o
				// comportamento do sistema não é o que ele pediu.
				//
				// POR QUE UM MODAL, E NÃO SÓ DESABILITAR O BOTÃO
				// -----------------------------------------------
				// Desabilitar o botão tornaria a regra invisível: um botão cinza
				// não diz QUALQUER escolha está faltando, e o usuário fica
				// experimentando. O modal diz o que falta e onde. Então o botão
				// NÃO é desabilitado — ele barra o avanço e explica. O que dá
				// "bloqueado" aqui é o resultado, não a aparência.
				//
				// A ordem das mensagens é a ordem em que as escolhas aparecem na
				// tela, então o modal sempre aponta para a primeira pendência.
				var loc = VoltrisOptimizer.Services.LocalizationService.Instance;

				if (string.IsNullOrEmpty(vm.UseCase))
				{
					App.LoggingService?.LogWarning("[QUESTIONARIO] Avanço bloqueado: nenhum perfil de uso selecionado.");
					ModernMessageBox.Show(
						loc["ProfilerBlockUsageTitle"],
						loc["ProfilerBlockUsageMessage"],
						MessageBoxButton.OK,
						MessageBoxImage.Information,
						Window.GetWindow(this));
					return;
				}

				if (string.IsNullOrEmpty(vm.Strategy))
				{
					App.LoggingService?.LogWarning("[QUESTIONARIO] Avanço bloqueado: nenhuma performance selecionada.");
					ModernMessageBox.Show(
						loc["ProfilerBlockStrategyTitle"],
						loc["ProfilerBlockStrategyMessage"],
						MessageBoxButton.OK,
						MessageBoxImage.Information,
						Window.GetWindow(this));
					return;
				}

				vm.Completed -= Vm_Completed;
				vm.Completed += Vm_Completed;
				App.LoggingService?.LogInfo("[QUESTIONARIO] ConfirmButton_Click — chamando SubmitAsync...");
				await vm.SubmitAsync();
				App.LoggingService?.LogInfo("[QUESTIONARIO] SubmitAsync concluído.");
			}
			else
			{
				App.LoggingService?.LogError("[QUESTIONARIO] DataContext inválido no Click!");
			}
		}
		catch (Exception ex2)
		{
			Exception ex = ex2;
			App.LoggingService?.LogError("[QUESTIONARIO] Erro no Click: " + ex.Message, ex);
		}
	}

	private void OnLoaded(object sender, RoutedEventArgs e)
	{
	try
	{
		App.LoggingService?.LogInfo("[QUESTIONARIO] View carregada, registrando evento...");

		// [FIX:CARDS-DIM] Estado inicial do foco dos cards. Sem esta chamada os
		// seis abrem com o valor padrão do XAML; hoje isso já é "nenhum
		// desfocado", mas centralizar aqui garante que a página abra no estado
		// certo mesmo se o estilo voltar a definir Opacity/Effect.
		RefreshCardFocus();

		if (base.DataContext is ProfilerQuestionnaireViewModel profilerQuestionnaireViewModel)
			{
				profilerQuestionnaireViewModel.Completed -= Vm_Completed;
				profilerQuestionnaireViewModel.Completed += Vm_Completed;
				App.LoggingService?.LogSuccess("[QUESTIONARIO] Evento Completed verificado no Loaded.");
			}
			else
			{
				App.LoggingService?.LogError("[QUESTIONARIO] DataContext não é ProfilerQuestionnaireViewModel!");
			}

			// O colapso responsivo precisa rodar já no primeiro desenho: o
			// SizeChanged pode não disparar se o tamanho inicial já for o final.
			ApplyResponsiveCollapse();
			DumpLayoutDiagnostics("LOADED");
		}
		catch (Exception ex)
		{
			App.LoggingService?.LogError("[QUESTIONARIO] Erro ao registrar evento: " + ex.Message);
		}
	}

	private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
	{
		try
		{
			if (e.OldValue is ProfilerQuestionnaireViewModel profilerQuestionnaireViewModel)
			{
				profilerQuestionnaireViewModel.Completed -= Vm_Completed;
				App.LoggingService?.LogInfo("[QUESTIONARIO] Evento antigo desregistrado");
			}
			if (e.NewValue is ProfilerQuestionnaireViewModel profilerQuestionnaireViewModel2)
			{
				profilerQuestionnaireViewModel2.Completed -= Vm_Completed;
				profilerQuestionnaireViewModel2.Completed += Vm_Completed;
				App.LoggingService?.LogSuccess("[QUESTIONARIO] Evento Completed registrado no DataContextChanged");
			}
		}
		catch (Exception ex)
		{
			App.LoggingService?.LogError("[QUESTIONARIO] Erro em OnDataContextChanged: " + ex.Message);
		}
	}

	private void Vm_Completed(object? sender, EventArgs e)
	{
		try
		{
			App.LoggingService?.LogInfo("[QUESTIONARIO] ========================================");
			App.LoggingService?.LogInfo("[QUESTIONARIO] Vm_Completed CHAMADO!");
			App.LoggingService?.LogInfo("[QUESTIONARIO] Perfil Inteligente concluido. Destravingo e indo para o Dashboard...");
			Application current = Application.Current;
			if (current == null)
			{
				return;
			}
			((DispatcherObject)current).Dispatcher.BeginInvoke((Delegate)(Action)delegate
			{
				try
				{
					MainWindow mainWindow = (Window.GetWindow((DependencyObject)(object)this) as MainWindow) ?? (Application.Current?.MainWindow as MainWindow);
					if (mainWindow == null && Application.Current != null)
					{
						foreach (Window window in Application.Current.Windows)
						{
							if (window is MainWindow mainWindow2)
							{
								mainWindow = mainWindow2;
								break;
							}
						}
					}
					if (mainWindow == null)
					{
						App.LoggingService?.LogError("[QUESTIONARIO] MainWindow não encontrada! Não foi possível navegar para o resumo.");
					}
					else
					{
						App.LoggingService?.LogInfo("[QUESTIONARIO] MainWindow encontrada");
						ProfileStore profileStore = new ProfileStore();
						ProfilerState profilerState = profileStore.Load();
						profilerState.TutorialCompleted = true;
						profileStore.Save(profilerState);
						App.LoggingService?.LogInfo("[QUESTIONARIO] Tutorial marcado como completo");

						// O questionário É a última etapa do primeiro uso: a página
						// "Relatório DNA" foi removida e as otimizações que ela aplicava
						// passaram para o primeiro clique do botão circular.
						// Por isso o app é destravado AQUI.
						try
						{
							var onboardingService2 = new VoltrisOptimizer.Services.OnboardingService();
							onboardingService2.MarkOnboardingComplete();
						}
						catch (Exception markEx)
						{
							App.LoggingService?.LogWarning("[QUESTIONARIO] Falha ao marcar onboarding: " + markEx.Message);
						}

						mainWindow.UnlockGate();
						mainWindow.EnableSidebar();
						App.LoggingService?.LogSuccess("[QUESTIONARIO] App destravado apos concluir o Perfil Inteligente.");
						mainWindow.NavigateToDashboard();
					}
				}
				catch (Exception ex2)
				{
					App.LoggingService?.LogError("[QUESTIONARIO] Erro ao navegar: " + ex2.Message, ex2);
				}
			}, Array.Empty<object>());
		}
		catch (Exception ex)
		{
			App.LoggingService?.LogError("[QUESTIONARIO] Erro em Vm_Completed: " + ex.Message, ex);
		}
	}
}
