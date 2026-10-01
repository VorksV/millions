using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.RateLimiting
{
    /// <summary>
    /// Rate limiter global para prevenir execuções concorrentes e abuso
    /// CORREÇÃO ENTERPRISE: Proteção contra race conditions
    /// </summary>
    public class GlobalRateLimiter
    {
        private static readonly Lazy<GlobalRateLimiter> _instance = new(() => new GlobalRateLimiter());
        public static GlobalRateLimiter Instance => _instance.Value;
        
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
        private readonly ConcurrentDictionary<string, DateTime> _lastExecutions = new();
        private readonly ConcurrentDictionary<string, int> _executionCounts = new();
        
        private GlobalRateLimiter() { }
        
        /// <summary>
        /// Executa operação com rate limiting
        /// </summary>
        public async Task<T> ExecuteAsync<T>(
            string operationKey,
            Func<Task<T>> operation,
            TimeSpan? minInterval = null,
            int maxConcurrent = 1,
            CancellationToken ct = default)
        {
            // Obter ou criar lock para esta operação
            var semaphore = _locks.GetOrAdd(operationKey, _ => new SemaphoreSlim(maxConcurrent, maxConcurrent));
            
            // Verificar intervalo mínimo
            if (minInterval.HasValue)
            {
                if (_lastExecutions.TryGetValue(operationKey, out var lastExec))
                {
                    var elapsed = DateTime.UtcNow - lastExec;
                    if (elapsed < minInterval.Value)
                    {
                        var waitTime = minInterval.Value - elapsed;
                        throw new RateLimitException($"Operação '{operationKey}' executada muito rapidamente. Aguarde {waitTime.TotalSeconds:F1}s");
                    }
                }
            }
            
            // Tentar adquirir lock
            if (!await semaphore.WaitAsync(0, ct))
            {
                throw new RateLimitException($"Operação '{operationKey}' já está em execução");
            }
            
            try
            {
                // Atualizar contadores
                _lastExecutions[operationKey] = DateTime.UtcNow;
                _executionCounts.AddOrUpdate(operationKey, 1, (_, count) => count + 1);
                
                // Executar operação
                return await operation();
            }
            finally
            {
                semaphore.Release();
            }
        }
        
        /// <summary>
        /// Verifica se operação pode ser executada
        /// </summary>
        public bool CanExecute(string operationKey, TimeSpan? minInterval = null)
        {
            // Verificar se já está em execução
            if (_locks.TryGetValue(operationKey, out var semaphore))
            {
                if (semaphore.CurrentCount == 0)
                    return false;
            }
            
            // Verificar intervalo mínimo
            if (minInterval.HasValue && _lastExecutions.TryGetValue(operationKey, out var lastExec))
            {
                var elapsed = DateTime.UtcNow - lastExec;
                return elapsed >= minInterval.Value;
            }
            
            return true;
        }
        
        /// <summary>
        /// Obtém estatísticas de execução
        /// </summary>
        public RateLimitStats GetStats(string operationKey)
        {
            _executionCounts.TryGetValue(operationKey, out var count);
            _lastExecutions.TryGetValue(operationKey, out var lastExec);
            
            return new RateLimitStats
            {
                OperationKey = operationKey,
                TotalExecutions = count,
                LastExecution = lastExec,
                IsCurrentlyExecuting = _locks.TryGetValue(operationKey, out var sem) && sem.CurrentCount == 0
            };
        }
        
        /// <summary>
        /// Reseta contadores (para testes)
        /// </summary>
        public void Reset(string operationKey)
        {
            _lastExecutions.TryRemove(operationKey, out _);
            _executionCounts.TryRemove(operationKey, out _);
        }
    }
    
    public class RateLimitException : Exception
    {
        public RateLimitException(string message) : base(message) { }
    }
    
    public class RateLimitStats
    {
        public string OperationKey { get; set; } = "";
        public int TotalExecutions { get; set; }
        public DateTime LastExecution { get; set; }
        public bool IsCurrentlyExecuting { get; set; }
    }
}
