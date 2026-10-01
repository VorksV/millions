using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.License;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer.Intelligence.Implementation
{
    public class GameProfileSwitcherService
    {
        private static readonly Lazy<GameProfileSwitcherService> _instance = 
            new Lazy<GameProfileSwitcherService>(() => new GameProfileSwitcherService(App.LoggingService!));

        public static GameProfileSwitcherService Instance => _instance.Value;

        private readonly ILoggingService _logger;
        private readonly SemaphoreSlim _switchLock = new SemaphoreSlim(1, 1);
        private IntelligentProfileType? _previousProfile;
        private string? _currentGameProcess;

        private GameProfileSwitcherService(ILoggingService logger)
        {
            _logger = logger;
            _logger.LogEntry("[GameProfileSwitcherService] .ctor");

            // [FIX:LICENSE-IMMEDIATE] A troca por jogo passa a valer IMEDIATAMENTE
            // quando a licença é ativada, sem esperar o próximo jogo abrir.
            //
            // O COMPORTAMENTO ANTERIOR
            // ----------------------------
            // A troca só era consultada dentro de `OnGameDetectedAsync`. Como a
            // licença é checada ali, no momento da DETECÇÃO, ativar a licença no
            // meio de uma sessão não produzia efeito nenhum até o próximo jogo
            // ser aberto — e, se o usuário já estava jogando, nunca produzia
            // efeito naquela sessão.
            //
            // A pergunta que o gate responde é "agora, com a licença paga, o
            // perfil que está em uso é o que este jogo pede?". Se a resposta
            // muda, a decisão tem de ser refeita no ato. Ficar esperando o
            // próximo evento de detecção faz o gate responder a uma pergunta
            // velha.
            //
            // Só reavalia se HÁ jogo em andamento. Sem jogo, não há o que
            // trocar — e mexer no perfil do usuário fora disso seria o
            // contrário do que se pediu, que é respeitar a escolha dele.
            LicenseManager.Instance.LicenseStatusChanged += OnLicenseStatusChanged;

            _logger.LogExit("[GameProfileSwitcherService] .ctor");
        }

        /// <summary>
        /// [FIX:LICENSE-IMMEDIATE] Reavalia o perfil quando a licença muda.
        ///
        /// Só age quando a licença PASSOU a estar paga e existe um jogo em
        /// andamento. A direção oposta — licença removida — não toca no perfil de
        /// propósito: o requisito é que, sem licença, o app volte ao perfil que
        /// o usuário escolheu. E o `OnGameClosedAsync` já restaura o perfil
        /// anterior quando o jogo fecha, que é a janela correta para isso.
        /// </summary>
        private async void OnLicenseStatusChanged(object? sender, EventArgs e)
        {
            try
            {
                if (!ProFeatureGuard.IsPaidActive())
                {
                    _logger.LogDebug("[ProfileSwitcher] Licenca desativada. Perfil atual sera restaurado ao fechar o jogo, sem acao aqui.");
                    return;
                }

                if (string.IsNullOrEmpty(_currentGameProcess))
                {
                    _logger.LogDebug("[ProfileSwitcher] Licenca ativada, mas nenhum jogo em andamento. Nada a reavaliar.");
                    return;
                }

                string process = _currentGameProcess;
                _logger.LogInfo(
                    $"[ProfileSwitcher] Licenca ativada COM jogo em andamento ('{process}'). " +
                    "Reavaliando o perfil imediatamente.");

                await OnGameDetectedAsync(process).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ProfileSwitcher] Erro ao reavaliar perfil apos mudanca de licenca: {ex.Message}");
            }
        }

        public async Task OnGameDetectedAsync(string processName)
        {
            _logger.LogEntry(nameof(OnGameDetectedAsync), ("processName", processName));
            
            await _switchLock.WaitAsync();
            try
            {
                // [FIX:LICENSE-IMMEDIATE] Este guarda É a exceção deliberada que
                // permite a reavaliação por licença.
                //
                // Sem ele, `OnLicenseStatusChanged` chamaria este método com o
                // processo JÁ em andamento e sairia aqui, com
                // "already current" — e ativar a licença não faria nada até o
                // próximo jogo. Esse "já é o jogo atual" era exatamente o que
                // impedia a reavaliação.
                //
                // A distinção é segura porque o resto do método é idempotente:
                // reavaliar o mesmo jogo recalcula a categoria, e o `if
                // (currentProfile != targetProfile)` lá embaixo garante que o
                // perfil só muda se de fato precisar mudar. Nenhuma otimização
                // é aplicada duas vezes — e, quando a licença acabou de ser
                // ativada, a única diferença possível é ter passado de
                // "perfil mantido" para "perfil trocado".
                if (_currentGameProcess == processName)
                {
                    _logger.LogDebug($"[ProfileSwitcher] '{processName}' já é o jogo atual. Reavaliando a decisão de perfil.");
                    _logger.LogExit(nameof(OnGameDetectedAsync), "already current -> reavaliando");
                }

                var currentProfile = SettingsService.Instance.Settings.IntelligentProfile;

                // [FIX:LICENSE-IMMEDIATE] O perfil de referência é guardado
                // ANTES de qualquer troca, e só quando ainda não existe um.
                //
                // Na reavaliação por licença, `_currentGameProcess` JÁ está
                // preenchido, então este bloco não roda e `_previousProfile` é
                // preservado — que é o comportamento certo: o perfil do
                // usuário continua guardado para ser restaurado quando o jogo
                // fechar. Se o guarda fosse removido, a reavaliação sobrescreveria
                // `_previousProfile` com o perfil do JOGO, e fechar o jogo
                // devolveria o usuário ao perfil do jogo em vez do perfil dele.
                if (string.IsNullOrEmpty(_currentGameProcess))
                {
                    _previousProfile = currentProfile;
                    _logger.LogDebug($"[ProfileSwitcher] Perfil de referência guardado: {currentProfile}");
                }

                _currentGameProcess = processName;

                // Classifica o jogo
                _logger.LogDebug($"[ProfileSwitcher] 🔄 Classificando '{processName}'...");
                var category = GameClassificationService.Instance.ClassifyGame(processName);
                _logger.LogDebug($"[ProfileSwitcher] 🏷️ '{processName}' -> Categoria: {category}");

                // Para jogos desconhecidos (General), NÃO troca o perfil — mantém o atual
                if (category == GameClassificationCategory.General)
                {
                    _logger.LogInfo($"[ProfileSwitcher] Jogo '{processName}' não classificado (General). Perfil mantido: {currentProfile}.");
                    _logger.LogExit(nameof(OnGameDetectedAsync), "General category");
                    return;
                }

                var targetProfile = GameClassificationService.Instance.GetProfileForCategory(category);
                _logger.LogDebug($"[ProfileSwitcher] 🎯 Categoria '{category}' -> Perfil alvo: {targetProfile}");

                if (currentProfile != targetProfile)
                {
                    if (!ProFeatureGuard.IsPaidActive())
                    {
                        _logger.LogInfo($"[ProfileSwitcher] Troca automática BLOQUEADA (sem licença Pro) para '{processName}'. Perfil mantido: {currentProfile}.");
                        _logger.LogExit(nameof(OnGameDetectedAsync), "license required");
                        return;
                    }

                    // Altera o perfil e notifica (isso fará SmartEnergy e a UI atualizarem)
                    SettingsService.Instance.Settings.IntelligentProfile = targetProfile;
                    SettingsService.Instance.SaveSettings(); // Salva em disco em bg
                    SettingsService.Instance.NotifyProfileChanged(targetProfile);

                    // Log via telemetria
                    await PerformanceTelemetryService.Instance.LogEventAsync("PROFILE_SWITCH", "GameStarted", new
                    {
                        Process = processName,
                        Category = category.ToString(),
                        PreviousProfile = _previousProfile?.ToString(),
                        NewProfile = targetProfile.ToString()
                    });
                    
                    _logger.LogInfo($"[ProfileSwitcher] Jogo {processName} detectado ({category}). Perfil alterado de {currentProfile} para {targetProfile}.");
                }
                else
                {
                    _logger.LogInfo($"[ProfileSwitcher] Jogo {processName} detectado. Perfil já está em {currentProfile}.");
                }

                // [FIX:PRIORIDADE-UNICA] APLICA O COMPORTAMENTO DE PROCESSO
                // ========================================================
                // Até aqui, a matriz declarava `GameCpuPriority` e
                // `ExcludeGameFromBalancer` e NADA lia esses campos. A troca de
                // perfil mudava só o plano de energia — e, como os valores de
                // CPU da tabela são iguais entre os perfis, não mudava nem isso.
                //
                // A partir daqui o Perfil passa a ter autoridade real sobre o
                // processo do jogo, e essa autoridade tem um dono só
                // (`GamerProcessAuthority`). A prioridade é resolvida por perfil
                // E por categoria de jogo, e é CORRIGIDA pelo regime do
                // instante: numa máquina estrangulada o jogo perde prioridade em
                // vez de ganhar, porque é o que devolve folga ao sistema.
                await ApplyProcessPolicyAsync(processName, targetProfile, category, _logger);
                _logger.LogExit(nameof(OnGameDetectedAsync), "Success");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ProfileSwitcher] Erro ao trocar perfil: {ex.Message}");
                _logger.LogExit(nameof(OnGameDetectedAsync), $"Error: {ex.Message}");
            }
            finally
            {
                _switchLock.Release();
            }
        }

        public async Task OnGameClosedAsync(string processName)
        {
            _logger.LogEntry(nameof(OnGameClosedAsync), ("processName", processName));
            
            await _switchLock.WaitAsync();
            try
            {
            // Guarda ORIGINAL, restaurado sem a exceção que eu havia colocado aqui
            // por engano.
            //
            // Este método é o de FECHAMENTO. A reavaliação por licença é feita em
            // `OnGameDetectedAsync`; ela não passa por aqui. E o guarda é
            // estritamente necessário: ele ignora o fechamento de sub-processos
            // que não são o jogo ativo — o .exe helper de um jogo, por exemplo.
            // Sem ele, fechar um helper restauraria o perfil do usuário no meio
            // da sessão, com o jogo ainda rodando.
            if (_currentGameProcess != processName)
            {
                _logger.LogDebug($"[ProfileSwitcher] '{processName}' não é o jogo ativo (atual='{_currentGameProcess}'). Ignorando.");
                _logger.LogExit(nameof(OnGameClosedAsync), "not active game");
                return; // Ignora se não for o jogo ativo (ex: um sub-processo fechou)
            }

                var currentProfile = SettingsService.Instance.Settings.IntelligentProfile;
                var targetProfile = _previousProfile ?? IntelligentProfileType.GeneralBalanced;
                
                _logger.LogDebug($"[ProfileSwitcher] 🔄 Restaurando perfil: atual={currentProfile}, alvo={targetProfile}");

                _currentGameProcess = null;
                _previousProfile = null;

                if (currentProfile != targetProfile)
                {
                    // Restaura o perfil e notifica
                    SettingsService.Instance.Settings.IntelligentProfile = targetProfile;
                    SettingsService.Instance.SaveSettings();
                    SettingsService.Instance.NotifyProfileChanged(targetProfile);

                    // Log via telemetria
                    await PerformanceTelemetryService.Instance.LogEventAsync("PROFILE_SWITCH", "GameClosed", new
                    {
                        Process = processName,
                        RestoredProfile = targetProfile.ToString()
                    });
                    
                    _logger.LogInfo($"[ProfileSwitcher] Jogo {processName} fechado. Perfil restaurado para {targetProfile}.");
                }

                // [FIX:PRIORIDADE-UNICA] DEVOLVE A PRIORIDADE AO DONO ORIGINAL
                //
                // O perfil voltou ao do usuário, então a decisão de processo que o
                // Perfil tomou para o jogo também tem que ser desfeita. A
                // autoridade devolve o valor ORIGINAL — não "Normal" — porque o
                // processo pode já estar em outro valor por outro motivo, e
                // nesse caso quem elevou aquilo não foi o Perfil.
                RestoreProcessPolicy(processName, _logger);

                _logger.LogExit(nameof(OnGameClosedAsync), "Success");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ProfileSwitcher] Erro ao restaurar perfil: {ex.Message}");
                _logger.LogExit(nameof(OnGameClosedAsync), $"Error: {ex.Message}");
            }
            finally
            {
                _switchLock.Release();
            }
        }

        /// <summary>
        /// [FIX:PRIORIDADE-UNICA] A LIGAÇÃO ENTRE O PERFIL E O PROCESSO DO JOGO
        /// =====================================================================
        /// Resolve a prioridade pela <see cref="GamerProcessPolicy"/> e entrega
        /// à <see cref="GamerProcessAuthority"/>, que é a única que escreve.
        ///
        /// Três decisões acontecem aqui, e todas são deliberadas:
        ///
        /// 1. O contexto de REGIME vem do Brain (mesmo snapshot da energia). Não
        ///    é um segundo sensor nem uma segunda medição: é a mesma leitura,
        ///    usada pela mesma política. É isso que garante que o Perfil não
        ///    promote o processo no instante em que está recuando a energia.
        ///
        /// 2. O TIER vem do mesmo retrato da máquina. Também não é medido de
        ///    novo.
        ///
        /// 3. A prioridade DECLARADA vem da linha da matriz do perfil. É a
        ///    primeira vez que esse campo tem leitor.
        /// </summary>
        private static async Task ApplyProcessPolicyAsync(
            string processName,
            IntelligentProfileType targetProfile,
            GameClassificationCategory category,
            ILoggingService logger)
        {
            try
            {
                var capability = Power.HardwareCapabilityProbe.Get(logger, forceRefresh: false);
                var row = Power.ProfilePowerMatrix.Resolve(targetProfile, capability.Tier);
                var snapshot = Core.Brain.V2.AntiStutter.AntiStutterSnapshotHub.Current();
                var context = Power.EnergyRegimeClassifier.FromSnapshot(snapshot, capability.Tier);

                bool isGamerProfile =
                    category != GameClassificationCategory.General;

                var plan = Power.GamerProcessPolicy.Resolve(
                    declaredPriority: row.GameCpuPriority,
                    tier: capability.Tier,
                    context: context,
                    isGamerProfile: isGamerProfile);

                logger?.LogInfo(
                    $"[ProfileSwitcher] Politica de processo para '{processName}' " +
                    $"({category}, perfil={targetProfile}): {plan}");

                int[] pids = FindGamePids(processName);

                if (pids.Length == 0)
                {
                    logger?.LogWarning(
                        $"[ProfileSwitcher] Nenhum processo vivo chamado '{processName}'; politica calculada mas nao aplicada.");
                    return;
                }

                foreach (int pid in pids)
                {
                    Power.GamerProcessAuthority.Apply(pid, plan, processName, logger);
                }

                await Task.CompletedTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(
                    $"[ProfileSwitcher] Falha ao aplicar a politica de processo de '{processName}': {ex.Message}");
            }
        }

        /// <summary>
        /// Devolve a prioridade original de todos os processos do jogo.
        /// </summary>
        private static void RestoreProcessPolicy(string processName, ILoggingService logger)
        {
            try
            {
                foreach (int pid in FindGamePids(processName))
                {
                    Power.GamerProcessAuthority.Restore(pid, processName, logger);
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning(
                    $"[ProfileSwitcher] Falha ao restaurar a prioridade de '{processName}': {ex.Message}");
            }
        }

        /// <summary>
        /// PIDs vivos do processo do jogo. Pode haver mais de um (lançadores,
        /// janelas de configuração, anti-cheat): o Perfil trata todos, porque
        /// decidir por um só deixaria o resto do jogo fora da política.
        /// </summary>
        private static int[] FindGamePids(string processName)
        {
            var pids = new List<int>();

            foreach (var proc in System.Diagnostics.Process.GetProcessesByName(processName))
            {
                try
                {
                    if (!proc.HasExited) pids.Add(proc.Id);
                }
                catch { }
                finally { proc.Dispose(); }
            }

            return pids.ToArray();
        }
    }
}
