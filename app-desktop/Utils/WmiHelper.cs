using System;
using System.Collections.Generic;
using System.Management;
using System.Threading.Tasks;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Security;
using System.Threading;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Utils
{
    /// <summary>
    /// Versão blindada de objeto WMI que não mantém conexão viva
    /// </summary>
    public class WmiObject
    {
        private readonly Dictionary<string, object?> _properties = new(StringComparer.OrdinalIgnoreCase);

        public WmiObject(ManagementObject source)
        {
            if (source == null) return;
            
            try
            {
                // Tenta capturar todas as propriedades enquanto o objeto está vivo
                foreach (PropertyData prop in source.Properties)
                {
                    try
                    {
                        if (prop != null)
                        {
                            _properties[prop.Name] = prop.Value;
                        }
                    }
                    catch (Exception ex)
                    {
                        // Propriedade isolada com Access Denied ou Handle Inválido pelo driver WMI subjacente
                        App.LoggingService?.LogDebug($"[WmiHelper] Leitura ignorada para propriedade de driver restrita. {ex.Message}");
                    }
                }
            }
            catch (ManagementException mEx)
            {
                App.LoggingService?.LogWarning($"[WmiHelper] Handle Management inválido ao tentar iterar coleções da classe COM. WMI Namespace pode estar corrompido: {mEx.Message}");
            }
            catch (Exception ex)
            {
                // Este item base WMI quebrou a leitura interativa. Retornamos estrutura vazia mas emitimos Warning no Log.
                App.LoggingService?.LogWarning($"[WmiHelper] Erro inesperado conectando-se à coleção de instâncias da BIOS/Placa Mãe: {ex.Message}");
            }
        }

        public object? this[string propertyName]
        {
            get
            {
                _properties.TryGetValue(propertyName, out var value);
                return value;
            }
        }

        public IEnumerable<string> PropertyNames => _properties.Keys;
        
        public T GetValue<T>(string propertyName, T defaultValue = default)
        {
            var val = this[propertyName];
            if (val == null) return defaultValue;
            try
            {
                return (T)Convert.ChangeType(val, typeof(T));
            }
            catch (InvalidCastException)
            {
                // LogDebug utilizado porque conversões de valores vagos do WMI são esperadas sem falhar o sistema
                App.LoggingService?.LogDebug($"[WmiHelper] Conversão silenciosa WMI falhou para propriedade ({propertyName}) do tipo {typeof(T).Name}.");
                return defaultValue;
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[WmiHelper] Leitura abortada ao extrair a propriedade '{propertyName}' devido a erro complexo de conversão: {ex.Message}");
                return defaultValue;
            }
        }
    }

    /// <summary>
    /// Helper robusto para consultas WMI com proteção contra travamentos (Hang Protection)
    /// </summary>
    public static class WmiHelper
    {
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
        private static readonly SemaphoreSlim _globalWmiLock = new(1, 1);
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _blacklistedQueries = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Executa uma query WMI de forma segura com timeout e snapshot imediato.
        /// Protege contra COM Access Violations e ObjectDisposedException.
        /// </summary>
        [HandleProcessCorruptedStateExceptions]
        public static async Task<IEnumerable<WmiObject>> QuerySafeAsync(string query, string scope = "root\\CIMV2", TimeSpan? timeout = null)
        {
            var cacheKey = $"{scope}::{query}";
            if (_blacklistedQueries.ContainsKey(cacheKey))
            {
                return Enumerable.Empty<WmiObject>();
            }

            var actualTimeout = timeout ?? DefaultTimeout;
            
            try
            {
                // Serialização global de WMI para evitar conflitos de driver/COM
                var task = Task.Run(async () =>
                {
                    if (!await _globalWmiLock.WaitAsync(actualTimeout).ConfigureAwait(false))
                    {
                        var msg = $"[WMI Global Lock Timeout] Query: {query} após {actualTimeout.TotalSeconds}s";
                        App.LoggingService?.LogWarning(msg);
                        System.Diagnostics.Debug.WriteLine(msg);
                        return new List<WmiObject>();
                    }

                    try
                    {
                        App.LoggingService?.LogDebug($"[WMI-LOCK] Acquired for: {query}");
                        var results = new List<WmiObject>();
                        try
                        {
                            using var searcher = new ManagementObjectSearcher(scope, query);
                            searcher.Options.ReturnImmediately = true;
                            searcher.Options.Rewindable = false;
                            
                            using var collection = searcher.Get();
                            foreach (ManagementObject obj in collection)
                            {
                                try
                                {
                                    results.Add(new WmiObject(obj));
                                }
                                finally
                                {
                                    try 
                                    { 
                                        obj.Dispose(); 
                                    } 
                                    catch (Exception ex) 
                                    { 
                                        App.LoggingService?.LogWarning($"[WmiHelper] Leak Evitado: Dispose do Objeto Nativo WMI COM falhou no loop: {ex.Message}"); 
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            // Erro "InvalidNamespace" ou "InvalidClass" é perfeitamente normal se o hardware ou driver atrelado à query não existir no PC do usuário.
                            if (ex is ManagementException mEx && (mEx.ErrorCode == ManagementStatus.InvalidNamespace || mEx.ErrorCode == ManagementStatus.InvalidClass))
                            {
                                _blacklistedQueries.TryAdd(cacheKey, true);
                                App.LoggingService?.LogDebug($"[WMI Info] Namespace ou Classe ausente para: {query}. (Driver ou hardware não detectado). Query inserida na BLACKLIST.");
                            }
                            else if (ex is ManagementException invalidParam && invalidParam.ErrorCode == ManagementStatus.InvalidParameter)
                            {
                                // Query estruturalmente inválida (filtro/escopo malformado). Repetir a
                                // mesma consulta a cada ciclo apenas gera "Parâmetro inválido" em
                                // log. A query completa é registrada para diagnóstico.
                                _blacklistedQueries.TryAdd(cacheKey, true);
                                App.LoggingService?.LogWarning(
                                    $"[WMI Error] Query com parâmetro inválido — desativada após a primeira falha. " +
                                    $"Query: {query} | {ex.DescribeWmi()}");
                            }
                            else if (ex is ManagementException notSupportedEx && notSupportedEx.ErrorCode == ManagementStatus.NotSupported)
                            {
                                _blacklistedQueries.TryAdd(cacheKey, true);
                                // Temperatura via WMI é frequentemente indispon�vel - usar fallback do LibreHardwareMonitor
                                if (query.Contains("MSAcpi_ThermalZoneTemperature", StringComparison.OrdinalIgnoreCase))
                                {
                                    App.LoggingService?.LogDebug($"[WMI Info] Temperatura via WMI n�o suportada (comum em alguns hardwares). Usando fallback do LibreHardwareMonitor. Query: {query}");
                                }
                                else
                                {
                                    App.LoggingService?.LogDebug($"[WMI Info] Funcionalidade n�o suportada (Sem suporte) para: {query}. Query inserida na BLACKLIST.");
                                }
                            }
                            else if (ex.Message.Contains("Namespace", StringComparison.OrdinalIgnoreCase))
                            {
                                _blacklistedQueries.TryAdd(cacheKey, true);
                                App.LoggingService?.LogDebug($"[WMI Info] Namespace WMI não localizado para a query: {query}. ({ex.Message}). Query inserida na BLACKLIST.");
                            }
                            else
                            {
                                // Consultas de temperatura frequentemente falham - usar fallback do LibreHardwareMonitor
                                if (query.Contains("Temperature", StringComparison.OrdinalIgnoreCase) || 
                                    query.Contains("ThermalZone", StringComparison.OrdinalIgnoreCase))
                                {
                                    _blacklistedQueries.TryAdd(cacheKey, true);
                                    App.LoggingService?.LogDebug($"[WMI Info] Sensor de temperatura via WMI indispon�vel. Usando LibreHardwareMonitor como fallback. Query: {query}");
                                }
                                else
                                {
                                    App.LoggingService?.LogWarning($"[WMI Error] Query: {query} | Error: {ex.Message}");
                                }
                            }
                        }
                        return results;
                    }
                    finally
                    {
                        _globalWmiLock.Release();
                    }
                });

                // ✅ CORREÇÃO: Usar WaitAsync em vez de Wait() + Result e adicionar ConfigureAwait(false) para evitar deadlocks
                if (await Task.WhenAny(task, Task.Delay(actualTimeout)).ConfigureAwait(false) == task)
                {
                    return await task.ConfigureAwait(false);
                }

                App.LoggingService?.LogWarning($"[WMI Timeout] Query: {query} (Wait Limit {actualTimeout.TotalSeconds}s)");
            }
            catch (Exception ex)
            {
                if (ex is ManagementException mEx && (mEx.ErrorCode == ManagementStatus.InvalidNamespace || mEx.ErrorCode == ManagementStatus.InvalidClass))
                {
                    _blacklistedQueries.TryAdd(cacheKey, true);
                    App.LoggingService?.LogDebug($"[WMI Critical Info] Namespace ou Classe ausente para a consulta: {query}");
                }
                else if (ex.Message.Contains("Namespace", StringComparison.OrdinalIgnoreCase))
                {
                    _blacklistedQueries.TryAdd(cacheKey, true);
                    App.LoggingService?.LogDebug($"[WMI Critical Info] Namespace ausente: {query}. ({ex.Message})");
                }
                else
                {
                    App.LoggingService?.LogWarning($"[Wmi Critical Error] {query}: {ex.Message}");
                }
            }
            
            return Enumerable.Empty<WmiObject>();
        }

        /// <summary>
        /// Wrapper síncrono para QuerySafeAsync (para compatibilidade com código legado).
        /// ⚠️ AVISO: Este método bloqueia a thread chamadora. NUNCA chamar da UI thread.
        /// Use QuerySafeAsync diretamente em código novo.
        /// </summary>
        public static IEnumerable<WmiObject> QuerySafe(string query, string scope = "root\\CIMV2", TimeSpan? timeout = null)
        {
            // DETECÇÃO E CORREÇÃO AUTOMÁTICA: Se chamado na UI thread, redirecionar para Task.Run
            // para evitar freeze. O GetAwaiter().GetResult() numa thread de pool sem
            // SynchronizationContext não causa deadlock.
            if (System.Windows.Application.Current?.Dispatcher?.CheckAccess() == true)
            {
                var msg = $"[WMI-CRITICAL] QuerySafe() chamado na UI thread! Query='{query}' — redirecionando para Task.Run para evitar freeze.";
                App.LoggingService?.LogError(msg);
                System.Diagnostics.Debug.WriteLine(msg);
                try { VoltrisOptimizer.Helpers.VoltrisDiagnosticSystem.Instance.Timeline("WMI-UI-VIOLATION", msg); } catch { }

                // Redirecionar para fora do SynchronizationContext da UI para evitar deadlock
                return Task.Run(() => QuerySafeAsync(query, scope, timeout)).GetAwaiter().GetResult();
            }

            return QuerySafeAsync(query, scope, timeout).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Retorna o primeiro resultado de uma query de forma segura.
        /// </summary>
        public static WmiObject? QueryFirstSafe(string query, string scope = "root\\CIMV2", TimeSpan? timeout = null)
        {
            return QuerySafe(query, scope, timeout).FirstOrDefault();
        }

        /// <summary>
        /// Helper para facilitar a transição de código antigo
        /// </summary>
        public static T? GetPropertySafe<T>(this WmiObject obj, string propertyName)
        {
            return obj.GetValue<T>(propertyName);
        }
    }
}
