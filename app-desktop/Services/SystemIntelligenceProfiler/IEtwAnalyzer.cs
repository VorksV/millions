using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.SystemIntelligenceProfiler
{
    /// <summary>
    /// Interface para o analisador ETW (Event Tracing for Windows).
    /// </summary>
    public interface IEtwAnalyzer
    {
        /// <summary>
        /// Inicia a coleta de eventos ETW.
        /// </summary>
        /// <param name="sessionName">Nome da sessão ETW.</param>
        /// <param name="providers">Lista de provedores de eventos.</param>
        /// <returns>Tarefa assíncrona.</returns>
        Task StartCollectionAsync(string sessionName, List<EtwProvider> providers);

        /// <summary>
        /// Para a coleta de eventos ETW.
        /// </summary>
        /// <returns>Tarefa assíncrona.</returns>
        Task StopCollectionAsync();

        /// <summary>
        /// Aplica filtros aos eventos coletados.
        /// </summary>
        /// <param name="filters">Filtros a serem aplicados.</param>
        /// <returns>Tarefa assíncrona.</returns>
        Task ApplyFiltersAsync(List<EtwFilter> filters);

        /// <summary>
        /// Obtém eventos coletados.
        /// </summary>
        /// <param name="maxEvents">Número máximo de eventos a retornar.</param>
        /// <returns>Lista de eventos ETW.</returns>
        Task<List<EtwEvent>> GetCollectedEventsAsync(int maxEvents = 1000);

        /// <summary>
        /// Gera um relatório de análise dos eventos coletados.
        /// </summary>
        /// <returns>Relatório de análise ETW.</returns>
        Task<EtwAnalysisReport> GenerateAnalysisReportAsync();

        /// <summary>
        /// Evento disparado quando novos eventos são coletados.
        /// </summary>
        event EventHandler<EtwEventsCollectedEventArgs> EventsCollected;

        /// <summary>
        /// Sinais de governança para o orquestrador (Loop de Feedback).
        /// </summary>
        event EventHandler<GovernanceSignalEventArgs> GovernanceSignalDetected;
    }

    /// <summary>
    /// Provedor de eventos ETW.
    /// </summary>
    public class EtwProvider
    {
        /// <summary>
        /// Nome ou GUID do provedor.
        /// </summary>
        public string NameOrGuid { get; set; }

        /// <summary>
        /// Nível de detalhe dos eventos.
        /// </summary>
        public EtwEventLevel Level { get; set; } = EtwEventLevel.Informational;

        /// <summary>
        /// Palavras-chave para filtrar tipos de eventos.
        /// </summary>
        public ulong Keywords { get; set; } = 0;
    }

    /// <summary>
    /// Níveis de eventos ETW.
    /// </summary>
    public enum EtwEventLevel
    {
        /// <summary>
        /// Evento de log sempre escrito.
        /// </summary>
        LogAlways = 0,

        /// <summary>
        /// Evento crítico.
        /// </summary>
        Critical = 1,

        /// <summary>
        /// Erro.
        /// </summary>
        Error = 2,

        /// <summary>
        /// Aviso.
        /// </summary>
        Warning = 3,

        /// <summary>
        /// Informacional.
        /// </summary>
        Informational = 4,

        /// <summary>
        /// Verbose.
        /// </summary>
        Verbose = 5
    }

    /// <summary>
    /// Filtro para eventos ETW.
    /// </summary>
    public class EtwFilter
    {
        /// <summary>
        /// Tipo de filtro.
        /// </summary>
        public EtwFilterType FilterType { get; set; }

        /// <summary>
        /// Valor do filtro.
        /// </summary>
        public string FilterValue { get; set; }

        /// <summary>
        /// Operador do filtro.
        /// </summary>
        public EtwFilterOperator Operator { get; set; }
    }

    /// <summary>
    /// Tipos de filtros ETW.
    /// </summary>
    public enum EtwFilterType
    {
        /// <summary>
        /// Filtra por ID do evento.
        /// </summary>
        EventId,

        /// <summary>
        /// Filtra por nome do provedor.
        /// </summary>
        ProviderName,

        /// <summary>
        /// Filtra por nível do evento.
        /// </summary>
        Level,

        /// <summary>
        /// Filtra por palavra-chave.
        /// </summary>
        Keyword,

        /// <summary>
        /// Filtra por mensagem do evento.
        /// </summary>
        Message,

        /// <summary>
        /// Filtra por ID do processo.
        /// </summary>
        ProcessId,

        /// <summary>
        /// Filtra por ID da thread.
        /// </summary>
        ThreadId
    }

    /// <summary>
    /// Operadores para filtros ETW.
    /// </summary>
    public enum EtwFilterOperator
    {
        /// <summary>
        /// Igual.
        /// </summary>
        Equals,

        /// <summary>
        /// Diferente.
        /// </summary>
        NotEquals,

        /// <summary>
        /// Contém.
        /// </summary>
        Contains,

        /// <summary>
        /// Não contém.
        /// </summary>
        NotContains,

        /// <summary>
        /// Maior que.
        /// </summary>
        GreaterThan,

        /// <summary>
        /// Menor que.
        /// </summary>
        LessThan,

        /// <summary>
        /// Começa com.
        /// </summary>
        StartsWith,

        /// <summary>
        /// Termina com.
        /// </summary>
        EndsWith
    }

    /// <summary>
    /// Evento ETW coletado.
    /// </summary>
    public class EtwEvent
    {
        /// <summary>
        /// ID do evento.
        /// </summary>
        public int EventId { get; set; }

        /// <summary>
        /// Nome do provedor.
        /// </summary>
        public string ProviderName { get; set; }

        /// <summary>
        /// Nível do evento.
        /// </summary>
        public EtwEventLevel Level { get; set; }

        /// <summary>
        /// Palavras-chave do evento.
        /// </summary>
        public ulong Keywords { get; set; }

        /// <summary>
        /// ID do processo.
        /// </summary>
        public int ProcessId { get; set; }

        /// <summary>
        /// ID da thread.
        /// </summary>
        public int ThreadId { get; set; }

        /// <summary>
        /// Timestamp do evento.
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Mensagem do evento.
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Dados adicionais do evento.
        /// </summary>
        public Dictionary<string, object> Payload { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// Argumentos para evento de eventos coletados.
    /// </summary>
    public class EtwEventsCollectedEventArgs : EventArgs
    {
        /// <summary>
        /// Eventos coletados.
        /// </summary>
        public List<EtwEvent> Events { get; }

        /// <summary>
        /// Inicializa uma nova instância de <see cref="EtwEventsCollectedEventArgs"/>.
        /// </summary>
        /// <param name="events">Eventos coletados.</param>
        public EtwEventsCollectedEventArgs(List<EtwEvent> events)
        {
            Events = events ?? throw new ArgumentNullException(nameof(events));
        }
    }
}
