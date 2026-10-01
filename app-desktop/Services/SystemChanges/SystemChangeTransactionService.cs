using System;
using System.Collections.Generic;
using Microsoft.Win32;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.SystemChanges
{
    public class SystemChangeTransactionService : ISystemChangeTransactionService
    {
        private readonly ILoggingService _logger;

        public SystemChangeTransactionService(ILoggingService logger)
        {
            _logger = logger;
        }

        public ISystemTransaction Begin(string name) => new SystemTransaction(_logger, name);

        private class SystemTransaction : ISystemTransaction
        {
            private readonly ILoggingService _logger;
            private readonly string _name;
            private readonly List<Action> _rollbackActions = new();
            private bool _committed;

            public SystemTransaction(ILoggingService logger, string name)
            {
                _logger = logger;
                _name = name;
                _logger.LogInfo($"[Tx] Iniciada transação de sistema: {_name}");
            }

            public void RegisterRegistryChange(string rootPath, string valueName, object? oldValue, object? newValue, bool currentUser = false)
            {
                _logger.LogInfo($"[Tx] Registro: {(currentUser ? "HKCU" : "HKLM")}\\{rootPath} {valueName}: {oldValue} -> {newValue}");
                _rollbackActions.Add(() =>
                {
                    try
                    {
                        var root = currentUser ? Registry.CurrentUser : Registry.LocalMachine;
                        using var key = root.OpenSubKey(rootPath, true);
                        if (key != null)
                        {
                            if (oldValue == null)
                                key.DeleteValue(valueName, false);
                            else
                                key.SetValue(valueName, oldValue);
                        }
                    }
                    catch { }
                });
            }

            public void RegisterServiceChange(string serviceName, int? oldStartType, int? newStartType)
            {
                _logger.LogInfo($"[Tx] Serviço: {serviceName} Start {oldStartType} -> {newStartType}");
                _rollbackActions.Add(() =>
                {
                    try
                    {
                        using var key = Registry.LocalMachine.OpenSubKey($"SYSTEM\\CurrentControlSet\\Services\\{serviceName}", true);
                        if (key != null && oldStartType.HasValue)
                            key.SetValue("Start", oldStartType.Value, RegistryValueKind.DWord);
                    }
                    catch { }
                });
            }

            public void RegisterTaskChange(string taskPath, bool enabledBefore, bool enabledAfter)
            {
                _logger.LogInfo($"[Tx] Tarefa: {taskPath} Enabled {enabledBefore} -> {enabledAfter}");
                _rollbackActions.Add(() =>
                {
                    try
                    {
                        var args = enabledBefore
                            ? $"/Change /TN \"{taskPath}\" /Enable"
                            : $"/Change /TN \"{taskPath}\" /Disable";
                        var psi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "schtasks",
                            Arguments = args,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var p = System.Diagnostics.Process.Start(psi);
                        p?.WaitForExit(3000);
                    }
                    catch { }
                });
            }

            public void Commit()
            {
                _committed = true;
                _logger.LogSuccess($"[Tx] Commit: {_name}");
            }

            public void Rollback()
            {
                _logger.LogWarning($"[Tx] Rollback: {_name}");
                foreach (var action in _rollbackActions)
                {
                    try { action(); } catch { }
                }
            }

            public void Dispose()
            {
                if (!_committed)
                    Rollback();
            }
        }
    }
}
