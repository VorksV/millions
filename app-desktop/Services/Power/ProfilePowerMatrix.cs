using System;
using System.Collections.Generic;
using System.Linq;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Power
{
    /// <summary>
    /// [FIX:POWER-MATRIX] OS VALORES REAIS DE CADA PERFIL.
    ///
    /// ESTA É A TABELA QUE SUBSTITUI A ESPERANÇA
    /// =========================================
    /// Antes, cada serviço tinha seus próprios números e nenhum deles chegava
    /// dentro dos planos "Voltris - {Perfil}" — que eram cópias literais de
    /// "Alto Desempenho" ou "Equilibrado" com o nome trocado. Três perfis gamer
    /// diferentes escreviam exatamente o mesmo valor. Não havia um lugar único
    /// onde se pudesse olhar e ver o que cada perfil realmente faz.
    ///
    /// Agora existe UM lugar só. Uma linha por perfil, colunas por tier. E o
    /// valor nunca depende só do perfil: depende também do que a MÁQUINA
    /// realmente aguenta, medido (ver <see cref="HardwareCapabilityProbe"/>).
    ///
    /// POR QUE O EPP CAI DA ESQUERDA PARA A DIREITA
    /// ============================================
    /// `EPP` é a Energy Performance Preference: 0 = performance máxima,
    /// 255 = economia máxima. É o valor que manda a Intel escolher o bin de
    /// turbo.
    ///
    /// Numa máquina que SEGURA o pico, 0 é correto e é o que dá a menor
    /// latência. Numa máquina fina, 0 é o PIOR valor: o processador corre para
    /// o topo, gasta o turbo de 8 segundos, bate no limite de potência e passa
    /// a ficar abaixo do que o plano "Equilibrado" sustentaria. Por isso o mesmo
    /// perfil tem valores crescentes conforme o tier piora.
    ///
    /// Um gaming notebook (tier SUSTAINED) recebe 0. O ultrabook 15W
    /// (tier POWER_LIMITED) recebe 45. Mesmo código, máquinas diferentes,
    /// resultado correto nas duas — porque a decisão é medido, não adivinhada.
    ///
    /// NOTA SOBRE "Criativo": é o perfil INVERSO do gamer. Render/export é
    /// trabalho sustentado, não pico. Por isso o EPP dele é ALTO até no tier
    /// mais capaz: forçar turbo máximo aqui só antecipa o estrangulamento
    /// térmico e piora o render.
    /// </summary>
    public static class ProfilePowerMatrix
    {
        /// <summary>
        /// Uma linha completa da tabela: todos os valores de um perfil num tier.
        /// AC e DC são SEMPRE campos distintos — essa separação é a correção do
        /// defeito mais grave encontrado (o app gravava o mesmo valor nos dois).
        /// </summary>
        public sealed class Row
        {
            public IntelligentProfileType Profile { get; set; }
            /// <summary>
        /// O TIER que a MEDIÇÃO atribuiu. Nunca deduzir isto do EPP: perfis como
        /// Estratégia (15/35/50) e Escritório (60/60/70) não seguem o corte
        /// "EPP baixo = tier capaz", e um rótulo errado no log faz a tabela
        /// parecer inconsistente quando os valores estão certos.
        /// </summary>
        public PowerCapabilityTier Tier { get; set; }

            // ---- TOMADA (AC) ----
            public int EppAc { get; init; }
            public int BoostModeAc { get; init; }
            public int MinProcessorAc { get; init; }
            public int MaxProcessorAc { get; init; }
            public int CoreParkingMinAc { get; init; }
            public int CoolingPolicyAc { get; init; }
            public int PcieLinkStateAc { get; init; }
            public int UsbSuspendAc { get; init; }
            public int DiskIdleAc { get; init; }

            // ---- BATERIA (DC) ----
            public int EppDc { get; init; }
            public int BoostModeDc { get; init; }
            public int MinProcessorDc { get; init; }
            public int MaxProcessorDc { get; init; }
            public int CoreParkingMinDc { get; init; }
            public int CoolingPolicyDc { get; init; }
            public int PcieLinkStateDc { get; init; }
            public int UsbSuspendDc { get; init; }
            public int DiskIdleDc { get; init; }

            // ---- modificadores de comportamento ----

            /// <summary>Prioridade de CPU do processo do jogo (nunca RealTime).</summary>
            public string GameCpuPriority { get; init; } = "Normal";

            /// <summary>Se o jogo deve ficar fora do balanceador de CPU.</summary>
            public bool ExcludeGameFromBalancer { get; init; }

            /// <summary>Se o disco deve nunca ir para espera (streaming de assets).</summary>
            public bool AggressiveDisk { get; init; }

            /// <summary>Se o núcleo mínimo sobe, para evitar latência de wake.</summary>
            public bool RaiseMinProcessor { get; init; }

            /// <summary>
            /// Se o app deve FIXAR núcleos do jogo. Padrão é NÃO, e por um motivo
            /// documentado: a Intel desaconselha afinidade dura em CPU híbrida,
            /// porque um thread preso pode acabar disputando núcleo com um
            /// processo de alta prioridade e passar fome. A entrada existe
            /// porque é explicitamente o que NÃO devemos fazer — deixá-la
            /// visível impede alguém de "otimizar" isso sem perceber.
            /// </summary>
            public bool PinGameCores { get; init; }

            /// <summary>Se a linha de CPU pode ser carimbada no processo do jogo.</summary>
            public bool AllowPerProcessEnergy { get; init; }

            // ---- rastreio do regime (nunca usados como entrada de outra decisão) ----

            /// <summary>
            /// O regime do instante em que esta linha foi resolvida.
            ///
            /// Fica NA LINHA, e não em log solto, por um motivo prático: a linha
            /// é o que o aplicador grava, é o que o vigia de valores compara e
            /// é o que o self-test inspeciona. Se o regime morresse num log, um
            /// valor divergente weeks depois não teria como ser explicado.
            /// </summary>
            public EnergyRegime Regime { get; init; } = EnergyRegime.Unknown;

            /// <summary>
            /// Se esta linha ficou ABAIXO do piso do Windows porque a escalada foi
            /// autorizada. Fica explícito porque é a única coisa nesta tabela que
            /// pode pedir mais do que o Windows já pede — e portanto a única que
            /// precisa ser defendível linha por linha no log.
            /// </summary>
            public bool EppEscalated { get; init; }

            /// <summary>Por que o regime foi esse, e por que a escalada foi ou não autorizada.</summary>
            public string RegimeReason { get; init; } = "";

            /// <summary>Classe de regime, para o log em uma linha.</summary>
            public string RegimeTag() =>
                $"regime={Regime} escalada={(EppEscalated ? "APLICADA" : "nao")}";

            public string Describe()
            {
                return
                    $"{Profile}/{Tier}: AC[epp={EppAc} boost={BoostModeAc} min={MinProcessorAc} max={MaxProcessorAc} " +
                    $"park={CoreParkingMinAc} cool={CoolingPolicyAc} pcie={PcieLinkStateAc} usb={UsbSuspendAc} disk={DiskIdleAc}] " +
                    $"DC[epp={EppDc} boost={BoostModeDc} min={MinProcessorDc} max={MaxProcessorDc} " +
                    $"park={CoreParkingMinDc} cool={CoolingPolicyDc} pcie={PcieLinkStateDc} usb={UsbSuspendDc} disk={DiskIdleDc}] " +
                    $"| jogo[prio={GameCpuPriority} foraDoBalanceador={ExcludeGameFromBalancer} " +
                    $"discoAgressivo={AggressiveDisk} nucleusMinAlto={RaiseMinProcessor} fixaNucleo={PinGameCores} " +
                    $"energiaPorProcesso={AllowPerProcessEnergy}]";
            }
        }

        // Constantes de setting (documentadas pela Microsoft)
        private const int BoostDisabled = 0;
        private const int BoostEnabled = 1;
        private const int BoostAggressive = 2;

        private const int PcieOff = 0;
        private const int PcieModerate = 1;
        private const int PcieMaximum = 2;

        // [FIX:PISO-FABRICA] EPP que o proprio Windows aplica no plano
        // "Equilibrado" desta maquina, lido pela API de energia. Vale como
        // piso para AC e para DC: um perfil nao pode deixar a maquina mais
        // lenta do que o sistema ja a mantem. Tambem e' o unico valor valido
        // de fallback onde a tabela usava numeros fora da escala 0-100.
        private const int WindowsBaselineEppAc = 33;
        private const int WindowsBaselineEppDc = 33;

        /// <summary>
        /// Tabela completa. Para acrescentar um perfil novo basta uma linha aqui —
        /// e o self-test cobra que ela exista para os TRÊS tiers.
        /// </summary>
        /// <summary>
        /// Resolve a linha SEM contexto de regime.
        ///
        /// É a sobrecarga que preserva o comportamento anterior exatamente: sem
        /// contexto, o regime é <see cref="EnergyRegime.Unknown"/> e a linha
        /// sai idêntica à que saía antes do regime existir. Existe para que
        /// nenhum caminho de código mude de comportamento por existir um
        /// parâmetro novo — mudar a assinatura obrigaria a revisar todos os
        /// chamadores e o risco de um deles passar a semear contexto por
        /// acidente é maior do que o de repetir a delegação.
        /// </summary>
        public static Row Resolve(IntelligentProfileType profile, PowerCapabilityTier tier)
            => Resolve(profile, tier, context: null);

        /// <summary>
        /// Resolve a linha considerando o regime do instante.
        ///
        /// O regime NÃO escolhe o perfil nem o plano: ele pode, no máximo,
        /// autorizar que a linha peça mais do que o piso do Windows. Quem
        /// escolhe o plano continua sendo o tier, e quem escolhe o perfil
        /// continua sendo o usuário e o Brain. É essa separação que impede o
        /// regime de virar um terceiro dono de energia.
        /// </summary>
        /// <param name="profile">Perfil escolhido.</param>
        /// <param name="tier">Tier medido por <see cref="HardwareCapabilityProbe"/>.</param>
        /// <param name="context">
        /// Regime do instante. <c>null</c> significa "não olhei" e produz
        /// exatamente a linha de sempre.
        /// </param>
        public static Row Resolve(
            IntelligentProfileType profile,
            PowerCapabilityTier tier,
            EnergyContext? context)
        {
            var row = ResolveBase(profile, tier);
            return ApplyRegime(row, tier, context);
        }

        /// <summary>
        /// A ESCALADA — O ÚNICO LUGAR DESTA TABELA QUE PODE PEDIR MAIS QUE O WINDOWS
        /// =====================================================================
        ///
        /// [FIX:ESCALADA-SELO] POR QUE ESTE PORTÃO ESTÁ FECHADO
        /// ==================================================
        /// `EscalationEnabled` é `false` e o self-test cobra que continue `false`.
        ///
        /// Não é falta de implementação: a linha logo abaixo está pronta, é
        /// determinística e respeita todas as regras. O que falta é EVIDÊNCIA.
        ///
        /// Todo valor differentiated desta tabela nasceu de um problema real
        /// observado em campo, e foi por isso que became número. A única
        /// exceção seria este: pedir MENOS EPP que o Windows numa máquina que
        /// segura o pico. A pesquisa de fabricante sustenta que a direção é
        /// correta, e a direção não é o problema — o problema é que "sustenta o
        /// pico" nunca foi medido nesta máquina nem em nenhuma outra, e o
        /// benchmark que media isso foi REMOVIDO por travamento do app.
        ///
        /// Aplicar um ganho plausível sem medição é exatamente o que produziu
        /// EPP 60, boost 1 e máximo 99. A diferença é que aqueles números
        /// MELHORARAM o produto e ainda assim estragaram a máquina; este
        /// MELHORARIA, o que tornaria a distinção ainda mais difícil de provar
        /// depois. Um otimizador que só publica número com medição é mais lento
        /// para melhorar e muito mais difícil de quebrar.
        ///
        /// O que destrava: rodá-lo em `true` com o regime ativo e o log
        /// registrando, por sessões completas de jogo, quantas vezes a máquina
        /// entrou em <c>Constrained</c> depois da escalada. Se entrar
        /// significantemente mais do que antes, a escalada está errada e a
        /// máquina é a vítima; se nunca entrar, há ganho a colher.
        ///
        /// [FIX:ESCALADA-BATERIA] A ESCALADA NUNCA TOCA A LINHA DC
        /// ======================================================
        /// Só `EppAc` muda. A linha de bateria continua no piso, mesmo com a
        /// escalada ligada, porque a autorização é válida para a tomada em que
        /// a máquina ESTÁ — não para uma tomada em que ela poderá estar. Pedir
        /// mais em bateria sem estar medindo bateria é o mesmo erro de
        /// premissa, com o pior denominador: bateria é o caso em que errar
        /// custa autonomia, e a REGRA 4 do self-test passa a garantir que a
        /// bateria é sempre a linha mais conservadora das duas.
        /// </summary>
        public const bool EscalationEnabled = false;

        /// <summary>
        /// EPP da escalada: UM passo, e só um, abaixo do piso.
        ///
        /// 25 em vez de um 0 radical. O motivo é geométrico, não estético: o
        /// que colapsa uma máquina fina é o salto grande na direção
        /// agressiva, porque ela passa a sustentar um regime térmico que ela
        /// não consegue sustentar. Um passo pequeno é reversível, não derruba o
        /// clock e ainda preserva o ganho procurado — que é o objetivo real de
        /// qualquer otimização: existe para melhorar e para poder ser
        /// desfeita.
        /// </summary>
        private const int EscalatedEppAc = 25;

        private static Row ApplyRegime(Row row, PowerCapabilityTier tier, EnergyContext? context)
        {
            if (context == null)
            {
                return row;
            }

            // Mantém o motivo sempre visível, mesmo quando nada muda. Um regime
            // que só aparece no log quando age é um regime que ninguém consegue
            // auditar quando NÃO agiu — que é justamente o caso que importa.
            string motivo = context.Describe();

            // [FIX:TIER-DUPLICADO] O portão é avaliado contra o TIER ARGUMENTO.
            //
            // Não contra `context.Tier`. Os dois são válidos isoladamente, e por
            // isso a divergência entre eles não produz exceção nem aviso: ela
            // produz uma escalada silenciosa numa máquina que não devia
            // escalar. Ver a justificativa completa em
            // <see cref="EnergyContext.CanEscalateFor"/>.
            if (!context.CanEscalateFor(tier))
            {
                string causa = context.IsConstrained
                    ? "MÁQUINA NO LIMITE: escalada barrada."
                    : context.Regime == EnergyRegime.Unknown
                        ? "SEM TELEMETRIA: escalada barrada."
                        : "sem folga provada para pedir mais: escalada barrada.";

                return new Row
                {
                    Profile = row.Profile,
                    Tier = row.Tier,
                    Regime = context.Regime,
                    EppEscalated = false,
                    RegimeReason = causa + " " + motivo,
                    EppAc = row.EppAc, BoostModeAc = row.BoostModeAc,
                    MinProcessorAc = row.MinProcessorAc, MaxProcessorAc = row.MaxProcessorAc,
                    CoreParkingMinAc = row.CoreParkingMinAc, CoolingPolicyAc = row.CoolingPolicyAc,
                    PcieLinkStateAc = row.PcieLinkStateAc, UsbSuspendAc = row.UsbSuspendAc,
                    DiskIdleAc = row.DiskIdleAc,
                    EppDc = row.EppDc, BoostModeDc = row.BoostModeDc,
                    MinProcessorDc = row.MinProcessorDc, MaxProcessorDc = row.MaxProcessorDc,
                    CoreParkingMinDc = row.CoreParkingMinDc, CoolingPolicyDc = row.CoolingPolicyDc,
                    PcieLinkStateDc = row.PcieLinkStateDc, UsbSuspendDc = row.UsbSuspendDc,
                    DiskIdleDc = row.DiskIdleDc,
                    GameCpuPriority = row.GameCpuPriority,
                    ExcludeGameFromBalancer = row.ExcludeGameFromBalancer,
                    AggressiveDisk = row.AggressiveDisk,
                    RaiseMinProcessor = row.RaiseMinProcessor,
                    PinGameCores = row.PinGameCores,
                    AllowPerProcessEnergy = row.AllowPerProcessEnergy
                };
            }

            if (!EscalationEnabled)
            {
                return new Row
                {
                    Profile = row.Profile,
                    Tier = row.Tier,
                    Regime = context.Regime,
                    EppEscalated = false,
                    RegimeReason =
                        "folga suficiente, mas escalada desligada por falta de medicao: " + motivo,
                    EppAc = row.EppAc, BoostModeAc = row.BoostModeAc,
                    MinProcessorAc = row.MinProcessorAc, MaxProcessorAc = row.MaxProcessorAc,
                    CoreParkingMinAc = row.CoreParkingMinAc, CoolingPolicyAc = row.CoolingPolicyAc,
                    PcieLinkStateAc = row.PcieLinkStateAc, UsbSuspendAc = row.UsbSuspendAc,
                    DiskIdleAc = row.DiskIdleAc,
                    EppDc = row.EppDc, BoostModeDc = row.BoostModeDc,
                    MinProcessorDc = row.MinProcessorDc, MaxProcessorDc = row.MaxProcessorDc,
                    CoreParkingMinDc = row.CoreParkingMinDc, CoolingPolicyDc = row.CoolingPolicyDc,
                    PcieLinkStateDc = row.PcieLinkStateDc, UsbSuspendDc = row.UsbSuspendDc,
                    DiskIdleDc = row.DiskIdleDc,
                    GameCpuPriority = row.GameCpuPriority,
                    ExcludeGameFromBalancer = row.ExcludeGameFromBalancer,
                    AggressiveDisk = row.AggressiveDisk,
                    RaiseMinProcessor = row.RaiseMinProcessor,
                    PinGameCores = row.PinGameCores,
                    AllowPerProcessEnergy = row.AllowPerProcessEnergy
                };
            }

            return new Row
            {
                Profile = row.Profile,
                Tier = row.Tier,
                Regime = context.Regime,
                EppEscalated = true,
                RegimeReason =
                    $"escalada aplicada: EPP {row.EppAc} -> {EscalatedEppAc} (um passo, so na tomada). " + motivo,
                EppAc = EscalatedEppAc, BoostModeAc = row.BoostModeAc,
                MinProcessorAc = row.MinProcessorAc, MaxProcessorAc = row.MaxProcessorAc,
                CoreParkingMinAc = row.CoreParkingMinAc, CoolingPolicyAc = row.CoolingPolicyAc,
                PcieLinkStateAc = row.PcieLinkStateAc, UsbSuspendAc = row.UsbSuspendAc,
                DiskIdleAc = row.DiskIdleAc,
                EppDc = row.EppDc, BoostModeDc = row.BoostModeDc,
                MinProcessorDc = row.MinProcessorDc, MaxProcessorDc = row.MaxProcessorDc,
                CoreParkingMinDc = row.CoreParkingMinDc, CoolingPolicyDc = row.CoolingPolicyDc,
                PcieLinkStateDc = row.PcieLinkStateDc, UsbSuspendDc = row.UsbSuspendDc,
                DiskIdleDc = row.DiskIdleDc,
                GameCpuPriority = row.GameCpuPriority,
                ExcludeGameFromBalancer = row.ExcludeGameFromBalancer,
                AggressiveDisk = row.AggressiveDisk,
                RaiseMinProcessor = row.RaiseMinProcessor,
                PinGameCores = row.PinGameCores,
                AllowPerProcessEnergy = row.AllowPerProcessEnergy
            };
        }

        /// <summary>
        /// A linha da tabela, sem nenhum olhar do regime. É o corpo antigo do
        /// <see cref="Resolve"/>, isolado para que a lógica do regime tenha um
        /// único ponto de entrada e nenhum outro possa "esquecer" de aplicá-lo.
        /// </summary>
        private static Row ResolveBase(IntelligentProfileType profile, PowerCapabilityTier tier)
        {
            // [FIX:UNKNOWN-VALUES] MEDIÇÃO INDISPONÍVEL USA OS VALORES
            // CONSERVADORES, E NÃO OS DO MEIO-TERMO.
            //
            // Este foi um erro real, observado em campo. O `Resolve` convertia
            // `Unknown` em `Balanced`, e o plano ia para o "Voltris - Equilibrado"
            // — mas os VALORES vinham da linha Balanced, com EPP 25 no
            // competitivo. Numa máquina de 15W isso é PIOR que o Equilibrado de
            // fábrica, que fica perto de 50. Ou seja: o plano dizia "equilibrado"
            // e os números diziam o contrário, e o usuário sentiu a piora
            // exatamente como descreveu.
            //
            // A incoerência vem de tratar "não sabemos o que a máquina aguenta"
            // como sinônimo de "aguenta o suficiente para o meio-termo". Não é.
            // É o contrário: na dúvida, vale o piso.
            //
            // E o outro lado do mesmo defeito: `Unknown` tem valor 0 no enum, e
            // a checagem do teto dos sinais diretos é `medido > teto`. Com
            // `Unknown` a comparação nunca é verdadeira, então o atalho da
            // medição inválida ANULAVA a proteção que os sinais diretos
            //_exists_ para dar. É por isso que nem o PowerLimited foi aplicado.
            PowerCapabilityTier effectiveTier = tier == PowerCapabilityTier.Unknown
                ? PowerCapabilityTier.PowerLimited
                : tier;

            var row = (profile, effectiveTier) switch
            {
                // ============ GAMER ============
                (IntelligentProfileType.GamerCompetitive, PowerCapabilityTier.SustainedPerformer) =>
                    Gamer(33, BoostAggressive, "AboveNormal", 5, 100, PcieMaximum, 0, 0, true),
                (IntelligentProfileType.GamerCompetitive, PowerCapabilityTier.Balanced) =>
                    Gamer(33, BoostAggressive, "AboveNormal", 5, 100, PcieMaximum, 0, 0, true),

                // [FIX:PISO-FABRICA] TIER LIMITADO USA OS MESMOS VALORES DE FÁBRICA.
                //
                // A tabela usava EPP 45 e boost 1 neste tier, partindo da
                // premissa — errada — de que "conservador" significava "seguro".
                // Em notebook limitado por potência, EPP alto segura o processador
                // numa frequência baixa, e boost suave tira a agressividade que a
                // máquina consegue sustentar. O resultado medido na prática foi o
                // oposto do pretendido: mais lento que o Windows.
                //
                // Sem medição a favor, o piso é o valor de fábrica.
                (IntelligentProfileType.GamerCompetitive, PowerCapabilityTier.PowerLimited) =>
                    Gamer(33, BoostAggressive, "AboveNormal", 5, 100, PcieMaximum, 0, 0, true),

                (IntelligentProfileType.GamerSinglePlayer, PowerCapabilityTier.SustainedPerformer) =>
                    Gamer(33, BoostAggressive, "AboveNormal", 5, 100, PcieMaximum, 0, 0, true),
                (IntelligentProfileType.GamerSinglePlayer, PowerCapabilityTier.Balanced) =>
                    Gamer(33, BoostAggressive, "AboveNormal", 5, 100, PcieMaximum, 0, 0, true),
                (IntelligentProfileType.GamerSinglePlayer, PowerCapabilityTier.PowerLimited) =>
                    Gamer(33, BoostAggressive, "AboveNormal", 5, 100, PcieMaximum, 0, 0, true),

                // Simulação: o jogo costuma ter 1-2 threads principal. O valor
                // de EPP é um pouco mais alto que o competitivo porque a
                // prioridade aqui é SUSTENTAR, e não ganhar 5ms.
                (IntelligentProfileType.GamerSimulation, PowerCapabilityTier.SustainedPerformer) =>
                    Sim(33, BoostAggressive, "AboveNormal", 5, 100, PcieMaximum, 0, 0, true),
                (IntelligentProfileType.GamerSimulation, PowerCapabilityTier.Balanced) =>
                    Sim(33, BoostAggressive, "AboveNormal", 5, 100, PcieMaximum, 0, 0, true),
                (IntelligentProfileType.GamerSimulation, PowerCapabilityTier.PowerLimited) =>
                    Sim(33, BoostAggressive, "AboveNormal", 5, 100, PcieMaximum, 0, 0, true),

                // MMO: muitos threads e streaming pesado de assets. Disco sem
                // espera. O núcleo mínimo SOBE só no tier capaz: num ultrabook
                // manter o mínimo em 20% gasta orçamento ocioso e antecipa o
                // estrangulamento, que é justamente o defeito medido.
                (IntelligentProfileType.GamerMMO, PowerCapabilityTier.SustainedPerformer) =>
                    Mmo(33, BoostAggressive, "AboveNormal", 20, 100, PcieMaximum, 0, 0, true),
                (IntelligentProfileType.GamerMMO, PowerCapabilityTier.Balanced) =>
                    Mmo(33, BoostAggressive, "AboveNormal", 20, 100, PcieMaximum, 0, 0, true),
                (IntelligentProfileType.GamerMMO, PowerCapabilityTier.PowerLimited) =>
                    Mmo(33, BoostAggressive, "AboveNormal", 5, 100, PcieMaximum, 0, 0, true),

                // Estratégia: a jogabilidade depende de RESPOSTA DE INTERFACE
                // (muitos cliques, menus, leitura), não de pico bruto. Por isso o
                // EPP é o mais alto entre os gamers: dar folga ao processador
                // deixa a UI e os vídeos de longe mais estáveis.
                (IntelligentProfileType.GamerStrategy, PowerCapabilityTier.SustainedPerformer) =>
                    Strategy(33, BoostAggressive, "AboveNormal", 20, 100, PcieMaximum, 0, 0, true),
                (IntelligentProfileType.GamerStrategy, PowerCapabilityTier.Balanced) =>
                    Strategy(33, BoostAggressive, "AboveNormal", 20, 100, PcieMaximum, 0, 0, true),
                (IntelligentProfileType.GamerStrategy, PowerCapabilityTier.PowerLimited) =>
                    Strategy(33, BoostAggressive, "AboveNormal", 5, 100, PcieMaximum, 0, 0, true),

                // ============ CRIATIVO / VÍDEO ============
                // INVERSO DO GAMER: render e export são carga SUSTENTADA.
                // MaxProcessor 100 (não 99) porque aqui o processador precisa
                // segurar o topo pelo tempo todo, e a folga térmica é uma
                // prioridade MENOR que terminar o trabalho.
                (IntelligentProfileType.CreativeVideoEditing, PowerCapabilityTier.SustainedPerformer) =>
                    Creative(33, 100, 1, PcieMaximum, PcieMaximum),
                (IntelligentProfileType.CreativeVideoEditing, PowerCapabilityTier.Balanced) =>
                    Creative(33, 100, 1, PcieMaximum, PcieMaximum),
                (IntelligentProfileType.CreativeVideoEditing, PowerCapabilityTier.PowerLimited) =>
                    Creative(33, 100, 1, PcieMaximum, PcieMaximum),

                // ============ DESENVOLVIMENTO ============
                // Compilação é em rajadas: importa a CURVA DE PARTIDA, não o
                // sustentado. Por isso boost agressivo com EPP moderado.
                (IntelligentProfileType.DeveloperProgramming, PowerCapabilityTier.SustainedPerformer) =>
                    Dev(33, BoostAggressive, 100, 1, PcieMaximum, PcieMaximum),
                (IntelligentProfileType.DeveloperProgramming, PowerCapabilityTier.Balanced) =>
                    Dev(33, BoostAggressive, 100, 1, PcieMaximum, PcieMaximum),
                (IntelligentProfileType.DeveloperProgramming, PowerCapabilityTier.PowerLimited) =>
                    Dev(33, BoostAggressive, 100, 1, PcieMaximum, PcieMaximum),

                // ============ ESCRITÓRIO ============
                // Refrigeração PASSIVA é intencional aqui: significa "use o
                // ventilador o necessário". Escritório é o perfil em que o
                // barulho importa mais do que o clock — e é a REFRIGERAÇÃO
                // que controla o barulho, não o EPP.
                //
                // [FIX:PISO-FABRICA] A COMBINACAO ORIGINAL (EPP 60/70 COM boost
                // 0 E max 99) ERA A PIOR POSSIVEL PARA TRABALHO DE OFFICE, e
                // e' exatamente o que o usuario encontrou no plano ativo:
                //
                //     EPP 60 + boost desligado + max 99
                //
                // EPP alto segura o CPU numa frequencia baixa durante o
                // expediente INTEIRO, e boost 0 tira o turbo justamente ao abrir
                // planilha, compilar e alternar entre aplicacoes — que e' o que
                // office faz o dia todo. Nenhuma medicao justificou: era premissa
                // herdada de "barulho", que cooling policy ja resolve sozinho.
                // Piso de fabrica: EPP 33, boost 2, max 100.
                (IntelligentProfileType.WorkOffice, PowerCapabilityTier.SustainedPerformer) =>
                    Office(33, BoostAggressive, 100, 5, 1),
                (IntelligentProfileType.WorkOffice, PowerCapabilityTier.Balanced) =>
                    Office(33, BoostAggressive, 100, 5, 1),
                (IntelligentProfileType.WorkOffice, PowerCapabilityTier.PowerLimited) =>
                    Office(33, BoostAggressive, 100, 5, 1),

                // ============ ENTERPRISE ============
                // Conservador, com refrigeração ATIVA: aqui a prioridade é a
                // LONGEVIDADE da máquina, não o desempenho. Ventilador ativo é
                // desgaste menor para o processador.
                //
                // [FIX:PISO-FABRICA] EPP 80/85 ERA O PIOR CASO DA TABELA INTEIRA:
                // a maquina passava o expediente inteiro pedindo o maximo de
                // resistencia termica. Conformidade e' restricao de POLITICA (o
                // que pode rodar), nao de potencia — nao existe razao tecnica para
                // o CPU ficar lento por causa dela. Piso de fabrica aplicado.
                (IntelligentProfileType.EnterpriseSecure, PowerCapabilityTier.SustainedPerformer) =>
                    Enterprise(33, BoostAggressive, 100, 1, PcieMaximum),
                (IntelligentProfileType.EnterpriseSecure, PowerCapabilityTier.Balanced) =>
                    Enterprise(33, BoostAggressive, 100, 1, PcieMaximum),
                (IntelligentProfileType.EnterpriseSecure, PowerCapabilityTier.PowerLimited) =>
                    Enterprise(33, BoostAggressive, 100, 1, PcieMaximum),

                // ============ USO GERAL ============
                //
                // [FIX:PISO-FABRICA] NENHUM TIER PODE SER PIOR QUE O WINDOWS.
                //
                // Estes quatro valores ja nao sao decisoes de projeto: são o
                // PISO. A comparação completa do plano aplicado contra o
                // "Equilibrado" de fábrica da própria máquina mostrou que TODOS
                // os settings que a tabela escrevia eram piores:
                //
                //     setting              Voltris   Equilibrado (funciona)
                //     EPP                      60       33
                //     Boost mode                1        2
                //     Processor maximum        99      100
                //     PCIe link state           1        2
                //
                // Nenhum desses números tinha medição a favor. Todos tinham o
                // sentido errado na direção, e o efeito observado pelo usuário foi
                // exatamente o esperado: PC lento e com gargalo assim que o perfil
                // era aplicado, voltando ao normal ao trocar para o plano nativo.
                //
                // A regra que fecha isso é simples e não admite discussão: um
                // perfil não pode deixar a máquina PIOR do que o Windows já faz.
                // Se não há medição de ganho, o valor é o do sistema, e ponto.
                // Otimização que não demonstrou benefício não é otimização — é
                // risco sem contrapartida.
                (IntelligentProfileType.GeneralBalanced, PowerCapabilityTier.SustainedPerformer) =>
                    General(33, 2, 100, 5, 1, PcieMaximum),
                (IntelligentProfileType.GeneralBalanced, PowerCapabilityTier.Balanced) =>
                    General(33, 2, 100, 5, 1, PcieMaximum),
                (IntelligentProfileType.GeneralBalanced, PowerCapabilityTier.PowerLimited) =>
                    General(33, 2, 100, 5, 1, PcieMaximum),

                _ => General(33, 2, 100, 5, 1, PcieMaximum)
            };

            // Os construtores são compartilhados entre variantes gamer e por isso
            // não sabem o perfil. Ele é carimbado AQUI, num único lugar, para que
            // não exista forma de obter uma linha sem perfil — o log e o
            // self-test dependem disso para identificar a linha.
            row.Profile = profile;
            row.Tier = effectiveTier;   // o tier vem da MEDICAO, nunca de deducao pelo EPP
            return row;
        }

        // ------------------------------------------------------------------
        // Construtores de linha. Um por perfil, para que a diferença entre os
        // perfis fique VISÍVEL no código — inclusive a diferença de que
        // Criativo usa MaxProcessor 100 e os gamers usam 99.
        // ------------------------------------------------------------------

        /// <summary>
        /// [FIX:CORE-PARKING] POR QUE ESTACIONAMENTO É SEMPRE 100
        /// ==============================================
        /// Este era o defeito que o usuário viu na prática, e ele é o mais
        /// caro da tabela inteira.
        ///
        /// A versão anterior passava `parkAc` por chamada, e as chamadas
        /// gravavam 0 ("nunca estacionar núcleo"). A teoria por trás era a
        /// receita clássica de otimização: reduzir latência de wake. Em desktop
        /// com folga de potência isso às vezes ajuda.
        ///
        /// MEDIDO no NP550XDA (i5-1135G7, 15W), mesma carga, a frio:
        ///     plano de fábrica, estacionamento 100 .... 108% de pico
        ///     "Voltris - Equilibrado", estacionamento 0 ..  62% de pico
        ///
        /// Ou seja, 43% MENOS clock por desativar o estacionamento. O motivo
        /// é físico: com todos os núcleos sempre ativos o processador gasta o
        /// orçamento térmico ocioso, e quando a carga chega ele já está quente
        /// e bate no limite de potência. O sintoma observado pelo usuário foi
        /// exatamente esse: os GHz CAÍAM quando o jogo começava, chegando a
        /// 1,3-1,5 GHz — abaixo do clock base.
        ///
        /// Por isso `CoreParkingMin` deixa de ser parâmetro: vira constante 100.
        /// Não é opcional, não é por tier e não é por perfil. O Windows decide
        /// quando estacionar, que é o comportamento de fábrica dos DOIS planos
        /// nativos desta máquina. Nada aqui tenta ser mais esperto que isso.
        /// </summary>
        private const int CoreParkingAlwaysAllowed = 100;

        private static Row Gamer(int eppAc, int boostAc, string prio, int minAc, int maxAc,
                                int pcieAc, int usbAc, int diskAc, bool excludeFromBalancer)
        {
            return new Row
            {
                EppAc = eppAc,
                BoostModeAc = boostAc,
                MinProcessorAc = minAc,
                MaxProcessorAc = maxAc,
                CoreParkingMinAc = CoreParkingAlwaysAllowed,
                CoolingPolicyAc = 1,              // ATIVA: o ventilador é de graça
                PcieLinkStateAc = pcieAc,
                UsbSuspendAc = usbAc,
                DiskIdleAc = diskAc,

                // BATERIA: sem diferenciacao inventada.
                //
                // [FIX:EPP-DC-INVALIDO] Este campo usava
                //     EppDc = eppAc <= 45 ? 128 : 128
                // que e um ternario MORTO (os dois ramos iguais) e, pior, um valor
                // FORA da escala. O EPP (PERF_EPP) vai de 0 a 100: 128 nao e um
                // EPP alto, e' um valor invalido. A API trunca ou rejeita, e o
                // resultado nao era "conservador" — era indefinido.
                //
                // A diferenciacao AC/DC tambem nao tinha lastro: nao existe medicao
                // que justifique um EPP de bateria diferente do EPP de tomada. E o
                // comentario antigo dizia "nada de agressividade na bateria" sem
                // dizer QUAL valor agressivo seria, porque nao havia esse valor.
                //
                // Regra adotada: sem medicao, o piso do Windows vale para os dois
                // lados. Se um dia houver medicao em bateria, o numero entra aqui
                // com evidencia em vez de com premissa.
                EppDc = WindowsBaselineEppDc,
                BoostModeDc = BoostAggressive,
                MinProcessorDc = 5,
                MaxProcessorDc = 100,
                CoreParkingMinDc = 100,           // deixa o Windows estacionar
                CoolingPolicyDc = 1,
                PcieLinkStateDc = PcieMaximum,     // economia na bateria
                UsbSuspendDc = 1,                  // economia na bateria
                DiskIdleDc = diskAc,

                GameCpuPriority = prio,
                ExcludeGameFromBalancer = excludeFromBalancer,
                AggressiveDisk = diskAc == 0,
                RaiseMinProcessor = minAc > 5,
                PinGameCores = false,              // Intel desaconselha
                AllowPerProcessEnergy = true
            };
        }

        private static Row Sim(int eppAc, int boostAc, string prio, int minAc, int maxAc,
                               int pcieAc, int usbAc, int diskAc, bool excludeFromBalancer)
        {
            var r = Gamer(eppAc, boostAc, prio, minAc, maxAc, pcieAc, usbAc, diskAc, excludeFromBalancer);
            return new Row
            {
                Profile = IntelligentProfileType.GamerSimulation,
                EppAc = r.EppAc, BoostModeAc = r.BoostModeAc, MinProcessorAc = r.MinProcessorAc,
                MaxProcessorAc = r.MaxProcessorAc, CoreParkingMinAc = CoreParkingAlwaysAllowed, CoolingPolicyAc = 1,
                PcieLinkStateAc = pcieAc, UsbSuspendAc = usbAc, DiskIdleAc = diskAc,
                EppDc = r.EppDc, BoostModeDc = r.BoostModeDc, MinProcessorDc = r.MinProcessorDc,
                MaxProcessorDc = r.MaxProcessorDc, CoreParkingMinDc = r.CoreParkingMinDc,
                CoolingPolicyDc = 1, PcieLinkStateDc = r.PcieLinkStateDc,
                UsbSuspendDc = r.UsbSuspendDc, DiskIdleDc = r.DiskIdleDc,
                GameCpuPriority = prio,
                ExcludeGameFromBalancer = excludeFromBalancer,
                AggressiveDisk = diskAc == 0,
                RaiseMinProcessor = minAc > 5,
                PinGameCores = false,
                AllowPerProcessEnergy = true
            };
        }

        private static Row Mmo(int eppAc, int boostAc, string prio, int minAc, int maxAc,
                               int pcieAc, int usbAc, int diskAc, bool excludeFromBalancer)
        {
            return new Row
            {
                EppAc = eppAc,
                BoostModeAc = boostAc,
                MinProcessorAc = minAc,          // núcleo mín alto: sem latência de wake
                MaxProcessorAc = maxAc,
                CoreParkingMinAc = CoreParkingAlwaysAllowed,
                CoolingPolicyAc = 1,
                PcieLinkStateAc = pcieAc,
                UsbSuspendAc = usbAc,
                DiskIdleAc = diskAc,             // 0 = disco nunca espera (streaming)
                EppDc = WindowsBaselineEppDc,
                BoostModeDc = BoostAggressive,
                MinProcessorDc = 5,
                MaxProcessorDc = 100,
                CoreParkingMinDc = 100,
                CoolingPolicyDc = 1,
                PcieLinkStateDc = PcieMaximum,
                UsbSuspendDc = 1,
                DiskIdleDc = diskAc,
                GameCpuPriority = prio,
                ExcludeGameFromBalancer = excludeFromBalancer,
                AggressiveDisk = true,
                RaiseMinProcessor = true,
                PinGameCores = false,
                AllowPerProcessEnergy = true
            };
        }

        private static Row Strategy(int eppAc, int boostAc, string prio, int minAc, int maxAc,
                                    int pcieAc, int usbAc, int diskAc, bool excludeFromBalancer)
        {
            return new Row
            {
                EppAc = eppAc,
                BoostModeAc = boostAc,
                MinProcessorAc = minAc,          // UI responde
                MaxProcessorAc = maxAc,
                CoreParkingMinAc = CoreParkingAlwaysAllowed,
                CoolingPolicyAc = 1,
                PcieLinkStateAc = pcieAc,
                UsbSuspendAc = usbAc,
                DiskIdleAc = diskAc,
                EppDc = WindowsBaselineEppDc,
                BoostModeDc = BoostAggressive,
                MinProcessorDc = 5,
                MaxProcessorDc = 100,
                CoreParkingMinDc = 100,
                CoolingPolicyDc = 1,
                PcieLinkStateDc = PcieMaximum,
                UsbSuspendDc = 1,
                DiskIdleDc = diskAc,
                GameCpuPriority = prio,
                ExcludeGameFromBalancer = excludeFromBalancer,
                AggressiveDisk = diskAc == 0,
                RaiseMinProcessor = true,
                PinGameCores = false,
                AllowPerProcessEnergy = true
            };
        }

        private static Row Creative(int eppAc, int maxProc, int coolAc, int pcieAc, int pcieDc)
            {
                return new Row
                {
                    EppAc = eppAc,
                    // [FIX:PISO-FABRICA] Era BoostEnabled fixo, ignorando o
                    // argumento, com o comentario "render e sustentado". Render de
                    // video e justamente o caso em que boost nao pode ser
                    // desligado: e' uma rajada de codificacao curta, e o boost
                    // existe para entregar o pico dela. Boost suave aqui tirava
                    // quadro de preview sem nenhuma medicao a favor.
                    BoostModeAc = BoostAggressive,
                    MinProcessorAc = 5,
                    MaxProcessorAc = maxProc,         // 100: piso de fabrica
                    CoreParkingMinAc = CoreParkingAlwaysAllowed,
                    CoolingPolicyAc = coolAc,         // ATIVA: folga têrmica
                    PcieLinkStateAc = pcieAc,
                    UsbSuspendAc = 0,                 // placa de vídeo não pode suspender
                    DiskIdleAc = 0,                   // nunca esperar: leitura de footage
                    // EppDc era 200, valor FORA da escala do EPP (0-100). Um
                    // valor invalido aqui nao era um valor conservador: era um
                    // valor sem significado, e a API trunca ou rejeita.
                    EppDc = WindowsBaselineEppDc,
                    BoostModeDc = BoostAggressive,
                MinProcessorDc = 5,
                MaxProcessorDc = 100,
                CoreParkingMinDc = 100,
                CoolingPolicyDc = 1,
                PcieLinkStateDc = pcieDc,
                UsbSuspendDc = 1,
                DiskIdleDc = 0,
                GameCpuPriority = "Normal",
                ExcludeGameFromBalancer = false,
                AggressiveDisk = true,
                RaiseMinProcessor = false,
                PinGameCores = false,
                AllowPerProcessEnergy = false    // nada por processo fora de jogo
            };
        }

        private static Row Dev(int eppAc, int boostAc, int maxProc, int coolAc, int pcieAc, int pcieDc)
        {
            return new Row
            {
                EppAc = eppAc,
                BoostModeAc = boostAc,           // agressivo só no tier capaz
                MinProcessorAc = 5,
                MaxProcessorAc = maxProc,
                CoreParkingMinAc = CoreParkingAlwaysAllowed,
                CoolingPolicyAc = coolAc,
                PcieLinkStateAc = pcieAc,
                UsbSuspendAc = 0,                 // SSD e periféricos de build
                DiskIdleAc = 0,                   // indexação e node_modules
                EppDc = WindowsBaselineEppDc,
                BoostModeDc = BoostAggressive,
                MinProcessorDc = 5,
                MaxProcessorDc = 100,
                CoreParkingMinDc = 100,
                CoolingPolicyDc = 1,
                PcieLinkStateDc = pcieDc,
                UsbSuspendDc = 1,
                DiskIdleDc = 0,
                GameCpuPriority = "Normal",
                ExcludeGameFromBalancer = false,
                AggressiveDisk = true,
                RaiseMinProcessor = false,
                PinGameCores = false,
                AllowPerProcessEnergy = false
            };
        }

        private static Row Office(int eppAc, int boostAc, int maxProc, int minProc, int coolAc)
        {
            return new Row
            {
                EppAc = eppAc,
                BoostModeAc = boostAc,            // boost DESLIGADO no escritório
                MinProcessorAc = minProc,
                MaxProcessorAc = maxProc,
                CoreParkingMinAc = 100,           // deixa estacionar: barulho e calor
                CoolingPolicyAc = coolAc,         // PASSIVA: silêncio tem prioridade
                PcieLinkStateAc = PcieMaximum,
                UsbSuspendAc = 1,
                DiskIdleAc = 120,
                EppDc = WindowsBaselineEppDc,
                BoostModeDc = BoostAggressive,
                MinProcessorDc = 5,
                MaxProcessorDc = 100,
                CoreParkingMinDc = 100,
                CoolingPolicyDc = 0,
                PcieLinkStateDc = PcieMaximum,
                UsbSuspendDc = 1,
                DiskIdleDc = 120,
                GameCpuPriority = "Normal",
                ExcludeGameFromBalancer = false,
                AggressiveDisk = false,
                RaiseMinProcessor = false,
                PinGameCores = false,
                AllowPerProcessEnergy = false
            };
        }

        private static Row Enterprise(int eppAc, int boostAc, int maxProc, int coolAc, int pcieAc)
        {
            return new Row
            {
                EppAc = eppAc,
                BoostModeAc = boostAc,            // boost DESLIGADO
                MinProcessorAc = 5,
                MaxProcessorAc = maxProc,
                CoreParkingMinAc = 100,
                CoolingPolicyAc = coolAc,         // ATIVA:prolongar a vida do hardware
                PcieLinkStateAc = pcieAc,         // ASPM máximo
                UsbSuspendAc = 1,
                DiskIdleAc = 300,
                EppDc = WindowsBaselineEppDc,
                BoostModeDc = BoostAggressive,
                MinProcessorDc = 5,
                MaxProcessorDc = 100,
                CoreParkingMinDc = 100,
                CoolingPolicyDc = 0,
                PcieLinkStateDc = PcieMaximum,
                UsbSuspendDc = 1,
                DiskIdleDc = 300,
                GameCpuPriority = "Normal",
                ExcludeGameFromBalancer = false,
                AggressiveDisk = false,
                RaiseMinProcessor = false,
                PinGameCores = false,
                AllowPerProcessEnergy = false
            };
        }

        private static Row General(int eppAc, int boostAc, int maxProc, int minProc, int coolAc, int pcieAc)
        {
            return new Row
            {
                EppAc = eppAc,
                BoostModeAc = boostAc,
                MinProcessorAc = minProc,
                MaxProcessorAc = maxProc,
                CoreParkingMinAc = 100,
                CoolingPolicyAc = coolAc,
                PcieLinkStateAc = pcieAc,
                UsbSuspendAc = 1,
                DiskIdleAc = 120,
                EppDc = WindowsBaselineEppDc,
                BoostModeDc = BoostAggressive,
                MinProcessorDc = 5,
                MaxProcessorDc = 100,
                CoreParkingMinDc = 100,
                CoolingPolicyDc = 0,
                PcieLinkStateDc = PcieMaximum,
                UsbSuspendDc = 1,
                DiskIdleDc = 120,
                GameCpuPriority = "Normal",
                ExcludeGameFromBalancer = false,
                AggressiveDisk = false,
                RaiseMinProcessor = false,
                PinGameCores = false,
                AllowPerProcessEnergy = false
            };
        }
        /// <summary>
        /// [FIX:POWER-TIER] O PLANO QUE A MÁQUINA VAI USAR.
        ///
        /// AREGATIVO, NÃO SUBSTITUTIVO
        /// ============================
        /// Os dez planos "Voltris - {Perfil}" que já existiam CONTINUAM
        /// existindo. Nada foi renomeado nem apagado — uma máquina capaz continua
        /// usando exatamente o plano que usava antes.
        ///
        /// O que é NOVO é o plano "Voltris - Equilibrado", que entra AO LADO
        /// deles na lista de Opções de Energia do Windows. Ele existe para um
        /// único motivo: máquinas que a MEDIÇÃO classificou como limitadas por
        /// potência (ultrabook fino, como o NP550XDA, com PL1 de 15W travado
        /// na BIOS).
        ///
        /// E ele precisa ser VISÍVEL pelo motivo que o usuário pediu: quando
        /// alguém abre Opções de Energia e vê "Voltris - Equilibrado" marcado
        /// como ativo, entende na hora que o app reconheceu a limitação da
        /// máquina e está se comportando de forma conservadora. Um plano
        /// escondido não prova nada.
        ///
        /// Por que não reaproveitar "Voltris - Game Competitivo" nesses casos:
        /// porque o nome promete performance e o conteúdo seria conservador.
        /// Nome e conteúdo precisam concordar, senão o app volta a mentir — que é
        /// exatamente o defeito que estamos corrigindo.
        /// </summary>
        public const string PlanEquilibrado = "Voltris - Equilibrado";

        /// <summary>
        /// [FIX:UNICA-FONTE] Prefixo único dos planos do Perfil Inteligente.
        ///
        /// Existe UM plano por perfil, nomeado "Voltris - {Perfil}", e o
        /// "Voltris - Equilibrado" para máquina limitada por potência. Não existe
        /// mais a família de dez nomes diferentes criada pelo serviço legado.
        /// </summary>
        public const string VoltrisPlanPrefix = "Voltris -";

        /// <summary>
        /// Devolve o nome do plano a ativar para um perfil num tier.
        ///
        /// Lembre-se: os nomes por perfil vivem em
        /// <c>o servico legado.GetVoltrisPlanName</c>, e é de lá que
        /// vem a lista existente. Este método só decide entre "usar o plano do
        /// perfil" e "usar o plano Equilibrado novo".
        /// </summary>
        public static string ResolvePlanName(IntelligentProfileType profile, PowerCapabilityTier tier)
        {
            // [FIX:MEASURE-GUARD] `Unknown` TEM DE SER TRATADO ANTES DE VIRAR
            // `Balanced`.
            //
            // A primeira versão convertia `Unknown` para `Balanced` no topo do
            // método e só depois testava os casos. Resultado: a comparação
            // `== Unknown` nunca era verdadeira, e uma medição inválida ia para o
            // plano por perfil ("Voltris - Game MMO") em vez do plano
            // conservador. É o tipo de erro que só aparece quando se olha o log:
            // o tier dizia Unknown e o plano dizia o contrário.
            if (tier == PowerCapabilityTier.PowerLimited || tier == PowerCapabilityTier.Unknown)
            {
                return PlanEquilibrado;
            }

            // [FIX:UNICA-FONTE] O nome do plano por perfil saiu de
            // `o servico legado`, o serviço legado que criava e
            // renomeava os dez planos "Voltris - {Perfil}" — e que era o segundo
            // criador de planos do app (foi ele que gerou os duplicados com o
            // mesmo nome e configurações opostas).
            //
            // Agora o Perfil Inteligente tem UM plano só, com o nome que descreve
            // o que ele faz. Em vez de mentir o nome ("Game Competitivo" num
            // plano com EPP 45), o plano se chama pelo perfil e a tabela explica
            // os valores no log.
            return $"{VoltrisPlanPrefix} {profile}";
        }

        private static PowerCapabilityTier TierForEpp(int epp) => epp switch
        {
            <= 10 => PowerCapabilityTier.SustainedPerformer,
            <= 40 => PowerCapabilityTier.Balanced,
            _ => PowerCapabilityTier.PowerLimited
        };
    }
}
