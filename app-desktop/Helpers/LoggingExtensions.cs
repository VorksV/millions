using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Helpers
{
    public static class LoggingExtensions
    {
        public static ILoggingService LogMethodStart(
            this ILoggingService logger,
            [CallerMemberName] string methodName = "",
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            var fileName = System.IO.Path.GetFileName(filePath);
            logger.LogDebug($"▶ ENTER {methodName} | {fileName}:{lineNumber}");
            return logger;
        }

        public static ILoggingService LogMethodStart<T1>(
            this ILoggingService logger,
            T1 arg1,
            [CallerMemberName] string methodName = "",
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            var fileName = System.IO.Path.GetFileName(filePath);
            logger.LogDebug($"▶ ENTER {methodName}({arg1}) | {fileName}:{lineNumber}");
            return logger;
        }

        public static ILoggingService LogMethodStart<T1, T2>(
            this ILoggingService logger,
            T1 arg1, T2 arg2,
            [CallerMemberName] string methodName = "",
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            var fileName = System.IO.Path.GetFileName(filePath);
            logger.LogDebug($"▶ ENTER {methodName}({arg1}, {arg2}) | {fileName}:{lineNumber}");
            return logger;
        }

        public static void LogMethodEnd(
            this ILoggingService logger,
            [CallerMemberName] string methodName = "",
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            var fileName = System.IO.Path.GetFileName(filePath);
            logger.LogDebug($"◀ EXIT {methodName} | {fileName}:{lineNumber}");
        }

        public static void LogMethodEnd<T>(
            this ILoggingService logger,
            T result,
            [CallerMemberName] string methodName = "",
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            var fileName = System.IO.Path.GetFileName(filePath);
            logger.LogDebug($"◀ EXIT {methodName} = {result} | {fileName}:{lineNumber}");
        }

        public static void LogMethodEnd(
            this ILoggingService logger,
            bool result,
            [CallerMemberName] string methodName = "",
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            var fileName = System.IO.Path.GetFileName(filePath);
            logger.LogDebug($"◀ EXIT {methodName} = {(result ? "SUCCESS" : "FAILED")} | {fileName}:{lineNumber}");
        }
    }
}