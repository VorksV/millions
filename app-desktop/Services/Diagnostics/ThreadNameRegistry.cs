using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace VoltrisOptimizer.Services.Diagnostics
{
    /// <summary>
    /// Registro global de nomes de threads gerenciados → OS thread ID.
    /// 
    /// Cada serviço que cria um background loop deve chamar:
    ///   ThreadNameRegistry.Register("NomeDoServico");
    /// 
    /// Isso permite que o CpuSelfProfiler identifique threads por nome no Telegram.
    /// Custo: ~1 dicionário lookup por thread, zero overhead em idle.
    /// </summary>
    public static class ThreadNameRegistry
    {
        // OS Thread ID → nome do serviço
        private static readonly ConcurrentDictionary<int, string> _map = new();

        /// <summary>
        /// Registra o thread atual com um nome. Chamar no início de cada Task.Run/background loop.
        /// </summary>
        public static void Register(string name)
        {
            int osId = GetCurrentThreadId();
            _map[osId] = name;
        }

        /// <summary>
        /// Remove o thread atual do registro (chamar em finally/OnExit).
        /// </summary>
        public static void Unregister()
        {
            _map.TryRemove(GetCurrentThreadId(), out _);
        }

        /// <summary>
        /// Retorna o nome de uma thread pelo ID do OS.
        /// </summary>
        public static string? GetThreadName(int osId)
        {
            return _map.TryGetValue(osId, out var name) ? name : null;
        }

        /// <summary>
        /// Retorna snapshot do mapa atual (OS thread ID → nome).
        /// </summary>
        public static Dictionary<int, string> GetSnapshot()
        {
            return new Dictionary<int, string>(_map);
        }

        [DllImport("kernel32.dll")]
        private static extern int GetCurrentThreadId();
    }
}
