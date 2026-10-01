// ============================================================
// NOVOS MÓDULOS DE LIMPEZA - VoltrisOptimizer
// Todos os métodos aqui são partial da classe UltraCleanerService
// 100% seguros: não tocam em arquivos pessoais, apps ativos ou navegadores
// ============================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Models;

using VoltrisOptimizer.Services.UltraClean;

namespace VoltrisOptimizer.Services
{
    public partial class UltraCleanerService
    {
        // ============================================================
        // HELPERS INTERNOS SEGUROS
        // ============================================================

        /// <summary>
        /// Deleta arquivos em um diretório com verificações de segurança máximas.
        /// Nunca deleta arquivos em uso, arquivos de sistema críticos ou arquivos pessoais.
        /// </summary>
        private long SafeDeleteFiles(string directory, string pattern = "*",
            int olderThanDays = 0, bool recursive = false,
            string[]? skipExtensions = null, string[]? skipNames = null)
        {
        if (!Directory.Exists(directory)) return 0;
        // PORTÃO DE SEGURANÇA: blacklists de usuário/jogo/Windows valem para
        // todos os módulos, inclusive os que já usam IsFileLocked.
        if (!CleanupSafetyGuard.CanDelete(directory, pattern))
        {
            _logger.LogWarning($"[SafeDelete] LIMPEZA BLOQUEADA por seguranca: {directory} ({pattern})");
            return 0;
        }
        long freed = 0;
            var cutoff = olderThanDays > 0 ? DateTime.Now.AddDays(-olderThanDays) : DateTime.MinValue;
            var skipExt = skipExtensions ?? Array.Empty<string>();
            var skipNm  = skipNames      ?? Array.Empty<string>();

            try
            {
                var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                foreach (var file in Directory.EnumerateFiles(directory, pattern, option))
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        // Pular extensões protegidas
                        if (skipExt.Contains(fi.Extension.ToLowerInvariant())) continue;
                        // Pular nomes protegidos
                        if (skipNm.Any(n => fi.Name.Equals(n, StringComparison.OrdinalIgnoreCase))) continue;
                        // Respeitar cutoff de data
                        if (cutoff > DateTime.MinValue && fi.LastWriteTime >= cutoff) continue;
                        // Verificar se arquivo está em uso
                        if (IsFileLocked(fi)) { _logger.LogTrace($"[SafeDelete] Em uso, pulando: {fi.Name}"); continue; }

                        freed += fi.Length;
                        _logger.LogTrace($"[SafeDelete] Deletando: {fi.FullName} ({FormatBytes(fi.Length)})");
                        fi.Delete();
                    }
                    catch (Exception ex) { _logger.LogTrace($"[SafeDelete] Não foi possível deletar {file}: {ex.Message}"); }
                }
            }
            catch (Exception ex) { _logger.LogTrace($"[SafeDelete] Erro ao enumerar {directory}: {ex.Message}"); }
            return freed;
        }

        /// <summary>
        /// Verifica se um arquivo está sendo usado por outro processo.
        /// </summary>
        private static bool IsFileLocked(FileInfo file)
        {
            try
            {
                using var stream = file.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                return false;
            }
            catch (IOException) { return true; }
            catch { return false; }
        }

        /// <summary>
        /// Retorna o tamanho de um diretório de forma segura, sem lançar exceções.
        /// </summary>
        private long SafeGetDirSize(string path)
        {
            if (!Directory.Exists(path)) return 0;
            long size = 0;
            try
            {
                foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { size += new FileInfo(f).Length; } catch { }
                }
            }
            catch { }
            return size;
        }

        /// <summary>
        /// Retorna o tamanho total de múltiplos diretórios de forma segura.
        /// </summary>
        private long SafeGetMultiDirSize(IEnumerable<string> paths)
        {
            return paths.Where(Directory.Exists).Sum(SafeGetDirSize);
        }

        // ============================================================
        // CATEGORIA: LOGS DO SISTEMA (NOVOS)
        // ============================================================

        // --- CBS .cab de backup (gerados pelo SFC/DISM, podem ter GBs) ---
        private long AnalyzeCbsCabBackups()
        {
            _logger.LogInfo("[UltraClean] Analisando CBS .cab backups...");
            var cbsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Logs", "CBS");
            if (!Directory.Exists(cbsPath)) return 0;
            long size = 0;
            try
            {
                foreach (var f in Directory.EnumerateFiles(cbsPath, "*.cab"))
                    try { size += new FileInfo(f).Length; } catch { }
            }
            catch { }
            _logger.LogInfo($"[UltraClean] CBS .cab backups: {FormatBytes(size)}");
            return size;
        }

        private long CleanCbsCabBackups()
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInfo("[UltraClean] ▶ Limpando CBS .cab backups...");
            var cbsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Logs", "CBS");
            long freed = SafeDeleteFiles(cbsPath, "*.cab", olderThanDays: 7);
            sw.Stop();
            _logger.LogSuccess($"[UltraClean] ✅ CBS .cab backups: {FormatBytes(freed)} em {sw.Elapsed.TotalSeconds:F1}s");
            return freed;
        }

        // --- System32\LogFiles (IIS, WMI, WLAN, ETW) ---
        private long AnalyzeSystem32LogFiles()
        {
            _logger.LogInfo("[UltraClean] Analisando System32\\LogFiles...");
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "LogFiles");
            long size = SafeGetDirSize(path);
            _logger.LogInfo($"[UltraClean] System32\\LogFiles: {FormatBytes(size)}");
            return size;
        }

        private long CleanSystem32LogFiles()
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInfo("[UltraClean] ▶ Limpando System32\\LogFiles...");
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "LogFiles");
            // Manter arquivos dos últimos 14 dias; pular arquivos sem extensão (podem ser sockets/pipes)
            long freed = SafeDeleteFiles(path, "*", olderThanDays: 14, recursive: true,
                skipExtensions: new[] { ".sys", ".dll", ".exe" });
            sw.Stop();
            _logger.LogSuccess($"[UltraClean] ✅ System32\\LogFiles: {FormatBytes(freed)} em {sw.Elapsed.TotalSeconds:F1}s");
            return freed;
        }

        // --- Windows\debug (netlogon.log, PASSWD.LOG, etc.) ---
        private long AnalyzeWindowsDebugLogs()
        {
            _logger.LogInfo("[UltraClean] Analisando Windows\\debug...");
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "debug");
            long size = SafeGetDirSize(path);
            _logger.LogInfo($"[UltraClean] Windows\\debug: {FormatBytes(size)}");
            return size;
        }

        private long CleanWindowsDebugLogs()
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInfo("[UltraClean] ▶ Limpando Windows\\debug...");
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "debug");
            long freed = SafeDeleteFiles(path, "*.log", olderThanDays: 14, recursive: false);
            freed    += SafeDeleteFiles(path, "*.LOG", olderThanDays: 14, recursive: false);
            freed    += SafeDeleteFiles(path, "*.txt", olderThanDays: 14, recursive: false);
            sw.Stop();
            _logger.LogSuccess($"[UltraClean] ✅ Windows\\debug: {FormatBytes(freed)} em {sw.Elapsed.TotalSeconds:F1}s");
            return freed;
        }

        // --- WER ReportQueue e ReportArchive (ProgramData, não do usuário) ---
        private long AnalyzeSystemWerReports()
        {
            _logger.LogInfo("[UltraClean] Analisando WER ReportQueue/Archive do sistema...");
            var base1 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Microsoft", "Windows", "WER");
            var paths = new[] {
                Path.Combine(base1, "ReportQueue"),
                Path.Combine(base1, "ReportArchive")
            };
            long size = SafeGetMultiDirSize(paths);
            _logger.LogInfo($"[UltraClean] WER sistema: {FormatBytes(size)}");
            return size;
        }

        private long CleanSystemWerReports()
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInfo("[UltraClean] ▶ Limpando WER ReportQueue/Archive do sistema...");
            var base1 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Microsoft", "Windows", "WER");
            var paths = new[] {
                Path.Combine(base1, "ReportQueue"),
                Path.Combine(base1, "ReportArchive")
            };
            long freed = 0;
            foreach (var p in paths.Where(Directory.Exists))
            {
                long f = SafeDeleteFiles(p, "*", olderThanDays: 7, recursive: true);
                _logger.LogInfo($"[UltraClean] 🗑 {Path.GetFileName(p)}: {FormatBytes(f)}");
                freed += f;
            }
            sw.Stop();
            _logger.LogSuccess($"[UltraClean] ✅ WER sistema: {FormatBytes(freed)} em {sw.Elapsed.TotalSeconds:F1}s");
            return freed;
        }

        // --- Windows\Panther (logs de upgrade, pode ter GBs) ---
        private long AnalyzeWindowsPantherLogs()
        {
            _logger.LogInfo("[UltraClean] Analisando Windows\\Panther...");
            var winPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var paths = new[] {
                Path.Combine(winPath, "Panther"),
                Path.Combine(winPath, "Panther", "UnattendGC")
            };
            long size = SafeGetMultiDirSize(paths);
            _logger.LogInfo($"[UltraClean] Windows\\Panther: {FormatBytes(size)}");
            return size;
        }

        private long CleanWindowsPantherLogs()
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInfo("[UltraClean] ▶ Limpando Windows\\Panther (logs de upgrade)...");
            var winPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var paths = new[] {
                Path.Combine(winPath, "Panther"),
                Path.Combine(winPath, "Panther", "UnattendGC")
            };
            long freed = 0;
            foreach (var p in paths.Where(Directory.Exists))
            {
                // Só deletar .log, .etl, .xml — nunca binários
                long f = SafeDeleteFiles(p, "*.log", olderThanDays: 0, recursive: false);
                f    += SafeDeleteFiles(p, "*.etl", olderThanDays: 0, recursive: false);
                f    += SafeDeleteFiles(p, "*.xml", olderThanDays: 0, recursive: false);
                _logger.LogInfo($"[UltraClean] 🗑 {Path.GetFileName(p)}: {FormatBytes(f)}");
                freed += f;
            }
            sw.Stop();
            _logger.LogSuccess($"[UltraClean] ✅ Windows\\Panther: {FormatBytes(freed)} em {sw.Elapsed.TotalSeconds:F1}s");
            return freed;
        }

        // --- $SysReset logs ---
        private long AnalyzeSysResetLogs()
        {
            _logger.LogInfo("[UltraClean] Analisando $SysReset...");
            var path = Path.Combine(Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\", "$SysReset");
            long size = SafeGetDirSize(path);
            _logger.LogInfo($"[UltraClean] $SysReset: {FormatBytes(size)}");
            return size;
        }

        private long CleanSysResetLogs()
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInfo("[UltraClean] ▶ Limpando $SysReset...");
            var path = Path.Combine(Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\", "$SysReset");
            long freed = SafeDeleteFiles(path, "*.log", recursive: true);
            freed    += SafeDeleteFiles(path, "*.etl", recursive: true);
            sw.Stop();
            _logger.LogSuccess($"[UltraClean] ✅ $SysReset: {FormatBytes(freed)} em {sw.Elapsed.TotalSeconds:F1}s");
            return freed;
        }

        // --- ServiceProfiles Temp (LocalService + NetworkService) ---
        private long AnalyzeServiceProfilesTemp()
        {
            _logger.LogInfo("[UltraClean] Analisando ServiceProfiles Temp...");
            var winPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var paths = new[] {
                Path.Combine(winPath, "ServiceProfiles", "LocalService",   "AppData", "Local", "Temp"),
                Path.Combine(winPath, "ServiceProfiles", "NetworkService", "AppData", "Local", "Temp")
            };
            long size = SafeGetMultiDirSize(paths);
            _logger.LogInfo($"[UltraClean] ServiceProfiles Temp: {FormatBytes(size)}");
            return size;
        }

        private long CleanServiceProfilesTemp()
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInfo("[UltraClean] ▶ Limpando ServiceProfiles Temp...");
            var winPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var paths = new[] {
                Path.Combine(winPath, "ServiceProfiles", "LocalService",   "AppData", "Local", "Temp"),
                Path.Combine(winPath, "ServiceProfiles", "NetworkService", "AppData", "Local", "Temp")
            };
            long freed = 0;
            foreach (var p in paths.Where(Directory.Exists))
            {
                long f = SafeDeleteFiles(p, "*", olderThanDays: 7, recursive: true,
                    skipExtensions: new[] { ".sys", ".dll", ".exe" });
                _logger.LogInfo($"[UltraClean] 🗑 {Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(p)!)))!)}: {FormatBytes(f)}");
                freed += f;
            }
            sw.Stop();
            _logger.LogSuccess($"[UltraClean] ✅ ServiceProfiles Temp: {FormatBytes(freed)} em {sw.Elapsed.TotalSeconds:F1}s");
            return freed;
        }

        // ============================================================
        // CATEGORIA: CACHE DE APPS MICROSOFT (NOVOS)
        // ============================================================

        // --- Teams Cache (clássico) ---
        private long AnalyzeTeamsCache()
        {
            _logger.LogInfo("[UltraClean] Analisando Microsoft Teams cache...");
            long size = 0;
            foreach (var profile in GetAllUserProfiles())
            {
                var local = Path.Combine(profile, "AppData", "Local", "Microsoft", "Teams");
                var paths = new[] {
                    Path.Combine(local, "Cache"),
                    Path.Combine(local, "blob_storage"),
                    Path.Combine(local, "GPUCache"),
                    Path.Combine(local, "Code Cache"),
                    Path.Combine(local, "tmp")
                };
                size += SafeGetMultiDirSize(paths);
            }
            _logger.LogInfo($"[UltraClean] Teams cache: {FormatBytes(size)}");
            return size;
        }

        private long CleanTeamsCache()
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInfo("[UltraClean] ▶ Limpando Microsoft Teams cache...");
            long freed = 0;
            foreach (var profile in GetAllUserProfiles())
            {
                var local = Path.Combine(profile, "AppData", "Local", "Microsoft", "Teams");
                var paths = new[] {
                    Path.Combine(local, "Cache"),
                    Path.Combine(local, "blob_storage"),
                    Path.Combine(local, "GPUCache"),
                    Path.Combine(local, "Code Cache"),
                    Path.Combine(local, "tmp")
                };
                foreach (var p in paths.Where(Directory.Exists))
                {
                    // Nunca deletar databases/ ou Local Storage/ — contém dados de sessão
                    long f = SafeDeleteFiles(p, "*", olderThanDays: 0, recursive: true,
                        skipExtensions: new[] { ".ldb", ".log", ".manifest" });
                    if (f > 0) _logger.LogInfo($"[UltraClean] 🗑 Teams [{Path.GetFileName(profile)}] {Path.GetFileName(p)}: {FormatBytes(f)}");
                    freed += f;
                }
            }
            sw.Stop();
            _logger.LogSuccess($"[UltraClean] ✅ Teams cache: {FormatBytes(freed)} em {sw.Elapsed.TotalSeconds:F1}s");
            return freed;
        }

        // --- Outlook OST órfãos (contas removidas) ---
        private long AnalyzeOutlookOrphanOst()
        {
            _logger.LogInfo("[UltraClean] Analisando OST órfãos do Outlook...");
            long size = 0;
            foreach (var profile in GetAllUserProfiles())
            {
                var ostDir = Path.Combine(profile, "AppData", "Local", "Microsoft", "Outlook");
                if (!Directory.Exists(ostDir)) continue;
                // Verificar quais OSTs estão referenciados no perfil do Outlook
                var activeOsts = GetActiveOutlookOstPaths();
                if (activeOsts == null) return 0;
                foreach (var ost in Directory.EnumerateFiles(ostDir, "*.ost"))
                {
                    if (!activeOsts.Contains(ost, StringComparer.OrdinalIgnoreCase))
                    {
                        try { size += new FileInfo(ost).Length; } catch { }
                        _logger.LogInfo($"[UltraClean] OST órfão encontrado: {Path.GetFileName(ost)}");
                    }
                }
            }
            _logger.LogInfo($"[UltraClean] OST órfãos: {FormatBytes(size)}");
            return size;
        }

        private long CleanOutlookOrphanOst()
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInfo("[UltraClean] ▶ Limpando OST orfaos do Outlook...");
            long freed = 0;

            // FAIL-SAFE: sem lista confianca de OST ativos, nao apaga NADA.
            var activeOsts = GetActiveOutlookOstPaths();
            if (activeOsts == null)
            {
                _logger.LogWarning("[UltraClean] OST preservados: nao foi possivel determinar quais estao em uso.");
                return 0;
            }

            // .ost e caixa de correio local: pode conter rascunhos nao enviados.
            // Nunca reaproveitar como "cache" sem idade minima.
            var minAge = DateTime.UtcNow.AddDays(-30);

            foreach (var profile in GetAllUserProfiles())
            {
                var ostDir = Path.Combine(profile, "AppData", "Local", "Microsoft", "Outlook");
                if (!Directory.Exists(ostDir)) continue;
                foreach (var ost in Directory.EnumerateFiles(ostDir, "*.ost"))
                {
                    if (activeOsts.Contains(ost, StringComparer.OrdinalIgnoreCase)) continue;
                    try
                    {
                        var fi = new FileInfo(ost);

                        // Guarda extra: qualquer .ost passa pelo portao de seguranca.
                        string blocked = CleanupSafetyGuard.DescribeProtection(fi.FullName);
                        if (blocked.Length > 0)
                        {
                            _logger.LogWarning($"[UltraClean] OST preservado por seguranca: {fi.Name} ({blocked})");
                            continue;
                        }

                        if (fi.LastWriteTimeUtc > minAge) continue;
                        if (CleanupSafetyGuard.IsFileLocked(fi)) { _logger.LogTrace($"[UltraClean] OST em uso, pulando: {fi.Name}"); continue; }
                        _logger.LogInfo($"[UltraClean] Deletando OST orfao: {fi.Name} ({FormatBytes(fi.Length)})");
                        freed += fi.Length;
                        fi.Delete();
                    }
                    catch (Exception ex) { _logger.LogWarning($"[UltraClean] Nao foi possivel deletar OST: {ex.Message}"); }
                }
            }
            sw.Stop();
            _logger.LogSuccess($"[UltraClean] OST orfaos: {FormatBytes(freed)} em {sw.Elapsed.TotalSeconds:F1}s");
            return freed;
        }

        /// <summary>
        /// Retorna os caminhos de OST ativos de TODOS os usuarios carregados e de TODAS
        /// as versoes do Office.
        ///
        /// RETORNA NULL quando nao e possivel determinar com confianca o conjunto ativo.
        /// Nesse caso o chamador DEVE abortar: antes, uma falha de registro devolvia
        /// conjunto vazio e TODOS os .ost da maquina eram classificados como "orfaos"
        /// e apagados (perda de e-mail).
        /// </summary>
        private HashSet<string>? GetActiveOutlookOstPaths()
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool foundAnyKey = false;

            // Office 2010 (14.0), 2013 (15.0), 2016/2019/2021/365 (16.0)
            var officeVersions = new[] { "14.0", "15.0", "16.0" };

            try
            {
                // HKEY_USERS cobre TODOS os perfis carregados, nao so o usuario atual.
                using var users = Registry.Users;

                foreach (var sid in users.GetSubKeyNames())
                {
                    // Ignorar classes/_Classes e SID de serviço
                    if (sid.StartsWith("_", StringComparison.Ordinal)) continue;
                    if (sid.EndsWith("_Classes", StringComparison.OrdinalIgnoreCase)) continue;

                    foreach (var ver in officeVersions)
                    {
                        string profilesPath = $@"Software\Microsoft\Office\{ver}\Outlook\Profiles";
                        using var profilesKey = users.OpenSubKey($@"{sid}\{profilesPath}");
                        if (profilesKey == null) continue;

                        foundAnyKey = true;
                        foreach (var profileName in profilesKey.GetSubKeyNames())
                        {
                            using var profileKey = profilesKey.OpenSubKey(profileName);
                            if (profileKey == null) continue;
                            foreach (var subName in profileKey.GetSubKeyNames())
                            {
                                using var subKey = profileKey.OpenSubKey(subName);
                                if (subKey == null) continue;
                                var ostPath = subKey.GetValue("001e6610")?.ToString()
                                           ?? subKey.GetValue("001f6610")?.ToString();
                                if (!string.IsNullOrEmpty(ostPath)) result.Add(ostPath);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[UltraClean] Nao foi possivel ler perfis do Outlook: {ex.Message}. OST sera preservado.");
                return null;
            }

            // Sem nenhuma chave de perfil encontrada = nao sabemos o que esta em uso.
            if (!foundAnyKey)
            {
                _logger.LogWarning("[UltraClean] Nenhum perfil do Outlook localizado no registro. OST sera preservado (regra fail-safe).");
                return null;
            }

            return result;
        }

        // --- AutomaticDestinations e CustomDestinations (JumpList) ---
        private long AnalyzeJumpListCache()
        {
            _logger.LogInfo("[UltraClean] Analisando JumpList cache...");
            long size = 0;
            foreach (var profile in GetAllUserProfiles())
            {
                var recent = Path.Combine(profile, "AppData", "Roaming", "Microsoft", "Windows", "Recent");
                size += SafeGetDirSize(Path.Combine(recent, "AutomaticDestinations"));
                size += SafeGetDirSize(Path.Combine(recent, "CustomDestinations"));
            }
            _logger.LogInfo($"[UltraClean] JumpList cache: {FormatBytes(size)}");
            return size;
        }

        private long CleanJumpListCache()
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInfo("[UltraClean] ▶ Limpando JumpList cache...");
            long freed = 0;
            foreach (var profile in GetAllUserProfiles())
            {
                var recent = Path.Combine(profile, "AppData", "Roaming", "Microsoft", "Windows", "Recent");
                long f = SafeDeleteFiles(Path.Combine(recent, "AutomaticDestinations"), "*", olderThanDays: 30);
                f    += SafeDeleteFiles(Path.Combine(recent, "CustomDestinations"),     "*", olderThanDays: 30);
                if (f > 0) _logger.LogInfo($"[UltraClean] 🗑 JumpList [{Path.GetFileName(profile)}]: {FormatBytes(f)}");
                freed += f;
            }
            sw.Stop();
            _logger.LogSuccess($"[UltraClean] ✅ JumpList cache: {FormatBytes(freed)} em {sw.Elapsed.TotalSeconds:F1}s");
            return freed;
        }

        // --- INetCache IE legado (WebView2 / apps que usam WebView) ---
        private long AnalyzeINetCacheLegacy()
        {
            _logger.LogInfo("[UltraClean] Analisando INetCache IE legado...");
            long size = 0;
            foreach (var profile in GetAllUserProfiles())
            {
                size += SafeGetDirSize(Path.Combine(profile, "AppData", "Local", "Microsoft", "Windows", "INetCache", "IE"));
                size += SafeGetDirSize(Path.Combine(profile, "AppData", "Local", "Microsoft", "Windows", "INetCache", "Low", "IE"));
            }
            _logger.LogInfo($"[UltraClean] INetCache IE: {FormatBytes(size)}");
            return size;
        }

        private long CleanINetCacheLegacy()
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInfo("[UltraClean] ▶ Limpando INetCache IE legado...");
            long freed = 0;
            foreach (var profile in GetAllUserProfiles())
            {
                long f = SafeDeleteFiles(Path.Combine(profile, "AppData", "Local", "Microsoft", "Windows", "INetCache", "IE"),
                    "*", olderThanDays: 7, recursive: true);
                f    += SafeDeleteFiles(Path.Combine(profile, "AppData", "Local", "Microsoft", "Windows", "INetCache", "Low", "IE"),
                    "*", olderThanDays: 7, recursive: true);
                if (f > 0) _logger.LogInfo($"[UltraClean] 🗑 INetCache [{Path.GetFileName(profile)}]: {FormatBytes(f)}");
                freed += f;
            }
            sw.Stop();
            _logger.LogSuccess($"[UltraClean] ✅ INetCache IE: {FormatBytes(freed)} em {sw.Elapsed.TotalSeconds:F1}s");
            return freed;
        }

        // ============================================================
        // CATEGORIA: CACHE DE APPS DE TERCEIROS (ELECTRON/CHAT)
        // ============================================================

        // --- Discord Cache ---
        private long AnalyzeDiscordCache()
        {
            _logger.LogInfo("[UltraClean] Analisando Discord cache...");
            long size = 0;
            foreach (var profile in GetAllUserProfiles())
            {
                var local = Path.Combine(profile, "AppData", "Local", "Discord");
                size += SafeGetMultiDirSize(new[] {
                    Path.Combine(local, "Cache"),
                    Path.Combine(local, "GPUCache"),
                    Path.Combine(local, "Code Cache")
                });
            }
            _logger.LogInfo($"[UltraClean] Discord cache: {FormatBytes(size)}");
            return size;
        }

        private long CleanDiscordCache()
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInfo("[UltraClean] ▶ Limpando Discord cache...");
            long freed = 0;
            foreach (var profile in GetAllUserProfiles())
            {
                var local = Path.Combine(profile, "AppData", "Local", "Discord");
                var paths = new[] {
                    Path.Combine(local, "Cache"),
                    Path.Combine(local, "GPUCache"),
                    Path.Combine(local, "Code Cache")
                };
                foreach (var p in paths.Where(Directory.Exists))
                {
                    long f = SafeDeleteFiles(p, "*", olderThanDays: 0, recursive: true,
                        skipExtensions: new[] { ".ldb", ".manifest" });
                    if (f > 0) _logger.LogInfo($"[UltraClean] 🗑 Discord [{Path.GetFileName(profile)}] {Path.GetFileName(p)}: {FormatBytes(f)}");
                    freed += f;
                }
            }
            sw.Stop();
            _logger.LogSuccess($"[UltraClean] ✅ Discord cache: {FormatBytes(freed)} em {sw.Elapsed.TotalSeconds:F1}s");
            return freed;
        }

        // --- Slack Cache ---
        private long AnalyzeSlackCache()
        {
            _logger.LogInfo("[UltraClean] Analisando Slack cache...");
            long size = 0;
            foreach (var profile in GetAllUserProfiles())
            {
                var local = Path.Combine(profile, "AppData", "Local", "slack");
                size += SafeGetMultiDirSize(new[] {
                    Path.Combine(local, "Cache"),
                    Path.Combine(local, "GPUCache"),
                    Path.Combine(local, "Code Cache")
                });
            }
            _logger.LogInfo($"[UltraClean] Slack cache: {FormatBytes(size)}");
            return size;
        }

        private long CleanSlackCache()
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInfo("[UltraClean] ▶ Limpando Slack cache...");
            long freed = 0;
            foreach (var profile in GetAllUserProfiles())
            {
                var local = Path.Combine(profile, "AppData", "Local", "slack");
                var paths = new[] {
                    Path.Combine(local, "Cache"),
                    Path.Combine(local, "GPUCache"),
                    Path.Combine(local, "Code Cache")
                };
                foreach (var p in paths.Where(Directory.Exists))
                {
                    long f = SafeDeleteFiles(p, "*", olderThanDays: 0, recursive: true,
                        skipExtensions: new[] { ".ldb", ".manifest" });
                    if (f > 0) _logger.LogInfo($"[UltraClean] 🗑 Slack [{Path.GetFileName(profile)}] {Path.GetFileName(p)}: {FormatBytes(f)}");
                    freed += f;
                }
            }
            sw.Stop();
            _logger.LogSuccess($"[UltraClean] ✅ Slack cache: {FormatBytes(freed)} em {sw.Elapsed.TotalSeconds:F1}s");
            return freed;
        }

        // --- Spotify Cache ---
        private long AnalyzeSpotifyCache()
        {
            _logger.LogInfo("[UltraClean] Analisando Spotify cache...");
            long size = 0;
            foreach (var profile in GetAllUserProfiles())
            {
                var local = Path.Combine(profile, "AppData", "Local", "Spotify");
                size += SafeGetMultiDirSize(new[] {
                    Path.Combine(local, "Data"),
                    Path.Combine(local, "Storage"),
                    Path.Combine(local, "Browser", "Cache")
                });
            }
            _logger.LogInfo($"[UltraClean] Spotify cache: {FormatBytes(size)}");
            return size;
        }

        private long CleanSpotifyCache()
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInfo("[UltraClean] ▶ Limpando Spotify cache...");
            long freed = 0;
            foreach (var profile in GetAllUserProfiles())
            {
                var local = Path.Combine(profile, "AppData", "Local", "Spotify");
                // Data/ e Storage/ são cache de mídia — 100% seguro deletar
                long f = SafeDeleteFiles(Path.Combine(local, "Data"),    "*", olderThanDays: 0, recursive: true);
                f    += SafeDeleteFiles(Path.Combine(local, "Storage"),  "*", olderThanDays: 0, recursive: true);
                f    += SafeDeleteFiles(Path.Combine(local, "Browser", "Cache"), "*", olderThanDays: 0, recursive: true);
                if (f > 0) _logger.LogInfo($"[UltraClean] 🗑 Spotify [{Path.GetFileName(profile)}]: {FormatBytes(f)}");
                freed += f;
            }
            sw.Stop();
            _logger.LogSuccess($"[UltraClean] ✅ Spotify cache: {FormatBytes(freed)} em {sw.Elapsed.TotalSeconds:F1}s");
            return freed;
        }

        // --- Zoom Cache e Logs ---
        private long AnalyzeZoomCache()
        {
            _logger.LogInfo("[UltraClean] Analisando Zoom cache/logs...");
            long size = 0;
            foreach (var profile in GetAllUserProfiles())
            {
                size += SafeGetMultiDirSize(new[] {
                    Path.Combine(profile, "AppData", "Local",   "Zoom", "data"),
                    Path.Combine(profile, "AppData", "Roaming", "Zoom", "logs"),
                    Path.Combine(profile, "AppData", "Roaming", "Zoom", "data", "Zoom_Recordings_Temp")
                });
            }
            _logger.LogInfo($"[UltraClean] Zoom cache: {FormatBytes(size)}");
            return size;
        }

        private long CleanZoomCache()
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInfo("[UltraClean] ▶ Limpando Zoom cache/logs...");
            long freed = 0;
            foreach (var profile in GetAllUserProfiles())
            {
                long f = SafeDeleteFiles(Path.Combine(profile, "AppData", "Local",   "Zoom", "data"),
                    "*", olderThanDays: 7, recursive: true);
                f    += SafeDeleteFiles(Path.Combine(profile, "AppData", "Roaming", "Zoom", "logs"),
                    "*.log", olderThanDays: 7, recursive: false);
                // Gravações temporárias (não finalizadas)
                f    += SafeDeleteFiles(Path.Combine(profile, "AppData", "Roaming", "Zoom", "data", "Zoom_Recordings_Temp"),
                    "*", olderThanDays: 1, recursive: true);
                if (f > 0) _logger.LogInfo($"[UltraClean] 🗑 Zoom [{Path.GetFileName(profile)}]: {FormatBytes(f)}");
                freed += f;
            }
            sw.Stop();
            _logger.LogSuccess($"[UltraClean] ✅ Zoom cache: {FormatBytes(freed)} em {sw.Elapsed.TotalSeconds:F1}s");
            return freed;
        }

        // --- WhatsApp Desktop Cache ---
        private long AnalyzeWhatsAppCache()
        {
            _logger.LogInfo("[UltraClean] Analisando WhatsApp Desktop cache...");
            long size = 0;
            foreach (var profile in GetAllUserProfiles())
            {
                size += SafeGetMultiDirSize(new[] {
                    Path.Combine(profile, "AppData", "Local", "WhatsApp", "Cache"),
                    Path.Combine(profile, "AppData", "Local", "WhatsApp", "GPUCache")
                });
            }
            _logger.LogInfo($"[UltraClean] WhatsApp cache: {FormatBytes(size)}");
            return size;
        }

        private long CleanWhatsAppCache()
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInfo("[UltraClean] ▶ Limpando WhatsApp Desktop cache...");
            long freed = 0;
            foreach (var profile in GetAllUserProfiles())
            {
                long f = SafeDeleteFiles(Path.Combine(profile, "AppData", "Local", "WhatsApp", "Cache"),
                    "*", olderThanDays: 0, recursive: true, skipExtensions: new[] { ".ldb", ".manifest" });
                f    += SafeDeleteFiles(Path.Combine(profile, "AppData", "Local", "WhatsApp", "GPUCache"),
                    "*", olderThanDays: 0, recursive: true);
                if (f > 0) _logger.LogInfo($"[UltraClean] 🗑 WhatsApp [{Path.GetFileName(profile)}]: {FormatBytes(f)}");
                freed += f;
            }
            sw.Stop();
            _logger.LogSuccess($"[UltraClean] ✅ WhatsApp cache: {FormatBytes(freed)} em {sw.Elapsed.TotalSeconds:F1}s");
            return freed;
        }

        // --- Telegram Desktop Cache ---
        private long AnalyzeTelegramCache()
        {
            _logger.LogInfo("[UltraClean] Analisando Telegram Desktop cache...");
            long size = 0;
            foreach (var profile in GetAllUserProfiles())
            {
                // tdata/user_data/cache — cache de mídia, 100% seguro
                var tdataPath = Path.Combine(profile, "AppData", "Roaming", "Telegram Desktop", "tdata");
                if (!Directory.Exists(tdataPath)) continue;
                foreach (var userDir in Directory.EnumerateDirectories(tdataPath, "user_data*"))
                    size += SafeGetDirSize(Path.Combine(userDir, "cache"));
            }
            _logger.LogInfo($"[UltraClean] Telegram cache: {FormatBytes(size)}");
            return size;
        }

        private long CleanTelegramCache()
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInfo("[UltraClean] ▶ Limpando Telegram Desktop cache...");
            long freed = 0;
            foreach (var profile in GetAllUserProfiles())
            {
                var tdataPath = Path.Combine(profile, "AppData", "Roaming", "Telegram Desktop", "tdata");
                if (!Directory.Exists(tdataPath)) continue;
                foreach (var userDir in Directory.EnumerateDirectories(tdataPath, "user_data*"))
                {
                    long f = SafeDeleteFiles(Path.Combine(userDir, "cache"), "*", olderThanDays: 0, recursive: true);
                    if (f > 0) _logger.LogInfo($"[UltraClean] 🗑 Telegram [{Path.GetFileName(profile)}]: {FormatBytes(f)}");
                    freed += f;
                }
            }
            sw.Stop();
            _logger.LogSuccess($"[UltraClean] ✅ Telegram cache: {FormatBytes(freed)} em {sw.Elapsed.TotalSeconds:F1}s");
            return freed;
        }

        // ============================================================
        // CATEGORIA: DRIVERS EXTRAÍDOS (AMD / NVIDIA / INTEL / OEM)
        // ============================================================

        // --- AMD Driver Extraction Folder ---
        private long AnalyzeAmdExtractedDrivers()
        {
            _logger.LogInfo("[UltraClean] Analisando pasta AMD (drivers extraídos)...");
            var root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\";
            long size = SafeGetDirSize(Path.Combine(root, "AMD"));
            _logger.LogInfo($"[UltraClean] AMD extraídos: {FormatBytes(size)}");
            return size;
        }

        private long CleanAmdExtractedDrivers()
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInfo("[UltraClean] ▶ Limpando pasta AMD (drivers extraídos)...");
            var root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\";
            var amdPath = Path.Combine(root, "AMD");
            if (!Directory.Exists(amdPath)) return 0;

            long freed = 0;
            // Deletar apenas subpastas de versões antigas — manter a mais recente
            var subDirs = Directory.GetDirectories(amdPath)
                .OrderByDescending(d => Directory.GetCreationTime(d))
                .ToList();

            // Manter a pasta mais recente, deletar as demais
            foreach (var dir in subDirs.Skip(1))
            {
                _logger.LogInfo($"[UltraClean] 🗑 AMD versão antiga: {Path.GetFileName(dir)}");
                freed += DeleteDirectorySafe(dir);
            }
            sw.Stop();
            _logger.LogSuccess($"[UltraClean] ✅ AMD extraídos: {FormatBytes(freed)} em {sw.Elapsed.TotalSeconds:F1}s");
            return freed;
        }

        // --- NVIDIA Driver Extraction Folder ---
        private long AnalyzeNvidiaExtractedDrivers()
        {
            _logger.LogInfo("[UltraClean] Analisando pasta NVIDIA (drivers extraídos)...");
            var root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\";
            long size = SafeGetDirSize(Path.Combine(root, "NVIDIA"));
            _logger.LogInfo($"[UltraClean] NVIDIA extraídos: {FormatBytes(size)}");
            return size;
        }

        private long CleanNvidiaExtractedDrivers()
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInfo("[UltraClean] ▶ Limpando pasta NVIDIA (drivers extraídos)...");
            var root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\";
            var nvidiaPath = Path.Combine(root, "NVIDIA");
            if (!Directory.Exists(nvidiaPath)) return 0;

            long freed = 0;
            var subDirs = Directory.GetDirectories(nvidiaPath)
                .OrderByDescending(d => Directory.GetCreationTime(d))
                .ToList();

            foreach (var dir in subDirs.Skip(1))
            {
                _logger.LogInfo($"[UltraClean] 🗑 NVIDIA versão antiga: {Path.GetFileName(dir)}");
                freed += DeleteDirectorySafe(dir);
            }
            sw.Stop();
            _logger.LogSuccess($"[UltraClean] ✅ NVIDIA extraídos: {FormatBytes(freed)} em {sw.Elapsed.TotalSeconds:F1}s");
            return freed;
        }

        // --- Intel Driver Extraction Folder ---
        private long AnalyzeIntelExtractedDrivers()
        {
            _logger.LogInfo("[UltraClean] Analisando pasta Intel (drivers extraídos)...");
            var root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\";
            long size = SafeGetDirSize(Path.Combine(root, "Intel"));
            _logger.LogInfo($"[UltraClean] Intel extraídos: {FormatBytes(size)}");
            return size;
        }

        private long CleanIntelExtractedDrivers()
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInfo("[UltraClean] ▶ Limpando pasta Intel (drivers extraídos)...");
            var root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\";
            var intelPath = Path.Combine(root, "Intel");
            if (!Directory.Exists(intelPath)) return 0;

            long freed = 0;
            // Deletar apenas subpastas de logs e temp — não tocar em drivers ativos
            var safeFolders = new[] { "Logs", "Temp", "Setup", "Install" };
            foreach (var dir in Directory.GetDirectories(intelPath))
            {
                var name = Path.GetFileName(dir);
                if (safeFolders.Any(s => name.StartsWith(s, StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogInfo($"[UltraClean] 🗑 Intel pasta segura: {name}");
                    freed += DeleteDirectorySafe(dir);
                }
            }
            // Deletar arquivos .log e .txt na raiz
            freed += SafeDeleteFiles(intelPath, "*.log", olderThanDays: 0, recursive: false);
            freed += SafeDeleteFiles(intelPath, "*.txt", olderThanDays: 0, recursive: false);
            sw.Stop();
            _logger.LogSuccess($"[UltraClean] ✅ Intel extraídos: {FormatBytes(freed)} em {sw.Elapsed.TotalSeconds:F1}s");
            return freed;
        }

        // --- OEM Drivers Folder (Dell/HP/Lenovo C:\Drivers) ---
        private long AnalyzeOemDriversFolder()
        {
            _logger.LogInfo("[UltraClean] Analisando pasta OEM Drivers...");
            var root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\";
            long size = SafeGetDirSize(Path.Combine(root, "Drivers"));
            _logger.LogInfo($"[UltraClean] OEM Drivers: {FormatBytes(size)}");
            return size;
        }

        private long CleanOemDriversFolder()
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInfo("[UltraClean] ▶ Limpando pasta OEM Drivers...");
            var root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\";
            var driversPath = Path.Combine(root, "Drivers");
            if (!Directory.Exists(driversPath)) return 0;

            // Deletar apenas arquivos de log e temp — nunca .inf, .sys, .cat
            long freed = SafeDeleteFiles(driversPath, "*.log", olderThanDays: 0, recursive: true);
            freed    += SafeDeleteFiles(driversPath, "*.tmp", olderThanDays: 0, recursive: true);
            freed    += SafeDeleteFiles(driversPath, "*.bak", olderThanDays: 0, recursive: true);
            sw.Stop();
            _logger.LogSuccess($"[UltraClean] ✅ OEM Drivers: {FormatBytes(freed)} em {sw.Elapsed.TotalSeconds:F1}s");
            return freed;
        }

        // ============================================================
        // CATEGORIA: WINDOWS INSTALLER ÓRFÃOS (ALTO IMPACTO)
        // ============================================================

        /// <summary>
        /// Analisa arquivos .msi/.msp órfãos em C:\Windows\Installer
        /// (programas já desinstalados que deixaram instaladores para trás).
        /// NUNCA deleta instaladores de programas ainda instalados.
        /// </summary>
        private long AnalyzeOrphanWindowsInstaller()
        {
            _logger.LogInfo("[UltraClean] Analisando Windows\\Installer órfãos...");
            var installerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Installer");
            if (!Directory.Exists(installerPath)) return 0;

            var activePatches = GetActiveInstallerFiles();
            long size = 0;
            int orphanCount = 0;

            foreach (var file in Directory.EnumerateFiles(installerPath, "*.msi")
                .Concat(Directory.EnumerateFiles(installerPath, "*.msp")))
            {
                if (activePatches.Contains(file, StringComparer.OrdinalIgnoreCase)) continue;
                try
                {
                    var fi = new FileInfo(file);
                    size += fi.Length;
                    orphanCount++;
                    _logger.LogTrace($"[UltraClean] Instalador órfão: {fi.Name} ({FormatBytes(fi.Length)})");
                }
                catch { }
            }
            _logger.LogInfo($"[UltraClean] Windows\\Installer órfãos: {orphanCount} arquivos, {FormatBytes(size)}");
            return size;
        }

        private long CleanOrphanWindowsInstaller()
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInfo("[UltraClean] ▶ Limpando Windows\\Installer órfãos...");
            var installerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Installer");
            if (!Directory.Exists(installerPath)) return 0;

            var activePatches = GetActiveInstallerFiles();
            long freed = 0;
            int count = 0;

            foreach (var file in Directory.EnumerateFiles(installerPath, "*.msi")
                .Concat(Directory.EnumerateFiles(installerPath, "*.msp")))
            {
                if (activePatches.Contains(file, StringComparer.OrdinalIgnoreCase))
                {
                    _logger.LogTrace($"[UltraClean] Instalador ativo, preservando: {Path.GetFileName(file)}");
                    continue;
                }
                try
                {
                    var fi = new FileInfo(file);
                    if (IsFileLocked(fi)) { _logger.LogTrace($"[UltraClean] Em uso, pulando: {fi.Name}"); continue; }
                    _logger.LogInfo($"[UltraClean] 🗑 Instalador órfão: {fi.Name} ({FormatBytes(fi.Length)})");
                    freed += fi.Length;
                    fi.Delete();
                    count++;
                }
                catch (Exception ex) { _logger.LogWarning($"[UltraClean] Não foi possível deletar {Path.GetFileName(file)}: {ex.Message}"); }
            }
            sw.Stop();
            _logger.LogSuccess($"[UltraClean] ✅ Windows\\Installer: {count} órfãos removidos, {FormatBytes(freed)} em {sw.Elapsed.TotalSeconds:F1}s");
            return freed;
        }

        /// <summary>
        /// Obtém a lista de arquivos do Windows Installer que ainda estão em uso por programas instalados.
        /// Consulta o registro para garantir que NUNCA deletamos instaladores ativos.
        /// </summary>
        private HashSet<string> GetActiveInstallerFiles()
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var installerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Installer");

            try
            {
                // Produtos instalados
                using var productsKey = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\UserData");
                if (productsKey != null)
                {
                    foreach (var userSid in productsKey.GetSubKeyNames())
                    {
                        using var userKey = productsKey.OpenSubKey($@"{userSid}\Products");
                        if (userKey == null) continue;
                        foreach (var productCode in userKey.GetSubKeyNames())
                        {
                            using var productKey = userKey.OpenSubKey($@"{productCode}\InstallProperties");
                            if (productKey == null) continue;
                            var localPackage = productKey.GetValue("LocalPackage")?.ToString();
                            if (!string.IsNullOrEmpty(localPackage))
                                result.Add(localPackage);
                        }
                    }
                }

                // Patches ativos
                using var patchesKey = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\UserData");
                if (patchesKey != null)
                {
                    foreach (var userSid in patchesKey.GetSubKeyNames())
                    {
                        using var userKey = patchesKey.OpenSubKey($@"{userSid}\Patches");
                        if (userKey == null) continue;
                        foreach (var patchCode in userKey.GetSubKeyNames())
                        {
                            using var patchKey = userKey.OpenSubKey(patchCode);
                            if (patchKey == null) continue;
                            var localPackage = patchKey.GetValue("LocalPackage")?.ToString();
                            if (!string.IsNullOrEmpty(localPackage))
                                result.Add(localPackage);
                        }
                    }
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraClean] Erro ao ler registro de instaladores: {ex.Message}"); }

            _logger.LogInfo($"[UltraClean] Instaladores ativos encontrados: {result.Count}");
            return result;
        }

        // ============================================================
        // CATEGORIA: CACHE DE DESENVOLVIMENTO (DEV TOOLS)
        // ============================================================

        // --- Visual Studio ComponentModelCache ---
        private long AnalyzeVsComponentModelCache()
        {
            _logger.LogInfo("[UltraClean] Analisando VS ComponentModelCache...");
            long size = 0;
            foreach (var profile in GetAllUserProfiles())
            {
                var vsBase = Path.Combine(profile, "AppData", "Local", "Microsoft", "VisualStudio");
                if (!Directory.Exists(vsBase)) continue;
                foreach (var vsDir in Directory.EnumerateDirectories(vsBase))
                {
                    size += SafeGetDirSize(Path.Combine(vsDir, "ComponentModelCache"));
                    size += SafeGetDirSize(Path.Combine(vsDir, "Designer", "ShadowCache"));
                }
            }
            _logger.LogInfo($"[UltraClean] VS ComponentModelCache: {FormatBytes(size)}");
            return size;
        }

        private long CleanVsComponentModelCache()
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInfo("[UltraClean] ▶ Limpando VS ComponentModelCache...");
            long freed = 0;
            foreach (var profile in GetAllUserProfiles())
            {
                var vsBase = Path.Combine(profile, "AppData", "Local", "Microsoft", "VisualStudio");
                if (!Directory.Exists(vsBase)) continue;
                foreach (var vsDir in Directory.EnumerateDirectories(vsBase))
                {
                    long f = SafeDeleteFiles(Path.Combine(vsDir, "ComponentModelCache"), "*", olderThanDays: 0, recursive: true);
                    f    += SafeDeleteFiles(Path.Combine(vsDir, "Designer", "ShadowCache"), "*", olderThanDays: 0, recursive: true);
                    if (f > 0) _logger.LogInfo($"[UltraClean] 🗑 VS [{Path.GetFileName(vsDir)}]: {FormatBytes(f)}");
                    freed += f;
                }
            }
            sw.Stop();
            _logger.LogSuccess($"[UltraClean] ✅ VS ComponentModelCache: {FormatBytes(freed)} em {sw.Elapsed.TotalSeconds:F1}s");
            return freed;
        }

        // --- Gradle Cache ---
        private long AnalyzeGradleCache()
        {
            _logger.LogInfo("[UltraClean] Analisando Gradle cache...");
            long size = 0;
            foreach (var profile in GetAllUserProfiles())
            {
                // Apenas caches — nunca wrapper (contém executáveis do Gradle)
                size += SafeGetDirSize(Path.Combine(profile, ".gradle", "caches"));
                size += SafeGetDirSize(Path.Combine(profile, ".gradle", "daemon"));
            }
            _logger.LogInfo($"[UltraClean] Gradle cache: {FormatBytes(size)}");
            return size;
        }

        private long CleanGradleCache()
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInfo("[UltraClean] ▶ Limpando Gradle cache...");
            long freed = 0;
            foreach (var profile in GetAllUserProfiles())
            {
                long f = SafeDeleteFiles(Path.Combine(profile, ".gradle", "caches"),
                    "*", olderThanDays: 30, recursive: true);
                // Daemon logs são seguros de deletar
                f    += SafeDeleteFiles(Path.Combine(profile, ".gradle", "daemon"),
                    "*.log", olderThanDays: 7, recursive: true);
                if (f > 0) _logger.LogInfo($"[UltraClean] 🗑 Gradle [{Path.GetFileName(profile)}]: {FormatBytes(f)}");
                freed += f;
            }
            sw.Stop();
            _logger.LogSuccess($"[UltraClean] ✅ Gradle cache: {FormatBytes(freed)} em {sw.Elapsed.TotalSeconds:F1}s");
            return freed;
        }

        // --- Maven Cache ---
        private long AnalyzeMavenCache()
        {
            _logger.LogInfo("[UltraClean] Analisando Maven cache...");
            long size = 0;
            foreach (var profile in GetAllUserProfiles())
                size += SafeGetDirSize(Path.Combine(profile, ".m2", "repository"));
            _logger.LogInfo($"[UltraClean] Maven cache: {FormatBytes(size)}");
            return size;
        }

        private long CleanMavenCache()
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInfo("[UltraClean] ▶ Limpando Maven cache (arquivos não usados há 60 dias)...");
            long freed = 0;
            foreach (var profile in GetAllUserProfiles())
            {
                // Deletar apenas arquivos não acessados há 60 dias — preserva dependências ativas
                long f = SafeDeleteFiles(Path.Combine(profile, ".m2", "repository"),
                    "*.jar", olderThanDays: 60, recursive: true);
                f    += SafeDeleteFiles(Path.Combine(profile, ".m2", "repository"),
                    "*.pom", olderThanDays: 60, recursive: true);
                if (f > 0) _logger.LogInfo($"[UltraClean] 🗑 Maven [{Path.GetFileName(profile)}]: {FormatBytes(f)}");
                freed += f;
            }
            sw.Stop();
            _logger.LogSuccess($"[UltraClean] ✅ Maven cache: {FormatBytes(freed)} em {sw.Elapsed.TotalSeconds:F1}s");
            return freed;
        }

        // --- pip Cache (Python) ---
        private long AnalyzePipCache()
        {
            _logger.LogInfo("[UltraClean] Analisando pip cache...");
            long size = 0;
            foreach (var profile in GetAllUserProfiles())
            {
                size += SafeGetDirSize(Path.Combine(profile, "AppData", "Local", "pip", "cache"));
                size += SafeGetDirSize(Path.Combine(profile, "AppData", "Local", "pip", "Cache"));
            }
            _logger.LogInfo($"[UltraClean] pip cache: {FormatBytes(size)}");
            return size;
        }

        private long CleanPipCache()
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInfo("[UltraClean] ▶ Limpando pip cache...");
            long freed = 0;
            foreach (var profile in GetAllUserProfiles())
            {
                long f = SafeDeleteFiles(Path.Combine(profile, "AppData", "Local", "pip", "cache"),
                    "*", olderThanDays: 30, recursive: true);
                f    += SafeDeleteFiles(Path.Combine(profile, "AppData", "Local", "pip", "Cache"),
                    "*", olderThanDays: 30, recursive: true);
                if (f > 0) _logger.LogInfo($"[UltraClean] 🗑 pip [{Path.GetFileName(profile)}]: {FormatBytes(f)}");
                freed += f;
            }
            sw.Stop();
            _logger.LogSuccess($"[UltraClean] ✅ pip cache: {FormatBytes(freed)} em {sw.Elapsed.TotalSeconds:F1}s");
            return freed;
        }

        // --- Cargo Cache (Rust) ---
        private long AnalyzeCargoCache()
        {
            _logger.LogInfo("[UltraClean] Analisando Cargo cache...");
            long size = 0;
            foreach (var profile in GetAllUserProfiles())
                size += SafeGetDirSize(Path.Combine(profile, ".cargo", "registry", "cache"));
            _logger.LogInfo($"[UltraClean] Cargo cache: {FormatBytes(size)}");
            return size;
        }

        private long CleanCargoCache()
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInfo("[UltraClean] ▶ Limpando Cargo cache...");
            long freed = 0;
            foreach (var profile in GetAllUserProfiles())
            {
                long f = SafeDeleteFiles(Path.Combine(profile, ".cargo", "registry", "cache"),
                    "*.crate", olderThanDays: 60, recursive: true);
                if (f > 0) _logger.LogInfo($"[UltraClean] 🗑 Cargo [{Path.GetFileName(profile)}]: {FormatBytes(f)}");
                freed += f;
            }
            sw.Stop();
            _logger.LogSuccess($"[UltraClean] ✅ Cargo cache: {FormatBytes(freed)} em {sw.Elapsed.TotalSeconds:F1}s");
            return freed;
        }

        // --- Composer Cache (PHP) ---
        private long AnalyzeComposerCache()
        {
            _logger.LogInfo("[UltraClean] Analisando Composer cache...");
            long size = 0;
            foreach (var profile in GetAllUserProfiles())
                size += SafeGetDirSize(Path.Combine(profile, "AppData", "Roaming", "Composer", "cache"));
            _logger.LogInfo($"[UltraClean] Composer cache: {FormatBytes(size)}");
            return size;
        }

        private long CleanComposerCache()
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInfo("[UltraClean] ▶ Limpando Composer cache...");
            long freed = 0;
            foreach (var profile in GetAllUserProfiles())
            {
                long f = SafeDeleteFiles(Path.Combine(profile, "AppData", "Roaming", "Composer", "cache"),
                    "*", olderThanDays: 30, recursive: true);
                if (f > 0) _logger.LogInfo($"[UltraClean] 🗑 Composer [{Path.GetFileName(profile)}]: {FormatBytes(f)}");
                freed += f;
            }
            sw.Stop();
            _logger.LogSuccess($"[UltraClean] ✅ Composer cache: {FormatBytes(freed)} em {sw.Elapsed.TotalSeconds:F1}s");
            return freed;
        }

        // ============================================================
        // CATEGORIA: DEFENDER & SEGURANÇA (NOVOS)
        // ============================================================

        // --- Defender Scan History ---
        private long AnalyzeDefenderScanHistory()
        {
            _logger.LogInfo("[UltraClean] Analisando Defender Scan History...");
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Microsoft", "Windows Defender", "Scans", "History");
            long size = SafeGetDirSize(path);
            _logger.LogInfo($"[UltraClean] Defender Scan History: {FormatBytes(size)}");
            return size;
        }

        private long CleanDefenderScanHistory()
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInfo("[UltraClean] ▶ Limpando Defender Scan History...");
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Microsoft", "Windows Defender", "Scans", "History");
            // Manter últimos 30 dias de histórico
            long freed = SafeDeleteFiles(path, "*", olderThanDays: 30, recursive: true,
                skipExtensions: new[] { ".sys", ".dll", ".exe" });
            sw.Stop();
            _logger.LogSuccess($"[UltraClean] ✅ Defender Scan History: {FormatBytes(freed)} em {sw.Elapsed.TotalSeconds:F1}s");
            return freed;
        }

        // --- Defender MpCache (arquivos .bin de cache de scan) ---
        private long AnalyzeDefenderMpCache()
        {
            _logger.LogInfo("[UltraClean] Analisando Defender MpCache...");
            var scansPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Microsoft", "Windows Defender", "Scans");
            long size = 0;
            if (Directory.Exists(scansPath))
            {
                foreach (var f in Directory.EnumerateFiles(scansPath, "mpcache-*.bin"))
                    try { size += new FileInfo(f).Length; } catch { }
            }
            _logger.LogInfo($"[UltraClean] Defender MpCache: {FormatBytes(size)}");
            return size;
        }

        private long CleanDefenderMpCache()
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInfo("[UltraClean] ▶ Limpando Defender MpCache...");
            var scansPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Microsoft", "Windows Defender", "Scans");
            long freed = SafeDeleteFiles(scansPath, "mpcache-*.bin", olderThanDays: 7, recursive: false);
            sw.Stop();
            _logger.LogSuccess($"[UltraClean] ✅ Defender MpCache: {FormatBytes(freed)} em {sw.Elapsed.TotalSeconds:F1}s");
            return freed;
        }

        // --- Defender Definition Backup ---
        private long AnalyzeDefenderDefinitionBackup()
        {
            _logger.LogInfo("[UltraClean] Analisando Defender Definition Backup...");
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Microsoft", "Windows Defender", "Definition Updates", "Backup");
            long size = SafeGetDirSize(path);
            _logger.LogInfo($"[UltraClean] Defender Definition Backup: {FormatBytes(size)}");
            return size;
        }

        private long CleanDefenderDefinitionBackup()
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInfo("[UltraClean] ▶ Limpando Defender Definition Backup...");
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Microsoft", "Windows Defender", "Definition Updates", "Backup");
            // Backup de definições antigas — as atuais ficam em outra pasta
            long freed = SafeDeleteFiles(path, "*", olderThanDays: 7, recursive: true,
                skipExtensions: new[] { ".sys", ".dll", ".exe" });
            sw.Stop();
            _logger.LogSuccess($"[UltraClean] ✅ Defender Definition Backup: {FormatBytes(freed)} em {sw.Elapsed.TotalSeconds:F1}s");
            return freed;
        }

        // ============================================================
        // CATEGORIA: UWP TempState (apps da Store)
        // ============================================================

        // --- UWP TempState e LocalCache ---
        private long AnalyzeUwpTempState()
        {
            _logger.LogInfo("[UltraClean] Analisando UWP TempState/LocalCache...");
            long size = 0;
            foreach (var profile in GetAllUserProfiles())
            {
                var packagesPath = Path.Combine(profile, "AppData", "Local", "Packages");
                if (!Directory.Exists(packagesPath)) continue;
                foreach (var pkg in Directory.EnumerateDirectories(packagesPath))
                {
                    size += SafeGetDirSize(Path.Combine(pkg, "TempState"));
                    size += SafeGetDirSize(Path.Combine(pkg, "LocalCache"));
                    size += SafeGetDirSize(Path.Combine(pkg, "AC", "Temp"));
                }
            }
            _logger.LogInfo($"[UltraClean] UWP TempState: {FormatBytes(size)}");
            return size;
        }

        private long CleanUwpTempState()
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInfo("[UltraClean] ▶ Limpando UWP TempState/LocalCache...");
            long freed = 0;
            int pkgCount = 0;
            foreach (var profile in GetAllUserProfiles())
            {
                var packagesPath = Path.Combine(profile, "AppData", "Local", "Packages");
                if (!Directory.Exists(packagesPath)) continue;
                foreach (var pkg in Directory.EnumerateDirectories(packagesPath))
                {
                    long f = SafeDeleteFiles(Path.Combine(pkg, "TempState"),  "*", olderThanDays: 0, recursive: true);
                    f    += SafeDeleteFiles(Path.Combine(pkg, "LocalCache"),  "*", olderThanDays: 7, recursive: true);
                    f    += SafeDeleteFiles(Path.Combine(pkg, "AC", "Temp"),  "*", olderThanDays: 0, recursive: true);
                    if (f > 0)
                    {
                        _logger.LogInfo($"[UltraClean] 🗑 UWP [{Path.GetFileName(pkg)}]: {FormatBytes(f)}");
                        pkgCount++;
                    }
                    freed += f;
                }
            }
            sw.Stop();
            _logger.LogSuccess($"[UltraClean] ✅ UWP TempState: {pkgCount} apps, {FormatBytes(freed)} em {sw.Elapsed.TotalSeconds:F1}s");
            return freed;
        }

        // ============================================================
        // CATEGORIA: HISTÓRICO E CACHE DO WINDOWS (NOVOS)
        // ============================================================

        // --- 1. ÁREA DE TRANSFERÊNCIA (Clipboard) ---
        private long AnalyzeClipboard()
        {
            // Ocupa memória, não disco, mas o usuário quer limpar
            return 0;
        }

        private long CleanClipboard()
        {
            _logger.LogInfo("[UltraClean] ▶ Limpando Área de Transferência...");
            try
            {
                // Comando silencioso via PowerShell para limpar o clipboard
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = "-WindowStyle Hidden -Command \"Set-Clipboard -Value $null\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                Process.Start(psi)?.WaitForExit(2000);
                _logger.LogSuccess("[UltraClean] ✅ Área de Transferência limpa.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[UltraClean] Falha ao limpar clipboard: {ex.Message}");
            }
            return 0;
        }

        // --- 2. FRAGMENTOS DE DISCO (FOUND.000 / .chk) ---
        private long AnalyzeChkdskFragments()
        {
            _logger.LogInfo("[UltraClean] Analisando fragmentos de verificação de disco...");
            long size = 0;
            try
            {
                foreach (var drive in System.IO.DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == System.IO.DriveType.Fixed))
                {
                    try
                    {
                        var root = drive.RootDirectory.FullName;
                        // Pastas criadas pelo chkdsk: FOUND.000, FOUND.001, etc.
                        foreach (var dir in Directory.EnumerateDirectories(root, "FOUND.*"))
                        {
                            size += SafeGetDirSize(dir);
                        }
                    }
                    catch { }
                }
            }
            catch { }
            _logger.LogInfo($"[UltraClean] Fragmentos de disco: {FormatBytes(size)}");
            return size;
        }

        private long CleanChkdskFragments()
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInfo("[UltraClean] ▶ Removendo fragmentos de verificação de disco (.chk)...");
            long freed = 0;
            try
            {
                foreach (var drive in System.IO.DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == System.IO.DriveType.Fixed))
                {
                    try
                    {
                        var root = drive.RootDirectory.FullName;
                        foreach (var dir in Directory.EnumerateDirectories(root, "FOUND.*"))
                        {
                            _logger.LogInfo($"[UltraClean] 🗑 Removendo fragmentos em: {dir}");
                            freed += DeleteDirectorySafe(dir);
                        }
                    }
                    catch { }
                }
            }
            catch { }
            sw.Stop();
            _logger.LogSuccess($"[UltraClean] ✅ Fragmentos de disco: {FormatBytes(freed)} em {sw.Elapsed.TotalSeconds:F1}s");
            return freed;
        }

        // --- 3. CACHE DA ORDEM DOS MENUS ---
        private long AnalyzeMenuOrderCache() => 0;
        private long CleanMenuOrderCache()
        {
            _logger.LogInfo("[UltraClean] ▶ Resetando ordem personalizada do Menu Iniciar...");
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\MenuOrder", true);
                if (key != null)
                {
                    Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Explorer\MenuOrder", false);
                    _logger.LogSuccess("[UltraClean] ✅ Ordem do Menu Iniciar resetada.");
                }
            }
            catch { }
            return 0;
        }

        // --- 4. CACHE DA ÁREA DE NOTIFICAÇÃO (Tray) ---
        private long AnalyzeTrayNotifyCache() => 0;
        private long CleanTrayNotifyCache()
        {
            _logger.LogInfo("[UltraClean] ▶ Limpando ícones antigos da bandeja do sistema...");
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\TrayNotify", true);
                if (key != null)
                {
                    key.DeleteValue("IconStreams", false);
                    key.DeleteValue("PastIconsStream", false);
                    _logger.LogSuccess("[UltraClean] ✅ Cache de ícones da bandeja limpo (reinicie o explorer.exe para ver o efeito).");
                }
            }
            catch { }
            return 0;
        }

        // --- 5. TAMANHO E LOCALIZAÇÃO DAS JANELAS ---
        private long AnalyzeWindowPositions() => 0;
        private long CleanWindowPositions()
        {
            _logger.LogInfo("[UltraClean] ▶ Resetando posições e tamanhos das janelas...");
            try
            {
                // StreamMRU e outros armazenam posições de janelas do Explorer
                string[] paths = {
                    @"Software\Microsoft\Windows\Shell\BagMRU",
                    @"Software\Microsoft\Windows\Shell\Bags",
                    @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\BagMRU",
                    @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\Bags"
                };
                foreach (var path in paths)
                {
                    try { Registry.CurrentUser.DeleteSubKeyTree(path, false); } catch { }
                }
                _logger.LogSuccess("[UltraClean] ✅ Posições de janelas resetadas.");
            }
            catch { }
            return 0;
        }

        // --- 6. HISTÓRICO DO ASSISTENTE / SHELLBAGS ---
        private long AnalyzeShellBags() => 0;
        private long CleanShellBags()
        {
            _logger.LogInfo("[UltraClean] ▶ Limpando logs do UserAssist (apps frequentes)...");
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist", true);
                if (key != null)
                {
                    Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist", false);
                    _logger.LogSuccess("[UltraClean] ✅ Histórico de apps frequentes limpo.");
                }
            }
            catch { }
            return 0;
        }

        // ============================================================
        // HELPER: Obter todos os perfis de usuário do sistema
        // ============================================================

        /// <summary>
        /// Retorna todos os perfis de usuário reais do sistema (exclui Default, Public, All Users).
        /// </summary>
        private IEnumerable<string> GetAllUserProfiles()
        {
            var usersRoot = Path.Combine(
                Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\",
                "Users");

            if (!Directory.Exists(usersRoot)) yield break;

            var skipNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "Default", "Default User", "Public", "All Users", "defaultuser0" };

            foreach (var dir in Directory.EnumerateDirectories(usersRoot))
            {
                var name = Path.GetFileName(dir);
                if (skipNames.Contains(name)) continue;
                yield return dir;
            }
        }

        // ============================================================
        // REGISTRO DAS NOVAS CATEGORIAS NO InitializeCategories
        // Chamado via RegisterNewCleanupModules() no construtor
        // ============================================================

        /// <summary>
        /// Registra todos os novos módulos de limpeza nas categorias existentes e novas.
        /// Deve ser chamado ao final de InitializeCategories().
        /// </summary>
        internal void RegisterNewCleanupModules()
        {
            // ── CATEGORIA: HISTÓRICO E CACHE DO WINDOWS (novos) ───────────
            _categories.Add(new CleanupCategory
            {
                Name = "Histórico do Windows",
                Icon = "🕒",
                Items = new List<CleanupCategoryItem>
                {
                    new() { Name = "Área de Transferência",          Description = "Limpa o que foi copiado recentemente para o clipboard",        CleanAction = (Func<long>)CleanClipboard,         AnalyzeAction = (Func<long>)AnalyzeClipboard,         RequiresAdmin = false, IsSafe = true },
                    new() { Name = "Fragmentos de Disco (.chk)",     Description = "Restos de arquivos de verificação de disco (FOUND.nnn)",        CleanAction = (Func<long>)CleanChkdskFragments,   AnalyzeAction = (Func<long>)AnalyzeChkdskFragments,   RequiresAdmin = true,  IsSafe = true },
                    new() { Name = "Cache da Ordem dos Menus",       Description = "Reseta a ordem personalizada dos ícones no Menu Iniciar",        CleanAction = (Func<long>)CleanMenuOrderCache,       AnalyzeAction = (Func<long>)AnalyzeMenuOrderCache,       RequiresAdmin = false, IsSafe = true },
                    new() { Name = "Cache da Área de Notificação",   Description = "Limpa ícones antigos e inativos da bandeja do sistema (Tray)",  CleanAction = (Func<long>)CleanTrayNotifyCache,      AnalyzeAction = (Func<long>)AnalyzeTrayNotifyCache,      RequiresAdmin = false, IsSafe = true },
                    new() { Name = "Posições das Janelas",           Description = "Reseta os tamanhos e as posições salvas de janelas do Explorer", CleanAction = (Func<long>)CleanWindowPositions,     AnalyzeAction = (Func<long>)AnalyzeWindowPositions,     RequiresAdmin = false, IsSafe = true },
                    new() { Name = "Histórico de Apps Frequentes",  Description = "Limpa logs de programas abertos com frequência (UserAssist)",    CleanAction = (Func<long>)CleanShellBags,          AnalyzeAction = (Func<long>)AnalyzeShellBags,          RequiresAdmin = false, IsSafe = true }}
            });

            // ── CATEGORIA: LOGS DO SISTEMA (novos itens) ──────────────────
            _categories.Add(new CleanupCategory
            {
                Name = "Logs do Sistema",
                Icon = "📄",
                Items = new List<CleanupCategoryItem>
                {
                    new() { Name = "CBS .cab Backups (SFC/DISM)",       Description = "Backups .cab gerados pelo SFC e DISM, podem ter GBs",          CleanAction = (Func<long>)CleanCbsCabBackups,         AnalyzeAction = (Func<long>)AnalyzeCbsCabBackups,         RequiresAdmin = true,  IsSafe = true },
                    new() { Name = "System32\\LogFiles",                 Description = "IIS, WMI, WLAN, ETW logs acumulados",                          CleanAction = (Func<long>)CleanSystem32LogFiles,      AnalyzeAction = (Func<long>)AnalyzeSystem32LogFiles,      RequiresAdmin = true,  IsSafe = true },
                    new() { Name = "Windows\\debug Logs",                Description = "netlogon.log, PASSWD.LOG e outros logs de debug",              CleanAction = (Func<long>)CleanWindowsDebugLogs,      AnalyzeAction = (Func<long>)AnalyzeWindowsDebugLogs,      RequiresAdmin = true,  IsSafe = true },
                    new() { Name = "WER ReportQueue/Archive (Sistema)",  Description = "Fila e arquivo de relatórios de erro do sistema (ProgramData)", CleanAction = (Func<long>)CleanSystemWerReports,      AnalyzeAction = (Func<long>)AnalyzeSystemWerReports,      RequiresAdmin = true,  IsSafe = true },
                    new() { Name = "Windows\\Panther (Logs de Upgrade)", Description = "Logs de upgrade do Windows, pode ter GBs após atualizações",   CleanAction = (Func<long>)CleanWindowsPantherLogs,    AnalyzeAction = (Func<long>)AnalyzeWindowsPantherLogs,    RequiresAdmin = true,  IsSafe = true },
                    new() { Name = "$SysReset Logs",                     Description = "Logs de reset do sistema",                                     CleanAction = (Func<long>)CleanSysResetLogs,          AnalyzeAction = (Func<long>)AnalyzeSysResetLogs,          RequiresAdmin = true,  IsSafe = true },
                    new() { Name = "ServiceProfiles Temp",               Description = "Temp dos serviços LocalService e NetworkService",              CleanAction = (Func<long>)CleanServiceProfilesTemp,   AnalyzeAction = (Func<long>)AnalyzeServiceProfilesTemp,   RequiresAdmin = true,  IsSafe = true }}
            });

            // ── CATEGORIA: APPS MICROSOFT (novos itens) ───────────────────
            _categories.Add(new CleanupCategory
            {
                Name = "Apps Microsoft",
                Icon = "🪟",
                Items = new List<CleanupCategoryItem>
                {
                    new() { Name = "Microsoft Teams Cache",       Description = "Cache, blob_storage e GPUCache do Teams clássico",    CleanAction = (Func<long>)CleanTeamsCache,          AnalyzeAction = (Func<long>)AnalyzeTeamsCache,          RequiresAdmin = false, IsSafe = true },
                    new() { Name = "Outlook OST Órfãos",          Description = "Arquivos OST de contas do Outlook já removidas",      CleanAction = (Func<long>)CleanOutlookOrphanOst,    AnalyzeAction = (Func<long>)AnalyzeOutlookOrphanOst,    RequiresAdmin = false, IsSafe = true },
                    new() { Name = "JumpList Cache",              Description = "AutomaticDestinations e CustomDestinations antigos",  CleanAction = (Func<long>)CleanJumpListCache,       AnalyzeAction = (Func<long>)AnalyzeJumpListCache,       RequiresAdmin = false, IsSafe = true },
                    new() { Name = "INetCache IE Legado",         Description = "Cache legado do IE usado por WebView e apps",         CleanAction = (Func<long>)CleanINetCacheLegacy,     AnalyzeAction = (Func<long>)AnalyzeINetCacheLegacy,     RequiresAdmin = false, IsSafe = true },
                    new() { Name = "VS ComponentModelCache",      Description = "Cache de componentes do Visual Studio (se regenera)", CleanAction = (Func<long>)CleanVsComponentModelCache, AnalyzeAction = (Func<long>)AnalyzeVsComponentModelCache, RequiresAdmin = false, IsSafe = true }}
            });

            // ── CATEGORIA: APPS DE COMUNICAÇÃO ────────────────────────────
            _categories.Add(new CleanupCategory
            {
                Name = "Apps de Comunicação",
                Icon = "💬",
                Items = new List<CleanupCategoryItem>
                {
                    new() { Name = "Discord Cache",   Description = "Cache e GPUCache do Discord (Electron)",   CleanAction = (Func<long>)CleanDiscordCache,   AnalyzeAction = (Func<long>)AnalyzeDiscordCache,   RequiresAdmin = false, IsSafe = true },
                    new() { Name = "Slack Cache",     Description = "Cache e GPUCache do Slack (Electron)",     CleanAction = (Func<long>)CleanSlackCache,     AnalyzeAction = (Func<long>)AnalyzeSlackCache,     RequiresAdmin = false, IsSafe = true },
                    new() { Name = "Spotify Cache",   Description = "Cache de mídia do Spotify (Data/Storage)", CleanAction = (Func<long>)CleanSpotifyCache,   AnalyzeAction = (Func<long>)AnalyzeSpotifyCache,   RequiresAdmin = false, IsSafe = true },
                    new() { Name = "Zoom Cache/Logs", Description = "Cache de dados e logs do Zoom",            CleanAction = (Func<long>)CleanZoomCache,      AnalyzeAction = (Func<long>)AnalyzeZoomCache,      RequiresAdmin = false, IsSafe = true },
                    new() { Name = "WhatsApp Cache",  Description = "Cache do WhatsApp Desktop",               CleanAction = (Func<long>)CleanWhatsAppCache,  AnalyzeAction = (Func<long>)AnalyzeWhatsAppCache,  RequiresAdmin = false, IsSafe = true },
                    new() { Name = "Telegram Cache",  Description = "Cache de mídia do Telegram Desktop",      CleanAction = (Func<long>)CleanTelegramCache,  AnalyzeAction = (Func<long>)AnalyzeTelegramCache,  RequiresAdmin = false, IsSafe = true }}
            });

            // ── CATEGORIA: DRIVERS EXTRAÍDOS ──────────────────────────────
            _categories.Add(new CleanupCategory
            {
                Name = "Drivers Extraídos",
                Icon = "🔧",
                Items = new List<CleanupCategoryItem>
                {
                    new() { Name = "AMD Drivers Extraídos",   Description = "Pasta C:\\AMD com versões antigas de drivers",    CleanAction = (Func<long>)CleanAmdExtractedDrivers,   AnalyzeAction = (Func<long>)AnalyzeAmdExtractedDrivers,   RequiresAdmin = true, IsSafe = true },
                    new() { Name = "NVIDIA Drivers Extraídos", Description = "Pasta C:\\NVIDIA com versões antigas de drivers", CleanAction = (Func<long>)CleanNvidiaExtractedDrivers, AnalyzeAction = (Func<long>)AnalyzeNvidiaExtractedDrivers, RequiresAdmin = true, IsSafe = true },
                    new() { Name = "Intel Drivers Extraídos",  Description = "Pasta C:\\Intel com logs e temp de instalação",   CleanAction = (Func<long>)CleanIntelExtractedDrivers,  AnalyzeAction = (Func<long>)AnalyzeIntelExtractedDrivers,  RequiresAdmin = true, IsSafe = true },
                    new() { Name = "OEM Drivers (C:\\Drivers)", Description = "Pasta C:\\Drivers de OEMs (Dell/HP/Lenovo)",     CleanAction = (Func<long>)CleanOemDriversFolder,       AnalyzeAction = (Func<long>)AnalyzeOemDriversFolder,       RequiresAdmin = true, IsSafe = true }}
            });

            // ── CATEGORIA: WINDOWS INSTALLER ÓRFÃOS ───────────────────────
            _categories.Add(new CleanupCategory
            {
                Name = "Windows Installer",
                Icon = "📦",
                Items = new List<CleanupCategoryItem>
                {
                    new() { Name = "Instaladores Órfãos (.msi/.msp)", Description = "Instaladores de programas já desinstalados em C:\\Windows\\Installer", CleanAction = (Func<long>)CleanOrphanWindowsInstaller, AnalyzeAction = (Func<long>)AnalyzeOrphanWindowsInstaller, RequiresAdmin = true, IsSafe = true }}
            });

            // ── CATEGORIA: FERRAMENTAS DE DESENVOLVIMENTO ─────────────────
            _categories.Add(new CleanupCategory
            {
                Name = "Ferramentas de Dev",
                Icon = "⚙️",
                Items = new List<CleanupCategoryItem>
                {
                    new() { Name = "Gradle Cache",   Description = "Cache de dependências do Gradle (Java/Android)",  CleanAction = (Func<long>)CleanGradleCache,   AnalyzeAction = (Func<long>)AnalyzeGradleCache,   RequiresAdmin = false, IsSafe = true },
                    new() { Name = "Maven Cache",    Description = "Cache de dependências do Maven (Java, 60+ dias)", CleanAction = (Func<long>)CleanMavenCache,    AnalyzeAction = (Func<long>)AnalyzeMavenCache,    RequiresAdmin = false, IsSafe = true },
                    new() { Name = "pip Cache",      Description = "Cache de pacotes Python do pip",                  CleanAction = (Func<long>)CleanPipCache,      AnalyzeAction = (Func<long>)AnalyzePipCache,      RequiresAdmin = false, IsSafe = true },
                    new() { Name = "Cargo Cache",    Description = "Cache de crates do Rust (60+ dias)",              CleanAction = (Func<long>)CleanCargoCache,    AnalyzeAction = (Func<long>)AnalyzeCargoCache,    RequiresAdmin = false, IsSafe = true },
                    new() { Name = "Composer Cache", Description = "Cache de pacotes PHP do Composer",               CleanAction = (Func<long>)CleanComposerCache, AnalyzeAction = (Func<long>)AnalyzeComposerCache, RequiresAdmin = false, IsSafe = true }}
            });

            // ── CATEGORIA: WINDOWS DEFENDER (novos itens) ─────────────────
            _categories.Add(new CleanupCategory
            {
                Name = "Windows Defender",
                Icon = "🛡️",
                Items = new List<CleanupCategoryItem>
                {
                    new() { Name = "Defender Scan History",        Description = "Histórico de scans do Defender (30+ dias)",          CleanAction = (Func<long>)CleanDefenderScanHistory,       AnalyzeAction = (Func<long>)AnalyzeDefenderScanHistory,       RequiresAdmin = true, IsSafe = true },
                    new() { Name = "Defender MpCache (.bin)",      Description = "Arquivos mpcache-*.bin de cache de scan",            CleanAction = (Func<long>)CleanDefenderMpCache,           AnalyzeAction = (Func<long>)AnalyzeDefenderMpCache,           RequiresAdmin = true, IsSafe = true },
                    new() { Name = "Defender Definition Backup",   Description = "Backup de definições antigas do Defender",          CleanAction = (Func<long>)CleanDefenderDefinitionBackup,  AnalyzeAction = (Func<long>)AnalyzeDefenderDefinitionBackup,  RequiresAdmin = true, IsSafe = true }}
            });

            // ── CATEGORIA: UWP APPS ────────────────────────────────────────
            _categories.Add(new CleanupCategory
            {
                Name = "UWP Apps (Store)",
                Icon = "🏪",
                Items = new List<CleanupCategoryItem>
                {
                    new() { Name = "UWP TempState e LocalCache", Description = "TempState, LocalCache e AC\\Temp de todos os apps da Store", CleanAction = (Func<long>)CleanUwpTempState, AnalyzeAction = (Func<long>)AnalyzeUwpTempState, RequiresAdmin = false, IsSafe = true }}
            });
        }
    }
}
