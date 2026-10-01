using VoltrisOptimizer.Helpers;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Services.Gamer.Interfaces;

namespace VoltrisOptimizer.Services.Gamer.GamerModeManager.Services
{
    /// <summary>
    /// Advanced CPU Scheduler Service - Otimização avançada de scheduler para gaming
    /// Core parking granular, thread quantum, NUMA optimization
    /// </summary>
    public class AdvancedCpuSchedulerService : IAdvancedCpuSchedulerService
    {
        private readonly ILoggingService _logger;
        
        // Backup de configurações
        private int? _originalQuantumLength;
        private int? _originalQuantumBoost;
        private int? _originalSeparation;
        
        // Cache híbrido para evitar WMI síncrono
        private bool? _isHybridCpu;
        private readonly object _hybridLock = new object();
        
        // APIs nativas
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessAffinityMask(IntPtr hProcess, IntPtr dwProcessAffinityMask);
        
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetCurrentProcess();
        
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetNumaHighestNodeNumber(out int HighestNodeNumber);
        
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetNumaNodeProcessorMask(int Node, out ulong ProcessorMask);
        
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetThreadAffinityMask(IntPtr hThread, IntPtr dwThreadAffinityMask);
        
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenThread(uint dwDesiredAccess, bool bInheritHandle, uint dwThreadId);
        
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);
        
        // Constantes
        private const uint THREAD_SET_INFORMATION = 0x0200;
        private const uint THREAD_ALL_ACCESS = 0x1F03FF;
        
        public AdvancedCpuSchedulerService(ILoggingService logger)
        {
            _logger.LogEntry(nameof(AdvancedCpuSchedulerService));
            _logger = logger;
            _logger.LogExit(nameof(AdvancedCpuSchedulerService));
        }
        
        /// <summary>
        /// Otimiza scheduler avançado de CPU
        /// </summary>
        public async Task<bool> OptimizeCpuSchedulerAsync()
        {
            _logger.LogEntry(nameof(OptimizeCpuSchedulerAsync));
try
            {
                _logger.LogInfo("[CPU-Scheduler] Iniciando otimização avançada de scheduler...");
                
                // 1. Configurar core parking granular (P-cores only)
                await OptimizeCoreParkingGranularAsync();
                
                // 2. Ajustar thread quantum para gaming
                OptimizeThreadQuantum();
                
                // 3. Configurar NUMA-aware scheduling
                await OptimizeNumaSchedulingAsync();
                
                // 4. Otimizar foreground boost
                OptimizeForegroundBoost();
                
                // 5. Configurar processor affinity inteligente
                await OptimizeProcessorAffinityAsync();
                
                _logger.LogSuccess("[CPU-Scheduler] [OK] Scheduler avançado otimizado - Performance máxima!");
return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("[CPU-Scheduler] Erro na otimização de scheduler", ex);
return false;
            }
            _logger.LogExit(nameof(OptimizeCpuSchedulerAsync));
}
        
        /// <summary>
        /// Otimiza core parking granular (apenas P-cores)
        /// </summary>
        private async Task OptimizeCoreParkingGranularAsync()
        {
            _logger.LogEntry(nameof(OptimizeCoreParkingGranularAsync));
            try
            {
                // Detectar se é CPU híbrida (P-cores + E-cores)
                var isHybrid = await DetectHybridCpuAsync();

                // [FIX:UNICO-DONO-DE-ENERGIA] Core parking granular NÃO é mais
                // gravado no registro.
                //
                // Este método escrevia direto em
                // HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerSettings, com
                // `CreateSubKey` (que CRIA a chave se não existir) e
                // `SetValue("Value", 100)`.
                //
                // Três problemas, em ordem de gravidade:
                //
                // 1. NÍVEL DE CHAVE. `CreateSubKey` em HKLM exige elevação. A
                //    simples leitura de energia não. Este era um caminho de
                //    escrita que só funcionava com o app inteiro elevado — e o
                //    projeto tem vários pontos que pedem elevação justamente
                //    para poder tocar energia.
                //
                // 2. INVISÍVEL PARA O DONO. Estes valores estão no registro
                //    GLOBAL do Windows, não no plano de energia. O Perfil
                //    Inteligente lê e escreve o plano; ele não tem como ler estas
                //    chaves, e portanto não tem como restaurar nem saber que
                //    foram alteradas. É a mesma classe de problema do
                //    `SetTurboBoostPolicy` no PowerArm: uma escrita que o dono da
                //    energia não enxerga, e por isso não pode reverter.
                //
                // 3. O VALOR. Em CPU não-híbrida, o log dizia "Core parking
                //    desabilitado" ao gravar `Value = 100`. Os dois comentários
                //    do código admitem que 100 significa "100% dos núcleos
                //    ativos". Isso é o oposto de estacionamento: é desligar o
                //    mecanismo por completo, deixando o processador com todos os
                //    núcleos acordados sem necessidade. A REGRA 9 do self-test
                //    fixa estacionamento em 100 justamente para nunca desativar,
                //    e este caminho desativava.
                //
                // A detecção de CPU híbrida continua, e vale como informação: ela
                // alimenta o log e o Perfil pode querer considerá-la depois. Só a
                // gravação vai embora.
                if (isHybrid)
                {
                    _logger.LogInfo(
                        "[CPU-Scheduler] [NEUTRALIZADO] CPU híbrida detectada (P-cores + E-cores). " +
                        "Core parking granular NÃO gravado: é energia do Perfil Inteligente, e " +
                        "esta escrita no registro global não é visível para ele.");
                }
                else
                {
                    _logger.LogInfo(
                        "[CPU-Scheduler] [NEUTRALIZADO] CPU não-híbrida. Core parking NÃO " +
                        "gravado — \"desabilitar\" aqui significava manter 100% dos núcleos " +
                        "sempre ativos, o oposto do que a REGRA 9 do perfil exige.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[CPU-Scheduler] Erro ao configurar core parking: {ex.Message}");
            }
            _logger.LogExit(nameof(OptimizeCoreParkingGranularAsync));
}
        
        /// <summary>
        /// Detecta se é CPU híbrida
        /// </summary>
        private async Task<bool> DetectHybridCpuAsync()
        {
            _logger.LogEntry(nameof(DetectHybridCpuAsync));
lock (_hybridLock)
            {
                if (_isHybridCpu.HasValue) return _isHybridCpu.Value;
            }

            try
            {
                bool isHybrid = await Task.Run(() =>
                {
                    try
                    {
                        using var searcher = new System.Management.ManagementObjectSearcher("SELECT * FROM Win32_Processor");
                        foreach (var obj in searcher.Get())
                        {
                using var __dispose_obj = obj;
                            var description = obj["Description"]?.ToString() ?? "";
                            // Verificar se contém indicadores de CPU híbrida
                            if (description.Contains("P-core") || description.Contains("E-core") || 
                                description.Contains("Hybrid") || description.Contains("12th Gen") ||
                                description.Contains("13th Gen") || description.Contains("14th Gen"))
                            {
return true;
                            }
                        }
                    }
                    catch
                    {
                    }
return false;
                });

                lock (_hybridLock)
                {
                    _isHybridCpu = isHybrid;
                }
return isHybrid;
            }
            catch
            {
return false;
            }
            _logger.LogExit(nameof(DetectHybridCpuAsync));
}
        
        /// <summary>
        /// Otimiza thread quantum para gaming
        /// </summary>
        private void OptimizeThreadQuantum()
        {
            _logger.LogEntry(nameof(OptimizeThreadQuantum));
try
            {
                using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\PriorityControl");
                
                // Backup
                _originalQuantumBoost = key.GetValue("Win32PrioritySeparation") as int?;
                
                // Configurar quantum para gaming (menor quantum = maior responsividade)
                key.SetValue("Win32PrioritySeparation", 0x28, RegistryValueKind.DWord); // Quantum curto
                
                // Configurar quantum boost para foreground
                key.SetValue("QuantumBoost", 3, RegistryValueKind.DWord); // Máximo boost
                
                _logger.LogInfo("[CPU-Scheduler] [OK] Thread quantum otimizado para gaming");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[CPU-Scheduler] Erro ao otimizar quantum: {ex.Message}");
            }
            _logger.LogExit(nameof(OptimizeThreadQuantum));
}
        
        /// <summary>
        /// Otimiza NUMA-aware scheduling
        /// </summary>
        private async Task OptimizeNumaSchedulingAsync()
        {
            _logger.LogEntry(nameof(OptimizeNumaSchedulingAsync));
try
            {
                // Verificar suporte NUMA
                if (!GetNumaHighestNodeNumber(out int highestNode))
                {
                    _logger.LogInfo("[CPU-Scheduler] Sistema não suporta NUMA");
return;
                }
                
                if (highestNode == 0)
                {
                    _logger.LogInfo("[CPU-Scheduler] Sistema single-node (sem NUMA)");
return;
                }
                
                // Configurar processo para usar o NUMA node local
                var currentProcess = GetCurrentProcess();
                var processorCount = Environment.ProcessorCount;
                
                // Encontrar o melhor NUMA node (com mais cores)
                int bestNode = 0;
                ulong maxMask = 0;
                
                for (int node = 0; node <= highestNode; node++)
                {
                    if (GetNumaNodeProcessorMask(node, out ulong mask))
                    {
                        if (mask > maxMask)
                        {
                            maxMask = mask;
                            bestNode = node;
                        }
                    }
                }
                
                // Aplicar affinity para o melhor NUMA node
                var affinityMask = (IntPtr)maxMask;
                SetProcessAffinityMask(currentProcess, affinityMask);
                
                _logger.LogInfo($"[CPU-Scheduler] [OK] NUMA scheduling otimizado (Node {
bestNode})");
}
            catch (Exception ex)
            {
                _logger.LogWarning($"[CPU-Scheduler] Erro ao otimizar NUMA: {ex.Message}");
            }
            _logger.LogExit(nameof(OptimizeNumaSchedulingAsync));
}
        
        /// <summary>
        /// Otimiza foreground boost
        /// </summary>
        private void OptimizeForegroundBoost()
        {
            _logger.LogEntry(nameof(OptimizeForegroundBoost));
try
            {
                using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\PriorityControl");
                
                // Backup
                _originalSeparation = key.GetValue("Win32PrioritySeparation") as int?;
                
                // Configurar foreground boost máximo
                key.SetValue("Win32PrioritySeparation", 0x18, RegistryValueKind.DWord); // Foreground boost máximo
                
                _logger.LogInfo("[CPU-Scheduler] [OK] Foreground boost otimizado");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[CPU-Scheduler] Erro ao configurar foreground boost: {ex.Message}");
            }
            _logger.LogExit(nameof(OptimizeForegroundBoost));
}
        
        /// <summary>
        /// Otimiza processor affinity inteligente
        /// </summary>
        private async Task OptimizeProcessorAffinityAsync()
        {
            _logger.LogEntry(nameof(OptimizeProcessorAffinityAsync));
try
            {
                var currentProcess = Process.GetCurrentProcess();
                var processorCount = Environment.ProcessorCount;
                
                // Para gaming, usar as melhores cores (geralmente as primeiras)
                uint affinityMask = 0;
                
                // Configurar affinity para as melhores metade das cores
                var coresToUse = Math.Max(2, processorCount / 2);
                
                for (int i = 0; i < coresToUse; i++)
                {
                    affinityMask |= (uint)(1 << i);
                }
                
                // Aplicar affinity ao processo principal
                SetProcessAffinityMask(currentProcess.Handle, (IntPtr)affinityMask);
                
                // Aplicar affinity às threads principais
                foreach (ProcessThread thread in currentProcess.Threads)
                {
try
                    {
                        var hThread = OpenThread(THREAD_SET_INFORMATION, false, (uint)thread.Id);
                        if (hThread != IntPtr.Zero)
                        {
                            SetThreadAffinityMask(hThread, (IntPtr)affinityMask);
                            CloseHandle(hThread);
                        }
                    }
                    catch
                    {
                        // Ignorar threads que não podem ser acessados
                    }
}
                
                _logger.LogInfo($"[CPU-Scheduler] [OK] Processor affinity otimizado ({coresToUse} cores)");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[CPU-Scheduler] Erro ao otimizar affinity: {ex.Message}");
            }
            _logger.LogExit(nameof(OptimizeProcessorAffinityAsync));
}
        
        /// <summary>
        /// Restaura configurações originais do scheduler
        /// </summary>
        public async Task<bool> RestoreCpuSchedulerAsync()
        {
            _logger.LogEntry(nameof(RestoreCpuSchedulerAsync));
try
            {
                _logger.LogInfo("[CPU-Scheduler] Restaurando configurações de scheduler...");
                
                // [FIX:UNICO-DONO-DE-ENERGIA] A restauração do quantum de core
                // parking também não grava mais.
                //
                // `_originalQuantumLength` só era preenchido pelo caminho de
                // escrita que acabamos de neutralizar. Como a escrita não
                // acontece mais, o backup também não é capturado — e tentar
                // "restaurar" aqui não faria nada, ou pior: se algum outro
                // caminho no futuro voltasse a gravar, este `if` reativaria
                // silenciosamente um valor capturado de um estado antigo.
                //
                // A restauração de quantum boost e foreground boost (PriorityControl)
                // NÃO é energia de plano: é escalonamento de prioridade de
                // processo, que é um domínio diferente e não passa pelo dono da
                // energia. Esse caminho continua como está, por decisão
                // consciente.
                if (_originalQuantumLength.HasValue)
                {
                    _logger.LogInfo(
                        "[CPU-Scheduler] Restauracao de quantum de core parking ignorada: " +
                        "a gravacao original ja foi neutralizada, entao nao ha valor a devolver.");
                }
                
                // Restaurar quantum boost
                if (_originalQuantumBoost.HasValue)
                {
                    using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\PriorityControl");
                    key.SetValue("Win32PrioritySeparation", _originalQuantumBoost.Value, RegistryValueKind.DWord);
                }
                
                // Restaurar foreground boost
                if (_originalSeparation.HasValue)
                {
                    using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\PriorityControl");
                    key.SetValue("Win32PrioritySeparation", _originalSeparation.Value, RegistryValueKind.DWord);
                }
                
                // Restaurar affinity padrão (todas as cores)
                var currentProcess = GetCurrentProcess();
                var allCoresMask = (IntPtr)((1UL << Environment.ProcessorCount) - 1);
                SetProcessAffinityMask(currentProcess, allCoresMask);
                
                _logger.LogInfo("[CPU-Scheduler] [OK] Configurações de scheduler restauradas");
return true;
            }
            catch (Exception ex)
            {
_logger.LogError("[CPU-Scheduler] Erro ao restaurar scheduler", ex);
                return false;
}
            _logger.LogExit(nameof(RestoreCpuSchedulerAsync));
}
        
        /// <summary>
        /// Obtém métricas do scheduler
        /// </summary>
        public (int ActiveCores, int QuantumLength, bool NumaEnabled, bool HybridCpu) GetSchedulerMetrics()
        {
            try
            {
                var activeCores = Environment.ProcessorCount;
                
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\PriorityControl");
                var quantumLength = key?.GetValue("Win32PrioritySeparation") as int? ?? 0x18;
                
                // Verificar NUMA
                GetNumaHighestNodeNumber(out int highestNode);
                var numaEnabled = highestNode > 0;
                
                // Verificar CPU híbrida (usar valor em cache se disponível, sem bloqueio síncrono do WMI)
                bool hybridCpu = false;
                lock (_hybridLock)
                {
if (_isHybridCpu.HasValue)
                    {
                        hybridCpu = _isHybridCpu.Value;
                    }
                    else
                    {
                        // Iniciar detecção em background para futuras chamadas
                        _ = Task.Run(async () => await DetectHybridCpuAsync());
                    }
}
                
                return (activeCores, quantumLength, numaEnabled, hybridCpu);
            }
            catch
            {
                return (Environment.ProcessorCount, 0x18, false, false);
            }
        }
    }
    
    /// <summary>
    /// Interface para otimização avançada de scheduler
    /// </summary>
    public interface IAdvancedCpuSchedulerService
    {
        Task<bool> OptimizeCpuSchedulerAsync();
        Task<bool> RestoreCpuSchedulerAsync();
        (int ActiveCores, int QuantumLength, bool NumaEnabled, bool HybridCpu) GetSchedulerMetrics();
    }
}
