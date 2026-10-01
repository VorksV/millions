using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using VoltrisOptimizer.Core;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Gamer.Overlay.Models;
using VoltrisOptimizer.Services.Gamer.Overlay.Implementation;
using VoltrisOptimizer.Services.Monitoring.Interfaces;

namespace VoltrisOptimizer.UI.Overlay
{
    public partial class ProfessionalGameOverlayWindow : Window, IDisposable
    {
        private OverlaySettings _settings;
        // ❌ REMOVIDO: GameHardwareMonitor desativado (V2 Architecture - usar SystemMetricsCache)
        private readonly ILoggingService? _logger;
        private readonly IMetricToggleService? _metricToggles;
        private DispatcherTimer _updateTimer;
        private readonly object _lock = new object();

        // ── Lifecycle tracking ──
        private long _updateCycleCount;
        private long _fpsEventCount;
        private long _lastFpsValueBits;
        private DateTime _lastFpsUpdate = DateTime.MinValue;
        private DateTime _lastHardwareUpdate = DateTime.MinValue;
        private DateTime _lastLogHeartbeat = DateTime.MinValue;

        // ── Update rates (inteligente por categoria) ──
        private const int FpsUpdateIntervalMs = 100;     // FPS: 100ms (quase real-time via ETW)
        private const int HwUpdateIntervalMs = 500;      // HW: 500ms (CPU/GPU/RAM)
        private const int SlowUpdateIntervalMs = 2000;   // Clocks/Temps: 2s

        // ── Win32: esconder do Alt+Tab ──
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int GWL_EXSTYLE = -20;

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        public ProfessionalGameOverlayWindow(
            OverlaySettings settings,
            // ❌ REMOVIDO: GameHardwareMonitor desativado (V2 Architecture)
            ILoggingService? logger = null,
            IMetricToggleService? metricToggles = null)
        {
            _settings = settings;
            _logger = logger;
            _metricToggles = metricToggles;

            _logger?.LogInfo("[PRO-GAME-WINDOW] ════════════════════════════════════════");
            _logger?.LogInfo("[PRO-GAME-WINDOW] 🚀 Inicializando Overlay HUD Enterprise");
            _logger?.LogInfo($"[PRO-GAME-WINDOW] Settings: FPS={settings.ShowFps}, HW={settings.ShowCpu || settings.ShowGpu}, Mem={settings.ShowMemory}");
            _logger?.LogInfo($"[PRO-GAME-WINDOW] SmartMetricToggles: {(metricToggles != null ? "✔️ ativo" : "❌ não injetado")}");
            _logger?.LogInfo($"[PRO-GAME-WINDOW] Update Rates: FPS={FpsUpdateIntervalMs}ms, HW={HwUpdateIntervalMs}ms, Slow={SlowUpdateIntervalMs}ms");

            InitializeComponent();
            ConfigureProfessionalWindow();
            SetupEventHandlers();
            SubscribeToMetricsCache();
            StartProfessionalUpdates();

            _logger?.LogSuccess("[PRO-GAME-WINDOW] ✅ Overlay HUD enterprise inicializado com sucesso");
        }

        private void ConfigureProfessionalWindow()
        {
            try
            {
                Title = "Voltris Performance HUD";
                WindowStyle = WindowStyle.None;
                Background = Brushes.Transparent;
                ShowInTaskbar = false;
                Topmost = true;

                Left = _settings.PositionX;
                Top = _settings.PositionY;
                Opacity = _settings.Opacity;

                Loaded += OnWindowLoaded;
                MouseLeftButtonDown += OnWindowMouseDown;
                KeyDown += OnWindowKeyDown;

                _logger?.LogInfo($"[PRO-GAME-WINDOW] Janela configurada em ({_settings.PositionX},{_settings.PositionY}), opacidade={_settings.Opacity}");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[PRO-GAME-WINDOW] ❌ Erro ao configurar janela: {ex.Message}", ex);
            }
        }

        private void SetupEventHandlers()
        {
            try
            {
                // ❌ REMOVIDO: GameHardwareMonitor desativado (V2 Architecture)
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[PRO-GAME-WINDOW] ❌ Erro ao configurar event handlers: {ex.Message}", ex);
            }
        }

        private bool _subscribedToCache;

        private void SubscribeToMetricsCache()
        {
            if (_subscribedToCache) return;
            try
            {
                SystemMetricsCache.Instance.MetricsUpdated += OnCacheMetricsUpdated;
                _subscribedToCache = true;
                _logger?.LogInfo("[PRO-GAME-WINDOW] 📡 Inscrito no SystemMetricsCache.MetricsUpdated (event-driven)");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[PRO-GAME-WINDOW] ❌ Erro ao inscrever no MetricsCache: {ex.Message}", ex);
            }
        }

        private void UnsubscribeFromMetricsCache()
        {
            if (!_subscribedToCache) return;
            try
            {
                SystemMetricsCache.Instance.MetricsUpdated -= OnCacheMetricsUpdated;
                _subscribedToCache = false;
                _logger?.LogInfo("[PRO-GAME-WINDOW] Desinscrito do SystemMetricsCache");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[PRO-GAME-WINDOW] ❌ Erro ao desinscrever do MetricsCache: {ex.Message}", ex);
            }
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                Topmost = true;
                ShowInTaskbar = false;
                Opacity = 0.95;

                // Esconder do Alt+Tab via WS_EX_TOOLWINDOW + WS_EX_NOACTIVATE
                try
                {
                    var helper = new WindowInteropHelper(this);
                    IntPtr hwnd = helper.Handle;
                    if (hwnd != IntPtr.Zero)
                    {
                        IntPtr exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                        SetWindowLong(hwnd, GWL_EXSTYLE, (IntPtr)((long)exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE));
                        _logger?.LogInfo("[PRO-GAME-WINDOW] WS_EX_TOOLWINDOW aplicado — janela oculta do Alt+Tab");
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError($"[PRO-GAME-WINDOW] ⚠️ Falha ao aplicar WS_EX_TOOLWINDOW: {ex.Message}");
                }

                ApplyVisibilitySettings();

                _logger?.LogInfo("[PRO-GAME-WINDOW] Janela carregada com sucesso");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[PRO-GAME-WINDOW] ❌ Erro ao carregar janela: {ex.Message}", ex);
            }
        }

        // ─────────────── EVENT-DRIVEN UPDATE (FPS em tempo real) ───────────────

        private void OnCacheMetricsUpdated(object? sender, EventArgs e)
        {
            try
            {
                var now = DateTime.UtcNow;
                var cache = SystemMetricsCache.Instance;

                // FPS: atualiza sempre (sem bloqueio, via InvokeAsync Background)
                UpdateFpsFromCache();
                Interlocked.Increment(ref _fpsEventCount);

                // Log heartbeat reduzido a cada 30s (apenas uma linha)
                if ((now - _lastLogHeartbeat).TotalSeconds >= 30)
                {
                    _lastLogHeartbeat = now;
                    long fpsEvts = Interlocked.Read(ref _fpsEventCount);
                    long cycles = Interlocked.Read(ref _updateCycleCount);
                    _logger?.LogInfo("[PRO-GAME-WINDOW] Heartbeat — FPS events: " + fpsEvts + ", cycles: " + cycles);
                }
            }
            catch (Exception ex)
            {
                long err = Interlocked.Increment(ref _updateCycleCount);
                if (err % 10 == 0)
                    _logger?.LogError("[PRO-GAME-WINDOW] Erro OnCacheMetricsUpdated: " + ex.Message, ex);
            }
        }

        private void UpdateFpsFromCache()
        {
            try
            {
                var cache = SystemMetricsCache.Instance;

                double fpsValue = cache.FpsAvailable ? cache.Fps : 0;
                bool available = cache.FpsAvailable;

                if (fpsValue > 0)
                {
                    Interlocked.Exchange(ref _lastFpsValueBits, BitConverter.DoubleToInt64Bits(fpsValue));
                }

                Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        if (!available)
                        {
                            if (FpsText != null) FpsText.Text = "\u2014";
                            if (FrameTimeText != null) FrameTimeText.Text = "\u2014";
                            if (AvgFpsText != null) AvgFpsText.Text = "\u2014";
                            if (EtwBadge != null) EtwBadge.Visibility = Visibility.Collapsed;
                            return;
                        }

                        if (FpsText != null)
                        {
                            FpsText.Text = fpsValue.ToString("F0");
                            FpsText.Foreground = FpsColor(fpsValue);
                        }

                        if (EtwBadge != null)
                            EtwBadge.Visibility = Visibility.Visible;

                        double frameTime = cache.FpsAverageFrametimeMs;
                        _lastFrameTimeMs = frameTime;
                        if (FrameTimeText != null)
                        {
                            FrameTimeText.Text = frameTime > 0 ? frameTime.ToString("F1") + "ms" : "\u2014";
                            FrameTimeText.Foreground = frameTime > 33.3
                                ? Brushes.Red
                                : frameTime > 16.7
                                    ? Brushes.Yellow
                                    : Brushes.Green;
                        }

                        if (AvgFpsText != null)
                        {
                            AvgFpsText.Text = string.Format(LocalizationService.Instance.GetString("OverlayAvgFpsFormat"), cache.FpsOnePercentLow.ToString("F0"));
                        }

                        UpdateStutterIndicator(frameTime, fpsValue);
                    }
                    catch { }
                }, DispatcherPriority.Background);
            }
            catch { }
        }

        // ─────────────── TIMER-BASED UPDATE (HW metrics em frequências otimizadas) ───────────────

        private bool _pendingUpdate;

        private void StartProfessionalUpdates()
        {
            try
            {
                _updateTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(HwUpdateIntervalMs)
                };
                _updateTimer.Tick += UpdateDisplay;
                _updateTimer.Start();

                _logger?.LogInfo($"[PRO-GAME-WINDOW] Timer iniciado: intervalo={HwUpdateIntervalMs}ms (HW/Slow updates)");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[PRO-GAME-WINDOW] ❌ Erro ao iniciar timer: {ex.Message}", ex);
            }
        }

        private void UpdateDisplay(object? sender, EventArgs e)
        {
            if (_pendingUpdate)
                return;
            _pendingUpdate = true;

            try
            {
                Interlocked.Increment(ref _updateCycleCount);
                var now = DateTime.UtcNow;

                // ✅ V2 Architecture: Usar SystemMetricsCache (reativo, zero polling)
                var cache = SystemMetricsCache.Instance;
                
                // Apenas atualiza HW se passou tempo suficiente (rate limiting por categoria)
                bool shouldUpdateHw = (now - _lastHardwareUpdate).TotalMilliseconds >= HwUpdateIntervalMs;

                if (shouldUpdateHw)
                {
                    _lastHardwareUpdate = now;
                    Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            UpdateHardwareDisplayFromCache(cache, now);
                        }
                        catch { }
                    }, DispatcherPriority.Background);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[PRO-GAME-WINDOW] ❌ Erro ao atualizar display: {ex.Message}", ex);
            }
            finally
            {
                _pendingUpdate = false;
            }
        }

        // ─────────────── HARDWARE DISPLAY (CPU, GPU, RAM, VRAM) ───────────────

        private void UpdateHardwareDisplayFromCache(SystemMetricsCache cache, DateTime now)
        {
            try
            {
                bool shouldUpdateSlow = (now - _lastSlowUpdate).TotalMilliseconds >= SlowUpdateIntervalMs;
                if (shouldUpdateSlow)
                    _lastSlowUpdate = now;

                // LOG resumido das métricas (a cada slow update ~2s)
                if (shouldUpdateSlow)
                {
                    _logger?.LogInfo($"[PRO-GAME-WINDOW] CPU: {cache.CpuPercent:F0}% @ {cache.CpuClockMhz / 1000.0:F2}GHz");
                }

                // ── CPU USAGE (sempre atualiza) ──
                if (CpuUsageText != null)
                {
                    CpuUsageText.Text = cache.CpuPercent.ToString("F0") + "%";
                    CpuUsageText.Foreground = UsageColor(cache.CpuPercent);
                }
                if (CpuBarFill != null)
                {
                    double cpuPct = Math.Min(cache.CpuPercent / 100.0, 1.0);
                    CpuBarFill.Width = CpuBarFill.Parent is Border parent ? parent.ActualWidth * cpuPct : 0;
                }

                if (shouldUpdateSlow)
                {
                    // ── CPU TEMPERATURE (stub - V2 Architecture não coleta temperatura diretamente)
                    if (CpuTempText != null)
                    {
                        CpuTempText.Text = "—";
                        CpuTempText.Foreground = Brushes.White;
                        CpuTempText.Visibility = _settings.Metrics.ShowCpuTemperature
                            ? Visibility.Visible : Visibility.Collapsed;
                    }

                    // ── CPU CLOCK ──
                    if (CpuClockText != null)
                    {
                        CpuClockText.Text = $"{cache.CpuClockMhz / 1000.0:F2}GHz";
                        CpuClockText.Visibility = _settings.Metrics.ShowCpuClock
                            ? Visibility.Visible : Visibility.Collapsed;
                    }

                    if (CpuMaxClockText != null && CpuClockSep != null && CpuClockText != null)
                    {
                        bool showTurbo = _settings.Metrics.ShowCpuClock && cache.CpuMaxClockMhz > 0;
                        if (showTurbo)
                        {
                            CpuMaxClockText.Text = $"{cache.CpuMaxClockMhz / 1000.0:F2}GHz";
                            CpuMaxClockText.Visibility = Visibility.Visible;
                            CpuClockSep.Visibility = Visibility.Visible;
                        }
                        else
                        {
                            CpuMaxClockText.Visibility = Visibility.Collapsed;
                            CpuClockSep.Visibility = Visibility.Collapsed;
                        }
                    }

                    // ── GPU TEMPERATURE (stub - V2 Architecture não coleta temperatura diretamente)
                    if (GpuTempText != null)
                    {
                        GpuTempText.Text = "—";
                        GpuTempText.Foreground = Brushes.White;
                        GpuTempText.Visibility = _settings.Metrics.ShowGpuTemperature
                            ? Visibility.Visible : Visibility.Collapsed;
                    }

                    // ── GPU CLOCK ──
                    if (GpuClockText != null)
                    {
                        if (!_settings.Metrics.ShowGpuClock)
                        {
                            GpuClockText.Visibility = Visibility.Collapsed;
                        }
                        else if (cache.GpuCoreClockMhz > 0)
                        {
                            GpuClockText.Text = $"{cache.GpuCoreClockMhz:F0}MHz";
                            GpuClockText.Foreground = Brushes.LimeGreen;
                            GpuClockText.Visibility = Visibility.Visible;
                        }
                        else
                        {
                            GpuClockText.Text = "⏳";
                            GpuClockText.Foreground = Brushes.Gray;
                            GpuClockText.Visibility = Visibility.Visible;
                        }
                    }
                }

                // ── GPU USAGE (sempre atualiza) ──
                if (GpuUsageText != null)
                {
                    GpuUsageText.Text = $"{cache.GpuUsagePercent:F0}%";
                    GpuUsageText.Foreground = UsageColor(cache.GpuUsagePercent);
                }
                if (GpuBarFill != null)
                {
                    double gpuPct = Math.Min(cache.GpuUsagePercent / 100.0, 1.0);
                    GpuBarFill.Width = GpuBarFill.Parent is Border parent ? parent.ActualWidth * gpuPct : 0;
                }

                // ── RAM (sempre atualiza) ──
                if (RamUsageText != null)
                    RamUsageText.Text = $"{cache.MemoryUsedPercent:F1}%";
                if (RamBarFill != null)
                {
                    double ramPct = Math.Min(cache.MemoryUsedPercent / 100.0, 1.0);
                    RamBarFill.Width = RamBarFill.Parent is Border parent ? parent.ActualWidth * ramPct : 0;
                }

                // ── VRAM (sempre atualiza) ──
                if (VramUsageText != null)
                    VramUsageText.Text = $"{cache.GpuVramUsedGb:F1}GB";

                // ── Input Latency ──
                if (InputLatencyText != null && _settings.Metrics.ShowInputLatency)
                {
                    double lastInputMs = cache.LastInputMs;
                    if (lastInputMs >= 0 && lastInputMs < 5000)
                    {
                        double estimatedLatency = Math.Max(5.0, Math.Min(100.0, lastInputMs * 0.1));
                        InputLatencyText.Text = string.Format(LocalizationService.Instance.GetString("OverlayLatencyFormat"), $"{estimatedLatency:F0}");
                        InputLatencyText.Visibility = Visibility.Visible;
                        InputLatencyText.Foreground = estimatedLatency > 50
                            ? Brushes.Red
                            : estimatedLatency > 30
                                ? Brushes.Orange
                                : Brushes.Green;
                    }
                    else
                    {
                        InputLatencyText.Visibility = Visibility.Collapsed;
                    }
                }
                else if (InputLatencyText != null)
                {
                    InputLatencyText.Visibility = Visibility.Collapsed;
                }

                // ── Capture Info (ETW badge) ──
                if (CaptureInfoText != null)
                {
                    CaptureInfoText.Text = cache.FpsAvailable ? "ETW" : "—";
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[PRO-GAME-WINDOW] ❌ Erro ao atualizar display de hardware: {ex.Message}", ex);
            }
        }

        // ❌ REMOVIDO: UpdateHardwareDisplay(GameHardwareMonitor.GameHardwareMetrics) - V2 Architecture

        private DateTime _lastSlowUpdate = DateTime.MinValue;

        // ─────────────── FPS DISPLAY ───────────────

        private double _lastFrameTimeMs = 0;

        private void OnHardwareUpdated(object? sender, GameHardwareMonitor.GameHardwareUpdatedEventArgs e)
        {
            // Timer + cache events handle all display updates
        }

        // ═══════════════════════════════════════════
        //  SMART COLOR CACHE (Pre-frozen to avoid GC pressure)
        // ═══════════════════════════════════════════
        private static readonly SolidColorBrush _fpsGreenBrush = CreateFrozenBrush(Color.FromRgb(0, 230, 118));
        private static readonly SolidColorBrush _fpsYellowBrush = CreateFrozenBrush(Color.FromRgb(255, 215, 64));
        private static readonly SolidColorBrush _fpsRedBrush = CreateFrozenBrush(Color.FromRgb(255, 82, 82));

        private static readonly SolidColorBrush _usageLowBrush = CreateFrozenBrush(Color.FromRgb(224, 224, 224));
        private static readonly SolidColorBrush _usageMedBrush = CreateFrozenBrush(Color.FromRgb(255, 145, 0));
        private static readonly SolidColorBrush _usageHighBrush = _fpsRedBrush;

        private static readonly SolidColorBrush _tempLowBrush = CreateFrozenBrush(Color.FromRgb(176, 190, 197));
        private static readonly SolidColorBrush _tempMedBrush = _usageMedBrush;
        private static readonly SolidColorBrush _tempHighBrush = _fpsRedBrush;

        private static SolidColorBrush CreateFrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private static SolidColorBrush FpsColor(double fps)
        {
            if (fps >= 60) return _fpsGreenBrush;
            if (fps >= 30) return _fpsYellowBrush;
            return _fpsRedBrush;
        }

        private static SolidColorBrush UsageColor(double pct)
        {
            if (pct < 70) return _usageLowBrush;
            if (pct < 90) return _usageMedBrush;
            return _usageHighBrush;
        }

        private static SolidColorBrush TempColor(double temp)
        {
            if (temp < 60) return _tempLowBrush;
            if (temp < 80) return _tempMedBrush;
            return _tempHighBrush;
        }

        private void UpdateStutterIndicator(double frameTimeMs, double fps)
        {
            if (StutterDot == null) return;

            bool isCritical = frameTimeMs > 50 || fps < 20;
            bool isWarning = frameTimeMs > 33.3 || fps < 30;

            if (isCritical)
            {
                StutterDot.Fill = (SolidColorBrush)TryFindResource("CriticalBrush") ?? Brushes.Red;
                StutterDot.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 6,
                    ShadowDepth = 0,
                    Color = Colors.Red,
                    Opacity = 0.8
                };
            }
            else if (isWarning)
            {
                StutterDot.Fill = (SolidColorBrush)TryFindResource("UnstableBrush") ?? Brushes.Yellow;
                StutterDot.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 4,
                    ShadowDepth = 0,
                    Color = Colors.Yellow,
                    Opacity = 0.5
                };
            }
            else
            {
                StutterDot.Fill = (SolidColorBrush)TryFindResource("StableBrush") ?? Brushes.Green;
                StutterDot.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 4,
                    ShadowDepth = 0,
                    Color = Color.FromRgb(0, 230, 118),
                    Opacity = 0.6
                };
            }
        }

        // ─────────────── SETTINGS / VISIBILITY ───────────────

        public void Dispose()
        {
            try
            {
                UnsubscribeFromMetricsCache();

                // ❌ REMOVIDO: GameHardwareMonitor desativado

                _updateTimer?.Stop();
                _updateTimer = null;

                _logger?.LogInfo("[PRO-GAME-WINDOW] 🧹 Recursos liberados — eventos desvinculados, timer parado");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[PRO-GAME-WINDOW] ❌ Erro ao realizar Dispose: {ex.Message}", ex);
            }
        }

        private void OnWindowMouseDown(object sender, MouseButtonEventArgs e)
        {
            try { DragMove(); }
            catch { }
        }

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                _logger?.LogInfo("[PRO-GAME-WINDOW] Tecla ESC pressionada — fechando overlay");
                Close();
            }
        }

        public void UpdateSettings(OverlaySettings newSettings)
        {
            try
            {
                lock (_lock)
                {
                    _settings = newSettings;
                }

                // ⚠️ Dispatcher.Invoke fora do lock para evitar deadlock!
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        lock (_lock)
                        {
                            var s = _settings;
                            if (s.PositionX > 0 && s.PositionY > 0)
                            {
                                Left = s.PositionX;
                                Top = s.PositionY;
                            }
                            Opacity = s.Opacity;
                        }
                        ApplyVisibilitySettings();
                    }
                    catch (Exception innerEx)
                    {
                        _logger?.LogError($"[PRO-GAME-WINDOW] ❌ Erro no dispatcher UpdateSettings: {innerEx.Message}", innerEx);
                    }
                });

                _logger?.LogInfo($"[PRO-GAME-WINDOW] Configurações atualizadas: opacity={newSettings.Opacity}, pos=({newSettings.PositionX},{newSettings.PositionY})");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[PRO-GAME-WINDOW] ❌ Erro ao atualizar configurações: {ex.Message}", ex);
            }
        }

        private void ApplyVisibilitySettings()
        {
            if (_settings == null)
            {
                _logger?.LogWarning("[PRO-GAME-WINDOW] ⚠️ ApplyVisibilitySettings: _settings é NULL!");
                return;
            }

            try
            {
                var metrics = _settings.Metrics;
                if (metrics == null)
                {
                    _logger?.LogWarning("[PRO-GAME-WINDOW] ⚠️ ApplyVisibilitySettings: Metrics é NULL!");
                    return;
                }

                _logger?.LogInfo("[PRO-GAME-WINDOW] ════════════════════════════════════════");
                _logger?.LogInfo("[PRO-GAME-WINDOW] 👁️ ApplyVisibilitySettings INICIADO");
                _logger?.LogInfo($"[PRO-GAME-WINDOW] 📋 metrics.ShowFps={metrics.ShowFps}, metrics.ShowFrameTime={metrics.ShowFrameTime}");
                _logger?.LogInfo($"[PRO-GAME-WINDOW] 📋 metrics.ShowCpuUsage={metrics.ShowCpuUsage}, metrics.ShowCpuTemperature={metrics.ShowCpuTemperature}, metrics.ShowCpuClock={metrics.ShowCpuClock}");
                _logger?.LogInfo($"[PRO-GAME-WINDOW] 📋 metrics.ShowGpuUsage={metrics.ShowGpuUsage}, metrics.ShowGpuTemperature={metrics.ShowGpuTemperature}, metrics.ShowGpuClock={metrics.ShowGpuClock}");
                _logger?.LogInfo($"[PRO-GAME-WINDOW] 📋 metrics.ShowRamUsage={metrics.ShowRamUsage}, metrics.ShowVramUsage={metrics.ShowVramUsage}");
                _logger?.LogInfo($"[PRO-GAME-WINDOW] 📋 metrics.ShowInputLatency={metrics.ShowInputLatency}");

                // Block 1: FPS
                FpsPanel.Visibility = metrics.ShowFps ? Visibility.Visible : Visibility.Collapsed;
                _logger?.LogInfo($"[PRO-GAME-WINDOW]   FPS Panel: {FpsPanel.Visibility}");

                // ETW badge visibility follows FPS
                if (EtwBadge != null)
                {
                    bool fpsDataAvailable = SystemMetricsCache.Instance.FpsAvailable;
                    EtwBadge.Visibility = (metrics.ShowFps && fpsDataAvailable) ? Visibility.Visible : Visibility.Collapsed;
                }

                // Block 2: CPU / GPU
                bool showCpu = metrics.ShowCpuUsage || metrics.ShowCpuTemperature || metrics.ShowCpuClock;
                bool showGpu = metrics.ShowGpuUsage || metrics.ShowGpuTemperature || metrics.ShowGpuClock;
                CpuPanel.Visibility = showCpu ? Visibility.Visible : Visibility.Collapsed;
                GpuPanel.Visibility = showGpu ? Visibility.Visible : Visibility.Collapsed;
                HardwareGrid.Visibility = (showCpu || showGpu) ? Visibility.Visible : Visibility.Collapsed;
                _logger?.LogInfo($"[PRO-GAME-WINDOW]   CPU Panel: {CpuPanel.Visibility} (showCpu={showCpu}), GPU Panel: {GpuPanel.Visibility} (showGpu={showGpu})");

                if (CpuUsageText != null)
                    CpuUsageText.Visibility = metrics.ShowCpuUsage ? Visibility.Visible : Visibility.Collapsed;
                if (CpuClockText != null)
                    CpuClockText.Visibility = metrics.ShowCpuClock ? Visibility.Visible : Visibility.Collapsed;
                if (CpuTempText != null)
                    CpuTempText.Visibility = metrics.ShowCpuTemperature ? Visibility.Visible : Visibility.Collapsed;
                if (CpuMaxClockText != null)
                    CpuMaxClockText.Visibility = metrics.ShowCpuClock ? Visibility.Visible : Visibility.Collapsed;
                if (CpuClockSep != null)
                    CpuClockSep.Visibility = metrics.ShowCpuClock ? Visibility.Visible : Visibility.Collapsed;

                if (GpuUsageText != null)
                    GpuUsageText.Visibility = metrics.ShowGpuUsage ? Visibility.Visible : Visibility.Collapsed;
                if (GpuClockText != null)
                    GpuClockText.Visibility = metrics.ShowGpuClock ? Visibility.Visible : Visibility.Collapsed;
                if (GpuTempText != null)
                    GpuTempText.Visibility = metrics.ShowGpuTemperature ? Visibility.Visible : Visibility.Collapsed;

                // Block 3: Memory
                if (RamPanel != null)
                    RamPanel.Visibility = metrics.ShowRamUsage ? Visibility.Visible : Visibility.Collapsed;
                if (VramPanel != null)
                    VramPanel.Visibility = metrics.ShowVramUsage ? Visibility.Visible : Visibility.Collapsed;
                if (MemoryGrid != null)
                    MemoryGrid.Visibility = (metrics.ShowRamUsage || metrics.ShowVramUsage)
                        ? Visibility.Visible : Visibility.Collapsed;
                _logger?.LogInfo($"[PRO-GAME-WINDOW]   RAM Panel: {(RamPanel?.Visibility.ToString() ?? "null")}, VRAM Panel: {(VramPanel?.Visibility.ToString() ?? "null")}");

                // Status panel (latency)
                if (StatusPanel != null)
                {
                    StatusPanel.Visibility = metrics.ShowInputLatency
                        ? Visibility.Visible : Visibility.Collapsed;
                    _logger?.LogInfo($"[PRO-GAME-WINDOW]   Status Panel (latency): {StatusPanel.Visibility}");
                }

                // Update accent colors
                if (!string.IsNullOrEmpty(_settings.TextColor))
                {
                    try
                    {
                        var accentColor = (Color)ColorConverter.ConvertFromString(_settings.TextColor);
                        Resources["AccentBrush"] = new SolidColorBrush(accentColor);
                        _logger?.LogInfo($"[PRO-GAME-WINDOW]   TextColor atualizado: {_settings.TextColor}");
                    }
                    catch (Exception ex) { _logger?.LogWarning($"[PRO-GAME-WINDOW]   TextColor inválido: {_settings.TextColor} — {ex.Message}"); }
                }

                if (!string.IsNullOrEmpty(_settings.BackgroundColor))
                {
                    try
                    {
                        var bgColor = (Color)ColorConverter.ConvertFromString(_settings.BackgroundColor);
                        Resources["BgRootBrush"] = new SolidColorBrush(bgColor);
                        _logger?.LogInfo($"[PRO-GAME-WINDOW]   BackgroundColor atualizado: {_settings.BackgroundColor}");
                    }
                    catch (Exception ex) { _logger?.LogWarning($"[PRO-GAME-WINDOW]   BackgroundColor inválido: {_settings.BackgroundColor} — {ex.Message}"); }
                }

                _logger?.LogSuccess("[PRO-GAME-WINDOW] ✅ ApplyVisibilitySettings concluído com sucesso");

                // LOG FINAL: estado visível real de cada elemento crítico
                _logger?.LogInfo($"[PRO-GAME-WINDOW][VIS-DEBUG] Final: FpsPanel={FpsPanel?.Visibility}, CpuClockText={CpuClockText?.Visibility}, CpuMaxClockText={CpuMaxClockText?.Visibility}, GpuClockText={GpuClockText?.Visibility}, StatusPanel={StatusPanel?.Visibility}");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[PRO-GAME-WINDOW] ❌ Erro em ApplyVisibilitySettings: {ex.Message}", ex);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                _logger?.LogInfo("[PRO-GAME-WINDOW] Janela fechando — salvando posição...");

                Dispose();
                _updateTimer?.Stop();

                _settings.PositionX = (int)Left;
                _settings.PositionY = (int)Top;
                _settings.SaveToFile();

                _logger?.LogInfo($"[PRO-GAME-WINDOW] Posição salva: ({Left},{Top})");
                base.OnClosed(e);
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[PRO-GAME-WINDOW] ❌ Erro ao fechar janela: {ex.Message}", ex);
            }
        }
    }
}
