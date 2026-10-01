using System;
using System.Diagnostics;
using VoltrisOptimizer.Services.Gamer.OptimizationModules;
using VoltrisOptimizer.Services.Gamer.Intelligence.Implementation;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer
{
    public class PriorityCacheService : IDisposable
    {
        private readonly ILoggingService _logger;
        private readonly RollbackRegistry _rollbackRegistry;
        private int _gameProcessId;
        private readonly object _lock = new();

        public PriorityCacheService(ILoggingService logger, RollbackRegistry rollbackRegistry)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _rollbackRegistry = rollbackRegistry ?? throw new ArgumentNullException(nameof(rollbackRegistry));
            _logger.LogEntry(nameof(PriorityCacheService));
            _logger.LogExit(nameof(PriorityCacheService));
        }

        public void Start(int gameProcessId)
        {
            _logger.LogEntry(nameof(Start));
            lock (_lock)
            {
                _gameProcessId = gameProcessId;
                ApplyOptimizations();
            }
            _logger.LogExit(nameof(Start));
        }

        public void Stop()
        {
            _logger.LogEntry(nameof(Stop));
            lock (_lock)
            {
                RestoreAllPriorities();
            }
            _logger.LogExit(nameof(Stop));
        }

        private void ApplyOptimizations()
        {
            _logger.LogEntry(nameof(ApplyOptimizations));
            try
            {
                using var process = Process.GetProcessById(_gameProcessId);
                
                if (process != null && !process.HasExited)
                {
                    // ═══════════════════════════════════════════════════════════════
                    // ✅ ANTI-CHEAT CHECK: PULAR se o jogo usa BattlEye/EAC/Vanguard
                    // ═══════════════════════════════════════════════════════════════
                    var antiCheat = AntiCheatCompatibilityService.Instance.GetAntiCheatForGame(process.ProcessName);
                    if (!string.IsNullOrEmpty(antiCheat))
                    {
                        _logger.LogInfo($"[PriorityCache] Anti-cheat '{antiCheat}' detectado para '{process.ProcessName}' — pulando otimizações de processo para evitar detecção.");
                        _logger.LogExit(nameof(ApplyOptimizations));
                        return;
                    }

                    try 
                    {
                        // 1. Process Priority
                        _rollbackRegistry.RegisterProcessPriority(_gameProcessId, process.PriorityClass);
                        process.PriorityClass = ProcessPriorityClass.High;

                        // 2. IO Priority (Normal é 2, High é 3)
                        _rollbackRegistry.RegisterProcessIoPriority(_gameProcessId, 2);
                        NativeProcessOptimizer.SetIoPriority(process.Handle, 3);


                    // 3. Page Priority (5 = Normal/High)
                    NativeProcessOptimizer.SetPagePriority(process.Handle, 5);

                    // 4. Core 0 Avoidance (Apenas se houver mais de 4 núcleos)
                    // DESABILITADO: Core 0 Avoidance pode causar issues de scheduling em alguns sistemas
                    // if (Environment.ProcessorCount > 4)
                    // {
                    //     try
                    //     {
                    //         var originalAffinity = process.ProcessorAffinity;
                    //         _rollbackRegistry.RegisterProcessAffinity(_gameProcessId, originalAffinity);
                    //
                    //         // Remove o bit 0 (Core 0)
                    //         long affinityMask = originalAffinity.ToInt64();
                    //         affinityMask &= ~1L; // Bitwise NOT 1
                    //
                    //         if (affinityMask > 0)
                    //         {
                    //             process.ProcessorAffinity = new IntPtr(affinityMask);
                    //             _logger.LogInfo($"[PriorityCache] Core 0 Avoidance aplicado.");
                    //         }
                    //     }
                    //     catch (Exception ex)
                    //     {
                    //         _logger.LogWarning($"[PriorityCache] Falha ao aplicar Core 0 Avoidance: {ex.Message}");
                    //     }
                    // }

                    }
                    catch (InvalidOperationException)
                    {
                        // Processo encerrou abruptamente, ignorar
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[PriorityCache] Erro ao priorizar: {ex.Message}");
            }
            _logger.LogExit(nameof(ApplyOptimizations));
        }

        public void RestoreAllPriorities()
        {
            _logger.LogEntry(nameof(RestoreAllPriorities));
            try
            {
                var snapshots = _rollbackRegistry.GetProcessSnapshots();
                foreach (var snap in snapshots)
                {
                    if (snap.ProcessId == _gameProcessId)
                    {
                        try
                        {
                            using var process = Process.GetProcessById(snap.ProcessId);
                            
                            // Restaura prioridade
                            if (process.PriorityClass != snap.OriginalPriority)
                            {
                                process.PriorityClass = snap.OriginalPriority;
                            }

                            // Restaura Afinidade (Core 0)
                            if (snap.OriginalAffinity.HasValue && process.ProcessorAffinity != snap.OriginalAffinity.Value)
                            {
                                process.ProcessorAffinity = snap.OriginalAffinity.Value;
                            }

                            // Restaura IO
                            NativeProcessOptimizer.SetIoPriority(process.Handle, (uint)snap.OriginalIoPriority);
                            _logger.LogInfo($"[PriorityCache] Prioridades de {_gameProcessId} restauradas.");
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[PriorityCache] Erro ao restaurar prioridades: {ex.Message}");
            }
            _logger.LogExit(nameof(RestoreAllPriorities));
        }

        public void Dispose()
        {
            _logger.LogEntry(nameof(Dispose));
            Stop();
            _logger.LogExit(nameof(Dispose));
        }
    }
}
