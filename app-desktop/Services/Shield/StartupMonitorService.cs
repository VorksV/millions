using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Shield
{
    public class StartupMonitorService
    {
        private readonly ILoggingService _logger;
        private readonly IRegistryService _registry;
        private readonly SecurityLogService _securityLog;
        
        private CancellationTokenSource? _monitoringCts;
        private Task? _monitoringTask;
        private bool _isMonitoring;
        private bool _lowActivityMode;
        private Dictionary<string, string> _lastKnownStartupEntries = new();
        
        // Intervalo de polling para detectar mudanças no registro (30s normal, 120s gamer)
        private const int NORMAL_POLL_INTERVAL_MS = 30_000;
        private const int GAMER_POLL_INTERVAL_MS = 120_000;
        
        public event EventHandler<StartupChangeDetectedEventArgs>? StartupChangeDetected;
        
        private readonly string[] _startupRegistryKeys = new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"
        };
        
        // Whitelist de publishers/nomes legítimos para reduzir falsos positivos
        private readonly string[] _trustedPublishers = new[]
        {
            "microsoft", "windows", "realtek", "intel", "nvidia", "amd",
            "google chrome", "steam", "discord", "spotify", "onedrive",
            "security health", "windowsdefender", "cortana"
        };
        
        public StartupMonitorService(ILoggingService logger, IRegistryService registry, SecurityLogService securityLog)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _securityLog = securityLog ?? throw new ArgumentNullException(nameof(securityLog));
        }
        
        public async Task StartMonitoringAsync()
        {
            if (_isMonitoring) return;
            
            _logger.LogInfo("[StartupMonitor] Iniciando monitoramento contínuo de inicialização...");
            _securityLog.LogSecurityEvent("StartupMonitor", "MONITORING_STARTED", "Startup monitoring enabled with registry polling");
            
            // Capturar snapshot inicial
            await CaptureStartupSnapshotAsync();
            
            var monitoringCts = new CancellationTokenSource();
            _monitoringCts = monitoringCts;
            _isMonitoring = true;

            _monitoringTask = Task.Run(async () =>
            {
                try
                {
                    VoltrisOptimizer.Services.Diagnostics.ThreadNameRegistry.Register("Shield-StartupMonitor");
                    await MonitoringLoopAsync(monitoringCts.Token);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogError("[StartupMonitor] Erro fatal no loop de monitoramento", ex);
                }
            }, CancellationToken.None);
        }
        
        public async Task StopMonitoringAsync()
        {
            if (!_isMonitoring && _monitoringTask == null)
                return;

            var monitoringCts = _monitoringCts;
            var monitoringTask = _monitoringTask;
            _monitoringCts = null;
            _monitoringTask = null;

            try
            {
                _logger.LogInfo("[StartupMonitor] Parando monitoramento de inicialização...");
                monitoringCts?.Cancel();
                if (monitoringTask != null)
                    await monitoringTask;

                _isMonitoring = false;
                _securityLog.LogSecurityEvent("StartupMonitor", "MONITORING_STOPPED", "Startup monitoring disabled");
            }
            catch (Exception ex)
            {
                _logger.LogError("[StartupMonitor] Erro ao parar monitoramento", ex);
            }
            finally
            {
                monitoringCts?.Dispose();
            }
        }
        
        public void SetLowActivityMode(bool enabled)
        {
            _lowActivityMode = enabled;
            _logger.LogInfo($"[StartupMonitor] Modo baixa atividade: {enabled}");
        }
        
        private async Task MonitoringLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var interval = _lowActivityMode ? GAMER_POLL_INTERVAL_MS : NORMAL_POLL_INTERVAL_MS;
                    await Task.Delay(interval, ct);
                    
                    if (ct.IsCancellationRequested) break;
                    
                    await DetectStartupChangesAsync();
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogError("[StartupMonitor] Erro no loop de monitoramento", ex);
                    try { await Task.Delay(5000, ct); } catch { break; }
                }
            }
        }
        
        private async Task CaptureStartupSnapshotAsync()
        {
            var items = await GetAllStartupItemsAsync();
            _lastKnownStartupEntries.Clear();
            foreach (var item in items)
            {
                var key = $"{item.RegistryHive}|{item.RegistryPath}|{item.Name}";
                _lastKnownStartupEntries[key] = item.Path;
            }
        }
        
        private async Task DetectStartupChangesAsync()
        {
            var currentItems = await GetAllStartupItemsAsync();
            var currentEntries = new Dictionary<string, string>();
            
            foreach (var item in currentItems)
            {
                var key = $"{item.RegistryHive}|{item.RegistryPath}|{item.Name}";
                currentEntries[key] = item.Path;
                
                // Detectar novas entradas
                if (!_lastKnownStartupEntries.ContainsKey(key))
                {
                    // Não alertar sobre entradas do próprio Voltris
                    bool isVoltrisEntry = item.Name != null && 
                        (item.Name.Contains("Voltris", StringComparison.OrdinalIgnoreCase) ||
                         (item.Path != null && item.Path.Contains("Voltris", StringComparison.OrdinalIgnoreCase)));
                    
                    if (isVoltrisEntry)
                    {
                        _logger.LogInfo($"[StartupMonitor] Entrada Voltris registrada: {item.Name} -> {item.Path}");
                    }
                    else
                    {
                        _logger.LogWarning($"[StartupMonitor] Nova entrada de startup detectada: {item.Name} -> {item.Path}");
                    }
                    _securityLog.LogStartupChange("NEW_ENTRY_DETECTED", item.Name, item.Path);
                    
                    StartupChangeDetected?.Invoke(this, new StartupChangeDetectedEventArgs
                    {
                        ChangeType = "ADDED",
                        ItemName = item.Name,
                        ItemPath = item.Path,
                        IsSuspicious = item.IsSuspicious
                    });
                }
            }
            
            // Detectar entradas removidas
            foreach (var kvp in _lastKnownStartupEntries)
            {
                if (!currentEntries.ContainsKey(kvp.Key))
                {
                    _logger.LogInfo($"[StartupMonitor] Entrada de startup removida: {kvp.Key}");
                    _securityLog.LogStartupChange("ENTRY_REMOVED", kvp.Key, kvp.Value);
                }
            }
            
            _lastKnownStartupEntries = currentEntries;
        }
        
        public async Task<List<StartupItem>> GetAllStartupItemsAsync()
        {
            var items = new List<StartupItem>();
            try
            {
                using var _ = VoltrisOptimizer.Services.Diagnostics.CpuSelfProfiler.Instance.BeginSection("StartupMonitor.Scan");
                // Registry HKLM
                items.AddRange(await GetRegistryStartupItemsAsync(RegistryHive.LocalMachine));
                // Registry HKCU
                items.AddRange(await GetRegistryStartupItemsAsync(RegistryHive.CurrentUser));
                // Startup folders
                items.AddRange(GetStartupFolderItems());
            }
            catch (Exception ex)
            {
                _logger.LogError("[StartupMonitor] Erro ao obter itens", ex);
            }
            return items;
        }
        
        public async Task<int> ScanStartupItemsAsync()
        {
            var items = await GetAllStartupItemsAsync();
            return items.Count(i => i.IsSuspicious);
        }
        
        public async Task<bool> DisableStartupItemAsync(StartupItem item)
        {
            try
            {
                if (item.Location == StartupLocation.Registry)
                {
                    return await DisableRegistryStartupAsync(item);
                }
                else if (item.Location == StartupLocation.StartupFolder)
                {
                    return DisableStartupFolderItem(item);
                }
                
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[StartupMonitor] Erro ao desativar {item.Name}", ex);
                return false;
            }
        }
        
        public async Task<bool> RemoveStartupItemAsync(StartupItem item)
        {
            try
            {
                if (item.Location == StartupLocation.Registry)
                {
                    return await RemoveRegistryStartupAsync(item);
                }
                else if (item.Location == StartupLocation.StartupFolder)
                {
                    return RemoveStartupFolderItem(item);
                }
                
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[StartupMonitor] Erro ao remover {item.Name}", ex);
                return false;
            }
        }
        
        private async Task<List<StartupItem>> GetRegistryStartupItemsAsync(RegistryHive hive)
        {
            var items = new List<StartupItem>();
            
            await Task.Run(() =>
            {
                try
                {
                    using var baseKey = hive == RegistryHive.LocalMachine 
                        ? RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                        : RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
                    
                    foreach (var keyPath in _startupRegistryKeys)
                    {
                        try
                        {
                            using var key = baseKey.OpenSubKey(keyPath);
                            if (key == null) continue;
                            
                            foreach (var valueName in key.GetValueNames())
                            {
                                var value = key.GetValue(valueName)?.ToString() ?? string.Empty;
                                bool isDisabled = valueName.StartsWith("_DISABLED_", StringComparison.OrdinalIgnoreCase);

                                string reason = string.Empty;
                                var isSuspicious = !isDisabled && EvaluateSuspiciousStartupItem(valueName, value, out reason);

                                var item = new StartupItem
                                {
                                    Name = valueName,
                                    Path = value,
                                    Location = StartupLocation.Registry,
                                    RegistryHive = hive,
                                    RegistryPath = keyPath,
                                    IsEnabled = !isDisabled,
                                    IsSuspicious = isSuspicious,
                                    SuspiciousReason = reason
                                };

                                LogSuspiciousOnce(item);

                                items.Add(item);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"[StartupMonitor] Erro ao ler {keyPath}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[StartupMonitor] Erro ao acessar registry {hive}", ex);
                }
            });
            
            return items;
        }
        
        private List<StartupItem> GetStartupFolderItems()
        {
            var items = new List<StartupItem>();
            
            try
            {
                var startupFolders = new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup))
                };
                
                foreach (var folder in startupFolders)
                {
                    if (!Directory.Exists(folder)) continue;
                    
                    var files = Directory.GetFiles(folder, "*.*", SearchOption.TopDirectoryOnly);
                    
                    foreach (var file in files)
                    {
                        var isSuspicious = EvaluateSuspiciousStartupItem(Path.GetFileName(file), file, out var folderReason);

                        var item = new StartupItem
                        {
                            Name = Path.GetFileNameWithoutExtension(file),
                            Path = file,
                            Location = StartupLocation.StartupFolder,
                            IsEnabled = true,
                            IsSuspicious = isSuspicious,
                            SuspiciousReason = folderReason
                        };

                        LogSuspiciousOnce(item);

                        items.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("[StartupMonitor] Erro ao ler pastas de inicialização", ex);
            }
            
            return items;
        }
        
        /// <summary>
        /// Itens ja reportados como suspeitos, para nao repetir o MESMO aviso a
        /// cada varredura (o monitor roda a cada ~30s e uma entrada orfa de
        /// autostart gerava um WARNING idêntico para sempre, sem nenhuma
        /// informação nova). A chave inclui o motivo, então se o estado mudar
        /// (ex.: o executavel passa a existir) o item volta a ser reportado.
        /// </summary>
        private readonly HashSet<string> _suspiciousJaReportados = new(StringComparer.Ordinal);

        private void LogSuspiciousOnce(StartupItem item)
        {
            if (!item.IsSuspicious) return;

            string chave = $"{item.Location}|{item.Name}|{item.Path}|{item.SuspiciousReason}";
            if (!_suspiciousJaReportados.Add(chave)) return;

            _logger.LogWarning($"[StartupMonitor] Item de inicialização suspeito: {item.Name} -> {item.Path} ({item.SuspiciousReason})");
        }

        private bool EvaluateSuspiciousStartupItem(string name, string path, out string reason)
        {
            var nameLower = name.ToLowerInvariant();
            var pathLower = path.ToLowerInvariant();

            if (_trustedPublishers.Any(tp => nameLower.Contains(tp) || pathLower.Contains(tp)))
            {
                reason = string.Empty;
                return false;
            }

            var executablePath = ExtractExecutablePath(path);
            var executableMissing = !string.IsNullOrEmpty(executablePath) && !File.Exists(executablePath);

            var highConfidenceKeywords = new[] { "toolbar", "adware", "browser helper", "conduit", "babylon", "mindspark", "searchprotect" };
            var keyword = highConfidenceKeywords.FirstOrDefault(k => nameLower.Contains(k) || pathLower.Contains(k));
            if (keyword != null)
            {
                reason = $"nome suspeito ('{keyword}')";
                return true;
            }

            if (pathLower.Contains(@"\temp\") || pathLower.Contains(@"\appdata\local\temp\"))
            {
                reason = "executável em pasta temporária";
                return true;
            }

            if (executableMissing)
            {
                reason = $"executável não encontrado: {executablePath}";
                return true;
            }

            reason = string.Empty;
            return false;
        }

        private string ExtractExecutablePath(string commandLine)
        {
            if (string.IsNullOrWhiteSpace(commandLine)) return string.Empty;

            var trimmed = commandLine.Trim();

            if (trimmed.StartsWith("\""))
            {
                var endQuote = trimmed.IndexOf('"', 1);
                if (endQuote > 0) return trimmed.Substring(1, endQuote - 1);
            }

            if (File.Exists(trimmed))
            {
                return trimmed;
            }

            // Tenta os prefixos do MAIS LONGO para o mais curto, para que o executavel
            // real seja encontrado mesmo em caminhos com espacos
            // (ex.: "C:\Riot Games\Riot Client\RiotClientServices.exe --launch-background-mode").
            var firstSpace = trimmed.IndexOf(' ');
            if (firstSpace > 0)
            {
                for (var i = trimmed.LastIndexOf(' '); i > 0; i = trimmed.LastIndexOf(' ', i - 1))
                {
                    var candidate = trimmed.Substring(0, i);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            // Nenhum prefixo existe no disco. Ainda assim devolvemos o melhor
            // palpite de executavel: o prefixo MAIS LONGO que termine em extensao
            // de executavel (.exe/.com/.bat/.cmd/.ps1).
            //
            // Antes devolvia a linha de comando inteira, o que quebrava duas coisas:
            //  1. File.Exists() falhava sempre, porque o caminho incluía os argumentos;
            //  2. a mensagem ficava enganosa, apontando para um caminho impossivel:
            //     "executavel nao encontrado: C:\Riot Games\...\RiotClientServices.exe --launch-background-mode"
            // Truncar no primeiro espaco tambem nao servia, pois geraria "C:\Riot".
            // Usar o prefixo mais longo que termina em extensao de executavel
            // resolve os dois casos de uma vez.
            var execExtensions = new[] { ".exe", ".com", ".bat", ".cmd", ".ps1" };
            for (var i = trimmed.LastIndexOf(' '); i > 0; i = trimmed.LastIndexOf(' ', i - 1))
            {
                var candidate = trimmed.Substring(0, i);
                if (execExtensions.Any(ext => candidate.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                {
                    return candidate;
                }
            }

            // Sem extensao reconhecivel: devolve a linha de comando completa em vez
            // de truncar no primeiro espaco, para nao inventar "C:\Riot".
            return trimmed;
        }
        
        private async Task<bool> DisableRegistryStartupAsync(StartupItem item)
        {
            try
            {
                // Backup do valor original antes de desativar
                var backup = _registry.BackupValue(item.RegistryHive, item.RegistryPath, item.Name);
                
                // Ler o valor atual
                var currentValue = _registry.GetValue<string>(item.RegistryHive, item.RegistryPath, item.Name);
                if (currentValue == null)
                {
                    _logger.LogWarning($"[StartupMonitor] Valor não encontrado para desativar: {item.Name}");
                    return false;
                }
                
                // Estratégia: remover o valor original e criar um com prefixo _DISABLED_
                // Isso é o mesmo padrão usado pelo Autoruns da Microsoft
                var deleteResult = _registry.DeleteValue(item.RegistryHive, item.RegistryPath, item.Name);
                if (!deleteResult.Success)
                {
                    _logger.LogWarning($"[StartupMonitor] Falha ao deletar valor original: {deleteResult.Message}");
                    return false;
                }
                
                var disabledName = $"_DISABLED_{item.Name}";
                var setResult = _registry.SetValue(item.RegistryHive, item.RegistryPath, disabledName, currentValue, RegistryValueKind.String);
                if (!setResult.Success)
                {
                    // Rollback — restaurar o valor original
                    _registry.RestoreValue(backup);
                    _logger.LogWarning($"[StartupMonitor] Falha ao criar valor desativado, rollback executado: {setResult.Message}");
                    return false;
                }
                
                _securityLog.LogStartupChange("DISABLED", item.Name, item.Path);
                _logger.LogSuccess($"[StartupMonitor] Item desativado com sucesso: {item.Name}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[StartupMonitor] Erro ao desativar {item.Name}", ex);
                return false;
            }
        }
        
        private async Task<bool> RemoveRegistryStartupAsync(StartupItem item)
        {
            try
            {
                // Backup antes de remover
                _registry.BackupValue(item.RegistryHive, item.RegistryPath, item.Name);
                
                var result = _registry.DeleteValue(item.RegistryHive, item.RegistryPath, item.Name);
                if (!result.Success)
                {
                    _logger.LogWarning($"[StartupMonitor] Falha ao remover valor do registro: {result.Message}");
                    return false;
                }
                
                _securityLog.LogStartupChange("REMOVED", item.Name, item.Path);
                _logger.LogSuccess($"[StartupMonitor] Item removido do registro com sucesso: {item.Name}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[StartupMonitor] Erro ao remover {item.Name}", ex);
                return false;
            }
        }
        
        private bool DisableStartupFolderItem(StartupItem item)
        {
            try
            {
                var newPath = item.Path + ".disabled";
                File.Move(item.Path, newPath);
                
                _securityLog.LogStartupChange("DISABLED", item.Name, item.Path);
                _logger.LogInfo($"[StartupMonitor] Item desativado: {item.Name}");
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[StartupMonitor] Erro ao desativar {item.Name}", ex);
                return false;
            }
        }
        
        private bool RemoveStartupFolderItem(StartupItem item)
        {
            try
            {
                File.Delete(item.Path);
                
                _securityLog.LogStartupChange("REMOVED", item.Name, item.Path);
                _logger.LogInfo($"[StartupMonitor] Item removido: {item.Name}");
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[StartupMonitor] Erro ao remover {item.Name}", ex);
                return false;
            }
        }
    }
    
    public class StartupItem
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public StartupLocation Location { get; set; }
        public RegistryHive RegistryHive { get; set; }
        public string RegistryPath { get; set; }
        public bool IsEnabled { get; set; }
        public bool IsSuspicious { get; set; }
        public string SuspiciousReason { get; set; } = string.Empty;
        public string Publisher { get; set; } = "Desconhecido";
    }
    
    public enum StartupLocation
    {
        Registry,
        StartupFolder
    }
    
    public class StartupChangeDetectedEventArgs : EventArgs
    {
        public string ChangeType { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string ItemPath { get; set; } = string.Empty;
        public bool IsSuspicious { get; set; }
    }
}
