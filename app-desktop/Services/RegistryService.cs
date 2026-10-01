using System;
using System.Collections.Generic;
using Microsoft.Win32;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services
{
    /// <summary>
    /// Implementação segura do IRegistryService com backup automático e logging
    /// </summary>
    public class RegistryService : IRegistryService
    {
        private readonly ILoggingService? _logger;
        private readonly List<RegistryBackup> _sessionBackups = new();
        private readonly object _lock = new();

        public RegistryService(ILoggingService? logger = null)
        {
            _logger = logger;
        }

        public T? GetValue<T>(RegistryHive hive, string subKey, string valueName, T? defaultValue = default)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
                using var key = baseKey.OpenSubKey(subKey, false);

                if (key == null)
                {
                    _logger?.LogInfo($"[Registry] Chave não encontrada: {hive}\\{subKey}");
                    return defaultValue;
                }

                var value = key.GetValue(valueName);
                if (value == null)
                {
                    return defaultValue;
                }

                // Tentar converter para o tipo esperado
                if (typeof(T) == typeof(int) && value is int intVal)
                    return (T)(object)intVal;
                if (typeof(T) == typeof(uint))
                {
                    // Tratar conversão segura para uint (DpcTimeout = 0xFFFFFFFF armazenado como int -1)
                    if (value is int signedVal)
                        return (T)(object)(uint)signedVal;
                    if (value is uint uintDirectVal)
                        return (T)(object)uintDirectVal;
                    if (value is long longForUint)
                        return (T)(object)(uint)(longForUint & 0xFFFFFFFF);
                    return (T)(object)Convert.ToUInt32(value);
                }
                if (typeof(T) == typeof(string) && value is string strVal)
                    return (T)(object)strVal;
                if (typeof(T) == typeof(long) && value is long longVal)
                    return (T)(object)longVal;
                if (typeof(T) == typeof(byte[]) && value is byte[] byteVal)
                    return (T)(object)byteVal;

                // Fallback para conversão genérica
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[Registry] Erro ao ler {hive}\\{subKey}\\{valueName}: {ex.Message}");
                return defaultValue;
            }
        }

        public RegistryOperationResult SetValue(RegistryHive hive, string subKey, string valueName, object value, RegistryValueKind valueKind)
        {
            var result = new RegistryOperationResult();

            try
            {
                // Fazer backup antes de modificar
                var backup = BackupValue(hive, subKey, valueName);
                result.PreviousValue = backup.Value;

                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
                using var key = baseKey.OpenSubKey(subKey, true) ?? baseKey.CreateSubKey(subKey, true);

                if (key == null)
                {
                    result.Success = false;
                    result.Message = $"Não foi possível abrir/criar a chave: {hive}\\{subKey}";
                    _logger?.LogError($"[Registry] {result.Message}", null);
                    return result;
                }

                key.SetValue(valueName, value, valueKind);

                result.Success = true;
                result.Message = $"Valor definido com sucesso: {hive}\\{subKey}\\{valueName} = {value}";
                _logger?.LogInfo($"[Registry] {result.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                result.Success = false;
                result.Message = $"Acesso negado. Execute como administrador. Detalhes: {ex.Message}";
                result.Exception = ex;
                _logger?.LogError($"[Registry] Acesso negado em {hive}\\{subKey}\\{valueName}", ex);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Erro ao definir valor: {ex.Message}";
                result.Exception = ex;
                _logger?.LogError($"[Registry] Erro em {hive}\\{subKey}\\{valueName}: {ex.Message}", ex);
            }

            return result;
        }

        public RegistryOperationResult DeleteValue(RegistryHive hive, string subKey, string valueName)
        {
            var result = new RegistryOperationResult();

            try
            {
                // Fazer backup antes de deletar
                var backup = BackupValue(hive, subKey, valueName);
                result.PreviousValue = backup.Value;

                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
                using var key = baseKey.OpenSubKey(subKey, true);

                if (key == null)
                {
                    result.Success = true;
                    result.Message = LocalizationService.Instance.GetString("RegistryKeyNothingToDelete");
                    return result;
                }

                key.DeleteValue(valueName, false);

                result.Success = true;
                result.Message = $"Valor deletado: {hive}\\{subKey}\\{valueName}";
                _logger?.LogInfo($"[Registry] {result.Message}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Erro ao deletar valor: {ex.Message}";
                result.Exception = ex;
                _logger?.LogError($"[Registry] Erro ao deletar {hive}\\{subKey}\\{valueName}: {ex.Message}", ex);
            }

            return result;
        }

        public bool KeyExists(RegistryHive hive, string subKey)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
                using var key = baseKey.OpenSubKey(subKey, false);
                return key != null;
            }
            catch
            {
                return false;
            }
        }

        public bool ValueExists(RegistryHive hive, string subKey, string valueName)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
                using var key = baseKey.OpenSubKey(subKey, false);
                if (key == null) return false;
                return key.GetValue(valueName) != null;
            }
            catch
            {
                return false;
            }
        }

        public RegistryOperationResult CreateKey(RegistryHive hive, string subKey)
        {
            var result = new RegistryOperationResult();

            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
                using var key = baseKey.CreateSubKey(subKey, true);

                result.Success = key != null;
                result.Message = key != null
                    ? $"Chave criada: {hive}\\{subKey}"
                    : $"Falha ao criar chave: {hive}\\{subKey}";

                _logger?.LogInfo($"[Registry] {result.Message}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Erro ao criar chave: {ex.Message}";
                result.Exception = ex;
                _logger?.LogError($"[Registry] Erro ao criar {hive}\\{subKey}: {ex.Message}", ex);
            }

            return result;
        }

        public RegistryOperationResult DeleteKey(RegistryHive hive, string subKey)
        {
            var result = new RegistryOperationResult();

            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
                baseKey.DeleteSubKeyTree(subKey, false);

                result.Success = true;
                result.Message = $"Chave deletada: {hive}\\{subKey}";
                _logger?.LogInfo($"[Registry] {result.Message}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Erro ao deletar chave: {ex.Message}";
                result.Exception = ex;
                _logger?.LogError($"[Registry] Erro ao deletar {hive}\\{subKey}: {ex.Message}", ex);
            }

            return result;
        }

        public string[] GetValueNames(RegistryHive hive, string subKey)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
                using var key = baseKey.OpenSubKey(subKey, false);
                return key?.GetValueNames() ?? Array.Empty<string>();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[Registry] Erro ao obter nomes de valores de {hive}\\{subKey}: {ex.Message}");
                return Array.Empty<string>();
            }
        }

        public string[] GetSubKeyNames(RegistryHive hive, string subKey)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
                using var key = baseKey.OpenSubKey(subKey, false);
                return key?.GetSubKeyNames() ?? Array.Empty<string>();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[Registry] Erro ao obter subchaves de {hive}\\{subKey}: {ex.Message}");
                return Array.Empty<string>();
            }
        }

        public RegistryBackup BackupValue(RegistryHive hive, string subKey, string valueName)
        {
            var backup = new RegistryBackup
            {
                KeyPath = $"{hive}\\{subKey}",
                ValueName = valueName,
                BackupTime = DateTime.Now,
                Existed = false
            };

            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
                using var key = baseKey.OpenSubKey(subKey, false);

                if (key != null)
                {
                    var value = key.GetValue(valueName);
                    if (value != null)
                    {
                        backup.Value = value;
                        backup.ValueKind = key.GetValueKind(valueName);
                        backup.Existed = true;
                    }
                }

                lock (_lock)
                {
                    _sessionBackups.Add(backup);
                }

                _logger?.LogInfo($"[Registry] Backup criado: {backup.KeyPath}\\{backup.ValueName} (existia: {backup.Existed})");
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[Registry] Erro ao fazer backup de {hive}\\{subKey}\\{valueName}: {ex.Message}");
            }

            return backup;
        }

        public RegistryOperationResult RestoreValue(RegistryBackup backup)
        {
            var result = new RegistryOperationResult();

            try
            {
                // Extrair hive e subkey do KeyPath
                var parts = backup.KeyPath.Split(new[] { '\\' }, 2);
                if (parts.Length != 2)
                {
                    result.Success = false;
                    result.Message = LocalizationService.Instance.GetString("RegistryInvalidBackupFormat");
                    return result;
                }

                var hive = Enum.Parse<RegistryHive>(parts[0]);
                var subKey = parts[1];

                if (backup.Existed && backup.Value != null)
                {
                    // Restaurar o valor original
                    return SetValue(hive, subKey, backup.ValueName, backup.Value, backup.ValueKind);
                }
                else
                {
                    // Deletar o valor (não existia antes)
                    return DeleteValue(hive, subKey, backup.ValueName);
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Erro ao restaurar backup: {ex.Message}";
                result.Exception = ex;
                _logger?.LogError($"[Registry] Erro ao restaurar backup de {backup.KeyPath}\\{backup.ValueName}: {ex.Message}", ex);
            }

            return result;
        }

        public IReadOnlyList<RegistryBackup> GetSessionBackups()
        {
            lock (_lock)
            {
                return _sessionBackups.AsReadOnly();
            }
        }

        public void RestoreAllSessionBackups()
        {
            _logger?.LogInfo("[Registry] Iniciando restauração de todos os backups da sessão...");

            List<RegistryBackup> backupsToRestore;
            lock (_lock)
            {
                backupsToRestore = new List<RegistryBackup>(_sessionBackups);
            }

            // Restaurar em ordem reversa (LIFO)
            backupsToRestore.Reverse();

            int successCount = 0;
            int failCount = 0;

            foreach (var backup in backupsToRestore)
            {
                var result = RestoreValue(backup);
                if (result.Success)
                    successCount++;
                else
                    failCount++;
            }

            _logger?.LogInfo($"[Registry] Restauração concluída: {successCount} sucesso, {failCount} falhas");
            ClearSessionBackups();
        }

        public void ClearSessionBackups()
        {
            lock (_lock)
            {
                _sessionBackups.Clear();
            }
            _logger?.LogInfo("[Registry] Backups da sessão limpos");
        }
    }
}

