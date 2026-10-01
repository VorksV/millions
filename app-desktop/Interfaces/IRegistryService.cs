using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace VoltrisOptimizer.Interfaces
{
    /// <summary>
    /// Resultado de uma operação de registro
    /// </summary>
    public class RegistryOperationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public object? PreviousValue { get; set; }
        public Exception? Exception { get; set; }
    }

    /// <summary>
    /// Backup de uma chave de registro
    /// </summary>
    public class RegistryBackup
    {
        public string KeyPath { get; set; } = string.Empty;
        public string ValueName { get; set; } = string.Empty;
        public object? Value { get; set; }
        public RegistryValueKind ValueKind { get; set; }
        public DateTime BackupTime { get; set; }
        public bool Existed { get; set; }
    }

    /// <summary>
    /// Interface para operações seguras de registro do Windows
    /// Fornece abstração, logging e backup automático
    /// </summary>
    public interface IRegistryService
    {
        /// <summary>
        /// Obtém um valor do registro
        /// </summary>
        /// <typeparam name="T">Tipo do valor esperado</typeparam>
        /// <param name="hive">Hive do registro (HKLM, HKCU, etc)</param>
        /// <param name="subKey">Caminho da subchave</param>
        /// <param name="valueName">Nome do valor</param>
        /// <param name="defaultValue">Valor padrão se não existir</param>
        /// <returns>Valor encontrado ou padrão</returns>
        T? GetValue<T>(RegistryHive hive, string subKey, string valueName, T? defaultValue = default);

        /// <summary>
        /// Define um valor no registro com backup automático
        /// </summary>
        /// <param name="hive">Hive do registro</param>
        /// <param name="subKey">Caminho da subchave</param>
        /// <param name="valueName">Nome do valor</param>
        /// <param name="value">Valor a definir</param>
        /// <param name="valueKind">Tipo do valor</param>
        /// <returns>Resultado da operação</returns>
        RegistryOperationResult SetValue(RegistryHive hive, string subKey, string valueName, object value, RegistryValueKind valueKind);

        /// <summary>
        /// Remove um valor do registro com backup automático
        /// </summary>
        /// <param name="hive">Hive do registro</param>
        /// <param name="subKey">Caminho da subchave</param>
        /// <param name="valueName">Nome do valor</param>
        /// <returns>Resultado da operação</returns>
        RegistryOperationResult DeleteValue(RegistryHive hive, string subKey, string valueName);

        /// <summary>
        /// Verifica se uma chave existe
        /// </summary>
        bool KeyExists(RegistryHive hive, string subKey);

        /// <summary>
        /// Verifica se um valor existe
        /// </summary>
        bool ValueExists(RegistryHive hive, string subKey, string valueName);

        /// <summary>
        /// Cria uma subchave se não existir
        /// </summary>
        RegistryOperationResult CreateKey(RegistryHive hive, string subKey);

        /// <summary>
        /// Remove uma subchave (com todas as subchaves filhas)
        /// </summary>
        RegistryOperationResult DeleteKey(RegistryHive hive, string subKey);

        /// <summary>
        /// Obtém todos os nomes de valores de uma chave
        /// </summary>
        string[] GetValueNames(RegistryHive hive, string subKey);

        /// <summary>
        /// Obtém todos os nomes de subchaves
        /// </summary>
        string[] GetSubKeyNames(RegistryHive hive, string subKey);

        /// <summary>
        /// Faz backup de um valor antes de modificar
        /// </summary>
        RegistryBackup BackupValue(RegistryHive hive, string subKey, string valueName);

        /// <summary>
        /// Restaura um valor a partir de um backup
        /// </summary>
        RegistryOperationResult RestoreValue(RegistryBackup backup);

        /// <summary>
        /// Obtém todos os backups realizados na sessão atual
        /// </summary>
        IReadOnlyList<RegistryBackup> GetSessionBackups();

        /// <summary>
        /// Restaura todos os backups da sessão atual
        /// </summary>
        void RestoreAllSessionBackups();

        /// <summary>
        /// Limpa os backups da sessão atual
        /// </summary>
        void ClearSessionBackups();
    }
}

