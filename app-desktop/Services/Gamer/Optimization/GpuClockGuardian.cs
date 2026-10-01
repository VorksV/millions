  using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer.Optimization
{
    public sealed class GpuClockGuardian : IDisposable
    {
        private readonly ILoggingService _logger;
        private Timer? _monitorTimer;
        private CancellationTokenSource? _cts;
        private int _gamePid;
        private string _gameName = string.Empty;
        private bool _isActive;

        private double _lastGpuClockMhz;
        private double _maxGpuClockMhz;
        private double _minLockedClockMhz;
        private bool _clockLocked;
        private string _gpuVendor = string.Empty;

        private const int MonitorIntervalMs = 2000;
        private const int ClockDropThresholdPercent = 15;

        public bool IsActive => _isActive;
        public double CurrentClockMhz => _lastGpuClockMhz;
        public string GpuVendor => _gpuVendor;

        public GpuClockGuardian(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task StartAsync(int gameProcessId, string gameName)
        {
            _logger.LogInfo("[GpuClockGuardian.StartAsync] Entry");
            if (_isActive)
            {
                _logger.LogInfo("[GpuClockGuardian.StartAsync] Exit (already active)");
                return;
            }
            _gamePid = gameProcessId;
            _gameName = gameName;
            _isActive = true;
            _cts = new CancellationTokenSource();

            try
            {
                _gpuVendor = DetectGpuVendor();
                _logger.LogInfo($"[GpuClockGuardian] GPU detectada: {_gpuVendor}");

                if (_gpuVendor.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                {
                    _maxGpuClockMhz = await GetNvidiaMaxClockAsync();
                }
                else if (_gpuVendor.Contains("AMD", StringComparison.OrdinalIgnoreCase))
                {
                    _maxGpuClockMhz = await GetAmdMaxClockAsync();
                }
                else
                {
                    _maxGpuClockMhz = 0;
                }

                double minLockMhz = _maxGpuClockMhz * 0.85;
                _minLockedClockMhz = minLockMhz;

                _logger.LogInfo($"[GpuClockGuardian] Clock maximo: {_maxGpuClockMhz:F0}MHz. Lock minimo: {_minLockedClockMhz:F0}MHz.");

                _monitorTimer = new Timer(OnMonitorTick, null, 0, MonitorIntervalMs);
                _logger.LogInfo($"[GpuClockGuardian] Monitoramento iniciado para {_gameName} (PID={_gamePid}).");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GpuClockGuardian] Erro ao iniciar: {ex.Message}");
            }
            _logger.LogInfo("[GpuClockGuardian.StartAsync] Exit");
        }

        public void Stop()
        {
            _logger.LogInfo("[GpuClockGuardian.Stop] Entry");
            if (!_isActive)
            {
                _logger.LogInfo("[GpuClockGuardian.Stop] Exit (not active)");
                return;
            }
            _isActive = false;

            _monitorTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _monitorTimer?.Dispose();
            _monitorTimer = null;

            _cts?.Cancel();

            if (_clockLocked)
            {
                RestoreClock();
            }

            _logger.LogInfo($"[GpuClockGuardian] Monitoramento parado para {_gameName}.");
            _logger.LogInfo("[GpuClockGuardian.Stop] Exit");
        }

        private void OnMonitorTick(object? state)
        {
            _logger.LogDebug("[GpuClockGuardian.OnMonitorTick] Entry");
            if (!_isActive) return;

            try
            {
                double currentClock = 0;

                if (_gpuVendor.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                {
                    currentClock = GetNvidiaCurrentClock();
                }
                else if (_gpuVendor.Contains("AMD", StringComparison.OrdinalIgnoreCase))
                {
                    currentClock = GetAmdCurrentClock();
                }
                else
                {
                    currentClock = GetWmiGpuClock();
                }

                if (currentClock <= 0) return;

                _lastGpuClockMhz = currentClock;

                if (_maxGpuClockMhz > 0 && currentClock < _minLockedClockMhz && !_clockLocked)
                {
                    double dropPercent = (1.0 - (currentClock / _maxGpuClockMhz)) * 100.0;
                    if (dropPercent >= ClockDropThresholdPercent)
                    {
                        _logger.LogWarning($"[GpuClockGuardian] Clock GPU caiu {dropPercent:F1}% ({currentClock:F0}/{_maxGpuClockMhz:F0}MHz). Aplicando lock...");
                        ForceClockLock();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[GpuClockGuardian] Erro no tick: {ex.Message}");
            }
            _logger.LogDebug("[GpuClockGuardian.OnMonitorTick] Exit");
        }

        private void ForceClockLock()
        {
            _logger.LogInfo("[GpuClockGuardian.ForceClockLock] Entry");
            try
            {
                if (_gpuVendor.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                {
                    LockNvidiaClock();
                }
                else if (_gpuVendor.Contains("AMD", StringComparison.OrdinalIgnoreCase))
                {
                    LockAmdClock();
                }
                else
                {
                    LockGenericGpuPowerMode();
                }

                _clockLocked = true;
                _logger.LogInfo($"[GpuClockGuardian] Clock GPU travado em {_minLockedClockMhz:F0}+ MHz.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GpuClockGuardian] Falha ao travar clock: {ex.Message}");
            }
            _logger.LogInfo("[GpuClockGuardian.ForceClockLock] Exit");
        }

        private void RestoreClock()
        {
            _logger.LogInfo("[GpuClockGuardian.RestoreClock] Entry");
            try
            {
                if (_gpuVendor.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                {
                    RestoreNvidiaClock();
                }
                else if (_gpuVendor.Contains("AMD", StringComparison.OrdinalIgnoreCase))
                {
                    RestoreAmdClock();
                }
                else
                {
                    RestoreGenericGpuPowerMode();
                }

                _clockLocked = false;
                _logger.LogInfo("[GpuClockGuardian] Clock GPU restaurado ao normal.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GpuClockGuardian] Falha ao restaurar clock: {ex.Message}");
            }
            _logger.LogInfo("[GpuClockGuardian.RestoreClock] Exit");
        }

        #region GPU Vendor Detection

        private string DetectGpuVendor()
        {
            _logger.LogDebug("[GpuClockGuardian.DetectGpuVendor] Entry");
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT * FROM Win32_VideoController");
                foreach (var obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    using var mo = obj;
                    string name = mo["Name"]?.ToString() ?? "";
                    if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogDebug("[GpuClockGuardian.DetectGpuVendor] Exit (NVIDIA)");
                        return "NVIDIA";
                    }
                    if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogDebug("[GpuClockGuardian.DetectGpuVendor] Exit (AMD)");
                        return "AMD";
                    }
                    if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogDebug("[GpuClockGuardian.DetectGpuVendor] Exit (Intel)");
                        return "Intel";
                    }
                }
            }
            catch { }
            _logger.LogDebug("[GpuClockGuardian.DetectGpuVendor] Exit (Unknown)");
            return "Unknown";
        }

        #region NVIDIA

        private async Task<double> GetNvidiaMaxClockAsync()
        {
            try
            {
                string result = await RunProcessAsync("nvidia-smi.exe",
                    "--query-gpu=clocks.max.gr --format=csv,noheader,nounits");
                if (double.TryParse(result.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double mhz))
                {
                    _logger.LogDebug("[GpuClockGuardian.GetNvidiaMaxClockAsync] Exit");
                    return mhz;
                }
            }
            catch { }
            _logger.LogDebug("[GpuClockGuardian.GetNvidiaMaxClockAsync] Exit (error)");
            return 0;
        }

        private double GetNvidiaCurrentClock()
        {
            _logger.LogDebug("[GpuClockGuardian.GetNvidiaCurrentClock] Entry");
            try
            {
                string result = RunProcessSync("nvidia-smi.exe",
                    "--query-gpu=clocks.gr --format=csv,noheader,nounits");
                if (double.TryParse(result.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double mhz))
                {
                    _logger.LogDebug("[GpuClockGuardian.GetNvidiaCurrentClock] Exit");
                    return mhz;
                }
            }
            catch { }
            _logger.LogDebug("[GpuClockGuardian.GetNvidiaCurrentClock] Exit (error)");
            return 0;
        }

        private void LockNvidiaClock()
        {
            _logger.LogDebug("[GpuClockGuardian.LockNvidiaClock] Entry");
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\nvlddmkm\Parameters", writable: true);
            if (key != null)
            {
                key.SetValue("D3dClockOverride", (int)_minLockedClockMhz, Microsoft.Win32.RegistryValueKind.DWord);
            }
            _logger.LogDebug("[GpuClockGuardian.LockNvidiaClock] Exit");
        }

        private void RestoreNvidiaClock()
        {
            _logger.LogDebug("[GpuClockGuardian.RestoreNvidiaClock] Entry");
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\nvlddmkm\Parameters", writable: true);
            if (key != null && key.GetValue("D3dClockOverride") != null)
            {
                key.DeleteValue("D3dClockOverride");
            }
            _logger.LogDebug("[GpuClockGuardian.RestoreNvidiaClock] Exit");
        }

        #endregion

        #region AMD

        private async Task<double> GetAmdMaxClockAsync()
        {
            _logger.LogDebug("[GpuClockGuardian.GetAmdMaxClockAsync] Entry");
            try
            {
                string result = await RunProcessAsync("powershell.exe",
                    @"-NoProfile -Command ""Get-CimInstance -Namespace root\cimv2 -ClassName Win32_VideoController | Select-Object -First 1 | ForEach-Object { $_.MaxRefreshRate }""");
                return 0;
            }
            catch { }
            return 0;
        }

        private double GetAmdCurrentClock()
        {
            _logger.LogDebug("[GpuClockGuardian.GetAmdCurrentClock] Entry/Exit");
            return 0;
        }

        private void LockAmdClock()
        {
            _logger.LogDebug("[GpuClockGuardian.LockAmdClock] Entry");
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}", writable: false);
                if (key == null) return;

                foreach (var subKey in key.GetSubKeyNames())
                {
                    try
                    {
                        using var gpuKey = key.OpenSubKey(subKey, writable: true);
                        if (gpuKey?.GetValue("DriverDesc")?.ToString()?.Contains("AMD", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            gpuKey.SetValue("PP_ThermalAutoThrottling", 0, Microsoft.Win32.RegistryValueKind.DWord);
                            gpuKey.SetValue("PP_SclkDeepSleepDisable", 1, Microsoft.Win32.RegistryValueKind.DWord);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void RestoreAmdClock()
        {
            _logger.LogDebug("[GpuClockGuardian.RestoreAmdClock] Entry");
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}", writable: false);
                if (key == null) return;

                foreach (var subKey in key.GetSubKeyNames())
                {
                    try
                    {
                        using var gpuKey = key.OpenSubKey(subKey, writable: true);
                        if (gpuKey?.GetValue("DriverDesc")?.ToString()?.Contains("AMD", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            if (gpuKey.GetValue("PP_ThermalAutoThrottling") != null)
                                gpuKey.DeleteValue("PP_ThermalAutoThrottling");
                            if (gpuKey.GetValue("PP_SclkDeepSleepDisable") != null)
                                gpuKey.DeleteValue("PP_SclkDeepSleepDisable");
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        #endregion

        #region Generic / WMI

        private double GetWmiGpuClock()
        {
            _logger.LogDebug("[GpuClockGuardian.GetWmiGpuClock] Entry");
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT * FROM Win32_VideoController");
                foreach (var obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    using var mo = obj;
                    long ram = Convert.ToInt64(mo["AdapterRAM"] ?? 0);
                    if (ram > 0)
                        return ram / 1000000.0;
                }
            }
            catch { }
            return 0;
        }

        /// [FIX:UNICO-DONO-DE-ENERGIA] Trava de clock de GPU removida.
        ///
        /// Este método escrevia `ACSettingIndex = 0` em
        /// HKLM\...\PowerSettings\54533251-…\0b2d69d7-…, que é o subgroup
        /// "Processador" e o setting "Limites Máximos de Estado de Desempenho do
        /// Processador" — no limite MÁXIMO do valor, não no mínimo.
        ///
        /// O detalhe é o que torna o método mais enganoso do arquivo. O nome
        /// `LockGenericGpuPowerMode` e o `0` sugerem "travar em 0" ou "forçar
        /// economy". O que a escrita fazia era escrever o MÁXIMO permitido pelo
        /// setting, numa chave do PROCESSADOR, dentro de um serviço cujo nome é
        /// de GPU. Ela não tocava na GPU.
        ///
        /// Além disso, `RestoreGenericGpuPowerMode` fazia
        /// `DeleteValue("ACSettingIndex")` — apagar o valor em vez de restaurar
        /// o anterior. Isso não restaura nada: apaga a configuração que o
        /// Windows tinha, e o Windows volta ao seu padrão silenciosamente. É
        /// esse par, gravar-e-apagar, que produz a classe de bug mais difícil de
        /// diagnosticar: nada falha, o valor some, e o sistema parece estar bem
        /// porque "voltou ao padrão" — para um padrão que ninguém escolheu.
        ///
        /// E, como nos demais casos de registro: nada disso é visível para o
        /// Perfil Inteligente, que trabalha no plano de energia e não lê estas
        /// chaves globais.
        private void LockGenericGpuPowerMode()
        {
            _logger.LogInfo(
                "[GpuClockGuardian] LockGenericGpuPowerMode NEUTRALIZADO. A escrita em " +
                "PowerSettings (ACSettingIndex) foi removida: é energia do Perfil " +
                "Inteligente, e o par escrever+DeleteValue nunca restaurava o valor " +
                "original — apagava a configuração do Windows.");
        }

        /// [FIX:UNICO-DONO-DE-ENERGIA] A restauração vai embora com a escrita.
        /// Ver a nota em <see cref="LockGenericGpuPowerMode"/> para o porquê do
        /// `DeleteValue` ser pior do que parece.
        private void RestoreGenericGpuPowerMode()
        {
            _logger.LogDebug(
                "[GpuClockGuardian.RestoreGenericGpuPowerMode] Sem o que restaurar: " +
                "a gravacao foi neutralizada.");
        }

        #endregion

        #region Process Helpers

        private async Task<string> RunProcessAsync(string fileName, string arguments)
        {
            _logger.LogDebug("[GpuClockGuardian.RunProcessAsync] Entry");
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();
            string stdout = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            _logger.LogDebug("[GpuClockGuardian.RunProcessAsync] Exit");
            return stdout;
        }

        private string RunProcessSync(string fileName, string arguments)
        {
            _logger.LogDebug("[GpuClockGuardian.RunProcessSync] Entry");
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();
            string stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            _logger.LogDebug("[GpuClockGuardian.RunProcessSync] Exit");
            return stdout;
        }

        #endregion

        public void Dispose()
        {
            _logger.LogDebug("[GpuClockGuardian.Dispose] Entry");
            Stop();
            _cts?.Dispose();
            _logger.LogDebug("[GpuClockGuardian.Dispose] Exit");
        }
        #endregion
    }
}