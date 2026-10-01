  using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer.Optimization
{
    public sealed class CoreIsolationEngine : IDisposable
    {
        private readonly ILoggingService _logger;

        private nint _originalGameAffinity;
        private nint _isolatedAffinityMask;
        private nint _systemAffinityMask;
        private bool _isApplied;
        private int _totalLogicalCores;
        private bool _isHybridCpu;
        private int[]? _pCoreIndices;
        private int[]? _eCoreIndices;
        // AUDITORIA: _enforcementTimer removido — polling causa overhead desnecessário

        public bool IsApplied => _isApplied;
        public nint GameAffinityMask => _isolatedAffinityMask;
        public string TopologySummary { get; private set; } = string.Empty;

        public CoreIsolationEngine(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void DetectTopology()
        {
            _logger.LogInfo("[CoreIsolation.DetectTopology] Entry");
            _totalLogicalCores = Environment.ProcessorCount;
            int physicalCores = 0;
            int logicalPerPhysical = 1;

            try
            {
                var coreTopology = new Dictionary<byte, List<int>>();
                var coreOffsets = new Dictionary<byte, List<int>>();

                foreach (var item in new System.Management.ManagementObjectSearcher(
                    "SELECT * FROM Win32_Processor").Get())
                {
                    using var mo = item;
                    physicalCores = Convert.ToInt32(mo["NumberOfCores"]);
                    logicalPerPhysical = Convert.ToInt32(mo["NumberOfLogicalProcessors"]) / Math.Max(1, physicalCores);
                }

                _isHybridCpu = _totalLogicalCores > physicalCores * logicalPerPhysical;

                if (_isHybridCpu)
                {
                    // Em CPUs Híbridas (Intel 12a Gen+), o Windows Thread Director já faz um ótimo trabalho.
                    // Isolar núcleos manualmente sem a API nativa pode quebrar a performance severamente.
                    // Vamos dar ao jogo acesso a todos os núcleos para que o SO cuide.
                    var allCores = Enumerable.Range(0, _totalLogicalCores).ToArray();
                    _pCoreIndices = allCores;
                    _eCoreIndices = Array.Empty<int>();

                    _isolatedAffinityMask = BuildAffinityMask(_pCoreIndices);
                    _systemAffinityMask = _isolatedAffinityMask; // Sistema e Jogo livres

                    TopologySummary = $"Hibrido: {_totalLogicalCores} logicos (Isolamento manual desativado por segurança para Thread Director)";
                    _logger.LogInfo($"[CoreIsolation] CPU Hibrida detectada. {TopologySummary}");
                }
                else if (logicalPerPhysical > 1)
                {
                    // CPU uniforme COM Hyper-Threading (Ex: AMD Ryzen, Intel Core pré-12a gen).
                    // CORREÇÃO CRÍTICA: Threads irmãos (HT) são sempre adjacentes (0 e 1, 2 e 3).
                    // Para o jogo usar núcleos físicos puros, ele deve usar os índices onde (i % logicalPerPhysical == 0).
                    var primaryThreads = new List<int>();
                    var htSiblings = new List<int>();

                    for (int i = 0; i < _totalLogicalCores; i++)
                    {
                        if (i % logicalPerPhysical == 0)
                            primaryThreads.Add(i); // Jogo: Núcleos físicos reais (0, 2, 4, 6...)
                        else
                            htSiblings.Add(i); // Sistema: Threads HT irmãos (1, 3, 5, 7...)
                    }

                    _pCoreIndices = primaryThreads.ToArray();
                    _eCoreIndices = htSiblings.ToArray();

                    _isolatedAffinityMask = BuildAffinityMask(_pCoreIndices);
                    _systemAffinityMask = BuildAffinityMask(_eCoreIndices);

                    TopologySummary = $"Uniforme+HT: {_totalLogicalCores} logicos (fisicos para jogo, HT para sistema)";
                    _logger.LogInfo($"[CoreIsolation] CPU Uniforme+HT. {TopologySummary}");
                }
                else
                {
                    // CPU uniforme SEM Hyper-Threading: jogo usa metade superior,
                    // sistema usa metade inferior
                    int gameCores = Math.Max(1, _totalLogicalCores / 2);
                    int systemCores = _totalLogicalCores - gameCores;

                    var gameIndices = Enumerable.Range(systemCores, gameCores).ToArray();
                    var systemIndices = Enumerable.Range(0, systemCores).ToArray();

                    _isolatedAffinityMask = BuildAffinityMask(gameIndices);
                    _systemAffinityMask = BuildAffinityMask(systemIndices);

                    _pCoreIndices = gameIndices;
                    _eCoreIndices = systemIndices;

                    TopologySummary = $"Uniforme: {_totalLogicalCores} logicos (jogo={gameCores}, sistema={systemCores})";
                    _logger.LogInfo($"[CoreIsolation] CPU Uniforme (sem HT). {TopologySummary}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[CoreIsolation] Erro ao detectar topologia: {ex.Message}");
                _isolatedAffinityMask = (nint)((1UL << Math.Max(1, _totalLogicalCores - 2)) - 1);
                _systemAffinityMask = ~_isolatedAffinityMask;
                TopologySummary = $"Fallback: {_totalLogicalCores} logicos";
            }
            _logger.LogInfo("[CoreIsolation.DetectTopology] Exit");
        }

        public void ApplyIsolation(int gameProcessId)
        {
            _logger.LogInfo("[CoreIsolation.ApplyIsolation] Entry");
            if (_isolatedAffinityMask == 0)
            {
                _logger.LogWarning("[CoreIsolation] Topologia nao detectada. Execute DetectTopology() primeiro.");
                _logger.LogInfo("[CoreIsolation.ApplyIsolation] Exit (no topology)");
                return;
            }

            try
            {
                var gameProcess = Process.GetProcessById(gameProcessId);

                _originalGameAffinity = gameProcess.ProcessorAffinity;

                // ════════════════════════════════════════════════════════════
                // CORRECAO CRITICA: SO isola o jogo em CPUs hibridas (P-cores).
                // Em CPUs uniformes (com ou sem HT), restringir afinidade do jogo
                // degrada performance — o proprio escalonador do Windows gerencia melhor.
                // ════════════════════════════════════════════════════════════
                if (_isHybridCpu)
                {
                    gameProcess.ProcessorAffinity = _isolatedAffinityMask;
                    _logger.LogInfo($"[CoreIsolation] Jogo (PID={gameProcessId}) isolado em P-cores: {BitMaskToString(_isolatedAffinityMask)}");
                }
                else
                {
                    _logger.LogInfo($"[CoreIsolation] CPU uniforme — afinidade do jogo nao alterada. Usando todos os {_totalLogicalCores} nucleos.");
                }

                // ════════════════════════════════════════════════════════════
                // MUDANÇA ARQUITETÔNICA PROFISSIONAL (Anti-Stuttering):
                // Processos de background NÃO devem ter sua afinidade restrita!
                // Mudar a afinidade de dwm.exe, audiodg.exe ou drivers quebra o
                // Thread Director do Windows 11 e causa DPC Latency/Stuttering massivo.
                // Agora confiamos no Windows Scheduler para o restante do sistema.
                // ════════════════════════════════════════════════════════════
                
                _logger.LogInfo($"[CoreIsolation] Isolamento de background ignorado intencionalmente para evitar DPC Latency. O Windows Scheduler gerenciara o SO.");

                // TrySteerInterruptsAwayFromGameCores() desativado: Forçar IRQs para E-cores eleva latência.

                // AUDITORIA FORENSE: Timer de enforcement REMOVIDO.
                // Polling a cada 5s causa: (a) syscall desnecessário, (b) contenção de scheduler,
                // (c) gasto de CPU em loop de verificação. A afinidade definida uma vez é preservada
                // pelo kernel a menos que outro processo a altere — cenário raro que não justifica polling.
                _isApplied = true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[CoreIsolation] Erro ao isolar jogo PID={gameProcessId}: {ex.Message}");
            }
            _logger.LogInfo("[CoreIsolation.ApplyIsolation] Exit");
        }

        public void RemoveIsolation(int gameProcessId)
        {
            _logger.LogInfo("[CoreIsolation.RemoveIsolation] Entry");
            try
            {
                // AUDITORIA: disposal do timer removido — polling foi eliminado

                if (_originalGameAffinity != 0 && _isHybridCpu)
                {
                    try
                    {
                        var gameProcess = Process.GetProcessById(gameProcessId);
                        gameProcess.ProcessorAffinity = _originalGameAffinity;
                    }
                    catch { }
                }

                // ════════════════════════════════════════════════════════════
                // Como não alteramos mais a afinidade global, não precisamos restaurar.
                // ════════════════════════════════════════════════════════════
                
                _isApplied = false;
                _logger.LogInfo($"[CoreIsolation] Isolamento removido. Jogo PID={gameProcessId} restaurado. Processos de SO permaneceram intactos.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[CoreIsolation] Erro ao remover isolamento: {ex.Message}");
            }
            _logger.LogInfo("[CoreIsolation.RemoveIsolation] Exit");
        }

        // AUDITORIA: EnforceIsolationTick removido — polling de 5s eliminado por causar
        // syscalls desnecessários e contenção de scheduler.

        private void TrySteerInterruptsAwayFromGameCores()
        {
            _logger.LogDebug("[CoreIsolation.TrySteerInterruptsAwayFromGameCores] Entry");
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Enum\PCI", writable: false);

                if (key == null) return;

                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    try
                    {
                        using var deviceKey = key.OpenSubKey(subKeyName);
                        if (deviceKey == null) continue;

                        foreach (var subSub in deviceKey.GetSubKeyNames())
                        {
                            try
                            {
                                using var intKey = deviceKey.OpenSubKey($@"{subSub}\Device Parameters\Interrupt Management\Affinity Policy", writable: true);
                                if (intKey != null)
                                {
                                    intKey.SetValue("DevicePolicy", 4, Microsoft.Win32.RegistryValueKind.DWord);
                                    intKey.SetValue("AssignmentSetOverride", (ulong)_systemAffinityMask, Microsoft.Win32.RegistryValueKind.QWord);
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[CoreIsolation] Interrupt steering nao disponivel: {ex.Message}");
            }
            _logger.LogDebug("[CoreIsolation.TrySteerInterruptsAwayFromGameCores] Exit");
        }

        private static nint BuildAffinityMask(int[] coreIndices)
        {
            ulong mask = 0;
            foreach (int idx in coreIndices)
            {
                if (idx >= 0 && idx < 64)
                    mask |= 1UL << idx;
            }
            return (nint)mask;
        }

        private static string BitMaskToString(nint mask)
        {
            var cores = new List<int>();
            ulong m = (ulong)mask;
            for (int i = 0; i < 64; i++)
            {
                if ((m & (1UL << i)) != 0)
                    cores.Add(i);
            }
            return cores.Count > 0 ? string.Join(",", cores) : "(none)";
        }

        public void Dispose()
        {
            _logger.LogDebug("[CoreIsolation.Dispose] Entry");
            // AUDITORIA: timer disposal removido — sem polling ativo
            _logger.LogDebug("[CoreIsolation.Dispose] Exit");
        }
    }
}