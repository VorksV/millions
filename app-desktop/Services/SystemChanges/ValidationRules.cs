using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.SystemChanges
{
    /// <summary>
    /// Gerenciador de regras de validação para operações seguras.
    /// </summary>
    public class ValidationRulesManager
    {
        private readonly ISystemInfoService _systemInfoService;
        private readonly List<ValidationRule> _globalRules;

        /// <summary>
        /// Inicializa uma nova instância de <see cref="ValidationRulesManager"/>.
        /// </summary>
        /// <param name="systemInfoService">Serviço de informações do sistema.</param>
        public ValidationRulesManager(ISystemInfoService systemInfoService)
        {
            _systemInfoService = systemInfoService ?? throw new ArgumentNullException(nameof(systemInfoService));
            _globalRules = new List<ValidationRule>();
            InitializeGlobalRules();
        }

        /// <summary>
        /// Adiciona uma regra de validação global.
        /// </summary>
        /// <param name="rule">Regra a ser adicionada.</param>
        public void AddGlobalRule(ValidationRule rule)
        {
            if (rule == null)
                throw new ArgumentNullException(nameof(rule));

            _globalRules.Add(rule);
        }

        /// <summary>
        /// Remove uma regra de validação global.
        /// </summary>
        /// <param name="ruleId">ID da regra a ser removida.</param>
        public void RemoveGlobalRule(string ruleId)
        {
            if (string.IsNullOrEmpty(ruleId))
                throw new ArgumentException("O ID da regra não pode ser vazio.", nameof(ruleId));

            _globalRules.RemoveAll(r => r.Id == ruleId);
        }

        /// <summary>
        /// Valida uma operação com base nas regras globais e específicas.
        /// </summary>
        /// <param name="operationContext">Contexto da operação.</param>
        /// <param name="specificRules">Regras específicas para esta operação.</param>
        /// <returns>Resultado da validação.</returns>
        public async Task<ValidationResult> ValidateOperationAsync(
            OperationContext operationContext, 
            List<ValidationRule> specificRules = null)
        {
            if (operationContext == null)
                throw new ArgumentNullException(nameof(operationContext));

            var result = new ValidationResult
            {
                IsValid = true
            };

            // Aplica regras globais
            foreach (var rule in _globalRules)
            {
                await ApplyRuleAsync(rule, operationContext, result);
            }

            // Aplica regras específicas
            if (specificRules != null)
            {
                foreach (var rule in specificRules)
                {
                    await ApplyRuleAsync(rule, operationContext, result);
                }
            }

            return result;
        }

        /// <summary>
        /// Aplica uma regra de validação.
        /// </summary>
        /// <param name="rule">Regra a ser aplicada.</param>
        /// <param name="context">Contexto da operação.</param>
        /// <param name="result">Resultado da validação.</param>
        /// <returns>Tarefa assíncrona.</returns>
        private async Task ApplyRuleAsync(ValidationRule rule, OperationContext context, ValidationResult result)
        {
            try
            {
                var ruleResult = await rule.ValidateAsync(context, _systemInfoService);
                
                if (!ruleResult.IsValid)
                {
                    result.IsValid = false;
                    result.Errors.AddRange(ruleResult.Errors);
                }
                
                result.Warnings.AddRange(ruleResult.Warnings);
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.Errors.Add($"Erro ao aplicar regra '{rule.Name}': {ex.Message}");
            }
        }

        /// <summary>
        /// Inicializa as regras de validação globais padrão.
        /// </summary>
        private void InitializeGlobalRules()
        {
            // Regra para verificar permissões de administrador
            _globalRules.Add(new AdminPermissionRule());
            
            // Regra para verificar espaço em disco
            _globalRules.Add(new DiskSpaceRule(100)); // 100MB mínimo
            
            // Regra para verificar conflitos de processo
            _globalRules.Add(new ProcessConflictRule(new List<string> { "msiexec", "setup", "installer" }));
            
            // Regra para verificar integridade do sistema
            _globalRules.Add(new SystemIntegrityRule());
        }
    }

    /// <summary>
    /// Regra de validação abstrata.
    /// </summary>
    public abstract class ValidationRule
    {
        /// <summary>
        /// ID da regra.
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Nome da regra.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Descrição da regra.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Indica se a regra está ativa.
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Valida o contexto da operação.
        /// </summary>
        /// <param name="context">Contexto da operação.</param>
        /// <param name="systemInfoService">Serviço de informações do sistema.</param>
        /// <returns>Resultado da validação.</returns>
        public abstract Task<ValidationResult> ValidateAsync(OperationContext context, ISystemInfoService systemInfoService);
    }

    /// <summary>
    /// Regra para verificar permissões de administrador.
    /// </summary>
    public class AdminPermissionRule : ValidationRule
    {
        /// <summary>
        /// Inicializa uma nova instância de <see cref="AdminPermissionRule"/>.
        /// </summary>
        public AdminPermissionRule()
        {
            Name = "Permissões de Administrador";
            Description = "Verifica se o aplicativo está sendo executado com permissões de administrador";
        }

        /// <summary>
        /// Valida se o aplicativo está sendo executado com permissões de administrador.
        /// </summary>
        /// <param name="context">Contexto da operação.</param>
        /// <param name="systemInfoService">Serviço de informações do sistema.</param>
        /// <returns>Resultado da validação.</returns>
        public override async Task<ValidationResult> ValidateAsync(OperationContext context, ISystemInfoService systemInfoService)
        {
            var result = new ValidationResult();

            try
            {
                var isAdmin = await systemInfoService.IsRunningAsAdministratorAsync();
                if (!isAdmin)
                {
                    result.IsValid = false;
                    result.Errors.Add("Permissões de administrador são necessárias para esta operação");
                }
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.Errors.Add($"Erro ao verificar permissões de administrador: {ex.Message}");
            }

            await Task.CompletedTask;
            return result;
        }
    }

    /// <summary>
    /// Regra para verificar espaço em disco.
    /// </summary>
    public class DiskSpaceRule : ValidationRule
    {
        private readonly long _minimumSpaceMB;

        /// <summary>
        /// Inicializa uma nova instância de <see cref="DiskSpaceRule"/>.
        /// </summary>
        /// <param name="minimumSpaceMB">Espaço mínimo necessário em MB.</param>
        public DiskSpaceRule(long minimumSpaceMB)
        {
            _minimumSpaceMB = minimumSpaceMB;
            Name = "Espaço em Disco";
            Description = $"Verifica se há pelo menos {_minimumSpaceMB}MB de espaço livre em disco";
        }

        /// <summary>
        /// Valida se há espaço suficiente em disco.
        /// </summary>
        /// <param name="context">Contexto da operação.</param>
        /// <param name="systemInfoService">Serviço de informações do sistema.</param>
        /// <returns>Resultado da validação.</returns>
        public override async Task<ValidationResult> ValidateAsync(OperationContext context, ISystemInfoService systemInfoService)
        {
            var result = new ValidationResult();

            try
            {
                var availableSpace = await systemInfoService.GetAvailableDiskSpaceMBAsync();
                if (availableSpace < _minimumSpaceMB)
                {
                    result.IsValid = false;
                    result.Errors.Add($"Espaço em disco insuficiente. Necessário: {_minimumSpaceMB}MB, Disponível: {availableSpace}MB");
                }
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.Errors.Add($"Erro ao verificar espaço em disco: {ex.Message}");
            }

            await Task.CompletedTask;
            return result;
        }
    }

    /// <summary>
    /// Regra para verificar conflitos de processo.
    /// </summary>
    public class ProcessConflictRule : ValidationRule
    {
        private readonly List<string> _conflictingProcesses;

        /// <summary>
        /// Inicializa uma nova instância de <see cref="ProcessConflictRule"/>.
        /// </summary>
        /// <param name="conflictingProcesses">Lista de processos em conflito.</param>
        public ProcessConflictRule(List<string> conflictingProcesses)
        {
            _conflictingProcesses = conflictingProcesses ?? new List<string>();
            Name = "Conflitos de Processo";
            Description = "Verifica se há processos em execução que podem causar conflitos";
        }

        /// <summary>
        /// Valida se há processos em conflito em execução.
        /// </summary>
        /// <param name="context">Contexto da operação.</param>
        /// <param name="systemInfoService">Serviço de informações do sistema.</param>
        /// <returns>Resultado da validação.</returns>
        public override async Task<ValidationResult> ValidateAsync(OperationContext context, ISystemInfoService systemInfoService)
        {
            var result = new ValidationResult();

            try
            {
                var runningConflicts = await systemInfoService.CheckConflictingProcessesAsync(_conflictingProcesses);
                if (runningConflicts.Count > 0)
                {
                    result.Warnings.Add($"Processos em conflito detectados: {string.Join(", ", runningConflicts)}");
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Erro ao verificar conflitos de processo: {ex.Message}");
            }

            await Task.CompletedTask;
            return result;
        }
    }

    /// <summary>
    /// Regra para verificar integridade do sistema.
    /// </summary>
    public class SystemIntegrityRule : ValidationRule
    {
        /// <summary>
        /// Inicializa uma nova instância de <see cref="SystemIntegrityRule"/>.
        /// </summary>
        public SystemIntegrityRule()
        {
            Name = "Integridade do Sistema";
            Description = "Verifica a integridade geral do sistema";
        }

        /// <summary>
        /// Valida a integridade do sistema.
        /// </summary>
        /// <param name="context">Contexto da operação.</param>
        /// <param name="systemInfoService">Serviço de informações do sistema.</param>
        /// <returns>Resultado da validação.</returns>
        public override async Task<ValidationResult> ValidateAsync(OperationContext context, ISystemInfoService systemInfoService)
        {
            var result = new ValidationResult();

            try
            {
                var integrityIssues = await systemInfoService.CheckSystemIntegrityAsync();
                if (integrityIssues.Count > 0)
                {
                    result.Warnings.AddRange(integrityIssues);
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Erro ao verificar integridade do sistema: {ex.Message}");
            }

            await Task.CompletedTask;
            return result;
        }
    }

    /// <summary>
    /// Contexto de uma operação.
    /// </summary>
    public class OperationContext
    {
        /// <summary>
        /// ID da operação.
        /// </summary>
        public string OperationId { get; set; }

        /// <summary>
        /// Tipo da operação.
        /// </summary>
        public OperationType OperationType { get; set; }

        /// <summary>
        /// Caminhos de arquivos envolvidos na operação.
        /// </summary>
        public List<string> FilePaths { get; set; } = new List<string>();

        /// <summary>
        /// Chaves do registro envolvidas na operação.
        /// </summary>
        public List<string> RegistryKeys { get; set; } = new List<string>();

        /// <summary>
        /// Serviços afetados pela operação.
        /// </summary>
        public List<string> Services { get; set; } = new List<string>();

        /// <summary>
        /// Metadados adicionais.
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// Tipos de operações.
    /// </summary>
    public enum OperationType
    {
        /// <summary>
        /// Otimização do sistema.
        /// </summary>
        SystemOptimization,

        /// <summary>
        /// Instalação de software.
        /// </summary>
        SoftwareInstallation,

        /// <summary>
        /// Atualização do sistema.
        /// </summary>
        SystemUpdate,

        /// <summary>
        /// Configuração de hardware.
        /// </summary>
        HardwareConfiguration,

        /// <summary>
        /// Limpeza do sistema.
        /// </summary>
        SystemCleanup,

        /// <summary>
        /// Personalização do sistema.
        /// </summary>
        SystemCustomization
    }

    /// <summary>
    /// Serviço de validação em tempo real.
    /// </summary>
    public class RealTimeValidationService
    {
        private readonly ValidationRulesManager _rulesManager;
        private readonly Dictionary<string, OperationContext> _activeOperations;

        /// <summary>
        /// Inicializa uma nova instância de <see cref="RealTimeValidationService"/>.
        /// </summary>
        /// <param name="rulesManager">Gerenciador de regras de validação.</param>
        public RealTimeValidationService(ValidationRulesManager rulesManager)
        {
            _rulesManager = rulesManager ?? throw new ArgumentNullException(nameof(rulesManager));
            _activeOperations = new Dictionary<string, OperationContext>();
        }

        /// <summary>
        /// Registra uma operação em andamento.
        /// </summary>
        /// <param name="context">Contexto da operação.</param>
        public void RegisterOperation(OperationContext context)
        {
            if (context == null || string.IsNullOrEmpty(context.OperationId))
                throw new ArgumentException("Contexto de operação inválido.");

            _activeOperations[context.OperationId] = context;
        }

        /// <summary>
        /// Remove uma operação registrada.
        /// </summary>
        /// <param name="operationId">ID da operação.</param>
        public void UnregisterOperation(string operationId)
        {
            if (string.IsNullOrEmpty(operationId))
                throw new ArgumentException("ID da operação não pode ser vazio.");

            _activeOperations.Remove(operationId);
        }

        /// <summary>
        /// Valida uma operação em tempo real.
        /// </summary>
        /// <param name="operationId">ID da operação.</param>
        /// <returns>Resultado da validação.</returns>
        public async Task<ValidationResult> ValidateOperationAsync(string operationId)
        {
            if (string.IsNullOrEmpty(operationId))
                throw new ArgumentException("ID da operação não pode ser vazio.");

            if (!_activeOperations.TryGetValue(operationId, out var context))
            {
                throw new InvalidOperationException($"Operação '{operationId}' não encontrada.");
            }

            return await _rulesManager.ValidateOperationAsync(context);
        }

        /// <summary>
        /// Obtém o status de validação de todas as operações ativas.
        /// </summary>
        /// <returns>Dicionário com status de validação por operação.</returns>
        public async Task<Dictionary<string, ValidationResult>> GetActiveOperationsValidationStatusAsync()
        {
            var status = new Dictionary<string, ValidationResult>();

            foreach (var kvp in _activeOperations)
            {
                try
                {
                    var result = await _rulesManager.ValidateOperationAsync(kvp.Value);
                    status[kvp.Key] = result;
                }
                catch (Exception ex)
                {
                    status[kvp.Key] = new ValidationResult
                    {
                        IsValid = false,
                        Errors = new List<string> { $"Erro ao validar operação: {ex.Message}" }
                    };
                }
            }

            return status;
        }
    }
}
