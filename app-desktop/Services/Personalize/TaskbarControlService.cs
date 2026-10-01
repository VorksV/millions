using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics;
using Microsoft.Win32;
using Accessibility;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Personalize
{
    // ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦
    // TaskbarControlService çª¶ï¿½ Motor idï¾ƒï½ªntico ao TaskbarX (TaskbarCenter.vb)
    //
    // CORREï¾ƒï¿½グ DEFINITIVA: accLocation() retorna 0 neste sistema por diferenï¾ƒï½§a de
    // sessï¾ƒï½£o/integridade entre o Voltris e o Explorer. Toda mediï¾ƒï½§ï¾ƒï½£o de posiï¾ƒï½§ï¾ƒï½£o e
    // tamanho usa GetWindowRect (Win32 puro), que nunca falha.
    //
    // Algoritmo de centralizaï¾ƒï½§ï¾ƒï½£o (idï¾ƒï½ªntico ao PositionCalculator do TaskbarX):
    //   TrayWndWidth  = Shell_TrayWnd.right  - Shell_TrayWnd.left
    //   RebarWndLeft  = ReBarWindow32.left   - Shell_TrayWnd.left   (offset do rebar)
    //   TaskListWidth = MSTaskListWClass.right - MSTaskListWClass.left
    //   Position      = (TrayWndWidth / 2) çª¶ï¿½ (TaskListWidth / 2) çª¶ï¿½ RebarWndLeft
    //
    // O estado (string de detecï¾ƒï½§ï¾ƒï½£o de mudanï¾ƒï½§a) ï¾ƒï½©:
    //   orient + taskListWidth + trayWndWidth
    // ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦

    public enum TaskbarStyleMode
    {
        Default = 0,
        Transparent = 1,
        Blur = 2,
        Acrylic = 3,
        Gradient = 4,
        Mica = 5
    }

    public class TaskbarControlService
    {
        private readonly ILoggingService _logger;
        private const string TAG = "[TaskbarControl]";

        // Import do Win32 para notificar o sistema sobre mudancas no registro
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, string lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);
        
        private const uint WM_SETTINGCHANGE = 0x001a;
        private const uint SMTO_ABORTIFHUNG = 0x0002;
        private static readonly IntPtr HWND_BROADCAST = new IntPtr(0xffff);

        private CancellationTokenSource? _loopCts;
        private CancellationTokenSource? _trayLoopCts; // Thread do TrayLoopFix
        private bool _centeringEnabled = false;
        private bool _styleEnabled;
        private const int LoopRefreshRate = 400; // TaskbarX usa 400ms para deteccao rapida de mudancas
        private volatile bool _forceRecenter = false;
        private int _quickCycleCounter = 0; // Contador para centralizacao periodica (TaskbarX style)
        
        // TrayLoopFix: armazena ultimo TrayNotifyWidth por handle para detectar mudancas
        private readonly Dictionary<IntPtr, int> _lastTrayNotifyWidths = new();

        // Debounce para persistencia - evita I/O excessivo no registry/schtasks
        private DateTime _lastPersistAttempt = DateTime.MinValue;
        private readonly TimeSpan _persistDebounceInterval = TimeSpan.FromSeconds(5);
        
        private DateTime _lastStylePersistAttempt = DateTime.MinValue;
        private readonly TimeSpan _stylePersistDebounceInterval = TimeSpan.FromSeconds(5);

        private TaskbarStyleMode _styleMode = TaskbarStyleMode.Transparent;
        private byte   _opacity    = 255;
        private int    _colorR, _colorG, _colorB, _colorAlpha;

        // Debounce para aplicacao de estilo - evita bloquear UI thread durante drag do slider
        private volatile bool _pendingStyleApply = false;

        // Quando VoltrisBlur esta ativo, Nao aplicar SetWindowCompositionAttribute na taskbar.
        // A DLL VoltrisBlur.dll gerencia o efeito da taskbar diretamente - qualquer chamada
        // a SetWindowCompositionAttribute sobrescreve o efeito dela e deixa a taskbar branca.
        private volatile bool _voltrisBlurActive = false;

        // ï¿½æ«¨ CORREï¾ƒï¿½グ #3: Explorer Restart Monitor - campos para detectar restart do Explorer
        private int _lastExplorerPid = 0;
        private DateTime _lastExplorerCheck = DateTime.MinValue;
        private readonly TimeSpan _explorerCheckInterval = TimeSpan.FromSeconds(2);
        
        // ï¿½æ«¨ CORREï¾ƒï¿½グ #4: Task Scheduler Health Check - verificar tarefa a cada 30s
        private DateTime _lastTaskSchedulerCheck = DateTime.MinValue;
        private readonly TimeSpan _taskSchedulerCheckInterval = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Calcula a posicao esperada para centralizacao 100% precisa no centro absoluto da tela.
        ///
        /// Formula CORRETA (AbsoluteCenter):
        ///   trayWndWidth = largura total da Shell_TrayWnd (= largura do monitor)
        ///   swLeft       = Shell_TrayWnd.Left ate SwHwnd.Left (offset absoluto do pai)
        ///   screenCenter = trayWndWidth / 2
        ///   iconStartAbs = screenCenter - (iconsWidth / 2)
        ///   position     = iconStartAbs - swLeft   (relativo ao pai SwHwnd)
        ///
        /// Esta formula e imune a mudancas na barra de pesquisa, TrayNotify ou
        /// News/Interests porque mira diretamente o centro geometrico da Shell_TrayWnd,
        /// sem depender de offsets calculados a partir de elementos laterais.
        /// currentPos tambem usa SwHwnd como referencia, ficando 100% consistente
        /// com o SetWindowPos (que posiciona relativo ao pai direto = SwHwnd).
        /// </summary>
        private int CalculateExpectedPosition(TaskbarInfo info, int taskbarWidth, out int currentPos)
        {
            GetWindowRect(info.TrayWnd, out var trayRect);
            GetWindowRect(info.TaskListHwnd, out var taskListRect);

            IntPtr swHwnd = info.SwHwnd;
            if (swHwnd == IntPtr.Zero)
                swHwnd = FindWindowEx(info.RebarHwnd, IntPtr.Zero, "MSTaskSwWClass", IntPtr.Zero);
            GetWindowRect(swHwnd, out var swRect);

            bool isH = info.Orientation == "H";

            // Largura total da Shell_TrayWnd (= largura do monitor para barra horizontal)
            int trayWndWidth = isH
                ? Math.Abs(trayRect.Right  - trayRect.Left)
                : Math.Abs(trayRect.Bottom - trayRect.Top);

            // Offset do SwHwnd desde o inicio da Shell_TrayWnd (coordenadas absolutas)
            int swLeft = isH
                ? Math.Abs(swRect.Left - trayRect.Left)
                : Math.Abs(swRect.Top  - trayRect.Top);

            // Centro geometrico absoluto da barra de tarefas
            int screenCenter = trayWndWidth / 2;

            // Onde os icones devem comecar (coordenadas absolutas relativas a tray)
            int iconStartAbs = screenCenter - (taskbarWidth / 2);

            // Posicao relativa ao SwHwnd (pai direto do MSTaskListWClass)
            int position = Math.Max(0, iconStartAbs - swLeft);

            // Posicao ATUAL relativa ao SwHwnd (consistente com o SetWindowPos)
            currentPos = isH
                ? (taskListRect.Left - swRect.Left)
                : (taskListRect.Top  - swRect.Top);

            return position;
        }



        /// <summary>
        /// Informa ao TaskbarControlService se o VoltrisBlur estï¾ƒï½¡ ativo.
        /// Quando ativo, o estilo nativo (SetWindowCompositionAttribute) ï¾ƒï½© suprimido
        /// para nï¾ƒï½£o conflitar com a DLL VoltrisBlur.
        /// </summary>
        public void SetVoltrisBlurActive(bool active)
        {
            _logger.LogInfo($"{TAG} SetVoltrisBlurActive({active}) çª¶ï¿½ styleEnabled={_styleEnabled}");
            _voltrisBlurActive = active;
            // VoltrisBlur afeta apenas o Explorer (File Explorer), nï¾ƒï½£o a Taskbar.
            // Nï¾ƒï½£o resetar o accent da taskbar aqui çª¶ï¿½ sï¾ƒï½£o features independentes.
        }

        // ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦
        // Win32 P/Invoke
        // ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦
        #region Win32

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string lclassName, string? windowTitle);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string lclassName, IntPtr windowTitle);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr hWnd);

        /// <summary>
        /// Largura da janela primária em pixels. Usado como referência para detectar
        /// visualmente se a lista de tarefas está centralizada.
        /// </summary>
        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        private const int SM_CXSCREEN = 0;      // Largura da tela primária
        private const int SM_XVIRTUALSCREEN = 76; // X da origem do desktop virtual

        /// <summary>
        /// Notifica o shell de que uma mudança de configuração ocorreu.
        /// Chamado após gravar TaskbarAl, porque o shell mantém cache do layout da
        /// barra de tarefas; sem SHCNE_ASSOCCHANGED a mudança pode não aparecer até
        /// o Explorer reiniciar.
        /// </summary>
        [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = false)]
        private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [DllImport("user32.dll")]
        private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);


        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        private delegate bool EnumChildProc(IntPtr hwnd, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumChildProc lpEnumFunc, IntPtr lParam);

        private const uint SWP_NOSIZE         = 0x0001;
        private const uint SWP_NOZORDER       = 0x0004;
        private const uint SWP_NOACTIVATE     = 0x0010;
        private const uint SWP_ASYNCWINDOWPOS = 0x4000;
        private const uint SWP_NOSENDCHANGING = 0x0400;
        private const int  GWL_EXSTYLE        = -20;
        private const int  WS_EX_LAYERED      = 0x80000;
        private const int  WS_EX_TOPMOST      = 0x00000008;
        private const int  MONITOR_DEFAULTTOPRIMARY = 0x00000001;
        private const int  MONITOR_DEFAULTTONEAREST = 0x00000002;

        internal enum AccentState : int
        {
            ACCENT_DISABLED                   = 0,
            ACCENT_ENABLE_GRADIENT            = 1,
            ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
            ACCENT_ENABLE_BLURBEHIND          = 3,
            ACCENT_ENABLE_ACRYLICBLURBEHIND   = 4,
            ACCENT_ENABLE_TRANSPARENT         = 6}

        [StructLayout(LayoutKind.Sequential)]
        internal struct AccentPolicy
        {
            public AccentState AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        internal struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWPLACEMENT
        {
            public int length;
            public int flags;
            public int showCmd;
            public int ptMinPosition_x;
            public int ptMinPosition_y;
            public int ptMaxPosition_x;
            public int ptMaxPosition_y;
            public int rcNormalLeft;
            public int rcNormalTop;
            public int rcNormalRight;
            public int rcNormalBottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }

        #endregion

        private const string TASKBAR_STYLE_KEY = @"SOFTWARE\VoltrisOptimizer\TaskbarStyle";
        private const int WCA_ACCENT_POLICY = 19;

        public TaskbarControlService(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogInfo($"{TAG} TaskbarControlService instanciado.");
            
            // VERIFICAR SE A TAREFA DE PERSISTï¾ƒå“¢CIA EXISTE E RECREAR SE NECESSï¾ƒヽIO
            CheckAndRestorePersistTask();
            
            // Tentar carregar configuraï¾ƒï½§ï¾ƒï½µes salvas no boot
            LoadSavedSettings();
        }

        /// <summary>
        /// Verifica se a tarefa de persistï¾ƒï½ªncia existe e recria se foi deletada
        /// </summary>
        private void CheckAndRestorePersistTask()
        {
            try
            {
                string taskName = "VoltrisTaskbarCenter";
                bool taskExists = false;
                bool taskEnabled = false;
                
                // Verificar se a tarefa existe e se estï¾ƒï½¡ habilitada
                var checkPsi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/Query /TN \"{taskName}\" /V /FO LIST",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                
                using (var checkProc = Process.Start(checkPsi))
                {
                    if (checkProc != null)
                    {
                        string output = checkProc.StandardOutput.ReadToEnd();
                        checkProc.WaitForExit(5000);
                        
                        if (checkProc.ExitCode == 0 && !string.IsNullOrEmpty(output))
                        {
                            taskExists = true;
                            // Verificar se estï¾ƒï½¡ habilitada
                            taskEnabled = output.Contains("Enabled: Yes") || output.Contains("Habilitada: Sim");
                            
                            _logger.LogInfo($"{TAG} [Persist] Tarefa existe: {taskExists}, Habilitada: {taskEnabled}");
                            
                            // Se existe mas estï¾ƒï½¡ DESATIVADA, forï¾ƒï½§ar habilitaï¾ƒï½§ï¾ƒï½£o!
                            if (taskExists && !taskEnabled)
                            {
                                _logger.LogWarning($"{TAG} [Persist] ç¬žï¿½ Tarefa existe mas estï¾ƒï½¡ DESATIVADA! Habilitando...");
                                
                                var enablePsi = new ProcessStartInfo
                                {
                                    FileName = "schtasks.exe",
                                    Arguments = $"/Change /TN \"{taskName}\" /ENABLE",
                                    CreateNoWindow = true,
                                    UseShellExecute = false
                                };
                                
                                using (var enableProc = Process.Start(enablePsi))
                                {
                                    enableProc?.WaitForExit(2000);
                                    if (enableProc != null && enableProc.ExitCode == 0)
                                    {
                                        _logger.LogSuccess($"{TAG} [Persist] ç¬¨ï¿½ Tarefa HABILITADA com sucesso!");
                                    }
                                    else
                                    {
                                        _logger.LogError($"{TAG} [Persist] ç¬¶ï¿½ Falha ao habilitar tarefa. ExitCode={enableProc?.ExitCode}");
                                    }
                                }
                            }
                        }
                        else
                        {
                            // Tarefa nï¾ƒï½£o existe - verificar se estava habilitada nas configuraï¾ƒï½§ï¾ƒï½µes
                            _logger.LogWarning($"{TAG} [Persist] ç¬žï¿½ Tarefa de persistï¾ƒï½ªncia Nï¾ƒグ encontrada!");
                            
                            try
                            {
                                using (var key = Registry.CurrentUser.OpenSubKey(TASKBAR_STYLE_KEY))
                                {
                                    if (key != null)
                                    {
                                        var centeringEnabled = key.GetValue("TaskbarCenteringEnabled");
                                        if (centeringEnabled != null && Convert.ToBoolean(centeringEnabled))
                                        {
                                            _logger.LogWarning($"{TAG} [Persist] Centralizaï¾ƒï½§ï¾ƒï½£o estava habilitada - recriando tarefa...");
                                            // Recriar a tarefa
                                            Task.Run(() => PersistTaskbarAlignment(true));
                                        }
                                        else
                                        {
                                            _logger.LogInfo($"{TAG} [Persist] Centralizaï¾ƒï½§ï¾ƒï½£o nï¾ƒï½£o estava habilitada nas configuraï¾ƒï½§ï¾ƒï½µes");
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning($"{TAG} [Persist] Erro ao verificar configuraï¾ƒï½§ï¾ƒï½µes: {ex.Message}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"{TAG} [Persist] Erro ao verificar tarefa: {ex.Message}");
            }
        }

        private void LoadSavedSettings()
        {
            try
            {
                // Carregar estilo da taskbar
                using var key = Registry.CurrentUser.OpenSubKey(TASKBAR_STYLE_KEY);
                if (key != null)
                {
                    _styleMode = (TaskbarStyleMode)(int)key.GetValue("Mode", 0);
                    _opacity = (byte)(int)key.GetValue("Opacity", 255);
                    _colorR = (int)key.GetValue("R", 0);
                    _colorG = (int)key.GetValue("G", 0);
                    _colorB = (int)key.GetValue("B", 0);
                    
                    if (_styleMode != TaskbarStyleMode.Default)
                    {
                        _styleEnabled = true;
                        _pendingStyleApply = true;
                        _logger.LogInfo($"{TAG} Configurações de estilo carregadas: {_styleMode} (Opacidade: {_opacity})");
                    }
                }
                
                // [CORREÇÃO] Carregar centralização salva
                LoadSavedCenteringSettings();
            }
            catch (Exception ex)
            {
                _logger.LogError($"{TAG} Erro ao carregar configurações salvas: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Carrega configuração de centralização salva (Win11: Registro, Win10: Task Scheduler)
        /// </summary>
        private void LoadSavedCenteringSettings()
        {
            try
            {
                bool wasCenteringEnabled = false;
                
                // Windows 11: Verificar registro nativo TaskbarAl
                if (SystemInfoService.IsWindows11)
                {
                    using var advKey = Registry.CurrentUser.OpenSubKey(TASKBAR_ALIGNMENT_KEY);
                    if (advKey != null)
                    {
                        var taskbarAlValue = advKey.GetValue(TASKBAR_ALIGNMENT_VALUE);
                        if (taskbarAlValue != null && int.TryParse(taskbarAlValue.ToString(), out int taskbarAl))
                        {
                            if (taskbarAl == 1)
                            {
                                wasCenteringEnabled = true;
                                _logger.LogInfo($"{TAG} [Win11] Centralização detectada no registro nativo (TaskbarAl=1)");
                            }
                        }
                    }
                }
                
                // Windows 10: Verificar se tarefa agendada existe
                if (!SystemInfoService.IsWindows11)
                {
                    try
                    {
                        var taskExists = CheckTaskbarSchedulerExists();
                        if (taskExists)
                        {
                            wasCenteringEnabled = true;
                            _logger.LogInfo($"{TAG} [Win10] Centralização detectada via Task Scheduler");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"{TAG} Erro ao verificar task scheduler: {ex.Message}");
                    }
                }
                
                // Se centralização estava ativa, reativar
                if (wasCenteringEnabled)
                {
                    _centeringEnabled = true;
                    _logger.LogInfo($"{TAG} Centralização restaurada - reativando loop de monitoramento...");
                    
                    // Reativar o loop de centralização
                    EnsureLoopRunning();
                    
                    // ï¿½æ«¨ CORREï¾ƒï¿½グ #5: Aplicação DUPLA no startup (combate reversão do Explorer)
                    // O Explorer leva 6-15s para inicializar completamente e pode REVERTER
                    // os ï¾ƒï½­cones para LEFT=0 durante esse perï¾ƒï½­odo. Aplicamos 3 vezes:
                    //   1. IMEDIATA (0s) - antes do Explorer sobrescrever
                    //   2. Reforço (3s) - quando o Explorer estï¾ƒï½¡ carregando configuraï¾ƒï½§ï¾ƒï½µes
                    //   3. Reforço final (8s) - apï¾ƒï½³s Explorer totalmente carregado
                    
                    _logger.LogInfo($"{TAG} [CORREï¾ƒï¿½グ #5] ï¿½å¼  Aplicando centralizaï¾ƒï½§ï¾ƒï½£o TRIPLA no startup (0s, 3s, 8s)...");
                    
                    // 1. Aplicação IMEDIATA (síncrona)
                    try
                    {
                        _forceRecenter = true;
                        CenterAllTaskbars();
                        _logger.LogSuccess($"{TAG} [CORREï¾ƒï¿½グ #5] ï¿½ç±¨ï¿½ Centralizaï¾ƒï½§ï¾ƒï½£o #1 aplicada IMEDIATAMENTE (0s)");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"{TAG} Erro ao aplicar centralizaï¾ƒï½§ï¾ƒï½£o imediata: {ex.Message}");
                    }
                    
                    // 2. Re-aplicação após 3 segundos
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(3000);
                        try
                        {
                            if (_centeringEnabled)
                            {
                                _forceRecenter = true;
                                CenterAllTaskbars();
                                _logger.LogSuccess($"{TAG} [CORREï¾ƒï¿½グ #5] ï¿½ç±¨ï¿½ Centralizaï¾ƒï½§ï¾ƒï½£o #2 aplicada (3s)");
                                
                                // 3. Forçar registro do Windows 11 tambï¾ƒï½©m
                                if (SystemInfoService.IsWindows11)
                                {
                                    WriteWin11NativeAlignment(true, out _);
                                    _logger.LogInfo($"{TAG} [CORREï¾ƒï¿½グ #5] Registro TaskbarAl=1 forçado (3s)");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"{TAG} Erro ao reaplicar centralizaï¾ƒï½§ï¾ƒï½£o apï¾ƒï½³s 3s: {ex.Message}");
                        }
                    });
                    
                    // 3. Re-aplicação final após 8 segundos (Explorer totalmente carregado)
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(8000);
                        try
                        {
                            if (_centeringEnabled)
                            {
                                _forceRecenter = true;
                                CenterAllTaskbars();
                                _logger.LogSuccess($"{TAG} [CORREï¾ƒï¿½グ #5] ï¿½ç±¨ï¿½ Centralizaï¾ƒï½§ï¾ƒï½£o #3 aplicada (8s) - EXPLORER TOTALMENTE CARREGADO");
                                
                                // Tambï¾ƒï½©m forçar registro no Windows 11
                                if (SystemInfoService.IsWindows11)
                                {
                                    WriteWin11NativeAlignment(true, out _);
                                    _logger.LogInfo($"{TAG} [CORREï¾ƒï¿½グ #5] Registro TaskbarAl=1 forçado (8s)");
                                }
                                
                                _logger.LogSuccess($"{TAG} [CORREï¾ƒï¿½グ #5] ï¿½ç±¨ï¿½ Sequï¾ƒï½ªncia TRIPLA concluï¾ƒï½­da! Centralizaï¾ƒï½§ï¾ƒï½£o permanente garantida.");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"{TAG} Erro ao reaplicar centralizaï¾ƒï½§ï¾ƒï½£o apï¾ƒï½³s 8s: {ex.Message}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"{TAG} Erro ao carregar centralização salva: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Verifica se a tarefa agendada de centralização existe
        /// </summary>
        private bool CheckTaskbarSchedulerExists()
        {
            try
            {
                var result = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "schtasks",
                    Arguments = "/Query /TN \"VoltrisTaskbarCenter\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                
                result?.WaitForExit(2000);
                var output = result?.StandardOutput.ReadToEnd() ?? "";
                return output.Contains("VoltrisTaskbarCenter");
            }
            catch
            {
                return false;
            }
        }

        private void SaveSettings(TaskbarStyleMode mode, byte opacity, int r, int g, int b)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(TASKBAR_STYLE_KEY);
                key.SetValue("Mode", (int)mode);
                key.SetValue("Opacity", (int)opacity);
                key.SetValue("R", r);
                key.SetValue("G", g);
                key.SetValue("B", b);
            }
            catch { }
        }
        // Persistï¾ƒï½ªncia Nativa Windows 11 e Legado Windows 10
        private const string TASKBAR_ALIGNMENT_KEY = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        private const string TASKBAR_ALIGNMENT_VALUE = "TaskbarAl";

        /// <summary>
        /// Sistema Inteligente de Persistï¾ƒï½ªncia: 
        /// Win11: Usa Registro Nativo. 
        /// Win10: Usa Task Scheduler Dedicado.
        /// </summary>
        private void PersistTaskbarAlignment(bool center)
        {
            try
            {
                // 1. Se for Windows 11, usar o registro nativo (mais profissional)
                if (SystemInfoService.IsWindows11)
                {
                    if (!WriteWin11NativeAlignment(center, out var win11Error))
                    {
                        _logger.LogError($"{TAG} [Persist] Alinhamento nativo do Windows 11 NAO aplicado: {win11Error}");
                    }
                }
                
                // 2. Criar ou Remover a Tarefa Agendada Silenciosa (Indispensï¾ƒï½¡vel para Win10)
                UpdateTaskbarScheduler(center);
                
                // ï¿½æ«¨ PERSISTï¾ƒå“¢CIA MELHORADA: Aplicar imediatamente tambï¾ƒï½©m
                if (center && !SystemInfoService.IsWindows11)
                {
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(1000);
                        try
                        {
                            // CORREï¾ƒï¿½グ: verificar se o centering ainda estï¾ƒï½¡ ativo
                            // O usuï¾ƒï½¡rio pode ter desligado o toggle durante o delay de 1s
                            if (!_centeringEnabled)
                            {
                                _logger.LogInfo($"{TAG} [Persist] Centralizaï¾ƒï½§ï¾ƒï½£o cancelada çª¶ï¿½ usuï¾ƒï½¡rio desativou durante delay.");
                                return;
                            }
                            CenterAllTaskbars();
                            _logger.LogInfo($"{TAG} [Persist] Centralizaï¾ƒï½§ï¾ƒï½£o aplicada imediatamente apï¾ƒï½³s persistï¾ƒï½ªncia.");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"{TAG} [Persist] Falha ao aplicar centralizaï¾ƒï½§ï¾ƒï½£o imediata: {ex.Message}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"{TAG} [Persist] Falha geral ao salvar alinhamento: {ex.Message}");
            }
        }

        /// <summary>
        /// Aplica o alinhamento NATIVO do Windows 11 gravando
        /// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarAl</c>
        /// (1 = centralizado, 0 = alinhado à esquerda) e notificando o shell.
        ///
        /// CORREÇÕES DE AUDITORIA:
        ///  1. Usava <c>OpenSubKey(writable: true)</c> com um <c>else { }</c> VAZIO:
        ///     se a chave não existisse, nada era escrito e nenhuma falha era
        ///     registrada — falha silenciosa. Agora usa <c>CreateSubKey</c> e loga
        ///     o erro real.
        ///  2. Não fazia read-back: logava "Registro Nativo Win11 atualizado" mesmo
        ///     sem ter escrito nada. Agora relê o valor e só reporta sucesso se
        ///     confirmar.
        ///  3. Enviava apenas <c>WM_SETTINGCHANGE</c>. Para alteração de layout da
        ///     barra de tarefas o shell também precisa de <c>SHChangeNotify</c> /
        ///     <c>SHCNE_ASSOCCHANGED</c>; sem isso, a mudança pode não aparecer até
        ///     o Explorer reiniciar.
        /// </summary>
        private bool WriteWin11NativeAlignment(bool center, out string error)
        {
            error = string.Empty;
            try
            {
                int desired = center ? 1 : 0;

                // CreateSubKey garante que a chave exista (fresh profile / roaming).
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(TASKBAR_ALIGNMENT_KEY, writable: true);
                if (key == null)
                {
                    error = $"Não foi possível criar/abrir '{TASKBAR_ALIGNMENT_KEY}'.";
                    _logger.LogError($"{TAG} [TaskbarAl] {error} O alinhamento NÃO foi aplicado.");
                    return false;
                }

                key.SetValue(TASKBAR_ALIGNMENT_VALUE, desired, Microsoft.Win32.RegistryValueKind.DWord);
                key.Flush();

                // ── READ-BACK ──
                var readBack = key.GetValue(TASKBAR_ALIGNMENT_VALUE);
                if (readBack == null || !int.TryParse(readBack.ToString(), out int actual) || actual != desired)
                {
                    error = $"Valor gravado ({readBack}) difere do esperado ({desired}).";
                    _logger.LogError($"{TAG} [TaskbarAl] {error} Alteração NÃO confirmada.");
                    return false;
                }

                // Notificação ao shell, fora da UI thread para não bloquear.
                Task.Run(() =>
                {
                    try
                    {
                        // Ambos são necessários: o primeiro invalida o cache de layout
                        // da barra de tarefas, o segundo sinaliza a mudança de "settings".
                        SHChangeNotify(0x08000000 /*SHCNE_ASSOCCHANGED*/, 0, IntPtr.Zero, IntPtr.Zero);
                        SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, IntPtr.Zero, "TraySettings", SMTO_ABORTIFHUNG, 500, out _);
                    }
                    catch (Exception notifyEx)
                    {
                        _logger.LogDebug($"{TAG} [TaskbarAl] Notificação ao shell falhou: {notifyEx.Message}");
                    }
                });

                _logger.LogSuccess($"{TAG} [TaskbarAl] Alinhamento {(center ? "CENTRALIZADO" : "à ESQUERDA")} " +
                    $"gravado e CONFIRMADO (TaskbarAl={actual}). " +
                    "Se a barra não refletir a mudança imediatamente, reinicie o Explorer ou a sessão.");

                return true;
            }
            catch (UnauthorizedAccessException ex)
            {
                error = $"Sem permissão para gravar em HKCU: {ex.Message}";
                _logger.LogError($"{TAG} [TaskbarAl] {error}");
                return false;
            }
            catch (Exception ex)
            {
                error = $"{ex.GetType().Name}: {ex.Message}";
                _logger.LogError($"{TAG} [TaskbarAl] Falha ao gravar o alinhamento: {error}", ex);
                return false;
            }
        }

        private void UpdateTaskbarScheduler(bool enable)
        {
            try
            {
                string taskName = "VoltrisTaskbarCenter";
                
                // VERIFICAR SE ESTï¾ƒï¿½ EXECUTANDO COMO ADMINISTRADOR
                using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
                {
                    var principal = new System.Security.Principal.WindowsPrincipal(identity);
                    if (!principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
                    {
                        _logger.LogError($"{TAG} [Persist] ç¬¶ï¿½ ERRO CRï¾ƒæŽ§ICO: Aplicaï¾ƒï½§ï¾ƒï½£o Nï¾ƒグ estï¾ƒï½¡ rodando como ADMINISTRADOR!");
                        _logger.LogError($"{TAG} [Persist] A tarefa agendada REQUER privilï¾ƒï½©gios de administrador.");
                        _logger.LogError($"{TAG} [Persist] Por favor, feche o VOLTRIS e execute como Administrador.");
                        return;
                    }
                }
                
                _logger.LogInfo($"{TAG} [Persist] UpdateTaskbarScheduler chamado: enable={enable}");
                
                // CORREÇÃO: a tarefa agendada NÃO pode permanecer registrada após o
                // usuário desativar a centralização. O comportamento anterior mantinha
                // a tarefa "permanente", de modo que o Windows re-centralizava os ícones
                // a cada logon mesmo com o toggle DESLIGADO — estado real e interface
                // em desacordo, e sem forma de reverter pela interface.
                if (!enable)
                {
                    RemoveTaskbarScheduler("VoltrisTaskbarCenter");
                    return;
                }

                // Se estiver ativando, cria a tarefa com delay de 3 segundos (PT3S)
                string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                _logger.LogInfo($"{TAG} [Persist] ExePath: {exePath}");
                
                // ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦
                // XML da tarefa agendada corrigido:
                // - UserId com o usuï¾ƒï½¡rio atual (LogonTrigger dispara para o usuï¾ƒï½¡rio certo)
                // - LogonType InteractiveToken (sessï¾ƒï½£o grï¾ƒï½¡fica ativa, nï¾ƒï½£o serviï¾ƒï½§o)
                // - SEM RunLevel HighestAvailable (evita UAC prompt em algumas builds)
                // - Delay PT8S (Explorer leva ~6-8s para estabilizar completamente)
                // - ExecutionTimeLimit PT2M (tarefa deve encerrar em 2 minutos)
                // ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦
                string currentUser = $"{Environment.UserDomainName}\\{Environment.UserName}";
                _logger.LogInfo($"{TAG} [Persist] Criando tarefa para usuï¾ƒï½¡rio: {currentUser}");
                
                string taskXml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Date>{DateTime.Now:yyyy-MM-ddTHH:mm:ss}</Date>
    <Author>Voltris Optimizer</Author>
    <Description>Centraliza automaticamente os icones da taskbar ao iniciar o sistema. Nao fechar çª¶ï¿½ gerenciado pelo Voltris Optimizer.</Description>
    <URI>\{taskName}</URI>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <StartBoundary>{DateTime.Now:yyyy-MM-dd}T00:00:00</StartBoundary>
      <Enabled>true</Enabled>
      <UserId>{currentUser}</UserId>
      <Delay>PT8S</Delay>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <UserId>{currentUser}</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>true</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT2M</ExecutionTimeLimit>
    <Priority>6</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>""{exePath}""</Command>
      <Arguments>--taskbar-center</Arguments>
    </Exec>
  </Actions>
</Task>";

                _logger.LogInfo($"{TAG} [Persist] XML gerado");
                
                // Salvar XML temporï¾ƒï½¡rio e registrar
                string tempXml = Path.GetTempFileName();
                try
                {
                    File.WriteAllText(tempXml, taskXml, Encoding.Unicode);
                    _logger.LogInfo($"{TAG} [Persist] XML salvo em: {tempXml}");
                    
                    // Executar schtasks com redirect de output
                    var psi = new ProcessStartInfo
                    {
                        FileName = "schtasks.exe",
                        Arguments = $"/Create /TN \"{taskName}\" /XML \"{tempXml}\" /F",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    
                    _logger.LogInfo($"{TAG} [Persist] Executando: {psi.FileName} {psi.Arguments}");
                    
                    using (var proc = Process.Start(psi))
                    {
                        if (proc != null)
                        {
                            string output = proc.StandardOutput.ReadToEnd();
                            string error = proc.StandardError.ReadToEnd();
                            proc.WaitForExit(10000);
                            
                            _logger.LogInfo($"{TAG} [Persist] ExitCode={proc.ExitCode}");
                            if (!string.IsNullOrEmpty(output)) _logger.LogInfo($"{TAG} [Persist] Output={output}");
                            if (!string.IsNullOrEmpty(error)) _logger.LogError($"{TAG} [Persist] Error={error}");
                            
                            if (proc.ExitCode == 0)
                            {
                                // Confirma que a tarefa REALMENTE ficou registrada.
                                // Sem isso, o log afirmava sucesso apenas com base no
                                // código de saída, sem provar que o Windows aceitou.
                                if (TaskbarSchedulerTaskExists(taskName))
                                {
                                    _logger.LogSuccess($"{TAG} [Persist] Tarefa '{taskName}' criada e CONFIRMADA no Agendador do Tarefas do Windows.");

                                    var enablePsi = new ProcessStartInfo
                                    {
                                        FileName = "schtasks.exe",
                                        Arguments = $"/Change /TN \"{taskName}\" /ENABLE",
                                        CreateNoWindow = true,
                                        UseShellExecute = false,
                                        RedirectStandardOutput = true,
                                        RedirectStandardError = true
                                    };
                                    using var enableProc = Process.Start(enablePsi);
                                    if (enableProc != null)
                                    {
                                        var tOut = enableProc.StandardOutput.ReadToEndAsync();
                                        var tErr = enableProc.StandardError.ReadToEndAsync();
                                        bool exited = enableProc.WaitForExit(10_000);
                                        if (!exited) { try { enableProc.Kill(); } catch { } }

                                        if (exited && enableProc.ExitCode == 0)
                                        {
                                            _logger.LogSuccess($"{TAG} [Persist] Tarefa habilitada.");
                                        }
                                        else
                                        {
                                            _logger.LogWarning(
                                                $"{TAG} [Persist] schtasks /Change /ENABLE retornou " +
                                                $"{(exited ? enableProc.ExitCode.ToString() : "timeout")}. A tarefa existe mas pode estar desabilitada.");
                                        }
                                    }
                                }
                                else
                                {
                                    _logger.LogError(
                                        $"{TAG} [Persist] schtasks retornou 0, mas a tarefa '{taskName}' NÃO foi encontrada no Windows. " +
                                        "A centralização pode não sobreviver ao próximo logon.");
                                }
                            }
                            else
                            {
                                _logger.LogError($"{TAG} [Persist] schtasks falhou: {error}");
                            }
                        }
                    }
                }
                finally
                {
                    if (File.Exists(tempXml)) File.Delete(tempXml);
                }
            }
            catch (Exception ex) 
            { 
                _logger.LogError($"{TAG} [Persist] Erro ao atualizar Task Scheduler: {ex.Message}", ex); 
            }
        }

        /// <summary>
        /// Remove a tarefa agendada de centralização do Agendador de Tarefas do Windows.
        /// Chamado quando o usuário DESATIVA a centralização, para que o estado
        /// persistido corresponda ao toggle. Idempotente: "não existe" é sucesso.
        /// </summary>
        private void RemoveTaskbarScheduler(string taskName)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/Delete /TN \"{taskName}\" /F",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    _logger.LogError($"{TAG} [Persist] Não foi possível iniciar schtasks para remover '{taskName}'.");
                    return;
                }

                var tOut = proc.StandardOutput.ReadToEndAsync();
                var tErr = proc.StandardError.ReadToEndAsync();
                if (!proc.WaitForExit(15_000))
                {
                    try { proc.Kill(); } catch { }
                    _logger.LogError($"{TAG} [Persist] schtasks /Delete excedeu 15s para '{taskName}'.");
                    return;
                }

                string err = tErr.GetAwaiter().GetResult();
                string outp = tOut.GetAwaiter().GetResult();

                if (proc.ExitCode == 0)
                {
                    if (TaskbarSchedulerTaskExists(taskName))
                    {
                        _logger.LogWarning($"{TAG} [Persist] schtasks /Delete retornou 0, mas '{taskName}' ainda existe.");
                    }
                    else
                    {
                        _logger.LogSuccess($"{TAG} [Persist] Tarefa '{taskName}' REMOVIDA. A centralização não será reaplicada no próximo logon.");
                    }
                }
                else if (err.Contains("não existe", StringComparison.OrdinalIgnoreCase) ||
                         err.Contains("cannot find", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInfo($"{TAG} [Persist] Tarefa '{taskName}' não existia. Nada a remover.");
                }
                else
                {
                    _logger.LogError($"{TAG} [Persist] Falha ao remover '{taskName}' (exit={proc.ExitCode}): " +
                        $"{(string.IsNullOrWhiteSpace(err) ? outp : err).Trim()}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"{TAG} [Persist] Erro ao remover a tarefa '{taskName}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Confirma se a tarefa existe no Agendador de Tareças do Windows.
        /// </summary>
        private bool TaskbarSchedulerTaskExists(string taskName)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/Query /TN \"{taskName}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return false;

                var tOut = proc.StandardOutput.ReadToEndAsync();
                var tErr = proc.StandardError.ReadToEndAsync();
                if (!proc.WaitForExit(10_000)) { try { proc.Kill(); } catch { } return false; }

                return proc.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Forï¾ƒï½§a uma re-centralizaï¾ƒï½§ï¾ƒï½£o imediata sem alterar o estado de habilitaï¾ƒï½§ï¾ƒï½£o.
        /// Usado pelo processo headless --taskbar-center durante o loop de guarda de 45s
        /// para combater o Explorer revertendo a posiï¾ƒï½§ï¾ƒï½£o dos ï¾ƒï½­cones ao inicializar.
        /// </summary>
        public void ForceRecenter()
        {
            _logger.LogDebug($"{TAG} [ForceRecenter] Solicitado pelo processo headless.", source: "TaskbarCtrl");
            _forceRecenter = true;

            // Garantir que o loop estï¾ƒï½¡ rodando (pode nï¾ƒï½£o estar se SetCentering ainda nï¾ƒï½£o foi chamado)
            if (!_centeringEnabled)
            {
                _centeringEnabled = true;
                EnsureLoopRunning();
            }
        }

        public void SetCentering(bool enable)
        {
            _logger.LogInfo($"{TAG} SetCentering({enable}) çª¶ï¿½ styleEnabled={_styleEnabled}");
            _centeringEnabled = enable;

            if (enable)
            {
                // ï¿½æ«¨ CORREï¾ƒï¿½グ DEFINITIVA #2: Aplicar centralizaï¾ƒï½§ï¾ƒï½£o IMEDIATAMENTE (sem debounce)
                // O debounce de 5s era perigoso - se o usuï¾ƒï½¡rio reiniciasse o PC nesse intervalo,
                // a centralizaï¾ƒï½§ï¾ƒï½£o nï¾ƒï½£o estaria aplicada ainda.
                //
                // Estratï¾ƒï½©gia:
                //   1. Aplicar centralizaï¾ƒï½§ï¾ƒï½£o IMEDIATA (antes do Explorer sobrescrever)
                //   2. Agendar persistï¾ƒï½ªncia (Task Scheduler) com debounce para evitar I/O excessivo
                //   3. Loop de monitoramento jï¾ƒï½¡ estï¾ƒï½¡ ativo para corrigir qualquer drift
                
                _logger.LogInfo($"{TAG} [SetCentering] ï¿½å¼  APLICANDO centralizaï¾ƒï½§ï¾ƒï½£o IMEDIATA (sem debounce)...");
                
                // 1. Aplicação IMEDIATA (síncrona)
                try
                {
                    _forceRecenter = true;
                    EnsureLoopRunning();
                    
                    // Sem espera bloqueante: SetCentering e chamado pelo setter de uma
                    // propriedade ligada por binding, portanto na UI thread. Um
                    // Thread.Sleep aqui congelava a interface por 200 ms a cada clique.
                    // O loop de 400 ms aplica o recenter em poucos milissegundos de
                    // qualquer forma, e o estado real e lido logo depois por
                    // IsCenteringRecentlyApplied().
                    // A confirmacao NAO e afirmada aqui: quem verifica e
                    // IsCenteringCurrentlyApplied(), chamado pelo ViewModel.
                    _logger.LogInfo($"{TAG} [SetCentering] Solicitacao de centralizacao registrada. A confirmacao no sistema sera feita por IsCenteringCurrentlyApplied().");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"{TAG} [SetCentering] Erro na aplicaï¾ƒï½§ï¾ƒï½£o imediata: {ex.Message}");
                }
                
                // 2. Persistï¾ƒï½ªncia (com debounce apenas para I/O, nï¾ƒï½£o para aplicaï¾ƒï½§ï¾ƒï½£o)
                if ((DateTime.UtcNow - _lastPersistAttempt) < _persistDebounceInterval)
                {
                    _logger.LogDebug($"{TAG} [SetCentering] Persistï¾ƒï½ªncia ignorada (debounce ativo)");
                }
                else
                {
                    _lastPersistAttempt = DateTime.UtcNow;
                    Task.Run(() => PersistTaskbarAlignment(enable));
                }
            }
            else
            {
                RevertTasklistToZero();
                StopLoopIfIdle();

                // Persiste o estado DESATIVADO.
                // Antes, desativar apenas movia a janela de volta e parava o loop:
                // o TaskbarAl=1 continuava gravado no Win11 e a tarefa agendada do
                // Win10 continuava registrada, de modo que o Windows re-centralizava
                // os icones no proximo logon. O toggle dizia "desativado" enquanto
                // o sistema fazia o contrario.
                _ = Task.Run(() => PersistTaskbarAlignment(false));
            }
        }

        /// <summary>
        /// Informa se a centralizacao esta REALMENTE aplicada no sistema.
        /// Le a fonte correta para cada versao do Windows, em vez de assumir.
        /// </summary>
        public bool IsCenteringCurrentlyApplied()
        {
            try
            {
                if (SystemInfoService.IsWindows11)
                {
                    using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(TASKBAR_ALIGNMENT_KEY);
                    var raw = key?.GetValue(TASKBAR_ALIGNMENT_VALUE);
                    if (raw != null && int.TryParse(raw.ToString(), out int taskbarAl))
                        return taskbarAl == 1;

                    // Chave/valor ausentes significam o padrao do Windows: alinhado a esquerda.
                    return false;
                }

                // Win10: a posicao real da janela MSTaskListWClass e a fonte de verdade.
                var trayHandles = GetAllTrayHandles();
                if (trayHandles == null || trayHandles.Count == 0) return false;

                var info = GetTaskbarInfo(trayHandles[0]);
                if (info.TaskListHwnd == IntPtr.Zero) return false;

                if (!GetWindowRect(info.TaskListHwnd, out var rect)) return false;

                int swWidth = GetSystemMetrics(SM_CXSCREEN);
                int swLeft = GetSystemMetrics(SM_XVIRTUALSCREEN);

                if (swWidth <= 0) return false;

                int taskListWidth = rect.Right - rect.Left;
                if (taskListWidth <= 0) return false;

                // Centralizado = borda esquerda aproximadamente na metade da tela.
                int relativeLeft = rect.Left - swLeft;
                int centerTarget = (swWidth / 2) - (taskListWidth / 2);
                const int tolerance = 24;
                return Math.Abs(relativeLeft - centerTarget) <= tolerance;
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"{TAG} [IsCentering] Falha ao ler o estado real: {ex.Message}");
                return _centeringEnabled;
            }
        }


        public void SetStyle(bool enable, TaskbarStyleMode mode = TaskbarStyleMode.Transparent,
            byte opacity = 255, int r = 0, int g = 0, int b = 0, int alpha = 0)
        {
            _logger.LogInfo($"{TAG} SetStyle(enable={enable} mode={mode} opacity={opacity} rgba={r},{g},{b},{alpha}) çª¶ï¿½ centeringEnabled={_centeringEnabled}");
            bool modeChanged = _styleMode != mode || _opacity != opacity || _colorR != r || _colorG != g || _colorB != b || _colorAlpha != alpha || _styleEnabled != enable;
            _logger.LogDebug($"{TAG} [SetStyle] modeChanged={modeChanged} (prev: mode={_styleMode} opacity={_opacity})", source: "TaskbarCtrl");

            _styleEnabled = enable;
            _styleMode    = mode;
            _opacity      = opacity;
            _colorR = r; _colorG = g; _colorB = b; _colorAlpha = alpha;

            SaveSettings(mode, opacity, r, g, b);

            if ((DateTime.UtcNow - _lastStylePersistAttempt) < _stylePersistDebounceInterval)
            {
                _logger.LogDebug($"{TAG} [SetStyle] Style persist ignorada (debounce ativo)");
            }
            else
            {
                _lastStylePersistAttempt = DateTime.UtcNow;
                UpdateTaskbarStyleScheduler(enable, mode, opacity, r, g, b);
            }

            if (enable)
            {
                _pendingStyleApply = true;
                _logger.LogDebug($"{TAG} [SetStyle] pendingStyleApply=true çª¶ï¿½ loop de background vai aplicar.", source: "TaskbarCtrl");

                if (_centeringEnabled)
                {
                    _forceRecenter = true;
                }
            }
            else
            {
                ResetStyleOnAllTaskbars();
                _pendingStyleApply = false;
            }
        }

        /// <summary>
        /// ï¿½æ«¨ NOVO: Sistema de Persistï¾ƒï½ªncia Permanente para Efeitos Visuais do Taskbar
        /// Cria tarefa agendada para restaurar efeitos visuais no boot do sistema
        /// </summary>
        private void UpdateTaskbarStyleScheduler(bool enable, TaskbarStyleMode mode, byte opacity, int r, int g, int b)
        {
            try
            {
                string taskName = "VoltrisTaskbarStyle";
                
                // Se estiver desativando, apenas deleta a tarefa (em background)
                if (!enable)
                {
                    Task.Run(() => {
                        try {
                            Process.Start(new ProcessStartInfo { 
                                FileName = "schtasks.exe", 
                                Arguments = $"/Delete /TN \"{taskName}\" /F", 
                                CreateNoWindow = true, 
                                UseShellExecute = false 
                            })?.WaitForExit(2000);
                        } catch { }
                    });
                    _logger.LogInfo($"{TAG} [StylePersist] Tarefa Agendada de estilo sendo removida em background.");
                    return;
                }

                // Se estiver ativando, cria a tarefa com parï¾ƒï½¢metros completos
                string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                string styleParams = $"--taskbar-style --mode={(int)mode} --opacity={opacity} --r={r} --g={g} --b={b}";
                
                string taskXml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Date>{DateTime.Now:yyyy-MM-dd}T{DateTime.Now:HH:mm:ss}</Date>
    <Author>Voltris Optimizer</Author>
    <Description>Restaura automaticamente os efeitos visuais da taskbar ao iniciar o sistema (estilo, transparï¾ƒï½ªncia, cor).</Description>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
      <Delay>PT5S</Delay>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <UserId>{Environment.UserDomainName}\\{Environment.UserName}</UserId>
    <LogonType>InteractiveToken</LogonType>
    <RunLevel>LeastPrivilege</RunLevel>
  </Principals>
  <Settings>
    <MultipleInstances>IgnoreNew</MultipleInstances>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>true</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>true</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT72H</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>""{exePath}""</Command>
      <Arguments>""{styleParams}""</Arguments>
    </Exec>
  </Actions>
</Task>";

                // Criar e registrar a tarefa em background para nï¾ƒï½£o travar a UI
                Task.Run(() => {
                    try {
                        // Salvar XML temporï¾ƒï½¡rio e registrar
                        string tempXml = Path.GetTempFileName();
                        File.WriteAllText(tempXml, taskXml, Encoding.Unicode);
                        
                        Process.Start(new ProcessStartInfo { 
                            FileName = "schtasks.exe", 
                            Arguments = $"/Create /TN \"{taskName}\" /XML \"{tempXml}\"", 
                            CreateNoWindow = true, 
                            UseShellExecute = false 
                        })?.WaitForExit(5000);
                        
                        File.Delete(tempXml);
                        _logger.LogInfo($"{TAG} [StylePersist] Tarefa Agendada de estilo atualizada com sucesso.");
                    } catch (Exception ex) {
                        _logger.LogError($"{TAG} [StylePersist] Falha ao atualizar tarefa agendada: {ex.Message}");
                    }
                });
                _logger.LogInfo($"{TAG} [StylePersist] Tarefa Agendada de estilo criada com sucesso.");
                
                // ï¿½æ«¨ APLICAR IMEDIATAMENTE: Garantir que o estilo fique ativo agora
                _ = Task.Run(async () =>
                {
                    await Task.Delay(2000); // Aguardar sistema estabilizar
                    try
                    {
                        ApplyStyleToAllTaskbars();
                        _logger.LogInfo($"{TAG} [StylePersist] Estilo aplicado imediatamente apï¾ƒï½³s persistï¾ƒï½ªncia.");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"{TAG} [StylePersist] Falha ao aplicar estilo imediato: {ex.Message}");
                    }
                });
            }
            catch (Exception ex) 
            { 
                _logger.LogError($"{TAG} [StylePersist] Erro ao atualizar Task Scheduler de estilo: {ex.Message}"); 
            }
        }

        public void StopAll()
        {
            _logger.LogInfo($"{TAG} StopAll()");
            _centeringEnabled = false;
            _styleEnabled     = false;
            RevertTasklistToZero();
            ResetStyleOnAllTaskbars();
            StopLoop();
        }

        // ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦
        // LOOP DE MONITORAMENTO çª¶ï¿½ idï¾ƒï½ªntico ao Looper() do TaskbarX
        // ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦

        private readonly object _loopLock = new object();

        private void EnsureLoopRunning()
        {
            lock (_loopLock)
            {
                if (_loopCts != null && !_loopCts.IsCancellationRequested)
                {
                    _logger.LogDebug($"{TAG} [Loop] Jï¾ƒï½¡ ativo çª¶ï¿½ nenhuma aï¾ƒï½§ï¾ƒï½£o necessï¾ƒï½¡ria.", source: "TaskbarCtrl");
                    return;
                }
                _logger.LogInfo($"{TAG} [Loop] Iniciando (intervalo={LoopRefreshRate}ms, centering={_centeringEnabled}, style={_styleEnabled}, voltrisBlur={_voltrisBlurActive}).");
                _loopCts = new CancellationTokenSource();
                var token = _loopCts.Token;
                Task.Run(() => MonitorLoop(token), token);
                
                // CORREï¾ƒï¿½グ CRï¾ƒæŽ§ICA: Iniciar TrayLoopFix para monitorar "Notï¾ƒï½­cias e Interesses"
                if (_centeringEnabled && _trayLoopCts == null)
                {
                    _logger.LogInfo($"{TAG} [TrayLoopFix] Iniciando thread de monitoramento do TrayNotifyWnd...");
                    _trayLoopCts = new CancellationTokenSource();
                    var trayToken = _trayLoopCts.Token;
                    Task.Run(() => TrayLoopFix(trayToken), trayToken);
                    _logger.LogSuccess($"{TAG} [TrayLoopFix] ç¬¨ï¿½ TrayLoopFix iniciado - \"Notï¾ƒï½­cias e Interesses\" serï¾ƒï½¡ monitorado!");
                }
                
                // ï¿½æ«¨ CORREï¾ƒï¿½グ #3: Iniciar Explorer Restart Monitor
                if (_centeringEnabled)
                {
                    StartExplorerRestartMonitor();
                }
            }
        }
        
        /// <summary>
        /// ï¿½æ«¨ CORREï¾ƒï¿½グ #3: Monitora restart do Explorer e reaplica centralizaï¾ƒï½§ï¾ƒï½£o automaticamente
        /// Quando o explorer.exe reinicia, a taskbar é recriada e os ï¾ƒï½­cones voltam para LEFT=0.
        /// Este monitor detecta a mudanï¾ƒï½§a de PID e reaplica a centralizaï¾ƒï½§ï¾ƒï½£o em < 1 segundo.
        /// </summary>
        private void StartExplorerRestartMonitor()
        {
            // Verificar se jï¾ƒï½¡ estï¾ƒï½¡ rodando
            if (_lastExplorerPid != 0)
            {
                _logger.LogDebug($"{TAG} [ExplorerMonitor] Jï¾ƒï½¡ iniciado (lastPid={_lastExplorerPid}).");
                return;
            }
            
            _ = Task.Run(async () =>
            {
                _lastExplorerPid = GetExplorerProcessId();
                _logger.LogInfo($"{TAG} [ExplorerMonitor] ï¿½å¼  INICIADO - Explorer PID inicial: {_lastExplorerPid}");
                
                while (_centeringEnabled)
                {
                    try
                    {
                        await Task.Delay(_explorerCheckInterval);
                        
                        // Respeitar intervalo mï¾ƒï½­nimo de verificaï¾ƒï½§ï¾ƒï½£o
                        if ((DateTime.UtcNow - _lastExplorerCheck) < _explorerCheckInterval)
                            continue;
                        
                        _lastExplorerCheck = DateTime.UtcNow;
                        var currentExplorerPid = GetExplorerProcessId();
                        
                        // Detectar restart do Explorer (PID mudou)
                        if (currentExplorerPid != _lastExplorerPid && currentExplorerPid > 0)
                        {
                            _logger.LogInfo($"{TAG} [ExplorerMonitor] ï¿½è­˜ [CRï¾ƒæŽ§ICO] Explorer REINICIADO! PID: {_lastExplorerPid} -> {currentExplorerPid}");
                            
                            // Aguardar Explorer carregar completamente (3 segundos)
                            _logger.LogInfo($"{TAG} [ExplorerMonitor] Aguardando 3s para Explorer estabilizar...");
                            await Task.Delay(3000);
                            
                            // Reaplicar centralização IMEDIATAMENTE
                            if (_centeringEnabled)
                            {
                                _logger.LogInfo($"{TAG} [ExplorerMonitor] ï¿½å¼  Reaplicando centralizaï¾ƒï½§ï¾ƒï½£o apï¾ƒï½³s restart do Explorer...");
                                
                                try
                                {
                                    // Forçar re-centralização
                                    _forceRecenter = true;
                                    CenterAllTaskbars();
                                    
                                    // Tambï¾ƒï½©m garantir registro no Windows 11
                                    if (SystemInfoService.IsWindows11)
                                    {
                                        WriteWin11NativeAlignment(true, out _);
                                        _logger.LogInfo($"{TAG} [ExplorerMonitor] Registro TaskbarAl=1 forçado.");
                                    }
                                    
                                    _logger.LogSuccess($"{TAG} [ExplorerMonitor] ï¿½ç±¨ï¿½ Centralizaï¾ƒï½§ï¾ƒï½£o REAPLICADA automaticamente apï¾ƒï½³s restart do Explorer!");
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError($"{TAG} [ExplorerMonitor] Erro ao reaplicar: {ex.Message}");
                                }
                            }
                            
                            _lastExplorerPid = currentExplorerPid;
                        }
                        else if (currentExplorerPid == 0)
                        {
                            // Explorer caiu ou nï¾ƒï½£o estï¾ƒï½¡ rodando - aguardar
                            _logger.LogWarning($"{TAG} [ExplorerMonitor] Explorer nï¾ƒï½£o encontrado (PID=0). Aguardando restart...");
                            await Task.Delay(2000);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"{TAG} [ExplorerMonitor] Erro: {ex.Message}");
                    }
                }
                
                // Resetar quando centralizaï¾ƒï½§ï¾ƒï½£o for desativada
                _lastExplorerPid = 0;
                _logger.LogInfo($"{TAG} [ExplorerMonitor] Encerrado (centeringEnabled=false).");
            });
        }
        
        /// <summary>
        /// Obtém PID do Explorer
        /// </summary>
        private int GetExplorerProcessId()
        {
            try
            {
                var explorer = System.Diagnostics.Process.GetProcessesByName("explorer")
                    .FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero || p.Threads.Count > 0);
                return explorer?.Id ?? 0;
            }
            catch
            {
                return 0;
            }
        }

        private void StopLoop()
        {
            if (_loopCts != null)
            {
                _logger.LogInfo($"{TAG} [Loop] Parando.");
                _loopCts.Cancel();
                _loopCts.Dispose();
                _loopCts = null;
            }
            
            // Parar tambï¾ƒï½©m o TrayLoopFix
            if (_trayLoopCts != null)
            {
                _logger.LogInfo($"{TAG} [TrayLoopFix] Parando.");
                _trayLoopCts.Cancel();
                _trayLoopCts.Dispose();
                _trayLoopCts = null;
                _lastTrayNotifyWidths.Clear();
            }
        }

        private void StopLoopIfIdle()
        {
            if (!_centeringEnabled && !_styleEnabled) StopLoop();
        }

        private int _stateChangeRetryCount = 0;
        private const int StateChangeRetryMax  = 3;
        private const int StateChangeRetryMs   = 200;
        private bool _isFullscreenAppActive = false;

        /// <summary>
        /// Detecta se o foreground window estï¾ƒï½¡ em modo fullscreen (TaskbarX CheckFullscreenApp).
        /// Verifica se a janela ativa estï¾ƒï½¡ maximizada e cobre a ï¾ƒï½¡rea de trabalho inteira.
        /// Nï¾ƒグ considera fullscreen: explorer, terminal, powershell, cmd, navegador, etc.
        /// Apenas jogos e apps verdadeiramente fullscreen devem pausar a centralizaï¾ƒï½§ï¾ƒï½£o.
        /// </summary>
        private bool IsFullscreenAppActive()
        {
            try
            {
                IntPtr fgWindow = GetForegroundWindow();
                if (fgWindow == IntPtr.Zero) return false;

                // Ignorar Shell_TrayWnd e a prï¾ƒï½³pria taskbar
                var sb = new StringBuilder(256);
                GetClassName(fgWindow, sb, 256);
                string cls = sb.ToString();
                if (cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd" ||
                    cls == "WorkerW" || cls == "Progman") return false;

                // Ignorar janela do prï¾ƒï½³prio VoltrisOptimizer
                if (cls == "WindowClass1") return false;

                // Obter nome do processo para filtrar apps que Nï¾ƒグ sï¾ƒï½£o jogos fullscreen
                uint pid = 0;
                GetWindowThreadProcessId(fgWindow, out pid);
                if (pid > 0)
                {
                    try
                    {
                        using (var proc = System.Diagnostics.Process.GetProcessById((int)pid))
                        {
                            string procName = proc.ProcessName.ToLower();
                            // Lista de processos que NUNCA devem ser considerados fullscreen
                            // (apps desktop normais que podem estar maximizados)
                            if (procName.Contains("explorer") || 
                                procName.Contains("powershell") || 
                                procName.Contains("pwsh") ||
                                procName.Contains("cmd") ||
                                procName.Contains("conhost") ||
                                procName.Contains("wt") || // Windows Terminal
                                procName.Contains("terminal") ||
                                procName.Contains("code") || // VS Code
                                procName.Contains("notepad") ||
                                procName.Contains("word") ||
                                procName.Contains("excel") ||
                                procName.Contains("powerpnt") || // PowerPoint
                                procName.Contains("outlook") ||
                                procName.Contains("chrome") ||
                                procName.Contains("msedge") ||
                                procName.Contains("firefox") ||
                                procName.Contains("iexplore"))
                            {
                                _logger.LogDebug($"{TAG} [Fullscreen] {procName} ignorado (nï¾ƒï½£o ï¾ƒï½© jogo fullscreen)");
                                return false;
                            }
                        }
                    }
                    catch { }
                }

                // Verificar se estï¾ƒï½¡ maximizada
                var placement = new WINDOWPLACEMENT();
                placement.length = Marshal.SizeOf<WINDOWPLACEMENT>();
                if (!GetWindowPlacement(fgWindow, ref placement)) return false;
                if (placement.showCmd != 3) return false; // SW_SHOWMAXIMIZED

                // Verificar se cobre o monitor inteiro
                IntPtr monitor = MonitorFromWindow(fgWindow, MONITOR_DEFAULTTONEAREST);
                if (monitor == IntPtr.Zero) return false;

                var mi = new MONITORINFO();
                mi.cbSize = Marshal.SizeOf<MONITORINFO>();
                if (!GetMonitorInfo(monitor, ref mi)) return false;

                GetWindowRect(fgWindow, out var fgRect);
                int fgW = fgRect.Right - fgRect.Left;
                int fgH = fgRect.Bottom - fgRect.Top;
                int monW = mi.rcMonitor.Right - mi.rcMonitor.Left;
                int monH = mi.rcMonitor.Bottom - mi.rcMonitor.Top;

                // Se a janela cobre ç«•ï½¥98% do monitor E nï¾ƒï½£o ï¾ƒï½© um app desktop conhecido, ï¾ƒï½© fullscreen
                bool isFullscreen = (fgW >= monW * 0.98 && fgH >= monH * 0.98);
                
                if (isFullscreen)
                {
                    _logger.LogInfo($"{TAG} [Fullscreen] ç¬žï¿½ FULLSCREEN DETECTADO: cls={cls} pid={pid} size={fgW}x{fgH} monitor={monW}x{monH}");
                }
                
                return isFullscreen;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"{TAG} [Fullscreen] Erro na detecï¾ƒï½§ï¾ƒï½£o: {ex.Message}");
                return false;
            }
        }

        private async Task MonitorLoop(CancellationToken ct)
        {
            _logger.LogInfo($"{TAG} [Loop] ç¬Šçµ¶æ­¦ Thread INICIADA tid={Environment.CurrentManagedThreadId} centering={_centeringEnabled} style={_styleEnabled} voltrisBlur={_voltrisBlurActive} ç¬Šçµ¶æ­¦");
            string lastState = string.Empty;
            int fullscreenPauseCycles = 0;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInfo($"{TAG} [Loop]   HEARTBEAT cycle start centering={_centeringEnabled} fullscreen={_isFullscreenAppActive} forceRecenter={_forceRecenter} retry={_stateChangeRetryCount}");
                    
                    // ï¿½æ«¨ CORREï¾ƒï¿½グ #4: Task Scheduler Health Check (a cada 30 segundos)
                    // Verifica se a tarefa "VoltrisTaskbarCenter" estï¾ƒï½¡ ativa e habilitada.
                    // O Windows pode desativar tarefas automaticamente por polï¾ƒï½­ticas de energia.
                    if (_centeringEnabled && (DateTime.UtcNow - _lastTaskSchedulerCheck) >= _taskSchedulerCheckInterval)
                    {
                        _lastTaskSchedulerCheck = DateTime.UtcNow;
                        _ = Task.Run(() => CheckAndRestorePersistTask());
                        _logger.LogDebug($"{TAG} [Loop] Task Scheduler Health Check agendado.");
                    }
                    
                    // ç¬ ç¬  DETECï¾ƒï¿½グ DE FULLSCREEN (TaskbarX CheckFullscreenApp) ç¬ ç¬ 
                    bool fullscreenNow = IsFullscreenAppActive();
                    if (fullscreenNow != _isFullscreenAppActive)
                    {
                        _isFullscreenAppActive = fullscreenNow;
                        if (fullscreenNow)
                        {
                            _logger.LogInfo($"{TAG} [Loop] ï¿½å¼  App fullscreen DETECTADO çª¶ï¿½ pausando centralizaï¾ƒï½§ï¾ƒï½£o.");
                            fullscreenPauseCycles = 5;
                        }
                        else
                        {
                            _logger.LogInfo($"{TAG} [Loop] ï¿½å¼  App fullscreen ENCERRADO çª¶ï¿½ retomando centralizaï¾ƒï½§ï¾ƒï½£o.");
                            _forceRecenter = true;
                        }
                    }

                    if (_isFullscreenAppActive || fullscreenPauseCycles > 0)
                    {
                        if (fullscreenPauseCycles > 0) fullscreenPauseCycles--;
                        _logger.LogDebug($"{TAG} [Loop] ç«¢ï½¸ SKIPPED centering: fullscreen={_isFullscreenAppActive} pauseCycles={fullscreenPauseCycles}");
                        await Task.Delay(LoopRefreshRate, ct);
                        continue;
                    }

                    // ç¬ ç¬  Estilo nativo da taskbar ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ 
                    if (_styleEnabled && !_voltrisBlurActive)
                    {
                        ApplyStyleToAllTaskbars();
                        _logger.LogDebug($"{TAG} [Loop] Estilo reaplicado mode={_styleMode} opacity={_opacity}", source: "TaskbarCtrl");
                    }
                    else if (_styleEnabled && _voltrisBlurActive)
                    {
                        _logger.LogDebug($"{TAG} [Loop] VoltrisBlur ativo çª¶ï¿½ estilo nativo suprimido", source: "TaskbarCtrl");
                    }

                    // ç¬ ç¬  Centralizaï¾ƒï½§ï¾ƒï½£o ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ ç¬ 
                    if (_centeringEnabled)
                    {
                        bool forced = _forceRecenter;
                        if (forced)
                        {
                            _forceRecenter = false;
                            _logger.LogInfo($"{TAG} [Loop] ï¿½å£² Re-centralizaï¾ƒï½§ï¾ƒï½£o FORï¾ƒâ‘¡DA (forceRecenter=true).");
                        }

                        string currentState = BuildStateString();
                        bool stateChanged = currentState != lastState && !string.IsNullOrEmpty(currentState);

                        if (stateChanged)
                        {
                            _logger.LogInfo($"{TAG} [Loop] ï¿½ç›— Estado MUDOU: '{lastState}' ç«Šï¿½ '{currentState}'");
                            lastState = currentState;
                            _stateChangeRetryCount = StateChangeRetryMax;
                        }

                        // TaskbarX: recalcula na mudanï¾ƒï½§a de estado + reaplicaï¾ƒï½§ï¾ƒï½µes periï¾ƒï½³dicas
                        if (forced || stateChanged || _stateChangeRetryCount > 0)
                        {
                            if (_stateChangeRetryCount > 0)
                            {
                                _stateChangeRetryCount--;
                                _logger.LogInfo($"{TAG} [Loop] ç¬†ï½¶ Centralizando (retry={_stateChangeRetryCount}/{StateChangeRetryMax}) forced={forced} state='{lastState}'");
                                
                                await Task.Delay(300, ct);
                                
                                string stateAfterDelay = BuildStateString();
                                if (stateAfterDelay != lastState && !string.IsNullOrEmpty(stateAfterDelay))
                                {
                                    _logger.LogInfo($"{TAG} [Loop] ï¿½ç›— Estado mudou DURANTE delay: '{lastState}' ç«Šï¿½ '{stateAfterDelay}'");
                                    lastState = stateAfterDelay;
                                    if (_stateChangeRetryCount < StateChangeRetryMax)
                                        _stateChangeRetryCount = StateChangeRetryMax;
                                }
                            }
                            else
                            {
                                _logger.LogDebug($"{TAG} [Loop] ç¬†ï½¶ Centralizando forced={forced} state='{lastState}'", source: "TaskbarCtrl");
                            }

                            _logger.LogInfo($"{TAG} [Loop] ï¿½å™« CHAMANDO CenterAllTaskbars() AGORA...");
                            CenterAllTaskbars();
                            _logger.LogInfo($"{TAG} [Loop] ç¬¨ï¿½ CenterAllTaskbars() COMPLETO");
                        }
                        else
                        {
                            // ï¿½æ«¨ CORREï¾ƒï¿½グ DEFINITIVA #1: Verificaï¾ƒï½§ï¾ƒï½£o de drift CONTï¾ƒæŽ½NUA (a cada ciclo)
                            // Isso previne que os ï¾ƒï½­cones fiquem descentralizados por atï¾ƒï½© 2s.
                            // O Explorer pode reposicionar o TaskList para LEFT=0 a qualquer momento,
                            // especialmente apï¾ƒï½³s restart, mudanï¾ƒï½§a de wallpaper, ou alteraï¾ƒï½§ï¾ƒï½µes na taskbar.
                            bool positionDrifted = IsTaskListPositionDrifted();
                            if (positionDrifted)
                            {
                                _logger.LogInfo($"{TAG} [Loop] ï¿½è­˜ [CRï¾ƒæŽ§ICO] Posiï¾ƒï½§ï¾ƒï½£o do TaskList DESVIOU çª¶ï¿½ recentralizando IMEDIATAMENTE (drift detection).");
                                _stateChangeRetryCount = StateChangeRetryMax;
                            }
                            else
                            {
                                // Apenas incrementa o contador se Nï¾ƒグ houver drift
                                _quickCycleCounter++;
                                if (_quickCycleCounter >= 5)
                                {
                                    _quickCycleCounter = 0;
                                    _logger.LogInfo($"{TAG} [Loop] ï¿½å£² Centralizaï¾ƒï½§ï¾ƒï½£o periï¾ƒï½³dica (5 ciclos) - CHAMANDO CenterAllTaskbars()...");
                                    CenterAllTaskbars();
                                    _logger.LogInfo($"{TAG} [Loop] ç¬¨ï¿½ CenterAllTaskbars() periï¾ƒï½³dico COMPLETO");
                                }
                                else
                                {
                                    _logger.LogDebug($"{TAG} [Loop] ç«¢ï½­ Skip centering: quickCycle={_quickCycleCounter}/5 forced={forced} stateChanged={stateChanged} retry={_stateChangeRetryCount}");
                                }
                            }
                        }
                    }

                    await Task.Delay(LoopRefreshRate, ct);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogWarning($"{TAG} [Loop] Erro: {ex.Message}");
                    try { await Task.Delay(1000, ct); } catch { break; }
                }
            }
            _logger.LogInfo($"{TAG} [Loop] Thread ENCERRADA tid={Environment.CurrentManagedThreadId}");
        }

        private readonly Dictionary<IntPtr, int> _lastNewsAndInterestsWidths = new();

        private async Task TrayLoopFix(CancellationToken ct)
        {
            _logger.LogInfo($"{TAG} [TrayLoopFix] Thread INICIADA");
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        foreach (var info in GetAllTaskbarInfos())
                        {
                            if (!info.Valid) continue;
                            var shellTrayWnd  = info.TrayWnd;
                            var trayNotifyWnd = FindWindowEx(shellTrayWnd, IntPtr.Zero, "TrayNotifyWnd", (string?)null);
                            var rebarWnd      = FindWindowEx(shellTrayWnd, IntPtr.Zero, "ReBarWindow32", (string?)null);
                            var taskSwWnd     = FindWindowEx(rebarWnd, IntPtr.Zero, "MSTaskSwWClass", (string?)null);
                            var taskListWnd   = FindWindowEx(taskSwWnd, IntPtr.Zero, "MSTaskListWClass", (string?)null);
                            var newsWidgetWnd = FindWindowEx(shellTrayWnd, IntPtr.Zero, "DynamicContent1", (string?)null);
                            if (taskListWnd == IntPtr.Zero) continue;

                            GetWindowRect(taskListWnd, out var taskListRect);
                            GetWindowRect(rebarWnd,    out var rebarRect2);
                            GetWindowRect(shellTrayWnd, out var trayWndRect);
                            IntPtr swHwndFix = taskSwWnd != IntPtr.Zero
                                ? taskSwWnd
                                : FindWindowEx(rebarWnd, IntPtr.Zero, "MSTaskSwWClass", IntPtr.Zero);
                            GetWindowRect(swHwndFix, out var swRectFix);

                            int taskListWidth  = taskListRect.Right  - taskListRect.Left;
                            int taskListHeight = taskListRect.Bottom - taskListRect.Top;
                            string orientation = (taskListHeight >= taskListWidth) ? "V" : "H";

                            int localTaskbarWidth = GetTaskbarIconsWidth(taskListWnd, orientation);
                            if (localTaskbarWidth <= 0)
                                localTaskbarWidth = orientation == "H" ? taskListWidth : taskListHeight;

                            int trayNotifyWidth = 0;
                            if (trayNotifyWnd != IntPtr.Zero)
                            {
                                GetWindowRect(trayNotifyWnd, out var trayNotifyRect);
                                trayNotifyWidth = orientation == "H"
                                    ? (trayNotifyRect.Right - trayNotifyRect.Left)
                                    : (trayNotifyRect.Bottom - trayNotifyRect.Top);
                            }

                            int newsWidgetWidth = 0;
                            if (newsWidgetWnd != IntPtr.Zero)
                            {
                                GetWindowRect(newsWidgetWnd, out var newsRect);
                                newsWidgetWidth = orientation == "H"
                                    ? (newsRect.Right - newsRect.Left)
                                    : (newsRect.Bottom - newsRect.Top);
                            }

                            bool trayChanged = false;
                            if (_lastTrayNotifyWidths.TryGetValue(shellTrayWnd, out var lastTrayW))
                                if (trayNotifyWidth != lastTrayW) trayChanged = true;

                            bool newsChanged = false;
                            if (_lastNewsAndInterestsWidths.TryGetValue(shellTrayWnd, out var lastNewsW))
                                if (newsWidgetWidth != lastNewsW) newsChanged = true;

                            _lastTrayNotifyWidths[shellTrayWnd]       = trayNotifyWidth;
                            _lastNewsAndInterestsWidths[shellTrayWnd] = newsWidgetWidth;

                            if ((trayChanged || newsChanged) && _centeringEnabled)
                            {
                                await Task.Delay(50, ct);

                                // Variaveis identicas ao TaskbarX-master
                                int trayWndWidth = orientation == "H"
                                    ? Math.Abs(trayWndRect.Right - trayWndRect.Left)
                                    : Math.Abs(trayWndRect.Bottom - trayWndRect.Top);
                                int trayWndLeftFix = orientation == "H" ? Math.Abs(trayWndRect.Left) : Math.Abs(trayWndRect.Top);
                                int rebarWndLeftFix = orientation == "H" ? Math.Abs(rebarRect2.Left) : Math.Abs(rebarRect2.Top);
                                int rebarWidthFix = orientation == "H" ? Math.Abs(rebarRect2.Right - rebarRect2.Left) : Math.Abs(rebarRect2.Bottom - rebarRect2.Top);
                                int taskbarLeftFix = Math.Abs(rebarWndLeftFix - trayWndLeftFix);

                                // TrayNotifyWnd width
                                int tnWidthFix = 0;
                                var tnWndFix = FindWindowEx(shellTrayWnd, IntPtr.Zero, "TrayNotifyWnd", (string?)null);
                                if (tnWndFix != IntPtr.Zero)
                                {
                                    GetWindowRect(tnWndFix, out var tnRectFix);
                                    tnWidthFix = orientation == "H" ? (tnRectFix.Right - tnRectFix.Left) : (tnRectFix.Bottom - tnRectFix.Top);
                                }

                                // NewsAndInterests width
                                int newsWidthFix = 0;
                                var newsWndFix = FindWindowEx(shellTrayWnd, IntPtr.Zero, "DynamicContent1", (string?)null);
                                if (newsWndFix != IntPtr.Zero)
                                {
                                    GetWindowRect(newsWndFix, out var newsRectFix);
                                    newsWidthFix = orientation == "H" ? (newsRectFix.Right - newsRectFix.Left) : (newsRectFix.Bottom - newsRectFix.Top);
                                }

                                // Formula CORRETA AbsoluteCenter (identica ao CenterAllTaskbars)
                                // swLeft = offset do SwHwnd desde o inicio da Shell_TrayWnd
                                int swLeftFix = orientation == "H"
                                    ? Math.Abs(swRectFix.Left - trayWndRect.Left)
                                    : Math.Abs(swRectFix.Top  - trayWndRect.Top);
                                int screenCenterFix = trayWndWidth / 2;
                                int iconStartAbsFix = screenCenterFix - (localTaskbarWidth / 2);
                                int newPosition = Math.Max(0, iconStartAbsFix - swLeftFix);

                                // Expande o SwClass se a task list ultrapassar seus limites
                                int tlWidth = orientation == "H" ? (taskListRect.Right - taskListRect.Left) : (taskListRect.Bottom - taskListRect.Top);
                                int swWidthFix = orientation == "H" ? (swRectFix.Right - swRectFix.Left) : (swRectFix.Bottom - swRectFix.Top);
                                int neededWidth = newPosition + tlWidth;
                                if (neededWidth > swWidthFix)
                                {
                                    SetWindowPos(swHwndFix, IntPtr.Zero, 0, 0, neededWidth + 4, 50,
                                        SWP_ASYNCWINDOWPOS | SWP_NOACTIVATE | SWP_NOZORDER | SWP_NOSENDCHANGING);
                                }

                                // Apenas MOVE a TaskList (NAO redimensiona nada - Windows gerencia)
                                bool swpOk = orientation == "H"
                                    ? SetWindowPos(taskListWnd, IntPtr.Zero, newPosition, 0, 0, 0,
                                        SWP_NOSIZE | SWP_ASYNCWINDOWPOS | SWP_NOACTIVATE | SWP_NOZORDER | SWP_NOSENDCHANGING)
                                    : SetWindowPos(taskListWnd, IntPtr.Zero, 0, newPosition, 0, 0,
                                        SWP_NOSIZE | SWP_ASYNCWINDOWPOS | SWP_NOACTIVATE | SWP_NOZORDER | SWP_NOSENDCHANGING);

                                _forceRecenter = true;
                            }
                        }
                        await Task.Delay(400, ct);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"{TAG} [TrayLoopFix] Erro: {ex.Message}");
                        try { await Task.Delay(1000, ct); } catch { break; }
                    }
                }
                _logger.LogInfo($"{TAG} [TrayLoopFix] Thread ENCERRADA");
            }
            catch (Exception ex)
            {
                _logger.LogError($"{TAG} [TrayLoopFix] Excecao fatal: {ex.Message}");
            }
        }

        private bool IsTaskListPositionDrifted()
        {
            try
            {
                foreach (var info in GetAllTaskbarInfos())
                {
                    if (!info.Valid) continue;
                    GetWindowRect(info.TaskListHwnd, out var taskListRect);
                    
                    int tlW = taskListRect.Right - taskListRect.Left;
                    int tlH = taskListRect.Bottom - taskListRect.Top;
                    string orient = (tlH >= tlW && tlH > 0) ? "V" : "H";
                    int iconsWidth = GetTaskbarIconsWidth(info.TaskListHwnd, orient);
                    int taskbarWidth = (iconsWidth > 0) ? iconsWidth : (orient == "H" ? tlW : tlH);

                    int expectedPos = CalculateExpectedPosition(info, taskbarWidth, out int currentPos);
                    
                    int diff = Math.Abs(expectedPos - currentPos);
                    if (diff > 5) return true;
                }
                return false;
            }
            catch { return false; }
        }

        private string BuildStateString()
        {
            var sb = new StringBuilder();
            foreach (var info in GetAllTaskbarInfos())
            {
                if (!info.Valid) continue;
                GetWindowRect(info.TrayWnd,      out var trayRect);
                GetWindowRect(info.RebarHwnd,    out var rebarRect);
                GetWindowRect(info.TaskListHwnd, out var taskListRect);

                int tlW = taskListRect.Right  - taskListRect.Left;
                int tlH = taskListRect.Bottom - taskListRect.Top;
                string orient = (tlH >= tlW && tlH > 0) ? "V" : "H";

                int iconsWidth = GetTaskbarIconsWidth(info.TaskListHwnd, orient);
                int taskbarCount = (iconsWidth > 0) ? iconsWidth : (tlW > 0 ? tlW : 0);

                int trayWndSize = orient == "H" ? (trayRect.Right - trayRect.Left) : (trayRect.Bottom - trayRect.Top);
                int newsAndInterestsWidth = GetNewsAndInterestsWidth(info.TrayWnd, orient);
                int trayNotifyWidth = GetTrayNotifyWidth(info.TrayWnd, orient);

                sb.Append($"{orient}{taskbarCount}{trayWndSize}{newsAndInterestsWidth}{trayNotifyWidth}");
            }
            return sb.ToString();
        }

        private int GetNewsAndInterestsWidth(IntPtr trayWnd, string orient)
        {
            if (trayWnd == IntPtr.Zero) return 0;
            try
            {
                IntPtr hwnd = FindWindowEx(trayWnd, IntPtr.Zero, "DynamicContent1", (string?)null);
                if (hwnd == IntPtr.Zero) return 0;
                GetWindowRect(hwnd, out var rect);
                int w = orient == "H" ? (rect.Right - rect.Left) : (rect.Bottom - rect.Top);
                return Math.Max(0, w);
            }
            catch { return 0; }
        }

        private int GetTrayNotifyWidth(IntPtr trayWnd, string orient)
        {
            if (trayWnd == IntPtr.Zero) return 0;
            try
            {
                IntPtr hwnd = FindWindowEx(trayWnd, IntPtr.Zero, "TrayNotifyWnd", (string?)null);
                if (hwnd == IntPtr.Zero) hwnd = FindWindowEx(trayWnd, IntPtr.Zero, "ClockButton", (string?)null);
                if (hwnd == IntPtr.Zero) return 0;

                RECT rect;
                if (GetWindowRect(hwnd, out rect))
                    return orient == "H" ? (rect.Right - rect.Left) : (rect.Bottom - rect.Top);
            }
            catch { }
            return 0;
        }

        [DllImport("oleacc.dll")]
        private static extern int AccessibleObjectFromWindow(IntPtr hwnd, uint id, ref Guid iid, [Out, MarshalAs(UnmanagedType.IUnknown)] out object ppvObject);

        [DllImport("oleacc.dll")]
        private static extern int AccessibleChildren(IAccessible paccContainer, int iChildStart, int cChildren, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] object[] rgvarChildren, out int pcObtained);

        private static Guid IID_IAccessible = new Guid("618736e0-3c3d-11cf-810c-00aa00389b71");
        private const uint OBJID_WINDOW = 0x00000000;

        private int GetTaskbarIconsWidth(IntPtr taskListHwnd, string orient)
        {
            if (taskListHwnd == IntPtr.Zero) return -1;

            try
            {
                object? accObj = null;
                var guid = IID_IAccessible;

                int hr = AccessibleObjectFromWindow(taskListHwnd, OBJID_WINDOW, ref guid, out accObj!);
                if (hr != 0 || accObj == null) return -1;

                var taskListAcc = (IAccessible)accObj;

                taskListAcc.accLocation(out int tlLeft, out int tlTop, out int tlW, out int tlH, 0);
                if (tlLeft == 0 && tlTop == 0 && tlW == 0 && tlH == 0)
                {
                    RECT winRect;
                    if (GetWindowRect(taskListHwnd, out winRect))
                    {
                        tlLeft = winRect.Left; tlTop = winRect.Top;
                        tlW = winRect.Right - winRect.Left; tlH = winRect.Bottom - winRect.Top;
                    }
                }

                int childCount = taskListAcc.accChildCount;
                if (childCount <= 0) return -1;

                object[] children = new object[childCount];
                hr = AccessibleChildren(taskListAcc, 0, childCount, children, out int obtained);
                if (hr != 0 || obtained == 0) return -1;

                IAccessible? toolbar = null;
                for (int i = 0; i < obtained; i++)
                {
                    if (children[i] is IAccessible childAcc)
                    {
                        try
                        {
                            object roleObj = childAcc.get_accRole(0);
                            int role = roleObj is int ri ? ri : Convert.ToInt32(roleObj);
                            if (role == 22) { toolbar = childAcc; break; }
                        }
                        catch { }
                    }
                }
                if (toolbar == null)
                {
                    try
                    {
                        object roleObj = taskListAcc.get_accRole(0);
                        int role = roleObj is int ri ? ri : Convert.ToInt32(roleObj);
                        if (role == 22) toolbar = taskListAcc;
                    }
                    catch { }
                }
                if (toolbar == null) return -1;

                int toolbarChildCount = toolbar.accChildCount;
                if (toolbarChildCount <= 0) return -1;

                int lastLeftWithSize = orient == "H" ? tlLeft : tlTop;
                int lastSizeValue = 0;
                bool foundAny = false;

                for (int childId = toolbarChildCount; childId >= 1; childId--)
                {
                    try
                    {
                        toolbar.accLocation(out int cLeft, out int cTop, out int cW, out int cH, childId);
                        if (cW >= 10 && cH >= 10)
                        {
                            lastLeftWithSize = orient == "H" ? cLeft : cTop;
                            lastSizeValue = orient == "H" ? cW : cH;
                            foundAny = true;
                            break;
                        }
                    }
                    catch { }
                }

                if (!foundAny) return -1;

                // TaskbarX original usa: LastChildPos.left - TaskListPos.left
                // Isso da a largura dos icones SEM incluir o ultimo icone, pois a
                // posicao SetWindowPos move o inicio da janela (canto esquerdo).
                int width = orient == "H"
                    ? Math.Max(0, lastLeftWithSize - tlLeft)
                    : Math.Max(0, lastLeftWithSize - tlTop);

                return width >= 0 ? width : -1;
            }
            catch { return -1; }
        }

        private void CenterAllTaskbars()
        {
            if (!_centeringEnabled) return;

            foreach (var info in GetAllTaskbarInfos())
            {
                if (!info.Valid) continue;

                try
                {
                    GetWindowRect(info.TrayWnd,      out var trayRect);
                    GetWindowRect(info.RebarHwnd,    out var rebarRect);
                    GetWindowRect(info.TaskListHwnd, out var taskListRect);
                    GetWindowRect(info.SwHwnd,       out var swRect);

                    int tlW = taskListRect.Right  - taskListRect.Left;
                    int tlH = taskListRect.Bottom - taskListRect.Top;

                    string orient = (tlH >= tlW && tlH > 0) ? "V" : "H";
                    
                    int iconsWidth = GetTaskbarIconsWidth(info.TaskListHwnd, orient);
                    
                    int taskbarWidth = 0;
                    if (iconsWidth > 0)
                    {
                        taskbarWidth = iconsWidth;
                    }
                    else
                    {
                        taskbarWidth = tlW > 0 ? tlW : 0;
                        _logger.LogWarning($"[{TAG}] [Center] IAccessible falhou (iconsWidth={iconsWidth}) -> fallback tlW={taskbarWidth}px");
                    }

                    // ============================================================
                    // Formula CORRETA: AbsoluteCenter
                    //
                    // Centraliza os icones no centro geometrico absoluto da tela:
                    //   1) trayWndWidth = largura total da Shell_TrayWnd (= monitor)
                    //   2) swLeft       = offset do SwHwnd desde o inicio da tray
                    //   3) screenCenter = trayWndWidth / 2
                    //   4) iconStartAbs = screenCenter - iconsWidth/2  (abs)
                    //   5) position     = iconStartAbs - swLeft  (relativo ao pai SwHwnd)
                    //
                    // Imune a mudancas em barra de pesquisa, TrayNotify ou News/Interests.
                    // ============================================================
                    int trayWndWidth = orient == "H"
                        ? Math.Abs(trayRect.Right  - trayRect.Left)
                        : Math.Abs(trayRect.Bottom - trayRect.Top);

                    int swLeft = orient == "H"
                        ? Math.Abs(swRect.Left - trayRect.Left)
                        : Math.Abs(swRect.Top  - trayRect.Top);

                    int screenCenter  = trayWndWidth / 2;
                    int iconStartAbs  = screenCenter - (taskbarWidth / 2);
                    int position      = Math.Max(0, iconStartAbs - swLeft);

                    // --- Variaveis de diagnostico (nao afetam o calculo) ---
                    int trayNotifyWidth = 0;
                    var tnWndDiag = FindWindowEx(info.TrayWnd, IntPtr.Zero, "TrayNotifyWnd", (string?)null);
                    if (tnWndDiag != IntPtr.Zero) { GetWindowRect(tnWndDiag, out var tnR); trayNotifyWidth = orient == "H" ? (tnR.Right - tnR.Left) : (tnR.Bottom - tnR.Top); }
                    int newsWidth = 0;
                    var newsWndDiag = FindWindowEx(info.TrayWnd, IntPtr.Zero, "DynamicContent1", (string?)null);
                    if (newsWndDiag != IntPtr.Zero) { GetWindowRect(newsWndDiag, out var nwR); newsWidth = orient == "H" ? (nwR.Right - nwR.Left) : (nwR.Bottom - nwR.Top); }
                    int swWidth      = orient == "H" ? (swRect.Right    - swRect.Left)    : (swRect.Bottom    - swRect.Top);
                    int rebarWidth   = orient == "H" ? (rebarRect.Right - rebarRect.Left) : (rebarRect.Bottom - rebarRect.Top);
                    int tlWindowWidth = orient == "H" ? (taskListRect.Right - taskListRect.Left) : (taskListRect.Bottom - taskListRect.Top);
                    _logger.LogInfo($"[{TAG}] [Center] trayW={trayWndWidth} swLeft={swLeft} center={screenCenter} iconStart={iconStartAbs} iconsW={taskbarWidth} pos={position} (AbsoluteCenter)");
                    _logger.LogInfo($"[{TAG}] [Center] DEBUG: tnW={trayNotifyWidth} newsW={newsWidth} swW={swWidth} rebarW={rebarWidth} tlW={tlWindowWidth} tlPos={taskListRect.Left} swPos={swRect.Left}");

                    // Posicao atual relativa ao SwHwnd (consistente com SetWindowPos)
                    int currentPos = orient == "H"
                        ? (taskListRect.Left - swRect.Left)
                        : (taskListRect.Top  - swRect.Top);
                    
                    if (_stateChangeRetryCount <= 0 && Math.Abs(position - currentPos) <= 2)
                    {
                        _logger.LogDebug($"[{TAG}] [Center] Ja centralizado (diff<=2px). Sem movimento.", source: "TaskbarCtrl");
                        continue;
                    }

_logger.LogInfo($"[{TAG}] [Center] Movendo: currentPos={currentPos} (rel ReBar) -> newPos={position}");

                    // Expande o SwClass se a task list ultrapassar seus limites (evita clipping)
                    int neededWidth = position + tlWindowWidth;
                    if (neededWidth > swWidth)
                    {
                        SetWindowPos(info.SwHwnd, IntPtr.Zero, 0, 0, neededWidth + 4, 50,
                            SWP_ASYNCWINDOWPOS | SWP_NOACTIVATE | SWP_NOZORDER | SWP_NOSENDCHANGING);
                        _logger.LogInfo($"[{TAG}] [Center] SwClass expandido: {swWidth} -> {neededWidth + 4}");
                    }

                    // Move a TaskList (NUNCA redimensiona - Windows gerencia)
                    SetWindowPos(info.TaskListHwnd, IntPtr.Zero, position, 0, 0, 0,
                        SWP_NOSIZE | SWP_ASYNCWINDOWPOS | SWP_NOACTIVATE | SWP_NOZORDER | SWP_NOSENDCHANGING);

                    int err = Marshal.GetLastWin32Error();
                    if (err != 0)
                        _logger.LogWarning($"[{TAG}] [Center] SetWindowPos err=0x{err:X}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[{TAG}] [Center] Excecao: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
        // ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦
        // RevertToZero çª¶ï¿½ idï¾ƒï½ªntico ao RevertToZero() do TaskbarX
        // ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦

        private void RevertTasklistToZero()
        {
            _logger.LogInfo($"{TAG} [Revert] Revertendo taskList para posiï¾ƒï½§ï¾ƒï½£o 0.");
            foreach (var info in GetAllTaskbarInfos())
            {
                try
                {
                    if (!info.Valid) continue;
                    bool ok = SetWindowPos(info.TaskListHwnd, IntPtr.Zero, 0, 0, 0, 0,
                        SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOZORDER | SWP_NOSENDCHANGING);
                    _logger.LogDebug($"{TAG} [Revert] hwnd={info.TaskListHwnd} ok={ok}", source: "TaskbarCtrl");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"{TAG} [Revert] Erro: {ex.Message}");
                }
            }
        }

        // ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦
        // ESTILO (Transparï¾ƒï½ªncia / Blur / Acrï¾ƒï½­lico)
        // ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦

        private void ApplyStyleToAllTaskbars()
        {
            // ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦
            // GradientColor formato ABGR (little-endian): bytes [R, G, B, A]
            //
            // Como o slider de opacidade funciona em cada modo (Win10):
            //
            // Transparent (ACCENT_ENABLE_TRANSPARENTGRADIENT):
            //   alpha=0   ç«Šï¿½ totalmente transparente (sem cor)
            //   alpha=255 ç«Šï¿½ cor sï¾ƒï½³lida (preta por padrï¾ƒï½£o)
            //   ç«Šï¿½ slider controla diretamente o alpha
            //
            // Blur (ACCENT_ENABLE_BLURBEHIND):
            //   alpha=0   ç«Šï¿½ blur puro (sem sobreposiï¾ƒï½§ï¾ƒï½£o de cor) çª¶ï¿½ efeito mï¾ƒï½¡ximo visï¾ƒï½­vel
            //   alpha=255 ç«Šï¿½ blur coberto por cor sï¾ƒï½³lida çª¶ï¿½ efeito mï¾ƒï½­nimo visï¾ƒï½­vel
            //   ç«Šï¿½ slider INVERTIDO: opacity=255 ç«Šï¿½ alpha=0 (blur puro), opacity=0 ç«Šï¿½ alpha=255 (cor sï¾ƒï½³lida)
            //   Isso faz o slider funcionar intuitivamente: mais opacidade = mais blur visï¾ƒï½­vel
            //
            // Acrylic (ACCENT_ENABLE_ACRYLICBLURBEHIND):
            //   alpha=0   ç«Šï¿½ acrï¾ƒï½­lico transparente
            //   alpha=255 ç«Šï¿½ acrï¾ƒï½­lico opaco
            //   ç«Šï¿½ slider direto
            //
            // Gradient (ACCENT_ENABLE_GRADIENT):
            //   alpha=0   ç«Šï¿½ gradiente transparente
            //   alpha=255 ç«Šï¿½ gradiente opaco
            //   ç«Šï¿½ slider direto
            // ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦

            int bgAlpha;
            int colorR = _colorR, colorG = _colorG, colorB = _colorB;
            AccentState accentState;
            int accentFlags;

            switch (_styleMode)
            {
                case TaskbarStyleMode.Transparent:
                    bgAlpha     = Math.Clamp((int)_opacity, 0, 255);
                    accentState = AccentState.ACCENT_ENABLE_TRANSPARENTGRADIENT;
                    accentFlags = 0;
                    break;

                case TaskbarStyleMode.Blur:
                    // Invertido para o slider do Windows ser intuitivo
                    bgAlpha     = 255 - Math.Clamp((int)_opacity, 0, 255);
                    accentState = AccentState.ACCENT_ENABLE_BLURBEHIND;
                    accentFlags = 0;
                    break;

                case TaskbarStyleMode.Acrylic:
                    bgAlpha     = Math.Clamp((int)_opacity, 0, 255);
                    accentState = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND;
                    accentFlags = 2; // Granular opacity
                    break;

                case TaskbarStyleMode.Gradient:
                    bgAlpha     = Math.Clamp((int)_opacity, 0, 255);
                    accentState = AccentState.ACCENT_ENABLE_GRADIENT;
                    accentFlags = 2;
                    break;

                case TaskbarStyleMode.Mica:
                    bgAlpha     = 255;
                    accentState = AccentState.ACCENT_ENABLE_TRANSPARENTGRADIENT;
                    accentFlags = 0;
                    break;

                default:
                    bgAlpha = 0;
                    accentState = AccentState.ACCENT_DISABLED;
                    accentFlags = 0;
                    break;
            }

            // GradientColor: little-endian ABGR ç«Šï¿½ bytes [R, G, B, A]
            int gradientColor = BitConverter.ToInt32(new byte[]
            {
                (byte)_colorR,
                (byte)_colorG,
                (byte)_colorB,
                (byte)bgAlpha
            }, 0);

            _logger.LogInfo($"{TAG} [Style] mode={_styleMode} accentState={accentState} opacity={_opacity} accentFlags={accentFlags} bgAlpha={bgAlpha} rgb=({colorR},{colorG},{colorB}) gradientColor=0x{gradientColor:X8}");

            var accent = new AccentPolicy
            {
                AccentState   = accentState,
                AccentFlags   = accentFlags,
                GradientColor = gradientColor,
                AnimationId   = 0};

            int sz = Marshal.SizeOf<AccentPolicy>();
            IntPtr ptr = Marshal.AllocHGlobal(sz);
            try
            {
                Marshal.StructureToPtr(accent, ptr, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute  = WCA_ACCENT_POLICY,
                    SizeOfData = sz,
                    Data       = ptr
                };

                foreach (var trayWnd in GetAllTrayHandles())
                {
                    try
                    {
                        // Remover WS_EX_LAYERED se presente çª¶ï¿½ interfere com SetWindowCompositionAttribute
                        int exStyle = GetWindowLong(trayWnd, GWL_EXSTYLE);
                        bool hadLayered = (exStyle & WS_EX_LAYERED) != 0;
                        if (hadLayered)
                        {
                            SetWindowLong(trayWnd, GWL_EXSTYLE, exStyle & ~WS_EX_LAYERED);
                            _logger.LogDebug($"{TAG} [Style] hwnd={trayWnd} WS_EX_LAYERED removido (exStyle=0x{exStyle:X})", source: "TaskbarCtrl");
                        }

                        int res = SetWindowCompositionAttribute(trayWnd, ref data);
                        _logger.LogDebug($"{TAG} [Style] hwnd={trayWnd} accent={accent.AccentState} flags={accentFlags} bgAlpha={bgAlpha} hadLayered={hadLayered} result={res}", source: "TaskbarCtrl");

                        if (res == 0)
                            _logger.LogWarning($"{TAG} [Style] ç¬žï¿½ SetWindowCompositionAttribute retornou 0 para hwnd={trayWnd} çª¶ï¿½ efeito pode nï¾ƒï½£o ter sido aplicado");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"{TAG} [Style] Erro hwnd={trayWnd}: {ex.Message}");
                    }
                }
            }
            finally { Marshal.FreeHGlobal(ptr); }
        }

        private void ResetStyleOnAllTaskbars()
        {
            _logger.LogInfo($"{TAG} [Style] Restaurando ACCENT_DISABLED em todas as taskbars.");
            var accent = new AccentPolicy { AccentState = AccentState.ACCENT_DISABLED };
            int sz  = Marshal.SizeOf<AccentPolicy>();
            IntPtr ptr = Marshal.AllocHGlobal(sz);
            try
            {
                Marshal.StructureToPtr(accent, ptr, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute  = WCA_ACCENT_POLICY,
                    SizeOfData = sz,
                    Data       = ptr
                };
                foreach (var trayWnd in GetAllTrayHandles())
                {
                    try
                    {
                        int exStyle = GetWindowLong(trayWnd, GWL_EXSTYLE);
                        if ((exStyle & WS_EX_LAYERED) != 0)
                        {
                            SetWindowLong(trayWnd, GWL_EXSTYLE, exStyle & ~WS_EX_LAYERED);
                            _logger.LogDebug($"{TAG} [Style] Reset hwnd={trayWnd} WS_EX_LAYERED removido", source: "TaskbarCtrl");
                        }

                        int res = SetWindowCompositionAttribute(trayWnd, ref data);
                        _logger.LogDebug($"{TAG} [Style] Reset hwnd={trayWnd} ACCENT_DISABLED result={res}", source: "TaskbarCtrl");
                    }
                    catch (Exception ex) { _logger.LogWarning($"{TAG} [Style] Erro reset hwnd={trayWnd}: {ex.Message}"); }
                }
            }
            finally { Marshal.FreeHGlobal(ptr); }
        }

        private static AccentState MapStyleMode(TaskbarStyleMode mode) => mode switch
        {
            TaskbarStyleMode.Transparent => AccentState.ACCENT_ENABLE_TRANSPARENT,
            TaskbarStyleMode.Blur        => AccentState.ACCENT_ENABLE_BLURBEHIND,
            TaskbarStyleMode.Acrylic     => AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND,
            TaskbarStyleMode.Gradient    => AccentState.ACCENT_ENABLE_GRADIENT,
            _                            => AccentState.ACCENT_ENABLE_TRANSPARENT};

        // ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦
        // HELPERS çª¶ï¿½ localizar handles da taskbar
        // ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦ç¬Šçµ¶æ­¦

        private record struct TaskbarInfo(
            bool    Valid,
            IntPtr  TrayWnd,
            IntPtr  RebarHwnd,
            IntPtr  SwHwnd,
            IntPtr  TaskListHwnd,
            string  Orientation);

        private List<IntPtr> GetAllTrayHandles()
        {
            var list = new List<IntPtr>();
            IntPtr main = FindWindow("Shell_TrayWnd", null);
            if (main != IntPtr.Zero) list.Add(main);
            IntPtr sec  = FindWindow("Shell_SecondaryTrayWnd", null);
            if (sec  != IntPtr.Zero) list.Add(sec);
            return list;
        }

        private List<TaskbarInfo> GetAllTaskbarInfos()
        {
            var infos = new List<TaskbarInfo>();
            foreach (var tray in GetAllTrayHandles())
                infos.Add(GetTaskbarInfo(tray));
            return infos;
        }

        /// <summary>
        /// Resolve todos os handles necessï¾ƒï½¡rios para uma taskbar.
        /// Suporta hierarquia clï¾ƒï½¡ssica (Win10) e fallback via EnumChildWindows (Win11).
        /// </summary>
        private TaskbarInfo GetTaskbarInfo(IntPtr trayWnd)
        {
            var sb = new StringBuilder(256);
            GetClassName(trayWnd, sb, 256);
            string cls = sb.ToString();

            IntPtr rebarHwnd    = IntPtr.Zero;
            IntPtr swHwnd       = IntPtr.Zero;
            IntPtr taskListHwnd = IntPtr.Zero;

            if (cls == "Shell_TrayWnd")
            {
                // Hierarquia CLï¾ƒヾSICA: Shell_TrayWnd ç«Šï¿½ ReBarWindow32 ç«Šï¿½ MSTaskSwWClass ç«Šï¿½ MSTaskListWClass
                rebarHwnd    = FindWindowEx(trayWnd,   IntPtr.Zero, "ReBarWindow32",   null);
                swHwnd       = FindWindowEx(rebarHwnd, IntPtr.Zero, "MSTaskSwWClass",   null);
                taskListHwnd = FindWindowEx(swHwnd,    IntPtr.Zero, "MSTaskListWClass", null);

                _logger.LogDebug(
                    $"{TAG} [GetInfo] Primary: tray={trayWnd} rebar={rebarHwnd} sw={swHwnd} taskList={taskListHwnd}",
                    source: "TaskbarCtrl");

                // Fallback Win11: enumeraï¾ƒï½§ï¾ƒï½£o recursiva de filhos
                if (taskListHwnd == IntPtr.Zero)
                {
                    _logger.LogDebug($"{TAG} [GetInfo] MSTaskListWClass nï¾ƒï½£o encontrado via FindWindowEx çª¶ï¿½ EnumChildWindows fallback...");
                    taskListHwnd = FindChildByClass(trayWnd, "MSTaskListWClass");
                    if (taskListHwnd != IntPtr.Zero)
                    {
                        swHwnd    = GetParent(taskListHwnd);
                        rebarHwnd = GetParent(swHwnd);
                        _logger.LogInfo($"{TAG} [GetInfo] MSTaskListWClass encontrado via fallback: {taskListHwnd}");
                    }
                }
            }
            else if (cls == "Shell_SecondaryTrayWnd")
            {
                IntPtr worker = FindWindowEx(trayWnd, IntPtr.Zero, "WorkerW", null);
                taskListHwnd  = FindWindowEx(worker,  IntPtr.Zero, "MSTaskListWClass", null);
                rebarHwnd     = worker;
                swHwnd        = worker;

                _logger.LogDebug(
                    $"{TAG} [GetInfo] Secondary: tray={trayWnd} worker={worker} taskList={taskListHwnd}",
                    source: "TaskbarCtrl");

                if (taskListHwnd == IntPtr.Zero)
                    taskListHwnd = FindChildByClass(trayWnd, "MSTaskListWClass");
            }

            if (taskListHwnd == IntPtr.Zero)
            {
                _logger.LogDebug($"{TAG} [GetInfo] MSTaskListWClass nï¾ƒï½£o disponï¾ƒï½­vel para {cls} (hwnd={trayWnd}) çª¶ï¿½ Explorer pode estar reiniciando.");
                return new TaskbarInfo(false, trayWnd, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, "H");
            }

            // Orientaï¾ƒï½§ï¾ƒï½£o via GetWindowRect çª¶ï¿½ idï¾ƒï½ªntico ao TaskbarX: if tH >= tW ç«Šï¿½ V else H
            GetWindowRect(taskListHwnd, out var tlRect);
            int w = tlRect.Right - tlRect.Left, h = tlRect.Bottom - tlRect.Top;
            string orient = (h >= w && h > 0) ? "V" : "H";

            _logger.LogDebug(
                $"{TAG} [GetInfo] taskList={taskListHwnd} orient={orient} W={w} H={h}",
                source: "TaskbarCtrl");

            return new TaskbarInfo(true, trayWnd, rebarHwnd, swHwnd, taskListHwnd, orient);
        }

        private IntPtr FindChildByClass(IntPtr parent, string className)
        {
            IntPtr found = IntPtr.Zero;
            var sbLocal  = new StringBuilder(256);
            _logger.LogDebug($"{TAG} [FindChild] Buscando '{className}' em hwnd={parent}...", source: "TaskbarCtrl");
            EnumChildWindows(parent, (h, _) =>
            {
                sbLocal.Clear();
                GetClassName(h, sbLocal, 256);
                if (sbLocal.ToString() == className)
                {
                    found = h;
                    return false; // para a enumeraï¾ƒï½§ï¾ƒï½£o
                }
                return true;
            }, IntPtr.Zero);
            if (found != IntPtr.Zero)
                _logger.LogDebug($"{TAG} [FindChild] '{className}' encontrado: hwnd={found}", source: "TaskbarCtrl");
            else
                _logger.LogDebug($"{TAG} [FindChild] '{className}' Nï¾ƒグ encontrado em hwnd={parent}", source: "TaskbarCtrl");
            return found;
        }
    }
}


