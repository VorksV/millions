using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Shield
{
    /// <summary>
    /// Resultado da normalização de um caminho observado pelo FileSystemWatcher.
    /// </summary>
    public sealed class NormalizedPath
    {
        /// <summary>Caminho como o Windows realmente resolve (junction/symlink/8.3 desfeitos).</summary>
        public string Canonical { get; init; } = string.Empty;

        /// <summary>Caminho original recebido do watcher.</summary>
        public string Original { get; init; } = string.Empty;

        public string FileName { get; init; } = string.Empty;
        public string FileNameLower { get; init; } = string.Empty;
        public string Extension { get; init; } = string.Empty;
        public string Directory { get; init; } = string.Empty;
        public string DirectoryLower { get; init; } = string.Empty;

        /// <summary>True quando o arquivo tem magic MZ: e um PE, independente da extensao.</summary>
        public bool IsPortableExecutable { get; init; }

        /// <summary>True quando o PE tem o bit IMAGE_FILE_DLL ligado.</summary>
        public bool IsPeLibrary { get; init; }

        /// <summary>Nome do fluxo de dados alternativo (ADS), quando existir.</summary>
        public string? AlternateDataStream { get; init; }

        /// <summary>True quando o caminho tem um stream oculto: "foto.jpg.exe:payload".</summary>
        public bool HasHiddenStream => AlternateDataStream is not null;

        /// <summary>Extensao EFFECTIVA. Se o arquivo e PE mas a extensao nao e executavel,
        /// a extensao real vem do cabecalho PE — e o descasamento vira evidencia.</summary>
        public string EffectiveExtension { get; init; } = string.Empty;

        /// <summary>Nome sem a extensao, para deteccao de extensao dupla.</summary>
        public string NameWithoutExtension { get; init; } = string.Empty;

        public long Length { get; init; }
    }

    /// <summary>
    /// Normalizacao de caminhos — a camada 0 do pipeline de deteccao.
    ///
    /// POR QUE ISSO EXISTE: a versao anterior do Shield aplicava regras de substring
    /// sobre o caminho cru do FileSystemWatcher. Isso quebrava em tres direcoes:
    ///
    /// 1. EVASAO. Um malware em "C:\Users\V\AppData\Local\Temp" casava com a regra,
    ///    mas o mesmo arquivo alcancavel por junction em "C:\Windows\Fonts" nao
    ///    casava — e vice-versa. O atacante escolhe o caminho.
    /// 2. FALSO POSITIVO. "@"\bin"", "@"\out"", "@"\build"" eram comparados com
    ///    Contains sem separador final, entao casavam com "\binary", "\output",
    ///    "\outside" — silenciosamente excluindo (e portanto cegos para) paths
    ///    verdadeiros. A allowlist virava um furo em vez de um filtro.
    /// 3. NOME 8.3. "C:\Users\V\AppData\Local\Temp" e
    ///    "C:\Users\V\AppData\Loc~1\Temp" sao o mesmo diretorio, mas strings
    ///    diferentes. Sem canonicalizar, a regra decide diferente para o mesmo lugar.
    ///
    /// Alem disso a extensao vem do NOME do arquivo, nunca do conteudo. Um
    /// "relatorio.txt" que e na verdade um PE passa como texto; e um
    /// "foto.jpg.exe:payload" — o veiculo classico de execucao sem arquivo —
    /// era tratado como JPEG.
    /// </summary>
    public static class PathNormalizer
    {
        private const uint FileFlagBackupSemantics = 0x02000000;
        private const uint VolumeNameDos = 0x00000000;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint GetFinalPathNameByHandle(
            IntPtr hFile, StringBuilder lpszFilePath, uint cchFilePath, uint dwFlags);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFileW(
            string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition,
            uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint GenericRead = 0x80000000;
        private const uint ShareReadWriteDelete = 0x00000007;
        private const uint OpenExisting = 3;

        private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".dll", ".sys", ".scr", ".ocx", ".cpl", ".drv",
            ".efi", ".com", ".pif", ".msi", ".msp", ".mst", ".hta", ".msc"
        };

        private static readonly HashSet<string> ScriptExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".ps1", ".psm1", ".bat", ".cmd"
        };

        private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".rtf",
            ".odt", ".ods", ".csv", ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp",
            ".svg", ".ico", ".mp3", ".mp4", ".avi", ".mkv", ".mov", ".wav", ".zip",
            ".rar", ".7z", ".iso", ".html", ".htm", ".lnk", ".url"
        };

        public static bool IsExecutableExtension(string extension) =>
            ExecutableExtensions.Contains(extension);

        public static bool IsScriptExtension(string extension) =>
            ScriptExtensions.Contains(extension);

        public static bool IsDocumentExtension(string extension) =>
            DocumentExtensions.Contains(extension);

        public static bool IsWindowsSystemExtension(string extension) =>
            extension is ".sys" or ".dll" or ".drv" or ".ocx" or ".cpl" or ".efi" or ".msi";

        /// <summary>
        /// Canonicaliza o caminho resolvendo junction, symlink e nomes curtos 8.3,
        /// e le a assinatura do PE do proprio arquivo.
        /// Nunca lanca: falha de normalizacao degrada para o caminho original.
        /// </summary>
        public static NormalizedPath Normalize(string rawPath, ILoggingService? logger = null)
        {
            var original = rawPath ?? string.Empty;
            var resolved = TryCanonicalize(original) ?? original;

            // Streams alternativos sao um vetor de execucao sem arquivo: o
            // conteudo real vive depois do ':' e nao aparece na listagem de
            // diretorio. Canonicalizacao e feita no caminho INTEIRO, com o stream
            // incluido, porque o que se quer resolver (junction, nome curto) e o
            // hospedeiro.
            string? ads = null;
            var streamPath = resolved;
            var colonIndex = IndexOfAlternateDataStreamColon(resolved);
            if (colonIndex >= 0)
            {
                ads = resolved[(colonIndex + 1)..];
                streamPath = resolved[..colonIndex];
            }

            var canonical = streamPath;

            var fileName = SafeGetFileName(canonical);
            var fileNameLower = fileName.ToLowerInvariant();
            var extension = SafeGetExtension(canonical).ToLowerInvariant();
            var directory = SafeGetDirectory(canonical);
            var directoryLower = directory.ToLowerInvariant();

            // O cabecalho PE e lido do CONTEUDO EFETIVO: quando ha stream, e o
            // stream que sera executado, nao o arquivo hospedeiro. Ler o host
            // aria descrever um JPEG como "nao-PE" e liberar o payload.
            var (isPe, isLibrary, size) = ReadPeHeader(resolved);

            // Extensao efetiva: se o conteudo e PE mas o nome diz outra coisa,
            // quem vale e o conteudo. E o inverso tambem — um ".exe" que nao tem
            // magic MZ esta sendo falsificado.
            var effectiveExtension = extension;
            if (isPe && !IsExecutableExtension(extension))
                effectiveExtension = isLibrary ? ".dll" : ".exe";

            // O NOME DO STREAM e o que executa, nao o nome do arquivo hospedeiro.
            // "foto.jpg:payload.exe" e um executavel com icon de JPEG; tratar o
            // conjunto pela extensao do hospedeiro (".jpg") faria a deteccao
            // classificar conteudo executavel como imagem inofensiva.
            if (ads is not null)
            {
                var adsExtension = SafeGetExtension(ads).ToLowerInvariant();
                if (!string.IsNullOrEmpty(adsExtension))
                    effectiveExtension = adsExtension;
            }

            var nameWithoutExtension = fileName.Length > 0
                ? Path.GetFileNameWithoutExtension(fileName)
                : fileName;

            return new NormalizedPath
            {
                Canonical = canonical,
                Original = original,
                FileName = fileName,
                FileNameLower = fileNameLower,
                Extension = extension,
                Directory = directory,
                DirectoryLower = directoryLower,
                IsPortableExecutable = isPe,
                IsPeLibrary = isLibrary,
                AlternateDataStream = ads,
                EffectiveExtension = effectiveExtension,
                NameWithoutExtension = nameWithoutExtension,
                Length = size
            };
        }

        /// <summary>
        /// Resolve o caminho final real via handle do filesystem. Junction, symlink
        /// e nome curto 8.3 aparecem no "Original" mas somem aqui.
        /// </summary>
        private static string? TryCanonicalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            // O teste de existencia roda no caminho SEM o stream: File.Exists
            // responde falso para "arquivo:stream" em varias versoes do .NET, e
            // perder a canonicalizacao por causa disso seria unfair.
            var probePath = path;
            var probeColon = IndexOfAlternateDataStreamColon(path);
            if (probeColon >= 0) probePath = path[..probeColon];

            if (!File.Exists(probePath) && !Directory.Exists(probePath))
                return null;

            IntPtr handle = IntPtr.Zero;
            try
            {
                handle = CreateFileW(path, 0, ShareReadWriteDelete, IntPtr.Zero,
                    OpenExisting, FileFlagBackupSemantics, IntPtr.Zero);
                if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                    return null;

                var capacity = 1024;
                var buffer = new StringBuilder(capacity);
                uint length = GetFinalPathNameByHandle(handle, buffer, (uint)capacity, VolumeNameDos);
                if (length == 0)
                    return null;

                if (length >= capacity)
                {
                    buffer = new StringBuilder((int)length + 2);
                    length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, VolumeNameDos);
                    if (length == 0) return null;
                }

                var result = buffer.ToString();
                if (result.StartsWith(@"\\?\", StringComparison.Ordinal))
                    result = result[4..];

                return string.IsNullOrWhiteSpace(result) ? null : result;
            }
            catch
            {
                return null;
            }
            finally
            {
                if (handle != IntPtr.Zero && handle != new IntPtr(-1))
                    CloseHandle(handle);
            }
        }

        /// <summary>
        /// Localiza o ':' que abre um alternate data stream, ignorando o ':' da
        /// letra de unidade. "C:\a\b.dll" -> -1. "C:\a\b.dll:payload" -> 11.
        /// </summary>
        private static int IndexOfAlternateDataStreamColon(string path)
        {
            if (path.Length < 3) return -1;
            for (int i = 2; i < path.Length; i++)
            {
                if (path[i] != ':') continue;
                // ":$DATA" e streams nomeados sao reais; o que buscamos e o veiculo
                // de execucao, que sempre vem seguido de um nome de stream util.
                if (i + 1 >= path.Length) return -1;
                return i;
            }
            return -1;
        }

        /// <summary>
        /// Le "MZ", o offset do cabecalho PE e o bit IMAGE_FILE_DLL. E o que
        /// permite detectar conteudo executavel com extensao inofensiva.
        /// </summary>
        private static (bool isPe, bool isLibrary, long length) ReadPeHeader(string path)
        {
            try
            {
                // NAO usar FileInfo.Exists para decidir acesso: um caminho de
                // alternate data stream ("foto.jpg:payload.exe") responde
                // false para FileInfo.Exists, porque nao existe uma entrada de
                // diretorio para ele. Usar FileInfo aqui fazia o stream ser
                // lido como "nao-PE", e o payload executavel de dentro da imagem
                // passava despercebido.
                //
                // O FileStream e a fonte de verdade: ele abre o stream nativamente
                // e expoe o comprimento real do conteudo.
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.None);

                var length = stream.Length;
                if (length < 0x40) return (false, false, length);

                var dosHeader = new byte[0x40];
                if (stream.Read(dosHeader, 0, dosHeader.Length) != dosHeader.Length)
                    return (false, false, length);

                if (dosHeader[0] != (byte)'M' || dosHeader[1] != (byte)'Z')
                    return (false, false, length);

                var peOffset = BitConverter.ToInt32(dosHeader, 0x3C);
                if (peOffset <= 0 || peOffset > length - 6)
                    return (true, false, length); // tem MZ mas cabecalho PE invalido — suspeito por si so

                stream.Position = peOffset;
                var signature = new byte[4];
                if (stream.Read(signature, 0, 4) != 4)
                    return (true, false, length);

                if (signature[0] != (byte)'P' || signature[1] != (byte)'E' ||
                    signature[2] != 0 || signature[3] != 0)
                    return (true, false, length);

                // IMAGE_FILE_HEADER: Machine(2) NumberOfSections(2) TimeDateStamp(4)
                // PointerToSymbolTable(4) NumberOfSymbols(4) SizeOfOptionalHeader(2)
                // Characteristics(2)  -> Characteristics no offset 22
                var fileHeader = new byte[24];
                if (stream.Read(fileHeader, 0, fileHeader.Length) != fileHeader.Length)
                    return (true, false, length);

                const ushort imageFileDll = 0x2000;
                var characteristics = BitConverter.ToUInt16(fileHeader, 22);
                var isLibrary = (characteristics & imageFileDll) != 0;

                return (true, isLibrary, length);
            }
            catch
            {
                return (false, false, 0);
            }
        }

        /// <summary>
        /// Compara caminho com frontera de segmento. "C:\a\bin" NAO esta sob
        /// "C:\a\binx" — ao contrario de Contains, que casaria os dois.
        /// Toda checagem de diretorio no Shield passa por aqui.
        /// </summary>
        public static bool IsUnderDirectory(string canonicalPath, string root)
        {
            if (string.IsNullOrEmpty(canonicalPath) || string.IsNullOrEmpty(root))
                return false;

            var path = EnsureTrailingSeparator(canonicalPath);
            var normalizedRoot = EnsureTrailingSeparator(root.TrimEnd('\\', '/'));

            return path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsUnderAnyDirectory(string canonicalPath, IEnumerable<string> roots)
        {
            foreach (var root in roots)
            {
                if (IsUnderDirectory(canonicalPath, root))
                    return true;
            }
            return false;
        }

        private static string EnsureTrailingSeparator(string path)
        {
            if (path.Length == 0) return path;
            var last = path[^1];
            return last == '\\' || last == '/' ? path : path + "\\";
        }

        /// <summary>
        /// Detecta o padrao de staging de instalador: %TEMP%\{GUID}\arquivo.
        ///
        /// ESTA E A RESPOSTA PROFISSIONAL A LISTA DE WHITELIST POR NOME DE ARQUIVO.
        /// A versao anterior enumerava 22 nomes de DLL do DISM porque so conseguia
        /// reconhecer "arquivo legitimo do Windows em %TEMP%" perguntando pelo NOME do
        /// arquivo — o que quebra no proximo componente que o Windows extrair.
        /// Reconhecer a ESTRUTURA do diretorio de staging cobre DISM, Sysprep,
        /// Setup, MSI, Chrome, Edge, Office e qualquer coisa futura, sem nunca
        /// citar o nome de um arquivo sequer.
        /// </summary>
        public static bool IsInstallerStagingLayout(string canonicalPath)
        {
            var directory = SafeGetDirectory(canonicalPath);
            if (string.IsNullOrEmpty(directory)) return false;

            var leaf = directory;
            int slash = leaf.LastIndexOf('\\');
            if (slash >= 0)
                leaf = leaf[(slash + 1)..];

            return IsGuid(leaf);
        }

        /// <summary>
        /// Reconhece um nome de segmento como GUID canonico. Usado para identificar
        /// diretorios de staging criados por instaladores e por ferramentas do
        /// Windows (DISM, Sysprep, Setup).
        /// </summary>
        public static bool IsGuid(string? value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 36) return false;

            return Guid.TryParseExact(value, "D", out _)
                || Guid.TryParse(value, out _);
        }

        /// <summary>
        /// Detecta extensao dupla enganosa com a FORMA do ataque, nao com a
        /// presenca de ponto.
        ///
        /// A versao anterior comparava apenas `nameWithoutExt.Contains('.')`, o que
        /// disparava em "cpu-z_2.11.exe", "hwinfo64_7.56.exe" — o padrao da industria
        /// — e chegou a marcar o HWMonitor. A forma real do ataque e documento ou
        /// midia seguida de executavel: "fatura.pdf.exe". Exigimos entao que o
        /// trecho intermediario seja uma extensao de documento/media E que o
        /// executavel final exista de verdade.
        /// </summary>
        public static bool IsDeceptiveDoubleExtension(NormalizedPath path)
        {
            if (!IsExecutableExtension(path.EffectiveExtension)) return false;

            var name = path.NameWithoutExtension;
            if (string.IsNullOrEmpty(name)) return false;

            int lastDot = name.LastIndexOf('.');
            if (lastDot <= 0) return false;

            var intermediate = name[(lastDot + 1)..];

            // Numero de versao ("2.11", "7.56", "2024") e o padrao de nome legitimo.
            if (intermediate.Length == 0) return false;
            if (intermediate.All(char.IsDigit)) return false;

            // "setup_1.0.0_beta" — unico ponto, sem documento antes.
            if (intermediate.Contains('.')) return false;

            var intermediateExt = "." + intermediate.ToLowerInvariant();
            if (!DocumentExtensions.Contains(intermediateExt)) return false;

            // A forma do ataque esta comprovada: "fatura.pdf" e o nome sem
            // extensao e ".exe" e a extensao real. Nao ha nada a exigir alem
            // disso.
            //
            // Nao se exige um terceiro ponto. Uma verificacao extra desse tipo
            // (exigir que o prefixo antes do documento tambem contenha um ponto)
            // rejeitaria justamente o caso que importa: "fatura.pdf.exe" tem
            // apenas dois pontos, e "relatorio.fatura.pdf.exe" tres. Exigir tres
            // descartaria o vetor mais simples e mais usado.
            return true;
        }

        private static string SafeGetFileName(string path)
        {
            try { return Path.GetFileName(path) ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static string SafeGetExtension(string path)
        {
            try { return Path.GetExtension(path) ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static string SafeGetDirectory(string path)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                return string.IsNullOrEmpty(dir) ? string.Empty : dir.TrimEnd('\\');
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
