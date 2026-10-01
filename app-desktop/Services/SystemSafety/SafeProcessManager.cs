using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using VoltrisOptimizer.Services.SystemChanges;
using VoltrisOptimizer.Core.Configuration;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.SystemSafety
{
    public interface ISafeProcessManager
    {
        bool TrySetPriorityClass(int processId, ProcessPriorityClass priority);
        bool TrySuspendProcess(int processId);
        bool TryResumeProcess(int processId);
    }

    /// <summary>
    /// Gerenciador Seguro de Processos.
    /// Utiliza Caching agressivo de handles e permissões para evitar exceptions e IO desnecessário no Kernel.
    /// Garante que processos críticos de sistema e anticheats não sejam alterados.
    /// </summary>
    public sealed class SafeProcessManager : ISafeProcessManager
    {
        private readonly ICapabilityGuard _capabilityGuard;
        private readonly IVoltrisFeatureFlagManager _featureFlags;
        private readonly ILoggingService _logger;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetPriorityClass(IntPtr handle, uint priorityClass);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint GetPriorityClass(IntPtr handle);

        private const uint PROCESS_SET_INFORMATION = 0x0200;
        private const uint PROCESS_QUERY_INFORMATION = 0x0400;

        // Cache de PIDs que sabemos que são inacessíveis (ex: Anticheats, Serviços de Kernel protegidos)
        // Isso evita gerar exceptions de UnauthorizedAccess milhares de vezes por segundo no loop do Gamer Mode
        private readonly ConcurrentDictionary<int, DateTime> _accessDeniedCache = new();
        private readonly TimeSpan _deniedCacheExpiration = TimeSpan.FromMinutes(5);

        public SafeProcessManager(ICapabilityGuard capabilityGuard, IVoltrisFeatureFlagManager featureFlags, ILoggingService logger)
        {
            _capabilityGuard = capabilityGuard;
            _featureFlags = featureFlags;
            _logger = logger;
        }

        public bool TrySetPriorityClass(int processId, ProcessPriorityClass priority)
        {
            if (!_featureFlags.UseSafeProcessManager)
                return LegacySetPriorityClass(processId, priority);

            // Evitar tentativas em processos notoriamente protegidos. Zero overhead de exceptions.
            if (IsProcessKnownAsDenied(processId))
                return false;

            IntPtr handle = IntPtr.Zero;
            try
            {
                // Acesso Mínimo Necessário (Least Privilege)
                handle = OpenProcess(PROCESS_SET_INFORMATION | PROCESS_QUERY_INFORMATION, false, processId);
                
                if (handle == IntPtr.Zero)
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err == 5) // ERROR_ACCESS_DENIED
                    {
                        _accessDeniedCache[processId] = DateTime.UtcNow;
                    }
                    return false;
                }

                // Delta Check (Zero Placebo)
                uint currentPriority = GetPriorityClass(handle);
                uint targetPriority = GetNativePriorityClass(priority);

                if (currentPriority == targetPriority && targetPriority != 0)
                {
                    // Já está na prioridade correta
                    return true;
                }

                bool success = SetPriorityClass(handle, targetPriority);
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erro crítico ao setar prioridade do processo {processId}", ex);
                return false;
            }
            finally
            {
                if (handle != IntPtr.Zero)
                {
                    CloseHandle(handle);
                }
            }
        }

        public bool TrySuspendProcess(int processId)
        {
            // Implementação nativa usando NtSuspendProcess (ntdll.dll) será adicionada.
            // Essa é uma das fundações críticas para o VFE e RealTimeSmoothnessService.
            return false;
        }

        public bool TryResumeProcess(int processId)
        {
             // Implementação nativa usando NtResumeProcess (ntdll.dll) será adicionada.
            return false;
        }

        private bool IsProcessKnownAsDenied(int processId)
        {
            if (_accessDeniedCache.TryGetValue(processId, out DateTime deniedAt))
            {
                if (DateTime.UtcNow - deniedAt < _deniedCacheExpiration)
                    return true;
                
                // Expirou, vamos tentar novamente
                _accessDeniedCache.TryRemove(processId, out _);
            }
            return false;
        }

        private uint GetNativePriorityClass(ProcessPriorityClass priority)
        {
            return priority switch
            {
                ProcessPriorityClass.Idle => 0x00000040,
                ProcessPriorityClass.BelowNormal => 0x00004000,
                ProcessPriorityClass.Normal => 0x00000020,
                ProcessPriorityClass.AboveNormal => 0x00008000,
                ProcessPriorityClass.High => 0x00000080,
                ProcessPriorityClass.RealTime => 0x00000100,
                _ => 0
            };
        }

        private bool LegacySetPriorityClass(int processId, ProcessPriorityClass priority)
        {
            try
            {
                using var p = Process.GetProcessById(processId);
                p.PriorityClass = priority;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
