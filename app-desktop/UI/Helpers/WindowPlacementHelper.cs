using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace VoltrisOptimizer.UI.Helpers
{
    /// <summary>
    /// Posicionamento de janelas: centralização real, ciente de DPI e do monitor correto.
    ///
    /// POR QUE ESTE HELPER EXISTE
    /// ===========================
    /// O WPF oferece <c>WindowStartupLocation="CenterScreen"</c>, mas ele calcula o
    /// centro ANTES de a janela ter o seu tamanho final. Na prática isso só é
    /// seguro quando <c>Width</c>/<c>Height</c> são explicitamente definidos. Com
    /// <c>SizeToContent="Manual"</c> e nenhuma largura declarada, o WPF deriva o
    /// tamanho inicial do <i>DesiredSize</i> do conteúdo — que, no primeiro passo de
    /// layout, ainda está incompleto (recursos dinâmicos e bindings de localização
    /// não resolveram). O centro é calculado sobre esse tamanho errado e nunca é
    /// recalculado: o resultado é uma janela permanentemente deslocada (no caso
    /// relatado, para a direita).
    ///
    /// Além disso, <see cref="System.Windows.SystemParameters.WorkArea"/> é
    /// SEMPRE a área de trabalho do monitor PRIMÁRIO. Em um setup com mais de um
    /// monitor, ou com DPI misto, isso centraliza no monitor errado e nas
    /// coordenadas erradas.
    ///
    /// O app é <c>PerMonitorV2</c> (ver app.manifest), ou seja: toda coordenada vinda
    /// de Win32 é física (pixels) e precisa ser convertida para DIPs usando o DPI da
    /// janela — nunca assumida como 1:1. Este helper faz exatamente isso e aplica o
    /// resultado em UMA única chamada <c>SetWindowPos</c>, o que evita o salto visual
    /// de reposicionar quadro a quadro.
    /// </summary>
    public static class WindowPlacementHelper
    {
        private const uint SWP_NOSIZE     = 0x0001;
        private const uint SWP_NOZORDER   = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;

        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromRect(ref RECT lprc, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
                                               int X, int Y, int cx, int cy, uint uFlags);

        /// <summary>
        /// Fração da área de trabalho usada como limite para o tamanho de uma janela
        /// que ainda não tem tamanho definido. Deixa sempre uma borda visível, para
        /// que a barra de tarefas e as bordas do monitor continuem alcançáveis.
        /// </summary>
        public const double MaxWorkAreaFillRatio = 0.92;

        /// <summary>
        /// Escala DPI real da janela. Usa <see cref="VisualTreeHelper.GetDpi"/> quando
        /// a árvore visual já existe e cai para 1.0 enquanto a janela ainda não foi
        /// apresentada (nesse ponto não há HWND, logo não há nada a converter).
        /// </summary>
        public static double GetDpiScale(Window window)
        {
            if (window == null) return 1.0;

            try
            {
                if (window.IsLoaded || VisualTreeHelper.GetParent(window) != null)
                {
                    var dpi = VisualTreeHelper.GetDpi(window);
                    if (dpi.DpiScaleX > 0 && !double.IsNaN(dpi.DpiScaleX) && !double.IsInfinity(dpi.DpiScaleX))
                        return dpi.DpiScaleX;
                }
            }
            catch
            {
                // A árvore visual pode não estar pronta ainda.
            }

            return 1.0;
        }

        /// <summary>
        /// Resolve o HWND da janela sem criar a fonte. Retorna <see cref="IntPtr.Zero"/>
        /// enquanto a janela ainda não tem fonte criada, para que o chamador possa
        /// simplesmente repetir depois.
        /// </summary>
        private static IntPtr TryGetHandle(Window window)
        {
            try
            {
                if (window == null) return IntPtr.Zero;
                if (window.IsLoaded) return new WindowInteropHelper(window).Handle;
            }
            catch { }
            return IntPtr.Zero;
        }

        /// <summary>
        /// Área de trabalho do monitor em DIPs, para o monitor em que a janela está
        /// (ou o mais próximo, se a janela ainda não tem HWND). Fallback para
        /// <see cref="SystemParameters.WorkArea"/> apenas se a API do Win32 falhar.
        /// </summary>
        public static bool TryGetWorkAreaInDips(Window window, out Rect workArea)
        {
            workArea = SystemParameters.WorkArea;
            if (window == null) return false;

            try
            {
                var hwnd = TryGetHandle(window);
                if (hwnd == IntPtr.Zero) return false;

                if (!GetWindowRect(hwnd, out var winRect)) return false;

                var monitor = MonitorFromRect(ref winRect, MONITOR_DEFAULTTONEAREST);
                if (monitor == IntPtr.Zero) return false;

                var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (!GetMonitorInfo(monitor, ref info)) return false;

                double scale = GetDpiScale(window);
                workArea = new Rect(
                    info.rcWork.Left / scale,
                    info.rcWork.Top / scale,
                    info.rcWork.Width / scale,
                    info.rcWork.Height / scale);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Centraliza a janela pelo caminho GERENCIADO, para ser chamado no
        /// construtor, antes de existir HWND.
        ///
        /// POR QUE ISTO PRECISA EXISTIR
        /// ============================
        /// A janela declara <c>WindowStartupLocation="Manual"</c> e não define
        /// Left/Top no XAML. Sem uma posição explícita, o WPF deixa o primeiro
        /// quadro nascer na posição padrão do Windows (canto/cascata), e a
        /// janela só é corrigida depois — por OnSourceInitialized ou, quando ele
        /// falha, por OnContentRendered, que já é DEPOIS do primeiro paint. O
        /// usuário vê a janela abrir fora do centro e se endireitar na frente.
        ///
        /// Antes do HWND existir não há como consultar o monitor real, então este
        /// método usa <see cref="SystemParameters.WorkArea"/> (monitor primário).
        /// Isso é deliberado: define uma posição inicial CORRETA, e o caminho
        /// preciso por monitor continua depois refinando o resultado — agora sem
        /// que o usuário já tenha visto a janela torta.
        ///
        /// Não mexe em janelas maximizadas ou minimizadas, onde Left/Top não
        /// descrevem a posição visível.
        /// </summary>
        /// <returns><c>true</c> se a posição foi definida.</returns>
        public static bool TryCenterBeforeShow(Window window)
        {
            if (window == null) return false;

            try
            {
                if (WindowStateHelper.IsMaximizedOrMinimized(window)) return false;

                var work = SystemParameters.WorkArea;
                if (work.Width <= 0 || work.Height <= 0) return false;

                double w = ResolveManagedWidth(window);
                double h = ResolveManagedHeight(window);
                if (w <= 0 || h <= 0) return false;

                SyncManagedPosition(
                    window,
                    Math.Round(work.Left + (work.Width - w) / 2.0),
                    Math.Round(work.Top + (work.Height - h) / 2.0));
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Centraliza a janela na área de trabalho do monitor em que ela está.
        ///
        /// O tamanho usado é o tamanho REAL atual (via <c>GetWindowRect</c>, que
        /// inclui a borda não-cliente e por isso é a única fonte verdadeira), com
        /// fallback para <c>ActualWidth/ActualHeight</c> e, por fim, para
        /// <c>Width/Height</c>. A posição é aplicada de uma vez só, o que evita
        /// qualquer animação de reposicionamento.
        /// </summary>
        /// <returns><c>true</c> se a centralização foi aplicada.</returns>
        public static bool CenterOnCurrentMonitor(Window window)
        {
            if (window == null) return false;

            try
            {
                if (WindowStateHelper.IsMaximizedOrMinimized(window)) return false;

                var hwnd = TryGetHandle(window);
                if (hwnd == IntPtr.Zero) return false;

                if (!GetWindowRect(hwnd, out var winRect))
                {
                    // Sem HWND válido, ainda assim tenta pelo caminho gerenciado.
                    return CenterManaged(window);
                }

                var monitor = MonitorFromRect(ref winRect, MONITOR_DEFAULTTONEAREST);
                if (monitor == IntPtr.Zero) return false;

                var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (!GetMonitorInfo(monitor, ref info)) return CenterManaged(window);

                // O trabalho é feito em pixels FÍSICOS: a largura da janela vem do
                // Win32 e a área de trabalho também, então não há conversão no meio
                // do caminho. Só se converte no fim, para atribuir Left/Top em DIPs.
                double scale = GetDpiScale(window);
                if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale)) scale = 1.0;

                int widthPx = winRect.Width;
                int heightPx = winRect.Height;
                if (widthPx <= 0 || heightPx <= 0) return CenterManaged(window);

                int x = info.rcWork.Left + (info.rcWork.Width - widthPx) / 2;
                int y = info.rcWork.Top + (info.rcWork.Height - heightPx) / 2;

                if (!SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0,
                                 SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE))
                {
                    return CenterManaged(window);
                }

                // Sincroniza o estado gerenciado com o que foi realmente aplicado,
                // para que um centro automático posterior não recalcule sobre o valor
                // velho e re-desloque a janela.
                SyncManagedPosition(window, x / scale, y / scale);
                return true;
            }
            catch
            {
                return CenterManaged(window);
            }
        }

        /// <summary>
        /// Caminho gerenciado (sem Win32), usado antes de a janela ter HWND e como
        /// fallback quando a API falha.
        /// </summary>
        private static bool CenterManaged(Window window)
        {
            try
            {
                var work = SystemParameters.WorkArea;
                if (work.Width <= 0 || work.Height <= 0) return false;

                double w = ResolveManagedWidth(window);
                double h = ResolveManagedHeight(window);

                window.Left = Math.Round(work.Left + (work.Width - w) / 2.0);
                window.Top = Math.Round(work.Top + (work.Height - h) / 2.0);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void SyncManagedPosition(Window window, double left, double top)
        {
            try
            {
                // Um único BeginAnimation(…, null) limpa qualquer animação de
                // Left/Top que ainda esteja segurando o valor antigo (FillBehavior
                // HoldEnd faria a propriedade voltar ao valor animado).
                window.BeginAnimation(Window.LeftProperty, null);
                window.BeginAnimation(Window.TopProperty, null);
                window.Left = left;
                window.Top = top;
            }
            catch { }
        }

        /// <summary>
        /// Largura efetiva da janela em DIPs, resolvida na ordem de confiabilidade:
        /// tamanho real do HWND, depois <c>ActualWidth</c>, depois <c>Width</c>.
        /// </summary>
        public static double ResolveManagedWidth(Window window)
        {
            try
            {
                var hwnd = TryGetHandle(window);
                if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out var r) && r.Width > 0)
                {
                    double scale = GetDpiScale(window);
                    if (scale <= 0 || double.IsNaN(scale)) scale = 1.0;
                    return r.Width / scale;
                }
            }
            catch { }

            if (window.ActualWidth > 0) return window.ActualWidth;
            if (!double.IsNaN(window.Width) && window.Width > 0) return window.Width;
            return 0;
        }

        /// <summary>Altura efetiva da janela em DIPs. Espelha <see cref="ResolveManagedWidth"/>.</summary>
        public static double ResolveManagedHeight(Window window)
        {
            try
            {
                var hwnd = TryGetHandle(window);
                if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out var r) && r.Height > 0)
                {
                    double scale = GetDpiScale(window);
                    if (scale <= 0 || double.IsNaN(scale)) scale = 1.0;
                    return r.Height / scale;
                }
            }
            catch { }

            if (window.ActualHeight > 0) return window.ActualHeight;
            if (!double.IsNaN(window.Height) && window.Height > 0) return window.Height;
            return 0;
        }

        /// <summary>
        /// Ajusta <paramref name="window"/> ao maior tamanho que caiba na área de
        /// trabalho do monitor, respeitando Min/Max. Usado para dar à janela um
        /// tamanho DETERMINÍSTICO antes do primeiro <c>Show()</c>, que é o
        /// pré-requisito para que a centralização seja correta.
        /// </summary>
        public static void FitToWorkArea(Window window, double desiredWidth, double desiredHeight)
        {
            if (window == null) return;

            try
            {
                var work = SystemParameters.WorkArea;
                if (work.Width <= 0 || work.Height <= 0) return;

                double maxW = work.Width * MaxWorkAreaFillRatio;
                double maxH = work.Height * MaxWorkAreaFillRatio;

                double w = ClampWindowDimension(window, desiredWidth, window.MinWidth, window.MaxWidth, maxW);
                double h = ClampWindowDimension(window, desiredHeight, window.MinHeight, window.MaxHeight, maxH);

                window.Width = w;
                window.Height = h;
            }
            catch { }
        }

        private static double ClampWindowDimension(Window window, double desired, double min, double max, double hardMax)
        {
            double value = desired;

            if (double.IsNaN(value) || value <= 0)
                value = hardMax; // Sem tamanho desejado definido: usa o limite da tela.

            double lower = double.IsNaN(min) || min <= 0 ? 0 : min;
            double upper = hardMax;
            if (!double.IsNaN(max) && max > 0) upper = Math.Min(upper, max);

            if (upper < lower)
            {
                // Min maior que o que cabe na tela (ex.: monitor muito pequeno).
                // A área de trabalho tem precedência: melhor uma janela encolhida
                // do que uma janela que não cabe e nasce fora da tela.
                return Math.Max(1, hardMax);
            }

            value = Math.Min(value, upper);
            value = Math.Max(value, lower);
            return Math.Round(value);
        }
    }

    /// <summary>
    /// Guarda de estado da janela. Centralizado aqui porque
    /// <c>WindowPlacementHelper</c> precisa recusar centralizar uma janela
    /// maximizada (SetWindowPos seria ignorado) ou minimizada (o HWND não tem
    /// geometria válida).
    /// </summary>
    internal static class WindowStateHelper
    {
        public static bool IsMaximizedOrMinimized(Window window)
        {
            try
            {
                return window.WindowState == WindowState.Maximized
                    || window.WindowState == WindowState.Minimized;
            }
            catch
            {
                return true; // Em caso de dúvida, não mexe na janela.
            }
        }
    }
}
