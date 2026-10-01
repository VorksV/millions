using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.SystemIntelligenceProfiler
{
    /// <summary>
    /// Coletor de eventos ETW de alta frequência.
    /// </summary>
    public class EtwHighFrequencyCollector
    {
        private readonly EtwSession _etwSession;
        private readonly ConcurrentQueue<EtwEvent> _eventBuffer;
        private readonly SemaphoreSlim _bufferSemaphore;
        private readonly object _lockObject = new object();
        
        private CancellationTokenSource _cancellationTokenSource;
        private Task _collectionTask;
        private bool _isCollecting = false;
        
        private const int BufferSize = 100000; // 100k eventos
        private const int FlushThreshold = 80000; // 80k eventos
        private const int CollectionIntervalMs = 15000; // Aumentado para 15s para poupar CPU

        /// <summary>
        /// Tamanho máximo do buffer de eventos.
        /// </summary>
        public int MaxBufferSize { get; set; } = BufferSize;

        /// <summary>
        /// Limiar para flush automático do buffer.
        /// </summary>
        public int FlushThresholdCount { get; set; } = FlushThreshold;

        /// <summary>
        /// Evento disparado quando o buffer atinge o limiar de flush.
        /// </summary>
        public event EventHandler<EtwBufferFlushEventArgs> BufferFlushThresholdReached;

        /// <summary>
        /// Evento disparado quando eventos são coletados.
        /// </summary>
        public event EventHandler<EtwEventsCollectedEventArgs> EventsCollected;

        /// <summary>
        /// Inicializa uma nova instância de <see cref="EtwHighFrequencyCollector"/>.
        /// </summary>
        /// <param name="etwSession">Sessão ETW.</param>
        public EtwHighFrequencyCollector(EtwSession etwSession)
        {
            _etwSession = etwSession ?? throw new ArgumentNullException(nameof(etwSession));
            _eventBuffer = new ConcurrentQueue<EtwEvent>();
            _bufferSemaphore = new SemaphoreSlim(1, 1);
        }

        /// <summary>
        /// Inicia a coleta de alta frequência.
        /// </summary>
        /// <param name="sessionName">Nome da sessão ETW.</param>
        /// <param name="providers">Provedores de eventos.</param>
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
                _cancellationTokenSource = new CancellationTokenSource();
            }

            // Inicia a sessão ETW
            await _etwSession.StartSessionAsync(sessionName, providers);

            // Inicia a tarefa de coleta
            _collectionTask = CollectEventsAsync(_cancellationTokenSource.Token);

            await Task.CompletedTask;
        }

        /// <summary>
        /// Para a coleta de alta frequência.
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
            // Nota: Em uma implementação real, precisaríamos do nome da sessão
            // await _etwSession.StopSessionAsync(sessionName);

            // Aguarda o término da coleta
            if (_collectionTask != null)
            {
                try
                {
                    await _collectionTask;
                }
                catch (OperationCanceledException)
                {
                    // Esperado quando cancelamos a tarefa
                }
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Obtém eventos do buffer.
        /// </summary>
        /// <param name="maxEvents">Número máximo de eventos a retornar.</param>
        /// <returns>Lista de eventos.</returns>
        public async Task<List<EtwEvent>> GetEventsAsync(int maxEvents)
        {
            await _bufferSemaphore.WaitAsync();
            try
            {
                var events = new List<EtwEvent>();
                int count = 0;
                
                while (_eventBuffer.TryDequeue(out var evt) && count < maxEvents)
                {
                    events.Add(evt);
                    count++;
                }
                
                return events;
            }
            finally
            {
                _bufferSemaphore.Release();
            }
        }

        /// <summary>
        /// Flush o buffer de eventos.
        /// </summary>
        /// <returns>Eventos que estavam no buffer.</returns>
        public async Task<List<EtwEvent>> FlushBufferAsync()
        {
            await _bufferSemaphore.WaitAsync();
            try
            {
                var events = new List<EtwEvent>();
                
                while (_eventBuffer.TryDequeue(out var evt))
                {
                    events.Add(evt);
                }
                
                return events;
            }
            finally
            {
                _bufferSemaphore.Release();
            }
        }

        /// <summary>
        /// Coleta eventos da sessão ETW.
        /// </summary>
        /// <param name="cancellationToken">Token de cancelamento.</param>
        /// <returns>Tarefa assíncrona.</returns>
        private async Task CollectEventsAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _isCollecting)
            {
                // Boundary Tracking: Registra o nome da thread
                VoltrisOptimizer.Services.Diagnostics.ThreadNameRegistry.Register("ETW-HighFreq-Collector");

                try
                {
                    using var profiler = VoltrisOptimizer.Services.Diagnostics.CpuSelfProfiler.Instance.BeginSection("ETW.Collect");

                    // Coleta eventos da sessão
                    var events = await _etwSession.GetEventsAsync(1000);
                    
                    if (events != null && events.Count > 0)
                    {
                        // Processa e adiciona eventos ao buffer
                        foreach (var rawEvent in events)
                        {
                            // Em uma implementação real, parsearíamos o evento bruto
                            var evt = new EtwEvent
                            {
                                EventId = 1001,
                                ProviderName = "Microsoft-Windows-Kernel-Process",
                                Level = EtwEventLevel.Informational,
                                Keywords = 0x1000000000000000,
                                ProcessId = new Random().Next(1000, 5000),
                                ThreadId = new Random().Next(100, 1000),
                                Timestamp = DateTime.UtcNow,
                                Message = "Evento de alta frequência coletado"
                            };
                            
                            // Adiciona ao buffer com controle de tamanho
                            await AddEventToBufferAsync(evt);
                        }
                        
                        // Dispara evento de eventos coletados
                        OnEventsCollected(events.Count);
                    }
                    
                    // Verifica se precisa fazer flush do buffer
                    await CheckBufferFlushAsync();
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    // Log de erro mas continua executando
                    System.Diagnostics.Debug.WriteLine($"Erro na coleta de alta frequência: {ex.Message}");
                }
                finally
                {
                    VoltrisOptimizer.Services.Diagnostics.ThreadNameRegistry.Unregister();
                }

                // Aguarda o intervalo de coleta
                await Task.Delay(CollectionIntervalMs, cancellationToken);
            }
        }

        /// <summary>
        /// Adiciona um evento ao buffer com controle de tamanho.
        /// </summary>
        /// <param name="evt">Evento a ser adicionado.</param>
        /// <returns>Tarefa assíncrona.</returns>
        private async Task AddEventToBufferAsync(EtwEvent evt)
        {
            await _bufferSemaphore.WaitAsync();
            try
            {
                // Se o buffer está cheio, remove o evento mais antigo
                if (_eventBuffer.Count >= MaxBufferSize)
                {
                    _eventBuffer.TryDequeue(out _);
                }
                
                // Adiciona o novo evento
                _eventBuffer.Enqueue(evt);
            }
            finally
            {
                _bufferSemaphore.Release();
            }
        }

        /// <summary>
        /// Verifica se é necessário fazer flush do buffer.
        /// </summary>
        /// <returns>Tarefa assíncrona.</returns>
        private async Task CheckBufferFlushAsync()
        {
            await _bufferSemaphore.WaitAsync();
            try
            {
                if (_eventBuffer.Count >= FlushThresholdCount)
                {
                    OnBufferFlushThresholdReached(_eventBuffer.Count);
                }
            }
            finally
            {
                _bufferSemaphore.Release();
            }
        }

        /// <summary>
        /// Dispara o evento de limite de buffer atingido.
        /// </summary>
        /// <param name="eventCount">Número de eventos no buffer.</param>
        protected virtual void OnBufferFlushThresholdReached(int eventCount)
        {
            BufferFlushThresholdReached?.Invoke(this, new EtwBufferFlushEventArgs(eventCount));
        }

        /// <summary>
        /// Dispara o evento de eventos coletados.
        /// </summary>
        /// <param name="count">Número de eventos coletados.</param>
        protected virtual void OnEventsCollected(int count)
        {
            EventsCollected?.Invoke(this, new EtwEventsCollectedEventArgs(
                new List<EtwEvent> { new EtwEvent { Message = $"Coletados {count} eventos" } }));
        }
    }

    /// <summary>
    /// Argumentos para evento de flush do buffer.
    /// </summary>
    public class EtwBufferFlushEventArgs : EventArgs
    {
        /// <summary>
        /// Número de eventos no buffer.
        /// </summary>
        public int EventCount { get; }

        /// <summary>
        /// Inicializa uma nova instância de <see cref="EtwBufferFlushEventArgs"/>.
        /// </summary>
        /// <param name="eventCount">Número de eventos no buffer.</param>
        public EtwBufferFlushEventArgs(int eventCount)
        {
            EventCount = eventCount;
        }
    }

    /// <summary>
    /// Compressor de eventos ETW para reduzir uso de memória.
    /// </summary>
    public class EtwEventCompressor
    {
        /// <summary>
        /// Comprime uma lista de eventos.
        /// </summary>
        /// <param name="events">Eventos a serem comprimidos.</param>
        /// <returns>Eventos comprimidos.</returns>
        public List<CompressedEtwEvent> CompressEvents(List<EtwEvent> events)
        {
            var compressedEvents = new List<CompressedEtwEvent>();
            
            foreach (var evt in events)
            {
                compressedEvents.Add(new CompressedEtwEvent
                {
                    Id = evt.EventId,
                    Provider = CompressString(evt.ProviderName),
                    Level = (byte)evt.Level,
                    Timestamp = evt.Timestamp.ToBinary(),
                    Message = CompressString(evt.Message),
                    ProcessId = evt.ProcessId,
                    ThreadId = evt.ThreadId
                    // Payload seria comprimido separadamente em uma implementação real
                });
            }
            
            return compressedEvents;
        }

        /// <summary>
        /// Descomprime uma lista de eventos.
        /// </summary>
        /// <param name="compressedEvents">Eventos comprimidos.</param>
        /// <returns>Eventos descomprimidos.</returns>
        public List<EtwEvent> DecompressEvents(List<CompressedEtwEvent> compressedEvents)
        {
            var events = new List<EtwEvent>();
            
            foreach (var compressed in compressedEvents)
            {
                events.Add(new EtwEvent
                {
                    EventId = compressed.Id,
                    ProviderName = DecompressString(compressed.Provider),
                    Level = (EtwEventLevel)compressed.Level,
                    Timestamp = DateTime.FromBinary(compressed.Timestamp),
                    Message = DecompressString(compressed.Message),
                    ProcessId = compressed.ProcessId,
                    ThreadId = compressed.ThreadId
                });
            }
            
            return events;
        }

        /// <summary>
        /// Comprime uma string.
        /// </summary>
        /// <param name="input">String a ser comprimida.</param>
        /// <returns>String comprimida.</returns>
        private string CompressString(string input)
        {
            // Em uma implementação real, usaria algoritmos de compressão como GZip
            // Por simplicidade, estamos apenas retornando a string original
            return input ?? string.Empty;
        }

        /// <summary>
        /// Descomprime uma string.
        /// </summary>
        /// <param name="input">String a ser descomprimida.</param>
        /// <returns>String descomprimida.</returns>
        private string DecompressString(string input)
        {
            // Em uma implementação real, descomprimiria a string
            return input ?? string.Empty;
        }
    }

    /// <summary>
    /// Evento ETW comprimido.
    /// </summary>
    public class CompressedEtwEvent
    {
        /// <summary>
        /// ID do evento.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Provedor (comprimido).
        /// </summary>
        public string Provider { get; set; }

        /// <summary>
        /// Nível do evento.
        /// </summary>
        public byte Level { get; set; }

        /// <summary>
        /// Timestamp do evento.
        /// </summary>
        public long Timestamp { get; set; }

        /// <summary>
        /// Mensagem (comprimida).
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// ID do processo.
        /// </summary>
        public int ProcessId { get; set; }

        /// <summary>
        /// ID da thread.
        /// </summary>
        public int ThreadId { get; set; }
    }
}
