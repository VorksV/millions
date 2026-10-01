using System;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;

namespace VoltrisOptimizer.Services.HardwareTelemetry
{
    public class HardwareTelemetryService : IDisposable
    {
        private readonly ILoggingService _logger;
        private readonly SensorCache _sensorCache;
        private Computer? _computer;
        private HardwareUpdateVisitor? _visitor;
        private CancellationTokenSource? _cts;
        private Task? _pollingTask;

        public bool IsActive { get; private set; }

        public HardwareTelemetryService(ILoggingService logger, SensorCache sensorCache)
        {
            _logger = logger;
            _sensorCache = sensorCache;
        }

        private string GetAdminStatus()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator) ? "ADMIN" : "USER";
            }
            catch (Exception adminEx)
            {
                _logger.LogDebug($"[HardwareTelemetry] GetAdminStatus exception: {adminEx.GetType().Name}");
                return "UNKNOWN";
            }
        }

        public void Start()
        {
            if (IsActive) return;

            _logger.LogInfo($"[HardwareTelemetry] Initializing LibreHardwareMonitorLib (elevation: {GetAdminStatus()}, TID={Environment.CurrentManagedThreadId})...");
            
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (attempt > 0)
                {
                    _logger.LogInfo("[HardwareTelemetry] Retry attempt 1 after 1s delay...");
                    Thread.Sleep(1000);
                }

                try
                {
                    _logger.LogDebug("[HardwareTelemetry] Creating Computer instance...");
                    _computer = new Computer
                    {
                        IsCpuEnabled = true,
                        IsGpuEnabled = true,
                        IsMemoryEnabled = true,
                        IsMotherboardEnabled = true,
                        IsStorageEnabled = true,
                        IsNetworkEnabled = false,
                        IsControllerEnabled = false
                    };

                    _logger.LogDebug("[HardwareTelemetry] Calling _computer.Open() — may trigger first-chance WMI exceptions...");
                    _computer.Open();
                    
                    _logger.LogDebug("[HardwareTelemetry] Creating HardwareUpdateVisitor...");
                    _visitor = new HardwareUpdateVisitor();
                    _computer.Accept(_visitor);
                    
                    _logger.LogDebug("[HardwareTelemetry] Generating initial snapshot...");
                    var initialSnapshot = _visitor.GenerateSnapshot();
                    LogSensorDiagnostics(initialSnapshot);
                    
                    _sensorCache.Update(initialSnapshot);

                    _cts = new CancellationTokenSource();
                    _pollingTask = Task.Run(() => PollingLoop(_cts.Token), _cts.Token);

                    IsActive = true;
                    _logger.LogSuccess("[HardwareTelemetry] LibreHardwareMonitorLib initialized successfully.");
                    return;
                }
                catch (ManagementException mEx)
                {
                    _logger.LogWarning($"[HardwareTelemetry] Attempt {attempt + 1}/2: ManagementException (classe WMI inválida no sistema): Code={mEx.ErrorCode}, {mEx.Message}");
                    _logger.LogWarning("[HardwareTelemetry] Causas possíveis:");
                    _logger.LogWarning("[HardwareTelemetry]   - WMI service não está totalmente inicializado");
                    _logger.LogWarning("[HardwareTelemetry]   - Problemas de permissão ao acessar classes WMI");
                    _logger.LogWarning("[HardwareTelemetry]   - Hardware não suporta certas queries WMI");
                    DisposeComputerSafe();
                    
                    if (attempt == 0)
                    {
                        _logger.LogInfo("[HardwareTelemetry] Aguardando 1s antes do retry...");
                        continue;
                    }
                }
                catch (InvalidOperationException ioEx)
                {
                    _logger.LogWarning($"[HardwareTelemetry] Attempt {attempt + 1}/2: InvalidOperationException (sensor/COM não inicializado): {ioEx.Message}");
                    _logger.LogWarning("[HardwareTelemetry] Causas possíveis:");
                    _logger.LogWarning("[HardwareTelemetry]   - Race condition entre threads acessando sensores");
                    _logger.LogWarning("[HardwareTelemetry]   - Sensor não disponível neste hardware");
                    DisposeComputerSafe();
                    
                    if (attempt == 0)
                    {
                        _logger.LogInfo("[HardwareTelemetry] Aguardando 1s antes do retry...");
                        continue;
                    }
                }
                catch (TargetInvocationException tiEx)
                {
                    _logger.LogWarning($"[HardwareTelemetry] Attempt {attempt + 1}/2: TargetInvocationException (reflexão em sensor): Inner={tiEx.InnerException?.GetType().Name}: {tiEx.InnerException?.Message}");
                    _logger.LogWarning("[HardwareTelemetry] Causas possíveis:");
                    _logger.LogWarning("[HardwareTelemetry]   - Falha ao acessar sensor via reflexão");
                    _logger.LogWarning("[HardwareTelemetry]   - Driver de sensor não instalado");
                    DisposeComputerSafe();
                    
                    if (attempt == 0)
                    {
                        _logger.LogInfo("[HardwareTelemetry] Aguardando 1s antes do retry...");
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[HardwareTelemetry] Attempt {attempt + 1}/2 failed: {ex.GetType().Name}: {ex.Message}");
                    DisposeComputerSafe();
                    
                    if (attempt == 0)
                    {
                        _logger.LogInfo("[HardwareTelemetry] Aguardando 1s antes do retry...");
                        continue;
                    }
                }
            }
            
            _logger.LogWarning("[HardwareTelemetry] Failed to initialize LibreHardwareMonitorLib after 2 attempts. Temperature will be estimated via CPU load.");
            _logger.LogWarning("[HardwareTelemetry] Funcionalidades disponíveis:");
            _logger.LogWarning("[HardwareTelemetry]   - Estimativa de temperatura baseada em CPU load");
            _logger.LogWarning("[HardwareTelemetry]   - CPU/GPU load via Performance Counter");
            _logger.LogWarning("[HardwareTelemetry]   - Clock via WMI (se disponível)");
        }

        private void DisposeComputerSafe()
        {
            try
            {
                _computer?.Close();
                _computer = null;
            }
            catch (Exception disposeEx)
            {
                _logger.LogDebug($"[HardwareTelemetry] DisposeComputerSafe exception: {disposeEx.GetType().Name}");
            }
        }

        private void LogSensorDiagnostics(HardwareSnapshot snapshot)
        {
            if (_computer == null) return;

            int cpuSensors = 0, gpuSensors = 0;
            try
            {
                foreach (var hw in _computer.Hardware)
                {
                    switch (hw.HardwareType)
                    {
                        case HardwareType.Cpu:
                            cpuSensors = hw.Sensors.Count(s => s.SensorType == SensorType.Temperature);
                            break;
                        case HardwareType.GpuNvidia:
                        case HardwareType.GpuAmd:
                        case HardwareType.GpuIntel:
                            gpuSensors = hw.Sensors.Count(s => s.SensorType == SensorType.Temperature);
                            break;
                    }
                }
            }
            catch (Exception logDiagEx)
            {
                _logger.LogDebug($"[HardwareTelemetry] LogSensorDiagnostics exception: {logDiagEx.GetType().Name}");
            }

            _logger.LogInfo($"[HardwareTelemetry] LibreHardwareMonitor found: {cpuSensors} CPU temp sensor(s), {gpuSensors} GPU temp sensor(s)");

            // Tentar fallback WMI se LibreHardwareMonitor não tiver sensores reais
            double tempToLog = snapshot.CpuTemperature;
            bool fromWmiFallback = false;
            if (double.IsNaN(tempToLog) || tempToLog <= 0)
            {
                double wmiTemp = ReadCpuTemperatureFromWmi();
                if (wmiTemp > 0)
                {
                    tempToLog = wmiTemp;
                    fromWmiFallback = true;
                }
            }

            if (!double.IsNaN(tempToLog) && tempToLog > 0)
            {
                if (tempToLog >= 0 && tempToLog <= 150)
                {
                    _logger.LogInfo($"[HardwareTelemetry] CPU temperature: {tempToLog:F1}°C ({(fromWmiFallback ? "WMI fallback" : "REAL sensor")})");
                }
                else
                {
                    _logger.LogWarning($"[HardwareTelemetry] CPU temperature fora do range válido: {tempToLog:F1}°C (esperado 0-150°C)");
                    _logger.LogWarning("[HardwareTelemetry] Temperatura será tratada como inválida - usando estimativa");
                }
            }
            else
            {
                _logger.LogWarning("[HardwareTelemetry] CPU temperature: NaN (no real sensor data — will use estimation)");
                _logger.LogWarning("[HardwareTelemetry] Possíveis causas:");
                _logger.LogWarning("[HardwareTelemetry]   - Drivers de sensores não instalados");
                _logger.LogWarning("[HardwareTelemetry]   - Hardware não expõe sensores via LibreHardwareMonitor nem WMI");
                _logger.LogWarning("[HardwareTelemetry]   - Timing de inicialização do sensor");
                _logger.LogWarning("[HardwareTelemetry] Solução: Sistema usará estimativa baseada em CPU load");
            }
        }

        private static double ReadCpuTemperatureFromWmi()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation");
                foreach (ManagementObject obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    using (obj)
                    {
                        var val = obj["HighPrecisionTemperature"];
                        if (val != null)
                        {
                            double tempKelvin = Convert.ToDouble(val) / 10.0;
                            double tempCelsius = tempKelvin - 273.15;
                            if (tempCelsius > 0 && tempCelsius < 150)
                                return tempCelsius;
                        }
                    }
                }
            }
            catch (Exception exWmi) { System.Diagnostics.Debug.WriteLine($"[HardwareTelemetry] Erro WMI ao ler temperatura CPU (WMI_CPU): {exWmi.Message}"); }

            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT * FROM MSAcpi_ThermalZoneTemperature");
                foreach (ManagementObject obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    using (obj)
                    {
                        var val = obj["CurrentTemperature"];
                        if (val != null)
                        {
                            double tempKelvin = Convert.ToDouble(val) / 10.0;
                            double tempCelsius = tempKelvin - 273.15;
                            if (tempCelsius > 0 && tempCelsius < 150)
                                return tempCelsius;
                        }
                    }
                }
            }
            catch (Exception exAcpi) { System.Diagnostics.Debug.WriteLine($"[HardwareTelemetry] Erro ACPI ao ler temperatura: {exAcpi.Message}"); }

            return double.NaN;
        }

		private async Task PollingLoop(CancellationToken cancellationToken)
		{
			using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(3000));
			try
			{
				while (await timer.WaitForNextTickAsync(cancellationToken))
				{
					if (_computer == null || _visitor == null) break;
					try
					{
						_computer.Accept(_visitor);
						var snapshot = _visitor.GenerateSnapshot();
						_sensorCache.Update(snapshot);
					}
					catch (Exception ex)
					{
						_logger.LogDebug($"[HardwareTelemetry] Transient error during polling: {ex.Message}");
					}
				}
			}
			catch (OperationCanceledException) { }
		}

		public void ForcePoll()
		{
			if (_computer == null || _visitor == null || !IsActive) return;
			try
			{
				_computer.Accept(_visitor);
				var snapshot = _visitor.GenerateSnapshot();
				_sensorCache.Update(snapshot);
			}
			catch (Exception ex)
			{
				_logger.LogDebug($"[HardwareTelemetry] ForcePoll transient error: {ex.Message}");
			}
		}

		public void Dispose()
		{
			if (!IsActive) return;
			IsActive = false;
			_cts?.Cancel();
			_cts?.Dispose();
			try { _pollingTask?.Wait(2000); } catch (Exception exPoll) { _logger?.LogWarning($"[HardwareTelemetry] Erro ao aguardar polling task: {exPoll.Message}"); }
			DisposeComputerSafe();
			_logger.LogInfo("[HardwareTelemetry] Stopped cleanly.");
		}
    }
}
