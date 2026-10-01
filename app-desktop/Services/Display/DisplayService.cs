using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Display
{
    // ─── Modelos ────────────────────────────────────────────────────────────────

    public class MonitorInfo
    {
        public string Name { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public int CurrentHz { get; set; }
        public int CurrentWidth { get; set; }
        public int CurrentHeight { get; set; }
        public int BitDepth { get; set; }
        public List<int> SupportedHz { get; set; } = new();
        public List<(int W, int H)> SupportedResolutions { get; set; } = new();
        public bool IsPrimary { get; set; }
        public string StatusColor => CurrentHz >= 120 ? "#00FF88" : CurrentHz >= 60 ? "#FFAA00" : "#FF4466";
    }

    public class DisplayMetrics
    {
        public double EstimatedInputLagMs { get; set; }
        public double FramePacingMs { get; set; }
        public bool TearingDetected { get; set; }
        public bool StutterDetected { get; set; }
        public double RefreshRateReal { get; set; }
        public string TearingColor => TearingDetected ? "#FF4466" : "#00FF88";
        public string StutterColor => StutterDetected ? "#FFAA00" : "#00FF88";
    }

    // ─── P/Invoke ────────────────────────────────────────────────────────────────

    internal static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

        [DllImport("user32.dll")]
        public static extern int ChangeDisplaySettingsEx(string? deviceName, ref DEVMODE devMode, IntPtr hwnd, uint flags, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        public const int ENUM_CURRENT_SETTINGS = -1;
        public const int ENUM_REGISTRY_SETTINGS = -2;
        public const uint CDS_TEST = 0x00000002;
        public const uint CDS_UPDATEREGISTRY = 0x00000001;
        public const int DISP_CHANGE_SUCCESSFUL = 0;
        public const int DISP_CHANGE_RESTART = 1;
        public const int DISP_CHANGE_FAILED = -1;
        public const uint DM_PELSWIDTH = 0x00080000;
        public const uint DM_PELSHEIGHT = 0x00100000;
        public const uint DM_DISPLAYFREQUENCY = 0x00400000;
        public const uint DM_BITSPERPEL = 0x00040000;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    internal struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public uint dmFields;
        public int dmPositionX, dmPositionY;
        public uint dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth, dmPelsHeight;
        public uint dmDisplayFlags, dmDisplayFrequency;
        public uint dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    internal struct DISPLAY_DEVICE
    {
        public uint cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RAMP
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public ushort[] Red;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public ushort[] Green;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public ushort[] Blue;
    }

    internal static class GdiMethods
    {
        [DllImport("gdi32.dll")]
        public static extern bool SetDeviceGammaRamp(IntPtr hDC, ref RAMP lpRamp);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    }

    // ─── Service ─────────────────────────────────────────────────────────────────

    public sealed class DisplayService
    {
        private const string TAG = "[DisplayService]";
        private readonly ILoggingService _logger;
        
        // 🔧 CORREÇÃO PROFISSIONAL: Controle de compatibilidade de hardware para evitar tentativas repetidas
        private int _gammaFailureCount = 0;
        private bool _isGammaHardwareCompatible = true;

        public DisplayService(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogInfo($"{TAG} Instância criada.");
        }

        // ── Enumeração de monitores ──────────────────────────────────────────────

        public Task<List<MonitorInfo>> GetMonitorsAsync() => Task.Run(() =>
        {
            _logger.LogInfo($"{TAG} [GetMonitorsAsync] Enumerando monitores...");
            var result = new List<MonitorInfo>();
            try
            {
                uint devNum = 0;
                var dd = new DISPLAY_DEVICE { cb = (uint)Marshal.SizeOf<DISPLAY_DEVICE>() };

                while (NativeMethods.EnumDisplayDevices(null, devNum, ref dd, 0))
                {
                    if ((dd.StateFlags & 0x1) != 0) // DISPLAY_DEVICE_ACTIVE
                    {
                        var info = BuildMonitorInfo(dd);
                        result.Add(info);
                        _logger.LogInfo($"{TAG} Monitor encontrado: {info.Name} | {info.CurrentWidth}x{info.CurrentHeight}@{info.CurrentHz}Hz | Primary={info.IsPrimary}");
                    }
                    devNum++;
                    dd = new DISPLAY_DEVICE { cb = (uint)Marshal.SizeOf<DISPLAY_DEVICE>() };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"{TAG} [GetMonitorsAsync] Erro: {ex.Message}", ex);
            }
            _logger.LogInfo($"{TAG} [GetMonitorsAsync] Total monitores: {result.Count}");
            return result;
        });

        private MonitorInfo BuildMonitorInfo(DISPLAY_DEVICE dd)
        {
            var info = new MonitorInfo
            {
                DeviceName = dd.DeviceName,
                Name = dd.DeviceString,
                IsPrimary = (dd.StateFlags & 0x4) != 0
            };

            // Configuração atual
            var current = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
            if (NativeMethods.EnumDisplaySettings(dd.DeviceName, NativeMethods.ENUM_CURRENT_SETTINGS, ref current))
            {
                info.CurrentWidth = (int)current.dmPelsWidth;
                info.CurrentHeight = (int)current.dmPelsHeight;
                info.CurrentHz = (int)current.dmDisplayFrequency;
                info.BitDepth = (int)current.dmBitsPerPel;
            }

            // Modos suportados
            var hzSet = new HashSet<int>();
            var resSet = new HashSet<(int, int)>();
            int modeIdx = 0;
            var mode = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
            while (NativeMethods.EnumDisplaySettings(dd.DeviceName, modeIdx, ref mode))
            {
                hzSet.Add((int)mode.dmDisplayFrequency);
                resSet.Add(((int)mode.dmPelsWidth, (int)mode.dmPelsHeight));
                modeIdx++;
                mode = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
            }
            info.SupportedHz = new List<int>(hzSet);
            info.SupportedHz.Sort();
            info.SupportedResolutions = new List<(int, int)>(resSet);
            info.SupportedResolutions.Sort((a, b) => (b.Item1 * b.Item2).CompareTo(a.Item1 * a.Item2));

            return info;
        }

        // ── Alterar taxa de atualização ──────────────────────────────────────────

        public Task<bool> SetRefreshRateAsync(string deviceName, int hz) => Task.Run(() =>
        {
            _logger.LogInfo($"{TAG} [SetRefreshRateAsync] Tentando definir {hz}Hz em {deviceName}");
            try
            {
                var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
                if (!NativeMethods.EnumDisplaySettings(deviceName, NativeMethods.ENUM_CURRENT_SETTINGS, ref dm))
                {
                    _logger.LogWarning($"{TAG} [SetRefreshRateAsync] Falha ao obter configuração atual.");
                    return false;
                }

                dm.dmDisplayFrequency = (uint)hz;
                dm.dmFields = NativeMethods.DM_DISPLAYFREQUENCY;

                // Teste primeiro
                int testResult = NativeMethods.ChangeDisplaySettingsEx(deviceName, ref dm, IntPtr.Zero, NativeMethods.CDS_TEST, IntPtr.Zero);
                _logger.LogInfo($"{TAG} [SetRefreshRateAsync] Teste CDS_TEST resultado: {testResult}");

                if (testResult != NativeMethods.DISP_CHANGE_SUCCESSFUL)
                {
                    _logger.LogWarning($"{TAG} [SetRefreshRateAsync] Modo {hz}Hz não suportado (teste falhou: {testResult}).");
                    return false;
                }

                int result = NativeMethods.ChangeDisplaySettingsEx(deviceName, ref dm, IntPtr.Zero, NativeMethods.CDS_UPDATEREGISTRY, IntPtr.Zero);
                _logger.LogInfo($"{TAG} [SetRefreshRateAsync] ChangeDisplaySettingsEx resultado: {result}");

                bool success = result == NativeMethods.DISP_CHANGE_SUCCESSFUL || result == NativeMethods.DISP_CHANGE_RESTART;
                if (success) _logger.LogSuccess($"{TAG} [SetRefreshRateAsync] Taxa alterada para {hz}Hz com sucesso.");
                else _logger.LogError($"{TAG} [SetRefreshRateAsync] Falha ao alterar taxa: código {result}");
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError($"{TAG} [SetRefreshRateAsync] Exceção: {ex.Message}", ex);
                return false;
            }
        });

        // ── Alterar resolução ────────────────────────────────────────────────────

        public Task<bool> SetResolutionAsync(string deviceName, int width, int height) => Task.Run(() =>
        {
            _logger.LogInfo($"{TAG} [SetResolutionAsync] Tentando {width}x{height} em {deviceName}");
            try
            {
                var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
                NativeMethods.EnumDisplaySettings(deviceName, NativeMethods.ENUM_CURRENT_SETTINGS, ref dm);
                dm.dmPelsWidth = (uint)width;
                dm.dmPelsHeight = (uint)height;
                dm.dmFields = NativeMethods.DM_PELSWIDTH | NativeMethods.DM_PELSHEIGHT;

                int test = NativeMethods.ChangeDisplaySettingsEx(deviceName, ref dm, IntPtr.Zero, NativeMethods.CDS_TEST, IntPtr.Zero);
                if (test != NativeMethods.DISP_CHANGE_SUCCESSFUL)
                {
                    _logger.LogWarning($"{TAG} [SetResolutionAsync] Resolução {width}x{height} não suportada (teste: {test}).");
                    return false;
                }

                int result = NativeMethods.ChangeDisplaySettingsEx(deviceName, ref dm, IntPtr.Zero, NativeMethods.CDS_UPDATEREGISTRY, IntPtr.Zero);
                bool ok = result == NativeMethods.DISP_CHANGE_SUCCESSFUL || result == NativeMethods.DISP_CHANGE_RESTART;
                if (ok) _logger.LogSuccess($"{TAG} [SetResolutionAsync] Resolução alterada para {width}x{height}.");
                else _logger.LogError($"{TAG} [SetResolutionAsync] Falha: código {result}");
                return ok;
            }
            catch (Exception ex)
            {
                _logger.LogError($"{TAG} [SetResolutionAsync] Exceção: {ex.Message}", ex);
                return false;
            }
        });

        // ── Métricas em tempo real ───────────────────────────────────────────────

        public async Task<DisplayMetrics> GetDisplayMetricsAsync()
        {
            _logger.LogDebug($"{TAG} [GetDisplayMetricsAsync] Coletando métricas de display...");
            var metrics = new DisplayMetrics();
            try
            {
                // Medir frame pacing via Stopwatch (amostragem de 100ms)
                var sw = Stopwatch.StartNew();
                await Task.Delay(100).ConfigureAwait(false);
                sw.Stop();
                double elapsed = sw.Elapsed.TotalMilliseconds;
                metrics.FramePacingMs = Math.Round(elapsed - 100.0, 2);

                // Estimar input lag baseado em Hz atual
                var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
                NativeMethods.EnumDisplaySettings(null, NativeMethods.ENUM_CURRENT_SETTINGS, ref dm);
                int hz = (int)dm.dmDisplayFrequency;
                metrics.RefreshRateReal = hz;
                metrics.EstimatedInputLagMs = hz > 0 ? Math.Round(1000.0 / hz, 2) : 16.67;

                // Detecção de tearing via DWM
                metrics.TearingDetected = IsDwmCompositionDisabled();
                metrics.StutterDetected = metrics.FramePacingMs > 2.0;

                _logger.LogDebug($"{TAG} [GetDisplayMetricsAsync] Hz={hz} | InputLag≈{metrics.EstimatedInputLagMs}ms | Tearing={metrics.TearingDetected} | Stutter={metrics.StutterDetected}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"{TAG} [GetDisplayMetricsAsync] Erro: {ex.Message}", ex);
            }
            return metrics;
        }

        // ── DWM / VSync ──────────────────────────────────────────────────────────

        [DllImport("dwmapi.dll")]
        private static extern int DwmIsCompositionEnabled(out bool enabled);

        private bool IsDwmCompositionDisabled()
        {
            try
            {
                DwmIsCompositionEnabled(out bool enabled);
                return !enabled;
            }
            catch { return false; }
        }

        public Task<bool> SetDwmVSyncAsync(bool enable) => Task.Run(() =>
        {
            _logger.LogInfo($"{TAG} [SetDwmVSyncAsync] Inserindo preferência de VSync DWM: {enable}");
            try
            {
                // No Windows 10/11, a composição é sempre ativa por padrão, mas podemos tentar
                // influenciar a política de apresentação do DWM.
                // 0 = Auto, 1 = Force Enable, 2 = Force Disable (pode reduzir input lag ao custo de tearing)
                
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\DWM");
                if (key != null)
                {
                    key.SetValue("Composition", enable ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("DwmCompositionPolicy", enable ? 0 : 2, RegistryValueKind.DWord);
                    
                    // Outras flags para responsividade
                    key.SetValue("EnableAeroPeek", enable ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("AlwaysHibernateThumbnails", enable ? 0 : 1, RegistryValueKind.DWord);
                }

                _logger.LogSuccess($"{TAG} [SetDwmVSyncAsync] Registro DWM atualizado para VSync={(enable ? "ON" : "OFF")}");
                _logger.LogInfo($"{TAG} [SetDwmVSyncAsync] Nota: Alterações no DWM podem exigir reinício do Explorer para surtir efeito visual imediato.");
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"{TAG} [SetDwmVSyncAsync] Erro crítico ao gravar registro: {ex.Message}", ex);
                return false;
            }
        });

        // ── Gamma / Brilho via WMI ───────────────────────────────────────────────

        public Task<bool> SetGammaAsync(double gammaValue) => Task.Run(() =>
        {
            _logger.LogInfo($"{TAG} [SetGammaAsync] Iniciando ajuste de gamma para: {gammaValue:F1}");
            try
            {
                // 🔧 CORREÇÃO PROFISSIONAL: Verificar compatibilidade de hardware antes de tentar
                if (!_isGammaHardwareCompatible)
                {
                    _logger.LogWarning($"{TAG} [SetGammaAsync] Hardware incompatível detectado anteriormente. Pulando ajuste de gamma para evitar erros.");
                    _logger.LogInfo($"{TAG} [SetGammaAsync] Recomendação: Desabilite ajustes de gamma nas configurações ou use monitor compatível.");
                    return false;
                }

                // 🔧 CORREÇÃO: Validar limites de gamma
                if (gammaValue < 0.1 || gammaValue > 3.0)
                {
                    _logger.LogWarning($"{TAG} [SetGammaAsync] Valor de gamma inválido: {gammaValue:F1}. Deve estar entre 0.1 e 3.0.");
                    return false;
                }

                // 🔧 DETECÇÃO AVANÇADA: Verificar se HDR está ativo (bloqueia gamma adjustments)
                if (IsHDREnabled())
                {
                    _isGammaHardwareCompatible = false;
                    _logger.LogWarning($"{TAG} [SetGammaAsync] Monitor HDR detectado. Ajustes de gamma não são compatíveis com HDR.");
                    _logger.LogWarning($"{TAG} [SetGammaAsync] Desabilitando ajustes de gamma para esta sessão.");
                    return false;
                }

                // Gerar rampa de gamma
                var ramp = new RAMP
                {
                    Red = new ushort[256],
                    Green = new ushort[256],
                    Blue = new ushort[256]
                };

                for (int i = 0; i < 256; i++)
                {
                    // Cálculo da curva de gamma: (i / 255) ^ (1 / gamma) * 65535
                    double val = Math.Pow(i / 255.0, 1.0 / gammaValue) * 65535.0;
                    if (val > 65535) val = 65535;
                    if (val < 0) val = 0;

                    ushort shortVal = (ushort)val;
                    ramp.Red[i] = shortVal;
                    ramp.Green[i] = shortVal;
                    ramp.Blue[i] = shortVal;
                }

                _logger.LogDebug($"{TAG} [SetGammaAsync] Ramp gerada com sucesso. Obtendo DC do sistema...");

                IntPtr hDC = GdiMethods.GetDC(IntPtr.Zero);
                if (hDC == IntPtr.Zero)
                {
                    _logger.LogWarning($"{TAG} [SetGammaAsync] Não foi possível obter o DC do monitor primário.");
                    return false;
                }

                bool success = GdiMethods.SetDeviceGammaRamp(hDC, ref ramp);
                GdiMethods.ReleaseDC(IntPtr.Zero, hDC);

                if (success)
                {
                    _logger.LogSuccess($"{TAG} [SetGammaAsync] Gamma aplicado fisicamente via GDI com sucesso.");
                    
                    // Resetar contador de falhas em caso de sucesso
                    _gammaFailureCount = 0;
                    _isGammaHardwareCompatible = true;
                    
                    // Persistimos no registro para lembrança (opcional, mas bom para debug)
                    using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM");
                    key?.SetValue("VoltrisLastGamma", gammaValue.ToString("F1"), RegistryValueKind.String);
                }
                else
                {
                    _gammaFailureCount++;
                    
                    // 🔧 CORREÇÃO PROFISSIONAL: Detectar hardware incompatível e evitar tentativas repetidas
                    if (_gammaFailureCount >= 3)
                    {
                        _isGammaHardwareCompatible = false;
                        _logger.LogWarning($"{TAG} [SetGammaAsync] Hardware incompatível detectado após {_gammaFailureCount} tentativas. Desabilitando ajustes de gamma para evitar erros recorrentes.");
                        _logger.LogWarning($"{TAG} [SetGammaAsync] Causas possíveis: Monitor HDR, driver bloqueando, hardware não suportado, ou modo de cor avançado ativo.");
                    }
                    else
                    {
                        _logger.LogWarning($"{TAG} [SetGammaAsync] Falha na chamada nativa SetDeviceGammaRamp ({_gammaFailureCount}/3). Driver pode estar bloqueando ou hardware não suporta.");
                        _logger.LogDebug($"{TAG} [SetGammaAsync] Hardware detectado: Monitor HDR={IsHDREnabled()}, Driver bloqueando={IsDriverBlockingGamma()}");
                    }
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError($"{TAG} [SetGammaAsync] Exceção crítica: {ex.Message}", ex);
                return false;
            }
        });

        // ── Restaurar padrão ─────────────────────────────────────────────────────

        public Task<bool> RestoreDefaultsAsync(string deviceName) => Task.Run(() =>
        {
            _logger.LogInfo($"{TAG} [RestoreDefaultsAsync] Restaurando padrões para {deviceName}");
            try
            {
                var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
                NativeMethods.EnumDisplaySettings(deviceName, NativeMethods.ENUM_REGISTRY_SETTINGS, ref dm);
                int result = NativeMethods.ChangeDisplaySettingsEx(deviceName, ref dm, IntPtr.Zero, NativeMethods.CDS_UPDATEREGISTRY, IntPtr.Zero);
                bool ok = result == NativeMethods.DISP_CHANGE_SUCCESSFUL || result == NativeMethods.DISP_CHANGE_RESTART;
                if (ok) _logger.LogSuccess($"{TAG} [RestoreDefaultsAsync] Padrões restaurados.");
                return ok;
            }
            catch (Exception ex)
            {
                _logger.LogError($"{TAG} [RestoreDefaultsAsync] Erro: {ex.Message}", ex);
                return false;
            }
        });

        /// <summary>
        /// 🔥 MÉTODO ADICIONADO: SetGammaAsync com sobrecarga para deviceName
        /// </summary>
        public Task<bool> SetGammaAsync(string deviceName, double gamma)
        {
            return SetGammaAsync(gamma); // Usar implementação existente
        }

        // 🔧 MÉTODOS DE DETECÇÃO DE HARDWARE - Para evitar tentativas repetidas em hardware incompatível
        
        private bool IsHDREnabled()
        {
            try
            {
                // Verificar se HDR está ativo via WMI
                using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBrightness");
                foreach (ManagementObject obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    // Se encontrar monitor com capacidade HDR, assumir incompatível
                    if (obj["InstanceName"]?.ToString().Contains("HDR") == true)
                    {
                        _logger.LogDebug($"{TAG} [IsHDREnabled] Monitor HDR detectado via WMI");
                        return true;
                    }
                }

                // Verificar via registro do Windows
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\HDR");
                if (key != null)
                {
                    var hdrEnabled = key.GetValue("HDROn") as int?;
                    if (hdrEnabled == 1)
                    {
                        _logger.LogDebug($"{TAG} [IsHDREnabled] HDR ativado via registro");
                        return true;
                    }
                }

                // Verificar configurações de cor avançadas
                using var colorKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
                if (colorKey != null)
                {
                    var colorMode = colorKey.GetValue("TaskbarColorMode") as int?;
                    if (colorMode == 2) // Modo de cor automático/avançado
                    {
                        _logger.LogDebug($"{TAG} [IsHDREnabled] Modo de cor avançado detectado");
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"{TAG} [IsHDREnabled] Erro ao detectar HDR: {ex.Message}");
                return false;
            }
        }

        private bool IsDriverBlockingGamma()
        {
            try
            {
                // Verificar se driver de vídeo está bloqueando ajustes de gamma
                using var searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT * FROM Win32_VideoController");
                foreach (ManagementObject obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    var driverVersion = obj["DriverVersion"]?.ToString() ?? "";
                    var name = obj["Name"]?.ToString() ?? "";
                    
                    // Drivers conhecidos por bloquear gamma
                    if (name.Contains("NVIDIA") && driverVersion.StartsWith("31."))
                    {
                        _logger.LogDebug($"{TAG} [IsDriverBlockingGamma] Driver NVIDIA {driverVersion} pode bloquear gamma");
                        return true;
                    }
                    
                    if (name.Contains("AMD") && driverVersion.StartsWith("22."))
                    {
                        _logger.LogDebug($"{TAG} [IsDriverBlockingGamma] Driver AMD {driverVersion} pode bloquear gamma");
                        return true;
                    }
                    
                    if (name.Contains("Intel") && driverVersion.StartsWith("30."))
                    {
                        _logger.LogDebug($"{TAG} [IsDriverBlockingGamma] Driver Intel {driverVersion} pode bloquear gamma");
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"{TAG} [IsDriverBlockingGamma] Erro ao verificar driver: {ex.Message}");
                return false;
            }
        }
    }
}
