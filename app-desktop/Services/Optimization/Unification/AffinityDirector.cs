using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using VoltrisOptimizer.Services;
using System.Collections.Generic;

namespace VoltrisOptimizer.Services.Optimization.Unification
{
    public class AffinityDirector
    {
        private readonly ILoggingService _logger;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessAffinityMask(IntPtr hProcess, IntPtr dwProcessAffinityMask);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetLogicalProcessorInformationEx(
            int relationType, 
            IntPtr buffer, 
            ref uint returnedLength);

        // Constantes para topologia real
        private const int RelationProcessorCore = 0;

        public AffinityDirector(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Detecta topologia real via API Win32 profunda. 
        /// Nao usa string.Contains("Intel") para saber se tem E-cores.
        /// Retorna a máscara apenas dos Cores de Alta Performance.
        /// </summary>
        public IntPtr GetRealPerformanceCoresMask()
        {
            try
            {
                uint length = 0;
                // Primeira chamada retorna tamanho necessario
                GetLogicalProcessorInformationEx(RelationProcessorCore, IntPtr.Zero, ref length);
                
                if (length == 0) return (IntPtr)((1L << Environment.ProcessorCount) - 1);

                IntPtr buffer = Marshal.AllocHGlobal((int)length);
                try
                {
                    if (GetLogicalProcessorInformationEx(RelationProcessorCore, buffer, ref length))
                    {
                        long pCoreMask = 0;
                        int offset = 0;
                        
                        while (offset < length)
                        {
                            IntPtr currentPtr = IntPtr.Add(buffer, offset);
                            
                            // Parse struct SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX
                            int structSize = Marshal.ReadInt32(currentPtr, 4); // offset 4 é Size
                            byte efficiencyClass = Marshal.ReadByte(currentPtr, 8); // offset 8 é EfficiencyClass  (Win10+)
                            
                            // P-Cores tem EfficiencyClass mais alta (geralmente 1 ou 2, E-cores sao 0)
                            // Para simplificar no C#, pegamos agrupamentos logicamente validos via Mask struct
                            // offset 16 é apontador para as mascaras de grupo
                            
                            long groupMask = Marshal.ReadInt64(currentPtr, 16); 

                            // Se classe de eficiencia > 0, é P-Core
                            if (efficiencyClass > 0)
                            {
                                pCoreMask |= groupMask;
                            }
                            
                            offset += structSize;
                        }
                        
                        // Se não encontrou estrutura hibrida mapeada, retorna fallback total
                        if (pCoreMask == 0) return (IntPtr)((1L << Environment.ProcessorCount) - 1);
                        
                        return (IntPtr)pCoreMask;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[AffinityDirector] Erro ao ler GetLogicalProcessorInformationEx: {ex.Message}");
            }
            return (IntPtr)((1L << Environment.ProcessorCount) - 1);
        }

        public bool SetAffinity(int processId, IntPtr mask, string reason)
        {
            try
            {
                using var p = Process.GetProcessById(processId);
                if (p.HasExited) return false;

                bool success = SetProcessAffinityMask(p.Handle, mask);
                if (success)
                {
                    _logger.LogInfo($"[AffinityDirector] Afinidade aplicada PID={processId} Mask=0x{mask:X}. Motivo: {reason}");
                }
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[AffinityDirector] Exceção ao aplicar afinidade: {ex.Message}");
                return false;
            }
        }
        
        public bool RestoreAffinity(int processId)
        {
             try
            {
                using var p = Process.GetProcessById(processId);
                if (p.HasExited) return false;
                
                long allCoresMask = (1L << Environment.ProcessorCount) - 1;
                bool success = SetProcessAffinityMask(p.Handle, (IntPtr)allCoresMask);
                return success;
            }
            catch { return false; }
        }
    }
}
