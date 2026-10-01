using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Prediction
{
    /// <summary>
    /// Prefetcher de I/O que antecipa e carrega recursos para reduzir latência.
    /// </summary>
    public class IOPrefetcher
    {
        private readonly ISystemInfoService _systemInfoService;
        private readonly List<string> _monitoredPaths;
        private readonly Dictionary<string, FileAccessPattern> _accessPatterns;
        private readonly object _lockObject = new object();
        
        private CancellationTokenSource _cancellationTokenSource;
        private Task _prefetchTask;
        private bool _isRunning = false;
        
        private const int PrefetchIntervalMs = 5000;
        private const int MaxPrefetchQueueSize = 50;

        /// <summary>
        /// Limiar de atividade de I/O para acionar prefetch.
        /// </summary>
        public int IoActivityThreshold { get; set; } = 1000;

        /// <summary>
        /// Tamanho máximo do cache de prefetch em MB.
        /// </summary>
        public int MaxCacheSizeMB { get; set; } = 512;

        /// <summary>
        /// Inicializa uma nova instância de <see cref="IOPrefetcher"/>.
        /// </summary>
        /// <param name="systemInfoService">Serviço de informações do sistema.</param>
        public IOPrefetcher(ISystemInfoService systemInfoService)
        {
            _systemInfoService = systemInfoService ?? throw new ArgumentNullException(nameof(systemInfoService));
            _monitoredPaths = new List<string>();
            _accessPatterns = new Dictionary<string, FileAccessPattern>();
        }

        /// <summary>
        /// Inicia o prefetcher.
        /// </summary>
        /// <returns>Tarefa assíncrona.</returns>
        public async Task StartAsync()
        {
            lock (_lockObject)
            {
                if (_isRunning)
                    return;

                _isRunning = true;
                _cancellationTokenSource = new CancellationTokenSource();
            }

            // Inicia o loop de prefetch
            _prefetchTask = PrefetchLoopAsync(_cancellationTokenSource.Token);
            
            await Task.CompletedTask;
        }

        /// <summary>
        /// Para o prefetcher.
        /// </summary>
        /// <returns>Tarefa assíncrona.</returns>
        public async Task StopAsync()
        {
            lock (_lockObject)
            {
                if (!_isRunning)
                    return;

                _isRunning = false;
                _cancellationTokenSource?.Cancel();
            }

            // Aguarda o término do loop de prefetch
            if (_prefetchTask != null)
            {
                try
                {
                    await _prefetchTask;
                }
                catch (OperationCanceledException)
                {
                    // Esperado quando cancelamos a tarefa
                }
            }
            
            // Limpa o cache
            ClearPrefetchCache();
            
            await Task.CompletedTask;
        }

        /// <summary>
        /// Adiciona um caminho para monitoramento e prefetch.
        /// </summary>
        /// <param name="path">Caminho a ser monitorado.</param>
        public void AddMonitoredPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("O caminho não pode ser vazio.", nameof(path));

            lock (_lockObject)
            {
                if (!_monitoredPaths.Contains(path))
                {
                    _monitoredPaths.Add(path);
                }
            }
        }

        /// <summary>
        /// Remove um caminho do monitoramento.
        /// </summary>
        /// <param name="path">Caminho a ser removido.</param>
        public void RemoveMonitoredPath(string path)
        {
            lock (_lockObject)
            {
                _monitoredPaths.Remove(path);
                _accessPatterns.Remove(path);
            }
        }

        /// <summary>
        /// Realiza prefetch de recursos com base nos padrões identificados.
        /// </summary>
        /// <returns>Tarefa assíncrona.</returns>
        public async Task PrefetchAsync()
        {
            var candidates = IdentifyPrefetchCandidates();
            await ExecutePrefetchAsync(candidates);
        }

        /// <summary>
        /// Loop principal de prefetch.
        /// </summary>
        /// <param name="cancellationToken">Token de cancelamento.</param>
        /// <returns>Tarefa assíncrona.</returns>
        private async Task PrefetchLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _isRunning)
            {
                try
                {
                    // Verifica se há atividade de I/O alta que justifique prefetch
                    var ioActivity = await _systemInfoService.GetIoActivityAsync();
                    // ioActivity é um double representando MB/s
                    if (ioActivity > IoActivityThreshold)
                    {
                        await PrefetchAsync();
                    }
                    
                    // Aguarda o intervalo definido
                    await Task.Delay(PrefetchIntervalMs, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Tarefa cancelada, sair do loop
                    break;
                }
                catch (Exception ex)
                {
                    // Log de erro mas continua executando
                    System.Diagnostics.Debug.WriteLine($"Erro no loop de prefetch: {ex.Message}");
                    
                    // Aguarda mesmo em caso de erro para evitar loops muito rápidos
                    try
                    {
                        await Task.Delay(PrefetchIntervalMs, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Identifica candidatos para prefetch com base em padrões de acesso.
        /// </summary>
        /// <returns>Lista de candidatos para prefetch.</returns>
        private List<PrefetchCandidate> IdentifyPrefetchCandidates()
        {
            var candidates = new List<PrefetchCandidate>();
            
            lock (_lockObject)
            {
                foreach (var path in _monitoredPaths)
                {
                    // Verifica se temos padrões de acesso para este caminho
                    if (_accessPatterns.TryGetValue(path, out var pattern))
                    {
                        // Identifica arquivos que provavelmente serão acessados em breve
                        var likelyFiles = PredictNextAccesses(pattern, path);
                        candidates.AddRange(likelyFiles);
                    }
                    else
                    {
                        // Se não temos padrões ainda, adiciona arquivos do diretório como candidatos
                        var initialCandidates = GetInitialCandidates(path);
                        candidates.AddRange(initialCandidates);
                    }
                }
            }
            
            // Limita o número de candidatos
            if (candidates.Count > MaxPrefetchQueueSize)
            {
                candidates.Sort((a, b) => b.Priority.CompareTo(a.Priority));
                candidates.RemoveRange(MaxPrefetchQueueSize, candidates.Count - MaxPrefetchQueueSize);
            }
            
            return candidates;
        }

        /// <summary>
        /// Prevê quais arquivos serão acessados em seguida.
        /// </summary>
        /// <param name="pattern">Padrão de acesso.</param>
        /// <param name="basePath">Caminho base.</param>
        /// <returns>Lista de candidatos para prefetch.</returns>
        private List<PrefetchCandidate> PredictNextAccesses(FileAccessPattern pattern, string basePath)
        {
            var candidates = new List<PrefetchCandidate>();
            
            // Prevê com base na sequência histórica de acessos
            foreach (var nextFile in pattern.PredictNextFiles())
            {
                var fullPath = Path.Combine(basePath, nextFile);
                if (File.Exists(fullPath))
                {
                    candidates.Add(new PrefetchCandidate
                    {
                        FilePath = fullPath,
                        Priority = CalculatePrefetchPriority(fullPath, pattern),
                        EstimatedAccessTimeMs = pattern.EstimateNextAccessTime(nextFile)
                    });
                }
            }
            
            return candidates;
        }

        /// <summary>
        /// Obtém candidatos iniciais para prefetch.
        /// </summary>
        /// <param name="path">Caminho base.</param>
        /// <returns>Lista de candidatos iniciais.</returns>
        private List<PrefetchCandidate> GetInitialCandidates(string path)
        {
            var candidates = new List<PrefetchCandidate>();
            
            try
            {
                if (Directory.Exists(path))
                {
                    var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        // Prioriza arquivos maiores e mais recentemente modificados
                        var info = new FileInfo(file);
                        if (info.Length > 1024 * 1024) // Arquivos maiores que 1MB
                        {
                            candidates.Add(new PrefetchCandidate
                            {
                                FilePath = file,
                                Priority = CalculateInitialPriority(info),
                                EstimatedAccessTimeMs = 1000 // Estimativa padrão
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erro ao obter candidatos iniciais: {ex.Message}");
            }
            
            return candidates;
        }

        /// <summary>
        /// Calcula a prioridade inicial para um arquivo.
        /// </summary>
        /// <param name="fileInfo">Informações do arquivo.</param>
        /// <returns>Prioridade calculada.</returns>
        private int CalculateInitialPriority(FileInfo fileInfo)
        {
            // Baseia-se no tamanho e data de modificação
            var sizeScore = Math.Min(100, (int)(fileInfo.Length / (1024 * 1024))); // 1 ponto por MB, max 100
            var recencyScore = 0;
            
            var daysSinceModified = (DateTime.Now - fileInfo.LastWriteTime).Days;
            if (daysSinceModified < 1) recencyScore = 50;
            else if (daysSinceModified < 7) recencyScore = 30;
            else if (daysSinceModified < 30) recencyScore = 10;
            
            return sizeScore + recencyScore;
        }

        /// <summary>
        /// Calcula a prioridade de prefetch para um arquivo.
        /// </summary>
        /// <param name="filePath">Caminho do arquivo.</param>
        /// <param name="pattern">Padrão de acesso.</param>
        /// <returns>Prioridade calculada.</returns>
        private int CalculatePrefetchPriority(string filePath, FileAccessPattern pattern)
        {
            var fileInfo = new FileInfo(filePath);
            var basePriority = CalculateInitialPriority(fileInfo);
            
            // Ajusta com base na frequência de acesso
            var accessFrequency = pattern.GetAccessFrequency(Path.GetFileName(filePath));
            var frequencyBonus = Math.Min(50, accessFrequency * 10);
            
            return basePriority + frequencyBonus;
        }

        /// <summary>
        /// Executa o prefetch dos candidatos identificados.
        /// </summary>
        /// <param name="candidates">Candidatos para prefetch.</param>
        /// <returns>Tarefa assíncrona.</returns>
        private async Task ExecutePrefetchAsync(List<PrefetchCandidate> candidates)
        {
            foreach (var candidate in candidates)
            {
                try
                {
                    await PrefetchFileAsync(candidate.FilePath);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Erro ao fazer prefetch do arquivo {candidate.FilePath}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Realiza prefetch de um arquivo específico.
        /// </summary>
        /// <param name="filePath">Caminho do arquivo.</param>
        /// <returns>Tarefa assíncrona.</returns>
        private async Task PrefetchFileAsync(string filePath)
        {
            try
            {
                // Verifica se o arquivo existe
                if (!File.Exists(filePath))
                    return;
                
                // Verifica o tamanho do arquivo
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length > MaxCacheSizeMB * 1024 * 1024)
                    return; // Arquivo muito grande para prefetch
                
                // Lê o arquivo para carregá-lo no cache do sistema
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var buffer = new byte[64 * 1024]; // 64KB buffer
                    int bytesRead;
                    
                    // Lê o arquivo em blocos para simular acesso real
                    while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        // O simples ato de ler carrega o arquivo no cache do sistema
                        // Em uma implementação real, poderíamos usar APIs específicas de prefetch
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erro ao prefetchar arquivo {filePath}: {ex.Message}");
            }
        }

        /// <summary>
        /// Limpa o cache de prefetch.
        /// </summary>
        private void ClearPrefetchCache()
        {
            // Em uma implementação real, isso liberaria recursos do cache
            // Por enquanto, apenas limpa as estruturas de dados
            lock (_lockObject)
            {
                _accessPatterns.Clear();
            }
        }
    }

    /// <summary>
    /// Candidato para prefetch.
    /// </summary>
    public class PrefetchCandidate
    {
        /// <summary>
        /// Caminho completo do arquivo.
        /// </summary>
        public string FilePath { get; set; }

        /// <summary>
        /// Prioridade do prefetch (maior valor = maior prioridade).
        /// </summary>
        public int Priority { get; set; }

        /// <summary>
        /// Tempo estimado até o acesso em milissegundos.
        /// </summary>
        public int EstimatedAccessTimeMs { get; set; }
    }

    /// <summary>
    /// Padrão de acesso a arquivos.
    /// </summary>
    public class FileAccessPattern
    {
        private readonly Dictionary<string, int> _accessFrequency = new Dictionary<string, int>();
        private readonly List<string> _accessSequence = new List<string>();
        private readonly object _lockObject = new object();

        /// <summary>
        /// Registra um acesso a um arquivo.
        /// </summary>
        /// <param name="fileName">Nome do arquivo acessado.</param>
        public void RegisterAccess(string fileName)
        {
            lock (_lockObject)
            {
                // Atualiza a frequência de acesso
                if (_accessFrequency.ContainsKey(fileName))
                {
                    _accessFrequency[fileName]++;
                }
                else
                {
                    _accessFrequency[fileName] = 1;
                }
                
                // Adiciona ao histórico de sequência
                _accessSequence.Add(fileName);
                
                // Mantém o tamanho máximo da sequência
                if (_accessSequence.Count > 100)
                {
                    _accessSequence.RemoveAt(0);
                }
            }
        }

        /// <summary>
        /// Prevê quais arquivos serão acessados em seguida.
        /// </summary>
        /// <returns>Lista de nomes de arquivos previstos.</returns>
        public List<string> PredictNextFiles()
        {
            var predictions = new List<string>();
            
            lock (_lockObject)
            {
                if (_accessSequence.Count == 0)
                    return predictions;
                
                // Usa o padrão de Markov simples para prever o próximo arquivo
                var lastFile = _accessSequence[_accessSequence.Count - 1];
                
                // Encontra todas as transições deste arquivo
                var transitions = new Dictionary<string, int>();
                for (int i = 0; i < _accessSequence.Count - 1; i++)
                {
                    if (_accessSequence[i] == lastFile)
                    {
                        var nextFile = _accessSequence[i + 1];
                        if (transitions.ContainsKey(nextFile))
                        {
                            transitions[nextFile]++;
                        }
                        else
                        {
                            transitions[nextFile] = 1;
                        }
                    }
                }
                
                // Ordena por frequência e retorna os mais prováveis
                var sortedTransitions = new List<KeyValuePair<string, int>>(transitions);
                sortedTransitions.Sort((a, b) => b.Value.CompareTo(a.Value));
                
                // Retorna os 3 mais prováveis
                for (int i = 0; i < Math.Min(3, sortedTransitions.Count); i++)
                {
                    predictions.Add(sortedTransitions[i].Key);
                }
            }
            
            return predictions;
        }

        /// <summary>
        /// Obtém a frequência de acesso de um arquivo.
        /// </summary>
        /// <param name="fileName">Nome do arquivo.</param>
        /// <returns>Frequência de acesso.</returns>
        public int GetAccessFrequency(string fileName)
        {
            lock (_lockObject)
            {
                return _accessFrequency.TryGetValue(fileName, out var frequency) ? frequency : 0;
            }
        }

        /// <summary>
        /// Estima o tempo até o próximo acesso a um arquivo.
        /// </summary>
        /// <param name="fileName">Nome do arquivo.</param>
        /// <returns>Tempo estimado em milissegundos.</returns>
        public int EstimateNextAccessTime(string fileName)
        {
            lock (_lockObject)
            {
                // Calcula a média de tempo entre acessos a este arquivo
                var accessTimes = new List<int>();
                var lastAccessIndex = -1;
                
                for (int i = 0; i < _accessSequence.Count; i++)
                {
                    if (_accessSequence[i] == fileName)
                    {
                        if (lastAccessIndex >= 0)
                        {
                            // Calcula o tempo entre acessos (assumindo intervalo fixo para simplificação)
                            accessTimes.Add((i - lastAccessIndex) * 100); // 100ms entre registros
                        }
                        lastAccessIndex = i;
                    }
                }
                
                if (accessTimes.Count == 0)
                    return 1000; // Valor padrão
                
                // Calcula a média
                int sum = 0;
                foreach (var time in accessTimes)
                {
                    sum += time;
                }
                
                return sum / accessTimes.Count;
            }
        }
    }
}
