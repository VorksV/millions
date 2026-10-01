using System;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.UpdateFlow
{
    /// <summary>
    /// Onde o fluxo de atualização está.
    /// </summary>
    public enum UpdateFlowStage
    {
        /// <summary>Nada acontecendo. É o estado inicial e o de descanso.</summary>
        Idle = 0,

        /// <summary>
        /// A atualização foi encontrada e o botão circular já está mostrando a
        /// versão. Ainda não baixou nada.
        /// </summary>
        Detected = 1,

        /// <summary>Baixando o pacote. É este estado que bloqueia a interface.</summary>
        Downloading = 2,

        /// <summary>
        /// Baixou tudo, chegou a 100% e ESPERA o usuário autorizar.
        ///
        /// É um estado próprio, e não um detalhe: instala e reiniciar sem
        /// ninguém pedir é o tipo de coisa que faz o usuário perder trabalho e
        /// odiar o programa. Chegar a 100% não é autorização para reiniciar.
        /// </summary>
        ReadyToApply = 3,

        /// <summary>Falhou. A interface volta a ficar utilizável.</summary>
        Failed = 4
    }

    /// <summary>
    /// [UPDATE-FLUXO] O ESTADO ÚNICO DO "ESTAMOS ATUALIZANDO".
    ///
    /// POR QUE ESTE TIPO EXISTE
    /// ========================
    /// A pergunta "o programa está atualizando agora?" precisa de UMA resposta,
    /// e essa resposta é lida de lugares muito diferentes: a sidebar, os botões
    /// do header, os botões do Dashboard, o botão circular e a barra global.
    ///
    /// Se cada um guardasse o seu próprio "está atualizando", cada um teria sua
    /// própria verdade, e o resultado seria a situação clássica de um app em que
    /// a sidebar está travada mas o botão do header não, ou vice-versa — que é
    /// pior do que não travar nada, porque o usuário não sabe em que estado o
    /// programa está.
    ///
    /// O padrão é o mesmo que a energia usa: um estado central, publicado por
    /// evento, e todo mundo ASSISTE em vez de decidir.
    ///
    /// O QUE ESTE TIPO NÃO FAZ
    /// ========================
    /// Não consulta o GitHub. Não baixa nada. Não instala. Ele guarda estado e
    /// avisa. A detecção e o download continuam sendo feitos por
    /// <see cref="Core.Updater.UpdateService"/>, que é o dono desse assunto e
    /// NÃO É ALTERADO por este fluxo — é a garantia de que quem usa uma versão
    /// antiga continua conseguindo atualizar.
    ///
    /// MODO DE SIMULAÇÃO
    /// =================
    /// <see cref="SimulationEnabled"/> existe para poder testar a interface
    /// inteira sem encostar no GitHub, sem baixar pacote e sem abrir
    /// instalador. Ver a propriedade para os detalhes de por que isso é
    /// necessário em vez de "testar de verdade e tomar cuidado".
    /// </summary>
    public static class UpdateFlowState
    {
        private static readonly object Sync = new object();

        private static UpdateFlowStage _stage = UpdateFlowStage.Idle;
        private static string _latestVersion = string.Empty;
        private static double _progress;
        private static string _statusKey = string.Empty;
        private static bool _simulation;

        /// <summary>
        /// Dispara quando qualquer parte do estado muda. Assinado pela
        /// interface (para travar ou destravar) e pelo Dashboard (para o arco).
        /// </summary>
        public static event EventHandler? Changed;

        public static UpdateFlowStage Stage
        {
            get { lock (Sync) { return _stage; } }
        }

        /// <summary>
        /// Se a interface tem de ficar travada AGORA.
        ///
        /// Só vale para <see cref="UpdateFlowStage.Downloading"/>. Nos outros
        /// estados o app é utilizável:
        ///
        /// - <c>Idle</c> e <c>Failed</c>: nada acontece, tem de ser normal.
        /// - <c>Detected</c>: a atualização foi achada mas nada está sendo
        ///   baixado. Travar aqui seria impedir o usuário de sair do programa
        ///   cancelando uma atualização que ele nem pediu ainda.
        /// - <c>ReadyToApply</c>: a interface volta a ficar utilizável DE
        ///   PROPÓSITO, porque é o estado em que o botão de aplicar precisa
        ///   ser clicado. Travar aqui deixaria o app preso num limbo em que só
        ///   um botão que não pode ser clicado decide o destino do programa.
        /// </summary>
        public static bool BlocksInput
        {
            get { lock (Sync) { return _stage == UpdateFlowStage.Downloading; } }
        }

        /// <summary>A versão detectada, para mostrar no botão circular.</summary>
        public static string LatestVersion
        {
            get { lock (Sync) { return _latestVersion; } }
        }

        /// <summary>Progresso de 0 a 100. Alimenta o arco e a barra global.</summary>
        public static double Progress
        {
            get { lock (Sync) { return _progress; } }
        }

        /// <summary>Chave de tradução do texto atual, já no idioma do usuário.</summary>
        public static string StatusText
        {
            get
            {
                string key;
                lock (Sync) { key = _statusKey; }

                if (string.IsNullOrEmpty(key)) return string.Empty;

                return LocalizationService.Instance.GetString(key);
            }
        }

        /// <summary>Chave crua, para quem quiser montar a própria frase.</summary>
        public static string StatusKey
        {
            get { lock (Sync) { return _statusKey; } }
        }

        /// <summary>
        /// Se o fluxo está em simulação.
        ///
        /// [FIX:SEGURANCA-DO-TESTE] POR QUE ISTO É NECESSÁRIO
        /// ===================================================
        /// A tentação seria "é só testar de verdade, com uma versão real". Isso
        /// foi descartado por três motivos, todos práticos:
        ///
        /// 1. Um teste de verdade BAIXA o pacote. São várias centenas de MB, para
        ///    testar um desenho de botão.
        ///
        /// 2. Um teste de verdade ABRE O INSTALADOR, que fecha o programa. Não
        ///    há como ver o estado "100%, aguardando autorização" porque o
        ///    programa já morreu. E é justamente esse estado que precisa ser
        ///    testado — é o que impede o reinício surpresa.
        ///
        /// 3. Um teste de verdade depende do GitHub estar no ar e de existir
        ///    versão nova. Um teste que falha por causa da rede não é um teste
        ///    do botão, é um teste do GitHub, e as duas coisas se confundem.
        ///
        /// Com a simulação, o caminho exercitado é o MESMO código de produção
        /// — os mesmos eventos, o mesmo conversor de arco, a mesma barra global,
        /// o mesmo bloqueio de interface — e o que muda é apenas de onde vem o
        /// número. É o teste mais fiel possível sem risco.
        /// </summary>
        public static bool SimulationEnabled
        {
            get { lock (Sync) { return _simulation; } }
            set
            {
                lock (Sync) { _simulation = value; }
                Raise();
            }
        }

        /// <summary>
        /// Começa o fluxo: uma versão foi encontrada e o botão circular assume.
        ///
        /// Chamar com <paramref name="simulate"/> true mantém o GitHub fora do
        /// caminho e apenas publica o estado, para o teste da interface.
        /// </summary>
        public static void Begin(string latestVersion, bool simulate = false)
        {
            lock (Sync)
            {
                _stage = UpdateFlowStage.Detected;
                _latestVersion = latestVersion ?? string.Empty;
                _simulation = simulate;
                _progress = 0;
                _statusKey = "UpdateDetectedVersion";
            }
            Raise();
        }

        /// <summary>Começa o download. É daqui que a interface passa a travar.</summary>
        public static void StartDownload()
        {
            lock (Sync)
            {
                _stage = UpdateFlowStage.Downloading;
                _progress = 0;
                _statusKey = "UpdateWinDownloading";
            }
            Raise();
        }

        /// <summary>Atualiza o progresso e o texto, sem mudar de estado.</summary>
        public static void SetProgress(double percentage, string? statusKey = null)
        {
            lock (Sync)
            {
                // Trava o intervalo. Progresso é lido de um download real e de
                // uma simulação; deixar passar valor fora de 0..100 faria o
                // conversor de arco receber algo que ele não foi feito para
                // desenhar, e o arco viraria lixo em vez de um número.
                _progress = Math.Max(0.0, Math.Min(100.0, percentage));
                if (!string.IsNullOrEmpty(statusKey)) _statusKey = statusKey;
            }
            Raise();
        }

        /// <summary>
        /// Chegou a 100% e AGUARDA. Não instala, não reinicia.
        ///
        /// [FIX:FINAL-DA-SIMULACAO] O TEXTO AGORA DEPENDE DO MODO.
        /// ======================================================
        /// Este método recebia a mesma chave nos dois casos — real e simulação —
        /// e por isso a simulação terminava anunciando "Pronto para aplicar!".
        ///
        /// A frase estava certa e a situação, não. Numa atualização real ela
        /// descreve o próximo passo; numa simulação, o próximo passo é
        /// deliberadamente NENHUM, e o botão que aparece logo abaixo não faz
        /// nada quando é pressionado. O usuário ficava diante de um botão que
        /// prometia uma ação e não executava nenhuma, sem nenhuma pista de que
        /// aquilo era o comportamento esperado.
        ///
        /// O estado já carrega `_simulation`, então a distinção é lida de onde
        /// ela é mantida, em vez de ser passada de fora por um parâmetro que
        /// poderia discordar do estado.
        /// </summary>
        public static void ReadyToApply()
        {
            lock (Sync)
            {
                _stage = UpdateFlowStage.ReadyToApply;
                _progress = 100;
                _statusKey = _simulation ? "UpdateFlowSimDone" : "UpdateWinReadyToApply";
            }
            Raise();
        }

        /// <summary>Falhou. A interface é liberada e o usuário pode tentar de novo.</summary>
        public static void Fail(string? statusKey = null)
        {
            lock (Sync)
            {
                _stage = UpdateFlowStage.Failed;
                _statusKey = statusKey ?? "UpdateWinDownloadFailed";
            }
            Raise();
        }

        /// <summary>Volta ao normal. Usado depois de aplicar ou de cancelar.</summary>
        public static void Reset()
        {
            lock (Sync)
            {
                _stage = UpdateFlowStage.Idle;
                _latestVersion = string.Empty;
                _progress = 0;
                _statusKey = string.Empty;
                _simulation = false;
            }
            Raise();
        }

        /// [FIX:MULTICAST-ENGOLIA-OS-ASSINANTES] O `catch` FAZIA O CONTRÁRIO DO QUE DIZIA.
        /// ==================================================================================
        /// A versão anterior era uma linha:
        ///
        ///     try { Changed?.Invoke(null, EventArgs.Empty); } catch { }
        ///
        /// com o comentário de que "um assinante com exceção não pode impedir os outros de
        /// saber que o app está travado". O código fazia exatamente o oposto.
        ///
        /// `Changed` é um delegate MULTICAST: `Invoke` percorre a lista e, no primeiro
        /// assinante que lança, ABORTA o percurso. Os assinantes registrados depois
        /// daquele nunca são chamados — nem neste evento, nem nos seguintes. O `catch`
        /// então engole a exceção e o próximo `Changed +=` volta a funcionar, o que
        /// produz um defeito que parece intermitente e não é: é uma lista de
        /// assinantes permanentemente truncada a partir do ponto do primeiro erro.
        ///
        /// Foi exatamente isso que o log mostrou. O Dashboard e a barra global
        /// registram-se no mesmo evento que um assinante que toca interface a partir
        /// da thread de download — e `UpdateFlowState.Raise` é chamado de lá, porque
        /// quem publica o progresso é o `Task.Run` do download. Um assinante lançava
        /// `Dispatcher.VerifyAccess`, e o botão circular nunca recebia a notificação:
        /// a navegação para o Dashboard acontecia, o programa saía de Configurações, e
        /// a tela nova não tinha nada para mostrar. Para o usuário, o botão de
        /// simulação "não fazia nada".
        ///
        /// A correção é percorrer a lista um assinante por vez, cada um no seu
        /// próprio `try`. Um assinante que falha é culpa dele e não pode levar os
        /// outros junto — que é o que o comentário dizia e o que o código nunca fez.
        ///
        /// O nome do assinante que lançou é registrado. Sem isso, corrigir o
        /// sintoma (botão circular parado) deixaria a causa — o acesso entre
        /// threads — escondida em algum assinante que ninguém pensaria em olhar.
        private static void Raise()
        {
            EventHandler? handlers = Changed;
            if (handlers == null) return;

            foreach (Delegate subscriber in handlers.GetInvocationList())
            {
                try
                {
                    ((EventHandler)subscriber)(null, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    // Registrado em arquivo, e não no log do serviço: no
                    // startup o `App.LoggingService` pode ainda não existir,
                    // e uma falha de notificação que não deixa rastro é
                    // indistinguível de um fluxo que nunca foi exercitado.
                    Mark($"assinante {subscriber.Method.DeclaringType?.Name}.{subscriber.Method.Name} Lancou: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Registro de falhas de notificação, com carimbo direto em arquivo.
        /// </summary>
        private static void Mark(string message)
        {
            try
            {
                lock (LogSync)
                {
                    string dir = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Voltris", "Logs");

                    System.IO.Directory.CreateDirectory(dir);
                    System.IO.File.AppendAllText(
                        System.IO.Path.Combine(dir, "update_state.log"),
                        $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[UpdateFlowState] falha ao gravar log: " + ex.Message);
            }
        }

        private static readonly object LogSync = new object();
    }
}
