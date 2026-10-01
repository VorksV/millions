using VoltrisOptimizer.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;

namespace VoltrisOptimizer.Services.Gamer.GamerModeManager
{
    /// <summary>
    /// Interface do serviço de otimização de processos
    /// </summary>
    public interface IProcessOptimizationService : IDisposable
    {
        void SetProcessPriority(int processId, ProcessPriorityLevel priority);
        void SetProcessAffinity(int processId, int[] cores);
        void SetProcessAffinityAllCores(int processId);
        void RestoreProcessPriority(int processId);
        void RestoreProcessAffinity(int processId);
        void CloseProcesses(IEnumerable<string> processNames);
        void LowerBackgroundPriorities();
        void RestoreBackgroundPriorities();
    }
    
    /// <summary>
    /// Serviço de otimização e priorização de processos
    /// </summary>
    public class ProcessOptimizationService : IProcessOptimizationService
    {
        private readonly ILoggingService _logger;
        
        // Backup de estados anteriores
        private readonly Dictionary<int, ProcessPriorityClass> _originalPriorities = new();
        private readonly Dictionary<int, IntPtr> _originalAffinities = new();
        private readonly Dictionary<int, ProcessPriorityClass> _loweredBackgroundPriorities = new();
        
        // Processos que nunca devem ser fechados
        private static readonly HashSet<string> ProtectedProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "explorer", "dwm", "csrss", "lsass", "services", "svchost",
            "System", "Registry", "smss", "wininit", "winlogon",
            "taskmgr", "taskhostw", "sihost", "fontdrvhost",
            "spoolsv", "searchindexer", "msiexec", "tiworker",
            "SecurityHealthService", "MsMpEng", "NisSrv",
            "audiodg", "conhost", "dllhost", "runtimebroker",
            "applicationframehost", "shellexperiencehost", "startmenuexperiencehost"
        };
        
        // Processos de background que podem ter prioridade reduzida
        private static readonly HashSet<string> BackgroundProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            // Browsers
            "chrome", "firefox", "msedge", "opera", "brave",
            
            // Communication
            "discord", "slack", "teams", "skype", "zoom", "telegram",
            
            // Media
            "spotify", "itunes", "vlc", "wmplayer",
            
            // Productivity
            "onedrive", "dropbox", "googledrivesync",
            
            // Development
            "code", "devenv", "idea64", "pycharm64",
            
            // Others
            "wallpaperengine", "razer synapse", "logitech g hub",
            "corsair", "icue", "armoury crate", "nahimic"
        };
        
        public ProcessOptimizationService(ILoggingService logger)
        {
            _logger.LogEntry(nameof(ProcessOptimizationService));
_logger = logger;
            _logger.LogExit(nameof(ProcessOptimizationService));
}
        
        /// <summary>
        /// Define a prioridade de um processo
        /// </summary>
        public void SetProcessPriority(int processId, ProcessPriorityLevel priority)
        {
            _logger.LogEntry(nameof(SetProcessPriority));
try
            {
                using var proc = Process.GetProcessById(processId);
                
                // Salvar prioridade original
                if (!_originalPriorities.ContainsKey(processId))
                {
                    _originalPriorities[processId] = proc.PriorityClass;
                }
                
                // Converter nível para PriorityClass
                var priorityClass = priority switch
                {
                    ProcessPriorityLevel.RealTime => ProcessPriorityClass.RealTime,
                    ProcessPriorityLevel.High => ProcessPriorityClass.High,
                    ProcessPriorityLevel.AboveNormal => ProcessPriorityClass.AboveNormal,
                    _ => ProcessPriorityClass.Normal
                };
                
                proc.PriorityClass = priorityClass;
                _logger.LogInfo($"[Process] Prioridade definida: PID {processId} -> {priority}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Process] Erro ao definir prioridade PID {processId}: {ex.Message}");
            }
            _logger.LogExit(nameof(SetProcessPriority));
}
        
        /// <summary>
        /// Define afinidade para cores específicos
        /// </summary>
        public void SetProcessAffinity(int processId, int[] cores)
        {
            _logger.LogEntry(nameof(SetProcessAffinity));
try
            {
                using var proc = Process.GetProcessById(processId);
                
                // Salvar afinidade original
                if (!_originalAffinities.ContainsKey(processId))
                {
                    _originalAffinities[processId] = proc.ProcessorAffinity;
                }
                
                // Calcular máscara de afinidade
                long affinityMask = 0;
                foreach (var core in cores)
                {
                    affinityMask |= (1L << core);
                }
                
                proc.ProcessorAffinity = new IntPtr(affinityMask);
                _logger.LogInfo($"[Process] Afinidade definida: PID {processId} -> Cores [{string.Join(",", cores)}]");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Process] Erro ao definir afinidade PID {processId}: {ex.Message}");
            }
            _logger.LogExit(nameof(SetProcessAffinity));
}
        
        /// <summary>
        /// Define afinidade para todos os cores
        /// </summary>
        public void SetProcessAffinityAllCores(int processId)
        {
            _logger.LogEntry(nameof(SetProcessAffinityAllCores));
try
            {
                using var proc = Process.GetProcessById(processId);
                
                // Salvar afinidade original
                if (!_originalAffinities.ContainsKey(processId))
                {
                    _originalAffinities[processId] = proc.ProcessorAffinity;
                }
                
                // Obter número de cores
                int coreCount = Environment.ProcessorCount;
                long affinityMask = (1L << coreCount) - 1;
                
                proc.ProcessorAffinity = new IntPtr(affinityMask);
                _logger.LogInfo($"[Process] Afinidade definida para todos os {coreCount} cores: PID {processId}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Process] Erro ao definir afinidade PID {processId}: {ex.Message}");
            }
            _logger.LogExit(nameof(SetProcessAffinityAllCores));
}
        
        /// <summary>
        /// Restaura prioridade original
        /// </summary>
        public void RestoreProcessPriority(int processId)
        {
            _logger.LogEntry(nameof(RestoreProcessPriority));
try
            {
                if (_originalPriorities.TryGetValue(processId, out var originalPriority))
                {
                    try
                    {
                        using var proc = Process.GetProcessById(processId);
                        proc.PriorityClass = originalPriority;
                        _logger.LogInfo($"[Process] Prioridade restaurada: PID {processId}");
                    }
                    catch { }
                    
                    _originalPriorities.Remove(processId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Process] Erro ao restaurar prioridade PID {processId}: {ex.Message}");
            }
            _logger.LogExit(nameof(RestoreProcessPriority));
}
        
        /// <summary>
        /// Restaura afinidade original
        /// </summary>
        public void RestoreProcessAffinity(int processId)
        {
            _logger.LogEntry(nameof(RestoreProcessAffinity));
try
            {
                if (_originalAffinities.TryGetValue(processId, out var originalAffinity))
                {
                    try
                    {
                        using var proc = Process.GetProcessById(processId);
                        proc.ProcessorAffinity = originalAffinity;
                        _logger.LogInfo($"[Process] Afinidade restaurada: PID {processId}");
                    }
                    catch { }
                    
                    _originalAffinities.Remove(processId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Process] Erro ao restaurar afinidade PID {processId}: {ex.Message}");
            }
            _logger.LogExit(nameof(RestoreProcessAffinity));
}
        
        /// <summary>
        /// Fecha processos especificados
        /// </summary>
        public void CloseProcesses(IEnumerable<string> processNames)
        {
            _logger.LogEntry(nameof(CloseProcesses));
foreach (var name in processNames)
            {
                if (ProtectedProcesses.Contains(name))
                {
                    _logger.LogWarning($"[Process] Processo protegido, ignorando: {name}");
                    continue;
                }
                
                try
                {
                    var processes = Process.GetProcessesByName(name);
                    
                    foreach (var proc in processes)
                    {
                        try
                        {
                            // Tentar fechar graciosamente primeiro
                            if (proc.CloseMainWindow())
                            {
                                if (!proc.WaitForExit(3000))
                                {
                                    proc.Kill();
                                }
                            }
                            else
                            {
                                proc.Kill();
                            }
                            
                            _logger.LogInfo($"[Process] Processo fechado: {name}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"[Process] Erro ao fechar {name}: {ex.Message}");
                        }
                        finally
                        {
                            proc.Dispose();
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[Process] Erro ao buscar processo {name}: {ex.Message}");
                }
            }
            _logger.LogExit(nameof(CloseProcesses));
}
        
        /// <summary>
        /// Reduz prioridade de processos de background
        /// </summary>
        public void LowerBackgroundPriorities()
        {
            _logger.LogEntry(nameof(LowerBackgroundPriorities));
try
            {
                var processes = Process.GetProcesses();
                
                foreach (var proc in processes)
                {
                    try
                    {
                        var name = proc.ProcessName;
                        
                        if (BackgroundProcesses.Contains(name))
                        {
                            // Salvar prioridade original
                            if (!_loweredBackgroundPriorities.ContainsKey(proc.Id))
                            {
                                _loweredBackgroundPriorities[proc.Id] = proc.PriorityClass;
                            }
                            
                            // Reduzir prioridade
                            if (proc.PriorityClass > ProcessPriorityClass.BelowNormal)
                            {
                                proc.PriorityClass = ProcessPriorityClass.BelowNormal;
                            }
                        }
                    }
                    catch { }
                    finally
                    {
                        proc.Dispose();
                    }
                }
                
                _logger.LogInfo($"[Process] Prioridade reduzida para {_loweredBackgroundPriorities.Count} processos de background");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Process] Erro ao reduzir prioridades: {ex.Message}");
            }
            _logger.LogExit(nameof(LowerBackgroundPriorities));
}
        
        /// <summary>
        /// Restaura prioridades de processos de background
        /// </summary>
        public void RestoreBackgroundPriorities()
        {
            _logger.LogEntry(nameof(RestoreBackgroundPriorities));
try
            {
                foreach (var kvp in _loweredBackgroundPriorities.ToList())
                {
                    try
                    {
                        using var proc = Process.GetProcessById(kvp.Key);
                        proc.PriorityClass = kvp.Value;
                    }
                    catch { }
                }
                
                var count = _loweredBackgroundPriorities.Count;
                _loweredBackgroundPriorities.Clear();
                
                _logger.LogInfo($"[Process] Prioridades restauradas para {count} processos de background");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Process] Erro ao restaurar prioridades: {ex.Message}");
            }
            _logger.LogExit(nameof(RestoreBackgroundPriorities));
}
        
        /// <summary>
        /// Obtém lista de cores físicos (excluindo hyperthreading se desejado)
        /// </summary>
        public int[] GetPhysicalCores()
        {
            _logger.LogEntry(nameof(GetPhysicalCores));
try
            {
                int physicalCores = 0;
                
                using var searcher = new ManagementObjectSearcher("SELECT NumberOfCores FROM Win32_Processor");
                foreach (ManagementObject obj in searcher.Get())
                {
                    using var __dispose_obj = obj;
                    physicalCores += Convert.ToInt32(obj["NumberOfCores"]);
                }
                return Enumerable.Range(0, physicalCores).ToArray();
            }
            catch
            {
                // Fallback: assumir metade dos cores lógicos são físicos
return Enumerable.Range(0, Environment.ProcessorCount / 2).ToArray();
            }
            _logger.LogExit(nameof(GetPhysicalCores));
}
        
        public void Dispose()
        {
            _logger.LogEntry(nameof(Dispose));
// Restaurar tudo ao finalizar
            foreach (var pid in _originalPriorities.Keys.ToList())
            {
                RestoreProcessPriority(pid);
            }
            
            foreach (var pid in _originalAffinities.Keys.ToList())
            {
RestoreProcessAffinity(pid);
}
            
            RestoreBackgroundPriorities();
            _logger.LogExit(nameof(Dispose));
}
    }
}

