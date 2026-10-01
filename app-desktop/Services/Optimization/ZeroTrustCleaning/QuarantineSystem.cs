using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Optimization.ZeroTrustCleaning
{
    public class QuarantineSystem : IQuarantineSystem
    {
        private readonly ILoggingService _logger;
        private readonly string _quarantineDir;
        private readonly string _quarantineIndexFile;

        // Map do OriginalPath -> Caminho na Quarentena
        private Dictionary<string, QuarantineEntry> _index = new();

        public QuarantineSystem(ILoggingService logger)
        {
            _logger = logger;
            _quarantineDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Voltris", "Quarantine");
            _quarantineIndexFile = Path.Combine(_quarantineDir, "quarantine_index.json");

            EnsureDirectoryExists();
            LoadIndex();
        }

        private void EnsureDirectoryExists()
        {
            if (!Directory.Exists(_quarantineDir))
            {
                Directory.CreateDirectory(_quarantineDir);
            }
        }

        private void LoadIndex()
        {
            try
            {
                if (File.Exists(_quarantineIndexFile))
                {
                    string json = File.ReadAllText(_quarantineIndexFile);
                    _index = JsonSerializer.Deserialize<Dictionary<string, QuarantineEntry>>(json) ?? new Dictionary<string, QuarantineEntry>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[Quarantine] Erro ao carregar índice: {ex.Message}");
                _index = new Dictionary<string, QuarantineEntry>();
            }
        }

        private void SaveIndex()
        {
            try
            {
                string json = JsonSerializer.Serialize(_index, new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,  WriteIndented = true });
                File.WriteAllText(_quarantineIndexFile, json);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[Quarantine] Erro ao salvar índice: {ex.Message}");
            }
        }

        public async Task<bool> MoveToQuarantineAsync(MutableNode node)
        {
            if (node.Type != NodeType.File || string.IsNullOrEmpty(node.Path))
                return false;

            return await Task.Run(() =>
            {
                try
                {
                    if (!File.Exists(node.Path)) return false;

                    string quarantineFileName = Guid.NewGuid().ToString() + Path.GetExtension(node.Path);
                    string quarantineFilePath = Path.Combine(_quarantineDir, quarantineFileName);

                    // Verifica se está travado
                    using (new FileStream(node.Path, FileMode.Open, FileAccess.Read, FileShare.None)) {}

                    File.Copy(node.Path, quarantineFilePath, true);
                    
                    // Exclusão original (nível de reciclável inicial)
                    File.Delete(node.Path);

                    _index[node.Path] = new QuarantineEntry
                    {
                        OriginalPath = node.Path,
                        QuarantinePath = quarantineFilePath,
                        QuarantinedAt = DateTime.Now,
                        ExpiresAt = DateTime.Now.AddHours(48),
                        SizeBytes = node.SizeBytes
                    };

                    SaveIndex();
                    
                    _logger.LogInfo($"[Quarantine] Isolado: {node.Path}");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[Quarantine] Falha ao enviar para quarentena {node.Path}: {ex.Message}");
                    return false;
                }
            });
        }

        public async Task<bool> RestoreFromQuarantineAsync(string originalPath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (!_index.TryGetValue(originalPath, out var entry))
                    {
                        _logger.LogWarning($"[Quarantine] Item não encontrado no índice: {originalPath}");
                        return false;
                    }

                    if (!File.Exists(entry.QuarantinePath))
                    {
                        _logger.LogWarning($"[Quarantine] Arquivo em quarentena não existe mais: {entry.QuarantinePath}");
                        _index.Remove(originalPath);
                        SaveIndex();
                        return false;
                    }

                    var dir = Path.GetDirectoryName(originalPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    File.Copy(entry.QuarantinePath, originalPath, true);
                    File.Delete(entry.QuarantinePath);

                    _index.Remove(originalPath);
                    SaveIndex();

                    _logger.LogSuccess($"[Quarantine] Restaurado com sucesso: {originalPath}");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[Quarantine] Falha ao restaurar {originalPath}: {ex.Message}");
                    return false;
                }
            });
        }

        public async Task PurgeExpiredItemsAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    var toRemove = new List<string>();
                    var now = DateTime.Now;

                    foreach (var kvp in _index)
                    {
                        if (now >= kvp.Value.ExpiresAt)
                        {
                            try
                            {
                                if (File.Exists(kvp.Value.QuarantinePath))
                                {
                                    File.Delete(kvp.Value.QuarantinePath);
                                }
                                toRemove.Add(kvp.Key);
                            }
                            catch (Exception itemEx)
                            {
                                _logger.LogWarning($"[Quarantine] Erro ao expurgar item {kvp.Key}: {itemEx.Message}");
                            }
                        }
                    }

                    if (toRemove.Count > 0)
                    {
                        foreach (var key in toRemove)
                        {
                            _index.Remove(key);
                        }
                        SaveIndex();
                        _logger.LogInfo($"[Quarantine] {toRemove.Count} itens expirados foram purgados.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[Quarantine] Erro global no purgo: {ex.Message}");
                }
            });
        }
    }

    public class QuarantineEntry
    {
        public string OriginalPath { get; set; } = string.Empty;
        public string QuarantinePath { get; set; } = string.Empty;
        public DateTime QuarantinedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public long SizeBytes { get; set; }
    }
}
