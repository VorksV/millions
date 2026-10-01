using System;
using System.Collections.Concurrent;
using System.Security.AccessControl;
using Microsoft.Win32;
using VoltrisOptimizer.Services.SystemChanges;
using VoltrisOptimizer.Core.Configuration;

namespace VoltrisOptimizer.Services.SystemSafety
{
    public interface ISafeRegistryManager
    {
        bool TryWriteValue(string keyPath, string valueName, object value, RegistryValueKind kind);
        object ReadValue(string keyPath, string valueName, object defaultValue);
    }

    /// <summary>
    /// Gerenciador de Registro Seguro com Performance de Nível Enterprise.
    /// Utiliza Lock-Free Reads, Caching de chaves e permissões, evitando IO redundante.
    /// Suporta Strangler Pattern.
    /// </summary>
    public sealed class SafeRegistryManager : ISafeRegistryManager
    {
        private readonly ICapabilityGuard _capabilityGuard;
        private readonly IVoltrisFeatureFlagManager _featureFlags;
        
        // Cache lock-free para leituras rápidas: "KeyPath\ValueName" -> (Value, Timestamp)
        // Isso evita acessar o Kernel/Registry se a mesma chave for pedida repetidas vezes em curtos intervalos
        private readonly ConcurrentDictionary<string, RegistryCacheEntry> _readCache = new(StringComparer.OrdinalIgnoreCase);

        // Cache de permissões. Só checamos AccessControl uma vez por Hive/KeyBase.
        private readonly ConcurrentDictionary<string, bool> _permissionCache = new(StringComparer.OrdinalIgnoreCase);

        private readonly TimeSpan _cacheExpiration = TimeSpan.FromSeconds(30);

        private struct RegistryCacheEntry
        {
            public object Value;
            public DateTime CachedAt;
        }

        public SafeRegistryManager(ICapabilityGuard capabilityGuard, IVoltrisFeatureFlagManager featureFlags)
        {
            _capabilityGuard = capabilityGuard;
            _featureFlags = featureFlags;
        }

        public object ReadValue(string keyPath, string valueName, object defaultValue)
        {
            string cacheKey = $"{keyPath}\\{valueName}";

            if (_readCache.TryGetValue(cacheKey, out var cached))
            {
                if (DateTime.UtcNow - cached.CachedAt < _cacheExpiration)
                    return cached.Value;
            }

            try
            {
                using var baseKey = GetBaseKey(keyPath, out string subKeyPath);
                if (baseKey == null) return defaultValue;

                using var subKey = baseKey.OpenSubKey(subKeyPath, false);
                if (subKey == null) return defaultValue;

                var val = subKey.GetValue(valueName, defaultValue);
                
                // Atualiza cache (Thread-safe por ser ConcurrentDictionary com struct)
                _readCache[cacheKey] = new RegistryCacheEntry { Value = val, CachedAt = DateTime.UtcNow };
                
                return val;
            }
            catch
            {
                return defaultValue;
            }
        }

        public bool TryWriteValue(string keyPath, string valueName, object value, RegistryValueKind kind)
        {
            if (!_capabilityGuard.AllowRegistryTweaks()) return false;

            // STRANGLER PATTERN: Se a feature flag estiver desligada, opera em modo "Legacy/Bypass"
            // Na implementação final, o bypass seria chamar a Engine de Registro antiga, 
            // mas aqui estamos construindo a nova base atômica.
            if (!_featureFlags.UseSafeRegistryManager)
            {
                return WriteDirectly(keyPath, valueName, value, kind);
            }

            // MODO SEGURO: Zero Placebo & Evita IO desnecessário
            
            // 1. Otimização de Delta: Ler valor atual usando o Cache
            var currentValue = ReadValue(keyPath, valueName, null);
            if (currentValue != null && currentValue.Equals(value))
            {
                // Valor já é o desejado. Retorna true (idempotente) com zero custo de Kernel.
                return true;
            }

            // 2. Validação de Permissão via Cache (AccessCheck é muito lento)
            if (!HasPermissionToCreateOrWrite(keyPath))
                return false;

            // 3. Execução (Idealmente envelopada pelo RollbackEngine via ISystemChangeTransaction)
            bool success = WriteDirectly(keyPath, valueName, value, kind);

            // 4. Invalidação de Cache
            if (success)
            {
                string cacheKey = $"{keyPath}\\{valueName}";
                _readCache[cacheKey] = new RegistryCacheEntry { Value = value, CachedAt = DateTime.UtcNow };
            }

            return success;
        }

        private bool WriteDirectly(string keyPath, string valueName, object value, RegistryValueKind kind)
        {
            try
            {
                using var baseKey = GetBaseKey(keyPath, out string subKeyPath);
                if (baseKey == null) return false;

                using var subKey = baseKey.CreateSubKey(subKeyPath, true);
                if (subKey == null) return false;

                subKey.SetValue(valueName, value, kind);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool HasPermissionToCreateOrWrite(string keyPath)
        {
            if (_permissionCache.TryGetValue(keyPath, out bool hasPermission))
                return hasPermission;

            try
            {
                using var baseKey = GetBaseKey(keyPath, out string subKeyPath);
                if (baseKey != null)
                {
                    // Tenta abrir a chave com direitos de escrita ou a base para criação.
                    // Isso é mais rápido que extrair RegistrySecurity complexo.
                    using var subKey = baseKey.OpenSubKey(subKeyPath, RegistryKeyPermissionCheck.ReadWriteSubTree);
                    hasPermission = subKey != null || (baseKey.OpenSubKey("", RegistryKeyPermissionCheck.ReadWriteSubTree) != null);
                }
            }
            catch (UnauthorizedAccessException)
            {
                hasPermission = false;
            }
            catch
            {
                hasPermission = false;
            }

            _permissionCache[keyPath] = hasPermission;
            return hasPermission;
        }

        private RegistryKey GetBaseKey(string fullPath, out string subKeyPath)
        {
            subKeyPath = string.Empty;
            if (fullPath.StartsWith(@"HKEY_LOCAL_MACHINE\"))
            {
                subKeyPath = fullPath.Substring(19);
                return Registry.LocalMachine;
            }
            else if (fullPath.StartsWith(@"HKEY_CURRENT_USER\"))
            {
                subKeyPath = fullPath.Substring(18);
                return Registry.CurrentUser;
            }
            else if (fullPath.StartsWith(@"HKEY_CLASSES_ROOT\"))
            {
                subKeyPath = fullPath.Substring(18);
                return Registry.ClassesRoot;
            }
            else if (fullPath.StartsWith(@"HKEY_USERS\"))
            {
                subKeyPath = fullPath.Substring(11);
                return Registry.Users;
            }
            return null;
        }
    }
}
