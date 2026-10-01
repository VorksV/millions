using System;
using System.Diagnostics;

namespace VoltrisOptimizer.Services.Responsiveness
{
    /// <summary>
    /// Representa um boost temporário de prioridade aplicado a um processo.
    /// Controla o decaimento automático — o boost expira após a duração configurada.
    ///
    /// DECISÃO: Boosts contínuos degradam a eficiência do scheduler.
    /// Micro-bursts (1–2s) são mais eficazes: o processo recebe prioridade
    /// quando precisa (abertura, input) e volta ao normal automaticamente.
    /// Isso evita que o sistema fique "quase Realtime sem querer".
    /// </summary>
    internal sealed class PriorityBoostToken
    {
        public int Pid { get; init; }
        public string ProcessName { get; init; } = "";
        public ProcessPriorityClass OriginalCpuPriority { get; init; }
        public IoPriorityTarget OriginalIoPriority { get; init; }
        public DateTime AppliedAt { get; init; }
        public TimeSpan Duration { get; init; }
        public BoostReason Reason { get; init; }

        public bool IsExpired => DateTime.UtcNow - AppliedAt >= Duration;

        /// <summary>
        /// Tempo restante do boost. Zero se expirado.
        /// </summary>
        public TimeSpan Remaining => IsExpired
            ? TimeSpan.Zero
            : Duration - (DateTime.UtcNow - AppliedAt);

        public override string ToString() =>
            $"BoostToken[{ProcessName}({Pid}) reason={Reason} remaining={Remaining.TotalMilliseconds:F0}ms]";
    }

    public enum BoostReason
    {
        /// <summary>Usuário interagiu com o processo (clique, tecla)</summary>
        UserInput,
        /// <summary>Processo entrou em foreground</summary>
        ForegroundChange,
        /// <summary>Processo recém-lançado</summary>
        ProcessLaunch,
        /// <summary>Comando DSL explícito</summary>
        DslCommand,
        /// <summary>App fullscreen detectado</summary>
        FocusMode
    }
}
