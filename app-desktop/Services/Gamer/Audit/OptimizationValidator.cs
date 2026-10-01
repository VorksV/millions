using System;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer.Audit
{
    public static class OptimizationValidator
    {
        private static ILoggingService? _logger;

        public static void Initialize(ILoggingService logger)
        {
            _logger = logger;
            _logger.LogEntry(nameof(Initialize));
            _logger.LogExit(nameof(Initialize));
        }

        public static ValidationStatus ValidatePowerPlan(Guid expectedGuid)
        {
            _logger.LogEntry(nameof(ValidatePowerPlan));
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powercfg",
                        Arguments = "/getactivescheme",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true
                    }
                };
                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(2000);

                var match = Regex.Match(output, @"\{([0-9a-fA-F-]+)\}");
                if (match.Success && Guid.TryParse(match.Groups[1].Value, out var activeGuid))
                {
                    var result = activeGuid == expectedGuid
                        ? ValidationStatus.VALIDATED
                        : ValidationStatus.VALIDATION_FAILED;
                    _logger.LogExit(nameof(ValidatePowerPlan), result);
                    return result;
                }
                _logger.LogExit(nameof(ValidatePowerPlan), ValidationStatus.VALIDATION_FAILED);
                return ValidationStatus.VALIDATION_FAILED;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug($"[Validator] Erro ao validar PowerPlan: {ex.Message}");
                _logger.LogExit(nameof(ValidatePowerPlan), ValidationStatus.VALIDATION_FAILED);
                return ValidationStatus.VALIDATION_FAILED;
            }
        }

        public static ValidationStatus ValidateRegistryValue(string hive, string subKey, string valueName, object expectedValue)
        {
            _logger.LogEntry(nameof(ValidateRegistryValue));
            try
            {
                var actualValue = ReadRegistryValue(hive, subKey, valueName);
                if (actualValue == null)
                {
                    _logger.LogExit(nameof(ValidateRegistryValue), ValidationStatus.VALIDATION_FAILED);
                    return ValidationStatus.VALIDATION_FAILED;
                }

                var actualStr = actualValue.ToString();
                var expectedStr = expectedValue.ToString();

                var result = string.Equals(actualStr, expectedStr, StringComparison.OrdinalIgnoreCase)
                    ? ValidationStatus.VALIDATED
                    : ValidationStatus.VALIDATION_FAILED;
                _logger.LogExit(nameof(ValidateRegistryValue), result);
                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug($"[Validator] Erro ao validar Registry: {ex.Message}");
                _logger.LogExit(nameof(ValidateRegistryValue), ValidationStatus.VALIDATION_FAILED);
                return ValidationStatus.VALIDATION_FAILED;
            }
        }

        public static ValidationStatus ValidateHags(bool expectedEnabled)
        {
            _logger.LogEntry(nameof(ValidateHags));
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
                if (key == null) { _logger.LogExit(nameof(ValidateHags), ValidationStatus.VALIDATION_FAILED); return ValidationStatus.VALIDATION_FAILED; }

                var value = key.GetValue("HwSchMode");
                if (value is int hwSchMode)
                {
                    var isEnabled = hwSchMode == 2;
                    var result = isEnabled == expectedEnabled
                        ? ValidationStatus.VALIDATED
                        : ValidationStatus.VALIDATION_FAILED;
                    _logger.LogExit(nameof(ValidateHags), result);
                    return result;
                }
                _logger.LogExit(nameof(ValidateHags), ValidationStatus.VALIDATION_FAILED);
                return ValidationStatus.VALIDATION_FAILED;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug($"[Validator] Erro ao validar HAGS: {ex.Message}");
                _logger.LogExit(nameof(ValidateHags), ValidationStatus.VALIDATION_FAILED);
                return ValidationStatus.VALIDATION_FAILED;
            }
        }

        public static ValidationStatus ValidateCoreParking(int expectedPercent)
        {
            _logger.LogEntry(nameof(ValidateCoreParking));
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power\PowerSettings\54533251-82be-4824-96c1-47b60b740d00\0cc5b647-c1df-4637-891a-dec35c318583\DefaultPowerSchemeValues");
                if (key == null) { _logger.LogExit(nameof(ValidateCoreParking), ValidationStatus.VALIDATION_FAILED); return ValidationStatus.VALIDATION_FAILED; }

                var activePlanGuid = GetActivePowerPlanGuid();
                if (activePlanGuid == null) { _logger.LogExit(nameof(ValidateCoreParking), ValidationStatus.VALIDATION_FAILED); return ValidationStatus.VALIDATION_FAILED; }

                using var planKey = key.OpenSubKey(activePlanGuid.Value.ToString("B"));
                if (planKey == null) { _logger.LogExit(nameof(ValidateCoreParking), ValidationStatus.VALIDATION_FAILED); return ValidationStatus.VALIDATION_FAILED; }

                var value = planKey.GetValue("ACSettingIndex");
                if (value is int parkingPercent)
                {
                    var result = parkingPercent == expectedPercent
                        ? ValidationStatus.VALIDATED
                        : ValidationStatus.VALIDATION_FAILED;
                    _logger.LogExit(nameof(ValidateCoreParking), result);
                    return result;
                }
                _logger.LogExit(nameof(ValidateCoreParking), ValidationStatus.VALIDATION_FAILED);
                return ValidationStatus.VALIDATION_FAILED;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug($"[Validator] Erro ao validar CoreParking: {ex.Message}");
                _logger.LogExit(nameof(ValidateCoreParking), ValidationStatus.VALIDATION_FAILED);
                return ValidationStatus.VALIDATION_FAILED;
            }
        }

        public static ValidationStatus ValidateTimerResolution(double expectedMs)
        {
            _logger.LogEntry(nameof(ValidateTimerResolution));
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Executive");
                if (key == null) { _logger.LogExit(nameof(ValidateTimerResolution), ValidationStatus.VALIDATION_FAILED); return ValidationStatus.VALIDATION_FAILED; }

                var value = key.GetValue("GlobalTimerResolutionRequests");
                if (value is byte[] raw && raw.Length >= 8)
                {
                    var currentMs = BitConverter.ToUInt64(raw, 0) / 10000.0;
                    var result = Math.Abs(currentMs - expectedMs) < 0.1
                        ? ValidationStatus.VALIDATED
                        : ValidationStatus.VALIDATION_FAILED;
                    _logger.LogExit(nameof(ValidateTimerResolution), result);
                    return result;
                }
                _logger.LogExit(nameof(ValidateTimerResolution), ValidationStatus.VALIDATION_FAILED);
                return ValidationStatus.VALIDATION_FAILED;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug($"[Validator] Erro ao validar TimerResolution: {ex.Message}");
                _logger.LogExit(nameof(ValidateTimerResolution), ValidationStatus.VALIDATION_FAILED);
                return ValidationStatus.VALIDATION_FAILED;
            }
        }

        public static ValidationStatus ValidateServiceState(string serviceName, string expectedState)
        {
            _logger.LogEntry(nameof(ValidateServiceState));
            try
            {
                using var searcher = new ManagementObjectSearcher($"SELECT State FROM Win32_Service WHERE Name = '{serviceName}'");
                var results = searcher.Get().Cast<ManagementObject>().ToList();
                if (results.Count == 0) { _logger.LogExit(nameof(ValidateServiceState), ValidationStatus.VALIDATION_FAILED); return ValidationStatus.VALIDATION_FAILED; }

                var actualState = results[0]["State"]?.ToString();
                var result = string.Equals(actualState, expectedState, StringComparison.OrdinalIgnoreCase)
                    ? ValidationStatus.VALIDATED
                    : ValidationStatus.VALIDATION_FAILED;
                _logger.LogExit(nameof(ValidateServiceState), result);
                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug($"[Validator] Erro ao validar serviço {serviceName}: {ex.Message}");
                _logger.LogExit(nameof(ValidateServiceState), ValidationStatus.VALIDATION_FAILED);
                return ValidationStatus.VALIDATION_FAILED;
            }
        }

        public static ValidationStatus ValidateNetworkSetting(string interfaceName, string setting, string expectedValue)
        {
            _logger.LogEntry(nameof(ValidateNetworkSetting));
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "netsh",
                        Arguments = $"int show interface name=\"{interfaceName}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true
                    }
                };
                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(2000);

                var result = output.Contains(expectedValue, StringComparison.OrdinalIgnoreCase)
                    ? ValidationStatus.VALIDATED
                    : ValidationStatus.VALIDATION_FAILED;
                _logger.LogExit(nameof(ValidateNetworkSetting), result);
                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug($"[Validator] Erro ao validar Network: {ex.Message}");
                _logger.LogExit(nameof(ValidateNetworkSetting), ValidationStatus.VALIDATION_FAILED);
                return ValidationStatus.VALIDATION_FAILED;
            }
        }

        public static ValidationStatus ValidateProcessPriority(int processId, ProcessPriorityClass expectedPriority)
        {
            _logger.LogEntry(nameof(ValidateProcessPriority));
            try
            {
                using var process = Process.GetProcessById(processId);
                var result = process.PriorityClass == expectedPriority
                    ? ValidationStatus.VALIDATED
                    : ValidationStatus.VALIDATION_FAILED;
                _logger.LogExit(nameof(ValidateProcessPriority), result);
                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug($"[Validator] Erro ao validar prioridade do processo {processId}: {ex.Message}");
                _logger.LogExit(nameof(ValidateProcessPriority), ValidationStatus.VALIDATION_FAILED);
                return ValidationStatus.VALIDATION_FAILED;
            }
        }

        public static ValidationStatus ValidateVsyncDisabled()
        {
            _logger.LogEntry(nameof(ValidateVsyncDisabled));
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers");
                if (key == null) { _logger.LogExit(nameof(ValidateVsyncDisabled), ValidationStatus.VALIDATION_FAILED); return ValidationStatus.VALIDATION_FAILED; }

                foreach (var valueName in key.GetValueNames())
                {
                    var value = key.GetValue(valueName)?.ToString();
                    if (value != null && value.Contains("DWM8And16BitMitigation", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogExit(nameof(ValidateVsyncDisabled), ValidationStatus.VALIDATED);
                        return ValidationStatus.VALIDATED;
                    }
                }
                _logger.LogExit(nameof(ValidateVsyncDisabled), ValidationStatus.VALIDATION_FAILED);
                return ValidationStatus.VALIDATION_FAILED;
            }
            catch
            {
                _logger.LogExit(nameof(ValidateVsyncDisabled), ValidationStatus.VALIDATION_FAILED);
                return ValidationStatus.VALIDATION_FAILED;
            }
        }

        public static string ReadRegistryValueSafe(string hive, string subKey, string valueName)
        {
            _logger.LogEntry(nameof(ReadRegistryValueSafe));
            try
            {
                var value = ReadRegistryValue(hive, subKey, valueName);
                var result = value?.ToString() ?? "(null)";
                _logger.LogExit(nameof(ReadRegistryValueSafe));
                return result;
            }
            catch
            {
                _logger.LogExit(nameof(ReadRegistryValueSafe));
                return "(error)";
            }
        }

        private static object? ReadRegistryValue(string hive, string subKey, string valueName)
        {
            _logger.LogEntry(nameof(ReadRegistryValue));
            try
            {
                RegistryKey? baseKey = hive.ToUpperInvariant() switch
                {
                    "HKLM" or "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
                    "HKCU" or "HKEY_CURRENT_USER" => Registry.CurrentUser,
                    "HKCR" or "HKEY_CLASSES_ROOT" => Registry.ClassesRoot,
                    "HKU" or "HKEY_USERS" => Registry.Users,
                    "HKCC" or "HKEY_CURRENT_CONFIG" => Registry.CurrentConfig,
                    _ => null
                };

                if (baseKey == null) { _logger.LogExit(nameof(ReadRegistryValue)); return null; }
                using var key = baseKey.OpenSubKey(subKey);
                var val = key?.GetValue(valueName);
                _logger.LogExit(nameof(ReadRegistryValue));
                return val;
            }
            catch
            {
                _logger.LogExit(nameof(ReadRegistryValue));
                return null;
            }
        }

        private static Guid? GetActivePowerPlanGuid()
        {
            _logger.LogEntry(nameof(GetActivePowerPlanGuid));
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powercfg",
                        Arguments = "/getactivescheme",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true
                    }
                };
                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(2000);

                var match = Regex.Match(output, @"\{([0-9a-fA-F-]+)\}");
                if (match.Success && Guid.TryParse(match.Groups[1].Value, out var guid))
                {
                    _logger.LogExit(nameof(GetActivePowerPlanGuid), guid);
                    return guid;
                }
            }
            catch { }
            _logger.LogExit(nameof(GetActivePowerPlanGuid));
            return null;
        }
    }
}

