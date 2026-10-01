using System;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Core.EnterpriseCheck;

public class EnvironmentDetector
{
	private static class SystemParametersInfo
	{
		public struct SYSTEM_POWER_STATUS
		{
			public byte ACLineStatus;

			public byte BatteryFlag;

			public byte BatteryLifePercent;

			public byte SystemStatusFlag;

			public int BatteryLifeTime;

			public int BatteryFullLifeTime;
		}

		[DllImport("kernel32.dll", SetLastError = true)]
		public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

		public static SYSTEM_POWER_STATUS GetPowerStatus()
		{
			GetSystemPowerStatus(out var lpSystemPowerStatus);
			return lpSystemPowerStatus;
		}
	}

	private readonly ILoggingService _logger;

	private bool? _isCorporateManagedCache = null;

	private DateTime _lastThermalCheck = DateTime.MinValue;

	private bool _lastThermalState = false;

	public EnvironmentDetector(ILoggingService logger)
	{
		_logger = logger;
	}

	public bool IsCorporateManaged()
	{
		if (_isCorporateManagedCache.HasValue)
		{
			return _isCorporateManagedCache.Value;
		}
		try
		{
			string domainName = IPGlobalProperties.GetIPGlobalProperties().DomainName;
			if (!string.IsNullOrEmpty(domainName) && domainName != "WORKGROUP")
			{
				_logger.LogWarning("[ENV] Ambiente Corporativo Detectado! Domínio: " + domainName);
				_isCorporateManagedCache = true;
				return true;
			}
			using (ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("SELECT PartOfDomain FROM Win32_ComputerSystem"))
			{
				foreach (ManagementObject item in managementObjectSearcher.Get())
				{
                using var __dispose_item = item;
					if ((bool)item["PartOfDomain"])
					{
						_logger.LogWarning("[ENV] Ambiente Corporativo Detectado (WMI)!");
						_isCorporateManagedCache = true;
						return true;
					}
				}
			}
			_isCorporateManagedCache = false;
			return false;
		}
		catch (Exception exception)
		{
			_logger.LogError("[ENV] Erro ao detectar ambiente corporativo", exception);
			_isCorporateManagedCache = false;
			return false;
		}
	}

	public bool IsLaptopOnBattery()
	{
		try
		{
			bool flag = SystemParametersInfo.GetPowerStatus().ACLineStatus == 0;
			if (flag)
			{
				_logger.LogInfo("[ENV] Dispositivo operando na bateria. Modo performance restrito.");
			}
			return flag;
		}
		catch (Exception exception)
		{
			_logger.LogError("[ENV] Erro ao detectar status de energia", exception);
			return false;
		}
	}

	public bool ThermalThrottlingDetected()
	{
		if ((DateTime.UtcNow - _lastThermalCheck).TotalSeconds < 10.0)
		{
			return _lastThermalState;
		}
		try
		{
			using (ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM MSAcpi_ThermalZoneTemperature"))
			{
				foreach (ManagementObject item in managementObjectSearcher.Get())
				{
                using var __dispose_item = item;
					double num = Convert.ToDouble(item["CurrentTemperature"]);
					double num2 = num / 10.0 - 273.15;
					if (num2 > 90.0)
					{
						ILoggingService logger = _logger;
						DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(63, 1);
						defaultInterpolatedStringHandler.AppendLiteral("[ENV] ALERTA TÉRMICO: Zona detectada a ");
						defaultInterpolatedStringHandler.AppendFormatted(num2, "F1");
						defaultInterpolatedStringHandler.AppendLiteral("°C. Throttling iminente.");
						logger.LogWarning(defaultInterpolatedStringHandler.ToStringAndClear());
						_lastThermalState = true;
						_lastThermalCheck = DateTime.UtcNow;
						return true;
					}
				}
			}
			_lastThermalState = false;
			_lastThermalCheck = DateTime.UtcNow;
			return false;
		}
		catch
		{
			_lastThermalState = false;
			_lastThermalCheck = DateTime.UtcNow;
			return false;
		}
	}
}
