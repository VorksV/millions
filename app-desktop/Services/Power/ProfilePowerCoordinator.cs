using System;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Power
{
    /// <summary>
    /// [FIX:POWER-MATRIX] O ÚNICO LUGAR QUE APLICA ENERGIA.
    ///
    /// POR QUE UM COORDENADOR
    /// ======================
    /// A troca de perfil acontece por CINCO entradas diferentes na interface:
    ///
    ///   1. página de primeira configuração (questionário)
    ///   2. botão "Perfil Inteligente" no Dashboard (modal de seleção)
    ///   3. systray (menu do ícone)
    ///   4. Modo Gamer / detecção de jogo
    ///   5. troca automática por mudança de janela em foco
    ///
    /// As cinco já passam pelo MESMO evento: `SettingsService.ProfileChanged`.
    /// Por isso este coordenador se inscreve nesse evento e é o único a aplicar
    /// energia. Nenhuma tela precisa saber que ele existe, e nenhuma tela pode
    /// "esquecer" de chamar — o esquecimento é o defeito original.
    ///
    /// POR QUE ASSINCRONO
    /// ==================
    /// A medição da máquina leva ~13 segundos. Se isso rodasse na thread da
    /// interface, a tela congelaria por 13 segundos a cada troca de perfil. Por
    /// isso o trabalho vai para uma thread de fundo, e o log avisa o que está
    /// acontecendo para que a espera seja compreensível.
    ///
    /// O cache da medição é aquecido em segundo plano durante o startup, de
    /// modo que a primeira troca de perfil já encontra o tier pronto.
    ///
    /// REVERSÃO AO SAIR
    /// ================
    /// Este coordenador guarda o plano que estava ativo antes da primeira
    /// aplicação do app e o devolve no encerramento. O método
    /// `o servico legado.RestoreOriginalPlanAsync` existe e promete
    /// isso no comentário, mas não tinha NENHUMA chamada no código — o plano
    /// ficava trocado para sempre depois que o app fechava.
    /// </summary>
    public static class ProfilePowerCoordinator
    {
        private const string Tag = "[PowerCoord]";

        private static readonly object Sync = new object();
        private static bool _subscribed;
        private static bool _applying;

        /// <summary>
        /// Resolve o log no momento do uso.
        ///
        /// [FIX:LOGGER-NULL] NO STARTUP, `App.LoggingService` AINDA É NULO.
        ///
        /// Este era o bug que tornava o sistema invisível. A primeira versão
        /// capturava o `ILoggingService` recebido por parâmetro e logava através
        /// dele. A medição de campo mostrou que, no ponto do startup onde o
        /// coordenador é registrado, os DOIS estão nulos:
        ///
        ///     Initialize entrou
        ///     logger resolvido: NULO; App.LoggingService: nulo
        ///
        /// Consequência: nenhuma linha do coordenador era gravada — nem a
        /// inscrição, nem o plano original, nem as 13 amostras da medição. A
        /// medição EXECUTAVA, era descartada por estar a máquina ocupada (o que
        /// está certo), e era cacheada como `Unknown` — e, sem log, "descartada
        /// com razão" era indistinguível de "não aconteceu".
        ///
        /// A correção é resolver o log a cada uso e, enquanto ele não existir,
        /// escrever direto no arquivo de marcador. Perder um log é aceitável;
        /// perder a rastreabilidade de uma decisão de energia não é.
        /// </summary>
        private static ILoggingService? ResolvedLogger => App.LoggingService;

        /// <summary>Log que funciona mesmo antes do serviço existir.</summary>
        private static void Log(string level, string message)
        {
            var logger = ResolvedLogger;
            if (logger != null)
            {
                switch (level)
                {
                    case "warn": logger.LogWarning(message); break;
                    case "error": logger.LogError(message); break;
                    case "success": logger.LogSuccess(message); break;

                    // [FIX:LOG-AVANCADO] "debug" NÃO É SINÔNIMO DE "info".
                    //
                    // O guard de 12 segundos escreve em nível debug para não
                    // poluir o log principal quando está tudo em ordem. Mas o
                    // `ILoggingService` do projeto não expõe `LogDebug` de forma
                    // utilizável aqui, e encaminhar para `LogInfo` faria o log
                    // principal receber uma linha a cada 12 segundos — ruído
                    // puro.
                    //
                    // A saída correta é o arquivo de marcador, que é justamente
                    // o canal de diagnóstico deste componente. Ele não depende
                    // do serviço de log e não polui nada.
                    case "debug":
                        Mark($"[DEBUG] {message}");
                        return;

                    default: logger.LogInfo(message); break;
                }
                return;
            }
            Mark($"[{level.ToUpperInvariant()}] {message}");
        }

        /// <summary>
        /// Escreve um carimbo num arquivo próprio, sem passar pelo
        /// `ILoggingService`. Também é o destino do log enquanto o serviço de
        /// log ainda não existe no startup.
        /// </summary>
        private static void Mark(string message)
        {
            try
            {
                string dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Voltris", "Logs");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(dir, "power_marker.log"),
                    $"{DateTime.Now:HH:mm:ss.fff} [tid={Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}");
            }
            catch
            {
                // diagnóstico não pode derrubar a aplicação
            }
        }

        /// <summary>
        /// [FIX:POWER-GATE] RE-ASSERT: NENHUM PLANO PODE DERRUBAR O NOSSO.
        ///
        /// A auditoria mostrou que Gamer Mode troca o plano com
        /// `PowerPlanPriority.GamerMode` (1), que é MAIS ALTA que
        /// `IntelligentProfile` (3) — e o Brain escreve `powercfg /setactive`
        /// direto, fora do orquestrador. Ou seja: os dois tiravam o plano
        /// gerenciado do ar, e todos os valores medidos e gravados nele viravam
        /// inertes.
        ///
        /// Bloquear a troca seria errado: Gamer Mode e Brain pedem troca
        /// legitimamente, e proibi-los quebraria o app. A solução é DEVOLVER o
        /// plano gerenciado: o Vs. perde a disputa de prioridade, e o
        /// comportamento desejado (perfil manda) continua valendo.
        ///
        /// Este monitor roda de tempos em tempos, só com trabalho quando
        /// necessário (uma leitura de GUID), e é a rede de segurança final do
        /// sistema inteiro.
        /// </summary>
        private static void StartPlanGuard()
        {
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        await Task.Delay(PlanGuardInterval).ConfigureAwait(false);

                        if (!PowerWriteGate.HasManagedPlan) continue;

                        if (VoltrisOptimizer.Utils.Win32.PowerNativeMethods.TryGetActiveSchemeGuid(out Guid active))
                        {
                            // [FIX:LOG-AVANCADO] REGISTRO DO CICLO INTEIRO.
                            //
                            // Este guard roda a cada 12 segundos, e antes de
                            // corrigir a detecção de override ele era o elo de um
                            // ciclo: o perfil re-assertava o plano, o orquestrador
                            // via a troca como "ação do usuário", descartava as
                            // requests, e o perfil re-assertava de novo.
                            //
                            // O log agora registra os TRÊS lados do ciclo — quem
                            // está asking, o que está ativo, e o que o perfil
                            // espera — em TODOS os casos, inclusive quando tudo
                            // está correto. Sem isso, um loop de 12 segundos
                            // produzia apenas um punhado de linhas parecidas, e
                            // não dava para saber quem estava-playando.
                            //
                            // O diagnóstico é gratuito: é uma leitura de GUID e
                            // uma comparação. E vale o custo, porque a próxima
                            // vez que algo brigar, a resposta vai estar escrita.
                            string activeName = VoltrisOptimizer.Utils.Win32.PowerNativeMethods.GetPlanName(active);
                            bool correto = active == PowerWriteGate.ManagedPlan;

                            Mark(
                                $"[GUARD] ativo='{activeName}' ({active}) " +
                                $"| gerenciado='{PowerWriteGate.ManagedPlanName}' ({PowerWriteGate.ManagedPlan}) " +
                                $"| {(correto ? "OK" : "DIVERGENTE -> re-assert")}");

                            Log(correto ? "debug" : "warn",
                                $"{Tag} [GUARD] ativo='{activeName}' | esperado='{PowerWriteGate.ManagedPlanName}' " +
                                (correto ? "(em dia)" : "(DIVERGENTE - re-assertando)"));

                            if (!correto)
                            {
                                ReassertManagedPlan();
                            }
                        }

                        // [FIX:POWER-VALUE-GUARD] Vigia dos VALORES, nao so do plano.
                        //
                        // O guard acima so nota quando o PLANO inteiro sai do ar. Mas
                        // o defeito observado em campo nao exige trocar de plano:
                        // basta reescrever VALORES dentro do plano que ja esta ativo.
                        //
                        // Foi assim que o estrago aconteceu. Dois servicos legacy
                        // escreviam no esquema ATIVO, que e o gerenciado, por
                        // P/Invoke proprio e por `Process.Start` direto — caminhos
                        // que nenhum filtro central alcanca. O resultado era o
                        // plano correto no nome e sabotado no conteudo: EPP 45
                        // virava 0, e o estacionamento voltava a 0. Ambos foram
                        // removidos do projeto, e o vigia abaixo e a ultima rede
                        // para qualquer escrita que ainda exista.
                        //
                        // Em vez de editar uma vintena de arquivos legados um por
                        // um, o perfil se torna a unica autoridade: a cada ciclo
                        // ele RELE os valores, e se alguem mexeu, reescreve e
                        // registra QUEM mexeu pelo lido divergente. O custo e uma
                        // leitura, e a garantia e que o plano gerenciado volta a
                        // ser o que a tabela diz — sempre.
                        VerifyManagedPlanValues();
                    }
                    catch (Exception ex)
                    {
                        Mark($"[ERRO] monitor de plano falhou: {ex.Message}");
                    }
                }
            });
        }

        /// <summary>
        /// [FIX:POWER-VALUE-GUARD] Os valores que definem o caracter do perfil.
        ///
        /// A lista e curta de proposito: sao os settings cujo valor errado
        /// estragou o desempenho de forma mensuravel nesta maquina. Estacionamento
        /// de nucleo e o primeiro da lista porque foi ele que custou 43% de clock
        /// (108% -> 62% de pico medidos a frio no NP550XDA).
        /// </summary>
        private static readonly Guid GuardSubProcessor = new("54533251-82be-4824-96c1-47b60b740d00");

        private static readonly (Guid Setting, string Nome, int Valor)[] GuardedValues =
        {
            (new Guid("36687f9e-e3a5-4dbf-b1dc-15eb381c6863"), "EPP",                 -1),
            (new Guid("0cc5b647-c1df-4637-891a-dec35c318583"), "CoreParkingMin",      -1),
            (new Guid("893dee8e-2bef-41e0-89c6-b55d0929964c"), "MinProcessorState",   -1),
            (new Guid("be337238-0d82-4146-a960-4f3749d470c7"), "BoostMode",           -1),
        };

        private static void VerifyManagedPlanValues()
        {
            Guid managed = PowerWriteGate.ManagedPlan;
            if (managed == Guid.Empty) return;

            int expectedEpp = -1;
            int expectedParking = -1;
            int expectedMin = -1;
            int expectedBoost = -1;

            using (PowerWriteGate.BeginProfileApply())
            {
                for (int i = 0; i < GuardedValues.Length; i++)
                {
                    var (setting, nome, _) = GuardedValues[i];
                    uint? lido = VoltrisOptimizer.Utils.Win32.PowerNativeMethods.TryReadSchemeAcValueIndex(managed, setting, GuardSubProcessor);
                    if (!lido.HasValue) continue;

                    int esperado = ResolveExpected(managed, nome, (int)lido.Value, ref expectedEpp, ref expectedParking, ref expectedMin, ref expectedBoost);
                    if (esperado < 0 || (int)lido.Value == esperado) continue;

                    Log("warn",
                        $"{Tag} VALOR SABOTADO: {nome} do plano '{PowerWriteGate.ManagedPlanName}' esta {lido.Value}, " +
                        $"e o perfil pede {esperado}. Rewrificando.");

                    VoltrisOptimizer.Utils.Win32.PowerNativeMethods.ApplyPowerSetting(managed, GuardSubProcessor, setting, (uint)esperado, (uint)esperado);
                    Log("success", $"{Tag} {nome} restaurado para {esperado}.");
                }
            }
        }

        /// <summary>
        /// Descobre o valor correto olhando a propria tabela do perfil. Nao ha
        /// numero magico aqui: o valor vem de <see cref="ProfilePowerMatrix"/>,
        /// entao o vigia e sempre coerente com o que o perfil gravou.
        /// </summary>
        private static int ResolveExpected(
            Guid managed, string nome, int atual,
            ref int epp, ref int parking, ref int min, ref int boost)
        {
            if (epp < 0)
            {
                var row = _lastRow;
                if (row == null) return -1;
                epp = row.EppAc;
                parking = row.CoreParkingMinAc;
                min = row.MinProcessorAc;
                boost = row.BoostModeAc;
            }

            switch (nome)
            {
                case "EPP": return epp;
                case "CoreParkingMin": return parking;
                case "MinProcessorState": return min;
                case "BoostMode": return boost;
                default: return -1;
            }
        }

        /// <summary>
        /// A ultima linha da tabela aplicada. E o que o vigia usa para saber o
        /// valor correto de cada setting.
        /// </summary>
        private static ProfilePowerMatrix.Row? _lastRow;

        internal static void RememberRow(ProfilePowerMatrix.Row? row)
        {
            if (row != null) _lastRow = row;
        }

        private static void ReassertManagedPlan()
        {
            Guid managed = PowerWriteGate.ManagedPlan;
            if (managed == Guid.Empty) return;

            try
            {
                VoltrisOptimizer.Services.Power.SmartEnergyService.RunPowercfg($"/setactive {managed}");
                Log("success", $"{Tag} Plano gerenciado re-assertado: '{PowerWriteGate.ManagedPlanName}' ({managed})");
            }
            catch (Exception ex)
            {
                Log("warn", $"{Tag} Falha ao re-assertar o plano gerenciado: {ex.Message}");
            }
        }

        /// <summary>De quanto em quanto tempo conferimos se o plano continua ativo.</summary>
        private static readonly TimeSpan PlanGuardInterval = TimeSpan.FromSeconds(12);

        /// <summary>
        /// [FIX:UNICA-FONTE] O escritor legado de CPU FOI REMOVIDO.
        ///
        /// Antes, este método desligava o `o servico legado` em
        /// runtime - ele existia, consumia recursos e continuava instanciado.
        /// Desligar não é remover: o código continuava ali, pronto para voltar a
        /// gravar, e dependia de ninguém lembrar de chamar o desligamento.
        ///
        /// O serviço foi apagado do projeto. Não existe mais nenhuma segunda
        /// fonte de escrita de energia para desabilitar, e este método existe
        /// apenas para manter a chamada explícita no log de inicialização — é o
        /// ponto onde se registra, de forma permanente, que a energia tem UM
        /// único dono.
        ///
        /// O que existia antes: EPP 20/30/35/50/60/70/80 gravados no esquema
        /// ATIVO, subscritos ao MESMO evento `ProfileChanged` do sistema novo,
        /// sem lock e sem ordem. Em campo, ele gravou EPP 20 logo depois do
        /// sistema novo gravar 45, e o valor da tabela nunca chegou a valer.
        /// A tabela dele também não separava AC/DC nem tinha tiers: era um
        /// número por perfil, cego para a máquina.
        /// </summary>
        public static void DisableLegacyCpuProfileWriter(ILoggingService? logger)
        {
            Log("info",
                $"{Tag} Energia: fonte ÚNICA confirmada. O escritor legado de CPU e os " +
                "demais escritores legados foram removidos do projeto; nao ha segunda via de " +
                "escrita de EPP, plano, core parking ou boost.");
        }

        /// <summary>Plano ativo ANTES da primeira aplicação do Voltris.</summary>
        private static Guid _originalPlan = Guid.Empty;

        public static string OriginalPlanName { get; private set; } = "(desconhecido)";

        /// <summary>
        /// Assina o evento e aquece o cache de medição em segundo plano.
        /// Seguro para chamar mais de uma vez.
        /// </summary>
        public static void Initialize(ILoggingService? logger)
        {
            // [FIX:LOGGER-DIAG] MARCADOR DIRETO EM ARQUIVO, SEM O SERVIÇO DE LOG.
            //
            // Este bloco existe para uma única pergunta de diagnóstico: o
            // `Initialize` está sendo chamado mesmo? A resposta anterior era
            // ambígua porque o serviço de log descartava as primeiras linhas
            //Logo após o self-test escrever centenas de linhas seguidas.
            //
            // A resposta é sempre a mesma, e é barata: um arquivo sem
            // dependências, na pasta Logs, com um carimbo por evento. Se o
            // arquivo existir, o método rodou.
            Mark("Initialize entrou");

            lock (Sync)
            {
                if (_subscribed) { Mark("Initialize ja estava inscrito; retornando"); return; }
                _subscribed = true;
            }

            ILoggingService? log = ResolvedLogger ?? logger;
            Mark($"logger resolvido: {(log == null ? "NULO" : "OK")}; App.LoggingService: {(App.LoggingService == null ? "nulo" : "ok")}");

            try
            {
                SettingsService.Instance.ProfileChanged += OnProfileChanged;
                Log("info", $"{Tag} Inscrito em SettingsService.ProfileChanged. " +
                            "As 5 entradas (primeira configuracao, Dashboard, systray, Modo Gamer, troca por janela) " +
                            "passam a aplicar energia por este caminho unico.");
            }
            catch (Exception ex)
            {
                Log("error", $"{Tag} Falha ao assinar ProfileChanged: {ex.Message}");
            }

            // Guarda o plano original ANTES de qualquer aplicação, para devolver
            // no encerramento.
            try
            {
                if (VoltrisOptimizer.Utils.Win32.PowerNativeMethods.TryGetActiveSchemeGuid(out Guid active))
                {
                    _originalPlan = active;
                    OriginalPlanName = VoltrisOptimizer.Utils.Win32.PowerNativeMethods.GetPlanName(active);
                    Log("info", $"{Tag} Plano original guardado: '{OriginalPlanName}' ({active})");
                }
            }
            catch (Exception ex)
            {
                Log("warn", $"{Tag} Nao foi possivel guardar o plano original: {ex.Message}");
            }

            // [FIX:POWER-GATE] LIGA AS TRAVAS ANTES DE QUALQUER COISA.
            //
            // Primeiro desliga o escritor legado de CPU (que gravava EPP por
            // conta própria e brigava com a tabela nova), depois declara que este
            // serviço é o dono da verdade, e só então começa a monitorar se
            // alguém tirou o nosso plano do ar.
            DisableLegacyCpuProfileWriter(log);
            StartPlanGuard();

            // [FIX:WARMUP-DELAY] O AQUECIMENTO ESPERA O STARTUP PASSAR.
            //
            // Medir durante o startup é medir a própria carga do startup: WMI,
            // LibreHardwareMonitor e enumeração de processos já estão usando a
            // CPU, o que (com razão) invalida a medição. Além disso, o serviço de
            // log só existe depois. Então a medição é adiada: quando rodar, a
            // máquina já estará tranquila e o resultado valerá.
            _ = Task.Run(async () =>
            {
                await Task.Delay(WarmupDelay).ConfigureAwait(false);
                try
                {
                    Log("info", $"{Tag} Aquecendo a medicao da maquina em segundo plano (~13s)...");
                    var cap = HardwareCapabilityProbe.Get(ResolvedLogger, forceRefresh: true);
                    Log("success", $"{Tag} Medicao pronta: {cap.Describe()}");
                }
                catch (Exception ex)
                {
                    Log("warn", $"{Tag} Falha ao aquecer a medicao: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Espera antes da primeira medição. Tempo suficiente para o startup
        /// (que passa de 70s nesta máquina) deixar a CPU descansar.
        /// </summary>
        private static readonly TimeSpan WarmupDelay = TimeSpan.FromSeconds(100);

        private static void OnProfileChanged(object? sender, IntelligentProfileType newProfile)
        {
            var logger = App.LoggingService;
            logger?.LogInfo($"{Tag} Perfil alterado para {newProfile}. Aplicando energia em segundo plano...");

            _ = Task.Run(() => ApplyNow(newProfile, ResolvedLogger));
        }

        /// <summary>Aplica o perfil. Público para permitir acionamento manual.</summary>
        /// <summary>
        /// [FIX:UNICA-FONTE] Aplica o perfil que está guardado nas configurações.
        ///
        /// Usado na inicialização, quando ainda não houve evento
        /// <c>ProfileChanged</c> nesta sessão. Lê o mesmo perfil que o
        /// questionário e o painel gravam, e segue exatamente o mesmo caminho de
        /// <see cref="ApplyNow"/>, para que a primeira aplicação da sessão não
        /// possa divergir das seguintes.
        ///
        /// Se nenhuma configuração existir, não faz nada: sem perfil escolhido
        /// pelo usuário, o app não decide energia sozinho.
        /// </summary>
        public static void ApplyCurrentProfile(ILoggingService? logger)
        {
            try
            {
                var profile = SettingsService.Instance?.Settings?.IntelligentProfile;
                if (profile == null)
                {
                    Log("info", $"{Tag} Nenhum perfil salvo ainda; energia nao aplicada na inicializacao.");
                    return;
                }

                Log("info", $"{Tag} Aplicando perfil salvo: {profile}");
                ApplyNow(profile.Value, logger);
            }
            catch (Exception ex)
            {
                Log("warn", $"{Tag} Falha ao aplicar o perfil salvo: {ex.Message}");
            }
        }

        public static void ApplyNow(IntelligentProfileType profile, ILoggingService? logger)
        {
            // [FIX:APLICACAO-REPETIDA] O MESMO PERFIL NÃO É APLICADO DUAS VEZES.
            //
            // O log do usuário mostrou três aplicações em oito segundos:
            //
            //     02:14:17  Aplicacao concluida: ... gravados=15 confirmados=15
            //     02:14:22  Aplicacao concluida: ... gravados=15 confirmados=15
            //     02:14:24  Aplicacao concluida: ... gravados=15 confirmados=15
            //
            // As três vieram de caminhos diferentes — o startup, a inicialização
            // do orquestrador e um `ProfileChanged` — e todas convergentes no
            // MESMO perfil. A trava `_applying` anterior só impedia que duas
            // aplicações se sobrepusessem; ela não impedia que a segunda
            // começasse assim que a primeira terminava.
            //
            // O efeito é um desperdício que ainda atrapalha: cada aplicação
            // reescreve os 15 valores, reativa o plano e dispara a detecção de
            // usurpação do orquestrador — que é justamente a máquina que
            // mantinha o ciclo de troca vivo. Três aplicações no startup
            // significam três ruídos no log e três janelas em que o plano pode
            // ser notificado como having sido trocado por outrem.
            //
            // A regra agora é por CONTEÚDO, não por tempo: se o perfil pedido é
            // o mesmo que já está aplicado, não há nada a fazer. Isso é
            // diferente de usar um temporizador — um temporizador impediria uma
            // reexecução legítima depois que o usuário troca a tomada, que
            // muda o que precisa ser aplicado.
            lock (Sync)
            {
                if (_applying)
                {
                    logger?.LogInfo($"{Tag} Ja existe uma aplicacao em andamento; esta foi ignorada.");
                    return;
                }

                if (_lastAppliedProfile == profile && _lastAppliedPlan == PowerWriteGate.ManagedPlan)
                {
                    logger?.LogInfo(
                        $"{Tag} Perfil '{profile}' ja esta aplicado no plano gerenciado; " +
                        "aplicacao duplicada dispensada.");
                    return;
                }

                _applying = true;
            }

            try
            {
                var result = ProfilePowerApplier.Apply(profile, logger);
                logger?.LogInfo($"{Tag} Aplicacao concluida: {result.Summary}");

                lock (Sync)
                {
                    _lastAppliedProfile = profile;
                    _lastAppliedPlan = PowerWriteGate.ManagedPlan;
                }
            }
            catch (Exception ex)
            {
                logger?.LogError($"{Tag} Erro ao aplicar energia do perfil {profile}: {ex.Message}");

                // [FIX:APLICACAO-REPETIDA] FALHOU NÃO É APLICADO.
                //
                // Sem isto, um erro deixaria o cache falando "perfil X já
                // aplicado" quando na verdade o Windows está com outro plano em
                // vigor — e nenhuma tentativa posterior de corrigir passaria.
                lock (Sync)
                {
                    _lastAppliedProfile = null;
                }
            }
            finally
            {
                lock (Sync) { _applying = false; }
            }
        }

        /// <summary>
        /// O perfil já aplicado nesta sessão, e no plano em que foi aplicado.
        ///
        /// O plano é guardado junto do perfil de propósito: se o plano gerenciado
        /// mudar — o que acontece quando o app o recria, ou quando o usuário o
        /// apaga de Opções de Energia —, a mesma combinação perfil+plano já não
        /// vale, e a aplicação tem de acontecer de novo. Comparar só o perfil
        /// deixaria o sistema acreditando que os valores ainda estão lá, e eles
        /// não estão.
        /// </summary>
        private static IntelligentProfileType? _lastAppliedProfile;
        private static Guid _lastAppliedPlan = Guid.Empty;

        /// <summary>
        /// Devolve o plano de energia original. Chamado no encerramento do app.
        /// </summary>
        public static void RestoreOriginal(ILoggingService? logger)
        {
            lock (Sync)
            {
                if (_originalPlan == Guid.Empty)
                {
                    logger?.LogInfo($"{Tag} Nenhum plano original guardado; nada a restaurar.");
                    return;
                }
            }

            try
            {
                if (VoltrisOptimizer.Utils.Win32.PowerNativeMethods.TryGetActiveSchemeGuid(out Guid current) && current == _originalPlan)
                {
                    logger?.LogInfo($"{Tag} Plano ja e o original ('{OriginalPlanName}'); nada a restaurar.");
                    return;
                }

                VoltrisOptimizer.Services.Power.SmartEnergyService.RunPowercfg($"/setactive {_originalPlan}");

                logger?.LogSuccess($"{Tag} Plano original restaurado: '{OriginalPlanName}' ({_originalPlan})");
            }
            catch (Exception ex)
            {
                logger?.LogWarning($"{Tag} Falha ao restaurar o plano original: {ex.Message}");
            }
        }
    }
}
