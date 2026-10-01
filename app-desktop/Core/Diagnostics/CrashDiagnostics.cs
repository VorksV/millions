using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace VoltrisOptimizer.Core.Diagnostics
{
    /// <summary>
    /// Sistema de diagnóstico de crash de nível profissional.
    /// Usa FileStream com flush síncrono para garantir que CADA linha
    /// seja gravada em disco ANTES de retornar — sobrevive a crashes nativos.
    /// 
    /// Técnica: "breadcrumb trail" — cada operação perigosa deixa uma marca
    /// no arquivo ANTES de executar. Se o processo morrer, a última marca
    /// indica exatamente onde o crash aconteceu.
    /// </summary>
    public static class CrashDiagnostics
    {
        private static FileStream? _fs;
        private static readonly object _lock = new();
        private static string? _logPath;
        private static long _sequence;
        private static int _pid;

        // Windows API para flush forçado no kernel (bypass de cache do SO)
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FlushFileBuffers(IntPtr hFile);

        /// <summary>
        /// Inicializa o sistema de diagnóstico. Deve ser chamado o mais cedo possível no startup.
        /// </summary>
        public static void Initialize()
        {
            try
            {
                _pid = Environment.ProcessId;
                var logDir = LogDirectoryResolver.Resolve();
                Directory.CreateDirectory(logDir);
                
                // Rotação: limpar crash traces antigos (manter últimos 10)
                CleanupOldTraceFiles(logDir);
                
                _logPath = Path.Combine(logDir, $"crash_trace_{DateTime.Now:yyyyMMdd_HHmmss}_{_pid}.log");

                // FileOptions.WriteThrough = bypass do cache do filesystem
                _fs = new FileStream(_logPath, FileMode.Create, FileAccess.Write, FileShare.Read,
                    bufferSize: 256, FileOptions.WriteThrough | FileOptions.SequentialScan);

                WriteLineRaw($"=== VOLTRIS CRASH DIAGNOSTICS ===");
                WriteLineRaw($"PID: {_pid}");
                WriteLineRaw($"Start: {DateTime.Now:O}");
                WriteLineRaw($"OS: {Environment.OSVersion}");
                WriteLineRaw($"CLR: {Environment.Version}");
                WriteLineRaw($"Processors: {Environment.ProcessorCount}");
                WriteLineRaw($"WorkingSet: {Environment.WorkingSet / 1024 / 1024}MB");
                WriteLineRaw($"Is64Bit: {Environment.Is64BitProcess}");
                WriteLineRaw($"===================================");
            }
            catch { }
        }

        /// <summary>
        /// Marca o início de uma operação perigosa. Se o processo morrer antes de Mark("...done"),
        /// saberemos exatamente qual operação causou o crash.
        /// </summary>
        public static void Mark(string breadcrumb)
        {
            var seq = Interlocked.Increment(ref _sequence);
            var tid = Environment.CurrentManagedThreadId;
            WriteLineRaw($"[{DateTime.Now:HH:mm:ss.fff}] [T{tid:D4}] [{seq:D6}] {breadcrumb}");
        }

        /// <summary>
        /// Registra uma chamada P/Invoke perigosa ANTES de executá-la.
        /// </summary>
        public static void TraceNativeCall(string api, string args)
        {
            Mark($">>> NATIVE: {api}({args})");
        }

        /// <summary>
        /// Registra o resultado de uma chamada P/Invoke.
        /// </summary>
        public static void TraceNativeResult(string api, bool success)
        {
            Mark($"<<< NATIVE: {api} -> {(success ? "OK" : $"FAIL err={Marshal.GetLastWin32Error()}")}");
        }

        /// <summary>
        /// Registra informações de memória do processo atual.
        /// </summary>
        public static void TraceMemory(string context)
        {
            try
            {
                using var proc = Process.GetCurrentProcess();
                Mark($"[MEM:{context}] WorkingSet={proc.WorkingSet64 / 1024 / 1024}MB, " +
                     $"Private={proc.PrivateMemorySize64 / 1024 / 1024}MB, " +
                     $"Virtual={proc.VirtualMemorySize64 / 1024 / 1024}MB, " +
                     $"Handles={proc.HandleCount}, " +
                     $"Threads={proc.Threads.Count}");
            }
            catch (Exception ex)
            {
                Mark($"[MEM:{context}] FAILED: {ex.Message}");
            }
        }

        /// <summary>
        /// Registra exceção capturada.
        /// </summary>
        public static void TraceException(string context, Exception ex)
        {
            Mark($"[EXCEPTION:{context}] {ex.GetType().Name}: {ex.Message}");
            if (ex.StackTrace != null)
            {
                // Gravar apenas as primeiras 5 linhas do stack trace
                var lines = ex.StackTrace.Split('\n');
                for (int i = 0; i < Math.Min(5, lines.Length); i++)
                {
                    WriteLineRaw($"    {lines[i].Trim()}");
                }
            }
            if (ex.InnerException != null)
            {
                Mark($"[INNER] {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            }
        }

        /// <summary>
        /// Grava uma linha diretamente no disco com flush forçado.
        /// Usa WriteThrough + FlushFileBuffers para garantir persistência.
        /// </summary>
        private static void WriteLineRaw(string line)
        {
            if (_fs == null) return;
            try
            {
                lock (_lock)
                {
                    var bytes = Encoding.UTF8.GetBytes(line + "\n");
                    _fs.Write(bytes, 0, bytes.Length);
                    _fs.Flush(flushToDisk: true);
                    // Flush extra via kernel para garantir em caso de crash do CLR
                    FlushFileBuffers(_fs.SafeFileHandle.DangerousGetHandle());
                }
            }
            catch { }
        }

        /// <summary>
        /// Remove crash trace files antigos, mantendo apenas os 10 mais recentes.
        /// Também remove arquivos crash_native_* antigos.
        /// </summary>
        private static void CleanupOldTraceFiles(string logDir)
        {
            try
            {
                const int maxFiles = 10;
                
                // Limpar crash_trace_*.log
                var traceFiles = Directory.GetFiles(logDir, "crash_trace_*.log")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Skip(maxFiles)
                    .ToArray();
                
                foreach (var file in traceFiles)
                {
                    try { file.Delete(); } catch { }
                }
                
                // Limpar crash_native_*.log
                var nativeFiles = Directory.GetFiles(logDir, "crash_native_*.log")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Skip(maxFiles)
                    .ToArray();
                
                foreach (var file in nativeFiles)
                {
                    try { file.Delete(); } catch { }
                }
            }
            catch { }
        }

        /// <summary>
        /// Marca uma saída graciosa (Environment.Exit) para distinguir de crash verdadeiro.
        /// </summary>
        public static void MarkGracefulExit(string reason = "Exit normal")
        {
            Mark($"=== GRACEFUL EXIT: {reason} ===");
            try
            {
                lock (_lock)
                {
                    _fs?.Flush(flushToDisk: true);
                    _fs?.Dispose();
                    _fs = null;
                }
            }
            catch { }
        }

        /// <summary>
        /// Fecha o sistema de diagnóstico.
        /// </summary>
        public static void Shutdown()
        {
            try
            {
                Mark("=== SHUTDOWN NORMAL ===");
                lock (_lock)
                {
                    _fs?.Flush(flushToDisk: true);
                    _fs?.Dispose();
                    _fs = null;
                }
            }
            catch { }
        }
    }
}
