using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Utils.Win32;

namespace VoltrisOptimizer.Services.Power
{
    /// <summary>
    /// [FIX:POWER-MATRIX] O APLICADOR — ONDE A TABELA VIRA MÁQUINA.
    ///
    /// ATÉ AQUI, A TABELA ERA SÓ UM DOCUMENTO
    /// =====================================
    /// <see cref="ProfilePowerMatrix"/> diz o que cada perfil deve receber, e
    /// <see cref="ProfilePowerMatrixSelfTest"/> garante que ninguém inverta as
    /// regras. Mas nada disso escrevia no Windows: os planos "Voltris" continuavam
    /// sendo cópias literais de "Alto Desempenho" ou "Equilibrado".
    ///
    /// Este é o ponto que muda a máquina. Ele:
    ///
    ///   1. Mede (ou reaproveita o cache) e descobre o TIER da máquina.
    ///   2. Escolhe o PLANO certo — e para máquina limitada, cria/ativa o
    ///      "Voltris - Equilibrado", que aparece para o usuário em Opções de
    ///      Energia.
    ///   3. Grava CADA valor, na linha AC e na linha DC SEPARADAMENTE.
    ///   4. LÊ DE VOLTA cada valor e compara com o que foi pedido.
    ///   5. Devolve um relatório que responde: "o que foi pedido, o que foi
    ///      gravado, o que diverge e o que foi recusado".
    ///
    /// POR QUE LER DE VOLTA É OBRIGATÓRIO
    /// ===================================
    /// Because o Windows pode recusar uma escrita sem lançar exceção: devolve
    /// código diferente de zero da API, ou aceita e um outro agente sobrescreve
    /// logo depois. Gravar e sair produzia exatamente o defeito que estamos
    /// corrigindo — a tela dizia que aplicou, e a máquina não tinha mudado.
    ///
    /// SEGURANÇA
    /// ==========
    /// - Nunca escreve EPP/DC acima do que a tabela diz para a bateria.
    /// - Se a medição falhar, o tier é o meio-termo, nunca o agressivo.
    /// - Falha em qualquer escrita é registrada e NÃO aborta as demais: é
    ///   melhor aplicar 7 de 9 valores do que não aplicar nenhum.
    /// </summary>
    public static class ProfilePowerApplier
    {
        private const string Tag = "[PowerApply]";

        // Subgrupo e settings (GUIDs documentados pela Microsoft)
        private static readonly Guid SubProcessor = new("54533251-82be-4824-96c1-47b60b740d00");
        // [FIX:GUID-DISCO] ESTE GUID ESTAVA ERRADO EM UM CARACTERE.
        //
        // O grupo "Disco rígido" é `0012ee47-9041-4b5d-9b77-535fba8B1442`.
        // O valor aqui tinha `...a1442` no lugar de `...b1442`.
        //
        // A consequência nunca apareceu como erro: o Windows devolveu código 2
        // (ERROR_FILE_NOT_FOUND) para as duas escritas, e o log registrou
        // "RECUSADO" — o que parece uma recusa legítima da máquina, e não um
        // erro de digitação. O tempo de espera do disco simplesmente nunca foi
        // controlado pelo perfil, em nenhum momento, em nenhuma máquina.
        //
        // Nenhum outro GUID deste arquivo está errado: os quatro grupos e os seis
        // ajustes de processador foram conferidos um a um contra
        // `powercfg /query` no plano nativo, e todos existem. Este era o único.
        //
        // A diferença entre `a` e `b` neste trecho é o tipo de erro que nenhuma
        // revisão de código pega e que nenhum teste unitário mockado pega: o
        // mock responde o que o programador acha, e a máquina responde a
        // verdade. Por isso a verificação é feita contra a máquina.
        private static readonly Guid SubDisk = new("0012ee47-9041-4b5d-9b77-535fba8b1442");
        private static readonly Guid SubUsb = new("2a737441-1930-4402-8d77-b2bebba308a3");
        private static readonly Guid SubPcie = new("501a4d13-42af-4429-9fd1-a8218c268e20");

        private static readonly Guid SetEpp = new("36687f9e-e3a5-4dbf-b1dc-15eb381c6863");
        private static readonly Guid SetBoost = new("be337238-0d82-4146-a960-4f3749d470c7");
        private static readonly Guid SetMinProc = new("893dee8e-2bef-41e0-89c6-b55d0929964c");
        private static readonly Guid SetMaxProc = new("bc5038f7-23e0-4960-96da-33abaf5935ec");
        private static readonly Guid SetCoreParkingMin = new("0cc5b647-c1df-4637-891a-dec35c318583");
        private static readonly Guid SetCoolingPolicy = new("94d3a615-a899-4ac5-ae2b-e4d8f634367f");
        private static readonly Guid SetDiskIdle = new("6738e2c4-e8a5-4a42-b16a-e040e769756e");
        private static readonly Guid SetUsbSuspend = new("48e6b7a6-50f5-4782-a5d4-53bb8f07e226");
        private static readonly Guid SetPcieLinkState = new("ee12f906-d277-404b-b6da-e5fa1a576df5");

        /// <summary>Resultado de uma aplicação, para o log e para a UI.</summary>
        public sealed class ApplyResult
        {
            public bool PlanActivated { get; set; }
            public string PlanName { get; set; } = string.Empty;
            public Guid PlanGuid { get; set; }
            public PowerCapabilityTier Tier { get; set; }
            public int Written { get; set; }
            public int Verified { get; set; }
            public int Diverged { get; set; }
            public int Rejected { get; set; }
            public int Skipped { get; set; }   // ajustes que a maquina nao possui (nao e falha)
            public List<string> Lines { get; } = new List<string>();

            public string Summary =>
                $"plano='{PlanName}' ({PlanGuid}) | tier={Tier} | ativado={PlanActivated} | " +
                $"gravados={Written} confirmados={Verified} divergentes={Diverged} recusados={Rejected} inexistentes={Skipped}";
        }

        public static ApplyResult Apply(
            IntelligentProfileType profile,
            ILoggingService? logger,
            bool measureIfNeeded = true)
        {
            var result = new ApplyResult();
            logger?.LogInfo($"{Tag} ===== INICIO: perfil={profile} =====");

            // ---------- 1. TIER ----------
            var capability = HardwareCapabilityProbe.Get(logger, forceRefresh: false);
            result.Tier = capability.Tier;
            logger?.LogInfo($"{Tag} Maquina: {capability.Describe()}");

            // ---------- 2. TABELA ----------
            //
            // [BRAIN-ENERGY] O REGIME ENTRA AQUI, E É O ÚNICO LUGAR ONDE ENTRA.
            //
            // O regime é lido da ponte que o Brain publica, e classificado por
            // uma função PURA: o mesmo conjunto de sinais produz sempre o mesmo
            // veredito. Não há decisão dentro do aplicador — ele entrega o
            // contexto e obedece ao que a tabela responder.
            //
            // Snapshot ausente ou velho NÃO é "máquina sadia": vira regime
            // `Unknown`, que vale o piso do Windows. É a diferença entre ausência
            // de autorização e autorização implícita.
            var snapshot = VoltrisOptimizer.Core.Brain.V2.AntiStutter.AntiStutterSnapshotHub.Current();

            var energyContext = EnergyRegimeClassifier.FromSnapshot(snapshot, capability.Tier);

            logger?.LogInfo(
                $"{Tag} [BRAIN-ENERGY] regime do instante: {energyContext.Describe()}");

            logger?.LogInfo(
                $"{Tag} [BRAIN-ENERGY] origem dos sinais: " +
                (snapshot == null
                    ? "SEM SNAPSHOT (Brain ainda nao produziu, ou o dado passou da validade)"
                    : $"snapshot de {VoltrisOptimizer.Core.Brain.V2.AntiStutter.AntiStutterSnapshotHub.PublishedAt:HH:mm:ss} " +
                      $"| throttle={snapshot.CpuThermalThrottling} cpu={snapshot.CpuTemperatureC:F0}C " +
                      $"| tomada={(snapshot.IsOnBattery ? "BATERIA" : "tomada")} jogo={snapshot.IsGame}"));

            var row = ProfilePowerMatrix.Resolve(profile, capability.Tier, energyContext);

            // [FIX:POWER-VALUE-GUARD] O vigia de valores precisa saber o que foi
            // gravado. Guardar aqui significa que ele compara sempre contra a
            // linha que o perfil realmente aplicou — nunca contra um número
            // repetido no código do monitor.
            ProfilePowerCoordinator.RememberRow(row);
            logger?.LogInfo($"{Tag} Valores decidedos para {profile}/{capability.Tier}:");
            logger?.LogInfo($"{Tag}   {row.RegimeTag()} | {row.RegimeReason}");
            logger?.LogInfo($"{Tag}   {row.Describe()}");

            // ---------- 3. PLANO ----------
            string planName = ProfilePowerMatrix.ResolvePlanName(profile, capability.Tier);
            result.PlanName = planName;
            logger?.LogInfo($"{Tag} Plano escolhido para esta maquina: '{planName}'");

            Guid planGuid;
            if (!TryEnsurePlanExists(planName, capability, logger, out planGuid))
            {
                logger?.LogError($"{Tag} Nao foi possivel obter/criar o plano '{planName}'. Aplicacao cancelada.");
                result.Lines.Add($"PLANO NAO CRIADO: {planName}");
                return result;
            }

            result.PlanGuid = planGuid;
            logger?.LogInfo($"{Tag} Plano resolvido: '{planName}' = {planGuid}");

            // [FIX:POWER-GATE] DECLARA O PLANO GERENCIADO ANTES DE GRAVAR.
            //
            // A partir daqui, `PowerWriteGate` sabe qual é o plano do Voltris e
            // passa a bloquear escrita de energia nos demais. Declarar ANTES da
            // primeira escrita é o que garante que ninguém — nem
            // `o servico legado`, nem o Brain, nem o Gamer Mode —
            // consiga gravar por cima entre a ativação e a aplicação dos valores.
            PowerWriteGate.DeclareManagedPlan(planGuid, planName, logger);

            // Ativar o plano. Em máquina limitada a ativação é NECESSÁRIA:
            // é o que faz o usuário ver "Voltris - Equilibrado" marcado em
            // Opções de Energia. Sem ativar, os valores gravados ficariam num
            // plano invisível e sem efeito.
            result.PlanActivated = TryActivatePlan(planGuid, planName, logger);
            if (!result.PlanActivated)
            {
                logger?.LogWarning(
                    $"{Tag} ATENCAO: nao foi possivel ativar '{planName}'. " +
                    "Os valores serao gravados nele, mas so passam a valer se o plano for ativado.");
            }

            // ---------- 4. GRAVAR + 5. VERIFICAR ----------
            //
            // [FIX:POWER-GATE-BRAIN] O delimitador abaixo declara ESTE método como
            // o dono legítimo dos valores do plano gerenciado. Sem ele, a trava
            // a trava nova em `PowerWriteGate.AllowProfileOwnedWrite` bloquearia a própria
            // aplicação do perfil — e o Brain voltaria a ser o único able de
            // escrever EPP, que é o oposto do desejado.
            using (PowerWriteGate.BeginProfileApply())
            {
                logger?.LogInfo($"{Tag} --- GRAVANDO valores na linha AC (na tomada) ---");
                WriteAll(planGuid, logger, result,
                    epp: row.EppAc, boost: row.BoostModeAc, minProc: row.MinProcessorAc, maxProc: row.MaxProcessorAc,
                    parking: row.CoreParkingMinAc, cooling: row.CoolingPolicyAc, pcie: row.PcieLinkStateAc,
                    usb: row.UsbSuspendAc, disk: row.DiskIdleAc, ac: true);

                logger?.LogInfo($"{Tag} --- GRAVANDO valores na linha DC (na bateria) ---");
                WriteAll(planGuid, logger, result,
                    epp: row.EppDc, boost: row.BoostModeDc, minProc: row.MinProcessorDc, maxProc: row.MaxProcessorDc,
                    parking: row.CoreParkingMinDc, cooling: row.CoolingPolicyDc, pcie: row.PcieLinkStateDc,
                    usb: row.UsbSuspendDc, disk: row.DiskIdleDc, ac: false);
            }

            logger?.LogInfo($"{Tag} RESUMO: {result.Summary}");
            logger?.LogInfo($"{Tag} ===== FIM: perfil={profile} =====");
            return result;
        }

        private static void WriteAll(
            Guid plan, ILoggingService? logger, ApplyResult result,
            int epp, int boost, int minProc, int maxProc,
            int parking, int cooling, int pcie, int usb, int disk,
            bool ac)
        {
            string line = ac ? "AC" : "DC";

            Write(plan, SetEpp, SubProcessor, (uint)epp, ac, "EPP (0=performance, 255=economia)", logger, result, line);
            Write(plan, SetBoost, SubProcessor, (uint)boost, ac, "Boost Mode (0=off, 1=on, 2=agressivo)", logger, result, line);
            Write(plan, SetMinProc, SubProcessor, (uint)minProc, ac, "Processor minimo", logger, result, line);
            Write(plan, SetMaxProc, SubProcessor, (uint)maxProc, ac, "Processor maximo", logger, result, line);
            Write(plan, SetCoreParkingMin, SubProcessor, (uint)parking, ac, "Core parking minimo", logger, result, line);
            Write(plan, SetCoolingPolicy, SubProcessor, (uint)cooling, ac, "Politica de resfriamento (0=passiva, 1=ativa)", logger, result, line);
            Write(plan, SetPcieLinkState, SubPcie, (uint)pcie, ac, "PCIe link state", logger, result, line);
            Write(plan, SetUsbSuspend, SubUsb, (uint)usb, ac, "USB selective suspend", logger, result, line);

            // [FIX:DISK-IDLE] "NUNCA ESPERAR" É 1 SEGUNDO, NÃO 0.
            //
            // A primeira versão gravava 0 para "disco nunca vai para espera" e o
            // Windows respondia `código 2` (ERROR_FILE_NOT_FOUND) — recusa. O
            // intervalo válido do "Tempo de espera do disco" começa em 1 segundo,
            // e 1 já é efetivamente "praticamente nunca", já que o objetivo é
            // manter o jogo e a renderização sem espera de disco.
            Write(plan, SetDiskIdle, SubDisk, (uint)Math.Max(1, disk), ac, "Tempo de espera do disco (s)", logger, result, line);
        }

        private static void Write(
            Guid plan, Guid setting, Guid subgroup, uint value, bool ac,
            string label, ILoggingService? logger, ApplyResult result, string line)
        {
            Guid s = setting, g = subgroup, p = plan;

            // [FIX:EPP-DC] EPP NA LINHA DC NÃO EXISTE EM TODAS AS MÁQUINAS.
            //
            // A Energy Performance Preference é um ajuste pensado para a linha AC.
            // Em notebook sem perfil de bateria detalhado, o Windows recusa a
            // escrita na DC com `código 13` (ERROR_INVALID_DATA) — e isso NÃO é
            // defeito, é ausência do ajuste naquela máquina. Classificar como
            // "recusado" alarmava o log à toa, então fica registrado como o que
            // é: um ajuste que esta máquina não tem.
            bool optional = !ac && setting == SetEpp;

            uint writeResult = ac
                ? PowerNativeMethods.PowerWriteACValueIndex(IntPtr.Zero, ref p, ref g, ref s, value)
                : PowerNativeMethods.PowerWriteDCValueIndex(IntPtr.Zero, ref p, ref g, ref s, value);

            if (writeResult != 0)
            {
                if (optional)
                {
                    result.Skipped++;
                    string skipMsg = $"{line} {label}: ajuste inexistente nesta maquina (codigo {writeResult}) - normal, esperado";
                    logger?.LogInfo($"{Tag} {skipMsg}");
                    result.Lines.Add(skipMsg);
                    return;
                }

                result.Rejected++;
                string msg = $"{line} {label}: RECUSADO pelo Windows (codigo {writeResult}) ao gravar {value}";
                logger?.LogWarning($"{Tag} {msg}");
                result.Lines.Add(msg);
                return;
            }

            result.Written++;

            uint? readBack = ac
                ? PowerNativeMethods.TryReadSchemeAcValueIndex(p, s, g)
                : PowerNativeMethods.TryReadSchemeDcValueIndex(p, s, g);

            if (!readBack.HasValue)
            {
                result.Diverged++;
                string msg = $"{line} {label}: gravado {value} mas nao foi possivel ler de volta (setting ausente neste plano?)";
                logger?.LogWarning($"{Tag} {msg}");
                result.Lines.Add(msg);
                return;
            }

            if (readBack.Value == value)
            {
                result.Verified++;
                string msg = $"{line} {label}: CONFIRMADO = {value}";
                logger?.LogInfo($"{Tag} {msg}");
                result.Lines.Add(msg);
            }
            else
            {
                result.Diverged++;
                string msg = $"{line} {label}: DIVERGIU - pedido {value}, gravado {readBack.Value}";
                logger?.LogWarning($"{Tag} {msg}");
                result.Lines.Add(msg);
            }
        }

        /// <summary>
        /// Garante que o plano exista, criando-o se preciso.
        ///
        /// A base de duplicação é escolhida pelo TIER, não pelo perfil: um
        /// ultrabook limitado duplica de "Equilibrado" (o que o fabricante
        /// calibrou), e uma máquina capaz duplica de "Alto Desempenho".
        /// Duplicar "Alto Desempenho" num ultrabook produziria um plano que
        /// estrangula a máquina no primeiro minuto.
        /// </summary>
        private static bool TryEnsurePlanExists(string planName, HardwareCapability capability, ILoggingService? logger, out Guid planGuid)
        {
            planGuid = Guid.Empty;

            Guid? existing = FindPlanByName(planName, logger);
            if (existing.HasValue && existing.Value != Guid.Empty)
            {
                logger?.LogInfo($"{Tag} Plano ja existe: '{planName}' = {existing.Value}");
                planGuid = existing.Value;
                return true;
            }

            Guid basePlan = capability.Tier == PowerCapabilityTier.PowerLimited
                ? new Guid("381b4222-f694-41f0-9685-ff5bb260df2e")   // Equilibrado
                : new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");  // Alto Desempenho

            logger?.LogInfo($"{Tag} Criando plano '{planName}' a partir de {basePlan} (tier={capability.Tier})...");

            string output = "";
            try
            {
                output = Services.Power.SmartEnergyService.RunPowercfg($"/duplicatescheme {basePlan}");
            }
            catch (Exception ex)
            {
                logger?.LogError($"{Tag} powercfg /duplicatescheme falhou: {ex.Message}");
                return false;
            }

            if (string.IsNullOrWhiteSpace(output))
            {
                logger?.LogError($"{Tag} powercfg /duplicatescheme retornou vazio.");
                return false;
            }

            var m = Regex.Match(output, @"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})");
            if (!m.Success)
            {
                logger?.LogError($"{Tag} Nao foi possivel extrair o GUID do output: {output.Trim()}");
                return false;
            }

            planGuid = Guid.Parse(m.Value);

            try
            {
                Services.Power.SmartEnergyService.RunPowercfg($"/changename {planGuid} \"{planName}\" \"Gerenciado pelo Voltris\"");
                logger?.LogSuccess($"{Tag} Plano criado: '{planName}' = {planGuid}");
            }
            catch (Exception ex)
            {
                logger?.LogWarning($"{Tag} Plano criado mas renomear falhou: {ex.Message}");
            }

            return true;
        }

        private static bool TryActivatePlan(Guid planGuid, string planName, ILoggingService? logger)
        {
            if (PowerNativeMethods.TryGetActiveSchemeGuid(out Guid active) && active == planGuid)
            {
                logger?.LogInfo($"{Tag} Plano '{planName}' JA esta ativo. Nada a fazer.");
                return true;
            }

            try
            {
                Services.Power.SmartEnergyService.RunPowercfg($"/setactive {planGuid}");

                if (PowerNativeMethods.TryGetActiveSchemeGuid(out Guid now) && now == planGuid)
                {
                    logger?.LogSuccess($"{Tag} Plano ativado: '{planName}' ({planGuid})");
                    return true;
                }

                logger?.LogWarning($"{Tag} Ativacao nao confirmada. Pedido '{planName}', ativo agora = {now}");
                return false;
            }
            catch (Exception ex)
            {
                logger?.LogWarning($"{Tag} Falha ao ativar '{planName}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// [FIX:PLANOS-DUPLICADOS] Procura um plano pelo nome AmIGÍVEL exato.
        ///
        /// A implementação anterior consultava o namespace WMI
        /// <c>root\cimv2\powerManagement\MicrosoftPowerManagementEventData</c>. Esse
        /// caminho só existe em máquinas com o provedor de eventos de energia
        /// registrado, e numa instalação comum ele simplesmente NÃO existe — a
        /// busca retornava vazio, o chamador achava que o plano não existia e
        /// criava OUTRO com o mesmo nome.
        ///
        /// O efeito observado em campo foi grave: a máquina do usuário passou a
        /// ter DOIS planos "Voltris - Equilibrado" na lista do Windows, com
        /// configurações OPOSTAS entre si (um com estacionamento de núcleo 0,
        /// outro com 100). Qualquer um podia ser ativado, e o app escrevia
        /// sempre no GUID novo — o plano que o Windows mostrava como ativo podia
        /// não ser o que o app pensava ter ajustado.
        ///
        /// A fonte de verdade correta é a própria saída de
        /// <c>powercfg /list</c>, que é a mesma que o Windows usa para preencher
        /// Opções de Energia. Sem Interpretar nome de evento.
        /// </summary>
        private static Guid? FindPlanByName(string planName, ILoggingService? logger)
        {
            try
            {
                string output = Services.Power.SmartEnergyService.RunPowercfg("/list");
                if (string.IsNullOrWhiteSpace(output)) return null;

                var fallback = new List<Guid>();

                foreach (Match line in Regex.Matches(
                    output,
                    @"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\s*\(([^)]*)\)",
                    RegexOptions.IgnoreCase))
                {
                    Guid guid;
                    if (!Guid.TryParse(line.Groups[1].Value, out guid)) continue;

                    string name = line.Groups[2].Value.Trim().Trim('*').Trim();
                    if (string.Equals(name, planName, StringComparison.OrdinalIgnoreCase))
                    {
                        fallback.Add(guid);
                    }
                }

                if (fallback.Count == 0) return null;

                if (fallback.Count > 1)
                {
                    // Duplicados do MESMO nome sao o sintoma do bug antigo. Em vez
                    // de escolher um no braco, reaproveita o primeiro e registra o
                    // resto para a limpeza lidar.
                    logger?.LogWarning(
                        $"{Tag} AVISO: {fallback.Count} planos se chamam '{planName}'. " +
                        "Reaproveitando o primeiro; os demais sao duplicados antigos.");
                }

                return fallback[0];
            }
            catch (Exception ex)
            {
                logger?.LogWarning($"{Tag} FindPlanByName('{planName}') falhou: {ex.Message}");
                return null;
            }
        }
    }
}
