using System;
using VoltrisOptimizer.Core.Brain.V2.AntiStutter;

namespace VoltrisOptimizer.Services.Power
{
    /// <summary>
    /// [REGIME] EM QUE ESTADO A MÁQUINA ESTA AGORA.
    ///
    /// O QUE ISTO SUBSTITUI
    /// =====================
    /// A tabela <see cref="ProfilePowerMatrix"/> até aqui respondia a uma pergunta
    /// sobre a MÁQUINA: "que tipo de equipamento é este?". A resposta vinha do
    /// tier, que é determinístico e está certo.
    ///
    /// Falta a pergunta sobre o INSTANTE: "esta máquina, AGORA, está segurando o
    /// que pedimos, ou está no limite?". Essa é a pergunta que o tier não
    /// responde, e é a que interessa na hora de decidir se vale insistir.
    ///
    /// POR QUE A SEPARAÇÃO IMPORTA
    /// ===========================
    /// Uma máquina é o que é o tempo todo: um notebook de 15W não vira desktop
    /// porque o ventilador estava ligado. Mas o ESTADO dela muda a cada segundo.
    /// Misturar as duas coisas é o que produziu os defeitos anteriores: um valor
    /// escolhido por uma premissa fixa ("notebook éConservador") sendo gravado
    /// mesmo quando a medição do instante dizia o contrário.
    ///
    /// REGRA DE DESENHO: o regime NUNCA promove sozinho. Ele pode bloquear
    /// escalada, mas só a evidência de headroom abre espaço para pedir mais. É a
    /// mesma ordem de autoridade que já vale para o tier — "estatística sugere,
    /// sinal direto manda" — aplicada ao outro lado da decisão.
    /// </summary>
    public enum EnergyRegime
    {
        /// <summary>
        /// Não há telemetria confiável. É o estado PADRÃO e o mais defensável:
        /// sem dado, vale o piso do Windows. Nunca é "neutro", é ausência de
        /// autorização para fazer qualquer coisa além do piso.
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// A máquina está com folga: nenhum sinal de estrangulamento e a
        /// temperatura está dentro do teto físico. Só neste estado a escalada
        /// pode ser considerada — e mesmo assim só com headroom provado.
        /// </summary>
        Sustaining = 1,

        /// <summary>
        /// A máquina está no limite agora. A resposta correta aqui NÃO é pedir
        /// mais: é não insistir. Insistir sobre uma máquina estrangulada é
        /// exatamente o que produz o colapso de clock observado em campo.
        /// </summary>
        Constrained = 2
    }

    /// <summary>
    /// O retrato do instante que o Brain entrega ao Perfil.
    ///
    /// É um objeto de DADOS, não uma decisão: ele carrega os sinais e o regime
    /// já classificado, mais o motivo em texto. O motivo existe porque a única
    /// forma de auditar uma decisão de energia depois é ler POR QUE ela foi
    /// tomada — e um regime sem motivo registrado é indistinguível de um chute.
    ///
    /// Todos os campos são <c>init</c> e o tipo é selado: o contexto é um
    /// instantâneo imutável, e ninguém pode "ajustar" o regime depois de
    /// Entscheidão sem quebrar a rastreabilidade do log.
    /// </summary>
    public sealed class EnergyContext
    {
        /// <summary>O regime classificado. <c>Unknown</c> quando não há telemetria.</summary>
        public EnergyRegime Regime { get; init; } = EnergyRegime.Unknown;

        /// <summary>Por que este regime. Vai para o log junto com a decisão.</summary>
        public string Reason { get; init; } = "sem contexto: vale o piso do Windows";

        /// <summary>O tier já medido. Entra no contexto para a decisão de escalada.</summary>
        public PowerCapabilityTier Tier { get; init; } = PowerCapabilityTier.Unknown;

        /// <summary>Flag de estrangulamento relatada pela telemetria, se houver.</summary>
        public bool Throttling { get; init; }

        /// <summary>Temperatura da CPU em Celsius. Negativo significa "não lida".</summary>
        public double CpuTemperatureC { get; init; } = -1.0;

        /// <summary>Se a máquina está na bateria agora.</summary>
        public bool OnBattery { get; init; }

        /// <summary>
        /// Se existe ALGUM sinal de telemetria neste contexto.
        ///
        /// Distingue "a máquina está bem" de "não olhamos". Sem isso, uma falha
        /// de leitura seria indistinguível de uma máquina folgada — e o sistema
        /// trataria silêncio de sensor como autorização.
        /// </summary>
        public bool HasTelemetry { get; init; }

        /// <summary>Instante da coleta, para o log.</summary>
        public DateTimeOffset SampledAt { get; init; } = DateTimeOffset.Now;

        /// <summary>
        /// Se a escalada abaixo do piso do Windows é admissível, avaliada contra
        /// o tier que o CHAMADOR está usando.
        ///
        /// Este é o portão canônico, e ele recebe o tier por parâmetro de
        /// propósito.
        ///
        /// [FIX:TIER-DUPLICADO] O tier chegava por dois caminhos ao mesmo tempo:
        /// como argumento de <c>Resolve</c> e dentro do contexto. O portão usava
        /// o do contexto — e o self-test não pegou, porque nos testes os dois
        /// vinham iguais. Um teste com um contexto classificado como
        /// <c>SustainedPerformer</c> e resolvido contra o tier <c>Balanced</c>
        /// escalava para EPP 25, que é justamente a máquina que a escalada não
        /// pode tocar.
        ///
        /// A causa não é o teste: é ter dois donos para o mesmo dado. Dois donos
        /// discordam na primeira vez em que alguém monta o contexto num lugar e
        /// resolve a linha em outro — e nada no código denuncia isso, porque
        /// ambos os valores são válidos individualmente.
        ///
        /// A correção é dar a autoridade ao ARGUMENTO: o tier que vale é o mesmo
        /// que será carimbado na linha e é o mesmo que escolheu o plano. O tier
        /// do contexto vira informativo, e passa a existir só para o log.
        ///
        /// Exige as quatro condições ao mesmo tempo, e nenhuma é negociável
        /// isoladamente:
        ///
        ///   1. regime <c>Sustaining</c> — a máquina está com folga AGORA;
        ///   2. tier <c>SustainedPerformer</c> — o tipo de equipamento já provou
        ///      ter orçamento (sem bateria, CPU não mobile de baixo consumo);
        ///   3. na tomada — bateria tem orçamento térmico próprio e nunca
        ///      sustenta um pico por policy;
        ///   4. telemetria presente — sem sensor não há como saber que a
        ///      máquina está segurando.
        ///
        /// Qualquer uma ausente devolve <c>false</c>. Não existe caminho em que
        /// a ausência de informação resulte em permissão.
        /// </summary>
        public bool CanEscalateFor(PowerCapabilityTier tier) =>
            Regime == EnergyRegime.Sustaining &&
            tier == PowerCapabilityTier.SustainedPerformer &&
            !OnBattery &&
            HasTelemetry;

        /// <summary>
        /// O portão avaliado contra o tier que o próprio contexto carrega.
        ///
        /// Serve para o log e para diagnóstico. NÃO use para decidir: a decisão
        /// tem que usar <see cref="CanEscalateFor"/>, pelo motivo documentado em
        /// <c>TIER-DUPLICADO</c> acima.
        /// </summary>
        public bool CanEscalate => CanEscalateFor(Tier);

        /// <summary>Se a máquina está no limite e a escalada está barrada por isso.</summary>
        public bool IsConstrained => Regime == EnergyRegime.Constrained;

        public string Describe()
        {
            string telemetria = !HasTelemetry
                ? "AUSENTE"
                : $"throttle={(Throttling ? "SIM" : "nao")} cpu={CpuTemperatureC:F0}C";

            string tier = Tier.ToString();
            string regime = Regime.ToString();

            return
                $"regime={regime} (escalada={(CanEscalate ? "PERMITIDA" : "bloqueada")}) | tier={tier} | " +
                $"tomada={(OnBattery ? "BATERIA" : "tomada")} | {telemetria} | motivo='{Reason}'";
        }

        /// <summary>
        /// O contexto que significa "não olhei". É o padrão explícito, para que
        /// nenhum caminho de código chegue ao Perfil sem dizer que não sabe.
        /// </summary>
        public static EnergyContext NoTelemetry(PowerCapabilityTier tier) => new()
        {
            Regime = EnergyRegime.Unknown,
            Tier = tier,
            HasTelemetry = false,
            Reason = "sem telemetria disponivel: vale o piso do Windows"
        };
    }

    /// <summary>
    /// [REGIME] O CLASSIFICADOR — FUNÇÃO PURA, SEM I/O E SEM ESCRITA.
    ///
    /// POR QUE ELE NÃO LÊ NADA
    /// ========================
    /// Esta classe é o único lugar que decide o regime, e ela é deliberadamente
    /// burra: recebe primitivos e devolve um veredito. Não abre WMI, não chama
    /// LibreHardwareMonitor, não lê registro, não toca em energia.
    ///
    /// A razão é prática e é a mesma que motivou remover o benchmark de
    /// sustentado: quando a classificação depende de I/O, ela falha de um jeito
    /// que não pode ser distinguido de uma classificação real. O log do projeto
    /// mostra o caso — a medição com carga travava o app, e o tier ficava
    /// <c>Unknown</c> sem que ninguém pudesse dizer se era "não deu" ou "deu
    /// Conservative". Se a função é pura, o regime é SEMPRE determinístico: o
    /// mesmo conjunto de sinais produz sempre o mesmo veredito, e um veredito
    /// errado aponta para o chamador, nunca para a lógica.
    ///
    /// POR QUE O TETO É 95 °C E NÃO UM NÚMERO CHUTADO POR PERFIL
    /// ==========================================================
    /// O projeto já tem um lugar que chuta temperatura por contexto —
    /// `AdaptiveStateMachine` usa 80 °C, `AdvancedThermalMonitorService` usa 94 °C
    /// na CPU, e `ThermalThresholds` tem outros números. Nenhum deles tem
    /// fundamento físico, e é por isso que discordam entre si.
    ///
    /// Aqui a temperatura tem um papel estritamente diferente e mais modesto:
    /// ela só pode **confirmar um estrangulamento**, nunca provocá-lo. O
    /// sinal primário é a flag de throttling, que Intel e AMD expõem e que é
    /// verificado pelo próprio hardware. A temperatura entra apenas como rede de
    /// segurança física: acima de 95 °C — a Tjmax típicas de processadores
    /// modernos, tanto Intel quanto AMD — alguma coisa está errado em qualquer
    /// máquina, e o número não é uma preferência por perfil.
    ///
    /// Abaixo de 95 °C este classificador não move NADA. É por isso que ele não
    /// consegue causar o defeito que os números por perfil causavam.
    /// </summary>
    public static class EnergyRegimeClassifier
    {
        /// <summary>
        /// Teto físico. Acima disto, qualquer processador moderno está fora de
        /// operação normal, e a única resposta correta é parar de insistir.
        /// </summary>
        public const double HardThermalCeilingC = 95.0;

        /// <summary>
        /// Classifica o regime a partir de sinais já coletados.
        ///
        /// A ORDEM IMPORTA e é deliberada: a flag de throttling vence a
        /// temperatura. Uma máquina pode estar a 99 °C e sem flag — e nesse caso
        /// o limite físico ainda manda, porque 99 °C não é normal em lugar
        /// nenhum. Mas uma máquina a 60 °C COM flag de throttling élimited por
        /// POTÊNCIA, não por temperatura, e tratá-la como "fria" seria
        /// exatamente o erro que a projetamos para não cometer.
        /// </summary>
        /// <param name="throttling">Flag de estrangulamento da telemetria. <c>false</c> se não houver telemetria.</param>
        /// <param name="cpuTemperatureC">Temperatura da CPU. Negativo ou zero = não lida.</param>
        /// <param name="tier">Tier já medido por <see cref="HardwareCapabilityProbe"/>.</param>
        /// <param name="onBattery">Se está na bateria.</param>
        public static EnergyContext Classify(
            bool throttling,
            double cpuTemperatureC,
            PowerCapabilityTier tier,
            bool onBattery)
        {
            bool temperaturaLida = cpuTemperatureC > 0.0 && !double.IsNaN(cpuTemperatureC);

            // ---- Sem telemetria: Unknown. Ponto. ----
            if (!throttling && !temperaturaLida)
            {
                return new EnergyContext
                {
                    Regime = EnergyRegime.Unknown,
                    Tier = tier,
                    Throttling = false,
                    CpuTemperatureC = cpuTemperatureC,
                    OnBattery = onBattery,
                    HasTelemetry = false,
                    Reason = "nem throttling nem temperatura disponiveis: vale o piso do Windows"
                };
            }

            // ---- Caso 1: estrangulamento ativo. ----
            if (throttling)
            {
                string causa = temperaturaLida
                    ? $"estrangulamento ativo a {cpuTemperatureC:F0}C"
                    : "estrangulamento ativo (temperatura nao lida)";

                return new EnergyContext
                {
                    Regime = EnergyRegime.Constrained,
                    Tier = tier,
                    Throttling = true,
                    CpuTemperatureC = cpuTemperatureC,
                    OnBattery = onBattery,
                    HasTelemetry = true,
                    Reason =
                        $"{causa}: a maquina esta no limite AGORA. " +
                        "Não se insiste: insistir sobre máquina estrangulada é o que produz o colapso de clock. " +
                        "O plano ja carrega a correcao (estacionamento sempre liberado); a alavanca que falta e de firmware (PL1/PL2/PPT), fora do alcance do plano."
                };
            }

            // ---- Caso 2: temperatura fora de qualquer condicao aceitavel. ----
            if (temperaturaLida && cpuTemperatureC >= HardThermalCeilingC)
            {
                return new EnergyContext
                {
                    Regime = EnergyRegime.Constrained,
                    Tier = tier,
                    Throttling = false,
                    CpuTemperatureC = cpuTemperatureC,
                    OnBattery = onBattery,
                    HasTelemetry = true,
                    Reason =
                        $"temperatura {cpuTemperatureC:F0}C acima do teto fisico de {HardThermalCeilingC:F0}C: " +
                        "estado fora de operacao normal em qualquer processador, nao e preferencia por perfil"
                };
            }

            // ---- Caso 3: com folga. ----
            return new EnergyContext
            {
                Regime = EnergyRegime.Sustaining,
                Tier = tier,
                Throttling = false,
                CpuTemperatureC = cpuTemperatureC,
                OnBattery = onBattery,
                HasTelemetry = true,
                Reason = temperaturaLida
                    ? $"sem estrangulamento e {cpuTemperatureC:F0}C dentro do teto: a maquina esta sustentando"
                    : "sem estrangulamento (temperatura nao lida): a maquina esta sustentando"
            };
        }

        /// <summary>
        /// Constrói o contexto a partir do snapshot do Brain, quando existe.
        ///
        /// O snapshot é o único lugar do projeto que já reúne temperatura,
        /// estrangulamento e estado da bateria no MESMO instante. Reutilizá-lo é
        /// o que impede a existência de duas leituras que discordam entre si —
        /// que foi sempre a origem dos valores contraditórios no log.
        ///
        /// Devolve <c>Unknown</c> quando não há snapshot, porque a ausência de
        /// snapshot não é evidência de folga.
        /// </summary>
        public static EnergyContext FromSnapshot(
            AntiStutterSnapshot? snapshot,
            PowerCapabilityTier tier)
        {
            if (snapshot == null)
            {
                return EnergyContext.NoTelemetry(tier);
            }

            return Classify(
                throttling: snapshot.CpuThermalThrottling,
                cpuTemperatureC: snapshot.CpuTemperatureC,
                tier: tier,
                onBattery: snapshot.IsOnBattery);
        }
    }
}
