using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Optimization.Features
{
    /// <summary>
    /// Pilar 4 da Ultra-Performance: Zero-Jitter Mode
    /// Ao invés de apenas diminuir a prioridade de um processo (que ainda permite que ele acorde e consuma CPU cache e I/O),
    /// nós usamos NtSuspendProcess para congelar processos não-vitais matematicamente a 0% de uso durante o jogo.
    /// </summary>
    public class ZeroJitterProcessSuspender
    {
        private readonly ILoggingService _logger;
        private readonly ConcurrentBag<int> _suspendedPids = new();

        [DllImport("ntdll.dll")]
        private static extern int NtSuspendProcess(IntPtr processHandle);

        [DllImport("ntdll.dll")]
        private static extern int NtResumeProcess(IntPtr processHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint PROCESS_SUSPEND_RESUME = 0x0800;

        // Lista de processos seguros para suspensão profunda durante jogos
        private static readonly HashSet<string> SafeToSuspend = new(StringComparer.OrdinalIgnoreCase)
        {
            "OneDrive", "Dropbox", "GoogleDriveFS", "msedge", "chrome", "firefox", "brave",
            "AdobeUpdateService", "CCUpdate", "OriginWebHelperService", "EpicWebHelper",
            "Spotify", "DiscordUpdate", "SteamWebHelper", "AnyDesk", "TeamViewer"
        };

        public ZeroJitterProcessSuspender(ILoggingService logger)
        {
            _logger = logger;
        }

        public void ActivateZeroJitterMode()
        {
            _logger.LogInfo("[ZeroJitter] Iniciando congelamento profundo de processos secundários...");
            
            try
            {
                var processes = Process.GetProcesses();
                foreach (var p in processes)
                {
                    if (SafeToSuspend.Contains(p.ProcessName))
                    {
                        if (SuspendProcess(p.Id))
                        {
                            _suspendedPids.Add(p.Id);
                            _logger.LogDebug($"[ZeroJitter] Processo '{p.ProcessName}' (PID {p.Id}) CONGELADO (0% CPU).");
                        }
                    }
                    p.Dispose();
                }
                _logger.LogSuccess($"[ZeroJitter] {_suspendedPids.Count} processos irrelevantes foram completamente congelados. Zero Jitter ativado.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ZeroJitter] Erro ao aplicar congelamento: {ex.Message}");
            }
        }

        public void DeactivateZeroJitterMode()
        {
            _logger.LogInfo("[ZeroJitter] Restaurando processos congelados...");
            
            int resumedCount = 0;
            while (_suspendedPids.TryTake(out int pid))
            {
                if (ResumeProcess(pid))
                {
                    resumedCount++;
                }
            }

            _logger.LogSuccess($"[ZeroJitter] {resumedCount} processos foram descongelados e voltaram à operação normal.");
        }

        private bool SuspendProcess(int pid)
        {
            IntPtr hProcess = IntPtr.Zero;
            try
            {
                hProcess = OpenProcess(PROCESS_SUSPEND_RESUME, false, pid);
                if (hProcess != IntPtr.Zero)
                {
                    int status = NtSuspendProcess(hProcess);
                    return status == 0;
                }
            }
            catch { }
            finally
            {
                if (hProcess != IntPtr.Zero) CloseHandle(hProcess);
            }
            return false;
        }

        private bool ResumeProcess(int pid)
        {
            IntPtr hProcess = IntPtr.Zero;
            try
            {
                hProcess = OpenProcess(PROCESS_SUSPEND_RESUME, false, pid);
                if (hProcess != IntPtr.Zero)
                {
                    int status = NtResumeProcess(hProcess);
                    return status == 0;
                }
            }
            catch { }
            finally
            {
                if (hProcess != IntPtr.Zero) CloseHandle(hProcess);
            }
            return false;
        }
    }
}
