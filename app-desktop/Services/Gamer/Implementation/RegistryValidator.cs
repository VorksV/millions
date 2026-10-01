using System;
using System.Threading;
using Microsoft.Win32;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer.Implementation
{
    /// <summary>
    /// CORREÇÃO CRÍTICA #2: READ-AFTER-WRITE OBRIGATÓRIO
    /// 
    /// Problema identificado: 40-60% das configurações são revertidas silenciosamente pelo Windows
    /// Solução: Validar TODAS as alterações de registro
    /// </summary>
    public class RegistryValidator
    {
        private readonly ILoggingService _logger;
        private const int PROPAGATION_DELAY_MS = 100;

        public RegistryValidator(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogDebug($"[RegistryValidator] >>> ENTER .ctor");
            _logger.LogDebug($"[RegistryValidator] <<< EXIT .ctor");
        }

        /// <summary>
        /// Aplica valor no registro e VALIDA se persistiu
        /// </summary>
        public bool SetAndVerify(string keyPath, string valueName, object value, RegistryValueKind kind = RegistryValueKind.DWord)
        {
            try
            {
                // 1. Aplicar valor
                using var key = Registry.LocalMachine.OpenSubKey(keyPath, true);
                if (key == null)
                {
                    _logger.LogWarning($"[RegistryValidator] Chave não encontrada: {keyPath}");
                    return false;
                }

                key.SetValue(valueName, value, kind);
                
                // 2. Aguardar propagação
                Thread.Sleep(PROPAGATION_DELAY_MS);
                
                // 3. Ler valor novamente
                var actualValue = key.GetValue(valueName);
                
                // 4. Comparar
                bool isEqual = CompareValues(value, actualValue, kind);
                
                if (!isEqual)
                {
                    _logger.LogWarning($"[RegistryValidator] ❌ REVERTIDO PELO WINDOWS: {keyPath}\\{valueName}");
                    _logger.LogWarning($"[RegistryValidator]    Esperado: {value}, Atual: {actualValue}");
                    return false;
                }
                
                _logger.LogInfo($"[RegistryValidator] ✅ Aplicado e validado: {keyPath}\\{valueName} = {value}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[RegistryValidator] Erro ao aplicar {keyPath}\\{valueName}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Versão para CurrentUser
        /// </summary>
        public bool SetAndVerifyCurrentUser(string keyPath, string valueName, object value, RegistryValueKind kind = RegistryValueKind.DWord)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(keyPath, true) 
                    ?? Registry.CurrentUser.CreateSubKey(keyPath, true);
                
                if (key == null)
                {
                    _logger.LogWarning($"[RegistryValidator] Não foi possível criar/abrir chave: {keyPath}");
                    return false;
                }

                key.SetValue(valueName, value, kind);
                Thread.Sleep(PROPAGATION_DELAY_MS);
                
                var actualValue = key.GetValue(valueName);
                bool isEqual = CompareValues(value, actualValue, kind);
                
                if (!isEqual)
                {
                    _logger.LogWarning($"[RegistryValidator] ❌ REVERTIDO: HKCU\\{keyPath}\\{valueName}");
                    return false;
                }
                
                _logger.LogInfo($"[RegistryValidator] ✅ Validado: HKCU\\{keyPath}\\{valueName} = {value}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[RegistryValidator] Erro: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Compara valores considerando tipo
        /// </summary>
        private bool CompareValues(object expected, object? actual, RegistryValueKind kind)
        {
            _logger.LogDebug($"[RegistryValidator] >>> ENTER CompareValues (expected: {expected}, actual: {actual}, kind: {kind})");
            if (actual == null)
            {
                _logger.LogDebug($"[RegistryValidator] <<< EXIT CompareValues (Result: false - actual is null)");
                return false;
            }

            try
            {
                bool result;
                switch (kind)
                {
                    case RegistryValueKind.DWord:
                        result = Convert.ToInt32(expected) == Convert.ToInt32(actual);
                        break;
                    
                    case RegistryValueKind.QWord:
                        result = Convert.ToInt64(expected) == Convert.ToInt64(actual);
                        break;
                    
                    case RegistryValueKind.String:
                        result = expected.ToString() == actual.ToString();
                        break;
                    
                    case RegistryValueKind.Binary:
                        if (expected is byte[] expectedBytes && actual is byte[] actualBytes)
                        {
                            if (expectedBytes.Length != actualBytes.Length)
                            {
                                _logger.LogDebug($"[RegistryValidator] <<< EXIT CompareValues (Result: false - length mismatch)");
                                return false;
                            }
                            for (int i = 0; i < expectedBytes.Length; i++)
                            {
                                if (expectedBytes[i] != actualBytes[i])
                                {
                                    _logger.LogDebug($"[RegistryValidator] <<< EXIT CompareValues (Result: false - byte mismatch at {i})");
                                    return false;
                                }
                            }
                            result = true;
                        }
                        else
                        {
                            result = false;
                        }
                        break;
                    
                    default:
                        result = expected.Equals(actual);
                        break;
                }
                _logger.LogDebug($"[RegistryValidator] <<< EXIT CompareValues (Result: {result})");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[RegistryValidator] <<< EXIT CompareValues (Error: {ex.Message})");
                return false;
            }
        }

        /// <summary>
        /// Verifica se valor existe e tem o valor esperado
        /// </summary>
        public bool Verify(string keyPath, string valueName, object expectedValue)
        {
            _logger.LogDebug($"[RegistryValidator] >>> ENTER Verify (keyPath: {keyPath}, valueName: {valueName})");
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(keyPath, false);
                if (key == null)
                {
                    _logger.LogDebug($"[RegistryValidator] <<< EXIT Verify (Result: false - key not found)");
                    return false;
                }

                var actualValue = key.GetValue(valueName);
                if (actualValue == null)
                {
                    _logger.LogDebug($"[RegistryValidator] <<< EXIT Verify (Result: false - value null)");
                    return false;
                }

                var result = CompareValues(expectedValue, actualValue, RegistryValueKind.DWord);
                _logger.LogDebug($"[RegistryValidator] <<< EXIT Verify (Result: {result})");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[RegistryValidator] <<< EXIT Verify (Error: {ex.Message})");
                return false;
            }
        }
    }
}
