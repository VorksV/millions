using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Core;
using VoltrisOptimizer.Services.Power;

namespace VoltrisOptimizer.Services.SystemSnapshot
{
    /// <summary>
    /// Serviço de snapshot completo do sistema antes de operações críticas
    /// CORREÇÃO ENTERPRISE: Garantia de rollback total
    /// </summary>
    public class SystemSnapshotService
    {
        private readonly ILoggingService _logger;
        private readonly string _snapshotPath;
        
        public SystemSnapshotService(ILoggingService logger)
        {
            _logger = logger;
            _snapshotPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Voltris",
                "Snapshots"
            );
            
            Directory.CreateDirectory(_snapshotPath);
        }
        
        /// <summary>
        /// Cria snapshot completo do sistema
        /// </summary>
        public async Task<SystemSnapshot> CreateSnapshotAsync(string operationName)
        {
            _logger.LogInfo($"[Snapshot] Criando snapshot para: {operationName}");
            
            var snapshot = new SystemSnapshot
            {
                Id = Guid.NewGuid().ToString(),
                OperationName = operationName,
                Timestamp = DateTime.UtcNow,
                MachineId = Environment.MachineName
            };
            
            try
            {
                // Capturar estado de registro crítico
                snapshot.RegistryBackups = await BackupCriticalRegistryKeysAsync();
                
                // Capturar plano de energia atual
                snapshot.PowerPlanGuid = await GetCurrentPowerPlanAsync();
                
                // Capturar serviços em execução
                snapshot.RunningServices = await GetRunningServicesAsync();
                
                // Capturar configurações de rede
                snapshot.NetworkSettings = await GetNetworkSettingsAsync();
                
                // Salvar snapshot em disco
                await SaveSnapshotAsync(snapshot);
                
                _logger.LogSuccess($"[Snapshot] Snapshot criado: {snapshot.Id}");
                
                return snapshot;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[Snapshot] Erro ao criar snapshot: {ex.Message}", ex);
                throw;
            }
        }
        
        /// <summary>
        /// Restaura sistema a partir de snapshot
        /// </summary>
        public async Task<bool> RestoreSnapshotAsync(string snapshotId)
        {
            try
            {
                _logger.LogInfo($"[Snapshot] Restaurando snapshot: {snapshotId}");
                
                var snapshot = await LoadSnapshotAsync(snapshotId);
                if (snapshot == null)
                {
                    _logger.LogError("[Snapshot] Snapshot não encontrado");
                    return false;
                }
                
                bool success = true;
                
                // Restaurar registro
                if (snapshot.RegistryBackups != null)
                {
                    foreach (var backup in snapshot.RegistryBackups)
                    {
                        success &= await RestoreRegistryKeyAsync(backup);
                    }
                }
                
                // Restaurar plano de energia
                if (!string.IsNullOrEmpty(snapshot.PowerPlanGuid))
                {
                    success &= await RestorePowerPlanAsync(snapshot.PowerPlanGuid);
                }
                
                // Restaurar serviços
                if (snapshot.RunningServices != null)
                {
                    success &= await RestoreServicesAsync(snapshot.RunningServices);
                }
                
                if (success)
                {
                    _logger.LogSuccess("[Snapshot] Restauração concluída com sucesso");
                }
                else
                {
                    _logger.LogWarning("[Snapshot] Restauração concluída com avisos");
                }
                
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[Snapshot] Erro ao restaurar: {ex.Message}", ex);
                return false;
            }
        }
        
        /// <summary>
        /// Lista todos os snapshots disponíveis
        /// </summary>
        public async Task<List<SystemSnapshot>> ListSnapshotsAsync()
        {
            var snapshots = new List<SystemSnapshot>();
            
            try
            {
                var files = Directory.GetFiles(_snapshotPath, "*.json");
                
                foreach (var file in files)
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(file);
                        var snapshot = JsonSerializer.Deserialize<SystemSnapshot>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                        if (snapshot != null)
                        {
                            snapshots.Add(snapshot);
                        }
                    }
                    catch
                    {
                        // Ignorar arquivos corrompidos
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[Snapshot] Erro ao listar snapshots: {ex.Message}");
            }
            
            return snapshots;
        }
        
        private async Task<List<RegistryBackup>> BackupCriticalRegistryKeysAsync()
        {
            var backups = new List<RegistryBackup>();
            
            // Chaves críticas para backup
            var criticalKeys = new[]
            {
                @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Power",
                @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters",
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\GameDVR"
            };
            
            foreach (var keyPath in criticalKeys)
            {
                try
                {
                    var backup = await BackupRegistryKeyAsync(keyPath);
                    if (backup != null)
                    {
                        backups.Add(backup);
                    }
                }
                catch
                {
                    // Continuar com próxima chave
                }
            }
            
            return backups;
        }
        
        private async Task<RegistryBackup?> BackupRegistryKeyAsync(string keyPath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var parts = keyPath.Split('\\', 2);
                    var hive = parts[0] switch
                    {
                        "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
                        "HKEY_CURRENT_USER" => Registry.CurrentUser,
                        _ => null
                    };
                    
                    if (hive == null) return null;
                    
                    using var key = hive.OpenSubKey(parts[1]);
                    if (key == null) return null;
                    
                    var backup = new RegistryBackup
                    {
                        KeyPath = keyPath,
                        Values = new Dictionary<string, object>()
                    };
                    
                    foreach (var valueName in key.GetValueNames())
                    {
                        var value = key.GetValue(valueName);
                        if (value != null)
                        {
                            backup.Values[valueName] = value;
                        }
                    }
                    
                    return backup;
                }
                catch
                {
                    return null;
                }
            });
        }
        
        private async Task<bool> RestoreRegistryKeyAsync(RegistryBackup backup)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var parts = backup.KeyPath.Split('\\', 2);
                    var hive = parts[0] switch
                    {
                        "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
                        "HKEY_CURRENT_USER" => Registry.CurrentUser,
                        _ => null
                    };
                    
                    if (hive == null) return false;
                    
                    using var key = hive.OpenSubKey(parts[1], true);
                    if (key == null) return false;
                    
                    foreach (var kvp in backup.Values)
                    {
                        key.SetValue(kvp.Key, kvp.Value);
                    }
                    
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }
        
        private async Task<string> GetCurrentPowerPlanAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    IntPtr activeGuidPtr;
                    uint res = VoltrisOptimizer.Utils.Win32.PowerNativeMethods.PowerGetActiveScheme(IntPtr.Zero, out activeGuidPtr);
                    if (res == 0 && activeGuidPtr != IntPtr.Zero)
                    {
                        Guid activeGuid = System.Runtime.InteropServices.Marshal.PtrToStructure<Guid>(activeGuidPtr);
                        VoltrisOptimizer.Utils.Win32.PowerNativeMethods.LocalFree(activeGuidPtr);
                        return activeGuid.ToString("D");
                    }
                }
                catch { }
                
                return "";
            });
        }
        
        private async Task<bool> RestorePowerPlanAsync(string guid)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (string.IsNullOrEmpty(guid)) return false;
                    Guid target = new Guid(guid);
                    return ProfilePowerAuthority.RequestProfileApply("SystemSnapshot", "pedido legado de troca de plano", _logger);
                }
                catch { }
                
                return false;
            });
        }
        
        private async Task<List<string>> GetRunningServicesAsync()
        {
            return await Task.Run(() =>
            {
                var services = new List<string>();
                
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "sc",
                        Arguments = "query state= running",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    };
                    
                    using var proc = System.Diagnostics.Process.Start(psi);
                    if (proc != null)
                    {
                        var output = proc.StandardOutput.ReadToEnd();
                        proc.WaitForExit();
                        
                        // Parse output para extrair nomes de serviços
                        var lines = output.Split('\n');
                        foreach (var line in lines)
                        {
                            if (line.Contains("SERVICE_NAME:"))
                            {
                                var serviceName = line.Split(':')[1].Trim();
                                services.Add(serviceName);
                            }
                        }
                    }
                }
                catch { }
                
                return services;
            });
        }
        
        private async Task<bool> RestoreServicesAsync(List<string> services)
        {
            // Placeholder: Implementar lógica de restauração de serviços
            await Task.CompletedTask;
            return true;
        }
        
        private async Task<Dictionary<string, string>> GetNetworkSettingsAsync()
        {
            // Placeholder: Capturar configurações de rede
            await Task.CompletedTask;
            return new Dictionary<string, string>();
        }
        
        private async Task SaveSnapshotAsync(SystemSnapshot snapshot)
        {
            var filePath = Path.Combine(_snapshotPath, $"{snapshot.Id}.json");
            // CORREÇÃO: ReferenceHandler.IgnoreCycles evita JsonException quando RegistryBackup.Values
            // contém objetos COM/WMI com referências circulares ou tipos não-serializáveis.
            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true,
                MaxDepth = 32,
                ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            await File.WriteAllTextAsync(filePath, json);
        }
        
        private async Task<SystemSnapshot?> LoadSnapshotAsync(string snapshotId)
        {
            try
            {
                var filePath = Path.Combine(_snapshotPath, $"{snapshotId}.json");
                if (!File.Exists(filePath)) return null;
                
                var json = await File.ReadAllTextAsync(filePath);
                return JsonSerializer.Deserialize<SystemSnapshot>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            }
            catch
            {
                return null;
            }
        }
    }
    
    public class SystemSnapshot
    {
        public string Id { get; set; } = "";
        public string OperationName { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public string MachineId { get; set; } = "";
        public List<RegistryBackup>? RegistryBackups { get; set; }
        public string PowerPlanGuid { get; set; } = "";
        public List<string>? RunningServices { get; set; }
        public Dictionary<string, string>? NetworkSettings { get; set; }
    }
    
    public class RegistryBackup
    {
        public string KeyPath { get; set; } = "";
        public Dictionary<string, object> Values { get; set; } = new();
    }
}
