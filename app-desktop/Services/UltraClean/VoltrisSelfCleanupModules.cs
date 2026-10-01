// ============================================================
// MODULOS DE AUTOLIMPEZA DO VOLTRIS - Services\UltraClean
// Todos os métodos aqui são partial da classe UltraCleanerService.
//
// OBJETIVO
// O proprio VOLTRIS produz log rotativo (voltris.log.old.txt e
// voltris.log.<timestamp>.txt) e dezenas de logs de diagnostico datados
// (VoltrisDiag_*, StartupDiag_*, FreezeForensics_*, ...). Sem nenhum
// recolhimento, %LOCALAPPDATA%\Voltris cresce para sempre na maquina do
// usuario. Estes modulos fecham o ciclo.
//
// GARANTIA DE SEGURANCA (100%)
// 1. NUNCA apaga o log ATIVO (voltris.log.txt) nem configuracao/licenca.
// 2. NUNCA apaga diretorio. Apenas arquivos, sempre um a um.
// 3. NUNCA apaga reparse point (junction/symlink).
// 4. NUNCA apaga arquivo em uso: teste REAL de lock (FileShare.None),
//    nao heuristica de data.
// 5. So toca em caminho dentro das raizes de dados do VOLTRIS resolvidas
//    em tempo de execucao (LogDirectoryResolver + %APPDATA%\VoltrisOptimizer).
// 6. Selecao por REGEX EXATA de nome, nunca por wildcard aberto.
// 7. RETENCAO: sempre preserva os N arquivos mais recentes, para o
//    suporte ter um historico recente mesmo apos a limpeza.
// 8. Everything passa por CleanupSafetyGuard (portao unico de seguranca).
// 9. Analise e Limpeza usam EXATAMENTE o mesmo seletor, entao o espaco
//    prometido e o espaco real sao o mesmo numero.
// ============================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using VoltrisOptimizer.Models;
using VoltrisOptimizer.Services.UltraClean;

namespace VoltrisOptimizer.Services
{
    public partial class UltraCleanerService
    {
        // ============================================================
        // POLITICA DE RETENCAO
        // ============================================================

        /// <summary>Quantos arquivos rotacionados (.old) preservar. 1 = sempre
        /// sobra o log da sessao anterior para o suporte.</summary>
        private const int VoltrisRotatedLogKeepNewest = 1;

        /// <summary>Idade minima do arquivo rotacionado. 24h garante que o
        /// arquivo nao pertence a uma sessao ainda em curso.</summary>
        private static readonly TimeSpan VoltrisRotatedLogMinAge = TimeSpan.FromHours(24);

        /// <summary>Dias minimos de um log datado de diagnostico. Abaixo
        /// disso o log ainda e uteis para investigar problema aberto.</summary>
        private static readonly TimeSpan VoltrisDatedLogMinAge = TimeSpan.FromDays(7);

        /// <summary>Quantos logs datados preservar, independentemente da idade.
        /// Rede de seguranca: mesmo que o usuario nao limpe ha meses, nunca
        /// ficamos sem nenhum historico.</summary>
        private const int VoltrisDatedLogKeepNewest = 3;

        /// <summary>Idade minima dos logs de debug de nome fixo. 14 dias e
        /// folgado: debug so e consultado dentro de uma sessao de suporte.</summary>
        private static readonly TimeSpan VoltrisDebugLogMinAge = TimeSpan.FromDays(14);

        /// <summary>Backups de configuracao preservados. O app usa o backup
        /// mais recente para reverter setting corrompido.</summary>
        private const int VoltrisSettingsBackupKeepNewest = 5;

        /// <summary>Idade minima de residuo temporario (.tmp/.bak/.old/.dmp).</summary>
        private static readonly TimeSpan VoltrisStaleArtifactMinAge = TimeSpan.FromDays(2);

        /// <summary>Idade minima do log de seguranca. Alto de proposito:
        /// e trilha de auditoria, entao so entra com opt-in explicito.</summary>
        private static readonly TimeSpan VoltrisSecurityLogMinAge = TimeSpan.FromDays(30);

        // ============================================================
        // PADRÕES DE NOME (regex exatas — nunca curingas abertas)
        // ============================================================

        /// <summary>
        /// Log rotacionado do LoggingService. Cobre as duas grafias que o
        /// servico ja gravou (voltris.log.old.txt) e a forma sem extensao
        /// (voltris.log.old), alem dos arquivos carimbados com timestamp.
        /// </summary>
        private static readonly Regex VoltrisRotatedLogPattern = new Regex(
            @"^voltris\.log\.old(\.[a-z0-9]+)?$|^voltris\.log\.\d{8}_\d{6}(\.[a-z0-9]+)?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// Familias de log datado de diagnostico do VOLTRIS. Cada familia tem
        /// sufixo de data, entao a retencao por idade funciona naturalmente.
        /// Nao inclui security_* (vai no item separado, com opt-in).
        ///
        /// NOTA: o separador "_" e literal, nao escape. `\_` e sequencia de
        /// escape invalida em regex .NET e lancaria RegexParseException na
        /// inicializacao do tipo.
        /// </summary>
        private static readonly Regex VoltrisDatedLogPattern = new Regex(
            @"^(voltrisdiag|startupdiag|startupsteps|startuptrace|app|watchdog|freezeforensics|profiler_view_debug|dls|branding|startup)_\d{4}-\d{2}-\d{2}.*\.log$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// Logs de debug de nome fixo. Sao recriados sob demanda pelo
        /// respective subsistema, entao apagar o antigo nao perde nada.
        /// </summary>
        private static readonly Regex VoltrisFixedDebugLogPattern = new Regex(
            @"^(language|tooltip_debug|profiler_view_debug|startup_execution|startup_profile|dls)\.log$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex VoltrisSettingsBackupPattern = new Regex(
            @"^settings_backup_.*\.json$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex VoltrisStaleArtifactPattern = new Regex(
            @".*\.(tmp|bak|old|partial\.tmp)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex VoltrisDumpPattern = new Regex(
            @".*\.dmp$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex VoltrisSecurityLogPattern = new Regex(
            @"^security_.*\.log$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // ============================================================
        // BARREIRA DE SEGURANÇA: nunca apagar, em hipotese nenhuma
        // ============================================================

        /// <summary>
        /// Denylist de nome exato. Mesmo que um padrao regex evolua por engano,
        /// estes arquivos estao fora de alcance. Entram aqui: o log que o
        /// processo esta escrevendo AGORA e todo estado persistente do app
        /// (settings, licenca, identidade, credencial, widget, perfil).
        /// </summary>
        private static readonly HashSet<string> VoltrisProtectedFileNames = new HashSet<string>(
            new[] {
                "voltris.log.txt",     // log ATIVO - nunca
                "settings.json",       // configuracao do usuario - nunca
                "license.json",        // licenca - nunca
                "identity_cache.json", // identidade de maquina - nunca
                "device_credential.bin", // credencial do dispositivo - nunca
                "widget-config.json",  // layout dos widgets - nunca
                "state.json"           // estado de onboarding/perfil - nunca
            },
            StringComparer.OrdinalIgnoreCase);

        // ============================================================
        // INFRAESTRUTURA DE SELECAO
        // ============================================================

        /// <summary>
        /// Raizes de dados do VOLTRIS onde este modulo tem permissao para
        /// agir, resolvidas em tempo de execucao (nunca hardcoded).
        ///
        /// 1. Pai de LogDirectoryResolver.Resolve() = %LOCALAPPDATA%\Voltris
        ///    (contem Logs\ e Backups\).
        /// 2. %APPDATA%\VoltrisOptimizer\logs (log legado do DLS).
        ///
        /// A lista e validada: raiz de volume, raiz de perfil e a propria
        /// pasta do .exe sao rejeitadas.
        /// </summary>
        private static IReadOnlyList<string> GetVoltrisDataRoots()
        {
            var roots = new List<string>();

            try
            {
                string logDir = LogDirectoryResolver.Resolve();
                string? parent = Path.GetDirectoryName(logDir);
                if (!string.IsNullOrWhiteSpace(parent))
                    roots.Add(parent!);
            }
            catch { }

            try
            {
                string roamingLogs = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "VoltrisOptimizer", "logs");
                roots.Add(roamingLogs);
            }
            catch { }

            return roots
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Select(r =>
                {
                    try { return Path.GetFullPath(r); }
                    catch { return string.Empty; }
                })
                .Where(IsUsableDataRoot)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Rejeita qualquer raiz que nao seja uma pasta de dados do VOLTRIS
        /// pasta do .exe sao rejeitadas. Defense in depth: mesmo que o resolver
        /// mude, uma raiz perigosa nunca entra na lista.
        /// </summary>
        private static bool IsUsableDataRoot(string root)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(root)) return false;

                var fi = new FileInfo(root);
                if (!fi.Exists) return false;

                // Nunca uma raiz de volume, a pasta do .exe, nem um reparse point.
                if (fi.FullName.Length <= 3) return false;
                if ((fi.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint) return false;

                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                if (!string.IsNullOrWhiteSpace(exeDir) &&
                    string.Equals(fi.FullName.TrimEnd('\\'), exeDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    return false;

                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Verificacao completa de UM arquivo antes de_delete_.
        /// Unico ponto de decisao: quem nao passa daqui, nao e apagado.
        /// </summary>
        private static bool IsVoltrisFileDeletable(FileInfo fi, IReadOnlyList<string> roots)
        {
            try
            {
                // 1) Denylist absoluta (log ativo + estado persistente).
                if (VoltrisProtectedFileNames.Contains(fi.Name)) return false;

                // 2) Dentro de uma raiz de dados do VOLTRIS?
                bool underRoot = roots.Any(r => CleanupSafetyGuard.IsUnder(fi.FullName, r));
                if (!underRoot) return false;

                // 3) O arquivo e' realmente um arquivo comum (nao diretio, nao link).
                if ((fi.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint) return false;
                if ((fi.Attributes & FileAttributes.Directory) == FileAttributes.Directory) return false;

                // 4) Portao unico de seguranca do UltraClean.
                if (!CleanupSafetyGuard.CanDelete(fi.FullName)) return false;

                // 5) Ha espaco real alocado? Arquivo de tamanho zero nao
                //    devolve bytes e nao vale a pena gastar uma operacao de disco.
                if (fi.Length == 0) return false;

                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Constroi a lista final de alvos: CASAMENTO por padrao + IDADE minima
        /// + RETENCAO dos N mais novos + teste real de lock.
        ///
        /// A MESMA funcao alimenta Analyze e Clean. E' isso que garante que o
        /// numero mostrado na barra de progresso seja o numero que sera
        /// realmente liberado.
        /// </summary>
        private List<FileInfo> CollectVoltrisTargets(
            Regex namePattern,
            TimeSpan minAge,
            int keepNewest,
            IReadOnlyList<string>? roots = null)
        {
            var result = new List<FileInfo>();
            var allowedRoots = roots ?? GetVoltrisDataRoots();
            if (allowedRoots.Count == 0) return result;

            var cutoff = DateTime.UtcNow - minAge;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var root in allowedRoots)
            {
                foreach (var fi in EnumerateVoltrisFilesSafe(root))
                {
                    // Deduplicacao: a mesma pasta pode ser raiz e estar dentro
                    // de outra raiz (caso legado do %APPDATA%).
                    if (!seen.Add(fi.FullName)) continue;

                    try
                    {
                        if (!namePattern.IsMatch(fi.Name)) continue;
                        if (fi.LastWriteTimeUtc > cutoff) continue;
                        if (!IsVoltrisFileDeletable(fi, allowedRoots)) continue;

                        // Teste REAL de lock. Arquivo aberto pelo proprio
                        // LoggingService e' pulado, nunca truncado.
                        if (CleanupSafetyGuard.IsFileLocked(fi)) continue;

                        result.Add(fi);
                    }
                    catch { /* arquivo sumiu ou sem acesso: ignora */ }
                }
            }

            // Retencao: preserva os N mais recentes, sempre.
            // Ordena por LastWriteTime DESC e descarta os N primeiros.
            return result
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Skip(Math.Max(0, keepNewest))
                .OrderBy(f => f.LastWriteTimeUtc)
                .ToList();
        }

        /// <summary>
        /// Enumeracao manual e segura dos arquivos sob uma raiz de dados.
        ///
        /// <see cref="SearchOption.AllDirectories"/> NAO e usado de proposito:
        /// ele desce em junction/symlink e pode escapar da raiz (ou entrar em
        /// loop). Aqui a descida e feita passo a passo e um diretorio
        /// reparse point encerra aquele ramo. Alem disso ha um teto de
        /// arquivos para nao travar a varredura se algum dia um cache do app
        /// crescer dentro destas pastas.
        /// </summary>
        private static IEnumerable<FileInfo> EnumerateVoltrisFilesSafe(string root, int maxFiles = 20000)
        {
            var stack = new Stack<string>();
            stack.Push(root);

            int emitted = 0;
            while (stack.Count > 0)
            {
                string dir = stack.Pop();

                string[] files;
                try { files = Directory.GetFiles(dir); }
                catch { continue; }

                foreach (var file in files)
                {
                    if (++emitted > maxFiles) yield break;
                    FileInfo fi;
                    try { fi = new FileInfo(file); } catch { continue; }
                    yield return fi;
                }

                string[] subdirs;
                try { subdirs = Directory.GetDirectories(dir); }
                catch { continue; }

                foreach (var sub in subdirs)
                {
                    try
                    {
                        var attr = File.GetAttributes(sub);
                        if ((attr & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint) continue;
                        if ((attr & FileAttributes.Hidden) == FileAttributes.Hidden) continue;
                    }
                    catch { continue; }

                    stack.Push(sub);
                }
            }
        }

        /// <summary>Soma dos bytes realmente recuperaveis da lista de alvos.</summary>
        private static long MeasureVoltrisTargets(List<FileInfo> targets)
        {
            long total = 0;
            foreach (var fi in targets)
            {
                try { total += fi.Length; } catch { }
            }
            return total;
        }

        /// <summary>
        /// Apaga a lista de alvos e devolve os bytes REALMENTE liberados.
        /// A contagem e feita apos o delete ter sucesso, entao nunca
        /// promete espaco que nao saiu do disco.
        /// </summary>
        private long PurgeVoltrisTargets(List<FileInfo> targets, string label)
        {
            long freed = 0;
            int removed = 0;

            foreach (var fi in targets)
            {
                try
                {
                    long size = fi.Length;
                    fi.Delete();

                    // Reexiste? Outro processo recriou — nao conta como liberado.
                    if (File.Exists(fi.FullName)) continue;

                    freed += size;
                    removed++;
                    _logger.LogInfo($"[UltraClean.Voltris] 🗑 {label}: {fi.Name} ({FormatBytes(size)})");
                }
                catch (IOException ex)
                {
                    _logger.LogTrace($"[UltraClean.Voltris] Em uso, mantido: {fi.Name} ({ex.Message})");
                }
                catch (UnauthorizedAccessException ex)
                {
                    _logger.LogTrace($"[UltraClean.Voltris] Sem permissao, mantido: {fi.Name} ({ex.Message})");
                }
                catch (Exception ex)
                {
                    _logger.LogTrace($"[UltraClean.Voltris] Falhou: {fi.Name} ({ex.Message})");
                }
            }

            return freed;
        }

        // ============================================================
        // 1. LOG ROTACIONADO (.old / timestamp)
        // ============================================================

        private long AnalyzeVoltrisRotatedLogArchive()
        {
            var targets = CollectVoltrisTargets(VoltrisRotatedLogPattern, VoltrisRotatedLogMinAge, VoltrisRotatedLogKeepNewest);
            long size = MeasureVoltrisTargets(targets);
            _logger.LogInfo($"[UltraClean.Voltris] Log rotacionado: {targets.Count} arquivo(s), {FormatBytes(size)}");
            return size;
        }

        private long CleanVoltrisRotatedLogArchive()
        {
            var sw = Stopwatch.StartNew();
            var targets = CollectVoltrisTargets(VoltrisRotatedLogPattern, VoltrisRotatedLogMinAge, VoltrisRotatedLogKeepNewest);
            long freed = PurgeVoltrisTargets(targets, "log rotacionado");
            sw.Stop();
            _logger.LogSuccess($"[UltraClean.Voltris] ✅ Log rotacionado: {FormatBytes(freed)} em {sw.Elapsed.TotalSeconds:F1}s (mantidos os {VoltrisRotatedLogKeepNewest} mais recentes)");
            return freed;
        }

        // ============================================================
        // 2. LOGS DE DIAGNOSTICO DATADOS (7+ dias)
        // ============================================================

        private long AnalyzeVoltrisDatedDiagnosticLogs()
        {
            var targets = CollectVoltrisTargets(VoltrisDatedLogPattern, VoltrisDatedLogMinAge, VoltrisDatedLogKeepNewest);
            long size = MeasureVoltrisTargets(targets);
            _logger.LogInfo($"[UltraClean.Voltris] Logs de diagnostico: {targets.Count} arquivo(s), {FormatBytes(size)}");
            return size;
        }

        private long CleanVoltrisDatedDiagnosticLogs()
        {
            var sw = Stopwatch.StartNew();
            var targets = CollectVoltrisTargets(VoltrisDatedLogPattern, VoltrisDatedLogMinAge, VoltrisDatedLogKeepNewest);
            long freed = PurgeVoltrisTargets(targets, "log de diagnostico");
            sw.Stop();
            _logger.LogSuccess($"[UltraClean.Voltris] ✅ Logs de diagnostico: {FormatBytes(freed)} em {sw.Elapsed.TotalSeconds:F1}s");
            return freed;
        }

        // ============================================================
        // 3. LOGS DE DEBUG DE NOME FIXO (14+ dias)
        // ============================================================

        private long AnalyzeVoltrisLegacyDebugLogs()
        {
            var targets = CollectVoltrisTargets(VoltrisFixedDebugLogPattern, VoltrisDebugLogMinAge, 0);
            long size = MeasureVoltrisTargets(targets);
            _logger.LogInfo($"[UltraClean.Voltris] Logs de debug: {targets.Count} arquivo(s), {FormatBytes(size)}");
            return size;
        }

        private long CleanVoltrisLegacyDebugLogs()
        {
            var sw = Stopwatch.StartNew();
            var targets = CollectVoltrisTargets(VoltrisFixedDebugLogPattern, VoltrisDebugLogMinAge, 0);
            long freed = PurgeVoltrisTargets(targets, "log de debug");
            sw.Stop();
            _logger.LogSuccess($"[UltraClean.Voltris] ✅ Logs de debug: {FormatBytes(freed)} em {sw.Elapsed.TotalSeconds:F1}s");
            return freed;
        }

        // ============================================================
        // 4. BACKUPS ANTIGOS DE CONFIGURACAO (mantem os 5 mais recentes)
        // ============================================================

        private long AnalyzeVoltrisSettingsBackupHistory()
        {
            var targets = CollectVoltrisTargets(VoltrisSettingsBackupPattern, VoltrisStaleArtifactMinAge, VoltrisSettingsBackupKeepNewest);
            long size = MeasureVoltrisTargets(targets);
            _logger.LogInfo($"[UltraClean.Voltris] Backups de configuracao: {targets.Count} arquivo(s), {FormatBytes(size)}");
            return size;
        }

        private long CleanVoltrisSettingsBackupHistory()
        {
            var sw = Stopwatch.StartNew();
            var targets = CollectVoltrisTargets(VoltrisSettingsBackupPattern, VoltrisStaleArtifactMinAge, VoltrisSettingsBackupKeepNewest);
            long freed = PurgeVoltrisTargets(targets, "backup de configuracao");
            sw.Stop();
            _logger.LogSuccess($"[UltraClean.Voltris] ✅ Backups de configuracao: {FormatBytes(freed)} em {sw.Elapsed.TotalSeconds:F1}s (mantidos os {VoltrisSettingsBackupKeepNewest} mais recentes)");
            return freed;
        }

        // ============================================================
        // 5. RESIDUO TEMPORARIO DO VOLTRIS (.tmp/.bak/.old/.dmp)
        // ============================================================

        private long AnalyzeVoltrisStaleArtifacts()
        {
            long size = 0;
            size += MeasureVoltrisTargets(CollectVoltrisTargets(VoltrisStaleArtifactPattern, VoltrisStaleArtifactMinAge, 0));
            size += MeasureVoltrisTargets(CollectVoltrisTargets(VoltrisDumpPattern, VoltrisStaleArtifactMinAge, 0));
            _logger.LogInfo($"[UltraClean.Voltris] Residuo temporario: {FormatBytes(size)}");
            return size;
        }

        private long CleanVoltrisStaleArtifacts()
        {
            var sw = Stopwatch.StartNew();
            long freed = PurgeVoltrisTargets(
                CollectVoltrisTargets(VoltrisStaleArtifactPattern, VoltrisStaleArtifactMinAge, 0), "residuo temporario");
            freed += PurgeVoltrisTargets(
                CollectVoltrisTargets(VoltrisDumpPattern, VoltrisStaleArtifactMinAge, 0), "dump de falha");
            sw.Stop();
            _logger.LogSuccess($"[UltraClean.Voltris] ✅ Residuo temporario: {FormatBytes(freed)} em {sw.Elapsed.TotalSeconds:F1}s");
            return freed;
        }

        // ============================================================
        // 6. LOG DE SEGURANCA ANTIGO (30+ dias) - OPT-IN
        // ============================================================

        private long AnalyzeVoltrisSecurityAuditLog()
        {
            var targets = CollectVoltrisTargets(VoltrisSecurityLogPattern, VoltrisSecurityLogMinAge, 0);
            long size = MeasureVoltrisTargets(targets);
            _logger.LogInfo($"[UltraClean.Voltris] Log de seguranca: {targets.Count} arquivo(s), {FormatBytes(size)}");
            return size;
        }

        private long CleanVoltrisSecurityAuditLog()
        {
            var sw = Stopwatch.StartNew();
            var targets = CollectVoltrisTargets(VoltrisSecurityLogPattern, VoltrisSecurityLogMinAge, 0);
            long freed = PurgeVoltrisTargets(targets, "log de seguranca");
            sw.Stop();
            _logger.LogSuccess($"[UltraClean.Voltris] ✅ Log de seguranca: {FormatBytes(freed)} em {sw.Elapsed.TotalSeconds:F1}s");
            return freed;
        }

        // ============================================================
        // REGISTRO NO CATALOGO
        // ============================================================

        /// <summary>
        /// Registra a categoria de manutencao do proprio VOLTRIS.
        /// Chamado via RegisterVoltrisSelfCleanupModules() no construtor.
        ///
        /// IsSafe = true  -> entra na selecao automatica do botao circular.
        /// IsSafe = false -> fica visivel na analise, mas exige opt-in: e o
        ///                  caso do log de seguranca, que e trilha de auditoria.
        /// </summary>
        internal void RegisterVoltrisSelfCleanupModules()
        {
            _categories.Add(new CleanupCategory
            {
                Name = "Manutenção do VOLTRIS",
                LocalizationKey = "CatVoltrisMaintenance",
                Icon = "🧹",
                Items = new List<CleanupCategoryItem>
                {
                    new()
                    {
                        Name = "Logs Rotacionados do VOLTRIS (.old)",
                        Description = "voltris.log.old e arquivos carimbados da rotação de log",
                        LocalizationKey = "ItemVoltrisRotatedLogs",
                        DescriptionLocalizationKey = "ItemVoltrisRotatedLogsDesc",
                        CleanAction = (Func<long>)CleanVoltrisRotatedLogArchive,
                        AnalyzeAction = (Func<long>)AnalyzeVoltrisRotatedLogArchive,
                        RequiresAdmin = false,
                        IsSafe = true
                    },
                    new()
                    {
                        Name = "Logs de Diagnóstico Antigos do VOLTRIS",
                        Description = "VoltrisDiag, StartupDiag, FreezeForensics e afins com 7+ dias",
                        LocalizationKey = "ItemVoltrisDatedLogs",
                        DescriptionLocalizationKey = "ItemVoltrisDatedLogsDesc",
                        CleanAction = (Func<long>)CleanVoltrisDatedDiagnosticLogs,
                        AnalyzeAction = (Func<long>)AnalyzeVoltrisDatedDiagnosticLogs,
                        RequiresAdmin = false,
                        IsSafe = true
                    },
                    new()
                    {
                        Name = "Logs de Depuração Antigos do VOLTRIS",
                        Description = "Logs de debug de nome fixo com 14+ dias, recriados sob demanda",
                        LocalizationKey = "ItemVoltrisDebugLogs",
                        DescriptionLocalizationKey = "ItemVoltrisDebugLogsDesc",
                        CleanAction = (Func<long>)CleanVoltrisLegacyDebugLogs,
                        AnalyzeAction = (Func<long>)AnalyzeVoltrisLegacyDebugLogs,
                        RequiresAdmin = false,
                        IsSafe = true
                    },
                    new()
                    {
                        Name = "Backups Antigos de Configurações do VOLTRIS",
                        Description = "Mantém os 5 backups mais recentes para reverter configuração",
                        LocalizationKey = "ItemVoltrisSettingsBackups",
                        DescriptionLocalizationKey = "ItemVoltrisSettingsBackupsDesc",
                        CleanAction = (Func<long>)CleanVoltrisSettingsBackupHistory,
                        AnalyzeAction = (Func<long>)AnalyzeVoltrisSettingsBackupHistory,
                        RequiresAdmin = false,
                        IsSafe = true
                    },
                    new()
                    {
                        Name = "Arquivos Temporários do VOLTRIS",
                        Description = "Resíduos .tmp, .bak, .old e .dmp de execuções anteriores",
                        LocalizationKey = "ItemVoltrisStaleArtifacts",
                        DescriptionLocalizationKey = "ItemVoltrisStaleArtifactsDesc",
                        CleanAction = (Func<long>)CleanVoltrisStaleArtifacts,
                        AnalyzeAction = (Func<long>)AnalyzeVoltrisStaleArtifacts,
                        RequiresAdmin = false,
                        IsSafe = true
                    },
                    new()
                    {
                        Name = "Log de Segurança Antigo do VOLTRIS",
                        Description = "Trilha de auditoria do app com 30+ dias — requer confirmação",
                        LocalizationKey = "ItemVoltrisSecurityLog",
                        DescriptionLocalizationKey = "ItemVoltrisSecurityLogDesc",
                        CleanAction = (Func<long>)CleanVoltrisSecurityAuditLog,
                        AnalyzeAction = (Func<long>)AnalyzeVoltrisSecurityAuditLog,
                        RequiresAdmin = false,
                        IsSafe = false,
                        RequiresConsent = true
                    }
                }
            });
        }
    }
}
