using System;
using System.Diagnostics;

namespace VoltrisOptimizer.Utils
{
    /// <summary>
    /// Wrapper para PerformanceCounter que suprime falhas causadas por corrupção no registro do Windows do usuário.
    /// Retorna 0 ao invés de quebrar a aplicação caso a telemetria do SO esteja corrompida.
    /// </summary>
    public class SafePerformanceCounter : IDisposable
    {
        private PerformanceCounter _counter;

        public SafePerformanceCounter(string categoryName, string counterName, string instanceName = null, bool readOnly = true)
        {
            try
            {
                if (instanceName != null)
                    _counter = new PerformanceCounter(categoryName, counterName, instanceName, readOnly);
                else
                    _counter = new PerformanceCounter(categoryName, counterName, readOnly);
            }
            catch
            {
                _counter = null;
            }
        }

        /// <summary>
        /// Lê o contador.
        ///
        /// CORREÇÃO DE AUDITORIA: a versão anterior devolvia <c>0f</c> em QUALQUER
        /// falha, inclusive quando o contador não existe na máquina. Esse 0 era
        /// indistinguível de uma leitura real de zero, e o consumidor o exibia
        /// diretamente (ex.: "DPC 0.0%", "Lat 0 ms", "PF 0/s"). Uma máquina sem
        /// contador de DPC aparecia como tendo 0% de DPC — um dado inventado.
        ///
        /// Agora a falha é sinalizada por <c>float.NaN</c> e
        /// <see cref="LastReadFailed"/>, permitindo que a interface exiba "N/D".
        /// </summary>
        public float NextValue()
        {
            try
            {
                if (_counter == null)
                {
                    LastReadFailed = true;
                    return float.NaN;
                }

                float value = _counter.NextValue();
                LastReadFailed = float.IsNaN(value) || float.IsInfinity(value);
                return LastReadFailed ? float.NaN : value;
            }
            catch (Exception ex)
            {
                LastReadFailed = true;
                LastError = ex.Message;
                return float.NaN;
            }
        }

        /// <summary>Verdadeiro quando a última leitura não pôde ser obtida.</summary>
        public bool LastReadFailed { get; private set; }

        /// <summary>Motivo da última falha de leitura, quando houver.</summary>
        public string? LastError { get; private set; }

        public string CounterName
        {
            get
            {
                try { return _counter?.CounterName ?? string.Empty; }
                catch { return string.Empty; }
            }
        }

        public string InstanceName
        {
            get
            {
                try { return _counter?.InstanceName ?? string.Empty; }
                catch { return string.Empty; }
            }
        }

        public void Dispose()
        {
            try
            {
                if (_counter != null)
                {
                    _counter.Dispose();
                    _counter = null;
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// Wrapper seguro para PerformanceCounterCategory.
    /// </summary>
    public class SafePerformanceCounterCategory
    {
        private PerformanceCounterCategory _category;

        public SafePerformanceCounterCategory(string categoryName)
        {
            try
            {
                _category = new PerformanceCounterCategory(categoryName);
            }
            catch
            {
                _category = null;
            }
        }

        public static bool Exists(string categoryName)
        {
            try
            {
                return PerformanceCounterCategory.Exists(categoryName);
            }
            catch
            {
                return false;
            }
        }

        public bool InstanceExists(string instanceName)
        {
            try
            {
                return _category != null && _category.InstanceExists(instanceName);
            }
            catch
            {
                return false;
            }
        }

        public string[] GetInstanceNames()
        {
            try
            {
                return _category != null ? _category.GetInstanceNames() : Array.Empty<string>();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        public SafePerformanceCounter[] GetCounters(string instanceName)
        {
            try
            {
                if (_category == null) return Array.Empty<SafePerformanceCounter>();
                var counters = _category.GetCounters(instanceName);
                var safeCounters = new SafePerformanceCounter[counters.Length];
                for (int i = 0; i < counters.Length; i++)
                {
                    safeCounters[i] = new SafePerformanceCounter(_category.CategoryName, counters[i].CounterName, instanceName, true);
                }
                return safeCounters;
            }
            catch
            {
                return Array.Empty<SafePerformanceCounter>();
            }
        }

        public SafePerformanceCounter[] GetCounters()
        {
            try
            {
                if (_category == null) return Array.Empty<SafePerformanceCounter>();
                var counters = _category.GetCounters();
                var safeCounters = new SafePerformanceCounter[counters.Length];
                for (int i = 0; i < counters.Length; i++)
                {
                    safeCounters[i] = new SafePerformanceCounter(_category.CategoryName, counters[i].CounterName, null, true);
                }
                return safeCounters;
            }
            catch
            {
                return Array.Empty<SafePerformanceCounter>();
            }
        }
    }
}
