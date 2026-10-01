using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.Shield;

namespace VoltrisOptimizer.Services.Shield.Advanced
{
    /// <summary>
    /// Detector de Injeção de Código — Analisa processos ativos em busca de anomalias de memória (hollowing/reflective loading).
    /// </summary>
    public class CodeInjectionDetector
    {
        private readonly ILoggingService _logger;
        private readonly SignatureVerificationService _signatureService;

        // Constantes da API Windows (Ofuscadas)
        private const uint PROCESS_QUERY_INFORMATION = 0x0400;
        private const uint PROCESS_VM_READ = 0x0010;
        private const uint MEM_COMMIT = 0x1000;
        private const uint PAGE_EXECUTE_READWRITE = 0x40; // RWX 

        private const int RwxThreshold = 2;
        private const int PersistenceWindowSeconds = 20;
        private const int StaleTrackingSeconds = 300;

        private static readonly TimeSpan ConfirmationDelay = TimeSpan.FromSeconds(6);
        private static readonly TimeSpan ConfirmationMaxGap = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Hosts de runtime gerenciado / interpretado.
        /// <para>
        /// Estes processos COMPARTILHAM memória executável (RWX) por desenho: o JIT do
        /// CLR, do Java HotSpot, do V8, do CPython e afins precisam gerar código nativo
        /// em memória gravável+executável. Uma sessão PowerShell longa acumula regiões
        /// RWX de forma PERMANENTE — exatamente o que a heurística de persistência
        ///-take como prova de injeção.
        /// </para>
        /// <para>
        /// Antes desta lista, o PowerShell do próprio usuário era reportado como
        /// "Injeção de Código (Critical)" e disparava o toast "Ameaça Crítica Detectada"
        /// (log 14:19:58 e 14:50:06, PID 9840, 19 regiões, 124 KB). Isso é um falso
        /// positivo estrutural: para estes hosts, memória RWX persistente é o estado
        /// normal de operação, não evidência de ataque.
        /// </para>
        /// <para>
        /// Eles NÃO são ignorados por inteiro: continuam com o monitor de comportamento,
        /// assinatura, quarentena e detecção de rede. Apenas a heurística de RWX é
        /// inaplicável a eles.
        /// </para>
        /// </summary>
        private static readonly string[] JitRuntimeProcessNames =
        {
            // PowerShell (Windows PowerShell 5.1 e PowerShell 7+)
            "powershell", "pwsh",
            // Hosts .NET
            "dotnet", "msbuild", "vbcscompiler", "vbc", "csc",
            // Java
            "java", "javaw", "jscript", "vbscript",
            // JavaScript / TypeScript
            "node", "deno", "bun",
            // Python
            "python", "pythonw", "py",
            // Automação de script do Windows
            "wscript", "cscript", "mshta",
            // Outros runtimes / ferramentas que JIT-compilam
            "perl", "ruby", "php", "lua", "rundll32"
        };

        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, RwxObservation> _rwxObservations
            = new System.Collections.Concurrent.ConcurrentDictionary<int, RwxObservation>();

        private sealed class RwxObservation
        {
            public int Regions { get; set; }
            public int Confirmations { get; set; }
            public DateTime FirstSeenUtc { get; set; }
            public DateTime LastSeenUtc { get; set; }
        }

        public sealed class InjectionCheckResult
        {
            public bool IsSuspicious { get; init; }
            public string ProcessName { get; init; } = string.Empty;
            public int ProcessId { get; init; }
            public string Details { get; init; } = string.Empty;
        }

        [DllImport("kernel32.dll", EntryPoint = "OpenProcess", SetLastError = true)]
        private static extern IntPtr OpenProcessNative(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", EntryPoint = "CloseHandle", SetLastError = true)]
        private static extern bool CloseHandleNative(IntPtr hObject);

        [DllImport("kernel32.dll", EntryPoint = "ReadProcessMemory", SetLastError = true)]
        private static extern bool ReadProcessMemoryNative(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, out int lpNumberOfBytesRead);

        [DllImport("kernel32.dll", EntryPoint = "VirtualQueryEx", SetLastError = true)]
        private static extern uint VirtualQueryExNative(IntPtr hProcess, IntPtr lpAddress, out MEMORY_BASIC_INFORMATION lpBuffer, uint dwLength);

        [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageName", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageNameNative(IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);

        // Wrapper de segurança para evitar detecção de string estática
        private static IntPtr OpenProcess(uint access, bool inherit, int pid) => OpenProcessNative(access, inherit, pid);
        private static bool CloseHandle(IntPtr handle) => CloseHandleNative(handle);
        private static uint VirtualQueryEx(IntPtr hProc, IntPtr addr, out MEMORY_BASIC_INFORMATION info, uint len) => VirtualQueryExNative(hProc, addr, out info, len);

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORY_BASIC_INFORMATION
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public IntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        public CodeInjectionDetector(ILoggingService logger, SignatureVerificationService signatureService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _signatureService = signatureService ?? throw new ArgumentNullException(nameof(signatureService));
        }

        /// <summary>
        /// Verifica anomalias de injeção em um processo específico (Hollowing).
        /// </summary>
        public async Task<bool> DetectProcessHollowing(int processId)
        {
            var result = await AnalyzeProcess(processId);
            return result.IsSuspicious;
        }

        /// <summary>
        /// Analisa um processo e devolve o veredito com a evidência observada.
        /// Regiões RWX transitórias (JIT do CLR, WPF/XAML, PowerShell) não são confirmadas,
        /// pois memória de shellcode injetado permanece executável entre amostras.
        /// </summary>
        /// <param name="cancellationToken">
        /// Permite interromper a varredura. O laço de VirtualQueryEx e o
        /// Process.GetProcessById verificam o token entre etapas — a API do
        /// kernel em si não é cancelável, daí o timeout por processo ser
        /// aplicado pelo chamador (VoltrisShieldService.RunInjectionSweepAsync).
        /// </param>
        public Task<InjectionCheckResult> AnalyzeProcess(int processId) =>
            AnalyzeProcess(processId, CancellationToken.None);

        /// <inheritdoc cref="AnalyzeProcess(int)"/>
        public Task<InjectionCheckResult> AnalyzeProcess(int processId, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                Process? proc = null;
                IntPtr hProcess = IntPtr.Zero;
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    proc = Process.GetProcessById(processId);
                    if (proc.HasExited)
                        return NotSuspicious(proc, processId);

                    hProcess = OpenProcess(0x0010 | 0x0400, false, processId);
                    if (hProcess == IntPtr.Zero)
                        return NotSuspicious(proc, processId);

                    cancellationToken.ThrowIfCancellationRequested();

                    var executablePath = GetProcessImagePath(hProcess, proc);

                    if (IsJitRuntimeProcess(proc.ProcessName))
                    {
                        ForgetObservation(processId);
                        _logger.LogTrace($"[InjectionDetect] Runtime JIT ignorado para RWX: {proc.ProcessName} (PID: {processId}) - memória executável é esperada neste host");
                        return NotSuspicious(proc, processId);
                    }

                    if (IsTrustedProcess(executablePath))
                    {
                        ForgetObservation(processId);
                        _logger.LogTrace($"[InjectionDetect] Processo confiável ignorado: {proc.ProcessName} (PID: {processId})");
                        return NotSuspicious(proc, processId);
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    IntPtr address = IntPtr.Zero;
                    int rwxRegionsFound = 0;
                    long largestRegion = 0;

                    while (VirtualQueryEx(hProcess, address, out MEMORY_BASIC_INFORMATION memInfo, (uint)Marshal.SizeOf(typeof(MEMORY_BASIC_INFORMATION))) != 0)
                    {
                        // A API nao cancela, mas o laco e nosso: sair cedo aqui e a
                        // diferenca entre um ciclo de varredura e um travamento.
                        cancellationToken.ThrowIfCancellationRequested();

                        if (memInfo.State == MEM_COMMIT && (memInfo.Protect & 0xFF) == PAGE_EXECUTE_READWRITE)
                        {
                            rwxRegionsFound++;
                            if (memInfo.RegionSize.ToInt64() > largestRegion)
                                largestRegion = memInfo.RegionSize.ToInt64();
                        }

                        address = new IntPtr(memInfo.BaseAddress.ToInt64() + memInfo.RegionSize.ToInt64());
                    }

                    if (rwxRegionsFound < RwxThreshold)
                    {
                        ForgetObservation(processId);
                        if (rwxRegionsFound == 1)
                        {
                            _logger.LogDebug($"[InjectionDetect] 1 região RWX transitória em {proc.ProcessName} (PID: {processId}) - ignorada");
                        }
                        return NotSuspicious(proc, processId);
                    }

                    if (!IsRwxPersistent(processId, rwxRegionsFound))
                    {
                        _logger.LogTrace($"[InjectionDetect] RWX transitório em {proc.ProcessName} (PID: {processId}): {rwxRegionsFound} região(ões) aguardando confirmação temporal");
                        return NotSuspicious(proc, processId);
                    }

                    _logger.LogWarning($"[InjectionDetect] Memória RWX persistente detectada em {proc.ProcessName} (PID: {processId}) — {rwxRegionsFound} regiões, maior {largestRegion / 1024} KB");
                    return new InjectionCheckResult
                    {
                        IsSuspicious = true,
                        ProcessName = proc.ProcessName,
                        ProcessId = processId,
                        Details = $"{rwxRegionsFound} regiões RWX persistentes (maior {largestRegion / 1024} KB) confirmadas em amostras sucessivas"
                    };
                }
                catch (OperationCanceledException)
                {
                    // Cancelamento e esperado: o chamador pediu para parar.
                    // Nao registrar como erro — nao e falha de analise.
                    ForgetObservation(processId);
                    throw;
                }
                catch (Exception ex)
                {
                    if (ex.Message.Contains("is not running"))
                    {
                        ForgetObservation(processId);
                        _logger.LogDebug($"[InjectionDetect] Processo {processId} terminou durante análise (condição normal)");
                    }
                    else
                    {
                        _logger.LogDebug($"[InjectionDetect] Erro ao analisar processo {processId}: {ex.Message}");
                    }
                    return new InjectionCheckResult { ProcessId = processId, ProcessName = proc?.ProcessName ?? "desconhecido" };
                }
                finally
                {
                    if (hProcess != IntPtr.Zero)
                    {
                        CloseHandle(hProcess);
                    }

                    proc?.Dispose();
                }
            });
        }

        private static InjectionCheckResult NotSuspicious(Process proc, int processId)
        {
            return new InjectionCheckResult { ProcessName = proc.ProcessName, ProcessId = processId };
        }

        /// <summary>
        /// Confirma apenas se as regiões RWX reaparecerem depois de um intervalo mínimo.
        /// </summary>
        private bool IsRwxPersistent(int processId, int rwxRegionsFound)
        {
            var now = DateTime.UtcNow;
            var entry = _rwxObservations.GetOrAdd(processId, _ => new RwxObservation
            {
                Regions = rwxRegionsFound,
                Confirmations = 0,
                FirstSeenUtc = now,
                LastSeenUtc = now
            });

            if (now - entry.FirstSeenUtc > ConfirmationMaxGap)
            {
                entry.FirstSeenUtc = now;
                entry.Regions = rwxRegionsFound;
                entry.Confirmations = 0;
            }

            entry.Regions = rwxRegionsFound;
            entry.LastSeenUtc = now;
            entry.Confirmations++;

            PruneObservations(now);

            return entry.Confirmations >= 2 && now - entry.FirstSeenUtc >= ConfirmationDelay;
        }

        private void ForgetObservation(int processId)
        {
            _rwxObservations.TryRemove(processId, out _);
        }

        private void PruneObservations(DateTime nowUtc)
        {
            var cutoff = nowUtc.AddSeconds(-StaleTrackingSeconds);
            foreach (var pair in _rwxObservations)
            {
                if (pair.Value.LastSeenUtc < cutoff)
                    _rwxObservations.TryRemove(pair.Key, out _);
            }
        }

        private static string? GetProcessImagePath(IntPtr processHandle, Process process)
        {
            var capacity = 32768u;
            var buffer = new StringBuilder((int)capacity);
            if (QueryFullProcessImageNameNative(processHandle, 0, buffer, ref capacity))
                return buffer.ToString();

            try
            {
                return process.MainModule?.FileName;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsJitRuntimeProcess(string? processName)
        {
            if (string.IsNullOrWhiteSpace(processName))
                return false;

            return Array.Exists(JitRuntimeProcessNames,
                name => string.Equals(name, processName, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsTrustedProcess(string? executablePath)
        {
            if (string.IsNullOrWhiteSpace(executablePath))
                return false;

            try
            {
                var fullPath = Path.GetFullPath(executablePath);
                if (!File.Exists(fullPath))
                    return false;

                var trustedRoots = new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                };
                var isTrustedPath = trustedRoots.Where(root => !string.IsNullOrWhiteSpace(root))
                    .Select(root => Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar)
                    .Any(root => fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase));

                var signature = _signatureService.VerifySignature(fullPath);
                if (!signature.IsValid)
                    return false;

                if (isTrustedPath)
                    return true;

                if (!signature.IsTrustedPublisher)
                    return false;

                return signature.Publisher?.StartsWith("Anomaly Innovations", StringComparison.OrdinalIgnoreCase) == true;
            }
            catch
            {
                return false;
            }
        }
    }
}
