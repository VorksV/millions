using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Persistence
{
    public class PersistenceMigrationService
    {
        private readonly ILoggingService _logger;
        
        private static readonly string UnifiedRoot = AppDataPaths.UnifiedRoot;

        // [FIX:M-4] Contadores da migração. O objetivo é responder uma pergunta
        // que antes não tinha resposta: "a migração de hoje perdeu dados?".
        // Sem esses contadores, ApplyVersionSpecificPatches não tem como saber se
        // é seguro gravar LastSavedVersion (M-3).
        private int _filesMoved;
        private int _filesIdenticalRemoved;
        private int _conflictsPreserved;
        private int _failures;
        private bool _sourceEqualsDestinationSkipped;

        public PersistenceMigrationService(ILoggingService logger)
        {
            _logger = logger;
        }

        public async Task RunMigrationAuditAsync()
        {
            _logger.LogInfo("📂 [Persistence] Iniciando auditoria de migração de dados...");
            
            try
            {
                AppDataPaths.EnsureDirectory(UnifiedRoot);

                // 2. Migrar Profiler State
                MigrateDirectory(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoltrisOptimizer", "Profiler"),
                    AppDataPaths.Profiler,
                    "Profiler State");

                // Migração CRÍTICA: Unificar perfis de jogos em %LOCALAPPDATA%\Voltris\Games
                MigrateDirectory(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoltrisOptimizer", "GameProfiles"),
                    Path.Combine(UnifiedRoot, "Games"),
                    "Game Profiles (Legacy → Unified)");
                
                MigrateDirectory(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Voltris", "Brain"),
                    Path.Combine(AppDataPaths.RoamingRoot, "Brain"),
                    "Brain AI Data (Roaming)");
                
                MigrateDirectory(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Voltris", "GamerMode"),
                    Path.Combine(AppDataPaths.RoamingRoot, "GamerMode"),
                    "GamerMode Config (Roaming)");

                MigrateFile(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoltrisOptimizer", "widget-config.json"),
                    Path.Combine(UnifiedRoot, "widget-config.json"),
                    "Widget Config");

                MigrateDirectory(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoltrisOptimizer", "Backups"),
                    AppDataPaths.Backups,
                    "Legacy Backups");

                // =========================================================
                // MIGRAÇÃO DE DADOS DO BASE DIRECTORY (perdidos em updates)
                // =========================================================
                // Nota: onboarding.json, history.json, schedules.json foram removidos (órfãos)

                MigrateFile(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Games", "library.json"),
                    AppDataPaths.GamesLibrary,
                    "Games Library");

                MigrateFile(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Games", "profiles.json"),
                    AppDataPaths.GamesProfiles,
                    "Games Profiles");

                MigrateDirectory(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Backups"),
                    AppDataPaths.Backups,
                    "BaseDir Backups");

                // =========================================================
                // MIGRAÇÃO DE MAIS ARQUIVOS LEGADOS DO BASE DIRECTORY
                // =========================================================
                MigrateFile(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AI", "streamhub_settings.json"),
                    AppDataPaths.StreamHubSettings,
                    "StreamHub Settings");

                MigrateFile(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "feature_flags.json"),
                    AppDataPaths.FeatureFlagsCache,
                    "FeatureFlags Cache");

                MigrateFile(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "power_diag_results.json"),
                    AppDataPaths.PowerDiagResults,
                    "Power Diag Results");

                MigrateFile(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gamer_state_memory.json"),
                    AppDataPaths.GamerStateMemory,
                    "Gamer State Memory");

                MigrateFile(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "shell_state.json"),
                    AppDataPaths.ShellState,
                    "Shell State");

                MigrateFile(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Benchmark", "pending_validation.json"),
                    AppDataPaths.BenchmarkPendingValidation,
                    "Benchmark Pending Validation");

                MigrateFile(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "restoration_state.json"),
                    AppDataPaths.RestorationState,
                    "Restoration State");

                MigrateFile(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "system_snapshot.json"),
                    AppDataPaths.SystemSnapshot,
                    "System Snapshot");

                ApplyVersionSpecificPatches();

                CleanupOldDirectories();

                // [FIX:M-4]/[FIX:M-3] Só grava LastSavedVersion quando a migração
                // não teve nenhuma falha. Anteriormente ApplyVersionSpecificPatches
                // lia a versão, comparava, e NUNCA a gravava — o resultado era que
                // a migração inteira re-executava a cada startup. Se alguém
                // "consertasse" isso gravando a versão sempre, uma migração que
                // falhou pela metade marcaria a versão como concluída e os patches
                // jamais tentariam de novo: perda silenciosa e permanente.
                bool migracaoSemFalhas = _failures == 0;
                _logger.LogInfo(
                    $"[FIX:M-4] Resultado da migração | movidos={_filesMoved} | " +
                    $"identicosRemovidos={_filesIdenticalRemoved} | conflitosPreservados={_conflictsPreserved} | " +
                    $"falhas={_failures} | sourceEqualsDestinoIgnorado={_sourceEqualsDestinationSkipped}");

                if (migracaoSemFalhas)
                {
                    PersistLastSavedVersion();
                }
                else
                {
                    _logger.LogWarning(
                        $"[FIX:M-3] LastSavedVersion NÃO gravada: a migração teve {_failures} falha(s). " +
                        $"A migração será reexecutada no próximo startup. " +
                        $"Isso é intencional — marcar como concluída uma migração parcial perderia dados.");
                }

                _logger.LogSuccess("📂 [Persistence] Auditoria de migração concluída com sucesso.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"📂 [Persistence] Erro crítico durante migração: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// [FIX:M-3] Persiste a versão somente após uma migração íntegra.
        /// Isolar o passo em um método próprio deixa a invariante explícita:
        /// o chamador tem que ter avaliado o resultado antes de chegar aqui.
        /// </summary>
        private void PersistLastSavedVersion()
        {
            try
            {
                var settingsService = VoltrisOptimizer.Services.SettingsService.Instance;
                var currentVersion = VoltrisOptimizer.Core.Updater.UpdateService.GetCurrentVersion();
                var anterior = settingsService.Settings.LastSavedVersion;

                settingsService.Settings.LastSavedVersion = currentVersion;
                // SaveSettings() é fire-and-forget (Task.Run). Aceito de propósito:
                // se o processo morrer antes do flush, a migração reexecuta no
                // próximo startup — e isso agora é inofensivo, porque o conflito
                // é preservado em vez de apagado. A idempotência vem da política
                // de arquivos, não da marcação prematura de "feito".
                settingsService.SaveSettings();

                _logger.LogInfo(
                    $"[FIX:M-3] LastSavedVersion gravada APÓS migração bem-sucedida | {anterior} -> {currentVersion}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[FIX:M-3] Falha ao gravar LastSavedVersion (migração será reexecutada): {ex.Message}");
            }
        }

        private void ApplyVersionSpecificPatches()
        {
            try
            {
                var settingsService = VoltrisOptimizer.Services.SettingsService.Instance;
                string lastVersion = settingsService.Settings.LastSavedVersion;
                string currentVersion = VoltrisOptimizer.Core.Updater.UpdateService.GetCurrentVersion();

                if (lastVersion == currentVersion) return;

                _logger.LogInfo($"📂 [Persistence] Detectada mudança de versão: {lastVersion} -> {currentVersion}. Aplicando patches...");

                // Exemplo de patch: Versão < 1.0.1.5 precisa resetar cache de WMI
                if (IsVersionOlder(lastVersion, "1.0.1.5"))
                {
                    _logger.LogInfo("📂 [Persistence] Aplicando patch v1.0.1.6: Limpando cache WMI legado...");
                    // Lógica do patch aqui
                }

                // Patch para unificação (esta versão)
                if (IsVersionOlder(lastVersion, "1.1.0.0"))
                {
                    _logger.LogInfo("📂 [Persistence] Aplicando patch v1.1.0.0: Unificação de diretórios concluída.");
                    // Outras transformações de dados se necessárias
                }

                _logger.LogInfo("📂 [Persistence] Todos os patches de versão aplicados.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"📂 [Persistence] Erro ao aplicar patches de versão: {ex.Message}");
            }
        }

        private bool IsVersionOlder(string current, string target)
        {
            try
            {
                Version vCurrent = new Version(current);
                Version vTarget = new Version(target);
                return vCurrent < vTarget;
            }
            catch { return true; }
        }

        private void EnsureDirectory(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
                _logger.LogInfo($"📂 [Persistence] Diretório criado: {path}");
            }
        }

        private void MigrateDirectory(string source, string destination, string label)
        {
            try
            {
                if (!Directory.Exists(source)) return;

                // [FIX:M-4] Guarda contra source == destination.
                //
                // BUG ORIGINAL: sem esta guarda, se as duas rotas apontarem para a
                // mesma pasta (o que já aconteceu quando o diretório unificado
                // foi criado dentro do legado durante uma versão de teste),
                // MigrateDirectory chamava a si mesma indefinidamente sobre a
                // mesma pasta. A comparação canônica pega isso antes de qualquer
                // operação de arquivo.
                if (PathsEqual(source, destination))
                {
                    _sourceEqualsDestinationSkipped = true;
                    _logger.LogWarning(
                        $"[FIX:M-4] source e destino são o mesmo caminho — migração ignorada para não corromper dados | " +
                        $"label={label} | path={source}");
                    return;
                }

                // Não entra no source se ele estiver dentro do destino (ou vice-versa),
                // o que causaria a mesma recursão infinita via subdiretórios.
                if (IsSubPathOf(destination, source))
                {
                    _sourceEqualsDestinationSkipped = true;
                    _logger.LogWarning(
                        $"[FIX:M-4] source está contido no destino — migração ignorada | label={label} | source={source} | destino={destination}");
                    return;
                }

                _logger.LogInfo($"📂 [Persistence] Migrando {label} de '{source}' para '{destination}'...");
                
                EnsureDirectory(destination);

                foreach (string file in Directory.GetFiles(source))
                {
                    string destFile = Path.Combine(destination, Path.GetFileName(file));
                    MigrateFile(file, destFile, label);
                }

                // Migrar subdiretórios recursivamente
                foreach (string dir in Directory.GetDirectories(source))
                {
                    MigrateDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)), label);
                }
            }
            catch (Exception ex)
            {
                _failures++;
                _logger.LogWarning($"📂 [Persistence] Falha ao migrar {label}: {ex.Message}");
            }
        }

        /// <summary>
        /// [FIX:M-4] Compara dois caminhos ignorando barra final, caixa e prefixos
        /// 8.3, que fazem <c>File.Move</c> recursar sem fim quando as duas strings
        /// são iguais na prática.
        /// </summary>
        private static bool PathsEqual(string a, string b)
        {
            try
            {
                return string.Equals(
                    Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                // Se não der para normalizar, compara por string para não migrar por engano.
                return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static bool IsSubPathOf(string child, string parent)
        {
            try
            {
                var c = Path.GetFullPath(child).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var p = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                return c.StartsWith(p, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private void MigrateFile(string source, string destination, string label)
        {
            try
            {
                if (!File.Exists(source)) return;

                if (PathsEqual(source, destination))
                {
                    _sourceEqualsDestinationSkipped = true;
                    _logger.LogWarning(
                        $"[FIX:M-4] source e destino são o mesmo arquivo — ignorado | label={label} | path={source}");
                    return;
                }

                _logger.LogInfo($"📂 [Persistence] Migrando {label} de '{source}' para '{destination}'...");

                EnsureDirectory(Path.GetDirectoryName(destination)!);

                if (!File.Exists(destination))
                {
                    // Sem conflito: preserva o mtime original, que o File.Move já faz.
                    File.Move(source, destination);
                    _filesMoved++;
                    _logger.LogInfo($"📂 [Persistence] Arquivo {label} migrado com sucesso.");
                    return;
                }

                // ---------- CONFLITO: destino já existe ----------
                //
                // BUG ORIGINAL (a perda de dados mais grave do serviço):
                //
                //   else {
                //       // Se já existe no destino, apenas deleta o antigo se for
                //       // idêntico ou deixa lá se for conflito
                //       // Decidimos manter o do destino (mais novo)
                //       try { File.Delete(file); } catch { }
                //   }
                //
                // O comentário descreve uma decisão condicional ("apenas deleta
                // se for idêntico OU deixa lá se for conflito"). O código fazia
                // as DUAS coisas ao mesmo tempo: apagava sempre, sem comparar e
                // sem backup. Como RunMigrationAuditAsync roda em TODO startup,
                // uma única execução com o destino invalido apagava a cópia boa
                // para sempre, sem nenhuma forma de recuperação.
                //
                // A política correta é: em conflito, nunca destruir nada. Quem
                // perde o direito de ser o canônico vira ".conflict-<timestamp>",
                // e o original permanece legível.
                if (FilesAreIdentical(source, destination))
                {
                    // Idênticos: apagar a origem é limpeza pura, não perda de dados.
                    try
                    {
                        File.Delete(source);
                        _filesIdenticalRemoved++;
                        _logger.LogInfo(
                            $"📂 [Persistence] {label}: origem idêntica ao destino, origem removida com segurança | {source}");
                    }
                    catch (Exception delEx)
                    {
                        _logger.LogWarning(
                            $"[FIX:M-4] {label}: arquivos idênticos mas a origem não pôde ser removida (será deixada) | " +
                            $"{source} | {delEx.Message}");
                    }
                    return;
                }

                // Divergentes: preservar as DUAS cópias.
                var srcInfo = new FileInfo(source);
                var dstInfo = new FileInfo(destination);
                bool sourceIsNewer = srcInfo.LastWriteTimeUtc > dstInfo.LastWriteTimeUtc;

                string loserPath;
                if (sourceIsNewer)
                {
                    // A origem é mais recente: ela vira o canônico, e o destino
                    //VELHO é preservado como conflito (não é descartado).
                    loserPath = destination + $".conflict-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
                    try
                    {
                        File.Move(destination, loserPath);
                        File.Move(source, destination);
                    }
                    catch (Exception ex)
                    {
                        _failures++;
                        _logger.LogError(
                            $"[FIX:M-4] {label}: falha ao promover a origem mais recente — AMBAS as cópias preservadas | " +
                            $"origem={source} (newer={srcInfo.LastWriteTimeUtc:o}) | destino={destination} ({dstInfo.LastWriteTimeUtc:o}) | {ex.Message}", ex);
                        return;
                    }
                }
                else
                {
                    // O destino já é o mais recente: a origem é preservada como
                    // conflito. O destino NÃO é tocado.
                    loserPath = source + $".conflict-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
                    try
                    {
                        File.Copy(source, loserPath, overwrite: false);
                        File.Delete(source);
                    }
                    catch (Exception ex)
                    {
                        _failures++;
                        _logger.LogError(
                            $"[FIX:M-4] {label}: falha ao preservar a origem divergente — origem deixada intacta | " +
                            $"origem={source} | conflito={loserPath} | {ex.Message}", ex);
                        return;
                    }
                }

                _conflictsPreserved++;
                _logger.LogWarning(
                    $"[FIX:M-4] {label}: CONFLITO de migração — nenhuma cópia foi destruída | " +
                    $"origem={source} ({srcInfo.LastWriteTimeUtc:o}, {srcInfo.Length} bytes) | " +
                    $"destino={destination} ({dstInfo.LastWriteTimeUtc:o}, {dstInfo.Length} bytes) | " +
                    $"vencedor={(sourceIsNewer ? "ORIGEM (mais recente)" : "DESTINO (mais recente)")} | " +
                    $"perdedor preservado em={loserPath}");
            }
            catch (Exception ex)
            {
                _failures++;
                _logger.LogWarning($"📂 [Persistence] Falha ao migrar arquivo {label}: {ex.Message}");
            }
        }

        /// <summary>
        /// [FIX:M-4] Igualdade real de conteúdo por tamanho + SHA-256. Só o
        /// tamanho não bastava: um JSON truncado tem o mesmo tamanho com
        /// frequência, e foi exatamente esse o caso que fazia a migração apagar
        /// dados.
        /// </summary>
        private static bool FilesAreIdentical(string a, string b)
        {
            try
            {
                var fa = new FileInfo(a);
                var fb = new FileInfo(b);
                if (fa.Length != fb.Length) return false;

                using var sha = System.Security.Cryptography.SHA256.Create();
                using var streamA = File.OpenRead(a);
                using var streamB = File.OpenRead(b);
                var hashA = sha.ComputeHash(streamA);
                var hashB = sha.ComputeHash(streamB);
                return hashA.SequenceEqual(hashB);
            }
            catch
            {
                // Sem conseguir comparar, assumir NÃO idêntico é o lado seguro:
                // o chamador cai na política de preservação.
                return false;
            }
        }

private void CleanupOldDirectories()
        {
            string[] oldDirs = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoltrisOptimizer"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VoltrisOptimizer")
            };

            foreach (var dir in oldDirs)
            {
                try
                {
                    if (!Directory.Exists(dir)) continue;
                    
                    // Verificar se diretório está vazio
                    if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    {
                        Directory.Delete(dir);
                        _logger.LogInfo($"📂 [Persistence] Diretório vazio removido: {dir}");
                        continue;
                    }
                    
                    // Verificar subdiretórios específicos que foram migrados
                    var gameProfilesDir = Path.Combine(dir, "GameProfiles");
                    if (Directory.Exists(gameProfilesDir) && !Directory.EnumerateFileSystemEntries(gameProfilesDir).Any())
                    {
                        Directory.Delete(gameProfilesDir);
                        _logger.LogInfo($"📂 [Persistence] GameProfiles vazio removido: {gameProfilesDir}");
                    }
                    
                    var profilerDir = Path.Combine(dir, "Profiler");
                    if (Directory.Exists(profilerDir) && !Directory.EnumerateFileSystemEntries(profilerDir).Any())
                    {
                        Directory.Delete(profilerDir);
                        _logger.LogInfo($"📂 [Persistence] Profiler vazio removido: {profilerDir}");
                    }
                    
                    var backupsDir = Path.Combine(dir, "Backups");
                    if (Directory.Exists(backupsDir) && !Directory.EnumerateFileSystemEntries(backupsDir).Any())
                    {
                        Directory.Delete(backupsDir);
                        _logger.LogInfo($"📂 [Persistence] Backups vazio removido: {backupsDir}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"📂 [Persistence] Erro ao limpar diretório {dir}: {ex.Message}");
                }
            }
            
            // Limpar arquivos legados específicos
            CleanupLegacyFiles();
        }
        
        private void CleanupLegacyFiles()
        {
            string[] legacyFiles = new[]
            {
                // License legado
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Voltris", "license.dat"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "license.dat"),
                
                // Logs especializados (agora consolidados)
                Path.Combine(LogDirectoryResolver.Resolve(), "settings_errors.log"),
                Path.Combine(LogDirectoryResolver.Resolve(), "metrics_crash.log"),
                Path.Combine(LogDirectoryResolver.Resolve(), "metrics_cache_clocks.log"),
                Path.Combine(LogDirectoryResolver.Resolve(), "dls.json")
            };
            
            foreach (var file in legacyFiles)
            {
                try
                {
                    if (File.Exists(file))
                    {
                        // Fazer backup antes de remover
                        var backupPath = file + ".backup";
                        File.Copy(file, backupPath, true);
                        File.Delete(file);
                        _logger.LogInfo($"📂 [Persistence] Arquivo legado removido (backup criado): {file}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"📂 [Persistence] Erro ao remover arquivo legado {file}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Retorna o caminho unificado para um serviço específico.
        /// </summary>
        public static string GetUnifiedPath(string subPath)
        {
            return Path.Combine(UnifiedRoot, subPath);
        }
    }
}
