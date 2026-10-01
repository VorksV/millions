using System;
using System.Diagnostics;
using System.Text;

namespace VoltrisOptimizer.Core.Logging
{
    /// <summary>
    /// Logger de alta performance para o VOLTRIS.
    /// Foca em zero alocação desnecessária e estrutura determinística (Antes/Depois/Métricas).
    /// </summary>
    public sealed class ProfessionalLogger
    {
        private static readonly Lazy<ProfessionalLogger> _instance = new(() => new ProfessionalLogger());
        public static ProfessionalLogger Instance => _instance.Value;

        // Idealmente injetado, mas singleton para uso global rápido
        private ProfessionalLogger() { }

        /// <summary>
        /// Registra uma transação de otimização completa.
        /// </summary>
        public void LogOptimizationTransaction(
            string engine,
            string feature,
            string stateBefore,
            string stateAfter,
            string metricsBefore,
            string metricsAfter,
            TimeSpan duration,
            bool success,
            bool rollbackExecuted,
            Exception? exception = null)
        {
            // Otimização: Uso de StringBuilder em thread-local ou alocação rápida
            // Para simplificar a implementação e garantir thread-safety, usaremos interpolação
            // No futuro, podemos usar ArrayPool para strings se o volume for altíssimo.
            
            var sb = new StringBuilder();
            sb.Append($"[{DateTime.UtcNow:O}] ");
            sb.Append($"[{(success ? "SUCCESS" : "FAILED ")}]");
            
            if (rollbackExecuted) sb.Append("[ROLLBACK] ");
            
            sb.Append($" Engine: {engine} |");
            sb.Append($" Feature: {feature} |");
            sb.Append($" State: '{stateBefore}' -> '{stateAfter}' |");
            sb.Append($" Metrics: '{metricsBefore}' -> '{metricsAfter}' |");
            sb.Append($" Duration: {duration.TotalMilliseconds:F2}ms");

            if (exception != null)
            {
                sb.AppendLine();
                sb.Append($"Exception: {exception.GetType().Name} - {exception.Message}");
            }

            // Exemplo de saída: Console / Arquivo / Trace
            // O arquivo deve ter write assíncrono em batch para não travar a thread.
            Trace.WriteLine(sb.ToString());
            
            // Aqui conectaríamos ao arquivo de log assíncrono.
        }

        public void LogSystemState(string component, string message)
        {
            Trace.WriteLine($"[{DateTime.UtcNow:O}] [INFO] [{component}] {message}");
        }

        public void LogError(string component, string message, Exception ex)
        {
            Trace.WriteLine($"[{DateTime.UtcNow:O}] [ERROR] [{component}] {message} - {ex.Message}");
            
            string telegramMsg = $"🚨 VOLTRIS PRO ERROR 🚨\n\n" +
                                 $"Máquina: {Environment.MachineName}\n" +
                                 $"Componente: {component}\n" +
                                 $"Mensagem: {message}\n" +
                                 $"\n--- CAUSA EXATA (STACK TRACE) ---\n{ex.ToString()}";
            VoltrisOptimizer.Services.TelegramLogger.SendMessageFireAndForget(telegramMsg);
        }
    }
}
