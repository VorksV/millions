using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using VoltrisOptimizer.Utils;

namespace VoltrisOptimizer.Services.Performance
{
    /// <summary>
    /// MemoryReclaimService — recuperação de memória que produz ganho REAL e
    /// mensurável em "Memória disponível".
    /// <para>
    /// POR QUE ESTE SERVIÇO EXISTE
    /// </para>
    /// <para>
    /// O projeto já chamava <c>EmptyWorkingSet</c> e
    /// <c>MemoryPurgeStandbyList</c>, mas essas chamadas não habilitavam os
    /// privilégios exigidos, então falhavam silenciosamente. Além disso, as duas
    /// áreas que de fato devolvem memória de forma rápida e permanente —
    /// <b>Modified Page List</b> e <b>System File Cache</b> — não existiam
    /// aqui. Este serviço as implementa.
    /// </para>
    /// <para>
    /// DIFERENÇA IMPORTANTE EM RELAÇÃO AO <c>EmptyWorkingSet</c>
    /// </para>
    /// <para>
    /// <c>EmptyWorkingSet</c> em todos os processos tem efeito cosmético: o
    /// Windows recarrega as páginas na próxima falta de página, e com carga real
    /// o sistema retorna ao estado anterior em segundos — podendo ainda provocar
    /// uma rajada de page faults que piora a responsividade. Por isso este
    /// serviço <b>não</b> varre processos. Trabalha nas três áreas de memória
    /// do kernel cuja purga é real e durável.
    /// </para>
    /// <para>
    /// TÉCNICA E CONSTANTES
    /// </para>
    /// <para>
    /// Implementação equivalente à do WinMemoryCleaner
    /// (<c>ComputerService.OptimizeModifiedPageList</c> linha 579 e
    /// <c>OptimizeSystemFileCache</c> linha 662), com os valores de
    /// <c>Constants.cs</c>:
    /// <c>SystemInformationClass.SystemMemoryListInformation = 80</c>,
    /// <c>SystemInformationClass.SystemFileCacheInformation = 21</c>,
    /// <c>MemoryFlushModifiedList = 3</c>,
    /// <c>PrivilegeAttribute.Enabled = 2</c>.
    /// </para>
    /// </summary>
    public sealed class MemoryReclaimService
    {
        // ── Constantes (valores verificados em WinMemoryCleaner Constants.cs) ──
        private const int ErrorSuccess = 0;
        private const int SystemMemoryListInformation = 80;
        private const int SystemFileCacheInformation = 21;
        private const int MemoryFlushModifiedList = 3;

        private readonly ILoggingService? _logger;

        public MemoryReclaimService(ILoggingService? logger = null)
        {
            _logger = logger;
        }

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtSetSystemInformation(
            int systemInformationClass, IntPtr systemInformation, uint systemInformationLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetSystemFileCacheSize(
            IntPtr minimumFileCacheSize, IntPtr maximumFileCacheSize, int flags);

        /// <summary>
        /// SYSTEM_FILE_CACHE_INFORMATION.
        /// <para>
        /// O layout muda conforme a bitagem do PROCESSO: em 64 bits todos os
        /// campos são <c>SIZE_T</c> (8 bytes); em 32 bits são <c>ULONG</c>
        /// (4 bytes). Por isso existem duas structs separadas — usar a errada
        /// produz tamanho desalinhado e a chamada é rejeitada.
        /// <para>
        /// <c>Pack = 1</c> é obrigatório: sem ele o CLR insere padding e o
        /// tamanho não bate com o esperado pelo kernel.
        /// </para>
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct SystemFileCacheInformation64
        {
            public long CurrentSize;
            public long PeakSize;
            public long PageFaultCount;
            public long MinimumWorkingSet;
            public long MaximumWorkingSet;
            public long CurrentSizeIncludingTransitionInPages;
            public long PeakSizeIncludingTransitionInPages;
            public long TransitionRePurposeCount;
            public long PageFaultCountTime;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct SystemFileCacheInformation32
        {
            public int CurrentSize;
            public int PeakSize;
            public int PageFaultCount;
            public int MinimumWorkingSet;
            public int MaximumWorkingSet;
            public int CurrentSizeIncludingTransitionInPages;
            public int PeakSizeIncludingTransitionInPages;
            public int TransitionRePurposeCount;
            public int PageFaultCountTime;
        }

        /// <summary>
        /// Purgar a <b>Modified Page List</b> — páginas modificadas que já foram
        /// gravadas em disco mas ainda ocupam RAM. É memória genuinamente
        /// recuperável, e o ganho aparece de imediato em "Disponível".
        /// </summary>
        public async Task<MemoryReclaimResult> ReclaimModifiedPageListAsync()
        {
            var result = new MemoryReclaimResult { Area = "Modified Page List" };

            return await Task.Run(() =>
            {
                if (!Utils.PrivilegeHelper.EnablePrivilege(Utils.PrivilegeHelper.SE_PROFILE_SINGLE_PROCESS_NAME))
                {
                    result.Skipped = "SeProfileSingleProcessPrivilege não disponível";
                    _logger?.LogWarning($"[MEM-RECLAIM] Modified Page List PULADO: {result.Skipped}");
                    return result;
                }

                // Aloca-se um int fixo e passa-se o endereço pinado. O parâmetro
                // da chamada é a INFORMAÇÃO (o comando 3), não um ponteiro.
                int command = MemoryFlushModifiedList;
                var handle = GCHandle.Alloc(command, GCHandleType.Pinned);
                try
                {
                    int status = NtSetSystemInformation(
                        SystemMemoryListInformation,
                        handle.AddrOfPinnedObject(),
                        (uint)Marshal.SizeOf(command));

                    if (status != ErrorSuccess)
                    {
                        result.Skipped = $"NTSTATUS 0x{status:X8}";
                        _logger?.LogWarning($"[MEM-RECLAIM] Modified Page List falhou: {result.Skipped}");
                        return result;
                    }

                    result.Ok = true;
                    _logger?.LogInfo("[MEM-RECLAIM] Modified Page List purgada com sucesso.");
                    return result;
                }
                catch (Exception ex)
                {
                    result.Skipped = ex.Message;
                    _logger?.LogWarning($"[MEM-RECLAIM] Modified Page List erro: {ex.Message}");
                    return result;
                }
                finally
                {
                    if (handle.IsAllocated) handle.Free();
                }
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Reduz o <b>System File Cache</b> ao mínimo, forçando o Windows a
        /// descartar as páginas de arquivo em cache. É a maior fonte de
        /// "ocupada mas descartável" em um Windows ocioso.
        /// <para>
        /// Duas etapas, como no original: primeiro o
        /// <c>NtSetSystemInformation(SystemFileCacheInformation)</c> com
        /// Min/Max = -1, e depois o <c>SetSystemFileCacheSize(-1, -1, 0)</c>,
        /// que é quem efetivamente provoca o flush.
        /// </para>
        /// </summary>
        public async Task<MemoryReclaimResult> ReclaimSystemFileCacheAsync()
        {
            var result = new MemoryReclaimResult { Area = "System File Cache" };

            return await Task.Run(() =>
            {
                if (!Utils.PrivilegeHelper.EnablePrivilege(Utils.PrivilegeHelper.SE_INCREASE_QUOTA_NAME))
                {
                    result.Skipped = "SeIncreaseQuotaPrivilege não disponível";
                    _logger?.LogWarning($"[MEM-RECLAIM] System File Cache PULADO: {result.Skipped}");
                    return result;
                }

                // 64 bits: Min/Max = -1 (SIZE_T). 32 bits: int.MaxValue (ULONG).
                object info = IntPtr.Size == 8
                    ? new SystemFileCacheInformation64 { MinimumWorkingSet = -1L, MaximumWorkingSet = -1L }
                    : (object)new SystemFileCacheInformation32 { MinimumWorkingSet = int.MaxValue, MaximumWorkingSet = int.MaxValue };

                int size = Marshal.SizeOf(info);
                var handle = GCHandle.Alloc(info, GCHandleType.Pinned);
                try
                {
                    int status = NtSetSystemInformation(
                        SystemFileCacheInformation,
                        handle.AddrOfPinnedObject(),
                        (uint)size);

                    if (status != ErrorSuccess)
                    {
                        result.Skipped = $"NTSTATUS 0x{status:X8}";
                        _logger?.LogWarning($"[MEM-RECLAIM] SetSystemFileCacheInformation falhou: {result.Skipped}");
                        return result;
                    }
                }
                finally
                {
                    if (handle.IsAllocated) handle.Free();
                }

                // O passo que realmente faz o flush.
                var flush = new IntPtr(-1);
                if (!SetSystemFileCacheSize(flush, flush, 0))
                {
                    int err = Marshal.GetLastWin32Error();
                    result.Skipped = new Win32Exception(err).Message;
                    _logger?.LogWarning($"[MEM-RECLAIM] SetSystemFileCacheSize falhou: {result.Skipped}");
                    return result;
                }

                result.Ok = true;
                _logger?.LogInfo("[MEM-RECLAIM] System File Cache reduzido e liberado com sucesso.");
                return result;
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Resultado da última execução de <see cref="ReclaimAllAsync"/>.
        /// <para>
        /// Exposto como "última execução" em vez de retorno assíncrono porque
        /// <c>RunQuickOptimizeCoreAsync</c> é compartilhado pelo botão de
        /// otimização rápida e pela etapa 4 do botão circular; mudar a assinatura
        /// dele apenas para carregar um número não vale o risco. Quem precisar
        /// exibir o ganho lê este campo logo após chamar a otimização.
        /// </para>
        /// </summary>
        public static MemoryReclaimSummary? LastRun { get; private set; }

        /// <summary>
        /// Executa as duas áreas de memória com ganho real, medindo o antes/depois.
        /// </summary>
        public async Task<MemoryReclaimSummary> ReclaimAllAsync()
        {
            var summary = new MemoryReclaimSummary
            {
                AvailableBeforeMb = ReadAvailableMemoryMb()
            };

            summary.ModifiedPageList = await ReclaimModifiedPageListAsync().ConfigureAwait(false);
            summary.SystemFileCache = await ReclaimSystemFileCacheAsync().ConfigureAwait(false);

            // Pequena pausa para o gerenciador republished os contadores.
            await Task.Delay(250).ConfigureAwait(false);
            summary.AvailableAfterMb = ReadAvailableMemoryMb();
            summary.FreedMb = Math.Max(0, summary.AvailableAfterMb - summary.AvailableBeforeMb);
            LastRun = summary;

            _logger?.LogInfo(
                $"[MEM-RECLAIM] Concluído: ModifiedPageList={summary.ModifiedPageList.Ok}, " +
                $"FileCache={summary.SystemFileCache.Ok}, disponível {summary.AvailableBeforeMb:N0} MB -> " +
                $"{summary.AvailableAfterMb:N0} MB (+{summary.FreedMb:N0} MB)");

            return summary;
        }

        private static double ReadAvailableMemoryMb()
        {
            try
            {
                var info = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(info))
                {
                    return info.ullAvailPhys / 1024.0 / 1024.0;
                }
            }
            catch
            {
                // leitura é apenas para relatório; falha aqui não invalida a reclaim
            }
            return 0;
        }

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

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);
    }

    public class MemoryReclaimResult
    {
        public string Area { get; set; } = "";
        public bool Ok { get; set; }
        public string Skipped { get; set; } = "";
    }

    public class MemoryReclaimSummary
    {
        public double AvailableBeforeMb { get; set; }
        public double AvailableAfterMb { get; set; }
        public double FreedMb { get; set; }
        public MemoryReclaimResult ModifiedPageList { get; set; } = new();
        public MemoryReclaimResult SystemFileCache { get; set; } = new();
    }
}
