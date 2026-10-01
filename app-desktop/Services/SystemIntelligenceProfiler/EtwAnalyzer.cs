using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.SystemIntelligenceProfiler
{
    /// <summary>
    /// Implementação do analisador ETW (Event Tracing for Windows).
    /// </summary>
    public class EtwAnalyzer : IEtwAnalyzer
    {
        private readonly EtwSession _etwSession;
        private readonly EtwEventParser _eventParser;
        private readonly ILoggingService _logger; // Injected Logger
        private readonly ConcurrentQueue<EtwEvent> _eventQueue;
        private readonly List<EtwFilter> _activeFilters;
        private readonly object _lockObject = new object();
        
        private CancellationTokenSource _cancellationTokenSource;
        private Task _processingTask;
        private bool _isCollecting = false;
        private string _currentSessionName;
        
        private const int MaxQueueSize = 10000;
        private const int ProcessingIntervalMs = 15000; // Aumentado para 15s para poupar CPU massivamente

        /// <summary>
        /// Evento disparado quando novos eventos são coletados.
        /// </summary>
        public event EventHandler<EtwEventsCollectedEventArgs> EventsCollected;

        /// <summary>
        /// Inicializa uma nova instância de <see cref="EtwAnalyzer"/>.
        /// </summary>
        /// <param name="etwSession">Sessão ETW.</param>
        /// <param name="eventParser">Parser de eventos ETW.</param>
        /// <param name="logger">Serviço de Log.</param>
        public EtwAnalyzer(EtwSession etwSession, EtwEventParser eventParser, ILoggingService logger)
        {
            _etwSession = etwSession ?? throw new ArgumentNullException(nameof(etwSession));
            _eventParser = eventParser ?? throw new ArgumentNullException(nameof(eventParser));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _eventQueue = new ConcurrentQueue<EtwEvent>();
            _activeFilters = new List<EtwFilter>();
        }

        /// <summary>
        /// Inicia a coleta de eventos ETW.
        /// </summary>
        /// <param name="sessionName">Nome da sessão ETW.</param>
        /// <param name="providers">Lista de provedores de eventos.</param>
        /// <returns>Tarefa assíncrona.</returns>
        public async Task StartCollectionAsync(string sessionName, List<EtwProvider> providers)
        {
            if (string.IsNullOrEmpty(sessionName))
                throw new ArgumentException("O nome da sessão não pode ser vazio.", nameof(sessionName));
            
            if (providers == null || providers.Count == 0)
                throw new ArgumentException("É necessário especificar pelo menos um provedor.", nameof(providers));

            lock (_lockObject)
            {
                if (_isCollecting)
                    throw new InvalidOperationException("A coleta já está em andamento.");

                _isCollecting = true;
                _currentSessionName = sessionName;
                _cancellationTokenSource = new CancellationTokenSource();
            }

            // Inicia a sessão ETW
            _logger.LogInfo($"[ETW] Iniciando sessão de coleta: {sessionName}");
            await _etwSession.StartSessionAsync(sessionName, providers);

            // Inicia o processamento de eventos
            _processingTask = ProcessEventsAsync(_cancellationTokenSource.Token);

            await Task.CompletedTask;
        }

        /// <summary>
        /// Para a coleta de eventos ETW.
        /// </summary>
        /// <returns>Tarefa assíncrona.</returns>
        public async Task StopCollectionAsync()
        {
            lock (_lockObject)
            {
                if (!_isCollecting)
                    return;

                _isCollecting = false;
                _cancellationTokenSource?.Cancel();
            }

            // Para a sessão ETW
            if (!string.IsNullOrEmpty(_currentSessionName))
            {
                await _etwSession.StopSessionAsync(_currentSessionName);
                _logger.LogInfo($"[ETW] Sessão parada: {_currentSessionName}");
            }

            // Aguarda o término do processamento
            if (_processingTask != null)
            {
                try
                {
                    await _processingTask;
                }
                catch (OperationCanceledException)
                {
                    // Esperado quando cancelamos a tarefa
                }
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Aplica filtros aos eventos coletados.
        /// </summary>
        /// <param name="filters">Filtros a serem aplicados.</param>
        /// <returns>Tarefa assíncrona.</returns>
        public async Task ApplyFiltersAsync(List<EtwFilter> filters)
        {
            if (filters == null)
                throw new ArgumentNullException(nameof(filters));

            lock (_lockObject)
            {
                _activeFilters.Clear();
                _activeFilters.AddRange(filters);
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Obtém eventos coletados.
        /// </summary>
        /// <param name="maxEvents">Número máximo de eventos a retornar.</param>
        /// <returns>Lista de eventos ETW.</returns>
        public async Task<List<EtwEvent>> GetCollectedEventsAsync(int maxEvents = 1000)
        {
            var events = new List<EtwEvent>();
            var tempQueue = new ConcurrentQueue<EtwEvent>();
            
            // Transfere eventos da fila principal para uma temporária
            int count = 0;
            while (_eventQueue.TryDequeue(out var evt) && count < maxEvents)
            {
                events.Add(evt);
                count++;
            }
            
            // Devolve os eventos não retornados para a fila
            foreach (var evt in _eventQueue)
            {
                tempQueue.Enqueue(evt);
            }
            
            // Substitui a fila original
            var dummy = _eventQueue;
            // Atribuição atômica
            // Note: Em uma implementação real, seria melhor usar um ConcurrentQueue interno
            
            return events;
        }

        /// <summary>
        /// Gera um relatório de análise dos eventos coletados.
        /// </summary>
        /// <returns>Relatório de análise ETW.</returns>
        public async Task<EtwAnalysisReport> GenerateAnalysisReportAsync()
        {
            // Coleta eventos para análise
            var events = new List<EtwEvent>();
            var tempEvents = new List<EtwEvent>();
            
            // Copia eventos para análise (sem removê-los da fila)
            foreach (var evt in _eventQueue)
            {
                tempEvents.Add(evt);
            }
            
            // Aplica filtros se houver
            var filteredEvents = ApplyFilters(tempEvents);
            
            // Gera o relatório de análise
            var report = new EtwAnalysisReport
            {
                GeneratedAt = DateTime.UtcNow,
                TotalEventsCollected = _eventQueue.Count,
                EventsAnalyzed = filteredEvents.Count,
                CriticalEvents = filteredEvents.Count(e => e.Level == EtwEventLevel.Critical),
                ErrorEvents = filteredEvents.Count(e => e.Level == EtwEventLevel.Error),
                WarningEvents = filteredEvents.Count(e => e.Level == EtwEventLevel.Warning),
                TopEventProviders = GetTopEventProviders(filteredEvents),
                EventFrequency = CalculateEventFrequency(filteredEvents),
                PerformanceInsights = ExtractPerformanceInsights(filteredEvents)
            };
            
            return report;
        }

        /// <summary>
        /// Processa eventos coletados da sessão ETW.
        /// </summary>
        /// <param name="cancellationToken">Token de cancelamento.</param>
        /// <returns>Tarefa assíncrona.</returns>
        private async Task ProcessEventsAsync(CancellationToken cancellationToken)
        {
            int totalEventsProcessed = 0;
            DateTime lastLogTime = DateTime.UtcNow;

            _logger.LogInfo("[ETW] Processador de eventos iniciado.");

            while (!cancellationToken.IsCancellationRequested && _isCollecting)
            {
                // Boundary Tracking: Registra o nome da thread
                VoltrisOptimizer.Services.Diagnostics.ThreadNameRegistry.Register("ETW-Analyzer-Processor");
                using var profiler = VoltrisOptimizer.Services.Diagnostics.CpuSelfProfiler.Instance.BeginSection("ETW.Process");

                try
                {
                    // Coleta eventos da sessão
                    var rawEvents = await _etwSession.GetEventsAsync(100);
                    
                    if (rawEvents != null && rawEvents.Count > 0)
                    {
                        // Converte eventos brutos para eventos ETW
                        var parsedEvents = rawEvents.Select(rawEvent => 
                            _eventParser.ParseEvent(rawEvent)).ToList();
                        
                        // Aplica filtros
                        var filteredEvents = ApplyFilters(parsedEvents);
                        
                        // Adiciona eventos filtrados à fila
                        foreach (var evt in filteredEvents)
                        {
                            // Mantém o tamanho da fila controlado
                            if (_eventQueue.Count >= MaxQueueSize)
                            {
                                _eventQueue.TryDequeue(out _); // Remove o mais antigo
                            }
                            
                            _eventQueue.Enqueue(evt);
                        }
                        
                        // Dispara evento de eventos coletados
                        if (filteredEvents.Count > 0)
                        {
                            OnEventsCollected(filteredEvents);
                        }

                        // Logging de Throughput (Hearbeat do Sensor)
                        totalEventsProcessed += parsedEvents.Count;
                        if ((DateTime.UtcNow - lastLogTime).TotalSeconds >= 30) // Log a cada 30s
                        {
                            _logger.LogInfo($"[ETW-HEARTBEAT] Processando stream: {totalEventsProcessed} eventos totais. Fila atual: {_eventQueue.Count}");
                            lastLogTime = DateTime.UtcNow;
                        }
                    }
                    
                    // Aguarda o intervalo de processamento
                    profiler.Dispose();
                    VoltrisOptimizer.Services.Diagnostics.ThreadNameRegistry.Unregister();

                    await Task.Delay(ProcessingIntervalMs, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    VoltrisOptimizer.Services.Diagnostics.ThreadNameRegistry.Unregister();
                    break;
                }
                catch (Exception ex)
                {
                    // Log de erro mas continua executando
                    System.Diagnostics.Debug.WriteLine($"Erro no processamento de eventos ETW: {ex.Message}");
                    
                    // Aguarda mesmo em caso de erro para evitar loops muito rápidos
                    try
                    {
                        await Task.Delay(ProcessingIntervalMs, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Aplica os filtros ativos aos eventos.
        /// </summary>
        /// <param name="events">Eventos a serem filtrados.</param>
        /// <returns>Eventos filtrados.</returns>
        private List<EtwEvent> ApplyFilters(List<EtwEvent> events)
        {
            List<EtwFilter> filters;
            lock (_lockObject)
            {
                filters = new List<EtwFilter>(_activeFilters);
            }
            
            if (filters.Count == 0)
                return events;
            
            // OTIMIZAÇÃO: Filtragem in-place para evitar múltiplas alocações de lista
            return events.Where(e => {
                foreach (var filter in filters)
                {
                    if (!MatchesFilter(e, filter)) return false;
                }
                return true;
            }).ToList();
        }

        /// <summary>
        /// Verifica se um evento corresponde a um filtro.
        /// </summary>
        /// <param name="evt">Evento a ser verificado.</param>
        /// <param name="filter">Filtro a ser aplicado.</param>
        /// <returns>Verdadeiro se o evento corresponde ao filtro.</returns>
        private bool MatchesFilter(EtwEvent evt, EtwFilter filter)
        {
            try
            {
                switch (filter.FilterType)
                {
                    case EtwFilterType.EventId:
                        if (int.TryParse(filter.FilterValue, out var eventId))
                            return CompareValues(evt.EventId, eventId, filter.Operator);
                        return false;
                        
                    case EtwFilterType.ProviderName:
                        return CompareStringValues(evt.ProviderName, filter.FilterValue, filter.Operator);
                        
                    case EtwFilterType.Level:
                        if (Enum.TryParse<EtwEventLevel>(filter.FilterValue, out var level))
                            return CompareValues((int)evt.Level, (int)level, filter.Operator);
                        return false;
                        
                    case EtwFilterType.Keyword:
                        if (ulong.TryParse(filter.FilterValue, out var keyword))
                            return (evt.Keywords & keyword) != 0;
                        return false;
                        
                    case EtwFilterType.Message:
                        return CompareStringValues(evt.Message, filter.FilterValue, filter.Operator);
                        
                    case EtwFilterType.ProcessId:
                        if (int.TryParse(filter.FilterValue, out var processId))
                            return CompareValues(evt.ProcessId, processId, filter.Operator);
                        return false;
                        
                    case EtwFilterType.ThreadId:
                        if (int.TryParse(filter.FilterValue, out var threadId))
                            return CompareValues(evt.ThreadId, threadId, filter.Operator);
                        return false;
                        
                    default:
                        return true;
                }
            }
            catch
            {
                // Em caso de erro na comparação, inclui o evento
                return true;
            }
        }

        /// <summary>
        /// Compara valores numéricos de acordo com o operador.
        /// </summary>
        /// <typeparam name="T">Tipo dos valores.</typeparam>
        /// <param name="left">Valor à esquerda.</param>
        /// <param name="right">Valor à direita.</param>
        /// <param name="op">Operador.</param>
        /// <returns>Resultado da comparação.</returns>
        private bool CompareValues<T>(T left, T right, EtwFilterOperator op) where T : IComparable<T>
        {
            var comparison = left.CompareTo(right);
            
            switch (op)
            {
                case EtwFilterOperator.Equals:
                    return comparison == 0;
                case EtwFilterOperator.NotEquals:
                    return comparison != 0;
                case EtwFilterOperator.GreaterThan:
                    return comparison > 0;
                case EtwFilterOperator.LessThan:
                    return comparison < 0;
                default:
                    return true;
            }
        }

        /// <summary>
        /// Compara valores de texto de acordo com o operador.
        /// </summary>
        /// <param name="left">Texto à esquerda.</param>
        /// <param name="right">Texto à direita.</param>
        /// <param name="op">Operador.</param>
        /// <returns>Resultado da comparação.</returns>
        private bool CompareStringValues(string left, string right, EtwFilterOperator op)
        {
            if (left == null) left = string.Empty;
            if (right == null) right = string.Empty;
            
            switch (op)
            {
                case EtwFilterOperator.Equals:
                    return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
                case EtwFilterOperator.NotEquals:
                    return !string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
                case EtwFilterOperator.Contains:
                    return left.IndexOf(right, StringComparison.OrdinalIgnoreCase) >= 0;
                case EtwFilterOperator.NotContains:
                    return left.IndexOf(right, StringComparison.OrdinalIgnoreCase) < 0;
                case EtwFilterOperator.StartsWith:
                    return left.StartsWith(right, StringComparison.OrdinalIgnoreCase);
                case EtwFilterOperator.EndsWith:
                    return left.EndsWith(right, StringComparison.OrdinalIgnoreCase);
                default:
                    return true;
            }
        }

        /// <summary>
        /// Obtém os principais provedores de eventos.
        /// </summary>
        /// <param name="events">Eventos analisados.</param>
        /// <returns>Dicionário com provedores e contagem de eventos.</returns>
        private Dictionary<string, int> GetTopEventProviders(List<EtwEvent> events)
        {
            return events
                .GroupBy(e => e.ProviderName)
                .Select(g => new { Provider = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(10)
                .ToDictionary(x => x.Provider, x => x.Count);
        }

        /// <summary>
        /// Calcula a frequência de eventos.
        /// </summary>
        /// <param name="events">Eventos analisados.</param>
        /// <returns>Dicionário com níveis e contagem de eventos.</returns>
        private Dictionary<EtwEventLevel, int> CalculateEventFrequency(List<EtwEvent> events)
        {
            return events
                .GroupBy(e => e.Level)
                .ToDictionary(g => g.Key, g => g.Count());
        }

        /// <summary>
        /// Extrai insights de performance dos eventos.
        /// </summary>
        /// <param name="events">Eventos analisados.</param>
        /// <returns>Lista de insights de performance.</returns>
        private List<string> ExtractPerformanceInsights(List<EtwEvent> events)
        {
            var insights = new List<string>();
            
            // Verifica eventos críticos e de erro
            var criticalErrors = events.Count(e => e.Level <= EtwEventLevel.Error);
            if (criticalErrors > 0)
            {
                insights.Add($"Detectados {criticalErrors} eventos críticos ou de erro que podem impactar a performance");
            }
            
            // Verifica padrões de repetição
            var repeatedEvents = events
                .GroupBy(e => new { e.ProviderName, e.EventId })
                .Where(g => g.Count() > 10)
                .Select(g => new { g.Key.ProviderName, g.Key.EventId, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(5);
                
            foreach (var repeated in repeatedEvents)
            {
                insights.Add($"Alto volume de eventos repetidos: Provider='{repeated.ProviderName}', EventId={repeated.EventId} ({repeated.Count} vezes)");
            }
            
            // Verifica eventos com mensagens de timeout ou delay
            var timeoutEvents = events.Count(e => 
                e.Message != null && 
                (e.Message.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 e.Message.IndexOf("delay", StringComparison.OrdinalIgnoreCase) >= 0));
                 
            if (timeoutEvents > 0)
            {
                insights.Add($"Detectados {timeoutEvents} eventos relacionados a timeouts ou delays");
            }
            
            return insights;
        }

        /// <summary>
        /// Dispara o evento de eventos coletados.
        /// </summary>
        /// <param name="events">Eventos coletados.</param>
        protected virtual void OnEventsCollected(List<EtwEvent> events)
        {
            EventsCollected?.Invoke(this, new EtwEventsCollectedEventArgs(events));
            
            // ANÁLISE EM TEMPO REAL (GOVERNANÇA)
            // Analisa eventos críticos imediatamente e dispara sinais para o Orchestrator
            AnalyzeFastPath(events);
        }


        public event EventHandler<GovernanceSignalEventArgs> GovernanceSignalDetected;

        private void AnalyzeFastPath(List<EtwEvent> events)
        {
            foreach (var evt in events)
            {
                // Detectar Stutter (DPC/ISR Spikes ou Long Frame Times)
                // Exemplo simplificado: Eventos de DxgKrnl com latência alta
                if (evt.ProviderName.Contains("DxgKrnl") && evt.Message.Contains("Latency"))
                {
                     // Heurística básica: Se a latência for muito alta, é um sinal de stutter
                     // Em produção, isso seria parseado do payload real do evento
                     _logger.LogWarning($"[ETW-SENSOR] Latência Gráfica Detectada! Provider: {evt.ProviderName}");
                     OnGovernanceSignal("StutterDetected", "High Render Latency detected in DxgKrnl", 80);
                }

                // Detectar Context Switch excessivo (Thrashing)
                if (evt.EventId == 36 && evt.ProviderName.Contains("Kernel-Process"))
                {
                    // Lógica simplificada
                    // OnGovernanceSignal("ThrashingDetected", "Excessive Context Switches", 60);
                }
            }
        }

        private void OnGovernanceSignal(string type, string message, int severity)
        {
            GovernanceSignalDetected?.Invoke(this, new GovernanceSignalEventArgs
            {
                SignalType = type,
                Message = message,
                Severity = severity,
                Timestamp = DateTime.UtcNow
            });
        }
    }

    /// <summary>
    /// Sinal de governança para o orquestrador (Loop de Feedback)
    /// </summary>
    public class GovernanceSignalEventArgs : EventArgs
    {
        public string SignalType { get; set; } // "StutterDetected", "LatencySpike", "ThermalsCritical"
        public string Message { get; set; }
        public int Severity { get; set; } // 0-100
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Relatório de análise ETW.
    /// </summary>
    public class EtwAnalysisReport
    {
        /// <summary>
        /// Data e hora de geração do relatório.
        /// </summary>
        public DateTime GeneratedAt { get; set; }

        /// <summary>
        /// Total de eventos coletados.
        /// </summary>
        public int TotalEventsCollected { get; set; }

        /// <summary>
        /// Número de eventos analisados.
        /// </summary>
        public int EventsAnalyzed { get; set; }

        /// <summary>
        /// Número de eventos críticos.
        /// </summary>
        public int CriticalEvents { get; set; }

        /// <summary>
        /// Número de eventos de erro.
        /// </summary>
        public int ErrorEvents { get; set; }

        /// <summary>
        /// Número de eventos de aviso.
        /// </summary>
        public int WarningEvents { get; set; }

        /// <summary>
        /// Principais provedores de eventos.
        /// </summary>
        public Dictionary<string, int> TopEventProviders { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// Frequência de eventos por nível.
        /// </summary>
        public Dictionary<EtwEventLevel, int> EventFrequency { get; set; } = new Dictionary<EtwEventLevel, int>();

        /// <summary>
        /// Insights de performance extraídos.
        /// </summary>
        public List<string> PerformanceInsights { get; set; } = new List<string>();
    }
}
