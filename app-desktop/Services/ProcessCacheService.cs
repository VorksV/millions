using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using VoltrisOptimizer.Utils.Win32;

namespace VoltrisOptimizer.Services
{
    /// <summary>
    /// Cache global de processos para eliminar chamadas repetidas a Process.GetProcesses()
    /// CRÍTICO: Process.GetProcesses() é MUITO custoso (50-150ms) e causa stuttering
    /// </summary>
    public class ProcessCacheService : IDisposable
    {
        private readonly Dictionary<int, CachedProcessInfo> _cache = new();
        private readonly ReaderWriterLockSlim _lock = new();
        private readonly ILoggingService? _logger;
        private DateTime _lastFullRefresh = DateTime.MinValue;
        private readonly TimeSpan _fullRefreshInterval = TimeSpan.FromSeconds(30);
        private CancellationTokenSource? _cts;
        private Task? _backgroundRefreshTask;
        private bool _disposed = false;
        private CachedProcessInfo[] _cachedProcessInfosSnapshot = Array.Empty<CachedProcessInfo>();
        private Process[] _cachedProcessesSnapshot = Array.Empty<Process>();

        public ProcessCacheService(ILoggingService? logger = null)
        {
            _logger = logger;
            
            // Fazer refresh IMEDIATO síncrono via API Nativa
            try
            {
                var processes = ScanProcessesNative();
                foreach (var proc in processes)
                {
                    _cache[proc.Id] = proc;
                }
                _lastFullRefresh = DateTime.Now;
                
                // Initialize snapshots
                _cachedProcessInfosSnapshot = _cache.Values.ToArray();
                _cachedProcessesSnapshot = _cache.Values.Where(i => i.Process != null).Select(i => i.Process!).ToArray();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[ProcessCacheService] Erro na inicialização do cache: {ex.Message}");
            }
            
            // Iniciar refresh em background
            _cts = new CancellationTokenSource();
            _backgroundRefreshTask = Task.Run(() => BackgroundRefreshLoopAsync(_cts.Token));
            
            _logger?.LogInfo($"[ProcessCache] Cache de processos inicializado ({_cache.Count} processos)");
        }

        /// <summary>
        /// Retorna informações ricas dos processos do cache (MUITO RÁPIDO, ZERO ALOCAÇÃO)
        /// </summary>
        public IEnumerable<CachedProcessInfo> GetCachedProcessInfos()
        {
            return _cachedProcessInfosSnapshot;
        }

        /// <summary>
        /// Retorna processos do cache (compatibilidade legado, ZERO ALOCAÇÃO)
        /// </summary>
        public IEnumerable<Process> GetCachedProcesses()
        {
            return _cachedProcessesSnapshot;
        }

        /// <summary>
        /// Retorna processos filtrados por nome (rápido - usa cache)
        /// </summary>
        public IEnumerable<Process> GetProcessesByName(string processName)
        {
            _lock.EnterReadLock();
            try
            {
                return _cache.Values
                    .Where(c => c.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase))
                    .Select(c => c.Process)
                    .Where(p => !p.HasExited)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[ProcessCacheService] Erro ao buscar processos por nome: {ex.Message}");
                return Array.Empty<Process>();
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        /// <summary>
        /// Verifica se processo existe (rápido - usa cache)
        /// </summary>
        public bool ProcessExists(int pid)
        {
            _lock.EnterReadLock();
            try
            {
                if (_cache.TryGetValue(pid, out var info))
                {
                    try
                    {
                        return !info.Process.HasExited;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning($"[ProcessCacheService] Erro ao verificar processo {pid}: {ex.Message}");
                        return false;
                    }
                }
                return false;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        /// <summary>
        /// Loop de refresh em background (não bloqueia)
        /// </summary>
        private async Task BackgroundRefreshLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                // Boundary Tracking: Registra o nome da thread
                VoltrisOptimizer.Services.Diagnostics.ThreadNameRegistry.Register("ProcessCache-Refresh");

                try
                {
                    await Task.Delay(_fullRefreshInterval, ct);
                    
                    using var profiler = VoltrisOptimizer.Services.Diagnostics.CpuSelfProfiler.Instance.BeginSection("ProcessCache.Refresh");
                    await RefreshCacheAsync();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning($"[ProcessCache] Erro no refresh: {ex.Message}");
                }
                finally
                {
                    VoltrisOptimizer.Services.Diagnostics.ThreadNameRegistry.Unregister();
                }
            }
        }

        /// <summary>
        /// Atualiza cache via monitoramento nativo (NtQuerySystemInformation)
        /// É cerca de 10-20x mais rápido que Process.GetProcesses()
        /// </summary>
        private async Task RefreshCacheAsync()
        {
            try
            {
                var processes = await Task.Run(() => ScanProcessesNative());

                _lock.EnterWriteLock();
                try
                {
                    try
                    {
                        var currentPids = new HashSet<int>(processes.Select(p => p.Id));
                        var toRemove = _cache.Keys.Where(pid => !currentPids.Contains(pid)).ToList();

                        foreach (var pid in toRemove)
                        {
                            if (_cache.TryGetValue(pid, out var info))
                            {
                                try { info.Process?.Dispose(); } catch (Exception exDispose) { _logger?.LogWarning($"[ProcessCacheService] Erro ao fazer dispose do processo {pid}: {exDispose.Message}"); }
                                _cache.Remove(pid);
                            }
                        }

                        foreach (var info in processes)
                        {
                            if (!_cache.TryGetValue(info.Id, out var existing))
                            {
                                _cache[info.Id] = info;
                            }
                            else
                            {
                                existing.LastSeen = DateTime.Now;
                                existing.WorkingSet64 = info.WorkingSet64;
                                existing.BasePriority = info.BasePriority;

                                var now = DateTime.Now;
                                var elapsed = (now - existing.LastCpuTimestamp).TotalMilliseconds;
                                if (elapsed > 100)
                                {
                                    long totalDelta = (info.LastKernelTime - existing.LastKernelTime) +
                                                     (info.LastUserTime - existing.LastUserTime);

                                    double cpuMs = totalDelta / 10000.0;
                                    existing.CpuUsage = Math.Max(0, Math.Min(100, (cpuMs / elapsed) * 100.0 / Environment.ProcessorCount));

                                    existing.LastKernelTime = info.LastKernelTime;
                                    existing.LastUserTime = info.LastUserTime;
                                    existing.LastCpuTimestamp = now;
                                }
                            }
                        }
                    }
                    catch (OutOfMemoryException)
                    {
                        _cache.Clear();
                        foreach (var info in processes)
                            _cache[info.Id] = info;
                    }

                    _lastFullRefresh = DateTime.Now;

                    _cachedProcessInfosSnapshot = _cache.Values.ToArray();
                    _cachedProcessesSnapshot = _cache.Values.Where(i => i.Process != null).Select(i => i.Process!).ToArray();
                }
                finally { _lock.ExitWriteLock(); }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[ProcessCache] Erro no refresh nativo: {ex.Message}");
            }
        }

        private List<CachedProcessInfo> ScanProcessesNative()
        {
            var results = new List<CachedProcessInfo>();
            int bufferSize = 0x10000;
            IntPtr buffer = Marshal.AllocHGlobal(bufferSize);

            try
            {
                int status = ProcessNativeMethods.NtQuerySystemInformation(
                    ProcessNativeMethods.SystemProcessInformation, buffer, bufferSize, out int requiredSize);

                if (status != 0 && requiredSize > bufferSize)
                {
                    Marshal.FreeHGlobal(buffer);
                    bufferSize = requiredSize + 0x1000;
                    buffer = Marshal.AllocHGlobal(bufferSize);
                    status = ProcessNativeMethods.NtQuerySystemInformation(
                        ProcessNativeMethods.SystemProcessInformation, buffer, bufferSize, out _);
                }

                if (status == 0)
                {
                    IntPtr currentPtr = buffer;
                    while (true)
                    {
                        var info = Marshal.PtrToStructure<ProcessNativeMethods.SYSTEM_PROCESS_INFORMATION>(currentPtr);
                        int pid = info.UniqueProcessId.ToInt32();
                        
                        if (pid > 0)
                        {
                            string name = "Idle";
                            if (info.ImageName.Buffer != IntPtr.Zero)
                            {
                                name = Marshal.PtrToStringUni(info.ImageName.Buffer, info.ImageName.Length / 2);
                            }

                            // PERFORMANCE: NÃO criar o objeto 'Process' aqui.
                            // Quase todos os consumidores só precisam do PID, Nome, RAM e Prioridade
                            // que já temos na estrutura 'info' do NtQuerySystemInformation.
                            results.Add(new CachedProcessInfo
                            {
                                Id = pid,
                                ProcessName = name,
                                WorkingSet64 = (long)info.WorkingSetSize,
                                BasePriority = info.BasePriority,
                                LastKernelTime = info.KernelTime,
                                LastUserTime = info.UserTime,
                                LastCpuTimestamp = DateTime.Now,
                                LastSeen = DateTime.Now,
                                _pidForLazyLoad = pid
                            });
                        }

                        if (info.NextEntryOffset == 0) break;
                        currentPtr = (IntPtr)((long)currentPtr + info.NextEntryOffset);
                    }
                }
            }
            finally { Marshal.FreeHGlobal(buffer); }
            return results;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _cts?.Cancel();
            
            try
            {
                _backgroundRefreshTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch (Exception ex) { _logger?.LogWarning($"[ProcessCacheService] Erro ao esperar background refresh: {ex.Message}"); }

            _cts?.Dispose();

            _lock.EnterWriteLock();
            try
            {
                foreach (var info in _cache.Values)
                {
                    try { info.Process?.Dispose(); } catch (Exception exInfo) { _logger?.LogWarning($"[ProcessCacheService] Erro ao fazer dispose no cleanup: {exInfo.Message}"); }
                }
                _cache.Clear();
            }
            finally
            {
                _lock.ExitWriteLock();
            }

            _lock.Dispose();
            _disposed = true;

            _logger?.LogInfo("[ProcessCache] Cache de processos disposed");
        }

        public class CachedProcessInfo
        {
            public int Id { get; set; }
            public string ProcessName { get; set; } = string.Empty;
            public DateTime LastSeen { get; set; }
            public long WorkingSet64 { get; set; }
            public int BasePriority { get; set; }
            public double CpuUsage { get; set; }
            public long LastKernelTime { get; set; }
            public long LastUserTime { get; set; }
            public DateTime LastCpuTimestamp { get; set; }

            // Lazy Load do objeto Process (pesado)
            internal int _pidForLazyLoad;
            private Process? _process;
            private bool _failedLoad;

            public Process? Process
            {
                get
                {
                    if (_process == null && !_failedLoad)
                    {
                        try { _process = Process.GetProcessById(_pidForLazyLoad); }
                        catch (ArgumentException) { _failedLoad = true; }
                        catch (InvalidOperationException) { _failedLoad = true; }
                        catch { System.Diagnostics.Debug.WriteLine($"[ProcessCacheService.CachedProcessInfo] Falha ao carregar processo PID={_pidForLazyLoad}"); _failedLoad = true; }
                    }
                    return _process;
                }
                set => _process = value;
            }
        }
    }
}
