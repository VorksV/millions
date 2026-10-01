using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Optimization.Providers
{
    public class WindowsProcessProvider : IProcessProvider
    {
        private readonly ProcessCacheService? _cache;
        private readonly ILoggingService? _logger;

        // Removidos P/Invokes duplicados

        public WindowsProcessProvider(ProcessCacheService? cache = null, ILoggingService? logger = null)
        {
            _cache = cache;
            _logger = logger;
            _logger?.LogInfo("[Providers] WindowsProcessProvider inicializado.");
        }

        public Process[] GetProcesses()
        {
            if (_cache != null)
            {
                return _cache.GetCachedProcesses().ToArray();
            }
            return Process.GetProcesses();
        }

        public Process? GetForegroundProcess()
        {
            int pid = VoltrisOptimizer.Core.ForegroundWindowTracker.Instance.CurrentPid;
            if (pid <= 0) return null;

            try
            {
                // Tenta pegar do cache primeiro
                if (_cache != null)
                {
                    var cached = _cache.GetCachedProcesses().FirstOrDefault(p => p.Id == (int)pid);
                    if (cached != null) return cached;
                }
                return Process.GetProcessById((int)pid);
            }
            catch
            {
                return null;
            }
        }
    }
}
