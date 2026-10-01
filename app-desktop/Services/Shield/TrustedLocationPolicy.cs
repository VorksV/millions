using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Shield
{
    /// <summary>
    /// Politica de locais e publicadores confiáveis.
    ///
    /// REGRA DE OURO DE UM ANTIVIRUS: reconhecer CATEGORIAS de diretorio, nunca nomes
    /// de arquivo um a um.
    ///
    /// O historico deste codigo e a prova do contrario. A lista de exclusao grew de
    /// 22 nomes de DLL do DISM (FileMonitorService.cs) porque o unico jeito de
    /// reconhecer "DLL legitima do Windows extraida em %TEMP%" era perguntar pelo
    /// NOME do arquivo. Cada instalador novo trazia um nome novo, e cada FP trazia
    /// um remendo. Hoje a mesma situacao e resolvida por ESTRUTURA
    /// (PathNormalizer.IsInstallerStagingLayout) e por CATEGORIA (esta classe), sem
    /// citar o nome de um unico binario.
    ///
    /// Uma categoria errada aqui custa falso NEGATIVO ( malware pasa ), por isso
    /// todo diretorio listado e justificado por conteudo previsivel, e nenhum
    /// diretorio de usuario onde o usuario baixa coisas entra na lista.
    /// </summary>
    public sealed class TrustedLocationPolicy
    {
        private readonly ILoggingService _logger;
        private readonly List<string> _trustedRoots = new();
        private readonly HashSet<string> _trustedPrefixes = new(StringComparer.OrdinalIgnoreCase);
        private string? _voltrisInstallRoot;

        /// <summary>Segmentos de diretorio que indicam saida de build/cache de ferramenta.</summary>
        private static readonly string[] BuildArtifactSegments =
        {
            @"\obj\", @"\bin\", @"\node_modules\", @"\.nuget\", @"\packages\",
            @"\build\", @"\dist\", @"\out", @"\.vs\", @"\.git\", @"\ref\assembly\"
        };

        /// <summary>
        /// Diretorios de aplicacao onde e NORMAL que um instalador ou updater
        /// extraia executaveis temporarios. Nenhum destes e local de download
        /// do usuario — por isso nao abre brecha.
        /// </summary>
        private static readonly string[] UpdaterScratchDirectories =
        {
            @"\.cache\", @"\crashpad\", @"\crash dumps\", @"\webcache\", @"\gpucache\",
            @"\code cache\", @"\service worker\", @"\interpreter\", @"\blob_storage\",
            @"\downloads\squirrel.temp", @"\pending", @"\staging\", @"\installer_cache\"
        };

        public TrustedLocationPolicy(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            BuildRoots();
        }

        private void BuildRoots()
        {
            _trustedRoots.Clear();
            _trustedPrefixes.Clear();

            void Add(string? path, string label)
            {
                if (string.IsNullOrWhiteSpace(path)) return;
                try
                {
                    if (!Directory.Exists(path)) return;
                    _trustedRoots.Add(Path.GetFullPath(path));
                    _logger.LogDebug($"[TrustedLocation] Categoria '{label}': {path}");
                }
                catch
                {
                    // Ambiente sem o path (ex.: %LOCALAPPDATA% ausente) — sem categoria.
                }
            }

            // --- Sistema operacional ---
            Add(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Windows");
            Add(Environment.GetFolderPath(Environment.SpecialFolder.System), "System");
            Add(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86), "SystemX86");
            Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ProgramFiles");
            Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "ProgramFilesX86");
            Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles), "CommonProgramFiles");
            Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86), "CommonProgramFilesX86");
            Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ProgramData");

            // ---Instaladores do proprio Windows (categoria, nao nome de arquivo) ---
            AddWindowsInstallerRoots();

            // --- Diretorio de instalacao do Voltris ---
            TrySetVoltrisInstallRoot();
        }

        private void AddWindowsInstallerRoots()
        {
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (string.IsNullOrEmpty(windows)) return;

            // Installer, servicing e staging do proprio Windows. Tudo aqui e
            // sobrescrito por componentesSIGNED do sistema, e o usuario nao
            // escreve diretamente.
            foreach (var relative in new[]
            {
                @"Temp",
                @"SoftwareDistribution",
                @"Logs",
                @"Minidump",
                @"Prefetch",
                @"Panther",
                @"debug",
                @"CbsTemp",
                @"WinSxS",
                @"servicing",
                @"tracing"
            })
            {
                try
                {
                    var full = Path.Combine(windows, relative);
                    if (Directory.Exists(full))
                        _trustedRoots.Add(Path.GetFullPath(full));
                }
                catch
                {
                    // Ignorar
                }
            }
        }

        private void TrySetVoltrisInstallRoot()
        {
            try
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                {
                    _voltrisInstallRoot = Path.GetDirectoryName(Path.GetFullPath(exePath));
                }
                else
                {
                    _voltrisInstallRoot = AppContext.BaseDirectory.TrimEnd('\\');
                }
            }
            catch
            {
                _voltrisInstallRoot = null;
            }
        }

        /// <summary>Raiz do proprio executavel do Voltris.</summary>
        public string? VoltrisInstallRoot => _voltrisInstallRoot;

        /// <summary>Categorias reconhecidas (apenas para log/diagnostico).</summary>
        public IReadOnlyList<string> TrustedRoots => _trustedRoots;

        /// <summary>
        /// Avalia o local do arquivo e registra as evidencias no scorecard.
        /// Negativo = absolvicao. Nenhum local sozinho decide o veredito.
        /// </summary>
        public void ApplyLocationScore(NormalizedPath path, DetectionRuleSet notes)
        {
            var canonical = path.Canonical;

            if (IsInWindows(canonical))
            {
                notes.Add(DetectionRule.ExecutableInStagingDir,
                    ShieldPolicy.InTrustedSystemRoot,
                    $"Localizado sob o sistema ({Path.GetFileName(path.Directory)})");
            }
            else if (IsInProgramFiles(canonical))
            {
                notes.Add(DetectionRule.ExecutableInStagingDir,
                    ShieldPolicy.InProgramFiles,
                    "Localizado em Program Files (instalacao confiavel)");
            }
            else if (IsVoltrisOwned(canonical))
            {
                notes.Add(DetectionRule.ExecutableInStagingDir,
                    ShieldPolicy.VoltrisOwnedFile,
                    "Artefato do proprio Voltris");
            }
            else if (IsBuildArtifact(canonical))
            {
                notes.Add(DetectionRule.ExecutableInStagingDir,
                    ShieldPolicy.BuildArtifact,
                    "Saida de compilacao/cache de ferramenta");
            }
            else if (IsUpdaterScratch(canonical))
            {
                notes.Add(DetectionRule.ExecutableInStagingDir,
                    ShieldPolicy.InstallerStagingLayout,
                    "Cache de atualizador de aplicacao");
            }

            // A EXECUCAO dentro de %TEMP% nao e suspeita por si so — e onde TODO
            // instalador do Windows trabalha. Vale 10 pontos, nao veredito. A
            // versao anterior tratava isto como Medium incondicional, o que
            // transformou 15 arquivos Microsoft/Windscribe assinados em 15 alertas.
            if (IsInTempTree(canonical))
            {
                notes.Add(DetectionRule.ExecutableInStagingDir,
                    ShieldPolicy.InStagingDir,
                    "Executavel em diretorio temporario (padrao de instalador)");

                // Estrutura de GUID = staging de instalador conhecido.
                if (PathNormalizer.IsInstallerStagingLayout(canonical))
                {
                    notes.Add(DetectionRule.ExecutableInStagingDir,
                    ShieldPolicy.InstallerStagingLayout,
                        "Layout de staging de instalador (subdiretorio GUID) em %TEMP%");
                }
            }

            if (IsInDownloads(path.DirectoryLower))
            {
                notes.Add(DetectionRule.ExecutableInDownloads,
                    ShieldPolicy.InDownloadsDir,
                    "Localizado em Downloads (destino comum de download do usuario)");

                if (PathNormalizer.IsScriptExtension(path.Extension) ||
                    PathNormalizer.IsExecutableExtension(path.Extension))
                {
                    notes.Add(DetectionRule.ScriptInDownloads,
                        ShieldPolicy.ScriptInDownloads,
                        $"Executavel/script em Downloads: {path.Extension}");
                }
            }
        }

        private static bool IsInWindows(string canonicalPath)
        {
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (string.IsNullOrEmpty(windows)) return false;
            return PathNormalizer.IsUnderDirectory(canonicalPath, windows);
        }

        private static bool IsInProgramFiles(string canonicalPath)
        {
            return PathNormalizer.IsUnderAnyDirectory(canonicalPath, new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            });
        }

        private bool IsVoltrisOwned(string canonicalPath)
        {
            if (string.IsNullOrEmpty(_voltrisInstallRoot)) return false;
            return PathNormalizer.IsUnderDirectory(canonicalPath, _voltrisInstallRoot);
        }

        private static bool IsInTempTree(string canonicalPath)
        {
            var temp = Path.GetTempPath();
            if (string.IsNullOrEmpty(temp)) return false;

            // %TEMP% real e o mesmo em forma normal e 8.3, mas tambem pode ser
            // alcancado por caminho alternativo. Comparamos canonicalizado.
            if (PathNormalizer.IsUnderDirectory(canonicalPath, temp))
                return true;

            // Windows\Temp tambem e staging legitimate.
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrEmpty(windows))
            {
                if (PathNormalizer.IsUnderDirectory(canonicalPath, Path.Combine(windows, "Temp")))
                    return true;
            }

            return false;
        }

        private static bool IsInDownloads(string directoryLower)
        {
            if (string.IsNullOrEmpty(directoryLower)) return false;

            var downloads = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(downloads)) return false;

            var canonicalDownloads = Path.Combine(downloads, "Downloads");
            return directoryLower.StartsWith(canonicalDownloads.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase)
                || directoryLower.Contains(@"\downloads\")
                || directoryLower.EndsWith(@"\downloads", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Reconhece saida de compilacao por SEGMENTO de diretorio, exigindo
        /// fronteira de segmento nos casos em que o segment nao tem barra final.
        ///
        /// A versao anterior comparava "@"\bin"", "@"\out"", "@"\build"" com
        /// Contains sem separador final, casando "\binary", "\output", "\outside".
        /// Uma allowlist que casa segmentos errados e um FURO, nao um filtro.
        /// </summary>
        private static bool IsBuildArtifact(string canonicalPathLower)
        {
            foreach (var segment in BuildArtifactSegments)
            {
                if (canonicalPathLower.Contains(segment, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            // Segmentos sem barra final exigem fronteira explícita.
            foreach (var segment in new[] { @"\bin", @"\out", @"\dist", @"\build" })
            {
                var index = canonicalPathLower.LastIndexOf(segment, StringComparison.OrdinalIgnoreCase);
                if (index < 0) continue;

                var after = index + segment.Length;
                if (after >= canonicalPathLower.Length) return true;
                if (canonicalPathLower[after] == '\\' || canonicalPathLower[after] == '/')
                    return true;
            }

            return false;
        }

        private static bool IsUpdaterScratch(string canonicalPathLower)
        {
            foreach (var segment in UpdaterScratchDirectories)
            {
                if (canonicalPathLower.Contains(segment, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
