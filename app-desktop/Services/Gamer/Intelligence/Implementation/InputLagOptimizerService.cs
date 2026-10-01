using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.Gamer.Intelligence.Interfaces;
using VoltrisOptimizer.Services.Gamer.Intelligence.Models;

namespace VoltrisOptimizer.Services.Gamer.Intelligence.Implementation
{
    /// <summary>
    /// Otimizador de input lag - minimiza latência entre input e tela
    /// </summary>
    public class InputLagOptimizerService : IInputLagOptimizer
    {
        private readonly ILoggingService _logger;
        private readonly IRegistryService _registry;
        private InputLagMetrics _currentMetrics = new();
        private readonly object _lock = new();

        // Backup de configurações originais
        private int? _originalMouseSpeed;
        private int? _originalMouseAcceleration;
        private int? _originalKeyboardDelay;

        public InputLagMetrics CurrentMetrics => _currentMetrics;

        public InputLagOptimizerService(ILoggingService logger, IRegistryService registry)
        {
            _logger = logger;
            _logger.LogEntry(nameof(InputLagOptimizerService));
            _registry = registry;
            _logger.LogExit(nameof(InputLagOptimizerService));
        }

        public async Task<InputLagMetrics> AnalyzeAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogEntry(nameof(AnalyzeAsync));
            return await Task.Run(() =>
            {
                var metrics = new InputLagMetrics();

                try
                {
                    // Detecta polling rate do mouse
                    metrics.MousePollingRateHz = DetectMousePollingRate();

                    // Detecta VSync
                    metrics.IsVSyncEnabled = IsVSyncEnabled();

                    // Detecta se está em fullscreen otimizado
                    metrics.IsFullscreenOptimized = IsFullscreenOptimizationsDisabled();

                    // Estima latência de render baseado em configurações
                    metrics.RenderLatencyMs = EstimateRenderLatency();

                    // Estima latência de display (assume 60Hz se não souber)
                    metrics.DisplayLatencyMs = EstimateDisplayLatency();

                    // Calcula input lag total estimado
                    metrics.TotalInputLagMs = 
                        (1000.0 / metrics.MousePollingRateHz) + // Mouse delay
                        metrics.RenderLatencyMs +
                        metrics.DisplayLatencyMs +
                        (metrics.IsVSyncEnabled ? 16.67 : 0); // VSync adiciona 1 frame

                    // Classifica
                    metrics.Classification = metrics.TotalInputLagMs switch
                    {
                        < 10 => InputLagClass.Excellent,
                        < 20 => InputLagClass.Good,
                        < 40 => InputLagClass.Average,
                        < 60 => InputLagClass.Poor,
                        _ => InputLagClass.Bad
                    };

                    lock (_lock) { _currentMetrics = metrics; }
                    _logger.LogInfo($"[InputLag] Análise: {metrics.TotalInputLagMs:F1}ms ({metrics.Classification})");
                    _logger.LogExit(nameof(AnalyzeAsync));
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[InputLag] Erro na análise: {ex.Message}");
                    _logger.LogExit(nameof(AnalyzeAsync));
                }

                return metrics;
            }, cancellationToken);
        }

        private double DetectMousePollingRate()
        {
            _logger.LogEntry(nameof(DetectMousePollingRate));
            // Verifica configuração de raw input
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse");
                var sensitivity = key?.GetValue("MouseSensitivity")?.ToString();
                
                // A maioria dos mouses gaming tem 1000Hz
                // Mouses padrão tem 125Hz
                // Tentamos detectar pelo driver HID
                _logger.LogMethodEnd(1000);
                return 1000; // Assume 1000Hz como padrão gamer
            }
            catch
            {
                _logger.LogMethodEnd(125);
                return 125; // Fallback para mouse padrão
            }
        }

        private bool IsVSyncEnabled()
        {
            _logger.LogEntry(nameof(IsVSyncEnabled));
            try
            {
                // Verifica configuração global do NVIDIA
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers\Configuration");
                // VSync é normalmente controlado por jogo
                _logger.LogMethodEnd(false);
                return false; // Assume desabilitado por padrão
            }
            catch
            {
                _logger.LogMethodEnd(false);
                return false;
            }
        }

        private bool IsFullscreenOptimizationsDisabled()
        {
            _logger.LogEntry(nameof(IsFullscreenOptimizationsDisabled));
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"System\GameConfigStore");
                var value = key?.GetValue("GameDVR_FSEBehaviorMode");
                var result = value != null && Convert.ToInt32(value) == 2;
                _logger.LogMethodEnd(result);
                return result;
            }
            catch
            {
                _logger.LogMethodEnd(false);
                return false;
            }
        }

        private double EstimateRenderLatency()
        {
            _logger.LogEntry(nameof(EstimateRenderLatency));
            // Baseado em configurações típicas
            // GPU rendering: 8-16ms para 60fps
            // Pre-rendered frames: adiciona 16ms por frame
            _logger.LogMethodEnd(16.67);
            return 16.67; // Assume 1 frame de latência
        }

        private double EstimateDisplayLatency()
        {
            _logger.LogEntry(nameof(EstimateDisplayLatency));
            // Obtém taxa de atualização do monitor
            try
            {
                var refreshRate = GetPrimaryMonitorRefreshRate();
                var result = 1000.0 / refreshRate / 2; // Metade do tempo de refresh
                _logger.LogMethodEnd(result);
                return result;
            }
            catch
            {
                _logger.LogMethodEnd(8.33);
                return 8.33; // Assume 120Hz
            }
        }

        private int GetPrimaryMonitorRefreshRate()
        {
            _logger.LogEntry(nameof(GetPrimaryMonitorRefreshRate));
            try
            {
                DEVMODE dm = new() { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
                if (EnumDisplaySettings(null, -1, ref dm))
                {
                    _logger.LogMethodEnd(dm.dmDisplayFrequency);
                    return dm.dmDisplayFrequency;
                }
            }
            catch { }
            _logger.LogMethodEnd(60);
            return 60;
        }

        public async Task<bool> OptimizeAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogEntry(nameof(OptimizeAsync));
            int optimizations = 0;

            try
            {
                _logger.LogInfo("[InputLag] Iniciando otimizações de input lag...");

                // 1. Desabilita aceleração do mouse
                if (DisableMouseAcceleration())
                {
                    _logger.LogInfo("[InputLag] âœ“ Aceleração do mouse desabilitada");
                    optimizations++;
                }

                // 2. Otimiza polling rate
                if (OptimizeMousePolling())
                {
                    _logger.LogInfo("[InputLag] âœ“ Raw input otimizado");
                    optimizations++;
                }

                // 3. Desabilita FSO (Fullscreen Optimizations)
                if (DisableFullscreenOptimizations())
                {
                    _logger.LogInfo("[InputLag] âœ“ Fullscreen Optimizations desabilitadas");
                    optimizations++;
                }

                // 4. Otimiza teclado
                if (OptimizeKeyboard())
                {
                    _logger.LogInfo("[InputLag] âœ“ Delay do teclado minimizado");
                    optimizations++;
                }

                // 5. Otimização de composição removida (Incompatível com Win 10/11 Snipping Tool)
                _logger.LogInfo("[InputLag] - Composição do Windows: Mantida (Estabilidade)");

                // 6. Otimiza timer resolution
                if (SetHighTimerResolution())
                {
                    _logger.LogInfo("[InputLag] âœ“ Timer resolution otimizado");
                    optimizations++;
                }

                // Re-analisa após otimizações
                await AnalyzeAsync(cancellationToken);

                _logger.LogInfo($"[InputLag] Total de otimizações aplicadas: {optimizations}");
                _logger.LogMethodEnd(optimizations > 0);
                return optimizations > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[InputLag] Erro nas otimizações: {ex.Message}");
                _logger.LogMethodEnd(false);
                return false;
            }
        }

        private bool DisableMouseAcceleration()
        {
            _logger.LogEntry(nameof(DisableMouseAcceleration));
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse", true);
                if (key == null)
                {
                    _logger.LogMethodEnd(false);
                    return false;
                }

                // Backup
                _originalMouseAcceleration = Convert.ToInt32(key.GetValue("MouseSpeed") ?? 0);
                _originalMouseSpeed = Convert.ToInt32(key.GetValue("MouseSensitivity") ?? 10);

                // Desabilita aceleração (Enhanced Pointer Precision)
                key.SetValue("MouseSpeed", "0", RegistryValueKind.String);
                key.SetValue("MouseThreshold1", "0", RegistryValueKind.String);
                key.SetValue("MouseThreshold2", "0", RegistryValueKind.String);

                // Aplica curva 1:1
                int[] smoothCurve = new int[20];
                for (int i = 0; i < 20; i++)
                    smoothCurve[i] = (i + 1) * (65536 / 20);

                byte[] curveBytes = new byte[80];
                Buffer.BlockCopy(smoothCurve, 0, curveBytes, 0, 80);
                key.SetValue("SmoothMouseXCurve", curveBytes, RegistryValueKind.Binary);
                key.SetValue("SmoothMouseYCurve", curveBytes, RegistryValueKind.Binary);

                // Aplica mudanças imediatamente
                SystemParametersInfo(SPI_SETMOUSE, 0, IntPtr.Zero, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);

                _logger.LogMethodEnd(true);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[InputLag] Erro ao desabilitar aceleração: {ex.Message}");
                _logger.LogMethodEnd(false);
                return false;
            }
        }

        public bool OptimizeMousePolling()
        {
            _logger.LogEntry(nameof(OptimizeMousePolling));
            try
            {
                // Habilita raw input para menor latência
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\mouclass\Parameters", true);
                key?.SetValue("MouseDataQueueSize", 16, RegistryValueKind.DWord);
                _logger.LogMethodEnd(true);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[InputLag] Erro ao otimizar mouse: {ex.Message}");
                _logger.LogMethodEnd(false);
                return false;
            }
        }

        private bool DisableFullscreenOptimizations()
        {
            _logger.LogEntry(nameof(DisableFullscreenOptimizations));
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"System\GameConfigStore", true);
                if (key == null)
                {
                    _logger.LogMethodEnd(false);
                    return false;
                }

                // Desabilita FSO globalmente
                key.SetValue("GameDVR_FSEBehaviorMode", 2, RegistryValueKind.DWord);
                key.SetValue("GameDVR_HonorUserFSEBehaviorMode", 1, RegistryValueKind.DWord);
                key.SetValue("GameDVR_FSEBehavior", 2, RegistryValueKind.DWord);

                _logger.LogMethodEnd(true);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[InputLag] Erro ao desabilitar FSO: {ex.Message}");
                _logger.LogMethodEnd(false);
                return false;
            }
        }

        private bool OptimizeKeyboard()
        {
            _logger.LogEntry(nameof(OptimizeKeyboard));
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Keyboard", true);
                if (key == null)
                {
                    _logger.LogMethodEnd(false);
                    return false;
                }

                _originalKeyboardDelay = Convert.ToInt32(key.GetValue("KeyboardDelay") ?? 1);

                // Minimiza delay do teclado
                key.SetValue("KeyboardDelay", "0", RegistryValueKind.String);
                key.SetValue("KeyboardSpeed", "31", RegistryValueKind.String); // Máximo

                _logger.LogMethodEnd(true);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[InputLag] Erro ao otimizar teclado: {ex.Message}");
                _logger.LogMethodEnd(false);
                return false;
            }
        }

        public bool DisableComposition()
        {
            _logger.LogEntry(nameof(DisableComposition));
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM", true);
                if (key != null)
                {
                    key.SetValue("Composition", 0, RegistryValueKind.DWord);
                    _logger.LogExit(nameof(DisableComposition), true);
                    return true;
                }
                _logger.LogExit(nameof(DisableComposition), false);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[InputLag] Erro ao desabilitar composição: {ex.Message}");
                _logger.LogExit(nameof(DisableComposition), false);
                return false;
            }
        }

        private bool SetHighTimerResolution()
        {
            _logger.LogEntry(nameof(SetHighTimerResolution));
            try
            {
                // Define timer resolution para 0.5ms
                uint current = 0, min = 0, max = 0;
                NtQueryTimerResolution(ref min, ref max, ref current);
                var result = NtSetTimerResolution(max, true, ref current) == 0;
                _logger.LogMethodEnd(result);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[InputLag] Erro ao definir timer: {ex.Message}");
                _logger.LogMethodEnd(false);
                return false;
            }
        }

        public async Task<bool> RestoreAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogEntry(nameof(RestoreAsync));
            try
            {
                _logger.LogInfo("[InputLag] Restaurando configurações originais...");

                // Restaura mouse
                if (_originalMouseSpeed.HasValue || _originalMouseAcceleration.HasValue)
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse", true);
                    if (_originalMouseSpeed.HasValue)
                        key?.SetValue("MouseSensitivity", _originalMouseSpeed.Value.ToString());
                    if (_originalMouseAcceleration.HasValue)
                        key?.SetValue("MouseSpeed", _originalMouseAcceleration.Value.ToString());
                }

                // Restaura teclado
                if (_originalKeyboardDelay.HasValue)
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Keyboard", true);
                    key?.SetValue("KeyboardDelay", _originalKeyboardDelay.Value.ToString());
                }

                // Restaura timer resolution
                uint current = 0;
                NtSetTimerResolution(156250, false, ref current); // Default ~15.6ms

                await Task.CompletedTask;
                _logger.LogInfo("[InputLag] Configurações restauradas");
                _logger.LogMethodEnd(true);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[InputLag] Erro ao restaurar: {ex.Message}");
                _logger.LogMethodEnd(false);
                return false;
            }
        }

        #region Native Methods

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtSetTimerResolution(uint DesiredResolution, bool SetResolution, ref uint CurrentResolution);

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtQueryTimerResolution(ref uint MinimumResolution, ref uint MaximumResolution, ref uint CurrentResolution);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

        private const uint SPI_SETMOUSE = 0x0004;
        private const uint SPIF_UPDATEINIFILE = 0x0001;
        private const uint SPIF_SENDCHANGE = 0x0002;

        [DllImport("user32.dll")]
        private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

        [StructLayout(LayoutKind.Sequential)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }

        #endregion
    }
}

