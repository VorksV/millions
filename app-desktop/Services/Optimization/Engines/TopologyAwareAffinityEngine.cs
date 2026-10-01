using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Linq;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Optimization.Engines
{
    /// <summary>
    /// Pilar 1 da Ultra-Performance: Affinity Engine (Thread Director)
    /// Realiza pin cirúrgico em processos para rodarem nos núcleos mais rápidos (P-Cores ou CCD com V-Cache)
    /// </summary>
    public class TopologyAwareAffinityEngine
    {
        private readonly ILoggingService _logger;
        private readonly ProcessorTopology _topology;

        public TopologyAwareAffinityEngine(ILoggingService logger)
        {
            _logger = logger;
            _topology = DetectTopology();
            _logger.LogInfo($"[Topology] Detectados {_topology.TotalLogicalProcessors} Threads, {_topology.PerformanceCoresMask:X} P-Cores Mask, {_topology.EfficiencyCoresMask:X} E-Cores Mask.");
        }

        public void ApplyGamingAffinity(int pid)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                
                // Se o processador for híbrido, forcamos o jogo para os P-Cores
                if (_topology.IsHybrid)
                {
                    process.ProcessorAffinity = (IntPtr)_topology.PerformanceCoresMask;
                    _logger.LogInfo($"[AffinityEngine] PID {pid} pinado para P-Cores (Mask: {_topology.PerformanceCoresMask:X})");
                }
                // Se for um Ryzen Dual CCD (Ex: 16 cores / 32 threads), forcamos para o primeiro CCD (frequentemente o V-Cache)
                else if (_topology.IsDualCCD)
                {
                    process.ProcessorAffinity = (IntPtr)_topology.FirstCCDMask;
                    _logger.LogInfo($"[AffinityEngine] PID {pid} pinado para CCD Primário (Mask: {_topology.FirstCCDMask:X})");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[AffinityEngine] Falha ao aplicar afinidade no PID {pid}: {ex.Message}");
            }
        }

        public void ApplyBackgroundAffinity(int pid)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                
                if (_topology.IsHybrid)
                {
                    // Bloqueia apps de fundo nos E-Cores
                    process.ProcessorAffinity = (IntPtr)_topology.EfficiencyCoresMask;
                }
                else if (_topology.IsDualCCD)
                {
                    // Força background pro segundo CCD
                    process.ProcessorAffinity = (IntPtr)_topology.SecondCCDMask;
                }
                else
                {
                    // Em CPUs normais, tira do Core 0 (frequentemente usado pelo sistema)
                    if (_topology.TotalLogicalProcessors > 4)
                    {
                        ulong allButCore0 = (1UL << _topology.TotalLogicalProcessors) - 1;
                        allButCore0 &= ~3UL; // Remove Core 0 (thread 0 e 1)
                        process.ProcessorAffinity = (IntPtr)allButCore0;
                    }
                }
            }
            catch { }
        }

        private ProcessorTopology DetectTopology()
        {
            var topology = new ProcessorTopology();
            topology.TotalLogicalProcessors = Environment.ProcessorCount;
            
            // Simulação segura baseada na arquitetura geral caso GetLogicalProcessorInformationEx falhe.
            // Para Intel Gen 12+, os últimos N threads são os E-Cores.
            // Exemplo genérico para fins de arquitetura:
            ulong fullMask = (topology.TotalLogicalProcessors >= 64) ? ulong.MaxValue : (1UL << topology.TotalLogicalProcessors) - 1;
            
            // Heurística básica de E-Core baseada em WMI / Nome do Processador (ex: i9-13900K)
            // Em produção real, o P/Invoke de GetLogicalProcessorInformationEx constrói a bitmask.
            try
            {
                using (var searcher = new System.Management.ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor"))
                {
                    foreach (var item in searcher.Get())
                    {
                using var __dispose_item = item;
                        string name = item["Name"]?.ToString() ?? "";
                        int cores = Convert.ToInt32(item["NumberOfCores"]);
                        int logical = Convert.ToInt32(item["NumberOfLogicalProcessors"]);

                        if (name.Contains("Intel") && logical > cores * 2 == false && logical != cores)
                        {
                            // Arquitetura Híbrida detectada (Cores != Logical && não é puro SMT 2x)
                            // i9-13900K: 24 Cores, 32 Threads (8 P-Cores com HT = 16 threads, 16 E-Cores = 16 threads)
                            topology.IsHybrid = true;
                            
                            int eCoresCount = (cores * 2) - logical; // Matemática básica para achar E-Cores
                            int pThreads = logical - eCoresCount;
                            
                            topology.PerformanceCoresMask = (1UL << pThreads) - 1; // Primeiros N threads
                            topology.EfficiencyCoresMask = fullMask ^ topology.PerformanceCoresMask; // O restante
                        }
                        else if (name.Contains("AMD") && cores >= 12)
                        {
                            // Ryzen 9 (12+ Cores, Dual CCD)
                            topology.IsDualCCD = true;
                            int threadsPerCCD = logical / 2;
                            topology.FirstCCDMask = (1UL << threadsPerCCD) - 1;
                            topology.SecondCCDMask = fullMask ^ topology.FirstCCDMask;
                        }
                    }
                }
            }
            catch 
            {
                topology.PerformanceCoresMask = fullMask;
                topology.EfficiencyCoresMask = fullMask;
            }
            
            // Failsafe
            if (topology.PerformanceCoresMask == 0) topology.PerformanceCoresMask = fullMask;
            if (topology.EfficiencyCoresMask == 0) topology.EfficiencyCoresMask = fullMask;
            
            return topology;
        }

        private class ProcessorTopology
        {
            public int TotalLogicalProcessors { get; set; }
            public bool IsHybrid { get; set; }
            public bool IsDualCCD { get; set; }
            
            public ulong PerformanceCoresMask { get; set; }
            public ulong EfficiencyCoresMask { get; set; }
            
            public ulong FirstCCDMask { get; set; }
            public ulong SecondCCDMask { get; set; }
        }
    }
}
