using System;
using System.Collections.Generic;
using System.Diagnostics;
using VoltrisOptimizer.Core.Native;

namespace VoltrisOptimizer.Services.Optimization
{
    /// <summary>
    /// Servico de Hardening DSL 5.x para deteccao de dependencias de threads (Deadlock Prevention).
    /// </summary>
    public class WaitChainService : IDisposable
    {
        private readonly IntPtr _wctHandle;
        private readonly ILoggingService _logger;
        private long _callCount;

        public WaitChainService(ILoggingService logger)
        {
            _logger = logger;
            _wctHandle = HardeningNativeMethods.OpenThreadWaitChainSession(0, IntPtr.Zero);
            if (_wctHandle == IntPtr.Zero)
            {
                _logger.LogWarning("[WaitChain] Falha ao abrir sessao WCT. Deteccao de deadlock desativada.");
            }
        }

        /// <summary>
        /// Verifica se o PID alvo esta bloqueando o PID de primeiro plano.
        /// </summary>
        public bool IsProcessBlockingForeground(int targetPid, int foregroundPid)
        {
            if (_wctHandle == IntPtr.Zero || foregroundPid <= 0 || targetPid <= 0) return false;

            try
            {
                // Breadcrumb na primeira chamada e a cada 200 chamadas
                var callNum = System.Threading.Interlocked.Increment(ref _callCount);
                if (callNum <= 3 || callNum % 200 == 0)
                {
                    Core.Diagnostics.CrashDiagnostics.Mark(
                        $"WCT IsBlocking #{callNum} target={targetPid} fg={foregroundPid}");
                }

                // Obter todas as threads do processo em primeiro plano
                Process fgProcess;
                try { fgProcess = Process.GetProcessById(foregroundPid); }
                catch { return false; }

                if (fgProcess.HasExited) return false;

                // Copiar IDs das threads para evitar problemas de enumeração se o processo sair
                var threadIds = new List<int>();
                try
                {
                    foreach (ProcessThread thread in fgProcess.Threads)
                        threadIds.Add(thread.Id);
                }
                catch { return false; }

                foreach (var tid in threadIds)
                {
                    try
                    {
                        if (IsThreadBlockedByProcess((uint)tid, (uint)targetPid))
                            return true;
                    }
                    catch { }
                }
            }
            catch { }

            return false;
        }

        private long _wctNativeCallCount;

        private bool IsThreadBlockedByProcess(uint threadId, uint targetPid)
        {
            try
            {
                var nodes = new HardeningNativeMethods.WAITC_CHAIN_NODE_INFO[16];
                uint nodeCount = (uint)nodes.Length;

                var nativeCall = System.Threading.Interlocked.Increment(ref _wctNativeCallCount);
                if (nativeCall <= 5 || nativeCall % 500 == 0)
                {
                    Core.Diagnostics.CrashDiagnostics.Mark(
                        $"WCT native #{nativeCall} tid={threadId} target={targetPid}");
                }

                if (HardeningNativeMethods.GetThreadWaitChain(_wctHandle, IntPtr.Zero, 0, threadId, ref nodeCount, nodes, out bool isCycle))
                {
                    for (int i = 0; i < Math.Min(nodeCount, (uint)nodes.Length); i++)
                    {
                        // Para nós do tipo Thread, verificar se o ProcessId é o alvo
                        if (nodes[i].ObjectType == HardeningNativeMethods.WCT_OBJECT_TYPE.WctThreadType
                            && nodes[i].ProcessId == targetPid)
                        {
                            return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        public void Dispose()
        {
            if (_wctHandle != IntPtr.Zero)
            {
                HardeningNativeMethods.CloseThreadWaitChainSession(_wctHandle);
            }
        }
    }
}
