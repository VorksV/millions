using System;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core.Updater;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.UpdateFlow;

// [FIX:NOME-COLIDINDO] EXISTEM DUAS CLASSES `UpdateInfo` NO PROJETO.
//
//     VoltrisOptimizer.Core.Updater.UpdateInfo     <- a que o UpdateService usa
//     VoltrisOptimizer.Services.UpdateInfo          <- outra, em namespace irmão
//
// Como este arquivo mora em `VoltrisOptimizer.Services.Update`, o nome simples
// `UpdateInfo` resolvia para a do namespace pai e não para a do `Core.Updater`.
// O compilador apontava 19 erros, todos do mesmo motivo.
//
// O alias é a correção explícita e deixa a intenção visível: o tipo que este
// fluxo carrega é SEMPRE o do `Core.Updater`, o mesmo que a janela de
// atualização antiga usa. Trocar de tipo aqui significaria passar a
// manusear um objeto que o `UpdateService` nunca receberia — e o download
// falharia em tempo de execução, que é pior do que falhar agora.
using UpdateInfo = VoltrisOptimizer.Core.Updater.UpdateInfo;

namespace VoltrisOptimizer.Services.UpdateFlow
{
    /// <summary>
    /// [FIX:BOTAO-MORTO-POR-ESPERA] POR QUE O RETORNO MUDOU DE TIPO.
    /// ============================================================
    /// Este método devolvia <c>bool</c>, e o <c>false</c> significava três
    /// coisas diferentes ao mesmo tempo: "não há atualização", "já existe uma
    /// verificação em andamento" e "a detecção falhou".
    ///
    /// Um `bool` que carrega três significados obriga o chamador a adivinhar.
    /// E o primeiro palpite que alguém faz — inclusive quem escreveu — é tratar
    /// tudo como "não há atualização", que é a única das três que é uma boa
    /// notícia. O resultado foi um programa dizendo ao usuário que estava
    /// atualizado enquanto estava ocupado, e o botão de simulação Recusando o
    /// clique em silêncio.
    ///
    /// Pior, a "correção" seguinte piorou: um guarda que devolvia cedo quando
    /// já havia execução em andamento. Era defensável no papel, mas na prática
    /// transformou qualquer flag travada em um botão que nunca mais funciona.
    /// Um botão que às vezes não responde é pior do que um botão que responde
    /// com uma frase honesta.
    ///
    /// A resposta certa não é impedir a ação — é devolver o que aconteceu, e
    /// deixar quem chamou decidir. Por isso o retorno é um enum, e não um
    /// palpite codificado num `bool`.
    /// </summary>
    public enum UpdateRunOutcome
    {
        /// <summary>A atualização foi detectada e o download começou.</summary>
        Started = 0,

        /// <summary>Já havia uma verificação ou atualização em andamento.</summary>
        AlreadyRunning = 1,

        /// <summary>A consulta foi feita e não há versão nova.</summary>
        UpToDate = 2,

        /// <summary>A consulta falhou. O motivo já foi registrado no log.</summary>
        Failed = 3
    }

    /// <summary>
    /// [UPDATE-FLUXO] QUEM ORQUESTRA A ATUALIZAÇÃO PELO BOTÃO CIRCULAR.
    ///
    /// A REGRA INEGOCIÁVEL DESTE ARQUIVO
    /// ===================================
    /// <see cref="UpdateService"/> NÃO É ALTERADO. Nem uma linha.
    ///
    /// É por isso que a detecção continua funcionando para quem usa uma versão
    /// antiga: se este fluxo reescrevesse a comparação de versão, ou mudasse a
    /// URL do repositório, ou o `IsNewerThan`, um usuário na 1.0.1.0 poderia
    /// deixar de ver a 1.0.2.0 — e o sintoma seria "o programa parou de
    /// atualizar", que é a falha mais cara que existe num produto que se
    /// atualiza sozinho.
    ///
    /// Aqui só existe uma coisa: CHAMAR o serviço e OBSERVAR o progresso. A
    /// detecção entra por <c>CheckForUpdatesAsync()</c> e a instalação por
    /// <c>StartAutoUpdateAsync()</c>, exatamente como a janela antiga faz. O
    /// que muda é para ONDE o progresso aparece, não COMO ele é obtido.
    ///
    /// POR QUE ISSO NÃO É UM "REFATOR"
    /// ===============================
    /// Porque a janela antiga e este fluxo fazem o mesmo trabalho em paralelo.
    /// A janela continua existindo e continua funcionando — ela é usada pela
    /// verificação automática do startup, que não foi mexida. Os dois chamam o
    /// mesmo serviço. Nenhum interfere no outro.
    /// </summary>
    public static class UpdateFlowController
    {
        private const string Tag = "[UpdateFlow]";

        private static CancellationTokenSource? _cts;

        /// <summary>Token da operação na barra global, aberto no início do download.</summary>
        private static VoltrisOptimizer.Services.OperationToken? _globalToken;

        /// <summary>Caminho do arquivo baixado, aguardando autorização para aplicar.</summary>
        private static string? _pendingDownload;

        /// <summary>Objeto da atualização detectada, para a aplicação final.</summary>
        private static UpdateInfo? _pendingInfo;

        /// <summary>
        /// Se existe um download concluído esperando o usuário autorizar.
        ///
        /// A trava de interface é LIBERADA neste estado, e é por isso que o
        /// botão precisa conseguir responder: sem esta propriedade, o programa
        /// ficaria num limbo em que a tela voltou a funcionar mas nada explica
        /// por que ela voltou.
        /// </summary>
        public static bool HasPendingApply => !string.IsNullOrEmpty(_pendingDownload);

        /// <summary>
        /// O pacote baixado, aguardando autorização.
        ///
        /// É exposto para que a `MainWindow` possa passar ao
        /// <see cref="ApplyAndRestartAsync"/>. Existe como propriedade, e não
        /// fica escondido atrás de um método que busca, para que o caminho do
        /// clique seja LIDO de ponta a ponta: a tela mostra o botão, o botão
        /// entrega o caminho, e o serviço decide.
        ///
        /// Sem `lock` próprio: estes campos são escrita UMA vez, pelo laço de
        /// download, e lidos pela thread da interface. A garantia que importa
        /// não é exclusão mútua, é a de ORDEM — o botão só fica visível em
        /// `ReadyToApply`, que só é alcançado DEPOIS da escrita. Uma trava aqui
        /// daria aparência de segurança sem impedir nenhuma corrida real.
        /// </summary>
        public static string? PendingDownloadPath => _pendingDownload;

        /// <summary>A atualização detectada, para a aplicação final.</summary>
        public static UpdateInfo? PendingUpdateInfo => _pendingInfo;

        /// <summary>
        /// Reserva o direito de rodar o download.
        ///
        /// [FIX:CORRIDA] `IsRunning` sozinho não fecha a janela entre ler e
        /// gravar. Duas cliques muito próximos — dois toques, ou o duplo clique
        /// de um mouse mal configurado — podem ler `IsRunning == false` nos dois
        /// e iniciar dois downloads do mesmo arquivo ao mesmo tempo.
        ///
        /// O `Interlocked.CompareExchange` faz a troca ser uma operação só: o
        /// segundo chamador perde a disputa e sai, sem esperar e sem meio-termo.
        /// E `== 1` no retorno é o teste que prova que ESTE chamador ganhou.
        /// </summary>
        /// [FIX:FLAG-JAMA-RESETADA] O TRAVAO NUNCA VOLTAVA A ZERO.
        /// =====================================================
        /// Esta comparação de troca é o que impede dois downloads do mesmo
        /// arquivo ao mesmo tempo, e ela funciona. O problema é o que ninguém
        /// escreveu do outro lado: <c>Finish()</c> desligava <c>IsRunning</c>
        /// e nunca devolvia <c>_runningFlag</c> a zero.
        ///
        /// O efeito é pior do que "dois downloads juntos". Depois da PRIMEIRA
        /// atualização da sessão, o valor fica 1 para sempre, e todo
        /// <c>RunAsync</c> seguinte é rejeitado com "Já existe uma atualização
        /// em andamento" — mesmo não havendo nada em andamento. O usuário
        /// ficava com um botão de atualização que funcionava uma vez e nunca
        /// mais, e a mensagem dizia exatamente o contrário do que era verdade.
        ///
        /// E havia a causa que enableceu isso: DOIS estados para a mesma
        /// pergunta. <c>_runningFlag</c> decidia quem entrava, e
        /// <c>IsRunning</c> era o que todo mundo lia. Assim que os dois
        /// divergiram, o segundo passou a mentir.
        ///
        /// A correção não é só devolver o int a zero. É apagar a segunda fonte
        /// da verdade: <see cref="IsRunning"/> passa a LER o int, e não a ter
        /// uma cópia própria que pode ficar para trás. Duas variáveis para
        /// "está rodando?" é a forma mais barata de criar um defeito que só
        /// aparece na segunda execução.
        /// </summary>
// [FIX:BOOLEANO-INVERTIDO] `== 1` ESTAVA DE TRAVADO.
            // ===============================================
            // Esta comparação de troca é o que impede dois downloads do mesmo
            // arquivo ao mesmo tempo, e ela funciona. O problema é o que ninguém
            // escreveu do outro lado: <c>Finish()</c> desligava <c>IsRunning</c>
            // e nunca devolvia <c>_runningFlag</c> a zero.
            //
            // O efeito é pior do que "dois downloads juntos". Depois da PRIMEIRA
            // atualização da sessão, o valor fica 1 para sempre, e todo
            // <c>RunAsync</c> seguinte é rejeitado com "Já existe uma atualização
            // em andamento" — mesmo não havendo nada em andamento. O usuário
            // ficava com um botão de atualização que funcionava uma vez e nunca
            // mais, e a mensagem dizia exatamente o contrário do que era verdade.
            //
            // E havia a causa que enableceu isso: DOIS estados para a mesma
            // pergunta. <c>_runningFlag</c> decidia quem entrava, e
            // <c>IsRunning</c> era o que todo mundo lia. Assim que os dois
            // divergiram, o segundo passou a mentir.
            //
            // A correção não é só devolver o int a zero. É apagar a segunda fonte
            // da verdade: <see cref="IsRunning"/> passa a LER o int, e não a ter
            // uma cópia própria que pode ficar para trás. Duas variáveis para
            // "está rodando?" é a forma mais barata de criar um defeito que só
            // aparece na segunda execução.
            /// </summary>
        private static bool TryReserveRun()
        {
            // [FIX:BOOLEANO-INVERTIDO] ESTE `== 1` ERA O DEFEITO INTEIRO.
            // =================================================
            // `Interlocked.CompareExchange` grava o valor novo e DEVOLVE O
            // VALOR ANTIGO. Então:
            //
            //   trava livre  (antigo = 0): grava 1, devolve 0 ->  `won = false`
            //   trava ocupada(antigo = 1): não grava, devolve 1 -> `won = true`
            //
            // O método devolvia VERDADEIRO quando PERDIA a disputa, e FALSO
            // quando a ganhava. Tudo o que vinha depois obedecera esse
            // resultado invertido:
            //
            // - o clique que ganhava a trava era recusado, e a trava ficava
            //   ligada para sempre, porque quem recusou não entra no `finally`
            //   que a devolve;
            // - o clique que PERDIA é que partia para o download;
            // - e o sintoma virava "às vezes funciona": um clique sim, um não.
            //
            // Foi por isso que o botão de simulação recusava com "já existe uma
            // atualização em andamento" mesmo com a trava que eu acabara de
            // zerar na linha de cima, e por isso que eu passei rodadas
            // procurando um segundo escritor que nunca existiu.
            //
            // O conserto é comparar com ZERO, que é o valor que significa "a
            // trava estava livre quando eu cheguei".
            if (Volatile.Read(ref _runningFlag) == 1 && _cts == null)
            {
                MarkGlobal("TRAVA ORFA detectada (flag=1 sem CTS) | recuperando");
                Interlocked.Exchange(ref _runningFlag, 0);
            }

            // O valor devolvido é o ANTERIOR. Zero = estava livre = ganhei.
            int anterior = Interlocked.CompareExchange(ref _runningFlag, 1, 0);
            bool won = anterior == 0;

            // Registro de qual chamada ganhou. Sem isto, um "pedido ignorado"
            // no log é indistinguível de um clique duplo (comportamento
            // correto) de uma trava presa (defeito), e são exatamente o oposto
            // para quem está tentando entender o que aconteceu.
            MarkGlobal($"reserva de execucao | ganhou={won} | flag_antes={anterior}");

            return won;
        }

        private static int _runningFlag;

        /// <summary>
        /// Se o fluxo de atualização já está em andamento.
        ///
        /// Lê o mesmo inteiro que <see cref="TryReserveRun"/> decide, e não uma
        /// cópia: um espelho pode divergir, e um espelho que diverge mente
        /// justamente quando alguém olha.
        /// </summary>
        public static bool IsRunning
        {
            get { return Volatile.Read(ref _runningFlag) == 1; }
        }

        /// <summary>
        /// Executa o fluxo completo a partir de uma verificação.
        ///
        /// O retorno é <c>true</c> se havia atualização, <c>false</c> se o
        /// programa já está na versão mais recente. Serve para o chamador
        /// mostrar "você já está atualizado" sem precisar repetir a consulta.
        ///
        /// [FIX:NAVEGACAO-TARDE] ESTE MÉTODO NÃO ESPERA O DOWNLOAD.
        /// =====================================================
        /// A primeira versão fazia <c>await</c> de tudo e devolvia só no fim.
        /// A consequência foi que a navegação para o Dashboard — que o
        /// chamador faz logo DEPOIS desta chamada, achando que o fluxo já tinha
        /// começado — só acontecia com tudo terminado.
        ///
        /// O usuário ficava na tela de Configurações durante o download
        /// inteiro, o botão circular nunca aparecia girando, e a única coisa que
        /// ele via era a trava da interface, sem nenhuma explicação do motivo.
        ///
        /// A correção é devolver assim que a ATUALIZAÇÃO É DETECTADA, e deixar
        /// o download rodando em segundo plano. A detecção é rápida (uma
        /// consulta); o download é o que demora — e é o que o botão circular
        /// precisa estar mostrando.
        ///
        /// O download continua sendo rastreado por <c>IsRunning</c>, que impede
        /// dois downloads simultâneos, e por <c>Cancel()</c>, que aborta.
        /// </summary>
        /// <param name="simulate">
        /// Quando <c>true</c>, NENHUMA chamada de rede é feita: nem a detecção
        /// nem o download. O fluxo roda inteiro com progresso sintético, o que
        /// permite testar a interface sem baixar centenas de MB e sem abrir
        /// instalador.
        /// </param>
        public static async Task<UpdateRunOutcome> RunAsync(bool simulate, ILoggingService? logger = null)
        {
            // [FIX:SIMULACAO-NUNCA-RECUSADA] O BOTÃO DE TESTE NÃO PODE
            // DEPENDER DE ESTADO DE OUTRO.
            // =================================================
            // A simulação usava a MESMA trava da verificação real, e por isso
            // um clique era recusado com "já existe uma atualização em
            // andamento" — quando o que existia era uma consulta de rede que
            // o próprio programa tinha disparado sozinha no startup, e que o
            // usuário não podia ver, não podia cancelar e não sabia que
            // existia.
            //
            // Isso é a definição de um botão de teste que não funciona: o seu
            // resultado passa a depender de uma condição que o teste não
            // controla. Dois testes seguidos, ou um teste logo depois de abrir
            // o programa, dão resultados diferentes — e um botão assim não é
            // uma ferramenta, é uma loteria.
            //
            // A simulação representa NENHUMA operação real. Ela não consulta o
            // GitHub, não baixa, não instala. Nada que ela displace pode
            // quebrar: no máximo, um download real em andamento é abandonado,
            // e isso é registrado explicitamente para não ser silencioso.
            if (simulate)
            {
                if (Volatile.Read(ref _runningFlag) == 1)
                {
                    MarkGlobal("SIMULACAO: substituindo fluxo anterior em andamento");
                }

                Interlocked.Exchange(ref _runningFlag, 0);
                _cts = null;
                _pendingDownload = null;
                _pendingInfo = null;
            }

            if (!TryReserveRun())
            {
                logger?.LogInfo($"{Tag} Já existe uma atualização em andamento; pedido ignorado.");
                return UpdateRunOutcome.AlreadyRunning;
            }

            // [FIX:EXCECAO-TRAVA-A-FLAG-PARA-SEMPRE]
            // =============================================
            // Este `try` não existia, e a sua ausência é um vazamento de trava
            // com o MESMO sintoma do defeito anterior, por outra porta.
            //
            // A reserva acontece na linha de cima. Se qualquer coisa entre ela e
            // o `Task.Run` lançar — e a operação mais provável de lançar é
            // `CheckForUpdatesAsync()`, porque é rede — a trava CONTINUA
            // ligada, porque só o `finally` do download a desliga, e esse
            // `finally` nunca chega a ser criado: a exceção sai do método
            // antes.
            //
            // Quem recebia a exceção era o `catch` da `SettingsView`, que
            // escreve a mensagem num `TextBlock` da tela. Ou seja: o programa
            // mostrava o erro, o log não registrava nada, e a trava ficava
            // presa. O próximo clique recebia "Já existe uma atualização em
            // andamento" — sendo que não existia nada em andamento, e nada
            // voltaria a rodar até o programa ser fechado.
            //
            // É por isso que o log mostrava a flag em 1 sem NENHUMA reserva
            // vencedora antes dela: a reserva vencedora existiu, a exceção
            // foi engolida por quem só escreve na tela, e ninguém nunca mais
            // desceu a dívida.
            try
            {
            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;

            UpdateInfo? info;

            if (simulate)
            {
                info = BuildSimulatedInfo();
                logger?.LogInfo($"{Tag} MODO SIMULAÇÃO: nenhuma chamada de rede será feita.");
            }
            else
            {
                // ÚNICO ponto de contato com a detecção. Intocado.
                info = await UpdateService.CheckForUpdatesAsync().ConfigureAwait(false);
            }

            if (info == null)
            {
                logger?.LogInfo($"{Tag} Nenhuma atualização disponível.");
                Finish();
                return UpdateRunOutcome.UpToDate;
            }

            // ---------- O BOTÃO CIRCULAR ASSUME ----------
            //
            // Antes de devolver, para que o chamador possa navegar com o
            // estado já publicado.
            UpdateFlowState.Begin(info.LatestVersion, simulate);
            Report(logger, $"Atualização detectada: {info.LatestVersion}");

            // ---------- DOWNLOAD EM SEGUNDO PLANO ----------
            //
            // `_ = Task.Run(...)` e NÃO `await`. Esta é a linha que faz o
            // botão circular aparecer enquanto o arquivo baixa. Com `await`
            // aqui, o chamador não conseguiria navegar a tempo.
            CancellationTokenSource cts = _cts;
            _ = Task.Run(async () =>
            {
                try
                {
                    await DownloadAndWaitAsync(info, simulate, cts.Token, logger).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger?.LogError($"{Tag} Falha no download: {ex.Message}");
                    UpdateFlowState.Fail();
                }
                finally
                {
                    Finish();
                }
            }, token);

            return UpdateRunOutcome.Started;
            }
            catch (Exception ex)
            {
                // [FIX:EXCECAO-TRAVA-A-FLAG-PARA-SEMPRE] A DÍVIDA É PAGA AQUI.
                //
                // Sem este `catch`, a falha da detecção deixava a trava ligada e
                // o botão de atualização morria para o resto da sessão. Com ele,
                // a falha vira três coisas que o usuário entende: um estado de
                // erro visível, uma linha no log, e a trava devolvida para que
                // ele pueda tentar de novo.
                logger?.LogError($"{Tag} Falha ao iniciar o fluxo: {ex.GetType().Name}: {ex.Message}");
                Report(logger, "Falha ao iniciar o fluxo de atualização.");

                UpdateFlowState.Fail();
                Finish();

                return UpdateRunOutcome.Failed;
            }
        }

        /// <summary>
        /// Baixa, publica o progresso e ESPERA autorização. É o corpo do
        /// download, separado para que o caminho de retorno do fluxo e o
        /// trabalho de fundo não fiquem entrelaçados.
        /// </summary>
        private static async Task DownloadAndWaitAsync(
            UpdateInfo info,
            bool simulate,
            CancellationToken token,
            ILoggingService? logger)
        {
            UpdateFlowState.StartDownload();

            string? downloaded = simulate
                ? await SimulateDownloadAsync(token).ConfigureAwait(false)
                : await DownloadRealAsync(info, logger).ConfigureAwait(false);

            if (string.IsNullOrEmpty(downloaded))
            {
                UpdateFlowState.Fail();
                Report(logger, "Falha no download.");
                return;
            }

            // Atingir 100% NÃO instala. Ficar à espera é o que evita o
            // reinício surpresa — e é o estado que a simulação permite
            // testar sem que o instalador feche o programa.
            UpdateFlowState.ReadyToApply();
            Report(logger, "Download concluído. Aguardando autorização do usuário.");

            _pendingDownload = downloaded;
            _pendingInfo = info;
        }

        /// <summary>
        /// [FIX:SEMAFORO] Encerra o fluxo.
        ///
        /// A primeira versão chamava `Gate.Release()` aqui, mas o
        /// `Gate.WaitAsync()` correspondente tinha sumido quando o download foi
        /// movido para segundo plano — o método passou a devolver logo após
        /// detectar a atualização, e não entrava mais no semáforo.
        ///
        /// O resultado era `SemaphoreFullException` a cada atualização, dentro
        /// do `finally` do download — que é a pior posição possível para uma
        /// exceção: ela aparece DEPOIS de o fluxo ter funcionado, e o log passa
        /// a mostrar um erroStack_trace que parece vir do download, quando na
        /// verdade é só a limpeza.
        ///
        /// O semáforo foi removido de vez, e não corrigido. Ele existia para
        /// serializar o método inteiro; agora o que serializa é o campo
        /// `IsRunning`, que é verificável e não tem estado interno para
        /// desbalancear. A trava do `Interlocked` abaixo mantém a janela entre
        /// "checou que estava livre" e "marcou como ocupado" fechada.
        /// </summary>
        private static void Finish()
        {
            // [FIX:FLAG-JAMA-RESETADA] DEVOLVER A TRAVA. É AQUI.
            //
            // Sem esta linha, a primeira atualização da sessão não aceita
            // nenhuma outra. Ver a nota longa em `TryReserveRun`.
            Interlocked.Exchange(ref _runningFlag, 0);

            MarkGlobal("execucao encerrada | flag devolvida a 0");

            try
            {
                _cts?.Dispose();
            }
            catch
            {
                // Descartar um CTS cancelado não pode falhar o encerramento.
            }
            finally
            {
                _cts = null;
            }
        }

        /// <summary>
        /// Cancela o fluxo em andamento.
        ///
        /// Só é chamado em estado que não bloqueia a interface, porque a
        /// intenção é justamente devolver o controle ao usuário.
        /// </summary>
        public static void Cancel()
        {
            try
            {
                _cts?.Cancel();
            }
            catch
            {
                // Cancelar nunca pode ser a origem de uma nova falha.
            }

            _pendingDownload = null;
            _pendingInfo = null;

            // [FIX:CANCEL-NAO-DEVOLVIA-A-TRAVA] O MESMO VAZAMENTO, OUTRA PORTA.
            // ==================================================================
            // `Cancel()` aborta o download e zera o estado da interface, mas
            // não tocava na trava. O download em andamento vê o token
            // cancelado, sai pelo `catch`/`finally` e chama `Finish()` — o que
            // na maior parte das vezes salva. Mas o download JÁ pode ter
            // terminado antes do cancelamento, e nesse caso ninguém mais
            // desce a dívida: o estado diz "cancelado" e a trava diz "em
            // andamento", para sempre.
            //
            // `Finish()` é idempotente — devolve a trava e descarta o token
            // — então chamá-lo aqui não pode estragar o `finally` do download
            // quando os dois acontecem. Chamar as duas vezes é seguro; chamar
            // nenhuma vez trava o botão.
            Finish();

            UpdateFlowState.Reset();
        }

        /// <summary>
        /// Aplica a atualização baixada e reinicia. Só é chamado depois de
        /// autorização explícita do usuário, e é o ÚNICO caminho que reinicia.
        ///
        /// Recusa explicitamente quando não há nada autorizado a aplicar. Sem
        /// essa guarda, um clique aqui com o estado errado passaria um caminho
        /// nulo para o `UpdateService` e falharia lá dentro, com uma mensagem
        /// que não diria nada sobre a causa.
        /// </summary>
        public static async Task<bool> ApplyAndRestartAsync(
            UpdateInfo info,
            string localFilePath,
            ILoggingService? logger = null)
        {
            if (UpdateFlowState.SimulationEnabled)
            {
                // [FIX:RECUSA-SILENCIOSA] A SIMULAÇÃO AGORA RESPONDE NA TELA.
                // ========================================================
                // Antes disto a recusa existia só no log. O usuário clicava no
                // botão, o programa não reininhava — corretamente — e não
                // aparecia NADA. Um caminho sem saída e sem explicação é
                // indistinguível de um botão quebrado, e é por isso que a
                // reclamação foi "nada acontece" em vez de "a simulação
                // recusa instalar".
                //
                // `SetProgress` e não `Fail`: o estágio continua sendo
                // `ReadyToApply`, que é o que mantém o botão visível e a
                // interface utilizável. Só o texto muda, para explicar que a
                // ausência de reinício É o resultado esperado do teste.
                UpdateFlowState.SetProgress(100, "UpdateFlowSimRefused");

                logger?.LogInfo($"{Tag} SIMULAÇÃO: a aplicação foi ignorada de propósito (é o comportamento esperado).");
                return false;
            }

            if (info == null || string.IsNullOrEmpty(localFilePath))
            {
                logger?.LogWarning($"{Tag} Aplicação solicitada sem download autorizado. Ignorando.");
                return false;
            }

            try
            {
                // Intocado — mesma chamada da janela antiga.
                return await UpdateService.StartAutoUpdateAsync(info, localFilePath)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger?.LogError($"{Tag} Falha ao aplicar a atualização: {ex.Message}");
                UpdateFlowState.Fail();
                return false;
            }
        }

        // ------------------------------------------------------------------
        // DOWNLOAD
        // ------------------------------------------------------------------

        private static async Task<string?> DownloadRealAsync(
            UpdateInfo info,
            ILoggingService? logger)
        {
            // O progresso vem do MESMO `IProgress<double>` que a janela antiga
            // usava. É a prova de que a detecção e o download não foram
            // reimplementados: só mudou para quem o número é publicado.
            var progress = new Progress<double>(p =>
            {
                UpdateFlowState.SetProgress(p);
                PublishGlobal(p, UpdateFlowState.StatusKey);
            });

            return await UpdateService.DownloadUpdateAsync(info, progress).ConfigureAwait(false);
        }

        private static async Task<string?> SimulateDownloadAsync(CancellationToken ct)
        {
            // Mesmo caminho do download real, com o tempo encurtado para que o
            // teste não demore minutos. A curva é não-linear de propósito: os
            // downloads reais quase param nos últimos 20%, e um teste com
            // velocidade constante não exercita o trecho em que o arco
            // realmente trabalha.
            double[] curve =
            {
                0.4, 1.2, 2.6, 4.3, 6.5, 9.1, 12.4, 16.2, 20.8, 26.0,
                32.1, 39.0, 46.4, 54.2, 62.3, 70.1, 77.4, 84.0, 89.6, 93.8,
                96.7, 98.4, 99.3, 99.8, 100.0
            };

            foreach (double step in curve)
            {
                if (ct.IsCancellationRequested) return null;

                UpdateFlowState.SetProgress(step);
                PublishGlobal(step, UpdateFlowState.StatusKey);

                await Task.Delay(180, ct).ConfigureAwait(false);
            }

            return "(simulado) pacote de teste";
        }

        // ------------------------------------------------------------------
        // APOIO
        // ------------------------------------------------------------------

        /// <summary>
        /// Espelha o progresso na barra global, para sidebar, header e o resto
        /// da interface acompanharem o mesmo número do arco.
        ///
        /// [FIX:BARRA-GLOBAL] ELA PRECISA DE UMA OPERAÇÃO ABERTA.
        /// =======================================================
        /// A primeira versão chamava só `UpdateProgress`, e a barra não
        /// reagia. A razão é que `GlobalProgressService` não é um mostrador
        /// livre: ele administra uma FILA de operações, e `UpdateProgress` sem
        /// operação em andamento é descartado. Não é defeito do serviço — é o
        /// contrato dele, e a culpa era de quem chamou.
        ///
        /// Por isso há agora um `BeginGlobalOperation` no início do download e
        /// um `EndGlobalOperation` no fim, e o token fica guardado para que as
        /// atualizações usem a MESMA operação em vez de criar uma por
        /// progresso (o que enche a fila de operações de 1% que nunca terminam).
        /// </summary>
        /// <summary>
        /// [FIX:BARRA-NAO-USAR-A-FILA] POR QUE ESTE MÉTODO ESTÁ VAZIO DE PROPÓSITO.
        /// =====================================================================
        /// A primeira versão registrava uma operação na fila do
        /// `GlobalProgressService` e publicava o progresso pelo token. O log
        /// mostrou exatamente o que acontece:
        ///
        ///     18:04:56.300  TASK STARTED:   'Verificando atualizações...'  Priority: 1
        ///     18:04:56.321  TASK STARTED:   'Carregando Dashboard...'     Priority: 0
        ///     18:04:56.326  TASK COMPLETED: 'Verificando atualizações...'  Duration: 0,0s
        ///
        /// Vinte e seis milissegundos. A tarefa morreu sozinha.
        ///
        /// A causa é o `isPriority: true`. Com prioridade 1, a atualização é a
        /// tarefa mais prioritária da fila — e `CompleteOperation()` encerra
        /// exatamente a de maior prioridade. Então QUALQUER `CompleteOperation()`
        /// que o app dispara por outros caminhos (e o app dispara muitos: ao
        /// navegar, ao trocar de tela, ao fechar um serviço) mata a atualização.
        ///
        /// Não é um defeito do `GlobalProgressService`: é o contrato dele. A fila
        /// serve para operações independentes competirem por uma barra, e a
        /// atualização NÃO é uma delas — ela tem que aparecer até o fim, sem
        /// depender de sorte na fila.
        ///
        /// Por isso a barra passou a ser escrita DIRETO pela `MainWindow`, que
        /// acompanha `UpdateFlowState.Changed` e chama o mesmo `UpdateProgress`
        /// que a fila chama. Ninguém mais pode encerrar, e o texto vai para o
        /// mesmo `StatusLabel` que a otimização usa — que é o que o usuário
        /// pediu: o mesmo padrão, não um segundo sistema.
        /// </summary>
        private static void PublishGlobal(double percentage, string key)
        {
            // Mantido apenas para registro de diagnóstico do caminho antigo.
            // A publicação real acontece em `MainWindow.OnUpdateFlowChanged`.
        }

        /// <summary>
        /// Carimbo da barra global em arquivo próprio.
        ///
        /// A barra é controlada por três elos — o token, o serviço e o
        /// <c>MainWindow</c> — e um defeito em qualquer um deles produz o mesmo
        /// sintoma: nada aparece. Sem este registro, a única forma de saber em
        /// qual elo parou seria acrescentar log nos três e compilar três vezes.
        /// </summary>
        private static void MarkGlobal(string message)
        {
            // [FIX:DIAGNOSTICO-ENGOLINDO-LINHAS]
            // ==========================================
            // Este método tem um `catch { }` vazio, e por isso uma linha de log
            // pode SUMIR sem ninguém perceber.
            //
            // `File.AppendAllText` abre o arquivo com compartilhamento
            // exclusivo. Duas chamadas simultâneas — o que acontece com
            // frequência aqui, porque o estado muda na thread de download e a
            // interface lê ao mesmo tempo — fazem uma delas lançar, e a linha
            // vai embora. O `catch` vazio garante que o log não derrube nada,
            // e o preço é um log que mente por omissão.
            //
            // Foi exatamente o que aconteceu na verificação de 19:30:16: o
            // log mostrava a flag travada em 1 sem NENHUMA reserva vencedora
            // antes dela, o que é logicamente impossível pelo código, e a
            // única explicação possível era uma linha perdida.
            //
            // Um diagnóstico que perde a linha que ele existe para registrar
            // não é um diagnóstico: é uma aposta. O `lock` abaixo serializa
            // as escritas, e a falha de verdade passa a ser visível em vez de
            // silenciosa.
            lock (GlobalLogSync)
            {
                try
                {
                    string dir = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Voltris", "Logs");

                    System.IO.Directory.CreateDirectory(dir);
                    System.IO.File.AppendAllText(
                        System.IO.Path.Combine(dir, "update_globalbar.log"),
                        $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
                }
                catch (Exception ex)
                {
                    // Falhou de verdade. Registrado em stderr para não ser
                    // engolido, porque o ficheiro que falhou é o próprio log.
                    System.Diagnostics.Debug.WriteLine(
                        "[UpdateFlow] falha ao gravar log de diagnóstico: " + ex.Message);
                }
            }
        }

        private static readonly object GlobalLogSync = new object();

        private static void BeginGlobalOperation()
        {
            try
            {
                _globalToken = GlobalProgressService.Instance.BeginOperation(
                    LocalizationService.Instance.GetString("UpdateFlowChecking"),
                    isPriority: true);

                MarkGlobal($"operacao iniciada | token nulo = {_globalToken == null}");

                _globalToken?.UpdateProgress(0);
            }
            catch (Exception ex)
            {
                MarkGlobal("FALHA ao abrir operacao: " + ex.Message);
                _globalToken = null;
            }
        }

        private static void EndGlobalOperation()
        {
            try
            {
                _globalToken?.Complete(LocalizationService.Instance.GetString("UpdateWinReadyToApply"));
            }
            catch
            {
                // Fechar a operação é cosmético: a barra é um espelho.
            }
            finally
            {
                _globalToken = null;
            }
        }

        private static void Report(ILoggingService? logger, string message)
        {
            logger?.LogInfo($"{Tag} {message}");
        }

        private static UpdateInfo BuildSimulatedInfo()
        {
            // A versão simulada é sempre MAIOR que a atual de propósito: é o que
            // faria a detecção real devolver este objeto, e um teste que
            // exercitasse um caminho impossível não estaria testando nada.
            string current = UpdateService.GetCurrentVersion();
            string fake = BumpVersion(current);

            // [FIX:VERSAO-SIMULADA-PAREECE-REAL] O número é MARCADO.
            //
            // A primeira versão devolvia `1.0.3.9` limpo, idêntico ao formato
            // de uma versão real. O usuário lia "Atualizando v1.0.3.9" e não
            // tinha como saber se aquilo vinha do GitHub ou era de teste.
            //
            // Isso não é vaidade do log: é o que separa "o programa está
            // atualizando de verdade" de "isto é um teste". Uma informação de
            // teste que parece real é pior que nenhuma, porque induz o
            // usuário a conclusionar sobre o estado do programa que não
            // existe.
            string marked = fake + " (simulação)";

            return new UpdateInfo
            {
                LatestVersion = marked,
                DownloadUrl = "https://localhost/simulado",
                Changelog = "(simulação) nenhum dado real foi baixado ou consultado",
                Mandatory = false
            };
        }

        /// <summary>
        /// Sobe o último componente da versão, para a simulação produzir uma
        /// versão plausivelmente mais nova que a instalada.
        /// </summary>
        private static string BumpVersion(string current)
        {
            if (!System.Version.TryParse(current, out Version? v))
            {
                return "99.0.0.0";
            }

            return new Version(v.Major, v.Minor, Math.Max(0, v.Build) + 1, v.Revision).ToString();
        }
    }
}
