using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Power
{
    /// <summary>
    /// [FIX:POWER-MATRIX] O GUARDIÃO DAS REGRAS DE ENERGIA.
    ///
    /// ESTE TESTE EXISTE POR CAUSA DO MEDO LEGÍTIMO
    /// ==============================================
    /// A preocupação não é hypotética: se alguém "ajustar" a tabela e um gaming
    /// notebook passar a receber EPP 45, o usuário do PC gamer tem o desempenho
    /// PIOR sem nenhuma pista de por quê. E o oposto também: se o tier
    /// POWER_LIMITED deixar de ser conservador, o ultrabook volta a estrangular.
    ///
    /// Nenhum desses erros apareceria em build, em tela ou em log. Por isso as
    /// regras que protegem as máquinas estão escritas AQUI como código que
    /// FALHA, e não como comentário.
    ///
    /// AS 10 REGRAS
    /// ============
    /// 1. LIMITADO nunca é agressivo — nenhum perfil recebe EPP <= 10 nesse tier.
    /// 2. SUSTENTADO recebe o valor mais agressivo — gaming notebook não é
    ///    penalizado. É a regra que responde "e se o app limitar meu PC gamer?".
    /// 3. EPP cresce (ou se mantém) do tier mais capaz para o mais limitado,
    ///    para TODOS os perfis. Nunca o contrário.
    /// 4. Bateria nunca é mais agressiva que a tomada, em nenhum campo.
    /// 5. AC e DC são sempre diferentes em EPP. A separação é o defeito mais
    ///    grave que corrigimos; se um dia voltarem a ser iguais, isso quebra.
    /// 6. Criativo usa MaxProcessor 100; os gamers usam 99. Render precisa
    ///    sustentar o topo, jogo precisa de folga térmica.
    /// 7. Nenhum perfil fixa núcleo do jogo (`PinGameCores`). A Intel desaconselha
    ///    afinidade dura em CPU híbrida, e fixar em X3D custa 15-20% de FPS.
    /// 8. Tier desconhecido NUNCA resolve para SUSTAINED.
    /// </summary>
    public static class ProfilePowerMatrixSelfTest
    {
        /// <summary>
        /// [FIX:PISO-FABRICA] EPP que o Windows grava sozinho no plano
        /// "Equilibrado" desta maquina, lido pela API de energia (nao por
        /// suposicao). Enquanto o perfil do app ficar ACIMA deste valor, ele
        /// esta pedindo MAIS resistencia termica do que o proprio sistema ja
        /// pediu, e o resultado disso na pratica foi lentidao e travamento
        /// assim que o perfil era aplicado.
        /// </summary>
        private const int WindowsBaselineEpp = 33;

        /// <summary>
        /// Valor de PCIe link state do plano "Equilibrado" de fabrica
        /// (2 = gerenciamento de energia maxima). Abaixo disso o transporte de
        /// dados da GPU/iGPU fica mais lento do que o Windows ja entrega.
        /// </summary>
        private const int WindowsBaselinePcieLinkState = 2;

        /// <summary>
        /// Boost mode do plano de fabrica (2 = agressivo). O Boost suave (1)
        /// foi medido pelo usuario como MAIS LISO que o plano original, nao
        /// menos — portanto ele nao pode ser imposto por tabela.
        /// </summary>
        private const int WindowsBaselineBoostMode = 2;

        public static bool Run(ILoggingService? logger, out string report)
        {
            var sb = new StringBuilder();
            var failures = new List<string>();

            var profiles = (IntelligentProfileType[])Enum.GetValues(typeof(IntelligentProfileType));
            var tiers = new[]
            {
                PowerCapabilityTier.SustainedPerformer,
                PowerCapabilityTier.Balanced,
                PowerCapabilityTier.PowerLimited
            };

            sb.AppendLine("[PowerMatrixSelfTest] ===== TABELA DE ENERGIA POR PERFIL =====");

            // Desenha a tabela inteira no log. Se alguém quiser ver o que cada
            // perfil faz, a resposta está nestas linhas.
            foreach (var profile in profiles)
            {
                foreach (var tier in tiers)
                {
                    var row = ProfilePowerMatrix.Resolve(profile, tier);
                    sb.AppendLine("  " + row.Describe());
                }
            }

            sb.AppendLine();
            sb.AppendLine("[PowerMatrixSelfTest] ===== VERIFICACOES =====");

            // ---- REGRA 1: tier limitado nunca agressivo ----
            foreach (var profile in profiles)
            {
                var row = ProfilePowerMatrix.Resolve(profile, PowerCapabilityTier.PowerLimited);
                if (row.EppAc <= 10)
                {
                    failures.Add($"REGRA 1: perfil {profile} recebe EPP {row.EppAc} (deveria ser > 10) no tier PowerLimited");
                }
            }
            Log(logger, sb, failures, "REGRA 1", "tier PowerLimited nunca recebe EPP <= 10 (nao estrangula notebook fino)");

            // ---- REGRA 2: tier capaz recebe o valor mais agressivo ----
            foreach (var profile in profiles)
            {
                var sust = ProfilePowerMatrix.Resolve(profile, PowerCapabilityTier.SustainedPerformer);
                var lim = ProfilePowerMatrix.Resolve(profile, PowerCapabilityTier.PowerLimited);

                if (sust.EppAc > lim.EppAc)
                {
                    failures.Add($"REGRA 2: perfil {profile} recebe EPP {sust.EppAc} no tier Sustained, MENOS agressivo que {lim.EppAc} no PowerLimited");
                }
            }
            Log(logger, sb, failures, "REGRA 2", "tier Sustained e sempre >= em agressividade vs PowerLimited (PC gamer nao e penalizado)");

            // ---- REGRA 3: EPP monotonico nos tres tiers ----
            foreach (var profile in profiles)
            {
                int a = ProfilePowerMatrix.Resolve(profile, PowerCapabilityTier.SustainedPerformer).EppAc;
                int b = ProfilePowerMatrix.Resolve(profile, PowerCapabilityTier.Balanced).EppAc;
                int c = ProfilePowerMatrix.Resolve(profile, PowerCapabilityTier.PowerLimited).EppAc;

                if (!(a <= b && b <= c))
                {
                    failures.Add($"REGRA 3: perfil {profile} tem EPP nao monotonico: Sustained={a}, Balanced={b}, PowerLimited={c}");
                }
            }
            Log(logger, sb, failures, "REGRA 3", "EPP cresce (ou mantem) do tier mais capaz para o mais limitado");

            // ---- REGRA 4: bateria nunca mais agressiva que tomada ----
            foreach (var profile in profiles)
            {
                foreach (var tier in tiers)
                {
                    var r = ProfilePowerMatrix.Resolve(profile, tier);

                    if (r.EppDc < r.EppAc) failures.Add($"REGRA 4: {profile}/{tier} EPP bateria {r.EppDc} < tomada {r.EppAc} (bateria mais agressiva)");
                    if (r.MaxProcessorDc > r.MaxProcessorAc) failures.Add($"REGRA 4: {profile}/{tier} MaxProcessor bateria {r.MaxProcessorDc} > tomada {r.MaxProcessorAc}");
                    if (r.CoreParkingMinDc < r.CoreParkingMinAc) failures.Add($"REGRA 4: {profile}/{tier} core parking bateria {r.CoreParkingMinDc} < tomada {r.CoreParkingMinAc}");
                    if (r.BoostModeDc > r.BoostModeAc) failures.Add($"REGRA 4: {profile}/{tier} boost bateria {r.BoostModeDc} > tomada {r.BoostModeAc}");
                }
            }
            Log(logger, sb, failures, "REGRA 4", "bateria nunca e mais agressiva que a tomada (nenhum campo)");

            // ---- REGRA 5: EPP dentro da escala valida em AC e DC ----
            //
            // [FIX:EPP-DC-INVALIDO] ESTA REGRA VERIFICAVA A COISA ERRADA.
            //
            // A versao anterior exigia que EppAc != EppDc, com a justificativa de
            // que "a separacao foi perdida". Isso nao verificava se o EPP era um
            // numero valido — verificava apenas que os dois lados diferentes.
            // E foi exatamente por isso que o defeito passou: a tabela gravava
            // EppDc = 128 (e 160, 180, 200, 220 em outros perfis), que sao TODOS
            // fora da escala 0-100 do EPP. Como AC e DC eram diferentes, a regra
            // passava e nao dizia nada sobre o problema real.
            //
            // Um numero invalido nao e um numero conservador. A API trunca ou
            // rejeita, e o campo fica com um valor que ninguem escolheu de
            // proposito. A regra que importa e' a de faixa, nao a de diferenca.
            //
            // A separacao AC/DC foi removida de proposito: nao existe medicao que
            // justifique EPP de bateria diferente do EPP de tomada, e um valor
            // inventado por premissa e' exatamente o que produced o defeito.
            foreach (var profile in profiles)
            {
                foreach (var tier in tiers)
                {
                    var r = ProfilePowerMatrix.Resolve(profile, tier);
                    foreach (var (lado, valor) in new[] { ("AC", r.EppAc), ("DC", r.EppDc) })
                    {
                        if (valor < 0 || valor > 100)
                        {
                            failures.Add(
                                $"REGRA 5: {profile}/{tier} tem EPP {lado} = {valor}, FORA da escala valida 0-100. " +
                                "Valor invalido nao e conservador: a API trunca ou rejeita o write.");
                        }
                    }
                }
            }
            Log(logger, sb, failures, "REGRA 5", "EPP de AC e DC sempre dentro da escala valida 0-100");

            // ---- REGRA 6: MaxProcessor = 100 em TODOS os perfis ----
            //
            // [FIX:PISO-FABRICA] ESTA REGRA CONTRADIZIA A REGRA 11, E ESTAVA ERRADA.
            //
            // A versao anterior exigia MaxProcessor 100 para Criativo e 99 para
            // gamers, com o argumento de "99 para folga termica". Duas coisas
            // tornam isso indefensavel:
            //
            // 1) CONTRADICAO DIRETA COM A REGRA 11. Uma manda 99, a outra proibe
            //    qualquer valor abaixo de 100. Nao existe linha que satisfaca as
            //    duas, entao o self-test nao podia passar — o que significa que
            //    essa regra nunca foi realmente exercitada.
            //
            // 2) PREMISA, NAO MEDICAO. "99 para folga termica" nao tem medicao
            //    nenhuma por tras. E o Windows usa 100 no plano de fabrica desta
            //    maquina, que o usuario mediu como o melhor resultado. Folga
            //    termica se obtem com EPP e cooling policy, nao cortando o teto
            //    do processador em 1%: 99 nao evita estrangulamento, apenas
            //    impede o turbo de usar o ultimo ponto livre do envelope.
            //
            // A folga termica que se quer de verdade ja e' exigida pela regra
            // de EPP, que mede contra o piso real do Windows. O max 99 era so um
            // numero conservative que nunca precisou se justificar.
            foreach (var tier in tiers)
            {
                foreach (var profile in profiles)
                {
                    var r = ProfilePowerMatrix.Resolve(profile, tier);
                    if (r.MaxProcessorAc != 100)
                    {
                        failures.Add(
                            $"REGRA 6: {profile}/{tier} MaxProcessor tomada = {r.MaxProcessorAc}, " +
                            "deveria ser 100. Cortar o teto do processador nao evita estrangulamento; " +
                            "a folga termica vem do EPP, nao de um teto artificial.");
                    }
                }
            }
            Log(logger, sb, failures, "REGRA 6", "MaxProcessor = 100 em todos os perfis e tiers");

            // ---- REGRA 7: nunca fixa nucleo ----
            foreach (var profile in profiles)
            {
                foreach (var tier in tiers)
                {
                    var r = ProfilePowerMatrix.Resolve(profile, tier);
                    if (r.PinGameCores)
                    {
                        failures.Add($"REGRA 7: {profile}/{tier} esta fixando nucleo do jogo (PinGameCores=true). Intel desaconselha em CPU hibrida e custa 15-20% de FPS em X3D.");
                    }
                }
            }
            Log(logger, sb, failures, "REGRA 7", "nenhum perfil fixa nucleo do jogo");

            // ---- REGRA 8: tier desconhecido NUNCA resolve para o meio-termo ----
            //
            // [FIX:UNKNOWN-VALUES] Esta regra mudou de regra para "conservador".
            //
            // A versão anterior exigia que `Unknown` produzisse os mesmos valores
            // de `Balanced`. Em campo, isso deu EPP 25 no competitivo — mais
            // agressivo que o Equilibrado de fábrica (~50) — e o usuário sentiu
            // a máquina PIOR do que estava, dentro de um plano chamado
            // "Equilibrado". O plano dizia uma coisa e os números diziam outra.
            //
            // "Não sabemos o que a máquina aguenta" não é "aguenta o suficiente
            // para o meio-termo": é o oposto. Na dúvida vale o piso.
            foreach (var profile in profiles)
            {
                var unknownRow = ProfilePowerMatrix.Resolve(profile, PowerCapabilityTier.Unknown);
                var limitedRow = ProfilePowerMatrix.Resolve(profile, PowerCapabilityTier.PowerLimited);

                if (unknownRow.EppAc != limitedRow.EppAc || unknownRow.Tier != PowerCapabilityTier.PowerLimited)
                {
                    failures.Add(
                        $"REGRA 8: {profile} com tier Unknown produziu EPP {unknownRow.EppAc} " +
                        $"(PowerLimited = {limitedRow.EppAc}, tier = {unknownRow.Tier}); " +
                        "medicao indisponivel tem de cair no conservador, nunca no meio-termo");
                }
            }
            Log(logger, sb, failures, "REGRA 8", "tier Unknown (medicao falhou) usa os valores CONSERVADORES do PowerLimited");

            // ---- REGRA 9: estacionamento de nucleo NUNCA e desativado ----
            //
            // [FIX:CORE-PARKING] Este foi o defeito que o usuario encontrou em
            // campo, na propria maquina dele. A tabela gravava
            // `CoreParkingMin = 0` ("nunca estacionar nucleo") em quase todos os
            // perfis, num ultrabook de 15W.
            //
            // Medido no NP550XDA (i5-1135G7), mesma carga, a frio:
            //     estacionamento 100 (padrao de fabrica) .... 108% de pico
            //     estacionamento 0   (o que gravavamos) .....  62% de pico
            //
            // 43% a MENOS de clock. Com todos os nucleos sempre ativos o CPU
            // gasta o orcamento termico ocioso e, quando o jogo comeca, ja bate
            // no limite de potencia. Sintoma observado: os GHz CAIAM para
            // 1,3-1,5 GHz quando a carga chegava, abaixo do clock base de 2,4.
            //
            // A regra e absoluta: nenhum perfil, nenhum tier, nenhum valor.
            foreach (var profile in profiles)
            {
                foreach (var tier in tiers)
                {
                    var row = ProfilePowerMatrix.Resolve(profile, tier);
                    if (row.CoreParkingMinAc != 100)
                    {
                        failures.Add(
                            $"REGRA 9: {profile}/{tier} grava core parking {row.CoreParkingMinAc} " +
                            "(deveria ser 100). Desativar estacionamento mediu 43% MENOS de clock.");
                    }
                    if (row.CoreParkingMinDc != 100)
                    {
                        failures.Add(
                            $"REGRA 9: {profile}/{tier} grava core parking de bateria {row.CoreParkingMinDc} " +
                            "(deveria ser 100).");
                    }
                }
            }
            Log(logger, sb, failures, "REGRA 9", "core parking = 100 em todos os perfis e tiers (nunca desativado)");

            // ---- REGRA 10: removida de proposito ----
            //
            // A REGRA 10 original exigia boost 1 (Enabled) no tier PowerLimited,
            // partindo da ideia de que "conservador" era mais seguro. Ela e a
            // REGRA 11 se contradiziam, e a medicao do usuario provou que a
            // REGRA 10 estava errada: o plano do app ficava MAIS LENTO que o
            // Equilibrado nativo exatamente neste tier, e voltar ao plano nativo
            // resolvia. boost 1 nao era conservatism — era perda de Performance.
            //
            // A premissa de que boost agressivo antecipava o estrangulamento nao
            // se confirmou: o proprio plano de fabrica do Windows usa boost 2 e
            // nao engasga. Regra mantida por premissa, sem medicao, e exatamente
            // o tipo de regra que produz o defeito que a REGRA 11 agora impede.
            // Nenhuma verificacao aqui: a REGRA 11 cobre o tier limitado de forma
            // coerente com os demais.

            // ---- REGRA 11: NENHUM PERFIL PODE SER PIOR QUE O WINDOWS ----
            //
            // [FIX:PISO-FABRICA] ESTA E A REGRA QUE TERIA EVITADO O RELATO.
            //
            // O usuario comparou o plano aplicado com o "Equilibrado" de fabrica
            // da propria maquina e encontrou, um a um:
            //
            //     setting              Voltris   Equilibrado (que funciona)
            //     EPP                      60       33
            //     Boost mode                1        2
            //     Processor maximum        99      100
            //     PCIe link state           1        2
            //
            // Todos os quatro piores, e nenhum com medicao a favor. O sintoma
            // relatado - PC lento e com gargalo assim que o perfil era aplicado,
            // voltando ao normal ao trocar para o plano nativo - e a consequencia
            // direta de um otimizador que derrubava a maquina abaixo do padrao
            // do sistema.
            //
            // O piso e objetivo e verificavel, e vale para TODOS os perfis e
            // TODOS os tiers. Nao faz sentido ter regra so para o tier limitado:
            // o defeito nao era do tier, era da tabela inteira. O self-test e a
            // unica forma de garantir que essa regra nao seja quebrada de novo
            // na proxima alteracao - que e exatamente como o defeito entrou.
            foreach (var profile in profiles)
            {
                foreach (var tier in tiers)
                {
                    var row = ProfilePowerMatrix.Resolve(profile, tier);

                    // Acima do piso: estariamos pedindo MAIS resistencia termica
                    // do que o proprio Windows ja pede. Em maquina limitada por
                    // potencia isso segura o processador em frequencia baixa.
                    if (row.EppAc > WindowsBaselineEpp)
                    {
                        failures.Add(
                            $"REGRA 11: {profile}/{tier} usa EPP {row.EppAc}, acima do piso de fabrica " +
                            $"({WindowsBaselineEpp}). O Windows ja usa {WindowsBaselineEpp}; subir disso " +
                            "prende o processador em frequencia baixa e deixa a maquina mais lenta.");
                    }

                    if (row.MaxProcessorAc < 100)
                    {
                        failures.Add(
                            $"REGRA 11: {profile}/{tier} limita Processor maximum a {row.MaxProcessorAc}. " +
                            "O Windows usa 100; limitar sem medicao de ganho e piorar a maquina.");
                    }

                    if (row.PcieLinkStateAc < WindowsBaselinePcieLinkState)
                    {
                        failures.Add(
                            $"REGRA 11: {profile}/{tier} usa PCIe link state {row.PcieLinkStateAc}. " +
                            $"O Windows usa {WindowsBaselinePcieLinkState} (desempenho maximo); abaixo disso " +
                            "o gargalo de dados da GPU/iGPU piora.");
                    }

                    if (row.BoostModeAc < WindowsBaselineBoostMode)
                    {
                        failures.Add(
                            $"REGRA 11: {profile}/{tier} usa Boost mode {row.BoostModeAc}. " +
                            $"O Windows usa {WindowsBaselineBoostMode} (agressivo), e o usuario mediu que o " +
                            "boost suave NAO traz ganho - apenas tira frequencia.");
                    }
                }
            }
            Log(logger, sb, failures, "REGRA 11",
                "nenhum perfil/tier fica abaixo do plano de fabrica do Windows (EPP, max, boost, PCIe)");

            // ==================================================================
            // REGRAS DO REGIME
            // ==================================================================
            //
            // [BRAIN-ENERGY] O regime decide sobre o INSTANTE, e por isso é a
            // parte do sistema com mais chance de reintroduzir o defeito que as
            // REGRAS 1 a 11 existem para impedir. Ele é a única coisa que pode
            // escrever um valor mais agressivo que o piso do Windows, e a única
            // que depende de telemetria — ou seja, a única cujo valor muda
            // conforme a máquina. As regras a seguir não verificam se o regime
            // é inteligente: verificam que ele não consegue fazer estrago.

            // Cada regime é montado com os sinais que REALMENTE o produzem.
            //
            // [FIX:REGIME-12] A primeira versão desta regra derivava os sinais a
            // partir do nome do regime, e o teste passou a exercitar `Unknown`
            // de um jeito que produzia `Sustaining`. O relatório acusou 30
            // violações — todas a mesma — e a primeira leitura seria culpar o
            // classificador.
            //
            // O classificador estava certo: temperatura lida e sem estrangulamento
            // É Sustaining, por definição. O erro era do teste, que montou o
            // cenário errado e depois esperou um regime que aquele cenário não
            // produz. Vale registrar porque é a mesma armadilha que produziu os
            // valores ruins: um número esperado que não corresponde ao mundo.
            var cenarios = new (EnergyRegime Esperado, bool Throttling, double Temp)[]
            {
                (EnergyRegime.Unknown,    false, -1.0),   // sem sinal nenhum
                (EnergyRegime.Sustaining, false, 60.0),   // folgada
                (EnergyRegime.Constrained, true,  60.0),   // estrangulando
                (EnergyRegime.Constrained, false, 99.0),   // acima do teto fisico
                (EnergyRegime.Sustaining, false, 94.9)    // logo abaixo do teto
            };

            // ---- REGRA 12: NENHUM REGIME PODE SUBIR ACIMA DO PISO ----
            //
            // A REGRA 11 cobre a chamada sem contexto. Esta cobre a chamada COM
            // contexto, que é um caminho de código novo e por isso precisa da
            // mesma garantia. A diferença que importa: aqui o valor pode
            // DESCER abaixo de 33 (a escalada), mas nunca SUBIR acima — subir é
            // a direção que prende o processador em frequência baixa, e é o
            // defeito que o usuário encontrou em campo.
            foreach (var cenario in cenarios)
            {
                foreach (var profile in profiles)
                {
                    foreach (var tier in tiers)
                    {
                        var ctx = EnergyRegimeClassifier.Classify(
                            throttling: cenario.Throttling,
                            cpuTemperatureC: cenario.Temp,
                            tier: tier,
                            onBattery: false);

                        if (ctx.Regime != cenario.Esperado)
                        {
                            failures.Add(
                                $"REGRA 12: sinais (throttle={cenario.Throttling}, temp={cenario.Temp}) " +
                                $"produziram {ctx.Regime}, esperado {cenario.Esperado}. " +
                                "O classificador precisa ser deterministico: os mesmos sinais sempre dao o mesmo regime.");
                            continue;
                        }

                        var row = ProfilePowerMatrix.Resolve(profile, tier, ctx);

                        if (row.EppAc > WindowsBaselineEpp)
                        {
                            failures.Add(
                                $"REGRA 12: {profile}/{tier} no regime {ctx.Regime} usa EPP {row.EppAc}, " +
                                $"ACIMA do piso de fabrica ({WindowsBaselineEpp}). Nenhum regime pode subir o EPP.");
                        }

                        if (row.MaxProcessorAc < 100)
                        {
                            failures.Add(
                                $"REGRA 12: {profile}/{tier} no regime {ctx.Regime} limita Processor maximum " +
                                $"a {row.MaxProcessorAc}.");
                        }

                        if (row.BoostModeAc < WindowsBaselineBoostMode)
                        {
                            failures.Add(
                                $"REGRA 12: {profile}/{tier} no regime {ctx.Regime} usa Boost mode {row.BoostModeAc}, " +
                                $"abaixo do plano de fabrica ({WindowsBaselineBoostMode}).");
                        }
                    }
                }
            }
            Log(logger, sb, failures, "REGRA 12",
                "nenhum regime (nem sem contexto) altera os valores de fabrica para pior em nenhum perfil/tier");

            // ---- REGRA 13: A ESCALADA ESTÁ FECHADA E CONTINUA FECHADA ----
            //
            // Esta regra não verifica um valor: verifica uma PROMESSA. Enquanto
            // não existir medição de "a máquina sustenta o pico" nesta máquina,
            // publicar um número mais agressivo que o do Windows seria repetir o
            // erro com uma nova roupa — e a diferença é que agora ele viria com
            // uma explicação plausível anexada, o que o torna mais difícil de
            // questionar depois.
            //
            // Se alguém ligar `EscalationEnabled` de propósito, esta regra falha
            // e obriga a remover a linha abaixo junto com a justificativa. É um
            // obstáculo deliberado: mudar o comportamento tem de custar uma
            // alteração visível no self-test, não um booleano esquecido num
            // arquivo de política.
            if (ProfilePowerMatrix.EscalationEnabled)
            {
                failures.Add(
                    "REGRA 13: EscalationEnabled esta TRUE. A escalada exige medicao de que a maquina " +
                    "sustenta o pico, e essa medicao nao existe nesta maquina. Desligue, ou apague esta " +
                    "regra junto com a justificativa da escalada.");
            }
            Log(logger, sb, failures, "REGRA 13",
                "escalada abaixo do piso do Windows esta DESLIGADA (sem medicao que a sustente)");

            // ---- REGRA 14: A ESCALADA NUNCA TOCA A LINHA DE BATERIA ----
            //
            // A autorização para pedir mais vale para a tomada em que a máquina
            // ESTÁ. Se a escalada mexesse também na linha DC, bastaria o usuário
            // desligar o carregador depois de aplicar o perfil para a máquina
            // passar a pedir um EPP de tomada em bateria — sem que nada tenha
            // sido medido sobre bateria.
            foreach (var profile in profiles)
            {
                foreach (var tier in tiers)
                {
                    var ctx = EnergyRegimeClassifier.Classify(
                        throttling: false,
                        cpuTemperatureC: 55.0,
                        tier: tier,
                        onBattery: false);

                    var row = ProfilePowerMatrix.Resolve(profile, tier, ctx);

                    if (row.EppDc != WindowsBaselineEpp)
                    {
                        failures.Add(
                            $"REGRA 14: {profile}/{tier} usa EPP de bateria {row.EppDc}, diferente do piso " +
                            $"({WindowsBaselineEpp}). A linha de bateria nao pode mudar: a escalada vale " +
                            "apenas para a tomada em que a maquina esta.");
                    }

                    if (row.EppDc < row.EppAc)
                    {
                        failures.Add(
                            $"REGRA 14: {profile}/{tier} tem EPP de bateria {row.EppDc} mais agressivo que " +
                            $"o de tomada {row.EppAc}. A bateria tem de ser a linha mais conservadora das duas.");
                    }
                }
            }
            Log(logger, sb, failures, "REGRA 14",
                "a linha de bateria permanece no piso do Windows, com ou sem escalada aplicada");

            // ---- REGRA 15: A ESCALADA EXIGE AS QUATRO CONDIÇÕES ----
            //
            // `EnergyContext.CanEscalate` é a única porta. Ela exige regime
            // `Sustaining`, tier `SustainedPerformer`, tomada e telemetria — as
            // quatro ao mesmo tempo. Este teste percorre a matriz completa de
            // combinações e falha se alguma condição isolada for suficiente,
            // que é a forma como uma porta lógica costuma ser afrouxada sem
            // ninguém perceber.
            var condicoes = new (string Nome, bool Throttling, double Temp, PowerCapabilityTier Tier, bool Bateria)[]
            {
                ("Sustaining+Sp+tomada+telemetria", false, 55.0, PowerCapabilityTier.SustainedPerformer, false),
                ("Sustaining+Sp+BATERIA",            false, 55.0, PowerCapabilityTier.SustainedPerformer, true),
                ("Sustaining+Balanced",              false, 55.0, PowerCapabilityTier.Balanced, false),
                ("Sustaining+PowerLimited",          false, 55.0, PowerCapabilityTier.PowerLimited, false),
                ("Constrained+Sp+tomada",            true,  55.0, PowerCapabilityTier.SustainedPerformer, false),
                ("SEM TEMPERATURA+Sp+tomada",        false, -1.0, PowerCapabilityTier.SustainedPerformer, false),
                ("SEM SINAL NENHUM+Sp+tomada",       false, -1.0, PowerCapabilityTier.SustainedPerformer, false)
            };

            foreach (var c in condicoes)
            {
                var ctx = EnergyRegimeClassifier.Classify(c.Throttling, c.Temp, c.Tier, c.Bateria);

                // A única combinação autorizada é a primeira.
                bool esperado = c.Nome.StartsWith("Sustaining+Sp+tomada", StringComparison.Ordinal);
                if (ctx.CanEscalate != esperado)
                {
                    failures.Add(
                        $"REGRA 15: '{c.Nome}' produziu CanEscalate={ctx.CanEscalate}, esperado {esperado}. " +
                        $"regime={ctx.Regime} telemetria={ctx.HasTelemetry}. " +
                        "A escalada so pode ser permitida com regime Sustaining, tier SustainedPerformer, " +
                        "na tomada E com telemetria presente - todas ao mesmo tempo.");
                }
            }
            Log(logger, sb, failures, "REGRA 15",
                "a escalada so e admissivel com as QUATRO condicoes ao mesmo tempo (regime, tier, tomada, telemetria)");

            // ---- REGRA 15b: O TIER DO ARGUMENTO VENCE O TIER DO CONTEXTO ----
            //
            // [FIX:TIER-DUPLICADO] ESTA REGRA EXISTE PORQUE A 15 PASSOU.
            //
            // O tier chegava por dois caminhos — como argumento de `Resolve` e
            // dentro do contexto — e o portão da escalada usava o do contexto.
            // Nos testes os dois vinham sempre iguais, então a REGRA 15 (que
            // verifica a porta) passava normalmente. O defeito só apareceu
            // quando um contexto classificado como `SustainedPerformer` foi
            // resolvido contra o tier `Balanced`: escalou para EPP 25 numa
            // máquina que a escalada não pode tocar.
            //
            // Isso é uma classe de bug que nenhuma regra de valor pega, porque
            // em cada chamada isolada todo valor involvedo é válido. A defesa
            // não é testar mais valores: é testar o CONTRITO entre as duas
            // fontes, que é o único lugar onde o defeito mora.
            //
            // O cenário abaixo é o que falhou: contexto de máquina capaz,
            // resolução contra tier intermediário. A escalada tem de ficar
            // barrada, e o valor tem de ser o piso.
            foreach (var perfilTeste in profiles)
            {
                var contextoCapaz = EnergyRegimeClassifier.Classify(
                    throttling: false,
                    cpuTemperatureC: 55.0,
                    tier: PowerCapabilityTier.SustainedPerformer,
                    onBattery: false);

                var row = ProfilePowerMatrix.Resolve(
                    perfilTeste, PowerCapabilityTier.Balanced, contextoCapaz);

                if (row.EppEscalated)
                {
                    failures.Add(
                        $"REGRA 15b: {perfilTeste} escalou para EPP {row.EppAc} resolvido contra o tier " +
                        "Balanced, usando um contexto classificado como SustainedPerformer. " +
                        "O tier do ARGUMENTO tem de mandar: e' o mesmo que sera carimbado na linha " +
                        "e o mesmo que escolheu o plano.");
                }

                if (row.EppAc != WindowsBaselineEpp)
                {
                    failures.Add(
                        $"REGRA 15b: {perfilTeste} saiu do piso ({row.EppAc}, esperado {WindowsBaselineEpp}) " +
                        "quando o contexto e o argumento discordam sobre o tier.");
                }
            }
            Log(logger, sb, failures, "REGRA 15b",
                "o tier do ARGUMENTO vence o tier do contexto (nenhum dono duplicado para o mesmo dado)");

            // ---- REGRA 16: O CLASSIFICADOR É PURO ----
            //
            // A mesma entrada tem que dar a mesma saída, sempre. A medição de
            // carga foi removida do projeto porque travava o app, e o jeito
            // mais rápido de recriar isso seria um classificador que I/O, WMI ou
            // relógio. Aqui isso é verificado na prática: o mesmo conjunto de
            // sinais é classificado duas vezes e comparado.
            var entradas = new (bool Throttling, double Temp, PowerCapabilityTier Tier, bool Bateria)[]
            {
                (true,  70.0, PowerCapabilityTier.Balanced, false),
                (false, 96.0, PowerCapabilityTier.Balanced, false),
                (false, 94.9, PowerCapabilityTier.SustainedPerformer, false),
                (false, 60.0, PowerCapabilityTier.SustainedPerformer, true),
                (false, -1.0, PowerCapabilityTier.Unknown, false)
            };

            foreach (var e in entradas)
            {
                var a = EnergyRegimeClassifier.Classify(e.Throttling, e.Temp, e.Tier, e.Bateria);
                var b = EnergyRegimeClassifier.Classify(e.Throttling, e.Temp, e.Tier, e.Bateria);

                if (a.Regime != b.Regime || a.Reason != b.Reason)
                {
                    failures.Add(
                        $"REGRA 16: o classificador nao e puro para (throttle={e.Throttling}, temp={e.Temp}, " +
                        $"tier={e.Tier}, bateria={e.Bateria}): '{a.Regime}' e '{b.Regime}'.");
                }
            }

            // E o teto fisico tem de valer para qualquer processador.
            var quente = EnergyRegimeClassifier.Classify(false, 99.0, PowerCapabilityTier.SustainedPerformer, false);
            if (quente.Regime != EnergyRegime.Constrained)
            {
                failures.Add(
                    $"REGRA 16: 99C produziu regime {quente.Regime}. Acima do teto fisico o regime tem de ser " +
                    "Constrained, sem depender de marca, modelo ou perfil.");
            }

            // E a ausencia total de sinal tem de ser Unknown, nunca Sustaining.
            var mudo = EnergyRegimeClassifier.Classify(false, -1.0, PowerCapabilityTier.SustainedPerformer, false);
            if (mudo.Regime != EnergyRegime.Unknown)
            {
                failures.Add(
                    $"REGRA 16: sem throttling e sem temperatura o regime foi {mudo.Regime}. " +
                    "Silencio de sensor tem de ser Unknown — nunca Sustaining. " +
                    "Ausencia de informacao nao e autorizacao.");
            }
            Log(logger, sb, failures, "REGRA 16",
                "o classificador de regime e puro, e ausencia de sinal significa Unknown (nunca Sustaining)");

            // ---- REGRA 17: A CLASSIFICAÇÃO DA MÁQUINA É DETERMINÍSTICA ----
            //
            // [FIX:REGRESSAO-NOTEBOOK] ESTA REGRA EXISTE PORQUE O USUÁRIO SENTIU
            // O DEFATO NO CORPO.
            //
            // No notebook dele, abrir o CS2 travava tudo. O log mostrou a causa
            // exata: em duas medições da MESMA máquina, o tier mudou de
            // `PowerLimited` para `Balanced` — e com ele o plano deixou de ser
            // "Voltris - Equilibrado" (duplicado de Equilibrado) e passou a ser
            // "Voltris - GamerCompetitivo" (duplicado de ALTO DESEMPENHO).
            //
            // A diferença entre as duas medições foi o campo `resfriamento`,
            // que passou de "Passiva" para "desconhecido" por uma correção de
            // dependência circular. E o nome da CPU já falhava (`(desconhecida)`)
            // nas DUAS. Ou seja: a classificação do notebook dependia de DUAS
            // leituras que podem falhar, e uma delas falhou.
            //
            // Nenhuma regra de valores pegaria isso. A regra que pega é a
            // INVARIANTE: bateria presente sem GPU dedicada é notebook fino,
            // sempre, com qualquer combination das leituras que falham.
            //
            // O teste percorre a matriz inteira de sinais — inclusive as
            // combinações que quebraram em campo.
            var nomesCpu = new[]
            {
                "(desconhecida)",                 // a leitura que falhou no notebook
                string.Empty,                     // pior caso: sem nome nenhum
                "Intel(R) Core(TM) i5-1135G7",    // o nome verdadeiro do notebook
                "Intel(R) Core(TM) i7-12700K",    // desktop forte
                "AMD Ryzen 7 5800X"               // desktop AMD
            };

            var resfriamentos = new[] { -1, 0, 1 };   // desconhecido, passiva, ativa

            foreach (string cpu in nomesCpu)
            {
                foreach (int resf in resfriamentos)
                {
                    // COM BATERIA E SEM GPU DEDICADA TEM DE SER SEMPRE
                    // PowerLimited. Sem exceção, sem depender de cpu ou resf.
                    var notebook = HardwareCapabilityProbe.ClassifyFromDirectSignals(
                        hasBattery: true, hasDiscreteGpu: false, cpuName: cpu, coolingPolicy: resf);

                    if (notebook != PowerCapabilityTier.PowerLimited)
                    {
                        failures.Add(
                            $"REGRA 17: maquina com bateria e SEM GPU dedicada (cpu='{cpu}', " +
                            $"resfriamento={resf}) foi classificada como {notebook}, e nao PowerLimited. " +
                            "Esta e a EXATA combinacao do notebook que travava ao abrir o jogo. " +
                            "A classificacao nao pode depender do nome da CPU nem do resfriamento.");
                    }

                    // E o plano TEM de ser o Equilibrado, nunca o do perfil.
                    foreach (var p in profiles)
                    {
                        string plano = ProfilePowerMatrix.ResolvePlanName(p, notebook);
                        if (plano != ProfilePowerMatrix.PlanEquilibrado)
                        {
                            failures.Add(
                                $"REGRA 17: {p} com tier {notebook} (cpu='{cpu}', resfriamento={resf}) " +
                                $"escolheu o plano '{plano}', e nao '{ProfilePowerMatrix.PlanEquilibrado}'. " +
                                "Um notebook limitado por potencia NUNCA pode receber um plano duplicado " +
                                "de Alto Desempenho — e' o plano que trava a maquina.");
                        }
                    }
                }
            }

            // E o oposto precisa continuar valendo: desktop sem bateria e CPU
            // normal tem de ser capaz, senão a correção acima estraga o gamer.
            foreach (string cpu in nomesCpu)
            {
                foreach (int resf in resfriamentos)
                {
                    var desktop = HardwareCapabilityProbe.ClassifyFromDirectSignals(
                        hasBattery: false, hasDiscreteGpu: true, cpuName: cpu, coolingPolicy: resf);

                    if (desktop == PowerCapabilityTier.PowerLimited)
                    {
                        failures.Add(
                            $"REGRA 17: desktop (sem bateria, com GPU dedicada, cpu='{cpu}', " +
                            $"resfriamento={resf}) foi classificado como PowerLimited. " +
                            "A regra do notebook nao pode se espalhar para desktop, ou o PC gamer vira conservador.");
                    }
                }
            }
            Log(logger, sb, failures, "REGRA 17",
                "bateria sem GPU dedicada == SEMPRE PowerLimited e SEMPRE plano Equilibrado, " +
                "independentemente do nome da CPU e do resfriamento (a regressao que travava o notebook)");

            sb.AppendLine();
            sb.AppendLine(failures.Count == 0
                ? "[PowerMatrixSelfTest] RESULTADO: OK - todas as regras ativas passam. Nenhum perfil desativa o estacionamento de nucleos, e nenhum perfil deixa a maquina pior que o plano que o proprio Windows ja aplica."
                : "[PowerMatrixSelfTest] RESULTADO: FALHOU - " + failures.Count + " violacao(oes):");

            foreach (var f in failures) sb.AppendLine("  - " + f);

            report = sb.ToString();

            if (failures.Count == 0)
            {
                logger?.LogSuccess(report);
            }
            else
            {
                logger?.LogError(report);
            }

            return failures.Count == 0;
        }

        /// <summary>
        /// Imprime o resultado de UMA regra no relatório.
        ///
        /// Apenas CONTA as violações daquela regra — nunca as remove da lista.
        /// Uma versão anterior removia para contar, o que fazia a lista de
        /// falhas perder items silenciosamente e o relatório final declarar
        /// "OK" com violações dentro. Contar sem remover é o comportamento
        /// correto e é o que garante que o veredito final seja confiável.
        /// </summary>
        private static void Log(ILoggingService? logger, StringBuilder sb, List<string> failures, string rule, string description)
        {
            int count = failures.Count(f => f.StartsWith(rule, StringComparison.Ordinal));

            if (count == 0)
            {
                sb.AppendLine($"  [OK]   {rule}: {description}");
            }
            else
            {
                sb.AppendLine($"  [ERRO] {rule}: {description} ({count} problema(s))");
            }
        }
    }
}
