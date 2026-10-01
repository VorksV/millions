using System;
using System.Diagnostics;
using System.Management;
using System.Runtime.CompilerServices;
using System.Security.Principal;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Core.Body.Arms;

public sealed class GpuArm : IGpuArm, IDisposable
{
	private readonly ILoggingService _logger;

	private GpuVendor _detectedVendor = GpuVendor.Unknown;

	private string _gpuName = string.Empty;

	private string _driverVersion = string.Empty;

	private object? _originalHagsMode;

	private object? _originalNvidiaPowerMizer;
	private object? _originalNvidiaPowerMizerLevel;

	private object? _originalAmdGpuPowerDown;

	private object? _originalAmdThermalThrottling;

	private object? _originalIntelPowerPolicy;

	private readonly bool _isAdmin;

	public GpuVendor DetectedVendor => _detectedVendor;

	public string GpuName => _gpuName;

	public GpuArm(ILoggingService logger)
	{
		_logger = logger ?? throw new ArgumentNullException("logger");
		_isAdmin = IsRunningAsAdmin();
		DetectGpuVendor();
	}

	private bool IsRunningAsAdmin()
	{
		try
		{
			using WindowsIdentity ntIdentity = WindowsIdentity.GetCurrent();
			WindowsPrincipal windowsPrincipal = new WindowsPrincipal(ntIdentity);
			return windowsPrincipal.IsInRole(WindowsBuiltInRole.Administrator);
		}
		catch
		{
			return false;
		}
	}

	private void DetectGpuVendor()
	{
		try
		{
			using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
			foreach (ManagementObject item in managementObjectSearcher.Get())
			{
                using var __dispose_item = item;
				string text = item["AdapterCompatibility"]?.ToString() ?? "";
				string text2 = item["Name"]?.ToString() ?? "";
				string driverVersion = item["DriverVersion"]?.ToString() ?? "";
				if (!string.IsNullOrEmpty(text2))
				{
					_gpuName = text2;
					_driverVersion = driverVersion;
					string text3 = text2.ToLowerInvariant();
					if (text3.Contains("nvidia") || text3.Contains("geforce") || text3.Contains("rtx") || text3.Contains("gtx"))
					{
						_detectedVendor = GpuVendor.Nvidia;
					}
					else if (text3.Contains("intel") || text3.Contains("iris") || text3.Contains("uhd graphics"))
					{
						_detectedVendor = GpuVendor.Intel;
					}
					else if (text3.Contains("amd") || text3.Contains("radeon") || text3.Contains("ati"))
					{
						_detectedVendor = GpuVendor.Amd;
					}
					else if (text.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
					{
						_detectedVendor = GpuVendor.Nvidia;
					}
					else if (text.Contains("Intel", StringComparison.OrdinalIgnoreCase))
					{
						_detectedVendor = GpuVendor.Intel;
					}
					else if (text.Contains("AMD", StringComparison.OrdinalIgnoreCase) || text.Contains("Advanced Micro", StringComparison.OrdinalIgnoreCase))
					{
						_detectedVendor = GpuVendor.Amd;
					}
					else
					{
						_detectedVendor = GpuVendor.Unknown;
					}
					ILoggingService logger = _logger;
					DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(36, 2);
					defaultInterpolatedStringHandler.AppendLiteral("[ARM-GPU] Vendor detectado: ");
					defaultInterpolatedStringHandler.AppendFormatted(_detectedVendor);
					defaultInterpolatedStringHandler.AppendLiteral(" (GPU: ");
					defaultInterpolatedStringHandler.AppendFormatted(text2);
					defaultInterpolatedStringHandler.AppendLiteral(")");
					logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
					_logger.LogInfo("[ARM-GPU] GPU: " + _gpuName + " | Driver: " + _driverVersion);
					break;
				}
			}
		}
		catch (Exception ex)
		{
			_logger.LogError("[ARM-GPU] ERRO ao detectar vendor: " + ex.Message);
		}
	}

	public Task<ArmActionResult> ApplyGamingProfileAsync(string processName)
	{
		string processName2 = processName;
		return Task.Run(delegate
		{
			Stopwatch stopwatch = Stopwatch.StartNew();
			try
			{
				_logger.LogInfo("[ARM-GPU] Aplicando gaming profile para " + processName2);
				CaptureOriginalValues();
				switch (_detectedVendor)
				{
				case GpuVendor.Nvidia:
					ApplyNvidiaGamingProfile();
					break;
				case GpuVendor.Amd:
					ApplyAmdGamingProfile();
					break;
				case GpuVendor.Intel:
					ApplyIntelGamingProfile();
					break;
				default:
					_logger.LogWarning("[ARM-GPU] Vendor desconhecido, aplicando tweaks genricos");
					ApplyGenericGamingProfile();
					break;
				}
				ApplyGpuPriorityTweak();
				return CreateActionResult(success: true, "Gaming profile aplicado", stopwatch.ElapsedMilliseconds);
			}
			catch (Exception ex)
			{
				return CreateActionResult(success: false, ex.Message, stopwatch.ElapsedMilliseconds);
			}
		});
	}

	private ArmActionResult CreateActionResult(bool success, string message, long executionTimeMs)
	{
		return new ArmActionResult
		{
			Success = success,
			Message = message,
			ExecutionTimeMs = executionTimeMs
		};
	}

	private void CaptureOriginalValues()
	{
		try
		{
			_originalHagsMode = Registry.GetValue("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Control\\GraphicsDrivers", "HwSchMode", null);
			ILoggingService logger = _logger;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(35, 1);
			defaultInterpolatedStringHandler.AppendLiteral("[ARM-GPU] HAGS original capturado: ");
			defaultInterpolatedStringHandler.AppendFormatted<object>(_originalHagsMode ?? "null");
			logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
		}
		catch (Exception ex)
		{
			_logger.LogError("[ARM-GPU] ERRO ao capturar HAGS original: " + ex.Message);
		}
	}

	private void ApplyNvidiaGamingProfile()
	{
		try
		{
			_originalNvidiaPowerMizer = Registry.GetValue("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Services\\nvlddmkm\\Global\\NvTweak", "PowerMizerEnable", null);
			_originalNvidiaPowerMizerLevel = Registry.GetValue("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Services\\nvlddmkm\\Global\\NvTweak", "PowerMizerLevel", null);
			Registry.SetValue("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Services\\nvlddmkm\\Global\\NvTweak", "PowerMizerEnable", 1, RegistryValueKind.DWord);
			Registry.SetValue("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Services\\nvlddmkm\\Global\\NvTweak", "PowerMizerLevel", 1, RegistryValueKind.DWord);
			Registry.SetValue("HKEY_CURRENT_USER\\Software\\NVIDIA Corporation\\Global\\NvTweak", "Anisão", 1, RegistryValueKind.DWord);
			_logger.LogInfo("[ARM-GPU] NVIDIA | Registry tweaks aplicados | PowerMizer=max perf | Anisão=1");
		}
		catch (Exception ex)
		{
			_logger.LogError("[ARM-GPU] ERRO ao aplicar tweaks NVIDIA: " + ex.Message);
		}
	}

	private void ApplyAmdGamingProfile()
	{
		try
		{
			_originalAmdGpuPowerDown = Registry.GetValue("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Control\\Class\\{4d36e968-e325-11ce-bfc1-08002be10318}\\0000", "PP_GPUPowerDownEnabled", null);
			Registry.SetValue("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Control\\Class\\{4d36e968-e325-11ce-bfc1-08002be10318}\\0000", "PP_GPUPowerDownEnabled", 0, RegistryValueKind.DWord);
			_originalAmdThermalThrottling = Registry.GetValue("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Control\\Class\\{4d36e968-e325-11ce-bfc1-08002be10318}\\0000", "PP_ThermalAutoThrottlingEnable", null);
			Registry.SetValue("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Control\\Class\\{4d36e968-e325-11ce-bfc1-08002be10318}\\0000", "PP_ThermalAutoThrottlingEnable", 0, RegistryValueKind.DWord);
			_logger.LogInfo("[ARM-GPU] AMD | Power management tweaks aplicados");
		}
		catch (Exception ex)
		{
			_logger.LogError("[ARM-GPU] ERRO ao aplicar tweaks AMD: " + ex.Message);
		}
	}

	private void ApplyIntelGamingProfile()
	{
		try
		{
			_originalIntelPowerPolicy = Registry.GetValue("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Control\\Class\\{4d36e968-e325-11ce-bfc1-08002be10318}\\0000", "PowerPolicy", null);
			Registry.SetValue("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Control\\Class\\{4d36e968-e325-11ce-bfc1-08002be10318}\\0000", "PowerPolicy", 2, RegistryValueKind.DWord);
			_logger.LogInfo("[ARM-GPU] Intel | Power policy Maximum Performance");
		}
		catch (Exception ex)
		{
			_logger.LogError("[ARM-GPU] ERRO ao aplicar tweaks Intel: " + ex.Message);
		}
	}

	private void ApplyGenericGamingProfile()
	{
		try
		{
			Registry.SetValue("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Control\\GraphicsDrivers", "HwSchMode", 2, RegistryValueKind.DWord);
			_logger.LogInfo("[ARM-GPU] Tweak genrico aplicado");
		}
		catch (Exception ex)
		{
			_logger.LogError("[ARM-GPU] ERRO ao aplicar tweaks genricos: " + ex.Message);
		}
	}

	private void ApplyGpuPriorityTweak()
	{
		try
		{
			string keyName = "HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile\\Tasks\\Games";
			Registry.SetValue(keyName, "GPU Priority", 8, RegistryValueKind.DWord);
			Registry.SetValue(keyName, "Priority", 6, RegistryValueKind.DWord);
			Registry.SetValue(keyName, "Scheduling Category", "High", RegistryValueKind.String);
			_logger.LogInfo("[ARM-GPU] GPU Priority tweak aplicado para Games");
		}
		catch (Exception ex)
		{
			_logger.LogError("[ARM-GPU] ERRO ao aplicar GPU Priority tweak: " + ex.Message);
		}
	}

	public Task<ArmActionResult> SetLowLatencyModeAsync(bool enable)
	{
		return Task.Run(delegate
		{
			Stopwatch stopwatch = Stopwatch.StartNew();
			try
			{
				switch (_detectedVendor)
				{
				case GpuVendor.Nvidia:
					ApplyNvidiaLowLatency(enable);
					break;
				case GpuVendor.Amd:
					ApplyAmdLowLatency(enable);
					break;
				case GpuVendor.Intel:
					_logger.LogInfo("[ARM-GPU] Intel Low Latency: configurado via PowerArm");
					break;
				}
				return CreateActionResult(success: true, "Low Latency mode aplicado", stopwatch.ElapsedMilliseconds);
			}
			catch (Exception ex)
			{
				return CreateActionResult(success: false, ex.Message, stopwatch.ElapsedMilliseconds);
			}
		});
	}

	private void ApplyNvidiaLowLatency(bool enable)
	{
		try
		{
			int num = ((!enable) ? 1 : 0);
			Registry.SetValue("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Services\\nvlddmkm\\Global\\NvTweak", "RenderModeSelect", num, RegistryValueKind.DWord);
			_logger.LogInfo("[ARM-GPU] NVIDIA Low Latency Mode: " + (enable ? "ON" : "OFF") + " via Registry");
		}
		catch (Exception ex)
		{
			_logger.LogError("[ARM-GPU] ERRO ao aplicar NVIDIA Low Latency: " + ex.Message);
		}
	}

	private void ApplyAmdLowLatency(bool enable)
	{
		try
		{
			int num = ((!enable) ? 1 : 0);
			Registry.SetValue("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Control\\Class\\{4d36e968-e325-11ce-bfc1-08002be10318}\\0000", "EnableUlps", num, RegistryValueKind.DWord);
			_logger.LogInfo("[ARM-GPU] AMD Anti-Lag: " + (enable ? "ON" : "OFF"));
		}
		catch (Exception ex)
		{
			_logger.LogError("[ARM-GPU] ERRO ao aplicar AMD Anti-Lag: " + ex.Message);
		}
	}

	public Task<ArmActionResult> EnableHagsAsync()
	{
		return Task.Run(delegate
		{
			Stopwatch stopwatch = Stopwatch.StartNew();
			try
			{
				if (!_isAdmin)
				{
					_logger.LogWarning("[ARM-GPU] HAGS: REQUER ADMIN, pulado");
					return new ArmActionResult
					{
						Success = false,
						Message = "Requer admin",
						ExecutionTimeMs = stopwatch.ElapsedMilliseconds
					};
				}
				string keyName = "HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Control\\GraphicsDrivers";
				object value = Registry.GetValue(keyName, "HwSchMode", null);
				if (value == null)
				{
					_logger.LogInfo("[ARM-GPU] HAGS: não suportado neste hardware/driver");
					return new ArmActionResult
					{
						Success = false,
						Message = "Não suportado",
						ExecutionTimeMs = stopwatch.ElapsedMilliseconds
					};
				}
				if (Convert.ToInt32(value) == 2)
				{
					_logger.LogInfo("[ARM-GPU] HAGS: já ativo, nenhuma ação necessária");
					return new ArmActionResult
					{
						Success = true,
						Message = "Já ativo",
						ExecutionTimeMs = stopwatch.ElapsedMilliseconds
					};
				}
				Registry.SetValue(keyName, "HwSchMode", 2, RegistryValueKind.DWord);
				_logger.LogInfo("[ARM-GPU] HAGS: habilitado (era desabilitado) | reboot necessário");
				return new ArmActionResult
				{
					Success = true,
					Message = "Habilitado (reboot necessário)",
					ExecutionTimeMs = stopwatch.ElapsedMilliseconds
				};
			}
			catch (Exception ex)
			{
				_logger.LogError("[ARM-GPU] ERRO em EnableHagsAsync: " + ex.Message);
				return new ArmActionResult
				{
					Success = false,
					Message = ex.Message,
					ExecutionTimeMs = stopwatch.ElapsedMilliseconds
				};
			}
		});
	}

	public Task<ArmActionResult> RestoreGpuDefaultsAsync()
	{
		return Task.Run(delegate
		{
			Stopwatch stopwatch = Stopwatch.StartNew();
			try
			{
				_logger.LogInfo("[ARM-GPU] Restáaurando defaults de GPU");
				if (_originalHagsMode != null)
				{
					Registry.SetValue("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Control\\GraphicsDrivers", "HwSchMode", _originalHagsMode, RegistryValueKind.DWord);
				}
				if (_originalNvidiaPowerMizer != null)
				{
					Registry.SetValue("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Services\\nvlddmkm\\Global\\NvTweak", "PowerMizerEnable", _originalNvidiaPowerMizer, RegistryValueKind.DWord);
				}
				if (_originalNvidiaPowerMizerLevel != null)
				{
					Registry.SetValue("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Services\\nvlddmkm\\Global\\NvTweak", "PowerMizerLevel", _originalNvidiaPowerMizerLevel, RegistryValueKind.DWord);
				}
				if (_originalAmdGpuPowerDown != null)
				{
					Registry.SetValue("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Control\\Class\\{4d36e968-e325-11ce-bfc1-08002be10318}\\0000", "PP_GPUPowerDownEnabled", _originalAmdGpuPowerDown, RegistryValueKind.DWord);
				}
				if (_originalAmdThermalThrottling != null)
				{
					Registry.SetValue("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Control\\Class\\{4d36e968-e325-11ce-bfc1-08002be10318}\\0000", "PP_ThermalAutoThrottlingEnable", _originalAmdThermalThrottling, RegistryValueKind.DWord);
				}
				if (_originalIntelPowerPolicy != null)
				{
					Registry.SetValue("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Control\\Class\\{4d36e968-e325-11ce-bfc1-08002be10318}\\0000", "PowerPolicy", _originalIntelPowerPolicy, RegistryValueKind.DWord);
				}
				ILoggingService logger = _logger;
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(40, 1);
				defaultInterpolatedStringHandler.AppendLiteral("[ARM-GPU] Defaults restáaurados | em ");
				defaultInterpolatedStringHandler.AppendFormatted(stopwatch.ElapsedMilliseconds);
				defaultInterpolatedStringHandler.AppendLiteral(" ms");
				logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
				return new ArmActionResult
				{
					Success = true,
					Message = "Defaults restáaurados",
					ExecutionTimeMs = stopwatch.ElapsedMilliseconds
				};
			}
			catch (Exception ex)
			{
				_logger.LogError("[ARM-GPU] ERRO em RestoreGpuDefaultsAsync: " + ex.Message);
				return new ArmActionResult
				{
					Success = false,
					Message = ex.Message,
					ExecutionTimeMs = stopwatch.ElapsedMilliseconds
				};
			}
		});
	}

	public void Dispose()
	{
	}
}
