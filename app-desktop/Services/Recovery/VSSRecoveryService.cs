using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Recovery
{
    /// <summary>
    /// Serviço de recuperação via VSS (Volume Shadow Copy Service).
    /// Funciona em SSDs com TRIM ativo, pois usa snapshots criados ANTES da deleção.
    /// </summary>
    public class VSSRecoveryService
    {
        public class ShadowCopyInfo
        {
            public string Id { get; set; } = string.Empty;
            public string DevicePath { get; set; } = string.Empty;
            public string VolumeName { get; set; } = string.Empty;
            public DateTime CreationTime { get; set; }
            public string DisplayName => $"Snapshot de {CreationTime:dd/MM/yyyy HH:mm}";
        }

        public class VSSRecoveredFile
        {
            public string OriginalPath { get; set; } = string.Empty;
            public string SnapshotPath { get; set; } = string.Empty;
            public string Filename { get; set; } = string.Empty;
            public string Extension { get; set; } = string.Empty;
            public long SizeBytes { get; set; }
            public string SizeDisplay => FormatSize(SizeBytes);
            public DateTime LastModified { get; set; }
            public string SnapshotId { get; set; } = string.Empty;
            public FileCategory Category { get; set; }

            private static string FormatSize(long bytes)
            {
                if (bytes < 1024) return $"{bytes} B";
                if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
                if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F2} MB";
                return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
            }
        }

        /// <summary>
        /// Lista todos os Shadow Copies disponíveis para um volume.
        /// </summary>
        public List<ShadowCopyInfo> GetAvailableShadowCopies(string driveLetter)
        {
            var sw = Stopwatch.StartNew();
            App.LoggingService?.LogInfo($"[VSSRecovery] GetAvailableShadowCopies iniciado para volume: {driveLetter}");
            var result = new List<ShadowCopyInfo>();
            try
            {
                string volume = driveLetter.TrimEnd('\\') + "\\";
                using var searcher = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_ShadowCopy WHERE VolumeName = '" + volume.Replace("\\", "\\\\") + "'");

                foreach (ManagementObject obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    result.Add(new ShadowCopyInfo
                    {
                        Id = obj["ID"]?.ToString() ?? string.Empty,
                        DevicePath = obj["DeviceObject"]?.ToString() ?? string.Empty,
                        VolumeName = obj["VolumeName"]?.ToString() ?? string.Empty,
                        CreationTime = ParseWmiDate(obj["InstallDate"]?.ToString())
                    });
                }

                result = result.OrderByDescending(x => x.CreationTime).ToList();
                Debug.WriteLine($"[VSSRecovery] {result.Count} shadow copies encontrados para {driveLetter}");
                App.LoggingService?.LogInfo($"[VSSRecovery] GetAvailableShadowCopies concluído: {result.Count} shadow copies em {sw.Elapsed.TotalSeconds:F2}s");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VSSRecovery] Erro ao listar shadow copies: {ex.Message}");
                App.LoggingService?.LogError($"[VSSRecovery] Erro ao listar shadow copies para {driveLetter}", ex);
            }
            sw.Stop();
            return result;
        }

        /// <summary>
        /// Varre um shadow copy completo buscando arquivos deletados comparando com o filesystem atual.
        /// </summary>
        public async Task<List<VSSRecoveredFile>> ScanShadowCopyAsync(
            ShadowCopyInfo shadowCopy,
            string driveLetter,
            IProgress<(double percent, int count)>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var outerSw = Stopwatch.StartNew();
            App.LoggingService?.LogInfo($"[VSSRecovery] ScanShadowCopyAsync iniciado para snapshot {shadowCopy.Id} no volume {driveLetter}");
            var result = new List<VSSRecoveredFile>();

            await Task.Run(() =>
            {
                var innerSw = Stopwatch.StartNew();
                string linkPath = Path.Combine(Path.GetTempPath(), $"voltris_vss_{Guid.NewGuid():N}");
                bool linkCreated = false;

                try
                {
                    // Criar link simbólico para o snapshot
                    string devicePath = shadowCopy.DevicePath + "\\";
                    var psi = new ProcessStartInfo("cmd.exe",
                        $"/c mklink /d \"{linkPath}\" \"{devicePath}\"")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        Verb = "runas"
                    };

                    using var proc = Process.Start(psi);
                    proc?.WaitForExit(5000);
                    linkCreated = Directory.Exists(linkPath);

                    if (!linkCreated)
                    {
                        Debug.WriteLine("[VSSRecovery] Falha ao criar link simbólico para snapshot.");
                        App.LoggingService?.LogError($"[VSSRecovery] Falha ao criar link simbólico para snapshot em: {linkPath}", null);
                        return;
                    }

                    Debug.WriteLine($"[VSSRecovery] Link criado em: {linkPath}");
                    App.LoggingService?.LogInfo($"[VSSRecovery] Link simbólico criado com sucesso em: {linkPath}");

                    // Mapear arquivos atuais no volume real
                    string realDrivePath = driveLetter.TrimEnd('\\') + "\\";
                    var currentFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    try
                    {
                        foreach (var f in Directory.EnumerateFiles(realDrivePath, "*", SearchOption.AllDirectories))
                        {
                            if (cancellationToken.IsCancellationRequested) return;
                            currentFiles.Add(f.Substring(realDrivePath.Length));
                        }
                        App.LoggingService?.LogInfo($"[VSSRecovery] Enumeração do volume real concluída: {currentFiles.Count} arquivos atuais");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[VSSRecovery] Erro enumerando arquivos reais: {ex.Message}");
                        App.LoggingService?.LogError($"[VSSRecovery] Erro ao enumerar arquivos reais em {realDrivePath}", ex);
                    }

                    // Escanear snapshot e encontrar arquivos que não existem mais
                    var snapshotFiles = new List<string>();
                    try
                    {
                        snapshotFiles = Directory.EnumerateFiles(linkPath, "*", SearchOption.AllDirectories).ToList();
                        App.LoggingService?.LogInfo($"[VSSRecovery] Enumeração do snapshot concluída: {snapshotFiles.Count} arquivos no snapshot");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[VSSRecovery] Erro enumerando snapshot: {ex.Message}");
                        App.LoggingService?.LogError($"[VSSRecovery] Erro ao enumerar snapshot em {linkPath}", ex);
                    }

                    int total = snapshotFiles.Count;
                    int processed = 0;

                    foreach (var snapshotFile in snapshotFiles)
                    {
                        if (cancellationToken.IsCancellationRequested) break;
                        processed++;

                        try
                        {
                            string relativePath = snapshotFile.Substring(linkPath.Length).TrimStart('\\', '/');

                            // Skip system/hidden paths
                            if (IsSystemPath(relativePath)) continue;

                            // Arquivo existe no snapshot mas não no disco = foi deletado!
                            bool isDeleted = !currentFiles.Contains(relativePath);

                            if (isDeleted)
                            {
                                var fi = new FileInfo(snapshotFile);
                                string ext = fi.Extension.TrimStart('.').ToLowerInvariant();

                                result.Add(new VSSRecoveredFile
                                {
                                    OriginalPath = Path.Combine(realDrivePath, relativePath),
                                    SnapshotPath = snapshotFile,
                                    Filename = fi.Name,
                                    Extension = ext,
                                    SizeBytes = fi.Length,
                                    LastModified = fi.LastWriteTime,
                                    SnapshotId = shadowCopy.Id,
                                    Category = Categorize(ext)
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[VSSRecovery] Arquivo inacessível durante scan: {snapshotFile}");
                            App.LoggingService?.LogWarning($"[VSSRecovery] Arquivo inacessível no snapshot: {snapshotFile}");
                        }

                        if (processed % 500 == 0)
                        {
                            double pct = total > 0 ? (double)processed / total * 100.0 : 0;
                            progress?.Report((pct, result.Count));
                        }
                    }

                    progress?.Report((100.0, result.Count));
                    Debug.WriteLine($"[VSSRecovery] Scan completo: {result.Count} arquivos deletados encontrados no snapshot.");
                    App.LoggingService?.LogInfo($"[VSSRecovery] ScanShadowCopyAsync concluído: {total} arquivos no snapshot, {result.Count} deletados encontrados em {innerSw.Elapsed.TotalSeconds:F2}s");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[VSSRecovery] Erro crítico no scan VSS: {ex.Message}");
                    App.LoggingService?.LogError($"[VSSRecovery] Erro crítico no scan do snapshot {shadowCopy.Id}", ex);
                }
                finally
                {
                    // Remover link simbólico
                    if (linkCreated && Directory.Exists(linkPath))
                    {
                        try
                        {
                            Debug.WriteLine($"[VSSRecovery] Removendo link simbólico: {linkPath}");
                            App.LoggingService?.LogInfo($"[VSSRecovery] Limpando link simbólico: {linkPath}");
                            var psi = new ProcessStartInfo("cmd.exe",
                                $"/c rmdir \"{linkPath}\"")
                            {
                                UseShellExecute = false,
                                CreateNoWindow = true
                            };
                            using var proc = Process.Start(psi);
                            proc?.WaitForExit(3000);
                            Debug.WriteLine($"[VSSRecovery] Link simbólico removido com sucesso: {linkPath}");
                            App.LoggingService?.LogInfo($"[VSSRecovery] Link simbólico removido com sucesso");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[VSSRecovery] Erro ao remover link simbólico: {linkPath}");
                            App.LoggingService?.LogWarning($"[VSSRecovery] Erro ao limpar link simbólico: {linkPath}");
                        }
                    }
                }
            }, cancellationToken);

            outerSw.Stop();
            App.LoggingService?.LogInfo($"[VSSRecovery] ScanShadowCopyAsync finalizado (total): {result.Count} arquivos recuperáveis em {outerSw.Elapsed.TotalSeconds:F2}s");
            return result;
        }

        /// <summary>
        /// Recupera um arquivo do shadow copy copiando-o para o destino.
        /// </summary>
        public bool RecoverFromSnapshot(VSSRecoveredFile file, string outputDirectory)
        {
            var sw = Stopwatch.StartNew();
            App.LoggingService?.LogInfo($"[VSSRecovery] RecoverFromSnapshot iniciado: {file.Filename} ({file.SizeDisplay}) -> {outputDirectory}");
            try
            {
                if (!File.Exists(file.SnapshotPath))
                {
                    Debug.WriteLine($"[VSSRecovery] Arquivo não existe no snapshot: {file.SnapshotPath}");
                    App.LoggingService?.LogWarning($"[VSSRecovery] Arquivo não encontrado no snapshot: {file.SnapshotPath}");
                    sw.Stop();
                    return false;
                }

                Directory.CreateDirectory(outputDirectory);
                string destPath = Path.Combine(outputDirectory, file.Filename);

                // Evitar sobrescrita
                int count = 1;
                string nameWithoutExt = Path.GetFileNameWithoutExtension(destPath);
                string ext = Path.GetExtension(destPath);
                while (File.Exists(destPath))
                {
                    destPath = Path.Combine(outputDirectory, $"{nameWithoutExt} ({count++}){ext}");
                }

                File.Copy(file.SnapshotPath, destPath);
                Debug.WriteLine($"[VSSRecovery] Recuperado com sucesso: {destPath}");
                App.LoggingService?.LogInfo($"[VSSRecovery] RecoverFromSnapshot concluído: {destPath} em {sw.Elapsed.TotalSeconds:F2}s");
                sw.Stop();
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VSSRecovery] Erro ao recuperar: {ex.Message}");
                App.LoggingService?.LogError($"[VSSRecovery] Erro ao recuperar arquivo {file.Filename} para {outputDirectory}", ex);
                sw.Stop();
                return false;
            }
        }

        private static bool IsSystemPath(string relativePath)
        {
            var skipPaths = new[] { "$RECYCLE.BIN", "System Volume Information", "pagefile.sys", "hiberfil.sys", "swapfile.sys" };
            return skipPaths.Any(s => relativePath.StartsWith(s, StringComparison.OrdinalIgnoreCase));
        }

        private static FileCategory Categorize(string ext)
        {
            if (new[] { "jpg", "jpeg", "png", "bmp", "gif", "tiff", "webp", "ico", "psd", "svg" }.Contains(ext)) return FileCategory.Images;
            if (new[] { "mp4", "mkv", "avi", "mov", "wmv", "flv", "webm", "m4v" }.Contains(ext)) return FileCategory.Videos;
            if (new[] { "pdf", "docx", "doc", "xlsx", "xls", "txt", "pptx", "rtf", "odt" }.Contains(ext)) return FileCategory.Documents;
            if (new[] { "mp3", "wav", "flac", "ogg", "m4a", "aac" }.Contains(ext)) return FileCategory.Music;
            if (new[] { "zip", "rar", "7z", "tar", "gz" }.Contains(ext)) return FileCategory.Archives;
            if (new[] { "exe", "dll", "sys", "msi", "bat" }.Contains(ext)) return FileCategory.System;
            return FileCategory.Others;
        }

        private static DateTime ParseWmiDate(string? wmiDate)
        {
            if (string.IsNullOrEmpty(wmiDate)) return DateTime.MinValue;
            try
            {
                // WMI date format: "20260407185500.000000+000"
                if (wmiDate.Length >= 14)
                {
                    int year   = int.Parse(wmiDate.Substring(0, 4));
                    int month  = int.Parse(wmiDate.Substring(4, 2));
                    int day    = int.Parse(wmiDate.Substring(6, 2));
                    int hour   = int.Parse(wmiDate.Substring(8, 2));
                    int minute = int.Parse(wmiDate.Substring(10, 2));
                    int second = int.Parse(wmiDate.Substring(12, 2));
                    return new DateTime(year, month, day, hour, minute, second);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VSSRecovery] Erro ao parsear data WMI: {wmiDate} - {ex.Message}");
                App.LoggingService?.LogWarning($"[VSSRecovery] Falha ao parsear data WMI: {wmiDate}");
            }
            return DateTime.MinValue;
        }
    }
}
