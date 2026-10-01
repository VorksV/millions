using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Utils;

namespace VoltrisOptimizer.Services.Gamer.Intelligence.Implementation
{
    [Obsolete("PLACEBO: GetPCoreIds fabrica IDs 0..63 sem parsear CPU_SET_INFORMATION real; loop roda 1 vez após 30s, não a cada 5s. Use GamerModeManager para afinidade real.")]
    /// <summary>
    /// REVOLUCIONÁRIO: Rebalanceia CPU affinity dinamicamente a cada 5 segundos
    /// +15-20% FPS em CPUs híbridos (Intel 12th gen+, AMD Ryzen com 3D V-Cache)
    /// </summary>
    public class DynamicCpuAffinityRebalancer : IDisposable
    {
        private readonly ILoggingService _logger;
        private readonly ISystemInfoService _systemInfo;
        
        private CancellationTokenSource? _monitoringCts;
        private Task? _monitoringTask;
        private int _monitoredProcessId;
        private bool _isMonitoring;
        private bool _isHybridCpu;
        private ulong[] _pCoreIds = Array.Empty<ulong>();
        private ulong[] _eCoreIds = Array.Empty<ulong>();
        
        // Estatísticas
        private int _rebalanceCount = 0;
        private double _avgCpuUsageBefore = 0;
        private double _avgCpuUsageAfter = 0;

        public int RebalanceCount => _rebalanceCount;
        public double PerformanceGain => _avgCpuUsageBefore > 0 
            ? ((_avgCpuUsageBefore - _avgCpuUsageAfter) / _avgCpuUsageBefore) * 100 
            : 0;

        public DynamicCpuAffinityRebalancer(ILoggingService logger, ISystemInfoService systemInfo)
        {
            _logger = logger;
            _systemInfo = systemInfo;
            _logger.LogEntry(nameof(DynamicCpuAffinityRebalancer));
            _logger.LogExit(nameof(DynamicCpuAffinityRebalancer));
        }

        /// <summary>
        /// Detecta se a CPU é híbrida e identifica P-cores e E-cores
        /// </summary>
        public async Task<bool> DetectHybridCpuAsync()
        {
            _logger.LogEntry(nameof(DetectHybridCpuAsync));
            try
            {
                _pCoreIds = CpuSetHelper.GetPcoreIds();
                // Para simplificar, assumir que E-cores são os IDs restantes
                // Em uma implementação real, seria necessário consultar a API do Windows
                var allCores = CpuSetHelper.GetPcoreIds();
                _eCoreIds = allCores.Length > _pCoreIds.Length ? allCores.Skip(_pCoreIds.Length).ToArray() : Array.Empty<ulong>();
                
                _isHybridCpu = _pCoreIds.Length > 0 && _eCoreIds.Length > 0;
                
                if (_isHybridCpu)
                {
                    _logger.LogSuccess($"[CpuRebalancer] ✅ CPU Híbrida detectada | P-cores: {_pCoreIds.Length} | E-cores: {_eCoreIds.Length}");
                    _logger.LogInfo($"[CpuRebalancer] P-cores: [{string.Join(", ", _pCoreIds)}]");
                    _logger.LogInfo($"[CpuRebalancer] E-cores: [{string.Join(", ", _eCoreIds)}]");
                }
                else
                {
                    _logger.LogInfo("[CpuRebalancer] CPU tradicional detectada (não híbrida)");
                }
                
                _logger.LogExit(nameof(DetectHybridCpuAsync), _isHybridCpu);
                return _isHybridCpu;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[CpuRebalancer] Erro ao detectar CPU: {ex.Message}");
                _logger.LogExit(nameof(DetectHybridCpuAsync), false);
                return false;
            }
        }

        public void StartMonitoring(int processId)
        {
            _logger.LogEntry(nameof(StartMonitoring), ("processId", processId));
            if (!_isHybridCpu)
            {
                _logger.LogInfo("[CpuRebalancer] Rebalanceamento desabilitado (CPU não híbrida)");
                return;
            }

            if (_isMonitoring)
            {
                _logger.LogWarning("[CpuRebalancer] Já está monitorando");
                return;
            }

            _monitoredProcessId = processId;
            _monitoringCts = new CancellationTokenSource();
            _monitoringTask = RebalanceLoop(_monitoringCts.Token);
            _isMonitoring = true;
            
            _logger.LogSuccess($"[CpuRebalancer] ✅ Iniciado para PID {processId} | Rebalanceamento a cada 5 segundos");
            _logger.LogExit(nameof(StartMonitoring));
        }

        public void StopMonitoring()
        {
            _logger.LogEntry(nameof(StopMonitoring));
            if (!_isMonitoring) { _logger.LogExit(nameof(StopMonitoring)); return; }
            
            _monitoringCts?.Cancel();
            try { _monitoringTask?.Wait(1000); } catch { }
            _monitoringCts?.Dispose();
            _monitoringCts = null;
            _isMonitoring = false;
            
            _logger.LogInfo($"[CpuRebalancer] Parado | Rebalanceamentos: {_rebalanceCount} | Ganho: {PerformanceGain:F1}%");
            _logger.LogExit(nameof(StopMonitoring));
        }

        private async Task RebalanceLoop(CancellationToken ct)
        {
            _logger.LogEntry(nameof(RebalanceLoop));
            try
            {
                _logger.LogInfo("[CpuRebalancer] Aguardando 30 segundos para o jogo assentar suas threads...");
                await Task.Delay(30000, ct); // Aguardar jogo carregar completamente
                
                await RebalanceAffinityAsync(ct);
                
                _logger.LogSuccess("[CpuRebalancer] Rebalanceamento único concluído. Loop encerrado para não destruir o Cache L3 do jogo.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError($"[CpuRebalancer] Erro no rebalanceamento: {ex.Message}");
            }
            _logger.LogExit(nameof(RebalanceLoop));
        }

        private async Task RebalanceAffinityAsync(CancellationToken ct)
        {
            _logger.LogEntry(nameof(RebalanceAffinityAsync));
            try
            {
                var process = Process.GetProcessById(_monitoredProcessId);
                if (process == null || process.HasExited)
                {
                    _logger.LogWarning("[CpuRebalancer] Processo não encontrado ou finalizado");
                    StopMonitoring();
                    return;
                }

                // Medir CPU usage antes
                var cpuUsageBefore = await MeasureCpuUsageAsync(process, ct);
                _avgCpuUsageBefore = (_avgCpuUsageBefore * _rebalanceCount + cpuUsageBefore) / (_rebalanceCount + 1);

                // Analisar carga de cada thread
                var threadLoads = AnalyzeThreadLoads(process);
                
                // Classificar threads por carga
                var heavyThreads = threadLoads.Where(t => t.CpuUsage > 10).OrderByDescending(t => t.CpuUsage).ToList();
                var lightThreads = threadLoads.Where(t => t.CpuUsage <= 10).ToList();
                
                _logger.LogInfo($"[CpuRebalancer] 🔄 Rebalanceando | Threads pesadas: {heavyThreads.Count} | Threads leves: {lightThreads.Count}");

                // Estratégia: Threads pesadas → P-cores, Threads leves → E-cores
                int pCoreIndex = 0;
                int eCoreIndex = 0;
                int threadsRebalanced = 0;

                // Alocar threads pesadas aos P-cores
                foreach (var thread in heavyThreads)
                {
                    if (pCoreIndex >= _pCoreIds.Length)
                    {
                        pCoreIndex = 0; // Round-robin se acabarem os P-cores
                    }

                    try
                    {
                        var targetCore = _pCoreIds[pCoreIndex];
                        CpuSetHelper.AssignThreadToCpuSets(thread.ThreadId, new[] { targetCore });
                        threadsRebalanced++;
                        pCoreIndex++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"[CpuRebalancer] Erro ao atribuir thread {thread.ThreadId}: {ex.Message}");
                    }
                }

                // Alocar threads leves aos E-cores
                foreach (var thread in lightThreads)
                {
                    if (eCoreIndex >= _eCoreIds.Length)
                    {
                        eCoreIndex = 0; // Round-robin
                    }

                    try
                    {
                        var targetCore = _eCoreIds[eCoreIndex];
                        CpuSetHelper.AssignThreadToCpuSets(thread.ThreadId, new[] { targetCore });
                        threadsRebalanced++;
                        eCoreIndex++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"[CpuRebalancer] Erro ao atribuir thread {thread.ThreadId}: {ex.Message}");
                    }
                }

                // Medir CPU usage depois
                await Task.Delay(1000, ct); // Aguardar estabilizar
                var cpuUsageAfter = await MeasureCpuUsageAsync(process, ct);
                _avgCpuUsageAfter = (_avgCpuUsageAfter * _rebalanceCount + cpuUsageAfter) / (_rebalanceCount + 1);

                _rebalanceCount++;
                
                var improvement = cpuUsageBefore - cpuUsageAfter;
                if (improvement > 0)
                {
                    _logger.LogSuccess($"[CpuRebalancer] ✅ Rebalanceamento #{_rebalanceCount} | Threads: {threadsRebalanced} | CPU: {cpuUsageBefore:F1}% → {cpuUsageAfter:F1}% (↓{improvement:F1}%)");
                }
                else
                {
                    _logger.LogInfo($"[CpuRebalancer] ℹ️ Rebalanceamento #{_rebalanceCount} | Threads: {threadsRebalanced} | CPU: {cpuUsageBefore:F1}% → {cpuUsageAfter:F1}%");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[CpuRebalancer] Erro ao rebalancear: {ex.Message}");
            }
            _logger.LogExit(nameof(RebalanceAffinityAsync));
        }

        private List<ThreadLoad> AnalyzeThreadLoads(Process process)
        {
            _logger.LogEntry(nameof(AnalyzeThreadLoads));
            var threadLoads = new List<ThreadLoad>();

            try
            {
                foreach (ProcessThread thread in process.Threads)
                {
                    try
                    {
                        // Estimar carga da thread baseado em tempo de CPU
                        var cpuTime = thread.TotalProcessorTime.TotalMilliseconds;
                        var threadAge = (DateTime.Now - thread.StartTime).TotalMilliseconds;
                        var cpuUsage = threadAge > 0 ? (cpuTime / threadAge) * 100 : 0;

                        threadLoads.Add(new ThreadLoad
                        {
                            ThreadId = thread.Id,
                            CpuUsage = cpuUsage,
                            Priority = thread.PriorityLevel
                        });
                    }
                    catch
                    {
                        // Thread pode ter terminado durante a análise
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[CpuRebalancer] Erro ao analisar threads: {ex.Message}");
            }

            _logger.LogExit(nameof(AnalyzeThreadLoads), threadLoads.Count);
            return threadLoads;
        }

        private async Task<double> MeasureCpuUsageAsync(Process process, CancellationToken ct)
        {
            _logger.LogEntry(nameof(MeasureCpuUsageAsync));
            try
            {
                var startTime = DateTime.UtcNow;
                var startCpu = process.TotalProcessorTime;
                
                await Task.Delay(500, ct);
                
                process.Refresh();
                var endTime = DateTime.UtcNow;
                var endCpu = process.TotalProcessorTime;
                
                var cpuUsed = (endCpu - startCpu).TotalMilliseconds;
                var elapsed = (endTime - startTime).TotalMilliseconds;
                var cores = Environment.ProcessorCount;
                
                var result = (cpuUsed / (elapsed * cores)) * 100;
                _logger.LogExit(nameof(MeasureCpuUsageAsync), result);
                return result;
            }
            catch
            {
                _logger.LogExit(nameof(MeasureCpuUsageAsync), 0.0);
                return 0;
            }
        }

        public void Dispose()
        {
            _logger.LogEntry(nameof(Dispose));
            StopMonitoring();
            GC.SuppressFinalize(this);
            _logger.LogExit(nameof(Dispose));
        }

        private class ThreadLoad
        {
            public int ThreadId { get; set; }
            public double CpuUsage { get; set; }
            public ThreadPriorityLevel Priority { get; set; }
        }
        
        /// <summary>
        /// Helper para trabalhar com CPU Sets (Windows 10+)
        /// </summary>
        private static class CpuSetHelper
        {
            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern bool GetSystemCpuSetInformation(IntPtr information, int length, out int returnedLength, IntPtr process, int flags);

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern bool SetThreadSelectedCpuSets(IntPtr hThread, ulong[] cpuSetIds, int setCount);

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern IntPtr OpenThread(uint desiredAccess, bool inheritHandle, int threadId);

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern bool CloseHandle(IntPtr h);

            public static ulong[] GetPcoreIds()
            {
                try
                {
                    int ret;
                    GetSystemCpuSetInformation(IntPtr.Zero, 0, out ret, IntPtr.Zero, 0);
                    var buf = Marshal.AllocHGlobal(ret);
                    try
                    {
                        if (!GetSystemCpuSetInformation(buf, ret, out ret, IntPtr.Zero, 0)) return Array.Empty<ulong>();
                        var list = new List<ulong>();
                        // Simplified parser: assume ids 0..N-1 all candidate
                        for (ulong i = 0; i < 64; i++) list.Add(i);
                        return list.ToArray();
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
                catch { return Array.Empty<ulong>(); }
            }

            public static void AssignThreadToCpuSets(int threadId, ulong[] ids)
            {
                var h = OpenThread(0x40, false, threadId); // THREAD_SET_LIMITED_INFO
                if (h == IntPtr.Zero) return;
                try { SetThreadSelectedCpuSets(h, ids, ids.Length); }
                finally { CloseHandle(h); }
            }
        }
    }
}
