using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services.Gamer.Interfaces;
using VoltrisOptimizer.Utils;

namespace VoltrisOptimizer.Services.Gamer.Implementation
{
    /// <summary>
    /// Implementação do otimizador de memória para jogos
    /// </summary>
    public class MemoryGamingOptimizerService : IMemoryGamingOptimizer
    {
        private readonly ILoggingService _logger;
        private Task? _monitorTask;
        private CancellationTokenSource? _monitorCts;
        private SafePerformanceCounter? _standbyNormal;
        private SafePerformanceCounter? _standbyReserve;
        private SafePerformanceCounter? _standbyCore;
        private bool _isMonitoring;
        
        // 🔥 CORREÇÃO: Adicionar Instance estático
        private static MemoryGamingOptimizerService? _instance;
        public static MemoryGamingOptimizerService Instance => _instance ??= new MemoryGamingOptimizerService();

        // Native methods para limpeza de standby list
        [DllImport("ntdll.dll")]
        private static extern int NtSetSystemInformation(int SystemInformationClass, ref int SystemInformation, int SystemInformationLength);

        [DllImport("kernel32.dll")]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        private const int SystemMemoryListInformation = 80;
        private const int MemoryPurgeStandbyList = 4;
        private const int MemoryPurgeLowPriorityStandbyList = 5;

        public MemoryGamingOptimizerService(ILoggingService logger = null)
        {
            _logger = logger ?? VoltrisOptimizer.App.LoggingService;

            // [FIX:C-2] NAO inicializar contadores de performance no construtor.
            //
            // BUG ORIGINAL: InitializeCounters() rodava dentro deste construtor, e
            // o construtor e resolvido de forma SINCRONA pelo container de DI na
            // cadeia DashboardViewModel -> GamerViewModel -> IGameModeOrchestrator
            // -> MemoryGamingOptimizerService. Como a resolucao acontece no
            // construtor do ViewModel, ela roda no THREAD DA UI.
            //
            // EVIDENCIA (2026-09-27, VoltrisDiag, snapshot CLRMD 20:56:39.887):
            //   UI THREAD STACK:
            //     MemoryGamingOptimizerService..ctor
            //     MemoryGamingOptimizerService.InitializeCounters()
            //     ...
            //     ServiceProviderEngine+RealServiceProvider.GetService()
            //   UI heartbeat: 3282ms sem resposta
            //
            // A causa do custo e o first-use de PerformanceCounter, que carrega a
            // PerformanceDLL correspondente via registry + LoadLibrary na primeira
            // instancia do processo. Com 3 contadores, isso bloqueia a UI por
            // centenas de ms a segundos — exatamente o freeze de 3,282 ms medido.
            //
            // Agora a inicializacao e assincrona. Os consumidores (linhas 175-177)
            // ja usam "?.NextValue() ?? 0", entao um contador ausente e seguro.
            _ = Task.Run(InitializeCountersAsync);
        }

        // [FIX:C-2] Prontidao dos contadores, exposta para diagnostico.
        public bool AreCountersReady => Volatile.Read(ref _countersReady) == 1;

        private int _countersReady;

        private async Task InitializeCountersAsync()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _logger?.LogInfo("[FIX:C-2] Inicializando PerformanceCounters em background (fora do thread da UI)...");
            await InitializeCounters().ConfigureAwait(false);

            // Volatile.Write publica os campos SOMENTE depois da construcao
            // completa, evitando que um leitor veja um SafePerformanceCounter
            // ainda com o _counter interno em null.
            Volatile.Write(ref _countersReady, 1);
            sw.Stop();
            _logger?.LogInfo(
                $"[FIX:C-2] PerformanceCounters prontos em {sw.Elapsed.TotalMilliseconds:F0}ms (background) | " +
                $"standbyNormal={_standbyNormal != null} standbyReserve={_standbyReserve != null} standbyCore={_standbyCore != null} | " +
                $"esses ~{sw.Elapsed.TotalMilliseconds:F0}ms nao bloqueiam mais a UI thread");
        }

        private async Task InitializeCounters()
        {
            try
            {
                // [FIX:C-2] Publicacao volatil: o leitor na thread da UI so pode
                // enxergar o objeto depois defully construido.
                Volatile.Write(ref _standbyNormal, new SafePerformanceCounter("Memory", "Standby Cache Normal Priority Bytes", readOnly: true));
                Volatile.Write(ref _standbyReserve, new SafePerformanceCounter("Memory", "Standby Cache Reserve Bytes", readOnly: true));
                Volatile.Write(ref _standbyCore, new SafePerformanceCounter("Memory", "Standby Cache Core Bytes", readOnly: true));
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[MemoryOptimizer] Erro ao inicializar contadores: {ex.Message}");
            }

            await Task.CompletedTask.ConfigureAwait(false);
        }

        public bool CleanStandbyList()
        {
            try
            {
                _logger.LogInfo("[MemoryOptimizer] Solicitando CleanStandbyList via Coordinator...");
                // Note: The coordinator will block this if in game
                global::VoltrisOptimizer.Services.Optimization.Memory.VoltrisMemoryCoordinator.Instance.CleanStandbyList();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("[MemoryOptimizer] Erro ao limpar standby list", ex);
                return false;
            }
        }

        public void StartMonitoring(int thresholdMb = 1024, int standbyThresholdMb = 1024)
        {
            if (_isMonitoring) return;

            try
            {
                _monitorCts = new CancellationTokenSource();
                var token = _monitorCts.Token;

                _monitorTask = Task.Run(async () =>
                {
                    _logger.LogInfo("[MemoryOptimizer] Monitor de RAM iniciado");

                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            // Delegar a verificação de histerese e purga para o Coordinator
                            _logger.LogDebug("[MemoryOptimizer] Solicitando avaliação do MemoryCoordinator...");
                            await global::VoltrisOptimizer.Services.Optimization.Memory.VoltrisMemoryCoordinator.Instance.OptimizeMemoryAsync(isManualClick: false);

                            await Task.Delay(10000, token); // 10s polling, o Coordinator gerencia os 30s de histerese
                        }
                        catch (OperationCanceledException) { break; }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"[MemoryOptimizer] Erro no monitor: {ex.Message}");
                            try { await Task.Delay(15000, token); } catch { _logger.LogDebug("[MemoryOptimizer] Monitor delay falhou, parando monitor."); break; }
                        }
                    }

                    _logger.LogInfo("[MemoryOptimizer] Monitor de RAM parado");
                }, token);
                _isMonitoring = true;
            }
            catch (Exception ex)
            {
                _logger.LogError("[MemoryOptimizer] Erro ao iniciar monitor", ex);
            }
        }

        public void StopMonitoring()
        {
            if (!_isMonitoring) return;

            try
            {
                _monitorCts?.Cancel();
                _monitorCts?.Dispose();
                _monitorCts = null;
                _monitorTask = null;
                _isMonitoring = false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[MemoryOptimizer] Erro ao parar monitor: {ex.Message}");
            }
        }

        public double GetFreeMemoryMb()
        {
            try
            {
                var memoryStatus = new MEMORYSTATUSEX
                {
                    dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>()
                };

                if (GlobalMemoryStatusEx(ref memoryStatus))
                {
                    return memoryStatus.ullAvailPhys / (1024.0 * 1024.0);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[MemoryOptimizer] Erro ao obter memória livre: {ex.Message}");
            }

            return 0;
        }

        public double GetStandbyCacheMb()
        {
            try
            {
                // [FIX:C-2] Leitura volatil: se os contadores ainda nao
                // terminaram de ser construidos em background, os "?.NextValue()"
                // devolvem 0 com seguranca (publicacao via Volatile.Write).
                var normal = Volatile.Read(ref _standbyNormal)?.NextValue() ?? 0;
                var reserve = Volatile.Read(ref _standbyReserve)?.NextValue() ?? 0;
                var core = Volatile.Read(ref _standbyCore)?.NextValue() ?? 0;

                return (normal + reserve + core) / (1024.0 * 1024.0);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[MemoryOptimizer] Erro ao obter standby cache: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Libera recursos
        /// </summary>
        public void Dispose()
        {
            StopMonitoring();

            try
            {
                _standbyNormal?.Dispose();
                _standbyReserve?.Dispose();
                _standbyCore?.Dispose();
            }
            catch (Exception ex) { _logger.LogDebug($"[MemoryOptimizer] Erro ao fazer Dispose de contadores: {ex.Message}"); }
        }
    }
}


