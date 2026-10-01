using System;
using System.Collections.Generic;
using System.Diagnostics;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Power
{
    /// <summary>
    /// [FIX:PRIORIDADE-UNICA] O DONO ÚNICO DO PROCESSO DO JOGO.
    ///
    /// O PROBLEMA QUE ESTE SUBSTITUI
    /// ============================
    /// A auditoria do projeto inteiro encontrou <b>62 arquivos distintos</b>
    /// escrevendo prioridade de processo, cada um com seu próprio timer e seu
    /// próprio valor: `High`, `AboveNormal`, `Normal`, `BelowNormal` — e, em
    /// `AudioLatencyEliminationService`, <c>RealTime</c>.
    ///
    /// Não era um problema de valor errado. Era um problema de DONO: com 62
    /// escritores, a prioridade final do processo do jogo era o que o último
    /// timer a disparar tivesse deixado. O Perfil Inteligente não tinha
    /// autoridade nenhuma sobre isso — a matriz declarava
    /// <c>GameCpuPriority</c> e <b>nada no projeto lia o campo</b>.
    ///
    /// E havia uma contradição direta: a REGRA 7 do self-test proíbe fixar
    /// núcleos do jogo, e o `ActionLayer` faz exatamente isso com
    /// <c>SetProcessAffinityMask</c>. A regra passava porque verificava a
    /// tabela, e o código que rodava estava em outro lugar.
    ///
    /// POR QUE UM DONO RESOLVE, E NÃO UMA REGRA A MAIS
    /// ===============================================
    /// Acrescentar "não use RealTime" a uma tabela não impede ninguém de usá-lo:
    /// qualquer um dos 62 writers continua livre para pedir. A regra só tem
    /// efeito no ponto em que a ESCRITA acontece — que é exatamente onde a
    /// energia já foi corrigida, com o `PowerWriteGate`, e o resultado foi
    /// zero escrita fora do dono.
    ///
    /// Este é o mesmo padrão aplicado ao processo. A diferença é que aqui o
    /// dono também <b>decide</b>, porque o objeto da decisão é o mesmo perfil
    /// que decide a energia.
    ///
    /// O QUE ESTE TIPO NÃO FAZ
    /// ========================
    /// Não executa otimização nenhuma. Não mexe em registro, não mexe em
    /// serviço, não chama driver. Ele aplica uma prioridade a um PID, guarda o
    /// valor original e devolve depois. É pequeno de propósito: um dono com
    /// muitas funções deixa de ser dono.
    /// </summary>
    public static class GamerProcessAuthority
    {
        private const string Tag = "[GamerProc]";

        private static readonly object Sync = new object();

        /// <summary>PID do jogo -> prioridade original, para devolver no fim da sessão.</summary>
        private static readonly Dictionary<int, ProcessPriorityClass> Originals =
            new Dictionary<int, ProcessPriorityClass>();

        /// <summary>
        /// Valor máximo que este dono aceita aplicar. Existe para que a
        /// restrição não dependa apenas do chamador passar um enum válido: se
        /// alguém chamar com um valor numérico grande, o teto aqui segura.
        /// </summary>
        public const int MaxAllowedPriority = (int)GameProcessPriority.High;

        /// <summary>
        /// O que a política decidiu para este PID, e por quê.
        /// </summary>
        public static GamerProcessPlan? Current { get; private set; }

        /// <summary>Último motivo registrado, para o log.</summary>
        public static string LastReason { get; private set; } = "";

        /// <summary>
        /// Aplica a prioridade resolvida pela <see cref="GamerProcessPolicy"/>.
        ///
        /// Guarda o valor original na primeira aplicação, para que
        /// <see cref="Restore"/> consiga devolver — sem isso, um processo que já
        /// estava em <c>High</c> por outro caminho voltaria para <c>Normal</c>
        /// ao fim do jogo, e o Perfil teria acabado de mudar algo que era de
        /// outro dono.
        /// </summary>
        public static bool Apply(
            int pid,
            GamerProcessPlan plan,
            string gameName,
            ILoggingService? logger)
        {
            if (plan == null) return false;

            try
            {
                using Process? proc = TryGet(pid);
                if (proc == null)
                {
                    logger?.LogWarning($"{Tag} PID {pid} ({gameName}) nao existe mais; nada a aplicar.");
                    return false;
                }

                // Guarda o original uma única vez por PID.
                lock (Sync)
                {
                    if (!Originals.ContainsKey(pid))
                    {
                        Originals[pid] = proc.PriorityClass;
                    }
                }

                int requested = (int)plan.Priority;

                if (requested > MaxAllowedPriority)
                {
                    logger?.LogWarning(
                        $"{Tag} RECUSADO: prioridade {requested} acima do teto do Perfil ({MaxAllowedPriority}).");
                    return false;
                }

                var target = ToWindowsPriority(plan.Priority);

                if (proc.PriorityClass == target)
                {
                    logger?.LogInfo($"{Tag} {gameName} (pid {pid}) ja esta em {target}; nada a fazer.");
                    Record(plan);
                    return true;
                }

                proc.PriorityClass = target;

                Current = plan;
                LastReason = plan.Reason;

                logger?.LogInfo(
                    $"{Tag} {gameName} (pid {pid}): {proc.PriorityClass} -> {target} | {plan}");
                return true;
            }
            catch (Exception ex)
            {
                logger?.LogWarning($"{Tag} Falha ao aplicar prioridade em '{gameName}' (pid {pid}): {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Devolve a prioridade original do processo.
        ///
        /// Chamado quando o jogo fecha. Se o original não estava registrado,
        /// nada é feito: neste ponto o Perfil nunca mexeu no processo, e mexer
        /// agora seria mudar algo de outro dono.
        /// </summary>
        public static bool Restore(int pid, string gameName, ILoggingService? logger)
        {
            ProcessPriorityClass original;

            lock (Sync)
            {
                if (!Originals.TryGetValue(pid, out original))
                {
                    logger?.LogInfo($"{Tag} {gameName} (pid {pid}) sem registro de prioridade; nada a restaurar.");
                    Current = null;
                    return false;
                }
                Originals.Remove(pid);
            }

            try
            {
                using Process? proc = TryGet(pid);
                if (proc == null) return false;

                proc.PriorityClass = original;
                Current = null;
                logger?.LogInfo($"{Tag} {gameName} (pid {pid}): prioridade restaurada para {original}.");
                return true;
            }
            catch (Exception ex)
            {
                logger?.LogWarning($"{Tag} Falha ao restaurar prioridade de '{gameName}' (pid {pid}): {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Consulta o portão: este pedido é permitido?
        ///
        /// Existe para que um serviço legado possa VERIFICAR antes de escrever,
        /// em vez de ser bloqueado depois de já ter escrito. É a diferença entre
        /// uma trava que impede e uma que reclama.
        /// </summary>
        public static bool IsBlocked(
            string requester,
            ProcessPriorityClass requested,
            bool isAffinityPin,
            out string reason)
        {
            reason = "";

            if (isAffinityPin)
            {
                reason =
                    $"{requester}: afinidade de nucleo RECUSADA. A REGRA 7 proibe fixar nucleos do jogo — " +
                    "a Intel desaconselha em CPU hibrida, onde um thread preso pode passar fome.";
                return true;
            }

            if (requested == ProcessPriorityClass.RealTime)
            {
                reason =
                    $"{requester}: RealTime RECUSADO. Processo em tempo real rouba a CPU de todo o sistema; " +
                    "a matriz do Perfil ja dizia 'nunca RealTime' e o enum nao tinha o valor para impedir. " +
                    $"Use no maximo {GameProcessPriority.High}.";
                return true;
            }

            return false;
        }

        /// <summary>Processos sob gestão deste dono, para diagnóstico.</summary>
        public static int ManagedCount
        {
            get { lock (Sync) { return Originals.Count; } }
        }

        private static void Record(GamerProcessPlan plan)
        {
            Current = plan;
            LastReason = plan.Reason;
        }

        private static ProcessPriorityClass ToWindowsPriority(GameProcessPriority p) => p switch
        {
            GameProcessPriority.High => ProcessPriorityClass.High,
            GameProcessPriority.AboveNormal => ProcessPriorityClass.AboveNormal,
            _ => ProcessPriorityClass.Normal
        };

        private static Process? TryGet(int pid)
        {
            try
            {
                Process proc = Process.GetProcessById(pid);
                return proc.HasExited ? null : proc;
            }
            catch
            {
                return null;
            }
        }
    }
}
