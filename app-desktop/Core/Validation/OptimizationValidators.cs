using System;
using System.Diagnostics;
using System.ServiceProcess;
using Microsoft.Win32;

namespace VoltrisOptimizer.Core.Validation
{
    /// <summary>
    /// Validadores que PROVAM que otimizações foram aplicadas no Windows real.
    /// Nenhuma otimização é considerada bem-sucedida sem validação pós-aplicação.
    /// </summary>
    public static class OptimizationValidators
    {
        /// <summary>
        /// Valida que valor de registro foi realmente escrito
        /// </summary>
        public static OperationResult ValidateRegistryValue(
            RegistryKey rootKey,
            string keyPath,
            string valueName,
            object expectedValue,
            RegistryValueKind expectedKind)
        {
            try
            {
                using var key = rootKey.OpenSubKey(keyPath, false);
                
                if (key == null)
                {
                    App.LoggingService?.LogWarning($"[VALIDATOR] Registro não encontrado: {rootKey.Name}\\{keyPath}");
                    return OperationResult.CreateFailure(
                        $"Registry key not found: {rootKey.Name}\\{keyPath}",
                        "Key does not exist after write operation");
                }
                
                var actualValue = key.GetValue(valueName);
                
                if (actualValue == null)
                {
                    App.LoggingService?.LogWarning($"[VALIDATOR] Valor de registro '{valueName}' não encontrado em {keyPath}");
                    return OperationResult.CreateFailure(
                        $"Registry value '{valueName}' not found",
                        $"Value does not exist in key {keyPath}");
                }
                
                // Validar tipo
                var actualKind = key.GetValueKind(valueName);
                if (actualKind != expectedKind)
                {
                    return OperationResult.CreateFailure(
                        $"Registry value type mismatch",
                        $"Expected {expectedKind}, got {actualKind}");
                }
                
                // Validar valor
                bool valuesMatch = false;
                
                if (expectedKind == RegistryValueKind.DWord)
                {
                    var expected = Convert.ToInt32(expectedValue);
                    var actual = Convert.ToInt32(actualValue);
                    valuesMatch = (expected == actual);
                }
                else if (expectedKind == RegistryValueKind.String)
                {
                    valuesMatch = expectedValue.ToString() == actualValue.ToString();
                }
                else
                {
                    valuesMatch = expectedValue.Equals(actualValue);
                }
                
                if (!valuesMatch)
                {
                    App.LoggingService?.LogWarning($"[VALIDATOR] Divergência no registro '{valueName}'. Esperado: '{expectedValue}', Atual: '{actualValue}'");
                    return OperationResult.CreateFailure(
                        $"Registry value mismatch",
                        $"Expected '{expectedValue}', got '{actualValue}'");
                }
                
                App.LoggingService?.LogSuccess($"[VALIDATOR] Registro validado com sucesso: {valueName} = {actualValue}");
                return OperationResult.CreateSuccess(
                    $"Registry value validated: {valueName} = {actualValue}",
                    validated: true)
                    .WithMetadata("KeyPath", keyPath)
                    .WithMetadata("ValueName", valueName)
                    .WithMetadata("ActualValue", actualValue);
            }
            catch (UnauthorizedAccessException ex)
            {
                return OperationResult.CreateFailure(
                    "Access denied to registry",
                    $"Insufficient permissions: {ex.Message}");
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure(
                    "Registry validation failed",
                    ex.Message);
            }
        }
        
        /// <summary>
        /// Valida que serviço do Windows foi realmente configurado
        /// </summary>
        public static OperationResult ValidateServiceStartupType(
            string serviceName,
            ServiceStartMode expectedStartMode)
        {
            try
            {
                using var service = new ServiceController(serviceName);
                
                // Forçar refresh do estado
                service.Refresh();
                
                var actualStartMode = service.StartType;
                
                if (actualStartMode != expectedStartMode)
                {
                    App.LoggingService?.LogWarning($"[VALIDATOR] Startup do serviço '{serviceName}' incorreto. Esperado: {expectedStartMode}, Atual: {actualStartMode}");
                    return OperationResult.CreateFailure(
                        $"Service startup type mismatch",
                        $"Expected {expectedStartMode}, got {actualStartMode} for service '{serviceName}'");
                }
                
                App.LoggingService?.LogSuccess($"[VALIDATOR] Serviço validado: {serviceName} = {actualStartMode}");
                return OperationResult.CreateSuccess(
                    $"Service validated: {serviceName} = {actualStartMode}",
                    validated: true)
                    .WithMetadata("ServiceName", serviceName)
                    .WithMetadata("StartMode", actualStartMode)
                    .WithMetadata("ServiceStatus", service.Status.ToString());
            }
            catch (InvalidOperationException ex)
            {
                return OperationResult.CreateFailure(
                    $"Service '{serviceName}' not found",
                    ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                return OperationResult.CreateFailure(
                    "Access denied to service",
                    $"Insufficient permissions: {ex.Message}");
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure(
                    "Service validation failed",
                    ex.Message);
            }
        }
        
        /// <summary>
        /// Valida que plano de energia está ativo
        /// </summary>
        public static OperationResult ValidatePowerPlan(string expectedGuid)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powercfg.exe",
                    Arguments = "/getactivescheme",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                
                using var process = Process.Start(psi);
                if (process == null)
                {
                    return OperationResult.CreateFailure(
                        "Failed to start powercfg.exe",
                        "Process.Start returned null");
                }
                
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                
                if (process.ExitCode != 0)
                {
                    return OperationResult.CreateFailure(
                        $"powercfg.exe failed with exit code {process.ExitCode}",
                        error);
                }
                
                // Output format: "Power Scheme GUID: <guid> (<name>)"
                if (!output.Contains(expectedGuid, StringComparison.OrdinalIgnoreCase))
                {
                    App.LoggingService?.LogWarning($"[VALIDATOR] Plano de energia {expectedGuid} não está ativo.");
                    return OperationResult.CreateFailure(
                        "Power plan not active",
                        $"Expected GUID {expectedGuid} not found in active scheme. Output: {output}");
                }
                
                App.LoggingService?.LogSuccess($"[VALIDATOR] Plano de energia validado: {expectedGuid}");
                return OperationResult.CreateSuccess(
                    $"Power plan validated: {expectedGuid}",
                    validated: true)
                    .WithMetadata("ExpectedGuid", expectedGuid)
                    .WithMetadata("PowercfgOutput", output.Trim());
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure(
                    "Power plan validation failed",
                    ex.Message);
            }
        }
        
        /// <summary>
        /// Valida que processo foi executado com sucesso
        /// </summary>
        public static OperationResult ValidateProcessExecution(
            string fileName,
            string arguments,
            int expectedExitCode = 0)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                
                using var process = Process.Start(psi);
                if (process == null)
                {
                    return OperationResult.CreateFailure(
                        $"Failed to start {fileName}",
                        "Process.Start returned null");
                }
                
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                
                if (process.ExitCode != expectedExitCode)
                {
                    return OperationResult.CreateFailure(
                        $"{fileName} failed with exit code {process.ExitCode}",
                        $"Expected {expectedExitCode}. Error: {error}");
                }
                
                return OperationResult.CreateSuccess(
                    $"Process executed successfully: {fileName} {arguments}",
                    validated: true)
                    .WithMetadata("FileName", fileName)
                    .WithMetadata("Arguments", arguments)
                    .WithMetadata("ExitCode", process.ExitCode)
                    .WithMetadata("Output", output.Trim());
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure(
                    "Process execution failed",
                    ex.Message);
            }
        }
    }
}
