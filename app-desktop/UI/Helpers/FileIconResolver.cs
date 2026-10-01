using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VoltrisOptimizer.UI.Helpers
{
    /// <summary>
    /// Ícones REAIS do shell do Windows, por tipo de arquivo.
    ///
    /// Por que não dá para usar <c>Icon.ExtractAssociatedIcon</c>: ele exige
    /// que o arquivo EXISTA no disco. Um arquivo recuperado está, por
    /// definição, apagado — não há caminho para extrair. E um caminho
    /// inventado seria pior: o shell devolveria o ícone do .exe.
    ///
    /// A API correta é <c>SHGetFileInfo</c> com <c>SHGFI_USEFILEATTRIBUTES</c>.
    /// Essa flag diz ao shell: "responda só com base no NOME e na extensão,
    /// não vá ao disco". É o mesmo caminho que o Explorer usa para decidir o
    /// ícone de um item, e é o que devolve o PDF de verdade, o ícone do Acrobat, o
    /// ícone de pasta do Windows 11, o de .msi, .node, .dll e assim por
    /// diante — mudando sozinho com o tema e com os programas instalados.
    ///
    /// Pastas não têm extensão, então <c>SHGFI_USEFILEATTRIBUTES</c>
    /// devolveria o ícone genérico de arquivo. Para elas usa-se
    /// <c>SHGetStockIconInfo</c> com <c>SHIID_FOLDER</c>, que é o ícone de
    /// pasta do sistema.
    /// </summary>
    public static class FileIconResolver
    {
        // ── SHGetFileInfo ────────────────────────────────────────────────
        private const uint SHGFI_ICON            = 0x000000100;
        private const uint SHGFI_LARGEICON       = 0x000000000;
        private const uint SHGFI_SMALLICON       = 0x000000001;
        private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;

        // ── SHGetStockIconInfo ───────────────────────────────────────────
        private const uint SHSTI_ICON            = 0x000000001;
        private const uint SHSTI_SMALLICON       = 0x000000100;
        private const uint SHSTI_USEFILEATTRIBUTES = 0x000000010;
        private const int  SHIID_FOLDER          = 3;
        private const int  SHIID_DOC             = 4;

        [StructLayout(LayoutKind.Sequential)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int    iIcon;
            public uint   dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SHSTOCKICONINFO
        {
            public IntPtr cbSize;
            public IntPtr iIcon;
            public int    iSysImageIndex;
            public int    iIconIndex;
            public uint   dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szName;
            public int    iRes;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        private static extern IntPtr SHGetFileInfo(
            string pszPath, uint dwFileAttributes, out SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        private static extern IntPtr SHGetStockIconInfo(
            int iid, uint uFlags, out uint piSysImageIndex, out SHSTOCKICONINFO psii, uint cbShellInfo);

        [DllImport("user32.dll", SetLastError = false)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        /// <summary>
        /// Cache por tipo. Extrair ícone de shell custa uma chamada ao
        /// explorer.exe por item; numa lista com milhares de linhas isso
        /// travaria a interface. Como o ícone depende só da extensão (e de
        /// "é pasta"), a chave é a extensão — e 30 extensões Movie Store
        /// resolvem 30 vezes, não 30 mil.
        /// </summary>
        private static readonly ConcurrentDictionary<string, ImageSource> _cache = new();

        private static ImageSource? _folderIcon;

        /// <summary>
        /// Resolve o ícone de um item. Não lança: devolve null e a UI usa o
        /// fallback. Um ícone que falhou não pode derrubar a lista.
        /// </summary>
        public static ImageSource? Resolve(string? filename, bool isDirectory)
        {
            try
            {
                if (isDirectory) return ResolveFolder();

                string ext = Path.GetExtension(filename ?? string.Empty).ToLowerInvariant();
                if (ext.Length == 0) ext = ".*";   // sem extensão: arquivo genérico

                return _cache.GetOrAdd(ext, key => ExtractForExtension(key));
            }
            catch
            {
                return null;
            }
        }

        private static ImageSource? ResolveFolder()
        {
            if (_folderIcon != null) return _folderIcon;
            return _folderIcon = ExtractStock(SHIID_FOLDER);
        }

        private static ImageSource? ExtractForExtension(string ext)
        {
            // Um nome com a extensão basta: com SHGFI_USEFILEATTRIBUTES o
            // shell responde pelo nome, sem tocar o disco. "x.pdf" nunca
            // existiu e mesmo assim devolve o ícone de PDF.
            string fake = "arquivo" + (ext == ".*" ? ".txt" : ext);
            uint flags = SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES;
            IntPtr r = SHGetFileInfo(fake, 0, out SHFILEINFO info, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
            if (r == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
            return ToImageSource(info.hIcon);
        }

        private static ImageSource? ExtractStock(int iid)
        {
            uint sysIndex;
            var ii = new SHSTOCKICONINFO
            {
                cbSize = (IntPtr)Marshal.SizeOf<SHSTOCKICONINFO>(),
            };
            uint flags = SHSTI_ICON | SHSTI_SMALLICON | SHSTI_USEFILEATTRIBUTES;
            // O P/Invoke declara o 4º parâmetro como `out` (SHSTOCKICONINFO é
            // blittable), então a chamada precisa usar `out`, não `ref`.
            IntPtr r = SHGetStockIconInfo(iid, flags, out sysIndex, out ii, (uint)Marshal.SizeOf<SHSTOCKICONINFO>());
            if (r == IntPtr.Zero || ii.iIcon == IntPtr.Zero) return null;
            return ToImageSource(ii.iIcon);
        }

        /// <summary>
        /// Converte o HICON do shell em ImageSource e DESTRÓI o HICON.
        ///
        /// Não destruir é vazamento de GDI: numa lista com milhares de itens
        /// o processo esgota os handles e o Windows começa a pintar tudo de
        /// preto. Por isso o <c>try/finally</c> é obrigatório, e a cópia é
        /// feita ANTES de destruir.
        /// </summary>
        private static ImageSource? ToImageSource(IntPtr hIcon)
        {
            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(
                    hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());

                // Congela: o bitmap não é alterado depois, e uma imagem
                // congelada pode ser compartilhada entre threads sem
                // bloqueio — o que importa numa lista com EnableRowVirtualization.
                source.Freeze();
                return source;
            }
            finally
            {
                if (hIcon != IntPtr.Zero) DestroyIcon(hIcon);
            }
        }

        /// <summary>Limpa o cache. Usado se o tema do Windows mudar em runtime.</summary>
        public static void ClearCache()
        {
            _cache.Clear();
            _folderIcon = null;
        }

        /// <summary>Diagnóstico: writes no log quais extensões foram resolvidas.</summary>
        public static void LogResolution(IEnumerable<string> filenames, bool isDirectory, Action<string> log)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in filenames)
            {
                var ext = Path.GetExtension(f ?? string.Empty).ToLowerInvariant();
                if (ext.Length == 0) ext = "(sem extensao)";
                counts.TryGetValue(ext, out int c);
                counts[ext] = c + 1;
            }
            foreach (var kv in counts.OrderByDescending(k => k.Value))
            {
                var img = Resolve(kv.Key == "(sem extensao)" ? null : "x" + kv.Key, isDirectory);
                log($"[ICONE] {kv.Key,-10} x{kv.Value,-6} -> {(img == null ? "FALHOU (sera usado o fallback)" : "ok " + ((BitmapSource)img).PixelWidth + "x" + ((BitmapSource)img).PixelHeight)}");
            }
        }
    }
}
