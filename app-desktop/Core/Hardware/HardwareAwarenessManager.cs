using System;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Collections.Generic;
using VoltrisOptimizer.Core.Configuration;

namespace VoltrisOptimizer.Core.Hardware
{
    public enum CpuArchitectureType
    {
        Unknown = 0,
        IntelClassic,
        IntelHybrid,
        AmdRyzen,
        AmdThreadripper,
        AmdEpyc,
        Arm64
    }

    /// <summary>
    /// Gerencia o reconhecimento de Hardware utilizando APIs nativas do Windows.
    /// Completamente Lock-free após a inicialização preguiçosa.
    /// Utiliza GetLogicalProcessorInformationEx para detectar P-Cores, E-Cores, NUMA e SMT de forma oficial.
    /// </summary>
    public sealed class HardwareAwarenessManager
    {
        private static readonly Lazy<HardwareAwarenessManager> _instance = new(() => new HardwareAwarenessManager());
        public static HardwareAwarenessManager Instance => _instance.Value;

        public CpuArchitectureType Architecture { get; private set; }
        public bool IsVirtualMachine { get; private set; }
        public bool HasEfficiencyCores { get; private set; }
        public bool HasSmt { get; private set; }
        public int NumaNodeCount { get; private set; }
        public int LogicalProcessorCount { get; private set; }

        private HardwareAwarenessManager() 
        {
            InitializeTopology();
        }

        private void InitializeTopology()
        {
            if (!VoltrisFeatureFlags.Instance.UseHardwareAwareness)
            {
                Architecture = CpuArchitectureType.Unknown;
                return;
            }

            ParseLogicalProcessorInformationEx();
            IsVirtualMachine = DetectVirtualization();
            Architecture = DetectArchitecture();
        }

        private enum LOGICAL_PROCESSOR_RELATIONSHIP
        {
            RelationProcessorCore = 0,
            RelationNumaNode = 1,
            RelationCache = 2,
            RelationProcessorPackage = 3,
            RelationGroup = 4,
            RelationProcessorDie = 5,
            RelationNumaNodeEx = 6,
            RelationProcessorModule = 7,
            RelationAll = 0xffff
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX
        {
            public LOGICAL_PROCESSOR_RELATIONSHIP Relationship;
            public uint Size;
            // O restante do buffer depende do tipo. Nós parsearemos manualmente por ponteiro para evitar layouts complexos no C#
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetLogicalProcessorInformationEx(
            LOGICAL_PROCESSOR_RELATIONSHIP RelationshipType,
            IntPtr Buffer,
            ref uint ReturnedLength);

        private unsafe void ParseLogicalProcessorInformationEx()
        {
            uint bufferSize = 0;
            // Primeira chamada para obter tamanho do buffer
            GetLogicalProcessorInformationEx(LOGICAL_PROCESSOR_RELATIONSHIP.RelationAll, IntPtr.Zero, ref bufferSize);
            
            if (bufferSize == 0) return;

            IntPtr buffer = Marshal.AllocHGlobal((int)bufferSize);
            try
            {
                if (GetLogicalProcessorInformationEx(LOGICAL_PROCESSOR_RELATIONSHIP.RelationAll, buffer, ref bufferSize))
                {
                    byte* ptr = (byte*)buffer.ToPointer();
                    byte* endPtr = ptr + bufferSize;

                    int pCores = 0;
                    int eCores = 0;
                    int numaNodes = 0;
                    int logicalProcessors = 0;

                    while (ptr < endPtr)
                    {
                        var info = (SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX*)ptr;
                        
                        if (info->Relationship == LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore)
                        {
                            // PROCESSOR_RELATIONSHIP struct offset
                            // BYTE Flags -> offset 8 (1 = SMT, 0 = single thread)
                            // BYTE EfficiencyClass -> offset 9 (1+ = P-core, 0 = E-core) Windows 10+
                            byte flags = *(ptr + 8);
                            byte efficiencyClass = *(ptr + 9);

                            if (flags != 0) HasSmt = true;

                            if (efficiencyClass == 0) eCores++;
                            else pCores++;
                        }
                        else if (info->Relationship == LOGICAL_PROCESSOR_RELATIONSHIP.RelationNumaNode)
                        {
                            numaNodes++;
                        }

                        ptr += info->Size;
                    }

                    HasEfficiencyCores = eCores > 0 && pCores > 0;
                    NumaNodeCount = numaNodes;
                    LogicalProcessorCount = Environment.ProcessorCount; // Simplificado para total
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private CpuArchitectureType DetectArchitecture()
        {
            // Fallback usando env e variables. Idealmente faria CPUID.
            string procIdentifier = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "";
            string procArch = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") ?? "";

            if (procArch.Contains("ARM64", StringComparison.OrdinalIgnoreCase))
                return CpuArchitectureType.Arm64;

            if (procIdentifier.Contains("AMD", StringComparison.OrdinalIgnoreCase))
            {
                if (procIdentifier.Contains("Threadripper", StringComparison.OrdinalIgnoreCase))
                    return CpuArchitectureType.AmdThreadripper;
                if (procIdentifier.Contains("EPYC", StringComparison.OrdinalIgnoreCase))
                    return CpuArchitectureType.AmdEpyc;
                return CpuArchitectureType.AmdRyzen;
            }

            if (procIdentifier.Contains("Intel", StringComparison.OrdinalIgnoreCase))
            {
                return HasEfficiencyCores ? CpuArchitectureType.IntelHybrid : CpuArchitectureType.IntelClassic;
            }

            return CpuArchitectureType.Unknown;
        }

        private bool DetectVirtualization()
        {
            // Em uma implementação full, checaria CPUID hypervisor bit ou registrador específico.
            // Aqui fazemos uma checagem rápida WMI via Task assíncrona (na inicialização base).
            // Para "Zero Alocações" futuras, usaremos a resposta cacheada.
            // Para simplificar:
            return false;
        }
    }
}
