using System;
using System.Diagnostics;

namespace VoltrisOptimizer.Services.Drivers
{
    /// <summary>
    /// Escopo de operação para o pipeline de drivers.
    ///
    /// Garante que toda operação crítica responda, no log, às perguntas exigidas:
    /// o que aconteceu, quando, em qual serviço/operação, qual foi o resultado,
    /// onde falhou, qual exceção, se houve rollback e quanto tempo levou.
    ///
    /// Usa o sistema de logs EXISTENTE (App.LoggingService) — nenhum sistema novo é criado.
    /// </summary>
    public sealed class DriverOperationScope : IDisposable
    {
        private readonly string _operation;
        private readonly string _subject;
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private readonly bool _logVerbose;
        private bool _completed;
        private bool _rolledBack;
        private string? _failureStage;

        /// <summary>Identificador curto da operação, correlacionando todas as linhas de log.</summary>
        public string OperationId { get; }

        /// <summary>Etapa em que a falha ocorreu (detect/download/validate/backup/install/verify).</summary>
        public string? FailureStage => _failureStage;

        public DriverOperationScope(string operation, string subject, bool logVerbose = false)
        {
            _operation = operation;
            _subject = subject;
            _logVerbose = logVerbose;
            OperationId = Guid.NewGuid().ToString("N")[..8];

            App.LoggingService?.LogInfo(
                $"[DriverOp:{OperationId}] BEGIN {operation} | alvo='{subject}' | thread={Environment.CurrentManagedThreadId}");
        }

        /// <summary>Registra uma etapa intermediária bem-sucedida.</summary>
        public void Stage(string stage, string detail)
        {
            App.LoggingService?.LogInfo($"[DriverOp:{OperationId}]   {stage}: {detail} | t={_sw.ElapsedMilliseconds}ms");
        }

        /// <summary>Registra uma etapa intermediária que falhou. Não encerra a operação.</summary>
        public void StageFailed(string stage, string detail, Exception? ex = null)
        {
            _failureStage = stage;
            string chain = ex != null ? $" | excecao={ex.Describe()}" : string.Empty;
            App.LoggingService?.LogError($"[DriverOp:{OperationId}]   FALHA em {stage}: {detail}{chain} | t={_sw.ElapsedMilliseconds}ms", ex);
        }

        /// <summary>Marca que um rollback foi executado.</summary>
        public void MarkRollback(string reason)
        {
            _rolledBack = true;
            App.LoggingService?.LogWarning($"[DriverOp:{OperationId}] ROLLBACK acionado: {reason} | t={_sw.ElapsedMilliseconds}ms");
        }

        /// <summary>Encerra a operação com sucesso.</summary>
        public void Succeed(string result)
        {
            if (_completed) return;
            _completed = true;
            _sw.Stop();
            App.LoggingService?.LogInfo(
                $"[DriverOp:{OperationId}] END { _operation} OK | {result} | duracao={_sw.ElapsedMilliseconds}ms | rollback={_rolledBack}");
        }

        /// <summary>Encerra a operação com falha, registrando onde e por quê.</summary>
        public void Fail(string reason, Exception? ex = null)
        {
            if (_completed) return;
            _completed = true;
            _sw.Stop();
            string stage = _failureStage ?? "desconhecida";
            string chain = ex != null ? $" | excecao={ex.Describe()}" : string.Empty;
            App.LoggingService?.LogError(
                $"[DriverOp:{OperationId}] END { _operation} FALHOU | motivo={reason} | etapa={stage} | duracao={_sw.ElapsedMilliseconds}ms | rollback={_rolledBack}{chain}",
                ex);
        }

        /// <summary>Log verboso opcional (evita inundar o log em operações de alto volume).</summary>
        public void Trace(string message)
        {
            if (_logVerbose)
                App.LoggingService?.LogInfo($"[DriverOp:{OperationId}]   {message} | t={_sw.ElapsedMilliseconds}ms");
        }

        public void Dispose()
        {
            if (!_completed)
            {
                _sw.Stop();
                App.LoggingService?.LogWarning(
                    $"[DriverOp:{OperationId}] END {_operation} encerrado sem resultado explicito | duracao={_sw.ElapsedMilliseconds}ms");
            }
        }
    }
}
