using System;
using System.IO;
using Microsoft.Win32;

namespace VoltrisOptimizer.Services.Performance.CpuTuning
{
    /// <summary>
    /// Monitors system stability and triggers automatic rollback on crashes.
    /// Monitors backend health, MSR/MMIO operations, thermal protection, and rollback events.
    /// </summary>
    public class StabilityWatchdog : IStabilityWatchdog
    {
        private readonly ILoggingService _logger;
        private const string RegistryPath = @"SOFTWARE\VoltrisOptimizer\CpuTuning";
        private const string FailureCountKey = "FailureCount";
        private const string LastCrashKey = "LastCrashTime";
        private const string DisabledKey = "TuningDisabled";
        private const string BackendFailureCountKey = "BackendFailureCount";
        private const string MsrFailureCountKey = "MsrFailureCount";
        private const string MmioFailureCountKey = "MmioFailureCount";
        private const string ThermalTriggerCountKey = "ThermalTriggerCount";
        private const string RollbackCountKey = "RollbackCount";
        private const int MaxFailures = 3;
        private const int MaxBackendFailures = 5;
        private const int MaxMsrFailures = 10;
        private const int MaxMmioFailures = 5;
        
        private int _failureCount;
        private bool _tuningDisabled;
        private int _backendFailureCount;
        private int _msrFailureCount;
        private int _mmioFailureCount;
        private int _thermalTriggerCount;
        private int _rollbackCount;
        private DateTime _lastBackendFailure = DateTime.MinValue;
        private DateTime _lastMsrFailure = DateTime.MinValue;
        private DateTime _lastMmioFailure = DateTime.MinValue;
        private DateTime _lastThermalTrigger = DateTime.MinValue;
        private DateTime _lastRollback = DateTime.MinValue;
        
        public event EventHandler<RollbackEventArgs>? RollbackRequired;
        
        public StabilityWatchdog(ILoggingService logger)
        {
            _logger = logger;
        }
        
        public void Initialize()
        {
            _logger.LogInfo("[StabilityWatchdog] ============================================");
            _logger.LogInfo("[StabilityWatchdog] INICIANDO STABILITY WATCHDOG");
            _logger.LogInfo("[StabilityWatchdog] ============================================");
            _logger.LogInfo("[StabilityWatchdog] Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}");
            _logger.LogInfo("[StabilityWatchdog] Thread: {Thread.CurrentThread.ManagedThreadId}");
            
            try
            {
                // Read failure counts from registry
                using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
                _failureCount = (int)(key.GetValue(FailureCountKey, 0) ?? 0);
                _tuningDisabled = Convert.ToBoolean(key.GetValue(DisabledKey, false) ?? false);
                _backendFailureCount = (int)(key.GetValue(BackendFailureCountKey, 0) ?? 0);
                _msrFailureCount = (int)(key.GetValue(MsrFailureCountKey, 0) ?? 0);
                _mmioFailureCount = (int)(key.GetValue(MmioFailureCountKey, 0) ?? 0);
                _thermalTriggerCount = (int)(key.GetValue(ThermalTriggerCountKey, 0) ?? 0);
                _rollbackCount = (int)(key.GetValue(RollbackCountKey, 0) ?? 0);
                
                _logger.LogInfo("[StabilityWatchdog] Estado carregado do registro:");
                _logger.LogInfo($"[StabilityWatchdog]   Failure Count: {_failureCount}/{MaxFailures}");
                _logger.LogInfo($"[StabilityWatchdog]   Backend Failures: {_backendFailureCount}/{MaxBackendFailures}");
                _logger.LogInfo($"[StabilityWatchdog]   MSR Failures: {_msrFailureCount}/{MaxMsrFailures}");
                _logger.LogInfo($"[StabilityWatchdog]   MMIO Failures: {_mmioFailureCount}/{MaxMmioFailures}");
                _logger.LogInfo($"[StabilityWatchdog]   Thermal Triggers: {_thermalTriggerCount}");
                _logger.LogInfo($"[StabilityWatchdog]   Rollback Count: {_rollbackCount}");
                _logger.LogInfo($"[StabilityWatchdog]   Tuning Disabled: {_tuningDisabled}");
                
                if (_failureCount > 0)
                {
                    _logger.LogWarning($"[StabilityWatchdog] ⚠️ Previous failures detected: {_failureCount}");
                    
                    // Check if last crash was recent (within 5 minutes)
                    var lastCrashStr = key.GetValue(LastCrashKey) as string;
                    if (!string.IsNullOrEmpty(lastCrashStr) && DateTime.TryParse(lastCrashStr, out var lastCrash))
                    {
                        var timeSinceCrash = DateTime.UtcNow - lastCrash;
                        if (timeSinceCrash.TotalMinutes < 5)
                        {
                            _logger.LogWarning($"[StabilityWatchdog] ⚠️ Recent crash detected ({timeSinceCrash.TotalMinutes:F1} minutes ago)");
                            RecordCrash("System restarted after crash");
                        }
                    }
                }
                
                if (_tuningDisabled)
                {
                    _logger.LogWarning($"[StabilityWatchdog] ⚠️ CPU tuning is DISABLED due to repeated failures");
                    _logger.LogWarning($"[StabilityWatchdog] Motivo: {_failureCount} falhas detectadas (limite: {MaxFailures})");
                    _logger.LogWarning($"[StabilityWatchdog] Ação: Execute ResetFailureCounter() para reabilitar manualmente");
                }
                else
                {
                    _logger.LogSuccess($"[StabilityWatchdog] ✅ Stability Watchdog inicializado com sucesso");
                }
                _logger.LogInfo($"[StabilityWatchdog] ============================================");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[StabilityWatchdog] ❌ Initialization failed: {ex.Message}");
                _logger.LogError($"[StabilityWatchdog] StackTrace: {ex.StackTrace}");
            }
        }
        
        public void RecordSuccessfulSession()
        {
            // Successful session - gradually reduce failure counts
            bool changed = false;
            
            if (_failureCount > 0)
            {
                _failureCount = Math.Max(0, _failureCount - 1);
                changed = true;
            }
            if (_backendFailureCount > 0)
            {
                _backendFailureCount = Math.Max(0, _backendFailureCount - 1);
                changed = true;
            }
            if (_msrFailureCount > 0)
            {
                _msrFailureCount = Math.Max(0, _msrFailureCount - 1);
                changed = true;
            }
            if (_mmioFailureCount > 0)
            {
                _mmioFailureCount = Math.Max(0, _mmioFailureCount - 1);
                changed = true;
            }
            
            if (changed)
            {
                SaveFailureCount();
                _logger.LogInfo($"[StabilityWatchdog] Successful session recorded. Failure counts reduced.");
                _logger.LogInfo($"[StabilityWatchdog]   Failure: {_failureCount}, Backend: {_backendFailureCount}, MSR: {_msrFailureCount}, MMIO: {_mmioFailureCount}");
            }
        }
        
        public void RecordBackendFailure(string reason, string? backendName = null)
        {
            _backendFailureCount++;
            _lastBackendFailure = DateTime.UtcNow;
            _logger.LogWarning($"[StabilityWatchdog] Backend failure recorded: {reason}");
            if (backendName != null)
                _logger.LogWarning($"[StabilityWatchdog] Backend: {backendName}");
            _logger.LogWarning($"[StabilityWatchdog] Backend failure count: {_backendFailureCount}/{MaxBackendFailures}");
            
            SaveBackendFailureCount();
            
            if (_backendFailureCount >= MaxBackendFailures)
            {
                _logger.LogError($"[StabilityWatchdog] ❌ Backend failure threshold reached ({MaxBackendFailures})");
                _logger.LogError($"[StabilityWatchdog] Impact: Hardware backend está instável ou indisponível");
                RecordCrash("Backend failure threshold exceeded");
            }
        }
        
        public void RecordMsrFailure(string msrAddress, string reason)
        {
            _msrFailureCount++;
            _lastMsrFailure = DateTime.UtcNow;
            _logger.LogWarning($"[StabilityWatchdog] MSR failure recorded: MSR {msrAddress}");
            _logger.LogWarning($"[StabilityWatchdog] Motivo: {reason}");
            _logger.LogWarning($"[StabilityWatchdog] MSR failure count: {_msrFailureCount}/{MaxMsrFailures}");
            
            SaveMsrFailureCount();
            
            if (_msrFailureCount >= MaxMsrFailures)
            {
                _logger.LogError($"[StabilityWatchdog] ❌ MSR failure threshold reached ({MaxMsrFailures})");
                _logger.LogError($"[StabilityWatchdog] Impact: CPU MSR access está bloqueado ou instável");
                RecordCrash("MSR failure threshold exceeded");
            }
        }
        
        public void RecordMmioFailure(ulong address, string reason)
        {
            _mmioFailureCount++;
            _lastMmioFailure = DateTime.UtcNow;
            _logger.LogWarning($"[StabilityWatchdog] MMIO failure recorded: @ 0x{address:X}");
            _logger.LogWarning($"[StabilityWatchdog] Motivo: {reason}");
            _logger.LogWarning($"[StabilityWatchdog] MMIO failure count: {_mmioFailureCount}/{MaxMmioFailures}");
            
            SaveMmioFailureCount();
            
            if (_mmioFailureCount >= MaxMmioFailures)
            {
                _logger.LogWarning($"[StabilityWatchdog] ⚠️ MMIO failure threshold reached ({MaxMmioFailures})");
                _logger.LogWarning($"[StabilityWatchdog] Impact: MMIO Sync desabilitado (operando em MSR-only mode)");
                // MMIO failures don't trigger crash, just disable MMIO sync
            }
        }
        
        public void RecordThermalTrigger(string reason, double temperature)
        {
            _thermalTriggerCount++;
            _lastThermalTrigger = DateTime.UtcNow;
            _logger.LogWarning($"[StabilityWatchdog] Thermal protection trigger recorded");
            _logger.LogWarning($"[StabilityWatchdog] Motivo: {reason}");
            _logger.LogWarning($"[StabilityWatchdog] Temperatura: {temperature:F1}°C");
            _logger.LogWarning($"[StabilityWatchdog] Thermal trigger count: {_thermalTriggerCount}");
            
            SaveThermalTriggerCount();
        }
        
        public void RecordRollback(string reason)
        {
            _rollbackCount++;
            _lastRollback = DateTime.UtcNow;
            _logger.LogWarning($"[StabilityWatchdog] Rollback event recorded");
            _logger.LogWarning($"[StabilityWatchdog] Motivo: {reason}");
            _logger.LogWarning($"[StabilityWatchdog] Rollback count: {_rollbackCount}");
            
            SaveRollbackCount();
        }
        
        public bool ShouldDisableTuning()
        {
            return _tuningDisabled || _failureCount >= MaxFailures;
        }
        
        public void RecordCrash(string reason)
        {
            _failureCount++;
            _logger.LogError($"[StabilityWatchdog] ============================================");
            _logger.LogError($"[StabilityWatchdog] ❌ CRASH RECORDED");
            _logger.LogError($"[StabilityWatchdog] ============================================");
            _logger.LogError($"[StabilityWatchdog] Motivo: {reason}");
            _logger.LogError($"[StabilityWatchdog] Failure count: {_failureCount}/{MaxFailures}");
            _logger.LogError($"[StabilityWatchdog] Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}");
            _logger.LogError($"[StabilityWatchdog] Backend failures: {_backendFailureCount}");
            _logger.LogError($"[StabilityWatchdog] MSR failures: {_msrFailureCount}");
            _logger.LogError($"[StabilityWatchdog] MMIO failures: {_mmioFailureCount}");
            _logger.LogError($"[StabilityWatchdog] Thermal triggers: {_thermalTriggerCount}");
            _logger.LogError($"[StabilityWatchdog] Rollbacks: {_rollbackCount}");
            _logger.LogError($"[StabilityWatchdog] ============================================");
            
            SaveFailureCount();
            SaveLastCrashTime();
            
            // Trigger rollback event
            RollbackRequired?.Invoke(this, new RollbackEventArgs
            {
                Reason = reason,
                FailureCount = _failureCount
            });
            
            // Check if we should disable tuning
            if (_failureCount >= MaxFailures)
            {
                _tuningDisabled = true;
                SaveDisabledState();
                _logger.LogError($"[StabilityWatchdog] ❌ TUNING DISABLED após {_failureCount} falhas");
                _logger.LogError($"[StabilityWatchdog] Ação: Reabilitação manual requerida via ResetFailureCounter()");
            }
        }
        
        public void ResetFailureCounter()
        {
            _logger.LogInfo("[StabilityWatchdog] ============================================");
            _logger.LogInfo("[StabilityWatchdog] RESETANDO CONTADORES DE FALHA");
            _logger.LogInfo("[StabilityWatchdog] ============================================");
            _logger.LogInfo("[StabilityWatchdog] Ação: Reset manual solicitado pelo usuário");
            
            _failureCount = 0;
            _tuningDisabled = false;
            _backendFailureCount = 0;
            _msrFailureCount = 0;
            _mmioFailureCount = 0;
            _thermalTriggerCount = 0;
            _rollbackCount = 0;
            
            SaveFailureCount();
            SaveDisabledState();
            SaveBackendFailureCount();
            SaveMsrFailureCount();
            SaveMmioFailureCount();
            SaveThermalTriggerCount();
            SaveRollbackCount();
            
            _logger.LogSuccess("[StabilityWatchdog] ✅ Todos os contadores resetados com sucesso");
            _logger.LogInfo("[StabilityWatchdog] CPU tuning reabilitado");
            _logger.LogInfo("[StabilityWatchdog] ============================================");
        }
        
        public int GetFailureCount()
        {
            return _failureCount;
        }
        
        public (int failureCount, int backendFailures, int msrFailures, int mmioFailures, int thermalTriggers, int rollbacks) GetDetailedStatus()
        {
            return (_failureCount, _backendFailureCount, _msrFailureCount, _mmioFailureCount, _thermalTriggerCount, _rollbackCount);
        }
        
        public bool IsBackendUnstable()
        {
            return _backendFailureCount >= MaxBackendFailures;
        }
        
        public bool IsMsrAccessUnstable()
        {
            return _msrFailureCount >= MaxMsrFailures;
        }
        
        public bool IsMmioAccessUnstable()
        {
            return _mmioFailureCount >= MaxMmioFailures;
        }
        
        private void SaveFailureCount()
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
                key.SetValue(FailureCountKey, _failureCount, RegistryValueKind.DWord);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[StabilityWatchdog] Failed to save failure count: {ex.Message}");
            }
        }
        
        private void SaveLastCrashTime()
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
                key.SetValue(LastCrashKey, DateTime.UtcNow.ToString("O"), RegistryValueKind.String);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[StabilityWatchdog] Failed to save crash time: {ex.Message}");
            }
        }
        
        private void SaveBackendFailureCount()
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
                key.SetValue(BackendFailureCountKey, _backendFailureCount, RegistryValueKind.DWord);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[StabilityWatchdog] Failed to save backend failure count: {ex.Message}");
            }
        }
        
        private void SaveMsrFailureCount()
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
                key.SetValue(MsrFailureCountKey, _msrFailureCount, RegistryValueKind.DWord);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[StabilityWatchdog] Failed to save MSR failure count: {ex.Message}");
            }
        }
        
        private void SaveMmioFailureCount()
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
                key.SetValue(MmioFailureCountKey, _mmioFailureCount, RegistryValueKind.DWord);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[StabilityWatchdog] Failed to save MMIO failure count: {ex.Message}");
            }
        }
        
        private void SaveThermalTriggerCount()
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
                key.SetValue(ThermalTriggerCountKey, _thermalTriggerCount, RegistryValueKind.DWord);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[StabilityWatchdog] Failed to save thermal trigger count: {ex.Message}");
            }
        }
        
        private void SaveRollbackCount()
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
                key.SetValue(RollbackCountKey, _rollbackCount, RegistryValueKind.DWord);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[StabilityWatchdog] Failed to save rollback count: {ex.Message}");
            }
        }
        
        private void SaveDisabledState()
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
                key.SetValue(DisabledKey, _tuningDisabled ? 1 : 0, RegistryValueKind.DWord);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[StabilityWatchdog] Failed to save disabled state: {ex.Message}");
            }
        }
    }
}
