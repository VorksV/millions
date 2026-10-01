using System;
using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Security.Principal;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Core.Body.Arms;

public sealed class NetworkArm : INetworkArm, IDisposable
{
	private readonly ILoggingService _logger;

	private string _activeNicName = string.Empty;

	private string _activeNicGuid = string.Empty;

	private object? _originalNetworkThrottlingIndex;

	private object? _originalTcpAckFrequency;

	private object? _originalTcpNoDelay;

	private object? _originalTcpDelAckTicks;

	private readonly bool _isAdmin;

	public string ActiveNicName => _activeNicName;

	public string ActiveNicGuid => _activeNicGuid;

	public NetworkArm(ILoggingService logger)
	{
		_logger = logger ?? throw new ArgumentNullException("logger");
		_isAdmin = IsRunningAsAdmin();
		Task.Run(delegate
		{
			GetActiveNic();
		});
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

	private void GetActiveNic()
	{
		try
		{
			NetworkInterface[] allNetworkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
			NetworkInterface[] array = allNetworkInterfaces;
			foreach (NetworkInterface networkInterface in array)
			{
				if (networkInterface.OperationalStatus == OperationalStatus.Up && networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback && networkInterface.NetworkInterfaceType != NetworkInterfaceType.Tunnel && networkInterface.Speed > 0)
				{
					IPInterfaceProperties iPProperties = networkInterface.GetIPProperties();
					if (iPProperties.GatewayAddresses.Count > 0)
					{
						_activeNicName = networkInterface.Description;
						_activeNicGuid = networkInterface.Id;
						ILoggingService logger = _logger;
						DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(45, 2);
						defaultInterpolatedStringHandler.AppendLiteral("[ARM - NETWORK] NIC ativa detectada: ");
						defaultInterpolatedStringHandler.AppendFormatted(networkInterface.Description);
						defaultInterpolatedStringHandler.AppendLiteral(" (");
						defaultInterpolatedStringHandler.AppendFormatted(networkInterface.Speed / 1000000);
						defaultInterpolatedStringHandler.AppendLiteral(" Mbps)");
						logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
						_logger.LogInfo("[ARM - NETWORK] NIC GUID: " + networkInterface.Id);
						return;
					}
				}
			}
			NetworkInterface[] array2 = allNetworkInterfaces;
			foreach (NetworkInterface networkInterface2 in array2)
			{
				if (networkInterface2.OperationalStatus == OperationalStatus.Up && networkInterface2.NetworkInterfaceType != NetworkInterfaceType.Loopback && networkInterface2.NetworkInterfaceType != NetworkInterfaceType.Tunnel && networkInterface2.Speed > 0)
				{
					_activeNicName = networkInterface2.Description;
					_activeNicGuid = networkInterface2.Id;
					ILoggingService logger2 = _logger;
					DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(56, 2);
					defaultInterpolatedStringHandler.AppendLiteral("[ARM - NETWORK] NIC ativa detectada (fallback): ");
					defaultInterpolatedStringHandler.AppendFormatted(networkInterface2.Description);
					defaultInterpolatedStringHandler.AppendLiteral(" (");
					defaultInterpolatedStringHandler.AppendFormatted(networkInterface2.Speed / 1000000);
					defaultInterpolatedStringHandler.AppendLiteral(" Mbps)");
					logger2.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
					_logger.LogInfo("[ARM - NETWORK] NIC GUID: " + networkInterface2.Id);
					return;
				}
			}
			_logger.LogWarning("[ARM - NETWORK] Nenhuma NIC ativa detectada");
		}
		catch (Exception ex)
		{
			_logger.LogError("[ARM - NETWORK] ERRO ao detectar NIC: " + ex.Message);
		}
	}

	public Task<ArmActionResult> OptimizeForGamingAsync(string gameProcessName)
	{
		string gameProcessName2 = gameProcessName;
		return Task.Run(delegate
		{
			Stopwatch stopwatch = Stopwatch.StartNew();
			try
			{
				_logger.LogInfo("[ARM - NETWORK] Aplicando otimizações de rede para " + gameProcessName2);
				CaptureOriginalNetworkValues();
				_originalNetworkThrottlingIndex = Registry.GetValue("HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile", "NetworkThrottlingIndex", null);
				Registry.SetValue("HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile", "NetworkThrottlingIndex", -1, RegistryValueKind.DWord);
				ILoggingService logger = _logger;
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(53, 1);
				defaultInterpolatedStringHandler.AppendLiteral("[ARM - NETWORK] NetworkThrottlingIndex: ");
				defaultInterpolatedStringHandler.AppendFormatted<object>(_originalNetworkThrottlingIndex ?? "null");
				defaultInterpolatedStringHandler.AppendLiteral(" → 0xFFFFFFFF");
				logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
				if (!string.IsNullOrEmpty(_activeNicGuid))
				{
					string keyName = "HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters\\Interfaces\\" + _activeNicGuid;
					_originalTcpNoDelay = Registry.GetValue(keyName, "TcpNoDelay", null);
					Registry.SetValue(keyName, "TcpNoDelay", 1, RegistryValueKind.DWord);
					ILoggingService logger3 = _logger;
					defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(32, 1);
					defaultInterpolatedStringHandler.AppendLiteral("[ARM - NETWORK] TcpNoDelay: ");
					defaultInterpolatedStringHandler.AppendFormatted<object>(_originalTcpNoDelay ?? "null");
					defaultInterpolatedStringHandler.AppendLiteral(" → 1");
					logger3.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
					_logger.LogInfo("[ARM - NETWORK] TcpAckFrequency/TcpDelAckTicks mantidos padrão (evitam picos de latência/packet loss em jogos)");
				}
                _logger.LogInfo("[ARM - NETWORK] Nenhuma política QoS/DSCP é criada pelo VOLTRIS. Motivo técnico: " +
                    "para um upload de vídeo CBR (≈6.000 kbps, pacotes de ~7,5 KB) sobre um link saturado, marcar DSCP " +
                    "não reduz latência e compete com o tráfego interativo do mesmo adaptador (áudio do chat, input do jogo). " +
                    "Latência de upload é dominada pelo tempo de codificação, pelo buffer de ingestão da plataforma (~1-2 s) " +
                    "e pelo atraso no lado do servidor — nenhum deles é afetado por DSCP. Por isso o VOLTRIS prioriza " +
                    "APENAS latência de loopback local e deixa a política de uplink sob controle do usuário/roteador.");

				ILoggingService logger4 = _logger;
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(71, 3);
				defaultInterpolatedStringHandler.AppendLiteral("[ARM - NETWORK] NIC = ");
				defaultInterpolatedStringHandler.AppendFormatted(_activeNicName);
				defaultInterpolatedStringHandler.AppendLiteral(" | Gaming tweaks aplicados | processo = ");
				defaultInterpolatedStringHandler.AppendFormatted(gameProcessName2);
				defaultInterpolatedStringHandler.AppendLiteral(" | em ");
				defaultInterpolatedStringHandler.AppendFormatted(stopwatch.ElapsedMilliseconds);
				defaultInterpolatedStringHandler.AppendLiteral(" ms");
				logger4.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
				return new ArmActionResult
				{
					Success = true,
					Message = "Network optimizations aplicadas",
					ExecutionTimeMs = stopwatch.ElapsedMilliseconds
				};
			}
			catch (Exception ex2)
			{
				_logger.LogError("[ARM - NETWORK] ERRO em OptimizeForGamingAsync: " + ex2.Message);
				return new ArmActionResult
				{
					Success = false,
					Message = ex2.Message,
					ExecutionTimeMs = stopwatch.ElapsedMilliseconds
				};
			}
		});
	}

	private void CaptureOriginalNetworkValues()
	{
		try
		{
			_originalNetworkThrottlingIndex = Registry.GetValue("HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile", "NetworkThrottlingIndex", null);
			if (!string.IsNullOrEmpty(_activeNicGuid))
			{
				string keyName = "HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters\\Interfaces\\" + _activeNicGuid;
				_originalTcpAckFrequency = Registry.GetValue(keyName, "TCPAckFrequency", null);
				_originalTcpNoDelay = Registry.GetValue(keyName, "TcpNoDelay", null);
				_originalTcpDelAckTicks = Registry.GetValue(keyName, "TcpDelAckTicks", null);
			}
		}
		catch (Exception ex)
		{
			_logger.LogError("[ARM - NETWORK] ERRO ao capturar valores originais: " + ex.Message);
		}
	}

	public Task<ArmActionResult> SetInterruptAffinityAsync()
	{
		return Task.Run(delegate
		{
			Stopwatch stopwatch = Stopwatch.StartNew();
			try
			{
				if (!_isAdmin)
				{
					_logger.LogWarning("[ARM - NETWORK] Interrupt Affinity: REQUER ADMIN pulado");
					return new ArmActionResult
					{
						Success = false,
						Message = "Requer admin",
						ExecutionTimeMs = stopwatch.ElapsedMilliseconds
					};
				}
				if (string.IsNullOrEmpty(_activeNicGuid))
				{
					_logger.LogWarning("[ARM - NETWORK] NIC GUID não disponível Interrupt Affinity pulado");
					return new ArmActionResult
					{
						Success = false,
						Message = "NIC GUID não disponível",
						ExecutionTimeMs = stopwatch.ElapsedMilliseconds
					};
				}
				try
				{
					string text = null;
					try
					{
						using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("SELECT PNPDeviceID FROM Win32_NetworkAdapter WHERE GUID = '" + _activeNicGuid + "'");
						using ManagementObjectCollection.ManagementObjectEnumerator managementObjectEnumerator = managementObjectSearcher.Get().GetEnumerator();
						if (managementObjectEnumerator.MoveNext())
						{
							ManagementObject managementObject = (ManagementObject)managementObjectEnumerator.Current;
							text = managementObject["PNPDeviceID"]?.ToString();
						}
					}
					catch (Exception ex)
					{
						_logger.LogWarning("[ARM - NETWORK] WMI falhou ao encontrar PNPDeviceID: " + ex.Message);
					}
					if (string.IsNullOrEmpty(text))
					{
						_logger.LogWarning("[ARM - NETWORK] PNPDeviceID não encontrado para NIC ativa, interrupt affinity pulado");
						return new ArmActionResult
						{
							Success = false,
							Message = "PNPDeviceID da NIC não encontrado",
							ExecutionTimeMs = stopwatch.ElapsedMilliseconds
						};
					}
					_logger.LogInfo("[ARM - NETWORK] NIC PNPDeviceID detectado: " + text);
					string text2 = "HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Enum\\" + text;
					string keyName = text2 + "\\Device Parameters\\Interrupt Management\\MessageSignaledInterruptProperties";
					Registry.SetValue(keyName, "MSISupported", 1, RegistryValueKind.DWord);
					_logger.LogInfo("[ARM - NETWORK] MSI habilitado para NIC");
					string keyName2 = text2 + "\\Device Parameters\\Interrupt Management\\AffinityPolicy";
					Registry.SetValue(keyName2, "AssignmentSetOverride", 2, RegistryValueKind.DWord);
					Registry.SetValue(keyName2, "DevicePriority", 3, RegistryValueKind.DWord);
					_logger.LogInfo("[ARM - NETWORK] Interrupt affinity: NIC Core 1 (bitmask = 0x2) | MSI = habilitado | Device: " + text);
				}
				catch (Exception ex2)
				{
					_logger.LogWarning("[ARM - NETWORK] ERRO ao configurar interrupt affinity: " + ex2.Message);
					return new ArmActionResult
					{
						Success = false,
						Message = "ERRO ao configurar interrupt affinity",
						ExecutionTimeMs = stopwatch.ElapsedMilliseconds
					};
				}
				return new ArmActionResult
				{
					Success = true,
					Message = "Interrupt affinity configurado",
					ExecutionTimeMs = stopwatch.ElapsedMilliseconds
				};
			}
			catch (Exception ex3)
			{
				_logger.LogError("[ARM - NETWORK] ERRO em SetInterruptAffinityAsync: " + ex3.Message);
				return new ArmActionResult
				{
					Success = false,
					Message = ex3.Message,
					ExecutionTimeMs = stopwatch.ElapsedMilliseconds
				};
			}
		});
	}

	public Task<ArmActionResult> RestoreNetworkDefaultsAsync()
	{
		return Task.Run(delegate
		{
			Stopwatch stopwatch = Stopwatch.StartNew();
			try
			{
				_logger.LogInfo("[ARM - NETWORK] Restaurando defaults de rede");
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler;
				if (_originalNetworkThrottlingIndex != null)
				{
					Registry.SetValue("HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile", "NetworkThrottlingIndex", _originalNetworkThrottlingIndex, RegistryValueKind.DWord);
					ILoggingService logger = _logger;
					defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(51, 1);
					defaultInterpolatedStringHandler.AppendLiteral("[ARM - NETWORK] NetworkThrottlingIndex restaurado: ");
					defaultInterpolatedStringHandler.AppendFormatted<object>(_originalNetworkThrottlingIndex);
					logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
				}
				else
				{
					_logger.LogInfo("[ARM - NETWORK] NetworkThrottlingIndex não existia originalmente. Ignorando restauração.");
				}
				if (!string.IsNullOrEmpty(_activeNicGuid))
				{
					string keyName = "HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters\\Interfaces\\" + _activeNicGuid;
					if (_originalTcpAckFrequency != null)
					{
						Registry.SetValue(keyName, "TCPAckFrequency", _originalTcpAckFrequency, RegistryValueKind.DWord);
						ILoggingService logger2 = _logger;
						defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(44, 1);
						defaultInterpolatedStringHandler.AppendLiteral("[ARM - NETWORK] TCPAckFrequency restaurado: ");
						defaultInterpolatedStringHandler.AppendFormatted<object>(_originalTcpAckFrequency);
						logger2.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
					}
					if (_originalTcpNoDelay != null)
					{
						Registry.SetValue(keyName, "TcpNoDelay", _originalTcpNoDelay, RegistryValueKind.DWord);
						ILoggingService logger3 = _logger;
						defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(39, 1);
						defaultInterpolatedStringHandler.AppendLiteral("[ARM - NETWORK] TcpNoDelay restaurado: ");
						defaultInterpolatedStringHandler.AppendFormatted<object>(_originalTcpNoDelay);
						logger3.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
					}
					if (_originalTcpDelAckTicks != null)
					{
						Registry.SetValue(keyName, "TcpDelAckTicks", _originalTcpDelAckTicks, RegistryValueKind.DWord);
						ILoggingService logger4 = _logger;
						defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(43, 1);
						defaultInterpolatedStringHandler.AppendLiteral("[ARM - NETWORK] TcpDelAckTicks restaurado: ");
						defaultInterpolatedStringHandler.AppendFormatted<object>(_originalTcpDelAckTicks);
						logger4.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
					}
				}
				if (_isAdmin)
				{
					try
					{
						Process.Start(new ProcessStartInfo
						{
							FileName = "powershell.exe",
							Arguments = "-NoProfile -NonInteractive -Command \"Get-NetQosPolicy -Name 'VoltrisQoS_*' -ErrorAction SilentlyContinue|Remove-NetQosPolicy -Confirm:$false\"",
							UseShellExecute = false,
							RedirectStandardOutput = true,
							CreateNoWindow = true
						})?.WaitForExit();
						_logger.LogInfo("[ARM - NETWORK] QoS policy removida: VoltrisQoS_*");
					}
					catch (Exception ex)
					{
						_logger.LogWarning("[ARM - NETWORK] ERRO ao remover QoS policy: " + ex.Message);
					}
				}
				ILoggingService logger5 = _logger;
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(58, 1);
				defaultInterpolatedStringHandler.AppendLiteral("[ARM - NETWORK] Configurações de rede restauradas | em ");
				defaultInterpolatedStringHandler.AppendFormatted(stopwatch.ElapsedMilliseconds);
				defaultInterpolatedStringHandler.AppendLiteral(" ms");
				logger5.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
				return new ArmActionResult
				{
					Success = true,
					Message = "Configurações de rede restauradas",
					ExecutionTimeMs = stopwatch.ElapsedMilliseconds
				};
			}
			catch (Exception ex2)
			{
				_logger.LogError("[ARM - NETWORK] ERRO em RestoreNetworkDefaultsAsync: " + ex2.Message);
				return new ArmActionResult
				{
					Success = false,
					Message = ex2.Message,
					ExecutionTimeMs = stopwatch.ElapsedMilliseconds
				};
			}
		});
	}

	public void Dispose()
	{
	}
}
