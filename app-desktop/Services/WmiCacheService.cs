using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services
{
    /// <summary>
    /// Cache genérico para WMI queries com TTL
    /// CRÍTICO: WMI queries são LENTAS (50-200ms) e bloqueiam threads
    /// </summary>
    public class WmiCacheService
    {
        private readonly ConcurrentDictionary<string, CachedValue> _cache = new();
        private readonly ILoggingService? _logger;

        public WmiCacheService(ILoggingService? logger = null)
        {
            _logger = logger;
        }

        /// <summary>
        /// Obtém valor do cache ou executa factory se expirado
        /// </summary>
        public T GetOrUpdate<T>(string key, Func<T> factory, TimeSpan ttl)
        {
            if (_cache.TryGetValue(key, out var cached))
            {
                if (DateTime.Now - cached.Timestamp < ttl)
                {
                    return (T)cached.Value;
                }
            }

            // Cache expirado ou não existe - executar factory
            var value = factory();
            _cache[key] = new CachedValue
            {
                Value = value!,
                Timestamp = DateTime.Now
            };

            return value;
        }

        /// <summary>
        /// Versão async do GetOrUpdate
        /// </summary>
        public async Task<T> GetOrUpdateAsync<T>(string key, Func<Task<T>> factory, TimeSpan ttl)
        {
            if (_cache.TryGetValue(key, out var cached))
            {
                if (DateTime.Now - cached.Timestamp < ttl)
                {
                    return (T)cached.Value;
                }
            }

            // Cache expirado ou não existe - executar factory
            var value = await factory().ConfigureAwait(false);
            _cache[key] = new CachedValue
            {
                Value = value!,
                Timestamp = DateTime.Now
            };

            return value;
        }

        /// <summary>
        /// Invalida cache para uma chave específica
        /// </summary>
        public void Invalidate(string key)
        {
            _cache.TryRemove(key, out _);
        }

        /// <summary>
        /// Limpa todo o cache
        /// </summary>
        public void Clear()
        {
            _cache.Clear();
            _logger?.LogInfo("[WmiCache] Cache limpo");
        }

        private class CachedValue
        {
            public object Value { get; set; } = null!;
            public DateTime Timestamp { get; set; }
        }
    }
}
