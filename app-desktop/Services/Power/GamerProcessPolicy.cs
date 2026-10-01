using System;
using System.Collections.Generic;
using System.Threading;
using VoltrisOptimizer.Core.Brain.V2.AntiStutter;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Power
{
    /// <summary>
    /// PRIORIDADE DE CPU DE UM PROCESSO. Redefinida aqui, menor que a do
    /// <c>GamerModels</c>, de propósito.
    ///
    /// [FIX:PRIORIDADE-UNICA] POR QUE NÃO REUSAR O ENUM EXISTENTE
    /// ==========================================================
    /// `GamerModels.ProcessPriorityLevel` tem seis valores e o último é
    /// <c>RealTime</c>. Esse valor é usado de verdade no projeto — há código
    /// colocando processo em <c>ProcessPriorityClass.RealTime</c> — e ele é a
    /// causa clássica de travamento e engasgo em jogo: um processo que rouba a
    /// CPU de tudo o mais deixa o sistema inteiro, e o Windows, sem fatia, para
    /// de responder.
    ///
    /// A matriz de energia do Perfil já dizia, no seu próprio comentário:
    /// <c>"Prioridade de CPU do processo do jogo (nunca RealTime)"</c>. A regra
    /// estava escrita e o enum a ignorava.
    ///
    /// A correção não é uma linha de comentário: é a REMOÇÃO da opção. Enquanto
    /// <c>RealTime</c> existir no enum, qualquer um dos/services pode pedi-lo e
    /// o pedido é válido. Um enum sem o valor torna o erro impossível de
    /// compilar — e é a única forma de uma regra de segurança ser realmente
    /// obrigatória.
    ///
    /// `BelowNormal` também não entra. Não há cenário em que rebaixar o jogo
    /// seja decisão do Perfil: quem rebaixa processo é o gerenciador de
    /// recursos, para libertar CPU, e essa função é de outro dono.
    /// </summary>
    public enum GameProcessPriority
    {
        Normal = 0,
        AboveNormal = 1,
        High = 2
    }

    /// <summary>
    /// O que o Perfil Decide fazer com o processo do jogo.
    ///
    /// É um resultado de decisão, não uma configuração: cada campo tem um
    /// motivo registrado, e o motivo é o que permite auditar depois por que o
    /// jogo estava naquele estado. Sem ele, um processo em <c>High</c> não
    /// distingue "o Perfil pediu" de "algum serviço resolveu empurrar".
    /// </summary>
    public sealed class GamerProcessPlan
    {
        public GameProcessPriority Priority { get; init; } = GameProcessPriority.Normal;

        /// <summary>
        /// Se o jogo deve ficar fora do balanceador de CPU do Windows.
        ///
        /// Só é verdade quando a máquina está com folga comprovada. Numa máquina
        /// estrangulada o efeito é o CONTRÁRIO do desejado: tirar o jogo do
        /// balanceador impede o sistema de ceder CPU a ele justamente quando
        /// todos estão disputando.
        /// </summary>
        public bool ExcludeFromCpuBalancer { get; init; }

        /// <summary>
        /// Se vale carim energia por processo no jogo (EPP individual).
        ///
        /// Depende da MESMA porta da escalada de energia, e por um motivo
        /// concreto: EPP por processo é o ajuste de granularidade mais fino que
        /// existe, e ajuste fino em máquina sem folga é o primeiro a produzir
        /// oscilação. Os dois sistemas discordarem aqui significaria o Perfil
        /// subindo a energia do plano e o processo ao mesmo tempo, por lados
        /// diferentes e sem que nenhum dos dois soubesse do outro.
        /// </summary>
        public bool AllowPerProcessEnergy { get; init; }

        /// <summary>
        /// Se os núcleos do jogo devem ser fixados.
        ///
        /// Sempre <c>false</c>, e não é parâmetro: é uma CONSTANTE, pelo mesmo
        /// motivo do estacionamento de núcleo ser constante. A Intel desaconselha
        /// afinidade dura em CPU híbrida porque um thread preso pode ficar
        /// disputando núcleo com um processo de alta prioridade e passar fome.
        /// A REGRA 7 do self-test já proibia isso dentro da tabela; aqui a
        /// vedação passa a ser do lado de quem executa.
        /// </summary>
        public bool PinCores => false;

        public string Reason { get; init; } = "";

        public override string ToString() =>
            $"prioridade={Priority} foraDoBalanceador={ExcludeFromCpuBalancer} " +
            $"energiaPorProcesso={AllowPerProcessEnergy} fixaNucleos={PinCores} | {Reason}";
    }

    /// <summary>
    /// [FIX:PRIORIDADE-UNICA] A PARTE INTELIGENTE: o que fazer com o jogo.
    ///
    /// A entrada é o mesmo conjunto de sinais que já decide a energia —
    /// perfil, categoria do jogo, tier da máquina e o regime do instante. A
    /// diferença é que aqui eles produzem comportamento de PROCESSO, e não só
    /// números de plano.
    ///
    /// POR QUE O REGIME DE ENERGIA APARECE AQUI
    /// =======================================
    /// Porque é a mesma física. Uma máquina estrangulada não sofre por estar com
    /// o plano errado: sofre por não ter orçamento. E a resposta certa a um
    /// processo que está competindo por CPU num orçamento estourado NÃO é
    /// promover esse processo. É devolver ao sistema a liberdade de
    /// arbitrá-lo.
    ///
    /// É o inverso do que otimizador de jogo costuma fazer, e é por isso que
    /// importa. A maioria sobe prioridade quando o jogo abre e não desce nunca.
    /// Aqui a prioridade é uma FUNÇÃO do estado: sobe quando há folga, cede
    /// quando não há.
    ///
    /// A segunda consequência é que as duas decisões ficam coerentes. O regime
    /// já é o mesmo que a energia usa, então é impossível o Perfil promover o
    /// processo e recuar a energia — ou o contrário.
    /// </summary>
    public static class GamerProcessPolicy
    {
        /// <summary>
        /// Resolve o plano de processo do jogo.
        /// </summary>
        /// <param name="declaredPriority">
        /// A prioridade que a <see cref="ProfilePowerMatrix"/> declara para este
        /// perfil. Vem como texto porque é assim que está na tabela; a
        /// conversão abaixo é a única ponte entre os dois.
        /// </param>
        /// <param name="tier">Tier medido da máquina.</param>
        /// <param name="context">Regime do instante, ou <c>null</c> se não houver telemetria.</param>
        /// <param name="isGamerProfile">Se o perfil ativo é da família gamer.</param>
        public static GamerProcessPlan Resolve(
            string declaredPriority,
            PowerCapabilityTier tier,
            EnergyContext? context,
            bool isGamerProfile)
        {
            var reasons = new List<string>();
            GameProcessPriority priority = Parse(declaredPriority, out string? saneado);

            if (saneado != null)
            {
                reasons.Add(saneado);
            }

            bool onBattery = context?.OnBattery ?? false;
            bool constrained = context?.IsConstrained ?? false;
            bool sustaining = context?.Regime == EnergyRegime.Sustaining;
            bool telemetria = context?.HasTelemetry ?? false;

            // ---- 2. MÁQUINA ESTRANGULADA: CEDE, NÃO INSISTE ----
            //
            // Esta é a regra que faz o sistema valer a pena. Um jogo que abre
            // ganha prioridade; um jogo que começa a estrangular o processador
            // PERDE prioridade. Sem ela, o perfil só tem um comportamento para
            // a sessão inteira, e o pior momento é justamente o em que ele
            // continua empurrando.
            if (constrained)
            {
                if (priority == GameProcessPriority.High)
                {
                    priority = GameProcessPriority.AboveNormal;
                    reasons.Add("regime Constrained: prioridade do jogo reduzida de High para AboveNormal");
                }
                else if (priority == GameProcessPriority.AboveNormal)
                {
                    priority = GameProcessPriority.Normal;
                    reasons.Add("regime Constrained: prioridade do jogo reduzida de AboveNormal para Normal");
                }
            }

            // ---- 3. BATERIA NUNCA É MAIS AGRESSIVA QUE A TOMADA ----
            //
            // Mesma regra da REGRA 4 da energia, pelo mesmo motivo: a bateria é
            // o caso em que errar custa autonomia, e é o caso em que o
            // processador precisa de folga para durar.
            if (onBattery && priority != GameProcessPriority.Normal)
            {
                priority = GameProcessPriority.Normal;
                reasons.Add("na bateria: prioridade limitada a Normal (a tomada pode ser mais agressiva, a bateria nao)");
            }

            // ---- 4. FORA DO BALANCEADOR SÓ COM FOLGA PROVADA ----
            //
            // Exige: perfil gamer, regime Sustaining, telemetria presente e
            // tier capaz. Nenhuma condição isolada basta.
            bool balancer = isGamerProfile && sustaining && telemetria && !onBattery &&
                            tier == PowerCapabilityTier.SustainedPerformer;

            if (isGamerProfile && !balancer)
            {
                reasons.Add(
                    "fora do balanceador: NAO aplicado. " +
                    (constrained
                        ? "a maquina esta estrangulada e precisa do balanceador para arbitrar CPU."
                        : onBattery
                            ? "na bateria nao se tira o jogo do balanceador."
                            : !telemetria
                                ? "sem telemetria nao ha como afirmar folga."
                                : "tier sem orcamento comprovado."));
            }

            // ---- 5. ENERGIA POR PROCESSO USA A MESMA PORTA DA ESCALADA ----
            //
            // Aqui está o ponto que amarra os dois sistemas. Se a escalada de
            // energia do plano está desligada por falta de medição, a energia
            // por processo também está — porque é a mesma medição que falta.
            // Os dois lados concordam por construção, não por coincidência.
            bool perProcess = balancer && !constrained;

            return new GamerProcessPlan
            {
                Priority = priority,
                ExcludeFromCpuBalancer = balancer,
                AllowPerProcessEnergy = perProcess,
                Reason = reasons.Count == 0
                    ? "perfil nao pede ajuste de processo"
                    : string.Join(" | ", reasons)
            };
        }

        /// <summary>
        /// Converte o texto da matriz no enum, saneando o que não existe.
        ///
        /// [FIX:PRIORIDADE-UNICA] `RealTime` É SANEADO AQUI, E NÃO EXISTE NO
        /// ENUM. É este o ponto em que o pedido vira recusa: a matriz ainda
        /// guarda o texto, e alguém pode escrevê-lo lá, mas ele chega aqui como
        /// <c>AboveNormal</c> e com o motivo registrado. Um valor que não pode
        /// nem ser representado não pode ser aplicado.
        /// </summary>
        private static GameProcessPriority Parse(string? text, out string? saneado)
        {
            saneado = null;

            if (string.IsNullOrWhiteSpace(text)) return GameProcessPriority.Normal;

            string t = text.Trim();

            if (t.Equals("RealTime", StringComparison.OrdinalIgnoreCase))
            {
                saneado =
                    $"prioridade declarada '{t}' RECUSADA e convertida em AboveNormal: " +
                    "processo em tempo real rouba a CPU de todo o sistema e e a causa classica de travamento em jogo";
                return GameProcessPriority.AboveNormal;
            }

            if (t.Equals("High", StringComparison.OrdinalIgnoreCase))
                return GameProcessPriority.High;

            if (t.Equals("AboveNormal", StringComparison.OrdinalIgnoreCase))
                return GameProcessPriority.AboveNormal;

            if (t.Equals("Normal", StringComparison.OrdinalIgnoreCase))
                return GameProcessPriority.Normal;

            if (t.Equals("BelowNormal", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("Idle", StringComparison.OrdinalIgnoreCase))
            {
                saneado = $"prioridade declarada '{t}' convertida em Normal: rebaixar processo e funcao do gerenciador de recursos, nao do Perfil";
                return GameProcessPriority.Normal;
            }

            saneado = $"prioridade declarada '{t}' nao e reconhecida; usando Normal";
            return GameProcessPriority.Normal;
        }
    }
}
