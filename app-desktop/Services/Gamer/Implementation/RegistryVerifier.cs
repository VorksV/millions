using System;
using System.Diagnostics;
using Microsoft.Win32;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer.Implementation
{
    /// <summary>
    /// ETAPA 5 - VERIFICAÇÃO DE CADA ALTERAÇÃO
    /// 
    /// Utility class para verificar se alterações de registro foram aplicadas corretamente.
    /// Nunca assume sucesso sem verificar.
    /// </summary>
    public static class RegistryVerifier
    {
        /// <summary>
        /// Aplica uma alteração de registro e verifica imediatamente se foi aplicada.
        /// </summary>
        /// <param name="logger">Serviço de logging</param>
        /// <param name="hive">Hive do registro</param>
        /// <param name="subKey">Chave do registro</param>
        /// <param name="valueName">Nome do valor</param>
        /// <param name="newValue">Novo valor a ser aplicado</param>
        /// <param name="valueKind">Tipo do valor</param>
        /// <param name="operationName">Nome da operação para logging</param>
        /// <returns>True se a alteração foi aplicada e verificada com sucesso</returns>
        public static bool ApplyAndVerify(
            ILoggingService logger,
            RegistryHive hive,
            string subKey,
            string valueName,
            object newValue,
            RegistryValueKind valueKind,
            string operationName)
        {
            logger.LogDebug($"[RegistryVerifier] >>> ENTER ApplyAndVerify (operation: {operationName})");
            var sw = Stopwatch.StartNew();
            string fullKey = $@"{hive}\{subKey}\{valueName}";
            
            try
            {
                // ETAPA 1: Ler estado atual ANTES de modificar
                object? currentValue = null;
                try
                {
                    using var baseKey = GetBaseKey(hive);
                    using var key = baseKey?.OpenSubKey(subKey, false);
                    if (key != null)
                    {
                        currentValue = key.GetValue(valueName);
                        logger.LogDebug($"[RegistryVerifier] 📖 Estado atual de {operationName}: {currentValue ?? "null"}");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning($"[RegistryVerifier] ⚠️ Não foi possível ler estado atual: {ex.Message}");
                }
                
                // ETAPA 2: Aplicar alteração
                logger.LogInfo($"[RegistryVerifier] 🔧 Aplicando {operationName}: {currentValue ?? "null"} → {newValue}");
                
                using var writeBaseKey = GetBaseKey(hive);
                using var writeKey = writeBaseKey?.OpenSubKey(subKey, true) ?? writeBaseKey?.CreateSubKey(subKey);
                
                if (writeKey == null)
                {
                    logger.LogError($"[RegistryVerifier] ❌ Falha ao abrir chave para escrita: {fullKey}");
                    logger.LogDebug($"[RegistryVerifier] <<< EXIT ApplyAndVerify (Result: false - failed to open key)");
                    return false;
                }
                
                writeKey.SetValue(valueName, newValue, valueKind);
                logger.LogDebug($"[RegistryVerifier] 📝 SetValue executado");
                
                // ETAPA 3: VERIFICAÇÃO IMEDIATA (CRÍTICO!)
                logger.LogInfo($"[RegistryVerifier] 🔍 Verificando aplicação...");
                
                // Aguardar propagação do registry (necessário em alguns casos)
                System.Threading.Thread.Sleep(10);
                
                using var verifyBaseKey = GetBaseKey(hive);
                using var verifyKey = verifyBaseKey?.OpenSubKey(subKey, false);
                
                if (verifyKey == null)
                {
                    logger.LogError($"[RegistryVerifier] ❌ Falha ao abrir chave para verificação: {fullKey}");
                    logger.LogDebug($"[RegistryVerifier] <<< EXIT ApplyAndVerify (Result: false - failed to open verify key)");
                    return false;
                }
                
                var verifiedValue = verifyKey.GetValue(valueName);
                bool success = Equals(verifiedValue, newValue);
                
                sw.Stop();
                
                if (success)
                {
                    logger.LogSuccess($"[RegistryVerifier] ✅ VERIFICAÇÃO: {operationName} aplicado corretamente! ({sw.ElapsedMilliseconds}ms)");
                    logger.LogDebug($"[RegistryVerifier] <<< EXIT ApplyAndVerify (Result: true, Duration: {sw.ElapsedMilliseconds}ms)");
                    return true;
                }
                else
                {
                    logger.LogError($"[RegistryVerifier] ❌ VERIFICAÇÃO FALHOU: {operationName}");
                    logger.LogError($"[RegistryVerifier]   Valor esperado: {newValue}");
                    logger.LogError($"[RegistryVerifier]   Valor obtido: {verifiedValue ?? "null"}");
                    logger.LogError($"[RegistryVerifier]   Tipo esperado: {newValue?.GetType().Name ?? "null"}");
                    logger.LogError($"[RegistryVerifier]   Tipo obtido: {verifiedValue?.GetType().Name ?? "null"}");
                    logger.LogDebug($"[RegistryVerifier] <<< EXIT ApplyAndVerify (Result: false - verification failed)");
                    return false;
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                logger.LogError($"[RegistryVerifier] ❌ EXCEÇÃO em {operationName}: {ex.Message}", ex);
                logger.LogError($"[RegistryVerifier] ⏱️ Tempo até falha: {sw.ElapsedMilliseconds}ms");
                logger.LogDebug($"[RegistryVerifier] <<< EXIT ApplyAndVerify (Error: {ex.Message})");
                return false;
            }
        }
        
        /// <summary>
        /// Verifica se um valor de registro existe e tem o valor esperado.
        /// </summary>
        public static bool VerifyValue(
            ILoggingService logger,
            RegistryHive hive,
            string subKey,
            string valueName,
            object expectedValue,
            string operationName)
        {
            logger.LogDebug($"[RegistryVerifier] >>> ENTER VerifyValue (operation: {operationName})");
            try
            {
                using var baseKey = GetBaseKey(hive);
                using var key = baseKey?.OpenSubKey(subKey, false);
                
                if (key == null)
                {
                    logger.LogError($"[RegistryVerifier] ❌ Chave não existe: {hive}\\{subKey}");
                    logger.LogDebug($"[RegistryVerifier] <<< EXIT VerifyValue (Result: false - key not found)");
                    return false;
                }
                
                var actualValue = key.GetValue(valueName);
                bool matches = Equals(actualValue, expectedValue);
                
                if (matches)
                {
                    logger.LogSuccess($"[RegistryVerifier] ✅ {operationName} verificado: {expectedValue}");
                }
                else
                {
                    logger.LogWarning($"[RegistryVerifier] ⚠️ {operationName} não corresponde");
                    logger.LogWarning($"[RegistryVerifier]   Esperado: {expectedValue}");
                    logger.LogWarning($"[RegistryVerifier]   Obtido: {actualValue ?? "null"}");
                }
                
                logger.LogDebug($"[RegistryVerifier] <<< EXIT VerifyValue (Result: {matches})");
                return matches;
            }
            catch (Exception ex)
            {
                logger.LogError($"[RegistryVerifier] ❌ Erro ao verificar {operationName}: {ex.Message}", ex);
                logger.LogDebug($"[RegistryVerifier] <<< EXIT VerifyValue (Error: {ex.Message})");
                return false;
            }
        }
        
        /// <summary>
        /// Obtém a chave base para um hive.
        /// </summary>
        private static RegistryKey? GetBaseKey(RegistryHive hive)
        {
            var result = hive switch
            {
                RegistryHive.CurrentUser => Registry.CurrentUser,
                RegistryHive.LocalMachine => Registry.LocalMachine,
                RegistryHive.ClassesRoot => Registry.ClassesRoot,
                RegistryHive.Users => Registry.Users,
                RegistryHive.CurrentConfig => Registry.CurrentConfig,
                _ => null
            };
            return result;
        }
        
        /// <summary>
        /// Compara dois valores considerando tipos diferentes.
        /// </summary>
        private static bool Equals(object? a, object? b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            
            // Mesmo tipo - comparação direta
            if (a.GetType() == b.GetType())
            {
                return a.Equals(b);
            }
            
            // Tipos diferentes - tentar conversão
            try
            {
                // Converter para o tipo de 'a'
                var converted = Convert.ChangeType(b, a.GetType());
                return a.Equals(converted);
            }
            catch
            {
                return false;
            }
        }
    }
}