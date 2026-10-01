using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Optimization
{
    /// <summary>
    /// Serviço de telemetria para orçamentos de performance.
    /// </summary>
    public class BudgetTelemetryService
    {
        private readonly ConcurrentQueue<BudgetTelemetryEvent> _eventQueue;
        private readonly string _logFilePath;
        private readonly object _lockObject = new object();
        
        private const int MaxQueueSize = 1000;
        private const int FlushThreshold = 500;

        /// <summary>
        /// Inicializa uma nova instância de <see cref="BudgetTelemetryService"/>.
        /// </summary>
        /// <param name="logFilePath">Caminho do arquivo de log.</param>
        public BudgetTelemetryService(string logFilePath = null)
        {
            _eventQueue = new ConcurrentQueue<BudgetTelemetryEvent>();
            _logFilePath = logFilePath ?? Path.Combine(
                Path.GetDirectoryName(typeof(BudgetTelemetryService).Assembly.Location),
                "budget_telemetry.log");
        }

        /// <summary>
        /// Registra um evento de telemetria.
        /// </summary>
        /// <param name="telemetryEvent">Evento de telemetria.</param>
        public async Task LogEventAsync(BudgetTelemetryEvent telemetryEvent)
        {
            if (telemetryEvent == null)
                throw new ArgumentNullException(nameof(telemetryEvent));

            // Adiciona o evento à fila
            await AddEventToQueueAsync(telemetryEvent);
            
            // Verifica se precisa fazer flush
            await CheckFlushThresholdAsync();
        }

        /// <summary>
        /// Obtém eventos de telemetria recentes.
        /// </summary>
        /// <param name="maxEvents">Número máximo de eventos a retornar.</param>
        /// <returns>Lista de eventos de telemetria.</returns>
        public async Task<List<BudgetTelemetryEvent>> GetRecentEventsAsync(int maxEvents)
        {
            var events = new List<BudgetTelemetryEvent>();
            var tempQueue = new Queue<BudgetTelemetryEvent>();
            
            // Copia eventos da fila (sem removê-los)
            foreach (var evt in _eventQueue)
            {
                tempQueue.Enqueue(evt);
            }
            
            // Retorna os eventos mais recentes
            int count = 0;
            while (tempQueue.Count > 0 && count < maxEvents)
            {
                events.Add(tempQueue.Dequeue());
                count++;
            }
            
            await Task.CompletedTask;
            return events;
        }

        /// <summary>
        /// Exporta eventos de telemetria para um arquivo CSV.
        /// </summary>
        /// <param name="filePath">Caminho do arquivo CSV.</param>
        /// <param name="startTime">Data/hora inicial para filtrar eventos.</param>
        /// <param name="endTime">Data/hora final para filtrar eventos.</param>
        /// <returns>Tarefa assíncrona.</returns>
        public async Task ExportToCsvAsync(string filePath, DateTime? startTime = null, DateTime? endTime = null)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("O caminho do arquivo não pode ser vazio.", nameof(filePath));

            var events = await GetFilteredEventsAsync(startTime, endTime);
            var csvContent = GenerateCsvContent(events);
            
            // Cria o diretório se não existir
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            // Escreve o arquivo CSV
            await File.WriteAllTextAsync(filePath, csvContent, Encoding.UTF8);
        }

        /// <summary>
        /// Gera estatísticas de uso dos orçamentos.
        /// </summary>
        /// <param name="startTime">Data/hora inicial para filtrar eventos.</param>
        /// <param name="endTime">Data/hora final para filtrar eventos.</param>
        /// <returns>Estatísticas de uso.</returns>
        public async Task<BudgetUsageStatistics> GenerateUsageStatisticsAsync(DateTime? startTime = null, DateTime? endTime = null)
        {
            var events = await GetFilteredEventsAsync(startTime, endTime);
            var stats = new BudgetUsageStatistics();
            
            // Calcula estatísticas básicas
            stats.TotalEvents = events.Count;
            stats.UniqueGames = new HashSet<string>(events.Select(e => e.GameId)).Count;
            
            // Conta tipos de eventos
            foreach (var evt in events)
            {
                switch (evt.EventType)
                {
                    case TelemetryEventType.BudgetSet:
                        stats.BudgetSetEvents++;
                        break;
                    case TelemetryEventType.BudgetExceeded:
                        stats.BudgetExceededEvents++;
                        break;
                    case TelemetryEventType.BudgetWithinLimits:
                        stats.BudgetWithinLimitsEvents++;
                        break;
                    case TelemetryEventType.BudgetRemoved:
                        stats.BudgetRemovedEvents++;
                        break;
                }
                
                // Acumula valores de recursos
                stats.TotalCpuUsage += evt.CpuUsagePercent;
                stats.TotalGpuUsage += evt.GpuUsagePercent;
                stats.TotalMemoryUsage += evt.MemoryUsageMB;
                stats.TotalIoRate += evt.IoRateMBps;
            }
            
            // Calcula médias
            if (events.Count > 0)
            {
                stats.AverageCpuUsage = stats.TotalCpuUsage / events.Count;
                stats.AverageGpuUsage = stats.TotalGpuUsage / events.Count;
                stats.AverageMemoryUsage = stats.TotalMemoryUsage / events.Count;
                stats.AverageIoRate = stats.TotalIoRate / events.Count;
            }
            
            await Task.CompletedTask;
            return stats;
        }

        /// <summary>
        /// Adiciona um evento à fila de telemetria.
        /// </summary>
        /// <param name="telemetryEvent">Evento de telemetria.</param>
        /// <returns>Tarefa assíncrona.</returns>
        private async Task AddEventToQueueAsync(BudgetTelemetryEvent telemetryEvent)
        {
            // Mantém o tamanho da fila controlado
            if (_eventQueue.Count >= MaxQueueSize)
            {
                _eventQueue.TryDequeue(out _); // Remove o mais antigo
            }
            
            _eventQueue.Enqueue(telemetryEvent);
            
            await Task.CompletedTask;
        }

        /// <summary>
        /// Verifica se o limiar de flush foi atingido.
        /// </summary>
        /// <returns>Tarefa assíncrona.</returns>
        private async Task CheckFlushThresholdAsync()
        {
            if (_eventQueue.Count >= FlushThreshold)
            {
                await FlushEventsToFileAsync();
            }
        }

        /// <summary>
        /// Faz flush dos eventos para o arquivo de log.
        /// </summary>
        /// <returns>Tarefa assíncrona.</returns>
        private async Task FlushEventsToFileAsync()
        {
            var eventsToFlush = new List<BudgetTelemetryEvent>();
            
            // Retira eventos da fila para flush
            int count = 0;
            while (_eventQueue.TryDequeue(out var evt) && count < FlushThreshold)
            {
                eventsToFlush.Add(evt);
                count++;
            }
            
            // Escreve eventos no arquivo
            if (eventsToFlush.Count > 0)
            {
                var logContent = GenerateLogContent(eventsToFlush);
                
                try
                {
                    await File.AppendAllTextAsync(_logFilePath, logContent, Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    // Em caso de erro, devolve os eventos à fila
                    foreach (var evt in eventsToFlush)
                    {
                        _eventQueue.Enqueue(evt);
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"Erro ao escrever log de telemetria: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Obtém eventos filtrados por data/hora.
        /// </summary>
        /// <param name="startTime">Data/hora inicial.</param>
        /// <param name="endTime">Data/hora final.</param>
        /// <returns>Lista de eventos filtrados.</returns>
        public async Task<List<BudgetTelemetryEvent>> GetFilteredEventsAsync(DateTime? startTime, DateTime? endTime)
        {
            var events = new List<BudgetTelemetryEvent>();
            
            // Copia eventos da fila
            foreach (var evt in _eventQueue)
            {
                events.Add(evt);
            }
            
            // Aplica filtros de data/hora
            if (startTime.HasValue)
            {
                events.RemoveAll(e => e.Timestamp < startTime.Value);
            }
            
            if (endTime.HasValue)
            {
                events.RemoveAll(e => e.Timestamp > endTime.Value);
            }
            
            await Task.CompletedTask;
            return events;
        }

        /// <summary>
        /// Gera conteúdo de log para eventos.
        /// </summary>
        /// <param name="events">Eventos a serem registrados.</param>
        /// <returns>Conteúdo do log.</returns>
        private string GenerateLogContent(List<BudgetTelemetryEvent> events)
        {
            var sb = new StringBuilder();
            
            foreach (var evt in events)
            {
                sb.AppendLine($"{evt.Timestamp:O}|{evt.GameId}|{evt.EventType}|CPU:{evt.CpuUsagePercent:F1}%|GPU:{evt.GpuUsagePercent:F1}%|MEM:{evt.MemoryUsageMB}MB|IO:{evt.IoRateMBps:F1}MB/s");
            }
            
            return sb.ToString();
        }

        /// <summary>
        /// Gera conteúdo CSV para eventos.
        /// </summary>
        /// <param name="events">Eventos a serem exportados.</param>
        /// <returns>Conteúdo CSV.</returns>
        private string GenerateCsvContent(List<BudgetTelemetryEvent> events)
        {
            var sb = new StringBuilder();
            
            // Cabeçalho CSV
            sb.AppendLine("Timestamp,GameId,EventType,CpuUsagePercent,GpuUsagePercent,MemoryUsageMB,IoRateMBps,AdditionalData");
            
            // Dados dos eventos
            foreach (var evt in events)
            {
                var additionalData = evt.AdditionalData != null ? 
                    string.Join(";", evt.AdditionalData.Select(kvp => $"{kvp.Key}={kvp.Value}")) : 
                    string.Empty;
                    
                sb.AppendLine($"{evt.Timestamp:O},{evt.GameId},{evt.EventType},{evt.CpuUsagePercent:F2},{evt.GpuUsagePercent:F2},{evt.MemoryUsageMB},{evt.IoRateMBps:F2},\"{additionalData}\"");
            }
            
            return sb.ToString();
        }
    }

    /// <summary>
    /// Evento de telemetria de orçamento.
    /// </summary>
    public class BudgetTelemetryEvent
    {
        /// <summary>
        /// ID do jogo.
        /// </summary>
        public string GameId { get; set; }

        /// <summary>
        /// Tipo do evento.
        /// </summary>
        public TelemetryEventType EventType { get; set; }

        /// <summary>
        /// Timestamp do evento.
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Percentual de uso da CPU.
        /// </summary>
        public double CpuUsagePercent { get; set; }

        /// <summary>
        /// Percentual de uso da GPU.
        /// </summary>
        public double GpuUsagePercent { get; set; }

        /// <summary>
        /// Uso de memória em MB.
        /// </summary>
        public long MemoryUsageMB { get; set; }

        /// <summary>
        /// Taxa de I/O em MB/s.
        /// </summary>
        public double IoRateMBps { get; set; }

        /// <summary>
        /// Dados adicionais do evento.
        /// </summary>
        public Dictionary<string, string> AdditionalData { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Tipos de eventos de telemetria.
    /// </summary>
    public enum TelemetryEventType
    {
        /// <summary>
        /// Orçamento definido.
        /// </summary>
        BudgetSet,

        /// <summary>
        /// Orçamento excedido.
        /// </summary>
        BudgetExceeded,

        /// <summary>
        /// Orçamento dentro dos limites.
        /// </summary>
        BudgetWithinLimits,

        /// <summary>
        /// Orçamento removido.
        /// </summary>
        BudgetRemoved,

        /// <summary>
        /// Estatísticas atualizadas.
        /// </summary>
        StatsUpdated
    }

    /// <summary>
    /// Estatísticas de uso dos orçamentos.
    /// </summary>
    public class BudgetUsageStatistics
    {
        /// <summary>
        /// Número total de eventos.
        /// </summary>
        public int TotalEvents { get; set; }

        /// <summary>
        /// Número de jogos únicos.
        /// </summary>
        public int UniqueGames { get; set; }

        /// <summary>
        /// Número de eventos de orçamento definido.
        /// </summary>
        public int BudgetSetEvents { get; set; }

        /// <summary>
        /// Número de eventos de orçamento excedido.
        /// </summary>
        public int BudgetExceededEvents { get; set; }

        /// <summary>
        /// Número de eventos de orçamento dentro dos limites.
        /// </summary>
        public int BudgetWithinLimitsEvents { get; set; }

        /// <summary>
        /// Número de eventos de orçamento removido.
        /// </summary>
        public int BudgetRemovedEvents { get; set; }

        /// <summary>
        /// Uso total de CPU.
        /// </summary>
        public double TotalCpuUsage { get; set; }

        /// <summary>
        /// Uso total de GPU.
        /// </summary>
        public double TotalGpuUsage { get; set; }

        /// <summary>
        /// Uso total de memória.
        /// </summary>
        public long TotalMemoryUsage { get; set; }

        /// <summary>
        /// Taxa total de I/O.
        /// </summary>
        public double TotalIoRate { get; set; }

        /// <summary>
        /// Média de uso da CPU.
        /// </summary>
        public double AverageCpuUsage { get; set; }

        /// <summary>
        /// Média de uso da GPU.
        /// </summary>
        public double AverageGpuUsage { get; set; }

        /// <summary>
        /// Média de uso de memória.
        /// </summary>
        public double AverageMemoryUsage { get; set; }

        /// <summary>
        /// Média de taxa de I/O.
        /// </summary>
        public double AverageIoRate { get; set; }
    }

    /// <summary>
    /// Cliente de telemetria para integração com serviços externos.
    /// </summary>
    public class TelemetryClient
    {
        private readonly BudgetTelemetryService _telemetryService;
        private readonly string _apiKey;
        private readonly string _endpointUrl;

        /// <summary>
        /// Inicializa uma nova instância de <see cref="TelemetryClient"/>.
        /// </summary>
        /// <param name="telemetryService">Serviço de telemetria.</param>
        /// <param name="apiKey">Chave de API para autenticação.</param>
        /// <param name="endpointUrl">URL do endpoint para envio de telemetria.</param>
        public TelemetryClient(BudgetTelemetryService telemetryService, string apiKey, string endpointUrl)
        {
            _telemetryService = telemetryService ?? throw new ArgumentNullException(nameof(telemetryService));
            _apiKey = apiKey;
            _endpointUrl = endpointUrl;
        }

        /// <summary>
        /// Envia eventos de telemetria para um serviço externo.
        /// </summary>
        /// <param name="startTime">Data/hora inicial para filtrar eventos.</param>
        /// <param name="endTime">Data/hora final para filtrar eventos.</param>
        /// <returns>Tarefa assíncrona.</returns>
        public async Task SendTelemetryAsync(DateTime? startTime = null, DateTime? endTime = null)
        {
            if (string.IsNullOrEmpty(_endpointUrl))
            {
                System.Diagnostics.Debug.WriteLine("Endpoint URL não configurado para envio de telemetria.");
                return;
            }
            
            var events = await _telemetryService.GetFilteredEventsAsync(startTime, endTime);
            
            if (events.Count == 0)
                return;
            
            // Em uma implementação real, serializaria os eventos e enviaria para o endpoint
            // Por exemplo, usando HttpClient para fazer uma requisição POST
            
            System.Diagnostics.Debug.WriteLine($"Enviando {events.Count} eventos de telemetria para {_endpointUrl}");
            
            await Task.CompletedTask;
        }

        /// <summary>
        /// Gera e envia um relatório de uso.
        /// </summary>
        /// <param name="startTime">Data/hora inicial para o relatório.</param>
        /// <param name="endTime">Data/hora final para o relatório.</param>
        /// <returns>Tarefa assíncrona.</returns>
        public async Task SendUsageReportAsync(DateTime? startTime = null, DateTime? endTime = null)
        {
            var stats = await _telemetryService.GenerateUsageStatisticsAsync(startTime, endTime);
            
            // Em uma implementação real, serializaria as estatísticas e enviaria para o endpoint
            System.Diagnostics.Debug.WriteLine($"Relatório de uso gerado: {stats.TotalEvents} eventos, {stats.UniqueGames} jogos únicos");
            
            await Task.CompletedTask;
        }
    }
}
