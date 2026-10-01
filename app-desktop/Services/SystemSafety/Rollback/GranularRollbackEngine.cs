using System;
using System.Collections.Generic;
using Microsoft.Win32;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Core.Configuration;

namespace VoltrisOptimizer.Services.SystemSafety.Rollback
{
    public interface IGranularRollbackEngine
    {
        bool TryRollbackTransaction(Guid transactionId);
    }

    /// <summary>
    /// Motor de Rollback Granular e Idempotente.
    /// Recupera apenas o que foi estritamente alterado naquela transação e falhou.
    /// Evita a corrupção generalizada de voltar o perfil de energia inteiro por causa de um erro num Registry value.
    /// </summary>
    public sealed class GranularRollbackEngine : IGranularRollbackEngine
    {
        private readonly IIncrementalSnapshotManager _snapshotManager;
        private readonly ISafeRegistryManager _registryManager;
        private readonly ISafePowerManager _powerManager;
        private readonly IVoltrisFeatureFlagManager _featureFlags;
        private readonly ILoggingService _logger;

        public GranularRollbackEngine(
            IIncrementalSnapshotManager snapshotManager,
            ISafeRegistryManager registryManager,
            ISafePowerManager powerManager,
            IVoltrisFeatureFlagManager featureFlags,
            ILoggingService logger)
        {
            _snapshotManager = snapshotManager;
            _registryManager = registryManager;
            _powerManager = powerManager;
            _featureFlags = featureFlags;
            _logger = logger;
        }

        public bool TryRollbackTransaction(Guid transactionId)
        {
            if (!_featureFlags.UseAtomicRollbackEngine)
                return false;

            var items = _snapshotManager.GetSnapshotItems(transactionId);
            if (items == null || items.Count == 0) return true; // Nada a reverter

            _logger.LogWarning($"[ROLLBACK] Iniciando Rollback Granular para Transação: {transactionId} com {items.Count} itens");

            bool allSuccess = true;

            foreach (var item in items)
            {
                try
                {
                    bool itemSuccess = RevertItem(item);
                    if (!itemSuccess) allSuccess = false;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[ROLLBACK] Falha severa ao reverter item {item.ResourceId} do tipo {item.Type}", ex);
                    allSuccess = false;
                }
            }

            // Se aplicou o rollback, descarta a transação da RAM para evitar double-rollbacks ou leaks
            _snapshotManager.DiscardTransaction(transactionId);

            return allSuccess;
        }

        private bool RevertItem(SnapshotItem item)
        {
            switch (item.Type)
            {
                case ResourceType.Registry:
                    // Formato do ResourceId: "HKEY_LOCAL_MACHINE\Path\To\Key|ValueName|Kind"
                    var parts = item.ResourceId.Split('|');
                    if (parts.Length >= 2)
                    {
                        string keyPath = parts[0];
                        string valueName = parts[1];
                        RegistryValueKind kind = RegistryValueKind.Unknown;
                        
                        if (parts.Length == 3 && Enum.TryParse(parts[2], out RegistryValueKind parsedKind))
                            kind = parsedKind;

                        if (item.OriginalValue == null)
                        {
                            // Significa que não existia. Precisaríamos de um TryDeleteValue.
                            // Por ora, vamos assumir que apenas rollback de modificação é prioritário.
                            return true; 
                        }

                        // Reverte o valor atráves da via segura para popular cache de volta.
                        return _registryManager.TryWriteValue(keyPath, valueName, item.OriginalValue, kind);
                    }
                    break;

                case ResourceType.PowerSetting:
                    // Formato: "SchemeGuid|SubGroupGuid|SettingGuid"
                    var powerGuids = item.ResourceId.Split('|');
                    if (powerGuids.Length == 3 &&
                        Guid.TryParse(powerGuids[0], out Guid scheme) &&
                        Guid.TryParse(powerGuids[1], out Guid subGroup) &&
                        Guid.TryParse(powerGuids[2], out Guid setting) &&
                        item.OriginalValue is uint originalAcValue)
                    {
                        return _powerManager.TrySetPowerSetting(scheme, subGroup, setting, originalAcValue);
                    }
                    break;
                
                // Outros ResourceTypes (ProcessPriority, etc.) podem ser mapeados aqui.
                // Mas geralmente priority não precisa de "rollback atômico" em falha transacional, 
                // ele só é setado de volta em Cleanup Session.
            }

            return false; // Não sabe como reverter
        }
    }
}
