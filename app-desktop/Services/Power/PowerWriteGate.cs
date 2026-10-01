using System;
using System.Threading;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Utils.Win32;

namespace VoltrisOptimizer.Services.Power
{
    /// <summary>
    /// [FIX:POWER-GATE] O ÚNICO LUGAR QUE AUTORIZA GRAVAR ENERGIA.
    ///
    /// O QUE ESTE ARQUIVO CORRIGE
    /// ==========================
    /// A auditoria encontrou TRÊS sistemas gravando energia de forma independente,
    /// todos vivos ao mesmo tempo, todos inscritos no mesmo evento
    /// `SettingsService.ProfileChanged`:
    ///
    ///   1. `ProfilePowerApplier` (novo) — grava a tabela por tier no plano Voltris
    ///   2. `o servico legado` (antigo) — grava EPP 20/30/35/50/60/70/80
    ///      no esquema ATIVO, seja qual for
    ///   3. `Brain` / `Gamer Mode` — trocam o plano para planos NATIVOS do Windows
    ///      (Equilibrado, Alto Desempenho, Ultimate), o que tira o plano Voltris do ar
    ///      e torna inertes todos os valores gravados nele
    ///
    /// A consequência medida: o app gravava EPP 45 no plano "Voltris - Equilibrado"
    /// e, milissegundos depois, o `o servico legado` gravava EPP 20 no
    /// plano nativo — que era o que ficava ativo. O valor da tabela nunca valia.
    ///
    /// Por que a guarda que existia não servia: em
    /// `PowerNativeMethods.PowerWriteACValueIndex` havia um `if (!IsVoltrisPlan)`
    /// que só **logava** e seguia escrevendo. Um guarda que observa e obedece é
    /// um log.
    ///
    /// A REGRA DESTE PORTÃO
    /// ====================
    /// O portão não sabe qual é o "perfil certo" — isso é a tabela, e ela já
    /// existe. Ele sabe apenas uma coisa: **qual plano é o plano gerenciado**, e
    /// quem tem o direito de escrever nele.
    ///
    /// Regra:
    ///   - escrita de ENERGIA (EPP, min/max, boost, parking, resfriamento) só é
    ///     liberada no plano gerenciado, ou em modo de manutenção (quando o
    ///     próprio usuário edita em Opções de Energia);
    ///   - troca de plano que tire o plano gerenciado do ar é registrada como
    ///     usurpação, e o coordenador se re-asserta;
    ///   - qualquer serviço legado que tentar gravar registra o motivo do bloqueio.
    ///
    /// Assim o modo de falha passa a ser "um serviço antigo não conseguiu gravar" —
    /// que é um log — e nunca "a máquina ficou com o valor errado".
    /// </summary>
    public static class PowerWriteGate
    {
        private const string Tag = "[PowerGate]";

        /// <summary>Settings que definem o CARÁTER do perfil e, por isso, são protegidos.</summary>
        private static readonly Guid[] ProtectedSettings =
        {
            new("36687f9e-e3a5-4dbf-b1dc-15eb381c6863"), // EPP
            new("893dee8e-2bef-41e0-89c6-b55d0929964c"), // Processor minimo
            new("bc5038f7-23e0-4960-96da-33abaf5935ec"), // Processor maximo
            new("be337238-0d82-4146-a960-4f3749d470c7"), // Boost Mode
            new("0cc5b647-c1df-4637-891a-dec35c318583"), // Core parking
            new("94d3a615-a899-4ac5-ae2b-e4d8f634367f")  // Politica de resfriamento
        };

        private static Guid _managedPlan = Guid.Empty;
        private static string _managedPlanName = "(nenhum)";

        /// <summary>
        /// Quando ligado, o portão deixa passar tudo. Usado APENAS enquanto o
        /// usuário edita valores manualmente na tela de Energia, que é uma
        /// intenção explícita dele.
        /// </summary>
        public static bool MaintenanceMode { get; set; }

        /// <summary>Plano que o Voltris escolheu para esta máquina.</summary>
        public static Guid ManagedPlan
        {
            get { lock (Sync) return _managedPlan; }
        }

        public static string ManagedPlanName
        {
            get { lock (Sync) return _managedPlanName; }
        }

        public static bool HasManagedPlan => ManagedPlan != Guid.Empty;

        private static readonly object Sync = new object();

        /// <summary>
        /// Declara qual plano é o plano do Voltris. Chamado pelo
        /// <see cref="ProfilePowerApplier"/> logo após criar/ativar o plano.
        /// </summary>
        public static void DeclareManagedPlan(Guid planGuid, string planName, ILoggingService? logger)
        {
            bool changed;
            lock (Sync)
            {
                changed = _managedPlan != planGuid;
                _managedPlan = planGuid;
                _managedPlanName = planName;
            }

            logger?.LogInfo(
                $"{Tag} Plano gerenciado declarado: '{planName}' ({planGuid})" +
                (changed ? " (mudou)" : string.Empty) +
                ". Toda gravacao de energia fora deste plano passa a ser bloqueada.");
        }

        public static bool IsManagedPlan(Guid schemeGuid)
        {
            Guid managed = ManagedPlan;
            return managed != Guid.Empty && schemeGuid == managed;
        }

        private static bool IsProtected(Guid settingGuid)
        {
            foreach (var g in ProtectedSettings)
            {
                if (g == settingGuid) return true;
            }
            return false;
        }

        /// <summary>
        /// Decide se uma escrita de setting de energia pode seguir.
        /// É este método que é chamado pelos DOIS caminhos de escrita do app:
        /// a API nativa e o `powercfg.exe`.
        /// </summary>
        public static bool AllowSettingWrite(
            Guid schemeGuid, Guid settingGuid, string requester, out string reason)
        {
            if (MaintenanceMode)
            {
                reason = "modo de manutencao (usuario editando em Opcoes de Energia)";
                return true;
            }

            Guid managed = ManagedPlan;
            if (managed == Guid.Empty)
            {
                // Antes da primeira aplicação do perfil, nada é gerenciado e
                // nada precisa ser bloqueado. O Windows segue com o plano nativo.
                reason = "nenhum plano gerenciado ainda (antes da primeira aplicacao de perfil)";
                return true;
            }

            if (!IsProtected(settingGuid))
            {
                reason = "setting nao protegido (nao define o caracter do perfil)";
                return true;
            }

            // [FIX:POWER-GATE-BRAIN] ESTA ERA A BRECHA QUE DEIXAVA TUDO PASSAR.
            //
            // A versão anterior respondia "escrita no plano gerenciado" com
            // PERMISSÃO. Era a maior falha possível no desenho, porque os
            // serviços legacy não escrevem em "outro plano" — eles escrezem no
            // PLANO ATIVO, que é justamente o gerenciado. Ou seja: a condição
            // que autorizava a escrita era a mesma que a tornava destrutiva.
            //
            // Dois serviços faziam exatamente isso, e nenhum dos dois passava
            // por `powercfg.exe` nem por `PowerNativeMethods`:
            //
            //   o servico legado.cs:308  ->  CPMINCORES = 0  (estacionamento)
            //   o servico legado.cs:311  ->  min processor = 100
            //   o servico legado.cs:69      ->  CPMINCORES = 0
            //   o servico legado.cs:70      ->  min processor = 100
            //
            // CPU 4 núcleos com núcleo mínimo 100% e estacionamento desligado
            // nunca consegue baixar o clock: o orçamento térmico é gasto antes
            // da carga chegar. É o defeito medido (43% menos clock) e é também
            // o "os GHz diminuem" que o usuário vinha relatando.
            //
            // A regra agora: com um plano gerenciado existente, um setting
            // protegido só muda dentro de `BeginProfileApply()` (o próprio perfil)
            // ou no modo de manutenção (usuário mexendo de propósito).
            if (Volatile.Read(ref _profileApplyDepth) <= 0)
            {
                reason =
                    $"setting protegido do plano gerenciado '{_managedPlanName}' ({managed}) " +
                    "pertence ao Perfil Inteligente. Escrita externa bloqueada " +
                    $"(veio de '{requester}').";

                App.LoggingService?.LogWarning($"{Tag} BLOQUEADO ({requester}): {reason}");
                return false;
            }

            reason = "aplicacao do perfil em andamento (dono legitimo do plano gerenciado)";
            return true;
        }

        /// <summary>
        /// [FIX:POWER-GATE-BRAIN] QUEM DONA OS VALORES DO PLANO GERENCIADO
        ///
        /// Este é o furo que a auditoria NÃO fechou, e ele só apareceu em campo.
        ///
        /// `AllowSettingWrite` barra escrita de energia que sai do plano
        /// gerenciado — mas LIBERA escrita DENTRO dele (`schemeGuid == managed`).
        /// Essa liberação estava certa para o `ProfilePowerApplier`, que é quem
        /// escreve a tabela, e errada para todo mundo mais. E o Brain escreve
        /// justamente dentro do plano gerenciado, porque o plano ativo É o
        /// Voltris.
        ///
        /// O que o log da máquina do usuário provou, 7 segundos depois de o
        /// perfil ter gravado EPP 45 com o plano correto:
        ///
        ///     23:35:12 [PowerApply]   AC EPP ... CONFIRMADO = 45
        ///     23:35:19 [BRAIN-DECISION] Regra: exploit | epp = 0 max performance
        ///     23:35:19 [ARM-POWER]   EPP alterado de 50 -> 0 em 16ms
        ///
        /// Ou seja: o plano estava CERTO, e o Brain o destruiu em seguida,
        /// escrevendo por `o servico legado` — caminho que nenhum dos dois
        /// filtros anteriores alcança, porque ele não passa por `powercfg.exe`
        /// nem por `PowerNativeMethods`. EPP 0 neste ultrabook de 15W é
        /// exatamente o "os GHz caem para 1,3 GHz" que o usuário relutava.
        ///
        /// A regra agora é simples e absoluta: quando existe um plano gerenciado,
        /// os VALORES DELE pertencem ao perfil. Quem quiser mudá-lo precisa do
        /// perfil, e o perfil é uma coisa só. Brain, Gamer Mode e serviços
        /// legados continuam livres para pedir TROCA DE PLANO (que é registrado
        /// e re-assertado), mas não para reescrever o conteúdo do plano que o
        /// perfil escolheu.
        /// </summary>
        private static int _profileApplyDepth;

        /// <summary>
        /// Delimitador da aplicação legítima do perfil. Só dentro dele a escrita
        /// no plano gerenciado é permitida, porque só ela implementa a tabela
        /// <c>ProfilePowerMatrix</c>.
        /// </summary>
        public static IDisposable BeginProfileApply()
        {
            Interlocked.Increment(ref _profileApplyDepth);
            return new ProfileApplyScope();
        }

        private sealed class ProfileApplyScope : IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                Interlocked.Decrement(ref _profileApplyDepth);
            }
        }

        /// <summary>
        /// Autoriza a reescrita de um VALOR dentro do plano gerenciado.
        /// Verdadeiro apenas durante <see cref="BeginProfileApply"/> ou no modo de
        /// manutenção (quando o usuário mexe em Opções de Energia de propósito).
        /// </summary>
        public static bool AllowProfileOwnedWrite(string requester, out string reason)
        {
            if (MaintenanceMode)
            {
                reason = "modo de manutencao (usuario editando em Opcoes de Energia)";
                return true;
            }

            if (!HasManagedPlan)
            {
                reason = "nenhum plano gerenciado ainda (antes da primeira aplicacao de perfil)";
                return true;
            }

            if (Volatile.Read(ref _profileApplyDepth) > 0)
            {
                reason = "aplicacao do perfil em andamento (dono legitimo do plano)";
                return true;
            }

            reason =
                $"os valores do plano gerenciado '{_managedPlanName}' ({ManagedPlan}) pertencem " +
                "ao Perfil Inteligente. O Brain e o Gamer Mode podem pedir TROCA DE PLANO, " +
                "mas nao podem reescrever o EPP de dentro do plano escolhido.";

            App.LoggingService?.LogWarning($"{Tag} BLOQUEADO ({requester}): {reason}");
            return false;
        }

        /// <summary>
        /// [FIX:POWER-GATE] FILTRO PARA O SEGUNDO CAMINHO DE ESCRITA: O
        /// `powercfg.exe`.
        ///
        /// Treze lugares do app gravam energia por aqui, e nenhum deles passa
        /// pela API nativa — então o portão de `PowerNativeMethods` não os
        /// alcança. Como `SmartEnergyService.RunPowercfgChecked` é o wrapper de
        /// todos eles, filtrar aqui fecha o segundo caminho de uma vez.
        ///
        /// Só são barradas as escritas de VALOR (`/setacvalueindex`,
        /// `/setdcvalueindex`). A TROCA de plano (`/setactive`) é mantida e apenas
        /// registrada: Gamer Mode e Brain legitimately pedem troca de plano, e
        /// proibi-las aqui quebraria o app. O que não pode é a troca passar
        /// BATIDA — e para isso existe o re-assert do coordenador.
        /// </summary>
        public static bool IsBlockedByGate(string args, out string reason, out bool isPlanSwitch)
        {
            reason = string.Empty;
            isPlanSwitch = false;

            if (MaintenanceMode) return false;
            if (!HasManagedPlan) return false;

            string a = args ?? string.Empty;
            string lower = a.ToLowerInvariant();

            isPlanSwitch = lower.Contains("/setactive");

            bool isValueWrite = lower.Contains("/setacvalueindex") || lower.Contains("/setdcvalueindex");
            if (!isValueWrite) return false;

            // O esquema alvo é o primeiro GUID da linha de comando. Se for o
            // gerenciado (ou `SCHEME_CURRENT` apontando para ele), passa.
            var m = System.Text.RegularExpressions.Regex.Match(
                a, @"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})");

            if (m.Success && Guid.TryParse(m.Groups[1].Value, out Guid target))
            {
                if (IsManagedPlan(target)) return false;
                if (PowerNativeMethods.TryGetActiveSchemeGuid(out Guid active) && active == ManagedPlan) return false;
            }
            else if (lower.Contains("scheme_current"))
            {
                if (PowerNativeMethods.TryGetActiveSchemeGuid(out Guid activeCur) && IsManagedPlan(activeCur)) return false;
            }

            reason =
                $"gravacao de energia por powercfg para fora do plano gerenciado '{_managedPlanName}' ({ManagedPlan}). " +
                $"comando='{a.Trim()}'. Se esta e uma acao pedida pelo usuario na tela de Energia, " +
                "o modo de manutencao precisa estar ligado.";

            return true;
        }

        /// <summary>
        /// Registra uma tentativa de TROCA DE PLANO.
        ///
        /// Trocar o plano é o que realmente desfaz tudo: o plano gerenciado sai
        /// do ar e os valores gravados nele ficam inertes. Por isso a tentativa
        /// é registrada, e o coordenador se re-asserta logo depois.
        /// </summary>
        public static void NotifyPlanSwitchAttempt(string requester, string targetName, ILoggingService? logger)
        {
            if (MaintenanceMode)
            {
                logger?.LogInfo($"{Tag} Troca de plano para '{targetName}' liberada (modo manutencao).");
                return;
            }

            Guid managed = ManagedPlan;
            if (managed == Guid.Empty)
            {
                logger?.LogInfo($"{Tag} Troca de plano para '{targetName}' por '{requester}' (ainda sem plano gerenciado).");
                return;
            }

            logger?.LogWarning(
                $"{Tag} UPSURPACAO: '{requester}' tentou ativar o plano '{targetName}', " +
                $"tirando do ar o plano gerenciado '{_managedPlanName}' ({managed}). " +
                "O coordenador vai se re-assertar; o perfil Voltris permanece o dono da verdade.");
        }
    }
}
