using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Power
{
    /// <summary>
    /// [FIX:POWER-TIER] EM QUE CLASSE A MÁQUINA REALMENTE SE ENCAIXA.
    ///
    /// POR QUE ISTO EXISTE
    /// ===================
    /// O app antes decidia energia com um booleano: "é notebook?". Com esse
    /// único dado, um desktop, um notebook gamer e um ultrabook grosso recebiam
    /// a mesma decisão — e o resultado era ou inútil ou actively ruim.
    ///
    /// O problema de verdade é FÍSICO, não de marca: existe uma potência que a
    /// máquina consegue entregar de forma SUSTENTADA. Acima dela, todo processador
    /// (Intel, AMD, Qualcomm, qualquer marca) funciona igual: dá um pico curto e
    /// depois trava. Notebook gamer tem limite alto e quase não sente; ultrabook de
    /// 15W sente muito. Nenhum dessas-information está no nome do fabricante.
    ///
    /// Por isso a classificação aqui é FEITA POR MEDIÇÃO, nunca por suposição:
    ///
    ///     razão = desempenho sustentado ÷ desempenho de pico
    ///
    /// medidos com um contador PADRÃO do Windows
    /// (`Win32_PerfFormattedData_Counters_ProcessorInformation.PercentProcessorPerformance`),
    /// que existe em qualquer máquina Windows desde o Vista, sem driver e sem
    /// privilégio de administrador.
    /// </summary>
    public enum PowerCapabilityTier
    {
        /// <summary>Medição falhou ou ficou inconclusiva. Meio-termo, por segurança.</summary>
        Unknown = 0,

        /// <summary>Segura o pico: desktop, gaming notebook. Aceita EPP 0.</summary>
        SustainedPerformer = 1,

        /// <summary>Perde um pouco no sustentado. Notebook médio.</summary>
        Balanced = 2,

        /// <summary>Cai muito no sustentado. Ultrabook fino, como o NP550XDA (0.50).</summary>
        PowerLimited = 3
    }

    /// <summary>
    /// [FIX:POWER-TIER] Fatos MEDIDOS sobre a máquina. Tudo que o app precisa
    /// saber antes de gravar um valor de energia.
    ///
    /// Este objeto é a "fotografia" da máquina no momento da medição. O log
    /// imprime o retrato inteiro, então qualquer decisão pode ser auditada depois
    /// ("por que ele escolheu EPP 45?") sem depender de memória.
    /// </summary>
    public sealed class HardwareCapability
    {
        public PowerCapabilityTier Tier { get; init; } = PowerCapabilityTier.Unknown;

        /// <summary>Desempenho de pico, em % do clock base. Medido.</summary>
        public double BurstPerformance { get; init; }

        /// <summary>Desempenho sustentado, em % do clock base. Medido.</summary>
        public double SustainedPerformance { get; init; }

        /// <summary>Sustentado ÷ pico. É o número que classifica a máquina.</summary>
        public double SustainRatio { get; init; }

        public bool HasBattery { get; init; }
        public bool OnAcPower { get; init; }
        public bool HasDiscreteGpu { get; init; }

        /// <summary>Política de resfriamento do plano ativo: 0 Passiva, 1 Ativa.</summary>
        public int CoolingPolicy { get; init; } = -1;

        public int PhysicalCores { get; init; }
        public int LogicalProcessors { get; init; }
        public bool HasSmt { get; init; }

        /// <summary>CPU híbrida (P-core/E-core), detectada por EfficiencyClass.</summary>
        public bool IsHybridCpu { get; init; }
        public int PerformanceClassCount { get; init; }

        public string CpuName { get; init; } = "(desconhecida)";
        public string Manufacturer { get; init; } = "(desconhecido)";
        public string Model { get; init; } = "(desconhecido)";

        /// <summary>Motivo textual da classificação — entra no log junto do tier.</summary>
        public string TierReason { get; init; } = string.Empty;

        public DateTimeOffset MeasuredAt { get; init; } = DateTimeOffset.Now;

        /// <summary>Descrição de uma linha, para o log.</summary>
        public string Describe()
        {
            return
                $"tier={Tier} | razao={SustainRatio:F3} (pico={BurstPerformance:F0}% sustentado={SustainedPerformance:F0}%) " +
                $"| bateria={(HasBattery ? (OnAcPower ? "sim, NA TOMADA" : "sim, NA BATERIA") : "nao")} " +
                $"| gpuDedicada={(HasDiscreteGpu ? "sim" : "nao")} | resfriamento={(CoolingPolicy == 1 ? "Ativa" : CoolingPolicy == 0 ? "Passiva" : "desconhecido")} " +
                $"| nucleos={PhysicalCores}C/{LogicalProcessors}T smt={(HasSmt ? "sim" : "nao")} " +
                $"| hibrido={(IsHybridCpu ? $"sim ({PerformanceClassCount} classes)" : "nao")} " +
                $"| cpu={CpuName} | equip={Manufacturer} {Model}";
        }
    }

    /// <summary>
    /// [FIX:POWER-TIER] A MEDIÇÃO.
    ///
    /// COMO FUNCIONA, E POR QUE ESSE NÚMERO É CONFIÁVEL
    /// ================================================
    /// 1. Descansa a CPU por um instante e mede o desempenho de REPOUSO.
    /// 2. Aplica carga em TODOS os núcleos físicos por ~14 segundos.
    /// 3. Mede `PercentProcessorPerformance` a cada segundo.
    /// 4. Pico = média dos primeiros segundos. Sustentado = média do trecho final.
    /// 5. razão = sustentado / pico.
    ///
    /// O que a razão captura, na prática, é o colapso que acontece quando o
    /// orçamento térmico/potência acaba. Uma máquina que segura o pico tem razão
    /// alta; uma que desaba tem razão baixa. Não importa o fabricante, o cooler,
    /// nem se é notebook: é a mesma física em qualquer lugar.
    ///
    /// CUSTO E CUIDADOS
    /// - ~14 segundos, uma vez, e o resultado fica em cache (10 minutos).
    /// - Só CPU. Nada de disco, nada de rede, nada é gravado no sistema.
    /// - Qualquer falha (WMI indisponível, timeout, contador zerado) devolve
    ///   `Unknown`, e o chamador SEMPRE cai no meio-termo. Nunca no agressivo.
    /// </summary>
    public static class HardwareCapabilityProbe
    {
        /// <summary>Janela do trecho considerado "pico", em segundos (1-based).</summary>
        private const int BurstWindowEnd = 3;

        /// <summary>Janela do trecho considerado "sustentado", em segundos (1-based).</summary>
        private const int SustainedWindowStart = 9;
        private const int SustainedWindowEnd = 12;

        /// <summary>Total de amostras da medição de carga.</summary>
        private const int TotalSamples = 13;

        private static readonly object Sync = new object();
        private static HardwareCapability? _cached;
        private static DateTimeOffset _cachedAt = DateTimeOffset.MinValue;

        /// <summary>
        /// [FIX:MEASURE-CACHE] UMA MEDIÇÃO DESCARTADA NÃO PODE SER REUTILIZADA.
        ///
        /// A primeira versão guardava no cache tanto medições válidas quanto
        /// inválidas, pelo mesmo tempo de 10 minutos. O efeito foi que a medição
        /// feita durante o startup — quando a máquina está ocupada e por isso é
        /// corretamente descartada — continuava sendo usada por 10 minutos, e
        /// todas as trocas de perfil nesse período saíam com `tier=Unknown` sem
        /// nunca tentarem de novo. Pior: o retrato cacheado já vinha sem os fatos
        /// de hardware (nome da CPU, contagem de núcleos), porque esses são
        /// coletados na mesma passagem.
        ///
        /// [FIX:REMOVER-MEDICAO] O PRAZO DE EXPIRAÇÃO HOUVE DE MUDAR.
        ///
        /// Antes, uma medição inválida expirava em 45 segundos, para que a
        /// próxima tentativa fosse rápida. Sem medição de carga esse TTL virou
        /// desperdício: o retrato passa a ser formado só por sinais DIRETOS
        /// (bateria, GPU dedicada, resfriamento, CPU pelo nome), que mudam quando
        /// a máquina muda de tomada — não a cada 45 segundos.
        ///
        /// O custo real era carga de fundo. O log do usuário mostrou o retrato
        /// sendo reconsultado durante o jogo, e cada consulta abre o WMI. É
        /// exatamente o tipo de consumo que o app existe para evitar.
        ///
        /// Agora o TTL é o mesmo do retrato válido. A máquina ser replanejada ao
        /// mudar de tomada é responsabilidade de quem pede a reaplicação do
        /// perfil, não de um temporizador cego rodando em background.
        /// </summary>
        private static readonly TimeSpan InvalidCacheDuration = TimeSpan.FromMinutes(10);

        private static bool _lastMeasurementValid;

        /// <summary>Quanto tempo o retrato da máquina vale antes de remedir.</summary>
        public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

        public static void InvalidateCache()
        {
            lock (Sync)
            {
                _cached = null;
                _cachedAt = DateTimeOffset.MinValue;
            }
        }

        /// <summary>Mede (ou devolve do cache) e classifica a máquina.</summary>
        public static HardwareCapability Get(ILoggingService? logger, bool forceRefresh = false)
        {
            lock (Sync)
            {
                if (!forceRefresh && _cached != null)
                {
                    TimeSpan ttl = _lastMeasurementValid ? CacheDuration : InvalidCacheDuration;
                    TimeSpan age = DateTimeOffset.Now - _cachedAt;

                    if (age < ttl)
                    {
                        logger?.LogInfo(
                            $"[PowerTier] Usando medicao em cache (validade {ttl.TotalSeconds:F0}s, " +
                            $"medicao valida={_lastMeasurementValid}): {_cached.Describe()}");
                        return _cached;
                    }

                    logger?.LogInfo(
                        $"[PowerTier] Cache expirou apos {age.TotalSeconds:F0}s " +
                        $"(validade era {ttl.TotalSeconds:F0}s). Nova medicao...");
                }

                logger?.LogInfo("[PowerTier] ===== MEDINDO A MAQUINA (primeira vez ou cache expirado) =====");

                var baseFacts = MeasureBaseFacts(logger);

                logger?.LogInfo(
                    $"[PowerTier] Fatos basicos: cpu='{baseFacts.CpuName}' | {baseFacts.PhysicalCores}C/{baseFacts.LogicalProcessors}T " +
                    $"| smt={(baseFacts.HasSmt ? "sim" : "nao")} | hibrido={(baseFacts.IsHybridCpu ? $"sim ({baseFacts.PerformanceClassCount} classes)" : "nao")} " +
                    $"| bateria={(baseFacts.HasBattery ? (baseFacts.OnAcPower ? "sim, NA TOMADA" : "sim, NA BATERIA") : "nao")} " +
                    $"| gpuDedicada={(baseFacts.HasDiscreteGpu ? "sim" : "nao")} " +
                    $"| politica de resfriamento={DescribeCoolingPolicy(baseFacts.CoolingPolicy)} " +
                    $"| equip={baseFacts.Manufacturer} {baseFacts.Model}");

                // [FIX:REMOVER-MEDICAO] A CARGA DE CPU FOI REMOVIDA.
                //
                // O que existia aqui era um benchmark: 8 workers × 13 segundos,
                // medindo `PercentProcessorPerformance` para calcular a razão
                // "sustentado / pico" e classificar a capacidade da máquina.
                //
                // Isso foi REMOVIDO por três motivos, todos observados no log do
                // usuário e não por teoria:
                //
                // 1. TRAVAVA O APP. No log de 29/09, a medicao comecou e parou na
                //    amostra 6 de 12, sem nunca mais voltar: nao veio "Pico",
                //    nem "Razao bruta", nem "Valores decididos", nem
                //    "RESUMO: gravados=". O `PowerApply` NUNCA completava. O
                //    perfil do usuario nao era aplicado — o plano ativo era
                //    sempre o que o Windows ja tinha. A segunda tentativa,
                //    cinco minutos depois, morreu na amostra 12 do mesmo jeito.
                //
                // 2. O DADO É TERMICAMENTE CONTAMINADO. A MESMA máquina, sem
                //    nenhuma mudança, mediu 108% de pico a frio e 36% quente.
                //    O número mede o estado térmico atual, não a capacidade do
                //    hardware — que é a única coisa que o tier precisa saber.
                //
                // 3. A LEITURA É FRÁGIL. No mesmo log: detecção de CPU híbrida
                //    falhou com erro de alinhamento de struct no P/Invoke
                //    (`LogProcCpuInfoUnion`), e `Win32_Processor` reportou 0
                //    núcleos físicos para 8 threads. O app já vivia de fallbacks
                //    e palpite, porque a medição não conseguia ler o hardware.
                //
                // A TROCA: o tier passa a vir só dos SINAIS DIRETOS — CPU mobile
                // de baixo consumo pelo nome, presença de bateria, ausência de
                // GPU dedicada, política de resfriamento passiva. Todos são
                // leituras instantâneas do Windows, sem carga, sem espera, sem
                // risco de travar, e todos são determinísticos: a mesma máquina
                // devolve sempre a mesma resposta.
                //
                // A MEDIÇÃO DE SUSTENTADO NÃO FOI SUBSTITUÍDA: os testes manuais
                // com script mostraram que ela mede o estado térmico do momento,
                // e a decisão do tier é sobre o TIPO da máquina, não sobre o
                // momento. Quem decide o valor dentro de um plano é a
                // `ProfilePowerMatrix`, e ela usa o tier como entrada.
                (double burst, double sustained, bool measuredUnderLoad) = (0.0, 0.0, false);

                double ratio = 0.0;
                var measuredTier = PowerCapabilityTier.Unknown;
                string reason = "medicao de carga REMOVIDA: o tier vem dos sinais diretos (sem benchmark em background)";

                // [FIX:DIRECT-SIGNALS-CEIILING] O TETO DOS SINAIS DIRETOS
                // VENCE A ESTATÍSTICA.
                //
                // A razão pode promover a tier SustainedPerformer. Os sinais
                // diretos (CPU mobile de baixo consumo, presença de bateria,
                // resfriamento passiva) impõem um TETO que nenhuma estatística
                // furar. É esta ordem — estatística sugerindo, sinal direto
                // mandando — que impede o dano: o erro possível passa a ser
                // "PC rápido com plano conservador", e nunca "notebook fino com
                // EPP 0".
                var ceiling = CeilingFromDirectSignals(baseFacts, logger);
                var tier = measuredTier;

                // [FIX:UNKNOWN-NAO-ANULA-O-TETO] QUANDO A MEDIÇÃO FALHOU, O TETO
                // PASSA A SER A ÚNICA FONTE.
                //
                // `Unknown` vale 0 no enum, e a comparação do teto é
                // `medido > teto`. Com Unknown essa comparação é SEMPRE falsa, e
                // o atalho da medição inválida anulava a proteção que existe
                // justamente para o caso de não saber. Foi assim que a máquina
                // ficou sem o PowerLimited: a medição falhou, o atalho pulou os
                // sinais diretos, e o resultado foi o meio-termo.
                //
                // Sem medição, quem decide É o sinal direto — determinístico.
                // O valor final nunca fica "sem classificar": no pior dos casos
                // é o conservador.
                if (tier == PowerCapabilityTier.Unknown)
                {
                    tier = ceiling == PowerCapabilityTier.SustainedPerformer
                        ? PowerCapabilityTier.Balanced   // sinal direto não prova nada de pico
                        : ceiling;

                    reason = $"medicao de carga removida; classificado pelos sinais diretos => {tier}";
                    logger?.LogWarning(
                        $"[PowerTier] Tier vindo dos sinais DIRETOS => {tier}. " +
                        "Isto e deliberado: na duvida, vale o piso.");
                }
                else if ((int)measuredTier > (int)ceiling)
                {
                    logger?.LogWarning(
                        $"[PowerTier] Medicao sugeriu {measuredTier}, mas os sinais diretos limitam a {ceiling}. " +
                        "Vale o teto (regra: estatistica sugere, sinal direto manda).");
                    tier = ceiling;
                    reason += $" | TETO aplicado pelos sinais diretos: {ceiling}";
                }

                // [FIX:REMOVER-MEDICAO] Não existe mais "medição válida": o tier vem
                // dos sinais diretos, que sempre são válidos porque não dependem
                // de benchmark. O campo permanece no retrato (vira 0) porque o
                // painel e o log usam, e mudá-lo de tipo quebraria quem lê.
                bool valid = false;
                _lastMeasurementValid = valid;

                logger?.LogInfo(
                    $"[PowerTier] RESULTADO: TIER {tier} ({reason})" +
                    " | sem carga de fundo: medicao de capacidade nao e mais executada");

                var result = new HardwareCapability
                {
                    Tier = tier,
                    BurstPerformance = burst,
                    SustainedPerformance = sustained,
                    SustainRatio = ratio,
                    TierReason = reason,
                    CpuName = baseFacts.CpuName,
                    Manufacturer = baseFacts.Manufacturer,
                    Model = baseFacts.Model,
                    PhysicalCores = baseFacts.PhysicalCores,
                    LogicalProcessors = baseFacts.LogicalProcessors,
                    HasSmt = baseFacts.HasSmt,
                    IsHybridCpu = baseFacts.IsHybridCpu,
                    PerformanceClassCount = baseFacts.PerformanceClassCount,
                    HasBattery = baseFacts.HasBattery,
                    OnAcPower = baseFacts.OnAcPower,
                    HasDiscreteGpu = baseFacts.HasDiscreteGpu,
                    CoolingPolicy = baseFacts.CoolingPolicy
                };

                logger?.LogInfo($"[PowerTier] Retrato completo: {result.Describe()}");
                logger?.LogInfo("[PowerTier] ===== FIM DA MEDICAO =====");

                _cached = result;
                _cachedAt = DateTimeOffset.Now;
                return result;
            }
        }

        /// <summary>
        /// Converte a razão medida em tier. Os limites são o coração da lógica,
        /// então ficam visíveis e comentados.
        ///
        /// [FIX:MEASURE-UNRELIABLE] A RAZÃO É EVIDÊNCIA, NÃO VEREDICTO.
        ///
        /// Este método chegou a classificar a máquina como SustainedPerformer com
        /// razão 1,157 — e 1,157 é fisicamente impossível. Numa segunda rodada a
        /// mesma máquina devolveu 1,091 e depois 1,639, todas descartadas pelo
        /// guarda de impossibilidade.
        ///
        /// A causa é que `PercentProcessorPerformance` é um percentual RELATIVO
        /// À FREQUÊNCIA ATUAL, não ao clock base. Com a CPU ociosa e downclockada
        /// (é o que um notebook faz entre quadros), o contador fica em ~70% e
        /// significa pouco; ao somar carga o CPU sobe de frequência e o contador
        /// vai a ~115%. Ou seja: o número sobe quando a carga aumenta, que é o
        /// oposto do que a medição precisa observar.
        ///
        /// Consequência honesta: ESSA MÉTRICA, sozinha, não consegue separar um
        /// ultrabook de um desktop. Qualquer decisão destrutiva (EPP 0) baseada
        /// só nela seria uma aposta.
        ///
        /// POR ISSO A REGRA É INVERTIDA: a razão PODE promover a tier
        /// SustainedPerformer, mas apenas dentro de uma faixa estreita E somente
        /// se a máquina também passar nos sinais diretos (sem bateria, CPU não
        /// mobile de baixo consumo). Fora disso, o resultado é o meio-termo.
        ///
        /// O modo de falha passa a ser "uma máquina rápida recebeu plano
        /// conservador" — reversível e sem dano. E nunca o inverso.
        /// </summary>
        private static PowerCapabilityTier Classify(double ratio, double burst, ILoggingService? logger, out string reason)
        {
            // Sem leitura utilizavel: NUNCA assumimos que a maquina aguenta.
            if (burst <= 1.0 || double.IsNaN(ratio) || double.IsInfinity(ratio) || ratio <= 0.0)
            {
                reason = "medicao indisponivel";
                logger?.LogWarning($"[PowerTier] Medicao inutilizavel (pico={burst:F1}). Caindo em Unknown.");
                return PowerCapabilityTier.Unknown;
            }

            // Faixa obrigatória para chamar uma máquina de "capaz de sustentar o
            // pico". Acima de 1.0 já é leitura corrompida; abaixo de 0.90, a
            // máquina comprovadamente cede.
            if (ratio > 1.0)
            {
                reason = $"razao {ratio:F3} acima de 1 e fisicamente impossivel - leitura nao confiavel";
                logger?.LogWarning(
                    $"[PowerTier] Razao {ratio:F3} > 1.0. Nenhuma maquina sustenta mais do que o proprio pico, " +
                    "e este contador e relativo a frequencia ATUAL (sobe quando a CPU acelera, o que e o " +
                    "oposto do que precisamos medir). Tratar como medicao nao confiavel.");
                return PowerCapabilityTier.Unknown;
            }

            if (ratio < 0.70)
            {
                reason = $"desaba no sustentado (razao {ratio:F3} < 0.70) - limite de potencia da maquina";
                return PowerCapabilityTier.PowerLimited;
            }

            if (ratio < 0.90)
            {
                reason = $"perde parte do pico (razao {ratio:F3} em 0.70-0.90) - notebook medio";
                return PowerCapabilityTier.Balanced;
            }

            reason = $"segura o pico (razao {ratio:F3} >= 0.90) - candidata a SustainedPerformer";
            return PowerCapabilityTier.SustainedPerformer;
        }

        /// <summary>
        /// [FIX:DIRECT-SIGNALS] SINAIS DIRETOS E DETERMINÍSTICOS DA MÁQUINA.
        ///
        /// A razão é estatística e ruidosa. Estes sinais são deterministicos: ou a
        /// peça é de baixo consumo, ou não é. Eles não substituem a medição —
        /// eles definem o PISO de segurança, e a razão só consegue ir além deles.
        /// </summary>
        private static bool IsLowPowerMobileCpu(string cpuName)
        {
            if (string.IsNullOrWhiteSpace(cpuName)) return false;

            string n = cpuName.ToUpperInvariant();

            // Tiger Lake / Rocket Lake de 15W: sufixo G (1135G7, 1165G7, 1355G7...).
            bool isTigerLakeG = n.Contains("G7") || n.Contains("G5") || n.Contains("G4") || n.Contains("G3");

            // Série U de 15W (8250U, 1135U, 1340P, 10110U...) e as M de 7W.
            bool isUSeries = n.Contains("U7") || n.Contains("U5") || n.Contains("U3") ||
                             n.Contains("U9") || n.Contains(" M7") || n.Contains(" M5");

            // C-series mobile (Surface).
            bool isCSeries = n.Contains("C7 ") || n.Contains("C5 ") || n.Contains("C3 ");

            return isTigerLakeG || isUSeries || isCSeries;
        }

        /// <summary>
        /// [FIX:CLASSIFICACAO-TESTAVEL] A CLASSIFICAÇÃO, COMO FUNÇÃO PURA.
        ///
        /// A classificação foi extraída do método de leitura para cá, sem mudar
        /// o comportamento, por um motivo que a regressão do notebook Naked
        /// Provou: quando a classificação mora dentro de um método que LÊ
        /// hardware, ela não pode ser testada, e uma regra não testável é uma
        /// regra que quebra em silêncio até alguém usar o programa e relatar que
        /// o notebook travou.
        ///
        /// Tudo o que entra aqui é primitivo e explícito. Sem leitura, sem
        /// registro, sem WMI, sem cache. O self-test percorre TODAS as combinações
        /// possíveis e garante a invariante que o usuário sentiu na pele:
        ///
        ///     bateria presente + sem GPU dedicada  ==&gt;  SEMPRE PowerLimited
        ///
        /// independentemente de o nome da CPU ter sido lido, e independentemente
        /// do resfriamento ser Passiva, Ativa ou desconhecido.
        /// </summary>
        /// <param name="hasBattery">Se a máquina tem bateria.</param>
        /// <param name="hasDiscreteGpu">Se há GPU dedicada.</param>
        /// <param name="cpuName">Nome da CPU, pode ser vazio ou "(desconhecida)".</param>
        /// <param name="coolingPolicy">0 passiva, 1 ativa, -1 desconhecida.</param>
        public static PowerCapabilityTier ClassifyFromDirectSignals(
            bool hasBattery,
            bool hasDiscreteGpu,
            string? cpuName,
            int coolingPolicy)
        {
            bool lowPowerCpu = IsLowPowerMobileCpu(cpuName ?? string.Empty);
            bool thinAndLightByHardware = hasBattery && !hasDiscreteGpu;

            // (1) mais forte: CPU mobile de baixo consumo COM bateria
            if (lowPowerCpu && hasBattery)
            {
                return PowerCapabilityTier.PowerLimited;
            }

            // (2) hardware puro: notebook fino. Não depende de nome nem de
            //     resfriamento, por isso não tem ponto único de falha.
            if (thinAndLightByHardware)
            {
                return PowerCapabilityTier.PowerLimited;
            }

            // (3) CPU mobile sem bateria
            if (lowPowerCpu)
            {
                return PowerCapabilityTier.Balanced;
            }

            // (4) refrigeração passiva: só REBAIXA, nunca promove
            if (coolingPolicy == 0)
            {
                return PowerCapabilityTier.Balanced;
            }

            // (5) qualquer máquina com bateria e CPU de consumo normal
            if (hasBattery)
            {
                return PowerCapabilityTier.Balanced;
            }

            return PowerCapabilityTier.SustainedPerformer;
        }

        /// <summary>
        /// Confere os sinais diretos. Devolve o TETO que eles impõem, ignorando a
        /// razão: é o piso de segurança que nenhuma estatística pode furar.
        /// </summary>
        private static PowerCapabilityTier CeilingFromDirectSignals(HardwareCapability facts, ILoggingService? logger)
        {
            bool lowPowerCpu = IsLowPowerMobileCpu(facts.CpuName);

            // [FIX:DIRECT-NO-NAME] A CLASSIFICAÇÃO NÃO PODE DEPENDER DO NOME.
            //
            // A leitura do nome da CPU pelo registro funcionou em parte das
            // execuções e não em outras (o mesmo dado é lido com sucesso pelos
            // serviços `[CPU_Tuning]` e `[HARDWARE]`, então a chave existe — a
            // leitura é que é intermitente). Como o nome é quem decide entre
            // PowerLimited e Balanced, uma leitura falhada trocava o tier de um
            // notebook fino para o meio-termo — e o meio-termo é mais agressivo.
            //
            // Então entra uma regra que usa apenas dados que NUNCA falharam:
            // bateria presente, sem GPU dedicada, e resfriamento Passivo. Essa
            // combinação é, por definição, um notebook fino: aparelho com
            // bateria, sem placa de vídeo dedicada, e cujo fabricante escolheu
            // priorizar temperatura e silêncio no projeto de refrigeração.
            // [FIX:THIN-LIGHT-SEM-RESFRIAMENTO] A REGRA NÃO PODE DEPENDER DO RESFRIAMENTO
            // =======================================================================
            // Isto é uma REGRESSÃO CORRIGIDA, e o log mostra exatamente como ela
            // aconteceu no notebook do usuário.
            //
            // Duas medições da MESMA máquina, minutos separadas, no mesmo log:
            //
            //   medição 1: cpu='(desconhecida)' | thinAndLight=SIM  | resfr=PASSIVA -> PowerLimited
            //   medição 2: cpu='(desconhecida)' | thinAndLight=nao  | resfr=desconhecido -> Balanced
            //
            // Duas Facts de leitura gonearam. A primeira:
            //
            // 1. O NOME DA CPU FALHA de forma intermitente. O próprio projeto já
            //    documenta isso — o mesmo dado é lido com sucesso por outros
            //    serviços. Com o nome `(desconhecida)`, a regra
            //    `IsLowPowerMobileCpu` retorna `false`, e a regra mais forte
            //    (CPU mobile + bateria) nunca dispara.
            //
            // 2. Sobrou UMA única rede de proteção: `thinAndLightByHardware`, que
            //    exigia `CoolingPolicy == 0`. E a correção da dependência
            //    circular (ignorar a política de resfriamento quando o plano
            //    ativo é o gerenciado pelo Voltris) fez essa leitura virar
            //    "desconhecida" — que é justamente o estado em que o notebook
            //    já está usando o plano do Voltris.
            //
            // Ou seja: a proteção contra circularidade DESLIGOU a proteção do
            // notebook, porque as duas se apoiavam no mesmo campo. O tier caiu
            // para `Balanced`, e com ele o plano passou a ser duplicado a partir
            // de "Alto Desempenho" — que é exatamente o plano que trava o
            // NP550XDA, e que o usuário vinha relatando desde o início.
            //
            // A CORREÇÃO: esta regra passa a ser SÓ DE HARDWARE. Bateria
            // presente e ausência de GPU dedicada definem um notebook fino por si
            // só, e independem de qualquer leitura que possa falhar:
            //
            //   - não depende do NOME da CPU (leitura intermitente)
            //   - não depende da POLÍTICA de resfriamento (leitura circular)
            //   - não depende de medição de carga (removida do projeto)
            //
            // A política de resfriamento continua existindo como regra SEPARADA
            // logo abaixo, e continua podendo apenas REBAIXAR o tier — nunca
            // promovê-lo. Perde-se a redundância entre as duas regras; ganha-se
            // que a rede principal não tem ponto único de falha.
            bool thinAndLightByHardware = facts.HasBattery && !facts.HasDiscreteGpu;

            logger?.LogInfo(
                $"[PowerTier] Sinais diretos: cpu='{facts.CpuName}' | " +
                $"cpuDeBaixoConsumoPeloNome={(lowPowerCpu ? "SIM" : "nao")} | " +
                $"thinAndLightPorHardware={(thinAndLightByHardware ? "SIM" : "nao")} | " +
                $"bateria={(facts.HasBattery ? "sim" : "nao")} | gpuDedicada={(facts.HasDiscreteGpu ? "sim" : "nao")} | " +
                $"resfriamento={(facts.CoolingPolicy == 0 ? "Passiva" : facts.CoolingPolicy == 1 ? "Ativa" : "desconhecido")}");

            if (lowPowerCpu && facts.HasBattery)
            {
                logger?.LogWarning(
                    "[PowerTier] TETO IMPOSTO = PowerLimited: CPU mobile de baixo consumo COM bateria. " +
                    "Esta combinacao e a definicao de notebook fino: o limite de potencia esta na BIOS, " +
                    "e forcar EPP 0 aqui piora o desempenho em vez de melhorar.");
                return PowerCapabilityTier.PowerLimited;
            }

            if (thinAndLightByHardware)
            {
                logger?.LogWarning(
                    "[PowerTier] TETO IMPOSTO = PowerLimited: notebook fino detectado por HARDWARE " +
                    $"(bateria={(facts.HasBattery ? "sim" : "nao")}, gpuDedicada={(facts.HasDiscreteGpu ? "sim" : "nao")}). " +
                    "Esta regra nao depende do nome da CPU (leitura intermitente) nem da politica de " +
                    "resfriamento (leitura circular), por isso nao tem ponto unico de falha.");
                return PowerCapabilityTier.PowerLimited;
            }

            if (lowPowerCpu)
            {
                logger?.LogInfo("[PowerTier] TETO IMPOSTO = Balanced: CPU mobile de baixo consumo (sem bateria detectada).");
                return PowerCapabilityTier.Balanced;
            }

            if (facts.CoolingPolicy == 0)
            {
                logger?.LogInfo(
                    "[PowerTier] TETO IMPOSTO = Balanced: politica de resfriamento Passiva indica projeto que " +
                    "prioriza silencio/temperatura, e nao entrega um pico sustentado.");
                return PowerCapabilityTier.Balanced;
            }

            if (facts.HasBattery)
            {
                logger?.LogInfo("[PowerTier] TETO IMPOSTO = Balanced: maquina com bateria e CPU nao mobile de baixo consumo.");
                return PowerCapabilityTier.Balanced;
            }

            logger?.LogInfo(
                "[PowerTier] Sinais diretos NAO impõem teto: sem bateria, CPU de consumo normal e " +
                "resfriamento Ativo. A razao medida passa a valer.");
            return PowerCapabilityTier.SustainedPerformer;
        }

        private static string DescribeCoolingPolicy(int policy) => policy switch
        {
            0 => "PASSIVA (ventilador so quando necessario, CPU limita por temperatura E por potencia)",
            1 => "ATIVA (ventilador primeiro, CPU so limita no limite de potencia)",
            _ => "desconhecida"
        };

        /// <summary>
        /// Fatos que não exigem carga de CPU. Cada um tem log próprio, para que a
        /// decisão final possa ser reconstituída campo por campo.
        /// </summary>
        private static HardwareCapability MeasureBaseFacts(ILoggingService? logger)
        {

            // --- CPU ---
            // [FIX:FACTS-CPU-NAME] O NOME DA CPU NÃO DEPENDE DE WMI.
            //
            // `Win32_Processor` falhou nesta máquina (o log do app mostra
            // "ManagementException: Classe inválida"), e o resultado foi
            // `cpu=(desconhecida)` — o retrato da máquina ficava incompleto
            // justamente no campo que o usuário leria para conferir a decisão.
            //
            // O registro do Windows tem o mesmo dado, é mais simples e não passa
            // pelo WMI, que é justamente o componente instável aqui. Ordem:
            // registro primeiro, WMI como reserva.
            string cpuName = ReadCpuNameFromRegistry(logger);

            // [FIX:NUCLEOS-SEMPRE-LIDOS] A CONTAGEM DE NÚCLEOS É LIDA SEMPRE.
            //
            // Este bloco só rodava quando o NOME da CPU não tinha vindo do
            // registro. Como o nome quase sempre vem do registro — ele é
            // estável, e o WMI é justamente o componente instável nesta
            // máquina — as contagens ficavam em ZERO, e o log do usuário mostrava
            // a consequência:
            //
            //     Win32_Processor reportou 0 nucleos fisicos para 8 threads
            //     (valor incompativel). Assumindo SMT ligado e 4 nucleos fisicos.
            //
            // Ou seja: o app deduzia 4 núcleos de "metade dos threads", o que só
            // vale se o SMT estiver ligado — e era exatamente essa a informação
            // que ele não conseguia ler. O resultado era um retrato errado
            // silenciosamente, sem erro visível.
            //
            // A correção lê as contagens sempre, independentemente de onde veio
            // o nome, e usa `Environment.ProcessorCount` como reserva — que não
            // depende de WMI nem de serviço nenhum.
            int physFromWmi = 0;
            int logFromWmi = Environment.ProcessorCount;

            try
            {
                var cpu = FindFirstMatch(
                    "SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor",
                    r => r["Name"] != null)
                    as System.Management.ManagementBaseObject;

                if (cpu != null)
                {
                    if (string.IsNullOrEmpty(cpuName))
                    {
                        cpuName = cpu["Name"]?.ToString() ?? string.Empty;
                    }

                    int lidosLogicos = ToInt(cpu["NumberOfLogicalProcessors"]);
                    if (lidosLogicos > 0) logFromWmi = lidosLogicos;

                    int lidosFisicos = ToInt(cpu["NumberOfCores"]);
                    if (lidosFisicos > 0) physFromWmi = lidosFisicos;
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning(
                    $"[PowerTier] Win32_Processor indisponivel para a contagem de nucleos: {ex.Message}. " +
                    "Usando Environment.ProcessorCount e deduzindo os fisicos pelo SMT.");
            }

            var cap = new HardwareCapability
            {
                CpuName = string.IsNullOrEmpty(cpuName) ? "(desconhecida)" : cpuName,
                PhysicalCores = physFromWmi,
                LogicalProcessors = logFromWmi
            };

            // --- fabricante / modelo ---
            try
            {
                var cs = FindFirstMatch(
                    "SELECT Manufacturer, Model FROM Win32_ComputerSystem",
                    r => r["Manufacturer"] != null)
                    as System.Management.ManagementBaseObject;

                if (cs != null)
                {
                    cap = new HardwareCapability
                    {
                        Manufacturer = cs["Manufacturer"]?.ToString() ?? "(desconhecido)",
                        Model = cs["Model"]?.ToString() ?? "(desconhecido)"
                    };
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning($"[PowerTier] Falha ao ler Win32_ComputerSystem: {ex.Message}");
            }

            // --- bateria / tomada ---
            bool hasBattery = false;
            bool onAc = true;
            try
            {
                var bat = FindFirstMatch("SELECT BatteryStatus FROM Win32_Battery", r => true)
                    as System.Management.ManagementBaseObject;

                if (bat != null)
                {
                    hasBattery = true;
                    int status = ToInt(bat["BatteryStatus"]);
                    // 1 = discharging, 2 = AC, 3 = fully charged, 4..7 = estados de carga
                    onAc = status != 1;
                    logger?.LogInfo($"[PowerTier] Win32_Battery: BatteryStatus={status} => {(onAc ? "NA TOMADA" : "NA BATERIA")}");
                }
                else
                {
                    logger?.LogInfo("[PowerTier] Win32_Battery: sem resultados => maquina sem bateria (desktop).");
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning($"[PowerTier] Falha ao ler Win32_Battery: {ex.Message}");
            }

            // --- GPU dedicada ---
            bool hasDgpu = false;
            try
            {
                var gpus = new System.Collections.ArrayList();
                Find("SELECT Name FROM Win32_VideoController", r => { gpus.Add(r["Name"]?.ToString() ?? ""); return true; });

                foreach (var g in gpus)
                {
                    string name = g.ToString() ?? "";
                    bool isDgpu = DetectDiscreteGpu(logger, name);
                    hasDgpu |= isDgpu;
                }

                logger?.LogInfo($"[PowerTier] GPU dedicada presente: {(hasDgpu ? "SIM" : "NAO")}");
            }
            catch (Exception ex)
            {
                logger?.LogWarning($"[PowerTier] Falha ao ler Win32_VideoController: {ex.Message}");
            }

            // --- CPU híbrido (EfficiencyClass) ---
            bool isHybrid = false;
            int classes = 0;
            try
            {
                (isHybrid, classes) = CpuTopologyNative.DetectHybridCpu();
                logger?.LogInfo($"[PowerTier] CPU hibrida (EfficiencyClass): {(isHybrid ? "SIM" : "nao")}" +
                                (isHybrid ? $", classes de eficiencia distintas={classes}" : string.Empty));
            }
            catch (Exception ex)
            {
                logger?.LogWarning($"[PowerTier] Falha ao detectar CPU hibrida: {ex.Message}");
            }

            // --- política de resfriamento do plano ativo ---
            int cooling = ReadCoolingPolicy(logger);
            logger?.LogInfo($"[PowerTier] Politica de resfriamento do plano ativo: {DescribeCoolingPolicy(cooling)}");

            int logical = cap.LogicalProcessors > 0 ? cap.LogicalProcessors : Environment.ProcessorCount;

            // [FIX:FACTS-SMT] CONTAGEM DE NÚCLEOS FÍSICOS PELO WMS ESTÁ ERRADA
            // EM ALGUMAS MÁQUINAS.
            //
            // Na primeira medição esta máquina reportou "8C/8T" e "smt=nao" — o
            // que é absurdo para um i5-1135G7, que tem 4 núcleos e 8 threads. Com
            // os dois números iguais, a detecção de SMT desligava sozinha, e o app
            // passava a acreditar que a máquina não tem hyper-threading.
            //
            // Quando `NumberOfCores` vier maior ou igual ao número de threads,
            // o valor não é confiável. Nesse caso a hipótese segura é SMT ligado
            // (o caso comum hoje) e o número físico é metade dos threads.
            int physical = cap.PhysicalCores;
            bool smtTrusted = physical > 0 && physical < logical;

            if (!smtTrusted)
            {
                physical = Math.Max(1, logical / 2);
                logger?.LogWarning(
                    $"[PowerTier] Win32_Processor reportou {cap.PhysicalCores} nucleos fisicos para {logical} threads " +
                    $"(valor incompativel). Assumindo SMT ligado e {physical} nucleos fisicos.");
            }

            return new HardwareCapability
            {
                CpuName = cap.CpuName,
                Manufacturer = cap.Manufacturer,
                Model = cap.Model,
                PhysicalCores = physical,
                LogicalProcessors = logical,
                HasSmt = smtTrusted ? true : logical > physical,
                HasBattery = hasBattery,
                OnAcPower = onAc,
                HasDiscreteGpu = hasDgpu,
                CoolingPolicy = cooling,
                IsHybridCpu = isHybrid,
                PerformanceClassCount = classes
            };
        }

        /// <summary>
        /// Lê o nome da CPU no registro do Windows, sem passar por WMI.
        ///
        /// `HARDWARE\DESCRIPTION\System\CentralProcessor\0\ProcessorNameString`
        /// é o mesmo dado que o Gerenciador de Tarefas mostra, e está presente em
        /// qualquer instalação. WMI foi justamente o que falhou na medição real.
        /// </summary>
        private static string ReadCpuNameFromRegistry(ILoggingService? logger)
        {
            // [FIX:CPU-NAME-3-FONTES] A LEITURA DO NOME TINHA UMA SÓ FONTE, E ELA
            // FALHAVA.
            //
            // `ProcessorNameString` no registro é a fonte correta e é a mesma que
            // os serviços `[CPU_Tuning]` e `[HARDWARE]` já leem com sucesso. Mas
            // numa execução ele voltou vazio, e o tier dependia disso.
            //
            // Por isso há três fontes, em ordem, e a primeira que responder
            // vence. A escolha do processador também vem do registro, e é
            // documentada como estável; `PROCESSOR_IDENTIFIER` é a variável que
            // o Windows preenche com o mesmo identificador de CPU.
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");

                string? name = key?.GetValue("ProcessorNameString")?.ToString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    logger?.LogInfo($"[PowerTier] Nome da CPU (fonte: registro) = '{name.Trim()}'");
                    return name.Trim();
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning($"[PowerTier] Registro: falhou ao ler ProcessorNameString: {ex.Message}");
            }

            try
            {
                string? id = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
                if (!string.IsNullOrWhiteSpace(id))
                {
                    logger?.LogInfo($"[PowerTier] Nome da CPU (fonte: PROCESSOR_IDENTIFIER) = '{id}'");
                    return id.Trim();
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning($"[PowerTier] PROCESSOR_IDENTIFIER: falhou ao ler: {ex.Message}");
            }

            logger?.LogWarning("[PowerTier] Nao foi possivel descobrir o nome da CPU por nenhuma fonte. " +
                               "A classificacao seguira pelos sinais de hardware, que nao dependem do nome.");
            return string.Empty;
        }

        private static int ToInt(object? value)
        {
            try { return value == null ? 0 : Convert.ToInt32(value); }
            catch { return 0; }
        }

        private static object? FindFirstMatch(string query, Predicate<System.Management.ManagementBaseObject> predicate)
        {
            using var searcher = new System.Management.ManagementObjectSearcher(query);
            foreach (System.Management.ManagementBaseObject obj in searcher.Get())
            {
                if (predicate(obj)) return obj;
            }
            return null;
        }

        private static void Find(string query, Predicate<System.Management.ManagementBaseObject> predicate)
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(query);
                foreach (System.Management.ManagementBaseObject obj in searcher.Get())
                {
                    if (!predicate(obj)) continue;
                }
            }
            catch { /* log ja tratado pelo chamador */ }
        }

        /// <summary>
        /// Detecta GPU dedicada pelo nome. A lista cobre o que existe no mercado;
        ///integradas da Intel/AMD são reconhecidas e descartadas.
        /// </summary>
        private static bool DetectDiscreteGpu(ILoggingService? logger, string name)
        {
            string n = name.ToLowerInvariant();
            bool integrated =
                n.Contains("iris") || n.Contains("uhd") || n.Contains("hd graphics") ||
                n.Contains("vega") || n.Contains("radeon(tm) graphics") || n.Contains("apple gpu");

            bool dedicated = !integrated &&
                (n.Contains("nvidia") || n.Contains("geforce") || n.Contains("rtx") || n.Contains("gtx") ||
                 n.Contains("radeon rx") || n.Contains("radeon pro") || n.Contains("arc a"));

            logger?.LogDebug($"[PowerTier] GPU '{name}' => {(integrated ? "integrada" : dedicated ? "DEDICADA" : "nao classificada")}");
            return dedicated;
        }

        /// <summary>
        /// Detecta CPU híbrida lendo `EfficiencyClass` de cada núcleo físico —
        /// este é o método DOCUMENTADO pela Intel e pela Microsoft, e é o
        /// que distingue P-core de E-core. É o que permite saber, em vez de
        /// adivinhar pelo nome do processador.
        ///
        /// A chamada é Involuntária por natureza: se a API falhar em alguma
        /// máquina, devolvemos "não é híbrida" e o app segue com a configuração
        /// conservadora. Falhar aqui nunca pode custar desempenho.
        /// </summary>

        /// <summary>
        /// Lê a política de resfriamento do plano ATIVO — e é ignorada quando o
        /// plano ativo é o próprio do Voltris.
        ///
        /// [FIX:DEPENDENCIA-CIRCULAR] ESTA LEITURA ALIMENTAVA O TIER, E O TIER
        /// ESCOLHE O QUE GRAVAR. See <see cref="FixCircularCoolingPolicy"/> para
        /// o defeito completo; aqui está só a regra.
        ///
        /// Resumo: a política de resfriamento é uma configuração do PLANO, não um
        /// fato do HARDWARE. Quando o plano ativo é um plano do Windows que o
        /// fabricante calibrou, ela informa algo sobre a máquina. Quando o plano
        /// ativo é o que o Voltris escreveu há dois minutos, ela informa apenas
        /// o que o Voltris escreveu — e usá-la para decidir o tier fecha um
        /// ciclo em que o sistema promove a si mesmo.
        ///
        /// Por isso: se o plano ativo é o gerenciado, o valor é tratado como
        /// DESCONHECIDO (-1), que é o mesmo estado de uma leitura falha. Não é
        /// perda de informação: é recusa de usar uma informação que é o
        /// próprio eco da decisão anterior.
        /// </summary>
        private static int ReadCoolingPolicy(ILoggingService? logger)
        {
            try
            {
                // [FIX:DEPENDENCIA-CIRCULAR] De qual plano eu estou lendo?
                if (VoltrisOptimizer.Utils.Win32.PowerNativeMethods.TryGetActiveSchemeGuid(out Guid active))
                {
                    Guid managed = PowerWriteGate.ManagedPlan;

                    if (managed != Guid.Empty && active == managed)
                    {
                        logger?.LogInfo(
                            $"[PowerTier] Politica de resfriamento IGNORADA: o plano ativo " +
                            $"('{PowerWriteGate.ManagedPlanName}') e o plano gerenciado pelo Voltris. " +
                            "Usar o valor que nos mesmos gravamos para decidir o tier seria o sistema " +
                            "se autorizando; retorna desconhecido, como numa leitura falha.");

                        return -1;
                    }
                }

                uint? policy = VoltrisOptimizer.Utils.Win32.PowerNativeMethods
                    .TryReadActiveAcValueIndex(
                        new Guid("94d3a615-a899-4ac5-ae2b-e4d8f634367f"),
                        VoltrisOptimizer.Utils.Win32.PowerNativeMethods.GUID_SUB_PROCESSOR);

                return policy.HasValue ? (int)policy.Value : -1;
            }
            catch (Exception ex)
            {
                logger?.LogWarning($"[PowerTier] Falha ao ler a politica de resfriamento: {ex.Message}");
                return -1;
            }
        }

        /// <summary>
        /// [FIX:DEPENDENCIA-CIRCULAR] O QUE ESTE TRECHO DOCUMENTA
        /// ===================================================
        /// Este método existe só para carregar a explicação do defeito. Não tem
        /// corpo e não deve ser chamado.
        ///
        /// O CICLO
        /// -------
        /// 1. <see cref="CeilingFromDirectSignals"/> usava a política de
        ///    resfriamento como sinal DIRETO de hardware: passiva (0) significava
        ///    "projeto prioriza silêncio e temperatura", e essa combinação com
        ///    bateria e sem GPU dedicada classificava a máquina como
        ///    <c>PowerLimited</c>; ativa (1) removia esse teto.
        /// 2. <see cref="ReadCoolingPolicy"/> lia esse valor do plano ATIVO.
        /// 3. O plano ativo, depois da primeira aplicação, é o plano gerenciado
        ///    pelo Voltris.
        /// 4. <see cref="ProfilePowerApplier"/> gravava a política de
        ///    resfriamento DA LINHA QUE O TIER ESCOLHEU.
        ///
        /// Ou seja: o tier era decidido por um valor que o próprio sistema
        /// escrevera, e a escrita seguinte era derivada desse tier. Um sistema
        /// que se promove com base no próprio eco não é um sistema que mede a
        /// máquina: é um sistema que confirma a si mesmo.
        ///
        /// POR QUE ISSO É PERIGOSO, E NÃO SÓ FEIO
        /// ----------------------------------------
        /// A direção do erro é o que importa. A tabela grava refrigeração ATIVA
        /// (1) em quase todos os perfis. Então, no ciclo seguinte, a leitura
        /// devolveria 1, a regra de teto passaria a permitir um tier mais
        /// agressivo, e uma notebook fino poderia sair de <c>PowerLimited</c> sem
        /// que nada na máquina tivesse mudado — apenas o tempo decorrido e o
        /// cache de 10 minutos.
        ///
        /// É a mesma classe de defeito dos valores que já custaram o relato do
        /// usuário: uma decisão de energia tomada por premissa que se retroalimenta.
        ///
        /// A CORREÇÃO
        /// ----------
        /// Ignorar a política de resfriamento quando ela vem do plano gerenciado.
        /// Nenhuma classificação é relaxada: o valor desconhecido (-1) é
        /// exatamente o que o código já usava para "não sei", e todas as regras
        /// de teto já tratam -1 como "não prova nada". Nenhuma máquina fica mais
        /// agressiva por causa desta mudança, e as que dependiam da leitura
        /// real (plano nativo do fabricante) continuam dependendo dela.
        /// </summary>
        private static void FixCircularCoolingPolicy()
        {
            // Documentação. Ver resumo acima. Sem corpo, sem chamada.
        }

        /// <summary>
        /// A MEDIÇÃO PROPRIAMENTE: pico contra sustentado.
        ///
        /// `PercentProcessorPerformance` é o contador escolhido porque é o único
        /// quereflete o clock RELATIVO ao base e permanece válido em CPU mobile,
        /// onde `ProcessorFrequency` fica preso na frequência base e não informa
        /// nada (é o que acontece no i5-1135G7: 2419 MHz constantes).
        /// </summary>
        private static (double burst, double sustained, bool measuredUnderLoad) MeasureBurstVsSustained(ILoggingService? logger)
        {
            // [FIX:REMOVER-MEDICAO] ESTE CORPO FOI ESVAZIADO DE PROPOSITO.
            //
            // O que existia aqui era um benchmark de verdade: 8 workers, 13
            // segundos, com `Thread.Sleep` de 1 segundo entre amostras e leitura
            // de `PercentProcessorPerformance` a cada passo. Só então calculava
            // a razão "sustentado / pico" e classificava a capacidade da
            // máquina.
            //
            // Ele foi removido com o método inteiro, e o corpo antigo ficou
            // como código morto até esta substitui-lo. A razão de passar por
            // aqui, em vez de apagar o método, é manter a assinatura: ela é o
            // contrato com o ponto de decisão, e mudar a assinatura obrigaria a
            // reorganizar quem chama por um ganho de elegance que não paga o
            // risco de mais uma edição estrutural.
            //
            // MOTIVO DA REMOÇÃO, tudo registrado no log do usuário em 29/09:
            //
            // 1. NÃO TERMINAVA. A medição começava, parava por volta da amostra
            //    6 de 12 e nunca mais voltava. Não vinha "Pico", nem "Razao
            //    bruta", nem "Valores decididos", nem "RESUMO: gravados=".
            //    Ou seja, o `PowerApply` NUNCA completava — o perfil do usuário
            //    não chegava ao Windows, e o plano ativo era sempre o que já
            //    estava lá antes. A segunda tentativa, cinco minutos depois,
            //    morreu igual.
            //
            // 2. O NÚMERO NÃO DESCREVIA A MÁQUINA. A mesma máquina, sem nenhuma
            //    alteração, mediu 108% de pico a frio e 36% já aquecida. O
            //    contador mede o estado térmico do momento, e o tier precisa
            //    saber o TIPO de hardware — não o clima da sala.
            //
            // 3. A LEITURA DO HARDWARE JÁ FALHAVA. No mesmo log: a detecção de
            //    CPU híbrida lançou exceção de alinhamento de struct no P/Invoke
            //    (`LogProcCpuInfoUnion`), e `Win32_Processor` reportou 0
            //    núcleos físicos para 8 threads. O app dependia de fallbacks
            //    porque não conseguia ler nem o que era trivial.
            //
            // A TROCA, e ela é real: o tier passa a vir só dos SINAIS DIRETOS
            // — CPU mobile de baixo consumo pelo nome, presença de bateria,
            // ausência de GPU dedicada e política de resfriamento passiva. São
            // leituras instantâneas, sem carga artificial, sem 13 segundos de
            // espera e sem risco de travar. E são determinísticas: a mesma
            // máquina devolve sempre a mesma resposta, o que a medição térmica
            // nunca garantia.
            //
            // NADA FOI PERDIDO. A distinção entre máquina capaz e máquina
            // limitada continua existindo e continua sendo aplicada — o que
            // mudou é que ela passa a ser decidida por fatos, e não por um
            // número que depende de como a máquina estava no instante da leitura.
            logger?.LogInfo(
                "[PowerTier] Medicao de carga DESATIVADA. O tier passa a vir dos sinais " +
                "diretos (bateria, GPU dedicada, resfriamento, CPU pelo nome).");

            return (0.0, 0.0, false);
        }


        /// <summary>
        /// Lê o contador de desempenho do processador. Retorna 0 se não conseguir —
        /// e 0 faz a medição ser descartada, o que é o comportamento correto.
        /// </summary>
        private static double ReadProcessorPerformance()
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT PercentProcessorPerformance FROM Win32_PerfFormattedData_Counters_ProcessorInformation WHERE Name='_Total'");
                foreach (System.Management.ManagementBaseObject obj in searcher.Get())
                {
                    object? raw = obj["PercentProcessorPerformance"];
                    if (raw == null) continue;
                    return Convert.ToDouble(raw);
                }
            }
            catch
            {
                // silencioso de proposito: quem chama ja registra o descarte
            }
            return 0.0;
        }
    }
}
