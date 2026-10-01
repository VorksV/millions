using System;
using System.Runtime.CompilerServices;

namespace VoltrisOptimizer.Services
{
    /// <summary>
    /// Extensões para ILoggingService que suprimem logs de debug em produção.
    /// Substitui os "[DEBUG MÁXIMO]" que poluem logs de produção.
    /// </summary>
    public static class LoggingExtensions
    {
        /// <summary>
        /// Log apenas em builds DEBUG. Em Release, é um no-op.
        /// Usar em vez de LogInfo para mensagens de diagnóstico de desenvolvimento.
        /// </summary>
        [System.Diagnostics.Conditional("DEBUG")]
        public static void LogDebugOnly(this ILoggingService logger, string message,
            [CallerMemberName] string? caller = null)
        {
#if DEBUG
            logger.LogInfo($"[DEV:{caller}] {message}");
#endif
        }

        /// <summary>
        /// Log de startup estruturado: [Startup] [OK] StepName — Xms
        /// </summary>
        public static void LogStartupStep(this ILoggingService logger, string step, long ms, bool success = true)
        {
            var icon = success ? "[OK]" : "[FAIL]";
            var perf = ms > 1000 ? " [WARN] LENTO" : ms > 500 ? " [SLOW]" : "";
            logger.LogInfo($"[Startup] {icon} {step} — {ms}ms{perf}");
        }
    }

    /// <summary>
    /// Formatação de cadeia de exceções para diagnóstico.
    /// WPF (XamlReader.RewrapException), WMI (ManagementException) e System.Management
    /// embrulham a exceção real em wrappers; registrar apenas ex.Message perde a causa raiz.
    /// </summary>
    public static class ExceptionDiagnostics
    {
        /// <summary>
        /// Devolve a cadeia completa "Tipo: mensagem (origem: TipoAnterior)" + stack do frame mais externo.
        /// </summary>
        public static string Describe(this Exception? ex, int maxDepth = 8)
        {
            if (ex == null) return "(sem exceção)";

            var chain = new System.Collections.Generic.List<string>();
            var current = ex;
            int depth = 0;
            while (current != null && depth < maxDepth)
            {
                string origin = chain.Count == 0 ? string.Empty : $" <- {chain[chain.Count - 1].Split(':')[0]}";
                chain.Add($"{current.GetType().FullName}: {current.Message}{origin}");
                current = current.InnerException;
                depth++;
            }

            string header = string.Join(" || ", chain);
            string stack = ex.StackTrace ?? string.Empty;
            return string.IsNullOrEmpty(stack) ? header : $"{header}{Environment.NewLine}{stack}";
        }

        /// <summary>
        /// Lista as propriedades relevantes de uma exceção WMI (código e Parameters), útil quando
        /// a mensagem é genérica ("Classe inválida", "Parâmetro inválido").
        /// </summary>
        public static string DescribeWmi(this Exception? ex)
        {
            if (ex is not System.Management.ManagementException mgmt)
                return Describe(ex);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(Describe(mgmt));
            try
            {
                var codeProp = mgmt.GetType().GetProperty("ErrorCode");
                object? code = codeProp?.GetValue(mgmt);
                sb.AppendLine($"    WMI ErrorCode: {code}");

                var paramsProp = mgmt.GetType().GetProperty("Parameters");
                if (paramsProp?.GetValue(mgmt) is System.Collections.IEnumerable parameters)
                {
                    foreach (var p in parameters)
                        sb.AppendLine($"    WMI Parameter: {p}");
                }

                var queryProp = mgmt.GetType().GetProperty("Query");
                sb.AppendLine($"    WMI Query: {queryProp?.GetValue(mgmt)}");
            }
            catch
            {
                // Diagnóstico é best-effort; nunca deve mascarar a exceção original.
            }
            return sb.ToString().TrimEnd();
        }
    }
}
