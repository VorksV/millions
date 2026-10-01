using System;

using System.Collections.Generic;

using System.Diagnostics;

using System.Linq;

using System.Threading;

using System.Threading.Tasks;

using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Gamer.Implementation 
{
public class GamerExecutionTracer : IDisposable 
{
private readonly ILoggingService _logger;

 private readonly string _sessionId;

 private readonly Dictionary < string, ExecutionTrace > _traces = new();

 private readonly object _traceLock = new object();

 private readonly Stopwatch _sessionTimer = new();

private readonly HashSet<string> _expectedModules = new();

 private readonly HashSet < string > _executedModules = new();

 private readonly HashSet < string > _failedModules = new();

private readonly GamerExecutionStatus _status = new();

    public GamerExecutionTracer(ILoggingService logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logger.LogEntry(nameof(GamerExecutionTracer));
        _sessionId = Guid.NewGuid().ToString("N")[..8];

        _sessionTimer.Start();
        _logger.LogInfo($"[GAMER - MODE][SESSION {_sessionId}] Sessão de execução iniciada");
        _logger.LogExit(nameof(GamerExecutionTracer));
    }

    public void RegisterExpectedModules(params string[] modules)
    {
        _logger.LogEntry(nameof(RegisterExpectedModules));
        foreach (var module in modules)
        {
            _expectedModules.Add(module);
            _logger.LogDebug($"[GAMER - MODE][SESSION {_sessionId}] Módulo esperado: {module}");
        }
        _logger.LogExit(nameof(RegisterExpectedModules));
    }

    public async Task<T> TraceExecutionAsync<T>(string moduleName, string operationName, Func<Task<T>> execution, bool validateResult = false, Func<T, bool>? validator = null)
    {
        _logger.LogEntry(nameof(TraceExecutionAsync));
        var traceId = $"{moduleName}_{operationName}";
        var stopwatch = Stopwatch.StartNew();

        lock (_traceLock)
        {
            _traces[traceId] = new ExecutionTrace
            {
                ModuleName = moduleName,
                OperationName = operationName,
                StartTime = DateTime.UtcNow,
                Status = ExecutionStatus.Running
            };
        }

        _logger.LogInfo($"[GAMER - MODE][SESSION {_sessionId}] {moduleName}: {operationName} START");

        try
        {
            var result = await execution();
            stopwatch.Stop();

            var success = true;
            string details = string.Empty;

            if (validateResult && validator != null)
            {
                success = validator(result);

                if (!success)
                {
                    details = "VALIDATION_FAILED - Resultado não corresponde ao esperado";
                    _logger.LogWarning($"[GAMER - MODE][SESSION {_sessionId}] {moduleName}: {operationName} VALIDATION FAILED");
                }
            }

            lock (_traceLock)
            {
                _traces[traceId].EndTime = DateTime.UtcNow;
                _traces[traceId].DurationMs = stopwatch.ElapsedMilliseconds;
                _traces[traceId].Status = success ? ExecutionStatus.Success : ExecutionStatus.ValidationFailed;
                _traces[traceId].Details = details;
                _traces[traceId].Result = result;
            }

            _executedModules.Add(moduleName);

            if (!success)
                _failedModules.Add(moduleName);

            UpdateModuleStatus(moduleName, success);

            var statusIcon = success ? "?" : "?";
            _logger.LogInfo($"[GAMER - MODE][SESSION {_sessionId}] {statusIcon} {moduleName}: {operationName} {(success ? "SUCCESS" : "FAIL")} ({stopwatch.ElapsedMilliseconds} ms)");

            if (!string.IsNullOrEmpty(details))
            {
                _logger.LogSuccess($"[GAMER - MODE][SESSION {_sessionId}] {moduleName}: {details}");
            }
            _logger.LogExit(nameof(TraceExecutionAsync));
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            lock (_traceLock)
            {
                _traces[traceId].EndTime = DateTime.UtcNow;
                _traces[traceId].DurationMs = stopwatch.ElapsedMilliseconds;
                _traces[traceId].Status = ExecutionStatus.Failed;
                _traces[traceId].Exception = ex;
                _traces[traceId].Details = $"EXCEPTION: {ex.Message}";
            }

            _executedModules.Add(moduleName);
            _failedModules.Add(moduleName);
            UpdateModuleStatus(moduleName, false);
            _logger.LogError($"[GAMER - MODE][SESSION {_sessionId}] {moduleName}: {operationName} FAILED ({stopwatch.ElapsedMilliseconds} ms)", ex);
            _logger.LogExit(nameof(TraceExecutionAsync));
            throw;
        }
    }

    public ValidationResult ValidateExecution()
    {
        _logger.LogEntry(nameof(ValidateExecution));
        var missing = _expectedModules.Except(_executedModules).ToList();
        var failed = _failedModules.ToList();
        var success = _executedModules.Except(_failedModules).ToList();

        var validation = new ValidationResult
        {
            IsComplete = !missing.Any(),
            TotalExpected = _expectedModules.Count,
            TotalExecuted = _executedModules.Count,
            TotalSuccess = success.Count,
            TotalFailed = failed.Count,
            MissingModules = missing,
            FailedModules = failed,
            SuccessModules = success,
            SessionDuration = _sessionTimer.Elapsed
        };

        if (validation.IsComplete)
        {
            _logger.LogSuccess($"[GAMER - MODE][SESSION {_sessionId}] VALIDAÇÃO COMPLETA: {validation.TotalSuccess}/{validation.TotalExpected} módulos com sucesso");
        }
        else
        {
            _logger.LogError($"[GAMER - MODE][SESSION {_sessionId}] VALIDAÇÃO FALHOU: {validation.TotalSuccess}/{validation.TotalExpected} módulos com sucesso");

            if (missing.Any())
            {
                _logger.LogError($"[GAMER - MODE][SESSION {_sessionId}] MÓDULOS NÃO EXECUTADOS: {string.Join(",", missing)}");
            }

            if (failed.Any())
            {
                _logger.LogError($"[GAMER - MODE][SESSION {_sessionId}] MÓDULOS FALHARAM: {string.Join(",", failed)}");
            }
        }
        _logger.LogExit(nameof(ValidateExecution));
        return validation;
    }

    public GamerExecutionStatus GetCurrentStatus()
    {
        _logger.LogEntry(nameof(GetCurrentStatus));
        _logger.LogExit(nameof(GetCurrentStatus));
        return _status;
    }

    public bool IsModuleSuccessful(string moduleName)
    {
        _logger.LogEntry(nameof(IsModuleSuccessful));
        var result = _executedModules.Contains(moduleName) && !_failedModules.Contains(moduleName);
        _logger.LogExit(nameof(IsModuleSuccessful));
        return result;
    }

    public ExecutionTrace? GetModuleTrace(string moduleName, string operationName)
    {
        _logger.LogEntry(nameof(GetModuleTrace));
        var traceId = $"{moduleName}_{operationName}";

        lock (_traceLock)
        {
            _logger.LogExit(nameof(GetModuleTrace));
            return _traces.TryGetValue(traceId, out var trace) ? trace : null;
        }
    }

    private void UpdateModuleStatus(string moduleName, bool success)
    {
        _logger.LogEntry(nameof(UpdateModuleStatus));
        switch (moduleName.ToLowerInvariant())
        {
            case "cpu":
                _status.CpuApplied = success;
                break;

            case "gpu":
                _status.GpuApplied = success;
                break;

            case "power":
                _status.PowerApplied = success;
                break;

            case "priority":
                _status.PriorityApplied = success;
                break;

            case "memory":
                _status.MemoryApplied = success;
                break;

            case "network":
                _status.NetworkApplied = success;
                break;

            case "process":
                _status.ProcessApplied = success;
                break;

            case "visual":
                _status.VisualApplied = success;
                break;
        }
        _logger.LogExit(nameof(UpdateModuleStatus));
    }

    public ExecutionReport GenerateReport()
    {
        _logger.LogEntry(nameof(GenerateReport));
        _logger.LogExit(nameof(GenerateReport));
        return new ExecutionReport
        {
            SessionId = _sessionId,
            StartTime = DateTime.UtcNow - _sessionTimer.Elapsed,
            EndTime = DateTime.UtcNow,
            Duration = _sessionTimer.Elapsed,
            TotalOperations = _traces.Count,
            Traces = _traces.Values.ToList(),
            Validation = ValidateExecution(),
            Status = _status
        };
    }

    public void Dispose()
    {
        _logger.LogEntry(nameof(Dispose));
        try
        {
            _sessionTimer.Stop();

            if (_traces.Any())
            {
                var report = GenerateReport();
                _logger.LogInfo($"[GAMER - MODE][SESSION {_sessionId}] Relatório final: {report.Validation.TotalSuccess}/{report.Validation.TotalExpected} módulos com sucessão em {report.Duration.TotalMilliseconds:F0} ms");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"[GAMER - MODE][SESSION {_sessionId}] Erro ao finalizar tracer: {ex.Message}");
        }
        _logger.LogExit(nameof(Dispose));
    }
}

public class ExecutionTrace
{
    public string ModuleName
    {
        get;
        set;
    } = string.Empty;

    public string OperationName
    {
        get;
        set;
    } = string.Empty;

    public DateTime StartTime
    {
        get;
        set;
    }

    public DateTime EndTime
    {
        get;
        set;
    }

    public TimeSpan Duration => EndTime - StartTime;

    public long DurationMs
    {
        get;
        set;
    }

    public ExecutionStatus Status
    {
        get;
        set;
    }

    public string Details
    {
        get;
        set;
    } = string.Empty;

    public string? ErrorDetails
    {
        get;
        set;
    }
    public Exception? Exception
    {
        get;
        set;
    }
    public object? Result
    {
        get;
        set;
    }
}

public class ValidationResult
{
    public bool IsComplete
    {
        get;
        set;
    }
    public int TotalExpected
    {
        get;
        set;
    }
    public int TotalExecuted
    {
        get;
        set;
    }
    public int TotalSuccess
    {
        get;
        set;
    }
    public int TotalFailed
    {
        get;
        set;
    }
    public List<string> MissingModules
    {
        get;
        set;
    } = new();

    public List<string> FailedModules
    {
        get;
        set;
    } = new();

    public List<string> SuccessModules
    {
        get;
        set;
    } = new();

    public TimeSpan SessionDuration
    {
        get;
        set;
    }
}

public class ExecutionReport
{
    public string SessionId
    {
        get;
        set;
    } = string.Empty;

    public DateTime StartTime
    {
        get;
        set;
    }
    public DateTime EndTime
    {
        get;
        set;
    }
    public TimeSpan Duration
    {
        get;
        set;
    }
    public int TotalOperations
    {
        get;
        set;
    }
    public List<ExecutionTrace> Traces
    {
        get;
        set;
    } = new();

    public ValidationResult Validation
    {
        get;
        set;
    } = new();

    public GamerExecutionStatus Status
    {
        get;
        set;
    } = new();
}

public class GamerExecutionStatus
{
    public bool CpuApplied
    {
        get;
        set;
    }

    public bool GpuApplied
    {
        get;
        set;
    }

    public bool PowerApplied
    {
        get;
        set;
    }

    public bool PriorityApplied
    {
        get;
        set;
    }

    public bool MemoryApplied
    {
        get;
        set;
    }

    public bool NetworkApplied
    {
        get;
        set;
    }

    public bool ProcessApplied
    {
        get;
        set;
    }

    public bool VisualApplied
    {
        get;
        set;
    }

    public bool IsFullyApplied => CpuApplied && GpuApplied && PowerApplied && PriorityApplied;

    public double SuccessPercentage => new[]
    {
        CpuApplied, GpuApplied, PowerApplied, PriorityApplied, MemoryApplied, NetworkApplied, ProcessApplied, VisualApplied
    }.Count(b => b) * 100.0 / 8.0;
}

public enum ExecutionStatus
{
    Running, Success, Failed, ValidationFailed
}

public static class GamerTracerExtensions
{
    public static async Task<T> ExecuteWithTraceAsync<T>(this GamerExecutionTracer tracer, string moduleName, string operationName, Func<Task<T>> execution, Func<T, bool>? validator = null)
    {
        return await tracer.TraceExecutionAsync(moduleName, operationName, execution, validateResult: validator != null, validator: validator);
    }

    public static async Task ExecuteWithTraceAsync(this GamerExecutionTracer tracer, string moduleName, string operationName, Func<Task> execution)
    {
        await tracer.TraceExecutionAsync(moduleName, operationName, async () =>
        {
            await execution();
            return true;
        });
    }
}
}
