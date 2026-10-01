using System;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.SystemChanges
{
    /// <summary>
    /// Interface para operações seguras com rollback.
    /// </summary>
    public interface ISafeOperation
    {
        /// <summary>
        /// Executa uma operação segura.
        /// </summary>
        /// <param name="operation">Operação a ser executada.</param>
        /// <param name="rollbackAction">Ação de rollback em caso de falha.</param>
        /// <returns>Resultado da operação.</returns>
        Task<SafeOperationResult> ExecuteAsync(Func<Task> operation, Func<Task> rollbackAction);

        /// <summary>
        /// Executa uma operação segura com valor de retorno.
        /// </summary>
        /// <typeparam name="T">Tipo do valor retornado.</typeparam>
        /// <param name="operation">Operação a ser executada.</param>
        /// <param name="rollbackAction">Ação de rollback em caso de falha.</param>
        /// <returns>Resultado da operação com valor.</returns>
        Task<SafeOperationResult<T>> ExecuteAsync<T>(Func<Task<T>> operation, Func<T, Task> rollbackAction);

        /// <summary>
        /// Valida se uma operação pode ser executada com segurança.
        /// </summary>
        /// <param name="validationRules">Regras de validação.</param>
        /// <returns>Resultado da validação.</returns>
        Task<ValidationResult> ValidateAsync(ValidationRules validationRules);
    }

    /// <summary>
    /// Resultado de uma operação segura.
    /// </summary>
    public class SafeOperationResult
    {
        /// <summary>
        /// Indica se a operação foi bem-sucedida.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Mensagem de resultado.
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Exceção ocorrida, se houver.
        /// </summary>
        public Exception Exception { get; set; }

        /// <summary>
        /// Indica se o rollback foi executado.
        /// </summary>
        public bool RollbackExecuted { get; set; }

        /// <summary>
        /// Timestamp da operação.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Resultado de uma operação segura com valor de retorno.
    /// </summary>
    /// <typeparam name="T">Tipo do valor retornado.</typeparam>
    public class SafeOperationResult<T> : SafeOperationResult
    {
        /// <summary>
        /// Valor retornado pela operação.
        /// </summary>
        public T Value { get; set; }
    }

    /// <summary>
    /// Resultado de uma validação.
    /// </summary>
    public class ValidationResult
    {
        /// <summary>
        /// Indica se a validação foi bem-sucedida.
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// Lista de erros de validação.
        /// </summary>
        public System.Collections.Generic.List<string> Errors { get; set; } = new System.Collections.Generic.List<string>();

        /// <summary>
        /// Lista de advertências de validação.
        /// </summary>
        public System.Collections.Generic.List<string> Warnings { get; set; } = new System.Collections.Generic.List<string>();

        /// <summary>
        /// Indica se há erros críticos.
        /// </summary>
        public bool HasCriticalErrors => Errors.Count > 0;
    }

    /// <summary>
    /// Regras de validação.
    /// </summary>
    public class ValidationRules
    {
        /// <summary>
        /// Indica se deve validar permissões de administrador.
        /// </summary>
        public bool RequireAdminPermissions { get; set; } = true;

        /// <summary>
        /// Indica se deve validar espaço em disco.
        /// </summary>
        public bool CheckDiskSpace { get; set; } = true;

        /// <summary>
        /// Espaço mínimo necessário em MB.
        /// </summary>
        public long MinimumDiskSpaceMB { get; set; } = 100;

        /// <summary>
        /// Indica se deve validar conflitos com outros processos.
        /// </summary>
        public bool CheckProcessConflicts { get; set; } = true;

        /// <summary>
        /// Lista de processos que podem causar conflitos.
        /// </summary>
        public System.Collections.Generic.List<string> ConflictingProcesses { get; set; } = new System.Collections.Generic.List<string>();

        /// <summary>
        /// Indica se deve validar dependências do sistema.
        /// </summary>
        public bool CheckSystemDependencies { get; set; } = true;

        /// <summary>
        /// Versão mínima do sistema operacional.
        /// </summary>
        public Version MinimumOSVersion { get; set; }

        /// <summary>
        /// Indica se deve validar integridade do sistema.
        /// </summary>
        public bool CheckSystemIntegrity { get; set; } = true;
    }
}
