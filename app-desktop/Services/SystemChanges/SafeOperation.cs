using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.SystemChanges
{
    /// <summary>
    /// Implementação de operações seguras com rollback.
    /// </summary>
    public class SafeOperation : ISafeOperation
    {
        private readonly ISystemInfoService _systemInfoService;

        /// <summary>
        /// Inicializa uma nova instância de <see cref="SafeOperation"/>.
        /// </summary>
        /// <param name="systemInfoService">Serviço de informações do sistema.</param>
        public SafeOperation(ISystemInfoService systemInfoService)
        {
            _systemInfoService = systemInfoService;
        }

        /// <summary>
        /// Executa uma operação segura.
        /// </summary>
        /// <param name="operation">Operação a ser executada.</param>
        /// <param name="rollbackAction">Ação de rollback em caso de falha.</param>
        /// <returns>Resultado da operação.</returns>
        public async Task<SafeOperationResult> ExecuteAsync(Func<Task> operation, Func<Task> rollbackAction)
        {
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));

            var result = new SafeOperationResult
            {
                Success = false,
                RollbackExecuted = false
            };

            try
            {
                // Executa a operação
                await operation();
                result.Success = true;
                result.Message = LocalizationService.Instance.GetString("SafeOperationCompleted");
            }
            catch (Exception ex)
            {
                result.Exception = ex;
                result.Message = $"Falha na operação: {ex.Message}";

                // Tenta executar o rollback se disponível
                if (rollbackAction != null)
                {
                    try
                    {
                        await rollbackAction();
                        result.RollbackExecuted = true;
                        result.Message += " - Rollback executado com sucesso";
                    }
                    catch (Exception rollbackEx)
                    {
                        result.Message += $" - Falha no rollback: {rollbackEx.Message}";
                        // Log de erro de rollback
                        Debug.WriteLine($"Erro no rollback: {rollbackEx}");
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Executa uma operação segura com valor de retorno.
        /// </summary>
        /// <typeparam name="T">Tipo do valor retornado.</typeparam>
        /// <param name="operation">Operação a ser executada.</param>
        /// <param name="rollbackAction">Ação de rollback em caso de falha.</param>
        /// <returns>Resultado da operação com valor.</returns>
        public async Task<SafeOperationResult<T>> ExecuteAsync<T>(Func<Task<T>> operation, Func<T, Task> rollbackAction)
        {
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));

            var result = new SafeOperationResult<T>
            {
                Success = false,
                RollbackExecuted = false
            };

            T operationValue = default(T);

            try
            {
                // Executa a operação
                operationValue = await operation();
                result.Value = operationValue;
                result.Success = true;
                result.Message = LocalizationService.Instance.GetString("SafeOperationCompleted");
            }
            catch (Exception ex)
            {
                result.Exception = ex;
                result.Message = $"Falha na operação: {ex.Message}";

                // Tenta executar o rollback se disponível
                if (rollbackAction != null)
                {
                    try
                    {
                        await rollbackAction(operationValue);
                        result.RollbackExecuted = true;
                        result.Message += " - Rollback executado com sucesso";
                    }
                    catch (Exception rollbackEx)
                    {
                        result.Message += $" - Falha no rollback: {rollbackEx.Message}";
                        // Log de erro de rollback
                        Debug.WriteLine($"Erro no rollback: {rollbackEx}");
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Valida se uma operação pode ser executada com segurança.
        /// </summary>
        /// <param name="validationRules">Regras de validação.</param>
        /// <returns>Resultado da validação.</returns>
        public async Task<ValidationResult> ValidateAsync(ValidationRules validationRules)
        {
            if (validationRules == null)
                throw new ArgumentNullException(nameof(validationRules));

            var result = new ValidationResult
            {
                IsValid = true
            };

            // Valida permissões de administrador
            if (validationRules.RequireAdminPermissions)
            {
                if (!IsRunningAsAdministrator())
                {
                    result.Errors.Add("Permissões de administrador são necessárias para esta operação");
                    result.IsValid = false;
                }
            }

            // Valida espaço em disco
            if (validationRules.CheckDiskSpace)
            {
                var diskSpace = await GetAvailableDiskSpaceMBAsync();
                if (diskSpace < validationRules.MinimumDiskSpaceMB)
                {
                    result.Errors.Add($"Espaço em disco insuficiente. Necessário: {validationRules.MinimumDiskSpaceMB}MB, Disponível: {diskSpace}MB");
                    result.IsValid = false;
                }
            }

            // Valida conflitos com processos
            if (validationRules.CheckProcessConflicts)
            {
                var conflictingProcesses = await CheckConflictingProcessesAsync(validationRules.ConflictingProcesses);
                if (conflictingProcesses.Count > 0)
                {
                    result.Warnings.Add($"Processos em conflito detectados: {string.Join(", ", conflictingProcesses)}");
                }
            }

            // Valida versão do sistema operacional
            if (validationRules.CheckSystemDependencies && validationRules.MinimumOSVersion != null)
            {
                var osVersion = Environment.OSVersion.Version;
                if (osVersion < validationRules.MinimumOSVersion)
                {
                    result.Errors.Add($"Versão do sistema operacional insuficiente. Necessária: {validationRules.MinimumOSVersion}, Atual: {osVersion}");
                    result.IsValid = false;
                }
            }

            // Valida integridade do sistema
            if (validationRules.CheckSystemIntegrity)
            {
                var integrityIssues = await CheckSystemIntegrityAsync();
                if (integrityIssues.Count > 0)
                {
                    result.Warnings.AddRange(integrityIssues);
                }
            }

            return result;
        }

        /// <summary>
        /// Verifica se o aplicativo está sendo executado com permissões de administrador.
        /// </summary>
        /// <returns>Verdadeiro se estiver executando como administrador.</returns>
        private bool IsRunningAsAdministrator()
        {
            try
            {
                var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Obtém o espaço disponível em disco em MB.
        /// </summary>
        /// <returns>Espaço disponível em MB.</returns>
        private async Task<long> GetAvailableDiskSpaceMBAsync()
        {
            try
            {
                var drive = new System.IO.DriveInfo(Path.GetPathRoot(Environment.CurrentDirectory));
                return drive.AvailableFreeSpace / (1024 * 1024);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Verifica processos em conflito.
        /// </summary>
        /// <param name="conflictingProcesses">Lista de processos em conflito.</param>
        /// <returns>Lista de processos em execução que estão em conflito.</returns>
        private async Task<List<string>> CheckConflictingProcessesAsync(List<string> conflictingProcesses)
        {
            var runningConflictingProcesses = new List<string>();

            try
            {
                var processes = Process.GetProcesses();
                foreach (var process in processes)
                {
                    try
                    {
                        // Verifica se o nome do processo está na lista de conflitos
                        if (conflictingProcesses != null && conflictingProcesses.Any(p => string.Equals(p, process.ProcessName, StringComparison.OrdinalIgnoreCase)))
                        {
                            runningConflictingProcesses.Add(process.ProcessName);
                        }
                    }
                    catch
                    {
                        // Ignora erros ao acessar informações de processos
                    }
                }
            }
            catch
            {
                // Em caso de erro, retorna lista vazia
            }

            await Task.CompletedTask;
            return runningConflictingProcesses;
        }

        /// <summary>
        /// Verifica a integridade do sistema.
        /// </summary>
        /// <returns>Lista de problemas de integridade.</returns>
        private async Task<List<string>> CheckSystemIntegrityAsync()
        {
            var issues = new List<string>();

            // Em uma implementação real, poderia verificar:
            // - Estado do sistema de arquivos
            // - Integridade de arquivos críticos
            // - Estado do registro do Windows
            // - Serviços essenciais em execução

            await Task.CompletedTask;
            return issues;
        }
    }
}
