using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VoltrisOptimizer.Services.UltraClean
{
    /// <summary>
    /// Portao unico de seguranca do VOLTRIS UltraClean.
    ///
    /// Objetivo: nenhum item de limpeza pode apagar arquivo do usuario, dado de
    /// aplicativo/jogo em uso, ou conteudo de recuperacao do Windows, mesmo que
    /// o catalogo o tenha marcado como "IsSafe = true".
    ///
    /// Este guard e obrigatorio: deve ser consultado em TODOS os primitivos de
    /// delete do servico (DeleteDirectorySafe, DeleteFilesInDirectory,
    /// SafeDeleteFiles) antes de qualquer operacao no disco.
    ///
    /// Regra: na duvida, BLOQUEIA. Liberar so com prova explicita.
    /// </summary>
    public static class CleanupSafetyGuard
    {
        /// <summary>Notificado quando uma operacao e bloqueada. Usado para auditoria/log.</summary>
        public static Action<string>? OnBlocked;

        private static long _blockedCount;
        private static long _lockedSkippedCount;

        /// <summary>Total de operacoes bloqueadas pela blacklist de caminhos.</summary>
        public static long BlockedCount => InterlockedRead(ref _blockedCount);

        /// <summary>Total de arquivos pulados por estarem em uso (lock real).</summary>
        public static long LockedSkippedCount => InterlockedRead(ref _lockedSkippedCount);

        private static long InterlockedRead(ref long value) => System.Threading.Interlocked.Read(ref value);

        private static void Block(string path, string reason)
        {
            System.Threading.Interlocked.Increment(ref _blockedCount);
            OnBlocked?.Invoke($"[CleanupSafetyGuard] BLOQUEADO: {path} -> {reason}");
        }

        private static void SkipLocked(string path)
        {
            System.Threading.Interlocked.Increment(ref _lockedSkippedCount);
        }

        /// <summary>Normaliza um caminho para comparacao estavel (barras minusculas, sem barra final).</summary>
        public static string Normalize(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            string p = path.Trim().Replace('/', '\\');
            while (p.Length > 3 && p.EndsWith("\\", StringComparison.Ordinal)) p = p.Substring(0, p.Length - 1);
            return p.ToLowerInvariant();
        }

        /// <summary>True se <paramref name="path"/> esta dentro de (ou e igual a) <paramref name="root"/>.</summary>
        public static bool IsUnder(string path, string root)
        {
            string p = Normalize(path);
            string r = Normalize(root);
            if (r.Length == 0) return false;
            if (p == r) return true;
            return p.StartsWith(r + "\\", StringComparison.Ordinal);
        }

        private static bool IsUnderAny(string path, IEnumerable<string> roots)
        {
            foreach (var r in roots)
            {
                if (r.Length > 0 && IsUnder(path, r)) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------
        // Blacklist de dados do usuario / jogos / estado de aplicacao
        // ------------------------------------------------------------------

        /// <summary>
        /// Pastas pessoais de QUALQUER perfil de usuario.
        ///
        /// Regra ESTRUTURAL, não por enumeração:HKU Documents só existe se o
        /// diretório existir no disco. Um perfil que ainda não foi criado, ou que
        /// fica em outro volume, passaria direto — que é exatamente o bug que
        /// fazia o Outlook tratar todo .ost alheio como "órfão". Aqui a regra é
        /// "qualquer pasta <drive>\Users\<qualquer>\Documents", exista ou não.
        /// </summary>
        private static readonly string[] PersonalFolderNames =
        {
            "documents", "desktop", "downloads", "pictures", "videos",
            "music", "onedrive", "saved games", "favorites", "links", "contacts"
        };

        /// <summary>
        /// Detecta <drive>\Users\<perfil>\... e <drive>\Users\<perfil>.
        /// Retorna o nome do perfil se o caminho estiver dentro de alguma pasta
        /// pessoal de qualquer usuário; null caso contrário.
        /// </summary>
        private static string? GetPersonalFolderProfile(string normalizedPath)
        {
            foreach (var drive in EnumerateFixedDrives())
            {
                string usersPrefix = Normalize(drive) + "users\\";
                if (!normalizedPath.StartsWith(usersPrefix, StringComparison.Ordinal)) continue;

                string remainder = normalizedPath.Substring(usersPrefix.Length);
                var parts = remainder.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;

                string profile = parts[0];
                // <drive>\Users\<perfil>  -> a raiz do perfil em si
                if (parts.Length == 1) return profile;

                // <drive>\Users\<perfil>\<pasta pessoal>
                if (PersonalFolderNames.Contains(parts[1], StringComparer.OrdinalIgnoreCase))
                    return profile;
            }
            return null;
        }

        /// <summary>Raiz de todos os perfis de usuario existentes na maquina.</summary>
        public static IEnumerable<string> EnumerateUserProfileRoots()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var drive in EnumerateFixedDrives())
            {
                string users = Path.Combine(drive, "Users");
                if (!Directory.Exists(users)) continue;
                string[] dirs;
                try { dirs = Directory.GetDirectories(users); }
                catch { continue; }
                foreach (var d in dirs)
                {
                    if (seen.Add(d)) yield return d;
                }
            }
        }

        private static IEnumerable<string> EnumerateFixedDrives()
        {
            var result = new List<string>();
            try
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    if (d.DriveType != DriveType.Fixed) continue;
                    if (!d.IsReady) continue;
                    result.Add(d.RootDirectory.FullName);
                }
            }
            catch { }
            if (result.Count == 0) result.Add(@"C:\");
            return result;
        }

        /// <summary>
        /// Nomes de pastas de dados de jogos/launchers dentro de AppData, de
        /// qualquer perfil e qualquer volume. Regra estrutural (independe de o
        /// perfil existir). Cache de shader/shadercache fica de fora: e
        /// regeneravel e limpado de proposito.
        /// </summary>
        private static readonly string[] GameDataFolderNames =
        {
            "unrealengine", "unity", "epicgameslauncher", "roblox", "ubisoft game launcher",
            "battle.net", "blizzard", "origin", "itch", "gog galaxy", "minecraft",
            "prince of persia", "rockstar games", "beware", "miHoYo", "hoyoverse", "riot games"
        };

        /// <summary>
        /// Detecta <perfil>\AppData\<Local|Roaming>\<pasta de jogo> para qualquer
        /// perfil em qualquer volume.
        /// </summary>
        private static bool IsUnderGameDataFolder(string normalizedPath)
        {
            foreach (var drive in EnumerateFixedDrives())
            {
                string prefix = Normalize(drive) + "users\\";
                if (!normalizedPath.StartsWith(prefix, StringComparison.Ordinal)) continue;

                var segs = normalizedPath.Substring(prefix.Length)
                                .Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
                if (segs.Length < 4) continue;
                if (!segs[1].Equals("appdata", StringComparison.OrdinalIgnoreCase)) continue;
                if (!segs[2].Equals("local", StringComparison.OrdinalIgnoreCase) &&
                    !segs[2].Equals("roaming", StringComparison.OrdinalIgnoreCase)) continue;

                if (GameDataFolderNames.Contains(segs[3], StringComparer.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Raizes de dados de jogos e launchers. Cache de shader/shadercache e
        /// tolerado; o resto (common, userdata, saves) nunca e automatico.
        /// </summary>
        private static IEnumerable<string> GameDataRoots()
        {
            foreach (var profile in EnumerateUserProfileRoots())
            {
                string local = Path.Combine(profile, "AppData", "Local");
                string roaming = Path.Combine(profile, "AppData", "Roaming");

                foreach (var lib in SteamLibraries())
                {
                    yield return Path.Combine(lib, "steamapps", "common");
                    yield return Path.Combine(lib, "steamapps", "userdata");
                    yield return Path.Combine(lib, "steamapps", "downloading");
                    yield return Path.Combine(lib, "steamapps", "workshop");
                    yield return Path.Combine(lib, "steamapps", "shadercache");
                }

                yield return Path.Combine(local, "EpicGamesLauncher", "Saved");
                yield return Path.Combine(roaming, "Origin");
                yield return Path.Combine(local, "Unity");
                yield return Path.Combine(local, "UnrealEngine");
                yield return Path.Combine(local, "Roblox");
                yield return Path.Combine(roaming, "Roblox");
                yield return Path.Combine(local, "Ubisoft Game Launcher");
                yield return Path.Combine(roaming, "Ubisoft Game Launcher");
                yield return Path.Combine(local, "Battle.net");
                yield return Path.Combine(roaming, "Battle.net");
                yield return Path.Combine(local, "Blizzard");
                yield return Path.Combine(roaming, "Blizzard");
                yield return Path.Combine(local, "Packages", "Microsoft.Roblox");
            }
        }

        /// <summary>Bibliotecas Steam known: VDF padrao + detectadas por registro.</summary>
        private static IEnumerable<string> SteamLibraries()
        {
            var result = new List<string> { @"C:\Program Files (x86)\Steam" };
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
                var path = key?.GetValue("InstallPath")?.ToString();
                if (!string.IsNullOrWhiteSpace(path)) result.Add(path);
            }
            catch { }
            return result;
        }

        /// <summary>
        /// Diretorios de recuperacao / upgrade / diagnostico do Windows.
        /// Apagar estos quebra recuperacao, upgrade ou reset pendente.
        /// </summary>
        private static IEnumerable<string> SystemCriticalRoots()
        {
            foreach (var drive in EnumerateFixedDrives())
            {
                yield return Path.Combine(drive, "$Recycle.Bin");
                yield return Path.Combine(drive, "$WinREAgent");
                yield return Path.Combine(drive, "$SysReset");
                yield return Path.Combine(drive, "Recovery");
                yield return Path.Combine(drive, "System Volume Information");
                yield return Path.Combine(drive, "PerfLogs");
                yield return Path.Combine(drive, "Config.Msi");
                yield return Path.Combine(drive, "$GetCurrent");
            }
            yield return @"C:\Windows\System32\winevt\Logs";
            yield return @"C:\Windows\System32\LogFiles";
            yield return @"C:\Windows\System32\config";
            yield return @"C:\Windows\WinSxS\Temp";
            yield return @"C:\Windows\Installer";
        }

        // ------------------------------------------------------------------
        // API principal
        // ------------------------------------------------------------------

        /// <summary>
        /// Consulta OBRIGATORIA antes de apagar qualquer arquivo ou diretorio.
        /// Retorna true quando a operacao pode seguir.
        /// </summary>
        public static bool CanDelete(string? path, string? pattern = null)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                Block("(vazio)", "caminho vazio");
                return false;
            }

            string full;
            try { full = Path.GetFullPath(path); }
            catch { Block(path, "caminho invalido"); return false; }

            string reason = DescribeProtection(full, pattern);
            if (reason.Length > 0)
            {
                Block(full, reason);
                return false;
            }
            return true;
        }

        /// <summary>Descreve porque o caminho esta protegido. Retorna string vazia se permitido.</summary>
        public static string DescribeProtection(string? path, string? pattern = null)
        {
            if (string.IsNullOrWhiteSpace(path)) return "caminho vazio";

            string full;
            try { full = Path.GetFullPath(path); }
            catch { return "caminho invalido"; }

            string p = Normalize(full);
            string fileName = Normalize(Path.GetFileName(full));

            // 1) Raiz de volume, raiz de perfil, raiz do Windows: nunca.
            if (p.Length <= 3) return "raiz de volume";
            if (p == Normalize(@"C:\windows") || p == Normalize(@"C:\users") || p == Normalize(@"C:\programdata"))
                return "raiz de sistema";
            if (p == Normalize(@"C:\program files") || p == Normalize(@"C:\program files (x86)"))
                return "raiz de programas";

            // 2) Pastas pessoais e raiz de perfil de QUALQUER usuario, em qualquer
            //    volume. Regra estrutural: nao depende de o perfil existir no disco.
            string? personalProfile = GetPersonalFolderProfile(p);
            if (personalProfile != null)
            {
                string remainderTail = p.Substring(p.IndexOf("users\\", StringComparison.Ordinal) + 6);
                var segs = remainderTail.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
                if (segs.Length == 1) return $"raiz de perfil de usuario ({personalProfile})";
                return $"pasta pessoal do usuario ({personalProfile}\\{segs[1]})";
            }

            // 3) Dados de jogos e saves (regra estrutural: qualquer perfil/volume).
            if (IsUnderAny(full, GameDataRoots()) || IsUnderGameDataFolder(p))
                return "dados de jogo/launcher (common, userdata, saves)";

            // 4) Estado de aplicacao (UWP LocalState guarda saves/preferencias).
            if (p.Contains(@"\appdata\local\packages\", StringComparison.Ordinal) &&
                (p.EndsWith(@"\localstate", StringComparison.Ordinal) ||
                 p.Contains(@"\localstate\", StringComparison.Ordinal)))
                return "LocalState de app Store (estado persistido)";

            // 5) Recuperacao / upgrade / diagnostico do Windows.
            if (IsUnderAny(full, SystemCriticalRoots())) return "diretorio critico de recuperacao/upgrade do Windows";

            // 6) FOUND.000 = arquivos recuperados pelo chkdsk (unica copia).
            if (fileName.StartsWith("found.", StringComparison.Ordinal)) return "FOUND.* (arquivos recuperados pelo chkdsk)";

            // 7) Filas de upgrade/reset na raiz do drive.
            if (fileName.StartsWith("$winreagent", StringComparison.Ordinal) ||
                fileName.StartsWith("$sysreset", StringComparison.Ordinal))
                return "fila de upgrade/reset do Windows";

            // 8) Extensoes perigosas em locais perigosos.
            if ((fileName.EndsWith(".ost", StringComparison.Ordinal) || fileName.EndsWith(".pst", StringComparison.Ordinal)) &&
                p.Contains(@"\microsoft\outlook\", StringComparison.Ordinal))
                return "caixa de correio local do Outlook (.ost/.pst)";

            if (IsUnder(full, @"C:\Windows\Installer") &&
                (fileName.EndsWith(".msi", StringComparison.Ordinal) || fileName.EndsWith(".msp", StringComparison.Ordinal)))
                return "instalador do Windows Installer (remocao quebra desinstalar/reparar)";

            if (fileName.EndsWith(".ost", StringComparison.Ordinal) && p.Contains(@"\outlook\", StringComparison.Ordinal))
                return "caixa de correio do Outlook";

            // 9) Unreal: Saved = saves de projetos.
            if (p.Contains(@"\unrealengine\", StringComparison.Ordinal) &&
                (p.EndsWith(@"\saved", StringComparison.Ordinal) || p.Contains(@"\saved\", StringComparison.Ordinal)))
                return "pasta Saved de projeto Unreal (saves)";

            // 10) Filhos diretos de Program Files: protege contra o bug que
            //     apaga Path.GetDirectoryName(installPath) e desinstala programas.
            string parent = Normalize(Path.GetDirectoryName(full));
            if (parent == Normalize(@"C:\Program Files") || parent == Normalize(@"C:\Program Files (x86)"))
                return "filho direto de Program Files (risco de remover app instalado)";

            // 11) Reparse points (junction/symlink) nunca sao seguidos nem apagados.
            try
            {
                if (Directory.Exists(full) || File.Exists(full))
                {
                    var attr = File.GetAttributes(full);
                    if ((attr & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                        return "reparse point (junction/symlink)";
                }
            }
            catch { return "nao foi possivel inspecionar atributos"; }

            // 12) Padrao explicitamente perigoso informado pelo chamador.
            if (!string.IsNullOrEmpty(pattern) && IsDangerousPattern(pattern))
                return $"padrao perigoso informado pelo modulo: {pattern}";

            return string.Empty;
        }

        private static readonly string[] DangerousPatterns =
        {
            "*.ost", "*.pst", "*.msi", "*.msp", "*.pstx", "*.ostx",
            "*.sav", "*.save", "*.dat", "*.db-journal", "*.vhd", "*.vhdx"
        };

        private static bool IsDangerousPattern(string pattern)
        {
            foreach (var d in DangerousPatterns)
            {
                if (string.Equals(d, pattern, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>
        /// Teste REAL de arquivo em uso. Mucho mais forte que a heuristica de
        /// "arquivo modificado nos ultimos 5 minutos".
        /// </summary>
        public static bool IsFileLocked(FileInfo file)
        {
            try
            {
                using (file.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    return false;
                }
            }
            catch (IOException) { SkipLocked(file.FullName); return true; }
            catch (UnauthorizedAccessException) { return true; }
        }

        /// <summary>
        /// Verificacao completa de um arquivo antes de apagar: protegido? em uso?
        /// </summary>
        public static bool CanDeleteFile(FileInfo file)
        {
            if (!CanDelete(file.FullName)) return false;
            if (IsFileLocked(file)) return false;
            return true;
        }

        /// <summary>Zera contadores (usado em testes/diagnostico).</summary>
        public static void ResetCounters()
        {
            System.Threading.Interlocked.Exchange(ref _blockedCount, 0);
            System.Threading.Interlocked.Exchange(ref _lockedSkippedCount, 0);
        }

        // ------------------------------------------------------------------
        // Rebaixamento central de itens perigosos
        // ------------------------------------------------------------------

        /// <summary>
        /// Ações NUNCA podem rodar no botão circular. Precisam de opt-in
        /// explícito do usuário, mesmo que o catálogo as tenha marcado
        /// como IsSafe = true.
        ///
        /// Estes nomes foram extraídos do catálogo real (135 ações). A
        /// correspondência é por substring, então prefixos como "CleanSteam"
        /// cobrem CleanSteamCache e variantes.
        ///
        /// Grupos:
        ///  - Recuperação/boot : apagam o caminho de volta do Windows
        ///  - Log de segurança : apaga trilha de auditoria
        ///  - Fontes de inst.  : quebram "Desinstalar"/"Reparar" de apps
        ///  - Pastas irmãs     : desinstalam programas por comparação de versão
        ///  - Dados de jogo    : apagam saves e estado de launcher
        ///  - Sessão/cookies   : derrubam o usuário logado
        ///  - Registro         : apagam chaves de apps instalados
        ///  - Driver em uso    : quebram instalação de driver
        ///  - Lixeira alheia   : irreversível, inclui outros usuários
        /// </summary>
        private static readonly string[] OptInOnlyActions =
        {
            // Recuperação / boot / rollback
            "CleanOldRestorePoints", "CleanRestorePoint", "CleanShadowCopy",
            "CleanWindowsBackup", "CleanLastKnownGood", "CleanBootBackupInfo",
            "CleanServicePackBackup", "CleanHardLinkBackup", "CleanWindowsOld",

            // Log de segurança e auditoria
            "CleanEventLogs", "CleanAdvancedSystemLogs", "CleanSystemLogs",
            "CleanSystem32LogFiles", "CleanWindowsLogsEnhanced", "CleanSecurityLog",

            // Fontes de instalação (quebram Desinstalar/Reparar/Atualizar)
            "CleanOrphanWindowsInstaller", "CleanInstallerBaselineCache",
            "CleanVSPackageCache", "CleanPackageCache", "CleanInstallerCache",
            "CleanOfficeMSOCache", "CleanOfficeClickToRunCache",
            "CleanRealtekAudioCache", "CleanJavaInstallCache",
            "CleanSQLServerUpdateCache", "CleanRetailDemoContent",
            "CleanWindowsUpdateHistory", "CleanAdobeAcrobatCache",

            // Fila de upgrade/reset do Windows (o guard bloqueia a pasta toda,
            // então este item também exige opt-in para não prometer espaço falso)
            "CleanSysResetLogs",

            // Pastas irmãs de apps instalados (o bug do Path.GetDirectoryName)
            "CleanChromeOldBackup", "CleanOperaOldBackup", "CleanPPLiveOldBackup",
            "CleanWPSOldBackup", "CleanKuGouMusicOldBackup",
            "CleanAlibabaWangwangOldBackup", "CleanAlibabaQintaoOldBackup",
            "Clean360BrowserOldBackup", "Clean2345PinyinOldBackup",
            "CleanMicrosoftPinyinInstall",

            // Dados de jogo, saves e estado de launcher
            "CleanUnrealCache", "CleanUnityCache", "CleanEpicCache",
            "CleanSteamCache", "CleanOriginCache", "CleanSpotifyCache",
            "CleanZoomCache", "CleanCorelVideoStudioProxy",
            "CleanQQTempData", "CleanTencentDownloadDir", "CleaniTunesBackup",
            "CleanAviraTempFiles", "CleanDefenderScanHistory", "CleanDefenderHistory",

            // UWP / Store (AppContainer em uso)
            "CleanStoreCache", "CleanUWPCache", "CleanUwpTempState", "CleanCorruptedAppx",

            // Sessão, cookies e listas salvas
            "CleanTerminalServerClientCache", "CleanWinINetCookies",
            "CleanINetCacheLegacy", "CleanMRULists", "CleanWebPICache",

            // Registro de apps instalados
            "CleanOrphanUninstallKeys", "CleanOrphanFileExtensions",

            // Pastas de driver (em uso durante instalação)
            "CleanDriverTempDirectories", "CleanAmdExtractedDrivers",
            "CleanIntelExtractedDrivers", "CleanNvidiaExtractedDrivers",
            "CleanOemDriversFolder", "CleanOldDrivers", "CleanDriverTemp",
            "CleanIntelDriverCache", "CleanNVIDIADriverCache", "CleanNVIDIADownloaderCache",

            // Antivírus em uso pelo MsMpEng e downloads em andamento
            "CleanMicrosoftAntivirusUselessFiles", "CleanAviraQuarantine",
            "CleanIDMTempFiles",

            // Lixeira (inclui a de todos os usuários, irreversível)
            "CleanRecycleBin", "EmptyRecycleBin",

            // Estado do shell do usuário
            "CleanShellBags", "CleanWindowPositions", "CleanMenuOrderCache",
            "CleanTrayNotifyCache", "CleanRecentFiles", "CleanJumpListCache",

            // Cache de apps de mensagem (derruba sessão / corrompe app aberto)
            "CleanTeamsCache", "CleanDiscordCache", "CleanSlackCache",
            "CleanWhatsAppCache", "CleanTelegramCache", "CleanBaiduNetdiskLogs",
            "CleanFetionLogs", "CleanYYTempFiles",

            // Caixa de correio do Outlook
            "CleanOutlookOrphanOst"
        };

        /// <summary>
        /// Aplica a regra de rebaixamento em um item do catálogo.
        /// Retorna true se o item foi rebaixado.
        /// </summary>
        public static bool DemoteIfOptInOnly(Delegate? cleanAction, out string reason)
        {
            reason = string.Empty;
            var name = cleanAction?.Method?.Name;
            if (string.IsNullOrEmpty(name)) return false;

            foreach (var denied in OptInOnlyActions)
            {
                if (name.IndexOf(denied, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (name.Equals(denied, StringComparison.OrdinalIgnoreCase))
                {
                    reason = denied;
                    return true;
                }
                // Correspondência por prefixo: CleanWindowsOldX -> CleanWindowsOld
                if (name.StartsWith(denied, StringComparison.OrdinalIgnoreCase))
                {
                    reason = denied;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Nomes de ação que checam substring em vez de prefixo exato.
        /// Usado para variantes com sufixo no meio do nome.
        /// </summary>
        public static bool IsOptInOnlyAction(Delegate? cleanAction)
        {
            var name = cleanAction?.Method?.Name;
            if (string.IsNullOrEmpty(name)) return false;
            foreach (var denied in OptInOnlyActions)
            {
                if (name.IndexOf(denied, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }
    }
}
