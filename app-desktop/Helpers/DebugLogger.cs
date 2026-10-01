using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Helpers
{
    public static class DebugLogger
    {
        [Conditional("DEBUG")]
        public static void LogEntry(this ILoggingService? logger, string method,
            [CallerFilePath] string? file = null, [CallerLineNumber] int line = 0)
        {
            logger?.LogDebug($"▶ ENTRY {method} | {Path.GetFileName(file)}:{line}");
        }

        [Conditional("DEBUG")]
        public static void LogEntry(this ILoggingService? logger, string method,
            (string name, object? value) arg1,
            [CallerFilePath] string? file = null, [CallerLineNumber] int line = 0)
        {
            logger?.LogDebug($"▶ ENTRY {method}({arg1.name}={arg1.value}) | {Path.GetFileName(file)}:{line}");
        }

        [Conditional("DEBUG")]
        public static void LogEntry(this ILoggingService? logger, string method,
            (string name, object? value) arg1, (string name, object? value) arg2,
            [CallerFilePath] string? file = null, [CallerLineNumber] int line = 0)
        {
            logger?.LogDebug($"▶ ENTRY {method}({arg1.name}={arg1.value}, {arg2.name}={arg2.value}) | {Path.GetFileName(file)}:{line}");
        }

        [Conditional("DEBUG")]
        public static void LogEntry(this ILoggingService? logger, string method,
            (string name, object? value) arg1, (string name, object? value) arg2, (string name, object? value) arg3,
            [CallerFilePath] string? file = null, [CallerLineNumber] int line = 0)
        {
            logger?.LogDebug($"▶ ENTRY {method}({arg1.name}={arg1.value}, {arg2.name}={arg2.value}, {arg3.name}={arg3.value}) | {Path.GetFileName(file)}:{line}");
        }

        [Conditional("DEBUG")]
        public static void LogExit(this ILoggingService? logger, string method,
            object? result = null, long elapsedMs = 0,
            [CallerFilePath] string? file = null, [CallerLineNumber] int line = 0)
        {
            if (result != null)
                logger?.LogDebug($"◀ EXIT {method} = {result}{(elapsedMs > 0 ? $" | {elapsedMs}ms" : "")} | {Path.GetFileName(file)}:{line}");
            else
                logger?.LogDebug($"◀ EXIT {method}{(elapsedMs > 0 ? $" | {elapsedMs}ms" : "")} | {Path.GetFileName(file)}:{line}");
        }

        [Conditional("DEBUG")]
        public static void LogState(this ILoggingService? logger, string label, object? state,
            [CallerMemberName] string? method = null, [CallerLineNumber] int line = 0)
        {
            logger?.LogDebug($"[{method}:{line}] STATE {label} = {state}");
        }

        [Conditional("DEBUG")]
        public static void LogEvent(this ILoggingService? logger, string category, string message,
            [CallerMemberName] string? method = null, [CallerLineNumber] int line = 0)
        {
            logger?.LogDebug($"[{method}:{line}] EVENT [{category}] {message}");
        }

        [Conditional("DEBUG")]
        public static void LogDecision(this ILoggingService? logger, string decision, string reason, object? context = null,
            [CallerMemberName] string? method = null, [CallerLineNumber] int line = 0)
        {
            logger?.LogDebug($"[{method}:{line}] DECISION {decision} | reason={reason}{(context != null ? $" | ctx={context}" : "")}");
        }

        [Conditional("DEBUG")]
        public static void LogTransition(this ILoggingService? logger, string from, string to, string? trigger = null,
            [CallerMemberName] string? method = null, [CallerLineNumber] int line = 0)
        {
            logger?.LogDebug($"[{method}:{line}] TRANSITION {from} → {to}{(trigger != null ? $" | trigger={trigger}" : "")}");
        }

        [Conditional("DEBUG")]
        public static void LogValue(this ILoggingService? logger, string name, object? value,
            [CallerMemberName] string? method = null, [CallerLineNumber] int line = 0)
        {
            logger?.LogDebug($"[{method}:{line}] VALUE {name} = {value}");
        }

        [Conditional("DEBUG")]
        public static void LogLoop(this ILoggingService? logger, string loopName, int iteration, string? detail = null,
            [CallerMemberName] string? method = null, [CallerLineNumber] int line = 0)
        {
            logger?.LogDebug($"[{method}:{line}] LOOP {loopName}[{iteration}]{(detail != null ? $" {detail}" : "")}");
        }

        [Conditional("DEBUG")]
        public static void LogTimer(this ILoggingService? logger, string timerName, long intervalMs,
            [CallerMemberName] string? method = null, [CallerLineNumber] int line = 0)
        {
            logger?.LogDebug($"[{method}:{line}] TIMER {timerName} fired ({intervalMs}ms)");
        }

        [Conditional("DEBUG")]
        public static void LogCache(this ILoggingService? logger, string cacheName, string action, string? detail = null,
            [CallerMemberName] string? method = null, [CallerLineNumber] int line = 0)
        {
            logger?.LogDebug($"[{method}:{line}] CACHE {cacheName} {action}{(detail != null ? $" | {detail}" : "")}");
        }
    }
}
