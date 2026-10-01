using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Shield
{
    /// <summary>
    /// QuarantineService — Isolamento seguro de ameaças.
    /// Move arquivos para pasta protegida, remove permissão de execução,
    /// registra metadados e permite restauração ou exclusão definitiva.
    /// Comportamento similar ao Windows Defender Quarantine.
    /// </summary>
    public class QuarantineService
    {
        private readonly ILoggingService _logger;
        private readonly SecurityLogService _securityLog;
        private readonly string _quarantineDir;
        private readonly string _metadataFile;
        private readonly object _lock = new();
        private List<QuarantineEntry> _entries = new();

        public event EventHandler<QuarantineEntry>? ItemQuarantined;
        public event EventHandler<QuarantineEntry>? ItemRestored;
        public event EventHandler<QuarantineEntry>? ItemDeleted;

        public QuarantineService(ILoggingService logger, SecurityLogService securityLog)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _securityLog = securityLog ?? throw new ArgumentNullException(nameof(securityLog));

            _quarantineDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Voltris", "Quarantine");
            _metadataFile = Path.Combine(_quarantineDir, "quarantine_index.json");

            Initialize();
        }

        private bool IsPathWithinQuarantine(string path)
        {
            try
            {
                var root = Path.GetFullPath(_quarantineDir)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                var fullPath = Path.GetFullPath(path);
                return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private bool IsSafeOriginalPath(string path)
        {
            try
            {
                var fullPath = Path.GetFullPath(path);
                return !string.IsNullOrWhiteSpace(fullPath)
                    && !IsPathWithinQuarantine(fullPath)
                    && !string.Equals(fullPath, _quarantineDir, StringComparison.OrdinalIgnoreCase)
                    && !ContainsReparsePoint(fullPath);
            }
            catch
            {
                return false;
            }
        }

        private bool IsSafeRestorePath(string path, string expectedName)
        {
            if (!IsSafeOriginalPath(path) || IsProtectedSystemPath(path))
                return false;

            try
            {
                return string.Equals(Path.GetFileName(Path.GetFullPath(path)), expectedName, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsProtectedSystemPath(string path)
        {
            try
            {
                var fullPath = Path.GetFullPath(path);
                var protectedRoots = new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
                };

                return protectedRoots.Where(root => !string.IsNullOrWhiteSpace(root))
                    .Any(root => IsSameOrChildPath(fullPath, root));
            }
            catch
            {
                return true;
            }
        }

        private static bool IsSameOrChildPath(string path, string root)
        {
            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullPath = Path.GetFullPath(path);
            return string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase)
                || fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsReparsePoint(string path)
        {
            try
            {
                var current = Path.GetFullPath(path);
                while (!string.IsNullOrWhiteSpace(current))
                {
                    if (File.Exists(current) || Directory.Exists(current))
                    {
                        if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                            return true;
                    }

                    var parent = Path.GetDirectoryName(current);
                    if (string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                        break;
                    current = parent ?? string.Empty;
                }

                return false;
            }
            catch
            {
                return true;
            }
        }

        private bool IsValidEntry(QuarantineEntry entry)
        {
            try
            {
                var quarantinePath = Path.GetFullPath(entry.QuarantinePath);
                var root = Path.GetFullPath(_quarantineDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var expectedName = $"{entry.Id}.qvault";
                return !string.IsNullOrWhiteSpace(entry.Id)
                    && !string.IsNullOrWhiteSpace(entry.SHA256)
                    && IsPathWithinQuarantine(quarantinePath)
                    && string.Equals(Path.GetDirectoryName(quarantinePath), root, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(Path.GetFileName(quarantinePath), expectedName, StringComparison.OrdinalIgnoreCase)
                    && !ContainsReparsePoint(quarantinePath)
                    && IsSafeOriginalPath(entry.OriginalPath);
            }
            catch
            {
                return false;
            }
        }

        private void TrySecureDirectory()
        {
            try
            {
                var security = new DirectorySecurity();
                security.SetAccessRuleProtection(true, false);
                var inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
                security.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                    FileSystemRights.FullControl,
                    inheritance,
                    PropagationFlags.None,
                    AccessControlType.Allow));
                security.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                    FileSystemRights.FullControl,
                    inheritance,
                    PropagationFlags.None,
                    AccessControlType.Allow));
                new DirectoryInfo(_quarantineDir).SetAccessControl(security);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Quarantine] Não foi possível aplicar ACL protetora: {ex.Message}");
            }
        }

        private void Initialize()
        {
            try
            {
                if (!Directory.Exists(_quarantineDir))
                {
                    Directory.CreateDirectory(_quarantineDir);
                    _logger.LogInfo($"[Quarantine] Diretório criado: {_quarantineDir}");
                }

                TrySecureDirectory();
                try
                {
                    var dirInfo = new DirectoryInfo(_quarantineDir);
                    dirInfo.Attributes |= FileAttributes.Hidden;
                    _logger.LogInfo("[Quarantine] Pasta de quarentena marcada como oculta");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[Quarantine] Não foi possível ocultar pasta: {ex.Message}");
                }

                LoadMetadata();
                _logger.LogSuccess($"[Quarantine] Inicializado com {_entries.Count} itens em quarentena");
            }
            catch (Exception ex)
            {
                _logger.LogError("[Quarantine] Erro na inicialização", ex);
            }
        }

        /// <summary>
        /// Move um arquivo para quarentena. O arquivo é renomeado com hash,
        /// metadados são registrados e permissões de execução removidas.
        /// </summary>
        public async Task<bool> QuarantineFileAsync(string filePath, string reason, ThreatSeverity severity)
        {
            try
            {
                var fullSourcePath = Path.GetFullPath(filePath);
                if (!IsSafeOriginalPath(fullSourcePath) || !File.Exists(fullSourcePath))
                {
                    _logger.LogWarning($"[Quarantine] Arquivo inválido ou não encontrado: {filePath}");
                    return false;
                }

                var fileInfo = new FileInfo(fullSourcePath);
                var hash = await ComputeHashAsync(fullSourcePath);
                if (string.IsNullOrWhiteSpace(hash) || hash == "hash_error")
                {
                    _logger.LogWarning($"[Quarantine] Não foi possível calcular o hash: {filePath}");
                    return false;
                }
                var quarantineId = Guid.NewGuid().ToString("N")[..12];
                var quarantineName = $"{quarantineId}.qvault";
                var quarantinePath = Path.Combine(_quarantineDir, quarantineName);

                _logger.LogInfo($"[Quarantine] ══════════════════════════════════════════");
                _logger.LogInfo($"[Quarantine] QUARENTENA INICIADA");
                _logger.LogInfo($"[Quarantine] Arquivo: {filePath}");
                _logger.LogInfo($"[Quarantine] SHA256: {hash}");
                _logger.LogInfo($"[Quarantine] Tamanho: {fileInfo.Length} bytes");
                _logger.LogInfo($"[Quarantine] Razão: {reason}");
                _logger.LogInfo($"[Quarantine] Severidade: {severity}");

                try
                {
                    File.Move(fullSourcePath, quarantinePath, overwrite: false);
                }
                catch (IOException ex)
                {
                    _logger.LogWarning($"[Quarantine] Arquivo bloqueado ou indisponível; nenhuma sessão foi encerrada: {fullSourcePath}. {ex.Message}");
                    return false;
                }
                catch (UnauthorizedAccessException ex)
                {
                    _logger.LogWarning($"[Quarantine] Acesso negado ao mover arquivo: {fullSourcePath}. {ex.Message}");
                    return false;
                }
                
                _logger.LogInfo($"[Quarantine] Arquivo movido para: {quarantinePath}");

                // Renomear arquivo em quarentena para extensão inerte (impede execução acidental)
                _logger.LogInfo("[Quarantine] Arquivo renomeado com extensão .qvault (inerte)");

                // Registrar metadados
                var entry = new QuarantineEntry
                {
                    Id = quarantineId,
                    OriginalPath = fullSourcePath,
                    OriginalName = fileInfo.Name,
                    QuarantinePath = quarantinePath,
                    SHA256 = hash,
                    SizeBytes = fileInfo.Length,
                    Reason = reason,
                    Severity = severity,
                    QuarantinedAt = DateTime.UtcNow,
                    OriginalCreationTime = fileInfo.CreationTimeUtc,
                    OriginalLastWriteTime = fileInfo.LastWriteTimeUtc
                };

                lock (_lock)
                {
                    _entries.Add(entry);
                    if (!SaveMetadata())
                    {
                        _entries.Remove(entry);
                        try
                        {
                            if (File.Exists(quarantinePath) && !File.Exists(fullSourcePath))
                            {
                                File.Move(quarantinePath, fullSourcePath, overwrite: false);
                            }
                        }
                        catch (Exception rollbackException)
                        {
                            _logger.LogError($"[Quarantine] Falha ao reverter move após erro de índice: {rollbackException.Message}");
                        }
                        return false;
                    }
                }

                _securityLog.LogSecurityEvent("Quarantine", "FILE_QUARANTINED",
                    $"File: {filePath} | Hash: {hash} | Reason: {reason} | Severity: {severity}");

                _logger.LogSuccess($"[Quarantine] ✅ Arquivo em quarentena: {entry.OriginalName} (ID: {quarantineId})");
                _logger.LogInfo($"[Quarantine] ══════════════════════════════════════════");

                ItemQuarantined?.Invoke(this, entry);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[Quarantine] Erro ao colocar em quarentena: {filePath}", ex);
                return false;
            }
        }

        /// <summary>
        /// Restaura um arquivo da quarentena para o local original.
        /// </summary>
        public async Task<bool> RestoreFileAsync(string quarantineId)
        {
            try
            {
                QuarantineEntry? entry;
                lock (_lock)
                {
                    entry = _entries.FirstOrDefault(e => e.Id == quarantineId);
                }

                if (entry == null)
                {
                    _logger.LogWarning($"[Quarantine] ID não encontrado: {quarantineId}");
                    return false;
                }

                if (!IsValidEntry(entry) || !File.Exists(entry.QuarantinePath))
                {
                    _logger.LogWarning($"[Quarantine] Entrada inválida ou arquivo ausente: {quarantineId}");
                    return false;
                }

                if (!IsSafeRestorePath(entry.OriginalPath, entry.OriginalName))
                {
                    _logger.LogWarning($"[Quarantine] Destino de restauração bloqueado por política de segurança: {entry.OriginalPath}");
                    return false;
                }

                var currentHash = await ComputeHashAsync(entry.QuarantinePath);
                if (!string.Equals(currentHash, entry.SHA256, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning($"[Quarantine] Hash divergente; restauração cancelada: {entry.OriginalName}");
                    return false;
                }

                _logger.LogInfo($"[Quarantine] Restaurando: {entry.OriginalName} -> {entry.OriginalPath}");

                var destinationPath = Path.GetFullPath(entry.OriginalPath);
                if (File.Exists(destinationPath))
                {
                    _logger.LogWarning($"[Quarantine] Destino já existe; restauração cancelada: {destinationPath}");
                    return false;
                }

                var destDir = Path.GetDirectoryName(destinationPath);
                if (string.IsNullOrWhiteSpace(destDir) || ContainsReparsePoint(destDir))
                {
                    _logger.LogWarning($"[Quarantine] Diretório de destino inválido: {destDir}");
                    return false;
                }

                if (!Directory.Exists(destDir))
                    Directory.CreateDirectory(destDir);

                if (ContainsReparsePoint(destinationPath))
                {
                    _logger.LogWarning($"[Quarantine] Destino contém reparse point; restauração cancelada: {destinationPath}");
                    return false;
                }

                File.Move(entry.QuarantinePath, destinationPath, overwrite: false);

                lock (_lock)
                {
                    _entries.Remove(entry);
                    if (!SaveMetadata())
                    {
                        _entries.Add(entry);
                        try
                        {
                            if (!File.Exists(entry.QuarantinePath) && File.Exists(destinationPath))
                            {
                                File.Move(destinationPath, entry.QuarantinePath, overwrite: false);
                            }
                        }
                        catch (Exception rollbackException)
                        {
                            _logger.LogError($"[Quarantine] Falha ao reverter restauração: {rollbackException.Message}");
                        }
                        return false;
                    }
                }

                _securityLog.LogSecurityEvent("Quarantine", "FILE_RESTORED",
                    $"File: {entry.OriginalPath} | Hash: {entry.SHA256}");

                _logger.LogSuccess($"[Quarantine] ✅ Arquivo restaurado: {entry.OriginalName}");
                ItemRestored?.Invoke(this, entry);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[Quarantine] Erro ao restaurar: {quarantineId}", ex);
                return false;
            }
        }

        /// <summary>
        /// Exclui permanentemente um arquivo da quarentena.
        /// </summary>
        public bool DeletePermanently(string quarantineId)
        {
            try
            {
                QuarantineEntry? entry;
                lock (_lock)
                {
                    entry = _entries.FirstOrDefault(e => e.Id == quarantineId);
                }

                if (entry == null)
                {
                    _logger.LogWarning($"[Quarantine] ID não encontrado para exclusão: {quarantineId}");
                    return false;
                }

                if (!IsValidEntry(entry))
                {
                    _logger.LogWarning($"[Quarantine] Entrada inválida para exclusão: {quarantineId}");
                    return false;
                }

                _logger.LogInfo($"[Quarantine] Excluindo permanentemente: {entry.OriginalName}");

                if (File.Exists(entry.QuarantinePath))
                {
                    // Sobrescrever com zeros antes de deletar (secure delete)
                    try
                    {
                        var size = new FileInfo(entry.QuarantinePath).Length;
                        if (size > 0 && size < 100_000_000) // Até 100MB
                        {
                            using var fs = new FileStream(entry.QuarantinePath, FileMode.Open, FileAccess.Write);
                            var zeros = new byte[Math.Min(size, 65536)];
                            long written = 0;
                            while (written < size)
                            {
                                var toWrite = (int)Math.Min(zeros.Length, size - written);
                                fs.Write(zeros, 0, toWrite);
                                written += toWrite;
                            }
                        }
                    }
                    catch { /* Best effort secure delete */ }

                    File.Delete(entry.QuarantinePath);
                }

                lock (_lock)
                {
                    _entries.Remove(entry);
                    if (!SaveMetadata())
                    {
                        _entries.Add(entry);
                        return false;
                    }
                }

                _securityLog.LogSecurityEvent("Quarantine", "FILE_DELETED_PERMANENTLY",
                    $"File: {entry.OriginalName} | Hash: {entry.SHA256}");

                _logger.LogSuccess($"[Quarantine] ✅ Arquivo excluído permanentemente: {entry.OriginalName}");
                ItemDeleted?.Invoke(this, entry);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[Quarantine] Erro ao excluir: {quarantineId}", ex);
                return false;
            }
        }

        /// <summary>
        /// Retorna todos os itens em quarentena.
        /// </summary>
        public List<QuarantineEntry> GetAllEntries()
        {
            lock (_lock)
            {
                return _entries.ToList();
            }
        }

        public int Count
        {
            get { lock (_lock) { return _entries.Count; } }
        }

        /// <summary>
        /// Limpa itens com mais de 30 dias automaticamente.
        /// </summary>
        public int CleanExpiredEntries(int maxAgeDays = 30)
        {
            int cleaned = 0;
            lock (_lock)
            {
                var expired = _entries.Where(e => (DateTime.UtcNow - e.QuarantinedAt).TotalDays > maxAgeDays).ToList();
                foreach (var entry in expired)
                {
                    try
                    {
                        if (IsValidEntry(entry) && File.Exists(entry.QuarantinePath))
                            File.Delete(entry.QuarantinePath);
                        _entries.Remove(entry);
                        cleaned++;
                        _logger.LogInfo($"[Quarantine] Expirado removido: {entry.OriginalName} ({entry.QuarantinedAt:yyyy-MM-dd})");
                    }
                    catch { }
                }
                if (cleaned > 0) SaveMetadata();
            }
            _logger.LogInfo($"[Quarantine] Limpeza: {cleaned} itens expirados removidos");
            return cleaned;
        }

        private async Task<string> ComputeHashAsync(string filePath)
        {
            try
            {
                using var sha256 = SHA256.Create();
                using var stream = File.OpenRead(filePath);
                var hash = await sha256.ComputeHashAsync(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
            catch
            {
                return "hash_error";
            }
        }

        private void LoadMetadata()
        {
            try
            {
                if (File.Exists(_metadataFile))
                {
                    var json = File.ReadAllText(_metadataFile);
                    var loaded = JsonSerializer.Deserialize<List<QuarantineEntry>>(json) ?? new();
                    _entries = loaded.Where(IsValidEntry).ToList();
                    _logger.LogInfo($"[Quarantine] {_entries.Count} entradas válidas carregadas do índice");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Quarantine] Erro ao carregar índice: {ex.Message}");
                _entries = new();
            }
        }

        private bool SaveMetadata()
        {
            try
            {
                var json = JsonSerializer.Serialize(_entries, new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, WriteIndented = true });
                var tempFile = _metadataFile + ".tmp";
                File.WriteAllText(tempFile, json, new UTF8Encoding(false));
                using (var stream = new FileStream(tempFile, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
                {
                    stream.Flush(true);
                }
                File.Move(tempFile, _metadataFile, overwrite: true);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[Quarantine] Erro ao salvar índice: {ex.Message}");
                return false;
            }
        }
    }

    public class QuarantineEntry
    {
        public string Id { get; set; } = string.Empty;
        public string OriginalPath { get; set; } = string.Empty;
        public string OriginalName { get; set; } = string.Empty;
        public string QuarantinePath { get; set; } = string.Empty;
        public string SHA256 { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string Reason { get; set; } = string.Empty;
        public ThreatSeverity Severity { get; set; }
        public DateTime QuarantinedAt { get; set; }
        public DateTime OriginalCreationTime { get; set; }
        public DateTime OriginalLastWriteTime { get; set; }
    }
}
