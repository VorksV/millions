namespace VoltrisOptimizer.UI.ViewModels
{
    /// <summary>
    /// Estados EMOCIONAIS do rosto da IA (AiFaceView).
    ///
    /// IMPORTANTE -isto NAO e uma inteligencia nova. Cada valor e a representacao
    /// visual de um estado JA EXISTE do VOLTRIS BRAIN
    /// (<c>VoltrisBrainV2</c> / <c>BrainStateOrchestrator</c> /
    /// <c>BrainMetricsCache</c>). O ViewModel (<c>DashboardViewModel</c>) deriva
    /// estes valores a partir de dados reais do Brain e a camada visual
    /// (<c>AiFaceExpressionLibrary</c>) os traduz em geometria e cor. Nenhum
    /// estado aqui e inventado na ausencia de um sinal do Brain.
    ///
    /// A cor representa o que a IA esta FAZENDO (rampa de intensidade
    /// ambar -> laranja -> vermelho), nao "como ela se sente". A expressao facial
    /// e o que carrega a personalidade.
    ///
    /// ORDEM: os 10 primeiros valores sao historicos e nao podem ser reordenados
    /// (o enum alimenta binds e caches). Os novos estados foram APPENDADOS.
    /// </summary>
    public enum BrainMood
    {
        /// <summary>NEUTRO/REPOUSO. Brain desligado ou em standby.</summary>
        Sleeping,

        /// <summary>NEUTRO. Ligada, ociosa, sem atividade relevante.</summary>
        Idle,

        /// <summary>PENSANDO. Analisando metricas para decidir.</summary>
        Thinking,

        /// <summary>PROCESSANDO. Executando otimizacoes.</summary>
        Working,

        /// <summary>CONCENTRACAO. Carga alta, decisao sustentada.</summary>
        Focused,

        /// <summary>RAIVA/ALERTA. Sobrecarga termica confirmada.</summary>
        Furious,

        /// <summary>FELIZ. Recompensa subindo (o Q-Learning aprendendo).</summary>
        Happy,

        /// <summary>TRISTE. Resultado ruim, recompensa caindo.</summary>
        Sad,

        /// <summary>Sem trabalho faz tempo. Cinza-azulado dessaturado de proposito.</summary>
        Bored,

        /// <summary>PREOCUPADO. Anomalia detectada sem severidade critica.</summary>
        Concerned,

        // ─────────── estados acrescentados na reconstrucao visual ───────────

        /// <summary>
        /// ATENTO. Contexto operacional mudou (Gaming/Work/Rendering) ou o
        /// <c>BrainStateOrchestrator</c> mudou de estado. O Brain esta orientedo
        /// para uma carga nova, nao necessariamente sob carga.
        /// </summary>
        Attentive,

        /// <summary>
        /// SATISFEITO. Otimizacao concluida com melhora mensuravel
        /// (<c>SessionReward</c> subiu depois de um ciclo concluido).
        /// </summary>
        Satisfied,

        /// <summary>
        /// MOTIVADO. Progresso de aprendizado sustentado: episodios crescendo
        /// (<c>Decision.Episodes</c>) com recompensa positiva consistente.
        /// </summary>
        Motivated,

        /// <summary>
        /// FRUSTRADO. Acoes do Brain falhando repetidamente
        /// (<c>ActionExecuted</c> com <c>Executed == false</c> em sequencia).
        /// </summary>
        Frustrated,

        /// <summary>
        /// SURPRESA. Pico abrupto de carga detectado pelo proprio Brain
        /// (<c>StutterDetected</c> ou salto grande de CPU num ciclo).
        /// </summary>
        Surprised,

        /// <summary>
        /// CHORANDO. Frustracao sustentada com recompensa negativa: o unico
        /// estado que usa Lagrimas. Discreto por projeto.
        /// </summary>
        Weeping,

        /// <summary>
        /// SUCESSO. O Brain publicou <c>BrainEventSeverity.Success</c> no
        /// <c>BrainObservabilityHub</c> (evento real, ja existente, antes sem
        /// assinante).
        /// </summary>
        Succeeded,

        /// <summary>
        /// ERRO. O Brain publicou <c>BrainEventSeverity.Error</c> no
        /// <c>BrainObservabilityHub</c>, ou a execucao de acao foi rejeitada.
        /// </summary>
        Errored
    }
}
