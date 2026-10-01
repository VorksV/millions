using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using VoltrisOptimizer.Services;
using System.IO;

namespace VoltrisOptimizer.Helpers
{
    /// <summary>
    /// Gerenciador centralizado de composição visual do Windows.
    /// Responsável por: Acrylic, Mica, bordas arredondadas, transparência e DWM.
    /// 
    /// Arquitetura:
    /// - Detecta automaticamente Windows 10 vs Windows 11
    /// - Aplica a melhor API disponível para cada versão
    /// - Centraliza toda a lógica de composição em um único lugar
    /// - Fornece API simples: Enable/Update/Disable
    /// - Logs detalhados para debugging
    /// </summary>
    public static class VisualEffectsManager
    {
        private static readonly ILoggingService _logger = App.LoggingService ?? new LoggingService(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VoltrisOptimizer", "Logs"));
        private static bool _isInitialized = false;
        private static WindowsVersion _detectedVersion;
        private static bool _acrylicEnabled = false;
        private static bool _transparencyEnabledInSettings = true;
        // Estado REAL: true somente quando SetWindowCompositionAttribute foi aceito pelo DWM
        // numa janela visível. Evita o falso positivo "Acrylic consistente" no startup minimizado.
        private static bool _acrylicActuallyApplied = false;
        private static readonly HashSet<Window> _pendingReapplyWindows = new HashSet<Window>();

        #region Win32 Imports

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMargins);

        [DllImport("user32.dll")]
        private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidth, int nHeight);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public int AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MARGINS
        {
            public int cxLeftWidth;
            public int cxRightWidth;
            public int cyTopHeight;
            public int cyBottomHeight;
        }

        private const int WCA_ACCENT_POLICY = 19;
        private const int ACCENT_DISABLED = 0;
        private const int ACCENT_ENABLE_BLURBEHIND = 3;
        private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWCP_ROUND = 2;
        private const int DWMWCP_DONROUND = 0;

        private const int DWMSBT_MAINWINDOW = 2;
        private const int DWMSBT_TRANSIENTWINDOW = 3;
        private const int DWMSBT_TABBEDWINDOW = 4;

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TRANSPARENT = 0x00000020;

        #endregion

        #region Enums

        public enum WindowsVersion
        {
            Unknown,
            Windows10,
            Windows11
        }

        public enum WindowType
        {
            MainWindow,
            Widget,
            Popup,
            Tooltip,
            TrayMenu,
            Dialog,
            Overlay
        }

        public enum BackdropType
        {
            None,
            Acrylic,
            Mica,
            Blur
        }

        #endregion

        #region Initialization

        /// <summary>
        /// Inicializa o gerenciador e detecta a versão do Windows.
        /// Deve ser chamado uma vez no startup da aplicação.
        /// </summary>
        public static void Initialize()
        {
            if (_isInitialized)
            {
                _logger?.LogInfo("[VisualEffectsManager] Já inicializado, ignorando");
                return;
            }

            try
            {
                var version = Environment.OSVersion.Version;
                _detectedVersion = version.Build >= 22000 ? WindowsVersion.Windows11 : WindowsVersion.Windows10;

                _logger?.LogInfo("========================================");
                _logger?.LogInfo("[VisualEffectsManager] INICIALIZADO");
                _logger?.LogInfo($"[VisualEffectsManager] OS Version: {version.Major}.{version.Minor}.{version.Build}");
                _logger?.LogInfo($"[VisualEffectsManager] Windows Detectado: {_detectedVersion}");
                _logger?.LogInfo($"[VisualEffectsManager] APIs Disponíveis:");
                _logger?.LogInfo($"  - SetWindowCompositionAttribute: Sim");
                _logger?.LogInfo($"  - DwmSetWindowAttribute: Sim");
                _logger?.LogInfo($"  - DWMWA_WINDOW_CORNER_PREFERENCE: {_detectedVersion == WindowsVersion.Windows11}");
                _logger?.LogInfo($"  - DWMWA_SYSTEMBACKDROP_TYPE: {_detectedVersion == WindowsVersion.Windows11}");
                _logger?.LogInfo($"========================================");

                _isInitialized = true;
            }
            catch (Exception ex)
            {
                _logger?.LogError("[VisualEffectsManager] Erro na inicialização", ex);
                _detectedVersion = WindowsVersion.Windows10;
                _isInitialized = true;
            }
        }

        #endregion

        #region Public API

        /// <summary>
        /// Verdadeiro somente no Windows 11 (build 22000+). Use para Conditioning
        /// elementos de XAML que devem ter cantos arredondados apenas no Win11,
        /// mantendo o visual quadrado no Windows 10.
        /// </summary>
        public static bool IsWindows11 => _detectedVersion == WindowsVersion.Windows11;

        /// <summary>
        /// Raio de canto nativo a aplicar no Win11. No Win10 sempre 0 (quadrado).
        /// </summary>
        public static double NativeCornerRadius => IsWindows11 ? 16d : 0d;

        public static bool ShouldApplyRoundedCorners(bool isWindows11, bool roundedCornersEnabled)
        {
            if (!roundedCornersEnabled) return false;
            return isWindows11;
        }

        /// <summary>
        /// Aplica efeitos visuais em uma janela.
        /// Método principal que deve ser usado por todo o projeto.
        /// </summary>
        public static void ApplyVisualEffects(Window window, WindowType windowType = WindowType.MainWindow, BackdropType backdrop = BackdropType.Acrylic, bool roundedCorners = true)
        {
            if (!_isInitialized) Initialize();
            if (window == null)
            {
                _logger?.LogError("[VisualEffectsManager] ApplyVisualEffects: window é NULL - ABORTANDO");
                return;
            }

            if (window.WindowState == WindowState.Minimized)
            {
                _logger?.LogInfo("[VisualEffectsManager] ApplyVisualEffects: janela minimizada, agendando reaplicação quando restaurar (evita efeito parcial)");
                _acrylicActuallyApplied = false;
                ScheduleReapplyOnRestore(window, windowType, backdrop, roundedCorners);
                return;
            }

            IntPtr hWnd = GetWindowHandle(window);
            if (hWnd == IntPtr.Zero)
            {
                if (!window.IsLoaded)
                {
                    RoutedEventHandler? onLoaded = null;
                    onLoaded = (s, e) =>
                    {
                        window.Loaded -= onLoaded;
                        ApplyVisualEffects(window, windowType, backdrop, roundedCorners);
                    };
                    window.Loaded += onLoaded;
                }
                return;
            }

            try
            {
                // Re-validar transparência antes de aplicar (previne "acrylic fantasma" no startup)
                bool currentTransparencySetting = SettingsService.Instance?.Settings?.EnableTransparency ?? true;
                if (currentTransparencySetting != _transparencyEnabledInSettings)
                {
                    _logger?.LogWarning($"[VisualEffectsManager] Inconsistência de transparência detectada! Settings={currentTransparencySetting}, Interno={_transparencyEnabledInSettings}. Atualizando...");
                    _transparencyEnabledInSettings = currentTransparencySetting;
                }

                _logger?.LogInfo($"[VisualEffectsManager] ApplyVisualEffects INICIADO | Window={window.GetType().Name} | Backdrop={backdrop} | Rounded={roundedCorners}");

                _logger?.LogInfo($"[VisualEffectsManager] Handle obtido: 0x{hWnd:X8}");

                // 1. Aplicar bordas arredondadas (Windows 11 apenas)
                if (roundedCorners && _detectedVersion == WindowsVersion.Windows11)
                {
                    _logger?.LogInfo("[VisualEffectsManager] Aplicando bordas arredondadas (Windows 11)...");
                    ApplyRoundedCorners(hWnd, window, 16);
                    _logger?.LogInfo("[VisualEffectsManager] Rounded corners aplicados (16px)");
                }
                else if (_detectedVersion == WindowsVersion.Windows10)
                {
                    _logger?.LogInfo("[VisualEffectsManager] Windows 10: bordas quadradas (sem arredondamento)");
                }
                else
                {
                    _logger?.LogWarning($"[VisualEffectsManager] roundedCorners={roundedCorners}, Win11={_detectedVersion == WindowsVersion.Windows11} - PULANDO");
                }

                // 2. Aplicar backdrop (Acrylic/Mica/Blur)
                if (backdrop != BackdropType.None && _transparencyEnabledInSettings)
                {
                    _logger?.LogInfo($"[VisualEffectsManager] Aplicando backdrop {backdrop}...");
                    ApplyBackdrop(hWnd, window, backdrop, windowType);
                    _logger?.LogInfo($"[VisualEffectsManager] Backdrop {backdrop} aplicado");
                }
                else
                {
                    _logger?.LogInfo($"[VisualEffectsManager] Backdrop desativado (backdrop={backdrop}, transparencyEnabled={_transparencyEnabledInSettings})");
                }

                _logger?.LogInfo($"[VisualEffectsManager] ApplyVisualEffects COMPLETADO | Window={window.GetType().Name}");
                _logger?.LogInfo("========================================");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[VisualEffectsManager] ApplyVisualEffects FALHOU: {ex.Message}");
                _logger?.LogError($"[VisualEffectsManager] StackTrace: {ex.StackTrace}");
                _logger?.LogInfo("========================================");
            }
        }

        /// <summary>
        /// Atualiza os efeitos visuais quando há mudança de configuração.
        /// </summary>
        public static void UpdateVisualEffects(Window window, bool enableTransparency)
        {
            if (!_isInitialized) Initialize();
            if (window == null) return;

            try
            {
                _logger?.LogInfo($"[VisualEffectsManager] UpdateVisualEffects | Enable={enableTransparency} | Window={window.GetType().Name}");

                _transparencyEnabledInSettings = enableTransparency;

                if (enableTransparency)
                {
                    ApplyVisualEffects(window, WindowType.MainWindow, BackdropType.Acrylic, true);
                }
                else
                {
                    RemoveVisualEffects(window);
                }

                _logger?.LogInfo("[VisualEffectsManager] UpdateVisualEffects COMPLETADO");
            }
            catch (Exception ex)
            {
                _logger?.LogError("[VisualEffectsManager] UpdateVisualEffects FALHOU", ex);
            }
        }

        /// <summary>
        /// Remove todos os efeitos visuais de uma janela.
        /// </summary>
        public static void RemoveVisualEffects(Window window)
        {
            if (!_isInitialized) Initialize();
            if (window == null) return;

            try
            {
                _logger?.LogInfo($"[VisualEffectsManager] RemoveVisualEffects | Window={window.GetType().Name}");

                IntPtr hWnd = GetWindowHandle(window);
                if (hWnd == IntPtr.Zero) return;

                DisableAccent(hWnd);
                _acrylicActuallyApplied = false;

                if (_detectedVersion == WindowsVersion.Windows11)
                {
                    int preference = DWMWCP_DONROUND;
                    try
                    {
                        DwmSetWindowAttribute(hWnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
                        _logger?.LogInfo("[VisualEffectsManager] DWM corners desativados");
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogDebug($"[VisualEffectsManager] Erro ao remover corners: {ex.Message}");
                    }
                }

                _logger?.LogInfo("[VisualEffectsManager] RemoveVisualEffects COMPLETADO");
            }
            catch (Exception ex)
            {
                _logger?.LogError("[VisualEffectsManager] RemoveVisualEffects FALHOU", ex);
            }
        }

        /// <summary>
        /// Sincroniza o estado interno com o estado real do sistema.
        /// Retorna true se Acrylic estiver ativo.
        /// </summary>
        public static bool IsAcrylicEnabled()
        {
            return _acrylicEnabled && _acrylicActuallyApplied && _transparencyEnabledInSettings;
        }

        /// <summary>
        /// Define o estado interno do Acrylic (para sincronização com UI).
        /// </summary>
        public static void SetAcrylicState(bool enabled)
        {
            _acrylicEnabled = enabled;
            _logger?.LogInfo($"[VisualEffectsManager] Acrylic state atualizado: {enabled}");
        }

        #endregion

        #region Private Implementation

        /// <summary>
        /// Quando a janela está minimizada/oculta, agenda a aplicação dos efeitos
        /// para o momento em que ela for restaurada e estiver visível (após o render).
        /// Corrige o caso do startup minimizado, onde o Acrylic nunca era aplicado.
        /// </summary>
        private static void ScheduleReapplyOnRestore(Window window, WindowType windowType, BackdropType backdrop, bool roundedCorners)
        {
            if (window == null) return;

            if (!_pendingReapplyWindows.Add(window))
            {
                _logger?.LogDebug("[VisualEffectsManager] Reaplicação já agendada para esta janela, ignorando");
                return;
            }

            EventHandler? stateChanged = null;
            DependencyPropertyChangedEventHandler? visibleChanged = null;

            stateChanged = (s, e) =>
            {
                if (window.WindowState != WindowState.Minimized && window.IsVisible)
                {
                    window.StateChanged -= stateChanged;
                    window.IsVisibleChanged -= visibleChanged;
                    _pendingReapplyWindows.Remove(window);
                    _logger?.LogInfo("[VisualEffectsManager] Janela restaurada - reagendando efeitos visuais após render");
                    window.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            ApplyVisualEffects(window, windowType, backdrop, roundedCorners);
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError($"[VisualEffectsManager] Reaplicação após restore falhou: {ex.Message}");
                        }
                    }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                }
            };

            visibleChanged = (s, e) => stateChanged?.Invoke(window, EventArgs.Empty);

            window.StateChanged += stateChanged;
            window.IsVisibleChanged += visibleChanged;
        }

        private static IntPtr GetWindowHandle(Window window)
        {
            try
            {
                var helper = new WindowInteropHelper(window);
                IntPtr hWnd = helper.Handle;

                if (hWnd == IntPtr.Zero)
                {
                    _logger?.LogDebug("[VisualEffectsManager] Handle era Zero, tentando EnsureHandle...");
                    hWnd = helper.EnsureHandle();
                    _logger?.LogDebug($"[VisualEffectsManager] Handle após EnsureHandle: 0x{hWnd:X8}");
                }

                return hWnd;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[VisualEffectsManager] GetWindowHandle FALHOU: {ex.Message}", ex);
                return IntPtr.Zero;
            }
        }

        private static void ApplyRoundedCorners(IntPtr hWnd, Window window, double radius)
        {
            try
            {
                if (window.ActualWidth <= 1 || window.ActualHeight <= 1)
                {
                    _logger?.LogDebug("[VisualEffectsManager] ApplyRoundedCorners: tamanho inválido, ignorando");
                    return;
                }
                _logger?.LogDebug("[VisualEffectsManager] ApplyRoundedCorners INICIADO");
                
                if (_detectedVersion == WindowsVersion.Windows11)
                {
                    // Usa cantos arredondados nativos do Windows 11
                    int preference = DWMWCP_ROUND;
                    DwmSetWindowAttribute(hWnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
                    _logger?.LogDebug("[VisualEffectsManager] ApplyRoundedCorners: DWMWCP_ROUND aplicado.");
                }
                else
                {
                    // No Windows 10, o usuário exigiu que não existam bordas arredondadas por padrão.
                    _logger?.LogDebug("[VisualEffectsManager] ApplyRoundedCorners: Windows 10 detectado, mantendo bordas quadradas conforme solicitação.");
                }
                
                _logger?.LogDebug("[VisualEffectsManager] ApplyRoundedCorners COMPLETADO");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[VisualEffectsManager] ApplyRoundedCorners ERRO: {ex.Message}");
            }
        }

        private static void ApplyWindowRegionSafe(IntPtr hWnd, Window window, double radius)
        {
            try
            {
                double w = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
                double h = window.ActualHeight > 0 ? window.ActualHeight : window.Height;

                if (double.IsNaN(w) || double.IsNaN(h) || w <= 0 || h <= 0) return;

                double dpiX = 1.0, dpiY = 1.0;
                var ps = PresentationSource.FromVisual(window);
                if (ps?.CompositionTarget != null)
                {
                    dpiX = ps.CompositionTarget.TransformToDevice.M11;
                    dpiY = ps.CompositionTarget.TransformToDevice.M22;
                }

                int pw = (int)Math.Ceiling(w * dpiX);
                int ph = (int)Math.Ceiling(h * dpiY);

                // Raio da região: +2px lógicos para esconder serrilhado atrás do clip WPF
                double rgnRadius = radius + 2;
                int diamX = (int)Math.Round(rgnRadius * 2 * dpiX);
                int diamY = (int)Math.Round(rgnRadius * 2 * dpiY);

                IntPtr rgn = CreateRoundRectRgn(0, 0, pw + 1, ph + 1, diamX, diamY);
                if (rgn != IntPtr.Zero)
                {
                    SetWindowRgn(hWnd, rgn, true);
                    DeleteObject(rgn);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug($"[VisualEffectsManager] ApplyWindowRegionSafe erro: {ex.Message}");
            }
        }

        private static void ApplyWpfClip(Window window, double radius)
        {
            try
            {
                double w = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
                double h = window.ActualHeight > 0 ? window.ActualHeight : window.Height;

                if (w <= 0 || h <= 0) return;

                window.Clip = new RectangleGeometry(new Rect(0, 0, w, h), radius, radius);
                _logger?.LogDebug($"[VisualEffectsManager] WPF Clip aplicado: {w}x{h} radius={radius}");
            }
            catch (Exception ex)
            {
                _logger?.LogDebug($"[VisualEffectsManager] ApplyWpfClip erro: {ex.Message}");
            }
        }

        private static void ApplyBackdrop(IntPtr hWnd, Window window, BackdropType type, WindowType windowType)
        {
            try
            {
                if (type == BackdropType.None)
                {
                    DisableAccent(hWnd);
                    _acrylicActuallyApplied = false;
                    if (_detectedVersion == WindowsVersion.Windows11)
                        ApplyRoundedCorners(hWnd, window, 16);
                    return;
                }

                if (_detectedVersion == WindowsVersion.Windows11)
                {
                    int preference = DWMWCP_ROUND;
                    DwmSetWindowAttribute(hWnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
                    _logger?.LogDebug("[VisualEffectsManager] ApplyRoundedCorners: DWMWCP_ROUND aplicado.");
                }

                bool isLightTheme = SettingsService.Instance?.Settings?.Theme?.Equals("Light", StringComparison.OrdinalIgnoreCase) == true;
                
                // COR SÓLIDA PARA O ACRYLIC
                // Formato: 0xAARRGGBB
                int tintColor;
                if (isLightTheme)
                {
                    tintColor = unchecked((int)0x40FCF8F8); // Tema claro: branco com 25% alpha
                }
                else
                {
                    if (_detectedVersion == WindowsVersion.Windows10)
                        tintColor = unchecked((int)0x880A0A0F); // 55% alpha no Windows 10
                    else
                        tintColor = unchecked((int)0x550A0A0F); // 33% alpha no Windows 11
                }

                int accentState = ACCENT_ENABLE_ACRYLICBLURBEHIND;
                if (Environment.OSVersion.Version.Build < 17134)
                    accentState = ACCENT_ENABLE_BLURBEHIND;

                var accent = new AccentPolicy
                {
                    AccentState = accentState,
                    AccentFlags = 0x20 | 0x40 | 0x2,
                    GradientColor = tintColor,
                    AnimationId = 0
                };

bool accentOk = SetAccent(hWnd, accent);
                _acrylicActuallyApplied = accentOk;
                if (!accentOk)
                {
                    // Informação visível em produção (Debug é filtrado pelo LoggingService)
                    _logger?.LogWarning("[VisualEffectsManager] SetWindowCompositionAttribute retornou 0 (FALHOU) - o Acrylic pode não aparecer");
                }
                
                try
                {
                    var margins = new MARGINS
                    {
                        cxLeftWidth = -1,
                        cxRightWidth = -1,
                        cyTopHeight = -1,
                        cyBottomHeight = -1
                    };
                    DwmExtendFrameIntoClientArea(hWnd, ref margins);
                    _logger?.LogInfo("[VisualEffectsManager] DwmExtendFrameIntoClientArea aplicado");
                }
                catch (Exception extendEx)
                {
                    _logger?.LogWarning($"[VisualEffectsManager] DwmExtendFrameIntoClientArea falhou: {extendEx.Message}");
                }

                // Garantir background transparente no WPF para permitir que o Acrylic do DWM brilhe através da janela
                window.Background = new SolidColorBrush(Color.FromArgb(0, 255, 255, 255));
                window.Opacity = 1.0;
                
                _logger?.LogInfo($"[VisualEffectsManager] Backdrop {type} aplicado com sucesso.");
            }
            catch (Exception ex)
            {
                _logger?.LogDebug($"[VisualEffectsManager] ApplyBackdrop failed: {ex.Message}");
            }
        }

        private static bool SetAccent(IntPtr hWnd, AccentPolicy accent)
        {
            int accentSize = Marshal.SizeOf(accent);
            IntPtr accentPtr = Marshal.AllocHGlobal(accentSize);
            int result = 0;

            try
            {
                Marshal.StructureToPtr(accent, accentPtr, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WCA_ACCENT_POLICY,
                    Data = accentPtr,
                    SizeOfData = accentSize
                };

                result = SetWindowCompositionAttribute(hWnd, ref data);
                _logger?.LogDebug($"[VisualEffectsManager] SetWindowCompositionAttribute retornou: {result}");
            }
            finally
            {
                Marshal.FreeHGlobal(accentPtr);
            }

            return result != 0;
        }

        private static void DisableAccent(IntPtr hWnd)
        {
            var accent = new AccentPolicy { AccentState = ACCENT_DISABLED };
            SetAccent(hWnd, accent);
            _logger?.LogDebug("[VisualEffectsManager] Accent desativado");
        }

        #endregion
    }
}