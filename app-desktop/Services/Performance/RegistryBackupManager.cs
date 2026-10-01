using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Performance
{
    /// <summary>
    /// Gerenciador persistente de backups de registro para otimizações de performance
    /// Garante que os valores originais NUNCA sejam perdidos, mesmo após fechar o app.
    /// </summary>
    public class RegistryBackupManager
    {
        private readonly string _backupFilePath;
        private readonly ILoggingService _logger;
        private Dictionary<string, object> _cachedBackups;
        private readonly object _lock = new object();

        public RegistryBackupManager(ILoggingService logger)
        {
            _logger = logger;
            _backupFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VoltrisOptimizer",
                "performance_backups.json");
            
            _cachedBackups = LoadBackups();
        }

        private Dictionary<string, object> LoadBackups()
        {
            try
            {
                if (File.Exists(_backupFilePath))
                {
                    var json = File.ReadAllText(_backupFilePath);
                    return JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new Dictionary<string, object>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[BackupManager] Erro ao carregar backups: {ex.Message}");
            }
            return new Dictionary<string, object>();
        }

        public void SaveBackups()
        {
            try
            {
                lock (_lock)
                {
                    var directory = Path.GetDirectoryName(_backupFilePath);
                    if (!Directory.Exists(directory)) Directory.CreateDirectory(directory!);

                    var json = JsonSerializer.Serialize(_cachedBackups, new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,  WriteIndented = true });
                    File.WriteAllText(_backupFilePath, json);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[BackupManager] Erro ao salvar backups: {ex.Message}");
            }
        }

        public void BackupValue(string hive, string path, string valueName)
        {
            string key = $"{hive}\\{path}\\{valueName}".ToUpperInvariant();

            lock (_lock)
            {
                if (_cachedBackups.ContainsKey(key)) return; // Já temos o ORIGINAL

                try
                {
                    var root = hive.ToUpper() switch
                    {
                        "HKLM" or "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
                        "HKCU" or "HKEY_CURRENT_USER" => Registry.CurrentUser,
                        _ => null
                    };

                    if (root == null) return;

                    using var regKey = root.OpenSubKey(path);
                    if (regKey != null)
                    {
                        var value = regKey.GetValue(valueName);
                        _cachedBackups[key] = value ?? ""; // Null vira vazio
                        SaveBackups();
                        _logger.LogInfo($"[BackupManager] Backup criado: {key}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[BackupManager] Falha ao fazer backup de {key}: {ex.Message}");
                }
            }
        }

        public object? GetBackupValue(string hive, string path, string valueName)
        {
            string key = $"{hive}\\{path}\\{valueName}".ToUpperInvariant();
            lock (_lock)
            {
                return _cachedBackups.TryGetValue(key, out var val) ? val : null;
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _cachedBackups.Clear();
                if (File.Exists(_backupFilePath)) File.Delete(_backupFilePath);
            }
        }
    }
}
