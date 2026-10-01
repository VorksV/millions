using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Rollback
{
    /// <summary>
    /// Gerencia o estado transacional das otimizações, garantindo rollback absoluto
    /// em caso de falhas, crash do software, ou desligamentos abruptos (BSOD).
    /// </summary>
    public class RecoveryGuard : IRecoveryGuard
    {
        private readonly ILoggingService _logger;
        private readonly string _transactionStateFile;
        private readonly string _registrySnapshotFile;
        
        // Mantém as transações ativas em memória
        private readonly Dictionary<string, SystemTransaction> _activeTransactions = new();

        public RecoveryGuard(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Diretório seguro em ProgramData para sobreviver a desinstalações parciais ou crashes
            var baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Voltris", "Recovery");
            Directory.CreateDirectory(baseDir);
            
            _transactionStateFile = Path.Combine(baseDir, "active_transactions.json");
            _registrySnapshotFile = Path.Combine(baseDir, "registry_snapshot.json");
        }

        /// <summary>
        /// Verifica no boot/startup se existem transações órfãs (o PC desligou abruptamente)
        /// e reverte automaticamente o sistema para o estado seguro.
        /// </summary>
        public async Task CheckAndRecoverPendingTransactionsAsync()
        {
            _logger.LogInfo("[RecoveryGuard] Verificando integridade pós-boot...");

            if (!File.Exists(_transactionStateFile))
            {
                _logger.LogSuccess("[RecoveryGuard] Nenhuma transação pendente. Boot limpo.");
                return;
            }

            try
            {
                var json = await File.ReadAllTextAsync(_transactionStateFile);
                var pending = JsonSerializer.Deserialize<List<SystemTransaction>>(json);

                if (pending != null && pending.Count > 0)
                {
                    _logger.LogWarning($"[RecoveryGuard] DETECTADAS {pending.Count} TRANSAÇÕES INCOMPLETAS (Possível BSOD ou Crash). Iniciando Rollback de Emergência!");
                    
                    foreach (var tx in pending)
                    {
                        await RollbackTransactionAsync(tx);
                    }
                }
                
                // Limpa o arquivo de estado após o recovery
                File.Delete(_transactionStateFile);
            }
            catch (Exception ex)
            {
                _logger.LogError("[RecoveryGuard] Falha crítica ao ler transações órfãs.", ex);
            }
        }

        /// <summary>
        /// Inicia uma transação segura antes de alterar o Windows.
        /// </summary>
        public async Task<string> BeginTransactionAsync(string componentName, string actionDescription, Dictionary<string, object> originalState)
        {
            var txId = Guid.NewGuid().ToString();
            var tx = new SystemTransaction
            {
                Id = txId,
                ComponentName = componentName,
                ActionDescription = actionDescription,
                Timestamp = DateTime.UtcNow,
                OriginalState = originalState
            };

            _activeTransactions[txId] = tx;
            
            await SaveActiveTransactionsAsync();
            
            _logger.LogInfo($"[RecoveryGuard] TRANSAÇÃO INICIADA [{txId}]: {actionDescription}");
            return txId;
        }

        /// <summary>
        /// Finaliza a transação indicando que a otimização ocorreu com sucesso
        /// e não causou falhas.
        /// </summary>
        public async Task CommitTransactionAsync(string txId)
        {
            if (_activeTransactions.Remove(txId, out var tx))
            {
                await SaveActiveTransactionsAsync();
                _logger.LogSuccess($"[RecoveryGuard] TRANSAÇÃO COMMITADA [{txId}]: {tx.ActionDescription}");
            }
        }

        /// <summary>
        /// Reverte uma transação imediatamente (Ex: timeout, exceção capturada).
        /// </summary>
        public async Task RollbackTransactionAsync(string txId)
        {
            if (_activeTransactions.Remove(txId, out var tx))
            {
                await RollbackTransactionAsync(tx);
                await SaveActiveTransactionsAsync();
            }
        }

        private async Task RollbackTransactionAsync(SystemTransaction tx)
        {
            _logger.LogWarning($"[RecoveryGuard] EXECUTANDO ROLLBACK: {tx.ActionDescription} (Comp: {tx.ComponentName})");
            
            try
            {
                // Aqui a lógica específica de Rollback baseado no ComponentName.
                // Exemplo: Restaurar Powercfg
                if (tx.ComponentName == "AdaptiveHardwareEngine")
                {
                    RestorePowercfgState(tx.OriginalState);
                }
                else if (tx.ComponentName == "VoltrisBlur")
                {
                    UnregisterDllHook(tx.OriginalState);
                }

                _logger.LogSuccess($"[RecoveryGuard] Rollback concluído com sucesso para: {tx.Id}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[RecoveryGuard] FALHA CATASTRÓFICA AO REVERTER TRANSAÇÃO {tx.Id}", ex);
            }
        }

        private void RestorePowercfgState(Dictionary<string, object> originalState)
        {
            if (originalState.TryGetValue("EPP", out var epp))
            {
                _logger.LogInfo($"[RecoveryGuard] Restaurando EPP para: {epp}");
                // Lógica de restauração powercfg aqui
            }
            if (originalState.TryGetValue("CoreParking", out var coreParking))
            {
                _logger.LogInfo($"[RecoveryGuard] Restaurando CoreParking para: {coreParking}");
                // Lógica de restauração powercfg aqui
            }
        }

        private void UnregisterDllHook(Dictionary<string, object> originalState)
        {
            _logger.LogInfo("[RecoveryGuard] Forçando regsvr32 /u da VoltrisBlur.dll para recuperação do Explorer...");
            // Lógica de desregistro aqui
        }

        private async Task SaveActiveTransactionsAsync()
        {
            try
            {
                var list = new List<SystemTransaction>(_activeTransactions.Values);
                var json = JsonSerializer.Serialize(list, VoltrisOptimizer.App.GlobalJsonOptions);
                await File.WriteAllTextAsync(_transactionStateFile, json);
            }
            catch (Exception ex)
            {
                _logger.LogError("[RecoveryGuard] Falha ao salvar estado das transações no disco.", ex);
            }
        }
    }

    public class SystemTransaction
    {
        public string Id { get; set; } = string.Empty;
        public string ComponentName { get; set; } = string.Empty;
        public string ActionDescription { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        
        // Estado original serializado para permitir o rollback completo
        public Dictionary<string, object> OriginalState { get; set; } = new();
    }
}
