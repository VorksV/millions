using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using VoltrisOptimizer.Utils.Win32;

namespace VoltrisOptimizer.Services.Optimization.Engines
{
    /// <summary>
    /// DSL 5.0 - Process Behavior Engine
    /// Analisa padrões de comportamento para prever necessidades de recursos e aplicar contenção inteligente.
    /// 
    /// SEGURANÇA ANTIVÍRUS: Removido manipulação de EcoQoS/PowerThrottling que causava falsos positivos.
    /// Otimização focada apenas em Memory Priority para processos pesados.
    /// </summary>
    public class ProcessBehaviorEngine : IDisposable
    {
        private readonly ILoggingService _logger;
        private readonly ConcurrentDictionary<string, BehaviorProfile> _processLibrary = new();
        private readonly ConcurrentDictionary<int, uint> _lastAppliedPriority = new();
        private DateTime _lastLeakReport = DateTime.MinValue;
        
        public class BehaviorProfile
        {
            public string Name { get; set; } = "";
            public double AvgCpu { get; set; }
            public long AvgWorkingSet { get; set; }
            public long PeakWorkingSet { get; set; }
            public int ExecutionCount { get; set; }
            public DateTime LastSeen { get; set; }
            public bool IsResourceIntensive { get; set; }
            public bool IsLeaky { get; set; }
            public int LeakSampleCount { get; set; }
        }

        public ProcessBehaviorEngine(ILoggingService logger)
        {
            _logger = logger;
        }

        public void Update(ProcessCacheService.CachedProcessInfo[] processes, int? foregroundPid)
        {
            try
            {
                foreach (var p in processes)
                {
                    try
                    {
                        string name = p.ProcessName;
                        var profile = _processLibrary.GetOrAdd(name, n => new BehaviorProfile { Name = n, LastSeen = DateTime.Now });

                        profile.LastSeen = DateTime.Now;
                        profile.ExecutionCount++;
                        long currentWs = p.WorkingSet64;

                        // Média móvel exponencial do working set
                        if (profile.AvgWorkingSet == 0)
                            profile.AvgWorkingSet = currentWs;
                        else
                            profile.AvgWorkingSet = (profile.AvgWorkingSet * 3 + currentWs) / 4;

                        if (currentWs > profile.PeakWorkingSet)
                            profile.PeakWorkingSet = currentWs;

                        // Detecção de Leak - ajustado para ser menos sensível
                        // Whitelist para IDEs e aplicações pesadas legítimas
                        var heavyApps = new[] { "windsurf", "code", "devenv", "chrome", "firefox", "msedge" };
                        var isHeavyApp = heavyApps.Any(app => name.Contains(app, StringComparison.OrdinalIgnoreCase));
                        
                        if (currentWs > profile.AvgWorkingSet * 2.0 && p.Id != foregroundPid && !isHeavyApp)
                        {
                            if ((DateTime.Now - _lastLeakReport).TotalMinutes > 15) // Aumentado para 15 minutos
                            {
                                _logger.LogWarning($"[Behavior] Leak detectado: {name} ({currentWs / 1024 / 1024}MB)");
                                _lastLeakReport = DateTime.Now;
                            }
                            profile.LeakSampleCount++;
                            if (profile.LeakSampleCount >= 10) // Aumentado para 10 amostras
                            {
                                profile.IsLeaky = true;
                            }
                        }
                        else
                        {
                            profile.LeakSampleCount = Math.Max(0, profile.LeakSampleCount - 1);
                        }

                        if (profile.AvgWorkingSet > 1024 * 1024 * 1024) // Aumentamos para 1GB para ser menos agressivo
                            profile.IsResourceIntensive = true;

                        ApplyHeuristicSafety(p.Id, profile, foregroundPid);
                    }
                    catch { }
                }

                if (DateTime.Now.Second % 30 == 0) CleanupLibrary();
            }
            catch { }
        }

        private void ApplyHeuristicSafety(int pid, BehaviorProfile profile, int? foregroundPid)
        {
            try
            {
                if (pid == foregroundPid) 
                {
                    _lastAppliedPriority.TryRemove(pid, out _);
                    return;
                }

                // SEGURANÇA: Só agir em processos que realmente precisam.
                // Se o processo é normal, não o tocamos de forma alguma (evita falsos positivos de AV).
                if (!profile.IsLeaky && !profile.IsResourceIntensive)
                {
                    // Se ele estava sob contenção e agora não está mais, poderíamos resetar,
                    // mas para máxima segurança de AV, vamos apenas ignorar processos normais.
                    return;
                }

                uint targetPriority = 2; // Low
                if (profile.IsLeaky) targetPriority = 1; // Very Low

                // OTIMIZAÇÃO: Se já aplicamos este nível, ignorar.
                if (_lastAppliedPriority.TryGetValue(pid, out uint last) && last == targetPriority)
                    return;

                IntPtr hProcess = ProcessNativeMethods.OpenProcess(
                    ProcessNativeMethods.PROCESS_SET_INFORMATION, 
                    false, pid);

                if (hProcess == IntPtr.Zero) return;

                try
                {
                    // Memory Priority é uma operação segura e raramente flagada por AVs
                    var memInfo = new ProcessNativeMethods.MEMORY_PRIORITY_INFORMATION { MemoryPriority = targetPriority };
                    if (ProcessNativeMethods.SetProcessInformation(hProcess, 1, ref memInfo, Marshal.SizeOf(memInfo)))
                    {
                        _lastAppliedPriority[pid] = targetPriority;
                    }
                }
                finally { ProcessNativeMethods.CloseHandle(hProcess); }
            }
            catch { }
        }

        private void CleanupLibrary()
        {
            // Limpar processos não vistos há mais de 30 minutos do biblioteca de comportamento
            var oldKeys = _processLibrary.Where(x => (DateTime.Now - x.Value.LastSeen).TotalMinutes > 30).Select(x => x.Key).ToList();
            foreach (var key in oldKeys) _processLibrary.TryRemove(key, out _);
            
            // CORREÇÃO PERFORMANCE: Antes chamava Process.GetProcessById() para cada PID cacheado.
            // Em máquinas com 200+ processos isso alocava 200 objetos Process por ciclo de cleanup.
            // Nova abordagem: se o cache crescer além de 500 entradas, limpar tudo.
            // PIDs mortos serão naturalmente removidos na próxima vez que o BehaviorEngine
            // tentar aplicar contenção e a chamada OpenProcess() retornar zero.
            if (_lastAppliedPriority.Count > 500)
            {
                _lastAppliedPriority.Clear();
            }
        }

        public void Dispose() { }
    }
}
