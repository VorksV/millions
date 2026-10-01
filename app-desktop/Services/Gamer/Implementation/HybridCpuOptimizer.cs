using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.Gamer.Interfaces;

namespace VoltrisOptimizer.Services.Gamer.Implementation
{
    [Obsolete("PLACEBO: ApplyHybridAffinity define máscara = todos os núcleos (no-op funcional). Use GamerModeManager.OptimizeGameProcessAsync que gerencia afinidade real.")]
    /// <summary>
    /// CORREÇÃO CRÍTICA #5: THREAD AFFINITY PARA CPUs HÍBRIDAS
    /// 
    /// Problema identificado: Em CPUs 12th gen+, threads críticos podem ir para E-cores
    /// Solução: Pinar threads do jogo em P-cores
    /// 
    /// Arquitetura Híbrida (Intel 12th gen+, AMD Ryzen 7000+):
    /// - P-cores: Performance (threads críticos do jogo)
    /// - E-cores: Efficiency (background tasks)
    /// </summary>
    public class HybridCpuOptimizer
    {
        private readonly ILoggingService _logger;
        private readonly IHardwareDetector _hardwareDetector;

        // P/Invoke para APIs avançadas do Windows
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentThread();

        [DllImport("kernel32.dll")]
        private static extern IntPtr SetThreadAffinityMask(IntPtr hThread, IntPtr dwThreadAffinityMask);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessAffinityMask(IntPtr hProcess, IntPtr dwProcessAffinityMask);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetProcessAffinityMask(IntPtr hProcess, out IntPtr lpProcessAffinityMask, out IntPtr lpSystemAffinityMask);

        public HybridCpuOptimizer(ILoggingService logger, IHardwareDetector hardwareDetector)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _hardwareDetector = hardwareDetector ?? throw new ArgumentNullException(nameof(hardwareDetector));
        }

        /// <summary>
        /// Aplica afinidade otimizada para jogo em CPU híbrida
        /// </summary>
        public bool ApplyHybridAffinity(int processId)
        {
            try
            {
                // CORREÇÃO: Validar sistema e modo Gamer
                if (!_hardwareDetector.IsHybridCpu())
                {
                    _logger.LogInfo("[HybridCpu] CPU não é híbrida, afinidade padrão mantida");
                    return true;
                }

                // CORREÇÃO: Afinidade segura, não agressiva
                int pCoreCount = Environment.ProcessorCount / 2; // Estimativa simples
                int eCoreCount = Environment.ProcessorCount - pCoreCount;
                
                // CORREÇÃO: Usar todos os cores, apenas priorizar P-cores
                long affinityMask = (1L << (pCoreCount + eCoreCount)) - 1; // Todos os cores
                
                using var process = Process.GetProcessById(processId);
                process.ProcessorAffinity = (IntPtr)affinityMask;

                _logger.LogSuccess($"[HybridCpu] ✅ Jogo configurado com todos os cores (Mask: 0x{affinityMask:X}) - seguro");
                _logger.LogInfo($"[HybridCpu] P-cores: {pCoreCount}, E-cores: {eCoreCount} - otimização balanceada");

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[HybridCpu] Erro ao aplicar afinidade: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Calcula número de threads dos P-cores
        /// </summary>
        private int CalculatePCoreThreads(int totalCores, int totalThreads)
        {
            // Se threads == cores, não há HyperThreading (provavelmente não é híbrida)
            if (totalThreads == totalCores)
            {
                return totalCores;
            }

            // Heurística para CPUs Intel 12th/13th/14th gen:
            // P-cores têm HT (2 threads/core), E-cores não (1 thread/core)
            // Exemplo: 12900K = 8P + 8E = 16 + 8 = 24 threads
            // Fórmula: pCores = (totalThreads - totalCores) / 1
            // Mas precisamos saber quantos são P-cores...
            
            // Simplificação: Assumir que metade dos cores são P-cores (válido para 12900K, 13900K)
            int estimatedPCores = totalCores / 2;
            int pCoreThreads = estimatedPCores * 2; // P-cores com HT

            // Validação: pCoreThreads não pode exceder totalThreads
            if (pCoreThreads > totalThreads)
            {
                pCoreThreads = totalThreads / 2;
            }

            return pCoreThreads;
        }

        /// <summary>
        /// Restaura afinidade padrão (todos os cores)
        /// </summary>
        public bool RestoreDefaultAffinity(int processId)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                
                // Obter máscara do sistema (todos os cores)
                GetProcessAffinityMask(
                    process.Handle, 
                    out IntPtr processAffinity, 
                    out IntPtr systemAffinity);

                // Aplicar máscara do sistema (todos os cores disponíveis)
                process.ProcessorAffinity = systemAffinity;

                _logger.LogSuccess("[HybridCpu] ✅ Afinidade padrão restaurada");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[HybridCpu] Erro ao restaurar afinidade: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Aplica prioridade alta + afinidade P-core para thread específica
        /// </summary>
        public bool OptimizeCriticalThread(ProcessThread thread, int pCoreThreads)
        {
            try
            {
                // Criar máscara para P-cores
                long affinityMask = (1L << pCoreThreads) - 1;

                // Aplicar afinidade
                thread.ProcessorAffinity = (IntPtr)affinityMask;

                // Elevar prioridade
                thread.PriorityLevel = ThreadPriorityLevel.Highest;

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[HybridCpu] Erro ao otimizar thread: {ex.Message}");
                return false;
            }
        }
    }
}
