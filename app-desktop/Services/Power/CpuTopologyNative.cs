using System;
using System.Runtime.InteropServices;

namespace VoltrisOptimizer.Services.Power
{
    /// <summary>
    /// [FIX:POWER-TIER] Interop para detectar CPU híbrida.
    ///
    /// POR QUE ISSO EXISTE
    /// ==================
    /// "Esta CPU é híbrida?" (P-core + E-core) não tem resposta confiável pelo
    /// nome do processador. A resposta DOCUMENTADA é o campo `EfficiencyClass` de
    /// cada núcleo físico, exposto por `GetLogicalProcessorInformationEx` com
    /// `RelationProcessorCore` — é a mesma informação que o Windows usa no
    /// Thread Director. Valores iguais = CPU uniforme; valores diferentes = híbrida.
    ///
    /// É forward-compatible: uma CPU futura com 3 classes de eficiência aparece
    /// naturalmente como "híbrida com 3 classes", sem novo código.
    ///
    /// Tudo aqui é P/Invoke de API documentada do Windows. Se a chamada falhar em
    /// alguma máquina, `HardwareCapabilityProbe` devolve "não é híbrida" e o app
    /// segue na configuração conservadora — falhar aqui nunca custa desempenho.
    /// </summary>
    internal static class CpuTopologyNative
    {
        private const int ERROR_INSUFFICIENT_BUFFER = 122;

        [Flags]
        internal enum LogProcRel : int
        {
            CpuInfoTypeRelationProcessorCore = 0,
            CpuInfoTypeRelationNumaNode = 1,
            CpuInfoTypeRelationCache = 2,
            CpuInfoTypeRelationProcessorPackage = 3,
            CpuInfoTypeRelationGroup = 4,
            CpuInfoTypeRelationAll = 0xFFFF
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct SystemInfoUnion
        {
            public uint OemId;
            public uint PageSize;
            public IntPtr lpMinimumApplicationAddress;
            public IntPtr lpMaximumApplicationAddress;
            public IntPtr dwActiveProcessorMask;
            public uint dwNumberOfProcessors;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct CacheDescriptor
        {
            public byte Level;
            public byte Associativity;
            public ushort LineSize;
            public uint Size;
            public uint Group;
        }

        /// <summary>
        /// [FIX:STRUCT-ALIGNMENT] O LAÇO EM CIMA DOS MEMBROS.
        ///
        /// Este struct é usado com `LayoutKind.Explicit` e `FieldOffset(0)`,
        /// o que proíbe o runtime de aplicar o alinhamento automático dos
        /// campos de objeto. Um array gerenciado (`byte[] Reserved`) dentro de
        /// um struct com layout explícito viola essa regra, e o CLR recusa
        /// carregar o tipo inteiro.
        ///
        /// O sintoma era registrado no log do usuário como falha na detecção de
        /// CPU híbrida:
        ///
        ///     Could not load type 'LogProcCpuInfoUnion' ... because it contains
        ///     an object field at offset 0 that is incorrectly aligned or
        ///     overlapped by a non-object field.
        ///
        /// O efeito prático era o app cair no fallback e declarar `hibrido=nao`
        /// numa CPU que é híbrida — bem no campo que decide como escalonar
        /// núcleos de desempenho e de eficiência.
        ///
        /// A correção é substituir o array gerenciado por campos de byte
        /// individuais. Um array (`byte[]`) é um objeto no CLR e um layout
        /// explícito proíbe objeto em `FieldOffset(0)`; já um `fixed byte[]`
        /// exigiria contexto `unsafe` no tipo inteiro. Vinte campos `byte`
        /// ocupam exatamente os mesmos 20 bytes, são dispostos de forma
        /// determinística e não exigem nada além do layout sequencial — que é o
        /// que o SO descreve.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct ProcessorCoreInfo
        {
            public byte Flags;
            public byte EfficiencyClass;
            public byte R0;  public byte R1;  public byte R2;  public byte R3;  public byte R4;
            public byte R5;  public byte R6;  public byte R7;  public byte R8;  public byte R9;
            public byte R10; public byte R11; public byte R12; public byte R13; public byte R14;
            public byte R15; public byte R16; public byte R17; public byte R18; public byte R19;

            /// <summary>Em KB. Precisa ser o primeiro campo de 32 bits do layout real.</summary>
            public uint Size;
        }

        [StructLayout(LayoutKind.Explicit)]
        internal struct LogProcCpuInfoUnion
        {
            [FieldOffset(0)] public ProcessorCoreInfo ProcessorCore;
            [FieldOffset(0)] public CacheDescriptor Cache;
            [FieldOffset(0)] public SystemInfoUnion System;
        }

        /// <summary>
        /// [FIX:STRUCT-ALIGNMENT] O MESMO PROBLEMA, NO STRUCT DE TRANSIÇÃO.
        ///
        /// Este é o struct que envolve a union, e ele tinha exatamente o mesmo
        /// defeito: um `byte[]` gerenciado dentro de um tipo usado com layout
        /// explícito. O CLR recusava carregar `LogProcCpuInfoUnion` — e, por
        /// consequence, esta função inteira — com o erro de alinhamento que
        /// aparecia no log do usuário.
        ///
        /// Vinte e um bytes de reserva viram campos `byte` individuais, que
        /// ocupam o mesmo espaço e não impõem restrição nenhuma ao layout.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct LogProcCpuInfo
        {
            public LogProcRel Relationship;
            public byte ReservedByte;
            public byte Q0;  public byte Q1;  public byte Q2;  public byte Q3;  public byte Q4;
            public byte Q5;  public byte Q6;  public byte Q7;  public byte Q8;  public byte Q9;
            public byte Q10; public byte Q11; public byte Q12; public byte Q13; public byte Q14;
            public byte Q15; public byte Q16; public byte Q17; public byte Q18;
            public LogProcCpuInfoUnion Union;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetLogicalProcessorInformationEx(
            LogProcRel relationship,
            IntPtr buffer,
            ref int returnedLength);

        /// <summary>
        /// Lê as EfficiencyClass de todos os núcleos físicos.
        /// Devolve (false, 0) se a API não estiver disponível — sem exceção.
        /// </summary>
        internal static (bool isHybrid, int distinctClasses) DetectHybridCpu()
        {
            IntPtr buffer = IntPtr.Zero;
            try
            {
                int size = 0;
                GetLogicalProcessorInformationEx(LogProcRel.CpuInfoTypeRelationProcessorCore, IntPtr.Zero, ref size);

                if (size <= 0) return (false, 0);

                buffer = Marshal.AllocHGlobal(size);
                if (!GetLogicalProcessorInformationEx(LogProcRel.CpuInfoTypeRelationProcessorCore, buffer, ref size))
                {
                    return (false, 0);
                }

                var classes = new System.Collections.Generic.HashSet<byte>();
                int offset = 0;
                int headerSize = Marshal.SizeOf<LogProcCpuInfo>();

                while (offset + headerSize <= size)
                {
                    var info = Marshal.PtrToStructure<LogProcCpuInfo>(buffer + offset);
                    if (info.Relationship != LogProcRel.CpuInfoTypeRelationProcessorCore) break;

                    classes.Add(info.Union.ProcessorCore.EfficiencyClass);

                    int structSize = (int)info.Union.ProcessorCore.Size;
                    if (structSize <= 0) break;
                    offset += structSize;
                }

                return (classes.Count > 1, classes.Count);
            }
            catch
            {
                return (false, 0);
            }
            finally
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            }
        }
    }
}
